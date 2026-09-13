using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// Covers the mode authoring→spec conversion and, through <see cref="Descent_MatchesDesign"/>,
/// pins the shipped Descent to GD §4.5 and §8.2 — so a stray Inspector edit that made the run
/// finite, or moved the first stage, is a red test rather than a mystery on a phone.
/// </summary>
/// <remarks>
/// The same shape as <see cref="EnemyDefinitionTests"/> and
/// <see cref="CharacterDefinitionTests"/>, deliberately: fixtures built with
/// <see cref="ScriptableObject.CreateInstance{T}()"/> and poked through
/// <see cref="SerializedObject"/>, because the fields are <c>[SerializeField] private</c> and
/// their <c>[Min]</c> attributes only clamp the Inspector GUI — a <c>SerializedProperty</c> write
/// goes straight past them, which is the hole <c>ToSpec</c> exists to close (M0-11).
/// </remarks>
[TestFixture]
public sealed class ModeDefinitionTests
{
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";

    private readonly List<ModeDefinition> _created = new List<ModeDefinition>();

    [TearDown]
    public void DestroyCreatedInstances()
    {
        foreach (ModeDefinition definition in _created)
        {
            if (definition != null)
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        _created.Clear();
    }

    [Test]
    public void Descent_MatchesDesign()
    {
        var definition = AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath);
        Assert.That(definition, Is.Not.Null, $"No ModeDefinition at {DescentPath}.");

        ModeSpec spec = definition.ToSpec();

        // The asset's file name matches the last segment of its id — the project's asset-naming
        // rule, and what makes Descent.asset findable from a save file that holds "mode.descent".
        Assert.That(spec.Id.Value, Is.EqualTo("mode.descent"));
        Assert.That(spec.NameKey.Key, Is.EqualTo("mode.descent.name"));

        // GD §4.5: "V1 ships exactly one mode: Descent — the endless run. No win condition; the
        // score is depth reached." Both halves are load-bearing — a finite Descent would end a run
        // at a stage nobody designed an ending for.
        Assert.That(spec.IsEndless, Is.True, "GD §4.5: Descent is endless.");
        Assert.That(spec.FinalStage, Is.EqualTo(int.MaxValue));
        Assert.That(spec.StartingStage, Is.EqualTo(1), "GD §8.2's schedule starts at stage 1.");

        // GD §8.2's schedule, all three rows, as of M2-06 — which is the task that authored the
        // Spitter and the Bloater and added them here in the same change (M2-02 rule 10). Until
        // then this row asserted the Husk alone, because RunSession.Start resolves every roster id
        // against the catalog before it announces a run: a row naming an archetype nobody had
        // authored would have refused to start the game.
        //
        // Order is meaningful — ModeSpec.RosterFor answers in it — so the rows are asserted by
        // index rather than searched for.
        Assert.That(spec.Roster.Count, Is.EqualTo(3), "GD §8.2: Husk, Spitter and Bloater.");

        Assert.That(spec.Roster[0].SpecId.Value, Is.EqualTo("enemy.husk"));
        Assert.That(spec.Roster[0].IntroducedAtStage, Is.EqualTo(1), "GD §8.2: Husk at stage 1.");

        Assert.That(spec.Roster[1].SpecId.Value, Is.EqualTo("enemy.spitter"));
        Assert.That(spec.Roster[1].IntroducedAtStage, Is.EqualTo(2), "GD §8.2: Spitter at stage 2.");

        Assert.That(spec.Roster[2].SpecId.Value, Is.EqualTo("enemy.bloater"));
        Assert.That(spec.Roster[2].IntroducedAtStage, Is.EqualTo(4), "GD §8.2: Bloater at stage 4.");
    }

    [Test]
    public void Descent_CarriesDesignScaling()
    {
        // GD §12's numbers as the shipped mode holds them. Every expectation is computed from the
        // formula rather than copied from a table — and B(40) is why that matters: GD §12.1's own
        // table says 1,772 where the formula gives 1,876.9, and the row is the error (M2-03 rule
        // 1, still to be corrected in the design doc by the owner).
        //
        // Traps §7 applies to this whole row: ScalingBlock's C# initialisers are these same
        // numbers, so a YAML key that bound to nothing would leave the field holding the value
        // this asserts and the row would pass anyway. Descent_EveryYamlKeyBindsToAField is what
        // closes that hole, for this block along with every other field.
        var definition = AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath);
        Assert.That(definition, Is.Not.Null, $"No ModeDefinition at {DescentPath}.");

        ScalingSpec scaling = definition.ToSpec().Scaling;

        Assert.That(scaling.Budget.At(1), Is.EqualTo(40f).Within(0.05f));
        Assert.That(scaling.Budget.At(10), Is.EqualTo(220.9f).Within(0.05f));
        Assert.That(scaling.Budget.At(40), Is.EqualTo(1876.9f).Within(0.05f));

        Assert.That(scaling.Waves.At(1), Is.EqualTo(2));
        Assert.That(scaling.Waves.At(15), Is.EqualTo(5));
        Assert.That(scaling.Waves.At(40), Is.EqualTo(5), "GD §12.2 clamps at 5.");

        // The device cap is not authored — it is a fact about the phone (GD §11.1), so the curve
        // is asked with one rather than holding one.
        Assert.That(scaling.Concurrency.At(1, 28), Is.EqualTo(10));
        Assert.That(scaling.Concurrency.At(20, 28), Is.EqualTo(20));
        Assert.That(scaling.Concurrency.At(80, 28), Is.EqualTo(28));

        Assert.That(scaling.Hp.At(40), Is.EqualTo(3.34f).Within(1e-4f));
        Assert.That(scaling.Hp.At(99), Is.EqualTo(4f).Within(1e-4f), "GD §12.3's soft cap.");
        Assert.That(scaling.Damage.At(20), Is.EqualTo(1.665f).Within(1e-4f));
        Assert.That(scaling.Damage.At(99), Is.EqualTo(3f).Within(1e-4f), "GD §12.3's hard cap.");
        Assert.That(scaling.Speed.At(4), Is.EqualTo(1f).Within(1e-4f));
        Assert.That(scaling.Speed.At(5), Is.EqualTo(1.02f).Within(1e-4f), "Steps every fifth stage.");
        Assert.That(scaling.Speed.At(99), Is.EqualTo(1.3f).Within(1e-4f));
    }

    [Test]
    public void ToSpec_CapBelowOne_ThrowsNamingAsset()
    {
        // The mis-authoring with no symptom: a cap of 0.5 halves every deep enemy's hit points and
        // every other number in the game still looks right. [Min(1f)] clamps the Inspector GUI and
        // nothing else (Traps §5), which is why StatCurve carries the real guard — and why this
        // row writes the value through SerializedObject, which goes straight past the attribute.
        ModeDefinition definition = NewDefinition("BrokenHpCap");

        SetString(definition, "_id", "mode.broken");
        SetString(definition, "_nameKey", "mode.broken.name");
        SetFloat(definition, "_scaling._hp._cap", 0.5f);

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.Contain("BrokenHpCap"));
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ToSpec_ZeroWaveStep_ThrowsNamingAsset()
    {
        // A zero step would be a division by zero on the first stage composed — an exception from
        // inside a curve, one call stack away from the asset that was actually wrong.
        ModeDefinition definition = NewDefinition("BrokenWaveStep");

        SetString(definition, "_id", "mode.broken");
        SetString(definition, "_nameKey", "mode.broken.name");
        SetInt(definition, "_scaling._waveStagesPerStep", 0);

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.Contain("BrokenWaveStep"));
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Descent_ToSpec_ReturnsNewInstanceEachCall()
    {
        var definition = AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath);
        Assert.That(definition, Is.Not.Null, $"No ModeDefinition at {DescentPath}.");

        ModeSpec first = definition.ToSpec();
        ModeSpec second = definition.ToSpec();

        // ADR-0006: the SO holds no runtime state. A cached spec handed out twice would be one
        // object shared by two runs.
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second.Id, Is.EqualTo(first.Id));
    }

    [Test]
    public void Descent_EveryYamlKeyBindsToAField()
    {
        // Traps §7: a serialized field whose C# initialiser equals the value the asset ships makes
        // an "the asset carries these values" test vacuous — it passes identically if the YAML key
        // binds to nothing at all. Two of Descent's five are in exactly that position:
        // `_startingStage` initialises to 1 and ships 1, `_isEndless` initialises to true and
        // ships true, so the row above cannot tell a bound key from a misspelled one for either.
        //
        // ForceReserializeAssets drops keys matching no field, so a reserialise that changes the
        // file is a key that did not bind. Comparing the text either side is the assertion — and
        // it leaves no diff behind when it passes, which a bare reserialise would not (M1-03).
        string before = System.IO.File.ReadAllText(DescentPath);

        AssetDatabase.ForceReserializeAssets(new[] { DescentPath });

        string after = System.IO.File.ReadAllText(DescentPath);

        Assert.That(after, Is.EqualTo(before),
            "Reserialising Descent.asset changed it, which means at least one of its YAML keys " +
            "matches no field on ModeDefinition and was dropped. Compare the two and fix the " +
            "name — a dropped key reads as a field quietly holding its C# initialiser.");
    }

    [Test]
    public void Definition_ToSpec_RoundTrip()
    {
        ModeDefinition definition = NewDefinition("RoundTripMode");

        SetString(definition, "_id", "mode.trial");
        SetString(definition, "_nameKey", "mode.trial.name");
        SetInt(definition, "_startingStage", 3);
        SetBool(definition, "_isEndless", false);
        SetInt(definition, "_finalStage", 12);
        SetRoster(definition, new[] { ("enemy.husk", 3), ("enemy.spitter", 5) });

        ModeSpec spec = definition.ToSpec();

        Assert.That(spec.Id.Value, Is.EqualTo("mode.trial"));
        Assert.That(spec.NameKey.Key, Is.EqualTo("mode.trial.name"));
        Assert.That(spec.StartingStage, Is.EqualTo(3));
        Assert.That(spec.IsEndless, Is.False);
        Assert.That(spec.FinalStage, Is.EqualTo(12));

        Assert.That(spec.Roster.Count, Is.EqualTo(2));
        Assert.That(spec.Roster[0].SpecId.Value, Is.EqualTo("enemy.husk"));
        Assert.That(spec.Roster[0].IntroducedAtStage, Is.EqualTo(3));
        Assert.That(spec.Roster[1].SpecId.Value, Is.EqualTo("enemy.spitter"));
        Assert.That(spec.Roster[1].IntroducedAtStage, Is.EqualTo(5));
    }

    [Test]
    public void ToSpec_EndlessIgnoresFinalStage()
    {
        // The one place the conversion has an opinion of its own worth pinning: an endless mode
        // that also carries a small final stage is not a contradiction to refuse, it is a
        // designer who toggled Endless on and left the number below alone.
        ModeDefinition definition = NewDefinition("EndlessMode");

        SetString(definition, "_id", "mode.endless");
        SetString(definition, "_nameKey", "mode.endless.name");
        SetBool(definition, "_isEndless", true);
        SetInt(definition, "_finalStage", 2);

        ModeSpec spec = definition.ToSpec();

        Assert.That(spec.FinalStage, Is.EqualTo(int.MaxValue));
        Assert.That(spec.HasStage(9999), Is.True);
    }

    [Test]
    public void AllModeDefinitions_ValidUniqueIds()
    {
        string[] guids = AssetDatabase.FindAssets($"t:{nameof(ModeDefinition)}");

        Assert.That(guids, Is.Not.Empty, "The project ships at least Descent.");

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var definition = AssetDatabase.LoadAssetAtPath<ModeDefinition>(path);

            Assert.That(definition, Is.Not.Null, $"{path} did not load as a ModeDefinition.");

            ModeSpec spec = null;
            Assert.That(() => spec = definition.ToSpec(), Throws.Nothing, $"{path} is not valid content.");

            Assert.That(seen.Add(spec.Id.Value), Is.True,
                $"{path} repeats the id '{spec.Id.Value}'. Two modes under one id means the " +
                "catalog refuses to build at all — ContentCatalog throws on a duplicate.");
        }
    }

    [Test]
    public void AllModeDefinitions_RosterIdsAreAuthored()
    {
        // The failure this catches is the one M2-02 rule 10 is about, one step earlier than
        // RunSession.Start catches it: a roster naming an archetype no EnemyDefinition authors
        // makes the mode unstartable, and the Console message at boot would be about a run rather
        // than about the asset. When M2-06 adds the Spitter and the Bloater to Descent, this row
        // is what says whether it added them in the right order — the assets first.
        var authored = new HashSet<string>(StringComparer.Ordinal);

        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(EnemyDefinition)}"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            authored.Add(AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec().Id.Value);
        }

        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(ModeDefinition)}"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ModeSpec spec = AssetDatabase.LoadAssetAtPath<ModeDefinition>(path).ToSpec();

            foreach (RosterEntry entry in spec.Roster)
            {
                Assert.That(authored, Does.Contain(entry.SpecId.Value),
                    $"{path} rosters '{entry.SpecId}', which no EnemyDefinition authors. A run " +
                    "of this mode would refuse to start.");
            }
        }
    }

    [Test]
    public void ToSpec_InvalidId_ThrowsNamingAsset()
    {
        ModeDefinition definition = NewDefinition("BrokenModeId");

        // Setting a bad id also trips OnValidate's warning, which this row deliberately does not
        // assert — that rule is owned by the row below alone (M0-11).
        SetString(definition, "_id", "Mode Descent");

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.Contain("BrokenModeId"),
            "The message must name the asset, or the Console points at no file a designer can open.");
    }

    [Test]
    public void ToSpec_TwoIntroductionsAtOneStage_ThrowsNamingAsset()
    {
        // GD §8.2's rule, reaching a designer through the asset name rather than through a stack
        // trace into the boot installer. [Min] does not police this and could not: it is a
        // relation between rows, not a bound on one.
        ModeDefinition definition = NewDefinition("BrokenSchedule");

        SetString(definition, "_id", "mode.broken");
        SetString(definition, "_nameKey", "mode.broken.name");
        SetRoster(definition, new[] { ("enemy.spitter", 2), ("enemy.bloater", 2) });

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.Contain("BrokenSchedule"));
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentException>());
    }

    [Test]
    public void ToSpec_FinalStageBeforeStart_ThrowsNamingAsset()
    {
        ModeDefinition definition = NewDefinition("BrokenStages");

        SetString(definition, "_id", "mode.broken");
        SetString(definition, "_nameKey", "mode.broken.name");
        SetBool(definition, "_isEndless", false);
        SetInt(definition, "_startingStage", 5);
        SetInt(definition, "_finalStage", 4);

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.Contain("BrokenStages"));
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ToSpec_RosterStageBelowOne_ThrowsNamingAsset()
    {
        // [Min(1)] clamps the Inspector and nothing else, which is the whole reason RosterEntry
        // carries the guard as well.
        ModeDefinition definition = NewDefinition("BrokenRosterStage");

        SetString(definition, "_id", "mode.broken");
        SetString(definition, "_nameKey", "mode.broken.name");
        SetRoster(definition, new[] { ("enemy.husk", 0) });

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.Contain("BrokenRosterStage"));
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void OnValidate_InvalidId_LogsWarningNamingAsset()
    {
        ModeDefinition definition = NewDefinition("WarnsInInspector");

        // LogAssert fails at teardown if this warning never arrives, which is what makes the row
        // load-bearing: a silent OnValidate would go unnoticed otherwise, since a stray warning
        // does not fail a test on its own.
        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("WarnsInInspector")));

        SetString(definition, "_id", "Mode.Descent");
    }

    private ModeDefinition NewDefinition(string assetName)
    {
        var definition = ScriptableObject.CreateInstance<ModeDefinition>();

        // CreateInstance leaves `name` empty, and an empty name makes Does.Contain pass against
        // any message at all — the assertion would prove nothing (M0-11).
        definition.name = assetName;
        _created.Add(definition);
        return definition;
    }

    private static void SetString(ModeDefinition definition, string field, string value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <remarks>
    /// Takes a dotted path as well as a bare field name — see <see cref="SetFloat"/>.
    /// </remarks>
    private static void SetInt(ModeDefinition definition, string path, int value)
    {
        var serialized = new SerializedObject(definition);
        SerializedProperty property = serialized.FindProperty(path);

        Assert.That(property, Is.Not.Null, $"No serialized property at '{path}'.");

        property.intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <remarks>
    /// Takes a dotted path — <c>"_scaling._hp._cap"</c> — because the curves live in nested
    /// <c>[Serializable]</c> types a test cannot name: <c>ScalingBlock</c> and
    /// <c>StatCurveRow</c> are both private. <c>FindProperty</c> understands the path, which is
    /// the only route in.
    /// </remarks>
    private static void SetFloat(ModeDefinition definition, string path, float value)
    {
        var serialized = new SerializedObject(definition);
        SerializedProperty property = serialized.FindProperty(path);

        Assert.That(property, Is.Not.Null, $"No serialized property at '{path}'.");

        property.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetBool(ModeDefinition definition, string field, bool value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Writes the roster array through <see cref="SerializedObject"/>, which is the only way in:
    /// <c>RosterRow</c> is a private nested type, so a test cannot name it.
    /// </summary>
    private static void SetRoster(ModeDefinition definition, (string SpecId, int Stage)[] rows)
    {
        var serialized = new SerializedObject(definition);
        SerializedProperty roster = serialized.FindProperty("_roster");
        roster.arraySize = rows.Length;

        for (int i = 0; i < rows.Length; i++)
        {
            SerializedProperty row = roster.GetArrayElementAtIndex(i);
            row.FindPropertyRelative("_specId").stringValue = rows[i].SpecId;
            row.FindPropertyRelative("_introducedAtStage").intValue = rows[i].Stage;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
