# M0-10 — `RunSession`, `RecordingIntents` fake, tests

**Size:** S · **Depends on:** M0-03, M0-04, M0-09 · **Branch:** `m0-10-run-session`
**Design refs:** AR §3, §4.1, §7 (constructor injection)

## Goal

The brain exists: given a snapshot each tick, it moves the player through `PlayerMotor` and emits a `PlayerMoveIntent`, publishes run lifecycle events, and is fully tested with no Unity in sight.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Run/RunSession.cs` | Core | Implements `IRunSession` |
| `Tests/Core/Fakes/RecordingIntents.cs` | Tests.Core | `IIntentSink` fake capturing intents |
| `Tests/Core/Run/RunSessionTests.cs` | Tests.Core | Every rule below |

## Public API

```csharp
namespace Soulvail.Core.Run;

public sealed class RunSession : IRunSession
{
    public RunSession(ContentCatalog catalog, IRandom random, IDomainEvents events, IIntentSink intents);
    // IRunSession members
}
```

```csharp
namespace Soulvail.Tests.Core.Fakes;

public sealed class RecordingIntents : IIntentSink
{
    public List<PlayerMoveIntent> PlayerMoves { get; }
    public PlayerMoveIntent LastPlayerMove { get; }     // throws if none
    public void Clear();
}
```

## Behaviour

1. `Start(config)` when already running throws `InvalidOperationException`.
2. `Start` resolves `catalog.Character(config.CharacterId)` — an unknown id propagates the catalog's `KeyNotFoundException`, and the session stays not-running.
3. `Start` builds `RunState` with `Seed = random.Seed`, a `PlayerMotor` from `Character.Movement` with initial facing `+Z`, `Time = 0`, publishes `RunStarted(characterId, seed)`, then sets `IsRunning = true`. (Event is published *before* `IsRunning` flips, so a subscriber reading `IsRunning` inside the handler sees `false` — documented, tested.)
4. `Tick(snapshot)` when not running throws `InvalidOperationException`.
5. `Tick`: `State.Time += snapshot.Dt`; `State.PlayerPosition = snapshot.PlayerPosition`; `Motor.Tick(snapshot.Dt, snapshot.MoveInput, null)`; `intents.PlayerMove(new(Motor.Velocity, Motor.Facing))`. Exactly one `PlayerMove` per tick, including when input is zero (the body needs the decel velocity too).
6. `End()` when not running is a no-op (safe to call from scope disposal).
7. `End()`: publishes `RunEnded(State.Time)`, sets `IsRunning = false`. `State` remains readable afterwards.
8. After `End`, `Start` may be called again with a new config; a new `RunState` is created.
9. `Tick` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `Start_PublishesRunStarted_WithSeed` | catalog{oathbound}, FixedRandom seed 99 / Start / `Single<RunStarted>()` has id oathbound, seed 99; `IsRunning` |
| `Start_BuildsState` | — / Start / `State.Character.Id == oathbound`, `Motor.Facing == +Z`, `Time == 0` |
| `Start_WhenRunning_Throws` | started / Start / throws; still running |
| `Start_UnknownCharacter_Throws_NotRunning` | Start("x.y") / — / `KeyNotFoundException`; `IsRunning == false`; no events |
| `Start_EventPublishedBeforeIsRunningFlips` | subscriber captures `IsRunning` / Start / captured false |
| `Tick_BeforeStart_Throws` | — / Tick / `InvalidOperationException` |
| `Tick_AdvancesTime_AndStoresPosition` | started / Tick(dt 0.5, pos (1,0,2)) / `Time == 0.5`, `PlayerPosition == (1,0,2)` |
| `Tick_EmitsExactlyOnePlayerMove` | started / Tick / `PlayerMoves.Count == 1` |
| `Tick_ZeroInput_StillEmitsIntent` | started / Tick with MoveInput zero / one intent, velocity zero |
| `Tick_FullInput_IntentMatchesMotor` | started / 1 s of ticks with (0,1) / last intent velocity ≈ (0,0,5.4), facing ≈ +Z |
| `End_PublishesRunEnded_WithTime` | ticked to 2.0 / End / `Single<RunEnded>().Time == 2.0`; `IsRunning == false` |
| `End_WhenNotRunning_IsNoOp` | — / End / no throw, no events |
| `End_ThenStart_CreatesNewState` | run, End / Start / new `State` instance, `Time == 0` |
| `Tick_AllocatesNothing` | started, warm-up / 10 000 ticks / allocates nothing (`AllocationAssert`) |

Test fixture: a catalog with one `CharacterSpec` (`character.oathbound`, CC §7 numbers), `RecordingEvents`, `RecordingIntents`, `FixedRandom` with a known seed. `FixedRandom` must expose `Seed` (constructor parameter) — add it in this task if M0-04 didn't.

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Anything beyond moving the player: no enemies, no stages, no XP. This is the walking skeleton's brain, deliberately tiny.
- Face-direction from targeting — M1-05 passes it.

## As built

_Filled at merge._
