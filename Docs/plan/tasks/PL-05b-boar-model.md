# PL-05b — The boar's model on `Rig_Panther`

**Size:** M · **Depends on:** the owner's TRELLIS sculpt (prompt below) · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Stampede); GD §16.4, §17.1; art source ruling; JG-30d, BM-03b

## Goal

A wild jungle boar for the Stampede, refit onto `Rig_Panther` as the Apex and the Hound were, and
painted as the player's.

## Owner step: the sculpt

Make the picture in Gemini, run it through TRELLIS, and save it as
`Tools/Blender/Sources/Boar_trellis.glb`. Prompt:

```
A wild jungle boar charging, full body, side three-quarter view, on four short legs, head low,
big curved pale tusks, plain white background. Stylised low-poly game character in the KayKit
style: smooth simple shapes, no hair strands. Dark brown coat with a pale bristly mane along the
back. No saddle, no armour, no props, no red, no gold.
```

## Files

| Path (under `Assets/_Project/` unless rooted) | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/boar.py` (repo root) | — | **New.** `hound.py`'s pipeline on the boar |
| `Art/Pets/Boar.fbx` | — | **New** |
| `Art/Pets/Textures/T_Boar_Albedo.png` | — | **New.** Coat, mane, a cyan war-paint stripe on each flank |
| `Materials/Pets/M_Boar.mat` | — | **New** |
| `Prefabs/Pets/Boar.prefab` | — | **New** |
| `Tests/Game/Art/BoarModelTests.cs` | Tests.Game | **New** |

## Behaviour

1. One skinned body under `Model`, `Rig_Panther` bone names refit to short legs and a low head.
2. **Colour:** the flank stripes are `Palette.Player` cyan (at least 2 % of texels); none within
   ΔE 20 of `Palette.Danger`, none in the gold or enemy lime band.
3. **Clips:** `Charge` (loop, the stampede run) and `Idle`. Stationary root; no events.
4. **Budget:** under half the crowd budget, since up to twelve run at once; every vertex weighted.

## Tests

| Test | Given / When / Then |
|---|---|
| `Boar_OneModelChild_PantherBones` (1) |
| `Paint_HasPlayerCyan_NoDangerGoldLime` (2) |
| `Boar_HasChargeAndIdle_NoEvents_NoRootMotion` (3) |
| `Boar_HalfCrowdBudget_EveryVertexWeighted` (4) |

## Manual verification (Editor / device)

1. **[Editor]** A boar beside a Frog and a Panther: low, heavy, tusked, cyan-striped, nothing like
   an enemy.

## Out of scope

- The herd drawn (PL-05c).

## As built

_Filled at merge._
