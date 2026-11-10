# SS-06b — The model: beetle.py, Rig_Beetle, seven clips, turnaround first

**Size:** M · **Depends on:** — · **Branch:** `ss-06b-beetle-model`
**Design refs:** GD §8.6, §3 › The Sunscar, §16.4, §17.1

## Goal

`Beetle.fbx` exists: a KayKit-style bombardier beetle built from nothing in `beetle.py` on its own six-leg rig, `scorpion.py`'s method, with seven authored clips and the shared enemy atlas.

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/beetle.py` | — | shells, rig, clips, checks; `scorpion.py`'s shape |
| `Assets/_Project/Art/Enemies/Beetle.fbx` (+ `.meta`) | — | output; Generic, clips split |
| `Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png` | — | regenerated atlas |
| `Assets/_Project/Tests/Game/Art/BeetleModelTests.cs` | Tests.Game | `ScorpionModelTests`' rows |
| *small edits* | | `enemy_atlas.py`: `beetle_*` swatches |

## Behaviour

1. Built from smooth closed shells, one colour each, bound rigidly to bones, overlapping at joints (`scorpion.py`'s method). Nothing outside the repository is read.
2. Shape: squat and wide, a domed shell wider than tall, a small head, six short legs, a blunt abdomen whose tip is a round vent plate ~0.6 m across, facing back and up so the top-down camera sees it.
3. Palette: new `beetle_*` swatches — dusty umber and ochre shell, bleached-bone legs and underside, a darker vent plate — standing off the Dunes sand. No cyan, nothing near `#FF4A1F` (`RESERVED`). The atlas stays 512; swatches go in as `INSETS`, and no other swatch's pixels move.
4. **Turnaround first.** `--turnaround <png>` renders the unrigged shells front, side, back and top to `Temp/beetle_turnaround.png` (not committed). Rig and clips start only on the owner's OK in chat.
5. `Rig_Beetle`: `Root` (never moves), `Body`, `Head`, `Abdomen`, six legs (two-bone IK), and a `Vent` empty at the plate's centre facing out of the rear (SS-06c's glow and muzzle).
6. Clips, 30 fps, in place: `Idle` 48 f loop; `Walk` 16 f loop (alternating tripods, stride for 2.8 m/s); `Turn` 24 f loop (legs shuffle, body still — played while turning on the spot); `Aim` 21 f (0.7 s, abdomen lifts and holds); `Fire` 12 f (abdomen kicks, vent at full lift on frame 1); `Hit` 12 f; `Death` 30 f (flips, legs curl).
7. 400–2 500 triangles, one skinned mesh, one material slot (`M_Enemy`), every vertex weighted; asserted in the script and in tests.

## Tests

| Test | Given / When / Then |
|---|---|
| `BeetleModel_IsShipped` | fbx / load / one `SkinnedMeshRenderer` |
| `BeetleModel_StaysInTheCrowdsBudget` | mesh / count / 400–2 500 tris, 1 submesh |
| `BeetleModel_UsesTheEnemyMaterial` | renderer / read / `M_Enemy` only |
| `BeetleModel_EveryVertexIsWeighted` | mesh / weights / all > 0 |
| `BeetleModel_ImportsGenericWithItsClips` | importer / read / Generic; the seven clips, looping as rule 6 |
| `BeetleModel_EveryClipBindsAndLeavesTheRootAlone` | each clip / sample / binds; `Root` still |
| `BeetleModel_HasTheRigsBones` | rig / walk / rule 5's bones, `Vent` under `Abdomen`, pointing backwards |
| `BeetleModel_IsWiderThanTall` | bounds / measure / width ≥ 1.5 × height |
| `BeetleModel_StandsOffTheDunes` | area-weighted colour / vs Dunes sand / `ScorpionModel_StandsOffTheDunes`' margins |
| `BeetleModel_NoCyanOrDanger` | sampled swatches / hue / none in the reserved bands |

Every other model test stays green (rule 3's atlas check).

## Manual verification (Editor / device)

1. **[Editor]** Turnaround PNG approved by the owner before rigging.
2. **[Editor]** Each clip previewed: `Aim` lifts the vent into view from above; no foot sliding in `Walk`.

## Out of scope

- Prefab, animator, glow, glob (SS-06c). Data (SS-06d).
- A sculpt (the shells are the model); a second variant.

## As built

_Commit body._
