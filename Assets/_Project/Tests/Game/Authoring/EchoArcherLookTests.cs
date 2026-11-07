using System.Linq;
using NUnit.Framework;
using Soulvail.Game.Authoring;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Authoring;

[TestFixture]
public sealed class EchoArcherLookTests
{
    private const string Root = "Assets/_Project/";
    private const string Paths = Root + "Data/Paths/";

    private PathLook Look => AssetDatabase.LoadAssetAtPath<PathLook>(Paths + "EchoArcherLook.asset");

    [Test]
    public void Look_Assigned_ToEchoArcher()
    {
        var path = AssetDatabase.LoadAssetAtPath<PathDefinition>(Paths + "EchoArcher.asset");
        Assert.That(Look, Is.Not.Null);
        Assert.That(path.Look, Is.SameAs(Look));
    }

    [Test]
    public void Look_SwapsTheMarksmansSlots()
    {
        var marksman = AssetDatabase.LoadAssetAtPath<PathLook>(Paths + "MarksmanLook.asset");
        var skirmisher = AssetDatabase.LoadAssetAtPath<PathLook>(Paths + "SkirmisherLook.asset");
        var shade = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/Characters/M_Shade.mat");
        const string trimPath = Root + "Materials/Ranger/M_RangerEchoTrim.mat";
        var trim = AssetDatabase.LoadAssetAtPath<Material>(trimPath);

        Assert.That(trim, Is.Not.Null);
        Assert.That(Look.Materials.Select(swap => (swap.Renderer, swap.Slot, swap.Mesh, swap.BowTint)),
            Is.EqualTo(marksman.Materials.Select(swap => (swap.Renderer, swap.Slot, swap.Mesh, swap.BowTint))));
        Assert.That(Look.Materials.Select(swap => AssetDatabase.GetAssetPath(swap.Material)), Is.All.EqualTo(trimPath));
        Assert.That(trim.IsKeywordEnabled("_EMISSION"), Is.True, "The bow slot uses emissive tint.");
        Color.RGBToHSV(trim.color, out float trimHue, out _, out _);
        Color.RGBToHSV(shade.color, out float shadeHue, out _, out _);
        Assert.That(trimHue, Is.EqualTo(shadeHue).Within(0.01f));
        Assert.That(trim.color.grayscale, Is.LessThan(marksman.Materials.Min(swap => swap.Material.color.grayscale)));
        Assert.That(trim.color.grayscale, Is.LessThan(skirmisher.Materials.Min(swap => swap.Material.color.grayscale)));
    }

    [Test]
    public void Tints_AreNotVioletLimeRedOrGold()
    {
        const string trimPath = Root + "Materials/Ranger/M_RangerEchoTrim.mat";
        var trim = AssetDatabase.LoadAssetAtPath<Material>(trimPath);
        Color bow = Look.BowTint;
        Assert.That(bow.r, Is.EqualTo(trim.color.r).Within(0.0001f));
        Assert.That(bow.g, Is.EqualTo(trim.color.g).Within(0.0001f));
        Assert.That(bow.b, Is.EqualTo(trim.color.b).Within(0.0001f));
        AssertDeepCyan(Look.BowTint);
        AssertDeepCyan(Look.ArrowTrailTint);
        Assert.That(Look.ArrowTrailTint.grayscale - Look.BowTint.grayscale, Is.InRange(0.01f, 0.1f));
    }

    [Test]
    public void Prop_SocketExists()
    {
        var ranger = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Player/Bodies/Ranger.prefab");
        Assert.That(Look.Props.Count, Is.LessThanOrEqualTo(1));
        foreach (PathLook.Prop prop in Look.Props)
        {
            Assert.That(prop.Prefab, Is.Not.Null);
            Assert.That(ranger.GetComponentsInChildren<Transform>(true).Any(child => child.name == prop.Socket), Is.True);
        }
    }

    private static void AssertDeepCyan(Color color)
    {
        Color.RGBToHSV(color, out float hue, out _, out float value);
        Assert.That(hue * 360f, Is.InRange(175f, 200f));
        Assert.That(value, Is.InRange(0.25f, 0.55f));
        Assert.That(color.a, Is.EqualTo(1f));
    }
}
