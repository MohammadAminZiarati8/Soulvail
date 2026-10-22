# RS-07a — Bigger arrows with a trail

**Size:** S · **Depends on:** JG-02 · **Branch:** `dev` (direct)
**Design refs:** GD §16.3 (every projectile has a tracer), GD §16.4 (player shots are cyan), GD §11.3 (no transparent layer)

## Goal

The Ranger's arrow is about 1.6× its old size and draws a thin, short cyan trail, so a shot reads
at the portrait camera's distance.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Projectiles/Arrow.prefab` | asset | mesh scale 0.66 → 1.06; a `TrailRenderer`, named as `ProjectileView._trail` |
| `Materials/M_ArrowTrail.mat` | asset | a copy of `M_Impact` (unlit, vertex colour × the trail's gradient) |
| *small edits* | | `Game/Views/ProjectileView.cs` (`_trailInPlayerCyan`); `Tests/Game/Authoring/RangerTests.cs` (two rows), `Tests/Game/Views/ProjectileViewsTests.cs` (one row) |

JG-02's `ProjectileView._trail` already clears a trail on `Bind` and `OnDespawn`.

## Public API

```csharp
// ProjectileView (serialized, no public member)
[SerializeField] private bool _trailInPlayerCyan;
```

## Behaviour

1. `Arrow.prefab`'s mesh is scaled 1.6× from 0.6615 (≈ 1.058), so the arrow is about 1.33 m long.
2. The arrow carries a `TrailRenderer` that `ProjectileView` clears: 0.15 s long, 0.12 m wide at the
   head tapering to 0, on `M_ArrowTrail` — a copy of `M_Impact`, not `M_Impact` itself — with no
   shadows. Its gradient is a shade ramp (white head, 0.4 grey tail).
3. A `ProjectileView` with `_trailInPlayerCyan` multiplies its trail's gradient by `Palette.Player`
   on its first `Bind`, once per body; without it the trail is left as authored. The arrow sets it,
   so its trail's head is `#22D3EE` in a run.

## Tests

| Test | Rule |
|---|---|
| `RangerTests.Arrow_IsOnePointSixTimesTheKayKitArrow` | 1 |
| `RangerTests.Arrow_TrailsThinShortAndCyan` | 2, 3 |
| `ProjectileViewsTests.Bind_PaintsAPlayersTrailInPaletteCyan` | 3 |

## Manual verification (Editor / device)

1. **[Editor]** A Jungle run as the Ranger: each arrow is visibly larger than before and draws a
   thin cyan streak that dies within a body length.
2. **[Editor]** No streak runs from where the previous arrow landed to the bow.

## Out of scope

- Volley's own arrow (RS-07b). The arrow's impact, `VFX_ArrowImpact`, is unchanged.

## As built

- **The cyan is read, not authored — one code edit the spec had not planned.** The first full
  suite failed `PaletteTests.Palette_IsTheOnlyPlayerCyanInTheBuild`: no colour under `Prefabs/`
  may equal `Palette.Player`, so a copy cannot drift (UI-10b). The trail's gradient is therefore a
  shade ramp, and `ProjectileView._trailInPlayerCyan` (rule 3) lays `Palette.Player` over it on the
  first `Bind` — on `Bind` and not `Awake`, which an EditMode fixture never runs (Traps §5), and once,
  because a `Gradient` read and write allocates. `FocusGlowView` reads the colour the same way.
- **The tail is 0.4 × the cyan**, opaque, so the streak darkens instead of fading (GD §11.3).
- **Mesh scale 1.0584** (0.6615 × 1.6); the arrow is 1.33 m long, 0.17 m wide.
- **Verified:** 4 381 EditMode, 4 379 passed / 1 failed / 1 inconclusive, both the known pair.
