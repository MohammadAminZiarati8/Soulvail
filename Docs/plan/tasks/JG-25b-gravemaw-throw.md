# JG-25b — The eruption's throw: a ring-exit probe and a forced player move

> **Parked** (owner 2026-11-02): Gravemaw moves to stage 25; Thornheart takes 20. Revise before building: cut the ring-exit probe from 25b (a plain knockback stopped by walls), one move per window in phase 1, stage numbers and roster in 25g, and the JG-20x references.

**Size:** M · **Depends on:** JG-20a · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rule 2; AR §18.2 (core holds no walls), §4.1; JG-15b's lane probe

## Goal

A player caught in the eruption is thrown to 1 m outside the ring along the shortest open path in
0.25 s, with no stun; walls come from a probe Unity answers.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Run/PlayerThrowIntent.cs` | Core | **New.** Direction, distance, duration |
| `Core/Run/RingExitProbeIntent.cs` | Core | **New.** Centre, radius, margin, sectors |
| `Core/Ai/GravemawBehaviour.cs` | Core | Probe-gated arming; exit choice; the throw |
| `Game/Views/ThrowMotion.cs` | Game | **New.** Walks the throw like `ChargeMotion` |
| `Game/Adapters/RingExitAdapter.cs` | Game | **New.** Answers the probe |
| `Tests/Core/Ai/GravemawThrowTests.cs` | Tests.Core | **New** |
| `Tests/Game/Adapters/ThrowAdapterTests.cs` | Tests.Game | **New** |
| *small edits* | | `IIntentSink.RingExitProbe`, `.Throw`; `IRunSession.ReportRingExits`, routed to the live agent's `Inner as IRingExitListener`; `IntentBuffer`, `RunTicker`, `RunScope` |

## Public API

```csharp
public readonly struct PlayerThrowIntent { Vector2 DirectionXZ; float Distance; float Duration; }
public readonly struct RingExitProbeIntent { int EnemyId; Vector3 Centre; float Radius, Margin; int Sectors; }
// IRunSession: void ReportRingExits(int enemyId, bool centreOpen, int openMask);  bit i = sector i
public interface IRingExitListener { void OnRingExits(bool centreOpen, int openMask); }
// GravemawStats gains: ThrowSeconds /*0.25*/, ExitMargin /*1 m*/;  const Sectors = 24
```

**Sense or fact?** A fact. Walls are asked once per ring about a point core chose, so a standing
snapshot sense would ship a mask nobody reads for 59 of 60 frames. Same shape as `LaneProbe`.

## Behaviour

1. **Probe first:** at chase end core emits one probe at the predicted C; the ring arms on the first
   tick with an answer, or the second tick with none (all open).
2. **Blocked centre:** `centreOpen` false → probe once more at the player's position, arm on that
   answer whatever it says.
3. **Sector i** is open when a capsule cast from C along `i × 15°` to `Radius + Margin` is clear.
4. **Exit:** on a hit that was `Applied` (not `Blocked`), landing L_i = C + dir_i × (R + Margin);
   choose the open i nearest the player; ties to the lowest i. None open → no throw.
5. **Throw:** `PlayerThrowIntent(player→L, |L − player|, ThrowSeconds)`; core calls
   `PlayerMotor.Hold(ThrowSeconds)`. No status, no stun: firing and skills run.
6. **Missed or rolled:** a miss, a decoy quarry or an i-framed player is neither hurt nor thrown.
7. **Executor:** `ThrowMotion` moves the controller at distance ÷ duration, stopping at any wall; a
   `ChargeIntent` beginning mid-throw cancels it. The probe uses the `Cover` mask plus walls.

## Tests

| Test | Given / When / Then |
|---|---|
| `Probe_EmittedOnceAtChaseEnd_ArmsOnAnswer` / `Probe_Unanswered_ArmsSecondTickAllOpen` (1) |
| `Probe_CentreBlocked_RetriesAtPlayer` (2) |
| `Adapter_ClearSector_Open` / `Adapter_WallInSector_Blocked` (3) |
| `Exit_RadialOpen_LandsOneMetreOutside` / `Exit_Blocked_NextNearestOpen` / `Exit_TieLowestIndex` / `Exit_NoneOpen_NoThrow` (4) |
| `Throw_Hit_EmitsIntentAndHoldsMotor` / `Throw_DoesNotStopFiring` (5) |
| `Throw_Miss_None` / `Throw_Decoy_None` / `Throw_RollIFrames_NoDamageNoThrow` (6) |
| `ThrowMotion_MovesDistanceInDuration` / `ThrowMotion_StopsAtWall` / `ThrowMotion_CancelledByCharge` (7) |

## Out of scope

- Tremor and spit (20c); the ring's look (20f).

## As built

_Filled at merge._
