using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

public enum ShadeKind { Image, Decoy, Crossfire, Hall }

public readonly struct Shade
{
    internal Shade(int id, ShadeKind kind, Vector3 position, float expiresAt, float damageOfWeapon,
        float nextShotAt, int lureId = LureSystem.NoLure)
    {
        Id = id;
        Kind = kind;
        Position = position;
        ExpiresAt = expiresAt;
        DamageOfWeapon = damageOfWeapon;
        NextShotAt = nextShotAt;
        LureId = lureId;
    }

    public int Id { get; }
    public ShadeKind Kind { get; }
    public Vector3 Position { get; }
    public float ExpiresAt { get; }
    public float DamageOfWeapon { get; }
    public float NextShotAt { get; }
    internal int LureId { get; }
}

public sealed class ShadeSystem
{
    public const int Capacity = 8;
    public const float SideOffset = 1.2f;
    public const float BurstRadius = 3f;
    public const float BurstSlowSeconds = 2f;

    private readonly ProjectileSystem _projectiles;
    private readonly LureSystem _lures;
    private readonly CharacterSpec _character;
    private readonly IDomainEvents _events;
    private readonly Stat _range;
    private readonly Stat _fireRate;
    private readonly Stat _damage;
    private readonly Stat _fadeBurst;
    private readonly Stat _fadeBurstSlow;
    private readonly Shade[] _standing = new Shade[Capacity];
    private int _count;
    private int _nextId = 1;

    public ShadeSystem(ProjectileSystem projectiles, LureSystem lures, PlayerStats stats,
        CharacterSpec character, IDomainEvents events)
    {
        _projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        _lures = lures ?? throw new ArgumentNullException(nameof(lures));
        _ = stats ?? throw new ArgumentNullException(nameof(stats));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _range = stats.Resolve(PlayerStat.WeaponRange);
        _fireRate = stats.Resolve(PlayerStat.FireRate);
        _damage = stats.Resolve(PlayerStat.WeaponDamage);
        _fadeBurst = stats.Resolve(PlayerStat.FadeBurst);
        _fadeBurstSlow = stats.Resolve(PlayerStat.FadeBurstSlow);
    }

    public ReadOnlySpan<Shade> Standing => _standing.AsSpan(0, _count);

    public int Spawn(ShadeKind kind, Vector3 position, float now, float seconds, float damageOfWeapon)
    {
        if (kind is not (ShadeKind.Image or ShadeKind.Decoy or ShadeKind.Crossfire or ShadeKind.Hall))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (!float.IsFinite(now))
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        float expiresAt = now + seconds;
        if (!(seconds > 0f) || !float.IsFinite(expiresAt))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        if (!(damageOfWeapon >= 0f) || !float.IsFinite(damageOfWeapon))
        {
            throw new ArgumentOutOfRangeException(nameof(damageOfWeapon));
        }

        if (_count == Capacity)
        {
            Retire(0, expired: false);
        }

        int id = _nextId++;
        int lureId = kind == ShadeKind.Decoy ? _lures.Drop(position, now, seconds) : LureSystem.NoLure;
        _standing[_count++] = new Shade(id, kind, position, expiresAt, damageOfWeapon, now, lureId);
        _events.Publish(new ShadeAppeared(id, kind, position, expiresAt));
        return id;
    }

    public void Tick(float now, EnemySystem enemies, PlayerCombat player)
    {
        if (!float.IsFinite(now))
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        int index = 0;
        while (index < _count)
        {
            Shade shade = _standing[index];
            if (now >= shade.ExpiresAt)
            {
                Retire(index, expired: true);
                float multiplier = _fadeBurst.Value;
                if (multiplier > 0f)
                {
                    Burst(shade.Position, now, multiplier, enemies, player);
                }

                continue;
            }

            if (now >= shade.NextShotAt && shade.Kind is (ShadeKind.Image or ShadeKind.Hall))
            {
                TryShoot(index, shade, now, enemies);
            }

            index++;
        }
    }

    public void Clear()
    {
        while (_count > 0)
        {
            int lureId = _standing[0].LureId;
            Retire(0, expired: false);

            // Clearing the field ends the lure. Replacing a shade leaves its lure on its timer.
            _lures.Expire(lureId);
        }
    }

    private void Burst(Vector3 centre, float now, float multiplier, EnemySystem enemies, PlayerCombat player)
    {
        float damage = PlayerCombat.ShotDamage(multiplier * _damage.Value);
        float slow = MathF.Min(_fadeBurstSlow.Value, CrowdControl.MaxSlow);
        bool slows = slow > 0f;

        // ApplyDamage leaves corpses registered, so kills do not invalidate the span.
        foreach (EnemyAgent agent in enemies.Registry.Alive)
        {
            if (!agent.IsAlive)
            {
                continue;
            }

            float dx = agent.Position.X - centre.X;
            float dz = agent.Position.Z - centre.Z;
            float reach = BurstRadius + agent.Spec.BodyRadius;
            if (!((dx * dx) + (dz * dz) <= reach * reach))
            {
                continue;
            }

            DamageResult hit = enemies.ApplyDamage(agent.Id, damage, now, player);
            if (slows && !hit.Blocked && !hit.Killed)
            {
                enemies.Slow(agent.Id, slow, BurstSlowSeconds, allowHalfDurationOnBoss: true);
            }
        }

        _events.Publish(new ShadeBurst(centre, BurstRadius, slows));
    }

    private void TryShoot(int index, Shade shade, float now, EnemySystem enemies)
    {
        WeaponSpec weapon = _character.Weapon;
        float range = _range.Value;
        float fireRate = _fireRate.Value;
        if (weapon.Kind != WeaponKind.Projectile || !(range > 0f) || !(fireRate > 0f)
            || _projectiles.InFlightCount >= _projectiles.Capacity)
        {
            return;
        }

        EnemyAgent target = _projectiles.PickRicochetTarget(enemies, shade.Position, range, 0, fromShade: true);
        if (target == null)
        {
            return;
        }

        Vector3 aim = ProjectileLead.Solve(shade.Position, target.Position, target.Velocity, weapon.ShotSpeed);
        float damage = PlayerCombat.ShotDamage(shade.DamageOfWeapon * _damage.Value);
        var shot = new Projectile(_character.Id, 0, shade.Position, aim, weapon.ShotSpeed, weapon.ShotRadius,
            damage, ShotSide.AtEnemies, shotOrigin: ShotOrigin.Shade);
        if (_projectiles.Fire(shot, now) != ProjectileSystem.NoProjectile)
        {
            _standing[index] = new Shade(shade.Id, shade.Kind, shade.Position, shade.ExpiresAt,
                shade.DamageOfWeapon, now + (1f / fireRate));
            _events.Publish(new ShadeLoosed(shade.Id, aim));
        }
    }

    private void Retire(int index, bool expired)
    {
        Shade shade = _standing[index];
        _count--;
        Array.Copy(_standing, index + 1, _standing, index, _count - index);
        _events.Publish(new ShadeFaded(shade.Id, shade.Position, expired));
    }
}
