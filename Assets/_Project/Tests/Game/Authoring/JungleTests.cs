using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// The Jungle as a place a run is played in (M7-05i): its own Husk, Descent's numbers, a small room
/// for its first stages, and the first mode a new run starts. Its Husk is the Frog since M7-05m, its
/// Spitter the Pitcher since M7-05q, and its Bloater the Puffball since JG-03c.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Jungle's Husk is its own archetype with the Husk's numbers</b>, not the Husk in a skin:
/// identity is a <see cref="ContentId"/>, a look is keyed by it, and a place's enemies are its mode's
/// roster, so the Jungle's Husk is one roster row naming a different id. <see cref="JunglesHusk_PlaysTheHusksRole"/>
/// pins the numbers equal; they part when someone edits that row, which is the moment to decide they
/// should. The Rootling, which the Frog replaced, keeps its row: it is authored and could be rostered
/// again by one line. The Jungle's Spitter is the same ruling a second time:
/// <see cref="JunglesSpitter_PlaysTheSpittersRole"/> pins the Pitcher to the Spitter, which Descent
/// still rosters as a capsule, and <see cref="JunglesBloater_PlaysTheBloatersRole"/> pins the Puffball to
/// the Bloater, a third time.
/// </para>
/// <para>
/// <b>The Jungle is Descent with its own content.</b> <see cref="Jungle_IsDescentsNumbers"/> compares
/// every authored block but the four that make it the Jungle — id, name, roster and rooms — field by
/// field, so a retune of Descent that forgets the Jungle is a red row rather than two places drifting.
/// </para>
/// </remarks>
[TestFixture]
public sealed class JungleTests
{
    private const string JunglePath = "Assets/_Project/Data/Modes/Jungle.asset";
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";
    private const string RootlingPath = "Assets/_Project/Data/Enemies/Rootling.asset";
    private const string FrogPath = "Assets/_Project/Data/Enemies/Frog.asset";
    private const string HuskPath = "Assets/_Project/Data/Enemies/Husk.asset";
    private const string PitcherPath = "Assets/_Project/Data/Enemies/Pitcher.asset";
    private const string SpitterPath = "Assets/_Project/Data/Enemies/Spitter.asset";
    private const string PuffballPath = "Assets/_Project/Data/Enemies/Puffball.asset";
    private const string BloaterPath = "Assets/_Project/Data/Enemies/Bloater.asset";
    private const string PantherPath = "Assets/_Project/Data/Enemies/Panther.asset";
    private const string MossguardPath = "Assets/_Project/Data/Enemies/Mossguard.asset";
    private const string MossguardBodyPath = "Assets/_Project/Prefabs/Enemies/Mossguard.prefab";
    private const string CroakerPath = "Assets/_Project/Data/Enemies/Croaker.asset";
    private const string MotherPath = "Assets/_Project/Data/Enemies/MotherPitcher.asset";
    private const string MotherBossPath = "Assets/_Project/Data/Enemies/MotherPitcherBoss.asset";
    private const string BudPath = "Assets/_Project/Data/Enemies/PitcherBud.asset";
    private const string WardenPath = "Assets/_Project/Data/Enemies/Warden.asset";
    private const string RootlingBodyPath = "Assets/_Project/Prefabs/Enemies/Rootling.prefab";
    private const string FrogBodyPath = "Assets/_Project/Prefabs/Enemies/Frog.prefab";
    private const string PitcherBodyPath = "Assets/_Project/Prefabs/Enemies/Pitcher.prefab";
    private const string BudBodyPath = "Assets/_Project/Prefabs/Enemies/Pitcher_Bud.prefab";
    private const string PuffballBodyPath = "Assets/_Project/Prefabs/Enemies/Puffball.prefab";
    private const string PantherBodyPath = "Assets/_Project/Prefabs/Enemies/Panther.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";
    private const string PseudoPath = "Assets/_Project/Data/Localisation/Pseudo.asset";
    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";
    private const string ArenaFolder = "Assets/_Project/Prefabs/Arenas";

    /// <summary>
    /// The widest floor a place may open in: GD §7.2's 36 m square is the full room, and the owner's
    /// direction of 2026-09-27 is a smaller one for the first stages. 24 m holds the spawn clearance
    /// (M7-05e); 26 allows a room a little larger than the Hollow.
    /// </summary>
    private const float SmallRoomFloor = 26f;

    /// <summary>What a Frog is worth since JG-01: three of them reach level 2 (CH §5.2's 51.7).</summary>
    private const float FrogXp = 18f;

    /// <summary>
    /// The Frog's average speed since JG-05b, in m/s: slower than the Husk's 2, so a 0.8 s sit between
    /// hops keeps each leap at its old ~1.6 m.
    /// </summary>
    private const float FrogMoveSpeed = 1.25f;

    /// <summary>The Frog's sit between hops since JG-05b, in seconds (owner, 2026-10-26: hop less often).</summary>
    private const float FrogHopRestTime = 0.8f;

    /// <summary>The Frog's leap speed before JG-05b, in m/s: 2 × (0.5 + 0.3) / 0.5. JG-05b keeps it.</summary>
    private const float FrogLeapSpeed = 3.2f;

    /// <summary>The Puffball's blast since JG-05d, in metres; its spores are ~2.3 m out when they fade.</summary>
    private const float PuffballBlastRadius = 2.5f;

    /// <summary>The Bloater's blast, unchanged by JG-05d, in metres.</summary>
    private const float BloaterBlastRadius = 3f;

    /// <summary>The Pitcher's spit since JG-02, in m/s: two seconds across its 14 m standoff.</summary>
    private const float PitcherSpitSpeed = 7f;

    /// <summary>The Pitcher's reload since 2026-10-22, in seconds: with its 0.7 s wind-up, one throw every 3 s.</summary>
    private const float PitcherRecoverTime = 2.3f;

    private const string PitcherSpitPath = "Assets/_Project/Prefabs/Projectiles/PitcherSpit.prefab";
    private const string PitcherSplatPath = "Assets/_Project/Prefabs/VFX/VFX_PitcherSplat.prefab";

    /// <remarks>
    /// <c>_bossRoster</c> left the list in JG-04c: the Jungle's boss is Old Croaker, which
    /// <c>ContentValidationTests.Jungle_Stage5_IsTheCroaker</c> pins.
    /// </remarks>
    private static readonly string[] SharedBlocks =
    {
        "_startingStage", "_scaling", "_xp", "_overflow", "_essence",
        "_sanctum", "_ordeals",
    };

    [Test]
    public void OldCroaker_HitsForTwelve()
    {
        EnemySpec croaker = Load<EnemyDefinition>(CroakerPath).ToSpec();
        EnemySpec warden = Load<EnemyDefinition>(WardenPath).ToSpec();

        Assert.That(croaker.Id.Value, Is.EqualTo("enemy.croaker"));
        Assert.That(croaker.ContactDamage, Is.EqualTo(12f));
        Assert.That(warden.ContactDamage, Is.EqualTo(22f), "Descent's Warden keeps its authored hit.");
    }

    [Test]
    public void Frog_RestsBetweenHops_LeapUnchanged()
    {
        // JG-05b rule 1: a longer sit, not a longer jump. The leap's speed (and so its length, over the
        // same 0.5 s) is what the Frog had before.
        EnemySpec frog = Load<EnemyDefinition>(FrogPath).ToSpec();

        Assert.That(frog.HopTime, Is.EqualTo(0.5f).Within(1e-4f), "The Hop clip's leap (FrogBodyTests).");
        Assert.That(frog.HopRestTime, Is.EqualTo(FrogHopRestTime).Within(1e-4f));

        float leap = frog.MoveSpeed * (frog.HopTime + frog.HopRestTime) / frog.HopTime;

        Assert.That(leap, Is.EqualTo(FrogLeapSpeed).Within(0.1f), "Each hop is still ~1.6 m.");
    }

    [TestCase(FrogPath, "enemy.frog")]
    [TestCase(RootlingPath, "enemy.rootling")]
    public void JunglesHusk_PlaysTheHusksRole(string path, string id)
    {
        EnemySpec ours = Load<EnemyDefinition>(path).ToSpec();
        EnemySpec husk = Load<EnemyDefinition>(HuskPath).ToSpec();

        Assert.That(ours.Id.Value, Is.EqualTo(id));
        Assert.That(ours.NameKey.Key, Is.EqualTo(id + ".name"));

        Assert.That(ours.Behaviour, Is.EqualTo(husk.Behaviour), "It beelines and swings (GD §8.1).");
        Assert.That(ours.MaxHp, Is.EqualTo(husk.MaxHp));
        Assert.That(ours.MoveSpeed, Is.EqualTo(id == "enemy.frog" ? FrogMoveSpeed : husk.MoveSpeed),
            "The Frog's speed parted from the Husk's in JG-05b, so a longer sit keeps the same leap.");
        Assert.That(ours.TargetPriority, Is.EqualTo(husk.TargetPriority));
        Assert.That(ours.ThreatCost, Is.EqualTo(husk.ThreatCost));
        Assert.That(ours.XpValue, Is.EqualTo(id == "enemy.frog" ? FrogXp : husk.XpValue),
            "The Frog's XP parted from the Husk's in JG-01, so stage 1's three level the hero once.");
        Assert.That(ours.IsElite, Is.EqualTo(husk.IsElite));
        Assert.That(ours.ContactDamage, Is.EqualTo(husk.ContactDamage));
        Assert.That(ours.Reach, Is.EqualTo(husk.Reach));
        Assert.That(ours.WindupTime, Is.EqualTo(husk.WindupTime));
        Assert.That(ours.RecoverTime, Is.EqualTo(husk.RecoverTime));
        Assert.That(ours.AggroRange, Is.EqualTo(husk.AggroRange));
        Assert.That(ours.Projectile, Is.Null);
        Assert.That(ours.Explosion, Is.Null);
    }

    [Test]
    public void JunglesSpitter_PlaysTheSpittersRole()
    {
        EnemySpec ours = Load<EnemyDefinition>(PitcherPath).ToSpec();
        EnemySpec spitter = Load<EnemyDefinition>(SpitterPath).ToSpec();

        Assert.That(ours.Id.Value, Is.EqualTo("enemy.pitcher"));
        Assert.That(ours.NameKey.Key, Is.EqualTo("enemy.pitcher.name"));
        Assert.That(ours.Behaviour, Is.EqualTo(spitter.Behaviour), "It stops at range and throws (GD §8.1).");
        Assert.That(ours.MaxHp, Is.EqualTo(spitter.MaxHp));
        Assert.That(ours.MoveSpeed, Is.EqualTo(spitter.MoveSpeed));
        Assert.That(ours.TargetPriority, Is.EqualTo(spitter.TargetPriority));
        Assert.That(ours.ThreatCost, Is.EqualTo(spitter.ThreatCost));
        Assert.That(ours.XpValue, Is.EqualTo(spitter.XpValue));
        Assert.That(ours.IsElite, Is.EqualTo(spitter.IsElite));
        Assert.That(ours.ContactDamage, Is.EqualTo(spitter.ContactDamage));
        Assert.That(ours.Reach, Is.EqualTo(spitter.Reach));
        Assert.That(ours.WindupTime, Is.EqualTo(spitter.WindupTime), "The wind-up the Pitcher's spit is timed to (M7-05p).");
        Assert.That(ours.RecoverTime, Is.EqualTo(PitcherRecoverTime),
            "The owner, 2026-10-22: the Pitcher throws once every 3 s, slower than the Spitter.");
        Assert.That(ours.AggroRange, Is.EqualTo(spitter.AggroRange));
        Assert.That(ours.Projectile, Is.Not.Null);
        Assert.That(ours.Projectile.StandoffRange, Is.EqualTo(spitter.Projectile.StandoffRange));
        Assert.That(ours.Projectile.Speed, Is.EqualTo(PitcherSpitSpeed),
            "The Pitcher's spit parted from the Spitter's in JG-02: slower, so it can be seen and dodged.");
        Assert.That(ours.Projectile.Radius, Is.EqualTo(spitter.Projectile.Radius));
        Assert.That(ours.Explosion, Is.Null);
    }

    [Test]
    public void JunglesBloater_PlaysTheBloatersRole()
    {
        EnemySpec ours = Load<EnemyDefinition>(PuffballPath).ToSpec();
        EnemySpec bloater = Load<EnemyDefinition>(BloaterPath).ToSpec();

        Assert.That(ours.Id.Value, Is.EqualTo("enemy.puffball"));
        Assert.That(ours.NameKey.Key, Is.EqualTo("enemy.puffball.name"));
        Assert.That(ours.Behaviour, Is.EqualTo(bloater.Behaviour), "It waddles in and bursts (GD §8.1).");
        Assert.That(ours.MaxHp, Is.EqualTo(bloater.MaxHp));
        Assert.That(ours.MoveSpeed, Is.EqualTo(bloater.MoveSpeed));
        Assert.That(ours.TargetPriority, Is.EqualTo(bloater.TargetPriority));
        Assert.That(ours.ThreatCost, Is.EqualTo(bloater.ThreatCost));
        Assert.That(ours.XpValue, Is.EqualTo(bloater.XpValue));
        Assert.That(ours.IsElite, Is.EqualTo(bloater.IsElite));
        Assert.That(ours.ContactDamage, Is.EqualTo(bloater.ContactDamage));
        Assert.That(ours.Reach, Is.EqualTo(bloater.Reach));
        Assert.That(ours.WindupTime, Is.EqualTo(bloater.WindupTime), "The wind-up the Puffball's swell is timed to (JG-03b).");
        Assert.That(ours.RecoverTime, Is.EqualTo(bloater.RecoverTime));
        Assert.That(ours.AggroRange, Is.EqualTo(bloater.AggroRange));
        Assert.That(ours.Projectile, Is.Null);
        Assert.That(bloater.Projectile, Is.Null);
        Assert.That(ours.Explosion, Is.Not.Null);
        Assert.That(ours.Explosion.Radius, Is.EqualTo(PuffballBlastRadius),
            "The Puffball's blast parted from the Bloater's in JG-05d (owner, 2026-10-26: a bit less).");
        Assert.That(bloater.Explosion.Radius, Is.EqualTo(BloaterBlastRadius), "Descent's Bloater keeps its 3 m.");
    }

    [TestCase(FrogPath, FrogBodyPath)]
    [TestCase(RootlingPath, RootlingBodyPath)]
    [TestCase(PitcherPath, PitcherBodyPath)]
    [TestCase(PuffballPath, PuffballBodyPath)]
    [TestCase(PantherPath, PantherBodyPath)]
    public void JunglesEnemy_WearsItsOwnBody(string path, string bodyPath)
    {
        EnemyLook look = Load<EnemyDefinition>(path).ToLook();

        Assert.That(look.Body, Is.Not.Null);
        Assert.That(look.Body.gameObject, Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath)));
        Assert.That(look.Tint, Is.EqualTo(Color.white), "White, so the atlas reads as it was painted.");
        Assert.That(look.BodyScale, Is.EqualTo(1f));
    }

    [Test]
    public void Jungle_IsDescentsNumbers()
    {
        var jungle = new SerializedObject(Load<ModeDefinition>(JunglePath));
        var descent = new SerializedObject(Load<ModeDefinition>(DescentPath));

        foreach (string block in SharedBlocks)
        {
            Assert.That(Flatten(jungle.FindProperty(block)), Is.EqualTo(Flatten(descent.FindProperty(block))),
                $"The Jungle's {block} is not Descent's.");
        }
    }

    [Test]
    public void Jungle_RostersItsOwnEnemies()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.Id.Value, Is.EqualTo("mode.jungle"));
        Assert.That(jungle.Roster.Select(r => (r.SpecId.Value, r.IntroducedAtStage)), Is.EqualTo(new[]
        {
            ("enemy.frog", 1),
            ("enemy.pitcher", 2),
            ("enemy.puffball", 4),
            ("enemy.panther", 6),
            ("enemy.mossguard", 11),
        }), "The Jungle's own enemies, with the Panther after Old Croaker and the Mossguard after Mother Pitcher.");
    }

    [Test]
    public void Jungle_Panther_IsALungerWithItsNumbers()
    {
        // JG-07d rule 2: GD §8.1's Lunger where GD has numbers, JG-07a's table for the rest, XP the
        // next step up the Jungle's ladder.
        EnemySpec panther = Load<EnemyDefinition>(PantherPath).ToSpec();

        Assert.That(panther.Id.Value, Is.EqualTo("enemy.panther"));
        Assert.That(panther.NameKey.Key, Is.EqualTo("enemy.panther.name"));
        Assert.That(panther.Behaviour, Is.EqualTo(EnemyBehaviourKind.Lunger));
        Assert.That(panther.MaxHp, Is.EqualTo(44f));
        Assert.That(panther.ThreatCost, Is.EqualTo(9));
        Assert.That(panther.TargetPriority, Is.EqualTo(4));
        Assert.That(panther.WindupTime, Is.EqualTo(0.9f).Within(1e-5f), "The crouch the body holds (JG-07c).");
        Assert.That(panther.RecoverTime, Is.EqualTo(1f));
        Assert.That(panther.ContactDamage, Is.EqualTo(14f));
        Assert.That(panther.MoveSpeed, Is.EqualTo(2.4f).Within(1e-5f), "Its prowl.");
        Assert.That(panther.XpValue, Is.EqualTo(27f), "Frog 18, Pitcher 21, Puffball 24, Panther 27.");
        Assert.That(panther.Projectile, Is.Null);
        Assert.That(panther.Explosion, Is.Null);

        Assert.That(panther.Lunge, Is.Not.Null);
        Assert.That((panther.Lunge.Range, panther.Lunge.Length, panther.Lunge.Speed, panther.Lunge.HalfWidth, panther.Lunge.Cooldown),
            Is.EqualTo((9f, 12f, 24f, 0.6f, 2f)));

        Assert.That(Load<EnemyDefinition>(PantherPath).ToLook().Body.gameObject,
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(PantherBodyPath)));
    }

    [Test]
    public void Jungle_RostersThePantherFromStageSix()
    {
        // JG-07d rule 3: after the Puffball's 4, and the Jungle's alone.
        RosterEntry panther = Load<ModeDefinition>(JunglePath).ToSpec().Roster.Single(r => r.SpecId.Value == "enemy.panther");

        Assert.That(panther.IntroducedAtStage, Is.EqualTo(6));
        Assert.That(Load<ModeDefinition>(DescentPath).ToSpec().Roster.Select(r => r.SpecId.Value), Has.No.Member("enemy.panther"));
    }

    [Test]
    public void Jungle_StageSix_IsTwoPanthers()
    {
        // JG-07d rule 4: alone, as stage 4's Puffballs are (GD §8.2). Stages 1-4 are
        // Jungle_ScriptsItsFirstFourStages' rows; stage 5 is Old Croaker's.
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(6, out ScriptedStage sixth), Is.True);
        Assert.That(sixth.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)),
            Is.EqualTo(new[] { (1, "enemy.panther", 2) }), "Two Panthers and nothing else.");
    }

    [Test]
    public void Jungle_StageSeven_IsOnePantherAmongFrogs()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(7, out ScriptedStage seventh), Is.True);
        Assert.That(seventh.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.frog", 4),
            (2, "enemy.panther", 1),
            (2, "enemy.frog", 3),
        }));
    }

    [Test]
    public void Jungle_StageEight_IsPitchersThenPanthers()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(8, out ScriptedStage eighth), Is.True);
        Assert.That(eighth.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.pitcher", 2),
            (1, "enemy.frog", 3),
            (2, "enemy.panther", 2),
            (2, "enemy.frog", 3),
        }));
    }

    [Test]
    public void Jungle_StageNine_UsesAllFour()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(9, out ScriptedStage ninth), Is.True);
        Assert.That(ninth.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.frog", 3),
            (1, "enemy.puffball", 2),
            (2, "enemy.pitcher", 2),
            (2, "enemy.panther", 1),
            (3, "enemy.panther", 2),
            (3, "enemy.frog", 2),
        }));
    }

    [Test]
    public void Jungle_StagesSixToNine_ClimbInThreat()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        Dictionary<ContentId, int> costs = new[] { FrogPath, PitcherPath, PuffballPath, PantherPath }
            .Select(path => Load<EnemyDefinition>(path).ToSpec())
            .ToDictionary(enemy => enemy.Id, enemy => enemy.ThreatCost);
        var totals = new List<int>();

        for (int stage = 6; stage <= 9; stage++)
        {
            Assert.That(jungle.TryGetScriptedStage(stage, out ScriptedStage scripted), Is.True);
            totals.Add(scripted.Spawns.Sum(spawn => spawn.Count * costs[spawn.SpecId]));
        }

        Assert.That(totals, Is.EqualTo(new[] { 18, 37, 56, 77 }));
        Assert.That(totals, Is.Ordered.Ascending);
    }

    [Test]
    public void Jungle_StageTen_IsNotScripted()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(10, out _), Is.False, "Stage 10 is Mother Pitcher's boss stage.");
    }

    [Test]
    public void Jungle_Mossguard_IsABulwarkWithItsNumbers()
    {
        EnemyDefinition definition = Load<EnemyDefinition>(MossguardPath);
        EnemySpec mossguard = definition.ToSpec();

        Assert.That(mossguard.Id.Value, Is.EqualTo("enemy.mossguard"));
        Assert.That(mossguard.NameKey.Key, Is.EqualTo("enemy.mossguard.name"));
        Assert.That(mossguard.Behaviour, Is.EqualTo(EnemyBehaviourKind.Bulwark));
        Assert.That(mossguard.MaxHp, Is.EqualTo(90f));
        Assert.That(mossguard.ThreatCost, Is.EqualTo(14));
        Assert.That(mossguard.TargetPriority, Is.EqualTo(3));
        Assert.That(mossguard.MoveSpeed, Is.EqualTo(1.6f).Within(1e-5f));
        Assert.That(mossguard.Reach, Is.EqualTo(1.6f).Within(1e-5f));
        Assert.That(mossguard.WindupTime, Is.EqualTo(0.7f).Within(1e-5f));
        Assert.That(mossguard.RecoverTime, Is.EqualTo(1.5f));
        Assert.That(mossguard.ContactDamage, Is.EqualTo(16f));
        Assert.That(mossguard.XpValue, Is.EqualTo(30f));
        Assert.That(mossguard.Bulwark, Is.Not.Null);
        Assert.That((mossguard.Bulwark.ShieldHalfArcDegrees, mossguard.Bulwark.TurnRateDegrees, mossguard.Bulwark.WalkConeDegrees),
            Is.EqualTo((60f, 75f, 45f)));
        Assert.That(mossguard.Projectile, Is.Null);
        Assert.That(mossguard.Explosion, Is.Null);
        Assert.That(mossguard.Lunge, Is.Null);
        Assert.That(definition.ToLook().Body, Is.EqualTo(Load<GameObject>(MossguardBodyPath).GetComponent<EnemyView>()));
    }

    [Test]
    public void Jungle_RostersTheMossguardFromStageEleven()
    {
        RosterEntry mossguard = Load<ModeDefinition>(JunglePath).ToSpec().Roster.Single(r => r.SpecId.Value == "enemy.mossguard");

        Assert.That(mossguard.IntroducedAtStage, Is.EqualTo(11));
        Assert.That(Load<ModeDefinition>(DescentPath).ToSpec().Roster.Select(r => r.SpecId.Value), Has.No.Member("enemy.mossguard"));
    }

    [Test]
    public void Jungle_StageEleven_MeetsTheMossguard()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(11, out ScriptedStage eleventh), Is.True);
        Assert.That(eleventh.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.mossguard", 1),
            (2, "enemy.mossguard", 1),
            (2, "enemy.frog", 4),
        }));
        var costs = new Dictionary<ContentId, int>
        {
            [new ContentId("enemy.mossguard")] = Load<EnemyDefinition>(MossguardPath).ToSpec().ThreatCost,
            [new ContentId("enemy.frog")] = Load<EnemyDefinition>(FrogPath).ToSpec().ThreatCost,
        };
        Assert.That(eleventh.Spawns.Sum(s => s.Count * costs[s.SpecId]), Is.EqualTo(44));
    }

    [Test]
    public void Jungle_StageTwelve_IsShieldAndSpit()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(12, out ScriptedStage twelfth), Is.True);
        Assert.That(twelfth.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.mossguard", 1),
            (1, "enemy.pitcher", 2),
            (1, "enemy.frog", 3),
            (2, "enemy.mossguard", 1),
            (2, "enemy.panther", 2),
            (2, "enemy.frog", 1),
        }));
    }

    [Test]
    public void Jungle_StageThirteen_IsAPincer()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(13, out ScriptedStage thirteenth), Is.True);
        Assert.That(thirteenth.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.mossguard", 2),
            (1, "enemy.puffball", 2),
            (2, "enemy.pitcher", 2),
            (2, "enemy.panther", 2),
            (2, "enemy.frog", 3),
            (3, "enemy.mossguard", 1),
            (3, "enemy.puffball", 1),
        }));
    }

    [Test]
    public void Jungle_StageFourteen_UsesAllFive()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(14, out ScriptedStage fourteenth), Is.True);
        Assert.That(fourteenth.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.frog", 4),
            (1, "enemy.puffball", 2),
            (2, "enemy.mossguard", 2),
            (2, "enemy.pitcher", 2),
            (3, "enemy.mossguard", 2),
            (3, "enemy.panther", 2),
            (3, "enemy.frog", 2),
        }));
    }

    [Test]
    public void Jungle_StagesElevenToFourteen_ClimbInThreat()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        Dictionary<ContentId, int> costs = new[] { FrogPath, PitcherPath, PuffballPath, PantherPath, MossguardPath }
            .Select(path => Load<EnemyDefinition>(path).ToSpec())
            .ToDictionary(enemy => enemy.Id, enemy => enemy.ThreatCost);
        var totals = new List<int>();

        for (int stage = 11; stage <= 14; stage++)
        {
            Assert.That(jungle.TryGetScriptedStage(stage, out ScriptedStage scripted), Is.True);
            totals.Add(scripted.Spawns.Sum(spawn => spawn.Count * costs[spawn.SpecId]));
        }

        Assert.That(totals, Is.EqualTo(new[] { 44, 76, 110, 128 }));
        Assert.That(totals, Is.Ordered.Ascending);
    }

    [Test]
    public void Jungle_StagesFifteenAndSixteen_AreNotScripted()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(15, out _), Is.False, "Stage 15 is Old Croaker's.");
        Assert.That(jungle.TryGetScriptedStage(16, out _), Is.False, "Stage 16 uses the threat budget.");
    }

    [Test]
    public void Jungle_Mossguard_Boots()
    {
        var boot = new SerializedObject(Load<GameObject>(BootScopePath).GetComponent<BootScope>());
        var enemies = BootSpecs<EnemyDefinition, EnemySpec>(boot, "_enemies", d => d.ToSpec());
        Assert.That(enemies.Select(e => e.Id.Value), Does.Contain("enemy.mossguard"));
        var catalog = new ContentCatalog(
            BootSpecs<CharacterDefinition, CharacterSpec>(boot, "_characters", d => d.ToSpec()),
            enemies,
            BootSpecs<ModeDefinition, ModeSpec>(boot, "_modes", d => d.ToSpec()),
            BootSpecs<SkillDefinition, SkillSpec>(boot, "_skills", d => d.ToSpec()),
            BootSpecs<SkillTreeDefinition, SkillTreeSpec>(boot, "_trees", d => d.ToSpec()),
            BootSpecs<BossDefinition, BossSpec>(boot, "_bosses", d => d.ToSpec()));
        Assert.That(catalog.Enemy(new ContentId("enemy.mossguard")).Behaviour, Is.EqualTo(EnemyBehaviourKind.Bulwark));

        var key = new LocKey("enemy.mossguard.name");
        Assert.That(Load<LocalizationTable>(EnglishPath).ToDictionary()[key], Is.EqualTo("Mossguard"));
        Assert.That(Load<LocalizationTable>(PseudoPath).ToDictionary()[key], Is.Not.Empty);

        var random = new Soulvail.Game.Adapters.SeededRandom(14);
        var events = new RecordingEvents();
        var recorder = new RunRecorder(random, new FixedClock(System.DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, new RecordingIntents(), recorder, 64, 28, 64);
        var mode = new ContentId("mode.jungle");
        var ranger = new ContentId("character.ranger");
        RunSnapshot restore = new TestStart(catalog).At(mode, ranger, random.Seed, random.Capture(), 11);

        try
        {
            Assert.DoesNotThrow(() => session.Start(new RunConfig(mode, ranger, random.Seed, 11, SpawnPlan.Empty, restore)));
            Assert.That(session.IsRunning, Is.True);
            Assert.That(session.State.StageIndex, Is.EqualTo(11));
        }
        finally
        {
            session.End();
        }
    }

    [Test]
    public void Jungle_Panther_Boots()
    {
        // JG-07d rule 5: listed in BootScope, so the catalogue resolves the roster's id, and named in
        // both tables.
        var boot = AssetDatabase.LoadAssetAtPath<GameObject>(BootScopePath).GetComponentInChildren<BootScope>(true);
        SerializedProperty enemies = new SerializedObject(boot).FindProperty("_enemies");
        var listed = Enumerable.Range(0, enemies.arraySize).Select(i => enemies.GetArrayElementAtIndex(i).objectReferenceValue).ToList();

        Assert.That(listed, Does.Contain(Load<EnemyDefinition>(PantherPath)));

        var key = new LocKey("enemy.panther.name");

        Assert.That(Load<LocalizationTable>(EnglishPath).ToDictionary()[key], Is.EqualTo("Panther"));
        Assert.That(Load<LocalizationTable>(PseudoPath).ToDictionary().ContainsKey(key), Is.True);
    }

    [Test]
    public void Jungle_MotherPitcher_HasItsNumbers()
    {
        EnemyDefinition definition = Load<EnemyDefinition>(MotherPath);
        EnemySpec mother = definition.ToSpec();
        EnemyLook look = definition.ToLook();

        Assert.That(mother.Id.Value, Is.EqualTo("enemy.mother_pitcher"));
        Assert.That(mother.NameKey.Key, Is.EqualTo("enemy.mother_pitcher.name"));
        Assert.That(mother.Behaviour, Is.EqualTo(EnemyBehaviourKind.Boss));
        Assert.That((mother.MaxHp, mother.TargetPriority, mother.ThreatCost, mother.XpValue, mother.ContactDamage),
            Is.EqualTo((750f, 2, 40, 400f, 10f)));
        Assert.That(mother.MoveSpeed, Is.Zero);
        Assert.That(look.BodyScale, Is.EqualTo(2.5f));
        Assert.That(look.Body, Is.SameAs(Load<EnemyView>("Assets/_Project/Prefabs/Enemies/MotherPitcher.prefab")));
        Assert.That(look.Projectile, Is.SameAs(Load<ProjectileView>(PitcherSpitPath)));
        Assert.That(look.MarksLanding, Is.True);
        Assert.That(look.Cover, Is.SameAs(Load<GameObject>("Assets/_Project/Prefabs/Arenas/Jungle/Cover_MotherStones.prefab")));
    }

    [Test]
    public void Jungle_MotherPitcherBoss_HasItsPhases()
    {
        BossSpec boss = Load<BossDefinition>(MotherBossPath).ToSpec();

        Assert.That(boss.Id.Value, Is.EqualTo("boss.mother_pitcher"));
        Assert.That(boss.EnemySpecId.Value, Is.EqualTo("enemy.mother_pitcher"));
        Assert.That(boss.SpawnsCentral, Is.True);
        Assert.That(boss.BeatSeconds, Is.EqualTo(1.5f));
        Assert.That(boss.Phases.Select(p => p.EntersBelow), Is.EqualTo(new[] { 1f, 0.66f, 0.33f }));
        Assert.That(boss.Phases[0].Summons.Select(a => (a.SpecId.Value, a.Count)),
            Is.EqualTo(new[] { ("enemy.pitcher_bud", 3) }));
        Assert.That(boss.Phases[1].Summons.Select(a => (a.SpecId.Value, a.Count)),
            Is.EqualTo(new[] { ("enemy.pitcher_bud", 3), ("enemy.frog", 2) }));
        Assert.That(boss.Phases[2].Summons.Select(a => (a.SpecId.Value, a.Count)),
            Is.EqualTo(new[] { ("enemy.pitcher_bud", 3), ("enemy.frog", 3) }));
    }

    [Test]
    public void Jungle_PitcherBud_IsAHighPriorityStatic()
    {
        EnemyDefinition definition = Load<EnemyDefinition>(BudPath);
        EnemySpec bud = definition.ToSpec();

        Assert.That(bud.Id.Value, Is.EqualTo("enemy.pitcher_bud"));
        Assert.That(bud.NameKey.Key, Is.EqualTo("enemy.pitcher_bud.name"));
        Assert.That(bud.Behaviour, Is.EqualTo(EnemyBehaviourKind.Static));
        Assert.That((bud.MaxHp, bud.TargetPriority, bud.ThreatCost, bud.XpValue, bud.ContactDamage),
            Is.EqualTo((24f, 8, 4, 6f, 0f)));
        Assert.That(bud.MoveSpeed, Is.Zero);
        Assert.That(definition.ToLook().BodyScale, Is.EqualTo(0.6f));
        Assert.That(definition.ToLook().Body, Is.SameAs(Load<EnemyView>(BudBodyPath)));
    }

    [Test]
    public void Jungle_PitcherBud_IsNotRostered()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:ModeDefinition", new[] { "Assets/_Project/Data/Modes" }))
        {
            ModeSpec mode = Load<ModeDefinition>(AssetDatabase.GUIDToAssetPath(guid)).ToSpec();
            Assert.That(mode.Roster.Select(r => r.SpecId.Value), Has.No.Member("enemy.pitcher_bud"), mode.Id.Value);
        }
    }

    [Test]
    public void Jungle_StageTen_IsMotherPitcher_OtherFifthsCroaker()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        for (int stage = 1; stage <= jungle.FinalStage; stage++)
        {
            bool hasBoss = jungle.TryGetBossFor(stage, out ContentId boss);
            Assert.That(hasBoss, Is.EqualTo(stage % 5 == 0), $"Stage {stage}");
            if (hasBoss)
            {
                Assert.That(boss.Value, Is.EqualTo(stage == 10 ? "boss.mother_pitcher" : "boss.croaker"), $"Stage {stage}");
            }
        }
    }

    [Test]
    public void Jungle_MotherPitcher_Boots()
    {
        var boot = new SerializedObject(Load<GameObject>(BootScopePath).GetComponent<BootScope>());
        var catalog = new ContentCatalog(
            BootSpecs<CharacterDefinition, CharacterSpec>(boot, "_characters", d => d.ToSpec()),
            BootSpecs<EnemyDefinition, EnemySpec>(boot, "_enemies", d => d.ToSpec()),
            BootSpecs<ModeDefinition, ModeSpec>(boot, "_modes", d => d.ToSpec()),
            BootSpecs<SkillDefinition, SkillSpec>(boot, "_skills", d => d.ToSpec()),
            BootSpecs<SkillTreeDefinition, SkillTreeSpec>(boot, "_trees", d => d.ToSpec()),
            BootSpecs<BossDefinition, BossSpec>(boot, "_bosses", d => d.ToSpec()));

        Assert.That(catalog.Enemy(new ContentId("enemy.mother_pitcher")), Is.Not.Null);
        Assert.That(catalog.Enemy(new ContentId("enemy.pitcher_bud")), Is.Not.Null);
        Assert.That(catalog.Boss(new ContentId("boss.mother_pitcher")), Is.Not.Null);
        foreach (var name in new[] { ("enemy.mother_pitcher.name", "Mother Pitcher"), ("enemy.pitcher_bud.name", "Pitcher Bud") })
        {
            var key = new LocKey(name.Item1);
            Assert.That(Load<LocalizationTable>(EnglishPath).ToDictionary()[key], Is.EqualTo(name.Item2));
            Assert.That(Load<LocalizationTable>(PseudoPath).ToDictionary()[key], Is.Not.Empty);
        }

        var random = new Soulvail.Game.Adapters.SeededRandom(12);
        var events = new RecordingEvents();
        var recorder = new RunRecorder(random, new FixedClock(System.DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, new RecordingIntents(), recorder, 64, 28, 64);
        var mode = new ContentId("mode.jungle");
        var ranger = new ContentId("character.ranger");
        RunSnapshot restore = new TestStart(catalog).At(mode, ranger, random.Seed, random.Capture(), 10);

        try
        {
            Assert.DoesNotThrow(() => session.Start(new RunConfig(mode, ranger, random.Seed, 10, SpawnPlan.Empty, restore)));
            Assert.That(session.IsRunning, Is.True);
            Assert.That(session.State.StageIndex, Is.EqualTo(10));
        }
        finally
        {
            session.End();
        }
    }

    [Test]
    public void Jungle_MotherPitcher_FightIsInTheBand()
    {
        EnemySpec mother = Load<EnemyDefinition>(MotherPath).ToSpec();
        EnemySpec bud = Load<EnemyDefinition>(BudPath).ToSpec();
        BossSpec boss = Load<BossDefinition>(MotherBossPath).ToSpec();
        float hpScale = Load<ModeDefinition>(JunglePath).ToSpec().Scaling.Hp.At(10);
        // Rule 6: stage-10 Ranger ~27 DPS, standing 60% of the fight. Buds use the same uptime.
        const float EffectiveDps = 27f * 0.6f;
        int buds = boss.Phases.Sum(p => p.Summons.Where(a => a.SpecId == bud.Id).Sum(a => a.Count));
        float seconds = ((mother.MaxHp + buds * bud.MaxHp) * hpScale / EffectiveDps)
            + (boss.Phases.Count - 1) * boss.BeatSeconds;

        Assert.That(hpScale, Is.EqualTo(1.54f).Within(1e-5f));
        Assert.That(seconds, Is.InRange(75f, 120f), $"Authored HP + buds + beats: {seconds:F1} s.");
    }

    private static TSpec[] BootSpecs<TDefinition, TSpec>(SerializedObject boot, string field, System.Func<TDefinition, TSpec> convert)
        where TDefinition : ScriptableObject
    {
        SerializedProperty rows = boot.FindProperty(field);
        return Enumerable.Range(0, rows.arraySize)
            .Select(i => convert((TDefinition)rows.GetArrayElementAtIndex(i).objectReferenceValue)).ToArray();
    }

    [Test]
    public void Jungle_ScriptsItsFirstFourStages()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(1, out ScriptedStage first), Is.True);
        Assert.That(first.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)),
            Is.EqualTo(new[] { (1, "enemy.frog", 3) }), "The owner, 2026-10-22: three Frogs.");

        Assert.That(jungle.TryGetScriptedStage(2, out ScriptedStage second), Is.True);
        Assert.That(second.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)),
            Is.EqualTo(new[] { (1, "enemy.pitcher", 2) }), "The owner, 2026-10-22: two Pitchers.");

        Assert.That(jungle.TryGetScriptedStage(3, out ScriptedStage third), Is.True);
        Assert.That(third.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)),
            Is.EqualTo(new[] { (1, "enemy.pitcher", 2), (1, "enemy.frog", 3) }),
            "The owner, 2026-10-22: two Pitchers and three Frogs, together.");

        Assert.That(jungle.TryGetScriptedStage(4, out ScriptedStage fourth), Is.True);
        Assert.That(fourth.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)),
            Is.EqualTo(new[] { (1, "enemy.puffball", 2) }), "The owner, 2026-10-22: two Puffballs, nothing else.");

        Assert.That(jungle.TryGetScriptedStage(5, out _), Is.False, "Stage 5 is Old Croaker's.");
    }

    [Test]
    public void Jungle_FirstStageLevelsTheHeroOnce()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        jungle.TryGetScriptedStage(1, out ScriptedStage first);

        float xp = 0f;

        foreach (ScriptedSpawn spawn in first.Spawns)
        {
            string path = $"Assets/_Project/Data/Enemies/{Capitalised(spawn.SpecId)}.asset";
            xp += Load<EnemyDefinition>(path).ToSpec().XpValue * spawn.Count;
        }

        Assert.That(xp, Is.GreaterThanOrEqualTo(jungle.Xp.ToReach(2)), "Stage 1 must reach level 2.");
        Assert.That(xp, Is.LessThan(jungle.Xp.ToReach(2) + jungle.Xp.ToReach(3)), "…and not level 3.");
    }

    [Test]
    public void JunglesSpitter_ThrowsItsOwnSpit()
    {
        ProjectileView spit = Load<EnemyDefinition>(PitcherPath).ToLook().Projectile;

        Assert.That(spit, Is.SameAs(Load<ProjectileView>(PitcherSpitPath)), "JG-02: the Pitcher throws its own goo.");
        Assert.That(spit.Impact, Is.SameAs(Load<ImpactView>(PitcherSplatPath)), "…and it splats on a hit.");
        Assert.That(Load<EnemyDefinition>(SpitterPath).ToLook().Projectile == null, Is.True,
            "Descent's Spitter still throws the default bolt.");
    }

    [Test]
    public void JunglesSpitter_MarksWhereItsSpitLands()
    {
        // The owner's JG-13 playtest: the Pitcher's lob shows its target the way Mother's barrage does.
        Assert.That(Load<EnemyDefinition>(PitcherPath).ToLook().MarksLanding, Is.True);
        Assert.That(Load<EnemyDefinition>(SpitterPath).ToLook().MarksLanding, Is.False,
            "Descent's Spitter fires a straight bolt; it has no landing to mark.");
    }

    [Test]
    public void Jungle_OpensInASmallRoom()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.Arenas.Select(a => (a.ArenaId.Value, a.FirstStage, a.LastStage)), Is.EqualTo(new[]
        {
            ("arena.jungle.hollow", 1, 4),
            ("arena.jungle.clearing", 5, ArenaEntry.NoLastStage),
        }));

        Dictionary<ContentId, ArenaView> shipped = ShippedArenas();

        foreach (ArenaEntry entry in jungle.Arenas.Where(a => a.IsOpenAt(1)))
        {
            BoxCollider floor = shipped[entry.ArenaId].transform.Find("Collision/Floor").GetComponent<BoxCollider>();
            Vector3 size = Vector3.Scale(floor.size, floor.transform.lossyScale);

            Assert.That(Mathf.Max(size.x, size.z), Is.LessThanOrEqualTo(SmallRoomFloor),
                $"{entry.ArenaId} opens the Jungle on a {size.x} x {size.z} m floor.");
        }
    }

    [Test]
    public void Jungle_EveryArenaItRostersIsShipped()
    {
        Dictionary<ContentId, ArenaView> shipped = ShippedArenas();

        foreach (ArenaEntry entry in Load<ModeDefinition>(JunglePath).ToSpec().Arenas)
        {
            Assert.That(shipped.ContainsKey(entry.ArenaId), Is.True, $"No arena prefab carries {entry.ArenaId}.");
        }
    }

    [Test]
    public void Boot_TheJungleIsTheFirstPlace()
    {
        // A run plays the first mode the catalog holds (ClassSelectPresenter, RunTicker), and there is
        // no place-select screen yet: the Jungle first is what makes a new run a Jungle run, and
        // Descent second is what keeps a saved Descent run resuming.
        var boot = AssetDatabase.LoadAssetAtPath<GameObject>(BootScopePath).GetComponentInChildren<BootScope>(true);
        var serialized = new SerializedObject(boot);

        SerializedProperty modes = serialized.FindProperty("_modes");
        Assert.That(modes.arraySize, Is.EqualTo(2));
        Assert.That(modes.GetArrayElementAtIndex(0).objectReferenceValue, Is.EqualTo(Load<ModeDefinition>(JunglePath)));
        Assert.That(modes.GetArrayElementAtIndex(1).objectReferenceValue, Is.EqualTo(Load<ModeDefinition>(DescentPath)));

        SerializedProperty enemies = serialized.FindProperty("_enemies");
        var listed = Enumerable.Range(0, enemies.arraySize).Select(i => enemies.GetArrayElementAtIndex(i).objectReferenceValue).ToList();
        Assert.That(listed, Does.Contain(Load<EnemyDefinition>(FrogPath)));
        Assert.That(listed, Does.Contain(Load<EnemyDefinition>(PitcherPath)));
        Assert.That(listed, Does.Contain(Load<EnemyDefinition>(SpitterPath)), "Descent still rosters the Spitter's capsule.");
        Assert.That(listed, Does.Contain(Load<EnemyDefinition>(PuffballPath)));
        Assert.That(listed, Does.Contain(Load<EnemyDefinition>(BloaterPath)), "Descent still rosters the Bloater's capsule.");

        // Every bodied archetype the catalog lists is prewarmed to a pool of DeviceEnemyCap + 1 at
        // every Run load, rostered or not (M7-05g). The Rootling is rostered by no mode.
        Assert.That(listed, Has.No.Member(Load<EnemyDefinition>(RootlingPath)),
            "The Rootling is listed but no mode rosters it, so a run builds 29 of its bodies for nothing.");
    }

    /// <summary><c>enemy.frog</c> → <c>Frog</c>: an asset's file name is its id's last segment.</summary>
    private static string Capitalised(ContentId id)
    {
        string last = id.Value.Substring(id.Value.LastIndexOf('.') + 1);

        return char.ToUpperInvariant(last[0]) + last.Substring(1);
    }

    private static T Load<T>(string path)
        where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);

        Assert.That(asset, Is.Not.Null, $"No {typeof(T).Name} at {path}.");

        return asset;
    }

    /// <summary>
    /// Every value under <paramref name="property"/>, as text, in serialised order — or the property's
    /// own value when it has nothing under it.
    /// </summary>
    private static List<string> Flatten(SerializedProperty property)
    {
        var values = new List<string>();
        SerializedProperty cursor = property.Copy();
        SerializedProperty end = property.GetEndProperty();

        while (cursor.Next(true) && !SerializedProperty.EqualContents(cursor, end))
        {
            string value = Value(cursor);

            if (value != null)
            {
                values.Add($"{cursor.propertyPath}={value}");
            }
        }

        if (values.Count == 0)
        {
            values.Add($"{property.propertyPath}={Value(property)}");
        }

        return values;
    }

    /// <summary>A leaf's value as text, or null for a property that only holds others.</summary>
    private static string Value(SerializedProperty property) => property.propertyType switch
    {
        SerializedPropertyType.Float => property.floatValue.ToString("R"),
        SerializedPropertyType.Integer => property.longValue.ToString(),
        SerializedPropertyType.Boolean => property.boolValue.ToString(),
        SerializedPropertyType.String => property.stringValue,
        SerializedPropertyType.ObjectReference => AssetDatabase.GetAssetPath(property.objectReferenceValue),
        SerializedPropertyType.ArraySize => property.intValue.ToString(),
        _ => null,
    };

    private static Dictionary<ContentId, ArenaView> ShippedArenas()
    {
        var shipped = new Dictionary<ContentId, ArenaView>();

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ArenaFolder }))
        {
            var arena = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)).GetComponent<ArenaView>();

            if (arena != null)
            {
                shipped[arena.Id] = arena;
            }
        }

        return shipped;
    }
}
