# SS-06a — The beetle in core: a slow rear turn and a shot it can drop

**Size:** M · **Depends on:** — · **Branch:** `ss-06a-beetle-core`
**Design refs:** GD §8.6, §8.1

## Goal

`SpitterBehaviour` honours an optional `BombardierSpec`: planted, it turns its rear at 45°/s, braces, and drops a shot the hero circled out of.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/BombardierSpec.cs` | Core | `SlingerSpec`'s shape |
| `Core/Ai/SpitterBehaviour.cs` | Core | rear turn, start cone, braced aim, drop |
| `Tests/Core/Ai/BombardierBeetleTests.cs` | Tests.Core | |
| *small edits* | | `EnemySpec` param + property + guard; `EnemyEvents.cs`: `EnemyAimDropped` |

## Public API

```csharp
namespace Soulvail.Core.Content;
public sealed class BombardierSpec
{
    public BombardierSpec(float turnRateDegrees, float startConeDegrees, float dropConeDegrees); // 45, 10, 20
    public float TurnRateDegrees { get; }   // > 0
    public float StartConeDegrees { get; }  // (0, 180)
    public float DropConeDegrees { get; }   // [start, 180)
}
// EnemySpec(..., SlingerSpec slinger = null, BombardierSpec bombardier = null) last; EnemySpec.Bombardier { get; }
// public readonly struct EnemyAimDropped { public readonly int Id; public EnemyAimDropped(int id); }
```

## Behaviour

1. No `BombardierSpec`: unchanged; every Spitter test stays green.
2. `EnemySpec` refuses a `BombardierSpec` on a non-Spitter, or with a `SlingerSpec` (validation rows).
3. In `Approach` a bombardier walks in facing its way, as built. Inside `StandoffRange` it plants and never walks backwards; it walks again only when the quarry is beyond the band, as built.
4. Planted (and in `Recover`), its facing turns toward −`DirectionToPlayer` (rear to the quarry) by at most `TurnRateDegrees × dt`, the short way round.
5. It enters `Aim` only with reload done, line of sight and the rear within `StartConeDegrees` of the quarry; `EnemyTelegraph` is published then, as built.
6. In `Aim` its facing is locked (braced).
7. At release, the quarry more than `DropConeDegrees` off the rear: no `ProjectileFired`, one `EnemyAimDropped`, back to planted turning with no reload. Otherwise it fires one glob at the quarry's position (no lead) and enters `Recover`.
8. `Reset()` returns to Idle with the facing as spawned.
9. A bombardier's `Tick` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `NoBombardier_SpitterUnchanged` | Pitcher spec / hero at 9 m / backs out as built, never braces |
| `Spec_OnChaserOrWithSlinger_Throws` | Chaser + bombardier; both specs / construct / throws |
| `Planted_HeroClose_HoldsGround` | standoff 12, hero at 6 m / 2 s / velocity zero |
| `Planted_TurnsRearAtRate` | rear 90° off / 1 s / 45° turned, short way |
| `AboutFace_TakesAboutFourSeconds` | head-on at plant / tick / first `EnemyTelegraph` after ≥ 3.7 s |
| `OutsideStartCone_NeverAims` | rear 30° off, hero circling to hold it / 3 s / no `EnemyTelegraph` |
| `InsideStartCone_Aims` | rear 5° off, LOS, reloaded / tick / `Aim`, one `EnemyTelegraph` 0.7 |
| `NoSight_NeverAims` | rear on hero, no LOS / 3 s / no `EnemyTelegraph` |
| `Aim_FacingLocked` | in `Aim`, hero moves 15° / tick / facing unchanged |
| `Release_HeroInCone_FiresAtHero` | hero 12 m, 10° off at release / release / `ProjectileFired.Target` = hero position, `Recover` |
| `Release_HeroCircledOut_Drops` | hero 25° off at release / release / no `ProjectileFired`, one `EnemyAimDropped`, planted, no reload |
| `CirclingAt5m_DropsEveryShot` | hero circling at 5 m, 3.0 m/s / 15 s / zero `ProjectileFired` |
| `StillHeroAt12m_Fires` | hero still at 12 m / 6 s / ≥ 1 `ProjectileFired` |
| `Reset_FromAim_Idle` | mid-aim / `Reset` / Idle |
| `Tick_Bombardier_NoAlloc` | plant → turn → aim → drop → fire / 300 ticks / `AllocationAssert.None` |

## Out of scope

- Assets, model, view, roster (SS-06b–d).
- Turning while walking; a close-range answer; a lead.

## As built

_Commit body._
