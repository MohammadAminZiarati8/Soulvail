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

_Filled at merge._
