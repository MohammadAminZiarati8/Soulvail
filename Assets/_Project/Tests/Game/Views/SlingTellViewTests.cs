using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class SlingTellViewTests
{
    private const string Root = "Assets/_Project/";
    private const string Jackal = "Prefabs/Enemies/Jackal.prefab";
    private const string Pitcher = "Prefabs/Enemies/Pitcher.prefab";
    private const string JackalController = "Animation/Controllers/AC_Jackal.controller";
    private const int Id = 11;
    private const float Windup = 0.7f;
    private static readonly ContentId JackalId = new("enemy.jackal");
    private static readonly ContentId StoneId = new("projectile.sling_stone");

    private GameObject _root;
    private DomainEventHub _hub;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("Jackal body test");
        _hub = new DomainEventHub();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_root);
        _hub.Dispose();
    }

    [Test]
    public void Telegraph_Mine_LightsStoneAndSpins()
    {
        SlingTellView view = CreateTell(out Renderer stone, out Transform sling);
        Quaternion rest = sling.localRotation;
        Telegraph();
        view.Step(0.1f);

        Assert.That(view.IsShowing && stone.enabled, Is.True);
        var properties = new MaterialPropertyBlock();
        stone.GetPropertyBlock(properties);
        Assert.That(Palette.IsDanger(properties.GetColor("_BaseColor")), Is.True);
        Quaternion expected = Quaternion.AngleAxis(90f, Vector3.up) * rest;
        Assert.That(Quaternion.Angle(sling.localRotation, expected), Is.LessThan(0.05f));
    }

    [Test]
    public void Telegraph_OtherId_Ignored()
    {
        SlingTellView view = CreateTell(out Renderer stone, out Transform sling);
        Quaternion rest = sling.localRotation;
        _hub.Publish(new EnemyTelegraph(Id + 1, Windup));
        _hub.Publish(new EnemyTelegraph(Id, 0f));
        view.Step(0.1f);
        AssertResting(view, stone, sling, rest);
    }

    [Test]
    public void Fired_StopsAndRests()
    {
        SlingTellView view = CreateTell(out Renderer stone, out Transform sling);
        Quaternion rest = StartSpin(view, sling);
        _hub.Publish(new ProjectileFired(1, StoneId, Id + 1, default, default, 1f));
        Assert.That(view.IsShowing, Is.True);
        _hub.Publish(new ProjectileFired(2, StoneId, Id, default, default, 1f));
        AssertResting(view, stone, sling, rest);
    }

    [Test]
    public void Duration_Elapsed_Hides()
    {
        SlingTellView view = CreateTell(out Renderer stone, out Transform sling);
        Quaternion rest = sling.localRotation;
        Telegraph();
        view.Step(Windup / 2f);
        Assert.That(view.IsShowing, Is.True);
        view.Step(Windup / 2f);
        AssertResting(view, stone, sling, rest);
    }

    [Test]
    public void Died_Hides()
    {
        SlingTellView view = CreateTell(out Renderer stone, out Transform sling);
        Quaternion rest = StartSpin(view, sling);
        _hub.Publish(new EnemyDied(Id + 1, JackalId, default));
        Assert.That(view.IsShowing, Is.True);
        _hub.Publish(new EnemyDied(Id, JackalId, default));
        AssertResting(view, stone, sling, rest);
    }

    [Test]
    public void Disable_Hides()
    {
        SlingTellView view = CreateTell(out Renderer stone, out Transform sling);
        Quaternion rest = StartSpin(view, sling);
        view.enabled = false;
        // EditMode does not dispatch OnDisable for ordinary MonoBehaviours.
        typeof(SlingTellView).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
        Telegraph();
        AssertResting(view, stone, sling, rest);
    }

    [Test]
    public void BacksAway_MovingBackwards_SetsBacking()
    {
        EnemyView body = Spawn(Jackal);
        var view = body.GetComponentInChildren<EnemyAnimatorView>();
        var animator = view.GetComponent<Animator>();
        body.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        Vector3 forward = body.transform.forward;
        animator.Rebind();
        animator.Update(0f);

        SetVelocity(body, -forward);
        view.Step(0.1f);
        Assert.That(animator.GetBool("Backing"), Is.True);
        SetVelocity(body, -forward * 0.05f);
        view.Step(0.1f);
        Assert.That(animator.GetBool("Backing"), Is.False);
        SetVelocity(body, forward);
        view.Step(0.1f);
        Assert.That(animator.GetBool("Backing"), Is.False);

        SetVelocity(body, -forward);
        view.Step(0.1f);
        view.Forget();
        Assert.That(animator.GetBool("Backing"), Is.False);
    }

    [Test]
    public void BacksAway_Off_NoBacking()
    {
        EnemyView body = Spawn(Pitcher);
        var view = body.GetComponentInChildren<EnemyAnimatorView>();
        var animator = view.GetComponent<Animator>();
        Assert.That(new SerializedObject(view).FindProperty("_backsAway").boolValue, Is.False);
        animator.Rebind();
        animator.Update(0f);
        SetVelocity(body, -body.transform.forward);
        view.Step(0.1f);
        Assert.That(animator.parameters.Any(p => p.name == "Backing"), Is.False);
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void Prefab_WiresTheViews()
    {
        GameObject jackal = Load<GameObject>(Jackal);
        GameObject pitcher = Load<GameObject>(Pitcher);
        Assert.That(jackal.GetComponents<Component>().Select(c => c.GetType()),
            Is.EquivalentTo(pitcher.GetComponents<Component>().Select(c => c.GetType())));
        Transform model = jackal.transform.Find("Model");
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject),
            Is.SameAs(Load<GameObject>("Art/Enemies/Jackal.fbx")));
        Assert.That(jackal.transform.Find("Mesh").gameObject.activeSelf, Is.False);

        var animator = model.GetComponent<Animator>();
        var view = new SerializedObject(model.GetComponent<EnemyAnimatorView>());
        Assert.That(view.FindProperty("_animator").objectReferenceValue, Is.SameAs(animator));
        Assert.That(animator.applyRootMotion, Is.False);
        Assert.That(animator.runtimeAnimatorController, Is.SameAs(Load<AnimatorController>(JackalController)));
        Assert.That(view.FindProperty("_backsAway").boolValue, Is.True);
        var feedback = new SerializedObject(jackal.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue,
            Is.SameAs(model.GetComponentInChildren<SkinnedMeshRenderer>()));
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.Zero);
        Assert.That(feedback.FindProperty("_telegraphSwell").floatValue, Is.EqualTo(1f));

        SlingTellView tell = model.GetComponent<SlingTellView>();
        GetTellParts(tell, out Renderer stone, out Transform sling);
        Assert.That(sling.parent.name, Is.EqualTo("handslot.r"));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(sling.gameObject),
            Is.SameAs(Load<GameObject>("Art/Enemies/JackalSling.fbx")));
        Assert.That(stone.transform.parent.name, Is.EqualTo("Stone"));
        Assert.That(stone.transform.IsChildOf(sling), Is.True);
        Assert.That(stone.enabled, Is.False);
    }

    [Test]
    public void Controller_ChainsAimIntoThrow()
    {
        AnimatorController controller = Load<AnimatorController>(JackalController);
        AnimatorStateMachine main = controller.layers[0].stateMachine;
        AnimatorState locomotion = main.defaultState;
        Assert.That(locomotion.name, Is.EqualTo("Locomotion"));
        var blend = (BlendTree)locomotion.motion;
        Assert.That(blend.blendParameter, Is.EqualTo("Speed"));
        Assert.That(blend.children.Select(c => c.motion.name), Is.EqualTo(new[] { "Idle_B", "Walking_B" }));
        AnimatorState backing = State(main, "Backing");
        Assert.That(backing.motion.name, Is.EqualTo("Walking_Backwards"));
        Direction(locomotion, backing, AnimatorConditionMode.If);
        Direction(backing, locomotion, AnimatorConditionMode.IfNot);

        AnimatorState aim = State(main, "Attack");
        AnimatorState throwStone = State(main, "Throw");
        Assert.That(aim.motion.name, Is.EqualTo("Ranged_1H_Aiming"));
        Assert.That(throwStone.motion.name, Is.EqualTo("Throw"));
        Assert.That(throwStone.speedParameterActive, Is.False);
        var transition = main.anyStateTransitions.Single(t => t.destinationState == aim);
        Assert.That(transition.conditions.Any(c => c.parameter == "Attack" && c.mode == AnimatorConditionMode.If), Is.True);
        Assert.That(transition.offset, Is.Zero);
        Assert.That(aim.transitions.Single().hasExitTime, Is.True);
        Assert.That(aim.transitions.Single().exitTime, Is.EqualTo(1f));
        Assert.That(aim.transitions.Single().destinationState, Is.SameAs(throwStone));
        Assert.That(throwStone.transitions.Single().destinationState, Is.SameAs(locomotion));
        Assert.That(State(main, "Death").motion.name, Is.EqualTo("Death_A"));
        Assert.That(State(controller.layers[1].stateMachine, "Hit").motion.name, Is.EqualTo("Hit_A"));

        var view = Spawn(Jackal).GetComponentInChildren<EnemyAnimatorView>();
        var animator = view.GetComponent<Animator>();
        view.Construct(_hub);
        animator.Rebind();
        animator.Update(0f);
        Telegraph();
        animator.Update(0f);
        animator.Update(0.06f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), Is.True);
        animator.Update(Windup - 0.07f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), Is.True);
        animator.Update(0.02f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Throw"), Is.True);
    }

    [Test]
    public void StoneMaterials_AreDanger()
    {
        Material material = Load<Material>("Materials/Enemies/M_SlingStone.mat");
        Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Unlit"));
        Assert.That(Palette.IsDanger(material.GetColor("_BaseColor")), Is.True);
        GetTellParts(Load<GameObject>(Jackal).GetComponentInChildren<SlingTellView>(), out Renderer stone, out _);
        Assert.That(stone.sharedMaterial, Is.SameAs(material));
        GameObject shot = Load<GameObject>("Prefabs/Projectiles/SlingStone.prefab");
        Assert.That(shot.GetComponent<ProjectileView>(), Is.Not.Null);
        Assert.That(shot.GetComponentInChildren<MeshRenderer>().sharedMaterial, Is.SameAs(material));
    }

    // Rule 7 is covered by VentTellViewTests.LandingRing_DrawsOverTheStorm for the shared ring.
    private SlingTellView CreateTell(out Renderer stone, out Transform sling)
    {
        var view = Spawn(Jackal).GetComponentInChildren<SlingTellView>();
        view.Construct(_hub);
        GetTellParts(view, out stone, out sling);
        return view;
    }

    private EnemyView Spawn(string path)
    {
        var body = Object.Instantiate(Load<GameObject>(path), _root.transform).GetComponent<EnemyView>();
        body.Bind(Id, Vector3.zero);
        return body;
    }

    private void Telegraph() => _hub.Publish(new EnemyTelegraph(Id, Windup));

    private Quaternion StartSpin(SlingTellView view, Transform sling)
    {
        Quaternion rest = sling.localRotation;
        Telegraph();
        view.Step(0.1f);
        return rest;
    }

    private static void AssertResting(SlingTellView view, Renderer stone, Transform sling, Quaternion rest)
    {
        Assert.That(view.IsShowing, Is.False);
        Assert.That(stone.enabled, Is.False);
        Assert.That(Quaternion.Angle(sling.localRotation, rest), Is.LessThan(0.05f));
    }

    private static void GetTellParts(SlingTellView view, out Renderer stone, out Transform sling)
    {
        var fields = new SerializedObject(view);
        stone = (Renderer)fields.FindProperty("_stone").objectReferenceValue;
        sling = (Transform)fields.FindProperty("_sling").objectReferenceValue;
    }

    private static void SetVelocity(EnemyView body, Vector3 velocity) =>
        typeof(EnemyView).GetField("_velocity", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(body, velocity);

    private static AnimatorState State(AnimatorStateMachine machine, string name) =>
        machine.states.Select(s => s.state).Single(s => s.name == name);

    private static void Direction(AnimatorState from, AnimatorState to, AnimatorConditionMode mode)
    {
        AnimatorStateTransition transition = from.transitions.Single();
        Assert.That(transition.destinationState, Is.SameAs(to));
        Assert.That(transition.hasExitTime, Is.False);
        AnimatorCondition condition = transition.conditions.Single();
        Assert.That(condition.parameter, Is.EqualTo("Backing"));
        Assert.That(condition.mode, Is.EqualTo(mode));
    }

    private static T Load<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(Root + path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }
}
