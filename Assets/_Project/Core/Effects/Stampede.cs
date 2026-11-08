using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Effects;

public sealed class Stampede : IEffect
{
    public Stampede(float knockdownSeconds)
    {
        if (!(knockdownSeconds > 0f) || !float.IsFinite(knockdownSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(knockdownSeconds));
        }

        KnockdownSeconds = knockdownSeconds;
    }

    internal float KnockdownSeconds { get; }
}

internal sealed class StampedeHandler : IEffectHandler<Stampede>, IEffectCastGuard<Stampede>
{
    private readonly StampedeSystem _stampede;
    private readonly EnemySystem _enemies;
    private readonly PlayerCombat _combat;
    private readonly SimulatedClock _clock;
    private readonly EnemyPoint[] _points;

    internal StampedeHandler(StampedeSystem stampede, EnemySystem enemies, PlayerCombat combat, SimulatedClock clock)
    {
        _stampede = stampede;
        _enemies = enemies;
        _combat = combat;
        _clock = clock;
        _points = new EnemyPoint[enemies.Registry.Capacity];
    }

    public bool CanCast(Stampede effect) =>
        !_combat.IsDead && !_stampede.IsRunning
        && _stampede.TryPick(_combat.Blackboard.PlayerPosition, Points(), out _);

    public void Apply(Stampede effect, object source)
    {
        if (!_combat.IsDead && !_stampede.IsRunning)
        {
            _stampede.Start(_combat.Blackboard.PlayerPosition, Points(), _clock.Now, effect.KnockdownSeconds);
        }
    }

    public void Remove(Stampede effect, object source)
    {
    }

    private ReadOnlySpan<EnemyPoint> Points()
    {
        int count = 0;
        foreach (EnemyAgent enemy in _enemies.Registry.Alive)
        {
            if (enemy.IsAlive)
            {
                _points[count++] = new EnemyPoint(enemy.Id, enemy.Position, enemy.Spec.BodyRadius,
                    enemy.Spec.Behaviour == EnemyBehaviourKind.Boss);
            }
        }

        return _points.AsSpan(0, count);
    }
}
