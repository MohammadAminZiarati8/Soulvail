# LS-03 — Descent's and the Sunscar's arenas go wide

**Size:** M · **Depends on:** LS-02 · **Branch:** `ls-03-remaining-arenas-go-wide`
**Design refs:** GD §7.2

## Goal

Every shipped arena is a landscape rectangle run west to east, and a test keeps it so.

## Context

Left square after LS-02, all 36 × 36: `Arena_Pillars` (Descent; four pillars at ±8, start z −14, gate z +17), `Arena_Tiered` (Descent; 14 m dais, four ramps, five pillars to ±14, start (−13, −13), gate already east at x 16), `Arena_Courtyard` (the Sunscar's; laid out as the Clearing). Building the Sunscar's content stays on the owner's go; this reshapes only the existing arena.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Arenas/Arena_Pillars.prefab` | — | rules 1–3 |
| `Prefabs/Arenas/Arena_Tiered.prefab` | — | rules 1–4 |
| `Prefabs/Arenas/AshenReach/Arena_Courtyard.prefab` | — | rules 1–3 |
| `Prefabs/Arenas/NavMeshArena_Pillars.asset`, `NavMeshArena_Tiered.asset`, `AshenReach/NavMeshArena_Courtyard.asset` | — | rebaked |
| `Tests/Game/Arena/ArenaViewTests.cs` | Tests.Game | |

Allows `.meta` files and test count/coverage updates.

## Behaviour

1. Floor 44 × 28 m; `_halfSize` (22, 14); walls or `Bound_*` on the perimeter; dressing follows it.
2. Start, gate, barrier, door and spawn points as LS-02 rules 2–3.
3. Pillars: the four at (±10, ±6). Courtyard cover keeps its pieces, turned a quarter clockwise.
4. Tiered: dais and ramps stay centred; the five pillars turned a quarter clockwise and pulled inside 2 m of the walls; the gate stays east.
5. Every shipped arena is wider than deep, starts west of centre and gates east of it.

## Tests

| Test | Given / When / Then |
|---|---|
| `RemainingArenas_FloorSize` | three prefabs / floor / 44 × 28 |
| `RemainingArenas_RunWestToEast` | three / — / LS-02's placement rule |
| `Pillars_PillarsAtTenSix` | Pillars / — / (±10, ±6) |
| `Tiered_PillarsInsideWalls` | Tiered / — / each ≥ 2 m inside; dais at origin |
| `Arenas_AllShippedAreLandscape` | every prefab under `Prefabs/Arenas` with an `ArenaView` / — / x > z, start x < 0 < gate x |
| `Arenas_ShippedHaveNoFaults` *(existing)* | — / — / none |

## Manual verification (Editor / device)

1. **[Editor]** 2400×1080, a Descent run: both arenas framed wide, cover clear of the walls, gate reached.
2. **[Editor]** Open `Arena_Courtyard`: walls closed, dressing all round.

## Out of scope

- UI; any Sunscar content, roster or stages.

## As built

_Commit body, ≤ 1 500 bytes._
