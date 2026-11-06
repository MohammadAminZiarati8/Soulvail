using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using UnityEditor;
using UnityEngine;
using VContainer;

namespace Soulvail.Tests.Game.Authoring;

[TestFixture]
public sealed class MarksmanPathTests
{
    private const string Data = "Assets/_Project/Data/";

    [Test]
    public void Marksman_HasItsSkillsInOrder()
    {
        PathSpec path = Marksman().ToSpec(new ContentId(Ranger().Id));

        Assert.That(path.Id, Is.EqualTo(new ContentId("path.ranger.marksman")));
        Assert.That(path.CharacterId, Is.EqualTo(new ContentId("character.ranger")));
        Assert.That(path.Skills.Count, Is.EqualTo(6));
        Assert.That(path.NodeCount, Is.EqualTo(20));

        var stems = new[] { "steady-aim", "rain-of-arrows", "binding-shot", "eagle-eye", "hunters-snare", "killshot" };
        var counts = new[] { 4, 4, 3, 3, 3, 3 };
        var levels = new[] { 11, 11, 14, 14, 17, 20 };
        for (int skill = 0; skill < 6; skill++)
        {
            PathSkillSpec row = path.Skills[skill];
            string stem = stems[skill];
            int count = counts[skill];

            Assert.That(row.UnlockLevel, Is.EqualTo(levels[skill]));
            Assert.That(row.Ranks.TierCount, Is.EqualTo(count));

            for (int rank = 1; rank <= count; rank++)
            {
                string suffix = rank == 1 ? string.Empty : $"-{rank}";

                Assert.That(row.Ranks.Tier(rank), Is.EqualTo(new[] { new ContentId($"skill.marksman.{stem}{suffix}") }));
            }
        }
    }

    [Test]
    public void Marksman_TwentyNodes()
    {
        PathSpec path = Marksman().ToSpec(new ContentId(Ranger().Id));
        Assert.That(path.NodeCount, Is.EqualTo(20));
        Assert.That(path.NodeCount + Load<SkillTreeDefinition>("Trees/Ranger").ToSpec().NodeCount, Is.EqualTo(29));
    }

    [Test]
    public void SteadyAim_RankTwo_IsNinePercent()
    {
        SkillSpec rank = Marksman().Skills[0].Ranks[1].ToSpec();
        Assert.That(rank.Effects.Count, Is.EqualTo(2));
        AssertModifier(rank.Effects[0], PlayerStat.SteadyAimPerStack, 0.09f);
        AssertModifier(rank.Effects[1], PlayerStat.SteadyAimStopCrit, 2f);
    }

    [Test]
    public void BindingShot_RankTwo_IsOneSecond()
    {
        SkillSpec rank = Marksman().Skills[2].Ranks[1].ToSpec();
        Assert.That(rank.Effects.Count, Is.EqualTo(1));
        AssertModifier(rank.Effects[0], PlayerStat.BindStun, 1f);
    }

    [Test]
    public void Marksman_RanksChainByParent()
    {
        foreach (PathDefinition.SkillField skill in Marksman().Skills)
        {
            for (int rank = 0; rank < skill.Ranks.Count; rank++)
            {
                SkillSpec spec = skill.Ranks[rank].ToSpec();

                SkillKind first = (skill.Ranks[0].Id == "skill.marksman.rain-of-arrows" || skill.Ranks[0].Id == "skill.marksman.binding-shot"
                    || skill.Ranks[0].Id == "skill.marksman.hunters-snare" || skill.Ranks[0].Id == "skill.marksman.killshot") ? SkillKind.Active : SkillKind.Passive;
                Assert.That(spec.Kind, Is.EqualTo(rank == 0 ? first : SkillKind.Upgrade), spec.Id.Value);
                Assert.That(spec.ParentId, Is.EqualTo(rank == 0 ? default : new ContentId(skill.Ranks[rank - 1].Id)), spec.Id.Value);
            }
        }
    }

    [Test]
    public void Marksman_EffectsMatchTheTable()
    {
        var stats = new[]
        {
            PlayerStat.SteadyAimPerStack, PlayerStat.SteadyAimPerStack,
            PlayerStat.SteadyAimPerStack, PlayerStat.SteadyAimGuard,
        };
        var values = new[] { 0.05f, 0.09f, 0.02f, 0.2f };
        PathDefinition path = Marksman();

        for (int rank = 0; rank < 4; rank++)
        {
            SkillSpec spec = path.Skills[0].Ranks[rank].ToSpec();

            Assert.That(spec.Effects.Count, Is.EqualTo(rank == 1 ? 2 : 1));
            AssertModifier(spec.Effects[0], stats[rank], values[rank]);
        }

        foreach (SkillDefinition rank in path.Skills[3].Ranks)
        {
            SkillSpec spec = rank.ToSpec();

            Assert.That(spec.Effects.Count, Is.EqualTo(2));
            AssertModifier(spec.Effects[0], PlayerStat.WeaponRange, 1f);
            AssertModifier(spec.Effects[1], PlayerStat.EagleEyeFarBonus, 0.1f);
        }
    }

    [Test]
    public void Ranger_KeepsMarksmanInTheFirstSlot()
    {
        CharacterDefinition ranger = Ranger();
        CharacterSpec spec = ranger.ToSpec();

        Assert.That(ranger.PathSlots.Count, Is.EqualTo(3));
        Assert.That(ranger.PathSlots[0] == Marksman(), Is.True);
        Assert.That(spec.PathOpensAtLevel, Is.EqualTo(11));
        Assert.That(spec.PathIds[0], Is.EqualTo(new ContentId("path.ranger.marksman")));
    }

    [Test]
    public void Catalog_HasTheMarksman()
    {
        using IObjectResolver container = Boot();
        var catalog = container.Resolve<ContentCatalog>();

        Assert.That(catalog.TryGetPath(new ContentId("path.ranger.marksman"), out PathSpec path), Is.True);
        Assert.That(path.CharacterId, Is.EqualTo(new ContentId(Ranger().Id)));

        foreach (PathDefinition.SkillField skill in Marksman().Skills)
        {
            foreach (SkillDefinition rank in skill.Ranks)
            {
                Assert.That(catalog.TryGetSkill(new ContentId(rank.Id), out _), Is.True, rank.Id);
            }
        }
    }

    [Test]
    public void Loc_EveryKeyInBothTables()
    {
        PathSpec path = Marksman().ToSpec(new ContentId(Ranger().Id));
        var keys = new List<LocKey> { path.NameKey, path.DescriptionKey };

        foreach (PathDefinition.SkillField skill in Marksman().Skills)
        {
            foreach (SkillDefinition rank in skill.Ranks)
            {
                keys.Add(rank.ToSpec().NameKey);
                keys.Add(rank.ToSpec().DescriptionKey);
            }
        }

        IReadOnlyDictionary<LocKey, string> english = Load<LocalizationTable>("Localisation/English").ToDictionary();
        IReadOnlyDictionary<LocKey, string> pseudo = Load<LocalizationTable>("Localisation/Pseudo").ToDictionary();

        foreach (LocKey key in keys)
        {
            Assert.That(english.ContainsKey(key), Is.True, key.Key);
            Assert.That(pseudo.ContainsKey(key), Is.True, key.Key);
            Assert.That(english[key], Is.Not.Empty, key.Key);
            Assert.That(pseudo[key], Does.StartWith("[").And.EndWith("]"), key.Key);
            Assert.That(pseudo[key], Is.Not.EqualTo(english[key]), key.Key);
        }

        Assert.That(english[path.NameKey], Is.EqualTo("Marksman"));
        Assert.That(english[path.DescriptionKey], Is.EqualTo("Stand your ground. Every arrow you loose standing still hits harder."));
    }

    [Test]
    public void Icons_EverySkillHasOne()
    {
        using IObjectResolver container = Boot();
        var book = container.Resolve<SkillIconBook>();
        var expected = new Color(0.55f, 1f, 1f, 1f);

        foreach (PathDefinition.SkillField skill in Marksman().Skills)
        {
            Sprite icon = skill.Ranks[0].Icon;

            Assert.That(icon, Is.Not.Null);

            foreach (SkillDefinition rank in skill.Ranks)
            {
                Assert.That(rank.Icon == icon, Is.True, rank.Id);
                Assert.That(book.For(new ContentId(rank.Id)) == icon, Is.True, rank.Id);
                Assert.That(book.TintFor(new ContentId(rank.Id)), Is.EqualTo(expected), rank.Id);
            }
        }

        Assert.That(book.TintFor(default), Is.EqualTo(Color.white));
    }

    [Test]
    public void Assets_MatchTheRankTable()
    {
        IReadOnlyList<SkillDefinition> ranks = Marksman().Skills[1].Ranks;
        SkillSpec first = ranks[0].ToSpec();
        Assert.That(first.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(first.Active.Cooldown, Is.EqualTo(10f));
        Assert.That(first.Active.Trigger.Clauses.Count, Is.EqualTo(1));
        TriggerClause trigger = first.Active.Trigger.Clauses[0];
        Assert.That(trigger.Field, Is.EqualTo(TriggerField.EnemiesInAcquireRange));
        Assert.That(trigger.Comparison, Is.EqualTo(TriggerComparison.AtLeast));
        Assert.That(trigger.Threshold, Is.EqualTo(3f));
        Assert.That(first.Active.OnCast.Count, Is.EqualTo(1));
        var rain = first.Active.OnCast[0] as RainOfArrows;
        Assert.That(rain, Is.Not.Null);
        Assert.That(rain.Radius, Is.EqualTo(1.5f));
        Assert.That(rain.Duration, Is.EqualTo(2f));
        Assert.That(rain.PulseInterval, Is.EqualTo(0.25f));
        Assert.That(rain.DamageOfWeapon, Is.EqualTo(0.6f));
        Assert.That(ranks[1].ToSpec().Effects.Count, Is.EqualTo(1));
        AssertModifier(ranks[1].ToSpec().Effects[0], PlayerStat.RainDamage, 0.25f);
        Assert.That(ranks[2].ToSpec().Effects.Count, Is.EqualTo(2));
        AssertModifier(ranks[2].ToSpec().Effects[0], PlayerStat.RainRadius, 0.5f);
        AssertModifier(ranks[2].ToSpec().Effects[1], PlayerStat.RainSlow, 0.3f);
        Assert.That(ranks[3].ToSpec().Effects.Count, Is.EqualTo(1));
        var cooldown = ranks[3].ToSpec().Effects[0] as ModifySkillCooldown;
        Assert.That(cooldown, Is.Not.Null);
        Assert.That(cooldown.SkillId, Is.EqualTo(first.Id));
        Assert.That(cooldown.Kind, Is.EqualTo(ModifierKind.Flat));
        Assert.That(cooldown.Value, Is.EqualTo(-2f));
    }

    [Test]
    public void Path_RainIsSecondAtEleven()
    {
        PathSkillSpec rain = Marksman().ToSpec(new ContentId(Ranger().Id)).Skills[1];
        Assert.That(rain.UnlockLevel, Is.EqualTo(11));
        Assert.That(rain.Ranks.TierCount, Is.EqualTo(4));
        Assert.That(rain.Ranks.Tier(1)[0], Is.EqualTo(new ContentId("skill.marksman.rain-of-arrows")));
    }

    [Test]
    public void Loc_Keys()
    {
        var english = Load<LocalizationTable>("Localisation/English").ToDictionary();
        var pseudo = Load<LocalizationTable>("Localisation/Pseudo").ToDictionary();
        Assert.That(english[new LocKey("skill.marksman.rain-of-arrows.name")], Is.EqualTo("Rain of Arrows"));
        Assert.That(english[new LocKey("skill.marksman.rain-of-arrows.description")],
            Is.EqualTo("Arrows rain on the largest group for 2 s."));
        foreach (SkillDefinition rank in Marksman().Skills[1].Ranks)
        {
            SkillSpec spec = rank.ToSpec();
            Assert.That(english[spec.DescriptionKey], Is.Not.Empty);
            Assert.That(pseudo[spec.DescriptionKey], Does.StartWith("[").And.EndWith("]"));
        }
    }

    [Test]
    public void Assets_MatchRankTable()
    {
        IReadOnlyList<SkillDefinition> ranks = Marksman().Skills[2].Ranks;
        Assert.That(ranks.Count, Is.EqualTo(3));
        SkillSpec first = ranks[0].ToSpec();
        Assert.That(first.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(first.Active.Cooldown, Is.EqualTo(12f));
        Assert.That(first.Active.Trigger.Clauses.Count, Is.EqualTo(1));
        TriggerClause trigger = first.Active.Trigger.Clauses[0];
        Assert.That(trigger.Field, Is.EqualTo(TriggerField.EnemiesInAcquireRange));
        Assert.That(trigger.Comparison, Is.EqualTo(TriggerComparison.AtLeast));
        Assert.That(trigger.Threshold, Is.EqualTo(1f));
        Assert.That(first.Active.OnCast.Count, Is.EqualTo(1));
        var bind = first.Active.OnCast[0] as BindingShot;
        Assert.That(bind, Is.Not.Null);
        Assert.That(bind.Reach, Is.EqualTo(4f));
        Assert.That(bind.ConeHalfAngleDeg, Is.EqualTo(20f));
        Assert.That(bind.BoundSeconds, Is.EqualTo(2f));
        Assert.That(bind.AloneSeconds, Is.EqualTo(0.75f));
        Assert.That(bind.DamageOfWeapon, Is.EqualTo(2f));
        Assert.That(ranks[1].ToSpec().Effects.Count, Is.EqualTo(1));
        AssertModifier(ranks[1].ToSpec().Effects[0], PlayerStat.BindStun, 1f);
        Assert.That(ranks[2].ToSpec().Effects.Count, Is.EqualTo(1));
        var cooldown = ranks[2].ToSpec().Effects[0] as ModifySkillCooldown;
        Assert.That(cooldown, Is.Not.Null);
        Assert.That(cooldown.SkillId, Is.EqualTo(first.Id));
        Assert.That(cooldown.Kind, Is.EqualTo(ModifierKind.Flat));
        Assert.That(cooldown.Value, Is.EqualTo(-3f));
    }

    [Test]
    public void Path_BindingIsThirdAtFourteen()
    {
        PathSkillSpec bind = Marksman().ToSpec(new ContentId(Ranger().Id)).Skills[2];
        Assert.That(bind.UnlockLevel, Is.EqualTo(14));
        Assert.That(bind.Ranks.TierCount, Is.EqualTo(3));
        Assert.That(bind.Ranks.Tier(1)[0], Is.EqualTo(new ContentId("skill.marksman.binding-shot")));
    }

    [Test]
    public void Loc_BindingKeys()
    {
        var english = Load<LocalizationTable>("Localisation/English").ToDictionary();
        var pseudo = Load<LocalizationTable>("Localisation/Pseudo").ToDictionary();
        Assert.That(english[new LocKey("skill.marksman.binding-shot.name")], Is.EqualTo("Binding Shot"));
        Assert.That(english[new LocKey("skill.marksman.binding-shot.description")], Is.EqualTo(
            "Chains an enemy to whatever is behind it: another enemy, a stone, a tree or a wall. Both are stunned."));
        foreach (SkillDefinition rank in Marksman().Skills[2].Ranks)
        {
            SkillSpec spec = rank.ToSpec();
            Assert.That(english[spec.DescriptionKey], Is.Not.Empty);
            Assert.That(pseudo[spec.DescriptionKey], Does.StartWith("[").And.EndWith("]"));
        }
    }

    [Test]
    public void Snare_Assets_MatchRankTable()
    {
        IReadOnlyList<SkillDefinition> ranks = Marksman().Skills[4].Ranks;
        Assert.That(ranks.Count, Is.EqualTo(3));
        SkillSpec first = ranks[0].ToSpec();
        Assert.That(first.Id, Is.EqualTo(new ContentId("skill.marksman.hunters-snare")));
        Assert.That(ranks[1].Id, Is.EqualTo("skill.marksman.hunters-snare-2"));
        Assert.That(ranks[2].Id, Is.EqualTo("skill.marksman.hunters-snare-3"));
        Assert.That(first.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(first.Active.Cooldown, Is.EqualTo(10f));
        Assert.That(first.Active.Trigger.Clauses.Count, Is.EqualTo(1));
        TriggerClause trigger = first.Active.Trigger.Clauses[0];
        Assert.That(trigger.Field, Is.EqualTo(TriggerField.NearestEnemyDistance));
        Assert.That(trigger.Comparison, Is.EqualTo(TriggerComparison.Below));
        Assert.That(trigger.Threshold, Is.EqualTo(3f));
        Assert.That(first.Active.OnCast.Count, Is.EqualTo(1));
        var snare = first.Active.OnCast[0] as PlaceSnare;
        Assert.That(snare, Is.Not.Null);
        Assert.That(snare.Radius, Is.EqualTo(0.8f));
        Assert.That(snare.ArmDelay, Is.EqualTo(0.3f));
        Assert.That(snare.Lifetime, Is.EqualTo(10f));
        Assert.That(snare.RootSeconds, Is.EqualTo(2f));
        Assert.That(snare.DamageOfWeapon, Is.EqualTo(3f));
        Assert.That(ranks[1].ToSpec().Effects.Count, Is.EqualTo(2));
        AssertModifier(ranks[1].ToSpec().Effects[0], PlayerStat.SnareRoot, 0.5f);
        AssertModifier(ranks[1].ToSpec().Effects[1], PlayerStat.SnareDamage, 0.33f);
        Assert.That(ranks[2].ToSpec().Effects.Count, Is.EqualTo(2));
        AssertModifier(ranks[2].ToSpec().Effects[0], PlayerStat.SnareRoot, 0.5f);
        var cooldown = ranks[2].ToSpec().Effects[1] as ModifySkillCooldown;
        Assert.That(cooldown, Is.Not.Null);
        Assert.That(cooldown.SkillId, Is.EqualTo(first.Id));
        Assert.That(cooldown.Kind, Is.EqualTo(ModifierKind.Flat));
        Assert.That(cooldown.Value, Is.EqualTo(-2f));
    }

    [Test]
    public void Path_SnareIsFifthAtSeventeen()
    {
        PathSkillSpec snare = Marksman().ToSpec(new ContentId(Ranger().Id)).Skills[4];
        Assert.That(snare.UnlockLevel, Is.EqualTo(17));
        Assert.That(snare.Ranks.TierCount, Is.EqualTo(3));
        Assert.That(snare.Ranks.Tier(1)[0], Is.EqualTo(new ContentId("skill.marksman.hunters-snare")));
    }

    [Test]
    public void Loc_SnareKeys()
    {
        var english = Load<LocalizationTable>("Localisation/English").ToDictionary();
        var pseudo = Load<LocalizationTable>("Localisation/Pseudo").ToDictionary();
        Assert.That(english[new LocKey("skill.marksman.hunters-snare.name")], Is.EqualTo("Hunter's Snare"));
        Assert.That(english[new LocKey("skill.marksman.hunters-snare.description")], Is.EqualTo(
            "When an enemy gets close, drop a trap. The first to step on it is rooted and hurt."));
        foreach (SkillDefinition rank in Marksman().Skills[4].Ranks)
        {
            SkillSpec spec = rank.ToSpec();
            Assert.That(english[spec.NameKey], Is.Not.Empty);
            Assert.That(english[spec.DescriptionKey], Is.Not.Empty);
            Assert.That(pseudo[spec.DescriptionKey], Does.StartWith("[").And.EndWith("]"));
        }
    }

    [Test]
    public void KillshotAsset_TriggerIsAnyEnemyInRange()
    {
        SkillSpec skill = Load<SkillDefinition>("Skills/Marksman/Killshot").ToSpec();
        Assert.That(skill.Active.Trigger.Clauses.Count, Is.EqualTo(1));
        TriggerClause trigger = skill.Active.Trigger.Clauses[0];
        Assert.That(trigger.Field, Is.EqualTo(TriggerField.EnemiesInAcquireRange));
        Assert.That(trigger.Comparison, Is.EqualTo(TriggerComparison.AtLeast));
        Assert.That(trigger.Threshold, Is.EqualTo(1f));
        var shot = (Killshot)Load<KillshotDefinition>("Skills/Marksman/KillshotCast").ToEffect();
        Assert.That(shot.MinHits, Is.EqualTo(3));
    }

    [Test]
    public void Killshot_Assets_MatchRankTable()
    {
        IReadOnlyList<SkillDefinition> ranks = Marksman().Skills[5].Ranks;
        Assert.That(ranks.Count, Is.EqualTo(3));
        SkillSpec first = ranks[0].ToSpec();
        Assert.That(first.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(first.Active.Cooldown, Is.EqualTo(30f));
        Assert.That(first.Active.OnCast.Count, Is.EqualTo(1));
        var shot = first.Active.OnCast[0] as Killshot;
        Assert.That(shot, Is.Not.Null);
        Assert.That(shot.WindUp, Is.EqualTo(0.7f));
        Assert.That(shot.HalfWidth, Is.EqualTo(1.5f));
        Assert.That(shot.Length, Is.EqualTo(30f));
        Assert.That(shot.DamageOfWeapon, Is.EqualTo(8f));
        Assert.That(shot.FalloffPerHit, Is.EqualTo(0.15f));
        Assert.That(shot.MinFactor, Is.EqualTo(0.4f));
        Assert.That(shot.ExecuteBelow, Is.EqualTo(0.15f));
        Assert.That(ranks[1].ToSpec().Effects.Count, Is.EqualTo(2));
        AssertModifier(ranks[1].ToSpec().Effects[0], PlayerStat.KillshotExecute, 0.05f);
        AssertModifier(ranks[1].ToSpec().Effects[1], PlayerStat.KillshotDamage, 0.25f);
        Assert.That(ranks[2].ToSpec().Effects.Count, Is.EqualTo(2));
        AssertModifier(ranks[2].ToSpec().Effects[0], PlayerStat.KillshotExecute, 0.05f);
        var cooldown = ranks[2].ToSpec().Effects[1] as ModifySkillCooldown;
        Assert.That(cooldown, Is.Not.Null);
        Assert.That(cooldown.SkillId, Is.EqualTo(first.Id));
        Assert.That(cooldown.Kind, Is.EqualTo(ModifierKind.Flat));
        Assert.That(cooldown.Value, Is.EqualTo(-8f));
    }

    [Test]
    public void Path_KillshotIsSixthAtTwenty()
    {
        PathSkillSpec shot = Marksman().ToSpec(new ContentId(Ranger().Id)).Skills[5];
        Assert.That(shot.UnlockLevel, Is.EqualTo(20));
        Assert.That(shot.Ranks.TierCount, Is.EqualTo(3));
        for (int rank = 1; rank <= 3; rank++)
        {
            string suffix = rank == 1 ? string.Empty : $"-{rank}";
            Assert.That(shot.Ranks.Tier(rank)[0], Is.EqualTo(new ContentId($"skill.marksman.killshot{suffix}")));
        }
    }

    [Test]
    public void Loc_KillshotKeys()
    {
        var english = Load<LocalizationTable>("Localisation/English").ToDictionary();
        var pseudo = Load<LocalizationTable>("Localisation/Pseudo").ToDictionary();
        Assert.That(english[new LocKey("skill.marksman.killshot.name")], Is.EqualTo("Killshot"));
        Assert.That(english[new LocKey("skill.marksman.killshot.description")], Is.EqualTo(
            "Kneel and loose one arrow across the whole arena. It weakens through each enemy and kills the badly wounded outright."));
        foreach (SkillDefinition rank in Marksman().Skills[5].Ranks)
        {
            SkillSpec spec = rank.ToSpec();
            Assert.That(english[spec.NameKey], Is.Not.Empty);
            Assert.That(english[spec.DescriptionKey], Is.Not.Empty);
            Assert.That(pseudo[spec.NameKey], Does.StartWith("[").And.EndWith("]"));
            Assert.That(pseudo[spec.DescriptionKey], Does.StartWith("[").And.EndWith("]"));
        }
    }

    private static IObjectResolver Boot()
    {
        var builder = new ContainerBuilder();

        BootInstaller.Install(
            builder, new[] { Ranger() }, Array.Empty<EnemyDefinition>(), Array.Empty<ModeDefinition>(),
            new[] { Marksman().Skills[0].Ranks[0] }, Array.Empty<SkillTreeDefinition>(),
            Load<LocalizationTable>("Localisation/English"));

        return builder.Build();
    }

    private static void AssertModifier(IEffect effect, PlayerStat stat, float value)
    {
        Assert.That(effect, Is.TypeOf<ModifyStat>());
        var modifier = (ModifyStat)effect;

        Assert.That(modifier.Stat, Is.EqualTo(stat));
        Assert.That(modifier.Value, Is.EqualTo(value).Within(0.000001f));
        Assert.That(modifier.Kind, Is.EqualTo(ModifierKind.Flat));
        Assert.That(modifier.Target, Is.EqualTo(StatTarget.Player));
    }

    private static CharacterDefinition Ranger() => Load<CharacterDefinition>("Characters/Ranger");
    private static PathDefinition Marksman() => Load<PathDefinition>("Paths/Marksman");

    private static T Load<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>($"{Data}{path}.asset");

        Assert.That(asset, Is.Not.Null, path);

        return asset;
    }
}
