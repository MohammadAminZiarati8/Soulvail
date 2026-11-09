# SF-01 — Clear guide: slow-mo beat, gate beam, floor trail

**Size:** M · **Depends on:** — · **Branch:** `sf-01-clear-guide`
**Design refs:** GD §7.1–7.2

## Goal

Last kill: ~1 s slow-mo, a cyan gate beam, a floor trail; both pulse harder when idle.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Stage/StageFlowTuning.cs` | Core | |
| `Core/Stage/StageFlow.cs` | Core | rules 1–5 |
| `Tests/Core/Stage/StageFlowClearGuideTests.cs` | Tests.Core | |
| `Game/Composition/TimeScaleDriver.cs` | Game | rules 6–7 |
| `Game/Views/GateGuideView.cs` | Game | rules 8–10 |
| `Prefabs/Arenas/GateGuide.prefab` | — | |
| `Materials/M_GateGuide.mat` | — | unlit additive white |
| `Tests/Game/Views/ClearGuideTests.cs` | Tests.Game | |
| *small edits* | | `StageEvents` + 3 events; `RunScope`; `Run.unity` |
| *ripple* | | `StageFlow` ctor + `tuning = null` → `Default` |

Allows `.meta` files and test count/coverage updates.

## Public API

```csharp
public sealed class StageFlowTuning {   // init-only; finite, > 0
  public static StageFlowTuning Default { get; }
  public float SlowMotionScale { get; init; }    // 0.3
  public float SlowMotionTime { get; init; }     // 1.0 real s
  public float IdleHintTime { get; init; }       // 4.0
  public float IdleInputDeadzone { get; init; }  // 0.1
}
public readonly struct SlowMotionStarted { public readonly float Scale; }
public readonly struct SlowMotionEnded { }
public readonly struct GateHintChanged { public readonly bool Strong; }
```

## Behaviour

1. `EnterClear` publishes `SlowMotionStarted` after `StageCleared`, every clear.
2. `SlowMotionEnded` after `Scale × SlowMotionTime` scaled seconds, or at `Advance`.
3. Gate: idle accrues while `|MoveInput| < IdleInputDeadzone`, resets on input; `IdleHintTime` → `GateHintChanged(true)`, input → `(false)`, once per edge.
4. Transition while strong publishes `(false)`.
5. Clear/Gate ticks allocate nothing.
6. `TimeScaleDriver`: `Scale` on Started, 1 on Ended; writes `Time.timeScale` only while not `RunPause.IsPaused`, else on the first unpaused `LateUpdate`.
7. It writes only when the target changes; Dispose restores 1.
8. `GateGuideView` on `StageCleared`: beam at `GatePosition` (`_beamHeightM` 14, `_beamWidthM` 1.6); trail `_trailLiftM` 0.05 on a NavMesh path to the gate, repathed every `_repathS` 0.25 via `GetCornersNonAlloc`, else straight. `Palette.Player` by property block.
9. Calm: `_calmPulseHz` 0.8, `_calmAlpha` 0.55. Strong: `_strongPulseHz` 2, `_strongAlpha` 1, beam width × `_strongWidthMul` 1.5.
10. Transition or arrival hides both; `Update` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `Clear_SlowMotionAfterCleared` | last kill / tick / Cleared, Started |
| `SlowMotion_EndsAfterScaledTime` | Clear / 0.29, 0.31 s / no, then one Ended |
| `SlowMotion_LastStageToo` | mode complete / clear / Started |
| `SlowMotion_AdvanceEndsIt` | on / Advance / Ended |
| `Gate_IdleFourSeconds_HintStrong` | stick 0 / 4 s / one true |
| `Gate_MoveAfterHint_Calm` | strong / stick 1 / one false, idle 0 |
| `Gate_Transition_ClearsHint` | strong / reach gate / false |
| `ClearAndGate_AllocateNothing` | warmed / `AllocationAssert.None` |
| `Driver_Sets_WritesOnce_Restores` | Started ×2, Ended, Dispose / — / 0.3 once, 1 |
| `Driver_Paused_Defers` | paused / resume / 0.3 |
| `Guide_ShowsAndHides` | Cleared, TransitionStarted / — / on, off |
| `Guide_StrongHint_Values` | true, false / — / rule 9 |
| `Guide_TrailEndsAtGate` | Hollow / repath / ends ≤ 0.1 m off |

## Manual verification (Editor / device)

1. **[Editor]** Clear stage 1: ~1 s slow-mo; beam seen from the west wall; trail around cover follows the hero.
2. **[Editor]** Idle 4 s: faster, brighter; move: calm.
3. **[Editor]** Level-up on a last kill: tree frozen; slow-mo ends after the pick.
4. **[device]** Readable sideways.

## Out of scope

- Banner, edge arrow (UI rework); entry (SF-02). Old `const`s stay.
