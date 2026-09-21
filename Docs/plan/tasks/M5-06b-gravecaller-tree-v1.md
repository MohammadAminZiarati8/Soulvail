# M5-06b — The Gravecaller tree v1, and what a level is worth

**Size:** M (three code files and twenty-seven assets — the [sizing rule](../ROADMAP.md#how-to-read-this) counts code, and this is still a review) · **Depends on:** M5-02, M5-06a · **Branch:** `m5-06b-gravecaller-tree-v1`
**Design refs:** CH §3.2, §4, §4.1, §4.2, §5, §5.2; GD §6.2, §12.4; AR §10.1, §11.5; ADR-0006, ADR-0008 · **Ledger rows:** [5(i)](../ROADMAP.md#carry-forward-into-m5)

## Goal

The second class has twelve nodes a player can be offered — three branches of two tiers of two, the
class's whole identity in the Legion branch — and the two numbers that decide what more than half of
a deep run's power is worth stop being `const`s in core.

## The tree

**Three branches, two tiers of two, twelve nodes, no Keystone** — [M3-12c](M3-12c-oathbound-tree-v1.md)'s
shape exactly, and M3-02a rule 8's *"a partial tree is legal"* for the same reason. Tier 2 needs one
node taken in its branch, so a branch opens both of its tier-2 nodes after a single pick.

### Legion — the dead

| Tier | Id | Name | Kind | Effect | What it changes |
|---|---|---|---|---|---|
| 1 | `skill.gravecaller.exhume` | Exhume | **Active** | `RaiseMinions(3, 2.0)` · 20 s · `MinionCount Below 2` | skill |
| 1 | `skill.gravecaller.grave-strength` | Grave Strength | Passive | `ModifyStat(ContactDamage, PercentAdd, +0.20, Minions)` | stat, **minions** |
| 2 | `skill.gravecaller.deeper-graves` | Deeper Graves | **Upgrade** of Exhume | `ModifySkillCooldown(exhume, PercentMult, −0.25)` | **rule** |
| 2 | `skill.gravecaller.knitted-bone` | Knitted Bone | Passive | `ModifyStat(MaxHp, PercentAdd, +0.50, Minions)` | stat, **minions** |

### Grave-Work — the bolt and the body

| Tier | Id | Name | Kind | Effect | What it changes |
|---|---|---|---|---|---|
| 1 | `skill.gravecaller.sharpened-bone` | Sharpened Bone | Passive | `ModifyStat(WeaponDamage, PercentAdd, +0.15)` | stat |
| 1 | `skill.gravecaller.quick-hands` | Quick Hands | Passive | `ModifyStat(FireRate, PercentAdd, +0.12)` | stat |
| 2 | `skill.gravecaller.marrow-tithe` | Marrow Tithe | Passive | `ModifyStat(HealPerKill, Flat, +2)` | **rule** |
| 2 | `skill.gravecaller.pale-vigour` | Pale Vigour | Passive | `ModifyStat(MaxHp, Flat, +15)` | stat |

### Rot — the shroud, and what Veilrot will become

| Tier | Id | Name | Kind | Effect | What it changes |
|---|---|---|---|---|---|
| 1 | `skill.gravecaller.shroud-veil` | Shroud Veil | Passive | `ModifyStat(MovementSkillCooldown, PercentAdd, −0.20)` | **rule-adjacent** |
| 1 | `skill.gravecaller.rot-feast` | Rot Feast | Passive | `ModifyStat(XpGain, PercentAdd, +0.15)` | **rule** |
| 2 | `skill.gravecaller.withering-step` | Withering Step | Passive | `ModifyStat(MoveSpeed, Flat, +0.3)` | stat |
| 2 | `skill.gravecaller.restless-dead` | Restless Dead | Passive | `ModifyStat(MoveSpeed, PercentAdd, +0.30, Minions)` | stat, **minions** |

**The mix, counted honestly: six stat lines, three rule changes, one Upgrade, one Active, and three
of the twelve aimed at minions.** Against CH §4's shares — ~45 % Passive, ~25 % Active, ~25 %
Upgrade — this is **one Active in twelve where the Oathbound's v1 has two**, and rule 3 is why.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Progression/LevelUpFlow.cs` | Core | **Substantial.** [Ledger row 5(i)](../ROADMAP.md#carry-forward-into-m5): the two Overflow `const`s become a constructor argument (rules 8–10) |
| `Tests/Core/Progression/LevelUpFlowTests.cs` | Tests.Core | **Substantial.** Seven construction sites, and the authored source asserted rather than the literal |
| `Tests/Game/Authoring/GravecallerTreeTests.cs` | Tests.Game | The twenty-seven assets pin their own numbers — `OathboundTreeTests`' shape |
| `Data/Skills/Gravecaller/*.asset` ×12 · `Data/Effects/Gravecaller/*.asset` ×12 · `Data/Trees/GravecallerTree.asset` | — | The nodes, their effects, the tree. Assets — listed, not counted |
| *small edits* | Core, Game | `ModeSpec` gains an `OverflowSpec`; `ModeDefinition` gains the block beside `XpBlock` (rule 9); `Descent.asset` carries 0.02 / 0.02; `RunSession.Start` passes it; `BootScope.prefab`'s `_skills` and `_trees` arrays gain twelve and one; `English.asset` gains **twenty-seven** rows (rule 7) |
| *ripple* | Tests.Core, Tests.Game | `ModeSpecTests` and `ModeDefinitionTests` gain the block; `TimeToKillTests` reads the curve rather than the `const` (rule 10); `SkillAuthoringTests`' two array counts move; `ContentValidationTests` sweeps thirteen more assets |

Only these files change. Anything else is a deviation: say so in *As built*.

## Behaviour

1. **Every number is in an asset and nothing is hard-coded** — ADR-0006, M3-02b's shape, and M3-12c
   rule 1 verbatim. Exhume's cooldown, count, radius and trigger are `SkillDefinition` and
   `RaiseMinionsDefinition` fields; the twelve effects are `EffectDefinition` assets a node points
   at. **The owner retunes here, in the Inspector, without a task** — the M2-03 precedent.
2. **No Keystone, and the three names stay free.** CH §3.2 reserves **The Host**, **Second Death**
   and **Rot Bloom**, and each is a build-defining node with a drawback v1 does not ship: The Host
   needs a `PlayerStat` for the cap ([M5-06a](M5-06a-what-a-legion-node-may-reach.md)'s table refuses
   one), Second Death needs an on-kill trigger primitive that does not exist, and Rot Bloom rewrites
   a Veilrot relationship the build has no meter for. M3-12c rule 5's argument, made again: taking a
   Keystone's name for a lesser node makes the Keystone feel like a repeat when M7-04 authors it.
3. **One Active ships and two are refused, and the refusal is the ROADMAP's own row being ruled on.**
   The M5-06 row names *"Exhume, Tether, Rot Nova"*. **Exhume ships** because M5-06a built the one
   verb it needs and `MinionSystem` was already the system it drives. **Tether does not**: a link
   between the player and an enemy with its own lifetime, its own break condition and its own view is
   [M3-11b](M3-11b-consecrate-and-zones.md)'s size — a system, a primitive and a task. **Rot Nova
   does not**, and for two reasons rather than one: it needs a radial-damage primitive nothing has
   built, and **its CH §4.2 trigger is dead** — *"Veilrot ≥ 50 and ≥ 4 enemies within 8 m"* against a
   `TriggerField.Veilrot` nothing writes, which M5-06a rule 8's new
   `EveryTriggerField_HasAWriter` now turns into a failing row rather than a silent one. Authoring it
   with the Veilrot clause removed would ship a skill under a false name; authoring it whole would
   ship an Active that never fires. **Both are M7-04's, with the Keystones**, and the Active share
   being 1 in 12 against CH §4's ~25 % is the cost, stated rather than absorbed.
4. **The Upgrade's parent is in the same branch at a lower tier, and it is an Active.** Deeper Graves
   (Legion, tier 2) upgrades Exhume (Legion, tier 1) — `TreeRules`' cross-check satisfied rather than
   argued (M3-03 rule 1), and CH §4's *"improves a skill you already own"* read literally: the parent
   is the one node in this tree that grants a skill. **Exhume is therefore tier 1**, which is also
   M3-12c rule 3's placement — an Active reachable in the first two picks is what M3-04's
   `ActiveBoost` exists to weight, and with one Active in the tree that boost is either useful here
   or useful nowhere.
5. **Three nodes aim at `Minions`, and they are what makes this the Gravecaller's tree rather than a
   recolour of the Oathbound's.** Grave Strength, Knitted Bone and Restless Dead are the first
   content in the project to use `StatTarget.Minions` (M5-06a rule 1), and each buys something the
   class can feel: 8 → 9.6 damage is four Husk hits instead of five; 20 → 30 HP is four Husk contacts
   instead of three; 3.0 → 3.9 m/s is a Wight that catches a fleeing Spitter at 2 m/s with margin to
   spare. **Rule 3 of M5-06a's stated cost applies to all three**: a Wight already standing keeps its
   old numbers, and the arena is replaced within CH §3.2's twenty seconds.
6. **Rot carries no Veilrot and no Pact, exactly as the Oathbound's Judgment carried none.** GD
   §13.2's corrupted nodes are M6-05's and have no shape yet; GD §10's meter is M6-04's. The branch
   is authored as *the shroud and what decay feeds* — a faster Shroudstep, more experience per kill,
   a faster player and faster Wights — and is renamed by nobody: the branch **key** is
   `tree.gravecaller.rot`, so M6-04 fills it in rather than replacing it. M3-12c rule 6's ruling, one
   class on.
7. **Twenty-seven `LocKey`s ship, and all twenty-seven resolve.** Twelve names, twelve descriptions
   and three branch headings. **This is the half M3-12c did not have to do:** it shipped twenty-four
   keys unresolved because `ILocalizer` did not exist yet, and M3-14a's
   `ContentValidationTests.EveryLocKey_ResolvesInEnglish` now sweeps every authored key on every
   `CharacterDefinition`, `SkillDefinition` and `SkillTreeDefinition` in the project. **So an English
   row per key ships in this PR or the suite is red**, and the descriptions are written to GD §13.1's
   two-second budget — one clause, the number in it, no clause about when.

### Ledger row 5(i) — what a level is worth

8. **`LevelUpFlow.OverflowDamage` and `OverflowMaxHp` become a constructor argument, and the row's
   own count was short by one for a milestone.** ADR-0006 says every number is in an asset; these two
   are `public const float` in `Core/Progression/`, they are **both** `0.02f`, and the ledger called
   them *"the 2 %"* singular until M5-00a counted them. **They supply more than half a deep run's
   power** — M3-12c rule 8's table has twenty-nine Overflow levels against twelve nodes at stage 30,
   ×1.58 of a ×1.73 multiplier — so retuning the single most load-bearing number in the levelling
   curve is currently a rebuild rather than an Inspector edit.
9. **The asset is `ModeDefinition`, beside `XpBlock`, and the shape is `XpBlock`'s.** *"How fast a
   mode levels you is the mode's own statement"* (GD §4.5) is already why the XP curve lives there,
   and what a level is worth **when there is nothing left to buy** is the same sentence finished. It
   is a `[Serializable]` nested class with two `[SerializeField, Min(0f)]` floats and a `ToSpec`, so
   it draws as one foldout and a mode created from the Create menu is playable; `ModeSpec` gains an
   `OverflowSpec` property and refuses a non-finite or negative value in its constructor, which is
   the single account of what a legal curve is.
10. **It is not a one-line change, and the ripple is counted rather than estimated.**
    `new LevelUpFlow(...)` has **one production call site** — `RunSession.cs`, inside the
    null-tree ternary — and **seven in `LevelUpFlowTests.cs`**, five of which are the null-guard rows
    that must each grow an argument. `LevelUpFlow.OverflowDamage` is read by **three test files**:
    `LevelUpFlowTests` twice, and `TimeToKillTests` three times, where
    `Ttk_OverflowPerLevelIsTheShippedConstant` exists precisely to stop the number drifting — that row
    survives and changes its source from the `const` to `Descent.asset`, which is a stronger claim
    than it made before. `HudPresenter`'s remarks cite the const by name and are corrected in place.
    **The two `const`s are deleted rather than left as defaults**: a default beside an authored value
    is a second place the number lives, and the next reader would not know which one the game used.
11. **Nothing about a level-up's behaviour changes, and a row proves it.** The two modifiers are
    still built once at construction, still `PercentAdd` under one source so ten levels pool to ×1.20
    rather than 1.02¹⁰ (ADR-0008's order), still applied before the pick is spent and announced last.
    `Descent.asset` carries **0.02 and 0.02**, so every number in every shipped run is identical
    before and after this task — which is what makes the row an ADR-0006 change rather than a balance
    change.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Tree_HasTwelveNodesInThreeBranches` | `GravecallerTree.asset` / `ToSpec` / `NodeCount` 12, three branches, `TierCount` 2 each, two ids per tier |
| `Tree_NamesTheGravecaller` | the asset / `ToSpec` / `CharacterId` is `character.gravecaller`, and `TryGetTreeFor` resolves it |
| `Tree_HasNoKeystone` | the twelve / — / no node's `Kind` is `Keystone` — rule 2 |
| `Tree_AvoidsTheKeystoneNames` | the twelve ids and names / — / none is `the-host`, `second-death` or `rot-bloom` — rule 2 |
| `Tree_ExhumeIsTierOneAndTheOnlyActive` | `TryLocate` and the kinds / — / (Legion, 1), and `TreeRules.ActiveCount` is **1** — rules 3, 4 |
| `Tree_UpgradeParentIsInBranchBelow` | Deeper Graves / the `TreeRules` constructor / no throw; its parent is Exhume, same branch, tier 1 — rule 4 |
| `Tree_ThreeNodesAimAtMinions` | the twelve / converted / exactly Grave Strength, Knitted Bone and Restless Dead carry `StatTarget.Minions` — rule 5 |
| `Tree_CarriesNoVeilrotAndNoPact` | the twelve / — / no effect and no trigger clause names Veilrot — rule 6, and the row `EveryTriggerField_HasAWriter` would catch |
| `Tree_IdsAreKebabCaseAndConstructible` | the twelve ids / — / each constructs, and a camelCase spelling of one is still refused — M3-12c ruling 1 |
| `Nodes_FileNamesMapToTheirIds` | the twenty-five assets / — / hyphen-to-PascalCase against the last id segment — M3-12c ruling 1's mapping |
| `Nodes_AreLinkedToAMonoScript` | the twenty-five / loaded / every `m_Script` resolves — [Traps §5](../../Traps.md) |
| `Exhume_CarriesItsAuthoredNumbers` | `Exhume.asset` / `ToSpec` / cooldown 20, one clause `MinionCount Below 2` **by name**, one `RaiseMinions(3, 2.0)` — rule 1, and the field asserted by name for M3-12c's Bulwark reason |
| `DeeperGraves_LowersExhumesCooldown` | the asset / `ToSpec` / `ModifySkillCooldown(skill.gravecaller.exhume, PercentMult, −0.25)`, and the id it names is a node of this tree |
| `MinionNodes_CarryTheirNumbers` | the three / `ToSpec` / +0.20 `ContactDamage`, +0.50 `MaxHp`, +0.30 `MoveSpeed`, all `Minions` — rule 5 |
| `MinionNodes_ChangeWhatAWightIsWorth` | the three applied, then a spawn / — / 9.6 damage (four hits on a stage-1 Husk, not five), 30 HP (four Husk contacts, not three) and 3.9 m/s against the Husk's 2 — rule 5's three claims, so a retune of either asset reddens this |
| `StatNodes_CarryTheirNumbers` | the six / `ToSpec` / the tables' values exactly |
| `Tree_EveryNodeHasTwoKeys` | the twelve / — / `NameKey` and `DescriptionKey` non-default and all twenty-four distinct |
| `Tree_BranchesAreKeyed` | the three / — / `tree.gravecaller.legion`, `.grave-work`, `.rot` — rule 6 |
| `Tree_EveryKeyResolvesInEnglish` | the twenty-seven / against `English.asset` / every one present — **rule 7, the row M3-12c did not owe** |
| `Boot_RegistersTheSecondTreeAndTwelveSkills` | `BootScope.prefab`'s two arrays / `Install`, resolve the catalog / twenty-four skills, two trees, and both `TryGetTreeFor` calls answer |
| `Run_AGravecallerStartsWithItsOwnTree` | `Start` with `character.gravecaller` / — / `Available` is the six tier-1 ids of **this** tree and none of the Oathbound's |
| `Run_TheOathboundIsUnchanged` | `Start` with `character.oathbound` / a full stage played out / the twelve Oathbound nodes, unmoved, and `Oathbound.asset` has no diff |
| `Overflow_ComesFromTheMode` | `Descent.asset`'s block / `ToSpec` / 0.02 and 0.02, and `LevelUpFlow` applies exactly those — rules 8, 9 |
| `Overflow_TheConstantsAreGone` | `LevelUpFlow` / reflected / no `OverflowDamage` and no `OverflowMaxHp` field — rule 10, so the literal cannot come back |
| `Overflow_ARetunedModeMovesTheGrant` | a mode authoring 0.05 / ten levels of Overflow / ×1.50 on `WeaponDamage`, not ×1.20 — the whole point of row 5(i), asserted |
| `Overflow_StillPoolsAdditively` | ten Overflow levels under one source / — / ×1.20, not 1.02¹⁰ — rule 11, ADR-0008's order held across the change |
| `Overflow_TheShippedRunIsIdentical` | the shipped `Descent.asset` / a run levelled past a full tree / every number equal to `m5-06a`'s — **rule 11, the row that makes this an ADR-0006 change and not a balance one** |
| `Mode_RefusesAnImpossibleOverflow` | a non-finite or negative value / `ModeSpec` constructed / throws, naming the field |
| `Ttk_OverflowPerLevelIsTheAuthoredValue` | *(existing, source changed)* `Descent.asset` rather than the `const` / — / still 0.02, and the stage-15 and stage-30 hit counts are unmoved — rule 10 |

**Guard rows are implied, not listed:** the Overflow block's non-finite doors, `LevelUpFlow`'s null
row for the new argument, and `ModeDefinition.OnValidate` on the two new fields.

## Manual verification (Editor / device)

1. **[Editor]** Open `GravecallerTree.asset` and the twelve node assets. *Expected: every field of
   the three tables above, and the `Data/Skills/Gravecaller/` and `Data/Effects/Gravecaller/` folders
   hold twelve each.*
2. **[Editor]** Open `Descent.asset`. *Expected: an Overflow foldout beside the XP one, carrying 0.02
   and 0.02 (rules 9, 11).*
3. **[Editor]** Play an **Oathbound** run and level past the twelfth node. *Expected: the Overflow
   toast, and the same numbers as before this PR — the class is unchanged and rule 11 says so.*
4. **[Editor]** Nothing about the Gravecaller can be played yet (M5-02 rule 9). *Expected: the menu
   still starts an Oathbound, and the second tree is reachable from the catalog and from tests and
   from nowhere else. The first run of this tree is **M5-07**'s.*
5. **[device]** GD §13.1's two-second read, on twelve **new** English descriptions — the second
   half of what [ledger row 2](../ROADMAP.md#carry-forward-into-m5) has been asking for since M3.

## Out of scope

- **The primitive Exhume casts, the `StatTarget.Minions` it aims with, and the trigger field it
  fires on.** [M5-06a](M5-06a-what-a-legion-node-may-reach.md).
- **Tether and Rot Nova.** Rule 3. M7-04, with the Keystones.
- **The Host, Second Death, Rot Bloom, and the remaining fifteen nodes.** Rule 2. M7-04.
- **Veilrot, Pacts and Ordeals.** Rule 6. M6-04 and M6-05.
- **Making the class playable.** [M5-07](M5-07-class-select-screen.md).
- **`ShardPayout.PerStage` / `PerBoss`**, the other two `const`s ADR-0006 does not cover. Struck at
  M4-07 and a [parking-lot](../ROADMAP.md#parking-lot) line promoted by M6-02; they are not part of
  row 5(i).
- **Retuning Overflow.** Rule 11 ships the shipped value; what it *should* be is M8-05's, now that it
  can be typed rather than compiled.

## As built

**Twelve nodes, twenty-five assets, and Overflow off the mode. 2 425 EditMode / 0 / 0 (+33 on
M5-06a's 2 392), twice on the final code; PlayMode 19 / 0 / 0 first run, known issue 1 quiet.**
Console after the run: 14 errors / 25 warnings, every one a fixture on its own failure path and none
naming a new file. Six assemblies compiled clean against Unity's own `csc` argument list with the
shipped analyzers — zero new warnings. `dotnet format whitespace --verify-no-changes` green over all
fifteen touched C# files. `ProjectSettings/TimeManager.asset` re-serialised itself again and was
reverted ([Traps §5](../../Traps.md)). `Oathbound.asset` and `Gravecaller.asset` are not in
`git status`.

**Ten deviations. Four change something.**

1. **The tree asset is `Data/Trees/Gravecaller.asset`, not `GravecallerTree.asset`** (Files table).
   CLAUDE.md's rule is that a data asset's file name is the last segment of its id;
   `tree.gravecaller` ends in `gravecaller`, the folder says it is a tree, and M3-14b renamed the
   Oathbound's for exactly this. `ContentValidationTests.EveryFileName_MatchesTheLastSegmentOfItsId`
   sweeps both and would have refused the spec's name.
2. **Twenty-five assets, not twenty-seven.** 12 skills + 12 effects + 1 tree. The header's
   *"twenty-seven assets"* is the twenty-seven **keys** counted twice; the Tests table says
   twenty-five and wins.
3. **`OverflowSpec` lives in `Core/Content/ModeSpec.cs`, not in a file of its own.** That file
   already declares `RosterEntry` and `BossRosterEntry` — authored value types belonging to the mode
   — and this is two floats with no arithmetic. `XpCurve` earned its own file by carrying `ToReach`.
   Keeping it here holds the spec's declared *three code files*; a fourth would still have been M.
4. **CHANGES SOMETHING — `ModeSpec`'s new argument is optional and last, so an unauthored mode
   grants nothing.** It belongs beside `xp`; putting it there moves **forty-two** call sites for a
   widening none of them says anything about. Appended, `default(OverflowSpec)` is (0, 0) — a legal
   statement rather than a hole, so unlike a defaulted `XpCurve` there is no second check. **The
   cost is a ripple rule 10 did not count:** `RunSessionResumeTests.Resume_DerivesOverflow` and
   `Resume_OverflowRunsBeforeHealthRestore` assert ×1.28 through a fixture mode and went red at 0 %.
   One edit to that file's `Mode()` helper, which makes the dependency visible where it was implicit.
5. **CHANGES SOMETHING — rule 10's *"changes its source from the `const` to `Descent.asset`"* is
   impossible and was replaced.** `TimeToKillTests` is in `Soulvail.Tests.Core`, which cannot reach
   `AssetDatabase` (M0-10) and now has nothing in `Soulvail.Core` left to read. Both it and
   `LevelUpFlowTests` hold a `private const float OverflowPerLevel = 0.02f` naming the asset in its
   remarks, and meet `GravecallerTreeTests.Overflow_ComesFromTheMode` at the number — the
   arrangement that file has had with `KeenCenser.asset` since M3-12c. The existing row is renamed
   `Ttk_OverflowPerLevelIsTheAuthoredValue`.
6. **CHANGES SOMETHING — `SkillTreeValidationTests.Registry()` needed a sixth primitive, and its own
   remarks predicted it.** All three `NoTree_CanStarve_*` walks went red with *"`RaiseMinions` … no
   handler is registered"*: that helper is a hand-kept list, like `ContentValidationTests.Written`.
   It is registered **unconditionally**, unlike `RunSession.Start`, which registers it only for a
   class with an army — the walks are about tier gating, and skipping the Gravecaller's tree would
   stop them proving the one thing they exist to prove.
7. **CHANGES SOMETHING — `OathboundTreeTests.Boot_RegistersTheTreeAndTwelveSkills` asserted 12 and
   1 as equalities**, so a file about one class was asserting that nobody had shipped since. Now a
   superset over its own twelve ids and `Contains.Item("tree.oathbound")`; the totals are
   `SkillAuthoringTests.Boot_ScopeCarriesTheTwoLists`' (24 and 2), and
   `ContentValidationTests`' three floors moved with them.
8. **No `OnValidate` branch on the two new fields**, against the *Guard rows are implied* note.
   `ModeDefinition.OnValidate` is documented as checking the id and only its shape — *"a bad stage
   number is visibly a bad stage number in the Inspector"* — and a negative Overflow is visibly
   negative. `[Min(0f)]` clamps the GUI and `OverflowSpec`'s constructor is the one account of what
   is legal, on `XpBlock`'s precedent.
9. **`Overflow_StillPoolsAdditively` was not added**; the existing `Overflow_PoolsAdditively` is
   that row unchanged, and now runs against an authored pair. Three rows were added instead —
   `Overflow_TheConstantsAreGone`, `Overflow_ARetunedModeMovesTheGrant` and
   `Overflow_DamageAndMaxHpAreTwoNumbers`, the last because the ledger called them *"the 2 %"*
   singular for a milestone.
10. **The damage half of the two `Overflow_*` run rows is asserted in `Tests.Core`, not through the
    session.** `RunState` hands out no weapon number — `Combat` is `internal` on
    [AR §18.2](../../Architecture.md#182-the-boundary) — so the run rows own `PlayerMaxHp` and
    `LevelUpFlowTests.Overflow_ARetunedModeMovesTheGrant` owns damage, over a flow built by hand.

**Rule 6 held with no finding:** `EveryTriggerField_HasAWriter` is green — Exhume authors
`MinionCount`, which has a writer. **The named skip expired as designed:**
`TheTreelessClass_IsStillTreeless` went red, and it and the `continue` beside it were deleted; what
was worth keeping from it is now `BothShippedClasses_ResolveTheirOwnTree`.
