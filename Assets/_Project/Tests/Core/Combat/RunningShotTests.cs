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

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class RunningShotTests
{
    private const float Dt = 1f / 128f;
    private PlayerCombat _player;
    private EnemySystem _enemies;
    private WorldSnapshot _snapshot;
    private RecordingEvents _events;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        var random = new FixedRandom(7);
        var catalog = new ContentCatalog(new[] { Bowman() }, new[] { Husk() }, new[] { Mode() });
        _player = new PlayerCombat(Bowman(), _events, new RecordingIntents(), 8);
        _enemies = new EnemySystem(catalog, _events, random, new DepthScaling(Scalings.Design()), 8);
        _snapshot = new WorldSnapshot(8) { Dt = Dt };
        EnemyAgent enemy = _enemies.Spawn(Husk().Id, new Vector3(0f, 0f, 8f));
        _snapshot.AddEnemy() = new EnemySense
        {
            Id = enemy.Id,
            Position = enemy.Position,
            HasLineOfSight = true,
        };
        _enemies.Ingest(_snapshot);
        _now = 0f;
    }

    [Test]
    public void Moving_WithoutRunningShot_HoldsFire()
    {
        Assert.That(_player.MovingFireRate.Base, Is.EqualTo(1f));
        SetMoving();

        for (int i = 0; i < 256; i++)
        {
            Step();
            Assert.That(_player.TryTakeShot(out _), Is.False);
        }

        Assert.That(_events.Count<PlayerAttacked>(), Is.Zero);
    }

    [TestCase(1, true, 0.50f, TestName = "Moving_Rank1_Fires_AtFiftyPercent")]
    [TestCase(4, true, 1.25f, TestName = "Moving_Rank4_Fires_AtOneHundredTwentyFivePercent")]
    [TestCase(4, false, 1f, TestName = "Standing_FullRate")]
    public void Rate_SamplesAtTheStartOfEachDraw(int rank, bool moving, float rate)
    {
        LearnRunningShot(rank);
        if (moving)
        {
            SetMoving();
        }

        Step();
        float startedAt = _now;
        Assert.That(_player.Weapon.DrawSeconds, Is.EqualTo(0.25f / rate).Within(0.00001f));

        // A rate change during the draw belongs to the next interval.
        _player.MovingFireRate.Add(new Modifier(ModifierKind.Flat, 0.15f, this));
        int shots = 0;
        for (int i = 0; i < 256 && _events.Count<PlayerAttacked>() < 2; i++)
        {
            Step();
            if (_player.TryTakeShot(out _))
            {
                shots++;
            }
        }

        Assert.That(shots, Is.EqualTo(1));
        Assert.That(_events.Count<PlayerAttacked>(), Is.EqualTo(2));
        Assert.That(_now - startedAt, Is.EqualTo(0.5f / rate).Within(Dt));
        Assert.That(_player.Weapon.DrawSeconds,
            Is.EqualTo(0.25f / (moving ? rate + 0.15f : 1f)).Within(0.00001f));
    }

    [Test]
    public void Moving_FacesTarget()
    {
        LearnRunningShot(1);
        SetMoving();
        Step();
        Assert.That(_player.FaceDirection, Is.EqualTo(Vector3.UnitZ));
    }

    [Test]
    public void Moving_NoTarget_FacesRun()
    {
        LearnRunningShot(1);
        SetMoving();
        _enemies.Clear();
        Step();
        var motor = new PlayerMotor(Bowman().Movement, Vector3.UnitZ);
        motor.Tick(1f, _snapshot.MoveInput, _player.FaceDirection);

        Assert.That(_player.FaceDirection, Is.Null);
        Assert.That(Vector3.Dot(motor.Facing, Vector3.UnitX), Is.GreaterThan(0.99f));
    }

    [Test]
    public void Arrow_CapturesMomentumAtRelease()
    {
        LearnRunningShot(4);
        SetMoving();
        _player.HitAndRun.MomentumPerMetre.Base = 0.02f;
        Step();
        _snapshot.PlayerPosition = new Vector3(2f, 12f, 0f);
        Step();
        _snapshot.PlayerPosition = new Vector3(4f, 0f, 0f);

        for (int i = 0; i < 128; i++)
        {
            Step();
            if (_player.TryTakeShot(out Projectile shot))
            {
                _player.HitAndRun.Reset();
                Assert.That(shot.Damage, Is.EqualTo(10.8f).Within(0.0001f));
                return;
            }
        }

        Assert.Fail("The draw never released an arrow.");
    }

    private void LearnRunningShot(int rank)
    {
        _player.FireWhileMoving.Add(new Modifier(ModifierKind.Flat, 1f, this));
        _player.MovingFireRate.Add(new Modifier(ModifierKind.Flat, -0.50f, this));
        for (int i = 1; i < rank; i++)
        {
            _player.MovingFireRate.Add(new Modifier(ModifierKind.Flat, 0.25f, this));
        }
    }

    private void SetMoving()
    {
        _snapshot.MoveInput = Vector2.UnitX;
        _snapshot.PlayerVelocity = Vector3.UnitX * 3f;
    }

    private void Step()
    {
        _now += Dt;
        _player.Tick(Dt, _now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);
    }

    internal static CharacterSpec Bowman() => new(new ContentId("character.test.bow"),
        new LocKey("character.test.name"), new LocKey("character.test.desc"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, 10f, 2f, 12f, 360f, 0.5f, 40f, 0.5f, firesWhileMoving: false),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 6f, 0.3f, 3f, 0.15f, 0f, 0f, 0.05f),
        volley: new VolleySpec(3, 30f, 1.5f));

    internal static EnemySpec Husk(float hp = 1000f) => new(new ContentId("enemy.test.husk"),
        new LocKey("enemy.test.name"), hp, 3.5f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f,
        30f, EnemyBehaviourKind.Static);

    private static ModeSpec Mode() => new(new ContentId("mode.test.empty"), new LocKey("mode.test.name"),
        1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
}
