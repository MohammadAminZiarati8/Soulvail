# JG-25c — Gravemaw's tremor and rock spit while surfaced

> **Parked** (owner 2026-11-02): Gravemaw moves to stage 25; Thornheart takes 20. Revise before building: cut the ring-exit probe from 25b (a plain knockback stopped by walls), one move per window in phase 1, stage numbers and roster in 25g, and the JG-20x references.

**Size:** M · **Depends on:** JG-20b · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 2; Warlord lobs (JG-15d); `ShockwaveSystem`; Ranger roll (CC §5)

## Goal

Inside the 4 s window it sends a tremor ring the roll answers, then lobs ringed rocks; the throw
lands you right on the tremor's path.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/GravemawBehaviour.cs` | Core | Tremor and spit moves in the window |
| `Core/Ai/GravemawStats.cs` | Core | Gains the stats below |
| `Tests/Core/Ai/GravemawAttackTests.cs` | Tests.Core | **New** |
| *small edits* | | `GravemawBehaviour` ctor gains `ShockwaveSystem`, `ProjectileSystem`; `RunSession` row passes them |

## Public API

```csharp
// GravemawStats gains: TremorStartSeconds /*0.4 after eruption*/, TremorTelegraphSeconds /*0.8*/,
//   TremorSpeed /*7*/, TremorMaxRadius /*9*/, TremorThickness /*1*/, TremorDamageScale /*1*/,
//   SpitStartSeconds /*1.6*/, SpitTelegraphSeconds /*0.8*/, SpitSpears /*3*/, SpitSpread /*2.5*/,
//   SpitDamageScale /*1*/
// GravemawEvents: none new; moves publish EnemyTelegraph(id, s, 4 tremor | 5 spit)
```

## Behaviour

1. **Tremor:** `TremorStartSeconds` after the eruption (the throw has landed), stand and publish
   `EnemyTelegraph(id, 0.8, 4)`; at release `ShockwaveSystem.Emit` from C: speed 7, max radius 9,
   thickness 1, damage `ContactDamage × TremorDamageScale`. A full system drops the ring, as
   Croaker's.
2. **The roll answers it:** the wave damages through the player's damage door, so Charge i-frames
   passing the band take nothing; outrunning past 9 m also takes nothing.
3. **Spit:** `SpitStartSeconds` into the window, `EnemyTelegraph(id, 0.8, 5)`; at release fire
   `SpitSpears` projectiles carrying the boss's own spec id (its `MarksLanding` rings each landing):
   one at the player, two at ±`SpitSpread` across the line. One volley per window.
4. **Window gate:** both moves run only while Surfaced; a window that ends (or a phase entry) drops
   an armed move. The window still closes on its clock; moves never extend it.
5. **Every telegraph ≥ 0.6 s.** No per-tick allocation.

## Tests

| Test | Given / When / Then |
|---|---|
| `Tremor_AfterEruption_TelegraphsPointEight_ThenEmits` / `Tremor_FullSystem_Dropped` (1) |
| `Tremor_RollIFramesInBand_NoDamage` / `Tremor_OutsideMaxRadius_NoDamage` / `Tremor_StandingInBand_Hurts` — session (2) |
| `Spit_TelegraphsPointEight_FiresThreeWithOwnSpecId` / `Spit_OneVolleyPerWindow` (3) |
| `Moves_OnlyWhileSurfaced` / `Window_EndDropsArmedMove` / `Phase_DropsArmedMove` (4) |
| `Telegraphs_AtLeastPointSix` / `Tick_DoesNotAllocate` (5) |

## Out of scope

- Phase 2 doubling (20d); the tremor's and rocks' look (20f).

## As built

_Filled at merge._
