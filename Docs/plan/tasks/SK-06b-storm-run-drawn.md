# SK-06b — Storm Run drawn and authored

**Size:** M · **Depends on:** SK-06a, SK-04b · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Storm Run); colour law

## Goal

The Skirmisher's Lv-20 active is in the game: a cyan wind wraps the Ranger while Storm Run lasts,
and every roll bursts into arrows.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Authoring/StormRunDefinition.cs` | Game | **New.** Effect definition |
| `Game/Views/StormRunView.cs` | Game | **New.** Aura on `StormRunChanged`; a burst ring at each storm volley |
| `Prefabs/Vfx/VFX_StormRun.prefab` | prefab | **New.** Wind streaks around the body + a burst ring |
| `Data/Skills/Skirmisher/StormRun{,2,3}.asset` | assets | **New.** + their effect assets |
| `Tests/Game/Views/StormRunViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `Data/Paths/Skirmisher.asset` (inserted last, unlock 20), `English`/`Pseudo`, `SkillIconBook`, `StatusIconBook` (`status.storm-run`), `SkirmisherPathTests` |

## Behaviour

1. **Assets:** `skill.skirmisher.storm-run`, `-2`, `-3`, SK-06a rule 8 exactly; rank 1 `Active`,
   trigger `EnemiesInAcquireRange ≥ 5`, cooldown 30.
2. **Aura:** while active, pale cyan wind streaks circle the Ranger's body (built-in particles,
   no new shaders), and the roll leaves a short streak trail. It ends with `StormRunChanged(false)`.
3. **Burst:** when a roll ends during Storm Run, a quick cyan ring expands at the Ranger's feet
   (0.2 s) as the volley arrows leave; the arrows use the existing volley look.
4. The path's skills now read in CH §3.5's order: Running Shot, Ricochet, Hit and Run, Second
   Wind, Whirlwind, Storm Run — 20 ranks in all.
5. Pooled; ends on a stage boundary or death.
6. **Loc:** *"Storm Run — For 6 s every roll ends in a volley and the roll recharges twice as
   fast."*; ranks *"Lasts 8 s."*, *"Storm volley +30% damage."*

## Tests

| Test | Given / When / Then |
|---|---|
| `Assets_MatchTheRankTable` (1) |
| `View_AuraFollowsState` (2) |
| `View_BurstOnRollEnd_OnlyWhileActive` (3) |
| `Path_SixSkills_TwentyRanks_InOrder` (4) |
| `View_Pooled_ClearsOnBoundary` (5) |
| `Loc_Keys` (6) |

## Manual verification (Editor / device)

1. **[Editor]** Stage 21+, Skirmisher at level 20 with Storm Run: on five enemies the wind wraps
   the Ranger; each roll bursts arrows at the enemies in range and the roll button refills fast.
   After 6 s the wind fades.

## Out of scope

- Final icon art; sound.

## As built

_Filled at merge._
