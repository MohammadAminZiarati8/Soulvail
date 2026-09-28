# M7-05f — The Rootling's model

**Size:** M · **Depends on:** M7-05b (the Blender route and its conventions) · **Branch:** `m7-05f-the-rootlings-model`
**Design refs:** GD §8.1, GD §11.3, GD §16.4, GD §17.1; AR §18 (the M2-art rows: Generic, no root motion) · **Ledger rows:** none

The third task cut from the owner's go of 2026-09-27: *"a small hunched bundle of roots and moss
around a pale skull-like face, coming in packs. The body is pale (bone, fungus-white, dry bark), not
green … no cyan, violet or gold … a black silhouette at phone scale, 400–1,200 triangles. Script the
mesh in Blender like `jungle_kit.py`, skinned onto KayKit's `Rig_Medium` skeleton with the same bone
names."* The body it becomes in a run is M7-05h.

## Goal

`Rootling.fbx`, one skinned mesh of 400–1,200 triangles on KayKit's unchanged `Rig_Medium`, so
every `Rig_Medium` clip plays on it. It is made by one script in the repository, and it samples
the first swatches of a shared enemy atlas through the one enemy material every later enemy body
will share.

## Why this shape

- **The rig is read, never rebuilt.** The script loads `Skeleton_Minion.fbx`, a KayKit enemy on
  `Rig_Medium`, and keeps its armature and throws its meshes away. Unity binds a Generic clip by
  transform path (`Rig_Medium/root/hips/…`), so equal names, and rest positions equal to the
  millimetre, are what let the 139 clips play with no retargeting (VERSIONS.md, RS-01b rule 1).
- **Weights are written, not computed.** Each part is built for a bone, and a tube that crosses a
  joint blends along its length. Bone-heat weighting needs a manifold mesh, and a bundle of roots
  is many closed pieces.
- **One atlas and one material for every enemy** (GD §11.3, §17.1). `T_Enemy_Albedo` holds the
  Rootling's swatches in the layout M7-05b uses. Its first two columns are the Rootling's, and the
  rest is free for the Jungle's other bodies. `M_Enemy` has emission on at black, so a property block can
  flash it white on a hit and glow it red-orange on a wind-up (M7-05h). A texture multiplied by
  `_BaseColor` cannot flash white.
- **Hunched by its clips and its mass, not by its rig.** A clip drives every bone, so a mesh built
  hunched over an upright rest pose would be stood up by the first frame. The mass sits low and
  forward on `chest` and `hips` with a mossy hump behind. M7-05h picks crouched clips.

## Files

| Path (from the repository root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/rootling.py` | Blender 4.1 | **New.** Writes the two rows below; the only place they change. `--pose-sheet <png>` also renders the review sheet |
| `Assets/_Project/Art/Enemies/Rootling.fbx` | generated | the model, on `Rig_Medium` |
| `Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png` | generated | the shared enemy atlas |
| `Assets/_Project/Materials/Enemies/M_Enemy.mat`, `M_Enemy_Dissolve.mat` | — | the enemy material, and its transparent twin for the dissolve |
| `Assets/_Project/Tests/Game/Art/RootlingModelTests.cs` | Tests.Game | **New.** Rules 2–7 |

Only these files change. Anything else is a deviation: say so in *As built*. The pose sheet is
written outside the repository.

## Public API

```text
PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background \
    --factory-startup --python Tools/Blender/rootling.py [-- --pose-sheet <out.png>]
```

## Behaviour

1. **One script makes the model and the atlas, and running it again makes the same ones.** Its
   random streams are seeded. The PNG is byte-identical on a re-run, and the FBX differs only in
   the header's timestamp (M7-05b's finding).
2. **The skeleton is `Rig_Medium`, unchanged.** It has the 23 bones of `Skeleton_Minion.fbx`, under
   an armature named `Rig_Medium`, with the same paths. Each bone's imported local position is
   within 1 mm of the Minion's, and each rotation within 0.1°. No `_end` leaf bones.
3. **Every `Rig_Medium` clip binds.** Every curve path in the eight `Rig_Medium_*.fbx` files resolves
   to a transform on the imported model.
4. **It is in the crowd's budget.** It is one skinned mesh of 400–1,200 triangles with one material
   slot, and the importer remaps that slot to `M_Enemy`.
5. **Every vertex is weighted.** Weights sum to 1 over at most 4 bones, and `root`, `handslot.l` and
   `handslot.r` carry none. The skull is rigid on `head`.
6. **It is pale, and it carries no reserved colour.** Averaged over its surface, the colour it
   samples is light (luminance at least 0.55) and unsaturated (HSV saturation at most 0.25), so it
   reads against the Jungle's green floor. No atlas pixel is within 0.12 in RGB of the player's
   cyan, danger's red-orange, Veilrot's violet or reward gold (GD §16.4).
7. **It imports the way KayKit's rigs import.** Generic, with an avatar made from this model, no
   animation, *Optimize Game Objects* off, and root motion no concern of the model. The atlas is
   point-filtered (VERSIONS.md). `M_Enemy` is URP Lit at smoothness 0.05, reading the atlas, with
   `_EMISSION` on and a black emission colour. `M_Enemy_Dissolve` is the same, transparent.
8. **The silhouette is its own.** Seen from the game camera as a black shape, the Rootling is a low,
   wide mound with a crown of root spikes behind a round skull, and long root arms. Neither grey
   capsule the Spitter and the Bloater wear has that shape. This is checked on the pose sheet and a
   black-fill capture, recorded in *As built*.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `RootlingModel_IsShipped` | `Art/Enemies/Rootling.fbx` / loaded / found, with one `SkinnedMeshRenderer` (guards every row below) |
| `RootlingModel_WearsRigMedium` | the model and `Skeleton_Minion.fbx` / their transforms under `Rig_Medium` / the same paths; positions within 1 mm, rotations within 0.1° (rule 2) |
| `RootlingModel_EveryRigMediumClipBinds` | the eight clip files / every curve binding / its path resolves on the model (rule 3) |
| `RootlingModel_StaysInTheCrowdsBudget` | the mesh / triangles counted / 400–1,200, one submesh (rule 4) |
| `RootlingModel_UsesTheEnemyMaterial` | the renderer / its materials / `M_Enemy` alone (rule 4) |
| `RootlingModel_EveryVertexIsWeighted` | the mesh's bone weights / read / sum 1 ± 1e-3, ≤ 4 bones, none on `root` or a handslot (rule 5) |
| `RootlingModel_IsPale` | the atlas sampled at each vertex's UV, weighted by triangle area / averaged / luminance ≥ 0.55, saturation ≤ 0.25 (rule 6) |
| `EnemyAtlas_CarriesNoReservedColour` | every pixel of the atlas / compared / none within 0.12 of the four reserved colours (rule 6) |
| `RootlingModel_ImportsGenericWithoutAnimation` | the importer / read / Generic, `CreateFromThisModel`, no animation, not optimised (rule 7) |
| `EnemyAtlas_IsPointFiltered` | the atlas importer / read / `Point` (rule 7) |
| `EnemyMaterial_CanGlow` | `M_Enemy` and `M_Enemy_Dissolve` / read / the atlas, `_EMISSION` on, black emission; the twin transparent (rule 7) |

Rules 1 and 8 are checked by hand: a re-run and `git status`, and the pose sheet.

## Manual verification (Editor / device)

1. **[Editor]** Run the script and `git status`. *Expected:* the PNG unchanged, the FBX differing in
   its header only.
2. **[owner]** Read the pose sheet: walk, attack, hit and death at three frames each. *Expected:*
   limbs bend without tearing, the skull turns as one piece, and nothing inverts at the hips.

## Out of scope

- The prefab, the animator and the run (M7-05h, M7-05i).
- The Jungle's other enemies' bodies. Their swatches go in the atlas's free columns.
- A level of detail (M8-03, if a phone asks).

## As built

**Deviations.** *(1)* **The script needs `PYTHONHASHSEED=0` and refuses to run without it** (rule 1).
Blender's FBX exporter ids every node with Python's `hash()`, which is seeded afresh per process.
Unseeded, two runs differed in 1 186 bytes of object ids. Seeded, they differ in 4, the timestamp.
The PNG is byte-identical either way. Traps §5. *(2)* **The export uses *Apply Scalings: FBX All*.**
With the default, `Rig_Medium` imported at scale 100 over bones a hundredth of the size: the same
paths, every clip wrong. *(3)* **`M_Enemy` and its twin carry the GI flag `RealtimeEmissive`.** URP
switches `_EMISSION` off on import, and in the Inspector, when no emissive bit is set, so a black
emission under `EmissiveIsBlack` or `None` lost the keyword. `EnemyMaterial_CanGlow` pins the flag.
Traps §5. *(4)* The Rootling takes the atlas's first **two** columns, six swatches, not one; the
spec was corrected.

**Numbers.** 1 080 triangles and 618 vertices in Blender (2 424 in Unity, split by flat shading and
UV seams). One submesh. 23 bones, at most 2 influences a vertex. 1.71 m at rig size, crown
included. Atlas 512 px in 64 × 128 strips. Mean colour over the surface: luminance 0.656 against
the 0.55 floor, saturation 0.180 against the 0.25 ceiling. Imported, every transform under
`Rig_Medium` equals `Skeleton_Minion`'s to 0.00000 m and 0.000°.

**Finding — hunched is the mesh's job and the clip's.** The clips drive every bone, so the look
took three passes, each judged on the pose sheet from the game camera's 57°. The first was an
upright root-golem with the skull on top. The second set the skull in the bundle, but hid the face
under the hump and read grey-green from above. The third, shipped, sets the skull low and forward,
leans the hump back, shrinks and bleaches the lichen, and arches a hood of roots over the face.
Black on white beside a capsule, it is a low spiked mound with claws.

**Pose sheet.** `rootling.py -- --pose-sheet`, reloaded from the FBX. Seven clips (`Sneaking`,
`Crouching`, `Skeletons_Walking`, `Melee_2H_Attack_Chop`, `Melee_Unarmed_Attack_Punch_A`, `Hit_A`,
`Death_A`), three frames each from the game view and three-quarters, and a silhouette row. No limb
tears, the skull turns as one piece, and nothing inverts at the hips.

**Verified.** `RootlingModelTests` 11 / 11, with `JungleKitTests`' 33 unchanged. A re-run with the
seed: PNG identical, FBX differing in its 4 timestamp bytes. Suite counts are M7-05d's *As built*.
Renders: `Temp/Renders/rootling_pose_sheet.png`.
