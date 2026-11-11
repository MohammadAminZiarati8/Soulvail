# SS-09e — The glassback in the Sunscar: data, roster from stage 9

**Size:** M · **Depends on:** SS-09a, SS-09b, SS-09c, SS-09d · **Branch:** `ss-09e-glassback-data`
**Design refs:** GD §8.9, §8.2, §12.1, §12.3

## Goal

`enemy.glassback` is authored, catalogued and in the Sunscar roster from stage 9. Stages 1–8 stay as built, and stage 9 adds the shell that turns hits back.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Glassback.asset` | — | the archetype |
| `Data/Modes/Sunscar.asset` | — | roster entry |
| `Tests/Game/Content/SunscarTests.cs` | Tests.Game | rows replaced and added |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (Shine header, eleven fields, `ShineSpec` built when shine seconds > 0); `Prefabs/Composition/BootScope.prefab` (catalog entry); `Data/Localisation/English.asset` + `Pseudo.asset` (`enemy.glassback.name`) |

## Behaviour

1. `Glassback.asset`: id `enemy.glassback`, name key `enemy.glassback.name` ("Glassback Beetle"), 40 HP, 2.6 move speed, priority 2, threat 8, 24 XP, light, 12 damage, reach 1.2, wind-up 0.5, recover 0.8, aggro 30, Chaser. No pack, leap or brood. Shine 0.5 / 2 / 1 / 4 / 12 / 2 / 6 / 14 / 0.8 / 0.3 / 0.4 (SS-09a's order). Body `Glassback.prefab`. Look: projectile `GlassBolt.prefab` with marks-landing on, so the bolt draws as the shard into the lob ring (SS-09d prewarms it from this look); no `ProjectileSpec` is built for it — the bolt's numbers are the shine's.
2. A shine of 0 s builds no `ShineSpec`, and every existing enemy asset is unchanged.
3. `Sunscar.asset` roster: the entries as built (scorpion 1, beetle 2, brood 4, viper 6, jackal 8, all never retired), then `enemy.glassback`, introduced 9, retired 0 (never). Boss roster and scripted stages stay empty.
4. Stages 1–8 spawn no glassback. From stage 9 the wave budget may buy them.
5. The Jungle's assets and `arena.ashen-reach.courtyard` are untouched.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sunscar_Mode_RosterIsScorpionBeetleBroodViperJackalGlassback` (replaces `…ViperJackal`) | mode / read / rule 3's entries in stage order; boss roster, scripted stages empty |
| `Glassback_Asset_MatchesDesign` | catalog / spec / rule 1's numbers, `Shine` present, `Pack` null, `Behaviour` Chaser |
| `Glassback_Look_GlassBoltWithRing` | definition / read / projectile `GlassBolt`, marks landing on, no `ProjectileSpec` |
| `Glassback_ExistingEnemies_NoShine` | every other enemy spec / read / `Shine` null |
| `Glassback_TriggerInsideTheStormsClearing` | spec + `SunscarSandstormView` / read / `TriggerRange` < `ClearRadius` |
| `Glassback_SlowerThanEveryClass` | spec vs each character / read / move speed < class speed |
| `Sunscar_RosterByStage_GlassbackFrom9` | mode / roster at 8, 9 / without; with glassback |
| `Sunscar_Stage9_WavesClear` | session at Sunscar 9, the hero firing, sightings as stage 8's row / tick ≤ 240 s / only stage-9 roster enemies and scorplings; one `StageCleared` |
| `Glassback_NameKey_InEveryLanguage` | English, Pseudo / look up / both carry it |

Every other `SunscarTests` row stays green unchanged.

## Manual verification (Editor / device)

1. **[Editor]** DT-01, Sunscar stage 9: glassbacks walk in and bite. One the hero is not aiming at never glints.
2. **[Editor]** Targeted within 12 m: the shell brightens for half a second, then blazes white for 2 s and takes no damage.
3. **[Editor]** Firing into the shine sends pale shards back, each into a red-orange ring at most every 0.4 s. A step dodges them, and holding to switch Auto off stops them.
4. **[Editor]** The Beastmaster's pets biting a shining one take no bolts.
5. **[Editor]** After the shine the wing cases lift for 1 s and hits land double. About 4 s later it can shine again.
6. **[Editor]** Shell, shards and rings read through the storm. Nothing on it is cyan or blue.
7. **[device]** The glint reads at first look on a phone in time to lift a thumb.

## Out of scope

- Scripted stages, bosses (stage 10), retirements. Elites and affixes for it.
- Any core or view change. Wave budget (GD §12 as built).

## As built

_Commit body._
