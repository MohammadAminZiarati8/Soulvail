# SS-04a — The scorpion in core: the fan approach and a locked tail strike strip

**Size:** M · **Depends on:** — · **Branch:** `ss-04a-scorpion-core`
**Design refs:** GD §8.4, §8.1

## Goal

`ChaserBehaviour` honours an optional `SkitterSpec`: a pack fans to four bearings round the hero, closes, and strikes only in a locked strip ahead.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/SkitterSpec.cs` | Core | `LeapSpec`'s shape |
| `Core/Ai/ChaserBehaviour.cs` | Core | `Spread` state, locked wind-up, strip hit |
| `Tests/Core/Ai/ScorpionSkitterTests.cs` | Tests.Core | as `BullfrogLeapTests` |
| *small edits* | | `EnemySpec` param + property; `ChaserState.Spread` |

## Public API

```csharp
namespace Soulvail.Core.Content;
public sealed class SkitterSpec
{
    public const int SlotCount = 4;
    public SkitterSpec(float ringRadius, float fanDegrees, float spreadTimeout,
        float zoneLength, float zoneHalfWidth); // 4, 120, 3, 1.8, 0.5
    public float RingRadius { get; }
    public float FanDegrees { get; } // (0, 180]
    public float SpreadTimeout { get; }
    public float ZoneLength { get; }
    public float ZoneHalfWidth { get; }
    public static float SlotOffsetDegrees(int agentId, float fanDegrees);
}
// EnemySpec(..., SkitterSpec skitter = null) last; EnemySpec.Skitter { get; }
// ChaserState.Spread
// ChaserBehaviour.LockedFacing { get; }  // Vector2; zero outside a wind-up
// ChaserBehaviour.SpreadArriveDistance = 0.75f (public const)
```

## Behaviour

1. No `SkitterSpec`: unchanged; Frog and Bullfrog tests stay green.
2. `SlotOffsetDegrees`: `agentId % 4` → −½, −⅙, +⅙, +½ of the fan; negative ids throw.
3. Leaving Idle a skitter enters `Spread`, fixing `u` = its bearing from the quarry turned by its slot offset.
4. `Spread` walks straight at `MoveSpeed.Value` to quarry + `RingRadius·u` (goal follows the quarry), facing travel; → `Chase` within `SpreadArriveDistance` of it or after `SpreadTimeout`; → `Windup` once the quarry is inside `Reach`.
5. `Spread` runs once per aggro; `Recover` → `Chase`.
6. A skitter's `Windup` locks `LockedFacing` to `DirectionToPlayer` on entry, stands on it, never turns or cancels; `EnemyTelegraph` as today.
7. Its `Strike` hits the quarry (hero or pet) only inside the strip along `LockedFacing`: 0 ≤ along ≤ `ZoneLength`, |across| ≤ `ZoneHalfWidth`, XZ. `ContactDamage.Value`; `HitQuarry`'s decoy and pet rules hold.
8. `Reset()` clears `u` and `LockedFacing`.
9. A skitter's `Tick` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `NoSkitter_ChaserUnchanged` | Frog spec / aggro / `Chase`, never `Spread` |
| `SlotOffset_FourSlots` | ids 0–4, 120 / map / −60, −20, +20, +60, −60; −1 throws |
| `Spread_FourIds_FourBearings` | four at one point / tick / goals 4 m out, ≥ 40° apart |
| `Spread_GoalFollowsHero` | hero moves 2 m / tick / goal moves 2 m |
| `Spread_Arrives_Chases` | at its goal / tick / `Chase` |
| `Spread_Timeout_Chases` | hero retreats so the goal is never reached / 3 s / `Chase` |
| `Spread_HeroInReach_WindsUp` | hero steps within 1.5 m / tick / `Windup` |
| `Recover_ReturnsToChase_NotSpread` | full strike cycle / recover ends / `Chase` |
| `Windup_LocksFacing_NoCancel` | hero circles to 3 m / wind-up / facing held, strike fires |
| `Strike_InStrip_Hits` | hero 1.6 m ahead, 0.4 m across / strike / 8 damage |
| `Strike_SideStepped_Misses` | hero 0.6 m across / strike / no damage |
| `Strike_PetInStrip_HitsPet` | pet quarry inside strip / strike / pet damaged |
| `Reset_ClearsSpreadAndFacing` | mid-wind-up / `Reset` / Idle, facing zero |
| `Tick_Skitter_NoAlloc` | spread → strike / 200 ticks / `AllocationAssert.None` |

## Out of scope

- Assets, model, view, roster (SS-04b–d). `LaneSystem`: its 4 lanes would drop pack strikes.
- Spawn grouping; packs are what waves buy.
- Pathing round cover in `Spread`; the timeout covers it.

## As built

_Commit body._
