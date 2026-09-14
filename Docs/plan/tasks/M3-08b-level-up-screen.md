# M3-08b — The level-up screen: three cards, a tap, and the frame that starts again

**Size:** M · **Depends on:** M3-08a · **Branch:** `m3-08b-level-up-screen`
**Design refs:** GD §5.2, §7.3, §13.1, §16.1, §16.4; CH §5.1; CC §6.2; AR §7, §8, §18.1, §18.2; ADR-0004, ADR-0012 · **Ledger rows:** 4 (whether a mid-fight interruption reads well on a phone is device-only), 6 (four more colour constants with no `Palette` file — row 6's owner inherits a fourth reader, rule 8), 9 — ~~opened by this task~~ **opened by M3-00b (`ae7b853`), recounted at M3-00d, owned by M3-15; this task makes it *visible* rather than opening it** (a card draws a `LocKey`, because nothing resolves one yet — rule 7)

## Goal

GD §13.1's progression moment, on screen: the game is already stopped, three nodes are already drawn, and this turns them into three things a thumb can hit — then gets out of the way and lets the frame start again.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/LevelUpPresenter.cs` | Game | the screen: hears `OfferPresented`, ~~holds the pause~~ **renders a gate `RunTicker` holds** (rule 3, as rewritten), sends `ChooseOffer`, hides on `LevelUpClosed` — **block namespace** (Traps §5) |
| `Game/Controls/OfferCard.cs` | Game | one card: its index, its two keys, its kind tint, its tap — **block namespace** |
| `Tests/Game/Presentation/LevelUpPresenterTests.cs` | Tests.Game | over a real `RunSession` and hub, `ThreatArrowsTests`' shape |
| `Prefabs/UI/LevelUp.prefab` | — | the screen. An asset, not a code file — listed, not counted ([sizing rule](../ROADMAP.md#how-to-read-this)) |
| *small edits* | | `Game/Composition/RunScope.cs` + `_levelUpPresenter` and its `RegisterComponent`, `_hudPresenter`'s shape; `Scenes/Run.unity` dressed with the prefab; `InstallerTests` if the field is required |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Game.Presentation
{
    public sealed class LevelUpPresenter : MonoBehaviour
    {
        // [SerializeField] CanvasGroup _root; OfferCard[] _cards (3); TMP_Text _levelLabel, _pickLabel;
        // No RunPause: the gate is RunTicker's (rule 3, as rewritten). Its absence is pinned by
        // Construct_TakesNoRunPause.
        [Inject] public void Construct(IRunSession session, IProgressionCommands progression,
                                       DomainEventHub hub, ContentCatalog catalog);
    }
}

namespace Soulvail.Game.Controls
{
    public sealed class OfferCard : MonoBehaviour
    {
        // [SerializeField] TMP_Text _name, _description; Image _kindStrip; Button _button;
        // [SerializeField] Color _passive, _active, _upgrade, _keystone;   // rule 8

        public void Show(int index, SkillSpec spec, Action<int> onChosen);
        public void Hide();
        public void SetInteractable(bool value);
    }
}
```

## Behaviour

1. **It renders two events and owns no progression.** `OfferPresented` shows or redraws; `LevelUpClosed` hides. Nothing here counts a pick, decides what is available or knows what a node does — `RunState.Offer` carries the ids and `ContentCatalog.Skill(id)` carries the rest, which is `HudPresenter`'s bargain and the reason a disagreement between screen and game can only be a missed event in this file.
2. **It subscribes in `Construct`, not `OnEnable`** — `HudPresenter`'s reason: `RunScope` builds its container from its own `Awake` and Unity orders no two of those, so an `OnEnable` subscription reaches for a hub that may not exist. Dropped in `OnDestroy`.
3. ~~**It holds both halves of the pause.**~~ **Rewritten at build time on the owner's ruling — it holds neither half.** The gate is `RunTicker.LevelUpPhase`'s: it raises `PauseReason.LevelUp` when `HasOffer` goes true and lowers it when it goes false (M3-08a rule 12), and this screen **renders a gate it does not own**. The original rule's reason — *a pause raised in one class and lowered in another is how a screen ends up closed over a frozen game* — argues against **split** ownership, and the ticker holds both halves eight lines apart, so the reason survives and only the nomination of which file changed. What it buys: the run being stopped is a once-a-frame pure function of core state rather than a consequence of a view having received an event, so a presenter that is absent, destroyed or never dressed costs a missing screen rather than a run that ticks on with an offer nobody can spend. `RunPause` is **not** a dependency of this class and a test pins its absence. The cost, measured: the presenter's version would have turned four existing `Frame_*` rows red; this one turns none. See *As built* deviations 1–3.
4. **No fade, in or out.** `Time.timeScale` is 0 while the screen is up (M3-08a rule 13), so any scaled animation would freeze mid-way and an unscaled one is a second clock in a file whose whole job is to be brief — GD §13.1 budgets *"a couple of seconds"* for the entire interruption. M2-10's 0.3 s stage fade is the precedent for a transition the player is not deciding anything during; this is the opposite case.
5. **The buttons go dead the moment one is tapped**, and come back only when the next `OfferPresented` arrives. `ChooseOffer` runs synchronously and may immediately redraw for a second pick (M3-08a rule 6), so a double-tap on a card that has not yet been repainted would spend the second pick on whatever now sits at that index — a node the player never saw. One flag, set before the command and cleared by the event.
6. **Only `Count` cards are shown.** M3-04 rule 1 writes fewer than three when the tree is nearly exhausted, and the rest are hidden rather than drawn empty — CC §6.2's *"unused slots are not drawn"*, applied to the other screen that has a variable number of things on it.
7. **A card draws the `LocKey`, because nothing resolves one.** `ILocalizer` is an AR §6 port with no implementation (`TableLocalizer` is M6-10's), so `spec.NameKey.Key` and `spec.DescriptionKey.Key` are what appears — `skill.oathbound.consecrate`, not "Consecrate". This is **not** a third place raw English is typed into a prefab (`MenuPresenter` and `HudPresenter` are the first two and remain the last): nothing English is typed at all, the key is data, and ADR-0012's whole point is that the key exists from the first node. **It does mean GD §13.1's *"readable in under two seconds"* cannot be judged until M6-10** — the new ledger row 9, owned by M3-15, which has to decide whether to accept the milestone on keys or pull a minimal localizer forward.
8. **Four kind tints, serialized on the prefab, and deliberately not a palette.** GD §16.4's palette still has no file (ledger row 6, owner M3-13), and inventing `Palette` here would be M3-13 arriving early and unspecified. Four `Color` fields on `OfferCard` are honest about being placeholders and are one more caller for row 6 to absorb — said here so the row's scope is known before it is worked rather than after. CH §4's four kinds are what a player is distinguishing at a glance: a Keystone is not a Passive and the card has two seconds to say so.
9. **The screen is its own prefab and its own canvas**, above the HUD's sort order, with a scrim dark enough that the fight behind it is legible but not readable. Not a child of `Hud.prefab`, which already holds the death overlay: two screens in one prefab is how the third one (M3-09's) ends up there too, and a canvas that can be switched off whole is what makes rule 4's instant show cheap. `SafeAreaFitter` on the content root, like every other screen.
10. **Layout, in dp, as serialized fields so the owner can tune on device:** three cards of **200 × 260**, **20** apart, centred; the header two lines — *"Level N"* from `RunState.Level` and *"Pick i of n"* from `OfferPresented.PicksOwed`. The second line is what makes a double level-up legible (M3-08a rule 3) rather than a card that surprises the player by reappearing. Every one of these is a guess until a phone exists (ledger row 4).
11. **It takes no touches away from the game**, because the game is not running: the canvas is full-screen and the tick is gated, so the stick and the Charge button are inert underneath it rather than needing to be disabled (M3-08a rule 14).
12. **`OnDestroy` drops the subscriptions and nothing else.** A scope torn down with the screen up leaves the pause held, and `RunPause.Dispose` restores the globals unconditionally (M3-08a rule 13) — VContainer orders no two disposals, so this file must not depend on being the one that resumes.

## Tests

| Test | Given / When / Then |
|---|---|
| `Offer_ShowsThreeCards` | a run, an offer of 3 / `OfferPresented` / the root is on, three cards active, each naming its spec's key (rules 1, 7) |
| `Offer_ShowsFewerWhenScarce` | an offer of 2 / — / two cards active, the third hidden (rule 6) |
| `Offer_PausesTheRun` | idle, an offer / `OfferPresented` / `RunPause.Holder` is `LevelUp` (rule 3) |
| `Offer_RedrawDoesNotRePause` | two picks owed / choose the first / the second `OfferPresented` repaints, the pause is still held once, no throw (rule 3) |
| `Tap_SendsChooseOfferWithTheIndex` | three cards / tap the middle / `ChooseOffer(1)` (rule 1) |
| `Tap_DisablesEveryCardImmediately` | three cards / tap one / all three non-interactable **before** the command returns (rule 5) |
| `Tap_ReEnablesOnTheNextOffer` | two picks / tap / the redrawn cards are interactable again (rule 5) |
| `Tap_TwiceSpendsOnePick` | two picks owed, a scripted double tap in one frame / — / exactly one `ChooseOffer`, `PendingLevelUps` down by one (rule 5) |
| `Closed_HidesAndResumes` | the screen up, one pick / choose it / root off, `RunPause.IsPaused` false, `Time.timeScale` 1 (rules 1, 3) |
| `Closed_LeavesTheRunTicking` | the row above / the next frame / `RunState.Time` advances again (rule 3) |
| `Header_ShowsLevelAndPickCount` | level 7, two picks owed / `OfferPresented` / *"Level 7"* and *"Pick 1 of 2"*; after the first choice, *"Pick 2 of 2"* (rule 10) |
| `Offer_ShowsInstantly` | the root hidden / `OfferPresented` / alpha 1 on the same call, and the component carries no `Animator` and starts no coroutine — nothing scaled to freeze (rule 4) |
| `Card_DrawsTheKeyNotEnglish` | a spec with `skill.oathbound.consecrate` / `Show` / that exact string — the row that makes ledger row 9 visible rather than a surprise at M3-15 (rule 7) |
| `Card_TintsByKind` | one of each kind / `Show` / four different strip colours, from the serialized fields (rule 8) |
| `Overflow_ShowsNothing` | a full tree, a pick owed / `OpenLevelUp` / `OverflowGranted` published, the root stays off, nothing pauses (M3-08a rule 7 from this side) |
| `NoTree_ShowsNothing` | a class with no tree, picks owed / several frames / the root stays off, the run keeps ticking (M3-08a rule 5) |
| `Subscribes_FromConstruct` | a presenter constructed after `RunStarted` / publish `OfferPresented` / it renders — an `OnEnable` subscription would have missed the hub (rule 2) |
| `Destroy_DropsSubscriptions` | a constructed presenter / `OnDestroy`, then publish / nothing throws, nothing renders (rule 12) |
| `Destroy_WhilePaused_LeavesTheAppRunnable` | the screen up / destroy the presenter, dispose the scope / `Time.timeScale` 1 (rule 12) |
| `Prefab_IsDressed` | `LevelUp.prefab` / read the fields back after a save (Traps §5) / root, three cards, both labels present, each card's button and texts assigned, and its own `Canvas` sorting above `Hud.prefab`'s (rule 9) |
| `Touches_AreNotTakenFromTheGame` | — / — / **no row**: the stick and the Charge are inert because the tick is gated, which is M3-08a rule 14's `Frame_LevelUpPhaseRunsAboveCommands`. Asserting it again here would test that file from this one (rule 11) |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend with a hand-authored tree. Level once. The game stops dead and three cards appear over it; the header reads *"Level 2 · Pick 1 of 1"*. Tap one. The card disappears, the fight resumes on the next frame, and `DebugOverlay` shows the node taken.
2. **[Editor]** Kill a Bloater at stage 1 with enough XP banked to cross two thresholds (M3-01a rule 4). One screen appears, the header reads *"Pick 1 of 2"*, and the second card set is drawn over the **new** tree state — the node just taken is gone from the pool and anything it unlocked may appear.
3. **[Editor]** Take the last node of a hand-authored four-node tree, then level again. **No screen** — the overlay shows an Overflow level and the game never stops (M3-08a rule 7).
4. **[Editor]** Level up, then Stop Play with the screen open. Press Play again: the Editor's timeline is running and the Menu is not frozen (rule 12).
5. **[device]** Ledger row 4, and this is the largest single question in the milestone: **does a hard stop mid-fight read as a beat or as a stutter?** GD §13.1 wants the whole interruption inside a couple of seconds; the 30 fps drop, the zero `timeScale` and the instant show are all unmeasured. Deferred with the rest.
6. **[device]** Whether a 200 × 260 dp card is comfortably tappable with the same thumb that was on the stick a moment ago, and whether the three of them fit the safe area on a notched landscape phone. Deferred.

## Out of scope

- **The View Tree toggle** (CH §5.1, GD §13.1) — **M3-09**, which builds the tree view for pause and adds the button here. Until then the screen is three cards and a header, and that is a named gap rather than a forgotten line.
- **The Skills screen, the pause menu, the first-active hint** — M3-09.
- **An Overflow toast** — M3-10 or M3-13, from `OverflowGranted`.
- **A `Palette` file** — M3-13, ledger row 6. Rule 8 adds a reader to it and no more.
- **Localised text** — M6-10, ledger row 9. Rule 7 draws the key.
- **Reroll and Banish buttons** — M6-02; the commands do not exist (M3-08a's Out of scope).
- **Pact cards** (GD §13.2, M6-05) — a drawn offer is never corrupted yet, so a card has one appearance.

## As built

**Two new production files as specced** — `Game/Presentation/LevelUpPresenter.cs` and
`Game/Controls/OfferCard.cs`, both **block namespaces** — plus `Prefabs/UI/LevelUp.prefab`, the
`RunScope` edit and `Scenes/Run.unity` dressed. **1 447 EditMode / 0 / 0, twice consecutively on the
final code**, against M3-08a's 1 423: **24 new rows, and it lands to the row.** **PlayMode stayed at
15 rows** as predicted — no row was added there — but it came back **14/15 on both runs**, and
that is written up under *The PlayMode number* below rather than buried. `InstallerTests` was **not**
needed: the field is optional, so nothing in that fixture had to change.

### The deviations

1. **Rule 3 was rewritten rather than implemented, on the owner's ruling, and this was settled before
   a line was written.** M3-08a deviation 12 put the gate in `RunTicker.LevelUpPhase`; rule 3 put it
   in the presenter. The owner ruled **the ticker keeps it**. What decided it: rule 3's own stated
   reason — *"a pause raised in one class and lowered in another is how a screen ends up closed over
   a frozen game"* — argues against **split** ownership, and the ticker holds both halves eight
   lines apart, so the reason is satisfied and only the nomination of which file changed. What it
   buys is that the run being stopped is a once-a-frame pure function of `HasOffer` rather than a
   consequence of a view having received an event: a presenter that is absent, destroyed or never
   dressed costs a *missing screen*, loudly, instead of a run that ticks on with an offer nobody can
   spend. It also makes rule 12 true rather than a caveat. **The cost was measured rather than
   guessed**: moving the gate to the presenter would have turned **four existing rows red** — all
   four `Frame_*` rows M3-08a added, each asserting `_pause.IsPaused` against a fixture with no
   presenter in it — and would have edited the fixture whose 0/100 flake baseline had just been
   bought with 150 isolated runs. Keeping it in the ticker turned **zero** existing rows red.
2. **`RunPause` is not a dependency of `LevelUpPresenter`**, which is the spec's Public API minus one
   argument. `Construct_TakesNoRunPause` pins it at the constructor *and* over the private fields: a
   presenter that cannot reach a `RunPause` cannot grow an `if` guarding against the ticker already
   holding it, which is the two-owners state M3-08a deviation 12 exists to refuse. A task that moves
   the gate back must **delete a red row** and argue it.
3. **Three Tests-table rows were inverted in place rather than deleted**, for the reason above:
   `Offer_PausesTheRun` → **`Offer_TouchesNoPause`**, `Offer_RedrawDoesNotRePause` →
   **`Offer_RedrawRepaintsAndNeverPauses`**, `Closed_HidesAndResumes` → **`Closed_HidesTheScreen`**.
   Each holds a real `RunPause` the presenter is never handed and asserts it is untouched. The
   "it happened" half stays where it already lived — `Frame_LevelUpPhaseRunsAboveCommands`.
   `Closed_HidesTheScreen` asserts the pause is **still held** after the last pick, which is correct
   rather than a leak: the screen closes and the gate lifts one frame later, in that order, which is
   the order that can never show a closed screen over a frozen game.
4. **`LocKey` has no `.Value`, and the spec wrote it in four places.** The member is `Key`
   (`LocKey.cs:46`). The Public API comment, rule 7, `Card_DrawsTheKeyNotEnglish` and **ledger row 9's
   own text** all said `.Value`; none would have compiled. M3-00c recorded this two spec groups ago,
   so it is a known correction rather than a discovery. `Card_DrawsTheKeyNotEnglish` now also fails if
   `LocKey` ever grows a `Value`, so the two spellings cannot quietly coexist.
5. **The spec header's ledger claim is wrong and row 9 was not opened by this task.**
   `git log -S` puts row 9's text in `ae7b853 docs: spec M3-06…M3-08` — the **M3-00b** spec group —
   and M3-00d's ledger header records it being *recounted*, which is not something a row opened here
   could be. Real state: **open since M3-00b, recounted at M3-00d, owned by M3-15**; this task makes
   it *visible* rather than opening it. **Second consecutive task whose header mis-states a ledger
   fact** — M3-08a's said its ripple was one fixture and it was two.
6. **`Tests/Game/Soulvail.Tests.Game.asmdef` gained `Unity.TextMeshPro`, and the Files table does not
   cover it.** `UnityEngine.UI` resolves into that assembly without being listed because it is a
   built-in module; TextMeshPro is a package asmdef and does not. The alternative was reading every
   label through reflection on an untyped `Component`, which makes four rows unreadable to buy back
   one line of asmdef.
7. **Rule 5's latch is cleared in `Update`, not in the `OfferPresented` handler**, and the difference
   is the whole row. `ChooseOffer` repaints all three cards *inside* the call the first tap started,
   so a handler that cleared the flag would re-arm the cards mid-command and a double tap would spend
   the second pick on a node the player never saw. uGUI dispatches both taps of a double tap from one
   `EventSystem` pass with no `Update` between them, so only a new frame lifts it. The cards are made
   *interactable* again by the redraw immediately, so nothing looks dead; it is the command that
   waits a frame.
8. **`Destroy_DropsSubscriptions` invokes `OnDestroy` rather than relying on `DestroyImmediate`.**
   Unity does not call `OnDestroy` on an object whose `Awake` never ran, and `Awake` does not run in
   EditMode (Traps §5) — the row was **written and initially passed the wrong way round**: it went
   red on the first run at 23/24, and the cause was that the subscriptions were never dropped because
   the callback never fired. Left as first written it would have passed against a presenter with no
   `OnDestroy` at all. `Destroy_WhilePaused_LeavesTheAppRunnable` deliberately does **not** invoke it:
   its claim is that the app comes back even if the teardown never runs, which is the worse case and
   the one rule 12 is written against.
9. **`Docs/Traps.md` §7 gained a bullet** the Files table does not list, on M3-08a deviation 10's
   precedent. A `TestRunnerApi` created with `CreateInstance` and left in a local is **collected
   mid-run** and takes its callbacks with it: the run finishes, every test executes, and
   `RunFinished` never fires, so the results file is never written and the Editor afterwards reports
   idle. It reads as a hung suite and is length-dependent — five short runs worked before the first
   full one was lost, and three runs were lost in total. `hideFlags = HideAndDontSave` plus a static
   root fixes it.
10. **`RunScope` registers the presenter optionally, on `_hudPresenter`'s terms, and the registration
    carries a comment saying why that is not the same bargain.** Under the ruling the gate is the
    ticker's, so a scene dressed without this screen does not play "the same without a level-up" — on
    the first pick it stops dead and shows nothing. It is still optional because `IsLevelUpPending`
    requires a tree and nothing ships in `Data/Trees` until M3-12, so no run in the current build can
    reach that state. **M3-12 is named in the comment as the task that should make it a
    `MissingReferenceException`**, because that is the task that makes the failure reachable.
11. **The fixture's tree holds no Keystone and no Upgrade, and the validators are what decided that.**
    `TreeRules` refuses a Keystone sharing a tier (CH §5) and `SkillSpec` refuses an Upgrade with no
    parent — both found by running, not by reading. Branch a's tier deliberately holds three so an
    untouched tree offers three and an emptied branch a offers exactly two. All four kinds are still
    exercised: `Card_TintsByKind` builds its specs directly, because a tint is a property of one card
    rather than of a legal tree.

### The rows the table did not list, and the one float door

Four rows beyond the table's twenty: `Construct_TakesNoRunPause` (the pin above),
`Construct_RefusesNullDependencies`, `Show_RefusesNothingToDrawAndNobodyToTell`, and
`Place_IgnoresANonFiniteDpField`.

**The float door is the serialized dp layout, and it is genuinely owed.** `OfferCard`'s public
surface has none — `Show(int, SkillSpec, Action<int>)`, `Hide()`, `SetInteractable(bool)` — and
`CanvasGroup.alpha` is **not** a door: the presenter writes the constants 0 and 1 and no caller can
pass a value through it, which is M3-08a's `OverflowDamage` argument. But `_cardSizeDp` and
`_cardGapDp` are **Inspector** doors — rule 10 exists so the owner can tune them on a device — and a
NaN reaching `sizeDelta` is a `RectTransform` that never renders again, which on *this* screen is a
stopped game showing nothing. `Place` leaves the prefab's authored layout alone for NaN, either
infinity, zero and negative widths, and the row walks all five. The gap is checked separately because
zero is a legal gap and zero is not a legal width.

No new spec type, so no validation row is owed.

### The PlayMode number, said plainly rather than filed under the old alibi

**15 rows, 14 passing, on both full runs.** The failure is `Ticker_RunsTheStepsInOrder` with M2-15a's
exact message — *"The cone found nothing where the body had just walked to"* — which is the project's
one known intermittency (PROGRESS → Known issues), measured by M3-08a at **10 % over 50 isolated runs
on untouched `dev`**. All four `Frame_*` rows passed both times, and `BootSmokeTests`' three rows
passed both times, which is the dressed scene composing and a run starting with the new presenter
registered.

**Two things stop this being waved through as noise.** First, 2-for-2 at a 10 % baseline is about a
1 % coincidence. Second, **M3-08a's full PlayMode run was 15/15**, so the number moved. Isolated,
`FrameOrderTests` came back **8/8 green on three runs** — the "green alone, red inside a full run"
signature the row carried for two tasks before M3-06 retired it.

**The old alibi is not available and is not claimed.** M3-08a already recorded that both limbs were
gone. What is new and worth naming: **this is the first task since M3-02b to change `Scenes/Run.unity`**,
and `BootSmokeTests` loads that scene in the same PlayMode process *before* `FrameOrderTests` runs.
A heavier Run scene — one more full-screen `Canvas`, a `GraphicRaycaster`, three `Button`s and five
TMP labels — changing timing or GC in the shared process is exactly the *"something inside a single
PlayMode run that moves between rows"* variable the Known issues row has been unable to name. **That
is a lead for the diagnosis task, not a diagnosis**, and the isolated 8/8 is the evidence that the
fixture itself is unchanged. The measurement stopped at n = 2 full and n = 3 isolated because three
further runs were lost to the Traps §7 bullet in deviation 9 before it was understood.

### Manual steps, and why four of them could not be exercised

Steps 1–4 all need a hand-authored tree with an Active, and **M3-12 has not shipped**. No content was
authored: `SkillAuthoringTests.Boot_ScopeCarriesTheTwoLists` pins `BootScope`'s `_skills` and `_trees`
**empty** until M3-12, and wiring them turns that row red. Nothing in the build can currently reach
`IsLevelUpPending`, so the screen is verified by its suite and by the asset reading back off disk
(`Prefab_IsDressed`), and the four Editor steps are the owner's to run once a tree exists. Steps 5 and
6 are device rows and join ledger row 4.
