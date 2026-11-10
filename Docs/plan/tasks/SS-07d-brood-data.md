# SS-07d — The brood in the Sunscar: data, roster from stage 4

**Size:** M · **Depends on:** SS-07a, SS-07b, SS-07c · **Branch:** `ss-07d-brood-data`
**Design refs:** GD §8.7, §8.4, §12.1, §12.3

## Goal

`enemy.brood` and `enemy.scorpling` are catalogued; the brood joins the Sunscar roster at stage 4.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/BroodMother.asset` | — | the mother |
| `Data/Enemies/Scorpling.asset` | — | the child, not in any roster |
| `Data/Modes/Sunscar.asset` | — | roster entry |
| `Tests/Game/Content/SunscarTests.cs` | Tests.Game | rows replaced and added |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (Brood header; `BroodSpec` when count > 0); `Prefabs/Composition/BootScope.prefab` (two catalog entries); `Data/Localisation/English.asset` + `Pseudo.asset` (two names) |

## Behaviour

1. `BroodMother.asset`: id `enemy.brood`, name key `enemy.brood.name` ("Brood Scorpion"), 50 HP, 1.5 move speed, priority 2, threat 12, 24 XP, 14 damage, reach 2, wind-up 0.8, recover 1.0, aggro 30, Scorpion's behaviour kind; skitter ring 5, fan 120, timeout 4, strip 2.4 × half-width 0.7; brood `enemy.scorpling` × 3, delay 0.5, radius 1.2; body `BroodMother.prefab`.
2. `Scorpling.asset`: id `enemy.scorpling`, name key `enemy.scorpling.name` ("Scorpling"), 10 HP, 2.8 move speed, priority 1, threat 3, 3 XP, 4 damage, reach 1, wind-up 0.5, recover 0.6, aggro 50, skitter ring 3, fan 120, timeout 2, strip 1.2 × half-width 0.35; no brood; body `Scorpling.prefab`.
3. A count of 0 builds no `BroodSpec`; every existing enemy asset is unchanged.
4. `Sunscar.asset` roster: scorpion 1–never and beetle 2–never as built, then `enemy.brood`, introduced 4, retired 0. The scorpling is in no roster. Boss roster and scripted stages stay empty.
5. Stages 1–3 spawn no brood; from stage 4 the budget may buy it; no wave ever buys a scorpling.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sunscar_Mode_RosterIsScorpionBeetleBrood` (replaces `Sunscar_Mode_RosterIsScorpionThenBeetle`) | mode / read / [scorpion 1–never, beetle 2–never, brood 4–never]; boss roster, scripted stages empty |
| `Brood_Asset_MatchesDesign` | catalog / spec / rule 1, `Brood.Child` = scorpling |
| `Scorpling_Asset_MatchesDesign` | catalog / spec / rule 2, `Brood` null |
| `Brood_ExistingEnemies_NoBrood` | every other enemy spec / read / `Brood` null |
| `Scorpling_InNoRoster` | every mode / read / no `enemy.scorpling` entry |
| `Scorpling_SlowerThanEveryClass` | scorpling vs each character / read / move speed < class speed |
| `Sunscar_RosterByStage_BroodFrom4` | mode / roster at 3, 4 / [scorpion, beetle]; + brood |
| `Sunscar_Stage4_WavesClear` | session at Sunscar 4, the hero firing, sightings fed as stage 2's row / tick ≤ 240 s / only scorpions, beetles, broods and their scorplings; one `StageCleared` after the last scorpling |
| `Brood_NameKeys_InEveryLanguage` | English, Pseudo / look up / both names |

Every other `SunscarTests` row stays green unchanged.

## Manual verification (Editor / device)

1. **[Editor]** DT-01, Sunscar stage 4: a fat, slow scorpion with three riders fans in and strikes into a large red-orange strip; one sidestep clears it.
2. **[Editor]** Killed, its hump swells and pulses twice, then three scorplings land round it and come from three sides.
3. **[Editor]** Killed at ~10 m, the scorplings cross open sand and die to single arrows before they reach the hero.
4. **[Editor]** The portal opens only after the last scorpling dies.
5. **[Editor]** Riders, swell and scorplings read through the storm; nothing cyan.
6. **[device]** Riders, swell and scorplings read at first look on a phone.

## Out of scope

- The jackal (SS-05d, stage 8), scripted stages, bosses, retirements.
- Elites and affixes for it; any core or view change.

## As built

_Commit body._
