# M6-05b — The offer that rolls one, and the two draws it always spends

**Size:** M · **Depends on:** M6-05a · **Branch:** `m6-05b-pact-offers`
**Design refs:** GD §10.1, §13.1, §13.2, §16.4; CH §4.4, §5.1; AR §8, §14, §18.2, §18.3; ADR-0011 · **Ledger rows:** [1](../ROADMAP.md#carry-forward-into-m6) — one device row; [3](../ROADMAP.md#carry-forward-into-m6) — touched and not moved; [7](../ROADMAP.md#carry-forward-into-m6) — two strings

## Goal

One of the three cards at a level-up may arrive corrupted — violet, with its own description and its
Rot price on it — and taking it puts GD §10's meter up by what the card said.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Progression/OfferGenerator.cs` | Core | **Substantial.** The roll, and the two draws it always costs |
| `Core/Progression/LevelUpFlow.cs` | Core | **Substantial.** Which card is the Pact, and the Veilrot a corrupted take pays |
| `Game/Controls/OfferCard.cs` | Game | **Substantial.** A violet frame, the Pact's description, and its price in Rot |
| `Tests/Core/Progression/PactOfferTests.cs` | Tests.Core | The roll, the consumption rule, and the take |
| *small edits* | Core, Game | `Core/Events/ProgressionEvents.cs` — `OfferPresented.PactIndex`; `Core/Run/RunState.cs` — the `PactIndex` read; `Core/Run/RunSession.cs` — the meter handed to the flow; `Game/Presentation/LevelUpPresenter.cs` — one argument to `OfferCard.Show`; `Game/Presentation/Palette.cs` — the class remark about Pact frames corrected (rule 8); `Prefabs/UI/LevelUp.prefab` — a frame graphic and a Rot label on each card; `Data/Localisation/English.asset` — two rows |
| *ripple* | Tests.Core, Tests.Game | **10 `OfferGenerator.Draw(...)` sites across 3 files** and **9 `new LevelUpFlow(...)` sites across 2 files** — compiler-guided; `OfferCardTests` and `LevelUpPresenterTests` gain the corrupted draw |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Progression;

public sealed class OfferGenerator
{
    /// <summary>
    /// How often an offer holds a Pact — GD §13.2's <em>"one of the three offered nodes **may**
    /// appear as a Pact."</em>
    /// </summary>
    /// <remarks>
    /// <b>A <c>const</c> beside <see cref="SameBranchPenalty"/> rather than a number on the mode,
    /// and the argument is the one the project just made.</b> M4-05a put <c>ShardPayout</c>'s two
    /// numbers in code, M4-07 struck the row that wanted them moved, and M6-00a fired that
    /// parking-lot line's promoter and answered **no** with the count behind it — inventing an
    /// authored home for a number nothing tunes costs 63 fixtures or a second home for the value.
    /// Nothing in V1 turns this; **M8-05** is the first task that would, and it is the same task
    /// the payout line was re-aimed at.
    /// </remarks>
    public const float PactChance = 0.25f;

    /// <summary>
    /// Fills <paramref name="destination"/> and says which of the written cards, if any, is a Pact.
    /// </summary>
    /// <param name="pactIndex">
    /// The index into <paramref name="destination"/> of the corrupted card, or <c>-1</c>. Never
    /// more than one (GD §13.2).
    /// </param>
    public int Draw(
        SkillTree tree, IRandomStream offers, int count, Span<ContentId> destination,
        out int pactIndex);
}

public sealed class LevelUpFlow
{
    /// <param name="veilrot">
    /// GD §10's meter, required — every run has one, and a null would be a run that took Pacts for
    /// free (M6-01a rule 5's argument, 9 call sites over).
    /// </param>
    public LevelUpFlow(
        SkillTree tree, LevelTracker progression, SkillRunner runner, EffectRegistry effects,
        IDomainEvents events, OverflowSpec overflow, Veilrot veilrot);

    /// <summary>Which offered card is a Pact, or <c>-1</c>. Empty offer answers <c>-1</c>.</summary>
    public int PactIndex { get; }
}
```

```csharp
namespace Soulvail.Core.Events;

public readonly struct OfferPresented
{
    // ... Count and PicksOwed, then:

    /// <summary>Which card is corrupted, or <c>-1</c> — rule 5.</summary>
    public readonly int PactIndex;
}
```

```csharp
// Game/Controls/OfferCard.cs — one more argument on the method that already takes four.
public sealed class OfferCard : MonoBehaviour
{
    /// <param name="isPact">
    /// Whether to draw <paramref name="spec"/>'s corrupted form: the Pact's description, its Rot
    /// price, and <see cref="Palette.Veilrot"/> on the frame — rule 7.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="isPact"/> is true and <paramref name="spec"/> has no Pact.
    /// </exception>
    public void Show(int index, SkillSpec spec, ILocalizer localizer, Action<int> onChosen, bool isPact);

    /// <summary>How it was last drawn. The read every Pact row asserts against.</summary>
    public bool IsPact { get; }
}
```

## Behaviour

1. **Exactly two extra draws per non-empty offer, taken unconditionally, and that is the whole of
   this task's seed discipline.** `OfferGenerator`'s own rule is that *"consumption is a function of
   the pick count alone — three offers cost three draws whether the tree holds six candidates or
   twenty-seven"*, and a roll that only drew when it was going to matter would break it in the worst
   way available: the stream would advance by an amount that depended on how many of the player's
   available nodes happened to carry a Pact block, so the same seed would replay differently after a
   content edit. So a call that writes `picks > 0` cards spends `picks + 2`: **the index first, then
   the chance**, both always. `IRandomStream.Chance` is already documented as consuming one draw
   either way *"which is what keeps a sequence stable when a probability is tuned to a certainty"* —
   this is that sentence applied to the other operand.
2. **An offer of nothing makes no draw at all, including these two.** The existing rule, unchanged:
   there is no pick to roll for. `pactIndex` is `-1`.
3. **The index is drawn over every written card, and a card with no Pact block simply is not one.**
   `NextInt(0, picks)` picks a slot; if the node in it has no `PactSpec`, `pactIndex` is `-1` and
   nothing else changes. **Restricting the draw to Pact-carrying candidates was weighed and
   refused**: it would make the *number of draws* independent of content but the *meaning* of the
   draw depend on it, which is the same seed hazard one layer down, and it would also make Pacts
   appear at `PactChance` regardless of how few exist. As written the observed rate is
   `PactChance × coverage`, which is honest and self-correcting — **M7-04** authoring eighty-one
   nodes raises it by authoring, not by a constant.
4. **A Pact is never offered for a node the tree would refuse anyway.** The roll happens *after* the
   picks are made, over what `SkillTree.Available` already yielded, so a banished node
   ([M6-02b](M6-02b-four-things-essence-buys.md) rule 4), a locked tier and an Upgrade with no owned
   parent are all out before the roll exists. Nothing in `OfferGenerator` gains a second opinion
   about availability.
5. **`LevelUpFlow` holds the index and the screen is told, because a card cannot ask.**
   `_pactIndex` sits beside `_offer`, `OfferPresented` carries it, and `RunState.PactIndex` is the
   scalar read — `RunState.Offer`'s bargain, one field over. **`ChooseOffer` still takes a
   position**: what the player tapped is a place on a screen, and whether that place was corrupted
   is the model's fact rather than the view's claim, so nothing about M3-08's command changes and a
   view cannot ask for a Pact it was not offered.
6. **Taking it is one take and one gain, in that order.** `Choose` calls
   `_tree.Take(id, asPact: index == _pactIndex)` and then, only if it was, `_veilrot.Gain(spec.Pact.Veilrot)`.
   The order is `SkillTree.Take`'s own: effects, then `NodeTaken`, then the meter — so a subscriber
   reading the player's damage inside `NodeTaken` sees the corrupted node, and a subscriber reading
   `VeilrotChanged` sees a node that is already owned. **A gain that reaches 100 fires the Claiming
   from inside `Choose`**, which is correct and is worth a row: M6-04 rule 6's latch closes on the
   tick the meter reaches `Max`, and that tick is a level-up screen the player is looking at.
7. **A corrupted card is violet, keeps its kind stripe, and says what it costs.**
   `Palette.Veilrot` on a frame — GD §16.4 gives violet *"Veilrot, Pacts, corruption"*, one colour
   for all three. The **kind stripe stays CH §4's**, because a corrupted Keystone is still a
   Keystone and the stripe is the only thing on the card that says so. The name stays the clean
   node's and the description is the Pact's ([M6-05a](M6-05a-what-a-pact-is.md) rule 4), and
   `ui.offer.rot` puts *"+15 Rot"* where a clean card has nothing — the one number GD §13.2 puts in
   brackets after every example it gives.
8. **`Palette`'s class remark is corrected here, because this task is what proves it wrong.** It
   reads *"M3-13b's damage tint, M4-04's boss segments, M6-04's Veilrot meter and M6-05's Pact
   frames each add a member and a test row"* — the Pact frame adds **no member**, because GD §16.4
   makes one violet mean Veilrot and Pacts and corruption, and a second would be the drift ledger
   row 6 exists to catch arriving from inside the file that exists to stop it.
9. **A reroll rolls again, and that is the arithmetic said out loud.**
   [M6-02b](M6-02b-four-things-essence-buys.md) rule 3's banked reroll makes `Open` draw, discard and
   draw again, so a rerolled level spends `2 × (picks + 2)` on `Offers` and its second three may hold
   a Pact where its first did not. Confined to `Offers` (ADR-0011), so nothing about what the run
   fights changes.
10. **Vigil needs nothing from this file.** GD §13.4's two-card offer is `count` 2 at the call site
    ([M6-06b](M6-06b-four-ordeals-and-two-refusals.md)), and the roll is over *written* cards, so
    `NextInt(0, 2)` and the same two draws. The consumption rule survives an Ordeal because it was
    written against `picks` rather than against three.
11. **Nothing here allocates.** Two draws, an `int` field, one `bool` on the event, and
    `OfferCard.Show` writing a colour it is handed. `RunState.PactIndex` is a field read.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Offer_SpendsTwoMoreDrawsThanPicks` | a counting `IRandomStream`, a tree of twelve / one three-card draw / **5** draws on `Offers` and none on any other stream — rule 1 |
| `Offer_SpendsTheSameTwoWhenThereIsNoPact` | every candidate without a Pact block / a draw / still 5 draws, `pactIndex` −1 — rule 1 |
| `Offer_SpendsNothingOnAnEmptyTree` | nothing available / `Draw` / 0 written, 0 drawn, `pactIndex` −1 — rule 2 |
| `Offer_ConsumptionDoesNotDependOnCoverage` | two trees identical but for which nodes carry Pacts / the same scripted stream / the **same three ids** and the same draw count — rule 1, the hazard asserted |
| `Offer_AtMostOneCardIsAPact` | 10 000 draws, every node Pact-carrying / — / `pactIndex` is always in [−1, count) and never two cards |
| `Offer_RollsAboutAQuarter` | 10 000 draws over an all-Pact tree with an LCG stream / — / the rate is `PactChance` ± 0.02 — rule 3 |
| `Offer_ANodeWithoutAPactIsNotOne` | a scripted stream landing the index on a clean node / — / `pactIndex` −1 while the chance said yes — rule 3 |
| `Offer_ABanishedNodeIsNeverAPact` | one node banished / 10 000 draws / it appears zero times, corrupted or clean — rule 4 |
| `Flow_PublishesThePactIndex` | a draw that rolled one / — / `OfferPresented.PactIndex` matches `RunState.PactIndex` — rule 5 |
| `Flow_EmptyOfferAnswersMinusOne` | no offer open / `RunState.PactIndex` / −1 |
| `Flow_TakingThePactCardAppliesTheCorruptedEffects` | card 1 is the Pact / `Choose(1)` / the Pact's effects, `SkillTree.IsPact` true — rule 6 |
| `Flow_TakingAnotherCardIsClean` | card 1 is the Pact / `Choose(0)` / clean effects, `IsPact` false, **Veilrot unmoved** |
| `Flow_TakingThePactPaysItsRot` | a 15-Rot Pact, meter at 0 / `Choose` / `Veilrot.Value` 15, one `VeilrotChanged` — rule 6 |
| `Flow_TheMeterMovesAfterTheNodeIsOwned` | a subscriber reading `RunState.TakenNodeIds` inside `VeilrotChanged` / a Pact taken / it already holds the id — rule 6 |
| `Flow_APactThatReachesAHundredClaims` | meter at 88, a 15-Rot Pact / `Choose` / `Value` 100, one `ClaimingBegan`, and the run is still on a level-up screen — rule 6 |
| `Flow_RefusesANullMeter` | `new LevelUpFlow(..., veilrot: null)` / — / `ArgumentNullException` — the Public API's stated reason |
| `Flow_ARerolledOfferRollsAgain` | one banked reroll, a scripted stream / a level-up / 10 draws and the **second** three is what is presented — rule 9 |
| `Flow_AVigilOfferStillRollsTwo` | `count` 2 / a draw / 4 draws, `pactIndex` in [−1, 2) — rule 10 |
| `Card_DrawsAPactViolet` | a Pact card / `Show(..., isPact: true)` / the frame is `Palette.Veilrot`, the kind stripe is still CH §4's — rule 7 |
| `Card_DrawsThePactsDescriptionAndTheCleanName` | as above / — / the name is `spec.NameKey`'s word and the description is `Pact.DescriptionKey`'s — rule 7 |
| `Card_DrawsTheRotPrice` | a 15-Rot Pact / — / *"+15 Rot"* through `ui.offer.rot`, and a clean card draws nothing there |
| `Card_RepaintsBackToClean` | a card drawn as a Pact, then redrawn clean for a second pick / — / frame, description and Rot label all back — `OfferCard`'s repaint-in-place rule |
| `Card_RefusesAPactItCannotDraw` | a spec with no Pact / `Show(..., isPact: true)` / `ArgumentException` — the presenter reading past the model |
| `Card_CarriesNoSerializedColour` | `typeof(OfferCard)` / reflection / none — `Views_CarryNoSerializedColour`, still |
| `Palette_ThePactFrameIsNotATenthColour` | the sweep / — / `OfferCard` reads `Palette.Veilrot`, and `Palette` has gained no member — rule 8 |
| `Presenter_PassesThePactThrough` | `OfferPresented(3, 1, pactIndex: 2)` / — / card 2 is drawn as a Pact and the other two clean |
| `Offer_AllocatesNothing` | 100 000 draws / `AllocationAssert.None` / zero — rule 11 |

**Guard rows are implied, not listed:** every existing `Draw` and `Choose` guard firing unchanged,
and a `pactIndex` outside the offer refused by `Choose`.

## Manual verification (Editor / device)

1. **[Editor]** Play to level 6 with a fixed seed. At least one of the six offers is violet, carries
   a different sentence from the clean version of the same node, and says what it costs. Take it:
   the meter on the right edge moves by that number.
2. **[Editor]** Play the same seed twice, taking clean nodes both times. The offers are identical —
   rule 1, looked at, because it is the property that is invisible when it works.
3. **[Editor]** Buy a Reroll, then level up. The three cards are a different three and may be
   corrupted differently — rule 9.
4. **[device]** **[ledger row 1](../ROADMAP.md#carry-forward-into-m6)**: a Pact-framed offer card is
   the same violet as the meter on a screen GD §13.1 gives the player under two seconds to read, and
   whether *"corrupted"* reads as a warning or as a highlight at six inches is not answerable in an
   Editor whose `Screen.dpi` reads 120 ([Traps §9](../../Traps.md)).

## Out of scope

- **What a Pact *is*.** [M6-05a](M6-05a-what-a-pact-is.md) — the block, the band, and the save.
- **A second Pact on one offer.** GD §13.2 says *one of the three*.
- **A Pact on a splash-borrowed branch's nodes.** They are ordinary nodes of the run's tree after
  CH §5.4's install, so they roll like the rest, and nothing here knows the difference — asserted by
  `Offer_ABanishedNodeIsNeverAPact`'s neighbour rather than given a rule.
- **Tuning `PactChance`.** The `const`'s remark names **M8-05**, and it is the same task
  `ShardPayout`'s two numbers were re-aimed at.
- **Drawing corruption as anything but a frame.** GD §13.2's *"visually corrupted"* is M7's art
  pass; a violet frame and a Rot figure is what a capsule build can say.
- **The tree screen marking a node the run took as a Pact.** [Ledger row 3](../ROADMAP.md#carry-forward-into-m6):
  `TreeNodeView`'s cell holds neither an icon nor a word, and a fifth `NodeState` would be a fifth
  colour on a cell that already cannot carry four. `SkillTree.IsPact` exists for the day it can.

## As built

**Built to the table's shape.** `OfferGenerator.PactChance` is 0.25. `Draw` gains `out pactIndex`, and
anything it writes costs `picks + 2` draws: the slot, then the chance, both always taken. The slot
is a Pact only if the chance says yes **and** its node carries a block. `LevelUpFlow` takes a
required `Veilrot`, holds `_pactIndex` next to `_offer`, and publishes it on `OfferPresented`.
`Choose` calls `Take(id, asPact)` and then `Gain`, so the meter moves after `NodeTaken`.
`RunState.PactIndex` forwards the value. `OfferCard.Show` gains `isPact`, which turns on a violet
frame, swaps in the Pact's description and writes the price, and `IsPact` reads back how the card
was drawn. `Palette` gains no member. EditMode 2 766 → **2 793, +27**; PlayMode **26**, unchanged.

### Deviations

1. **No `OfferCardTests` exists.** `OfferCard`'s rows have always been in `LevelUpPresenterTests`,
   so the six `Card_*` rows and `Presenter_PassesThePactThrough` are there.
   `Palette_ThePactFrameIsNotATenthColour` is in `PaletteTests`. It pins the member count at
   **19**, which is what reflection returns.
2. **The frame is a UGUI `Outline` on the card's own `Image`** (4 px, off by default). Unity ships
   no hollow sprite, and a filled child `Image` would cover the body. `OfferCard` writes
   `effectColor` from `Palette.Veilrot` on every draw, so the colour serialized on the `Outline` is
   never what the player sees and `Card_CarriesNoSerializedColour` holds. The prefab gained the
   `Outline` and a `Rot` TMP label (18 pt, bold, bottom edge) on each card, and `Prefab_IsDressed`
   now checks both references.
3. **The label reads *"Pact · +15 Rot"*: two rows, `ui.offer.pact` and `ui.offer.rot`.** Ledger
   row 7 counted *"the card's frame label and its Rot figure"*, and the word means a Pact is not
   told by colour alone. `ILocalizer` has no `Format` (M6-10's), so the card builds the line with
   an invariant `string.Format`, `ClassCard`'s way.
4. **`Flow_PublishesThePactIndex` asserts `LevelUpFlow.PactIndex`**, because a bare flow has no
   `RunState`. The `RunState` half is the first assertion of `Presenter_PassesThePactThrough`,
   which runs over a real session.
5. **`Flow_AVigilOfferStillRollsTwo` calls the generator directly.** Rule 10 puts `count` 2 at the
   call site, and the flow has no Vigil yet.
6. **The ripple counts were wrong.** `Draw` has **24 sites across 2 files**, not 10 across 3
   (`OfferGeneratorTests` 20, `SplashBranchTests` 4). Five existing rows had pinned rule 1's old
   cost and were re-pinned: `Draw_OneDrawPerPickAndOnlyOffers` 3 → 5,
   `Draw_FewerThanCountWhenScarce` 2 → 4, `Open_DrawsFromOffersAndNoOtherStream` 3 → 5,
   `Reroll_TheNextOfferIsTheSecondDraw` (script padded, 6 → 10), and
   `SplashBranchTests.Offer_DrawsFromBothTrees`. That last one's sweep of 800 values assumed one
   value per draw. It went red at 67 against 200 and now strides three.
7. **`LevelUpPresenterTests`' fixture nodes carry a Pact** (every kind that allows one: +15 %, 15
   Rot), so a real offer can roll one. Fifteen Rot stays under the 25 threshold, so no existing row
   moves.
8. **No row for *"a `pactIndex` outside the offer refused by `Choose`"*.** `Choose` takes a
   position, and the only writer of `_pactIndex` is the generator's `[-1, picks)`. The existing
   index guard is the refusal.
9. **`RunSession`'s comment on `State.Rot.Tick`** said nothing gains Veilrot until this task. It is
   corrected.

### Findings

- **Coverage is the real rate.** With 4 Pacts in 24 nodes, an offer shows one about
  `0.25 × P(the slot holds one of the four)` of the time, well under a quarter. Manual step 1's
  *"at least one of six offers is violet"* may need a second seed. Rule 3 says so, and **M7-04**
  moves it.
- **M6-05a's manual step 2 can now be run** through the card rather than a debug command.
- **Known issue 1 did not fire.** PlayMode was 26 / 0 twice.
- **Ledger row 7** gets its two strings. It is touched and not moved.
