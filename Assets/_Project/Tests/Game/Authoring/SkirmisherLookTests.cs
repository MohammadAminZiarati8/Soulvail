using System.Linq;
using NUnit.Framework;
using Soulvail.Game.Authoring;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Authoring;

[TestFixture]
public sealed class SkirmisherLookTests
{
    private const string Root = "Assets/_Project/";
    private const string Paths = Root + "Data/Paths/";
    private const string QuiverPath = "Assets/ThirdParty/KayKit/Adventurers/Props/quiver.fbx";

    private PathLook Look => AssetDatabase.LoadAssetAtPath<PathLook>(Paths + "SkirmisherLook.asset");

    [Test]
    public void Look_Assigned_ToSkirmisher()
    {
        var path = AssetDatabase.LoadAssetAtPath<PathDefinition>(Paths + "Skirmisher.asset");

        Assert.That(Look, Is.Not.Null);
        Assert.That(path.Look, Is.SameAs(Look));
    }

    [Test]
    public void Look_SwapsTheMarksmansSlots()
    {
        var marksman = AssetDatabase.LoadAssetAtPath<PathLook>(Paths + "MarksmanLook.asset");
        const string trimPath = Root + "Materials/Ranger/M_RangerSkirmisherTrim.mat";
        var trim = AssetDatabase.LoadAssetAtPath<Material>(trimPath);

        Assert.That(trim, Is.Not.Null);
        Assert.That(Look.Materials.Select(swap => (swap.Renderer, swap.Slot, swap.Mesh, swap.BowTint)),
            Is.EqualTo(marksman.Materials.Select(swap => (swap.Renderer, swap.Slot, swap.Mesh, swap.BowTint))));
        Assert.That(Look.Materials.Select(swap => AssetDatabase.GetAssetPath(swap.Material)), Is.All.EqualTo(trimPath));
        Assert.That(trim.IsKeywordEnabled("_EMISSION"), Is.True, "The bow slot uses emissive tint.");
        AssertPaleSilverCyan(trim.color);
    }

    [Test]
    public void Tints_AreNotGreenRedOrGold()
    {
        AssertPaleSilverCyan(Look.BowTint);
        AssertPaleSilverCyan(Look.ArrowTrailTint);
        Assert.That(Look.ArrowTrailTint.grayscale, Is.GreaterThan(Look.BowTint.grayscale));
    }

    [Test]
    public void Prop_SocketExists()
    {
        var ranger = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Player/Bodies/Ranger.prefab");

        Assert.That(Look.Props.Count, Is.EqualTo(1));
        PathLook.Prop prop = Look.Props[0];
        Assert.That(prop.Prefab, Is.Not.Null);
        Assert.That(AssetDatabase.GetAssetPath(prop.Prefab), Is.EqualTo(QuiverPath));
        Assert.That(prop.Socket, Is.EqualTo("hips"));
        Assert.That(ranger.GetComponentsInChildren<Transform>(true).Any(child => child.name == prop.Socket), Is.True);
    }

    private static void AssertPaleSilverCyan(Color color)
    {
        Color.RGBToHSV(color, out float hue, out float saturation, out float value);
        Assert.That(hue * 360f, Is.InRange(175f, 200f));
        Assert.That(saturation, Is.InRange(0.05f, 0.25f));
        Assert.That(value, Is.InRange(0.8f, 1f));
        Assert.That(color.a, Is.EqualTo(1f));
    }
}
