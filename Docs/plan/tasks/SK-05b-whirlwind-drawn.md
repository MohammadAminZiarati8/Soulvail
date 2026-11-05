# SK-05b — Whirlwind drawn and authored

**Size:** M · **Depends on:** SK-05a, SK-02 · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Whirlwind); colour law

## Goal

The Skirmisher's Lv-17 active is in the game: the Ranger spins on the run and rings of arrows fan
out all round it.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Authoring/WhirlwindDefinition.cs` | Game | **New.** Effect definition |
| `Game/Views/WhirlwindView.cs` | Game | **New.** On `WhirlwindCast`: a swirl ring per ring fired |
| `Prefabs/Vfx/VFX_Whirlwind.prefab` | prefab | **New.** Flat cyan swirl ring, 0.25 s |
| `Data/Skills/Skirmisher/Whirlwind{,2,3}.asset` | assets | **New.** + their effect assets |
| `Tests/Game/Views/WhirlwindViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `Data/Paths/Skirmisher.asset` (inserted after Second Wind, unlock 17), `RangerAnimatorView` + `AC_Ranger.controller` (spin), `English`/`Pseudo`, `SkillIconBook`, `SkirmisherPathTests` |

## Behaviour

1. **Assets:** `skill.skirmisher.whirlwind`, `-2`, `-3`, SK-05a rule 7 exactly; rank 1 `Active`,
   trigger `EnemiesWithin6m ≥ 3`, cooldown 10.
2. **Spin:** each ring plays a fast 360° turn of the upper body (SK-02's layer); the legs keep
   running. Use the rig's spin clip if one exists, else rotate the spine through 360° over 0.25 s.
3. **Swirl:** each ring shows a flat cyan swirl at the player's feet, about 1.5 m across, for
   0.25 s. Its arrows are ordinary arrows.
4. Pooled; nothing instantiated per cast after warm-up; ends on a stage boundary.
5. **Loc:** *"Whirlwind — Spin and loose two rings of 12 arrows all round."*; ranks *"16 arrows a
   ring."*, *"Three rings. Cooldown 8 s."*

## Tests

| Test | Given / When / Then |
|---|---|
| `Assets_MatchTheRankTable` / `Path_WhirlwindAtSeventeen` (1) |
| `View_OneSwirlPerRing_AtThePlayer` (3) |
| `Animator_SpinOnUpperLayer_LegsKeepRunning` (2) |
| `View_Pooled_ClearsOnBoundary` (4) |
| `Loc_Keys` (5) |

## Manual verification (Editor / device)

1. **[Editor]** Skirmisher, Whirlwind taken, three enemies close: the Ranger spins twice while
   running, two rings of arrows fan out, and every enemy around takes hits.

## Out of scope

- Final icon art; sound.

## As built

_Filled at merge._
