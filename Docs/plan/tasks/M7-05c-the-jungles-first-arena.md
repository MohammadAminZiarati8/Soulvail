# M7-05c — The Jungle's first arena

**Size:** S · **Depends on:** M7-05a (a place's arena folder and id), M7-05b (the kit) · **Branch:** `m7-05c-the-jungles-first-arena`
**Design refs:** GD §7.2, GD §11.3, GD §16.4, GD §17.1 · **Ledger rows:** none

The second half of the owner's go of 2026-09-27 (M7-05b): *"build the jungle place's kit and its
first arena yourself."*

## Goal

The Jungle's clearing — a ruined plaza under the canopy, built from M7-05b's kit, legal under
GD §7.2 and rostered by Descent — the second place's first room, laid out the way M7-05a laid out
the first.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Arenas/Jungle/Arena_Clearing.prefab` | — | the arena, `arena.jungle.clearing` |
| `Prefabs/Arenas/Jungle/NavMeshArena_Clearing.asset` | — | its baked NavMesh (generated) |
| `Tests/Game/Arena/ArenaViewTests.cs` | Tests.Game | one row rewritten |
| *small edits* | | `Data/Modes/Descent.asset` (+1 arena id), `Scenes/Run.unity` (+1 prefab on `RunScope._arenaPrefabs`) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Behaviour

1. **The clearing is a legal arena.** A player start, a gate with a barrier and a door, at least
   three spawn points clear of the start, 3–6 cover pieces on the `Cover` layer and a baked
   NavMesh — every rule `ArenaViewTests` holds a shipped arena to.
2. **Its id names its place.** `Jungle/Arena_Clearing.prefab` ↔ `arena.jungle.clearing`, by
   M7-05a's rule.
3. **Descent rosters the clearing beside the courtyard and the two grey boxes, and the Run scene
   can raise it.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Arena_HasNoFaults`, `Arena_HasAtLeastThreeSpawnPoints`, `Arena_SpawnPointsClearThePlayerStart`, `Arena_HasThreeToSixCoverPillars`, `Arena_HasBakedNavMesh`, `Arena_HasADoor` | the clearing is under `Prefabs/Arenas/` / the rows enumerate it / each passes (rule 1) |
| `Arena_IdMatchesTheAssetName` | unchanged / now covers the clearing (rule 2) |
| `Descent_RostersItsArenas` | Descent / its spec is read / pillars, tiered, courtyard, clearing (rule 3) |
| `Descent_EveryArenaItRostersIsShipped` | unchanged / now covers the clearing (rule 3) |

## Manual verification (Editor / device)

1. **[Editor]** Start a run and play until the clearing stands; with four rooms and no repeats it
   is one stage in three or four. *Watch:* nothing on the south edge hides the player; leaves
   fall; the vine curtain in the north doorway parts when the stage clears, and the stone path
   leads to it; enemies walk round the two buttress trunks, the pillar and the broken wall.
2. **[Editor]** Its colours: greens, browns, moss-grey stone — nothing red-orange, gold, violet or
   cyan (GD §16.4).
3. **[device]** Draw calls and frame time in the clearing. Deferred with every device row.

## Out of scope

- The Jungle's other 7–11 rooms, its `ModeDefinition`, its enemies' looks, and its light and fog
  (scene-wide today).
- Combining an arena's meshes or turning on the GPU Resident Drawer — see *As built*.

## As built

**Deviations.** *(1)* The KayKit pieces take the Jungle's materials by a per-renderer override in
the prefab, not at the importer: the importer's remap sends them to `M_ForestNature` and
`M_Dungeon`, which the Ashen courtyard still reads. That is the mechanism VERSIONS.md now states —
a place recolours a pack by swapping its atlas — and the next Jungle room copies these renderers
rather than setting the override again. *(2)* The gate is M7-05b's vine curtains, not a KayKit
piece: sealed as the barrier in `wall_doorway`'s opening, parted as the door. *(3)* Two choices
changed after renders: `Bush_2_C` read as cube hedges along the low south edge and became
`Bush_1_E` at 1.3×, and the buttress trunk's moss, seen from above, read as leaves and was
flattened.

**Layout.** Moss-grey dungeon walls on three sides with hanging vines along them; a low south row
of barrier columns, because the camera looks north at 57° and anything tall on the south edge
stands between it and a player backed against that wall. A plaza and a path of
large floor slabs over the jungle floor; four cover pieces — two buttress trunks, a decorated
pillar, a broken wall; eight rubble pieces, all with colliders; undergrowth inside the walls; and
fourteen trees outside them. The art carries no colliders but the cover and the rubble, as in
M7-05a: 17 collision sources.

**Numbers.** 140 renderers in the prefab, 139 standing (the gate swaps two); 55 597 triangles;
four materials — `M_Jungle` 76, `M_JungleRuins` 62, the ground and the leaves. NavMesh: 1 136 m² of
the 1 296 m² floor walkable; all 8 spawn points path to the start, the start paths to the gate,
a loop round the middle completes, and all 12 obstacles carve the floor (tested by floor raycast,
per [Traps §5](../../Traps.md#5-unity-editor-and-the-asset-pipeline)).

**Finding — the draw-call budget, again and worse.** M7-05a's finding at 85 renderers, now 139:
nearly three times GD §11.3's low-tier 50 before an enemy stands. Undergrowth is what a jungle is
made of, so the count will not fall by editing rooms. The fix is an edit-time combine of each
arena's static meshes per material — four draws here — or URP's GPU Resident Drawer, a
render-pipeline change and the owner's to approve. Either wants its own task before M7-05's other
rooms are built; a phone has not confirmed the cost.

**Verified.** EditMode 3 317 / 3 316 passed, 0 failed, 1 inconclusive (the animator clock row);
this change's seven arena rows pass. PlayMode 67 / 67. Console: no errors; the clearing joins the
other arenas in the reload-time "0 spawn point(s)" warning (PROGRESS, Known issue 2). Format check
clean on `ArenaViewTests.cs`.
