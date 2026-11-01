using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Events;

/// <summary>
/// An enemy's status started, lengthened or ended. <see cref="Seconds"/> is what remains, 0 for ended.
/// Death ends every status silently: nothing follows an <c>EnemyDied</c>.
/// </summary>
public readonly struct EnemyStatusChanged
{
    /// <exception cref="ArgumentException"><paramref name="rule"/> is the default rule.</exception>
    public EnemyStatusChanged(int enemyId, StatusRule rule, float seconds, int stacks)
    {
        if (rule.Id.Value is null)
        {
            throw new ArgumentException("A status event needs a rule.", nameof(rule));
        }

        EnemyId = enemyId;
        Rule = rule;
        Seconds = seconds;
        Stacks = stacks;
    }

    public int EnemyId { get; }

    public StatusRule Rule { get; }

    public ContentId Status => Rule.Id;

    /// <summary>0 = ended.</summary>
    public float Seconds { get; }

    /// <summary>1 for a status that does not stack.</summary>
    public int Stacks { get; }
}
