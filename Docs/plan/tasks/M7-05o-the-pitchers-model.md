# M7-05o — The Pitcher's model

**Size:** M · **Depends on:** M7-05n (the shell style, `enemy_atlas.py`) · **Branch:** `m7-05o-the-pitchers-model`
**Design refs:** GD §8.1, GD §16.4, GD §17.1; AR §18 (the M2-art rows: Generic, no root motion) · **Ledger rows:** none

The owner's go of 2026-09-29: *"Design and build the Jungle's Spitter as a pitcher plant, in KayKit
style … a tall, narrow jug-shaped carnivorous plant on short stubby root legs. It shuffles on its roots,
leans back and spits a glob from its open lid, flinches when hit, and wilts and topples when it dies.
Its silhouette must read as the opposite of the Frog's."* The Frog's three steps are the pattern: this
model, then the body (M7-05p), then the roster (M7-05q). The owner OK'd a turnaround beside the Frog
and KayKit's Ranger before the rig and the clips were made.

## Goal

`Pitcher.fbx`, one skinned mesh of 400–2,500 triangles on a skeleton of its own with five clips of its
own, sampling the shared enemy atlas through `M_Enemy`, made by one script from nothing outside the
repository.

## Why this shape

- **Built as the Frog is (M7-05n).** Smooth closed shells of one colour each, bound rigidly to one
  bone, overlapping at every joint. The jug is one shell from roots to lip, so it has no seam; the
  rim, the throat and the lid ride a `mouth` bone above it.
- **Tall and narrow against the Frog's low and wide.** A 1.35 m jug, 0.54 m across and 0.84 m
  across its leaves, where the Frog is 1.0 m tall and 1.2 m wide. GD §17.1 wants enemies told apart
  as black shapes, and the two Jungle enemies a player meets first are told apart by proportion.
- **The lid is the telegraph.** It rests half shut at 35°: near 57° it is edge-on to the game
  camera and vanishes, and at 35° its top faces the camera. The attack flips it past upright before
  the spit, so a lid snapping open reads from above as *about to fire*.
- **The jug stands on the hips.** `body` is a short hip bone the legs hang from; `jug` stands on it
  and carries everything above the roots. A lean, a flinch or a wilt tilts the jug and leaves the
  feet planted, as the Frog's spine does over its legs.
- **Columns 4–5 of the atlas**: a pale chartreuse jug, a leaf-green lid, a wine rim and speckles, a
  dark throat, green leaves, brown roots, dark eyes and a glint. The plant's real violet and orange
  blotches are wine, because GD §16.4 reserves both.

## Files

| Path (from the repository root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/pitcher.py` | Blender 4.1 | **New.** Writes the model and the atlas; `--pose-sheet <png>` renders the review sheet |
| `Assets/_Project/Art/Enemies/Pitcher.fbx` | generated | the model, on `Rig_Pitcher`, with its clips; its `.meta` holds the import settings |
| `Assets/_Project/Tests/Game/Art/PitcherModelTests.cs` | Tests.Game | **New.** Rules 2–7 |
| *small edits* | | `Tools/Blender/enemy_atlas.py` (the Pitcher's swatches, columns 4–5); `T_Enemy_Albedo.png` (regenerated) |

## Public API

```text
PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background \
    --factory-startup --python Tools/Blender/pitcher.py [-- --pose-sheet <out.png>]
```

## Behaviour

1. **One script makes the model and the atlas, and a re-run makes the same ones.** The PNG is
   byte-identical and the FBX differs in its header's timestamp. `frog.py` and `rootling.py` re-runs
   write the same atlas.
2. **It is in the crowd's budget.** One skinned mesh of 400–2,500 triangles, one material slot,
   remapped to `M_Enemy`.
3. **Every vertex is weighted** to at most four bones summing to 1, and none to `root`.
4. **It imports Generic with its five clips**: `Idle` (48 f), `Shuffle` (12 f, 0.44 m of ground),
   `Attack` (36 f, the spit on frame 18), `Hit` (12 f) and `Death` (30 f, on its side by frame 15),
   at 30 fps, only `Idle` and `Shuffle` looping. An avatar from this model, not optimised.
5. **Every clip binds, and none moves the root.**
6. **It stands off the Jungle's floor** by the Frog's margins, and no atlas pixel comes near a
   reserved colour (`RootlingModelTests.EnemyAtlas_CarriesNoReservedColour`, unchanged).
7. **It is tall where the Frog is wide.** At rest its height is at least 1.5 times its width, and
   the Frog's at most its width.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `PitcherModel_IsShipped` | the model / loaded / one `SkinnedMeshRenderer` (guards every row) |
| `PitcherModel_StaysInTheCrowdsBudget` | the mesh / triangles / 400–2,500, one submesh (rule 2) |
| `PitcherModel_UsesTheEnemyMaterial` | the renderer / its materials / `M_Enemy` alone (rule 2) |
| `PitcherModel_EveryVertexIsWeighted` | the bone weights / read / sum 1 ± 1e-3, ≤ 4 bones, none on `root` (rule 3) |
| `PitcherModel_ImportsGenericWithItsClips` | the importer and clips / read / Generic, own avatar, not optimised, five clips, two loops, 30 fps (rule 4) |
| `PitcherModel_EveryClipBindsAndLeavesTheRootAlone` | each clip / every binding, every frame sampled / resolves; `root` still (rule 5) |
| `PitcherModel_StandsOffTheJungleFloor` | the atlas at each triangle and the floor texture / averaged / +0.05 luma, +0.15 saturation (rule 6) |
| `PitcherModel_IsTallWhereTheFrogIsWide` | both meshes at rest / height over width / Pitcher ≥ 1.5, Frog ≤ 1 (rule 7) |

Rule 1 is checked by hand: a re-run of each script and a byte compare. The script also asserts rules
2, 3 and 6 before it writes.

## Manual verification (Editor / device)

1. **[owner]** Read the pose sheet (`pitcher.py -- --pose-sheet Temp/Renders/pitcher_pose_sheet.png`):
   the roots step, the lid flips open as the jug leans back, the spit snaps forward, the flinch slams
   the lid, and the plant topples onto its side.
2. **[Editor]** Seen in a run at M7-05q.

## Out of scope

- The body, its animator and its place in a run (M7-05p, M7-05q).
- A glob on the model. The shot is `ProjectileView`'s, drawn in the reserved red-orange.

## As built

**As specified**, after three review renders the owner did not see and one the owner OK'd
(`Temp/Renders/pitcher_lineup.png` and `pitcher_turnaround.png`, made by a throwaway script beside the
repository). What those rounds changed: the leaves became broad hanging leaves, not fins; the lid
went from 43° to 68° and back to 35°, by the game-camera argument above; and the neck became part of
the jug, because a separate neck shell showed a seam twice.

**Findings.** *(1)* **Workbench's specular sheen greys a flat dark face.** The throat, a dark disc
facing up, rendered pale grey from the game camera. `M_Enemy` is URP Lit at smoothness 0.05, so the
review renders and the pose sheet draw without the sheen, which is the closer picture. *(2)* **A
mouth that moves on its own bone can bare the jug's lip.** The spit first shrank the rim to 0.92,
and then tipped it 12° past the jug; both showed a yellow crescent inside the rim, the second only
in Unity's render from the game camera (M7-05p). So the mouth only ever swells (1.0–1.08), tips at
most 8° past the jug, which does the leaning, and the jug's own opening is capped dark under a flat
throat. *(3)* **`pitcher.py` copies
`frog.py`'s shell and pose helpers** rather than sharing a module, so `frog.py` and `Frog.fbx` are
untouched here. A third body script is the time to lift them out, as `enemy_atlas.py` was lifted at
the second.

**Numbers.** 2,106 triangles, after `cull()` dropped 56 faces; 1,155 vertices in Blender and in
Unity. 14 bones, 13 of them deforming. 1.349 m tall, 0.842 m wide, 0.612 m deep. The surface
averages 0.472 luma and 0.597 saturation against the floor's 0.386 and 0.415: +0.086 and +0.182.
The shuffle covers 0.44 m in 0.4 s, 1.1 m/s at 1×. The spit is frame 18, 0.6 s.

**Deviations.** *(1)* **No PROGRESS entry and no ROADMAP row**, as for M7-05a to M7-05n: the
Jungle's art is built ahead of M7-00e's specs, from the spec file alone.

**Verified.** `PitcherModelTests` 8 / 8; with `FrogModelTests` and `RootlingModelTests`, 26 / 26. A
re-run of `pitcher.py`: the PNG identical, the FBX differing in 4 timestamp bytes. Re-runs of
`frog.py` and `rootling.py`: the same atlas, their FBX differing in timestamp bytes alone, restored.
Suite counts are M7-05q's.
