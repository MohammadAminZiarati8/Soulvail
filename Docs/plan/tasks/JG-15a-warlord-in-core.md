# JG-15a — The Mossguard Warlord in core: guard, turn, glaive sweep

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §9.1, §9.2; AR §18.2, §18.4 (telegraph is a promise; no behaviour knows CC)

## Goal

Stage 15's boss in core: a big Mossguard that guards its front, turns slowly, walks in and sweeps
its glaive.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/WarlordBehaviour.cs` | Core | **New.** Inner boss behaviour: guard, turn, approach, sweep |
| `Core/Events/WarlordEvents.cs` | Core | **New.** `WarlordGuardArc`; 15b–d add theirs |
| `Tests/Core/Ai/WarlordBehaviourTests.cs` | Tests.Core | **New** |
| *small edits* | | `RunSession._bossBehaviours` row `boss.mossguard_warlord`; `BossDispatchTests` row |

## Public API

```csharp
public enum WarlordMove { None, Sweep = 1, Charge = 2, WarCry = 3, Lob = 4, Combo = 5, ComboSecond = 6 }
public sealed class WarlordBehaviour : IEnemyBehaviour, IBossPhaseListener {
  public const float HalfArcDeg = 60f, TurnRateDeg = 40f, WalkConeDeg = 45f;
  public const float AttackGapSeconds = 1.4f, RecoverSeconds = 0.8f;
  public const float SweepWindupSeconds = 0.8f, SweepHalfAngleDeg = 60f, SweepReach = 3.5f,
                     SweepDamageScale = 1.5f;
  public WarlordBehaviour(EnemyAgent agent, ProjectileSystem projectiles, IRandomStream random,
                          ContentId guardId);
  public WarlordMove Move { get; }
  public float GuardHalfArcDeg { get; }
  public Vector2 Facing { get; }
}
public readonly struct WarlordGuardArc { int BossId; float HalfArcDeg; } // WarlordEvents.cs
```

## Behaviour

1. **Guard** (Bulwark's rule): while the player's XZ direction is within `GuardHalfArcDeg` of
   `Facing`: externally invulnerable and `IsVulnerable` false; outside, both lowered. `GuardHalfArcDeg` 0 means never guarded.
2. **Turn:** faces the player on the first tick; then turns toward the path at ≤ `TurnRateDeg`/s,
   only while approaching.
3. **Approach:** walks at `MoveSpeed` along `Facing` only while the path lies within
   `WalkConeDeg`; otherwise stands and turns.
4. **Sweep:** when the player is within `SweepReach` and `AttackGapSeconds` have passed since the
   last recovery, arm `Sweep`: stand, catch the aim (player direction), publish
   `EnemyTelegraph(id, 0.8, 1)` and `SprayArmed(id, origin, aim, 60, 3.5, 0.8)` — the cone
   cone Mother's spray draws.
5. **Release** at 0.8 s: the player is hit for `ContactDamage × 1.5` iff XZ distance ≤ 3.5 and
   within 60° of the caught aim (melee: no line of sight). Publish `SprayFired(id, hit)`;
   recover `RecoverSeconds` facing the aim.
6. `WarlordGuardArc` publishes on the first tick and whenever `GuardHalfArcDeg` changes.
7. **Phase entry** (`OnPhaseEntered`): an armed move is dropped (`SprayCleared` if a cone was armed),
   the gap restarts. **Reset** lowers the guard and returns to approach.
8. Registered in `RunSession` for `boss.mossguard_warlord` with `Misc` and `enemy.mossguard`.
   No per-tick allocation.

## Tests

| Test | Given / When / Then |
|---|---|
| `Guard_PlayerInFront_Invulnerable` / `Guard_PlayerBehind_Hurtable` / `Guard_Edge61_Hurtable` (1) |
| `Turn_IsAtMostFortyDegreesPerSecond` / `Turn_FrozenDuringWindup` (2) |
| `Approach_PathOutsideCone_Stands` / `Approach_PathInCone_Walks` (3) |
| `Sweep_InReachAfterGap_ArmsAndTelegraphsPointEight` / `Sweep_BeforeGap_DoesNotArm` (4) |
| `Sweep_InArc_HitsOnce` / `Sweep_Beside61_Misses` / `Sweep_Behind_Misses` / `Sweep_PlayerLeftReach_Misses` (5) |
| `GuardArc_PublishedFirstTickAndOnChange` (6) |
| `Phase_DropsArmedSweep_ClearsCone` / `Reset_LowersGuard` (7) |
| `Dispatch_WarlordBossId_BuildsWarlord` / `Tick_DoesNotAllocate` (8) |

## Out of scope

- Charge (15b), war cry (15c), lobs and shatter (15d), anything Unity (15e–g).

## As built

_Filled at merge._
