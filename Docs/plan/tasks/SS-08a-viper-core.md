# SS-08a — The viper in core: a long recoil on a miss and a sidewind

**Size:** M · **Depends on:** — · **Branch:** `ss-08a-viper-core`
**Design refs:** GD §8.8, §8.1

## Goal

`LungerBehaviour` honours an optional `SidewinderSpec`: a miss recoils 2 s; after each recoil it slides 45° round the hero, sides alternating.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/SidewinderSpec.cs` | Core | `PackSpec`'s shape |
| `Core/Ai/LungerBehaviour.cs` | Core | landed check, long recoil, `Sidewind` state |
| `Tests/Core/Ai/SidewinderViperTests.cs` | Tests.Core | |
| *small edits* | | `EnemySpec` param + property + guards; `EnemyEvents.cs`: `EnemyStrikeMissed` |

## Public API

```csharp
namespace Soulvail.Core.Content;
public sealed class SidewinderSpec  // PackSpec's validation: finite, > 0; arc in (0, 180]
{
    public SidewinderSpec(float missRecoverTime, float speed, float arcDegrees, float radius, float timeout); // 2, 2.8, 45, 7, 2.5
    public float MissRecoverTime { get; } public float Speed { get; } public float ArcDegrees { get; }
    public float Radius { get; } public float Timeout { get; }
}
// EnemySpec(..., BroodSpec brood = null, SidewinderSpec sidewinder = null) last; EnemySpec.Sidewinder { get; }
// LungerState.Sidewind, appended.
// public readonly struct EnemyStrikeMissed { int Id; float Seconds; }
```

## Behaviour

1. No `SidewinderSpec`: unchanged; `LungerBehaviourTests` and `ShadowPantherPackTests` stay green.
2. `EnemySpec` refuses a `SidewinderSpec` on a non-Lunger, beside a `PackSpec`, with `Radius ≥ Lunge.Range`, or with `MissRecoverTime < RecoverTime`.
3. On the tick the wind-up ends it is **landed** when the quarry's XZ position lies inside the strip it opened, `Lane`'s test. A pet quarry is always landed.
4. Landed: `Recover` lasts `RecoverTime`, as built. Missed: one `EnemyStrikeMissed(id, MissRecoverTime)` on that tick, and `Recover` lasts `MissRecoverTime`.
5. After `Recover` a sidewinder enters `Sidewind`, not `Stalk`: it moves at `Speed` along the tangent round the quarry on its side, steered toward `Radius` from it, facing the quarry.
6. Its side alternates every strike; the first is left (counter-clockwise from above) on an even id, right on an odd.
7. `Sidewind` ends once it has swung `ArcDegrees` round the quarry, or after `Timeout`; then `Stalk`, which coils at once when in range and off cooldown, as built.
8. `Reset()` returns to `Stalk`, clearing side, arc and landed flag.
9. A sidewinder's `Tick` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `NoSidewinder_LungerUnchanged` | panther spec / full cycle / `Recover` → `Stalk`; no `EnemyStrikeMissed` |
| `Spec_BadPairings_Throw` | each case of rule 2 / construct / throws |
| `HeroInStrip_Landed_ShortRecoil` | hero still, or backing 2 m down the lane / coil ends / no `EnemyStrikeMissed`; `Recover` 1 s |
| `HeroStepsOut_Missed_LongRecoil` | hero 1 m sideways in the coil / coil ends / one `EnemyStrikeMissed(id, 2)`; `Recover` 2 s |
| `PetQuarry_AlwaysLanded` | quarry is a pet, off the line / dash / no `EnemyStrikeMissed` |
| `AfterRecover_Sidewinds` | recover ends / tick / `Sidewind`, 2.8 m/s, facing the hero |
| `Sidewind_SteersTowardRadius` | starts 4 m out / sidewind / ends within 7 ± 0.5 m |
| `Sidewind_EndsAtArc` | still hero / sidewind / ends having swung 45 ± 3° |
| `Sidewind_EndsAtTimeout` | position held / 2.5 s / `Stalk` |
| `Sides_AlternateFromIdParity` | even id, two cycles / read swing sign / CCW then CW; odd id the reverse |
| `StillHero_LanesDifferByTheArc` | still hero, two strikes / compare lane directions / ≥ 40° apart |
| `Reset_MidSidewind_Stalks` | sidewinding / `Reset` / `Stalk`, side by id parity |
| `Tick_Sidewinder_NoAlloc` | coil → strike → miss → sidewind / 300 ticks / `AllocationAssert.None` |

## Out of scope

- Model, view, data, roster (SS-08b–d).
- Walls in core (the timeout ends a blocked sidewind); packs; Elites.

## As built

_Commit body._
