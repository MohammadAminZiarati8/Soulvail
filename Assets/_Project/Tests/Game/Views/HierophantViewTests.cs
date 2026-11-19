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
    public void Prefab_BindsPrototypeAssets()
    {
        Assert.That(_animator.avatar.isValid, Is.True);
        Assert.That(_animator.applyRootMotion, Is.False);
        Assert.That(_audio.playOnAwake, Is.False);
        var mesh = _animator.GetComponentInChildren<SkinnedMeshRenderer>();
        Assert.That(AssetDatabase.GetAssetPath(mesh.sharedMesh), Is.EqualTo(Project + "Art/Enemies/JackalPriest.fbx"));
        Transform staff = _animator.GetComponentInChildren<MeshFilter>().transform;
        Assert.That(staff.parent.name, Is.EqualTo("handslot.r"));
        Assert.That(AssetDatabase.GetAssetPath(staff.GetComponent<MeshFilter>().sharedMesh),
            Is.EqualTo(Project + "Art/Enemies/PriestStaff.fbx"));
        Assert.That(_prefab.GetComponent<PriestMendView>(), Is.Null);
        Assert.That(_animator.GetComponent<EnemyAnimatorView>(), Is.Null);

        var controller = (AnimatorController)_animator.runtimeAnimatorController;
        Assert.That(controller.layers[0].stateMachine.states.Select(state => state.state.name),
            Is.EquivalentTo(new[] { "Idle", "Walk", "SweepTell", "SweepRelease", "SealTell", "SealRelease", "Recovery", "Death" }));
        Assert.That(controller.animationClips.All(clip => clip.events.Length == 0), Is.True);

        var seal = AssetDatabase.LoadAssetAtPath<GameObject>(Project + "Prefabs/Arenas/Sunscar/HierophantSeal.prefab");
        Assert.That(seal.GetComponentsInChildren<Collider>(true), Is.Empty);
        Assert.That(seal.GetComponentsInChildren<NavMeshObstacle>(true), Is.Empty);
        Assert.That(seal.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
        Assert.That(seal.GetComponentsInChildren<MeshRenderer>(true), Has.Length.EqualTo(3));
        Assert.That(seal.transform.Find("Light").GetComponent<Renderer>().enabled, Is.False);

        Place(Boss);
        _events.Publish(Arm(Boss, HierophantMove.StaffSweep));
        Assert.That(_audio.clip.name, Is.EqualTo("DuneclawSweep"));
        AudioClip sweep = _audio.clip;
        _events.Publish(Arm(Boss, HierophantMove.SunSeal));
        Assert.That(_audio.clip.name, Is.EqualTo("SFX_ShardbackFan"));
        Assert.That(_audio.clip, Is.Not.SameAs(sweep));
    }

    [TestCase(HierophantMove.StaffSweep)]
    [TestCase(HierophantMove.SunSeal)]
    public void Tell_MatchesCoreAndStaysInPlace(HierophantMove move)
    {
        Place(Boss);
        HierophantArmed armed = Arm(Boss, move);
        _events.Publish(armed);
        MeshFilter warning = Layer("Warning");
        MeshFilter progress = Layer("Progress");
        MeshFilter release = Layer("Release");
        bool sweep = move == HierophantMove.StaffSweep;
        AssertPose(sweep ? "SweepTell" : "SealTell");
        Assert.That(Visible(warning), Is.True);
        Assert.That(Visible(progress), Is.False);
        Assert.That(Visible(release), Is.False);
        Assert.That(Lights(), Is.EqualTo(new[] { false, false, !sweep }));
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

        _events.Publish(new HierophantImpact(Boss, move, sweep ? 1 : 0, 1f));
        Assert.That(Visible(warning), Is.True);
        _events.Publish(new HierophantImpact(Boss, move, armed.SealSlot, 1f));
        Assert.That(Visible(warning), Is.False);
        Assert.That(Visible(progress), Is.False);
        Assert.That(Visible(release), Is.True);
        Assert.That(_audio.clip, Is.Null);
        AssertFootprint(release, armed, 1f);
        AssertPose(sweep ? "SweepRelease" : "SealRelease");
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
    public void Lifecycle_ClearsAndRebinds()
    {
        Place(Boss);
        var seal = Arm(Boss, HierophantMove.SunSeal);
        var sweep = Arm(Boss, HierophantMove.StaffSweep);
        _events.Publish(seal);
        Vector3[] firstPlacement = SealPositions();
        _events.Publish(new HierophantSealsPlaced(Boss + 1, Num.Vector2.One, Num.Vector2.One, Num.Vector2.One));
        _events.Publish(Arm(Boss + 1, HierophantMove.StaffSweep));
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

        _events.Publish(sweep);
        _events.Publish(new HierophantImpact(Boss, sweep.Move, sweep.SealSlot, 2f));
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

    private static HierophantArmed Arm(int boss, HierophantMove move) => move == HierophantMove.StaffSweep
        ? new HierophantArmed(boss, move, -1, new Num.Vector3(3f, 0f, -2f), Num.Vector2.UnitX, 3.25f, 0f, 50f, 1f)
        : new HierophantArmed(boss, move, 2, new Num.Vector3(4f, 0f, 0f), Num.Vector2.UnitY, 9f, 2f, 0f, 1.3f);

    private MeshFilter Layer(string label) => _floor.Find("Footprint/" + label).GetComponent<MeshFilter>();
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
        if (armed.Move == HierophantMove.StaffSweep)
        {
            Assert.That(offsets[0], Is.EqualTo(Vector2.zero));
            Assert.That(offsets.Skip(1).Select(point => point.magnitude),
                Is.All.EqualTo(reach).Within(0.001f));
            var facing = new Vector2(armed.Facing.X, armed.Facing.Y);
            Assert.That(Vector2.SignedAngle(facing, offsets[1]), Is.EqualTo(armed.HalfAngleDegrees).Within(0.001f));
            Assert.That(Vector2.SignedAngle(facing, offsets[^1]), Is.EqualTo(-armed.HalfAngleDegrees).Within(0.001f));
            Assert.That(Vector2.Angle(facing, offsets[offsets.Length / 2]), Is.Zero.Within(0.001f));
        }
        else
        {
            Assert.That(offsets, Is.EquivalentTo(new[]
            {
                new Vector2(-armed.HalfWidth, -reach), new Vector2(-armed.HalfWidth, reach),
                new Vector2(armed.HalfWidth, -reach), new Vector2(armed.HalfWidth, reach),
            }));
        }
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
