using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using static Soulvail.Tests.Game.Authoring.PackleaderPathTests;
using Num3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>BT-06b: Frenzy in the Packleader's Lv-20 slot, its ranks, and how a frenzied animal looks.</summary>
public sealed class FrenzyAuthoringTests
{
    private static readonly ContentId PathId = new("path.packleader");
    private static readonly ContentId FrenzyId = new("skill.packleader.frenzy");
    private static readonly ContentId HowlId = new("skill.packleader.howl");

    // ---- Rule 1: the slot -------------------------------------------------------------------------

    [Test]
    public void Packleader_LevelTwenty_IsFrenzy_ThreeRanks()
    {
        SkillSpec[] ranks = FrenzyRanks(out PathSkillSpec row, out _);

        Assert.That(row.UnlockLevel, Is.EqualTo(20));
        Assert.That(row.Ranks.TierCount, Is.EqualTo(3));
        Assert.That(ranks.Select(rank => rank.Id.Value),
            Is.EqualTo(new[] { "skill.packleader.frenzy", "skill.packleader.frenzy-2", "skill.packleader.frenzy-3" }));
        Assert.That(ranks.Select(rank => rank.Kind), Is.EqualTo(new[] { SkillKind.Active, SkillKind.Upgrade, SkillKind.Upgrade }));
        Assert.That(ranks.Select(rank => rank.ParentId), Is.EqualTo(new[] { default(ContentId), ranks[0].Id, ranks[1].Id }));
        Assert.That(ranks[0].Active.OnCast.Single(), Is.TypeOf<Frenzy>());
        Assert.That(Load<FrenzyDefinition>("Data/Effects/Packleader/FrenzyCast.asset").ToEffect(), Is.TypeOf<Frenzy>());

        var english = Load<LocalizationTable>("Data/Localisation/English.asset").ToDictionary();
        Assert.That(english[ranks[0].NameKey], Is.EqualTo("Frenzy"));
        Assert.That(ranks.Select(rank => english[rank.DescriptionKey]), Is.All.Not.Empty);
        Assert.That(ranks.Select(rank => english[rank.DescriptionKey]), Has.None.Contains("\n"));
    }

    [Test]
    public void Packleader_TotalsTwentyRanks()
    {
        using IObjectResolver boot = Boot();
        var rows = boot.Resolve<ContentCatalog>().Path(PathId).Skills;
        Assert.That(rows.Sum(row => row.Ranks.TierCount), Is.EqualTo(20));
        Assert.That(rows[5].Ranks.Tier(1).Single(), Is.EqualTo(FrenzyId));
    }

    // ---- Rule 2: ranks ----------------------------------------------------------------------------

    [Test]
    public void FrenzyRanks_ResolveSecondsBonusCooldown()
    {
        SkillSpec[] ranks = FrenzyRanks(out _, out ContentCatalog catalog);
        CharacterSpec beastmaster = catalog.Character(new ContentId("character.beastmaster"));
        var events = new RecordingEvents();
        var combat = new PlayerCombat(beastmaster, events, new RecordingIntents(), 8);
        var stats = new PlayerStats(combat, new PlayerMotor(beastmaster.Movement, Num3.UnitZ),
            new LevelTracker(new XpCurve(20f, 12f, 1.4f), events));
        var handler = new ModifyStatHandler(stats);

        float[] seconds = new float[3];
        float[] bonus = new float[3];
        for (int i = 0; i < ranks.Length; i++)
        {
            foreach (ModifyStat effect in ranks[i].Effects.OfType<ModifyStat>())
            {
                handler.Apply(effect, this);
            }

            seconds[i] = stats.FrenzySeconds.Value;
            bonus[i] = stats.FrenzyBonus.Value;
        }

        Assert.That(seconds, Is.EqualTo(new[] { 8f, 10f, 10f }).Within(0.0001f));
        Assert.That(bonus, Is.EqualTo(new[] { 0.3f, 0.3f, 0.4f }).Within(0.0001f));

        using var hub = new DomainEventHub();
        RunSession session = StartRun(catalog, hub, Load<CharacterDefinition>("Data/Characters/Beastmaster.asset"), level: 20);
        session.ChoosePath(PathId);
        session.TakeNode(ranks[0].Id);
        Assert.That(CooldownOf(session, FrenzyId), Is.EqualTo(30f));
        session.TakeNode(ranks[1].Id);
        Assert.That(CooldownOf(session, FrenzyId), Is.EqualTo(30f));
        session.TakeNode(ranks[2].Id);
        Assert.That(CooldownOf(session, FrenzyId), Is.EqualTo(24f));
    }

    // ---- Rule 3: trigger --------------------------------------------------------------------------

    [Test]
    public void Frenzy_Trigger_OneInRange_Cooldown30()
    {
        SkillSpec[] ranks = FrenzyRanks(out _, out _);
        ActiveSpec active = ranks[0].Active;

        Assert.That(active.Cooldown, Is.EqualTo(30f));
        TriggerClause clause = active.Trigger.Clauses.Single();
        Assert.That((clause.Field, clause.Comparison, clause.Threshold),
            Is.EqualTo((TriggerField.EnemiesInAcquireRange, TriggerComparison.AtLeast, 1f)));
    }

    // ---- Rule 4: look -----------------------------------------------------------------------------

    [Test]
    public void PetBar_Frenzied_ShowsAttackUpIcon_ClearsOnEnd()
    {
        var parent = new GameObject("Frenzy test");
        var hub = new DomainEventHub();
        try
        {
            using var resolver = new ContainerBuilder().Build();
            using var views = new PetViews(resolver, Load<GameObject>("Prefabs/Pets/Hound.prefab").GetComponent<PetView>(),
                parent.transform, hub);
            hub.Publish(new PetSpawned(1, Num3.Zero, isLead: true));
            hub.Publish(new PetSpawned(2, Num3.Zero, isLead: false));
            PetView[] pets = parent.GetComponentsInChildren<PetView>();
            Assert.That(pets, Has.Length.EqualTo(2));
            PetView lead = pets.Single(pet => pet.Id == 1);
            PetView second = pets.Single(pet => pet.Id == 2);
            Material original = Body(lead).sharedMaterial;

            Assert.That(Icon(lead).gameObject.activeSelf, Is.False);
            float now = Time.time;
            hub.Publish(new PetsFrenzied(8f, 0.3f));

            var expected = Load<Sprite>("Art/UI/Hud/T_Status_Frenzied.png");
            Assert.That(Load<StatusIconBook>("Data/StatusIconBook.asset").For(PetHealthBar.FrenziedStatus), Is.SameAs(expected));
            foreach (PetView pet in pets)
            {
                Assert.That(Icon(pet).gameObject.activeSelf, Is.True, $"Pet {pet.Id}");
                Assert.That(Icon(pet).sprite, Is.SameAs(expected));
                Assert.That(Body(pet).sharedMaterial, Is.Not.SameAs(original));
            }

            Color glow = Body(lead).sharedMaterial.GetColor("_EmissionColor");
            Assert.That(glow.r / glow.b, Is.EqualTo(Palette.Player.r / Palette.Player.b).Within(0.001f), "Cyan, not gold/lime.");
            Assert.That(glow.g / glow.b, Is.EqualTo(Palette.Player.g / Palette.Player.b).Within(0.001f));
            Color icon = AverageOpaque(expected);
            Assert.That(icon.b, Is.GreaterThan(icon.r), "The icon is cyan, not lime or gold.");
            Assert.That(icon.b, Is.GreaterThan(icon.g * 0.9f));

            lead.StepHowl(now + 7.9f);
            Assert.That(Icon(lead).gameObject.activeSelf, Is.True);
            lead.StepHowl(now + 8f);
            Assert.That(Icon(lead).gameObject.activeSelf, Is.False, "The buff ended.");
            Assert.That(Body(lead).sharedMaterial, Is.SameAs(original));

            hub.Publish(new PetFell(2, Num3.Zero));
            Assert.That(Icon(second).gameObject.activeSelf, Is.False, "It fell.");
            Assert.That(Body(second).sharedMaterial, Is.SameAs(original));

            hub.Publish(new PetsFrenzied(8f, 0.3f));
            Assert.That(Icon(second).gameObject.activeSelf, Is.False, "A fallen animal is not buffed.");
            Assert.That(Icon(lead).gameObject.activeSelf, Is.True);
        }
        finally
        {
            hub.Dispose();
            Object.DestroyImmediate(parent);
        }
    }

    // ---- Rule 5: Howl -----------------------------------------------------------------------------

    [Test]
    public void HowlRanks_HealOnly()
    {
        using IObjectResolver boot = Boot();
        var catalog = boot.Resolve<ContentCatalog>();
        PathSkillSpec row = catalog.Path(PathId).Skills[1];
        SkillSpec[] ranks = Enumerable.Range(1, 4).Select(i => catalog.Skill(row.Ranks.Tier(i).Single())).ToArray();

        Assert.That(ranks[0].Active.OnCast.Single(), Is.TypeOf<Howl>());
        Assert.That(new[] { ranks[1], ranks[2] }.Select(rank => ((ModifyStat)rank.Effects.Single()).Stat),
            Is.All.EqualTo(PlayerStat.HowlHeal));
        Assert.That(ranks.SelectMany(rank => rank.Effects).OfType<ModifyStat>().Select(effect => effect.Stat),
            Has.None.EqualTo(PlayerStat.HowlSeconds));

        float heal = 0.2f;
        float[] heals = ranks.Select(rank => heal += rank.Effects.OfType<ModifyStat>().Sum(effect => effect.Value)).ToArray();
        Assert.That(heals, Is.EqualTo(new[] { 0.2f, 0.3f, 0.4f, 0.4f }).Within(0.0001f));
        Assert.That(System.IO.File.ReadAllText("Assets/_Project/Data/Effects/Packleader/HowlCast.asset"),
            Does.Not.Contain("_attackSpeedBonus"), "HowlCast re-saved without BT-06a's dead field.");

        var english = Load<LocalizationTable>("Data/Localisation/English.asset").ToDictionary();
        string[] texts = ranks.Select(rank => english[rank.DescriptionKey]).ToArray();
        Assert.That(texts, Is.EqualTo(new[]
        {
            "Every animal heals 20% of max HP.",
            "Howl heals 30% of max HP.",
            "Howl heals 40% of max HP.",
            "Howl cooldown drops to 11 s.",
        }));

        using var hub = new DomainEventHub();
        RunSession session = StartRun(catalog, hub, Load<CharacterDefinition>("Data/Characters/Beastmaster.asset"), level: 20);
        session.ChoosePath(PathId);
        foreach (SkillSpec rank in ranks)
        {
            session.TakeNode(rank.Id);
        }

        Assert.That(CooldownOf(session, HowlId), Is.EqualTo(11f));
    }

    // ---- Fixtures ---------------------------------------------------------------------------------

    private static SkillSpec[] FrenzyRanks(out PathSkillSpec row, out ContentCatalog catalog)
    {
        using IObjectResolver boot = Boot();
        catalog = boot.Resolve<ContentCatalog>();
        row = catalog.Path(PathId).Skills[5];
        PathSkillSpec slot = row;
        ContentCatalog skills = catalog;
        return Enumerable.Range(1, 3).Select(i => skills.Skill(slot.Ranks.Tier(i).Single())).ToArray();
    }

    private static float CooldownOf(RunSession session, ContentId id)
    {
        for (int i = 0; i < session.State.OwnedActiveCount; i++)
        {
            if (session.State.SkillIdAt(i) == id)
            {
                return session.State.SkillCooldownSeconds(i);
            }
        }

        Assert.Fail($"{id.Value} is not owned.");
        return 0f;
    }

    private static Image Icon(PetView pet) => pet.GetComponentInChildren<PetHealthBar>(true).Status;

    private static SkinnedMeshRenderer Body(PetView pet) => pet.GetComponentInChildren<SkinnedMeshRenderer>();

    private static Color AverageOpaque(Sprite sprite)
    {
        string path = UnityEditor.AssetDatabase.GetAssetPath(sprite);
        var texture = new Texture2D(2, 2);
        try
        {
            texture.LoadImage(System.IO.File.ReadAllBytes(path));
            Color sum = Color.clear;
            int count = 0;
            foreach (Color pixel in texture.GetPixels())
            {
                if (pixel.a > 0.5f && Mathf.Max(pixel.r, pixel.g, pixel.b) > 0.4f)
                {
                    sum += pixel;
                    count++;
                }
            }

            return count == 0 ? Color.clear : sum / count;
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }
}
