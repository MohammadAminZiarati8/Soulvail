using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Effects;

// The seventh effect primitive, and its handler. One file per pair, `SpawnHealZone.cs`' shape, and
// its mirror: the same ZoneSystem, the other ZoneSide.

/// <summary>
/// Places a patch of ground that burns whatever is standing in it — CH §3.3's Ash branch, and
/// <see cref="SpawnHealZone"/>'s mirror. The only new effect primitive this milestone ships.
/// </summary>
/// <remarks>
/// <para>
/// <b>A second primitive rather than a side flag on <see cref="SpawnHealZone"/></b> (M6-08 rule 3).
/// That type is named for what it does and is authored on shipped assets; giving it a
/// <see cref="ZoneSide"/> would make every existing <c>SpawnHealZoneDefinition</c> carry a field
/// whose default decides whether a Consecrate heals or burns, which is one typo away from a heal
/// zone that kills the player's own army. Two types, two definitions, two handlers, one
/// <see cref="ZoneSystem"/>.
/// </para>
/// <para>
/// <b>Three numbers, and the interval is not one of them.</b> A burn pulses every
/// <see cref="MovementSkillSpec.PoolPulseInterval"/>, the Blink pool's own beat, so every fire the
/// Emberwright places reads at the same rhythm and the arithmetic is duration over one constant.
/// Emberfall is 4 m for 4 s at 6 a pulse — eight pulses, 48 — and Cinder Nova 6 m for 1.5 s at 10 —
/// three pulses, 30 — both inside the pool's 24-over-three-seconds family (rule 3).
/// </para>
/// <para>
/// Immutable and shared across every run, like every effect (AR §10.1).
/// </para>
/// </remarks>
public sealed class SpawnBurnZone : IEffect
{
    /// <param name="radius">How far it reaches, in metres. Finite and above zero.</param>
    /// <param name="duration">How long it burns, in simulated seconds. Finite and above zero.</param>
    /// <param name="damagePerPulse">What one pulse takes off each enemy inside. Finite and above zero.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any argument is not a finite number greater than zero, or <paramref name="duration"/> over
    /// <see cref="MovementSkillSpec.PoolPulseInterval"/> exceeds <see cref="ZoneSystem.MaxPulses"/>.
    /// Refused here as well as at <c>ZoneSystem.Spawn</c>, because this is where the mistake is
    /// authored (AR §18.3).
    /// </exception>
    public SpawnBurnZone(float radius, float duration, float damagePerPulse)
    {
        Radius = Positive(radius, nameof(radius));
        Duration = Positive(duration, nameof(duration));
        DamagePerPulse = Positive(damagePerPulse, nameof(damagePerPulse));

        if (duration / MovementSkillSpec.PoolPulseInterval > ZoneSystem.MaxPulses)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                $"A burn lasting {duration} s pulses every {MovementSkillSpec.PoolPulseInterval} s, "
                    + $"which schedules more than {ZoneSystem.MaxPulses} pulses — an authoring "
                    + "mistake rather than a long fire.");
        }
    }

    /// <summary>How far it reaches, in metres.</summary>
    public float Radius { get; }

    /// <summary>How long it burns, in simulated seconds.</summary>
    public float Duration { get; }

    /// <summary>What one pulse takes off each enemy standing in it.</summary>
    public float DamagePerPulse { get; }

    /// <remarks><see cref="SpawnHealZone"/>'s spelling: NaN fails <c>&gt; 0</c>, infinity is asked apart.</remarks>
    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"SpawnBurnZone {paramName} must be a finite number greater than zero. A burn with "
                    + "no extent, no life or no damage is one that is drawn and does nothing.");
        }

        return value;
    }
}

/// <summary>
/// What a <see cref="SpawnBurnZone"/> means to this run: burning ground under the caster, on the
/// run's own clock. <c>SpawnHealZoneHandler</c>'s mirror.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered unconditionally, the opposite call from <c>RaiseMinionsHandler</c></b> (M6-08
/// rule 5). That one needs a <c>MinionSpec</c> a run may not have; a burn needs a
/// <see cref="ZoneSystem"/> wired to burn, which <c>RunSession</c> builds for every run. So CH §5.4
/// can lend the Emberwright's Ash and Arcana branches to any class.
/// </para>
/// <para>
/// <b>The clock is a <see cref="SimulatedClock"/></b>, for <c>SpawnHealZoneHandler</c>'s reason:
/// <c>Apply</c> is handed no time, and a zone timed off a wall clock would burn down through a
/// level-up screen.
/// </para>
/// </remarks>
public sealed class SpawnBurnZoneHandler : IEffectHandler<SpawnBurnZone>
{
    private readonly ZoneSystem _zones;
    private readonly SimulatedClock _clock;

    /// <param name="zones">The ground this run stands on — built with enemies to burn.</param>
    /// <param name="clock">Simulated seconds, for the schedule. Never a wall clock.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public SpawnBurnZoneHandler(ZoneSystem zones, SimulatedClock clock)
    {
        _zones = zones ?? throw new ArgumentNullException(nameof(zones));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Places <paramref name="effect"/>'s burn where the player is standing, on behalf of
    /// <paramref name="source"/>.
    /// </summary>
    /// <remarks>
    /// At the player's feet by <c>ZoneSystem.Spawn</c>'s existing rule — which for Cinder Nova is
    /// the mechanic and for Emberfall is the cost (rule 3). A second cast places a second zone.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> is null, or <paramref name="source"/> is.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="ZoneSystem.Capacity"/> zones stand, or the system was built with nothing to burn.
    /// </exception>
    public void Apply(SpawnBurnZone effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        _zones.Spawn(
            effect.Radius,
            effect.Duration,
            effect.DamagePerPulse,
            MovementSkillSpec.PoolPulseInterval,
            _clock.Now,
            source,
            ZoneSide.BurnsEnemies);
    }

    /// <summary>Does nothing. A zone owns its own life — <c>ZoneSystem</c>'s rule 9.</summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> or <paramref name="source"/> is null — <c>SpawnHealZoneHandler</c>'s
    /// answer to a mis-wired caller, kept identical.
    /// </exception>
    public void Remove(SpawnBurnZone effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }
    }
}
