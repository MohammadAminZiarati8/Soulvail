# JG-15b — The Warlord's shield charge and stone daze, with a lane probe port

**Size:** M · **Depends on:** JG-15a · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 2, 6; AR §18.2 (no walls in core), §18.4; MK-08a's probe

## Goal

A 0.9 s lane, then a charge down it; a stone ends it and dazes the Warlord 2 s, guard down.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/WarlordBehaviour.cs` | Core | Charge: wind-up, dash, daze |
| `Core/Ai/ILaneStopListener.cs` | Core | **New.** `void OnLaneStop(bool hit, float distance)` |
| `Core/Events/WarlordEvents.cs` | Core | Gains `WarlordDazed` |
| `Tests/Core/Ai/WarlordChargeTests.cs` | Tests.Core | **New** |
| *small edits* | | `IIntentSink.LaneProbe(in LaneProbeIntent)`; `IRunSession.ReportLaneStop`; `RunSession.ReportLaneStop` routes to the live agent's `BossBehaviour.Inner as ILaneStopListener` (unknown or dead id ignored) |
| *ripple* | | `IntentBuffer` (list, `Clear`), `EnemySystem`'s forwarding sink, every test `IIntentSink` |

## Public API

```csharp
public readonly struct LaneProbeIntent { int EnemyId; Vector3 From; Vector3 Direction; float Reach; float Radius; }
// IRunSession: void ReportLaneStop(int enemyId, bool hit, float distance);
// WarlordBehaviour : ILaneStopListener
public const float ChargeTelegraphSeconds = 0.9f, ChargeMinRange = 4f, ChargeLength = 14f,
  ChargeHalfWidth = 1.1f, ChargeSpeed = 16f, ChargeDamageScale = 2f, ChargeCooldownSeconds = 6f,
  ProbeRadius = 0.8f, DazeSeconds = 2f, DazeSlack = 1f, DashGraceSeconds = 0.3f;
public bool IsDazed { get; }
public readonly struct WarlordDazed { int BossId; Vector3 At; float Seconds; }
```

## Behaviour

1. **Choose:** after the gap, with no sweep due, the player ≥ `ChargeMinRange` away and the charge
   off cooldown → arm `Charge`: stand, catch the line to the player, publish
   `EnemyTelegraph(id, 0.9, 2)`, emit one `LaneProbe` (from the body, the line, 14 m, 0.8 m).
2. **Lane:** opens on the first tick with an answer — length `min(14, distance)` on a hit, 14 on a
   miss — or, unanswered, on the second tick at 14. Armed for the wind-up's remainder, damage
   `ContactDamage × 2`, through `LaneSystem.Open`. The lane is the hit; the dash deals none.
3. **Dash** at 0.9 s along the line at `ChargeSpeed`. Travel is measured from the agent's reported
   position (XZ), not from speed × time. It ends when travel ≥ lane length − 0.1 or after
   `length / speed + DashGraceSeconds`.
4. **Daze:** probe hit and travel ≥ distance − `DazeSlack` → dazed `DazeSeconds`: stands, guard 0
   (shield wedged), `WarlordDazed` published; then the guard returns. Else it recovers at once.
   Cooldown counts from the dash's end.
5. **Root** (Snare): `RootGate` zeroes the dash; travel stays short → no daze; the dash times out.
6. **Stun** (Binding Shot to a stone): the behaviour is not ticked; on release a dash in progress
   continues its timeout. Bosses' half duration is `EnemySystem.Stun`'s.
7. A phase entry mid-charge drops the move; an open lane still bites (promise). No allocation.

## Tests

| Test | Given / When / Then |
|---|---|
| `Charge_FarPlayer_ArmsTelegraphsAndProbes` / `Charge_NearPlayer_Sweeps` / `Charge_OnCooldown_NotArmed` (1) |
| `Lane_ProbeHit_LengthIsTheStone` / `Lane_ProbeMiss_Full` / `Lane_NoAnswer_OpensFullOnSecondTick` (2) |
| `Lane_ArmedForTheRemainder_BitesAtDash` (2) |
| `Dash_MeasuresReportedTravel` / `Dash_EndsAtLaneEnd` / `Dash_TimesOut` (3) |
| `Stone_Reached_Dazes2s_GuardZero_Publishes` / `Daze_Ends_GuardRestored` / `NoStone_NoDaze` (4) |
| `Rooted_InLane_NoDaze` — session: `EnemySystem.Root` mid-dash (5) |
| `Stunned_MidDash_NotTicked_HalfDuration` — session (6) |
| `Report_UnknownOrDeadId_Ignored` / `Phase_MidCharge_LaneStillBites` / `Tick_DoesNotAllocate` (7) |

## Out of scope

- The Unity probe adapter and stones (15g); the daze look (15f).

## As built

_Filled at merge._
