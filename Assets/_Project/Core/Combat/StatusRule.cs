using System;
using Soulvail.Core.Content;

namespace Soulvail.Core.Combat;

/// <summary>What a status is: its id, its side, where it draws and how a boss shortens or refuses it.</summary>
public readonly struct StatusRule
{
    /// <exception cref="ArgumentException"><paramref name="id"/> is the default id.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bossScale"/> is outside [0, 1].</exception>
    public StatusRule(ContentId id, StatusSide side, int priority, float bossScale)
    {
        if (id.Value is null)
        {
            throw new ArgumentException("A status needs an id.", nameof(id));
        }

        if (!(bossScale >= 0f) || bossScale > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(bossScale), bossScale, "A boss scale is in [0, 1].");
        }

        Id = id;
        Side = side;
        Priority = priority;
        BossScale = bossScale;
    }

    public ContentId Id { get; }

    public StatusSide Side { get; }

    /// <summary>Higher draws first within a side.</summary>
    public int Priority { get; }

    /// <summary>[0, 1]: the duration multiplier on a boss; 0 means a boss shrugs it off.</summary>
    public float BossScale { get; }
}
