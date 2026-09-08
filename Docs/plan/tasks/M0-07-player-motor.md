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
| `Tick_AllocatesNothing` | warm-up / 10 000 ticks / allocates nothing (`AllocationAssert`) |

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

Four files, not three, and 19 tests, not 17.

1. **A fourth file, `Tests/Core/Content/ContentTests.cs`.** `Spec_NonPositive_Throws` tests `MovementSpec`, a `Core/Content` type, and the Files table filed it under the motor's fixture. Rather than a dedicated `MovementSpecTests.cs`, this is the fixture **M0-08 already specifies** for the Content module ("all four types — one module, one test file"), created one task early with its first Content type in it. M0-08's Files table is annotated to add to it rather than create it.
2. **Two tests beyond the Tests table.** `Facing_ZeroFaceDirection_FallsBackToVelocity` covers decision 4 below, which the Behaviour section does not decide. `Spec_StoresValues` guards argument transposition in a four-`float` constructor — swapping `accelTime` and `decelTime` passes every other test in the suite.
3. **Behaviour rule 5 extended: a `faceDirection` with no ground direction falls back to the velocity rule.** `Vector3?` lets a caller pass `Vector3.Zero` or a purely vertical vector; normalising either is NaN, and a NaN facing never recovers. "Provided" therefore means "provided a direction". Not deferrable the way M0-06 deferred its zero-`Facing` question — the null case here is silent, permanent corruption rather than an open design question.
4. **`Tick` integrates velocity before facing**, which the spec does not order and which is load-bearing: from rest at full stick the speed passes rule 5's 0.05 threshold in 0.56 ms, so turning first would discard a frame of rotation every time the player starts moving.
5. **`Tick` treats `dt <= 0` as "no time passed"**, extending rule 7 from `dt == 0`. A negative `dt` would accelerate backwards and turn the wrong way.
6. **`MovementSpec` rejects NaN as well as non-positive**, spelled `!(value > 0f)` — the natural `value <= 0f` waves NaN through, and one NaN reaches every later frame.
7. **Tick sizes vary by test.** Two specced durations are not whole 1/120 s ticks (0.03 s is 3.6, 0.0625 s is 7.5), so those tests use sizes summing to the specced duration exactly.
8. `Facing_RotatesAtTurnSpeed` reaches full speed with the aim pinned to its current facing before releasing it, so it measures the turn rate rather than the accel/facing interaction of decision 4.
9. The Tests table's allocation row was reworded from "allocated-bytes delta == 0" to "allocates nothing (`AllocationAssert`)" — as were M0-10's and M0-16's, the only other M0 specs still carrying the phrase. See PROGRESS for why the `_TEMPLATE.md` gloss was not enough.
10. `PlayerMotor`'s constructor throws `ArgumentNullException` on a null spec. Not in the Public API and not given a row; a conventional guard that replaces a `NullReferenceException`.
