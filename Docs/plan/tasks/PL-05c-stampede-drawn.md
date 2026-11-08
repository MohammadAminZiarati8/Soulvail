# PL-05c — Stampede drawn and authored

**Size:** M · **Depends on:** PL-05a, PL-05b · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Stampede); MK-10b (Killshot drawn); P1

## Goal

The Packleader's Lv-20 active in the arc: a cyan line flashes, a herd of boars thunders down it in
dust, and struck enemies tumble.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/StampedeView.cs` | Game | **New.** A pool of 12 boars placed along core's line by time; dust trail |
| `Animation/Controllers/AC_Boar.controller` | — | **New.** `Charge` loop |
| `Game/Authoring/StampedeDefinition.cs` | Game | **New.** Knockdown seconds |
| `Data/Skills/Packleader/Stampede*.asset` | — | **New.** Three ranks |
| `Data/Effects/Packleader/Stampede*.asset` | — | **New** |
| `Data/Paths/Packleader.asset` | — | Stampede in the Lv-20 row: the path's 20 ranks complete |
| `Tests/Game/Views/StampedeViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `Boar.prefab` gains the animator; `Prefabs/Vfx/StampedeDust.prefab`; `RunScope`; `English`/`Pseudo`; `SkillIconBook`; a light camera shake on start |

## Behaviour

1. On `StampedeStarted` a cyan strip of the line's width shows for 0.3 s (the player's attack, so
   cyan, never red-orange).
2. Boars appear at the start point `Spacing` s apart and run down the line at core's speed with a
   dust trail; each fades at the line's end. Positions are computed from the event's numbers and
   time, never ahead of core.
3. A small dust burst at each `BoarHit`; light enemies show their stun stars (BM-07a).
4. `StampedeEnded` or a stage change returns every boar to the pool.
5. Stampede is a circle in the arc. The path now authors 20 ranks.
   - Rank 1: active `Stampede(0.8)`, trigger `EnemiesInAcquireRange ≥ 1`, 30 s cooldown,
     auto-cast on by default.
   - Rank 2: `StampedeBoars` +3.
   - Rank 3: `StampedeDamage` +0.3 and `StampedeHalfWidth` +0.5.

## Tests

| Test | Given / When / Then |
|---|---|
| `Started_ShowsCyanStrip_PointThree` (1) |
| `Boars_SpacedAndOnLine` (2) |
| `Hit_DustBurst` (3) |
| `Ended_ReturnsAll` (4) |
| `Stampede_ThreeRanks_Numbers` / `Packleader_TwentyRanks` (5) |

## Manual verification (Editor / device)

1. **[Editor]** Learn Stampede at 20 in a crowd: six boars run the crowded line, frogs tumble;
   at rank 2 nine boars.
2. **[device]** Frame time holds with twelve boars and a full wave.

## Out of scope

- The Packleader's look (PL-06).

## As built

_Filled at merge._
