# M7-05r — The Pitcher in the showcase

**Size:** S · **Depends on:** M7-05o (the Pitcher's clips), M7-05k (the enemy showcase) · **Branch:** `m7-05r-the-pitcher-in-the-showcase`
**Design refs:** none; a review scene · **Ledger rows:** none

The owner's word of 2026-09-29, after the Pitcher's three tasks: *"add it to enemyshowcase scene
aswell."*

## Goal

`EnemyShowcase.unity` shows the Pitcher's five clips, one body each, looping beside the Frog's and
the Rootling's.

## Why this shape

- **M7-05k's shape, a third row.** One raw model per clip, each started in its state by
  `ShowcaseClip`, on a showcase controller whose one-shots replay after a hold. No code.
- **`AC_Pitcher_Showcase` is `AC_Frog_Showcase`** with `Hop` renamed `Shuffle`, `Leap` dropped and the
  Pitcher's clips, so the holds are the Frog's: Attack 40 % of its length, Hit 80 %, Death 120 %.
- **Behind the Rootlings, at the prefab's 1.1**, spaced 2.3 m as the Rootling's row is.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Animation/Controllers/AC_Pitcher_Showcase.controller` | — | **New.** One looping state per clip (rule 2) |
| *small edits* | | `Scenes/EnemyShowcase.unity` (+ the Pitcher row); `Tests/Game/Art/EnemyShowcaseTests.cs` (rule 1) |

## Behaviour

1. **Each Pitcher clip is shown once.** The Pitcher's row names each of `Pitcher.fbx`'s five clips,
   and each state of `AC_Pitcher_Showcase` plays the Pitcher clip of its own name.
2. **Nothing plays once and freezes**, and every body starts in a state of its controller: M7-05k's
   rules 2 and 3, whose rows now sweep the Pitcher's bodies too.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Showcase_ShowsEveryPitcherClip` | the scene and `Pitcher.fbx` / the Pitcher row's states / the five clips, each once, each state its own clip (rule 1) |
| *(unchanged)* `Showcase_EveryBodyStartsInAStateOfItsController`, `Showcase_EveryStateLoops` | every body and controller, the Pitcher's included / swept / pass (rule 2) |

## Manual verification (Editor / device)

1. **[Editor]** Open `Scenes/EnemyShowcase.unity` and press Play. *Expected:* behind the Frogs and
   the Rootlings, five Pitchers: Idle, Shuffle, Attack, Hit, Death. Each loops, and a one-shot pauses
   at its end before playing again.

## Out of scope

- The wind-up glow and the hit flash, which a run's `EnemyHitFeedback` drives (M7-05k).

## As built

**As specified.** `Showcase_ShowsEveryFrogClip`'s body became a helper both clip rows call, so the
two rows read the same. The scene was open in the Editor with no unsaved changes, and its diff is
the row alone.

**Verified.** `EnemyShowcaseTests` 5 / 5; with `PitcherModelTests` and `PitcherBodyTests`, 20 / 20.
Render: `Temp/Renders/enemy_showcase.png`, the showcase camera's view, each body sampled mid-clip in
a preview scene so the saved scene stayed clean. Console: no errors and no warnings. The rest of the
suite is M7-05q's run; this task adds one row and touches no code.
