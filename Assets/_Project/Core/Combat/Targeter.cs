using System;
using Soulvail.Core.Content;

namespace Soulvail.Core.Combat;

/// <summary>
/// The stateful half of CC §3.1's targeting loop: <em>when</em> to decide, and what the player's
/// tap overrides. <see cref="TargetScorer"/> answers "which enemy scores highest"; this answers
/// "which enemy is the gun facing right now", which is a different question because it involves
/// time. See CC §3.1 and §3.3–3.6.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two clocks, neither of them real.</b> Everything here is driven by the <c>dt</c> the caller
/// hands to <see cref="Tick"/> — the snapshot's, clamped at 50 ms — because core never reads a
/// clock (AR §4.4). The cadence accumulator and the focus out-of-range timer are the only state
/// that time touches, and both are seconds of simulated run time, so a 30 fps phone and a 120 fps
/// one make the same decisions at the same moments.
/// </para>
/// <para>
/// <b>It publishes nothing and gathers nothing.</b> M1-08 builds the candidate span from the
/// enemy registry each frame and turns <see cref="ChangedThisTick"/> into a <c>TargetChanged</c>
/// event; M1-09 resolves a thumb-tap to the id passed to <see cref="Focus"/>. This class sits
/// between them holding the rules and no references.
/// </para>
/// <para>
/// <b>Blocked is a state, not a failure.</b> <see cref="IsCurrentBlocked"/> is CC §3.6's "go
/// around": the character holds facing on something it cannot hurt, and the reticle says so. It
/// is deliberately a separate flag rather than a null target, because the view needs to know
/// which of two questions it is being told the answer to — "shoot this" or "look at this".
/// </para>
/// </remarks>
public sealed class Targeter
{
    /// <summary>
    /// Seconds a focused enemy may stay outside <see cref="TargetingSpec.AcquireRange"/> before
    /// the focus is dropped — CC §3.4's "leaves acquireRange for &gt;2 s", and CC §7's focus drop
    /// delay.
    /// </summary>
    /// <remarks>
    /// A constant rather than a field on <see cref="TargetingSpec"/>, which deliberately carries
    /// only the scoring block: this number and the 3 m tap radius belong to the focus override,
    /// and the override's other half lands in M1-09. If either ever needs to differ per class,
    /// they move into authored data together — one spec change rather than two.
    /// </remarks>
    private const float FocusDropDelay = 2f;

    private readonly TargetScorer _scorer;
    private readonly TargetingSpec _spec;

    /// <summary>
    /// Seconds accumulated towards the next scheduled decision. Never reset by an immediate
    /// re-selection — see <see cref="Tick"/>.
    /// </summary>
    private float _sinceLastSelection;

    /// <summary>
    /// How long the focused enemy has been continuously out of range. Cleared by any tick that
    /// finds it back inside, which is what makes rule 3's window <em>continuous</em> rather than
    /// cumulative.
    /// </summary>
    private float _focusOutOfRangeFor;

    /// <summary>
    /// What <see cref="Focus"/> or <see cref="ClearFocus"/> asked for, pending the next
    /// <see cref="Tick"/>.
    /// </summary>
    private int _pendingFocusId = -1;

    private bool _focusPending;

    /// <param name="scorer">The scoring half. Shares this character's <paramref name="spec"/>.</param>
    /// <param name="spec">
    /// The class's targeting numbers. Read here for <see cref="TargetingSpec.Cadence"/> and
    /// <see cref="TargetingSpec.AcquireRange"/> — the range twice over, since the scorer filters
    /// on it too, and the focus override has to apply the same boundary to bypass it correctly.
    /// </param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public Targeter(TargetScorer scorer, TargetingSpec spec)
    {
        _scorer = scorer ?? throw new ArgumentNullException(nameof(scorer));
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
    }

    /// <summary>The enemy the character is facing, or −1 when there is nothing to face.</summary>
    public int CurrentTargetId { get; private set; } = -1;

    /// <summary>
    /// <see cref="CurrentTargetId"/> cannot be damaged right now: either every candidate is
    /// blocked and this is the nearest of them, or it is a focused enemy the player is holding on
    /// through a phase of invulnerability. Either way the weapon should not claim it is hitting
    /// anything.
    /// </summary>
    public bool IsCurrentBlocked { get; private set; }

    /// <summary>
    /// The player's forced target, or −1. Reflects what the last <see cref="Tick"/> applied, not
    /// what <see cref="Focus"/> most recently asked for — commands land on the tick, per rule 5.
    /// </summary>
    public int FocusedTargetId { get; private set; } = -1;

    /// <summary>Whether a focus is currently held.</summary>
    public bool HasFocus => FocusedTargetId >= 0;

    /// <summary>
    /// Any of <see cref="CurrentTargetId"/>, <see cref="IsCurrentBlocked"/> or
    /// <see cref="FocusedTargetId"/> differs from what it was when the last <see cref="Tick"/>
    /// began. The three are compared as one triple across the whole tick rather than per branch,
    /// so a target that is dropped and re-acquired within a single tick correctly reports no
    /// change, and a focus that lands without moving the current target still does.
    /// </summary>
    public bool ChangedThisTick { get; private set; }

    /// <summary>
    /// Forces <paramref name="enemyId"/> as the target from the next <see cref="Tick"/>, ignoring
    /// scoring entirely (CC §3.4).
    /// </summary>
    /// <remarks>
    /// Deferred rather than immediate because every other decision in this class is made inside
    /// a tick, against a candidate span it can actually see. A tap arrives between ticks, when
    /// this object has no idea whether that enemy exists, is in range or is alive — all three of
    /// which rule 3 has an opinion about. A negative id is the same request as
    /// <see cref="ClearFocus"/>, since −1 is what "no focus" is spelled as everywhere else here.
    /// </remarks>
    public void Focus(int enemyId)
    {
        _pendingFocusId = enemyId < 0 ? -1 : enemyId;
        _focusPending = true;
    }

    /// <summary>Drops the player's focus from the next <see cref="Tick"/>, returning to scoring.</summary>
    public void ClearFocus() => Focus(-1);

    /// <summary>
    /// Advances the cadence, applies pending focus commands, and re-selects when the schedule or
    /// the state of the current target says to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An immediate re-selection never touches the accumulator.</b> A scheduled decision is
    /// due every <see cref="TargetingSpec.Cadence"/> seconds and stays due however many times the
    /// target dies in between — resetting the accumulator on each immediate retarget would let a
    /// stream of dying targets starve the scheduled loop entirely, so the character would keep
    /// answering "what died" and never "what is best". The two triggers are independent by
    /// construction: one is a clock, the other is a fact about the world.
    /// </para>
    /// <para>
    /// A step longer than the cadence still runs exactly one decision — one tick, one decision —
    /// and the surplus is discarded rather than banked (the remainder, so the phase survives),
    /// because a hitch must not be followed by a burst of catch-up selections. With the
    /// snapshot's 50 ms <c>Dt</c> clamp against a 100 ms cadence this cannot arise in a run at
    /// all; it is here so that a caller stepping the targeter coarsely in a test or a replay gets
    /// the sane answer rather than an accumulator that grows for ever.
    /// </para>
    /// </remarks>
    /// <param name="dt">Seconds since the previous tick — the snapshot's <c>Dt</c>.</param>
    /// <param name="candidates">
    /// Every enemy worth considering this frame, gathered by the caller (CC §3.1 steps 1–2).
    /// Borrowed for the duration of the call and never retained.
    /// </param>
    /// <param name="dpsOneSecond">One second of expected damage, for the scorer's finisher bonus.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="dt"/> is negative, NaN or infinite. A non-finite step would poison both
    /// timers at once — the cadence would either fire every tick or never again, and the focus
    /// drop timer would stop comparing — with nothing logged, which is how every other
    /// non-finite input in this project is treated.
    /// </exception>
    public void Tick(float dt, ReadOnlySpan<TargetCandidate> candidates, float dpsOneSecond)
    {
        // `!(dt >= 0f)` rather than `dt < 0f`, for the reason MovementSpec documents: every
        // comparison against NaN is false, so the natural spelling admits NaN.
        if (!(dt >= 0f) || float.IsInfinity(dt))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dt),
                dt,
                "dt must be a finite number of seconds, zero or more.");
        }

        int previousCurrent = CurrentTargetId;
        bool previousBlocked = IsCurrentBlocked;
        int previousFocus = FocusedTargetId;

        ApplyPendingFocus();
        UpdateFocus(candidates, dt);

        _sinceLastSelection += dt;

        bool scheduled = _sinceLastSelection >= _spec.Cadence;

        if (scheduled)
        {
            _sinceLastSelection %= _spec.Cadence;
        }

        // The focus comparison covers both directions — a focus that landed and a focus that was
        // just dropped — because either one changes what the answer should be, and waiting up to
        // a cadence to act on the player's own tap is exactly the latency CC §3.4 is about.
        if (scheduled || FocusedTargetId != previousFocus || NeedsImmediateSelection(candidates))
        {
            Select(candidates, dpsOneSecond);
        }

        ChangedThisTick =
            CurrentTargetId != previousCurrent
            || IsCurrentBlocked != previousBlocked
            || FocusedTargetId != previousFocus;
    }

    /// <summary>
    /// Back to nothing: no target, no focus, not blocked, both timers at zero.
    /// </summary>
    /// <remarks>
    /// The accumulator is included deliberately, so a reset targeter decides on its first tick
    /// rather than inheriting a phase from the run that ended. What a new stage, a respawn or a
    /// pooled agent (M1-19) gets instead of a rebuilt <see cref="Targeter"/>.
    /// </remarks>
    public void Reset()
    {
        CurrentTargetId = -1;
        IsCurrentBlocked = false;
        FocusedTargetId = -1;
        ChangedThisTick = false;
        _sinceLastSelection = 0f;
        _focusOutOfRangeFor = 0f;
        _pendingFocusId = -1;
        _focusPending = false;
    }

    /// <summary>Rule 5: a queued <see cref="Focus"/> or <see cref="ClearFocus"/> lands here.</summary>
    private void ApplyPendingFocus()
    {
        if (!_focusPending)
        {
            return;
        }

        FocusedTargetId = _pendingFocusId;
        _focusPending = false;

        // A fresh focus starts with a clean out-of-range window, or a player who focuses an enemy
        // that has been standing outside the range would lose it on the next tick.
        _focusOutOfRangeFor = 0f;
    }

    /// <summary>
    /// Rule 3: focus survives invulnerability, dies with the enemy, and expires after
    /// <see cref="FocusDropDelay"/> seconds continuously out of range.
    /// </summary>
    /// <remarks>
    /// Being temporarily undamageable is precisely the case the override exists for — the player
    /// picked that Warden on purpose and is walking around it — so a phase of invulnerability
    /// must not silently hand target selection back to the computer. Distance is the one thing
    /// that does expire the focus, and only after a window long enough that a chase or a Charge
    /// through the pack does not lose it.
    /// </remarks>
    private void UpdateFocus(ReadOnlySpan<TargetCandidate> candidates, float dt)
    {
        if (FocusedTargetId < 0)
        {
            return;
        }

        int index = IndexOf(candidates, FocusedTargetId);

        // Absent means dead or despawned — the caller gathers the living (CC §3.1 step 2), so an
        // id that is no longer in the span has nothing left to hold on to. `!(Hp > 0f)` is the
        // same fact arriving by the other route, and is checked for consistency with the
        // current-target validity rule rather than because gathering is expected to emit corpses.
        if (index < 0 || !(candidates[index].Hp > 0f))
        {
            FocusedTargetId = -1;
            _focusOutOfRangeFor = 0f;
            return;
        }

        // `!(distance <= range)` rather than `>`, so a NaN distance counts as out of range and
        // eventually drops the focus, instead of holding it for ever on an enemy whose position
        // has gone bad.
        if (!(candidates[index].Distance <= _spec.AcquireRange))
        {
            _focusOutOfRangeFor += dt;

            // Strictly greater, because CC §3.4 says "for >2 s": at exactly the delay the focus
            // still holds, and the tick that carries it past drops it.
            if (_focusOutOfRangeFor > FocusDropDelay)
            {
                FocusedTargetId = -1;
                _focusOutOfRangeFor = 0f;
            }

            return;
        }

        _focusOutOfRangeFor = 0f;
    }

    /// <summary>
    /// Rule 1's other trigger: the current target is no longer something worth holding, so the
    /// decision cannot wait for the schedule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CC §3.3 lists the three: it died, it left range, it became undamageable. Having no target
    /// at all counts as a fourth, and is the cheapest of the lot — with rule 4 in force,
    /// <see cref="CurrentTargetId"/> is −1 only when the last tick saw an empty span, so the scan
    /// this triggers is over the span that just became non-empty. It buys instant acquisition
    /// when the first enemy of a wave walks in, instead of up to a cadence of standing there.
    /// </para>
    /// <para>
    /// <b>Invulnerability does not re-trigger while already blocked.</b> Once
    /// <see cref="IsCurrentBlocked"/> is up, the target being undamageable is the state we are
    /// deliberately in — not news — and treating it as an invalidation would run a full selection
    /// every frame for as long as a Warden faces the player, which is the per-frame cost the 10 Hz
    /// cadence exists to avoid. The scheduled decision still lands within a cadence, so a blocked
    /// target that opens up is picked up in under 100 ms, and a blocked target that dies or
    /// leaves is still caught immediately by the two tests above it.
    /// </para>
    /// </remarks>
    private bool NeedsImmediateSelection(ReadOnlySpan<TargetCandidate> candidates)
    {
        if (CurrentTargetId < 0)
        {
            return true;
        }

        int index = IndexOf(candidates, CurrentTargetId);

        if (index < 0)
        {
            return true;
        }

        ref readonly TargetCandidate current = ref candidates[index];

        // All three spelled as negated positives, so a NaN distance or hp reads as invalid and
        // forces a fresh choice rather than pinning the character to a target it can never
        // resolve.
        return !(current.Hp > 0f)
            || !(current.Distance <= _spec.AcquireRange)
            || (!current.IsVulnerable && !IsCurrentBlocked);
    }

    /// <summary>Rules 2 and 4: the focus override first, then scoring, then the blocked state.</summary>
    /// <remarks>
    /// The focused branch does not fall through to the nearest-target rule when the focused enemy
    /// is invulnerable. The M1-04 spec's rule 3 says selection "falls through to rule 4" and then
    /// states the opposite in the same sentence — <c>Current = focused</c>, blocked — and the
    /// test row settles it: holding the ring on the enemy the player chose is the whole point of
    /// the override, and swinging the facing to whatever happens to be nearest would be the game
    /// overruling a decision it just promised was theirs.
    /// </remarks>
    private void Select(ReadOnlySpan<TargetCandidate> candidates, float dpsOneSecond)
    {
        if (FocusedTargetId >= 0)
        {
            // Present and alive by construction — UpdateFocus ran first this tick and drops a
            // focus that is neither — so the only question left is range. Out of range, the
            // override stands down and scoring takes over while the drop timer runs; the player
            // keeps the focus, the gun does not stand idle.
            int index = IndexOf(candidates, FocusedTargetId);

            if (index >= 0 && candidates[index].Distance <= _spec.AcquireRange)
            {
                CurrentTargetId = FocusedTargetId;
                IsCurrentBlocked = !candidates[index].IsVulnerable;
                return;
            }
        }

        // The incumbent is named, so it carries its hysteresis (CC §3.3). An invalid one cannot
        // benefit — the scorer filters dead, distant and undamageable candidates before scoring
        // them — so "skip hysteresis on an immediate retarget" is true here by construction
        // rather than by a branch.
        int best = _scorer.SelectBest(candidates, CurrentTargetId, dpsOneSecond);

        if (best >= 0)
        {
            CurrentTargetId = best;
            IsCurrentBlocked = false;
            return;
        }

        // CC §3.6: something is there but none of it can be hurt. Hold facing on the nearest and
        // keep the glyph up.
        if (candidates.Length > 0)
        {
            CurrentTargetId = _scorer.SelectNearest(candidates);
            IsCurrentBlocked = true;
            return;
        }

        CurrentTargetId = -1;
        IsCurrentBlocked = false;
    }

    /// <summary>
    /// Position of <paramref name="id"/> in the span, or −1. A linear scan because the span is at
    /// most the 28 enemies of GD §11 and a dictionary would allocate, which rule 8 forbids.
    /// </summary>
    private static int IndexOf(ReadOnlySpan<TargetCandidate> candidates, int id)
    {
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
