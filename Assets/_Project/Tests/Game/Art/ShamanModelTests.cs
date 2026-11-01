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
        AssetDatabase.LoadAssetAtPath<AnimationClip>(CastPath).SampleAnimation(model, .45f);
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
    [TestCase(WalkPath)]
    [TestCase(RunPath)]
    public void ShamanGait_BobsTheHipsAndPlantsTheFeet(string path)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        GameObject model = Object.Instantiate(Model);
        _created.Add(model);
        Transform hips = Bone(model, "hips");
        float ground = LowestFoot(model, Clip("Idle_B"));
        float low = float.MaxValue, high = float.MinValue;

        for (int i = 0; i <= Samples; i++)
        {
            clip.SampleAnimation(model, clip.length * i / Samples);
            float y = model.transform.InverseTransformPoint(hips.position).y;
            low = Mathf.Min(low, y);
            high = Mathf.Max(high, y);
        }

        Assert.That(clip.isLooping, Is.True);
        Assert.That(clip.frameRate, Is.EqualTo(30));
        Assert.That(high - low, Is.GreaterThan(.02f), "The hips ride the steps, not frozen as JG-16g's clips were.");
        Assert.That(LowestFoot(model, clip), Is.EqualTo(ground).Within(.005f), "Feet touch the idle's ground, neither sinking nor floating.");
    }

    [TestCase(WalkPath, true)]
    [TestCase(RunPath, true)]
    [TestCase(HitPath, false)]
    [TestCase(CastPath, false)]
    [TestCase(DeathPath, false)]
    public void ShamanClips_PlayWithoutAPop(string path, bool loops)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        GameObject model = Object.Instantiate(Model);
        _created.Add(model);
        Transform[] bones = model.GetComponentsInChildren<Transform>(true);
        var previous = new Quaternion[bones.Length];
        int frames = Mathf.RoundToInt(clip.length * clip.frameRate);

        Assert.That(clip.isLooping, Is.EqualTo(loops));

        // Every frame in order; a loop then goes from the last back to the first, the seam it plays every cycle.
        for (int f = 0; f <= frames; f++)
        {
            clip.SampleAnimation(model, (loops ? f % frames : f) / clip.frameRate);

            for (int b = 0; b < bones.Length; b++)
            {
                if (f > 0)
                {
                    // KayKit's own knee turns up to ~51° a frame; JG-16g's flips were 170°+.
                    Assert.That(Quaternion.Angle(previous[b], bones[b].localRotation), Is.LessThan(60f), $"{bones[b].name} at frame {f}");
                }

                previous[b] = bones[b].localRotation;
            }
        }
    }

    [Test]
    public void ShamanGait_StaffArmKeepsTheIdleGrip()
    {
        GameObject model = Object.Instantiate(Model);
        _created.Add(model);
        Clip("Idle_B").SampleAnimation(model, 0f);
        string[] arm = { "upperarm.r", "lowerarm.r", "wrist.r", "hand.r", "handslot.r" };
        Quaternion[] grip = arm.Select(name => Bone(model, name).localRotation).ToArray();

        foreach (string path in new[] { WalkPath, RunPath })
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

            foreach (float time in new[] { 0f, .25f, .5f, .75f })
            {
                clip.SampleAnimation(model, clip.length * time);

                for (int i = 0; i < arm.Length; i++)
                {
                    Assert.That(Quaternion.Angle(grip[i], Bone(model, arm[i]).localRotation), Is.LessThan(.5f), $"{path} {arm[i]} at {time}");
                }
            }
        }
    }

    [Test]
    public void ShamanGait_StridesMatchTheClips()
    {
        var view = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponentInChildren<Soulvail.Game.Views.EnemyAnimatorView>(true);
        var fields = new SerializedObject(view);
        float walkStride = fields.FindProperty("_strideSpeed").floatValue;
        float runStride = fields.FindProperty("_runStrideSpeed").floatValue;
        float runBase = fields.FindProperty("_runBaseSpeed").floatValue;
        GameObject model = Object.Instantiate(Model);
        _created.Add(model);

        Assert.That(walkStride, Is.EqualTo(PlantedFootSpeed(model, AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkPath))).Within(.05f));
        Assert.That(runStride, Is.EqualTo(PlantedFootSpeed(model, AssetDatabase.LoadAssetAtPath<AnimationClip>(RunPath))).Within(.1f));
        Assert.That(runBase * 1.2f, Is.LessThan(2f), "The Shaman's own 2 m/s plays the run, not a walk blurred past the ceiling.");

        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
        UnityEditor.Animations.AnimatorState[] states = controller.layers[0].stateMachine.states.Select(s => s.state).ToArray();
        var tree = (UnityEditor.Animations.BlendTree)states.Single(s => s.name == "Locomotion").motion;
        Assert.That(tree.children[1].motion, Is.EqualTo(AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkPath)));
        Assert.That(tree.children[1].threshold, Is.EqualTo(walkStride).Within(1e-4f));
        Assert.That(states.Single(s => s.name == "Run").motion, Is.EqualTo(AssetDatabase.LoadAssetAtPath<AnimationClip>(RunPath)));
    }

    [Test]
    public void ShamanActions_PlayTheRebuiltClips()
    {
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
        UnityEditor.Animations.AnimatorState[] states = controller.layers
            .SelectMany(layer => layer.stateMachine.states.Select(s => s.state)).ToArray();
        var shaman = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponentInChildren<Soulvail.Game.Views.ShamanView>(true);
        AnimationClip cast = AssetDatabase.LoadAssetAtPath<AnimationClip>(CastPath);

        Assert.That(states.Single(s => s.name == "Hit").motion, Is.EqualTo(AssetDatabase.LoadAssetAtPath<AnimationClip>(HitPath)));
        Assert.That(states.Single(s => s.name == "Cast").motion, Is.EqualTo(cast));
        Assert.That(states.Single(s => s.name == "Death").motion, Is.EqualTo(AssetDatabase.LoadAssetAtPath<AnimationClip>(DeathPath)));
        Assert.That(new SerializedObject(shaman).FindProperty("_castClipSeconds").floatValue, Is.EqualTo(cast.length).Within(1e-3f),
            "The cast rate stretches this clip over core's cast.");
    }

    private const string WalkPath = "Assets/_Project/Animation/Clips/A_Shaman_Walk.anim";
    private const string RunPath = "Assets/_Project/Animation/Clips/A_Shaman_Run.anim";
    private const string HitPath = "Assets/_Project/Animation/Clips/A_Shaman_Hit.anim";
    private const string CastPath = "Assets/_Project/Animation/Clips/A_Shaman_Cast.anim";
    private const string DeathPath = "Assets/_Project/Animation/Clips/A_Shaman_Death.anim";
    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Shaman.prefab";
    private const string ControllerPath = "Assets/_Project/Animation/Controllers/AC_Shaman.controller";
    private const int Samples = 120;

    private static Transform Bone(GameObject model, string name) => model.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);

    private static float LowestFoot(GameObject model, AnimationClip clip)
    {
        Transform left = Bone(model, "foot.l"), right = Bone(model, "foot.r");
        float lowest = float.MaxValue;

        for (int i = 0; i <= Samples; i++)
        {
            clip.SampleAnimation(model, clip.length * i / Samples);
            lowest = Mathf.Min(lowest, model.transform.InverseTransformPoint(left.position).y, model.transform.InverseTransformPoint(right.position).y);
        }

        return lowest;
    }

    /// <summary>How fast a foot on the ground slides back under the body: the ground the clip covers at 1x.</summary>
    private static float PlantedFootSpeed(GameObject model, AnimationClip clip)
    {
        float sum = 0f;
        int count = 0;

        foreach (string name in new[] { "foot.l", "foot.r" })
        {
            Transform foot = Bone(model, name);
            var heights = new float[Samples + 1];
            var depths = new float[Samples + 1];

            for (int i = 0; i <= Samples; i++)
            {
                clip.SampleAnimation(model, clip.length * i / Samples);
                Vector3 local = model.transform.InverseTransformPoint(foot.position);
                heights[i] = local.y;
                depths[i] = local.z;
            }

            float floor = heights.Min() + .02f;

            for (int i = 0; i < Samples; i++)
            {
                if (heights[i] < floor && heights[i + 1] < floor)
                {
                    sum += (depths[i] - depths[i + 1]) * Samples / clip.length;
                    count++;
                }
            }
        }

        return sum / count;
    }

    private static AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Single(c => c.name == name);
}
