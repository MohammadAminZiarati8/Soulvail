# SS-10d — The antlion in the Sunscar: data, roster from stage 11

**Size:** M · **Depends on:** SS-10a, SS-10b, SS-10c · **Branch:** `ss-10d-antlion-data`
**Design refs:** GD §8.10, §8.2, §12.1, §12.3

## Goal

`enemy.antlion` is authored, catalogued and in the Sunscar roster from stage 11. Stages 1–10 stay as built.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Antlion.asset` | — | the archetype |
| `Data/Modes/Sunscar.asset` | — | roster entry |
| `Tests/Game/Content/SunscarTests.cs` | Tests.Game | rows replaced and added |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (Burrow header, ten fields, `BurrowSpec` built for `Antlion`); `Prefabs/Composition/BootScope.prefab` (catalog entry); `Data/Localisation/English.asset` + `Pseudo.asset` (`enemy.antlion.name`) |

## Behaviour

1. `Antlion.asset`: id `enemy.antlion`, name key `enemy.antlion.name` ("Antlion"), 56 HP, 2.4 move speed, priority 5, threat 12, 30 XP, light, aggro 30, behaviour `Antlion`. Burrow 4.5 / 2 / 1.2 / 1.5 / 4 / 1.6 / 0.8 / 16 / 2.5 / 0.6 (SS-10a's order). Body `Antlion.prefab`.
2. An enemy that is not `Antlion` builds no `BurrowSpec`. Every existing enemy asset is unchanged.
3. `Sunscar.asset` roster: the entries as built (scorpion 1, beetle 2, brood 4, viper 6, jackal 8, glassback 9, all never retired), then `enemy.antlion`, introduced 11, retired 0. Boss roster and scripted stages stay empty.
4. Stages 1–10 spawn no antlion. From stage 11 the wave budget may buy them.
5. The Jungle's assets are untouched.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sunscar_Mode_RosterIs…Antlion` (replaces `…Glassback`) | mode / read / rule 3's entries in stage order; boss roster, scripted stages empty |
| `Antlion_Asset_MatchesDesign` | catalog / spec / rule 1's numbers, `Burrow` present, `Behaviour` Antlion |
| `Antlion_ExistingEnemies_NoBurrow` | every other enemy spec / read / `Burrow` null |
| `Antlion_TunnelOutrunsEveryClass` | spec vs each character / read / tunnel speed > class speed |
| `Antlion_RingClearableInArm` | spec vs each class / read / speed × arm ≥ ring radius |
| `Sunscar_RosterByStage_AntlionFrom11` | mode / roster at 10, 11 / without; with antlion |
| `Sunscar_Stage11_WavesClear` | session at Sunscar 11, the hero firing, sightings as stage 9's row / tick ≤ 240 s / only stage-11 roster enemies and scorplings; one `StageCleared` |
| `Antlion_NameKey_InEveryLanguage` | English, Pseudo / look up / both carry it |

Every other `SunscarTests` row stays green unchanged.

## Manual verification (Editor / device)

1. **[Editor]** DT-01, Sunscar stage 11: antlions dig in at once, and a ripple crosses the sand toward the hero's back.
2. **[Editor]** Standing still: a red-orange ring and a sand swirl under the hero, and if the hero stays, a bite. Stepping out clears it.
3. **[Editor]** Walking: the ring lands where the hero was and misses.
4. **[Editor]** Underground it is never auto-aimed. Surfaced it sits 2.5 s and can be shot and stunned, then digs in.
5. **[Editor]** Against a rock: the ring rises on open ground, never in cover.
6. **[Editor]** The ripple and ring read through the storm. Nothing on it is cyan.
7. **[device]** The ripple reads at first look on a phone, and the ring is seen in time to step.

## Out of scope

- Scripted stages, bosses, retirements. Elites and affixes for it.
- Any core or view change.

## As built

_Commit body._
