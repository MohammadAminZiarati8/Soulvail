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
    private const string ModelPath = Art + "Shaman.fbx";
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
        Assert.That(body[0].sharedMesh.triangles.Length / 3, Is.EqualTo(179970));
        Assert.That(body[0].sharedMesh.subMeshCount, Is.EqualTo(1));
        var staff = Staff.GetComponentsInChildren<MeshRenderer>(true);
        Assert.That(staff, Has.Length.EqualTo(2));
        Assert.That(Staff.GetComponentsInChildren<MeshFilter>(true).Sum(m => m.sharedMesh.triangles.Length / 3), Is.EqualTo(179996));
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Enemies/M_Shaman.mat");
        Assert.That(body[0].sharedMaterial, Is.EqualTo(material));
        foreach (var renderer in staff)
        {
            Assert.That(renderer.sharedMaterial, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Enemies/M_ShamanStaff.mat")));
        }
        Assert.That(material.GetTexture("_BaseMap"), Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Textures/T_Shaman_Albedo.png")));
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
        var expectedRoot = rig.GetComponentsInChildren<Transform>(true).Single(t => t.name == "root");
        var actualRoot = Model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "root");
        var expected = expectedRoot.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
        var actual = actualRoot.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
        Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
        Assert.That(actual.Count, Is.EqualTo(23));
        foreach (var pair in expected)
        {
            Assert.That(actual[pair.Key].parent.name, Is.EqualTo(pair.Value.parent.name), pair.Key);
        }
    }
    [TestCase("Idle_B", true)]
    [TestCase("Walking_B", true)]
    [TestCase("Running_A", true)]
    [TestCase("Ranged_Magic_Spellcasting", false)]
    [TestCase("Hit_A", false)]
    [TestCase("Death_A", false)]
    public void ShamanModel_ImportsGenericWithItsClips(string name, bool loops)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(Art + "Shaman.fbx");
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.optimizeGameObjects, Is.False);
        AnimationClip clip = Clip(name);
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
        Clip("Idle_B").SampleAnimation(model, 0);
        Assert.That(Vector3.Distance(staff.transform.position, slot.position), Is.LessThan(.05f));
        var seedFilter = staff.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "ShamanStaff_Seed");
        Mesh mesh = seedFilter.sharedMesh;
        Vector3[] v = mesh.vertices;
        Clip("Ranged_Magic_Spellcasting").SampleAnimation(model, .45f);
        float seed = float.NegativeInfinity;
        for (int i = 0; i < v.Length; i++)
        { seed = Mathf.Max(seed, seedFilter.transform.TransformPoint(v[i]).y); }
        Transform head = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "head");
        Assert.That(seed, Is.GreaterThan(head.position.y));
    }

    [Test]
    public void ShamanModel_SeedIsTheOnlyGlow()
    {
        Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Textures/T_ShamanStaff_Emission.png");
        Assert.That(mask, Is.Not.Null);
        Assert.That(mask.width, Is.EqualTo(2));
        Assert.That(mask.GetPixels().All(c => c.r > .99f && c.g > .99f && c.b > .99f), Is.True);
        var filters = Staff.GetComponentsInChildren<MeshFilter>();
        Assert.That(filters.Any(f => f.name == "ShamanStaff_Seed" && f.sharedMesh.triangles.Length > 0), Is.True);
        Assert.That(filters.Any(f => f.name == "ShamanStaff_Wood" && f.sharedMesh.triangles.Length > 0), Is.True);
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

    [Test]
    public void ShamanModel_IdlePreservesTheSourceMesh()
    {
        var instance = Object.Instantiate(Model);
        _created.Add(instance);
        var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
        Vector3[] source = renderer.sharedMesh.vertices;
        Vector2[] uv = renderer.sharedMesh.uv;
        Assert.That(uv.Length, Is.EqualTo(source.Length));
        Assert.That(uv.Distinct().Count(), Is.GreaterThan(10000), "Original source UVs, not constant atlas swatches.");
        Clip("Idle_B").SampleAnimation(instance, 0f);
        var baked = new Mesh();
        _created.Add(baked);
        renderer.BakeMesh(baked);
        Vector3[] idle = baked.vertices;
        float maximum = 0f;
        for (int i = 0; i < source.Length; i++)
        {
            maximum = Mathf.Max(maximum, Vector3.Distance(source[i], idle[i]));
        }
        Assert.That(maximum, Is.LessThan(.002f), "The animation bind must preserve the actual sculpt, not reshape it.");
        Assert.That(renderer.sharedMesh.bounds.size.y, Is.InRange(1.56f, 1.64f));
        Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Textures/T_Shaman_Albedo.png").width, Is.EqualTo(2048));
    }
    [Test]
    public void ShamanModel_SourceSeedGlowStaysColored()
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Shaman.prefab"));
        _created.Add(root);
        var animator = root.GetComponentInChildren<Animator>();
        animator.Rebind();
        animator.Update(0f);
        var hub = new Soulvail.Game.Adapters.DomainEventHub();
        var view = root.GetComponent<Soulvail.Game.Views.ShamanView>();
        root.GetComponent<Soulvail.Game.Views.EnemyView>().Bind(17, Vector3.zero);
        view.Construct(hub);
        hub.Publish(new Soulvail.Core.Events.ShamanCast(17, .6f));
        view.Step(.6f);
        var seed = root.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "ShamanStaff_Seed");
        var wood = root.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "ShamanStaff_Wood");
        var block = new MaterialPropertyBlock();
        seed.GetPropertyBlock(block);
        Color color = block.GetColor("_EmissionColor");
        Assert.That(color.g, Is.GreaterThan(color.r));
        Assert.That(color.r, Is.GreaterThan(color.b));
        Assert.That(color.maxColorComponent, Is.LessThanOrEqualTo(.5f), "Keep the original green seed visible instead of clipping it white.");
        wood.GetPropertyBlock(block);
        Assert.That(block.GetColor("_EmissionColor").maxColorComponent, Is.Zero);
        Assert.That(seed.sharedMaterial.GetTexture("_BaseMap"), Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Textures/T_ShamanStaff_Albedo.png")));
        view.OnDespawn();
        hub.Dispose();
    }
    private static AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Single(c => c.name == name);
}
