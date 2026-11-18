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
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Run;

[TestFixture]
public sealed class ShardbackProbeRoutingTests
{
    private ShardbackProbeKey Key() => new(Guid.NewGuid(), 1);

    [Test]
    public void Contract_Invalid_Refused()
    {
        Assert.That(default(ShardbackProbeKey).IsValid, Is.False);
        Assert.That(default(ShardbackStop).IsValid, Is.False);
        Assert.Throws<ArgumentException>(() => new ShardbackProbeKey(Guid.Empty, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShardbackProbeKey(Guid.NewGuid(), 0));
        ShardbackProbeKey key = Key();
        Assert.Throws<ArgumentException>(() => new ShardbackStop(0, key, 0, ShardbackStopKind.None, 0, -1));
        Assert.Throws<ArgumentException>(() => new ShardbackStop(1, default, 0, ShardbackStopKind.None, 0, -1));
        foreach (int lane in new[] { -1, 4 })
        {
            Assert.Throws<ArgumentException>(() => new ShardbackStop(1, key, lane, ShardbackStopKind.None, 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LaneProbeIntent(1, Vector3.Zero, Vector3.UnitZ, 1, 1, key, lane));
        }
        foreach (float distance in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.Throws<ArgumentException>(() => new ShardbackStop(1, key, 0, ShardbackStopKind.Cover, distance, -1));
        }
        foreach (ShardbackStopKind kind in new[] { (ShardbackStopKind)(-1), (ShardbackStopKind)5 })
        {
            Assert.Throws<ArgumentException>(() => new ShardbackStop(1, key, 0, kind, 0, -1));
        }
        foreach (ShardbackStopKind kind in Enum.GetValues(typeof(ShardbackStopKind)))
        {
            for (int lane = 0; lane <= 3; lane++)
            {
                int slot = kind == ShardbackStopKind.Pillar ? 0 : -1;
                Assert.That(new ShardbackStop(1, key, lane, kind, 0, slot).IsValid, Is.True);
            }
            Assert.Throws<ArgumentException>(() => new ShardbackStop(1, key, 0, kind, 0,
                kind == ShardbackStopKind.Pillar ? -1 : 0));
        }
        foreach (int slot in new[] { 0, 1, 2 })
        {
            Assert.That(new ShardbackStop(1, key, 0, ShardbackStopKind.Pillar, 2, slot).IsValid, Is.True);
        }
        Assert.Throws<ArgumentException>(() => new ShardbackStop(1, key, 0, ShardbackStopKind.Pillar, 0, 3));
        Assert.Throws<ArgumentException>(() => new LaneProbeIntent(1, Vector3.Zero, Vector3.UnitZ, 1, 1, default, 0));
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LaneProbeIntent(1, Vector3.Zero, Vector3.UnitZ, bad, 1, key, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LaneProbeIntent(1, Vector3.Zero, Vector3.UnitZ, 1, bad, key, 0));
        }
        foreach (Vector3 direction in new[] { Vector3.Zero, Vector3.UnitY })
        {
            Assert.Throws<ArgumentException>(() => new LaneProbeIntent(1, Vector3.Zero, direction, 1, 1, key, 0));
        }
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            foreach (Vector3 vector in new[] { new Vector3(bad, 0, 1), new Vector3(0, bad, 1), new Vector3(0, 0, bad) })
            {
                Assert.Throws<ArgumentException>(() => new LaneProbeIntent(1, vector, Vector3.UnitZ, 1, 1, key, 0));
                Assert.Throws<ArgumentException>(() => new LaneProbeIntent(1, Vector3.Zero, vector, 1, 1, key, 0));
            }
        }
        Assert.That(new LaneProbeIntent(1, Vector3.Zero, Vector3.Zero, -1, float.NaN).Key.IsValid, Is.False,
            "Legacy construction remains permissive.");
    }

    [Test]
    public void Report_Route_OnlyLiveListener()
    {
        var events = new RecordingEvents();
        var intents = new RecordingIntents();
        var random = new FixedRandom(0.5f);
        var session = new RunSession(Catalog(), random, events, intents,
            new RunRecorder(random, new FixedClock(default), events), 8, 8, 8);
        Assert.Throws<InvalidOperationException>(() => session.ReportShardbackStop(default));
        session.Start(new RunConfig(new ContentId("mode.test"), Character().Id, 0, 1, SpawnPlan.Empty, null));
        var enemies = (EnemySystem)typeof(RunState).GetProperty("Enemies", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(session.State);
        EnemyAgent agent = enemies.Registry.Spawn(Body(), Vector3.Zero);
        var listener = new Listener();
        var bossSpec = new BossSpec(new ContentId("boss.test"), Body().Id,
            new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) }, 1.5f);
        var boss = new BossBehaviour(agent, bossSpec, listener);
        PropertyInfo behaviour = typeof(EnemyAgent).GetProperty("Behaviour");
        var report = new ShardbackStop(agent.Id, Key(), 3, ShardbackStopKind.Pillar, 2.5f, 2);
        behaviour.SetValue(agent, boss);
        session.ReportShardbackStop(report);
        session.ReportShardbackStop(report);
        Assert.That(listener.Count, Is.EqualTo(2), "Correlation and duplicates belong to the listener.");
        Assert.That(listener.Last, Is.EqualTo(report));
        session.ReportShardbackStop(default);
        session.ReportShardbackStop(new ShardbackStop(int.MaxValue, Key(), 0, ShardbackStopKind.Unknown, 0, -1));
        behaviour.SetValue(agent, listener);
        session.ReportShardbackStop(report);
        behaviour.SetValue(agent, new BossBehaviour(agent, bossSpec));
        session.ReportShardbackStop(report);
        behaviour.SetValue(agent, boss);
        agent.Health.ApplyDamage(Body().MaxHp * 0.6f, 0);
        boss.Tick(new EnemyTickContext(0.01f, 0.01f, new PlayerCombat(Character(), events, intents, 8),
            intents, events, new ProjectileSystem(events, 8), enemies));
        Assert.That(boss.IsInBeat, Is.True);
        session.ReportShardbackStop(report);
        Assert.That(listener.Count, Is.EqualTo(2));
        boss.Tick(new EnemyTickContext(2f, 2.01f, new PlayerCombat(Character(), events, intents, 8),
            intents, events, new ProjectileSystem(events, 8), enemies));
        Assert.That(boss.IsInBeat, Is.False);
        session.ReportShardbackStop(report);
        Assert.That(listener.Count, Is.EqualTo(3));
        agent.Health.SetExternalInvulnerable(false);
        agent.Health.ApplyDamage(10000f, 3f);
        Assert.That(agent.IsAlive, Is.False);
        session.ReportShardbackStop(report);
        Assert.That(listener.Count, Is.EqualTo(3));
        session.End();
        Assert.Throws<InvalidOperationException>(() => session.ReportShardbackStop(report));
    }

    private sealed class Listener : IEnemyBehaviour, IShardbackProbeListener
    {
        public int Count;
        public ShardbackStop Last;
        public void OnShardbackStop(in ShardbackStop report) { Count++; Last = report; }
        public void Tick(in EnemyTickContext ctx) { }
        public void Reset() { }
    }
    private ContentCatalog Catalog()
    {
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var boss = new BossSpec(new ContentId("boss.mossguard_warlord"), Body().Id,
            new[] { new BossPhaseSpec(1f) }, 1.5f);
        return new ContentCatalog(new[] { Character() }, new[] { Body() }, new[] { mode }, bosses: new[] { boss });
    }

    private EnemySpec Body() => new(new ContentId("enemy.mossguard_warlord"), new LocKey("enemy.warlord.name"),
        3200f, 1.8f, 8, 40, 300f, false, 20f, 2.5f, 0.9f, 1.2f, 40f, EnemyBehaviourKind.Boss);

    private CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
