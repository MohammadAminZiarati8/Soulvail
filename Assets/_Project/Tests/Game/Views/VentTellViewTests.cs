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
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class VentTellViewTests
{
    private const string Assets = "Assets/_Project/";
    private const string Beetle = "Prefabs/Enemies/Beetle.prefab";
    private const string Pitcher = "Prefabs/Enemies/Pitcher.prefab";
    private const string Controller = "Animation/Controllers/AC_Beetle.controller";
    private const int Id = 11;
    private const float Windup = 0.7f;
    private static readonly ContentId BeetleId = new("enemy.beetle");

    private GameObject _root;
    private DomainEventHub _hub;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("SS-06c test");
        _hub = new DomainEventHub();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_root);
        _hub.Dispose();
    }

    [Test]
    public void Telegraph_Mine_LightsVent()
    {
        VentTellView view = CreateVent();
        Telegraph();
        Assert.That(view.IsShowing, Is.True);
        var block = new MaterialPropertyBlock();
        var disc = (Renderer)new SerializedObject(view).FindProperty("_vent").objectReferenceValue;
        disc.GetPropertyBlock(block);
        Assert.That(Palette.IsDanger(block.GetColor("_BaseColor")), Is.True);
    }

    [Test]
    public void Telegraph_OtherId_Ignored()
    {
        VentTellView view = CreateVent();
        _hub.Publish(new EnemyTelegraph(Id + 1, Windup));
        Assert.That(view.IsShowing, Is.False);
        _hub.Publish(new EnemyTelegraph(Id, 0f));
        Assert.That(view.IsShowing, Is.False);
    }

    [Test]
    public void Fired_Hides()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(_hub);
        using var container = builder.Build();
        var book = new EnemyLookBook(new Dictionary<ContentId, EnemyLook>());
        var template = Object.Instantiate(Load<GameObject>(Beetle), _root.transform);
        // The muzzle test uses the tell and the real pools. Status and hit feedback have separate fixtures.
        Object.DestroyImmediate(template.GetComponent<EnemyStatusView>());
        Object.DestroyImmediate(template.GetComponent<EnemyHitFeedback>());
        using var enemies = new EnemyViews(container, template.GetComponent<EnemyView>(),
            _root.transform, _hub, book);
        using var shots = new ProjectileViews(container,
            Load<GameObject>("Prefabs/Projectiles/BeetleGlob.prefab").GetComponent<ProjectileView>(),
            _root.transform, _hub, enemies: enemies);
        _hub.Publish(new EnemySpawned(Id, BeetleId, default));
        Assert.That(enemies.TryGet(Id, out EnemyView body), Is.True);
        var tell = body.GetComponentInChildren<VentTellView>();
        var muzzle = tell.Muzzle.position;
        Telegraph();
        _hub.Publish(new ProjectileFired(1, BeetleId, Id + 1, default, default, 2f));
        Assert.That(tell.IsShowing, Is.True);
        Assert.That(shots.TryGet(1, out ProjectileView otherShot), Is.True);
        Assert.That(otherShot.transform.position, Is.EqualTo(Vector3.zero));
        _hub.Publish(new ProjectileFired(2, BeetleId, Id, default, default, 2f));
        Assert.That(tell.IsShowing, Is.False);
        Assert.That(shots.TryGet(2, out ProjectileView shot), Is.True);
        Assert.That(shot.transform.position, Is.EqualTo(muzzle));
    }

    [Test]
    public void AimDropped_Hides()
    {
        VentTellView view = CreateVent();
        Telegraph();
        _hub.Publish(new EnemyAimDropped(Id + 1));
        Assert.That(view.IsShowing, Is.True);
        _hub.Publish(new EnemyAimDropped(Id));
        Assert.That(view.IsShowing, Is.False);
    }

    [Test]
    public void Duration_Elapsed_Hides()
    {
        VentTellView view = CreateVent();
        Telegraph();
        view.Step(Windup / 2f);
        Assert.That(view.IsShowing, Is.True);
        view.Step(Windup / 2f);
        Assert.That(view.IsShowing, Is.False);
    }

    [Test]
    public void Died_Hides()
    {
        VentTellView view = CreateVent();
        Telegraph();
        _hub.Publish(new EnemyDied(Id + 1, BeetleId, default));
        Assert.That(view.IsShowing, Is.True);
        _hub.Publish(new EnemyDied(Id, BeetleId, default));
        Assert.That(view.IsShowing, Is.False);
    }

    [Test]
    public void Disable_Hides()
    {
        VentTellView view = CreateVent();
        Telegraph();
        // EditMode does not dispatch MonoBehaviour disable callbacks for ordinary components.
        view.enabled = false;
        typeof(VentTellView).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
        Assert.That(view.IsShowing, Is.False);
        Telegraph();
        Assert.That(view.IsShowing, Is.False);
    }

    [Test]
    public void TurnsInPlace_Yawing_SetsTurning()
    {
        EnemyView body = Spawn(Beetle);
        EnemyAnimatorView view = body.GetComponentInChildren<EnemyAnimatorView>();
        Animator animator = view.GetComponent<Animator>();
        StepTurn(body, view);
        Assert.That(animator.GetBool("Turning"), Is.True);
        view.Step(0.1f);
        Assert.That(animator.GetBool("Turning"), Is.False);
    }

    [Test]
    public void TurnsInPlace_Off_NoTurning()
    {
        EnemyView body = Spawn(Pitcher);
        EnemyAnimatorView view = body.GetComponentInChildren<EnemyAnimatorView>();
        Assert.That(new SerializedObject(view).FindProperty("_turnsInPlace").boolValue, Is.False);
        StepTurn(body, view);
        Assert.That(view.GetComponent<Animator>().parameters.Any(p => p.name == "Turning"), Is.False);
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void AimDropped_OtherController_Untouched()
    {
        EnemyView body = Spawn(Pitcher);
        EnemyAnimatorView view = body.GetComponentInChildren<EnemyAnimatorView>();
        view.Construct(_hub);
        _hub.Publish(new EnemyAimDropped(Id));
        Assert.That(view.GetComponent<Animator>().parameters.Any(p => p.name == "AimDropped"), Is.False);
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void Prefab_WiresTheViews()
    {
        GameObject prefab = Load<GameObject>(Beetle);
        Assert.That(prefab.GetComponents<Component>().Select(c => c.GetType()),
            Is.EquivalentTo(Load<GameObject>(Pitcher).GetComponents<Component>().Select(c => c.GetType())));
        var animator = prefab.GetComponentInChildren<Animator>(true);
        var modelSource = PrefabUtility.GetCorrespondingObjectFromSource(animator.gameObject);
        Assert.That(AssetDatabase.GetAssetPath(modelSource), Is.EqualTo(Assets + "Art/Enemies/Beetle.fbx"));
        Assert.That(animator.runtimeAnimatorController, Is.SameAs(Load<AnimatorController>(Controller)));
        Assert.That(animator.applyRootMotion, Is.False);
        var animation = new SerializedObject(prefab.GetComponentInChildren<EnemyAnimatorView>(true));
        Assert.That(animation.FindProperty("_turnsInPlace").boolValue, Is.True);
        Assert.That(animation.FindProperty("_animator").objectReferenceValue, Is.SameAs(animator));
        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.Zero);
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue, Is.TypeOf<SkinnedMeshRenderer>());

        var view = prefab.GetComponentInChildren<VentTellView>(true);
        var disc = (Renderer)new SerializedObject(view).FindProperty("_vent").objectReferenceValue;
        Assert.That(disc.enabled, Is.False);
        Assert.That(view.Muzzle.name, Is.EqualTo("Vent"));
        Assert.That(disc.transform.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(disc.transform.lossyScale.x, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(disc.transform.lossyScale.z, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(Vector3.Dot(disc.transform.up, view.Muzzle.forward), Is.GreaterThan(0.999f));
        Assert.That(Vector3.Dot(disc.transform.up, prefab.transform.forward), Is.LessThan(0f));
        Assert.That(disc.GetComponent<Collider>(), Is.Null);
    }

    [Test]
    public void Controller_ChainsAimIntoFireOrDrop()
    {
        var controller = Load<AnimatorController>(Controller);
        var machine = controller.layers[0].stateMachine;
        var locomotion = machine.defaultState;
        Assert.That(locomotion.name, Is.EqualTo("Locomotion"));
        var blend = (BlendTree)locomotion.motion;
        Assert.That(blend.blendParameter, Is.EqualTo("Speed"));
        Assert.That(blend.children.Select(c => c.motion.name), Is.EqualTo(new[] { "Idle", "Walk" }));
        var turn = State(machine, "Turn");
        Assert.That(turn.motion.name, Is.EqualTo("Turn"));
        Assert.That(locomotion.transitions.Single(t => t.destinationState == turn).conditions.Single().parameter,
            Is.EqualTo("Turning"));
        Assert.That(turn.transitions.Single().conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.IfNot));
        var aim = State(machine, "Aim");
        Assert.That(aim.motion.name, Is.EqualTo("Aim"));
        Assert.That(machine.anyStateTransitions.Single(t => t.destinationState == aim).conditions.Any(c => c.parameter == "Attack"),
            Is.True);
        var fire = State(machine, "Fire");
        Assert.That(fire.motion.name, Is.EqualTo("Fire"));
        var drop = aim.transitions[0];
        Assert.That(drop.destinationState, Is.SameAs(locomotion));
        Assert.That(drop.hasExitTime, Is.False);
        Assert.That(drop.conditions.Single().parameter, Is.EqualTo("AimDropped"));
        Assert.That(aim.transitions.Single(t => t.destinationState == fire).hasExitTime, Is.True);
        Assert.That(fire.transitions.Single().destinationState, Is.SameAs(locomotion));
        Assert.That(State(machine, "Death").motion.name, Is.EqualTo("Death"));
        var flinch = controller.layers.Single(l => l.name == "Flinch");
        Assert.That(State(flinch.stateMachine, "Hit").motion.name, Is.EqualTo("Hit"));
        var mask = flinch.avatarMask;
        var maskedBones = Enumerable.Range(0, mask.transformCount).Where(mask.GetTransformActive)
            .Select(i => mask.GetTransformPath(i).Split('/').Last());
        Assert.That(maskedBones, Is.EquivalentTo(new[] { "Head", "Abdomen" }));

        EnemyView body = Spawn(Beetle);
        EnemyAnimatorView view = body.GetComponentInChildren<EnemyAnimatorView>();
        view.Construct(_hub);
        Animator animator = view.GetComponent<Animator>();
        animator.Rebind();
        Telegraph();
        animator.Update(0.01f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Aim"), Is.True);
        _hub.Publish(new EnemyAimDropped(Id + 1));
        animator.Update(0.01f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Aim"), Is.True);
        _hub.Publish(new EnemyAimDropped(Id));
        animator.Update(Windup);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), Is.True);
        Telegraph();
        animator.Update(0.01f);
        animator.Update(Windup + 0.01f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Fire"), Is.True);
    }

    [Test]
    public void GlobMaterials_AreDanger()
    {
        GameObject glob = Load<GameObject>("Prefabs/Projectiles/BeetleGlob.prefab");
        var ball = glob.GetComponentInChildren<MeshRenderer>();
        Assert.That(ball.bounds.size.x, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(ball.bounds.size.y, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(ball.bounds.size.z, Is.EqualTo(0.5f).Within(0.001f));
        var tell = Load<Material>("Materials/Enemies/M_BeetleTell.mat");
        Assert.That(tell.shader.name, Is.EqualTo("Universal Render Pipeline/Unlit"));
        Assert.That(Palette.IsDanger(tell.GetColor("_BaseColor")), Is.True);
        Assert.That(Palette.IsDanger(ball.sharedMaterial.GetColor("_BaseColor")), Is.True);
    }

    [Test]
    public void LandingRing_DrawsOverTheStorm()
    {
        var dunes = Load<GameObject>("Prefabs/Arenas/Sunscar/Arena_Dunes.prefab");
        var arena = Object.Instantiate(dunes, _root.transform).GetComponent<ArenaView>();
        var storm = arena.GetComponentInChildren<SunscarSandstormView>(true);
        storm.Bind(arena, _root.transform);
        var stormMaterial = storm.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        var ring = Load<Material>("Materials/M_TelegraphRing.mat");
        var tell = Load<Material>("Materials/Enemies/M_BeetleTell.mat");
        try
        {
            Assert.That(ring.renderQueue, Is.GreaterThan(stormMaterial.renderQueue));
            Assert.That(tell.renderQueue, Is.GreaterThan(stormMaterial.renderQueue));
        }
        finally
        {
            Object.DestroyImmediate(stormMaterial);
        }
    }

    private VentTellView CreateVent()
    {
        var view = Spawn(Beetle).GetComponentInChildren<VentTellView>();
        view.Construct(_hub);
        return view;
    }

    private EnemyView Spawn(string path)
    {
        var body = Object.Instantiate(Load<GameObject>(path), _root.transform).GetComponent<EnemyView>();
        body.Bind(Id, Vector3.zero);
        return body;
    }

    private void Telegraph() => _hub.Publish(new EnemyTelegraph(Id, Windup));

    private static void StepTurn(EnemyView body, EnemyAnimatorView view)
    {
        view.Step(0.1f);
        body.transform.rotation = Quaternion.Euler(0f, 4.5f, 0f);
        view.Step(0.1f);
    }

    private static AnimatorState State(AnimatorStateMachine machine, string name) =>
        machine.states.Single(c => c.state.name == name).state;

    private static T Load<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(Assets + path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }
}
