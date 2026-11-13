# SS-12b — The model: the owner's cobra on Rig_Viper, hood bones, six clips

**Size:** M · **Depends on:** the owner's sculpt · **Branch:** `ss-12b-cobra-model`
**Design refs:** GD §8.12, §8.8, §3 › The Sunscar, §16.4, §17.1

## Goal

`Cobra.fbx` is the owner's Trellis sculpt, repaired, on `viper.py`'s `Rig_Viper` plus two hood bones, with six clips. Turnaround comes first.

## The sculpt (owner)

The owner generates this image, runs it through Trellis, and sends the `.glb` path with the go. Image prompt, kept for a re-roll:

```text
A single stylized fantasy desert cobra for a mobile game, low-poly hand-painted style like KayKit, chunky proportions, readable from above. The snake lies almost straight and flat on the ground, head slightly raised, the wide hood fully spread and flat like a shield behind the head, mouth closed, short blunt fangs. Body dusty sand-brown with bold dark umber bands; hood front pale bone with two dark umber eye-spot markings; belly pale bone. Matte. No blue, no cyan, no red, no orange, no gold, no green. Seen from a high three-quarter front view, symmetrical, full length visible from nose to tail tip. Plain flat light grey background, soft even studio lighting, no shadow on the ground, no base, no text, no other objects.
```

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/cobra.py` | — | repair, fit, rig, bind, clips, checks; `viper.py`'s shape |
| `Tools/Blender/Sources/Cobra_src.glb` | — | the sculpt, SHA-256 pinned at build |
| `Assets/_Project/Art/Enemies/Cobra.fbx` (+ `.meta`) | — | Generic, clips split, in place |
| `Assets/_Project/Tests/Game/Art/CobraModelTests.cs` | Tests.Game | `ViperModelTests`' rows |
| *small edits* | | `enemy_atlas.py` + `T_Enemy_Albedo.png`: `cobra_*` swatches |

## Behaviour

1. **The source.** Copy the owner's `.glb` (read-only) to `Sources/Cobra_src.glb`, and record its SHA-256 and inspected facts (tris, bounds, facing, how far the hood spreads) in *As built*. `repair_scan.py` closes it.
2. **Turnaround first.** `--turnaround <png>` renders the repaired, unrigged body front, side, back and top to `Temp/cobra_turnaround.png` (uncommitted). Rigging starts only on the owner's OK, recorded in *As built*.
3. Reduce to 1 500–3 000 tris. Head to Unity forward. Scale to **2.4 m** nose to tail, about 0.3 m thick, the hood about **0.7 m** across when spread.
4. **Rig:** `Rig_Viper` as `viper.py` fits it (`Root`, the spine chain, `Head`, `Jaw`), plus `Hood_L` and `Hood_R` under the first spine bone, hinged along the hood's spine line. The sculpt's hood is spread; the rest pose folds it to the neck (≤ 0.35 m across) by the hood bones, so `Idle` and `Slither` read as a snake and `Rear` opens it.
5. Bound by `viper.py`'s `bind`, the hood's vertices split at the midline to their bones, ≤ 4 weights, none unweighted, none on `Root`.
6. **Clips**, 30 fps, in place, `Root` still: `Idle` 48 f loop; `Slither` 24 f loop (2.4 m/s, the viper's wave); `Rear` 21 f (0.7 s: the head and first spine bones rise ~0.45 m, the hood opens to full spread by f 15 and holds); `Spit` 9 f (from the reared pose the head jabs 0.2 m forward, the jaw opens at f 3); `Hit` 12 f; `Death` 30 f (the body falls flat, the hood folds).
7. One skinned mesh, one material slot (`M_Enemy`).
8. **Palette:** `cobra_*` swatches from the baked tones: sand-brown a step darker than the Dunes sand, bold umber bands, a pale bone hood front with umber eye-spots, a bone belly. No cyan, no gold, no green, nothing near `#FF4A1F` (`RESERVED`). Insets only, no other swatch moves.

## Tests

| Test | Given / When / Then |
|---|---|
| `CobraModel_IsShippedInBudget` | fbx / load / one `SkinnedMeshRenderer`, `M_Enemy`, 1 submesh, rule 3's tris |
| `CobraModel_IsWeighted` | skin / read / all weighted, ≤ 4, none on `Root` |
| `CobraModel_HasRigViperAndHood` | rig / walk / `Rig_Viper`'s bones plus `Hood_L`, `Hood_R` under the first spine bone |
| `CobraModel_ImportsItsClips` | importer / read / Generic; rule 6's clips, frames, loops |
| `CobraModel_ClipsLeaveRootAlone` | each clip / sample / `Root` still |
| `CobraModel_RearOpensHood` | hood width, `Idle` vs `Rear` last frame / measure / ≤ 0.35 m, then ≥ 0.6 m |
| `CobraModel_RearLiftsHead` | `Head`, `Idle` vs `Rear` last frame / height / ≥ 0.35 m higher |
| `CobraModel_IsLongAndLow` | rest bounds / measure / 2.2–2.6 m long, ≤ 0.4 m tall |
| `CobraModel_StandsOffTheDunes` | colour / vs Dunes sand / the scorpion's margins |
| `CobraModel_NoCyanGoldGreenOrDanger` | swatches / hue / none in reserved bands |
| `CobraSource_HashPinned` | source / hash / rule 1's |
| `ViperModel_*` | unchanged / run / green |

## Manual verification (Editor / device)

None here. Rule 2's turnaround is a gate, not a play check; SS-12d holds the play steps.

## Out of scope

- Prefab, animator, cone (SS-12c). Data (SS-12d).

## As built

_Commit body._
