using System;
using Soulvail.Core.Content;

namespace Soulvail.Core.Combat;

/// <summary>
/// One health component for everything that can be hurt — the player, every enemy, every boss.
/// Shield before HP, a refill after quiet time, hit i-frames, death. See CC §2.5 and §6.1,
/// CH §3.1, and AR §3, which puts all of it in core.
/// </summary>
/// <remarks>
/// <para>
/// <b>Timed by the caller, and event-free.</b> Every method that cares about time is handed
/// <c>now</c>, because core never reads a clock (AR §4.4) and the run's simulated time is the
/// sum of each tick's <c>Dt</c>. And nothing here publishes: the owner reads the
/// <see cref="DamageResult"/> and decides what that means — <c>PlayerCombat</c> (M1-08)
/// publishes a player event, <c>EnemySystem</c> (M1-11) an enemy one. A component used by both
/// sides of every fight cannot name either.
/// </para>
/// <para>
/// <b>The clock it believes is the last one it was told.</b> <see cref="IsInvulnerable"/> takes
/// no argument, so <see cref="Tick"/> and <see cref="ApplyDamage"/> both record <c>now</c> as
/// they pass. A caller that stops ticking a health component and then reads
/// <see cref="IsInvulnerable"/> gets an answer from whenever it stopped; a caller that ticks
/// every frame, which is the contract, gets a live one.
/// </para>
/// <para>
/// <b>Nothing here is a <see cref="Stat"/> except the maximum.</b> Current HP is not a
/// gameplay number a modifier can apply to — it is state — and the shield's three numbers are
/// still authored data (see <see cref="ShieldSpec"/>). <see cref="MaxHp"/> is a stat because
/// tree nodes, Pacts and depth scaling all move it, and this component follows it live: see
/// rule 6 on <see cref="OnMaxHpChanged"/>.
/// </para>
/// <para>
/// <b>Reusable rather than disposable.</b> Subscribing to <see cref="Stat.Changed"/> means the
/// stat holds a reference to this object, so a <see cref="Health"/> cannot outlive its
/// <see cref="MaxHp"/> — which is the safe direction, since the two are always owned together
/// and die together. <see cref="Reset"/> is what a pooled agent (M1-19) calls instead of
/// rebuilding the pair.
/// </para>
/// </remarks>
public sealed class Health
{
    private readonly ShieldSpec _shield;
    private readonly float _hitIFrames;

    private float _current;
    private float _shieldCurrent;
    private bool _externalInvulnerable;

    /// <summary>The last <c>now</c> handed to <see cref="Tick"/> or <see cref="ApplyDamage"/>.</summary>
    private float _now;

    /// <summary>
    /// Hit i-frames are active while <see cref="_now"/> is before this. Negative infinity means
    /// "never hit", which is the honest starting value: any real <c>now</c> is after it, so a
    /// fresh component is vulnerable without needing a separate flag to say so.
    /// </summary>
    private float _iFramesUntil = float.NegativeInfinity;

    /// <summary>
    /// When damage last got through. Negative infinity so that a component which has never been
    /// hurt is already past its recharge delay — vacuously true, since it also has a full shield.
    /// </summary>
    private float _lastDamageAt = float.NegativeInfinity;

    /// <param name="maxHp">
    /// The live maximum. Followed rather than copied: this component subscribes to
    /// <see cref="Stat.Changed"/> for as long as it exists.
    /// </param>
    /// <param name="shield">
    /// The class's shield, or <see langword="null"/> for anything without one — which is every
    /// enemy and every class but the Oathbound.
    /// </param>
    /// <param name="hitIFrames">
    /// Seconds of invulnerability after a hit lands. 0.5 for the Oathbound (CC §7); 0 for
    /// enemies, which take every hit that reaches them.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="maxHp"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="hitIFrames"/> is negative, NaN or infinite. Infinity is called out
    /// because it is the one value that looks like a number and means "invulnerable forever" —
    /// a state this class supports deliberately through
    /// <see cref="SetExternalInvulnerable"/> and should never fall into by arithmetic.
    /// </exception>
    public Health(Stat maxHp, ShieldSpec shield, float hitIFrames)
    {
        MaxHp = maxHp ?? throw new ArgumentNullException(nameof(maxHp));

        // `!(hitIFrames >= 0f)` rather than `hitIFrames < 0f`, for the reason MovementSpec
        // documents: every comparison against NaN is false, so the natural spelling admits NaN.
        if (!(hitIFrames >= 0f) || float.IsInfinity(hitIFrames))
        {
            throw new ArgumentOutOfRangeException(
                nameof(hitIFrames),
                hitIFrames,
                "hitIFrames must be a finite number of seconds, zero or more. Zero means no " +
                "i-frames at all, which is what enemies use.");
        }

        _shield = shield;
        _hitIFrames = hitIFrames;

        _current = ClampedMax;
        _shieldCurrent = ShieldMax;

        MaxHp.Changed += OnMaxHpChanged;
    }

    /// <summary>The live maximum. Its modifier stack is where "+20 max HP" goes.</summary>
    public Stat MaxHp { get; }

    /// <summary>Current HP, in <c>[0, MaxHp.Value]</c>.</summary>
    public float Current => _current;

    /// <summary>
    /// <see cref="Current"/> over the *live* maximum, for HP bars and the low-HP triggers of
    /// CH §6 — so a node that raises max HP moves the bar's fill immediately, without a heal.
    /// Zero when the maximum is not positive, rather than a division by it.
    /// </summary>
    public float Fraction
    {
        get
        {
            float max = ClampedMax;
            return max > 0f ? _current / max : 0f;
        }
    }

    /// <summary>
    /// Whether this thing has a shield <em>at all</em> — not whether one is currently up. A HUD
    /// asking "do I draw the Aegis ring" wants this; a rule asking "is the shield holding"
    /// wants <c>Shield &gt; 0</c>.
    /// </summary>
    public bool HasShield => _shield is not null;

    /// <summary>Points of shield remaining. Zero when there is no shield.</summary>
    public float Shield => _shieldCurrent;

    /// <summary>The shield's authored maximum. Zero when there is no shield.</summary>
    public float ShieldMax => _shield?.Max ?? 0f;

    /// <summary><see cref="Shield"/> over <see cref="ShieldMax"/>; zero when there is no shield.</summary>
    public float ShieldFraction => _shield is null ? 0f : _shieldCurrent / _shield.Max;

    /// <summary>
    /// HP has reached zero. Spelled as the negation of "alive" so that a maximum which somehow
    /// went non-finite reads as dead — loudly wrong — rather than as permanently unkillable.
    /// </summary>
    public bool IsDead => !(_current > 0f);

    /// <summary>
    /// Damage will not get through: hit i-frames are still running, or something raised the
    /// external flag. The two are independent — see <see cref="SetExternalInvulnerable"/>.
    /// </summary>
    public bool IsInvulnerable => _externalInvulnerable || _now < _iFramesUntil;

    /// <summary>
    /// Applies <paramref name="amount"/> at time <paramref name="now"/>: shield first, the
    /// remainder to HP.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three ways nothing happens, and the difference between them is deliberate. A
    /// non-positive amount and an already-dead target both report
    /// <see cref="DamageResult.None"/> — there was nothing to do. An invulnerable target
    /// reports <see cref="DamageResult.Blocked"/> — something arrived and was turned away,
    /// which a view should show. Death wins over invulnerability, because a corpse with
    /// i-frames still running is not blocking anything.
    /// </para>
    /// <para>
    /// A call that applies nothing also starts nothing: rule 3 keys off
    /// <see cref="DamageResult.Applied"/>, not off having been called, so a blocked hit does not
    /// extend its own i-frames and a zero-damage hit does not hold the shield off.
    /// </para>
    /// </remarks>
    /// <param name="amount">
    /// Damage to apply. Zero, negative and NaN are all no-ops — the guard's spelling refuses
    /// NaN rather than letting it into <see cref="Current"/>.
    /// </param>
    /// <param name="now">Simulated run time, in seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="now"/> is NaN or infinite.</exception>
    public DamageResult ApplyDamage(float amount, float now)
    {
        _now = FiniteTime(now);

        if (IsDead)
        {
            return DamageResult.None;
        }

        if (!(amount > 0f))
        {
            return DamageResult.None;
        }

        if (IsInvulnerable)
        {
            return new DamageResult(0f, 0f, blocked: true, killed: false);
        }

        float toShield = 0f;

        if (_shieldCurrent > 0f)
        {
            toShield = MathF.Min(_shieldCurrent, amount);
            _shieldCurrent -= toShield;
        }

        // Min against Current, not the raw remainder, so overkill is reported as the damage
        // that landed rather than the damage that was swung. A hit for 10 000 on a 12 HP Husk
        // reports ToHp 12, which is what a damage-numbers view should float and what an
        // absorb-tracking node should count.
        float toHp = MathF.Min(_current, amount - toShield);
        _current -= toHp;

        // Both subtractions are exact by construction — each amount came from a Min against the
        // value it is taken from — but float dust from a long stream of hits is cheap to refuse
        // and a negative Current would make Fraction negative rather than merely zero.
        if (_current < 0f)
        {
            _current = 0f;
        }

        if (_shieldCurrent < 0f)
        {
            _shieldCurrent = 0f;
        }

        if (toShield + toHp > 0f)
        {
            // hitIFrames of 0 lands this exactly on `now`, and the comparison in
            // IsInvulnerable is strict, so zero really is no i-frames rather than one frame of
            // them.
            _iFramesUntil = now + _hitIFrames;
            _lastDamageAt = now;
        }

        // Unreachable while alive on entry, which is guaranteed above — so this reads "did that
        // finish it" rather than "was it already over".
        return new DamageResult(toShield, toHp, blocked: false, killed: IsDead);
    }

    /// <summary>
    /// Restores HP up to the live maximum and reports how much actually went in — which is what
    /// a healing view floats, and what a lifesteal node needs to know it did nothing.
    /// </summary>
    /// <remarks>
    /// Untimed on purpose: healing neither starts i-frames nor holds off the shield's refill.
    /// Only damage does either, and a heal that reset the Aegis delay would make a Consecrate
    /// zone (M3-11) actively counterproductive for the Oathbound.
    /// </remarks>
    /// <param name="amount">
    /// Points to restore. Zero, negative and NaN all heal nothing and return zero.
    /// </param>
    /// <returns>The amount actually healed; zero when dead or already full.</returns>
    public float Heal(float amount)
    {
        if (IsDead || !(amount > 0f))
        {
            return 0f;
        }

        float max = ClampedMax;
        float headroom = max - _current;

        if (!(headroom > 0f))
        {
            return 0f;
        }

        float healed = MathF.Min(amount, headroom);
        _current += healed;

        if (_current > max)
        {
            _current = max;
        }

        return healed;
    }

    /// <summary>
    /// Raises or clears invulnerability from an outside source — the Charge dash of M1-14, a
    /// Bulwark, a cutscene.
    /// </summary>
    /// <remarks>
    /// Independent of hit i-frames in both directions: turning it on does not extend them, and
    /// turning it off does not cut them short. The two have different owners — i-frames belong
    /// to this component and expire on their own clock, while the external flag belongs to
    /// whoever raised it and is only ever lowered by that same caller.
    /// </remarks>
    public void SetExternalInvulnerable(bool on) => _externalInvulnerable = on;

    /// <summary>
    /// Advances the shield's refill. The only thing in this class that a frame drives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the part of the step that is actually past the recharge delay counts. The step
    /// covers <c>[now - dt, now]</c>, the refill is allowed from <c>lastDamageAt +
    /// RechargeDelay</c>, and the overlap is what gets credited — so the Aegis is worth the same
    /// after a 4 s wait whether the game is running at 30 fps or 120, instead of being handed up
    /// to a whole frame of free shield by whichever tick happens to straddle the deadline. At
    /// <c>Dt</c>'s 50 ms clamp that would be 0.75 points, every time.
    /// </para>
    /// <para>
    /// The step's start is taken as <c>now - dt</c> rather than remembered from the last call,
    /// so one <see cref="Tick"/> is self-consistent whatever happened between it and the last
    /// one.
    /// </para>
    /// </remarks>
    /// <param name="dt">Seconds since the previous tick — the snapshot's <c>Dt</c>.</param>
    /// <param name="now">Simulated run time at the end of this step, in seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="dt"/> is negative, NaN or infinite, or <paramref name="now"/> is NaN or
    /// infinite. A negative step would drain the shield rather than fill it; a non-finite one
    /// would put NaN into the shield and, through <see cref="_lastDamageAt"/>, stop it
    /// recharging for the rest of the run without anything being logged.
    /// </exception>
    public void Tick(float dt, float now)
    {
        _now = FiniteTime(now);

        if (!(dt >= 0f) || float.IsInfinity(dt))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dt),
                dt,
                "dt must be a finite number of seconds, zero or more.");
        }

        if (_shield is null || IsDead || _shieldCurrent >= _shield.Max)
        {
            return;
        }

        float rechargeFrom = _lastDamageAt + _shield.RechargeDelay;
        float creditedFrom = MathF.Max(now - dt, rechargeFrom);
        float credited = now - creditedFrom;

        if (!(credited > 0f))
        {
            return;
        }

        _shieldCurrent = MathF.Min(_shield.Max, _shieldCurrent + (_shield.RefillPerSecond * credited));
    }

    /// <summary>
    /// Back to full: HP, shield, alive, no i-frames, and the external flag down.
    /// </summary>
    /// <remarks>
    /// What a pooled enemy (M1-19) gets instead of a new <see cref="Health"/>, and what a
    /// respawn or a new run gets instead of rebuilding the player. It deliberately clears the
    /// external flag as well — a Charge that was interrupted by death would otherwise leave the
    /// next life invulnerable, with nothing left holding the flag to lower it.
    /// </remarks>
    public void Reset()
    {
        _current = ClampedMax;
        _shieldCurrent = ShieldMax;
        _externalInvulnerable = false;
        _iFramesUntil = float.NegativeInfinity;
        _lastDamageAt = float.NegativeInfinity;
    }

    /// <summary>
    /// The maximum, floored at zero. <see cref="Stat"/> deliberately clamps nothing — a
    /// <c>PercentMult</c> of −1 is a legitimate way to say "this is now zero" — so the floor
    /// belongs here, where the number means hit points.
    /// </summary>
    private float ClampedMax => MathF.Max(0f, MaxHp.Value);

    /// <remarks>
    /// A run's time is the sum of each tick's <c>Dt</c>, so one NaN or infinity would poison it
    /// permanently: <see cref="_iFramesUntil"/> and <see cref="_lastDamageAt"/> are both derived
    /// from it, every comparison against NaN is false, and the symptom would be i-frames that
    /// never trigger and a shield that never recharges — with nothing logged. Refused at the
    /// door, like every other non-finite input in this project.
    /// </remarks>
    private static float FiniteTime(float now)
    {
        if (float.IsNaN(now) || float.IsInfinity(now))
        {
            throw new ArgumentOutOfRangeException(nameof(now), now, "now must be finite.");
        }

        return now;
    }

    /// <summary>
    /// Rule 6: a maximum that drops below current HP pulls it down; one that rises leaves it
    /// alone.
    /// </summary>
    /// <remarks>
    /// The asymmetry is the point. A node granting +40 max HP that also healed 40 would make
    /// every such node a panic button, and picking one at 5 HP would be worth more than a
    /// potion. Losing the node has to cost the HP, though, or a buff cycled on and off would be
    /// a free heal each time.
    /// <para>
    /// Nothing here reports a death. A maximum driven to zero clamps <see cref="Current"/> to
    /// zero and <see cref="IsDead"/> becomes true, but no <see cref="DamageResult"/> exists to
    /// carry <c>Killed</c> — a caller watching for death through damage results alone would miss
    /// it. That is a content mistake rather than a mechanic (nothing in V1 removes max HP), and
    /// the first thing that does needs the owner to check <see cref="IsDead"/> after the change.
    /// </para>
    /// </remarks>
    private void OnMaxHpChanged(Stat stat)
    {
        // Read through the argument rather than the property: they are the same object, and this
        // way the handler cannot quietly start following a different stat than the one that
        // raised the event.
        float max = MathF.Max(0f, stat.Value);

        if (_current > max)
        {
            _current = max;
        }
    }
}
