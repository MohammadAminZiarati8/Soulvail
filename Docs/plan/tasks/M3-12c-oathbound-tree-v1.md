# M3-12c — The Oathbound tree v1: twelve nodes, and the number ledger row 1 has been waiting for

**Size:** M (two code files and twenty-six assets — the [sizing rule](../ROADMAP.md#how-to-read-this) counts code, and this is still a review) · **Depends on:** M3-12a, M3-12b, M3-11a, M3-11b, M3-02b (the authoring), M3-04 (the offer that draws from it) · **Branch:** `m3-12c-oathbound-tree-v1`
**Design refs:** CH §3.1, §4, §4.1, §4.2, §5, §5.2, §5.3; CC §6.4, §7; GD §12.1, §12.4, §12.5, §13.1; AR §10.1; ADR-0006, ADR-0012 · **Ledger rows:** **1 — the task that closes it or proves it is not closed**; **9** (twelve nodes is twenty-four unresolved keys, the densest content in the milestone)

## Goal

The first content a player can actually be offered: three branches of four, two of them skills that fire, four of them rules — and a time-to-kill test at stages 1, 15 and 30 that says whether M3 did the thing M3 exists to do.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Core/Progression/TimeToKillTests.cs` | Tests.Core | **ledger row 1**: the band at stages 1, 15 and 30 under a stated path (rule 8) |
| `Tests/Game/Authoring/OathboundTreeTests.cs` | Tests.Game | the assets pin their own numbers — `EnemyDefinitionTests`' shape (rule 9) |
| `Data/Skills/Oathbound/*.asset` ×12 | — | the nodes. Assets, not code files — listed, not counted |
| `Data/Effects/Oathbound/*.asset` ×13 | — | the effect definitions each node points at — thirteen, not twelve, because Unbowed carries two (M3-05 rule 6: *"+2 damage and +15 %" is two of these*) |
| `Data/Trees/OathboundTree.asset` | — | three branches of two tiers of two |
| *small edits* | | `Prefabs/Composition/BootScope.prefab` — the `_skills` and `_trees` arrays, **empty since M3-02b**, filled (M3-02b rule 7); `InstallerTests` if a count is asserted |

Only these files change. Anything else is a deviation: say so in *As built*.

## The tree

**Three branches, two tiers of two, twelve nodes, no keystone** — M3-02a rule 8's *"a partial tree is legal"*, which exists for exactly this content. Tier 2 needs one node taken in its branch (M3-03 rule 2), so a branch opens both of its tier-2 nodes after a single pick and the offer stays a real draw.

### Oath — shield and endurance

| Tier | Id | Name | Kind | Effect | What it changes |
|---|---|---|---|---|---|
| 1 | `skill.oathbound.tempered-vow` | Tempered Vow | Passive | `ModifyStat(MaxHp, Flat, +15)` | stat |
| 1 | `skill.oathbound.steady-breath` | Steady Breath | Passive | `ModifyStat(ShieldRechargeDelay, PercentAdd, −0.25)` | stat |
| 2 | `skill.oathbound.bulwark` | Bulwark | **Active** | `GrantShield(35, 5)` · 8 s · `IncomingProjectiles ≥ 1` | skill |
| 2 | `skill.oathbound.unbowed` | Unbowed | Passive | `ModifyStat(MaxHp, PercentAdd, +0.10)` · `ModifyStat(MoveSpeed, Flat, +0.3)` | stat |

### Censure — the cone

| Tier | Id | Name | Kind | Effect | What it changes |
|---|---|---|---|---|---|
| 1 | `skill.oathbound.keen-censer` | Keen Censer | Passive | `ModifyStat(WeaponDamage, PercentAdd, +0.15)` | stat |
| 1 | `skill.oathbound.long-reach` | Long Reach | Passive | `ModifyStat(WeaponRange, Flat, +1.5)` | **rule-adjacent** |
| 2 | `skill.oathbound.broad-censure` | Broad Censure | Passive | `ModifyStat(WeaponConeAngle, PercentAdd, +0.5)` | **rule** |
| 2 | `skill.oathbound.crashing-censure` | Crashing Censure | Passive | `KnockbackOnSwing(1.5)` | **rule** |

### Judgment — auras and holy actives

| Tier | Id | Name | Kind | Effect | What it changes |
|---|---|---|---|---|---|
| 1 | `skill.oathbound.consecrate` | Consecrate | **Active** | `SpawnHealZone(3.5, 6, 3, 0.5)` · 12 s · `HpFraction < 0.6` | skill |
| 1 | `skill.oathbound.zealotry` | Zealotry | Passive | `ModifyStat(FireRate, PercentAdd, +0.12)` | stat |
| 2 | `skill.oathbound.retribution` | Retribution | Passive | `ModifyStat(HealPerKill, Flat, +2)` | **rule** |
| 2 | `skill.oathbound.lasting-ground` | Lasting Ground | **Upgrade** of Consecrate | `ModifySkillCooldown(consecrate, PercentMult, −0.25)` | **rule** |

**The mix, counted honestly: six stat lines, four rule changes, two Actives.** The M3-00c ruling asked for eight and four; the two Actives are M3-11's entire output and a milestone that built two skills nobody can be offered would be absurd, so they take two of the eight. **Long Reach and Broad Censure are the two that blur the line** and both do so by design — M3-12a rule 8's point is that range and angle raise effective DPS *without* raising damage, which is the opposite of filler and is invisible to a single-target TTK test.

## Behaviour

1. **Every number is in an asset and nothing is hard-coded**, ADR-0006 and M3-02b's whole shape. The two Actives' cooldowns, radii, durations and triggers are `SkillDefinition` fields; the twelve nodes' effects are `EffectDefinition` assets a node points at, reusable across nodes (M3-02b rule 1). **The owner retunes here**, in the Inspector, without a task — the M2-03 precedent, and rule 8's arithmetic is what a retune has to be checked against.
2. **The Upgrade's parent is in the same branch at a lower tier.** Lasting Ground (Judgment, tier 2) upgrades Consecrate (Judgment, tier 1), which is `TreeRules`' cross-check (M3-03 rule 1) satisfied rather than argued: an Upgrade whose parent sat in another branch would let one branch wait on a pick the player may never make.
3. **Both Actives sit where the player can reach them early.** Consecrate is a tier-1 node — the first pick in Judgment can be a skill — and Bulwark is tier 2 in Oath. CH §4's *"an early pick reshapes the whole rest of the run"* wants an Active reachable in the first two levels, and M3-04's `ActiveBoost` (×2 while the player owns fewer than two) is weighting a draw that has something to weight.
4. **Twenty-four `LocKey`s, none of which resolve** (ledger row 9). `skill.oathbound.<name>` and `skill.oathbound.<name>.desc` for each node, plus `tree.oathbound.oath` / `.censure` / `.judgment` for the branches. **This is the task that makes row 9 concrete**: the level-up card, the Skills row, the tree view and the auto-cast row now all have real content to fail to display, and M3-15 rules on the whole of it.
5. **Names avoid the three keystones on purpose.** CH §3.1 reserves Unbroken, Wide Censure and Martyr, and each is a build-defining node with a drawback that v1 does not ship. *Broad* Censure is deliberately not *Wide* Censure — a 90° cone is a flank-cover node and a 360° ring at 60 % damage is a different thing entirely. Taking a keystone's name for a lesser node would make the keystone feel like a repeat when M7-04 finally authors it.
6. **Nothing in this tree touches Veilrot or ships a Pact variant.** GD §13.2's corrupted nodes are M6-05's and have no shape yet (M3-02a's Out of scope); a tree with one Pact in it would be content for a mechanic that cannot yet be authored.
7. **`BootScope`'s two arrays stop being empty, and that is the moment the milestone becomes playable.** M3-02b rule 7 shipped them empty with `Boot_EmptyListsAreLegal` saying that was a boot before M3-12, not a broken one. Filling them makes `TryGetTreeFor(character.oathbound)` answer true for the first time, which retires M3-03 rule 10's null-tree branch from every real build — the branch stays, tested, because the Editor's direct-Play path can still compose a catalog without it.

**The arithmetic**

8. **Ledger row 1, closed or not closed by this table.** Row 1 measures a Husk against the Censer: 36 HP at stage 1, 66 at stage 15, 99 at stage 30, against 13 damage — *"3 hits at stage 1, 5 at stage 14, 6 at stage 15, and 8 at stage 30"*, where GD §12.4 wants 3–5 at every depth. The tree and Overflow together have to close that, and **a twelve-node tree makes Overflow much larger than M3-08a rule 8's example did**:

   | | Stage 1 | Stage 15 | Stage 30 |
   |---|---|---|---|
   | Level (M3-01a rule 9's curve) | ~2 | ~20 | ~42 |
   | Nodes taken (tree fills at level 13, ≈ stage 9) | 1 | 12 | 12 |
   | Overflow levels (`Level − 1 − 12`) | 0 | 7 | **29** |
   | Damage multiplier — Keen Censer +15 % and Overflow, pooling `PercentAdd` | ×1.00 | ×1.29 | ×1.73 |
   | Censer damage | 13 | 16.8 | 22.5 |
   | Husk HP | 36 | 66 | 99 |
   | **Hits to kill** | **3** | **4** | **5** |

   The band holds at all three depths, and the reason it holds at 30 is **Overflow rather than the tree** — twenty-nine uncapped levels against twelve nodes. That is CH §5.2 working as designed, and it is also a warning: the v1 tree is small, so Overflow is doing most of the work, and M7-04's full twenty-seven will shift the balance back. **Zealotry's +12 % fire rate is deliberately excluded from the table** — it raises DPS, not damage per hit, so it changes time-to-kill without changing hits-to-kill, and row 1 is stated in hits.
9. **The TTK test states the path it measures and excludes what it cannot see.** Three rows — stages 1, 15, 30 — each constructing a `PlayerCombat` with the shipped Censer, applying the stated median path's `ModifyStat` effects plus the stage's Overflow levels, and dividing a scaled Husk's HP. **Excluded, with the reason in the test's own name:** Broad Censure and Long Reach (more targets and earlier engagement, not more damage per hit), Crashing Censure (buys time — M3-12b rule 9), Consecrate and Bulwark (survivability — M3-11a rule 11, M3-11b rule 12), Zealotry (rule 8). What is left is the honest single-target number, and a test that quietly counted the others would pass while the tree did nothing.
10. **The asset test pins the numbers rather than the design.** `OathboundTreeTests` reads the twelve assets and asserts what the table above says: twelve nodes, three branches of two tiers of two, both Actives' cooldowns and triggers, the Upgrade's parent, the four rule effects, and that every node carries two non-default `LocKey`s. `EnemyDefinitionTests`' shape — the point is that a retune shows up as a failing row and a considered diff rather than as a silent drift.

## Tests

| Test | Given / When / Then |
|---|---|
| `Ttk_StageOne_IsThreeHits` | the shipped Censer, no nodes, 0 Overflow / a stage-1 Husk / 3 hits (rules 8, 9) |
| `Ttk_StageFifteen_IsWithinTheBand` | Keen Censer, 7 Overflow levels / a stage-15 Husk (66 HP) / 4 hits, inside GD §12.4's 3–5 (rules 8, 9) |
| `Ttk_StageThirty_IsWithinTheBand` | Keen Censer, 29 Overflow levels / a stage-30 Husk (99 HP) / 5 hits — **ledger row 1's headline row** (rules 8, 9) |
| `Ttk_UnlevelledStageThirtyStillFails` | no nodes, no Overflow / a stage-30 Husk / 8 hits — row 1's original finding, kept as the control that proves the test measures the right thing |
| `Ttk_ExcludesSurvivabilityAndReach` | the test's own construction / — / Consecrate, Bulwark, Crashing Censure, Broad Censure, Long Reach and Zealotry are not applied, and the test name says so (rule 9) |
| `Ttk_OverflowPoolsWithTheNode` | Keen Censer +15 % and 29 Overflow levels / — / ×1.73, not 1.15 × 1.58 — `PercentAdd` under ADR-0008's order (rule 8) |
| `Tree_HasTwelveNodesInThreeBranches` | `OathboundTree.asset` / `ToSpec` / `NodeCount` 12, three branches, `TierCount` 2 each, two ids per tier (rule 10) |
| `Tree_NamesTheOathbound` | the asset / `ToSpec` / `CharacterId` is `character.oathbound` |
| `Tree_HasNoKeystone` | the twelve / — / no node's `Kind` is `Keystone` — M3-02a rule 8's partial tree, asserted (rule 10) |
| `Tree_ConsecrateIsTierOne` · `Tree_BulwarkIsTierTwo` | `TryLocate` / — / (Judgment, 1) and (Oath, 2) (rule 3) |
| `Tree_UpgradeParentIsInBranchBelow` | Lasting Ground / `TreeRules` ctor / no throw; its parent is Consecrate, same branch, tier 1 (rule 2) |
| `Tree_EveryNodeHasTwoKeys` | the twelve / — / `NameKey` and `DescriptionKey` both non-default, all twenty-four distinct (rule 4) |
| `Tree_BranchesAreKeyed` | the three / — / `tree.oathbound.oath`, `.censure`, `.judgment` (rule 4) |
| `Tree_AvoidsTheKeystoneNames` | the twelve ids / — / none is `unbroken`, `wideCensure` or `martyr` (rule 5) |
| `Tree_CarriesNoPact` | the twelve / — / no node references a Veilrot effect, and none exists to reference (rule 6) |
| `Consecrate_CarriesItsAuthoredNumbers` | `Consecrate.asset` / `ToSpec` / cooldown 12, one clause `HpFraction Below 0.6`, one `SpawnHealZone(3.5, 6, 3, 0.5)` (rules 1, 10) |
| `Bulwark_CarriesItsAuthoredNumbers` | `Bulwark.asset` / `ToSpec` / cooldown 8, one clause `IncomingProjectiles AtLeast 1`, one `GrantShield(35, 5)` (rules 1, 10) |
| `RuleNodes_CarryTheirPrimitives` | the four / `ToSpec` / a `KnockbackOnSwing(1.5)`, a `ModifySkillCooldown(consecrate, PercentMult, −0.25)`, a `ModifyStat(HealPerKill, Flat, 2)`, a `ModifyStat(WeaponConeAngle, PercentAdd, 0.5)` (rule 10) |
| `StatNodes_CarryTheirNumbers` | the six / `ToSpec` / the table's values exactly (rule 10) |
| `Boot_RegistersTheTreeAndTwelveSkills` | `BootScope.prefab`'s two arrays / `Install`, resolve the catalog / twelve skills, one tree, `TryGetTreeFor(character.oathbound)` true (rule 7) |
| `Boot_EmptyListsAreStillLegal` | *the existing M3-02b row* / — / unchanged: a catalog with no skills still boots (rule 7) |
| `Run_StartsWithATree` | `Start` with the shipped catalog / — / `State.TakenNodeCount` 0, `IsTreeFull` false, `IsLevelUpPending` false; and `Available` is the six tier-1 ids (rule 7) |
| `Run_FirstOfferDrawsFromTheSix` | level once / `OpenLevelUp` / three of the six tier-1 nodes, distinct (rule 7) |
| `Tree_FillsAtLevelThirteen` | twelve picks / — / `IsTreeFull`; the thirteenth level grants Overflow (rule 8) |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend. Kill the first wave and level: **three real cards**, drawn from the six tier-1 nodes, each reading its `LocKey`. Take one. Level again: the offer now includes that branch's tier-2 nodes (rule 3).
2. **[Editor]** Take Consecrate early. Drop below 60 %: the zone appears and heals you. Take Lasting Ground later and watch the Skills row's cooldown fall from 12 s to 9 s (M3-12b's manual step, now with shipped content).
3. **[Editor]** Take Broad Censure and swing. The cone is visibly wider, and it hits enemies at your flank that it did not before — the single clearest *"this node changed how I play"* moment in the milestone (GD §13.1).
4. **[Editor]** Take Crashing Censure. Every swing shoves. Notice that the stage takes slightly longer (rule 9's exclusion, felt).
5. **[Editor]** Take all twelve, then level again: no screen, a toast reading *"Overflow ×1"* (M3-10b rule 10).
6. **[Editor]** Play to stage 15 with the median path and count hits on a Husk. Four (rule 8). **This is the playtest the milestone exists for.**
7. **[device]** Ledger row 9 with real content: twenty-four keys across four screens. Whatever M3-15 decides, this is what it decides about.
8. **[device]** Ledger row 1's real verdict. The table is arithmetic against a stationary Husk; whether stage 15 *feels* like four hits with three Spitters shooting is what a phone answers and a test cannot.

## Out of scope

- **Keystones, and the remaining fifteen nodes** — M7-04. Rule 5 keeps their names free.
- **General content validation** — M3-14: unique ids across every catalog, every `LocKey` present, every tree well-formed. This task pins *these* assets; that one pins the rules for all of them.
- **Retuning against the playtest.** Rule 1 says the owner retunes in the Inspector; M3-15 is where a measured run either accepts the table or sends it back.
- **The other two classes' trees** — M5-06b and M6-08.
- **Pacts, Veilrot, Ordeals** — rule 6, and M6-05.
- **CH §5.2's exponent.** M3-01a rule 9 flagged that the curve fills the tree early and ruled *ship as authored*; rule 8's table is computed against 1.4 as shipped, and M3-15 owns the retune.

## As built

**Two code files, twenty-six assets, one prefab edit, and one edit outside the Files table.** `TimeToKillTests.cs` (Tests.Core, 8 rows), `OathboundTreeTests.cs` (Tests.Game, 23 rows), twelve `Data/Skills/Oathbound/*.asset`, thirteen `Data/Effects/Oathbound/*.asset`, `Data/Trees/OathboundTree.asset`, and `BootScope.prefab`'s two arrays filled. **The deviation is `SkillAuthoringTests`, not `InstallerTests`:** the Files table named the latter *"if a count is asserted"* and the count is asserted in the former — `Boot_ScopeCarriesTheTwoLists` has read `arraySize == 0` since M3-02b. The assertion was **inverted rather than deleted** (12 and 1, with *which twelve* left to `Boot_RegistersTheTreeAndTwelveSkills`), because rule 7's claim is that the arrays stop being empty here and a row that stopped asking would stop saying so. `InstallerTests` was not touched: it passes `Array.Empty<>` to `BootInstaller.Install` explicitly and never reads the prefab.

**Two further edits outside the Files table, and neither was a choice: the pre-commit format check went live during this task and refused the commit.** The .NET SDK (**10.0.401**) is installed now, so the hook's `dotnet format --version` probe succeeds and the check stops self-skipping — **M3-12c is the first task it has ever refused**. The violation was real rather than M0-02's phantom: three single-line `case X: y++; break;` arms in `Tree_IsSixStatsFourRulesAndTwoActives`, which `.editorconfig` wants split one statement to a line. Fixed, and **`dotnet format whitespace --folder --verify-no-changes` now passes across the whole of `Assets/_Project`**, so no pre-existing file is waiting to ambush the next commit. `CLAUDE.md`'s C# lint bullet and [Traps §5](../../Traps.md) both stated *there is no `dotnet` SDK on this machine* and both are corrected in place, two sentences each — filed there rather than left in this footer because the protocol puts a durable toolchain fact in Traps.md, and a future session reading the old text would be misled exactly as this one was. **The suite was re-run twice on the re-final code after the whitespace fix: 1 833 / 0 / 0 both times.**

**Ruling 1 — seven of the twelve ids in the table above cannot be constructed, and the table is corrected here.** `ContentId.IsHeadChar` is `[a-z0-9]` and `IsTailChar` adds only `_` and `-`, so `skill.oathbound.temperedVow` **throws**; so do `steadyBreath`, `keenCenser`, `longReach`, `broadCensure`, `crashingCensure` and `lastingGround`. This is M3-02b's recorded finding, handed to M3-14b, not a discovery. **All seven ship kebab-case** — `tempered-vow`, `steady-breath`, `keen-censer`, `long-reach`, `broad-censure`, `crashing-censure`, `lasting-ground` — and the other five (`bulwark`, `unbowed`, `consecrate`, `zealotry`, `retribution`) were always legal and are unchanged. `Tree_IdsAreKebabCaseAndConstructible` asserts both halves, including that `skill.oathbound.temperedVow` is still refused, so the row cannot pass on a grammar that quietly widened. **What it does to CLAUDE.md's asset-naming rule:** *"a data asset's file name matches the last segment of its ContentId"* becomes a **mapping rather than a match** — `TemperedVow.asset` ↔ `skill.oathbound.tempered-vow`, hyphen-to-PascalCase, exactly the spelling M3-02b predicted. `Nodes_FileNamesMapToTheirIds` is that mapping as a test. **M3-14b still owes the sweep and owes it in the corrected form**: its file-name check is hyphen-to-PascalCase across every catalog, not string equality, and this task only pins the twelve. **The asymmetry was not "fixed" by accident:** `LocKey.IsValid` forbids only whitespace, so the twenty-four keys were legal in either casing and were left alone.

**Ruling 2 — the three `Run_*` rows live in `OathboundTreeTests` (Tests.Game), not in `TimeToKillTests`.** They need a `RunSession` over the **shipped** catalog, the shipped catalog means `AssetDatabase`, and `Soulvail.Tests.Core` cannot reach it (M0-10). This is M3-12b's `Definition_*` placement problem one task on, answered the same way. `TimeToKillTests` keeps only what it can assert without opening a file, and writes the Censer's numbers out the way `DepthScalingTests` writes the curves out; the two fixtures meet at +15 % `PercentAdd` on `WeaponDamage`, and a retune that moved one and not the other reddens `TimeToKillTests`.

**Ruling 3 — `AssetDatabase.CreateAsset` was probed on one throwaway before twenty-six were authored, and so was the delete path.** Both work from a `RunCommand`: creation produces a real `MonoScript` link (`MonoScript.FromScriptableObject` resolved), and the plural `AssetDatabase.DeleteAssets(string[], List<string>)` removed the throwaway and its `.meta` (Traps §4 — the singular is refused, the plural is not). **Nothing was hand-written as YAML and no `m_Script` GUID was computed by hand**; every asset came from `ScriptableObject.CreateInstance(Type)` with the type reached by assembly-qualified name. `Nodes_AreLinkedToAMonoScript` is the row that would go red if any of that were wrong, which is the one failure mode that reports nothing anywhere (Traps §5).

**Ruling 4 — the prefab moved, by M3-09c's recipe verbatim.** `Type.GetType("Soulvail.Game.Composition.BootScope, Soulvail.Game")` → non-generic `root.GetComponentInChildren(type, true)` → `new SerializedObject(component).FindProperty("_skills")`, then `PrefabUtility.SavePrefabAsset` and a **read-back off a freshly loaded prefab** rather than off the handle (Traps §5). `Run.unity` and `Hud.prefab` did not move.

**The finding the spec does not have, and it was caught by a probe rather than by a compile: Bulwark shipped with the wrong trigger on the first pass.** `TriggerField`'s ordinals are `0 HpFraction, 1 ShieldFraction, 2 EnemiesWithin6m, 3 EnemiesWithin8m, 4 EnemiesInAcquireRange, 5 IncomingProjectiles` — the intent was `IncomingProjectiles` and the asset was written with **3**, which is `EnemiesWithin8m`. **It compiles, it converts, it passes every structural check, and it is a legal Active that fires at the wrong moment for ever**: a shield that arrives because something is near, rather than because a bolt is in the air, which is the opposite of CC §6.4's Bulwark and of M3-06's *"the runner ticks above the projectile step so a trigger sees the bolts still in the air"*. It was found by printing the converted spec from a `RunCommand` — the content probe the protocol asks for — and it would have survived a green compile and a green suite. `Bulwark_CarriesItsAuthoredNumbers` now asserts the field by name and carries the ordinal table in its own comment.

**The other finding is the fixture's, and it is `RunSession.Start` working:** a catalog carrying only the Husk **refuses the run**, because `Descent.asset`'s roster names all three enemies and the roster is resolved before anything is announced. Three rows went red on `KeyNotFoundException: No enemy with id 'enemy.spitter'` on the first EditMode run, which is content validation doing its job at the one moment it is cheap. The fixture ships the real roster.

**Rule 4's key shape is the project's convention, not the spec's prose.** Rule 4 says `skill.oathbound.<name>` and `skill.oathbound.<name>.desc`; the assets carry `<id>.name` and `<id>.description`, which is what `character.oathbound.name` and `enemy.husk.name` have used since M0 and what `SkillDefinition`'s own field initialisers say. A name key **equal to the id** would make the two strings identical, which is precisely the confusion `LocKey` exists as a separate type from `ContentId` to prevent (its own remarks). `Tree_KeysAreDerivedFromTheId` pins it. Twenty-four keys, all distinct, plus three branch keys.

**Rule 8's arithmetic was computed from `DepthScaling` and it corrects the table.** `h(n) = min(1 + 0.06(n−1), 4)` against the Husk's 36 gives **36 / 66.24 / 98.64**, not the table's 36 / 66 / 99, and `Ttk_HuskHpIsTheShippedCurveRatherThanTheSpecTable` asserts the curve's answers so a retune of `h(n)` reddens this file. `Scalings.Design()` was checked field-for-field against `Descent.asset` before being used. The Overflow grant is read off **`LevelUpFlow.OverflowDamage`** rather than off the table's "+2 %", pinned by `Ttk_OverflowPerLevelIsTheShippedConstant`. **The hits still land 3 / 4 / 5 and 8 for the control**, so the table was right for a reason it did not state.

**Ledger row 1 CLOSES, and the condition M3-12b attached is met in the test's own text rather than only in the spec's.** `Ttk_ExcludesSurvivabilityAndReach` names all six exclusions in code — Long Reach, Broad Censure, Zealotry, Crashing Censure, Bulwark, Consecrate — asserts that **none of them can address `WeaponDamage`**, then applies the four that are applicable for real and shows the damage and the hit count do not move. It is discriminating rather than declarative: a fixture that had quietly counted them would fail this row. **It does not invert** — the spec named a twelve-node build unkillable at stage 30 as a real outcome, and the measured answer is 5 hits, inside the band rather than past it. **The warning the row demands is recorded: the reason it closes at stage 30 is Overflow, not the tree** — 29 uncapped levels against twelve nodes, ×1.73 of which ×1.58 is Overflow's — so M7-04's full twenty-seven shifts the balance back and should be re-measured then. What is left for **M3-15** is the row's own three checklist items: the *played* hit counts at 1 / 15 / 30 (manual step 6), the `Ttk_UnlevelledStageThirtyStillFails` control, and the warning carried into the tag whatever the verdict. **M3-14b's starve check is untouched and still owed.**

**Ledger row 9 becomes concrete and does not close.** Twenty-four unresolved node keys plus three branch keys ship in `Data/` for the first time, and every reader the row lists now has real content to fail to display: the level-up card draws three of the twenty-four, the Skills row draws the two Actives' names and their trigger lines, the slot button draws a manual Active's name in 60 dp, the 24 dp auto-cast cell draws neither, and **the tree view draws twenty-seven strings on one screen** — which is the number the row predicted, arriving as twelve names, twelve descriptions and three branch headings rather than as twenty-seven node keys. **What M3-14a now owes is twenty-seven rows of English against a real file rather than a count in a spec**, and `M3-14b`'s coverage sweep has twenty-six assets to sweep. **What M3-15 has to look at is unchanged in kind and worse in degree:** GD §13.1's two-second rule can finally be asked, and the 24 dp cell is still the one reader no table can fix.

**Seven of the nine ledger rows were checked and only two moved.** Rows 3, 5 and 7 are closed. Row 2 (save migration) is untouched — `RunSnapshot.CurrentVersion` stays 3 and nothing here is saved — though `SkillTree.Restore` now has real ids to replay for the first time. Row 4 (device) gains manual steps 7 and 8 and nothing else. Row 6 (palette) is untouched: this task ships no colour, and M3-13a is next. **Row 8 is worth a sentence it did not have**: it predicted that M3 *changes the thing it measures*, and this is the task that makes that true in the build — a stage is now actually interrupted by a level-up screen, so the first honest 40–75 s reading is the one taken after this PR.

**Verified.** **1833 EditMode / 0 / 0, twice consecutively on the final code** (four runs in all), against M3-12b's 1802 — **31 new rows**. The spec's Tests table is 24 lines and 25 names, one of which (`Boot_EmptyListsAreStillLegal`, shipped as `Boot_EmptyListsAreLegal`) already existed and was not edited, so **24 were owed and 31 landed**. The seven over are the rulings and the guards the spec's own note owes: `Tree_IdsAreKebabCaseAndConstructible` and `Nodes_FileNamesMapToTheirIds` (ruling 1), `Tree_KeysAreDerivedFromTheId` (the key shape), `Ttk_HuskHpIsTheShippedCurveRatherThanTheSpecTable` and `Ttk_OverflowPerLevelIsTheShippedConstant` (rule 8 computed rather than copied), `Nodes_AreLinkedToAMonoScript` (Traps §5, twenty-six assets), and `Tree_IsSixStatsFourRulesAndTwoActives` (rule 10's mix, counted off the assets). **PlayMode 16 / 16, the count unmoved, every row named and passed** including `Ticker_RunsTheStepsInOrder`; `Boot_ReachesMenu_WithinFiveSeconds` boots the real prefab and now converts twelve skills and a tree on the way, which is the row most likely to have moved and did not. **The content was probed by calling into it**, not by trusting a green compile: twelve ids, three branch keys, both Actives' cooldowns and triggers, the Upgrade's parent, and `TreeRules` built over the shipped catalog without throwing — `Count=12`, `ActiveCount=2`, `TryGetTreeFor(character.oathbound)` **true for the first time in the project's life**. **Two assemblies, `Soulvail.Tests.Core` and `Soulvail.Tests.Game`, no third — no ripple.** Console **36 / 11 / 25** after the last EditMode run, with one queued after PlayMode so the sweep had something to read: **identical to baseline, and not one of the twenty-six new assets appears in it**, which is the evidence the content is right rather than a formality. `ProjectSettings/TimeManager.asset` dirtied and reverted for the **fourteenth time in fifteen tasks**, the same re-serialisation of `Fixed Timestep` into its rational form at an identical 0.02. `Run.unity` and `Hud.prefab` did not move.
