# PL-06 — The Packleader's look

**Size:** S · **Depends on:** PL-05c · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Packleader look); MK-11 (a path changes the hero's look); SK-07, EA-06

## Goal

Choosing the Packleader paints cyan war-paint stripes on the Beastmaster and on every hound.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Paths/Packleader.asset` | — | Its `PathLook`: the hero's stripe tint |
| `Game/Authoring/PathLook.cs` | Game | An optional pet tint beside the hero's |
| `Game/Views/PathLookView.cs` | Game | Applies the pet tint to every pet body's stripe mask when a path is chosen and at each spawn |
| `Tests/Game/Views/PackleaderLookTests.cs` | Tests.Game | **New** |
| *small edits* | | `T_Beastmaster_Albedo` / `T_Hound_Albedo` gain a stripe mask cell if they lack one |

## Behaviour

1. With the Packleader chosen, the Beastmaster shows cyan stripes on the arms and chest, and every
   hound (lead and pack) shows cyan stripes on the flanks.
2. Pack hounds spawned later in the run get the stripes on arrival.
3. A run without the Packleader shows neither.
4. The tint is `Palette.Player`'s family; no violet, gold, lime or red-orange.

## Tests

| Test | Given / When / Then |
|---|---|
| `Packleader_HeroStriped` / `Packleader_EveryHoundStriped` (1) |
| `LaterPackHound_Striped` (2) |
| `NoPath_NoStripes` (3) |
| `Tint_InPlayerFamily` (4) |

## Manual verification (Editor / device)

1. **[Editor]** Choose Packleader at 11: the hero and the hound take cyan stripes; a pack hound
   learned later arrives striped.

## Out of scope

- Axe Master and Apex Bond (not yet specced).

## As built

_Filled at merge._
