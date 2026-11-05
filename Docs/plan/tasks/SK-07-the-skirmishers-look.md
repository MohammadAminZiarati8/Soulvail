# SK-07 — The Skirmisher's look

**Size:** S · **Depends on:** SK-01b · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (the Skirmisher's tint); MK-11 (`PathLook`, data-driven)

## Goal

Choosing the Skirmisher changes the Ranger's look, the way the Marksman's choice already does,
with data only.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Data/Paths/SkirmisherLook.asset` | asset | **New.** `PathLook`: trim swap, bow and arrow-trail tint, one prop |
| `Materials/Ranger/M_RangerSkirmisherTrim.mat` | material | **New.** Pale silver-cyan trim |
| `Tests/Game/Authoring/SkirmisherLookTests.cs` | Tests.Game | **New** |
| *small edits* | | `Data/Paths/Skirmisher.asset` (`_look`) |

## Behaviour

1. **Trim:** the materials MK-11's Marksman look swaps get `M_RangerSkirmisherTrim` instead — a
   pale silver-cyan, lighter than the Marksman's trim.
2. **Tints:** `_bowTint` a pale silver-cyan; `_arrowTrailTint` the same, a little brighter. No
   green, lime, red or gold: enemies own lime, and the player owns cyan.
3. **Prop:** one light prop that reads as fast — a short scarf or a hip quiver from the existing
   KayKit props — on an existing socket. If no fitting prop exists, leave the prop list empty and
   say so in *As built*.
4. No code changes; if one turns out to be needed, stop and raise it.

## Tests

| Test | Given / When / Then |
|---|---|
| `Look_Assigned_ToSkirmisher` (1) |
| `Look_SwapsTheMarksmansSlots` | same renderer/slot list as the Marksman look (1) |
| `Tints_AreNotGreenRedOrGold` | hue outside the banned ranges (2) |
| `Prop_SocketExists` | if a prop is set (3) |

## Manual verification (Editor / device)

1. **[Editor]** Choose the Skirmisher: the Ranger's trim turns pale silver-cyan, the bow and the
   arrow trails match, and the look differs at a glance from the Marksman's.

## Out of scope

- New models or textures.

## As built

_Filled at merge._
