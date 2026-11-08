using System;

namespace Soulvail.Core.Content;

public sealed class PetSpec
{
    public PetSpec(ContentId id, float maxHp, float moveSpeed, float biteDamage, float biteInterval,
        float reach, float heelDistance, bool isRanged = false)
    {
        if (id.Value is null)
        {
            throw new ArgumentException("A pet needs a valid content id.", nameof(id));
        }

        RequirePositive(maxHp, nameof(maxHp));
        RequirePositive(moveSpeed, nameof(moveSpeed));
        RequirePositive(biteDamage, nameof(biteDamage));
        RequirePositive(biteInterval, nameof(biteInterval));
        RequirePositive(reach, nameof(reach));
        RequirePositive(heelDistance, nameof(heelDistance));

        Id = id;
        MaxHp = maxHp;
        MoveSpeed = moveSpeed;
        BiteDamage = biteDamage;
        BiteInterval = biteInterval;
        Reach = reach;
        HeelDistance = heelDistance;
        IsRanged = isRanged;
    }

    public float HeelAt { get; } = 0.35f;
    public float ReturnAt { get; } = 0.8f;
    public float HeelRegen { get; } = 0.06f;
    public float RescueSeconds { get; } = 2f;
    public float RescueRadius { get; } = 2.5f;
    public float RiseFraction { get; } = 0.5f;

    /// <summary>BT-07a: a ranged pet holds off and spits instead of biting.</summary>
    public bool IsRanged { get; }
    public float SpitRange { get; } = 7f;
    public float HoldDistance { get; } = 5f;
    public float BackOffDistance { get; } = 3f;
    public float SpitInterval { get; } = 1.5f;
    public float SpitSpeed { get; } = 12f;

    public ContentId Id { get; }
    public float MaxHp { get; }
    public float MoveSpeed { get; }
    public float BiteDamage { get; }
    public float BiteInterval { get; }
    public float Reach { get; }
    public float HeelDistance { get; }

    private static void RequirePositive(float value, string name)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(name, "Pet stats must be finite and positive.");
        }
    }
}
