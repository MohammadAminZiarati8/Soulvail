# SS-16a — Shardback fan, burst and recovery in core

**Size:** M · **Depends on:** SS-15e · **Branch:** `ss-16a-shardback-attacks`
**Design refs:** GD §9.1, §9.4; Invariants §18.2–18.5.

## Goal

Committed fan/burst geometry; no roster change.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/ShardbackBehaviour.cs` | Core | Approach, casts, recovery, phase cancellation |
| `Core/Ai/ShardbackStats.cs` | Core | Named Stat properties below |
| `Core/Events/ShardbackEvents.cs` | Core | Typed immutable payloads below |
| `Tests/Core/Ai/ShardbackBehaviourTests.cs` | Tests.Core | Geometry, lifecycle, allocation |

Only these files and `.meta` companions change.

## Public API

```csharp
public sealed class ShardbackBehaviour : IEnemyBehaviour, IBossPhaseListener
{
    public ShardbackBehaviour(EnemyAgent agent, ShardbackStats stats);
    public void Tick(in EnemyTickContext ctx);
    public void Reset();
    public void OnPhaseEntered(int phase);
}
public sealed class ShardbackStats
{
    public Stat MoveSpeed { get; } // 2
    public Stat CastRange { get; } // 10
    public Stat FanLength { get; } // 12
    public Stat FanHalfWidth { get; } // 0.6
    public Stat FanAngleDegrees { get; } // 25
    public Stat FanWindupSeconds { get; } // 1
    public Stat FanDamage { get; } // 14
    public Stat FanRecoverySeconds { get; } // 1.25
    public Stat BurstRadius { get; } // 4
    public Stat BurstWindupSeconds { get; } // 1.2
    public Stat BurstDamage { get; } // 16
    public Stat BurstRecoverySeconds { get; } // 2
}
public enum ShardbackMove { Fan, Burst }
// readonly structs, matching public readonly fields and constructors:
public ShardbackArmed(int bossId, ShardbackMove move,
    Vector3 origin, Vector2 facing, float reach, float halfWidth,
    float angleDegrees, float seconds);
public ShardbackImpact(int bossId, ShardbackMove move,
    Vector3 origin, float recoverySeconds);
```

## Behaviour

1. Path toward the hero until within CastRange. One movement intent per tick, zero while casting/recovering. Lock origin/facing. Interim phase 0 repeats fan; phases 1/2 alternate fan/burst. SS-16b adds charge/combo.
2. After the full fan tell, test finite XZ strips at 0 and +/-FanAngleDegrees against current hero position. Edges/endpoints inclusive; behind/beyond safe. One FanDamage even in overlaps; no hero hit for a decoy. No tracking/projectile damage. SS-16b adds cover clipping; this fixture has no cover.
3. Burst tests a filled XZ disc at locked origin after its full tell. Edge inclusive; outside safe. Both attacks respect dodge/invulnerability and ignore decoys. No duplicate impact on coarse ticks.
4. Publish recovery duration at impact. Finish recovery before a full next tell, even on coarse ticks. OnPhaseEntered cancels pending work/restarts sequence; wrapper owns beats. Reset restores phase 0. No immunity, reflection or bonus damage.
5. Warm ticks allocate nothing. Events carry geometry. Use AllocationAssert.None.

Tests > Behaviour > API > Files; built code wins. Record deviations/resolutions in As built. Null/non-finite guards implied.

## Tests

| Test | Given / When / Then |
|---|---|
| `Approach_LocksGeometry` | Range/path, moving hero / arm / one intent, fixed geometry |
| `Fan_DeadlineAndEdges` | Three strips, overlaps, edges, gaps, behind/beyond / tick / no early hit; one hit inside |
| `Burst_PhasesAndDisc` | Phases 0/1/2, inside/edge/outside, dodge/decoy / tick / rotation and safe answers; also decoy fan |
| `Recovery_CoarseTick` | Impact, large dt / tick / one impact, full recovery/next tell |
| `PhaseOrReset_Cancels` | Armed/recovering / phase or reset / no stale damage/event, restarted sequence |
| `Tick_AllocatesNothing` | Warm cycles / AllocationAssert.None / zero allocation |

## Manual verification (Editor / device)

Core only. Full EditMode suite and format gate required.

## Out of scope

Charge, cover, pillars, live registration, art, audio, HP and adds.

## As built

Commit body, at most 1 500 bytes. Delete this spec and tick its ROADMAP row only after implementation and verification.
