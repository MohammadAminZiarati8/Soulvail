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
    /// Below this distance the player and the enemy are the same point and there is no direction
    /// to give. Not zero: dividing by a distance of 1e-9 yields a unit vector made of noise, which
    /// is worse than admitting there is no answer.
    /// </summary>
    private const float MinDirectionDistance = 1e-4f;

    private readonly ContentCatalog _catalog;
    private readonly IDomainEvents _events;

    /// <param name="catalog">Where a spawn's <see cref="ContentId"/> becomes an <see cref="EnemySpec"/>.</param>
    /// <param name="events">Where <see cref="EnemySpawned"/> and <see cref="EnemyDespawned"/> go.</param>
    /// <param name="capacity">
    /// The most enemies that may exist at once. Passed straight to the registry, which guards it,
    /// and it must match the snapshot's enemy capacity — an enemy core knows about but the
    /// snapshot cannot carry is one core is blind to the position of.
    /// </param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public EnemySystem(ContentCatalog catalog, IDomainEvents events, int capacity)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _events = events ?? throw new ArgumentNullException(nameof(events));

        Registry = new EnemyRegistry(capacity);
    }

    /// <summary>
    /// Who is out there. Read by targeting, by the AI and by anything resolving an id; written
    /// only through this system's <see cref="Spawn"/> and <see cref="Despawn"/>, so no caller can
    /// register an enemy that was never announced.
    /// </summary>
    public EnemyRegistry Registry { get; }

    /// <summary>
    /// Brings one enemy of <paramref name="specId"/> into being at <paramref name="position"/> and
    /// announces it.
    /// </summary>
    /// <remarks>
    /// The archetype is resolved before the agent is registered, so an unknown id leaves the
    /// registry untouched and publishes nothing — the same order, for the same reason, as
    /// <c>RunSession.Start</c> reading the catalog before it assigns any state. The event goes out
    /// *after* registration, so a handler that resolves the id it carries finds the agent.
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

        _events.Publish(new EnemySpawned(agent.Id, spec.Id, position));

        return agent;
    }

    /// <summary>Spawns every entry of <paramref name="plan"/>, in plan order.</summary>
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
    /// <see cref="EnemyDied"/> still resolvable while the event is being handled.
    /// </para>
    /// </remarks>
    /// <param name="enemyId">Who to hurt. An id that is not registered is not an error.</param>
    /// <param name="amount">Damage to apply. Zero, negative and NaN all do nothing.</param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <returns>What <see cref="Health"/> did, unchanged, for a caller with its own conclusions to draw.</returns>
    public DamageResult ApplyDamage(int enemyId, float amount, float now)
    {
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

            _events.Publish(new EnemyDied(enemyId, agent.Spec.Id, agent.Position));
        }

        return result;
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

        Perceive(snapshot.PlayerPosition);
    }

    /// <summary>
    /// Advances every living enemy's behaviour by <paramref name="dt"/> seconds.
    /// </summary>
    /// <param name="dt">Seconds since the last tick, from the snapshot.</param>
    /// <param name="now">
    /// Simulated run time, the same seconds <c>Health</c> and <c>Targeter</c> are handed — never a
    /// wall clock.
    /// </param>
    /// <param name="player">
    /// Who the enemies are fighting. Handed down rather than held as a field, because it belongs to
    /// the run and is rebuilt with it — a reference kept here would outlive the player it names the
    /// first time <c>RunSession.Start</c> is called twice.
    /// </param>
    /// <param name="intents">Where each behaviour's <c>EnemyMoveIntent</c> goes.</param>
    /// <remarks>
    /// <para>
    /// This dispatches and nothing else: <c>Static</c> does nothing by definition — that is what the
    /// kind means, and it is why a dummy holds still — and <c>Chaser</c> is
    /// <c>ChaserBehaviour</c>'s (M1-18). The switch is here because this is the one site that knows
    /// the full set of behaviours, which is why <c>EnemyBehaviourKind</c> is deliberately unvalidated
    /// where it is authored (M1-05) — a third kind added without teaching this method about it fails
    /// loudly here instead of standing motionless in the arena with nothing in the log.
    /// </para>
    /// <para>
    /// The corpse sweep runs first, so the behaviour pass walks a registry nothing is about to
    /// remove from. Order between the two is otherwise free — a corpse does not act either way —
    /// and this way there is only one span to reason about.
    /// </para>
    /// <para>
    /// <b>The span is re-read after the pass rather than hoisted across it, and that is not
    /// optional.</b> A strike can kill the player but never an enemy, so nothing in this loop can
    /// despawn anything and the span stays valid throughout — but the loop is written against
    /// <c>Registry.Alive</c> taken once *after* the sweep for exactly that reason, and the day a
    /// behaviour gains the power to retire an agent (a Bloater exploding, M2-08) it has to walk
    /// backwards the way <see cref="SweepCorpses"/> does.
    /// </para>
    /// </remarks>
    public void Tick(float dt, float now, PlayerCombat player, IIntentSink intents)
    {
        SweepCorpses(now);

        ReadOnlySpan<EnemyAgent> agents = Registry.Alive;

        for (int i = 0; i < agents.Length; i++)
        {
            EnemyAgent agent = agents[i];

            // Registered is not breathing (EnemyRegistry rule 4): a corpse sits in the span until
            // M1-11 has published its death, and a corpse does not act. This is also what makes a
            // Husk killed mid-windup cancel its strike — there is no path from here to the damage
            // frame for something that is not alive.
            if (!agent.IsAlive)
            {
                continue;
            }

            switch (agent.Spec.Behaviour)
            {
                case EnemyBehaviourKind.Static:
                    break;

                case EnemyBehaviourKind.Chaser:
                    agent.Behaviour.Tick(dt, now, player, intents, _events);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unhandled enemy behaviour '{agent.Spec.Behaviour}' on '{agent.Spec.Id}'.");
            }
        }
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
