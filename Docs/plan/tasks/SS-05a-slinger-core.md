# SS-05a — The slinger in core: a led throw and a retreat that cannot throw

**Size:** M · **Depends on:** — · **Branch:** `ss-05a-slinger-core`
**Design refs:** GD §8.5, §8.1

## Goal

`SpitterBehaviour` honours an optional `SlingerSpec`: a shot led up to 3 m, and a slow retreat when rushed that cannot throw.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/SlingerSpec.cs` | Core | `SkitterSpec`'s shape |
| `Core/Ai/SpitterBehaviour.cs` | Core | `Backoff` state, led release |
| `Tests/Core/Ai/JackalSlingerTests.cs` | Tests.Core | |
| *small edits* | | `EnemySpec` param + property + guard; `SpitterState.Backoff`; `EnemyBlackboard.PlayerVelocity`; `EnemySystem.Perceive` fills it |

## Public API

```csharp
namespace Soulvail.Core.Content;
public sealed class SlingerSpec
{
    public SlingerSpec(float maxLead, float retreatSpeedFraction, float retreatTime); // 3, 0.7, 3
    public float MaxLead { get; }              // > 0
    public float RetreatSpeedFraction { get; } // (0, 1]
    public float RetreatTime { get; }          // > 0
}
// EnemySpec(..., SlingerSpec slinger = null) last; EnemySpec.Slinger { get; }
// SpitterState.Backoff
// EnemyBlackboard.PlayerVelocity  // Vector2, XZ; zero for a decoy or pet quarry
// public static Vector3 SpitterBehaviour.LeadPoint(Vector3 self, Vector3 quarry,
//     Vector2 velocity, float projectileSpeed, float maxLead)
```

## Behaviour

1. No `SlingerSpec`: unchanged; Pitcher, Thorn pitcher and Mother tests stay green.
2. `EnemySpec` refuses a `SlingerSpec` on a non-Spitter (validation row).
3. `Ingest` sets `PlayerVelocity` to the snapshot's XZ velocity for a hero quarry; else, or non-finite, zero.
4. A slinger in `Approach` with the quarry inside `StandoffRange × RetreatFraction` enters `Backoff` (timer 0).
5. `Backoff` walks straight away at `MoveSpeed.Value × RetreatSpeedFraction`, facing the quarry; it never aims.
6. `Backoff` → `Approach` once distance ≥ `StandoffRange`; after `RetreatTime` → `Aim` if it has line of sight, else it stays in `Backoff` with the timer reset.
7. `LeadPoint` = quarry + velocity × (XZ distance / speed), the offset clamped to `maxLead`, quarry's Y.
8. A slinger's `Release` aims at `LeadPoint`; fan offsets apply round it.
9. `Recover` is unchanged (rooted); `Reset()` returns to Idle and clears the backoff timer.
10. A slinger's `Tick` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `NoSlinger_SpitterUnchanged` | Pitcher spec / hero at 9 m / `Approach` walking away, never `Backoff` |
| `Ingest_HeroQuarry_CarriesVelocity` | hero moving 3 m/s east / ingest / (3, 0) |
| `Ingest_PetOrNaN_ZeroVelocity` | pet quarry; NaN velocity / ingest / zero both |
| `Rushed_EntersBackoff` | standoff 12, hero 8 m / tick / `Backoff` |
| `Backoff_WalksAwaySlowly_FacesHero` | walk 2.8 / tick / velocity 1.96 away, facing hero |
| `Backoff_NeverAims` | hero held at 6 m, LOS / 2.9 s / no `EnemyTelegraph`, no shot |
| `Backoff_Clear_ReturnsToApproach` | hero at 12 m / tick / `Approach` |
| `Backoff_Timeout_Throws` | hero held at 6 m / 3 s + wind-up / one shot fired |
| `Backoff_TimeoutNoSight_KeepsBacking` | no LOS / 3 s / `Backoff`, timer 0, no shot |
| `LeadPoint_Still_IsQuarry` | zero velocity / call / quarry |
| `LeadPoint_Running_LeadsByFlight` | 6 m, speed 6, 1 m/s / call / 1 m ahead |
| `LeadPoint_Capped` | 12 m, speed 7, 3.2 m/s / call / 3 m ahead |
| `Release_Slinger_TargetsLeadPoint` | hero running / release / `ProjectileFired.Target` = lead point |
| `Reset_FromBackoff_Idle` | mid-backoff / `Reset` / Idle, timer 0 |
| `Tick_Slinger_NoAlloc` | backoff → throw / 300 ticks / `AllocationAssert.None` |

## Out of scope

- Assets, model, view, roster (SS-05b–d).
- Pathing round cover while backing (the timeout covers it); clamping the lead to the arena; leading a pet or decoy.

## As built

_Commit body._
