using System;

namespace Soulvail.Core.Content;

/// <summary>Range band, timing, cone and poison for a cobra's locked spit.</summary>
public sealed class SpitConeSpec
{
    public SpitConeSpec(float holdDistance, float closeDistance, float rearSeconds, float halfAngleDegrees,
        float length, float spitDamage, float poisonFractionPerSecond, float poisonSeconds,
        int poisonMaxStacks, float recoverSeconds, float cooldownSeconds)
    {
        HoldDistance = Positive(holdDistance, nameof(holdDistance));
        CloseDistance = Positive(closeDistance, nameof(closeDistance));
        RearSeconds = Positive(rearSeconds, nameof(rearSeconds));
        HalfAngleDegrees = Positive(halfAngleDegrees, nameof(halfAngleDegrees));
        Length = Positive(length, nameof(length));
        SpitDamage = Positive(spitDamage, nameof(spitDamage));
        PoisonFractionPerSecond = Positive(poisonFractionPerSecond, nameof(poisonFractionPerSecond));
        PoisonSeconds = Positive(poisonSeconds, nameof(poisonSeconds));
        RecoverSeconds = Positive(recoverSeconds, nameof(recoverSeconds));
        CooldownSeconds = Positive(cooldownSeconds, nameof(cooldownSeconds));

        if (!(CloseDistance < HoldDistance))
        {
            throw new ArgumentOutOfRangeException(nameof(closeDistance), closeDistance,
                "The close distance must be less than the hold distance.");
        }

        if (!(HalfAngleDegrees < 90f))
        {
            throw new ArgumentOutOfRangeException(nameof(halfAngleDegrees), halfAngleDegrees,
                "The cone's half angle must be less than 90 degrees.");
        }

        if (!(PoisonFractionPerSecond <= 1f))
        {
            throw new ArgumentOutOfRangeException(nameof(poisonFractionPerSecond), poisonFractionPerSecond,
                "The poison fraction must be at most 1.");
        }

        if (poisonMaxStacks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(poisonMaxStacks), poisonMaxStacks,
                "The poison needs at least one stack.");
        }

        PoisonMaxStacks = poisonMaxStacks;
    }

    public float HoldDistance { get; }
    public float CloseDistance { get; }
    public float RearSeconds { get; }
    public float HalfAngleDegrees { get; }
    public float Length { get; }
    public float SpitDamage { get; }
    public float PoisonFractionPerSecond { get; }
    public float PoisonSeconds { get; }
    public int PoisonMaxStacks { get; }
    public float RecoverSeconds { get; }
    public float CooldownSeconds { get; }

    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The value must be finite and greater than zero.");
        }

        return value;
    }
}
