# SS-10b — The model: the owner's antlion on Rig_Beetle, mandibles, seven clips

**Size:** M · **Depends on:** — · **Branch:** `ss-10b-antlion-model`
**Design refs:** GD §8.10, §3 › The Sunscar, §16.4, §17.1

## Goal

`Antlion.fbx` is the owner's Trellis sculpt, repaired, on `beetle.py`'s six-leg `Rig_Beetle` plus two mandible bones, with seven clips. Turnaround comes first.

## The sculpt (owner)

The owner generated this image and ran it through Trellis (2026-11-12, `Antlion-b9138e09-seed42_00001_.glb`). Image prompt, kept for a re-roll:

```text
A single stylized fantasy desert antlion larva for a mobile game, low-poly hand-painted style like KayKit, chunky and readable from above. A fat, round, low abdomen covered in short tufts of bristle, a small flat head, and two huge curved sickle-shaped mandibles reaching forward, longer than the head, with small inner teeth. Six short, stubby, splayed legs. Body is pale sandy tan and dusty ochre with darker umber blotches on the back; mandibles are bleached bone, darker at the tips. Matte. No blue, no cyan, no green, no red, no glow. Three-quarter view from above and slightly in front, whole body in frame, mandibles open, legs spread and fully visible, symmetrical, neutral standing pose. Plain flat light grey background, soft even studio lighting, no shadow on the ground, no base, no text, no other objects.
```

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/antlion.py` | — | repair, rig, bind, clips, checks; `glassback.py`'s shape |
| `Tools/Blender/Sources/Antlion_src.glb` | — | the sculpt, SHA-256 pinned at build |
| `Assets/_Project/Art/Enemies/Antlion.fbx` (+ `.meta`) | — | Generic, clips split, in place |
| `Assets/_Project/Tests/Game/Art/AntlionModelTests.cs` | Tests.Game | `GlassbackModelTests`' rows |
| *small edits* | | `enemy_atlas.py` + `T_Enemy_Albedo.png`: `antlion_*` swatches |

## Behaviour

1. **The source.** Copy the owner's `Antlion-b9138e09-seed42_00001_.glb` (`C:/Users/mazia/Documents/Codex/2026-10-23/sa/outputs/generated/`, read-only) to `Sources/Antlion_src.glb`, SHA-256 `66aaedb4fe6a802e20310647231d75fd568ca32eaca28610ae1d0b8cd4ee8131`. Inspected: one mesh, 92 987 tris, UVs, no normals, baked colour (2 images); bounds 0.69 × 0.34 × 1.0 (X × Y up × Z), symmetric across X. Its long axis is Z; which end is the mandibles is read from the mesh and confirmed by the turnaround.
2. `repair_scan.py` closes it. Reduce to 1 500–3 000 tris. Head to Unity forward. Scale to **1.4 m** long (mandible tips to abdomen); at the source's proportions it is about **1 m** wide.
3. **Turnaround first.** `--turnaround <png>` renders the unbound body front, side, back and top to `Temp/antlion_turnaround.png` (uncommitted). Rigging waits for the owner's OK.
4. Rig: `Rig_Beetle` as `beetle.py` builds it (glassback's import path), plus `Mandible_L` and `Mandible_R` under `Head`, hinged at each jaw root.
5. Bound by distance (`brood.py`'s `bind`), with mandible vertices split at the midline to their bones, ≤ 4 weights, none unweighted.
6. Clips, 30 fps, in place, `Root` still: `Idle` 48 f loop, `Walk` 16 f loop (2.4 m/s), `DigIn` 18 f (noses down, body sinks 0.6 m below `Root`, legs scrabble), `Erupt` 12 f (rises from −0.6 m, mandibles snap shut at f 8), `Surfaced` 30 f loop (jaws open ~35°, a slow pulse), `Hit` 12 f, `Death` 30 f (flips, legs curl). The sink and rise move `Body`, not `Root`.
7. One skinned mesh, one material slot (`M_Enemy`).
8. Palette: `antlion_*` swatches from the baked tones: tan and ochre a step darker than the Dunes sand so it stands off it, bone mandibles lighter. No cyan. Nothing near `#FF4A1F` (`RESERVED`). Insets only, and no other swatch moves.

## Tests

| Test | Given / When / Then |
|---|---|
| `AntlionModel_IsShippedInBudget` | fbx / load / one `SkinnedMeshRenderer`, `M_Enemy`, 1 submesh, rule 2's tris |
| `AntlionModel_IsWeighted` | skin / read / all weighted, ≤ 4 |
| `AntlionModel_HasRigBeetleAndMandibles` | rig / walk / `Rig_Beetle`'s bones plus `Mandible_L`, `Mandible_R` under `Head` |
| `AntlionModel_ImportsItsClips` | importer / read / Generic; rule 6's clips, frames, loops |
| `AntlionModel_ClipsLeaveRootAlone` | each clip / sample / `Root` still |
| `AntlionModel_DigInSinks` | `DigIn` last frame / `Body` / ≥ 0.5 m below rest |
| `AntlionModel_EruptSnapsJaws` | `Erupt` f 4 vs f 10 / mandibles / closed by ≥ 25° |
| `AntlionModel_IsLongAndLow` | rest bounds / measure / 1.3–1.5 m long, height ≤ 0.55 × width |
| `AntlionModel_StandsOffTheDunes` | colour / vs Dunes sand / the scorpion's margins |
| `AntlionModel_NoCyanOrDanger` | swatches / hue / none in reserved bands |
| `AntlionSource_HashPinned` | source / hash / rule 1's |
| `BeetleModel_*`, `GlassbackModel_*` | unchanged / run / green |

## Manual verification (Editor / device)

1. **[Editor]** Turnaround approved by the owner before rigging.
2. **[Editor]** `DigIn` and `Erupt` previewed from above: the body leaves and returns through the ground plane.

## Out of scope

- Prefab, animator, ripple, ring (SS-10c). Data (SS-10d).

## As built

_Commit body._
