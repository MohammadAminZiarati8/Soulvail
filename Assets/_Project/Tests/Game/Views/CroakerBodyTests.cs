using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// <c>AC_Croaker</c> and <c>Croaker.prefab</c> (JG-04d): Old Croaker as a whole boss body, the Frog's
/// controller with the boss's clips and an Attack sub-state picked by <c>Move</c>.
/// </summary>
/// <remarks>
/// Core's telegraphs for this boss are constants matched to <c>Croaker.fbx</c> at speed 1
/// (<see cref="CroakerBehaviour.SlamTelegraphSeconds"/>), so each Attack state plays its clip at 1x
/// from its first frame rather than at the view's <c>AttackRate</c>: one rate cannot bring three
/// clips' blows to three wind-ups. The impact and the reach are measured in the clips, as the Frog's
/// lash is, so a re-authored clip that moves either is a red row.
/// </remarks>
[TestFixture]
public sealed class CroakerBodyTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Croaker.prefab";
    private const string EnemyPrefabPath = "Assets/_Project/Prefabs/Enemies/Enemy.prefab";
    private const string ControllerPath = "Assets/_Project/Animation/Controllers/AC_Croaker.controller";
    private const string MaskPath = "Assets/_Project/Animation/Masks/AM_Frog_UpperBody.mask";
    private const string DissolvePath = "Assets/_Project/Materials/Enemies/M_Enemy_Dissolve.mat";
    private const string ModelPath = "Assets/_Project/Art/Enemies/Croaker.fbx";
    private const string DefinitionPath = "Assets/_Project/Data/Enemies/Croaker.asset";

    /// <summary>One frame of the Croaker's clips, which are authored at 30 fps.</summary>
    private const float Frame = 1f / 30f;

    /// <summary>The lash's wind-up, JG-04e rule 4 — core's own constant.</summary>
    private const float LashTelegraphSeconds = CroakerBehaviour.LashTelegraphSeconds;

    private const int Id = 7;

    private readonly List<Object> _created = new List<Object>();

    private readonly List<DomainEventHub> _hubs = new List<DomainEventHub>();

    [TearDown]
    public void DestroyCreatedObjects()
    {
        foreach (DomainEventHub hub in _hubs)
        {
            hub.Dispose();
        }

        _hubs.Clear();

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
    public void Croaker_LocomotionAndDeath()
    {
        AnimatorController controller = Controller();

        Assert.That(controller.parameters.Select(p => p.name),
            Is.EquivalentTo(new[] { "Speed", "WalkRate", "AttackRate", "Attack", "Move", "Hit", "Dead" }),
            "The seven parameters the view writes, and no others.");
        Assert.That(controller.parameters.Single(p => p.name == "Move").type, Is.EqualTo(AnimatorControllerParameterType.Int));

        AnimatorStateMachine body = controller.layers[0].stateMachine;
        AnimatorState locomotion = State(body, "Locomotion");
        AnimatorState death = State(body, "Death");

        Assert.That(body.defaultState, Is.SameAs(locomotion));
        Assert.That(locomotion.speedParameterActive && locomotion.speedParameter == "WalkRate", Is.True);

        var tree = (BlendTree)locomotion.motion;
        Assert.That(tree.blendParameter, Is.EqualTo("Speed"));
        Assert.That(tree.children.Select(c => c.motion.name), Is.EqualTo(new[] { "Idle", "Hop" }));
        Assert.That(tree.children[0].threshold, Is.Zero);
        Assert.That(tree.children[1].threshold, Is.EqualTo(ViewField("_strideSpeed")).Within(1e-4f),
            "At the stride speed the view plays the hop at 1x, so the blend is all Hop there.");

        Assert.That(death.motion.name, Is.EqualTo("Death"));
        AnimatorStateTransition toDeath = body.anyStateTransitions.Single(t => t.destinationState == death);
        Assert.That(toDeath.conditions.Single().parameter, Is.EqualTo("Dead"));
        Assert.That(toDeath.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.If));

        foreach (AnimationClip clip in controller.animationClips)
        {
            Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo(ModelPath), $"{clip.name} is not one of the Croaker's clips.");
        }
    }

    [Test]
    public void Croaker_MovePicksTheClip_ImpactOnTheTelegraphsEnd()
    {
        AnimatorController controller = Controller();
        AnimatorStateMachine body = controller.layers[0].stateMachine;
        AnimatorStateMachine attack = body.stateMachines.Select(s => s.stateMachine).Single(s => s.name == "Attack");

        var clipByMove = new Dictionary<int, string>
        {
            [CroakerBehaviour.SlamMove] = "Slam",
            [CroakerBehaviour.LashMove] = "Lash",
            [CroakerBehaviour.CroakMove] = "Croak",
        };

        foreach (KeyValuePair<int, string> pair in clipByMove)
        {
            AnimatorState state = State(attack, pair.Value);
            AnimatorStateTransition into = body.anyStateTransitions.Single(t => t.destinationState == state);

            Assert.That(into.conditions.Any(c => c.parameter == "Attack" && c.mode == AnimatorConditionMode.If), Is.True, pair.Value);
            Assert.That(into.conditions.Any(c => c.parameter == "Move" && c.mode == AnimatorConditionMode.Equals && (int)c.threshold == pair.Key), Is.True,
                $"{pair.Value} is not picked by Move {pair.Key}.");
            Assert.That(into.conditions.Any(c => c.parameter == "Dead" && c.mode == AnimatorConditionMode.IfNot), Is.True, "A corpse does not strike.");
            Assert.That(into.canTransitionToSelf, Is.True, "A chained hop restarts the Slam from its first frame.");
            Assert.That(into.offset, Is.Zero);

            Assert.That(state.motion.name, Is.EqualTo(pair.Value));
            Assert.That(state.speed, Is.EqualTo(1f));
            Assert.That(state.speedParameterActive, Is.False, "Core's telegraphs are matched to the clips at 1x.");
        }

        var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        model.hideFlags = HideFlags.HideAndDontSave;
        _created.Add(model);

        Assert.That(SlamImpact(model), Is.EqualTo(CroakerBehaviour.SlamTelegraphSeconds).Within(Frame * 0.5f),
            "The Slam lands as the fissure fires.");
        Assert.That(LashReach(model), Is.EqualTo(LashTelegraphSeconds).Within(Frame * 0.5f),
            "The tongue is out furthest as the lane fires.");
        Assert.That(Clip("Croak").length, Is.GreaterThanOrEqualTo(CroakerBehaviour.CroakTelegraphSeconds));

        // And the view tells the controller which: Move is set with the trigger, on the real controller.
        Animator animator = Rig(out DomainEventHub hub);
        hub.Publish(new EnemyTelegraph(Id, LashTelegraphSeconds, CroakerBehaviour.LashMove));

        Assert.That(animator.GetInteger("Move"), Is.EqualTo(CroakerBehaviour.LashMove));
        Assert.That(animator.GetBool("Attack"), Is.True);
    }

    [Test]
    public void Croaker_FlinchMask_WriteDefaultsOff()
    {
        AnimatorController controller = Controller();

        AnimatorControllerLayer flinch = controller.layers[1];
        Assert.That(flinch.avatarMask, Is.EqualTo(AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath)),
            "The Frog's upper-body mask: the Croaker is on the Frog's bones.");
        Assert.That(flinch.defaultWeight, Is.EqualTo(1f));
        Assert.That(State(flinch.stateMachine, "Hit").motion.name, Is.EqualTo("Hit"));

        foreach (AnimatorState state in controller.layers.SelectMany(l => AllStates(l.stateMachine)))
        {
            Assert.That(state.writeDefaultValues, Is.False,
                $"{state.name} writes defaults, which snaps the masked bones to the bind pose from an empty flinch state.");
        }
    }

    [Test]
    public void Croaker_PrefabSize_NoFloatingBar()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefinitionPath);

        Assert.That(prefab, Is.Not.Null, PrefabPath);
        Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Variant));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab), Is.EqualTo(enemy));

        var look = definition.ToLook();
        Assert.That(look.Body, Is.SameAs(prefab.GetComponent<EnemyView>()), "Croaker.asset wears this body.");
        Assert.That(look.Tint, Is.EqualTo(Color.white), "A textured body is tinted white; the capsule's moss would darken it.");

        // The mesh at rest at the model's scale, times the archetype's body scale, which
        // EnemyHitFeedback applies to the root on every rental.
        SkinnedMeshRenderer skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
        float height = skin.sharedMesh.bounds.max.y * prefab.transform.Find("Model").localScale.y * look.BodyScale;
        Assert.That(height, Is.GreaterThanOrEqualTo(2.8f), $"Old Croaker stands {height:F2} m in the arena.");

        Assert.That(prefab.GetComponent<EnemyHealthBar>(), Is.Null, "The boss bar shows its health, not a floating one.");
        Assert.That(prefab.transform.Find("HealthBar").gameObject.activeSelf, Is.False);

        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue, Is.SameAs(skin));
        Assert.That(feedback.FindProperty("_dissolveMaterial").objectReferenceValue, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>(DissolvePath)));
        Assert.That(prefab.transform.Find("Mesh").gameObject.activeSelf, Is.False, "The capsule is switched off.");

        Animator animator = prefab.GetComponentInChildren<Animator>(true);
        Assert.That(animator.runtimeAnimatorController, Is.EqualTo(AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath)));
        Assert.That(animator.cullingMode, Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms));
        Assert.That(animator.applyRootMotion, Is.False, "Core owns velocity; no clip may move a body.");

        var view = new SerializedObject(prefab.GetComponentInChildren<EnemyAnimatorView>(true));
        Assert.That(view.FindProperty("_animator").objectReferenceValue, Is.SameAs(animator));
        Assert.That(view.FindProperty("_hops").boolValue, Is.True, "It closes in hops since JG-05c; each takeoff restarts Hop.");
    }

    [Test]
    public void Croaker_HopTimeIsTheClipsLeap()
    {
        // JG-05c: FrogBodyTests' row for the boss. Core moves it only while in the air, and the view
        // plays Hop from its first frame at 1x on every takeoff, so the hop time is when the clip's
        // feet come back down: the frame after the peak where a foot is back within 5 mm of rest.
        var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        model.hideFlags = HideFlags.HideAndDontSave;
        _created.Add(model);

        Transform foot = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "foot.l");
        AnimationClip hop = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Single(c => c.name == "Hop");

        hop.SampleAnimation(model, 0f);
        float rest = foot.position.y, highest = rest, landed = hop.length;
        bool falling = false;

        for (float t = Frame; t <= hop.length + 1e-4f; t += Frame)
        {
            hop.SampleAnimation(model, t);
            float y = foot.position.y;

            if (y > highest)
            {
                highest = y;
            }
            else if (highest > rest + 0.05f)
            {
                falling = true;
            }

            if (falling && y - rest < 0.005f)
            {
                landed = t;

                break;
            }
        }

        var spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefinitionPath).ToSpec();

        Assert.That(spec.HopTime, Is.EqualTo(landed).Within(Frame), $"The Hop clip lands at {landed:F3} s.");
        Assert.That(spec.HopRestTime, Is.GreaterThan(0f), "It sits between hops, like a Frog.");
    }

    /// <summary>The Slam's landing: the first frame after the leap that a foot is back within 5 mm of rest.</summary>
    private static float SlamImpact(GameObject model)
    {
        Transform foot = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "foot.l");
        AnimationClip slam = Clip("Slam");

        slam.SampleAnimation(model, 0f);
        float rest = foot.position.y;
        bool airborne = false;

        for (int frame = 1; frame <= Mathf.RoundToInt(slam.length / Frame); frame++)
        {
            slam.SampleAnimation(model, frame * Frame);
            float y = foot.position.y;

            if (y > rest + 0.05f)
            {
                airborne = true;
            }
            else if (airborne && y - rest < 0.005f)
            {
                return frame * Frame;
            }
        }

        return slam.length;
    }

    /// <summary>The Lash's reach: the frame the tongue's tip is furthest forward.</summary>
    private static float LashReach(GameObject model)
    {
        Transform tip = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "tongue.tip");
        AnimationClip lash = Clip("Lash");

        float furthest = float.MinValue, at = 0f;

        for (int frame = 0; frame <= Mathf.RoundToInt(lash.length / Frame); frame++)
        {
            lash.SampleAnimation(model, frame * Frame);

            if (tip.position.z > furthest)
            {
                furthest = tip.position.z;
                at = frame * Frame;
            }
        }

        return at;
    }

    /// <summary>A body on <c>AC_Croaker</c>, the prefab's arrangement, with its view injected.</summary>
    private Animator Rig(out DomainEventHub hub)
    {
        var root = new GameObject("Croaker");
        _created.Add(root);
        var body = root.AddComponent<EnemyView>();

        var model = new GameObject("Model");
        model.transform.SetParent(root.transform, false);

        var animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = Controller();

        var view = model.AddComponent<EnemyAnimatorView>();
        typeof(EnemyAnimatorView).GetField("_animator", Private).SetValue(view, animator);

        hub = new DomainEventHub();
        _hubs.Add(hub);

        view.Construct(hub);
        body.Bind(Id, Vector3.zero);

        return animator;
    }

    private static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine machine) =>
        machine.states.Select(s => s.state).Concat(machine.stateMachines.SelectMany(s => AllStates(s.stateMachine)));

    private static AnimatorController Controller()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        Assert.That(controller, Is.Not.Null, ControllerPath);

        return controller;
    }

    private static AnimationClip Clip(string name) =>
        AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Single(c => c.name == name);

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
