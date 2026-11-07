using System;

namespace Soulvail.Core.Content;

public sealed class PetSpec
{
    public PetSpec(ContentId id, float maxHp, float moveSpeed, float biteDamage, float biteInterval,
        float reach, float leash, float returnRange, float heelDistance)
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
        RequirePositive(leash, nameof(leash));
        RequirePositive(returnRange, nameof(returnRange));
        RequirePositive(heelDistance, nameof(heelDistance));

        if (returnRange < leash)
        {
            throw new ArgumentOutOfRangeException(nameof(returnRange), "Return range must cover the leash.");
        }

        if (heelDistance > leash)
        {
            throw new ArgumentOutOfRangeException(nameof(heelDistance), "Heel distance must fit inside the leash.");
        }

        Id = id;
        MaxHp = maxHp;
        MoveSpeed = moveSpeed;
        BiteDamage = biteDamage;
        BiteInterval = biteInterval;
        Reach = reach;
        Leash = leash;
        ReturnRange = returnRange;
        HeelDistance = heelDistance;
    }

    public ContentId Id { get; }
    public float MaxHp { get; }
    public float MoveSpeed { get; }
    public float BiteDamage { get; }
    public float BiteInterval { get; }
    public float Reach { get; }
    public float Leash { get; }
    public float ReturnRange { get; }
    public float HeelDistance { get; }

    private static void RequirePositive(float value, string name)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(name, "Pet stats must be finite and positive.");
        }
    }
}
