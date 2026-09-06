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

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Enemy or skill intents — added by the tasks that need them (M1).
- Applying the intent to a `CharacterController` — M0-16 `PlayerView`.

## As built

_Filled at merge._
