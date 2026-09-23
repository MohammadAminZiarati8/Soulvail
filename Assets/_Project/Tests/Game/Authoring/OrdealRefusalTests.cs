using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// M6-06b's two refusals, pinned against the assets: GD §13.4's Fracture and Echo are correct
/// designs this build cannot carry, and nobody adds one without reading why.
/// </summary>
/// <remarks>
/// <para>
/// <b>In <c>Soulvail.Tests.Game</c> because the evidence is authored</b> — the Ordeal assets and the
/// arena prefabs — and <c>Soulvail.Tests.Core</c> cannot open either (M0-10). The core half of Echo's
/// refusal, that wave 4 is its own composition, is <c>OrdealEffectsTests.Waves_TheFourthIsNotACopy</c>.
/// </para>
/// <para>
/// <b>These rows are meant to go red</b>, and the message says where to read before turning them
/// green: the spec's two refusal sections, and the parking-lot lines that own each one.
/// </para>
/// </remarks>
[TestFixture]
public sealed class OrdealRefusalTests
{
    private const string OrdealFolder = "Assets/_Project/Data/Ordeals";
    private const string PillarsPath = "Assets/_Project/Prefabs/Arenas/Arena_Pillars.prefab";
    private const string TieredPath = "Assets/_Project/Prefabs/Arenas/Arena_Tiered.prefab";

    private const string ReadFirst =
        " Read M6-06b's refusal sections and the ROADMAP parking lot before changing this row.";

    /// <summary>GD §13.4's pillar count Fracture removes.</summary>
    private const int FracturePillars = 3;

    [Test]
    public void Ordeal_FractureIsNotAuthored()
    {
        // The five dials and three keys M6-06a authored, and nothing about an arena's cover — core
        // has never seen that number, and this would be the first mechanic to reach past the
        // boundary for one.
        Assert.That(
            SpecParameters(),
            Is.EquivalentTo(new[]
            {
                "id", "nameKey", "descriptionKey", "essenceMultiplier", "offerCount",
                "concurrencyBonus", "veilrotMultiplier", "threatCostTarget", "threatCostMultiplier",
            }),
            "OrdealSpec gained a dial." + ReadFirst);

        Assert.That(
            SerializedFields(typeof(OrdealDefinition)).Where(f => Mentions(f, "cover", "pillar")),
            Is.Empty,
            "OrdealDefinition gained a cover field." + ReadFirst);

        Assert.That(ShippedIds(), Does.Not.Contain("ordeal.fracture"), "Fracture is authored." + ReadFirst);
    }

    [Test]
    public void Ordeal_EchoIsNotAuthored()
    {
        Assert.That(ShippedIds(), Does.Not.Contain("ordeal.echo"), "Echo is authored." + ReadFirst);

        // And the shipped four are the whole pool, so the refusals are not dodged under another name.
        Assert.That(
            ShippedIds(),
            Is.EquivalentTo(new[] { "ordeal.famine", "ordeal.hunger", "ordeal.swarm", "ordeal.vigil" }));

        // WaveComposer still composes every wave from the budget alone: nothing it is handed names
        // a previous wave or a plan to copy one out of.
        ParameterInfo[] compose = typeof(WaveComposer).GetMethod(nameof(WaveComposer.Compose))!.GetParameters();

        Assert.That(
            compose.Select(p => p.Name),
            Is.EqualTo(new[] { "stage", "mode", "destination", "spawn", "ordeals" }),
            "WaveComposer.Compose was handed something new." + ReadFirst);
    }

    [Test]
    public void Arena_HasFewerThanFourPillarsToSpare()
    {
        // The arithmetic behind Fracture's refusal, re-checked whenever M7-05/06 authors a room:
        // "3 fewer" leaves 1 and 2, below the arena contract's own floor of 3 (GD §7.2, §12.4).
        ArenaView pillars = Arena(PillarsPath);
        ArenaView tiered = Arena(TieredPath);

        Assert.That(pillars.CoverCount, Is.EqualTo(4));
        Assert.That(tiered.CoverCount, Is.EqualTo(5));

        Assert.That(ArenaView.MinCoverPillars, Is.EqualTo(3));

        Assert.That(pillars.CoverCount - FracturePillars, Is.LessThan(ArenaView.MinCoverPillars));
        Assert.That(tiered.CoverCount - FracturePillars, Is.LessThan(ArenaView.MinCoverPillars));
    }

    private static IEnumerable<string> SpecParameters()
    {
        ConstructorInfo[] constructors = typeof(OrdealSpec).GetConstructors();

        Assert.That(constructors, Has.Length.EqualTo(1), "OrdealSpec has a second constructor.");

        return constructors[0].GetParameters().Select(p => p.Name);
    }

    private static IEnumerable<FieldInfo> SerializedFields(System.Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => f.IsPublic || f.GetCustomAttribute<SerializeField>() != null);

    private static bool Mentions(FieldInfo field, params string[] words) =>
        words.Any(w => field.Name.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0);

    private static List<string> ShippedIds()
    {
        var ids = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:OrdealDefinition", new[] { OrdealFolder }))
        {
            var ordeal = AssetDatabase.LoadAssetAtPath<OrdealDefinition>(AssetDatabase.GUIDToAssetPath(guid));

            Assert.That(ordeal, Is.Not.Null, $"An Ordeal asset failed to load: {guid}.");

            ids.Add(ordeal.Id);
        }

        return ids;
    }

    private static ArenaView Arena(string path)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(go, Is.Not.Null, $"No prefab at {path}.");

        return go.GetComponent<ArenaView>();
    }
}
