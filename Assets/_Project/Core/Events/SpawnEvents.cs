using System.Numerics;
using Soulvail.Core.Content;

namespace Soulvail.Core.Events;

// The director's domain events. Grouped per module like EnemyEvents and RunEvents, for the same
// reason: an event is three lines, and reading a module's vocabulary in one place is worth more
// than one type per file. See AR §5, §8 and <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// Three events and no fourth. A stage arriving, being sealed, being cleared and being left is
// M2-10's vocabulary, not this one — `SpawnDirector.IsStageComplete` is the handover, deliberately
// asked rather than announced, so that "the stage is over" has exactly one publisher when stage
// flow lands (M2-05 rule 6).

/// <summary>
/// A wave has begun: its bodies are queued and the first of them is about to be telegraphed.
/// </summary>
/// <remarks>
/// Published before anything of the wave is telegraphed, so a presenter that counts rings sees the
/// wave open before it sees the first one. It carries <see cref="WaveCount"/> because "wave 3" on
/// its own says nothing about how much of the stage is left — a HUD wants "3 of 4", and the
/// director is the only thing that knows the second number.
/// </remarks>
public readonly struct WaveStarted
{
    /// <summary>The depth this wave belongs to. Stages are numbered from 1 (GD §8.2).</summary>
    public readonly int Stage;

    /// <summary>Which wave of the stage, numbered from 1.</summary>
    public readonly int Wave;

    /// <summary>How many waves the stage holds in total — GD §12.2's W(n).</summary>
    public readonly int WaveCount;

    public WaveStarted(int stage, int wave, int waveCount)
    {
        Stage = stage;
        Wave = wave;
        WaveCount = waveCount;
    }
}

/// <summary>
/// Every body of a wave is dead.
/// </summary>
/// <remarks>
/// <b>Routinely published after the next wave has already started</b>, and that is the point of GD
/// §7.3's overlap rather than a fault: wave <c>i+1</c> begins while a quarter of wave <c>i</c> is
/// still standing, so the two events interleave. Anything that treats this as "the arena is empty"
/// is reading it wrong — that question is <c>SpawnDirector.IsStageComplete</c>, and it is asked
/// rather than published (M2-05 rule 6).
/// </remarks>
public readonly struct WaveCleared
{
    /// <summary>The depth the wave belonged to.</summary>
    public readonly int Stage;

    /// <summary>Which wave, numbered from 1.</summary>
    public readonly int Wave;

    public WaveCleared(int stage, int wave)
    {
        Stage = stage;
        Wave = wave;
    }
}

/// <summary>
/// Something is about to exist here. Published <c>SpawnDirector.TelegraphTime</c> seconds before
/// the body is spawned, at exactly the position it will appear at.
/// </summary>
/// <remarks>
/// <para>
/// <b>A telegraph is a promise, never a warning.</b> The director cannot cancel one, move one, or
/// let the concurrency cap eat one (M2-05 rules 7 and 8) — a ring the player dodged that produced
/// nothing, or produced something a metre to the left, teaches them not to trust the next one.
/// M2-12b draws it; nothing subscribes yet.
/// </para>
/// <para>
/// It carries <see cref="FiresAt"/> rather than a duration because a view that misses the frame it
/// was published on can still draw the right fraction of the ring: a deadline is absolute, and a
/// duration would have to be corrected by whatever lateness the subscriber cannot measure.
/// </para>
/// </remarks>
public readonly struct SpawnTelegraphed
{
    /// <summary>Which archetype will appear, e.g. <c>enemy.husk</c>.</summary>
    public readonly ContentId SpecId;

    /// <summary>Where it will appear, in world metres. Exactly where, not approximately.</summary>
    public readonly Vector3 Position;

    /// <summary>
    /// The simulated run second the body appears at — <c>RunState.Time</c>'s clock, never a wall
    /// clock.
    /// </summary>
    public readonly float FiresAt;

    public SpawnTelegraphed(ContentId specId, Vector3 position, float firesAt)
    {
        SpecId = specId;
        Position = position;
        FiresAt = firesAt;
    }
}
