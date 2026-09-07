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
/// a run publishes its own event first (M4-05's payout reads <c>PlayerDied</c>), and this one
/// closes the run for everyone who only needs to know that it did: the HUD, the ticker, the
/// scope teardown.
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
