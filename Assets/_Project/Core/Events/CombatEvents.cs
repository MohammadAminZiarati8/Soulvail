using System.Numerics;

namespace Soulvail.Core.Events;

// The combat module's player-facing domain events. Grouped per module like RunEvents and
// EnemyEvents, for the same reason: an event is three lines, and reading a module's vocabulary in
// one place is worth more than one type per file. See AR §5, §8 and
// <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// These six are what happens *to* or *by* the player. What happens to an enemy is EnemyEvents'
// (M1-11's `EnemyDamaged` and `EnemyDied` join the census pair there), and the split is the same
// one `Health` makes by publishing nothing at all: one component serves both sides of every fight,
// so the owner decides which vocabulary a result is spoken in.
//
// Two of them say "focus" and mean different things, because the design does: `TargetChanged`'s
// `IsFocused` is CC §3.4's tap-to-focus, a *target* the player picked, while `FocusRampChanged` is
// CC §4.3's standing-still ramp, a *fire rate*. The second carries the longer name for that reason
// alone — a reader who greps `Focus` in this file has to be able to tell them apart on sight.

/// <summary>
/// The character is now facing a different enemy, or the same enemy in a different way. Published
/// by <c>PlayerCombat.Tick</c> whenever the targeter reports a change, and by nothing else.
/// </summary>
/// <remarks>
/// <para>
/// CC §3.5 makes reticles mandatory, and this event is their whole input: one subtle ring on
/// <see cref="Id"/>, brighter and pulsing when <see cref="IsFocused"/>, hollow with a blocked
/// glyph when <see cref="IsBlocked"/>. A player who cannot see what they are shooting at concludes
/// the auto-aim is broken even when it is choosing well, so this fires on a change of *any* of the
/// three rather than only on a change of target.
/// </para>
/// <para>
/// <see cref="Id"/> is −1 when there is nothing to face, which is how the reticle learns to go
/// away. It carries no position: the view already owns the enemy it created on <c>EnemySpawned</c>
/// and can parent a ring to it, and a position here would be one frame stale by the time it was
/// drawn.
/// </para>
/// </remarks>
public readonly struct TargetChanged
{
    /// <summary>The enemy now being faced, or −1 for none.</summary>
    public readonly int Id;

    /// <summary>
    /// This target is the one the player tapped (CC §3.4) rather than one scoring chose — the
    /// brighter, pulsing ring plus the overhead chevron of CC §3.5.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Narrower than "a focus is held". A focused enemy that has walked out of acquire range keeps
    /// the focus for two seconds while scoring picks something else to shoot at, and during those
    /// seconds the bright ring belongs on neither — so this asks whether the *current* target is
    /// the focused one, not whether a focus exists. <c>CombatBlackboard.HasFocus</c> is the other
    /// question, for the code that wants it.
    /// </para>
    /// <para>
    /// The *tap-to-focus* of CC §3.4, and nothing to do with <see cref="FocusRampChanged"/>, which
    /// is CC §4.3's standing-still fire-rate ramp. Two mechanics, one word, because the design
    /// gives both the same name.
    /// </para>
    /// </remarks>
    public readonly bool IsFocused;

    /// <summary>
    /// This target cannot be damaged right now — CC §3.6. Facing is held on it and the reticle goes
    /// hollow with the blocked glyph, which is the game saying "go around" in its own language.
    /// </summary>
    public readonly bool IsBlocked;

    public TargetChanged(int id, bool isFocused, bool isBlocked)
    {
        Id = id;
        IsFocused = isFocused;
        IsBlocked = isBlocked;
    }
}

/// <summary>
/// Something hit the player. Published by <c>PlayerCombat.ApplyDamage</c> for every call that did
/// something — damage that landed, and damage that was turned away.
/// </summary>
/// <remarks>
/// <para>
/// Carries the split (<see cref="ToShield"/>, <see cref="ToHp"/>) *and* the resulting fractions,
/// which looks redundant and is not: the split is what a damage-number view floats and what CH
/// §3.1's Martyr keystone sums, while the fractions are what the HUD's bars are set to. A listener
/// that had only the split would have to hold its own copy of the player's health to derive them,
/// which is the duplicated state AR §10.2 exists to prevent.
/// </para>
/// <para>
/// A <see cref="Blocked"/> hit reports zero on both amounts and unchanged fractions. It is still
/// published, because a hit that was shrugged off is a thing that happened and CC §7's 0.5 s of
/// i-frames are invisible unless the game says so.
/// </para>
/// </remarks>
public readonly struct PlayerDamaged
{
    /// <summary>Damage the Aegis absorbed. Zero when there is no shield, or none left.</summary>
    public readonly float ToShield;

    /// <summary>Damage that reached HP, after the shield took its share.</summary>
    public readonly float ToHp;

    /// <summary>Current HP over the live maximum, after this hit.</summary>
    public readonly float HpFraction;

    /// <summary>Shield points over the shield's maximum, after this hit.</summary>
    public readonly float ShieldFraction;

    /// <summary>Nothing was applied: hit i-frames were running, or a Charge had raised the flag.</summary>
    public readonly bool Blocked;

    public PlayerDamaged(float toShield, float toHp, float hpFraction, float shieldFraction, bool blocked)
    {
        ToShield = toShield;
        ToHp = toHp;
        HpFraction = hpFraction;
        ShieldFraction = shieldFraction;
        Blocked = blocked;
    }
}

/// <summary>
/// The player's HP reached zero. Published exactly once per life, by the
/// <c>PlayerCombat.ApplyDamage</c> call that did it.
/// </summary>
/// <remarks>
/// It does not end the run — M1-17 wires the death flow, and M4-05's payout reads this. Publishing
/// the fact and acting on it are deliberately different jobs, because a death has several
/// audiences (the camera, the HUD, the run, the meta payout) and none of them is the one that
/// noticed.
/// </remarks>
public readonly struct PlayerDied
{
    /// <summary>Simulated run time at the moment of death, in seconds — the same clock <c>RunState.Time</c> keeps.</summary>
    public readonly float Time;

    public PlayerDied(float time)
    {
        Time = time;
    }
}

/// <summary>
/// The Aegis has changed by enough to be worth redrawing. Published by <c>PlayerCombat.Tick</c>
/// while the shield refills.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not published for every frame of the refill. At CC §7's 15 points per second into
/// a 30-point shield, one 120 fps frame moves the fraction by 0.004 — a change no one can see, and
/// 120 events a second to say so. <c>PlayerCombat</c> therefore publishes only once the fraction
/// has drifted at least 0.005 from what it last reported, which is roughly 25 events over the two
/// seconds of a full refill.
/// </para>
/// <para>
/// Damage that hits the shield does not publish this — <see cref="PlayerDamaged"/> already carries
/// <see cref="PlayerDamaged.ShieldFraction"/>, and a listener should not have to reconcile two
/// events describing one hit. This one is the quiet half: the shield coming back on its own.
/// </para>
/// </remarks>
public readonly struct PlayerShieldChanged
{
    /// <summary>Shield points over the shield's maximum, in <c>[0, 1]</c>.</summary>
    public readonly float Fraction;

    public PlayerShieldChanged(float fraction)
    {
        Fraction = fraction;
    }
}

/// <summary>
/// The character has started a swing. Published by <c>PlayerCombat.Tick</c> once per swing, at the
/// moment it begins — not when its damage lands.
/// </summary>
/// <remarks>
/// <para>
/// The animation and audio cue, and the reason it is separate from the cone request that follows
/// it: CC §4.2 puts the damage 40 % of the way through the swing, so a view that started the
/// windup on the damage frame would play the whole animation late and the game would look like it
/// was hitting before it swung. This is the start of the tell; <c>ConeHitIntent</c> is the payoff.
/// </para>
/// <para>
/// It carries no position — the view is on the player and knows where it is — but it does carry
/// the facing, because the swing arc has to be drawn along the same direction the cone will be
/// resolved along. Two answers to "which way did he swing" is one answer too many.
/// </para>
/// </remarks>
public readonly struct PlayerAttacked
{
    /// <summary>
    /// The direction the swing is centred on: a unit vector on the ground plane, <c>X</c> and
    /// <c>Z</c>. The same facing the tick's <c>ConeHitIntent</c> will carry.
    /// </summary>
    public readonly Vector2 FacingXZ;

    public PlayerAttacked(Vector2 facingXZ)
    {
        FacingXZ = facingXZ;
    }
}

/// <summary>
/// CC §4.3's Focus ramp has moved: the character has been standing still long enough for the swing
/// to be speeding up, or has just moved and lost it. Published by <c>FocusTracker.Tick</c>, and by
/// nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Standing still, not tap-to-focus.</b> The name carries "ramp" so that it cannot be read as a
/// relative of <see cref="TargetChanged.IsFocused"/> or <c>CombatBlackboard.HasFocus</c>, which are
/// CC §3.4's picked *target*. This one is a *fire rate*, driven by a centred stick, and the two
/// mechanics never interact.
/// </para>
/// <para>
/// Not published every frame of the ramp. <c>FocusTracker</c> announces a move of at least a
/// hundredth, plus either endpoint whatever the step, which is roughly twenty events over the 1.0 s
/// climb rather than sixty a second — the same bargain <see cref="PlayerShieldChanged"/> makes, for
/// the same reason.
/// </para>
/// <para>
/// It carries the fraction rather than the resulting fire rate. What a view does with Focus is
/// show how much of it there is (the ground glow's alpha and radius, GD §16.4), and a listener that
/// wanted the swings per second would be reading a number it should be getting from the weapon.
/// </para>
/// </remarks>
public readonly struct FocusRampChanged
{
    /// <summary>How far into the ramp the character is, in <c>[0, 1]</c>. Zero is no Focus at all.</summary>
    public readonly float Level;

    public FocusRampChanged(float level)
    {
        Level = level;
    }
}
