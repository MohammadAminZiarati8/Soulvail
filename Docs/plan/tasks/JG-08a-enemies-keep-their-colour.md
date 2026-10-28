# JG-08a — Enemies keep their colour as they lose health

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §16.2 (enemy health read), M3-13b (the dying tint)

## Goal

No enemy, boss included, darkens as its health falls. Health is read from bars only: the boss bar, the
Elite's fixed bar, and the basic enemy's brief bar on hit.

## Why this shape

- **The owner's ruling (2026-10-28) replaces GD §16.2's "a body that darkens as it dies".** The darkening is
  one term, `_damageTint`, composed in `EnemyHitFeedback.BodyColour`. Removing it leaves the flash, the
  telegraph glow and the dissolve as they are, because all three are written against `BodyColour`.
- **The bars already exist.** `EnemyHealthBar` keeps an Elite's bar up and fades a basic enemy's 2 s after a
  hit. `BossBarView` is untouched. Nothing new is drawn.
- `Palette.EnemyDying` goes with its only reader. `BossBarViewTests`' row comparing it to `Palette.Boss` goes too.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/EnemyHitFeedback.cs` | Game | `SetDamageTint`, `_damageTint` removed; `EnemyDamaged` drives the flash only |
| `Game/Presentation/Palette.cs` | Game | `EnemyDying` removed |
| `Tests/Game/Views/EnemyHitFeedbackTests.cs` | Tests.Game | `Tint_*` rows replaced by rules 1–3 |
| `Tests/Game/Presentation/BossBarViewTests.cs` | Tests.Game | the `EnemyDying` comparison dropped |
| *small edits* | | GD §16.2's sentence, rewritten to the ruling |

## Public API

```csharp
// Removes Palette.EnemyDying. No new members.
```

## Behaviour

1. A body at any HP fraction, 0 included, rests at its archetype colour.
2. The hit flash and the wind-up glow still return to the archetype colour, not a darker one.
3. The dissolve fades from the archetype colour.
4. An Elite's health bar still never fades, and a basic enemy's still appears on hit. Those `EnemyHealthBar`
   tests stay green, untouched.

## Tests

| Test | Rule |
|---|---|
| `Colour_UnchangedAtAnyHealth` (1, 0.5, 0.01, 0) | 1 |
| `Colour_FlashAndTelegraphReturnToTheArchetype` | 2 |
| `Colour_DissolveFadesFromTheArchetype` | 3 |
| existing `EnemyHealthBarTests` | 4 |

## Manual verification (Editor / device)

1. **[Editor]** Stage 1: a Frog hit down to a sliver keeps its green. Its thin bar shows on each hit.
2. **[Editor]** Stage 5: Old Croaker keeps its colour to the end. The boss bar drains.

## Out of scope

- Elites themselves (none spawn yet). A new low-health effect on bodies.

## As built

`EnemyHitFeedback` no longer stores or applies a health tint. Damage still flashes white, the wind-up still glows, and the corpse dissolves from its archetype colour. `Palette.EnemyDying` and its boss-bar comparison are gone; GD §16.2 records the owner's bar-only ruling. Six new EditMode cases passed, covering HP fractions 1, 0.5, 0.01 and 0, flash and telegraph return, and dissolve. `EnemyHealthBarTests` passed 28/28 unchanged. Manual Editor checks: a low-HP Frog on stage 1 and Old Croaker on stage 5 should retain their colour while their bars drain.
