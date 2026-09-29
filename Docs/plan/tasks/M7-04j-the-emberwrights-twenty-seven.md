# M7-04j — The Emberwright's twenty-seven, and the eighty-one counted

**Size:** S · **Depends on:** M7-04i · **Branch:** `m7-04j-the-emberwrights-twenty-seven`
**Design refs:** CH §3.3, §4, §4.1, §4.2, §5, §5.2, §5.4; GD §12.4, §13.1, §13.2, §19; AR §18.1; ADR-0006, ADR-0012 · **Ledger rows:** [2](../ROADMAP.md#carry-forward-into-m7) — rule 3; [10](../ROADMAP.md#carry-forward-into-m7) — the last twelve Pacts: coverage is 72 of 81, every node but a Keystone

## Goal

The Emberwright's tree is CH §5's full shape. GD §19's *"27-node in-run trees each (81 total)"* is
then true of the build, and CH §4's shares are measured rather than targeted.

## The tree

Ids are `skill.emberwright.<slug>`. Tiers 1 and 2 are [M6-08](M6-08-emberwright-tree-v1.md)'s,
unmoved ([M7-04h](M7-04h-the-oathbounds-twenty-seven.md)'s save rule). Pacts follow
[M7-04a](M7-04a-a-pact-on-every-node.md) rule 3 at 2.7×, with the Emberwright's downsides. Every stat
is `PercentAdd` unless marked, and every cooldown cut is `PercentMult`.

### Ember — the orb, and the heat that builds

| Tier | Slug | Name | Kind | Effect | Pact · Rot |
|---|---|---|---|---|---|
| 1 | `ember-touch` · `stoked-coals` | *shipped* | | | |
| 2 | `quickened-flame` · `long-burn` | *shipped* | | | |
| 3 | `flame-wave` | Flame Wave | **Active** | `Burst(5, 1.5, 2.5)` · 12 s · `EnemiesWithin6m AtLeast 2` | cooldown −0.40 · `MaxHp −10 flat` · 18 |
| 3 | `kindled-focus` | Kindled Focus | Passive | `FireRate +0.15` | `+0.41` · `MoveSpeed −0.10` · 12 |
| 4 | `wildflame` | Wildflame | **Upgrade** of Flame Wave | `ExtendCast(flame-wave, SpawnBurnZone(3, 3, 5))` | the burn at `14` a pulse · `MaxHp −10 flat` · 15 |
| 4 | `pyromancy` | Pyromancy | Passive | `WeaponDamage +0.20` | `+0.54` · `MaxHp −10 flat` · 15 |
| 5 | `wildfire` | **Wildfire** | **Keystone** | `ModifyStat(KindlingRetained, Flat, +0.5)` | — |

### Arcana — the actives, and the clock

| Tier | Slug | Name | Kind | Effect | Pact · Rot |
|---|---|---|---|---|---|
| 1 | `emberfall` · `arcane-haste` | *shipped* | | | |
| 2 | `deep-well` · `ashen-lore` | *shipped* | | | |
| 3 | `blaze-sigil` | Blaze Sigil | **Active** | `Empower(FireRate, PercentAdd, +0.50, 5)` · 16 s · `EnemiesInAcquireRange AtLeast 3` | cooldown −0.40 · `MoveSpeed −0.10` · 18 |
| 3 | `rapid-casting` | Rapid Casting | **Upgrade** of Emberfall | `ModifySkillCooldown(emberfall, −0.25)` | `−0.45` · `MaxHp −10 flat` · 12 |
| 4 | `ember-ward` | Ember Ward | **Active** | `GrantShield(40, 4)` · 24 s · `HpFraction Below 0.4` | cooldown −0.40 · `MovementSkillCooldown +0.25` · 18 |
| 4 | `arcane-flow` | Arcane Flow | Passive | `SkillCooldown −0.15` | `−0.41` · `MaxHp −10 flat` · 12 |
| 5 | `surge` | **Surge** | **Keystone** | `ModifyStat(FreeCastEvery, Flat, +5)` | — |

### Ash — the ground you leave behind

| Tier | Slug | Name | Kind | Effect | Pact · Rot |
|---|---|---|---|---|---|
| 1 | `cinder-nova` · `scorching-ground` | *shipped* | | | |
| 2 | `lingering-ash` · `emberheart` | *shipped* | | | |
| 3 | `firestorm` | Firestorm | **Active** | `SpawnBurnZone(5, 6, 5)` · 20 s · `EnemiesWithin8m AtLeast 5` | cooldown −0.40 · `MaxHp −10 flat` · 18 |
| 3 | `spreading-ash` | Spreading Ash | Passive | `PoolRadius +0.33` | `+0.89` · `MoveSpeed −0.10` · 12 |
| 4 | `smouldering` | Smouldering | **Upgrade** of Cinder Nova | `ExtendCast(cinder-nova, SpawnBurnZone(3, 4, 4))` | the burn at `11` a pulse · `MaxHp −10 flat` · 15 |
| 4 | `ashen-mantle` | Ashen Mantle | Passive | `MaxHp +0.15` | `+0.41` · `MoveSpeed −0.10` · 12 |
| 5 | `scorched-vail` | **Scorched Vail** | **Keystone** | `SpreadBurns(0.5, 2)` | — |

**The mix:** 6 Actives (Emberfall, Cinder Nova, Flame Wave, Blaze Sigil, Ember Ward, Firestorm),
4 Upgrades (Deep Well, Wildflame, Rapid Casting, Smouldering), 14 Passives and 3 Keystones.

**The eighty-one, counted** (CH §4's table, rule 6):

| Kind | Build | Share | CH §4's target |
|---|---|---|---|
| Active | 18 | 22 % | ~25 % |
| Upgrade | 13 | 16 % | ~25 % |
| Passive | 41 | 51 % | ~45 % |
| Keystone | 9 | 3 a class | 3 a class |

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/EmberwrightTreeTests.cs` | Tests.Game | **Substantial rewrite.** The 27, the shipped 12 unmoved, the numbers, the Keystones, the saves, and the splash rows at the new half-tree |
| *small edits* | Data, Docs, Tests | `Data/Skills/Emberwright/*` ×15; `Data/Effects/Emberwright/*`; `Data/Trees/Emberwright.asset` — five tiers a branch; `Prefabs/Composition/BootScope.prefab` — `_skills` gains fifteen; `Data/Localisation/English.asset` — 42 rows; `Pseudo.asset` regenerated; `Tests/Game/Authoring/ContentValidationTests.cs` — `Content_TheEightyOneAreCounted` (rule 6); `Docs/Characters.md` — §3.3's Keystone table as built with *Overflow* renamed *Surge* and why, §4's share paragraph as measured, and §4.2's trigger table gains Flame Wave, Blaze Sigil, Ember Ward and Firestorm |
| *ripple* | Tests.Game | `SkillTreeValidationTests.Registry()` gains `SpreadBurns`. `SkillAuthoringTests`' boot count goes 75 → 90. The `Catalog()` count in `OathboundTreeTests` and `GravecallerTreeTests` asserts the catalog holds every `SkillDefinition` on disk rather than a number (rule 7). The `AtHalfTree` helper takes fourteen, not six |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

None. Every node is data over primitives that exist.

## Behaviour

1. **Every number is in an asset**, and the shipped twelve keep their ids, branches and tiers
   (M7-04h's rule).
2. **Arcana stays lendable to every class.** Every one of its new nodes names a number every class
   has: `FireRate`, `SkillCooldown`, and a named Emberfall cooldown that waits harmlessly for a skill
   the borrower may take. It also uses a verb every run registers, `Empower` and `GrantShield`. Its
   Pacts' downsides are M7-04a rule 3's universal four. So M6-08's *"Arcana is lendable to everyone"*
   still holds at nine nodes. **Ember and Ash stay refused** to a class with no Kindling or no Blink,
   through their shipped nodes, as today.
3. **The fifteen are authored against [row 2](../ROADMAP.md#carry-forward-into-m7)'s third curve,
   which runs the other way.**
   - **The Emberwright sits *under* GD §12.4's band** at 2 hits through stage 9, and at 3–4 after. Hits
     to kill move with damage per hit, and fire rate does not move them.
   - **So one new node adds per-hit damage** (Pyromancy, +20 %, at tier 4). The rest add rate, area,
     cooldown and survival: Kindled Focus, Blaze Sigil, Flame Wave, Firestorm, Arcane Flow, Ember Ward
     and Ashen Mantle.
   - **The displaced Overflow is M7-04h rule 4's +30 %**, and most of it arrives here as something
     other than a bigger orb.
4. **The Keystones are [M7-04g](M7-04g-the-emberwrights-keystones.md)'s mechanisms as data, one
   renamed.**
   - **Wildfire** keeps half the ramp through a hit.
   - **Surge** makes every fifth cast free and instant. It is CH §3.3's *Overflow*, renamed because
     that word is the level-up toast's; CH §3.3 says so in its row.
   - **Scorched Vail** turns a pool that kills into a pool where the body fell.
5. **Twenty-four of twenty-seven carry a Pact**, the Keystones none. With M7-04h and M7-04i that is
   **72 of 81** — every node but the nine Keystones — and [row 10](../ROADMAP.md#carry-forward-into-m7)'s
   coverage half is answered in the build.
6. **CH §4's shares become measured.**
   - `Content_TheEightyOneAreCounted` walks the three V1 trees and asserts six things: 27 nodes each, 3
     Keystones each, **18** Actives, **13** Upgrades, **41** Passives, and 72 Pacts.
   - It also logs the shares.
   - CH §4's paragraph is rewritten to the table above, with why Upgrades sit under the target: the
     cast verbs are six, and a third cooldown cut on one skill is filler.
7. **No tree fixture pins the build's skill total.** Three rows in three files pinned 45 and would each
   move by 15 a task. Each now asserts that its catalog holds every `SkillDefinition` found on disk.
   `ContentValidationTests.ShippedSkills` is private and stays the one number that moves, as an
   `AtLeast` floor.
8. **Forty-two strings ship and resolve**, to the two-second budget. The three Keystones' descriptions
   are CH §3.3's sentences, with *Surge* for *Overflow*.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Tree_IsTwentySevenInCh5sShape` | `Emberwright.asset` / `ToSpec` / 27, three branches of five tiers, 2-2-2-2-1 — rule 1 |
| `Tree_TheShippedTwelveHaveNotMoved` | M6-08's twelve / `TryLocate` / where M6-08 put them — rule 1 |
| `Tree_EachKeystoneIsAloneAtTheTop` | the three / `TreeRules` / no throw; only Wildfire, Surge, Scorched Vail — rule 4 |
| `Tree_TheMixIsCounted` | the kinds / — / 6 Active, 4 Upgrade, 14 Passive, 3 Keystone — the table |
| `Tree_NoNodeIsCalledOverflow` | the 27 ids and names / — / none is `overflow` — rule 4 |
| `Actives_CarryTheirNumbers` · `Upgrades_Keystones_Passives_CarryTheirNumbers` | the new fifteen / `ToSpec` / the tables exactly — rule 1 |
| `Pacts_FollowTheConvention` | the twelve new / — / M7-04a's convention row over the 27 — rule 5 |
| `Splash_ArcanaIsBorrowableByEveryClass` | an Oathbound and a Gravecaller at their half-tree moments / `BranchesOf(character.emberwright)` / Arcana live for both; Ember and Ash dead with `ui.splash.refused.address` — rule 2 |
| `Wildfire_KeepsHalf` | taken, 30 stacks / a hit / 15 — rule 4 |
| `Surge_TheFifthCastIsFree` | taken, one Active / five casts / the fifth's `SkillCast` says 0 — rule 4 |
| `ScorchedVail_AKillSpreads` | taken, Emberfall kills a Husk / the pulse / a 2 m zone at the corpse — rule 4 |
| `Content_TheEightyOneAreCounted` | *(in `ContentValidationTests`)* the three V1 trees / — / rule 6's six numbers exactly — rules 5, 6 |
| `Tree_EveryKeyResolvesInEnglish` · `Nodes_FileNamesMapToTheirIds` · `Nodes_AreLinkedToAMonoScript` | the new assets / — / as M6-08's rows — rule 8 |
| `Boot_RegistersTheTwentySeven` | `BootScope.prefab` / `Install` / 90 skills, 4 trees — rule 1 |
| `Run_FillsAtLevelTwentyEight` · `Run_TheSplashOpensAtFourteen` | as M7-04h's — CH §5.2, §5.4 |
| `Run_AV4SaveOfTheShippedTwelveResumes` | a v4 snapshot of M6-08's twelve in a legal order / `Start` / no throw — rule 1 |

**Guard rows are implied, not listed.**

## Manual verification (Editor / device)

1. **[Editor]** Open `Data/Trees/Emberwright.asset`. *Expected: 2-2-2-2-1, the shipped twelve where
   they were.*
2. **[Editor]** Take Flame Wave and let two Husks reach you. *Expected: a ring that throws them back
   2.5 m and hurts them.* Take Wildflame. *Expected: the ring leaves burning ground.*
3. **[Editor]** Take Blaze Sigil. *Expected: five seconds of visibly faster orbs when three enemies
   are in range.*
4. **[Editor]** Take Firestorm and wait for five enemies within 8 m. *Expected: a 5 m burning circle
   for six seconds.*
5. **[Editor]** Reach Wildfire at a full ramp and take a hit. *Expected: the damage figure halves
   rather than dropping to base.*
6. **[Editor]** Reach Surge. *Expected: every fifth cast leaves its cooldown ring full.*
7. **[Editor]** Reach Scorched Vail and burn a crowd. *Expected: small pools where burned Husks fall,
   and no pool from those.*
8. **[device]** Twenty-four decals under Swarm with Scorched Vail; forty-two strings at 400 dpi. Row
   1's.

## Out of scope

- **Tuning, and CH §4's Upgrade share.** Rule 6 states the gap; M8-05 owns the numbers.
- **A HUD for Kindling or Surge.** [Row 3](../ROADMAP.md#carry-forward-into-m7).

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
