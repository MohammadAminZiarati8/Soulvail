using System;
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

[TestFixture]
public sealed class SidewinderViperTests
{
    private const float Frame = 1f / 120f;
    private const float Tolerance = 0.001f;
    private const float Range = 8f;
    private const float RecoverTime = 1f;
    private const float MissTime = 2f;
    private const float SidewindSpeed = 2.8f;
    private const float Arc = 45f;
    private const float Radius = 7f;
    private const float Timeout = 2.5f;
    private const int Capacity = 8;

    private RecordingEvents _events;
    private IDomainEvents _sink;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;
    private WorldSnapshot _snapshot;
    private EnemyAgent _agent;
    private LungerBehaviour _lunger;
    private Vector3 _hero;
    private Vector3 _position;
    private float _clock;
    private bool _petQuarry;

    [Test]
    public void NoSidewinder_LungerUnchanged()
    {
        Start(Enemy(null));
        Tick();
        _hero.X = 1f;

        AdvanceTo(LungerState.Recover);
        AdvanceTo(LungerState.Stalk);

        Assert.That(_events.Count<EnemyStrikeMissed>(), Is.Zero);
    }

    [Test]
    public void Spec_BadPairings_Throw()
    {
        Assert.That(() => Enemy(Sidewind(), behaviour: EnemyBehaviourKind.Chaser), Throws.ArgumentException);
        Assert.That(() => Enemy(Sidewind(), pack: new PackSpec(16f, 90f, 3f)), Throws.ArgumentException);
        Assert.That(() => Enemy(Sidewind(radius: Range)), Throws.ArgumentException);
        Assert.That(() => Enemy(Sidewind(missTime: RecoverTime - 0.1f)), Throws.ArgumentException);
        Assert.That(() => Enemy(Sidewind(missTime: RecoverTime)), Throws.Nothing);

        Assert.That(() => Sidewind(missTime: 0f), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => Sidewind(speed: float.PositiveInfinity), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => Sidewind(arc: 181f), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => Sidewind(radius: float.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => Sidewind(timeout: -1f), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => Sidewind(arc: 180f), Throws.Nothing);
    }

    [TestCase(0f)]
    [TestCase(2f)]
    public void HeroInStrip_Landed_ShortRecoil(float retreat)
    {
        Start();
        Tick();
        _hero.Z += retreat;
        _hero.Y = 3f;

        AdvanceTo(LungerState.Dash);
        Assert.That(_events.Count<LaneFired>(), Is.EqualTo(1));
        Assert.That(_events.Count<EnemyStrikeMissed>(), Is.Zero);
        AssertRecovery(RecoverTime);
    }

    [Test]
    public void HeroStepsOut_Missed_LongRecoil()
    {
        Start();
        Tick();
        _hero.X = 1f;

        AdvanceTo(LungerState.Dash);

        Assert.That(_events.Count<LaneFired>(), Is.EqualTo(1));
        EnemyStrikeMissed miss = _events.Single<EnemyStrikeMissed>();
        Assert.That(miss.Id, Is.EqualTo(_agent.Id));
        Assert.That(miss.Seconds, Is.EqualTo(MissTime));

        _hero.X = 0f;
        AssertRecovery(MissTime);
        Assert.That(_events.Count<EnemyStrikeMissed>(), Is.EqualTo(1));
    }

    [Test]
    public void PetQuarry_AlwaysLanded()
    {
        Start();
        _petQuarry = true;
        Tick();
        _hero.X = 1f;

        AdvanceTo(LungerState.Dash);
        Assert.That(_events.Count<LaneArmed>(), Is.Zero);
        Assert.That(_events.Count<EnemyStrikeMissed>(), Is.Zero);
        AssertRecovery(RecoverTime);
    }

    [Test]
    public void AfterRecover_Sidewinds()
    {
        Start();
        AdvanceTo(LungerState.Sidewind);

        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(SidewindSpeed).Within(Tolerance));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(new Vector2(0f, -1f)));
        Assert.That(_intents.CountEnemyMoves(_agent.Id), Is.EqualTo(1));
    }

    [Test]
    public void Sidewind_SteersTowardRadius()
    {
        Start();
        Vector2 start = BeginSidewind();
        Assert.That(start.Length(), Is.EqualTo(4f).Within(Tolerance));

        AdvanceTo(LungerState.Stalk);
        Assert.That(Bearing().Length(), Is.EqualTo(Radius).Within(0.5f));
    }

    [Test]
    public void Sidewind_EndsAtArc()
    {
        Start();
        Vector2 start = BeginSidewind();

        AdvanceTo(LungerState.Stalk);
        Assert.That(MathF.Abs(Swing(start, Bearing())), Is.EqualTo(Arc).Within(3f));
    }

    [Test]
    public void Sidewind_EndsAtTimeout()
    {
        Start();
        AdvanceTo(LungerState.Recover);
        AdvanceTo(LungerState.Sidewind, move: false);
        float start = _clock;

        AdvanceTo(LungerState.Stalk, move: false);
        Assert.That(_clock - start, Is.EqualTo(Timeout).Within(2f * Frame));
    }

    [Test]
    public void Sides_AlternateFromIdParity()
    {
        Start(even: true);
        Assert.That(_agent.Id % 2, Is.Zero);
        Assert.That(CompleteSidewind(), Is.GreaterThan(0f));
        Assert.That(CompleteSidewind(), Is.LessThan(0f));

        Start();
        Assert.That(_agent.Id % 2, Is.EqualTo(1));
        Assert.That(CompleteSidewind(), Is.LessThan(0f));
        Assert.That(CompleteSidewind(), Is.GreaterThan(0f));
    }

    [Test]
    public void StillHero_LanesDifferByTheArc()
    {
        Start();
        Tick();
        Vector2 first = _events.Single<LaneArmed>().Direction;

        AdvanceTo(LungerState.Sidewind);
        AdvanceTo(LungerState.Stalk);
        _events.Clear();
        Tick();

        Assert.That(_lunger.State, Is.EqualTo(LungerState.Windup));
        Vector2 second = _events.Single<LaneArmed>().Direction;
        Assert.That(MathF.Abs(Swing(first, second)), Is.GreaterThanOrEqualTo(40f));
    }

    [Test]
    public void Reset_MidSidewind_Stalks()
    {
        Start(even: true);
        CompleteSidewind();
        AdvanceTo(LungerState.Sidewind);
        Tick();

        _lunger.Reset();
        Assert.That(_lunger.State, Is.EqualTo(LungerState.Stalk));
        _position = new Vector3(0f, 0f, -Range);

        Assert.That(CompleteSidewind(), Is.EqualTo(Arc).Within(3f));
    }

    [Test]
    public void Tick_Sidewinder_NoAlloc()
    {
        Start(silent: true);
        Tick();
        _hero.X = 1f;

        // Warm each handler before measuring, then start the same missed cycle again.
        AdvanceTo(LungerState.Sidewind);
        Tick();
        _lunger.Reset();
        _position = new Vector3(0f, 0f, -Range);
        _hero = Vector3.Zero;
        Tick();
        _hero.X = 1f;

        // At 60 Hz, 300 frames span the coil, dash, miss recovery and part of the sidewind.
        AllocationAssert.None(() => Tick(dt: 2f * Frame), iterations: 300);

        Assert.That(_lunger.State, Is.EqualTo(LungerState.Sidewind));
    }

    private void Start(EnemySpec spec = null, bool even = false, bool silent = false)
    {
        spec ??= Enemy(Sidewind());
        _events = new RecordingEvents();
        _sink = silent ? new SilentEvents() : _events;
        _intents = new RecordingIntents();
        CharacterSpec hero = Hero();
        var catalog = new ContentCatalog(new[] { hero }, new[] { spec });
        _system = new EnemySystem(catalog, _sink, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
        _player = new PlayerCombat(hero, _sink, _intents, Capacity);
        _projectiles = new ProjectileSystem(_sink, Capacity);
        _lanes = new LaneSystem(_sink);
        _snapshot = new WorldSnapshot(Capacity);
        _clock = 0f;
        _petQuarry = false;
        _hero = Vector3.Zero;
        _position = new Vector3(0f, 0f, -Range);

        if (even)
        {
            _system.Spawn(spec.Id, new Vector3(40f, 0f, 40f));
        }

        _agent = _system.Spawn(spec.Id, _position);
        _lunger = (LungerBehaviour)_agent.Behaviour;
    }

    private void Tick(bool move = true, float dt = Frame)
    {
        _intents.Clear();
        _snapshot.Clear();
        _snapshot.Dt = dt;
        _snapshot.PlayerPosition = _hero;
        ref EnemySense sense = ref _snapshot.AddEnemy();
        sense.Id = _agent.Id;
        sense.Position = _position;
        sense.HasLineOfSight = true;
        _system.Ingest(_snapshot);
        _agent.Blackboard.QuarryPetId = _petQuarry ? 1 : 0;
        _player.Blackboard.PlayerPosition = _hero;

        _lanes.Tick(_clock, _hero, _player);
        _lunger.Tick(new EnemyTickContext(dt, _clock, _player, _intents, _sink, _projectiles, _system, _lanes));

        if (move)
        {
            _position += _intents.LastEnemyMove.Velocity * dt;
        }

        _clock += dt;
    }

    private void AdvanceTo(LungerState state, bool move = true)
    {
        for (int i = 0; i < 1200 && _lunger.State != state; i++)
        {
            Tick(move);
        }

        Assert.That(_lunger.State, Is.EqualTo(state));
    }

    private void AssertRecovery(float duration)
    {
        AdvanceTo(LungerState.Recover);
        float start = _clock;

        AdvanceTo(LungerState.Sidewind);

        Assert.That(_clock - start, Is.EqualTo(duration).Within(2f * Frame));
    }

    private Vector2 BeginSidewind()
    {
        AdvanceTo(LungerState.Recover);
        AdvanceTo(LungerState.Sidewind, move: false);
        return Bearing();
    }

    private float CompleteSidewind()
    {
        Vector2 start = BeginSidewind();
        AdvanceTo(LungerState.Stalk);
        return Swing(start, Bearing());
    }

    private Vector2 Bearing() => new Vector2(_position.X - _hero.X, _position.Z - _hero.Z);

    private static float Swing(Vector2 start, Vector2 end) =>
        MathF.Atan2((start.X * end.Y) - (start.Y * end.X), Vector2.Dot(start, end)) * (180f / MathF.PI);

    private static SidewinderSpec Sidewind(
        float missTime = MissTime, float speed = SidewindSpeed, float arc = Arc, float radius = Radius,
        float timeout = Timeout) => new SidewinderSpec(missTime, speed, arc, radius, timeout);

    private static EnemySpec Enemy(
        SidewinderSpec sidewinder, EnemyBehaviourKind behaviour = EnemyBehaviourKind.Lunger, PackSpec pack = null) =>
        new EnemySpec(
            new ContentId("enemy.viper"),
            new LocKey("enemy.viper.name"),
            maxHp: 44f,
            moveSpeed: 2.4f,
            targetPriority: 4,
            threatCost: 9,
            xpValue: 27f,
            isElite: false,
            contactDamage: 14f,
            reach: 1.2f,
            windupTime: 0.9f,
            recoverTime: RecoverTime,
            aggroRange: 30f,
            behaviour: behaviour,
            lunge: new LungeSpec(Range, 12f, 30f, 0.6f, 2f),
            pack: pack,
            sidewinder: sidewinder);

    private static CharacterSpec Hero() => new CharacterSpec(
        new ContentId("character.oathbound"),
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
