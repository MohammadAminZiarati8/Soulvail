using System.Numerics;

namespace Soulvail.Core.Events;

public enum BindAnchor { None, Enemy, Obstacle }

/// <summary>One resolved hit. Seconds is the authored duration plus upgrades, before boss scaling.</summary>
public readonly struct BindingShotBound
{
    public BindingShotBound(int targetId, BindAnchor anchor, int anchorEnemyId, Vector3 anchorPoint, float seconds)
    {
        TargetId = targetId;
        Anchor = anchor;
        AnchorEnemyId = anchorEnemyId;
        AnchorPoint = anchorPoint;
        Seconds = seconds;
    }

    public int TargetId { get; }
    public BindAnchor Anchor { get; }
    public int AnchorEnemyId { get; }
    public Vector3 AnchorPoint { get; }
    public float Seconds { get; }
}
