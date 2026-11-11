# SS-09c — The model: the owner's sculpt on Rig_Beetle, wing cases, eight clips

**Size:** M · **Depends on:** — · **Branch:** `ss-09c-glassback-model`
**Design refs:** GD §8.9, §3 › The Sunscar, §16.4, §17.1

## Goal

`Glassback.fbx` is the owner's Trellis sculpt, repaired, on `beetle.py`'s six-leg `Rig_Beetle` plus two wing-case bones, with eight clips. Turnaround comes first.

## The sculpt (owner)

The owner generated the image below and ran it through Trellis (2026-11-11, `CrystalBeetle-6ae014fa-seed44_00001_.glb`). Image prompt, kept for a re-roll:

```text
A single stylized fantasy desert beetle for a mobile game, low-poly hand-painted style like KayKit, chunky and readable from above. Low, broad body on six short sturdy legs, small head with short mandibles. Its whole back is one big domed shell made of smoky quartz crystal: pale ivory crystal with warm amber facets, cut into large clean faceted planes, split down the middle into two wing cases with a clear seam. Body, legs and head are dark umber brown, matte. No blue, no cyan, no green, no red, no glow, no gems other than the shell. Three-quarter view from above and slightly behind the front, whole body in frame, legs spread and fully visible, symmetrical, neutral standing pose. Plain flat light grey background, soft even studio lighting, no shadow on the ground, no base, no text, no other objects.
```

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/glassback.py` | — | repair, rig, bind, clips, checks; `viper.py`'s shape, `Rig_Beetle` imported from `beetle.py` |
| `Tools/Blender/Sources/Glassback_src.glb` | — | the sculpt, SHA-256 pinned at build |
| `Assets/_Project/Art/Enemies/Glassback.fbx` (+ `.meta`) | — | Generic, clips split, in place |
| `Assets/_Project/Tests/Game/Art/GlassbackModelTests.cs` | Tests.Game | `ViperModelTests`' rows |
| *small edits* | | `beetle.py`: the armature builder callable without side effects (the beetle's output unchanged); `enemy_atlas.py` + `T_Enemy_Albedo.png`: `glassback_*` swatches |

## Behaviour

1. **The source.** Copy the owner's `CrystalBeetle-6ae014fa-seed44_00001_.glb` (`Documents/Codex/2026-10-23/sa/outputs/generated/`, read-only) to `Sources/Glassback_src.glb`, SHA-256 `a0d6c5223ee8f6c8b6f7b5ac759eebce781c2bebd65b30280d0e103493efd6d4`. Inspected: one mesh, 99 247 tris, UVs, no normals, baked colour (2 images); bounds 1.0 × 0.57 × 1.0 (X × Y up × Z), symmetric across X. The low, dense end at +Z reads as the head and the tall end at −Z as the shell's rear; the turnaround confirms the facing. The legs spread as wide as the body is long.
2. `repair_scan.py` closes it. Reduce to 1 500–3 000 tris. Head to Unity forward. Scale to **1.5 m** nose to tail; legs spread, it is about as wide.
3. **Turnaround first.** `--turnaround <png>` renders the unbound body front, side, back and top to `Temp/glassback_turnaround.png` (uncommitted). Rigging waits for the owner's OK.
4. Rig: `Rig_Beetle`'s bones as `beetle.py` builds them (its `Vent` empty unused, kept so the bombardier is untouched), plus `Shell_L` and `Shell_R` under `Body`, hinged at the front of each wing case.
5. Bound by distance (`brood.py`'s `bind`), with shell vertices split at the seam to `Shell_L`/`Shell_R`, ≤ 4 weights, none unweighted.
6. Clips, 30 fps, in place, `Root` still: `Idle` 48 f loop, `Walk` 16 f loop (tripods, 2.6 m/s), `Bite` 15 f, `Glint` 15 f (crouches, legs brace), `Shine` 30 f loop (braced, tiny shiver), `Open` 30 f (wing cases lift ~50° and hold, then close from f 24), `Hit` 12 f, `Death` 30 f (flips, legs curl).
7. One skinned mesh, one material slot (`M_Enemy`).
8. Palette: `glassback_*` swatches from the baked tones: ivory/amber shell lighter than the Dunes sand, umber body darker. No cyan or blue. Nothing near `#FF4A1F` (`RESERVED`). Insets only, and no other swatch moves.

## Tests

| Test | Given / When / Then |
|---|---|
| `GlassbackModel_IsShippedInBudget` | fbx / load / one `SkinnedMeshRenderer`, `M_Enemy`, 1 submesh, rule 2's tris |
| `GlassbackModel_IsWeighted` | skin / read / all weighted, ≤ 4 |
| `GlassbackModel_HasRigBeetleAndShells` | rig / walk / `Rig_Beetle`'s bones plus `Shell_L`, `Shell_R` under `Body` |
| `GlassbackModel_ImportsItsClips` | importer / read / Generic; rule 6's clips, frames, loops |
| `GlassbackModel_ClipsLeaveRootAlone` | each clip / sample / `Root` still |
| `GlassbackModel_OpenLiftsTheShells` | `Open` f 20 vs `Idle` f 0 / shell bones / each ≥ 40° up |
| `GlassbackModel_IsBroadAndLow` | rest bounds / measure / 1.4–1.6 m long, height ≤ 0.65 × length |
| `GlassbackModel_StandsOffTheDunes` | colour / vs Dunes sand / the scorpion's margins |
| `GlassbackModel_NoCyanOrDanger` | swatches / hue / none in reserved bands, none blue |
| `GlassbackSource_HashPinned` | source / hash / rule 1's |
| `BeetleModel_*` | unchanged / run / green (rule 4) |

## Manual verification (Editor / device)

1. **[Editor]** Turnaround approved by the owner before rigging.
2. **[Editor]** `Open` previewed from above: the two wing cases part clearly.

## Out of scope

- Prefab, animator, shine glow, bolt (SS-09d). Data (SS-09e).

## As built

_Commit body._
