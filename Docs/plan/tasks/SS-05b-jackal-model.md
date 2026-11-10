# SS-05b — The model: the jackal on Rig_Medium, a sling prop, turnaround first

**Size:** M · **Depends on:** — · **Branch:** `ss-05b-jackal-model`
**Design refs:** GD §8.5, §3 › The Sunscar, §16.4, §17.1

## Goal

`Jackal.fbx` and `JackalSling.fbx` exist: the owner's jackal sculpt repaired and skinned to the unchanged KayKit `Rig_Medium`, and a sling built in script for the right hand.

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/jackal.py` | — | repair, bind, sling, checks, sheets; `shaman.py`'s shape |
| `Tools/Blender/Sources/Jackal_src.glb` | — | the owner's sculpt, repaired once, hash pinned |
| `Assets/_Project/Art/Enemies/Jackal.fbx` (+ `.meta`) | — | body; Generic, in place |
| `Assets/_Project/Art/Enemies/JackalSling.fbx` (+ `.meta`) | — | static prop |
| `Assets/_Project/Tests/Game/Art/JackalModelTests.cs` | Tests.Game | `ShamanModelTests`' rows |
| *small edits* | | `enemy_atlas.py` + `T_Enemy_Albedo.png`: `jackal_*` swatches |

## Behaviour

1. **The sculpt is the owner's.** Generation prompt (TRELLIS, one image or text): *"Low-poly stylised jackal-headed desert raider, KayKit style, T-pose, tall pointed ears, long snout, wrapped head-cloth, short ragged cloak and sash, bare forearms, sandals, no weapon, matte bleached bone and dusty ochre, no glow."* `repair_scan.py` closes it; the raw stays read-only.
2. **Turnaround first.** `--turnaround <png>` renders the repaired, unrigged body with the sling beside it, front, side, back and top, to `Temp/jackal_turnaround.png` (not committed). Binding and sheets start only on the owner's OK in chat.
3. Bound to `Rig_Medium` unchanged (23 bones), ≤ 4 weights per vertex, every vertex weighted; height within ±10 % of the Mossguard's.
4. The sling, built from shells in the script: two cords and a cupped pouch, ~0.6 m, one atlas swatch; a `Stone` empty at the pouch's centre (SS-05c's glow parent). Its grip origin sits where `handslot.r` holds it.
5. Palette: `jackal_*` swatches in bleached bone, dusty ochre and darker leather, standing off the Dunes sand. No cyan, nothing near `#FF4A1F` (`RESERVED`). Swatches go in as `INSETS`; no other swatch's pixels move.
6. Clips are the shared Rig_Medium FBXs, not copied into the body: `Idle_B`, `Walking_B`, `Walking_Backwards`, `Ranged_1H_Aiming` (wind-up), `Throw` (release), `Hit_A`, `Death_A`. `--pose-sheet <png>` renders each with the sling held.
7. Body 1 500–3 000 triangles, sling ≤ 300; one skinned mesh and one material slot (`M_Enemy`) each.

## Tests

| Test | Given / When / Then |
|---|---|
| `JackalModel_IsShipped` | fbx / load / one `SkinnedMeshRenderer` |
| `JackalModel_StaysInBudget` | meshes / count / rule 7 |
| `JackalModel_UsesTheEnemyMaterial` | both renderers / read / `M_Enemy` only |
| `JackalModel_IsWeightedToRigMedium` | skin / read / 23 bones, all weighted, ≤ 4 |
| `JackalModel_ImportsGenericInPlace` | importer / read / Generic, no root motion |
| `JackalModel_StandsMossguardHigh` | bounds / measure / ±10 % |
| `JackalSling_HasItsStone` | prop / walk / `Stone` child |
| `JackalModel_StandsOffTheDunes` | colour / vs Dunes sand / `ScorpionModel_StandsOffTheDunes`' margins |
| `JackalModel_NoCyanOrDanger` | swatches / hue / none in the reserved bands |
| `JackalSource_HashPinned` | `Jackal_src.glb` / hash / matches the script |

Every other model test stays green (rule 5's atlas check).

## Manual verification (Editor / device)

1. **[Editor]** Turnaround approved by the owner before binding.
2. **[Editor]** Pose sheet: sling in the right hand in every clip; ears and snout read from above.

## Out of scope

- Prefab, animator, glow, stone projectile (SS-05c). Data (SS-05d).
- Authored clips of its own; a second variant.

## As built

_Commit body._
