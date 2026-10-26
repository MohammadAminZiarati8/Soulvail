# JG-04a — Old Croaker's model

**Size:** S · **Depends on:** M7-05n (`frog.py`) · **Branch:** `dev` (direct)
**Design refs:** GD §9.1, GD §16.4

The first of five tasks for Old Croaker, the Jungle's stage-5 boss (owner's go, 2026-10-26). It is a
giant, old frog that hop-slams, lashes its tongue down a lane and croaks up Frogs. The owner asked about
Trellis; the ruling was no. A generated mesh is what M7-05n replaced, and it would come without a rig.

## Goal

`Croaker.fbx`: one skinned mesh on `Rig_Frog` with the boss's own clips, built by a script, not
sculpted.

## Files

| Path | Purpose |
|---|---|
| `Tools/Blender/croaker.py` | **New.** Imports `frog.py`'s builder and pose solver; adds the boss shells and clips |
| `Assets/_Project/Art/Enemies/Croaker.fbx` | **New.** Output of `croaker.py` |
| `Assets/_Project/Tests/Game/Art/CroakerModelTests.cs` | **New.** Rules 1–4 |
| *small edits* | `Tools/Blender/frog.py`: split `build()` and the clip table so another script can reuse them, without changing `Frog.fbx` |

## Behaviour

1. **Same skeleton.** Bone names and parents are exactly `Frog.fbx`'s, so `AM_Frog_UpperBody` masks it.
2. **Old and big, not just scaled.** About 3.4 m tall at 1×, which is ~3× the Frog. It has darker, mossier
   swatches from `enemy_atlas.py` and a moss mat on the back, warts, and a crown of three small
   mushrooms. Every colour obeys GD §16.4, and the eyes stay crimson.
3. **Clips**, 30 fps, in place, no root motion:
   `Idle` (loop) · `Hop` (loop) · `Slam` (crouch, launch, airborne hold, impact on a frame named in the
   docstring) · `Lash` (wind-up, tongue at full reach on a named frame) · `Croak` (throat sac swells
   and pulses) · `Hit` · `Death` (rolls onto its back, as the Frog does).
4. The `Frog.fbx` that `frog.py` writes after the refactor is unchanged: same vertex count, same bounds
   and same clip lengths.

## Tests

| Test | Rule |
|---|---|
| `Croaker_SharesTheFrogsBones` | 1 |
| `Croaker_StandsAboutThreeFrogsTall` | 2 |
| `Croaker_HasEveryClip_WithTheNamedLengths` | 3 |
| `Frog_UnchangedByTheRefactor` (existing `FrogModelTests` stay green) | 4 |

## Out of scope

Prefab, controller and any code (JG-04d); the mud hazard.

## As built

_2026-10-26._ **Croaker.fbx: 3,317 triangles, 1,945 vertices, 3.42 m tall at 1×**, which is 3.4 Frogs as
the Frog stands in the arena (its prefab is 1.2×). 4 / 4 rows green, plus every `Tests.Game.Art` row
(538 / 538) through `TestRunnerApi`. Console clean, `dotnet format whitespace` clean.

- **frog.py split for reuse, Frog.fbx unchanged.** `skeleton`/`build_armature` take a scale; `Poser` and
  `author_clips` take a `unit`, so poses stay in the Frog's metres; `assemble`, `check`, `export` and
  `pose_sheet` take a name, budget and label, defaulting to the Frog's; `spots()` is split into
  `surface()` + `landing()`; `cull()` reads `Part.closed` rather than the spot swatch; `main()` runs only
  as `__main__`. A re-run differs from the committed Frog.fbx only in the 7 header-timestamp bytes, so
  the committed file was kept. `Frog_UnchangedByTheRefactor` pins 1,322 vertices, the bounds and the
  six clip lengths.
- **croaker.py** builds the Frog's shells at `SCALE` 3.75, repaints `frog_green` → `croak_skin`, and adds
  a lumpy moss mat, drooping lids, a throat sac on the jaw (so `throat` swells it), ten pale warts and
  three wine-capped mushrooms. Its boss budget is 2,500–5,000 triangles, and the Frog's stand-off check
  runs on it (luma 0.553, saturation 0.592, on a floor of 0.386 / 0.415).
- **Clips are timed to JG-04b at speed 1:** Idle 60 (loop), Hop 24 (loop, the Frog's at ¾ speed),
  **Slam 45 with impact on frame 27 (0.9 s)**, **Lash 42 with full reach on frame 24 (0.8 s)**, where the
  tip measures 9.01 m out, the lane's length. Croak 48, with the call on frame 30 (1.0 s). Hit 12 (the
  Frog's), Death 36 (the Frog's at 5/6 speed).
- **Outside the Files table (said here):** the atlas was full but for two cells, so `enemy_atlas.py`
  gains `croak_skin` and `croak_moss` in column 7 rows 2–3, and `T_Enemy_Albedo.png` is rewritten (the
  other cells are unchanged). Every other Croaker part reuses an existing swatch. `Croaker.fbx.meta`
  matches the Frog's importer (Generic, from this model, unoptimised, M_Enemy remap, no cameras, lights
  or blend shapes).
