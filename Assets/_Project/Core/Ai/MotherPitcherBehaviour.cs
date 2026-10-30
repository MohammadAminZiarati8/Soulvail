using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>A rooted boss whose living buds reduce every incoming hit to a fifth.</summary>
public sealed class MotherPitcherBehaviour : IEnemyBehaviour
{
    public const float ShieldedDamageTaken = 0.2f;

    private readonly EnemyAgent _agent;
    private readonly ContentId _budId;

    // The remaining arguments are the attack dependencies used by JG-12c.
    public MotherPitcherBehaviour(EnemyAgent agent, ProjectileSystem projectiles,
        IRandomStream random, ContentId budId, ContentId frogId)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _budId = budId;
    }

    public bool IsShielded { get; private set; }

    public void Tick(in EnemyTickContext ctx)
    {
        bool shielded = false;
        ReadOnlySpan<EnemyAgent> agents = ctx.Enemies.Registry.Alive;

        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i].IsAlive && agents[i].Spec.Id == _budId)
            {
                shielded = true;
                break;
            }
        }

        if (shielded != IsShielded)
        {
            _agent.DamageTaken.RemoveAll(this);
            if (shielded)
            {
                _agent.DamageTaken.Add(new Modifier(
                    ModifierKind.PercentMult, ShieldedDamageTaken - 1f, this));
            }

            IsShielded = shielded;
        }

        ctx.Intents.EnemyMove(new EnemyMoveIntent(
            _agent.Id, Vector3.Zero, _agent.Blackboard.DirectionToPlayer));
    }

    public void Reset()
    {
        _agent.DamageTaken.RemoveAll(this);
        IsShielded = false;
    }
}
