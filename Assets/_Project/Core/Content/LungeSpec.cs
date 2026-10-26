using System;

namespace Soulvail.Core.Content;

/// <summary>
/// How a Lunger dashes: when it starts, how far and how fast it goes, how wide the lane that warns of
/// it is, and how long until the next one. GD §8.1's Lunger, the Jungle's panther (JG-07a).
/// </summary>
/// <remarks>
/// <para>
/// Absent on an archetype that does not dash, for <see cref="ExplosionSpec"/>'s reasons: a null block
/// is the one unambiguous "not this one", and the lane bites for the agent's <c>ContactDamage</c> so
/// depth scaling and affixes reach it without a second mechanism. The wind-up is the archetype's
/// <c>WindupTime</c> and the stand after it its <c>RecoverTime</c>, for the same reason.
/// </para>
/// <para>
/// <b><see cref="Range"/> may not exceed <see cref="Length"/></b>: a Lunger that starts from further
/// away than it dashes warns of a lane that stops short of where the player was, and the read is
/// that the strip reaches you.
/// </para>
/// </remarks>
public sealed class LungeSpec
{
    /// <param name="range">Metres within which it starts a wind-up. 9 for the panther.</param>
    /// <param name="length">Metres the dash and its lane run. 12 for the panther.</param>
    /// <param name="speed">Metres per second while dashing. 24 for the panther.</param>
    /// <param name="halfWidth">Half the lane's width, in metres. 0.6 for the panther.</param>
    /// <param name="cooldown">
    /// Seconds from one dash's end to the next wind-up. 2 for the panther. Zero is legal.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="range"/>, <paramref name="length"/>, <paramref name="speed"/> or
    /// <paramref name="halfWidth"/> is not a finite number greater than zero;
    /// <paramref name="cooldown"/> is negative, NaN or infinite; or <paramref name="range"/> is
    /// greater than <paramref name="length"/>.
    /// </exception>
    public LungeSpec(float range, float length, float speed, float halfWidth, float cooldown)
    {
        Range = Positive(range, nameof(range));
        Length = Positive(length, nameof(length));
        Speed = Positive(speed, nameof(speed));
        HalfWidth = Positive(halfWidth, nameof(halfWidth));

        if (!(cooldown >= 0f) || float.IsInfinity(cooldown))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cooldown),
                cooldown,
                "cooldown must be a finite number, zero or more.");
        }

        Cooldown = cooldown;

        if (range > length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(range),
                range,
                $"range must not exceed length ({length}): a lane that stops short of where the "
                    + "player stood warns of a dash that cannot reach them.");
        }

        DashSeconds = length / speed;
    }

    /// <summary>Metres within which it starts a wind-up.</summary>
    public float Range { get; }

    /// <summary>Metres the dash and its lane run.</summary>
    public float Length { get; }

    /// <summary>Metres per second while dashing.</summary>
    public float Speed { get; }

    /// <summary>Half the lane's width, in metres.</summary>
    public float HalfWidth { get; }

    /// <summary>Seconds from one dash's end to the next wind-up.</summary>
    public float Cooldown { get; }

    /// <summary>How long a dash lasts: <see cref="Length"/> over <see cref="Speed"/>.</summary>
    public float DashSeconds { get; }

    /// <remarks><see cref="ExplosionSpec"/>'s spelling, so NaN is refused too (AR §18.3).</remarks>
    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero. An archetype that does "
                    + "not dash carries no LungeSpec at all.");
        }

        return value;
    }
}
