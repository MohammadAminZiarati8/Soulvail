# M7-04h — The Oathbound's twenty-seven, and the two screens a full tree outgrows

**Size:** M · **Depends on:** M7-04g · **Branch:** `m7-04h-the-oathbounds-twenty-seven`
**Design refs:** CH §3.1, §4, §4.2, §4.4, §5, §5.2, §5.4; CC §6.4; GD §12.4, §12.5, §13.1, §13.2, §13.3; AR §10.1, §18.1; ADR-0006, ADR-0012 · **Ledger rows:** [2](../ROADMAP.md#carry-forward-into-m7) — rule 4 authors against its curve; [3](../ROADMAP.md#carry-forward-into-m7) — the tree screen already draws 27, the swing and the Banish list did not (rules 7, 8); [10](../ROADMAP.md#carry-forward-into-m7) — twelve more Pacts

## Goal

The Oathbound's tree is CH §5's full shape: three branches of four tiers of two, and a Keystone
alone at the top of each. That is 27 nodes, 24 of them able to arrive corrupted. The build's first
27-node tree also brings two screens: the swing is drawn as wide as it is, and the Sanctum lists every
node a player might banish.

## The rule every class task keeps: the shipped twelve do not move

**`SkillTree.Restore` enforces the gates on a saved take order.** A node moved to a higher tier or
into another branch makes an old save's order illegal, and the throw is worse than a refusal.
Traced:
- it leaves `RunSession.Start`;
- then `RunTicker.Start`, which has no catch;
- the Run scene sits with nothing running and no message;
- `run.json` is not deleted, so **Continue offers the same dead run on every launch** until the
  player taps Descend.

So **every shipped node keeps its id, its branch and its tier**, and tiers 3 to 5 go above them. That
keeps every v4 save resumable, pinned by rule 9's row. The hazard itself has no owner, and is a
[parking-lot](../ROADMAP.md#parking-lot) line from M7-00d.

## The tree

Ids are `skill.oathbound.<slug>`. Tiers 1 and 2 are [M3-12c](M3-12c-oathbound-tree-v1.md)'s,
unchanged but for [M7-04a](M7-04a-a-pact-on-every-node.md)'s Pacts. The Pact column follows M7-04a
rule 3 at the Oathbound's 2.25×; every stat is `PercentAdd` unless marked, and every cooldown cut
`PercentMult`.

### Oath — the shield, and staying on your feet

| Tier | Slug | Name | Kind | Effect | Pact · Rot |
|---|---|---|---|---|---|
| 1 | `tempered-vow` · `steady-breath` | *shipped* | | | |
| 2 | `bulwark` · `unbowed` | *shipped* | | | |
| 3 | `bastion` | Bastion | **Active** | `GrantShield(60, 6)` · 30 s · `HpFraction Below 0.35` | cooldown −0.33 · `MoveSpeed −0.10` · 18 |
| 3 | `stalwart-march` | Stalwart March | Passive | `MovementSkillCooldown −0.25` | `−0.56` · `MaxHp −25 flat` · 12 |
| 4 | `shield-wall` | Shield Wall | **Upgrade** of Bulwark | `ExtendCast(bulwark, Burst(4, 0.5, 3))` | the burst at `1.13` · `MaxHp −25 flat` · 15 |
| 4 | `undying` | Undying | Passive | `MaxHp +0.15` | `+0.34` · `MoveSpeed −0.10` · 12 |
| 5 | `unbroken` | **Unbroken** | **Keystone** | `ModifyStat(ShieldRefill, PercentMult, +1.0)` · `AegisBreakBurst(6, 0, 4)` | — |

### Censure — the cone

| Tier | Slug | Name | Kind | Effect | Pact · Rot |
|---|---|---|---|---|---|
| 1 | `keen-censer` · `long-reach` | *shipped* | | | |
| 2 | `broad-censure` · `crashing-censure` | *shipped* | | | |
| 3 | `sever` | Sever | **Active** | `Burst(6, 1.5, 2)` · 10 s · `EnemiesWithin6m AtLeast 3` | cooldown −0.33 · `MaxHp −25 flat` · 18 |
| 3 | `searing-censer` | Searing Censer | Passive | `WeaponDamage +0.20` | `+0.45` · `MaxHp −25 flat` · 15 |
| 4 | `rending-sever` | Rending Sever | **Upgrade** of Sever | `ExtendCast(sever, Empower(FireRate, PercentAdd, +0.30, 4))` | the buff at `+0.68` · `MoveSpeed −0.10` · 12 |
| 4 | `relentless` | Relentless | Passive | `FireRate +0.15` | `+0.34` · `MovementSkillCooldown +0.25` · 12 |
| 5 | `wide-censure` | **Wide Censure** | **Keystone** | `ModifyStat(WeaponConeAngle, Flat, +300)` · `ModifyStat(WeaponDamage, PercentMult, −0.4)` | — |

### Judgment — holy actives and the ground they bless

| Tier | Slug | Name | Kind | Effect | Pact · Rot |
|---|---|---|---|---|---|
| 1 | `consecrate` · `zealotry` | *shipped* | | | |
| 2 | `retribution` · `lasting-ground` | *shipped* | | | |
| 3 | `verdict` | Verdict | **Active** | `Burst(10, 1.0, 0)` · 16 s · `EnemiesInAcquireRange AtLeast 4` | cooldown −0.33 · `MoveSpeed −0.10` · 18 |
| 3 | `litany` | Litany | **Active** | `Empower(FireRate, PercentAdd, +0.40, 6)` · 20 s · `EnemiesWithin8m AtLeast 4` | cooldown −0.33 · `MaxHp −25 flat` · 18 |
| 4 | `hallowed-ground` | Hallowed Ground | **Upgrade** of Consecrate | `ExtendCast(consecrate, SpawnBurnZone(3.5, 6, 3))` | the burn at `7` a pulse · `MovementSkillCooldown +0.25` · 15 |
| 4 | `fervour` | Fervour | **Upgrade** of Litany | `ModifySkillCooldown(litany, −0.30)` | `−0.38` · `MaxHp −25 flat` · 12 |
| 5 | `martyr` | **Martyr** | **Keystone** | `AegisBreakBurst(10, 1.0, 0)` | — |

**The mix, counted:**

| Kind | Count | Share | CH §4's target |
|---|---|---|---|
| Actives | 6 — Bulwark, Consecrate, Bastion, Sever, Verdict, Litany | 22 % | ~25 % |
| Upgrades | 5 — Lasting Ground, Shield Wall, Rending Sever, Hallowed Ground, Fervour | 19 % | ~25 % |
| Passives | 13 | 48 % | ~45 % |
| Keystones | 3 | | 3 |

**The four Upgrades this task adds are three new behaviours and one cut**, where M3-12c's was a cut
alone.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/OathboundTreeTests.cs` | Tests.Game | **Substantial rewrite.** The 27, the shipped 12 unmoved, the numbers, the Keystones, the saves |
| `Game/Views/PlayerView.cs` | Game | **Substantial.** The swing drawn at `PlayerAttacked`'s angle and reach (rule 7) |
| `Tests/Game/Views/PlayerViewTests.cs` | Tests.Game | **New.** The wedge's width, reach and ring |
| `Game/Controls/BanishPicker.cs` | Game | **Substantial.** Rows cloned from a template to the run's tree, in a scroll (rule 8) |
| *small edits* | Core, Game, Data, Docs | `Core/Progression/SkillTree.cs` — `CanStillBeOffered(ContentId)` (rule 8); `Core/Progression/SanctumShop.cs` — `CanBuy(Reroll)` asks it (rule 8); `Prefabs/UI/Sanctum.prefab` — `Nodes` inside a vertical `ScrollRect`, one row template; `Data/Skills/Oathbound/*` ×15 new; `Data/Effects/Oathbound/*` — every effect and Pact effect they name; `Data/Trees/Oathbound.asset` — five tiers a branch; `Prefabs/Composition/BootScope.prefab` — `_skills` gains fifteen; `Data/Localisation/English.asset` — 42 rows (rule 10); `Pseudo.asset` regenerated; `Docs/Characters.md` — §3.1's Keystone table as built, and §4.2's trigger table gains Bastion, Verdict and Litany |
| *ripple* | Tests.Core, Tests.Game | `SkillTreeValidationTests` — `Registry()` gains `Burst`, `Empower`, `ExtendCast`, `AegisBreakBurst`; `ATreeWithNoKeystone_Passes` moves to a fixture tree (rule 11). `SkillAuthoringTests.Boot_ScopeCarriesTheTwoLists` goes 45 → 60. The `Catalog()` count in `GravecallerTreeTests` and `EmberwrightTreeTests` goes 45 → 60. `GravecallerTreeTests.Run_TheOathboundIsUnchanged` and `EmberwrightTreeTests.Run_TheOtherTwoAreUnchanged` read the Oathbound's 27. `ContentValidationTests`' floors rise. `SanctumPresenterTests`' two `Capacity == 12` rows read the tree's count. `SanctumShopTests` and `SkillTreeTests` gain rule 8's rows |

**The split line, if this grows**: the two screens (rules 7, 8) against the tree. Written here so it
is a cut made before the branch rather than after.

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Progression;

public sealed class SkillTree
{
    /// <summary>
    /// Whether <paramref name="id"/> could still reach an offer this run: untaken, unbanished, and not
    /// behind a gate a banish has closed for good (rule 8).
    /// </summary>
    public bool CanStillBeOffered(ContentId id);
}
```

```csharp
namespace Soulvail.Game.Controls
{
    public sealed class BanishPicker : MonoBehaviour
    {
        /// <summary>How many rows exist — cloned on demand, never fewer than the last list opened.</summary>
        public int Capacity { get; }
    }
}
```

## Behaviour

1. **Every number is in an asset and nothing is hard-coded** (ADR-0006, M3-12c rule 1). The owner
   retunes in the Inspector without a task.
2. **The shipped twelve keep their ids, branches and tiers**, for the reason above. Tiers 3 and 4 hold
   two nodes each and tier 5 the Keystone alone, which is `TreeRules.RequireKeystonePlacement`'s
   shape. Each Upgrade's parent is in its branch below it, `RequireParentBelowInBranch`'s rule.
3. **Each Active is a verb the tree did not have, and the trigger says when.**
   - **Bastion** is the last stand: a 60-point grant beside Bulwark's, a different source, when under a
     third.
   - **Sever** is CC §6.4's *"radial burst, ≥3 enemies within 6 m"*, for 1.5 × the swing and a shove.
   - **Verdict** is CC §6.4's *"Judgment — damage all in LOS, ≥4 enemies visible"*. It is renamed so
     the branch and a node do not share a word. It reads *in LOS* as *within 10 m*, because no
     player-side sight sense exists (M7-04b's *Out of scope*).
   - **Litany** is six seconds of +40 % attack speed when four close in.
4. **The fifteen are authored against [row 2](../ROADMAP.md#carry-forward-into-m7)'s curve, not against
   nothing.**
   - **What the fifteen replace.** A 27-node tree fills around stage 21 where a 12-node one filled at
     9 (CH §5.2, measured), so a deep run takes fifteen fewer Overflow levels. That is about **+30 %
     damage and +30 % maximum HP**, additive, that the tree now has to carry instead.
   - **What the Oathbound's fifteen carry.** Searing Censer's +20 % and Relentless's +15 % rate, a
     Litany window worth about +12 % at a 6 s / 20 s duty, Sever and Verdict's area, Undying's +15 %
     and Bastion's 60.
   - **Wide Censure pays for its ring** at ×0.6.
   - The Oathbound held 4 hits flat through stage 16 (M5-08). This keeps it near there, and the
     tuning stays [M8-05](../ROADMAP.md#m8--feel-perf-ship-titles-only)'s.
5. **The Keystones are M7-04e's mechanisms as data.**
   - **Unbroken** refills the Aegis at twice the rate and throws everything within 6 m back 4 m when
     it breaks.
   - **Wide Censure** swings a ring at 60 %.
   - **Martyr** hits everything within 10 m for what the Aegis absorbed since it was last full.
   - A full tree has both Unbroken and Martyr, and a break fires both (M7-04e rule 5).
6. **Twenty-four of twenty-seven carry a Pact, and the Keystones do not** (M7-04a rule 2).
   - **An extension's Pact is the same extension with its amount scaled**, not a second mechanism:
     Shield Wall's burst multiple, Rending Sever's buff, Hallowed Ground's burn per pulse.
   - Radius, seconds and shove stay, because they are shape rather than power.
   - The price is 15 when the amount is damage and 12 otherwise, M7-04a rule 3's column.
7. **The swing is drawn as wide and as long as it is.**
   - **The mesh.** `PlayerView` rebuilds its wedge when `PlayerAttacked.AngleDeg` or `Range` differs
     from the last build, and only then: a node taken, not a frame.
   - **The arrays.** The rebuild writes into arrays allocated in `Awake` for 24 segments, so 360° is a
     full disc and 60° is the wedge it was.
   - **Zero keeps the authored placeholder**, which is what a projectile swing says (M7-04e rule 8).
   - **Broad Censure's 90° has never been drawn**; from this task it is.
8. **The Sanctum lists every node a player could banish, and sells a Reroll only while something can
   still be offered.**
   - **The picker's rows.** `BanishPicker` clones its row template up to the list it is opened with —
     the run's tree, a borrowed branch included, so up to 34 — inside a vertical `ScrollRect`.
   - **When it allocates.** The clones are made when the Sanctum opens, a paused screen, and kept for
     the next opening.
   - **`Capacity` is the rows that exist**, and the two `SanctumPresenterTests` rows pinned at 12 read
     the tree's count.
   - **What `CanStillBeOffered` asks**, computed tier by tier from the bottom of each branch: untaken,
     unbanished, and
     - **its tier gate still reachable**: at least `tier − 1` nodes of its branch, at lower tiers, taken
       or still offerable. Only a lower tier can be taken first, because a node's own tier and those
       above need as many as it does;
     - **for an Upgrade**, a parent that is taken or still offerable — walked, for the Ranger's chains;
     - **for a Keystone**, every other node of its branch taken or still offerable.
   - **Reroll asks that; Banish does not.** `SanctumShop.CanBuy(Reroll)` asks whether any node can
     still be offered, where it asked whether any node was left. `CanBuy(Banish)` keeps *any untaken
     node*, which M6-02b rule 5 says is the player's call to make.
   - This promotes the [parking-lot](../ROADMAP.md#parking-lot) line M6-11d left, whose named
     promoter this is.
9. **An old save still resumes**, the rule above, pinned by a v4 snapshot that took the shipped
   twelve.
10. **Forty-two strings ship and all resolve** — fifteen names, fifteen descriptions, twelve Pact
    descriptions — to GD §13.1's two-second budget: one clause, the number in it. The Keystones'
    descriptions are CH §3.1's sentences with the numbers.
11. **The starvation proof stops reaching this tree, and the cost is stated.**
    `NoTree_CanStarve_Exhaustively` memoises by taken set and skips a tree over `ExhaustiveNodeLimit`
    (12), because a 27-node tree is 2²⁷ states. From this task the Oathbound is covered by the two rows
    that already walk every tree: `NoTree_CanStarve_Sampled` (1 000 seeded sequences) and
    `NoTree_CanStarve_SingleBranchFirst` (the six branch orders). The first row's own remarks say
    this is *"strong evidence and not a proof"* and that it matters at M7-04; this is that moment,
    written down. The exhaustive row keeps its `walked > 0` guard through the Ranger's nine.
    `ATreeWithNoKeystone_Passes` asserted the Oathbound has none; it moves to a fixture tree, because
    what it tests is the rule, not the Oathbound.
12. **Nothing here allocates on a tick** beyond what the primitives already promise. The view rebuilds
    on a change, and the picker on an opening.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Tree_IsTwentySevenInCh5sShape` | `Oathbound.asset` / `ToSpec` / `NodeCount` 27, three branches of `TierCount` 5, two a tier for tiers 1–4 and one at 5 — rule 2 |
| `Tree_TheShippedTwelveHaveNotMoved` | the twelve M3-12c ids / `TryLocate` / each at the branch and tier M3-12c put it — rule 2 |
| `Tree_EachKeystoneIsAloneAtTheTop` | the three / `TreeRules` over the tree / no throw; `IsKeystone` true for Unbroken, Wide Censure, Martyr and nothing else — rules 2, 5 |
| `Tree_TheMixIsCounted` | the kinds / — / 6 Active, 5 Upgrade, 13 Passive, 3 Keystone; `TreeRules.ActiveCount` 6 — the table |
| `Tree_EveryUpgradeParentIsBelowIt` | the five Upgrades / — / each parent in its branch at a lower tier — rule 2 |
| `Tree_EveryExtensionNamesItsParent` | Shield Wall, Rending Sever, Hallowed Ground / — / each `ExtendCast.SkillId` is its own `ParentId` — rule 6 |
| `Actives_CarryTheirNumbers` | Bastion, Sever, Verdict, Litany / `ToSpec` / cooldown, trigger clauses by name, and the one cast effect with the table's numbers — rule 3 |
| `Upgrades_CarryTheirNumbers` | the four new / — / the table's effects exactly — rule 6 |
| `Keystones_CarryTheirNumbers` | the three / — / the table's effects exactly, no Pact — rules 5, 6 |
| `Passives_CarryTheirNumbers` | the four new / — / the table's numbers — rule 1 |
| `Pacts_FollowTheConvention` | the twelve new Pacts / — / M7-04a's `Convention_EveryPactFollowsTheTable` passes over the 27, extensions scaled on their amount alone — rule 6 |
| `Tree_EveryKeyResolvesInEnglish` | every name, description, Pact description and branch key / against `English.asset` / present — rule 10 |
| `Nodes_FileNamesMapToTheirIds` · `Nodes_AreLinkedToAMonoScript` | the new assets / — / as M6-08's rows — Traps §5 |
| `Boot_RegistersTheTwentySeven` | `BootScope.prefab` / `Install` / 60 skills, 4 trees, and the Oathbound's 27 resolve — rule 2 |
| `Run_FillsAtLevelTwentyEight` | an Oathbound at level 28 with 27 picks owed / 27 choices / `IsTreeFull`, and the next level is Overflow — CH §5.2 |
| `Run_TheSplashOpensAtFourteen` | an Oathbound with the Gravecaller owned / 14 takes / `IsSplashPending` — CH §5.4 |
| `Run_AV4SaveOfTheShippedTwelveResumes` | a v4 snapshot whose `TakenNodeIds` are the twelve in M3-12c's legal order, two of them pacted / `Start` / no throw, twelve taken, two pacted — rule 9 |
| `Run_BothAegisKeystonesFireOnOneBreak` | a full tree, the Aegis broken by a Spitter bolt / one tick / two `BurstReleased` — rule 5 |
| `Run_WideCensureSwingsARing` | Wide Censure taken, Husks ahead and behind / a swing resolves / both damaged at 0.6 × — rule 5 |
| `Swing_DrawsTheLiveAngle` | *(in `PlayerViewTests`)* `PlayerAttacked(facing, 90, 8)` / — / the wedge's outermost vertices sit ±45° from the facing at 8 m — rule 7 |
| `Swing_ARingIsAFullDisc` | angle 360 / — / 24 segments closing the circle — rule 7 |
| `Swing_ZeroKeepsThePlaceholder` | `PlayerAttacked(facing)` / — / the authored 60° wedge, not rebuilt — rule 7 |
| `Swing_RebuildsOnlyOnAChange` | two swings at the same angle / — / one rebuild — rules 7, 12 |
| `Banish_ListsTwentySeven` | *(in `SanctumPresenterTests`)* a fresh Oathbound in the Sanctum / Banish opened / 27 rows, the last reachable by scrolling — rule 8 |
| `Banish_KeepsItsRowsForTheNextOpening` | opened twice / — / no second clone pass — rule 8 |
| `Reroll_RefusedWhenOnlyAnOrphanIsLeft` | *(in `SanctumShopTests`)* every node taken but an Upgrade whose parent was banished / `CanBuy(Reroll)` / false; `CanBuy(Banish)` true — rule 8 |
| `Reroll_RefusedWhenOnlyABlockedKeystoneIsLeft` | a branch with a banished node, its Keystone the last untaken node / — / false — rule 8 |
| `Offered_ABanishedTierClosesTheTiersAbove` | *(in `SkillTreeTests`)* both tier-1 nodes of a branch banished / `CanStillBeOffered` of its tier-2 to tier-4 nodes and its Keystone / all false, and the other branches unaffected — rule 8 |
| `Offered_WalksAChainOfUpgrades` | *(in `SkillTreeTests`)* a Ranger with Quick Draw banished / `CanStillBeOffered(quick-draw-3)` / false — rule 8 |
| `NoTree_CanStarve_Sampled` · `NoTree_CanStarve_SingleBranchFirst` | *(existing, unchanged)* / over the 27 / every walk fills; `ATreeWithNoKeystone_Passes` green on its fixture — rule 11 |

**Guard rows are implied, not listed.**

## Manual verification (Editor / device)

1. **[Editor]** Open `Data/Trees/Oathbound.asset`. *Expected: three branches of five tiers — two, two,
   two, two, one — and the twelve shipped nodes where they were.*
2. **[Editor]** Play an Oathbound with Crashing Censure. *Expected: Husks at the cone's edge are
   pushed slightly outward rather than straight ahead (M7-04e rule 7), and the swing is drawn at
   60°.* Then take Broad Censure. *Expected: the drawn swing widens to 90°.*
3. **[Editor]** Take Litany and Sever, walk into a wave. *Expected: Sever's cyan ring and shove when
   three close in; Litany's six seconds of visibly faster swings when four do.*
4. **[Editor]** Take Hallowed Ground. *Expected: Consecrate's circle now also burns the Husks that
   stand in it.*
5. **[Editor]** Reach Unbroken and let a hit break the Aegis. *Expected: every Husk within 6 m thrown
   back; the Aegis refills in half the time.*
6. **[Editor]** Reach Wide Censure. *Expected: the swing is a full ring, everything around you
   hit.*
7. **[Editor]** Reach Martyr and let the Aegis break after soaking hits. *Expected: a 10 m ring and
   every Husk inside it hurt.*
8. **[Editor]** Open the Sanctum's Banish on a fresh stage-1 Oathbound. *Expected: every untaken node
   listed, scrolling past the twelfth.*
9. **[Editor]** Resume a run saved before this task. *Expected: Continue resumes it, with its twelve
   nodes.*
10. **[device]** Forty-two new strings at 400 dpi, and the ring swing at 60 fps. Row 1's.

## Out of scope

- **Drawing a borrowed branch on the tree screen.** [Row 3](../ROADMAP.md#carry-forward-into-m7)'s,
  unchanged. `TreeViewPresenterTests.Tree_DrawsEveryNode` already proves a 27-node tree draws.
- **Tuning.** M8-05.
- **A mark for a running buff.** M7-04c's *Out of scope*.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
