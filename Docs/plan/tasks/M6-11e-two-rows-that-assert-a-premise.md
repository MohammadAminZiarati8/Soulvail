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

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
