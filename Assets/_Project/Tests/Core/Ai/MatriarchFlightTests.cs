using System;
using System.Numerics;
using System.Reflection;
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
public sealed class MatriarchFlightTests
{
    private delegate void ResolveRain(in EnemyTickContext context, Vector3 hero);

    private static readonly ContentId BroodId = new("enemy.vulture");
    private static readonly ContentId BossId = new("boss.carrion_matriarch");
    private static readonly Vector3 Landing = new(1f, 0f, 8f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private EnemySystem _enemies;
    private EnemyAgent _boss;
    private BossBehaviour _wrapper;
    private ResolveRain _resolveRain;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;
    private WorldSnapshot _world;
    private Vector3[] _open;
    private Vector3 _hero;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        Start(_events);
    }

    [TestCase(34f, 1)]
    [TestCase(68f, 2)]
    public void Brood_UnlocksOnPhaseAndCapsLiveSummons(float damage, int phase)
    {
        Tick(0f);
        Assert.That(LastArm().Move, Is.EqualTo(MatriarchMove.RuinShove));
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(1));
        _open = Array.Empty<Vector3>();
        EnterFlightPhase(damage, phase);
        Assert.That(_events.Count<MatriarchTellCleared>(), Is.EqualTo(1));
        Assert.That(_events.Count<MatriarchFlightChanged>(), Is.Zero);

        _open = new[] { new Vector3(4f, 0f, 0f), new Vector3(-4f, 0f, 0f), new Vector3(0f, 0f, -4f) };
        Tick(0.75f);
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(1));
        Tick(0.25f);
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(3));
        Assert.That(_events.Single<MatriarchFlightChanged>().BossId, Is.EqualTo(_boss.Id));
        Assert.That(_events.Single<MatriarchFlightChanged>().Airborne, Is.True);
        foreach (EnemyAgent add in _enemies.Registry.Alive[1..])
        {
            Assert.That(add.IsSummon, Is.True);
            Assert.That(add.PaysXp, Is.False);
            Assert.That(add.Spec.Id, Is.EqualTo(BroodId));
            Assert.That(add.Position, Is.EqualTo(_open[add.Id - _boss.Id - 1]));
        }
    }

    [TestCase(0f, false, false, 84f)]
    [TestCase(3f, true, false, 84f)]
    [TestCase(3.01f, true, true, 100f)]
    public void Flight_LocksRainOnRealHeroAndHitsOnce(float offset, bool cover, bool petQuarry, float hp)
    {
        if (cover)
        {
            Start(_events, (from, to) => Vector3.Lerp(from, to, 0.5f));
        }

        Vector3 second = new(-4f, 0f, 0f);
        _open = new[] { _open[0], second };
        EnterFlightPhase();
        KillAdd(_enemies.Registry.Alive[1]);
        AssertAloft();

        _boss.Blackboard.IsBehindCover = cover;
        _boss.Blackboard.QuarryIsAPet = petQuarry;
        _boss.Blackboard.QuarryIsADecoy = !petQuarry;
        Vector3 centre = _hero;
        Tick(3.2f, quarry: new Vector3(-10f, 0f, -10f));
        MatriarchArmed rain = LastArm();
        Assert.That(rain.Move, Is.EqualTo(MatriarchMove.FeatherRain));
        Assert.That(rain.Target, Is.EqualTo(centre));
        Assert.That(rain.Width, Is.Zero);
        Assert.That(rain.Radius, Is.EqualTo(3f));
        Assert.That(rain.Seconds, Is.EqualTo(1.2f));
        Assert.That(LastProgress(), Is.Zero);

        Vector3 before = _boss.Position;
        _hero = centre + Vector3.UnitX * 6f;
        Tick(1.19f);
        Assert.That(_boss.Position, Is.Not.EqualTo(before));
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_events.Count<MatriarchImpact>(), Is.Zero);

        _hero = centre + new Vector3(offset, 5f, 0f);
        Tick(0.02f);
        Assert.That(_events.Single<MatriarchImpact>().Position, Is.EqualTo(centre));
        Assert.That(_events.Single<MatriarchImpact>().Move, Is.EqualTo(MatriarchMove.FeatherRain));
        Assert.That(LastProgress(), Is.EqualTo(1f));
        Tick(1f);
        Assert.That(_player.Health.Current, Is.EqualTo(hp));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(hp < 100f ? 1 : 0));
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Assert.That(_events.Count<MatriarchTellCleared>(), Is.EqualTo(1));
        AssertAloft();
    }

    [Test]
    public void Flight_KillAfterEnemyTickOnRainDeadlineCancelsImpact()
    {
        EnterFlightPhase();
        Tick(3.2f);
        Assert.That(LastArm().Move, Is.EqualTo(MatriarchMove.FeatherRain));
        Tick(1.2f, beforeRain: () => KillAdd(_enemies.Registry.Alive[1]));

        Assert.That(_events.Count<MatriarchTellCleared>(), Is.EqualTo(1));
        Assert.That(_events.Count<MatriarchImpact>(), Is.Zero);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(LastArm().Move, Is.EqualTo(MatriarchMove.CarrionDive));
        Assert.That(LastProgress(), Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Flight_OrbitsRealHeroDuringFeatherTell(bool petQuarry)
    {
        EnterFlightPhase();
        Tick(0.125f);
        Assert.That(_boss.Position, Is.EqualTo(Vector3.UnitZ * 0.75f));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitY));

        Tick(3.2f);
        Assert.That(LastArm().Move, Is.EqualTo(MatriarchMove.FeatherRain));
        Assert.That(Vector3.Distance(_boss.Position, _hero), Is.EqualTo(7f).Within(0.0001f));
        Vector2 previousHeading = _intents.LastEnemyMove.FacingXZ;
        _boss.Blackboard.QuarryIsAPet = petQuarry;
        _boss.Blackboard.QuarryIsADecoy = !petQuarry;
        _hero = _boss.Position - Vector3.UnitZ * 7f;
        _player.Blackboard.PlayerPosition = _hero;
        Vector3 before = _boss.Position;
        const float step = 0.25f;
        float angle = (_boss.Id % 2 == 0 ? 1f : -1f) * (4f * step / 7f);
        Vector3 expected = _hero + new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle)) * 7f;
        Tick(step, quarry: new Vector3(-10f, 0f, -10f));

        Assert.That(Vector3.Distance(_boss.Position, expected), Is.LessThan(0.0001f));
        Vector3 velocity = (expected - before) / step;
        Assert.That(Vector3.Distance(_intents.LastEnemyMove.Velocity, velocity), Is.LessThan(0.0001f));
        Vector2 heading = Vector2.Normalize(new Vector2(velocity.X, velocity.Z));
        Assert.That(Vector2.Distance(_intents.LastEnemyMove.FacingXZ, heading), Is.LessThan(0.0001f));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.Not.EqualTo(previousHeading));
        Assert.That(LastProgress(), Is.InRange(0.01f, 0.99f));
        AssertAloft();
    }

    [TestCase(false, false, 68f, 1)]
    [TestCase(true, false, 100f, 0)]
    [TestCase(false, true, 100f, 1)]
    public void LastSummonDeath_StartsOneFullDive(bool dodge, bool defend, float hp, int hitAttempts)
    {
        EnterFlightPhase();
        Tick(3.1f);
        Assert.That(_events.Count<MatriarchArmed>(), Is.Zero);
        Tick(0.2f);
        Assert.That(LastArm().Move, Is.EqualTo(MatriarchMove.FeatherRain));
        Vector3 diveOrigin = _boss.Position;
        KillAdd(_enemies.Registry.Alive[1]);
        Assert.That(_enemies.PendingXp, Is.Zero);
        Tick(1.2f);
        MatriarchArmed dive = LastArm();
        Assert.That(dive.Move, Is.EqualTo(MatriarchMove.CarrionDive));
        Assert.That(dive.Origin, Is.EqualTo(diveOrigin));
        Assert.That(dive.Target, Is.EqualTo(Landing));
        Assert.That(dive.Radius, Is.EqualTo(3.2f));
        Assert.That(dive.Seconds, Is.EqualTo(1.5f));
        Assert.That(LastProgress(), Is.Zero);
        Assert.That(_events.Count<MatriarchTellCleared>(), Is.EqualTo(1));
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        AssertAloft();
        Vector2 lockedHeading = _intents.LastEnemyMove.FacingXZ;

        _hero = dodge ? new Vector3(-4f, 0f, 0f) : Landing + Vector3.UnitX * 3f;
        Tick(0.75f);
        Assert.That(LastProgress(), Is.EqualTo(0.5f));
        Assert.That(_events.Count<MatriarchImpact>(), Is.Zero);
        Assert.That(_intents.LastEnemyMove.LandingPosition, Is.Null);
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(lockedHeading));
        AssertAloft();

        if (defend)
        {
            float impactTime = _now + 0.75f;
            _player.Charge.Request(impactTime);
            _player.Tick(0f, impactTime, _world, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);
            Assert.That(_player.Charge.IsInvulnerable, Is.True);
        }

        Tick(0.75f);
        Assert.That(LastProgress(), Is.EqualTo(1f));
        Assert.That(_intents.LastEnemyMove.LandingPosition, Is.EqualTo(Landing));
        Assert.That(_boss.Position, Is.EqualTo(Landing));
        Assert.That(_events.Single<MatriarchImpact>().Position, Is.EqualTo(Landing));
        Assert.That(_events.Of<MatriarchFlightChanged>()[^1].Airborne, Is.False);
        Assert.That(_boss.IsHidden, Is.False);
        Assert.That(_boss.IsVulnerable, Is.True);
        Assert.That(_enemies.ApplyDamage(_boss.Id, 1f, _now, _player).Applied, Is.EqualTo(1f));
        Tick(1f);
        Assert.That(_player.Health.Current, Is.EqualTo(hp));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(hitAttempts));
        Assert.That(_events.Count<MatriarchImpact>(), Is.EqualTo(1));
        Assert.That(_events.Count<MatriarchArmed>(), Is.EqualTo(2));
    }

    [Test]
    public void DiveImpact_StartsFullCooldown()
    {
        EnterFlightPhase();
        Tick(30f);
        AssertAloft();
        KillAdd(_enemies.Registry.Alive[1]);
        Tick(30f);
        Tick(30f);
        _hero = new Vector3(0f, 0f, -8f);
        Tick(2.25f);
        Assert.That(LastArm().Move, Is.EqualTo(MatriarchMove.CarrionDive));
        Tick(0.25f);
        Assert.That(LastArm().Move, Is.EqualTo(MatriarchMove.RuinShove));

        _enemies.ApplyDamage(_boss.Id, 34f, _now, _player);
        Tick(0f);
        Assert.That(_wrapper.IsInBeat, Is.True);
        Assert.That(_events.Count<MatriarchTellCleared>(), Is.EqualTo(3));
        Tick(15.25f);
        Assert.That(_wrapper.Phase, Is.EqualTo(2));
        Assert.That(_events.Count<MatriarchFlightChanged>(), Is.EqualTo(2));
        Assert.That(LastArm().Move, Is.EqualTo(MatriarchMove.RuinShove));

        Tick(0.25f);
        Assert.That(_events.Count<MatriarchFlightChanged>(), Is.EqualTo(2));
        Tick(0.75f);
        Tick(0.5f);
        Tick(1.1f);
        Tick(0.5f);
        Tick(1.1f);
        Assert.That(_events.Count<MatriarchFlightChanged>(), Is.EqualTo(2));
        Tick(1.4f);
        Assert.That(_events.Count<MatriarchFlightChanged>(), Is.EqualTo(3));
        AssertAloft();
    }

    [TestCase("death")]
    [TestCase("teardown")]
    [TestCase("reset")]
    [TestCase("clear")]
    public void Cleanup_RemovesOwnedAddsAndPendingAttacks(string cleanup)
    {
        EnterFlightPhase();
        int addId = _enemies.Registry.Alive[1].Id;
        int unrelated = _enemies.SpawnSummon(BroodId, new Vector3(-4f, 0f, -4f)).Id;
        Tick(4f);
        Assert.That(LastArm().Move, Is.EqualTo(MatriarchMove.FeatherRain));
        if (cleanup == "death")
        {
            // Model a forced death while a tell is pending.
            _boss.Health.SetExternalInvulnerable(false);
            Assert.That(_enemies.ApplyDamage(_boss.Id, 1000f, _now, _player).Killed, Is.True);
        }
        else if (cleanup == "teardown")
        {
            _enemies.Clear();
        }
        else if (cleanup == "reset")
        {
            _wrapper.Reset();
        }
        else
        {
            ((MatriarchBehaviour)_wrapper.Inner).ClearAdds(Context(0f, _events));
        }

        Assert.That(_boss.IsHidden, Is.False);
        Assert.That(_events.Of<MatriarchFlightChanged>()[^1].Airborne, Is.False);
        Assert.That(_events.Count<MatriarchTellCleared>(), Is.EqualTo(1));
        if (cleanup != "teardown")
        {
            Assert.That(_enemies.Registry.TryGet(addId, out _), Is.True);
        }

        _open = Array.Empty<Vector3>();
        _enemies.Tick(Context(0f, _events));
        Assert.That(_enemies.Registry.TryGet(addId, out _), Is.False);
        Assert.That(_enemies.Registry.TryGet(unrelated, out _), Is.EqualTo(cleanup != "teardown"));
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Assert.That(_events.Count<MatriarchImpact>(), Is.Zero);
    }

    [Test]
    public void FlightTick_AllocatesNothing()
    {
        var silent = new SilentEvents();
        Start(silent);
        EnterFlightPhase(events: silent);
        Tick(4f, silent);
        MatriarchBehaviour behaviour = (MatriarchBehaviour)_wrapper.Inner;
        EnemyTickContext feather = Context(0.01f, silent);
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            behaviour.Tick(feather);
        }, 100);

        KillAdd(_enemies.Registry.Alive[1]);
        Tick(0f, silent);
        Assert.That(_boss.IsVulnerable, Is.False);
        EnemyTickContext dive = Context(0.001f, silent);
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            behaviour.Tick(dive);
        }, 100);
    }

    private void Start(IDomainEvents events, Func<Vector3, Vector3, Vector3?> traceCover = null)
    {
        _now = 0f;
        _hero = new Vector3(0f, 0f, 8f);
        _open = new[] { new Vector3(4f, 0f, 0f), Landing };
        _intents = new RecordingIntents();
        CharacterSpec hero = Character();
        _player = new PlayerCombat(hero, events, _intents, 8);
        _projectiles = new ProjectileSystem(events, 8, traceCover: traceCover);
        _lanes = new LaneSystem(events);
        _world = new WorldSnapshot(8);
        var body = new EnemySpec(new ContentId("enemy.carrion_matriarch"), new LocKey("enemy.carrion_matriarch.name"),
            100f, 2.2f, 8, 40, 300f, false, 20f, 9.5f, 1.35f, 1.8f, 40f, EnemyBehaviourKind.Boss);
        var vulture = new EnemySpec(BroodId, new LocKey("enemy.vulture.name"),
            50f, 0f, 6, 14, 31f, false, 0f, 1f, 0f, 0f, 40f, EnemyBehaviourKind.Vulture,
            dive: new DiveSpec(7f, 4f, 6f, 4f, 6f, 1f, 10f, 0.6f, 0.4f, 14f, 2f, 0.6f));
        var boss = new BossSpec(BossId, body.Id,
            new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.66f), new BossPhaseSpec(0.33f) },
            1f, usesAuthoredMaxHp: true);
        var catalog = new ContentCatalog(new[] { hero }, new[] { body, vulture }, bosses: new[] { boss });
        _enemies = new EnemySystem(catalog, events, new FixedRandom(0.5f), new DepthScaling(Scalings.Design()), 8,
            (agent, _) => new MatriarchBehaviour(agent, new MatriarchStats(), BroodId));
        _boss = _enemies.SpawnBoss(BossId, Vector3.Zero);
        _wrapper = (BossBehaviour)_boss.Behaviour;
        _resolveRain = (ResolveRain)typeof(MatriarchBehaviour)
            .GetMethod(nameof(ResolveRain), BindingFlags.Instance | BindingFlags.NonPublic)
            .CreateDelegate(typeof(ResolveRain), _wrapper.Inner);
    }

    private void EnterFlightPhase(float damage = 34f, int phase = 1, IDomainEvents events = null)
    {
        _enemies.ApplyDamage(_boss.Id, damage, _now, _player);
        Tick(0f, events);
        Assert.That(_wrapper.Phase, Is.EqualTo(phase));
        Assert.That(_wrapper.IsInBeat, Is.True);
        Tick(0.75f, events);
        Assert.That(_boss.IsHidden, Is.False);
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(1));
        Tick(0.25f, events);
        Assert.That(_wrapper.IsInBeat, Is.False);
    }

    private void AssertAloft()
    {
        Assert.That(_boss.IsVulnerable, Is.False);
        Assert.That(_boss.IsHidden, Is.True);
        Assert.That(_enemies.ApplyDamage(_boss.Id, 1f, _now, _player).Blocked, Is.True);
    }

    private void KillAdd(EnemyAgent add)
    {
        add.Health.SetExternalInvulnerable(false); // The add has reached its exposed window.
        Assert.That(_enemies.ApplyDamage(add.Id, 1000f, _now, _player).Killed, Is.True);
    }

    private void Tick(float dt, IDomainEvents events = null, Vector3? quarry = null, Action beforeRain = null)
    {
        _now += dt;
        _intents.Clear();
        _player.Blackboard.PlayerPosition = _hero;
        _boss.Blackboard.PlayerPosition = quarry ?? _hero;
        EnemyTickContext context = Context(dt, events ?? _events);
        _wrapper.Tick(context);
        _projectiles.Tick(_now, _hero, _player, _enemies);
        beforeRain?.Invoke();
        _resolveRain(context, _hero);
        EnemyMoveIntent move = _intents.LastEnemyMove;
        _world.Clear();
        _world.PlayerPosition = _hero;
        _world.AddEnemy() = new EnemySense
        {
            Id = _boss.Id,
            Position = move.LandingPosition ?? _boss.Position + move.Velocity * dt
        };
        _enemies.Ingest(_world);
    }

    private EnemyTickContext Context(float dt, IDomainEvents events) =>
        new(dt, _now, _player, _intents, events, _projectiles, _enemies, _lanes, _open);

    private MatriarchArmed LastArm() => _events.Of<MatriarchArmed>()[^1];
    private float LastProgress() => _events.Of<MatriarchTellProgress>()[^1].Fraction;

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f, new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        hitIFrames: 0.5f);
}
