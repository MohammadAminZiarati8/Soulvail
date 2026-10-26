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

/// <summary>JG-07a: the Lunger stalks, warns with a lane, dashes down it and recovers.</summary>
[TestFixture]
public sealed class LungerBehaviourTests
{
    private const string PantherId = "enemy.panther";
    private const string OathboundId = "character.oathbound";

    private const int Capacity = 64;

    private const float Frame = 1f / 120f;

    private const float Tolerance = 1e-4f;

    // JG-07a's table: what JG-07d authors on Panther.asset.
    private const float MoveSpeed = 2.4f;
    private const float Damage = 14f;
    private const float WindupTime = 0.9f;
    private const float RecoverTime = 1f;
    private const float Range = 9f;
    private const float Length = 12f;
    private const float Speed = 24f;
    private const float HalfWidth = 0.6f;
    private const float Cooldown = 2f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;

    private Vector3 _playerPosition;

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
        _lanes = new LaneSystem(_events);
        _clock = 0f;
        _allocationClock = 0f;
    }

    // ---- Rule 1: stalk ------------------------------------------------------------------------------

    [Test]
    public void Lunger_OutOfRange_StalksAlongThePath()
    {
        LungerBehaviour lunger = Lunger(distance: 15f);

        Vector2 path = Vector2.Normalize(new Vector2(1f, 1f));

        Blackboard(lunger).PathDirectionToPlayer = path;

        Tick(lunger);

        Assert.That(lunger.State, Is.EqualTo(LungerState.Stalk));

        EnemyMoveIntent move = _intents.LastEnemyMove;

        Assert.That(move.Velocity.X, Is.EqualTo(path.X * MoveSpeed).Within(Tolerance));
        Assert.That(move.Velocity.Z, Is.EqualTo(path.Y * MoveSpeed).Within(Tolerance));
        Assert.That(move.FacingXZ, Is.EqualTo(path), "Path first, straight line second.");
        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero);
    }

    [Test]
    public void Lunger_OnCooldown_StalksEvenInRange()
    {
        LungerBehaviour lunger = Lunger(distance: 5f);

        Blackboard(lunger).NextAttackAt = 10f;

        Tick(lunger);
        Tick(lunger);

        Assert.That(lunger.State, Is.EqualTo(LungerState.Stalk));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero);
        Assert.That(_lanes.ActiveCount, Is.Zero);
    }

    // ---- Rule 2: wind-up ----------------------------------------------------------------------------

    [Test]
    public void Lunger_InRange_TelegraphsAndOpensOneLane()
    {
        LungerBehaviour lunger = Lunger(distance: 8f);

        Tick(lunger);

        Assert.That(lunger.State, Is.EqualTo(LungerState.Windup));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero), "It stops to warn.");

        EnemyTelegraph telegraph = _events.Single<EnemyTelegraph>();

        Assert.That(telegraph.Id, Is.EqualTo(Agent(lunger).Id));
        Assert.That(telegraph.Duration, Is.EqualTo(WindupTime).Within(Tolerance));

        LaneArmed armed = _events.Single<LaneArmed>();

        Assert.That(armed.Origin, Is.EqualTo(Agent(lunger).Position), "From where it stands.");
        Assert.That(armed.Direction.X, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(armed.Direction.Y, Is.EqualTo(1f).Within(Tolerance), "Toward the player.");
        Assert.That(armed.Length, Is.EqualTo(Length).Within(Tolerance));
        Assert.That(armed.HalfWidth, Is.EqualTo(HalfWidth).Within(Tolerance));
        Assert.That(armed.ArmSeconds, Is.EqualTo(WindupTime).Within(Tolerance));
    }

    // ---- Rule 3: committed --------------------------------------------------------------------------

    [Test]
    public void Lunger_WindingUp_KeepsItsDirection()
    {
        LungerBehaviour lunger = Lunger(distance: 8f);

        Tick(lunger);

        Assert.That(lunger.State, Is.EqualTo(LungerState.Windup), "Sanity: winding up.");

        // The player circles round to its side; the Lunger never turns.
        Blackboard(lunger).DirectionToPlayer = new Vector2(1f, 0f);

        while (lunger.State == LungerState.Windup)
        {
            Tick(lunger);

            Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(new Vector2(0f, 1f)));
        }

        Tick(lunger);

        Assert.That(lunger.State, Is.EqualTo(LungerState.Dash));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(new Vector2(0f, 1f)), "Nor while dashing.");
    }

    // ---- Rule 4: dash -------------------------------------------------------------------------------

    [Test]
    public void Lunger_AfterTheWindup_DashesItsLength()
    {
        LungerBehaviour lunger = WoundUp();

        float distance = 0f;
        int moving = 0;

        for (int ticks = 0; lunger.State == LungerState.Dash; ticks++)
        {
            Assert.That(ticks, Is.LessThan(200), "The dash ends.");

            Tick(lunger);

            EnemyMoveIntent move = _intents.LastEnemyMove;

            Assert.That(move.Velocity.X, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(move.Velocity.Z, Is.LessThanOrEqualTo(Speed + Tolerance), "Never faster than its speed.");

            distance += move.Velocity.Z * Frame;

            // The tick that hands over to Recover stands still; it is not dash time.
            if (move.Velocity.Z > 0f)
            {
                moving++;
            }
        }

        Assert.That(distance, Is.EqualTo(Length).Within(0.01f), "Exactly its lane's length.");
        Assert.That(moving * Frame, Is.EqualTo(Length / Speed).Within(1.5f * Frame), "At 24 m/s: half a second.");
    }

    [Test]
    public void Lunger_StandingInTheLane_IsHitOnceAsTheDashLeaves()
    {
        LungerBehaviour lunger = Lunger(distance: 8f);

        // Which tick the bite lands on and which the dash starts on. The lane's deadline is an
        // absolute second and the wind-up a running sum of frames, so the two may fall one tick
        // apart; more than that would be a strip that bites before or after the move it warns of.
        int hitTick = -1;
        int dashTick = -1;

        for (int tick = 0; tick < 360; tick++)
        {
            LungerState before = lunger.State;

            Tick(lunger);

            if (hitTick < 0 && _events.Count<PlayerDamaged>() > 0)
            {
                hitTick = tick;
            }

            if (dashTick < 0 && before == LungerState.Windup && lunger.State == LungerState.Dash)
            {
                dashTick = tick;
            }
        }

        Assert.That(dashTick, Is.GreaterThan(0), "Sanity: it dashed.");
        Assert.That(hitTick, Is.InRange(dashTick - 1, dashTick + 1), "The strip bites as the dash leaves.");

        // Three seconds: the dash and the recovery are over, and it was still one hit, so the
        // behaviour adds none of its own.
        PlayerDamaged hit = _events.Single<PlayerDamaged>();

        Assert.That(hit.ToShield + hit.ToHp, Is.EqualTo(Damage).Within(Tolerance));
    }

    [Test]
    public void Lunger_SteppedOutOfTheLane_IsNotHit()
    {
        LungerBehaviour lunger = Lunger(distance: 8f);

        Tick(lunger);

        Assert.That(lunger.State, Is.EqualTo(LungerState.Windup), "Sanity: the lane is down.");

        // One sidestep, wider than the half-width.
        _playerPosition = new Vector3(1.5f, 0f, 8f);

        TickFor(lunger, 2f);

        Assert.That(_events.Count<LaneFired>(), Is.EqualTo(1), "Sanity: the lane went off.");
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    // ---- Rule 5: recover ----------------------------------------------------------------------------

    [Test]
    public void Lunger_AfterTheDash_RecoversThenStalks()
    {
        LungerBehaviour lunger = WoundUp();

        while (lunger.State == LungerState.Dash)
        {
            Tick(lunger);
        }

        Assert.That(lunger.State, Is.EqualTo(LungerState.Recover));

        // The transition was taken on the tick before; that tick's clock is the dash's end.
        float dashEnd = _clock - Frame;

        Assert.That(Blackboard(lunger).NextAttackAt, Is.EqualTo(dashEnd + Cooldown).Within(Tolerance));

        float started = _clock;

        while (lunger.State == LungerState.Recover)
        {
            Tick(lunger);

            if (lunger.State == LungerState.Recover)
            {
                Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero), "It stands.");
            }
        }

        Assert.That(lunger.State, Is.EqualTo(LungerState.Stalk));
        Assert.That(_clock - started, Is.EqualTo(RecoverTime).Within(2f * Frame));
    }

    // ---- Rule 6: one intent a tick ------------------------------------------------------------------

    [Test]
    public void Lunger_EveryState_SendsOneIntent()
    {
        LungerBehaviour lunger = Lunger(distance: 8f);
        int id = Agent(lunger).Id;

        bool[] seen = new bool[4];

        for (int i = 0; i < 600; i++)
        {
            _intents.Clear();

            LungerState before = lunger.State;

            Tick(lunger);

            Assert.That(_intents.CountEnemyMoves(id), Is.EqualTo(1), $"In {before}.");

            seen[(int)before] = true;
        }

        Assert.That(seen, Is.All.True, "Every state was visited.");
    }

    // ---- Rule 7: refused at the spec ----------------------------------------------------------------

    [Test]
    public void Spec_Lunger_RefusesAShortWindupOrNoLunge()
    {
        Assert.That(() => Panther(windupTime: 0.5f), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => Panther(windupTime: WardenBehaviour.MinTelegraphSeconds), Throws.Nothing);
        Assert.That(() => Panther(lunge: false), Throws.ArgumentException);
        Assert.That(() => new LungeSpec(13f, Length, Speed, HalfWidth, Cooldown), Throws.TypeOf<ArgumentOutOfRangeException>(),
            "A range past the lane's end warns of a dash that cannot reach.");
        Assert.That(new LungeSpec(Range, Length, Speed, HalfWidth, Cooldown).DashSeconds, Is.EqualTo(0.5f).Within(Tolerance));
    }

    // ---- Rule 8: a full table still dashes ----------------------------------------------------------

    [Test]
    public void Lunger_FullLaneTable_StillDashes()
    {
        for (int i = 0; i < LaneSystem.Capacity; i++)
        {
            _lanes.Open(new Vector3(30f, 0f, 30f), new Vector3(1f, 0f, 0f), 5f, 0.5f, 60f, 1f, 0f);
        }

        _events.Clear();

        LungerBehaviour lunger = Lunger(distance: 8f);

        Tick(lunger);

        Assert.That(lunger.State, Is.EqualTo(LungerState.Windup));
        Assert.That(_events.Count<LaneArmed>(), Is.Zero, "The warning is dropped.");

        TickFor(lunger, WindupTime + (2f * Frame));

        Assert.That(lunger.State, Is.EqualTo(LungerState.Dash), "The move is not.");
    }

    // ---- Rule 9: no lanes, no Lunger ----------------------------------------------------------------

    [Test]
    public void Lunger_WithoutLanes_Throws()
    {
        LungerBehaviour lunger = Lunger(distance: 8f);

        var bare = new EnemyTickContext(Frame, _clock, _player, _intents, _events, _projectiles, _system);

        Assert.That(() => lunger.Tick(bare), Throws.InvalidOperationException.With.Message.Contains("RunSession"));
    }

    // ---- Rule 10: reset -----------------------------------------------------------------------------

    [Test]
    public void Lunger_Reset_IsAFreshLunger()
    {
        LungerBehaviour lunger = WoundUp();

        while (lunger.State != LungerState.Recover)
        {
            Tick(lunger);
        }

        lunger.Reset();

        Assert.That(lunger.State, Is.EqualTo(LungerState.Stalk));
        Assert.That(Blackboard(lunger).NextAttackAt, Is.Zero);
        Assert.That(Blackboard(lunger).StateTimer, Is.Zero);
    }

    // ---- Rule 11: no allocation ---------------------------------------------------------------------

    [Test]
    public void Lunger_Tick_AllocatesNothing()
    {
        // SilentEvents, for ChaserBehaviourTests' reason: the recording fakes box every payload.
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);
        var projectiles = new ProjectileSystem(silent, Capacity);
        var lanes = new LaneSystem(silent);
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), Scaling(), Capacity)
        {
            Depth = 40,
        };

        var snapshot = new WorldSnapshot(Capacity);
        snapshot.Dt = Frame;
        snapshot.PlayerPosition = Vector3.Zero;

        // Half in range, cycling wind-up, dash and recovery; half stalking from out of range.
        for (int i = 0; i < 16; i++)
        {
            float z = i < 8 ? 6f : 20f;

            EnemyAgent agent = system.Spawn(new ContentId(PantherId), new Vector3(0f, 0f, z));

            ref EnemySense sense = ref snapshot.AddEnemy();

            sense.Id = agent.Id;
            sense.Position = agent.Position;
            sense.Velocity = Vector3.Zero;
            sense.PathDirectionToPlayer = Vector2.Zero;
            sense.HasLineOfSight = true;
        }

        system.Ingest(snapshot);

        for (int i = 0; i < 1200; i++)
        {
            intents.Clear();
            lanes.Tick(i * Frame, new Vector3(50f, 0f, 50f), player);
            system.Tick(new EnemyTickContext(Frame, i * Frame, player, intents, silent, projectiles, system, lanes));
        }

        _allocationClock = 1200 * Frame;

        AllocationAssert.None(() =>
        {
            intents.Clear();

            _allocationClock += Frame;

            lanes.Tick(_allocationClock, new Vector3(50f, 0f, 50f), player);
            system.Tick(new EnemyTickContext(Frame, _allocationClock, player, intents, silent, projectiles, system, lanes));
        });
    }

    // ---- Rule 12: no tongue -------------------------------------------------------------------------

    [Test]
    public void Lunger_Lane_CarriesNoTongue()
    {
        LungerBehaviour lunger = Lunger(distance: 8f);

        Tick(lunger);

        Assert.That(_events.Single<LaneArmed>().Tongue, Is.False);
    }

    // ---- Fixture helpers ----------------------------------------------------------------------------

    /// <summary>A spawned panther at the origin, the player <paramref name="distance"/> along +Z.</summary>
    private LungerBehaviour Lunger(float distance)
    {
        EnemyAgent agent = _system.Spawn(new ContentId(PantherId), Vector3.Zero);

        Assert.That(agent.Behaviour, Is.InstanceOf<LungerBehaviour>(), "A Lunger archetype gets a LungerBehaviour.");

        agent.Blackboard.DistanceToPlayer = distance;
        agent.Blackboard.DirectionToPlayer = new Vector2(0f, 1f);

        _playerPosition = new Vector3(0f, 0f, distance);

        return (LungerBehaviour)agent.Behaviour;
    }

    /// <summary>The same at 8 m, ticked through its wind-up so the dash starts on the next tick.</summary>
    private LungerBehaviour WoundUp()
    {
        LungerBehaviour lunger = Lunger(distance: 8f);

        Tick(lunger);

        while (lunger.State == LungerState.Windup)
        {
            Tick(lunger);
        }

        Assert.That(lunger.State, Is.EqualTo(LungerState.Dash), "Sanity: the wind-up is over.");

        return lunger;
    }

    /// <summary>The lanes, then the behaviour, as <c>RunSession</c> orders them; one frame on.</summary>
    private void Tick(LungerBehaviour lunger)
    {
        _lanes.Tick(_clock, _playerPosition, _player);

        lunger.Tick(new EnemyTickContext(Frame, _clock, _player, _intents, _events, _projectiles, _system, _lanes));

        _clock += Frame;
    }

    private void TickFor(LungerBehaviour lunger, float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            Tick(lunger);
        }
    }

    private EnemyAgent Agent(LungerBehaviour lunger)
    {
        ReadOnlySpan<EnemyAgent> agents = _system.Registry.Alive;

        for (int i = 0; i < agents.Length; i++)
        {
            if (ReferenceEquals(agents[i].Behaviour, lunger))
            {
                return agents[i];
            }
        }

        Assert.Fail("No registered agent owns this behaviour.");

        return null;
    }

    private EnemyBlackboard Blackboard(LungerBehaviour lunger) => Agent(lunger).Blackboard;

    private static ContentCatalog Catalog() =>
        new ContentCatalog(new[] { Oathbound() }, new[] { Panther() });

    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());

    /// <summary>GD §8.1's Lunger on JG-07a's numbers.</summary>
    private static EnemySpec Panther(float windupTime = WindupTime, bool lunge = true) => new EnemySpec(
        new ContentId(PantherId),
        new LocKey("enemy.panther.name"),
        maxHp: 44f,
        moveSpeed: MoveSpeed,
        targetPriority: 4,
        threatCost: 9,
        xpValue: 27f,
        isElite: false,
        contactDamage: Damage,
        reach: 1.2f,
        windupTime: windupTime,
        recoverTime: RecoverTime,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Lunger,
        lunge: lunge ? new LungeSpec(Range, Length, Speed, HalfWidth, Cooldown) : null);

    /// <summary>The Oathbound of CC §7, Focus off — what the lane lands on.</summary>
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
