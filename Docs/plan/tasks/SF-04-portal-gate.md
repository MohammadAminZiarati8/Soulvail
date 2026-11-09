# SF-04 — The gate is a portal; no entry door, no floor trail

**Size:** M · **Depends on:** SF-03 · **Branch:** `sf-04-portal-gate`
**Design refs:** GD §7.1, §16.4

## Goal

Owner, 2026-11-10: nothing drops at a stage's start, no line runs from the hero to the gate, and the open gate is a glowing cyan portal instead of a tall beam.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/EntryDoorView.cs` (+ `.meta`) | Game | **deleted**; rule 1 |
| `Game/Views/GateGuideView.cs` | Game | rewrite; rules 2–5 |
| `Prefabs/Arenas/GateGuide.prefab` | — | portal fields; keeps `M_GateGuide` (additive) |
| `Tests/Game/Views/ClearGuideTests.cs` | Tests.Game | guide rows rewritten |
| `Tests/Game/Arena/ArenaEntryTests.cs` | Tests.Game | door rows replaced |
| *small edits* | | `ArenaView`: `_entryDoor`, `OpenEntry`, `CloseEntry` go. `ArenaPool`: their two calls go. The six arena prefabs (`Arena_Pillars`, `Arena_Tiered`, `Arena_Courtyard`, `Arena_Clearing`, `Arena_Hollow`, `Arena_Grove`) lose the `Entry Door` child and its `Slab`; the `Entry` marker stays |

Allows `.meta` files and test count/coverage updates. Core is untouched: the walk-in, seal and beat before waves stay as SF-02 built them.

## Public API

```csharp
// GateGuideView — Update calls Advance(Time.deltaTime); tests call it directly.
public void Advance(float dt);
```

`ArenaView.OpenEntry`/`CloseEntry` are removed; `HasEntry`/`EntryPosition` stay.

## Behaviour

1. No arena has an entry door. The hero still appears at `Entry` and walks in; nothing moves or appears behind them.
2. `GateGuideView` draws no floor trail: no NavMesh path, no line from the hero.
3. On `StageCleared` with a next arena, a portal stands at `GatePosition`, base on the floor, facing the arena centre (XZ): an upright oval ring 2.4 m wide × 3.2 m tall (line width 0.18 m), a second ring 0.8× its size turning about the portal's facing axis at 90°/s, a filled oval surface inside the outer ring, and a flat 3.2 m floor glow beneath it. All `Palette.Player`, additive, no new models, no lights.
4. Calm: alpha pulses at 0.8 Hz around 0.55 (the surface at half the rings' alpha). Strong (`GateHintChanged(true)`): 2 Hz around 1.0 and the whole portal scaled 1.15×. `false` returns to calm.
5. `StageTransitionStarted`, `StageArrived` and `RunEnded` hide every portal part; the next `StageCleared` shows them again at the new gate.

## Tests

| Test | Given / When / Then |
|---|---|
| `Arenas_NoEntryDoor` (×6 shipped arenas) | prefab / load / no `Entry Door` or `Slab` in its children; `Entry` present (rule 1) |
| `Pool_Sealed_NothingMoves` | pool, arrival, seal / tick 1 s / no child transform under `Entry` changed (rule 1) |
| `Guide_NoTrail` | guide shown / — / no `LineRenderer` lies flat on the floor; no child named `Trail` (rule 2) |
| `Guide_PortalAtGate_FacesArena` | gate at (0,0,17), centre (0,0,0) / `StageCleared` / portal root at the gate, forward ≈ −Z, outer ring spans 2.4 × 3.2 m (rule 3) |
| `Guide_InnerRingTurns` | shown / advance 1 s / inner ring rotated 90° ± 1° about forward (rule 3) |
| `Guide_StrongHint_Values` | shown / hint true, then false / scale 1.15 and 2 Hz, then 1 and 0.8 Hz (rule 4) |
| `Guide_ShowsAndHides` | shown / each of the three events / all parts disabled; next clear re-enables (rule 5) |

## Manual verification (Editor / device)

1. **[Editor]** Clear a stage: a cyan oval portal glows on the north wall's gate, inner ring turning; no beam, no floor line.
2. **[Editor]** Stand still ~4 s: the portal swells and pulses faster.
3. **[Editor]** Walk through: flash, the hero walks in from the south; nothing drops behind them.
4. **[device]** The portal reads as the way out at first glance, from the far end.

## Out of scope

- The edge arrow; the STAGE N card (SF-05); particles, lights, a shader.
- Any core timing (walk-in, seal, beat) — unchanged.

## As built

_Written at merge as the commit body, ≤ 1 500 bytes._
