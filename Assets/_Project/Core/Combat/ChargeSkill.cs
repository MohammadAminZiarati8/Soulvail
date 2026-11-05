using System;
using System.Numerics;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>
/// The Oathbound's Charge as a clock: when a press counts, when the dash starts, how long it lasts,
/// how long it protects, and when the button lights up again. CC §5 entire, with no idea where the
/// character is or what it hits.
/// </summary>
/// <remarks>
/// <para>
/// <b>It decides timing and direction, and nothing else.</b> The 10 m of travel, the suspended
/// motor, the pass-through damage and the knockback are all M1-15's, resolved by the body against a
/// <c>ChargeIntent</c>; this class never sees a position, an enemy or a <c>Health</c>. That is what
/// keeps CC §5's feel testable as arithmetic and reusable for M5-03's Shroudstep and M6-07's Blink,
/// which differ in what happens along the path rather than in when it may be walked.
/// </para>
/// <para>
/// <b>Two of its rules exist only to absorb touch latency</b>, and they are the reason this is a
/// state machine rather than a boolean and a timer. The i-frame trail keeps a dodge that visually
/// cleared an attack from being undone by the frames between the glass and the simulation, and the
/// input buffer keeps a tap made a seventh of a second early from being thrown away. CC §5 is blunt
/// about the stakes: without them the dodge feels unreliable, and an unreliable dodge in a game
/// built on dodging is fatal.
/// </para>
/// <para>
/// <b>Everything is scheduled as an absolute time</b> against the simulated clock its owner passes
/// in — the same shape <c>Weapon</c> uses, and for the same reason. There is no accumulator to
/// drift, so a 30 fps phone dashes for exactly as long as a 120 fps one, and the properties below
/// are all read off the last time <see cref="Tick"/> was given rather than counted down.
/// </para>
/// <para>
/// <b>The cooldown is sampled when a charge starts refilling and held.</b> A modifier that lands
/// mid-cooldown changes the <em>next</em> one, which is the same promise <c>Weapon</c> makes about
/// its swing interval: the wait you are watching finishes at the length it began at, rather than
/// snapping shorter under a buff and leaving the button's fill sliding backwards.
/// </para>
/// </remarks>
public sealed class ChargeSkill
{
    private readonly MovementSkillSpec _spec;
    private readonly IDomainEvents _events;
    private int _maxCharges = 1;
    private int _spentCharges;
    private bool _guardPending;
    private float _guardUntil;

    /// <summary>
    /// The last time <see cref="Tick"/> was given. Every state property is read against this rather
    /// than against a clock of its own, so <see cref="IsActive"/> and the rest describe the tick the
    /// caller is in the middle of and cannot disagree with each other.
    /// </summary>
    private float _now;

    /// <summary>When the current dash stops. Zero at rest, which is in the past for any clock.</summary>
    private float _activeUntil;

    private float _invulnUntil;

    /// <summary>
    /// When the next spent charge refills. Zero when every charge is ready.
    /// </summary>
    private float _readyAt;

    /// <summary>When the pending press was made. Meaningless unless <see cref="_hasPress"/>.</summary>
    private float _pressAt;

    private bool _hasPress;

    /// <param name="spec">The class's authored movement skill. Seeds the cooldown stat and is read every tick.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    public ChargeSkill(MovementSkillSpec spec)
        : this(spec, null)
    {
    }

    internal ChargeSkill(MovementSkillSpec spec, IDomainEvents events)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _events = events;
        BonusCharges.Changed += OnBonusChargesChanged;

        // A fresh Stat rather than the spec's raw number, because this is the live value M3-12's
        // tree nodes and a future Pact apply a modifier to (ADR-0008). The spec stays what a
        // designer typed.
        Cooldown = new Stat(spec.Cooldown);

        // The second of them, as of M3-12a rule 3. Damage is here rather than on the spec because
        // a node moves it; Knockback deliberately stays authored — 4 m is a positioning number,
        // and a node that moved it would need a playtest of its own.
        Damage = new Stat(spec.Damage);

        // And a Blink's pool, three more the day they are authored (M6-07b rule 11): an authored
        // number a node will move is a Stat from the start (ADR-0008), or it is the one movement
        // number no node could ever reach. Zero on every kind that leaves no pool.
        PoolRadius = new Stat(spec.PoolRadius);
        PoolDuration = new Stat(spec.PoolDuration);
        PoolDamagePerPulse = new Stat(spec.PoolDamagePerPulse);
    }

    /// <summary>
    /// Seconds between dashes, live. Where "−15 % dash cooldown" goes, under M3-06's 40 % floor.
    /// </summary>
    public Stat Cooldown { get; }

    public Stat BonusCharges { get; } = new(0f);

    public Stat RollGuardSeconds { get; } = new(0f);

    public int MaxCharges => _maxCharges;

    public int ReadyCharges => MaxCharges - _spentCharges;

    public bool IsGuarding => IsGuardingAt(_now);

    internal float GuardRemaining => _guardUntil - _now;

    internal bool IsGuardingAt(float now) => _guardUntil > 0f && now < _guardUntil;

    internal bool ConsumeGuard()
    {
        if (!IsGuarding)
        {
            return false;
        }

        _guardUntil = 0f;
        return true;
    }

    /// <summary>
    /// What one pass-through of the dash is worth, live. Where "+50 % Charge damage" goes.
    /// </summary>
    /// <remarks>
    /// <b>This class never reads it.</b> It is held here because this is the object a modifier has
    /// to be able to reach — the dash is what a node names — while the damage itself is dealt a
    /// layer out, by <c>PlayerCombat.ResolveChargeHits</c>, which is the only thing that knows who
    /// was passed through. The same split <see cref="Cooldown"/> has with <c>CooldownRules</c>:
    /// the number lives with the skill, the arithmetic lives with the caller.
    /// </remarks>
    public Stat Damage { get; }

    /// <summary>How far a Blink's fire pool reaches, live. 0 on a movement skill that leaves none.</summary>
    /// <remarks>
    /// <b>This class never reads any of the three</b>, for <see cref="Damage"/>'s reason: the pool
    /// is dropped a layer out, by <c>PlayerCombat</c>'s start edge, which reads all three at the
    /// moment of the drop and floors them there — a <see cref="Stat"/> clamps nothing, and a number
    /// driven to zero or below means no pool rather than a <c>ZoneSystem.Spawn</c> that throws out
    /// of a dash. <b>No <c>PlayerStat</c> addresses any of them yet</b>, which is M3-12a's sequence:
    /// M6-08's Ash branch is where the first address arrives.
    /// </remarks>
    public Stat PoolRadius { get; }

    /// <summary>How long a Blink's fire pool burns, live, in simulated seconds.</summary>
    public Stat PoolDuration { get; }

    /// <summary>What one pulse of a Blink's fire pool takes off, live.</summary>
    public Stat PoolDamagePerPulse { get; }

    /// <summary>
    /// The direction of the current dash, or of the last one — a unit vector on the ground plane,
    /// in XZ. Zero before the first dash of a run and after <see cref="Reset"/>.
    /// </summary>
    /// <remarks>
    /// Fixed at the moment the dash starts and never steered afterwards, which is CC §5's whole
    /// bargain: the dash is a commitment made with the stick where it was, so a player who
    /// mis-aimed one has to live in it for 0.22 s rather than curving out of the mistake.
    /// </remarks>
    public Vector2 Direction { get; private set; }

    /// <summary>The dash is in flight: started, and not yet finished.</summary>
    /// <remarks>
    /// The window M1-15 suspends the motor for and resolves pass-through hits in — and, from
    /// M1-15, the window that also counts as movement for CC §4.3's Focus ramp, which is why a dash
    /// cannot pay out for the standing still it is meant to be the alternative to.
    /// </remarks>
    public bool IsActive => _now < _activeUntil;

    /// <summary>
    /// Damage is being ignored: the whole of the dash plus <see cref="MovementSkillSpec.IFrameTrail"/>.
    /// </summary>
    /// <remarks>
    /// Wider than <see cref="IsActive"/> on purpose and by exactly the trail. The two are separate
    /// properties rather than one because they answer different questions — where am I, versus can I
    /// be hurt — and M1-15 reads them at different places.
    /// </remarks>
    public bool IsInvulnerable => _now < _invulnUntil;

    /// <summary>The button is live when a charge is ready and no dash is in flight.</summary>
    public bool IsReady => !IsActive && ReadyCharges > 0;

    /// <summary>
    /// The cooldown fraction of the charge currently refilling, or zero when all are ready.
    /// </summary>
    /// <remarks>
    /// Measured against the <em>current</em> <see cref="Cooldown"/> value rather than the one
    /// sampled at the start of the dash, which is what CC §5's spec asks for and what makes a
    /// mid-cooldown buff visible: the wait itself does not shorten (see the class remarks), but the
    /// fill jumps down to show that the next one will be.
    /// </remarks>
    public float CooldownFraction
    {
        get
        {
            float remaining = _spentCharges > 0 ? _readyAt - _now : 0f;

            // Negated rather than `remaining <= 0f` so a non-finite clock reads as ready rather
            // than as a NaN fraction, which would leave M1-16's radial fill undrawable.
            if (!(remaining > 0f))
            {
                return 0f;
            }

            float cooldown = Cooldown.Value;

            // Stat deliberately clamps nothing (ADR-0008), so a stack of modifiers can drive this to
            // zero or below. There is still a real wait — _readyAt was fixed when the dash started —
            // and a full fill is the honest way to draw a wait whose length is unmeasurable. Dividing
            // anyway would report infinity or NaN for a button the player is watching.
            if (!(cooldown > 0f) || float.IsInfinity(cooldown))
            {
                return 1f;
            }

            float fraction = remaining / cooldown;

            return fraction < 1f ? fraction : 1f;
        }
    }

    /// <summary>
    /// Records a press. It fires on the next <see cref="Tick"/> where a dash is possible, provided
    /// that tick is within <see cref="MovementSkillSpec.InputBuffer"/> of this moment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The latest press wins, and there is no queue. Two taps inside the buffer are a player asking
    /// for one dash impatiently, not for two — and a queue would spend the second one the instant
    /// the first dash ended, sending them somewhere they asked to go a cooldown ago.
    /// </para>
    /// <para>
    /// <b>A <see cref="MovementSkillKind.None"/> class ignores the press</b> (RS-03a rule 7). Refused
    /// here, at the one door a press comes through, so <see cref="Tick"/> never has one to spend:
    /// no dash starts, no cooldown runs and <see cref="IsActive"/> is never true. The keyboard's
    /// Space still reaches this method on such a class, and this is what makes it do nothing.
    /// </para>
    /// </remarks>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    public void Request(float now)
    {
        if (_spec.Kind == MovementSkillKind.None)
        {
            return;
        }

        _pressAt = now;
        _hasPress = true;
    }

    /// <summary>
    /// Advances the clock to <paramref name="now"/> and starts the dash if a live press has finally
    /// become possible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the whole of rules 2 and 3: the clock moves first, so every property below
    /// describes this tick; then a press too old to matter is thrown away; then the two things that
    /// merely postpone it — a dash already in flight, a cooldown still running — leave it pending.
    /// That distinction is the input buffer. A press that is only early is kept, and a press that is
    /// stale is dropped, so "a tap 0.15 s before the cooldown ends still fires" and "a tap two
    /// seconds ago does not" are the same rule read at two ages.
    /// </para>
    /// <para>
    /// At most one dash per tick, and none while one is in flight.
    /// </para>
    /// </remarks>
    /// <param name="dt">
    /// Seconds since the previous tick. Deliberately unread, for the reason <c>Weapon.Tick</c> gives:
    /// every schedule here is an absolute time against <paramref name="now"/>, which is the same
    /// clock and cannot drift the way a summed accumulator can. It stays in the signature because
    /// every <c>Tick</c> in core takes one.
    /// </param>
    /// <param name="now">Simulated run time, in seconds.</param>
    /// <param name="stickXZ">
    /// The move stick this tick, on the ground plane. Any non-zero length aims the dash; it is
    /// normalised here, so a half-pushed stick dashes exactly as far as a full one.
    /// </param>
    /// <param name="facingXZ">
    /// Where the character is looking, as a unit vector on the ground plane — <c>PlayerMotor.Facing</c>
    /// flattened, which is always unit. Used only when the stick is neutral (CC §5), which is what
    /// makes a dash with no stick a step forward rather than a dash to nowhere.
    /// </param>
    /// <returns><see langword="true"/> on the tick a dash starts, and only then.</returns>
    public bool Tick(float dt, float now, Vector2 stickXZ, Vector2 facingXZ, bool allowStart = true)
    {
        _now = now;
        RefillCharges(now);
        if (_guardPending && !IsActive)
        {
            _guardPending = false;
            float seconds = RollGuardSeconds.Value;
            if (seconds > 0f)
            {
                _guardUntil = _activeUntil + seconds;
            }
        }

        if (!allowStart || !_hasPress)
        {
            return false;
        }

        // Negated rather than `now - _pressAt > InputBuffer`, so a press whose age is unreadable is
        // discarded rather than held forever: every comparison against NaN is false, and the
        // positive spelling would keep such a press pending for the rest of the run and fire it at
        // the first opportunity.
        if (!(now - _pressAt <= _spec.InputBuffer))
        {
            _hasPress = false;

            return false;
        }

        // Still early rather than too late: the press stays pending and is re-examined next tick,
        // until either it becomes possible or it goes stale above.
        if (!IsReady)
        {
            return false;
        }

        Direction = TryNormalise(stickXZ, out Vector2 aimed) ? aimed : facingXZ;

        _activeUntil = now + _spec.Duration;
        _invulnUntil = _activeUntil + _spec.IFrameTrail;

        if (_spentCharges == 0)
        {
            _readyAt = now + RechargeSeconds();
        }

        _spentCharges++;
        _guardUntil = 0f;
        _guardPending = true;
        _hasPress = false;
        PublishChargesChanged();

        return true;
    }

    /// <summary>
    /// Back to rest: no dash, no i-frames, no pending press, and ready to fire on the next tick.
    /// </summary>
    /// <remarks>
    /// What a new stage or a respawn gets, for the reason <c>Weapon.Reset</c> and
    /// <c>Health.Reset</c> exist. Like <c>Weapon.Reset</c> and unlike <c>FocusTracker.Reset</c> it
    /// leaves the modifier stack alone: this class puts nothing on its own <see cref="Cooldown"/>,
    /// so everything on there belongs to some other source, and only that source knows whether it
    /// should still be there.
    /// <para>
    /// The clock itself is deliberately not rewound. It is a reading of the caller's time, not state
    /// this object owns, and zeroing it would make the properties briefly describe a moment that
    /// never happened.
    /// </para>
    /// </remarks>
    public void Reset()
    {
        _activeUntil = 0f;
        _invulnUntil = 0f;
        _pressAt = 0f;
        _hasPress = false;

        Direction = Vector2.Zero;
        ResetSecondWind();
    }

    internal void ResetSecondWind()
    {
        _guardUntil = 0f;
        _guardPending = false;
        _readyAt = 0f;
        if (_spentCharges > 0)
        {
            _spentCharges = 0;
            PublishChargesChanged();
        }
    }

    private void OnBonusChargesChanged(Stat stat)
    {
        float bonus = stat.Value;
        int max = bonus > 0f ? 1 + (int)Math.Min(bonus, int.MaxValue - 1d) : 1;
        if (max == _maxCharges)
        {
            return;
        }

        _maxCharges = max;
        _spentCharges = Math.Min(_spentCharges, max);
        PublishChargesChanged();
    }

    private void RefillCharges(float now)
    {
        while (_spentCharges > 0 && now >= _readyAt)
        {
            _spentCharges--;
            _readyAt = _spentCharges > 0 ? _readyAt + RechargeSeconds() : 0f;
            PublishChargesChanged();
        }
    }

    private float RechargeSeconds() => CooldownRules.Effective(_spec.Cooldown, Cooldown.Value);

    private void PublishChargesChanged() =>
        _events?.Publish(new ChargeChargesChanged(ReadyCharges, MaxCharges));

    /// <summary>
    /// The unit vector along <paramref name="v"/>, or <see langword="false"/> when there is no such
    /// thing — a neutral stick, or one that arrived unreadable.
    /// </summary>
    /// <remarks>
    /// Asked as `!(lengthSquared &gt; 0f)` so that a NaN stick falls through to the facing rather
    /// than aiming the dash at NaN, which would put the character nowhere and leave every later
    /// distance comparison against its position false.
    /// </remarks>
    private static bool TryNormalise(Vector2 v, out Vector2 unit)
    {
        float lengthSquared = v.LengthSquared();

        if (!(lengthSquared > 0f) || float.IsInfinity(lengthSquared))
        {
            unit = Vector2.Zero;

            return false;
        }

        unit = v / MathF.Sqrt(lengthSquared);

        return true;
    }
}
