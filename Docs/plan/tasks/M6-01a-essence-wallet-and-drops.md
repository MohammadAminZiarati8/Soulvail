# M6-01a — The Essence wallet, and the one event that fills it

**Size:** S · **Depends on:** — · **Branch:** `m6-01a-essence-wallet`
**Design refs:** GD §7.1, §13.3, §15; AR §6, §8, §10.1, §14, §18.2, §18.3; ADR-0006, ADR-0008 · **Ledger rows:** none — [row 1](../ROADMAP.md#carry-forward-into-m6) gains one device question in *Manual verification* and is not answered here

## Goal

A run carries a number that goes up when a stage is cleared, and nothing can spend it yet.

## Why this is a task and not part of M6-02

The shop is four services, a sixth stage phase and a screen; the wallet is one number, one event and
one authored block. They are reviewed against different documents — GD §15's economy table against
GD §13.3's shop — and the wallet is the half that ships **used by nothing**, which is M4-01a's
bargain and what keeps this PR's review about the arithmetic. It is also what lets
[M6-01b](M6-01b-save-format-v4.md) bump the save format with a field that already has a writer.

## What GD §15 says, priced against what the build has

| Source | GD §15 | Ships here |
|---|---|---|
| **Stage clear at depth *n*** | `20 + 4·n` | **Yes** — rule 4, at the `Clear` edge |
| **Per boss** | `+60` | **Yes** — rule 4, on the same edge, because a boss stage *is* a stage clear |
| **Per Elite** | `+15` | **No payer.** Elites are **M7-02**; the number is authored anyway (rule 2) so the asset is complete and the day an Elite dies it is one call site, not a content change |
| **Famine's −40 %** | GD §13.4 | **No.** [M6-06b](M6-06b-four-ordeals-and-two-refusals.md) multiplies at the award site; a wallet that knew about Ordeals would be the Ordeal system in the wrong file |

Cumulative, so the prices in GD §13.3 can be read against something: a stage-1 clear pays **24**, and
a run that reaches stage 10 has been paid **540** — `Σ(20 + 4n)` for *n* = 1…10 is 420, plus two
bosses at 60. Against a 40-Essence Heal that is thirteen purchases over ten stages, and the first
Reroll at 25 is **one Essence out of reach** after stage 1. Nothing is tuned here; the numbers are
GD §15's, and this paragraph exists so that the first person to play it knows what the design
intended rather than inferring it from a balance they did not like.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Run/EssenceWallet.cs` | Core | The number, the two verbs, and the predicate a screen reads before spending |
| `Core/Events/EconomyEvents.cs` | Core | `EssenceChanged`, and the file [M6-02b](M6-02b-four-things-essence-buys.md) adds its own to |
| `Tests/Core/Run/EssenceWalletTests.cs` | Tests.Core | The wallet, the authored block, and the award at the edge |
| *small edits* | Core, Game | `Core/Content/ModeSpec.cs` — `EssenceSpec` beside `OverflowSpec`, and `ModeSpec.Essence` (rule 2); `Game/Authoring/ModeDefinition.cs` — an `Essence` foldout beside the `Overflow` one; `Data/Modes/Descent.asset` — 20 / 4 / 15 / 60; `Core/Run/RunState.cs` — the `Essence` read (rule 6); `Core/Run/RunSession.cs` — builds the wallet and hands it to the flow; `Core/Stage/StageFlow.cs` — a required `EssenceWallet` and one award in `EnterClear` (rules 4, 5) |
| *ripple* | Tests.Core, Tests.Game | **11 `new StageFlow(...)` sites across 2 files** — `StageFlowTests` and `RunSession` — compiler-guided (rule 5); `ModeSpecTests` and `ModeDefinitionTests` gain the block; `ContentValidationTests` gains `EveryShippedMode_PricesItsEssence` (rule 3) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Content/ModeSpec.cs — a new readonly struct beside OverflowSpec, in the same file, for the
// reason that file already gives: a mode's authored blocks are read together.
namespace Soulvail.Core.Content;

/// <summary>
/// What a run is paid, in Essence, for getting through things. GD §15's income table as authored
/// data (ADR-0006).
/// </summary>
public readonly struct EssenceSpec
{
    /// <exception cref="ArgumentOutOfRangeException">Any value is negative.</exception>
    public EssenceSpec(int perStageBase, int perStageDepth, int perElite, int perBoss);

    /// <summary>The flat half of a stage clear. 20 in Descent.</summary>
    public int PerStageBase { get; }

    /// <summary>What each stage of depth adds to it. 4 in Descent.</summary>
    public int PerStageDepth { get; }

    /// <summary>What one Elite is worth. 15, and nothing pays it until M7-02.</summary>
    public int PerElite { get; }

    /// <summary>What clearing a boss stage adds on top of the stage itself. 60.</summary>
    public int PerBoss { get; }

    /// <summary>GD §15's formula, in the one place it is written — rule 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    public int ForStageClear(int stage, bool bossStage);
}

public sealed class ModeSpec
{
    // ... the eleven arguments that exist, then, optional and last (rule 2):
    //     EssenceSpec essence = default

    /// <summary>What this mode pays. All zeroes for a mode that authors none — rule 2.</summary>
    public EssenceSpec Essence { get; }
}
```

```csharp
// Core/Run/EssenceWallet.cs
namespace Soulvail.Core.Run;

/// <summary>
/// One run's Essence: what it has been paid and what it has left. GD §15's second currency, and the
/// only one that is spent inside the run it was earned in.
/// </summary>
public sealed class EssenceWallet
{
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    public EssenceWallet(IDomainEvents events);

    /// <summary>What the player has, never negative.</summary>
    public int Balance { get; }

    /// <summary>Pays the run. Zero and negative do nothing and publish nothing — rule 1.</summary>
    public void Earn(int amount);

    /// <summary>
    /// Whether <paramref name="cost"/> can be paid right now. **The predicate a screen reads before
    /// it draws a button** — rule 7, M5-08a's lesson.
    /// </summary>
    public bool CanAfford(int cost);

    /// <summary>
    /// Takes <paramref name="cost"/> out. The invariant behind <see cref="CanAfford"/>, not an
    /// alternative to it — rule 7.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cost"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">The balance is short.</exception>
    public void Spend(int cost);

    /// <summary>What a resumed run comes back holding. M6-01b's; no event — rule 8.</summary>
    internal void Restore(int balance);
}
```

```csharp
// Core/Events/EconomyEvents.cs
namespace Soulvail.Core.Events;

/// <summary>
/// The wallet moved. Carries both numbers because a float-up wants the delta and a readout wants
/// the balance, and neither can be derived from the other without the reader keeping state.
/// </summary>
public readonly struct EssenceChanged
{
    public EssenceChanged(int balance, int delta);

    public readonly int Balance;

    /// <summary>Positive for a payment, negative for a purchase. Never zero — rule 1.</summary>
    public readonly int Delta;
}
```

## Behaviour

1. **`Earn` is silent for anything that is not a gain, and `EssenceChanged` is never published with
   a zero delta.** `Earn(0)`, `Earn(-5)` and a `Spend(0)` all leave the balance where it was and
   publish nothing — `Health.Heal`'s rule and for its reason: a reader that had to filter zeroes
   would be a reader that could forget to. The event carries **no reason field.** AR §8 asks events
   to describe rather than duplicate, and every payment in this task is published on the same tick
   as the `StageCleared` that caused it; every *spend* in [M6-02b](M6-02b-four-things-essence-buys.md) is
   published on the same tick as its own purchase event. A reason enum here would be a third way of
   saying what two events already say, and it would need a member per future source.
2. **The four numbers are authored on the mode, optional and last, and an unauthored mode pays
   nothing.** `OverflowSpec`'s shape exactly (M5-06b), including the cost: `new ModeSpec(...)` has
   **63 call sites across 44 files**, so placing `essence` anywhere but last would move all of them
   for a block only the shipped asset fills. `EssenceSpec.ForStageClear` is where GD §15's formula
   lives — not on the wallet, which knows nothing about depth, and not at the call site, which is
   where a second copy would start. `PerElite` is authored with no payer, and that is deliberate:
   the asset is what a designer reads, and a blank there would read as *"Elites pay nothing"* rather
   than *"nothing is an Elite yet"*.
3. **A shipped mode that prices nothing is a content failure, not a quiet zero.** Rule 2's default
   is what keeps 63 fixtures compiling; `ContentValidationTests.EveryShippedMode_PricesItsEssence`
   is what stops it reaching a build. It asserts every `ModeDefinition` under `Data/` authors a
   positive `PerStageBase` and a positive `PerBoss` — the two terms something in this build actually
   pays — and it is the same bargain M5-06b's `Overflow_ComesFromTheMode` made one milestone ago.
4. **The award is one call, on the edge into `Clear`, and a boss stage is a stage clear that pays
   more.** `StageFlow.EnterClear` already knows the stage and already asks the director questions;
   it gains `_essence.Earn(_mode.Essence.ForStageClear(Stage, _director.IsBossStage))` **above** the
   `StageCleared` publish, so a reader handling that event sees a wallet that has already been paid.
   `SpawnDirector.IsBossStage` is the existing read and no `stage % 5` is computed anywhere (M4-01b).
   **It fires once per stage**, because `EnterClear` is an edge: the flow parks in `Clear` for
   `ClearTime` and `Enter` is called once — the same property M2-14a rule 1's boundary write already
   depends on.
5. **`StageFlow`'s wallet is a required constructor argument, not an optional one.** Every run has a
   wallet, unlike the `MinionSystem` M5-04b made optional for a class that raises nothing. An
   optional wallet defaulting to null would make a mis-wired run clear stages and be paid nothing,
   with no throw and no log — the exact failure M5-06a's *As built* deviation 2 refused for
   `MinionRecipe`, and it costs **11 call sites across 2 files** to refuse it here.
6. **`RunState.Essence` is a scalar read and the wallet is not handed out** (AR §18.2). `Earn` and
   `Spend` are both public on it, so a public handle would let a view pay itself for a stage it did
   not clear. One read, for the HUD [M6-03b](M6-03b-the-meter-on-the-right-edge.md) draws and the debug overlay
   before it — `RunState.PlayerHp`'s bargain, thirteen reads on.
7. **`CanAfford` is the predicate and `Spend` is the invariant behind it, and that pairing is
   M5-08a's lesson made structural.** That task's finding was that *a screen may not offer what the
   model refuses*: `RequireInstallable` was correct, atomic and well tested, and the only fault was
   that nothing asked it before the player committed. So the refusal ships **as a question a caller
   can ask** from the first line of the economy rather than as an exception discovered on a phone.
   `Spend` still throws — the invariant survives — and [M6-03a](M6-03a-the-sanctum-screen.md) rule 3 is
   what makes a button that cannot be afforded undrawable rather than unhappy.
8. **`Restore` is `internal`, publishes nothing, and exists for a task that has not landed.**
   `LevelTracker.Restore` and `SkillRunner.Restore`'s shape (M3-01b rule 7, M3-07b rule 6): a resume
   is not news, and a `EssenceChanged` published during `RunSession.Start` would reach a HUD that has
   not subscribed yet. It is written here rather than at [M6-01b](M6-01b-save-format-v4.md) because
   the alternative is that task editing this file for one method.
9. **Nothing here allocates.** An `int` field, a comparison, and a `readonly struct` published by
   value. `Earn` on a tick with no stage clear is not called at all.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Wallet_StartsEmpty` | a new wallet / — / `Balance` 0, nothing published |
| `Wallet_EarnAdds` | `Earn(24)` / — / `Balance` 24, one `EssenceChanged` carrying **24 and +24** |
| `Wallet_EarnIsSilentForNothing` | `Earn(0)`, `Earn(-5)` / — / balance unmoved, **nothing published** — rule 1 |
| `Wallet_SpendTakes` | 100 banked / `Spend(40)` / `Balance` 60, one event carrying **60 and −40** |
| `Wallet_CanAffordAnswersBeforeSpending` | 39 banked / `CanAfford(40)`, `CanAfford(39)`, `CanAfford(0)` / false, true, true — rule 7 |
| `Wallet_SpendingWhatIsNotThereThrows` | 39 banked / `Spend(40)` / `InvalidOperationException`; the balance is **still 39** and nothing is published — rule 7 |
| `Wallet_SpendRefusesANegative` | any balance / `Spend(-1)` / `ArgumentOutOfRangeException` — a refund is not a purchase |
| `Wallet_SpendingEverythingIsLegal` | 40 banked / `Spend(40)` / `Balance` 0, one event |
| `Wallet_RestoreIsSilent` | a fresh wallet / `Restore(317)` / `Balance` 317 and **nothing published** — rule 8 |
| `Wallet_AllocatesNothing` | 100 000 earn-and-spend cycles / `AllocationAssert.None` / zero — rule 9 |
| `Essence_ForStageClearIsGdFifteen` | Descent's block / stages 1, 5, 10, 30, boss and not / **24, 40, 60, 140** ordinary and **+60** each on a boss stage — rule 2 |
| `Essence_RefusesANegativeNumber` | each of the four, negative / constructed / throws, naming the field |
| `Essence_RefusesAStageBelowOne` | `ForStageClear(0, false)` / — / throws — a stage-zero payment is a hand-edited save reaching the economy |
| `Essence_DefaultPaysNothing` | `default(EssenceSpec)` / `ForStageClear(9, true)` / **0**, no throw — rule 2's stated cost |
| `Mode_CarriesTheAuthoredBlock` | `Descent.asset` converted / — / 20, 4, 15, 60 |
| `Mode_WithoutABlockIsUnchanged` | a `ModeSpec` built without `essence` / — / `Essence` is all zeroes and every other property is what it was — rule 2 |
| `Content_EveryShippedModePricesItsEssence` | every `ModeDefinition` under `Data/` / converted / `PerStageBase` and `PerBoss` are both above zero — rule 3 |
| `Stage_ClearPaysOnce` | a run cleared at stage 3 / ticked through `Clear` for 3 s / **one** `EssenceChanged` of +32, on the frame the phase changed — rule 4 |
| `Stage_ABossStagePaysTheBossTerm` | stage 5 with a boss roster / cleared / +**100** (20 + 20 + 60), and the ordinary stage 6 pays +44 — rule 4 |
| `Stage_ThePaymentPrecedesTheEvent` | a subscriber recording the balance inside `StageCleared` / a clear / it reads the **paid** balance — rule 4 |
| `Stage_AModeThatPricesNothingPaysNothing` | a flow over a mode with no block / cleared / `Balance` 0, nothing published, no throw |
| `Stage_RefusesANullWallet` | `new StageFlow(..., essence: null)` / — / `ArgumentNullException` — rule 5 |
| `Run_TheWalletIsReadableAndNotReachable` | a live `RunSession` / — / `RunState.Essence` tracks the wallet, and `RunState` exposes no `EssenceWallet` — rule 6, asserted by reflection over the type's public surface |
| `Run_ClearingStagesAccumulates` | a session driven through stages 1, 2 and 3 / — / `RunState.Essence` is 24, then 52, then 84 |

**Guard rows are implied, not listed:** a null `IDomainEvents` to the wallet, and `Enum.IsDefined`
where there is an enum to check — there is not one here, which is rule 1's other half.

## Manual verification (Editor / device)

1. **[Editor]** Play a run to stage 3 with the debug overlay up. The Essence readout reads 0, 24,
   52, 84 — and the step *changes* at each depth, which is the only thing that distinguishes GD
   §15's formula from a flat payment by looking at it.
2. **[device]** *Nothing, and that is the point:* there is no new drawn element. The Sanctum screen's
   readability is [M6-03a](M6-03a-the-sanctum-screen.md)'s row on
   [ledger row 1](../ROADMAP.md#carry-forward-into-m6).

## Out of scope

- **Anything that spends it.** [M6-02b](M6-02b-four-things-essence-buys.md).
- **Drawing it.** [M6-03a](M6-03a-the-sanctum-screen.md) draws the balance and every price in the
  Sanctum and [M6-03b](M6-03b-the-meter-on-the-right-edge.md) puts GD §16.1's counter in the
  top-right corner; the debug overlay is what reads it until then.
- **Persisting it.** [M6-01b](M6-01b-save-format-v4.md), which is the milestone's one format bump.
  A run killed between this task and that one comes back with an empty wallet, which is the same
  thing that happens to a run's cooldowns today.
- **The Elite term paying anybody.** M7-02 authors Elites; rule 2 authors the number.
- **Famine, and any multiplier on an award.** [M6-06b](M6-06b-four-ordeals-and-two-refusals.md)
  wraps rule 4's call; a wallet that knew about Ordeals would be the Ordeal system in the wrong file.
- **Moving `ShardPayout.PerStage` and `PerBoss` onto the mode.** The
  [parking-lot line](../ROADMAP.md#parking-lot) names M6-02 as the promoter and this task is the one
  that opens `ModeDefinition` for an economy block, so the question was asked here and **answered
  no** — see that line, which now carries the count. Rule 2's optional-and-last argument defaults to
  zero, so moving two numbers that every existing fixture asserts would either re-author 63 call
  sites or need an `EssenceSpec.Default` in code — which is the second home for a number M5-06b
  rule 10 refused. The line is re-aimed at **M8-05**, the first task that wants to *retune* the
  payout and would pay the cost for a reason.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
