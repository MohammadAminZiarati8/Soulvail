# M3-12a — Five numbers a node can reach: the stats M3-05 deferred

**Size:** M · **Depends on:** M3-05 (`PlayerStat`, `PlayerStats.Resolve`) · **Branch:** `m3-12a-addressable-stats`
**Design refs:** CC §4.1, §5, §7; CH §3.1, §5; GD §12.4, §12.5, §13.1; AR §18.2, §18.3; ADR-0008, ADR-0010 · **Ledger rows:** **1** (four of these five are what the damage-touching half of the tree is made of — the arithmetic is rule 8)

## Goal

M3-05 rule 5 listed the numbers that are *"not addressable, on purpose, and each becomes addressable in the task whose node wants it"*. M3-12's twelve nodes want five of them, and this is that task.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Core/Effects/PlayerStatCoverageTests.cs` | Tests.Core | the five new members, each resolved to the very instance its owner holds, each moving the thing it names |
| `Tests/Core/Combat/StatReachTests.cs` | Tests.Core | the behaviour each newly-live number changes: a wider cone hits more, a longer range swings sooner, a shorter delay refills earlier |
| *small edits* | | `Core/Combat/Weapon.cs` — `Range` and `ConeAngleDeg` become `Stat`s (rule 2) — **substantial**; `Core/Combat/ChargeSkill.cs` — `Damage` becomes a `Stat` (rule 3); `Core/Combat/Health.cs` — `ShieldRechargeDelay` becomes a `Stat` (rule 4); `Core/Combat/PlayerCombat.cs` — `HealPerKill`, and the drain that spends it (rule 5); `Core/Ai/EnemySystem.cs` — `PendingKills` and `DrainKills()` (rule 5); `Core/Effects/PlayerStat.cs` — five members and five resolver lines (rule 1); `Core/Run/RunSession.cs` — the kill drain, beside the XP one; `Core/Combat/PlayerCombat.cs` — `ResolveConeHits` reads the two weapon stats (rule 2); `WeaponTests`, `ChargeSkillTests`, `HealthTests`, `PlayerCombatTests` gain the "a modifier moves it" rows |

Only these files change. Anything else is a deviation: say so in *As built*. **No new production file** — this task makes five existing numbers addressable and adds one.

## Public API

```csharp
// PlayerStat (added) — five members, five resolver lines, five rows in the walking test
public enum PlayerStat
{
    // ... the six from M3-05, then:
    WeaponRange,             // Weapon.Range
    WeaponConeAngle,         // Weapon.ConeAngleDeg
    ChargeDamage,            // ChargeSkill.Damage
    ShieldRechargeDelay,     // Health.ShieldRechargeDelay
    HealPerKill,             // PlayerCombat.HealPerKill — base 0 (rule 5)
}
```

```csharp
// Weapon (changed): forwarded floats become stats
public Stat Range { get; }              // was `=> _spec.Range`
public Stat ConeAngleDeg { get; }       // was `=> _spec.ConeAngleDeg`

// ChargeSkill (added)
public Stat Damage { get; }             // was read off MovementSkillSpec at resolve time

// Health (added)
public Stat ShieldRechargeDelay { get; }   // was `_shield.RechargeDelay`

// PlayerCombat (added)
public Stat HealPerKill { get; }        // base 0: nothing heals on a kill until a node says so

// EnemySystem (added) — the door a kill count comes through, DrainXp's shape
public int PendingKills { get; }
public int DrainKills();
```

## Behaviour

1. **Each of the five is a `Stat` on its owner, a `PlayerStat` member, one line in `PlayerStats.Resolve` and one row in the walking test.** That is M3-05 rule 4's whole shape, and `Stats_ResolveEveryMember` already walks `Enum.GetValues`, so a member added here without a resolver line fails in the suite rather than at a pick. Nothing else changes about how effects work: these nodes are `ModifyStat`, the primitive that already exists.
2. **`Weapon.Range` and `ConeAngleDeg` stop being forwarded floats**, which `Weapon`'s own remark has been waiting for. Both are read by `PlayerCombat.ResolveConeHits` when it builds the `ConeHitIntent`, so the stat has to be sampled **at the moment the swing is thrown**, not cached: a node taken mid-stage must widen the next swing and not the one after. `Range` also feeds the targeter's in-range test, so a longer weapon acquires sooner — which is the node changing how you play rather than only how hard you hit, and is why it is on the list.
3. **`ChargeSkill.Damage` becomes a stat, and `PlayerCombat.ResolveChargeHits` is the line that changes** — its own comment says so: *"when M3-12 gives them one, this is the line that changes."* Knockback is deliberately **not** promoted: 4 m is a positioning number, a node that moved it would need its own playtest, and no node in v1's twelve wants it.
4. **`Health.ShieldRechargeDelay` becomes a stat and the other two Aegis numbers do not.** CH §3.1's Aegis is *"the only regeneration in the game"* and its delay is the number a player feels — four seconds without being hit, in a game about not being hit. `Max` and `RefillPerSecond` stay authored: raising the maximum without filling it is a trap (M3-05's `Handler_MaxHpMovesHealthLive`), and the rate is Unbroken's keystone (*"Aegis recharges 2× faster"*), which is not in v1's twelve and should arrive whole.
5. **`HealPerKill` is the one number that is new rather than promoted, and it is zero until a node says otherwise.** A kill drains through the door XP already uses: `EnemySystem` accrues `PendingKills` on the call that reports `Killed` — the one door a death comes through (M3-01a rule 3) — and `RunSession.Tick` drains it beside the XP drain, healing `kills × HealPerKill.Value`. Zero base means the line is dead code until M3-12c's Retribution node, and `Heal` already ignores a non-positive amount, so the common path costs one multiply. **A stat rather than an `OnKillTrigger` primitive**, deliberately: M3-05's Out of scope names that primitive family, and the day a node wants *"kills grant shield"* or *"kills leave a zone"* is the day it earns a trigger. One number on a kill is a number.
6. **Every one of them is sampled live, never cached at `Start`.** A tree is taken during play (CH §4.3), so anything cached at the run's opening would silently ignore every node after the first. Each row below has a mirror that takes the modifier mid-run and checks the *next* use of the number, which is the failure this rule exists to catch.
7. **Non-finite and negative values are the stat stack's problem and this task's guard.** `Stat` clamps nothing (ADR-0008), so a cone angle driven to 400° or a range driven negative has to be answered where it is used, `CooldownRules`' argument one layer down: the cone clamps its angle to `(0, 360]` and its range to non-negative at the point the intent is built, and `ShieldRechargeDelay` answers a non-positive or non-finite value with zero — *"refills immediately"* is the honest reading of a delay a stack drove below zero, and it is reachable only by a stack nothing in M3 authors.
8. **The arithmetic these five are sized against (ledger row 1).** M3-01a rule 9 gives ≈ 1.3 picks a stage and GD §12.5's +8–12 % eDPS a stage, so **the average pick is ≈ +6–9 % eDPS** and *"the damage-touching half of the tree ≈ +12–18 % each."* Of these five, `WeaponRange` and `WeaponConeAngle` raise effective DPS **without raising damage** — more enemies per swing, and swings that start sooner — which is precisely what makes them worth promoting over a fourth flat damage number, and precisely what makes them hard to put in a TTK test against a single Husk. M3-12c owes that test and states the path it measures.

## Tests

| Test | Given / When / Then |
|---|---|
| `Stats_ResolveEveryMember` | *the existing row* / for each `PlayerStat` including the five new / `Resolve` returns the very instance — `ReferenceEquals` with `weapon.Range`, `weapon.ConeAngleDeg`, `charge.Damage`, `health.ShieldRechargeDelay`, `combat.HealPerKill` (rule 1) |
| `Weapon_RangeAndAngleAreStats` | the Oathbound's 8 m / 60° / — / `Range.Base` 8, `ConeAngleDeg.Base` 60, both `ModifierCount` 0 (rule 2) |
| `Weapon_WiderConeReachesTheIntent` | `ConeAngleDeg` +50 % / a swing / the `ConeHitIntent` carries 90°, not 60° (rule 2) |
| `Weapon_LongerRangeReachesTheIntent` | `Range` +2 Flat / a swing / the intent carries 10 m (rule 2) |
| `Weapon_LongerRangeAcquiresSooner` | a target at 9 m, range 8 then +2 / the targeter / out of range, then in (rule 2) |
| `Weapon_StatIsSampledPerSwing` | swing, then widen the cone mid-cooldown / the **next** swing / it carries the new angle (rule 6) |
| `Charge_DamageIsAStat` | the Oathbound's 20 / — / `Damage.Base` 20; +50 % / a dash through a Husk / 30 dealt (rule 3) |
| `Charge_KnockbackIsNotAddressable` | reflection over `PlayerStat` / — / no member names knockback (rule 3) |
| `Health_RechargeDelayIsAStat` | 4 s / — / `Base` 4; −50 % / a hit, then 2 s of `Tick` / the Aegis is refilling (rule 4) |
| `Health_ShieldMaxAndRateAreNotAddressable` | reflection over `PlayerStat` / — / no member names either (rule 4) |
| `Health_NonPositiveDelayRefillsImmediately` | delay driven to −1, then NaN / a hit, one `Tick` / refilling both times (rule 7) |
| `Kill_AccruesPendingKills` | a killing `ApplyDamage` / — / `PendingKills` 1; `DrainKills()` returns 1 and leaves 0 (rule 5) |
| `Kill_HealsNothingByDefault` | `HealPerKill` base 0, hp 100 of 140 / a kill, one `Tick` / hp 100 (rule 5) |
| `Kill_HealsWhenANodeSaysSo` | `HealPerKill` +2 Flat, hp 100 / three kills, one `Tick` / hp 106 (rule 5) |
| `Kill_HealIsDrainedOnTheTick` | a kill reported by `ReportConeHits` between ticks / before the next `Tick`: hp unchanged; after it: healed — the XP drain's shape (rule 5) |
| `Kill_DeadPlayerHealsNothing` | the player dies on the tick a kill lands / `Tick` / no heal, `RunEnded` — M3-01a rule 6's ordering, mirrored |
| `Kill_ClearDropsPendingKills` | `PendingKills` 3 / `Clear()` / 0 |
| `Cone_ClampsAnAbsurdAngle` | `ConeAngleDeg` driven to 400, then to −10, then NaN / a swing / the intent carries 360, then a refusal to swing, and 360 — never a NaN wedge (rule 7) |
| `Cone_ClampsANegativeRange` | `Range` driven to −3 / a swing / the intent carries 0 and hits nothing (rule 7) |
| `Stats_AreSampledLiveNotAtStart` | a run started, then a modifier on each of the five / the next use of each / all five moved (rule 6) |
| `Charge_UnmodifiedIsUnchanged` · `Weapon_UnmodifiedIsUnchanged` · `Health_UnmodifiedIsUnchanged` | the shipped assets / play / every existing behaviour row still passes — the rows that say this task changed nothing anyone can feel |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend. The Censer, the Charge and the Aegis behave exactly as they did at M2 — five numbers changed shape and none changed value.
2. **[Editor]** With a hand-authored node granting `ConeAngleDeg` +50 %, take it mid-stage. The very next swing sweeps visibly wider (rules 2, 6) — the clearest proof in the milestone that a pick changed the game.
3. **[device]** Nothing new. Five `Stat.Value` reads a frame where five float reads used to be; the `GC.Alloc` row (ledger row 4) covers it with the rest.

## Out of scope

- **`ModifySkillCooldown` and `KnockbackOnSwing`** — M3-12b. Those are primitives; these are stats.
- **The twelve nodes** — M3-12c, which authors against these five.
- **`ShieldMax`, `ShieldRefillPerSecond`, `MovementSkillSpec.Knockback`** — rules 3 and 4 say why each is left authored, and which future node claims it.
- **`EnemyStat`** — M7-02's affixes want the mirror table on `EnemyAgent`; M3-05's Out of scope says it is a second handler and not a second registry.
- **An `OnKillTrigger` primitive** — rule 5. The second on-kill node is what earns it.
- **Retuning anything.** Every base stays exactly what the assets say; this task only makes them reachable.

## As built

_Filled at merge._
