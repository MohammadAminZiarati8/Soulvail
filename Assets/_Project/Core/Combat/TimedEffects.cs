using System;
using Soulvail.Core.Effects;

namespace Soulvail.Core.Combat;

// The clock a cast effect expires on, and the one-field holder of simulated seconds it reads the
// deadline from. Two types in one file for `ModifyStat.cs`' reason: the second exists only because
// the first takes absolute expiries, and reading either without the other tells you half of why.

/// <summary>
/// The run's simulated seconds, in an object — <c>RunState.Time</c>, readable by something that was
/// built before <c>RunState</c> existed.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because <see cref="TimedEffects.Hold"/> takes an absolute deadline and
/// <c>IEffectHandler&lt;T&gt;.Apply</c> is handed no clock.</b> That signature is two parameters —
/// the effect and its source — and widening it would change every handler, both halves of
/// <c>EffectRegistry</c>'s dispatch and <c>ModifyStatHandler</c>, which wants no clock at all. So
/// the one handler that needs the time holds the time, and nothing else moves.
/// </para>
/// <para>
/// <b>Simulated, never wall-clock, and that is the whole of the name.</b> <c>IClock</c> is an AR §6
/// port with one member, <c>DateTimeOffset UtcNow</c>, and AR §18.2 says in as many words that there
/// is no <c>IClock</c> in the session. A shield deadline taken from a wall clock would drain through
/// a level-up screen at <c>timeScale</c> 0 and through a backgrounded app — the exact opposite of
/// <c>OverflowToast</c>'s unscaled dwell, because a shield is simulation and a toast is
/// presentation.
/// </para>
/// <para>
/// <b>One writer, and it is <c>RunSession.Tick</c></b>, on the line beside <c>State.Time += Dt</c>
/// and therefore above every reader in the frame — combat, the skills step, the behaviours. A
/// handler that runs outside a tick (a take effect applied from a level-up, which nothing in M3
/// authors) reads the last tick's second, which is the same second <c>RunState.Time</c> holds and
/// the same one the level-up screen is frozen at.
/// </para>
/// <para>
/// Deliberately not an interface and deliberately not a port: it is a number this run owns, in the
/// same sense <c>CombatBlackboard</c> is a table this run owns (ADR-0005 — one writer, many
/// readers).
/// </para>
/// </remarks>
public sealed class SimulatedClock
{
    /// <summary>Seconds since this run began. The sum of every tick's <c>Dt</c>, and nothing else.</summary>
    public float Now { get; set; }
}

/// <summary>
/// What a cast put on the player that has to come off again. The source owns the clock (M3-05
/// rule 6) and this is the clock it owns: one table, one absolute expiry each, and
/// <see cref="EffectRegistry.Remove"/> as the only way anything leaves.
/// </summary>
/// <remarks>
/// <para>
/// <b>It remembers; it does not apply.</b> The caster applies — <see cref="SkillRunner"/>'s
/// <c>Fire</c> walks <c>ActiveSpec.OnCast</c> through the registry (M3-06 rule 8) — and this class
/// is handed the same <c>(effect, source)</c> pair with a deadline. Splitting it that way keeps the
/// registry's one-line dispatch intact: an effect that expires is an ordinary effect plus a note,
/// not a second kind of effect. M3-06 rule 8's promise, that the first timed cast effect
/// <em>"owns its own clock, and EffectRegistry.Remove is the door it calls"</em>, lands here once
/// rather than in every handler that ever needs a duration.
/// </para>
/// <para>
/// <b>Absolute expiries against the simulated clock</b> — <c>Weapon</c>'s shape, <c>ChargeSkill</c>'s
/// and M3-06 rule 4's. No accumulator to drift, so a 30 fps phone and a 120 fps one hold a shield for
/// the same five seconds.
/// </para>
/// <para>
/// <b>Ticked immediately after <see cref="SkillRunner"/> and above the projectile step, and that
/// ordering is the mechanic's</b> (AR §18.1). The runner may cast this frame, so expiring first
/// would let a grant made last frame outlive one made this frame by a tick — and worse, would take a
/// grant back and hand it straight over again on the tick a skill recasts. It sits above the
/// projectile step for the reason the runner does (M3-06 rule 7): a shield that expired
/// <em>after</em> this tick's bolts were resolved would have absorbed a hit it was no longer
/// entitled to.
/// </para>
/// <para>
/// <b>Sorted by deadline, so <see cref="Tick"/> only ever reads the front.</b> Sixteen entries is
/// small enough that the insertion shift is free and the alternative — a scan for the earliest due
/// entry, every frame, per removal — is the one that is walked sixty times a second for nothing. The
/// order is also the answer to <em>which</em> comes off first when two fall due on the same tick,
/// which is a rule rather than an implementation detail.
/// </para>
/// <para>
/// <b>It is not the spend order and must not be mistaken for one.</b> <c>GrantedShieldPool</c> spends
/// in <em>grant</em> order and nothing here changes that: a grant that expires takes back what
/// remains of its own source, whoever paid for the last hit. Two orderings, two questions, and
/// <c>Health_SpendsTheOldestGrantFirst</c> is the one that pins the other.
/// </para>
/// <para>
/// <b>A fixed array, and a seventeenth hold throws rather than growing one.</b> Sixteen simultaneous
/// timed effects is a bug and not a budget — CH §4's tree holds about seven Actives in total — and
/// this is walked from a per-frame path, where a silently growing list is only ever found on a phone.
/// It is deliberately <em>not</em> <c>GrantedShieldPool.Capacity</c>, which is eight: that number
/// limits distinct sources of one mechanic, this one limits every timed effect in the game at once,
/// and tying them together would mean a future zone or debuff stealing a Bulwark's slot.
/// </para>
/// <para>
/// Nothing here allocates: three parallel arrays and a count, <c>SkillRunner</c>'s shape rather than
/// an array of structs.
/// </para>
/// </remarks>
public sealed class TimedEffects
{
    /// <summary>How many timed effects may be held at once.</summary>
    public const int Capacity = 16;

    private readonly EffectRegistry _effects;

    private readonly IEffect[] _held = new IEffect[Capacity];
    private readonly object[] _sources = new object[Capacity];
    private readonly float[] _expiresAt = new float[Capacity];

    private int _count;

    /// <param name="effects">
    /// The run's table, and the only door anything leaves through. Removal goes back to the handler
    /// that applied it, because a handler is the only thing that knows what undoing its own effect
    /// means (ADR-0009).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="effects"/> is null.</exception>
    public TimedEffects(EffectRegistry effects)
    {
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
    }

    /// <summary>How many effects are being held, due or not.</summary>
    public int Count => _count;

    /// <summary>
    /// Applies nothing — the caster already did. Remembers to take it back at
    /// <paramref name="expiresAt"/>.
    /// </summary>
    /// <remarks>
    /// <b>The same pair held twice refreshes its deadline rather than queuing a second one</b>, and
    /// that is <c>GrantedShieldPool</c>'s rule 6 arriving one layer up: a recast sets the source's
    /// contribution instead of doubling it, so a second note against the same <c>(effect, source)</c>
    /// would come due on the first cast's clock and take the <em>refreshed</em> shield back early.
    /// A pair matched by identity, never equality — <c>IEffectHandler&lt;T&gt;.Remove</c>'s contract,
    /// and the pool's.
    /// </remarks>
    /// <param name="effect">What the caster applied.</param>
    /// <param name="source">Who applied it — the <c>ActiveSpec</c>, for a cast (M3-06 rule 8).</param>
    /// <param name="expiresAt">
    /// Simulated seconds, absolute: <see cref="SimulatedClock.Now"/> plus the effect's duration.
    /// A deadline already in the past is legal and comes off on the next <see cref="Tick"/>, which
    /// is what an effect with a duration shorter than a frame means.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> or <paramref name="source"/> is null. A sourceless hold could never
    /// be taken back, which is <c>Modifier</c>'s rule two layers down.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="expiresAt"/> is NaN or infinite. Refused rather than admitted, for AR §18.3's
    /// reason: <see cref="Tick"/> asks <c>now &gt;= expiresAt</c> and every comparison against NaN is
    /// false, so a NaN deadline is an effect that is never taken back — a permanent shield, with
    /// nothing logged.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Capacity"/> effects are already held and this is not one of them.
    /// </exception>
    public void Hold(IEffect effect, object source, float expiresAt)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (float.IsNaN(expiresAt) || float.IsInfinity(expiresAt))
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                expiresAt,
                "A timed effect's deadline must be a finite number of simulated seconds.");
        }

        int existing = IndexOf(effect, source);

        if (existing >= 0)
        {
            // Dropped and re-inserted rather than written in place: the array's order *is* the
            // expiry order, and a refreshed deadline is almost always later than the one it
            // replaces, so leaving the entry where it sits would break the one invariant Tick
            // trusts.
            DropAt(existing);
        }
        else if (_count == Capacity)
        {
            throw new InvalidOperationException(
                $"No more than {Capacity} timed effects may be held at once, and a seventeenth is a "
                    + "bug rather than a build: nothing in the design puts that many durations on "
                    + "the player at the same moment. Growing the array instead would put an "
                    + "allocation on a per-frame path.");
        }

        // Insertion sort into a sixteen-slot array, and `>` rather than `>=` so that two effects
        // sharing a deadline come off in the order they were held.
        int slot = _count;

        while (slot > 0 && _expiresAt[slot - 1] > expiresAt)
        {
            _held[slot] = _held[slot - 1];
            _sources[slot] = _sources[slot - 1];
            _expiresAt[slot] = _expiresAt[slot - 1];

            slot--;
        }

        _held[slot] = effect;
        _sources[slot] = source;
        _expiresAt[slot] = expiresAt;

        _count++;
    }

    /// <summary>
    /// Removes everything whose time is up, earliest deadline first. Allocates nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The entry is dropped before the handler is called</b>, so a handler that holds something
    /// new from inside its own <c>Remove</c> — nothing does today — lands in a table that no longer
    /// contains what it is being asked to undo, and so that a handler that throws cannot leave this
    /// loop reading the same due entry for ever.
    /// </para>
    /// <para>
    /// <c>now &gt;= expiresAt</c> rather than its negation, which is the opposite spelling to
    /// <c>SkillRunner.Tick</c>'s and deliberately: there the unreadable clock must not be read as
    /// <em>ready</em>, here an unreadable one must not be read as <em>due</em>. A NaN handed in takes
    /// nothing back rather than emptying the table, and <see cref="Hold"/> has already refused a NaN
    /// on the other side of the comparison.
    /// </para>
    /// </remarks>
    /// <param name="now">Simulated seconds — <c>RunState.Time</c>, never a wall clock.</param>
    public void Tick(float now)
    {
        while (_count > 0 && now >= _expiresAt[0])
        {
            IEffect effect = _held[0];
            object source = _sources[0];

            DropAt(0);

            _effects.Remove(effect, source);
        }
    }

    /// <summary>
    /// Takes one back early, whether or not it was due.
    /// </summary>
    /// <remarks>
    /// For the case nothing in M3 has — a skill cancelled, a zone dismissed, a status cleansed — and
    /// specified now so that the next task to need one does not invent a second way out. Answering
    /// <see langword="false"/> for something never held is <c>Stat.RemoveAll</c>'s contract again,
    /// which is what lets a caller clean up unconditionally.
    /// </remarks>
    /// <returns>Whether the pair was held.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> or <paramref name="source"/> is null.
    /// </exception>
    public bool Release(IEffect effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        int index = IndexOf(effect, source);

        if (index < 0)
        {
            return false;
        }

        DropAt(index);

        _effects.Remove(effect, source);

        return true;
    }

    /// <summary>
    /// Forgets everything without removing anything from anywhere.
    /// </summary>
    /// <remarks>
    /// What the run's end calls. There is nothing left to remove <em>from</em> — the scope is going
    /// away and every live object with it — and a removal here would publish a shield expiring into
    /// a <c>RunScope</c> being torn down, which is <c>ProjectileSystem.Clear</c>'s argument exactly.
    /// The references are cleared rather than left behind, so a finished run's <c>ActiveSpec</c>s are
    /// not held alive by a table the next run will reuse.
    /// </remarks>
    public void Clear()
    {
        for (int i = 0; i < _count; i++)
        {
            _held[i] = null;
            _sources[i] = null;
            _expiresAt[i] = 0f;
        }

        _count = 0;
    }

    /// <summary>Where this exact pair sits, or −1. Identity only, never equality.</summary>
    private int IndexOf(IEffect effect, object source)
    {
        for (int i = 0; i < _count; i++)
        {
            if (ReferenceEquals(_held[i], effect) && ReferenceEquals(_sources[i], source))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Shifts the entry at <paramref name="index"/> out, keeping everything after it in order.
    /// </summary>
    /// <remarks>
    /// Shifted rather than swapped with the last entry, for <c>GrantedShieldPool.Remove</c>'s reason:
    /// the order entries sit in is the order <see cref="Tick"/> reads, and a swap would silently
    /// reorder the table every time something expired.
    /// </remarks>
    private void DropAt(int index)
    {
        for (int i = index; i < _count - 1; i++)
        {
            _held[i] = _held[i + 1];
            _sources[i] = _sources[i + 1];
            _expiresAt[i] = _expiresAt[i + 1];
        }

        _count--;

        // The vacated slot is cleared rather than left: a stale reference would hold an ActiveSpec
        // alive past the run that cast it, and would make a later IndexOf answer about a ghost.
        _held[_count] = null;
        _sources[_count] = null;
        _expiresAt[_count] = 0f;
    }
}
