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
/// Covers the enemy authoring→spec conversion and, through
/// <see cref="Husk_ToSpec_MatchesGameDesign"/>, pins the shipped Husk to GD §8.1 and to M1-05's
/// five invented numbers, so a stray Inspector drag is a red test rather than a balance mystery.
/// </summary>
/// <remarks>
/// The same shape as <see cref="CharacterDefinitionTests"/>, deliberately: the invalid-field
/// fixtures are built with <see cref="ScriptableObject.CreateInstance{T}()"/> and poked through
/// <see cref="SerializedObject"/>, because the fields are <c>[SerializeField] private</c> and
/// their <c>[Min]</c> and <c>[Range]</c> attributes only clamp the Inspector GUI — a
/// <c>SerializedProperty</c> write goes straight past them, which is exactly the hole
/// <c>ToSpec</c> exists to close (M0-11).
/// </remarks>
[TestFixture]
public sealed class EnemyDefinitionTests
{
    private const string HuskPath = "Assets/_Project/Data/Enemies/Husk.asset";

    /// <summary>
    /// Tight enough that no two of the Husk's numbers could satisfy each other's assertion,
    /// loose enough to survive Unity writing a float back as decimal text.
    /// </summary>
    private const float Tolerance = 1e-6f;

    private readonly List<EnemyDefinition> _created = new List<EnemyDefinition>();

    [TearDown]
    public void DestroyCreatedInstances()
    {
        foreach (EnemyDefinition definition in _created)
        {
            if (definition != null)
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        _created.Clear();
    }

    [Test]
    public void Husk_ToSpec_MatchesGameDesign()
    {
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(HuskPath);
        Assert.That(definition, Is.Not.Null, $"No EnemyDefinition at {HuskPath}.");

        EnemySpec spec = definition.ToSpec();

        Assert.That(spec.Id.Value, Is.EqualTo("enemy.husk"));
        Assert.That(spec.NameKey.Key, Is.EqualTo("enemy.husk.name"));

        // The three numbers GD §8.1 actually publishes for the Husk.
        Assert.That(spec.MaxHp, Is.EqualTo(36f).Within(Tolerance), "GD §8.1: base HP 36.");
        Assert.That(spec.TargetPriority, Is.EqualTo(1),
            "GD §8.1: the Husk is priority 1, the floor of the 1–8 scale the Choir tops at 8.");

        // The third of them, authored from M2-04. Two ints side by side in the constructor and in
        // the YAML, so a transposition is the live risk — and 1 against 4 is what makes it visible:
        // a swapped pair would price the Husk at 1 and spawn four times as many of them, while
        // every individual number still looked plausible. Same failure mode as windup/recover below.
        Assert.That(spec.ThreatCost, Is.EqualTo(4),
            "GD §8.1: the Husk costs 4 threat, the cheapest archetype in the roster — so a " +
            "stage-1 budget of 40 buys ten of them (GD §12.1).");

        Assert.That(spec.IsElite, Is.False, "Elites and their affixes are M7-02.");

        // The five M1-05 invented, which no design document owns — so this row is the only thing
        // that cross-checks them, and the only place they are written down twice on purpose.
        // Retuned 3.5 → 2 by the owner after playtesting, alongside the Oathbound's 5.4 → 3. The
        // *reason* for the assertion survives the numbers unchanged: 3 / 2 is a 1.5× margin where
        // 5.4 / 3.5 was 1.543×, so a Husk is still outrunnable by very nearly the same amount.
        // The margin is what matters here, not the pair — if a future retune ever takes it below
        // 1, GD §6.1's "every class must feel faster than almost every enemy" is broken and this is
        // the row that has to say so.
        Assert.That(spec.MoveSpeed, Is.EqualTo(2f).Within(Tolerance),
            "M1-05, retuned: 2 m/s, slower than the Oathbound's 3 — a Husk must be outrunnable.");
        Assert.That(spec.ContactDamage, Is.EqualTo(8f).Within(Tolerance), "M1-05: 8 per strike.");
        Assert.That(spec.Reach, Is.EqualTo(1.2f).Within(Tolerance), "M1-05: reach 1.2 m, not the windup.");

        // Windup and recover are the transposition risk here — 0.4 and 0.6 are close enough that a
        // swapped pair would still telegraph plausibly, and no behaviour row would notice. Same
        // failure mode M0-07 found between accel and decel.
        Assert.That(spec.WindupTime, Is.EqualTo(0.4f).Within(Tolerance), "M1-05: windup 0.4 s, not recover.");
        Assert.That(spec.RecoverTime, Is.EqualTo(0.6f).Within(Tolerance), "M1-05: recover 0.6 s, not windup.");

        // Static until M1-18, and a Chaser from it. The dummy that held still was scaffolding —
        // it is what made targeting, cone hits and damage judgeable on their own — and this is the
        // asset edit that ends it: the Husk of GD §8.1 beelines and strikes.
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Chaser),
            "GD §8.1's Husk chases and strikes; ChaserBehaviour is what moves it (M1-18).");
    }

    [Test]
    public void Husk_ToSpec_ReturnsNewInstanceEachCall()
    {
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(HuskPath);
        Assert.That(definition, Is.Not.Null, $"No EnemyDefinition at {HuskPath}.");

        EnemySpec first = definition.ToSpec();
        EnemySpec second = definition.ToSpec();

        // ADR-0006: the SO holds no runtime state. A cached spec handed out twice would be one
        // object shared by two runs — and an EnemySpec is read by every living Husk at once.
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second.Id, Is.EqualTo(first.Id));
        Assert.That(second.MaxHp, Is.EqualTo(first.MaxHp));
    }

    [Test]
    public void Husk_EveryYamlKeyBindsToAField()
    {
        // Traps §7: a serialized field whose C# initialiser equals the value the asset ships makes
        // an "the asset carries these values" test vacuous — it passes identically if the YAML key
        // binds to nothing at all. Six of the Husk's ten are in exactly that position, because
        // EnemyDefinition's defaults *are* the Husk (M0-11), and `_threatCost` joined them in
        // M2-04: it initialises to 4 and ships 4, so the row above cannot tell a bound key from a
        // misspelled one.
        //
        // ForceReserializeAssets drops keys matching no field, so a reserialise that changes the
        // file is a key that did not bind. Comparing the text either side is the assertion — and it
        // leaves no diff behind when it passes, which a bare reserialise would not (M1-03).
        string before = System.IO.File.ReadAllText(HuskPath);

        AssetDatabase.ForceReserializeAssets(new[] { HuskPath });

        string after = System.IO.File.ReadAllText(HuskPath);

        Assert.That(after, Is.EqualTo(before),
            "Reserialising Husk.asset changed it, which means at least one of its YAML keys " +
            "matches no field on EnemyDefinition and was dropped. Compare the two and fix the " +
            "name — a dropped key reads as a field quietly holding its C# initialiser.");
    }

    [Test]
    public void AllEnemyDefinitions_ValidUniqueIds()
    {
        string[] guids = AssetDatabase.FindAssets($"t:{nameof(EnemyDefinition)}");

        Assert.That(guids, Is.Not.Empty, "The project ships at least the Husk.");

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);

            Assert.That(definition, Is.Not.Null, $"{path} did not load as an EnemyDefinition.");

            EnemySpec spec = null;
            Assert.That(() => spec = definition.ToSpec(), Throws.Nothing, $"{path} is not valid content.");

            Assert.That(seen.Add(spec.Id.Value), Is.True,
                $"{path} repeats the id '{spec.Id.Value}'. Two archetypes under one id means the " +
                "catalog refuses to build at all — ContentCatalog throws on a duplicate.");
        }
    }

    [Test]
    public void ToSpec_InvalidPriority_ThrowsNamingAsset()
    {
        EnemyDefinition definition = NewDefinition("BrokenPriority");

        // [Range(1, 8)] keeps this out of the Inspector; a SerializedProperty write goes straight
        // past it, which is the hole EnemySpec's constructor closes. A priority of 9 would
        // out-shout the Choir at 8 and make a swarm dummy the thing auto-aim never looks away from.
        SetInt(definition, "_targetPriority", 9);

        // Assert.Throws is an exact type match (M0-08), and that is the point rather than an
        // accident of it: ToSpec promises plain ArgumentException whatever the inner failure was.
        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.Contain("BrokenPriority"),
            "The message must name the asset, or the Console points at no file a designer can open.");

        // The inner exception is what says *which* field, and dropping it would leave the log
        // saying only that some asset is wrong.
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ToSpec_InvalidHp_ThrowsNamingAsset()
    {
        EnemyDefinition definition = NewDefinition("BrokenHuskHp");
        SetFloat(definition, "_maxHp", 0f);

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.Contain("BrokenHuskHp"));
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ToSpec_InvalidId_ThrowsNamingAsset()
    {
        EnemyDefinition definition = NewDefinition("BrokenEnemyId");

        // Setting a bad id also trips OnValidate's warning, which this row deliberately does not
        // assert — rule "OnValidate names the asset" is owned by the row below alone (M0-11).
        SetString(definition, "_id", "Enemy Husk");

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.Contain("BrokenEnemyId"));
    }

    [Test]
    public void OnValidate_InvalidId_LogsWarningNamingAsset()
    {
        EnemyDefinition definition = NewDefinition("WarnsInInspector");

        // LogAssert fails at teardown if this warning never arrives, which is what makes the row
        // load-bearing: a silent OnValidate would go unnoticed otherwise, since a stray warning
        // does not fail a test on its own.
        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("WarnsInInspector")));

        SetString(definition, "_id", "Enemy.Husk");
    }

    private EnemyDefinition NewDefinition(string assetName)
    {
        var definition = ScriptableObject.CreateInstance<EnemyDefinition>();

        // CreateInstance leaves `name` empty, and an empty name makes Does.Contain pass against
        // any message at all — the assertion would prove nothing (M0-11).
        definition.name = assetName;
        _created.Add(definition);
        return definition;
    }

    private static void SetString(EnemyDefinition definition, string field, string value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetFloat(EnemyDefinition definition, string field, float value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetInt(EnemyDefinition definition, string field, int value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
