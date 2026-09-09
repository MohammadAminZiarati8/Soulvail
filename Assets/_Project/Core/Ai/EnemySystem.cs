using System;
using System.Collections.Generic;
using System.Numerics;
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
    /// <remarks>
    /// Both archetypes stand still in M1-06, so this only dispatches: <c>Static</c> does nothing
    /// by definition, and <c>Chaser</c> does nothing until M1-18 writes <c>ChaserBehaviour</c>.
    /// The loop and the switch exist now because this is the one site that knows the full set of
    /// behaviours, which is why <c>EnemyBehaviourKind</c> is deliberately unvalidated where it is
    /// authored (M1-05) — a third kind added without teaching this method about it fails loudly
    /// here instead of standing motionless in the arena with nothing in the log.
    /// </remarks>
    public void Tick(float dt, float now)
    {
        ReadOnlySpan<EnemyAgent> agents = Registry.Alive;

        for (int i = 0; i < agents.Length; i++)
        {
            EnemyAgent agent = agents[i];

            // Registered is not breathing (EnemyRegistry rule 4): a corpse sits in the span until
            // M1-11 has published its death, and a corpse does not act.
            if (!agent.IsAlive)
            {
                continue;
            }

            switch (agent.Spec.Behaviour)
            {
                case EnemyBehaviourKind.Static:
                    break;

                case EnemyBehaviourKind.Chaser:
                    // M1-18. Until then a Husk is a dummy that holds still, which is what makes
                    // targeting, cone hits and damage judgeable on their own.
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
