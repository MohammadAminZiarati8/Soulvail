# SS-11b — The model: the jackal priest on Rig_Medium and a sun-disc staff

**Size:** M · **Depends on:** the owner's sculpt · **Branch:** `ss-11b-priest-model`
**Design refs:** GD §8.11, §8.5, §3 › The Sunscar, §16.4, §17.1

## Goal

`JackalPriest.fbx` and `PriestStaff.fbx` exist: the owner's priest sculpt repaired and skinned to the unchanged KayKit `Rig_Medium`, and a staff built in script for the right hand. Turnaround comes first.

## The sculpt (owner)

The owner generates this image, runs it through Trellis, and sends the `.glb` path with the go:

```text
A single stylized fantasy jackal-headed desert priest for a mobile game, low-poly hand-painted style like KayKit, chunky proportions, readable from above. Tall pointed ears, long snout, a tall striped headdress falling to the shoulders, a broad beaded collar, a long plain robe to the ankles with wide sleeves, a dusty umber sash, sandals. Robe is pale bleached bone, almost white; headdress stripes bone and dusty ochre; fur dusty ochre. Matte. Empty hands, no staff, no weapon, no glow. No blue, no cyan, no red, no gold. Full body T-pose, front view, symmetrical, arms straight out to the sides. Plain flat light grey background, soft even studio lighting, no shadow on the ground, no base, no text, no other objects.
```

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/jackal_priest.py` | — | repair, bind, staff, checks, sheets; `jackal.py`'s shape |
| `Tools/Blender/Sources/JackalPriest_src.glb` | — | the sculpt, SHA-256 pinned at build |
| `Assets/_Project/Art/Enemies/JackalPriest.fbx` (+ `.meta`) | — | body; Generic, in place |
| `Assets/_Project/Art/Enemies/PriestStaff.fbx` (+ `.meta`) | — | static prop |
| `Assets/_Project/Tests/Game/Art/JackalPriestModelTests.cs` | Tests.Game | `JackalModelTests`' rows |
| *small edits* | | `enemy_atlas.py` + `T_Enemy_Albedo.png`: `priest_*` swatches |

## Behaviour

1. **The source.** Copy the owner's `.glb` (read-only) to `Sources/JackalPriest_src.glb`, and record its SHA-256 and inspected facts in *As built*. `repair_scan.py` closes it.
2. **Turnaround first.** `--turnaround <png>` renders the repaired, unrigged body with the staff beside it, front, side, back and top, to `Temp/jackal_priest_turnaround.png` (uncommitted). Binding and sheets start only on the owner's OK.
3. Bound to `Rig_Medium` unchanged (23 bones), ≤ 4 weights per vertex, every vertex weighted. The robe's hem follows the legs. Height within ±10 % of the jackal's, with the headdress on top.
4. **The staff**, built from shells in the script: a 1.6 m shaft, a sun disc 0.35 m across at the top, ringed, in bleached bone and dusty ochre — **not gold**. A `Glow` empty sits at the disc's centre (SS-11c's beam origin). Its grip origin sits where `handslot.r` holds it.
5. Palette: `priest_*` swatches: the robe the lightest Sunscar enemy tone, lighter than the jackal's cloak and standing off the Dunes sand. Ochre fur, umber sash. No cyan, no gold, nothing near `#FF4A1F` (`RESERVED`). Insets only, and no other swatch moves.
6. Clips are the shared Rig_Medium FBXs, not copied into the body: `Idle_B`, `Walking_B`, `Running_A` (flee), `Ranged_Magic_Spellcasting` (the mend, looped), `Hit_A`, `Death_A`. `--pose-sheet <png>` renders each with the staff held.
7. Body 1 500–3 000 triangles, staff ≤ 300. One skinned mesh and one material slot (`M_Enemy`) each.

## Tests

| Test | Given / When / Then |
|---|---|
| `PriestModel_IsShipped` | fbx / load / one `SkinnedMeshRenderer` |
| `PriestModel_StaysInBudget` | meshes / count / rule 7 |
| `PriestModel_UsesTheEnemyMaterial` | both renderers / read / `M_Enemy` only |
| `PriestModel_IsWeightedToRigMedium` | skin / read / 23 bones, all weighted, ≤ 4 |
| `PriestModel_ImportsGenericInPlace` | importer / read / Generic, no root motion |
| `PriestModel_StandsJackalHigh` | bounds / measure / ±10 % of `Jackal.fbx` |
| `PriestStaff_HasGlow` | staff / walk / `Glow` near the disc's centre |
| `PriestModel_RobeLighterThanJackal` | swatches / luminance / robe > jackal cloak |
| `PriestModel_NoCyanGoldOrDanger` | swatches / hue / none in reserved bands, none gold |
| `PriestSource_HashPinned` | source / hash / rule 1's |
| `JackalModel_*` | unchanged / run / green |

## Manual verification (Editor / device)

1. **[Editor]** Turnaround approved by the owner before binding.
2. **[Editor]** Pose sheet: the staff stays in hand through every clip, and the robe does not tear on `Running_A`.

## Out of scope

- Prefab, animator, beam (SS-11c). Data (SS-11d).

## As built

_Commit body._
