using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;
using NumericsVector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Content;

[TestFixture]
public sealed class SunscarTests
{
    private const string Root = "Assets/_Project/";
    private const string DunesPath = Root + "Prefabs/Arenas/Sunscar/Arena_Dunes.prefab";
    private const string QuicksandGroup = "Quicksand patches";
    private const string GrovePath = Root + "Prefabs/Arenas/Jungle/Arena_Grove.prefab";
    private const string CourtyardPath = Root + "Prefabs/Arenas/AshenReach/Arena_Courtyard.prefab";
    private static readonly ContentId Sunscar = new("mode.sunscar");
    private static readonly ContentId Jungle = new("mode.jungle");
    private static readonly ContentId Ranger = new("character.ranger");
    private const string ScorpionPath = Root + "Prefabs/Enemies/Scorpion.prefab";
    private static readonly ContentId Scorpion = new("enemy.scorpion");
    private static readonly ContentId Beetle = new("enemy.beetle");
    private static readonly ContentId Brood = new("enemy.brood");
    private static readonly ContentId Scorpling = new("enemy.scorpling");
    private static readonly ContentId Viper = new("enemy.viper");
    private static readonly ContentId Jackal = new("enemy.jackal");
    private static readonly ContentId Glassback = new("enemy.glassback");
    private static readonly ContentId Antlion = new("enemy.antlion");
    private static readonly ContentId Dunes = new("arena.sunscar.dunes");

    [Test]
    public void Sunscar_Mode_ListedAfterJungle()
    {
        ModeSpec mode = Mode("Sunscar");
        Assert.That(mode.Id, Is.EqualTo(Sunscar));
        Assert.That(mode.IsListed, Is.True);
        Assert.That(mode.UnlockedBy, Is.EqualTo(Jungle));
        Assert.That(mode.FinalStage, Is.EqualTo(30));
    }

    [Test]
    public void Sunscar_Mode_RosterIsScorpionBeetleBroodViperJackalGlassbackAntlion()
    {
        ModeSpec mode = Mode("Sunscar");
        Assert.That(mode.Roster.Select(entry => (entry.SpecId, entry.IntroducedAtStage, entry.RetiredAtStage)),
            Is.EqualTo(new[]
            {
                (Scorpion, 1, 0), (Beetle, 2, 0), (Brood, 4, 0), (Viper, 6, 0), (Jackal, 8, 0), (Glassback, 9, 0),
                (Antlion, 11, 0),
            }));
        Assert.That(mode.BossRoster, Is.Empty);
        Assert.That(mode.ScriptedStages, Is.Empty);
    }

    [Test]
    public void Sunscar_Arena_OnlyDunesEveryStage()
    {
        ModeSpec mode = Mode("Sunscar");
        Assert.That(mode.Arenas, Has.Count.EqualTo(1));
        for (int stage = 1; stage <= 30; stage++)
        {
            Assert.That(mode.ArenaFor(stage, 7), Is.EqualTo(Dunes), $"Stage {stage}");
        }
    }

    [Test]
    public void Scorpion_Asset_MatchesDesign()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.ToSpec().Id == Scorpion);
        Assert.That(definition, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "Data/Enemies/Scorpion.asset")));
        EnemySpec spec = definition.ToSpec();
        Assert.That(spec.NameKey.Key, Is.EqualTo("enemy.scorpion.name"));
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((36f, 2.2f, 1, 4, 18f)));
        Assert.That(spec.IsLight, Is.True);
        Assert.That(spec.IsElite, Is.False);
        Assert.That((spec.ContactDamage, spec.Reach, spec.WindupTime, spec.RecoverTime, spec.AggroRange),
            Is.EqualTo((8f, 1.5f, 0.6f, 0.8f, 30f)));
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Chaser));
        Assert.That((spec.HopTime, spec.HopRestTime), Is.EqualTo((0f, 0f)));
        Assert.That(definition.ToLook().Body, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(ScorpionPath).GetComponent<EnemyView>()));
        Assert.That(spec.Skitter, Is.Not.Null);
        SkitterSpec skitter = spec.Skitter;
        Assert.That((skitter.RingRadius, skitter.FanDegrees, skitter.SpreadTimeout, skitter.ZoneLength, skitter.ZoneHalfWidth),
            Is.EqualTo((4f, 120f, 3f, 1.8f, 0.5f)));
    }

    [Test]
    public void Scorpion_ExistingEnemies_NoSkitter()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemySpec spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();
            if (spec.Id != Scorpion && spec.Id != Brood && spec.Id != Scorpling)
            {
                Assert.That(spec.Skitter, Is.Null, path);
            }
        }
    }

    [Test]
    public void Scorpion_Strip_MatchesSpec()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.ToSpec().Id == Scorpion);
        SkitterSpec skitter = definition.ToSpec().Skitter;
        var tell = new SerializedObject(definition.ToLook().Body.GetComponent<ScorpionTellView>());
        Assert.That(tell.FindProperty("_stripLength").floatValue, Is.EqualTo(skitter.ZoneLength));
        Assert.That(tell.FindProperty("_stripHalfWidth").floatValue, Is.EqualTo(skitter.ZoneHalfWidth));
    }

    [Test]
    public void Scorpion_NameKey_InEveryLanguage()
    {
        var key = new LocKey("enemy.scorpion.name");
        var english = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset");
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset");
        Assert.That(english.ToDictionary()[key], Is.EqualTo("Sand Scorpion"));
        Assert.That(pseudo.ToDictionary()[key], Is.Not.Empty);
    }

    [Test]
    public void Beetle_Asset_MatchesDesign()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.ToSpec().Id == Beetle);
        Assert.That(definition, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "Data/Enemies/Beetle.asset")));
        EnemySpec spec = definition.ToSpec();
        Assert.That(spec.NameKey.Key, Is.EqualTo("enemy.beetle.name"));
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((28f, 2.8f, 3, 7, 21f)));
        Assert.That(spec.IsLight, Is.True);
        Assert.That(spec.IsElite, Is.False);
        Assert.That((spec.ContactDamage, spec.Reach, spec.WindupTime, spec.RecoverTime, spec.AggroRange),
            Is.EqualTo((12f, 1.2f, 0.7f, 2.3f, 30f)));
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Spitter));
        Assert.That(spec.Projectile, Is.Not.Null);
        ProjectileSpec projectile = spec.Projectile;
        Assert.That((projectile.StandoffRange, projectile.Speed, projectile.Radius, projectile.Count),
            Is.EqualTo((12f, 6f, 1.6f, 1)));
        Assert.That(spec.Bombardier, Is.Not.Null);
        BombardierSpec bombardier = spec.Bombardier;
        Assert.That((bombardier.TurnRateDegrees, bombardier.StartConeDegrees, bombardier.DropConeDegrees),
            Is.EqualTo((45f, 10f, 20f)));
        Assert.That(spec.Slinger, Is.Null);

        EnemyLook look = definition.ToLook();
        Assert.That(look.MarksLanding, Is.True);
        Assert.That(look.Body, Is.Not.Null);
        Assert.That(look.Body, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/Beetle.prefab").GetComponent<EnemyView>()));
        Assert.That(look.Projectile, Is.Not.Null);
        Assert.That(look.Projectile, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Projectiles/BeetleGlob.prefab").GetComponent<ProjectileView>()));
    }

    [Test]
    public void Beetle_ExistingEnemies_NoBombardier()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemySpec spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();
            if (spec.Id != Beetle)
            {
                Assert.That(spec.Bombardier, Is.Null, path);
            }
        }
    }

    [Test]
    public void Beetle_Standoff_InsideTheStormsClearing()
    {
        EnemySpec spec = BootEnemies().Select(enemy => enemy.ToSpec()).Single(enemy => enemy.Id == Beetle);
        var storm = Arena(DunesPath).GetComponentInChildren<SunscarSandstormView>(true);
        Assert.That(spec.Projectile.StandoffRange, Is.LessThan(storm.ClearRadius));
    }

    [Test]
    public void Beetle_NameKey_InEveryLanguage()
    {
        var key = new LocKey("enemy.beetle.name");
        var english = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset");
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset");
        Assert.That(english.ToDictionary()[key], Is.EqualTo("Bombardier Beetle"));
        Assert.That(pseudo.ToDictionary()[key], Is.Not.Empty);
    }

    [Test]
    public void Brood_Asset_MatchesDesign()
    {
        EnemySpec spec = AuthoredBroodEnemy(Brood, "BroodMother");
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((50f, 1.5f, 2, 12, 24f)));
        Assert.That((spec.ContactDamage, spec.Reach, spec.WindupTime, spec.RecoverTime, spec.AggroRange),
            Is.EqualTo((14f, 2f, 0.8f, 1f, 30f)));
        SkitterSpec skitter = spec.Skitter;
        Assert.That((skitter.RingRadius, skitter.FanDegrees, skitter.SpreadTimeout, skitter.ZoneLength, skitter.ZoneHalfWidth),
            Is.EqualTo((5f, 120f, 4f, 2.4f, 0.7f)));
        Assert.That(spec.Brood, Is.Not.Null);
        Assert.That((spec.Brood.Child, spec.Brood.Count, spec.Brood.BurstDelay, spec.Brood.DropRadius),
            Is.EqualTo((Scorpling, 3, 0.5f, 1.2f)));
    }

    [Test]
    public void Scorpling_Asset_MatchesDesign()
    {
        EnemySpec spec = AuthoredBroodEnemy(Scorpling, "Scorpling");
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((10f, 2.8f, 1, 3, 3f)));
        Assert.That((spec.ContactDamage, spec.Reach, spec.WindupTime, spec.RecoverTime, spec.AggroRange),
            Is.EqualTo((4f, 1f, 0.5f, 0.6f, 50f)));
        SkitterSpec skitter = spec.Skitter;
        Assert.That((skitter.RingRadius, skitter.FanDegrees, skitter.SpreadTimeout, skitter.ZoneLength, skitter.ZoneHalfWidth),
            Is.EqualTo((3f, 120f, 2f, 1.2f, 0.35f)));
        Assert.That(spec.Brood, Is.Null);
    }

    private static EnemySpec AuthoredBroodEnemy(ContentId id, string assetName)
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.Id == id.Value);
        Assert.That(definition, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + $"Data/Enemies/{assetName}.asset")));
        var body = AssetDatabase.LoadAssetAtPath<GameObject>(Root + $"Prefabs/Enemies/{assetName}.prefab");
        Assert.That(body.GetComponent<EnemyView>(), Is.Not.Null);
        Assert.That(definition.ToLook().Body, Is.SameAs(body.GetComponent<EnemyView>()));
        EnemySpec spec = definition.ToSpec();
        Assert.That(spec.NameKey.Key, Is.EqualTo(id.Value + ".name"));
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Chaser));
        Assert.That(spec.IsElite, Is.False);
        Assert.That(spec.Skitter, Is.Not.Null);
        return spec;
    }

    [Test]
    public void Brood_ExistingEnemies_NoBrood()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemySpec spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();
            if (spec.Id != Brood)
            {
                Assert.That(spec.Brood, Is.Null, path);
            }
        }
    }

    [Test]
    public void Scorpling_InNoRoster()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:ModeDefinition", new[] { Root + "Data/Modes" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ModeSpec mode = AssetDatabase.LoadAssetAtPath<ModeDefinition>(path).ToSpec();
            Assert.That(mode.Roster.Select(entry => entry.SpecId), Has.No.Member(Scorpling), path);
        }
    }

    [Test]
    public void Scorpling_SlowerThanEveryClass()
    {
        EnemySpec spec = BootEnemies().Select(enemy => enemy.ToSpec()).Single(enemy => enemy.Id == Scorpling);
        foreach (string guid in AssetDatabase.FindAssets("t:CharacterDefinition", new[] { Root + "Data/Characters" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            CharacterSpec character = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path).ToSpec();
            Assert.That(spec.MoveSpeed, Is.LessThan(character.Movement.Speed), path);
        }
    }

    [Test]
    public void Brood_NameKeys_InEveryLanguage()
    {
        var english = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset").ToDictionary();
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset").ToDictionary();
        var broodKey = new LocKey("enemy.brood.name");
        var scorplingKey = new LocKey("enemy.scorpling.name");
        Assert.That(english[broodKey], Is.EqualTo("Brood Scorpion"));
        Assert.That(english[scorplingKey], Is.EqualTo("Scorpling"));
        Assert.That(pseudo[broodKey], Is.Not.Empty);
        Assert.That(pseudo[scorplingKey], Is.Not.Empty);
    }

    [Test]
    public void Viper_Asset_MatchesDesign()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.Id == Viper.Value);
        Assert.That(definition, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "Data/Enemies/Viper.asset")));
        EnemySpec spec = definition.ToSpec();
        Assert.That(spec.NameKey.Key, Is.EqualTo("enemy.viper.name"));
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((44f, 2.4f, 4, 9, 27f)));
        Assert.That(spec.IsLight, Is.True);
        Assert.That(spec.IsElite, Is.False);
        Assert.That((spec.ContactDamage, spec.Reach, spec.WindupTime, spec.RecoverTime, spec.AggroRange),
            Is.EqualTo((14f, 1.2f, 0.9f, 1f, 30f)));
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Lunger));
        Assert.That(spec.Pack, Is.Null);
        Assert.That(spec.Lunge, Is.Not.Null);
        LungeSpec lunge = spec.Lunge;
        Assert.That((lunge.Range, lunge.Length, lunge.Speed, lunge.HalfWidth, lunge.Cooldown),
            Is.EqualTo((8f, 12f, 30f, 0.6f, 0f)));
        Assert.That(spec.Sidewinder, Is.Not.Null);
        SidewinderSpec sidewinder = spec.Sidewinder;
        Assert.That((sidewinder.MissRecoverTime, sidewinder.Speed, sidewinder.ArcDegrees, sidewinder.Radius, sidewinder.Timeout),
            Is.EqualTo((2f, 2.8f, 45f, 7f, 2.5f)));
        Assert.That(definition.ToLook().Body, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/Viper.prefab").GetComponent<EnemyView>()));
    }

    [Test]
    public void Viper_ExistingEnemies_NoSidewinder()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemySpec spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();
            if (spec.Id != Viper)
            {
                Assert.That(spec.Sidewinder, Is.Null, path);
            }
        }
    }

    [Test]
    public void Viper_Strike_InsideTheStormsClearing()
    {
        EnemySpec spec = Catalog().Enemy(Viper);
        var storm = Arena(DunesPath).GetComponentInChildren<SunscarSandstormView>(true);
        Assert.That(spec.Lunge.Range, Is.LessThan(storm.ClearRadius));
    }

    [Test]
    public void Viper_SidewindSlowerThanEveryClass()
    {
        EnemySpec spec = Catalog().Enemy(Viper);
        foreach (string guid in AssetDatabase.FindAssets("t:CharacterDefinition", new[] { Root + "Data/Characters" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            CharacterSpec character = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path).ToSpec();
            Assert.That(spec.MoveSpeed, Is.LessThan(character.Movement.Speed), path);
            Assert.That(spec.Sidewinder.Speed, Is.LessThan(character.Movement.Speed), path);
        }
    }

    [Test]
    public void Viper_NameKey_InEveryLanguage()
    {
        LocKey key = Catalog().Enemy(Viper).NameKey;
        var english = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset");
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset");
        Assert.That(english.ToDictionary()[key], Is.EqualTo("Sidewinder Viper"));
        Assert.That(pseudo.ToDictionary()[key], Is.Not.Empty);
    }

    [Test]
    public void Jackal_Asset_MatchesDesign()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.Id == Jackal.Value);
        Assert.That(definition, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "Data/Enemies/Jackal.asset")));
        EnemySpec spec = definition.ToSpec();
        Assert.That(spec.NameKey.Key, Is.EqualTo("enemy.jackal.name"));
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((28f, 2.8f, 3, 7, 21f)));
        Assert.That(spec.IsLight, Is.True);
        Assert.That(spec.IsElite, Is.False);
        Assert.That((spec.ContactDamage, spec.Reach, spec.WindupTime, spec.RecoverTime, spec.AggroRange),
            Is.EqualTo((12f, 1.2f, 0.7f, 2.3f, 30f)));
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Spitter));
        Assert.That(spec.Projectile, Is.Not.Null);
        ProjectileSpec projectile = spec.Projectile;
        Assert.That((projectile.StandoffRange, projectile.Speed, projectile.Radius, projectile.Count),
            Is.EqualTo((12f, 7f, 1.5f, 1)));
        Assert.That(spec.Slinger, Is.Not.Null);
        SlingerSpec slinger = spec.Slinger;
        Assert.That((slinger.MaxLead, slinger.RetreatSpeedFraction, slinger.RetreatTime),
            Is.EqualTo((3f, 0.7f, 3f)));

        EnemyLook look = definition.ToLook();
        Assert.That(look.MarksLanding, Is.True);
        Assert.That(look.Body, Is.Not.Null);
        Assert.That(look.Body, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/Jackal.prefab").GetComponent<EnemyView>()));
        Assert.That(look.Projectile, Is.Not.Null);
        Assert.That(look.Projectile, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Projectiles/SlingStone.prefab").GetComponent<ProjectileView>()));
    }

    [Test]
    public void Jackal_ExistingEnemies_NoSlinger()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemySpec spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();
            if (spec.Id != Jackal)
            {
                Assert.That(spec.Slinger, Is.Null, path);
            }
        }
    }

    [Test]
    public void Jackal_Standoff_InsideTheStormsClearing()
    {
        EnemySpec spec = Catalog().Enemy(Jackal);
        var storm = Arena(DunesPath).GetComponentInChildren<SunscarSandstormView>(true);
        Assert.That(spec.Projectile.StandoffRange, Is.LessThan(storm.ClearRadius));
    }

    [Test]
    public void Jackal_NameKey_InEveryLanguage()
    {
        LocKey key = Catalog().Enemy(Jackal).NameKey;
        var english = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset");
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset");
        Assert.That(english.ToDictionary()[key], Is.EqualTo("Jackal Slinger"));
        Assert.That(pseudo.ToDictionary()[key], Is.Not.Empty);
    }

    [Test]
    public void Glassback_Asset_MatchesDesign()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.Id == Glassback.Value);
        Assert.That(definition, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "Data/Enemies/Glassback.asset")));
        EnemySpec spec = definition.ToSpec();
        Assert.That(spec.NameKey.Key, Is.EqualTo("enemy.glassback.name"));
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((40f, 2.6f, 2, 8, 24f)));
        Assert.That(spec.IsLight, Is.True);
        Assert.That(spec.IsElite, Is.False);
        Assert.That((spec.ContactDamage, spec.Reach, spec.WindupTime, spec.RecoverTime, spec.AggroRange),
            Is.EqualTo((12f, 1.2f, 0.5f, 0.8f, 30f)));
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Chaser));
        Assert.That(spec.Pack, Is.Null);
        Assert.That(spec.Leap, Is.Null);
        Assert.That(spec.Brood, Is.Null);

        Assert.That(spec.Shine, Is.Not.Null);
        ShineSpec shine = spec.Shine;
        Assert.That((shine.GlintSeconds, shine.ShineSeconds, shine.OpenSeconds, shine.Cooldown,
                shine.TriggerRange, shine.OpenDamageTaken),
            Is.EqualTo((0.5f, 2f, 1f, 4f, 12f, 2f)));
        Assert.That((shine.BoltDamage, shine.BoltSpeed, shine.BoltRadius, shine.BoltMinFlight, shine.BoltInterval),
            Is.EqualTo((6f, 14f, 0.8f, 0.3f, 0.4f)));

        EnemyLook look = definition.ToLook();
        Assert.That(look.Body, Is.Not.Null);
        Assert.That(look.Body, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/Glassback.prefab").GetComponent<EnemyView>()));
    }

    [Test]
    public void Glassback_Look_GlassBoltWithRing()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.Id == Glassback.Value);
        EnemyLook look = definition.ToLook();
        Assert.That(look.Projectile, Is.Not.Null);
        Assert.That(look.Projectile, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Projectiles/GlassBolt.prefab").GetComponent<ProjectileView>()));
        Assert.That(look.MarksLanding, Is.True);
        Assert.That(definition.ToSpec().Projectile, Is.Null);
    }

    [Test]
    public void Glassback_ExistingEnemies_NoShine()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemySpec spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();
            if (spec.Id != Glassback)
            {
                Assert.That(spec.Shine, Is.Null, path);
            }
        }
    }

    [Test]
    public void Glassback_TriggerInsideTheStormsClearing()
    {
        EnemySpec spec = Catalog().Enemy(Glassback);
        var storm = Arena(DunesPath).GetComponentInChildren<SunscarSandstormView>(true);
        Assert.That(spec.Shine.TriggerRange, Is.LessThan(storm.ClearRadius));
    }

    [Test]
    public void Glassback_SlowerThanEveryClass()
    {
        EnemySpec spec = Catalog().Enemy(Glassback);
        foreach (string guid in AssetDatabase.FindAssets("t:CharacterDefinition", new[] { Root + "Data/Characters" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            CharacterSpec character = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path).ToSpec();
            Assert.That(spec.MoveSpeed, Is.LessThan(character.Movement.Speed), path);
        }
    }

    [Test]
    public void Glassback_NameKey_InEveryLanguage()
    {
        LocKey key = Catalog().Enemy(Glassback).NameKey;
        var english = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset");
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset");
        Assert.That(english.ToDictionary()[key], Is.EqualTo("Glassback Beetle"));
        Assert.That(pseudo.ToDictionary()[key], Is.Not.Empty);
    }

    [Test]
    public void Antlion_Asset_MatchesDesign()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.Id == Antlion.Value);
        Assert.That(definition, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "Data/Enemies/Antlion.asset")));
        EnemySpec spec = Catalog().Enemy(Antlion);
        Assert.That(spec.NameKey.Key, Is.EqualTo("enemy.antlion.name"));
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((56f, 2.4f, 5, 12, 30f)));
        Assert.That(spec.IsLight, Is.True);
        Assert.That(spec.IsElite, Is.False);
        Assert.That(spec.AggroRange, Is.EqualTo(30f));
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Antlion));
        Assert.That(definition.ToLook().Body, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/Antlion.prefab").GetComponent<EnemyView>()));
        Assert.That(spec.Burrow, Is.Not.Null);
        BurrowSpec burrow = spec.Burrow;
        Assert.That((burrow.TunnelSpeed, burrow.BehindDistance, burrow.LockDistance,
                burrow.MinTunnelSeconds, burrow.MaxTunnelSeconds),
            Is.EqualTo((4.5f, 2f, 1.2f, 1.5f, 4f)));
        Assert.That((burrow.RingRadius, burrow.ArmSeconds, burrow.EruptionDamage, burrow.SurfacedSeconds, burrow.DigSeconds),
            Is.EqualTo((1.6f, 0.8f, 16f, 2.5f, 0.6f)));
    }

    [Test]
    public void Antlion_ExistingEnemies_NoBurrow()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemySpec spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();
            if (spec.Id != Antlion)
            {
                Assert.That(spec.Burrow, Is.Null, path);
            }
        }
    }

    [Test]
    public void Antlion_TunnelOutrunsEveryClass()
    {
        BurrowSpec burrow = Catalog().Enemy(Antlion).Burrow;
        foreach (CharacterSpec character in AuthoredCharacters())
        {
            Assert.That(burrow.TunnelSpeed, Is.GreaterThan(character.Movement.Speed), character.Id.Value);
        }
    }

    [Test]
    public void Antlion_RingClearableInArm()
    {
        BurrowSpec burrow = Catalog().Enemy(Antlion).Burrow;
        foreach (CharacterSpec character in AuthoredCharacters())
        {
            Assert.That(character.Movement.Speed * burrow.ArmSeconds,
                Is.GreaterThanOrEqualTo(burrow.RingRadius), character.Id.Value);
        }
    }

    [Test]
    public void Antlion_NameKey_InEveryLanguage()
    {
        LocKey key = Catalog().Enemy(Antlion).NameKey;
        var english = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset");
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset");
        Assert.That(english.ToDictionary()[key], Is.EqualTo("Antlion"));
        Assert.That(pseudo.ToDictionary()[key], Is.Not.Empty);
    }

    [Test]
    public void Sunscar_RosterByStage_AntlionFrom11()
    {
        ModeSpec mode = Mode("Sunscar");
        var roster = new RosterEntry[mode.Roster.Count];
        int count = mode.RosterFor(10, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId),
            Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper, Jackal, Glassback }));
        count = mode.RosterFor(11, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId),
            Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper, Jackal, Glassback, Antlion }));
    }

    [Test]
    public void Sunscar_RosterByStage_GlassbackFrom9()
    {
        ModeSpec mode = Mode("Sunscar");
        var roster = new RosterEntry[mode.Roster.Count];
        int count = mode.RosterFor(8, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId),
            Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper, Jackal }));
        count = mode.RosterFor(9, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId),
            Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper, Jackal, Glassback }));
    }

    [Test]
    public void Sunscar_RosterByStage_JackalFrom8()
    {
        ModeSpec mode = Mode("Sunscar");
        var roster = new RosterEntry[mode.Roster.Count];
        int count = mode.RosterFor(7, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId), Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper }));
        count = mode.RosterFor(8, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId), Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper, Jackal }));
    }

    [Test]
    public void Sunscar_RosterByStage_ViperFrom6()
    {
        ModeSpec mode = Mode("Sunscar");
        var roster = new RosterEntry[mode.Roster.Count];
        int count = mode.RosterFor(5, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId), Is.EqualTo(new[] { Scorpion, Beetle, Brood }));
        count = mode.RosterFor(6, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId), Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper }));
    }

    [Test]
    public void Sunscar_RosterByStage_BroodFrom4()
    {
        ModeSpec mode = Mode("Sunscar");
        var roster = new RosterEntry[mode.Roster.Count];
        int count = mode.RosterFor(3, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId), Is.EqualTo(new[] { Scorpion, Beetle }));
        count = mode.RosterFor(4, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId), Is.EqualTo(new[] { Scorpion, Beetle, Brood }));
    }

    [Test]
    public void Sunscar_RosterByStage_BeetleFrom2()
    {
        ModeSpec mode = Mode("Sunscar");
        var roster = new RosterEntry[mode.Roster.Count];
        int count = mode.RosterFor(1, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId), Is.EqualTo(new[] { Scorpion }));
        count = mode.RosterFor(2, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId), Is.EqualTo(new[] { Scorpion, Beetle }));
    }

    [Test]
    public void Sunscar_Stage1_WavesClearsOpensPortal() => AssertStageWavesClear(1, Scorpion);

    [Test]
    public void Sunscar_Stage2_WavesClear() => AssertStageWavesClear(2, Scorpion, Beetle);

    [Test]
    public void Sunscar_Stage3_WavesClearWithoutBrood() => AssertStageWavesClear(3, Scorpion, Beetle);

    [Test]
    public void Sunscar_Stage4_WavesClear()
    {
        RecordingEvents events = AssertStageWavesClear(4, new[] { Scorpion, Beetle, Brood, Scorpling }, maxSeconds: 240);
        int births = 0;
        int deaths = 0;
        int broods = 0;
        foreach (object evt in events.All)
        {
            switch (evt)
            {
                case EnemyBroodBurst:
                    broods++;
                    break;
                case EnemySpawned spawned when spawned.SpecId == Scorpling:
                    births++;
                    Assert.That(births, Is.LessThanOrEqualTo(broods * 3), "Scorplings come from a brood burst.");
                    break;
                case EnemyDied died when died.SpecId == Scorpling:
                    deaths++;
                    break;
                case StageCleared:
                    Assert.That(broods, Is.GreaterThan(0));
                    Assert.That(births, Is.EqualTo(broods * 3));
                    Assert.That(deaths, Is.EqualTo(births), "Every scorpling dies before the portal opens.");
                    break;
            }
        }
    }

    [Test]
    public void Sunscar_Stage6_WavesClear() =>
        AssertStageWavesClear(6, new[] { Scorpion, Beetle, Brood, Viper, Scorpling }, maxSeconds: 240, sidestepTells: true);

    [Test]
    public void Sunscar_Stage8_WavesClear() =>
        AssertStageWavesClear(8, new[] { Scorpion, Beetle, Brood, Viper, Jackal, Scorpling },
            maxSeconds: 240, sidestepTells: true, dodgeLandings: true);

    [Test]
    public void Sunscar_Stage9_WavesClear() =>
        AssertStageWavesClear(9, new[] { Scorpion, Beetle, Brood, Viper, Jackal, Glassback, Scorpling },
            maxSeconds: 240, sidestepTells: true, dodgeLandings: true);

    [Test]
    public void Sunscar_Stage11_WavesClear() =>
        AssertStageWavesClear(11, new[] { Scorpion, Beetle, Brood, Viper, Jackal, Glassback, Antlion, Scorpling },
            maxSeconds: 240, sidestepTells: true, dodgeLandings: true, seed: 11);

    private static RecordingEvents AssertStageWavesClear(int stage, params ContentId[] expectedEnemies) =>
        AssertStageWavesClear(stage, expectedEnemies, maxSeconds: 180);

    private static RecordingEvents AssertStageWavesClear(
        int stage, ContentId[] expectedEnemies, int maxSeconds,
        bool sidestepTells = false, bool dodgeLandings = false, int seed = 7)
    {
        const float frameTime = 1f / 32f;
        ContentCatalog catalog = Catalog(Mode("Sunscar"));
        var random = new SeededRandom(seed);
        var events = new RecordingEvents();
        var intents = new RecordingIntents();
        var recorder = new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, intents, recorder, 64, 28, 64);
        RunSnapshot snapshot = new TestStart(catalog).At(Sunscar, Ranger, random.Seed, random.Capture(), stage);

        try
        {
            session.Start(new RunConfig(Sunscar, Ranger, random.Seed, stage, SpawnPlan.Empty, snapshot));
            StageArrived arrival = events.Of<StageArrived>().Single();
            Assert.That((arrival.Stage, arrival.ArenaId), Is.EqualTo((stage, Dunes)));

            var world = new WorldSnapshot(64);
            NumericsVector3[] points =
            {
                new(-8f, 0f, 8f), new(8f, 0f, 8f),
                new(-8f, 0f, -8f), new(8f, 0f, -8f),
            };
            var positions = new Dictionary<int, NumericsVector3>();
            NumericsVector3 player = NumericsVector3.Zero;
            var movementExtents = new NumericsVector3(12f, 0f, 12f);
            int tellsRead = 0;
            int shotsRead = 0;
            int ringsRead = 0;
            for (int frame = 0; frame < maxSeconds * 32 && events.Count<StageCleared>() == 0; frame++)
            {
                // Walk the bodies by the previous tick's intents, then report the living ones in sight.
                foreach (var move in intents.EnemyMoves)
                {
                    if (positions.TryGetValue(move.Id, out NumericsVector3 position))
                    {
                        positions[move.Id] = position + (move.Velocity * frameTime);
                    }
                }

                intents.Clear();
                world.Clear();
                world.Dt = frameTime;
                world.SpawnPoints = points;
                // Step out of the tell, then stand still to let the Ranger fire.
                NumericsVector3 dodgeOrigin = player;
                IReadOnlyList<EnemyTelegraph> tells = events.Of<EnemyTelegraph>();
                for (; tellsRead < tells.Count; tellsRead++)
                {
                    if (positions.TryGetValue(tells[tellsRead].Id, out NumericsVector3 attacker))
                    {
                        NumericsVector3 away = (sidestepTells ? dodgeOrigin : player) - attacker;
                        away = away.LengthSquared() > 1e-4f ? NumericsVector3.Normalize(away) : NumericsVector3.UnitX;
                        if (sidestepTells)
                        {
                            var sideways = new NumericsVector3(-away.Z, 0f, away.X);
                            away = NumericsVector3.Dot(sideways, player) > 0f ? -sideways : sideways;
                        }

                        player += away * 3f;
                    }
                }

                // Keep the stage-6 Ranger within aggro range of the four spawn points.
                if (sidestepTells)
                {
                    player = NumericsVector3.Clamp(player, -movementExtents, movementExtents);
                }

                if (dodgeLandings)
                {
                    // A led throw can follow the wind-up dodge. Its landing marker fixes the target.
                    IReadOnlyList<ProjectileFired> shots = events.Of<ProjectileFired>();
                    for (; shotsRead < shots.Count; shotsRead++)
                    {
                        ProjectileFired shot = shots[shotsRead];
                        if ((shot.SpecId == Jackal || shot.SpecId == Beetle || shot.SpecId == Glassback) &&
                            NumericsVector3.DistanceSquared(player, shot.Target) <= shot.Radius * shot.Radius)
                        {
                            player = StepAwayFrom(player, shot.Target, movementExtents);
                        }
                    }

                    IReadOnlyList<AntlionRingArmed> rings = events.Of<AntlionRingArmed>();
                    for (; ringsRead < rings.Count; ringsRead++)
                    {
                        AntlionRingArmed ring = rings[ringsRead];
                        if (NumericsVector3.DistanceSquared(player, ring.Centre) <= ring.Radius * ring.Radius)
                        {
                            player = StepAwayFrom(player, ring.Centre, movementExtents);
                        }
                    }
                }

                world.PlayerPosition = player;
                foreach (EnemySpawned spawned in events.Of<EnemySpawned>())
                {
                    if (!session.State.IsEnemyAlive(spawned.Id))
                    {
                        continue;
                    }

                    if (!positions.ContainsKey(spawned.Id))
                    {
                        positions.Add(spawned.Id, spawned.Position);
                    }

                    world.AddEnemy() = new EnemySense
                    {
                        Id = spawned.Id,
                        Position = positions[spawned.Id],
                        HasLineOfSight = true,
                    };
                }

                session.Tick(world);
                if (events.Count<PlayerDied>() > 0)
                {
                    Assert.Fail($"The Ranger died at {player} after {frame * frameTime:F1}s on stage {stage}.");
                }
            }

            Assert.That(events.Count<WaveStarted>(), Is.GreaterThan(0));
            Assert.That(events.Of<EnemySpawned>().Select(e => e.SpecId).Distinct(), Is.EquivalentTo(expectedEnemies));
            var survivors = events.Of<EnemySpawned>()
                .Where(spawn => session.State.IsEnemyAlive(spawn.Id))
                .Select(spawn => $"{spawn.SpecId} at {positions.GetValueOrDefault(spawn.Id, spawn.Position)}");
            Assert.That(events.Count<StageCleared>(), Is.EqualTo(1),
                $"Stage {stage} after {maxSeconds}s, player at {player}, survivors: {string.Join(", ", survivors)}");
            return events;
        }
        finally
        {
            session.End();
        }
    }

    private static NumericsVector3 StepAwayFrom(
        NumericsVector3 player, NumericsVector3 danger, NumericsVector3 movementExtents)
    {
        var step = 3f * NumericsVector3.UnitX;
        var side = 3f * NumericsVector3.UnitZ;
        return new[] { player + step, player - step, player + side, player - side }
            .Select(point => NumericsVector3.Clamp(point, -movementExtents, movementExtents))
            .OrderByDescending(point => NumericsVector3.DistanceSquared(point, danger))
            .First();
    }

    [Test]
    public void Quicksand_Arenas_OnlyDunesWithClearFootprints()
    {
        const float margin = 4f;
        const string arenas = Root + "Prefabs/Arenas";
        int dunesFound = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { arenas }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (!prefab.TryGetComponent(out ArenaView arena))
            {
                continue;
            }

            Transform patches = arena.transform.Find(QuicksandGroup);
            if (arena.Id != Dunes)
            {
                Assert.That(patches, Is.Null, arena.name);
                continue;
            }

            dunesFound++;
            Assert.That(patches, Is.Not.Null);
            Assert.That(patches.childCount, Is.InRange(2, 4));
            Vector3[] anchors = { arena.EntryPosition, arena.PlayerStart, arena.GatePosition };
            foreach (Transform patch in patches)
            {
                Bounds footprint = patch.GetComponent<MeshRenderer>().bounds;
                Assert.That(footprint.size.x, Is.GreaterThan(1f), patch.name);
                Assert.That(footprint.size.z, Is.GreaterThan(1f), patch.name);
                foreach (Vector3 anchor in anchors)
                {
                    var groundAnchor = new Vector3(anchor.x, footprint.center.y, anchor.z);
                    Assert.That(footprint.SqrDistance(groundAnchor), Is.GreaterThanOrEqualTo(margin * margin), patch.name);
                }

                foreach (string coverGroup in new[] { "Sandstone spurs", "Wind-carved rim", "Gate" })
                {
                    foreach (var cover in arena.transform.Find(coverGroup).GetComponentsInChildren<MeshRenderer>(true))
                    {
                        Bounds coverFootprint = cover.bounds;
                        coverFootprint.Encapsulate(new Vector3(coverFootprint.center.x, footprint.center.y, coverFootprint.center.z));
                        Assert.That(footprint.Intersects(coverFootprint), Is.False, $"{patch.name} overlaps {cover.name}");
                    }
                }
            }
        }

        Assert.That(dunesFound, Is.EqualTo(1));
    }

    [Test]
    public void Quicksand_Renderers_UseSharedGroundMaterialOnly()
    {
        Transform patches = Arena(DunesPath).transform.Find(QuicksandGroup);
        Assert.That(patches, Is.Not.Null);
        Assert.That(patches.GetComponents<Component>(), Has.Length.EqualTo(1));
        var shared = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/Environment/M_SunscarQuicksand.mat");
        Assert.That(shared, Is.Not.Null);
        Assert.That(shared.shader.name, Is.EqualTo("Universal Render Pipeline/Particles/Unlit"));
        Assert.That(shared.mainTexture, Is.EqualTo(
            AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Art/UI/Hud/Glow_Soft.png")));
        Assert.That(shared.renderQueue, Is.LessThan((int)UnityEngine.Rendering.RenderQueue.Transparent - 100));
        foreach (Transform patch in patches)
        {
            Assert.That(patch.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Ignore Raycast")));
            Assert.That(patch.childCount, Is.Zero);
            Assert.That(patch.GetComponents<Component>().Select(component => component.GetType()),
                Is.EquivalentTo(new[] { typeof(Transform), typeof(MeshFilter), typeof(MeshRenderer) }));
            var mesh = patch.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount, Is.EqualTo(4));
            Assert.That(Mathf.Abs(Vector3.Dot(patch.forward, Vector3.up)), Is.GreaterThan(0.999f));
            Material[] materials = patch.GetComponent<MeshRenderer>().sharedMaterials;
            Assert.That(materials.Select(material => material.shader.name), Has.No.Member("Soulvail/Cloak"));
            Assert.That(materials, Is.EqualTo(new[] { shared }));
        }
    }

    [Test]
    public void Dunes_Renderers_NoCyanOrDangerHue()
    {
        Renderer[] renderers = Arena(DunesPath).GetComponentsInChildren<Renderer>(true);
        Assert.That(renderers, Is.Not.Empty);
        foreach (Renderer renderer in renderers)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                Assert.That(material, Is.Not.Null, renderer.name);
                string property = material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                Assert.That(material.HasProperty(property), Is.True, material.name);
                Color.RGBToHSV(material.GetColor(property), out float hue, out float saturation, out _);
                float degrees = hue * 360f;
                bool cyan = degrees >= 155f && degrees <= 205f;
                bool danger = degrees <= 25f || degrees >= 350f;
                Assert.That(saturation > 0.05f && (cyan || danger), Is.False, material.name);
            }
        }
    }

    [Test]
    public void Dunes_NavMesh_StartReachesPortal()
    {
        ArenaView dunes = Arena(DunesPath);
        Assert.That(dunes.Surface.navMeshData, Is.Not.Null);
        NavMeshDataInstance mesh = NavMesh.AddNavMeshData(dunes.Surface.navMeshData);
        try
        {
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(dunes.PlayerStart, dunes.GatePosition, NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
        }
        finally
        {
            mesh.Remove();
        }
    }

    [Test]
    public void Dunes_Layout_MatchesGrove()
    {
        ArenaView dunes = Arena(DunesPath);
        ArenaView grove = Arena(GrovePath);
        Assert.That(dunes.HasEntry && dunes.HasGate, Is.True);
        Assert.That(dunes.PlayBounds.size, Is.EqualTo(new Vector2(36f, 36f)));
        Assert.That(dunes.EntryPosition, Is.EqualTo(grove.EntryPosition));
        Assert.That(dunes.PlayerStart, Is.EqualTo(grove.PlayerStart));
        Assert.That(dunes.GatePosition, Is.EqualTo(grove.GatePosition));
    }

    [Test]
    public void Courtyard_Untouched()
    {
        var courtyard = new ContentId("arena.ashen-reach.courtyard");
        Assert.That(Arena(CourtyardPath).Id, Is.EqualTo(courtyard));
        Assert.That(Mode("Descent").Arenas.Select(row => row.ArenaId), Does.Contain(courtyard));
        Assert.That(Mode("Sunscar").Arenas.Select(row => row.ArenaId), Has.No.Member(courtyard));
    }

    [Test]
    public void TestStartPanel_CanStartLockedSunscar()
    {
        ContentCatalog catalog = Catalog(Mode("Jungle"), Mode("Sunscar"));
        Assert.That(PlaceUnlocks.IsUnlocked(Sunscar, PlayerProfile.Default, catalog), Is.False);
        var pending = new PendingRun();
        var loader = new RecordingLoader();
        var root = new GameObject("Test start");
        root.SetActive(false);
        try
        {
            var menu = root.AddComponent<MenuPresenter>();
            typeof(MenuPresenter).GetProperty(nameof(MenuPresenter.SelectedCharacter)).SetValue(menu, Ranger);
            typeof(MenuPresenter).GetProperty(nameof(MenuPresenter.SelectedMode)).SetValue(menu, Jungle);

            var panel = root.AddComponent<TestStartPanel>();
            panel.Construct(pending, loader, catalog, menu);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(TestStartPanel).GetField("_stage", flags).SetValue(panel, 1);
            typeof(TestStartPanel).GetMethod("CyclePlace", flags).Invoke(panel, new object[] { 1 });
            typeof(TestStartPanel).GetMethod("Begin", flags).Invoke(panel, null);

            Assert.That(loader.Loaded, Is.EqualTo(SceneLoader.Run));
            Assert.That(pending.Snapshot.HasValue, Is.True);
            Assert.That(pending.ModeId, Is.EqualTo(Sunscar));
            Assert.That(pending.CharacterId, Is.EqualTo(Ranger));
            Assert.That(pending.Snapshot.Value.StageIndex, Is.EqualTo(1));
            Assert.That(menu.SelectedMode, Is.EqualTo(Jungle));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static ModeSpec Mode(string name) =>
        AssetDatabase.LoadAssetAtPath<ModeDefinition>(Root + $"Data/Modes/{name}.asset").ToSpec();

    private static ArenaView Arena(string path) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<ArenaView>();

    private static IEnumerable<CharacterSpec> AuthoredCharacters() =>
        AssetDatabase.FindAssets("t:CharacterDefinition", new[] { Root + "Data/Characters" })
            .Select(guid => AssetDatabase.LoadAssetAtPath<CharacterDefinition>(AssetDatabase.GUIDToAssetPath(guid)).ToSpec());

    private static ContentCatalog Catalog(params ModeSpec[] modes) =>
        new(new[] { AssetDatabase.LoadAssetAtPath<CharacterDefinition>(Root + "Data/Characters/Ranger.asset").ToSpec() },
            BootEnemies().Select(enemy => enemy.ToSpec()).ToArray(), modes: modes);

    private static EnemyDefinition[] BootEnemies()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Composition/BootScope.prefab");
        var serialized = new SerializedObject(prefab.GetComponent<BootScope>());
        SerializedProperty enemies = serialized.FindProperty("_enemies");
        var definitions = new EnemyDefinition[enemies.arraySize];
        for (int i = 0; i < definitions.Length; i++)
        {
            definitions[i] = (EnemyDefinition)enemies.GetArrayElementAtIndex(i).objectReferenceValue;
        }

        return definitions;
    }

    private sealed class RecordingLoader : SceneLoader
    {
        public string Loaded { get; private set; }

        public override Task LoadAsync(string sceneName)
        {
            Loaded = sceneName;
            return Task.CompletedTask;
        }
    }
}
