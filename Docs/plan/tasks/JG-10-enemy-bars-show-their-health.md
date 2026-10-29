# JG-10 — Enemy bars show their health

**Size:** S · **Depends on:** — · **Branch:** `jg-10-enemy-bars-show-their-health`
**Design refs:** GD §16.2, GD §16.4

## Goal

An enemy's bar shortens as it loses health, reads against the ground, and shows a white chunk for what the last hit took. The bug: `Enemy.prefab`'s `Fill` has no sprite, and uGUI draws a sprite-less `Image` full, ignoring `fillAmount`.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/EnemyHealthBar.cs` | Game | track, ghost, colours, sizes |
| `Prefabs/Enemies/Enemy.prefab` | — | `Track`, `Ghost` children; sprites; wiring; sizes |
| `Tests/Game/Views/EnemyHealthBarTests.cs` | Tests.Game | rows below |
| *small edits* | | `Game/Presentation/Palette.cs`: three colours |
| *ripple* | | `PaletteTests.Palette_ThePactFrameIsNotATenthColour`: 26 → 29, message names JG-10 |

The six variants inherit the bar; override nothing in them.

## Public API

```csharp
// Palette
public static readonly Color EnemyBar = Rgb(0xE8, 0xE2, 0xD6);      // fill
public static readonly Color EnemyBarGhost = Rgb(0xFF, 0xFF, 0xFF); // health just lost
public static readonly Color EnemyBarTrack;                          // Ink #0F1419, alpha 0.85
// EnemyHealthBar (block namespace)
[SerializeField] private Image _track;
[SerializeField] private Image _ghost;
[SerializeField] private float _eliteWidthDp = 48f; // _widthDp 28 → 36, _heightDp 3 → 6
public float GhostFraction { get; }
```

## Behaviour

1. `Track` uses `Art/UI/Hud/Pill_Dark.png`; `Fill` and `Ghost` use `Art/UI/Hud/Fill_BarThin.png`, `Filled`, Horizontal, origin Left.
2. Children of `HealthBar` in sibling order `Track`, `Ghost`, `Fill`.
3. `Draw` writes `EnemyBarTrack`, `EnemyBarGhost`, `EnemyBar` to the three images, each write guarded by a compare. `Palette.Neutral` is no longer read here.
4. A hit snaps the fill. The ghost holds its old value 0.1 s, then drains at 1.5/s to the fill; each hit restarts the hold; never below the fill. `Bind`/`Unbind` set it to 1. A non-finite fraction leaves ghost = fill.
5. The ghost advances in `Tick(dt)`: `dt = 0` freezes it, and it still drains on an Elite.
6. Root is `_widthDp` (basic) or `_eliteWidthDp` (Elite) × `_heightDp`, set in `Start` and `Bind`. `Fill` and `Ghost` sit 1 dp inside the track on every side (`offsetMin`/`offsetMax`, in `Place`). The prefab stores 36, 6, 48.
7. `Tick` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `Prefab_BarImagesCarrySprites` | `Enemy.prefab` and every variant / load / three images wired, each with a sprite; fill and ghost Filled, Horizontal, Left |
| `Prefab_FillDrawsOnTop` | prefab / sibling indices / track < ghost < fill |
| `Bar_DrawsThePaletteColours` (renamed) | hit 0.6 / — / the three `Palette` colours |
| `Ghost_HoldsAfterAHit` | hit 0.6 / `Tick(0.09)` / ghost 1, fill 0.6 |
| `Ghost_DrainsToTheFill` | hit 0.6 / `Tick(0.1)`, `Tick(0.2)` / 0.7; after `Tick(1)`, 0.6 |
| `Ghost_EachHitRestartsTheHold` | hit 0.8, `Tick(0.08)`, hit 0.5 / `Tick(0.08)` / ghost 1 |
| `Ghost_FreezesUnderAPause` | hit 0.6, `Tick(0.2)` / `Tick(0)` ×10 / unchanged |
| `Ghost_DrainsOnAnElite` | Elite, hit 0.5 / `Tick(2)` / 0.5 |
| `Ghost_ResetsOnRebind` | hit 0.3, `Unbind`, `Bind` / — / 1 |
| `Ghost_IgnoresANonFiniteFraction` | hit NaN / `Tick(1)` / ghost = fill, finite |
| `Bar_IsSizedInDp` (updated) | basic, Elite / `Bind` / 36 or 48 × 6 dp; fill inset 1 dp |
| `Tick_AllocatesNothing` | hit 0.5 / `AllocationAssert.None` on `Tick(0.016f)` ×100 / 0 |

## Manual verification (Editor / device)

1. **[Editor]** Stage 1, shoot a Frog once: dark track, light fill ~⅔ long, a white chunk that drains after a beat; gone 2 s later.
2. **[Editor]** Stage 6: a Panther's bar shortens per arrow and reads on the dark floor.

## Out of scope

- Red, gold or violet bars; colour that changes with health (GD §16.4).
- Elite art; the "always show" setting; the 2 s timing; round fill ends.

## As built

Built 2026-10-30. Base prefab has Track, Ghost, Fill with existing HUD sprites;
fill now shortens. Palette supplies light fill, white loss ghost and Ink track (0.85 alpha).
Owner playtest: reduced ghost hold from 0.4 s to 0.1 s per hit; drains at 1.5/s,
freezes at dt 0 and resets with the rental;
non-finite health draws full. Bind/Start size basic/Elite bars at 36/48 x 6 dp with 1 dp insets.
No variant edits: Croaker already removes EnemyHealthBar and hides its inherited children for
the boss HUD; tests check its inherited sprites and preserve that exception.

Verified: full EditMode 4 576 passed, 0 failed, 1 existing inconclusive (animator clock);
all Behaviour rows covered, including allocation measurement. Zero compiler errors or new
analyzer warnings. dotnet format whitespace verified all 549 project files clean.
Palette's opacity test now explicitly permits only the enemy track's 0.85 alpha.

Still owed: Editor stage 1 Frog hit (track/fill/ghost, gone after 2 s), stage 6 Panther hit
(shortens and reads against the dark floor). No device playtest.
