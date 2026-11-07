using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

public sealed class Pounce : IEffect
{
    internal const float DefaultMinReach = 3f;
    internal const float DefaultMaxReach = 6f;

    public Pounce(float minReach, float maxReach, float biteMultiplier, float knockdownSeconds, float leapSeconds)
    {
        MinReach = Positive(minReach, nameof(minReach));
        MaxReach = Positive(maxReach, nameof(maxReach));
        if (MaxReach < MinReach)
        {
            throw new ArgumentOutOfRangeException(nameof(maxReach));
        }

        BiteMultiplier = Positive(biteMultiplier, nameof(biteMultiplier));
        KnockdownSeconds = Positive(knockdownSeconds, nameof(knockdownSeconds));
        LeapSeconds = Positive(leapSeconds, nameof(leapSeconds));
    }

    public float MinReach { get; }
    public float MaxReach { get; }
    public float BiteMultiplier { get; }
    public float KnockdownSeconds { get; }
    public float LeapSeconds { get; }

    private static float Positive(float value, string name)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }
}

public sealed class PounceHandler : IEffectHandler<Pounce>, IEffectCastGuard<Pounce>
{
    private readonly PetSystem _pets;
    private readonly EnemySystem _enemies;
    private readonly PlayerCombat _combat;
    private readonly SimulatedClock _clock;

    public PounceHandler(PetSystem pets, EnemySystem enemies, PlayerCombat combat, SimulatedClock clock)
    {
        _pets = pets;
        _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool CanCast(Pounce effect)
    {
        Require(effect, nameof(effect));
        return _pets is not null && _pets.CanPounce(effect, _enemies);
    }

    public void Apply(Pounce effect, object source)
    {
        Remove(effect, source);
        _pets?.TryPounce(effect, _clock.Now, _enemies, _combat);
    }

    public void Remove(Pounce effect, object source)
    {
        Require(effect, nameof(effect));
        Require(source, nameof(source));
    }

    private static void Require(object value, string name)
    {
        if (value is null)
        {
            throw new ArgumentNullException(name);
        }
    }
}
