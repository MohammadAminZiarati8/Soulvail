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
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using Vector3Core = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

public sealed class GlassbackBodyTests
{
    private const string Root = "Assets/_Project/";
    private const string BodyPath = "Prefabs/Enemies/Glassback.prefab";
    private const string BeetlePath = "Prefabs/Enemies/Beetle.prefab";
    private const string BoltPath = "Prefabs/Projectiles/GlassBolt.prefab";
    private const string ModelPath = "Art/Enemies/Glassback.fbx";
    private const string ControllerPath = "Animation/Controllers/AC_Glassback.controller";
    private const string ShineMaterialPath = "Materials/Enemies/M_GlassbackShine.mat";
    private const int Id = 11;
    private const string Phase = "ShinePhase";

    private GameObject _instance;
    private DomainEventHub _events;
    private EnemyShineView _shine;
    private Renderer _overlay;

    [TearDown]
    public void Dispose()
    {
        if (_instance != null)
        {
            Object.DestroyImmediate(_instance);
        }

        _events?.Dispose();
    }

    [Test]
    public void Shine_None_OverlayOff()
    {
        SpawnShine();
        Assert.That(_overlay.enabled, Is.False);
        Publish(ShinePhase.Shine);
        Publish(ShinePhase.None);
        _shine.Step(0.1f);
        Assert.That(_overlay.enabled, Is.False);

        Publish(ShinePhase.Shine);
        _instance.SetActive(false);
        _instance.GetComponent<EnemyView>().Unbind();
        _instance.GetComponent<EnemyView>().Bind(Id + 1, Vector3.zero);
        _instance.SetActive(true);
        Publish(ShinePhase.Shine);
        _shine.Step(0.1f);
        Assert.That(_overlay.enabled, Is.False);
    }

    [Test]
    public void Shine_Glint_Ramps()
    {
        SpawnShine();
        Publish(ShinePhase.Glint, 0.5f);
        Assert.That(_overlay.enabled, Is.True);
        Assert.That(Intensity(), Is.Zero);
        _shine.Step(0.25f);
        Assert.That(Intensity(), Is.EqualTo(0.3f).Within(1e-5f));
        _shine.Step(0.5f);
        Assert.That(Intensity(), Is.EqualTo(0.6f).Within(1e-5f));
    }

    [Test]
    public void Shine_Shine_Blazes()
    {
        SpawnShine();
        Publish(ShinePhase.Shine);
        Assert.That(_overlay.enabled, Is.True);
        Assert.That(Intensity(), Is.EqualTo(1f));
        _shine.Step(1f / 24f);
        Assert.That(Intensity(), Is.EqualTo(1.1f).Within(1e-5f));
        _shine.Step(1f / 12f);
        Assert.That(Intensity(), Is.EqualTo(0.9f).Within(1e-5f));
    }

    [Test]
    public void Shine_Open_Off()
    {
        SpawnShine();
        Publish(ShinePhase.Shine);
        Publish(ShinePhase.Open);
        _shine.Step(0.25f);
        Assert.That(_overlay.enabled, Is.False);
        Assert.That(Intensity(), Is.Zero);
    }

    [Test]
    public void Shine_OtherId_Ignored()
    {
        SpawnShine();
        _shine.Bind(Id);
        Publish(ShinePhase.Shine, id: Id + 1);
        Assert.That(_overlay.enabled, Is.False);
        Publish(ShinePhase.Glint, 0.5f);
        Publish(ShinePhase.Open, id: Id + 1);
        _shine.Step(0.25f);
        Assert.That(_overlay.enabled, Is.True);
        Assert.That(Intensity(), Is.EqualTo(0.3f).Within(1e-5f));
    }

    [Test]
    public void Shine_SharedMaterialUntouched()
    {
        SpawnShine();
        var material = Load<Material>(ShineMaterialPath);
        string before = EditorJsonUtility.ToJson(material);
        Publish(ShinePhase.Glint, 0.5f);
        _shine.Step(0.5f);
        Publish(ShinePhase.Shine);
        _shine.Step(0.1f);
        Publish(ShinePhase.Open);
        Publish(ShinePhase.None);
        Assert.That(_overlay.sharedMaterial == material, Is.True);
        Assert.That(EditorJsonUtility.ToJson(material), Is.EqualTo(before));
    }

    [Test]
    public void Animator_ShinePhase_Set()
    {
        var view = SpawnAnimator(BodyPath, out Animator animator);
        Publish(ShinePhase.Glint, id: Id + 1);
        Assert.That(animator.GetInteger(Phase), Is.Zero);
        foreach (ShinePhase phase in new[] { ShinePhase.Glint, ShinePhase.Shine, ShinePhase.Open, ShinePhase.None })
        {
            Publish(phase);
            Assert.That(animator.GetInteger(Phase), Is.EqualTo((int)phase));
            animator.Update(0.1f);
            animator.Update(0.1f);
            string state = phase == ShinePhase.None ? "Locomotion" : phase.ToString();
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName(state), Is.True, state);
        }

        Publish(ShinePhase.Shine);
        view.Forget();
        Assert.That(animator.GetInteger(Phase), Is.Zero);
    }

    [Test]
    public void Animator_OtherController_Untouched()
    {
        var view = SpawnAnimator(BeetlePath, out Animator animator);
        Assert.That(animator.parameters.Any(parameter => parameter.name == Phase), Is.False);
        Publish(ShinePhase.Glint);
        Publish(ShinePhase.Shine);
        Publish(ShinePhase.Open);
        view.Forget();
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void Controller_ChainsGlintShineOpen()
    {
        var controller = Load<AnimatorController>(ControllerPath);
        var machine = controller.layers[0].stateMachine;
        var locomotion = machine.defaultState;
        Assert.That(locomotion.name, Is.EqualTo("Locomotion"));
        var tree = (BlendTree)locomotion.motion;
        Assert.That(tree.blendParameter, Is.EqualTo("Speed"));
        Assert.That(tree.children.Select(child => child.motion.name), Is.EqualTo(new[] { "Idle", "Walk" }));

        Assert.That(locomotion.transitions.Any(transition =>
            transition.destinationState.motion.name == "Bite" &&
            HasCondition(transition, "Attack", AnimatorConditionMode.If)), Is.True);
        Assert.That(machine.anyStateTransitions.Any(transition =>
            transition.destinationState.motion.name == "Death" &&
            HasCondition(transition, "Dead", AnimatorConditionMode.If)), Is.True);
        var flinch = controller.layers.Single(layer => layer.name == "Flinch").stateMachine;
        Assert.That(flinch.anyStateTransitions.Any(transition =>
            transition.destinationState.motion.name == "Hit" &&
            HasCondition(transition, "Hit", AnimatorConditionMode.If)), Is.True);

        string[] phases = { "Glint", "Shine", "Open" };
        for (int i = 0; i < phases.Length; i++)
        {
            AnimatorStateTransition entry = machine.anyStateTransitions.Single(transition =>
                transition.destinationState.name == phases[i]);
            Assert.That(HasCondition(entry, Phase, AnimatorConditionMode.Equals, i + 1), Is.True);
            Assert.That(entry.hasExitTime || entry.canTransitionToSelf, Is.False);
            Assert.That(entry.destinationState.transitions.Any(transition =>
                transition.destinationState == locomotion &&
                HasCondition(transition, Phase, AnimatorConditionMode.Equals)), Is.True);
        }

        var shine = (AnimationClip)machine.states.Single(child => child.state.name == "Shine").state.motion;
        Assert.That(shine.isLooping, Is.True);
    }

    [Test]
    public void Controller_EveryStateIsTheModels()
    {
        var controller = Load<AnimatorController>(ControllerPath);
        var used = new HashSet<string>();
        foreach (AnimatorState state in controller.layers.SelectMany(layer => layer.stateMachine.states).Select(child => child.state))
        {
            Assert.That(state.writeDefaultValues, Is.False, state.name);
            Motion[] motions = state.motion is BlendTree tree
                ? tree.children.Select(child => child.motion).ToArray()
                : new[] { state.motion };
            foreach (Motion motion in motions)
            {
                Assert.That(motion, Is.TypeOf<AnimationClip>());
                Assert.That(AssetDatabase.GetAssetPath(motion), Is.EqualTo(Root + ModelPath));
                Assert.That(((AnimationClip)motion).events, Is.Empty);
                used.Add(motion.name);
            }
        }

        Assert.That(used, Is.EquivalentTo(new[] { "Idle", "Walk", "Bite", "Glint", "Shine", "Open", "Hit", "Death" }));
    }

    [Test]
    public void Prefab_WiresTheViews()
    {
        var prefab = Load<GameObject>(BodyPath);
        var beetle = Load<GameObject>(BeetlePath);
        Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true).Select(view => view.GetType()),
            Is.EquivalentTo(beetle.GetComponentsInChildren<MonoBehaviour>(true).Select(view => view.GetType())
                .Append(typeof(EnemyShineView))));
        Assert.That(prefab.GetComponents<Collider>().Select(collider => collider.GetType()),
            Is.EquivalentTo(beetle.GetComponents<Collider>().Select(collider => collider.GetType())));

        var animator = prefab.GetComponentInChildren<Animator>();
        Assert.That(animator.runtimeAnimatorController == Load<AnimatorController>(ControllerPath), Is.True);
        Assert.That(animator.applyRootMotion, Is.False);
        Assert.That(animator.transform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
        using var view = new SerializedObject(animator.GetComponent<EnemyAnimatorView>());
        Assert.That(view.FindProperty("_animator").objectReferenceValue == animator, Is.True);

        using var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.Zero);
        var skin = (SkinnedMeshRenderer)feedback.FindProperty("_renderer").objectReferenceValue;
        Assert.That(AssetDatabase.GetAssetPath(skin.sharedMesh), Is.EqualTo(Root + ModelPath));

        using var shine = new SerializedObject(prefab.GetComponent<EnemyShineView>());
        var overlay = (SkinnedMeshRenderer)shine.FindProperty("_overlay").objectReferenceValue;
        Assert.That(overlay == skin, Is.False);
        Assert.That(overlay.enabled, Is.False);
        Assert.That(overlay.sharedMesh == skin.sharedMesh, Is.True);
        Assert.That(overlay.bones, Is.EqualTo(skin.bones));
        Assert.That(overlay.rootBone == skin.rootBone, Is.True);
        Assert.That(overlay.sharedMaterial == Load<Material>(ShineMaterialPath), Is.True);
    }

    [Test]
    public void Bolt_MapsToGlassBolt()
    {
        _instance = new GameObject("Glassback shot test");
        _events = new DomainEventHub();
        var id = new ContentId("enemy.glassback");
        var looks = new EnemyLookBook(new Dictionary<ContentId, EnemyLook>
        {
            [id] = new EnemyLook(Color.white, 1f, Load<GameObject>(BodyPath).GetComponent<EnemyView>(),
                marksLanding: true),
        });
        using IObjectResolver container = new ContainerBuilder().Build();
        using var shots = new ProjectileViews(container, Load<GameObject>("Prefabs/Projectiles/BeetleGlob.prefab")
            .GetComponent<ProjectileView>(), _instance.transform, _events, classPrewarm: 1, enemyLooks: looks);
        var bolt = Load<GameObject>(BoltPath);
        Assert.That(shots.PooledCountOf(bolt.GetComponent<ProjectileView>()), Is.EqualTo(1));

        var ringPrefab = Load<GameObject>("Prefabs/Vfx/VFX_TelegraphRing.prefab").GetComponent<TelegraphRingView>();
        using var rings = new TelegraphRings(container, ringPrefab, _instance.transform, _events, prewarm: 0, looks: looks);
        _events.Publish(new ProjectileFired(1, id, Id, Vector3Core.Zero, Vector3Core.UnitZ, 1f, radius: 1f));
        Assert.That(rings.Count, Is.EqualTo(1));
        Assert.That(_instance.GetComponentInChildren<TelegraphRingView>().Colour, Is.EqualTo(Palette.Danger));
        Assert.That(shots.TryGet(1, out ProjectileView shot), Is.True);
        var mesh = shot.GetComponent<MeshFilter>().sharedMesh;
        Assert.That(mesh == bolt.GetComponent<MeshFilter>().sharedMesh, Is.True);
        Assert.That(mesh.bounds.size.z, Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(mesh.bounds.size.z, Is.GreaterThan(mesh.bounds.size.x * 2f));
        var material = shot.GetComponent<MeshRenderer>().sharedMaterial;
        Assert.That(material.shader.name, Is.EqualTo("Soulvail/Fireball"));
        var core = material.GetColor("_CoreColor");
        Assert.That(core.r, Is.GreaterThanOrEqualTo(core.g));
        Assert.That(core.b, Is.GreaterThan(0.8f));
    }

    [Test]
    public void Bolt_DrawsOverTheStorm()
    {
        _instance = new GameObject("Glassback queue test");
        _instance.SetActive(false);
        var dunes = Load<GameObject>("Prefabs/Arenas/Sunscar/Arena_Dunes.prefab");
        var arena = Object.Instantiate(dunes, _instance.transform).GetComponent<ArenaView>();
        var storm = arena.GetComponentInChildren<SunscarSandstormView>(true);
        storm.Bind(arena, _instance.transform);
        var sand = storm.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        try
        {
            var bolt = Load<GameObject>(BoltPath).GetComponent<MeshRenderer>().sharedMaterial;
            var ring = Load<GameObject>("Prefabs/Vfx/VFX_TelegraphRing.prefab").GetComponentInChildren<MeshRenderer>(true);
            Assert.That(bolt.renderQueue, Is.GreaterThan(sand.renderQueue));
            Assert.That(ring.sharedMaterial.renderQueue, Is.GreaterThan(sand.renderQueue));
        }
        finally
        {
            Object.DestroyImmediate(sand);
        }
    }

    private void Spawn(string path)
    {
        _instance = Object.Instantiate(Load<GameObject>(path));
        _instance.GetComponent<EnemyView>().Bind(Id, Vector3.zero);
        _events = new DomainEventHub();
    }

    private void SpawnShine()
    {
        Spawn(BodyPath);
        _shine = _instance.GetComponent<EnemyShineView>();
        _shine.Construct(_events);
        using var view = new SerializedObject(_shine);
        _overlay = (Renderer)view.FindProperty("_overlay").objectReferenceValue;
    }

    private EnemyAnimatorView SpawnAnimator(string path, out Animator animator)
    {
        Spawn(path);
        animator = _instance.GetComponentInChildren<Animator>();
        animator.Rebind();
        var view = animator.GetComponent<EnemyAnimatorView>();
        view.Construct(_events);
        return view;
    }

    private void Publish(ShinePhase phase, float seconds = 1f, int id = Id)
        => _events.Publish(new EnemyShineChanged(id, phase, seconds));

    private float Intensity()
    {
        var properties = new MaterialPropertyBlock();
        _overlay.GetPropertyBlock(properties);
        float intensity = properties.GetFloat("_Intensity");
        Assert.That(properties.GetFloat("_Alpha"), Is.EqualTo(intensity));
        return intensity;
    }

    private static bool HasCondition(AnimatorStateTransition transition, string parameter, AnimatorConditionMode mode, float threshold = 0f)
        => transition.conditions.Any(condition =>
            condition.parameter == parameter && condition.mode == mode && condition.threshold == threshold);

    private static T Load<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(Root + path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }
}
