# M7-05a — The Ashen Reach's first arena

**Size:** S · **Depends on:** the KayKit environment packs, committed with it · **Branch:** `m7-05a-the-ashen-reachs-first-arena`
**Design refs:** GD §3, GD §7.2, GD §11.3, GD §16.4, GD §17.1 · **Ledger rows:** none

Pulled forward from M7-05 at the owner's request of 2026-09-26, the way RS-02a pulled RS-04's
scene forward: *"user in campaign can select a place like desert, hell … and the whole stages and
enemies are related to that place … create a stage (just one is enough) with the vision of … 30 or
50 stages for this place only. maybe even limitless stages."*

## Goal

The first arena with real art — the Ashen Reach's courtyard, built from the KayKit kit, legal under
GD §7.2 and rostered by Descent — laid out so every later arena of a place copies its shape.

## How a place scales, in the code that already exists

- **A place is a `ModeDefinition`.** It already carries an arena pool, an enemy roster with the
  stage each archetype arrives at, a boss every *n* stages, difficulty scaling and an endless flag.
  Descent is endless today, so limitless stages is not new work.
- **A stage is a step; an arena is a room.** `ModeSpec.ArenaFor` picks a room for each stage from
  the run's seed, never the previous one, so 30–50 stages want GD §7.2's 8–12 rooms, not 50.
  Variety beyond the rooms comes from the roster, the bosses, the Ordeals and the scaling.
- **What this task adds for scale:** an arena's id and folder name its place
  (`AshenReach/Arena_Courtyard.prefab` ↔ `arena.ashen-reach.courtyard`), and the kit's materials
  are remapped at the importer, so the next room is built by dragging pieces in.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Arenas/AshenReach/Arena_Courtyard.prefab` | — | the arena |
| `Prefabs/Arenas/AshenReach/NavMeshArena_Courtyard.asset` | — | its baked NavMesh (generated) |
| `Materials/Environment/M_Ash.mat` | — | the falling ash |
| `Tests/Game/Arena/ArenaViewTests.cs` | Tests.Game | two rows rewritten |
| *small edits* | | `Data/Modes/Descent.asset` (+1 arena id), `Scenes/Run.unity` (+1 prefab on `RunScope._arenaPrefabs`) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Behaviour

1. **The courtyard is a legal arena.** A player start, a gate with a barrier and a door, at least
   three spawn points clear of the start, 3–6 cover pieces on the `Cover` layer and a baked
   NavMesh — every rule `ArenaViewTests` holds a shipped arena to.
2. **An arena's id names its place when its folder does.** An arena directly in `Prefabs/Arenas/`
   has two segments (`arena.pillars`); one in a place's folder has three, the middle one the
   folder's name (`AshenReach/` ↔ `ashen-reach`). The last segment is the file's name either way.
3. **Descent rosters the courtyard beside the two grey boxes, and the Run scene can raise it.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Arena_HasNoFaults`, `Arena_HasAtLeastThreeSpawnPoints`, `Arena_SpawnPointsClearThePlayerStart`, `Arena_HasThreeToSixCoverPillars`, `Arena_HasBakedNavMesh`, `Arena_HasADoor` | the courtyard is under `Prefabs/Arenas/` / the rows enumerate it / each passes (rule 1) |
| `Arena_IdMatchesTheAssetName` | each shipped arena / its id is split / `arena`, the folder's place if any, the file's name (rule 2) |
| `Descent_RostersItsArenas` | Descent / its spec is read / pillars, tiered, courtyard (rule 3; was `Descent_RostersBothArenas`) |
| `Descent_EveryArenaItRostersIsShipped` | unchanged / now covers the courtyard (rule 3) |

## Manual verification (Editor / device)

1. **[Editor]** Start a run and play until the courtyard stands; with three rooms and no repeats
   it is one stage in two or three. *Watch:* the south railing never hides the player; ash falls;
   the bricked arch on the north wall becomes a doorway when the stage clears, and the tiled path
   leads to it; enemies walk round the pillars and the corner rocks.
2. **[Editor]** Its colours: grey stone, grey ash, blue-grey rock — nothing red-orange, gold,
   violet or cyan (GD §16.4).
3. **[device]** Draw calls and frame time in the courtyard. Deferred with every device row.

## Out of scope

- **A place-select screen, a `ModeDefinition` per place, and a place's own enemy looks.** The
  owner's campaign needs all three; each is its own task, and GD §3's ten-stage layers are rewritten
  when the campaign is specced.
- The Ashen Reach's other 7–11 rooms (M7-05), a place's light and fog (scene-wide today), and
  combining an arena's meshes or turning on the GPU Resident Drawer (see *As built*).

## As built

**Deviations.** *(1)* The two KayKit environment packs, `M_Dungeon`, `M_ForestNature` and the
importer remap to them ship in this change but not in the Files table: they set the packs up, and
every later arena needs them
([VERSIONS.md](../../../Assets/ThirdParty/KayKit/VERSIONS.md)). *(2)* The art carries no colliders
but the cover and the rubble; the floor and the four bounds are boxes with no renderer under
`Collision/`, so the bake reads 17 sources rather than 85 meshes. *(3)* GD §16.4 changed three
choices after the first render: `wall_gated`'s orange portcullis became a bricked arch
(`wall_arched`) as the barrier, the doorway's wooden leaf is hidden, and the banners and the brown
`rubble_*` heaps were replaced by grey rock.

**Numbers.** 85 renderers standing (the gate swaps two), 29 170 triangles, two materials plus the
ash. NavMesh: 163 triangles, 1 140 m² of the 1 296 m² floor walkable; all 8 spawn points path to the
player start, the start paths to the gate, and a loop round the middle completes.

**Finding — the draw-call budget.** GD §11.3's low tier is under 50 draw calls a frame, and this
room alone is 85 renderers: SRP-batched on two materials, but 85 draws before one enemy is on
screen. If a phone confirms it, the answer is either combining each arena's meshes per material
at edit time, or URP's GPU Resident Drawer, which is a render-pipeline asset change and the
owner's to approve. Not a blocker in the Editor.

**Finding — two NavMesh probes that lie**, filed in [Traps §5](../../Traps.md#5-unity-editor-and-the-asset-pipeline):
a bake in a preview scene is never registered, so a probe right after it reads an empty NavMesh
as *every obstacle carved*; and a point sample at an obstacle's centre finds an unreachable floor
polygon under any obstacle with a walkable top — the shipped arenas' pillars have them too. The
courtyard's obstacle colliders reach 0.3 m below the floor anyway; that removed none of the
polygons and costs nothing.

**Verified.** EditMode 3 277 — 3 276 passed, 0 failed, 1 inconclusive (the animator clock row) —
+7 on RS-03g's 3 270, the courtyard's seven per-arena rows; PlayMode 67 / 67. Console: no errors;
the courtyard joins both arena prefabs in the reload-time "0 spawn point(s)" warning (PROGRESS,
Known issue 2). Format check clean on `ArenaViewTests.cs`.
