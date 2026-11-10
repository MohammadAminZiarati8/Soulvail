# SS-07b — The model: the owner's brood sculpt on Rig_Scorpion, turnaround first

**Size:** M · **Branch:** `ss-07b-brood-model`
**Design refs:** GD §8.7, §8.4, §3 › The Sunscar, §16.4, §17.1

## Goal

`BroodMother.fbx`: the owner's sculpt on `Rig_Scorpion` refitted to it, a `Brood` hump bone, `scorpion.py`'s six clips.

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/brood.py` | — | repair, refit, bind, clips, checks |
| `Tools/Blender/Sources/BroodMother_src.glb` | — | the sculpt, SHA-256 pinned |
| `Assets/_Project/Art/Enemies/BroodMother.fbx` (+ `.meta`) | — | Generic, clips split, in place |
| `Assets/_Project/Tests/Game/Art/BroodMotherModelTests.cs` | Tests.Game | `ScorpionModelTests`' rows |
| *small edits* | | `scorpion.py`: rig/clip builders take a rest pose, output unchanged; `enemy_atlas.py` + `T_Enemy_Albedo.png`: `brood_*` swatches |

## Behaviour

1. **Use the owner's seed-42 sculpt.** `repair_scan.py` closes it.
2. **Turnaround first.** `--turnaround <png>` renders the unbound body front, side, back, top to `Temp/brood_turnaround.png` (not committed). Binding waits for the owner's OK in chat.
3. **The bones match or it stops.** `Rig_Scorpion`'s bones, names and hierarchy unchanged, rest positions refitted to the sculpt's legs, pincers and tail. Stop if they cannot be fitted.
4. One added bone, `Brood`, under `Body` at the hump's centre, weighted to the hump alone. No clip keys `Brood`.
5. Clips: `scorpion.py`'s `Idle`, `Skitter`, `Windup`, `Strike`, `Hit`, `Death`, baked on the refitted rest, same frame counts and keyed frames, 30 fps, in place; `Root` never moves. `brood.py` may pass its own `tail` tuples (`COCKED`, `EXTENDED`, `DOWN`, `Windup` frame 7) so the shorter tail arcs over the sac; nothing else changes. The stock strike point is not required.
6. ≤ 4 weights per vertex, every vertex weighted; 1 500–3 000 triangles, one skinned mesh, one material slot (`M_Enemy`). Footprint 1.6–2.2 × the scorpion's.
7. Palette: `brood_*` swatches, body darker than `scorp_*`, sac the palest swatch on it, standing off the Dunes sand. No cyan, nothing near `#FF4A1F` (`RESERVED`). Swatches go in as `INSETS`; no other swatch's pixels move.

## Tests

| Test | Given / When / Then |
|---|---|
| `BroodModel_IsShipped` | fbx / load / one `SkinnedMeshRenderer` |
| `BroodModel_StaysInBudget` | mesh / count / rule 6's tris, 1 submesh |
| `BroodModel_UsesTheEnemyMaterial` | renderer / read / `M_Enemy` only |
| `BroodModel_IsWeighted` | skin / read / all weighted, ≤ 4 |
| `BroodModel_HasRigScorpionAndBrood` | rig / walk / `Rig_Scorpion`'s bones by path; `Brood` under `Body` |
| `BroodModel_ImportsGenericWithItsClips` | importer / read / Generic; the six clips, frame counts as `Scorpion.fbx` |
| `BroodModel_ClipsLeaveRootAndBroodAlone` | each clip / sample / `Root`, `Brood` still |
| `BroodModel_TailClearsTheSac` | every clip frame / `Tail_*`, `Stinger` / outside sac and torso bounds |
| `BroodModel_StrikeLandsAhead` | `Strike` frame 2 / `Stinger` tip / past the torso front, between the pincers, ≤ 0.3 m off centre |
| `BroodModel_IsBiggerThanTheScorpion` | bounds vs `Scorpion.fbx` / measure / footprint ratio in rule 6 |
| `BroodModel_StandsOffTheDunes` | colour / vs Dunes sand / `ScorpionModel_StandsOffTheDunes`' margins |
| `BroodModel_NoCyanOrDanger` | swatches / hue / none in the reserved bands |
| `BroodSource_HashPinned` | source / hash / matches the script |

Every other model test stays green (rule 7; `scorpion.py`'s output byte-identical).

## Manual verification (Editor / device)

1. **[Editor]** Turnaround approved by the owner before binding.
2. **[Editor]** Each clip previewed: no foot sliding in `Skitter`; the hump does not tear when the tail cocks.

## Out of scope

- Prefab, animator, swell (SS-07c). Data (SS-07d).
- Its own rig or clips (rule 3 stops); a scorpling model.

## As built

Use the owner's read-only seed-42 scan with a pinned SHA-256. Repair and
reduce it to 2,960 triangles. Use the baked source tones to shade two
atlas insets, with the pale sac on its own unkeyed Brood bone.

Refit Rig_Scorpion in place. The brood's tail angles clear the torso and
sac through all six clips and put the frame-2 sting past the torso,
between the pincers. A fixed-timestamp stock Scorpion export is byte
identical before and after.

Unity EditMode ran 6,516 tests with 6,424 passed, zero failed, one
inconclusive and 91 skipped. Brood model checks passed 13/13. C# whitespace
passed. The owner approved the unbound turnaround. Blender pose renders
cover all six clips. Editor playback for Skitter foot contact is in OWED.
