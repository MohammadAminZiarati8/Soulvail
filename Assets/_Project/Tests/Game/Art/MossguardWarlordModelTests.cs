using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>JG-15e: the Warlord's body — the Mossguard variant, war-painted, glaive and big shield.</summary>
[TestFixture]
public sealed class MossguardWarlordModelTests
{
    private const string Prefabs = "Assets/_Project/Prefabs/Enemies/";
    private const string WarlordPath = Prefabs + "Mossguard_Warlord.prefab";
    private const string BasePath = Prefabs + "Mossguard.prefab";
    private const string GlaivePath = Prefabs + "WarlordGlaive.prefab";
    private const string Art = "Assets/_Project/Art/Enemies/";
    private const string ModelPath = Art + "Mossguard.fbx";
    private const string ShieldPath = Art + "MossguardShield.fbx";
    private const string SpearPath = Art + "MossguardSpear.fbx";
    private const string AxePath = "Assets/ThirdParty/KayKit/Adventurers/Props/axe_2handed.fbx";
    private const string AtlasPath = Art + "Textures/T_Enemy_Albedo.png";
    private const string AlbedoPath = Art + "Textures/T_MossguardWarlord_Albedo.png";
    private const string MaterialPath = "Assets/_Project/Materials/Enemies/M_MossguardWarlord.mat";
    private const string BaseMaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";
    private const string SliceClip = "Melee_1H_Attack_Slice_Horizontal";
    private const string MeleeClips = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx";

    // The atlas is 8 × 4 swatches, counted from the top-left. Only these three are repainted.
    private static readonly (int X, int Y) Skin = (1, 2);
    private static readonly (int X, int Y) Cloth = (5, 1);
    private static readonly (int X, int Y) Trim = (5, 0);

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void DestroyCreatedObjects()
    {
        foreach (Object obj in _created)
        {
            Object.DestroyImmediate(obj);
        }

        _created.Clear();
    }

    [Test]
    public void Warlord_IsAVariantOfTheMossguard()
    {
        GameObject warlord = Warlord();
        Assert.That(PrefabUtility.GetPrefabAssetType(warlord), Is.EqualTo(PrefabAssetType.Variant));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(warlord), Is.EqualTo(Load(BasePath)));
        Assert.That(warlord.GetComponents<Component>().Select(c => c.GetType()),
            Is.EqualTo(Load(BasePath).GetComponents<Component>().Select(c => c.GetType()).Append(typeof(WarlordView))),
            "The base's components, plus JG-15f's WarlordView.");
        Assert.That(warlord.transform.localScale, Is.EqualTo(Vector3.one), "The 1.6× is the data's BodyScale (15g).");
    }

    [Test]
    public void Warlord_BodyIsOneModelChild()
    {
        Transform model = Warlord().transform.Find("Model");
        Assert.That(model, Is.Not.Null);
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(model.gameObject)), Is.EqualTo(ModelPath));
        Assert.That(model.localScale, Is.EqualTo(Load(BasePath).transform.Find("Model").localScale), "No scale of its own.");

        foreach (SkinnedMeshRenderer body in Warlord().GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Assert.That(body.transform.IsChildOf(model), Is.True, body.name);
        }
    }

    [Test]
    public void Warlord_WearsTheWarPaint()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.That(material, Is.Not.Null);
        Assert.That(material.shader, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath).shader));
        Assert.That(material.GetTexture("_BaseMap"), Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath)));
        Assert.That(Body().sharedMaterials, Is.EqualTo(new[] { material }));
        Assert.That(ShieldOf(Warlord()).GetComponent<MeshRenderer>().sharedMaterials, Is.EqualTo(new[] { material }));
        Assert.That(Glaive().Find("Haft").GetComponent<MeshRenderer>().sharedMaterials, Is.EqualTo(new[] { material }));

        Texture2D atlas = Png(AtlasPath), paint = Png(AlbedoPath);
        Assert.That((paint.width, paint.height), Is.EqualTo((atlas.width, atlas.height)));
        Color32[] before = atlas.GetPixels32(), after = paint.GetPixels32();
        var repainted = new[] { Skin, Cloth, Trim };

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                bool same = Cell(before, atlas, x, y).SequenceEqual(Cell(after, paint, x, y));
                Assert.That(same, Is.EqualTo(!repainted.Contains((x, y))), $"swatch {x},{y}");
            }
        }

        Color.RGBToHSV(Mean(after, paint, Skin), out float skinHue, out _, out float skinValue);
        Color.RGBToHSV(Mean(before, atlas, Skin), out _, out _, out float oldSkinValue);
        Assert.That(skinHue * 360, Is.InRange(60, 100), "Olive.");
        Assert.That(skinValue, Is.LessThan(oldSkinValue), "Darker.");
        Color.RGBToHSV(Mean(after, paint, Cloth), out float clothHue, out float clothSaturation, out float clothValue);
        Assert.That(clothHue * 360, Is.InRange(15, 40), "Umber.");
        Assert.That(clothSaturation, Is.InRange(0.4f, 0.75f));
        Assert.That(clothValue, Is.LessThan(0.45f));
        Color.RGBToHSV(Mean(after, paint, Trim), out _, out float trimSaturation, out float trimValue);
        Assert.That(trimSaturation, Is.LessThan(0.15f), "Bone-white.");
        Assert.That(trimValue, Is.GreaterThan(0.8f));
        Assert.That(Body().sharedMesh.uv.Any(uv => InCell(uv, Trim)), Is.True, "The stripes land on the body.");
    }

    [Test]
    public void Warlord_PaintHasNoCyanAndNoDanger()
    {
        Texture2D paint = Png(AlbedoPath);
        AssertLawful(paint.GetPixels32(), "albedo");

        // The glaive's head keeps KayKit's own texture; the texels its UVs reach answer to the same law.
        MeshRenderer head = Glaive().Find("Head").GetComponent<MeshRenderer>();
        Texture2D kayKit = Png(AssetDatabase.GetAssetPath(head.sharedMaterial.GetTexture("_BaseMap")));
        AssertLawful(head.GetComponent<MeshFilter>().sharedMesh.uv.Select(uv => (Color32)kayKit.GetPixelBilinear(uv.x, uv.y)), "glaive head");
    }

    [Test]
    public void Warlord_ShieldIsBigger_OnTheSocket()
    {
        Transform shield = ShieldOf(Warlord());
        Transform baseShield = ShieldOf(Load(BasePath));
        Assert.That(shield.parent.name, Is.EqualTo("handslot.l"));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(shield.gameObject), Is.EqualTo(baseShield.gameObject),
            "The base's Shield, overridden — 15f hides it on the shatter.");
        Assert.That(shield.GetComponent<MeshFilter>().sharedMesh, Is.EqualTo(Load(ShieldPath).GetComponent<MeshFilter>().sharedMesh));
        Assert.That(Vector3.Distance(shield.localScale, baseShield.localScale * 1.35f), Is.LessThan(0.001f));
        Assert.That(shield.localPosition, Is.EqualTo(baseShield.localPosition));
        Assert.That(shield.localRotation, Is.EqualTo(baseShield.localRotation));
        Assert.That(shield.gameObject.activeSelf, Is.True);
    }

    [Test]
    public void Warlord_GlaiveOnTheSpearSocket_NoSpear()
    {
        Transform slot = Socket(Warlord(), "handslot.r");
        Transform spear = slot.Find("Spear");
        Assert.That(spear, Is.Not.Null);
        Assert.That(spear.gameObject.activeSelf, Is.False);
        Transform glaive = Glaive();
        Assert.That(glaive.parent, Is.EqualTo(slot));
        Assert.That(glaive.gameObject.activeSelf, Is.True);
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(glaive.gameObject), Is.EqualTo(Load(GlaivePath)));
        Assert.That((glaive.localPosition, glaive.localRotation, glaive.localScale), Is.EqualTo((spear.localPosition, spear.localRotation, spear.localScale)));

        MeshFilter haft = glaive.Find("Haft").GetComponent<MeshFilter>();
        MeshFilter head = glaive.Find("Head").GetComponent<MeshFilter>();
        Assert.That(haft.sharedMesh, Is.EqualTo(Load(SpearPath).GetComponent<MeshFilter>().sharedMesh));
        Assert.That(head.sharedMesh, Is.EqualTo(Load(AxePath).GetComponent<MeshFilter>().sharedMesh));
        Bounds haftBounds = BoundsOf(PointsIn(glaive, haft));
        Vector3[] headPoints = PointsIn(glaive, head);
        Bounds headBounds = BoundsOf(headPoints);
        float bladeBottom = headPoints.Where(p => Mathf.Abs(p.x) > haftBounds.extents.x).Min(p => p.y);
        Assert.That(bladeBottom, Is.EqualTo(haftBounds.max.y).Within(0.1f), "The blade starts at the haft's tip.");
        Assert.That(headBounds.min.y, Is.InRange(haftBounds.center.y, haftBounds.max.y), "Its neck seats inside the haft.");
        Assert.That(headBounds.size.x, Is.GreaterThan(headBounds.size.z * 3), "The blade spreads along the glaive's X.");

        GameObject warlord = Spawn(Warlord());
        AnimationClip slice = AssetDatabase.LoadAllAssetsAtPath(MeleeClips).OfType<AnimationClip>().Single(c => c.name == SliceClip);
        slice.SampleAnimation(warlord.transform.Find("Model").gameObject, slice.length * 0.5f);
        Transform swung = Socket(warlord, "handslot.r").Find("Glaive");
        Assert.That(Mathf.Abs(Vector3.Dot(swung.right, Vector3.up)), Is.LessThan(0.35f), "Edge along the sweep plane mid-slice.");
        Assert.That(Mathf.Abs(Vector3.Dot(swung.up, Vector3.up)), Is.LessThan(0.35f), "The haft sweeps flat too.");
    }

    [Test]
    public void Warlord_OneSkinnedBodyOnRigMedium()
    {
        GameObject warlord = Warlord();
        SkinnedMeshRenderer body = warlord.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
        Transform rig = warlord.transform.Find("Model/Rig_Medium");
        Assert.That(rig, Is.Not.Null);
        Assert.That(body.rootBone.IsChildOf(rig), Is.True);
        Assert.That(body.bones.All(b => b.IsChildOf(rig)), Is.True);
        Assert.That(body.sharedMesh.bindposes, Has.Length.EqualTo(body.bones.Length));
        Assert.That(Socket(warlord, "handslot.l").IsChildOf(rig) && Socket(warlord, "handslot.r").IsChildOf(rig), Is.True);

        string bodyName = body.name, meshName = body.sharedMesh.name;
        foreach (string script in Directory.GetFiles("Assets/_Project/Game", "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(script);
            Assert.That(text.Contains(bodyName) || text.Contains("\"" + meshName + "\"") || text.Contains("Mossguard.fbx"), Is.False,
                $"{script} names the body mesh; the sculpt slot must stay code-free.");
        }
    }

    [Test]
    public void Warlord_SwappedMesh_KeepsProps()
    {
        GameObject warlord = Spawn(Warlord());
        SkinnedMeshRenderer body = warlord.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Transform shield = ShieldOf(warlord), glaive = Socket(warlord, "handslot.r").Find("Glaive");
        (Transform Parent, Vector3 Position, Quaternion Rotation, Vector3 Scale) shieldBefore = Pose(shield), glaiveBefore = Pose(glaive);
        Transform[] bones = body.bones;

        // A stand-in sculpt: the same rig binding, a different surface.
        Mesh sculpt = Object.Instantiate(body.sharedMesh);
        sculpt.hideFlags = HideFlags.HideAndDontSave;
        _created.Add(sculpt);
        sculpt.vertices = sculpt.vertices.Select(v => new Vector3(v.x * 1.1f, v.y, v.z * 1.1f)).ToArray();
        body.sharedMesh = sculpt;
        var skin = new Material(body.sharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(skin);
        skin.SetTexture("_BaseMap", Texture2D.grayTexture);
        body.sharedMaterial = skin;

        Assert.That(body.bones, Is.EqualTo(bones));
        Assert.That(Pose(shield), Is.EqualTo(shieldBefore));
        Assert.That(Pose(glaive), Is.EqualTo(glaiveBefore));
        Assert.That(shield.GetComponent<MeshRenderer>().sharedMaterial, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>(MaterialPath)),
            "The props keep the warlord's paint when the body's skin changes.");
        var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(baked);
        body.BakeMesh(baked);
        Assert.That(baked.vertexCount, Is.EqualTo(sculpt.vertexCount));
        Assert.That(baked.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), Is.True);
    }

    [Test]
    public void Warlord_StaysInBudget_EveryVertexWeighted()
    {
        Mesh body = Body().sharedMesh;
        int bodyTriangles = Triangles(body);
        int shieldTriangles = Triangles(ShieldOf(Warlord()).GetComponent<MeshFilter>().sharedMesh);
        int axeTriangles = Triangles(Load(AxePath).GetComponent<MeshFilter>().sharedMesh);
        int glaiveTriangles = Glaive().GetComponentsInChildren<MeshFilter>(true).Sum(f => Triangles(f.sharedMesh));
        Assert.That(bodyTriangles, Is.InRange(1200, 2200), "MossguardModelTests' body budget.");
        Assert.That(shieldTriangles, Is.InRange(150, 400), "MossguardModelTests' shield budget.");
        Assert.That(glaiveTriangles, Is.LessThanOrEqualTo(300 + axeTriangles), "The spear's budget plus the axe's.");
        Assert.That(bodyTriangles + shieldTriangles + glaiveTriangles, Is.LessThanOrEqualTo(2800 + axeTriangles),
            "The crowd budget, plus the axe head the glaive adds.");

        byte[] counts = body.GetBonesPerVertex().ToArray();
        BoneWeight1[] weights = body.GetAllBoneWeights().ToArray();
        Assert.That(counts, Has.Length.EqualTo(body.vertexCount));
        int at = 0;

        for (int v = 0; v < counts.Length; v++)
        {
            Assert.That(counts[v], Is.InRange(1, 4), $"vertex {v}");
            float sum = 0;

            for (int k = 0; k < counts[v]; k++, at++)
            {
                Assert.That(weights[at].weight, Is.GreaterThan(0), $"vertex {v}");
                sum += weights[at].weight;
            }

            Assert.That(sum, Is.EqualTo(1).Within(0.001f), $"vertex {v}");
        }
    }

    private static GameObject Warlord() => Load(WarlordPath);
    private static SkinnedMeshRenderer Body() => Warlord().GetComponentInChildren<SkinnedMeshRenderer>(true);
    private static Transform Glaive() => Socket(Warlord(), "handslot.r").Find("Glaive");
    private static Transform ShieldOf(GameObject root) => Socket(root, "handslot.l").Find("Shield");
    private static Transform Socket(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
    private static int Triangles(Mesh mesh) => mesh.triangles.Length / 3;

    private static (Transform, Vector3, Quaternion, Vector3) Pose(Transform t) => (t.parent, t.localPosition, t.localRotation, t.localScale);

    private static GameObject Load(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }

    private GameObject Spawn(GameObject prefab)
    {
        GameObject instance = Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        _created.Add(instance);
        return instance;
    }

    private Texture2D Png(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(texture);
        Assert.That(texture.LoadImage(File.ReadAllBytes(path)), Is.True, path);
        texture.filterMode = FilterMode.Point;
        return texture;
    }

    private static Vector3[] PointsIn(Transform space, MeshFilter filter) =>
        filter.sharedMesh.vertices.Select(v => space.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();

    private static Bounds BoundsOf(Vector3[] points)
    {
        var bounds = new Bounds(points[0], Vector3.zero);

        foreach (Vector3 point in points)
        {
            bounds.Encapsulate(point);
        }

        return bounds;
    }

    private static bool InCell(Vector2 uv, (int X, int Y) cell) =>
        Mathf.FloorToInt(uv.x * 8) == cell.X && Mathf.FloorToInt((1f - uv.y) * 4) == cell.Y;

    // GetPixels32 rows run bottom-up; swatch rows count from the top.
    private static IEnumerable<Color32> Cell(Color32[] pixels, Texture2D texture, int x, int y)
    {
        int w = texture.width / 8, h = texture.height / 4;

        for (int row = texture.height - ((y + 1) * h); row < texture.height - (y * h); row++)
        {
            for (int column = x * w; column < (x + 1) * w; column++)
            {
                yield return pixels[(row * texture.width) + column];
            }
        }
    }

    private static Color Mean(Color32[] pixels, Texture2D texture, (int X, int Y) cell)
    {
        Color32[] texels = Cell(pixels, texture, cell.X, cell.Y).ToArray();
        return new Color(texels.Average(c => c.r / 255f), texels.Average(c => c.g / 255f), texels.Average(c => c.b / 255f));
    }

    private static void AssertLawful(IEnumerable<Color32> texels, string what)
    {
        Vector3 danger = Lab(Palette.Danger);

        foreach (Color32 texel in texels.Distinct())
        {
            Color.RGBToHSV(texel, out float hue, out float saturation, out _);
            Assert.That(hue * 360 >= 175 && hue * 360 <= 200 && saturation > 0.35f, Is.False, $"{what}: {texel} is the player's cyan.");
            Assert.That(Vector3.Distance(Lab(texel), danger), Is.GreaterThanOrEqualTo(20f), $"{what}: {texel} is within ΔE 20 of Danger.");
        }
    }

    // CIE 1976 L*a*b* from sRGB, D65.
    private static Vector3 Lab(Color colour)
    {
        float r = Mathf.GammaToLinearSpace(colour.r), g = Mathf.GammaToLinearSpace(colour.g), b = Mathf.GammaToLinearSpace(colour.b);
        float x = F(((0.4124f * r) + (0.3576f * g) + (0.1805f * b)) / 0.95047f);
        float y = F((0.2126f * r) + (0.7152f * g) + (0.0722f * b));
        float z = F(((0.0193f * r) + (0.1192f * g) + (0.9505f * b)) / 1.08883f);
        return new Vector3((116 * y) - 16, 500 * (x - y), 200 * (y - z));

        static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : (7.787f * t) + (16f / 116f);
    }
}
