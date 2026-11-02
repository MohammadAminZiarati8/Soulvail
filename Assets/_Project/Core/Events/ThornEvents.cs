using System.Numerics;

namespace Soulvail.Core.Events;

/// <summary>
/// A thorn wall has been placed and is arming (JG-20a rule 1). Published on the tick it opens.
/// </summary>
/// <remarks>
/// Self-sufficient for <see cref="LaneArmed"/>'s reason: the strip a view warns on is exactly the
/// rectangle <c>ThornSystem</c> tests, and the arm rides along so the warning cannot disagree with
/// the hazard about how long the player has.
/// </remarks>
public readonly struct ThornArmed
{
    /// <summary>The wall's run-stable id, issued from 1 and never reused within a run.</summary>
    public readonly int Id;

    /// <summary>Where it starts, in world metres.</summary>
    public readonly Vector3 Origin;

    /// <summary>Which way it runs: a unit vector on XZ, X then Z.</summary>
    public readonly Vector2 Direction;

    /// <summary>How far it runs, in metres.</summary>
    public readonly float Length;

    /// <summary>Half its width, in metres.</summary>
    public readonly float HalfWidth;

    /// <summary>Seconds before it rises. Never under GD §9.1 rule 1's 0.6.</summary>
    public readonly float ArmSeconds;

    public ThornArmed(
        int id,
        Vector3 origin,
        Vector2 direction,
        float length,
        float halfWidth,
        float armSeconds)
    {
        Id = id;
        Origin = origin;
        Direction = direction;
        Length = length;
        HalfWidth = halfWidth;
        ArmSeconds = armSeconds;
    }
}

/// <summary>The arm is over and the wall has risen; it bit whoever stood in it (rule 2).</summary>
/// <remarks>Published whether or not anyone was inside, for <see cref="LaneFired"/>'s reason.</remarks>
public readonly struct ThornRose
{
    /// <summary>Which wall — the id <see cref="ThornArmed"/> named.</summary>
    public readonly int Id;

    /// <summary>
    /// The player was inside as it rose and i-frames did not turn the bite away.
    /// </summary>
    public readonly bool Hit;

    public ThornRose(int id, bool hit)
    {
        Id = id;
        Hit = hit;
    }
}

/// <summary>The wall has withered and the ground is open again (rules 3, 4 and 6).</summary>
public readonly struct ThornWithered
{
    /// <summary>Which wall — the id <see cref="ThornArmed"/> named.</summary>
    public readonly int Id;

    public ThornWithered(int id)
    {
        Id = id;
    }
}
