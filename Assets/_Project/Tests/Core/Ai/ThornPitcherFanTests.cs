using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// JG-21c: a Spitter whose <see cref="ProjectileSpec.Count"/> is above one throws a fan of ringed
/// lobs landing in a line across the throw. Rules 1–6 and the spec's refusals.
/// </summary>
/// <remarks>
/// The Thorn pitcher's authored block (count 3, spread 3 m, radius 1.4 m) on the Spitter's other
/// numbers; the player stands due north, so "across the throw" is the X axis.
/// </remarks>
[TestFixture]
public sealed class ThornPitcherFanTests
{
    private const string ThornId = "enemy.thorn_pitcher";
    private const string PitcherId = "enemy.pitcher";
    private const string OathboundId = "character.oathbound";

    private const int Capacity = 32;
    private const int ProjectileCapacity = 256;
    private const float Frame = 1f / 120f;

    private const float WindupTime = 0.7f;
    private const float RecoverTime = 0.9f;
    private const float Standoff = 14f;
    private const float FlightSpeed = 12f;
    private const float Radius = 1.4f;
    private const int FanCount = 3;
    private const float Spread = 3f;

    /// <summary>Inside the standoff band, so a fresh agent plants and aims.</summary>
    private const float Distance = 12f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private float _clock;
    private float _allocationClock;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), Scaling(), Capacity);
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, ProjectileCapacity);
        _clock = 0f;
        _allocationClock = 0f;
    }

    // ---- Rule 1: landings ----------------------------------------------------------------------

    [Test]
    public void Fan_LandsAcrossTheThrow()
    {
        EnemyAgent agent = Spawn(ThornId, Vector3.Zero);

        Release(agent);

        IReadOnlyList<ProjectileFired> fired = _events.Of<ProjectileFired>();
        var xs = new float[fired.Count];

        for (int i = 0; i < fired.Count; i++)
        {
            Assert.That(fired[i].Target.Z, Is.EqualTo(Distance).Within(1e-4f), "Every lob lands on the player's row.");

            xs[i] = fired[i].Target.X;
        }

        Array.Sort(xs);

        Assert.That(xs, Is.EqualTo(new[] { -Spread, 0f, Spread }).Within(1e-4f));
    }

    // ---- Rule 2: one release -------------------------------------------------------------------

    [Test]
    public void Fan_FiresAllInOneTick()
    {
        EnemyAgent agent = Spawn(ThornId, Vector3.Zero);
        var thrower = (SpitterBehaviour)agent.Behaviour;

        TickUntil(agent, SpitterState.Release, WindupTime + 1f);

        IReadOnlyList<ProjectileFired> fired = _events.Of<ProjectileFired>();

        Assert.That(fired.Count, Is.EqualTo(FanCount), "All three leave on the tick the aim completes.");

        for (int i = 0; i < fired.Count; i++)
        {
            Assert.That(fired[i].SourceId, Is.EqualTo(agent.Id));
            Assert.That(fired[i].Radius, Is.EqualTo(Radius), "Each draws its own ring.");
        }

        _events.Clear();
        Tick(agent);

        Assert.That(thrower.State, Is.EqualTo(SpitterState.Recover));
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero, "Nothing more after the release tick.");
    }

    // ---- Rule 3: each lob hits on its own ------------------------------------------------------

    [Test]
    public void Fan_EachLobHitsAlone()
    {
        EnemyAgent agent = Spawn(ThornId, Vector3.Zero);
        Vector3 player = agent.Position + new Vector3(0f, 0f, Distance);

        Release(agent);

        float flight = _events.Of<ProjectileFired>()[0].FlightTime;

        _events.Clear();
        _projectiles.Tick(_clock + (2f * flight), player, _player, _system);

        IReadOnlyList<ProjectileImpacted> impacts = _events.Of<ProjectileImpacted>();
        int hits = 0;

        for (int i = 0; i < impacts.Count; i++)
        {
            hits += impacts[i].Hit ? 1 : 0;
        }

        Assert.That(impacts.Count, Is.EqualTo(FanCount));
        Assert.That(hits, Is.EqualTo(1), "Only the middle ring covers the player at a 3 m spread.");
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));
    }

    // ---- Rule 4: a full table ------------------------------------------------------------------

    [Test]
    public void Fan_FullTableDropsOutsideFirst()
    {
        _projectiles = new ProjectileSystem(_events, 3);

        // One shot already in the air leaves two slots.
        _projectiles.Fire(
            new Projectile(new ContentId(PitcherId), 999, new Vector3(50f, 0f, 0f), new Vector3(50f, 0f, 60f),
                FlightSpeed, Radius, 1f),
            _clock);

        EnemyAgent agent = Spawn(ThornId, Vector3.Zero);

        _events.Clear();

        Assert.DoesNotThrow(() => Release(agent));

        IReadOnlyList<ProjectileFired> fired = _events.Of<ProjectileFired>();

        Assert.That(fired.Count, Is.EqualTo(2), "What fits is fired, the rest dropped.");
        Assert.That(fired[0].Target.X, Is.EqualTo(0f).Within(1e-4f), "The middle goes first.");
        Assert.That(MathF.Abs(fired[1].Target.X), Is.EqualTo(Spread).Within(1e-4f), "Then one side.");
    }

    // ---- Rule 5: count one ---------------------------------------------------------------------

    [Test]
    public void CountOne_IsTheOldSpitter()
    {
        EnemyAgent agent = Spawn(PitcherId, Vector3.Zero);

        Release(agent);

        ProjectileFired fired = _events.Single<ProjectileFired>();

        Assert.That(fired.Target, Is.EqualTo(agent.Position + new Vector3(0f, 0f, Distance)));
    }

    // ---- Rule 6: no allocation -----------------------------------------------------------------

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);
        var projectiles = new ProjectileSystem(silent, ProjectileCapacity);
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), Scaling(), Capacity);
        var agents = new EnemyAgent[8];

        for (int i = 0; i < agents.Length; i++)
        {
            agents[i] = system.Spawn(new ContentId(ThornId), new Vector3(i * 3f, 0f, 0f));
            Place(agents[i]);
        }

        // Nowhere near a landing, so every lob misses and the player lives.
        var far = new Vector3(0f, 0f, -500f);

        for (int f = 0; f < 600; f++)
        {
            StepAll(agents, intents, silent, player, projectiles, system, f * Frame, far);
        }

        _allocationClock = 600 * Frame;

        AllocationAssert.None(() =>
        {
            _allocationClock += Frame;
            StepAll(agents, intents, silent, player, projectiles, system, _allocationClock, far);
        });
    }

    // ---- The spec's refusals -------------------------------------------------------------------

    [TestCase(0)]
    [TestCase(6)]
    [TestCase(-1)]
    public void Spec_RefusesCountOutsideOneToFive(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProjectileSpec(Standoff, FlightSpeed, Radius, count, Spread));

    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Spec_RefusesBadSpread(float spread) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProjectileSpec(Standoff, FlightSpeed, Radius, 1, spread));

    [Test]
    public void Spec_RefusesFanWithoutSpread() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProjectileSpec(Standoff, FlightSpeed, Radius, FanCount, 0f));

    [Test]
    public void Spec_DefaultsToOneLob()
    {
        var spec = new ProjectileSpec(Standoff, FlightSpeed, Radius);

        Assert.That(spec.Count, Is.EqualTo(1));
        Assert.That(spec.Spread, Is.Zero);
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static void StepAll(
        EnemyAgent[] agents,
        RecordingIntents intents,
        SilentEvents events,
        PlayerCombat player,
        ProjectileSystem projectiles,
        EnemySystem system,
        float now,
        Vector3 playerPosition)
    {
        intents.Clear();

        var ctx = new EnemyTickContext(Frame, now, player, intents, events, projectiles, system);

        for (int i = 0; i < agents.Length; i++)
        {
            agents[i].Behaviour.Tick(ctx);
        }

        projectiles.Tick(now, playerPosition, player, system);
    }

    private EnemyAgent Spawn(string id, Vector3 position)
    {
        EnemyAgent agent = _system.Spawn(new ContentId(id), position);

        Assert.That(agent.Behaviour, Is.InstanceOf<SpitterBehaviour>());

        Place(agent);

        return agent;
    }

    /// <summary>The player <see cref="Distance"/> due north, in open ground (SpitterBehaviourTests.Place).</summary>
    private static void Place(EnemyAgent agent)
    {
        EnemyBlackboard blackboard = agent.Blackboard;

        blackboard.SelfPosition = agent.Position;
        blackboard.PlayerPosition = agent.Position + new Vector3(0f, 0f, Distance);
        blackboard.DistanceToPlayer = Distance;
        blackboard.DirectionToPlayer = new Vector2(0f, 1f);
        blackboard.HasLineOfSight = true;
    }

    private void Release(EnemyAgent agent) => TickUntil(agent, SpitterState.Release, WindupTime + 1f);

    private void Tick(EnemyAgent agent)
    {
        agent.Behaviour.Tick(new EnemyTickContext(Frame, _clock, _player, _intents, _events, _projectiles, _system));

        _clock += Frame;
    }

    private void TickUntil(EnemyAgent agent, SpitterState state, float budgetSeconds)
    {
        var thrower = (SpitterBehaviour)agent.Behaviour;
        int frames = (int)MathF.Ceiling(budgetSeconds / Frame);

        for (int i = 0; i < frames && thrower.State != state; i++)
        {
            Tick(agent);
        }

        Assert.That(thrower.State, Is.EqualTo(state), "Sanity: the fixture reached the state it is about.");
    }

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound() },
        new[]
        {
            Enemy(ThornId, new ProjectileSpec(Standoff, FlightSpeed, Radius, FanCount, Spread)),
            Enemy(PitcherId, new ProjectileSpec(Standoff, FlightSpeed, Radius)),
        });

    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());

    private static EnemySpec Enemy(string id, ProjectileSpec projectile) => new EnemySpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        maxHp: 28f,
        moveSpeed: 2.8f,
        targetPriority: 3,
        threatCost: 7,
        xpValue: 21f,
        isElite: false,
        contactDamage: 12f,
        reach: 1.2f,
        windupTime: WindupTime,
        recoverTime: RecoverTime,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Spitter,
        projectile: projectile);

    /// <summary>The fixture's Oathbound, no Aegis, so a landing's damage is visible.</summary>
    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 16f, 4f, 0.05f),
        null,
        0.5f);
}
