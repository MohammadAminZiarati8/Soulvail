# M7-05j — The Frog's model

**Size:** M · **Depends on:** M7-05f (the shared enemy atlas and material) · **Branch:** `m7-05j-the-frogs-model`
**Design refs:** GD §11.3, GD §16.4, GD §17.1; AR §18 (the M2-art rows: Generic, no root motion) · **Ledger rows:** none

The owner's request of 2026-09-29: *"I have created a frog with Tripo and one with Gemini … check them
and see if you can work with them. Try to create proper animations and textures and material for
them. We want a frog enemy."* With it came a Gemini reference sheet, a red-eyed tree frog in faceted
low-poly camo, and *"if you can fix anything, please do."* The body it becomes is M7-05l.

## Goal

`Frog.fbx`, one skinned mesh of 400–1,200 triangles on a frog skeleton of its own, carrying six
clips of its own, sampling the shared enemy atlas through `M_Enemy`. It is made by one script in the
repository from the owner's Tripo sculpt.

## Why this shape

- **Tripo's shape, nothing else of either source.** The Tripo export is a well-proportioned frog at
  501 600 triangles, with no rig and a mustard bake full of grey holes. The Gemini build is ten loose
  primitives, 15 580 triangles, with a lily pad and a firefly fused in and no UVs. So the script cuts
  the sculpt to the crowd's budget with symmetry, replaces its small eyes with the reference's big
  ones, adds a tongue, and repaints it.
- **A rig of its own, not `Rig_Medium`.** A humanoid walk or chop cannot play on a quadruped, so the
  Frog's clips are authored for it and its bones are a frog's. That gives up the 139 KayKit clips,
  which the Frog could not have used anyway.
- **Poses as targets, legs by IK.** Each key names a body offset, a few joint angles and a target
  per limb, planted on the ground or carried with the body. Two-bone IK solves each leg, so feet
  stay planted as the body moves.
- **Painted face by face from the shared atlas.** The reference's facets are one colour each, and so
  is a swatch strip, so the camo is one swatch per face. The Frog takes the atlas's free columns 2–3
  and wears `M_Enemy`, so it flashes and glows as the Rootling does (M7-05h rule 6).
- **The atlas moves to `enemy_atlas.py`.** Two scripts now write one file. With the swatches in one
  module, a re-run of either writes the same bytes and neither can erase the other's columns.

## Files

| Path (from the repository root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/frog.py` | Blender 4.1 | **New.** Writes the model and the atlas; `--pose-sheet <png>` renders the review sheet |
| `Tools/Blender/enemy_atlas.py` | Blender 4.1 | **New.** The shared atlas's swatches and writer, imported by both body scripts |
| `Assets/_Project/Art/Enemies/Frog.fbx` | generated | the model, on `Rig_Frog`, with its clips |
| `Assets/_Project/Tests/Game/Art/FrogModelTests.cs` | Tests.Game | **New.** Rules 2–6 |
| *small edits* | | `Tools/Blender/rootling.py` imports the atlas; `T_Enemy_Albedo.png` gains columns 2–3 |

## Public API

```text
PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background \
    --factory-startup --python Tools/Blender/frog.py [-- --source <tripo.fbx>] [--pose-sheet <out.png>]
```

## Behaviour

1. **One script makes the model and the atlas, and a re-run makes the same ones.** The PNG is
   byte-identical and the FBX differs in its header's timestamp. A `rootling.py` re-run writes the
   same atlas, and leaves its model differing in its timestamp alone.
2. **It is in the crowd's budget.** One skinned mesh of 400–1,200 triangles, one material slot,
   remapped to `M_Enemy`.
3. **Every vertex is weighted** to at most four bones summing to 1, and none to `root`.
4. **It imports Generic with its six clips.** An avatar from this model, not optimised, clips `Idle`,
   `Hop`, `Attack`, `Leap`, `Hit` and `Death` at 30 fps, with only `Idle` and `Hop` looping.
5. **Every clip binds, and none moves the root.** Every curve resolves to a transform on the model,
   and `root` stays at rest on every frame.
6. **It stands off the Jungle's floor.** Averaged over its surface, it is at least 0.05 lighter and
   0.15 more saturated than `T_JungleGround_Albedo`. No atlas pixel comes near a reserved colour
   (`RootlingModelTests.EnemyAtlas_CarriesNoReservedColour`, unchanged).

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `FrogModel_IsShipped` | the model / loaded / one `SkinnedMeshRenderer` (guards every row) |
| `FrogModel_StaysInTheCrowdsBudget` | the mesh / triangles / 400–1,200, one submesh (rule 2) |
| `FrogModel_UsesTheEnemyMaterial` | the renderer / its materials / `M_Enemy` alone (rule 2) |
| `FrogModel_EveryVertexIsWeighted` | the bone weights / read / sum 1 ± 1e-3, ≤ 4 bones, none on `root` (rule 3) |
| `FrogModel_ImportsGenericWithItsClips` | the importer and clips / read / Generic, own avatar, not optimised, six clips, two loops, 30 fps (rule 4) |
| `FrogModel_EveryClipBindsAndLeavesTheRootAlone` | each clip / every binding, every frame sampled / resolves; `root` still (rule 5) |
| `FrogModel_StandsOffTheJungleFloor` | the atlas at each triangle and the floor texture / averaged / +0.05 luma, +0.15 saturation (rule 6) |

Rule 1 is checked by hand: a re-run of each script and a byte compare.

## Manual verification (Editor / device)

1. **[owner]** Read `Temp/Renders/frog_pose_sheet.png` and `frog_closeup.png` (`frog.py -- --pose-sheet`):
   limbs bend without tearing, the eyes turn with the head, the tongue leaves the mouth and returns.

## Out of scope

- The body, its animator and its place in a run (M7-05l, M7-05m).
- A Lunger that leaps: the `Leap` clip exists for it, and M7-01a owns the archetype.

## As built

**Deviations.** *(1)* **The source is outside the repository**, at the owner's
`Resources/Soulvail/Models/low+poly+frog+3d/`, 15 MB; `--source` names another copy. Only the model
it becomes is committed. *(2)* **The reference's orange eyes and toe pads are crimson**
(`C8203A`), 0.22 from GD §16.4's red-orange in the atlas's measure, because the Frog's wind-up glows
that very colour. *(3)* **The tongue is modelled out**, 0.86 m past the lips, so a tube can stretch
between two bones. The FBX's node transforms are a retracted pose, so the model stands with its
tongue in even without an Animator. *(4)* The Leap clip is authored though no archetype plays it yet.

**Numbers.** 1 177 triangles: the sculpt cut to 919, two eyes of 64, two slit pupils of 24 and a
tongue of 82. 594 vertices in Blender, 3 439 in Unity. 19 bones, 0.865 m to the top of the eyes.
Clips: Idle 1.6 s, Hop 0.6 s, Attack 1.2 s with the tongue furthest out at 0.500 s, Leap 1.0 s,
Hit 0.4 s, Death 1.0 s, belly up by 0.5 s. Mean surface colour: luma 0.481 and saturation 0.651,
against the floor's 0.386 and 0.415.

**Findings.** *(1)* **Tripo's `UVMap` survived the repaint** and the first export sampled the atlas
through Tripo's bake UVs: a grey frog with random red patches. `load_sculpt` removes every UV layer
before `paint` writes its own. *(2)* **Blender 4.1 renders through AgX by default**, which greys a
flat atlas; the pose sheet uses Standard.

**Verified.** `FrogModelTests` 7 / 7. A re-run of `frog.py`: PNG identical, FBX differing in 3
timestamp bytes. A re-run of `rootling.py`: the same atlas, `Rootling.fbx` differing in 6 timestamp
bytes, restored. Suite counts are M7-05m's.
