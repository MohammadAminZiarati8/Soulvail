# M0-07 — `MovementSpec` and `PlayerMotor`: accel/decel, no inertia, facing

**Size:** S · **Depends on:** M0-01 · **Branch:** `m0-07-player-motor`
**Design refs:** CC §2.4, §2.5, §7; AR §3

## Goal

The player's movement *logic* — how a shaped stick vector becomes a velocity and a facing — lives in core and is proven by tests before a capsule ever moves.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/MovementSpec.cs` | Core | Immutable movement numbers for a class |
| `Core/Combat/PlayerMotor.cs` | Core | Velocity + facing integration |
| `Tests/Core/Combat/PlayerMotorTests.cs` | Tests.Core | Every rule below |

## Public API

```csharp
namespace Soulvail.Core.Content;

public sealed class MovementSpec
{
    public float Speed        { get; }   // m/s
    public float AccelTime    { get; }   // s from rest to Speed
    public float DecelTime    { get; }   // s from Speed to rest
    public float TurnSpeedDeg { get; }   // deg/s
    public MovementSpec(float speed, float accelTime, float decelTime, float turnSpeedDeg);   // all > 0 else ArgumentOutOfRangeException
}
```

```csharp
namespace Soulvail.Core.Combat;
using System.Numerics;

public sealed class PlayerMotor
{
    public PlayerMotor(MovementSpec spec, Vector3 initialFacing);   // facing normalised; Y forced to 0; zero → +Z

    public Vector3 Velocity { get; }     // Y == 0
    public Vector3 Facing   { get; }     // unit, Y == 0

    /// moveInput: shaped stick, |v| <= 1, X → world X, Y → world Z.
    /// faceDirection: if non-null, rotate toward it instead of toward velocity.
    public void Tick(float dt, Vector2 moveInput, Vector3? faceDirection);
}
```

## Behaviour

1. Target velocity = `clampMagnitude(moveInput, 1) × Speed`, mapped to the XZ plane (`input.X → X`, `input.Y → Z`).
2. If `|moveInput| > 0`: `Velocity` moves linearly toward the target at `Speed / AccelTime` units per second. If `|moveInput| == 0`: toward zero at `Speed / DecelTime`.
3. `|Velocity|` never exceeds `Speed`.
4. Partial input settles at partial speed (`0.5` input → `0.5 × Speed`), because the target is proportional.
5. Facing: if `faceDirection` is provided, rotate toward it. Otherwise, if `|Velocity| > 0.05`, rotate toward the velocity direction. Otherwise hold the current facing.
6. Rotation is about Y at `TurnSpeedDeg` per second along the shorter arc, and snaps to the target when within one step.
7. `Tick(0, …)` changes nothing.
8. `Tick` allocates nothing.
9. Camera-relative mapping of the stick is **not** the motor's job; the snapshot's `MoveInput` is already in world XZ terms (M0-16).

Oathbound values (CC §7): Speed 5.4, AccelTime 0.06, DecelTime 0.08, TurnSpeedDeg 720. Tests use these.

## Tests

| Test | Given / When / Then |
|---|---|
| `Spec_NonPositive_Throws` | MovementSpec(0, …) etc. / — / `ArgumentOutOfRangeException` for each field |
| `FromRest_FullInput_ReachesSpeedWithinAccelTime` | rest / Tick(1/120) × 8 (0.0667 s) with input (0,1) / `|Velocity|` == 5.4 ± 1e-3 |
| `FromRest_FullInput_HalfwayAtHalfAccelTime` | rest / 0.03 s of ticks / `|Velocity|` == 2.7 ± 0.1 |
| `FullSpeed_ZeroInput_StopsWithinDecelTime` | at speed / 0.0833 s of ticks with zero input / `|Velocity|` < 1e-3 |
| `HalfInput_SettlesAtHalfSpeed` | rest / 1 s of ticks with (0, 0.5) / `|Velocity|` == 2.7 ± 1e-3 |
| `OversizedInput_IsClamped` | input (0, 3) / 1 s / `|Velocity|` == 5.4 |
| `InputMapsXToX_YToZ` | input (1, 0) / 1 s / Velocity ≈ (5.4, 0, 0); input (0, 1) → (0, 0, 5.4) |
| `Velocity_YIsAlwaysZero` | any / many ticks / `Velocity.Y == 0` |
| `Facing_FollowsVelocity_WhenNoFaceDirection` | facing +Z / 1 s moving +X / Facing ≈ (1, 0, 0) |
| `Facing_RotatesAtTurnSpeed` | facing +Z, moving +X / 0.0625 s / angle(Facing, +Z) == 45° ± 1° |
| `Facing_SnapsWhenWithinOneStep` | 1° off target / one Tick / exactly on target |
| `Facing_UsesFaceDirection_OverVelocity` | moving +X, faceDirection −Z / 1 s / Facing ≈ (0, 0, −1) |
| `Facing_HoldsLast_WhenIdle` | facing +X, rest / 1 s zero input, no faceDirection / Facing == (1, 0, 0) |
| `Facing_TakesShorterArc` | facing +Z, target −X (270° cw / 90° ccw) / small dt / rotated toward −X the short way |
| `Ctor_ZeroFacing_DefaultsToPlusZ` | initialFacing zero / — / Facing == (0, 0, 1) |
| `Tick_ZeroDt_NoChange` | any state / Tick(0, (0,1), null) / Velocity and Facing unchanged |
| `Tick_AllocatesNothing` | warm-up / 10 000 ticks / allocated-bytes delta == 0 |

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- `Stat`-backed speed (M1-01 replaces the raw float with a `Stat` so modifiers can apply).
- Charge / dash — M1-12.
- Focus (stationary detection) — M1-11.
- Position integration and collision — the body's job (M0-16).

## As built

_Filled at merge._
