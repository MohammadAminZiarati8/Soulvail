# M0-02 — `StateMachine<T>` and the core-purity guard test

**Size:** S · **Depends on:** M0-01 · **Branch:** `m0-02-state-machine`
**Design refs:** AR §5.2 (as written in v0.1 patterns), AR §5 modules table (`Ai`), ADR-0001, ADR-0003

## Goal

The one generic state machine used everywhere (game flow, enemy AI, boss phases) exists and is tested, and a test guards that `Soulvail.Core` never grows an engine reference.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Common/StateMachine.cs` | Core | Generic FSM over an enum |
| `Tests/Core/Common/StateMachineTests.cs` | Tests.Core | Behaviour tests |
| `Tests/Core/AssemblyPurityTests.cs` | Tests.Core | Asserts Core references no `UnityEngine*` / `UnityEditor*` assembly |

## Public API

```csharp
namespace Soulvail.Core.Common;

public sealed class StateMachine<TState> where TState : struct, Enum
{
    public StateMachine(TState initial);

    public TState Current { get; }
    public float  TimeInState { get; }        // seconds, reset on every transition
    public bool   IsStarted { get; }

    public void OnEnter(TState state, Action handler);
    public void OnExit (TState state, Action handler);
    public void OnTick (TState state, Action<float> handler);

    public void Start();                      // invokes OnEnter(initial). Required before Tick.
    public bool Transition(TState to);        // false if to == Current (no handlers run)
    public void Tick(float dt);
}
```

## Behaviour

1. `Start()` invokes the `OnEnter` handler of the initial state once. `Tick` before `Start` throws `InvalidOperationException`. `Start` twice throws.
2. `Transition(to)` with `to == Current` is a no-op and returns `false`.
3. `Transition(to)` runs `OnExit(Current)`, sets `Current = to`, resets `TimeInState` to 0, runs `OnEnter(to)`, returns `true`.
4. A `Transition` requested **inside** an `OnEnter`, `OnExit`, or `OnTick` handler is deferred until the current handler returns, then applied. If several are requested in one handler, the last wins.
5. `Tick(dt)` adds `dt` to `TimeInState` **before** invoking `OnTick(Current)(dt)`.
6. Multiple handlers may be registered per state and event; they run in registration order.
7. States with no handlers are valid; nothing runs.
8. `Tick` allocates nothing. `Transition` may allocate (enum boxing) — acceptable, transitions are rare.
9. Handlers registered after `Start` take effect immediately.

## Tests

| Test | Given / When / Then |
|---|---|
| `Start_InvokesEnterOfInitialState` | machine(A), OnEnter(A) counter / Start / counter == 1 |
| `Tick_BeforeStart_Throws` | machine(A) / Tick / `InvalidOperationException` |
| `Start_Twice_Throws` | started / Start / throws |
| `Transition_ToSameState_IsNoOp` | started at A / Transition(A) / returns false, no handlers ran |
| `Transition_RunsExitThenEnter_InOrder` | log list / Transition(B) / log == ["exit A", "enter B"], Current == B |
| `Transition_ResetsTimeInState` | Tick(1.5) / Transition(B) / TimeInState == 0 |
| `Tick_AccumulatesTimeInState_BeforeOnTick` | OnTick(A) records TimeInState / Tick(0.25) twice / recorded [0.25, 0.5] |
| `Transition_InsideOnEnter_IsDeferred_ThenApplied` | OnEnter(B) calls Transition(C) / Transition(B) / Current == C; enter order [B, C]; exit B ran once |
| `Transition_InsideOnTick_AppliesAfterHandler` | OnTick(A) calls Transition(B) / Tick / Current == B; OnTick(B) not called in the same Tick |
| `MultipleTransitionsInOneHandler_LastWins` | OnEnter(B) calls Transition(C) then Transition(D) / Transition(B) / Current == D; C never entered |
| `MultipleHandlers_RunInRegistrationOrder` | two OnEnter(A) handlers / Start / order preserved |
| `Tick_DoesNotAllocate` | started, 10 000 Ticks after warm-up / measure `GC.GetAllocatedBytesForCurrentThread()` / delta == 0 |
| `CoreAssembly_ReferencesNoUnityAssemblies` | `typeof(StateMachine<>).Assembly.GetReferencedAssemblies()` / — / none start with `UnityEngine` or `UnityEditor` |

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Hierarchical states, guards, or transition tables. If a machine needs more than ~8 states, split the behaviour.
- Any consumer of the state machine.

## As built

_Filled at merge._
