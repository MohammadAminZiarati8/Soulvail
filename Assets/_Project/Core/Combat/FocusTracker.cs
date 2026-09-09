using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>
/// CC §4.3's Focus ramp: how long the character has been standing still, what that is worth as a
/// fraction, and the one <see cref="Modifier"/> on <c>Weapon.FireRate</c> that turns the fraction
/// into a faster swing. The *dodge, plant, burn* rhythm, and the first live modifier in the game.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not the other Focus.</b> This is standing still (CC §4.3). <c>FocusResolver</c> is the other
/// one — a tap that pins the auto-aim to an enemy (CC §3.4) — and the two share nothing but the
/// word the design gives them. The event this publishes is <see cref="FocusRampChanged"/> rather
/// than <c>FocusChanged</c> for exactly that reason: it would otherwise sit two structs away from
/// <c>TargetChanged.IsFocused</c> in <c>CombatEvents</c>, meaning something else entirely.
/// </para>
/// <para>
/// <b>It owns the stationary clock.</b> <c>CombatBlackboard.StationaryTime</c> is a mirror of
/// <see cref="StationaryTime"/>, written by <c>PlayerCombat</c> each tick and never counted
/// independently. Two counters for "how long has the stick been centred" would be the duplicated
/// state AR §10.2 exists to prevent, and they would diverge the first time one of them learned
/// that M1-15's Charge counts as movement and the other did not. This is the object that has to
/// know, so this is the object that counts.
/// </para>
/// <para>
/// <b>ADR-0008 arrives here.</b> The ramp never writes a fire rate — it adds a
/// <see cref="ModifierKind.PercentAdd"/> tagged with itself and takes it off again, so the swing
/// speeds up without a single line of combat code knowing that Focus exists. Everything else that
/// will ever touch fire rate (a tree node, a Pact, an Elite affix) lands on the same stat the same
/// way, and none of them has to be aware of this one.
/// </para>
/// <para>
/// <b>The stack is touched only when the level moves.</b> A ramp that removed and re-added its
/// modifier every frame would churn the stat's cache 60 times a second to describe a number that
/// had not changed, so <see cref="Tick"/> compares first and returns. That is also what makes rule
/// 7's "allocates nothing at a stable level" true rather than aspirational.
/// </para>
/// </remarks>
public sealed class FocusTracker
{
    /// <summary>
    /// How far <see cref="Level"/> must move before <see cref="FocusRampChanged"/> is worth
    /// publishing. A hundredth of the ramp is under a tenth of a per cent of fire rate and a
    /// third of a per cent of the glow's alpha — invisible, and 60 events a second to say so.
    /// </summary>
    /// <remarks>
    /// The two endpoints are exempt (see <see cref="Publish"/>): a ramp that stopped announcing
    /// itself a hundredth short of full would leave the glow permanently dimmer than the fire rate
    /// it is describing, and one that stopped a hundredth short of zero would leave it lit under a
    /// character who is already running.
    /// </remarks>
    private const float LevelEpsilon = 0.01f;

    private readonly FocusSpec _spec;
    private readonly Stat _fireRate;
    private readonly IDomainEvents _events;

    /// <summary>
    /// What a full ramp is worth as a <see cref="ModifierKind.PercentAdd"/>: 0.3 for the Oathbound.
    /// Zero for a class that does not ramp, which is the one value <see cref="Tick"/> checks.
    /// </summary>
    private readonly float _gain;

    /// <summary>
    /// <see cref="Level"/> as last announced, not as it stood one tick ago. The baseline the
    /// epsilon is measured from, for the reason <c>PlayerCombat</c>'s shield fraction keeps the
    /// same kind of number: against a tick-to-tick comparison a 1.0 s ramp at 120 fps moves by
    /// 0.008 a frame and would never clear the epsilon at all.
    /// </summary>
    private float _lastPublishedLevel;

    /// <param name="spec">The class's authored ramp — read every tick, never mutated.</param>
    /// <param name="fireRate">
    /// The live swings-per-second this ramp modifies: <c>Weapon.FireRate</c>. Held rather than
    /// reached through the weapon, because this class has no business knowing what a weapon is —
    /// it moves a number, and M3-12's tree nodes will point it at the same one.
    /// </param>
    /// <param name="events">Where <see cref="FocusRampChanged"/> goes.</param>
    /// <exception cref="ArgumentNullException">Any of the three is null.</exception>
    public FocusTracker(FocusSpec spec, Stat fireRate, IDomainEvents events)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _fireRate = fireRate ?? throw new ArgumentNullException(nameof(fireRate));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _gain = _spec.MaxMultiplier - 1f;
    }

    /// <summary>
    /// How far into the ramp the character is, in <c>[0, 1]</c>. Zero until
    /// <see cref="StationaryTime"/> passes the delay, 1 once the ramp is complete.
    /// </summary>
    /// <remarks>
    /// Zero for the whole run when the class's <see cref="FocusSpec.MaxMultiplier"/> is 1. That is
    /// deliberate and it is what makes "this class does not ramp" mean the same thing everywhere:
    /// no level, so no modifier, no <see cref="FocusRampChanged"/> and no ground glow. A level that
    /// climbed to 1 while nothing anywhere got faster would light a disc under the character to
    /// announce a reward that does not exist.
    /// </remarks>
    public float Level { get; private set; }

    /// <summary>
    /// Seconds the stick has been continuously centred. Reset to zero by any tick with movement,
    /// so this is "how long have I been standing still", not "how long since I last moved".
    /// </summary>
    /// <remarks>
    /// The one authoritative copy — <c>CombatBlackboard.StationaryTime</c> mirrors it. It keeps
    /// counting past the end of the ramp, unbounded, because a future node ("+1 % damage per
    /// second stationary, no cap") reads the seconds rather than the level, and a clamp here would
    /// be an invented ceiling nothing in the design asks for.
    /// </remarks>
    public float StationaryTime { get; private set; }

    /// <summary>
    /// Advances the ramp by one tick and applies whatever that did to the fire rate.
    /// </summary>
    /// <remarks>
    /// Rules 2 and 3 of M1-13, in order: the clock, then the level, then — only if the level
    /// actually moved — the modifier and the event. Cancellation is a full reset rather than a
    /// decay, which is CC §4.3's "instant on any movement input" and the whole reason the ramp is
    /// a commitment: nudging the stick costs the second you spent earning it.
    /// </remarks>
    /// <param name="dt">Seconds since the previous tick — the snapshot's <c>Dt</c>.</param>
    /// <param name="isMoving">
    /// The character is moving under its own power this tick. <c>PlayerCombat</c> derives it from
    /// the stick alone today; M1-15 adds "or a Charge is in flight", because CC §5's 10 m dash is
    /// movement whatever the stick is doing and a ramp that survived one would pay out for the
    /// dodge it is meant to be the alternative to.
    /// </param>
    public void Tick(float dt, bool isMoving)
    {
        if (isMoving)
        {
            StationaryTime = 0f;
        }
        else
        {
            StationaryTime += dt;
        }

        if (_gain == 0f)
        {
            // A class that does not ramp still counts the clock above — CC §6.4 triggers read
            // StationaryTime through the blackboard whatever the weapon does with it — but it has
            // no level, and so nothing below it either. See Level's remarks.
            return;
        }

        float level = LevelAt(StationaryTime);

        if (level == Level)
        {
            // The common case by a wide margin — every frame of a full ramp, and every frame of
            // running around — and the reason nothing below runs 60 times a second for nothing.
            return;
        }

        Level = level;

        ApplyModifier();
        Publish();
    }

    /// <summary>
    /// Back to standing-still-for-no-time: no ramp, no modifier, nothing announced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a new stage or a respawn gets, for the reason <c>Weapon.Reset</c> exists. Unlike that
    /// one it *does* take its modifier off, and the two are consistent rather than contradictory:
    /// <c>Weapon.Reset</c> leaves stacks alone because only the source of a modifier knows whether
    /// it should still be there, and here the source is this object, which knows that a level of
    /// zero has no modifier. Leaving it would be a permanent +30 % earned by standing still once.
    /// </para>
    /// <para>
    /// Publishes nothing, like <c>PlayerCombat.Reset</c>: a reset is not a cancellation, and
    /// whatever asked for one is responsible for telling the view to redraw. The announcement
    /// baseline goes back to zero with everything else, so the next ramp is reported from a
    /// starting point that matches what is on screen.
    /// </para>
    /// </remarks>
    public void Reset()
    {
        StationaryTime = 0f;
        Level = 0f;
        _lastPublishedLevel = 0f;

        _fireRate.RemoveAll(this);
    }

    /// <summary>Rule 2: <c>clamp((stationary − delay) / ramp, 0, 1)</c>.</summary>
    private float LevelAt(float stationaryTime)
    {
        float ramped = (stationaryTime - _spec.Delay) / _spec.RampTime;

        if (!(ramped > 0f))
        {
            // Negated rather than `ramped <= 0f` so a NaN clock reads as no ramp. It cannot be one
            // while dt is finite, and the safe direction is unambiguous: an unmeasurable ramp pays
            // nothing rather than paying whatever a NaN comparison happens to allow through.
            return 0f;
        }

        return ramped < 1f ? ramped : 1f;
    }

    /// <summary>
    /// Rule 3: one <see cref="ModifierKind.PercentAdd"/> of <c>Level × (MaxMultiplier − 1)</c>,
    /// sourced here, replacing whatever this tracker had on the stat before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ModifierKind.PercentAdd"/> rather than <see cref="ModifierKind.PercentMult"/>,
    /// as the spec says, and the difference is what M3-12 inherits: pooled with every other
    /// percentage on fire rate, a node that grants "+20 % attack speed" and a full Focus ramp come
    /// to ×1.5 together rather than ×1.56, so the tenth source is worth what the first was. The
    /// remark on <see cref="ModifierKind.PercentMult"/> lists Focus among the loud multipliers;
    /// this is the one place the two disagree, and the pooled kind wins because the ramp is
    /// continuous — a multiplicative factor sliding between 1.0 and 1.3 every frame would scale
    /// every other percentage on the stat by a number that never settles.
    /// </para>
    /// <para>
    /// Removed and re-added rather than edited, because a <see cref="Modifier"/> is a readonly
    /// struct in a list and there is nothing to edit. Neither call allocates: the list keeps its
    /// capacity across a removal, so after the first ramp of a run the same slot is written over
    /// and over.
    /// </para>
    /// <para>
    /// A zero-valued modifier is skipped rather than added — rule 3's <c>Level &gt; 0</c> written
    /// in the spelling that is also true at a <see cref="_gain"/> of zero. A stat carrying a
    /// permanent +0 % would be a debug panel showing arithmetic nobody performed.
    /// </para>
    /// </remarks>
    private void ApplyModifier()
    {
        _fireRate.RemoveAll(this);

        float value = Level * _gain;

        if (value != 0f)
        {
            _fireRate.Add(new Modifier(ModifierKind.PercentAdd, value, this));
        }
    }

    /// <summary>Rule 4: announce a move of at least <see cref="LevelEpsilon"/>, or either end.</summary>
    /// <remarks>
    /// The endpoints are announced whatever the step size, because they are the two the view
    /// cannot infer: <c>FocusGlowView</c> hides itself at exactly zero and is at full alpha
    /// at exactly one, and a ramp that arrived within a hundredth of either and went quiet would
    /// leave the glow visibly disagreeing with the fire rate for as long as the player stood there.
    /// </remarks>
    private void Publish()
    {
        if (Level != 0f && Level != 1f && MathF.Abs(Level - _lastPublishedLevel) < LevelEpsilon)
        {
            return;
        }

        if (Level == _lastPublishedLevel)
        {
            return;
        }

        _lastPublishedLevel = Level;

        _events.Publish(new FocusRampChanged(Level));
    }
}
