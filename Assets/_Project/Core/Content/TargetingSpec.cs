using System;

namespace Soulvail.Core.Content;

/// <summary>
/// How a class aims itself, as authored numbers: how far it looks, what it weighs, and how
/// often it decides. The whole of CC §3.2's scoring formula lives here as data, because CC §7
/// says every one of these is a designer's knob rather than a constant in code.
/// </summary>
/// <remarks>
/// <para>
/// Immutable and shared, for the reason <see cref="MovementSpec"/> gives: this is what a
/// designer typed, and the modifier stack belongs to the live character. A tree node that wants
/// "+4 m acquire range" needs a <c>Stat</c> on whatever holds the run's targeting state, not a
/// mutable field here.
/// </para>
/// <para>
/// The Oathbound's values, and CC §7's targeting table entire: range 12, distance weight 3,
/// elite bonus 2, finisher bonus 1, hysteresis 1.5, cadence 0.1 s (the 10 Hz loop of CC §3.1).
/// Two of the table's rows are deliberately not here — tap-to-focus radius and focus drop
/// delay belong to M1-09's override, which bypasses scoring entirely rather than weighting it.
/// </para>
/// <para>
/// <b>Every class has one.</b> Unlike <see cref="ShieldSpec"/>, which is <c>null</c> for the
/// classes without an Aegis, there is no such thing as a class that does not aim — auto-aim is
/// the default control mode for all three (CC §3). That is why <see cref="CharacterSpec"/>
/// takes this as a required argument rather than an optional one: a missing spec is a
/// forgotten field, never a design statement.
/// </para>
/// </remarks>
public sealed class TargetingSpec
{
    /// <param name="acquireRange">
    /// Metres within which an enemy can be selected at all — 12 for the Oathbound, which CC §3.1
    /// derives as weapon range × 1.5. Also the divisor in the distance term, so it sets what
    /// "close" is worth as well as what is reachable.
    /// </param>
    /// <param name="distanceWeight">
    /// Score for a target at zero distance, falling linearly to zero at
    /// <paramref name="acquireRange"/>. 3.0.
    /// </param>
    /// <param name="eliteBonus">Flat score added for an Elite. 2.0.</param>
    /// <param name="finisherBonus">
    /// Flat score added when the target could die within one second of fire — CC §3.2's "prefer
    /// finishing wounded targets". 1.0.
    /// </param>
    /// <param name="hysteresis">
    /// Flat score added to the <em>current</em> target, which a challenger must beat to steal
    /// focus. 1.5. CC §3.3: without it, two similarly-scored enemies make the character twitch
    /// between them every 100 ms and waste the swing that was mid-windup.
    /// </param>
    /// <param name="cadence">
    /// Seconds between targeting decisions. 0.1 — the 10 Hz of CC §3.1, which is imperceptible
    /// as latency and meaningful CPU with 28 enemies on a mid-range phone.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="acquireRange"/> or <paramref name="cadence"/> is not a finite number
    /// greater than zero, or any of the four weights is negative, NaN or infinite.
    /// </exception>
    public TargetingSpec(
        float acquireRange,
        float distanceWeight,
        float eliteBonus,
        float finisherBonus,
        float hysteresis,
        float cadence)
    {
        AcquireRange = Positive(acquireRange, nameof(acquireRange));
        DistanceWeight = Weight(distanceWeight, nameof(distanceWeight));
        EliteBonus = Weight(eliteBonus, nameof(eliteBonus));
        FinisherBonus = Weight(finisherBonus, nameof(finisherBonus));
        Hysteresis = Weight(hysteresis, nameof(hysteresis));
        Cadence = Positive(cadence, nameof(cadence));
    }

    /// <summary>Metres within which an enemy can be selected. 12 for the Oathbound.</summary>
    public float AcquireRange { get; }

    /// <summary>Score at zero distance, falling linearly to zero at <see cref="AcquireRange"/>.</summary>
    public float DistanceWeight { get; }

    /// <summary>Flat score for an Elite.</summary>
    public float EliteBonus { get; }

    /// <summary>Flat score for a target that could die within a second.</summary>
    public float FinisherBonus { get; }

    /// <summary>Flat score for the incumbent — the anti-jitter margin of CC §3.3.</summary>
    public float Hysteresis { get; }

    /// <summary>Seconds between targeting decisions. 0.1 is CC §3.1's 10 Hz.</summary>
    public float Cadence { get; }

    /// <remarks>
    /// <c>!(value &gt; 0f)</c> rather than <c>value &lt;= 0f</c>, so NaN is refused with
    /// everything else: every comparison against NaN is false, and the natural spelling waves it
    /// through. Both of these numbers would be silent about it, which is the worst way to be
    /// wrong — a NaN range makes every distance term NaN, and every candidate then loses every
    /// comparison in <c>TargetScorer</c>, so the gun would simply stop choosing a target with
    /// nothing logged. Zero is refused for the same shape of reason: a zero range selects
    /// nobody and divides the distance term by nothing, and a zero cadence turns the 10 Hz loop
    /// CC §3.1 exists to specify back into a per-frame one.
    /// </remarks>
    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero.");
        }

        return value;
    }

    /// <remarks>
    /// One rung looser than <see cref="Positive"/>: zero is a legitimate weight, and means the
    /// term is switched off — a class that ignores distance entirely and picks purely by
    /// archetype priority is a coherent thing to tune towards, even if nothing in V1 does.
    /// Negative is not: it would invert the term's meaning while still reading as a weight, so
    /// "distance weight −3" would quietly prefer the enemy furthest away. Infinity is asked
    /// about separately because it passes a <c>&gt;= 0</c> test and would make one term
    /// out-shout every other for good.
    /// </remarks>
    private static float Weight(float value, string paramName)
    {
        if (!(value >= 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number, zero or more. Zero switches the term off; " +
                "a negative weight would invert what it means.");
        }

        return value;
    }
}
