using System;
using System.Collections.Generic;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>
/// Enemies, end to end, from core's side: it decides they exist, learns where they are, works out
/// what each of them can perceive, ticks their behaviour, and tells the body about the census
/// through events. The run owns one; nothing else may spawn or retire an enemy. See AR §3, §4.1–4.3
/// and §9.
/// </summary>
/// <remarks>
/// <para>
/// <b>The registry is the state, this is the verbs.</b> <see cref="EnemyRegistry"/> knows who is
/// out there and nothing else — no clock, no events, no catalog. This adds the three things that
/// make it a system: content resolution (an id becomes an <see cref="EnemySpec"/>), announcement
/// (a spawn and a despawn are facts the views need), and the per-frame pass that turns a snapshot
/// into perception.
/// </para>
/// <para>
/// <b>Ingest is two passes, and the split is by writer rather than by field.</b> The first pass
/// copies down what the snapshot said — position, velocity, path direction, line of sight — for
/// every agent it names. The second derives what follows from it for every *living* agent: where
/// the player is from here, how far, which way, and how many allies are close. The first pass is
/// keyed by the snapshot and so misses agents no view reported this frame; the second is keyed by
/// the registry and so covers every one of them. Anything derived from a position must therefore
/// be in the second pass, or an enemy whose view lagged a frame would carry a distance computed
/// from someone else's position.
/// </para>
/// <para>
/// <b>Nothing on the per-frame paths allocates</b> (AR §4.3): a span over the registry's array, a
/// dictionary lookup per snapshot entry, and struct arithmetic. The O(n²) ally count is the one
/// deliberate cost — at GD §11's 64-enemy cap it is ~4 000 squared-distance comparisons a frame,
/// which is cheaper than the spatial hash that would replace it and has no bucket to get stale.
/// Revisit when a profile says so, not before.
/// </para>
/// <para>
/// <b>A spawn is the exception, and only on an agent's first life.</b> <see cref="Spawn"/> puts
/// three depth modifiers on the agent, which grows three <c>List&lt;Modifier&gt;</c> backing arrays
/// the first time — after that <c>Stat.RemoveAll()</c> clears without releasing capacity, so a
/// recycled agent's three modifiers go back into storage that already exists. That matters because
/// a respawn is decided from inside <see cref="Tick"/>: without it, GD §12's scaling would put a
/// small GC spike behind every refill.
/// </para>
/// </remarks>
public sealed class EnemySystem
{
    /// <summary>
    /// Seconds a corpse stays registered after it dies, before <see cref="Tick"/> retires it.
    /// </summary>
    /// <remarks>
    /// The gap between <see cref="EnemyDied"/> and <see cref="EnemyDespawned"/>, and the whole
    /// reason the two are separate events: it is how long M1-12's dissolve has to play before the
    /// id it is animating stops resolving. A number rather than a per-archetype field because it
    /// is a property of the death <em>effect</em>, which is shared — the day an archetype wants a
    /// longer one, it moves onto <see cref="EnemySpec"/> along with the effect that needs it.
    /// </remarks>
    public const float CorpseTime = 0.6f;

    /// <summary>
    /// Metres within which another enemy counts as an ally, for
    /// <see cref="EnemyBlackboard.AlliesNearby"/> — GD §8.1's clustering pressure.
    /// </summary>
    private const float AllyRadius = 6f;

    /// <summary>Compared squared, so the count never takes a square root.</summary>
    private const float AllyRadiusSquared = AllyRadius * AllyRadius;

    /// <summary>
    /// The shallowest depth there is. Stages are numbered from 1 (GD §8.2), so this is also what
    /// <see cref="Depth"/> starts at — every mode in V1 begins there, and a system asked to spawn
    /// before anything set a depth scales to the first stage rather than throwing.
    /// </summary>
    private const int MinDepth = 1;

    /// <summary>
    /// Below this distance the player and the enemy are the same point and there is no direction
    /// to give. Not zero: dividing by a distance of 1e-9 yields a unit vector made of noise, which
    /// is worse than admitting there is no answer.
    /// </summary>
    private const float MinDirectionDistance = 1e-4f;

    private readonly ContentCatalog _catalog;
    private readonly IDomainEvents _events;
    private readonly IRandom _random;

    /// <summary>
    /// What makes a spawned enemy as tough as its depth says. One per run, built by
    /// <c>RunSession.Start</c> from the mode's curves.
    /// </summary>
    private readonly DepthScaling _scaling;

    private int _depth = MinDepth;

    /// <summary>
    /// What replaces the dead, adopted from the plan by <see cref="SpawnAll"/>, or null for an
    /// arena that empties once and stays empty.
    /// </summary>
    private RespawnPolicy _respawn;

    /// <summary>
    /// When the most recent enemy died, in simulated run seconds.
    /// </summary>
    /// <remarks>
    /// Negative infinity until something dies, which is how "or no death yet" in the respawn rule
    /// is spelled without a second flag: <c>now − (−∞)</c> is infinite, so the delay is always
    /// already elapsed and an arena that opens under its quota fills immediately.
    /// </remarks>
    private float _lastDeathAt = float.NegativeInfinity;

    /// <summary>
    /// Where the player was as of the last <see cref="Ingest"/>.
    /// </summary>
    /// <remarks>
    /// Kept because <see cref="Tick"/> needs it for spawn safety and is handed a
    /// <see cref="PlayerCombat"/>, which owns health and targeting and deliberately not a position
    /// — a position is a fact the snapshot reports, not a thing combat decides. <c>Ingest</c> runs
    /// immediately before <c>Tick</c> in <c>RunSession</c>, so this is always this frame's.
    /// </remarks>
    private Vector3 _playerPosition;

    /// <param name="catalog">Where a spawn's <see cref="ContentId"/> becomes an <see cref="EnemySpec"/>.</param>
    /// <param name="events">Where <see cref="EnemySpawned"/> and <see cref="EnemyDespawned"/> go.</param>
    /// <param name="random">
    /// The run's generator. Only <see cref="IRandom.Spawn"/> is ever drawn from here — which
    /// enemies appear and where is exactly what that stream is for, and drawing from another would
    /// make a new mechanic elsewhere shift every seeded run's spawns (ADR-0011).
    /// </param>
    /// <param name="scaling">
    /// What makes every spawned enemy as tough, as dangerous and as fast as <see cref="Depth"/>
    /// says (GD §12.3). A constructor argument rather than something adopted from a plan, so that
    /// <see cref="Spawn"/> cannot run without one: an unscaled enemy is not a loud failure, it is
    /// a stage-20 Husk that dies in two hits.
    /// </param>
    /// <param name="capacity">
    /// The most enemies that may exist at once. Passed straight to the registry, which guards it,
    /// and it must match the snapshot's enemy capacity — an enemy core knows about but the
    /// snapshot cannot carry is one core is blind to the position of.
    /// </param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public EnemySystem(
        ContentCatalog catalog,
        IDomainEvents events,
        IRandom random,
        DepthScaling scaling,
        int capacity)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _scaling = scaling ?? throw new ArgumentNullException(nameof(scaling));

        Registry = new EnemyRegistry(capacity);
    }

    /// <summary>
    /// Who is out there. Read by targeting, by the AI and by anything resolving an id; written
    /// only through this system's <see cref="Spawn"/> and <see cref="Despawn"/>, so no caller can
    /// register an enemy that was never announced.
    /// </summary>
    public EnemyRegistry Registry { get; }

    /// <summary>
    /// The depth everything spawned from now on is scaled to (GD §12.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set by <c>RunSession.Start</c> from <c>RunConfig.StageIndex</c>, and by M2-10 at each stage
    /// boundary. Held here rather than passed to <see cref="Spawn"/> because it is a property of
    /// the arena the enemies are appearing in: a respawn decided inside <see cref="Tick"/> has no
    /// caller to ask, and a stage number threaded through every spawn site is a stage number one
    /// of them will forget.
    /// </para>
    /// <para>
    /// Not the same number as <c>RunState.StageIndex</c> and deliberately a copy: that one is what
    /// the run reports and saves, this one is what a spawn is priced against. They agree because
    /// the two places that move a stage move both.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is below 1. Stages are numbered from 1, and a zero here would make the next spawn
    /// throw from inside a curve — one frame and one call stack away from the assignment that was
    /// actually wrong.
    /// </exception>
    public int Depth
    {
        get => _depth;

        set
        {
            if (value < MinDepth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"Depth must be at least {MinDepth}. Stages are numbered from 1 (GD §8.2).");
            }

            _depth = value;
        }
    }

    /// <summary>
    /// Brings one enemy of <paramref name="specId"/> into being at <paramref name="position"/>,
    /// scales it to <see cref="Depth"/>, and announces it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The archetype is resolved before the agent is registered, so an unknown id leaves the
    /// registry untouched and publishes nothing — the same order, for the same reason, as
    /// <c>RunSession.Start</c> reading the catalog before it assigns any state. The event goes out
    /// *after* registration, so a handler that resolves the id it carries finds the agent.
    /// </para>
    /// <para>
    /// <b>The scaling happens here, which is why there is nowhere to forget it.</b> Every enemy in
    /// the game comes into being through this method — a plan, a respawn, M2-05's director — so
    /// depth is applied once, in the one place, rather than by each caller remembering to. It runs
    /// after registration and before the announcement, so a handler reading the agent's hit points
    /// from inside <see cref="EnemySpawned"/> sees the scaled ones: a health bar built on the spawn
    /// event would otherwise be sized to the unscaled maximum for its first frame.
    /// </para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException">
    /// The catalog holds no enemy with that id. Let through rather than rewrapped: it is the
    /// catalog's answer and it already names the id.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The registry is full at its capacity. The caller chooses which enemies matter (GD §11).
    /// </exception>
    public EnemyAgent Spawn(ContentId specId, Vector3 position)
    {
        EnemySpec spec = _catalog.Enemy(specId);

        EnemyAgent agent = Registry.Spawn(spec, position);

        // The registry has just wiped and re-based all three of this agent's stats
        // (EnemyAgent.Initialise, ledger row 2), so this is applying depth to an archetype's
        // authored numbers and never on top of a previous life's.
        _scaling.Apply(agent, _depth);

        _events.Publish(new EnemySpawned(agent.Id, spec.Id, position));

        return agent;
    }

    /// <summary>
    /// Spawns every entry of <paramref name="plan"/>, in plan order, and adopts its respawn policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Order matters and is the plan's: ids are handed out in spawn order, and spawn order is what
    /// <c>EnemyRegistry.Alive</c> preserves and <c>TargetScorer</c>'s tie-break reads. A plan
    /// spawned in some other order would make the same seed play differently.
    /// </para>
    /// <para>
    /// The policy is adopted here rather than passed to <see cref="Tick"/> every frame, because it
    /// is authored data that does not change within a run — sixty copies a second of a reference
    /// the run already owns would be a parameter that could only ever be the same value, and one
    /// more thing <c>RunSession</c> would have to remember to forward.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is null.</exception>
    public void SpawnAll(SpawnPlan plan)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        _respawn = plan.Respawn;

        IReadOnlyList<SpawnPlan.Entry> initial = plan.Initial;

        for (int i = 0; i < initial.Count; i++)
        {
            SpawnPlan.Entry entry = initial[i];

            Spawn(entry.SpecId, entry.Position);
        }
    }

    /// <summary>Retires the enemy with <paramref name="id"/> and announces that it is gone.</summary>
    /// <remarks>
    /// The event goes out *after* the agent leaves the registry, which is the opposite of
    /// <see cref="Spawn"/>'s order and deliberate: a handler that looks the id up inside this
    /// event should find nothing, because nothing is what is there. Both orders say the same
    /// thing — during the event, the registry already agrees with the news.
    /// </remarks>
    /// <returns>
    /// <see langword="false"/> for an id that is not registered, with nothing published. Not an
    /// error: M1-11's death flow may despawn the same corpse twice without having to remember, and
    /// a wave cleanup can despawn unconditionally.
    /// </returns>
    public bool Despawn(int id)
    {
        if (!Registry.Despawn(id))
        {
            return false;
        }

        _events.Publish(new EnemyDespawned(id));

        return true;
    }

    /// <summary>
    /// Applies <paramref name="amount"/> to the enemy with <paramref name="enemyId"/> at time
    /// <paramref name="now"/> and announces what happened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single door damage reaches an enemy through, so that "an enemy was hurt" and "an enemy
    /// died" each have exactly one publisher. <c>PlayerCombat.ResolveConeHits</c> calls it for the
    /// Censer's arc (M1-11); M1-15's Charge and M5-01's projectiles will for theirs. The mirror of
    /// <c>PlayerCombat.ApplyDamage</c>, and deliberately the same shape: <see cref="Health"/> stays
    /// event-free and its owner decides what the result means.
    /// </para>
    /// <para>
    /// <b>A call that did nothing says nothing.</b> An unknown id and a corpse both report
    /// <see cref="DamageResult.None"/> without publishing — a second death for something already
    /// dead would make the kill arrive twice, and an <see cref="EnemyDamaged"/> of zero would flash
    /// a hit that never landed. The same silence covers a non-positive or NaN
    /// <paramref name="amount"/>, which <see cref="Health"/> refuses at the door; a
    /// <see cref="DamageResult.Blocked"/> result <em>is</em> published, because something arrived
    /// and was turned away, which is the one thing M7-01's Warden needs the game to say out loud.
    /// </para>
    /// <para>
    /// The corpse is left registered. <see cref="Tick"/> retires it <see cref="CorpseTime"/>
    /// seconds later, which is what gives the view its dissolve and what makes the id in
    /// <see cref="EnemyDied"/> still resolvable while the event is being handled. As of M2-08 it is
    /// also what keeps the behaviour pass's span valid across a detonation — see <see cref="Tick"/>.
    /// </para>
    /// <para>
    /// <b>It takes a <see cref="PlayerCombat"/> as of M2-08, and that is the honest signature.</b>
    /// Hurting an enemy can now hurt the player: an archetype carrying an
    /// <see cref="ExplosionSpec"/> goes off where it died, so the one door damage reaches an enemy
    /// through has to know who the player is. Both production callers are inside
    /// <c>PlayerCombat</c> and pass <c>this</c>; a Bloater's own fuse passes the player off its tick
    /// context.
    /// </para>
    /// </remarks>
    /// <param name="enemyId">Who to hurt. An id that is not registered is not an error.</param>
    /// <param name="amount">Damage to apply. Zero, negative and NaN all do nothing.</param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <param name="player">
    /// Who a resulting blast would catch. Required rather than optional, so that the compiler
    /// enumerates every call site the day an archetype starts exploding rather than letting one
    /// silently opt out of it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
    /// <returns>What <see cref="Health"/> did, unchanged, for a caller with its own conclusions to draw.</returns>
    public DamageResult ApplyDamage(int enemyId, float amount, float now, PlayerCombat player)
    {
        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        // Registered *and* breathing. TryGet finds a corpse on purpose (that is what lets a death
        // event name a resolvable id), so aliveness is the second half of the question here.
        if (!Registry.TryGet(enemyId, out EnemyAgent agent) || !agent.IsAlive)
        {
            return DamageResult.None;
        }

        DamageResult result = agent.Health.ApplyDamage(amount, now);

        // Spelled as "nothing arrived" rather than as a comparison against None, so a future
        // DamageResult field cannot quietly change what counts as silence. The same spelling, for
        // the same reason, as PlayerCombat.ApplyDamage.
        if (!result.Blocked && !(result.Applied > 0f))
        {
            return result;
        }

        _events.Publish(new EnemyDamaged(enemyId, result.Applied, agent.Health.Fraction, result.Killed));

        // Exactly once per life without a flag to remember it: Killed is true only on the call that
        // took HP to zero, and every later call finds a corpse and returns None above.
        if (result.Killed)
        {
            // Stamped before the announcement, so the corpse is already on the clock by the time
            // anything handles the death — the same order as Spawn's register-then-announce.
            agent.DiedAt = now;

            // The respawn rule's other clock, and deliberately the same stamp: the pause the player
            // reads as "I cleared that" is measured from the most recent death, so a wave killed
            // one at a time refills steadily while a wipe refills once, two seconds after the last
            // one falls.
            _lastDeathAt = now;

            _events.Publish(new EnemyDied(enemyId, agent.Spec.Id, agent.Position));

            // After the death and not instead of it (M2-08 rule 1). The trigger is the spec rather
            // than the kind, which is what makes "explodes on death" true however it died — shot at
            // range, cut down mid-fuse, killed by a Charge, or killed by its own fuse — and true for
            // anything that ever gets an explosion block, M7-02's Volatile affix included, with no
            // switch on an archetype to keep in step.
            if (agent.Spec.Explosion != null)
            {
                Explode(agent, now, player);
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves <paramref name="agent"/>'s explosion: everything inside
    /// <see cref="ExplosionSpec.Radius"/> of where it died takes its
    /// <see cref="EnemyAgent.ContactDamage"/>, and <see cref="EnemyExploded"/> is published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>"Everything" is the player and nothing else, and that is a ruling rather than an
    /// omission</b> (M2-08 rule 4). Damaging other enemies is the better <em>moment</em> — a Bloater
    /// killed in a crowd chaining through it is the best thing the archetype could produce — but it
    /// would re-enter <see cref="ApplyDamage"/> while <see cref="ApplyDamage"/> is still running,
    /// which can kill another Bloater, which explodes, and every one of those touches a registry
    /// that <see cref="Tick"/> may be walking. That wants a work queue and a recursion guard, and it
    /// is a bigger change than the archetype. Player-only keeps the whole blast a single leaf call:
    /// one XZ distance test, one <c>PlayerCombat.ApplyDamage</c>, no registry mutation, no
    /// re-entrancy. M7-02 is where a chain can be afforded.
    /// </para>
    /// <para>
    /// <b>XZ, from where it died</b> (AR §18.4) — the height between two capsule centres is a
    /// rendering detail, and counting it would shrink every blast by however tall the bodies are.
    /// The position is the agent's as of this frame's <see cref="Ingest"/>, which is the same one
    /// <see cref="EnemyDied"/> just carried, so the ring a view draws and the circle that hurt are
    /// the same circle.
    /// </para>
    /// <para>
    /// <b>The damage is the agent's stat, not a number on the explosion block</b> — so GD §12.3's
    /// d(n) is already in it and M7-02's affixes reach a blast without a second mechanism. It is the
    /// same reason a thrown shot carries <c>ContactDamage</c> (M2-07b).
    /// </para>
    /// <para>
    /// <b>The event is published whether or not it caught anybody.</b> A view has to draw the flash
    /// either way — <see cref="ProjectileImpacted"/>'s reasoning, and <see cref="EnemyDespawned"/>'s.
    /// </para>
    /// </remarks>
    private void Explode(EnemyAgent agent, float now, PlayerCombat player)
    {
        float radius = agent.Spec.Explosion.Radius;

        Vector3 centre = agent.Position;

        float dx = _playerPosition.X - centre.X;
        float dz = _playerPosition.Z - centre.Z;

        // Compared squared, so a blast never takes a square root. Guarded at the door it was
        // authored through — ExplosionSpec refuses a non-positive or infinite radius — which is what
        // makes squaring it here safe (AR §18.3).
        bool caught = (dx * dx) + (dz * dz) <= radius * radius;

        if (caught)
        {
            player.ApplyDamage(agent.ContactDamage.Value, now);
        }

        _events.Publish(new EnemyExploded(agent.Id, agent.Spec.Id, centre, radius, caught));
    }

    /// <summary>
    /// One frame of senses: copies the snapshot's positions onto the agents it names, then fills
    /// every living agent's perception.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An id in the snapshot that the registry does not know is ignored rather than refused. A
    /// view is allowed to lag a frame behind a despawn — it has a dissolve to play — and the
    /// alternative would be core throwing every time an enemy died.
    /// </para>
    /// <para>
    /// An agent that the snapshot does *not* name keeps its last position. That is also a lagging
    /// view, from the other side: the enemy was spawned this frame and its view has not reported
    /// yet, and the spawn position it was given is the best answer available.
    /// </para>
    /// </remarks>
    public void Ingest(WorldSnapshot snapshot)
    {
        // Stops at EnemyCount, never at Enemies.Length: Clear() leaves the array's contents alone,
        // so everything past the count is last frame's enemies (AR §4.2).
        for (int i = 0; i < snapshot.EnemyCount; i++)
        {
            ref EnemySense sense = ref snapshot.Enemies[i];

            if (!Registry.TryGet(sense.Id, out EnemyAgent agent))
            {
                continue;
            }

            agent.Position = sense.Position;
            agent.Velocity = sense.Velocity;

            // The two senses core cannot derive: pathfinding is Unity's (AR §3) and so is
            // visibility. Copied as given, zero included — a behaviour reads a zero path direction
            // as "no path" and falls back to the straight line.
            agent.Blackboard.PathDirectionToPlayer = sense.PathDirectionToPlayer;
            agent.Blackboard.HasLineOfSight = sense.HasLineOfSight;
        }

        // Kept for Tick's spawn-safety check, which runs after this in the same frame — see the
        // field. Written unconditionally, including when the snapshot names no enemies at all,
        // because an empty arena is precisely when a respawn is about to be decided.
        _playerPosition = snapshot.PlayerPosition;

        Perceive(snapshot.PlayerPosition);
    }

    /// <summary>
    /// Advances every living enemy's behaviour by <c>ctx.Dt</c> seconds.
    /// </summary>
    /// <param name="ctx">
    /// Everything a behaviour may reach this tick — the clock, the player, the intent sink, the
    /// event hub and the sky. Built once per tick by <c>RunSession</c> and passed straight through
    /// (M2-07b rule 2), which is why this method no longer takes four arguments: the run's player is
    /// handed down rather than held as a field, because it belongs to the run and is rebuilt with
    /// it, and one struct is what stops that list growing by one every time an archetype lands.
    /// </param>
    /// <remarks>
    /// <para>
    /// This dispatches and nothing else: <c>Static</c> does nothing by definition — that is what the
    /// kind means, and it is why a dummy holds still — while <c>Chaser</c> (M1-18),
    /// <c>Spitter</c> (M2-07b) and <c>Bloater</c> (M2-08) each tick the behaviour the agent was
    /// built with. The three share a line rather than repeating one, which is the seam earning its
    /// keep: what differs between a Husk, a Spitter and a Bloater is entirely on the other side of
    /// <c>IEnemyBehaviour</c>. The switch is
    /// here because this is the one site that knows the full set of behaviours, which is why
    /// <c>EnemyBehaviourKind</c> is deliberately unvalidated where it is authored (M1-05) — a kind
    /// added without teaching this method about it fails loudly here instead of standing motionless
    /// in the arena with nothing in the log.
    /// </para>
    /// <para>
    /// The corpse sweep runs first, so the behaviour pass walks a registry nothing is about to
    /// remove from. Order between the two is otherwise free — a corpse does not act either way —
    /// and this way there is only one span to reason about.
    /// </para>
    /// <para>
    /// <b>The span is re-read after the pass rather than hoisted across it, and that is not
    /// optional.</b> The loop is written against <c>Registry.Alive</c> taken once *after* the sweep,
    /// so that nothing added during the pass is walked by it.
    /// </para>
    /// <para>
    /// <b>M2-08 is the day this warning named, and the warning does not apply — which is worth
    /// keeping rather than deleting.</b> It used to read: the day a behaviour gains the power to
    /// retire an agent (a Bloater exploding) it has to walk backwards the way
    /// <see cref="SweepCorpses"/> does. A Bloater now kills itself from inside this very loop, and
    /// the span survives it because <see cref="ApplyDamage"/> leaves the corpse <em>registered</em>:
    /// a death marks an agent not-alive and stamps it, and <see cref="SweepCorpses"/> retires it
    /// <see cref="CorpseTime"/> seconds later at the top of a subsequent tick. Nothing is removed
    /// underneath the walk, so no index shifts and no slot is nulled. <b>The backwards walk is still
    /// owed by the next behaviour that calls <see cref="Despawn"/> directly</b>, which would compact
    /// the registry in place — that is the case this paragraph is kept for.
    /// </para>
    /// </remarks>
    public void Tick(in EnemyTickContext ctx)
    {
        SweepCorpses(ctx.Now);

        ReadOnlySpan<EnemyAgent> agents = Registry.Alive;

        for (int i = 0; i < agents.Length; i++)
        {
            EnemyAgent agent = agents[i];

            // Registered is not breathing (EnemyRegistry rule 4): a corpse sits in the span until
            // M1-11 has published its death, and a corpse does not act. This is also what makes a
            // Husk killed mid-windup cancel its strike — there is no path from here to the damage
            // frame for something that is not alive. A Bloater killed mid-fuse is the deliberate
            // counter-example and costs nothing here: it stops ticking exactly like the Husk, and it
            // still goes off, because the blast is a property of the corpse rather than of the fuse
            // and was already resolved by ApplyDamage on the way in (M2-08 rule 2).
            if (!agent.IsAlive)
            {
                continue;
            }

            switch (agent.Spec.Behaviour)
            {
                case EnemyBehaviourKind.Static:
                    break;

                case EnemyBehaviourKind.Chaser:
                case EnemyBehaviourKind.Spitter:
                case EnemyBehaviourKind.Bloater:
                    agent.Behaviour.Tick(ctx);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unhandled enemy behaviour '{agent.Spec.Behaviour}' on '{agent.Spec.Id}'.");
            }
        }

        // Last, and after the span above is finished with: a spawn compacts nothing but it does
        // write into the registry's array and hand out an agent, and the loop has no business
        // seeing an enemy that came into being during its own pass. It is also why the span is not
        // hoisted across this line.
        if (_respawn != null)
        {
            ApplyRespawn(_respawn, _playerPosition, ctx.Now, _random.Spawn);
        }
    }

    /// <summary>
    /// Puts one enemy back on the floor, if the arena is short of
    /// <see cref="RespawnPolicy.KeepAlive"/> and the quiet after the last death has elapsed.
    /// </summary>
    /// <param name="policy">What to spawn, where, and under what conditions.</param>
    /// <param name="playerPosition">
    /// Who to stay away from. Passed rather than read from the last <see cref="Ingest"/>, so a test
    /// can state the geometry it is asserting about instead of building a snapshot to imply it.
    /// </param>
    /// <param name="now">Simulated run time, in seconds — the same clock deaths are stamped with.</param>
    /// <param name="spawnStream">
    /// Where the position is drawn from. Always <see cref="IRandom.Spawn"/> in a run; a parameter
    /// rather than a field read so the one draw this method makes is visible in its signature.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>One per tick, deliberately.</b> A wipe refills over a few frames rather than instantly,
    /// which is both kinder to the frame that has just resolved eight deaths and better to look at
    /// — twelve bodies appearing together reads as a glitch, a trickle reads as the arena breathing.
    /// The cost is that a full refill takes twelve frames, a fifth of a second, which nobody can see.
    /// </para>
    /// <para>
    /// <b>The census counts the living, not the registered.</b> A corpse sits in the registry for
    /// <see cref="CorpseTime"/> after it dies (rule 4 of <see cref="EnemyRegistry"/>), and counting
    /// it would make the arena wait six-tenths of a second per kill before it even noticed it was
    /// short — on top of the delay the policy already asks for.
    /// </para>
    /// <para>
    /// Allocates nothing and takes no square root: a span over the registry, one draw, and at most
    /// <c>Positions.Count</c> squared-distance comparisons on the frame something spawns.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="policy"/> or <paramref name="spawnStream"/> is null.
    /// </exception>
    /// <returns>
    /// The agent that was spawned, or null when nothing was — under quota is not the only reason,
    /// and the caller in <see cref="Tick"/> has nothing to do about any of them.
    /// </returns>
    public EnemyAgent ApplyRespawn(
        RespawnPolicy policy,
        Vector3 playerPosition,
        float now,
        IRandomStream spawnStream)
    {
        if (policy is null)
        {
            throw new ArgumentNullException(nameof(policy));
        }

        if (spawnStream is null)
        {
            throw new ArgumentNullException(nameof(spawnStream));
        }

        if (LivingCount() >= policy.KeepAlive)
        {
            return null;
        }

        if (now - _lastDeathAt < policy.RespawnDelay)
        {
            return null;
        }

        // The registry throws when it is full, and being full is a legitimate state rather than a
        // bug here — corpses hold slots, and an arena whose KeepAlive is near its capacity can
        // reach it during a flurry of deaths. Skipping this tick costs one frame; throwing would
        // end the run.
        if (Registry.AliveCount >= Registry.Capacity)
        {
            return null;
        }

        IReadOnlyList<Vector3> positions = policy.Positions;

        // One draw whatever happens next, including when every position is refused. A stream whose
        // consumption depended on the geometry would replay differently the moment the player stood
        // somewhere else, which is the whole thing a seed is supposed to survive.
        int index = spawnStream.NextInt(0, positions.Count);

        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 candidate = positions[(index + i) % positions.Count];

            if (!policy.IsSafe(candidate, playerPosition))
            {
                continue;
            }

            return Spawn(policy.SpecId, candidate);
        }

        // Every position is inside the player's clearance: the player is standing in the middle of
        // the arena's spawn ring. Nothing appears this tick and nothing is said about it — they
        // will move, and GD §12.4's rule is that a spawn on top of the player is worse than a
        // pause.
        return null;
    }

    /// <summary>
    /// Empties the registry without announcing anything.
    /// </summary>
    /// <remarks>
    /// For the end of a run only, where <c>RunScope</c> is going away and with it every subscriber
    /// a despawn event could reach — publishing 60 of them into a scope mid-teardown would be noise
    /// at best. Anything that retires one enemy during a run calls <see cref="Despawn"/>, which
    /// does announce it.
    /// </remarks>
    public void Clear()
    {
        Registry.Clear();

        // The respawn rule's state goes with the census it was about. A policy left behind would
        // refill an arena that has been torn down, and a death stamp left behind would make the
        // next run's first quota check measure against a run that is over.
        _respawn = null;
        _lastDeathAt = float.NegativeInfinity;
        _playerPosition = Vector3.Zero;

        // Depth is deliberately *not* reset. It belongs to whoever advances the stage, not to the
        // census: M2-10 clears an arena at a stage boundary and the next stage's depth is the
        // point of the transition, so zeroing it here would mean the two had to happen in an order
        // this method could not state.
    }

    /// <summary>How many registered agents are breathing.</summary>
    /// <remarks>
    /// <para>
    /// Walked rather than counted incrementally, because the registry deliberately does not track
    /// it: <c>AliveCount</c> is how many are <em>registered</em>, corpses included, and a second
    /// counter kept in step with every death and every sweep is a counter that can drift. At GD
    /// §11's 64-enemy cap this is 64 boolean reads on the one frame a respawn is considered.
    /// </para>
    /// <para>
    /// Public since M2-05, for the one caller that has to ask it every tick rather than every
    /// respawn: <c>SpawnDirector</c> holds the arena to the stage's concurrency and the cap is
    /// about the <em>living</em>, since a corpse is neither a threat nor something the player can
    /// see is finished with. It stays a method rather than becoming a property, so the walk is
    /// visible at the call site.
    /// </para>
    /// </remarks>
    public int LivingCount()
    {
        ReadOnlySpan<EnemyAgent> agents = Registry.Alive;

        int count = 0;

        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i].IsAlive)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Retires every corpse that has been dead for at least <see cref="CorpseTime"/> seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Walked backwards, and that is the whole of why this is its own method.
    /// <see cref="Despawn"/> compacts the registry in place, so a forward loop over a span taken
    /// once would read a nulled slot the moment anything was removed. Removing index <c>i</c>
    /// shifts only what came after it, so every index below is still the agent it was — which
    /// makes the backwards walk correct without copying anything out first.
    /// </para>
    /// <para>
    /// <c>Registry.Alive</c> is re-read each step rather than hoisted, for the same reason: the
    /// span carries the count it was taken with. It is a struct over an array the registry already
    /// owns, so re-taking it allocates nothing.
    /// </para>
    /// </remarks>
    private void SweepCorpses(float now)
    {
        for (int i = Registry.AliveCount - 1; i >= 0; i--)
        {
            EnemyAgent agent = Registry.Alive[i];

            if (agent.IsAlive)
            {
                continue;
            }

            // `< CorpseTime` rather than a negated `>=`, so a corpse whose stamp is negative
            // infinity — one that died without going through ApplyDamage — fails this test and is
            // retired now. See EnemyAgent.DiedAt.
            if (now - agent.DiedAt < CorpseTime)
            {
                continue;
            }

            Despawn(agent.Id);
        }
    }

    /// <summary>
    /// Fills every living agent's derived perception from the positions just ingested.
    /// </summary>
    private void Perceive(Vector3 playerPosition)
    {
        ReadOnlySpan<EnemyAgent> agents = Registry.Alive;

        for (int i = 0; i < agents.Length; i++)
        {
            EnemyAgent agent = agents[i];

            if (!agent.IsAlive)
            {
                continue;
            }

            EnemyBlackboard blackboard = agent.Blackboard;
            Vector3 position = agent.Position;

            blackboard.SelfPosition = position;
            blackboard.SelfVelocity = agent.Velocity;
            blackboard.PlayerPosition = playerPosition;

            // XZ, not the full 3D separation: everything here happens on the ground plane, and the
            // Y difference between a player capsule's centre and an enemy's is a rendering detail
            // that would otherwise inflate every distance a strike or a spell is checked against.
            var toPlayer = new Vector2(playerPosition.X - position.X, playerPosition.Z - position.Z);
            float distance = toPlayer.Length();

            blackboard.DistanceToPlayer = distance;
            blackboard.DirectionToPlayer = distance < MinDirectionDistance
                ? Vector2.Zero
                : toPlayer / distance;

            blackboard.AlliesNearby = CountAlliesNearby(agents, i, position);
        }
    }

    /// <summary>
    /// How many other living enemies are within <see cref="AllyRadius"/> of
    /// <paramref name="position"/>.
    /// </summary>
    /// <remarks>
    /// Excludes the agent at <paramref name="index"/> by position in the span rather than by id,
    /// because the span is what is being walked and comparing ints beats resolving handles.
    /// </remarks>
    private static int CountAlliesNearby(ReadOnlySpan<EnemyAgent> agents, int index, Vector3 position)
    {
        int count = 0;

        for (int j = 0; j < agents.Length; j++)
        {
            if (j == index)
            {
                continue;
            }

            EnemyAgent other = agents[j];

            if (!other.IsAlive)
            {
                continue;
            }

            Vector3 otherPosition = other.Position;
            float dx = otherPosition.X - position.X;
            float dz = otherPosition.Z - position.Z;

            if ((dx * dx) + (dz * dz) <= AllyRadiusSquared)
            {
                count++;
            }
        }

        return count;
    }
}
