# M3-12b — Two primitives that are not a stat: a skill's own cooldown, and a swing that shoves

**Size:** M · **Depends on:** M3-12a, M3-06 (the address), M3-05 (the registry) · **Branch:** `m3-12b-cooldown-and-knockback-primitives`
**Design refs:** CH §4, §4.1; CC §4.2, §7; GD §13.1; AR §13, §18.2, §18.3; ADR-0008, ADR-0009 · **Ledger rows:** 1 (a knockback node raises effective DPS without raising damage, and rule 9 says what that does to the TTK test M3-12c owes)

## Goal

The two things v1's twelve nodes want that a `PlayerStat` cannot express: a modifier aimed at **one named skill**, and a swing that starts doing something it did not do before.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/ModifySkillCooldown.cs` | Core | the primitive and its handler — **M3-06 rule 12's promised pair** |
| `Core/Effects/KnockbackOnSwing.cs` | Core | the primitive and its handler |
| `Game/Authoring/ModifySkillCooldownDefinition.cs` | Game | its Inspector half — **block namespace** (Traps §5) |
| `Game/Authoring/KnockbackOnSwingDefinition.cs` | Game | the same — **block namespace** |
| `Tests/Core/Effects/SkillTargetedEffectTests.cs` | Tests.Core | both pairs: what they address, what they refuse, and what removal does |
| *small edits* | | `Core/Combat/PlayerCombat.cs` — `SwingKnockback` (a `Stat`, base 0) and the shove in `ResolveConeHits` (rule 5); `Core/Run/RunSession.cs` registers both handlers beside `ModifyStatHandler`; `Tests/Core/Combat/PlayerCombatTests.cs` gains the shove rows |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Effects;

/// "−25 % Consecrate cooldown". Named at a skill, which is why it cannot be a PlayerStat
/// (M3-06 rule 12: PlayerStat addresses one stat per member, and this addresses one per skill).
public sealed class ModifySkillCooldown : IEffect
{
    public ModifySkillCooldown(ContentId skillId, ModifierKind kind, float value);   // id non-default, value finite

    public ContentId SkillId { get; }
    public ModifierKind Kind { get; }
    public float Value { get; }
}

public sealed class ModifySkillCooldownHandler : IEffectHandler<ModifySkillCooldown>
{
    public ModifySkillCooldownHandler(SkillRunner runner);
    // Apply:  runner.CooldownOf(runner.TryIndexOf(id)).Add(new Modifier(kind, value, source))
    // Remove: the same stat's RemoveAll(source)
}

/// "Your swing knocks enemies back 1.5 m." A rule the cone did not have.
public sealed class KnockbackOnSwing : IEffect
{
    public KnockbackOnSwing(float distance);   // finite and > 0

    public float Distance { get; }
}

public sealed class KnockbackOnSwingHandler : IEffectHandler<KnockbackOnSwing>
{
    public KnockbackOnSwingHandler(PlayerCombat combat);
    // Apply / Remove: a Flat Modifier on combat.SwingKnockback
}
```

```csharp
// PlayerCombat (added)
public Stat SwingKnockback { get; }   // base 0: a swing shoves nobody until a node says so
```

## Behaviour

**The cooldown**

1. **It addresses a skill, and that is the whole reason it exists.** `PlayerStat` gives one member per stat (M3-05 rule 4) and *"−25 % Consecrate cooldown"* names a skill, so it cannot be one — M3-06 rule 12 said exactly this and built `CooldownOf(index)` and `TryIndexOf(id)` so *"that task is a file rather than a refactor."* This is that file.
2. **It resolves through the runner, live, and the `Stat` it reaches is the runner's own.** `TryIndexOf` then `CooldownOf` returns the very instance the entry holds (M3-06's `Runner_ExposesTheCooldownAddress` pins the `ReferenceEquals`), so a modifier lands on the number the runner reads each time it starts a cooldown. Nothing is copied and nothing is re-seeded.
3. **A node for a skill the player does not own is applied to nothing, silently, and is *not* an error.** This is the rule that decides the whole primitive's shape. An Upgrade node is *"only offered if you own the parent"* (CH §4) and `TreeRules` refuses a tree whose Upgrade has no parent in the branch (M3-03 rule 1) — so in authored content the parent is always owned by the time the child can be taken. But a **Passive** may carry one too, and nothing gates a passive. Throwing would make a legal tree crash at a pick; so the handler records the modifier against the id and applies it **when the skill arrives**, and applies it immediately when the skill is already there. A pending modifier is held on the handler, keyed by source, and is spent by `SkillRunner.Add`.

   **The alternative was rejected explicitly:** refusing to apply and forgetting would mean a node taken before its skill does nothing for ever, which is a node that lies. `Take` cannot throw from inside (M3-03 rule 4 guarantees `CanApply`, not that every handler finds its target), so silence plus a later arrival is the only honest answer.
4. **Removal takes the source back from wherever it landed**, whether that is a live stat or the pending list. `Stat.RemoveAll(source)` is the same door M3-05 rule 6 uses, and nothing in M3 removes a node — the path exists because the first timed cooldown buff will want it.

**The knockback**

5. **A stat with a base of zero, and the effect is what lifts it off zero.** `PlayerCombat.SwingKnockback` starts at 0, and `ResolveConeHits` emits an `EnemyKnockbackIntent` per hit enemy only when the value is greater than zero — the machinery already exists, because the Charge has shoved enemies through that same intent since M1-15 and `RunTicker.ApplyKnockbacks` already applies them. So the node adds a `Flat` modifier and a swing starts doing a thing it did not do.
6. **Why a primitive and not a `PlayerStat` member, given that it *is* a stat.** Because `PlayerStat`'s contract is *"every player number that exists as a `Stat` in code"* addressed for **`ModifyStat`**, and a designer authoring *"your swing knocks back"* should not have to know that the implementation is a zero-based stat, pick the right `ModifierKind`, and get `Flat` rather than `PercentAdd` right — `PercentAdd` on a base of zero is zero, silently, for ever. A named primitive with one number makes the wrong authoring impossible. `ModifyStat` remains the tool for numbers the player already has; this is the tool for a rule they did not.
7. **The shove is per enemy hit, in the direction the swing faced**, which is what `ResolveChargeHits` already does for the dash: away from the player along the XZ plane. Not away from the point of impact — a cone is a sweep and there is no single impact point, and the two answers differ most exactly where the cone is widest.
8. **It stacks additively and is clamped at the door.** Two nodes granting 1.5 m give 3 m; a stack driven negative or non-finite emits no intent (`!(value > 0f)`, AR §18.3), because a negative knockback is a pull and nothing in the design has ever asked for one.

**Both**

9. **What these do to ledger row 1, and it is not nothing this time.** A cooldown node raises effective DPS on an Active; a knockback node raises it by **buying time rather than dealing damage** — an enemy shoved 1.5 m spends a moment walking back in. Neither shows up in a naive time-to-kill measurement against a stationary Husk, which is the measurement M3-12c owes. So M3-12c's TTK test must **state the path it measures and exclude these two from it**, and say so — the alternative is a test that passes while the tree does nothing, or fails while the tree is fine.
10. **Both are registered in `RunSession.Start` beside `ModifyStatHandler`**, after the objects exist and before `RunStarted` — so `SkillTree`'s `CanApply` check (M3-03 rule 4) finds them and a tree authored against them is refused at the run's opening rather than at a pick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Cooldown_AppliesToTheNamedSkill` | two owned Actives, A at 8 s and B at 12 s / `ModifySkillCooldown(A, PercentMult, −0.25)` applied / `EffectiveCooldownOf(A)` 6, B unchanged (rules 1, 2) |
| `Cooldown_ReachesTheRunnersOwnStat` | the row above / — / `ReferenceEquals(runner.CooldownOf(0), the stat the modifier landed on)` (rule 2) |
| `Cooldown_ObeysTheFloor` | an 8 s skill, four `PercentAdd` −0.20 / — / `EffectiveCooldownOf` 3.2, not 1.6 — `CooldownRules` still owns the floor (M3-06 rule 1) |
| `Cooldown_ForAnUnownedSkill_IsSilent` | the runner holds nothing / `Apply` / no throw, nothing moved (rule 3) |
| `Cooldown_LandsWhenTheSkillArrives` | apply for A, **then** `runner.Add(A)` / `EffectiveCooldownOf(A)` / reduced — the pending modifier was spent (rule 3) |
| `Cooldown_PendingIsSpentOnce` | the row above / `Add` a second skill / A is reduced once, `ModifierCount` 1 (rule 3) |
| `Cooldown_RemoveTakesBackAPendingOne` | applied for an absent A / `Remove`, then `Add(A)` / A's cooldown is untouched (rule 4) |
| `Cooldown_RemoveTakesBackALiveOne` | applied and landed / `Remove` / `ModifierCount` 0 (rule 4) |
| `Cooldown_Guards` | default id; value NaN, ∞; kind 99 / ctor / throws each |
| `Cooldown_AllocatesNothing` | warm-up / 10 000 × apply-and-remove / allocated-bytes delta == 0 |
| `Swing_ShovesNobodyByDefault` | `SwingKnockback` base 0 / a swing hitting two Husks / zero `EnemyKnockbackIntent` (rule 5) |
| `Swing_ShovesWhenANodeSaysSo` | `KnockbackOnSwing(1.5)` applied / a swing hitting two Husks / two intents, 1.5 m each (rule 5) |
| `Swing_ShovesAwayFromThePlayer` | a Husk to the player's left, one to the right / a swing / each direction is away from the player on XZ, not from a shared point (rule 7) |
| `Swing_StacksAdditively` | two nodes of 1.5 / — / 3 m (rule 8) |
| `Swing_NegativeOrNonFiniteShovesNobody` | the stat driven to −2, then NaN / a swing / no intents either time (rule 8) |
| `Swing_RemoveStopsTheShove` | applied, then removed / a swing / no intents (rule 8) |
| `Swing_IsNotAPlayerStatMember` | reflection over `PlayerStat` / — / no member names swing knockback — rule 6, pinned so nobody adds one later |
| `Swing_Guards` | distance 0, −1, NaN, ∞ / ctor / throws each |
| `Definition_ModifySkillCooldown_RoundTrip` | an id, `PercentMult`, −0.25 through `SerializedObject` / `ToEffect` / the three (M3-02b rule 2) |
| `Definition_KnockbackOnSwing_RoundTrip` | 1.5 / `ToEffect` / a `KnockbackOnSwing(1.5)` |
| `Definition_Invalid_NamesTheAsset` | each with a bad field / `ToEffect` / `ArgumentException` starting with the asset name |
| `Session_RegistersBothHandlers` | a run started / `CanApply` for each / true, and a tree carrying either is accepted (rule 10) |
| `Tree_UnregisteredEffectStillThrowsAtConstruction` | *the existing M3-03 row* / — / unchanged: adding two handlers did not weaken the check |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** With a hand-authored tree: take Consecrate, then take an Upgrade carrying `ModifySkillCooldown(consecrate, PercentMult, −0.25)`. Pause → Skills shows the cooldown drop from 12 s to 9 s on the row (M3-09b rule 3) — the first time a node has visibly changed a number on a screen.
2. **[Editor]** Take a node carrying `KnockbackOnSwing(1.5)`. Swing into a pack of Husks: they all slide back, every swing. Then notice what it costs them — they walk back in, and the stage takes slightly longer. That is rule 9 in play.
3. **[Editor]** Author a Passive carrying a cooldown node for a skill you do not own, and take it first. Nothing happens and nothing throws; take the skill afterwards and the reduction is there (rule 3).
4. **[device]** Nothing specific. Both are arithmetic on paths that already existed.

## Out of scope

- **The twelve nodes** — M3-12c, which authors against these two and M3-12a's five.
- **A third primitive.** v1's twelve want exactly these two plus M3-12a's stats; M3-05's rule is that the eleventh primitive arrives with the node that needs it, and none of the twelve does.
- **An `OnKillTrigger` family** — M3-12a rule 5 routes *"kills heal"* through a stat and says what would earn the trigger instead.
- **Timed cooldown buffs.** Rule 4 leaves the removal path built and unused; `TimedEffects` (M3-11a) is the clock one would hang on.
- **Enemy knockback resistance.** Nothing resists a shove today, including the Charge's; an affix that does is M7-02's and would be an `EnemyStat`.
- **Retuning the Charge's knockback** — M3-12a rule 3 says why 4 m is left alone.

## As built

**Five counted files, size M's ceiling, no split — and the count started at the ceiling rather than the floor, which is the difference from M3-12a.** The Files table is five *new* code files before a single edit: `ModifySkillCooldown.cs`, `KnockbackOnSwing.cs`, the two `*Definition.cs`, and `SkillTargetedEffectTests.cs`. So the question was never *"is this one task"* but *"does any edit push it to six"*, and all three candidates were ruled out loud before a line was written. **`PlayerCombat.cs`**: a `Stat` property, one constructor line, one private `Vector2` field, one line in `TickWeapon` and six inside `ResolveConeHits`' existing loop — additive at every site, no method's control flow or fields moved. **`SkillRunner.cs`** (below): one field, one `internal` method, one call — fifteen lines in a 918-line file. **`RunSession.cs`**: two `Register` lines and one statement moved earlier. All three are [ROADMAP › How to read this](../ROADMAP.md#how-to-read-this)'s *"small additive edits… a new field on a spec, a registration"*. **Not 6, so the pre-named split into M3-12b-i / M3-12b-ii was not taken.**

**Rule 3's mechanism, ruled before writing: a hook on the runner.** The spec asserts that *"a pending modifier is held on the handler and is spent by `SkillRunner.Add`"*, and `Add` publishes nothing, raises nothing and takes no callback — so the mechanism had to be built. `SkillRunner` gains **`internal void WatchArrivals(Action<ContentId, Stat>)`**, invoked as the **last** line of `Add`; the pending table stays on the handler, where the spec puts it. **The two alternatives were priced and rejected.** A flush at the `SkillTree.Take` → `SkillRunner.Add` seam drags `LevelUpFlow.cs` in as a fourth production edit *and* makes every future caller of `Add` responsible for remembering — the runner's own resumed-run call site included. A domain event is banned outright: core does not listen to its own events, and no event carries the thing the listener actually needs, which is the `Stat` instance `Add` has just created. **One watcher per run and a second is refused loudly**, `EffectRegistry.Register`'s rule for its reason — a multicast delegate would make listener order depend on the composition root, and `Runner_TakesOneCooldownHandlerPerRun` is the row.

**The ordering is measured, not argued.** The hook fires after `_cooldowns[_count] = new Stat(...)` and after `_count++`. Moved above the `new Stat` line, **exactly four rows go red** — `Cooldown_LandsWhenTheSkillArrives`, `Cooldown_PendingIsSpentOnce`, `Cooldown_RemoveLeavesAnotherSourceAlone` and `Session_RegistersBothHandlers` — and the other **sixteen** rows in that fixture stay green, which is what makes those four the rows the line belongs to. Recorded in the code beside it.

**The finding the spec does not have: rule 3's pending path is not an edge case — it is the path every resumed run takes.** `RunSession.Start` replays the whole tree through `SkillTree.Restore` and only *then* pushes the resumed run's Actives into the runner, so a cooldown node is applied to an **empty** runner on every resume, whatever order it was taken in. The *"a Passive may carry one and nothing gates a passive"* case rule 3 argues from is the rarer one; the common one is a `Continue`. That makes the held note production wiring rather than a safety net, and `Session_RegistersBothHandlers` drives it end to end through a real session: 12 s → **9 s**, which is manual step 1's own number.

**Ruling on rule 7, because the rule and its test row disagree: the shove is the swing's facing, shared by every enemy one report names.** Rule 7's prose is unambiguous — *"in the direction the swing faced, which is what `ResolveChargeHits` already does for the dash"*, and that method sweeps everything one dash catches the same way — but the Tests table's `Swing_ShovesAwayFromThePlayer` stands a Husk to each side and asks that *"each direction is away from the player"*, which is the **radial** answer the same rule rejects two sentences later (*"not away from the point of impact"*). Shipped as the facing, and the row is **renamed `Swing_ShovesTheWayTheSwingFaced`** with a setup that discriminates: both Husks get the identical direction, **and** the left one's direction is asserted *not* to be `normalize(husk − player)`. As written the row would have been vacuous — two enemies swept the same way satisfy *"each is away from the player"* trivially, so it would have gone green against either implementation.

**`ResolveConeHits` has no facing in scope, and it is a field rather than a parameter.** The answer arrives at least a frame later from a body that was handed the facing on the `ConeHitIntent` and has no reason to hand it back, so a parameter would mean the body remembering core's geometry across a round trip *and* `RunSession.ReportConeHits`, `IRunCommands` and `RunTicker` all carrying it — to say something core already knew when it asked. `_pendingConeFacingXZ` is written on the damage frame beside `PendingConeRequestId`, which is where the rest of the pending cone lives. `Swing_ReadsTheFacingTheSwingWasThrownWith` is the row: five frames of the player facing the other way between the damage frame and the report, and the shove still goes the way the swing was thrown.

**`SwingKnockback` is a second base-0 `Stat` and it carries M3-12a's warning verbatim.** `Stat` computes `(Base + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult)`, so a percentage on a base of zero is zero, silently and for ever — a probe read **+500 % → 0**. Rule 6's *"wrong authoring is impossible"* holds **for the doors that exist**: `KnockbackOnSwing` carries one distance and no kind, its handler chooses `Flat`, and `Charge_KnockbackIsNotAddressable` already asserts no `PlayerStat` member names knockback. **It is pinned with a row anyway rather than left to the argument**, because the property is `public` and the arithmetic will not have changed the day a second door reaches it: `Swing_TheHandlerPicksTheOnlyKindThatCanWork` asserts 1.5 off one modifier on a base of zero, which **only `Flat` can produce** — a behavioural proof rather than an inspection, and the row that goes red the day somebody "tidies" the handler into taking an authored kind.

**`TryIndexOf` is `bool TryIndexOf(ContentId, out int)`, so the spec's `runner.CooldownOf(runner.TryIndexOf(id))` does not compile.** A text slip, not a design change: `CooldownOf(int)` and `EffectiveCooldownOf(int)` exist exactly as M3-06 rule 12 promised, and the handler resolves through the two-step.

**Deviations from the Files table, all named.**
1. **The three `Definition_*` rows cannot live where the Tests table puts them.** It assigns them to `SkillTargetedEffectTests.cs` in **Tests.Core**, and both definitions are `ScriptableObject`s in **Soulvail.Game**, which Tests.Core cannot see. They landed in the existing `Tests/Game/Authoring/SkillAuthoringTests.cs`, where M3-02b's, M3-11a-ii's and M3-11b's definition round-trips already live. **Renamed, because `Definition_Invalid_NamesTheAsset` already exists there** for `SpawnHealZone` — they are `SkillCooldown_*` and `Knockback_*` on the `GrantShield_*` precedent. **Seven rows rather than three**: each definition owes a round trip, an invalid row and a `MonoScript` link row (Traps §5), and the cooldown's `OnValidate` owes the warn row `ModifyStatDefinition`'s has.
2. **The six shove rows landed in `StatReachTests.cs`, not `PlayerCombatTests.cs`.** That file already drives a Censer to its damage frame and reports a wedge back (`SwingOnce`, `WithHusk`, `Enemies`); `PlayerCombatTests` has no `EnemySystem` and no damage-frame helper, so the spec's placement meant duplicating forty lines of fixture to watch one intent. `PlayerCombatTests` is therefore **not** edited at all, against the Files table's *small edits* note.
3. **`Core/Combat/SkillRunner.cs` is a fourth production edit the table does not list** — rule 3's mechanism, ruled above.
4. **`Tests/Core/Effects/PlayerStatCoverageTests.cs` gained `Swing_IsNotAPlayerStatMember`** beside `Charge_KnockbackIsNotAddressable`, reusing that file's `NamesAnythingLike` helper rather than duplicating it. One fragment now protects two claims. A second row, `Swing_TheStatItselfStartsAtZeroWithNoModifiers`, pins that the stat ships clean.
5. **`RunSession`'s `SkillRunner` construction moved above the `SkillTree`'s.** `ModifySkillCooldownHandler` holds the runner and `SkillTree`'s constructor sweeps every effect through `CanApply`, so leaving the runner below the tree would refuse any run whose tree carried a cooldown node — rule 10's requirement in reverse. Nothing between them depended on the tree.
6. **A `MaxPending` ceiling (32) and its throw, which the spec does not ask for.** **It is not the throw rule 3 forbids**: a node for a skill you do not own is legal, reachable and silent, where thirty-three unspent ones at once is not reachable from any tree `TreeRules` accepts. `SkillRunner.Add`'s capacity throw exactly — a backstop that would otherwise be a silent overwrite. `ModifySkillCooldownHandler.PendingCount` is public for the same reason `HandlerCount` is: rule 3's silence is not observable without it.
7. **`Docs/Traps.md` is edited**, which no Files table lists — §4's `System.Numerics` bullet is narrowed by the finding below, on the precedent M3-03, M3-04 and M3-07a set for a toolchain finding worth the next task's time.
8. **`ActiveSpec` refuses an empty `onCast`** (*"an active must do something when it fires"*), found by a probe rather than by reading, so the fixture's Actives carry a `ModifyStat` — `SkillRunnerTests.Active`'s shape.

**Verified:** **1 802 EditMode / 0 / 0, twice consecutively on the final code** (four full runs in all, plus one filtered swap run), against M3-12a's 1 766 — **36 new rows, and the arithmetic lands to the row.** The spec's Tests table is **23 lines**, of which one (`Tree_UnregisteredEffectStillThrowsAtConstruction`) names an **existing** M3-03 row that was left alone and is still green. The remaining 22 became 36 where they landed: **20** in `SkillTargetedEffectTests`, **7** in `StatReachTests`, **7** in `SkillAuthoringTests`, **2** in `PlayerStatCoverageTests`. The 14 over the spec's count are the four splits and the implied guards this spec's own note owes — `Definition_Invalid` split per definition, the two `MonoScript` link rows, the `OnValidate` warn row, `Handler_Guards` (null constructor, null effect and null source on **both** handlers), `Cooldown_PendingIsRefusedPastTheCeiling`, `Runner_TakesOneCooldownHandlerPerRun`, `Cooldown_RemoveLeavesAnotherSourceAlone`, `Swing_TheHandlerPicksTheOnlyKindThatCanWork`, `Swing_ReadsTheFacingTheSwingWasThrownWith`, `Swing_TheStatItselfStartsAtZeroWithNoModifiers`, `Session_ARunWithNoTreeStillStarts`, and the `Swing_StacksAdditively` / `Swing_Remove*` pairs that exist once against the handler and once against the swing. **PlayMode 16 / 16, the count unmoved, `Ticker_RunsTheStepsInOrder` fired and green**; `RunSession.Start` gained two registrations and `Boot_ReachesMenu_WithinFiveSeconds` still starts a run. **Four assemblies** — `Soulvail.Core` ×5, `Soulvail.Game` ×2, `Soulvail.Tests.Core` ×3, `Soulvail.Tests.Game` ×1 — no fifth, so no ripple. **No prefab and no scene moved.** Console **36 / 11 errors / 25 warnings** after the last EditMode run, with one EditMode run queued after PlayMode so the sweep had something to read: **errors unchanged at 11**, warnings **23 → 25**, and both new ones are this task's own `ModifySkillCooldownDefinition` `OnValidate` lines in the family `ModifyStatDefinition 'WarnsOnStaleAddress'` already occupies. `ProjectSettings/TimeManager.asset` dirtied and reverted for the **thirteenth time in fourteen tasks** — the same Unity re-serialisation of `Fixed Timestep` into its rational form, an identical 0.02.

**The compile was probed by calling the new API, not by trusting a green compile.** A `RunCommand` built a `SkillRunner`, added Actives at 8 s and 12 s, and read **8 → 6 on the named one and 12 unchanged on the other**, 1 modifier against 0; the floor held at **3.2, not 1.6**, under four additive −20 %; a modifier applied to an empty runner reported `pending=1 owned=0` and became **6 s with 1 modifier and 0 pending** after `Add`; and a second handler over one runner was refused. A second probe read `SwingKnockback` at **base 0, value 0, 0 modifiers**, then **1.5** off one `KnockbackOnSwing`, **3** stacked, **1.5** after one source came back — with the base still 0.

**Two Traps entries held and one is confirmed narrower than filed.** The rewriter hoisting a nested class out of `CommandScript` cost one probe before the helper was moved beside it (§4, already a row). **§4's *"no `System.Numerics` reference, so no command can call a core method taking a `Vector2`"* is true of `Vector2` itself and not of a struct that merely holds one**: `IIntentSink` was implemented whole inside a probe — `ConeHitIntent`, `ChargeIntent` and `EnemyKnockbackIntent` all name `Vector2` fields — and compiled clean, because the *signatures* name only Soulvail types. Worth knowing: it is what let the knockback half be probed against a real `PlayerCombat`.

**Manual verification:** step 4 is nothing. **Steps 1–3 all need hand-authored assets and none is runnable as it stands**, because `Data/` holds no tree asset at all — the same gap M3-12a recorded. What each needs is Inspector work and no code: step 1 wants a `ModifySkillCooldownDefinition` (its skill id typed as a string, `PercentMult`, −0.25), a `SkillDefinition` carrying it, a `SkillDefinition` for Consecrate, a `SkillTreeDefinition` and a `BootScope._trees` drop; step 2 swaps the effect asset for a `KnockbackOnSwingDefinition` at 1.5; step 3 is step 1's tree with the cooldown node taken first. **Step 1's claim that it is *"the first time a node has visibly changed a number on a screen"* is about M3-09b's row, not about this task's code**, and that row is shipped — `RunState.SkillCooldownSeconds` is what it prints, and `Session_RegistersBothHandlers` asserts exactly the 12 → 9 the screen would show.

**Ledger: row 1 moves, and this time it moves rather than only gaining reach.** Both primitives raise **effective DPS without raising damage per hit** — a shorter cooldown fires an Active more often, and a shove buys time rather than dealing damage — so **M3-12c's TTK test must name the path it measures and exclude these two**, and say so in its own text. A test that counted them would **pass while the tree did nothing**: a stationary-Husk time-to-kill is blind to a knockback, and an Active's cadence is not on the basic attack's path at all. **The row is not closeable by M3-12c alone on this task's evidence — but nothing here is what is outstanding.** What M3-12c owes is the TTK test at stage 1 / 15 / 30 plus the twelve nodes; what M3-12a left is reach; what this task leaves is the exclusion clause above. All three are M3-12c's, so **the row closes with M3-12c provided its TTK test states its exclusions** — and it is the *stating* that is the new condition, because an unstated one is indistinguishable from an oversight the next time somebody reads the row. **Row 4** gains one `Stat.Value` read and one comparison per swing on the `ResolveConeHits` path, and a `SkillRunner.Add` that now makes one delegate call. **No `AllocationAssert` row was added for the swing**, deliberately and for M3-12a's reason; `Cooldown_AllocatesNothing` walks **both** the live and the pending path, because a row that measured only the cheap one would pass against a pending table that allocated.
