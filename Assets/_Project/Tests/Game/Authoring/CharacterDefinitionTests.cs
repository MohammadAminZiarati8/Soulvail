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
/// Covers the authoring→spec conversion and, through <see cref="Oathbound_ToSpec_MatchesCoreCombatNumbers"/>,
/// pins the shipped Oathbound numbers to CC §7 so a stray Inspector drag is a red test rather
/// than a balance mystery.
/// </summary>
/// <remarks>
/// The invalid-field fixtures are built with <see cref="ScriptableObject.CreateInstance{T}()"/>
/// and poked through <see cref="SerializedObject"/>, because the fields are
/// <c>[SerializeField] private</c> and their <c>[Min]</c> attributes only clamp the Inspector
/// GUI — a <c>SerializedProperty</c> write goes straight past them, which is exactly the hole
/// <c>ToSpec</c> exists to close.
/// </remarks>
[TestFixture]
public sealed class CharacterDefinitionTests
{
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";

    private readonly List<CharacterDefinition> _created = new List<CharacterDefinition>();

    [TearDown]
    public void DestroyCreatedInstances()
    {
        foreach (CharacterDefinition definition in _created)
        {
            if (definition != null)
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        _created.Clear();
    }

    [Test]
    public void Oathbound_LoadsFromAssetPath()
    {
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(OathboundPath);

        Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {OathboundPath}.");
    }

    [Test]
    public void Oathbound_ToSpec_MatchesCoreCombatNumbers()
    {
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(OathboundPath);
        Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {OathboundPath}.");

        CharacterSpec spec = definition.ToSpec();

        Assert.That(spec.Id.Value, Is.EqualTo("character.oathbound"));
        Assert.That(spec.NameKey.Key, Is.EqualTo("character.oathbound.name"));
        Assert.That(spec.MaxHp, Is.EqualTo(140f).Within(Tolerance), "CC §7 survivability: max HP 140.");

        // Each of the four movement numbers is asserted against its own property, and the
        // tolerance is tight enough that no two of them overlap. M0-07 found that a four-float
        // constructor's likeliest defect is transposition, and that behaviour rows do not catch
        // it: 0.06 and 0.08 are close enough to pass a motor test while swapped, so this is the
        // row that has to tell accel from decel.
        Assert.That(spec.Movement.Speed, Is.EqualTo(5.4f).Within(Tolerance), "CC §7 move speed.");
        Assert.That(spec.Movement.AccelTime, Is.EqualTo(0.06f).Within(Tolerance), "CC §7 accel, not decel.");
        Assert.That(spec.Movement.DecelTime, Is.EqualTo(0.08f).Within(Tolerance), "CC §7 decel, not accel.");
        Assert.That(spec.Movement.TurnSpeedDeg, Is.EqualTo(720f).Within(Tolerance), "CC §7 turn speed.");
    }

    [Test]
    public void ToSpec_ReturnsNewInstanceEachCall()
    {
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(OathboundPath);
        Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {OathboundPath}.");

        CharacterSpec first = definition.ToSpec();
        CharacterSpec second = definition.ToSpec();

        // Rule 2: the SO holds no runtime state. Were a spec cached and handed out twice, two
        // runs would share one object — and the nested MovementSpec is checked too, because
        // caching just the inner record would pass a reference check on the outer one.
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second.Movement, Is.Not.SameAs(first.Movement));

        Assert.That(second.Id, Is.EqualTo(first.Id));
        Assert.That(second.NameKey, Is.EqualTo(first.NameKey));
        Assert.That(second.MaxHp, Is.EqualTo(first.MaxHp));
        Assert.That(second.Movement.Speed, Is.EqualTo(first.Movement.Speed));
        Assert.That(second.Movement.AccelTime, Is.EqualTo(first.Movement.AccelTime));
        Assert.That(second.Movement.DecelTime, Is.EqualTo(first.Movement.DecelTime));
        Assert.That(second.Movement.TurnSpeedDeg, Is.EqualTo(first.Movement.TurnSpeedDeg));
    }

    [Test]
    public void ToSpec_InvalidId_ThrowsNamingAsset()
    {
        CharacterDefinition definition = NewDefinition("BrokenId");

        // Setting a bad id also trips OnValidate's warning, which this row deliberately does
        // not assert: rule 3 is owned by OnValidate_InvalidId_LogsWarningNamingAsset alone.
        // An earlier draft expected the warning here too, and mutation-testing showed the cost
        // — silencing OnValidate reddened this row, which is about ToSpec and should not care.
        // A stray warning does not fail a test, so ignoring it is free.
        SetString(definition, "_id", "Bad Id");

        // Assert.Throws is an exact type match (M0-08), and that is the point of the row rather
        // than an accident of it: ToSpec promises plain ArgumentException whatever the inner
        // failure was, so a caller never has to know that maxHp fails as
        // ArgumentOutOfRangeException while the id fails as ArgumentException.
        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.Contain("BrokenId"),
            "The message must name the asset, or the Console points at no file a designer can open.");
    }

    [Test]
    public void ToSpec_InvalidHp_ThrowsNamingAsset()
    {
        CharacterDefinition definition = NewDefinition("BrokenHp");
        SetFloat(definition, "_maxHp", 0f);

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.Contain("BrokenHp"));

        // The inner exception is what says *which* field, and dropping it would leave the log
        // saying only that some asset is wrong. ArgumentOutOfRangeException derives from
        // ArgumentException, so this also pins that ToSpec narrows the type rather than letting
        // the subclass escape.
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void OnValidate_InvalidId_LogsWarningNamingAsset()
    {
        CharacterDefinition definition = NewDefinition("WarnsInInspector");

        // Rule 3. LogAssert fails at teardown if this warning never arrives, which is what makes
        // the row load-bearing: a silent OnValidate would go unnoticed otherwise, since a stray
        // warning does not fail a test on its own.
        ExpectInvalidIdWarning("WarnsInInspector");

        SetString(definition, "_id", "Character.Oathbound");
    }

    [Test]
    public void AllCharacterDefinitions_HaveValidUniqueIds()
    {
        string[] guids = AssetDatabase.FindAssets($"t:{nameof(CharacterDefinition)}");

        Assert.That(guids, Is.Not.Empty, "The project ships at least the Oathbound.");

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

            Assert.That(definition, Is.Not.Null, $"{path} did not load as a CharacterDefinition.");

            CharacterSpec spec = null;
            Assert.That(() => spec = definition.ToSpec(), Throws.Nothing, $"{path} is not valid content.");

            Assert.That(seen.Add(spec.Id.Value), Is.True,
                $"{path} repeats the id '{spec.Id.Value}'. Two classes under one id means the " +
                "catalog silently keeps one of them.");
        }
    }

    /// <summary>
    /// Tight enough that no two of the Oathbound's numbers could satisfy each other's
    /// assertion, loose enough to survive Unity writing a float back as decimal text.
    /// </summary>
    private const float Tolerance = 1e-6f;

    private CharacterDefinition NewDefinition(string assetName)
    {
        var definition = ScriptableObject.CreateInstance<CharacterDefinition>();

        // CreateInstance leaves `name` empty, and an empty name makes Does.Contain pass against
        // any message at all — the assertion would prove nothing. Naming it is what gives the
        // "message names the asset" rows something to actually find.
        definition.name = assetName;
        _created.Add(definition);
        return definition;
    }

    private static void ExpectInvalidIdWarning(string assetName)
    {
        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(assetName)));
    }

    private static void SetString(CharacterDefinition definition, string field, string value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetFloat(CharacterDefinition definition, string field, float value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
