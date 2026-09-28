# M7-05e — The Jungle's small room

**Size:** S · **Depends on:** M7-05b (the kit), M7-05c (the Clearing it is laid out like) · **Branch:** `m7-05e-the-jungles-small-room`
**Design refs:** GD §7.2, GD §11.3, GD §12.4, GD §16.4 · **Ledger rows:** none

The second task cut from the owner's go of 2026-09-27: *"build a small Jungle room for the first
stages from the M7-05b kit, laid out like the Clearing: low south edge, gate with vine curtains,
3–6 Cover pieces, baked NavMesh. Verify it with the NavMesh probes in Traps §5."*

## Goal

The Hollow, `arena.jungle.hollow`: a 24 × 24 m sunken ruin under the canopy. It is the room a
Jungle run opens in, legal under every rule `ArenaViewTests` holds an arena to, and cheaper to draw
than the Clearing.

## Why 24 × 24

- **The floor must hold the spawn clearance.** `SpawnDirector.MinPlayerDistance` is 6 m, so with the
  start near the south edge the north two-thirds of the room is spawnable. At 24 m that is about
  24 × 14 m, which holds GD §12.4's stage 1–4 concurrency of 10–12 bodies.
- **Smaller than 24 × 24 leaves no circle-strafe route** (GD §7.2) around 3 cover pieces.
- **Nothing rosters it here.** The Jungle's mode, which opens its runs in this room, is M7-05i. This
  task makes the room and lets the Run scene raise it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Arenas/Jungle/Arena_Hollow.prefab` | — | the arena, `arena.jungle.hollow` |
| `Prefabs/Arenas/Jungle/NavMeshArena_Hollow.asset` | — | its baked NavMesh (generated) |
| *small edits* | | `Scenes/Run.unity` (+1 prefab on `RunScope._arenaPrefabs`) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Behaviour

1. **The Hollow is a legal arena.** It has a player start, a gate with a barrier and a door, and at
   least three spawn points 6 m or more from the start. It has 3–6 cover pieces on the `Cover`
   layer and a baked NavMesh.
2. **Its id names its place.** `Jungle/Arena_Hollow.prefab` ↔ `arena.jungle.hollow`, by M7-05a's
   rule.
3. **It is laid out the way the Clearing is.** Moss-grey walls stand on three sides, and the south
   edge is a low row of barrier columns, because the camera looks north at 57°. The gate is
   `wall_doorway` with the sealed vine curtain as its barrier and the parted curtain as its door. A
   stone path runs from the start to the gate, and the undergrowth stays off the walking lines.
   Trees stand outside the walls and never inside, because a crown inside would hide the player.
4. **Its floor is 24 × 24 m**, bounded by colliders at the wall line.
5. **It costs fewer renderers than the Clearing.** Its standing renderer count is stated in *As
   built* and is below the Clearing's 139. Only the cover and the rubble carry colliders, as in
   M7-05a and M7-05c.
6. **Its NavMesh is checked by Traps §5's probes, not by a point sample.** Every spawn point paths
   to the start and the start paths to the gate, each with `PathComplete`. Every cover piece carves
   the floor, tested by `NavMesh.Raycast` from outside it, after a bake registered in a temporary
   additive scene.
7. **The Run scene can raise it.** It is on `RunScope._arenaPrefabs`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Arena_HasNoFaults`, `Arena_HasAtLeastThreeSpawnPoints`, `Arena_SpawnPointsClearThePlayerStart`, `Arena_HasThreeToSixCoverPillars`, `Arena_HasBakedNavMesh`, `Arena_HasADoor` | *(unchanged)* the Hollow is under `Prefabs/Arenas/` / the rows enumerate it / each passes (rule 1) |
| `Arena_IdMatchesTheAssetName` | *(unchanged)* now covers the Hollow (rule 2) |

Rules 3–6 are a layout and a probe, checked by hand and recorded in *As built*. Rule 4's size
becomes a row at M7-05i, where it is the Jungle's rule: `Jungle_OpensInASmallRoom`. Rule 7 is
exercised by that task's run.

## Manual verification (Editor / device)

1. **[Editor]** Open the prefab and look from the game camera's angle. *Expected:* nothing on the
   south edge stands taller than the player. The gate's curtain is in the north wall, and the
   colours are greens, browns and moss-grey stone, with nothing red-orange, gold, violet or cyan
   (GD §16.4).
2. **[Editor]** Played at M7-05i, the Jungle's stages 1–4 are this room.
3. **[device]** Draw calls and frame time in the Hollow. Deferred with every device row.

## Out of scope

- Rostering it (M7-05i) and the Jungle's other rooms (M7-05).
- The draw-call fix M7-05c's *As built* proposes: a combine per material, or the GPU Resident
  Drawer. This room is kept small instead, and the count is reported.

## As built

**Deviations.** *(1)* **The doorway's wooden door is switched off**, as the Clearing's is. KayKit's
`wall_doorway` carries a `wall_doorway_door` child, and left on it hid the vine curtain in both the
sealed and the parted state. A render found it, and the Clearing's prefab showed the fix. *(2)* No
test file changed. The seven arena rows enumerate the new prefab by themselves.

**Layout.** The floor is 24 × 24 m, bounded at ±12.5. The north wall is 4 m pieces, with half-walls
at the ends and `wall_doorway` centred for the gate. The east and west walls slope down to the
south. The south edge is barrier columns. Moss-grey pillars and columns stand at the corners, and
five hanging vines run along the walls. A stone cross runs from the start (0, −8) to the gate
(0, 11). Four cover pieces sit off the lines: two buttress trunks, a decorated pillar and a broken
wall. Three rubble pieces have colliders: two corner rocks and a fallen column. Undergrowth stays
along the walls, and thirteen trees and plants stand outside them. There are seven spawn points,
each at least 10 m from the start.

**Numbers.** **81 renderers standing** (82 in the prefab, and the gate swaps two), against the
Clearing's 139. About 32 300 triangles standing (33 467 in the prefab with both curtains),
against 55 597. Four materials: `M_JungleRuins` 42, `M_Jungle` 39, the ground and the leaves.
Still above GD §11.3's low-tier 50 draws, and M7-05c's finding stands for every room.

**NavMesh, by Traps §5's probes.** Baked in a temporary additive scene, then re-probed from the
saved asset. 497 of the 576 m² floor is walkable. All seven spawn points reach the start with
`PathComplete`, the start reaches the gate, and all eight legs of a loop at 8.5 m round the middle
complete. Every cover and rubble piece blocks every floor ray that starts on the mesh and points at
its centre: 16 / 16 for each cover piece, 5 / 5 and 6 / 6 for the corner rocks, 11 / 11 for the
column. A first probe cast from 3.2 m either side along x and z, and snapped its endpoints to the
mesh. It reported two corner rocks "not blocked", because the snap walked the start round the rock.
Start the rays on the mesh. Spent (stays here).

**Verified.** `ArenaViewTests` 38 / 38, the Hollow's seven rows among them. Suite counts are
M7-05d's *As built*. Renders: `Temp/Renders/hollow_start.png`, `hollow_middle.png`,
`hollow_gate_sealed.png` and `hollow_gate_open.png`, from the game camera's 57° and 16 m.
