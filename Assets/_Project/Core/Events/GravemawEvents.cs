using System.Numerics;

namespace Soulvail.Core.Events;

/// <summary>Gravemaw went underground and began a chase (JG-25a rule 2).</summary>
public readonly struct GravemawBurrowed
{
    public readonly int BossId;

    public GravemawBurrowed(int bossId)
    {
        BossId = bossId;
    }
}

/// <summary>
/// Gravemaw fixed its ring on the quarry's predicted spot; it erupts there after
/// <see cref="Seconds"/> (JG-25a rule 5). The centre never moves.
/// </summary>
public readonly struct GravemawRingArmed
{
    public readonly int BossId;

    /// <summary>The ring's centre on the ground plane.</summary>
    public readonly Vector3 At;

    /// <summary>Metres from <see cref="At"/>.</summary>
    public readonly float Radius;

    /// <summary>How long until the eruption.</summary>
    public readonly float Seconds;

    public GravemawRingArmed(int bossId, Vector3 at, float radius, float seconds)
    {
        BossId = bossId;
        At = at;
        Radius = radius;
        Seconds = seconds;
    }
}

/// <summary>Gravemaw erupted at its ring (JG-25a rule 7).</summary>
public readonly struct GravemawErupted
{
    public readonly int BossId;

    public readonly Vector3 At;

    public readonly float Radius;

    /// <summary>The player stood inside the ring and the damage was not turned away.</summary>
    public readonly bool Hit;

    public GravemawErupted(int bossId, Vector3 at, float radius, bool hit)
    {
        BossId = bossId;
        At = at;
        Radius = radius;
        Hit = hit;
    }
}

/// <summary>Gravemaw's surfaced window opened, or closed again (JG-25a rule 8).</summary>
public readonly struct GravemawWindow
{
    public readonly int BossId;

    /// <summary>Opened when true; closed when false.</summary>
    public readonly bool Open;

    /// <summary>How long it stays open; zero on a close.</summary>
    public readonly float Seconds;

    public GravemawWindow(int bossId, bool open, float seconds)
    {
        BossId = bossId;
        Open = open;
        Seconds = seconds;
    }
}

/// <summary>A crack opened in phase 2; an add crawls out of it after <see cref="Seconds"/> (JG-25d rule 4).</summary>
public readonly struct GravemawCrack
{
    public readonly int BossId;

    public readonly Vector3 At;

    public readonly float Seconds;

    public GravemawCrack(int bossId, Vector3 at, float seconds)
    {
        BossId = bossId;
        At = at;
        Seconds = seconds;
    }
}
