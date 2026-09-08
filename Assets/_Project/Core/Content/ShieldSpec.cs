using System;

namespace Soulvail.Core.Content;

/// <summary>
/// A regenerating shield, as authored numbers: how much it absorbs, how long it must go
/// unhurt before it starts coming back, and how fast it does. The Oathbound's Aegis is the
/// only instance in V1 — see CH §3.1 and CC §7.
/// </summary>
/// <remarks>
/// <para>
/// Immutable and shared: one instance describes the class, and every <c>Health</c> built from
/// it keeps its own current value. Nothing here is a <c>Stat</c>, for the reason
/// <see cref="MovementSpec"/> gives — this is what a designer typed, and the modifier stack
/// belongs to the live character. A tree node that wants "+20 Aegis" needs a <c>Stat</c> on
/// <c>Health</c> rather than a mutable field here; M3 is the first task that asks for one.
/// </para>
/// <para>
/// A class without a shield has no <see cref="ShieldSpec"/> at all — <c>null</c>, not a spec
/// with zero in it. The Gravecaller and the Emberwright are both in that case, as is every
/// enemy. That is why the numbers here are all required to be positive: a shield that exists
/// is a shield that does something.
/// </para>
/// </remarks>
public sealed class ShieldSpec
{
    /// <param name="max">Points of damage the shield absorbs when full. The Oathbound's is 30.</param>
    /// <param name="rechargeDelay">
    /// Seconds that must pass without taking damage before the shield starts refilling. 4 for
    /// the Oathbound — long enough that it is a reward for disengaging rather than a passive
    /// regeneration.
    /// </param>
    /// <param name="refillPerSecond">
    /// Points per second once refilling has started. 15 for the Oathbound, which is a full
    /// shield in 2 s.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any value is not greater than zero. A zero <paramref name="max"/> is a shield that
    /// absorbs nothing, a zero <paramref name="refillPerSecond"/> is one that never comes
    /// back, and a zero <paramref name="rechargeDelay"/> is regeneration rather than a
    /// shield — all three are better said by passing no spec at all.
    /// </exception>
    public ShieldSpec(float max, float rechargeDelay, float refillPerSecond)
    {
        Max = Positive(max, nameof(max));
        RechargeDelay = Positive(rechargeDelay, nameof(rechargeDelay));
        RefillPerSecond = Positive(refillPerSecond, nameof(refillPerSecond));
    }

    /// <summary>Points absorbed when full.</summary>
    public float Max { get; }

    /// <summary>Seconds without taking damage before the refill starts.</summary>
    public float RechargeDelay { get; }

    /// <summary>Points per second once the refill has started.</summary>
    public float RefillPerSecond { get; }

    /// <remarks>
    /// <c>!(value &gt; 0f)</c> rather than <c>value &lt;= 0f</c>, so NaN is refused too: every
    /// comparison against NaN is false, and the natural spelling waves it through. A NaN in any
    /// of these three would spread into a shield value, a recharge deadline or both, and — as
    /// <c>Stat</c> documents — a NaN does not make a number wrong so much as it makes every
    /// later comparison against it false, which is silence rather than a visible fault.
    /// Infinity is refused by the same check for <paramref name="value"/> and by
    /// <c>Health</c>'s clamp for the rest.
    /// </remarks>
    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero. A class or enemy with " +
                "no shield passes no ShieldSpec at all.");
        }

        return value;
    }
}
