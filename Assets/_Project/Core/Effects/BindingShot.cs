using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Effects;

/// <summary>Shared arrow data; pending hits and probes belong to the run's resolver.</summary>
public sealed class BindingShot : IEffect
{
    public BindingShot(float reach, float coneHalfAngleDeg, float boundSeconds, float aloneSeconds,
        float damageOfWeapon)
    {
        Reach = Positive(reach, nameof(reach));
        ConeHalfAngleDeg = Positive(coneHalfAngleDeg, nameof(coneHalfAngleDeg));
        if (coneHalfAngleDeg > 90f)
        {
            throw new ArgumentOutOfRangeException(nameof(coneHalfAngleDeg));
        }

        BoundSeconds = Positive(boundSeconds, nameof(boundSeconds));
        AloneSeconds = Positive(aloneSeconds, nameof(aloneSeconds));
        DamageOfWeapon = Positive(damageOfWeapon, nameof(damageOfWeapon));
    }

    public float Reach { get; }
    public float ConeHalfAngleDeg { get; }
    public float BoundSeconds { get; }
    public float AloneSeconds { get; }
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

public sealed class BindingShotHandler : IEffectHandler<BindingShot>, IEffectCastGuard<BindingShot>
{
    private readonly ProjectileSystem _projectiles;
    private readonly EnemySystem _enemies;
    private readonly PlayerStats _stats;
    private readonly CombatBlackboard _blackboard;
    private readonly CharacterSpec _character;
    private readonly SimulatedClock _clock;

    public BindingShotHandler(ProjectileSystem projectiles, EnemySystem enemies, PlayerStats stats,
        CombatBlackboard blackboard, CharacterSpec character, SimulatedClock clock)
    {
        _projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool CanCast(BindingShot effect)
    {
        Require(effect);
        return _character.Weapon.Kind == WeaponKind.Projectile
            && _projectiles.InFlightCount < _projectiles.Capacity && Pick() != null;
    }

    public void Apply(BindingShot effect, object source)
    {
        Require(effect);
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (!CanCast(effect))
        {
            return;
        }

        EnemyAgent target = Pick();
        _projectiles.Fire(new Projectile(_character.Id, 0, _blackboard.PlayerPosition, target.Position,
            _character.Weapon.ShotSpeed, _character.Weapon.ShotRadius,
            effect.DamageOfWeapon * _stats.Resolve(PlayerStat.WeaponDamage).Value,
            ShotSide.AtEnemies, bind: effect), _clock.Now);
    }

    public void Remove(BindingShot effect, object source)
    {
        Require(effect);
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }
    }

    private EnemyAgent Pick()
    {
        float range = _stats.Resolve(PlayerStat.WeaponRange).Value;
        if (!(range > 0f))
        {
            return null;
        }

        EnemyAgent nearest = null;
        float best = range * range;
        ReadOnlySpan<EnemyAgent> agents = _enemies.Registry.Alive;
        for (int i = 0; i < agents.Length; i++)
        {
            EnemyAgent agent = agents[i];
            Vector3 delta = agent.Position - _blackboard.PlayerPosition;
            float distance = delta.X * delta.X + delta.Z * delta.Z;
            if (!agent.IsAlive || !(distance <= range * range))
            {
                continue;
            }

            if (agent.Id == _blackboard.CurrentTargetId)
            {
                return agent;
            }

            if (nearest == null || distance < best || (distance == best && agent.Id < nearest.Id))
            {
                nearest = agent;
                best = distance;
            }
        }

        return nearest;
    }

    private static void Require(BindingShot effect)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }
    }
}
