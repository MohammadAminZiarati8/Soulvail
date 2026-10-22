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

/// <summary>
/// <c>AC_Puffball</c>, <c>Puffball.prefab</c> and <c>VFX_SporeBurst</c> (JG-03b): the puffball mushroom
/// as a whole enemy body, driven by the same <see cref="EnemyAnimatorView"/> as the Pitcher, glowing
/// cyan-white over its wind-up and bursting into spores when it dies.
/// </summary>
/// <remarks>
/// <see cref="PitcherBodyTests"/>' rows for a third body. A Bloater publishes the same
/// <c>EnemyTelegraph</c> a Husk does and kills itself on the tick its wind-up ends, so the strike is the
/// frame the cap is largest, measured in the clip. One row is the Puffball's alone: its burst rides the
/// corpse, so it has to be over before <see cref="EnemySystem.CorpseTime"/> returns the body to the pool.
/// </remarks>
[TestFixture]
public sealed class PuffballBodyTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Puffball.prefab";
    private const string EnemyPrefabPath = "Assets/_Project/Prefabs/Enemies/Enemy.prefab";
    private const string ControllerPath = "Assets/_Project/Animation/Controllers/AC_Puffball.controller";
    private const string MaskPath = "Assets/_Project/Animation/Masks/AM_Puffball_Cap.mask";
    private const string DissolvePath = "Assets/_Project/Materials/Enemies/M_Enemy_Dissolve.mat";
    private const string ModelPath = "Assets/_Project/Art/Enemies/Puffball.fbx";
    private const string BurstPath = "Assets/_Project/Prefabs/Vfx/VFX_SporeBurst.prefab";

    /// <summary>One frame of the Puffball's clips, which are authored at 30 fps.</summary>
    private const float Frame = 1f / 30f;

    /// <summary>How close in any one channel a spore may come to a reserved colour: the atlas's bar.</summary>
    private const float ReservedDistance = 0.12f;

    private static readonly Color[] Reserved =
    {
        new Color32(0x22, 0xD3, 0xEE, 0xFF), // the player
        new Color32(0xFF, 0x4A, 0x1F, 0xFF), // danger
        new Color32(0xA8, 0x55, 0xF7, 0xFF), // Veilrot
        new Color32(0xFB, 0xBF, 0x24, 0xFF), // reward
    };

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
    public void Puffball_ControllerNamesItsStates()
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
        Assert.That(tree.children.Select(c => c.motion.name), Is.EqualTo(new[] { "Idle", "Waddle" }));
        Assert.That(attack.motion.name, Is.EqualTo("Swell"));
        Assert.That(death.motion.name, Is.EqualTo("Burst"));

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
            Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo(ModelPath), $"{clip.name} is not one of the Puffball's clips.");
        }
    }

    [Test]
    public void Puffball_FlinchLeavesTheFootWaddling()
    {
        // A hit that does not kill squashes the cap. The body and the stalk stay on the base layer, so
        // a waddling Puffball keeps waddling.
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        var active = new Dictionary<string, bool>();

        for (int i = 0; i < mask.transformCount; i++)
        {
            active[mask.GetTransformPath(i).Split('/').Last()] = mask.GetTransformActive(i);
        }

        Assert.That(active["cap"], Is.True);
        Assert.That(active["root"] || active["body"] || active["stalk"], Is.False);
    }

    [Test]
    public void Puffball_IsAWholeBody()
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
        Assert.That(feedback.FindProperty("_dissolveStretch").floatValue, Is.EqualTo(1f), "A burst corpse collapses; it does not stretch.");
        Assert.That(feedback.FindProperty("_sporeTelegraph").boolValue, Is.True, "Its wind-up glows cyan-white (the owner, 2026-10-22).");

        var burst = (ParticleSystem)feedback.FindProperty("_deathBurst").objectReferenceValue;
        Assert.That(burst, Is.Not.Null, "It bursts into spores when it dies.");
        Assert.That(burst.transform.IsChildOf(prefab.transform), Is.True);
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(burst.gameObject),
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(BurstPath)));

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
    public void Puffball_IsFat()
    {
        // The mesh at rest at the model's scale in the prefab: taller than the Ranger's 1.5 m would
        // crowd the room; a body narrower than the Frog's would not read as a Bloater.
        Bounds bounds = Mesh().bounds;
        float scale = Scale();

        Assert.That(bounds.max.y * scale, Is.InRange(1.2f, 1.5f));
        Assert.That(bounds.size.x * scale, Is.GreaterThanOrEqualTo(1.15f));
    }

    [Test]
    public void Puffball_HealthBarFloatsJustOverItsCap()
    {
        // EnemyHealthBar places the bar at _heightMetres when it shows (M7-05l As built).
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        float bar = new SerializedObject(prefab.GetComponent<EnemyHealthBar>()).FindProperty("_heightMetres").floatValue;
        float height = Mesh().bounds.max.y * Scale();

        Assert.That(bar - height, Is.InRange(0.2f, 0.6f), $"The bar is {bar} m up over a {height:F2} m mushroom.");
    }

    [Test]
    public void Puffball_StrideIsTheWaddlesPointInTheBlend()
    {
        // At the stride speed the view plays the walk at 1x, so that is where the blend must be all
        // Waddle: below it the rocking shrinks toward idle, above it the rate climbs.
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var tree = (BlendTree)State(controller.layers[0].stateMachine, "Locomotion").motion;

        Assert.That(tree.children.Single(c => c.motion.name == "Waddle").threshold, Is.EqualTo(ViewField("_strideSpeed")).Within(1e-4f));
        Assert.That(tree.children.Single(c => c.motion.name == "Idle").threshold, Is.Zero);
    }

    [Test]
    public void Puffball_StrikeIsTheSwellsPeak()
    {
        // The view brings _strikeSeconds of the clip to the end of core's wind-up, the tick the Bloater
        // detonates, so that has to be the frame the cap is largest: the swell on screen and the blast
        // are one moment (GD §9.1 rule 1). Sampled at every frame of the clip.
        var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        model.hideFlags = HideFlags.HideAndDontSave;
        _created.Add(model);

        Transform cap = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "cap");
        AnimationClip swell = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Single(c => c.name == "Swell");

        float largest = float.MinValue, at = 0f;

        for (float t = 0f; t <= swell.length + 1e-4f; t += Frame)
        {
            swell.SampleAnimation(model, t);

            if (cap.lossyScale.x > largest)
            {
                largest = cap.lossyScale.x;
                at = t;
            }
        }

        Assert.That(ViewField("_strikeSeconds"), Is.EqualTo(at).Within(Frame * 0.5f),
            $"The cap is largest at {at:F3} s.");
        Assert.That(ViewField("_windupFromSeconds"), Is.LessThan(at), "The Attack state is entered before the peak.");
    }

    [Test]
    public void Puffball_SporeBurstIsAOneShotPuff()
    {
        var burst = AssetDatabase.LoadAssetAtPath<GameObject>(BurstPath).GetComponent<ParticleSystem>();
        ParticleSystem.MainModule main = burst.main;
        ParticleSystem.EmissionModule emission = burst.emission;

        Assert.That(main.loop, Is.False);
        Assert.That(main.playOnAwake, Is.False, "It plays on the body's death, not when the pool builds the body.");
        Assert.That(main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World), "Spores stay where they burst.");
        Assert.That(main.scalingMode, Is.EqualTo(ParticleSystemScalingMode.Shape),
            "The body's swell and scale move where spores start, not how big they are.");

        Assert.That(emission.rateOverTime.constantMax, Is.Zero);
        Assert.That(emission.rateOverDistance.constantMax, Is.Zero);
        Assert.That(emission.burstCount, Is.EqualTo(1));

        ParticleSystem.Burst puff = emission.GetBurst(0);
        Assert.That(puff.time + main.startLifetime.constantMax, Is.LessThanOrEqualTo(EnemySystem.CorpseTime),
            "Every spore is gone before the corpse goes back to the pool, which would cut it off.");

        var colours = new List<Color> { main.startColor.colorMin, main.startColor.colorMax };
        colours.AddRange(burst.colorOverLifetime.color.gradient.colorKeys.Select(k => k.color));

        foreach (Color colour in colours)
        {
            foreach (Color reserved in Reserved)
            {
                float nearest = Mathf.Max(Mathf.Abs(colour.r - reserved.r), Mathf.Abs(colour.g - reserved.g), Mathf.Abs(colour.b - reserved.b));

                Assert.That(nearest, Is.GreaterThan(ReservedDistance), $"A spore colour {colour} comes near {reserved}, which GD §16.4 reserves.");
            }
        }
    }

    private static Mesh Mesh() =>
        AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;

    private static float Scale() =>
        AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).transform.Find("Model").localScale.y;

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
