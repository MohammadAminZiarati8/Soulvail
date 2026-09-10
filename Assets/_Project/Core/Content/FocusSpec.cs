using System;

namespace Soulvail.Core.Content;

/// <summary>
/// The Focus ramp, as authored numbers: how long the character must stand still before the swing
/// starts speeding up, how long it takes to reach full, and how much faster full is. CC §4.3 and
/// CC §7's "Focus delay / cap / ramp" row entire. Carried on <see cref="CharacterSpec.Focus"/> and
/// read once by <c>FocusTracker</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not the other Focus.</b> This is the *dodge, plant, burn* ramp of CC §4.3 — a timer and a
/// multiplier, driven by the stick being centred. The tap-to-focus of CC §3.4, which pins the
/// auto-aim to an enemy the player touched, is a different mechanic that the design happens to
/// give the same name: it lives in <c>FocusResolver</c>, <c>Targeter.FocusedTargetId</c> and
/// <c>CombatBlackboard.HasFocus</c>, and nothing here has anything to do with it.
/// </para>
/// <para>
/// Immutable and shared, for the reason <see cref="WeaponSpec"/> gives: this is what a designer
/// typed. The live number the ramp actually moves is <c>Weapon.FireRate</c>, and ADR-0008 says the
/// ramp reaches it as a modifier rather than by editing anything here.
/// </para>
/// <para>
/// <b>Every class has one</b>, like <see cref="TargetingSpec"/> and <see cref="WeaponSpec"/> and
/// unlike <see cref="ShieldSpec"/>: standing still is not a class feature, it is something every
/// character can do, and CC §4.3's ramp is a property of the basic attack CC §4 gives all three.
/// A class that should not ramp says so with a <see cref="MaxMultiplier"/> of exactly 1 — which is
/// also how CC §4.3's own "if it doesn't feel good with capsules, cut it" is spent, as one number
/// in one asset rather than as a code change.
/// </para>
/// <para>
/// The Oathbound's values are CC §7's: 0.4 s of delay, a 1.0 s ramp, and ×1.3 at the top — 3.0
/// swings a second becoming 3.9, which is 39 damage a second becoming 50.7. That is the number to
/// check first when any of the three move, because GD §6.2's time-to-kill invariant is written
/// against the unramped one.
/// </para>
/// </remarks>
public sealed class FocusSpec
{
    /// <param name="delay">
    /// Seconds the character must be stationary before the ramp starts at all — 0.4 (CC §4.3).
    /// Zero is legal and means the ramp begins the instant the stick is released; it is the delay
    /// that keeps a momentary pause between two dodges from paying out.
    /// </param>
    /// <param name="rampTime">
    /// Seconds from the end of <paramref name="delay"/> to full — 1.0. Must be positive: a ramp of
    /// zero is a step function, and the ramp existing at all is what makes the commitment readable
    /// as it is being made rather than only once it has been.
    /// </param>
    /// <param name="maxMultiplier">
    /// Fire rate at full Focus, as a multiple of the unramped rate — 1.3, so 130 % (CC §4.3).
    /// Exactly 1 is legal and means this class does not ramp.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="delay"/> is negative, NaN or infinite; <paramref name="rampTime"/> is not a
    /// finite number greater than zero; or <paramref name="maxMultiplier"/> is not a finite number
    /// of at least 1.
    /// </exception>
    public FocusSpec(float delay, float rampTime, float maxMultiplier)
    {
        // `!(x >= 0f)` rather than `x < 0f` throughout, so NaN is refused with everything else —
        // the spelling every spec in this folder uses, for the reason TargetingSpec.Positive gives.
        // Infinity is asked about separately because it passes every comparison here: an infinite
        // delay is a ramp that never starts, which is better said with a MaxMultiplier of 1.
        if (!(delay >= 0f) || float.IsInfinity(delay))
        {
            throw new ArgumentOutOfRangeException(
                nameof(delay),
                delay,
                "delay must be a finite number of seconds, zero or more.");
        }

        if (!(rampTime > 0f) || float.IsInfinity(rampTime))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rampTime),
                rampTime,
                "rampTime must be a finite number of seconds greater than zero. A ramp of zero is "
                    + "an instant step to the cap, which is not what CC §4.3 describes — a class "
                    + "that should not ramp says so with a maxMultiplier of 1.");
        }

        // At least 1, not greater than 1: exactly 1 is the documented way to switch the ramp off,
        // and below 1 would mean standing still makes the character swing *slower*. That is a
        // legitimate thing for a future Pact to do (GD §13.2) and it would do it with a modifier on
        // Weapon.FireRate, not by inverting the meaning of this spec.
        if (!(maxMultiplier >= 1f) || float.IsInfinity(maxMultiplier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxMultiplier),
                maxMultiplier,
                "maxMultiplier must be a finite number of at least 1 — it is the fire rate at full "
                    + "Focus as a multiple of the unramped rate. Exactly 1 means this class does "
                    + "not ramp.");
        }

        Delay = delay;
        RampTime = rampTime;
        MaxMultiplier = maxMultiplier;
    }

    /// <summary>Seconds stationary before the ramp starts. 0.4 for every V1 class.</summary>
    public float Delay { get; }

    /// <summary>Seconds from the end of <see cref="Delay"/> to full Focus. 1.0.</summary>
    public float RampTime { get; }

    /// <summary>Fire rate at full Focus as a multiple of the unramped rate. 1.3.</summary>
    public float MaxMultiplier { get; }
}
