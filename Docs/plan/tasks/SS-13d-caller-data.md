# SS-13d — The storm caller in the Sunscar: data, roster from stage 18

**Size:** M · **Depends on:** SS-13a, SS-13b, SS-13c, SS-12d · **Branch:** `ss-13d-caller-data`
**Design refs:** GD §8.13, §8.2, §12.1, §12.3

## Goal

`enemy.storm-caller` is authored, catalogued and in the Sunscar roster from stage 18. Stages 1–17 stay as built.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/StormCaller.asset` | — | the archetype |
| `Data/Modes/Sunscar.asset` | — | roster entry |
| `Tests/Game/Content/SunscarTests.cs` | Tests.Game | rows replaced and added |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (Storm call header, seventeen fields, `StormCallSpec` built only for `StormCaller`); `Prefabs/Composition/BootScope.prefab` (catalog entry); `Data/Localisation/English.asset` + `Pseudo.asset` (`enemy.storm-caller.name`) |

## Behaviour

1. `StormCaller.asset`: id `enemy.storm-caller`, name key `enemy.storm-caller.name` ("Storm caller"), 55 HP, 2 move speed (the shaman's), priority 7, threat 15, 31 XP, not light, aggro 30, behaviour `StormCaller`. Storm call 12 / 6 / 1.2 / 1.4 / 16 / 2 / 3 / 6 / 2 / 1.6 / 60 / 8 / 1.5 / 10 / 3 / 1 / 0.4 (SS-13a's order). Body `StormCaller.prefab` at scale 1.
2. The drift speed is half the Ranger's authored base speed (3.2 → 1.6). An enemy that is not `StormCaller` builds no `StormCallSpec`. Every existing enemy asset is unchanged.
3. `Sunscar.asset` roster: the entries as built (scorpion 1, beetle 2, brood 4, viper 6, jackal 8, glassback 9, antlion 11, priest 14, cobra 16, all never retired), then `enemy.storm-caller`, introduced 18, retired 0. Boss roster and scripted stages stay empty.
4. Stages 1–17 spawn no caller. From stage 18 the wave budget may buy them.
5. The Jungle's assets are untouched.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sunscar_Mode_RosterIs…CobraCaller` (replaces `…Cobra`) | mode / read / rule 3's entries in stage order; boss roster, scripted stages empty |
| `Caller_Asset_MatchesDesign` | catalog / spec / rule 1's numbers, `StormCall` present, `Behaviour` StormCaller |
| `Caller_ExistingEnemies_NoStormCall` | every other enemy spec / read / `StormCall` null |
| `Caller_DriftIsHalfTheRanger` | spec vs `Ranger.asset` / read / drift = speed × 0.5 |
| `Caller_PullWeakerThanEveryClass` | spec vs each character / read / pull < class speed / 2 |
| `Caller_DriftSlowerThanEveryClass` | spec vs each character / read / drift < class speed |
| `Caller_HoldsInsideTheStormsClearing` | spec / read / hold < 13 m, flee < hold |
| `Caller_TwoSecondStaySurvivable` | spec at stage 18's depth scaling / 2 s × scaled damage / < a level-1 Ranger's 90 HP |
| `Sunscar_RosterByStage_CallerFrom18` | mode / roster at 17, 18 / without; with caller |
| `Sunscar_Stage18_WavesClear` | session at Sunscar 18, the hero firing, `sidestepTells`, `dodgeLandings` / tick ≤ 240 s / only stage-18 roster enemies and scorplings; one `StageCleared` |
| `Caller_NameKey_InEveryLanguage` | English, Pseudo / look up / both carry it |

`Stage18_WavesClear` may pick its seed, recorded in *As built*. If no seed clears because the simulated Ranger dies at that depth, the row may keep the hero alive with a helper flag in `SunscarTests` (SS-12d's, if it added one), named in *As built*. Every other `SunscarTests` row stays green unchanged.

## Manual verification (Editor / device)

1. **[Editor]** DT-01, Sunscar stage 18: callers hang back about 12 m and run when the hero closes inside 6 m.
2. **[Editor]** A caller plants, raises the staff, and sand spirals up for 2 s. One arrow into it scatters the spiral and no tornado rises.
3. **[Editor]** Left alone, a dark ochre funnel rises 2 m in front of it on a red-orange ring and drifts after the hero. Walking away outpaces it. The whole ring stays visible under the funnel.
4. **[Editor]** Standing in the ring: HP ticks down without a flinch; a gentle pull toward the centre; walking out is easy. A roll through it takes nothing.
5. **[Editor]** The funnel passes through rocks and enemies and never hurts or shoves an enemy or a pet. The auto-aim never turns to it.
6. **[Editor]** Never more than two funnels at once. Killing a caller collapses its funnel at once.
7. **[Editor]** The funnel reads as a hazard, not as the background sandstorm. Nothing on the caller is cyan or gold.
8. **[device]** The ring and funnel read at first look on a phone, sized to the arena.

## Out of scope

- Scripted stages, bosses, retirements. Elites and affixes for it.
- Any core or view change.

## As built

_Commit body._
