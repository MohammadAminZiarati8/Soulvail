using System.Numerics;

namespace Soulvail.Core.Combat;

/// <summary>A living enemy's geometry, copied from the registry before selecting a target.</summary>
public readonly struct EnemyPoint
{
    public EnemyPoint(int id, Vector3 position, float radius, bool isBoss)
    {
        Id = id;
        Position = position;
        Radius = radius;
        IsBoss = isBoss;
    }

    public int Id { get; }
    public Vector3 Position { get; }
    public float Radius { get; }
    public bool IsBoss { get; }
}
