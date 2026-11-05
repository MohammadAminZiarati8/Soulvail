using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Effects;

public sealed class RicochetShot : IEffect
{
    public const float BounceReach = 5f;

    public RicochetShot(float damageOfWeapon)
    {
        if (!(damageOfWeapon > 0f) || float.IsInfinity(damageOfWeapon))
        {
            throw new ArgumentOutOfRangeException(nameof(damageOfWeapon));
        }

        DamageOfWeapon = damageOfWeapon;
    }

    public float DamageOfWeapon { get; }
}

public sealed class RicochetShotHandler : IEffectHandler<RicochetShot>, IEffectCastGuard<RicochetShot>
{
    private readonly ProjectileSystem _projectiles;
    private readonly EnemySystem _enemies;
    private readonly PlayerStats _stats;
    private readonly CombatBlackboard _blackboard;
    private readonly CharacterSpec _character;
    private readonly SimulatedClock _clock;

    public RicochetShotHandler(ProjectileSystem projectiles, EnemySystem enemies, PlayerStats stats,
        CombatBlackboard blackboard, CharacterSpec character, SimulatedClock clock)
    {
        _projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool CanCast(RicochetShot effect)
    {
        Require(effect);
        return Pick() != null;
    }

    public void Apply(RicochetShot effect, object source)
    {
        Require(effect, source);
        EnemyAgent target = Pick();
        if (target == null)
        {
            return;
        }

        WeaponSpec weapon = _character.Weapon;
        Vector3 origin = _blackboard.PlayerPosition;
        Vector3 aim = ProjectileLead.Solve(origin, target.Position, target.Velocity, weapon.ShotSpeed);
        float damage = effect.DamageOfWeapon * _stats.Resolve(PlayerStat.WeaponDamage).Value
            * (1f + _stats.RicochetDamage.Value) * _stats.HitAndRun.DamageMultiplier(_clock.Now);
        damage = float.IsInfinity(damage) || !(damage > 0f) ? 0f : damage;
        int hits = (int)Math.Clamp(_stats.RicochetHits.Value, 1f, ProjectileSystem.MaxRicochetHits);
        _projectiles.FireRicochet(new Projectile(_character.Id, 0, origin, aim, weapon.ShotSpeed,
            weapon.ShotRadius, damage, ShotSide.AtEnemies), hits, _clock.Now);
    }

    public void Remove(RicochetShot effect, object source)
    {
        Require(effect, source);
    }

    private static void Require(RicochetShot effect, object source)
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
        if (_character.Weapon.Kind != WeaponKind.Projectile || !(range > 0f)
            || _projectiles.InFlightCount >= _projectiles.Capacity)
        {
            return null;
        }

        return _projectiles.PickRicochetTarget(_enemies, _blackboard.PlayerPosition, range,
            _blackboard.CurrentTargetId);
    }

    private static void Require(RicochetShot effect)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }
    }
}
