# UI-03b — The level-up banner and the point badge

**Size:** S · **Depends on:** UI-03a · **Branch:** `ui-03b-the-point-badge`
**Design refs:** UI-01b (the level star), GD §16.4 (colour law), Traps §9 (dp) · **Ledger rows:** none

## Goal

A free-pick run tells the player they levelled, without stopping, and shows the unspent points on the level star.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/PointBadge.cs` | Game | the green count on the star and the "LEVEL UP!" toast |
| `Tests/Game/Presentation/PointBadgeTests.cs` | Tests.Game | one row per rule |
| *small edits* | | `HudPresenter` (calls `Bind`, places the badge on the star); `Hud.prefab` (badge, toast); `Palette` (`Available`, a green, if none exists); string table (`hud.levelup`); `HudLayoutTests` (one row) |

## Public API

```csharp
// Block namespace (Traps §5). A dumb view: reads core, sends nothing.
public sealed class PointBadge : MonoBehaviour
{
    public void Bind(RunState state, IEventHub hub);   // called by HudPresenter, which is [Inject]ed
    public int Shown { get; }                          // the number drawn; 0 = hidden
    public bool ToastVisible { get; }
}
```

## Behaviour

1. The badge sits on the level star's upper-right corner, sized and offset in dp, and shows `RunState.SkillPoints`. Hidden at 0.
2. Its tint is `Palette.Available`, a green — never red-orange, which means danger alone.
3. `LeveledUp` shows "LEVEL UP!" (a `LocKey`) above the arena's centre for 1.5 s **of unscaled time**, so a pause does not freeze it on screen. It never pauses and blocks no touch (`raycastTarget` off); a second level restarts it.
4. The number is rewritten on `LeveledUp`, `NodeTaken` and `OverflowGranted`, never polled per frame; an unchanged number is not written (UI-01d).
5. In a mode that is not free the badge and toast stay off; the old offer screen owns that flow.
6. On resume the badge shows the banked points before the first tick; at run end both clear.
7. At 20:9 and 9:20 the badge overlaps nothing but its star.

## Tests

| Test | Given / When / Then |
|---|---|
| `Badge_ShowsPoints_HidesAtZero` | 0, then 3 points / `Shown` 0, then 3 |
| `Badge_FollowsNodeTaken` | 3 points / take one / 2 |
| `Badge_FollowsOverflow` | 2 points, settled / `OverflowGranted` / 0 |
| `Toast_LastsOnePointFiveUnscaledSeconds` | level, `timeScale` 0 / 1.6 unscaled s / gone |
| `Toast_SecondLevel_Restarts` | two levels / one toast, timer reset |
| `NotFree_BadgeAndToastStayOff` | rule 5 |
| `Resume_ShowsBankedPoints` | rule 6 |
| `Badge_IsPaletteGreen` | tint == `Palette.Available` |
| `Toast_BlocksNoTouch` | every graphic `raycastTarget` false |
| `Badge_SameNumber_IsNotRewritten` | same count twice / text write count 1 |
| `HudLayout_BadgeOverlapsOnlyTheStar` | both aspects |

## Manual verification

1. **[Editor]** Tick `_hasFreePick` on the Jungle asset locally (do not commit it), level up: toast, no pause, badge 1. Spending is UI-03c.

## Out of scope

- The star as a button (UI-03c). Sound. Any tree drawing.
- Removing `LevelUpPresenter`: it stays, idle while free.

## As built

_Filled at merge, 6 000 bytes or fewer._
