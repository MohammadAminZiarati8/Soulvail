using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

public sealed class Howl : IEffect
{
    public Howl(float attackSpeedBonus)
    {
        if (!(attackSpeedBonus > 0f) || float.IsInfinity(attackSpeedBonus))
        {
            throw new ArgumentOutOfRangeException(nameof(attackSpeedBonus));
        }

        AttackSpeedBonus = attackSpeedBonus;
    }

    public float AttackSpeedBonus { get; }
}

public sealed class HowlHandler : IEffectHandler<Howl>, IEffectCastGuard<Howl>
{
    private readonly PetSystem _pets;
    private readonly PlayerStats _stats;
    private readonly SimulatedClock _clock;

    public HowlHandler(PetSystem pets, PlayerStats stats, SimulatedClock clock)
    {
        _pets = pets;
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool CanCast(Howl effect) => _pets?.HasStandingPet() == true;

    public void Apply(Howl effect, object source) =>
        _pets?.Howl(_stats.HowlHeal.Value, effect.AttackSpeedBonus, _stats.HowlSeconds.Value, _clock.Now);

    public void Remove(Howl effect, object source)
    {
    }
}
