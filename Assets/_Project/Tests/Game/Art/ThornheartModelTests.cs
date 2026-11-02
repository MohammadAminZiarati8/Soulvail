using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Soulvail.Game.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>JG-20f: Thornheart's rigid parts, clips and paint, and its thorned seed pod.</summary>
[TestFixture]
public sealed class ThornheartModelTests
{
    private const string Art = "Assets/_Project/Art/Enemies/Thornheart/";
    private const string ModelPath = Art + "Thornheart.fbx";
    private const string PodModelPath = Art + "ThornPod.fbx";
    private const string Prefabs = "Assets/_Project/Prefabs/Enemies/";
    private const string Materials = "Assets/_Project/Materials/Enemies/";
    private const string ClipPrefix = "Assets/_Project/Animation/Clips/A_Thornheart_";
    private const string GroundPath = "Assets/_Project/Art/Environment/Jungle/Textures/T_JungleGround_Albedo.png";

    // Full detail like the Shaman (owner, 2026-11-02), not the crowd budget; not yet measured on a phone.
    private const int BodyTriangleCeiling = 200000;
    private const int PodTriangleCeiling = 100000;

    private static readonly string[] Parts = { "Trunk", "Crown", "Heart", "Root_0", "Root_1", "Root_2", "Root_3" };
    private static readonly string[] ClipNames = { "Idle", "Roots", "Slam", "Plant", "Exposed", "Hit", "Death" };

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void DestroyCreatedObjects()
    {
        foreach (Object created in _created)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        _created.Clear();
    }

    // Rule 1: an Enemy variant; the parts sit under Model at scale 1, and the base mesh is parked.
    [Test]
    public void Thornheart_IsAVariantOfEnemy_HasTheParts()
    {
        GameObject prefab = Load<GameObject>(Prefabs + "Thornheart.prefab");
        Transform model = prefab.transform.Find("Model");

        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab), Is.SameAs(Load<GameObject>(Prefabs + "Enemy.prefab")));
        Assert.That(prefab.transform.Find("Mesh").gameObject.activeSelf, Is.False);
        Assert.That(model, Is.Not.Null);
        Assert.That(model.localScale, Is.EqualTo(Vector3.one));
        Assert.That(model.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty, "Rigid parts, no skeleton.");
        Assert.That(model.Cast<Transform>().Select(t => t.name), Is.EquivalentTo(Parts));

        foreach (string part in Parts)
        {
            Assert.That(model.Find(part).GetComponent<MeshFilter>().sharedMesh, Is.Not.Null, part);
            Assert.That(model.Find(part).GetComponent<MeshRenderer>().sharedMaterial,
                Is.SameAs(Load<Material>(Materials + "M_Thornheart.mat")), part);
        }
    }

    // Rule 2: every rebuilt part is one closed shell, welded across its UV seams.
    [Test]
    public void Thornheart_Closed_NoBoundaryEdges()
    {
        foreach (MeshFilter filter in Load<GameObject>(ModelPath).GetComponentsInChildren<MeshFilter>(true)
            .Concat(Load<GameObject>(PodModelPath).GetComponentsInChildren<MeshFilter>(true)))
        {
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            var welded = new Dictionary<Vector3Int, int>();
            int[] index = vertices.Select(v =>
            {
                var key = Vector3Int.RoundToInt(v * 1e4f);
                if (!welded.TryGetValue(key, out int id))
                {
                    id = welded.Count;
                    welded[key] = id;
                }

                return id;
            }).ToArray();
            var edges = new Dictionary<long, int>();
            int[] triangles = mesh.triangles;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = index[triangles[t + k]], b = index[triangles[t + ((k + 1) % 3)]];
                    long edge = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                    edges[edge] = edges.TryGetValue(edge, out int count) ? count + 1 : 1;
                }
            }

            Assert.That(edges.Values.Count(c => c != 2), Is.Zero, $"{filter.name} has open or non-manifold edges.");
        }
    }

    // Rule 3: the heart is its own renderer in the crater, open to the sky, on an emissive material.
    [Test]
    public void Thornheart_HeartHasEmissiveRenderer()
    {
        Transform model = Load<GameObject>(Prefabs + "Thornheart.prefab").transform.Find("Model");
        var heart = model.Find("Heart").GetComponent<MeshRenderer>();
        Bounds crater = heart.bounds;
        Bounds trunk = model.Find("Trunk").GetComponent<MeshRenderer>().bounds;

        Assert.That(heart.sharedMaterial.IsKeywordEnabled("_EMISSION"), Is.True);
        Assert.That(heart.sharedMaterial.GetColor("_EmissionColor"), Is.EqualTo(Color.black), "Dark until 20g lights it.");
        Assert.That(crater.center.y, Is.GreaterThan(trunk.center.y + trunk.extents.y * 0.6f), "Atop the trunk.");
        Assert.That(new Vector2(crater.center.x, crater.center.z).magnitude, Is.LessThan(0.3f), "Centred in the crater.");
        Assert.That(crater.max.y, Is.GreaterThanOrEqualTo(model.Cast<Transform>()
            .Where(t => t.name != "Crown").Max(t => t.GetComponent<MeshRenderer>().bounds.max.y)), "Nothing covers it but leaves.");
    }

    // Rule 4: seven Generic transform clips, named exactly; Idle loops; no events; the root stays empty.
    [Test]
    public void Thornheart_HasTheSevenClips_NoEvents_NoRootMotion()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        GameObject prefab = Load<GameObject>(Prefabs + "Thornheart.prefab");
        var model = (GameObject)Object.Instantiate(prefab.transform.Find("Model").gameObject);
        _created.Add(model);

        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.optimizeGameObjects, Is.False);
        Assert.That(importer.motionNodeName, Is.Empty);
        Assert.That(prefab.GetComponentInChildren<Animator>(true).applyRootMotion, Is.False);

        foreach (string name in ClipNames)
        {
            AnimationClip clip = Load<AnimationClip>(ClipPrefix + name + ".anim");
            Vector3 rest = model.transform.localPosition;
            bool moved = false;

            Assert.That(clip.frameRate, Is.EqualTo(30f), name);
            Assert.That(clip.isLooping, Is.EqualTo(name == "Idle"), name);
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty, name);
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                Assert.That(binding.path, Is.Not.Empty, $"{name} animates the root.");
                Assert.That(model.transform.Find(binding.path), Is.Not.Null, $"{name} binds '{binding.path}'.");
            }

            Quaternion[] start = Parts.Select(p => model.transform.Find(p).localRotation).ToArray();
            Vector3[] startScale = Parts.Select(p => model.transform.Find(p).localScale).ToArray();
            for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * clip.frameRate); frame++)
            {
                clip.SampleAnimation(model, frame / clip.frameRate);
                Assert.That(model.transform.localPosition, Is.EqualTo(rest), $"{name} moves the root at {frame}.");
                for (int p = 0; p < Parts.Length; p++)
                {
                    Transform part = model.transform.Find(Parts[p]);
                    moved |= Quaternion.Angle(start[p], part.localRotation) > 1f || (startScale[p] - part.localScale).magnitude > 0.02f;
                }
            }

            Assert.That(moved, Is.True, $"{name} must move a part.");
        }
    }

    // Rule 5: no player cyan, nothing near Danger, the heart gold and the bark under the Jungle floor.
    [Test]
    public void Thornheart_PaintHasNoCyanAndNoDanger()
    {
        Color32[] paint = Pixels(Art + "T_Thornheart_Albedo.png");
        AssertLawful(paint, "Thornheart");
        AssertLawful(Pixels(Art + "T_ThornPod_Albedo.png"), "ThornPod");

        float bark = Median(paint.Select(Luma));
        float floor = Pixels(GroundPath).Average(Luma);
        Assert.That(bark, Is.LessThan(floor), "Bark reads darker than the Jungle floor.");

        Color32 heart = Mean(Load<GameObject>(ModelPath).transform.Find("Heart").GetComponent<MeshFilter>().sharedMesh, paint, 2048);
        Color.RGBToHSV(heart, out float hue, out float saturation, out float value);
        Assert.That(hue * 360, Is.InRange(35f, 60f), $"Gold heart, not {heart}.");
        Assert.That(saturation, Is.GreaterThan(0.4f));
        Assert.That(value, Is.GreaterThan(0.5f), "A lit gold, not a dark ochre.");
    }

    // Rule 5: the pod is the bud's variant with its own thorned shell and tint, the jug parked.
    [Test]
    public void ThornPod_IsABudVariant_OwnTint()
    {
        GameObject pod = Load<GameObject>(Prefabs + "ThornPod.prefab");
        Transform model = pod.transform.Find("Model");
        var shell = model.GetComponentsInChildren<MeshRenderer>(true).Single();

        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(pod), Is.SameAs(Load<GameObject>(Prefabs + "Pitcher_Bud.prefab")));
        Assert.That(model.Find("Pitcher_Body").gameObject.activeSelf, Is.False);
        Assert.That(shell.gameObject.activeSelf, Is.True);
        Assert.That(shell.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(Load<GameObject>(PodModelPath).GetComponentInChildren<MeshFilter>().sharedMesh));
        Assert.That(shell.sharedMaterial, Is.SameAs(Load<Material>(Materials + "M_ThornPod.mat")));
        Assert.That(shell.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(Load<Texture2D>(Art + "T_ThornPod_Albedo.png")));
        Assert.That(shell.sharedMaterial, Is.Not.SameAs(model.Find("Pitcher_Body").GetComponent<Renderer>().sharedMaterial));
        Assert.That(shell.transform.IsChildOf(model.Find("Rig_Pitcher")), Is.True, "Rides the bud's guard idle.");
    }

    // Rule 6: full detail within the owner's ceiling; one submesh and normals per part.
    [Test]
    public void Thornheart_StaysInBudget()
    {
        MeshFilter[] body = Load<GameObject>(ModelPath).GetComponentsInChildren<MeshFilter>(true);
        Mesh pod = Load<GameObject>(PodModelPath).GetComponentInChildren<MeshFilter>(true).sharedMesh;

        Assert.That(body.Sum(f => f.sharedMesh.triangles.Length / 3), Is.InRange(100000, BodyTriangleCeiling));
        Assert.That(pod.triangles.Length / 3, Is.InRange(20000, PodTriangleCeiling));
        foreach (Mesh mesh in body.Select(f => f.sharedMesh).Append(pod))
        {
            Assert.That(mesh.subMeshCount, Is.EqualTo(1), mesh.name);
            Assert.That(mesh.normals, Has.Length.EqualTo(mesh.vertexCount), mesh.name);
        }
    }

    private static T Load<T>(string path)
        where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);

        Assert.That(asset, Is.Not.Null, path);

        return asset;
    }

    private Color32[] Pixels(string path)
    {
        var texture = new Texture2D(2, 2);
        _created.Add(texture);
        texture.LoadImage(File.ReadAllBytes(path));
        return texture.GetPixels32();
    }

    private static float Luma(Color32 c) => ((0.2126f * c.r) + (0.7152f * c.g) + (0.0722f * c.b)) / 255f;

    private static float Median(IEnumerable<float> values)
    {
        float[] sorted = values.OrderBy(v => v).ToArray();
        return sorted[sorted.Length / 2];
    }

    private static Color32 Mean(Mesh mesh, Color32[] pixels, int size)
    {
        Color32[] texels = mesh.uv.Select(uv => pixels[(Mathf.Clamp((int)(uv.y * size), 0, size - 1) * size) + Mathf.Clamp((int)(uv.x * size), 0, size - 1)]).ToArray();
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
