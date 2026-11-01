using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

[TestFixture]
public sealed class ShamanModelTests
{
    private const string Art = "Assets/_Project/Art/Enemies/";
    private const string Clips = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_";
    private readonly List<Object> _created = new();
    private GameObject Model => AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Shaman.fbx");
    private GameObject Staff => AssetDatabase.LoadAssetAtPath<GameObject>(Art + "ShamanStaff.fbx");

    [TearDown]
    public void Cleanup()
    { foreach (Object obj in _created) { Object.DestroyImmediate(obj); } _created.Clear(); }

    [Test]
    public void ShamanModel_StaysInTheCrowdsBudget()
    {
        var body = Model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Assert.That(body, Has.Length.EqualTo(1));
        Assert.That(body[0].sharedMesh.triangles.Length / 3, Is.InRange(400, 2500));
        Assert.That(body[0].sharedMesh.subMeshCount, Is.EqualTo(1));
        var staff = Staff.GetComponentsInChildren<MeshRenderer>(true);
        Assert.That(staff, Has.Length.EqualTo(1));
        Assert.That(Staff.GetComponentInChildren<MeshFilter>(true).sharedMesh.triangles.Length / 3, Is.InRange(100, 400));
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Enemies/M_Enemy.mat");
        Assert.That(body[0].sharedMaterial, Is.EqualTo(material));
        Assert.That(staff[0].sharedMaterial, Is.EqualTo(material));
    }

    [Test]
    public void ShamanModel_EveryVertexIsWeighted()
    {
        var body = Model.GetComponentInChildren<SkinnedMeshRenderer>(true);
        foreach (BoneWeight w in body.sharedMesh.boneWeights)
        {
            Assert.That(w.weight0 + w.weight1 + w.weight2 + w.weight3, Is.EqualTo(1).Within(0.001));
            Assert.That(body.bones[w.boneIndex0].name, Is.Not.EqualTo("root"));
            if (w.weight1 > 0)
            { Assert.That(body.bones[w.boneIndex1].name, Is.Not.EqualTo("root")); }
        }
        var rig = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Minion.fbx");
        var expected = rig.GetComponentsInChildren<Transform>(true).Where(t => t != rig.transform).ToDictionary(t => t.name);
        var actual = Model.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
        foreach (var pair in expected.Where(p => p.Key == "Rig_Medium" || p.Key == "root" || p.Key == "head" || p.Key.Contains("slot")))
        {
            Assert.That(actual.ContainsKey(pair.Key), Is.True, pair.Key);
            Assert.That(Vector3.Distance(actual[pair.Key].localPosition, pair.Value.localPosition), Is.LessThan(0.001), pair.Key);
        }
        Assert.That(Model.GetComponentsInChildren<Transform>(true).Count(t => expected.ContainsKey(t.name)), Is.EqualTo(24));
    }

    [TestCase("General", "Idle_B", true)]
    [TestCase("MovementBasic", "Walking_B", true)]
    [TestCase("MovementBasic", "Running_A", true)]
    [TestCase("CombatRanged", "Ranged_Magic_Spellcasting", false)]
    [TestCase("General", "Hit_A", false)]
    [TestCase("General", "Death_A", false)]
    public void ShamanModel_ImportsGenericWithItsClips(string file, string name, bool loops)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(Art + "Shaman.fbx");
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.optimizeGameObjects, Is.False);
        AnimationClip clip = Clip(file, name);
        Assert.That(clip.frameRate, Is.EqualTo(30));
        Assert.That(clip.isLooping, Is.EqualTo(loops));
        GameObject model = Object.Instantiate(Model);
        _created.Add(model);
        Transform root = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "root");
        Vector3 rest = root.localPosition;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        { Assert.That(binding.path.Length == 0 || model.transform.Find(binding.path) != null, Is.True, binding.path); }
        foreach (float time in new[] { 0f, .25f, .5f, .75f, 1f })
        {
            clip.SampleAnimation(model, clip.length * time);
            Assert.That(Vector3.Distance(root.localPosition, rest), Is.LessThan(.001f));
            var mesh = new Mesh();
            _created.Add(mesh);
            model.GetComponentInChildren<SkinnedMeshRenderer>(true).BakeMesh(mesh);
            Assert.That(mesh.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), Is.True);
            Assert.That(mesh.bounds.size.magnitude, Is.InRange(.5f, 4f));
        }
    }

    [Test]
    public void ShamanModel_StaffSitsInTheHand()
    {
        GameObject model = Object.Instantiate(Model);
        GameObject staff = Object.Instantiate(Staff);
        _created.Add(model);
        _created.Add(staff);
        Transform slot = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "handslot.r");
        staff.transform.SetParent(slot, false);
        Clip("General", "Idle_B").SampleAnimation(model, 0);
        Assert.That(Vector3.Distance(staff.transform.position, slot.position), Is.LessThan(.05f));
        Mesh mesh = staff.GetComponentInChildren<MeshFilter>().sharedMesh;
        Vector2[] uv = mesh.uv;
        Vector3[] v = mesh.vertices;
        Clip("CombatRanged", "Ranged_Magic_Spellcasting").SampleAnimation(model, .45f);
        float seed = float.NegativeInfinity;
        for (int i = 0; i < v.Length; i++)
        { if (uv[i].x > .5f && uv[i].x < .625f && uv[i].y > .75f) { seed = Mathf.Max(seed, staff.GetComponentInChildren<MeshFilter>().transform.TransformPoint(v[i]).y); } }
        Transform head = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "head");
        Assert.That(seed, Is.GreaterThan(head.position.y));
    }

    [Test]
    public void ShamanModel_SeedIsTheOnlyGlow()
    {
        Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Textures/T_ShamanStaff_Emission.png");
        Assert.That(mask, Is.Not.Null);
        var staff = Staff.GetComponentInChildren<MeshFilter>().sharedMesh;
        int glowing = 0;
        foreach (Vector2 uv in staff.uv)
        {
            if (uv.x > .5f && uv.x < .625f && uv.y > .75f)
            { glowing++; }
        }
        Assert.That(glowing, Is.InRange(3, staff.vertexCount - 3));
        Assert.That(Model.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial.GetColor("_EmissionColor"), Is.EqualTo(Color.black));
    }

    [Test]
    public void ShamanModel_IsSlimmerThanTheMossguard()
    {
        Mesh body = Model.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
        var guard = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Mossguard.fbx").GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
        Assert.That(body.bounds.size.y / guard.bounds.size.y, Is.InRange(.9f, 1.1f));
        float Width(Mesh mesh)
        {
            var vertices = mesh.vertices.Where(v => v.y > .9f && v.y < 1.2f).ToArray();
            return vertices.Max(v => v.x) - vertices.Min(v => v.x);
        }
        Assert.That(Width(body), Is.LessThan(Width(guard)));
    }

    private static AnimationClip Clip(string file, string name) => AssetDatabase.LoadAllAssetsAtPath(Clips + file + ".fbx").OfType<AnimationClip>().Single(c => c.name == name);
}
