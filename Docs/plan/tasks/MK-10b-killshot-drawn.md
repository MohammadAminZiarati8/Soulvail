# MK-10b — Killshot drawn and authored

**Size:** M · **Depends on:** MK-10a, MK-02b · **Branch:** `dev` (direct)
**Design refs:** CH §3.5; JG-06b (`CameraShake`); colour law

## Goal

Killshot is the Marksman's big moment: a kneel and a glowing draw, then a bright streak across the
arena, a camera jolt, and a burst on every enemy it kills outright.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Views/KillshotView.cs` | Game | **New.** Wind-up glow + aim line, the streak, hit sparks, execute bursts |
| `Prefabs/VFX/Killshot.prefab` | prefab | **New.** Charge light, streak (trail/line), spark and burst systems |
| `Data/Skills/Marksman/Killshot{,2,3}.asset` | assets | **New.** + effect assets |
| `Tests/Game/Views/KillshotViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `RangerAnimatorView` + Ranger controller (a Kneel state: a kneeling/crouched bow-draw from the rig's clips — a crouch pose with the draw layered on if no kneel-and-aim clip exists — held for the wind-up, then release), `CameraShake` (a stronger preset), `Data/Paths/Marksman.asset` (inserted **sixth**, unlock 20), `English`/`Pseudo`, `SkillIconBook`, `MarksmanPathTests` |

## Behaviour

1. **Wind-up** (on `KillshotWindUp`, 0.7 s): the Ranger kneels and draws, facing the locked
   direction; a cyan-white light grows at the arrow tip; a thin, faint aim line shows the path
   across the arena.
2. **Fire** (on `KillshotFired`): release; a thick cyan-white streak from the bow along the whole
   line, bright for 0.1 s then fading over 0.4 s; camera jolt (stronger than the Croaker's slam,
   shorter); a small spark on each enemy hit.
3. **Execute** (on `KillshotExecuted`): a bright burst on that enemy as it dies.
4. The Ranger stands back up in ≤ 0.25 s after the release; movement returns when core releases the
   hold.
5. **Assets:** `skill.marksman.killshot`, `-2`, `-3`, MK-10a rule 7 exactly.
6. Loc: *"Killshot — Kneel and loose one arrow across the whole arena. It weakens through each enemy
   and kills the badly wounded outright."* and one line per rank.
7. Pooled; a death or stage boundary during the wind-up ends glow and pose cleanly.

## Tests

| Test | Given / When / Then |
|---|---|
| `WindUp_KneelsAndGlows_FacesLine` (1) |
| `Fire_StreakSpansLength_ThenFades` / `Fire_JoltsCamera` (2) |
| `Execute_Bursts` (3) |
| `Assets_MatchRankTable` / `Path_KillshotIsSixthAtTwenty` (5) |
| `Loc_Keys` (6) / `Cancel_EndsCleanly` (7) |

## Manual verification (Editor / device)

1. **[Editor]** Level 20 on a crowded late stage: the Ranger kneels, the aim line shows, the streak
   crosses the arena, weak enemies burst, the camera jolts. It reads as the best skill in the kit.

## Out of scope

- Slow-motion on the release (an idea for the owner's playtest, not built).

## As built

_Filled at merge._
