using System;
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
/// JG-21a's eight rules: a Chaser with a <see cref="LeapSpec"/> crouches, fixes and announces a
/// landing, flies there in its air time and bites the ring it lands in.
/// </summary>
/// <remarks>
/// Driven through <see cref="EnemySystem"/> with a hand-built snapshot and a body that integrates
/// the last move it was given, so crowd control and the one-frame position lag are the real ones.
/// The Bullfrog here does not walk (speed 0), so the band holds still until the leap moves it.
/// </remarks>
[TestFixture]
public sealed class BullfrogLeapTests
{
    private const string BullfrogId = "enemy.bullfrog";
    private const string FrogId = "enemy.frog";
    private const string OathboundId = "character.oathbound";

    private const int Capacity = 64;
    private const float Frame = 1f / 120f;

    private const float ContactDamage = 8f;

    // JG-21f's authored leap.
    private const float MinRange = 4f;
    private const float MaxRange = 9f;
    private const float CrouchSeconds = 0.6f;
    private const float AirSeconds = 0.5f;
    private const float Radius = 1.6f;
    private const float Multiplier = 1.5f;
    private const float Cooldown = 4f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private WorldSnapshot _snapshot;

    private EnemyAgent _agent;
    private Vector3 _body;
    private Vector3 _playerAt;
    private float _clock;
    private float _allocationClock;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), Scaling(), Capacity);
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, Capacity);
        _snapshot = new WorldSnapshot(Capacity);
        _clock = 0f;
        _allocationClock = 0f;
    }

    // ---- Rule 1 ---------------------------------------------------------------------------------

    [Test]
    public void Leap_StartsOnlyInBand()
    {
        Assert.That(CrouchesWithin(distance: 3f, seconds: 2f), Is.False, "3 m is inside the band.");

        SetUp();

        Assert.That(CrouchesWithin(distance: 6f, seconds: 2f), Is.True, "6 m is in the band.");

        SetUp();

        Assert.That(CrouchesWithin(distance: 12f, seconds: 2f), Is.False, "12 m is beyond the band.");
    }

    [Test]
    public void Leap_WaitsForCooldown()
    {
        Spawn(BullfrogId, playerDistance: 6f);

        TickUntil(ChaserState.Land, budgetSeconds: 3f);

        float landedAt = _clock - Frame;

        // Step 6 m off the landing spot, still in the band.
        _playerAt = _body + new Vector3(0f, 0f, 6f);

        while (_clock - landedAt < Cooldown - Frame)
        {
            Step();

            Assert.That(State, Is.Not.EqualTo(ChaserState.Crouch), $"Crouched {_clock - landedAt:F3} s after landing.");
        }

        TickUntil(ChaserState.Crouch, budgetSeconds: 0.1f);
    }

    // ---- Rule 2 ---------------------------------------------------------------------------------

    [Test]
    public void Crouch_FixesTargetAndAnnounces()
    {
        Spawn(BullfrogId, playerDistance: 6f);

        TickUntil(ChaserState.Crouch, budgetSeconds: 2f);

        // Mid-crouch the player sidesteps; the landing does not follow.
        TickFor(CrouchSeconds / 2f);
        _playerAt = new Vector3(3f, 0f, 6f);

        TickUntil(ChaserState.Land, budgetSeconds: 2f);

        Assert.That(_events.Count<EnemyLeapAimed>(), Is.EqualTo(1));

        EnemyLeapAimed aimed = _events.Single<EnemyLeapAimed>();

        Assert.That(aimed.Id, Is.EqualTo(_agent.Id));
        Assert.That(aimed.From, Is.EqualTo(Vector3.Zero));
        Assert.That(aimed.Target, Is.EqualTo(new Vector3(0f, 0f, 6f)));
        Assert.That(aimed.Radius, Is.EqualTo(Radius));
        Assert.That(aimed.CrouchSeconds, Is.EqualTo(CrouchSeconds));
        Assert.That(aimed.AirSeconds, Is.EqualTo(AirSeconds));
        Assert.That(Vector3.Distance(_body, aimed.Target), Is.LessThan(1e-3f), "It landed on the fixed target.");

        EnemyTelegraph tell = _events.Single<EnemyTelegraph>();

        Assert.That(tell.Duration, Is.EqualTo(CrouchSeconds));
        Assert.That(tell.Move, Is.EqualTo(1));
    }

    // ---- Rule 3 ---------------------------------------------------------------------------------

    [Test]
    public void Air_ArrivesInAirSeconds()
    {
        Spawn(BullfrogId, playerDistance: 7f);

        TickUntil(ChaserState.Crouch, budgetSeconds: 2f);

        int flightTicks = 0;

        for (int i = 0; i < 240 && State != ChaserState.Land; i++)
        {
            Step();

            if (_intents.LastEnemyMove.Velocity != Vector3.Zero)
            {
                flightTicks++;

                Assert.That(
                    _intents.LastEnemyMove.Velocity.Length(),
                    Is.LessThanOrEqualTo((7f / AirSeconds) + 1e-3f),
                    "Velocity is the distance over the air time.");
            }
        }

        Assert.That(State, Is.EqualTo(ChaserState.Land));
        Assert.That(flightTicks * Frame, Is.EqualTo(AirSeconds).Within(Frame + 1e-5f));
        Assert.That(Vector3.Distance(_body, new Vector3(0f, 0f, 7f)), Is.LessThan(1e-3f));
    }

    // ---- Rules 4 and 5 --------------------------------------------------------------------------

    [Test]
    public void Land_BitesInsideRing()
    {
        Spawn(BullfrogId, playerDistance: 6f);

        TickUntil(ChaserState.Recover, budgetSeconds: 3f);

        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));

        PlayerDamaged bite = _events.Single<PlayerDamaged>();

        Assert.That(bite.ToShield + bite.ToHp, Is.EqualTo(ContactDamage * Multiplier).Within(1e-4f));
    }

    [Test]
    public void Land_MissesOutsideRing()
    {
        Spawn(BullfrogId, playerDistance: 6f);

        TickUntil(ChaserState.Crouch, budgetSeconds: 2f);

        _playerAt = new Vector3(2f, 0f, 6f);

        TickUntil(ChaserState.Recover, budgetSeconds: 2f);

        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(0));
    }

    // ---- Rule 6 ---------------------------------------------------------------------------------

    [Test]
    public void Air_StunLandsWhereStopped()
    {
        Spawn(BullfrogId, playerDistance: 7f);

        TickUntil(ChaserState.Air, budgetSeconds: 2f);
        TickFor(AirSeconds / 2f);

        Assert.That(_system.Stun(_agent.Id, 1f), Is.True);

        Vector3 stoppedAt = _body;

        Assert.That(stoppedAt.Z, Is.InRange(2.5f, 4.5f), "Sanity: stunned about half way.");

        // The player stands at the stop, not the target.
        _playerAt = stoppedAt;

        TickUntil(ChaserState.Recover, budgetSeconds: 2f);

        Assert.That(Vector3.Distance(_body, stoppedAt), Is.LessThan(1e-3f), "It never resumed the flight.");
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));

        PlayerDamaged bite = _events.Single<PlayerDamaged>();

        Assert.That(bite.ToShield + bite.ToHp, Is.EqualTo(ContactDamage * Multiplier).Within(1e-4f));
    }

    // ---- Rule 7 ---------------------------------------------------------------------------------

    [Test]
    public void NoLeapSpec_IsTheOldChaser()
    {
        Spawn(FrogId, playerDistance: 6f);

        for (int i = 0; i < 360; i++)
        {
            Step();

            Assert.That(State, Is.Not.EqualTo(ChaserState.Crouch));
        }

        Assert.That(_events.Count<EnemyLeapAimed>(), Is.EqualTo(0));
        Assert.That(_agent.Spec.Leap, Is.Null);
    }

    // ---- Rule 8 ---------------------------------------------------------------------------------

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);
        var projectiles = new ProjectileSystem(silent, Capacity);
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), Scaling(), Capacity);
        var snapshot = new WorldSnapshot(Capacity);

        snapshot.Dt = Frame;
        snapshot.PlayerPosition = Vector3.Zero;

        // Sixteen Bullfrogs in the band; perception frozen, so each lands on its own count and
        // cycles crouch, air, land, recover and the cooldown.
        for (int i = 0; i < 16; i++)
        {
            float angle = i * MathF.PI / 8f;
            var at = new Vector3(MathF.Cos(angle) * 6f, 0f, MathF.Sin(angle) * 6f);

            EnemyAgent agent = system.Spawn(new ContentId(BullfrogId), at);

            AddSense(snapshot, agent.Id, at);
        }

        system.Ingest(snapshot);

        // Past a full cycle (1 + 0.6 + 0.5 + 0.6 + 4 s), so every transition has run once.
        for (int i = 0; i < 1200; i++)
        {
            intents.Clear();
            system.Tick(new EnemyTickContext(Frame, i * Frame, player, intents, silent, projectiles, system));
        }

        _allocationClock = 1200 * Frame;

        AllocationAssert.None(() =>
        {
            intents.Clear();

            _allocationClock += Frame;

            system.Tick(new EnemyTickContext(Frame, _allocationClock, player, intents, silent, projectiles, system));
        });
    }

    // ---- Fixture helpers ------------------------------------------------------------------------

    private ChaserState State => ((ChaserBehaviour)_agent.Behaviour).State;

    private void Spawn(string id, float playerDistance)
    {
        _body = Vector3.Zero;
        _playerAt = new Vector3(0f, 0f, playerDistance);
        _agent = _system.Spawn(new ContentId(id), _body);
    }

    private bool CrouchesWithin(float distance, float seconds)
    {
        Spawn(BullfrogId, distance);

        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            Step();

            if (State == ChaserState.Crouch)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One frame: the snapshot reports where the body is, the system ticks, and the body then moves
    /// by the velocity it was given — so the next tick sees it, as a run would.
    /// </summary>
    private void Step()
    {
        _snapshot.Clear();
        _snapshot.Dt = Frame;
        _snapshot.PlayerPosition = _playerAt;
        AddSense(_snapshot, _agent.Id, _body);

        _system.Ingest(_snapshot);

        _intents.Clear();
        _system.Tick(new EnemyTickContext(Frame, _clock, _player, _intents, _events, _projectiles, _system));

        if (_intents.CountEnemyMoves(_agent.Id) > 0)
        {
            _body += _intents.LastEnemyMove.Velocity * Frame;
        }

        _clock += Frame;
    }

    private void TickFor(float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            Step();
        }
    }

    private void TickUntil(ChaserState state, float budgetSeconds)
    {
        int frames = (int)MathF.Ceiling(budgetSeconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            if (State == state)
            {
                return;
            }

            Step();
        }

        Assert.That(State, Is.EqualTo(state), $"Still {State} after {frames} frames.");
    }

    private static void AddSense(WorldSnapshot snapshot, int id, Vector3 position)
    {
        ref EnemySense sense = ref snapshot.AddEnemy();

        sense.Id = id;
        sense.Position = position;
        sense.Velocity = Vector3.Zero;
        sense.PathDirectionToPlayer = Vector2.Zero;
        sense.HasLineOfSight = true;
    }

    private static ContentCatalog Catalog() =>
        new ContentCatalog(new[] { Oathbound() }, new[] { Bullfrog(), Frog() });

    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());

    /// <summary>The Husk's fight with JG-21f's leap, and no walk, so the band holds still.</summary>
    private static EnemySpec Bullfrog() => new EnemySpec(
        new ContentId(BullfrogId),
        new LocKey("enemy.bullfrog.name"),
        maxHp: 60f,
        moveSpeed: 0f,
        targetPriority: 1,
        threatCost: 6,
        xpValue: 12f,
        isElite: false,
        contactDamage: ContactDamage,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Chaser,
        leap: new LeapSpec(MinRange, MaxRange, CrouchSeconds, AirSeconds, Radius, Multiplier, Cooldown));

    /// <summary>A hopping Chaser with no leap: the Frog as it was.</summary>
    private static EnemySpec Frog() => new EnemySpec(
        new ContentId(FrogId),
        new LocKey("enemy.frog.name"),
        maxHp: 20f,
        moveSpeed: 4f,
        targetPriority: 1,
        threatCost: 2,
        xpValue: 6f,
        isElite: false,
        contactDamage: ContactDamage,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Chaser,
        hopTime: 0.3f,
        hopRestTime: 0.2f);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);
}
