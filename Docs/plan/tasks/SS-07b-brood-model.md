# SS-07b — The model: the owner's brood sculpt on Rig_Scorpion, turnaround first

**Size:** M · **Depends on:** — (the owner's sculpt, sent with the go) · **Branch:** `ss-07b-brood-model`
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

1. **The sculpt is the owner's.** Prompt (TRELLIS, one image or text): *"Low-poly stylised fat desert brood-mother scorpion, KayKit style, top-down readable, six short legs, two heavy pincers, segmented tail curled over the back ending in a fat stinger bulb, a large swollen ribbed brood sac on its back, no babies modelled, matte sandstone and dusty ochre body, pale bleached-bone sac, no glow."* `repair_scan.py` closes it.
2. **Turnaround first.** `--turnaround <png>` renders the unbound body front, side, back, top to `Temp/brood_turnaround.png` (not committed). Binding waits for the owner's OK in chat.
3. **The bones match or it stops.** `Rig_Scorpion`'s bones, names and hierarchy unchanged, rest positions refitted to the sculpt's legs, pincers and tail. If legs, pincers or tail cannot be fitted, stop and tell the owner.
4. One added bone, `Brood`, under `Body` at the hump's centre, weighted to the hump alone. No clip keys `Brood`.
5. Clips: `scorpion.py`'s `Idle`, `Skitter`, `Windup`, `Strike`, `Hit`, `Death`, baked on the refitted rest, same frame counts, 30 fps, in place; `Root` never moves.
6. ≤ 4 weights per vertex, every vertex weighted; 1 500–3 000 triangles, one skinned mesh, one material slot (`M_Enemy`). Footprint 1.6–2.2 × the scorpion's.
7. Palette: `brood_*` swatches, body darker than `scorp_*`, sac the palest swatch on it, standing off the Dunes sand. No cyan, nothing near `#FF4A1F` (`RESERVED`). Swatches go in as `INSETS`; no other swatch's pixels move.

## Tests

| Test | Given / When / Then |
|---|---|
| `BroodModel_IsShipped` | fbx / load / one `SkinnedMeshRenderer` |
| `BroodModel_StaysInBudget` | mesh / count / rule 6's tris, 1 submesh |
| `BroodModel_UsesTheEnemyMaterial` | renderer / read / `M_Enemy` only |
| `BroodModel_IsWeighted` | skin / read / all weighted, ≤ 4 |
| `BroodModel_HasRigScorpionAndBrood` | rig / walk / every `Rig_Scorpion` bone by path; `Brood` under `Body` |
| `BroodModel_ImportsGenericWithItsClips` | importer / read / Generic; the six clips, frame counts as `Scorpion.fbx` |
| `BroodModel_ClipsLeaveRootAndBroodAlone` | each clip / sample / `Root`, `Brood` still |
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

_Commit body._
