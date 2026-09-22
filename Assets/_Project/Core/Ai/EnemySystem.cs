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
/// One enemy's death, as much of it as anything downstream needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three fields, and each is here because something reads it</b> (M5-04b). <see cref="Position"/>
/// is what <c>RisePassive</c> stands a Wight up at — CH §3.2's corpse <em>is</em> the Wight — and it
/// is the whole reason this type exists at all, since <see cref="EnemySystem.PendingKills"/> is a
/// count and a count cannot say <em>where</em>. <see cref="WasBoss"/> is M5-04b rule 11's one rule.
/// <see cref="SpecId"/> is what a reader of a drained buffer has to have to know what died.
/// </para>
/// <para>
/// <b>A struct, and copied into a caller's span rather than handed out as a list</b> (AR §4.3):
/// <c>RunSession</c> drains once a tick into a buffer it owns, so a death costs one array write on
/// the kill path and one copy on the drain, and nothing on either allocates.
/// </para>
/// <para>
/// <b>It is not <see cref="Events.EnemyDied"/>, and the duplication is deliberate.</b> That one is an
/// announcement with an <em>id</em> on it, for views; this one is a fact with a <em>position</em> on
/// it, for core. Core does not subscribe to its own events (<c>RunSession</c>'s own remarks refuse
/// it), so a pull needs something to pull.
/// </para>
/// </remarks>
public readonly struct EnemyDeath
{
    /// <param name="specId">The archetype that died.</param>
    /// <param name="position">Where it stood when it died, in world metres.</param>
    /// <param name="wasBoss">Whether it was authored <see cref="EnemyBehaviourKind.Boss"/>.</param>
    /// <remarks>
    /// Unguarded, like every other struct core fills for itself: the one caller is
    /// <see cref="EnemySystem.ApplyDamage"/>, reading an agent it has already resolved.
    /// </remarks>
    public EnemyDeath(ContentId specId, Vector3 position, bool wasBoss)
    {
        SpecId = specId;
        Position = position;
        WasBoss = wasBoss;
    }

    /// <summary>What died, e.g. <c>enemy.husk</c>.</summary>
    public ContentId SpecId { get; }

    /// <summary>Where it stood as of the frame it died on.</summary>
    public Vector3 Position { get; }

    /// <summary>
    /// Whether it was a boss — the one thing CH §3.2's Rise refuses to raise (M5-04b rule 11).
    /// </summary>
    /// <remarks>
    /// Read off <see cref="EnemySpec.Behaviour"/> rather than off a flag somebody has to remember to
    /// set, because <c>SpawnBoss</c> is the only way a <see cref="EnemyBehaviourKind.Boss"/> body
    /// ever stands up and <c>EnemySystem.Tick</c> already refuses one that arrived another way.
    /// </remarks>
    public bool WasBoss { get; }
}

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

    /// <summary>
    /// Ids queued by <see cref="DespawnAtEndOfTick"/>, retired at the bottom of <see cref="Tick"/>.
    /// </summary>
    private readonly int[] _deferredDespawn;

    /// <summary>
    /// The deaths since the last <see cref="DrainDeaths"/>, oldest first in <c>[0, _deathCount)</c>.
    /// </summary>
    /// <remarks>
    /// <b>Sized at the registry's capacity and never grown</b> (M5-04b rule 2), which is the most
    /// bodies that can exist and therefore the most that can die between two drains while the drain
    /// runs every tick. A full buffer drops the <em>oldest</em> rather than growing — see
    /// <see cref="Bank"/>.
    /// </remarks>
    private readonly EnemyDeath[] _deaths;

    /// <summary>
    /// What a boss delegates its fighting to, or null while nothing does.
    /// </summary>
    /// <remarks>
    /// <b>A factory rather than an instance, and it lives here rather than at the call site
    /// because there is nowhere else it could</b> (M4-02). An <see cref="IEnemyBehaviour"/> holds
    /// the agent it drives — every implementer does, because <see cref="IEnemyBehaviour.Tick"/> is
    /// handed a context with no <em>self</em> on it — and the agent does not exist until
    /// <see cref="SpawnBoss"/> has spawned one. So a caller cannot build the <c>inner</c> it would
    /// pass, and the one thing it can hand over is the means of building it. The explicit
    /// <c>inner</c> parameter is untouched and still wins where a caller supplies one, which is
    /// what a fixture does.
    /// </remarks>
    private readonly Func<EnemyAgent, BossSpec, IEnemyBehaviour> _bossInner;

    /// <summary>
    /// GD §10's meter, read at every <see cref="Spawn"/> for the 25 row's 5 % (M6-04 rule 4), or
    /// null for a run without one.
    /// </summary>
    /// <remarks>
    /// <b>Held rather than asked at the crossing, and that is M5-06a rule 3's trade with a shorter
    /// bound.</b> There, a node buffed the <em>next</em> Wight and the lag was one lifespan; here a
    /// crossing speeds up the <em>next</em> body and the lag is the rest of the current wave. The
    /// alternative — walking <see cref="Registry"/> when the meter crosses 25 — needs the meter to
    /// hold the census, or a second call site in <c>RunSession</c> and a method here, for five per
    /// cent of one wave's move speed.
    /// </remarks>
    private readonly Veilrot _veilrot;

    private int _depth = MinDepth;

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

    private int _deferredCount;

    private int _deathCount;

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
    /// <param name="bossInner">
    /// What a boss delegates its fighting to, built once per boss from the agent and the spec —
    /// or null for a boss that only changes phase and summons, which is every boss until M4-02
    /// authors one. See <see cref="SpawnBoss"/> for why the factory lives here rather than at the
    /// call site.
    /// </param>
    /// <param name="veilrot">
    /// GD §10's meter, or null for a run without one — which is every fixture and no live run
    /// (M6-04 rule 4).
    /// </param>
    /// <remarks>
    /// <b><paramref name="veilrot"/> is optional where <paramref name="scaling"/> is required, and
    /// the asymmetry is what each of them costs to be missing.</b> An unscaled enemy is not a loud
    /// failure, it is a stage-20 Husk that dies in two hits; a meterless one is a Husk at exactly
    /// its authored speed, which is the honest answer for a run whose Veilrot is zero and for every
    /// fixture that has no meter to hand. It is a constructor argument rather than a settable
    /// property for the reason everything else here is one: a dependency that can be attached later
    /// is a dependency a spawn can happen without.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public EnemySystem(
        ContentCatalog catalog,
        IDomainEvents events,
        IRandom random,
        DepthScaling scaling,
        int capacity,
        Func<EnemyAgent, BossSpec, IEnemyBehaviour> bossInner = null,
        Veilrot veilrot = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _scaling = scaling ?? throw new ArgumentNullException(nameof(scaling));
        _bossInner = bossInner;
        _veilrot = veilrot;

        Registry = new EnemyRegistry(capacity);

        // Sized to the registry it drains into, because nothing can be queued that is not
        // registered and nothing is queued twice — see DespawnAtEndOfTick. Allocated here rather
        // than grown, so the one path that uses it never allocates.
        _deferredDespawn = new int[Registry.Capacity];

        // Sized the same way and for a nearby reason: nothing can die that is not registered, so
        // the registry's capacity is the most deaths one tick can produce. Allocated here rather
        // than grown, because Rise puts this write behind every kill (M5-04b rule 2).
        _deaths = new EnemyDeath[Registry.Capacity];
    }

    /// <summary>
    /// Who is out there. Read by targeting, by the AI and by anything resolving an id; written
    /// only through this system's <see cref="Spawn"/> and <see cref="Despawn"/>, so no caller can
    /// register an enemy that was never announced.
    /// </summary>
    public EnemyRegistry Registry { get; }

    /// <summary>
    /// What the deaths since the last <see cref="DrainXp"/> are worth, in experience.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Banked here rather than granted where it is earned</b>, because a death can be reported
    /// between ticks — <c>ReportConeHits</c> is a fact arriving mid-frame (ADR-0003) — and
    /// experience is decided on the tick, in the one place that knows whether the run is still
    /// running. <c>RunSession.Tick</c> drains this after the death check and before the director
    /// (AR §18.1); a kill reported between ticks is paid on the next one, at most a frame late,
    /// which is the lag every fact already has.
    /// </para>
    /// <para>
    /// A single <see langword="float"/> and not a queue: nothing downstream needs to know
    /// <em>which</em> enemy paid, only how much arrived, and a per-kill list would be an
    /// allocation on the one path that must not have one.
    /// </para>
    /// </remarks>
    public float PendingXp { get; private set; }

    /// <summary>
    /// How many deaths there have been since the last <see cref="DrainKills"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="PendingXp"/>'s shape, banked at the same line and for the same reason</b>
    /// (M3-12a rule 5): a death can be reported between ticks, and what it is worth is decided on
    /// the tick by the one thing that knows whether the run is still running.
    /// <c>RunSession.Tick</c> drains this beside the experience, after the death check, so a kill
    /// landing on the tick the player dies pays nothing.
    /// </para>
    /// <para>
    /// A count and not a list, for the reason the experience is a single float: what reads it —
    /// <c>PlayerCombat.HealForKills</c> — needs to know how many, never which, and a per-kill list
    /// would be an allocation on the one path that must not have one.
    /// </para>
    /// </remarks>
    public int PendingKills { get; private set; }

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

        // **GD §10.2's first row, applied here because here is where a body is born** (M6-04
        // rule 4). Immediately after the depth scaling, on top of it rather than instead of it: a
        // stage-20 Husk at 50 Veilrot is fast for both reasons. Sourced to the meter, which is what
        // makes it legible in a Stat.Describe beside the depth's own modifier.
        //
        // Skipped rather than added as a zero when the meter is below 25 or absent: a PercentMult of
        // 0 is a factor of 1 and changes nothing, but it would put a modifier on every enemy in
        // every run for the life of the project, and "does this body carry the Veilrot bonus" is a
        // question a test and a debug panel should be able to ask by looking.
        float veilrotSpeed = _veilrot?.EnemySpeedBonus ?? 0f;

        if (veilrotSpeed > 0f)
        {
            agent.MoveSpeed.Add(new Modifier(ModifierKind.PercentMult, veilrotSpeed, _veilrot));
        }

        // IsElite rides the event rather than being looked up, for the reason the field's own remarks
        // give: the one view that needs it holds a look book and not the catalog, and M7-02's Elites
        // are made at spawn rather than authored as an archetype.
        _events.Publish(new EnemySpawned(agent.Id, spec.Id, position, spec.IsElite));

        return agent;
    }

    /// <summary>
    /// Brings the boss <paramref name="bossId"/> names into being at <paramref name="position"/>:
    /// an ordinary agent wearing its authored body, with a <see cref="BossBehaviour"/> on it.
    /// </summary>
    /// <param name="bossId">The boss, e.g. <c>boss.warden</c>. Resolved against the catalog.</param>
    /// <param name="position">Where it stands up.</param>
    /// <param name="inner">
    /// What actually fights, or null to let the run's boss-behaviour factory decide — M4-02's
    /// <c>WardenBehaviour</c> is the first thing to arrive here, and it does so without changing
    /// this signature. An explicit one wins over the factory, which is what a fixture supplies.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>It spawns through <see cref="Spawn"/> and adds one thing to it</b> (M4-01b rule 2). The
    /// registry, the depth scaling and the <see cref="EnemySpawned"/> announcement are all the
    /// ordinary ones, because a boss is an ordinary agent; what this method exists for is the one
    /// step <c>EnemyAgent.Initialise</c> cannot take, which is building a behaviour that needs a
    /// <see cref="BossSpec"/> the archetype does not name.
    /// </para>
    /// <para>
    /// <b><see cref="EnemySpawned"/> gains nothing and carries no <c>IsBoss</c> flag</b> (rule 7).
    /// What a view needs is the phase count, and that rides on <c>BossPhaseChanged</c>, published
    /// here for phase 0 immediately after the spawn — so a segmented bar (M4-04) is sized at the
    /// moment the body appears rather than at the first threshold.
    /// </para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException">
    /// The catalog holds no boss with that id, or no enemy with the id the boss names. Let through
    /// rather than rewrapped, exactly as <see cref="Spawn"/> does: it is the catalog's answer and it
    /// already names the id.
    /// </exception>
    /// <exception cref="InvalidOperationException">The registry is full at its capacity.</exception>
    public EnemyAgent SpawnBoss(ContentId bossId, Vector3 position, IEnemyBehaviour inner = null)
    {
        BossSpec boss = _catalog.Boss(bossId);

        EnemyAgent agent = Spawn(boss.EnemySpecId, position);

        // After the spawn, because a behaviour holds the agent it drives and there is no agent to
        // hold until this line has run — see _bossInner. An explicit `inner` wins, so a fixture
        // that hands one over is never second-guessed.
        var behaviour = new BossBehaviour(agent, boss, inner ?? _bossInner?.Invoke(agent, boss));

        agent.Behaviour = behaviour;

        // After the attachment, so a handler that resolves the id and asks the agent what phase it
        // is in finds a behaviour to ask — the same register-then-announce order Spawn itself uses.
        behaviour.Announce(_events);

        return agent;
    }

    /// <summary>
    /// Queues <paramref name="id"/> to be retired at the end of this tick, rather than now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is what a behaviour uses to retire an agent that is not itself</b>, and the reason it
    /// exists is the hazard <see cref="Tick"/>'s remarks have named since M2-08:
    /// <c>EnemyRegistry.Despawn</c> is an order-preserving removal that shifts the array
    /// <c>Registry.Alive</c> spans and nulls the slot that falls off the end, so a despawn from
    /// inside the behaviour pass would move the agents the pass has not reached yet and walk it past
    /// its own length. M4-01b's boss is the first behaviour to reach that — the beat clears the adds
    /// it summoned — and this is the answer, rather than the backwards walk the old note predicted:
    /// a backwards walk would have changed the order every enemy in the arena acts in, and spawn
    /// order is what <c>TargetScorer</c>'s tie-break reads.
    /// </para>
    /// <para>
    /// <b>Queued twice is not an error and costs one extra <see cref="Despawn"/> that answers
    /// false</b>, which is that method's own bargain. The drain runs inside <see cref="Tick"/>, so
    /// an id queued outside one waits until the next — there is no path to that in a live run,
    /// because the only caller is a behaviour.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// More ids have been queued this tick than the registry could possibly hold. Unreachable while
    /// a caller queues living agents, and loud rather than silent, because the alternative is a
    /// write past the end of the buffer.
    /// </exception>
    public void DespawnAtEndOfTick(int id)
    {
        if (_deferredCount >= _deferredDespawn.Length)
        {
            throw new InvalidOperationException(
                $"More than {_deferredDespawn.Length} despawns were queued in one tick, which is "
                    + "more agents than the registry can hold. Something is queuing the same id "
                    + "repeatedly.");
        }

        _deferredDespawn[_deferredCount] = id;
        _deferredCount++;
    }

    /// <summary>
    /// Spawns every entry of <paramref name="plan"/>, in plan order.
    /// </summary>
    /// <remarks>
    /// Order matters and is the plan's: ids are handed out in spawn order, and spawn order is what
    /// <c>EnemyRegistry.Alive</c> preserves and <c>TargetScorer</c>'s tie-break reads. A plan
    /// spawned in some other order would make the same seed play differently.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is null.</exception>
    public void SpawnAll(SpawnPlan plan)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

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

            // Every death pays, whoever caused it (M3-01a rule 3). This is the one door a death
            // comes through (AR §18.2), so a Bloater that lit its own fuse, a Charge, a cone and
            // M5-04's Wights all bank the same way without anybody having to ask who swung. A rule
            // about *who* killed it would need a second mechanism the day minions exist, and GD §15
            // says only that experience is granted on kill.
            PendingXp += agent.Spec.XpValue;

            // On the same line and through the same door, so that "every death pays" is one rule
            // with two currencies rather than two rules that can drift apart. Anything that ever
            // becomes true of a kill is banked here.
            PendingKills++;

            // The third thing banked on that line, and the first that is not a number (M5-04b rule
            // 2). CH §3.2's Rise stands a Wight up *where the corpse fell*, and neither counter above
            // can say where — so the death itself is written down and drained the same way, by a
            // pull from the tick rather than by core subscribing to its own event.
            //
            // Above the publish, like the two counters and like Spawn's register-then-announce: by
            // the time anything handles EnemyDied, everything this death is worth has been banked.
            Bank(agent);

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
    /// <para>
    /// <b>And as of M5-03 the second pass may be told about a corpse rather than about the
    /// player</b> — see <see cref="Perceive"/>, which is the one site that writes those fields and
    /// therefore the one site the redirect can live at without touching a behaviour.
    /// </para>
    /// </remarks>
    /// <param name="lures">
    /// Where the arena's decoys are, or null when there are none. Passed in rather than held, for
    /// the reason <c>PlayerCombat</c> is handed the world each time it is asked about it: this class
    /// owns no view of what is standing on the floor, and a retained reference would be one more
    /// thing to keep in step with a run's lifetime. Null and empty mean the same thing.
    /// </param>
    public void Ingest(WorldSnapshot snapshot, LureSystem lures = null)
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

        Perceive(snapshot.PlayerPosition, lures);
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
    /// underneath the walk, so no index shifts and no slot is nulled.
    /// </para>
    /// <para>
    /// <b>M4-01b is the day the rest of that warning came due, and it was paid a different way.</b>
    /// The paragraph used to end: <em>the backwards walk is still owed by the next behaviour that
    /// calls <see cref="Despawn"/> directly</em>. A boss's beat clears the adds it summoned, which
    /// is exactly that — and a backwards walk would have changed the order every enemy in the arena
    /// acts in, which is the order <c>TargetScorer</c>'s tie-break reads. So a behaviour queues
    /// through <see cref="DespawnAtEndOfTick"/> instead and the walk is left alone; the ban on
    /// calling <see cref="Despawn"/> from inside this pass stands, and is now a ban with somewhere
    /// to go.
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

                case EnemyBehaviourKind.Boss:
                    // The one kind EnemyAgent.Initialise deliberately leaves without a behaviour,
                    // because building one needs the BossSpec that names this body and an
                    // EnemySpec does not carry it (M4-01b rule 2). Loud rather than a null-safe
                    // call: a boss authored Boss and spawned through the ordinary Spawn would
                    // otherwise stand in the arena for the whole fight, never changing phase, with
                    // nothing in the log — which is the silence M2-06 rule 11 refuses.
                    if (agent.Behaviour is null)
                    {
                        throw new InvalidOperationException(
                            $"'{agent.Spec.Id}' is authored as a Boss but has no behaviour. A boss "
                                + "is brought into being through EnemySystem.SpawnBoss, which is "
                                + "the one place that holds both its BossSpec and its body.");
                    }

                    agent.Behaviour.Tick(ctx);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unhandled enemy behaviour '{agent.Spec.Behaviour}' on '{agent.Spec.Id}'.");
            }
        }

        // Last, and after the walk rather than inside it — see DespawnAtEndOfTick. Nothing between
        // here and the loop may take a span over the registry.
        DrainDeferredDespawns();
    }

    /// <summary>
    /// Empties the registry without announcing anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the end of a run, where <c>RunScope</c> is going away and with it every subscriber a
    /// despawn event could reach — publishing 60 of them into a scope mid-teardown would be noise at
    /// best. Anything that retires one enemy <em>during</em> a stage calls <see cref="Despawn"/>,
    /// which does announce it.
    /// </para>
    /// <para>
    /// And for a stage boundary as of M2-10, which is the same silence for a nearby reason: the
    /// arena those bodies were standing in is about to stop existing, behind a covered screen. The
    /// stage is complete by the time it is called, so what it actually empties is corpses still
    /// waiting for their despawn frame — the one kind of thing that survives a boundary and turns up
    /// standing in the next arena.
    /// </para>
    /// </remarks>
    public void Clear()
    {
        Registry.Clear();

        // The queue goes with the bodies. An id queued by a boss's last beat and drained after the
        // arena was emptied would be a despawn of whatever the pool had handed that agent out as
        // next — the same staleness EnemyRegistry rule 1 is about, one tick wide.
        _deferredCount = 0;

        // Banked experience goes with the bodies that earned it, and nothing is actually lost
        // either way (M3-01a rule 7): at a stage boundary the drain has already run this tick,
        // upstream of the flow, and at the end of a run there is nobody left to pay. The line is
        // here so that the day something clears the arena mid-tick, the loss is a documented one
        // rather than a stage's last kill silently paying twice or not at all.
        PendingXp = 0f;

        // And the kills that earned it, for exactly the same reason and with exactly the same
        // caveat — banked on the same line, so they are dropped on the same one.
        PendingKills = 0;

        // And the deaths themselves, third on that line and third here (M5-04b rule 2). It matters
        // slightly more than the two above: an undrained death carries a *position*, and a position
        // belonging to an arena that has been torn down would stand a Wight up in the next one at
        // coordinates that no longer mean anything — the decoy's problem in M5-03 rule 9 exactly.
        _deathCount = 0;

        // The census's own reading of the world goes with it. A player position left behind would
        // make the next arena's first frame of perception measure against where the last run stood
        // — inert until Ingest runs, and exactly the kind of thing a stage boundary makes reachable.
        _playerPosition = Vector3.Zero;

        // Depth is deliberately *not* reset. It belongs to whoever advances the stage, not to the
        // census: M2-10 clears an arena at a stage boundary and the next stage's depth is the
        // point of the transition, so zeroing it here would mean the two had to happen in an order
        // this method could not state.
    }

    /// <summary>
    /// Hands over everything <see cref="PendingXp"/> has collected and zeroes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called once a tick by <c>RunSession</c> and by nothing else. Take-and-clear in one call
    /// rather than a read and a separate reset, because two calls is a pair a future caller can get
    /// half of — and the half that is forgotten pays a stage's kills over and over, every tick, for
    /// the rest of the run.
    /// </para>
    /// <para>
    /// Returns zero on the overwhelming majority of ticks, which is the ordinary case and not one
    /// worth branching on here: <see cref="Soulvail.Core.Progression.LevelTracker.Grant"/> is
    /// silent for anything that is not greater than zero.
    /// </para>
    /// </remarks>
    /// <returns>The experience banked since the last drain.</returns>
    public float DrainXp()
    {
        float earned = PendingXp;

        PendingXp = 0f;

        return earned;
    }

    /// <summary>
    /// Hands over everything <see cref="PendingKills"/> has counted and zeroes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DrainXp"/>'s shape exactly, and take-and-clear in one call for its reason: two
    /// calls is a pair a future caller can get half of, and the half that is forgotten heals for a
    /// stage's kills over and over, every tick, for the rest of the run.
    /// </para>
    /// <para>
    /// Returns zero on the overwhelming majority of ticks, which is not worth branching on here:
    /// <c>PlayerCombat.HealForKills</c> is silent for zero.
    /// </para>
    /// </remarks>
    /// <returns>The deaths counted since the last drain.</returns>
    public int DrainKills()
    {
        int killed = PendingKills;

        PendingKills = 0;

        return killed;
    }

    /// <summary>
    /// Copies the deaths since the last drain into <paramref name="destination"/>, oldest first,
    /// and empties the buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="DrainXp"/>'s shape, and beside it for its reason</b> (M5-04b rule 2). A death
    /// can be reported between ticks — <c>ReportConeHits</c> is a fact arriving mid-frame (ADR-0003)
    /// — so what it is worth is decided on the tick, by the one object that knows whether the run is
    /// still running. <c>RunSession.Tick</c> calls this once a tick and nothing else does.
    /// </para>
    /// <para>
    /// <b>Take-and-clear in one call</b>, for the reason the two above give: two calls is a pair a
    /// future caller can get half of, and the half that is forgotten raises a stage's dead over and
    /// over, every tick, for the rest of the run.
    /// </para>
    /// <para>
    /// <b>Into a caller-owned span rather than out as a list</b> (AR §4.3). The buffer here is the
    /// registry's capacity and is refilled in place; the destination is <c>RunSession</c>'s own
    /// array, built once with the same number. Returns 0 on the overwhelming majority of ticks,
    /// which is the ordinary case and not one worth branching on here.
    /// </para>
    /// </remarks>
    /// <param name="destination">Where to write them. Must be at least as long as what is pending.</param>
    /// <returns>How many were written into <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is shorter than the pending deaths. Loud rather than truncating:
    /// a silently dropped death is a kill that never rises, and the caller sized its buffer from the
    /// same capacity this one was sized from, so a short span is a wiring mistake.
    /// </exception>
    public int DrainDeaths(Span<EnemyDeath> destination)
    {
        int count = _deathCount;

        if (destination.Length < count)
        {
            throw new ArgumentException(
                $"destination holds {destination.Length} and {count} deaths are pending. The "
                    + "buffer is sized at the registry's capacity, and a caller that drains every "
                    + "tick should size its own the same way.",
                nameof(destination));
        }

        new ReadOnlySpan<EnemyDeath>(_deaths, 0, count).CopyTo(destination);

        _deathCount = 0;

        return count;
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
    /// Writes one death into the drain buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A full buffer drops the oldest rather than growing</b> (M5-04b rule 2), which is the one
    /// behaviour here worth stating out loud. Growing would allocate on the kill path; throwing
    /// would end a run over a bookkeeping buffer; keeping the oldest and refusing the newest would
    /// make the arena's most recent death the one thing that cannot be acted on. So the oldest
    /// entry falls off the front — one <see cref="Array.Copy"/> of at most <c>capacity − 1</c>
    /// entries, which allocates nothing.
    /// </para>
    /// <para>
    /// <b>It is unreachable while the drain runs every tick</b>, and it does: <c>RunSession.Tick</c>
    /// calls <see cref="DrainDeaths"/> unconditionally, for every class, which is most of why rule
    /// 10 says an Oathbound run pays one array write per kill and nothing else.
    /// </para>
    /// </remarks>
    private void Bank(EnemyAgent agent)
    {
        if (_deathCount == _deaths.Length)
        {
            Array.Copy(_deaths, 1, _deaths, 0, _deaths.Length - 1);

            _deathCount--;
        }

        _deaths[_deathCount] = new EnemyDeath(
            agent.Spec.Id,
            agent.Position,
            agent.Spec.Behaviour == EnemyBehaviourKind.Boss);

        _deathCount++;
    }

    /// <summary>
    /// Retires everything <see cref="DespawnAtEndOfTick"/> queued this tick, in the order it was
    /// queued, and announces each one.
    /// </summary>
    /// <remarks>
    /// The count is zeroed <em>before</em> the walk, so a handler of <see cref="EnemyDespawned"/>
    /// that queued another despawn would be queuing it for the next tick rather than growing the
    /// list this one is walking.
    /// </remarks>
    private void DrainDeferredDespawns()
    {
        int count = _deferredCount;

        _deferredCount = 0;

        for (int i = 0; i < count; i++)
        {
            Despawn(_deferredDespawn[i]);
        }
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
    /// Fills every living agent's derived perception from the positions just ingested — or from a
    /// corpse decoy, while one is standing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The redirect lives here and nowhere else, which is what keeps it out of the
    /// behaviours</b> (M5-03 rule 2). This method is the one writer of
    /// <see cref="EnemyBlackboard.PlayerPosition"/>, <see cref="EnemyBlackboard.DistanceToPlayer"/>
    /// and <see cref="EnemyBlackboard.DirectionToPlayer"/>, so a decoy is one local variable
    /// swapped before the three are derived from it and not a branch in four state machines.
    /// </para>
    /// <para>
    /// <b><see cref="EnemyBlackboard.PathDirectionToPlayer"/> is zeroed for a lured agent</b>, and
    /// that is the half a reader will not guess. The path in <c>EnemySense</c> was computed by the
    /// body against the <em>player</em>, and left alone it would steer the enemy around the arena
    /// towards a player it is not walking at any more. The behaviours already fall back to the
    /// straight line when it is zero (<c>ChaserBehaviour.TickChase</c>'s ternary) — the fallback
    /// M1-19 built for a missing NavMesh, and this is the first thing that uses it on purpose.
    /// <b>So a lured enemy walks in a straight line and can be stopped by a pillar</b>, which is
    /// acceptable over six metres and three seconds and is written down rather than discovered.
    /// </para>
    /// </remarks>
    private void Perceive(Vector3 playerPosition, LureSystem lures)
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

            // Where this enemy's quarry is: the player, unless a corpse is standing. Asked per
            // agent rather than once for the arena because the answer is "the nearest decoy to
            // *you*" — which is not a filter on who is taunted (every living enemy is, rule 8) but
            // a choice between the two that may stand at once.
            Vector3 quarry = playerPosition;
            bool lured = false;

            if (lures is not null && lures.TryGetLure(position, out Vector3 decoy))
            {
                quarry = decoy;
                lured = true;
            }

            blackboard.SelfPosition = position;
            blackboard.SelfVelocity = agent.Velocity;
            blackboard.PlayerPosition = quarry;

            // XZ, not the full 3D separation: everything here happens on the ground plane, and the
            // Y difference between a player capsule's centre and an enemy's is a rendering detail
            // that would otherwise inflate every distance a strike or a spell is checked against.
            var toPlayer = new Vector2(quarry.X - position.X, quarry.Z - position.Z);
            float distance = toPlayer.Length();

            blackboard.DistanceToPlayer = distance;
            blackboard.DirectionToPlayer = distance < MinDirectionDistance
                ? Vector2.Zero
                : toPlayer / distance;

            // Written unconditionally, so a decoy that rotted since the last tick lowers it — the
            // same discipline every perception field on this blackboard keeps.
            blackboard.QuarryIsADecoy = lured;

            // After the copy in Ingest's first pass and therefore winning over it. See the remarks:
            // the body's path was computed against the player, so it is the one sense that lies
            // while a decoy stands.
            if (lured)
            {
                blackboard.PathDirectionToPlayer = Vector2.Zero;
            }

            blackboard.AlliesNearby = CountAlliesNearby(agents, i, position);

            // The trigger half (M4-01a rule 6), written here rather than anywhere else for the
            // reason the perception half is written here: one writer, once a tick, above the
            // behaviour step — so a condition asked about this enemy's own health this tick reads
            // this tick's health. Copied from Health rather than derived, so the shield fraction
            // is honestly zero today instead of a literal that would be wrong the day something
            // grants an enemy one.
            blackboard.HpFraction = agent.Health.Fraction;
            blackboard.ShieldFraction = agent.Health.ShieldFraction;
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
