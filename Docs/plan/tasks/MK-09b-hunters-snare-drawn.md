# MK-09b — Hunter's Snare drawn and authored

**Size:** M · **Depends on:** MK-09a, MK-06b, MK-02b · **Branch:** `dev` (direct)
**Design refs:** CH §3.5; art ruling (KayKit + generated on KayKit rigs; props are props)

## Goal

The snare is a visible object: the Ranger drops it with a quick crouch, it sits armed with a faint
cyan glow, and snaps shut on the enemy's legs.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Views/TrapViews.cs` | Game | **New.** On trap events: place, arm, snap, expire |
| `Prefabs/VFX/HunterSnare.prefab` | prefab | **New.** Rope loop + three stakes, from KayKit props if one fits, else a small mesh on the player palette |
| `Data/Skills/Marksman/HuntersSnare{,2,3}.asset` | assets | **New.** + effect assets |
| `Tests/Game/Views/TrapViewsTests.cs` | Tests.Game | **New** |
| *small edits* | | `RangerAnimatorView` + Ranger controller (a place trigger: a short crouch-and-place from the rig's clips, upper-body or full, ≤ 0.4 s, never blocking movement), `Data/Paths/Marksman.asset` (inserted **fifth**, unlock 17), `English`/`Pseudo`, `SkillIconBook`, `MarksmanPathTests` |

## Behaviour

1. **Place:** on `TrapPlaced` the snare drops at the point with a small dust puff; the Ranger plays
   the place clip.
2. **Armed** (after 0.3 s): a faint cyan ring pulses slowly on the loop so the player can find it on
   the dark floor; enemies get no warning.
3. **Snap:** on `TrapSprung` the loop jerks up and closes around the enemy's feet (0.15 s), then the
   prop is hidden — MK-06b's root coils carry the rest.
4. **Expire:** on `TrapExpired` it sinks and fades over 0.3 s.
5. **Assets:** `skill.marksman.hunters-snare`, `-2`, `-3`, MK-09a rule 6 exactly; trigger
   `NearestEnemyDistance < 3`.
6. Loc: *"Hunter's Snare — When an enemy gets close, drop a trap. The first to step on it is rooted
   and hurt."* and one line per rank.
7. Pooled; all gone on `StageArrived`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Place_ShowsAtPoint` / `Armed_Pulses` (1, 2) |
| `Snap_ClosesThenHides` / `Expire_Fades` (3, 4) |
| `Assets_MatchRankTable` / `Path_SnareIsFifthAtSeventeen` (5) |
| `Loc_Keys` (6) / `Pooled_ClearedOnStage` (7) |

## Manual verification (Editor / device)

1. **[Editor]** Level 17, let a Frog hop close: the Ranger crouches, a snare lies glowing; the Frog
   steps on it, the loop snaps, the Frog is rooted with coils and loses health.

## Out of scope

- Generated art for the snare beyond a KayKit/primitive build — the owner may replace it later.

## As built

_Filled at merge._
