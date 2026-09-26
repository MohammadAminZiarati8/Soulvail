# M7-04i — The Gravecaller's twenty-seven

**Size:** S · **Depends on:** M7-04h · **Branch:** `m7-04i-the-gravecallers-twenty-seven`
**Design refs:** CH §3.2, §4, §4.2, §5, §5.4; GD §10, §12.4, §12.5, §13.1, §13.2; AR §18.1; ADR-0006, ADR-0012 · **Ledger rows:** [2](../ROADMAP.md#carry-forward-into-m7) — rule 4 authors against the curve the row diagnoses; [10](../ROADMAP.md#carry-forward-into-m7) — twelve more Pacts, and M7-04a's one exception closed

## Goal

The Gravecaller's tree is CH §5's full shape. It brings the three Actives M5-06b refused and three
more, a Legion that stands eight, a Grave-Work that finally scales the weapon, and a Rot branch that
reads the meter. Knitted Bone stops being a node that does nothing.

## The tree

Ids are `skill.gravecaller.<slug>`. Tiers 1 and 2 are [M5-06b](M5-06b-gravecaller-tree-v1.md)'s,
unmoved ([M7-04h](M7-04h-the-oathbounds-twenty-seven.md)'s save rule). Pacts follow
[M7-04a](M7-04a-a-pact-on-every-node.md) rule 3 at 2.7×, with the Gravecaller's downsides. `(M)` marks
a `StatTarget.Minions` effect, every stat is `PercentAdd` unless marked, and every cooldown cut is
`PercentMult`.

### Legion — the army

| Tier | Slug | Name | Kind | Effect | Pact · Rot |
|---|---|---|---|---|---|
| 1 | `exhume` · `grave-strength` | *shipped* | | | |
| 2 | `deeper-graves` | *shipped* | | | |
| 2 | `knitted-bone` | Knitted Bone | Passive | **re-aimed:** `MinionLifespan +0.50` (M) | `+1.35` (M) · `XpGain −0.20` · 12 |
| 3 | `grave-call` | Grave Call | **Active** | `RaiseMinions(2, 2.5)` · 14 s · `EnemiesWithin6m AtLeast 4` | cooldown −0.40 · `MaxHp −0.15` · 18 |
| 3 | `fourth-grave` | Fourth Grave | Passive | `MinionCap +1 flat` (M) | `+3 flat` (M) · `XpGain −0.20` · 12 |
| 4 | `bone-harvest` | Bone Harvest | **Upgrade** of Exhume | `ExtendCast(exhume, RaiseMinions(1, 2))` | the raise at `3` · `MoveSpeed −0.15` · 12 |
| 4 | `grave-fury` | Grave Fury | Passive | `ContactDamage +0.30` (M) | `+0.81` (M) · `MaxHp −0.15` · 15 |
| 5 | `the-host` | **The Host** | **Keystone** | `MinionCap +4 flat` (M) · `ContactDamage PercentMult −0.35` (M) | — |

### Grave-Work — the bolt, and what a kill leaves

| Tier | Slug | Name | Kind | Effect | Pact · Rot |
|---|---|---|---|---|---|
| 1 | `sharpened-bone` · `quick-hands` | *shipped* | | | |
| 2 | `marrow-tithe` · `pale-vigour` | *shipped* | | | |
| 3 | `splintered-bone` | Splintered Bone | Passive | `WeaponDamage +0.25` | `+0.68` · `MoveSpeed −0.15` · 15 |
| 3 | `bone-storm` | Bone Storm | **Active** | `Empower(FireRate, PercentAdd, +0.60, 5)` · 18 s · `EnemiesInAcquireRange AtLeast 3` | cooldown −0.40 · `MaxHp −0.15` · 18 |
| 4 | `marrow-rounds` | Marrow Rounds | Passive | `FireRate +0.20` | `+0.54` · `XpGain −0.20` · 12 |
| 4 | `tempest-of-bone` | Tempest of Bone | **Upgrade** of Bone Storm | `ModifySkillCooldown(bone-storm, −0.30)` | `−0.45` · `MoveSpeed −0.15` · 12 |
| 5 | `second-death` | **Second Death** | **Keystone** | `MinionBlastDamage +25 flat` (M) · `MinionBlastRadius +3 flat` (M) | — |

### Rot — what the Veil gives the one who wants it

| Tier | Slug | Name | Kind | Effect | Pact · Rot |
|---|---|---|---|---|---|
| 1 | `shroud-veil` · `rot-feast` | *shipped* | | | |
| 2 | `withering-step` · `restless-dead` | *shipped* | | | |
| 3 | `rot-nova` | Rot Nova | **Active** | `Burst(8, 3.0, 1)` · 14 s · `Veilrot AtLeast 50` **and** `EnemiesWithin8m AtLeast 4` | cooldown −0.40 · `MaxHp −0.15` · 18 |
| 3 | `plague-step` | Plague Step | **Active** | `Empower(MoveSpeed, PercentAdd, +0.40, 4)` · 16 s · `Veilrot AtLeast 25` **and** `HpFraction Below 0.5` | cooldown −0.40 · `XpGain −0.20` · 18 |
| 4 | `blight-nova` | Blight Nova | **Upgrade** of Rot Nova | `ExtendCast(rot-nova, Empower(WeaponDamage, PercentAdd, +0.30, 5))` | the buff at `+0.81` · `MaxHp −0.15` · 15 |
| 4 | `fever` | Fever | **Active** | `Empower(WeaponDamage, PercentAdd, +0.40, 6)` · 20 s · `Veilrot AtLeast 25` **and** `EnemiesInAcquireRange AtLeast 3` | cooldown −0.40 · `MoveSpeed −0.15` · 18 |
| 5 | `rot-bloom` | **Rot Bloom** | **Keystone** | `BloomVeilrot(0.5)` | — |

**The mix, counted:**

| Kind | Count | Share |
|---|---|---|
| Actives | 6 — Exhume, Grave Call, Bone Storm, Rot Nova, Plague Step, Fever | 22 % |
| Upgrades | 4 — Deeper Graves, Bone Harvest, Tempest of Bone, Blight Nova | 15 % |
| Passives | 14 | 52 % |
| Keystones | 3 | |

The Active share goes from M5-06b's 8.3 % to 22 %. The Upgrades are fewer than CH §4's ~25 %: the
Gravecaller's cast verbs are raises and buffs, and a third cooldown cut would be filler.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/GravecallerTreeTests.cs` | Tests.Game | **Substantial rewrite.** The 27, the shipped 12 unmoved, the numbers, the Keystones, the saves |
| *small edits* | Data, Docs, Tests | `Data/Skills/Gravecaller/*` ×15 new and `KnittedBone.asset` re-aimed with its Pact; `Data/Effects/Gravecaller/*`; `Data/Trees/Gravecaller.asset` — five tiers a branch; `Prefabs/Composition/BootScope.prefab` — `_skills` gains fifteen; `Data/Localisation/English.asset` — 43 rows new and Knitted Bone's description rewritten (rule 9); `Pseudo.asset` regenerated; `Tests/Game/Authoring/PactCoverageTests.cs` — M7-04a's Knitted Bone exception and `Coverage_TheExceptionIsInert` deleted; `Docs/Characters.md` — §3.2's Keystone table as built, The Host's drawback re-read (rule 5), and §4.2's trigger table gains Rot Nova as built, Grave Call, Bone Storm, Plague Step and Fever |
| *ripple* | Tests.Game | `Tree_CarriesNoVeilrotAndNoPact` is deleted — its own message says *"change this row with it rather than before it"*, and this is Rot Nova arriving. `SkillTreeValidationTests.Registry()` gains `BloomVeilrot`. `SkillAuthoringTests` and `EmberwrightTreeTests`' `Catalog()` count go 60 → 75. `EmberwrightTreeTests.Run_TheOtherTwoAreUnchanged` reads the Gravecaller's 27. `ContentValidationTests`' floors rise |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

None. Every node is data over primitives that exist.

## Behaviour

1. **Every number is in an asset** (ADR-0006). The shipped twelve keep their ids, branches and tiers,
   M7-04h's rule, for its reason.
2. **Knitted Bone is re-aimed, because what it did was nothing.**
   - Its `MaxHp +50 %` on the Wights has had no reader since M5-06b: `MinionSystem.ApplyDamage` has no
     caller, and no enemy targets a Wight ([M7-04f](M7-04f-the-gravecallers-keystones.md)'s finding).
   - It becomes `MinionLifespan +0.50`, so a Wight stands 30 s where it stood 20. *Knitted* bone lasts.
   - The id, branch and tier are unchanged, so a save that took it resumes into the new effect. That
     is a retune, the same as an Inspector edit, and it cannot break a gate.
   - Its Pact closes [M7-04a](M7-04a-a-pact-on-every-node.md)'s one exception, so coverage is 24 of
     24 non-Keystones for all three classes by [M7-04j](M7-04j-the-emberwrights-twenty-seven.md).
3. **Restless Dead stays in Rot.**
   - The [parking-lot](../ROADMAP.md#parking-lot) line named this task as its promoter: *"M7-04 … will
     place this one again anyway."* It is placed where it is.
   - **Moved to Legion**, every save that took it after a Rot node and before a Legion one breaks
     Legion's tier-2 gate, and `SkillTree.Restore` strands that run (M7-04h's rule).
   - **Rot therefore stays closed to a minionless borrower**, as it is today, and the cost is stated
     rather than absorbed.
   - **The four new Rot nodes name no Wight.** A later change to the splash — dropping what a borrower
     cannot use, the way it drops the Keystone — would then lose Rot one node rather than five.
4. **The fifteen are authored against [row 2](../ROADMAP.md#carry-forward-into-m7)'s diagnosis.**
   - **The diagnosis.** *"The Gravecaller's whole tree carries one weapon-damage node … player damage
     grew +15 % across nineteen stages while a Husk's HP grew +108 %"*, and the curve drifts 4 → 8 by
     stage 16.
   - **Grave-Work now carries the bolt:**
     - Splintered Bone, +25 %;
     - Marrow Rounds, +20 % rate;
     - Bone Storm, +60 % rate for five seconds in eighteen, about +17 % averaged;
     - Rot's Fever and Blight Nova, +40 % and +30 % windows while the meter is up. Those also ride
       the class's own +1 % a point.
   - **Where they land.** They reach tier 3 at around six picks into a branch, which is where the
     drift begins.
   - **The Overflow they displace.** Fifteen levels, about +30 % damage and +30 % maximum HP, as the
     Oathbound's (M7-04h rule 4).
   - **Whether it is enough** is instrument A's, rebuilt, and the tuning M8-05's.
5. **The Keystones are M7-04f's mechanisms as data, and The Host's price is re-read.**
   - **The Host.** CH §3.2's *"all Wights have 50 % HP"* is a drawback nothing pays, so it is
     `ContactDamage ×0.65`. Seven Wights at 65 % are 4.55 against three, still a Keystone. With
     Fourth Grave the cap is **8**, `MinionSystem.MaxConcurrent` exactly. Fourth Grave's Pact (+3)
     plus The Host is 10, clamped to 8 by `LiveCap`, and the clamp is stated rather than hidden. CH
     §3.2's row is edited as built.
   - **Second Death.** Twenty-five in 3 m when a Wight kills, and what the blast kills does not blast.
   - **Rot Bloom.** New enemies 5 % slower at 25, +20 % maximum HP at 75, and a Claiming that drains
     over 200 s. It is CH §3.2's *"a Gravecaller who rushes to 100 Veilrot on purpose"*, playable.
6. **Rot Nova is CH §4.2's trigger whole**: `Veilrot AtLeast 50` and `EnemiesWithin8m AtLeast 4`.
   `TriggerField.Veilrot` has had a writer since M6-04, and the Gravecaller starts at 15 and gains
   ×1.5, so two Pacts put it in reach. M5-06b rule 3's two refusals are both answered: the burst
   exists ([M7-04b](M7-04b-a-burst-around-you.md)) and the clause can hold.
7. **Tether is not built, and it was never CH's.** The ROADMAP's M5-06 row named it; CH §4.2 does not.
   M5-06b rule 3 costed it as a system — a link with a lifetime, a break condition and a view — for
   one node. Grave Call and Plague Step fill the tier it would have.
8. **Twenty-four of twenty-seven carry a Pact**, the Keystones none. Fourth Grave's upside rounds
   to 3, M7-04a rule 3's whole counts. The extensions scale their amount, M7-04h rule 6's reading.
9. **Forty-three new strings and one rewritten**, all resolving: fifteen names, fifteen
   descriptions, twelve Pact descriptions, Knitted Bone's new Pact description, and its rewritten
   description. Each is written to the two-second budget.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Tree_IsTwentySevenInCh5sShape` | `Gravecaller.asset` / `ToSpec` / 27, three branches of five tiers, 2-2-2-2-1 — rule 1 |
| `Tree_TheShippedTwelveHaveNotMoved` | M5-06b's twelve ids / `TryLocate` / where M5-06b put them; Restless Dead in Rot, tier 2 — rules 1, 3 |
| `Tree_EachKeystoneIsAloneAtTheTop` | the three / `TreeRules` / no throw; only The Host, Second Death and Rot Bloom are Keystones — rule 5 |
| `Tree_TheMixIsCounted` | the kinds / — / 6 Active, 4 Upgrade, 14 Passive, 3 Keystone — the table |
| `Tree_TheNewRotNodesNameNoWight` | Rot's tier 3–4 nodes, every take, cast, extension and Pact effect / — / none aims at `Minions` — rule 3 |
| `KnittedBone_IsReAimed` | the asset / `ToSpec` / `ModifyStat(MinionLifespan, PercentAdd, 0.5, Minions)`, and a Pact — rule 2 |
| `KnittedBone_AWightLastsThirty` | a Gravecaller with it taken / a rise / `MinionSpawned` lifespan 30 — rule 2 |
| `Actives_CarryTheirNumbers` | the five new / `ToSpec` / cooldown, clauses by name, and the table's cast — rules 6, the table |
| `RotNova_FiresOnItsWholeTrigger` | a Gravecaller at 55 Rot, four Husks within 8 m / a skills tick / one cast, one burst of 3 × the weapon; at 45 Rot, none — rule 6 |
| `Upgrades_Keystones_Passives_CarryTheirNumbers` | the rest of the new / — / the tables exactly — rules 1, 5 |
| `Pacts_FollowTheConvention` | the twelve new and Knitted Bone's / — / M7-04a's convention row over the 27 — rule 8 |
| `TheHost_StandsEightWithFourthGrave` | both taken / eight rises / eight stand, each striking for 0.65 × — rule 5 |
| `TheHost_ThePactedFourthGraveClampsAtEight` | Fourth Grave as a Pact and The Host / ten rises / eight — rule 5 |
| `SecondDeath_AWightsKillBursts` | taken, a Wight kills a Husk beside two / — / one `BurstReleased` of radius 3 at the corpse — rule 5 |
| `RotBloom_TheSeventyFiveRowIsAGift` | taken, the meter at 80 / — / `MaxHp` ×1.2 — rule 5 |
| `Tree_EveryKeyResolvesInEnglish` · `Nodes_FileNamesMapToTheirIds` · `Nodes_AreLinkedToAMonoScript` | the new and rewritten assets / — / as M6-08's rows — rule 9 |
| `Boot_RegistersTheTwentySeven` | `BootScope.prefab` / `Install` / 75 skills, 4 trees — rule 1 |
| `Run_FillsAtLevelTwentyEight` · `Run_TheSplashOpensAtFourteen` | as M7-04h's — CH §5.2, §5.4 |
| `Run_AV4SaveOfTheShippedTwelveResumes` | a v4 snapshot of M5-06b's twelve in a legal order, Rot Feast pacted, Knitted Bone taken / `Start` / no throw; Knitted Bone restores as the lifespan — rules 1, 2 |
| `Splash_RotIsStillClosedToTheOathbound` | an Oathbound at its half-tree moment / `BranchesOf(character.gravecaller)` / Rot and Legion dead with the minions key, Grave-Work live — rule 3 |

**Guard rows are implied, not listed.**

## Manual verification (Editor / device)

1. **[Editor]** Open `Data/Trees/Gravecaller.asset`. *Expected: 2-2-2-2-1 in each branch, the shipped
   twelve where they were.*
2. **[Editor]** Take Knitted Bone. *Expected: Wights stand for thirty seconds.*
3. **[Editor]** Take Bone Storm and Splintered Bone. *Expected: bolts that visibly hurry for five
   seconds when three enemies are in range.*
4. **[Editor]** Push the meter past 50 with Pacts and take Rot Nova. *Expected: a cyan ring through a
   crowd of four or more.*
5. **[Editor]** Reach The Host with Fourth Grave. *Expected: eight Wights standing at once.*
6. **[Editor]** Reach Second Death. *Expected: a small ring where each Wight's victim falls.*
7. **[Editor]** Reach Rot Bloom and cross 75. *Expected: the health bar grows rather than shrinks;
   at 100 the Claiming bar falls at half its old speed.*
8. **[device]** Eight Wights and their blasts under Swarm; forty-three strings at 400 dpi. Row 1's.

## Out of scope

- **Moving Restless Dead.** Rule 3.
- **Tether.** Rule 7.
- **A splash that drops the nodes a borrower cannot use.** Rule 3 names it and leaves it with the
  [parking lot](../ROADMAP.md#parking-lot)'s updated line.
- **Tuning.** M8-05.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
