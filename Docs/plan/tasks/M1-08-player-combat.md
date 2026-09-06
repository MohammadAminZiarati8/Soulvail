# M1-08 — `PlayerCombat`: health, targeting, `CombatBlackboard`, combat events; `RunSession` composes

**Size:** M · **Depends on:** M1-02, M1-04, M1-06 · **Branch:** `m1-08-player-combat`
**Design refs:** AR §3, §5 (`Combat`), §9; CC §3; ADR-0005, ADR-0008

## Goal

The player has a combat brain: health that can be hurt, a targeter fed from real enemies, a blackboard future triggers will read, and events the HUD and reticle will render. The motor now faces the target.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/CombatBlackboard.cs` | Core | Player perception for triggers |
| `Core/Combat/PlayerCombat.cs` | Core | Health + Targeter + candidates + events |
| `Core/Events/CombatEvents.cs` | Core | `TargetChanged`, `PlayerDamaged`, `PlayerDied`, `PlayerShieldChanged` |
| `Tests/Core/Combat/PlayerCombatTests.cs` | Tests.Core | Every rule |
| *small edits* | | `PlayerMotor` + `Stat Speed` (replaces the raw float; accel/decel rates scale with `Speed.Value`) and `Stop()`; `RunSession` creates and ticks `PlayerCombat`, passes `FaceDirection` to the motor; `RunState` + `Combat` |

## Public API

```csharp
namespace Soulvail.Core.Combat;

public sealed class CombatBlackboard
{
    public float HpFraction, ShieldFraction;
    public int   EnemiesWithin6m, EnemiesWithin8m, EnemiesInAcquireRange;
    public int   CurrentTargetId;          // -1 none
    public bool  IsTargetBlocked, HasFocus;
    public float StationaryTime;           // seconds with |MoveInput| == 0
    public float Veilrot;                  // 0 until M6-04
    public int   IncomingProjectiles;      // 0 until M2-07
    public void Reset();
}

public sealed class PlayerCombat
{
    public PlayerCombat(CharacterSpec spec, IDomainEvents events, int enemyCapacity);

    public Health           Health     { get; }
    public Targeter         Targeter   { get; }
    public CombatBlackboard Blackboard { get; }
    public bool             IsDead     => Health.IsDead;
    public Vector3?         FaceDirection { get; }     // unit XZ toward Current target; null when none
    public float            DpsOneSecond  { get; }     // 0 until M1-10 sets it from the weapon

    public DamageResult ApplyDamage(float amount, float now);
    public void Tick(float dt, float now, WorldSnapshot snapshot, ReadOnlySpan<EnemyAgent> enemies);
    public void Reset();
}
```

```csharp
namespace Soulvail.Core.Events;

public readonly struct TargetChanged      { public readonly int Id; public readonly bool IsFocused; public readonly bool IsBlocked; }
public readonly struct PlayerDamaged      { public readonly float ToShield, ToHp, HpFraction, ShieldFraction; public readonly bool Blocked; }
public readonly struct PlayerDied         { public readonly float Time; }
public readonly struct PlayerShieldChanged{ public readonly float Fraction; }
```

## Behaviour

1. Construction: `Health(new Stat(spec.MaxHp), spec.Shield, spec.HitIFrames)`, `Targeter(new TargetScorer(spec.Targeting), spec.Targeting)`, a preallocated `TargetCandidate[enemyCapacity]`.
2. `Tick`, in order: build candidates from `enemies` (distance = XZ distance from `snapshot.PlayerPosition`; `IsVulnerable = agent.IsAlive && agent.IsVulnerable`; `Hp = agent.Health.Current`) → `Targeter.Tick(dt, candidates, DpsOneSecond)` → if `Targeter.ChangedThisTick` publish `TargetChanged` → `Health.Tick(dt, now)`; if `ShieldFraction` changed by ≥ 0.005 publish `PlayerShieldChanged` → fill the blackboard (counts, target, `StationaryTime` += dt when `|snapshot.MoveInput| == 0`, else 0).
3. `FaceDirection`: unit XZ from player to the current target's position when `CurrentTargetId >= 0` (blocked or not — hold facing on blocked targets, CC §3.6); otherwise null.
4. `ApplyDamage`: result from `Health`; publish `PlayerDamaged` with the result and current fractions (also when `Blocked`, so the HUD can flash "blocked"); if `Killed`, publish `PlayerDied(now)` exactly once. Damage after death → `None`, no events.
5. `RunSession.Tick` order is now: `Time += dt` → `Enemies.Ingest` → `Combat.Tick(dt, Time, snapshot, Enemies.Registry.Alive)` → `Enemies.Tick(dt, Time)` → `Motor.Tick(dt, MoveInput, Combat.FaceDirection)` → `intents.PlayerMove`. Player death does not end the run yet (M1-17 wires that).
6. `PlayerMotor.Speed` is a `Stat` with base `spec.Speed`; rates use `Speed.Value`; M0-07 tests still pass unchanged.
7. `Tick` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `Tick_BuildsCandidates_FromEnemies` | 3 agents at known positions / Tick / targeter picked per M1-03 rules (assert `CurrentTargetId`) |
| `Tick_PublishesTargetChanged_OnlyOnChange` | stable target / Tick ×5 / exactly one `TargetChanged` (the first) |
| `FaceDirection_PointsAtTarget` | player (0,0,0), target (3,0,4) / Tick / (0.6, 0, 0.8) |
| `FaceDirection_NullWithoutTarget` | no enemies / Tick / null |
| `FaceDirection_HeldOnBlockedTarget` | only invulnerable enemy / Tick / non-null, `IsTargetBlocked` |
| `ApplyDamage_PublishesPlayerDamaged_WithFractions` | 140/30 / ApplyDamage(20) / event ToShield 20, HpFraction 1, ShieldFraction 1/3 |
| `ApplyDamage_Blocked_StillPublishes` | i-frames active / ApplyDamage / event with `Blocked` |
| `ApplyDamage_Kill_PublishesPlayerDiedOnce` | hp 5 / ApplyDamage(50) twice / one `PlayerDied`; second call `None` |
| `Tick_PublishesShieldChanged_WhileRecharging` | shield 0 past delay / Tick 0.1 / `PlayerShieldChanged` fraction 0.05 |
| `Blackboard_CountsByDistance` | enemies at 3, 7, 11, 20 m; range 12 / Tick / within6 1, within8 2, inRange 3 |
| `Blackboard_StationaryTime_AccumulatesAndResets` | input zero 0.5 s, then (1,0) / — / 0.5 then 0 |
| `RunSession_MotorFacesTarget` | one enemy at +X, no input / Tick 1 s / `Motor.Facing ≈ (1,0,0)` |
| `Reset_ClearsAll` | damaged, targeted / Reset / full health, no target, blackboard zero |
| `Tick_AllocatesNothing` | 32 enemies, warm-up / 10 000 ticks / allocated-bytes delta == 0 |

## Acceptance

- [ ] All tests green (including unchanged M0-07 motor tests)
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Focus commands and the reticle — M1-09. Weapon — M1-10. Anything that damages the player — M1-18.

## As built

_Filled at merge._
