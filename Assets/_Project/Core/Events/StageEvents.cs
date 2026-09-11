using System.Numerics;
using Soulvail.Core.Content;

namespace Soulvail.Core.Events;

// Stage flow's domain events. Grouped per module like SpawnEvents, EnemyEvents and RunEvents, for
// the same reason: an event is three lines, and reading a module's vocabulary in one place is worth
// more than one type per file. See AR §5, §8 and <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// Three events and no fourth, and the gaps are deliberate. There is no `StageGateOpened` — the door
// opening is what `StageCleared` means, and a second event for the same instant would be a second
// thing to keep in step. There is no `StageTransitionFinished` either: the next `StageArrived` is
// that, and it carries the stage the screen is uncovering onto (rule 9).

/// <summary>
/// A stage has begun. The barrier is sealed, the number may be shown, and nothing has spawned yet.
/// </summary>
/// <remarks>
/// <para>
/// <b>Published for the first stage too, not only for boundaries.</b> One event, one handler, one
/// code path for dressing an arena — the alternative is a <c>RunStarted</c> branch on the Unity side
/// doing the same work twice, and the two drift the first time one of them gains a line (rule 4).
/// </para>
/// <para>
/// It carries the arena rather than a separate advance event doing so: by the time anything needs to
/// know which arena to raise, this has already said so. <see cref="ArenaId"/> is
/// <c>default(ContentId)</c> until M2-11a fills the roster, exactly as <c>SpawnTelegraphed</c>
/// shipped with nothing drawing it (rule 5).
/// </para>
/// </remarks>
public readonly struct StageArrived
{
    /// <summary>The depth that has just begun. Stages are numbered from 1 (GD §8.2).</summary>
    public readonly int Stage;

    /// <summary>Which arena it is fought in, or <c>default</c> while the roster is empty.</summary>
    public readonly ContentId ArenaId;

    public StageArrived(int stage, ContentId arenaId)
    {
        Stage = stage;
        ArenaId = arenaId;
    }
}

/// <summary>
/// Every body the stage spawned is dead. The barrier drops and the door opens.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not <c>WaveCleared</c> with the last number on it.</b> Waves overlap by design (GD §7.3), so a
/// wave clearing says nothing about the arena being empty — that question is
/// <c>SpawnDirector.IsStageComplete</c>, asked by the flow rather than published by the director, so
/// that "the stage is over" has exactly one publisher (M2-05 rule 6).
/// </para>
/// <para>
/// <see cref="NextArenaId"/> is the owner's rendering rule made into a payload: the next stage must
/// not be visible before the player reaches it, so nothing of it exists during the fight, and this
/// beat is the window in which it may be built inactive and unlit, ready for a swap behind a covered
/// screen. Core is the only thing that knows which arena is next, so it says so at the one moment
/// the answer is useful (rule 5).
/// </para>
/// <para>
/// This is also the moment M2-14 will write the run to disk — GD §7.3 wants a stage boundary
/// persisted, and this is the boundary. Nothing is stubbed for it here.
/// </para>
/// </remarks>
public readonly struct StageCleared
{
    /// <summary>The depth that has just been cleared.</summary>
    public readonly int Stage;

    /// <summary>
    /// Where the door out is, in world metres — the position the flow read off the snapshot. Zero
    /// when the arena was dressed without one, which parks the flow in <c>Gate</c> (rule 15).
    /// </summary>
    public readonly Vector3 GatePosition;

    /// <summary>
    /// Which arena the next stage is fought in, to be prepared unrendered during this beat.
    /// <c>default</c> while the roster is empty, and for the final stage of a finite mode.
    /// </summary>
    public readonly ContentId NextArenaId;

    public StageCleared(int stage, Vector3 gatePosition, ContentId nextArenaId)
    {
        Stage = stage;
        GatePosition = gatePosition;
        NextArenaId = nextArenaId;
    }
}

/// <summary>
/// The player has stepped into the door. The screen has <see cref="Duration"/> seconds to cover
/// itself before the next <see cref="StageArrived"/> swaps the world underneath it.
/// </summary>
/// <remarks>
/// <b>The fade is timed by core and reported to nobody.</b> The alternative — Unity telling core the
/// fade had finished — would be a command on the input channel whose only content is the passage of
/// time core is already measuring, and it would let a dropped frame in a view stall the simulation.
/// So the duration rides out on the event, the HUD darkens over exactly that many seconds, and the
/// next <see cref="StageArrived"/> is what clears it again (rule 9).
/// </remarks>
public readonly struct StageTransitionStarted
{
    /// <summary>The depth being left.</summary>
    public readonly int Stage;

    /// <summary>Seconds the cover has to become opaque in. <c>StageFlow.FadeTime</c>.</summary>
    public readonly float Duration;

    public StageTransitionStarted(int stage, float duration)
    {
        Stage = stage;
        Duration = duration;
    }
}
