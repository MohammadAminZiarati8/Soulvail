using System.Numerics;

namespace Soulvail.Core.Events;

public readonly struct HawkArrived
{
}

public readonly struct HawkDove
{
    public readonly int EnemyId;
    public readonly Vector3 From;
    public readonly Vector3 To;

    public HawkDove(int enemyId, Vector3 from, Vector3 to)
    {
        EnemyId = enemyId;
        From = from;
        To = to;
    }
}

public readonly struct HawkStruck
{
    public readonly int EnemyId;
    public readonly float Amount;

    public HawkStruck(int enemyId, float amount)
    {
        EnemyId = enemyId;
        Amount = amount;
    }
}

public readonly struct HawkLeft
{
}
