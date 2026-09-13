# M3-08b — The level-up screen: three cards, a tap, and the frame that starts again

**Size:** M · **Depends on:** M3-08a · **Branch:** `m3-08b-level-up-screen`
**Design refs:** GD §5.2, §7.3, §13.1, §16.1, §16.4; CH §5.1; CC §6.2; AR §7, §8, §18.1, §18.2; ADR-0004, ADR-0012 · **Ledger rows:** 4 (whether a mid-fight interruption reads well on a phone is device-only), 6 (four more colour constants with no `Palette` file — row 6's owner inherits a fourth reader, rule 8), **9, opened by this task** (a card draws a `LocKey`, because nothing resolves one yet — rule 7)

## Goal

GD §13.1's progression moment, on screen: the game is already stopped, three nodes are already drawn, and this turns them into three things a thumb can hit — then gets out of the way and lets the frame start again.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/LevelUpPresenter.cs` | Game | the screen: hears `OfferPresented`, holds the pause, sends `ChooseOffer`, hides on `LevelUpClosed` — **block namespace** (Traps §5) |
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
        [Inject] public void Construct(IRunSession session, IProgressionCommands progression,
                                       DomainEventHub hub, ContentCatalog catalog, RunPause pause);
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
3. **It holds both halves of the pause.** `RunPause.Pause(PauseReason.LevelUp)` in the `OfferPresented` handler and `Resume(PauseReason.LevelUp)` in the `LevelUpClosed` one — *this* file, both times, because a pause raised in one class and lowered in another is how a screen ends up closed over a frozen game. M3-08a rule 4 only decides **when the offer is drawn**; whether the game is stopped is the screen's business. A redraw (a second `OfferPresented` for the next pick) does **not** re-pause: it is already held, and `RunPause` would throw (M3-08a rule 12), which is the guard working.
4. **No fade, in or out.** `Time.timeScale` is 0 while the screen is up (M3-08a rule 13), so any scaled animation would freeze mid-way and an unscaled one is a second clock in a file whose whole job is to be brief — GD §13.1 budgets *"a couple of seconds"* for the entire interruption. M2-10's 0.3 s stage fade is the precedent for a transition the player is not deciding anything during; this is the opposite case.
5. **The buttons go dead the moment one is tapped**, and come back only when the next `OfferPresented` arrives. `ChooseOffer` runs synchronously and may immediately redraw for a second pick (M3-08a rule 6), so a double-tap on a card that has not yet been repainted would spend the second pick on whatever now sits at that index — a node the player never saw. One flag, set before the command and cleared by the event.
6. **Only `Count` cards are shown.** M3-04 rule 1 writes fewer than three when the tree is nearly exhausted, and the rest are hidden rather than drawn empty — CC §6.2's *"unused slots are not drawn"*, applied to the other screen that has a variable number of things on it.
7. **A card draws the `LocKey`, because nothing resolves one.** `ILocalizer` is an AR §6 port with no implementation (`TableLocalizer` is M6-10's), so `spec.NameKey.Value` and `spec.DescriptionKey.Value` are what appears — `skill.oathbound.consecrate`, not "Consecrate". This is **not** a third place raw English is typed into a prefab (`MenuPresenter` and `HudPresenter` are the first two and remain the last): nothing English is typed at all, the key is data, and ADR-0012's whole point is that the key exists from the first node. **It does mean GD §13.1's *"readable in under two seconds"* cannot be judged until M6-10** — the new ledger row 9, owned by M3-15, which has to decide whether to accept the milestone on keys or pull a minimal localizer forward.
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

_Filled at merge._
