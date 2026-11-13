# SS-14d — The vulture in the Sunscar: data, roster from stage 22

**Size:** M · **Depends on:** SS-14a, SS-14b, SS-14c, SS-13d · **Branch:** `ss-14d-vulture-data`
**Design refs:** GD §8.14, §8.2, §12.1, §12.3

## Goal

`enemy.vulture` is authored, catalogued and in the Sunscar roster from stage 22. Stages 1–21 stay as built. The Sunscar holds its eleven enemies.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Vulture.asset` | — | the archetype |
| `Data/Modes/Sunscar.asset` | — | roster entry |
| `Tests/Game/Content/SunscarTests.cs` | Tests.Game | rows replaced and added |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (Dive header, twelve fields, `DiveSpec` built only for `Vulture`); `Prefabs/Composition/BootScope.prefab` (catalog entry); `Data/Localisation/English.asset` + `Pseudo.asset` (`enemy.vulture.name`) |

## Behaviour

1. `Vulture.asset`: id `enemy.vulture`, name key `enemy.vulture.name` ("Carrion vulture"), 50 HP, 4 move speed (its circle speed), priority 6, threat 14, 31 XP, light, aggro 30, behaviour `Vulture`. Dive 7 / 4 / 6 / 4 / 6 / 1 / 10 / 0.6 / 0.4 / 14 / 2 / 0.6 (SS-14a's order). Body `Vulture.prefab` at scale 1.
2. An enemy that is not `Vulture` builds no `DiveSpec`. Every existing enemy asset is unchanged.
3. `Sunscar.asset` roster: the entries as built (scorpion 1, beetle 2, brood 4, viper 6, jackal 8, glassback 9, antlion 11, priest 14, cobra 16, storm caller 18, all never retired), then `enemy.vulture`, introduced 22, retired 0. Eleven entries. Boss roster and scripted stages stay empty (20 is kept for a later boss).
4. Stages 1–21 spawn no vulture. From stage 22 the wave budget may buy them.
5. The Jungle's assets are untouched.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sunscar_Mode_RosterIs…CallerVulture` (replaces `…Caller`) | mode / read / rule 3's eleven entries in stage order; boss roster, scripted stages empty |
| `Vulture_Asset_MatchesDesign` | catalog / spec / rule 1's numbers, `Dive` present, `Behaviour` Vulture |
| `Vulture_ExistingEnemies_NoDive` | every other enemy spec / read / `Dive` null |
| `Vulture_LineSidestepInLock` | spec vs each class / read / speed × lock ≥ 2 × half width |
| `Vulture_LineOverrunsTheHero` | spec / read / line length > circle radius |
| `Vulture_CirclesInsideTheStormsClearing` | spec / read / circle radius < 13 m |
| `Sunscar_RosterByStage_VultureFrom22` | mode / roster at 21, 22 / without; with vulture |
| `Sunscar_Stage22_WavesClear` | session at Sunscar 22, the hero firing, `sidestepTells`, `dodgeLandings` / tick ≤ 300 s / only stage-22 roster enemies and scorplings; one `StageCleared` |
| `Vulture_NameKey_InEveryLanguage` | English, Pseudo / look up / both carry it |

`Stage22_WavesClear` may pick its seed, recorded in *As built*. If no seed clears because the simulated Ranger dies at that depth, the row may keep the hero alive with the helper flag SS-12d or SS-13d added (or add it), named in *As built*. Every other `SunscarTests` row stays green unchanged.

## Manual verification (Editor / device)

1. **[Editor]** DT-01, Sunscar stage 22: a vulture climbs away at once; then only its shadow circles the hero at about 7 m.
2. **[Editor]** Aloft it is never auto-aimed, and shots, stuns and poison never touch it.
3. **[Editor]** Every 4–6 s a red-orange line runs from the shadow through the hero for 1 s. Standing still: the dive hits once. One step sideways: it misses.
4. **[Editor]** It lands at the line's end, never in a rock, and stands 2 s: it can be shot and stunned, then climbs and is gone.
5. **[Editor]** Two vultures do not dive together. Pets ignore it aloft and bite it on the ground.
6. **[Editor]** The shadow and line read through the storm. It is clearly bigger and darker than the hawk pet. Nothing on it is cyan or gold.
7. **[device]** The shadow and line read at first look on a phone, and the line is seen in time to step.

## Out of scope

- Scripted stages, bosses, retirements, the Sunscar's cousins. Elites and affixes for it.
- Any core or view change.

## As built

_Commit body._
