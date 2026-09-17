# M3-11a-i — The granted shield pool: a third pool, spent first, taken back per source

**Size:** S (one code file, and `Health` gains one field and one branch) · **Depends on:** M1-02 (`Health`) · **Branch:** `m3-11a-i-granted-shield-pool`
**Design refs:** CC §6.4, §7; CH §3.1, §4.1; GD §16.2; AR §3, §18.1, §18.2, §18.3; ADR-0009 · **Ledger rows:** 2 (checked, honoured by writing nothing), 4 (a new per-frame walk joins the unmet `GC.Alloc` row)
**Split from:** [M3-11a](M3-11a-bulwark-and-timed-effects.md), under that spec's own pre-declared tripwire. The primitive and its clock are [M3-11a-ii](M3-11a-ii-bulwark-and-timed-effects.md).

## Goal

The half of Bulwark that is not about time: shield points that sit on top of the Aegis, stack per source, are spent before anything else, and can be handed back for exactly what they have left.

## Why this is its own task

M3-11a budgeted *"one field and one branch"* for `Health`'s pool and named its own split if that was exceeded. It is exceeded, and the proof is in M3-11a's own Tests table rather than in the implementation:

> A grants 35 and B grants 20 → 55. Twenty damage arrives → 35. **A expires — what comes off?** Spent-A-first leaves A at 15, so 20 remains. Spent-B-first leaves A at 35, so 0 remains. **The same total, two answers.**

Rule 8's *"expiry removes what remains of **that source**"* therefore needs per-source attribution **and** a stated spend order. That is a table, not a float — and every way of shipping it breaches a stated limit:

| Shape | `Health` | Files | Breach |
|---|---|---|---|
| Table inside `Health` | ~3 fields, a distributing walk, two methods, ~479 → ~640 lines | 5 | **the tripwire** |
| Table in its own file | one field, **zero** new branches | 6 | **the five-file ceiling** |

Both trip. The split is forced rather than chosen, and it falls out exactly as M3-11a named it: the pool, then the primitive.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/GrantedShieldPool.cs` | Core | the per-source table: capacity, grant-sets-not-adds, an ordered spend, remove-what-is-left |
| *small edits* | | `Core/Combat/Health.cs` — one field, the spend in `ApplyDamage`, `GrantedShield`/`GrantShield`/`RemoveGrantedShield`, one line in `Reset` and one in `Restore`; `Core/Run/RunState.cs` + `PlayerGrantedShield`; `Game/Presentation/DebugOverlay.cs` + one `Append`; `Tests/Core/Combat/HealthTests.cs` gains the pool rows; `Tests/Core/Combat/SkillRunnerTests.cs` gains the `State_*` row |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Health (added)
public float GrantedShield { get; }                       // absorbed before the Aegis (rule 1)
public void GrantShield(float amount, object source);     // stacks per source, never per call
public bool RemoveGrantedShield(object source);

// RunState (added)
public float PlayerGrantedShield { get; }                 // the twelfth scalar read
```

`GrantedShieldPool` is `internal` and never appears in a public signature. `Soulvail.Tests.Core` has no `InternalsVisibleTo` and deliberately never will (AR §18.2, M0-10), so every rule below is asserted through `Health`.

## Behaviour

1. **Granted shield is a third pool, spent before the Aegis, and it is deliberately not the Aegis.** The Aegis is the Oathbound's signature — 30 points, a 4 s delay, 15/s refill, *"the only regeneration in the game"* (CH §3.1) — and its `ShieldSpec` numbers are what `ShieldRingView` draws. Raising `ShieldMax` instead would have meant a `Stat` on the spec's maximum **and** a separate fill, because raising a maximum is not filling it (M3-05's `Handler_MaxHpMovesHealthLive`) — two changes to the class's signature mechanic to express a temporary one. So: `ApplyDamage` spends `GrantedShield`, then the Aegis, then hit points, and the Aegis's own numbers and its ring mean exactly what they meant before.
2. **It stacks per source, not per call.** Two casts of Bulwark before the first expires is one source (the `ActiveSpec`), so the second refreshes rather than doubles: `GrantShield(amount, source)` *sets* that source's contribution and resets nothing else — including its place in the spend order. Per-call stacking would make a floored cooldown (CH §4.1's 40 %) into permanent immunity, which is the exact failure the floor exists to prevent. Two *different* sources stack, because they are two different things.
3. **The spend is oldest grant first, and the order is grant order rather than expiry order.** Spending the grant that has stood longest is the nearest this class can get to *"spend what is about to be wasted"* without knowing anything about clocks — which it deliberately does not, because expiries belong to the thing that owns them (M3-05 rule 6) and nothing here ticks.
4. **It does not recharge and it is not healed.** The Aegis refills on its own clock and this does not: it is a cast, and it is gone when the timer says. `Health.Heal` and `Health.Tick` both needed nothing doing to them, which is the strongest form of this rule.
5. **Removal takes what is left and never goes negative.** A grant of 35 that absorbed 20 has 15; removal takes 15, not 35. A source holding nothing is still *held* and still removes, which is what lets an expiry clean up unconditionally (`Stat.RemoveAll`'s contract); a source that never granted answers `false` and changes nothing. `Reset` and `Restore` forget without removing, because the component is going back to a stated state either way.
6. **A fixed array of eight, and a ninth source throws rather than growing one.** Eight simultaneous distinct sources is already an order of magnitude past anything CH §4's tree describes, and this is walked from the damage path — a silently growing array there is only ever found on a phone. The number is a refusal, not a budget.
7. **A fully absorbed hit still starts i-frames, and leaving it out would have been a silent buff to the Aegis.** The i-frame branch reads `toGranted + toShield + toHp`; read as `toShield + toHp` it would not run at all for a hit the grant swallowed whole — no i-frames from it, and `_lastDamageAt` unmoved, so the Aegis would refill straight through a fight.
8. **`toGranted` is deliberately not reported on `DamageResult`.** `ToShield` means *the Aegis*: CH §3.1's Martyr keystone deals damage equal to everything the Aegis absorbed, and a listener summing that field is the whole implementation of it, so a Bulwark's points landing there would quietly make Martyr scale with a skill it has nothing to do with. **What that costs is named rather than hidden:** a hit absorbed entirely by granted shield leaves through `PlayerDamaged(0, 0, unchanged, unchanged, blocked: false)`, which reads as a hit that did nothing. **M3-13b** draws the granted pool on the player's bar and is the task that has to tell those apart; widening `DamageResult` is its call, not this one's.
9. **A resumed run comes back with no granted shield, and that is a decision** ([ledger row 2](../ROADMAP.md#carry-forward-into-m3)). Nothing about a grant is on disk: it is derived from a cast that happened, and the cast is gone — unlike a cooldown, which M3-07b could rebuild from `TakenNodeIds` because the *node* survives. Saving one would mean writing down a source identity there is nothing left to hand back to. `Restore` clears the pool for that reason.

## Tests

| Test | Given / When / Then |
|---|---|
| `Health_GrantedShieldAbsorbsFirst` | 140 hp, Aegis 30 full, granted 35 / 20 damage / granted 15, Aegis 30, hp 140 — and the result reports 0/0 unblocked with i-frames on (rules 1, 7, 8) |
| `Health_SpillsIntoTheAegisThenHp` | the same, 80 damage / — / granted 0, Aegis 0, hp 125 (rule 1) |
| `Health_AegisNumbersAreUnchanged` | granted 35 on a full Aegis / — / `ShieldMax` 30, `ShieldFraction` 1, `Shield` 30 (rule 1) |
| `Health_GrantStacksPerSource` | grant 35 from A, then 35 from A again / — / 35, not 70 (rule 2) |
| `Health_TwoSourcesStack` | 35 from A, 20 from B / — / 55 (rule 2) |
| `Health_RefreshSetsTheSourceRatherThanStacking` | 35 from A, spend 20, grant 35 from A / — / 35, and neither 50 nor 15 (rule 2) |
| `Health_SpendsTheOldestGrantFirst` | 35 from A then 20 from B, spend 20 / `RemoveGrantedShield(A)` / 20 — the total could not have said (rule 3) |
| `Health_RefreshKeepsItsPlaceInTheSpendOrder` | A, B, then A refreshed, spend 10 / `RemoveGrantedShield(A)` / 20, not 10 (rule 2) |
| `Health_GrantIsNotRecharged` | granted 35, spend 20 / 10 s of `Tick` / still 15 (rule 4) |
| `Health_HealDoesNotTouchIt` | granted 15, hp 100 of 140 / `Heal(30)` / hp 130, granted 15 (rule 4) |
| `Health_RemoveTakesWhatIsLeft` | 35 from A, spend 20 / `RemoveGrantedShield(A)` / 0, hp unchanged, never negative (rule 5) |
| `Health_RemoveUnknownSource_IsFalse` | nothing from A / `RemoveGrantedShield(A)` / false; and a spent source removes once and only once (rule 5) |
| `Health_ResetClearsThePool` | granted 35 / `Reset` / 0, and the source is forgotten rather than emptied (rule 5) |
| `Health_GrantedShieldIsInvulnerableSafe` | invulnerable by flag, then by i-frames, granted 35 / damage / nothing spent, both paths (rule 1) |
| `Health_GrantRefusesNullSourceAndNonFiniteAmount` | null source, NaN, ±∞, −1 / `GrantShield` / throws each; zero is legal and holds the source |
| `Health_GrantCapacityThrows` | eight sources / a ninth / throws naming the capacity; a refresh at capacity is still allowed (rule 6) |
| `Health_GrantedShieldAllocatesNothing` | grant, spend, remove, warm-up / `AllocationAssert.None` / allocated-bytes delta == 0 (rule 6) |
| `State_ExposesTheGrantedShield` | a live run / `PlayerGrantedShield` / 0 — correctly empty; reflection says `Combat` is still not public |

"allocated-bytes delta == 0" means `AllocationAssert.None` from M0-02 — never the raw `GC` API ([Traps §7](../../Traps.md)).

## Manual verification (Editor / device)

1. **[Editor]** Descend and watch the `DebugOverlay`. It reads `shield 0.00` for the whole run, beside `actives 0` and `slots 0/4` — the pool being correctly empty and saying so. **This is the only manual step this task has, and a non-zero reading needs M3-11a-ii to grant and M3-12 to author.**
2. **[device]** [Ledger row 4](../ROADMAP.md#carry-forward-into-m3): whether an eight-entry walk per frame shows on the Profiler's `GC.Alloc` line, with the rest.

## Out of scope

- **`TimedEffects`, `GrantShield` and its handler, and the two events** — [M3-11a-ii](M3-11a-ii-bulwark-and-timed-effects.md). Nothing in this task can put a grant on a live player; the pool ships wired to nothing, which is M3-04's and M3-05's bargain exactly.
- **Any view** — M3-11c. Until then a granted shield is a number on the debug overlay.
- **Drawing it on the HUD, and telling a fully absorbed hit from a hit that did nothing** — M3-13b, and rule 8 is the note it inherits.
- **Making the Aegis's numbers addressable** — M3-05 rule 5 says each arrives with the node that wants it, and rule 1 above says why Bulwark is not that node.

## As built

**Merged as specced, with three deviations, all named here.**

**One counted file and five small edits, exactly the table.** `GrantedShieldPool` is `internal sealed`, file-scoped (pure C#), and holds three fields — two parallel arrays and a count, which is `SkillRunner`'s house shape rather than an array of structs. `Total` is **walked rather than cached**: eight adds is cheaper than a fourth invariant to keep exact, and a cached total that drifts has nothing to be compared against.

**`Health` came in under its own budget: one field and *zero* new branches.** `_granted.Spend(amount)` is called unconditionally because the empty case is a loop that does not run, so the pool needed no guard on the damage path at all.

**Rule 7 is the finding that changed the code.** The i-frame branch read `toShield + toHp`, so a hit swallowed whole by a grant would have started no i-frames *and* left `_lastDamageAt` unmoved — the Aegis refilling straight through a fight. It reads `toGranted + toShield + toHp` and `Health_GrantedShieldAbsorbsFirst` asserts `IsInvulnerable` is true after a hit that reports 0 and 0.

**Deviation 1 — `Restore` is cleared as well as `Reset`, which the Files table did not name.** Rule 9's ruling has to be true of a resumed run and `Restore` is the door a resumed run comes through; leaving it would have let a grant outlive the run that made it. One line, no branch.

**Deviation 2 — `State_ExposesTheGrantedShield` ships as its reachable half and lands in `SkillRunnerTests`.** The spec's *"granted 35"* cannot be reached in this task: `Health.GrantShield` is public but `RunState.Combat` is not, and the only thing that will ever call it is M3-11a-ii's handler. So the row asserts a live run reads **0** — correctly empty and saying so — plus the reflection seal, and the non-zero half lands with the handler. It sits in `SkillRunnerTests` rather than beside `Health` because it needs a `RunSession` and `HealthTests` has none; the three `State_*` rows M3-09b and M3-10a added are already there for the same reason.

**Deviation 3 — `DebugOverlay.cs` is a sixth touched file the parent spec's table did not carry.** Added under the owner's ruling so that M3-11a's manual step 1 is runnable at all; it is one `Append` in an existing line, which the [ROADMAP's counting rule](../ROADMAP.md#how-to-read-this) classes as a small additive edit.

**Ledger row 2 is checked and honoured by writing nothing, for the sixth consecutive task — and the reason is its own rather than a copy of the fifth.** A cooldown was derivable from `TakenNodeIds` (M3-07b's ruling); a grant is derivable from nothing on disk, because the cast that made it is gone. `RunSnapshot.CurrentVersion` stays 3 and no migration step is owed. **Ledger row 4 gains an eight-entry per-frame walk** beside the unmet `GC.Alloc` line. **Ledger row 1 moves nothing**: Bulwark deals no damage, and its half of this task deals even less.

**Verified:** 1 627 EditMode / 0 / 0, twice consecutively on the final code, against M3-10b's 1 609 — **18 new rows**, and the arithmetic lands to the row. The Tests table above is 18 lines and 18 names: the parent spec's 13 that belong to the pool, one of them renamed, plus 5 this task owes — one implied-guard row covering both doors (`Constructor_RefusesNullMaxHpAndInvalidIFrames`'s precedent), the capacity refusal, the spend order, the refresh's place in it, and the allocation row ledger row 4 asks for.
