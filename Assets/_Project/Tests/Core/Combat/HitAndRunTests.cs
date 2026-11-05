using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Progression;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class HitAndRunTests
{
    private Stat _speed;
    private HitAndRun _passive;
    private StatusBoard _status;

    [SetUp]
    public void SetUp()
    {
        _speed = new Stat(4f);
        _status = new StatusBoard();
        _passive = new HitAndRun(_speed);
        _passive.AttachStatus(_status);
        _passive.HitSpeedBurst.Base = 0.15f;
        _passive.MomentumPerMetre.Base = 0.02f;
    }

    [Test]
    public void Hit_StartsBurst_SpeedRises()
    {
        _speed.Add(new Modifier(ModifierKind.PercentAdd, 0.25f, this));
        _passive.OnHit(1f);
        Assert.That(_passive.SpeedBonus(1f), Is.EqualTo(0.15f));
        Assert.That(_speed.Value, Is.EqualTo(5.75f).Within(0.0001f));
        Assert.That(_speed.ModifierCount, Is.EqualTo(2));
    }

    [Test]
    public void Hit_Refreshes_NoStack()
    {
        _passive.OnHit(1f);
        _passive.OnHit(2f);
        _passive.OnMoved(0f, 3f);

        Assert.That(_speed.ModifierCount, Is.EqualTo(1));
        Assert.That(_speed.Value, Is.EqualTo(4.6f).Within(0.0001f));
        Assert.That(_passive.SpeedBonus(3f), Is.EqualTo(0.15f));
    }

    [Test]
    public void Burst_Ends_SpeedRestored()
    {
        _passive.OnHit(1f);
        _passive.OnHit(2f);
        _passive.OnMoved(0f, 3.5f);

        Assert.That(_passive.SpeedBonus(3.5f), Is.Zero);
        Assert.That(_speed.Value, Is.EqualTo(4f));
        Assert.That(_speed.ModifierCount, Is.Zero);
        Assert.That(_status.Count, Is.Zero);
    }

    [Test]
    public void Burst_PostsAndRepostsStatus()
    {
        _passive.OnHit(1f);
        Assert.That(_status[0].Id, Is.EqualTo(new ContentId("status.hit-and-run")));
        Assert.That(_status[0].IsBuff, Is.True);
        Assert.That(_status[0].Remaining, Is.EqualTo(1.5f));
        _status.Tick(1f);
        Assert.That(_status[0].Remaining, Is.EqualTo(0.5f));

        _passive.OnHit(2f);
        Assert.That(_status.Count, Is.EqualTo(1));
        Assert.That(_status[0].Remaining, Is.EqualTo(1.5f));
        Assert.That(_status[0].Stacks, Is.EqualTo(1));
    }

    [Test]
    public void Momentum_FourMetres_Rank2_Plus8Percent()
    {
        _passive.OnMoved(2f, 0.2f);
        _passive.OnMoved(2f, 0.3f);
        Assert.That(_passive.DamageMultiplier(0.3f), Is.EqualTo(1.08f).Within(0.0001f));
    }

    [Test]
    public void Momentum_CapsAtEight()
    {
        for (int i = 0; i < 4; i++)
        {
            _passive.OnMoved(3f, i * 0.1f);
        }

        Assert.That(_passive.DamageMultiplier(0.4f), Is.EqualTo(1.16f).Within(0.0001f));
    }

    [Test]
    public void Momentum_SnapIgnored()
    {
        _passive.OnMoved(2f, 0.3f);
        _passive.OnMoved(3.001f, 0.4f);
        Assert.That(_passive.DamageMultiplier(0.4f), Is.EqualTo(1.04f).Within(0.0001f));
    }

    [Test]
    public void Momentum_ForgetsAfterTwoSeconds()
    {
        _passive.OnMoved(2f, 0.15f);
        _passive.OnMoved(1f, 0.5f);
        Assert.That(_passive.DamageMultiplier(2.149f), Is.EqualTo(1.06f).Within(0.0001f));
        Assert.That(_passive.DamageMultiplier(2.151f), Is.EqualTo(1.02f).Within(0.0001f));
        Assert.That(_passive.DamageMultiplier(2.5f), Is.EqualTo(1f));
    }

    [Test]
    public void Reset_ClearsAll()
    {
        _passive.OnHit(1f);
        _passive.OnMoved(2f, 1f);
        _passive.Reset();

        Assert.That(_status.Count, Is.Zero);
        Assert.That(_speed.Value, Is.EqualTo(4f));
        Assert.That(_passive.DamageMultiplier(1f), Is.EqualTo(1f));
    }

    [Test]
    public void StageBoundary_ClearsAll()
    {
        CharacterSpec bow = RunningShotTests.Bowman();
        EnemySpec enemy = RunningShotTests.Husk(18f);
        var mode = new ModeSpec(new ContentId("mode.test.burst"), new LocKey("mode.test.name"),
            1, true, 0, new ScalingSpec(new BudgetCurve(4f, 0f, 0f),
                new WaveCurve(1, 1000, 1, 1), new ConcurrencyCurve(8, 1000),
                new StatCurve(0.06f, 4f, 1, 1), new StatCurve(0.035f, 3f, 1, 1),
                new StatCurve(0.02f, 1.3f, 5, 0)), Scalings.Xp(),
            new[] { new RosterEntry(enemy.Id, 1) });
        var skill = new SkillSpec(new ContentId("skill.test.burst"), new LocKey("skill.test.name"),
            new LocKey("skill.test.desc"), SkillKind.Passive, new IEffect[]
            {
                new ModifyStat(PlayerStat.HitSpeedBurst, ModifierKind.Flat, 0.15f),
                new ModifyStat(PlayerStat.MomentumPerMetre, ModifierKind.Flat, 0.02f),
            });
        SkillSpec other = Filler("skill.test.other");
        SkillSpec third = Filler("skill.test.third");
        var tree = new SkillTreeSpec(new ContentId("tree.test.bow"), bow.Id, new[]
        {
            Branch("tree.test.a", skill.Id),
            Branch("tree.test.b", other.Id),
            Branch("tree.test.c", third.Id),
        });
        var catalog = new ContentCatalog(new[] { bow }, new[] { enemy }, new[] { mode },
            new[] { skill, other, third }, new[] { tree });
        var random = new FixedRandom(99);
        var events = new RecordingEvents();
        var session = new RunSession(catalog, random, events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(default), events), 8, 8, 16);
        var saved = new RunSnapshot(RunSnapshot.CurrentVersion, mode.Id, bow.Id, 99, 1,
            new RandomState(0, 0, 0, 0, 0), 100f, 0f, 0f, default, 2, 0f, 0,
            new[] { skill.Id }, new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        session.Start(new RunConfig(mode.Id, bow.Id, 99, 1, SpawnPlan.Empty, saved));

        var target = new Vector3(0f, 0f, 7f);
        var snapshot = new WorldSnapshot(8)
        {
            Dt = 0.01f,
            HasGate = true,
            SpawnPoints = new[] { target },
        };
        bool posted = false;
        for (int i = 0; i < 2000 && session.State.StageIndex == 1; i++)
        {
            snapshot.EnemyCount = 1;
            snapshot.Enemies[0] = new EnemySense { Id = 1, Position = target, HasLineOfSight = true };
            session.Tick(snapshot);
            posted |= session.State.StatusCount > 0;
            if (session.State.IsSanctumOpen)
            {
                session.LeaveSanctum();
            }
        }

        Assert.That(posted, Is.True, "A damaging arrow posted the burst before the boundary.");
        Assert.That(session.State.StageIndex, Is.EqualTo(2));
        Assert.That(session.State.StatusCount, Is.Zero);
        snapshot.MoveInput = Vector2.UnitX;
        session.Tick(snapshot);
        snapshot.Dt = 1f;
        session.Tick(snapshot);
        Assert.That(session.State.PlayerVelocity.Length(), Is.EqualTo(3f).Within(0.0001f));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        AllocationAssert.None(() =>
        {
            _passive.OnMoved(2f, 0.1f);
            _passive.OnMoved(2f, 0.2f);
            _ = _passive.DamageMultiplier(0.2f);
            _passive.OnHit(0.2f);
            _status.Tick(0.1f);
            _passive.OnHit(0.3f);
            _passive.OnMoved(0f, 1.8f);
            _passive.Reset();
        });
    }

    private static SkillSpec Filler(string id) => new(new ContentId(id), new LocKey("skill.test.name"),
        new LocKey("skill.test.desc"), SkillKind.Passive,
        new IEffect[] { new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f) });

    private static SkillBranchSpec Branch(string id, ContentId node) =>
        new(new LocKey(id), new IReadOnlyList<ContentId>[] { new[] { node } });
}
