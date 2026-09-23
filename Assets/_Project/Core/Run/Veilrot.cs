using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Run;

/// <summary>
/// GD §10's corruption meter: 0–100, never decays, and the only thing in the game that makes the
/// player stronger for being in trouble.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three states are recomputed and one is a latch</b> (rule 2). GD §10.2 is a table of effects
/// <em>at</em> a meter reading, and GD §13.3's Cleanse is the whole reason the Sanctum exists, so
/// cleansing from 78 to 63 gives the 20 % maximum hit points back. Each of the three is one
/// modifier sourced to <see langword="this"/>, so <c>Stat.RemoveAll(this)</c> takes it back without
/// knowing what else was added since (ADR-0008). The Claiming is the exception GD §10.2 writes into
/// itself — <em>"permanently, until you die"</em> — and <see cref="IsClaimed"/> never goes false
/// again.
/// </para>
/// <para>
/// <b>Three sources, and the split is what makes the arithmetic separable.</b>
/// <see langword="this"/> carries the 75 row's −20 % on the maximum; <see cref="_claiming"/>
/// carries the three buffs, none of which is ever taken off; <see cref="_drain"/> carries the one
/// modifier that is rewritten once a second. Two of the three sit on <c>MaxHp</c> at the same time,
/// which is why the drain cannot share the meter's own source: cleansing below 75 would take the
/// drain off with the threshold.
/// </para>
/// <para>
/// <b><see cref="Spend"/> is not <see cref="Cleanse"/>, and the difference is the refusal</b>
/// (M6-07c rule 5). A cleanse clamps at zero and wastes the remainder, because buying one at 8 Rot is
/// the player's decision; a spend is a <em>price</em>, so it throws when the meter is short and
/// <see cref="CanSpend"/> is the predicate in front of it — M6-04 rule 3's <em>"a different question
/// with a different failure"</em>, answered.
/// </para>
/// <para>
/// <b>And a class's relationship with the Veil is read here</b> (M6-07c). A
/// <see cref="VeilrotSpec"/> sets where a fresh run opens, multiplies every gain, and — for a class
/// that authors a <see cref="VeilrotSpec.DamagePerPoint"/> — keeps one <c>PercentAdd</c> on the
/// weapon's damage sourced to <see cref="_feeding"/>, rewritten when the meter moves and never on a
/// tick. Null is every class GD §10 describes and no other.
/// </para>
/// <para>
/// <b>It allocates nothing</b> (rule 11). Four floats, two <see langword="bool"/>s, an
/// <see langword="int"/>, three <see langword="object"/> sources built once here, and a
/// <c>Stat.RemoveAll</c>/<c>Add</c> pair at most once a second or once a movement. <see cref="Tick"/>
/// on an unclaimed run is one field write and one comparison.
/// </para>
/// </remarks>
public sealed class Veilrot
{
    /// <summary>The top of the meter, and GD §10.2's last row. A hundred.</summary>
    public const float Max = 100f;

    /// <summary>What one second of the Claiming takes, as a fraction of the maximum it began at.</summary>
    public const float ClaimingDrainPerSecond = 0.01f;

    /// <summary>GD §10.2's first row, where every enemy gets 5 % faster.</summary>
    private const float FirstThreshold = 25f;

    /// <summary>GD §10.2's second row, which this build publishes and does nothing else about.</summary>
    private const float SecondThreshold = 50f;

    /// <summary>GD §10.2's third row, where a fifth of the maximum goes.</summary>
    private const float ThirdThreshold = 75f;

    /// <summary>Which index of <see cref="Threshold"/> carries the 75 row's modifier.</summary>
    private const int MaxHpIndex = 2;

    /// <summary>Which index of <see cref="Threshold"/> closes the latch.</summary>
    private const int ClaimingIndex = 3;

    /// <summary>The <c>PercentMult</c> every enemy spawned at or above 25 wears on its move speed.</summary>
    private const float EnemySpeedAtTwentyFive = 0.05f;

    /// <summary>
    /// The 75 row's <c>PercentMult</c> on the player's maximum, and it is a <c>PercentMult</c>
    /// rather than a <c>PercentAdd</c> on purpose (rule 5).
    /// </summary>
    /// <remarks>
    /// Pooled additively it would cancel against a +20 % maximum-hit-points node and the threshold
    /// would silently do nothing for a run that had taken one. As its own factor it is a true fifth
    /// of whatever the player has built, which is what GD §10.2 means by −20 %. <c>Modifier</c>'s own
    /// remarks name this class of thing: <em>"the rare, loud multipliers — Focus at full ramp, a boss
    /// phase, the Claiming."</em>
    /// </remarks>
    private const float ThirdThresholdMaxHp = -0.20f;

    /// <summary>The Claiming's <em>+100 % damage</em>.</summary>
    private const float ClaimingDamage = 1f;

    /// <summary>The Claiming's <em>+30 % move speed</em>.</summary>
    private const float ClaimingMoveSpeed = 0.30f;

    /// <summary>The Claiming's <em>dash cooldown halved</em>.</summary>
    private const float ClaimingDashCooldown = -0.50f;

    /// <summary>
    /// How many whole seconds of the Claiming take the maximum to nothing. A hundred, which is
    /// GD §10.3's <em>"90 seconds of godhood to push two more stages"</em> bounded.
    /// </summary>
    /// <remarks>
    /// Written as a number rather than derived from <see cref="ClaimingDrainPerSecond"/>, because
    /// <c>1f / 0.01f</c> is 100.000002 and the cast that turned it back into a step count would be
    /// one rounding rule away from 99. The pair is pinned by a row instead.
    /// </remarks>
    private const int SecondsToZero = 100;

    /// <summary>
    /// How far short of a whole second still counts as one.
    /// </summary>
    /// <remarks>
    /// <b>A millisecond, because a run's seconds are a sum of frames and a sum of frames is not
    /// exact.</b> Sixty additions of <c>1f / 60f</c> come to 0.9999997 and three thousand come to
    /// 49.99941 — so a step boundary read as a bare <c>&gt;=</c> would silently skip the drain step
    /// a whole second had earned, and the hundredth second would arrive late or not at all. A
    /// millisecond is a sixteenth of a frame at 60 fps and four orders of magnitude above the dust
    /// that makes it necessary, so it can never advance the drain by a step the run has not lived.
    /// </remarks>
    private const float SecondTolerance = 1e-3f;

    private readonly Stat _maxHp;
    private readonly Stat _weaponDamage;
    private readonly Stat _moveSpeed;
    private readonly Stat _dashCooldown;
    private readonly PlayerCombat _combat;
    private readonly CombatBlackboard _blackboard;
    private readonly IDomainEvents _events;

    /// <summary>The run's Ordeals, for Hunger's multiplier. Null for a meter built without one.</summary>
    private readonly Ordeals _ordeals;

    /// <summary>Who the Claiming's three buffs are applied by — see the class remarks.</summary>
    private readonly object _claiming = new();

    /// <summary>Who the once-a-second drain is applied by — see the class remarks.</summary>
    private readonly object _drain = new();

    /// <summary>
    /// Who the damage that rides the meter is applied by (M6-07c rule 9). A fourth source rather
    /// than <see langword="this"/>, so the modifier can be rewritten without reading which stat the
    /// 75 row's lives on.
    /// </summary>
    private readonly object _feeding = new();

    /// <summary>This class's relationship with the Veil, or null for none (M6-07c).</summary>
    private readonly VeilrotSpec _relationship;

    private float _value;
    private float _claimedFor;
    private bool _isClaimed;

    /// <summary>How many whole seconds of drain have already been written onto the maximum.</summary>
    private int _drainSteps;

    /// <param name="stats">
    /// This run's addresses. The meter puts modifiers on four of them — the maximum, the weapon's
    /// damage, the top speed and the dash's cooldown. The address table rather than the objects, for
    /// the reason every effect handler takes it (AR §10.1): a number's home is a thing this class
    /// should be able to name and not a thing it should have to know.
    /// </param>
    /// <param name="combat">
    /// Who a death is announced through, and nothing else. The Claiming is the first thing in V1
    /// that removes maximum hit points, so it is the first thing that can kill without a
    /// <c>DamageResult</c> — see rule 8 and <c>Health.OnMaxHpChanged</c>, which asked for this in
    /// writing.
    /// </param>
    /// <param name="blackboard">
    /// Where <c>TriggerField.Veilrot</c> is read from — rule 10. Borrowed rather than owned, one
    /// writer and many readers (ADR-0005); <c>ProjectileSystem</c>'s arrangement for
    /// <c>IncomingProjectiles</c> rather than <c>PlayerCombat.UpdateBlackboard</c>'s for the other
    /// eight, and that method's last line has been saying so since M3-06.
    /// </param>
    /// <param name="events">Where the three events of this module go.</param>
    /// <param name="ordeals">
    /// What this run has been dealt, for GD §13.4's Hunger — or <see langword="null"/> for none,
    /// which is a true statement below the mode's first Ordeal stage rather than a mis-wiring
    /// (M6-06b rule 5). Optional and last; <c>RunSession</c> always passes the run's set.
    /// </param>
    /// <param name="relationship">
    /// The class's <see cref="VeilrotSpec"/>, or <see langword="null"/> for a class the Veil treats
    /// ordinarily (M6-07c rule 6). Optional and last; <c>RunSession</c> always passes the class's.
    /// <b>Its start is applied here, silently</b> (rule 3): before anything subscribes, so
    /// <c>RunStarted</c> is what seeds the HUD's meter, and a resumed run's <see cref="Restore"/>
    /// overwrites it rather than adding to it.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Any argument but <paramref name="ordeals"/> and <paramref name="relationship"/> is null.
    /// </exception>
    public Veilrot(
        PlayerStats stats,
        PlayerCombat combat,
        CombatBlackboard blackboard,
        IDomainEvents events,
        Ordeals ordeals = null,
        VeilrotSpec relationship = null)
    {
        if (stats is null)
        {
            throw new ArgumentNullException(nameof(stats));
        }

        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _ordeals = ordeals;

        // Resolved once, because the four addresses cannot change for the life of a run: a Stat is
        // the very instance the player's numbers live in, and RunSession builds both in one breath.
        _maxHp = stats.Resolve(PlayerStat.MaxHp);
        _weaponDamage = stats.Resolve(PlayerStat.WeaponDamage);
        _moveSpeed = stats.Resolve(PlayerStat.MoveSpeed);
        _dashCooldown = stats.Resolve(PlayerStat.MovementSkillCooldown);

        _relationship = relationship;

        // **CH §3.2's "starts at 15", silently** (M6-07c rule 3): M6-04 rule 9's "a resume is not
        // news" at the other end of the same run. Settled through the same two writers a restore
        // uses, so a class authored to open above 25 would open with the 25 row on and nothing said.
        if (relationship is not null && relationship.StartingVeilrot > 0f)
        {
            _value = relationship.StartingVeilrot;

            ApplyStates(0f, publish: false);
            RewriteFeeding();
        }
    }

    /// <summary>How many rows GD §10.2 has. Four.</summary>
    /// <remarks>
    /// <b>Amended at M6-00b: this was drafted as <c>public static readonly float[] Thresholds</c>
    /// and that is static mutable state, which AR §7 bans.</b> <c>readonly</c> protects the handle
    /// and not the four floats, so any caller could write <c>Thresholds[3] = 5f</c> and every later
    /// comparison in the meter would be wrong for the rest of the session. Two members handing out
    /// floats by value cost the one reader — M6-03b's HUD meter — four calls at <c>Start</c>.
    /// </remarks>
    public static int ThresholdCount => 4;

    /// <summary>GD §10.2's four rows, in order: 25, 50, 75, 100.</summary>
    /// <param name="index">Which row, from 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is outside <c>[0, ThresholdCount)</c>.
    /// </exception>
    public static float Threshold(int index)
    {
        return index switch
        {
            0 => FirstThreshold,
            1 => SecondThreshold,
            2 => ThirdThreshold,
            3 => Max,
            _ => throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"GD §10.2 has {ThresholdCount} rows, numbered from 0."),
        };
    }

    /// <summary>The meter, in <c>[0, <see cref="Max"/>]</c>.</summary>
    public float Value => _value;

    /// <summary>
    /// True from the moment <see cref="Value"/> first reaches <see cref="Max"/>. A latch — rule 6.
    /// </summary>
    /// <remarks>
    /// It never goes false again, so a run cleansed from 100 to 40 reads 40 here with the 75 and 25
    /// states off and the Claiming still on. That combination looks like a bug and is not: GD §10.2
    /// says the Claiming lasts <em>"until you die"</em>, and GD §13.3's Cleanse is a way to survive
    /// the drain rather than a way to give the power back.
    /// </remarks>
    public bool IsClaimed => _isClaimed;

    /// <summary>Seconds since the Claiming began, or zero. What the drain is a function of.</summary>
    /// <remarks>
    /// <b>Zero on a resumed run, whatever it was saved at</b> (rule 9). Nothing on disk carries it —
    /// v4 cut the field — so a <c>Continue</c> taken at 100 Veilrot is worth up to a hundred seconds.
    /// It is recorded here rather than discovered, and it is smaller than the free cooldown reset the
    /// same <c>Continue</c> already grants.
    /// </remarks>
    public float ClaimedFor => _claimedFor;

    /// <summary>
    /// The <c>PercentMult</c> every enemy spawned from now on wears on its move speed: 0.05 at or
    /// above 25, otherwise 0 — rule 4.
    /// </summary>
    /// <remarks>
    /// <b>Read at the spawn rather than pushed at the crossing, and a body already standing keeps its
    /// speed.</b> Walking <c>EnemyRegistry.Alive</c> on the crossing was weighed and refused: it needs
    /// the meter to hold the registry, or a second call site in <c>RunSession</c> and a method on
    /// <c>EnemySystem</c>, for five per cent of one wave's move speed. Veilrot is gained at a
    /// level-up, which pauses the run, so the bodies that miss it are the ones already on screen when
    /// the player took the Pact.
    /// </remarks>
    public float EnemySpeedBonus => _value >= FirstThreshold ? EnemySpeedAtTwentyFive : 0f;

    /// <summary>What one cast through a cooldown costs this class, or 0 — <c>SkillRunner</c>'s read.</summary>
    /// <remarks>
    /// Zero for a class with no relationship and for one whose relationship buys nothing, which is
    /// two of the three shipped classes (M6-07c rule 7): the runner's paid branch asks this first and
    /// leaves.
    /// </remarks>
    public float InstantCastCost => _relationship is null ? 0f : _relationship.InstantCastCost;

    /// <summary>
    /// Adds <paramref name="amount"/> to the meter. Pacts and Ordeals.
    /// </summary>
    /// <param name="amount">
    /// Points of Veilrot. Zero, negative and NaN all do nothing and publish nothing — rule 1, which
    /// is <c>EssenceWallet.Earn</c>'s rule and <c>Health.Heal</c>'s before it. A movement of nothing
    /// is not news, and a reader that had to filter zeroes out of <see cref="VeilrotChanged"/> would
    /// be a reader that could forget to.
    /// </param>
    /// <remarks>
    /// <b>A gain that would exceed 100 clamps</b>, and the clamp is what makes the Claiming reachable
    /// by a single +20 Pact from 85 rather than something a run has to hit exactly. Spelled as the
    /// negated positive so NaN is refused with the zeroes rather than admitted as a number; an
    /// infinite one lands on <see cref="Max"/>, which is the same answer any absurdly large gain gets.
    /// </remarks>
    public void Gain(float amount)
    {
        if (!(amount > 0f))
        {
            return;
        }

        // **GD §13.4's Hunger, above the clamp** (M6-06b rule 6): a 15-Rot Pact is 22.5, and a run at
        // 85 still arrives at exactly 100 and Claims. Below the guard, so a gain of nothing is still
        // nothing — the multiplier is finite and above zero (OrdealSpec's door) and cannot make one.
        // Cleanse does not read it: a cleanse is not a gain, and multiplying it would make Hunger a
        // discount at the Sanctum.
        //
        // **The class's multiplier first, then Hunger's, then the clamp** (M6-07c rule 4): a 15-Rot
        // Pact is 9 for an Oathbound, 22.5 for a Gravecaller and 33.75 for one under Hunger. The
        // order of two multiplications is not arithmetic; it is where a reader looks first. Cleanse
        // ignores this one for Hunger's reason — resisting corruption is not resisting the cure.
        if (_relationship is not null)
        {
            amount *= _relationship.GainMultiplier;
        }

        if (_ordeals is not null)
        {
            amount *= _ordeals.VeilrotMultiplier;
        }

        MoveTo(MathF.Min(Max, _value + amount));
    }

    /// <summary>
    /// GD §13.3's Cleanse: takes <paramref name="amount"/> off the meter.
    /// </summary>
    /// <param name="amount">
    /// Points to remove. Zero, negative and NaN all do nothing, for <see cref="Gain"/>'s reason.
    /// </param>
    /// <remarks>
    /// <b>It clamps at zero and does not refuse a partial one</b> (rule 3). Buying a 15-point cleanse
    /// at 8 Rot takes the meter to 0 and wastes 7, which is the player's decision and not the model's
    /// to prevent; what M6-02b refuses is buying one at <em>0</em>, where there is nothing to cleanse
    /// at all. <b>It never un-Claims</b> — see <see cref="IsClaimed"/>.
    /// </remarks>
    public void Cleanse(float amount)
    {
        if (!(amount > 0f))
        {
            return;
        }

        MoveTo(MathF.Max(0f, _value - amount));
    }

    /// <summary>
    /// Whether <paramref name="amount"/> could be spent right now. The predicate <see cref="Spend"/>
    /// is the invariant behind — M6-01a rule 7's pairing.
    /// </summary>
    /// <param name="amount">
    /// Points of Veilrot. False for zero, a negative, NaN or an infinity, none of which is a price.
    /// </param>
    public bool CanSpend(float amount) => IsPrice(amount) && _value >= amount;

    /// <summary>
    /// Takes <paramref name="amount"/> off the meter as a price rather than as a cleanse (M6-07c
    /// rule 5).
    /// </summary>
    /// <remarks>
    /// <b>It crosses thresholds downward the way <see cref="Cleanse"/> does</b>, so a spend from 26 to
    /// 21 loses the 25 row's enemy speed. That is the Emberwright paying twice for one cast and it is
    /// correct: the meter is the meter. <b>It never un-Claims</b> — see <see cref="IsClaimed"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="amount"/> is not a finite number above zero.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The meter holds less than <paramref name="amount"/>. Nothing has moved when it throws.
    /// </exception>
    public void Spend(float amount)
    {
        if (!IsPrice(amount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "A spend must be a finite number of points above zero.");
        }

        if (!CanSpend(amount))
        {
            throw new InvalidOperationException(
                $"The meter holds {_value} and {amount} was spent. CanSpend answers this before "
                    + "anything is bought (M6-07c rule 5), so reaching here is a cast that happened "
                    + "and was not paid for.");
        }

        MoveTo(_value - amount);
    }

    /// <summary>
    /// Writes the meter onto the blackboard, and advances the Claiming's drain. Nothing else here
    /// moves <see cref="Value"/>: GD §10.1 says the meter <em>"never decays"</em>.
    /// </summary>
    /// <param name="dt">Seconds since the previous tick — the snapshot's <c>Dt</c>.</param>
    /// <param name="now">
    /// Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock. Here because a
    /// drain step that kills has to announce a death, and <c>PlayerDied</c> carries the moment it
    /// happened; <c>Health.Tick</c> takes the same pair for the same reason.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="dt"/> is negative, NaN or infinite. A negative step would rewind the drain and
    /// hand the maximum back; a non-finite one would put NaN into <see cref="ClaimedFor"/> and stop
    /// every later comparison against it from ever being true, with nothing logged.
    /// </exception>
    /// <remarks>
    /// <b>The drain is one modifier rewritten once per whole second, not per frame</b> (rule 7).
    /// Per-frame rewriting would raise <c>Stat.Changed</c> sixty times a second on the one stat the
    /// HUD is subscribed to, for a step GD §10.2 states in seconds.
    /// </remarks>
    public void Tick(float dt, float now)
    {
        // `!(dt >= 0f)` rather than `dt < 0f`, for the reason every guard in core is spelled that
        // way: every comparison against NaN is false, so the natural spelling admits one.
        if (!(dt >= 0f) || float.IsInfinity(dt))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dt),
                dt,
                "dt must be a finite number of seconds, zero or more.");
        }

        // **Rule 10, and the end of a five-milestone deliberate absence.** ProjectileSystem's
        // arrangement for IncomingProjectiles rather than PlayerCombat.UpdateBlackboard's for the
        // other eight: the system that owns the number writes it, and this tick sits above the
        // combat step so the field is this tick's before SkillRunner.Tick reads a trigger over it
        // (AR §18.1).
        _blackboard.Veilrot = _value;

        if (!_isClaimed)
        {
            return;
        }

        _claimedFor += dt;

        // Truncation rather than a rounding, because a step is earned by having lived the second
        // rather than by being nearer to its end than its start. The tolerance is what stops a sum
        // of frames landing three ten-millionths short of one and losing the step — see its remarks.
        int seconds = (int)(_claimedFor + SecondTolerance);

        if (seconds > SecondsToZero)
        {
            seconds = SecondsToZero;
        }

        if (seconds <= _drainSteps)
        {
            return;
        }

        _drainSteps = seconds;

        // **Clamped at the step count rather than with MathF.Min on the product**, because 0.01f is
        // not one hundredth exactly: a hundred of them multiply out to 0.99999998, which leaves a
        // maximum of three millionths of a hit point and a player who is not quite dead. The floor
        // rule 7 states is −1, and this is the spelling that actually reaches it.
        float drained = seconds >= SecondsToZero ? 1f : ClaimingDrainPerSecond * seconds;

        // Removed and re-added rather than edited: a Stat's stack is a list of readonly structs, and
        // rewriting one in place is not a thing the type offers or should. The removal raises
        // Changed as the maximum springs back for an instant, which is why this runs once a second
        // and not once a frame.
        _maxHp.RemoveAll(_drain);
        _maxHp.Add(new Modifier(ModifierKind.PercentMult, -drained, _drain));

        // **Rule 8, and Health.OnMaxHpChanged asked for it in writing**: a maximum driven to zero
        // pulls Current down with it and no DamageResult exists to carry Killed, so the one publisher
        // of PlayerDied has to be reachable by a death that arrives without damage. Called after every
        // step rather than only after the last, because a run whose maximum was already low dies
        // before the hundredth second — and it is silent for the living, so there is no branch here
        // for a caller to get wrong.
        _combat.AnnounceDeath(now);
    }

    /// <summary>
    /// What a resumed run comes back at: the value, every state it implies, and the latch. No
    /// events, no crossings — rule 9.
    /// </summary>
    /// <param name="value">
    /// The saved meter. Unguarded, deliberately, like <c>EssenceWallet.Restore</c> and for its
    /// reason (AR §18.2): it is <c>internal</c>, the one caller is a line of core, and
    /// <c>RunSnapshot</c>'s constructor has already refused anything outside <c>[0, 100]</c> at the
    /// boundary it arrived through — which is the real door, because it came out of a file. A second
    /// copy of that guard here is the first place the two could disagree.
    /// </param>
    /// <remarks>
    /// <b>Silent, which is <c>EssenceWallet.Restore</c>'s rule</b> (M6-01a rule 8): a resume is not
    /// news, and a <see cref="ClaimingBegan"/> published inside <c>RunSession.Start</c> would reach a
    /// HUD that has not subscribed yet. A run restored at exactly 100 comes back Claimed with
    /// <see cref="ClaimedFor"/> at zero — see that property.
    /// </remarks>
    internal void Restore(float value)
    {
        float before = _value;

        // **Overwrites a class's start rather than adding to it** (M6-07c rule 3): a Gravecaller
        // saved at 40 comes back at 40, not 55, because `before` is the 15 the constructor opened at
        // and the states are settled from there.
        _value = value;

        ApplyStates(before, publish: false);
        RewriteFeeding();
    }

    /// <summary>
    /// Moves the meter to <paramref name="next"/> and settles everything that follows from it.
    /// </summary>
    /// <remarks>
    /// The one writer of <see cref="_value"/> outside <see cref="Restore"/>, so the four states and
    /// the event cannot come apart from the number. A movement of nothing leaves before it says
    /// anything, which is what makes <see cref="VeilrotChanged.Delta"/> never zero and what makes
    /// <c>Gain(20)</c> at 100 do nothing at all.
    /// </remarks>
    private void MoveTo(float next)
    {
        float before = _value;
        float delta = next - before;

        if (delta == 0f)
        {
            return;
        }

        _value = next;

        // Before the event, so a listener reading the weapon's damage from inside VeilrotChanged
        // reads the number the meter now implies (M6-07c rule 9).
        RewriteFeeding();

        _events.Publish(new VeilrotChanged(_value, delta));

        ApplyStates(before, publish: true);
    }

    /// <summary>
    /// CH §3.2's <em>"+1 % damage per Veilrot point"</em>: one <c>PercentAdd</c> on the weapon's
    /// damage, sourced to <see cref="_feeding"/>, rewritten to match <see cref="_value"/> (M6-07c
    /// rule 9).
    /// </summary>
    /// <remarks>
    /// <b>A <c>PercentAdd</c>, pooled</b> (ADR-0008), so 60 Rot is ×1.60 and not 1.01⁶⁰ — M6-07a's
    /// arrangement for Kindling. <b>Called when the meter moves and never on a tick</b>: a Pact, a
    /// Cleanse, a Spend or a restore, a handful of times a stage, against sixty a second on the stat
    /// the HUD is subscribed to. A class with no dial leaves before touching the stat, so it raises
    /// nothing and puts nothing on it.
    /// </remarks>
    private void RewriteFeeding()
    {
        if (_relationship is null || !(_relationship.DamagePerPoint > 0f))
        {
            return;
        }

        _weaponDamage.RemoveAll(_feeding);

        if (_value > 0f)
        {
            _weaponDamage.Add(new Modifier(
                ModifierKind.PercentAdd,
                _relationship.DamagePerPoint * _value,
                _feeding));
        }
    }

    /// <summary>Whether <paramref name="amount"/> is something a price could be.</summary>
    private static bool IsPrice(float amount) => amount > 0f && !float.IsInfinity(amount);

    /// <summary>
    /// Brings every one of GD §10.2's four rows into line with <see cref="_value"/>, given where the
    /// meter stood before.
    /// </summary>
    /// <remarks>
    /// Walked in ascending order, and that is load-bearing in exactly one place: the 75 row's −20 %
    /// has to be on the maximum before the Claiming reads it, because <see cref="ClaimingBegan"/>
    /// carries the number the drain is a percentage of.
    /// </remarks>
    private void ApplyStates(float before, bool publish)
    {
        for (int i = 0; i < ThresholdCount; i++)
        {
            float threshold = Threshold(i);
            bool was = before >= threshold;
            bool entered = _value >= threshold;

            if (was == entered)
            {
                continue;
            }

            bool claimed = false;

            switch (i)
            {
                case MaxHpIndex:
                    if (entered)
                    {
                        _maxHp.Add(new Modifier(
                            ModifierKind.PercentMult,
                            ThirdThresholdMaxHp,
                            this));
                    }
                    else
                    {
                        // Sourced to the meter, so this takes the threshold's modifier off and
                        // nothing else: the Claiming's buffs and the drain are two other sources,
                        // and the drain is on this very stat (ADR-0008).
                        _maxHp.RemoveAll(this);
                    }

                    break;

                case ClaimingIndex:
                    // **Entered only, and only once a run** (rule 6). Falling out of the row
                    // publishes the crossing below and changes nothing: the meter left 100, the
                    // latch did not open. Rising back into it after a Cleanse is the case the flag
                    // is really guarding — without it, a second arrival at 100 would add three more
                    // modifiers on top of the three already there.
                    if (entered && !_isClaimed)
                    {
                        BeginClaiming();

                        claimed = true;
                    }

                    break;

                default:
                    // The 25 row is read at a spawn rather than applied here (EnemySpeedBonus), and
                    // the 50 row is GD §19's Revenant, which this build does not have: it ships
                    // crossed, published and otherwise silent rather than substituted, because a
                    // Husk that follows you every stage is a free kill at 50 Veilrot and would make
                    // the threshold a reward.
                    break;
            }

            if (!publish)
            {
                continue;
            }

            // **After the modifier and before ClaimingBegan**, which is two orderings in one line.
            // After, so a listener reading the maximum from inside the 75 row's crossing sees the
            // fifth already gone rather than the number it is about to stop being; before, because
            // rule 6 says the Claiming is announced after the crossing that caused it.
            _events.Publish(new VeilrotThresholdCrossed(threshold, entered));

            if (claimed)
            {
                // The maximum **as it stands after the other three have gone on**, which for a meter
                // at 100 means with the 75 row's −20 % already applied. Read rather than remembered,
                // so the number a bar draws its countdown against is the one the drain will shrink.
                _events.Publish(new ClaimingBegan(_maxHp.Value));
            }
        }
    }

    /// <summary>GD §10.2's last row: three modifiers, a latch, and a clock that only ends one way.</summary>
    private void BeginClaiming()
    {
        _isClaimed = true;
        _claimedFor = 0f;
        _drainSteps = 0;

        _weaponDamage.Add(new Modifier(ModifierKind.PercentMult, ClaimingDamage, _claiming));
        _moveSpeed.Add(new Modifier(ModifierKind.PercentMult, ClaimingMoveSpeed, _claiming));
        _dashCooldown.Add(new Modifier(ModifierKind.PercentMult, ClaimingDashCooldown, _claiming));
    }
}
