using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Effects;

// The second effect primitive, and its handler. One file per pair, `ModifyStat.cs`' shape — and the
// first primitive in the game whose effect has to be *undone*, which is why it is also the first
// production caller of `EffectRegistry.Remove` in a live run (M3-05 rule 10's other half).

/// <summary>
/// CC §6.4's Bulwark, as data: shield points that sit on top of the Aegis and expire. The primitive
/// every timed grant in the game is made of.
/// </summary>
/// <remarks>
/// <para>
/// <b>Points, not a wall.</b> Ruled at M3-00c: this absorbs damage and never blocks a projectile
/// outright, so a bolt that is bigger than the grant still lands for the difference — which is what
/// makes a 35-point shield a decision about *when* rather than a free hit.
/// </para>
/// <para>
/// <b>The duration is the effect's, not the handler's.</b> A second timed primitive will carry its
/// own, and the clock they share (<see cref="TimedEffects"/>) knows nothing about either: it is
/// handed a deadline that someone else computed. That is the split that lets a new timed primitive
/// be one file and one <c>Register</c> line.
/// </para>
/// <para>
/// Immutable and shared across every run, like every effect (AR §10.1). The run's live objects are
/// the <em>handler</em>'s, which is the whole of ADR-0009's split.
/// </para>
/// </remarks>
public sealed class GrantShield : IEffect
{
    /// <param name="amount">
    /// Shield points granted. Strictly positive: a grant of nothing is a node that does nothing, and
    /// <c>Health.GrantShield</c>'s zero is a state the pool <em>reaches</em> rather than a value
    /// worth authoring.
    /// </param>
    /// <param name="duration">
    /// How long they stand, in simulated seconds. Strictly positive for the same reason, and because
    /// a zero-second shield is one that comes off on the tick it went on — a node whose whole
    /// behaviour is invisible.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either argument is zero, negative, NaN or infinite. Refused at this door as well as at
    /// <c>Health</c>'s, because this is where a mistake is <em>authored</em>: a content error caught
    /// when the asset is built is a content error, and the same error caught at the moment a player
    /// picks the node is a crash in a run (AR §18.3).
    /// </exception>
    public GrantShield(float amount, float duration)
    {
        // Spelled as the negation of the legal case rather than as `<= 0`, for AR §18.3's reason:
        // every comparison against NaN is false, so `amount <= 0` waves NaN straight through — and a
        // NaN in the pool would make GrantedShield NaN for the rest of the run, absorbing every hit
        // for ever with nothing logged.
        if (!(amount > 0f) || float.IsInfinity(amount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "GrantShield amount must be a finite number of points, greater than zero.");
        }

        if (!(duration > 0f) || float.IsInfinity(duration))
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "GrantShield duration must be a finite number of seconds, greater than zero. An "
                    + "effect that never expires is a passive, not a cast.");
        }

        Amount = amount;
        Duration = duration;
    }

    /// <summary>Shield points this grant is worth.</summary>
    public float Amount { get; }

    /// <summary>How long they stand, in simulated seconds.</summary>
    public float Duration { get; }
}

/// <summary>
/// What a <see cref="GrantShield"/> means to this run: points onto the player's granted-shield pool,
/// a note to take them back again, and two events so a view needs no read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three run-scoped objects and a clock, which is what M3-05 rule 1 means</b> by <em>"the handler
/// is built per run and holds the run's live objects"</em>. <see cref="ModifyStatHandler"/> holds one
/// because one is all it needs; this one grants, remembers, and says so.
/// </para>
/// <para>
/// <b>The clock is a <see cref="SimulatedClock"/> and never an <c>IClock</c></b>, and that is the
/// correction this task was written around. <c>IClock</c> is one member, <c>DateTimeOffset
/// UtcNow</c>, and a deadline taken from it would drain a shield through a level-up screen at
/// <c>timeScale</c> 0 and through a paused app. See that class's remarks, and AR §18.2's
/// <em>"there is no IClock in the session"</em>.
/// </para>
/// <para>
/// <b>Nothing in core listens to either event.</b> M3-11c draws them; the HUD's own treatment of a
/// granted shield is M3-13b's. They carry totals so that a ring can be sized and a countdown started
/// without a read back into <c>Health</c> — AR §10.2's rule against a listener keeping its own copy
/// of the player's state.
/// </para>
/// </remarks>
public sealed class GrantShieldHandler : IEffectHandler<GrantShield>
{
    private readonly Health _health;
    private readonly TimedEffects _timed;
    private readonly SimulatedClock _clock;
    private readonly IDomainEvents _events;

    /// <param name="health">The player's pools, this run.</param>
    /// <param name="timed">The clock that will call <see cref="Remove"/> back.</param>
    /// <param name="clock">Simulated seconds, for the deadline. Never a wall clock.</param>
    /// <param name="events">Where the two events go.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public GrantShieldHandler(
        Health health,
        TimedEffects timed,
        SimulatedClock clock,
        IDomainEvents events)
    {
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _timed = timed ?? throw new ArgumentNullException(nameof(timed));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>
    /// Puts <paramref name="effect"/>'s points on the player on behalf of
    /// <paramref name="source"/>, and books the moment they come off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The way out is reserved before the way in.</b> The hold goes first so that the only
    /// failure either call has — a capacity refusal — cannot leave a shield standing with nothing
    /// booked to take it back: a note against a source that never granted is a removal that does
    /// nothing, while a grant with no note is a permanent shield.
    /// </para>
    /// <para>
    /// <b>The event is published last, after both.</b> A listener reading
    /// <c>RunState.PlayerGrantedShield</c> from inside it sees the number the event is describing —
    /// <c>SkillRunner.Fire</c>'s rule 9, one layer down. Which also means the cast's own
    /// <c>SkillCast</c> arrives <em>after</em> this, because the runner applies its effects before
    /// it announces the cast.
    /// </para>
    /// </remarks>
    /// <param name="source">
    /// Who owns the grant — the <c>ActiveSpec</c>, for a cast (M3-06 rule 8). Identity only: it is
    /// what <see cref="Remove"/> takes the points back by.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> is null, or <paramref name="source"/> is.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="TimedEffects.Capacity"/> effects are already held, or
    /// <c>GrantedShieldPool.Capacity</c> sources already hold shield.
    /// </exception>
    public void Apply(GrantShield effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        _timed.Hold(effect, source, _clock.Now + effect.Duration);

        _health.GrantShield(effect.Amount, source);

        _events.Publish(new ShieldGranted(effect.Amount, _health.GrantedShield, effect.Duration));
    }

    /// <summary>
    /// Takes <paramref name="source"/>'s grant back — whatever is left of it.
    /// </summary>
    /// <remarks>
    /// What an expiry calls, through the registry, because a handler is the only thing that knows
    /// what undoing its own effect means (ADR-0009). It removes what <em>remains</em> rather than
    /// what was granted — <c>GrantedShieldPool</c>'s rule 8 — so a player who already spent the
    /// shield loses nothing extra when it lapses. A source holding nothing is not an error and says
    /// nothing, which is <c>Stat.RemoveAll</c>'s contract and what lets a caller clean up
    /// unconditionally; a source that was held and had been spent to zero <em>does</em> announce
    /// itself with a <c>Removed</c> of zero, because the grant really did end and a view drawing a
    /// countdown has to stop drawing it.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> or <paramref name="source"/> is null.
    /// </exception>
    public void Remove(GrantShield effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        float before = _health.GrantedShield;

        if (!_health.RemoveGrantedShield(source))
        {
            return;
        }

        float total = _health.GrantedShield;

        _events.Publish(new ShieldGrantExpired(before - total, total));
    }
}
