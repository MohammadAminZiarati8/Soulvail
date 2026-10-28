# RS-08c — A wind trail on the arrow, cyan on the volley

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** RS-07a/b (the arrows' look), GD §16 (colour law)

## Goal

An ordinary arrow streaks a thin, pale wind trail instead of cyan. A volley arrow stays cyan and is
clearly bigger than an ordinary one.

## Why this shape

- **Cyan is the volley's.** Today both prefabs set `_trailInPlayerCyan`, so the volley does not stand
  out by colour. The ordinary arrow turns its flag off and gets its colour from the new `Palette.Wind`, so
  the colour still lives in `Palette`.
- **"Wind"** = near-white with a cool tint, alpha 0.55 at the head, fading to 0. Width 0.12 → 0, 0.18 s.
  The head stays the arrow's model.
- **The volley arrow is already 1.38× the ordinary one** (model scale 1.455 vs 1.058). That is not enough
  to see, so it goes to 2.0 (≈ 1.9×) with a trail 0.4 wide.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/Palette.cs` | Game | `Wind` colour |
| `Game/Views/ProjectileView.cs` | Game | `_trailInWind` flag: paints `Palette.Wind` over the shade ramp, like the cyan one |
| `Prefabs/Projectiles/Arrow.prefab` | — | cyan off, wind on; trail 0.12 wide, 0.18 s |
| `Prefabs/Projectiles/VolleyArrow.prefab` | — | model scale 2.0; trail 0.4 wide; cyan kept |
| `Tests/Game/Views/ProjectileViewsTests.cs` | Tests.Game | rules 1–4 |

## Public API

```csharp
public static readonly Color Palette.Wind;   // pale, cool, not Palette.Player
```

## Behaviour

1. An ordinary Ranger arrow's trail is `Palette.Wind` at its head, with alpha fading to 0 at its tail. No cyan.
2. `Palette.Wind` is not `Palette.Player`, and is near-white (every channel ≥ 0.8).
3. A volley arrow's trail is `Palette.Player`, as today.
4. A volley arrow's model is at least 1.8× an ordinary arrow's, and its trail is wider.

## Tests

| Test | Rule |
|---|---|
| `Bind_PaintsAnArrowsTrailInWind` (Arrow prefab) | 1 |
| `Wind_IsPaleAndNotPlayerCyan` | 2 |
| `Bind_PaintsAPlayersTrailInPaletteCyan` (now the VolleyArrow prefab) | 3 |
| `VolleyArrow_IsBiggerThanTheArrow` | 4 |

## Manual verification (Editor / device)

1. **[Editor]** Stage 1: ordinary arrows leave a thin pale streak. The 6th shot's arrows are large and cyan.

## Out of scope

- Particle gusts or wavy trail shaders. The volley's burst on hit (RS-07b keeps it).

## As built

_Not built._
