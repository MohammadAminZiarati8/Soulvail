# SS-14b — The model: the owner's vulture on Rig_Hawk, flight and ground clips

**Size:** M · **Depends on:** — · **Branch:** `ss-14b-vulture-model`
**Design refs:** GD §8.14, §3 › The Sunscar, §16.4, §17.1

## Goal

`Vulture.fbx` is the owner's Trellis sculpt, repaired, on `hawk.py`'s `Rig_Hawk` refitted to it, with the hawk's five flight clips and four new ground clips. Turnaround comes first.

## The sculpt (owner)

The owner generated this image and ran it through Trellis (2026-11-13, `Vulture-e9ac8530-seed42_00001_.glb`). Image prompt, kept for a re-roll:

```text
A single stylized fantasy carrion vulture for a mobile game, low-poly hand-painted style like KayKit, chunky proportions, readable from above. Huge broad wings spread fully and straight out to the sides with long fingered tip feathers, short wide tail fanned, legs tucked under the body. Bald wrinkled head and long bare neck in pale bone-grey, a thick ruff of off-white feathers around the base of the neck, heavy hooked beak in dark horn. Plumage dark umber-brown, lighter dusty-brown edging on the wing feathers. Matte. No blue, no cyan, no red, no pink, no orange, no gold. Front view, perfectly symmetrical, wings level. Plain flat light grey background, soft even studio lighting, no shadow on the ground, no base, no text, no other objects.
```

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/vulture.py` | — | repair, refit, bind, clips, checks; `hawk.py`'s shape, its skeleton and flight keys imported |
| `Tools/Blender/Sources/Vulture_src.glb` | — | the sculpt, SHA-256 pinned at build |
| `Assets/_Project/Art/Enemies/Vulture.fbx` (+ `.meta`) | — | Generic, clips split, in place |
| `Assets/_Project/Tests/Game/Art/VultureModelTests.cs` | Tests.Game | `HawkModelTests`' rows |
| *small edits* | | `enemy_atlas.py` + `T_Enemy_Albedo.png`: `vulture_*` swatches |

## Behaviour

1. **The source.** Copy the owner's `Vulture-e9ac8530-seed42_00001_.glb` (`C:/Users/mazia/Documents/Codex/2026-10-23/sa/outputs/generated/`, read-only) to `Sources/Vulture_src.glb`, SHA-256 `4ee6d652aeb4a6ea456ca371f3e5a92ff8c90f3226fc57dd95656c525f85ab8f`. Inspected: one mesh, 97 187 tris, UVs, no normals, baked colour (2 images); bounds 1.0 × 0.36 × 0.28 (X × Y up × Z), symmetric across X: wings level along X, the body along Z. Front is ±Z, confirmed by the turnaround. `repair_scan.py` closes it and it is reduced to rule 3's budget.
2. **Turnaround first.** `--turnaround <png>` renders the repaired, unrigged body front, side, back and top to `Temp/vulture_turnaround.png` (uncommitted). Rigging starts only on the owner's OK, recorded in *As built*.
3. Reduce to 1 500–3 000 tris. Head to Unity forward. Scale to a **2.6 m** wingspan, at least 1.6× the hawk's, keeping the sculpt's proportions (about 0.7 m beak to tail).
4. **Rig:** `Rig_Hawk`'s bones and names as `hawk.py` builds them (`root`, `body`, `neck`, `head`, `tail`, `wing.l/r.1–2`, `leg.l/r`), refitted to the sculpt's joints. The armature keeps the name `Rig_Hawk`. `Hawk.fbx` is untouched.
5. Bound as `hawk.py` binds, ≤ 4 weights, none unweighted, none on `root`. The fingered tips follow `wing.*.2`.
6. **Clips**, 30 fps, in place, `root` still. The hawk's keys, unchanged in timing: `Glide` 48 f loop, `Flap` 12 f loop, `Dive` 20 f, `Strike` 15 f, `Climb` 22 f. New: `Land` 15 f (wings brake forward, legs reach, the body settles to standing on f 12); `Idle_Ground` 48 f loop (wings folded along the body, `wing.*.1` back about 80° and `wing.*.2` about 120°, a slow head bob); `Hit` 12 f (from the ground pose); `Death` 30 f (it slumps, one wing falling open).
7. One skinned mesh, one material slot (`M_Enemy`).
8. **Palette:** `vulture_*` swatches from the baked tones: dark umber plumage clearly darker than the Dunes sand, dusty edging, a bone-grey head, an off-white ruff, a dark horn beak. No cyan (the hawk's cyan swatch is not borrowed), no gold, no pink, nothing near `#FF4A1F` (`RESERVED`). Insets only, no other swatch moves.

## Tests

| Test | Given / When / Then |
|---|---|
| `VultureModel_IsShippedInBudget` | fbx / load / one `SkinnedMeshRenderer`, `M_Enemy`, 1 submesh, rule 3's tris |
| `VultureModel_IsWeighted` | skin / read / all weighted, ≤ 4, none on `root` |
| `VultureModel_HasRigHawk` | rig / walk / `Rig_Hawk` with the hawk's eleven bone names |
| `VultureModel_ImportsItsClips` | importer / read / Generic; rule 6's nine clips, frames, loops |
| `VultureModel_ClipsLeaveRootAlone` | each clip / sample / `root` still |
| `VultureModel_OutspansTheHawk` | rest bounds vs `Hawk.fbx` / span / 2.4–2.8 m and ≥ 1.6× |
| `VultureModel_GroundPoseFoldsWings` | `Idle_Ground` f 0 / span / ≤ 45 % of the rest span |
| `VultureModel_StandsOffTheDunes` | colour / vs Dunes sand / darker, the scorpion's margins |
| `VultureModel_NoCyanGoldOrDanger` | swatches / hue / none in reserved bands |
| `VultureSource_HashPinned` | source / hash / rule 1's |
| `HawkModel_*` | unchanged / run / green |

## Manual verification (Editor / device)

None here. Rule 2's turnaround is a gate, not a play check; SS-14d holds the play steps.

## Out of scope

- Prefab, animator, shadow, dive (SS-14c). Data (SS-14d). The hawk pet.

## As built

_Commit body._
