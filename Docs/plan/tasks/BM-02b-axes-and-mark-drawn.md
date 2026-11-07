# BM-02b — Axes and the Mark drawn

**Size:** M · **Depends on:** BM-02a · **Branch:** `dev` (direct)
**Design refs:** CH §3.6; GD §16.4, §16.5; P1 readable at first look

## Goal

Spinning axes with a cyan trail that follow core's flight, and a cyan claw glyph on every marked enemy.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/AxeViews.cs` | Game | **New.** Pool of `AxeSystem.Capacity`; spawns on `AxeThrown`, removes on `AxeCaught` and at stage changes |
| `Game/Views/AxeView.cs` | Game | **New.** Placed from `AxeSystem.InFlight` each frame, spins 900°/s, flashes on `AxeTurned` |
| `Prefabs/Projectiles/Axe.prefab` | — | **New.** `axe_1handed` with a short cyan trail |
| `Game/Views/MarkGlyphViews.cs` | Game | **New.** A cyan claw glyph above each marked enemy's head |
| `Prefabs/Vfx/MarkGlyph.prefab` | — | **New** |
| `Tests/Game/Views/AxeViewsTests.cs` | Tests.Game | **New** |
| *small edits* | | `RunScope` registers both views and a narrow read of `AxeSystem.InFlight`; `StatusIconBook.asset` gains the marked icon; `Art/UI/Icons/Status_Marked.png` |

## Behaviour

1. One axe view per axe in flight, at its core position every frame, never ahead of core.
2. The axe spins about its vertical axis; the trail is `Palette.Player`; a brief flash at the turn.
3. On `AxeCaught` the view returns to the pool; on a stage transition all return.
4. **Mark glyph:** a small cyan claw mark over the head of each marked enemy, shown on the body
   rather than in the bar (GD §16.5: the body carries the read); on the bar the marked icon shows
   only when nothing outranks it.
5. Glyph and trail use no red-orange, violet or lime.

## Tests

| Test | Given / When / Then |
|---|---|
| `Thrown_SpawnsOneView_AtCorePosition` (1) |
| `Caught_ReturnsToPool` / `StageChange_ClearsAll` (3) |
| `Trail_IsPlayerCyan` (2, 5) |
| `Marked_ShowsGlyph_Unmarked_Hides` (4) |
| `Icon_MarkedInStatusIconBook` (4) |

## Manual verification (Editor / device)

1. **[Editor]** A crowd of frogs: each axe spins out and back with a cyan trail; struck frogs wear a
   claw glyph that fades 4 s after the last hit.
2. **[device]** The glyph reads at phone zoom over a Frog and over a Panther.

## Out of scope

- Hound, swap, tree.

## As built

_Filled at merge._
