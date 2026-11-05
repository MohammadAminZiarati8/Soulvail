using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using VContainer;

namespace Soulvail.Tests.Game.Authoring;

[TestFixture]
public sealed class SkirmisherPathTests
{
    private const string Data = "Assets/_Project/Data/";
    private static readonly ContentId Marksman = new("path.ranger.marksman");
    private static readonly ContentId Skirmisher = new("path.ranger.skirmisher");
    private static readonly ContentId RunningShot = new("skill.skirmisher.running-shot");
    private static readonly ContentId Ricochet = new("skill.skirmisher.ricochet-shot");
    private static readonly ContentId HitAndRunId = new("skill.skirmisher.hit-and-run");
    private static readonly ContentId SecondWind = new("skill.skirmisher.second-wind");
    private static readonly ContentId WhirlwindId = new("skill.skirmisher.whirlwind");

    [Test]
    public void Assets_MatchTheRankTables()
    {
        PathDefinition path = Load<PathDefinition>("Paths/Skirmisher");
        AssertRanks(path.Skills[0].Ranks, new[]
        {
            new[] { (PlayerStat.FireWhileMoving, 1f), (PlayerStat.MovingFireRate, -0.60f) },
            new[] { (PlayerStat.MovingFireRate, 0.15f) },
            new[] { (PlayerStat.MovingFireRate, 0.15f) },
            new[] { (PlayerStat.MovingFireRate, 0.15f) },
        });
        AssertRanks(path.Skills[2].Ranks, new[]
        {
            new[] { (PlayerStat.HitSpeedBurst, 0.15f) },
            new[] { (PlayerStat.HitSpeedBurst, 0.05f), (PlayerStat.MomentumPerMetre, 0.02f) },
            new[] { (PlayerStat.HitSpeedBurst, 0.05f), (PlayerStat.MomentumPerMetre, 0.01f) },
        });
    }

    [Test]
    public void Assets_MatchTheRankTable()
    {
        IReadOnlyList<SkillDefinition> ranks = Load<PathDefinition>("Paths/Skirmisher").Skills[1].Ranks;
        SkillSpec first = ranks[0].ToSpec();
        Assert.That(first.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(first.Active.Cooldown, Is.EqualTo(8f));
        Assert.That(first.Active.Trigger.Clauses.Count, Is.EqualTo(1));
        TriggerClause trigger = first.Active.Trigger.Clauses[0];
        Assert.That(trigger.Field, Is.EqualTo(TriggerField.EnemiesInAcquireRange));
        Assert.That(trigger.Comparison, Is.EqualTo(TriggerComparison.AtLeast));
        Assert.That(trigger.Threshold, Is.EqualTo(3f));
        Assert.That(first.Active.OnCast.Count, Is.EqualTo(1));
        Assert.That(((RicochetShot)first.Active.OnCast[0]).DamageOfWeapon, Is.EqualTo(1f));

        var stats = new[] { PlayerStat.RicochetHits, PlayerStat.RicochetDamage, PlayerStat.RicochetHits };
        var values = new[] { 1f, 0.25f, 1f };
        for (int i = 1; i < ranks.Count; i++)
        {
            SkillSpec rank = ranks[i].ToSpec();
            Assert.That(rank.Kind, Is.EqualTo(SkillKind.Upgrade));
            Assert.That(rank.ParentId, Is.EqualTo(new ContentId(ranks[i - 1].Id)));
            Assert.That(rank.Effects.Count, Is.EqualTo(i == 3 ? 2 : 1));
            var stat = (ModifyStat)rank.Effects[0];
            Assert.That(stat.Stat, Is.EqualTo(stats[i - 1]));
            Assert.That(stat.Target, Is.EqualTo(StatTarget.Player));
            Assert.That(stat.Kind, Is.EqualTo(ModifierKind.Flat));
            Assert.That(stat.Value, Is.EqualTo(values[i - 1]));
        }

        var cooldown = (ModifySkillCooldown)ranks[3].ToSpec().Effects[1];
        Assert.That(cooldown.SkillId, Is.EqualTo(Ricochet));
        Assert.That(cooldown.Kind, Is.EqualTo(ModifierKind.Flat));
        Assert.That(cooldown.Value, Is.EqualTo(-2f));
    }

    [Test]
    public void SecondWind_Assets_MatchTheRankTable()
    {
        IReadOnlyList<SkillDefinition> ranks = Load<PathDefinition>("Paths/Skirmisher").Skills[3].Ranks;
        AssertRanks(ranks, new[]
        {
            new[] { (PlayerStat.MovementSkillCooldown, -0.5f) },
            new[] { (PlayerStat.MovementSkillCooldown, -0.5f), (PlayerStat.RollGuardSeconds, 1f) },
            new[] { (PlayerStat.MovementSkillCharges, 1f) },
        });
    }

    [Test]
    public void Whirlwind_Assets_MatchTheRankTable()
    {
        IReadOnlyList<SkillDefinition> ranks = Load<PathDefinition>("Paths/Skirmisher").Skills[4].Ranks;
        SkillSpec first = ranks[0].ToSpec();
        Assert.That(first.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(first.Active.Cooldown, Is.EqualTo(10f));
        Assert.That(first.Active.Trigger.Clauses.Count, Is.EqualTo(1));
        TriggerClause trigger = first.Active.Trigger.Clauses[0];
        Assert.That(trigger.Field, Is.EqualTo(TriggerField.EnemiesWithin6m));
        Assert.That(trigger.Comparison, Is.EqualTo(TriggerComparison.AtLeast));
        Assert.That(trigger.Threshold, Is.EqualTo(3f));
        Assert.That(first.Active.OnCast.Count, Is.EqualTo(1));
        Assert.That(((Whirlwind)first.Active.OnCast[0]).DamageOfWeapon, Is.EqualTo(0.6f));

        AssertWhirlwindUpgrade(ranks[1].ToSpec(), first.Id, PlayerStat.WhirlwindArrows, 4f, 1);
        SkillSpec third = ranks[2].ToSpec();
        AssertWhirlwindUpgrade(third, ranks[1].ToSpec().Id, PlayerStat.WhirlwindRings, 1f, 2);
        var cooldown = (ModifySkillCooldown)third.Effects[1];
        Assert.That(cooldown.SkillId, Is.EqualTo(WhirlwindId));
        Assert.That(cooldown.Kind, Is.EqualTo(ModifierKind.Flat));
        Assert.That(cooldown.Value, Is.EqualTo(-2f));
    }

    private static void AssertWhirlwindUpgrade(SkillSpec rank, ContentId parent, PlayerStat stat, float value, int count)
    {
        Assert.That(rank.Kind, Is.EqualTo(SkillKind.Upgrade));
        Assert.That(rank.ParentId, Is.EqualTo(parent));
        Assert.That(rank.Effects.Count, Is.EqualTo(count));
        var effect = (ModifyStat)rank.Effects[0];
        Assert.That(effect.Stat, Is.EqualTo(stat));
        Assert.That(effect.Target, Is.EqualTo(StatTarget.Player));
        Assert.That(effect.Kind, Is.EqualTo(ModifierKind.Flat));
        Assert.That(effect.Value, Is.EqualTo(value));
    }

    [Test]
    public void Path_WhirlwindAtSeventeen()
    {
        AssertSkill(Spec().Skills[4], WhirlwindId, 3, 17);
    }

    [Test]
    public void Path_SecondWindAtFourteen()
    {
        AssertSkill(Spec().Skills[3], SecondWind, 3, 14);
    }

    [Test]
    public void Path_RicochetIsSecondAtEleven()
    {
        PathSpec path = Spec();
        Assert.That(path.Id, Is.EqualTo(Skirmisher));
        Assert.That(path.CharacterId, Is.EqualTo(new ContentId("character.ranger")));
        Assert.That(path.Skills.Count, Is.EqualTo(5));
        Assert.That(path.NodeCount, Is.EqualTo(17));
        AssertSkill(path.Skills[0], RunningShot, 4, 11);
        AssertSkill(path.Skills[1], Ricochet, 4, 11);
        AssertSkill(path.Skills[2], HitAndRunId, 3, 14);
    }

    [Test]
    public void Ranger_OffersMarksmanAndSkirmisher()
    {
        CharacterDefinition ranger = Load<CharacterDefinition>("Characters/Ranger");
        Assert.That(ranger.PathSlots.Count, Is.EqualTo(3));
        Assert.That(ranger.PathSlots[0].Id, Is.EqualTo(Marksman.Value));
        Assert.That(ranger.PathSlots[1].Id, Is.EqualTo(Skirmisher.Value));
        Assert.That(ranger.PathSlots[2], Is.Null);
        Assert.That(ranger.ToSpec().PathOpensAtLevel, Is.EqualTo(11));
        Assert.That(ranger.ToSpec().PathIds, Is.EqualTo(new[] { Marksman, Skirmisher }));

        using IObjectResolver boot = Boot();
        var catalog = boot.Resolve<ContentCatalog>();
        Assert.That(catalog.Paths.Count, Is.EqualTo(2));
        Assert.That(catalog.TryGetPath(Skirmisher, out _), Is.True);
    }

    [TestCase(11, false)]
    [TestCase(14, true)]
    [TestCase(16, true)]
    [TestCase(17, true)]
    public void Choose_Skirmisher_InstallsPathSkills(int level, bool hitAndRunAvailable)
    {
        using var events = new DomainEventHub();
        RunSession session = StartRanger(level, events);

        Assert.That(session.State.IsPathChoiceOpen, Is.True);
        Assert.That(session.State.PathCandidates, Is.EqualTo(new[] { Marksman, Skirmisher }));
        int baseNodes = session.State.TreeNodeCount;
        session.ChoosePath(Skirmisher);

        Assert.That(session.State.PathId, Is.EqualTo(Skirmisher));
        Assert.That(session.State.IsPathChoiceOpen, Is.False);
        Assert.That(session.State.TreeNodeCount, Is.EqualTo(baseNodes + 17));
        Assert.That(session.State.UnlockLevelOf(Ricochet), Is.EqualTo(11));
        Assert.That(session.State.IsNodeAvailable(Ricochet), Is.True);
        Assert.That(session.State.UnlockLevelOf(RunningShot), Is.EqualTo(11));
        Assert.That(session.State.UnlockLevelOf(HitAndRunId), Is.EqualTo(14));
        Assert.That(session.State.IsNodeAvailable(RunningShot), Is.True);
        Assert.That(session.State.IsNodeAvailable(HitAndRunId), Is.EqualTo(hitAndRunAvailable));
        Assert.That(session.State.IsNodeAvailable(SecondWind), Is.EqualTo(hitAndRunAvailable));
        Assert.That(session.State.IsNodeAvailable(WhirlwindId), Is.EqualTo(level >= 17));
        session.ChoosePath(Marksman);
        Assert.That(session.State.PathId, Is.EqualTo(Skirmisher));
        session.End();
    }

    [Test]
    public void Path_WithoutLook_Loads()
    {
        PathDefinition asset = Load<PathDefinition>("Paths/Skirmisher");
        Assert.That(asset.Look, Is.Null);
        Assert.That(asset.ToSpec(new ContentId("character.ranger")).Id, Is.EqualTo(Skirmisher));
    }

    [Test]
    public void Loc_Keys()
    {
        PathSpec path = Spec();
        var keys = new List<LocKey> { path.NameKey, path.DescriptionKey, new LocKey("ui.combat.dodged") };
        foreach (PathDefinition.SkillField skill in Load<PathDefinition>("Paths/Skirmisher").Skills)
        {
            foreach (SkillDefinition rank in skill.Ranks)
            {
                SkillSpec spec = rank.ToSpec();
                keys.Add(spec.NameKey);
                keys.Add(spec.DescriptionKey);
            }
        }

        var english = Load<LocalizationTable>("Localisation/English").ToDictionary();
        var pseudo = Load<LocalizationTable>("Localisation/Pseudo").ToDictionary();
        foreach (LocKey key in keys)
        {
            Assert.That(english.ContainsKey(key), Is.True, key.Key);
            Assert.That(pseudo.ContainsKey(key), Is.True, key.Key);
            Assert.That(english[key], Is.Not.Empty, key.Key);
            Assert.That(pseudo[key], Does.StartWith("[").And.EndWith("]"), key.Key);
            Assert.That(pseudo[key], Is.Not.EqualTo(english[key]), key.Key);
        }

        Assert.That(english[new LocKey("skill.skirmisher.ricochet-shot.name")], Is.EqualTo("Ricochet Shot"));
        Assert.That(english[new LocKey("skill.skirmisher.ricochet-shot.description")], Is.EqualTo("An arrow that bounces between 3 enemies within 5 m."));
        Assert.That(english[new LocKey("skill.skirmisher.ricochet-shot-2.description")], Is.EqualTo("Bounces between 4 enemies."));
        Assert.That(english[new LocKey("skill.skirmisher.ricochet-shot-3.description")], Is.EqualTo("+25% damage."));
        Assert.That(english[new LocKey("skill.skirmisher.ricochet-shot-4.description")], Is.EqualTo("Bounces between 5. Cooldown 6 s."));
        Assert.That(english[new LocKey("ui.combat.dodged")], Is.EqualTo("Dodged"));
        Assert.That(english[new LocKey("skill.skirmisher.second-wind.name")], Is.EqualTo("Second Wind"));
        Assert.That(english[new LocKey("skill.skirmisher.second-wind.description")], Is.EqualTo("Roll cooldown −0.5 s."));
        Assert.That(english[new LocKey("skill.skirmisher.second-wind-2.description")],
            Is.EqualTo("Roll cooldown −1 s. The first hit within 1 s after a roll misses."));
        Assert.That(english[new LocKey("skill.skirmisher.second-wind-3.description")], Is.EqualTo("Two roll charges."));
        Assert.That(english[new LocKey("skill.skirmisher.whirlwind.name")], Is.EqualTo("Whirlwind"));
        Assert.That(english[new LocKey("skill.skirmisher.whirlwind.description")],
            Is.EqualTo("Spin and loose two rings of 12 arrows all round."));
        Assert.That(english[new LocKey("skill.skirmisher.whirlwind-2.description")], Is.EqualTo("16 arrows a ring."));
        Assert.That(english[new LocKey("skill.skirmisher.whirlwind-3.description")], Is.EqualTo("Three rings. Cooldown 8 s."));
        Assert.That(english[path.NameKey], Is.EqualTo("Skirmisher"));
        Assert.That(english[path.DescriptionKey], Is.EqualTo("Never stop. Shoot on the run, roll often, strike as you pass."));
    }

    [Test]
    public void Icons_AreRegisteredForPathSkillsAndTheBuff()
    {
        using IObjectResolver boot = Boot();
        var icons = boot.Resolve<SkillIconBook>();
        foreach (PathDefinition.SkillField skill in Load<PathDefinition>("Paths/Skirmisher").Skills)
        {
            Sprite icon = skill.Ranks[0].Icon;
            Assert.That(icon, Is.Not.Null);
            foreach (SkillDefinition rank in skill.Ranks)
            {
                Assert.That(icons.For(new ContentId(rank.Id)), Is.EqualTo(icon), rank.Id);
            }
        }

        var statusIcons = Load<StatusIconBook>("StatusIconBook");
        Sprite buff = statusIcons.For(new ContentId(HitAndRun.StatusKey));
        Assert.That(buff, Is.EqualTo(icons.For(HitAndRunId)));
        Assert.That(buff, Is.Not.EqualTo(statusIcons.For(default)));
        Sprite guard = statusIcons.For(new ContentId("status.second-wind"));
        Assert.That(guard, Is.EqualTo(icons.For(SecondWind)));
        Assert.That(guard, Is.Not.EqualTo(statusIcons.For(default)));
    }

    internal static RunSession StartRanger(int level, DomainEventHub events)
    {
        using IObjectResolver boot = Boot();
        var authored = boot.Resolve<ContentCatalog>();
        CharacterSpec ranger = authored.Characters[0];
        EnemySpec enemy = Load<EnemyDefinition>("Enemies/Husk").ToSpec();
        var mode = new ModeSpec(new ContentId("mode.test.skirmisher"), new LocKey("mode.test.name"),
            1, true, 0, new ScalingSpec(new BudgetCurve(4f, 0f, 0f),
                new WaveCurve(1, 1000, 1, 1), new ConcurrencyCurve(8, 1000),
                new StatCurve(0.06f, 4f, 1, 1), new StatCurve(0.035f, 3f, 1, 1),
                new StatCurve(0.02f, 1.3f, 5, 0)), new XpCurve(20f, 12f, 1.4f),
            new[] { new RosterEntry(enemy.Id, 1) }, hasFreePick: true);
        var catalog = new ContentCatalog(authored.Characters, new[] { enemy }, new[] { mode },
            authored.Skills, authored.Trees, paths: authored.Paths);
        var random = new FixedRandom(7);

        var session = new RunSession(catalog, random, events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(default), events), 64, 8, 64);
        var saved = new RunSnapshot(RunSnapshot.CurrentVersion, mode.Id, ranger.Id, 7, 1,
            new RandomState(101, 102, 103, 104, 105), ranger.MaxHp, 0f, 0f, default, level, 0f, level - 1,
            Array.Empty<ContentId>(), new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        session.Start(new RunConfig(mode.Id, ranger.Id, 7, 1, SpawnPlan.Empty, saved));

        return session;
    }

    private static void AssertRanks(
        IReadOnlyList<SkillDefinition> ranks, (PlayerStat Stat, float Value)[][] expected)
    {
        Assert.That(ranks.Count, Is.EqualTo(expected.Length));
        for (int rank = 0; rank < expected.Length; rank++)
        {
            SkillSpec spec = ranks[rank].ToSpec();
            Assert.That(spec.Kind, Is.EqualTo(rank == 0 ? SkillKind.Passive : SkillKind.Upgrade), spec.Id.Value);
            Assert.That(spec.ParentId, Is.EqualTo(rank == 0 ? default : new ContentId(ranks[rank - 1].Id)), spec.Id.Value);
            Assert.That(spec.Effects.Count, Is.EqualTo(expected[rank].Length), spec.Id.Value);

            for (int i = 0; i < expected[rank].Length; i++)
            {
                Assert.That(spec.Effects[i], Is.TypeOf<ModifyStat>(), spec.Id.Value);
                var effect = (ModifyStat)spec.Effects[i];
                Assert.That(effect.Target, Is.EqualTo(StatTarget.Player), spec.Id.Value);
                Assert.That(effect.Kind, Is.EqualTo(ModifierKind.Flat), spec.Id.Value);
                Assert.That(effect.Stat, Is.EqualTo(expected[rank][i].Stat), spec.Id.Value);
                Assert.That(effect.Value, Is.EqualTo(expected[rank][i].Value), spec.Id.Value);
            }
        }
    }

    private static void AssertSkill(PathSkillSpec skill, ContentId first, int count, int level)
    {
        Assert.That(skill.UnlockLevel, Is.EqualTo(level));
        Assert.That(skill.Ranks.TierCount, Is.EqualTo(count));
        for (int rank = 1; rank <= count; rank++)
        {
            ContentId id = rank == 1 ? first : new ContentId($"{first.Value}-{rank}");
            Assert.That(skill.Ranks.Tier(rank), Is.EqualTo(new[] { id }));
        }
    }

    private static IObjectResolver Boot()
    {
        string[] guids = AssetDatabase.FindAssets("t:SkillDefinition", new[] { Data + "Skills/Ranger" });
        var skills = new SkillDefinition[guids.Length];
        for (int i = 0; i < guids.Length; i++)
        {
            skills[i] = AssetDatabase.LoadAssetAtPath<SkillDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
        }

        var builder = new ContainerBuilder();
        BootInstaller.Install(builder, new[] { Load<CharacterDefinition>("Characters/Ranger") },
            Array.Empty<EnemyDefinition>(), Array.Empty<ModeDefinition>(), skills,
            new[] { Load<SkillTreeDefinition>("Trees/Ranger") }, Load<LocalizationTable>("Localisation/English"));
        return builder.Build();
    }

    private static PathSpec Spec() =>
        Load<PathDefinition>("Paths/Skirmisher").ToSpec(new ContentId("character.ranger"));

    private static T Load<T>(string relative) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>($"{Data}{relative}.asset");
        Assert.That(asset, Is.Not.Null, relative);
        return asset;
    }
}
