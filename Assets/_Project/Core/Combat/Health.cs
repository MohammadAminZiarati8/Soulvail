using System;
using Soulvail.Core.Content;

namespace Soulvail.Core.Combat;

/// <summary>
/// One health component for everything that can be hurt — the player, every enemy, every boss.
/// Granted shield, then the Aegis, then HP; a refill after quiet time, hit i-frames, death. See
/// CC §2.5, §6.1 and §6.4, CH §3.1, and AR §3, which puts all of it in core.
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
/// <b>Two numbers here are <see cref="Stat"/>s and the rest are not.</b> Current HP is not a
/// gameplay number a modifier can apply to — it is state — and two of the shield's three are
/// still authored data (see <see cref="ShieldSpec"/>). <see cref="MaxHp"/> is a stat because
/// tree nodes, Pacts and depth scaling all move it, and this component follows it live: see
/// rule 6 on <see cref="OnMaxHpChanged"/>. <see cref="ShieldRechargeDelay"/> joined it at
/// M3-12a, and says on itself why the Aegis's other two did not.
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

    /// <summary>
    /// The points a cast put on this thing, per source — rule 5's third pool, spent before the
    /// Aegis. Its own type because one float cannot say what a source has left; see
    /// <see cref="GrantedShieldPool"/>, which is where every rule about it lives.
    /// </summary>
    private readonly GrantedShieldPool _granted = new();

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

        // Seeded from the spec and live from here on (M3-12a rule 4), zero for anything without a
        // shield at all — which never reads it, because Tick leaves before it would.
        ShieldRechargeDelay = new Stat(shield?.RechargeDelay ?? 0f);

        _current = ClampedMax;
        _shieldCurrent = ShieldMax;

        MaxHp.Changed += OnMaxHpChanged;
    }

    /// <summary>The live maximum. Its modifier stack is where "+20 max HP" goes.</summary>
    public Stat MaxHp { get; }

    /// <summary>
    /// Seconds without being hit before the Aegis starts refilling, live. Where "−25 % Aegis
    /// delay" goes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one Aegis number a node can reach, and the other two stay authored on purpose</b>
    /// (M3-12a rule 4). CH §3.1 calls the Aegis the only regeneration in the game, and its delay
    /// is the number the player actually feels — four seconds of not being hit, in a game about
    /// not being hit. <see cref="ShieldMax"/> is refused because raising a maximum without filling
    /// it is the trap <c>Handler_MaxHpMovesHealthLive</c> already documents, and
    /// <see cref="ShieldSpec.RefillPerSecond"/> is refused because it is Unbroken's keystone
    /// (*"Aegis recharges 2× faster"*), which is not among v1's twelve nodes and should arrive
    /// whole rather than half-reachable.
    /// </para>
    /// <para>
    /// A stack can drive it non-positive or non-finite, which <see cref="Stat"/> permits and
    /// <see cref="Tick"/> answers with zero — see there.
    /// </para>
    /// </remarks>
    public Stat ShieldRechargeDelay { get; }

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
    /// Points of <em>granted</em> shield — what a cast put on, across every source. Spent before the
    /// Aegis and before HP (rule 5), and nothing to do with <see cref="Shield"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A third pool, and deliberately not the Aegis.</b> The Aegis is the Oathbound's signature —
    /// 30 points, a 4 s delay, 15/s refill, <em>"the only regeneration in the game"</em> (CH §3.1) —
    /// and <see cref="ShieldMax"/>, <see cref="ShieldFraction"/> and the ring that draws them all
    /// mean exactly what they meant before this existed. Expressing a temporary shield by raising
    /// the Aegis's maximum would have taken two changes to the class's signature mechanic — a
    /// <see cref="Stat"/> on the maximum <em>and</em> a separate fill, because raising a maximum is
    /// not filling it — to say something that is not about the Aegis at all.
    /// </para>
    /// <para>
    /// It does not recharge and it is not healed: it is a cast, and it is gone when whatever granted
    /// it says so. <see cref="Tick"/> and <see cref="Heal"/> both leave it alone, which needed
    /// nothing doing to either of them.
    /// </para>
    /// </remarks>
    public float GrantedShield => _granted.Total;

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

        // **Granted shield first, then the Aegis, then hit points** (rule 5). A cast's points are
        // temporary and the Aegis is the class's signature with its own refill, so spending the pool
        // that is about to lapse before the one that comes back on its own is the only order that
        // wastes neither. No branch guards this call: an empty pool is a loop that does not run.
        float toGranted = _granted.Spend(amount);

        float toShield = 0f;

        if (_shieldCurrent > 0f)
        {
            toShield = MathF.Min(_shieldCurrent, amount - toGranted);
            _shieldCurrent -= toShield;
        }

        // Min against Current, not the raw remainder, so overkill is reported as the damage
        // that landed rather than the damage that was swung. A hit for 10 000 on a 12 HP Husk
        // reports ToHp 12, which is what a damage-numbers view should float and what an
        // absorb-tracking node should count.
        float toHp = MathF.Min(_current, amount - toGranted - toShield);
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

        // **The granted pool counts here, and leaving it out would have been a silent buff to the
        // Aegis** (rule 5). A hit swallowed whole by a Bulwark is still a hit that got past the
        // i-frames, so it starts new ones exactly as a hit swallowed whole by the Aegis always has —
        // and it moves `_lastDamageAt`, so the Aegis's 4 s delay still restarts on every blow. Read
        // as `toShield + toHp` this branch would not run at all for a fully absorbed hit: the player
        // would take no i-frames from it, and the Aegis would refill straight through a fight.
        if (toGranted + toShield + toHp > 0f)
        {
            // hitIFrames of 0 lands this exactly on `now`, and the comparison in
            // IsInvulnerable is strict, so zero really is no i-frames rather than one frame of
            // them.
            _iFramesUntil = now + _hitIFrames;
            _lastDamageAt = now;
        }

        // **`toGranted` is deliberately not reported, and folding it into ToShield would be a
        // content bug rather than a tidy-up.** DamageResult.ToShield means *the Aegis* — CH §3.1's
        // Martyr keystone deals damage equal to everything the Aegis absorbed, and a listener
        // summing this field is the whole implementation of it, so a Bulwark's points landing in
        // there would quietly make Martyr scale with a skill it has nothing to do with. What that
        // costs is real and named: a hit absorbed entirely by granted shield leaves through
        // `PlayerDamaged(0, 0, unchanged, unchanged, blocked: false)`, which reads as a hit that did
        // nothing. M3-13b draws the granted pool on the player's bar and is the task that needs to
        // tell those apart; widening DamageResult is its call to make, not this one's.
        //
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
    /// Sets <paramref name="source"/>'s granted shield to <paramref name="amount"/> points.
    /// </summary>
    /// <remarks>
    /// <b>Per source, not per call.</b> Granting again from the same source refreshes that source's
    /// contribution rather than doubling it; two <em>different</em> sources stack, because they are
    /// two different things. <see cref="GrantedShieldPool"/> holds the rules and the reasons.
    /// </remarks>
    /// <param name="amount">
    /// The source's new contribution, zero or more. Zero is legal and means the source now holds
    /// nothing — which is a state the pool reaches on its own by being spent.
    /// </param>
    /// <param name="source">
    /// Who is granting — the <c>ActiveSpec</c> a cast came from. Identity only: it is what
    /// <see cref="RemoveGrantedShield"/> takes back by, so the same instance has to still be in hand
    /// then.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> is null. A sourceless grant could never be taken back, which is
    /// <c>Modifier</c>'s rule one layer down and would be a shield that lasted the run.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="amount"/> is negative, NaN or infinite. Spelled as the negation for the
    /// reason the constructor's guard is: every comparison against NaN is false, so the natural
    /// spelling admits one — and a NaN in the pool would make <see cref="GrantedShield"/> NaN for
    /// the rest of the run, absorbing every hit forever with nothing logged.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="GrantedShieldPool.Capacity"/> other sources already hold shield.
    /// </exception>
    public void GrantShield(float amount, object source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (!(amount >= 0f) || float.IsInfinity(amount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Granted shield must be a finite number of points, zero or more.");
        }

        _granted.Grant(amount, source);
    }

    /// <summary>
    /// Takes <paramref name="source"/>'s granted shield back — whatever is left of it.
    /// </summary>
    /// <remarks>
    /// What an expiry calls, through the handler that granted it (ADR-0009). It removes what
    /// <em>remains</em> rather than what was granted, so a player who already spent the shield loses
    /// nothing extra when it lapses and the pool cannot go below zero. A source that never granted
    /// anything is not an error — see <c>Stat.RemoveAll</c>'s contract, which is where that rule
    /// comes from.
    /// </remarks>
    /// <returns>Whether <paramref name="source"/> was holding anything to take back.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public bool RemoveGrantedShield(object source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return _granted.Remove(source);
    }

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

        float rechargeFrom = _lastDamageAt + EffectiveRechargeDelay();
        float creditedFrom = MathF.Max(now - dt, rechargeFrom);
        float credited = now - creditedFrom;

        if (!(credited > 0f))
        {
            return;
        }

        _shieldCurrent = MathF.Min(_shield.Max, _shieldCurrent + (_shield.RefillPerSecond * credited));
    }

    /// <summary>
    /// <see cref="ShieldRechargeDelay"/> as a number this class can add to a clock: the live value,
    /// or zero for anything a modifier stack drove below zero or made unreadable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// M3-12a rule 7. <see cref="Stat"/> clamps nothing (ADR-0008), so the guard belongs here,
    /// where the number finally means something — the same division of labour <c>CooldownRules</c>
    /// has with <see cref="ChargeSkill.Cooldown"/>.
    /// </para>
    /// <para>
    /// <b>Zero is the honest reading rather than a refusal.</b> A delay driven below zero says
    /// "wait no time at all", so the Aegis refills from the instant of the hit; a negative one
    /// added to the clock raw would say the same thing but would also credit the step *before* the
    /// hit landed. A non-finite one is answered the same way for the reason NaN is answered
    /// everywhere in this class: <c>_lastDamageAt + NaN</c> is NaN, every comparison against it is
    /// false, and the Aegis would simply never refill again for the rest of the run with nothing
    /// logged. Reachable by no stack anything in M3 authors, and cheap to be right about.
    /// </para>
    /// </remarks>
    private float EffectiveRechargeDelay()
    {
        float delay = ShieldRechargeDelay.Value;

        // The negated positive, so NaN falls to zero rather than through: every comparison against
        // it is false, and the natural spelling would let it past.
        return delay > 0f && !float.IsInfinity(delay) ? delay : 0f;
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
        _granted.Clear();
    }

    /// <summary>
    /// <see cref="Reset"/>'s sibling for a resumed run: back to stated absolute values rather than
    /// to full, with the same clean slate of timers behind them (M2-14b rule 3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It exists because a save carries absolutes and nothing else here accepts one.</b>
    /// <see cref="ApplyDamage"/> is the only other route to a non-full bar and it cannot be used:
    /// it spends the shield before it touches HP, so one call cannot land on a stated pair, and it
    /// starts i-frames and holds off the shield's refill — a resumed run would begin invulnerable
    /// for a beat it did not earn.
    /// </para>
    /// <para>
    /// <b>Both values are clamped rather than refused.</b> <c>RunSnapshot</c>'s constructor has
    /// already refused negatives and non-finite values at the boundary they arrive through, so
    /// what is left is a save whose numbers were legal under a <em>different</em> maximum — a
    /// stage-12 run restored after M3's tree moved <see cref="MaxHp"/>, which is a migration's
    /// problem and not a reason to refuse the run. Clamping lands the player at full instead of
    /// above it.
    /// </para>
    /// <para>
    /// <b>The timers are cleared, deliberately</b>, exactly as <see cref="Reset"/> clears them. A
    /// snapshot is taken at a stage boundary — the player has been standing at a door — so i-frames
    /// from a hit in the previous stage are long expired, and a shield already at its stated value
    /// should recharge from the first frame rather than wait out a delay measured against a run
    /// time that no longer exists.
    /// </para>
    /// </remarks>
    /// <param name="hp">Hit points to stand at, clamped into <c>[0, MaxHp.Value]</c>.</param>
    /// <param name="shield">Shield points to stand at, clamped into <c>[0, ShieldMax]</c>.</param>
    internal void Restore(float hp, float shield)
    {
        _current = Clamp(hp, ClampedMax);
        _shieldCurrent = Clamp(shield, ShieldMax);
        _externalInvulnerable = false;
        _iFramesUntil = float.NegativeInfinity;
        _lastDamageAt = float.NegativeInfinity;

        // **A resumed run comes back with no granted shield, and that is a decision rather than an
        // omission** (M3 ledger row 2). Nothing about a grant is on disk: it is derived from a cast
        // that happened, and the cast is gone — unlike a cooldown, which M3-07b could rebuild from
        // TakenNodeIds because the *node* survives. Saving one would mean writing down a source
        // identity that no longer exists to hand back to. Cleared here as well as in Reset, because
        // a snapshot is restored onto a live component and a grant left standing would outlive the
        // run that made it.
        _granted.Clear();
    }

    /// <summary>
    /// The maximum, floored at zero. <see cref="Stat"/> deliberately clamps nothing — a
    /// <c>PercentMult</c> of −1 is a legitimate way to say "this is now zero" — so the floor
    /// belongs here, where the number means hit points.
    /// </summary>
    private float ClampedMax => MathF.Max(0f, MaxHp.Value);

    /// <summary>
    /// <paramref name="value"/> into <c>[0, max]</c>, reading NaN as zero.
    /// </summary>
    /// <remarks>
    /// Spelled as <c>!(value &gt; 0f)</c> for the reason every guard in this class is: every
    /// comparison against NaN is false, so the natural spelling would let one through and a NaN
    /// in <see cref="Current"/> is permanent — <see cref="IsDead"/> would read true for the rest
    /// of the run, with nothing logged.
    /// </remarks>
    private static float Clamp(float value, float max)
    {
        if (!(value > 0f))
        {
            return 0f;
        }

        return value > max ? max : value;
    }

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
    /// it. <b>GD §10.2's Claiming is the first thing in V1 that removes max HP</b> (M6-04), and
    /// its owner answers this by calling <c>PlayerCombat.AnnounceDeath</c> after every step —
    /// the one publisher of <c>PlayerDied</c>, reachable without a damage result. Anything else
    /// that shrinks a player's maximum owes the same call.
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
