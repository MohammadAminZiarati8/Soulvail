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
| 1 | `skill.oathbound.temperedVow` | Tempered Vow | Passive | `ModifyStat(MaxHp, Flat, +15)` | stat |
| 1 | `skill.oathbound.steadyBreath` | Steady Breath | Passive | `ModifyStat(ShieldRechargeDelay, PercentAdd, −0.25)` | stat |
| 2 | `skill.oathbound.bulwark` | Bulwark | **Active** | `GrantShield(35, 5)` · 8 s · `IncomingProjectiles ≥ 1` | skill |
| 2 | `skill.oathbound.unbowed` | Unbowed | Passive | `ModifyStat(MaxHp, PercentAdd, +0.10)` · `ModifyStat(MoveSpeed, Flat, +0.3)` | stat |

### Censure — the cone

| Tier | Id | Name | Kind | Effect | What it changes |
|---|---|---|---|---|---|
| 1 | `skill.oathbound.keenCenser` | Keen Censer | Passive | `ModifyStat(WeaponDamage, PercentAdd, +0.15)` | stat |
| 1 | `skill.oathbound.longReach` | Long Reach | Passive | `ModifyStat(WeaponRange, Flat, +1.5)` | **rule-adjacent** |
| 2 | `skill.oathbound.broadCensure` | Broad Censure | Passive | `ModifyStat(WeaponConeAngle, PercentAdd, +0.5)` | **rule** |
| 2 | `skill.oathbound.crashingCensure` | Crashing Censure | Passive | `KnockbackOnSwing(1.5)` | **rule** |

### Judgment — auras and holy actives

| Tier | Id | Name | Kind | Effect | What it changes |
|---|---|---|---|---|---|
| 1 | `skill.oathbound.consecrate` | Consecrate | **Active** | `SpawnHealZone(3.5, 6, 3, 0.5)` · 12 s · `HpFraction < 0.6` | skill |
| 1 | `skill.oathbound.zealotry` | Zealotry | Passive | `ModifyStat(FireRate, PercentAdd, +0.12)` | stat |
| 2 | `skill.oathbound.retribution` | Retribution | Passive | `ModifyStat(HealPerKill, Flat, +2)` | **rule** |
| 2 | `skill.oathbound.lastingGround` | Lasting Ground | **Upgrade** of Consecrate | `ModifySkillCooldown(consecrate, PercentMult, −0.25)` | **rule** |

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
- **The other two classes' trees** — M5-05 and M6-08.
- **Pacts, Veilrot, Ordeals** — rule 6, and M6-05.
- **CH §5.2's exponent.** M3-01a rule 9 flagged that the curve fills the tree early and ruled *ship as authored*; rule 8's table is computed against 1.4 as shipped, and M3-15 owns the retune.

## As built

_Filled at merge._
