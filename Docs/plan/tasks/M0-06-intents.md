# M0-06 — `PlayerMoveIntent`, `IIntentSink`, `IntentBuffer`

**Size:** M · **Depends on:** M0-05 · **Branch:** `m0-06-intents`
**Design refs:** AR §4.1, §6 (`IIntentSink`), ADR-0003

## Goal

Core has a way to tell the body what to do this tick — the first intent is "move the player" — and Game has a buffer that views read after the tick.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Run/PlayerMoveIntent.cs` | Core | Desired velocity and facing |
| `Core/Ports/IIntentSink.cs` | Core | Outbound port core writes intents to |
| `Game/Adapters/IntentBuffer.cs` | Game | Per-frame store, read by views |
| `Tests/Game/Adapters/IntentBufferTests.cs` | Tests.Game | Buffer behaviour |

## Public API

```csharp
namespace Soulvail.Core.Run;
using System.Numerics;

public readonly struct PlayerMoveIntent
{
    public readonly Vector3 Velocity;      // world units / s, Y == 0
    public readonly Vector3 Facing;        // unit vector, Y == 0
    public PlayerMoveIntent(Vector3 velocity, Vector3 facing);
}
```

```csharp
namespace Soulvail.Core.Ports;

public interface IIntentSink
{
    void PlayerMove(in PlayerMoveIntent intent);
    // Later tasks add one method per intent kind: EnemyMove, EnemyAction, ConeHitRequest, Spawn …
    // Explicit methods, not a generic Push<T>: no boxing, and the port grows deliberately.
}
```

```csharp
namespace Soulvail.Game.Adapters;

public sealed class IntentBuffer : IIntentSink
{
    public bool HasPlayerMove { get; }
    public PlayerMoveIntent PlayerMove { get; }        // undefined if !HasPlayerMove — callers check
    void IIntentSink.PlayerMove(in PlayerMoveIntent intent);
    public void Clear();                                // HasPlayerMove = false
}
```

## Behaviour

1. A fresh buffer has `HasPlayerMove == false`.
2. `PlayerMove(intent)` stores the intent and sets `HasPlayerMove = true`. A second call in the same frame overwrites — last wins (the session emits exactly one per tick; this rule just makes the behaviour defined).
3. `Clear()` sets `HasPlayerMove = false`. It runs once per frame **before** `session.Tick` (M0-16 `RunTicker` owns the call order).
4. Neither `PlayerMove` nor `Clear` allocates.
5. `PlayerMoveIntent` is immutable; the constructor does not normalise or validate (core guarantees the invariants; a debug assertion that `Facing` is unit-length is acceptable).

## Tests

| Test | Given / When / Then |
|---|---|
| `Fresh_HasNoPlayerMove` | new buffer / — / `HasPlayerMove == false` |
| `PlayerMove_StoresIntent` | buffer / PlayerMove(v=(1,0,0), f=(0,0,1)) / `HasPlayerMove`, `PlayerMove.Velocity == (1,0,0)`, `Facing == (0,0,1)` |
| `PlayerMove_Twice_LastWins` | two calls / — / stored == second |
| `Clear_ResetsFlag` | stored intent / Clear / `HasPlayerMove == false` |
| `WriteAndClear_AllocateNothing` | warm-up / 10 000 × (PlayerMove, Clear) / allocated-bytes delta == 0 |

## Acceptance

- [x] All tests green
- [x] Zero errors, zero new analyzer warnings
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Enemy or skill intents — added by the tasks that need them (M1).
- Applying the intent to a `CharacterController` — M0-16 `PlayerView`.

## As built

**Five files, not four.** The Files table's fourth row is joined by `Tests/Core/Run/PlayerMoveIntentTests.cs`, because behaviour rule 5 describes a Core type and the table's only test file is in `Tests.Game`. Approved before implementation; still size M. Its one test covers the half of rule 5 the compiler cannot: that the constructor stores a non-unit facing and a zero facing verbatim, normalising and validating neither. Immutability is left to `readonly struct`, where a mutation is a compile error.

**No `Facing` unit-length assertion**, though the rule permits one. Release cost would have been nil — `Debug.Assert` is `[Conditional("DEBUG")]` and Unity defines `DEBUG` only for the Editor and Development Builds — but nothing constructs a `PlayerMoveIntent` yet, so the guard would have decided whether a zero facing is legal before any producer existed to have an opinion. M0-10/M0-16 own that.

**The `PlayerMove` name collision compiles**, as the Public API intends: an explicit implementation's name is `IIntentSink.PlayerMove` and never enters the class's declaration space. Kept deliberately — core holds an `IIntentSink` and can only write, a view holds an `IntentBuffer` and can only read, so the type enforces the direction of the boundary rather than a convention having to.

**`Clear()` leaves the stored intent alone**, matching the "undefined if `!HasPlayerMove`" contract and `WorldSnapshot.Clear`'s precedent. `Clear_ResetsFlag` asserts only the flag; pinning the leftover would promise something the spec calls undefined. On the PROGRESS watch list, because the visible failure is a view that skips the flag check and slides on last frame's velocity.

`WriteAndClear_AllocateNothing` is measured with `AllocationAssert`, not the row's stale "allocated-bytes delta == 0" — see PROGRESS, M0-02.

**Verified:** clean recompile of all four emitting assemblies, zero errors and zero warnings in the Console; full EditMode suite via `TestRunnerApi` green at **57 passed / 0 failed** (51 before).
