using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>JG-14c's body rules, with the event responses pinned in the two view fixtures.</summary>
[TestFixture]
public sealed class MossguardBodyTests
{
    private const string Root = "Assets/_Project/";
    private const string Clips = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/";

    [Test]
    public void MossguardBody_LegsBlendIdleWalk()
    {
        AnimatorStateMachine body = Controller().layers[0].stateMachine;
        AnimatorState locomotion = body.defaultState;
        var tree = (BlendTree)locomotion.motion;
        Assert.That(tree.blendType, Is.EqualTo(BlendTreeType.Simple1D));
        Assert.That(tree.blendParameter, Is.EqualTo("Speed"));
        Assert.That(tree.children.Select(c => c.motion.name), Is.EqualTo(new[] { "Idle_B", "Walking_B" }));
        Assert.That(tree.children.Select(c => c.threshold), Is.EqualTo(new[] { 0f, ViewField("_strideSpeed") }));
        Assert.That(locomotion.speedParameterActive && locomotion.speedParameter == "WalkRate", Is.True);
        AnimatorState death = State(body, "Death");
        Assert.That(death.motion.name, Is.EqualTo("Death_A"));
        AnimatorStateTransition fall = body.anyStateTransitions.Single(t => t.destinationState == death);
        Assert.That(fall.conditions.Single().parameter, Is.EqualTo("Dead"));
        Assert.That(fall.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.If));
        Assert.That(fall.hasExitTime || fall.canTransitionToSelf, Is.False);
    }

    [Test]
    public void MossguardBody_GuardLayerHoldsTheShield()
    {
        AnimatorControllerLayer guard = Controller().layers[1];
        Assert.That(guard.name, Is.EqualTo("Guard"));
        Assert.That(guard.defaultWeight, Is.EqualTo(1f));
        Assert.That(guard.blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));
        Assert.That(guard.avatarMask, Is.EqualTo(AssetDatabase.LoadAssetAtPath<AvatarMask>(Root + "Animation/Masks/AM_UpperBody.mask")));
        var blocking = (AnimationClip)guard.stateMachine.defaultState.motion;
        Assert.That(blocking.name, Is.EqualTo("Melee_Blocking"));
        Assert.That(blocking.isLooping, Is.True);
        // Guard must fall with the base; a weighted idle would otherwise hold the corpse upright.
        AnimatorState death = State(guard.stateMachine, "Death");
        Assert.That(death.motion, Is.EqualTo(State(Controller().layers[0].stateMachine, "Death").motion));
        Assert.That(guard.stateMachine.anyStateTransitions.Single(t => t.destinationState == death)
            .conditions.Single().parameter, Is.EqualTo("Dead"));
    }

    [Test]
    public void MossguardBody_BashLandsAtTheWindupsEnd()
    {
        AnimatorStateMachine guard = Controller().layers[1].stateMachine;
        AnimatorState attack = State(guard, "Attack");
        var clip = (AnimationClip)attack.motion;
        Assert.That(clip.name, Is.EqualTo("Melee_Block_Attack"));
        Assert.That(attack.speedParameterActive && attack.speedParameter == "AttackRate", Is.True);
        AnimatorStateTransition entry = guard.anyStateTransitions.Single(t => t.destinationState == attack);
        Assert.That(entry.conditions.Any(c => c.parameter == "Attack" && c.mode == AnimatorConditionMode.If), Is.True);
        Assert.That(entry.conditions.Any(c => c.parameter == "Dead" && c.mode == AnimatorConditionMode.IfNot), Is.True);
        Assert.That(entry.hasExitTime, Is.False);
        Assert.That(entry.duration, Is.Zero);
        float from = ViewField("_windupFromSeconds"), strike = ViewField("_strikeSeconds");
        Assert.That(entry.offset * clip.length, Is.EqualTo(from).Within(1e-4f));
        Assert.That(strike, Is.GreaterThan(from));

        GameObject body = Object.Instantiate(Prefab());
        using var hub = new DomainEventHub();
        try
        {
            GameObject model = body.transform.Find("Model").gameObject;
            Transform slot = model.GetComponentsInChildren<Transform>().Single(t => t.name == "handslot.l");
            float maximum = float.MinValue, blow = -1f;
            for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * clip.frameRate); frame++)
            {
                float time = frame / clip.frameRate;
                clip.SampleAnimation(model, time);
                float forward = model.transform.InverseTransformPoint(slot.position).z;
                if (forward > maximum + 1e-4f)
                {
                    maximum = forward;
                    blow = time;
                }
            }

            Assert.That(maximum, Is.GreaterThan(1f), "The probe must see the bash extend.");
            EnemyAnimatorView view = model.GetComponent<EnemyAnimatorView>();
            view.Construct(hub);
            body.GetComponent<EnemyView>().Bind(11, Vector3.zero);
            Animator animator = model.GetComponent<Animator>();
            hub.Publish(new EnemyTelegraph(11, 0.7f));
            float rate = animator.GetFloat("AttackRate") * attack.speed;
            Assert.That((blow - from) / rate, Is.EqualTo(0.7f).Within(0.5f / clip.frameRate));
            Assert.That(attack.transitions.Single().destinationState, Is.EqualTo(guard.defaultState));
        }
        finally
        {
            Object.DestroyImmediate(body);
        }
    }

    [Test]
    public void MossguardBody_BlockPlaysBlockHit()
    {
        AnimatorStateMachine guard = Controller().layers[1].stateMachine;
        AnimatorState block = State(guard, "Block");
        Assert.That(block.motion.name, Is.EqualTo("Melee_Block_Hit"));
        AnimatorStateTransition entry = guard.anyStateTransitions.Single(t => t.destinationState == block);
        Assert.That(entry.conditions.Any(c => c.parameter == "Block" && c.mode == AnimatorConditionMode.If), Is.True);
        Assert.That(entry.conditions.Any(c => c.parameter == "Dead" && c.mode == AnimatorConditionMode.IfNot), Is.True);
        Assert.That(entry.hasExitTime, Is.False);
        Assert.That(entry.duration, Is.Zero);
        Assert.That(block.transitions.Single().hasExitTime, Is.True);
        Assert.That(block.transitions.Single().destinationState, Is.EqualTo(guard.defaultState));
    }

    [Test]
    public void MossguardBody_RigMediumClipsNoWriteDefaults()
    {
        AnimatorController controller = Controller();
        foreach (AnimatorState state in controller.layers.SelectMany(l => l.stateMachine.states).Select(s => s.state))
        {
            Assert.That(state.writeDefaultValues, Is.False, state.name);
        }

        foreach (AnimationClip clip in controller.animationClips)
        {
            Assert.That(AssetDatabase.GetAssetPath(clip), Does.StartWith(Clips), clip.name);
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty);
        }

        AnimatorControllerLayer flinch = controller.layers[2];
        Assert.That(flinch.name, Is.EqualTo("Flinch"));
        Assert.That(flinch.defaultWeight, Is.Zero);
        Assert.That(flinch.avatarMask, Is.EqualTo(controller.layers[1].avatarMask));
        Assert.That(flinch.stateMachine.defaultState.motion, Is.Null);
        AnimatorState hit = State(flinch.stateMachine, "Hit");
        Assert.That(hit.motion.name, Is.EqualTo("Hit_A"));
        Assert.That(flinch.stateMachine.anyStateTransitions.Single(t => t.destinationState == hit)
            .conditions.Any(c => c.parameter == "Hit" && c.mode == AnimatorConditionMode.If), Is.True);
        Assert.That(hit.transitions.Single().destinationState, Is.EqualTo(flinch.stateMachine.defaultState));
    }

    [Test]
    public void MossguardBody_IsAWholeBody()
    {
        GameObject prefab = Prefab();
        Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Variant));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab),
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/Enemy.prefab")));
        Assert.That(prefab.transform.Find("Mesh").gameObject.activeSelf, Is.False);
        GameObject model = prefab.transform.Find("Model").gameObject;
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(model),
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Art/Enemies/Mossguard.fbx")));
        Animator animator = model.GetComponent<Animator>();
        Assert.That(animator.runtimeAnimatorController, Is.EqualTo(Controller()));
        Assert.That(animator.cullingMode, Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms));
        Assert.That(animator.applyRootMotion, Is.False);
        var view = new SerializedObject(model.GetComponent<EnemyAnimatorView>());
        Assert.That(view.FindProperty("_animator").objectReferenceValue, Is.SameAs(animator));
        Assert.That(view.FindProperty("_hops").boolValue, Is.False);
        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue, Is.SameAs(model.GetComponentInChildren<SkinnedMeshRenderer>()));
        Assert.That(feedback.FindProperty("_dissolveMaterial").objectReferenceValue,
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/Enemies/M_Enemy_Dissolve.mat")));
        Assert.That(feedback.FindProperty("_sporeTelegraph").boolValue, Is.False);
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.GreaterThan(0f));
        Transform shield = model.GetComponentsInChildren<Transform>().Single(t => t.name == "Shield");
        Assert.That(shield.parent.name, Is.EqualTo("handslot.l"));
        Assert.That(shield.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(shield.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(shield.localScale, Is.EqualTo(Vector3.one));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(shield.gameObject),
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Art/Enemies/MossguardShield.fbx")));
    }

    [Test]
    public void MossguardBody_IsBiggerThanTheRanger()
    {
        Bounds body = BakedBounds(Prefab());
        Bounds ranger = BakedBounds(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Player/Bodies/Ranger.prefab"));
        Assert.That(body.size.y / ranger.size.y, Is.InRange(1.15f, 1.35f));
        float bar = new SerializedObject(Prefab().GetComponent<EnemyHealthBar>()).FindProperty("_heightMetres").floatValue;
        Assert.That(bar - body.max.y, Is.InRange(0.2f, 0.6f));
        Assert.That(Prefab().GetComponent<CharacterController>().radius, Is.InRange(0.45f, 0.6f));
    }

    [Test]
    public void MossguardBody_GlanceSitsOnTheShield()
    {
        var feedback = new SerializedObject(Prefab().GetComponent<EnemyHitFeedback>());
        var glance = (ParticleSystem)feedback.FindProperty("_glance").objectReferenceValue;
        Assert.That(glance, Is.Not.Null);
        Assert.That(glance.transform.parent.name, Is.EqualTo("Shield"));
        MeshFilter face = glance.transform.parent.GetComponentInChildren<MeshFilter>();
        Bounds bounds = face.sharedMesh.bounds;
        Vector3 point = face.transform.InverseTransformPoint(glance.transform.position);
        Assert.That(point.x, Is.EqualTo(bounds.center.x).Within(0.05f));
        Assert.That(point.y, Is.EqualTo(bounds.center.y).Within(0.05f));
        Assert.That(point.z, Is.InRange(bounds.max.z, bounds.max.z + 0.03f));
        Assert.That(glance.main.playOnAwake || glance.main.loop, Is.False);
        Assert.That(glance.main.startLifetime.constantMax, Is.LessThanOrEqualTo(0.25f));
        Assert.That(glance.emission.rateOverTime.constantMax, Is.Zero);
        Assert.That(glance.emission.burstCount, Is.EqualTo(1));
        ParticleSystem.Burst burst = glance.emission.GetBurst(0);
        Assert.That(burst.time, Is.Zero);
        Assert.That(burst.count.Evaluate(0f, 0f), Is.InRange(6f, 10f));
        Assert.That(burst.count.Evaluate(0f, 1f), Is.InRange(6f, 10f));
        Assert.That(glance.GetComponent<ParticleSystemRenderer>().sharedMaterial,
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/M_Impact.mat")));
    }

    private static Bounds BakedBounds(GameObject prefab)
    {
        GameObject instance = Object.Instantiate(prefab);
        var bounds = new Bounds();
        bool first = true;
        try
        {
            foreach (SkinnedMeshRenderer skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (skin.name == "Bow")
                {
                    continue;
                }

                var mesh = new Mesh();
                try
                {
                    skin.BakeMesh(mesh);
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = skin.transform.TransformPoint(vertex);
                        if (first)
                        {
                            bounds = new Bounds(point, Vector3.zero);
                            first = false;
                        }
                        else
                        {
                            bounds.Encapsulate(point);
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(mesh);
                }
            }

            Assert.That(first, Is.False, "The height probe must see body vertices.");
            return bounds;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static GameObject Prefab() => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/Mossguard.prefab");
    private static AnimatorController Controller() => AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "Animation/Controllers/AC_Mossguard.controller");
    private static AnimatorState State(AnimatorStateMachine machine, string name) => machine.states.Single(s => s.state.name == name).state;
    private static float ViewField(string field) => new SerializedObject(Prefab().GetComponentInChildren<EnemyAnimatorView>()).FindProperty(field).floatValue;
}
