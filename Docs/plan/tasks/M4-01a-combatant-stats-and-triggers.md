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
   selection rule, and it is [M6](../ROADMAP.md#m6--systems-complete-titles-only)'s, not this task's.
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

**Five counted files, size M's ceiling, no split.** The two new Core files, the two new test files, and
**`ModifyStat.cs` counted as substantial**: its handler gained a field, a new public method, a private
resolver and a nested `IDisposable`, which is past *"a new field, a registration, an event struct"*.
Everything else was additive and is not counted — `PlayerStat.cs` (one member, one method, one
declaration), `EnemyBlackboard.cs` (two fields, two `Reset` lines), `EnemySystem.cs` (two lines),
`EnemyAgent.cs` (two lines), `ModifyStatDefinition.cs` (one field, one argument), and three existing
test fixtures. **Four assemblies**, each file confirmed through `GetAssemblyNameFromScriptPath`:
`Soulvail.Core` ×7, `Soulvail.Game` ×1, `Soulvail.Tests.Core` ×5, `Soulvail.Tests.Game` ×1.

**The spec disagreed with itself once, and the precedence rule settled it against the Files table.**
The Files row says *"`PlayerStat` gains nothing"*; rule 2 and the row
`Combatant_AnswersItsThreeStats` both say `CombatantStats` answers **`ContactDamage`**, which was not
a member — rule 1's own sentence puts the overlap at **two** (`MaxHp`, `MoveSpeed`), so the third
address had to be created or two named rows had to be dropped. *"The Tests table wins, then
Behaviour, then Public API, then Files"*, so **`PlayerStat.ContactDamage` was appended** and the Files
row is the part that lost. What it costs, stated rather than buried:

- **`PlayerStats.Resolve` now refuses a legal member.** Its loud default was previously reachable only
  by a stale ordinal; `ContactDamage` is the first member that is *meant* to land there.
  `Stats_ResolveEveryMember` carries one named exception asserting the refusal, rather than being
  loosened — *"not in the switch"* and *"deliberately not the player's"* must not look the same from
  that row. `PlayerStatCoverageTests` moved 11 → 12 with the reason written in.
- **A designer can now author `ContactDamage` on a player node**, and it throws when the node is
  picked. That is the same bargain rule 2 makes in the other direction and it is documented on the
  member, but it is a new way to author a broken node and the owner should know it exists.
- **Appended, never inserted.** `ModifyStatDefinition` serialises `_stat` as a raw `int`
  (`_stat: 0` in every shipped asset), so the ordinal *is* content identity here in practice; the
  enum's remarks now say so, because `OnValidate` catches an ordinal that is no longer a member and
  cannot catch one that is now a *different* member.

**Rule 4's ripple is nothing, and it is nothing for a different reason than the spec gives — measured,
not asserted.** The spec says *"Unity writes no line for a default"*. **That is false**, and the
shipped assets already showed it: `UnbowedHp.asset` carries `_stat: 0`, and `PlayerStat.MaxHp` **is**
`0`. Probed properly — an in-memory clone of the shipped asset serialised through
`InternalEditorUtility.SaveToSerializedFileAndForget` to a path outside `Assets/`, so nothing under
`Data/` was touched — Unity writes **`_target: 0`** in full. What actually holds is the *claim*, for
the other reason: **Unity does not rewrite an asset on disk because the script that reads it grew a
field**, and a line absent from the YAML deserialises to the member's default. Measured after the
field landed, a full reimport, three EditMode runs and two PlayMode runs: **zero of the nine assets
carries a `_target` line and `git status` shows nothing under `Data/`.** The line appears the first
time each asset is saved for some other reason, and when it does it will say `_target: 0`, which is
what it already meant. **So: no diff, no migration, and the ruling stands — but the sentence
explaining it does not**, and any later task that reasons *"Unity omits defaults"* will be wrong.

**Rule 5 was proved red before it was proved green.** With `Block`'s `Self` case temporarily written
as the fallback a default would produce — `_self ?? _player` — the fixture reported **PASS=13 FAIL=3**:
`Modify_SelfOutsideAScopeThrows`, `Aiming_ClearsOnDispose` and `Modify_SelfIsRemovedFromTheCaster`.
The refusal was then restored and the same rows went green. **The red run also caught a bad
assertion of ours**, which is the argument for running it: `Modify_SelfIsRemovedFromTheCaster`
asserted `ModifierCount` was **zero** after removal, and a spawned agent already wears
`DepthScaling`'s modifiers — at depth 1 they multiply by exactly 1, so they are invisible in the
value and would have made that row assert the depth curve was off. It now counts what the agent wore
at spawn and compares against that.

**`Aiming` shipped `public`, where the Public API says `internal`.** `Soulvail.Tests.Core` has no
`InternalsVisibleTo` and AR §18.2 says it deliberately never will, so an `internal` spelling would put
nine of the fifteen rows out of reach of the only assembly that could prove them — the Tests table
wins over Public API. **The seal is one layer out, which is `PlayerStats`' own argument**:
`RunState.Effects` is `internal`, so nothing outside core can reach a registry, let alone a handler
inside one. Written onto the method.

**Three things shipped that the spec does not list, each one line and each with a reason.**
1. **`Aiming` refuses to nest** (`InvalidOperationException`), rather than shadowing or silently
   restoring — either would be a buff landing on the wrong body with nothing said. It is also what
   makes reusing one scope instance safe, which is what holds `Aiming_AllocatesNothing` at zero.
2. **`EnemyAgent.Initialise` seeds the two new fields after `Health.Reset`.** `Blackboard.Reset`
   zeroes them, and a zero *health* fraction does not read as "not filled in yet" — it reads as
   **dead** to every trigger that asks. An agent spawned inside `EnemySystem.Tick` is not perceived
   until the next frame's `Ingest`, so without this a boss would spend its first tick claiming to be
   at zero. The perception fields are left zeroed because "distance zero" is merely wrong; this one is
   wrong in a direction something acts on.
3. **`ModifyStat`'s constructor validates `StatTarget` with `Enum.IsDefined`.** Unlike the address it
   has no second door — `ModifyStatDefinition` deliberately does not check it — so a stale ordinal
   would otherwise fall out of the handler's switch mid-run rather than at the asset.

**A finding the suite produced, now pinned: a corpse's blackboard is frozen, not zeroed.**
`EnemySystem.Perceive` skips agents that are not alive — which it has done since M1-06 for every field
it writes — so a dead enemy's `HpFraction` is the last one it was perceived with. A first draft of the
non-finite guard row assumed zero and cost **one red run** (2 003 / 1). It is now two rows:
`Blackboard_HealthFractionIsNeverNonFinite` (the guarantee lives in `Health.Fraction`, which answers
zero rather than dividing) and `Blackboard_ACorpseKeepsItsLastReading`. **M4-01b's phase machine must
ask `IsAlive` rather than trust the fraction**, and that is now a row rather than a memory.

**`Modify_ShippedAssetsAllTargetThePlayer` landed in `Tests/Game/Authoring/OathboundTreeTests.cs`,**
not in either new file: it has to open nine real assets, which means `AssetDatabase`, which
`Soulvail.Tests.Core` cannot reach (M0-10). The spec's *ripple* row anticipates `Tests.Game`; the
Files table's two test files are both `Tests.Core`, so this is named as the placement it is. It counts
the nine and asserts each converts to `Target.Player`.

**Out of scope, raised for the owner as instructed: `PlayerStat` now reads as a lie.** It is the
address space *every* combatant is addressed in and it holds one member no player has. `StatId` is the
honest name. **It was not renamed and must not be renamed casually**: `ModifyStatDefinition`
serialises the enum by ordinal into every authored asset, and nine of those exist — a rename is a
*move* (M3-14b's precedent), and it should be one task with the assets in the same PR. The enum's
remarks now say this in place of the paragraph that used to promise an `EnemyStat` mirror for M7-02,
which rule 1 refused.

**Verified:** **2 005 / 0 / 0 EditMode, three runs**, against M3-15's **1 981** — **+24 rows**, the
arithmetic landing to the row (**16** in `StatBlockTests`, **7** in `EnemyTriggerTests`, **1** in
`OathboundTreeTests`; the two `Has` assertions added to `Stats_ResolveEveryMember` are arms inside an
existing row and are correctly not counted, and no existing row was removed).
**PlayMode 16 / 0 / 0, twice**, `Ticker_RunsTheStepsInOrder` green both times — ledger row 4's
experiment remains unrun and this task did not run it. **Nothing under `Data/`, no prefab and no scene
moved.** Console swept after the last run: 11 errors and 25 warnings, **every one a fixture
deliberately exercising a validation path** (`WarnsOn…`, `Broken…`, `FailNextWrite`, `SilentActive`)
— **no compiler or analyzer warning at all**. `dotnet format whitespace --folder
--verify-no-changes` over all thirteen touched files: **exit 0**. `ProjectSettings/TimeManager.asset`
dirtied and reverted for the **nineteenth** time, same rational form (2 822 399 / 141 120 000 = 0.02,
`m_TimeScale` 1).

**One pre-existing Console warning found and deliberately not fixed**, because it is nobody's this
task: on every domain reload `ArenaView.OnValidate` reports *"Arena_Pillars: it authors 0 spawn
point(s) and needs at least 3"* for both arena prefabs. **The prefabs author 8 and 7** — read off
`SerializedObject`. `ArenaView.SpawnPoints` filters `_spawnPoints[i] == null`, and during a reload
those `Transform` references are fake-null, so the getter returns an empty list and `OnValidate`
reports a fault that does not exist. It is Traps §1's family in `OnValidate` clothing and belongs in
that document; it is raised here rather than written there because this task touched neither file.

**Manual verification:** step 2 is the owner's. **Step 1 is already answered by measurement** and
does not need the Inspector: no asset diff, confirmed three ways — `git status`, a byte scan of all
nine files for a `_target` line, and the serialisation probe above that says what *would* be written.
The Inspector will show the new **Target** dropdown reading **Player**; selecting an asset does not
dirty it.

**Ledger:** **no row closes and no row is added.** Row 3 (device debt) is untouched — nothing here is
reachable in play. Row 4's `FrameOrderTests` experiment is still unrun, and two green PlayMode runs are
not evidence either way at a 10 % rate. Row 6(i)'s content-discipline concern gains a neighbour worth
noting when someone next looks at it: **`StatTarget` is a code enum authored per asset, and the ordinal
is the identity** — the same shape as the `_stat: 0` finding above.
