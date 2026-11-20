using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using VContainer;
using Num = System.Numerics;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

public sealed class HierophantViewTests
{
    private const string Project = "Assets/_Project/";
    private const string BodyModel = Project + "Art/Enemies/Hierophant.fbx";
    private const string SealModel = Project + "Art/Environment/HierophantSeal.fbx";
    private const string SealPrefab = Project + "Prefabs/Arenas/Sunscar/HierophantSeal.prefab";
    private const int Boss = 7;
    private readonly ContentId _spec = new("enemy.hierophant");
    private GameObject _prefab;
    private DomainEventHub _events;
    private IObjectResolver _container;
    private ViewPool<EnemyView> _pool;
    private EnemyView _body;
    private HierophantView _view;
    private Animator _animator;
    private AudioSource _audio;
    private Transform _floor;
    private Scene _scene;

    [SetUp]
    public void SetUp()
    {
        _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Project + "Prefabs/Enemies/Hierophant.prefab");
        Assert.That(_prefab, Is.Not.Null);
        _events = new DomainEventHub();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(_events);
        var random = new FixedRandom(7);
        var recorder = new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), _events);
        builder.RegisterInstance<IRunSession>(new RunSession(new ContentCatalog(
                Array.Empty<CharacterSpec>(), Array.Empty<EnemySpec>(), Array.Empty<ModeSpec>()),
            random, _events, new RecordingIntents(), recorder, 8, 8, 8));
        _container = builder.Build();
        _scene = EditorSceneManager.NewPreviewScene();
        var parent = new GameObject("Bodies");
        SceneManager.MoveGameObjectToScene(parent, _scene);
        _pool = new ViewPool<EnemyView>(_container, _prefab.GetComponent<EnemyView>(), parent.transform, 0);
        _body = _pool.Get();
        InvokeLifecycle(_body.Feedback, "Awake");
        _body.Bind(Boss, new Vector3(3f, 0f, -2f));
        _view = _body.GetComponent<HierophantView>();
        _animator = _body.GetComponentInChildren<Animator>();
        _audio = _body.GetComponent<AudioSource>();
    }

    [TearDown]
    public void TearDown()
    {
        if (_view != null)
            InvokeLifecycle(_view, "OnDestroy");
        _pool?.Dispose();
        _container?.Dispose();
        _events?.Dispose();
        if (_scene.IsValid())
            EditorSceneManager.ClosePreviewScene(_scene);
    }

    [Test]
    public void Body_BindsRigAndSeparateStaff()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(BodyModel);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.motionNodeName, Is.Null.Or.Empty);
        Assert.That(importer.importAnimation, Is.False);
        Assert.That(_animator.avatar.isValid, Is.True);
        Assert.That(_animator.applyRootMotion, Is.False);
        var mesh = _animator.GetComponentInChildren<SkinnedMeshRenderer>();
        Assert.That(AssetDatabase.GetAssetPath(mesh.sharedMesh), Is.EqualTo(BodyModel));
        Assert.That(mesh.sharedMesh.GetIndexCount(0) / 3, Is.InRange(1500, 3900));
        var atlas = AssetDatabase.LoadAssetAtPath<Material>(Project + "Materials/Enemies/M_Enemy.mat");
        Assert.That(mesh.sharedMaterials, Is.EqualTo(new[] { atlas }));
        var priest = AssetDatabase.LoadAssetAtPath<GameObject>(Project + "Art/Enemies/JackalPriest.fbx");
        Transform[] prototypeBones = priest.transform.Find("Rig_Medium/root").GetComponentsInChildren<Transform>();
        Assert.That(prototypeBones, Has.Length.EqualTo(23));
        foreach (Transform prototype in prototypeBones)
        {
            string path = AnimationUtility.CalculateTransformPath(prototype, priest.transform);
            Transform bone = _animator.transform.Find(path);
            Assert.That(bone, Is.Not.Null, path);
            Assert.That(Vector3.Distance(bone.localPosition, prototype.localPosition), Is.LessThan(.001f), path);
            Assert.That(Quaternion.Angle(bone.localRotation, prototype.localRotation), Is.LessThan(.1f), path);
        }

        Transform staff = _animator.GetComponentInChildren<MeshFilter>().transform;
        Assert.That(staff.parent.name, Is.EqualTo("handslot.r"));
        Assert.That(AssetDatabase.GetAssetPath(staff.GetComponent<MeshFilter>().sharedMesh),
            Is.EqualTo(Project + "Art/Enemies/PriestStaff.fbx"));
        Assert.That(staff.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(staff.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(staff.localScale, Is.EqualTo(Vector3.one));
        Assert.That(staff.Find("Glow"), Is.Not.Null);

        var controller = (AnimatorController)_animator.runtimeAnimatorController;
        ChildAnimatorState[] states = controller.layers[0].stateMachine.states;
        Assert.That(states.Select(state => state.state.name),
            Is.EquivalentTo(new[] { "Idle", "Walk", "ThrowTell", "ThrowRelease", "SealTell", "SealRelease", "Recovery", "Death" }));
        Motion sealTell = states.Single(state => state.state.name == "SealTell").state.motion;
        Motion sealRelease = states.Single(state => state.state.name == "SealRelease").state.motion;
        Assert.That(sealTell.name, Is.EqualTo("Melee_1H_Attack_Chop"));
        Assert.That(sealRelease, Is.SameAs(sealTell));
        Assert.That(new SerializedObject(_view).FindProperty("_sealMotion").objectReferenceValue, Is.SameAs(sealTell));
        Assert.That(controller.animationClips.All(clip => clip.events.Length == 0), Is.True);
        Assert.That(states.Single(state => state.state.name == "ThrowRelease").state.motion.name,
            Is.EqualTo("Ranged_Magic_Shoot"));
        var baked = new Mesh();
        try
        {
            mesh.BakeMesh(baked);
            float ground = baked.vertices.Min(vertex => mesh.transform.TransformPoint(vertex).y) - _body.transform.position.y;
            Assert.That(ground, Is.Zero.Within(.02f));
            Vector3 restGrip = staff.position;
            Vector3 modelAnchor = _animator.transform.position;
            foreach (AnimationClip clip in states.Select(state => (AnimationClip)state.state.motion).Distinct())
            {
                clip.SampleAnimation(_animator.gameObject, .57f * clip.length);
                Assert.That(Vector3.Distance(staff.position, restGrip), Is.GreaterThan(.01f), clip.name);
                Assert.That(staff.localPosition, Is.EqualTo(Vector3.zero), clip.name);
                Assert.That(_animator.transform.position, Is.EqualTo(modelAnchor), clip.name);
                mesh.BakeMesh(baked);
                Assert.That(baked.bounds.size.sqrMagnitude, Is.GreaterThan(.5f), clip.name);
            }
        }
        finally
        {
            Object.DestroyImmediate(baked);
        }
    }

    [Test]
    public void Seal_BindsFinalMeshAndIndependentLight()
    {
        var seal = AssetDatabase.LoadAssetAtPath<GameObject>(SealPrefab);
        Assert.That(seal.GetComponentsInChildren<Collider>(true), Is.Empty);
        Assert.That(seal.GetComponentsInChildren<NavMeshObstacle>(true), Is.Empty);
        Assert.That(seal.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
        Assert.That(seal.GetComponentsInChildren<MeshRenderer>(true), Has.Length.EqualTo(3));
        var stone = seal.transform.Find("Sandstone plaque").GetComponent<MeshFilter>();
        var carving = seal.transform.Find("Sun carving").GetComponent<MeshFilter>();
        var light = seal.transform.Find("Light").GetComponent<MeshFilter>();
        Assert.That(AssetDatabase.GetAssetPath(stone.sharedMesh), Is.EqualTo(SealModel));
        Assert.That(AssetDatabase.GetAssetPath(carving.sharedMesh), Is.EqualTo(SealModel));
        Assert.That(carving.sharedMesh, Is.Not.SameAs(stone.sharedMesh));
        Assert.That(light.sharedMesh, Is.SameAs(carving.sharedMesh));
        Bounds bounds = stone.GetComponent<MeshRenderer>().bounds;
        Assert.That(bounds.center.x, Is.Zero.Within(.005f));
        Assert.That(bounds.center.z, Is.Zero.Within(.005f));
        Assert.That(bounds.max.y, Is.InRange(.005f, .02f));
        Assert.That(bounds.min.y, Is.LessThan(0f));
        Assert.That(carving.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_EmissionColor").maxColorComponent,
            Is.Zero);
        Assert.That(light.GetComponent<MeshRenderer>().enabled, Is.False);

        Place(Boss);
        MeshRenderer[] lights = SealLights();
        Assert.That(lights, Has.Length.EqualTo(3));
        foreach (MeshRenderer instance in lights)
        {
            Assert.That(instance.transform.parent.Find("Sandstone plaque").GetComponent<MeshFilter>().sharedMesh,
                Is.SameAs(stone.sharedMesh));
            Assert.That(instance.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(carving.sharedMesh));
        }

        _events.Publish(Arm(Boss, HierophantMove.SunSeal));
        Assert.That(Lights(), Is.EqualTo(new[] { false, false, true }));
    }

    [Test]
    public void ModelSwap_PreservesFightBindings()
    {
        var capsule = _prefab.GetComponent<CapsuleCollider>();
        var controller = _prefab.GetComponent<CharacterController>();
        Assert.That(capsule.isTrigger, Is.True);
        Assert.That(capsule.center, Is.EqualTo(Vector3.up * .8f));
        Assert.That(capsule.height, Is.EqualTo(1.6f));
        Assert.That(capsule.radius, Is.EqualTo(.4f));
        Assert.That(controller.center, Is.EqualTo(capsule.center));
        Assert.That(controller.height, Is.EqualTo(capsule.height));
        Assert.That(controller.radius, Is.EqualTo(capsule.radius));
        Assert.That(_animator.transform.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(_animator.transform.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(_animator.transform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(_audio.playOnAwake, Is.False);
        Assert.That(_prefab.GetComponent<PriestMendView>(), Is.Null);
        Assert.That(_animator.GetComponent<EnemyAnimatorView>(), Is.Null);
        var feedback = new SerializedObject(_prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue,
            Is.SameAs(_prefab.GetComponentInChildren<SkinnedMeshRenderer>()));
        Place(Boss);
        Assert.That(SealPositions(), Is.EqualTo(new[] { Vector3.left * 4f, Vector3.zero, Vector3.right * 4f }));
        _events.Publish(Arm(Boss, HierophantMove.FireThrow));
        Assert.That(_audio.clip.name, Is.EqualTo("DuneclawSweep"));
        AudioClip sweep = _audio.clip;
        _events.Publish(Arm(Boss, HierophantMove.SunSeal));
        Assert.That(_audio.clip.name, Is.EqualTo("SFX_ShardbackFan"));
        Assert.That(_audio.clip, Is.Not.SameAs(sweep));
    }

    [TestCase(HierophantMove.FireThrow)]
    [TestCase(HierophantMove.SunSeal)]
    public void Tell_MatchesCoreAndStaysInPlace(HierophantMove move)
    {
        Place(Boss);
        HierophantArmed armed = Arm(Boss, move);
        _events.Publish(armed);
        MeshFilter warning = Layer("Warning");
        MeshFilter progress = Layer("Progress");
        MeshFilter release = Layer("Release");
        bool fire = move == HierophantMove.FireThrow;
        AssertPose(fire ? "ThrowTell" : "SealTell");
        Assert.That(Visible(warning), Is.True);
        Assert.That(Visible(progress), Is.False);
        Assert.That(Visible(release), Is.False);
        Assert.That(Lights(), Is.EqualTo(new[] { false, false, !fire }));
        AssertFootprint(warning, armed, 1f);
        Vector3[] warningWorld = WorldVertices(warning);
        Vector3[] seals = SealPositions();

        _body.transform.SetPositionAndRotation(new Vector3(-2f, 0f, 6f), Quaternion.Euler(0f, 75f, 0f));
        _body.transform.localScale = Vector3.one * 1.75f;
        _view.Step(0.6f);
        Assert.That(Visible(progress), Is.False);
        _events.Publish(new HierophantTellProgress(Boss, 0.4f));
        Assert.That(Visible(progress), Is.True);
        AssertFootprint(progress, armed, 0.4f);
        Assert.That(progress.sharedMesh.colors[0].a, Is.GreaterThan(warning.sharedMesh.colors[0].a));
        Assert.That(WorldVertices(warning), Is.EqualTo(warningWorld));
        Assert.That(SealPositions(), Is.EqualTo(seals));

        _events.Publish(new HierophantImpact(Boss, move, fire ? 1 : 0, 1f));
        Assert.That(Visible(warning), Is.True);
        _events.Publish(new HierophantImpact(Boss, move, armed.SealSlot, 1f));
        Assert.That(Visible(warning), Is.False);
        Assert.That(Visible(progress), Is.False);
        Assert.That(_audio.clip, Is.Null);
        AssertPose(fire ? "ThrowRelease" : "SealRelease");
        if (fire)
        {
            Assert.That(Visible(release), Is.False);
            return;
        }

        Assert.That(Visible(release), Is.True);
        AssertFootprint(release, armed, 1f);
        float alpha = release.sharedMesh.colors[0].a;
        _view.Step(0.12f);
        Assert.That(Visible(release), Is.True);
        Assert.That(release.sharedMesh.colors[0].a, Is.LessThan(alpha));
        _view.Step(0.14f);
        Assert.That(Visible(release), Is.False);
        Assert.That(Lights(), Is.All.False);
        AssertPose("Recovery");
        _view.Step(0.75f);
        AssertPose("Idle");
    }

    [Test]
    public void Fire_CarriesTheStaffLaunchIntoTheLiveStrip()
    {
        Place(Boss);
        HierophantArmed armed = Arm(Boss, HierophantMove.FireThrow);
        Transform staff = _animator.GetComponentInChildren<MeshFilter>().transform;
        LineRenderer charge = Line("Staff fire");
        LineRenderer launch = Line("Staff fire launch");
        MeshFilter crest = _floor.Find("Footprint/Fire wave crest").GetComponent<MeshFilter>();
        _events.Publish(armed);
        _events.Publish(new HierophantTellProgress(Boss, 0.8f));
        AssertPose("ThrowTell");
        _view.Step(0f);
        Assert.That(charge.enabled, Is.True);
        Assert.That(charge.GetPosition(0), Is.EqualTo(staff.TransformPoint(Vector3.forward * 1.34f)));

        _events.Publish(new HierophantImpact(Boss, armed.Move, armed.SealSlot, 1.25f));
        _events.Publish(new HierophantFireWave(Boss, 0f, 0f, 0f, true));
        AssertPose("ThrowRelease");
        Vector3 releaseTip = staff.TransformPoint(Vector3.forward * 1.34f);
        _view.Step(0f);
        _events.Publish(new HierophantFireWave(Boss, 0.5f, 1.4f, 0.2f, true));
        _view.Step(0.1f);
        Assert.That(launch.enabled, Is.True);
        Assert.That(launch.GetPosition(0), Is.EqualTo(releaseTip));
        Assert.That(launch.GetPosition(2), Is.EqualTo(crest.transform.TransformPoint(new Vector3(0f, 0.32f, 1.4f))));
        Assert.That(Visible(crest), Is.True);
        var bounds = Layer("Release").sharedMesh.bounds;
        Assert.That((bounds.min.x, bounds.max.x), Is.EqualTo((-armed.HalfWidth, armed.HalfWidth)));
        Assert.That(bounds.min.z, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(bounds.max.z, Is.EqualTo(1.4f).Within(0.001f));

        _events.Publish(new HierophantFireWave(Boss, 6.1f, 7f, 1f, true));
        _view.Step(0.1f);
        Assert.That(launch.enabled, Is.False);
        Assert.That(crest.sharedMesh.bounds.max.z, Is.EqualTo(7f));
        _events.Publish(new HierophantFireWave(Boss, 0f, 0f, 1f, false));
        Assert.That(Visible(crest), Is.False);
        Assert.That(Visible(Layer("Release")), Is.False);
    }

    [Test]
    public void PriestHeal_HasItsOwnCueAndClearsOnEndAndPhase()
    {
        Place(Boss);
        var priest = new Num.Vector3(-2f, 0f, 4f);
        LineRenderer ward = Line("Priest ward");
        LineRenderer ring = Line("Priest heal ring");
        LineRenderer beam = Line("Priest heal beam");
        _events.Publish(new HierophantHealChannel(Boss, priest, 0.5f, true));
        Assert.That(ward.enabled, Is.False);
        Assert.That(ring.enabled && beam.enabled, Is.True);
        Assert.That(beam.startColor, Is.Not.EqualTo(ward.startColor));
        Assert.That(beam.GetPosition(0), Is.EqualTo(priest.ToUnity() + Vector3.up * 1.25f));
        _events.Publish(new HierophantHealChannel(Boss, default, 0f, false));
        Assert.That(ring.enabled || beam.enabled, Is.False);
        _events.Publish(new HierophantHealChannel(Boss, priest, 0f, true));
        _events.Publish(new BossPhaseChanged(Boss, 1, 3));
        Assert.That(ring.enabled || beam.enabled, Is.False);
    }

    [Test]
    public void SealEruption_FillsLockedStrip_AndSurvivesTheFollowUpWarning()
    {
        Place(Boss);
        HierophantArmed first = Arm(Boss, HierophantMove.SunSeal);
        _events.Publish(first);
        MeshFilter outline = Layer("Warning outline");
        Assert.That(Visible(outline), Is.True);
        Assert.That(outline.sharedMesh.bounds.min.x, Is.EqualTo(-first.HalfWidth));
        Assert.That(outline.sharedMesh.bounds.max.x, Is.EqualTo(first.HalfWidth));
        Assert.That(outline.sharedMesh.bounds.min.z, Is.Zero);
        Assert.That(outline.sharedMesh.bounds.max.z, Is.EqualTo(first.Reach));

        Transform eruption = _floor.Find("Sandstone eruption");
        Mesh crack = eruption.Find("Ground crack").GetComponent<MeshFilter>().sharedMesh;
        Mesh stone = eruption.Find("Rising sandstone").GetComponent<MeshFilter>().sharedMesh;
        Mesh dust = eruption.Find("Rolling dust").GetComponent<MeshFilter>().sharedMesh;
        Assert.That(eruption.gameObject.activeSelf, Is.False);
        _events.Publish(new HierophantTellProgress(Boss, 1f));
        Assert.That(eruption.gameObject.activeSelf, Is.False);
        _events.Publish(new HierophantImpact(Boss, first.Move, first.SealSlot, 0f));
        Assert.That(Visible(outline), Is.False);
        Assert.That(eruption.gameObject.activeSelf, Is.True);
        Assert.That(eruption.position, Is.EqualTo(first.Origin.ToUnity()));
        Assert.That(Vector3.Dot(eruption.forward, new Vector3(first.Facing.X, 0f, first.Facing.Y)),
            Is.EqualTo(1f).Within(0.0001f));
        Assert.That(crack.bounds.min.z, Is.Zero);
        Assert.That(crack.bounds.max.z, Is.EqualTo(first.Reach));
        Assert.That(dust.bounds.size.z, Is.GreaterThan(first.Reach * 0.9f));
        Assert.That(stone.bounds.size.z, Is.GreaterThan(first.Reach * 0.9f));
        float initialHeight = stone.bounds.max.y;
        Quaternion firstHeading = eruption.rotation;
        _view.Step(0.3f);
        Assert.That(stone.bounds.max.y, Is.GreaterThan(initialHeight + 0.4f));

        var second = new HierophantArmed(Boss, first.Move, 1, first.Origin, -Num.Vector2.UnitY,
            12f, first.HalfWidth, 1.3f);
        _events.Publish(second);
        Assert.That(Visible(outline), Is.True);
        Assert.That(outline.sharedMesh.bounds.max.z, Is.EqualTo(second.Reach));
        Assert.That(eruption.rotation, Is.EqualTo(firstHeading));
        Assert.That(eruption.gameObject.activeSelf, Is.True);
        _view.Step(0.49f);
        Assert.That(eruption.gameObject.activeSelf, Is.False);
        Assert.That(Visible(outline), Is.True);
        _events.Publish(new HierophantImpact(Boss, second.Move, second.SealSlot, 1.5f));
        Assert.That(eruption.gameObject.activeSelf, Is.True);
        Assert.That(crack.bounds.max.z, Is.EqualTo(second.Reach));
        _events.Publish(new BossPhaseChanged(Boss, 2, 3));
        Assert.That(eruption.gameObject.activeSelf, Is.False);
        Assert.That(Visible(outline), Is.False);
    }

    [Test]
    public void Ward_FollowsItsEndpoints_AndClearsOnInterruptionAndPhase()
    {
        Place(Boss);
        var tether = _floor.Find("Priest ward").GetComponent<LineRenderer>();
        var priest = new Num.Vector3(-3f, 0f, 6f);
        _events.Publish(new HierophantWard(Boss + 1, 9, true, priest));
        Assert.That(tether.enabled, Is.False);
        _events.Publish(new HierophantWard(Boss, 9, true, priest));
        Assert.That(tether.enabled, Is.True);
        Assert.That(tether.GetPosition(0), Is.EqualTo(new Vector3(-3f, 1.25f, 6f)));
        _body.transform.position = Vector3.right;
        _view.Step(0f);
        Assert.That(tether.GetPosition(1), Is.EqualTo(new Vector3(1f, 1.8f, 0f)));
        _events.Publish(new HierophantWard(Boss, 9, false, default));
        Assert.That(tether.enabled, Is.False);
        _events.Publish(new HierophantWard(Boss, 9, true, priest));
        _events.Publish(new BossPhaseChanged(Boss, 1, 3));
        Assert.That(tether.enabled, Is.False);
        _events.Publish(new HierophantWard(Boss, 9, true, priest));
        ReleaseBody();
        Assert.That(tether.enabled, Is.False);
    }

    [Test]
    public void Lifecycle_ClearsAndRebinds()
    {
        Place(Boss);
        var seal = Arm(Boss, HierophantMove.SunSeal);
        var fire = Arm(Boss, HierophantMove.FireThrow);
        _events.Publish(seal);
        Vector3[] firstPlacement = SealPositions();
        _events.Publish(new HierophantSealsPlaced(Boss + 1, Num.Vector2.One, Num.Vector2.One, Num.Vector2.One));
        _events.Publish(Arm(Boss + 1, HierophantMove.FireThrow));
        _events.Publish(new HierophantTellProgress(Boss + 1, 1f));
        _events.Publish(new HierophantImpact(Boss + 1, seal.Move, seal.SealSlot, 1f));
        _events.Publish(new BossPhaseChanged(Boss + 1, 1, 3));
        _events.Publish(new EnemyDied(Boss + 1, _spec, Num.Vector3.Zero));
        Assert.That(SealPositions(), Is.EqualTo(firstPlacement));
        Assert.That(Lights(), Is.EqualTo(new[] { false, false, true }));
        Assert.That(Visible(Layer("Warning")), Is.True);
        Assert.That(Visible(Layer("Progress")), Is.False);
        Assert.That(Visible(Layer("Release")), Is.False);
        AssertPose("SealTell");

        _events.Publish(new HierophantTellProgress(Boss, 0.5f));
        Assert.That(Visible(Layer("Progress")), Is.True);
        _events.Publish(new BossPhaseChanged(Boss, 1, 3));
        _events.Publish(new HierophantTellProgress(Boss, 1f));
        _events.Publish(new HierophantImpact(Boss, seal.Move, seal.SealSlot, 2f));
        AssertCleared();
        AssertPose("Idle");
        Assert.That(_floor.gameObject.activeSelf, Is.True);
        Assert.That(SealPositions(), Is.EqualTo(firstPlacement));

        _events.Publish(fire);
        _events.Publish(new HierophantImpact(Boss, fire.Move, fire.SealSlot, 2f));
        _events.Publish(new HierophantFireWave(Boss, 1.1f, 2f, 0.3f, true));
        Assert.That(Visible(Layer("Release")), Is.True);
        _events.Publish(new EnemyDied(Boss, _spec, Num.Vector3.Zero));
        Place(Boss);
        AssertCleared();
        Assert.That(_floor.gameObject.activeSelf, Is.False);
        AssertPose("Death");

        ReleaseBody();
        Assert.That(_pool.Get(), Is.SameAs(_body));
        _body.Bind(Boss + 1, Vector3.one);
        Place(Boss);
        _events.Publish(seal);
        Assert.That(_floor.gameObject.activeSelf, Is.False);
        AssertCleared();
        Place(Boss + 1, 8f);
        Place(Boss + 1, 8f);
        Assert.That(SealPositions().Select(position => position.x), Is.EqualTo(new[] { 4f, 8f, 12f }));
        Assert.That(Lights(), Is.All.False);
        _events.Publish(Arm(Boss + 1, HierophantMove.SunSeal));
        Assert.That(Lights(), Is.EqualTo(new[] { false, false, true }));
        ReleaseBody();
        Assert.That(_floor.gameObject.activeSelf, Is.False);
        AssertCleared();
    }

    private void ReleaseBody()
    {
        _pool.Release(_body);
        // EditMode does not dispatch the disable message that production pooling uses.
        InvokeLifecycle(_view, "OnDisable");
    }

    private static void InvokeLifecycle(MonoBehaviour view, string message) =>
        view.GetType().GetMethod(message, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);

    private void Place(int id, float shift = 0f)
    {
        _events.Publish(new HierophantSealsPlaced(id, new Num.Vector2(shift - 4f, 0f),
            new Num.Vector2(shift, 0f), new Num.Vector2(shift + 4f, 0f)));
        if (_floor == null)
            _floor = _scene.GetRootGameObjects().Single(root => root.name == "Hierophant floor visuals").transform;
    }

    private static HierophantArmed Arm(int boss, HierophantMove move) => move == HierophantMove.FireThrow
        ? new HierophantArmed(boss, move, -1, new Num.Vector3(3f, 0f, -2f), Num.Vector2.UnitX, 7f, 3.4641016f, 1f)
        : new HierophantArmed(boss, move, 2, new Num.Vector3(4f, 0f, 0f),
            Num.Vector2.Normalize(Num.Vector2.One), 18f, 2f, 1.3f);

    private MeshFilter Layer(string label) => _floor.Find("Footprint/" + label).GetComponent<MeshFilter>();
    private LineRenderer Line(string label) => _floor.Find(label).GetComponent<LineRenderer>();
    private static bool Visible(MeshFilter layer) => layer.GetComponent<MeshRenderer>().enabled;
    private static Vector3[] WorldVertices(MeshFilter layer) =>
        layer.sharedMesh.vertices.Select(layer.transform.TransformPoint).ToArray();

    private MeshRenderer[] SealLights() => _floor.Cast<Transform>()
        .Where(child => child.name.StartsWith("HierophantSeal"))
        .Select(child => child.Find("Light").GetComponent<MeshRenderer>()).ToArray();

    private bool[] Lights() => SealLights().Select(light => light.enabled).ToArray();
    private Vector3[] SealPositions() => SealLights().Select(light => light.transform.parent.position).ToArray();

    private static void AssertFootprint(MeshFilter mesh, in HierophantArmed armed, float fraction)
    {
        Vector3[] vertices = WorldVertices(mesh);
        Vector3 origin = armed.Origin.ToUnity();
        Vector2[] offsets = vertices.Select(point => new Vector2(point.x - origin.x, point.z - origin.z)).ToArray();
        float reach = armed.Reach * fraction;
        var forward = new Vector2(armed.Facing.X, armed.Facing.Y);
        var right = new Vector2(forward.y, -forward.x);
        Assert.That(offsets.Select(point => Vector2.Dot(point, forward)),
            Is.EqualTo(new[] { 0f, reach, 0f, reach }).Within(0.001f));
        Assert.That(offsets.Select(point => Vector2.Dot(point, right)),
            Is.EqualTo(new[] { -armed.HalfWidth, -armed.HalfWidth, armed.HalfWidth, armed.HalfWidth }).Within(0.001f));
    }

    private void AssertCleared()
    {
        Assert.That(Visible(Layer("Warning")), Is.False);
        Assert.That(Visible(Layer("Progress")), Is.False);
        Assert.That(Visible(Layer("Release")), Is.False);
        Assert.That(Lights(), Is.All.False);
        Assert.That(_audio.clip, Is.Null);
    }

    private void AssertPose(string state)
    {
        _animator.Update(0f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName(state), Is.True, state);
    }
}
