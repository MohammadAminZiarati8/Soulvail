# SS-11d — The priest in the Sunscar: data, roster from stage 14

**Size:** M · **Depends on:** SS-11a, SS-11b, SS-11c, SS-10d · **Branch:** `ss-11d-priest-data`
**Design refs:** GD §8.11, §8.2, §12.1, §12.3

## Goal

`enemy.jackal-priest` is authored, catalogued and in the Sunscar roster from stage 14. Stages 1–13 stay as built.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/JackalPriest.asset` | — | the archetype |
| `Data/Modes/Sunscar.asset` | — | roster entry |
| `Tests/Game/Content/SunscarTests.cs` | Tests.Game | rows replaced and added |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (Mend header, seven fields, `MendSpec` built when channel seconds > 0); `Prefabs/Composition/BootScope.prefab` (catalog entry); `Data/Localisation/English.asset` + `Pseudo.asset` (`enemy.jackal-priest.name`) |

## Behaviour

1. `JackalPriest.asset`: id `enemy.jackal-priest`, name key `enemy.jackal-priest.name` ("Jackal Priest"), 60 HP, the shaman's move speed, priority 8, threat 16, 32 XP, light, no attack (damage 0), behaviour `Shaman`. The Shaman block as `Shaman.asset`'s (hold 11 m, flee 4 m, its flee numbers). Mend 10 / 12 / 0.9 / 3 / 0.08 / 3 / 2 (SS-11a's order). Body `JackalPriest.prefab`.
2. A mend of 0 s builds no `MendSpec`. `Shaman.asset` and every other enemy asset are unchanged.
3. `Sunscar.asset` roster: the entries as built (scorpion 1, beetle 2, brood 4, viper 6, jackal 8, glassback 9, antlion 11, all never retired), then `enemy.jackal-priest`, introduced 14, retired 0. Boss roster and scripted stages stay empty.
4. Stages 1–13 spawn no priest. From stage 14 the wave budget may buy them.
5. The Jungle's assets are untouched.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sunscar_Mode_RosterIs…Priest` (replaces `…Antlion`) | mode / read / rule 3's entries in stage order; boss roster, scripted stages empty |
| `Priest_Asset_MatchesDesign` | catalog / spec / rule 1's numbers, `Mend` and `Shaman` present, `Behaviour` Shaman |
| `Priest_ExistingEnemies_NoMend` | every other enemy spec, the Moss Shaman's included / read / `Mend` null |
| `Priest_HoldsInsideTheStormsClearing` | spec + `SunscarSandstormView` / read / hold distance < `ClearRadius` |
| `Sunscar_RosterByStage_PriestFrom14` | mode / roster at 13, 14 / without; with priest |
| `Sunscar_Stage14_WavesClear` | session at Sunscar 14, the hero firing, sightings as stage 11's row / tick ≤ 240 s / only stage-14 roster enemies and scorplings; one `StageCleared` |
| `Priest_NameKey_InEveryLanguage` | English, Pseudo / look up / both carry it |

Every other `SunscarTests` row stays green unchanged.

## Manual verification (Editor / device)

1. **[Editor]** DT-01, Sunscar stage 14: the pale-robed priest stays back, behind another enemy, and never attacks.
2. **[Editor]** Hurt an enemy near it: a lime beam runs from the sun disc to the most hurt one, and its health climbs.
3. **[Editor]** One arrow on the priest snaps the beam with a flicker, and it does not mend again for a few seconds.
4. **[Editor]** Rushing it makes it run. The auto-aim takes it first whenever it is in range and in sight.
5. **[Editor]** The beam reads through the storm. Nothing on it is cyan or gold.
6. **[device]** At first look on a phone, the priest reads as different from the slinger, and the beam shows who is being healed.

## Out of scope

- Scripted stages, bosses, retirements. Elites and affixes for it.
- Any core or view change.

## As built

_Commit body._
