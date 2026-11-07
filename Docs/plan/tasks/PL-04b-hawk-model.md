# PL-04b — The hawk's model on its own rig

**Size:** M · **Depends on:** the owner's TRELLIS sculpt (prompt below) · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.2, §3.7; GD §16.4, §17.1; art source ruling; BM-03b

## Goal

A jungle hawk that reads as the player's bird, rigged on a small bird skeleton of its own
(`Rig_Hawk`); no KayKit or existing rig fits a bird.

## Owner step: the sculpt

Make the picture in Gemini, run it through TRELLIS, and save it as
`Tools/Blender/Sources/Hawk_trellis.glb`. Prompt:

```
A jungle hunting hawk in flight, full body, top three-quarter view, wings spread wide, plain white
background. Stylised low-poly game character in the KayKit style: smooth simple shapes, no
feather strands. Brown and pale bone feathers with a dark barred tail, a hooked pale beak, small
leather bands on both legs. No rider, no hood, no props, no red, no gold.
```

## Files

| Path (under `Assets/_Project/` unless rooted) | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/hawk.py` (repo root) | — | **New.** Remesh, builds `Rig_Hawk`, distance-skins, bakes clips |
| `Art/Pets/Hawk.fbx` | — | **New** |
| `Art/Pets/Textures/T_Hawk_Albedo.png` | — | **New.** Feathers, cyan leg bands and wing-tip edge |
| `Materials/Pets/M_Hawk.mat` | — | **New** |
| `Prefabs/Pets/Hawk.prefab` | — | **New.** Root with a `Model` child |
| `Tests/Game/Art/HawkModelTests.cs` | Tests.Game | **New** |

## Behaviour

1. **Rig `Rig_Hawk`:** `root`, `body`, `neck`, `head`, `wing.l.1`, `wing.l.2`, `wing.r.1`,
   `wing.r.2`, `tail`, `leg.l`, `leg.r`.
2. **Colour:** at least 2 % of texels in the cyan band (leg bands, wing-tip edge); none within
   ΔE 20 of `Palette.Danger`, none in the gold or enemy lime band.
3. **Clips**, named exactly: `Glide` (loop), `Flap` (loop), `Dive`, `Strike`, `Climb`. Stationary
   root; no events.
4. Wingspan at scale 1 is 1.4–1.8 m, so it reads from the camera at height 4 m.
5. **Budget:** under half the crowd budget; every vertex weighted.

## Tests

| Test | Given / When / Then |
|---|---|
| `Hawk_OneModelChild_RigHawkBones` (1) |
| `Paint_HasPlayerCyan_NoDangerGoldLime` (2) |
| `Hawk_HasTheFiveClips_NoEvents_NoRootMotion` (3) |
| `Wingspan_InBand` (4) |
| `Hawk_InBudget_EveryVertexWeighted` (5) |

## Manual verification (Editor / device)

1. **[Editor]** At camera height over Jungle ground: a bird, readable, the cyan bands visible.

## Out of scope

- The view (PL-04c).

## As built

_Filled at merge._
