# EA-05b — Hall of Mirrors drawn and authored

**Size:** M · **Depends on:** EA-02, EA-05a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Hall of Mirrors); readable at first look; SK-05b (Whirlwind's cast)

## Goal

The Echo Archer's Lv-20 active is in the game: a ring rises around the Ranger and fills with shades.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Authoring/HallOfMirrorsDefinition.cs` | Game | **New.** Effect definition |
| `Game/Views/HallOfMirrorsView.cs` | Game | **New.** The ring on `HallRaised`, held for its seconds |
| `Prefabs/Vfx/VFX_HallRing.prefab` | prefab | **New.** Ground ring of exactly `Radius`, a rising shimmer |
| `Data/Skills/EchoArcher/HallOfMirrors{,2,3}.asset` | assets | **New.** + their effect assets |
| `Tests/Game/Views/HallOfMirrorsViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `Data/Paths/EchoArcher.asset` (sixth, unlock 20), `RangerAnimatorView` (bow-raised cast), `RunScope`, `English`/`Pseudo`, `SkillIconBook`, `EchoArcherPathTests` |

## Behaviour

1. **Assets:** `skill.echo-archer.hall-of-mirrors`, `-2`, `-3`, EA-05a rule 6 exactly; rank 1
   `Active`, trigger `EnemiesInAcquireRange ≥ 1`, cooldown 30.
2. **Ring:** on `HallRaised`, a smoky deep-cyan ring of exactly `Radius` rises at `Center` over
   0.2 s with a brief shimmer, holds faintly for `Seconds`, then fades over 0.3 s.
3. **Cast:** the Ranger raises the bow overhead on the upper-body layer for 0.3 s; it never blocks
   movement.
4. Pooled (one ring); a stage boundary ends it.
5. **Loc:** *"Hall of Mirrors — 4 shades rise around you and shoot for 5 s, at 70% damage."*;
   ranks *"6 shades."*, *"7 s, 100% damage."*

## Tests

| Test | Given / When / Then |
|---|---|
| `Assets_MatchTheRankTable` / `Path_HallSixthAtTwenty` (1) |
| `Ring_MatchesRadius_HoldsForSeconds` (2) |
| `Cast_UpperBodyRaise_MovementKept` (3) |
| `Ring_ClearsOnBoundary` (4) |
| `Loc_Keys` (5) |

## Manual verification (Editor / device)

1. **[Editor]** Stage 20, Echo Archer, Hall taken: a ring rises around the Ranger and four shades
   stand on it shooting; walking out leaves them standing; all fade after 5 s.
2. **[device]** Six Hall shades and the Ranger shooting: no frame drop.

## Out of scope

- Sound; final icon art.

## As built

_Filled at merge._
