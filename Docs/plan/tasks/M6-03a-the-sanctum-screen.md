# M6-03a — The Sanctum screen, and a price that cannot be paid

**Size:** M · **Depends on:** M6-02b · **Branch:** `m6-03a-sanctum-screen`
**Design refs:** GD §7.1, §7.3, §11.4, §13.3, §16.1, §16.4; CH §5.1; AR §7, §8, §18.1, §18.2 · **Ledger rows:** [1](../ROADMAP.md#carry-forward-into-m6) — one device row, listed and not answered; [3](../ROADMAP.md#carry-forward-into-m6) — touched and not moved; [7](../ROADMAP.md#carry-forward-into-m6) — fourteen of M6's strings land here

## Goal

GD §13.3's four services are four rows a thumb can hit, three of them can be refused, a refusal reads
as a price rather than as a broken button, and the run stops while the player reads them.

## Why this is M6-03a and not M6-03

**Counted rather than felt.** GD §16.1 puts **two** readouts on the HUD — *Essence, top-right, small
counter* and *Veilrot meter, right screen edge, vertical* — and neither is on this screen. Written as
one task, M6-03 is `SanctumPresenter`, `ServiceRow`, `BanishPicker`, `Sanctum.prefab`,
`SanctumPresenterTests`, `VeilrotMeterView`, `VeilrotMeterViewTests` and a substantial `HudPresenter`
— **eight**, against the ROADMAP's five. The seam is the one M6-00a used twice already: **a screen the
player acts on** against **a readout that only reports**. [M6-03b](M6-03b-the-meter-on-the-right-edge.md)
takes the two meters and the fourth node state.

## What GD §13.3 sells, and what this screen can draw

| Service | Price | Drawn as | Refusable |
|---|---|---|---|
| **Reroll** | 25, doubling | one row, price recomputed each draw | only by being short |
| **Banish** | 40 | one row that opens a list of untaken nodes (rule 7) | short, or nothing banishable |
| **Heal** | 40 | one row | short, or already full |
| **Cleanse** | 60 | one row | short, or already clean |

Every refusal above is `SanctumShop.CanBuy`'s, already built and already tested at
[M6-02b](M6-02b-four-things-essence-buys.md) rule 7. **Nothing on this screen decides whether a
purchase is legal.** What this task adds is that the answer is *drawn* before the player commits,
which is [M5-08a](M5-08a-splash-offers-what-install-refuses.md)'s finding in as many words: *a screen
may not offer what the model refuses.*

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/SanctumPresenter.cs` | Game | The screen: one event opens it, four rows, one command closes it |
| `Game/Controls/ServiceRow.cs` | Game | One priced row — name, price, and either what it does or why it cannot |
| `Game/Controls/BanishPicker.cs` | Game | The second page: every node Banish may take, one tappable row each (rule 7) |
| `Prefabs/UI/Sanctum.prefab` | — | The screen, dressed. `Splash.prefab`'s two-page shape |
| `Tests/Game/Presentation/SanctumPresenterTests.cs` | Tests.Game | The four rows, the three refusals, the picker, and the pause |
| *small edits* | Game | `Game/Composition/RunPause.cs` — `PauseReason.Sanctum` (rule 4); `Game/Composition/RunTicker.cs` — `SanctumPhase` (rule 5); `Game/Composition/RunScope.cs` — the field and its optional registration; `Scenes/Run.unity` — the prefab dressed in; `Data/Localisation/English.asset` — fourteen rows; `Game/Presentation/Palette.cs` — `Essence`'s summary corrected (rule 10) |
| *ripple* | Tests.Game | `PaletteTests` — rules 10 and 11; `RunTickerTests` — one more phase; `RunScopeTests` — one more optional component |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Game/Composition/RunPause.cs — a fourth member. Block namespace does not apply: this enum is in a
// file-scoped namespace already and derives from nothing (Palette's split, Traps §5).
public enum PauseReason
{
    LevelUp,
    Menu,
    Splash,

    /// <summary>
    /// GD §13.3's shop is up. Untimed, so the run is gated rather than clocked slowly — rule 4.
    /// </summary>
    Sanctum,
}
```

```csharp
// Game/Controls/ServiceRow.cs — block namespace (Traps §5).
namespace Soulvail.Game.Controls
{
    /// <summary>One of GD §13.3's four services, priced, and honest about whether it can be had.</summary>
    public sealed class ServiceRow : MonoBehaviour
    {
        /// <summary>Which service this row is. Reported on a tap — <c>OfferCard</c>'s index, named.</summary>
        public SanctumService Service { get; }

        /// <summary>Whether the row is currently drawn.</summary>
        public bool IsShown { get; }

        /// <summary>Whether its button is live — <c>SanctumShop.CanBuy</c> and nothing else (rule 3).</summary>
        public bool IsAffordable { get; }

        /// <param name="price">What it costs right now — <c>SanctumShop.PriceOf</c>.</param>
        /// <param name="canBuy">Whether it may be bought — <c>SanctumShop.CanBuy</c>.</param>
        /// <param name="detail">
        /// What it does, or why it cannot be had. One key either way — rule 3.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="localizer"/> or <paramref name="onTapped"/> is null.</exception>
        public void Show(
            SanctumService service, int price, bool canBuy, LocKey detail,
            ILocalizer localizer, Action<SanctumService> onTapped);

        public void Hide();
    }
}
```

```csharp
// Game/Controls/BanishPicker.cs — block namespace (Traps §5).
namespace Soulvail.Game.Controls
{
    /// <summary>
    /// GD §13.3's <em>"remove a node from this run's offer pool"</em>, as a list of what is left.
    /// </summary>
    public sealed class BanishPicker : MonoBehaviour
    {
        /// <summary>How many rows this prefab authors — the bound rule 7 is written against.</summary>
        public int Capacity { get; }

        public bool IsOpen { get; }

        /// <summary>
        /// Draws <paramref name="count"/> of <paramref name="candidates"/> and reports a tap.
        /// Rows past <paramref name="count"/> are hidden, never drawn empty.
        /// </summary>
        /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
        public void Open(
            IReadOnlyList<ContentId> candidates, int count, ContentCatalog catalog,
            ILocalizer localizer, Action<ContentId> onPicked, Action onCancelled);

        public void Close();
    }
}
```

```csharp
// Game/Presentation/SanctumPresenter.cs — block namespace (Traps §5).
namespace Soulvail.Game.Presentation
{
    public sealed class SanctumPresenter : MonoBehaviour
    {
        /// <summary>Whether the screen is up. The read <c>RunTicker</c> does **not** use — rule 4.</summary>
        public bool IsShown { get; }

        /// <summary>Whether the Banish list is over the four rows.</summary>
        public bool IsPicking { get; }

        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        [Inject]
        public void Construct(
            IRunSession session,
            IProgressionCommands progression,
            DomainEventHub hub,
            ContentCatalog catalog,
            ILocalizer localizer);
    }
}
```

## Behaviour

1. **It renders one event, reads the run, and owns no economy.** `SanctumOpened` draws and shows;
   `IProgressionCommands.LeaveSanctum` hides. Nothing here prices a service, decides whether one may
   be bought, or knows what Cleanse does — `SanctumShop` does all three through
   `IProgressionCommands.PriceOf`, `CanBuy`, `Buy`, `BanishableInto` and `Banish`
   ([M6-02b](M6-02b-four-things-essence-buys.md) rule 2), and the balance comes off
   `RunState.Essence`. `LevelUpPresenter`'s bargain, one screen over, and the reason a disagreement
   between this screen and the game can only be a missed redraw in this file.
2. **It redraws on open and after every tap, and on nothing else — and rule 4 is what makes that
   correct.** Essence, hit points and Veilrot are the three numbers every row's price and refusal are
   computed from, and while the pause is held **nothing else in the build can move any of them**: the
   tick is gated, so no drop, no drain and no damage lands. So a `Update`-driven poll would be sixty
   recomputations a second of an answer that changes only when this file changes it.
   `SanctumOpened.Essence` is used for the first frame and `RunState.Essence` for every frame after,
   which is why the event carries it at all (M6-02a's `SanctumOpened` remarks).
3. **A row that cannot be bought is drawn, priced and dead, and says why in the place its effect text
   sits.** `SplashPresenter.DrawBranches`' shape exactly, including the reason it has that shape
   (M5-08a rule 5): the row keeps its name and its price, because reading what you cannot have is
   half of what a shop is for, and the *detail* line is the one that changes. Four refusal keys —
   `ui.sanctum.refused.short`, `.full`, `.clean`, `.nothing` — and the choice between them is
   `CanBuy` false plus one read: the balance decides *short*, and otherwise the service names its
   own. **`Buy` still throws** (M6-02b rule 7), and `OnRowTapped` re-asks `CanBuy` before sending,
   which is M5-08a rule 4's second door: unreachable through the UI, and written because the
   alternative is an `InvalidOperationException` out of a `Button.onClick`.
4. **The screen raises `PauseReason.Sanctum`, and this is M6-03a's ruling — core deliberately
   declined it** ([M6-02a](M6-02a-the-sixth-phase.md) rule 3). Three reasons and one stated cost:
   - **An untimed room that keeps ticking is a room that pays you to wait.** GD §13.3 says *untimed*
     and GD §7.3 calls a Sanctum *"the natural put-the-phone-down point"*. Unpaused, every cooldown
     in the build recovers for free, `RunState.Time` accumulates without bound, and the honest way to
     play stage 26 is to stand in stage 25's shop for two minutes first.
   - **`Screen.sleepTimeout` is `NeverSleep` for the whole run** (`RunTicker.Start`), so an unpaused
     Sanctum holds a phone awake at 60 fps on a screen its owner may have walked away from.
     `RunPause.PausedFrameRate` is 30 and GD §11.4 asks for exactly that.
   - **`Time.timeScale` 0 stops Animators and particles with the simulation** (`RunPause.Pause`'s own
     remarks), which a gated tick alone does not.
   - **The cost, recorded rather than discovered:** M6-02a rule 3's parenthetical *"cooldowns
     recover"* becomes false in the shipped game, because a gated frame advances no clock. That is
     the correct direction — see the first reason — and M6-02a's promise that *"nothing spawns,
     nothing is owed and nothing expires"* is unaffected, because the phase has no timeout to expire.
5. **`RunTicker` raises it, not this class, and `SanctumPhase` is a method of its own below
   `LevelUpPhase`.** M3-08b's ruling and its reason, unchanged: the gate is a once-a-frame pure
   function of `IProgressionCommands.IsSanctumOpen`, so a presenter that is absent, destroyed or
   never dressed costs a missing screen **loudly** rather than a run that ticks on with a shop
   nobody can close. `RunPause` is deliberately not a dependency of this class and a test pins its
   absence, `LevelUpPresenterTests`' row. The phase releases before it acquires and acquires only on
   `!_pause.IsPaused`, `LevelUpPhase`'s body. **Level-up wins a collision**, which is GD §13's own
   sentence — *"Level-up is progression. The Sanctum is economy. They never overlap"* — and
   **grepped, the collision is unreachable**: `Clear` parks for `ClearTime` of *ticked* seconds and a
   pending level-up gates the tick, so the flow cannot reach `Sanctum` with an offer open. The guard
   is written anyway for `RunPause.Pause`'s reason: it throws for a second holder, and a throw inside
   the frame loop turns a screen collision into a dead run.
6. **Every command goes straight down the port and never through `CommandPhase`.**
   `RunTicker.Tick` returns above `CommandPhase` while the pause is held, so a queued command would
   never arrive at all — `LevelUpPresenter.OnCardChosen` calls `_progression.ChooseOffer` directly
   for exactly this reason, and `SplashPresenter.OnBranchChosen` does the same. `Buy`, `Banish` and
   `LeaveSanctum` are three more of those.
7. **Banish is two taps, because it takes an argument and the other three do not.** `Buy(Banish)`
   throws by design (M6-02b rule 5), so the row opens `BanishPicker` over the four rows —
   `SplashPresenter`'s two-page shape, and `LevelUpPresenter.OpenTree`'s veil-and-return for the way
   back. The list is `BanishableInto`'s answer in tree order, drawn into rows authored on the prefab
   the way `SplashPresenter._branchButtons` are. **The prefab authors `TreeRules.Count` rows for the
   trees this build ships — twelve** — and a candidate list longer than `Capacity` draws the first
   `Capacity` and no more, which is a real limit rather than a bug: CH §5's full tree is 27 and
   **M7-04** is the task that authors it. `BanishPicker_RefusesNothingAndDrawsWhatItCan` is the row
   that says so, so the day 27 arrives it fails rather than silently hiding fifteen nodes.
8. **`TreeNodeView` is not reused for the picker's rows, and that is rule 1 of M3-09d honoured rather
   than worked around.** That class carries no `Button` on purpose — *"a tree you could tap to buy
   from would be the screen the design refused arriving through the back door"* (CH §5.1) — so
   putting a tap on it to build a banish list would delete the structural half of a design decision
   for the convenience of one screen. The picker's rows are a name and a button and nothing else.
9. **The screen is full-screen and takes the touches**, so the stick and the skill buttons are
   covered rather than disabled — M3-08a rule 14, and with the tick gated they move nobody anyway.
   No fade in or out, `LevelUpPresenter`'s rule and its reason: `Time.timeScale` is 0 while this is
   up, so a scaled tween would freeze half-way and an unscaled one is a second clock.
10. **`Palette.Essence`'s summary is corrected here, because this task makes it wrong twice over.**
    It reads *"No reader yet (M6)"* and has had a reader since [M4-06](M4-06-run-end-screen.md) —
    `RunEndPresenter.Draw` writes `_shards.color = Palette.Essence`, and that method's own remarks
    say *"`Palette.Essence`'s first reader"* in as many words. Three places, two of them wrong, and
    nothing noticed because `PaletteTests.Readers` is a hand-kept list of nine types that has never
    included `RunEndPresenter`. This screen is the second reader — GD §16.4's warm gold on the
    balance and on every price — so the summary becomes *"read by the run-end payout and the Sanctum's
    prices"* and the [parking-lot line](../ROADMAP.md#parking-lot) that named M6-03 as its promoter
    closes.
11. **`Palette_HasTheColoursNobodyReadsYet` is narrowed by being made a sweep, which is the fix the
    parking-lot line asks for rather than a second hand-kept entry.** `PaletteTests.Reads(Type,
    FieldInfo)` already walks IL for `ldsfld`/`ldsflda`, so the row's candidate set becomes **every
    type in `typeof(Palette).Assembly`** instead of the nine, and its claim becomes about
    `Palette.Veilrot` **alone** — which is the one that is still true, and which
    [M6-03b](M6-03b-the-meter-on-the-right-edge.md) retires by giving it a meter. `Readers` stays
    exactly as it is: it is `Views_CarryNoSerializedColour`'s list, a statement about nine specific
    files, and narrowing one row must not quietly widen another.
12. **Nothing here allocates per frame**, because nothing here runs per frame: `Update` clears the
    one-tap latch and does nothing else (`LevelUpPresenter._choosing`'s rule, for its reason — uGUI
    dispatches both taps of a double tap from one `EventSystem` pass).

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Sanctum_OpensOnTheEvent` | a dressed screen / `SanctumOpened(3, 84)` / `IsShown`, four rows drawn, balance 84 — rule 1 |
| `Sanctum_DrawsTheFourPrices` | 500 banked, one reroll bought / opened / 50, 40, 40, 60 — `PriceOf`'s answers, not this file's |
| `Sanctum_ARowThatCannotBeAffordedIsDrawnAndDead` | 39 banked / opened / all four drawn with their prices, all four non-interactable, each detail line `ui.sanctum.refused.short` — rule 3 |
| `Sanctum_HealAtFullHealthSaysWhy` | full health, 500 banked / opened / the Heal row is dead and reads `ui.sanctum.refused.full`, and the other three are live — rule 3 |
| `Sanctum_CleanseAtZeroRotSaysWhy` | 0 Veilrot, 500 banked / opened / dead, `ui.sanctum.refused.clean` |
| `Sanctum_BanishWithNothingLeftSaysWhy` | a tree with every node taken / opened / dead, `ui.sanctum.refused.nothing` |
| `Sanctum_ARefusalKeepsTheNameAndThePrice` | Heal refused / — / the name and `40` are still drawn — rule 3, M5-08a rule 5's shape |
| `Sanctum_BuyingRedrawsEveryRow` | 65 banked / tap Heal / balance 25, the Heal row's price unchanged, **Cleanse now dead** as short — rule 2 |
| `Sanctum_ARerollRedrawsItsOwnPrice` | 100 banked / tap Reroll / the row reads 50 — rule 2 |
| `Sanctum_ATapOnADeadRowSendsNothing` | Heal refused / `OnRowTapped(Heal)` by hand / no command, no throw — rule 3's second door |
| `Sanctum_OneTapPerFrame` | two taps in one `EventSystem` pass / — / one `Buy` — rule 12 |
| `Sanctum_LeavingClosesIt` | open / tap Leave / `LeaveSanctum` sent once, `IsShown` false |
| `Sanctum_CommandsBypassTheCommandPhase` | the pause held / every tap on the screen / each arrives at `IProgressionCommands` on the frame it was made, with `RunTicker.Tick` returning above `CommandPhase` — rule 6 |
| `Sanctum_CoversTheStickAndTheButtons` | shown / — / the canvas is full-screen and `blocksRaycasts`, and nothing under it is disabled; hidden / alpha 0 and no raycasts, no tween in either direction — rule 9 |
| `Banish_OpensTheList` | nine banishable / tap Banish / `IsPicking`, nine rows with their names, the four service rows veiled — rule 7 |
| `Banish_PickingSpends` | the list open / tap a row / `Banish(id)` sent with **that** id, list closed, four rows redrawn, balance down 40 |
| `Banish_CancelSpendsNothing` | the list open / cancel / no command, the four rows back, balance unmoved |
| `Banish_DrawsAtMostItsCapacity` | a candidate list longer than `Capacity` / opened / `Capacity` rows, no throw, and the row's message names M7-04 — rule 7 |
| `Banish_RowsPastTheCountAreHidden` | three banishable, twelve rows authored / opened / three shown and nine hidden, never drawn empty |
| `Picker_IsNotATreeCell` | `typeof(BanishPicker)` / reflection / it does not reference `TreeNodeView`, and `TreeNodeView` still carries no `Button` — rule 8 |
| `Pause_TheSanctumHoldsIt` | a run entering `Sanctum` / one `RunTicker` frame / `RunPause.Holder` is `PauseReason.Sanctum`, `Time.timeScale` 0, `Application.targetFrameRate` 30 — rule 4 |
| `Pause_ItIsGivenBackOnLeaving` | held / `LeaveSanctum`, one frame / holder null, both globals back to what they were |
| `Pause_ThePresenterCannotReachIt` | `typeof(SanctumPresenter)` / reflection over fields and `Construct`'s parameters / no `RunPause` — rule 5 |
| `Pause_ALevelUpIsNeverStampedOn` | the pause held by `LevelUp` / a frame in which `IsSanctumOpen` is true / the holder is **still** `LevelUp` and nothing throws — rule 5 |
| `Pause_TheOrderIsLevelUpThenSanctum` | `RunTicker.Tick`'s body / reflection or an instrumented fake / `SanctumPhase` runs below `LevelUpPhase` — rule 5 |
| `Ticker_TheSanctumIsGatedNotClocked` | in the Sanctum / 120 frames / `RunState.Time` is unmoved and no cooldown recovered — rule 4's stated cost, asserted rather than left implied |
| `Prefab_IsDressed` | `Sanctum.prefab` / loaded / root, four `ServiceRow`s one per `SanctumService`, a `BanishPicker`, a Leave button and a balance label — `LevelUpPresenterTests`' row |
| `Prefab_CarriesNoSerializedColour` | every type on the prefab / reflection / none — `Views_CarryNoSerializedColour`'s rule for a new screen |
| `Strings_EveryKeyThisScreenDrawsHasARow` | the fourteen keys / `English.asset` / every one resolves — [ledger row 7](../ROADMAP.md#carry-forward-into-m6) |
| `Palette_EssenceHasTwoReadersAndItsSummarySaysSo` | `Palette.Essence` / the sweep / `RunEndPresenter` **and** `SanctumPresenter` read it, and the summary no longer says *"no reader yet"* — rule 10 |
| `Palette_HasTheColourNobodyReadsYet` | *(rewritten)* `Palette.Veilrot` / a sweep of **every** type in `Soulvail.Game` / no reader — rule 11 |
| `Palette_TheSweepWouldHaveCaughtM4_06` | `Palette.Essence` against the sweep with `SanctumPresenter` excluded / — / it finds `RunEndPresenter`, which the nine-type list could not — rule 11, the parking-lot line's own diagnosis asserted |

**Guard rows are implied, not listed:** nulls to `Construct`, `Show` and `Open`; a missing root,
row or label refused by name in `Start` (`LevelUpPresenter.Start`'s three throws); a screen in a
scene with no run returning rather than throwing.

## Manual verification (Editor / device)

1. **[Editor]** Clear stage 1 with 24 Essence. The Sanctum opens, all four rows are drawn with their
   prices, and all four are dead — the balance is short of every one of them. The fight underneath is
   frozen and the frame rate reads 30.
2. **[Editor]** Clear three more stages, take a hit, then buy a Heal and a Reroll. The balance falls
   twice, the Reroll's price becomes 50 the moment it is bought, and Cleanse goes dead when the
   balance drops under 60 — without anything being tapped a second time.
3. **[Editor]** Tap Banish, pick a node, and play four more level-ups. It never appears. The tree
   screen still draws it as *Locked* until
   [M6-03b](M6-03b-the-meter-on-the-right-edge.md) gives it a state of its own.
4. **[Editor]** Stand in the Sanctum for two minutes. The debug overlay's run clock does not move and
   the Charge cooldown does not recover — rule 4's stated cost, looked at.
5. **[device]** **[ledger row 1](../ROADMAP.md#carry-forward-into-m6)**: four priced rows at thumb
   distance, where three of the four can be refused and the refusal has to read as a price rather
   than a broken button. Not answerable in an Editor whose `Screen.dpi` reads 120
   ([Traps §9](../../Traps.md)).

## Out of scope

- **The HUD's Essence counter and Veilrot meter** (GD §16.1). [M6-03b](M6-03b-the-meter-on-the-right-edge.md).
- **`NodeState.Banished`.** [M6-03b](M6-03b-the-meter-on-the-right-edge.md), with the meters: it is a
  readout of a state a merged mechanic created, which is that task's whole subject. `TreeNodeView`'s
  own remarks still say *"M6-02's Banish"* and that task is named there for the last time.
- **A Sanctum *room*.** M6-02a's *Out of scope*, unchanged: this screen opens over the arena the
  player just cleared.
- **Any change to what a service costs or whether it may be bought.** M6-02b owns all six numbers and
  both refusals; this file asks and draws.
- **Scrolling the banish list.** Rule 7's `Capacity` is twelve and this build's trees are twelve.
  **M7-04**.
- **`Palette.Veilrot`'s reader.** Rule 11 narrows the row on purpose so
  [M6-03b](M6-03b-the-meter-on-the-right-edge.md) can retire it; giving it a reader here would mean
  editing the same row twice for two different reasons.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
