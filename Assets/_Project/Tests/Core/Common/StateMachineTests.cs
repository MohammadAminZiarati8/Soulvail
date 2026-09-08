using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Common;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Common;

[TestFixture]
public sealed class StateMachineTests
{
    private enum State
    {
        A,
        B,
        C,
        D,
    }

    [Test]
    public void Start_InvokesEnterOfInitialState()
    {
        var fsm = new StateMachine<State>(State.A);
        int entered = 0;
        fsm.OnEnter(State.A, () => entered++);

        fsm.Start();

        Assert.That(entered, Is.EqualTo(1));
    }

    [Test]
    public void Tick_BeforeStart_Throws()
    {
        var fsm = new StateMachine<State>(State.A);

        Assert.Throws<InvalidOperationException>(() => fsm.Tick(0.1f));
    }

    [Test]
    public void Start_Twice_Throws()
    {
        var fsm = new StateMachine<State>(State.A);
        fsm.Start();

        Assert.Throws<InvalidOperationException>(() => fsm.Start());
    }

    [Test]
    public void Transition_ToSameState_IsNoOp()
    {
        var fsm = new StateMachine<State>(State.A);
        int entered = 0;
        int exited = 0;
        fsm.OnEnter(State.A, () => entered++);
        fsm.OnExit(State.A, () => exited++);
        fsm.Start();

        // Start legitimately entered A once; only what happens after it is under test.
        entered = 0;

        bool transitioned = fsm.Transition(State.A);

        Assert.That(transitioned, Is.False);
        Assert.That(entered, Is.Zero);
        Assert.That(exited, Is.Zero);
        Assert.That(fsm.Current, Is.EqualTo(State.A));
    }

    [Test]
    public void Transition_RunsExitThenEnter_InOrder()
    {
        var fsm = new StateMachine<State>(State.A);
        var log = new List<string>();
        fsm.OnExit(State.A, () => log.Add("exit A"));
        fsm.OnEnter(State.B, () => log.Add("enter B"));
        fsm.Start();

        bool transitioned = fsm.Transition(State.B);

        Assert.That(transitioned, Is.True);
        Assert.That(log, Is.EqualTo(new[] { "exit A", "enter B" }));
        Assert.That(fsm.Current, Is.EqualTo(State.B));
    }

    [Test]
    public void Transition_ResetsTimeInState()
    {
        var fsm = new StateMachine<State>(State.A);
        fsm.Start();
        fsm.Tick(1.5f);

        fsm.Transition(State.B);

        Assert.That(fsm.TimeInState, Is.Zero);
    }

    [Test]
    public void Tick_AccumulatesTimeInState_BeforeOnTick()
    {
        var fsm = new StateMachine<State>(State.A);
        var recorded = new List<float>();
        fsm.OnTick(State.A, _ => recorded.Add(fsm.TimeInState));
        fsm.Start();

        fsm.Tick(0.25f);
        fsm.Tick(0.25f);

        Assert.That(recorded, Is.EqualTo(new[] { 0.25f, 0.5f }));
    }

    [Test]
    public void Transition_InsideOnEnter_IsDeferred_ThenApplied()
    {
        var fsm = new StateMachine<State>(State.A);
        var enterOrder = new List<State>();
        int exitedB = 0;

        fsm.OnEnter(State.B, () =>
        {
            enterOrder.Add(State.B);
            fsm.Transition(State.C);

            // The request is deferred: B is still current until this handler returns.
            Assert.That(fsm.Current, Is.EqualTo(State.B));
        });
        fsm.OnExit(State.B, () => exitedB++);
        fsm.OnEnter(State.C, () => enterOrder.Add(State.C));
        fsm.Start();

        fsm.Transition(State.B);

        Assert.That(fsm.Current, Is.EqualTo(State.C));
        Assert.That(enterOrder, Is.EqualTo(new[] { State.B, State.C }));
        Assert.That(exitedB, Is.EqualTo(1));
    }

    [Test]
    public void Transition_InsideOnTick_AppliesAfterHandler()
    {
        var fsm = new StateMachine<State>(State.A);
        int tickedB = 0;
        fsm.OnTick(State.A, _ => fsm.Transition(State.B));
        fsm.OnTick(State.B, _ => tickedB++);
        fsm.Start();

        fsm.Tick(0.1f);

        Assert.That(fsm.Current, Is.EqualTo(State.B));
        Assert.That(tickedB, Is.Zero);
    }

    [Test]
    public void MultipleTransitionsInOneHandler_LastWins()
    {
        var fsm = new StateMachine<State>(State.A);
        bool enteredC = false;

        fsm.OnEnter(State.B, () =>
        {
            fsm.Transition(State.C);
            fsm.Transition(State.D);
        });
        fsm.OnEnter(State.C, () => enteredC = true);
        fsm.Start();

        fsm.Transition(State.B);

        Assert.That(fsm.Current, Is.EqualTo(State.D));
        Assert.That(enteredC, Is.False);
    }

    [Test]
    public void MultipleHandlers_RunInRegistrationOrder()
    {
        var fsm = new StateMachine<State>(State.A);
        var log = new List<string>();
        fsm.OnEnter(State.A, () => log.Add("first"));
        fsm.OnEnter(State.A, () => log.Add("second"));

        fsm.Start();

        Assert.That(log, Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var fsm = new StateMachine<State>(State.A);
        int ticks = 0;
        fsm.OnTick(State.A, _ => ticks++);
        fsm.Start();

        AllocationAssert.None(() => fsm.Tick(0.016f));

        Assert.That(ticks, Is.GreaterThan(0), "Sanity: the measured body actually ran the tick handler.");
    }
}
