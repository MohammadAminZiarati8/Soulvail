using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class EchoArrowTests
{
    private const float Dt = 1f / 128f;
    private const int EnemyCapacity = 8;

    private PlayerCombat _player;
    private EnemySystem _enemies;
    private EnemyAgent _target;
    private WorldSnapshot _snapshot;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _player = new PlayerCombat(RunningShotTests.Bowman(), new SilentEvents(),
            new RecordingIntents(), EnemyCapacity);
        CharacterSpec bow = RunningShotTests.Bowman();
        EnemySpec husk = RunningShotTests.Husk();
        ModeSpec mode = Mode(husk);
        var catalog = new ContentCatalog(new[] { bow }, new[] { husk }, new[] { mode });
        _enemies = new EnemySystem(catalog, new SilentEvents(), new FixedRandom(7),
            new DepthScaling(mode.Scaling), EnemyCapacity);
        _target = _enemies.Spawn(husk.Id, Vector3.UnitZ * 7f);
        _snapshot = new WorldSnapshot(EnemyCapacity);
        _now = 0f;
    }

    [Test]
    public void Rank1_FourthArrowEchoes()
    {
        LearnRank1();
        AssertCadence(8, 4, 0.6f);
    }

    [Test]
    public void Off_NoEcho()
    {
        _player.EchoDamage.Base = 0.6f;
        NextBasic();
        AssertNoShotForEchoDelay();
        _player.EchoDamage.Base = 0f;

        LearnRank1();
        NextBasic();
        _player.EchoEvery.Base = -3.5f;
        NextBasic();
        AssertNoShotForEchoDelay();
        _player.EchoEvery.Base = 0f;

        AssertCadence(4, 4, 0.6f);
    }

    [Test]
    public void Volley_NotCounted()
    {
        LearnRank1();
        _player.Volley.Every.Base = 2f;
        for (int group = 0; group < 2; group++)
        {
            NextBasic();
            AssertNoShotForEchoDelay();
            NextBasic();
            if (group == 1)
            {
                TakeEchoWhenDue();
            }

            AwaitShot();
            Assert.That(Take(ShotOrigin.Ranger).IsVolley, Is.True);
            AssertNoShotForEchoDelay();
        }
    }

    [Test]
    public void Echo_AfterDelay_SameTarget_SixtyPercent()
    {
        LearnRank1();
        _player.SteadyAim.PerStack.Base = 0.05f;
        for (int i = 0; i < 3; i++)
        {
            NextBasic();
        }

        _player.SteadyAim.OnMoved();
        _player.SteadyAim.StopCrit.Base = 2f;
        Projectile original = NextBasic();
        Assert.That(original.IsCrit, Is.True);

        _player.Weapon.Damage.Base = 90f;
        EnemyAgent other = _enemies.Spawn(_target.Spec.Id, Vector3.UnitZ * 3f);
        _snapshot.AddEnemy() = new EnemySense
        {
            Id = _target.Id,
            Position = new Vector3(1f, 0f, 7f),
            Velocity = Vector3.UnitX,
            HasLineOfSight = true,
        };
        _snapshot.AddEnemy() = new EnemySense
        {
            Id = other.Id,
            Position = other.Position,
            HasLineOfSight = true,
        };
        _enemies.Ingest(_snapshot);
        _player.Targeter.Focus(other.Id);
        Projectile echo = TakeEchoWhenDue();

        Assert.That(_player.Targeter.CurrentTargetId, Is.EqualTo(other.Id));
        Assert.That(echo.Target, Is.EqualTo(ProjectileLead.Solve(
            _snapshot.PlayerPosition, _target.Position, _target.Velocity, original.Speed)));
        Assert.That(echo.Damage, Is.EqualTo(original.Damage * 0.6f).Within(0.0001f));
        Assert.That(echo.Side, Is.EqualTo(ShotSide.AtEnemies));
    }

    [Test]
    public void Echo_TargetDead_Dropped()
    {
        LearnRank1();
        _player.EchoEvery.Base = -3f;
        NextBasic();

        _target.Health.ApplyDamage(10000f, _now);
        _enemies.Spawn(_target.Spec.Id, Vector3.UnitZ * 6f);
        AssertNoShotForEchoDelay();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Echo_FiresWhileMoving(bool rolling)
    {
        LearnRank1();
        _player.EchoEvery.Base = -3f;
        NextBasic();

        _snapshot.MoveInput = Vector2.UnitX;
        _snapshot.PlayerVelocity = Vector3.UnitX;
        _snapshot.PlayerPosition = Vector3.UnitX;
        if (rolling)
        {
            _player.Charge.Request(_now);
        }

        Projectile echo = TakeEchoWhenDue();
        Assert.That(echo.Origin, Is.EqualTo(_snapshot.PlayerPosition));
        Assert.That(_player.Charge.IsActive, Is.EqualTo(rolling));
    }

    [Test]
    public void Echo_NotCountedForSteadyAim()
    {
        LearnRank1();
        _player.EchoEvery.Base = -3f;
        _player.SteadyAim.PerStack.Base = 0.1f;
        NextBasic();

        TakeEchoWhenDue();
        Assert.That(_player.SteadyAim.Stacks, Is.EqualTo(1));
    }

    [Test]
    public void Pending_OverCap_Dropped()
    {
        LearnRank1();
        _player.EchoEvery.Base = -3f;
        _player.Weapon.FireRate.Base = 40f;

        NextBasic();
        float first = _now;
        _player.Weapon.Damage.Base = 20f;
        NextBasic();
        _player.Weapon.Damage.Base = 30f;
        NextBasic();
        Assert.That(_now, Is.LessThan(first + PlayerCombat.EchoDelay));

        _snapshot.MoveInput = Vector2.UnitX;
        for (int i = 0; i < 64 && _now < first + PlayerCombat.EchoDelay; i++)
        {
            Step();
        }

        Assert.That(Take(ShotOrigin.Echo).Damage, Is.EqualTo(6f));
        for (int i = 0; i < 32 && _player.PendingShot is null; i++)
        {
            Step();
        }

        Assert.That(Take(ShotOrigin.Echo).Damage, Is.EqualTo(12f));
        AssertNoShotForEchoDelay();
    }

    [Test]
    public void Rank4_EveryOther_EightyPercent()
    {
        LearnRank1();
        _player.EchoEvery.Add(new Modifier(ModifierKind.Flat, -1f, new object()));
        _player.EchoDamage.Add(new Modifier(ModifierKind.Flat, 0.2f, new object()));
        _player.EchoEvery.Add(new Modifier(ModifierKind.Flat, -1f, new object()));
        AssertCadence(6, 2, 0.8f);
    }

    [Test]
    public void StageBoundary_ClearsPending()
    {
        CharacterSpec bow = RunningShotTests.Bowman();
        EnemySpec husk = RunningShotTests.Husk(15f);
        ModeSpec mode = Mode(husk);
        SkillSpec echo = Passive("skill.test.echo", new ModifyStat(PlayerStat.EchoEvery, ModifierKind.Flat, 3f),
            new ModifyStat(PlayerStat.EchoDamage, ModifierKind.Flat, 0.6f));
        SkillSpec second = Passive("skill.test.second", new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f));
        SkillSpec third = Passive("skill.test.third", new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f));
        var tree = new SkillTreeSpec(new ContentId("tree.test.echo"), bow.Id, new[]
        {
            Branch("tree.test.a", echo.Id),
            Branch("tree.test.b", second.Id),
            Branch("tree.test.c", third.Id),
        });
        var catalog = new ContentCatalog(new[] { bow }, new[] { husk }, new[] { mode },
            new[] { echo, second, third }, new[] { tree });
        var random = new FixedRandom(7);
        var events = new RecordingEvents();
        var session = new RunSession(catalog, random, events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(default), events), EnemyCapacity, 8, 16);
        var saved = new RunSnapshot(RunSnapshot.CurrentVersion, mode.Id, bow.Id, 7, 1,
            new RandomState(0, 0, 0, 0, 0), 100f, 0f, 0f, default, 2, 0f, 0,
            new[] { echo.Id }, new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        session.Start(new RunConfig(mode.Id, bow.Id, 7, 1, SpawnPlan.Empty, saved));

        var target = Vector3.UnitZ * 7f;
        var snapshot = new WorldSnapshot(EnemyCapacity)
        {
            Dt = Dt,
            HasGate = true,
            SpawnPoints = new[] { target },
        };
        int shotCount = 0;
        float firstShotInStageTwo = float.PositiveInfinity;
        for (int i = 0; i < 2400 && session.State.Time < firstShotInStageTwo + 0.25f; i++)
        {
            snapshot.EnemyCount = 0;
            foreach (EnemySpawned spawned in events.Of<EnemySpawned>())
            {
                snapshot.AddEnemy() = new EnemySense { Id = spawned.Id, Position = target, HasLineOfSight = true };
            }

            session.Tick(snapshot);
            int currentShotCount = events.Count<ProjectileFired>();
            if (session.State.StageIndex == 2 && currentShotCount > shotCount && float.IsPositiveInfinity(firstShotInStageTwo))
            {
                firstShotInStageTwo = session.State.Time;
            }

            shotCount = currentShotCount;
            if (session.State.IsSanctumOpen)
            {
                session.LeaveSanctum();
            }
        }

        Assert.That(session.State.StageIndex, Is.EqualTo(2));
        Assert.That(firstShotInStageTwo, Is.LessThan(float.PositiveInfinity));
        var shots = events.Of<ProjectileFired>();
        Assert.That(shots.Count, Is.EqualTo(3), "Two basics cleared stage one, and one left in stage two.");
        Assert.That(shots.Select(shot => shot.ShotOrigin), Is.All.EqualTo(ShotOrigin.Ranger));
    }

    [TestCase(true, TestName = "Death_ClearsPending")]
    [TestCase(false, TestName = "Reset_ClearsPending")]
    public void Boundary_ClearsPendingAndPartialCount(bool death)
    {
        LearnRank1();
        _player.EchoEvery.Base = -2f;
        NextBasic();
        NextBasic();
        // The next basic shot also leaves a partial count before the first echo is due.
        _player.EchoEvery.Base = -1f;
        _player.Weapon.Reset();
        _player.Weapon.FireRate.Base = 20f;
        NextBasic();
        _player.Weapon.Reset();
        _player.Weapon.FireRate.Base = 2f;

        if (death)
        {
            _player.ApplyDamage(10000f, _now);
        }
        else
        {
            _player.Reset();
        }

        AssertNoShotForEchoDelay();
        if (!death)
        {
            AssertCadence(3, 3, 0.6f);
        }
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        LearnRank1();
        _player.EchoEvery.Base = -3f;

        AllocationAssert.None(() =>
        {
            Step();
            while (_player.TryTakeShot(out _))
            { }
        });
    }

    private void LearnRank1()
    {
        _player.EchoEvery.Add(new Modifier(ModifierKind.Flat, 4f, this));
        _player.EchoDamage.Add(new Modifier(ModifierKind.Flat, 0.6f, this));
    }

    private void AssertCadence(int basicCount, int every, float multiplier)
    {
        for (int i = 1; i <= basicCount; i++)
        {
            Projectile basic = NextBasic();
            if (i % every == 0)
            {
                Assert.That(TakeEchoWhenDue().Damage, Is.EqualTo(basic.Damage * multiplier).Within(0.0001f));
            }
            else
            {
                AssertNoShotForEchoDelay();
            }
        }
    }

    private void AwaitShot()
    {
        for (int i = 0; i < 256 && _player.PendingShot is null; i++)
        {
            Step();
        }
    }

    private Projectile NextBasic()
    {
        AwaitShot();
        Projectile shot = Take(ShotOrigin.Ranger);
        Assert.That(shot.IsVolley, Is.False);
        return shot;
    }

    private Projectile TakeEchoWhenDue()
    {
        float due = _now + PlayerCombat.EchoDelay;
        while (_now < due)
        {
            Step();
            if (_now < due)
            {
                Assert.That(_player.PendingShot, Is.Null);
            }
        }

        return Take(ShotOrigin.Echo);
    }

    private void AssertNoShotForEchoDelay()
    {
        float due = _now + PlayerCombat.EchoDelay;
        while (_now < due)
        {
            Step();
            Assert.That(_player.PendingShot, Is.Null);
        }
    }

    private Projectile Take(ShotOrigin origin)
    {
        Assert.That(_player.TryTakeShot(out Projectile shot), Is.True);
        Assert.That(shot.ShotOrigin, Is.EqualTo(origin));
        return shot;
    }

    private static ModeSpec Mode(EnemySpec husk) =>
        new(new ContentId("mode.test.echo"), new LocKey("mode.test.name"), 1, true, 0,
            new ScalingSpec(new BudgetCurve(4f, 0f, 0f), new WaveCurve(1, 1000, 1, 1),
                new ConcurrencyCurve(8, 1000), new StatCurve(0.06f, 4f, 1, 1),
                new StatCurve(0.035f, 3f, 1, 1), new StatCurve(0.02f, 1.3f, 5, 0)),
            Scalings.Xp(), new[] { new RosterEntry(husk.Id, 1) });

    private static SkillSpec Passive(string id, params IEffect[] effects) =>
        new(new ContentId(id), new LocKey("skill.test.name"), new LocKey("skill.test.desc"),
            SkillKind.Passive, effects);

    private static SkillBranchSpec Branch(string id, ContentId skill) =>
        new(new LocKey(id), new IReadOnlyList<ContentId>[] { new[] { skill } });

    private void Step()
    {
        _now += Dt;
        _player.Tick(Dt, _now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);
    }
}
