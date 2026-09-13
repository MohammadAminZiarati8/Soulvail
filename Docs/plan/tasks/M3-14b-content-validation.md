# M3-14b — Content validation: every id disciplined, every key resolvable, every tree well-formed and unstarvable

**Size:** S (two test files, no production code — rule 11) · **Depends on:** M3-14a (the table a key resolves against), M3-12c (the first twenty-six assets), M3-02b (the definitions) · **Branch:** `m3-14b-content-validation`
**Design refs:** CH §5, §5.1; GD §13.1; AR §11.5; ADR-0006, ADR-0010, ADR-0012 · **Ledger rows:** **9** (rule 4 is what makes M3-14a's closure a fact rather than a claim), 1 (rule 9 refuses the tree shape that would strand a pick — the arithmetic's structural half)

## Goal

Every shipped asset checked by a test rather than by a playtest: ids namespaced and named after their files, every `LocKey` present and resolvable, every tree the shape CH §5 describes, every class carrying one, and no tree that can leave a player with a pick and nothing to spend it on.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/ContentValidationTests.cs` | Tests.Game | ids, file names, keys, and the four kinds with no sweep of their own (rules 1–4, 10) |
| `Tests/Game/Authoring/SkillTreeValidationTests.cs` | Tests.Game | CH §5's shape, keystones, upgrade parents, coverage, and reachability (rules 5–9) |

Only these files change. Anything else is a deviation: say so in *As built*. **A failure this task finds is fixed in the asset, in this branch, and named in *As built*** — a validation task whose first run is red and whose fix is a second PR has validated nothing.

## What is already checked, and what this task is actually for

**Three of the four existing kinds already sweep themselves.** `EnemyDefinitionTests.AllEnemyDefinitions_ValidUniqueIds` walks `AssetDatabase.FindAssets("t:EnemyDefinition")`, asserts every asset converts and that no id repeats; `CharacterDefinitionTests` and `ModeDefinitionTests` do the same for theirs. `ContentCatalog` refuses a duplicate **within a kind** at construction (*"Duplicate {kind} id '{id}'. Content ids must be unique."*) and `TreeRules` cross-checks the run's own tree at `Start`. **So "unique IDs" is largely done, and saying so is this task's first job.** What nothing checks:

| # | Gap | Who could have |
|---|---|---|
| a | The four **new** kinds have no sweep: `SkillDefinition`, `SkillTreeDefinition`, the `EffectDefinition` subclasses, `LocalizationTable` | M3-02b shipped them with nothing in `Data/` (its rule 7) |
| b | An id whose **namespace** does not match its kind — `enemy.consecrate` would pass every check in the project | nobody; ADR-0010 states the convention and no test reads it |
| c | A data asset whose **file name** does not match the last segment of its id | nobody; CLAUDE.md states it and no test reads it |
| d | A `LocKey` that is **present but unresolvable** — the check M3-14a's table makes possible for the first time | nobody could, before M3-14a |
| e | A tree that is **well-formed but starvable** — M3-08a rule 2 named this task for it explicitly | nobody; `TreeRules` checks structure, not reachability |
| f | A **character with no tree** — legal in the catalog (M3-02a rule 11), and a run-breaking omission in a shipped build | nobody; M3-02a rule 11 says *"M3-14 pins that every shipped character has one"* |

## Behaviour

1. **It sweeps every asset in the project, not only the ones `BootScope` holds.** `AssetDatabase.FindAssets($"t:{nameof(X)}")`, `EnemyDefinitionTests`' shape. An asset sitting in `Data/` outside a boot list is content the next task to add a line to `BootScope` will ship, and finding it broken then is finding it late. Every kind gets the same three assertions the enemy sweep makes — it loads, it converts, its id is unique — and **the four kinds M3-02b added get theirs here**, which is gap (a).
2. **An id's namespace matches its kind** (gap b): `character.*`, `enemy.*`, `mode.*`, `arena.*`, `skill.*`, `tree.*`. ADR-0010 makes an id stable and human-readable identity; nothing makes it *honest*, and `enemy.consecrate` would pass the catalog, the sweeps and the game. **Cross-kind uniqueness falls out of this for free** and is asserted separately anyway: one `HashSet` over every id in the project, so the day two kinds share a prefix the collision is a failing row rather than a lookup that silently finds the wrong thing.
3. **A data asset's file name is the last segment of its id, PascalCased** (gap c) — CLAUDE.md's asset-naming rule, `Oathbound.asset` ↔ `character.oathbound`, unchecked until now. It matters because the id is what every spec, save file and test refers to and the file name is what a human navigates by; when they disagree, every future search for a node finds the wrong asset. The comparison is case-insensitive on the segment and exact on the shape, so `TemperedVow.asset` ↔ `skill.oathbound.temperedVow` passes and `Vow.asset` does not.
4. **Every `LocKey` is present, distinct, and resolvable — and the third is the row ledger row 9 has been waiting for** (gap d). *Present*: non-default on every spec that carries one, which for M3-12c's twelve nodes is twenty-four. *Distinct*: no two nodes share a name key, because two cards reading the same word is indistinguishable from a bug. *Resolvable*: **every key found in any asset has a row in `English.asset`**, asserted through `TableLocalizer.Has` (M3-14a's door). That last one is what makes M3-14a's closure a fact rather than a claim, and it is what fails the day someone authors a thirteenth node and forgets its two rows. **`TriggerText.KeyFor`'s eighteen are swept too**, since a trigger line with no row reads as a key on a screen a player uses constantly.
5. **CH §5's shape, over every tree asset**: exactly three branches (*"identical skeleton for every class, so the UI is built once"*), one to eight tiers each, every tier at least one id, every id appearing once in the whole tree, no `default`. This is M3-02a rule 8's construction-time contract asserted over assets rather than over hand-built specs — the difference being that a spec in a test was written by someone who knew the rules.
6. **Keystones and Upgrades, the two rules a tree can break quietly.** A `Keystone` is the sole node of its branch's last tier (CH §5), and an `Upgrade`'s parent is in the **same branch at a lower tier** (M3-03 rule 1, M3-12c rule 2) — an Upgrade whose parent sat in another branch would let one branch wait on a pick the player may never make. **A partial tree with no keystone is legal and the test must not refuse it**: that is M3-12c's v1 (M3-02a rule 8's *"a partial tree is legal"*), and a validation test that rejected the milestone's own content would be the worst possible version of this task.
7. **Every tree node resolves to a skill; a skill in no tree is not an error, and the test says why.** The first is required — a tree holding an id the catalog cannot answer is a level-up screen with a blank card, and M3-02b rule 6 is explicit that *"neither list resolves the other at conversion"* so nothing else catches it. The second is deliberately permitted: M6's Sanctum and M6-05's Pacts may grant a skill that is in no tree, and a test that forbade it today would have to be argued down by the task that needs it. Asserted as a **count reported in the failure message** rather than a pass/fail, so an unreachable skill is visible without being illegal.
8. **Every shipped character has a tree** (gap f, M3-02a rule 11's named obligation). `TryGetTreeFor(characterId)` answers true for every `CharacterDefinition` in the project. This is what **retires M3-03 rule 10's null-tree branch from a real build** — the branch stays and stays tested, because the Editor's direct-Play path can still compose a catalog without one (M3-12c rule 7), but no *shipped* run can reach it.
9. **No tree can starve, which is the check M3-08a rule 2 named this task for** (gap e). Its words: *"a branch whose remaining nodes are Upgrades of an untaken parent can be blocked while the tree is half empty, and all three blocked at once is content this build cannot yet refuse (M3-14 is where a tree is checked for it)."* A blocked-but-unfull tree turns a pick into Overflow, which is CH §5.2's consolation being paid out for an authoring mistake. **The check walks pick sequences and asserts the available set is non-empty until `IsFull`.** For a twelve-node tree the walk is exhaustive; for twenty-seven it cannot be (2²⁷ states), so it is **1 000 sequences from a fixed seed, plus every single-branch-first ordering** — a player who pours everything into one branch is the pathological case and is cheap to enumerate. The test states in its own name and message what it proves and what it does not: **a pass is strong evidence, not a proof**, and M7-04's eighty-one nodes are when that distinction starts to matter.
10. **A message names the asset path, always.** Every assertion failure starts with the path, `EnemyDefinitionTests`' convention (*"{path} is not valid content"*), because the whole value of a sweep is that it tells you *which* of twenty-six assets is wrong. A red row reading only *"expected true"* over a project-wide walk is worse than no test.
11. **It is a test, and there is no `ContentValidator` class.** Production code here would be a **second implementation** of rules `ContentCatalog`, `LocalizationTable.ToDictionary` and `TreeRules`' constructor already enforce, and the drift between the two would be the bug — one refusing what the other allows, with no way to tell which is right. The division is clean: **constructors validate specs at run time; this validates assets at author time**, and the second is the only one that can see an asset nobody has loaded.
12. **It runs in EditMode and depends on `AssetDatabase`, which is why it is `Tests.Game`.** `Soulvail.Tests.Core` cannot reference `UnityEditor` and should not — a pure-core test suite that needed the editor would be ADR-0001 leaking backwards. The assets are Game-side authoring objects and their `ToSpec` is the boundary being tested.

## Tests

| Test | Given / When / Then |
|---|---|
| `AllSkills_LoadConvertAndAreUnique` | every `SkillDefinition` in the project / `ToSpec` / loads, no throw, no repeated id, path in every message (rules 1, 10) |
| `AllTrees_LoadConvertAndAreUnique` | every `SkillTreeDefinition` / `ToSpec` / the same (rule 1) |
| `AllEffects_LoadAndConvert` | every `EffectDefinition` subclass / `ToEffect` / the same, and every subclass in the assembly has at least one asset or is named as unused (rule 1) |
| `AllTables_Convert` | every `LocalizationTable` / `ToDictionary` / no duplicate, no empty key (rule 1) |
| `EveryId_IsNamespacedForItsKind` | every asset of all seven kinds / — / `character.*`, `enemy.*`, `mode.*`, `arena.*`, `skill.*`, `tree.*` respectively (rule 2) |
| `EveryId_IsUniqueAcrossEveryKind` | every id in the project / one set / no collision — the check `ContentCatalog` cannot make (rule 2) |
| `EveryFileName_MatchesTheLastSegmentOfItsId` | every asset / path vs id / `Oathbound.asset` ↔ `character.oathbound`, `TemperedVow.asset` ↔ `skill.oathbound.temperedVow` (rule 3) |
| `EveryLocKey_IsPresent` | every spec carrying one / — / non-default; twenty-four for M3-12c's twelve nodes, three for its branches (rule 4) |
| `EveryNodeName_IsDistinct` | every skill in the project / — / no two share a `NameKey` (rule 4) |
| `EveryLocKey_ResolvesInEnglish` | every key in every asset / `TableLocalizer.Has` / true for all of them — **the row that makes ledger row 9's closure checkable** (rule 4) |
| `EveryTriggerKey_ResolvesInEnglish` | `TriggerText.KeyFor` over both enums / `Has` / true for all eighteen (rule 4) |
| `EveryTree_HasThreeBranches` | every tree / `ToSpec` / exactly three, each with 1–8 tiers, each tier non-empty (rule 5) |
| `EveryTree_UsesEachIdOnce` | every tree / — / no id twice, none `default` (rule 5) |
| `EveryKeystone_IsALastTierSingleton` | every tree with a keystone / — / it is alone in its branch's last tier (rule 6) |
| `ATreeWithNoKeystone_Passes` | `OathboundTree.asset` as M3-12c ships it / — / green, and the test's name says a partial tree is legal (rule 6) |
| `EveryUpgrade_HasAParentInItsBranchBelow` | every `Upgrade` node / — / the parent is in the same branch at a lower tier (rule 6) |
| `EveryTreeNode_ResolvesToASkill` | every id in every tree / the catalog built from every shipped asset / found (rule 7) |
| `SkillsOutsideEveryTree_AreReportedNotFailed` | the shipped set / — / green, and the message names any skill no tree holds; today that count is 0 (rule 7) |
| `EveryShippedCharacter_HasATree` | every `CharacterDefinition` / `TryGetTreeFor` / true — **what retires M3-03 rule 10 from a real build** (rule 8) |
| `NullTreeBranch_IsStillReachableInATest` | a catalog built without a tree / `TryGetTreeFor` / false, no throw — the branch stays, and this row is why (rule 8) |
| `NoTree_CanStarve_Exhaustively` | the twelve-node tree / every pick order / the available set is non-empty at every step until `IsFull` (rule 9) |
| `NoTree_CanStarve_Sampled` | every tree with more than twelve nodes / 1 000 seeded sequences / the same, and the test's name and message say a pass is evidence rather than proof (rule 9) |
| `NoTree_CanStarve_SingleBranchFirst` | every tree / each branch exhausted before the others are touched / the same — the pathological order, enumerated (rule 9) |
| `NoValidator_ShipsInGame` | reflection over `Soulvail.Game` / — / no type named `ContentValidator` or equivalent; the rules live in the constructors and here (rule 11) |
| `EveryFailureMessage_NamesAPath` | a deliberately broken temporary asset per sweep / the run / each message begins with the asset path (rule 10) |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

_No Game-side change is visible; this task adds no runtime behaviour. Three Editor checks prove the tests can fail._

1. **[Editor]** Rename `TemperedVow.asset` to `Vow.asset` and run the suite: `EveryFileName_MatchesTheLastSegmentOfItsId` fails naming the path. **Rename it back** (rule 3).
2. **[Editor]** Delete one row from `English.asset` and run: `EveryLocKey_ResolvesInEnglish` fails naming the key and the asset that carries it. **Put it back** (rule 4).
3. **[Editor]** Author a throwaway tree whose second tier holds only an `Upgrade` of an untaken tier-2 node and run: the starvation rows fail. **Delete it** (rule 9). If this does *not* fail, the walk is not walking and the whole of rule 9 is decoration — which is the one way this task can pass while doing nothing.
4. **[Editor]** Confirm the full EditMode suite is still green and that the new sweeps add no Console errors — a validation test that logs on a passing run makes every future red run harder to read.

## Out of scope

- **Pinning the twelve nodes' numbers** — M3-12c rule 10's `OathboundTreeTests` does that, and the split is exact: that task asserts *these* assets say what its table says, this one asserts the *rules* every asset obeys. A retune breaks M3-12c's rows and must not break these.
- **A tree editor for `SkillTreeDefinition`** — parking lot, promoted when M7-04 authors eighty-one nodes by hand. This task makes a hand-authored mistake fail loudly, which is the cheaper half of the same problem.
- **Validating arenas, projectiles, or the mode schedule beyond id and key discipline.** M2's own suites cover their content; rules 2, 3 and 4 apply to every kind, and nothing kind-specific is added for kinds M3 did not touch.
- **Checking that a node is *interesting*.** GD §13.1's *"every node must change how you play, not just a number"* is a design judgement — M3-12c's own honest count is six stat lines of twelve, and M3-15 is where a playtest rules on it. No test can.
- **Balance, TTK, or the offer's distribution** — M3-12c rule 8's table and M3-04's weighting tests. This task asks whether content is *well-formed*, never whether it is *good*.
- **Running in CI, or as a pre-commit hook.** There is no CI (M0-01) and the hooks are shell (`.githooks/`); the suite runs through `TestRunnerApi` like everything else.
- **A `SkillDefinition` that ships with no effect.** M3-02b's conversion refuses it already; a second refusal here would be rule 11's drift.

## As built

_Filled at merge._
