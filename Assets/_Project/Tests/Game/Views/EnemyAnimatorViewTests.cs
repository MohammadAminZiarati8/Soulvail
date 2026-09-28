using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// <c>EnemyAnimatorView</c>, <c>AC_Rootling</c> and <c>Rootling.prefab</c> (M7-05h): the legs follow
/// the body, the strike lands when core's does, a hit flinches, a death is final, a pooled body
/// forgets — and the shipped controller and prefab are wired the way those rules assume.
/// </summary>
/// <remarks>
/// The view rows drive a test-double controller, <c>PlayerAnimatorViewTests</c>' shape: parameters
/// and no states, read back through <see cref="Animator.GetFloat(int)"/> and
/// <see cref="Animator.GetBool(int)"/>, which round-trip in EditMode where no state machine runs.
/// </remarks>
[TestFixture]
public sealed class EnemyAnimatorViewTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Rootling.prefab";
    private const string EnemyPrefabPath = "Assets/_Project/Prefabs/Enemies/Enemy.prefab";
    private const string ControllerPath = "Assets/_Project/Animation/Controllers/AC_Rootling.controller";
    private const string MaskPath = "Assets/_Project/Animation/Masks/AM_UpperBody.mask";
    private const string DissolvePath = "Assets/_Project/Materials/Enemies/M_Enemy_Dissolve.mat";
    private const string ClipFolder = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/";

    private const int Id = 11;

    private static readonly ContentId Rootling = new ContentId("enemy.rootling");

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int WalkRateId = Animator.StringToHash("WalkRate");
    private static readonly int AttackRateId = Animator.StringToHash("AttackRate");
    private static readonly int AttackId = Animator.StringToHash("Attack");
    private static readonly int HitId = Animator.StringToHash("Hit");
    private static readonly int DeadId = Animator.StringToHash("Dead");

    private GameObject _root;
    private EnemyView _body;
    private EnemyAnimatorView _view;
    private Animator _animator;
    private AnimatorController _controller;
    private DomainEventHub _hub;

    [SetUp]
    public void CreateBody()
    {
        _controller = new AnimatorController { name = "AC_EnemyTestDouble", hideFlags = HideFlags.HideAndDontSave };
        _controller.AddLayer("Base Layer");
        _controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        _controller.AddParameter("WalkRate", AnimatorControllerParameterType.Float);
        _controller.AddParameter("AttackRate", AnimatorControllerParameterType.Float);
        _controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        _controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        _controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

        // The body at the root, the view on a model below it — the prefab's own arrangement.
        _root = new GameObject("Rootling");
        _body = _root.AddComponent<EnemyView>();

        var model = new GameObject("Model");
        model.transform.SetParent(_root.transform, false);

        _animator = model.AddComponent<Animator>();
        _animator.runtimeAnimatorController = _controller;

        _view = model.AddComponent<EnemyAnimatorView>();
        typeof(EnemyAnimatorView).GetField("_animator", Private).SetValue(_view, _animator);

        _hub = new DomainEventHub();

        _body.Bind(Id, Vector3.zero);
    }

    [TearDown]
    public void DestroyBody()
    {
        _hub?.Dispose();
        _hub = null;

        if (_root != null)
        {
            Object.DestroyImmediate(_root);
        }

        if (_controller != null)
        {
            Object.DestroyImmediate(_controller);
        }
    }

    // ---- The legs (rule 1) -----------------------------------------------------------------------

    [Test]
    public void Walk_SpeedFollowsTheBody()
    {
        Walk(2f);

        for (int i = 0; i < 10; i++)
        {
            _view.Step(0.05f);
        }

        Assert.That(_animator.GetFloat(SpeedId), Is.EqualTo(2f).Within(1e-4f));
    }

    [TestCase(2f, 1.6f)]
    [TestCase(8f, 4f)]
    [TestCase(0.1f, 0.6f)]
    public void Walk_RateIsTheStrideClamped(float speed, float rate)
    {
        // A stride of 1.25 m/s: 2 m/s plays at 1.6x, a sprint stops at the ceiling and leaves the feet
        // to slide, and a shuffle is held at the floor rather than frozen.
        typeof(EnemyAnimatorView).GetField("_strideSpeed", Private).SetValue(_view, 1.25f);

        Walk(speed);

        for (int i = 0; i < 20; i++)
        {
            _view.Step(0.05f);
        }

        Assert.That(_animator.GetFloat(WalkRateId), Is.EqualTo(rate).Within(1e-4f));
    }

    [Test]
    public void Walk_ABadStepDoesNothing()
    {
        Walk(2f);

        _view.Step(0f);
        _view.Step(float.NaN);
        _view.Step(-1f);

        Assert.That(_animator.GetFloat(SpeedId), Is.Zero);
    }

    [Test]
    public void View_FindsEnemyViewInItsParent()
    {
        // The view lives on the model, not beside the EnemyView: the body is found above it, which is
        // what the Walk rows read their velocity through.
        Walk(1f);
        _view.Step(1f);

        Assert.That(_animator.GetFloat(SpeedId), Is.EqualTo(1f).Within(1e-4f));
    }

    [Test]
    public void View_WithNoEnemyViewAboveThrowsInStart()
    {
        var loose = new GameObject("Loose");

        try
        {
            loose.AddComponent<Animator>();
            var view = loose.AddComponent<EnemyAnimatorView>();
            typeof(EnemyAnimatorView).GetField("_animator", Private).SetValue(view, loose.GetComponent<Animator>());

            var thrown = Assert.Throws<TargetInvocationException>(
                () => typeof(EnemyAnimatorView).GetMethod("Start", Private).Invoke(view, null));

            Assert.That(thrown.InnerException, Is.InstanceOf<InvalidOperationException>());
            Assert.That(thrown.InnerException.Message, Does.Contain(nameof(EnemyView)));
        }
        finally
        {
            Object.DestroyImmediate(loose);
        }
    }

    // ---- The strike (rule 2) ---------------------------------------------------------------------

    [Test]
    public void Attack_LandsWhenCoresBlowDoes()
    {
        // The shipped timing: the state starts 0.35 s into the chop and the blow lands at 0.87 s, so
        // a 0.4 s wind-up plays the remaining 0.52 s at 1.3x and the blow meets core's at its end.
        _view.Construct(_hub);

        _hub.Publish(new EnemyTelegraph(Id, 0.4f));

        Assert.That(_animator.GetFloat(AttackRateId), Is.EqualTo(0.52f / 0.4f).Within(1e-4f));
        Assert.That(_animator.GetBool(AttackId), Is.True);
    }

    [Test]
    public void Attack_IgnoresAnotherEnemy()
    {
        _view.Construct(_hub);

        _hub.Publish(new EnemyTelegraph(Id + 1, 0.4f));

        Assert.That(_animator.GetBool(AttackId), Is.False, "Every pooled body hears every wind-up.");
    }

    [Test]
    public void Attack_AZeroWindUpFiresNothing()
    {
        _view.Construct(_hub);

        _hub.Publish(new EnemyTelegraph(Id, 0f));

        Assert.That(_animator.GetBool(AttackId), Is.False);
    }

    // ---- The flinch and the fall (rules 3, 4) ----------------------------------------------------

    [Test]
    public void Hit_FlinchesOnAHitThatDoesNotKill()
    {
        _view.Construct(_hub);

        _hub.Publish(new EnemyDamaged(Id, 5f, 0.5f, killed: false));

        Assert.That(_animator.GetBool(HitId), Is.True);
    }

    [Test]
    public void Hit_AKillingBlowDoesNotFlinch()
    {
        _view.Construct(_hub);

        _hub.Publish(new EnemyDamaged(Id, 50f, 0f, killed: true));

        Assert.That(_animator.GetBool(HitId), Is.False, "The death is the answer to a killing blow.");
    }

    [Test]
    public void Death_IsFinal()
    {
        _view.Construct(_hub);

        _hub.Publish(new EnemyDied(Id, Rootling, System.Numerics.Vector3.Zero));
        _hub.Publish(new EnemyDamaged(Id, 5f, 0f, killed: false));
        _hub.Publish(new EnemyTelegraph(Id, 0.4f));

        Assert.That(_animator.GetBool(DeadId), Is.True);
        Assert.That(_animator.GetBool(HitId), Is.False, "A corpse does not flinch.");
        Assert.That(_animator.GetBool(AttackId), Is.False, "A corpse does not strike.");
    }

    // ---- The pooled reset (rule 5) ---------------------------------------------------------------

    [Test]
    public void Forget_ClearsTheLife()
    {
        _view.Construct(_hub);
        Walk(2f);
        _view.Step(1f);
        _hub.Publish(new EnemyDied(Id, Rootling, System.Numerics.Vector3.Zero));

        _view.Forget();

        Assert.That(_animator.GetBool(DeadId), Is.False);
        Assert.That(_animator.GetFloat(SpeedId), Is.Zero);

        // And the next life strikes: the latch is gone with the flag.
        _hub.Publish(new EnemyTelegraph(Id, 0.4f));

        Assert.That(_animator.GetBool(AttackId), Is.True);
    }

    [Test]
    public void Despawn_TellsTheViewToForget()
    {
        // EnemyView.OnDespawn is the one place a pooled body is put back (AR §18.4); the animator
        // view joins its list.
        _view.Construct(_hub);
        _hub.Publish(new EnemyDied(Id, Rootling, System.Numerics.Vector3.Zero));

        _body.OnDespawn();

        Assert.That(_animator.GetBool(DeadId), Is.False);
    }

    // ---- The shipped controller and body (rules 7, 8) --------------------------------------------

    [Test]
    public void Rootling_ControllerNamesItsStates()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        Assert.That(controller, Is.Not.Null, ControllerPath);
        Assert.That(controller.parameters.Select(p => p.name),
            Is.EquivalentTo(new[] { "Speed", "WalkRate", "AttackRate", "Attack", "Hit", "Dead" }),
            "The six parameters the view writes, and no others.");

        AnimatorStateMachine body = controller.layers[0].stateMachine;
        AnimatorState locomotion = State(body, "Locomotion");
        AnimatorState attack = State(body, "Attack");
        AnimatorState death = State(body, "Death");

        Assert.That(body.defaultState, Is.SameAs(locomotion));
        Assert.That(locomotion.speedParameterActive && locomotion.speedParameter == "WalkRate", Is.True);
        Assert.That(attack.speedParameterActive && attack.speedParameter == "AttackRate", Is.True);

        var tree = (BlendTree)locomotion.motion;
        Assert.That(tree.blendParameter, Is.EqualTo("Speed"));
        Assert.That(tree.children.Select(c => c.motion.name), Is.EqualTo(new[] { "Skeletons_Idle", "Crouching" }));

        var chop = (AnimationClip)attack.motion;
        Assert.That(chop.name, Is.EqualTo("Melee_2H_Attack_Chop"));
        Assert.That(death.motion.name, Is.EqualTo("Death_A"));

        // The offset the Attack state is entered at is the clip time the view's rate counts from, or
        // the blow lands early or late by the difference (rule 2).
        AnimatorStateTransition toAttack = body.anyStateTransitions.Single(t => t.destinationState == attack);
        float windupFrom = ViewField(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), "_windupFromSeconds");
        Assert.That(toAttack.offset * chop.length, Is.EqualTo(windupFrom).Within(1e-3f));

        AnimatorControllerLayer flinch = controller.layers[1];
        Assert.That(flinch.avatarMask, Is.EqualTo(AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath)));
        Assert.That(State(flinch.stateMachine, "Hit").motion.name, Is.EqualTo("Hit_A"));

        foreach (AnimatorState state in controller.layers.SelectMany(l => l.stateMachine.states).Select(s => s.state))
        {
            Assert.That(state.writeDefaultValues, Is.False,
                $"{state.name} writes defaults, which snaps the masked bones to the bind pose from an empty flinch state.");
        }

        foreach (AnimationClip clip in controller.animationClips)
        {
            Assert.That(AssetDatabase.GetAssetPath(clip), Does.StartWith(ClipFolder), $"{clip.name} is not a Rig_Medium clip.");
        }
    }

    [Test]
    public void Rootling_IsAWholeBody()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);

        Assert.That(prefab, Is.Not.Null, PrefabPath);
        Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Variant));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab), Is.EqualTo(enemy),
            "A variant of the shared body, so its colliders, feedback and health bar are the Husk's.");

        CapsuleCollider capsule = prefab.GetComponent<CapsuleCollider>(), husk = enemy.GetComponent<CapsuleCollider>();
        Assert.That((capsule.radius, capsule.height, capsule.center, capsule.isTrigger), Is.EqualTo((husk.radius, husk.height, husk.center, husk.isTrigger)));

        CharacterController controller = prefab.GetComponent<CharacterController>(), huskController = enemy.GetComponent<CharacterController>();
        Assert.That((controller.radius, controller.height, controller.center), Is.EqualTo((huskController.radius, huskController.height, huskController.center)));

        SkinnedMeshRenderer skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue, Is.SameAs(skin), "The feedback drives the skinned body.");
        Assert.That(feedback.FindProperty("_dissolveMaterial").objectReferenceValue, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>(DissolvePath)));

        Assert.That(prefab.transform.Find("Mesh").gameObject.activeSelf, Is.False, "The capsule is switched off.");

        Animator animator = prefab.GetComponentInChildren<Animator>(true);
        Assert.That(animator.runtimeAnimatorController, Is.EqualTo(AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath)));
        Assert.That(animator.cullingMode, Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms));
        Assert.That(animator.applyRootMotion, Is.False, "Core owns velocity; no clip may move a body.");

        var view = prefab.GetComponentInChildren<EnemyAnimatorView>(true);
        Assert.That(view, Is.Not.Null);
        Assert.That(new SerializedObject(view).FindProperty("_animator").objectReferenceValue, Is.SameAs(animator));
    }

    [Test]
    public void Rootling_StandsAboutAMetreTall()
    {
        // The mesh at rest, crown included, at the model's scale in the prefab. Its crouched walk
        // carries it lower still. The skinned renderer's own bounds are padded, so the mesh is read.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        SkinnedMeshRenderer skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Transform model = prefab.transform.Find("Model");

        float height = skin.sharedMesh.bounds.max.y * model.localScale.y;

        Assert.That(height, Is.InRange(1.0f, 1.4f));
    }

    private void Walk(float speed) =>
        typeof(EnemyView).GetField("_velocity", Private).SetValue(_body, new Vector3(0f, 0f, speed));

    private static AnimatorState State(AnimatorStateMachine machine, string name)
    {
        AnimatorState state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

        Assert.That(state, Is.Not.Null, $"No state '{name}'.");

        return state;
    }

    private static float ViewField(GameObject prefab, string field) =>
        new SerializedObject(prefab.GetComponentInChildren<EnemyAnimatorView>(true)).FindProperty(field).floatValue;
}
