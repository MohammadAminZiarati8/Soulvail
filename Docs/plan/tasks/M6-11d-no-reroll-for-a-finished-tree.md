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

**Rules 1–3 as written.** `SanctumShop.CanBuy` still refuses a price it cannot pay first, then asks
`Reroll or Banish => AnythingInThePool()`: taken plus banished short of `TreeRules.Count`, which
counts a borrowed branch once one is installed. `Buy(Reroll)` inherits the refusal because it asks
`CanBuy`. `SanctumPresenter.Detail` gains a Reroll arm, so an affordable Reroll refused for worth
draws `ui.sanctum.refused.complete`, *"Nothing left to offer"*. A short one still draws
`refused.short`. `Pseudo.asset` was regenerated from the menu, and the diff is the one row.

**Before building: M6-11a, b and c had never been opened as PRs.** The kickoff said M6-11c was
merged; `origin/dev` was still M6-11's merge. On the owner's green light they were opened and merged
as #158–#160, in order, and this branch was cut from the result.

**Three deviations.** None changes a decision.

*1. A fifth row, for rule 2.* The Tests table gave rule 2 no row, and rules ↔ rows are one to one.
`Reroll_ABankedChargeOutlivesTheTree` buys a Reroll with one node left, then takes that node. The next
purchase is refused, the charge is still banked, and nothing is refunded. The row's first draft
bought with its whole balance, so the refusal it asserted was the wallet's. It stayed green against
the bug in red check A, which ran 63 / 3 where four were expected. Funded with 500, it goes red with
the other three.

*2. `SanctumPresenterTests.cs` is outside the Files table.* The spec's own fourth row lives there.
`ScreenKeys` grows to eighteen, so `Strings_EveryKeyThisScreenDrawsHasARow` holds the new English
row.

*3. `AnythingBanishable` is renamed `AnythingInThePool`* — one predicate read by two services, and
the old name would have made the Reroll arm read as a banish. The two comments that stated the
exemption as design — `CanBuy`'s *"a charge is never wasted"* and `Detail`'s *"Reroll has none"* —
were corrected in files the table names.

**Stated cost: a node left is not a node reachable.** An Upgrade whose parent was banished, or a
Keystone its branch can no longer reach, stays in the pool and can never be offered. A run whose
untaken nodes are all of that kind is still sold a Reroll. Closing it means asking the tree's gating
forward in time. The remark on `AnythingInThePool` says so. It has no owner →
[parking lot](../ROADMAP.md#parking-lot). **The borrowed branch is not a gap:** a resumed run that
re-asks CH §5.4's choice does so on the first frame, before any Sanctum opens.

**Found on the way.** The ROADMAP's M6 paragraph still said *"a and b are merged, three are open"*.
M6-11c's handover missed it, and it is corrected here.

**Rules ↔ rows.** 1: `Reroll_RefusedWhenTheTreeIsExhausted`, `Reroll_RefusedWhenTheRestIsBanished`,
`Reroll_StillSoldWithOneNodeLeft`. 2: `Reroll_ABankedChargeOutlivesTheTree`. 3:
`Sanctum_AFinishedTreeSaysWhy`, which asserts the key through the passthrough localizer and the
English words through the shipped table.

**Red checks, on the Editor, over the two Sanctum fixtures.** *A* — `Reroll => true` restored in
core: **62 / 4**, exactly the four refusal rows. `StillSoldWithOneNodeLeft` stays green, as it must.
*B* — core fixed and the presenter's Reroll arm removed: **65 / 1**, `Sanctum_AFinishedTreeSaysWhy`
alone, reading `ui.sanctum.refused.short` where `.complete` was expected. Both were restored and
recompiled before the final pass.

**Verified:** **3 159 EditMode / 0 / 0** (+5 on M6-11c's 3 154) on the final code. Of two passes
before the row fix, one was 3 159 / 0 and one 3 158 / 1 on `SeededRandomTests.Capture_AllocatesNothing`
— the allocation flake M3-01a saw once, in code this task does not touch. Then **PlayMode 26 / 0 / 0**
on the first pass. Console after the EditMode pass: 16 errors and 30 warnings, M6-11c's count, each
from a passing negative-path row. Zero new analyzer warnings; `dotnet format` green over four C#
files; `TimeManager.asset` re-serialised and was reverted.
