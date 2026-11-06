using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Effects;

public sealed class HallOfMirrors : IEffect
{
    public const float Radius = 3f;

    public HallOfMirrors(float damageOfWeapon)
    {
        if (!(damageOfWeapon > 0f) || !float.IsFinite(damageOfWeapon))
        {
            throw new ArgumentOutOfRangeException(nameof(damageOfWeapon));
        }

        DamageOfWeapon = damageOfWeapon;
    }

    public float DamageOfWeapon { get; }
}

public sealed class HallOfMirrorsHandler : IEffectHandler<HallOfMirrors>, IEffectCastGuard<HallOfMirrors>
{
    private readonly ShadeSystem _shades;
    private readonly ProjectileSystem _projectiles;
    private readonly EnemySystem _enemies;
    private readonly PlayerStats _stats;
    private readonly CombatBlackboard _blackboard;
    private readonly CharacterSpec _character;
    private readonly SimulatedClock _clock;
    private readonly IDomainEvents _events;

    public HallOfMirrorsHandler(ShadeSystem shades, ProjectileSystem projectiles, EnemySystem enemies,
        PlayerStats stats, CombatBlackboard blackboard, CharacterSpec character, SimulatedClock clock,
        IDomainEvents events)
    {
        _shades = shades ?? throw new ArgumentNullException(nameof(shades));
        _projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public bool CanCast(HallOfMirrors effect)
    {
        _ = effect ?? throw new ArgumentNullException(nameof(effect));
        float range = _stats.Resolve(PlayerStat.WeaponRange).Value;
        float seconds = _stats.HallSeconds.Value;
        float damage = effect.DamageOfWeapon + _stats.HallDamage.Value;
        return _character.Weapon.Kind == WeaponKind.Projectile && range > 0f
            && seconds > 0f && float.IsFinite(_clock.Now + seconds)
            && damage >= 0f && float.IsFinite(damage)
            && _projectiles.PickRicochetTarget(_enemies, _blackboard.PlayerPosition, range, 0) != null;
    }

    public void Apply(HallOfMirrors effect, object source)
    {
        _ = source ?? throw new ArgumentNullException(nameof(source));
        if (!CanCast(effect))
        {
            return;
        }

        int count = (int)Math.Clamp(_stats.HallShades.Value, 4f, 6f);
        float seconds = _stats.HallSeconds.Value;
        float damage = effect.DamageOfWeapon + _stats.HallDamage.Value;
        float now = _clock.Now;
        Vector3 center = _blackboard.PlayerPosition;
        float step = (2f * MathF.PI) / count;

        _events.Publish(new HallRaised(center, HallOfMirrors.Radius, seconds));
        for (int i = 0; i < count; i++)
        {
            float angle = i * step;
            Vector3 offset = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * HallOfMirrors.Radius;
            _shades.Spawn(ShadeKind.Hall, center + offset, now, seconds, damage);
        }
    }

    public void Remove(HallOfMirrors effect, object source)
    {
        _ = effect ?? throw new ArgumentNullException(nameof(effect));
        _ = source ?? throw new ArgumentNullException(nameof(source));
    }
}
