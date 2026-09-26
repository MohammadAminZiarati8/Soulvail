# M7-04k — A full tree still tempts

**Size:** M · **Depends on:** M7-04j · **Branch:** `m7-04k-a-full-tree-still-tempts`
**Design refs:** GD §10.1, §10.3, §12.5, §13.1, §13.2; CH §4.4, §5.1, §5.2; AR §18.1, §18.2, §18.3; ADR-0011 · **Ledger rows:** [10](../ROADMAP.md#carry-forward-into-m7) — its second half, *"a finished tree rolls no offer at all … zero after stage ~13"*; [1](../ROADMAP.md#carry-forward-into-m7) — one device row

## Goal

A level that finds nothing left to take still pays its Overflow. It may also offer the corrupted form
of a node the player already owns: one card with a price in Rot, which they can take or refuse.

## Why this is the documents' own reading, not a new mechanic

[Row 10](../ROADMAP.md#carry-forward-into-m7)'s second clause is structural. `LevelUpFlow.Open`
spends a level with nothing to draw on Overflow, silently, and `OfferGenerator.Draw` makes no draw,
so a full tree never rolls a Pact again.

- **When the tree is full.** A 27-node tree fills around stage 21 (CH §5.2, measured), where a
  12-node one filled at 9 to 13. [M7-04h](M7-04h-the-oathbounds-twenty-seven.md) to
  [M7-04j](M7-04j-the-emberwrights-twenty-seven.md) move the silence later. They do not end it.
- **Where the design needs it.** GD §12.5 puts a skilled player's death at 35 to 50, so the silence
  covers the stages where the design leans on the meter hardest:
  - GD §10.3's own worked example is *"a player at stage 34 with a dying run can choose to hit 100
    Veilrot"*;
  - GD §10.1 gives Pacts as the only thing that raises the meter;
  - GD §13.2 says the temptation keeps §10 *"load-bearing at every single level-up"*.

  With no Pact past stage 21, the example is open only to a player whose meter was already high.
- **What the documents already say.** GD §13.2 says *"Any node can appear in its corrupted form."* A
  node already owned is a node. **The corruption of an owned node is that sentence read past the full
  tree.** Nothing else in the design changes for it: the pick is still Overflow, the Pact is still
  authored, and the price is still the Pact's own.

**What would change this:** the owner ruling that Veilrot past a full tree should come only from what
was taken before it filled. Then this task is deleted, and row 10's second half becomes that ruling.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Progression/LevelUpFlow.cs` | Core | **Substantial.** The roll after an Overflow level, a one-card offer that spends no pick, and a refusal |
| `Core/Progression/SkillTree.cs` | Core | **Substantial.** `Corrupt(id)`: the clean effects off, the Pact's on, the node recorded as pacted |
| `Game/Presentation/LevelUpPresenter.cs` | Game | **Substantial.** One card and a *Refuse* button |
| `Tests/Core/Progression/CorruptionOfferTests.cs` | Tests.Core | **New.** The roll, the draws, the take, the refusal, the resume |
| *small edits* | Core, Game, Data, Docs | `Core/Progression/OfferGenerator.cs` — `RollCorruption` (rule 2); `Core/Events/ProgressionEvents.cs` — `OfferPresented.IsCorruption`, optional and last (rule 3); `Core/Ports/IProgressionCommands.cs` — `DeclineOffer()` (rule 5); `Core/Run/RunSession.cs` — routes it; `Core/Run/RunState.cs` — the `IsCorruptionOffer` read; `Prefabs/UI/LevelUp.prefab` — a *Refuse* button and one line; `Data/Localisation/English.asset` — `ui.levelup.refuse` and `ui.levelup.corruption`; `Pseudo.asset` regenerated; `Docs/GameDesign.md` — §13.2 gains the as-built sentence |
| *ripple* | Tests.Core, Tests.Game, Tests.PlayMode | The three `IProgressionCommands` fakes gain `DeclineOffer`: `ResumeFlowTests.RecordingSession`, `SanctumPresenterTests.RecordingPort` and `FrameOrderTests.RecordingCore`. Any row counting `Offers` draws across an Overflow level gains two a level; the compiler cannot find those rows, and the first red pass does. `OfferPresented`'s two construction sites are untouched |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Progression;

public sealed class OfferGenerator
{
    /// <summary>
    /// After an Overflow level: two draws, always — a taken node's slot, then GD §13.2's chance — and
    /// true when the chance said yes and that node has a Pact it was not taken as.
    /// </summary>
    public bool RollCorruption(SkillTree tree, IRandomStream offers, out ContentId id);
}

public sealed class SkillTree
{
    /// <summary>Takes a taken node's clean effects off, puts its Pact's on, and records it as pacted.</summary>
    /// <exception cref="InvalidOperationException">Not taken, no Pact, or already pacted.</exception>
    public void Corrupt(ContentId id);
}

public sealed class LevelUpFlow
{
    /// <summary>Whether the offer on the table is a corruption of an owned node (rule 3).</summary>
    public bool IsCorruption { get; }

    /// <summary>Refuses a corruption offer. Throws for an ordinary one — a pick is not optional.</summary>
    public void Decline(IRandomStream offers);
}
```

```csharp
namespace Soulvail.Core.Ports;

public interface IProgressionCommands
{
    /// <summary>Refuses the corruption on the table (M7-04k). A no-op when none is.</summary>
    void DeclineOffer();
}

namespace Soulvail.Core.Events;

public readonly struct OfferPresented
{
    public OfferPresented(int count, int picksOwed, int pactIndex, bool isCorruption = false);
    public readonly bool IsCorruption;
}
```

## Behaviour

1. **The Overflow is paid first, whatever happens next.**
   - In `Open`'s loop, a pick with nothing to draw calls `GrantOne()` as it does today: the modifiers
     go on, the pick is spent, and `OverflowGranted` is published. **Only then** is the corruption
     rolled.
   - A refused corruption therefore costs nothing, and a taken one costs its Rot alone.
   - `Overflow = Level − 1 − Taken − Pending` still holds, because a corruption spends no pick and
     takes no new node. That is M3-08a rule 9's identity, which `RunSession.Start`'s restore derives
     from.
2. **The roll is two draws, always, and asks the tree what it owns.**
   - `RollCorruption` takes `slot = offers.NextInt(0, tree.TakenCount)` and
     `rolled = offers.Chance(OfferGenerator.PactChance)`, both into locals before either is read. That
     is `RollPact`'s rule and its reason: the stream moves by two whatever the answer (AR §18.3).
   - It answers true, with `TakenIds[slot]`, when `rolled` holds and that node `HasPact` and is not
     `IsPact`.
   - **A tree with nothing taken rolls nothing and draws nothing.** No real tree reaches an Overflow
     level with no node taken, and a draw over an empty range is not a draw.
   - **The rate falls as the owned Pacts run out.** That is the design rather than a flaw: at most
     twenty-four corruptions a run, one per non-Keystone node.
3. **A corruption is a one-card offer.**
   - `Open` writes `_offer[0] = id`, `_count = 1`, `_pactIndex = 0` and `IsCorruption = true`. It then
     publishes `OfferPresented(1, PendingLevelUps, 0, isCorruption: true)` and returns.
   - **The level-up pause is taken** as for any offer (`RunTicker.LevelUpPhase`), because there is a
     choice to make, which is GD §13.1's reason for pausing at all.
   - **The rest of the owed picks wait** behind it.
   - **A banked reroll is not spent on a corruption.** It is not a draw, and M6-02b rule 3 spends a
     charge *"only on a draw that found something"*.
4. **Taking it corrupts the node and charges its Rot, and spends nothing else.** `Choose(0)` on a
   corruption:
   - calls `SkillTree.Corrupt(id)`, then `Veilrot.Gain(spec.Pact.Veilrot)` — M6-05b rule 6's order;
   - calls no `SpendLevelUp` and no `SkillRunner.Add`, because the node is already owned;
   - clears the offer and runs `Open` again, publishing `LevelUpClosed` when nothing is left.
5. **Refusing it is a command, legal only on a corruption.**
   - `IProgressionCommands.DeclineOffer` reaches `LevelUpFlow.Decline`, which clears the offer, runs
     `Open` for any picks still owed, and publishes `LevelUpClosed` when none are.
   - **On an ordinary offer `Decline` throws**, because CH §5.1's pick is not optional. The port's
     method is a no-op when no corruption is on the table, so a stray tap is not a crash.
   - The button calls it from `onClick`, as the cards call `ChooseOffer` on the same paused screen
     (M3-08b).
6. **`SkillTree.Corrupt` swaps the effects under one source.**
   - For each clean effect of the node it calls `EffectRegistry.Remove(effect, spec)`, then applies the
     Pact's effects with the same `SkillSpec`. The clean and corrupted forms are one source (M6-05a
     rule 1), so each handler's `Remove` takes back exactly the clean claim.
   - **Every clean primitive already has its `Remove`**: `ModifyStat`, `KnockbackOnSwing`,
     `ModifySkillCooldown`, and [M7-04d](M7-04d-an-upgrade-that-adds-to-a-cast.md)'s `ExtendCast`.
   - **An Active has no clean take effects**, so its corruption only adds the Pact's cooldown cut and
     downside.
   - **The order has one stated cost.** The clean modifiers come off before the Pact's go on, because
     a Pact modifier added first would share the source and go with the `RemoveAll`. So corrupting a
     max-HP node at full health clamps the hit points to the maximum without the clean node's bonus,
     and the Pact's larger maximum then goes on without a heal — M3-05's rule that raising a maximum
     is not a heal. The loss is at most the clean node's number, once. A resume matches it, because
     `Health.Restore`'s hit points are absolute.
   - The node joins `PactedIds`. It is already in `TakenIds`, at its first take position.
7. **A resume is the same run.**
   - `SkillTree.Restore` replays a pacted id at its take position, as pacted. A node corrupted at stage
     30 therefore comes back as though taken corrupted at stage 12, with the same effects and stats.
   - It is saved through the existing `pactedNodeIds`, so the format does not change.
   - The Rot it cost is already in `Economy.Veilrot`.
8. **The screen draws one card and a way out.**
   - On `IsCorruption`, `LevelUpPresenter.Draw` shows card 0 through `OfferCard.Show(..., isPact:
     true)` and hides the other two.
   - It shows `_corruptionLine` (*"The Veil offers more"*) and `_refuse` (*"Refuse"*); `_refuse` is
     inactive on every ordinary offer.
   - The double-tap latch covers the button as it covers the cards.
9. **Nothing allocates on a tick.** The roll is two draws and an index, the swap walks effect lists
   already built, and the screen's two objects are on the prefab.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Overflow_IsPaidBeforeTheRoll` | a full tree, one level owed / `Open` / `OverflowGranted` published before any `OfferPresented`; `OverflowLevels` 1 — rule 1 |
| `Roll_AlwaysTwoDraws` | a scripted stream: the chance says no; then yes over a node with no Pact; then yes over an owned Pact / three Overflow levels / six draws — rule 2 |
| `Roll_OffersOnlyAnUncorruptedPact` | the slot lands on a node taken as a Pact, chance yes / — / no offer — rule 2 |
| `Roll_NothingTakenDrawsNothing` | an Overflow level with no node taken / — / zero draws — rule 2 |
| `Corruption_IsOneCard` | a roll that finds Keen Censer / — / `HasOffer`, `Offer.Count` 1, `PactIndex` 0, `IsCorruption`, and `OfferPresented(1, _, 0, true)` — rule 3 |
| `Corruption_DoesNotSpendAReroll` | a banked reroll and a corruption / — / `RerollCharges` still 1 — rule 3 |
| `Corruption_TakenSwapsTheEffects` | Keen Censer taken clean (+0.15), then corrupted / `Choose(0)` / weapon damage carries +0.34 and not +0.15, and max HP −25 — rules 4, 6 |
| `Corruption_ChargesItsRot` | an Emberwright, a 15-Rot Pact / `Choose(0)` / the meter +15 — rule 4 |
| `Corruption_SpendsNoPick` | two picks owed, the first an Overflow that corrupts / `Choose(0)` / `PendingLevelUps` moved by one, for the Overflow alone — rules 1, 4 |
| `Corruption_AMaxHpNodeClampsOnce` | Pale Vigour taken clean at full health (+15), corrupted (+41) / `Choose(0)` / the maximum rose by 26, the hit points fell by at most 15 — rule 6 |
| `Corruption_OfAnActiveCutsItsCooldown` | Emberfall owned clean, then corrupted / — / its effective cooldown is 0.6 × base — rule 6 |
| `Corruption_OfAnExtensionSwapsIt` | Hallowed Ground owned clean, then corrupted / a Consecrate cast / one burn zone at 7 a pulse, none at 3 — rule 6 |
| `Decline_ClosesAndPaysNothing` | a corruption on the table / `DeclineOffer` / no offer, `LevelUpClosed`, the node still clean, the meter unmoved — rule 5 |
| `Decline_AnOrdinaryOfferIsRefused` | three ordinary cards / `LevelUpFlow.Decline` / `InvalidOperationException`; the port's `DeclineOffer` does nothing — rule 5 |
| `Resume_TheCorruptedNodeComesBackCorrupted` | Keen Censer corrupted at an Overflow level, then a boundary snapshot / `Start` from it / +0.34 on, pacted, the Overflow derivation unchanged — rules 1, 7 |
| `Presenter_DrawsOneCardAndRefuse` | *(in `LevelUpPresenterTests`)* `OfferPresented(1, 0, 0, true)` / draw / card 0 active as a Pact, cards 1–2 inactive, `_refuse` active — rule 8 |
| `Presenter_RefuseIsHiddenOnAnOrdinaryOffer` | an ordinary three / draw / `_refuse` inactive — rule 8 |
| `Presenter_RefuseSendsDecline` | a spying port / tap `_refuse` twice in one frame / `DeclineOffer` once — rules 5, 8 |
| `Corruption_AllocatesNothing` | 10 000 rolls / `AllocationAssert.None` / zero — rule 9 |

**Guard rows are implied, not listed:** `Corrupt`'s three refusals; the roll's nulls.

## Manual verification (Editor / device)

1. **[Editor]** Play past a full tree, or start a run with every node taken through the debug level
   keys. *Expected: most Overflow levels are silent as before. Now and then the game pauses on one
   violet card — a node you own, its Pact sentence and its Rot — with a Refuse button.*
2. **[Editor]** Take one. *Expected: the meter rises by its price, the stat moves by the Pact's
   number, and the tree view still shows the node taken.*
3. **[Editor]** Refuse one. *Expected: the game resumes at once, and nothing has changed but the
   Overflow toast already shown.*
4. **[device]** The one-card screen and the Refuse button at 400 dpi, against the thumb rule. Row 1's.

## Out of scope

- **Pacts on Keystones.** M7-04a rule 2; the roll finds none.
- **What a finished tree's Essence buys.** [Row 9](../ROADMAP.md#carry-forward-into-m7)'s, and the
  owner's.
- **Tuning the rate.** It is `PactChance`, which is M8-05's.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
