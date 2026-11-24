using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Progression;

[TestFixture]
public sealed class CampaignBossXpTests
{
    private const int Capacity = 32;
    private const float BodyXp = 300f;
    private const float RegularXp = 12f;
    private const float Lethal = 1e9f;
    private const float Tolerance = 0.001f;
    private const BindingFlags InternalInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly CampaignXpProfile _profile = new CampaignXpProfile();
    private readonly ContentId _bossId = new ContentId("boss.warden");
    private readonly ContentId _bodyId = new ContentId("enemy.warden");
    private readonly ContentId _escortId = new ContentId("enemy.escort");
    private readonly ContentId _huskId = new ContentId("enemy.husk");

    [Test]
    public void CampaignProfile_UsesSharedCurveAndBudgets()
    {
        int[] budgets =
        {
            54, 135, 174, 234, 240,
            246, 312, 336, 408, 396,
            384, 432, 459, 491, 488,
            484, 538, 546, 587, 580,
            572, 634, 689, 737, 731,
            725, 755, 782, 800, 820, 820,
        };

        ModeSpec mode = Mode(_profile);
        Assert.That(mode.CampaignXp, Is.SameAs(_profile));
        Assert.That(mode.Xp.MaxLevel, Is.EqualTo(30));
        Assert.That(_profile.BossFraction, Is.EqualTo(0.75f));
        for (int level = 2; level <= 30; level++)
        {
            Assert.That(mode.Xp.ToReach(level), Is.EqualTo(20f + 5.5f * MathF.Pow(level, 1.4f)));
        }

        for (int stage = 1; stage <= budgets.Length; stage++)
        {
            Assert.That(_profile.RegularBudget(stage), Is.EqualTo(budgets[stage - 1]), $"Stage {stage}");
        }
    }

    [TestCase(0f, false)]
    [TestCase(0.99f, false)]
    [TestCase(0f, true)]
    [TestCase(0.99f, true)]
    public void BossGrant_EmptyAndNearlyFullBars(float initialFraction, bool boosted)
    {
        for (int level = 1; level <= 28; level++)
        {
            var events = new RecordingEvents();
            var tracker = new LevelTracker(_profile.Curve, events);
            while (tracker.Level < level)
            {
                tracker.Grant(tracker.XpToNext);
            }

            float cost = tracker.XpToNext;
            tracker.Grant(initialFraction * cost);
            if (boosted)
            {
                tracker.XpGain.Add(new Modifier(ModifierKind.PercentAdd, 0.5f, this));
            }

            events.Clear();
            int points = tracker.PendingLevelUps;
            tracker.GrantBoss(_profile.BossFraction);

            int levelsGained = initialFraction > 0f ? 1 : 0;
            Assert.That(tracker.Level, Is.EqualTo(level + levelsGained));
            Assert.That(tracker.PendingLevelUps, Is.EqualTo(points + levelsGained));
            Assert.That(tracker.Xp, Is.EqualTo((0.75f + initialFraction - levelsGained) * cost).Within(Tolerance));
            Assert.That(events.Single<XpChanged>().Gained, Is.EqualTo(0.75f * cost));
            Assert.That(events.Count<LeveledUp>(), Is.EqualTo(levelsGained));
            Assert.That(events.All[^1], Is.TypeOf<XpChanged>());
        }
    }

    [Test]
    public void BossGrant_ReachesCapThenStaysSilent()
    {
        var events = new RecordingEvents();
        var tracker = new LevelTracker(_profile.Curve, events);
        while (tracker.Level < 29)
        {
            tracker.Grant(tracker.XpToNext);
        }

        tracker.Grant(0.99f * tracker.XpToNext);
        int points = tracker.PendingLevelUps;
        events.Clear();

        tracker.GrantBoss(_profile.BossFraction);
        Assert.That(tracker.IsMaxLevel, Is.True);
        Assert.That(tracker.XpFraction, Is.EqualTo(1f));
        Assert.That(tracker.Xp, Is.Zero);
        Assert.That(tracker.PendingLevelUps, Is.EqualTo(points + 1));
        Assert.That(events.All.Select(evt => evt.GetType()), Is.EqualTo(new[] { typeof(LeveledUp), typeof(XpChanged) }));

        events.Clear();
        tracker.GrantBoss(_profile.BossFraction);
        Assert.That(events.All, Is.Empty);
        Assert.That(tracker.Xp, Is.Zero);
        Assert.That(tracker.XpFraction, Is.EqualTo(1f));
        Assert.That(tracker.PendingLevelUps, Is.EqualTo(points + 1));
    }

    [TestCase(true, 1)]
    [TestCase(true, 2)]
    [TestCase(false, 1)]
    public void CampaignBoss_KillPaysOnceAndAddsPayZero(bool survives, int bossCount)
    {
        var events = new RecordingEvents();
        (RunSession session, EnemySystem enemies, PlayerCombat player, EnemyAgent boss) =
            BossStage(Mode(_profile), events);
        EnemyAgent secondBoss = bossCount == 2 ? enemies.SpawnBoss(_bossId, Vector3.UnitX) : null;
        EnemyAgent summon = enemies.SpawnSummon(_huskId, Vector3.UnitX);
        var escort = (EnemyAgent)typeof(EnemySystem).GetMethod("SpawnEscort", InternalInstance)
            .Invoke(enemies, new object[] { _escortId, Vector3.UnitX, null, 0f, 0f });

        Assert.That(enemies.ApplyDamage(escort.Id, Lethal, session.State.Time, player).Killed, Is.True);
        session.Tick(Snapshot(0.1f));
        EnemyAgent child = enemies.Registry.Alive.ToArray().Single(agent => agent.IsAlive && agent.Id != summon.Id
            && agent.Spec.Id == _huskId);
        Assert.That(child.IsSummon, Is.True);
        Assert.That(events.Count<XpChanged>(), Is.Zero);

        EnemyAgent regular = enemies.Spawn(_huskId, Vector3.UnitX);
        Assert.That(enemies.ApplyDamage(regular.Id, Lethal, session.State.Time, player).Killed, Is.True);
        Assert.That(enemies.ApplyDamage(boss.Id, Lethal, session.State.Time, player).Killed, Is.True);
        Assert.That(enemies.ApplyDamage(boss.Id, Lethal, session.State.Time, player).Killed, Is.False);
        Assert.That(summon.IsAlive, Is.False);
        Assert.That(child.IsAlive, Is.False);
        if (secondBoss is not null)
        {
            Assert.That(enemies.ApplyDamage(secondBoss.Id, Lethal, session.State.Time, player).Killed, Is.True);
        }

        if (!survives)
        {
            Assert.That(player.ApplyDamage(Lethal, session.State.Time).Killed, Is.True);
        }

        session.Tick(Snapshot());
        if (!survives)
        {
            Assert.That(session.IsRunning, Is.False);
            Assert.That(events.Count<XpChanged>(), Is.Zero);
            Assert.That(events.Count<StageCleared>(), Is.Zero);
            Assert.That(enemies.PendingXp, Is.Zero);
            Assert.That(typeof(EnemySystem).GetMethod("DrainBossRewards", InternalInstance).Invoke(enemies, null),
                Is.EqualTo(0));
            return;
        }

        float firstReward = 0.75f * _profile.Curve.ToReach(2);
        float[] rewards = bossCount == 1
            ? new[] { RegularXp, firstReward }
            : new[] { RegularXp, firstReward, 0.75f * _profile.Curve.ToReach(3) };
        Assert.That(events.Of<XpChanged>().Select(evt => evt.Gained), Is.EqualTo(rewards));
        Assert.That(session.State.Level, Is.EqualTo(2));
        Assert.That(events.All.Where(evt => evt is XpChanged or LeveledUp or StageCleared)
            .Select(evt => evt.GetType()), Is.EqualTo(bossCount == 1
                ? new[] { typeof(XpChanged), typeof(LeveledUp), typeof(XpChanged), typeof(StageCleared) }
                : new[] { typeof(XpChanged), typeof(LeveledUp), typeof(XpChanged), typeof(XpChanged), typeof(StageCleared) }));

        session.Tick(Snapshot());
        Assert.That(events.Count<XpChanged>(), Is.EqualTo(rewards.Length));
    }

    [Test]
    public void LegacyBoss_KeepsFlatReward()
    {
        var events = new RecordingEvents();
        (RunSession session, EnemySystem enemies, PlayerCombat player, EnemyAgent boss) =
            BossStage(Mode(null), events);

        Assert.That(enemies.ApplyDamage(boss.Id, Lethal, session.State.Time, player).Killed, Is.True);
        session.Tick(Snapshot());

        Assert.That(events.Single<XpChanged>().Gained, Is.EqualTo(BodyXp));
    }

    private ModeSpec Mode(CampaignXpProfile profile) => new ModeSpec(
        new ContentId("mode.test"), new LocKey("mode.test.name"), 1, true, 0, Scalings.Design(), Scalings.Xp(),
        new[] { new RosterEntry(_huskId, 1) },
        bossRoster: new[] { new BossRosterEntry(_bossId, everyNStages: 0, atStage: 5) },
        campaignXp: profile);

    private (RunSession, EnemySystem, PlayerCombat, EnemyAgent) BossStage(ModeSpec mode, RecordingEvents events)
    {
        var character = new CharacterSpec(
            new ContentId("character.oathbound"),
            new LocKey("character.oathbound.name"),
            new LocKey("character.oathbound.description"),
            100f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f),
            new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
        var catalog = new ContentCatalog(
            new[] { character },
            new[]
            {
                Body(_bodyId, EnemyBehaviourKind.Boss, BodyXp),
                Body(_huskId, EnemyBehaviourKind.Static, RegularXp),
                Body(_escortId, EnemyBehaviourKind.Static, RegularXp, new BroodSpec(_huskId, 1, 0.05f, 1f)),
            },
            new[] { mode },
            bosses: new[] { new BossSpec(_bossId, _bodyId, new[] { new BossPhaseSpec(1f) }, 1.5f) });
        var random = new FixedRandom();
        var session = new RunSession(catalog, random, events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(default), events), Capacity, 28, 8);
        session.Start(new RunConfig(mode.Id, character.Id, 0, 5,
            new SpawnPlan(Array.Empty<SpawnPlan.Entry>()), restore: null));

        for (int tick = 0; tick < 1_200 && events.Count<EnemySpawned>() == 0; tick++)
        {
            session.Tick(Snapshot());
        }

        var enemies = (EnemySystem)typeof(RunState).GetProperty("Enemies", InternalInstance).GetValue(session.State);
        var player = (PlayerCombat)typeof(RunState).GetProperty("Combat", InternalInstance).GetValue(session.State);
        EnemyAgent boss = enemies.Registry.Alive.ToArray().Single();
        Assert.That(boss.Spec.Id, Is.EqualTo(_bodyId));
        return (session, enemies, player, boss);
    }

    private static EnemySpec Body(ContentId id, EnemyBehaviourKind behaviour, float xp, BroodSpec brood = null) =>
        new EnemySpec(id, new LocKey(id.Value + ".name"), 100f, 2f, 1, 4, xp, false,
            8f, 1.2f, 0.4f, 0.6f, 30f, behaviour, brood: brood);

    private static WorldSnapshot Snapshot(float dt = 1f / 60f) => new WorldSnapshot(Capacity)
    {
        Dt = dt,
        HasGate = true,
        GatePosition = new Vector3(0f, 0f, 18f),
        SpawnPoints = new[] { new Vector3(0f, 0f, 10f) },
    };
}
