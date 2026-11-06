using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Effects;

public sealed class Crossfire : IEffect
{
    public const float ShadeSeconds = 1f;

    public Crossfire(float damageOfWeapon)
    {
        if (!(damageOfWeapon > 0f) || !float.IsFinite(damageOfWeapon))
        {
            throw new ArgumentOutOfRangeException(nameof(damageOfWeapon));
        }

        DamageOfWeapon = damageOfWeapon;
    }

    public float DamageOfWeapon { get; }
}

public sealed class CrossfireHandler : IEffectHandler<Crossfire>, IEffectCastGuard<Crossfire>
{
    private readonly ShadeSystem _shades;
    private readonly ProjectileSystem _projectiles;
    private readonly EnemySystem _enemies;
    private readonly PlayerStats _stats;
    private readonly CombatBlackboard _blackboard;
    private readonly CharacterSpec _character;
    private readonly SimulatedClock _clock;

    public CrossfireHandler(ShadeSystem shades, ProjectileSystem projectiles, EnemySystem enemies,
        PlayerStats stats, CombatBlackboard blackboard, CharacterSpec character, SimulatedClock clock)
    {
        _shades = shades ?? throw new ArgumentNullException(nameof(shades));
        _projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool CanCast(Crossfire effect)
    {
        _ = effect ?? throw new ArgumentNullException(nameof(effect));
        return Pick() != null;
    }

    public void Apply(Crossfire effect, object source)
    {
        _ = effect ?? throw new ArgumentNullException(nameof(effect));
        _ = source ?? throw new ArgumentNullException(nameof(source));
        EnemyAgent target = Pick();
        if (target == null)
        {
            return;
        }

        float now = _clock.Now;
        Vector3 player = _blackboard.PlayerPosition;
        float side = ShadeSystem.SideOffset;
        _shades.Spawn(ShadeKind.Crossfire, player + Vector3.UnitX * side, now, Crossfire.ShadeSeconds, 0f);
        _shades.Spawn(ShadeKind.Crossfire, player - Vector3.UnitX * side, now, Crossfire.ShadeSeconds, 0f);
        if (Math.Clamp(_stats.CrossfireShades.Value, 2f, 3f) >= 3f)
        {
            _shades.Spawn(ShadeKind.Crossfire, player - Vector3.UnitZ * side, now, Crossfire.ShadeSeconds, 0f);
        }

        float damage = (effect.DamageOfWeapon + _stats.CrossfireDamage.Value)
            * _stats.Resolve(PlayerStat.WeaponDamage).Value;
        _shades.LooseAll(target, PlayerCombat.ShotDamage(damage), now);
    }

    public void Remove(Crossfire effect, object source)
    {
        _ = effect ?? throw new ArgumentNullException(nameof(effect));
        _ = source ?? throw new ArgumentNullException(nameof(source));
    }

    private EnemyAgent Pick()
    {
        float range = _stats.Resolve(PlayerStat.WeaponRange).Value;
        if (_character.Weapon.Kind != WeaponKind.Projectile || !(range > 0f))
        {
            return null;
        }

        return _projectiles.PickRicochetTarget(_enemies, _blackboard.PlayerPosition, range,
            _blackboard.CurrentTargetId);
    }
}
