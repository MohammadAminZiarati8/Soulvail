using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Soulvail.Core.Content;

namespace Soulvail.Core.Ai;

/// <summary>
/// The whole of GD §9.1 rule 3, with no agent in it: which phase a boss is in given its health,
/// when it crosses into the next, and how long the invulnerable beat after a crossing has left to
/// run. See M4-01b rules 3, 4 and 6.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure, and that is the point of it.</b> It takes a health fraction and a delta and answers a
/// phase and a beat — no agent, no registry, no events, no clock of its own. So the rule the fight
/// hangs on can be driven a thousand ticks in a row from a fixture with no world in it, which is
/// how <c>LevelTracker</c> (M3-01a) and <c>LevelUpFlow</c> (M3-08a) were built and why both have
/// exhaustive suites. <see cref="BossBehaviour"/> is the thin part that reads a <c>Health</c>,
/// calls this, and publishes.
/// </para>
/// <para>
/// <b>Crossing is one-way and latched, which is hysteresis by construction rather than by a
/// tolerance</b> (rule 3). Healing a boss back over 66 % does not put it back in phase 1: a
/// crossing opens a beat, and a beat clears the arena, so a boss oscillating across a threshold
/// would empty the room every few seconds. <see cref="Current"/> therefore only ever rises.
/// </para>
/// <para>
/// <b>It is driven from a <c>Health</c>, never from an <c>EnemyBlackboard</c></b>, and that is a
/// ruling rather than a preference (M4-01a's finding). <c>EnemySystem.Perceive</c> skips agents
/// that are not alive, so a dead enemy's <c>EnemyBlackboard.HpFraction</c> is frozen at the last
/// reading it was perceived with — a corpse reads as healthy. Anything that decides a fight is over
/// by looking at a fraction therefore decides it is still running for ever, which is why
/// <c>SpawnDirector</c> asks <c>IsAlive</c> and why this is handed <c>Health.Fraction</c>, which
/// answers zero rather than dividing.
/// </para>
/// <para>
/// <b>It allocates nothing.</b> The thresholds are copied out of the spec into a
/// <see langword="float"/> array once, at construction, and a tick is a backwards walk over it plus
/// two floats. This runs on a per-frame path for the whole of a boss fight.
/// </para>
/// </remarks>
public sealed class BossPhases
{
    /// <summary>
    /// The phase thresholds, copied out of the spec so a tick indexes an array rather than an
    /// interface.
    /// </summary>
    /// <remarks>
    /// Copied once rather than read through <see cref="BossSpec.Phases"/> every tick: the spec
    /// hands out a <c>ReadOnlyCollection</c>, so each read is an interface call, and this is the
    /// one thing here that runs sixty times a second. The spec has already refused an empty list
    /// and put the thresholds in strictly descending order, so the walk below can trust both.
    /// </remarks>
    private readonly float[] _entersBelow;

    /// <summary>
    /// <see cref="_entersBelow"/> as something a listener can read and nobody can write. Wrapped
    /// once here rather than per publish, which is what keeps <c>BossPhaseChanged</c> free.
    /// </summary>
    private readonly ReadOnlyCollection<float> _entersBelowView;

    private float _beatRemaining;

    /// <param name="spec">The boss whose phases these are.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    public BossPhases(BossSpec spec)
    {
        Spec = spec ?? throw new ArgumentNullException(nameof(spec));

        _entersBelow = new float[spec.Phases.Count];

        for (int i = 0; i < _entersBelow.Length; i++)
        {
            _entersBelow[i] = spec.Phases[i].EntersBelow;
        }

        _entersBelowView = Array.AsReadOnly(_entersBelow);
    }

    /// <summary>The boss these phases belong to.</summary>
    public BossSpec Spec { get; }

    /// <summary>
    /// Which phase the boss is in, numbered from 0. Never decreases within a fight.
    /// </summary>
    public int Current { get; private set; }

    /// <summary>
    /// How many phases the fight has. What <c>BossPhaseChanged</c> carries as its
    /// <c>ofPhases</c>, so a segmented bar (M4-04) learns its segment count from an event rather
    /// than from a flag on a spawn (rule 7).
    /// </summary>
    public int PhaseCount => _entersBelow.Length;

    /// <summary>
    /// Every phase's threshold, outermost first — the first is 1 and each one after it is strictly
    /// lower. What <c>BossPhaseChanged</c> carries as its <c>entersBelow</c>, so M4-04's bar puts its
    /// marks where the asset put its phases rather than at even spacing (rule 4).
    /// </summary>
    /// <remarks>
    /// The same view every time, so publishing it allocates nothing and a fight that crosses two
    /// thresholds hands out one object rather than two. Read-only rather than the array: these are
    /// the asset's numbers and a listener that could write them would be editing the fight.
    /// </remarks>
    public IReadOnlyList<float> EntersBelow => _entersBelowView;

    /// <summary>The phase the boss is currently in.</summary>
    public BossPhaseSpec CurrentPhase => Spec.Phases[Current];

    /// <summary>
    /// The boss is inside a phase change's invulnerable beat: it takes no damage and does not act.
    /// </summary>
    public bool IsInBeat => _beatRemaining > 0f;

    /// <summary>Seconds of the current beat still to run; zero when there is no beat.</summary>
    /// <remarks>
    /// Exposed so a behaviour can tell "the beat just ended this tick" from "there was never one",
    /// without keeping a second copy of the same countdown that could drift from this one.
    /// </remarks>
    public float BeatRemaining => _beatRemaining > 0f ? _beatRemaining : 0f;

    /// <summary>
    /// One tick of the fight: run the beat down if one is open, otherwise see whether
    /// <paramref name="hpFraction"/> has crossed into a new phase.
    /// </summary>
    /// <param name="hpFraction">
    /// The boss's current HP over its live maximum, in <c>[0, 1]</c> — <c>Health.Fraction</c>, and
    /// never an <c>EnemyBlackboard</c>'s copy of it (see the class remarks). A NaN crosses nothing:
    /// every comparison against it is false, so a broken reading leaves the fight in the phase it
    /// was in rather than skipping it to the end.
    /// </param>
    /// <param name="dt">Seconds since the last tick, from the snapshot.</param>
    /// <returns>
    /// <see langword="true"/> on the tick a new phase begins, and on no other — the caller clears
    /// adds, summons, telegraphs and makes the boss invulnerable. A crossing that passes two
    /// thresholds at once reports <em>once</em> and lands in the last phase it passed (rule 3):
    /// two beats back to back for one hit would be three seconds of an untouchable boss.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>A beat suppresses the crossing check, and it costs nothing.</b> The boss is invulnerable
    /// for the whole of one, so its health cannot fall during it and there is no crossing to miss;
    /// what the suppression actually buys is that a heal or a max-HP change landing mid-beat cannot
    /// open a second beat inside the first.
    /// </para>
    /// <para>
    /// <b>The beat is not run down on the tick it opens.</b> That tick <em>is</em> the beat's first,
    /// which is where GD §9.1 rule 3's add-clear happens (rule 4) — so an authored 1.5 s beat
    /// ticked at 0.1 is open for fifteen ticks and shut on the sixteenth.
    /// </para>
    /// </remarks>
    public bool Tick(float hpFraction, float dt)
    {
        if (_beatRemaining > 0f)
        {
            _beatRemaining -= dt;

            return false;
        }

        int target = TargetPhase(hpFraction);

        if (target <= Current)
        {
            return false;
        }

        Current = target;

        _beatRemaining = Spec.BeatSeconds;

        return true;
    }

    /// <summary>
    /// Back to the opening phase with no beat running. What a recycled agent's behaviour gets
    /// instead of a new phase machine (<c>EnemyAgent.Initialise</c>'s bargain).
    /// </summary>
    public void Reset()
    {
        Current = 0;
        _beatRemaining = 0f;
    }

    /// <summary>
    /// The deepest phase <paramref name="hpFraction"/> qualifies for — the last one whose
    /// threshold it is at or under.
    /// </summary>
    /// <remarks>
    /// Walked backwards and answered on the first match, so one hit that crosses two thresholds
    /// lands in the deeper of them (rule 3). The first phase's threshold is 1 and a health
    /// fraction can never exceed it, so the walk always finds something for any real reading — and
    /// falls through to phase 0 for a NaN, which is the direction that leaves the fight where it
    /// was.
    /// </remarks>
    private int TargetPhase(float hpFraction)
    {
        for (int i = _entersBelow.Length - 1; i > 0; i--)
        {
            if (hpFraction <= _entersBelow[i])
            {
                return i;
            }
        }

        return 0;
    }
}
