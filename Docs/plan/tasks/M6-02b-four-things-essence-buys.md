# M6-02b — Four things Essence buys, and the one that sculpts

**Size:** M · **Depends on:** M6-02a, M6-04 · **Branch:** `m6-02b-sanctum-services`
**Design refs:** GD §13.3, §15; CH §5; AR §6, §8, §10.1, §14, §18.2, §18.3; ADR-0006, ADR-0008, ADR-0011 · **Ledger rows:** none

## Goal

GD §13.3's four services work: a banked reroll, a node taken out of the run for good, thirty hit
points and fifteen Veilrot — priced on the mode, refused before they are offered, and remembered
across an app kill.

## Why it depends on M6-04 rather than the other way round

Cleanse is 60 Essence for −15 Veilrot, and until [M6-04](M6-04-veilrot-thresholds-and-the-claiming.md)
there is no meter to take it off. The two alternatives are both worse than reordering: shipping
three services and adding the fourth later is a screen dressed twice, and shipping four with one
that refuses is the exact defect [M5-08a](M5-08a-splash-offers-what-install-refuses.md) closed —
*a screen may not offer what the model refuses.* See [M6-04](M6-04-veilrot-thresholds-and-the-claiming.md)'s
own note on why M6's build order is not its ID order.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Progression/SanctumShop.cs` | Core | The four services, the prices, the two refusals, and the reroll counter |
| `Core/Progression/SkillTree.cs` | Core | **Substantial.** A node can be taken out of this run's pool (rules 4–6) |
| `Core/Progression/LevelUpFlow.cs` | Core | **Substantial.** A banked reroll is spent by the next draw (rule 3) |
| `Tests/Core/Progression/SanctumShopTests.cs` | Tests.Core | One fixture for the four services, their refusals and their persistence |
| *small edits* | Core, Game | `Core/Content/ModeSpec.cs` — `SanctumSpec` beside `EssenceSpec`, optional and last (rule 1); `Game/Authoring/ModeDefinition.cs` — a `Sanctum` foldout; `Data/Modes/Descent.asset` — 25 / 40 / 40 / 30 / 60 / 15; `Core/Events/EconomyEvents.cs` — `SanctumServiceBought`; `Core/Events/ProgressionEvents.cs` — `NodeBanished` beside `NodeTaken`; `Core/Ports/IProgressionCommands.cs` — `PriceOf`, `CanBuy`, `Buy`, `Banish`, `BanishableInto` (rule 2); `Core/Run/RunSession.cs` — builds the shop, implements the five, restores below the tree; `Core/Run/RunState.cs` — the shop's reads; `Core/Save/RunRecorder.cs` — the two counters and the banished list |
| *ripple* | Tests.Core, Tests.Game | `SkillTreeTests` and `OfferGeneratorTests` gain the banished cases; `LevelUpFlowTests` gains the reroll; `ModeSpecTests`, `ModeDefinitionTests` and `ContentValidationTests` gain the block; `RunSessionResumeTests` gains a banished node surviving a kill |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

/// <summary>GD §13.3's shop, as authored data. Prices and magnitudes both — ADR-0006.</summary>
public readonly struct SanctumSpec
{
    /// <exception cref="ArgumentOutOfRangeException">
    /// A price is negative, or a magnitude is not a finite number greater than zero.
    /// </exception>
    public SanctumSpec(
        int rerollPrice, int banishPrice, int healPrice, float healAmount,
        int cleansePrice, float cleanseAmount);

    public int RerollPrice { get; }     // 25, before doubling
    public int BanishPrice { get; }     // 40
    public int HealPrice { get; }       // 40
    public float HealAmount { get; }    // 30 hit points
    public int CleansePrice { get; }    // 60
    public float CleanseAmount { get; } // 15 Veilrot
}

public sealed class ModeSpec
{
    // ... M6-01a's `essence`, then, optional and last:
    //     SanctumSpec sanctum = default

    /// <summary>What this mode charges. All zeroes for a mode that authors none — rule 1.</summary>
    public SanctumSpec Sanctum { get; }
}
```

```csharp
namespace Soulvail.Core.Progression;

/// <summary>GD §13.3's four services. A closed set of arithmetic, never saved by ordinal.</summary>
public enum SanctumService { Reroll, Banish, Heal, Cleanse }

public sealed class SanctumShop
{
    /// <summary>The reroll's price multiplier per purchase — GD §13.3's "doubles per use".</summary>
    public const int RerollDoubling = 2;

    /// <exception cref="ArgumentNullException">Any argument but <paramref name="veilrot"/> is null.</exception>
    public SanctumShop(
        SanctumSpec prices, EssenceWallet wallet, PlayerCombat combat, Veilrot veilrot,
        SkillTree tree, LevelUpFlow levelUp, IDomainEvents events);

    /// <summary>What it costs right now. Saturates rather than overflowing — rule 2.</summary>
    public int PriceOf(SanctumService service);

    /// <summary>
    /// Whether it can be bought right now: affordable **and** worth something. The predicate a
    /// screen reads before it draws a button — rule 7, M5-08a's lesson.
    /// </summary>
    public bool CanBuy(SanctumService service);

    /// <summary>
    /// Buys it. The invariant behind <see cref="CanBuy"/>, never an alternative to it.
    /// <see cref="SanctumService.Banish"/> is refused here — it needs a node (rule 5).
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="CanBuy"/> is false, or the service is Banish.</exception>
    public void Buy(SanctumService service);

    /// <summary>
    /// Every node Banish may take, in tree order, and how many were written. Rule 5.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is shorter than <c>TreeRules.Count</c> — <c>SkillTree.Available</c>'s rule.
    /// </exception>
    public int BanishableInto(Span<ContentId> destination);

    /// <summary>Pays <see cref="SanctumService.Banish"/>'s price and takes the node out for good.</summary>
    /// <exception cref="InvalidOperationException">Short, or the node cannot be banished.</exception>
    public void Banish(ContentId skillId);

    /// <summary>Rerolls bought and used this run — what the save carries.</summary>
    public int RerollsBought { get; }
    public int RerollsSpent { get; }

    /// <summary>What a resumed run comes back with. Silent — rule 9.</summary>
    internal void Restore(int bought, int spent);
}
```

```csharp
namespace Soulvail.Core.Progression;

public sealed class SkillTree
{
    /// <summary>Takes <paramref name="id"/> out of this run's pool for good (GD §13.3).</summary>
    /// <exception cref="ArgumentException">Not a node of this tree, already taken, or already banished.</exception>
    public void Banish(ContentId id);

    /// <summary>Whether it has been banished. False for a stranger, as <c>IsTaken</c> is.</summary>
    public bool IsBanished(ContentId id);

    /// <summary>The banished ids, in banish order — what the recorder writes.</summary>
    public IReadOnlyList<ContentId> BanishedIds { get; }

    /// <summary>Replays a save's banishes. Drops what it cannot apply, silently — rule 9.</summary>
    public void RestoreBanished(IReadOnlyList<ContentId> banished);
}

public sealed class LevelUpFlow
{
    /// <summary>Banks one reroll, to be spent by the next draw — rule 3.</summary>
    internal void GrantReroll();
}
```

## Behaviour

1. **Six numbers on the mode, optional and last, and a shipped mode that authors none is a content
   failure.** `EssenceSpec`'s shape one argument over (M6-01a rule 2): `new ModeSpec(...)` has 63
   call sites, so the block goes at the end and `default(SanctumSpec)` is every price at zero. That
   default is worse than Essence's — free services rather than no income — so
   `ContentValidationTests.EveryShippedMode_PricesItsSanctum` asserts all four prices and both
   magnitudes are above zero on every `ModeDefinition` under `Data/`, which is
   [M6-01a](M6-01a-essence-wallet-and-drops.md) rule 3's guard with one more column.
2. **The doubling is a rule and the base is content, and the price saturates.** GD §13.3's *"doubles
   per use"* is arithmetic — it is never typed per mode, and a designer who wanted tripling would be
   changing the shape of the economy rather than a number — so it is a `const` beside
   `OfferGenerator.SameBranchPenalty`, which is the precedent for a tuning constant that is a rule.
   `PriceOf(Reroll)` is `RerollPrice × 2^RerollsBought` computed with **saturation at
   `int.MaxValue`**, because `RunEconomy` guards `RerollsBought` as non-negative and nothing bounds
   it above: a hand-edited `"rerollsBought": 99` would otherwise shift an `int` off the end of
   itself and produce a price that is negative, free, or zero. It is the same class of hole the
   `"xp":1e38` [parking-lot line](../ROADMAP.md#parking-lot) describes, caught at the one door that
   can see it.
3. **A bought reroll is a charge the next draw spends, and the player never sees what they
   avoided.** `Buy(Reroll)` calls `LevelUpFlow.GrantReroll`; the next `Open` that would draw an
   offer draws, discards, and draws again from the same stream, publishing one `OfferPresented` for
   the second result. **A Reroll button on the level-up screen was weighed and refused.** It is the
   better mechanic and it is not the one GD §13.3 describes: the service is bought *between* stages,
   before there is an offer to look at, and that section explicitly frames reroll as luck against
   Banish's sculpting — *"Banish is the most interesting purchase: it's how a player sculpts their
   offers rather than just re-rolling luck."* The button version also costs a second port member, a
   second command path and an edit to `LevelUpPresenter`, for a screen
   [M6-03a](M6-03a-the-sanctum-screen.md) is not otherwise opening. **The extra
   draw is a real seed consequence and is confined to `Offers`** (ADR-0011, `OfferGenerator`'s own
   remarks): a rerolled run's later offers differ, and nothing about what it fights does.
4. **Banish is a third flag on the tree, not a filter on the generator.** `SkillTree` already owns
   CH §5's gating and `IsAvailable`/`Available` walk one predicate so *"the two cannot disagree"*;
   putting the exclusion in `OfferGenerator` instead would give the tree screen and the offer
   different answers about the same node, which is precisely what that comment exists to prevent.
   So a `bool[] _banished` sits beside `_taken`, `Check` returns closed for a banished ordinal, and
   `Available` skips it. **`OfferGenerator` is not edited at all** — it walks `Available`.
5. **Any node the run has not taken may be banished, available or not — and the consequences are the
   player's.** GD §13.3 says *"remove a node from this run's offer pool"*, and the pool is everything
   untaken; restricting it to *currently available* nodes would make the service unable to remove
   the tier-4 node somebody keeps being offered at the end of a run, which is the case it exists
   for. Two things follow and neither is a defect: **banishing an Upgrade's parent makes that Upgrade
   unreachable for the rest of the run** (CH §4's *"only offered if you own the parent"*), and
   banishing enough of a branch makes its Keystone unreachable. The price is 40 Essence a node and
   `LevelUpFlow.Open` already spends a pick on Overflow when nothing can be drawn (M3-08a rule 5), so
   a run that banishes its own tree away is expensive, legal and survivable rather than broken.
   `Buy(Banish)` throws: banishing needs a node, and a service with an argument does not fit a
   parameterless verb — `ChooseSplash`'s asymmetry against `ChooseOffer` (M5-07 rule 4), one screen
   over.
6. **Heal and Cleanse are one call each into an object that already exists.** `Health.Heal(30)`
   returns what actually went in and is already silent for a corpse and a full bar;
   `Veilrot.Cleanse(15)` clamps at zero ([M6-04](M6-04-veilrot-thresholds-and-the-claiming.md)
   rule 3). Neither is re-implemented here, and neither number is a `const`: both are on the mode by
   rule 1, because *"+30 HP"* is exactly the kind of number ADR-0006 says lives in an asset.
7. **`CanBuy` refuses what is unaffordable *and* what is worthless, and that second half is M5-08a's
   lesson.** Heal at full health is 40 Essence for nothing; Cleanse at 0 Veilrot is 60 for nothing;
   Banish with nothing banishable is 40 for nothing. Each is refused by the model, so
   [M6-03a](M6-03a-the-sanctum-screen.md) rule 3 draws a dead button with a reason rather
   than taking the player's money — *"a guard that is correct and a screen that ignores it compose
   into a defect neither one contains."* **Reroll has no usefulness test**: a charge is never
   wasted, because it is spent by whatever offer comes next. **`Buy` still throws**, so the
   invariant survives the screen forgetting to ask; `CanBuy` is what stops it being discovered on a
   phone.
8. **Every purchase publishes once, and the wallet moves first.** `SanctumServiceBought(service,
   price, essence)` after `EssenceWallet.Spend`, so a subscriber reads a balance that has already
   been paid — [M6-01a](M6-01a-essence-wallet-and-drops.md) rule 4's ordering. Banish publishes
   `NodeBanished(skillId)` beside it, in `ProgressionEvents.cs` next to `NodeTaken`, because a node
   leaving the pool is the same kind of fact as a node entering the player's build and a tree screen
   listens for both.
9. **Restoring is silent, drops what it cannot apply, and happens below the tree's own restore.**
   `SkillTree.RestoreBanished` runs **after** `Restore(takenInOrder)`, and drops an id that is not a
   node of this tree (content changed under the save) **and** an id that is also taken (the take is
   the stronger fact and a save carrying both is corrupt rather than stale) — `SkillRunner.Restore`'s
   silent-drop rule, which M3-07b rule 6 wrote for exactly this shape. Nothing is published:
   `EssenceWallet.Restore`'s rule, and a `NodeBanished` fired inside `RunSession.Start` would reach a
   screen that has not subscribed.
10. **Nothing here allocates on a path that runs more than once a stage.** A `bool[]` sized with the
    tree, an `int` price, one `List<ContentId>` for the banish order that grows at most once per
    purchase, and `Available`'s existing allocation-free walk. `PriceOf` and `CanBuy` are read by a
    screen at 60 Hz and allocate nothing.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Sanctum_PricesComeFromTheMode` | `Descent.asset` / — / 25, 40, 40, 30, 60, 15 |
| `Sanctum_RefusesANegativePriceOrAZeroMagnitude` | each field in turn / constructed / throws, naming it |
| `Content_EveryShippedModePricesItsSanctum` | every `ModeDefinition` / converted / all six above zero — rule 1 |
| `Reroll_PriceDoubles` | 0, 1, 2, 5 bought / `PriceOf(Reroll)` / 25, 50, 100, 800 — rule 2 |
| `Reroll_PriceSaturates` | `Restore(bought: 99, spent: 0)` / `PriceOf(Reroll)` / `int.MaxValue`, positive, and `CanBuy` false — rule 2 |
| `Reroll_BuyingBanksACharge` | 25 banked / `Buy(Reroll)` / balance 0, `RerollsBought` 1, one `SanctumServiceBought` |
| `Reroll_TheNextOfferIsTheSecondDraw` | one charge, a scripted `Offers` stream / a level-up / the offer is the **second** three, one `OfferPresented`, `RerollsSpent` 1 — rule 3 |
| `Reroll_IsSpentOnceOnly` | one charge / two level-ups / the first is rerolled, the second is not |
| `Reroll_TwoChargesRerollTwoOffers` | two charges / two level-ups / both rerolled, four draws spent |
| `Reroll_ChangesNothingButOffers` | a counting `IRandom`, one charge / a level-up / only `Offers` advanced — rule 3, ADR-0011 |
| `Reroll_OnAnOverflowLevelSpendsNothing` | a full tree, one charge / a level-up / Overflow granted, `RerollsSpent` **0** — `Open` only spends a charge on a draw |
| `Banish_TakesTheNodeOutOfThePool` | 40 banked / `Banish(x)` / `IsBanished` true, `IsAvailable` false, and `Available` never writes it — rule 4 |
| `Banish_IsNeverOffered` | a tree of four with one banished / 10 000 draws / the banished id appears **zero** times |
| `Banish_AcceptsAnUnavailableNode` | a tier-4 node, nothing taken / `Banish` / accepted — rule 5 |
| `Banish_RefusesATakenNode` | a node already taken / `Banish` / throws, and nothing is spent |
| `Banish_RefusesItTwice` | banished once / again / throws, and the balance is unmoved |
| `Banish_RefusesAStranger` | an id from another class's tree / `Banish` / throws, naming the id |
| `Banish_OrphansAnUpgrade` | a parent banished / — / its Upgrade is never available again — rule 5's stated cost, asserted rather than discovered |
| `Banish_ThroughBuyThrows` | `Buy(Banish)` / — / `InvalidOperationException` naming `Banish(skillId)` — rule 5 |
| `Banish_BanishableIntoListsTheUntaken` | two taken, one banished, a tree of twelve / `BanishableInto` / nine, in tree order — rule 5 |
| `Banish_BanishableIntoRefusesAShortBuffer` | a buffer of 3 against a tree of 12 / — / throws — `SkillTree.Available`'s rule |
| `Heal_Restores` | 100/200 HP, 40 banked / `Buy(Heal)` / 130/200, balance 0 — rule 6 |
| `Heal_IsRefusedAtFullHealth` | full, 40 banked / `CanBuy(Heal)` / **false**; `Buy` throws and nothing is spent — rule 7 |
| `Heal_CannotOverfill` | 190/200 / `Buy(Heal)` / 200, and the 40 is still spent — a partial heal is the player's call |
| `Cleanse_Reduces` | 40 Veilrot, 60 banked / `Buy(Cleanse)` / 25 Veilrot, balance 0 — rule 6 |
| `Cleanse_IsRefusedAtZero` | 0 Veilrot / `CanBuy(Cleanse)` / **false** — rule 7 |
| `Cleanse_AtEightWastesSeven` | 8 Veilrot / `Buy(Cleanse)` / 0 Veilrot, 60 spent, no throw — M6-04 rule 3 |
| `Shop_RefusesWhatCannotBeAfforded` | 39 banked / `CanBuy` each / all false; each `Buy` throws and the balance is 39 — rule 7 |
| `Shop_TheWalletMovesBeforeTheEvent` | a subscriber reading the balance inside `SanctumServiceBought` / a purchase / it reads the paid balance — rule 8 |
| `Shop_CanBuyAllocatesNothing` | 100 000 reads across the four / `AllocationAssert.None` / zero — rule 10 |
| `Restore_ComesBackWithItsCountersAndBanishes` | a run with 2 bought, 1 spent and two banished, killed and resumed / — / all of it, and **nothing published** — rule 9 |
| `Restore_DropsABanishThatIsAlsoTaken` | a save naming one id in both lists / resumed / taken, not banished, no throw — rule 9 |
| `Restore_DropsABanishThisBuildDoesNotShip` | a save naming `skill.deleted` / resumed / dropped silently — rule 9 |
| `Recorder_WritesTheThree` | a run mid-shop / `Take` / `Economy.RerollsBought`, `RerollsSpent` and `BanishedNodeIds` all captured |

**Guard rows are implied, not listed:** nulls to the shop's constructor, `Enum.IsDefined` on
`SanctumService` at `PriceOf`, `CanBuy` and `Buy`, and a `default(ContentId)` to `Banish`.

## Manual verification (Editor / device)

1. **[Editor]** Clear stage 2 with 52 Essence and a debug command per service. Buy a Heal at full
   health: refused, balance unmoved. Take a hit, buy again: 30 HP and 12 left.
2. **[Editor]** Banish a node you have been offered twice, then play four more level-ups. It never
   appears again, and the tree screen still draws it (as *Locked* until
   [M6-03b](M6-03b-the-meter-on-the-right-edge.md) gives it a state of its own).
3. **[Editor]** Buy a reroll, quit to the menu, press `Continue`, and level up. The charge is still
   there and is spent. This is the only step that exercises rule 9 against
   [M6-01b](M6-01b-save-format-v4.md)'s two counters.

## Out of scope

- **Any screen, and `NodeState.Banished`.** `TreeNodeView`'s own remarks name *"M6-02's `Banished`"*
  as a member it is waiting for; with this task split, the member belongs to the first task that
  **draws** one, which is [M6-03b](M6-03b-the-meter-on-the-right-edge.md) rule 7. Until then a
  banished node draws as `Locked`, which is what it is.
- **A Reroll button on the level-up screen.** Rule 3, with the reason.
- **Cleansing shrines**, and any second sink for Essence. GD §10.1 names shrines and nothing owns one.
- **Famine's −40 % on income, and any Ordeal touching a price.** [M6-06b](M6-06b-four-ordeals-and-two-refusals.md)'s.
- **Un-banishing.** GD §13.3 says *permanently*, and CH §7 deleted respec.
- **The Emberwright's 5-Veilrot instant cast.** [M6-07c](M6-07c-what-each-class-does-with-the-veil.md)'s, which also takes the Oathbound's half-price Cleanse against this task's `PriceOf`. It needs `Veilrot.Spend` rather than
  `Cleanse` — [M6-04](M6-04-veilrot-thresholds-and-the-claiming.md) rule 3.

## As built

**Built as specced in shape.** `SanctumSpec` last on `ModeSpec`, Descent at 25/40/40/30/60/15;
`SanctumShop` prices, refuses and delivers; `SkillTree` carries a third flag that `Check` closes on,
so `OfferGenerator` is untouched; `LevelUpFlow.Open` spends a banked charge on the first draw that
finds something; `RunRecorder` writes the two counters and the banishes. 2 630 → **2 675, +45**.

### Deviations

1. **The owner's ruling, outside the table: `AC_Player.controller` gains a `Cast` trigger**, added
   through `AnimatorController.AddParameter` (six YAML lines). **Parameter only**: the controller
   has no cast state and nothing under `Animation/` is a cast clip. KayKit's `Rig_Medium_CombatRanged`
   ships `Ranged_Magic_Spellcasting`, `_Shoot` and `_Summon`, unwired because picking one is an art
   call. `PlayerAnimatorViewTests.Animator_EveryParameterItSetsExistsOnTheShippedController` sweeps
   the view's `_…Id` hashes against the shipped asset. The fixture's hand-built double had carried
   `Cast` since M3-06, which is why no row ever caught the gap.
2. **`RunSession` refuses a purchase outside the Sanctum.** `CanBuy` is false and `Buy`/`Banish`
   throw unless `IsSanctumOpen`. AR §18.1's boundary row rests on it: the snapshot is taken on the
   `Clear` edge, so a Sanctum purchase is rolled back by a kill before the next clear, while a
   mid-stage purchase would be kept. The row gains the clause; `Port_BuysOnlyInTheSanctum` pins it.
3. **`SanctumShop.Restore` is public**, for `SkillTree.Restore`'s reason: `Soulvail.Tests.Core` has no
   `InternalsVisibleTo`. `LevelUpFlow.GrantReroll` stays internal and its rows reach it by
   reflection, as `Grant` reaches `GrantOverflow`. `RestoreRerolls` and two reads are beside it.
4. **`SkillTree` gains `CanBanish` and `Banishable(span)`**, public and outside the API block. The
   shop must ask the tree's refusal before the wallet moves (rule 8's order) and walk tree order,
   which is private to the tree. `Available`'s short-buffer refusal became `RequireWholeTree`.
5. **The enum guard is a range check, not `Enum.IsDefined`**, which boxes; `Shop_CanBuyAllocatesNothing` would fail.
6. **`Shop_RefusesWhatCannotBeAfforded` banks 24, not 39.** 39 affords the 25 reroll, so the row as
   written failed once; 24 is the largest balance that refuses all four.
7. **`DebugOverlay`, outside the table**: F5–F8 buy Reroll / Banish / Heal / Cleanse while the shop is
   open, asking `CanBuy` first and logging a refusal. F6 banishes the first card of the last offer.
   The line gains `rr bought/spent` and `ban n`. It makes manual steps 1–3 playable, and goes with M6-03a.
8. **`RunState` gains `TreeNodeCount`**, the size a `BanishableInto` buffer must be. It also gains
   `SanctumPriceOf`, `CanBuySanctum`, `RerollsBought/Spent` and `IsNodeBanished`, narrow reads under
   an `internal Shop`; the narrow-read row now asserts `Shop` is not public.
9. **Where rows live.** The five `Reroll_` draw rows are in `LevelUpFlowTests`, `Banish_IsNeverOffered`
   in `OfferGeneratorTests`, and the `Restore_`/`Recorder_` rows in `RunSessionResumeTests`, whose
   mode now carries Descent's shop. Added beyond the table: `Banish_SurvivesASplash`,
   `Banish_RefusesToBeTaken`, `Port_BuysOnlyInTheSanctum`, and the guard rows.

### Findings

- **Manual step 3 cannot pass as written.** *"Buy a reroll, quit to the menu, Continue"* rolls the
  purchase back, Essence included, because the file was written on the `Clear` edge before the
  shop (AR §18.1). That is the invariant working, not a defect. The step that exercises rule 9 is:
  buy in the Sanctum, clear the **next** stage, then quit and Continue.
- **`BootSmokeTests` with a `run.json` on disk: PlayMode 21 / 0 / 0, zero warnings.** It was
  run against a v4 Gravecaller run written through `LocalJsonSaveStore`, with Exhume taken, one
  reroll banked and one node banished. Continue resumed it, and the opening rewrite carried
  `rerollsBought: 1` and the banish back out. The file was deleted afterwards; the disk had none before.
