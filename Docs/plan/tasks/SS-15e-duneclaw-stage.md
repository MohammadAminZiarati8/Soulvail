# SS-15e — Put the Duneclaw in Sunscar stage 5

**Size:** M · **Depends on:** SS-15a–d · **Branch:** `dev` (direct)
**Design refs:** [GD §§9.1, 9.3](../../GameDesign.md#9-bosses), `ApexStageTests`, `SunscarTests`

## Goal

Sunscar stage 5 plays the full Duneclaw fight and opens the usual portal after victory.

## Files

| Path under `Assets/_Project/` | Purpose |
|---|---|
| `Data/Enemies/Duneclaw.asset` | `enemy.duneclaw`, stats, look and body |
| `Data/Enemies/DuneclawBoss.asset` | `boss.duneclaw`, central spawn and phase table |
| `Data/Modes/Sunscar.asset` | Stage 5 boss roster entry |
| `Tests/Game/Authoring/DuneclawStageTests.cs` | Content, dispatch, duration and run flow |
| Small edits | `Prefabs/Composition/BootScope.prefab` registers the assets; `Data/Localisation/English.asset` and `Data/Localisation/Pseudo.asset` gain `enemy.duneclaw.name` |

Also update the empty boss roster assertion in `Tests/Game/Content/SunscarTests.cs` to
expect exactly the stage-5 entry.

## Behaviour

1. `enemy.duneclaw` uses `EnemyBehaviourKind.Boss` and `Duneclaw.prefab`. Display name
   is “The Duneclaw”. Tune base HP against a stage-5 Ranger with expected level and DPS,
   using the live run damage path, targeting a 75–90 s probe including both phase beats.
   Record HP, loadout, level and elapsed time in *As built*.
2. `boss.duneclaw` spawns centrally, enters phases at 1, 0.66 and 0.33 and uses 1.5 s
   transition beats. Only phase 1 summons, exactly three `enemy.scorpling`.
   Boss summons grant zero XP and any survivors clear on phase 2 entry or boss death.
3. Stage 5 enters the Duneclaw directly on the Dunes and runs the usual boss HUD.
   Defeat follows the ordinary non-final boss clear path, opens the north portal and
   advances to the normal Sunscar stage 6. Preserve every other stage and mode entry.
4. Check the authored Dunes spawn and measured floor points with SS-15d's placement rule.
   Place two slabs with a clear walking route out of each slam circle.

## Tests

| Test | Given / When / Then |
|---|---|
| `Duneclaw_AssetsRegisteredAndNameLocalised` | Boot data / resolve ids and name / valid authored boss body and strings (1) |
| `Duneclaw_StageFiveRangerDamageProbe` | Expected Ranger build / damage through live session / kill within 75–90 s (1) |
| `StageFive_WrapperDispatchesDuneclawAndThreeScorplings` | Sunscar stage 5 / hit thresholds / real inner behaviour, correct summon count and cleanup (2, 3) |
| `Scorplings_NoXpAndClearOnBossDeath` | Phase 1 with live summons / kill summon or boss / no add XP and no stranded enemies (2) |
| `StageFive_BossDeathOpensPortalToNormalStageSix` | Start in Sunscar / clear boss and enter portal / stage 6 normal waves and final stage still 30 (3) |
| `DunesFloor_HasTwoSlabLocationsWithClearExits` | Stage-5 arena and central spawn / measure open ground / two usable positions (4) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 to Sunscar stage 5. Read both attacks at each phase, bait a slab, see
   three scorplings at 66% and none at 33%, then take the north portal into stage 6.
2. **[device]** Play from stage 4 through stage 6 and time the fight. Check tell readability,
   stable walking escapes at near and far positions, cue audibility and slab placement.

## Out of scope

Sunscar stages 10, 15, 20, 25 and 30.

## As built

_Written at merge in the commit body._
