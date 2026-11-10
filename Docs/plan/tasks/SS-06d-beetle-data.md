# SS-06d — The beetle in the Sunscar: data, roster from stage 2

**Size:** M · **Depends on:** SS-06a, SS-06b, SS-06c · **Branch:** `ss-06d-beetle-data`
**Design refs:** GD §8.6, §8.2, §12.1, §12.3

## Goal

`enemy.beetle` is authored, catalogued and in the Sunscar roster from stage 2, so stage 2 is the first stage with a thrower and stage 1 stays scorpions only.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Beetle.asset` | — | the archetype |
| `Data/Modes/Sunscar.asset` | — | roster entry |
| `Tests/Game/Content/SunscarTests.cs` | Tests.Game | rows replaced and added |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (Bombardier header, three fields, `BombardierSpec` built when turn rate > 0); `Prefabs/Composition/BootScope.prefab` (catalog entry); `Data/Localisation/English.asset` + `Pseudo.asset` (`enemy.beetle.name`) |

## Behaviour

1. `Beetle.asset`: id `enemy.beetle`, name key `enemy.beetle.name` ("Bombardier Beetle"), 28 HP, 2.8 move speed, priority 3, threat 7, 21 XP, light, 12 damage, reach 1.2, wind-up 0.7, recover 2.3, aggro 30, Spitter; projectile standoff 12, speed 6, radius 1.6, count 1; marks landing; body `Beetle.prefab`, projectile `BeetleGlob.prefab`; Bombardier 45 / 10 / 20.
2. A turn rate of 0 builds no `BombardierSpec`; every existing enemy asset is unchanged.
3. `Sunscar.asset` roster: `enemy.scorpion` 1–never as built, then `enemy.beetle`, introduced 2, retired 0 (never). Boss roster and scripted stages stay empty.
4. Stage 1 still spawns scorpions only; from stage 2 the wave budget may buy beetles.
5. The Jungle's assets and `arena.ashen-reach.courtyard` are untouched.

## Tests

| Test | Given / When / Then |
|---|---|
| `Sunscar_Mode_RosterIsScorpionThenBeetle` (replaces `Sunscar_Mode_RosterIsTheScorpion`) | mode / read / [scorpion 1–never, beetle 2–never]; boss roster, scripted stages empty |
| `Beetle_Asset_MatchesDesign` | catalog / spec / rule 1's numbers, `Bombardier` present, `Slinger` null, Spitter, marks landing |
| `Beetle_ExistingEnemies_NoBombardier` | every other enemy spec / read / `Bombardier` null |
| `Beetle_Standoff_InsideTheStormsClearing` | spec + `SunscarSandstormView` / read / standoff < `ClearRadius` |
| `Sunscar_Stage1_WavesClearsOpensPortal` | as built / tick / only scorpions spawned (row tightened) |
| `Sunscar_RosterByStage_BeetleFrom2` | mode / roster at stages 1, 2 / [scorpion]; [scorpion, beetle] |
| `Sunscar_Stage2_WavesClear` | session at Sunscar 2, the hero firing, sightings fed as stage 1's row does / tick ≤ 180 s / only scorpions and beetles spawned, one `StageCleared` |
| `Beetle_NameKey_InEveryLanguage` | English, Pseudo / look up / both carry it |

`Courtyard_Untouched` and every other `SunscarTests` row stay green unchanged.

## Manual verification (Editor / device)

1. **[Editor]** DT-01, Sunscar stage 2: beetles walk in to about 12 m, plant in clear air and swing their rears round slowly.
2. **[Editor]** Each wind-up lights the vent; a red-orange glob lobs at where the hero stood, under a red-orange ring.
3. **[Editor]** Standing still is hit; any step clears the ring.
4. **[Editor]** Closing to ~5 m and circling: the vent lights and goes out with no glob, again and again.
5. **[Editor]** Vent, glob and ring read over quicksand and through the storm; nothing else on the beetle is warm.
6. **[device]** Vent, glob and ring read at first look on a phone.

## Out of scope

- Scripted Sunscar stages, bosses, the jackal (SS-05d, stage 8), retirements.
- Elites and affixes for it; any core or view change.
- Wave composition or budget (GD §12 as built).

## As built

_Commit body._
