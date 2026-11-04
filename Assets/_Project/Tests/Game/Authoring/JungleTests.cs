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
    private const string ShamanPath = "Assets/_Project/Data/Enemies/Shaman.asset";
    private const string StalkerPath = "Assets/_Project/Data/Enemies/Stalker.asset";
    private const string StalkerBodyPath = "Assets/_Project/Prefabs/Enemies/Stalker.prefab";
    private const string ThornweaverPath = "Assets/_Project/Data/Enemies/Thornweaver.asset";
    private const string ThornweaverBodyPath = "Assets/_Project/Prefabs/Enemies/Thornweaver.prefab";
    private const string MossguardBodyPath = "Assets/_Project/Prefabs/Enemies/Mossguard.prefab";
    private const string CroakerPath = "Assets/_Project/Data/Enemies/Croaker.asset";
    private const string MotherPath = "Assets/_Project/Data/Enemies/MotherPitcher.asset";
    private const string MotherBossPath = "Assets/_Project/Data/Enemies/MotherPitcherBoss.asset";
    private const string BudPath = "Assets/_Project/Data/Enemies/PitcherBud.asset";
    private const string WarlordPath = "Assets/_Project/Data/Enemies/MossguardWarlord.asset";
    private const string WarlordBossPath = "Assets/_Project/Data/Enemies/MossguardWarlordBoss.asset";
    private const string WarlordStonesPath = "Assets/_Project/Prefabs/Arenas/Jungle/Cover_WarlordStones.prefab";
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
    public void Jungle_Shaman_IsAShamanWithItsNumbers()
    {
        var definition = Load<EnemyDefinition>("Assets/_Project/Data/Enemies/Shaman.asset");
        EnemySpec spec = definition.ToSpec();
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Shaman));
        Assert.That(spec.MaxHp, Is.EqualTo(60));
        Assert.That(spec.MoveSpeed, Is.EqualTo(2));
        Assert.That(spec.TargetPriority, Is.EqualTo(8));
        Assert.That(spec.ThreatCost, Is.EqualTo(16));
        Assert.That(spec.XpValue, Is.EqualTo(32));
        Assert.That(spec.ContactDamage, Is.Zero);
        Assert.That(spec.Shaman, Is.Not.Null);
        Assert.That(new[] { spec.Shaman.AuraRadius, spec.Shaman.PulseInterval, spec.Shaman.CastSeconds,
            spec.Shaman.HealFraction, spec.Shaman.HasteFraction, spec.Shaman.HasteSeconds,
            spec.Shaman.HoldDistance, spec.Shaman.BehindDistance, spec.Shaman.FleeDistance,
            spec.Shaman.FleeSeconds, spec.Shaman.FleeSpeedMultiplier },
            Is.EqualTo(new[] { 8f, 3f, .6f, .1f, .25f, 3.5f, 11f, 2.5f, 4f, 1.2f, 1.4f }));
        Assert.That(definition.ToLook().Body.gameObject, Is.EqualTo(Load<GameObject>("Assets/_Project/Prefabs/Enemies/Shaman.prefab")));
    }

    [Test]
    public void Jungle_RostersTheShamanFromStageSeventeen()
    {
        RosterEntry shaman = Load<ModeDefinition>(JunglePath).ToSpec().Roster.Single(r => r.SpecId.Value == "enemy.shaman");
        Assert.That(shaman.IntroducedAtStage, Is.EqualTo(17));
        Assert.That(Load<ModeDefinition>(DescentPath).ToSpec().Roster.Select(r => r.SpecId.Value), Has.No.Member("enemy.shaman"));
    }

    [Test]
    public void Jungle_StageSeventeen_MeetsTheHealer()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        Assert.That(jungle.TryGetScriptedStage(17, out ScriptedStage stage), Is.True);
        Assert.That(stage.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.shaman", 1), (1, "enemy.mossguard", 2),
            (2, "enemy.shaman", 1), (2, "enemy.frog", 6), (2, "enemy.pitcher", 2),
            (3, "enemy.mossguard", 2), (3, "enemy.panther", 2),
        }));
        var costs = new Dictionary<string, int>
        {
            ["enemy.shaman"] = 16,
            ["enemy.mossguard"] = 14,
            ["enemy.frog"] = 4,
            ["enemy.pitcher"] = 7,
            ["enemy.panther"] = 9
        };
        Assert.That(stage.Spawns.Sum(s => s.Count * costs[s.SpecId.Value]), Is.EqualTo(144));
    }

    [Test]
    public void Jungle_Shaman_Boots()
    {
        var boot = new SerializedObject(Load<GameObject>(BootScopePath).GetComponent<BootScope>());
        var enemies = BootSpecs<EnemyDefinition, EnemySpec>(boot, "_enemies", d => d.ToSpec());
        Assert.That(enemies.Select(e => e.Id.Value), Does.Contain("enemy.shaman"));
        var catalog = new ContentCatalog(
            BootSpecs<CharacterDefinition, CharacterSpec>(boot, "_characters", d => d.ToSpec()),
            enemies,
            BootSpecs<ModeDefinition, ModeSpec>(boot, "_modes", d => d.ToSpec()),
            BootSpecs<SkillDefinition, SkillSpec>(boot, "_skills", d => d.ToSpec()),
            BootSpecs<SkillTreeDefinition, SkillTreeSpec>(boot, "_trees", d => d.ToSpec()),
            BootSpecs<BossDefinition, BossSpec>(boot, "_bosses", d => d.ToSpec()));
        Assert.That(catalog.Enemy(new ContentId("enemy.shaman")).Behaviour, Is.EqualTo(EnemyBehaviourKind.Shaman));

        var key = new LocKey("enemy.shaman.name");
        Assert.That(Load<LocalizationTable>(EnglishPath).ToDictionary()[key], Is.EqualTo("Moss Shaman"));
        Assert.That(Load<LocalizationTable>(PseudoPath).ToDictionary()[key], Is.Not.Empty);

        var random = new Soulvail.Game.Adapters.SeededRandom(14);
        var events = new RecordingEvents();
        var recorder = new RunRecorder(random, new FixedClock(System.DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, new RecordingIntents(), recorder, 64, 28, 64);
        var mode = new ContentId("mode.jungle");
        var ranger = new ContentId("character.ranger");
        RunSnapshot restore = new TestStart(catalog).At(mode, ranger, random.Seed, random.Capture(), 17);

        try
        {
            Assert.DoesNotThrow(() => session.Start(new RunConfig(mode, ranger, random.Seed, 17, SpawnPlan.Empty, restore)));
            Assert.That(session.IsRunning, Is.True);
            Assert.That(session.State.StageIndex, Is.EqualTo(17));
        }
        finally
        {
            session.End();
        }
    }


    [Test]
    public void Jungle_Cousins_HaveTheirNumbers()
    {
        EnemySpec bullfrog = Cousin("Bullfrog", FrogBodyPath, 1.3f, new Color(0.55f, 0.60f, 0.40f));
        Assert.That((bullfrog.Behaviour, bullfrog.MaxHp, bullfrog.MoveSpeed, bullfrog.TargetPriority,
            bullfrog.ThreatCost, bullfrog.XpValue, bullfrog.ContactDamage, bullfrog.HopTime, bullfrog.HopRestTime),
            Is.EqualTo((EnemyBehaviourKind.Chaser, 52f, 1.4f, 2, 6, 22f, 10f, 0.5f, 0.8f)));
        Assert.That(new[] { bullfrog.Leap.MinRange, bullfrog.Leap.MaxRange, bullfrog.Leap.CrouchSeconds,
            bullfrog.Leap.AirSeconds, bullfrog.Leap.Radius, bullfrog.Leap.DamageMultiplier, bullfrog.Leap.Cooldown },
            Is.EqualTo(new[] { 4f, 9f, 0.6f, 0.5f, 1.6f, 1.5f, 4f }), "JG-21a's authored leap.");

        EnemySpec pitcher = Cousin("ThornPitcher", PitcherBodyPath, 1.15f, new Color(0.70f, 0.80f, 0.50f));
        Assert.That((pitcher.Behaviour, pitcher.MaxHp, pitcher.MoveSpeed, pitcher.TargetPriority,
            pitcher.ThreatCost, pitcher.XpValue, pitcher.ContactDamage),
            Is.EqualTo((EnemyBehaviourKind.Spitter, 38f, 2.8f, 4, 10, 25f, 12f)));
        Assert.That((pitcher.Projectile.StandoffRange, pitcher.Projectile.Speed, pitcher.Projectile.Radius,
            pitcher.Projectile.Count, pitcher.Projectile.Spread), Is.EqualTo((14f, 7f, 1.4f, 3, 3f)));
        Assert.That(Load<EnemyDefinition>("Assets/_Project/Data/Enemies/ThornPitcher.asset").ToLook().MarksLanding,
            Is.True, "Its lobs are ringed, as the Pitcher's are.");

        EnemySpec bomber = Cousin("ChainBomber", PuffballBodyPath, 1.15f, new Color(0.85f, 0.90f, 0.55f));
        Assert.That((bomber.Behaviour, bomber.MaxHp, bomber.MoveSpeed, bomber.TargetPriority,
            bomber.ThreatCost, bomber.XpValue, bomber.ContactDamage, bomber.Reach),
            Is.EqualTo((EnemyBehaviourKind.Bloater, 30f, 2.4f, 2, 10, 28f, 15f, 2f)));
        Assert.That((bomber.Explosion.Radius, bomber.Explosion.ChainRadius), Is.EqualTo((2.5f, 4f)));

        EnemySpec panther = Cousin("ShadowPanther", PantherBodyPath, 1.1f, new Color(0.35f, 0.35f, 0.40f));
        EnemySpec parent = Load<EnemyDefinition>(PantherPath).ToSpec();
        Assert.That((panther.Behaviour, panther.MaxHp, panther.MoveSpeed, panther.TargetPriority,
            panther.ThreatCost, panther.XpValue, panther.ContactDamage),
            Is.EqualTo((EnemyBehaviourKind.Lunger, 60f, 2.6f, 5, 12, 31f, 16f)));
        Assert.That((panther.Lunge.Range, panther.Lunge.Length, panther.Lunge.Speed, panther.Lunge.HalfWidth,
            panther.Lunge.Cooldown), Is.EqualTo((parent.Lunge.Range, parent.Lunge.Length, parent.Lunge.Speed,
            parent.Lunge.HalfWidth, parent.Lunge.Cooldown)), "The Panther's lunge.");
        Assert.That((panther.Pack.PartnerRadius, panther.Pack.FlankAngleDegrees, panther.Pack.FlankTimeout),
            Is.EqualTo((16f, 90f, 3f)), "JG-21b's authored pack.");

        // The parents gain none of the new blocks.
        Assert.That(Load<EnemyDefinition>(FrogPath).ToSpec().Leap, Is.Null);
        Assert.That(Load<EnemyDefinition>(PitcherPath).ToSpec().Projectile.Count, Is.EqualTo(1));
        Assert.That(Load<EnemyDefinition>(PuffballPath).ToSpec().Explosion.ChainRadius, Is.Zero);
        Assert.That(parent.Pack, Is.Null);
    }

    [Test]
    public void Jungle_RosterAt21_IsCousinsAndKeepers()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        var buffer = new RosterEntry[jungle.Roster.Count];

        int count = jungle.RosterFor(26, buffer);

        Assert.That(buffer.Take(count).Select(r => r.SpecId.Value), Is.EquivalentTo(new[]
        {
            "enemy.mossguard", "enemy.shaman",
            "enemy.bullfrog", "enemy.thorn_pitcher", "enemy.chain_bomber", "enemy.shadow_panther",
            "enemy.stalker", "enemy.thornweaver",
        }));
        count = jungle.RosterFor(20, buffer);
        Assert.That(buffer.Take(count).Select(r => r.SpecId.Value), Is.EquivalentTo(new[]
        {
            "enemy.frog", "enemy.pitcher", "enemy.puffball", "enemy.panther", "enemy.mossguard", "enemy.shaman",
        }), "The easy four are still drawn up to 20.");
        Assert.That(Load<ModeDefinition>(DescentPath).ToSpec().Roster.All(r => r.RetiredAtStage == 0),
            Is.True, "Descent retires nothing.");
    }

    [Test]
    public void Jungle_StageTwentyOne_IsTheCousins()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(21, out ScriptedStage stage), Is.True);
        Assert.That(stage.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.bullfrog", 6),
            (2, "enemy.thorn_pitcher", 3), (2, "enemy.bullfrog", 3),
            (3, "enemy.chain_bomber", 4), (3, "enemy.bullfrog", 2),
            (4, "enemy.shadow_panther", 2), (4, "enemy.mossguard", 1), (4, "enemy.thorn_pitcher", 2),
        }));

        Dictionary<ContentId, int> costs = new[] { "Bullfrog", "ThornPitcher", "ChainBomber", "ShadowPanther", "Mossguard" }
            .Select(name => Load<EnemyDefinition>($"Assets/_Project/Data/Enemies/{name}.asset").ToSpec())
            .ToDictionary(enemy => enemy.Id, enemy => enemy.ThreatCost);
        int total = stage.Spawns.Sum(spawn => spawn.Count * costs[spawn.SpecId]);
        Assert.That(total, Is.EqualTo(194));
        Assert.That(total, Is.LessThan(208), "The breather after Thornheart, below stage 19's gate.");
    }

    [Test]
    public void Jungle_Cousins_Boot()
    {
        var boot = new SerializedObject(Load<GameObject>(BootScopePath).GetComponent<BootScope>());
        var catalog = new ContentCatalog(
            BootSpecs<CharacterDefinition, CharacterSpec>(boot, "_characters", d => d.ToSpec()),
            BootSpecs<EnemyDefinition, EnemySpec>(boot, "_enemies", d => d.ToSpec()),
            BootSpecs<ModeDefinition, ModeSpec>(boot, "_modes", d => d.ToSpec()),
            BootSpecs<SkillDefinition, SkillSpec>(boot, "_skills", d => d.ToSpec()),
            BootSpecs<SkillTreeDefinition, SkillTreeSpec>(boot, "_trees", d => d.ToSpec()),
            BootSpecs<BossDefinition, BossSpec>(boot, "_bosses", d => d.ToSpec()));
        var english = Load<LocalizationTable>(EnglishPath).ToDictionary();
        var pseudo = Load<LocalizationTable>(PseudoPath).ToDictionary();
        var names = new Dictionary<string, string>
        {
            ["enemy.bullfrog"] = "Bullfrog",
            ["enemy.thorn_pitcher"] = "Thorn Pitcher",
            ["enemy.chain_bomber"] = "Chain Bomber",
            ["enemy.shadow_panther"] = "Shadow Panther",
        };

        foreach (KeyValuePair<string, string> cousin in names)
        {
            EnemySpec spec = catalog.Enemy(new ContentId(cousin.Key));
            Assert.That(english[spec.NameKey], Is.EqualTo(cousin.Value));
            Assert.That(pseudo[spec.NameKey], Is.EqualTo(LocalisationSweepTests.Pseudolocalise(cousin.Value)));
        }

        var random = new Soulvail.Game.Adapters.SeededRandom(21);
        var events = new RecordingEvents();
        var recorder = new RunRecorder(random, new FixedClock(System.DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, new RecordingIntents(), recorder, 64, 28, 64);
        var mode = new ContentId("mode.jungle");
        var ranger = new ContentId("character.ranger");
        RunSnapshot restore = new TestStart(catalog).At(mode, ranger, random.Seed, random.Capture(), 21);

        try
        {
            Assert.DoesNotThrow(() => session.Start(new RunConfig(mode, ranger, random.Seed, 21, SpawnPlan.Empty, restore)));
            Assert.That(session.State.StageIndex, Is.EqualTo(21));
        }
        finally
        {
            session.End();
        }
    }

    [Test]
    public void Jungle_Stalker_IsAStalkerWithItsNumbers()
    {
        var definition = Load<EnemyDefinition>(StalkerPath);
        EnemySpec spec = definition.ToSpec();

        Assert.That((spec.Behaviour, spec.MaxHp, spec.ThreatCost, spec.TargetPriority, spec.MoveSpeed,
            spec.XpValue, spec.ContactDamage, spec.Reach, spec.WindupTime, spec.RecoverTime),
            Is.EqualTo((EnemyBehaviourKind.Stalker, 70f, 18, 6, 2.6f, 36f, 18f, 1.2f, 0.45f, 1f)));
        Assert.That(new[] { spec.Stalker.BehindDistance, spec.Stalker.CloakDelay, spec.Stalker.StrikeRange,
            spec.Stalker.BehindAngleDegrees, spec.Stalker.StrikeLength, spec.Stalker.StrikeSpeed,
            spec.Stalker.StrikeHalfWidth, spec.Stalker.RevealSeconds, spec.Stalker.StrikeCooldown },
            Is.EqualTo(new[] { 3f, 0.4f, 4f, 110f, 5f, 18f, 0.6f, 1.5f, 3f }), "JG-22a's authored block.");
        Assert.That(definition.ToLook().Body.gameObject, Is.EqualTo(Load<GameObject>(StalkerBodyPath)));
    }

    [Test]
    public void Jungle_RostersStalkerFrom22()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        var buffer = new RosterEntry[jungle.Roster.Count];

        int count = jungle.RosterFor(21, buffer);
        Assert.That(buffer.Take(count).Select(r => r.SpecId.Value), Has.No.Member("enemy.stalker"));
        count = jungle.RosterFor(22, buffer);
        Assert.That(buffer.Take(count).Select(r => r.SpecId.Value), Has.Member("enemy.stalker"));
        Assert.That(Load<ModeDefinition>(DescentPath).ToSpec().Roster.Select(r => r.SpecId.Value),
            Has.No.Member("enemy.stalker"));
    }

    [Test]
    public void Jungle_StageTwentyTwo_IsTheStalker()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(22, out ScriptedStage stage), Is.True);
        Assert.That(stage.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.stalker", 1), (1, "enemy.bullfrog", 6),
            (2, "enemy.shadow_panther", 2), (2, "enemy.thorn_pitcher", 2), (2, "enemy.chain_bomber", 4),
            (3, "enemy.stalker", 2), (3, "enemy.mossguard", 2), (3, "enemy.shaman", 1),
        }));
    }

    [Test]
    public void Jungle_StageTwentyThree_IsTheTangle()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(23, out ScriptedStage stage), Is.True);
        Assert.That(stage.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.chain_bomber", 4), (1, "enemy.shadow_panther", 2), (1, "enemy.bullfrog", 4),
            (2, "enemy.mossguard", 2), (2, "enemy.shaman", 1), (2, "enemy.thorn_pitcher", 3),
            (3, "enemy.stalker", 2), (3, "enemy.shadow_panther", 2), (3, "enemy.chain_bomber", 2),
        }));
    }

    [Test]
    public void Jungle_StageTwentyThree_IntroducesNothing()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        Dictionary<ContentId, int> introductions = jungle.Roster.ToDictionary(r => r.SpecId, r => r.IntroducedAtStage);

        Assert.That(jungle.TryGetScriptedStage(23, out ScriptedStage stage), Is.True);
        foreach (ScriptedSpawn spawn in stage.Spawns)
        {
            Assert.That(introductions.ContainsKey(spawn.SpecId), Is.True, $"{spawn.SpecId} is rostered");
            Assert.That(introductions[spawn.SpecId], Is.LessThan(23), $"{spawn.SpecId} is known before 23");
        }

        Assert.That(jungle.TryGetIntroduction(23, out _), Is.False);
        Assert.That(jungle.TryGetBossFor(25, out _), Is.True, "Stage 25 stays a boss stage.");
    }

    [Test]
    public void Jungle_StagesTwentyOneToTwentyNine_Climb()
    {
        // JG-24d rule 5 and JG-26c rules 6–7: 21–24 climb into Gravemaw, 26–29 into the Apex.
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        Dictionary<ContentId, int> costs = new[]
            {
                "Bullfrog", "ThornPitcher", "ChainBomber", "ShadowPanther", "Mossguard", "Shaman", "Stalker",
                "Thornweaver",
            }
            .Select(name => Load<EnemyDefinition>($"Assets/_Project/Data/Enemies/{name}.asset").ToSpec())
            .ToDictionary(enemy => enemy.Id, enemy => enemy.ThreatCost);

        int Threat(int index)
        {
            Assert.That(jungle.TryGetScriptedStage(index, out ScriptedStage stage), Is.True, $"stage {index}");
            return stage.Spawns.Sum(spawn => spawn.Count * costs[spawn.SpecId]);
        }

        Assert.That((Threat(21), Threat(22), Threat(23), Threat(24)), Is.EqualTo((194, 218, 242, 269)));
        Assert.That((Threat(26), Threat(27), Threat(28), Threat(29)), Is.EqualTo((295, 321, 345, 373)));
        Assert.That(jungle.TryGetBossFor(25, out _), Is.True, "Stage 25 stays a boss stage.");
        Assert.That(jungle.TryGetScriptedStage(25, out _), Is.False);
        Assert.That(jungle.TryGetBossFor(30, out _), Is.True, "Stage 30 stays a boss stage.");
        Assert.That(jungle.TryGetScriptedStage(30, out _), Is.False);
    }

    [Test]
    public void Jungle_StageTwentySix_IsTheClearing()
    {
        // JG-26c rule 1: a breather after Gravemaw.
        Assert.That(Spawns(26), Is.EqualTo(new[]
        {
            (1, "enemy.bullfrog", 6), (1, "enemy.chain_bomber", 4), (1, "enemy.shadow_panther", 2),
            (2, "enemy.thorn_pitcher", 3), (2, "enemy.mossguard", 2), (2, "enemy.shaman", 1),
            (2, "enemy.bullfrog", 2),
            (3, "enemy.stalker", 2), (3, "enemy.shadow_panther", 2), (3, "enemy.thornweaver", 1),
            (3, "enemy.bullfrog", 4), (3, "enemy.chain_bomber", 1),
        }));
    }

    [Test]
    public void Jungle_StageTwentySeven_IsTheFortress()
    {
        // JG-26c rule 2: a held line to break.
        Assert.That(Spawns(27), Is.EqualTo(new[]
        {
            (1, "enemy.mossguard", 3), (1, "enemy.shaman", 1), (1, "enemy.thorn_pitcher", 3),
            (1, "enemy.bullfrog", 4),
            (2, "enemy.thornweaver", 2), (2, "enemy.chain_bomber", 4), (2, "enemy.shadow_panther", 2),
            (2, "enemy.bullfrog", 1),
            (3, "enemy.mossguard", 3), (3, "enemy.shaman", 2), (3, "enemy.thornweaver", 1),
            (3, "enemy.thorn_pitcher", 2),
        }));
    }

    [Test]
    public void Jungle_StageTwentyEight_IsTheHunt()
    {
        // JG-26c rule 3: danger from the flanks and behind.
        Assert.That(Spawns(28), Is.EqualTo(new[]
        {
            (1, "enemy.shadow_panther", 4), (1, "enemy.bullfrog", 6), (1, "enemy.chain_bomber", 2),
            (2, "enemy.stalker", 3), (2, "enemy.shadow_panther", 2), (2, "enemy.chain_bomber", 3),
            (3, "enemy.shadow_panther", 4), (3, "enemy.stalker", 2), (3, "enemy.thornweaver", 1),
            (3, "enemy.bullfrog", 4), (3, "enemy.thorn_pitcher", 1),
        }));
    }

    [Test]
    public void Jungle_StageTwentyNine_IsTheGauntlet()
    {
        // JG-26c rule 4: four waves, one per theme.
        Assert.That(Spawns(29), Is.EqualTo(new[]
        {
            (1, "enemy.bullfrog", 6), (1, "enemy.chain_bomber", 4), (1, "enemy.shadow_panther", 2),
            (2, "enemy.mossguard", 2), (2, "enemy.shaman", 1), (2, "enemy.thornweaver", 2),
            (2, "enemy.thorn_pitcher", 2),
            (3, "enemy.stalker", 2), (3, "enemy.shadow_panther", 4),
            (4, "enemy.mossguard", 2), (4, "enemy.shaman", 1), (4, "enemy.thornweaver", 1),
            (4, "enemy.stalker", 1), (4, "enemy.shadow_panther", 1), (4, "enemy.bullfrog", 1),
        }));
    }

    [Test]
    public void Jungle_StagesTwentySixToTwentyNine_IntroduceNothing()
    {
        // JG-26c rule 5: the existing roster only, none of the easy four retired at 21.
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        var buffer = new RosterEntry[jungle.Roster.Count];
        string[] easyFour = { "enemy.frog", "enemy.pitcher", "enemy.puffball", "enemy.panther" };

        for (int index = 26; index <= 29; index++)
        {
            Assert.That(jungle.TryGetScriptedStage(index, out ScriptedStage stage), Is.True, $"stage {index}");
            int count = jungle.RosterFor(index, buffer);
            RosterEntry[] active = buffer.Take(count).ToArray();
            foreach (ScriptedSpawn spawn in stage.Spawns)
            {
                RosterEntry[] rows = active.Where(r => r.SpecId == spawn.SpecId).ToArray();
                Assert.That(rows, Has.Length.EqualTo(1), $"{spawn.SpecId} is rostered at {index}");
                Assert.That(rows[0].IntroducedAtStage, Is.LessThan(26), $"{spawn.SpecId} is known before 26");
                Assert.That(easyFour, Has.No.Member(spawn.SpecId.Value), $"stage {index}");
            }

            Assert.That(jungle.TryGetIntroduction(index, out _), Is.False, $"stage {index}");
        }
    }

    private static (int Wave, string SpecId, int Count)[] Spawns(int index)
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        Assert.That(jungle.TryGetScriptedStage(index, out ScriptedStage stage), Is.True, $"stage {index}");
        return stage.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)).ToArray();
    }

    [Test]
    public void Jungle_Thornweaver_HasItsNumbers()
    {
        // JG-24d rule 2.
        var definition = Load<EnemyDefinition>(ThornweaverPath);
        EnemySpec spec = definition.ToSpec();

        Assert.That((spec.Id.Value, spec.Behaviour, spec.MaxHp, spec.ThreatCost, spec.TargetPriority,
            spec.MoveSpeed, spec.XpValue, spec.ContactDamage, spec.Reach, spec.WindupTime, spec.RecoverTime),
            Is.EqualTo(("enemy.thornweaver", EnemyBehaviourKind.Thornweaver, 70f, 15, 7, 2f, 34f, 10f, 1f,
                0.8f, 1f)));
        ThornweaverSpec weaver = spec.Thornweaver;
        Assert.That(new[] { weaver.HoldDistance, weaver.CastRange, weaver.CastInterval, weaver.CastSeconds,
            weaver.WallLength, weaver.WallHalfWidth, weaver.ArmSeconds, weaver.StandSeconds,
            weaver.BeyondDistance }, Is.EqualTo(new[] { 10f, 14f, 5f, 0.8f, 5f, 0.4f, 0.9f, 5f, 2.5f }),
            "JG-24a's authored block.");
        Assert.That(weaver.MaxWalls, Is.EqualTo(2));
        Assert.That(definition.ToLook().Body.gameObject, Is.EqualTo(Load<GameObject>(ThornweaverBodyPath)));
    }

    [Test]
    public void Jungle_RostersThornweaverFrom24()
    {
        // JG-24d rule 3.
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        var buffer = new RosterEntry[jungle.Roster.Count];

        int count = jungle.RosterFor(23, buffer);
        Assert.That(buffer.Take(count).Select(r => r.SpecId.Value), Has.No.Member("enemy.thornweaver"));
        count = jungle.RosterFor(24, buffer);
        Assert.That(buffer.Take(count).Select(r => r.SpecId.Value), Has.Member("enemy.thornweaver"));
        Assert.That(Load<ModeDefinition>(DescentPath).ToSpec().Roster.Select(r => r.SpecId.Value),
            Has.No.Member("enemy.thornweaver"));
    }

    [Test]
    public void Jungle_StageTwentyFour_IsTheWeaver()
    {
        // JG-24d rule 4: met alone, then two weavers pen the player toward the Mossguards.
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(24, out ScriptedStage stage), Is.True);
        Assert.That(stage.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.thornweaver", 1), (1, "enemy.bullfrog", 8),
            (2, "enemy.stalker", 2), (2, "enemy.chain_bomber", 4), (2, "enemy.thorn_pitcher", 2),
            (2, "enemy.bullfrog", 2),
            (3, "enemy.thornweaver", 2), (3, "enemy.mossguard", 2), (3, "enemy.shadow_panther", 2),
            (3, "enemy.shaman", 1),
        }));
    }

    [Test]
    public void Jungle_Thornweaver_Boots()
    {
        // JG-24d rule 6.
        var boot = new SerializedObject(Load<GameObject>(BootScopePath).GetComponent<BootScope>());
        var catalog = new ContentCatalog(
            BootSpecs<CharacterDefinition, CharacterSpec>(boot, "_characters", d => d.ToSpec()),
            BootSpecs<EnemyDefinition, EnemySpec>(boot, "_enemies", d => d.ToSpec()),
            BootSpecs<ModeDefinition, ModeSpec>(boot, "_modes", d => d.ToSpec()),
            BootSpecs<SkillDefinition, SkillSpec>(boot, "_skills", d => d.ToSpec()),
            BootSpecs<SkillTreeDefinition, SkillTreeSpec>(boot, "_trees", d => d.ToSpec()),
            BootSpecs<BossDefinition, BossSpec>(boot, "_bosses", d => d.ToSpec()));
        EnemySpec weaver = catalog.Enemy(new ContentId("enemy.thornweaver"));
        Assert.That(weaver.Behaviour, Is.EqualTo(EnemyBehaviourKind.Thornweaver));
        Assert.That(Load<LocalizationTable>(EnglishPath).ToDictionary()[weaver.NameKey], Is.EqualTo("Thornweaver"));
        Assert.That(Load<LocalizationTable>(PseudoPath).ToDictionary()[weaver.NameKey],
            Is.EqualTo(LocalisationSweepTests.Pseudolocalise("Thornweaver")));

        var random = new Soulvail.Game.Adapters.SeededRandom(24);
        var events = new RecordingEvents();
        var recorder = new RunRecorder(random, new FixedClock(System.DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, new RecordingIntents(), recorder, 64, 28, 64);
        var mode = new ContentId("mode.jungle");
        var ranger = new ContentId("character.ranger");
        RunSnapshot restore = new TestStart(catalog).At(mode, ranger, random.Seed, random.Capture(), 24);

        try
        {
            Assert.DoesNotThrow(() => session.Start(new RunConfig(mode, ranger, random.Seed, 24, SpawnPlan.Empty, restore)));
            Assert.That(session.State.StageIndex, Is.EqualTo(24));
        }
        finally
        {
            session.End();
        }
    }

    [Test]
    public void Jungle_Stalker_Boots()
    {
        var boot = new SerializedObject(Load<GameObject>(BootScopePath).GetComponent<BootScope>());
        var catalog = new ContentCatalog(
            BootSpecs<CharacterDefinition, CharacterSpec>(boot, "_characters", d => d.ToSpec()),
            BootSpecs<EnemyDefinition, EnemySpec>(boot, "_enemies", d => d.ToSpec()),
            BootSpecs<ModeDefinition, ModeSpec>(boot, "_modes", d => d.ToSpec()),
            BootSpecs<SkillDefinition, SkillSpec>(boot, "_skills", d => d.ToSpec()),
            BootSpecs<SkillTreeDefinition, SkillTreeSpec>(boot, "_trees", d => d.ToSpec()),
            BootSpecs<BossDefinition, BossSpec>(boot, "_bosses", d => d.ToSpec()));
        EnemySpec stalker = catalog.Enemy(new ContentId("enemy.stalker"));
        Assert.That(stalker.Behaviour, Is.EqualTo(EnemyBehaviourKind.Stalker));
        Assert.That(Load<LocalizationTable>(EnglishPath).ToDictionary()[stalker.NameKey],
            Is.EqualTo("Chameleon Stalker"));
        Assert.That(Load<LocalizationTable>(PseudoPath).ToDictionary()[stalker.NameKey],
            Is.EqualTo(LocalisationSweepTests.Pseudolocalise("Chameleon Stalker")));

        var random = new Soulvail.Game.Adapters.SeededRandom(22);
        var events = new RecordingEvents();
        var recorder = new RunRecorder(random, new FixedClock(System.DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, new RecordingIntents(), recorder, 64, 28, 64);
        var mode = new ContentId("mode.jungle");
        var ranger = new ContentId("character.ranger");
        RunSnapshot restore = new TestStart(catalog).At(mode, ranger, random.Seed, random.Capture(), 22);

        try
        {
            Assert.DoesNotThrow(() => session.Start(new RunConfig(mode, ranger, random.Seed, 22, SpawnPlan.Empty, restore)));
            Assert.That(session.State.StageIndex, Is.EqualTo(22));
        }
        finally
        {
            session.End();
        }
    }

    /// <summary>A cousin's spec, after checking it wears its parent's body at its own scale and tint.</summary>
    private static EnemySpec Cousin(string asset, string bodyPath, float scale, Color tint)
    {
        var definition = Load<EnemyDefinition>($"Assets/_Project/Data/Enemies/{asset}.asset");
        EnemyLook look = definition.ToLook();
        Assert.That(look.Body.gameObject, Is.EqualTo(Load<GameObject>(bodyPath)), asset);
        Assert.That(look.BodyScale, Is.EqualTo(scale).Within(1e-5f), asset);
        Assert.That(new[] { look.Tint.r, look.Tint.g, look.Tint.b },
            Is.EqualTo(new[] { tint.r, tint.g, tint.b }).Within(1e-5f), asset);
        return definition.ToSpec();
    }

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
            ("enemy.shaman", 17),
            ("enemy.bullfrog", 21),
            ("enemy.thorn_pitcher", 21),
            ("enemy.chain_bomber", 21),
            ("enemy.shadow_panther", 21),
            ("enemy.stalker", 22),
            ("enemy.thornweaver", 24),
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
        Assert.That(mossguard.Reach, Is.EqualTo(2f).Within(1e-5f));
        Assert.That(mossguard.WindupTime, Is.EqualTo(0.7f).Within(1e-5f));
        Assert.That(mossguard.RecoverTime, Is.EqualTo(1.5f));
        Assert.That(mossguard.ContactDamage, Is.EqualTo(16f));
        Assert.That(definition.ToLook().BodyScale, Is.EqualTo(1.27f).Within(1e-5f));
        Assert.That(mossguard.BodyRadius, Is.EqualTo(EnemySpec.BaseBodyRadius * 1.27f).Within(1e-5f));
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
    public void Jungle_StagesFifteenAndTwenty_AreNotScripted()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(15, out _), Is.False, "Stage 15 is the Mossguard Warlord's.");
        Assert.That(jungle.TryGetScriptedStage(20, out _), Is.False, "Stage 20 is Thornheart's.");
    }

    [Test]
    public void Jungle_StageEighteen_IsTheHealersWall()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(18, out ScriptedStage stage), Is.True);
        Assert.That(stage.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.mossguard", 2), (1, "enemy.shaman", 1), (1, "enemy.pitcher", 2),
            (2, "enemy.frog", 4), (2, "enemy.puffball", 3), (2, "enemy.panther", 2),
            (3, "enemy.mossguard", 2), (3, "enemy.shaman", 1), (3, "enemy.panther", 2),
        }));
    }

    [Test]
    public void Jungle_StageNineteen_IsTheGate()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(19, out ScriptedStage stage), Is.True);
        Assert.That(stage.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.frog", 6), (1, "enemy.panther", 2), (1, "enemy.pitcher", 2),
            (2, "enemy.shaman", 2), (2, "enemy.mossguard", 3),
            (3, "enemy.mossguard", 2), (3, "enemy.shaman", 1), (3, "enemy.panther", 2), (3, "enemy.puffball", 2),
        }));
    }

    [Test]
    public void Jungle_StagesSixteenToNineteen_ClimbInThreat()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        Dictionary<ContentId, int> costs = new[] { FrogPath, PitcherPath, PuffballPath, PantherPath, MossguardPath, ShamanPath }
            .Select(path => Load<EnemyDefinition>(path).ToSpec())
            .ToDictionary(enemy => enemy.Id, enemy => enemy.ThreatCost);
        var totals = new List<int>();

        for (int stage = 16; stage <= 19; stage++)
        {
            Assert.That(jungle.TryGetScriptedStage(stage, out ScriptedStage scripted), Is.True);
            totals.Add(scripted.Spawns.Sum(spawn => spawn.Count * costs[spawn.SpecId]));
        }

        Assert.That(totals, Is.EqualTo(new[] { 121, 144, 178, 208 }));
        Assert.That(totals, Is.Ordered.Ascending);
    }

    [Test]
    public void Jungle_StageSixteen_Regroups()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        Assert.That(jungle.TryGetScriptedStage(16, out ScriptedStage sixteenth), Is.True);
        Assert.That(sixteenth.Spawns.Select(s => (s.Wave, s.SpecId.Value, s.Count)), Is.EqualTo(new[]
        {
            (1, "enemy.frog", 6),
            (1, "enemy.pitcher", 2),
            (2, "enemy.mossguard", 2),
            (2, "enemy.panther", 2),
            (3, "enemy.mossguard", 1),
            (3, "enemy.puffball", 2),
            (3, "enemy.pitcher", 1),
        }));
    }

    [Test]
    public void Jungle_StageSixteen_IsABreather()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        Dictionary<ContentId, int> costs = new[] { FrogPath, PitcherPath, PuffballPath, PantherPath, MossguardPath }
            .Select(path => Load<EnemyDefinition>(path).ToSpec())
            .ToDictionary(enemy => enemy.Id, enemy => enemy.ThreatCost);

        Assert.That(jungle.TryGetScriptedStage(16, out ScriptedStage sixteenth), Is.True);
        int total = sixteenth.Spawns.Sum(spawn => spawn.Count * costs[spawn.SpecId]);

        Assert.That(total, Is.EqualTo(121));
        Assert.That(total, Is.LessThan(128), "Stage 16 steps down from stage 14 after the Warlord.");
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
    public void Jungle_FifthStages_HaveTheirBosses()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();

        for (int stage = 1; stage <= jungle.FinalStage; stage++)
        {
            bool hasBoss = jungle.TryGetBossFor(stage, out ContentId boss);
            Assert.That(hasBoss, Is.EqualTo(stage % 5 == 0), $"Stage {stage}");
            if (hasBoss)
            {
                Assert.That(boss.Value, Is.EqualTo(stage switch
                {
                    10 => "boss.mother_pitcher",
                    15 => "boss.mossguard_warlord",
                    20 => "boss.thornheart",
                    25 => "boss.gravemaw",
                    30 => "boss.apex",
                    _ => "boss.croaker",
                }), $"Stage {stage}");
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

    [Test]
    public void Jungle_Warlord_HasItsNumbers()
    {
        EnemyDefinition definition = Load<EnemyDefinition>(WarlordPath);
        EnemySpec warlord = definition.ToSpec();
        EnemyLook look = definition.ToLook();

        Assert.That(warlord.Id.Value, Is.EqualTo("enemy.mossguard_warlord"));
        Assert.That(warlord.NameKey.Key, Is.EqualTo("enemy.mossguard_warlord.name"));
        Assert.That(warlord.Behaviour, Is.EqualTo(EnemyBehaviourKind.Boss));
        Assert.That((warlord.MoveSpeed, warlord.ContactDamage, warlord.XpValue, warlord.ThreatCost),
            Is.EqualTo((1.4f, 20f, 500f, 40)));
        Assert.That(look.BodyScale, Is.EqualTo(1.27f * 1.6f).Within(0.005f));
        Assert.That(look.Body, Is.SameAs(Load<EnemyView>("Assets/_Project/Prefabs/Enemies/Mossguard_Warlord.prefab")));
        Assert.That(look.Projectile, Is.SameAs(Load<ProjectileView>("Assets/_Project/Prefabs/Projectiles/WarlordSpear.prefab")));
        Assert.That(look.MarksLanding, Is.True);
        Assert.That(look.Cover, Is.SameAs(Load<GameObject>(WarlordStonesPath)));
        Assert.That(Load<LocalizationTable>(EnglishPath).ToDictionary()[warlord.NameKey], Is.EqualTo("Mossguard Warlord"));
        Assert.That(Load<LocalizationTable>(PseudoPath).ToDictionary()[warlord.NameKey], Is.Not.Empty);
    }

    [Test]
    public void Jungle_WarlordBoss_HasItsPhases()
    {
        BossSpec boss = Load<BossDefinition>(WarlordBossPath).ToSpec();

        Assert.That(boss.Id.Value, Is.EqualTo("boss.mossguard_warlord"));
        Assert.That(boss.EnemySpecId.Value, Is.EqualTo("enemy.mossguard_warlord"));
        Assert.That(boss.SpawnsCentral, Is.True);
        Assert.That(boss.BeatSeconds, Is.EqualTo(1.5f));
        Assert.That(boss.Phases.Select(p => p.EntersBelow), Is.EqualTo(new[] { 1f, 0.66f, 0.33f }));
        Assert.That(boss.Phases.SelectMany(p => p.Summons), Is.Empty, "The war cry is its adds.");
    }

    [Test]
    public void Jungle_WarlordStones_AreCoverAndRingTheCentre()
    {
        GameObject prefab = Load<GameObject>(WarlordStonesPath);
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab),
            Is.SameAs(Load<GameObject>("Assets/_Project/Prefabs/Arenas/Jungle/Cover_MotherStones.prefab")),
            "A variant of the Mother's stones.");
        GameObject stones = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        try
        {
            Physics.SyncTransforms();
            BoxCollider[] boxes = stones.GetComponentsInChildren<BoxCollider>();
            Assert.That(boxes.Length, Is.EqualTo(5));
            var angles = new List<float>();
            foreach (BoxCollider box in boxes)
            {
                Assert.That(box.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Cover")), box.name);
                Assert.That(box.isTrigger, Is.False, box.name);
                Vector3 half = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f;
                Assert.That(Mathf.Min(half.x, half.z), Is.GreaterThanOrEqualTo(1.2f), box.name);
                Vector3 at = box.bounds.center;
                float distance = new Vector2(at.x, at.z).magnitude;
                Assert.That(distance, Is.InRange(6f, 7f), box.name);
                Assert.That(distance - new Vector2(half.x, half.z).magnitude, Is.GreaterThan(3f), $"{box.name} clears the centre.");
                angles.Add(Mathf.Repeat(Mathf.Atan2(at.x, at.z) * Mathf.Rad2Deg, 360f));
            }
            angles.Sort();
            float[] gaps = angles.Select((a, i) => Mathf.Repeat(angles[(i + 1) % angles.Count] - a, 360f)).ToArray();
            // A 90° gap at 6.5 m leaves no ring point ~5 m from a stone: well inside a 14 m charge.
            Assert.That(gaps.Max(), Is.LessThanOrEqualTo(90f), "Every approach has a stone.");
            Assert.That(gaps.Max() - gaps.Min(), Is.GreaterThan(5f), "Uneven angles.");
        }
        finally
        {
            Object.DestroyImmediate(stones);
        }
    }

    [Test]
    public void Jungle_Warlord_FightIsInTheBand()
    {
        EnemySpec warlord = Load<EnemyDefinition>(WarlordPath).ToSpec();
        BossSpec boss = Load<BossDefinition>(WarlordBossPath).ToSpec();
        float hpScale = Load<ModeDefinition>(JunglePath).ToSpec().Scaling.Hp.At(15);
        // Rule 6: stage-15 Ranger ~33 DPS, half of it past the front guard. Guards excluded.
        const float EffectiveDps = 33f * 0.5f;
        float seconds = (warlord.MaxHp * hpScale / EffectiveDps) + ((boss.Phases.Count - 1) * boss.BeatSeconds);

        Assert.That(hpScale, Is.EqualTo(1.84f).Within(1e-5f));
        Assert.That(seconds, Is.InRange(90f, 100f), $"Aimed at ~95 s: {seconds:F1} s.");
    }

    [Test]
    public void Jungle_StageFifteen_StartsWithTheWarlord()
    {
        var boot = new SerializedObject(Load<GameObject>(BootScopePath).GetComponent<BootScope>());
        var catalog = new ContentCatalog(
            BootSpecs<CharacterDefinition, CharacterSpec>(boot, "_characters", d => d.ToSpec()),
            BootSpecs<EnemyDefinition, EnemySpec>(boot, "_enemies", d => d.ToSpec()),
            BootSpecs<ModeDefinition, ModeSpec>(boot, "_modes", d => d.ToSpec()),
            BootSpecs<SkillDefinition, SkillSpec>(boot, "_skills", d => d.ToSpec()),
            BootSpecs<SkillTreeDefinition, SkillTreeSpec>(boot, "_trees", d => d.ToSpec()),
            BootSpecs<BossDefinition, BossSpec>(boot, "_bosses", d => d.ToSpec()));
        var random = new Soulvail.Game.Adapters.SeededRandom(12);
        var events = new RecordingEvents();
        var recorder = new RunRecorder(random, new FixedClock(System.DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, new RecordingIntents(), recorder, 64, 28, 64);
        var mode = new ContentId("mode.jungle");
        var ranger = new ContentId("character.ranger");
        RunSnapshot restore = new TestStart(catalog).At(mode, ranger, random.Seed, random.Capture(), 15);

        try
        {
            session.Start(new RunConfig(mode, ranger, random.Seed, 15, SpawnPlan.Empty, restore));
            Assert.That(session.State.StageIndex, Is.EqualTo(15));
            var world = new WorldSnapshot(64)
            {
                Dt = 1f / 30f,
                SpawnPoints = new[]
                {
                    new System.Numerics.Vector3(-8f, 0f, 8f), new System.Numerics.Vector3(8f, 0f, 8f),
                    new System.Numerics.Vector3(-8f, 0f, -8f), new System.Numerics.Vector3(8f, 0f, -8f),
                },
            };
            for (int frame = 0; frame < 300 && events.Count<Soulvail.Core.Events.EnemySpawned>() == 0; frame++)
            {
                session.Tick(world);
            }
            Assert.That(events.Of<Soulvail.Core.Events.EnemySpawned>().Select(e => e.SpecId.Value),
                Has.Member("enemy.mossguard_warlord"));
        }
        finally
        {
            session.End();
        }
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
        // every Run load, rostered or not (M7-05g). The Rootling is rostered by no mode, but
        // Thornheart's ripe pods grow it (JG-20h), so the catalog must hold it.
        Assert.That(listed, Does.Contain(Load<EnemyDefinition>(RootlingPath)),
            "Thornheart's pods summon Rootlings; a catalog without one throws mid-fight.");
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
