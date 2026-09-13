using System;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Save;

/// <summary>
/// Turns the live run into a <see cref="RunSnapshot"/> at the two moments GD §7.3 names — the
/// opening of a run, and every stage boundary — and announces it. It writes nothing anywhere: the
/// medium is <c>ISaveStore</c>'s and the listener that reaches for it is on the Unity side.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is here, in <c>Core/Save</c>, because it holds the clock.</b> A snapshot is stamped with
/// <see cref="IClock.UtcNow"/>, and AR §18.2's rule — no clock in the session, simulated time is
/// the sum of each tick's <c>Dt</c> — is about the <em>simulation</em> reading wall-clock. A save
/// stamp is not simulation. <c>RunSession</c> therefore takes one of these rather than an
/// <see cref="IClock"/>, which is what keeps <c>Soulvail.Core.Run</c> clock-free (rule 12), and
/// <c>AssemblyPurityTests</c> asserts both halves: the namespace sweep that refuses a clock, and
/// the row that says this type may have one.
/// </para>
/// <para>
/// <b>It allocates nothing.</b> <see cref="RunSnapshot"/> and <see cref="RandomState"/> are
/// <c>readonly struct</c>s, <see cref="IRandom.Capture"/> allocates nothing (M2-13a), and
/// <c>RunSnapshotTaken</c> crosses <c>IDomainEvents</c> by <c>in</c>. It is not on a tick path — it
/// runs twice a minute at most — but a boundary frame is already swapping an arena, and it is the
/// last frame in a run that should also be asking for heap (rule 7).
/// </para>
/// </remarks>
public sealed class RunRecorder
{
    private readonly IRandom _random;
    private readonly IClock _clock;
    private readonly IDomainEvents _events;

    /// <param name="random">
    /// The run's generator, for <see cref="IRandom.Capture"/>. Read from, never drawn from: a
    /// capture is a reading of where a stream stands, not a draw out of it, so recording a run
    /// cannot move the sequence it is recording.
    /// </param>
    /// <param name="clock">
    /// Wall-clock, for <see cref="RunSnapshot.WrittenAt"/>. Never <c>RunState.Time</c>, which is
    /// simulated seconds and is carried separately on the same snapshot (M2-01 rule 1).
    /// </param>
    /// <param name="events">Where <see cref="RunSnapshotTaken"/> goes.</param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    public RunRecorder(IRandom random, IClock clock, IDomainEvents events)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>
    /// Writes the run down as it stands — with the streams captured at this instant — for a resume
    /// that will begin at <paramref name="resumeStage"/>, and publishes
    /// <see cref="RunSnapshotTaken"/>.
    /// </summary>
    /// <param name="state">The live run. Read only; nothing here moves anything on it.</param>
    /// <param name="resumeStage">
    /// The stage the player is <em>about to</em> play, never the one behind them (rule 3). A
    /// resume that replayed a cleared stage would be a rewind the player did not ask for.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="resumeStage"/> is below 1, from <see cref="RunSnapshot"/>'s own guard.
    /// </exception>
    /// <remarks>
    /// The boundary call, and the ordinary one: entering <c>Clear</c> is a moment at which nothing
    /// has yet drawn for the stage being described, so the capture instant and the announcement are
    /// the same instant. The opening of a run is not, which is what the overload below is for.
    /// </remarks>
    public void Take(RunState state, int resumeStage)
    {
        Take(state, resumeStage, _random.Capture());
    }

    /// <summary>
    /// As above, for a caller that captured the streams earlier than it can announce them.
    /// </summary>
    /// <param name="random">
    /// Where every stream stood <em>before</em> anything drew for the stage
    /// <paramref name="resumeStage"/> names. This is the whole of rule 5: a resumed run restores
    /// this position and composes the same stage from it, so a position taken after the
    /// composition would make the resumed stage a different stage with the same number.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>It exists because the opening of a run captures and announces at two different moments,
    /// and cannot do otherwise.</b> <c>RunSession.Start</c> composes the opening stage inside its
    /// validation block, <em>before</em> <c>RunStarted</c> — M2-02 put it there so that an
    /// ineligible mode throws with nothing announced (ledger row 3), and AR §18.1 pins it. So the
    /// position this snapshot must carry is taken at the top of <c>Start</c>, while the snapshot
    /// itself cannot be announced until the run has been. One moment reads the dice, the other
    /// reports them, and the gap between the two is the composition they must not include.
    /// </para>
    /// <para>
    /// <b>Rejected: capturing inside <see cref="Take"/> for the opening as well.</b> It compiles,
    /// round-trips, and produces a snapshot whose streams have already spent the opening stage's
    /// composition — so resuming a run killed during its first stage would deal it a different
    /// first stage. That is exactly the bug ledger row 1 was carried forward for, and it is
    /// invisible in every test that does not compare two compositions.
    /// </para>
    /// </remarks>
    public void Take(RunState state, int resumeStage, in RandomState random)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        // Not guarded here. RunSnapshot's constructor refuses a stage below 1 with the message that
        // explains why stages are numbered from 1, and a second copy of that rule here would be the
        // first place the two could disagree (AR §18.3).
        var snapshot = new RunSnapshot(
            RunSnapshot.CurrentVersion,
            state.ModeId,
            state.CharacterId,
            state.Seed,
            resumeStage,
            random,

            // Absolute, not fractions. `RunState` reports the shield as `PlayerShieldFraction` for
            // the HUD, and a fraction cannot be restored without the maximum that produced it —
            // which M3's tree moves. The narrow read beside `PlayerHp` is what rule 4 added.
            state.PlayerHp,
            state.PlayerShield,

            // Two clocks, neither derived from the other: simulated seconds the run has lasted, and
            // the wall-clock instant it was written at.
            state.Time,
            _clock.UtcNow);

        _events.Publish(new RunSnapshotTaken(snapshot));
    }
}
