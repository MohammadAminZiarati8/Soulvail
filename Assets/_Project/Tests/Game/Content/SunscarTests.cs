using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
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
    private static readonly ContentId Priest = new("enemy.jackal-priest");
    private static readonly ContentId Cobra = new("enemy.cobra");
    private static readonly ContentId Caller = new("enemy.storm-caller");
    private static readonly ContentId Vulture = new("enemy.vulture");
    private static readonly ContentId Dunes = new("arena.sunscar.dunes");
    private static readonly ContentId BuriedKing = new("boss.buried_king");

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
    public void Sunscar_Mode_RosterIsScorpionBeetleBroodViperJackalGlassbackAntlionPriestCobraCallerVulture()
    {
        ModeSpec mode = Mode("Sunscar");
        Assert.That(mode.Roster.Select(entry => (entry.SpecId, entry.IntroducedAtStage, entry.RetiredAtStage)),
            Is.EqualTo(new[]
            {
                (Scorpion, 1, 0), (Beetle, 2, 0), (Brood, 4, 0), (Viper, 6, 0), (Jackal, 8, 0), (Glassback, 9, 0),
                (Antlion, 11, 0), (Priest, 14, 0), (Cobra, 16, 0), (Caller, 18, 0), (Vulture, 22, 0),
            }));
        Assert.That(mode.BossRoster, Has.Count.EqualTo(5));
        for (int stage = 1; stage <= mode.FinalStage; stage++)
        {
            Assert.That(mode.TryGetBossFor(stage, out ContentId boss), Is.EqualTo(stage is 5 or 10 or 15 or 25 or 30));
            if (stage == 5)
            {
                Assert.That(boss.Value, Is.EqualTo("boss.duneclaw"));
            }
            else if (stage == 10)
            {
                Assert.That(boss.Value, Is.EqualTo("boss.shardback"));
            }
            else if (stage == 15)
            {
                Assert.That(boss.Value, Is.EqualTo("boss.hierophant"));
            }
            else if (stage == 25)
            {
                Assert.That(boss.Value, Is.EqualTo("boss.carrion_matriarch"));
            }
            else if (stage == 30)
            {
                Assert.That(boss, Is.EqualTo(BuriedKing));
            }
        }
        Assert.That(mode.ScriptedStages.Select(script => script.Stage),
            Is.EqualTo(Enumerable.Range(1, 30).Where(stage => stage % 5 != 0)));
    }

    [TestCase(1, 2, 5, 90)]
    [TestCase(2, 2, 7, 135)]
    [TestCase(3, 2, 9, 174)]
    [TestCase(4, 2, 11, 234)]
    [TestCase(6, 3, 12, 246)]
    [TestCase(7, 3, 14, 312)]
    [TestCase(8, 3, 16, 336)]
    [TestCase(9, 3, 19, 408)]
    [TestCase(11, 3, 18, 384)]
    [TestCase(12, 3, 20, 432)]
    [TestCase(13, 3, 21, 459)]
    [TestCase(14, 3, 22, 491)]
    [TestCase(16, 4, 22, 484)]
    [TestCase(17, 4, 24, 538)]
    [TestCase(18, 4, 24, 546)]
    [TestCase(19, 4, 26, 587)]
    [TestCase(21, 4, 26, 572)]
    [TestCase(22, 4, 28, 634)]
    [TestCase(23, 4, 30, 689)]
    [TestCase(24, 4, 32, 737)]
    [TestCase(26, 5, 32, 725)]
    [TestCase(27, 5, 33, 755)]
    [TestCase(28, 5, 34, 782)]
    [TestCase(29, 5, 35, 800)]
    public void Sunscar_ScriptedStage_MatchesWaveAndXpBudget(int stage, int waves, int enemies, int xp)
    {
        ModeSpec mode = Mode("Sunscar");
        ContentCatalog catalog = Catalog(mode);
        Assert.That(mode.TryGetScriptedStage(stage, out ScriptedStage script), Is.True);
        Assert.That(script.WaveCount, Is.EqualTo(waves));
        Assert.That(script.Spawns.Sum(spawn => spawn.Count), Is.EqualTo(enemies));
        Assert.That(script.Spawns.GroupBy(spawn => spawn.Wave).Select(wave => wave.Sum(spawn => spawn.Count)),
            Is.All.LessThanOrEqualTo(8));

        float clearXp = 0f;
        foreach (ScriptedSpawn spawn in script.Spawns)
        {
            Assert.That(mode.Roster.Any(entry => entry.SpecId == spawn.SpecId && entry.IntroducedAtStage <= stage),
                Is.True, $"{spawn.SpecId} appears before its introduction.");
            EnemySpec enemy = catalog.Enemy(spawn.SpecId);
            float reward = enemy.XpValue;
            if (enemy.Brood is BroodSpec brood)
            {
                reward += brood.Count * catalog.Enemy(brood.Child).XpValue;
            }

            clearXp += spawn.Count * reward;
        }

        Assert.That(clearXp, Is.EqualTo(xp));
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
    public void Priest_Asset_MatchesDesign()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.Id == Priest.Value);
        Assert.That(definition, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "Data/Enemies/JackalPriest.asset")));
        EnemySpec spec = Catalog().Enemy(Priest);
        Assert.That(spec.NameKey.Key, Is.EqualTo("enemy.jackal-priest.name"));
        EnemySpec shaman = Catalog().Enemy(new ContentId("enemy.shaman"));
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((60f, shaman.MoveSpeed, 8, 16, 32f)));
        Assert.That(spec.IsElite, Is.False);
        Assert.That(spec.ContactDamage, Is.EqualTo(0f));
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Shaman));
        Assert.That(definition.ToLook().Body, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/JackalPriest.prefab").GetComponent<EnemyView>()));
        Assert.That(spec.Shaman, Is.Not.Null);
        Assert.That((spec.Shaman.HoldDistance, spec.Shaman.FleeDistance, spec.Shaman.FleeSeconds,
                spec.Shaman.FleeSpeedMultiplier),
            Is.EqualTo((11f, 4f, 1.2f, 1.4f)));
        Assert.That(spec.Mend, Is.Not.Null);
        MendSpec mend = spec.Mend;
        Assert.That((mend.Range, mend.BreakRange, mend.HurtBelow, mend.ChannelSeconds),
            Is.EqualTo((10f, 12f, 0.9f, 3f)));
        Assert.That((mend.HealPerSecond, mend.Cooldown, mend.BrokenSeconds), Is.EqualTo((0.08f, 3f, 2f)));
    }

    [Test]
    public void Priest_ExistingEnemies_NoMend()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemySpec spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();
            if (spec.Id != Priest)
            {
                Assert.That(spec.Mend, Is.Null, path);
            }
        }
    }

    [Test]
    public void Priest_HoldsInsideTheStormsClearing()
    {
        EnemySpec spec = Catalog().Enemy(Priest);
        var storm = Arena(DunesPath).GetComponentInChildren<SunscarSandstormView>(true);
        Assert.That(spec.Shaman.HoldDistance, Is.LessThan(storm.ClearRadius));
    }

    [Test]
    public void Priest_NameKey_InEveryLanguage()
    {
        LocKey key = Catalog().Enemy(Priest).NameKey;
        var english = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset");
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset");
        Assert.That(english.ToDictionary()[key], Is.EqualTo("Jackal Priest"));
        Assert.That(pseudo.ToDictionary()[key], Is.Not.Empty);
    }

    [Test]
    public void Cobra_Asset_MatchesDesign()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.Id == Cobra.Value);
        Assert.That(definition, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "Data/Enemies/Cobra.asset")));
        EnemySpec spec = Catalog().Enemy(Cobra);
        Assert.That(spec.NameKey.Key, Is.EqualTo("enemy.cobra.name"));
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((44f, 2.4f, 4, 10, 28f)));
        Assert.That(spec.IsLight, Is.True);
        Assert.That(spec.IsElite, Is.False);
        Assert.That(spec.AggroRange, Is.EqualTo(30f));
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Cobra));
        EnemyLook look = definition.ToLook();
        Assert.That(look.Body, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/Cobra.prefab").GetComponent<EnemyView>()));
        Assert.That(look.BodyScale, Is.EqualTo(1f));
        Assert.That(spec.SpitCone, Is.Not.Null);
        SpitConeSpec spit = spec.SpitCone;
        Assert.That((spit.HoldDistance, spit.CloseDistance, spit.RearSeconds,
                spit.HalfAngleDegrees, spit.Length, spit.SpitDamage),
            Is.EqualTo((8f, 4f, 0.7f, 30f, 5f, 8f)));
        Assert.That((spit.PoisonFractionPerSecond, spit.PoisonSeconds, spit.PoisonMaxStacks,
                spit.RecoverSeconds, spit.CooldownSeconds),
            Is.EqualTo((0.02f, 4f, 3, 0.5f, 3f)));
    }

    [Test]
    public void Cobra_ExistingEnemies_NoSpitCone()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemySpec spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();
            if (spec.Id != Cobra)
            {
                Assert.That(spec.SpitCone, Is.Null, path);
            }
        }
    }

    [Test]
    public void Cobra_SlowerThanEveryClass()
    {
        EnemySpec spec = Catalog().Enemy(Cobra);
        foreach (CharacterSpec character in AuthoredCharacters())
        {
            Assert.That(spec.MoveSpeed, Is.LessThan(character.Movement.Speed), character.Id.Value);
        }
    }

    [Test]
    public void Cobra_ConeTipClearableInRear()
    {
        SpitConeSpec spit = Catalog().Enemy(Cobra).SpitCone;
        foreach (CharacterSpec character in AuthoredCharacters())
        {
            Assert.That(character.Movement.Speed * spit.RearSeconds,
                Is.GreaterThanOrEqualTo(spit.Length - spit.CloseDistance), character.Id.Value);
        }
    }

    [Test]
    public void Cobra_HoldsInsideTheStormsClearing()
    {
        var storm = Arena(DunesPath).GetComponentInChildren<SunscarSandstormView>(true);
        Assert.That(Catalog().Enemy(Cobra).SpitCone.HoldDistance, Is.LessThan(storm.ClearRadius));
    }

    [Test]
    public void Cobra_NameKey_InEveryLanguage()
    {
        LocKey key = Catalog().Enemy(Cobra).NameKey;
        var english = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset");
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset");
        Assert.That(english.ToDictionary()[key], Is.EqualTo("Spitting cobra"));
        Assert.That(pseudo.ToDictionary()[key], Is.Not.Empty);
    }

    [Test]
    public void Caller_Asset_MatchesDesign()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.Id == Caller.Value);
        Assert.That(definition, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "Data/Enemies/StormCaller.asset")));
        EnemySpec spec = Catalog().Enemy(Caller);
        Assert.That(spec.NameKey.Key, Is.EqualTo("enemy.storm-caller.name"));
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((55f, 2f, 7, 15, 31f)));
        Assert.That(spec.IsLight, Is.False);
        Assert.That(spec.IsElite, Is.False);
        Assert.That(spec.ContactDamage, Is.Zero);
        Assert.That(spec.AggroRange, Is.EqualTo(30f));
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.StormCaller));
        EnemyLook look = definition.ToLook();
        Assert.That(look.Body, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/StormCaller.prefab").GetComponent<EnemyView>()));
        Assert.That(look.BodyScale, Is.EqualTo(1f));
        Assert.That(spec.StormCall, Is.Not.Null);
        StormCallSpec stormCall = spec.StormCall;
        Assert.That((stormCall.HoldDistance, stormCall.FleeDistance, stormCall.FleeSeconds, stormCall.FleeSpeedMultiplier),
            Is.EqualTo((12f, 6f, 1.2f, 1.4f)));
        Assert.That((stormCall.ChannelReach, stormCall.ChannelSeconds, stormCall.BrokenSeconds, stormCall.RechannelSeconds),
            Is.EqualTo((16f, 2f, 3f, 6f)));
        Assert.That((stormCall.RiseAhead, stormCall.DriftSpeed, stormCall.TurnDegreesPerSecond, stormCall.TornadoSeconds),
            Is.EqualTo((2f, 1.6f, 60f, 8f)));
        Assert.That((stormCall.Radius, stormCall.DamagePerSecond, stormCall.PullRadius, stormCall.PullSpeed,
                stormCall.CollapseSeconds), Is.EqualTo((1.5f, 10f, 3f, 1f, 0.4f)));
    }

    [Test]
    public void Caller_ExistingEnemies_NoStormCall()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemySpec spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();
            if (spec.Id != Caller)
            {
                Assert.That(spec.StormCall, Is.Null, path);
            }
        }
    }

    [Test]
    public void Caller_DriftIsHalfTheRanger()
    {
        ContentCatalog catalog = Catalog();
        Assert.That(catalog.Enemy(Caller).StormCall.DriftSpeed,
            Is.EqualTo(catalog.Character(Ranger).Movement.Speed * 0.5f));
    }

    [Test]
    public void Caller_PullWeakerThanEveryClass()
    {
        StormCallSpec stormCall = Catalog().Enemy(Caller).StormCall;
        foreach (CharacterSpec character in AuthoredCharacters())
        {
            Assert.That(stormCall.PullSpeed, Is.LessThan(character.Movement.Speed * 0.5f), character.Id.Value);
        }
    }

    [Test]
    public void Caller_DriftSlowerThanEveryClass()
    {
        StormCallSpec stormCall = Catalog().Enemy(Caller).StormCall;
        foreach (CharacterSpec character in AuthoredCharacters())
        {
            Assert.That(stormCall.DriftSpeed, Is.LessThan(character.Movement.Speed), character.Id.Value);
        }
    }

    [Test]
    public void Caller_HoldsInsideTheStormsClearing()
    {
        var storm = Arena(DunesPath).GetComponentInChildren<SunscarSandstormView>(true);
        StormCallSpec stormCall = Catalog().Enemy(Caller).StormCall;
        Assert.That(stormCall.HoldDistance, Is.LessThan(storm.ClearRadius));
        Assert.That(stormCall.FleeDistance, Is.LessThan(stormCall.HoldDistance));
    }

    [Test]
    public void Caller_TwoSecondStaySurvivable()
    {
        ModeSpec mode = Mode("Sunscar");
        ContentCatalog catalog = Catalog(mode);
        float damage = 2f * catalog.Enemy(Caller).StormCall.DamagePerSecond * mode.Scaling.Damage.At(18);
        Assert.That(damage, Is.LessThan(catalog.Character(Ranger).MaxHp));
    }

    [Test]
    public void Caller_NameKey_InEveryLanguage()
    {
        LocKey key = Catalog().Enemy(Caller).NameKey;
        var english = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset");
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset");
        Assert.That(english.ToDictionary()[key], Is.EqualTo("Storm caller"));
        Assert.That(pseudo.ToDictionary()[key], Is.Not.Empty);
    }

    [Test]
    public void Vulture_Asset_MatchesDesign()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.Id == Vulture.Value);
        Assert.That(definition, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "Data/Enemies/Vulture.asset")));
        EnemySpec spec = Catalog().Enemy(Vulture);
        Assert.That(spec.NameKey.Key, Is.EqualTo("enemy.vulture.name"));
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((50f, 4f, 6, 14, 31f)));
        Assert.That(spec.IsLight, Is.True);
        Assert.That(spec.IsElite, Is.False);
        Assert.That(spec.ContactDamage, Is.Zero);
        Assert.That(spec.AggroRange, Is.EqualTo(30f));
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Vulture));
        EnemyLook look = definition.ToLook();
        Assert.That(look.Body, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/Vulture.prefab").GetComponent<EnemyView>()));
        Assert.That(look.BodyScale, Is.EqualTo(1f));
        Assert.That(spec.Dive, Is.Not.Null);
        DiveSpec dive = spec.Dive;
        Assert.That((dive.CircleRadius, dive.CircleSpeed, dive.ApproachSpeed), Is.EqualTo((7f, 4f, 6f)));
        Assert.That((dive.MinIntervalSeconds, dive.MaxIntervalSeconds, dive.LockSeconds), Is.EqualTo((4f, 6f, 1f)));
        Assert.That((dive.LineLength, dive.LineHalfWidth, dive.DiveSeconds, dive.DiveDamage),
            Is.EqualTo((10f, 0.6f, 0.4f, 14f)));
        Assert.That((dive.GroundedSeconds, dive.ClimbSeconds), Is.EqualTo((2f, 0.6f)));
    }

    [Test]
    public void Vulture_ExistingEnemies_NoDive()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemySpec spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();
            if (spec.Id != Vulture)
            {
                Assert.That(spec.Dive, Is.Null, path);
            }
        }
    }

    [Test]
    public void Vulture_LineSidestepInLock()
    {
        DiveSpec dive = Catalog().Enemy(Vulture).Dive;
        foreach (CharacterSpec character in AuthoredCharacters())
        {
            Assert.That(character.Movement.Speed * dive.LockSeconds,
                Is.GreaterThanOrEqualTo(2f * dive.LineHalfWidth), character.Id.Value);
        }
    }

    [Test]
    public void Vulture_LineOverrunsTheHero()
    {
        DiveSpec dive = Catalog().Enemy(Vulture).Dive;
        Assert.That(dive.LineLength, Is.GreaterThan(dive.CircleRadius));
    }

    [Test]
    public void Vulture_CirclesInsideTheStormsClearing()
    {
        var storm = Arena(DunesPath).GetComponentInChildren<SunscarSandstormView>(true);
        Assert.That(Catalog().Enemy(Vulture).Dive.CircleRadius, Is.LessThan(storm.ClearRadius));
    }

    [Test]
    public void Vulture_NameKey_InEveryLanguage()
    {
        LocKey key = Catalog().Enemy(Vulture).NameKey;
        var english = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset");
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset");
        Assert.That(english.ToDictionary()[key], Is.EqualTo("Carrion vulture"));
        Assert.That(pseudo.ToDictionary()[key], Is.Not.Empty);
    }

    [Test]
    public void Sunscar_RosterByStage_VultureFrom22()
    {
        ModeSpec mode = Mode("Sunscar");
        var roster = new RosterEntry[mode.Roster.Count];
        int count = mode.RosterFor(21, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId),
            Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper, Jackal, Glassback, Antlion, Priest, Cobra, Caller }));
        count = mode.RosterFor(22, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId),
            Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper, Jackal, Glassback, Antlion, Priest, Cobra, Caller, Vulture }));
    }

    [Test]
    public void Sunscar_RosterByStage_CallerFrom18()
    {
        ModeSpec mode = Mode("Sunscar");
        var roster = new RosterEntry[mode.Roster.Count];
        int count = mode.RosterFor(17, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId),
            Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper, Jackal, Glassback, Antlion, Priest, Cobra }));
        count = mode.RosterFor(18, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId),
            Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper, Jackal, Glassback, Antlion, Priest, Cobra, Caller }));
    }

    [Test]
    public void Sunscar_RosterByStage_CobraFrom16()
    {
        ModeSpec mode = Mode("Sunscar");
        var roster = new RosterEntry[mode.Roster.Count];
        int count = mode.RosterFor(15, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId),
            Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper, Jackal, Glassback, Antlion, Priest }));
        count = mode.RosterFor(16, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId),
            Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper, Jackal, Glassback, Antlion, Priest, Cobra }));
    }

    [Test]
    public void Sunscar_RosterByStage_PriestFrom14()
    {
        ModeSpec mode = Mode("Sunscar");
        var roster = new RosterEntry[mode.Roster.Count];
        int count = mode.RosterFor(13, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId),
            Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper, Jackal, Glassback, Antlion }));
        count = mode.RosterFor(14, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId),
            Is.EqualTo(new[] { Scorpion, Beetle, Brood, Viper, Jackal, Glassback, Antlion, Priest }));
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
        AssertStageWavesClear(6, new[] { Scorpion, Beetle, Viper },
            maxSeconds: 240, sidestepTells: true, dodgeLandings: true);

    [Test]
    public void Sunscar_Stage8_WavesClear() =>
        AssertStageWavesClear(8, new[] { Scorpion, Beetle, Viper, Jackal },
            maxSeconds: 240, sidestepTells: true, dodgeLandings: true);

    [Test]
    public void Sunscar_Stage9_WavesClear() =>
        AssertStageWavesClear(9, new[] { Scorpion, Beetle, Viper, Jackal, Glassback },
            maxSeconds: 240, sidestepTells: true, dodgeLandings: true);

    [Test]
    public void Sunscar_Stage11_WavesClear() =>
        AssertStageWavesClear(11, new[] { Scorpion, Beetle, Viper, Jackal, Antlion },
            maxSeconds: 240, sidestepTells: true, dodgeLandings: true, seed: 11);

    [Test]
    public void Sunscar_Stage14_WavesClearAndLeadToHierophant() =>
        AssertStageWavesClear(14, new[] { Scorpion, Beetle, Viper, Jackal, Glassback, Antlion, Priest },
            maxSeconds: 240, sidestepTells: true, dodgeLandings: true, seed: 1);

    [Test]
    public void Sunscar_Stage16_WavesClear()
    {
        RecordingEvents events = AssertStageWavesClear(16,
            new[] { Scorpion, Beetle, Viper, Jackal, Glassback, Antlion, Priest, Cobra },
            maxSeconds: 240, sidestepTells: true, dodgeLandings: true, seed: 1, keepHeroAlive: true);
        Assert.That(events.Of<EnemySpawned>().Select(spawn => spawn.SpecId), Does.Contain(Cobra));
    }

    [Test]
    public void Sunscar_Stage18_WavesClear()
    {
        RecordingEvents events = AssertStageWavesClear(18,
            new[] { Scorpion, Beetle, Viper, Jackal, Glassback, Antlion, Priest, Cobra, Caller },
            maxSeconds: 240, sidestepTells: true, dodgeLandings: true, seed: 2, keepHeroAlive: true);
        Assert.That(events.Of<EnemySpawned>().Select(spawn => spawn.SpecId), Does.Contain(Caller));
    }

    [Test]
    public void Sunscar_Stage22_WavesClear()
    {
        RecordingEvents events = AssertStageWavesClear(22,
            new[] { Scorpion, Beetle, Viper, Jackal, Glassback, Antlion, Priest, Cobra, Caller, Vulture },
            maxSeconds: 300, sidestepTells: true, dodgeLandings: true, seed: 2, keepHeroAlive: true,
            spendRangerPoints: true);
        Assert.That(events.Of<EnemySpawned>().Select(spawn => spawn.SpecId), Does.Contain(Vulture));
    }

    private static RecordingEvents AssertStageWavesClear(int stage, params ContentId[] expectedEnemies) =>
        AssertStageWavesClear(stage, expectedEnemies, maxSeconds: 180);

    private static RecordingEvents AssertStageWavesClear(
        int stage, ContentId[] expectedEnemies, int maxSeconds,
        bool sidestepTells = false, bool dodgeLandings = false, int seed = 7, bool keepHeroAlive = false,
        bool spendRangerPoints = false)
    {
        const float frameTime = 1f / 32f;
        ModeSpec mode = Mode("Sunscar");
        ContentCatalog catalog = Catalog(mode);
        if (spendRangerPoints)
        {
            SkillSpec[] skills = AssetDatabase.FindAssets("t:SkillDefinition", new[] { Root + "Data/Skills/Ranger" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<SkillDefinition>(AssetDatabase.GUIDToAssetPath(guid)).ToSpec())
                .ToArray();
            var tree = AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(Root + "Data/Trees/Ranger.asset");
            catalog = new ContentCatalog(catalog.Characters, catalog.Enemies, new[] { mode }, skills, new[] { tree.ToSpec() }, catalog.Bosses);
        }
        var random = new SeededRandom(seed);
        var events = new RecordingEvents();
        var intents = new RecordingIntents();
        var recorder = new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, intents, recorder, 64, 28, 64);
        RunSnapshot snapshot = new TestStart(catalog).At(Sunscar, Ranger, random.Seed, random.Capture(), stage);

        try
        {
            session.Start(new RunConfig(Sunscar, Ranger, random.Seed, stage, SpawnPlan.Empty, snapshot));
            if (spendRangerPoints)
            {
                session.TakeNode(new ContentId("skill.ranger.quick-draw"));
                session.TakeNode(new ContentId("skill.ranger.quick-draw-2"));
                session.TakeNode(new ContentId("skill.ranger.quick-draw-3"));
                var volley = new ContentId("skill.ranger.volley");
                session.TakeNode(volley);
                session.TakeNode(new ContentId("skill.ranger.volley-2"));
                session.TakeNode(new ContentId("skill.ranger.volley-3"));
                session.SetAutoCast(volley, true);
            }

            Health heroHealth = null;
            if (keepHeroAlive)
            {
                // This row verifies wave completion. Refill the simulated Ranger between ticks.
                var combat = (PlayerCombat)typeof(RunState)
                    .GetProperty("Combat", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(session.State);
                heroHealth = combat.Health;
            }

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
            NumericsVector3? gatePosition = stage == 14 ? Arena(DunesPath).GatePosition.ToNum() : null;
            int tellsRead = 0;
            int divesRead = 0;
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
                world.HasGate = gatePosition.HasValue;
                world.GatePosition = gatePosition.GetValueOrDefault();
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

                if (sidestepTells)
                {
                    IReadOnlyList<VultureDiveLocked> dives = events.Of<VultureDiveLocked>();
                    for (; divesRead < dives.Count; divesRead++)
                    {
                        var direction = dives[divesRead].Direction;
                        var sideways = new NumericsVector3(-direction.Y, 0f, direction.X);
                        player += (NumericsVector3.Dot(sideways, player) > 0f ? -sideways : sideways) * 3f;
                    }

                    // Keep the Ranger within aggro range of the four spawn points.
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

                heroHealth?.Heal(session.State.PlayerMaxHp);
                session.Tick(world);
                if (events.Count<PlayerDied>() > 0)
                {
                    Assert.Fail($"The Ranger died at {player} after {frame * frameTime:F1}s on stage {stage}.");
                }
            }

            Assert.That(mode.TryGetScriptedStage(stage, out ScriptedStage script), Is.True);
            Assert.That(events.Count<WaveStarted>(), Is.EqualTo(script.WaveCount));
            IEnumerable<ContentId> spawnedEnemies = events.Of<EnemySpawned>().Select(e => e.SpecId).Distinct();
            Assert.That(spawnedEnemies, Is.EquivalentTo(expectedEnemies));
            var survivors = events.Of<EnemySpawned>()
                .Where(spawn => session.State.IsEnemyAlive(spawn.Id))
                .Select(spawn => $"{spawn.SpecId} at {positions.GetValueOrDefault(spawn.Id, spawn.Position)}");
            Assert.That(events.Count<StageCleared>(), Is.EqualTo(1),
                $"Stage {stage} after {maxSeconds}s, player at {player}, survivors: {string.Join(", ", survivors)}");
            if (stage == 14)
            {
                // Take the progression path from Stage 14, without a Stage 15 restore.
                NumericsVector3 gate = events.Single<StageCleared>().GatePosition;
                int before = events.Count<EnemySpawned>();
                for (int frame = 0; frame < 15f / frameTime && events.Count<EnemySpawned>() == before; frame++)
                {
                    world.Clear();
                    world.Dt = frameTime;
                    world.PlayerPosition = session.State.StageIndex == 14 ? gate : player;
                    world.HasGate = true;
                    world.GatePosition = gate;
                    world.SpawnPoints = points;
                    session.Tick(world);
                }
                Assert.That(events.Of<StageArrived>().Select(e => (e.Stage, e.ArenaId)),
                    Is.EqualTo(new[] { (14, Dunes), (15, Dunes) }));
                Assert.That(events.Of<EnemySpawned>().Skip(before).Select(e => e.SpecId),
                    Is.EqualTo(new[] { new ContentId("enemy.hierophant") }));
            }
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
    public void BuriedKing_UsesTheRegisteredPlaceholderAndOwnsHisPhaseChanges()
    {
        ContentCatalog catalog = Catalog(Mode("Sunscar"));
        BossSpec boss = catalog.Boss(BuriedKing);
        Assert.That((boss.EnemySpecId, boss.SpawnsCentral, boss.UsesAuthoredMaxHp),
            Is.EqualTo((BuriedKingBehaviour.PrototypeBodyId, true, true)));
        Assert.That(boss.Phases, Has.Count.EqualTo(1));
        EnemyDefinition body = BootEnemies().Single(enemy => enemy.ToSpec().Id == boss.EnemySpecId);
        Assert.That(body.ToLook().Body, Is.Null);
        Assert.That(body.ToSpec().ContactDamage, Is.Zero);
        LocKey name = body.ToSpec().NameKey;
        Assert.That(AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset")
            .ToDictionary()[name], Is.EqualTo("Buried King"));
        Assert.That(AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset")
            .ToDictionary()[name], Is.Not.Empty);
    }

    [TestCase(29)]
    [TestCase(30)]
    public void Stage30_EitherEntrySpawnsOnlyTheBuriedKingAndFinishesSunscarOnce(int startStage)
    {
        ModeSpec mode = Mode("Sunscar");
        ContentCatalog authored = Catalog(mode);
        CharacterDefinition ranger = Object.Instantiate(
            AssetDatabase.LoadAssetAtPath<CharacterDefinition>(Root + "Data/Characters/Ranger.asset"));
        try
        {
            // Fast kills keep this a stage-flow test. Damage and health here are fixture values.
            var settings = new SerializedObject(ranger);
            settings.FindProperty("_maxHp").floatValue = 100000f;
            settings.FindProperty("_weaponDamage").floatValue = 10000f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var catalog = new ContentCatalog(new[] { ranger.ToSpec() }, authored.Enemies,
                new[] { mode }, bosses: authored.Bosses);
            var random = new SeededRandom(12);
            var events = new RecordingEvents();
            var recorder = new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), events);
            var session = new RunSession(catalog, random, events, new RecordingIntents(), recorder, 64, 28, 64);
            RunSnapshot restore = new TestStart(catalog).At(Sunscar, Ranger, random.Seed, random.Capture(), startStage);
            session.Start(new RunConfig(Sunscar, Ranger, random.Seed, startStage, SpawnPlan.Empty, restore));
            if (startStage == 30)
            {
                Assert.That(session.State.Level, Is.EqualTo(30));
            }

            ArenaView arena = Arena(DunesPath);
            NumericsVector3 start = arena.PlayerStart.ToNum();
            NumericsVector3 gate = arena.GatePosition.ToNum();
            NumericsVector3[] points = arena.SpawnPoints.Select(point => point.ToNum()).ToArray();
            var world = new WorldSnapshot(64);
            bool sawLivingKing = false;
            int waveSpawns = 0;
            int waveTelegraphs = 0;

            // Walk the Stage 29 gate through normal stage flow. No Stage 30 restore on this path.
            for (int frame = 0; frame < 32 * 160 && session.IsRunning; frame++)
            {
                world.Clear();
                world.Dt = 1f / 32f;
                world.SpawnPoints = points;
                world.HasGate = true;
                world.GatePosition = gate;
                world.PlayerPosition = session.State.StageIndex == 29 &&
                    events.Count<StageCleared>() > 0 ? gate : start;

                foreach (EnemySpawned spawn in events.Of<EnemySpawned>())
                {
                    if (session.State.IsEnemyAlive(spawn.Id))
                    {
                        world.AddEnemy() = new EnemySense
                        {
                            Id = spawn.Id,
                            Position = start + (3f * NumericsVector3.UnitZ),
                            HasLineOfSight = true,
                        };
                    }
                }

                if (session.State.StageIndex == 29)
                {
                    waveSpawns = events.Count<EnemySpawned>();
                    waveTelegraphs = events.Count<SpawnTelegraphed>();
                }
                else if (!sawLivingKing && world.EnemyCount > 0)
                {
                    sawLivingKing = true;
                    Assert.That(events.Count<ModeFinished>(), Is.Zero);
                    Assert.That(events.Of<StageCleared>().Any(clear => clear.Stage == 30), Is.False);
                }

                session.Tick(world);
            }

            Assert.That(session.IsRunning, Is.False);
            session.End();
            Assert.That(sawLivingKing, Is.True);
            Assert.That(events.Of<StageArrived>().Select(arrival => (arrival.Stage, arrival.ArenaId)),
                Is.EqualTo(Enumerable.Range(startStage, 31 - startStage).Select(stage => (stage, Dunes))));
            WaveStarted duel = events.Of<WaveStarted>().Single(wave => wave.Stage == 30);
            Assert.That((duel.Wave, duel.WaveCount), Is.EqualTo((1, 1)));
            Assert.That(events.Count<SpawnTelegraphed>(), Is.EqualTo(waveTelegraphs));
            EnemySpawned king = events.Of<EnemySpawned>().Skip(waveSpawns).Single();
            Assert.That((king.SpecId, king.Position),
                Is.EqualTo((BuriedKingBehaviour.PrototypeBodyId, new NumericsVector3(0f, 0f, 2f))));
            Assert.That(events.Of<EnemyDied>().Count(death => death.Id == king.Id && death.SpecId == king.SpecId),
                Is.EqualTo(1));
            StageCleared clear = events.Of<StageCleared>().Single(row => row.Stage == 30);
            Assert.That(clear.NextArenaId, Is.EqualTo(default(ContentId)));
            ModeFinished finish = events.Single<ModeFinished>();
            Assert.That((finish.ModeId, finish.FinalStage), Is.EqualTo((Sunscar, 30)));
            Assert.That(events.Count<RunEnded>(), Is.EqualTo(1));
            Assert.That(events.Count<PlayerDied>(), Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(ranger);
        }
    }

    [TestCase(1, 0)]
    [TestCase(-1, 0)]
    [TestCase(0, 1)]
    [TestCase(0, -1)]
    public void BuriedKing_CastAndRecallStayOnWalkableGroundNearEachDunesEdge(int x, int z)
    {
        var offset = new Vector3(2000f, 0f, 2000f);
        GameObject room = Object.Instantiate(Arena(DunesPath).gameObject, offset, Quaternion.identity);
        try
        {
            var arena = room.GetComponent<ArenaView>();
            arena.Surface.AddData();
            var ground = new OpenGroundSense(LayerMask.GetMask("Cover"), null);
            ground.Sense(arena);
            Vector3 rim = offset + (16f * new Vector3(x, 0f, z));
            Vector3 tangent = new Vector3(-z, 0f, x);
            Vector3 hero = rim + (4f * tangent);
            Vector3 boss = rim - (4f * tangent);
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(offset, boss, NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
            Assert.That(NavMesh.Raycast(boss, hero, out _, NavMesh.AllAreas), Is.False);

            ContentCatalog catalog = Catalog(Mode("Sunscar"));
            var random = new SeededRandom(12);
            var events = new RecordingEvents();
            var intents = new RecordingIntents();
            var recorder = new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), events);
            var session = new RunSession(catalog, random, events, intents, recorder, 64, 28, 64);
            session.Start(new RunConfig(Sunscar, Ranger, random.Seed, 30, SpawnPlan.Empty, restore: null));
            const float frameTime = 1f / 60f;
            var world = new WorldSnapshot(64)
            {
                Dt = frameTime,
                PlayerPosition = arena.PlayerStart.ToNum(),
                SpawnPoints = arena.SpawnPoints.Select(point => point.ToNum()).ToArray(),
            };
            for (int frame = 0; frame < 240 && events.Count<EnemySpawned>() == 0; frame++)
            {
                session.Tick(world);
                intents.Clear();
            }

            EnemySpawned king = events.Single<EnemySpawned>();
            for (int frame = 0; frame < 360; frame++)
            {
                world.EnemyCount = 0;
                world.PlayerPosition = hero.ToNum();
                world.OpenGround = ground.Spots;
                world.AddEnemy() = new EnemySense
                {
                    Id = king.Id,
                    Position = boss.ToNum(),
                    HasLineOfSight = true,
                };
                session.Tick(world);
                if (events.Of<BuriedKingStateChanged>().Any(state => state.Pose == BuriedKingPose.RecallTell))
                {
                    break;
                }

                foreach (var move in intents.EnemyMoves)
                {
                    boss += frameTime * move.Velocity.ToUnity();
                }

                intents.Clear();
            }

            BuriedKingStateChanged cast = events.Of<BuriedKingStateChanged>()
                .Single(state => state.Pose == BuriedKingPose.CastTell);
            BuriedKingStateChanged recall = events.Of<BuriedKingStateChanged>()
                .Single(state => state.Pose == BuriedKingPose.RecallTell);
            Assert.That(NumericsVector3.Distance(cast.End, hero.ToNum()), Is.LessThanOrEqualTo(OpenGroundSense.Step));
            Assert.That(recall.Start, Is.EqualTo(cast.End));
            Assert.That(recall.End, Is.EqualTo(boss.ToNum()));
            Assert.That(ground.Spots, Does.Contain(cast.End));
            Assert.That(NavMesh.Raycast(cast.Start.ToUnity(), cast.End.ToUnity(), out _, NavMesh.AllAreas), Is.False);
            Assert.That(NavMesh.Raycast(recall.Start.ToUnity(), recall.End.ToUnity(), out _, NavMesh.AllAreas), Is.False);
            session.End();
        }
        finally
        {
            Object.DestroyImmediate(room);
        }
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
            typeof(TestStartPanel).GetField("_stage", flags).SetValue(panel, 30);
            typeof(TestStartPanel).GetMethod("CyclePlace", flags).Invoke(panel, new object[] { 1 });
            typeof(TestStartPanel).GetMethod("Begin", flags).Invoke(panel, null);

            Assert.That(loader.Loaded, Is.EqualTo(SceneLoader.Run));
            Assert.That(pending.Snapshot.HasValue, Is.True);
            Assert.That(pending.ModeId, Is.EqualTo(Sunscar));
            Assert.That(pending.CharacterId, Is.EqualTo(Ranger));
            Assert.That(pending.Snapshot.Value.StageIndex, Is.EqualTo(30));
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
            BootEnemies().Select(enemy => enemy.ToSpec()).ToArray(), modes: modes,
            bosses: BootDefinitions<BossDefinition>("_bosses").Select(boss => boss.ToSpec()).ToArray());

    private static EnemyDefinition[] BootEnemies() => BootDefinitions<EnemyDefinition>("_enemies");

    private static T[] BootDefinitions<T>(string field) where T : ScriptableObject
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Composition/BootScope.prefab");
        var serialized = new SerializedObject(prefab.GetComponent<BootScope>());
        SerializedProperty rows = serialized.FindProperty(field);
        var definitions = new T[rows.arraySize];
        for (int i = 0; i < definitions.Length; i++)
        {
            definitions[i] = (T)rows.GetArrayElementAtIndex(i).objectReferenceValue;
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
