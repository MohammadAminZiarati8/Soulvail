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

**Four counted files, size M's floor, no split.** `Weapon.cs` (substantial, as declared), `PlayerCombat.cs`, and the two new test files. **`PlayerCombat` was ruled substantial although the argument runs both ways**: its five sites are a `Stat` property plus one constructor line, a five-line drain method, two private clamp helpers, and four reads swapping a float for a clamped `Stat.Value` — every one additive or a one-expression swap, with no method's control flow, ordering or fields moved, which reads like [ROADMAP › How to read this](../ROADMAP.md#how-to-read-this)'s *"small additive edits"*. It was counted anyway because it adds new public API to a hot-path class at five independent sites, and because not counting it would land the task at 3 files — size S — contradicting this spec's own declared size. `Health.cs`, `EnemySystem.cs`, `ChargeSkill.cs`, `PlayerStat.cs` and `RunSession.cs` were each checked individually and none is substantial. **Not 5, not 6.**

**Ruling on rule 7 and `Cone_ClampsAnAbsurdAngle`: NaN is refused, not widened, and the row's third expectation changes from 360 to 0.** The row asked for 400 → 360, −10 → a refusal and NaN → 360, which gives the *less* meaningful value the *more* permissive answer; answering an unreadable angle with 360 draws the widest wedge in the game and quietly turns the Censer omnidirectional. Shipped: **400 → 360, −10 → 0, non-finite → 0**, so rule 7's prose `(0, 360]` is **`[0, 360]`**. **The refusal is an intent of zero angle, not a suppressed intent** — matching `Cone_ClampsANegativeRange`'s *"carries 0 and hits nothing"* — because by the time `TickWeapon` builds the intent the swing has already started and published `PlayerAttacked`, and suppressing it would leave `PendingConeRequestId` waiting on a report that can never arrive. A zero-angle wedge resolves to zero hits and the empty report clears the id on the ordinary path. **One asymmetry is deliberate and documented on the helpers**: `+∞` clamps to 360 where an infinite *reach* is refused, because an infinite angle has a meaning and a widest wedge to land on, and an infinite reach has no maximum.

**Finding the spec does not have, and it is now measured: `HealPerKill`'s base of 0 kills half the modifier vocabulary.** `Stat.Recompute` was read rather than assumed — `(Base + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult)` — so with a base of zero and no `Flat` on the stack, **every percentage kind multiplies into zero and `ModifierKind.Flat` is the only kind that can ever move it.** A `RunCommand` probe read **+500 % → 0** and **+2 Flat → 2**. **The base stays 0**: a non-zero one would make every class in the game heal on kill, a balance change no design document asks for and the direct opposite of rule 5's *"nothing heals on a kill until a node says so"*. The constraint is written into `PlayerCombat.HealPerKill`'s own remarks where M3-12c's author will read it (naming `Flat` and Retribution explicitly, and the order to grant nodes in), into `PlayerStat.HealPerKill`'s, and **pinned by two rows** — `HealPerKill_APercentageModifierMovesItNotAtAll` and `HealPerKill_MovesOnlyWithAFlatModifier` — so a percentage-authored Retribution is a red row rather than a silent no-op.

**Two corrections found by running rather than by reading.**
1. **`Stat.Base` and `Modifier` both *throw* on a non-finite input** rather than swallowing it, so a NaN cannot reach the swing through either ordinary door — the clamp is a backstop against arithmetic overflow inside the stack. A first draft of `Cone_ClampsAnAbsurdAngle` assumed the NaN would be absorbed and cost **one red run** (1 765 / 1). The row now asserts both refusals, and `Cone_ANegativeProductHitsNothingRatherThanBecomingACircle` reaches the shared branch the only way a shipped modifier can: a `PercentMult` below −1, which ADR-0008 explicitly permits.
2. **`Cone_ClampsANegativeRange` as specified is unobservable**, and the reason is this task's own doing: the targeter's in-range test and the intent read the **same** clamped reach, so a range that is already negative stops the swing before it starts rather than throwing one that reaches nothing. Shipped as **two** rows — `Cone_ClampsANegativeRange` drives the reach negative *mid-swing* (which `Weapon`'s documented *"the damage frame belongs to the swing, not to the target"* makes observable) and `Cone_NegativeRangeStopsTheNextSwing` pins the other half. **Strictly better than the row asked for**, and the two sites cannot disagree because there is one clamp.

**No sixth reader, confirmed rather than assumed.** `Weapon.Range` and `ConeAngleDeg` had exactly the three sites the spec's ripple note named — `PlayerCombat` at the two intent reads and the in-range test — checked by grep across the whole of `Assets/_Project`. `Game/Adapters/ConeOverlapQuery` reads `ConeHitIntent.Range` / `.AngleDeg`, which are floats on the intent and did not change.

**Deviations from the Files table, all named:** `Tests/Core/Effects/ModifyStatTests.cs` is a **fifth** existing test fixture edited, which the table does not list — `Stats_ResolveEveryMember` lives there rather than in a `PlayerStats.cs` that does not exist, and rule 1 requires its five new arms. It gains **no** new row. The `Kill_*` family landed in `StatReachTests` rather than `PlayerCombatTests` because each spans `EnemySystem`, `PlayerCombat` and the drain; `PlayerCombatTests` took the three rows that are purely this class's. **`Charge_UnmodifiedIsUnchanged` collided with M3-06's row of that exact name** (that one is about the cooldown) and was renamed `Charge_UnmodifiedDamageIsUnchanged` rather than widening the existing row.

**Verified:** **1 766 / 0 / 0 EditMode, twice consecutively** (three runs in all), against 1 723 — **43 new rows, and the arithmetic lands to the row.** **PlayMode 16 / 16, `Ticker_RunsTheStepsInOrder` green**, and the count did not move even though `RunSession.Tick` gained a line: `FrameOrderTests` drives a `RecordingCore` whose `State` returns `null`, so the kill drain never executes there. **Two assemblies** (`Soulvail.Core` ×7, `Soulvail.Tests.Core` ×7), no third. **No prefab and no scene moved.** Console 37 / 11 / 23 / 3, every line a known family. `ProjectSettings/TimeManager.asset` dirtied and reverted for the twelfth time in thirteen tasks.

**Manual verification:** step 1 is the owner's. **Step 2 is runnable today** — `ModifyStatDefinition` serializes `PlayerStat` as an enum dropdown, so `WeaponConeAngle` appears there the moment this compiles; `SkillDefinition`, `SkillTreeDefinition` and `ModifyStatDefinition` all carry `CreateAssetMenu`, and `BootScope._trees` is a serialized slot. Three hand-authored assets and one Inspector drop, no code. **`Data/` holds no tree asset at all today**, so the assets have to be made before the step can run.

**Ledger:** **row 1 gains reach and does not close.** Four of the five are what the damage-touching half of the tree is made of, and this is the first task that makes any of it reachable — but **it moves no number in any run this build plays**, and **M3-12c still owes the TTK test at stage 1 / 15 / 30**. **Row 4** gains five `Stat.Value` reads a frame where five float reads used to be, all on the swing path, beside the `GC.Alloc` line unmet since M1-21. **No `AllocationAssert` row was added, deliberately**: a `Stat.Value` read on a clean cache is a field read, `Handler_AllocatesNothingAfterWarmUp` already pins the modifier path, and a new row would assert what existing rows assert while still not being the Profiler's line on a phone.
