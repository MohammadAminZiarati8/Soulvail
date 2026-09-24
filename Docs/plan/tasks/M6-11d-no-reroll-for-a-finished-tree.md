# M6-11d — The Sanctum does not sell a reroll a finished tree can never spend

**Size:** S · **Depends on:** M6-11 · **Branch:** `m6-11d-no-reroll-for-a-finished-tree`
**Design refs:** GD §13.3 · **Ledger rows:** [M7 row 7](../ROADMAP.md#carry-forward-into-m7)

## Goal

Reroll is refused, with a reason, once no offer can ever come again — M6-02b rule 7's *"refuses what
is worthless"* applied to the one service it exempted.

## Why this is a bug and how it was found

[M6-02b](M6-02b-four-things-essence-buys.md) rule 7 exempted Reroll from the worth test on one
premise: *"a charge is never wasted, because it is spent by whatever offer comes next."* **An
exhausted tree has no next offer** — every level is Overflow — and M6-02b's own
`Reroll_OnAnOverflowLevelSpendsNothing` pins that the charge then sits unspent. In
[M6-11](M6-11-acceptance-and-tag.md)'s stage-37 Sanctum the owner bought **five rerolls for 775
Essence** with a full tree; none can ever be used. The screen offered what the model knew was worthless,
which is M5-08a's defect one service over.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Progression/SanctumShop.cs` | Core | `CanBuy(Reroll)` asks the same question `AnythingBanishable` asks |
| `Tests/Core/Progression/SanctumShopTests.cs` | Tests.Core | Rules 1–2 |
| *small edits* | Game | `Game/Presentation/SanctumPresenter.cs` — a Reroll refused for worth draws `ui.sanctum.refused.complete`; `Data/Localisation/English.asset` one row; `Pseudo.asset` regenerated (*Soulvail ▸ Localisation ▸ Regenerate Pseudo-locale*) |

## Behaviour

1. **`CanBuy(Reroll)` is false when no node is left to offer** — taken plus banished covers the run's
   tree, the predicate `AnythingBanishable` already computes. Affordability is still checked first.
2. **A charge already banked stays banked** and is still spent only by a draw; this task refuses the
   next purchase, it does not refund the last.
3. **The row says why**: *"Nothing left to offer"*, where a refused-for-price row says *"Not enough
   Essence"*.

## Tests

| Test | Given / When / Then |
|---|---|
| `Reroll_RefusedWhenTheTreeIsExhausted` | every node taken, Essence 500 / `CanBuy(Reroll)` / false, and `Buy` throws |
| `Reroll_RefusedWhenTheRestIsBanished` | two untaken, both banished / `CanBuy(Reroll)` / false |
| `Reroll_StillSoldWithOneNodeLeft` | one untaken, unbanished / `CanBuy(Reroll)` / true |
| `Sanctum_AFinishedTreeSaysWhy` | an exhausted tree / the screen draws / the Reroll row is dead with `ui.sanctum.refused.complete` |

## Out of scope

- **Giving the Sanctum something to sell after the tree fills.** From stage ~13 three of four rows
  are refused and Essence piles up to 2 400 by stage 30 — that is [M7 row 9](../ROADMAP.md#carry-forward-into-m7),
  a design question, not this defect.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
