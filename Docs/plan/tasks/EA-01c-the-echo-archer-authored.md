# EA-01c — The Echo Archer authored

**Size:** M · **Depends on:** EA-01b · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Echo Archer); SK-01b (the Skirmisher's authoring, the shape to copy)

## Goal

The Echo Archer is the Ranger's third path: its card shows at level 11 beside the other two, and
choosing it installs Echo Arrow.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Data/Paths/EchoArcher.asset` | asset | **New.** `PathDefinition`: Echo Arrow (unlock 11) |
| `Data/Skills/EchoArcher/EchoArrow{,2,3,4}.asset` | assets | **New.** + their `ModifyStat` effect assets |
| `Tests/Game/Authoring/EchoArcherPathTests.cs` | Tests.Game | **New.** `SkirmisherPathTests`' shape |
| *small edits* | | `Data/Characters/Ranger.asset` (`_paths` slot 3), `English`/`Pseudo`, `SkillIconBook` (placeholder icon), `ContentValidationTests` if it lists paths |

## Behaviour

1. **Ids:** path `path.ranger.echo-archer`; skills `skill.echo-archer.echo-arrow`, `-2`…`-4`. Rank
   effects are exactly EA-01b rule 5.
2. Echo Arrow is the path's first skill. Later EA tasks insert theirs in CH §3.5's table order.
3. The Ranger's `_paths` are Marksman, Skirmisher, Echo Archer. From level 11 the tree shows three
   cards; choosing one hides the others, as MK-03 does.
4. `_look` stays empty until EA-06; the Ranger keeps its base look.
5. **Loc** (English; Pseudo generated):
   - card — *"Echo Archer — Be in many places. Shades of you shoot, lure and burst."*
   - Echo Arrow — *"Every 4th arrow is loosed again by a shade, for 60% damage."*; ranks
     *"Every 3rd arrow."*, *"80% damage."*, *"Every 2nd arrow."*
6. A placeholder icon in the existing icon style; final art is out of scope.
7. Three cards must fit the tree screen's path area; if they overlap, the smallest layout change
   that fits them is in scope and named in *As built*.

## Tests

| Test | Given / When / Then |
|---|---|
| `Assets_MatchTheRankTable` | every rank equals EA-01b rule 5 (1) |
| `Path_EchoArrowFirstAtEleven` (2) |
| `Ranger_OffersThreePaths` | slots 1–3 set, in order (3) |
| `Choose_EchoArcher_InstallsEchoArrow` | `PathFlow` choose / in the tree, gated by level (3) |
| `Path_WithoutLook_Loads` (4) |
| `Loc_Keys` | every new key resolves in English and Pseudo (5) |
| `TreeScreen_ThreeCards_DoNotOverlap` | three card rects disjoint (7) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 at stage 12: the tree shows three cards. Choose the Echo Archer and take
   Echo Arrow: every 4th arrow is followed by a second one a moment later (ordinary-looking until
   EA-02).

## Out of scope

- Mirror Image's assets and the shade view (EA-02); the look (EA-06); final icon art.

## As built

_Filled at merge._
