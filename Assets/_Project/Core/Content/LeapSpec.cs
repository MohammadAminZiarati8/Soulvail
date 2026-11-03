using System;

namespace Soulvail.Core.Content;

/// <summary>
/// How a Chaser leaps: the band it leaps from, how long it crouches and flies, how wide the ring it
/// bites on landing is, how hard, and how long until the next one. GD §8.2's Bullfrog (JG-21a).
/// </summary>
/// <remarks>
/// <para>
/// Absent on an archetype that does not leap, for <see cref="LungeSpec"/>'s reasons: a null block is
/// the one unambiguous "not this one". Honoured by <c>ChaserBehaviour</c> only. The bite is the
/// agent's <c>ContactDamage</c> times <see cref="DamageMultiplier"/>, so depth scaling reaches it,
/// and the stand after landing is the archetype's <c>RecoverTime</c>.
/// </para>
/// <para>
/// <b><see cref="MinRange"/> may not exceed <see cref="MaxRange"/></b>: an empty band is a leap that
/// can never start.
/// </para>
/// </remarks>
public sealed class LeapSpec
{
    /// <param name="minRange">Metres inside which it does not leap. 4 for the Bullfrog.</param>
    /// <param name="maxRange">Metres beyond which it does not leap. 9 for the Bullfrog.</param>
    /// <param name="crouchSeconds">Seconds it crouches, the landing spot announced. 0.6.</param>
    /// <param name="airSeconds">Seconds the flight takes, whatever its length. 0.5.</param>
    /// <param name="radius">Metres (XZ) of the ring it bites on landing. 1.6.</param>
    /// <param name="damageMultiplier">The bite as a multiple of <c>ContactDamage</c>. 1.5.</param>
    /// <param name="cooldown">Seconds from one landing to the next crouch. 4. Zero is legal.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any value but <paramref name="cooldown"/> is not a finite number greater than zero;
    /// <paramref name="cooldown"/> is negative, NaN or infinite; or <paramref name="minRange"/> is
    /// greater than <paramref name="maxRange"/>.
    /// </exception>
    public LeapSpec(
        float minRange,
        float maxRange,
        float crouchSeconds,
        float airSeconds,
        float radius,
        float damageMultiplier,
        float cooldown)
    {
        MinRange = Positive(minRange, nameof(minRange));
        MaxRange = Positive(maxRange, nameof(maxRange));
        CrouchSeconds = Positive(crouchSeconds, nameof(crouchSeconds));
        AirSeconds = Positive(airSeconds, nameof(airSeconds));
        Radius = Positive(radius, nameof(radius));
        DamageMultiplier = Positive(damageMultiplier, nameof(damageMultiplier));

        if (!(cooldown >= 0f) || float.IsInfinity(cooldown))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cooldown),
                cooldown,
                "cooldown must be a finite number, zero or more.");
        }

        Cooldown = cooldown;

        if (minRange > maxRange)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minRange),
                minRange,
                $"minRange must not exceed maxRange ({maxRange}): an empty band never leaps.");
        }
    }

    /// <summary>Metres inside which it does not leap.</summary>
    public float MinRange { get; }

    /// <summary>Metres beyond which it does not leap.</summary>
    public float MaxRange { get; }

    /// <summary>Seconds it crouches before leaving the ground.</summary>
    public float CrouchSeconds { get; }

    /// <summary>Seconds the flight takes; the speed is the distance over this.</summary>
    public float AirSeconds { get; }

    /// <summary>Metres (XZ) of the ring it bites on landing.</summary>
    public float Radius { get; }

    /// <summary>The bite as a multiple of the agent's <c>ContactDamage</c>.</summary>
    public float DamageMultiplier { get; }

    /// <summary>Seconds from one landing to the next crouch.</summary>
    public float Cooldown { get; }

    /// <remarks><see cref="LungeSpec"/>'s spelling, so NaN is refused too (AR §18.3).</remarks>
    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero. An archetype that does "
                    + "not leap carries no LeapSpec at all.");
        }

        return value;
    }
}
