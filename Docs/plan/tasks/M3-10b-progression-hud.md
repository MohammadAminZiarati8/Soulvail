# M3-10b — The progression HUD: the XP strip, the level, the auto-cast row, and Overflow

**Size:** M · **Depends on:** M3-01a (`XpChanged`, `LeveledUp`), M3-06 (the fills), M3-07a (which skills are Auto), M3-08a (`OverflowGranted`) · **Branch:** `m3-10b-progression-hud`
**Design refs:** GD §13.1, §16.1, §16.4; CH §4.2, §5.2; CC §6.1; AR §7, §8, §18.2 · **Ledger rows:** **6** (three more colour readers with no `Palette` file — rule 9), **9** (the auto-cast row has nowhere to put a key at all — rule 8), 4 (a 4 dp strip and a 24 dp fill are both device-only legibility questions), 1 (Overflow is half of stage 30's answer and this is the only place the player is told it happened)

## Goal

GD §16.1's other half: a strip that says how close the next level is, a number that says which level this is, a row that makes an auto-cast build **visible**, and the one announcement Overflow ever gets.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/XpBarView.cs` | Game | the thin strip along the top edge — **block namespace** (Traps §5) |
| `Game/Presentation/AutoCastRow.cs` | Game | the row of radial fills under HP: every Active the player is *not* casting themselves — **block namespace** |
| `Game/Presentation/OverflowToast.cs` | Game | the only thing that ever says an Overflow level happened — **block namespace** |
| `Tests/Game/Presentation/XpBarViewTests.cs` | Tests.Game | the strip, the level number and the toast — one fixture, they are one row of the HUD |
| `Tests/Game/Presentation/AutoCastRowTests.cs` | Tests.Game | membership, fills, and what leaves the row |
| *small edits* | | `Game/Presentation/HudPresenter.cs` — the level label, its place in `Place()`, and the `LeveledUp` subscription (rule 3); `Prefabs/UI/Hud.prefab` gains the strip, the label, the row and the toast; `Game/Composition/RunScope.cs` + the three components |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Game.Presentation
{
    public sealed class XpBarView : MonoBehaviour
    {
        // [SerializeField] Image _fill; float _heightDp = 4f;          // GD §16.1
        [Inject] public void Construct(IRunSession session, DomainEventHub hub);
    }

    public sealed class AutoCastRow : MonoBehaviour
    {
        // [SerializeField] Image[] _fills (SkillRunner.MaxActives); Image[] _kindStrips;
        // [SerializeField] float _cellSizeDp = 24f, _gapDp = 4f; Vector2 _marginDp;
        // [SerializeField] Color _passive, _active, _upgrade, _keystone;   // rule 9
        [Inject] public void Construct(IRunSession session, DomainEventHub hub, ContentCatalog catalog);
    }

    public sealed class OverflowToast : MonoBehaviour
    {
        // [SerializeField] CanvasGroup _root; TMP_Text _text; float _secondsShown = 2f;
        [Inject] public void Construct(DomainEventHub hub);
    }
}
```

## Behaviour

**The strip**

1. **A 4 dp strip along the very top edge, full width, and deliberately ignorable.** GD §16.1's words are *"Fills toward the next level. Full width, 4px, ignorable but always there."* Four **dp**, not four pixels: the project has measured every touch target and every bar in dp since M1-16 because a reference pixel is a different physical size on every phone, and a strip that vanished on a dense screen would be the one HUD element nobody noticed was broken. The number is a serialized field, so the owner can disagree on a real device.
2. **Driven by `XpChanged` and nothing else.** The event carries `Fraction` already settled (M3-01a rule 5: every `LeveledUp` first, one `XpChanged` last), so the strip never has to draw a fraction above 1 and never has to ask the run anything. `RunStarted` draws the opening state, for `HudPresenter`'s reason — a resumed run comes back part-way through a level and no event describes a frame that has not happened.
3. **The level is a number beside HP, and it belongs to `HudPresenter`.** GD §16.1 puts it *"top-left, beside HP"* and `HudPresenter.Place` is explicitly one method for that whole row, because *"the three rects have to agree about where each other are, and three components each doing their own arithmetic is three chances for them to overlap."* So the label is a fourth rect in that method rather than a fourth component — and `HudPresenter` gains one subscription, `LeveledUp`, plus the level read in its existing `Redraw`. Anything else would be a second class laying out the same row.

**The row**

4. **It exists because the default build is invisible, and that is GD §16.1's own argument.** Every skill starts on Auto (CC §6.1) and *"a player who never opens the menu has a complete, playable game with one button"* — which means a player who never opens the menu has every active firing itself with no readout anywhere. GD §16.1 is blunt about the consequence: *"Auto-cast skills need visible cooldowns even though the player doesn't trigger them — otherwise the build is invisible."* This row is the only thing in M3 that makes an auto-cast build legible, and it matters more than the S1–S4 buttons, which only exist for players who opted out of the default.
5. **Membership is exactly the Actives that are *not* in a slot.** Walk `OwnedActiveCount`, keep the ids where `IsAutoCast(id)` is true, draw one cell each. A skill switched to Manual **leaves this row and gains a button** (M3-10a); a skill switched back does the reverse. The two readouts are disjoint by construction, so nothing on screen ever shows one cooldown twice — which is the rule `HudPresenter`'s own remarks state for the Charge (*"a second readout here would be a second answer to when the player may dash"*), applied to the skills.
6. **Membership is rebuilt from two events; the fills are polled.** `NodeTaken` with `Kind == Active` adds a cell and `SkillAutoCastChanged` moves one in or out — both carry what is needed (M3-03 rule 7, M3-07a rule 9). The **fills** are read every frame from `SkillCooldownFraction(index)`, `SkillButton`'s bargain: a fill slides continuously and an event per frame is not an event. Only the changed ones are written, for the canvas-rebuild reason `SkillButton.Draw` gives.
7. **Sized for twelve and drawn for as many as there are.** `SkillRunner.MaxActives` is 12 and CH §4's ~25 % of 27 is about seven; twelve 24 dp cells with 4 dp gaps is 332 dp, which fits a landscape safe area under a 320 dp health bar. Cells are pre-made and deactivated, never instantiated during a run.
8. **A cell has no icon and no text, and that is ledger row 9's worst corner.** There are no skill icons, and a 24 dp cell cannot hold `skill.oathbound.consecrate` — it cannot hold *"Consecrate"* either. So a cell is a radial fill and a kind tint, and which skill it is is not communicated at all until art exists. **This is worse than the card and the row**, where a key at least occupies the space its text will: here there is no space, so M6-10 does not fix it and only icons will. Named so M3-15 rules on it with the rest of row 9 and does not discover it in a playtest.
9. **Four kind tints again, serialized, still not a palette.** M3-08b rule 8's argument for the third time: `OfferCard` has four, `TreeNodeView` has four plus three states, and this row has four. Ledger row 6's owner (M3-13) now inherits **six** readers across three files. One shared `Palette` file fixes all of it and inventing it here would still be M3-13 arriving early — but six is the number the row should be sized against, and it is no longer a small tidy-up.

**Overflow**

10. **One toast, two seconds, no pause.** M3-08a rule 7 made Overflow silent and instant *"no pause, no screen"* and announced only through `OverflowGranted`; this is the announcement. The event carries the level and the running total (M3-08a rule 7), so the toast reads *"Overflow ×N"* — the total rather than the increment, because the fourteenth one at stage 30 means *"you have +28 % damage"* and the increment means *"you got 2 % again"*. It is the difference between the mechanic feeling cumulative and feeling like a consolation prize.
11. **It is the only place the player learns about half of stage 30's power.** Ledger row 1's arithmetic says Overflow supplies ≈ +28 % damage and +28 % max HP by stage 30, against the +52 % a Husk needs — *"Overflow alone supplies more than half of it"* (M3-08a rule 8). A player who never sees it announced has no way to know their character got stronger without picking anything, which is precisely CH §5.2's *"levelling never stops meaning something"* failing quietly. Two seconds and a number is the cheapest thing that makes it true.
12. **Unscaled time, and it takes no touches.** The toast fires while the game is running (Overflow never pauses), but a level-up or a pause can land on top of it and `Time.timeScale` goes to 0 there (M3-08a rule 13) — a toast counting scaled seconds would hang on screen for the length of the screen above it. `blocksRaycasts` false, like every other non-interactive overlay.
13. **The toast is a `LocKey` plus a number** and draws unresolved like the rest (ledger row 9's fifth reader), which for once is nearly harmless: *"Overflow ×14"* is mostly the number, and the number resolves fine.

## Tests

| Test | Given / When / Then |
|---|---|
| `Xp_FillFollowsTheEvent` | a run / `XpChanged(_, 3, 0.42)` / `_fill.fillAmount` 0.42 (rule 2) |
| `Xp_NeverDrawsAboveOne` | a grant crossing two thresholds / the events / every fraction written is in [0, 1] — M3-01a rule 5 from this side (rule 2) |
| `Xp_DrawsTheOpeningStateOnRunStarted` | a resumed run at level 4 with 30 xp / `RunStarted` / the strip shows `XpFraction`, no event needed (rule 2) |
| `Xp_StripIsFourDpAndFullWidth` | a canvas with a known scale / `Start` / height `4 × pxPerDp`, anchored across the top edge (rule 1) |
| `Level_ShowsAndUpdates` | level 1 / `LeveledUp(2, 1)` / the label reads *"2"* (rule 3) |
| `Level_IsPlacedInTheHudRow` | the HP row / `Place` / the level label sits beside the ring and overlaps nothing — `HudPresenter`'s existing layout rows extended (rule 3) |
| `Level_DrawsOnRunStarted` | a resumed run at level 7 / `RunStarted` / *"7"* (rule 3) |
| `Row_ShowsOneCellPerAutoActive` | two Actives owned, both Auto / `NodeTaken` ×2 / two cells active (rules 5, 6) |
| `Row_ExcludesAManualSkill` | two Auto Actives / `SkillAutoCastChanged(A, false, 0)` / one cell — A left the row (rule 5) |
| `Row_TakesASkillBack` | the row above / `SkillAutoCastChanged(A, true, −1)` / two cells again (rule 5) |
| `Row_AndButtonsAreDisjoint` | A manual, B auto / — / A has a button and no cell; B has a cell and no button (rule 5) |
| `Row_IgnoresPassives` | a Passive taken / `NodeTaken` / no cell (rule 5) |
| `Row_FillsTrackTheirSkills` | two Auto Actives, cast one / the next frames / that cell's fill moves and the other's does not (rule 6) |
| `Row_WritesOnlyChangedFills` | two ready Actives, idle / 60 frames / no `fillAmount` assignment after the first (rule 6) |
| `Row_HoldsTwelve` | twelve Auto Actives / — / twelve cells, none instantiated after `Start` (rule 7) |
| `Row_TintsByKind` | one of each kind / — / the strips differ (rule 9) |
| `Row_DrawsTheOpeningStateOnRunStarted` | a resumed run owning two Auto Actives / `RunStarted` / two cells (rule 6) |
| `Overflow_ToastShowsTheTotal` | — / `OverflowGranted(30, 14)` / the toast reads the key and *"14"*, not *"1"* (rule 10) |
| `Overflow_ToastHidesOnItsOwn` | the toast up / `_secondsShown` of **unscaled** time / hidden (rules 10, 12) |
| `Overflow_ToastSurvivesAPause` | the toast up / `Time.timeScale` 0 for 5 s, then 1 / it was still counting down and is gone (rule 12) |
| `Overflow_ToastTakesNoTouches` | the toast up / — / `blocksRaycasts` false (rule 12) |
| `Overflow_NothingPauses` | a full tree, a pick owed / `OpenLevelUp` / the toast shows and `RunPause.IsPaused` is false — M3-08a rule 7 from this side (rule 10) |
| `Overflow_RepeatsRetriggerTheToast` | three `OverflowGranted` in a row / — / the toast shows the latest total each time, one timer (rule 10) |
| `Subscribes_FromConstruct` | each component constructed after `RunStarted` / publish its event / it renders (all three) |
| `Destroy_DropsSubscriptions` | each / `OnDestroy`, then publish / nothing throws |
| `Prefab_IsDressed` | `Hud.prefab` / read the fields back after a save (Traps §5) / the strip, the level label, twelve row cells and the toast present and wired |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend. The top edge carries a thin strip that creeps right with every kill, and *"1"* sits beside the health bar. Kill enough to level: the strip resets to nearly empty and the number becomes *"2"* (rules 1, 3).
2. **[Editor]** With a hand-authored tree, take two Actives and leave both on Auto. Two small fills appear under the health bar and cycle on their own while you do nothing — which is the whole of rule 4, and the first time an auto-cast build has been visible at all.
3. **[Editor]** Set one to Manual. Its cell leaves the row and a button appears in the corner; set it back and the reverse (rule 5).
4. **[Editor]** Take every node of a four-node hand-authored tree, then level again. No screen, and a toast reads *"Overflow ×1"*. Level twice more: *"×2"*, *"×3"* (rules 10, 11).
5. **[device]** Ledger row 4: whether a 4 dp strip on the very top edge is visible at all next to a notch, and whether a 24 dp radial fill reads as a cooldown or as a dot. Both numbers are fields for this reason.
6. **[device]** Ledger row 9 at its most awkward: a row of identical circles with no icons. If a playtest cannot tell which cell is which skill, the answer is art, not localisation (rule 8).

## Out of scope

- **The S1–S4 buttons** — M3-10a. Rule 5 is the line between them.
- **Skill icons** — M7's art pass. Rule 8 says what their absence costs.
- **A `Palette` file** — M3-13, ledger row 6. Rule 9 brings the reader count to six and sizes the row.
- **Health-bar treatment** (GD §16.2's tint, elite bars, boss stub) — M3-13. This task touches `HudPresenter`'s layout and deliberately not its bars.
- **Essence, the Veilrot meter, stage/wave text, enemies-remaining** — the rest of GD §16.1's table, owned by M6 and M2's existing work. Only the four progression readouts are here.
- **HUD opacity** (GD §16.1's *"user-adjustable down to 40 %"*) — M8-03, a settings field and a profile bump by M3-09c rule 3's rule.
- **A "you levelled" flourish.** The screen already stops the world (M3-08b); a second celebration is the interruption GD §13.1 budgets two seconds for, spent twice.

## As built

**Built as specced, with one ruling, one finding that changed the implementation, and two rows added by name.**

**The Files table shipped whole.** `Game/Presentation/XpBarView.cs`, `Game/Presentation/AutoCastRow.cs` and `Game/Presentation/OverflowToast.cs` are all new, all **block namespaces** (Traps §5 — all three are `MonoBehaviour`s, unlike M3-10a's `SkillSlotInput`), and all three `MonoScript`s were confirmed to resolve their class off disk. `Tests/Game/Presentation/XpBarViewTests.cs` and `Tests/Game/Presentation/AutoCastRowTests.cs` are the two fixtures. The named small edits: `HudPresenter` gained `_levelText`, a `LeveledUp` subscription, a `WriteLevel` and a **fourth rect in `Place()`**; `Prefabs/UI/Hud.prefab` gained the strip, the label, the row's twelve cells and the toast; `Game/Composition/RunScope.cs` gained three optional serialized fields and their `RegisterComponent`s. **Five counted code files, size M at its ceiling, no split needed.**

**One file outside the table and it is named here:** `Scenes/Run.unity`, dressed with the three cross-prefab references the new `RunScope` fields need — 36 insertions, zero deletions, being three `stripped MonoBehaviour` stubs plus three lines on the scope. Nearer M3-10a's 12 than M3-09d's 170 because it adds references onto the existing HUD instance rather than a prefab instance. Without it every run in the build throws from three `Start` guards, so it is mandatory rather than tidy — `BootSmokeTests` passing 3/3 in PlayMode is the evidence that it composes.

**Rule 3's level label is optional rather than guarded, which is a deviation worth stating.** `HudPresenter.Start` still refuses its original four pieces by name and the level label is not among them — it is optional on `_fade`'s terms, because a HUD without one plays exactly the same fight and cannot say which level the player is on, where a missing HP bar looks like a game that has stopped. `Place` and `WriteLevel` both tolerate the null. The alternative was a fifth name in that exception's message and a throw for every hand-built `HudPresenter`, which is a bigger edit to a method nothing had ever tested.

**Rule 3's width is a `private const`, and that is why no new non-finite row is owed on `HudPresenter`.** `LevelWidthDp` is 48 and nothing can pass a value through it — `LevelUpFlow.OverflowDamage`'s argument, applied to a layout. Every *other* number in that row is an Inspector door, and `Place()` has had no non-finite answer since M1-17; adding a fifth door would have owed a row about the other four as well, which is a task rather than a small edit. Said out loud rather than left as an absence (M3-04's lesson).

**Rule 9's four tints ship and three of them are unreachable.** Rule 5's membership admits Actives and nothing else, so a drawn cell is always `_active`; the lookup is total anyway for `TreeNodeView.Frame`'s reason, and `Row_TintsByKind` reaches the private lookup to prove all four answer differently *and* that all four agree with `OfferCard`'s value for value — which is what M3-09d did for `TreeNodeView`, so M3-13a now inherits one answer to CH §4's four kinds across three files. **Rule 9's reader count is stale in this spec and was not followed:** it says *"six readers across three files"*, which predates M3-00d's recount to nine across nine files; the ledger was updated from the recount, to **six predicted and three real**.

**The finding that changed the implementation: membership is rebuilt on the next `Update`, not inside the event handler.** `LevelUpFlow.ChooseOffer` calls `SkillTree.Take` — which publishes `NodeTaken` — and hands the spec to `SkillRunner.Add` *afterwards* (`LevelUpFlow.cs:239` then `:246`), so a handler reading `OwnedActiveCount` from inside the event sees the count from **before** the node. That is M3-09c's finding arriving at a second reader, and a row that rebuilt in the handler would have missed the very cell it had just been told about. So the two events set one flag and `Update` acts on it — which is where the fills are read anyway, and which runs at a `timeScale` of 0, so the row is correct before the level-up screen has even closed. Every membership row in `AutoCastRowTests` passes a frame for this reason, and the fixture remarks say so.

**Rule 5 needed no twelfth `RunState` read.** The walk is `OwnedActiveCount` → `SkillIdAt(i)` → `IsAutoCast(id)` → `SkillCooldownFraction(i)`, and all four already existed; the `internal` seal on `RunState.Skills` did not move (AR §18.2). This task is the first reader of `IsAutoCast` outside the Skills screen, and it is the one class allowed to be — `SkillBarPresenterTests.Bar_KnowsNothingAboutAuto` walks the IL of the two M3-10a classes to keep them out of it, and that row was left alone.

**The float doors, picked per field and per the two shipped answers.** `_heightDp`, `_cellSizeDp`, `_gapDp` and `_marginDp` are **layout** and take `PausePresenter.Place`'s and `SkillBarPresenter.Place`'s answer — *leave the authored layout alone* — because all of them are authored on `Hud.prefab` at a real size in a real corner, unlike `TreeViewPresenter`'s runtime clones. Within that: **a gap of 0 is legal and honoured** (twelve cells touching is a layout) and **a margin of 0 is legal**, while **a cell size of 0 and a strip height of 0 are refused**, because a readout 0 dp across is GD §16.1's always-there element silently absent rather than a layout anyone asked for. `_secondsShown` is a **duration** and takes `FirstActiveHint.Dwell`'s answer — a constant — because a toast that never hides is worse than one that hides at the default, and a duration has no authored rect to fall back to.

**Rule 12's seam is `FirstActiveHint`'s, copied deliberately.** `Update` runs `Tick(Time.unscaledDeltaTime)` and `Tick(float)` is separate so a fixture can drive the dwell by hand. `Overflow_ToastSurvivesAPause` then does what the seam alone cannot: it walks the IL of `OverflowToast` and asserts it reads `Time.unscaledDeltaTime` and **never** `Time.deltaTime`, because driving `Tick` by hand proves the split rather than the clock and in EditMode neither clock advances at all. That is `SkillBarPresenterTests.Calls`' technique narrowed to one declaring type — a first copy, promoted on its own count.

**Two rows this *As built* adds by name.** `Row_IsPlacedInDp` — the per-cell dp layout and the assertion that the row does not overlap the health bar, which is rule 7's own arithmetic and nothing else checks. And `Level_IsPlacedInTheHudRow` checks the **whole** HP row rather than only the rect this task added, because `Place()` has laid that row out untested since M1-17 and this is the cheapest moment there will ever be to pin it.

**Twelve authored cells, not a cloned template.** Rule 7's *"pre-made and deactivated"* and `Prefab_IsDressed`'s *"twelve row cells present and wired"* both force it, so the prefab carries 24 serialized `Image` references (`_fills` + `_kindStrips`). That is the opposite of `SkillsPresenter`, `TreeViewPresenter` and `LevelUpPresenter`, and the reason is that twelve is fixed and knowable where twenty-seven tree nodes are not. `Row_HoldsTwelve` asserts the child count and the `Image` count are unchanged after five frames, so *"none instantiated after Start"* holds.

**Three readouts that no run in this build can reach, and one that every run can.** The strip and the level number are live for every player today — a kill moves the strip and a threshold moves the number. The row and the toast need an Active and a full tree respectively, so both need M3-12: every run in this build draws twelve deactivated cells and never toasts.

**Verified:** 1 609 EditMode / 0 / 0, twice consecutively on the final code, against M3-10a's 1 577 — **32 new rows**, which is the Tests table's 26 plus 5 implied guards plus the 1 named above. PlayMode **16 / 16** with `Ticker_RunsTheStepsInOrder` green; no `InitTestScene` left behind. Six assemblies, zero compile errors, zero analyzer warnings; the compile confirmed by *calling* the new API from a `RunCommand` (Traps §3). All seven touched `.cs` files confirmed through `GetAssemblyNameFromScriptPath` — `Soulvail.Game` ×5, `Soulvail.Tests.Game` ×2, **two assemblies and no ripple**. Both assets read back off disk after the save (Traps §5). `Hud.prefab`'s diff is +3 606 / −1 079 and the deletions are a re-serialisation: the multiset of `m_Name:` values differs by **64 added and 0 removed**. `ProjectSettings/TimeManager.asset` turned up dirty again and was reverted — seven of the last eight tasks.
