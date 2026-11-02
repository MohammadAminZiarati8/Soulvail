# JG-25a — Gravemaw in core: burrow chase, predicted ring, eruption, vulnerable window

> **Parked** (owner 2026-11-02): Gravemaw moves to stage 25; Thornheart takes 20. Revise before building: cut the ring-exit probe from 25b (a plain knockback stopped by walls), one move per window in phase 1, stage numbers and roster in 25g, and the JG-20x references.

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §9.1; AR §18.4

## Goal

Stage 20's boss in core, first cut: it burrows, chases, rings the player's predicted spot, erupts
there and can be hurt for 4 s.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/GravemawBehaviour.cs` | Core | **New.** Cycle, prediction, ring, window |
| `Core/Ai/GravemawStats.cs` | Core | **New.** Every number below as a `Stat` |
| `Core/Events/GravemawEvents.cs` | Core | **New.** Four events |
| `Tests/Core/Ai/GravemawBehaviourTests.cs` | Tests.Core | **New** |
| *small edits* | | `RunSession._bossBehaviours` row; `BossDispatchTests` row |

## Public API

```csharp
public enum GravemawState { Chase, Tunnel, Surfaced, Dive }   // Tunnel = ring warning
public sealed class GravemawStats {  // each a Stat, default in comment
  Stat ChaseSeconds /*2.5*/, TunnelSpeedScale /*3*/, WarnSeconds /*1*/, RingRadius /*3*/,
       RingDamageScale /*1.5*/, PredictWindowSeconds /*0.4*/, LeadSeconds /*1*/,
       MaxLead /*6*/, WindowSeconds /*4*/, DiveSeconds /*0.5*/; }
public sealed class GravemawBehaviour : IEnemyBehaviour, IBossPhaseListener {
  public GravemawBehaviour(EnemyAgent agent, GravemawStats stats, IRandomStream random);
  public GravemawState State { get; }  public Vector3 RingCentre { get; }
}
// GravemawEvents.cs: GravemawBurrowed(BossId) · GravemawRingArmed(BossId, At, Radius, Seconds)
//   GravemawErupted(BossId, At, Radius, Hit) · GravemawWindow(BossId, Open, Seconds)
```

## Behaviour

1. **Cycle:** Chase → Tunnel → Surfaced → Dive → Chase; clocks count ticked time.
2. **Untargetable** in Chase/Tunnel/Dive: `IsVulnerable` false and external invulnerable (no
   auto-aim; damage `Blocked`); lifted in Surfaced. `GravemawBurrowed` on entering Chase.
3. **Chase:** moves at `MoveSpeed` along `DirectionToPlayer`; ends once `ChaseSeconds` have passed
   and the predicted point is within `MoveSpeed × TunnelSpeedScale × WarnSeconds`.
4. **Prediction:** velocity estimate from `Blackboard.PlayerPosition` samples, smoothed over
   `PredictWindowSeconds`; point = position + velocity × `LeadSeconds`, lead ≤ `MaxLead`. A decoy
   is the quarry and draws the ring.
5. **Ring:** at chase end fix centre C; publish `EnemyTelegraph(id, 1, 3)`, `GravemawRingArmed`.
   C never moves.
6. **Tunnel:** the body moves to C at `MoveSpeed × TunnelSpeedScale`, stopping within 0.3 m.
7. **Eruption** at `WarnSeconds`: a non-decoy quarry within `RingRadius` of C (XZ) is hit for
   `ContactDamage × RingDamageScale`. Publish `GravemawErupted`.
8. **Window:** `WindowSeconds` vulnerable (`GravemawWindow`), then `DiveSeconds` standing, then Chase.
9. **CC:** stun freezes every clock (the window stretches); root stops Tunnel short, the ring still
   bites at C; slow slows Chase and Tunnel (boss half durations are `EnemySystem`'s).
10. **Phase entry** drops the ring and burrows to Chase. **Reset** lifts invulnerability.
11. Registered for `boss.gravemaw`, `Misc`. No per-tick allocation.

## Tests

| Test | Given / When / Then |
|---|---|
| `Cycle_RunsInOrder` (1) |
| `Burrowed_IsInvulnerableAndUntargetable` / `Surfaced_IsVulnerable` (2) |
| `Chase_WalksToPlayer_EndsAfterSecondsAndInReach` (3) |
| `Predict_StraightRun_Leads` / `Predict_Standing_OnPlayer` / `Predict_Clamped` / `Predict_Decoy_AimsAtDecoy` (4) |
| `Ring_TelegraphsOneSecond_NeverMoves` (5) |
| `Tunnel_ArrivesAtCentre` (6) |
| `Eruption_InsideRing_Hits` / `Eruption_Outside_Misses` / `Eruption_Decoy_Misses` (7) |
| `Window_FourSeconds_ThenDiveThenChase` (8) |
| `Stun_StretchesWindow` / `Root_Tunnel_RingStillBites` / `Slow_ChaseSlower` — session (9) |
| `Phase_DropsRing_ToChase` / `Reset_LiftsInvulnerability` (10) |
| `Dispatch_GravemawBossId_Builds` / `Tick_DoesNotAllocate` (11) |

## Out of scope

- Everything in 20b–g.

## As built

_Filled at merge._
