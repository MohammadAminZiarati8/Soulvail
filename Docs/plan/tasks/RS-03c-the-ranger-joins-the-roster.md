# RS-03c — The Ranger joins the roster

**Size:** M · **Depends on:** RS-02c, RS-03b · **Branch:** `rs-03c-the-ranger-joins-the-roster`
**Design refs:** GD §6.1, §6.2, §12.4, §19; CH §4, §5, §5.4, §6 · **Ledger rows:** none

## Goal

The Ranger is a class you can pick. It has a bow that fires only standing still, a dodge roll you
can switch off, KayKit's body, and a nine-node tree: the owner's three passives, each taken three
times.

## The owner's rulings of 2026-09-25

- **The name is "Ranger"**, id `character.ranger`.
- **It is free while it is built.** No `UnlockSpec`, so it is owned from the start. Its price, and
  whether it ships in V1 at all (GD §19 puts a fourth class in V2), are the owner's before release.
  The parking lot carries that line.
- **Three passives, each upgradable three times:** attack speed; every *n* shots a volley, with
  *n* = 5, 4, 3; movement speed.
- **The dodge roll can be switched off:** one field, `Movement Skill Kind`, set to `None` (RS-03a).

## The tree

**Three branches, one per passive, three tiers of one node each.** Rank I is a Passive, and ranks
II and III are Upgrades whose parent is the rank below. CH §5's tier gate already makes each branch
a chain: rank II is offered only once rank I is taken. With a one-node tier the level-up offer is
simply *"which passive next"*, which is the owner's design. M3-00a's objection to one-node tiers
was variety across a twenty-seven-node tree, and it does not apply to nine.

| Branch (`tree.ranger.*`) | Rank I (Passive) | Rank II (Upgrade) | Rank III (Upgrade) | Effect per rank |
|---|---|---|---|---|
| **Draw** (`draw`) | Quick Draw | Quick Draw II | Quick Draw III | `FireRate` PercentAdd **+0.12** |
| **Volley** (`volley`) | Volley | Volley II | Volley III | `VolleyEvery` Flat **+5**, then **−1**, then **−1** |
| **Stride** (`stride`) | Fleet Foot | Fleet Foot II | Fleet Foot III | `MoveSpeed` PercentAdd **+0.08** |

- **Ids** are `skill.ranger.quick-draw`, `-2` and `-3`, and likewise for the others. Files follow
  the id's last segment: `QuickDraw.asset`, `QuickDraw2.asset`, `QuickDraw3.asset`.
- **No Pacts.** Nobody has written the corrupted forms, so `HasPact` is false on all nine.
- **No Keystones and no Actives.** That is the owner's design for now.

## The class's starting numbers

These are an engineering call inside the design's bands. RS-03e measures them and moves them.

| | Value | Why |
|---|---|---|
| HP | **90** | GD §6.1's band is 70–140. A kiter, above the Gravecaller's 80 |
| Speed | **3.2 m/s** | GD §6.1's band is 3.0–3.4, and it outruns the Spitter's 2.8 |
| Bow | Projectile, **13** damage, **2.2** shots/s, **10 m**, damage frame **0.72**, **30 m/s**, radius **0.5** | 13 kills a stage-1 Husk (36 HP) in 3 hits (GD §6.2's *"~3 at stage 1"*). Standing still ramps Focus to ×1.3, **37 DPS** planted, against the Oathbound's 39 |
| Fires while moving | **no** | RS-03a |
| Acquire / Focus | 12 m; 0.4 s, 1 s, ×1.3 | the shipped defaults |
| Movement skill | **Charge**: 5 m, 0.3 s, cooldown 2.5 s, damage 0, knockback 0, i-frame trail 0.05 s | a roll, with GD §6.1's i-frames. Zero damage touches nobody (RS-03a rule 8) |
| Volley | **3** arrows, **30°**, **×1.5** | the fan the owner picked |
| Shield, minions, Kindling, Veilrot | none | no signature mechanic yet — see *Out of scope* |
| Looks | body `Bodies/Ranger`, projectile `Arrow.prefab` | RS-02b, RS-02c |

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/RangerTests.cs` | Tests.Game | **New.** Rules 1–3 and 6 |
| `Tests/Game/Authoring/RangerTreeTests.cs` | Tests.Game | **New.** Rules 4 and 5 |
| *small edits* | Game | `ClassSelectPresenter._cards` defaults to four (rule 6) |
| *ripple* | Tests.Game | The class count **3 → 4** in `EmberwrightTests` and `GravecallerTests`. `_skills` **36 → 45** and `_trees` **3 → 4** in `SkillAuthoringTests`, `EmberwrightTreeTests` and `GravecallerTreeTests` (their `Catalog()` helpers count skills on disk). `ContentValidationTests`' floors: characters 4, skills 45, trees 4, effects 54. `ClassSelectPresenterTests.AuthoredCards` 4, whose over-capacity row uses five classes. `EmberwrightTests`' card count 4 |
| *assets* | | `Data/Characters/Ranger.asset`; `Data/Skills/Ranger/` (9); `Data/Effects/Ranger/` (9 `ModifyStatDefinition`s); `Data/Trees/Ranger.asset`; the four lists in `BootScope.prefab`; 23 rows in `English.asset` (2 class, 18 node, 3 branch) and `Pseudo.asset` regenerated; a fourth card in `ClassSelect.prefab` |

## Behaviour

1. **The Ranger converts and is in the catalog,** fourth after the Emberwright, owned from the start.
2. **Its numbers are the table's.** HP and speed sit inside GD §6.1's bands. It outruns every enemy.
   A stage-1 Husk dies in 3 hits. Its weapon does not fire while moving. Its movement skill is a
   `Charge` with no damage and no knockback. It carries a `VolleySpec` of 3, 30° and 1.5.
3. **It wears `Bodies/Ranger` and flies `Arrow.prefab`.**
4. **Its tree is the table's:** three branches, three one-node tiers each; rank I a Passive; ranks II
   and III Upgrades whose parent is the rank below; each effect exactly as tabled; no Pact.
5. **The ranks do what the owner said.** Taking all three Quick Draws adds +36 % fire rate. Taking
   all three Fleet Foots adds +24 % speed. Volley, Volley II and Volley III put `Every` at 5, 4 and 3.
6. **Class select shows four cards,** laid out so that none overlaps another and all four sit inside
   the Menu canvas's 1920 × 1080 reference at match 0.5.
7. **Every string resolves** in English and the pseudo-locale. The existing sweeps enforce it:
   `EveryLocKey_ResolvesInEnglish`, `Pseudo_HasARowForEveryEnglishRow` and
   `Pseudo_IsTheGeneratorsOutput`.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Ranger_ConvertsAndIsInTheCatalog` | BootScope's lists / catalog built / four classes, the Ranger fourth, `Unlock` null — rule 1 |
| `Ranger_NumbersAreTheTables` | the asset / `ToSpec` / every row of the table — rule 2 |
| `Ranger_StaysInsideTheBands` | — / — / HP in 70–140, speed in 3.0–3.4, above every enemy's, ⌈36 / 13⌉ = 3 — rule 2 |
| `Ranger_WearsItsBodyAndFliesArrows` | `ToLook()` / — / `Bodies/Ranger`, `Arrow.prefab` — rule 3 |
| `Tree_IsThreeChainsOfThree` | the tree asset / — / shape, kinds and parents as tabled — rule 4 |
| `Tree_EffectsAreTheTables`, `Tree_HasNoPact` | — / — / each stat, kind and value; no Pact — rule 4 |
| `Ranks_QuickDrawThreeTimesIsThirtySixPercent`, `Ranks_FleetFootThreeTimesIsTwentyFourPercent`, `Ranks_VolleyCountsFiveFourThree` | a Ranger run / the ranks taken in order / the stat values — rule 5 |
| `Cards_FourFitTheMenu` | `ClassSelect.prefab` / — / four cards, no overlap, inside the reference — rule 6 |
| existing sweeps | — / — / pass with the Ranger in — rule 7 |
| `Run_WearsTheBodyItsClassNames` (RS-02b's PlayMode row, one more case) | a run as the Ranger / — / `Bodies/Ranger` under the player — rule 3 |

## Manual verification (Editor / device)

1. **[Editor]** Class select. *Expected: four cards. The Ranger is free, and its card reads 90 HP,
   3.2 m/s and 29 DPS.*
2. **[Editor]** A run as the Ranger. *Expected: it runs with the bow down and shoots when it stops.
   Each level-up offers the next rank of each passive. By Volley III, every fourth arrow is a fan of
   three.*
3. **[Editor]** Set `Movement Skill Kind` to `None` on `Ranger.asset` and play. *Expected: no
   button, and no roll.* Set it back.

## Out of scope

- **The bow lowered, the volley lit and the roll animated:** RS-03d. Until then the Ranger's bow
  animates as RS-02a's, and the roll plays as a slide.
- **A signature mechanic and a Veilrot relationship.** CH §2 gives every class both, and neither is
  in the owner's design yet. `VeilrotSpec` is optional, and a class without one meets Veilrot
  neutrally: no gain multiplier, no damage per point, and no instant cast (`Veilrot`'s null
  relationship).
- **The running-shot skill, Actives, Keystones and Pacts:** the owner's next design round.

## As built

_6 000 bytes or fewer, measured._

**Files, resolved: three `BootScope` lists, not four.** The Ranger went onto `_characters` (fourth),
its nine nodes onto `_skills` (45) and its tree onto `_trees` (4). Effects are reached through their
nodes, and no fourth list takes an entry. Every asset was made in the Editor through `CreateInstance`
and `SerializedObject`, which is M3-12c's recipe: no YAML typed, no GUID computed.

**Deviation 1, three ripple rows beyond the Files table's list.**
`EmberwrightTreeTests.Stats_TheEnumIsAppendedNotInserted` skips `Data/Effects/Ranger/` beside the
Emberwright's folder: those effects postdate M6-08, and three store `VolleyEvery`'s ordinal 17.
`GravecallerTreeTests.Boot_RegistersTheSecondTreeAndTwelveSkills` lists tree ids, and gains
`tree.ranger`. `ClassSelectPresenterTests.ShippedCatalog` gains the Ranger, because
`Card_AnOwnedCardHasNoPriceOrDeed` walks every card and a fourth card was otherwise unbound. The
over-capacity row asks for `AuthoredCards + 1`, which is five.

**Rule 6, resolved: four cards, 420 wide, at x = ±230 and ±690.** Their gap stays 40, and the
outer margin is 60, the inset the Back button and the balance already use. `Cards_FourFitTheMenu`
reads the Menu scene's one `CanvasScaler` from the file (1920 × 1080, match 0.5). It does the
anchor-and-offset arithmetic itself, because an EditMode fixture has no screen to scale against.
It also asserts that no card sits under the Title, the balance or Back, which is "none overlaps
another" read for the whole screen. Card3 is a clone of Card2 whose eight references all point at
its own children. The prefab's other changes are three positions and widths and one `_cards` entry.

**Rule 5, resolved: the ranks are taken through the level-up.** Each rank row resumes a Ranger
run on `BootScope`'s own lists with three picks owed, then taps the rank's card through
`OpenLevelUp` and `ChooseOffer`. Before every pick it asserts that at most one rank of each chain
is open and that the offer is exactly those, which is manual step 2's *"each level-up offers the
next rank of each passive"*. `LevelUpFlow.Choose` redraws at once while a pick is owed, so from
the second pick an offer is already up. The stats are read off `RunState`'s internal combat and
motor by reflection, `BurningGroundTests`' route.

**Rule 2, pinned further than the table.** `Ranger_NumbersAreTheTables` also asserts the *Why*
column's arithmetic: 29 DPS resting, which is the card's figure, and 37 planted against the
Oathbound's 39.

**Rule 3's PlayMode case.** `Run_WearsTheBodyItsClassNames` takes `[Values]` over the Gravecaller
and the Ranger. The Knight assertion became `AssertTheRunWears(id, body, controller, view)`. The
Ranger case finds one Animator, `AC_Ranger`, and an injected `RangerAnimatorView`.

**The text.** The Volley's row names 5, 3 and 50 %, which are the asset's numbers, so a retune of
`VolleySpec` or of a rank is a retune of its row. Saving English re-folded three rows the task did
not touch, and they were restored by hand, so the diff is the 23 rows. Pseudo was regenerated by
`LocalisationSweepTests.Regenerate`.

**One asset difference, left as it is.** The nine new nodes carry the Pact fields at their
defaults (`_hasPact: 0`, `_pactDescriptionKey: skill.new.pact.description`). The older nodes
predate those fields and were never re-serialised. `ToSpec` reads the key only with a Pact, and
the placeholder sweep reads spec keys, so neither is a red row.

**How it was checked.** A red check moved Card3 to x = 800 and Volley III to 0. The two Ranger
fixtures went 8 / 3, and the three red rows were exactly `Cards_FourFitTheMenu`,
`Tree_EffectsAreTheTables` and `Ranks_VolleyCountsFiveFourThree`. Both were restored and diffed
back. The first targeted pass also failed the three rank rows on the redraw above, and the three
ripple rows of deviation 1.

**Not done here:** the bow lowered, the volley lit and the roll animated (RS-03d); manual steps
1–3, which are the owner's.
