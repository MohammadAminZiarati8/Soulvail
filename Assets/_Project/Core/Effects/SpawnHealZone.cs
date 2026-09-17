using System;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

// The third effect primitive, and its handler. One file per pair, `ModifyStat.cs`' shape — and the
// first primitive whose effect is a thing in the *world* rather than a number on the player, which is
// why it is also the first one whose `Remove` is deliberately empty (rule 9).

/// <summary>
/// CC §6.4's Consecrate, as data: healing ground, placed under the caster. The primitive every placed
/// zone in the game is made of.
/// </summary>
/// <remarks>
/// <para>
/// <b>Four numbers and all four are authored</b> (M3-05 rule 10). Consecrate is 3.5 m for 6 s, 3 hit
/// points every 0.5 s — thirty-six against CC §7's 140, about a quarter of a bar for holding ground
/// through half a wave — and the owner retunes every one of them in <c>Consecrate.asset</c> (M3-12b)
/// rather than in a task.
/// </para>
/// <para>
/// <b>Placed, never carried.</b> Where it lands is <c>ZoneSystem</c>'s business and the argument for
/// it is there; what this type says is that a zone has an extent and a life, which a buff does not.
/// </para>
/// <para>
/// Immutable and shared across every run, like every effect (AR §10.1). The run's live objects are
/// the <em>handler</em>'s, which is the whole of ADR-0009's split.
/// </para>
/// </remarks>
public sealed class SpawnHealZone : IEffect
{
    /// <param name="radius">
    /// How far it reaches, in metres. Strictly positive: a zone with no extent is a node whose whole
    /// behaviour is invisible.
    /// </param>
    /// <param name="duration">
    /// How long it stands, in simulated seconds. Strictly positive for the same reason — a
    /// zero-second zone is one that is over on the tick it is cast.
    /// </param>
    /// <param name="healPerPulse">
    /// Hit points one pulse restores. Strictly positive: a heal of nothing is a node that does
    /// nothing, and zero is a state a pulse <em>reaches</em> at full health rather than a value worth
    /// authoring.
    /// </param>
    /// <param name="pulseInterval">
    /// Simulated seconds between pulses. Strictly positive, and far enough inside
    /// <see cref="ZoneSystem.MaxPulses"/> of the duration that the zone is a zone rather than a loop.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any argument is zero, negative, NaN or infinite, or the duration schedules more than
    /// <see cref="ZoneSystem.MaxPulses"/> pulses. Refused at this door as well as at
    /// <c>ZoneSystem</c>'s, because this is where a mistake is <em>authored</em>: a content error
    /// caught when the asset is built is a content error, and the same error caught at the moment a
    /// player picks the node is a crash in a run (AR §18.3).
    /// </exception>
    public SpawnHealZone(float radius, float duration, float healPerPulse, float pulseInterval)
    {
        Radius = Positive(radius, nameof(radius));
        Duration = Positive(duration, nameof(duration));
        HealPerPulse = Positive(healPerPulse, nameof(healPerPulse));
        PulseInterval = Positive(pulseInterval, nameof(pulseInterval));

        if (duration / pulseInterval > ZoneSystem.MaxPulses)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pulseInterval),
                pulseInterval,
                $"A zone lasting {duration} s with a pulse every {pulseInterval} s schedules more "
                    + $"than {ZoneSystem.MaxPulses} pulses, which is an authoring mistake rather "
                    + "than a fast zone.");
        }
    }

    /// <summary>How far it reaches, in metres.</summary>
    public float Radius { get; }

    /// <summary>How long it stands, in simulated seconds.</summary>
    public float Duration { get; }

    /// <summary>Hit points one pulse restores.</summary>
    public float HealPerPulse { get; }

    /// <summary>Simulated seconds between pulses.</summary>
    public float PulseInterval { get; }

    /// <remarks>
    /// <c>!(value &gt; 0f)</c> rather than <c>value &lt;= 0f</c>, so NaN is refused too (AR §18.3),
    /// and infinity separately because it passes a <c>&gt; 0</c> test.
    /// </remarks>
    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"SpawnHealZone {paramName} must be a finite number greater than zero. A zone with "
                    + "no extent, no life, no pulse or no heal is one that is drawn and does "
                    + "nothing.");
        }

        return value;
    }
}

/// <summary>
/// What a <see cref="SpawnHealZone"/> means to this run: a patch of ground under the caster, on the
/// run's own clock.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two run-scoped objects, which is what M3-05 rule 1 means</b> by <em>"the handler is built per
/// run and holds the run's live objects"</em>. It publishes nothing itself — <c>ZoneSystem</c>
/// announces a placement, because the system is what knows the id it issued.
/// </para>
/// <para>
/// <b>The clock is a <see cref="SimulatedClock"/> and never an <c>IClock</c>, for the second time in
/// two tasks.</b> <c>IClock</c> is one member, <c>DateTimeOffset UtcNow</c>, and AR §18.2 says in as
/// many words that there is no <c>IClock</c> in the session: a zone timed off a wall clock would burn
/// down through a level-up screen at <c>timeScale</c> 0 and through a backgrounded app. The other
/// half of that correction is why the clock is held at all — <c>IEffectHandler&lt;T&gt;.Apply</c> is
/// handed no time, and <c>ZoneSystem</c> remembering <c>_now</c> from its own <see cref="Tick"/> would
/// schedule a zone cast this frame off last frame's clock, because that class ticks <em>after</em> the
/// caster (M3-11a-ii, correction 2, one task later and unchanged).
/// </para>
/// <para>
/// <b><see cref="Remove"/> does nothing, and that is the deliberate difference from
/// <c>GrantShield</c>.</b> A granted shield is state on the player and has to be taken back; a zone is
/// a thing in the world that ends when it ends, so nothing holds one in <c>TimedEffects</c> and
/// nothing ever calls this. It is written, and pinned by a row, because
/// <c>IEffectHandler&lt;T&gt;.Remove</c> already promises that a source holding nothing is not an
/// error — the contract is what the row keeps, rather than a path.
/// </para>
/// </remarks>
public sealed class SpawnHealZoneHandler : IEffectHandler<SpawnHealZone>
{
    private readonly ZoneSystem _zones;
    private readonly SimulatedClock _clock;

    /// <param name="zones">The ground this run is standing on.</param>
    /// <param name="clock">Simulated seconds, for the schedule. Never a wall clock.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public SpawnHealZoneHandler(ZoneSystem zones, SimulatedClock clock)
    {
        _zones = zones ?? throw new ArgumentNullException(nameof(zones));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Places <paramref name="effect"/>'s zone where the player is standing, on behalf of
    /// <paramref name="source"/>.
    /// </summary>
    /// <param name="source">
    /// Who cast it — the <c>ActiveSpec</c>, for a cast (M3-06 rule 8). Identity only, and held by the
    /// zone rather than read by it.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> is null, or <paramref name="source"/> is.
    /// </exception>
    /// <exception cref="InvalidOperationException"><see cref="ZoneSystem.Capacity"/> zones stand.</exception>
    public void Apply(SpawnHealZone effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        _zones.Spawn(
            effect.Radius,
            effect.Duration,
            effect.HealPerPulse,
            effect.PulseInterval,
            _clock.Now,
            source);
    }

    /// <summary>
    /// Does nothing: a zone owns its own life (rule 9).
    /// </summary>
    /// <remarks>
    /// Not an oversight and not a <c>NotSupportedException</c>. <c>IEffectHandler&lt;T&gt;.Remove</c>
    /// says a source holding nothing is not an error — <c>Stat.RemoveAll</c>'s contract, and what lets
    /// a caller clean up unconditionally — and this handler's answer is that <em>every</em> source
    /// holds nothing, because what a cast left behind is a place rather than a modifier. Nothing calls
    /// it: no zone is ever held in <c>TimedEffects</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> or <paramref name="source"/> is null. Refused even though nothing
    /// afterwards would read either, so that this door and <c>GrantShieldHandler</c>'s answer a
    /// mis-wired caller the same way rather than one of them silently accepting a removal that names
    /// nobody.
    /// </exception>
    public void Remove(SpawnHealZone effect, object source)
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
