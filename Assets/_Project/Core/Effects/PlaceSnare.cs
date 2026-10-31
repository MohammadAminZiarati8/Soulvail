using System;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

/// <summary>An independently lived trap; upgrades are read once at cast time.</summary>
public sealed class PlaceSnare : IEffect
{
    public PlaceSnare(float radius, float armDelay, float lifetime, float rootSeconds, float damageOfWeapon)
    {
        Radius = Positive(radius, nameof(radius));
        ArmDelay = Positive(armDelay, nameof(armDelay));
        Lifetime = Positive(lifetime, nameof(lifetime));
        RootSeconds = Positive(rootSeconds, nameof(rootSeconds));
        DamageOfWeapon = Positive(damageOfWeapon, nameof(damageOfWeapon));
    }

    public float Radius { get; }
    public float ArmDelay { get; }
    public float Lifetime { get; }
    public float RootSeconds { get; }
    public float DamageOfWeapon { get; }

    private static float Positive(float value, string name)
    {
        if (!(value > 0f) || !float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }
}

public sealed class PlaceSnareHandler : IEffectHandler<PlaceSnare>
{
    private readonly TrapSystem _traps;
    private readonly SimulatedClock _clock;
    private readonly PlayerStats _stats;
    private readonly CombatBlackboard _blackboard;

    public PlaceSnareHandler(TrapSystem traps, SimulatedClock clock, PlayerStats stats, CombatBlackboard blackboard)
    {
        _traps = traps ?? throw new ArgumentNullException(nameof(traps));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
    }

    public void Apply(PlaceSnare effect, object source)
    {
        Require(effect, source);
        float root = effect.RootSeconds + _stats.Resolve(PlayerStat.SnareRoot).Value;
        float damage = effect.DamageOfWeapon * _stats.Resolve(PlayerStat.WeaponDamage).Value
            * (1f + _stats.Resolve(PlayerStat.SnareDamage).Value);
        _traps.Place(_blackboard.PlayerPosition, root, damage, effect.Lifetime, _clock.Now,
            effect.Radius, effect.ArmDelay);
    }

    // A trap owns its lifetime. Removing the granting node does not remove the ground it placed.
    public void Remove(PlaceSnare effect, object source) => Require(effect, source);

    private static void Require(PlaceSnare effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }
    }
}
