# M7-04a — A Pact on every node, the Actives included

**Size:** S · **Depends on:** M7-03d · **Branch:** `m7-04a-a-pact-on-every-node`
**Design refs:** GD §10.1, §10.3, §13.1, §13.2; CH §3.1, §4.4, §5.4; AR §10.1, §18.1; ADR-0006, ADR-0009, ADR-0012 · **Ledger rows:** [10](../ROADMAP.md#carry-forward-into-m7) — the coverage half, the Active door and the ¾ re-read (rules 1–7); its full-tree half is [M7-04k](M7-04k-a-full-tree-still-tempts.md)'s

## Goal

Every node of the three V1 trees but a Keystone can arrive corrupted, the three Actives included. A
Pact can no longer be offered on a branch its effects would throw on.

## Why the coverage is authoring, and why the Active door is smaller than M6-05a thought

[Row 10](../ROADMAP.md#carry-forward-into-m7) measured *one Pact card in 62 offers*. The roll is not
the problem: `OfferGenerator.PactChance` is 0.25 and `RollPact` lands on a written card, so the rate a
player sees is **0.25 × the share of offered nodes that carry a Pact**. Four nodes of thirty-six carry
one. With every non-Keystone node covered the rate is the roll's own 25 %, which is GD §13.2's
*"one of the three offered nodes may appear as a Pact"* read as the design meant it.

**M6-05a rule 3 refused a Pact on an Active** because *"a corrupted Active is a second
`ActiveSpec`"*, and `SkillSpec`'s constructor still says *"Exhume, Bulwark and Consecrate wait for
M7-04, which builds that door."* Counting the code shows no second `ActiveSpec` is needed.

- **A Pact's effects are take effects**, applied by `SkillTree.Record` instead of the clean list. An
  Active's clean list is empty, so its Pact list is simply what the corrupted Active adds on take.
- **An Active's corrupted form is its own cast with its cooldown cut**:
  `ModifySkillCooldown(itself, PercentMult, …)` and a downside. `ModifySkillCooldownHandler` already
  holds a modifier for a skill the runner does not own yet and spends it in `SkillRunner.Add`
  (M3-12b rule 3).
- **The order already works.** `LevelUpFlow.Choose` takes the node, then calls `_runner.Add(spec)`.
  `RunSession.Start`'s restore block replays the take, then adds the Actives.
- **So the door is one refusal deleted**, not a second cast list.

## What counting found: three sweeps never look at a Pact

A Pact's effects are swept for a handler by `SkillTree.RequireHandlers` (its `"pacts"` pass). **They
are swept for nothing else.** Grepped, all four class-shaped refusals walk `spec.Effects` and
`spec.Active.OnCast` and never `spec.Pact.Effects`:

- `RunSession.RequireNoMinionTarget` and `RunSession.RequireOwnAddresses` — a class's own tree at `Start`;
- `SplashFlow.TryRefusal` and `SplashFlow.RequireInstallable` — a borrowed branch.

Today that is inert. The four shipped Pacts name `WeaponDamage`, `MaxHp`, `MoveSpeed`, `XpGain`,
`FireRate` and `ShieldRechargeDelay`, and every class has those. This task authors thirty-one more,
and the three tree tasks after it author thirty-seven, Knitted Bone's included. A Pact naming `KindlingMaxStacks` or aimed at `Minions`,
on a node whose clean form any class can take, would be offered to a borrower **as a card that throws
from `SkillTree.Take` after the node is recorded**. That is M5-08a's defect arriving a third way.
Rule 5 closes it, and rule 3's authoring rule keeps it from being needed.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Core/Progression/ActivePactTests.cs` | Tests.Core | **New.** An Active taken corrupted, fresh and resumed, and the four sweeps reading a Pact |
| `Tests/Game/Authoring/PactCoverageTests.cs` | Tests.Game | **New.** Coverage asserted, the convention asserted, and the offer rate the coverage buys |
| *small edits* | Core, Game, Data, Docs | `Core/Content/SkillSpec.cs` — the Active refusal deleted, its remarks and `PactSpec`'s rewritten (rule 1); `Core/Run/RunSession.cs` — `RequireNoMinionTarget` and `RequireOwnAddresses` walk `spec.Pact.Effects` when `HasPact` (rule 5); `Core/Progression/SplashFlow.cs` — `TryRefusal(SkillTreeSpec, int, out LocKey)` and `RequireInstallable(SkillTreeSpec, int, string)` the same, with `"pacts"` as the verb (rule 5); `Game/Authoring/SkillDefinition.cs` — the Pact header's tooltip and the `OnValidate` warning on an Active deleted; `Data/Skills/**` — 31 Pact blocks, and Keen Censer's and Zealotry's re-read (rules 3, 4); `Data/Effects/**` — the effects they name (rule 6); `Data/Localisation/English.asset` — 31 Pact descriptions, two rewritten; `Pseudo.asset` regenerated; `Docs/Characters.md` — §3.1's ¾ clause as built, §4.4's *"Any node"* as built |
| *ripple* | Tests.Core, Tests.Game | `PactSpecTests.Skill_RefusesAPactOnAnActive` becomes `Skill_AcceptsAPactOnAnActive`; `ContentValidationTests.Content_EveryPactAsksWithinTheBand` loses its Active clause, and `Content_ReportsItsPactCoverage` is deleted for rule 2's row; `ContentValidationTests.ShippedEffects` raised to what ships; `PactOfferTests`' `PactDamage` constant is a fixture's and stays |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

public sealed class SkillSpec
{
    // Unchanged signature. The Active refusal is deleted: `pact` is legal on every kind (rule 1).
    public SkillSpec(
        ContentId id, LocKey nameKey, LocKey descriptionKey, SkillKind kind,
        IReadOnlyList<IEffect> effects, ActiveSpec active = null,
        ContentId parentId = default, PactSpec pact = null);
}
```

No other member is added. The sweeps in `RunSession` and `SplashFlow` are private and change their
bodies only.

## Behaviour

1. **A Pact is legal on an Active, and its effects are what the corrupted Active adds on take.**
   - `SkillSpec`'s refusal (`kind == SkillKind.Active && pact is not null`) is deleted. Its remarks say
     the door is `ModifySkillCooldownHandler`'s pending table rather than a second `ActiveSpec`.
   - The cast list is the clean node's and is never corrupted. `SkillRunner.ApplyCast` casts
     `Active.OnCast` with the `ActiveSpec` as source whichever form was taken.
   - `LevelUpFlow.Choose` and `RunSession.Start` are unedited. The first applies the Pact on `Take` and
     adds the Active after. The second replays the Pact in `SkillTree.Restore` and adds the Active in
     the loop below it. Both orders land a cooldown Pact through the pending table.
2. **Every node of the three V1 trees carries a Pact except a Keystone.**
   - **This build has no Keystone**, so this task covers **35 of 36**, and the thirty-sixth is the one named exception.
     [M7-04h](M7-04h-the-oathbounds-twenty-seven.md) to [M7-04j](M7-04j-the-emberwrights-twenty-seven.md)
     author their twelve new nodes each with one.
   - **A Keystone carries none.** Its power is a rule rather than an amount — a ring, an inversion, a
     spread — so *"1.8× the budget"* has nothing to multiply. A corrupted rule would be a second
     mechanism per Keystone, for a card offered at most once a run.
   - **Knitted Bone is the one exception, named in the row and expiring at
     [M7-04i](M7-04i-the-gravecallers-twenty-seven.md).** Its clean effect, `MaxHp +50 %` on the
     Wights, is inert: `MinionSystem.ApplyDamage` has no caller, and nothing in V1 hurts a Wight.
     A Pact on an inert number would be a downside for nothing. M7-04i re-aims the node and writes its
     Pact.
   - **The Ranger is excluded**, by name. Its nine nodes are the owner's design of 2026-09-25, GD §19
     puts a fourth class in V2, and nobody asked for its corruption.
3. **The Pact convention, so seventy-two corrupted nodes are one rule rather than seventy-two
   guesses.**

   | Clean node | Pact upside | Pact downside | Rot |
   |---|---|---|---|
   | a number *v* on a stat (any target) | the same stat at **2.7 *v*** — **2.25 *v* for the Oathbound** | one from the list below | **15** if the upside is damage — the weapon's, a minion's, a pool's or Kindling's; **12** otherwise, fire rate included |
   | an Active | `ModifySkillCooldown(itself, PercentMult, −0.40)` — **−0.33 for the Oathbound** | one from the list | **18** |
   | an Upgrade that cuts a cooldown by *c* | the same skill cut by **0.45** — **0.38 for the Oathbound** | one from the list | **12** |
   | `KnockbackOnSwing(d)` | `KnockbackOnSwing(2.25 d)` — the Oathbound's alone | one from the list | **12** |

   - **2.7 is what M6-05a authored for the Gravecaller**: Sharpened Bone and Rot Feast take 0.15 to
     0.40. With its downside that is GD §13.2's ~1.8× budget.
   - **2.25 is CH §3.1's ¾**, ruled at M6-11. The downside is kept and the upside moves: 0.75 × 1.8 =
     1.35 net, and 1.35 plus the downside's ~0.9 is 2.25.
   - **The downside names a number every class has and every class feels**:
     - `MaxHp` — **Flat −25** for the Oathbound, **PercentAdd −0.15** for the Gravecaller, **Flat −10**
       for the Emberwright. Never on a node whose upside is `MaxHp`.
     - `MoveSpeed` — **PercentAdd −0.10**, and **−0.15** for the Gravecaller, which is Sharpened
       Bone's.
     - `MovementSkillCooldown` — **PercentAdd +0.25**.
     - `XpGain` — **PercentAdd −0.20**.
   - **Why no class-shaped downside.** A Pact on a branch another class can borrow must bite the
     borrower too. An Aegis delay is free to a class with no Aegis. A Kindling number would refuse the
     branch under rule 5. Rule 5 is the backstop and this list is why it never fires on shipped
     content.
   - **The price column is the four shipped Pacts read back**: Keen Censer and Sharpened Bone are
     damage at 15, Rot Feast (experience) and Zealotry (fire rate) are 12.
   - **Rounding**, half up. Fractions to two decimals, metres to one, hit points and whole counts to
     the integer, and a heal per kill to one decimal. The convention row accepts ±0.01, ±0.1 and ±1
     respectively, so M6-05a's 0.40 on a 0.15 passes where 2.7 × 0.15 is 0.405.
4. **Keen Censer and Zealotry are re-read against the ¾** — row 10's third clause.
   - **Keen Censer**: `WeaponDamage +0.45, MaxHp −25` becomes **`+0.34, MaxHp −25`**, still 15 Rot.
   - **Zealotry**: `FireRate +0.30, ShieldRechargeDelay +0.50` becomes **`FireRate +0.27, MoveSpeed
     −0.10`**, still 12 Rot. The Aegis delay was a downside a borrower of Judgment never paid,
     which rule 3 refuses.
   - Both descriptions are rewritten.
   - `PactOfferTests.PactDamage` is a fixture's own 0.45 and does not move.
5. **All four class-shaped sweeps read a Pact's effects, with `"pacts"` as the verb in the message.**
   - `RunSession.RequireNoMinionTarget` and `RequireOwnAddresses` refuse the **run** for an own-tree
     Pact that names `Minions` on a minionless class, or an address `PlayerStats.Has` denies.
   - `SplashFlow.TryRefusal` draws the borrowed branch **dead**, with the existing keys:
     `ui.splash.refused.primitive`, `.minions` or `.address`.
   - `SplashFlow.RequireInstallable` throws from `Choose` and `Restore`. **No new key.** The reason a
     player reads is the same one: the branch reaches a number this class does not have.
6. **The thirty-one Pacts this task authors**, to rule 3 — every shipped node without one but Knitted Bone (rule 2). Effects are authored per node:
   `Data/Effects/<Class>/<Node>Pact<What>.asset`, M6-05a's names. Descriptions follow the shipped
   form, *"+40% orb damage, but −10 maximum health."*: one clause for the upside and one for the
   price, the numbers in both.

   | Class | Node | Pact | Rot |
   |---|---|---|---|
   | Oathbound | Tempered Vow | `MaxHp +34` · `MoveSpeed −0.10` | 12 |
   | | Steady Breath | `ShieldRechargeDelay −0.56` · `MaxHp −25` | 12 |
   | | Bulwark *(Active)* | `ModifySkillCooldown(bulwark, −0.33)` · `MaxHp −25` | 18 |
   | | Unbowed | `MaxHp +0.23`, `MoveSpeed +0.7 flat` · `MovementSkillCooldown +0.25` | 12 |
   | | Long Reach | `WeaponRange +3.4` · `MaxHp −25` | 12 |
   | | Broad Censure | `WeaponConeAngle +1.13` · `MoveSpeed −0.10` | 12 |
   | | Crashing Censure | `KnockbackOnSwing(3.4)` · `MovementSkillCooldown +0.25` | 12 |
   | | Consecrate *(Active)* | `ModifySkillCooldown(consecrate, −0.33)` · `MoveSpeed −0.10` | 18 |
   | | Retribution | `HealPerKill +4.5` · `MaxHp −25` | 12 |
   | | Lasting Ground | `ModifySkillCooldown(consecrate, −0.38)` · `MoveSpeed −0.10` | 12 |
   | Gravecaller | Exhume *(Active)* | `ModifySkillCooldown(exhume, −0.40)` · `MaxHp −0.15` | 18 |
   | | Grave Strength | `ContactDamage +0.54` *(Minions)* · `MaxHp −0.15` | 15 |
   | | Deeper Graves | `ModifySkillCooldown(exhume, −0.45)` · `MoveSpeed −0.15` | 12 |
   | | Quick Hands | `FireRate +0.32` · `MoveSpeed −0.15` | 12 |
   | | Marrow Tithe | `HealPerKill +5.4` · `XpGain −0.20` | 12 |
   | | Pale Vigour | `MaxHp +41` · `MoveSpeed −0.15` | 12 |
   | | Shroud Veil | `MovementSkillCooldown −0.54` · `MaxHp −0.15` | 12 |
   | | Withering Step | `MoveSpeed +0.8 flat` · `MaxHp −0.15` | 12 |
   | | Restless Dead | `MoveSpeed +0.81` *(Minions)* · `XpGain −0.20` | 12 |
   | Emberwright | Ember Touch | `WeaponDamage +0.41` · `MaxHp −10` | 15 |
   | | Stoked Coals | `KindlingPerStack +0.03` · `MoveSpeed −0.10` | 15 |
   | | Quickened Flame | `FireRate +0.32` · `MaxHp −10` | 12 |
   | | Long Burn | `KindlingMaxStacks +27` · `MoveSpeed −0.10` | 15 |
   | | Emberfall *(Active)* | `ModifySkillCooldown(emberfall, −0.40)` · `MaxHp −10` | 18 |
   | | Arcane Haste | `MovementSkillCooldown −0.54` · `MaxHp −10` | 12 |
   | | Deep Well | `ModifySkillCooldown(emberfall, −0.45)` · `MoveSpeed −0.10` | 12 |
   | | Ashen Lore | `XpGain +0.41` · `MaxHp −10` | 12 |
   | | Cinder Nova *(Active)* | `ModifySkillCooldown(cinder-nova, −0.40)` · `MovementSkillCooldown +0.25` | 18 |
   | | Scorching Ground | `PoolDamage +1.35` · `MaxHp −10` | 15 |
   | | Lingering Ash | `PoolDuration +1.35` · `MoveSpeed −0.10` | 12 |
   | | Emberheart | `MaxHp +41` · `MoveSpeed −0.10` | 12 |

   - Every stat is `PercentAdd` unless marked *flat*, or unless its clean node is `Flat`: Tempered
     Vow, Pale Vigour, Emberheart, Long Reach, Retribution, Marrow Tithe, Stoked Coals and Long Burn
     scale their own `Flat`.
   - Every cooldown cut is `PercentMult`. Skill ids are abbreviated; the asset names the node.
   - **Stoked Coals rounds to 0.03.** 2.7 × 0.01 is 0.027, and two decimals is the precision the
     clean node is authored at. It is 3× rather than 2.7×, and it sits in Ember, which no other class
     can borrow.
   - **Unbowed's two stats both scale.** The Pact is the clean node made bigger, and the downside
     moves to the dash.
   - Pool numbers are Blink-only and sit in Ash, which rule 5 already refuses to a non-Blink borrower
     through the clean nodes.
7. **The rate the coverage buys is measured in the suite, which is row 10's instrument before a
   phone.**
   - `Offers_APactIsOneOfferInFour` draws 10 000 seeded offers of three from a fully covered tree.
   - It asserts that the share carrying a Pact card is within ±0.02 of `PactChance`.
   - In play, [M7-08](../ROADMAP.md#m7--content-pass) counts offers rolled against Pact cards shown
     and taken.
8. **Nothing here allocates on a tick.** The sweeps run at `Start` and at the splash. The Pacts are
   boot-time specs.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Skill_AcceptsAPactOnAnActive` | *(in `PactSpecTests`)* an Active with a `PactSpec` / construct / no throw, `HasPact` true — rule 1 |
| `ActivePact_ArrivesWithItsCooldownCut` | an offer whose Pact card is an Active whose Pact cuts it 0.40 / `Choose` that card / the runner owns it, `EffectiveCooldownOf` is 0.6 × base, the handler's `PendingCount` is 0 — rule 1 |
| `ActivePact_CastsTheCleanList` | the same Active, taken clean and taken corrupted in two runs / one cast each / the same `OnCast` effects applied with the `ActiveSpec` as source — rule 1 |
| `ActivePact_SurvivesAResume` | a snapshot whose `PactedNodeIds` names the Active / `Start` with it / the cooldown is cut, nothing pending — rule 1, AR §18.1's restore row |
| `ActivePact_PaysItsRot` | the Active taken corrupted / — / `Veilrot.Value` rose by 18 × the class's gain multiplier — rule 3 |
| `Sweep_ARunRefusesAnOwnPactAimedAtMinions` | a minionless class whose own node's Pact names `Minions` / `Start` / `ArgumentException` whose message says `pacts` — rule 5 |
| `Sweep_ARunRefusesAnOwnPactAtAMissingAddress` | an Oathbound whose own node's Pact names `KindlingMaxStacks` / `Start` / refused, naming the node and the stat — rule 5 |
| `Sweep_TheSplashDrawsAPactsBranchDead` | a lender branch whose clean nodes pass and one Pact names `KindlingMaxStacks` / `BranchesOf` for an Oathbound / not borrowable, `RefusedKey` is `ui.splash.refused.address` — rule 5 |
| `Sweep_TheSplashRefusesToInstallIt` | the same branch / `Choose` / `ArgumentException` naming `pacts`, nothing installed — rule 5 |
| `Sweep_ACleanBranchIsStillBorrowable` | a lender branch whose Pacts name only universal addresses / `BranchesOf` / borrowable — rule 5 |
| `Coverage_EveryNodeButAKeystoneCarriesAPact` | *(in `PactCoverageTests`)* the three V1 tree assets / every node converted / every non-Keystone node `HasPact`, except `skill.gravecaller.knitted-bone`, named in the message with M7-04i; the Ranger's tree is not walked — rule 2 |
| `Coverage_TheExceptionIsInert` | Knitted Bone / — / its one effect is `ModifyStat(MaxHp, …, Minions)`, and no production type calls `MinionSystem.ApplyDamage` (reflection over the source) — rule 2's reason, so the exception cannot outlive it |
| `Convention_EveryPactFollowsTheTable` | every Pact on the three trees / against its clean node / the upside's stat and kind match the clean node's, its value is 2.7 × (2.25 × for the Oathbound) within the rounding of rule 3, one downside from the list, the Rot is 15, 12 or 18 by rule 3's column — rule 3 |
| `Convention_NoDownsideIsClassShaped` | every Pact's downside / — / its address is `MaxHp`, `MoveSpeed`, `MovementSkillCooldown` or `XpGain`, aimed at the player — rule 3 |
| `Convention_KeenCenserAndZealotryAreReRead` | the two assets / converted / rule 4's numbers exactly — rule 4 |
| `Convention_EveryPactDescriptionResolves` | the thirty-five Pacts' `DescriptionKey`s / against `English.asset` / present and distinct — rule 6 |
| `Offers_APactIsOneOfferInFour` | *(Tests.Core, in `ActivePactTests`)* a 27-node fixture tree with every node carrying a Pact, 10 000 seeded draws / `OfferGenerator.Draw` / the share with `pactIndex ≥ 0` is 0.25 ± 0.02 — rule 7 |
| `Content_EveryPactAsksWithinTheBand` | *(existing, narrowed)* / — / the band row alone; the Active clause is gone — rule 1 |

**Guard rows are implied, not listed.**

## Manual verification (Editor / device)

1. **[Editor]** Play an Emberwright to level 6. *Expected: a violet card in about one offer of four,
   on any node, Emberfall and Cinder Nova included, each with its own sentence and a Rot price.*
2. **[Editor]** Take a corrupted Emberfall. *Expected: it casts as before and returns 40 % sooner;
   the meter rises by 18.*
3. **[Editor]** Play an Oathbound to the half-tree moment with the Gravecaller owned. *Expected: the
   splash screen draws the same live and dead branches it drew before this task — no shipped Pact
   names a class-shaped number.*
4. **[device]** Thirty-one new Pact sentences against GD §13.1's two-second budget, at 400 dpi. Row
   1's.

## Out of scope

- **A Pact on a Keystone.** Rule 2.
- **What a full tree's level-up offers.** [M7-04k](M7-04k-a-full-tree-still-tempts.md).
- **Tuning `PactChance`, or any Pact's price.** [M8-05](../ROADMAP.md#m8--feel-perf-ship-titles-only);
  rule 7's row makes the rate a number rather than an impression.
- **The Ranger's Pacts.** Rule 2.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
