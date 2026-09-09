using System;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ports;

/// <summary>
/// The inbound port the body drives the brain through: start a run, tick it with what the senses
/// can see, end it. The one façade Unity holds a reference to for the whole of a run — everything
/// else core does happens behind it. See AR §4.1, §6 and
/// <see href="../../../../Docs/adr/0003-commands-facts-tick-events-intents.md">ADR-0003</see>.
/// </summary>
/// <remarks>
/// <para>
/// Inbound, so every member is a method call or a read: the body asks core to do something, or
/// looks at what core decided. Nothing here is an event and nothing here returns an outcome —
/// outcomes leave through <see cref="IDomainEvents"/>, per-tick instructions through
/// <see cref="IIntentSink"/>. That one-way shape is what keeps the flow traceable.
/// </para>
/// <para>
/// <see cref="Tick"/> takes the snapshot alone, where AR §4.1 sketches <c>Tick(dt, snapshot)</c>:
/// <c>dt</c> is already a field on the snapshot (AR §4.2), and two ways to say how much time
/// passed is one way too many. The sketch predates the snapshot carrying it.
/// </para>
/// <para>
/// The facts AR §6 lists arrive with the mechanics that produce them:
/// <see cref="ReportConeHits"/> in M1-11, <c>ReportContact</c> with the chasers of M1-18,
/// <c>ReportProjectileHit</c> with the Spitter in M2-07. Player commands have their own port,
/// <see cref="IPlayerCommands"/> (M1-09), which <c>RunSession</c> also implements: lifecycle is
/// what the frame loop holds, commands are what an input adapter holds, and an adapter able to
/// <see cref="End"/> the run would have a reach it has no business having.
/// </para>
/// </remarks>
public interface IRunSession
{
    /// <summary>Whether a run is live: <see cref="Start"/> has been called and <see cref="End"/> has not.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// The live run's state, or <c>null</c> before the first <see cref="Start"/>.
    /// </summary>
    /// <remarks>
    /// Readable after <see cref="End"/> as well, holding the run that just finished — the run-end
    /// screen (M4-06) reads its final numbers from here rather than being handed a copy.
    /// </remarks>
    RunState State { get; }

    /// <summary>
    /// Begins a run with <paramref name="config"/>, building fresh state and publishing
    /// <c>RunStarted</c>.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">A run is already running.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">
    /// The catalog holds no character with the configured id. The session stays not-running.
    /// </exception>
    void Start(RunConfig config);

    /// <summary>
    /// Advances the run by <c>snapshot.Dt</c> seconds, given where everything is right now.
    /// Called once per frame; core throttles its own expensive work internally.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">No run is running.</exception>
    void Tick(WorldSnapshot snapshot);

    /// <summary>
    /// The body's answer to the outstanding <c>ConeHitIntent</c>: these are the enemies that were
    /// standing in the wedge. Core turns it into damage, deaths and events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first fact, and the shape every later one follows: a method call carrying a physical
    /// result the instant it is known, rather than a field on the next snapshot. A hit that waited
    /// for the following frame would land after the swing that caused it had visibly finished.
    /// </para>
    /// <para>
    /// Fire and forget, in both directions. Nothing is returned — what the damage did leaves as
    /// <c>EnemyDamaged</c> and <c>EnemyDied</c> — and a report core is not expecting is dropped in
    /// silence, so the body never has to know which of its swings are still owed an answer.
    /// </para>
    /// </remarks>
    /// <param name="enemyIds">
    /// The ids found in the cone. A <see cref="ReadOnlySpan{T}"/> so that the body can hand over a
    /// slice of a buffer it reuses every frame: nothing is copied and nothing is retained.
    /// Duplicates, unknown ids and ids that have since died are all harmless.
    /// </param>
    /// <exception cref="System.InvalidOperationException">No run is running.</exception>
    void ReportConeHits(ReadOnlySpan<int> enemyIds);

    /// <summary>
    /// Ends the run and publishes <c>RunEnded</c>. A no-op when no run is running, so scope
    /// disposal can call it without first asking whether it needs to.
    /// </summary>
    void End();
}
