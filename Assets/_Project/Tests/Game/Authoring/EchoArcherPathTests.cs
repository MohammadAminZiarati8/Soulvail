using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using UnityEditor;
using VContainer;

namespace Soulvail.Tests.Game.Authoring;

[TestFixture]
public sealed class EchoArcherPathTests
{
    private static readonly ContentId Ranger = new("character.ranger");
    private static readonly ContentId EchoArcher = new("path.ranger.echo-archer");
    private static readonly ContentId EchoArrow = new("skill.echo-archer.echo-arrow");

    private static readonly ContentId MirrorImageId = new("skill.echo-archer.mirror-image");

    [Test]
    public void Assets_MatchTheRankTable()
    {
        var ranks = Path().Skills[0].Ranks;
        SkirmisherPathTests.AssertRanks(ranks, new[]
        {
            new[] { (PlayerStat.EchoEvery, 4f), (PlayerStat.EchoDamage, 0.6f) },
            new[] { (PlayerStat.EchoEvery, -1f) },
            new[] { (PlayerStat.EchoDamage, 0.2f) },
            new[] { (PlayerStat.EchoEvery, -1f) },
        });

        using IObjectResolver boot = SkirmisherPathTests.Boot();
        var icons = boot.Resolve<SkillIconBook>();
        foreach (SkillDefinition rank in ranks)
        {
            Assert.That(rank.Icon, Is.Not.Null, rank.Id);
            Assert.That(icons.For(new ContentId(rank.Id)), Is.SameAs(rank.Icon), rank.Id);
        }
    }

    [Test]
    public void MirrorImage_Assets_MatchTheRankTable()
    {
        var ranks = Path().Skills[1].Ranks;
        Assert.That(ranks.Count, Is.EqualTo(4));
        SkillSpec first = ranks[0].ToSpec();
        Assert.That(first.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(first.Active.Cooldown, Is.EqualTo(14f));
        Assert.That(first.Active.Trigger.Clauses.Count, Is.EqualTo(1));
        TriggerClause trigger = first.Active.Trigger.Clauses[0];
        Assert.That(trigger.Field, Is.EqualTo(TriggerField.EnemiesInAcquireRange));
        Assert.That(trigger.Comparison, Is.EqualTo(TriggerComparison.AtLeast));
        Assert.That(trigger.Threshold, Is.EqualTo(1f));
        Assert.That(first.Active.OnCast.Count, Is.EqualTo(1));
        Assert.That(((MirrorImage)first.Active.OnCast[0]).DamageOfWeapon, Is.EqualTo(0.5f));

        var stats = new[] { PlayerStat.MirrorImageShades, PlayerStat.MirrorImageSeconds, PlayerStat.MirrorImageShades };
        using IObjectResolver boot = SkirmisherPathTests.Boot();
        var icons = boot.Resolve<SkillIconBook>();
        for (int i = 0; i < ranks.Count; i++)
        {
            SkillSpec rank = ranks[i].ToSpec();
            Assert.That(ranks[i].Icon, Is.Not.Null, rank.Id.Value);
            Assert.That(icons.For(rank.Id), Is.SameAs(ranks[i].Icon), rank.Id.Value);
            if (i == 0)
            {
                continue;
            }

            Assert.That(rank.Kind, Is.EqualTo(SkillKind.Upgrade));
            Assert.That(rank.ParentId, Is.EqualTo(new ContentId(ranks[i - 1].Id)));
            Assert.That(rank.Effects.Count, Is.EqualTo(i == 3 ? 2 : 1));
            var stat = (ModifyStat)rank.Effects[0];
            Assert.That(stat.Stat, Is.EqualTo(stats[i - 1]));
            Assert.That(stat.Target, Is.EqualTo(StatTarget.Player));
            Assert.That(stat.Kind, Is.EqualTo(ModifierKind.Flat));
            Assert.That(stat.Value, Is.EqualTo(i == 2 ? 2f : 1f));
        }

        var cooldown = (ModifySkillCooldown)ranks[3].ToSpec().Effects[1];
        Assert.That(cooldown.SkillId, Is.EqualTo(MirrorImageId));
        Assert.That(cooldown.Kind, Is.EqualTo(ModifierKind.Flat));
        Assert.That(cooldown.Value, Is.EqualTo(-3f));
    }

    [Test]
    public void Path_MirrorImageSecondAtEleven()
    {
        var skill = Path().ToSpec(Ranger).Skills[1];
        Assert.That(skill.UnlockLevel, Is.EqualTo(11));
        Assert.That(skill.Ranks.TierCount, Is.EqualTo(4));
        Assert.That(skill.Ranks.Tier(1), Is.EqualTo(new[] { MirrorImageId }));
        for (int rank = 2; rank <= skill.Ranks.TierCount; rank++)
        {
            Assert.That(skill.Ranks.Tier(rank), Is.EqualTo(new[] { new ContentId($"{MirrorImageId.Value}-{rank}") }));
        }
    }

    [Test]
    public void Path_EchoArrowFirstAtEleven()
    {
        PathSpec path = Path().ToSpec(Ranger);
        Assert.That(path.Id, Is.EqualTo(EchoArcher));
        Assert.That(path.CharacterId, Is.EqualTo(Ranger));
        Assert.That(path.Skills[0].UnlockLevel, Is.EqualTo(11));
        var ranks = path.Skills[0].Ranks;
        Assert.That(ranks.TierCount, Is.EqualTo(4));
        Assert.That(ranks.Tier(1), Is.EqualTo(new[] { EchoArrow }));
        for (int rank = 2; rank <= ranks.TierCount; rank++)
            Assert.That(ranks.Tier(rank), Is.EqualTo(new[] { new ContentId($"{EchoArrow.Value}-{rank}") }));
    }

    [Test]
    public void Ranger_OffersThreePaths()
    {
        CharacterDefinition ranger = Load<CharacterDefinition>("Characters/Ranger");
        var expected = new[] { new ContentId("path.ranger.marksman"), new ContentId("path.ranger.skirmisher"), EchoArcher };
        Assert.That(ranger.PathSlots.Count, Is.EqualTo(expected.Length));
        for (int i = 0; i < expected.Length; i++)
            Assert.That(ranger.PathSlots[i].Id, Is.EqualTo(expected[i].Value));
        Assert.That(ranger.ToSpec().PathIds, Is.EqualTo(expected));
        Assert.That(ranger.ToSpec().PathOpensAtLevel, Is.EqualTo(11));
    }

    [TestCase(10, false)]
    [TestCase(11, true)]
    public void Choose_EchoArcher_InstallsEchoArrow(int level, bool canChoose)
    {
        using var events = new DomainEventHub();
        RunSession session = SkirmisherPathTests.StartRanger(level, events);
        int baseNodes = session.State.TreeNodeCount;
        int points = session.State.SkillPoints;
        Assert.That(session.State.IsPathChoiceOpen, Is.EqualTo(canChoose));

        session.ChoosePath(EchoArcher);

        Assert.That(session.State.PathId, Is.EqualTo(canChoose ? EchoArcher : default));
        Assert.That(session.State.IsPathChoiceOpen, Is.False);
        Assert.That(session.State.TreeNodeCount, Is.EqualTo(baseNodes + (canChoose ? Path().ToSpec(Ranger).NodeCount : 0)));
        Assert.That(session.State.IsNodeAvailable(EchoArrow), Is.EqualTo(canChoose));
        Assert.That(session.State.SkillPoints, Is.EqualTo(points));
        if (canChoose)
        {
            session.TakeNode(EchoArrow);
            Assert.That(session.State.SkillPoints, Is.EqualTo(points - 1));
            Assert.That(session.State.IsNodeAvailable(new ContentId($"{EchoArrow.Value}-2")), Is.True);
        }
        session.End();
    }

    [Test]
    public void Path_WithoutLook_Loads()
    {
        PathDefinition path = Path();
        Assert.That(path.Look, Is.Null);
        Assert.That(path.ToSpec(Ranger).Id, Is.EqualTo(EchoArcher));
    }

    [Test]
    public void Loc_Keys()
    {
        var english = Load<LocalizationTable>("Localisation/English").ToDictionary();
        var pseudo = Load<LocalizationTable>("Localisation/Pseudo").ToDictionary();
        PathSpec path = Path().ToSpec(Ranger);
        Check(path.NameKey, "Echo Archer");
        Check(path.DescriptionKey, "Be in many places. Shades of you shoot, lure and burst.");

        string[] descriptions =
        {
            "Every 4th arrow is loosed again by a shade, for 60% damage.",
            "Every 3rd arrow.",
            "80% damage.",
            "Every 2nd arrow.",
        };
        var ranks = Path().Skills[0].Ranks;
        Assert.That(ranks.Count, Is.EqualTo(descriptions.Length));
        for (int i = 0; i < ranks.Count; i++)
        {
            SkillSpec spec = ranks[i].ToSpec();
            Check(spec.NameKey, i == 0 ? "Echo Arrow" : $"Echo Arrow {i + 1}");
            Check(spec.DescriptionKey, descriptions[i]);
        }

        string[] mirrorDescriptions =
        {
            "A shade beside you shoots for 6 s, at 50% damage.",
            "2 shades.",
            "8 s.",
            "3 shades. Cooldown 11 s.",
        };
        var mirrorRanks = Path().Skills[1].Ranks;
        Assert.That(mirrorRanks.Count, Is.EqualTo(mirrorDescriptions.Length));
        for (int i = 0; i < mirrorRanks.Count; i++)
        {
            SkillSpec spec = mirrorRanks[i].ToSpec();
            Check(spec.NameKey, i == 0 ? "Mirror Image" : $"Mirror Image {i + 1}");
            Check(spec.DescriptionKey, mirrorDescriptions[i]);
        }

        void Check(LocKey key, string value)
        {
            Assert.That(english[key], Is.EqualTo(value), key.Key);
            Assert.That(pseudo[key], Is.EqualTo(LocalisationSweepTests.Pseudolocalise(value)), key.Key);
        }
    }

    private static PathDefinition Path() => Load<PathDefinition>("Paths/EchoArcher");

    private static T Load<T>(string relative) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>($"Assets/_Project/Data/{relative}.asset");
        Assert.That(asset, Is.Not.Null, relative);
        return asset;
    }
}
