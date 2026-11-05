# SK-01b — The Skirmisher authored

**Size:** M · **Depends on:** SK-01a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Skirmisher); MK-02b (the Marksman's authoring, the shape to copy)

## Goal

The Skirmisher is the Ranger's second path: its card shows at level 11 beside the Marksman's, and
choosing it installs Running Shot and Hit and Run.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Data/Paths/Skirmisher.asset` | asset | **New.** `PathDefinition`: Running Shot (unlock 11), Hit and Run (unlock 14) |
| `Data/Skills/Skirmisher/RunningShot{,2,3,4}.asset` | assets | **New.** + their `ModifyStat` effect assets |
| `Data/Skills/Skirmisher/HitAndRun{,2,3}.asset` | assets | **New.** + their effect assets |
| `Tests/Game/Authoring/SkirmisherPathTests.cs` | Tests.Game | **New.** `MarksmanPathTests`' shape |
| *small edits* | | `Data/Characters/Ranger.asset` (`_paths` slot 2), `English`/`Pseudo`, `SkillIconBook` (placeholder icons), `StatusIconBook` (`status.hit-and-run`), `ContentValidationTests` if it lists paths |

## Behaviour

1. **Ids:** path `path.ranger.skirmisher`; skills `skill.skirmisher.running-shot`, `-2`…`-4` and
   `skill.skirmisher.hit-and-run`, `-2`, `-3`. Rank effects are exactly SK-01a rules 3 and 7.
2. **Path order:** Running Shot first (11), Hit and Run second (14). Later SK tasks insert their
   skills in CH §3.5's table order; this task authors only these two.
3. The Ranger's `_paths` are Marksman, Skirmisher, empty. From level 11 the tree shows **two**
   cards; choosing one hides the other, as MK-03 does.
4. `_look` stays empty until SK-07; a path without a look keeps the Ranger's base look. If
   `PathDefinition` refuses an empty look, author an empty `PathLook` and say so in *As built*.
5. **Loc** (English; Pseudo generated):
   - card — *"Skirmisher — Never stop. Shoot on the run, roll often, strike as you pass."*
   - Running Shot — *"Fire while moving, at 40% of your standing rate."*; ranks *"55%"*, *"70%"*,
     *"85%"*.
   - Hit and Run — *"Hits give +15% move speed for 1.5 s."*; rank 2 *"+20% speed. Momentum: +2%
     damage per metre moved in the last 2 s, up to 8 m."*; rank 3 *"+25% speed; +3% per metre."*
6. Placeholder icons from the existing icon book style; final art is out of scope.
7. Status icon `status.hit-and-run`: a placeholder in the cyan-rimmed style of MK-05's row.

## Tests

| Test | Given / When / Then |
|---|---|
| `Assets_MatchTheRankTables` | every rank's effects equal SK-01a rules 3 and 7 (1) |
| `Path_OrderAndUnlockLevels` | Running Shot 11 first, Hit and Run 14 second (2) |
| `Ranger_OffersMarksmanAndSkirmisher` | slots 1–2 set, 3 empty (3) |
| `Choose_Skirmisher_InstallsBothSkills` | `PathFlow` choose / both in the tree, gated by level (3) |
| `Path_WithoutLook_Loads` (4) |
| `Loc_Keys` | every new key resolves in English and Pseudo (5, 7) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 at stage 12: the tree shows Marksman and Skirmisher cards. Choose Skirmisher,
   take Running Shot: the Ranger shoots while walking, slower than standing (still in the bow-down
   pose until SK-02). Take Hit and Run at 14: hits speed the Ranger and its status icon shows.

## Out of scope

- The run-and-shoot animation (SK-02); the other four skills; the look (SK-07).

## As built

_Filled at merge._
