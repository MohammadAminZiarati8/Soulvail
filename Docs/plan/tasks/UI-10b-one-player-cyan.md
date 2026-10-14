# UI-10b — One player cyan

**Size:** S · **Depends on:** the owner's answer below · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §16.4 · **Ledger rows:** none — promoted from the [parking lot](../ROADMAP.md#parking-lot)

## Goal

The build has one player cyan, read from `Palette.Player`, and the test that pinned two is turned into one that forbids a second.

## The owner's question

> **Which cyan is the player's?** `#22D3EE` (GD §16.4's hex, `Palette.Player`: the health bar, the threat arrows, Consecrate, Bulwark, and the new HUD art's tints) or `#4CE6FF` (the lighter cyan on the reticle, the focus glow and the swing arc since M0/M1)?
>
> **Recommended: `#22D3EE`.** It is the written law, it already colours the HUD the owner approved, and it moves three world-space surfaces rather than the palette and every HUD piece. The visible cost: the reticle, the focus glow and the swing arc turn a little deeper and less bright.

This is a look decision, so the task waits on the answer. If the owner picks `#4CE6FF`, rule 1 reads the other way, `Palette.Player` and GD §16.4 change to it, and the files below gain the HUD pieces that tint from it — re-sized then.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ReticleView.cs` | Game | `_colour` read from `Palette.Player`, not serialized |
| `Game/Views/FocusGlowView.cs` | Game | likewise |
| `Game/Views/PlayerView.cs` | Game | `_swingColour`'s rgb from `Palette.Player`, its authored alpha (0.25) kept |
| `Tests/Game/Presentation/PaletteTests.cs` | Tests.Game | `Palette_IsNotTheOnlyCyanInTheBuild` replaced |
| *small edits* | | `Player.prefab`, `Reticle.prefab`: the old serialized colours removed; the parking-lot line struck |

## Behaviour

1. **The reticle, the focus glow and the swing arc draw `Palette.Player`'s rgb.** Their alphas are what they are today.
2. **No serialized field holds a player cyan**: a value of `#4CE6FF` or `#22D3EE` anywhere under `Prefabs/` or `Scenes/` fails a row, so a second cyan cannot come back through the Inspector.
3. **`Palette_IsNotTheOnlyCyanInTheBuild` is retired** and replaced by rule 2's row.

## Tests

| Test | Given / When / Then |
|---|---|
| `Reticle_DrawsPalettePlayer` | rule 1 |
| `FocusGlow_DrawsPalettePlayer` | rule 1 |
| `Swing_DrawsPalettePlayerAtItsAlpha` | rule 1 |
| `Palette_IsTheOnlyPlayerCyanInTheBuild` | rules 2, 3 |

## Manual verification

1. **[Editor]** A run: reticle, focus glow and swing arc the same cyan as the health bar, in UI-07b's capture.

## Out of scope

Any other colour, and the HUD art's sprites, which are white and tinted already.

## As built

_Filled at merge._
