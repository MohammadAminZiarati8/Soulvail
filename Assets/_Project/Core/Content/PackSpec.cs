using System;

namespace Soulvail.Core.Content;

/// <summary>
/// How a Lunger hunts in a pair: how far it looks for a partner of its own kind, how far round the
/// player the flanker swings, and how long it swings before striking alone. GD §8.2's Shadow
/// panther (JG-21b).
/// </summary>
/// <remarks>
/// Absent on an archetype that hunts alone, for <see cref="LungeSpec"/>'s reasons: a null block is
/// the one unambiguous "not this one". Honoured by <c>LungerBehaviour</c> only. Lanes, damage and
/// cooldowns stay the Lunger's.
/// </remarks>
public sealed class PackSpec
{
    /// <param name="partnerRadius">Metres (XZ) within which it takes a partner. 16.</param>
    /// <param name="flankAngleDegrees">
    /// Degrees round the player from the leader's bearing the flanker stands at. 90. Above zero, at
    /// most 180.
    /// </param>
    /// <param name="flankTimeout">Seconds it swings before striking alone. 3.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any value is not a finite number greater than zero, or <paramref name="flankAngleDegrees"/>
    /// exceeds 180.
    /// </exception>
    public PackSpec(float partnerRadius, float flankAngleDegrees, float flankTimeout)
    {
        PartnerRadius = Positive(partnerRadius, nameof(partnerRadius));
        FlankAngleDegrees = Positive(flankAngleDegrees, nameof(flankAngleDegrees));
        FlankTimeout = Positive(flankTimeout, nameof(flankTimeout));

        if (flankAngleDegrees > 180f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(flankAngleDegrees),
                flankAngleDegrees,
                "flankAngleDegrees must be at most 180: past that it is the other side's angle.");
        }
    }

    /// <summary>Metres (XZ) within which it takes a partner.</summary>
    public float PartnerRadius { get; }

    /// <summary>Degrees round the player from the leader's bearing the flanker stands at.</summary>
    public float FlankAngleDegrees { get; }

    /// <summary>Seconds it swings before striking alone.</summary>
    public float FlankTimeout { get; }

    /// <remarks><see cref="LungeSpec"/>'s spelling, so NaN is refused too (AR §18.3).</remarks>
    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero. An archetype that hunts "
                    + "alone carries no PackSpec at all.");
        }

        return value;
    }
}
