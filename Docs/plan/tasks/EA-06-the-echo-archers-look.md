# EA-06 — The Echo Archer's look

**Size:** S · **Depends on:** EA-01c · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (the shade's colour); MK-11 (`PathLook`, data-driven); SK-07 (the shape)

## Goal

Choosing the Echo Archer changes the Ranger's look, with data only.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Data/Paths/EchoArcherLook.asset` | asset | **New.** `PathLook`: trim swap, bow and arrow-trail tint, one prop |
| `Materials/Ranger/M_RangerEchoTrim.mat` | material | **New.** Smoky deep-cyan trim |
| `Tests/Game/Authoring/EchoArcherLookTests.cs` | Tests.Game | **New** |
| *small edits* | | `Data/Paths/EchoArcher.asset` (`_look`) |

## Behaviour

1. **Trim:** the slots MK-11's looks swap get `M_RangerEchoTrim` — a smoky deep cyan, darker than
   the Marksman's trim and the Skirmisher's silver-cyan, matching `M_Shade`'s hue.
2. **Tints:** `_bowTint` the same smoky deep cyan; `_arrowTrailTint` a little brighter. No violet
   (Veilrot's), lime, red or gold.
3. **Prop:** one prop that reads as shadowy — a hood or a short cape from the existing KayKit
   props — on an existing socket. If none fits, leave the list empty and say so in *As built*.
4. No code changes; if one turns out to be needed, stop and raise it.

## Tests

| Test | Given / When / Then |
|---|---|
| `Look_Assigned_ToEchoArcher` / `Look_SwapsTheMarksmansSlots` (1) |
| `Tints_AreNotVioletLimeRedOrGold` (2) |
| `Prop_SocketExists` | if a prop is set (3) |

Rule 4 is a review rule.

## Manual verification (Editor / device)

1. **[Editor]** Choose the Echo Archer: trim, bow and trails turn smoky deep cyan, unlike the other
   two paths at a glance, and the shades still read apart from the hero.

## Out of scope

- New models or textures.

## As built

_Filled at merge._
