# SS-13b — The model: the hyena caller on Rig_Medium and a spiral staff

**Size:** M · **Depends on:** — · **Branch:** `ss-13b-caller-model`
**Design refs:** GD §8.13, §8.11, §3 › The Sunscar, §16.4, §17.1

## Goal

`StormCaller.fbx` is the owner's Trellis sculpt, repaired and bound to KayKit `Rig_Medium` by `jackal_priest.py`'s pipeline, and `CallerStaff.fbx` is a script-built staff with an open spiral ring head. Turnaround comes first.

## The sculpt (owner)

The owner generated this image and ran it through Trellis (2026-11-13, `HyenaNomadWarrior-822c0e0b-seed44_00001_.glb`). Image prompt, kept for a re-roll:

```text
A single stylized fantasy hyena-headed desert storm sorcerer for a mobile game, low-poly hand-painted style like KayKit, chunky proportions, readable from above. Hunched shoulders, rounded ears, short heavy muzzle, a bristly mane down the neck, spotted dusty-tan fur. Wrapped in layered tattered scarves and a hooded cloak that flare out as if caught in wind, wrapped forearms and shins, bare clawed feet. Cloak and scarves dark charcoal-brown and pale sand, a few loose wrap ends. Matte. Empty hands, no staff, no weapon, no glow, no magic effects. No blue, no cyan, no red, no orange, no gold. Full body T-pose, front view, symmetrical, arms straight out to the sides. Plain flat light grey background, soft even studio lighting, no shadow on the ground, no base, no text, no other objects.
```

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/storm_caller.py` | — | repair, bind, staff, checks, sheets; `jackal_priest.py`'s shape |
| `Tools/Blender/Sources/StormCaller_src.glb` | — | the sculpt, SHA-256 pinned at build |
| `Assets/_Project/Art/Enemies/StormCaller.fbx` (+ `.meta`) | — | body; Generic, in place |
| `Assets/_Project/Art/Enemies/CallerStaff.fbx` (+ `.meta`) | — | static prop |
| `Assets/_Project/Tests/Game/Art/StormCallerModelTests.cs` | Tests.Game | `JackalPriestModelTests`' rows |
| *small edits* | | `enemy_atlas.py` + `T_Enemy_Albedo.png`: `caller_*` swatches |

## Behaviour

1. **The source.** Copy the owner's `HyenaNomadWarrior-822c0e0b-seed44_00001_.glb` (`C:/Users/mazia/Documents/Codex/2026-10-23/sa/outputs/generated/`, read-only) to `Sources/StormCaller_src.glb`, SHA-256 `953ce4b27aceb8984cfcc34dd2aee4aa0cf90605030c23b6e32c9e90731c1c9a`. Inspected: one mesh, 99 577 tris, UVs, no normals, baked colour (2 images); bounds 0.99 × 1.0 × 0.65 (X × Y up × Z), symmetric across X: upright, arms out in a T, the flared cloak giving the depth. Front is ±Z, confirmed by the turnaround. `repair_scan.py` closes it and it is reduced to rule 7's budget.
2. **Turnaround first.** `--turnaround <png>` renders the repaired, unrigged body with the staff beside it, front, side, back and top, to `Temp/storm_caller_turnaround.png` (uncommitted). Binding and sheets start only on the owner's OK, recorded in *As built*.
3. Bound to `Rig_Medium` unchanged (23 bones), ≤ 4 weights per vertex, every vertex weighted. The cloak and loose wraps follow the legs and arms and do not tear on `Running_A`. Height within ±10 % of the jackal's; the hunch may sit low in that band.
4. **The staff**, built from shells in the script: a 1.6 m gnarled shaft and, at the top, an **open spiral ring** about 0.4 m across: a flat band coiling one and a half turns round an empty centre. Dark umber and pale bone, **not gold**. A `Glow` empty sits at the ring's centre (SS-13c's spiral origin). Its grip origin sits where `handslot.r` holds it.
5. **Palette:** `caller_*` swatches from the baked tones: spotted tan fur, charcoal-brown cloak darker than the jackal's and far darker than the priest's robe, pale sand wraps. No cyan, no gold, nothing near `#FF4A1F` (`RESERVED`), nothing near the funnel's dark ochre. Insets only, no other swatch moves.
6. **Clips** are the shared Rig_Medium FBXs, not copied into the body: `Idle_B`, `Walking_B`, `Running_A` (flee), `Ranged_Magic_Spellcasting` (the channel, looped as the priest's), `Hit_A`, `Death_A`. `--pose-sheet <png>` renders each with the staff held.
7. Body 1 500–3 000 triangles, staff ≤ 300. One skinned mesh and one material slot (`M_Enemy`) each.

## Tests

| Test | Given / When / Then |
|---|---|
| `CallerModel_IsShipped` | fbx / load / one `SkinnedMeshRenderer` |
| `CallerModel_StaysInBudget` | meshes / count / rule 7 |
| `CallerModel_UsesTheEnemyMaterial` | both renderers / read / `M_Enemy` only |
| `CallerModel_IsWeightedToRigMedium` | skin / read / 23 bones, all weighted, ≤ 4 |
| `CallerModel_ImportsGenericInPlace` | importer / read / Generic, no root motion |
| `CallerModel_StandsJackalHigh` | bounds / measure / ±10 % of `Jackal.fbx` |
| `CallerStaff_HasGlowInTheRing` | staff / walk / `Glow` within 0.05 m of the ring's centre; the centre open |
| `CallerModel_DarkerThanJackalAndPriest` | swatches / luminance / cloak < jackal cloak < priest robe |
| `CallerModel_NoCyanGoldOrDanger` | swatches / hue / none in reserved bands, none gold |
| `CallerSource_HashPinned` | source / hash / rule 1's |
| `JackalModel_*`, `PriestModel_*` | unchanged / run / green |

## Manual verification (Editor / device)

None here. Rule 2's turnaround is a gate, not a play check; SS-13d holds the play steps.

## Out of scope

- Prefab, animator, spiral, tornado (SS-13c). Data (SS-13d).

## As built

_Commit body._
