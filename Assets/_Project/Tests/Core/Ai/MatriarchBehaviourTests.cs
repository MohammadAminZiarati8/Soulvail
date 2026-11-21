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

[TestFixture]
public sealed class MatriarchBehaviourTests
{
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private MatriarchBehaviour _matriarch;
    private WorldSnapshot _world;
    private Vector3[] _openGround;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Character(), _events, _intents, 8);
        _projectiles = new ProjectileSystem(_events, 8);
        var body = new EnemySpec(new ContentId("enemy.matriarch"), new LocKey("enemy.matriarch.name"),
            100f, 2.2f, 8, 40, 300f, false, 20f, 9.5f, 1.35f, 1.8f, 40f, EnemyBehaviourKind.Static);
        var catalog = new ContentCatalog(new[] { Character() }, new[] { body });
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(0.5f),
            new DepthScaling(Scalings.Design()), 8);
        _agent = _enemies.Registry.Spawn(body, Vector3.Zero);
        _matriarch = new MatriarchBehaviour(_agent, new MatriarchStats());
        _world = new WorldSnapshot(8);
        _openGround = new[] { new Vector3(4f, 0f, 4f), new Vector3(-4f, 0f, -4f) };
        _now = 0f;
        PlaceHero(0f, 8f);
    }

    [TestCase(1f, false, 82f, 1)]
    [TestCase(1f, true, 100f, 1)]
    [TestCase(2f, false, 100f, 0)]
    public void Shove_LocksLaneAndStartsPounce(float impactX, bool defend, float hp, int hitAttempts)
    {
        Tick(20f);
        MatriarchArmed shove = _events.Single<MatriarchArmed>();
        Assert.That(shove.Move, Is.EqualTo(MatriarchMove.RuinShove));
        Assert.That(shove.Origin, Is.EqualTo(Vector3.Zero));
        Assert.That(shove.Target, Is.EqualTo(10f * Vector3.UnitZ));
        Assert.That(shove.Width, Is.EqualTo(2.4f));
        Assert.That(shove.Seconds, Is.EqualTo(1f));
        Assert.That(_events.Single<MatriarchTellProgress>().Fraction, Is.Zero);

        PlaceHero(8f, 0f);
        Tick(0.5f);
        Assert.That(LastProgress(), Is.EqualTo(0.5f));
        Assert.That(_events.Count<MatriarchArmed>(), Is.EqualTo(1));
        Assert.That(_events.Count<MatriarchImpact>(), Is.Zero);
        PlaceHero(impactX, 9f);
        _player.Health.SetExternalInvulnerable(defend);
        Tick(20f);
        Assert.That(LastProgress(), Is.EqualTo(1f));
        Assert.That(_events.Single<MatriarchImpact>().Position, Is.EqualTo(shove.Target));
        Assert.That(_events.Single<MatriarchTellCleared>().BossId, Is.EqualTo(_agent.Id));
        Assert.That(_player.Health.Current, Is.EqualTo(hp));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(hitAttempts));

        Tick(0.25f);
        Assert.That(_events.Count<MatriarchArmed>(), Is.EqualTo(1));
        Tick(20f);
        Assert.That(LastArm().Move, Is.EqualTo(MatriarchMove.CarrionPounce));
        Assert.That(LastProgress(), Is.Zero);
        Assert.That(_events.Count<MatriarchImpact>(), Is.EqualTo(1));
    }

    [TestCase(0, 100f, false)]
    [TestCase(1, 60f, false)]
    [TestCase(2, 33f, false)]
    [TestCase(2, 32f, true)]
    public void Pounce_LocksOpenGroundAndRepeatsOnlyInFinalPhase(int phase, float bossHp, bool repeat)
    {
        _agent.Health.ApplyDamage(100f - bossHp, _now);
        _matriarch.OnPhaseEntered(phase);
        Tick(0f);
        PlaceHero(5f, 4f); // Outside the shove and next to the first open point.
        Tick(1f);
        Tick(0.5f);
        MatriarchArmed first = LastArm();
        Assert.That(first.Target, Is.EqualTo(_openGround[0]));
        Assert.That(first.Radius, Is.EqualTo(2.5f));
        Assert.That(first.Seconds, Is.EqualTo(1.1f));

        PlaceHero(-4f, -4f);
        Tick(0.55f);
        Assert.That(LastProgress(), Is.EqualTo(0.5f));
        Assert.That(_agent.Position, Is.EqualTo(Vector3.Zero));
        PlaceHero(6f, 4f);
        Tick(0.55f);
        Assert.That(_agent.Position, Is.EqualTo(first.Target));
        Assert.That(_events.Of<MatriarchImpact>()[1].Position, Is.EqualTo(first.Target));
        Assert.That(_player.Health.Current, Is.EqualTo(76f));

        if (repeat)
        {
            PlaceHero(-5f, -4f);
            Tick(0.49f);
            Assert.That(_events.Count<MatriarchArmed>(), Is.EqualTo(2));
            Tick(20f);
            MatriarchArmed second = LastArm();
            Assert.That(second.Move, Is.EqualTo(MatriarchMove.CarrionPounce));
            Assert.That(second.Origin, Is.EqualTo(first.Target));
            Assert.That(second.Target, Is.EqualTo(_openGround[1]));
            Assert.That(second.Seconds, Is.EqualTo(first.Seconds));
            Assert.That(LastProgress(), Is.Zero);
            Assert.That(_events.Count<MatriarchImpact>(), Is.EqualTo(2));

            PlaceHero(4f, 4f);
            Tick(1.09f);
            Assert.That(_events.Count<MatriarchImpact>(), Is.EqualTo(2));
            Tick(20f);
            Assert.That(_agent.Position, Is.EqualTo(second.Target));
            Assert.That(_player.Health.Current, Is.EqualTo(76f));
        }

        int warnings = _events.Count<MatriarchArmed>();
        Tick(1.39f);
        Assert.That(_events.Count<MatriarchArmed>(), Is.EqualTo(warnings));
        Tick(20f);
        Assert.That(_events.Count<MatriarchArmed>(), Is.EqualTo(warnings + 1));
        Assert.That(LastArm().Move, Is.EqualTo(MatriarchMove.RuinShove));
    }

    [Test]
    public void Facing_TurnsSmoothlyAfterEachCast()
    {
        PlaceHero(0f, -8f);
        Tick(0.1f);
        AssertTurn(Vector2.UnitY, 27f);
        Assert.That(LastArm().Target, Is.EqualTo(-10f * Vector3.UnitZ));
        PlaceHero(8f, 0f);
        Tick(1f);
        Assert.That(_events.Single<MatriarchImpact>().Position, Is.EqualTo(-10f * Vector3.UnitZ));
        Tick(0.49f);
        PlaceHero(-8f, 0f);
        Vector2 beforePounce = Facing();
        Tick(0.02f);
        AssertTurn(beforePounce, 5.4f);
        MatriarchArmed pounce = LastArm();
        Tick(1.09f);
        Vector2 beforeLanding = Facing();
        PlaceHero(8f, 0f);
        Tick(0.02f);
        AssertTurn(beforeLanding, 5.4f);
        Assert.That(_agent.Position, Is.EqualTo(pounce.Target));

        Vector2 afterLanding = Facing();
        PlaceHero(pounce.Target.X, pounce.Target.Z);
        Tick(0.02f);
        Assert.That(Facing(), Is.EqualTo(afterLanding));
        Tick(1.37f);
        PlaceHero(8f, 0f);
        Tick(0.02f);
        AssertTurn(afterLanding, 5.4f);
        Assert.That(LastArm().Move, Is.EqualTo(MatriarchMove.RuinShove));

        Vector2 beforeBeat = Facing();
        _matriarch.OnPhaseEntered(1);
        PlaceHero(-8f, 0f);
        Tick(0.02f);
        AssertTurn(beforeBeat, 5.4f);
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(3, false)]
    [TestCase(4, false)]
    [TestCase(5, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    [TestCase(4, true)]
    [TestCase(5, true)]
    public void PhaseAndReset_CancelPendingGroundAttacks(int steps, bool reset)
    {
        _agent.Health.ApplyDamage(68f, _now);
        _matriarch.OnPhaseEntered(2);
        Tick(0f);
        for (int i = 0; i < steps; i++)
        {
            Tick(20f);
        }

        _events.Clear();
        if (reset)
        {
            _matriarch.Reset();
        }
        else
        {
            _matriarch.OnPhaseEntered(1);
        }

        Assert.That(_events.Count<MatriarchTellCleared>(), Is.EqualTo(steps % 2 == 0 ? 1 : 0));
        float playerHp = _player.Health.Current;
        Tick(20f);
        Assert.That(_events.Count<MatriarchImpact>(), Is.Zero);
        Assert.That(_events.Single<MatriarchArmed>().Move, Is.EqualTo(MatriarchMove.RuinShove));
        Assert.That(_player.Health.Current, Is.EqualTo(playerHp));
        Assert.That(_agent.IsVulnerable, Is.True);
        Assert.That(_agent.Health.ApplyDamage(1f, _now + 1f).Applied, Is.EqualTo(1f));

        Tick(20f);
        Tick(20f);
        Tick(20f);
        Tick(0.5f);
        Assert.That(_events.Count<MatriarchArmed>(), Is.EqualTo(2));
    }

    [Test]
    public void Tick_AllocatesNothing()
    {
        var silent = new SilentEvents();
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        _agent.Health.ApplyDamage(68f, 0f);
        AllocationAssert.None(() =>
        {
            player.Health.Reset();
            _matriarch.Reset();
            _matriarch.OnPhaseEntered(2);
            PlaceHero(0f, 4f);

            for (int i = 0; i < 14; i++)
            {
                _intents.Clear();
                _matriarch.Tick(new EnemyTickContext(2f, i * 2f, player, _intents, silent,
                    _projectiles, _enemies, openGround: _openGround));
            }
        }, 100);
    }

    private void Tick(float dt)
    {
        _now += dt;
        int moves = _intents.EnemyMoves.Count;
        _matriarch.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events,
            _projectiles, _enemies, openGround: _openGround));
        Assert.That(_intents.EnemyMoves.Count, Is.EqualTo(moves + 1));
        _world.Clear();
        _world.PlayerPosition = _agent.Blackboard.PlayerPosition;
        _world.AddEnemy() = new EnemySense
        {
            Id = _agent.Id,
            Position = _agent.Position + _intents.LastEnemyMove.Velocity * dt
        };
        _enemies.Ingest(_world);
    }

    private void AssertTurn(Vector2 before, float degrees)
    {
        Vector2 after = Facing();
        float dot = Vector2.Dot(before, after);
        float cross = before.X * after.Y - before.Y * after.X;
        float turn = MathF.Abs(MathF.Atan2(cross, dot)) * (180f / MathF.PI);
        Assert.That(turn, Is.EqualTo(degrees).Within(0.001f));
    }

    private void PlaceHero(float x, float z) => _agent.Blackboard.PlayerPosition = new Vector3(x, 0f, z);
    private Vector2 Facing() => _intents.LastEnemyMove.FacingXZ;
    private MatriarchArmed LastArm() => _events.Of<MatriarchArmed>()[^1];
    private float LastProgress() => _events.Of<MatriarchTellProgress>()[^1].Fraction;

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        hitIFrames: 0.5f);
}
