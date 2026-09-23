using System;
using System.Collections.Generic;
using Soulvail.Core.Content;

namespace Soulvail.Core.Events;

// The run module's domain events. Event structs are grouped per module — the one accepted
// exception to one type per file, because an event is three lines and reading a module's
// vocabulary in one place is worth more than the rule. See AR §5, §8 and
// <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// Every event here says what happened, never what to do, and carries only what a listener
// cannot look up for itself. They are `readonly struct`s with public readonly fields and a
// constructor: no properties, no logic, no behaviour to go wrong, and nothing to allocate when
// they cross `IDomainEvents` by `in`.

/// <summary>
/// A run has begun. Published by <c>RunSession.Start</c> before <c>IsRunning</c> flips, so a
/// handler that reads the session inside this event still sees a session that is not running —
/// the run is announced, not yet live.
/// </summary>
public readonly struct RunStarted
{
    /// <summary>The class being played.</summary>
    public readonly ContentId CharacterId;

    /// <summary>
    /// What the run's random generator was seeded with — the number a bug report or a Daily
    /// needs to reproduce this run's spawns.
    /// </summary>
    public readonly int Seed;

    public RunStarted(ContentId characterId, int seed)
    {
        CharacterId = characterId;
        Seed = seed;
    }
}

/// <summary>
/// A run is over, however it ended. Published by <c>RunSession.End</c>.
/// </summary>
/// <remarks>
/// Carries no cause — death, a completed descent, the player quitting to the menu. Whatever ends
/// a run publishes its own event first (a death publishes <c>PlayerDied</c> and then
/// <see cref="ShardsAwarded"/>), and this one closes the run for everyone who only needs to know
/// that it did: the HUD, the ticker, the scope teardown.
/// </remarks>
public readonly struct RunEnded
{
    /// <summary>Simulated seconds the run lasted, as <c>RunState.Time</c> read at the end.</summary>
    public readonly float Time;

    public RunEnded(float time)
    {
        Time = time;
    }
}

/// <summary>
/// What the run just paid, in Soul Shards (GD §14.1). Published on the death path and nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own event rather than a field on <see cref="RunEnded"/>, and that is a ruling.</b>
/// <c>RunEnded</c> is also published when <c>RunScope</c> is torn down for any other reason —
/// <c>RunSession.End</c> is a deliberate no-op-if-not-running so disposal can call it blind, which
/// is why <c>SaveWriter</c>, <c>HudPresenter</c> and <c>PausePresenter</c> have each already
/// refused that event. A payout riding <c>RunEnded</c> would pay a player for quitting to the menu,
/// and would pay them again on every scope teardown. So the payout gets this event instead,
/// published only inside <c>RunSession.Tick</c>'s death branch: the order on the wire is
/// <c>PlayerDied</c> → <c>ShardsAwarded</c> → <c>RunEnded</c>.
/// </para>
/// <para>
/// <b>It carries its own breakdown</b> — the two terms as well as the total — because the screen
/// that draws it is a readout and must not hold a handle to the thing that computed it. That is
/// M4-04's precedent, applied to the event the payout actually rides.
/// </para>
/// </remarks>
public readonly struct ShardsAwarded
{
    /// <summary>What this run is worth, all terms summed.</summary>
    public readonly int Total;

    /// <summary>The first term's input: <c>RunState.StageIndex</c> at the moment of death.</summary>
    public readonly int DeepestStage;

    /// <summary>The second term's input: how many boss stages the run left behind it.</summary>
    public readonly int BossesKilled;

    /// <summary>
    /// The archetypes this run met for the first time, in the order they were met — the third
    /// term's input. Empty on almost every death, never null; what <c>ShardWriter</c> adds to the
    /// profile's set (M6-09a rule 8).
    /// </summary>
    /// <remarks>
    /// The ids rather than a count, because the writer has to add them to a set and a count cannot
    /// say which. <b>One allocation per run</b>, on the death tick, which is not a frame path
    /// (M6-09a rule 6).
    /// </remarks>
    public readonly IReadOnlyList<ContentId> NewArchetypes;

    /// <summary>
    /// The mode the run was played in — what <c>ShardWriter</c> hands <c>ClassUnlocks.Earned</c> for
    /// its boss roster. <c>default</c> only from a publisher that is not a run.
    /// </summary>
    /// <remarks>
    /// On the event because the writer cannot look it up: by the time a listener hears this, the
    /// run that knew its mode is being torn down, and nothing else in the run scope states it.
    /// </remarks>
    public readonly ContentId ModeId;

    /// <param name="newArchetypes">
    /// Optional, and null reads as none: most readers of this event draw a number and never ask.
    /// </param>
    /// <param name="modeId">Optional for the same reason; <c>RunSession</c> always states it.</param>
    public ShardsAwarded(
        int total,
        int deepestStage,
        int bossesKilled,
        IReadOnlyList<ContentId> newArchetypes = null,
        ContentId modeId = default)
    {
        Total = total;
        DeepestStage = deepestStage;
        BossesKilled = bossesKilled;
        NewArchetypes = newArchetypes ?? Array.Empty<ContentId>();
        ModeId = modeId;
    }
}
