using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// JG-15f rule 1: <c>AC_MossguardWarlord</c> — <c>AC_Mossguard</c>'s layers with a <c>Move</c> int,
/// one Strike sub-machine per move (each holding an <c>Attack</c> state, which is what weights the
/// layer), and a daze on the legs.
/// </summary>
[TestFixture]
public sealed class MossguardWarlordBodyTests
{
    private const string Root = "Assets/_Project/";
    private const string Clips = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/";
    private const float Frame = 1f / 30f;

    private static readonly (WarlordMove Move, string Machine, string Clip)[] Moves =
    {
        (WarlordMove.Sweep, "Sweep", "Melee_2H_Attack_Slice"),
        (WarlordMove.Combo, "Sweep", "Melee_2H_Attack_Slice"),
        (WarlordMove.ComboSecond, "Chop", "Melee_2H_Attack_Chop"),
        (WarlordMove.Charge, "Charge", "Melee_Blocking"),
        (WarlordMove.WarCry, "WarCry", "Skeletons_Taunt"),
        (WarlordMove.Lob, "Lob", "Throw"),
    };

    [Test]
    public void WarlordBody_EveryMoveHasItsClip()
    {
        AnimatorController controller = Controller();
        Assert.That(controller.layers.Select(l => l.name), Is.EqualTo(new[] { "Base Layer", "Guard", "Strike", "Flinch" }));
        Assert.That(controller.parameters.Single(p => p.name == "Move").type, Is.EqualTo(AnimatorControllerParameterType.Int));

        AnimatorStateMachine strike = controller.layers[2].stateMachine;
        Assert.That(strike.defaultState.motion, Is.Null);
        Assert.That(strike.states.Select(s => s.state), Is.EqualTo(new[] { strike.defaultState }), "Every move lives in its own sub-machine.");

        foreach ((WarlordMove move, string machine, string clip) in Moves)
        {
            AnimatorState state = MoveState(strike, machine);
            Assert.That(state.name, Is.EqualTo("Attack"), "FlinchLayer.Drive weights Strike only in a state named Attack.");
            Assert.That(state.motion.name, Is.EqualTo(clip), $"{move}");
            AnimatorStateTransition into = Entry(strike, move);
            Assert.That(into.destinationState, Is.SameAs(state), $"{move}");
            Assert.That(into.conditions.Any(c => c.parameter == "Attack" && c.mode == AnimatorConditionMode.If), Is.True);
            Assert.That(into.conditions.Any(c => c.parameter == "Dead" && c.mode == AnimatorConditionMode.IfNot), Is.True);
            Assert.That(state.transitions.Any(t => t.destinationState == strike.defaultState), Is.True, $"{move} returns to Empty.");
        }

        AnimatorStateMachine legs = controller.layers[0].stateMachine;
        Assert.That(State(legs, "Death").motion.name, Is.EqualTo("Death_A"));
        Assert.That(State(legs, "DazeHit").motion.name, Is.EqualTo("Hit_B"));
        Assert.That(State(legs, "DazeIdle").motion.name, Is.EqualTo("Idle_B"));
        Assert.That(controller.layers[1].stateMachine.defaultState.motion.name, Is.EqualTo("Melee_Blocking"));
        Assert.That(controller.layers[1].avatarMask, Is.EqualTo(Mossguard().layers[1].avatarMask), "The guard layer is the Mossguard's.");
        Assert.That(controller.layers[2].avatarMask, Is.EqualTo(Mossguard().layers[2].avatarMask));
    }

    [Test]
    public void WarlordBody_NoWriteDefaults_NoRootMotion()
    {
        AnimatorController controller = Controller();
        foreach (AnimatorState state in controller.layers.SelectMany(l => AllStates(l.stateMachine)))
        {
            Assert.That(state.writeDefaultValues, Is.False, state.name);
        }

        foreach (AnimationClip clip in controller.animationClips)
        {
            Assert.That(AssetDatabase.GetAssetPath(clip), Does.StartWith(Clips), clip.name);
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty, $"{clip.name}: animation never gates damage.");
        }

        Animator animator = Prefab().GetComponentInChildren<Animator>(true);
        Assert.That(animator.runtimeAnimatorController, Is.SameAs(controller));
        Assert.That(animator.applyRootMotion, Is.False);
    }

    [Test]
    public void WarlordBody_SweepLandsAtTheWindupsEnd()
    {
        AnimatorStateMachine strike = Controller().layers[2].stateMachine;
        var windups = new Dictionary<WarlordMove, float>
        {
            [WarlordMove.Sweep] = WarlordBehaviour.SweepWindupSeconds,
            [WarlordMove.Combo] = WarlordBehaviour.SweepWindupSeconds,
            [WarlordMove.ComboSecond] = WarlordBehaviour.ComboSecondWindupSeconds,
            [WarlordMove.Lob] = WarlordBehaviour.LobTelegraphSeconds,
            [WarlordMove.WarCry] = WarlordBehaviour.WarCryTelegraphSeconds,
        };

        GameObject body = Object.Instantiate(Prefab());
        using var hub = new DomainEventHub();
        try
        {
            GameObject model = body.transform.Find("Model").gameObject;
            Transform hand = model.GetComponentsInChildren<Transform>().Single(t => t.name == "handslot.r");
            Animator animator = model.GetComponent<Animator>();
            animator.Rebind();
            animator.Update(0f);
            model.GetComponent<EnemyAnimatorView>().Construct(hub);
            body.GetComponent<EnemyView>().Bind(15, Vector3.zero);

            foreach (KeyValuePair<WarlordMove, float> pair in windups)
            {
                string machine = Moves.First(m => m.Move == pair.Key).Machine;
                AnimatorState state = MoveState(strike, machine);
                var clip = (AnimationClip)state.motion;
                Assert.That(state.speedParameterActive && state.speedParameter == "AttackRate", Is.True, machine);

                // The war cry's moment is the fist at its height; every other blow, the hand at its fastest.
                float blow = pair.Key == WarlordMove.WarCry ? Highest(clip, model, hand) : Fastest(clip, model, hand);
                float from = Entry(strike, pair.Key).offset * clip.length;

                hub.Publish(new EnemyTelegraph(15, pair.Value, (int)pair.Key));
                Assert.That(animator.GetInteger("Move"), Is.EqualTo((int)pair.Key));
                float rate = animator.GetFloat("AttackRate") * state.speed;
                Assert.That((blow - from) / rate, Is.EqualTo(pair.Value).Within(0.5f * Frame / rate),
                    $"{pair.Key}'s blow at {blow:F3} s of {clip.name} lands at the wind-up's end.");
            }
        }
        finally
        {
            Object.DestroyImmediate(body);
        }
    }

    [Test]
    public void WarlordBody_ChargeHoldsTheBlock()
    {
        AnimatorController controller = Controller();
        AnimatorStateMachine strike = controller.layers[2].stateMachine;
        AnimatorState hold = MoveState(strike, "Charge");
        var clip = (AnimationClip)hold.motion;
        Assert.That(clip.name, Is.EqualTo("Melee_Blocking"));
        Assert.That(clip.isLooping, Is.True);
        Assert.That(hold.speedParameterActive, Is.False, "The hold is time, not a blow.");

        AnimatorStateTransition timed = hold.transitions.Single(t => t.hasExitTime);
        float held = timed.exitTime * clip.length / hold.speed;
        float dash = WarlordBehaviour.ChargeLength / WarlordBehaviour.ChargeSpeed;
        Assert.That(held, Is.GreaterThanOrEqualTo(WarlordBehaviour.ChargeTelegraphSeconds + dash), "Held through wind-up and dash.");
        Assert.That(held, Is.LessThanOrEqualTo(WarlordBehaviour.ChargeTelegraphSeconds + dash + WarlordBehaviour.DashGraceSeconds + Frame));
        Assert.That(hold.transitions.Any(t => !t.hasExitTime && t.conditions.Any(c => c.parameter == "Dazed" && c.mode == AnimatorConditionMode.If)),
            Is.True, "A stone ends the hold at once.");
        Assert.That(hold.transitions.Any(t => !t.hasExitTime && t.conditions.Any(c => c.parameter == "Dead" && c.mode == AnimatorConditionMode.If)), Is.True);

        // The legs run the dash: Running_A past any walk, below the dash's speed.
        var tree = (BlendTree)controller.layers[0].stateMachine.defaultState.motion;
        Assert.That(tree.children.Select(c => c.motion.name), Is.EqualTo(new[] { "Idle_B", "Walking_B", "Running_A" }));
        float stride = new SerializedObject(Prefab().GetComponentInChildren<EnemyAnimatorView>()).FindProperty("_strideSpeed").floatValue;
        Assert.That(tree.children[2].threshold, Is.GreaterThan(stride).And.LessThan(WarlordBehaviour.ChargeSpeed));
    }

    private static float Fastest(AnimationClip clip, GameObject model, Transform hand)
    {
        float best = 0f, at = 0f;
        clip.SampleAnimation(model, 0f);
        Vector3 previous = model.transform.InverseTransformPoint(hand.position);
        for (int frame = 1; frame <= Mathf.RoundToInt(clip.length / Frame); frame++)
        {
            clip.SampleAnimation(model, frame * Frame);
            Vector3 point = model.transform.InverseTransformPoint(hand.position);
            float speed = (point - previous).magnitude;
            if (speed > best)
            {
                best = speed;
                at = frame * Frame;
            }

            previous = point;
        }

        return at;
    }

    private static float Highest(AnimationClip clip, GameObject model, Transform hand)
    {
        float best = float.MinValue, at = 0f;
        for (int frame = 0; frame <= Mathf.RoundToInt(clip.length / Frame); frame++)
        {
            clip.SampleAnimation(model, frame * Frame);
            float y = model.transform.InverseTransformPoint(hand.position).y;
            if (y > best)
            {
                best = y;
                at = frame * Frame;
            }
        }

        return at;
    }

    private static AnimatorStateTransition Entry(AnimatorStateMachine strike, WarlordMove move) =>
        strike.anyStateTransitions.Single(t => t.conditions.Any(c =>
            c.parameter == "Move" && c.mode == AnimatorConditionMode.Equals && (int)c.threshold == (int)move));

    private static AnimatorState MoveState(AnimatorStateMachine strike, string machine) =>
        strike.stateMachines.Single(s => s.stateMachine.name == machine).stateMachine.states.Single().state;

    private static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine machine) =>
        machine.states.Select(s => s.state).Concat(machine.stateMachines.SelectMany(s => AllStates(s.stateMachine)));

    private static AnimatorState State(AnimatorStateMachine machine, string name) => machine.states.Single(s => s.state.name == name).state;

    private static GameObject Prefab() => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/Mossguard_Warlord.prefab");

    private static AnimatorController Controller() =>
        AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "Animation/Controllers/AC_MossguardWarlord.controller");

    private static AnimatorController Mossguard() =>
        AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "Animation/Controllers/AC_Mossguard.controller");
}
