# UI-10a — A circle for the telegraph rings

**Size:** S · **Depends on:** — · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §7.1, §9.1, §12.4, §16.4 · **Ledger rows:** none

## Goal

Every telegraph "ring" draws as a circle, not the untextured Quad's square, with its radius still exactly the radius core tested.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Art/Vfx/T_TelegraphRing_Mask.png` | — | 256 × 256 white disc with a brighter rim, alpha outside it |
| `Editor/Vfx/TelegraphRingTextureGenerator.cs` | Editor | writes the PNG — a menu item, beside `LowPolyVfxMeshGenerator`, so the circle is reproducible rather than drawn |
| `Tests/Game/Views/TelegraphRingTextureTests.cs` | Tests.Game | reads the texture and the material |
| *small edits* | | `M_TelegraphRing.mat`'s `_BaseMap` set to the mask |

## Behaviour

1. **The mask is a circle inscribed in the texture**: alpha is 0 at every corner and 1 at the centre, and the 50 % alpha contour lies at radius 0.5 of the texture's width, within one texel. The Quad's half-width is the ring's radius today, so the drawn edge stays the radius core tested ("the radius is the truth", `TelegraphRingView`).
2. **The rim is brighter than the fill**: the outer 8 % of the radius has alpha 1, the inside 0.45, so a spawn ring's growing disc and a blast ring's full disc both read as a ring with an edge.
3. **The mask is white**; colour stays a tint from `Palette.Danger` through `TelegraphRings`, as today (GD §16.4).
4. **It imports as** clamped, bilinear, mipmaps on, alpha is transparency, ASTC 6×6 on Android.
5. **`M_TelegraphRing.mat` samples it** as `_BaseMap`; its blend state and the five decals that share it are otherwise unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `RingMask_IsInscribedCircle` | rule 1: corners 0, centre ≥ 0.4, the 50 % contour at radius 0.5 ± 1 texel on 8 rays |
| `RingMask_RimIsBrighter` | rule 2 |
| `RingMask_IsWhite` | rule 3 |
| `RingMask_ImportSettings` | rule 4 |
| `RingMaterial_SamplesTheMask` | rule 5 |

## Manual verification

1. **[Editor]** A Jungle stage: spawn rings grow as discs with an edge, blast rings are round, Pitcher and boss decals likewise; UI-07b's capture shows it.

## Out of scope

New decal shapes, animation in the shader, and the arena-hazard look.

## As built

_Filled at merge._
