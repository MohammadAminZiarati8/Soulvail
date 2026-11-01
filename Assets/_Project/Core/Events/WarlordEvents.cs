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
