using System.Numerics;

namespace Soulvail.Core.Events;

public readonly struct KillshotWindUp
{
    public KillshotWindUp(Vector3 direction, float seconds)
    {
        Direction = direction;
        Seconds = seconds;
    }

    public Vector3 Direction { get; }
    public float Seconds { get; }
}

public readonly struct KillshotFired
{
    public KillshotFired(Vector3 origin, Vector3 direction, float length, int hits)
    {
        Origin = origin;
        Direction = direction;
        Length = length;
        Hits = hits;
    }

    public Vector3 Origin { get; }
    public Vector3 Direction { get; }
    public float Length { get; }
    public int Hits { get; }
}

/// <summary>A confirmed hit, before damage or death can retire the enemy's view.</summary>
public readonly struct KillshotHit
{
    public KillshotHit(int enemyId, Vector3 at)
    {
        EnemyId = enemyId;
        At = at;
    }

    public int EnemyId { get; }
    public Vector3 At { get; }
}

public readonly struct KillshotExecuted
{
    public KillshotExecuted(int enemyId) => EnemyId = enemyId;
    public int EnemyId { get; }
}
