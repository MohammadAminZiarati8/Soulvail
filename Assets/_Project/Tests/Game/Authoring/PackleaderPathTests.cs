using System;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Authoring;

public sealed class PackleaderPathTests
{
    private const string Root = "Assets/_Project/";
    private static readonly ContentId Packleader = new("path.packleader");
    private static readonly ContentId PackHounds = new("skill.packleader.pack-hounds");

    [Test]
    public void Beastmaster_FirstPathIsPackleader_OpensAt11()
    {
        CharacterDefinition beastmaster = Load<CharacterDefinition>("Data/Characters/Beastmaster.asset");
        Assert.That(beastmaster.PathSlots[0], Is.SameAs(PathAsset()));
        Assert.That(beastmaster.ToSpec().PathOpensAtLevel, Is.EqualTo(11));

        using IObjectResolver boot = Boot();
        var catalog = boot.Resolve<ContentCatalog>();
        using var hub = new DomainEventHub();
        RunSession session = StartRun(catalog, hub, beastmaster, level: 10);
        session.ChoosePath(Packleader);
        Assert.That(session.State.PathId, Is.EqualTo(default(ContentId)));

        session = StartRun(catalog, hub, beastmaster, level: 11);
        using var pause = new RunPause();
        GameObject screen = OpenTree(boot, session, pause);
        try
        {
            var cards = screen.GetComponentInChildren<PathCardsView>(true);
            Assert.That(cards.gameObject.activeInHierarchy, Is.True);
            Assert.That(cards.CardText(0), Is.EqualTo("Packleader"));
            Assert.That(cards.Card(0).interactable, Is.True);
            for (int i = 1; i < 3; i++)
            {
                Assert.That(cards.CardText(i), Is.EqualTo("Coming soon"));
                Assert.That(cards.Card(i).interactable, Is.False);
            }

            cards.Card(0).onClick.Invoke();
            Assert.That(cards.ChooseEnabled, Is.True);
            cards.Confirm();
            Assert.That(session.State.PathId, Is.EqualTo(Packleader));
            Assert.That(session.State.IsPathChoiceOpen, Is.False);
            Assert.That(cards.gameObject.activeSelf, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(screen);
        }
    }

    [Test]
    public void PackHounds_FourRanks_Numbers()
    {
        using IObjectResolver boot = Boot();
        var catalog = boot.Resolve<ContentCatalog>();
        var stats = new[] { PlayerStat.PackHounds, PlayerStat.PackShare, PlayerStat.PackHounds, PlayerStat.PackShare };
        var values = new[] { 1f, 0.15f, 1f, 0.15f };
        Sprite icon = Load<Sprite>("Art/UI/Icons/Icon_Placeholder.png");
        ContentId parent = default;

        for (int i = 0; i < stats.Length; i++)
        {
            SkillDefinition definition = PathAsset().Skills[0].Ranks[i];
            SkillSpec skill = catalog.Skill(new ContentId(definition.Id));
            Assert.That(skill.Kind, Is.EqualTo(i == 0 ? SkillKind.Passive : SkillKind.Upgrade));
            Assert.That(skill.ParentId, Is.EqualTo(parent));
            Assert.That(definition.Icon, Is.SameAs(icon));
            var effect = (ModifyStat)skill.Effects.Single();
            Assert.That((effect.Stat, effect.Value, effect.Kind, effect.Target),
                Is.EqualTo((stats[i], values[i], ModifierKind.Flat, StatTarget.Pet)));
            parent = skill.Id;
        }
    }

    [Test]
    public void PackHoundBody_SmallerScale_OwnBar()
    {
        using var hub = new DomainEventHub();
        using IObjectResolver resolver = new ContainerBuilder().Build();
        var root = new GameObject("Pet bodies");
        try
        {
            var prefab = Load<GameObject>("Prefabs/Pets/Hound.prefab").GetComponent<PetView>();
            using var views = new PetViews(resolver, prefab, root.transform, hub);
            hub.Publish(new PetSpawned(1, default));
            hub.Publish(new PetSpawned(2, default, healthFraction: 0.5f, isLead: false));
            PetView[] bodies = root.GetComponentsInChildren<PetView>();
            PetView lead = bodies.Single(body => body.Id == 1);
            PetView pack = bodies.Single(body => body.Id == 2);
            var leadBar = lead.GetComponentInChildren<PetHealthBar>();
            var packBar = pack.GetComponentInChildren<PetHealthBar>();
            var leadFill = leadBar.GetComponentsInChildren<Image>().Single(image => image.name == "Fill");
            var packFill = packBar.GetComponentsInChildren<Image>().Single(image => image.name == "Fill");
            Assert.That(pack.transform.localScale, Is.EqualTo(lead.transform.localScale * 0.8f));
            Assert.That(packFill.transform.lossyScale.x, Is.EqualTo(leadFill.transform.lossyScale.x * 0.8f).Within(0.000001f));
            Assert.That(packFill.color, Is.EqualTo(Palette.Player));
            Assert.That(packFill.rectTransform.anchorMax.x, Is.EqualTo(0.5f));
            Assert.That(leadFill.rectTransform.anchorMax.x, Is.EqualTo(1f));

            hub.Publish(new PetDespawned(2));
            Assert.That(pack.gameObject.activeSelf, Is.False);
            Assert.That(packBar.gameObject.activeInHierarchy, Is.False);
            Assert.That(lead.gameObject.activeSelf, Is.True);
            hub.Publish(new PetSpawned(3, default));
            Assert.That(pack.Id, Is.EqualTo(3));
            Assert.That(pack.transform.localScale, Is.EqualTo(prefab.transform.localScale));
            Assert.That(packBar.gameObject.activeInHierarchy, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void Path_FirstRow_PackHoundsAt11()
    {
        using IObjectResolver boot = Boot();
        var catalog = boot.Resolve<ContentCatalog>();
        PathSkillSpec row = catalog.Path(Packleader).Skills[0];
        Assert.That(row.UnlockLevel, Is.EqualTo(11));
        Assert.That(row.Ranks.TierCount, Is.EqualTo(4));
        Assert.That(row.Ranks.Tier(1), Is.EqualTo(new[] { PackHounds }));

        using var hub = new DomainEventHub();
        using var pause = new RunPause();
        RunSession session = StartRun(catalog, hub, Load<CharacterDefinition>("Data/Characters/Beastmaster.asset"), level: 11);
        session.ChoosePath(Packleader);
        GameObject screen = OpenTree(boot, session, pause);
        try
        {
            var block = screen.GetComponentInChildren<TreeBlockView>(true);
            Assert.That(block.RowCount, Is.EqualTo(5));
            Assert.That(block.GetComponentsInChildren<TMP_Text>().Select(label => label.text), Does.Contain("Lv 11").And.Contain("Lv 14").And.Contain("Lv 17").And.Contain("Lv 20"));
        }
        finally
        {
            Object.DestroyImmediate(screen);
        }
    }

    [Test]
    public void Skills_HaveLocKeys()
    {
        PathSpec path = PathAsset().ToSpec(new ContentId("character.beastmaster"));
        var keys = PathAsset().Skills.SelectMany(row => row.Ranks).Select(rank => rank.ToSpec())
            .SelectMany(skill => new[] { skill.NameKey, skill.DescriptionKey })
            .Concat(new[] { path.NameKey, path.DescriptionKey });

        var english = Load<LocalizationTable>("Data/Localisation/English.asset").ToDictionary();
        var pseudo = Load<LocalizationTable>("Data/Localisation/Pseudo.asset").ToDictionary();
        foreach (LocKey key in keys)
        {
            Assert.That(english[key], Is.Not.Empty, key.Key);
            Assert.That(pseudo[key], Does.StartWith("[").And.EndWith("]"), key.Key);
        }
    }

    [Test]
    public void PackTactics_Ranks()
    {
        AssertRanks(2, PlayerStat.PackTactics, new[] { 0.10f, 0.05f, 0.05f }, PlayerStat.PackTacticsSlow, 0.3f);
    }

    [Test]
    public void ThickPelts_Ranks_RescueOneSecond()
    {
        AssertRanks(3, PlayerStat.PetDamageTaken, new[] { -0.10f, -0.10f, -0.10f }, PlayerStat.PetRescueSeconds, -1f);
    }

    [Test]
    public void Packleader_TwentyRanks()
    {
        using IObjectResolver boot = Boot();
        var rows = boot.Resolve<ContentCatalog>().Path(Packleader).Skills;
        Assert.That(rows.Select(row => row.Ranks.TierCount), Is.EqualTo(new[] { 4, 4, 3, 3, 3, 3 }));
        Assert.That(rows.Select(row => row.UnlockLevel), Is.EqualTo(new[] { 11, 11, 14, 14, 17, 20 }));
        Assert.That(rows[2].Ranks.Tier(1).Single(), Is.EqualTo(new ContentId("skill.packleader.pack-tactics")));
        Assert.That(rows[3].Ranks.Tier(1).Single(), Is.EqualTo(new ContentId("skill.packleader.thick-pelts")));
    }

    private static void AssertRanks(int row, PlayerStat stat, float[] values, PlayerStat finalStat, float finalValue)
    {
        using IObjectResolver boot = Boot();
        var catalog = boot.Resolve<ContentCatalog>();
        var icons = boot.Resolve<SkillIconBook>();
        Sprite placeholder = Load<Sprite>("Art/UI/Icons/Icon_Placeholder.png");
        ContentId parent = default;
        var ranks = PathAsset().Skills[row].Ranks;
        for (int i = 0; i < values.Length; i++)
        {
            SkillSpec skill = catalog.Skill(new ContentId(ranks[i].Id));
            Assert.That(skill.Kind, Is.EqualTo(i == 0 ? SkillKind.Passive : SkillKind.Upgrade));
            Assert.That(skill.ParentId, Is.EqualTo(parent));
            Assert.That(icons.For(skill.Id), Is.SameAs(placeholder));

            var expected = i == values.Length - 1
                ? new[] { (stat, values[i]), (finalStat, finalValue) }
                : new[] { (stat, values[i]) };
            var effects = skill.Effects.Cast<ModifyStat>().ToArray();
            Assert.That(effects.Select(effect => (effect.Stat, effect.Value)), Is.EqualTo(expected));
            Assert.That(effects.All(effect => effect.Kind == ModifierKind.Flat && effect.Target == StatTarget.Pet), Is.True);
            parent = skill.Id;
        }
    }

    private static PathDefinition PathAsset() => Load<PathDefinition>("Data/Paths/Packleader.asset");

    internal static IObjectResolver Boot()
    {
        var boot = new SerializedObject(Load<GameObject>("Prefabs/Composition/BootScope.prefab").GetComponent<BootScope>());
        var builder = new ContainerBuilder();
        BootInstaller.Install(builder, References<CharacterDefinition>(boot, "_characters"),
            References<EnemyDefinition>(boot, "_enemies"), References<ModeDefinition>(boot, "_modes"),
            References<SkillDefinition>(boot, "_skills"), References<SkillTreeDefinition>(boot, "_trees"),
            Load<LocalizationTable>("Data/Localisation/English.asset"), References<BossDefinition>(boot, "_bosses"));
        return builder.Build();
    }

    private static T[] References<T>(SerializedObject source, string field) where T : Object
    {
        SerializedProperty array = source.FindProperty(field);
        return Enumerable.Range(0, array.arraySize)
            .Select(i => (T)array.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
    }

    internal static RunSession StartRun(ContentCatalog catalog, DomainEventHub hub, CharacterDefinition character, int level)
    {
        var mode = new ContentId("mode.jungle");
        var id = new ContentId(character.Id);
        var random = new SeededRandom(7);
        var instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);
        var session = new RunSession(catalog, random, hub, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(instant), hub), 64, 28, 8);
        var snapshot = new RunSnapshot(RunSnapshot.CurrentVersion, mode, id, random.Seed, 1,
            new RandomState(101, 102, 103, 104, 105), 90f, 0f, 120f, instant, level, 0f, level - 1,
            Array.Empty<ContentId>(), new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        session.Start(new RunConfig(mode, id, random.Seed, 1, SpawnPlan.Empty, snapshot));
        return session;
    }

    private static GameObject OpenTree(IObjectResolver boot, RunSession session, RunPause pause)
    {
        GameObject screen = Object.Instantiate(Load<GameObject>("Prefabs/UI/TreeView.prefab"));
        var presenter = screen.GetComponent<TreeViewPresenter>();
        presenter.Construct(session, session, pause, boot.Resolve<ContentCatalog>(),
            boot.Resolve<ILocalizer>(), boot.Resolve<SkillIconBook>(), boot.Resolve<CharacterLookBook>());
        presenter.UsePixelsPerDp(1f);
        presenter.Open(null);
        return screen;
    }

    internal static T Load<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(Root + path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }
}
