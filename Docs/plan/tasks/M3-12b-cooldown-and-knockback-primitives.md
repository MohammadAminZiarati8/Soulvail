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

_Filled at merge._
