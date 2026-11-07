using System.Numerics;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Events;

public readonly struct AxeThrown
{
    public readonly int Id;
    public readonly Vector3 From;
    public readonly Vector3 TurnPoint;

    public AxeThrown(int id, Vector3 from, Vector3 turnPoint)
    {
        Id = id;
        From = from;
        TurnPoint = turnPoint;
    }
}

public readonly struct AxeHit
{
    public readonly int Id;
    public readonly int EnemyId;
    public readonly AxePass Pass;
    public readonly float Amount;

    public AxeHit(int id, int enemyId, AxePass pass, float amount)
    {
        Id = id;
        EnemyId = enemyId;
        Pass = pass;
        Amount = amount;
    }
}

public readonly struct AxeTurned
{
    public readonly int Id;
    public readonly Vector3 Position;

    public AxeTurned(int id, Vector3 position)
    {
        Id = id;
        Position = position;
    }
}

public readonly struct AxeCaught
{
    public readonly int Id;

    public AxeCaught(int id)
    {
        Id = id;
    }
}
