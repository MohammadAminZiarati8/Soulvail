# M6-11e — Two rows that assert a premise rather than a behaviour

**Size:** S · **Depends on:** M6-11 · **Branch:** `m6-11e-two-rows-that-assert-a-premise`
**Design refs:** AR §18.1 · **Ledger rows:** [M7 row 8](../ROADMAP.md#carry-forward-into-m7) — M6 row 4 and known issue 7

## Goal

The project's frame-order row asserts what it means, and the animator row stops asserting the Editor's
state — so neither can fail without the code having changed.

## The two rulings, made at M6-11

**`FrameOrderTests.Ticker_RunsTheStepsInOrder` step 5 asserts *the seam is synchronous*.** Its claim
is AR §18.1's flush: when facts are answered, the physics scene holds each body where this frame's move
put it. The wedge is the wrong probe for that — it adds a zero-margin geometric claim the row never
meant to make, and the M5-05a instrument answered the same way every time it fired (M5-06a, M5-08a,
M6-04, M6-05a, M6-09a): ***wrong wedge***, the body **0.0001 m behind the apex**. So step 5 reads the
sync directly, with the fact-time overlap `Ticker_MinionsMoveBeforeTheFlush` already uses. **What that
costs, stated:** step 5 no longer proves the *cone query* resolves against moved bodies on frame two;
that stays `Ticker_ReportsFactsAfterBodiesMoved`'s claim on frame one, which has never flaked — and if it
does, the same zero-margin apex is the first suspect.

**`PlayerAnimatorViewTests.Animator_AttackSpeedIsUnreachableFromAnEditorClock` asserts Editor state.**
`Time.time` is 0 in an Editor that has not entered Play since launch and 0.73 in one that has, and it
does not reset on a reimport — a **false premise** where row 4 was a **margin**. The row's own message
already names the replacement: prime `_lastAttackTime` below `Time.time` and assert the ratio.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/PlayMode/FrameOrderTests.cs` | Tests.PlayMode | Step 5 becomes a fact-time overlap; `DiagnoseTheEmptyReport` and its control row retire with the cone assertion they diagnosed |
| `Tests/Game/Views/PlayerAnimatorViewTests.cs` | Tests.Game | The premise becomes `Assume`; the ratio is asserted when the clock has moved |

## Behaviour

1. **Step 5: at fact time, an overlap of `ProbeRadius` at the body's moved position finds its
   collider, and one at the snapshot position finds nothing.**
2. **Steps 1–4 and 6 are unchanged**, and so is every other row in the file.
3. **The animator row never goes red on the Editor's clock**: `Assume.That(Time.time, Is.Zero)` guards
   the unreachable half, and a sibling row asserts the multiplier whenever `Time.time > 0`.
4. **PlayMode is run ten times** for the hand-over, not twice — the only honest evidence a
   one-in-ten flake is gone.

## Tests

| Test | Given / When / Then |
|---|---|
| `Ticker_RunsTheStepsInOrder` | step 5 rewritten per rule 1 / ten passes / ten green |
| `Animator_AttackSpeedFollowsTheSwingRatioWhenTheClockRuns` | `Time.time > 0`, `_lastAttackTime` primed below it / a swing / the multiplier, clamped at 6 |

## Out of scope

- **`RunTicker` itself.** Nothing in the frame is wrong; the instrument said so every time.

## As built

**Rules 1, 2 and 4 as written; rule 3's premise was wrong, and its rows are right anyway.** Step 5
hooks `OnFactTime` on frame two and probes `ProbeRadius` for the body's trigger capsule at
`_body.Position` (found) and at the snapshot position (nothing). `TheSweepFinds` takes a `Collider`,
so the minion row and step 5 share it. Steps 1–4 and 6 and every other row are unchanged.
`DiagnoseTheEmptyReport`, `Ticker_AnEmptyConeReportIsDiagnosed`, `LateSync` and `WrongWedge` are
gone. The animator zero row `Assume`s its clock, and
`Animator_AttackSpeedFollowsTheSwingRatioWhenTheClockRuns` primes the previous swing at half the
clock (exact in binary). It asserts the clamped ratio, then clears the parameter and asserts the
ceiling on a swing twelve times too quick.

**Before building: M6-11d had never been opened as a PR** (M6-11d's own *As built* records the same of
a to c). On the owner's go it was opened and merged as #161, and this branch was cut from the result.

**Four deviations.** None changes a decision.

*1. The Editor clock does not move on entering Play.* The spec says 0.73 s *"in one that has"*. Here
it read **0 after ten PlayMode passes** and 0 on leaving a hand-entered Play at 35.5 s. It moved only
after the owner put the Editor in front: **1.83 s**. What else resets it was not pinned: one test
run saw 0 where a command had read 1.94 s a moment before, another 0.62 after 1.88. The remarks
state only what was measured. Rule 3 stands, since exactly one of the two rows runs whatever the
clock reads, but witnessing the sibling took **two focus requests** → [Traps §3](../../Traps.md).

*2. The instrument's other half retired with it.* `RecordingCore.ConeOffset`, `LastCone` and
`WroteCone` existed only for the diagnosis, and `_cone` goes back to a local. One comment in
`Frame_TheLevellingTickCompletes` named step 5's cone as the project's known flake and was
corrected.

*3. Step 5 writes its walk with `TestContext.WriteLine`*, the idiom `ContentValidationTests` uses, so
each pass's `TestResults.xml` carries the margin rule 4 is evidence of.

*4. The counts change shape.* PlayMode goes 26 → 25 (the control row). EditMode goes 3 159 → 3 160
with **one row inconclusive on every pass, by construction**, so the chain now reads *passed / failed /
inconclusive*.

**Stated cost: the negative half is a frame-time margin.** A body at 60 m/s clears its 0.5 m capsule
plus the 0.1 m probe only on a frame longer than 10 ms. Across eleven passes and a red check it
walked **0.949–1.031 m**, frames of 16–17 ms. `Ticker_ReportsFactsAfterBodiesMoved` already needed
8.3 ms. An Editor running at 100 fps would fail it with a message naming the walk, which is a
different failure from the wedge's, and a readable one. The cone on frame two is no longer asserted
(the spec's cost), and that claim stays on frame one.

**Rules ↔ rows.** 1, 2: `Ticker_RunsTheStepsInOrder`. 3: `Animator_AttackSpeedIsUnreachableFromAnEditorClock`
on a zero clock, `Animator_AttackSpeedFollowsTheSwingRatioWhenTheClockRuns` on a moved one. 4: ten
PlayMode passes, below.

**Red checks, both restored from git and recompiled before the final passes.** *A*, with
`Physics.SyncTransforms()` removed from `RunTicker`: `FrameOrderTests` ran **15 / 3**, exactly the
three rows that ask physics at fact time. Step 5 failed with *"At fact time the physics scene did not
hold the body where this frame's move put it"*. *B*, with `PlayerAnimatorView`'s ceiling removed, on
a moved clock: **14 / 1 / 1 inconclusive**. The sibling failed alone at the ceiling, *"Expected: 6.0f
But was: 11.9999971f"*, while its ratio half passed at 0.62 s. B's first attempt ran on a clock the
test saw as 0 and came back inconclusive, not red. It was repeated after the owner focused the Editor.

**Verified:** **EditMode 3 160: 3 159 passed / 0 failed / 1 inconclusive**, on the final code on both
clocks. At zero (after leaving Play) the sibling is the inconclusive one; moved (6.76 s, 4.60 s), the
zero row is. **PlayMode 25 / 0 / 0 on ten passes of ten**, then an eleventh on the final tree after
the red checks. Console: 48 entries, the 46 negative-path rows M6-11d counted plus two AI Assistant
token warnings. Zero new analyzer warnings; `dotnet format` *Formatted 0 of 2*; `TimeManager.asset`
re-serialised and was reverted. The PlayMode passes overwrote `run.json` (parking lot); it was backed
up first and restored.
