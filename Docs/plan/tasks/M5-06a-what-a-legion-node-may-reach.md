# M5-06a — What a Legion node may reach: a target, a trigger and a verb

**Size:** M · **Depends on:** M5-04b · **Branch:** `m5-06a-legion-effects`
**Design refs:** CH §3.2, §4.2, §5; AR §10.1, §14, §18.2, §18.3; ADR-0006, ADR-0008 · **Ledger rows:** none — this task answers the `StatTarget` question [M5-04b](M5-04b-rise-and-minion-stats.md) rule 9 carried to M5-06a, which is not a ledger row

## Goal

A tree node can say the three sentences CH §3.2's Legion branch and CH §4.2's Exhume are made of and
that nothing in the build can express: *"+20 % minion damage"*, *"raise three Wights"*, and
*"auto-cast when I have fewer than half my Wights."*

## Why this is a task and not part of M5-06b

The ROADMAP's single M5-06 row owns the Gravecaller's twelve nodes, [ledger row 5(i)](../ROADMAP.md#carry-forward-into-m5)
and the `StatTarget` question. Counted against the shipped code that is eight counted files, and the
halves are reviewed against different documents: **what a node may address** is AR §10.1 and
ADR-0008 — a primitive, an address space and a refusal — while **the tree** is CH §3.2 and §5, and
what a *level* is worth is CH §5.2. It is [M3-12a](M3-12a-addressable-stats.md) / [M3-12c](M3-12c-oathbound-tree-v1.md)
one milestone on, and the same split for the same reason: M3-12a widened the address space with no
content walking through it, and M3-12c authored the content.

**The verb is here rather than beside the node that casts it**, which is where M5-06b would have put
it: a primitive and its handler is a file, a `Register` line and its own review (M3-11a and M3-11b
each spent a whole task on one), and folding it into a twenty-seven-asset content PR would bury it.

**This task ships used by nothing**, which is M4-01a's bargain and what keeps the review about the
mechanism.

## The three things a Legion node wants, and which of them ship

CH §3.2 gives Legion the minion cap (The Host's +4), the rise chance and the Wights themselves.
Against the shipped code:

| Wanted | Where the number lives after M5-04a/b | Ships here |
|---|---|---|
| **Minion damage / HP / speed** | `MinionAgent.ContactDamage`, `MaxHp`, `MoveSpeed` — three `Stat`s per body, created at `Spawn` | **Yes**, rules 1–4 |
| **Raise Wights on demand** | nowhere — `MinionSystem.Spawn` exists and no effect calls it, so CH §4.2's Exhume has a system and no verb | **Yes**, rules 10, 11 |
| **Minion cap +4** | `MinionSystem.Cap`, a `Stat` (M5-04a rule 3) | **No** — it is The Host, a **Keystone**, and v1 ships none (M5-06b rule 2, M3-02a rule 8). It needs a `PlayerStat` member, which is M7-04's with the Keystone |
| **Rise chance +10 %** | `RisePassive.Chance`, a `Stat` (M5-04b rule 5) | **No**, for the cap's reason and one more: both are *run-level* numbers with no `PlayerStat` address, and inventing two members for nodes v1 does not author is guessing at the shape M7-04 will want |

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/MinionRecipe.cs` | Core | The three numbers a Wight is *born with*, and the `IStatBlock` over them |
| `Core/Effects/ModifyStat.cs` | Core | **Substantial.** `StatTarget.Minions`, and the third block in `ModifyStatHandler.Block` |
| `Core/Effects/RaiseMinions.cs` | Core | CH §4.2's Exhume as a primitive: the effect and its handler, one file — `ModifyStat.cs`'s shape (rules 10, 11) |
| `Game/Authoring/RaiseMinionsDefinition.cs` | Game | The Inspector half — `GrantShieldDefinition`'s shape, **block namespace** ([Traps §5](../../Traps.md)) |
| `Tests/Core/Effects/LegionEffectsTests.cs` | Tests.Core | One fixture for the three things a Legion node may do: the block, the aim, the refusal, the verb, and the twenty seconds of lag |
| *small edits* | Core | `MinionSystem` holds the recipe and `Spawn` seeds from it (rule 3); `CombatBlackboard` gains `MinionCount`; `PlayerCombat.UpdateBlackboard` writes it; `TriggerSpec`'s enum and `TriggerClause.IsMet` gain the member; `TriggerText.KeyFor` gains two pairs; `RunSession.Start` builds the recipe, registers the handler and refuses a mismatched tree (rules 5, 11) |
| *ripple* | Tests.Core, Tests.Game | `SkillSpecTests.Trigger_EveryFieldReads` already walks the enum and needs the new case to exist (rule 6); `ContentValidationTests` gains `EveryTriggerField_HasAWriter` (rule 8); `EffectRegistryTests` gains the sixth primitive; `English.asset` gains two trigger rows |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Effects/MinionRecipe.cs
/// <summary>
/// What the next Wight is born with. Three run-level Stats, seeded from MinionSpec and read by
/// MinionSystem.Spawn — the numbers a Legion node moves, one place rather than once per body.
/// </summary>
public sealed class MinionRecipe
{
    public MinionRecipe(MinionSpec spec);

    /// <summary>Mirrors MinionAgent's three (M5-04a rule 2), and nothing else.</summary>
    public Stat MaxHp { get; }
    public Stat MoveSpeed { get; }
    public Stat ContactDamage { get; }
}

/// <summary>An IStatBlock over the recipe. MinionStats' mirror, one level up — rule 2.</summary>
public sealed class MinionRecipeStats : IStatBlock
{
    public MinionRecipeStats(MinionRecipe recipe);
    public Stat Resolve(PlayerStat stat);
    public bool Has(PlayerStat stat);
}

// Core/Effects/ModifyStat.cs — widened
public enum StatTarget
{
    Player,
    Self,

    /// <summary>
    /// The run's minions, aimed at the recipe rather than at the bodies — rule 1. Legal only on a
    /// class with a MinionSpec; a run whose tree aims here without one is refused at Start (rule 5).
    /// </summary>
    Minions,
}

public sealed class ModifyStatHandler : IEffectHandler<ModifyStat>
{
    /// <param name="minions">
    /// This run's minion recipe, or null on a class that has none. Null is a real answer and the
    /// throw that follows it names the class — rule 5.
    /// </param>
    public ModifyStatHandler(IStatBlock player, IStatBlock minions = null);
}

// Core/Content/TriggerSpec.cs — widened
public enum TriggerField
{
    // … the nine that exist …

    /// <summary>
    /// Wights standing right now. CH §4.2's Exhume fires below half the cap; zero on a class with
    /// no minions, which is what makes the clause false rather than absent (rule 7).
    /// </summary>
    MinionCount,
}

// Core/Effects/RaiseMinions.cs
/// <summary>
/// CH §4.2's Exhume, as data: stand this many Wights up around the player, now. The first effect
/// primitive in the game whose subject is neither a Stat nor the player — rule 10.
/// </summary>
public sealed class RaiseMinions : IEffect
{
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="count"/> is not positive, or <paramref name="radius"/> is not a finite
    /// number greater than zero.
    /// </exception>
    public RaiseMinions(int count, float radius);

    /// <summary>How many to try to stand up. Three — CH §4.2's "raise 3 Wights".</summary>
    public int Count { get; }

    /// <summary>Metres from the player each one appears at. Ours — rule 10.</summary>
    public float Radius { get; }
}

public sealed class RaiseMinionsHandler : IEffectHandler<RaiseMinions>
{
    /// <param name="blackboard">
    /// Where the player is. Read rather than passed per cast, which is SpawnHealZoneHandler's own
    /// shape — ZoneSystem reads PlayerPosition off the blackboard for exactly this reason.
    /// </param>
    /// <param name="clock">The simulated second a raised Wight's twenty starts from.</param>
    public RaiseMinionsHandler(
        MinionSystem minions, CombatBlackboard blackboard, SimulatedClock clock);

    public void Apply(RaiseMinions effect, object source);

    /// <summary>Does nothing, and rule 11 is why.</summary>
    public void Remove(RaiseMinions effect, object source);
}
```

## Behaviour

1. **`StatTarget.Minions` aims at the *recipe*, not at the bodies, and that is what makes it one
   line rather than a selection rule.** M5-04b rule 9 named the gap: `ModifyStat` carries
   `{ Player, Self }` and neither spells *"every Wight I own"*. M4-01a rule 3 rules that an effect
   aimed at *someone else* is a different primitive with a selection rule — which target, chosen
   how, what happens when there is none. **A third member escapes that rule because the target is
   not a someone**: there is exactly one minion recipe per run, it exists for the run's whole life,
   and it is found without choosing anything. `ModifyStatHandler.Block` gains one case beside
   `Player` and `Self`; `Aiming` is untouched and does not nest around this.
2. **`MinionRecipeStats` answers the same three addresses `MinionStats` does, and refuses the rest
   by name.** `MaxHp`, `MoveSpeed`, `ContactDamage` — M5-04a rule 2's three, M5-04b rule 7's shape,
   and a fourth implementation of `IStatBlock` rather than a generalisation of the first three, for
   M5-04b rule 6's reason unchanged: the throw has to name *which* content is wrong, and
   `"the minion recipe has no stat at address WeaponRange"` is a different fact from
   `"'minion.wight' has no stat at address WeaponRange"`. A row walks every `PlayerStat` member and
   asserts `Has` and `Resolve` agree, as the other three have since M4-01a.
3. **`MinionSystem.Spawn` seeds each new agent's three `Stat`s from the recipe's live values, and a
   Wight already standing does not change.** This is the whole cost of aiming at the recipe and it is
   stated rather than discovered: a node taken while three Wights are up buffs the *fourth*. **The
   lag is bounded by CH §3.2's own twenty seconds** — every Wight in the arena is replaced within a
   lifespan — and the alternative is worse in kind rather than in degree: walking the live agents and
   adding a `Modifier` to each would mean the modifier stack on a body that is about to be recycled,
   a removal path for a node nothing removes (CH §7 deleted respec), and the question of what happens
   to a Wight raised *after* the walk. **A recipe is the honest object**: it is what a Wight is born
   with, and a node changes what being born means.
4. **The recipe is seeded from `MinionSpec` and the spec is untouched.** `new MinionRecipe(spec)`
   copies the four authored numbers into three `Stat`s at `RunSession.Start`; `MinionSpec` stays the
   shared immutable authored data every other spec is (AR §10.1), and two runs of the same class hold
   two recipes with independent stacks. `Reach`, `AttackInterval`, `Cap` and `Lifespan` are **not**
   on the recipe: the first two are not `Stat`s on an agent either (M5-04a), and the last two are the
   two addresses this task refuses (see the table above).
5. **A tree that aims at `Minions` on a class with no minions refuses the *run*, not the pick.**
   `ModifyStatHandler`'s `minions` block is null for an Oathbound, and `EffectRegistry.CanApply`
   cannot catch it — that method answers *"is there a handler for this type"* and nothing more
   (`SkillTree`'s own remarks), so a `ModifyStat` aimed at `Minions` passes the constructor sweep and
   would throw at the moment a player tapped the card. **`RunSession.Start` therefore sweeps the
   tree's take and cast effects for a `Minions` target and refuses before `RunStarted`** when
   `CharacterSpec.Minions` is null, naming the node and the class. It is `TreeRules`' own argument —
   an authoring mistake refuses the run rather than the pick — and it is where the Active-count check
   already lives. **The handler still throws if it is reached**, for `Self`-outside-a-scope's reason
   (M4-01a rule 5): a fallback to the player would move a real number by the authored amount and
   nothing would report it.
6. **`TriggerField.MinionCount` is a member, a clause, a blackboard field and a writer — four edits,
   and none of them optional.** CH §4.2 authors Exhume as *"Wight count < half of cap"*, and there is
   no field for it: the nine that exist are HP, shield, three enemy counts, projectiles, Veilrot,
   stationary time and focus ramp. `CombatBlackboard` gains `MinionCount` beside
   `EnemiesInAcquireRange`; `PlayerCombat.UpdateBlackboard` writes it from the count it is handed, in
   the same block that writes the other five; `TriggerClause.IsMet` gains its case, which
   `Trigger_EveryFieldReads` already walks `Enum.GetValues` to enforce; and `TriggerText.KeyFor`
   gains `trigger.minionCount.below` and `.atLeast` with two English rows, which
   `EveryTriggerKey_ResolvesInEnglish` already enforces.
7. **The threshold is a number, not a fraction of the cap, and that is a deliberate loss.** CH §4.2
   says *"< half of cap"*; `TriggerClause` compares a field against a `float` threshold, and a clause
   that meant *"half of whatever the cap currently is"* would be the first one in the game whose
   threshold is computed rather than authored — a second kind of clause, for one skill. So Exhume is
   authored `MinionCount Below 2` against CH §3.2's base cap of 3, and **if The Host ever raises the
   cap to 7 the trigger is wrong by design**: it fires below 2 rather than below 3.5. It costs
   nothing today because The Host is a Keystone v1 does not ship, and it is written here so M7-04
   knows it inherits a clause and not just a Keystone. **On a class with no minions the field reads
   zero**, so `Below 2` would be permanently true — which is why rule 5's refusal is about the
   *effect* and CC §6.3's trigger line is about the *skill*: an Oathbound cannot own Exhume, because
   Exhume is in the Gravecaller's tree and a tree belongs to one class.
8. **The starve check finally lands, and it is one row.** `TriggerField.Veilrot` reads a field
   nothing in the build writes — found at M3-06, handed to M3-14b, and **M3-14b shipped without
   taking it**, which nothing in the protocol noticed. The general form is an authoring question and
   this is the task that adds a tenth field, so this is the moment:
   `ContentValidationTests.EveryTriggerField_HasAWriter` asserts that every `TriggerField` a
   *shipped* asset authors is one `PlayerCombat.UpdateBlackboard` writes, and **`Veilrot` is the one
   it would catch** — which is exactly why [M5-06b](M5-06b-gravecaller-tree-v1.md) rule 6 authors Rot
   Nova without its Veilrot clause. The row is written so the check is on the *authored* fields
   rather than on the enum, because `Veilrot` must stay a member: M6-04 writes it, and deleting a
   field to satisfy a test would be the worst possible reading of one.
9. **Nothing here allocates.** Three `Stat`s per run built once, a jump table over an enum in
   `Resolve`, one `int` on the blackboard written in a block that already runs every tick, one
   comparison in `IsMet`, and a cast path that is three calls into a system holding a preallocated
   array of eight.

### The verb

10. **`RaiseMinions` places its Wights on a derived ring, and draws nothing.** Three at
    `Radius` = **2.0 m** — ours, far enough that a Wight is not inside the player's own silhouette
    and near enough to be read as *yours* — spaced evenly and anchored to the player's facing, which
    is `BossBehaviour`'s own *"summons a phase's adds on a derived ring"*. **No stream is consulted**:
    ADR-0011 says a draw is a change to what a seed means, and a cast the player chose the moment of
    is the worst place to spend one — two runs on one seed would diverge on the frame a thumb landed.
    It is the first primitive in the game whose subject is neither a `Stat` nor the player, and the
    argument it does *not* make is that it is therefore a new kind of thing: it is a verb with an
    authored count, the same shape `SpawnHealZone` has.
11. **A refused spawn is silent and `Remove` does nothing.** `MinionSystem.Spawn` returns null at the
    cap (M5-04a rule 3), so Exhume at a full army raises fewer than three — or none — and neither
    throws nor reports: `ProjectileSystem.Fire`'s rule, and what the trigger in rule 7 exists to keep
    rare. `Remove` is `SpawnHealZoneHandler.Remove`'s answer verbatim — *"every source holds
    nothing, because what a cast left behind is a place rather than a modifier"* — with the null
    guards kept so a mis-wired caller is refused the same way at every handler's door. **Nothing
    calls it**: no minion is ever held in `TimedEffects`.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Recipe_CarriesTheAuthoredNumbers` | the Gravecaller's `MinionSpec` / constructed / `MaxHp` 20, `MoveSpeed` 3.0, `ContactDamage` 8, each a live `Stat` at its base |
| `Recipe_ImplementsTheBlock` | a recipe / through `IStatBlock` / the three addresses return the recipe's own live `Stat` instances, not copies |
| `Recipe_RefusesAnAddressItDoesNotHave` | `Resolve(WeaponRange)` / — / throws, and the message names the address **and the recipe** — rule 2 |
| `Recipe_HasAgreesWithResolve` | every `PlayerStat` member / `Has` vs `Resolve` / true exactly when `Resolve` does not throw |
| `Recipe_AndMinionStatsAnswerTheSameSet` | `MinionRecipeStats` and `MinionStats` / every member / the same three answered, the same nine refused, **different messages** — rule 2 |
| `Recipe_TwoRunsDoNotShareAStack` | two recipes off one `MinionSpec`, a `Flat +10` on the first / — / the second's `ContactDamage` is unmoved, and `MinionSpec` itself is unchanged — rule 4 |
| `Minions_AimedAtMinionsMovesTheRecipe` | `ModifyStat(ContactDamage, PercentAdd, +0.2, Minions)` / applied / the recipe's damage is ×1.2 and **the player's is not** — rule 1 |
| `Minions_ANewWightIsBornWithTheBuff` | the buff applied, then `Spawn` / — / the new agent's `ContactDamage.Value` is 9.6 |
| `Minions_AStandingWightDoesNotChange` | one standing, then the buff applied / — / its `ContactDamage.Value` is still 8, and the next spawn's is 9.6 — **rule 3's stated cost** |
| `Minions_TheLagIsBoundedByTheLifespan` | one standing, the buff applied / ticked `MinionSpec.Lifespan` / every Wight in the arena carries the buff — rule 3's bound, measured |
| `Minions_AimingIsNotNeeded` | a `Minions` effect applied with no `Aiming` scope open / — / it applies, and no scope is opened — rule 1, so nobody wraps this in a `using` |
| `Minions_WithoutARecipeThrowsAndNamesTheClass` | a handler built with a null minion block / a `Minions` effect / throws; the message says the class has no minions and **does not** touch the player's numbers — rule 5 |
| `Start_RefusesAMinionNodeOnAClassWithoutMinions` | an Oathbound tree carrying one `Minions` effect / `RunSession.Start` / throws before `RunStarted`, naming the node and `character.oathbound`; nothing is published — rule 5 |
| `Start_AcceptsItOnTheGravecaller` | the same effect on a class with a `MinionSpec` / `Start` / no throw, and the run announces normally |
| `StatTarget_HasThreeMembersAndTheDefaultIsPlayer` | the enum and `ModifyStat`'s constructor / — / three members, `Player` default, `Enum.IsDefined` refuses a fourth — M4-01a's widening rule, held |
| `Trigger_MinionCountReads` | a blackboard with three Wights / `MinionCount Below 2` and `AtLeast 2` / false and true — rule 6 |
| `Trigger_EveryFieldReads` | *(existing)* every `TriggerField` / `IsMet` / no member falls through — the row that made rule 6 unavoidable |
| `Trigger_MinionCountIsWrittenEveryTick` | a run with Wights raised and expired / ticked / `CombatBlackboard.MinionCount` tracks `MinionSystem.Count` on every tick, and is **zero** on a class with no minions — rules 6, 7 |
| `Trigger_MinionCountHasItsTwoKeys` | `TriggerText.KeyFor` / both comparisons / `trigger.minionCount.below` and `.atLeast`, both present in `English.asset` |
| `Content_EveryTriggerFieldHasAWriter` | every shipped `SkillDefinition`'s clauses / against `UpdateBlackboard`'s writes / all written — and the row fails loudly on a `Veilrot` clause, which is why [M5-06b](M5-06b-gravecaller-tree-v1.md) rule 6 authors none — rule 8 |
| `Raise_StandsUpItsAuthoredCount` | `RaiseMinions(3, 2.0)` with the player at the origin / applied / three Wights, all within 2.0 m, three `MinionSpawned` — rule 10 |
| `Raise_IsOnADerivedRingAndDrawsNothing` | a counting `IRandom` / ten casts / every stream unadvanced, and two casts from the same player position and facing produce the **same three points** — rule 10 |
| `Raise_FollowsThePlayer` | the player at (12, 0, −4) / a cast / the three are around that point, read off the blackboard rather than passed in |
| `Raise_AtTheCapRaisesWhatItCan` | the cap at 3 with two standing / `RaiseMinions(3)` / one more stands, no throw, no event for the two refused — rule 11 |
| `Raise_AtAFullArmyIsSilent` | the cap reached / a cast / nothing stands, nothing throws, nothing is published |
| `Raise_TheNewWightsCarryTheRecipe` | `+20 % ContactDamage` aimed at `Minions`, then a cast / — / all three are born at 9.6 — rules 3 and 10 meeting |
| `Raise_RemoveDoesNothing` | a cast, then `Remove` with the same source / — / the three still stand, and a null effect or source still throws — rule 11 |
| `Raise_RefusesAnImpossibleCount` | count 0, count −1, a non-finite or non-positive radius / constructed / throws in each case, naming the field |
| `Raise_AllocatesNothing` | 10 000 cast-and-expire cycles / `AllocationAssert.None` / zero |
| `Registry_HoldsSixPrimitives` | the run's `EffectRegistry` / — / `ModifyStat`, `GrantShield`, `SpawnHealZone`, `KnockbackOnSwing`, `ModifySkillCooldown` and `RaiseMinions`, and `CanApply` answers for all six |
| `Recipe_AllocatesNothing` | 100 000 `Resolve` calls and 10 000 spawns / `AllocationAssert.None` / zero |

**Guard rows are implied, not listed:** a null `MinionSpec` to the recipe, a null `MinionRecipe` to
the block, a null `MinionSystem`, `CombatBlackboard` or `SimulatedClock` to the handler, and
`Enum.IsDefined` on the widened `StatTarget` and `TriggerField`.

## Manual verification (Editor / device)

_None._ Nothing authored reaches any of this: no shipped asset carries a `Minions` target or a
`MinionCount` clause until [M5-06b](M5-06b-gravecaller-tree-v1.md), and the Gravecaller is not
selectable until M5-07. Every run this build plays is byte-identical in behaviour, which is M4-01a's
bargain and what keeps this PR's review about the mechanism.

## Out of scope

- **The twelve nodes, and any asset that uses this** — including the `Exhume.asset` that casts
  rule 10's verb and the `RaiseMinions.asset` behind it. [M5-06b](M5-06b-gravecaller-tree-v1.md).
- **Tether and Rot Nova**, the other two Actives the ROADMAP's M5-06 row names. Each needs a
  primitive of its own — a per-enemy link with a lifetime, and radial damage — and each is M3-11a's
  or M3-11b's size. [M5-06b](M5-06b-gravecaller-tree-v1.md) rule 3 rules on them; this task builds
  the one verb the twelve nodes actually use.
- **`PlayerStat.MinionCap` and `PlayerStat.RiseChance`.** The table above: both are The Host's and
  Legion's Keystone-tier nodes, v1 ships no Keystone, and two members for content nobody authors is
  guessing at M7-04's shape.
- **A selection rule, a threat table, or `StatTarget` growing a fourth member.** Rule 1 escapes
  M4-01a rule 3 precisely because the recipe is not a someone; anything aimed at a *chosen* body is
  still a different primitive and still M6's.
- **Re-applying a node's modifiers to Wights already standing.** Rule 3, with the bound measured.
- **A computed trigger threshold.** Rule 7, and the cost of not having one is written down.
- **Veilrot, and `TriggerField.Veilrot` gaining a writer.** M6-04. Rule 8 makes its absence a failing
  row rather than a silent one.

## As built

**Seven deviations. Three change something, and two of those are this spec's own claims refuted by
probing them before building — the shape [Traps §1](../../Traps.md) gained a row for at M5-05b.**

1. **Rule 3's mechanism was wrong: `MinionSystem.Spawn` does not seed a Wight's `Stat`s.**
   `MinionAgent.Initialise` does, re-basing all three from `Spec` on the way out of the pool, and its
   own remarks call the ordering load-bearing — `Health.Reset` refills `Current` from `MaxHp.Value`,
   so a caller that re-based *after* `Initialise` would stand every buffed Wight up on the unbuffed
   maximum. So the recipe is read in `Initialise`, and **`MinionAgent.cs` is a file the *small edits*
   row does not name.** `MinionSystem` takes the recipe and hands it to the bodies; it holds no field
   of its own, because one nothing read would be a warning.
2. **`MinionSystem`'s recipe parameter is required, not optional, which rippled to four test files.**
   Ten of eleven construction sites are in `MinionSystemTests`, `RiseTests`, `StatBlockTests` and
   `StageFlowTests`. An optional parameter defaulting to a recipe built from the spec would have
   rippled to none — and would have let a later caller silently create a *second* recipe, which is a
   node moving numbers no body reads. That is Traps §1's own failure mode, so the noisy option won.
3. **Rule 8's Tests-table wording would have landed the starve check red on arrival.** The row is
   written *"against `UpdateBlackboard`'s writes"*, and `Bulwark.asset` authors `_field: 5` —
   `IncomingProjectiles`, which `ProjectileSystem.Tick` writes and `UpdateBlackboard` does not. Scoped
   as written the row fails on a correct, shipped, written field. It is implemented as the row's own
   *name* says — **has a writer anywhere in the build** — against a hand-kept `Written` table with
   each entry's writer beside it. `Veilrot` is still the one it would catch, and the handover's
   prediction held: **it is green on arrival and nothing had to be weakened.**
4. **Rule 10's ring is anchored to the world, not to the player's facing.** The rule cites
   `BossBehaviour`'s adds ring as precedent; that ring puts its first body at **+X**
   (`angle = placed * 2π / wanted`), not at the boss's facing, and `CombatBlackboard` carries no
   facing for a handler to read. The formula is copied exactly. The Tests-table row — same position
   and facing, same three points — is satisfied either way, and `Raise_IsOnADerivedRingAndDrawsNothing`
   asserts it.
5. **`RaiseMinions` is registered only in runs that can raise, and that is the spec's own implied
   guard talking.** The guard list requires `RaiseMinionsHandler` to refuse a null `MinionSystem`, so
   it cannot exist on an Oathbound — and the conditional registration is what makes rule 5's bespoke
   sweep *unnecessary* for the verb: a verb is a **type**, so `SkillTree`'s existing `CanApply` sweep
   refuses a tree carrying one before `RunStarted`, naming the node; a target is a **field** on a
   registered type, which that sweep cannot see. **An Oathbound run's registry therefore holds five
   primitives, not six.** `Registry_HoldsSixPrimitives` is a Gravecaller run carrying all six, plus
   the Oathbound refusal as its other half.
6. **`TriggerText.UnitOf` gained a case the Files table does not name**, beside the two `KeyFor`
   pairs it does: `UnitOf`'s own throw says a new member needs *"a line here and two in KeyFor"*, and
   `TriggerTextTests.Trigger_EveryPairHasAKey` walks the cross product. `MinionCount` answers `Count`,
   which is what `TriggerUnit`'s remarks predicted a tenth field would do.
7. **Three ripple sites the spec did not list**, all count assertions that hard-code nine fields:
   `TriggerTextTests.PairCount` (18 → 20) and `ContentValidationTests.EveryTriggerKey_ResolvesInEnglish`
   (18 → 20), plus the prose that carries the number in each.

**Two spec claims probed and confirmed rather than assumed.** `EffectRegistry.CanApply` is
`_bindings.ContainsKey(effect.GetType())` and nothing more, so rule 5's refusal genuinely had to be
written by hand. `SkillSpecTests.Trigger_EveryFieldReads` does walk `Enum.GetValues` — **and is
stricter than rule 6 says**: it reflects a `CombatBlackboard` **public field of exactly the member's
name** and sets it, so `MinionCount` had to be a public `int` field spelled that way, which it is.

**One bug of mine, caught by the suite rather than by review:** `SkillBranchSpec.Tier` numbers tiers
from 1 and refuses 0, and the first sweep walked from 0 — 213 red rows, every one of them a run with
a tree. Fixed and re-run green.

**Verified:** **2 392 EditMode / 0 / 0** (+31 on M5-05b's 2 361), twice on the final code, and
**PlayMode 19 / 0 / 0** on two of three runs — run 2 was [known issue 1](../PROGRESS.md#current-state),
which had been quiet for three tasks. It fired with M5-05a's instrument answering ***wrong wedge***
again: the body **0.0001 m behind the apex**, which no wedge of any width contains. That is the
zero-margin-apex hypothesis confirmed a second time, on an untouched fixture. Console swept: 14
errors, 25 warnings, every one a fixture provoking its own failure path (`BrokenId`,
`RefusingLoader`, *"told to fail this write"*) and none naming a file this task added. Zero new
analyzer warnings; `dotnet format whitespace --verify-no-changes` green over all eighteen touched
files. `TimeManager.asset` re-serialised itself again and was reverted ([Traps §5](../../Traps.md)).
