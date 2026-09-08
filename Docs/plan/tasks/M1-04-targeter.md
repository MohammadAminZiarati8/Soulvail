# M1-04 — `Targeter`: cadence, immediate retarget, focus override, all-blocked state

**Size:** S · **Depends on:** M1-03 · **Branch:** `m1-04-targeter`
**Design refs:** CC §3.1, §3.3–3.6

## Goal

The stateful part of targeting: re-evaluate on a cadence rather than every frame, never jitter, retarget instantly when the target dies, honour the player's focus, and expose the "everything is blocked — go around" state for the reticle.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/Targeter.cs` | Core | Stateful selection over `TargetScorer` |
| `Tests/Core/Combat/TargeterTests.cs` | Tests.Core | Every rule |

## Public API

```csharp
namespace Soulvail.Core.Combat;

public sealed class Targeter
{
    public Targeter(TargetScorer scorer, TargetingSpec spec);

    public int  CurrentTargetId  { get; }   // -1 none
    public bool IsCurrentBlocked { get; }   // true when Current is an invulnerable "hold facing" target
    public int  FocusedTargetId  { get; }   // -1 none
    public bool HasFocus         { get; }
    public bool ChangedThisTick  { get; }   // Current/Blocked/Focus changed during the last Tick

    public void Focus(int enemyId);
    public void ClearFocus();
    public void Tick(float dt, ReadOnlySpan<TargetCandidate> candidates, float dpsOneSecond);
    public void Reset();
}
```

## Behaviour

1. `Tick` accumulates `dt`. A **scheduled** re-selection runs when the accumulator reaches `spec.Cadence` (then subtracts it). An **immediate** re-selection runs, ignoring the accumulator, when the current target is no longer valid: absent from `candidates`, `Hp <= 0`, `Distance > AcquireRange`, or `!IsVulnerable`.
2. Selection with focus: if `FocusedTargetId` is present, vulnerable, and within range → it is `Current`, score ignored, `IsCurrentBlocked = false`.
3. Focus drops (`ClearFocus` equivalent) when the focused enemy is absent from `candidates` (dead/despawned), or has been continuously out of `AcquireRange` for > 2 s. Focus does **not** drop merely because the enemy is temporarily invulnerable — but while it is, selection falls through to rule 4 and the reticle shows blocked on the focused id (`Current = focused`, `IsCurrentBlocked = true`).
4. Without an applicable focus: `Current = scorer.SelectBest(candidates, Current, dps)`. If that returns −1 and `candidates` is non-empty → `Current = scorer.SelectNearest(candidates)`, `IsCurrentBlocked = true`. If `candidates` is empty → `Current = -1`, `IsCurrentBlocked = false`.
5. `Focus(id)` and `ClearFocus()` take effect on the next `Tick` (they set a flag; the tick runs an immediate selection).
6. `ChangedThisTick` is true when `CurrentTargetId`, `IsCurrentBlocked`, or `FocusedTargetId` differs from before the tick — the caller publishes `TargetChanged` on it (M1-08).
7. `Reset` clears everything, including the accumulator.
8. No allocation in `Tick`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Tick_BeforeCadence_DoesNotReselect` | current A; B now scores higher / Tick(0.05) / still A |
| `Tick_AtCadence_Reselects` | same / Tick(0.05) again (total 0.1) / B |
| `CurrentDies_ImmediateRetarget` | current A / Tick(0.01) with A absent / new Current immediately, `ChangedThisTick` |
| `CurrentOutOfRange_ImmediateRetarget` | A at 13 m / Tick(0.01) / retargeted |
| `CurrentBecomesInvulnerable_ImmediateRetarget` | A invulnerable / Tick(0.01) / another target or blocked state |
| `Focus_OverridesScore` | B scores higher / Focus(A), Tick / Current A, `HasFocus` |
| `Focus_DropsWhenFocusedDies` | focused A / Tick with A absent / `HasFocus == false`, Current from scoring |
| `Focus_DropsAfterTwoSecondsOutOfRange` | focused A at 15 m / Tick 1.9 s / still focused; Tick 0.2 more / dropped |
| `Focus_OutOfRangeTimer_ResetsWhenBackInRange` | A out 1.5 s, in 0.1 s, out 1.5 s / — / still focused |
| `Focus_Invulnerable_ShowsBlockedOnFocused` | focused A invulnerable, B vulnerable / Tick / Current A, `IsCurrentBlocked`; focus retained |
| `AllBlocked_HoldsNearest_WithFlag` | all invulnerable / Tick / Current == nearest, `IsCurrentBlocked` |
| `NoCandidates_NoTarget` | empty / Tick / −1, not blocked |
| `ClearFocus_ReturnsToScoring` | focused A, B better / ClearFocus, Tick / B |
| `ChangedThisTick_FalseWhenStable` | current A / Tick(0.1) with same best / false |
| `Reset_ClearsState` | focused, current / Reset / −1, no focus, not blocked |
| `Tick_AllocatesNothing` | 40 candidates, warm-up / 10 000 ticks / allocated-bytes delta == 0 |

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Resolving a tap to an enemy id (M1-09). `Focus(id)` takes an id.
- Publishing events — M1-08.

## As built

_Filled at merge._
