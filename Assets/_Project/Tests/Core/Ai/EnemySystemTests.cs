using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// <c>EnemySystem</c>, <c>SpawnPlan</c> and the two census events — every rule M1-06 wrote.
/// </summary>
/// <remarks>
/// <para>
/// The two <c>RunSession_</c> rows live here rather than in <c>RunSessionTests</c> because the
/// spec's Files table gives M1-06 one test file and those rows are its rules: what a run does with
/// an enemy system at each end of its life. The session's own contracts — its three methods, its
/// events, its intent — stay where M0-10 put them.
/// </para>
/// <para>
/// Every archetype here is built in the fixture rather than loaded from an asset.
/// <c>EnemyDefinition</c> and <c>Husk.asset</c> arrive in M1-07; until then the catalog is
/// assembled from specs, which is also what keeps these rows independent of what a designer later
/// types into the Inspector.
/// </para>
/// </remarks>
[TestFixture]
public sealed class EnemySystemTests
{
    private const string HuskId = "enemy.husk";
    private const string SpitterId = "enemy.spitter";
    private const string BloaterId = "enemy.bloater";
    private const string OathboundId = "character.oathbound";
    private const string DescentId = "mode.descent";

    /// <summary>
    /// What every session in this fixture is seeded with. Named since M2-02, because a
    /// <c>RunConfig</c> now states the seed and <c>RunSession.Start</c> refuses one that
    /// disagrees with the generator — so the two literals have to be the same literal.
    /// </summary>
    private const int SessionSeed = 7;

    /// <summary>An id of the right shape that the catalog does not hold.</summary>
    private const string UnknownId = "enemy.nobody";

    private const int Capacity = 8;

    /// <summary>
    /// The device cap a run composes its stages under (M2-05). Equal to the capacity, so nothing
    /// here is quietly bounded by a device tier — and every mode this fixture builds has an empty
    /// roster, so nothing is composed and the director is inert either way.
    /// </summary>
    private const int DeviceCap = 8;

    /// <summary>
    /// Room for every shot a row here puts in the air, which is none: nothing fires one until
    /// M2-07b. Required by <c>RunSession</c> since M2-07a, and guarded positive, so it is a
    /// number rather than a zero.
    /// </summary>
    private const int ProjectileCapacity = 8;

    /// <summary>60 fps doubled, matching <c>RunSessionTests</c>.</summary>
    private const float Frame = 1f / 120f;

    private RecordingEvents _events;
    private ContentCatalog _catalog;

    /// <summary>
    /// The run's generator, required by the system as of M1-19 and drawn from by nothing in this
    /// fixture: no row here carries a respawn policy, so the only stream that would be touched is
    /// never reached. <c>RespawnPolicyTests</c> is where the draws are asserted on.
    /// </summary>
    private FixedRandom _random;

    private EnemySystem _system;

    /// <summary>
    /// What <c>Tick</c> hands the behaviours as of M1-18. Inert in every row in this fixture — its
    /// Husks are all <c>Static</c>, and a static behaviour never reaches either — but the signature
    /// requires them, and building them in <c>SetUp</c> keeps them out of the allocation row's
    /// measured body.
    /// </summary>
    private PlayerCombat _player;

    private RecordingIntents _intents;

    /// <summary>The sky the context carries. Nothing in this fixture fires into it.</summary>
    private ProjectileSystem _projectiles;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _catalog = Catalog();
        _random = new FixedRandom();
        _system = new EnemySystem(_catalog, _events, _random, Scaling(), Capacity);
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, ProjectileCapacity);
    }

    [Test]
    public void Ctor_NullDependency_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EnemySystem(null, _events, _random, Scaling(), Capacity));
        Assert.Throws<ArgumentNullException>(() => new EnemySystem(_catalog, null, _random, Scaling(), Capacity));
        Assert.Throws<ArgumentNullException>(() => new EnemySystem(_catalog, _events, null, Scaling(), Capacity));

        // Required as of M2-03, and a constructor argument rather than something adopted from a
        // plan on purpose: it means Spawn cannot run without one. An unscaled enemy is not a loud
        // failure, it is a stage-20 Husk that dies in two hits.
        Assert.Throws<ArgumentNullException>(() => new EnemySystem(_catalog, _events, _random, null, Capacity));

        // The registry's own guard, surfaced through this constructor: a system that can hold no
        // enemies is a configuration mistake rather than a valid state to run with.
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnemySystem(_catalog, _events, _random, Scaling(), 0));
    }

    [Test]
    public void Spawn_AppliesDepth()
    {
        _system.Depth = 10;

        EnemyAgent agent = _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        // h(10) = 1 + 0.06·9 = 1.54, on GD §8.1's 36. The scaling happens inside Spawn, which is
        // why there is nowhere to forget it: every enemy in the game — a plan, a respawn, M2-05's
        // director — comes into being through that one method (M2-03 rule 12).
        Assert.That(agent.Health.MaxHp.Value, Is.EqualTo(36f * 1.54f).Within(1e-3f));

        // And full at the number it now has, not at the one it was authored with.
        Assert.That(agent.Health.Current, Is.EqualTo(agent.Health.MaxHp.Value).Within(1e-3f));

        // The other two go with it, so a stage-10 Husk is not merely a bigger health bar.
        Assert.That(agent.ContactDamage.Value, Is.EqualTo(8f * 1.315f).Within(1e-3f));
        Assert.That(agent.MoveSpeed.Value, Is.EqualTo(3.5f * 1.04f).Within(1e-3f));
    }

    [Test]
    public void Spawn_ScaledBeforeAnnounced()
    {
        // The order inside Spawn, and it is observable: a health bar built on EnemySpawned would
        // otherwise be sized to the unscaled maximum for its first frame.
        var events = new CallbackEvents();
        var system = new EnemySystem(_catalog, events, _random, Scaling(), Capacity) { Depth = 10 };

        float? maxDuringEvent = null;

        events.OnPublish = evt =>
        {
            if (evt is EnemySpawned spawned && system.Registry.TryGet(spawned.Id, out EnemyAgent agent))
            {
                maxDuringEvent = agent.Health.MaxHp.Value;
            }
        };

        system.Spawn(new ContentId(HuskId), Vector3.Zero);

        Assert.That(maxDuringEvent, Is.Not.Null, "Sanity: the spawn event resolved its own id.");
        Assert.That(maxDuringEvent.Value, Is.EqualTo(36f * 1.54f).Within(1e-3f));
    }

    [Test]
    public void Depth_DefaultsToOne_AndRefusesLess()
    {
        // Stages are numbered from 1 (GD §8.2), so the default is the shallowest depth there is
        // rather than zero: a system asked to spawn before anything set a depth scales to the
        // first stage instead of throwing from inside a curve.
        Assert.That(_system.Depth, Is.EqualTo(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => _system.Depth = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => _system.Depth = -4);

        Assert.That(_system.Depth, Is.EqualTo(1), "A refused assignment changes nothing.");
    }

    [Test]
    public void Depth_SurvivesClear()
    {
        // Deliberate, and the reason is M2-10: a stage boundary clears the arena and the next
        // stage's depth is the point of the transition, so zeroing it here would put the two in an
        // order this method could not state.
        _system.Depth = 7;
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        _system.Clear();

        Assert.That(_system.Depth, Is.EqualTo(7));
    }

    [Test]
    public void Spawn_RegistersAndPublishes()
    {
        EnemyAgent agent = _system.Spawn(new ContentId(HuskId), new Vector3(2f, 0f, 3f));

        Assert.That(_system.Registry.AliveCount, Is.EqualTo(1));
        Assert.That(agent.Id, Is.EqualTo(1), "Ids start at one, so zero is never a valid enemy.");
        Assert.That(agent.Spec.Id, Is.EqualTo(new ContentId(HuskId)));
        Assert.That(agent.Position, Is.EqualTo(new Vector3(2f, 0f, 3f)));

        EnemySpawned spawned = _events.Single<EnemySpawned>();

        Assert.That(spawned.Id, Is.EqualTo(1));
        Assert.That(spawned.SpecId, Is.EqualTo(new ContentId(HuskId)));
        Assert.That(spawned.Position, Is.EqualTo(new Vector3(2f, 0f, 3f)));
    }

    [Test]
    public void Spawn_UnknownSpec_Throws_NoSpawn()
    {
        // The catalog's exception, not a rewrapped one: it already names the id, and a spawn plan
        // pointing at an archetype nobody authored is missing content.
        KeyNotFoundException ex = Assert.Throws<KeyNotFoundException>(
            () => _system.Spawn(new ContentId(UnknownId), Vector3.Zero));

        Assert.That(ex.Message, Does.Contain(UnknownId));

        // Resolved before anything is registered, so nothing is half-spawned and nothing was
        // announced — the same order Start takes with the character.
        Assert.That(_system.Registry.AliveCount, Is.Zero);
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void SpawnAll_InPlanOrder()
    {
        var plan = new SpawnPlan(new[]
        {
            new SpawnPlan.Entry(new ContentId(HuskId), new Vector3(1f, 0f, 0f)),
            new SpawnPlan.Entry(new ContentId(SpitterId), new Vector3(2f, 0f, 0f)),
            new SpawnPlan.Entry(new ContentId(BloaterId), new Vector3(3f, 0f, 0f)),
        });

        _system.SpawnAll(plan);

        Assert.That(_system.Registry.AliveCount, Is.EqualTo(3));

        // Ids follow plan order, and so does Alive — which is what TargetScorer's tie-break reads,
        // so a plan spawned in some other order would make the same seed play differently.
        ReadOnlySpan<EnemyAgent> alive = _system.Registry.Alive;

        Assert.That(alive[0].Id, Is.EqualTo(1));
        Assert.That(alive[0].Spec.Id, Is.EqualTo(new ContentId(HuskId)));
        Assert.That(alive[1].Id, Is.EqualTo(2));
        Assert.That(alive[1].Spec.Id, Is.EqualTo(new ContentId(SpitterId)));
        Assert.That(alive[2].Id, Is.EqualTo(3));
        Assert.That(alive[2].Spec.Id, Is.EqualTo(new ContentId(BloaterId)));

        IReadOnlyList<EnemySpawned> spawned = _events.Of<EnemySpawned>();

        Assert.That(spawned.Count, Is.EqualTo(3));
        Assert.That(spawned[0].SpecId, Is.EqualTo(new ContentId(HuskId)));
        Assert.That(spawned[1].SpecId, Is.EqualTo(new ContentId(SpitterId)));
        Assert.That(spawned[2].SpecId, Is.EqualTo(new ContentId(BloaterId)));
        Assert.That(spawned[2].Position, Is.EqualTo(new Vector3(3f, 0f, 0f)));

        // Nothing else published: SpawnAll spawns, it does not tidy up after itself.
        Assert.That(_events.All.Count, Is.EqualTo(3));
    }

    [Test]
    public void SpawnAll_NullPlan_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _system.SpawnAll(null));
    }

    [Test]
    public void SpawnAll_EmptyPlan_SpawnsNothing()
    {
        _system.SpawnAll(SpawnPlan.Empty);

        Assert.That(_system.Registry.AliveCount, Is.Zero);
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Despawn_PublishesAfterRemoval()
    {
        var events = new CallbackEvents();
        var system = new EnemySystem(_catalog, events, _random, Scaling(), Capacity);
        system.Spawn(new ContentId(HuskId), Vector3.Zero);

        int? countDuringEvent = null;
        bool? resolvableDuringEvent = null;

        events.OnPublish = evt =>
        {
            if (evt is EnemyDespawned)
            {
                countDuringEvent = system.Registry.AliveCount;
                resolvableDuringEvent = system.Registry.TryGet(1, out EnemyAgent _);
            }
        };

        Assert.That(system.Despawn(1), Is.True);

        // The opposite order from Spawn, and deliberate: a handler looking the id up inside this
        // event should find nothing, because nothing is what is there. Both orders say the same
        // thing — during the event the registry already agrees with the news.
        Assert.That(countDuringEvent, Is.EqualTo(0));
        Assert.That(resolvableDuringEvent, Is.False);
    }

    [Test]
    public void Despawn_Unknown_NoEvent()
    {
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);
        _events.Clear();

        Assert.That(_system.Despawn(9), Is.False);

        Assert.That(_events.All, Is.Empty);
        Assert.That(_system.Registry.AliveCount, Is.EqualTo(1), "The live enemy is untouched.");

        // Idempotent for the same corpse, which is what lets M1-11's death flow despawn without
        // first having to remember whether it already did.
        Assert.That(_system.Despawn(1), Is.True);
        Assert.That(_system.Despawn(1), Is.False);
        Assert.That(_events.Count<EnemyDespawned>(), Is.EqualTo(1));
    }

    [Test]
    public void Ingest_CopiesPositionsById()
    {
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        var snapshot = new WorldSnapshot(Capacity);

        // Deliberately out of order. The snapshot is filled by whichever views the builder walked
        // first, which is Unity's business and not core's — an ingestion that trusted the index
        // would silently swap two enemies' positions the first time a view was re-parented.
        AddSense(snapshot, id: 2, position: new Vector3(0f, 0f, 7f), velocity: new Vector3(0f, 0f, 1f));
        AddSense(snapshot, id: 1, position: new Vector3(4f, 0f, 0f), velocity: new Vector3(2f, 0f, 0f));

        _system.Ingest(snapshot);

        Assert.That(_system.Registry.TryGet(1, out EnemyAgent first), Is.True);
        Assert.That(_system.Registry.TryGet(2, out EnemyAgent second), Is.True);

        Assert.That(first.Position, Is.EqualTo(new Vector3(4f, 0f, 0f)));
        Assert.That(first.Velocity, Is.EqualTo(new Vector3(2f, 0f, 0f)));
        Assert.That(second.Position, Is.EqualTo(new Vector3(0f, 0f, 7f)));
        Assert.That(second.Velocity, Is.EqualTo(new Vector3(0f, 0f, 1f)));

        // And the blackboard agrees with the agent, because that is what a behaviour reads.
        Assert.That(first.Blackboard.SelfPosition, Is.EqualTo(new Vector3(4f, 0f, 0f)));
        Assert.That(first.Blackboard.SelfVelocity, Is.EqualTo(new Vector3(2f, 0f, 0f)));
    }

    [Test]
    public void Ingest_IgnoresUnknownIds()
    {
        _system.Spawn(new ContentId(HuskId), new Vector3(1f, 0f, 1f));

        var snapshot = new WorldSnapshot(Capacity);
        AddSense(snapshot, id: 7, position: new Vector3(9f, 0f, 9f));
        AddSense(snapshot, id: 1, position: new Vector3(2f, 0f, 2f));

        // A view is allowed to lag a frame behind a despawn — it has a dissolve to play — so an
        // unknown id is ordinary rather than exceptional. Throwing here would mean core failing
        // every time an enemy died.
        Assert.That(() => _system.Ingest(snapshot), Throws.Nothing);

        Assert.That(_system.Registry.TryGet(1, out EnemyAgent agent), Is.True);
        Assert.That(agent.Position, Is.EqualTo(new Vector3(2f, 0f, 2f)), "The known id was still ingested.");
    }

    [Test]
    public void Ingest_KeepsPositionWhenAbsent()
    {
        _system.Spawn(new ContentId(HuskId), new Vector3(5f, 0f, 5f));

        var snapshot = new WorldSnapshot(Capacity);

        _system.Ingest(snapshot);

        Assert.That(_system.Registry.TryGet(1, out EnemyAgent agent), Is.True);

        // The other side of the lagging view: this enemy was spawned this frame and no view has
        // reported it yet, so the spawn position it was given is the best answer there is. Zeroing
        // it would teleport every new enemy to the origin for one frame.
        Assert.That(agent.Position, Is.EqualTo(new Vector3(5f, 0f, 5f)));
        Assert.That(agent.Blackboard.SelfPosition, Is.EqualTo(new Vector3(5f, 0f, 5f)),
            "Perception covers every living agent, not only the ones the snapshot named.");
    }

    [Test]
    public void Perception_DistanceAndDirection()
    {
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        var snapshot = new WorldSnapshot(Capacity);

        // The spec's numbers with the enemy and the player the other way round. `DirectionToPlayer`
        // points *at* the player: from an enemy at (3, 0, 4) towards an origin player it is
        // (−0.6, −0.8), which the row below pins, and the spec's own (0.6, 0.8) is this geometry.
        snapshot.PlayerPosition = new Vector3(3f, 0f, 4f);
        AddSense(snapshot, id: 1, position: Vector3.Zero);

        _system.Ingest(snapshot);

        EnemyBlackboard blackboard = Blackboard(1);

        Assert.That(blackboard.PlayerPosition, Is.EqualTo(new Vector3(3f, 0f, 4f)));
        Assert.That(blackboard.DistanceToPlayer, Is.EqualTo(5f).Within(1e-5f));
        Assert.That(blackboard.DirectionToPlayer.X, Is.EqualTo(0.6f).Within(1e-5f));
        Assert.That(blackboard.DirectionToPlayer.Y, Is.EqualTo(0.8f).Within(1e-5f));
    }

    [Test]
    public void Perception_DistanceIgnoresHeight()
    {
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        var snapshot = new WorldSnapshot(Capacity);

        // A player capsule's centre sits a metre or so above an enemy's, and that difference is a
        // rendering detail. Counted, it would inflate every distance a strike or a spell is
        // checked against — and the reach numbers in EnemySpec are ground-plane metres.
        snapshot.PlayerPosition = new Vector3(3f, 12f, 4f);
        AddSense(snapshot, id: 1, position: Vector3.Zero);

        _system.Ingest(snapshot);

        Assert.That(Blackboard(1).DistanceToPlayer, Is.EqualTo(5f).Within(1e-5f));
    }

    [Test]
    public void Perception_DirectionZeroWhenCoincident()
    {
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        var snapshot = new WorldSnapshot(Capacity);
        snapshot.PlayerPosition = new Vector3(2f, 0f, 2f);
        AddSense(snapshot, id: 1, position: new Vector3(2f, 0f, 2f));

        _system.Ingest(snapshot);

        EnemyBlackboard blackboard = Blackboard(1);

        // Zero rather than a normalised NaN. There is genuinely no direction here, and a behaviour
        // has to be able to tell — one NaN would survive every later multiplication and the enemy
        // would never move again, with nothing in the log.
        Assert.That(blackboard.DirectionToPlayer, Is.EqualTo(Vector2.Zero));
        Assert.That(blackboard.DistanceToPlayer, Is.EqualTo(0f).Within(1e-6f));
        Assert.That(float.IsNaN(blackboard.DirectionToPlayer.X), Is.False);
    }

    [Test]
    public void Perception_PathDirectionCopied()
    {
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        var snapshot = new WorldSnapshot(Capacity);
        AddSense(
            snapshot,
            id: 1,
            position: Vector3.Zero,
            path: new Vector2(1f, 0f),
            hasLineOfSight: true);

        _system.Ingest(snapshot);

        EnemyBlackboard blackboard = Blackboard(1);

        // Copied, never derived: pathfinding and visibility are senses Unity owns (AR §3). Core
        // decides what to do about them and could not compute either one for itself.
        Assert.That(blackboard.PathDirectionToPlayer, Is.EqualTo(new Vector2(1f, 0f)));
        Assert.That(blackboard.HasLineOfSight, Is.True);
    }

    [Test]
    public void Perception_AlliesNearby()
    {
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        var snapshot = new WorldSnapshot(Capacity);
        AddSense(snapshot, id: 1, position: Vector3.Zero);
        AddSense(snapshot, id: 2, position: new Vector3(0f, 0f, 5f));
        AddSense(snapshot, id: 3, position: new Vector3(0f, 0f, 20f));

        _system.Ingest(snapshot);

        Assert.That(Blackboard(1).AlliesNearby, Is.EqualTo(1));
        Assert.That(Blackboard(2).AlliesNearby, Is.EqualTo(1));
        Assert.That(Blackboard(3).AlliesNearby, Is.Zero, "Nobody counts themselves.");

        // Six metres exactly is nearby — the radius is inclusive, and a boundary that moved with
        // floating-point noise would make GD §8.1's clustering pressure flicker.
        snapshot.Clear();
        AddSense(snapshot, id: 1, position: Vector3.Zero);
        AddSense(snapshot, id: 2, position: new Vector3(0f, 0f, 6f));
        AddSense(snapshot, id: 3, position: new Vector3(0f, 0f, 20f));

        _system.Ingest(snapshot);

        Assert.That(Blackboard(1).AlliesNearby, Is.EqualTo(1));
    }

    [Test]
    public void Perception_SkipsDeadAgents()
    {
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);
        EnemyAgent corpse = _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        var snapshot = new WorldSnapshot(Capacity);
        snapshot.PlayerPosition = new Vector3(0f, 0f, 10f);
        AddSense(snapshot, id: 1, position: Vector3.Zero);
        AddSense(snapshot, id: 2, position: Vector3.Zero);

        corpse.Health.ApplyDamage(corpse.Spec.MaxHp, now: 0f);
        Assert.That(corpse.IsAlive, Is.False);

        _system.Ingest(snapshot);

        // A corpse stays in Alive until M1-11 has published its death, so both halves of rule 5
        // have to say "living": it gets no perception of its own, and it is nobody's ally.
        Assert.That(corpse.Blackboard.DistanceToPlayer, Is.Zero);
        Assert.That(Blackboard(1).AlliesNearby, Is.Zero);
    }

    [Test]
    public void Tick_StaticBehaviour_DoesNothing()
    {
        EnemyAgent agent = _system.Spawn(new ContentId(HuskId), new Vector3(3f, 0f, 0f));

        _system.Tick(Context(now: 1f));

        // The whole of M1-06's behaviour: a dummy that holds still, which is what makes targeting,
        // cone hits and damage judgeable on their own.
        Assert.That(agent.Position, Is.EqualTo(new Vector3(3f, 0f, 0f)));
        Assert.That(agent.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(agent.Blackboard.StateTimer, Is.Zero);

        // Static means static in the strongest sense as of M1-18: no behaviour object was ever
        // built for it, so it cannot have walked, and it wrote no intent for a body to read.
        Assert.That(agent.Behaviour, Is.Null);
        Assert.That(_intents.EnemyMoves, Is.Empty);
    }

    [Test]
    public void Tick_UnhandledBehaviour_Throws()
    {
        // Beyond the spec's Tests table, and the reason EnemySpec deliberately does not validate
        // EnemyBehaviourKind (M1-05): the dispatch is the one site that knows the full set, so a
        // third kind added without teaching it about the new behaviour has to fail here. A cast is
        // how a test reaches that state, and it is exactly the state a half-finished enum leaves.
        var catalog = new ContentCatalog(
            Array.Empty<CharacterSpec>(),
            new[] { Enemy("enemy.unhandled", (EnemyBehaviourKind)99) });

        var system = new EnemySystem(catalog, _events, _random, Scaling(), Capacity);
        system.Spawn(new ContentId("enemy.unhandled"), Vector3.Zero);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => system.Tick(Context(now: 0f)));

        Assert.That(ex.Message, Does.Contain("enemy.unhandled"));
    }

    [Test]
    public void Clear_EmptiesWithoutEvents()
    {
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);
        _system.Spawn(new ContentId(HuskId), Vector3.Zero);
        _events.Clear();

        _system.Clear();

        Assert.That(_system.Registry.AliveCount, Is.Zero);
        Assert.That(_events.All, Is.Empty, "Between runs, with nobody left to tell.");

        // Ids start again from one, which is only safe because nothing outside is still holding
        // one — that is what makes Clear a between-runs operation.
        Assert.That(_system.Spawn(new ContentId(HuskId), Vector3.Zero).Id, Is.EqualTo(1));
    }

    [Test]
    public void RunSession_Start_PublishesRunStartedBeforeSpawns()
    {
        var events = new RecordingEvents();
        RunSession session = Session(events);

        var plan = new SpawnPlan(new[]
        {
            new SpawnPlan.Entry(new ContentId(HuskId), new Vector3(1f, 0f, 0f)),
            new SpawnPlan.Entry(new ContentId(SpitterId), new Vector3(2f, 0f, 0f)),
        });

        session.Start(new RunConfig(
            new ContentId(DescentId), new ContentId(OathboundId), SessionSeed, 1, plan));

        // A run has to be announced before the things inside it are: a view handling EnemySpawned
        // may reasonably assume there is a run to put an enemy in. Subscribers are wired when the
        // scope is built, long before Start, so the ordering is about meaning rather than about
        // who is listening.
        Assert.That(events.All.Count, Is.EqualTo(3));
        Assert.That(events.All[0], Is.InstanceOf<RunStarted>());
        Assert.That(events.All[1], Is.InstanceOf<EnemySpawned>());
        Assert.That(events.All[2], Is.InstanceOf<EnemySpawned>());

        Assert.That(((EnemySpawned)events.All[1]).SpecId, Is.EqualTo(new ContentId(HuskId)));
        Assert.That(((EnemySpawned)events.All[2]).SpecId, Is.EqualTo(new ContentId(SpitterId)));

        Assert.That(session.State.EnemyCount, Is.EqualTo(2));
    }

    [Test]
    public void RunSession_End_ClearsEnemies_NoDespawnEvents()
    {
        var events = new RecordingEvents();
        RunSession session = Session(events);

        var plan = new SpawnPlan(new[]
        {
            new SpawnPlan.Entry(new ContentId(HuskId), Vector3.Zero),
            new SpawnPlan.Entry(new ContentId(HuskId), new Vector3(1f, 0f, 0f)),
        });

        session.Start(new RunConfig(
            new ContentId(DescentId), new ContentId(OathboundId), SessionSeed, 1, plan));
        Assert.That(session.State.EnemyCount, Is.EqualTo(2));

        events.Clear();

        session.End();

        Assert.That(session.State.EnemyCount, Is.Zero);

        // The scope is going away and with it every subscriber a despawn could reach, so 64
        // farewell events would be noise. Anything that retires one enemy *during* a run calls
        // Despawn, which does announce it.
        Assert.That(events.Count<EnemyDespawned>(), Is.Zero);
        Assert.That(events.Count<RunEnded>(), Is.EqualTo(1));
    }

    [Test]
    public void RunSession_Tick_TicksEnemies()
    {
        // Beyond the spec's Tests table, and it pins a wire rather than a value. RunState.Enemies
        // is internal (see its remarks) and Soulvail.Tests.Core has no InternalsVisibleTo, so
        // there is no public route from a session to an agent's blackboard — which leaves the loud
        // dispatch as the only publicly observable effect of the session ticking its enemies. It
        // proves the call happens; the *order* of the calls inside Tick is unobservable until
        // M1-08's targeting reads a position, and until then it is verified by reading.
        var catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Enemy("enemy.unhandled", (EnemyBehaviourKind)99) },
            new[] { Descent() });

        var session = new RunSession(
            catalog,
            new FixedRandom(SessionSeed),
            new RecordingEvents(),
            new RecordingIntents(),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        var plan = new SpawnPlan(new[]
        {
            new SpawnPlan.Entry(new ContentId("enemy.unhandled"), Vector3.Zero),
        });

        session.Start(new RunConfig(
            new ContentId(DescentId), new ContentId(OathboundId), SessionSeed, 1, plan));

        var snapshot = new WorldSnapshot(Capacity);
        snapshot.Dt = Frame;
        AddSense(snapshot, id: 1, position: Vector3.Zero);

        Assert.Throws<InvalidOperationException>(() => session.Tick(snapshot));
    }

    [Test]
    public void IngestAndTick_AllocateNothing()
    {
        const int count = 32;
        var catalog = Catalog();
        var system = new EnemySystem(catalog, new RecordingEvents(), new FixedRandom(), Scaling(), 64);
        var snapshot = new WorldSnapshot(64);

        snapshot.PlayerPosition = new Vector3(3f, 0f, 3f);

        for (int i = 0; i < count; i++)
        {
            system.Spawn(new ContentId(HuskId), new Vector3(i, 0f, 0f));

            AddSense(
                snapshot,
                id: i + 1,
                position: new Vector3(i * 0.5f, 0f, 0f),
                velocity: new Vector3(0f, 0f, 1f),
                path: new Vector2(0f, 1f),
                hasLineOfSight: true);
        }

        // The snapshot is filled outside the measured body deliberately: AllocationAssert cannot
        // measure a `stackalloc` or a ref struct through a lambda (M1-03), and refilling here
        // would measure AddEnemy's bookkeeping rather than the two methods under test.
        AllocationAssert.None(() =>
        {
            system.Ingest(snapshot);
            system.Tick(Context(1f));
        });
    }

    [Test]
    public void Catalog_Enemy_LookupAndDuplicates()
    {
        EnemySpec husk = Enemy(HuskId, EnemyBehaviourKind.Static);
        var catalog = new ContentCatalog(Array.Empty<CharacterSpec>(), new[] { husk });

        Assert.That(catalog.Enemy(husk.Id), Is.SameAs(husk));
        Assert.That(catalog.TryGetEnemy(husk.Id, out EnemySpec found), Is.True);
        Assert.That(found, Is.SameAs(husk));
        Assert.That(catalog.Enemies.Count, Is.EqualTo(1));
        Assert.That(catalog.Enemies[0], Is.SameAs(husk));

        // The id that finds it is a value, not the instance that was registered.
        Assert.That(catalog.Enemy(new ContentId(HuskId)), Is.SameAs(husk));

        // Unknown, default and absent all behave as they do for characters — one pair of
        // accessors per kind, with the same contract (M0-08).
        KeyNotFoundException ex = Assert.Throws<KeyNotFoundException>(
            () => catalog.Enemy(new ContentId(UnknownId)));
        Assert.That(ex.Message, Does.Contain(UnknownId));

        Assert.That(catalog.TryGetEnemy(new ContentId(UnknownId), out EnemySpec missing), Is.False);
        Assert.That(missing, Is.Null);
        Assert.Throws<KeyNotFoundException>(() => catalog.Enemy(default));

        // Omitting the list entirely is how M1-06 ships — EnemyDefinition arrives in M1-07 — and
        // it must read as "no enemies", not as a broken catalog.
        var charactersOnly = new ContentCatalog(new[] { Oathbound() });
        Assert.That(charactersOnly.Enemies, Is.Empty);
        Assert.Throws<KeyNotFoundException>(() => charactersOnly.Enemy(new ContentId(HuskId)));

        ArgumentException duplicate = Assert.Throws<ArgumentException>(
            () => new ContentCatalog(
                Array.Empty<CharacterSpec>(),
                new[] { Enemy(HuskId, EnemyBehaviourKind.Static), Enemy(HuskId, EnemyBehaviourKind.Chaser) }));

        Assert.That(duplicate.Message, Does.Contain(HuskId));

        Assert.Throws<ArgumentException>(
            () => new ContentCatalog(Array.Empty<CharacterSpec>(), new EnemySpec[] { null }));
    }

    [Test]
    public void SpawnPlan_Guards()
    {
        Assert.Throws<ArgumentNullException>(() => new SpawnPlan(null));

        // default(ContentId) names no archetype, and it is refused where the plan is built rather
        // than where it is spawned — left alone it would surface as the catalog's "no enemy with
        // id ''", pointing at content that was never at fault.
        Assert.Throws<ArgumentException>(() => new SpawnPlan.Entry(default, Vector3.Zero));

        // …and again on the list, because default(Entry) carries a zeroed id straight past the
        // constructor: a struct always has a zeroed form (M1-01's default(Modifier)).
        Assert.Throws<ArgumentException>(() => new SpawnPlan(new SpawnPlan.Entry[1]));

        // A NaN spawn position is permanent and silent: it becomes the agent's position, then its
        // DistanceToPlayer, and every comparison against it is false.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SpawnPlan.Entry(new ContentId(HuskId), new Vector3(float.NaN, 0f, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SpawnPlan.Entry(new ContentId(HuskId), new Vector3(0f, 0f, float.PositiveInfinity)));

        // One shared immutable instance, not a fresh plan per read — which is what makes it safe
        // to be static at all, with domain reload disabled on Play.
        Assert.That(SpawnPlan.Empty.Initial, Is.Empty);
        Assert.That(SpawnPlan.Empty, Is.SameAs(SpawnPlan.Empty));
    }

    [Test]
    public void SpawnPlan_CopiesInput()
    {
        var entries = new List<SpawnPlan.Entry>
        {
            new SpawnPlan.Entry(new ContentId(HuskId), Vector3.Zero),
        };

        var plan = new SpawnPlan(entries);
        entries.Clear();

        // The same guard ContentCatalog makes: a builder that keeps filling its own list
        // afterwards cannot change what the plan holds.
        Assert.That(plan.Initial.Count, Is.EqualTo(1));
        Assert.That(plan.Initial[0].SpecId, Is.EqualTo(new ContentId(HuskId)));
    }

    /// <summary>
    /// What <c>Tick</c> takes as of M2-07b: one struct rather than four arguments (rule 2).
    /// </summary>
    private EnemyTickContext Context(float now) =>
        new EnemyTickContext(Frame, now, _player, _intents, _events, _projectiles);

    /// <summary>
    /// Fills one enemy slot the way M1-07's view sync will fill it.
    /// </summary>
    /// <remarks>
    /// <c>ref</c> on both sides of the assignment, which is load-bearing: without it the caller
    /// gets a copy, every field below is written into nothing, and the test passes while proving
    /// the opposite of what it says (M0-05).
    /// </remarks>
    private static void AddSense(
        WorldSnapshot snapshot,
        int id,
        Vector3 position,
        Vector3 velocity = default,
        Vector2 path = default,
        bool hasLineOfSight = false)
    {
        ref EnemySense sense = ref snapshot.AddEnemy();

        sense.Id = id;
        sense.Position = position;
        sense.Velocity = velocity;
        sense.PathDirectionToPlayer = path;
        sense.HasLineOfSight = hasLineOfSight;
    }

    private static EnemySpec Enemy(string id, EnemyBehaviourKind behaviour) => new EnemySpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        // GD §8.1's Husk for the numbers that have a published value, M1-05's for the five that
        // are born in EnemySpec.
        maxHp: 36f,
        moveSpeed: 3.5f,
        targetPriority: 1,
        threatCost: 4,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: behaviour);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        120f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        // Required as of M1-13, and switched off with a MaxMultiplier of 1: no row here is
        // about the player's swing rate, and a live ramp would be background noise in a fixture
        // about enemies.
        new FocusSpec(0.4f, 1f, 1f),
        // Required as of M1-14, and inert here for the same reason: this fixture is about enemies,
        // and the player never dashes in any of its rows.
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound() },
        new[]
        {
            Enemy(HuskId, EnemyBehaviourKind.Static),
            Enemy(SpitterId, EnemyBehaviourKind.Static),
            Enemy(BloaterId, EnemyBehaviourKind.Static),
        },
        new[] { Descent() });

    /// <summary>
    /// Descent as this fixture needs it: endless, from stage 1, empty roster. Empty because
    /// <c>RunSession.Start</c> resolves every roster id against the catalog and no row here is
    /// about a schedule — the archetypes these rows spawn come from a <c>SpawnPlan</c>.
    /// </summary>
    private static ModeSpec Descent() => new ModeSpec(
        new ContentId(DescentId),
        new LocKey("mode.descent.name"),
        1,
        true,
        0,
        Scalings.Design(),
        Array.Empty<RosterEntry>());

    /// <summary>
    /// The depth scaling every <c>EnemySystem</c> here is built with, required as of M2-03.
    /// </summary>
    /// <remarks>
    /// GD §12's curves, so the rows about depth can state a real multiplier. Every other row is
    /// unaffected by construction rather than by luck: <c>Depth</c> defaults to 1, where GD §12.3's
    /// three multipliers are all exactly 1.
    /// </remarks>
    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());

    /// <summary>A session over this fixture's catalog, publishing into <paramref name="events"/>.</summary>
    private RunSession Session(IDomainEvents events) =>
        new RunSession(_catalog, new FixedRandom(SessionSeed), events, new RecordingIntents(), Capacity, DeviceCap, ProjectileCapacity);

    private EnemyBlackboard Blackboard(int id)
    {
        Assert.That(_system.Registry.TryGet(id, out EnemyAgent agent), Is.True, $"No enemy {id}.");
        return agent.Blackboard;
    }

    /// <summary>
    /// An <see cref="IDomainEvents"/> that hands each payload to a callback as it is published, so
    /// a test can look at the registry from *inside* the publish.
    /// </summary>
    /// <remarks>
    /// Nested and private, like <c>RunSessionTests</c>' equivalent and for the same reason:
    /// <c>RecordingEvents</c> records what was published, which says nothing about what was true
    /// at the moment it happened — and here that ordering is the rule. If a third fixture needs
    /// one, it belongs in <c>Fakes/</c>.
    /// </remarks>
    private sealed class CallbackEvents : IDomainEvents
    {
        /// <summary>Called with each payload, boxed. Boxing is fine here; this never ships.</summary>
        public Action<object> OnPublish { get; set; }

        public void Publish<T>(in T evt) where T : struct
        {
            OnPublish?.Invoke(evt);
        }
    }
}
