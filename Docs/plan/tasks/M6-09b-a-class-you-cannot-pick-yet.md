# M6-09b — A class you cannot pick yet, and the first Shard anyone has ever spent

**Size:** S · **Depends on:** M6-09a · **Branch:** `m6-09b-class-unlock-screen`
**Design refs:** GD §14.1, §14.2, §14.3, §16.4; CH §3, §5.1; AR §8, §18.2 · **Ledger rows:** [1](../ROADMAP.md#carry-forward-into-m6) — one device row; [7](../ROADMAP.md#carry-forward-into-m6) — four strings

## Goal

A locked class is drawn with its price and its proof rather than hidden, the balance is on the
screen, and tapping a price spends Shards — which nothing in this game has ever done.

## Why this is separate from M6-09a

[M6-03a](M6-03a-the-sanctum-screen.md)'s seam, one screen over: **a model that decides** against
**a screen that draws the decision.** `ClassUnlocks` answers `IsUnlocked`, `CanBuy` and `Earned`, and
nothing on this screen prices a class or decides whether it may be had. What this task adds is that
the answer is *drawn before the player commits*, which is
[M5-08a](M5-08a-splash-offers-what-install-refuses.md)'s finding in as many words: **a screen may not
offer what the model refuses.** Counted, the two together are `SaveDtos`, `ClassUnlocks`,
`ShardPayout`, a core fixture, `ClassCard`, `ClassSelectPresenter` and a presenter fixture — **seven**.

## What GD §14.2 sells, and what this screen can draw

| Class | Price | Deed | Drawn as |
|---|---|---|---|
| **Oathbound** | — | — | a live card, exactly as it is today |
| **Gravecaller** | 2 000 | *kill Choirmother* — **M7-03**, so it never fires ([M6-09a](M6-09a-profile-v4-and-what-a-shard-buys.md) rule 4) | a live card if owned; otherwise a price, and **no deed line** |
| **Emberwright** | 3 500 | *reach stage 20* | a live card if owned; otherwise a price **and** a deed line |

**The deed line is drawn only where the deed can be done**, which is rule 4 and the sharpest version
of M5-08a's lesson in this milestone: telling a player *"or kill the Choirmother"* about a boss that
does not exist is offering something the model refuses, in prose, where no guard can catch it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Controls/ClassCard.cs` | Game | **Substantial.** A card that is owned, a card that is priced, and a card that can only be earned |
| `Game/Presentation/ClassSelectPresenter.cs` | Game | **Substantial.** The balance, the gate, the purchase, and the redraw |
| `Tests/Game/Presentation/ClassSelectPresenterTests.cs` | Tests.Game | **Substantial.** The three states, the two taps, and the refusals |
| *small edits* | Game | `Game/Adapters/ProfileStore.cs` — `Unlock`, the one writer (rule 6); `Prefabs/UI/ClassSelect.prefab` — a balance label and a lock line per card; `Data/Localisation/English.asset` — four rows |
| *ripple* | Tests.Game | `ClassCardTests` gains the two locked states; `ProfileStoreTests` gains the purchase and its refusal |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Game/Controls/ClassCard.cs — block namespace (Traps §5).
namespace Soulvail.Game.Controls
{
    public sealed class ClassCard : MonoBehaviour
    {
        /// <summary>
        /// Draws <paramref name="spec"/> as a class the player owns — unchanged, and still the
        /// only overload that arms <c>onChosen</c>.
        /// </summary>
        public void Bind(CharacterSpec spec, ILocalizer localizer, Action<ContentId> onChosen);

        /// <summary>
        /// Draws <paramref name="spec"/> as a class the player does not own: its numbers, its
        /// price, what proves it, and a button that buys rather than one that plays — rule 2.
        /// </summary>
        /// <param name="affordable">
        /// Whether the price can be paid right now — <c>ClassUnlocks.CanBuy</c> and nothing else.
        /// </param>
        /// <param name="deed">
        /// The one line that says what else would earn it, or <c>default</c> for a class no deed can
        /// reach — rule 4.
        /// </param>
        /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
        public void BindLocked(
            CharacterSpec spec, int price, bool affordable, LocKey deed,
            ILocalizer localizer, Action<ContentId> onUnlockTapped);

        /// <summary>Which of the three states this card was last drawn in.</summary>
        public ClassCardState State { get; }

        /// <summary>Whether its button is live — owned, or locked and affordable (rule 3).</summary>
        public bool IsInteractable { get; }
    }

    /// <summary>How a card was last drawn. Three members, and the third is what M5-08a is about.</summary>
    public enum ClassCardState
    {
        /// <summary>Not in use — <c>Clear</c>'s state.</summary>
        Hidden,

        /// <summary>Owned. Tapping starts a run.</summary>
        Owned,

        /// <summary>Priced. Tapping buys it, or does nothing when the balance is short.</summary>
        Locked,
    }
}
```

```csharp
// Game/Presentation/ClassSelectPresenter.cs — block namespace (Traps §5).
namespace Soulvail.Game.Presentation
{
    public sealed class ClassSelectPresenter : MonoBehaviour
    {
        /// <summary>What the player has to spend. Read from the profile at every draw — rule 5.</summary>
        public int Shards { get; }

        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        [Inject]
        public void Construct(
            PendingRun pending,
            ContentCatalog catalog,
            SceneLoader loader,
            ILocalizer localizer,
            ProfileStore profile);
    }
}
```

```csharp
// Game/Adapters/ProfileStore.cs — one member, and it is the one writer (rule 6).
public sealed class ProfileStore
{
    /// <summary>
    /// Spends <paramref name="price"/> and adds <paramref name="characterId"/> to the owned list,
    /// then saves. Both fields move or neither does.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>ClassUnlocks.CanBuy</c> is false — the invariant behind the predicate, never an
    /// alternative to it.
    /// </exception>
    public Task Unlock(ContentId characterId, int price);
}
```

## Behaviour

1. **The screen reads the model and owns no economy.** `ClassUnlocks.IsUnlocked` decides which cards
   are owned, `CharacterSpec.Unlock.ShardPrice` is the price, `ClassUnlocks.CanBuy` decides whether a
   tap does anything, and `ProfileStore` holds the balance. Nothing here computes any of it —
   [M6-03a](M6-03a-the-sanctum-screen.md) rule 1's bargain, and the reason a disagreement between
   this screen and the game can only be a missed redraw in this file.
2. **A locked card keeps its name and its three numbers, and changes only the line underneath.**
   `SplashPresenter.DrawBranches`' shape and M5-08a rule 5's reason: reading what you cannot have is
   half of what a storefront is for, and a class hidden until it is paid for is a class nobody knows
   they want. HP, speed and DPS are drawn for all three whatever their state — **which is GD §14.3
   working**, since there is nothing to hide: every class starts every run from the same baseline, so
   a locked card's numbers are the numbers.
3. **A price that cannot be paid is drawn, priced and dead.** `ui.classselect.locked.price` carries
   the figure either way; what changes is `interactable`, which is `CanBuy` and nothing else. **And
   `OnUnlockTapped` re-asks `CanBuy` before sending**, which is M5-08a rule 4's second door and is
   written because the alternative is an `InvalidOperationException` out of a `Button.onClick` — the
   exact defect that task exists to have closed. `ProfileStore.Unlock` still throws: the predicate is
   the gate, the exception stays the invariant.
4. **The deed line is drawn only for a deed this build can do, and that is a ruling rather than an
   omission.** `UnlockSpec.DeedStage` above zero draws `ui.classselect.locked.deed` with the number
   in it; `UnlockSpec.DeedBossId` draws **nothing**, because
   [M6-09a](M6-09a-profile-v4-and-what-a-shard-buys.md) rule 4 established that no boss in the build
   can match it until **M7-03**. A line reading *"or kill the Choirmother"* would be the only thing
   on any screen in this game that promises something the model cannot deliver, and it would be
   invisible to every guard the project has, because prose is not a predicate.
   `Card_ABossDeedDrawsNoLine` is the row and its message names M7-03, so the line arrives the day
   the boss does.
5. **The balance is drawn once per open and once per purchase, and on nothing else.** Nothing in the
   Menu scene can move Shards while this screen is up — `ShardWriter` writes on a death and there is
   no run — so an `Update`-driven poll would be sixty reads a second of a number that changes only
   when this file changes it ([M6-03a](M6-03a-the-sanctum-screen.md) rule 2's argument, with the
   pause replaced by *there is no simulation*). It is drawn in `Palette.Essence` — GD §16.4's warm
   gold for *"Essence, rewards, Gates"* — which is the same colour `RunEndPresenter` already pays the
   player in, so the number the run-end screen showed and the number this screen spends look like the
   same currency because they are.
6. **`ProfileStore` is the one writer and the purchase is atomic.** M3-09c rule 3 and M4-05b rule 2
   gave the profile exactly one holder and one writer, and this is the second feature to test the
   claim from outside: `Unlock` reads, applies **both** `WithShards` and `WithUnlocked`, and saves
   once. A presenter that called two `With` helpers itself would be a second writer, and a save
   between them is an install that paid and owns nothing — which is the failure direction M4-05b
   named.
7. **The purchase redraws every card rather than the one that was bought.** Buying the Emberwright at
   3 500 takes the balance below the Gravecaller's 2 000, so a screen that redrew one card would
   leave the other reading as affordable. `ClassSelect_BuyingOneKillsTheOther` is the row —
   [M6-03a](M6-03a-the-sanctum-screen.md) rule 2's `Sanctum_BuyingRedrawsEveryRow` one screen over.
8. **Buying is not picking, and the card takes two taps to start a run.** The tap that spends Shards
   redraws the card as `Owned` and **does not descend**: a player who has just spent 3 500 on a class
   deserves to look at it before committing to a run, and a single tap that both bought and started
   would make an accidental double tap a purchase and a descent. The `_descending` latch is unchanged
   and still guards the second tap; a **second latch** guards the first, for its own reason — uGUI
   dispatches both taps of a double tap from one `EventSystem` pass, and `ProfileStore.Unlock` is a
   `Task`, so two taps in one pass would send two purchases against one balance.
9. **A class with no `UnlockSpec` is drawn exactly as it is today.** `Bind`'s signature does not
   move, the Oathbound's path through this file is byte-identical, and
   `ClassSelect_TheStarterIsUnchanged` is what says so — because the thing a player will notice first
   about this PR is that the screen they have used since M5-07 now has two dead cards on it.
10. **Nothing here allocates per frame**, because nothing here runs per frame: `Update` clears the
    two latches and does nothing else (`LevelUpPresenter._choosing`'s rule).

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `ClassSelect_TheStarterIsUnchanged` | a profile owning nothing / opened / the Oathbound card is `Owned`, live, and drawn exactly as `ClassCardTests` pinned it at M5-07 — rule 9 |
| `ClassSelect_ALockedClassIsDrawnWithItsNumbers` | the same / opened / the Emberwright card is `Locked`, shows 70 HP, 3.4 m/s and 25.5 DPS, and reads **3 500** — rule 2 |
| `ClassSelect_ALockedClassIsNotTappableToPlay` | the same / tap the Emberwright / **no** `PendingRun` write, no scene load — rule 3 |
| `ClassSelect_APriceThatCannotBePaidIsDead` | 3 499 shards / opened / the Emberwright's button is not interactable and the price is still drawn — rule 3 |
| `ClassSelect_ATapOnADeadCardSendsNothing` | 3 499 shards / `OnUnlockTapped` by hand / no `Unlock`, no throw — rule 3's second door, M5-08a rule 4 |
| `ClassSelect_APriceThatCanBePaidIsLive` | 3 500 shards / opened / interactable |
| `ClassSelect_BuyingSpendsAndOwns` | 3 500 shards / tap / one `Unlock`, balance **0**, the card redraws `Owned` and live — rules 3, 6 |
| `ClassSelect_BuyingDoesNotDescend` | the same / — / `PendingRun` untouched and no scene load; a **second** tap starts the run — rule 8 |
| `ClassSelect_OnePurchasePerFrame` | two taps in one `EventSystem` pass / — / one `Unlock` — rule 8 |
| `ClassSelect_BuyingOneKillsTheOther` | 3 600 shards, both locked / buy the Emberwright / the Gravecaller's card goes dead at 2 000 without being tapped — rule 7 |
| `ClassSelect_DrawsTheBalance` | 650 shards / opened / the label reads 650 in `Palette.Essence` — rule 5 |
| `ClassSelect_TheBalanceIsNotPolled` | opened / 120 frames / one profile read, not 120 — rule 5 |
| `Card_ADepthDeedDrawsItsLine` | the Emberwright locked / — / `ui.classselect.locked.deed` with **20** in it — rule 4 |
| `Card_ABossDeedDrawsNoLine` | the Gravecaller locked / — / the price only, **no** deed line, and the row's message names **M7-03** — rule 4 |
| `Card_AnOwnedCardHasNoPriceOrDeed` | any owned class / — / neither line is drawn |
| `Card_LockedAndOwnedAreTheSameNumbers` | the Emberwright drawn both ways / — / HP, speed and DPS identical — rule 2, GD §14.3 |
| `Card_RepaintsBackToOwned` | a card drawn `Locked`, then `Bind`ed / — / the price and the deed gone, `onChosen` armed, `onUnlockTapped` dropped |
| `Card_ClearIsStillThird` | a three-card prefab and a two-class catalog / opened / one `Hidden` — `ClassCard.Clear`'s rule, unmoved |
| `Store_UnlockSpendsAndOwnsInOneWrite` | 3 500 shards / `Unlock` / **one** save, both fields moved — rule 6 |
| `Store_UnlockRefusesWhatCannotBeBought` | 3 499 shards / `Unlock` / `InvalidOperationException`, and the profile on disk is unmoved — rule 6 |
| `Store_UnlockRefusesWhatIsOwned` | an owned class / `Unlock` / throws, and no Shard is spent twice |
| `Store_UnlockTouchesNothingElse` | a full profile / `Unlock` / haptics, the hint, the archetype set and the locale are unmoved — rule 6 |
| `Prefab_IsDressed` | `ClassSelect.prefab` / loaded / three cards each with a price label and a deed label, and a balance label on the root |
| `Prefab_CarriesNoSerializedColour` | every type on the prefab / reflection / none — `Views_CarryNoSerializedColour`'s rule |
| `Strings_EveryKeyThisScreenDrawsHasARow` | the four keys / `English.asset` / every one resolves — [ledger row 7](../ROADMAP.md#carry-forward-into-m6) |
| `ClassSelect_AllocatesNothingPerFrame` | 10 000 frames with the screen up / `AllocationAssert.None` / zero — rule 10 |

**Guard rows are implied, not listed:** nulls to `Construct`, `Bind` and `BindLocked`; a missing
price or deed label left silent rather than throwing (`SkillBarPresenter`'s bargain); and an empty
catalog still opening a screen the Back button can leave (`Open`'s existing rule).

## Manual verification (Editor / device)

1. **[Editor]** Boot on the owner's own profile. *Expected: three cards — two live, and the
   Emberwright priced at 3 500 and dead. Its HP, speed and DPS are readable anyway.*
2. **[Editor]** Edit `profile.json`'s shard total to 3 500 and reopen. *Expected: the Emberwright's
   button comes alive. Tap it once: the balance falls to 0 and the card becomes an ordinary one. Tap
   it again: the run starts — rule 8.*
3. **[Editor]** Set the balance to 3 600 with both classes locked. *Expected: buy the Emberwright and
   the Gravecaller's card goes dead the same frame — rule 7.*
4. **[Editor]** Look at the Gravecaller's locked card. *Expected: a price and **no** second line,
   where the Emberwright's says *"or reach stage 20"* — rule 4.*
5. **[Editor]** Reach stage 20 and die, then open the screen. *Expected: the Emberwright is owned and
   nothing was spent — [M6-09a](M6-09a-profile-v4-and-what-a-shard-buys.md) rule 4, seen from the
   screen.*
6. **[device]** **[ledger row 1](../ROADMAP.md#carry-forward-into-m6)**: three cards at thumb
   distance where two can be *refused*, and a four-digit price that has to read as a price rather
   than as a broken button. It is [M6-03a](M6-03a-the-sanctum-screen.md) step 5's question on a
   different screen, and it is the first screen in the game a new player sees.

## Out of scope

- **Cosmetics.** GD §14.2's third row; a [parking-lot](../ROADMAP.md#parking-lot) line opened at
  [M6-09a](M6-09a-profile-v4-and-what-a-shard-buys.md).
- **A Shard counter anywhere but here.** The run-end screen already pays in `Palette.Essence`
  (M4-06); a persistent balance on the main menu is a screen decision with no design behind it.
- **An "unlocked!" celebration.** GD §16.3's game-feel checklist is M8-01's, and a toast here would
  be the first animation in this project's UI.
- **Deciding whether a class may be bought.** [M6-09a](M6-09a-profile-v4-and-what-a-shard-buys.md)
  owns both routes and both refusals; this file asks and draws.
- **The Choirmother's deed line.** Rule 4, with the owner.
- **Confirming a 3 500-Shard purchase.** Two taps to *play* is rule 8; a modal *"are you sure"* is a
  third screen for a decision GD §14.3 makes harmless — nothing is lost, and every class is the same
  power.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
