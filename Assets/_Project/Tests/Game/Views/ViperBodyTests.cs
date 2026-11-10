using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class ViperBodyTests
{
    private const string Root = "Assets/_Project/";
    private const string Viper = Root + "Prefabs/Enemies/Viper.prefab";
    private const string Panther = Root + "Prefabs/Enemies/Panther.prefab";
    private const string Model = Root + "Art/Enemies/Viper.fbx";
    private const string ControllerPath = Root + "Animation/Controllers/AC_Viper.controller";
    private const int Id = 11;
    private const float LaneWidth = 1.2f;

    private readonly List<Object> _created = new();

    [TearDown]
    public void TearDown()
    {
        foreach (Object obj in _created)
        {
            Object.DestroyImmediate(obj);
        }

        _created.Clear();
    }

    [Test]
    public void Sidewinds_MovingSideways_SetsSideways()
    {
        EnemyAnimatorView view = Spawn(Viper, out Animator animator, out _);
        SetVelocity(view, Vector3.right * 2.8f);
        view.Step(0.1f);
        Assert.That(animator.GetBool("Sideways"), Is.True);

        SetVelocity(view, Vector3.right * 0.5f);
        view.Step(0.1f);
        Assert.That(animator.GetBool("Sideways"), Is.False);
    }

    [Test]
    public void Sidewinds_MovingForward_NotSideways()
    {
        EnemyAnimatorView view = Spawn(Viper, out Animator animator, out _);
        EnemyView body = view.GetComponentInParent<EnemyView>();
        body.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        SetVelocity(view, Vector3.right * 2.8f);
        view.Step(0.1f);
        Assert.That(animator.GetBool("Sideways"), Is.False);

        SetVelocity(view, Quaternion.Euler(0f, 59f, 0f) * body.transform.forward * 2.8f);
        view.Step(0.1f);
        Assert.That(animator.GetBool("Sideways"), Is.False);
    }

    [Test]
    public void Sidewinds_Off_NoSideways()
    {
        EnemyAnimatorView view = Spawn(Panther, out Animator animator, out _);
        SetVelocity(view, Vector3.right * 2.8f);
        view.Step(0.1f);
        Assert.That(animator.parameters.Any(p => p.name == "Sideways"), Is.False);
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void StrikeMissed_Mine_SetsTrigger()
    {
        EnemyAnimatorView view = Spawn(Viper, out Animator animator, out DomainEventHub hub);
        hub.Publish(new EnemyStrikeMissed(Id, 2f));
        Assert.That(animator.GetBool("StrikeMissed"), Is.True);
        view.Forget();
        Assert.That(animator.GetBool("StrikeMissed"), Is.False);
    }

    [Test]
    public void StrikeMissed_OtherId_Ignored()
    {
        Spawn(Viper, out Animator animator, out DomainEventHub hub);
        hub.Publish(new EnemyStrikeMissed(Id + 1, 2f));
        Assert.That(animator.GetBool("StrikeMissed"), Is.False);
    }

    [Test]
    public void StrikeMissed_OtherController_Untouched()
    {
        Spawn(Panther, out Animator animator, out DomainEventHub hub);
        hub.Publish(new EnemyStrikeMissed(Id, 2f));
        Assert.That(animator.parameters.Any(p => p.name == "StrikeMissed"), Is.False);
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void Controller_ChainsCoilStrikeAndRecoil()
    {
        AnimatorController controller = Controller();
        AnimatorStateMachine main = controller.layers[0].stateMachine;
        AnimatorState locomotion = State(main, "Locomotion");
        AnimatorState sidewind = State(main, "Sidewind");
        AnimatorState coil = State(main, "Coil");
        AnimatorState strike = State(main, "Strike");
        AnimatorState stretched = State(main, "Stretched");
        AnimatorState recoil = State(main, "Recoil");
        Assert.That(main.defaultState, Is.SameAs(locomotion));
        var blend = (BlendTree)locomotion.motion;
        Assert.That(blend.blendParameter, Is.EqualTo("Speed"));
        Assert.That(blend.children.Select(c => c.motion.name), Is.EqualTo(new[] { "Idle", "Slither" }));
        Condition(locomotion.transitions.Single(), sidewind, "Sideways", AnimatorConditionMode.If);
        Condition(sidewind.transitions.Single(), locomotion, "Sideways", AnimatorConditionMode.IfNot);
        Assert.That(coil.speedParameterActive && coil.speedParameter == "AttackRate", Is.True);
        AnimatorStateTransition attack = main.anyStateTransitions.Single(t => t.destinationState == coil);
        Assert.That(attack.conditions.Any(c => c.parameter == "Attack" && c.mode == AnimatorConditionMode.If), Is.True);
        Assert.That(attack.offset, Is.Zero);
        Exit(coil.transitions.Single(), strike);
        Assert.That(strike.transitions, Has.Length.EqualTo(2));
        Exit(strike.transitions[0], stretched);
        Condition(strike.transitions[0], stretched, "StrikeMissed", AnimatorConditionMode.If);
        Exit(strike.transitions[1], recoil);
        Assert.That(strike.transitions[1].conditions, Is.Empty);
        Exit(stretched.transitions.Single(), locomotion);
        Exit(recoil.transitions.Single(), locomotion);
        AnimatorState death = State(main, "Death");
        Condition(main.anyStateTransitions.Single(t => t.destinationState == death), death, "Dead", AnimatorConditionMode.If);
        AnimatorStateMachine flinch = controller.layers[1].stateMachine;
        AnimatorState hit = State(flinch, "Hit");
        Assert.That(flinch.anyStateTransitions.Single().destinationState, Is.SameAs(hit));
        Assert.That(hit.transitions.Single().destinationState, Is.SameAs(flinch.defaultState));

        // Core publishes the miss as the dash starts. It must survive the whole Strike clip.
        foreach (bool missed in new[] { false, true })
        {
            Spawn(Viper, out Animator animator, out DomainEventHub hub);
            hub.Publish(new EnemyTelegraph(Id, 0.6f));
            animator.Update(0f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Coil"), Is.True);
            Assert.That(animator.GetFloat("AttackRate"), Is.EqualTo(1.5f).Within(1e-4f));
            animator.Update(0.59f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Coil"), Is.True);
            if (missed)
            {
                hub.Publish(new EnemyStrikeMissed(Id, 2f));
            }

            animator.Update(0.02f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Strike"), Is.True);
            animator.Update(0.41f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName(missed ? "Stretched" : "Recoil"), Is.True);
        }
    }

    [Test]
    public void Controller_EveryStateIsTheModels()
    {
        AnimatorController controller = Controller();
        foreach (AnimatorState state in controller.layers.SelectMany(l => l.stateMachine.states).Select(s => s.state))
        {
            Assert.That(state.writeDefaultValues, Is.False, state.name);
            if (state.name != "Empty")
            {
                Assert.That(state.motion, Is.Not.Null, state.name);
            }
        }

        Assert.That(controller.animationClips.Select(c => c.name), Is.EquivalentTo(new[]
        {
            "Idle", "Slither", "Sidewind", "Coil", "Strike", "Stretched", "Recoil", "Hit", "Death",
        }));
        Assert.That(controller.animationClips.All(c => AssetDatabase.GetAssetPath(c) == Model), Is.True);
    }

    [Test]
    public void Prefab_WiresTheViews()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Viper);
        var panther = AssetDatabase.LoadAssetAtPath<GameObject>(Panther);
        Assert.That(prefab.GetComponents<Component>().Select(c => c.GetType()),
            Is.EquivalentTo(panther.GetComponents<Component>().Select(c => c.GetType())));
        Assert.That(prefab.transform.Find("Mesh").gameObject.activeSelf, Is.False);
        Transform model = prefab.transform.Find("Model");
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject),
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(Model)));
        Assert.That(model.localScale, Is.EqualTo(Vector3.one));
        Transform spine = model.Find("Rig_Viper/Root/Spine_01");
        Assert.That(Vector3.Distance(spine.position, prefab.transform.position), Is.LessThan(1e-4f));

        var animator = model.GetComponent<Animator>();
        Assert.That(animator.runtimeAnimatorController, Is.SameAs(Controller()));
        Assert.That(animator.applyRootMotion, Is.False);
        var view = new SerializedObject(model.GetComponent<EnemyAnimatorView>());
        Assert.That(view.FindProperty("_sidewinds").boolValue, Is.True);
        Assert.That(view.FindProperty("_animator").objectReferenceValue, Is.SameAs(animator));
        Assert.That(view.FindProperty("_strikeSeconds").floatValue, Is.EqualTo(0.9f).Within(1e-4f));
        Assert.That(view.FindProperty("_windupFromSeconds").floatValue, Is.Zero);
        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.Zero);
        Assert.That(feedback.FindProperty("_telegraphSwell").floatValue, Is.EqualTo(1f));
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue,
            Is.SameAs(model.GetComponentInChildren<SkinnedMeshRenderer>(true)));
    }

    [Test]
    public void Strip_FitsTheViper()
    {
        GameObject body = Instance(Viper);
        var skin = body.GetComponentInChildren<SkinnedMeshRenderer>();
        var mesh = new Mesh();
        _created.Add(mesh);
        skin.BakeMesh(mesh);
        float[] across = mesh.vertices.Select(v => body.transform.InverseTransformPoint(skin.transform.TransformPoint(v)).x).ToArray();
        Assert.That(across.Max() - across.Min(), Is.InRange(0.01f, LaneWidth));
        Assert.That(body.GetComponent<CharacterController>().radius * 2f, Is.LessThanOrEqualTo(LaneWidth));
    }

    [Test]
    public void Strip_DrawsOverTheStorm()
    {
        var parent = new GameObject("Viper lane test");
        _created.Add(parent);
        parent.SetActive(false);
        var dunes = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Arenas/Sunscar/Arena_Dunes.prefab");
        var arena = Object.Instantiate(dunes, parent.transform).GetComponent<ArenaView>();
        var storm = arena.GetComponentInChildren<SunscarSandstormView>(true);
        storm.Bind(arena, parent.transform);
        var stormMaterial = storm.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        _created.Insert(0, stormMaterial);

        using var hub = new DomainEventHub();
        using IObjectResolver container = new ContainerBuilder().Build();
        var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(Viper).GetComponent<EnemyView>();
        using var enemies = new EnemyViews(container, enemy, parent.transform, hub,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()));
        using var views = new BossViews(container, Vfx<ShockwaveView>("VFX_Shockwave"), Vfx<FissureView>("VFX_Fissure"),
            Vfx<BossBeatView>("VFX_BossBeat"), parent.transform, hub, enemies, null, 0, 0, 0);
        hub.Publish(new LaneArmed(Id, System.Numerics.Vector3.Zero, System.Numerics.Vector2.UnitY, 12f, LaneWidth / 2f, 0.9f));
        var lane = parent.transform.Find($"Lane {Id}").GetComponent<MeshRenderer>();
        Assert.That(lane.sharedMaterial.renderQueue, Is.GreaterThan(stormMaterial.renderQueue));

        var properties = new MaterialPropertyBlock();
        lane.GetPropertyBlock(properties);
        Color colour = properties.GetColor("_BaseColor");
        Assert.That(colour.a, Is.GreaterThan(0f));
        Assert.That(colour.r / colour.a, Is.EqualTo(Palette.Danger.r).Within(1e-4f));
        Assert.That(colour.g / colour.a, Is.EqualTo(Palette.Danger.g).Within(1e-4f));
        Assert.That(colour.b / colour.a, Is.EqualTo(Palette.Danger.b).Within(1e-4f));
    }

    private GameObject Instance(string path)
    {
        var obj = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        _created.Add(obj);
        return obj;
    }

    private EnemyAnimatorView Spawn(string path, out Animator animator, out DomainEventHub hub)
    {
        GameObject obj = Instance(path);
        animator = obj.GetComponentInChildren<Animator>();
        var view = obj.GetComponentInChildren<EnemyAnimatorView>();
        hub = new DomainEventHub();
        view.Construct(hub);
        obj.GetComponent<EnemyView>().Bind(Id, Vector3.zero);
        animator.Rebind();
        animator.Update(0f);
        return view;
    }

    private static void SetVelocity(EnemyAnimatorView view, Vector3 velocity) =>
        typeof(EnemyView).GetField("_velocity", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(view.GetComponentInParent<EnemyView>(), velocity);

    private static T Vfx<T>(string name) where T : Component =>
        AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Vfx/" + name + ".prefab").GetComponent<T>();

    private static AnimatorController Controller() => AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

    private static AnimatorState State(AnimatorStateMachine machine, string name) =>
        machine.states.Select(s => s.state).Single(s => s.name == name);

    private static void Exit(AnimatorStateTransition transition, AnimatorState destination)
    {
        Assert.That(transition.destinationState, Is.SameAs(destination));
        Assert.That(transition.hasExitTime, Is.True);
        Assert.That(transition.exitTime, Is.EqualTo(1f));
    }

    private static void Condition(AnimatorStateTransition transition, AnimatorState destination, string parameter, AnimatorConditionMode mode)
    {
        Assert.That(transition.destinationState, Is.SameAs(destination));
        AnimatorCondition condition = transition.conditions.Single();
        Assert.That(condition.parameter, Is.EqualTo(parameter));
        Assert.That(condition.mode, Is.EqualTo(mode));
    }
}
