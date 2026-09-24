using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// The second class, number by number, each against the document it comes from or the ruling that
/// moved it. <c>Gravecaller.asset</c> is authored by M5-02 and read by no run until M5-07, so this
/// fixture is the only thing in the project that looks at it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two of these numbers are not CH §3.2's, and the rows say which.</b> The speed is 3.1 rather
/// than 5.6 (rule 1: the band was scaled by the factor the owner's M2-03 retune already applied to
/// the Oathbound), and Bone Bolt's damage is 9 rather than 7 (rule 2: seven kills a stage-1 Husk in
/// six hits, outside GD §6.2's three-to-five, before a run has started). Both are asserted here
/// <em>with their controls</em> — <see cref="Speed_EveryClassOutrunsEveryEnemy"/> and, in
/// <c>TimeToKillTests</c>, <c>BoneBolt_AtTheDocumentedSevenWouldBreakTheBand</c> — so that a row
/// which passes is a row that measured something rather than one that restated a constant.
/// </para>
/// <para>
/// <b>Where the split with <c>TimeToKillTests</c> falls, and why.</b> Anything that needs the
/// shipped asset is here, because <c>Soulvail.Tests.Core</c> cannot reach <c>AssetDatabase</c>
/// (M0-10); the band arithmetic is there, because it is arithmetic and that fixture owns GD §6.2.
/// The two meet at 9 damage and 4.0 swings a second — <see cref="Gravecaller_BoneBoltIsTheRuledWeapon"/>
/// reads them off the file, <c>TimeToKillTests</c> writes them out, and a retune that moved one and
/// not the other reddens the other. M3-12c's ruling 2, one milestone on.
/// </para>
/// <para>
/// <b>Nothing here plays a run</b>, because nothing can: <c>PendingRun.CharacterId</c> is written by
/// a menu that offers one class until M5-07 (rule 9). What the fixture can say is that the asset
/// converts, that the boot catalog carries it, and that every number on it is the one that was
/// argued for.
/// </para>
/// </remarks>
[TestFixture]
public sealed class GravecallerTests
{
    private const string GravecallerPath = "Assets/_Project/Data/Characters/Gravecaller.asset";
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";
    private const string HuskPath = "Assets/_Project/Data/Enemies/Husk.asset";
    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";

    private const string GravecallerId = "character.gravecaller";
    private const string OathboundId = "character.oathbound";

    /// <summary>Rule 1's ruled band: the Oathbound's floor and the Emberwright's ceiling.</summary>
    /// <remarks>
    /// 3.0 is what <c>Oathbound.asset</c> has carried since the owner's M2-03 retune and 3.4 is
    /// 6.2 × (3 / 5.4) rounded to a decimal — the same factor, applied to the row GD §6.1 still
    /// published. <c>Emberwright.asset</c> carries it as of M6-07a, and
    /// <c>EmberwrightTests.Emberwright_SpeedIsTheTopOfTheRuledBand</c> reads it off the file.
    /// </remarks>
    private const float BandFloor = 3.0f;

    private const float BandCeiling = 3.4f;

    /// <summary>
    /// Tight enough that no two of the class's numbers could satisfy each other's assertion, loose
    /// enough to survive Unity writing a float back as decimal text —
    /// <c>CharacterDefinitionTests</c>' tolerance, for its reason.
    /// </summary>
    private const float Tolerance = 1e-6f;

    // ---- Rule 9 and the catalog -------------------------------------------------------------------

    [Test]
    public void Gravecaller_ConvertsAndIsInTheCatalog()
    {
        CharacterSpec spec = Gravecaller();

        Assert.That(spec.Id.Value, Is.EqualTo(GravecallerId));
        Assert.That(spec.NameKey.Key, Is.EqualTo("character.gravecaller.name"));

        // Built off BootScope.prefab's own list rather than off the two paths above, because the
        // claim is that the *boot* catalog carries it: an asset in Data/ that nobody dragged onto
        // the scope converts perfectly and is not in the game (M3-12c's Boot_ rows, same shape).
        ContentCatalog catalog = BootCatalog();

        // Three since M6-07a, which authored the Emberwright onto the same list.
        Assert.That(catalog.Characters.Count, Is.EqualTo(3), "CH §3's roster of three boots.");

        Assert.That(catalog.Character(new ContentId(GravecallerId)).Id.Value, Is.EqualTo(GravecallerId));
        Assert.That(catalog.Character(new ContentId(OathboundId)).Id.Value, Is.EqualTo(OathboundId));
    }

    // ---- Rule 4: the class -------------------------------------------------------------------------

    [Test]
    public void Gravecaller_IsEightyHitPointsAndNoShield()
    {
        CharacterSpec spec = Gravecaller();

        Assert.That(spec.MaxHp, Is.EqualTo(80f).Within(Tolerance), "CH §3's class table: 80 HP.");

        Assert.That(spec.Shield, Is.Null,
            "CH §3.1 calls the Aegis the only regeneration in the game, so a second class with one "
                + "would make that sentence false. A shield max of 0 on the asset is how it says "
                + "so — never a ShieldSpec full of zeroes.");

        Assert.That(spec.HitIFrames, Is.EqualTo(0.5f).Within(Tolerance),
            "GD §6.1's hit i-frames are a game constant, not a class one.");
    }

    [Test]
    public void Gravecaller_SpeedIsTheRuledBand()
    {
        CharacterSpec spec = Gravecaller();

        Assert.That(spec.Movement.Speed, Is.EqualTo(3.1f).Within(Tolerance),
            "Rule 1: 5.6 × (3 / 5.4), to a decimal.");

        // Strictly between, which is the half of rule 1 that a single pinned number cannot say:
        // the design wants the tank slowest and the wizard fastest, and the scaling preserved that
        // ordering exactly. 5.6 would have been 1.87× the class the whole game was tuned around.
        Assert.That(spec.Movement.Speed, Is.GreaterThan(BandFloor),
            "The Gravecaller is faster than the Oathbound.");

        Assert.That(spec.Movement.Speed, Is.LessThan(BandCeiling),
            "…and slower than the Emberwright's ruled 3.4.");

        Assert.That(Oathbound().Movement.Speed, Is.EqualTo(BandFloor).Within(Tolerance),
            "The Oathbound does not move: the owner's retune is the measurement and the documents "
                + "were the stale copy.");
    }

    [Test]
    public void Speed_EveryClassOutrunsEveryEnemy()
    {
        // GD §6.1's row is a range and its *purpose* is a rule — "every class must feel faster than
        // almost every enemy". Asserted as the ratio rather than as a pair of pinned numbers,
        // because a retune of either side is allowed to move both numbers and is not allowed to
        // move this: at 3.1 against the Spitter's 2.8 the margin is 1.11×, the thinnest in the game.
        var problems = new List<string>();

        foreach (CharacterSpec character in EveryCharacter())
        {
            foreach (EnemySpec enemy in EveryEnemy())
            {
                float ratio = character.Movement.Speed / enemy.MoveSpeed;

                if (ratio <= 1f)
                {
                    problems.Add(
                        $"{character.Id} moves at {character.Movement.Speed} m/s against "
                            + $"{enemy.Id}'s {enemy.MoveSpeed} — a ratio of {ratio:0.###}.");
                }
            }
        }

        ContentValidationTests.AssertNoProblems(problems, "GD §6.1: every class outruns every enemy");
    }

    [Test]
    public void Gravecaller_TargetingAndFocusMatchTheOathbound()
    {
        // A decision, not a copy (rule 4): CC §3 is one targeting system for every class, so a
        // per-class acquire range would be a second tuning surface with no design behind it. Pinned
        // field by field so that the day one of them legitimately differs, it differs on purpose.
        CharacterSpec gravecaller = Gravecaller();
        CharacterSpec oathbound = Oathbound();

        Assert.That(gravecaller.Targeting.AcquireRange, Is.EqualTo(oathbound.Targeting.AcquireRange).Within(Tolerance));
        Assert.That(gravecaller.Targeting.DistanceWeight, Is.EqualTo(oathbound.Targeting.DistanceWeight).Within(Tolerance));
        Assert.That(gravecaller.Targeting.EliteBonus, Is.EqualTo(oathbound.Targeting.EliteBonus).Within(Tolerance));
        Assert.That(gravecaller.Targeting.FinisherBonus, Is.EqualTo(oathbound.Targeting.FinisherBonus).Within(Tolerance));
        Assert.That(gravecaller.Targeting.Hysteresis, Is.EqualTo(oathbound.Targeting.Hysteresis).Within(Tolerance));
        Assert.That(gravecaller.Targeting.Cadence, Is.EqualTo(oathbound.Targeting.Cadence).Within(Tolerance));

        Assert.That(gravecaller.Focus.Delay, Is.EqualTo(oathbound.Focus.Delay).Within(Tolerance));
        Assert.That(gravecaller.Focus.RampTime, Is.EqualTo(oathbound.Focus.RampTime).Within(Tolerance));
        Assert.That(gravecaller.Focus.MaxMultiplier, Is.EqualTo(oathbound.Focus.MaxMultiplier).Within(Tolerance));

        // And the one number that makes the shared acquire range mean something different for this
        // class: the Censer reaches 8 m out of a 12 m sight, the Bone Bolt reaches all 12.
        Assert.That(gravecaller.Weapon.Range, Is.EqualTo(gravecaller.Targeting.AcquireRange).Within(Tolerance),
            "Rule 3: a ranged class whose reach is shorter than its sight would refuse shots for a "
                + "reason no player can see.");
    }

    // ---- Rule 3: the weapon --------------------------------------------------------------------------

    [Test]
    public void Gravecaller_BoneBoltIsTheRuledWeapon()
    {
        WeaponSpec bolt = Gravecaller().Weapon;

        Assert.That(bolt.Kind, Is.EqualTo(WeaponKind.Projectile), "CH §3.2, and M5-01 rule 1.");
        Assert.That(bolt.Damage, Is.EqualTo(9f).Within(Tolerance), "Rule 2 — CH §3.2 says 7.");
        Assert.That(bolt.SwingsPerSecond, Is.EqualTo(4f).Within(Tolerance), "CH §3.2.");
        Assert.That(bolt.Range, Is.EqualTo(12f).Within(Tolerance), "Ours: it matches the acquire range.");
        Assert.That(bolt.ConeAngleDeg, Is.EqualTo(360f).Within(Tolerance),
            "M5-01 rule 2's one spelling of 'the angle does not gate this weapon'.");
        Assert.That(bolt.DamageFrame, Is.EqualTo(0.15f).Within(Tolerance),
            "Ours: 0.15 of a 0.25 s interval is 37 ms — a release, not a windup.");
        Assert.That(bolt.ShotSpeed, Is.EqualTo(40f).Within(Tolerance),
            "Ours: the quickest thing in the air, against the Spitter's 12.");
        Assert.That(bolt.ShotRadius, Is.EqualTo(0.8f).Within(Tolerance),
            "Ours: half the Spitter's 1.6 — a player's shot forgives less (GD §8.1, CC §3.7).");
        Assert.That(bolt.ShotSpread, Is.Zero, "Reserved (M5-01): a spread is a change to a seed.");
    }

    // ---- Rule 7: the member, and as of M5-03 the behaviour behind it ---------------------------------

    [Test]
    public void Gravecaller_MovementIsAShroudstepThatIsNotYetOne()
    {
        MovementSkillSpec step = Gravecaller().MovementSkill;

        Assert.That(step.Kind, Is.EqualTo(MovementSkillKind.Shroudstep),
            "The kind is content identity and lands with the class; what it does is M5-03's.");

        Assert.That(step.DecoyDuration, Is.EqualTo(3f).Within(Tolerance),
            "CH §3.2: the corpse taunts for three seconds. Authored here by M5-03, and validated "
                + "against the kind — a Shroudstep with no duration is refused at the spec's door.");

        Assert.That(step.Distance, Is.EqualTo(6f).Within(Tolerance), "CH §3.2: a 6 m blink.");
        Assert.That(step.Duration, Is.EqualTo(0.05f).Within(Tolerance), "Ours: a blink, not a dash.");
        Assert.That(step.Cooldown, Is.EqualTo(2.5f).Within(Tolerance),
            "The Charge's clock — the decoy is the payload, not a shorter cooldown.");

        // The two zeroes are the whole of rule 7's honesty, and M5-03 left them alone: PlayerCombat
        // still builds a ChargeSkill from this spec whatever the kind says, and the kind selects a
        // *payload* on the start edge. So a Gravecaller blinks 6 m, damages nothing on the way, and
        // leaves a corpse — rather than being a Charge in disguise dealing 20 damage down a
        // corridor nobody authored.
        Assert.That(step.Damage, Is.Zero, "A blink hits nothing on the way.");
        Assert.That(step.Knockback, Is.Zero, "…and shoves nothing.");

        Assert.That(step.IFrameTrail, Is.EqualTo(0.05f).Within(Tolerance), "CC §5's touch-latency trail.");
        Assert.That(step.InputBuffer, Is.EqualTo(0.15f).Within(Tolerance), "CC §5's input buffer.");
    }

    // ---- Rules 5 and 6: the minion block -------------------------------------------------------------

    [Test]
    public void Minions_AreTheAuthoredBlock()
    {
        MinionSpec wight = Gravecaller().Minions;

        Assert.That(wight, Is.Not.Null, "CH §3.2's Rise is the class's signature.");

        Assert.That(wight.SpecId.Value, Is.EqualTo("minion.wight"));
        Assert.That(wight.NameKey.Key, Is.EqualTo("minion.wight.name"));

        Assert.That(wight.Cap, Is.EqualTo(3), "CH §3.2: base cap 3.");
        Assert.That(wight.Lifespan, Is.EqualTo(20f).Within(Tolerance), "CH §3.2: 20 s.");
        Assert.That(wight.RiseChance, Is.EqualTo(0.25f).Within(Tolerance),
            "CH §3.2: 25 % of enemies killed — a fraction, never a percentage.");

        // The five with no document behind them (rule 5), stated here so M5-08's playtest knows
        // what it is looking at: they are a first authoring, and whether 25 % at a cap of three
        // reads as an army or as an occasional friend is what a played minute decides.
        Assert.That(wight.MaxHp, Is.EqualTo(20f).Within(Tolerance), "Ours: three Husk hits.");
        Assert.That(wight.MoveSpeed, Is.EqualTo(3f).Within(Tolerance), "Ours: 1.5× the Husk's 2.");
        Assert.That(wight.Damage, Is.EqualTo(8f).Within(Tolerance), "Ours.");
        Assert.That(wight.AttackInterval, Is.EqualTo(1f).Within(Tolerance), "Ours: 8 DPS each.");
        Assert.That(wight.Reach, Is.EqualTo(1.5f).Within(Tolerance), "Ours: the Husk's own 1.5 m.");
    }

    [Test]
    public void Minions_AreNullOnTheOathbound()
    {
        Assert.That(Oathbound().Minions, Is.Null,
            "Rule 6: null exactly as the Gravecaller's shield is, because a zeroed block would have "
                + "to be read against the class id to be understood.");

        // And the asset itself carries no minions, whether or not Unity has ever re-serialised it.
        // M4-01a's measured finding is that it does not rewrite an asset because the script that
        // reads it grew a field — so the shipped Oathbound.asset has no `_minionCap` line at all,
        // and the day it acquires one that line must still read 0. Written as the second of those
        // rather than the first, because a re-serialised asset is correct content and a row that
        // reddened for it would be reporting a diff rather than a defect.
        foreach (string line in File.ReadAllLines(OathboundPath))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("_minionCap:", StringComparison.Ordinal))
            {
                Assert.That(trimmed, Is.EqualTo("_minionCap: 0"),
                    "Oathbound.asset carries a minion cap above zero, which would give the starter "
                        + "class the Gravecaller's signature.");
            }
        }
    }

    [Test]
    public void Minion_CatchesAHusk()
    {
        // Rule 5's two claims, against the asset each is measured from — so a retune of either
        // reddens this rather than quietly making a Wight a slower, weaker thing than it reads as.
        MinionSpec wight = Gravecaller().Minions;
        EnemySpec husk = Husk();

        Assert.That(wight.MoveSpeed / husk.MoveSpeed, Is.GreaterThan(1f),
            "A minion sent at something has to be able to reach it — the same margin GD §6.1 asks "
                + "the player to have.");

        Assert.That(Hits(husk.MaxHp, wight.Damage), Is.EqualTo(5),
            "Five hits to kill a stage-1 Husk: the class's damage is most of a second weapon, not "
                + "a replacement for the first.");

        Assert.That(Hits(wight.MaxHp, husk.ContactDamage), Is.EqualTo(3),
            "And three the other way — a Wight is a timer rather than a wall.");
    }

    // ---- Traps §5 and rule 10 -------------------------------------------------------------------------

    [Test]
    public void Assets_AreLinkedToAMonoScript()
    {
        // The one failure mode that reports nothing anywhere: a ScriptableObject whose script
        // reference did not bind loads as null, with no error in the Console and no red anywhere
        // else. Every authored asset in this project owes this row.
        var asset = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(GravecallerPath);

        Assert.That(asset, Is.Not.Null, $"{GravecallerPath} did not load as a CharacterDefinition.");

        MonoScript script = MonoScript.FromScriptableObject(asset);

        Assert.That(script, Is.Not.Null,
            $"{GravecallerPath}: m_Script does not resolve (Traps §5).");

        Assert.That(script.GetClass(), Is.EqualTo(typeof(CharacterDefinition)));
    }

    [Test]
    public void Keys_AreAuthoredAndResolve()
    {
        CharacterSpec spec = Gravecaller();
        TableLocalizer english = ContentValidationTests.English();

        LocKey name = spec.NameKey;
        LocKey wight = spec.Minions.NameKey;

        Assert.That(name.Key, Is.EqualTo("character.gravecaller.name"));
        Assert.That(wight.Key, Is.EqualTo("minion.wight.name"));

        // **Rule 10 shipped one row fewer than it could.** It rules both keys unresolved until
        // M6-10 owns the table, and ContentValidationTests.EveryLocKey_ResolvesInEnglish sweeps
        // every CharacterDefinition's name key — so an unresolved `character.gravecaller.name`
        // is a red row in another fixture rather than a deferral. It ships with an English row;
        // the finding was reported at M5-00b rather than edited into the spec, and this is where
        // it is spent.
        Assert.That(english.Has(name), Is.True,
            "character.gravecaller.name has an English row, because the content sweep demands one.");

        // **M6-10 added it deliberately, which is what this row asked for.** Until then the minion
        // key was reached by nothing that sweeps and shipped unresolved as rule 10 intended, with the
        // absence asserted here. M6-10's LocalisationSweepTests found the walk stopping short, so
        // EveryAuthoredKey now takes a character's description and its minion's name too, and
        // English carries "Wight" — for a word no screen draws yet.
        Assert.That(english.Has(wight), Is.True,
            "minion.wight.name has an English row as of M6-10, and EveryAuthoredKey sweeps it.");
    }

    // ---- Fixtures -------------------------------------------------------------------------------------

    /// <summary>Whole hits, because a thing at one hit point is still standing.</summary>
    private static int Hits(float hp, float damagePerHit) =>
        (int)Math.Ceiling(hp / damagePerHit);

    private static CharacterSpec Gravecaller() => Character(GravecallerPath);

    private static CharacterSpec Oathbound() => Character(OathboundPath);

    private static CharacterSpec Character(string path)
    {
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

        Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {path}.");

        return definition.ToSpec();
    }

    private static EnemySpec Husk()
    {
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(HuskPath);

        Assert.That(definition, Is.Not.Null, $"No EnemyDefinition at {HuskPath}.");

        return definition.ToSpec();
    }

    /// <summary>
    /// Every class on disk, through <c>ContentValidationTests</c>' sweep rather than through the
    /// two paths above: a third class authored and not added here would otherwise go unmeasured by
    /// the one row that is about the whole band.
    /// </summary>
    private static IEnumerable<CharacterSpec> EveryCharacter()
    {
        foreach (string path in ContentValidationTests.PathsOf<CharacterDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

            if (asset != null)
            {
                yield return asset.ToSpec();
            }
        }
    }

    /// <summary>Every archetype on disk, for <see cref="EveryCharacter"/>'s reason.</summary>
    private static IEnumerable<EnemySpec> EveryEnemy()
    {
        foreach (string path in ContentValidationTests.PathsOf<EnemyDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);

            if (asset != null)
            {
                yield return asset.ToSpec();
            }
        }
    }

    /// <summary>
    /// A catalog built from <c>BootScope.prefab</c>'s own <c>_characters</c> list — what the app
    /// actually boots with, rather than what is on disk.
    /// </summary>
    private static ContentCatalog BootCatalog()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BootScopePath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {BootScopePath}.");

        var scope = prefab.GetComponent<Soulvail.Game.Composition.BootScope>();

        Assert.That(scope, Is.Not.Null, "BootScope did not load off its own prefab (Traps §5).");

        SerializedProperty characters = new SerializedObject(scope).FindProperty("_characters");
        var specs = new List<CharacterSpec>(characters.arraySize);

        for (int i = 0; i < characters.arraySize; i++)
        {
            var definition = characters.GetArrayElementAtIndex(i).objectReferenceValue
                as CharacterDefinition;

            Assert.That(definition, Is.Not.Null, $"_characters[{i}] is an empty slot.");

            specs.Add(definition.ToSpec());
        }

        return new ContentCatalog(specs);
    }
}
