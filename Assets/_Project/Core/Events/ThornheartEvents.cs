using System.Numerics;

namespace Soulvail.Core.Events;

/// <summary>
/// Thornheart's heart opened to double damage after a slam rose, or closed again (JG-20b rule 5).
/// </summary>
public readonly struct HeartExposed
{
    public readonly int BossId;

    /// <summary>Opened when true; closed when false.</summary>
    public readonly bool Open;

    /// <summary>How long it stays open; zero on a close.</summary>
    public readonly float Seconds;

    public HeartExposed(int bossId, bool open, float seconds)
    {
        BossId = bossId;
        Open = open;
        Seconds = seconds;
    }
}

/// <summary>
/// Thornheart planted a seed pod that bursts into Rootlings if it is still alive when it ripens
/// (JG-20c rule 2).
/// </summary>
public readonly struct ThornPodPlanted
{
    public readonly int BossId;

    /// <summary>The pod's enemy id.</summary>
    public readonly int PodId;

    public readonly Vector3 At;

    /// <summary>How long until it ripens.</summary>
    public readonly float RipenSeconds;

    public ThornPodPlanted(int bossId, int podId, Vector3 at, float ripenSeconds)
    {
        BossId = bossId;
        PodId = podId;
        At = at;
        RipenSeconds = ripenSeconds;
    }
}
