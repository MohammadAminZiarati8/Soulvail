using System.Numerics;

namespace Soulvail.Core.Events;

/// <summary>The Warlord's current frontal guard; zero means its guard is lowered.</summary>
public readonly struct WarlordGuardArc
{
    public readonly int BossId;
    public readonly float HalfArcDeg;

    public WarlordGuardArc(int bossId, float halfArcDeg)
    {
        BossId = bossId;
        HalfArcDeg = halfArcDeg;
    }
}

/// <summary>The Warlord reached a stone and wedged its shield, lowering its guard.</summary>
public readonly struct WarlordDazed
{
    public readonly int BossId;
    public readonly Vector3 At;
    public readonly float Seconds;

    public WarlordDazed(int bossId, Vector3 at, float seconds)
    {
        BossId = bossId;
        At = at;
        Seconds = seconds;
    }
}
