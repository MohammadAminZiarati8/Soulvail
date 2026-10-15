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

Built as specced, owner's answer `#22D3EE`. `ReticleView` and `FocusGlowView` read `Palette.Player` through a static property (`_colour`) and expose it as `Colour`; `PlayerView.SwingColour` is `Palette.Player`'s rgb at the `SwingAlpha` constant (0.25). The three serialized fields and their prefab values are gone.

**Deviations (outside the Files table, forced by rule 2):**
1. Rule 2 as written also caught serialized cyans the table did not list, so the test failed until they went: orphaned `_colour` lines on `VFX_Bulwark` and `VFX_ConsecrateZone` (fields removed at M3-13a, YAML left behind), `_tint` on `Wight` and `VFX_Decoy` (the field default is already `Palette.Player`, so the line was dropped), and the `Ring` Image on `Hud.prefab`.
2. That Ring is `ShieldRingView._fill`, and nothing painted it: the prefab tint was its only source. `ShieldRingView.cs` gains `_fill.color = Palette.Player` in `Start`, and the prefab Image is white. Same colour on screen.
3. The `AutoCast` glow Image on `Hud.prefab` held `#4CE6FF` at alpha 0.85; code overwrites it with `Palette.Player` before it shows, so it is now white (alpha 0.85 kept).
4. `Palette.cs` and `PaletteTests.cs` doc comments that pointed at the retired test were reworded.

**Tests:** `Reticle_DrawsPalettePlayer`, `FocusGlow_DrawsPalettePlayer`, `Swing_DrawsPalettePlayerAtItsAlpha`, `Palette_IsTheOnlyPlayerCyanInTheBuild` (scans every `.prefab` and `.unity` under `Prefabs/` and `Scenes/` for an rgb of either cyan); `Palette_IsNotTheOnlyCyanInTheBuild` retired. 4 282 EditMode: 4 280 passed / 1 failed / 1 inconclusive (+3: +4 rows, −1 retired); the failure is `CharacterPreviewTests.Resize_…` (D3D12 depth, Traps §9).

**Owed:** manual step 1, the Editor look at reticle, focus glow and swing arc beside the health bar.
