namespace Soulvail.Core.Combat;

/// <summary>Uses the target's ground-plane distance when the arrow leaves.</summary>
public sealed class EagleEye
{
    public const float FarDistance = 6f;

    public Stat FarBonus { get; } = new(0f);

    public float MultiplierAt(float distanceXZ) => distanceXZ > FarDistance
        ? 1f + FarBonus.Value : 1f;
}
