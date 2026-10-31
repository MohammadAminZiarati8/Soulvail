using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class SteadyAimTests
{
    [Test]
    public void Inert_WithoutPerStack()
    {
        var events = new RecordingEvents();
        var aim = new SteadyAim(events);
        aim.StopCrit.Base = 2f;
        aim.Guard.Base = 0.2f;
        for (int i = 0; i < 8; i++)
        {
            Assert.That(aim.OnLoose(out bool crit), Is.EqualTo(1f));
            Assert.That(crit, Is.False);
        }
        aim.OnMoved();
        Assert.That(aim.Stacks, Is.Zero);
        Assert.That(aim.IncomingMultiplier, Is.EqualTo(1f));
        Assert.That(events.All, Is.Empty);
    }

    [Test]
    public void Stacks_ApplyBeforeAdding()
    {
        var aim = Aim();
        for (int i = 0; i < 8; i++)
        {
            Assert.That(aim.OnLoose(), Is.EqualTo(1f + (0.05f * Math.Min(i, 5))).Within(0.00001f));
        }
    }

    [Test]
    public void Stacks_CapAtMax()
    {
        var aim = Aim();
        aim.MaxStacks.Base = 3f;
        for (int i = 0; i < 8; i++)
        { aim.OnLoose(); }
        Assert.That(aim.Stacks, Is.EqualTo(3));
    }

    [Test]
    public void StopCrit_FirstArrowOnly()
    {
        var aim = Aim();
        aim.StopCrit.Base = 2f;
        Assert.That(aim.OnLoose(out bool first), Is.EqualTo(2f));
        Assert.That(first, Is.True);
        Assert.That(aim.OnLoose(out bool second), Is.EqualTo(1.05f));
        Assert.That(second, Is.False);
        aim.OnMoved();
        Assert.That(aim.OnLoose(out bool stopped), Is.EqualTo(2f));
        Assert.That(stopped, Is.True);
    }

    [Test]
    public void Guard_OnlyAtFullStacks()
    {
        var player = Player(new RecordingEvents());
        player.SteadyAim.PerStack.Base = 0.05f;
        player.SteadyAim.Guard.Base = 0.2f;
        for (int i = 0; i < 4; i++)
        { player.SteadyAim.OnLoose(); }
        Assert.That(player.ApplyDamage(10f, 0f).ToHp, Is.EqualTo(10f));
        player.SteadyAim.OnLoose();
        Assert.That(player.ApplyDamage(10f, 1f).ToHp, Is.EqualTo(8f).Within(0.00001f));
    }

    [Test]
    public void Changed_PublishesOncePerValue()
    {
        var events = new RecordingEvents();
        var aim = new SteadyAim(events);
        aim.PerStack.Base = 0.05f;
        for (int i = 0; i < 8; i++)
        { aim.OnLoose(); }
        aim.OnMoved();
        aim.OnMoved();
        var changes = events.Of<SteadyAimChanged>();
        Assert.That(changes.Count, Is.EqualTo(6));
        for (int i = 0; i < 5; i++)
        {
            Assert.That(changes[i].Stacks, Is.EqualTo(i + 1));
            Assert.That(changes[i].MaxStacks, Is.EqualTo(5));
        }
        Assert.That(changes[5].Stacks, Is.Zero);
    }

    [Test]
    public void Move_ClearsStacks()
    {
        var player = Player(new RecordingEvents());
        player.SteadyAim.PerStack.Base = 0.05f;
        player.SteadyAim.OnLoose();
        var snapshot = new WorldSnapshot(8) { PlayerVelocity = Vector3.UnitX };
        player.Tick(0.01f, 0f, snapshot, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);
        Assert.That(player.SteadyAim.Stacks, Is.Zero);
    }

    [Test]
    public void Roll_ClearsStacks()
    {
        var player = Player(new RecordingEvents());
        player.SteadyAim.PerStack.Base = 0.05f;
        player.SteadyAim.OnLoose();
        player.Charge.Request(0f);
        player.Tick(0.01f, 0f, new WorldSnapshot(8), ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);
        Assert.That(player.SteadyAim.Stacks, Is.Zero);
    }

    [Test]
    public void OrdinaryAndVolley_EachArrowStacksAndCarriesCrit()
    {
        var events = new RecordingEvents();
        var player = Player(events);
        player.SteadyAim.PerStack.Base = 0.05f;
        player.SteadyAim.StopCrit.Base = 2f;
        player.EagleEye.FarBonus.Base = 0.2f;
        player.Volley.Every.Base = 1f;
        var enemies = new EnemyRegistry(8);
        enemies.Spawn(Enemy(), new Vector3(0f, 0f, 7f));
        enemies.Spawn(Enemy(), new Vector3(1f, 0f, 7f));
        var snapshot = new WorldSnapshot(8);
        var system = new ProjectileSystem(events, 8);
        int arrows = 0;
        for (int i = 0; i < 100 && arrows < 3; i++)
        {
            player.Tick(0.01f, i * 0.01f, snapshot, enemies.Alive, Vector3.UnitZ);
            while (player.TryTakeShot(out Projectile shot))
            {
                float expected = arrows == 0 ? 31.2f : 13f * 1.5f * (1f + (0.05f * arrows)) * 1.2f;
                Assert.That(shot.Damage, Is.EqualTo(expected).Within(0.0001f));
                Assert.That(shot.IsCrit, Is.EqualTo(arrows == 0));
                system.Fire(shot, i * 0.01f);
                arrows++;
            }
        }
        Assert.That(arrows, Is.EqualTo(3));
        Assert.That(player.SteadyAim.Stacks, Is.EqualTo(3));
        var fired = events.Of<ProjectileFired>();
        Assert.That(fired[0].IsCrit, Is.True);
        Assert.That(fired[1].IsCrit, Is.False);
        Assert.That(fired[1].IsVolley, Is.True);
    }

    [Test]
    public void StatAddresses_AppendAndResolve()
    {
        var events = new RecordingEvents();
        var player = Player(events);
        var stats = new PlayerStats(player,
            new PlayerMotor(new MovementSpec(3f, 0.06f, 0.08f, 720f), Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), events));
        Assert.That((int)PlayerStat.SteadyAimPerStack, Is.EqualTo((int)PlayerStat.VolleyDamage + 1));
        Assert.That(stats.Resolve(PlayerStat.SteadyAimPerStack), Is.SameAs(player.SteadyAim.PerStack));
        Assert.That(stats.Resolve(PlayerStat.SteadyAimStopCrit), Is.SameAs(player.SteadyAim.StopCrit));
        Assert.That(stats.Resolve(PlayerStat.SteadyAimGuard), Is.SameAs(player.SteadyAim.Guard));
        Assert.That(stats.Resolve(PlayerStat.SteadyAimMaxStacks).Value, Is.EqualTo(5f));
        Assert.That(stats.Resolve(PlayerStat.EagleEyeFarBonus), Is.SameAs(player.EagleEye.FarBonus));
        for (int i = (int)PlayerStat.SteadyAimPerStack; i <= (int)PlayerStat.SteadyAimMaxStacks; i++)
        {
            Assert.That(stats.Has((PlayerStat)i), Is.True);
        }
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var events = new SilentEvents();
        var player = new PlayerCombat(Character(), events, new RecordingIntents(), 8);
        player.SteadyAim.PerStack.Base = 0.05f;
        player.SteadyAim.StopCrit.Base = 2f;
        player.EagleEye.FarBonus.Base = 0.2f;
        var enemies = new EnemyRegistry(8);
        enemies.Spawn(Enemy(), new Vector3(0f, 0f, 7f));
        var snapshot = new WorldSnapshot(8);
        float now = 0f;
        AllocationAssert.None(() =>
        {
            player.SteadyAim.OnMoved();
            player.Tick(0.01f, now += 0.01f, snapshot, enemies.Alive, Vector3.UnitZ);
            while (player.TryTakeShot(out _))
            { }
        });
    }

    [Test]
    public void Boundary_ClearsStacks()
    {
        var events = new RecordingEvents();
        var skill = new SkillSpec(new ContentId("skill.test.aim"), new LocKey("skill.test.name"),
            new LocKey("skill.test.desc"), SkillKind.Passive,
            new IEffect[] { new ModifyStat(PlayerStat.SteadyAimPerStack, ModifierKind.Flat, 0.05f) });
        SkillBranchSpec Branch(string id, ContentId node) => new(new LocKey(id),
            new System.Collections.Generic.IReadOnlyList<ContentId>[] { new[] { node } });
        var other = new SkillSpec(new ContentId("skill.test.other"), new LocKey("skill.test.name"),
            new LocKey("skill.test.desc"), SkillKind.Passive, new IEffect[] { new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f) });
        var third = new SkillSpec(new ContentId("skill.test.third"), new LocKey("skill.test.name"),
            new LocKey("skill.test.desc"), SkillKind.Passive, new IEffect[] { new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f) });
        var tree = new SkillTreeSpec(new ContentId("tree.test.bow"), Character().Id,
            new[] { Branch("tree.test.a", skill.Id), Branch("tree.test.b", other.Id), Branch("tree.test.c", third.Id) });
        var mode = new ModeSpec(new ContentId("mode.test.empty"), new LocKey("mode.test.name"),
            1, true, 0, new ScalingSpec(new BudgetCurve(4f, 0f, 0f),
                new WaveCurve(1, 1000, 1, 1), new ConcurrencyCurve(8, 1000),
                new StatCurve(0.06f, 4f, 1, 1), new StatCurve(0.035f, 3f, 1, 1),
                new StatCurve(0.02f, 1.3f, 5, 0)), Scalings.Xp(),
            new[] { new RosterEntry(Enemy().Id, 1) });
        var catalog = new ContentCatalog(new[] { Character() }, new[] { Enemy(70f) }, new[] { mode },
            new[] { skill, other, third }, new[] { tree });
        var random = new FixedRandom(99);
        var session = new RunSession(catalog, random, events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(default), events), 8, 8, 16);
        var saved = new RunSnapshot(RunSnapshot.CurrentVersion, mode.Id, Character().Id, 99, 1,
            new RandomState(0, 0, 0, 0, 0), 100f, 0f, 0f, default, 2, 0f, 0,
            new[] { skill.Id }, new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        session.Start(new RunConfig(mode.Id, Character().Id, 99, 1,
            SpawnPlan.Empty, saved));
        var snapshot = new WorldSnapshot(8) { Dt = 0.01f, HasGate = true, GatePosition = Vector3.Zero, SpawnPoints = new[] { new Vector3(0f, 0f, 7f) } };
        for (int i = 0; i < 2000 && session.State.StageIndex == 1; i++)
        {
            snapshot.EnemyCount = 1;
            snapshot.Enemies[0] = new EnemySense { Id = 1, Position = new Vector3(0f, 0f, 7f), HasLineOfSight = true };
            session.Tick(snapshot);
            if (session.State.IsSanctumOpen)
            { session.LeaveSanctum(); }
        }
        Assert.That(session.State.StageIndex, Is.EqualTo(2));
        var changes = events.Of<SteadyAimChanged>();
        Assert.That(changes.Count, Is.GreaterThan(1));
        Assert.That(changes[0].Stacks, Is.EqualTo(1));
        Assert.That(changes[changes.Count - 1].Stacks, Is.Zero);
    }

    private static SteadyAim Aim()
    {
        var aim = new SteadyAim(new SilentEvents());
        aim.PerStack.Base = 0.05f;
        return aim;
    }

    private static PlayerCombat Player(RecordingEvents events) =>
        new(Character(), events, new RecordingIntents(), 8);

    private static CharacterSpec Character() => new(new ContentId("character.test.bow"),
        new LocKey("character.test.name"), new LocKey("character.test.desc"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, 13f, 2f, 12f, 360f, 0.5f, 40f, 0.5f, firesWhileMoving: false),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 6f, 0.3f, 3f, 0.15f, 0f, 0f, 0.05f),
        volley: new VolleySpec(3, 30f, 1.5f));

    private static EnemySpec Enemy(float hp = 1000f) => new(new ContentId("enemy.test.husk"),
        new LocKey("enemy.test.name"), hp, 3.5f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f,
        30f, EnemyBehaviourKind.Static);
}
