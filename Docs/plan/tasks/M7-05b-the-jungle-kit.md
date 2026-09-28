# M7-05b — The Jungle's kit

**Size:** S · **Depends on:** M7-05a (the KayKit packs and their importer remap) · **Branch:** `m7-05b-the-jungle-kit`
**Design refs:** GD §11.3, GD §16.4, GD §17.1 · **Ledger rows:** none

Pulled forward from M7-05 at the owner's go of 2026-09-27: *"build the jungle place's kit and its
first arena yourself — restore the Forest Nature bushes and grass …, recolour the KayKit pieces to
the jungle palette, script the missing jungle pieces in Blender, generate the ground texture.
Working name Jungle."* The arena is M7-05c.

## Goal

The Jungle's art kit — KayKit's forest and dungeon pieces recoloured, nine pieces KayKit has no
equivalent for, and a tiling ground — made by one script in the repository and imported so an
arena is dressed by dragging pieces in.

## Files

| Path (under `Assets/_Project/` unless rooted) | Assembly | Purpose |
|---|---|---|
| `/Tools/Blender/jungle_kit.py` | — (Blender 4.1) | writes the two rows below it; the only place they change |
| `Art/Environment/Jungle/*.fbx` | — | generated: `JungleTree_A`/`_B`, `ButtressTrunk_A`, `BigLeafPlant_A`/`_B`, `Fern_A`, `VineCurtain_Sealed`/`_Parted`, `HangingVines_A` |
| `Art/Environment/Jungle/Textures/*.png` | — | generated: `T_Jungle_Albedo`, `T_JungleRuins_Albedo`, `T_JungleGround_Albedo` |
| `Materials/Environment/M_Jungle`, `M_JungleRuins`, `M_JungleGround`, `M_Leaf` | — | the kit's materials; the first three are copies of `M_Dungeon` |
| `Tests/Game/Art/JungleKitTests.cs` | Tests.Game | new |
| *small edits* | | `/Assets/ThirdParty/KayKit/ForestNature/Bushes/` (22) and `Grass/` (16) restored, remapped to `M_ForestNature`; `/Assets/ThirdParty/KayKit/VERSIONS.md` |

Only these files change. Anything else is a deviation: say so in *As built*.

## Behaviour

1. **One script makes the kit, and running it again makes the same kit.** Every file under
   `Art/Environment/Jungle/` is its output, changed in the script and never by hand; its random
   streams are seeded.
2. **The kit and KayKit's forest pieces share one material.** `T_Jungle_Albedo` keeps Forest
   Nature's atlas layout — its three strips in column 0 repainted, this kit's swatches in the space
   KayKit reserves — so a KayKit tree, bush or rock given `M_Jungle` reads jungle-green. Every
   generated model's placeholder material is remapped at the importer to `M_Jungle`, and to nothing
   else.
3. **Each model imports standing at its origin, with no root transform, in budget.** The export
   bakes Blender's Z-up into the vertices; no piece passes 1 500 triangles.
4. **The palette atlases are point-filtered; the ground repeats and is filtered.** KayKit's rule
   for atlases (VERSIONS.md); the ground tiles 12 m to a repeat across a 100 m plane.

## Tests

| Test | Given / When / Then |
|---|---|
| `JungleKit_IsShipped` | the kit folder / its models are found / at least one (guards every row below) |
| `JungleModel_UsesTheJungleMaterialAlone` | each model / its renderers are read / every material is `M_Jungle` (rule 2) |
| `JungleModel_StaysInItsBudget` | each model / its triangles are summed / ≤ 1 500 (rule 3) |
| `JungleModel_ImportsWithoutARootTransform` | each model / its root is read / identity rotation, unit scale (rule 3) |
| `JungleTree_StandsUp` | both trees / mesh bounds / taller than deep, nothing below −0.5 m (rule 3) |
| `JungleAtlas_IsPointFiltered` | the kit and ruins atlases / their importers / `Point` (rule 4) |
| `JungleGround_Repeats` | the ground / its importer / `Repeat`, not `Point` (rule 4) |

Rule 1 has no row: it is a property of a script Unity never runs. It is checked by hand, below.

## Manual verification (Editor / device)

1. **[Editor]** Run the script (its docstring has the line) and `git status`: the three PNGs are
   unchanged; the FBXs differ only in the header's timestamp.
2. **[Editor]** The kit's colours, in M7-05c's clearing: muted greens, browns and grey stone —
   nothing cyan, red-orange, violet or gold (GD §16.4).

## Out of scope

- The arena (M7-05c), a place-select screen, a Jungle `ModeDefinition` and the Jungle's own enemy
  looks — each its own task, as M7-05a's *Out of scope* says.
- Baked lighting and lightmap UVs (GD §17.1), which no arena has yet.

## As built

**Deviations.** *(1)* `Tools/` is new at the repository root, outside the Files convention's
`Assets/_Project/`: the script is a build input Unity must not compile or import. *(2)* `Art/` is
new under `_Project`, for source art that is not a prefab, a material or data.
*(3)* GD §17.1 wants one atlas per biome; the Jungle has three. The ruins are KayKit Dungeon pieces
whose UVs point into the Dungeon atlas's layout, so they need a recolour of that atlas rather than
a swatch in this one; the ground is a tiled texture, not a palette. Three materials, all
SRP-batched. *(4)* The generated models import with no rig and no animation, settling the question
VERSIONS.md left to M7-05 for this kit's pieces; KayKit's own pieces keep the importer's defaults.

**Numbers.** Triangles: trees 576 and 676, trunk 190, big-leaf plants 448 and 320, fern 540, vine
curtains 1 190 sealed and 1 130 parted, hanging vines 524. Atlases 1 024 px square. The ground is
periodic FFT noise in three greens with earth patches and flecks, tuned twice after renders —
the first read as camouflage.

**Finding — the script reproduces its output.** Run twice, the PNGs are byte-identical and each
FBX differs in 38–39 bytes of header, at equal size: Blender stamps the export time and a file id.
A re-run shows in `git status` as nine changed FBXs with no change to what renders.

**Verified**, in one run with M7-05c in the tree. EditMode 3 317 — 3 316 passed, 0 failed,
1 inconclusive (the animator clock row) — +40 on M7-05a's 3 277: this fixture's 33 and M7-05c's
seven arena rows. PlayMode 67 / 67. Console: no errors. Format check clean on `JungleKitTests.cs`.
