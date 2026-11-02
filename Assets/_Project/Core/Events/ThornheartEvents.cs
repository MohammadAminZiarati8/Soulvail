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
