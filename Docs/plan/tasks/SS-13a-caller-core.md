# SS-13a — The storm caller in core: a breakable channel and a chasing tornado

**Size:** M · **Depends on:** SS-12a (`PlayerCombat.ApplyPulse`) · **Branch:** `ss-13a-caller-core`
**Design refs:** GD §8.13, §8.11 (the priest's break), Invariants §18.1 at build (a step inside `EnemySystem.Tick`)

## Goal

A new `StormCallerBehaviour` holds 12 m, flees inside 6 m and channels 2 s, broken by any landed hit. A finished channel raises a tornado in a two-slot `TornadoField` that chases, pulses and pulls only the hero, and collapses when its caller dies.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/StormCallSpec.cs` | Core | `BurrowSpec`'s shape and validation |
| `Core/Ai/StormCallerBehaviour.cs` | Core | hold, flee, channel, cooldowns |
| `Core/Ai/TornadoField.cs` | Core | two slots: reserve, rise, drift, pulse, pull, collapse; `TornadoReading` |
| `Core/Events/StormCallerEvents.cs` | Core | the events below and `StormCallerPhase` |
| `Tests/Core/Ai/StormCallerBehaviourTests.cs` | Tests.Core | |
| `Tests/Core/Ai/TornadoFieldTests.cs` | Tests.Core | |
| *small edits* | | `EnemySpec` (`stormCall = null` last, property, guard); `EnemyBehaviourKind.StormCaller` appended; `EnemyAgent`'s factory case; `EnemySystem` (owns `Tornadoes`, ticks it after the behaviours, calls `BreakChannel` from the damage door where the priest's `OnHpLost` is called, collapses on death, clears on `Clear`, `AttachMotor`); `PlayerMotor.AddDrift`; `RunState.TornadoCount`, `TornadoAt` |
| *ripple* | | where `RunState` builds `EnemySystem`: hand it the motor (`PlayerCombat.AttachStatus`'s shape) |

## Public API

```csharp
namespace Soulvail.Core.Content;
public sealed class StormCallSpec // finite, > 0; Flee < Hold < ChannelReach; PullRadius > Radius
{
    public StormCallSpec(float holdDistance, float fleeDistance, float fleeSeconds, float fleeSpeedMultiplier,
        float channelReach, float channelSeconds, float brokenSeconds, float rechannelSeconds, float riseAhead,
        float driftSpeed, float turnDegreesPerSecond, float tornadoSeconds, float radius,
        float damagePerSecond, float pullRadius, float pullSpeed, float collapseSeconds);
        // 12, 6, 1.2, 1.4, 16, 2, 3, 6, 2, 1.6, 60, 8, 1.5, 10, 3, 1, 0.4
}
namespace Soulvail.Core.Ai;
public sealed class TornadoField { public const int Capacity = 2; public const float PulseSeconds = 0.5f; }
public readonly struct TornadoReading { int Id; int OwnerId; Vector3 Position; float Radius; bool Collapsing; }
// StormCallerBehaviour.State; EnemySystem.Tornadoes (internal); PlayerMotor.AddDrift(Vector2 velocityXZ)
// RunState.TornadoCount, RunState.TornadoAt(int index)
namespace Soulvail.Core.Events;
public enum StormCallerPhase { Hold, Flee, Channel }
public readonly struct StormCallerPhaseChanged { int Id; StormCallerPhase Phase; float Seconds; }
public readonly struct StormChannelBroken { int Id; }
public readonly struct TornadoRose { int TornadoId; int OwnerId; Vector3 Position; float Radius; float Seconds; }
public readonly struct TornadoEnded { int TornadoId; int OwnerId; bool Collapsed; }
```

## Behaviour

1. **Hold, flee.** It walks to `HoldDistance` from the hero, facing them. Inside `FleeDistance` it flees on `−DirectionToPlayer` for `FleeSeconds` at `MoveSpeed × FleeSpeedMultiplier` (the shaman's rule, without an anchor).
2. **Channel** starts when: no cooldown, no tornado of its own, hero within `ChannelReach` and outside `FleeDistance`, and `Tornadoes` grants a reservation (risen plus reserved < `Capacity`). It plants facing the hero for `ChannelSeconds`.
3. **Broken.** Damage that lands on it (`ToHp > 0`, from any source) or a flee during the channel ends it: one `StormChannelBroken`, the reservation released, `BrokenSeconds` cooldown.
4. **Rise.** A finished channel spends the reservation: a tornado at `RiseAhead` in front of the caller, heading to the hero; one `TornadoRose`.
5. **Drift.** Each tick it turns toward the hero by at most `TurnDegreesPerSecond × dt` and moves `DriftSpeed × dt` on XZ, through cover and enemies.
6. **Pulse.** Every `PulseSeconds`, a hero within `Radius` on XZ takes `DamagePerSecond × PulseSeconds` through `PlayerCombat.ApplyPulse`.
7. **Pull.** A hero within `PullRadius` and not in a Space move gets `AddDrift` toward the centre at `PullSpeed`, never past it. `PlayerMotor` adds the drift to its next `Tick` and clears it.
8. **End.** After `TornadoSeconds` it ends (`TornadoEnded`, not collapsed). The caller's death collapses it: no drift, pulse or pull from that tick, the slot freed after `CollapseSeconds`. Its cooldown is `RechannelSeconds` from the end.
9. **Never an enemy.** It is not an agent: no target, no auto-aim candidate, no hit on any enemy, pet or minion.
10. Stage `Clear` empties the field silently. `Reset()` returns the caller to Hold with no cooldown. Nothing allocates per tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Spec_Guards` | no spec on StormCaller; spec on a Chaser; bad numbers / construct / throws |
| `Hold_WalksTo12` · `Flee_Inside6` | hero at 20 m; at 5 m / tick / closes to 12; flees 1.2 s at 1.4× |
| `Channel_StartsInReach` | ready, hero 14 m / tick / `Channel`, 2 s, planted |
| `Channel_HitBreaks` · `Channel_FleeBreaks` | hit at 1 s; hero to 5 m / tick / broken, released, no rise, 3 s cooldown |
| `Channel_RisesTornado` | 2 s clean / tick / `TornadoRose` 2 m ahead, r 1.5, 8 s |
| `Cap_TwoInTheArena` | three callers ready / tick / two channel; the third waits |
| `Cap_OnePerCaller` | own tornado up, no cooldown / tick / no channel |
| `Drift_HalfSpeedTurnCapped` | hero behind the heading / 1 s / moved 1.6 m, turned ≤ 60° |
| `Pulse_TenPerSecond` | hero in ring, just hit / 1 s / 10 lost in 2 pulses, `IsPulse` |
| `Pulse_OutsideRingNone` · `Pulse_RollRefused` | hero at 2 m; rolling / 1 s / nothing lost |
| `Pull_WeakerThanWalking` | hero 2.5 m out / read the drift / 1 m/s to the centre; none at 3.5 m or in a Space move |
| `Drift_ClearedAfterTick` | `AddDrift` once / two motor ticks / added once |
| `NeverTouchesEnemies` | enemy and pet in the ring / 2 s / untouched, not moved |
| `NotATarget` | tornado up / targeter candidates / no tornado |
| `End_After8` · `Collapse_OnDeath` | 8 s; caller killed / tick / ended; collapsed, harmless, slot free at 0.4 s |
| `Rechannel_SixAfterEnd` | ended at t / tick / next channel ≥ t + 6 |
| `Clear_EmptiesField` · `Reset_Caller` | / clear, reset / no tornado; Hold |
| `Tick_Cycle_NoAlloc` | two tornado cycles / ticks / `AllocationAssert.None` |
| `PriestMend*`, `Shaman*`, `Antlion*`, `Cobra*` | unchanged / run / green |

No count or coverage test reads `EnemyBehaviourKind`'s length (grep, 2026-11-13); one that breaks moves by one and *As built* names it.

## Out of scope

- Model, body, funnel, ring (SS-13b–c). Data, roster (SS-13d). Weather of any kind. Elites.

## As built

_Commit body._
