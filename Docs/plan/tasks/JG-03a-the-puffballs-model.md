# JG-03a — The Puffball's model

**Size:** S · **Depends on:** M7-05o (the method, `pitcher.py`), M7-05n (`enemy_atlas.py`) · **Branch:** `dev` (direct)
**Design refs:** GD §8.1, GD §16.4, GD §17.1; AR §18 (the M2-art rows: Generic, no root motion)

The owner's go of 2026-10-22: the Jungle's Bloater becomes a puffball mushroom — *"a fat, round
puffball mushroom in KayKit's style, pale cream cap with a few darker spots, a short stubby stalk, two
small eyes so it has a front. It must not read like the Frog (low and wide, green) or the Pitcher (tall
jug) as a black silhouette."* The Pitcher's three steps are the pattern: this model, then the body
(JG-03b), then the content (JG-03c).

## Goal

`Puffball.fbx`, one skinned mesh of 400–2,500 triangles on a skeleton of its own with five clips of its
own, sampling the shared enemy atlas through `M_Enemy`, made by one script from nothing outside the
repository.

## Why this shape

- **Built as the Pitcher is.** Smooth closed shells of one colour each, bound rigidly to one bone and
  overlapping at every joint; `puffball.py` copies `pitcher.py`'s shell, posing and export helpers, as
  `pitcher.py` copied `frog.py`'s.
- **Round where the Frog is wide and the Pitcher tall.** A ball about a metre across on a stub of a
  stalk: height over width between the Frog's (≤ 1) and the Pitcher's (≥ 1.5), and as deep as it is
  wide, so it is a circle from the game camera, with nothing sticking out.
- **No legs.** It waddles by rocking from one edge of its stalk's foot to the other with a little hop
  between, the whole body tipping; each frame is lifted so its lowest point meets the floor.
- **The cap is the telegraph.** `Swell` inflates the cap on its bone, trembling, to its largest on the
  frame the Bloater detonates; `Burst` pops it a little further and collapses it to a flat skin. The
  glow is the body's (JG-03b), not the model's.
- **Pale, as the Rootling is**, not saturated as the Frog and the Pitcher are: a cream cap and stalk
  with warm tan spots and a darker pore on top. It stands off the green floor by lightness, so it is
  held to `RootlingModelTests`' bars. Columns 6–7 of the atlas, the last free ones.

## Files

| Path (from the repository root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/puffball.py` | Blender 4.1 | **New.** Writes the model and the atlas; `--pose-sheet <png>` renders the review sheet |
| `Assets/_Project/Art/Enemies/Puffball.fbx` | generated | the model, on `Rig_Puffball`, with its clips; its `.meta` holds the import settings |
| `Assets/_Project/Tests/Game/Art/PuffballModelTests.cs` | Tests.Game | **New.** Rules 2–7 |
| *small edits* | | `Tools/Blender/enemy_atlas.py` (the Puffball's swatches, columns 6–7); `T_Enemy_Albedo.png` (regenerated) |

## Public API

```text
PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background \
    --factory-startup --python Tools/Blender/puffball.py [-- --pose-sheet <out.png>]
```

## Behaviour

1. **One script makes the model and the atlas, and a re-run makes the same ones.** The PNG is
   byte-identical and the FBX differs in its header's timestamp. `frog.py`, `rootling.py` and
   `pitcher.py` re-runs write the same atlas.
2. **It is in the crowd's budget.** One skinned mesh of 400–2,500 triangles, one material slot,
   remapped to `M_Enemy`.
3. **Every vertex is weighted** to at most four bones summing to 1, and none to `root`.
4. **It imports Generic with its five clips**: `Idle` (48 f), `Waddle` (20 f), `Swell` (30 f, the cap
   largest on frame 21), `Burst` (24 f, flat by frame 9) and `Hit` (12 f), at 30 fps, only `Idle` and
   `Waddle` looping. An avatar from this model, not optimised.
5. **Every clip binds, and none moves the root.**
6. **It is pale on the Jungle's floor**: averaged, luma at least 0.55 and saturation at most 0.25
   (the Rootling's bars), and no atlas pixel comes near a reserved colour
   (`RootlingModelTests.EnemyAtlas_CarriesNoReservedColour`, unchanged).
7. **It is round.** At rest its height over its width is 1.0–1.35 and its depth over its width
   0.85–1.15; the Frog's height over width stays at most 1 and the Pitcher's at least 1.5.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `PuffballModel_IsShipped` | the model / loaded / one `SkinnedMeshRenderer` (guards every row) |
| `PuffballModel_StaysInTheCrowdsBudget` | the mesh / triangles / 400–2,500, one submesh (rule 2) |
| `PuffballModel_UsesTheEnemyMaterial` | the renderer / its materials / `M_Enemy` alone (rule 2) |
| `PuffballModel_EveryVertexIsWeighted` | the bone weights / read / sum 1 ± 1e-3, ≤ 4 bones, none on `root` (rule 3) |
| `PuffballModel_ImportsGenericWithItsClips` | the importer and clips / read / Generic, own avatar, not optimised, five clips, two loops, 30 fps (rule 4) |
| `PuffballModel_EveryClipBindsAndLeavesTheRootAlone` | each clip / every binding, every frame sampled / resolves; `root` still (rule 5) |
| `PuffballModel_IsPaleOnTheJungleFloor` | the atlas at each triangle / averaged / luma ≥ 0.55, saturation ≤ 0.25 (rule 6) |
| `PuffballModel_IsRoundWhereTheOthersAreNot` | the three meshes at rest / proportions / as rule 7 (rule 7) |

Rule 1 is checked by hand: a re-run of each script and a byte compare. The script also asserts rules
2, 3 and 6 before it writes.

## Manual verification (Editor / device)

1. **[Editor]** The pose sheet (`puffball.py -- --pose-sheet Temp/Renders/puffball_pose_sheet.png`):
   the body rocks and hops, the cap trembles and swells, pops and collapses flat, and squashes back on
   a hit; the last row's black shapes tell the Frog, the Pitcher and the Puffball apart.
2. **[Editor]** Seen in a run at JG-03c.

## Out of scope

- The body, its glow, its burst VFX and its place in a run (JG-03b, JG-03c).
- Lifting the shared shell helpers out of `frog.py`, `pitcher.py` and `puffball.py` into one module:
  it rewrites two shipped scripts and regenerates two shipped models.

## As built

**As specified**, after two review rounds on the pose sheet (`Temp/Renders/puffball_pose_sheet.png`).
They changed: the eyes grew a third, which they need to read from the game camera; and `Burst` ended as
a wide plate standing on its stalk, because the cap rides the stalk's scale and a squashed stalk
widened and flattened the cap twice over. The cap's own scale now compensates, so the deflated skin is
1.14 m across and 0.32 m tall in all, sitting on a squashed foot.

**Findings.** *(1)* **An FBX clip keys the armature object's own transform**, so a pose sheet that moves
an imported armature to lay bodies side by side sees it snap back to the origin on the next frame drawn,
and the three silhouettes rendered on top of each other. Each now sits under an offset empty.
*(2)* **Rule 4's frame numbers are not test rows**, as the Pitcher's spit frame was not at M7-05o: the
strike frame is measured where the body uses it, in JG-03b's `PuffballBodyTests`.

**Numbers.** 1,179 triangles, after `cull()` dropped 25 faces; 662 vertices. 4 bones, 2 of them deforming
(`stalk`, `cap`). 1.025 m tall, 0.980 m wide, 0.985 m deep: height over width 1.05, depth over width
1.01. The surface averages 0.797 luma and 0.185 saturation. At the strike the cap is 1.28 × 1.26 on a
stalk at 1.06 × 1.04, so 1.36 wide in all. The waddle's nominal 0.9 m in 20 frames is 1.35 m/s at 1×.

**Deviations.** None. The stand-off check copied from `pitcher.py` became a pale check (rule 6), and no
floor texture is read.

**Verified.** `PuffballModelTests` 8 / 8; with the Pitcher, Frog and Rootling model fixtures, 34 / 34.
EditMode suite through `TestRunnerApi`: **4 397 — 4 395 passed / 1 failed / 1 inconclusive**. The failure
is `CharacterPreviewTests.Resize` (known); the inconclusive row is
`PlayerAnimatorViewTests.Animator_AttackSpeedFollowsTheSwingRatioWhenTheClockRuns`, as at RS-07b. A
re-run of `puffball.py`: the PNG identical, the FBX differing in 4 timestamp bytes. A re-run of
`pitcher.py` writes the same atlas (its FBX restored).
