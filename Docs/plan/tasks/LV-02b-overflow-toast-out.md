# LV-02b — The Overflow toast leaves the HUD

**Size:** S · **Depends on:** LV-02a · **Branch:** `dev`
**Design refs:** owner 2026-11-05 (Overflow goes away)

## Goal

Nothing in Unity listens for `OverflowGranted`; the toast, its key and its wiring are gone, so LV-02c can delete the event.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/OverflowToast.cs` (+ `.meta`) | Game | deleted |
| `Game/Presentation/PointBadge.cs` | Game | drops its `OverflowGranted` subscription |
| `Game/Composition/RunScope.cs` | Game | drops `_overflowToast` |
| `Prefabs/UI/Hud.prefab` | — | `OverflowToast` object removed |
| `Scenes/Run.unity` | — | `_overflowToast` reference removed |
| `Tests/Game/Presentation/XpBarViewTests.cs` | Tests.Game | toast rows removed |
| *small edits* | | `ui.overflow.granted` out of English/Pseudo; `TableLocalizerTests` type list; `BossBarViewTests`/`VeilrotMeterViewTests` toast-presence rows; `PointBadgeTests` Overflow rows; comment mentions in `TimedEffects`, `BossBarView`, `LevelUpPresenter`, `HpBarViewTests`, `PaletteTests` |

## Public API

```csharp
// Removed: OverflowToast. PointBadge no longer subscribes to OverflowGranted.
```

## Behaviour

1. `Hud.prefab` holds no `OverflowToast`; `RunScope` registers none.
2. `PointBadge` redraws from `LeveledUp`, `SkillTaken` and its existing reads only.
3. No table holds `ui.overflow.granted`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Hud_HoldsNoOverflowToast` (XpBarViewTests) | Hud.prefab / search by name / no `OverflowToast` child |
| `PointBadge_IgnoresNothingItNeeds` (PointBadgeTests) | badge / level up then take / count right |
| `Tables_HaveNoOverflowKey` (TableLocalizerTests) | English, Pseudo / lookup / absent |

## Manual verification (Editor / device)

1. **[Editor]** Play Jungle to a level-up: badge counts, no toast, no Console error.

## Out of scope

- Core's Overflow (LV-02c).
