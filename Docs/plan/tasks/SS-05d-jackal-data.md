# SS-05d — The slinger in the Sunscar: data, roster from stage 2

**Size:** M · **Depends on:** SS-05a, SS-05b, SS-05c · **Branch:** `ss-05d-jackal-data`
**Design refs:** GD §8.5, §8.2, §12.1, §12.3

## Goal

`enemy.jackal` is authored, catalogued and in the Sunscar roster from stage 2, so stage 2 is the first stage with a thrower and stage 1 stays scorpions only.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Jackal.asset` | — | the archetype |
| `Data/Modes/Sunscar.asset` | — | roster entry |
| `Tests/Game/Content/SunscarTests.cs` | Tests.Game | rows replaced and added |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (Slinger header, three fields, `SlingerSpec` built when max lead > 0); `Prefabs/Composition/BootScope.prefab` (catalog entry); `Data/Localisation/English.asset` + `Pseudo.asset` (`enemy.jackal.name`) |

## Behaviour

1. `Jackal.asset`: id `enemy.jackal`, name key `enemy.jackal.name` ("Jackal Slinger"), 28 HP, 2.8 move speed, priority 3, threat 7, 21 XP, light, 12 damage, reach 1.2, wind-up 0.7, recover 2.3, aggro 30, Spitter; projectile standoff 12, speed 7, radius 1.5, count 1; marks landing; body `Jackal.prefab`, projectile `SlingStone.prefab`; Slinger 3 / 0.7 / 3.
2. A max lead of 0 builds no `SlingerSpec`; every existing enemy asset is unchanged.
3. `Sunscar.asset` roster: `enemy.scorpion` 1–never as built, then `enemy.jackal`, introduced 2, retired 0 (never). Boss roster and scripted stages stay empty.
4. Stage 1 still spawns scorpions only; from stage 2 the wave budget may buy jackals.
5. The Jungle's assets and `arena.ashen-reach.courtyard` are untouched.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sunscar_Mode_RosterIsScorpionThenJackal` (replaces `Sunscar_Mode_RosterIsTheScorpion`) | mode / read / [scorpion 1–never, jackal 2–never]; boss roster, scripted stages empty |
| `Jackal_Asset_MatchesDesign` | catalog / spec / rule 1's numbers, `Slinger` present, `Behaviour` Spitter, marks landing |
| `Jackal_ExistingEnemies_NoSlinger` | every other enemy spec / read / `Slinger` null |
| `Jackal_Standoff_InsideTheStormsClearing` | spec + `SunscarSandstormView` / read / standoff < `ClearRadius` |
| `Sunscar_Stage1_WavesClearsOpensPortal` | as built / tick / only scorpions spawned (row tightened) |
| `Sunscar_RosterByStage_JackalFrom2` | mode / roster at stages 1, 2 / [scorpion]; [scorpion, jackal] |
| `Sunscar_Stage2_WavesClear` | session at Sunscar 2, the hero firing, sightings fed as stage 1's row does / tick ≤ 180 s / only scorpions and jackals spawned, one `StageCleared` |
| `Jackal_NameKey_InEveryLanguage` | English, Pseudo / look up / both carry it |

`Courtyard_Untouched` and every other `SunscarTests` row stay green unchanged.

## Manual verification (Editor / device)

1. **[Editor]** DT-01, Sunscar stage 2: jackals walk in to about 12 m and plant, in clear air through the storm.
2. **[Editor]** Each wind-up whirls a glowing stone; on release a red-orange circle lands up to 3 m ahead of a running hero, under a still one.
3. **[Editor]** Turning or stopping clears the circle; running straight is hit.
4. **[Editor]** Rushing inside ~8 m sends it walking backwards with no throw for about 3 s; it is caught and killed.
5. **[Editor]** The circle reads over quicksand and through the storm; nothing on the jackal but the stone is warm.
6. **[device]** Circle and stone read at first look on a phone.

## Out of scope

- Scripted Sunscar stages, bosses, later enemies, retirements.
- Elites and affixes for it; any core or view change.
- Wave composition or budget (GD §12 as built).

## As built

_Commit body._
