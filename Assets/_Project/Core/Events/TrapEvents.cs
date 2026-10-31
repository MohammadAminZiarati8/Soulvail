using System.Numerics;

namespace Soulvail.Core.Events;

public readonly struct TrapPlaced
{
    public TrapPlaced(int id, Vector3 at) { Id = id; At = at; }
    public int Id { get; }
    public Vector3 At { get; }
}

public readonly struct TrapSprung
{
    public TrapSprung(int id, int enemyId) { Id = id; EnemyId = enemyId; }
    public int Id { get; }
    public int EnemyId { get; }
}

public readonly struct TrapExpired
{
    public TrapExpired(int id) { Id = id; }
    public int Id { get; }
}
