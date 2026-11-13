# SS-12d — The cobra in the Sunscar: data, roster from stage 16

**Size:** M · **Depends on:** SS-12a, SS-12b, SS-12c · **Branch:** `ss-12d-cobra-data`
**Design refs:** GD §8.12, §8.2, §12.1, §12.3

## Goal

`enemy.cobra` is authored, catalogued and in the Sunscar roster from stage 16. Stages 1–15 stay as built.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Cobra.asset` | — | the archetype |
| `Data/Modes/Sunscar.asset` | — | roster entry |
| `Tests/Game/Content/SunscarTests.cs` | Tests.Game | rows replaced and added |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (Spit cone header, eleven fields, `SpitConeSpec` built only for `Cobra`); `Prefabs/Composition/BootScope.prefab` (catalog entry); `Data/Localisation/English.asset` + `Pseudo.asset` (`enemy.cobra.name`) |

## Behaviour

1. `Cobra.asset`: id `enemy.cobra`, name key `enemy.cobra.name` ("Spitting cobra"), 44 HP, 2.4 move speed, priority 4, threat 10, 28 XP, light, aggro 30, behaviour `Cobra`. Spit cone 8 / 4 / 0.7 / 30 / 5 / 8 / 0.02 / 4 / 3 / 0.5 / 3 (SS-12a's order). Body `Cobra.prefab` at scale 1.
2. An enemy that is not `Cobra` builds no `SpitConeSpec`. Every existing enemy asset is unchanged.
3. `Sunscar.asset` roster: the entries as built (scorpion 1, beetle 2, brood 4, viper 6, jackal 8, glassback 9, antlion 11, priest 14, all never retired), then `enemy.cobra`, introduced 16, retired 0. Boss roster and scripted stages stay empty (15, 20, 25, 30 are kept for later bosses).
4. Stages 1–15 spawn no cobra. From stage 16 the wave budget may buy them.
5. The Jungle's assets are untouched.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sunscar_Mode_RosterIs…PriestCobra` (replaces `…Priest`) | mode / read / rule 3's entries in stage order; boss roster, scripted stages empty |
| `Cobra_Asset_MatchesDesign` | catalog / spec / rule 1's numbers, `SpitCone` present, `Behaviour` Cobra |
| `Cobra_ExistingEnemies_NoSpitCone` | every other enemy spec / read / `SpitCone` null |
| `Cobra_SlowerThanEveryClass` | spec vs each character / read / move speed < class speed |
| `Cobra_ConeTipClearableInRear` | spec vs each class / read / speed × rear ≥ length − close distance |
| `Cobra_HoldsInsideTheStormsClearing` | spec / read / hold distance < 13 m |
| `Sunscar_RosterByStage_CobraFrom16` | mode / roster at 15, 16 / without; with cobra |
| `Sunscar_Stage16_WavesClear` | session at Sunscar 16, the hero firing, `sidestepTells`, `dodgeLandings` / tick ≤ 240 s / only stage-16 roster enemies and scorplings; one `StageCleared` |
| `Cobra_NameKey_InEveryLanguage` | English, Pseudo / look up / both carry it |

`Stage16_WavesClear` may pick its seed, recorded in *As built* (SS-11d's took 1). If no seed clears because the simulated Ranger dies at that depth, the row may keep the hero alive with a helper flag in `SunscarTests`, named in *As built*: the row tests that the waves clear, not the simulation's survival. Every other `SunscarTests` row stays green unchanged.

## Manual verification (Editor / device)

1. **[Editor]** DT-01, Sunscar stage 16: cobras hold about 8 m and back away when rushed.
2. **[Editor]** Standing still: a cobra slithers in, rears with the hood spread, a red-orange cone shows for 0.7 s, then it spits. Stepping back out of the tip, or rolling, takes nothing.
3. **[Editor]** Hit: the status row shows a pale-violet drop with a stack count; HP ticks down without the hero flinching. A second hit inside 4 s reads 2.
4. **[Editor]** A roll (Ranger), a charge (Oathbound) or a swap (Beastmaster) clears every stack at once.
5. **[Editor]** A rock between hero and cobra: the cone is cut at the rock and the spit misses behind it.
6. **[Editor]** Walking away: the cobra never catches up and never spits.
7. **[Editor]** The cone and spray read through the storm. Nothing on the cobra is cyan, gold or green.
8. **[device]** The cone reads at first look on a phone, and the violet drop reads as Poison, not as a buff.

## Out of scope

- Scripted stages, bosses, retirements. Elites and affixes for it.
- Any core or view change.

## As built

_Commit body._
