using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;
using Num3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class PackleaderLookTests
{
    private const string Root = "Assets/_Project/";
    private const string HeroStripes = Root + "Materials/Characters/M_BeastmasterPackleader.mat";
    private const string HeroPlain = Root + "Materials/Characters/M_Beastmaster.mat";
    private const string HoundStripes = Root + "Materials/Pets/M_HoundPackleader.mat";
    private const string HoundPlain = Root + "Materials/Pets/M_Hound.mat";
    private static readonly string[] StripedParts = { "Barbarian_ArmLeft", "Barbarian_ArmRight", "Barbarian_Body" };

    private DomainEventHub _hub;
    private IObjectResolver _resolver;
    private GameObject _body;
    private GameObject _parent;
    private PathLookView _view;
    private PetViews _pets;
    private Mesh _plainHound;

    private static PathLook Look => AssetDatabase.LoadAssetAtPath<PathLook>(Root + "Data/Paths/PackleaderLook.asset");

    [SetUp]
    public void SetUp()
    {
        _hub = new DomainEventHub();
        _resolver = new ContainerBuilder().Build();
        _parent = new GameObject("Packleader test pets");
        var hound = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Pets/Hound.prefab");
        _plainHound = hound.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
        _pets = new PetViews(_resolver, hound.GetComponent<PetView>(), _parent.transform, _hub);
        _body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Characters/Beastmaster.prefab"));
        _view = _body.GetComponent<PathLookView>();
        _view.Construct(_hub, null, null, _pets);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_body);
        _pets.Dispose();
        _hub.Dispose();
        _resolver.Dispose();
        Object.DestroyImmediate(_parent);
    }

    [Test]
    public void Packleader_HeroStriped()
    {
        var path = AssetDatabase.LoadAssetAtPath<PathDefinition>(Root + "Data/Paths/Packleader.asset");
        Assert.That(path.Look, Is.SameAs(Look));

        ChoosePackleader();

        Assert.That(_view.Look, Is.SameAs(Look));
        foreach (string part in StripedParts)
        {
            Assert.That(MaterialPath(HeroRenderer(part)), Is.EqualTo(HeroStripes), part);
        }
        Assert.That(MaterialPath(HeroRenderer("Barbarian_Head")), Is.EqualTo(HeroPlain), "Only arms and chest are painted.");
    }

    [Test]
    public void Packleader_EveryHoundStriped()
    {
        _hub.Publish(new PetSpawned(1, Num3.Zero));
        _hub.Publish(new PetSpawned(2, Num3.UnitX, isLead: false));

        ChoosePackleader();

        Assert.That(_pets.Active.Count, Is.EqualTo(2));
        foreach (PetView pet in _pets.Active)
        {
            AssertHound(pet, striped: true);
        }
    }

    [Test]
    public void LaterPackHound_Striped()
    {
        _hub.Publish(new PetSpawned(1, Num3.Zero));
        ChoosePackleader();

        _hub.Publish(new PetSpawned(2, Num3.UnitX, isLead: false));

        foreach (PetView pet in _pets.Active)
        {
            AssertHound(pet, striped: true);
        }
    }

    [Test]
    public void NoPath_NoStripes()
    {
        _hub.Publish(new PetSpawned(1, Num3.Zero));
        _view.ApplyPath(default, false);
        _hub.Publish(new PetSpawned(2, Num3.UnitX, isLead: false));

        AssertPlain();

        ChoosePackleader();
        _view.Dispose();

        AssertPlain();
    }

    [Test]
    public void Tint_InPlayerFamily()
    {
        Object.DestroyImmediate(AssertStripesInFamily("Art/Characters/Textures/T_Beastmaster_Albedo.png",
            "Art/Characters/Textures/T_Beastmaster_Packleader_Albedo.png", out _));
        Texture2D hound = AssertStripesInFamily("Art/Pets/Textures/T_Hound_Albedo.png",
            "Art/Pets/Textures/T_Hound_Packleader_Albedo.png", out HashSet<int> changed);

        Mesh striped = Look.PetMaterials[0].Mesh;
        int painted = 0;
        foreach (Vector2 uv in striped.uv)
        {
            int index = (int)(uv.x * hound.width) + (int)(uv.y * hound.height) * hound.width;
            painted += changed.Contains(index) ? 1 : 0;
        }
        Object.DestroyImmediate(hound);
        Assert.That(painted, Is.GreaterThan(0), "The hound mesh's stripe faces point at the stripe swatch.");
        Assert.That(Look.BowTint.a, Is.EqualTo(0f));
        Assert.That(Look.ArrowTrailTint.a, Is.EqualTo(0f));
    }

    private void ChoosePackleader() => _hub.Publish(new PathChosen(new ContentId("path.packleader")));

    private void AssertPlain()
    {
        foreach (string part in StripedParts)
        {
            Assert.That(MaterialPath(HeroRenderer(part)), Is.EqualTo(HeroPlain), part);
        }
        Assert.That(_pets.Active.Count, Is.EqualTo(2));
        foreach (PetView pet in _pets.Active)
        {
            AssertHound(pet, striped: false);
        }
    }

    private void AssertHound(PetView pet, bool striped)
    {
        var skin = pet.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Assert.That(MaterialPath(skin), Is.EqualTo(striped ? HoundStripes : HoundPlain));
        Assert.That(skin.sharedMesh, Is.SameAs(striped ? Look.PetMaterials[0].Mesh : _plainHound));
    }

    private Renderer HeroRenderer(string name)
    {
        foreach (Renderer renderer in _body.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.name == name)
            {
                return renderer;
            }
        }
        Assert.Fail(name);
        return null;
    }

    private static string MaterialPath(Renderer renderer) => AssetDatabase.GetAssetPath(renderer.sharedMaterial);

    private static Texture2D AssertStripesInFamily(string basePath, string stripedPath, out HashSet<int> changed)
    {
        Texture2D plain = Png(basePath);
        Texture2D striped = Png(stripedPath);
        Assert.That(striped.width, Is.EqualTo(plain.width));
        Assert.That(striped.height, Is.EqualTo(plain.height));
        Color32[] before = plain.GetPixels32();
        Color32[] after = striped.GetPixels32();
        Object.DestroyImmediate(plain);
        Color.RGBToHSV(Palette.Player, out float playerHue, out _, out _);
        changed = new HashSet<int>();
        for (int i = 0; i < after.Length; i++)
        {
            if (before[i].Equals(after[i]))
            {
                continue;
            }
            changed.Add(i);
            Color stripe = after[i];
            Color.RGBToHSV(stripe, out float hue, out float saturation, out float value);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(hue * 360f, playerHue * 360f)), Is.LessThan(15f), stripedPath);
            Assert.That(saturation, Is.GreaterThan(0.6f), stripedPath);
            Assert.That(value, Is.GreaterThan(0.6f), stripedPath);
            Assert.That(Palette.IsDanger(stripe), Is.False, stripedPath);
        }
        Assert.That(changed.Count, Is.GreaterThan(0), stripedPath + " carries no stripes.");
        return striped;
    }

    private static Texture2D Png(string path)
    {
        var texture = new Texture2D(2, 2);
        Assert.That(texture.LoadImage(System.IO.File.ReadAllBytes(Root + path)), Is.True, path);
        return texture;
    }
}
