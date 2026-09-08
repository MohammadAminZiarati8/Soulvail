using System;

namespace Soulvail.Core.Content;

/// <summary>
/// How a class moves, as authored numbers. Handed to <c>PlayerMotor</c>, which turns them into
/// a velocity and a facing every frame. See CC §2.4, §2.5 and §7.
/// </summary>
/// <remarks>
/// <para>
/// Immutable, and deliberately raw <see cref="float"/>s. A <c>Stat</c> stack exists as of
/// M1-01, but it belongs to the player rather than to the authored data: M1-08 gives
/// <c>PlayerCombat</c> a <c>Stat</c> for speed, seeded from <see cref="Speed"/>, so modifiers
/// apply to the live character while this stays what a designer typed.
/// </para>
/// <para>
/// The Oathbound's values (CC §7): speed 5.4, accel 0.06, decel 0.08, turn 720.
/// </para>
/// </remarks>
public sealed class MovementSpec
{
    /// <param name="speed">Top speed, m/s.</param>
    /// <param name="accelTime">Seconds from rest to <paramref name="speed"/>.</param>
    /// <param name="decelTime">Seconds from <paramref name="speed"/> to rest.</param>
    /// <param name="turnSpeedDeg">Yaw rate, degrees per second.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any value is not greater than zero. The two times are divisors — a zero would make the
    /// motor's acceleration rate infinite — and a non-positive speed or turn rate describes a
    /// character that cannot move or cannot aim, which is a content mistake rather than a
    /// state worth supporting.
    /// </exception>
    public MovementSpec(float speed, float accelTime, float decelTime, float turnSpeedDeg)
    {
        Speed = Positive(speed, nameof(speed));
        AccelTime = Positive(accelTime, nameof(accelTime));
        DecelTime = Positive(decelTime, nameof(decelTime));
        TurnSpeedDeg = Positive(turnSpeedDeg, nameof(turnSpeedDeg));
    }

    /// <summary>Top speed, in metres per second.</summary>
    public float Speed { get; }

    /// <summary>Seconds to go from rest to <see cref="Speed"/> at full stick.</summary>
    public float AccelTime { get; }

    /// <summary>Seconds to go from <see cref="Speed"/> to rest once the stick is released.</summary>
    public float DecelTime { get; }

    /// <summary>Yaw rate in degrees per second, about Y.</summary>
    public float TurnSpeedDeg { get; }

    /// <remarks>
    /// Written as <c>!(value &gt; 0f)</c> rather than <c>value &lt;= 0f</c> so that NaN is
    /// rejected as well. Every comparison against NaN is false, so the natural spelling waves
    /// it through, and one NaN reaching the motor turns its velocity and facing to NaN on the
    /// first tick — permanently, since NaN survives every later arithmetic operation.
    /// </remarks>
    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be greater than zero.");
        }

        return value;
    }
}
