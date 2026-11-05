using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Effects;

public sealed class Whirlwind : IEffect
{
    public const float RingGap = 0.3f;

    public Whirlwind(float damageOfWeapon)
    {
        if (!(damageOfWeapon > 0f) || float.IsInfinity(damageOfWeapon))
        {
            throw new ArgumentOutOfRangeException(nameof(damageOfWeapon));
        }

        DamageOfWeapon = damageOfWeapon;
    }

    public float DamageOfWeapon { get; }
}

public sealed class WhirlwindHandler : IEffectHandler<Whirlwind>
{
    private const int MaxRings = 3;

    private readonly ProjectileSystem _projectiles;
    private readonly PlayerCombat _combat;
    private readonly PlayerMotor _motor;
    private readonly PlayerStats _stats;
    private readonly CharacterSpec _character;
    private readonly SimulatedClock _clock;
    private readonly IDomainEvents _events;
    private readonly (float Due, int Arrows, float Angle, float DamageOfWeapon)[] _pending =
        new (float, int, float, float)[MaxRings];
    private int _pendingCount;

    public WhirlwindHandler(ProjectileSystem projectiles, PlayerCombat combat, PlayerMotor motor,
        PlayerStats stats, CharacterSpec character, SimulatedClock clock, IDomainEvents events)
    {
        _projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _motor = motor ?? throw new ArgumentNullException(nameof(motor));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public void Apply(Whirlwind effect, object source)
    {
        Require(effect, source);
        if (_combat.IsDead || _character.Weapon.Kind != WeaponKind.Projectile)
        {
            return;
        }

        float count = _stats.WhirlwindArrows.Value;
        int arrows = !(count > 0f) ? 0 : count >= int.MaxValue ? int.MaxValue : (int)count;
        float ringCount = _stats.WhirlwindRings.Value;
        int rings = ringCount >= 1f ? (int)Math.Min(ringCount, MaxRings) : 1;
        Vector3 origin = _combat.Blackboard.PlayerPosition;
        Vector3 facing = _motor.Facing;
        float angle = MathF.Atan2(facing.X, facing.Z);
        float now = _clock.Now;

        for (int ring = 1; ring < rings && _pendingCount < _pending.Length; ring++)
        {
            float offset = MathF.PI * ring / Math.Max(1, arrows);
            _pending[_pendingCount++] = (now + (ring * Whirlwind.RingGap), arrows, angle + offset,
                effect.DamageOfWeapon);
        }

        _events.Publish(new WhirlwindCast(origin, arrows, rings, Whirlwind.RingGap));
        FireRing(now, origin, arrows, angle, effect.DamageOfWeapon);
    }

    public void Tick(float now, Vector3 playerPosition)
    {
        if (_combat.IsDead)
        {
            Clear();
            return;
        }

        int remaining = 0;
        for (int i = 0; i < _pendingCount; i++)
        {
            var ring = _pending[i];
            if (now >= ring.Due)
            {
                FireRing(now, playerPosition, ring.Arrows, ring.Angle, ring.DamageOfWeapon);
            }
            else
            {
                _pending[remaining++] = ring;
            }
        }

        _pendingCount = remaining;
    }

    public void Remove(Whirlwind effect, object source) => Require(effect, source);

    internal void Clear() => _pendingCount = 0;

    private void FireRing(float now, Vector3 origin, int arrows, float angle, float damageOfWeapon)
    {
        float range = _combat.Weapon.Range.Value;
        if (!(range > 0f) || float.IsInfinity(range) || arrows == 0)
        {
            return;
        }

        WeaponSpec weapon = _character.Weapon;
        float step = (2f * MathF.PI) / arrows;
        float damage = damageOfWeapon * _combat.Weapon.Damage.Value * _combat.HitAndRun.DamageMultiplier(now);
        float farMultiplier = 1f + _combat.EagleEye.FarBonus.Value;
        for (int arrow = 0; arrow < arrows && _projectiles.InFlightCount < _projectiles.Capacity; arrow++)
        {
            float heading = angle + (arrow * step);
            var direction = new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading));
            float aimedDamage = PlayerCombat.ShotDamage(damage * _combat.ArrowAimMultiplier(out bool isCrit));
            _projectiles.FireWhirlwind(new Projectile(_character.Id, 0, origin, origin + (direction * range),
                weapon.ShotSpeed, weapon.ShotRadius, aimedDamage, ShotSide.AtEnemies, isCrit: isCrit),
                now, farMultiplier);
        }
    }

    private static void Require(Whirlwind effect, object source)
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
