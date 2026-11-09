# SS-04d — The scorpion in the Sunscar: data, roster from stage 1

**Size:** M · **Depends on:** SS-04a, SS-04b, SS-04c · **Branch:** `ss-04d-scorpion-data`
**Design refs:** GD §8.4, §12.1, §12.3

## Goal

`enemy.scorpion` is authored, catalogued and in the Sunscar roster from stage 1, so the Sunscar's first stage waves, clears and opens its portal.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Scorpion.asset` | — | the archetype |
| `Data/Modes/Sunscar.asset` | — | roster entry |
| `Tests/Game/Content/SunscarTests.cs` | Tests.Game | rows replaced and added |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (Skitter header, five fields, `SkitterSpec` built when ring radius > 0, the Pack block's shape); `Prefabs/Composition/BootScope.prefab` (catalog entry); `Data/Localisation/English.asset` + `Pseudo.asset` (`enemy.scorpion.name`) |

## Behaviour

1. `Scorpion.asset`: id `enemy.scorpion`, name key `enemy.scorpion.name` ("Sand Scorpion"), 36 HP, 2.2 move speed, priority 1, threat 4, 18 XP, light, 8 damage, reach 1.5, wind-up 0.6, recover 0.8, aggro 30, Chaser, no hop, body `Scorpion.prefab`; Skitter 4 / 120 / 3 / 1.8 / 0.5.
2. A ring radius of 0 builds no `SkitterSpec`; every existing enemy asset is unchanged.
3. `Sunscar.asset` roster: `enemy.scorpion`, introduced 1, retired 0 (never). Boss roster and scripted stages stay empty; `RunSession`'s empty-roster arrival path is untouched.
4. The body's strip matches the data: `ScorpionTellView._stripLength` and `_stripHalfWidth` equal the asset's zone length and half-width.
5. `arena.ashen-reach.courtyard` and the Jungle's assets are untouched.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sunscar_Mode_RosterIsTheScorpion` (replaces `Sunscar_Mode_NoEnemies`) | mode / read / roster = [scorpion 1–never]; boss roster, scripted stages empty |
| `Scorpion_Asset_MatchesDesign` | catalog / spec / rule 1's numbers, `Skitter` present, `Behaviour` Chaser |
| `Scorpion_ExistingEnemies_NoSkitter` | every other enemy spec / read / `Skitter` null |
| `Scorpion_Strip_MatchesSpec` | prefab + spec / read / rule 4 |
| `Sunscar_Stage1_WavesClearsOpensPortal` (replaces `Sunscar_Stage1_SpawnsNothingNeverClears`) | session at Sunscar 1, the hero firing with sightings fed as `GravemawStageTests` does / tick ≤ 180 s / ≥ 1 `WaveStarted`, only scorpions spawned, one `StageCleared` (the portal opening, per StageEvents) |
| `Scorpion_NameKey_InEveryLanguage` | English, Pseudo / look up / both carry it |

`Courtyard_Untouched` and every other `SunscarTests` row stay green unchanged.

## Manual verification (Editor / device)

1. **[Editor]** DT-01, Sunscar stage 1: scorpions skitter in low and fast, a pack fanning to several sides before closing.
2. **[Editor]** Each wind-up shows a lit stinger and a red-orange strip ahead; nothing else on it is warm.
3. **[Editor]** One sidestep out of the strip avoids the hit; the 0.8 s tail-down recovery is a clear window to punish.
4. **[Editor]** Killing the waves opens the cyan portal; the body reads against the Dunes sand at phone size.
5. **[device]** The strip and the tip read at first look on a phone.

## Out of scope

- Sunscar boss stages, scripted stages, later enemies, retirement of the scorpion.
- Elites and affixes for it; any core or view change.
- Wave composition or budget (GD §12 as built).

## As built

_Commit body._
