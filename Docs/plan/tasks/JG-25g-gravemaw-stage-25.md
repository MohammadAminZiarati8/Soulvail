# JG-25g — Stage 20 is Gravemaw's: data, ruins, roster, the script

> **Parked** (owner 2026-11-02): Gravemaw moves to stage 25; Thornheart takes 20. Revise before building: cut the ring-exit probe from 25b (a plain knockback stopped by walls), one move per window in phase 1, stage numbers and roster in 25g, and the JG-20x references.

**Size:** M · **Depends on:** JG-20f · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 5, 6, §9.2; AR §18.1; JG-15g's shape; M4-01b (a boss stage has no director waves)

## Goal

The Jungle's stage 20 spawns Gravemaw in a ruined arena; stages 5, 25, 30 keep the Croaker, 10 the
Mother, 15 the Warlord.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Gravemaw.asset` | — | **New.** `enemy.gravemaw` |
| `Data/Enemies/GravemawBoss.asset` | — | **New.** `boss.gravemaw` |
| `Prefabs/Arenas/Jungle/Cover_GravemawRuins.prefab` | — | **New.** Variant of `Cover_WarlordStones`, ruin layout |
| `Prefabs/Projectiles/GravemawRock.prefab` | — | **New.** `ProjectileView`, a lobbed rock |
| `Tests/Game/Authoring/GravemawStageTests.cs` | Tests.Game | **New** |
| *small edits* | | `Jungle.asset` boss roster row; `BootScope.prefab` catalog lists; English/Pseudo name rows; `JungleTests`; `GameDesign.md` §9.2 |

## Behaviour

1. **Enemy:** `Boss` kind; `MoveSpeed` 6; `ContactDamage` 20; `XpValue` 600; `ThreatCost` 40;
   `BodyScale` 3; body `Gravemaw.prefab`; projectile `GravemawRock`; `MarksLanding` on; cover
   `Cover_GravemawRuins`. Name = "Gravemaw".
2. **Boss:** phases 1 / 0.5, `BeatSeconds` 1.5, `SpawnsCentral` on, no phase summons (the cracks are
   its adds).
3. **Roster:** row `boss.gravemaw`, `_atStage` 20, ahead of the Croaker cycle.
4. **The script.** Stage 20 is a boss stage and stays out of `_scriptedStages`: no director waves
   (M4-01b), the one body stands up on the first tick. Its "waves" are the fight's: phase 1 none;
   phase 2 two Frogs per surface from cracks (20d), cleared at the beat. **Door:** the standard
   `IsStageComplete` — boss down and nothing breathing; nothing here edits it.
5. **Ruins:** four pillars, layer Cover, colliders ≥ 1.2 m radius, 5.5–7 m from the centre at uneven
   angles, so some thrown exits are blocked and the probe matters; none within 3 m of the centre.
6. **Fight length:** `Jungle_Gravemaw_FightIsInTheBand` — Mother's formula at stage 20 with 33 DPS ×
   0.5 uptime (a 4 s window in an ~8 s cycle), plus one beat; `MaxHp` set to land ~95 s inside
   75–120. The number goes in *As built*.
7. **GD §9.2:** row 20 becomes Gravemaw (the Jungle); the Archon moves to the next place's stage 20.

## Tests

| Test | Given / When / Then |
|---|---|
| `Jungle_Gravemaw_HasItsNumbers` (1) / `Jungle_GravemawBoss_HasItsPhases` (2) |
| `Jungle_FifthStages_HaveTheirBosses` — 20 is Gravemaw (3) |
| `Jungle_StageTwenty_IsNotScripted` / `Jungle_StageTwenty_BossStageHasNoWaves` (4) |
| `StageTwenty_DoorOpens_WhenBossAndAddsAreDown` — session (4) |
| `Jungle_GravemawRuins_AreCoverAndRingTheCentre` (5) |
| `Jungle_Gravemaw_FightIsInTheBand` / `Jungle_StageTwenty_StartsWithGravemaw` — DT-01 start (6) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 stage 20: walk straight — the ring lands ahead; stop or turn — it misses.
   Stand in it: thrown out; stand beside a pillar: thrown the other way.
2. **[Editor]** Roll the tremor. Shoot only in the window. Phase 2: two rings, Frogs from cracks.

## Out of scope

- Balance beyond rule 6; a second arena; the Archon.

## As built

_Filled at merge._
