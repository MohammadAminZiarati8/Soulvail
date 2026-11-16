using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VContainer;
using Point = System.Numerics.Vector3;
using Heading = System.Numerics.Vector2;

namespace Soulvail.Tests.Game.Views;

public sealed class VultureBodyTests
{
    private const string Root = "Assets/_Project/";
    private const string BodyPath = "Prefabs/Enemies/Vulture.prefab";
    private const string ShadowPath = "Prefabs/Vfx/FX_Vulture_Shadow.prefab";
    private const int Id = 7;

    private DomainEventHub _events;
    private GameObject _instance;
    private EnemyView _enemy;
    private VultureView _view;
    private Animator _animator;
    private Renderer _body;
    private Renderer _shadow;
    private Transform _model;

    [SetUp]
    public void SetUp() => _events = new DomainEventHub();

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_instance);
        _events.Dispose();
    }

    [Test]
    public void View_Aloft_HidesBodyShowsShadow()
    {
        Spawn();
        Change(VulturePhase.Circle, 4f);
        _view.Step(0.1f);
        Assert.That(_body.enabled, Is.False);
        Assert.That(_shadow.enabled, Is.True);
        Assert.That(_shadow.transform.position.x, Is.EqualTo(_enemy.transform.position.x));
        Assert.That(_shadow.transform.position.z, Is.EqualTo(_enemy.transform.position.z));
        Assert.That(_enemy.HealthBar.IsShown, Is.False);
    }

    [Test]
    public void View_Shadow_FollowsAgent()
    {
        Spawn();
        Change(VulturePhase.Circle, 4f);
        float width = _shadow.transform.localScale.x;
        _enemy.transform.position = new Vector3(2f, 0f, 0f);
        _view.Step(0.3f);
        Assert.That(_shadow.transform.position.x, Is.EqualTo(2f));
        Assert.That(Vector3.Angle(_shadow.transform.forward, Vector3.right), Is.Zero.Within(0.01f));
        Assert.That(_shadow.transform.localScale.x, Is.LessThan(width));
        AllocationAssert.None(() => _view.Step(0.01f), iterations: 100);
    }

    [Test]
    public void View_Lock_PinsShadowToLaneOrigin()
    {
        Spawn();
        Lock();
        _enemy.transform.position = new Vector3(2f, 0f, 0f);
        _view.Step(0.1f);
        Assert.That(_shadow.transform.position.x, Is.Zero);
        Assert.That(_shadow.transform.position.z, Is.Zero);
        Assert.That(Vector3.Angle(_shadow.transform.forward, Vector3.forward), Is.Zero.Within(0.01f));
        Assert.That(_body.enabled, Is.False);
    }

    [Test]
    public void View_Climb_RisesAndFades()
    {
        Spawn();
        Change(VulturePhase.Grounded, 2f);
        Change(VulturePhase.Climb, 0.6f);
        Assert.That(_enemy.HealthBar.IsShown, Is.False);
        Assert.That(_shadow.enabled, Is.False);
        _view.Step(0.5f);
        Assert.That(ReadTint(_body).a, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(ReadTint(_shadow).a, Is.EqualTo(0.255f).Within(0.001f));
        _view.Step(0.1f);
        Assert.That(_model.position.y, Is.EqualTo(VultureView.AloftHeight));
        Assert.That(_body.enabled, Is.False);
        Assert.That(_shadow.enabled, Is.True);
    }

    [Test]
    public void View_Dive_DescendsToLanding()
    {
        Spawn();
        Lock();
        Change(VulturePhase.Dive, 0.4f);
        Assert.That(_model.position, Is.EqualTo(Vector3.up * VultureView.AloftHeight));
        float span = _shadow.transform.localScale.x;
        _view.Step(0.2f);
        Assert.That(_model.position, Is.EqualTo(new Vector3(0f, VultureView.AloftHeight / 2f, 5f)));
        Assert.That(_body.enabled, Is.True);
        Assert.That(_shadow.transform.localScale.x, Is.LessThan(span));
        _view.Step(0.2f);
        Assert.That(_model.position, Is.EqualTo(Vector3.forward * 10f));
        Assert.That(_shadow.enabled, Is.False);
        Assert.That(_enemy.transform.position, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void View_Grounded_ShowsBodyAndBar()
    {
        Spawn();
        Lock();
        Change(VulturePhase.Dive, 0.4f);
        _enemy.transform.position = Vector3.forward * 10f;
        Change(VulturePhase.Grounded, 2f);
        Assert.That(_body.enabled, Is.True);
        Assert.That(_shadow.enabled, Is.False);
        Assert.That(_model.position, Is.EqualTo(_enemy.transform.position));
        Assert.That(_enemy.HealthBar.IsShown, Is.True);
    }

    [TestCase(VulturePhase.Climb, true)]
    [TestCase(VulturePhase.Circle, true)]
    [TestCase(VulturePhase.Lock, true)]
    [TestCase(VulturePhase.Dive, true)]
    [TestCase(VulturePhase.Grounded, false)]
    public void View_PassesCoverAloftOnly(VulturePhase phase, bool passes)
    {
        Spawn();
        var controller = _instance.GetComponent<CharacterController>();
        int cover = 1 << LayerMask.NameToLayer(ArenaView.CoverLayerName);
        Assert.That((controller.excludeLayers.value & cover) != 0, Is.True);
        Change(phase, 1f);
        Assert.That(_view.PassesCover, Is.EqualTo(passes));
        Assert.That((controller.excludeLayers.value & cover) != 0, Is.EqualTo(passes));
    }

    [Test]
    public void View_OtherId_Ignored()
    {
        Spawn();
        Lock();
        _events.Publish(new VulturePhaseChanged(Id + 1, VulturePhase.Grounded, 2f));
        _events.Publish(new VultureDiveLocked(Id + 1, 2, Point.UnitX, Heading.UnitY, Point.UnitZ, 1f));
        _view.Step(0.1f);
        Assert.That(_body.enabled, Is.False);
        Assert.That(_shadow.transform.position.x, Is.Zero);
        Assert.That(_animator.GetInteger("VulturePhase"), Is.EqualTo((int)VulturePhase.Lock));
    }

    [Test]
    public void View_PooledReuse_Resets()
    {
        Spawn();
        Change(VulturePhase.Climb, 0.6f);
        _view.Step(0.5f);
        Material opaque = Asset<GameObject>(BodyPath).GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
        _enemy.OnDespawn();
        _instance.SetActive(false);
        _instance.SetActive(true);
        Bind(Id + 1);
        _view.Bind(Id + 1);
        _animator.Update(0f);
        Assert.That(_view.PassesCover, Is.True);
        Assert.That(_shadow.enabled, Is.False);
        Assert.That(_body.enabled, Is.True);
        Assert.That(_body.sharedMaterial, Is.SameAs(opaque));
        Assert.That(_model.localPosition, Is.EqualTo(Vector3.zero));
        AssertState("Climb");
    }

    [Test]
    public void Lane_DrawsForTheVulture()
    {
        Spawn();
        _instance.name = "Non-boss stage";
        using IObjectResolver container = new ContainerBuilder().Build();
        using var enemies = new EnemyViews(container, _enemy, _instance.transform, _events,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()));
        using var views = new BossViews(container, Vfx<ShockwaveView>("VFX_Shockwave"),
            Vfx<FissureView>("VFX_Fissure"), Vfx<BossBeatView>("VFX_BossBeat"),
            _instance.transform, _events, enemies, null, 0, 0, 0);
        Lock();
        _events.Publish(new LaneArmed(1, Point.Zero, Heading.UnitY, 10f, 0.6f, 1f));
        views.Step(0.1f);
        Assert.That(views.LaneCount, Is.EqualTo(1));
        var lane = _instance.transform.Find("Lane 1").GetComponent<MeshRenderer>();
        Assert.That(lane.transform.position.z, Is.EqualTo(5f));
        Assert.That(lane.transform.localScale, Is.EqualTo(new Vector3(1.2f, 10f, 1f)));
        Color tint = ReadTint(lane);
        Assert.That(tint.a, Is.GreaterThan(0f));
        Assert.That(tint.r / tint.a, Is.EqualTo(Palette.Danger.r).Within(0.0001f));
        Assert.That(tint.g / tint.a, Is.EqualTo(Palette.Danger.g).Within(0.0001f));
        Assert.That(tint.b / tint.a, Is.EqualTo(Palette.Danger.b).Within(0.0001f));
    }

    [TestCase(VulturePhase.Climb)]
    [TestCase(VulturePhase.Circle)]
    [TestCase(VulturePhase.Lock)]
    [TestCase(VulturePhase.Dive)]
    [TestCase(VulturePhase.Grounded)]
    public void Animator_VulturePhase_Set(VulturePhase phase)
    {
        Spawn();
        Change(phase, 1f);
        Assert.That(_animator.GetInteger("VulturePhase"), Is.EqualTo((int)phase));
    }

    [Test]
    public void Animator_PlaysThePhaseSequence()
    {
        Spawn();
        AssertState("Climb");
        Change(VulturePhase.Circle, 4f);
        _animator.Update(0.01f);
        AssertState("Glide");
        _animator.SetBool("Turning", true);
        _animator.Update(0.01f);
        AssertState("Flap");
        Lock();
        _animator.Update(0.01f);
        AssertState("Glide");
        Change(VulturePhase.Dive, 0.4f);
        _animator.Update(0.01f);
        AssertState("Dive");
        _animator.Update(0.26f);
        AssertState("Strike");
        Change(VulturePhase.Grounded, 2f);
        _animator.Update(0.01f);
        AssertState("Land");
        _animator.Update(0.5f);
        AssertState("Idle_Ground");
        _events.Publish(new EnemyDamaged(Id, 10f, 0.8f, false));
        _animator.Update(0.01f);
        AssertState("Hit");
        _events.Publish(new EnemyDied(Id, new ContentId("enemy.vulture"), Point.Zero));
        _animator.Update(0.01f);
        AssertState("Death");
    }

    [Test]
    public void Controller_EveryStateIsTheModels()
    {
        var controller = Asset<AnimatorController>("Animation/Controllers/AC_Vulture.controller");
        var states = controller.layers.SelectMany(layer => layer.stateMachine.states).Select(child => child.state);
        Assert.That(states.Select(state => state.name), Is.EquivalentTo(
            new[] { "Climb", "Glide", "Flap", "Dive", "Strike", "Land", "Idle_Ground", "Hit", "Death" }));
        foreach (AnimatorState state in states)
        {
            Assert.That(state.motion, Is.TypeOf<AnimationClip>());
            Assert.That(state.motion.name, Is.EqualTo(state.name));
            Assert.That(AssetDatabase.GetAssetPath(state.motion), Is.EqualTo(Root + "Art/Enemies/Vulture.fbx"));
            Assert.That(state.writeDefaultValues, Is.False);
        }
    }

    [Test]
    public void Prefab_WiresTheView()
    {
        GameObject prefab = Asset<GameObject>(BodyPath);
        var view = new SerializedObject(prefab.GetComponent<VultureView>());
        var model = (Transform)view.FindProperty("_model").objectReferenceValue;
        var body = (SkinnedMeshRenderer)view.FindProperty("_body").GetArrayElementAtIndex(0).objectReferenceValue;
        var shadow = (Renderer)view.FindProperty("_shadow").objectReferenceValue;
        Assert.That(body.transform.IsChildOf(model), Is.True);
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(shadow.gameObject), Is.EqualTo(Root + ShadowPath));
        var animator = model.GetComponentInChildren<Animator>();
        Assert.That(animator.runtimeAnimatorController.name, Is.EqualTo("AC_Vulture"));
        Assert.That(animator.applyRootMotion, Is.False);
        Assert.That(animator.cullingMode, Is.EqualTo(AnimatorCullingMode.AlwaysAnimate));
        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue, Is.SameAs(body));
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.Zero);
        var antlionTypes = Asset<GameObject>("Prefabs/Enemies/Antlion.prefab").GetComponents<Component>()
            .Select(component => component.GetType()).Where(type => type != typeof(AntlionView));
        Assert.That(prefab.GetComponents<Component>().Select(component => component.GetType()),
            Is.EquivalentTo(antlionTypes.Append(typeof(VultureView))));
    }

    [Test]
    public void Effects_DrawOverTheStorm()
    {
        GameObject dunes = Asset<GameObject>("Prefabs/Arenas/Sunscar/Arena_Dunes.prefab");
        _instance = Object.Instantiate(dunes);
        var arena = _instance.GetComponent<ArenaView>();
        var storm = _instance.GetComponentInChildren<SunscarSandstormView>(true);
        storm.Bind(arena, _instance.transform);
        int stormQueue = storm.GetComponent<ParticleSystemRenderer>().sharedMaterial.renderQueue;
        var shadow = Asset<GameObject>(ShadowPath).GetComponent<Renderer>();
        Assert.That(shadow.sharedMaterial.renderQueue, Is.GreaterThan(stormQueue));
        Assert.That(Vfx<FissureView>("VFX_Fissure").GetComponent<Renderer>().sharedMaterial.renderQueue,
            Is.GreaterThan(stormQueue));
    }

    [Test]
    public void Shadow_NoCyanOrDanger()
    {
        var shadow = Asset<GameObject>(ShadowPath).GetComponent<MeshRenderer>();
        Color colour = shadow.sharedMaterial.color;
        Color.RGBToHSV(colour, out float hue, out _, out float value);
        Assert.That(hue, Is.InRange(0.075f, 0.115f));
        Assert.That(value, Is.LessThan(0.25f));
        Assert.That(colour.a, Is.EqualTo(0.5f).Within(0.02f));
        Assert.That(shadow.sharedMaterial.mainTexture, Is.Not.Null);
        Assert.That(shadow.GetComponent<MeshFilter>().sharedMesh.bounds.size.x, Is.InRange(2.6f, 2.8f));
    }

    private void Spawn()
    {
        _instance = Object.Instantiate(Asset<GameObject>(BodyPath));
        _enemy = _instance.GetComponent<EnemyView>();
        _view = _instance.GetComponent<VultureView>();
        _animator = _instance.GetComponentInChildren<Animator>();
        _body = _instance.GetComponentInChildren<SkinnedMeshRenderer>();
        _shadow = _instance.GetComponentInChildren<MeshRenderer>();
        _model = _body.transform.GetComponentInParent<Animator>().transform.parent;
        _view.Construct(_events);
        _animator.GetComponent<EnemyAnimatorView>().Construct(_events);
        Bind(Id);
        _animator.Rebind();
        _animator.Update(0f);
    }

    private void Bind(int id)
    {
        _enemy.Bind(id, Vector3.zero);
        _enemy.HealthBar.Bind(id, isElite: false);
    }

    private void Change(VulturePhase phase, float seconds) => _events.Publish(new VulturePhaseChanged(Id, phase, seconds));

    private void Lock()
    {
        Change(VulturePhase.Lock, 1f);
        _events.Publish(new VultureDiveLocked(Id, 1, Point.Zero, Heading.UnitY, Point.UnitZ * 10f, 1f));
    }

    private void AssertState(string state) => Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName(state),
        Is.True, $"Expected {state}");

    private static Color ReadTint(Renderer renderer)
    {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        return block.GetColor("_BaseColor");
    }

    private static T Vfx<T>(string name) where T : Component => Asset<GameObject>("Prefabs/Vfx/" + name + ".prefab").GetComponent<T>();

    private static T Asset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(Root + path);
}
