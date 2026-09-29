using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// <c>AC_Frog</c> and <c>Frog.prefab</c> (M7-05l): the Frog as a whole enemy body, driven by the same
/// <see cref="EnemyAnimatorView"/> as the Rootling, and wired the way that view's rules assume.
/// </summary>
/// <remarks>
/// The view's own rules are <c>EnemyAnimatorViewTests</c>'. What is the Frog's alone is where its
/// numbers come from: the stride is the Hop's point in the blend, and the strike is the frame the
/// tongue reaches furthest, measured in the clip rather than read off a comment, so a re-authored
/// clip that moves the lash without moving the prefab is a red row, not a tongue that lands late.
/// </remarks>
[TestFixture]
public sealed class FrogBodyTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Frog.prefab";
    private const string EnemyPrefabPath = "Assets/_Project/Prefabs/Enemies/Enemy.prefab";
    private const string ControllerPath = "Assets/_Project/Animation/Controllers/AC_Frog.controller";
    private const string MaskPath = "Assets/_Project/Animation/Masks/AM_Frog_UpperBody.mask";
    private const string DissolvePath = "Assets/_Project/Materials/Enemies/M_Enemy_Dissolve.mat";
    private const string ModelPath = "Assets/_Project/Art/Enemies/Frog.fbx";

    /// <summary>One frame of the Frog's clips, which are authored at 30 fps.</summary>
    private const float Frame = 1f / 30f;

    private static readonly string[] HindLegs = { "thigh.l", "shin.l", "foot.l", "thigh.r", "shin.r", "foot.r" };

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void DestroyCreatedObjects()
    {
        foreach (Object created in _created)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        _created.Clear();
    }

    [Test]
    public void Frog_ControllerNamesItsStates()
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
        Assert.That(tree.children.Select(c => c.motion.name), Is.EqualTo(new[] { "Idle", "Hop" }));
        Assert.That(attack.motion.name, Is.EqualTo("Attack"));
        Assert.That(death.motion.name, Is.EqualTo("Death"));

        // The offset the Attack state is entered at is the clip time the view's rate counts from.
        AnimatorStateTransition toAttack = body.anyStateTransitions.Single(t => t.destinationState == attack);
        Assert.That(toAttack.offset * ((AnimationClip)attack.motion).length, Is.EqualTo(ViewField("_windupFromSeconds")).Within(1e-3f));
        Assert.That(body.anyStateTransitions.Single(t => t.destinationState == death).conditions.Single().parameter, Is.EqualTo("Dead"));

        AnimatorControllerLayer flinch = controller.layers[1];
        Assert.That(flinch.avatarMask, Is.EqualTo(AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath)));
        Assert.That(State(flinch.stateMachine, "Hit").motion.name, Is.EqualTo("Hit"));

        foreach (AnimatorState state in controller.layers.SelectMany(l => l.stateMachine.states).Select(s => s.state))
        {
            Assert.That(state.writeDefaultValues, Is.False,
                $"{state.name} writes defaults, which snaps the masked bones to the bind pose from an empty flinch state.");
        }

        foreach (AnimationClip clip in controller.animationClips)
        {
            Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo(ModelPath), $"{clip.name} is not one of the Frog's clips.");
        }
    }

    [Test]
    public void Frog_FlinchLeavesTheHindLegsHopping()
    {
        // A hit that does not kill flinches the spine up and the forelegs (M7-05h rule 3). The hind
        // legs and the body they hang from stay on the base layer, so a hopping Frog keeps hopping.
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        var active = new Dictionary<string, bool>();

        for (int i = 0; i < mask.transformCount; i++)
        {
            active[mask.GetTransformPath(i).Split('/').Last()] = mask.GetTransformActive(i);
        }

        Assert.That(active["head"] && active["spine"] && active["upperarm.l"] && active["upperarm.r"], Is.True);
        Assert.That(active["body"] || active["root"] || HindLegs.Any(b => active[b]), Is.False);
    }

    [Test]
    public void Frog_IsAWholeBody()
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
    public void Frog_StandsAboutAMetreTall()
    {
        // The mesh at rest, eyes included, at the model's scale in the prefab. The skinned renderer's
        // own bounds are padded, so the mesh is read.
        Assert.That(Height(), Is.InRange(0.9f, 1.2f));
    }

    [Test]
    public void Frog_HealthBarFloatsJustOverItsHead()
    {
        // EnemyHealthBar places the bar at _heightMetres when it shows; the shared body's 2.1 m would
        // leave it a metre over a frog.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        float bar = new SerializedObject(prefab.GetComponent<EnemyHealthBar>()).FindProperty("_heightMetres").floatValue;

        Assert.That(bar - Height(), Is.InRange(0.2f, 0.6f), $"The bar is {bar} m up over a {Height():F2} m frog.");
    }

    [Test]
    public void Frog_StrideIsTheHopsPointInTheBlend()
    {
        // At the stride speed the view plays the walk at 1x, so that is where the blend must be all
        // Hop: below it the hop shrinks toward idle, above it the rate climbs.
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var tree = (BlendTree)State(controller.layers[0].stateMachine, "Locomotion").motion;

        Assert.That(tree.children.Single(c => c.motion.name == "Hop").threshold, Is.EqualTo(ViewField("_strideSpeed")).Within(1e-4f));
        Assert.That(tree.children.Single(c => c.motion.name == "Idle").threshold, Is.Zero);
    }

    [Test]
    public void Frog_StrikeIsTheTonguesFullestReach()
    {
        // The view brings _strikeSeconds of the clip to the end of core's wind-up (M7-05h rule 2), so
        // that has to be the frame the tongue is furthest out: the blow on screen and the damage are
        // one moment (GD §9.1 rule 1). Sampled at every frame of the clip.
        var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        model.hideFlags = HideFlags.HideAndDontSave;
        _created.Add(model);

        Transform tip = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "tongue.tip");
        AnimationClip attack = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Single(c => c.name == "Attack");

        float furthest = float.MinValue, at = 0f;

        for (float t = 0f; t <= attack.length + 1e-4f; t += Frame)
        {
            attack.SampleAnimation(model, t);

            if (tip.position.z > furthest)
            {
                furthest = tip.position.z;
                at = t;
            }
        }

        Assert.That(ViewField("_strikeSeconds"), Is.EqualTo(at).Within(Frame * 0.5f),
            $"The tongue is furthest out at {at:F3} s.");
        Assert.That(ViewField("_windupFromSeconds"), Is.LessThan(at), "The Attack state is entered after the lash.");
    }

    private static float Height()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        SkinnedMeshRenderer skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);

        return skin.sharedMesh.bounds.max.y * prefab.transform.Find("Model").localScale.y;
    }

    private static AnimatorState State(AnimatorStateMachine machine, string name)
    {
        AnimatorState state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

        Assert.That(state, Is.Not.Null, $"No state '{name}'.");

        return state;
    }

    private static float ViewField(string field) =>
        new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponentInChildren<EnemyAnimatorView>(true))
            .FindProperty(field).floatValue;
}
