# M2-15a — `Physics.SyncTransforms()`: make the frame order true of the code, not only of the call order

**Size:** S · **Depends on:** M2-15 · **Branch:** `m2-15a-physics-sync`
**Design refs:** AR §18.1 (ordering), AR §4.3 (the frame), GD §12.4 (the fairness contract) · **Ledger rows:** M3 row 3 — this closes it

## Goal

A swing resolves against the arena as it stands *this* frame. Today it does not, and the gap has been visible as an intermittent PlayMode failure since M2-12a.

## Why this is a task and not a line

M2-15's Files table says **no code**, and *"a bug found here becomes `M2-15a…` with its own PR."* This is that. The owner ruled on [M3 ledger row 3](../ROADMAP.md#carry-forward-into-m3) directly: **add the line**, and treat the frame cost as a measurement to take on the first real phone.

## The fault

`Physics.autoSyncTransforms` is **0** for this project. Moving a `Transform` therefore does **not** move the collider the physics scene holds until something flushes it. `RunTicker.Tick` does this:

1. `_player.Apply` and `ApplyEnemyMoves` — write transforms
2. `StepCharge` → `ChargeMotion.Sweep` — asks physics
3. `ResolveConeHits` → `ConeOverlapQuery` — asks physics

Steps 2 and 3 see step 1's *previous* positions. **A cone misses an enemy that stepped into it this frame**, which is precisely what AR §18.1's ordering row exists to prevent — the order was honoured by the sequence of calls and not by the state the calls read.

It presented as flakiness rather than as a bug because the staleness only matters when a body crosses the cone boundary within one frame: `FrameOrderTests` gave **9/11 then 11/11 on one unchanged tree** at M2-15, and different counts at M2-12a, M2-13b and M2-14a. Four tasks read that as an unreliable fixture.

## Files

| Path | Purpose |
|---|---|
| `Assets/_Project/Game/Composition/RunTicker.cs` | The call, at the seam, plus the `Tick` remarks that previously described an ordering the code did not honour |
| `Docs/Architecture.md` | §18.1 gains the invariant as its own row; the frame-order row gains **sync** |
| `Docs/plan/PROGRESS.md` · `Docs/plan/ROADMAP.md` | Entry, Current State, M3 ledger row 3 struck |

No test file. See *Coverage*.

## Behaviour

1. **One `Physics.SyncTransforms()`, after `_telegraphRings.Step` and before `StepCharge`.** That is the seam: everything above moves bodies, everything below asks physics about them.
2. **One call serves both queries.** Nothing between `StepCharge` and `ResolveConeHits` writes an enemy transform. `ChargeMotion` moves the *player* through `CharacterController.Move`, which is a physics call and carries itself; the colliders both sweeps are asked about are the enemies', flushed once above.
3. **`ApplyKnockbacks` stays last.** It moves enemies again, after every question has been asked — so it needs no flush of its own, and the next frame's call covers it.
4. **The project setting is not changed.** `autoSyncTransforms = true` would pay the same cost on every transform write in the game; this pays it once, at the only point that asks physics a question.
5. **The three adapter fixtures keep their own `SyncTransforms` calls.** `ConeOverlapQueryTests`, `LineOfSightSenseTests` and `SnapshotBuilderTests` move transforms directly rather than through `RunTicker`, so this line is not theirs to inherit — one of them says so in a comment already.

## Coverage

**No new test, and that is the point of the ones that exist.** `Tests/PlayMode/FrameOrderTests.cs` was written at M2-11b to pin this ordering *by observation*, and its two failing rows — `Ticker_ReportsFactsAfterBodiesMoved` and `Ticker_RunsTheStepsInOrder` — are exactly the assertion this fixes. A new test would restate them.

The evidence is **repetition**, because the failure was intermittent: a single green run proves nothing.

## Acceptance

- [x] `FrameOrderTests` 11/11 on **three consecutive PlayMode runs** — 4.3 s, 4.0 s, 3.4 s
- [x] EditMode 1077 passed / 0 failed / 0 skipped, 13.8 s — no regression
- [x] Zero compile errors, zero new analyzer warnings
- [x] `git diff ProjectSettings/` empty — the fix is code, not a setting
- [x] M3 ledger row 3 struck, naming this task

## Out of scope

- **Measuring the cost.** It is a flush of the frame's dirty transforms, paid once per frame, and it has never been measured on a phone because there is no phone. Stays in PROGRESS → *Deferred — device-only*.
- **A narrower flush** — syncing only when a cone or sweep is actually pending. Cheaper in principle, conditional in practice, and optimising against an unmeasured cost is how the original bug's cousin gets written. Revisit with a profiler and hardware.
- **The three fixtures' own calls.** Rule 5.

## As built

**As specified.** One line and its comment, one `Tick` remarks paragraph, one new AR §18.1 row.

**One thing the ruling did not say and this task had to decide: where exactly.** "Between the bodies step and the fact phase" has two candidate seams, because `_projectileViews.Step` and `_telegraphRings.Step` sit between them. It went **after** both, immediately above `StepCharge` — they are the frame's two purely cosmetic steps, touch no collider and ask physics nothing, so putting the flush above them would have made the two of them look like part of the physical half of the frame when they are explicitly not (M2-09 rule 3, M2-12b rule 4).

**Verified:** PlayMode **11/11 three times running** against 9/11 immediately before the change; EditMode **1077 / 0 / 0**; Console clean apart from the expected negative-path logs; `ProjectSettings/` untouched.
