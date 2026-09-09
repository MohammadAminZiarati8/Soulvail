using System;
using Soulvail.Core.Content;

namespace Soulvail.Core.Combat;

/// <summary>
/// What one tick of a weapon produced: nothing, the start of a swing, the moment its damage lands,
/// or — when a swing ends and the next begins on the same tick — both.
/// </summary>
/// <remarks>
/// Returned by value rather than published, because the two moments have different audiences and
/// only the weapon's owner knows them: a swing start is a <c>PlayerAttacked</c> event for the
/// animation and the audio, while a damage frame is a <c>ConeHitIntent</c> asking the body a
/// question. Keeping both out of <see cref="Weapon"/> is what lets the same class drive an enemy's
/// attack later without publishing player events.
/// </remarks>
public readonly struct WeaponTick
{
    /// <summary>A new swing began on this tick.</summary>
    public readonly bool SwingStarted;

    /// <summary>
    /// The current swing's damage landed on this tick. Exactly once per swing, and never on a tick
    /// where the swing did not exist.
    /// </summary>
    public readonly bool DamageFrame;

    public WeaponTick(bool swingStarted, bool damageFrame)
    {
        SwingStarted = swingStarted;
        DamageFrame = damageFrame;
    }
}

/// <summary>
/// The cadence of a basic attack: when a swing starts, when its damage lands, and when the next
/// one may begin. CC §4.1–4.2 as a clock, with no idea what it is swinging at or what it hits.
/// </summary>
/// <remarks>
/// <para>
/// <b>It decides timing and nothing else.</b> Whether there is anything worth swinging at arrives
/// as the <c>targetInRange</c> argument, and what the swing actually hits is resolved by the body
/// against the <c>ConeHitIntent</c> its owner emits — this class never sees an enemy, a position or
/// a direction. That is what keeps the Censer's rhythm testable as arithmetic and reusable for
/// M5-01's projectile weapon, which differs in what it asks for and not in when.
/// </para>
/// <para>
/// <b>The interval is sampled at swing start and held.</b> A modifier that lands mid-swing changes
/// the <em>next</em> one, which is what makes M1-13's Focus ramp legible: the swing you are
/// watching finishes at the speed it began at, rather than snapping shorter as the ramp climbs.
/// Everything is scheduled as absolute times against the simulated clock its owner passes in, so
/// there is no accumulator to drift and a 30 fps phone swings at the same moments a 120 fps one
/// does.
/// </para>
/// <para>
/// <b>The damage frame belongs to the swing, not to the target.</b> Once a swing has started its
/// frame will fire, even if the enemy that provoked it walked away or died in the meantime (CC
/// §4.2's "not a commitment" cuts both ways). The cone then resolves against whatever is actually
/// standing there, which is how a swing aimed at one Husk kills the two beside it.
/// </para>
/// </remarks>
public sealed class Weapon
{
    private readonly WeaponSpec _spec;

    /// <summary>
    /// The earliest time the next swing may begin. Zero at rest, so the first tick of a run with
    /// something in range swings immediately rather than waiting out an interval nobody watched.
    /// </summary>
    private float _nextSwingAt;

    private float _swingEndsAt;
    private float _damageFrameAt;

    /// <summary>
    /// Whether the current swing has already landed its damage. Cleared at every swing start, which
    /// is what makes <see cref="WeaponTick.DamageFrame"/> once-per-swing rather than once-per-tick
    /// for the rest of the swing.
    /// </summary>
    private bool _damageFrameFired;

    /// <param name="spec">The weapon's authored numbers. Seeds both stats and is read for the arc.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    public Weapon(WeaponSpec spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));

        // Fresh Stats rather than the spec's raw numbers, because these are the live values a tree
        // node, a Pact or M1-13's Focus ramp applies a modifier to (ADR-0008). The spec stays what
        // a designer typed.
        Damage = new Stat(spec.Damage);
        FireRate = new Stat(spec.SwingsPerSecond);
    }

    /// <summary>Damage per swing, live. Where "+2 damage" and "+15 % damage" go.</summary>
    public Stat Damage { get; }

    /// <summary>Swings per second, live. Where M1-13's Focus ramp goes.</summary>
    public Stat FireRate { get; }

    /// <summary>
    /// How far the arc reaches, in metres. Forwarded from the spec rather than held as a
    /// <see cref="Stat"/>: nothing in V1 modifies a weapon's reach, and a stat with no modifier is
    /// a cache with a subscription. It becomes one the day a node wants "+2 m".
    /// </summary>
    public float Range => _spec.Range;

    /// <summary>The full opening angle of the arc, in degrees. Forwarded, like <see cref="Range"/>.</summary>
    public float ConeAngleDeg => _spec.ConeAngleDeg;

    /// <summary>A swing is in progress: started, and not yet finished.</summary>
    public bool IsSwinging { get; private set; }

    /// <summary>
    /// What a second of uninterrupted fire is expected to do to one enemy — 39 for the Censer.
    /// </summary>
    /// <remarks>
    /// An estimate for CC §3.2's finisher bonus, not a promise: it ignores the swing phase the
    /// weapon happens to be in, and against a cluster the real number is this times however many
    /// enemies are in the arc. Both are the right simplification for "could I finish this target
    /// within a second", which is a question about the target's remaining health.
    /// </remarks>
    public float DpsOneSecond => Damage.Value * FireRate.Value;

    /// <summary>
    /// Advances the swing clock to <paramref name="now"/> and reports what happened on the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Order within the tick is the whole of rules 3 and 4: the running swing lands its damage
    /// first, then ends, and only then may the next one start — so a tick that arrives exactly on
    /// the end of a swing reports the new <see cref="WeaponTick.SwingStarted"/> without a dead
    /// frame between them, and a swing can never end before the damage it owed.
    /// </para>
    /// <para>
    /// At most one damage frame per tick. Two swings can only be spanned by a <c>dt</c> longer than
    /// a whole swing interval, which the snapshot's 50 ms clamp puts out of reach of anything the
    /// game ships — the Censer's is 333 ms, and 128 ms at the fastest a Focus ramp can drive it.
    /// </para>
    /// </remarks>
    /// <param name="dt">
    /// Seconds since the previous tick. Deliberately unread: every schedule here is an absolute
    /// time against <paramref name="now"/>, which is the same clock and cannot drift the way a
    /// summed accumulator can. It stays in the signature because every <c>Tick</c> in core takes
    /// one, and a weapon that grows a per-frame ramp of its own will want it.
    /// </param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <param name="targetInRange">
    /// Whether there is something worth swinging at right now. Its owner decides what that means —
    /// for <c>PlayerCombat</c> it is a live, unblocked target inside <see cref="Range"/>.
    /// </param>
    public WeaponTick Tick(float dt, float now, bool targetInRange)
    {
        bool damageFrame = false;

        if (IsSwinging)
        {
            if (!_damageFrameFired && now >= _damageFrameAt)
            {
                damageFrame = true;
                _damageFrameFired = true;
            }

            if (now >= _swingEndsAt)
            {
                IsSwinging = false;
            }
        }

        bool swingStarted = false;

        if (!IsSwinging && targetInRange && now >= _nextSwingAt && TryGetInterval(out float interval))
        {
            IsSwinging = true;
            swingStarted = true;

            _damageFrameAt = now + (_spec.DamageFrame * interval);
            _damageFrameFired = false;
            _swingEndsAt = now + interval;

            // The next swing may begin the moment this one ends — CC §4.2's rate is a cadence, not
            // a cooldown that starts counting afterwards.
            _nextSwingAt = _swingEndsAt;
        }

        return new WeaponTick(swingStarted, damageFrame);
    }

    /// <summary>
    /// Back to rest: no swing in progress, and ready to start one on the next tick that has
    /// something in range.
    /// </summary>
    /// <remarks>
    /// What a new stage or a respawn gets, for the reason <c>Health.Reset</c> and
    /// <c>Targeter.Reset</c> exist. The modifier stacks are deliberately left alone — a reset is a
    /// fresh swing clock, not a stripped character, and whatever put a modifier on this weapon is
    /// the only thing that knows whether it should still be there.
    /// </remarks>
    public void Reset()
    {
        IsSwinging = false;
        _damageFrameFired = false;
        _nextSwingAt = 0f;
        _swingEndsAt = 0f;
        _damageFrameAt = 0f;
    }

    /// <summary>
    /// The seconds one swing takes at the current fire rate, or <see langword="false"/> when there
    /// is no such thing.
    /// </summary>
    /// <remarks>
    /// <see cref="Stat"/> deliberately clamps nothing (ADR-0008), so a stack of modifiers can drive
    /// <see cref="FireRate"/> to zero or below — a −100 % PercentMult is a legitimate way for a
    /// future Pact to say "your weapon is silenced". Read literally that is an infinite or negative
    /// interval, and a swing scheduled to end at infinity would leave <see cref="IsSwinging"/> true
    /// for the rest of the run, with the weapon jammed rather than silenced. Refusing to start the
    /// swing at all says the same thing and says it reversibly: take the modifier off and the next
    /// tick swings.
    /// </remarks>
    private bool TryGetInterval(out float interval)
    {
        float rate = FireRate.Value;

        if (!(rate > 0f) || float.IsInfinity(rate))
        {
            interval = 0f;
            return false;
        }

        interval = 1f / rate;
        return true;
    }
}
