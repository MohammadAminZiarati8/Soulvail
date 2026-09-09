using System.Numerics;

namespace Soulvail.Core.Run;

/// <summary>
/// What core wants the body to do about a swing that has just landed its damage: sweep this wedge
/// and report who was standing in it. The first intent that asks a question — everything before it
/// told the body what to do and expected no answer. See AR §4.1, §6 and ADR-0003.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is still an intent, not a query.</b> Core does not wait, and nothing is returned: the body
/// resolves the overlap on its own frame and sends the result back in through a
/// <c>ReportConeHits</c> fact (M1-11), which core then turns into damage. That is the whole of
/// ADR-0003's rule that core never reaches out into the world — a physics overlap is exactly the
/// kind of thing core cannot know and must be told.
/// </para>
/// <para>
/// <b><see cref="RequestId"/> is what makes the round trip safe.</b> The answer arrives at least a
/// frame after the question, by which time the player has moved and may have swung again, so a fact
/// that only said "these enemies were hit" could be applied to the wrong swing. The id is
/// monotonic within a run and never reused, so a stale or duplicated report is recognisable rather
/// than merely unlikely.
/// </para>
/// <para>
/// The geometry travels with the request for the same reason. Resolving the cone against wherever
/// the player is when the fact comes back would silently widen every swing by a frame of movement;
/// resolving it against these numbers means the wedge that was drawn is the wedge that hits.
/// </para>
/// <para>
/// A <c>readonly struct</c>, taken by <c>in</c> at the port, like <see cref="PlayerMoveIntent"/> —
/// though this one crosses the boundary three times a second rather than sixty.
/// </para>
/// </remarks>
public readonly struct ConeHitIntent
{
    /// <summary>
    /// Identifies this request among the run's others. Starts at 1 and increases by one per swing;
    /// 0 is never issued, so a default-constructed intent cannot be mistaken for a real one.
    /// </summary>
    public readonly int RequestId;

    /// <summary>Where the wedge starts: the player's position as core last had it reported.</summary>
    public readonly Vector3 Origin;

    /// <summary>
    /// The direction the wedge is centred on, as a unit vector on the ground plane — <c>X</c> and
    /// <c>Z</c>, with the height dropped.
    /// </summary>
    /// <remarks>
    /// A <see cref="Vector2"/> rather than a flattened <see cref="Vector3"/> because it is an
    /// angle on the ground and nothing else; carrying a <c>Y</c> that is always zero would invite
    /// somebody to put something in it. Neither normalised nor validated here — core has already
    /// decided the facing, and re-deriving it would put the same computation in two places.
    /// </remarks>
    public readonly Vector2 FacingXZ;

    /// <summary>How far the wedge reaches, in metres.</summary>
    public readonly float Range;

    /// <summary>
    /// The full opening angle of the wedge, in degrees — 60 means ±30° either side of
    /// <see cref="FacingXZ"/>.
    /// </summary>
    public readonly float AngleDeg;

    public ConeHitIntent(int requestId, Vector3 origin, Vector2 facingXZ, float range, float angleDeg)
    {
        RequestId = requestId;
        Origin = origin;
        FacingXZ = facingXZ;
        Range = range;
        AngleDeg = angleDeg;
    }
}
