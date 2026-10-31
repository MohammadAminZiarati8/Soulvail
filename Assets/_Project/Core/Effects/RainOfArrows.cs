using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Effects;

/// <summary>Immutable rain geometry and weapon multiplier; rank upgrades use PlayerStat addresses.</summary>
public sealed class RainOfArrows : IEffect
{
    public RainOfArrows(float radius, float duration, float pulseInterval, float damageOfWeapon)
    {
        Radius = Positive(radius, nameof(radius));
        Duration = Positive(duration, nameof(duration));
        PulseInterval = Positive(pulseInterval, nameof(pulseInterval));
        DamageOfWeapon = Positive(damageOfWeapon, nameof(damageOfWeapon));
        if (duration / pulseInterval > ZoneSystem.MaxPulses)
        {
            throw new ArgumentOutOfRangeException(nameof(pulseInterval));
        }
    }

    public float Radius { get; }
    public float Duration { get; }
    public float PulseInterval { get; }
    public float DamageOfWeapon { get; }

    private static float Positive(float value, string name)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }
}

/// <summary>Places rain on a living cluster. Scratch storage belongs to the run, never the effect.</summary>
public sealed class RainOfArrowsHandler : IEffectHandler<RainOfArrows>, IEffectCastGuard<RainOfArrows>
{
    private readonly ZoneSystem _zones;
    private readonly SimulatedClock _clock;
    private readonly EnemySystem _enemies;
    private readonly PlayerStats _stats;
    private readonly CombatBlackboard _blackboard;
    private readonly IDomainEvents _events;
    private readonly EnemyPoint[] _points;

    public RainOfArrowsHandler(
        ZoneSystem zones, SimulatedClock clock, EnemySystem enemies, PlayerStats stats,
        CombatBlackboard blackboard, IDomainEvents events)
    {
        _zones = zones ?? throw new ArgumentNullException(nameof(zones));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _points = new EnemyPoint[enemies.Registry.Capacity];
    }

    public bool CanCast(RainOfArrows effect) => TryPick(effect, out _);

    public void Apply(RainOfArrows effect, object source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (!TryPick(effect, out Vector3 centre))
        {
            return;
        }

        float radius = effect.Radius + _stats.Resolve(PlayerStat.RainRadius).Value;
        float damage = effect.DamageOfWeapon * _stats.Resolve(PlayerStat.WeaponDamage).Value
            * (1f + _stats.Resolve(PlayerStat.RainDamage).Value);
        _zones.Spawn(radius, effect.Duration, damage, effect.PulseInterval, _clock.Now, source,
            ZoneSide.BurnsEnemies, at: centre, slow: _stats.Resolve(PlayerStat.RainSlow).Value);
        _events.Publish(new RainOfArrowsCast(centre, radius, effect.Duration));
    }

    public void Remove(RainOfArrows effect, object source)
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

    private bool TryPick(RainOfArrows effect, out Vector3 centre)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        ReadOnlySpan<EnemyAgent> agents = _enemies.Registry.Alive;
        int count = 0;
        for (int i = 0; i < agents.Length; i++)
        {
            EnemyAgent agent = agents[i];
            if (agent.IsAlive)
            {
                _points[count++] = new EnemyPoint(agent.Id, agent.Position, agent.Spec.BodyRadius,
                    agent.Spec.Behaviour == EnemyBehaviourKind.Boss);
            }
        }

        return DensestPoint.TryPick(new ReadOnlySpan<EnemyPoint>(_points, 0, count),
            _blackboard.PlayerPosition, _stats.Resolve(PlayerStat.WeaponRange).Value,
            effect.Radius + _stats.Resolve(PlayerStat.RainRadius).Value, out centre);
    }
}
