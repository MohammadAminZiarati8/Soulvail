# EA-03c — Decoy Roll and Fading Burst drawn and authored

**Size:** M · **Depends on:** EA-02, EA-03a, EA-03b · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Decoy Roll, Fading Burst); readable at first look (zones sized to their reach)

## Goal

Both Lv-14 passives are in the game: the decoy reads as a lure, the burst as a 3 m ring.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Views/ShadeBurstView.cs` | Game | **New.** Pooled ring on `ShadeBurst` |
| `Prefabs/Vfx/VFX_ShadeBurst.prefab` | prefab | **New.** Ground ring of exactly `BurstRadius` + a puff |
| `Data/Skills/EchoArcher/DecoyRoll{,2,3}.asset` | assets | **New.** + their effect assets |
| `Data/Skills/EchoArcher/FadingBurst{,2,3}.asset` | assets | **New.** + their effect assets |
| `Tests/Game/Views/ShadeBurstViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `Data/Paths/EchoArcher.asset` (third and fourth, unlock 14), `ShadeView` (decoy pose), `RunScope`, `English`/`Pseudo`, `SkillIconBook`, `EchoArcherPathTests` |

## Behaviour

1. **Assets:** `skill.echo-archer.decoy-roll`, `-2`, `-3` (EA-03a rule 5) and
   `skill.echo-archer.fading-burst`, `-2`, `-3` (EA-03b rule 5), both `Passive`, in that order.
2. **Decoy pose:** a `Decoy` shade holds the bow low and pulses gently instead of aiming, so it
   reads as bait rather than a shooter.
3. **Burst:** on `ShadeBurst`, a smoky deep-cyan ring of exactly `Radius` flashes on the ground
   for 0.4 s with a puff; with `Slows`, the ring is edged in the slow icon's colour.
4. Pooled (8); nothing instantiated per burst after warm-up; a stage boundary ends every ring.
5. **Loc:** Decoy Roll — *"A roll leaves a shade enemies chase for 2 s, at most every 8 s.
   Bosses ignore it."*; ranks *"3 s, every 6 s."*, *"Every 5 s."* Fading Burst — *"Shades burst
   when they fade: 1.5× weapon damage within 3 m."*; ranks *"2×."*, *"2.5×, and the burst slows."*

## Tests

| Test | Given / When / Then |
|---|---|
| `Assets_MatchTheRankTables` / `Path_BothAtFourteen_InOrder` (1) |
| `DecoyShade_LowBowPose_NoAim` (2) |
| `Burst_RingMatchesRadius` / `Burst_Slows_EdgeTinted` (3) |
| `Pool_NoInstantiate_ClearsOnBoundary` (4) |
| `Loc_Keys` (5) |

## Manual verification (Editor / device)

1. **[Editor]** Stage 16, Echo Archer with both: roll — a shade stays behind and the enemies
   turn to it; a second roll at once leaves none. At stage 15 the Warlord ignores it.
2. **[Editor]** A Mirror Image shade fades: a 3 m ring flashes and enemies inside take damage.

## Out of scope

- Sound; final icon art.

## As built

_Filled at merge._
