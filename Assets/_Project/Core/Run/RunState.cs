using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Run;

/// <summary>
/// Everything the live run is, right now. Core is the single source of truth: views hold no
/// gameplay state, they render events and read intents. Created by <c>RunSession.Start</c> and
/// dropped when the next run starts; the whole object dies with <c>RunScope</c>. See AR §10.2.
/// </summary>
/// <remarks>
/// <para>
/// The constructor and the two setters are <c>internal</c>, which is the point of this type
/// rather than a detail of it: no view can nudge the clock, no adapter can teleport the player by
/// assignment. Unity moves the player because core told it to through a
/// <see cref="PlayerMoveIntent"/>, and reports where that ended up in the next snapshot;
/// <see cref="PlayerPosition"/> is core writing down what it was told, never core deciding.
/// </para>
/// <para>
/// That seal covers this object's own fields, not everything reachable through them.
/// <see cref="Motor"/> is a live object with a public <c>Tick</c>, so nothing in the compiler
/// stops a view from advancing it a second time and double-integrating the frame. Nothing outside
/// core has a reason to — the velocity a view needs arrives in a <see cref="PlayerMoveIntent"/>,
/// not from here — and once M0-16 has confirmed that, this property can narrow to the two values
/// anyone actually reads.
/// </para>
/// <para>
/// The guards that <see cref="CharacterSpec"/> and <see cref="RunConfig"/> carry are absent here
/// on purpose. Those two are built from authored assets and from a menu choice, so they defend a
/// boundary; this constructor is reachable only from core, with arguments core just built, so a
/// guard here would defend against a bug in the same assembly — and, being unreachable from
/// <c>Soulvail.Tests.Core</c>, could only be tested by opening internals to the test assembly.
/// Opening internals to prove a check against your own mistake is the wrong trade. If this
/// constructor ever becomes public, the guards come with it.
/// </para>
/// <para>
/// Grows a field per milestone — health and shields (M1-02), the stage and its wave (M2-10),
/// level, XP and the tree (M3) — and stays the one place a run's live numbers live.
/// </para>
/// </remarks>
public sealed class RunState
{
    internal RunState(ContentId characterId, int seed, CharacterSpec character, PlayerMotor motor)
    {
        CharacterId = characterId;
        Seed = seed;
        Character = character;
        Motor = motor;
    }

    /// <summary>The class being played. Same id as <see cref="Character"/>'s, kept for the log line that quotes it before the spec is dereferenced.</summary>
    public ContentId CharacterId { get; }

    /// <summary>
    /// What the run's random generator was seeded with.
    /// </summary>
    /// <remarks>
    /// Recorded, not chosen: <c>IRandom</c> owns the seed and this is a copy of it, so that a
    /// player reporting a bug — or a Daily being reproduced — has the one number that replays the
    /// spawns. See <see href="../../../../Docs/adr/0011-random-streams.md">ADR-0011</see>.
    /// </remarks>
    public int Seed { get; }

    /// <summary>The class's authored numbers, resolved from the catalog at <c>Start</c>.</summary>
    public CharacterSpec Character { get; }

    /// <summary>The player's movement, ticked every frame. The run owns it; nothing else may.</summary>
    public PlayerMotor Motor { get; }

    /// <summary>
    /// Seconds of simulated run time, summed from each tick's <c>Dt</c>.
    /// </summary>
    /// <remarks>
    /// Simulated, not wall-clock: it does not advance while the game is paused or backgrounded,
    /// which is what makes it fair to show as a run's duration and safe to compare between runs.
    /// Wall-clock is <c>IClock</c>'s job (M2-01), and it is a different number.
    /// </remarks>
    public float Time { get; internal set; }

    /// <summary>
    /// Where the body last reported the player to be.
    /// </summary>
    /// <remarks>
    /// A sense, written from the snapshot each tick. Core never assigns a position to move the
    /// player — collision is resolved by Unity's character controller, so the only honest
    /// position is the one that came back.
    /// </remarks>
    public Vector3 PlayerPosition { get; internal set; }
}
