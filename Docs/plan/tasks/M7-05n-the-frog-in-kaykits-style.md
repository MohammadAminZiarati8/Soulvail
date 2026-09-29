# M7-05n — The Frog in KayKit's style

**Size:** M · **Depends on:** M7-05j (the model this replaces), M7-05l (the body that wears it) · **Branch:** `m7-05n-the-frog-in-kaykits-style`
**Design refs:** GD §16.4, GD §17.1; AR §18 (the M2-art rows: Generic, no root motion) · **Ledger rows:** none

The owner's word of 2026-09-29: *"our frog enemy is so far from what we want visually … make it like
the KayKit art style."* A prototype rendered beside KayKit's Skeleton and Ranger showed where the
gap was, and the owner's go followed: *"rebuild the Jungle Frog in KayKit style from the frog_kk
prototype — rewrite frog.py (procedural parts, no Tripo source), refit Rig_Frog and re-check all six
clips, update the enemy atlas, raise the crowd triangle cap and reword GD §17.1."*

## Goal

`Frog.fbx` is a KayKit-style frog: rounded, smooth-shaded shells in a few clean colours, a head
wider than its body, big eyes and short thick limbs. It keeps `Rig_Frog`'s bones and its six clips,
so `Frog.prefab`, `AC_Frog` and the view take it unchanged. It is made by `frog.py` from nothing
outside the repository.

## Why this shape

- **What was wrong was the style, not the frog.** M7-05j cut a Tripo sculpt to 1,200 flat-shaded
  triangles and painted each face a random camo swatch. Beside KayKit's bodies it read as faceted
  polygon art in a noisy, saturated green, with a real tree frog's thin legs and small features.
- **Built the way KayKit builds a character.** KayKit's bodies are separate closed shells that
  intersect, each smooth-shaded and one colour with a soft gradient from its atlas. The script builds
  the frog from about thirty such shells, and binds each one rigidly to one bone. Shells overlap at
  every joint, so a bent joint shows no seam and there is no bone-heat weighting to go wrong.
- **The same skeleton, refitted.** The bone names, parents and the tongue's two-bone stretch are
  M7-05j's, so the mask, the view's three numbers and every test that names a bone still hold. The
  joints move to the new proportions, and the clips are re-authored from the same pose table.
- **Smooth costs triangles, not vertices.** A round silhouette needs about 2,000 triangles where a
  faceted one managed with 1,200. Flat shading, though, splits every vertex once per face: the old
  frog was 594 vertices in Blender and 3,439 in Unity. The new frog's shells share their vertices,
  and skinning cost is per vertex. **GD §17.1's crowd budget is raised to 2,500 triangles.**

## Files

| Path (from the repository root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/frog.py` | Blender 4.1 | **Rewritten.** Builds the shells, the rig, the clips, the atlas; `--pose-sheet <png>` renders the review sheet |
| `Assets/_Project/Art/Enemies/Frog.fbx` | generated | the model, on `Rig_Frog`, with its clips |
| *small edits* | | `Tools/Blender/enemy_atlas.py` (the Frog's swatches); `T_Enemy_Albedo.png` (regenerated); `FrogModelTests.cs` and `RootlingModelTests.cs` (the budget's cap); `Docs/GameDesign.md` §17.1 |

## Public API

```text
PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background \
    --factory-startup --python Tools/Blender/frog.py [-- --pose-sheet <out.png>]
```

`--source` is gone: the script reads no file it did not write.

## Behaviour

1. **One script makes the model and the atlas from nothing outside the repository, and a re-run
   makes the same ones.** The PNG is byte-identical and the FBX differs in its header's timestamp.
2. **It is in the crowd's new budget.** One skinned mesh of 400–2,500 triangles, one material slot,
   remapped to `M_Enemy`. GD §17.1 says 400–2,500, smooth-shaded.
3. **Every vertex is weighted** to at most four bones summing to 1, and none to `root`.
4. **It imports Generic with its six clips**, as M7-05j rule 4: `Idle`, `Hop`, `Attack`, `Leap`,
   `Hit` and `Death` at 30 fps and the same frame counts, only `Idle` and `Hop` looping.
5. **Every clip binds, and none moves the root.**
6. **It stands off the Jungle's floor**, by M7-05j rule 6's margins, and no atlas pixel comes near a
   reserved colour.
7. **The body keeps its numbers.** About a metre tall in the prefab, with the health bar just over
   it. The tongue is furthest out at 0.5 s into `Attack`. The Hop covers 0.9 m.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `FrogModel_StaysInTheCrowdsBudget` | the mesh / triangles / 400–2,500, one submesh (rule 2; the cap moves) |
| `RootlingModel_StaysInTheCrowdsBudget` | the Rootling / triangles / 400–2,500 (rule 2; the cap moves, its model does not) |
| `FrogModel_*` | M7-05j's other six rows, unchanged (rules 3–6) |
| `Frog_StandsAboutAMetreTall`, `Frog_HealthBarFloatsJustOverItsHead`, `Frog_StrikeIsTheTonguesFullestReach`, `Frog_StrideIsTheHopsPointInTheBlend`, `Frog_FlinchLeavesTheHindLegsHopping` | M7-05l's rows, unchanged (rule 7) |

Rule 1 is checked by hand: a re-run and a byte compare. The script also asserts rules 2, 3 and 6
before it writes, so a change that breaks them fails in Blender, before Unity reads the model.

## Manual verification (Editor / device)

1. **[owner]** Read the pose sheet (`frog.py -- --pose-sheet Temp/Renders/frog_pose_sheet.png`):
   every clip bends without a seam opening, the mouth opens dark, and the tongue leaves the mouth
   and returns.
2. **[Editor]** A Jungle run's stage 1: Frogs hop, lash, flinch and flip over dead.

## Out of scope

- The Rootling in the same style. It has the same faceted look and is benched (M7-05m); it gets the
  same treatment when a run brings it back.
- The eye colour. Crimson stands, as M7-05j chose it and the owner has not overruled.
- A Lunger that leaps: the `Leap` clip is kept for it.

## As built

**Deviations.** *(1)* **The clips' numbers moved, not only their joints.** The throat pulse is 1.03–1.06
where it was 1.10–1.14: the jaw is now a shell of its own, scaled about its hinge, and at the old
values it grew into a cream collar around the head. The strike opens the jaw 26° rather than 22°,
so the mouth reads open. Every key frame, and so every clip's length and the strike at frame 15,
is unchanged. *(2)* **No PROGRESS entry and no ROADMAP row**, as for M7-05a to M7-05m: the Jungle's
art has been built ahead of M7-00e's specs, from the spec file alone.

**What changed in the atlas.** Columns 2–3 keep their slots: `frog_green` is a warmer, lighter
lime, `frog_spot` a mid green, and `frog_belly` a warmer cream. Two of the old camo greens became
`frog_shine`, the eyes' glint, and `frog_mouth`, a dark crimson-brown. `frog_red`, `frog_pupil` and
`frog_tongue` are unchanged, and the eyes stay crimson.

**Numbers.** 2,318 triangles, after `cull()` dropped 239 faces buried inside a shell on the same bone.
1,322 vertices, in Blender and in Unity alike, because each shell shares its vertices. The faceted
frog was 1,177 triangles on 3,439 imported vertices. 19 bones, 0.824 m to the top of the eyes, so
0.99 m in the prefab, with the health bar 0.51 m over it. The surface averages 0.601 luma and 0.600
saturation, against the floor's 0.387 and 0.416: +0.214 and +0.183, against bars of 0.05 and 0.15.

**Findings.** *(1)* **One dark shell on the head is not a mouth.** The first build put the mouth
interior on the head bone, inside the head's shell, so `cull()` removed nearly all of it. A dropped
jaw then showed the jaw's own top, which is cream. The mouth is now a roof on the head and a floor
on the jaw, each hidden inside the other's shell while the mouth is shut. *(2)* **Normals are made
per shell before culling.** `cull()` leaves shells open, and Blender's normal recalculation guesses
on an open mesh, so `outward()` winds each shell while it is still closed. *(3)* **Blender does not
resolve `--pose-sheet` from the working directory.** A relative path wrote nowhere visible, and
M7-05j's sheet was read as the new one before the file dates gave it away. The script now makes the
path absolute.

**Verified.** `FrogModelTests`, `FrogBodyTests` and `RootlingModelTests`: 25 / 25. EditMode: 3,484,
of which 3,483 passed, 0 failed and 1 was inconclusive (the animator clock row, by construction).
PlayMode: 66 / 67 on the full pass, queued while Unity was not the active application.
`RangerSandboxTests.Loop_ShootsOnlyStandingStill` failed with "No arrow on the run", and failed again
alone. It passed alone after a clean recompile, which is Traps §8's known sandbox failure, not a
regression. It reads a stick through the Input System and touches no enemy. A re-run of `frog.py`:
the PNG identical, the FBX differing in 5 timestamp bytes. A re-run of `rootling.py`: the same
atlas, `Rootling.fbx` differing in 5 timestamp bytes, restored. Format check clean on both changed
C# files. Console: no errors, one AI Assistant account warning (Known issue 5).
`ProjectSettings/TimeManager.asset`, rewritten by the Editor, was reverted.
