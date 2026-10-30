using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>JG-12e's seven rows, in spec order; core still owns every release.</summary>
[TestFixture]
public sealed class MotherPitcherBodyTests
{
    public const float BodyScale = 2.5f;

    private const string Root = "Assets/_Project/";
    private const string ModelPath = Root + "Art/Enemies/MotherPitcher.fbx";
    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void DestroyCreatedObjects()
    {
        foreach (Object item in _created)
        {
            if (item != null)
            {
                Object.DestroyImmediate(item);
            }
        }

        _created.Clear();
    }

    [Test]
    public void MotherBody_IsRootedAndDies()
    {
        AnimatorStateMachine body = Controller().layers[0].stateMachine;
        Assert.That(body.defaultState.name, Is.EqualTo("Idle"));
        Assert.That(body.defaultState.motion.name, Is.EqualTo("Idle"));
        Assert.That(States(body).Any(s => s.motion is BlendTree), Is.False);
        Assert.That(View().FindProperty("_hops").boolValue, Is.False);
        AnimatorState death = States(body).Single(s => s.name == "Death");
        Assert.That(death.motion.name, Is.EqualTo("Death"));
        AnimatorStateTransition transition = body.anyStateTransitions.Single(t => t.destinationState == death);
        Assert.That(transition.conditions.Single().parameter, Is.EqualTo("Dead"));
        Assert.That(transition.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.If));
        Assert.That(transition.hasExitTime, Is.False);
        Assert.That(transition.canTransitionToSelf, Is.False);

        Animator animator = Instance().GetComponentInChildren<Animator>();
        foreach (int move in new[] { 0, 1, 2, 3 })
        {
            animator.Rebind();
            animator.Update(0f);
            if (move != 0)
            {
                StartMove(animator, move);
            }

            animator.SetBool("Dead", true);
            animator.Update(0.01f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Death"), Is.True, $"Death from move {move}");
        }
    }

    [Test]
    public void MotherBody_AttackFollowsTheMove()
    {
        Animator animator = Instance().GetComponentInChildren<Animator>();
        Assert.That(Controller().parameters.Single(p => p.name == "Move").type, Is.EqualTo(AnimatorControllerParameterType.Int));
        foreach (int move in new[] { MotherPitcherBehaviour.BarrageMove, MotherPitcherBehaviour.SprayMove, MotherPitcherBehaviour.PodMove })
        {
            animator.Rebind();
            animator.Update(0f);
            StartMove(animator, move);
            string clip = move == MotherPitcherBehaviour.SprayMove ? "Spray" : "Barrage";
            Assert.That(animator.GetCurrentAnimatorClipInfo(0).Single().clip.name, Is.EqualTo(clip), $"Move {move}");
            AnimatorState state = Entry(move).destinationState;
            Assert.That(state.speedParameterActive && state.speedParameter == "AttackRate", Is.True);
            Assert.That(Entry(move).conditions.Any(c => c.parameter == "Dead" && c.mode == AnimatorConditionMode.IfNot), Is.True);
            Assert.That(state.transitions.Single().destinationState.name, Is.EqualTo("Idle"));
        }
    }

    [Test]
    public void MotherBody_BlowLandsAtTheWindupsEnd()
    {
        GameObject model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        _created.Add(model);
        Transform cup = model.GetComponentsInChildren<Transform>().Single(t => t.name == "cup");
        foreach (int move in new[] { 1, 2, 3 })
        {
            AnimatorStateTransition entry = Entry(move);
            AnimatorState state = entry.destinationState;
            var clip = (AnimationClip)state.motion;
            float maximum = float.MinValue, blow = -1f;
            // First maximum forward tip: Barrage's release, Spray's start of the held stream.
            for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * clip.frameRate); frame++)
            {
                float time = frame / clip.frameRate;
                clip.SampleAnimation(model, time);
                float tip = Mathf.DeltaAngle(0f, cup.eulerAngles.x);
                if (tip > maximum + 0.01f)
                {
                    maximum = tip;
                    blow = time;
                }
            }

            Assert.That(maximum, Is.GreaterThan(20f), "The probe must see a release pose.");
            float from = View().FindProperty("_windupFromSeconds").floatValue;
            float strike = View().FindProperty("_strikeSeconds").floatValue;
            float windup = move == 2 ? MotherPitcherBehaviour.SprayTelegraphSeconds :
                move == 3 ? MotherPitcherBehaviour.PodTelegraphSeconds : MotherPitcherBehaviour.BarrageTelegraphSeconds;
            Assert.That(entry.offset * clip.length, Is.EqualTo(from).Within(1e-4f));
            Assert.That(entry.duration, Is.Zero);
            Assert.That(entry.hasExitTime, Is.False);
            Assert.That(strike, Is.GreaterThan(from));
            float rate = (strike - from) / windup * state.speed;
            Assert.That((blow - from) / rate, Is.EqualTo(windup).Within(0.5f / clip.frameRate), $"Move {move}");
        }
    }

    [Test]
    public void MotherBody_EveryStateIsTheModelsAndNoWriteDefaults()
    {
        foreach (AnimatorState state in Controller().layers.SelectMany(l => States(l.stateMachine)))
        {
            Assert.That(state.writeDefaultValues, Is.False, state.name);
            if (state.motion != null)
            {
                Assert.That(state.motion, Is.TypeOf<AnimationClip>());
                Assert.That(AssetDatabase.GetAssetPath(state.motion), Is.EqualTo(ModelPath));
                Assert.That(AnimationUtility.GetAnimationEvents((AnimationClip)state.motion), Is.Empty);
            }
        }

        AnimatorControllerLayer layer = Controller().layers.Single(l => l.name == "Flinch");
        Assert.That(layer.defaultWeight, Is.Zero);
        Assert.That(States(layer.stateMachine).Single(s => s.name == "Hit").motion.name, Is.EqualTo("Hit"));
        Assert.That(States(Controller().layers[0].stateMachine).SelectMany(s => s.transitions)
            .Concat(Controller().layers[0].stateMachine.anyStateTransitions)
            .Any(t => t.conditions.Any(c => c.parameter == "Hit")), Is.False);

        GameObject instance = Instance();
        Animator animator = instance.GetComponentInChildren<Animator>();
        EnemyAnimatorView view = instance.GetComponentInChildren<EnemyAnimatorView>();
        foreach (int move in new[] { 1, 2, 3 })
        {
            animator.Rebind();
            animator.Update(0f);
            StartMove(animator, move);
            int attack = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            float before = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            animator.SetTrigger("Hit");
            animator.Update(0.05f);
            view.Step(0.05f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Hit"), Is.True);
            Assert.That(animator.GetLayerWeight(1), Is.EqualTo(1f));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).fullPathHash, Is.EqualTo(attack));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(before));
            animator.Update(0.5f);
            animator.Update(0f);
            view.Step(0.01f);
            Assert.That(animator.GetLayerWeight(1), Is.Zero, "A spent flinch returns the bones.");
        }
    }

    [Test]
    public void MotherBody_IsAWholeBody()
    {
        GameObject prefab = Prefab();
        Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Variant));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab),
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/Enemy.prefab")));
        Assert.That(prefab.transform.Find("Mesh").gameObject.activeSelf, Is.False);
        Animator animator = prefab.GetComponentInChildren<Animator>(true);
        Assert.That(animator.runtimeAnimatorController, Is.EqualTo(Controller()));
        Assert.That(animator.cullingMode, Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms));
        Assert.That(animator.applyRootMotion, Is.False);
        Assert.That(View().FindProperty("_animator").objectReferenceValue, Is.EqualTo(animator));
        SkinnedMeshRenderer skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Assert.That(AssetDatabase.GetAssetPath(skin.sharedMesh), Is.EqualTo(ModelPath));
        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue, Is.EqualTo(skin));
        Assert.That(feedback.FindProperty("_dissolveMaterial").objectReferenceValue,
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/Enemies/M_Enemy_Dissolve.mat")));
        Assert.That(feedback.FindProperty("_sporeTelegraph").boolValue, Is.False);
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.GreaterThan(0f));
        Assert.That(skin.sharedMaterial, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/Enemies/M_Enemy.mat")));
        Assert.That(skin.sharedMaterial.IsKeywordEnabled("_EMISSION"), Is.True);
        Assert.That(skin.sharedMaterial.globalIlluminationFlags & MaterialGlobalIlluminationFlags.RealtimeEmissive, Is.Not.Zero);
        Assert.That(prefab.GetComponentsInChildren<Component>(true).All(c => c != null), Is.True, "No missing scripts.");
    }

    [Test]
    public void MotherBody_IsAGiantWithItsBarAbove()
    {
        GameObject instance = Instance();
        SkinnedMeshRenderer skin = instance.GetComponentInChildren<SkinnedMeshRenderer>();
        var baked = new Mesh();
        _created.Add(baked);
        skin.BakeMesh(baked);
        instance.transform.localScale *= BodyScale;
        Vector3[] points = baked.vertices.Select(v => skin.transform.TransformPoint(v)).ToArray();
        float top = points.Max(v => v.y);
        Assert.That(top - points.Min(v => v.y), Is.InRange(3.5f, 4.5f));
        // EnemyHealthBar authors a local position; EnemyHitFeedback scales its parent on rental.
        float localBar = new SerializedObject(Prefab().GetComponent<EnemyHealthBar>()).FindProperty("_heightMetres").floatValue;
        float bar = instance.transform.TransformPoint(new Vector3(0f, localBar, 0f)).y;
        Assert.That(bar - top, Is.InRange(0.2f, 0.6f));
    }

    [Test]
    public void MotherBody_CannotBeStoodIn()
    {
        GameObject prefab = Prefab();
        CharacterController controller = prefab.GetComponent<CharacterController>();
        Assert.That(controller.enabled, Is.True);
        Assert.That(controller.radius, Is.InRange(1f, 1.6f));
        CapsuleCollider trigger = prefab.GetComponent<CapsuleCollider>();
        Assert.That(trigger.enabled && trigger.isTrigger, Is.True);
        Assert.That(trigger.direction, Is.EqualTo(1));
        // Test actual cup vertices against the capsule, excluding the broad ground leaves and lid.
        SkinnedMeshRenderer skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
        BoneWeight[] weights = skin.sharedMesh.boneWeights;
        Vector3[] vertices = skin.sharedMesh.vertices;
        int cup = System.Array.FindIndex(skin.bones, b => b.name == "cup");
        int tested = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (weights[i].boneIndex0 != cup)
            {
                continue;
            }

            Vector3 point = prefab.transform.InverseTransformPoint(skin.transform.TransformPoint(vertices[i]));
            Vector3 delta = point - trigger.center;
            float halfSegment = Mathf.Max(0f, trigger.height * 0.5f - trigger.radius);
            delta.y -= Mathf.Clamp(delta.y, -halfSegment, halfSegment);
            Assert.That(delta.magnitude, Is.LessThanOrEqualTo(trigger.radius + 0.02f), $"Cup vertex {i}");
            Assert.That(new Vector2(point.x - controller.center.x, point.z - controller.center.z).magnitude,
                Is.LessThanOrEqualTo(controller.radius), $"Solid body at cup vertex {i}");
            tested++;
        }

        Assert.That(tested, Is.GreaterThan(20), "The coverage probe must find the cup.");
    }

    private static GameObject Prefab() => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/MotherPitcher.prefab");

    private static AnimatorController Controller() => AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "Animation/Controllers/AC_MotherPitcher.controller");

    private static SerializedObject View() => new SerializedObject(Prefab().GetComponentInChildren<EnemyAnimatorView>(true));

    private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine) =>
        machine.states.Select(s => s.state).Concat(machine.stateMachines.SelectMany(s => States(s.stateMachine)));

    private static AnimatorStateTransition Entry(int move) => Controller().layers[0].stateMachine.anyStateTransitions
        .Single(t => t.conditions.Any(c => c.parameter == "Move" && (int)c.threshold == move));

    private GameObject Instance()
    {
        GameObject instance = Object.Instantiate(Prefab());
        _created.Add(instance);
        Animator animator = instance.GetComponentInChildren<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.Update(0f);
        return instance;
    }

    private static void StartMove(Animator animator, int move)
    {
        animator.SetFloat("AttackRate", 1f);
        animator.SetInteger("Move", move);
        animator.SetTrigger("Attack");
        animator.Update(0.01f);
    }
}
