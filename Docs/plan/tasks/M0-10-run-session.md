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
| `Config_DefaultId_Throws` | — / `new RunConfig(default)` / `ArgumentException`; a well-formed but unknown id does **not** throw here |
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

`Config_DefaultId_Throws` is the one row here whose behaviour was **built in M0-09**, not in this task: `RunConfig` rejects `default(ContentId)` because it means nobody chose a class, which is a composition mistake rather than missing content. It belongs in `RunSessionTests` rather than a fixture of its own — M0-09 is contracts with no tests, and its spec says this fixture exercises all of them. Rule 2 above is the other half of the pair and must stay true: a well-formed id the catalog does not hold still reaches the catalog and still throws `KeyNotFoundException`.

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Anything beyond moving the player: no enemies, no stages, no XP. This is the walking skeleton's brain, deliberately tiny.
- Face-direction from targeting — M1-05 passes it.

## As built

Three files exactly as the Files table names them, plus the one edit the fixture note authorises (`FixedRandom.Seed`). 18 tests, suite 95 → 113, green after a clean rebuild with zero errors and zero warnings. Six deviations, and one open question of this task's settled in `Architecture.md`.

1. **`FixedRandom` gained a *second* constructor, `FixedRandom(int seed, params float[] floats)`.** M0-04 hard-coded `Seed => 0`, and `Start_PublishesRunStarted_WithSeed` needs 99. A `params` array must come last, so the seed cannot be added to the existing constructor without breaking it; a second one leaves all three existing `new FixedRandom(0.5f, …)` call sites binding exactly where they did, since there is no implicit `float`→`int` conversion. The float-only constructor now delegates with seed 0. One sharp edge, documented in the type: `new FixedRandom(99)` is a *seed*, because `int`→`int` beats `int`→`float` in overload resolution; a scripted value of 99 is spelled `99f`.
2. **`RecordingIntents.PlayerMoves` is an `IReadOnlyList<PlayerMoveIntent>`, where the Public API block says `List<PlayerMoveIntent>`.** A test that can `Add` to the record can fake a tick that never happened, and the assertion that then passes is worthless — the same reasoning that wrapped `ContentCatalog.Characters` in M0-08, and the shape `RecordingEvents.All` already has. Nothing in the Tests table needs `List`'s API. `IIntentSink.PlayerMove` is implemented **explicitly**, as the Public API block implies by omitting it: writing goes through the port, reading through the class, which is the one-way shape `IntentBuffer` enforces for the same reason.
3. **Two guards rules 1–9 do not list, and two test rows for them.** `RunSession`'s constructor rejects a null dependency and `Start` rejects a null config, both `ArgumentNullException`. Both are boundaries in M0-09's sense — the constructor is public and called from `Soulvail.Game` (M0-12's `RunInstaller`), and `Start` is the inbound port — so the guards are reachable, testable, and defend against code core cannot see. Without them a forgotten `intents` registration surfaces a frame later as an NRE inside `Tick`, naming the tick rather than the registration. `Ctor_NullDependency_Throws` and `Start_NullConfig_Throws` cover them. **`Tick` deliberately has no null-snapshot guard**: it runs 60 times a second against one instance the builder owns for the whole run, so a null could only be the first tick after a mis-wired scope, which fails immediately and unmissably either way.
4. **A third extra row, `End_EventPublishedBeforeIsRunningFlips`.** Rule 7 states an order — publish, then flip — exactly as rule 3 does, but unlike rule 3 it names no consequence and gets no row. The consequence is real and now documented in the type: **during either lifecycle event the session still reports the state it is leaving**, so a handler reading `IsRunning` gets a consistent answer at both ends (`false` inside `RunStarted`, `true` inside `RunEnded`). Documenting it makes it a promise, so it is pinned.
5. **A private nested `CapturingEvents` inside `RunSessionTests` — the first helper class in a test file in this project.** The two ordering rows need to look at the session from *inside* a publish, and `RecordingEvents` cannot do that: it records what was published, which says nothing about what was true when it happened. The alternatives were a fourth file outside the Files table or a callback hook bolted onto the shared fake for two tests' benefit. Nested and private is the smallest honest option, and one-class-per-file is about types a reader has to find.
6. **Extra assertions inside specced rows, no new rows.** `End_PublishesRunEnded_WithTime` also asserts that `Tick` after `End` throws — rule 4's "not running" has two ways to arise and the table only names one. `Tick_AdvancesTime_AndStoresPosition` ticks twice, because one tick cannot tell `+=` from `=`. `Tick_ZeroInput_StillEmitsIntent` also releases the stick mid-speed, which is the rule's real payload. `End_WhenNotRunning_IsNoOp` also calls `End` twice on a finished run, which is the case `RunScope`'s teardown will actually hit. M0-05's precedent: assertions inside the row that already owns the behaviour.

**The open question is settled: no `IClock`.** Simulated time is the sum of each tick's `Dt`, which arrives on the snapshot, so a clock in the session would be a second answer to "how much time has passed" — and the wrong one, since wall-clock keeps running while the game is paused. `IClock` (M2-01) answers a different question, for persistence. **AR §7's `new RunSession(clock, random, events, catalog, intents)` is corrected to the built four-argument shape in this change**, with the reasoning recorded beside it under the same rule §6 already states about ports.

**The recurring "Tests table names a fixture this type does not belong in" gap did not occur** — checked before writing, as asked: no row tests `RecordingIntents`, so no fourth file was needed. The cost is that the fake's own contract is uncovered (`LastPlayerMove` throwing when empty, `Clear`), exactly as `RecordingEvents.Clear()` has been since M0-03. Worth rows if either fake grows.

**`Tick_AllocatesNothing` had to grow the recorder before measuring**, and the workaround is load-bearing rather than superstition — mutation-tested. `RecordingIntents` keeps every intent, so its `List<T>` doubles its array ~14 times across the measured window; the test ticks 20 000 times first and then clears, since `Clear` keeps capacity. Removing that pre-grow turns the test red on a session that allocates nothing, which would read as a defect in `Tick`. The real sink, `IntentBuffer`, keeps one intent and has no array to double.

Mutation-tested, since the first run was green (M0-07's rule). Flipping publish and the `IsRunning` assignment in `Start` reddened `Start_EventPublishedBeforeIsRunningFlips` alone; raising `IsRunning` before the catalog lookup reddened `Start_UnknownCharacter_Throws_NotRunning` alone; `State.Time = snapshot.Dt` instead of `+=` reddened `Tick_AdvancesTime_AndStoresPosition` and `End_PublishesRunEnded_WithTime`; a `new object()` per tick reddened `Tick_AllocatesNothing` and nothing else — which is what proves the allocation probe is live through this path rather than passing by default.
