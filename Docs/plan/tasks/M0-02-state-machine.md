# M0-02 — `StateMachine<T>` and the core-purity guard test

**Size:** M · **Depends on:** M0-01 · **Branch:** `m0-02-state-machine`
**Design refs:** AR §5.2 (as written in v0.1 patterns), AR §5 modules table (`Ai`), ADR-0001, ADR-0003

## Goal

The one generic state machine used everywhere (game flow, enemy AI, boss phases) exists and is tested, and a test guards that `Soulvail.Core` never grows an engine reference.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Common/StateMachine.cs` | Core | Generic FSM over an enum |
| `Tests/Core/Common/StateMachineTests.cs` | Tests.Core | Behaviour tests |
| `Tests/Core/AssemblyPurityTests.cs` | Tests.Core | Asserts Core references no `UnityEngine*` / `UnityEditor*` assembly |
| `Tests/Core/Support/AllocationAssert.cs` | Tests.Core | The one way every spec measures "allocates nothing" |

```csharp
namespace Soulvail.Tests.Core.Support;

public static class AllocationAssert
{
    /// Runs body once as warm-up, then `iterations` times under measurement, and fails if anything was allocated.
    /// Probes GC.GetAllocatedBytesForCurrentThread() once; if the runtime doesn't support it (returns 0 for a
    /// deliberately allocating probe), falls back to asserting GC.CollectionCount(0) is unchanged across
    /// `iterations` × 10 runs — coarse, but it catches per-call allocation.
    public static void None(Action body, int iterations = 10_000);
}
```

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
| `Tick_DoesNotAllocate` | started / `AllocationAssert.None(() => fsm.Tick(0.016f))` / passes |
| `CoreAssembly_ReferencesNoUnityAssemblies` | `typeof(StateMachine<>).Assembly.GetReferencedAssemblies()` / — / none start with `UnityEngine` or `UnityEditor` |
| `AllocationAssert_DetectsAllocation` | — / `None(() => new object())` / fails (self-test of the helper, whichever strategy it picked) |
| `AllocationAssert_PassesForPureBody` | — / `None(() => Math.Sqrt(2))` / passes |

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Hierarchical states, guards, or transition tables. If a machine needs more than ~8 states, split the behaviour.
- Any consumer of the state machine.

## As built

**15/15 tests green. Zero errors, zero warnings.** Public API and all nine behaviour rules built as written.

Three deviations, detailed in [PROGRESS](../PROGRESS.md) under M0-02:

1. **`csc.rsp` (`-langversion:10`) beside all five asmdefs.** Unity 6.3 compiles at C# 9, so the file-scoped namespaces this spec is written in did not build. Owner chose to keep the convention over rewriting it. Every future asmdef needs one.
2. **`AllocationAssert` measures with Unity's GC recorder**, not the `GC.GetAllocatedBytesForCurrentThread()` / `GC.CollectionCount(0)` strategies described above. Both are inert on Unity's Mono — measured, not assumed — and would have made every "allocates nothing" claim in the project vacuous. The signature in the Files table is unchanged. **Supersede that comment when this spec is next read as a pattern.**
3. **Fifth file `Tests/Core/Support/AllocationAssertTests.cs`** — the two `AllocationAssert_*` rows in the Tests table had no fixture in the Files table.

Implementation note for later readers: `Tick` caches the current state's handler list on transition rather than doing an enum-keyed dictionary lookup per tick, so rule 8 holds by construction on any runtime.
