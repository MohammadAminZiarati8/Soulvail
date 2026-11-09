# LS-02 — The Jungle's arenas go wide: west to east

**Size:** M · **Depends on:** LS-01 · **Branch:** `ls-02-jungle-arenas-go-wide`
**Design refs:** GD §7.2

## Goal

The Hollow (stages 1–4) and the Clearing (5–30) are landscape rectangles, entered at the west wall and left by a gate in the east wall.

## Context

Today: Hollow ~24 × 24 (start z −8, gate z +11), Clearing 36 × 36 (start z −14, gate +17, barrier and door on the north wall), 7 and 8 spawn points, `Collision/Bound_*` on the square. At 19.5:9 the LS-01 camera sees 40 × 25.6 m: the new Hollow fits whole, the Clearing pans a few metres. Boss cover (±8 m) and Thornheart's 14 m ring fit unchanged.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Arenas/Jungle/Arena_Hollow.prefab` | — | rules 1–4 |
| `Prefabs/Arenas/Jungle/Arena_Clearing.prefab` | — | rules 1–4 |
| `Prefabs/Arenas/Jungle/NavMeshArena_Hollow.asset` | — | rule 5, rebaked |
| `Prefabs/Arenas/Jungle/NavMeshArena_Clearing.asset` | — | rule 5, rebaked |
| `Tests/Game/Arena/ArenaViewTests.cs` | Tests.Game | |
| *small edits* | | `JungleTests.SmallRoomFloor` 26 → 30 |

Allows `.meta` files and test count/coverage updates.

## Behaviour

1. Floors: Hollow 30 × 22 m, Clearing 44 × 28 m (x × z); `_halfSize` matches; `Bound_*` colliders sit on the new perimeter.
2. Start 4 m in from the west wall, gate 1.5 m in from the east wall, both on z 0; barrier and door on the east wall line, turned to face west.
3. Spawn points: today's layout turned a quarter clockwise, (x, z) → (z, −x), scaled to the floor; same count; each ≥ `SpawnDirector.MinPlayerDistance` from the start and ≥ 1.5 m inside the walls.
4. Wall dressing, rubble, foliage and outskirts follow the new perimeter; no floor edge shows bare ground.
5. NavMesh rebaked; the gate is reachable from the start.

## Tests

| Test | Given / When / Then |
|---|---|
| `JungleArenas_FloorSizes` | both prefabs / `Collision/Floor` / 30 × 22, 44 × 28; `_halfSize` equal halves |
| `JungleArenas_RunWestToEast` | both / — / start x = −half.x + 4, gate x = half.x − 1.5, z 0; door and barrier x ≥ gate x |
| `JungleArenas_SpawnsKeepClearance` | both / — / counts 7, 8; every point rule 3 |
| `JungleArenas_BoundsOnPerimeter` | both / `Bound_*` / inner faces at ±half |
| `Arenas_ShippedHaveNoFaults` *(existing)* | — / — / none |
| `Jungle_OpensInASmallRoom` *(existing)* | Hollow / — / max side ≤ 30 |

Rule 4 and rule 5's reachability are manual steps 2 and 3.

## Manual verification (Editor / device)

1. **[Editor]** 2400×1080, stage 1: the whole Hollow on screen, gate at the right edge.
2. **[Editor]** Stages 1 and 5: walk the perimeter — walls closed, dressing all round, no bare edge.
3. **[Editor]** Stage 5: clear, walk to the gate, stage 6 loads.
4. **[Editor]** DT-01 at 10, 15, 20, 25, 30: boss cover inside the floor; at 20 the ring reads at the north and south walls.

## Out of scope

- UI; the stage-flow edge arrow and gate beam (unspecced).
- Descent's and the Sunscar's arenas (LS-03); new arenas for the pool.

## As built

_Commit body, ≤ 1 500 bytes._
