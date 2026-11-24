using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VContainer;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Art;

[TestFixture]
public sealed class ColossusModelTests
{
    private const string Project = "Assets/_Project/";
    private const string ModelPath = Project + "Art/Enemies/SundialColossus.fbx";
    private const string PrefabPath = Project + "Prefabs/Enemies/SundialColossus.prefab";
    private const string PrefabGuid = "12cf9b554f57df741b5ea37b6ea612aa";
    private const string Skeleton = "SweepPivot/Rig_Medium/root";
    private const string Arm = Skeleton + "/hips/spine/chest/upperarm.r/lowerarm.r";
    private const string Foot = Skeleton + "/hips/upperleg.r/lowerleg.r/foot.r";
    private GameObject _root;
    private Mesh _baked;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("Colossus model test");
        _baked = new Mesh();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_root);
        Object.DestroyImmediate(_baked);
    }

    [Test]
    public void Body_UsesGroundedRigAndRightWedge()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.motionNodeName, Is.Empty);
        GameObject model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), _root.transform);
        Transform rig = model.transform.Find(Skeleton);
        Assert.That(rig, Is.Not.Null);
        Assert.That(rig.GetComponentsInChildren<Transform>(), Has.Length.EqualTo(25));
        var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
        Assert.That(skin.sharedMesh.triangles.Length / 3, Is.InRange(23000, 24000));
        Assert.That(skin.sharedMesh.uv.Length, Is.EqualTo(skin.sharedMesh.vertexCount));
        Material material = skin.sharedMaterial;
        Assert.That(AssetDatabase.GetAssetPath(material), Is.EqualTo(Project + "Materials/Enemies/M_SundialColossus.mat"));
        Assert.That(material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
        AssertTexture("_BaseMap", "Albedo", true);
        AssertTexture("_MetallicGlossMap", "MetallicSmoothness", false);
        Vector3[] points = BakeInRoot(skin, model.transform);
        Assert.That(points.Min(point => point.y), Is.EqualTo(0f).Within(0.02f));
        Assert.That(points.Max(point => point.y), Is.InRange(3.8f, 4.2f));
        Assert.That(points.Max(point => Mathf.Abs(point.x)), Is.LessThan(1.9f));

        Transform lowerArm = model.transform.Find(Arm);
        Vector3[] blade = RigidPoints(skin, lowerArm, points);
        Vector3[] otherArm = RigidPoints(skin, lowerArm.parent.parent.Find("upperarm.l/lowerarm.l"), points);
        Assert.That(blade.Length, Is.GreaterThan(100));
        Assert.That(blade.Min(point => point.x), Is.GreaterThan(0.7f));
        Assert.That(blade.Max(point => point.y) - blade.Min(point => point.y),
            Is.GreaterThan(1.5f * (otherArm.Max(point => point.y) - otherArm.Min(point => point.y))));
        Assert.That(model.transform.Find(Foot + "/FootContact").position.z, Is.GreaterThan(0f));

        void AssertTexture(string property, string suffix, bool sRgb)
        {
            string path = AssetDatabase.GetAssetPath(material.GetTexture(property));
            Assert.That(path, Is.EqualTo(Project + $"Art/Enemies/T_SundialColossus_{suffix}.png"));
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(path)).sRGBTexture, Is.EqualTo(sRgb));
        }
    }

    [Test]
    public void Clips_MatchEventDrivenAttackPoses()
    {
        GameObject body = Object.Instantiate(Prefab(), _root.transform);
        var animator = body.GetComponentInChildren<Animator>();
        var controller = (AnimatorController)animator.runtimeAnimatorController;
        var states = controller.layers[0].stateMachine.states.ToDictionary(item => item.state.name,
            item => (AnimationClip)item.state.motion);
        Assert.That(states.Keys, Is.EquivalentTo(new[]
        {
            "Idle", "Walk", "SweepTell", "SweepStrike", "ReturnTell", "ReturnStrike", "StompTell", "StompStrike", "Recover"
        }));
        Assert.That(animator.applyRootMotion, Is.False);
        foreach (var (state, clip) in states)
        {
            Assert.That(clip.name, Is.EqualTo(state));
            Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo(ModelPath));
            Assert.That(clip.events, Is.Empty);
            Assert.That(clip.isLooping, Is.EqualTo(state is "Idle" or "Walk"), state);
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings.Any(binding => binding.path.StartsWith(Skeleton)), Is.True, state);
            Assert.That(bindings.All(binding => binding.path != "SweepPivot"
                && animator.transform.Find(binding.path) != null), Is.True, state);
        }

        Transform lowerArm = animator.transform.Find(Arm);
        Transform hand = lowerArm.Find("HandTip");
        Transform foot = animator.transform.Find(Foot);
        var skin = animator.GetComponentInChildren<SkinnedMeshRenderer>();
        foreach (string pass in new[] { "Sweep", "Return" })
        {
            Sample(states[pass + "Tell"], 0.999f);
            Assert.That(BladeHeight(), Is.GreaterThan(0.35f), pass);
            float previousBearing = float.NegativeInfinity;
            for (int sector = 0; sector < 3; sector++)
            {
                Sample(states[pass + "Strike"], (sector + 0.5f) / 3f);
                float bearing = Mathf.Atan2(hand.position.x, hand.position.z) * Mathf.Rad2Deg;
                Assert.That(bearing, Is.GreaterThan(previousBearing), pass);
                Assert.That(BladeHeight(), Is.InRange(-0.03f, 0.08f), pass + " slot " + sector);
                previousBearing = bearing;
            }
        }

        Sample(states["StompTell"], 0.999f);
        Assert.That(FootHeight(), Is.GreaterThan(0.3f));
        Sample(states["StompStrike"], 0.999f);
        Assert.That(FootHeight(), Is.EqualTo(0f).Within(0.025f));

        void Sample(AnimationClip clip, float fraction) => clip.SampleAnimation(animator.gameObject, clip.length * fraction);
        float BladeHeight() => RigidPoints(skin, lowerArm, BakeInRoot(skin, body.transform)).Min(point => point.y);
        float FootHeight() => RigidPoints(skin, foot, BakeInRoot(skin, body.transform)).Min(point => point.y);
    }

    [Test]
    public void ModelSwap_PreservesFightBindings()
    {
        GameObject prefab = Prefab();
        Assert.That(AssetDatabase.GUIDToAssetPath(PrefabGuid), Is.EqualTo(PrefabPath));
        Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(prefab.GetComponentsInChildren<Collider>(), Has.Length.EqualTo(2));
        var capsule = prefab.GetComponent<CapsuleCollider>();
        Assert.That(capsule.isTrigger, Is.True);
        Assert.That(capsule.radius, Is.EqualTo(0.94f));
        Assert.That(capsule.height, Is.EqualTo(3.9f));
        Assert.That(capsule.center, Is.EqualTo(Vector3.up * 1.95f));
        var motor = prefab.GetComponent<CharacterController>();
        Assert.That(motor.radius, Is.EqualTo(0.94f));
        Assert.That(motor.height, Is.EqualTo(3.5f));
        Assert.That(motor.center, Is.EqualTo(Vector3.up * 1.75f));
        Assert.That(AssetDatabase.GetAssetPath(prefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh),
            Is.EqualTo(ModelPath));

        using var events = new DomainEventHub();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(events);
        using IObjectResolver resolver = builder.Build();
        using var pool = new ViewPool<EnemyView>(resolver, prefab.GetComponent<EnemyView>(), _root.transform, 1);
        EnemyView body = pool.Get();
        body.Bind(17, Vector3.zero);
        var view = body.GetComponent<ColossusView>();
        view.Step(0f);
        var animator = body.GetComponentInChildren<Animator>();
        Transform hand = animator.transform.Find(Arm + "/HandTip");
        Vector3 neutral = body.transform.InverseTransformPoint(hand.position);
        events.Publish(new ColossusArmed(17, ColossusMove.Sweep, NVector3.Zero, NVector2.UnitY, 9.5f, 20f, 1.35f));
        var warning = (MeshRenderer)typeof(ColossusView)
            .GetField("_warning", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
        Assert.That(warning.enabled, Is.True);
        Assert.That(warning.transform.parent, Is.Null);
        Assert.That(warning.gameObject.scene, Is.EqualTo(body.gameObject.scene));
        Assert.That(body.GetComponent<AudioSource>().clip, Is.Not.Null);
        events.Publish(new ColossusImpact(17, ColossusMove.Sweep, 2, 1.8f));
        Assert.That(Vector3.Distance(body.transform.InverseTransformPoint(hand.position), neutral), Is.GreaterThan(0.5f));

        // EditMode doesn't dispatch OnDisable or OnEnable to the pooled view.
        view.OnDespawn();
        pool.Release(body);
        EnemyView reused = pool.Get();
        Assert.That(reused, Is.SameAs(body));
        reused.Bind(18, Vector3.right);
        view.Step(0f);
        Assert.That(warning.enabled, Is.False);
        Assert.That(body.GetComponent<AudioSource>().clip, Is.Null);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);
        Assert.That(Vector3.Distance(reused.transform.InverseTransformPoint(hand.position), neutral), Is.LessThan(0.001f));
    }

    private Vector3[] BakeInRoot(SkinnedMeshRenderer skin, Transform root)
    {
        skin.BakeMesh(_baked);
        Matrix4x4 toRoot = root.worldToLocalMatrix * skin.transform.localToWorldMatrix;
        return _baked.vertices.Select(toRoot.MultiplyPoint3x4).ToArray();
    }

    private static Vector3[] RigidPoints(SkinnedMeshRenderer skin, Transform bone, Vector3[] points)
    {
        int index = System.Array.IndexOf(skin.bones, bone);
        Assert.That(index, Is.GreaterThanOrEqualTo(0), bone.name);
        BoneWeight[] weights = skin.sharedMesh.boneWeights;
        return points.Where((_, vertex) => weights[vertex].boneIndex0 == index && weights[vertex].weight0 > 0.99f)
            .ToArray();
    }

    private static GameObject Prefab() => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
}
