# SS-08d — The viper in the Sunscar: data, roster from stage 6

**Size:** M · **Depends on:** SS-08a, SS-08b, SS-08c · **Branch:** `ss-08d-viper-data`
**Design refs:** GD §8.8, §8.2, §12.1, §12.3

## Goal

`enemy.viper` is authored, catalogued and in the Sunscar roster from stage 6, so stages 1–5 stay as built and stage 6 adds the coiled strike.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Viper.asset` | — | the archetype |
| `Data/Modes/Sunscar.asset` | — | roster entry |
| `Tests/Game/Content/SunscarTests.cs` | Tests.Game | rows replaced and added |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (Sidewinder header, five fields, `SidewinderSpec` built when miss recover > 0); `Prefabs/Composition/BootScope.prefab` (catalog entry); `Data/Localisation/English.asset` + `Pseudo.asset` (`enemy.viper.name`) |

## Behaviour

1. `Viper.asset`: id `enemy.viper`, name key `enemy.viper.name` ("Sidewinder Viper"), 44 HP, 2.4 move speed, priority 4, threat 9, 27 XP, light, 14 damage, reach 1.2, wind-up 0.9, recover 1, aggro 30, Lunger; lunge range 8, length 12, speed 30, half-width 0.6, cooldown 0; no pack; sidewinder 2 / 2.8 / 45 / 7 / 2.5; body `Viper.prefab`.
2. A miss recover of 0 builds no `SidewinderSpec`; every existing enemy asset is unchanged.
3. `Sunscar.asset` roster: the entries as built (scorpion 1, beetle 2, brood 4, all never retired), then `enemy.viper`, introduced 6, retired 0 (never). Boss roster and scripted stages stay empty.
4. Stages 1–5 spawn no viper; from stage 6 the wave budget may buy them.
5. **Meeting SS-05d.** The roster is ordered by introduction stage: scorpion 1, beetle 2, brood 4, viper 6, jackal 8. Whichever of SS-08d and SS-05d lands second inserts its entry in that order and its roster test names every entry then present; the code wins over either spec (ROADMAP's SS-07 note).
6. The Jungle's assets and `arena.ashen-reach.courtyard` are untouched.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sunscar_Mode_RosterIsScorpionBeetleBroodViper` (replaces `Sunscar_Mode_RosterIsScorpionBeetleBrood`; + jackal 8 if SS-05d landed) | mode / read / rule 3's entries in rule 5's order; boss roster, scripted stages empty |
| `Viper_Asset_MatchesDesign` | catalog / spec / rule 1's numbers, `Sidewinder` present, `Pack` null, `Behaviour` Lunger |
| `Viper_ExistingEnemies_NoSidewinder` | every other enemy spec / read / `Sidewinder` null |
| `Viper_Strike_InsideTheStormsClearing` | spec + `SunscarSandstormView` / read / `Lunge.Range` < `ClearRadius` |
| `Viper_SidewindSlowerThanEveryClass` | spec vs each character / read / sidewind and move speed < class speed |
| `Sunscar_RosterByStage_ViperFrom6` | mode / roster at 5, 6 / [scorpion, beetle, brood]; + viper |
| `Sunscar_Stage6_WavesClear` | session at Sunscar 6, the hero firing, sightings fed as stage 4's row / tick ≤ 240 s / only stage-6 roster enemies and scorplings; one `StageCleared` |
| `Viper_NameKey_InEveryLanguage` | English, Pseudo / look up / both carry it |

Every other `SunscarTests` row stays green unchanged.

## Manual verification (Editor / device)

1. **[Editor]** DT-01, Sunscar stage 6: vipers slither in and stop about 8 m out, in clear air through the storm.
2. **[Editor]** Each coil tightens the body into an S while a red-orange strip runs 12 m through the hero; one step sideways clears it, backing away does not.
3. **[Editor]** A miss leaves it lying straight about 2 s; a hit recoils in about 1 s.
4. **[Editor]** Between strikes it slides sideways round the hero; the next strip comes from a new angle, alternating sides.
5. **[Editor]** The strip reads over quicksand and through the storm; nothing on the viper is warm or cyan.
6. **[device]** Coil and strip read at first look on a phone.

## Out of scope

- The jackal (SS-05d, stage 8), scripted stages, bosses, retirements.
- Elites and affixes for it; any core or view change; wave budget (GD §12 as built).

## As built

_Commit body._
