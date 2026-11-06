using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Effects;

public sealed class MirrorImage : IEffect
{
    public MirrorImage(float damageOfWeapon)
    {
        if (!(damageOfWeapon > 0f) || !float.IsFinite(damageOfWeapon))
        {
            throw new ArgumentOutOfRangeException(nameof(damageOfWeapon));
        }

        DamageOfWeapon = damageOfWeapon;
    }

    public float DamageOfWeapon { get; }
}

public sealed class MirrorImageHandler : IEffectHandler<MirrorImage>, IEffectCastGuard<MirrorImage>
{
    private readonly ShadeSystem _shades;
    private readonly ProjectileSystem _projectiles;
    private readonly EnemySystem _enemies;
    private readonly PlayerStats _stats;
    private readonly CombatBlackboard _blackboard;
    private readonly CharacterSpec _character;
    private readonly SimulatedClock _clock;

    public MirrorImageHandler(ShadeSystem shades, ProjectileSystem projectiles, EnemySystem enemies,
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

    public bool CanCast(MirrorImage effect)
    {
        _ = effect ?? throw new ArgumentNullException(nameof(effect));
        float range = _stats.Resolve(PlayerStat.WeaponRange).Value;
        float seconds = _stats.MirrorImageSeconds.Value;
        return _character.Weapon.Kind == WeaponKind.Projectile && range > 0f
            && seconds > 0f && float.IsFinite(_clock.Now + seconds)
            && _projectiles.PickRicochetTarget(_enemies, _blackboard.PlayerPosition, range, 0) != null;
    }

    public void Apply(MirrorImage effect, object source)
    {
        _ = source ?? throw new ArgumentNullException(nameof(source));
        if (!CanCast(effect))
        {
            return;
        }

        int count = (int)Math.Clamp(_stats.MirrorImageShades.Value, 1f, 3f);
        float seconds = _stats.MirrorImageSeconds.Value;
        Vector3 player = _blackboard.PlayerPosition;
        _shades.Spawn(ShadeKind.Image, player + (Vector3.UnitX * ShadeSystem.SideOffset),
            _clock.Now, seconds, effect.DamageOfWeapon);
        if (count >= 2)
        {
            _shades.Spawn(ShadeKind.Image, player - (Vector3.UnitX * ShadeSystem.SideOffset),
                _clock.Now, seconds, effect.DamageOfWeapon);
        }

        if (count == 3)
        {
            _shades.Spawn(ShadeKind.Image, player - (Vector3.UnitZ * ShadeSystem.SideOffset),
                _clock.Now, seconds, effect.DamageOfWeapon);
        }
    }

    public void Remove(MirrorImage effect, object source)
    {
        _ = effect ?? throw new ArgumentNullException(nameof(effect));
        _ = source ?? throw new ArgumentNullException(nameof(source));
    }
}
