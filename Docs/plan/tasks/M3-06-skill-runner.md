# M3-06 — `SkillRunner`: cooldowns with the 40 % floor, and auto-cast over the blackboard

**Size:** M · **Depends on:** M3-02a (`ActiveSpec`, `TriggerSpec`), M3-03 (the tree that hands it a skill), M3-05 (the registry a cast applies through) · **Branch:** `m3-06-skill-runner`
**Design refs:** CH §4, §4.1, §4.2; CC §5, §6.1, §6.4, §7; GD §12.5; AR §5, §9, §14, §18.1, §18.2, §18.3; ADR-0003, ADR-0005, ADR-0008, ADR-0009 · **Ledger rows:** 1 (the runner is the machine GD §12.5's per-stage power runs on, and rule 1's floor is the ceiling on how much of it a cooldown node may supply — M3-12 authors, M3-15 gates), 4 (a new per-frame path joins the unmet no-`GC.Alloc` Profiler row)

## Goal

An owned active has a live cooldown, a floor nothing can drive it under, and an authored condition that fires it without the player — CH §4.2's *"the player does nothing"* as a class that owns clocks and casts, and knows nothing about buttons.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/CooldownRules.cs` | Core | CH §4.1's 40 % floor, in one place because two things obey it — `FocusResolver`'s static-class precedent |
| `Core/Combat/SkillRunner.cs` | Core | the owned actives, their cooldown stats, and the tick that casts one |
| `Core/Events/SkillEvents.cs` | Core | `SkillCast` — the module's vocabulary, `RunEvents.cs`' precedent |
| `Tests/Core/Combat/SkillRunnerTests.cs` | Tests.Core | the floor and the runner in one fixture: the floor is only interesting as the thing the runner and the Charge both obey |
| *small edits* | | `ChargeSkill.Tick` — `_readyAt` through `CooldownRules.Effective` (rule 3), which is what its own line 241 comment promises; `ChargeSkillTests` gains the floor rows; `RunSession.Start` builds the runner after the tree and adds the restored actives (rule 5); `RunSession.Tick` ticks it between combat and the behaviours (rule 7); `RunState` + `internal SkillRunner Skills` and four reads; `DebugOverlay` shows owned actives and the first one's fraction; **AR §5**'s `Combat` row gains the file it already names; **AR §18.1** gains the runner's ordering row |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Combat;

/// CH §4.1's floor. A static class rather than a member, because ChargeSkill obeys it too and two
/// copies of a floor is how one of them stops being the floor (FocusResolver's shape).
public static class CooldownRules
{
    public const float FloorFraction = 0.4f;

    /// <summary>The shortest this cooldown is allowed to be: never under 40 % of what was authored.</summary>
    public static float Effective(float authoredBase, float live);   // authoredBase finite and > 0
}

public sealed class SkillRunner
{
    /// CH §5's 27 nodes at CH §4's ~25 % Active is about seven; twelve is headroom, and the array
    /// is sized once so nothing grows on a pick.
    public const int MaxActives = 12;

    public SkillRunner(EffectRegistry effects, CombatBlackboard blackboard, IDomainEvents events);

    public int Count { get; }

    /// <summary>Takes ownership of an Active. Core calls this; core does not subscribe to itself.</summary>
    public void Add(SkillSpec skill);                  // Active only; a duplicate throws

    public ContentId IdAt(int index);
    public SkillSpec SpecAt(int index);
    public bool TryIndexOf(ContentId id, out int index);

    /// <summary>The live cooldown, and the address M3-12's `ModifySkillCooldown` resolves to (rule 12).</summary>
    public Stat CooldownOf(int index);

    public bool IsReady(int index);
    public float CooldownFraction(int index);          // 1 the instant a cast starts, 0 when live

    /// <summary>Casts at most one skill (rule 6). Allocates nothing.</summary>
    public void Tick(float dt, float now);

    /// <summary>Fires one regardless of its trigger. False when it is still cooling.</summary>
    public bool Cast(int index, float now, bool auto);

    public void Reset();
}
```

```csharp
namespace Soulvail.Core.Events;

/// An active fired. After its effects are on and its cooldown has started, so a handler reading
/// CooldownFraction from inside it sees 1.
public readonly struct SkillCast
{
    public readonly ContentId SkillId; public readonly float Cooldown; public readonly bool WasAuto;
}
```

```csharp
// RunState (added) — reads, never the handle (AR §18.2)
internal SkillRunner Skills { get; }
public int OwnedActiveCount { get; }
public ContentId SkillIdAt(int index);
public float SkillCooldownFraction(int index);
public bool IsSkillReady(int index);
```

## Behaviour

**The floor**

1. **`Effective(authoredBase, live)` is `max(FloorFraction × authoredBase, live)`** — CH §4.1's *"multiplicative and floored at 40 % of base"*, with the floor taken from the **authored** base and not from `Stat.Base`. The two are the same today; the day a node re-bases a cooldown they are not, and a floor computed from a base a node just raised would rise with it, which is a floor that stops floor­ing. A live value that is non-finite or not greater than zero answers the floor (`!(live > FloorFraction * authoredBase)`, AR §18.3's spelling) — `Stat` clamps nothing (ADR-0008), so a stack can drive a cooldown to zero or below, and this is the layer that says no. A non-finite or non-positive `authoredBase` throws: that is a content mistake, and `ActiveSpec` already refuses one (M3-02a rule 4).
2. **CH §4.1's *multiplicative* is a statement about how a cooldown node is authored, not about what this class does.** A "−15 % cooldown" node carries `ModifierKind.PercentMult` (M3-05 rule 8 reserves it for exactly this kind of loud multiplier); the runner reads `Stat.Value` and applies the floor, and would behave identically if a designer authored `PercentAdd`. **The arithmetic, written down:** the Charge at 2.5 s floors at **1.0 s**, and reaching it takes **six** −15 % multiplicative nodes (0.85⁵ = 0.444 → 1.11 s, above; 0.85⁶ = 0.377 → 0.94 s, floored) or **four** additive ones (1 − 0.60 = 0.40, exactly at it). An 8 s active floors at **3.2 s**. M3-12 authors against those, and that the multiplicative route costs half again as many picks is the point of CH §4.1's word.
3. **`ChargeSkill` obeys the same function**, which is what its own code has promised since M1-14: *"M3-06's floor is where a cooldown stops being allowed to reach zero, because that is the layer that knows what 'too short' means."* One line changes — `_readyAt = now + CooldownRules.Effective(_spec.Cooldown, Cooldown.Value)` — and the sampled-and-held rule around it is untouched. Nothing modifies the dash cooldown until M3-12, so this is invisible in play and pinned by a test rather than by a playtest.

**The runner**

4. **One entry per owned active, and the entry owns everything live about it:** the `SkillSpec`, a `Stat Cooldown` seeded from `ActiveSpec.Cooldown` — a fresh `Stat` and not the spec's raw number, `ChargeSkill`'s constructor's argument (ADR-0008: the spec stays what a designer typed) — and a `_readyAt`. Everything is scheduled as an **absolute time** against the simulated clock, `Weapon`'s and `ChargeSkill`'s shape: no accumulator to drift, so a 30 fps phone and a 120 fps one cast at the same rate. The cooldown is **sampled at the start of the cast and held**, so a modifier landing mid-cooldown changes the *next* one; `CooldownFraction` is measured against the **current** effective value, so a buff shows on the fill immediately even though the wait itself does not shorten — `ChargeSkill`'s bargain, in the same words, because M3-10 draws both fills side by side and they must not answer differently.
5. **`Add` is how a skill arrives, and core pushes it — core does not subscribe to its own events** (M3-03 rule 7, `RunSession`'s remark on `PlayerDied`). Two callers, both in core: M3-08a's `ChooseOffer` when the node taken is an Active, and `RunSession.Start` after `SkillTree.Restore`, walking `TakenIds` in take order and adding every Active among them. A duplicate id throws `InvalidOperationException`; a non-Active `SkillSpec` throws `ArgumentException`; a thirteenth throws. Take order is therefore the runner's order, on a fresh run and on a resumed one alike, which is what makes rule 6's walk reproducible.
6. **`Tick` casts at most one skill, walking the entries in take order.** Each is asked in turn: off cooldown, and `Active.Trigger.IsMet(blackboard)`; the first that answers yes is cast and the walk **stops**. No starvation — a cast puts that skill on cooldown, so the next tick reaches the next one, and four ready-and-triggered actives all fire within four frames (≈ 50 ms at 60 fps, below anything a player can time). Bounded per-frame work is AR §14's ask, and nothing in CC §6 or CH §4 asks for simultaneity; what they ask for is that Auto feel deliberate, and four skills detonating on one frame is the opposite of deliberate.
7. **Ticked immediately after `PlayerCombat.Tick` and before the enemy behaviours** — an **AR §18.1** row with two halves, and the second is the one that is easy to get wrong. *After combat*, because `UpdateBlackboard` fills nine of the eleven fields a trigger can read, and a predicate over last tick's HP is a Consecrate that fires a frame after the hit that should have caused it. *Before `ProjectileSystem.Tick`*, because **`IncomingProjectiles` is written by the projectile step and nowhere else** (`ProjectileSystem.cs:401`): read here it is the count of bolts still in the air **before this tick's arrivals are resolved**, which is exactly what CC §6.4's Bulwark means by *"an enemy projectile is inbound"* — a shield raised **before** the bolt lands. Ticked after that step instead, the same field would describe the sky *after* the hit, and the archetype's whole answer would be a shield put up over a wound. The field is therefore deliberately one step old, and that staleness is the mechanic rather than a lag to fix.
8. **A cast applies `ActiveSpec.OnCast` through the registry with the `ActiveSpec` as the source** — M3-05 rule 7's *"whoever owns the effect"*, and deliberately **not** the `SkillSpec` that `SkillTree.Take` passes. `Stat.RemoveAll(source)` takes a whole source back at once (M3-05 rule 6), so a node that both grants a passive and buffs on cast needs the two to be separable, and the two objects are already distinct. Nothing is timed yet: the first cast effect with a duration is M3-11's, it owns its own clock, and `EffectRegistry.Remove` is the door it calls.
9. **`SkillCast` is published last**, after the effects and after `_readyAt` moves, so a handler reading `CooldownFraction` sees 1 and a view drawing a buff sees it applied. It carries the **effective** cooldown rather than the authored one, because that is what a radial fill is over.
10. **No throttle, and the arithmetic is why.** `TriggerSpec.IsMet` is allocation-free over at most two clauses (M3-02a rules 5, 6); a full 27-node tree at CH §4's ~25 % Active is about **seven actives and at most fourteen float comparisons a frame**. ADR-0003 says core throttles its own expensive work; this is not expensive, and a 10 Hz cadence would make a trigger answer up to 100 ms late on the one system whose whole promise is that it reacts. The class allocates nothing after construction: fixed arrays sized `MaxActives`, and `Tick` writes no list.
11. **Not reset at a stage boundary.** M2-10's standing rule is that a boundary resets the `Targeter` and **never** `PlayerCombat` — a door is not a free heal, and it is not a free set of cooldowns either. `Reset()` exists for the reason `Weapon.Reset` does, leaves the modifier stacks alone for the reason `ChargeSkill.Reset` does, and nothing in M3 calls it.
12. **A per-skill cooldown is not a `PlayerStat`, and this is the seam the first Upgrade node uses.** `PlayerStat` addresses one stat per member (M3-05 rule 4) and *"−15 % Consecrate cooldown"* names a skill, so it cannot be one; it is `ModifySkillCooldown`, a file pair in M3-05 rule 5's shape carrying a `ContentId` and a `Modifier`, added by the task whose node needs it. `CooldownOf(index)` and `TryIndexOf` are the address its handler resolves through, and they are here now so that task is a file rather than a refactor.
13. **`RunState` hands out reads, never the runner** (AR §18.2, asked for the sixth time). `Add` and `Cast` are public, so a public handle would let a view fire the player's skills or grant them one. Four reads is what M3-10's buttons and the overlay need, and M3-07a adds the three the slots need.

## Tests

| Test | Given / When / Then |
|---|---|
| `Floor_IsFortyPercentOfBase` | base 2.5, live 0.5 / `Effective` / 1.0 (rule 1) |
| `Floor_LeavesAnUnreducedCooldown` | base 2.5, live 2.5 / — / 2.5 |
| `Floor_CatchesZeroNegativeAndNaN` | live 0, −4, NaN, −∞ / — / 1.0 each (rule 1) |
| `Floor_InfiniteLiveIsNotFloored` | live ∞ / — / ∞ — a stack that made the wait unmeasurable is not shortened to 40 % |
| `Floor_BaseGuards` | base 0, −1, NaN, ∞ / — / throws each |
| `Floor_SixMultiplicativeNodesReachIt` | 2.5 s, six `PercentMult` −0.15 / `Effective(2.5, stat.Value)` / 1.0, and five give 1.109 (rule 2) |
| `Floor_FourAdditiveNodesReachIt` | 2.5 s, four `PercentAdd` −0.15 / — / 1.0 exactly (rule 2) |
| `Charge_ObeysTheFloor` | the Oathbound's 2.5 s dash, `Cooldown` driven to 0.2 s / press, `Tick` / ready again at +1.0 s, not +0.2 — `ChargeSkillTests` (rule 3) |
| `Charge_UnmodifiedIsUnchanged` | the shipped spec / press / ready at +2.5 s, `CooldownFraction` as before — the row that says this task changed nothing anyone can feel |
| `Add_TakesOwnership` | one Active / `Add` / `Count` 1, `IdAt(0)`, `CooldownOf(0).Base` is the authored cooldown (rule 5) |
| `Add_Duplicate_Throws` · `Add_NonActive_Throws` · `Add_PastCapacity_Throws` | — / `Add` / throws each (rule 5) |
| `Fresh_SkillIsReady` | one Active, never cast / — / `IsReady` true, `CooldownFraction` 0 |
| `Tick_CastsWhenTheTriggerIsMet` | `HpFraction Below 0.6`, blackboard at 0.5 / `Tick` / one `SkillCast`, the `OnCast` effect applied, `IsReady` false (rules 6, 8, 9) |
| `Tick_DoesNotCastWhenItIsNot` | blackboard at 0.7 / `Tick` / no event, nothing applied |
| `Tick_DoesNotCastWhileCooling` | cast, then trigger still met / `Tick` at +1 s on an 8 s skill / no second cast |
| `Tick_CastsAgainAfterTheCooldown` | same / `Tick` at +8.1 s / a second `SkillCast` |
| `Tick_CastsAtMostOnePerTick` | two actives, both ready, both triggered / one `Tick` / exactly one `SkillCast`, the earlier-added one (rule 6) |
| `Tick_ReachesTheSecondOnTheNextTick` | the same two / a second `Tick` / the other one fires — no starvation (rule 6) |
| `Cast_SourceIsTheActiveSpecNotTheSkill` | a node whose `Effects` and `OnCast` both modify `WeaponDamage` / `Take` then a cast / `CopyModifiersTo` shows two modifiers with **different** sources; `RemoveAll(active)` leaves the take's one standing (rule 8) |
| `Cast_UsesTheEffectiveCooldown` | an 8 s skill floored at 3.2 by a −90 % stack / cast / `SkillCast.Cooldown` 3.2, ready at +3.2 s (rules 1, 9) |
| `Cast_FractionIsOneOnTheCastTick` | a handler reading `CooldownFraction` from inside `SkillCast` / cast / 1 (rule 9) |
| `Cast_MidCooldownBuffMovesTheFillNotTheWait` | cast an 8 s skill, then halve the stat / — / `CooldownFraction` drops, `IsReady` still at the original +8 s (rule 4) |
| `Cast_Directly_IgnoresTheTrigger` | trigger not met / `Cast(0, now, auto: false)` / true, one `SkillCast` with `WasAuto` false (the door M3-07a's manual cast uses) |
| `Cast_Directly_WhileCooling_IsFalse` | cooling / `Cast` / false, no event, nothing applied |
| `Tick_NaNBlackboardCastsNothing` | `HpFraction` NaN, `Below 0.6` / `Tick` / no cast — M3-02a rule 6 from this side (rule 10) |
| `Tick_AllocatesNothing` | seven actives, none triggered, warm-up / 10 000 × `Tick` / allocated-bytes delta == 0 (rule 10) |
| `Cast_AllocatesNothing` | one active with one `ModifyStat`, `SilentEvents`, warm-up / 10 000 × cast-and-expire / allocated-bytes delta == 0 |
| `Session_TicksTheRunnerAfterCombatAndBeforeTheBehaviours` | a trigger reading `HpFraction`, a Husk strike that drops the player below it this tick / `Tick` / the cast happened **this** tick — ordering by observation, `FrameOrderTests`' shape (rule 7) |
| `Session_TicksTheRunnerBeforeTheProjectileStep` | one bolt in the air from last tick, arriving this tick; an active triggered on `IncomingProjectiles AtLeast 1` / `Tick` / the `SkillCast` precedes the `PlayerDamaged` the bolt causes (rule 7) |
| `Session_BoundaryLeavesCooldownsRunning` | cast, then clear the stage / the boundary / still cooling, no `SkillCast` (rule 11) |
| `Session_RestoreAddsTheTakenActives` | a snapshot naming two nodes, one Active / `StartResumed` / `OwnedActiveCount` 1, ready, no `SkillCast` published before `RunStarted` (rule 5) |
| `Runner_ExposesTheCooldownAddress` | three actives / `TryIndexOf`, `CooldownOf` / the index of each, and the very `Stat` the entry holds — `ReferenceEquals`, so M3-12's handler resolves rather than copies (rule 12) |
| `State_HandsOutNoRunner` | reflection over `RunState` / — / `Skills` is not public (rule 13) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend. `DebugOverlay` shows `actives 0` — no tree ships until M3-12, so the runner is correctly empty and says so rather than being invisible.
2. **[Editor]** Dash repeatedly. The Charge's fill and its 2.5 s wait are exactly as before (rule 3's second test row, confirmed by hand once).
3. **[device]** Ledger row 4: whether ~14 float comparisons and a `Stat.Value` walk per frame show on the Profiler's `GC.Alloc` line. Deferred with the rest.

## Out of scope

- **Auto/Manual and the four slots** — M3-07a, which adds a flag to the entry and the two commands. Everything auto-casts here, and rule 6's walk is where M3-07a's gate goes.
- **The buttons and their fills** — M3-10. The reads they poll are rule 13's.
- **Actual actives.** Consecrate and Bulwark are M3-11's, with the cast primitives they need; `ModifyStat` is the only one that exists, so a cast in this task can only move a number.
- **`ModifySkillCooldown`** — M3-12, through rule 12's address.
- **A cast input buffer.** CC §5 gives the movement skill one because a dodge that eats an early press feels unreliable; CC §6.2 gives a slot button 40 % opacity and no tap response instead, which is a different answer to the same problem and the one the design chose for skills.
- **Casting on a cadence other than the tick.** A trigger is asked every tick (rule 10); anything cheaper is M-something's problem and would need a measurement first.

## As built

**Built as specced, with one owner ruling that added a file, three corrections to this spec's own text, and one finding with no owner.**

### The owner's ruling, taken before a line was written

**A tree holding more than `MaxActives` Actives refuses the run at `Start`, and the refusal lives in `RunSession.Start`.** The question was whether rule 5's thirteenth-`Add` throw should stay where it was. It should not, and the argument that settled it is not M3-03's keystone precedent — it is **`SkillTree`'s own constructor**, which sweeps `EffectRegistry.CanApply` over every effect in the tree at `Start` precisely so that `Apply`'s `KeyNotFoundException` is unreachable at pick time. That is this exact seam, already answered once: **sweep the authored content at `Start`, keep the throw as the backstop.** What makes it worth the file is that `Add` is not merely late but *dirty* — M3-08a rule 6 orders `ChooseOffer` as `Take` → `SpendLevelUp` → `Add`, so a thirteenth Active throws **after** the node is recorded, its effects applied and `NodeTaken` published: the player owns a skill that can never fire, the pick is spent, and the exception surfaces in a UI handler.

**Where it went, and why not `TreeRules`.** `TreeRules` gains `ActiveCount` — a *fact* about the tree, counted in the pass that already resolves every node — and `RunSession.Start` does the comparison. The rule is not the tree's: `TreeRules`' other three checks are true of the tree alone and for ever, where *"does this tree fit this run's runner"* is a question about a `Combat` array size that M3-07a's four slots and M7-04's full twenty-seven may both move. Putting `SkillRunner.MaxActives` inside `TreeRules` would also give a class that today imports `Content` and nothing else a dependency on `Combat`. **`TreeRules.cs` is the one file outside the Files table** — two lines and a property — and `RunSession.Start`, which the table already names, carries the refusal.

### Corrections to this spec

1. **Rule 7's count was wrong twice and its conclusion is untouched.** `CombatBlackboard` has **twelve** public fields and `TriggerField` has **nine** members, not eleven. `PlayerCombat.Tick` writes ten of the twelve, of which **seven** are trigger-readable; `ProjectileSystem` writes the eighth (`IncomingProjectiles`). The sentence is *"seven of the nine fields a trigger can read"*, and the ordering argument on both sides of it stands exactly as written. The code comment and the AR §18.1 row both say seven.
2. **`Cast_MidCooldownBuffMovesTheFillNotTheWait` has its direction backwards.** The fill is `remaining / effective`, so halving a cooldown mid-wait makes the same remaining seconds a *larger* share of a shorter cooldown — the fraction goes **up**, not down. `ChargeSkill.CooldownFraction`'s own remark has the same slip. The rule is unaffected and the row tests it in the form that matters: the fill moves (0.25 → 0.5) while `IsReady` still answers at the original moment, and six of the eight seconds have already passed, so a runner that re-based `_readyAt` from the live stat would be ready *that instant*.
3. **Rule 2's *"four additive ones (1 − 0.60 = 0.40, exactly at it)"* is true in real arithmetic and false in float.** Four `PercentAdd −0.15f` sum to −0.6000000238, so the stat is 0.99999994 where `FloorFraction × 2.5f` is exactly 1.0f — a hair *below* the floor rather than on it. The row is green because the floor **catches** it, which is the right reason, and it therefore asserts `Effective(2.5f, stat.Value)` and never the stat's own value. The multiplicative numbers were checked and are right: 0.85⁵ × 2.5 = 1.1093 (above, unfloored) and 0.85⁶ × 2.5 = 0.9429 (below, floored to 1.0).

### One finding, no owner, and it is not this task's to fix

**`TriggerField.Veilrot` reads a field nothing in the build writes.** `PlayerCombat.Tick` says so in as many words, and `CombatBlackboard.Veilrot` is a placeholder until M6-04. So a node authored `Veilrot AtLeast 50` **never fires** and `Veilrot Below 50` **always fires**, silently — and CH §4.2's Rot Nova is exactly the first of those. Nothing in this task's Files table can catch it and no test in this milestone would. **M3-14b** is the right owner rather than M6-04: it already sweeps every shipped asset, and the general form of the check — *a trigger clause reading a blackboard field with no writer* — is an authoring question, where M6-04 merely makes this one instance moot. Recorded for the owner's ruling on whether it earns a ledger row.

### Deviations from the Files table

- **`Core/Progression/TreeRules.cs`** — `ActiveCount`, for the owner's ruling above. Additive: one counter in an existing walk and one property.
- **Test placement.** The Tests table does not assign files. `SkillRunnerTests` holds the floor, the runner and the two **ordering** rows, which need a session of their own. `Session_RestoreAddsTheTakenActives`, `Session_BoundaryLeavesCooldownsRunning` and the new `Start_OverCapacityTreeRefusesTheRun` went to **`RunSessionResumeTests`**, which already owns `BuildWithTree`, `ClearTheStage` and `CrossTheBoundary`; rebuilding a stage machine inside a `Combat` fixture to re-answer them would be the duplication that goes stale. `Rules_CountsTheActives` is in `TreeRulesTests`. All additive rows; no new test file beyond the table's one.
- **`CombatBlackboard`'s class remark** was half stale and would have misled anyone writing rule 7's argument: it said `Veilrot` **and** `IncomingProjectiles` are placeholders, and M2-07 shipped. Corrected, with the one-step staleness recorded on the field itself as the mechanic rather than a bug.

### Decisions worth naming

- **A non-finite `now` is trusted, not guarded — and the guard's *spelling* is what makes that safe.** The snapshot is the door (AR §18.2): `Dt` is clamped by `SnapshotBuilder` and `RunState.Time` is its sum, so every other `Tick` in core trusts the clock it is handed and this one does too, which is the precedent followed. What the class does instead is spell the readiness test `!(now >= _readyAt)` rather than `now < _readyAt`. The difference is not cosmetic: the natural spelling reads an unreadable clock as *off cooldown*, casts, schedules a NaN `_readyAt` that no later comparison can ever be true against, and fires that skill every tick for the rest of the run. Two rows pin it — `Tick_NonFiniteNowCastsNothing` and `Cast_NonFiniteNow_IsFalse` — and both assert that a recovered clock finds a skill that never fired.
- **AR §18.1 gained a row *and* the existing `RunSession.Tick` row was edited.** The sequence row is the sequence, so leaving `skills` out of it would have made it wrong. But the sequence line cannot express rule 7's substance — the failure it prevents is *"a Bulwark shields a wound instead of a bolt"*, not *"the gun aims at where enemies were"* — so the relationship to the projectile step is its own row, on M3-03's precedent. Both halves name the row that goes red under their own swap.
- **`SkillRunner` holds no saved state, and a resumed run's cooldowns come back at zero.** The actives are *derived* from `TakenNodeIds`, which has been in the snapshot since v2, so this task needed no field, no version and no migration — ledger row 2 honoured by writing nothing, for the third time. **Whether a mid-cooldown resume should survive is a real question and is not answered here**: see the ledger note below.
- **A second namespace edge, not a new one.** `Core.Combat` now imports `Core.Effects`, which with `Effects → Combat` (`PlayerStat`) is a cycle — but `Combat → Content → Effects → Combat` already existed, so this shortens a cycle rather than opening one. It joins M3-02a's parking-lot line rather than earning a second.

### Verification

**1329 EditMode / 0 / 0** against M3-04's 1286 — **43 new rows**, which reconciles exactly: the Tests table's **35** named rows (33 lines, one naming three), plus **6** implied guards in `SkillRunnerTests` (`Add_Null_Throws`, `Runner_NullArguments_Throw`, `Runner_IndexGuards_Throw`, `Tick_NonFiniteNowCastsNothing`, `Cast_NonFiniteNow_IsFalse`, `Reset_ClearsTheClocksAndKeepsTheSkills`), plus the **2** this *As built* adds by name (`Rules_CountsTheActives` and `Start_OverCapacityTreeRefusesTheRun`, both for the owner's ruling). No validation row for `SkillCast`: it is an event struct, and no event in the project is validated.

**The two rows worth naming as evidence rather than as counts are the two ordering rows, and each was proved red under its own swap rather than assumed honest.** Moving `State.Skills.Tick` **below** `State.Projectiles.Tick` leaves `Session_TicksTheRunnerBeforeTheProjectileStep` red with `castAt` 2 against `damagedAt` 0 — it still casts, but *after* the wound, which is the precise failure the rule describes and which a row asserting "both happened" would have missed. Moving it **above** `State.Combat.Tick` leaves `Session_TicksTheRunnerAfterCombatAndBeforeTheBehaviours` red at 0 casts where 1 was expected. **Neither swap reddens the other's row**, which is what says the two halves are tested separately rather than by one accident. The ordering was restored and the suite re-run.

**The cost of those two rows is that the fixture plays `SnapshotBuilder`.** A Spitter will not begin a wind-up it cannot see through (M2-11b), and `EnemySense.HasLineOfSight` is a *sense* filled by a Unity adapter — false by default in a core-only test — so the first version of both rows failed with *"the fixture failed to land a bolt on the player"* after 3 000 ticks. The fixture now collects spawned ids off `EnemySpawned` and reports every body back each tick at exactly the 14 m standoff with line of sight: at standoff it has nowhere to back off to, so it fires rather than repositioning for ever, and 14 m is outside the class's 12 m acquire range so nothing ever dies and the fight is stable.

Both new `Core` files and the test file confirmed in their intended assembly through `GetAssemblyNameFromScriptPath`, and all three new types by direct `typeof` — `System.Reflection` is refused in an MCP command, so a `typeof()` that compiles is the available proof and the stronger one. The floor's arithmetic was run through the shipped code rather than trusted: 1.0000 at a 2.5 base, 3.2000 at 8, NaN floored to 1.0000, infinity returned unchanged.

### Ledger

**Rows 1 and 4 both move; neither closes. The other seven were checked row by row rather than trusted.**

- **Row 1** — nothing here raises damage and no content ships, so the TTK arithmetic is where M3-04 left it. What moved is that **rule 1's floor is now a measured fact rather than a design sentence** — 1.0 s at 2.5, 3.2 s at 8, six multiplicative nodes or four additive — which is the ceiling M3-12 authors cooldown nodes against. M3-12c rule 9 already excludes cooldowns from its TTK test by name, so this row's owners are unchanged.
- **Row 4** — **a new per-frame path joins the unmet Profiler `GC.Alloc` row.** Rule 10's arithmetic is ~14 float comparisons and a `Stat.Value` walk per frame for a full tree, and `Tick_AllocatesNothing` and `Cast_AllocatesNothing` pin zero bytes in the Editor over 10 000 iterations. Whether it shows on a phone is the row's, unchanged.
- **Row 2** — honoured by writing nothing, for the third time: no field, no version, no migration. **One question handed forward rather than left silent:** a resumed run's cooldowns come back at zero, because `Add` seeds `_readyAt` at 0 and nothing persists a clock. Cheap to accept — a boundary write happens the frame a stage clears, and the run is then suspended for an unknown wall-clock time — but it is a decision rather than an oversight, and **M3-07b already owns a v3 bump** for Auto/Manual, which is the task that could carry a cooldown field for free if the owner wants one. Recorded there rather than as a new row.
- **Rows 3 and 7** closed already. **Row 5** — no Animator touched. **Row 6** — nothing drawn, no `Color` added; the overlay line is text. **Row 8** — nothing paused. **Row 9** — what this task hands out is `ContentId`s (`SkillIdAt`), never a `LocKey`, so it adds no reader the row counts.
