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

**Built to the Files table plus the edits below; 13 rows in `PointBadgeTests` and one in `HudLayoutTests`. Deviations:**

1. **`Bind` takes a `DomainEventHub`, not an `IEventHub`**, which does not exist; `HudPresenter` already holds the hub and passes it. It calls `Bind(state, hub)` from `Redraw`, so on `Start` and on every `RunStarted`: the subscriptions are made once per hub and the number is drawn at once, which is rule 6's resume read.
2. **The three events mark the badge stale; `LateUpdate` writes it.** `NodeTaken` is published inside `SkillTree.Take`, before `LevelUpFlow.Take` spends the point, so a handler reading `RunState.SkillPoints` there draws one too many, for good. `OverflowGranted` and `LeveledUp` would have read true; one path is simpler than two. Still event-driven: a frame with no event costs one bool test, and a grant worth two levels is one write. Pinned by `Badge_FollowsNodeTaken`, which fails if the read moves into the handler. **UI-03c's tree screen has the same stale read** on `NodeTaken`.
3. **The component sits on a never-hidden host, `PointBadge`**, stretched over the safe area, with `Badge` (disc and `Count`) and `LevelUpToast` as children: a deactivated object gets no `Update`, and the toast's clock is one. `HudPresenter` places the children (`PlaceBadge`, `PlaceToast`) and sizes both texts in dp; it also writes the toast's word once, through `SetToastText`, so `PointBadge` takes no localiser.
4. **The key is `ui.hud.levelup`**, not the spec's `hud.levelup`, to sit with the other `ui.hud.*` rows; English and Pseudo both carry it. The prefab's authored toast text is empty (`Hud_PrefabHasNoOverlay` refuses a string with a space on that asset).
5. **The badge is 20 dp, the toast 32 dp tall; the digit draws at 12.4 dp and the toast at 22.4.** The spec gave no size. 20 is the least that keeps the digit on Android's 12 sp floor at `NumberScale`; `Text_ClearsTheAndroidFloor` is the extra row that says so. The badge's top is the star's and it hangs 3 dp past the star's right edge, inside the 4 dp gap, so it reaches neither the strip nor the bar.
6. **"Overlaps nothing but its star" means the star and the two pieces drawn in it**, the Aegis ring behind and the level over it. Both join `HudLayoutTests.Hosts`. The toast joins `BelowTheBand`; at 56 dp above centre it overlaps nothing at either aspect.
7. **The disc is Unity's built-in `Knob` sprite, tinted `Palette.Available` (`#4ADE80`).** The HUD art has no circle; the owner's art replaces the sprite, nothing else.
8. **Rows edited that the table did not name:** `PaletteTests.Palette_ThePactFrameIsNotATenthColour` (24 to 25 members), `RunEndPresenterTests.Hud_NoLongerOwnsTheDeathOverlay` (the key list gains `ui.hud.levelup`) and `SkillBarPresenterTests.Hud_NothingElseMoved` (`Count` and `LevelUpToast` are dp-sized texts). `HudLayout.cs` gained five constants. Each guard row fired on the new text, key or colour, which is what it is for.
9. **`LevelUpPresenter` is untouched** and idle while free. Manual step 1 is the owner's: tick `_hasFreePick` on `Jungle.asset` locally, do not commit it.
