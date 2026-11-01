using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Effects;

public sealed class Killshot : IEffect
{
    public Killshot(float windUp, float halfWidth, float length, float damageOfWeapon,
        float falloffPerHit, float minFactor, float executeBelow)
    {
        WindUp = Positive(windUp, nameof(windUp));
        HalfWidth = Positive(halfWidth, nameof(halfWidth));
        Length = Positive(length, nameof(length));
        DamageOfWeapon = Positive(damageOfWeapon, nameof(damageOfWeapon));
        FalloffPerHit = Fraction(falloffPerHit, nameof(falloffPerHit));
        MinFactor = Fraction(minFactor, nameof(minFactor));
        ExecuteBelow = Fraction(executeBelow, nameof(executeBelow));
    }

    public float WindUp { get; }
    public float HalfWidth { get; }
    public float Length { get; }
    public float DamageOfWeapon { get; }
    public float FalloffPerHit { get; }
    public float MinFactor { get; }
    public float ExecuteBelow { get; }

    private static float Positive(float value, string name) => value > 0f && float.IsFinite(value)
        ? value : throw new ArgumentOutOfRangeException(name);

    private static float Fraction(float value, string name) => value >= 0f && value <= 1f
        ? value : throw new ArgumentOutOfRangeException(name);
}

/// <summary>One pending shot per run. Geometry locks at cast; live bodies are read at release.</summary>
public sealed class KillshotHandler : IEffectHandler<Killshot>, IEffectCastGuard<Killshot>
{
    private readonly EnemySystem _enemies;
    private readonly PlayerCombat _player;
    private readonly PlayerMotor _motor;
    private readonly PlayerStats _stats;
    private readonly SimulatedClock _clock;
    private readonly IDomainEvents _events;
    private readonly EnemyPoint[] _points;
    private readonly EnemyPoint[] _ordered;
    private Killshot _pending;
    private Vector3 _origin;
    private Vector3 _direction;
    private float _firesAt;

    public KillshotHandler(EnemySystem enemies, PlayerCombat player, PlayerMotor motor,
        PlayerStats stats, SimulatedClock clock, IDomainEvents events)
    {
        _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _motor = motor ?? throw new ArgumentNullException(nameof(motor));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _points = new EnemyPoint[enemies.Registry.Capacity];
        _ordered = new EnemyPoint[enemies.Registry.Capacity];
    }

    public bool IsWindingUp => _pending != null;

    public bool CanCast(Killshot effect)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        return !IsWindingUp && !_player.IsDead && !_player.Charge.IsActive
            && KillshotLine.TryPick(Points(), _player.Blackboard.PlayerPosition,
                effect.HalfWidth, effect.Length, out _);
    }

    public void Apply(Killshot effect, object source)
    {
        Require(effect, source);
        if (!CanCast(effect))
        {
            return;
        }

        _origin = _player.Blackboard.PlayerPosition;
        KillshotLine.TryPick(Points(), _origin, effect.HalfWidth, effect.Length, out _direction);
        _pending = effect;
        _firesAt = _clock.Now + effect.WindUp;
        _motor.Hold(effect.WindUp);
        _player.IsHeld = true;
        _events.Publish(new KillshotWindUp(_direction, effect.WindUp));
    }

    public void Remove(Killshot effect, object source) => Require(effect, source);

    public void Clear()
    {
        _pending = null;
        _motor.ClearHold();
        _player.IsHeld = false;
    }

    public void Tick(float now)
    {
        if (!IsWindingUp)
        {
            return;
        }

        if (_player.IsDead)
        {
            Clear();
            return;
        }

        if (now < _firesAt)
        {
            return;
        }

        Killshot effect = _pending;
        Clear();
        int count = KillshotLine.Walk(Points(), _origin, _direction, effect.HalfWidth, effect.Length, _ordered);
        float damage = effect.DamageOfWeapon * (1f + _stats.Resolve(PlayerStat.KillshotDamage).Value)
            * _stats.Resolve(PlayerStat.WeaponDamage).Value;
        float threshold = effect.ExecuteBelow + _stats.Resolve(PlayerStat.KillshotExecute).Value;
        int hits = 0;
        for (int i = 0; i < count; i++)
        {
            if (!_enemies.Registry.TryGet(_ordered[i].Id, out EnemyAgent agent) || !agent.IsAlive)
            {
                continue;
            }

            float factor = MathF.Max(effect.MinFactor, 1f - effect.FalloffPerHit * hits);
            hits++;
            if (!_ordered[i].IsBoss && agent.Health.Fraction <= threshold)
            {
                _events.Publish(new KillshotExecuted(agent.Id));
                _enemies.Execute(agent.Id, now, _player);
            }
            else
            {
                _enemies.ApplyDamage(agent.Id, damage * factor, now, _player);
            }
        }

        _events.Publish(new KillshotFired(_origin, _direction, effect.Length, hits));
    }

    private ReadOnlySpan<EnemyPoint> Points()
    {
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

        return new ReadOnlySpan<EnemyPoint>(_points, 0, count);
    }

    private static void Require(Killshot effect, object source)
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
