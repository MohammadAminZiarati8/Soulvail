# SK-03b — Ricochet Shot drawn and authored

**Size:** M · **Depends on:** SK-03a, SK-01b · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Ricochet Shot); RS-07/RS-08c (the arrow look); colour law

## Goal

The Skirmisher's Lv-11 active is in the game: a bright arrow snaps from enemy to enemy with a spark
at each bounce.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Authoring/RicochetShotDefinition.cs` | Game | **New.** Effect definition (`RainOfArrowsDefinition`'s shape) |
| `Game/Views/RicochetShotView.cs` | Game | **New.** Bounce sparks; brighter trail on chain arrows |
| `Prefabs/Vfx/VFX_RicochetSpark.prefab` | prefab | **New.** Small cyan-white spark burst |
| `Data/Skills/Skirmisher/RicochetShot{,2,3,4}.asset` | assets | **New.** + their effect assets |
| `Tests/Game/Views/RicochetShotViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `Data/Paths/Skirmisher.asset` (inserted **second**, unlock 11), `RangerAnimatorView` (quick release), `English`/`Pseudo`, `SkillIconBook`, `SkirmisherPathTests` |

## Behaviour

1. **Assets:** `skill.skirmisher.ricochet-shot`, `-2`…`-4`, SK-03a rule 8 exactly; rank 1
   `Active`, trigger `EnemiesInAcquireRange ≥ 3`, cooldown 8.
2. **Cast:** the Ranger's normal release, played on the upper-body layer when moving (SK-02); it
   never blocks movement.
3. **Arrow:** a chain arrow is the ordinary arrow with a brighter, longer cyan-white trail, so it
   reads apart from basic shots at a glance.
4. **Bounce:** on each `ProjectileFired` with `ChainIndex` > 0, a spark plays at the arrow's start
   point (the previous impact); the last hit plays the ordinary impact.
5. Pooled; nothing instantiated per cast after warm-up; sparks end on a stage boundary.
6. **Loc:** *"Ricochet Shot — An arrow that bounces between 3 enemies within 5 m."*; ranks
   *"Bounces between 4 enemies."*, *"+25% damage."*, *"Bounces between 5. Cooldown 6 s."*

## Tests

| Test | Given / When / Then |
|---|---|
| `Assets_MatchTheRankTable` / `Path_RicochetIsSecondAtEleven` (1) |
| `View_ChainArrow_UsesBrightTrail` / `View_BasicArrow_Unchanged` (3) |
| `View_SparkAtEachBounce_NotAtFirst` (4) |
| `View_Pooled_ClearsOnBoundary` (5) |
| `Loc_Keys` (6) |

## Manual verification (Editor / device)

1. **[Editor]** Stage 12, Skirmisher, Ricochet taken: on a group of ≥ 3 a bright arrow hits one
   enemy and snaps to two more, sparking at each bounce; an enemy 6 m away from the group is skipped.

## Out of scope

- Final icon art; sound.

## As built

_Filled at merge._
