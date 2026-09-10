using System;
using System.Numerics;
using Soulvail.Core.Content;

namespace Soulvail.Core.Combat;

/// <summary>
/// How a shaped stick vector becomes a velocity and a facing. The whole of the player's
/// movement logic, and none of its motion: this integrates, the body moves. See CC §2.4, §2.5
/// and AR §3.
/// </summary>
/// <remarks>
/// <para>
/// Position and collision are not here. M0-16's <c>PlayerView</c> takes the velocity out of a
/// <c>PlayerMoveIntent</c> and drives the character controller with it — which is what lets
/// this be a pure class with tests that run in milliseconds and no scene.
/// </para>
/// <para>
/// The stick arrives already in world XZ terms, deadzoned and shaped by the input layer.
/// Camera-relative mapping is the snapshot builder's job (M0-16), not the motor's: doing it
/// here would make the motor's output depend on where the camera is looking, which is exactly
/// the engine knowledge core is not allowed to have.
/// </para>
/// </remarks>
public sealed class PlayerMotor
{
    /// <summary>
    /// Below this speed the velocity's direction is numerical noise rather than intent, so the
    /// facing holds instead of following it. Without it a character coasting to a stop would
    /// twitch as the last few millimetres per second changed sign.
    /// </summary>
    private const float FacingVelocityThreshold = 0.05f;

    private const float DegreesPerRadian = 180f / MathF.PI;
    private const float RadiansPerDegree = MathF.PI / 180f;

    private readonly MovementSpec _spec;

    private Vector3 _velocity;
    private Vector3 _facing;

    /// <param name="initialFacing">
    /// Which way the character starts looking. Flattened to the ground plane and normalised; if
    /// nothing is left of it — zero, or purely vertical — it defaults to +Z.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    public PlayerMotor(MovementSpec spec, Vector3 initialFacing)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        Speed = new Stat(spec.Speed);
        _velocity = Vector3.Zero;
        _facing = TryFlattenToDirection(initialFacing, out Vector3 facing) ? facing : Vector3.UnitZ;
    }

    /// <summary>
    /// Top speed in metres per second, as a live <see cref="Stat"/> seeded from
    /// <see cref="MovementSpec.Speed"/>. Where "+10 % move speed" goes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The acceleration and deceleration rates are derived from it rather than cached, so a
    /// modifier that raises the top speed raises the ramp with it: the spec's two times are
    /// "seconds from rest to top speed", and a cached rate would keep the old metres-per-second-per
    /// -second and quietly stretch a 0.06 s ramp into a longer one every time a node made the
    /// character faster. Recomputing is two divisions a frame.
    /// </para>
    /// <para>
    /// Owned by the motor rather than by <c>PlayerCombat</c>, because the motor is the only thing
    /// that reads it and a stat held one object away from its single reader is a stat that can go
    /// stale in transit.
    /// </para>
    /// </remarks>
    public Stat Speed { get; }

    /// <summary>Current velocity in metres per second. Always on the ground plane: <c>Y == 0</c>.</summary>
    public Vector3 Velocity => _velocity;

    /// <summary>Current facing. Always a unit vector on the ground plane: <c>Y == 0</c>.</summary>
    public Vector3 Facing => _facing;

    /// <summary>
    /// Advances the motor by one frame.
    /// </summary>
    /// <param name="dt">Seconds since the last tick. Zero or less does nothing.</param>
    /// <param name="moveInput">
    /// The shaped stick, <c>|v| &lt;= 1</c>, in world terms: X maps to world X, Y maps to world
    /// Z. An oversized vector is clamped rather than trusted.
    /// </param>
    /// <param name="faceDirection">
    /// An explicit aim to turn toward, overriding the direction of travel. Null — or a vector
    /// with no ground direction left in it — falls back to facing the way the character moves.
    /// </param>
    /// <remarks>
    /// Velocity is integrated before facing, and the order matters. From rest at full stick the
    /// speed passes <see cref="FacingVelocityThreshold"/> in well under a millisecond, so
    /// turning first would spend the whole first frame looking at a velocity of zero and
    /// discard a frame of rotation every time the player starts moving.
    /// </remarks>
    public void Tick(float dt, Vector2 moveInput, Vector3? faceDirection)
    {
        // Also catches a negative dt, which would otherwise accelerate backwards and turn the
        // wrong way, and NaN, which would be unrecoverable. No time passed, nothing happens.
        if (!(dt > 0f))
        {
            return;
        }

        IntegrateVelocity(dt, moveInput);
        IntegrateFacing(dt, faceDirection);
    }

    /// <summary>
    /// Drops the velocity to zero immediately, leaving the facing alone.
    /// </summary>
    /// <remarks>
    /// Not deceleration — an instant stop, for the things that take movement away from the player
    /// rather than asking them to stop: the Charge of M1-14 replaces the velocity outright for its
    /// duration, and death (M1-17) ends it. The facing survives because neither of those turns the
    /// character round, and a stop that also reset the aim would swing the body on the frame the
    /// player was hit.
    /// </remarks>
    public void Stop()
    {
        _velocity = Vector3.Zero;
    }

    private void IntegrateVelocity(float dt, Vector2 moveInput)
    {
        // Floored at zero, because Stat deliberately clamps nothing — a PercentMult of −1 is a
        // legitimate way to say "this is now zero" — and a negative top speed here would drive the
        // character backwards while the ramp rate ran the wrong way too. The same floor, for the
        // same reason, as Health.ClampedMax.
        float speed = MathF.Max(0f, Speed.Value);

        float inputLength = moveInput.Length();
        Vector3 target;
        float rate;

        if (inputLength > 0f)
        {
            // Clamped to the unit disc, so a stick the input layer over-delivered cannot buy
            // extra speed. Scaling is proportional, which is what makes a half-pushed stick
            // settle at half speed rather than ramping to full.
            Vector2 clamped = inputLength > 1f ? moveInput / inputLength : moveInput;
            target = new Vector3(clamped.X * speed, 0f, clamped.Y * speed);
            rate = speed / _spec.AccelTime;
        }
        else
        {
            target = Vector3.Zero;

            // From the live speed rather than the authored one, so "0.08 s from top speed to rest"
            // stays true of whatever the top speed currently is.
            rate = speed / _spec.DecelTime;
        }

        // Linear, not exponential: an exponential approach never actually arrives, and "0.06 s
        // to top speed" is a number the designer can feel and time. Because the step never
        // overshoots the target and the target's magnitude is at most Speed, the velocity's
        // magnitude cannot exceed Speed either.
        _velocity = MoveTowards(_velocity, target, rate * dt);
    }

    private void IntegrateFacing(float dt, Vector3? faceDirection)
    {
        float maxDegrees = _spec.TurnSpeedDeg * dt;

        // An explicit aim wins over the direction of travel — that is what will later let the
        // player strafe around a target without the body swinging to follow the stick.
        if (faceDirection.HasValue && TryFlattenToDirection(faceDirection.Value, out Vector3 aim))
        {
            _facing = RotateTowards(_facing, aim, maxDegrees);
            return;
        }

        if (_velocity.Length() <= FacingVelocityThreshold)
        {
            return;
        }

        _facing = RotateTowards(_facing, Vector3.Normalize(_velocity), maxDegrees);
    }

    /// <summary>
    /// Moves <paramref name="current"/> along the straight line toward <paramref name="target"/>
    /// by at most <paramref name="maxDistance"/>, landing exactly on it rather than overshooting.
    /// </summary>
    private static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistance)
    {
        Vector3 delta = target - current;
        float distance = delta.Length();

        if (distance <= maxDistance)
        {
            return target;
        }

        return current + (delta * (maxDistance / distance));
    }

    /// <summary>
    /// Rotates <paramref name="current"/> about Y toward <paramref name="target"/> by at most
    /// <paramref name="maxDegrees"/>, along the shorter arc. Both must be unit vectors on the
    /// ground plane.
    /// </summary>
    private static Vector3 RotateTowards(Vector3 current, Vector3 target, float maxDegrees)
    {
        // On the XZ plane the dot product is the cosine of the angle from current to target and
        // this cross term is its sine. Feeding both to Atan2 gives a *signed* angle in
        // (-180°, 180°], so the shorter arc falls out of the sign and never needs a comparison
        // or a wrap-around special case.
        float cos = (current.X * target.X) + (current.Z * target.Z);
        float sin = (current.Z * target.X) - (current.X * target.Z);
        float angleDeg = MathF.Atan2(sin, cos) * DegreesPerRadian;

        if (MathF.Abs(angleDeg) <= maxDegrees)
        {
            // Assigned, not rotated by the remaining angle. Landing exactly on the target is
            // what stops a held aim from creeping around it by a rounding error per frame.
            return target;
        }

        float stepRad = (angleDeg < 0f ? -maxDegrees : maxDegrees) * RadiansPerDegree;
        float stepCos = MathF.Cos(stepRad);
        float stepSin = MathF.Sin(stepRad);

        return new Vector3(
            (current.X * stepCos) + (current.Z * stepSin),
            0f,
            (current.Z * stepCos) - (current.X * stepSin));
    }

    /// <summary>
    /// Drops the Y component and normalises what is left, reporting whether any ground
    /// direction survived.
    /// </summary>
    /// <remarks>
    /// The false case is the whole point. <c>Vector3.Normalize(Vector3.Zero)</c> is NaN, and a
    /// NaN facing never recovers — every subsequent frame stays NaN. So "provided a direction"
    /// has to mean more than "provided a vector": a zero or purely vertical aim is treated as
    /// no aim at all, and the caller falls back rather than being silently poisoned.
    /// </remarks>
    private static bool TryFlattenToDirection(Vector3 value, out Vector3 direction)
    {
        float x = value.X;
        float z = value.Z;
        float lengthSquared = (x * x) + (z * z);

        // Again spelled to reject NaN as well as zero.
        if (!(lengthSquared > 0f))
        {
            direction = default;
            return false;
        }

        float length = MathF.Sqrt(lengthSquared);
        direction = new Vector3(x / length, 0f, z / length);
        return true;
    }
}
