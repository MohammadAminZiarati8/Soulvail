using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

public sealed class StampedeSystem
{
    public const float BoarSpeed = 14f;
    public const float Spacing = 0.15f;
    public const float StartBehind = 2f;
    public const float Length = 20f;

    private readonly IDomainEvents _events;
    private readonly PlayerStats _stats;
    private readonly (int Id, int Boars)[] _hits;
    private int _tracked;
    private int _boars;
    private Vector3 _from;
    private Vector3 _direction;
    private float _halfWidth;
    private float _knockdownSeconds;
    private float _startedAt;
    private float _endsAt;
    private float _lastTick;

    public StampedeSystem(IDomainEvents events, PlayerStats stats)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _hits = new (int, int)[stats.EnemyCapacity];
    }

    public bool IsRunning => _boars != 0;

    internal bool TryPick(Vector3 player, ReadOnlySpan<EnemyPoint> enemies, out Vector3 direction) =>
        KillshotLine.TryPick(enemies, player, _stats.StampedeHalfWidth.Value, Length, out direction);

    public bool Start(Vector3 player, ReadOnlySpan<EnemyPoint> enemies, float now, float knockdownSeconds)
    {
        if (IsRunning || !TryPick(player, enemies, out Vector3 direction))
        {
            return false;
        }

        _from = player - direction * StartBehind;
        _direction = direction;
        _halfWidth = _stats.StampedeHalfWidth.Value;
        _knockdownSeconds = knockdownSeconds;
        _startedAt = now;
        _lastTick = now;
        _boars = (int)Math.Clamp(_stats.StampedeBoars.Value, 1f, 12f);
        _endsAt = now + (_boars - 1) * Spacing + Length / BoarSpeed;
        _events.Publish(new StampedeStarted(_from, _direction, Length, _boars, _halfWidth));
        return true;
    }

    public void Tick(float now, EnemySystem enemies, PlayerCombat combat)
    {
        if (!IsRunning)
        {
            return;
        }

        bool ended = now >= _endsAt;
        float damage = _stats.StampedeDamage.Value * combat.Weapon.Damage.Value;
        int prior = 0;
        int tracked = 0;
        // Registry order is increasing id. Merge the old masks forward in place, dropping
        // dead/despawned ids and appending new ids, even when an agent has been recycled.
        foreach (EnemyAgent enemy in enemies.Registry.Alive)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            int id = enemy.Id;
            while (prior < _tracked && _hits[prior].Id < id)
            {
                prior++;
            }

            int hitBoars = prior < _tracked && _hits[prior].Id == id ? _hits[prior++].Boars : 0;
            if (KillshotLine.Contains(enemy.Position, _from, _direction,
                _halfWidth + enemy.Spec.BodyRadius, Length, out float along))
            {
                for (int boar = 0; boar < _boars && enemy.IsAlive; boar++)
                {
                    float arrival = _startedAt + boar * Spacing + along / BoarSpeed;
                    int bit = 1 << boar;
                    if ((hitBoars & bit) != 0 || arrival < _lastTick || (!ended && arrival > now))
                    {
                        continue;
                    }

                    hitBoars |= bit;
                    float amount = enemy.Spec.Behaviour == EnemyBehaviourKind.Boss ? damage * 0.5f : damage;
                    DamageResult result = enemies.ApplyGuardPiercingDamage(id, amount, now, combat);
                    if (!result.Blocked && _knockdownSeconds > 0f && enemies.IsLight(id))
                    {
                        enemies.Stun(id, _knockdownSeconds);
                    }

                    _events.Publish(new BoarHit(id, result.Applied));
                }
            }

            _hits[tracked++] = (id, hitBoars);
        }

        _tracked = tracked;
        _lastTick = now;
        if (ended)
        {
            Clear();
            _events.Publish(new StampedeEnded());
        }
    }

    public void Clear()
    {
        _boars = 0;
        _tracked = 0;
    }
}
