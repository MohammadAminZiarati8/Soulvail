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
/// <c>AC_Pitcher</c> and <c>Pitcher.prefab</c> (M7-05p): the pitcher plant as a whole enemy body,
/// driven by the same <see cref="EnemyAnimatorView"/> as the Frog, and wired the way that view's rules
/// assume.
/// </summary>
/// <remarks>
/// <see cref="FrogBodyTests"/>' rows for a second body. A Spitter publishes the same
/// <c>EnemyTelegraph</c> a Husk does and fires on the tick its wind-up ends, so the view needs nothing
/// new: the strike is the frame the mouth snaps furthest forward, measured in the clip, and a
/// re-authored spit that moves it without moving the prefab is a red row, not a glob that leaves early.
/// </remarks>
[TestFixture]
public sealed class PitcherBodyTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Pitcher.prefab";
    private const string EnemyPrefabPath = "Assets/_Project/Prefabs/Enemies/Enemy.prefab";
    private const string ControllerPath = "Assets/_Project/Animation/Controllers/AC_Pitcher.controller";
    private const string MaskPath = "Assets/_Project/Animation/Masks/AM_Pitcher_UpperBody.mask";
    private const string DissolvePath = "Assets/_Project/Materials/Enemies/M_Enemy_Dissolve.mat";
    private const string ModelPath = "Assets/_Project/Art/Enemies/Pitcher.fbx";

    /// <summary>One frame of the Pitcher's clips, which are authored at 30 fps.</summary>
    private const float Frame = 1f / 30f;

    private static readonly string[] Roots = { "thigh.l", "shin.l", "foot.l", "thigh.r", "shin.r", "foot.r" };

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
    public void Pitcher_ControllerNamesItsStates()
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
        Assert.That(tree.children.Select(c => c.motion.name), Is.EqualTo(new[] { "Idle", "Shuffle" }));
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
            Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo(ModelPath), $"{clip.name} is not one of the Pitcher's clips.");
        }
    }

    [Test]
    public void Pitcher_FlinchLeavesTheRootsWalking()
    {
        // A hit that does not kill flinches the jug and everything it carries. The hips and the root
        // legs stay on the base layer, so a shuffling Pitcher keeps shuffling.
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        var active = new Dictionary<string, bool>();

        for (int i = 0; i < mask.transformCount; i++)
        {
            active[mask.GetTransformPath(i).Split('/').Last()] = mask.GetTransformActive(i);
        }

        Assert.That(active["jug"] && active["mouth"] && active["lid"] && active["leaf.l"] && active["leaf.r"], Is.True);
        Assert.That(active["body"] || active["root"] || Roots.Any(b => active[b]), Is.False);
    }

    [Test]
    public void Pitcher_IsAWholeBody()
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
    public void Pitcher_StandsAboutAsTallAsThePlayer()
    {
        // The mesh at rest, lid included, at the model's scale in the prefab: the Ranger stands 1.5 m.
        // The skinned renderer's own bounds are padded, so the mesh is read.
        Assert.That(Height(), Is.InRange(1.3f, 1.7f));
    }

    [Test]
    public void Pitcher_HealthBarFloatsJustOverItsLid()
    {
        // EnemyHealthBar places the bar at _heightMetres when it shows (M7-05l As built).
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        float bar = new SerializedObject(prefab.GetComponent<EnemyHealthBar>()).FindProperty("_heightMetres").floatValue;

        Assert.That(bar - Height(), Is.InRange(0.2f, 0.6f), $"The bar is {bar} m up over a {Height():F2} m plant.");
    }

    [Test]
    public void Pitcher_StrideIsTheShufflesPointInTheBlend()
    {
        // At the stride speed the view plays the walk at 1x, so that is where the blend must be all
        // Shuffle: below it the steps shrink toward idle, above it the rate climbs.
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var tree = (BlendTree)State(controller.layers[0].stateMachine, "Locomotion").motion;

        Assert.That(tree.children.Single(c => c.motion.name == "Shuffle").threshold, Is.EqualTo(ViewField("_strideSpeed")).Within(1e-4f));
        Assert.That(tree.children.Single(c => c.motion.name == "Idle").threshold, Is.Zero);
    }

    [Test]
    public void Pitcher_StrikeIsTheSpitsSnap()
    {
        // The view brings _strikeSeconds of the clip to the end of core's wind-up, the tick the Spitter
        // releases its shot, so that has to be the frame the mouth is furthest forward: the spit on
        // screen and the shot are one moment (GD §9.1 rule 1). Sampled at every frame of the clip.
        var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        model.hideFlags = HideFlags.HideAndDontSave;
        _created.Add(model);

        Transform mouth = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "mouth");
        AnimationClip attack = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Single(c => c.name == "Attack");

        float furthest = float.MinValue, at = 0f;

        for (float t = 0f; t <= attack.length + 1e-4f; t += Frame)
        {
            attack.SampleAnimation(model, t);

            if (mouth.position.z > furthest)
            {
                furthest = mouth.position.z;
                at = t;
            }
        }

        Assert.That(ViewField("_strikeSeconds"), Is.EqualTo(at).Within(Frame * 0.5f),
            $"The mouth is furthest forward at {at:F3} s.");
        Assert.That(ViewField("_windupFromSeconds"), Is.LessThan(at), "The Attack state is entered after the spit.");
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
