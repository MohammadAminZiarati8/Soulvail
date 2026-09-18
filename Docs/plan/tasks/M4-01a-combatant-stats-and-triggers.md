# M4-01a — A stat an effect can aim at, and a trigger a boss can read about itself

**Size:** M · **Depends on:** M3-05, M3-06, M3-12a · **Branch:** `m4-01a-combatant-stats-and-triggers`
**Design refs:** GD §9.1; CH §4.2; AR §6, §10.1, §18.2, §18.3; ADR-0006, ADR-0008 · **Ledger rows:** none directly — this is what [M4-01b](M4-01b-boss-agent-framework.md) and M7's Archon need to exist at all

## Goal

An effect can raise or lower a stat on **something that is not the player**, and a skill's trigger can ask a
question about **the thing holding the skill** — so a boss can be buffed, debuffed, and can cast on its own
condition, using the machinery the player already uses rather than a second copy of it.

## Why this is a task and not part of M4-01b

**The owner's requirement is that a boss can have buffs, debuffs and skills — everything the player can get.**
Investigated at M4-00a against the shipped code rather than assumed, and the finding is that **the machinery is
already almost entirely target-agnostic**:

| Piece | Already generic? |
|---|---|
| `Health` | **Yes** — `EnemyAgent.cs:55` and `PlayerCombat.cs:287` construct the same class |
| `Stat` + `Modifier` | **Yes** — and `EnemyAgent` already exposes **two** (`MoveSpeed`, `ContactDamage`) |
| `GrantedShieldPool` | **Yes** — keyed by `object` source, never by the player |
| `EffectRegistry.Apply(IEffect, object source)` | **Yes** — generic dispatch |
| `TimedEffects` | **Yes** — works off the registry |
| `SkillRunner(EffectRegistry, CombatBlackboard, IDomainEvents)` | **Yes** — *no player in the signature* |
| `EnemyAgent.IsVulnerable` | **Yes** — the invulnerable beat GD §9.1 rule 3 needs already exists |

**Exactly two things are player-bound, and they are both narrow:**

1. **`ModifyStat` names a `PlayerStat`** (`Core/Effects/PlayerStat.cs:48`) and `ModifyStatHandler` holds *the*
   `PlayerStats` (`:122`). There is no way to say **whose** stat, so an effect can address the player and
   nothing else, for ever.
2. **`EnemyBlackboard` is a perception blackboard, not a trigger one.** It carries `SelfPosition`,
   `DistanceToPlayer`, `HasLineOfSight`, `AlliesNearby` — and **no health fraction at all**, while
   `TriggerSpec` reads `CombatBlackboard`, which is the *player's* (`PlayerCombat.cs:316` builds the only one,
   `RunSession.cs:484` hands it to the only `SkillRunner`). A boss skill that fires *"when my own health drops
   below 66 %"* has nowhere to read from.

**Breaking those two is this task. Everything else about a boss is M4-01b's.** They are separated because a
stat-addressing change touches `Effects` and is reviewed against ADR-0006 and ADR-0008, while a phase machine
touches `Ai` and is reviewed against GD §9.1 — two arguments, and M3-12's three-way split is the precedent.

**It ships used by nothing**, which is M3-05's and M3-12a's bargain again: no boss exists until M4-01b, so this
task moves no number in any run the build plays. That is deliberate and is what keeps its review honest.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/IStatBlock.cs` | Core | The one thing a `ModifyStat` needs of its target: resolve an address to a live `Stat` |
| `Core/Effects/CombatantStats.cs` | Core | `IStatBlock` over an `EnemyAgent` — the second implementation, and the one that proves the first was an interface rather than a rename |
| `Tests/Core/Effects/StatBlockTests.cs` | Tests.Core | Addressing, refusal, and the two implementations answering the same question |
| `Tests/Core/Ai/EnemyTriggerTests.cs` | Tests.Core | The blackboard's new fields, and a trigger evaluated against a non-player |
| *small edits* | Core | `PlayerStat` gains nothing; **`PlayerStats` gains `: IStatBlock`**; `ModifyStat` gains a target; `ModifyStatHandler` gains a resolver; `EnemyBlackboard` gains the trigger fields; `EnemyAgent` publishes them on tick |
| *ripple* | Tests.Core, Tests.Game | Every `new ModifyStat(...)` call site — the three effect fixtures, `ModifyStatDefinition`, and M3-12c's authored assets if the field is not defaulted (rule 4 says it is) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Effects/IStatBlock.cs
/// What a ModifyStat needs of whatever it is aimed at. Deliberately one member:
/// this is an address book, not a combatant.
public interface IStatBlock
{
    /// <summary>The live Stat behind an address, or throws if this block has no such stat.</summary>
    Stat Resolve(PlayerStat stat);

    /// <summary>Whether Resolve would answer — so a handler can refuse without catching.</summary>
    bool Has(PlayerStat stat);
}

// Core/Effects/CombatantStats.cs
public sealed class CombatantStats : IStatBlock   // over an EnemyAgent
{
    public CombatantStats(EnemyAgent agent);
    public Stat Resolve(PlayerStat stat);
    public bool Has(PlayerStat stat);
}

// Core/Effects/ModifyStat.cs — widened
public enum StatTarget { Player, Self }            // rule 3

public sealed class ModifyStat : IEffect
{
    public ModifyStat(PlayerStat stat, ModifierKind kind, float value,
                      StatTarget target = StatTarget.Player);   // rule 4
    public StatTarget Target { get; }
}

// Core/Effects/ModifyStatHandler.cs — widened
public sealed class ModifyStatHandler : IEffectHandler<ModifyStat>
{
    public ModifyStatHandler(IStatBlock player);
    /// <summary>The caster's own block, set for the duration of one Apply/Remove (rule 5).</summary>
    internal IDisposable Aiming(IStatBlock self);
}

// Core/Ai/EnemyBlackboard.cs — gains the trigger half
public float HpFraction;       // rule 6
public float ShieldFraction;   // rule 6 — always 0 until something grants an enemy shield
```

## Behaviour

1. **`IStatBlock` addresses by `PlayerStat` and the enum is *not* renamed or duplicated.** The alternative —
   a parallel `EnemyStat` — was weighed and refused: the two sets overlap on `MaxHp` and `MoveSpeed`, a second
   enum means `ModifyStatDefinition` needs a second dropdown and a designer needs to know which, and *"enum
   ordinals as content identity"* is already banned. **A block that does not have an address says so** rather
   than inventing a `Stat`, which is rule 2.
2. **`CombatantStats` answers `MaxHp`, `MoveSpeed` and `ContactDamage` and refuses everything else.** Those are
   the three `Stat`s an `EnemyAgent` actually has (`EnemyAgent.cs:82, 95, 106`); `WeaponRange` on a Husk is a
   question with no answer, and the refusal is a throw naming the address and the agent's spec id — **not a
   silently-created `Stat`**, which would be a modifier landing on a number nothing reads (Traps §1's family).
3. **`StatTarget` has two members and `Self` means *the thing that cast it*, not *the enemy*.** A boss buffing
   itself and a player node buffing the player are the same operation aimed differently. There is deliberately
   no `Target.Enemy` or `Target.Nearest`: an effect that debuffs *someone else* is a different primitive with a
   selection rule, and it is [M6](../ROADMAP.md#m6--systems-complete)'s, not this task's.
4. **The parameter is defaulted to `StatTarget.Player`, and that is what keeps the ripple to nothing.** Every
   existing `ModifyStat` call site and **all nine shipped `ModifyStatDefinition` assets** keep meaning exactly
   what they meant, with no asset touched and no migration — the same defaulted-parameter ruling M3-13b used
   for `EnemySpawned.IsElite`, which held the PR to three assemblies. **`ModifyStatDefinition` gains the
   dropdown but authors `Player`**, so a designer sees the new choice and no shipped content uses it.
5. **The handler is aimed for the duration of one call, not constructed per combatant.** `EffectRegistry`
   registers one handler per effect type for the whole run (`Register<TEffect>`), so a per-combatant handler
   would mean a registry per combatant. `Aiming(self)` sets the caster's block, returns a scope that clears it,
   and **`Apply` with `Target.Self` outside a scope throws** rather than falling back to the player — a boss
   buff that silently landed on the player is the worst outcome available here, and it is the one a default
   would produce.
6. **`EnemyBlackboard` gains `HpFraction` and `ShieldFraction`, written where the others are written.** It is
   a *perception* blackboard today and this makes it a trigger one too; both fields are published on the same
   tick as `DistanceToPlayer`, from `Health`, so a trigger reads this frame's health rather than last frame's.
   `ShieldFraction` is **always 0** until something grants an enemy shield, and it ships anyway so
   `TriggerField.ShieldFraction` is not a clause that silently never fires — which is exactly the
   `TriggerField.Veilrot` trap M3-07a raised and nothing has fixed.
7. **`TriggerSpec` is not widened and `CombatBlackboard` is not merged with `EnemyBlackboard`.** The two carry
   different questions and merging them would put `HasFocus` and `Veilrot` on a Husk. What M4-01b needs is a
   `SkillRunner` reading an *enemy's* blackboard, and **the runner already takes a `CombatBlackboard` by
   parameter** — so the shape that unblocks it is a narrow read, ruled in M4-01b rather than guessed here.
   **This task's job is that the fields exist to be read.**
8. **Nothing in this task is reachable in play.** No boss exists, no enemy holds a skill, no asset authors
   `Self`. `PlayerStats` gaining `: IStatBlock` must change no behaviour at all — its `Resolve` is the method
   the interface names, so the edit is the declaration and nothing else.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Player_ImplementsTheBlock` | the shipped `PlayerStats` / resolved through `IStatBlock` / answers the identical `Stat` instance as `Resolve`, for all eleven addresses |
| `Combatant_AnswersItsThreeStats` | a Husk agent / `Resolve(MaxHp/MoveSpeed/ContactDamage)` / returns the agent's own live `Stat`, not a copy |
| `Combatant_RefusesAnAddressItDoesNotHave` | a Husk agent / `Resolve(WeaponRange)` / throws, and the message names both the address and `enemy.husk` |
| `Combatant_HasAgreesWithResolve` | every `PlayerStat` member / `Has` vs `Resolve` / true exactly when `Resolve` does not throw — no address answers one way and not the other |
| `Modify_DefaultsToThePlayer` | `new ModifyStat(MaxHp, Flat, 15)` / applied with no scope / lands on the player, and `Target` reads `Player` |
| `Modify_ShippedAssetsAllTargetThePlayer` | all nine `ModifyStatDefinition` assets / converted / every one is `Target.Player` — rule 4's ripple, asserted rather than assumed |
| `Modify_SelfLandsOnTheCaster` | a Husk, aimed / apply `MoveSpeed −50 % Self` / the **agent's** `MoveSpeed` moves and the player's does not |
| `Modify_SelfOutsideAScopeThrows` | no scope / apply with `Target.Self` / throws, and does **not** land on the player |
| `Modify_SelfIsRemovedFromTheCaster` | a Husk, aimed, applied / `Remove` in the same scope / the agent's stat returns to base and the source is gone |
| `Aiming_ClearsOnDispose` | a scope opened and disposed / a second `Self` apply / throws, so a leaked scope cannot silently aim at a stale agent |
| `Aiming_AllocatesNothing` | 10 000 aim/apply/remove cycles / `AllocationAssert.None` / zero — it is a per-cast path and M3-06's runner is on the frame path |
| `Blackboard_PublishesHealthFraction` | an agent at 18 of 36 HP / ticked / `HpFraction` reads 0.5 **this** tick, not next |
| `Blackboard_ShieldFractionIsZeroAndSaysSo` | any shipped agent / ticked / `ShieldFraction` is exactly 0 — rule 6's anti-`Veilrot` row |
| `Trigger_EvaluatesAgainstAnEnemysHealth` | `HpFraction Below 0.66` / a Husk driven from full to 0.5 / the clause is false then true, proving the fields are readable by the shipped `TriggerSpec` |
| `Player_BehaviourIsUnchanged` | the eleven player addresses, before and after / every modifier path / identical — rule 8, the row that makes this a widening rather than a rewrite |

**Guard rows are implied, not listed:** a null block to the handler, a null agent to `CombatantStats`, a
non-finite fraction into the blackboard, and `Enum.IsDefined` on `StatTarget`.

## Manual verification (Editor / device)

1. **[Editor]** Open any of the nine `ModifyStat` assets. The Inspector shows a new **Target** dropdown reading
   **Player**. Change nothing. *Expected: no asset diff, because the field is defaulted and Unity writes no line
   for a default.* If a diff appears, rule 4's ripple claim is wrong and *As built* says so.
2. **[Editor]** Play a run, take any node. *Expected: identical to `m3` — the same number moves by the same
   amount.* This task is invisible by construction (rule 8).

## Out of scope

- **Any boss, any phase, any add-spawn** — M4-01b. This task ships the seam and nothing that walks through it.
- **A second `SkillRunner`, or an enemy that owns a skill.** M4-01b rules how a runner reads an enemy's
  blackboard; rule 7 says only that the fields exist.
- **`Target.Enemy` / debuffing someone else.** A different primitive with a selection rule — M6.
- **Widening `TriggerSpec`, or merging the two blackboards** (rule 7).
- **Granted shield on an enemy.** `GrantedShieldPool` is already generic and nothing grants one; `ShieldFraction`
  ships at 0 on purpose (rule 6).
- **`PlayerStat`'s name.** It reads oddly once a Husk uses it, and renaming it to `StatId` touches every
  authored asset's serialized enum — a rename is a move, and M3-14b's precedent says the owner rules it. Raised
  in *As built*, not done here.

## As built

_Filled at merge._
