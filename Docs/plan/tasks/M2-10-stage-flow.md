# M2-10 — `StageFlow`: a stage that arrives, seals, clears, and lets you leave through a door

**Size:** M · **Depends on:** M2-05 (`IsStageComplete`), M2-04, M2-02 · **Branch:** `m2-10-stage-flow`
**Design refs:** GD §7.1 (anatomy of a stage), §7.3 (the commute unit), §4.4, §12.1–12.3; AR §5, §18.1, §18.2 · **Ledger rows:** none directly; it retires the last of M1-19's stand-in spawner

## Goal

A run is more than one stage: the arena seals, the waves come, the last body drops, a door opens, and walking through it puts the player one depth further down with the budget, the curves and the arena that go with it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Stage/StageFlow.cs` | Core | The five-state machine, the stage boundary, and what it resets |
| `Core/Events/StageEvents.cs` | Core | `StageArrived`, `StageCleared`, `StageTransitionStarted` (grouped) |
| `Tests/Core/Stage/StageFlowTests.cs` | Tests.Core | Every rule below |
| *small edits* | | `WorldSnapshot` + `GatePosition` and `HasGate`; `SnapshotBuilder` fills them from a serialized gate marker; `RunScope` gains that marker and **loses** `_keepAlive`, `_respawnDelay` and `_minSpawnDistance`; `RunSession.Start` composes a `StageFlow` and `Tick` gains a step after the director; `RunState.StageIndex` advanced through its `internal set`; `HudPresenter` fades on `StageTransitionStarted` and back in on `StageArrived`; `DebugOverlay` shows the phase and the gate; **AR §18.1**'s ordering table gains the stage step |
| *deletions* | | `Core/Run/RespawnPolicy.cs` and `Tests/Core/Run/RespawnPolicyTests.cs`; `EnemySystem.ApplyRespawn`; `SpawnPlan.Respawn`; M2-02 rule 7's `RespawnPolicy.SpecId` validation branch. **The owner approves the deletion at review** — see rule 16 |
| *ripple* | | `RunSessionTests`' ordering rows gain the stage step; `SpawnPlan`'s constructor loses an argument, so every fixture that builds one changes — the compiler enumerates them |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Events;

/// A stage has begun. Published on entering `Arrival`, for **every** stage including the first —
/// so the one handler that dresses an arena has one event to listen to (rule 4).
public readonly struct StageArrived
{
    public readonly int Stage;
    public readonly ContentId ArenaId;   // default until M2-11a fills the roster (rule 5)
}

/// Every body the stage spawned is dead. The barrier drops and the door opens.
public readonly struct StageCleared
{
    public readonly int Stage;
    public readonly Vector3 GatePosition;
    public readonly ContentId NextArenaId;   // what to prepare, unrendered, during this beat (rule 5)
}

/// The player has stepped into the door. The screen has `Duration` seconds to cover itself before
/// the next `StageArrived` swaps the world underneath it (rule 9).
public readonly struct StageTransitionStarted
{
    public readonly int Stage;
    public readonly float Duration;
}
```

```csharp
namespace Soulvail.Core.Stage;

public enum StagePhase
{
    Arrival,      // 2 s. The barrier seals, the number shows, nothing spawns yet.
    Waves,        // the director runs
    Clear,        // the last body is down; barrier drops, door opens, next arena prepared
    Gate,         // waiting for the player to walk into the door. No timeout.
    Transition,   // the screen is covering itself. One beat, then the next Arrival.
}

public sealed class StageFlow
{
    public const float ArrivalTime = 2f;        // GD §7.1
    public const float ClearTime = 1.5f;        // rule 7 — the one number here with no design source
    public const float FadeTime = 0.3f;         // rule 9
    public const float GateReachRadius = 1.5f;  // metres, XZ (AR §18.4)

    public StageFlow(
        ModeSpec mode,
        WaveComposer composer,
        SpawnDirector director,
        EnemySystem enemies,
        ProjectileSystem projectiles,
        PlayerCombat player,
        IDomainEvents events,
        WavePlan plan,
        int seed);

    public StagePhase Phase { get; }
    public int Stage { get; }
    public float PhaseElapsed { get; }

    /// True once a finite mode has cleared its final stage — the run is over and the caller ends it.
    public bool IsModeComplete { get; }

    /// Opens `stage` at `now`: composes its waves and enters `Arrival`. Called once, by `RunSession.Start`.
    public void Begin(int stage, float now, IRandomStream spawn);

    /// One frame of the stage's own life. Never called before `Begin`.
    public void Tick(float now, in WorldSnapshot snapshot, IRandomStream spawn);
}
```

```csharp
namespace Soulvail.Core.Run;

// WorldSnapshot (added) — the arena's standing geometry, the only part of it core can see.
/// Where the door out of this arena is, in world metres. Meaningless when `HasGate` is false.
public Vector3 GatePosition;

/// This arena has a door. False in a scene dressed without one, which parks the flow in `Gate`
/// rather than throwing — rule 12, and M2-05 rule 12's bargain for the same reason.
public bool HasGate;
```

## Behaviour

**The five phases**

1. **`Arrival` lasts `ArrivalTime` and nothing spawns during it.** GD §7.1's opening beat, and the one M2-05 rule 1 deliberately refused to insert into the director: *"inserting a delay here that nothing seals or announces would be a pause with no cause."* This is the thing that seals and announces, so the pause now has one. `Begin` and every later boundary publish `StageArrived(stage, arenaId)` on entering it.
2. **`Waves` begins by handing the director its plan.** Entering it calls `SpawnDirector.Clear()` then `Begin(plan, now)`, in that order, so a stage never inherits the previous stage's claims or pending telegraphs. The director's first tick lands on the **next** frame, because the flow ticks after it (rule 13) — one frame of delay against a 2 s arrival, named rather than discovered.
3. **`Waves` ends when `SpawnDirector.IsStageComplete` is true**, which is the whole handover M2-05 rule 6 left open: the director publishes nothing about a stage ending because *"the stage is M2-10's to end"*, and this is where it is ended.
4. **`StageArrived` is published for the first stage too, not only for boundaries.** One event, one handler, one code path for dressing an arena — the alternative is a `RunStarted` branch on the Unity side that does the same work twice and drifts. It is also why the arena id is on this event rather than on a separate advance event: by the time anything needs to know which arena to raise, `StageArrived` has already said so.
5. **`StageCleared` carries the *next* arena's id, and that is the owner's rendering rule made into a payload.** The next stage must not be visible or rendered before the player reaches it, so nothing of it exists during `Waves`; the `Clear` beat is the window in which it may be built, inactive and unlit, ready for a swap that happens behind a covered screen. Core is the only thing that knows which arena is next (rule 6), so it says so at the only moment the answer is useful. Until M2-11a both ids are `default(ContentId)` and nothing listens — M2-05's `SpawnTelegraphed` shipped the same way, for the same reason.
6. **Which arena a stage uses is `mode.ArenaFor(stage, seed)` — a pure function of the seed and the depth, never a draw.** M2-11a adds the member; this task calls it and returns `default` while the roster is empty. Deriving it rather than drawing it means a run resumed at stage 7 lands in the arena stage 7 always had, with **no saved stream position and no dependence on ledger row 1**. Rejected: a `spawn.NextInt` draw, which makes arena identity a function of how many spawn positions the previous six stages happened to reject, and re-rolls the arena on every resume until row 1 is fixed.
7. **`Clear` lasts `ClearTime`, and 1.5 s is the one number in this spec that no design document gives.** GD §7.1 lists the barrier dropping and the gate materialising as a step of a stage without pricing it. Zero would mean the door pops open on the frame the last enemy dies, which reads as the game skipping its own beat; this is a pacing knob for the owner to tune against the 40–75 s stage of GD §7.3, and it is flagged here rather than presented as derived.
8. **`Gate` waits for the player and never times out.** The transition begins when the player's XZ distance to `snapshot.GatePosition` is at or inside `GateReachRadius` (AR §18.4 — the height between a player capsule's centre and a door's anchor is a rendering detail). No trigger collider and no fact: the answer does not depend on a collider core cannot see, so M2-07a rule 1 says core decides it. `IRunSession` gains nothing.
9. **`Transition` is `FadeTime` of core-owned clock, and it exists so that nothing outside core has to tell core when a fade finished.** Entering it publishes `StageTransitionStarted(stage, FadeTime)`; the HUD darkens over exactly that many seconds and clears again on the next `StageArrived`, so the arena swap of M2-11a happens under a screen that is already opaque. The alternative — Unity reporting "the fade is done" — would be a command on the input channel whose only content is the passage of time core is already measuring.

**Crossing the boundary**

10. **What advances, in this order:** `Stage` and `RunState.StageIndex` move to the next depth → `EnemySystem.Depth` is set to it (M2-03 rule 12, so nothing can spawn an unscaled enemy) → `EnemySystem.Clear()` → `ProjectileSystem.Clear()` → `player.Targeter.Reset()` → the plan is recomposed for the new stage → `Arrival`. Depth before the clears, because a body spawned by anything reacting to the clears must already be scaled.
11. **A bolt in the air does not follow you through the door**, which is what `ProjectileSystem.Clear()` is doing on that list: a shot fired at the old arena's floor, landing after the swap, would damage the player at coordinates that no longer mean anything. `EnemySystem.Clear()` is belt and braces — the stage is complete, so nothing is alive — but a corpse still awaiting its despawn frame is exactly the kind of thing that survives a boundary and stands in the next arena.
12. **`player.Targeter.Reset()`, and deliberately *not* `player.Reset()`.** A new stage must not heal: `PlayerCombat.Reset` calls `Health.Reset`, and a free refill at every boundary would delete the attrition GD §12.5's death horizon is made of, and pre-empt the Sanctum's heal (GD §13.3) before the Sanctum exists. What must go is the target — an id from the arena that was just torn down — so the narrow reset is the right one and the wide one is a trap worth naming.
13. **A run that ended this tick does not advance a stage.** `RunSession.Tick` becomes `… → projectiles → (dead? end) → director → stage flow → motor → intent`: after the death check for that reason, and after the director because the flow reads `IsStageComplete`, which the director has just this instant finished deciding. An addition to AR §18.1's table, composing with M2-05 rule 14 and M2-07a rule 10 without moving either.
14. **A finite mode that clears its final stage sets `IsModeComplete` and stays in `Clear`.** `mode.HasStage(stage + 1)` is the test (M2-02 rule 3). Descent is endless so this is inert in V1 — and it is written anyway, because `ModeSpec.FinalStage` exists and the alternative is a run that walks through a door into a stage its own mode says it does not have. `RunSession` reads the flag and ends the run; nothing here publishes `RunEnded`, which is the session's word.
15. **`HasGate` false parks the flow in `Gate`, visibly.** M2-05 rule 12's bargain, for its reason: a scene dressed without a door is the M0 grey box and every core test that never intends to leave stage 1, and neither should throw. The cost — a run that silently cannot advance — is bought back in the Editor, where `DebugOverlay` reads `gate: —`.

**Retiring the stand-in**

16. **`RespawnPolicy` is deleted, not deprecated.** M2-05 rule 15 set `keepAlive` to 0 and left the type in the codebase because *"deleting it here would remove the only spawner the grey-box scene has for the length of one PR"*. That PR is over: the director spawns, the flow paces, and a second spawner that keeps twelve bodies breathing regardless of the wave plan is now a bug waiting for someone to set the field back to 12. Going with it: `EnemySystem.ApplyRespawn`, `SpawnPlan.Respawn`, `RunScope`'s three serialized fields, M2-02 rule 7's validation of `RespawnPolicy.SpecId`, and `RespawnPolicyTests`. **Deleting files needs the owner's word (CLAUDE.md, *Ask before*), so this is a spec that asks rather than a change that assumes.**
17. `StageFlow` allocates nothing per tick: the `WavePlan` is the one M2-04 allocates once per run and refills per stage, the state machine is built with the flow, and the phase test is struct arithmetic over fields.

## Tests

| Test | Given / When / Then |
|---|---|
| `Begin_EntersArrivalAndAnnouncesIt` | stage 3 / `Begin` / `Phase` `Arrival`, one `StageArrived(3, …)` (rules 1, 4) |
| `Arrival_SpawnsNothing` | `Begin`, a plan with wave 1 due / `Tick` ×to 1.9 s / no `WaveStarted`, no telegraph |
| `Arrival_EndsAtTwoSeconds` | / `Tick` to 2.0 s / `Waves`, one `WaveStarted` on the following tick (rules 1, 2) |
| `Waves_ClearsTheDirectorBeforeBeginning` | a director mid-stage with a pending telegraph / a boundary into `Waves` / `PendingCount` 0 before wave 1 (rule 2) |
| `Waves_EndOnStageComplete` | last wave, one alive / `Tick` / `Waves`; it dies / `Tick` / `Clear`, one `StageCleared` (rule 3) |
| `Cleared_CarriesGateAndNextArena` | stage 3, gate at (0, 0, 18) / clear / `StageCleared.GatePosition` is the snapshot's, `NextArenaId` is `mode.ArenaFor(4, seed)` (rules 5, 6) |
| `ArenaFor_IsSeedAndStageOnly` | two flows, same seed, one having consumed 40 spawn draws / both at stage 4 / the same `NextArenaId` (rule 6) |
| `Clear_LastsClearTime` | in `Clear` / `Tick` at +1.4 / `Clear`; at +1.5 / `Gate` (rule 7) |
| `Gate_WaitsIndefinitely` | in `Gate`, player 8 m from the door / `Tick` ×600 / still `Gate`, no events (rule 8) |
| `Gate_TripsInsideTheRadius` | player 1.4 m from the door / `Tick` / `Transition`, one `StageTransitionStarted(stage, 0.3)` |
| `Gate_IsDecidedOnXz` | player 1 m away on XZ and 4 m above / `Tick` / `Transition` (AR §18.4) |
| `Transition_AdvancesAfterFadeTime` | in `Transition` / `Tick` at +0.29 / `Transition`, no `StageArrived`; at +0.3 / `Arrival`, `StageArrived(4, …)` (rule 9) |
| `Advance_MovesStageAndDepth` | stage 3 / a full boundary / `Stage` 4, `RunState.StageIndex` 4, `EnemySystem.Depth` 4 (rule 10) |
| `Advance_SetsDepthBeforeClearing` | a spy on `EnemySystem` / a boundary / `Depth` was already 4 when `Clear` was called (rule 10) |
| `Advance_ClearsProjectilesInFlight` | two bolts due after the boundary / cross it / `InFlightProjectiles` 0, no `PlayerDamaged` (rule 11) |
| `Advance_ClearsCorpses` | a body dead but not yet despawned / cross / nothing registered in the new stage |
| `Advance_ResetsTheTargetOnly` | player at 40 HP with a target / cross / `CurrentTargetId` −1 and **HP still 40** (rule 12) |
| `Advance_RecomposesThePlan` | stage 3's plan / cross / the plan's `Stage` is 4 and its budget is `B(4)` (rule 10) |
| `Advance_ReusesTheOnePlan` | warm-up / 20 boundaries / no allocation attributable to the plan (rule 17, M2-04's refill) |
| `Session_TicksFlowAfterDirectorBeforeMotor` | a stage completing on the same tick a spawn is due / `Tick` / the director ran first, the motor still wrote its intent (rule 13) |
| `Session_DeadPlayerDoesNotAdvance` | player dies on the tick the stage completes / `Tick` / `RunEnded`, `Phase` unchanged, no `StageArrived` (rule 13) |
| `Mode_FinalStageCompletesTheMode` | mode final 3, stage 3 cleared / `Tick` / `IsModeComplete` true, `Phase` `Clear`, no `StageArrived` (rule 14) |
| `Mode_EndlessNeverCompletes` | Descent, stage 60 cleared / cross / `IsModeComplete` false (rule 14) |
| `Gate_NoGate_ParksInGate` | `HasGate` false / clear, `Tick` ×600 / `Gate`, no throw, no `StageArrived` (rule 15) |
| `Session_NothingRespawns` | a stage whose plan is exhausted and whose bodies are all dead / `Tick` ×600 / nothing spawned and `Clear` is reached — the one behavioural assertion that the second spawner is gone (rule 16) |
| `Tick_AllocatesNothing` | a stage mid-`Waves`, warm-up / 10 000 × `Tick` / allocated-bytes delta == 0 |
| `Tick_DrawsNoRandomOutsideComposition` | a `FixedRandom` that throws on any draw, a plan already composed / `Tick` through `Arrival`, `Gate`, `Transition` / no draw |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Play the Run scene. The arena is quiet for two seconds, the stage number shows, then wave 1 arrives. Nothing spawns during the pause — that is rule 1, and the thing M2-05 could not give you.
2. **[Editor]** Clear the stage. The last body drops, a beat passes, the door opens. Walk into it: the screen darkens, and you are standing in stage 2 with the overlay reading `depth 2`. Enemies are measurably tougher, which is rule 10's `Depth` having been set before anything spawned.
3. **[Editor]** Stand still after clearing and do not walk into the door. Nothing happens, for as long as you like — rule 8, and the reason a stage is GD §7.3's commute unit rather than a timer.
4. **[Editor]** Let a Spitter release a bolt, then walk into the door before it lands. You arrive undamaged (rule 11).
5. **[Editor]** With `Keep Alive` gone from `RunScope`, kill everything and wait: nothing trickles back in. That is the whole visible content of rule 16, and the one way a surviving second spawner would show itself.
6. **[device]** Whether 2 s of arrival and 1.5 s of clear read as *beats* or as *waiting* on a phone, in a 40–75 s stage. Both constants are tuning knobs; this is the session that sets them. Deferred with the rest of the device list.

## Out of scope

- **The arena itself** — M2-11a. This task reads one gate position off the snapshot and knows nothing else about the world it is in; `RunScope` carries a serialized gate marker until an `ArenaView` supplies one.
- **The Sanctum**, which GD §7.1 puts between Clear and Gate. It is M6-02/03, and there is no economy, no shop and no heal to put in it. When it lands it becomes a sixth phase between `Clear` and `Gate`, which is why `Clear` is a state rather than an instant.
- **Essence auto-collecting on clear** (GD §7.1 step 3) — M6-01, nothing drops yet.
- **Persisting the boundary.** GD §7.3 wants run state written to disk at every stage boundary and that is exactly this moment, but the DTO does not exist until M2-13 and the write is M2-14's. Nothing is stubbed for it; `StageCleared` is the event it will hang on.
- **A stage timer or a par time.** GD §7.3's 40–75 s is a design target for the budget curve, not a clock the game runs.
- **Off-screen arrows for the last survivors** — M2-12a, which owns GD §7.3's 8-second stall rule. No wave timeout is introduced here to paper over it.

## As built

**Built as specified in behaviour: all fifteen rules hold, and the five phases run on the clock the spec gives them.** `RespawnPolicy` is gone, the owner approved the deletion up front (rule 16), and the manual steps no longer need `Descent.asset`'s starting stage edited — a run reaches stage 2 by playing.

**Ten deviations. Two change a signature, one changes what a row can assert, and the rest are the Files table being narrower than the work.**

1. **`Begin` adopts the opening stage's composition instead of making it, and so does not take the `Spawn` stream.** The spec's `Begin(int stage, float now, IRandomStream spawn)` composes. Every way of arranging that costs something M2-02 already paid for: composing here and calling `Begin` *before* `RunStarted` publishes `StageArrived` into a run that has not been announced, and calling it *after* lets an ineligible mode throw with the run announced and half an arena standing — which is **ledger row 3**, the exact bug M2-02's ordering exists to prevent. Composing here *as well* would draw the Spawn stream twice for the opening stage, for a composition the run then discards. So `RunSession.Start` keeps its one pre-`RunStarted` `Compose` as the validation it already is, and `Begin(int stage, float now)` adopts it. **`Tick` still takes the stream**, because a *boundary* composes and that is where the draw belongs — which is also what `Tick_DrawsNoRandomOutsideComposition` asserts on both sides.
2. **`Tick` takes `WorldSnapshot` by value, not `in`.** `WorldSnapshot` is a class (its own remarks say why), so `in` on it is a readonly reference to a reference — meaningless, and unlike every other core method that takes one: `PlayerCombat.Tick` and `EnemySystem.Ingest` both take it by value. Cosmetic; no behaviour rides on it.
3. **`ModeSpec.ArenaFor` was not added, and the seam is `StageFlow.ArenaFor(int stage)` instead.** Rule 6 says "M2-11a adds the member; this task calls it" — which cannot both be true, and `ModeSpec.cs` is not in the Files table. So the *shape* is here as a private method that reads the seed and the depth and answers `default(ContentId)`, documented as the call M2-11a moves onto `ModeSpec`. `ArenaFor_IsSeedAndStageOnly` therefore passes trivially today; it is written anyway, because it is the row that fails the moment somebody makes the arena a draw.
4. **`Advance_SetsDepthBeforeClearing` became `Advance_SetsDepthBeforeAnythingCanSpawn`, and asserts the consequence rather than the order.** The spec asks for "a spy on `EnemySystem`". `EnemySystem` is `sealed`, both writes have happened by the time the caller gets control back, and nothing the clears do publishes anything to hang an observer off — so there is no spy to write. What *is* observable is the thing the ordering guarantees: the first body that can exist in the new arena is priced at the new depth. The row spawns one either side of a boundary and compares its `MaxHp`, and a wrong order fails it with a stage-3 Husk.
5. **`RunState.StageIndex` is moved by `RunSession`, not by `StageFlow`.** The flow's constructor takes no `RunState` (the spec's own Public API), so it cannot reach the setter. `RunSession.Tick` copies `State.StageIndex = _flow.Stage` immediately after ticking it — one int write a frame, no allocation — and `Session_AdvancesRunStateStageIndex` is the row for it. The two numbers stay a deliberate copy for the reason `EnemySystem.Depth` is one.
6. **Two assets were dressed that the Files table does not name, because the two rules it does name are invisible without them.** `Run.unity` gains a **`Gate`** object at (0, 0.05, 17) — a collider-less slab against the north wall — wired to `RunScope._gate`; it is a **scene root rather than a child of `GreyBox.prefab`**, deliberately, because a renderer inside that prefab is geometry its `NavMeshSurface` may collect and re-baking a navmesh is not this task's to do. `Hud.prefab` gains a **`Fade`** panel (full-screen black `Image` + `CanvasGroup`, alpha 0, raycasts off) wired to `HudPresenter._fade`. The `Run.unity` diff is 95 added lines against **3 removed — exactly the three deleted `RunScope` fields** — with nothing re-serialised behind them (Traps §5).
7. **`SnapshotBuilder`'s constructor gained a `Transform gate`**, and `SnapshotBuilderTests` gained three rows for it (reported every frame; absent; destroyed mid-run, which is Unity's lifetime `!=` rather than C#'s). The spec's table says the builder "fills them from a serialized gate marker" without saying through what.
8. **`SpawnDirectorTests`' four session rows changed, which the ripple row did not anticipate.** Wave 1 no longer starts at `Start` — it waits out the arrival — so three rows gained a `TickThroughArrival` and one (`Session_NoSpawnPoints_DirectorInert`) had its tick count raised past two seconds, or it would have been asserting rule 1 rather than rule 12. `Session_TicksDirectorAfterDeathCheck` needed more: its Husk stood at 1 m and would now kill the player *during* arrival, ending the run on a tick no wave had ever been due on. It is dressed at 12 m and **the player walks onto it**, because an enemy in a core fixture never moves — a move is an intent, and only a body reporting back through the snapshot changes a position.
9. **`HudPresenter`'s fade runs on `Time.deltaTime`, not on core's clamped `dt`.** The divergence only ever errs safe: core's step is clamped to `SnapshotBuilder.MaxDt`, so on a hitching frame simulated time advances *more slowly* than the wall clock and the cover is already opaque when the swap happens. The other way round would show the player the arena being replaced.

10. **`InstallerTests` gained one row, `Run_NullByNameParameter_Resolves`.** `RunScope` passes the gate by name whether or not it is there, and VContainer never falls back to a C# default — so an *omitted* parameter fails to compose the run. Whether a **null** one is a value or an absence decides whether pressing Play in an undressed Run scene still works, which is the workflow every optional field on that scope exists to protect. It resolves; the row is what stops that being an assumption.

**`ClearTime` = 1.5 s remains the one number here with no design source (rule 7), and it is a knob rather than a finding.** GD §7.1 lists the barrier dropping and the gate materialising without pricing them. It is flagged in the constant's own remarks and it is the device session's to set, against the 40–75 s stage of GD §7.3 — manual step 6.

**One thing the fixture had to learn, and it is not in any doc yet:** a phase boundary asserted on the exact constant fails by float accumulation. The clock is a `float` summed a step at a time, so by the twentieth stage of `Advance_ReusesTheOnePlan` a sum that should read 1.500000 reads 1.499999. Every helper here steps *past* a boundary by a frame and the rows that care about *when* a phase ends check the tick before it as well.

**One bug found by the owner's playtest and fixed in this branch.** Walking out of stage 4 threw `IndexOutOfRangeException` from `SpawnDirector.SweepTheDead` and the run stopped. **`Advance` recomposed the run's one `WavePlan` while the director was still holding it** — `RunSession` ticks the director every frame, including through the two seconds of arrival that follow a boundary, and the director sizes its per-wave arrays from the plan's dimensions at `Begin` and trusts them thereafter. GD §12.2's W(n) goes from two waves to three at **stage 5**, so the first frame of that arrival walked `w < _plan.WaveCount` off the end of arrays sized for stage 4. **The fix is one line: `Advance` calls `_director.Clear()` before recomposing**, so the director holds no plan for the whole of Arrival and its `Tick` is a no-op — which also makes rule 1 true by construction rather than by this object happening not to have begun it yet. `EnterWaves` keeps its own `Clear()`; rule 2 states it, and it is now genuinely belt-and-braces.

**Why the suite missed it, which is the part worth keeping:** every row in `StageFlowTests` composed from a *flat* mode — one wave, a fixed concurrency — because a stage whose contents are stated rather than derived is what makes a pacing assertion readable. A flat plan never changes shape across a boundary, so the mutation was invisible. Two rows now use GD §12's real curves and nothing else does: `Advance_ClearsTheDirectorBeforeRecomposing` crosses 4 → 5 and ticks the whole arrival, and `Advance_SurvivesManyBoundariesOnTheRealCurves` runs six stages. **Both were confirmed to fail against the unfixed code with the owner's exact exception** before the fix went back in. The invariant is now written at `SpawnDirector.Begin` and in **AR §18.1**, because it is the director's rule rather than the flow's: *nobody may recompose a plan a director is still holding.*

**Verified:** 792 EditMode green, 0 failed, 0 skipped, 9.4 s; PlayMode 7 green, 0 failed, 4.5 s. Zero compile errors, zero new analyzer warnings, seven Console warnings (the authoring fixtures' own, none new), no `ProjectSettings/` drift.
