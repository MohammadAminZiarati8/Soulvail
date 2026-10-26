using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// <c>AC_Panther</c>, <c>AM_Panther_Front</c> and <c>Panther.prefab</c> (JG-07c): the panther as a whole
/// enemy body, driven by the same <see cref="EnemyAnimatorView"/> as the Puffball.
/// </summary>
/// <remarks>
/// <see cref="PuffballBodyTests"/>' rows for a Lunger. The dash is locomotion, not an attack: the view
/// reads it off the body's velocity, so the blend runs on past the prowl into the bound. The wind-up is
/// the attack, a crouch held to its end; the flinch is the front, so a hit mid-prowl leaves the legs.
/// </remarks>
[TestFixture]
public sealed class PantherBodyTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Panther.prefab";
    private const string EnemyPrefabPath = "Assets/_Project/Prefabs/Enemies/Enemy.prefab";
    private const string ControllerPath = "Assets/_Project/Animation/Controllers/AC_Panther.controller";
    private const string MaskPath = "Assets/_Project/Animation/Masks/AM_Panther_Front.mask";
    private const string DissolvePath = "Assets/_Project/Materials/Enemies/M_Enemy_Dissolve.mat";
    private const string ModelPath = "Assets/_Project/Art/Enemies/Panther.fbx";

    /// <summary>One frame of the Panther's clips, which are authored at 30 fps.</summary>
    private const float Frame = 1f / 30f;

    /// <summary>GD §8.1's Lunger wind-up, which the crouch is held across.</summary>
    private const float Windup = 0.9f;

    /// <summary>Where the blend is all bound: a dash at 24 m/s lands past it.</summary>
    private const float LungeSpeed = 6f;

    /// <summary>
    /// Nose to rump, from the model rather than the spec's 1.6–2.0 m, which was written before it: a
    /// scale reaching 1.6 m puts the shoulder past 0.95 m (JG-07b's finding, JG-07c As built).
    /// </summary>
    private const float MinLength = 1.4f;

    private const float MaxLength = 1.7f;

    private const float MinShoulder = 0.7f;

    private const float MaxShoulder = 0.95f;

    /// <summary>
    /// The slice of back the shoulder is read from, in metres behind the front legs' top bone toward
    /// the rump: the withers. Directly over the bone is the back of the head, which hangs over it.
    /// </summary>
    private const float WithersFrom = 0.05f;

    private const float WithersTo = 0.2f;

    /// <summary>A lane's half-width at JG-07a: the body may be no wider than the strip it warned of.</summary>
    private const float MaxRadius = 0.45f;

    private static readonly string[] FrontBones = { "chest", "neck", "head", "jaw" };

    [Test]
    public void PantherBody_LocomotionBlendsIdleProwlLunge()
    {
        AnimatorController controller = Controller();
        AnimatorStateMachine body = controller.layers[0].stateMachine;
        AnimatorState locomotion = State(body, "Locomotion");

        Assert.That(controller.parameters.Select(p => p.name),
            Is.EquivalentTo(new[] { "Speed", "WalkRate", "AttackRate", "Attack", "Hit", "Dead" }),
            "The six parameters the view writes, and no others.");
        Assert.That(body.defaultState, Is.SameAs(locomotion));
        Assert.That(locomotion.speedParameterActive && locomotion.speedParameter == "WalkRate", Is.True);

        var tree = (BlendTree)locomotion.motion;

        Assert.That(tree.blendParameter, Is.EqualTo("Speed"));
        Assert.That(tree.children.Select(c => c.motion.name), Is.EqualTo(new[] { "Idle", "Prowl", "Lunge" }));
        Assert.That(tree.children.Select(c => c.threshold),
            Is.EqualTo(new[] { 0f, ViewField("_strideSpeed"), LungeSpeed }).Within(1e-4f),
            "At the stride speed the view plays the prowl at 1x; the dash runs past the bound's point.");

        AnimatorState death = State(body, "Death");
        Assert.That(death.motion.name, Is.EqualTo("Death"));
        Assert.That(body.anyStateTransitions.Single(t => t.destinationState == death).conditions.Single().parameter, Is.EqualTo("Dead"));
    }

    [Test]
    public void PantherBody_CrouchHoldsToTheWindupsEnd()
    {
        // The view brings _strikeSeconds of the clip to the end of core's wind-up, which is when the
        // dash leaves: the crouch is held all the way, its last frame the spring.
        AnimatorStateMachine body = Controller().layers[0].stateMachine;
        AnimatorState attack = State(body, "Attack");
        var crouch = (AnimationClip)attack.motion;

        Assert.That(crouch.name, Is.EqualTo("Crouch"));
        Assert.That(attack.speedParameterActive && attack.speedParameter == "AttackRate", Is.True);
        Assert.That(ViewField("_strikeSeconds"), Is.EqualTo(Windup).Within(Frame * 0.5f));
        Assert.That(ViewField("_strikeSeconds"), Is.EqualTo(crouch.length).Within(Frame * 0.5f), "The strike is the crouch's end.");
        Assert.That(ViewField("_windupFromSeconds"), Is.LessThan(ViewField("_strikeSeconds")));

        AnimatorStateTransition toAttack = body.anyStateTransitions.Single(t => t.destinationState == attack);
        Assert.That(toAttack.offset * crouch.length, Is.EqualTo(ViewField("_windupFromSeconds")).Within(1e-3f));
    }

    [Test]
    public void PantherBody_FlinchIsTheFront()
    {
        AnimatorControllerLayer flinch = Controller().layers[1];
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);

        Assert.That(flinch.avatarMask, Is.EqualTo(mask));
        Assert.That(State(flinch.stateMachine, "Hit").motion.name, Is.EqualTo("Hit"));

        var active = new List<string>();

        for (int i = 0; i < mask.transformCount; i++)
        {
            if (mask.GetTransformActive(i))
            {
                active.Add(mask.GetTransformPath(i).Split('/').Last());
            }
        }

        Assert.That(active, Is.EquivalentTo(FrontBones), "Neck, head, jaw and chest; never the root, hips, legs or tail.");
    }

    [Test]
    public void PantherBody_EveryStateIsTheModelsAndNoWriteDefaults()
    {
        AnimatorController controller = Controller();

        foreach (AnimatorState state in controller.layers.SelectMany(l => l.stateMachine.states).Select(s => s.state))
        {
            Assert.That(state.writeDefaultValues, Is.False,
                $"{state.name} writes defaults, which snaps the masked bones to the bind pose from an empty flinch state.");
        }

        foreach (AnimationClip clip in controller.animationClips)
        {
            Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo(ModelPath), $"{clip.name} is not one of the Panther's clips.");
        }
    }

    [Test]
    public void PantherBody_IsAWholeBody()
    {
        GameObject prefab = Prefab();
        var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);

        Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Variant));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab), Is.EqualTo(enemy),
            "A variant of the shared body, so its colliders, feedback and health bar are the Husk's.");

        CapsuleCollider capsule = prefab.GetComponent<CapsuleCollider>(), husk = enemy.GetComponent<CapsuleCollider>();
        Assert.That((capsule.radius, capsule.height, capsule.center, capsule.isTrigger), Is.EqualTo((husk.radius, husk.height, husk.center, husk.isTrigger)));

        CharacterController controller = prefab.GetComponent<CharacterController>(), huskController = enemy.GetComponent<CharacterController>();
        Assert.That((controller.radius, controller.height, controller.center), Is.EqualTo((huskController.radius, huskController.height, huskController.center)));

        Assert.That(prefab.transform.Find("Mesh").gameObject.activeSelf, Is.False, "The capsule is switched off.");

        SkinnedMeshRenderer skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab.transform.Find("Model").gameObject),
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)));

        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue, Is.SameAs(skin), "The feedback drives the skinned body.");
        Assert.That(feedback.FindProperty("_dissolveMaterial").objectReferenceValue, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>(DissolvePath)));
        Assert.That(feedback.FindProperty("_sporeTelegraph").boolValue, Is.False, "Its wind-up glows danger.");

        Animator animator = prefab.GetComponentInChildren<Animator>(true);
        Assert.That(animator.runtimeAnimatorController, Is.EqualTo(AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath)));
        Assert.That(animator.cullingMode, Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms));
        Assert.That(animator.applyRootMotion, Is.False, "Core owns velocity; no clip may move a body.");

        var view = prefab.GetComponentInChildren<EnemyAnimatorView>(true);
        Assert.That(view, Is.Not.Null, "A null component here is Traps §5's block-namespace trap.");
        Assert.That(new SerializedObject(view).FindProperty("_animator").objectReferenceValue, Is.SameAs(animator));
        Assert.That(new SerializedObject(view).FindProperty("_hops").boolValue, Is.False);
    }

    [Test]
    public void PantherBody_IsABigCatWithItsBarAbove()
    {
        // Measured on the skinned mesh at rest, in its own space (Y up, Z forward), at the model's
        // scale in the prefab. The rump is where the tail leaves the body, so a straightened tail does
        // not count toward the length; the shoulder is the top of the withers, just behind the head.
        SkinnedMeshRenderer skin = Prefab().GetComponentInChildren<SkinnedMeshRenderer>(true);
        Mesh mesh = skin.sharedMesh;
        float scale = Scale();

        Transform BoneNamed(string name) => skin.bones.Single(b => b.name == name);
        Vector3 Local(string bone) => skin.transform.InverseTransformPoint(BoneNamed(bone).position);

        float rump = Local("tail_1").z;
        float nose = Mathf.Abs(mesh.bounds.max.z - rump) > Mathf.Abs(mesh.bounds.min.z - rump) ? mesh.bounds.max.z : mesh.bounds.min.z;
        float length = Mathf.Abs(nose - rump) * scale;

        float shoulderZ = Local("upper.front.l").z;
        float back = Mathf.Sign(rump - shoulderZ);
        float shoulder = mesh.vertices
            .Where(v => (v.z - shoulderZ) * back >= WithersFrom && (v.z - shoulderZ) * back <= WithersTo)
            .Max(v => v.y) * scale;

        Assert.That(length, Is.InRange(MinLength, MaxLength), $"Nose to rump is {length:F2} m.");
        Assert.That(shoulder, Is.InRange(MinShoulder, MaxShoulder), $"The back at the shoulder is {shoulder:F2} m.");

        float bar = new SerializedObject(Prefab().GetComponent<EnemyHealthBar>()).FindProperty("_heightMetres").floatValue;
        float top = mesh.bounds.max.y * scale;

        Assert.That(bar - top, Is.InRange(0.2f, 0.6f), $"The bar is {bar} m up over a {top:F2} m cat.");
    }

    [Test]
    public void PantherBody_FitsItsLane()
    {
        Assert.That(Prefab().GetComponent<CharacterController>().radius, Is.LessThanOrEqualTo(MaxRadius),
            "A 1.2 m lane holds the body that dashes down it.");
    }

    private static GameObject Prefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, PrefabPath);

        return prefab;
    }

    private static AnimatorController Controller()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        Assert.That(controller, Is.Not.Null, ControllerPath);

        return controller;
    }

    private static float Scale() => Prefab().transform.Find("Model").localScale.y;

    private static AnimatorState State(AnimatorStateMachine machine, string name)
    {
        AnimatorState state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

        Assert.That(state, Is.Not.Null, $"No state '{name}'.");

        return state;
    }

    private static float ViewField(string field) =>
        new SerializedObject(Prefab().GetComponentInChildren<EnemyAnimatorView>(true)).FindProperty(field).floatValue;
}
