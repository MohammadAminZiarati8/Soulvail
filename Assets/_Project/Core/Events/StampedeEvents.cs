using System.Numerics;

namespace Soulvail.Core.Events;

public readonly struct StampedeStarted
{
    public readonly Vector3 From;
    public readonly Vector3 Direction;
    public readonly float Length;
    public readonly int Boars;
    public readonly float HalfWidth;

    public StampedeStarted(Vector3 from, Vector3 direction, float length, int boars, float halfWidth)
    {
        From = from;
        Direction = direction;
        Length = length;
        Boars = boars;
        HalfWidth = halfWidth;
    }
}

public readonly struct BoarHit
{
    public readonly int EnemyId;
    public readonly float Amount;

    public BoarHit(int enemyId, float amount)
    {
        EnemyId = enemyId;
        Amount = amount;
    }
}

public readonly struct StampedeEnded
{
}
