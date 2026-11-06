# EA-04b — Crossfire drawn and authored

**Size:** M · **Depends on:** EA-02, EA-04a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Crossfire); GD §16.4 (colour law); SK-03b (the drawn shape)

## Goal

The Echo Archer's Lv-17 active is in the game: heavy smoky-cyan arrows converge on one enemy from
every shade.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Authoring/CrossfireDefinition.cs` | Game | **New.** Effect definition |
| `Game/Views/CrossfireView.cs` | Game | **New.** Aim lines and the heavy arrow on `Origin = Crossfire` |
| `Materials/M_CrossfireLine.mat` | material | **New.** Thin smoky-cyan line |
| `Data/Skills/EchoArcher/Crossfire{,2,3}.asset` | assets | **New.** + their effect assets |
| `Tests/Game/Views/CrossfireViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `Data/Paths/EchoArcher.asset` (fifth, unlock 17), `ProjectileView` (2× scale for `Crossfire`), `RunScope`, `English`/`Pseudo`, `SkillIconBook`, `EchoArcherPathTests` |

## Behaviour

1. **Assets:** `skill.echo-archer.crossfire`, `-2`, `-3`, EA-04a rule 7 exactly; rank 1 `Active`,
   trigger `EnemiesInAcquireRange ≥ 1`, cooldown 12.
2. **Aim lines:** for each `ProjectileFired` with `Origin = Crossfire`, a thin line from its origin
   to its target shows for 0.15 s, so the lines visibly meet on one enemy.
3. **Arrow:** a Crossfire arrow is drawn at 2× the ordinary arrow with a longer smoky-cyan trail.
4. **Cast:** the Ranger's quick release on the upper-body layer; every shade plays its release on
   `ShadeLoosed` (EA-02).
5. Pooled; nothing instantiated per cast after warm-up; lines end on a stage boundary.
6. **Loc:** *"Crossfire — Two shades appear, and every shade looses a heavy arrow at your target:
   1.5× weapon damage each."*; ranks *"Three shades appear."*, *"2× damage. Cooldown 9 s."*

## Tests

| Test | Given / When / Then |
|---|---|
| `Assets_MatchTheRankTable` / `Path_CrossfireFifthAtSeventeen` (1) |
| `Line_PerCrossfireArrow_EndsAfterPointOneFive` / `Line_NotForOtherOrigins` (2) |
| `CrossfireArrow_DoubleScale` (3) |
| `Cast_UpperBodyRelease_MovementKept` (4) |
| `Pool_NoInstantiate_ClearsOnBoundary` (5) |
| `Loc_Keys` (6) |

## Manual verification (Editor / device)

1. **[Editor]** Stage 18, Echo Archer with Mirror Image and Crossfire: two shades appear and
   heavy arrows from all of them meet on the target from different sides; the two new shades fade
   after a second.

## Out of scope

- Sound; final icon art.

## As built

_Filled at merge._
