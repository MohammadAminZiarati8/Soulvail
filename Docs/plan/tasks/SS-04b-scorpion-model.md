# SS-04b — The model: scorpion.py, Rig_Scorpion, six clips, turnaround first

**Size:** M · **Depends on:** — · **Branch:** `ss-04b-scorpion-model`
**Design refs:** GD §8.4, §3 › The Sunscar, §16.4, §17.1

## Goal

`Scorpion.fbx` exists: a KayKit-style sand scorpion built from nothing in `scorpion.py`, on its own rig, with six authored clips and the shared enemy atlas.

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/scorpion.py` | — | shells, rig, clips, checks; `pitcher.py`/`frog.py`'s shape |
| `Assets/_Project/Art/Enemies/Scorpion.fbx` (+ `.meta`) | — | output; Generic, clips split |
| `Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png` | — | regenerated atlas |
| `Assets/_Project/Tests/Game/Art/ScorpionModelTests.cs` | Tests.Game | `FrogModelTests`' rows |
| *small edits* | | `enemy_atlas.py`: `scorp_*` swatches |

## Behaviour

1. Built from smooth closed shells, one colour each, bound rigidly to bones, overlapping at joints (frog.py's KayKit method). Nothing outside the repository is read.
2. Shape: low and wide, longer than tall; six short legs, two pincers, a five-segment tail curled over the back, a fat stinger bulb. Reads as a scorpion from the top-down camera.
3. Palette: new `scorp_*` swatches in sandstone, bleached bone and dusty ochre, with darker ochre plates so the body stands off the Dunes sand. No cyan, nothing near `#FF4A1F` (`RESERVED`). The atlas stays 512; new swatches go in as `INSETS` the Apex's way, and no other swatch's pixels move.
4. **Turnaround first.** `--turnaround <png>` renders the unrigged shells front, side, back and top to `Temp/scorpion_turnaround.png` (not committed). Show it to the owner; rig and clips start only on the owner's OK in chat.
5. `Rig_Scorpion`: `Root` (never moves), `Body`, `Pincer_L/R`, six legs (two-bone IK), `Tail_1`–`Tail_5`, `Stinger` at the bulb (SS-04c's glow parent).
6. Clips, 30 fps, in place: `Idle` 48 f loop (breathing, pincer twitch); `Skitter` 12 f loop (alternating tripods, stride for 2.2 m/s at 1×); `Windup` 18 f (0.6 s, tail cocks back and holds); `Strike` 24 f (stinger at full reach on frame 2, tail on the ground to frame 24 = 0.8 s); `Hit` 12 f; `Death` 30 f (flips, legs curl).
7. 400–2 500 triangles, one skinned mesh, one material slot (`M_Enemy`), every vertex weighted; asserted in the script and in tests.
8. If the owner brings a Tripo shape, it is a proportion reference: the shells are rebuilt to match it, as frog.py replaced M7-05j's sculpt. It is never imported faceted.

## Tests

| Test | Given / When / Then |
|---|---|
| `ScorpionModel_IsShipped` | fbx / load / one `SkinnedMeshRenderer` |
| `ScorpionModel_StaysInTheCrowdsBudget` | mesh / count / 400–2 500 tris, 1 submesh |
| `ScorpionModel_UsesTheEnemyMaterial` | renderer / read / `M_Enemy` only |
| `ScorpionModel_EveryVertexIsWeighted` | mesh / weights / all > 0 |
| `ScorpionModel_ImportsGenericWithItsClips` | importer / read / Generic; the six clips, looping as rule 6 |
| `ScorpionModel_EveryClipBindsAndLeavesTheRootAlone` | each clip / sample / binds; `Root` still |
| `ScorpionModel_HasTheRigsBones` | rig / walk / rule 5's bones, `Stinger` under `Tail_5` |
| `ScorpionModel_IsLongerThanTall` | bounds / measure / length ≥ 2 × height |
| `ScorpionModel_StandsOffTheDunes` | area-weighted colour / vs Dunes sand / `FrogModel_StandsOffTheJungleFloor`'s margins |
| `ScorpionModel_NoCyanOrDanger` | sampled swatches / hue / none in the reserved bands |

The Frog, Pitcher, Panther and other model tests stay green (rule 3's atlas check).

## Manual verification (Editor / device)

1. **[Editor]** Turnaround PNG approved by the owner before rigging.
2. **[Editor]** Each clip previewed in the inspector: readable tell in `Windup`, no foot sliding in `Skitter`.

## Out of scope

- Prefab, animator controller, glow, strip (SS-04c). Data (SS-04d).
- A shield, pets, any second variant.

## As built

_Commit body._
