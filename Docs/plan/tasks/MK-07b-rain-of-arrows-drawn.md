# MK-07b — Rain of Arrows drawn and authored

**Size:** M · **Depends on:** MK-07a, MK-02b · **Branch:** `dev` (direct)
**Design refs:** CH §3.5; RS-07/RS-08c (the Ranger's arrow look); colour law

## Goal

The Marksman's Lv-11 active is in the game: the Ranger looses a volley skyward, a cyan-rimmed circle
marks the ground, and arrows rain into it for two seconds.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Views/RainOfArrowsView.cs` | Game | **New.** On `RainOfArrowsCast`: ring, falling arrows, impacts |
| `Prefabs/VFX/RainOfArrows.prefab` | prefab | **New.** Ground ring + arrow particle rain + dust puffs |
| `Data/Skills/Marksman/RainOfArrows{,2,3,4}.asset` | assets | **New.** + their effect assets |
| `Tests/Game/Views/RainOfArrowsViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `Data/Paths/Marksman.asset` (skill inserted **second**, unlock 11), `RangerAnimatorView` + Ranger controller (cast-up trigger), `English`/`Pseudo`, `SkillIconBook`, `MarksmanPathTests` (rows for the new skill) |

## Behaviour

1. **Assets:** `skill.marksman.rain-of-arrows`, `-2`…`-4`, MK-07a rule 6 exactly; rank 1 `Active`,
   trigger `EnemiesInAcquireRange ≥ 3`, cooldown 10.
2. **Cast animation:** the Ranger snaps a shot **upward** — a bow-up release clip from the rig's set if
   one exists, else the normal release with the bow pitched ~60° up via the existing aim; it never
   blocks movement. A short cyan arrow streak leaves the bow skyward.
3. **Ring:** a ground disc of the radius with a cyan edge, faint fill, for the duration, telegraph
   ring style (JG round discs) but in the player's cyan.
4. **Rain:** arrows (the ordinary arrow's mesh/trail, scaled 0.8) fall steeply at random points
   inside the ring, ~16 per second, each with a small dust puff on landing; starts 0.15 s after the
   cast streak, ends with the zone.
5. From rank 3, the ring's fill tints pale blue (the slow).
6. Pooled; nothing instantiated per cast after warm-up. All visuals end on the zone's end or a stage
   boundary.
7. Loc: *"Rain of Arrows — Arrows rain on the largest group for 2 s."* and one line per rank.

## Tests

| Test | Given / When / Then |
|---|---|
| `Assets_MatchTheRankTable` / `Path_RainIsSecondAtEleven` (1) |
| `View_RingMatchesRadiusAndDuration` (3) |
| `View_RainsInsideTheRing` | every spawned arrow's landing point within the radius (4) |
| `View_Rank3_TintsFill` (5) |
| `View_Pooled_EndsWithZone` (6) |
| `Loc_Keys` (7) |

## Manual verification (Editor / device)

1. **[Editor]** Stage 12, Marksman, Rain taken: on a group of ≥ 3 the Ranger shoots up, the ring
   appears on the group, arrows fall for 2 s, enemies inside lose health; rank 3 slows them visibly.

## Out of scope

- Final icon art; sound (audio source is open).

## As built

_Filled at merge._
