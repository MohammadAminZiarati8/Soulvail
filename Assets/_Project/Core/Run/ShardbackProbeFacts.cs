using System;

namespace Soulvail.Core.Run;

/// <summary>Identifies a probe cast within one boss lifetime.</summary>
public readonly struct ShardbackProbeKey
{
    public readonly Guid Lifetime;
    public readonly ulong Cast;

    public ShardbackProbeKey(Guid lifetime, ulong cast)
    {
        if (lifetime == Guid.Empty)
        {
            throw new ArgumentException("A lifetime is required.", nameof(lifetime));
        }
        if (cast == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cast));
        }
        Lifetime = lifetime;
        Cast = cast;
    }

    public bool IsValid => Lifetime != Guid.Empty && Cast > 0;
}

/// <summary>Unknown is conservatively blocked; it never identifies a pillar.</summary>
public enum ShardbackStopKind { Unknown, None, Cover, Boundary, Pillar }

/// <summary>Immutable obstruction fact; the requesting behaviour owns correlation and outcomes.</summary>
public readonly struct ShardbackStop
{
    public readonly int BossId;
    public readonly ShardbackProbeKey Key;
    public readonly int Lane;
    public readonly ShardbackStopKind Kind;
    public readonly float Distance;
    public readonly int PillarSlot;

    public ShardbackStop(int bossId, ShardbackProbeKey key, int lane,
        ShardbackStopKind kind, float distance, int pillarSlot)
    {
        BossId = bossId;
        Key = key;
        Lane = lane;
        Kind = kind;
        Distance = distance;
        PillarSlot = pillarSlot;
        if (!IsValid)
        {
            throw new ArgumentException("Invalid Shardback obstruction fact.");
        }
    }

    public bool IsValid => BossId > 0 && Key.IsValid && Lane >= 0 && Lane <= 3
        && Kind >= ShardbackStopKind.Unknown && Kind <= ShardbackStopKind.Pillar
        && !float.IsNaN(Distance) && !float.IsInfinity(Distance) && Distance >= 0f
        && (Kind == ShardbackStopKind.Pillar ? PillarSlot >= 0 && PillarSlot <= 2 : PillarSlot == -1);
}
