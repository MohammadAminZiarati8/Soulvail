using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

/// <summary>
/// An <see cref="IStatBlock"/> over an <see cref="EnemyAgent"/>: the three numbers an enemy body
/// actually carries, addressed the same way the player's eleven are.
/// </summary>
/// <remarks>
/// <para>
/// <b>The second implementation, and the reason the first is an interface.</b> A boss buffing its
/// own move speed and a node buffing the player's are the same operation aimed differently — one
/// <see cref="ModifyStat"/>, one <see cref="ModifyStatHandler"/>, one modifier stack — and that is
/// only true if the thing being aimed at is a seam rather than a class.
/// </para>
/// <para>
/// <b>Three addresses, and every other one is refused.</b> <see cref="PlayerStat.MaxHp"/>,
/// <see cref="PlayerStat.MoveSpeed"/> and <see cref="PlayerStat.ContactDamage"/> are the three
/// <c>Stat</c>s an <see cref="EnemyAgent"/> holds. <see cref="PlayerStat.WeaponRange"/> on a Husk
/// is a question with no answer, and the answer is a throw naming both the address and the spec id
/// — <b>never a silently created <c>Stat</c></b>, which would be a modifier landing on a number
/// nothing reads (Traps §1). Two of the three overlap with the player's set and one does not, which
/// is the whole of what the shared address space costs.
/// </para>
/// <para>
/// <b>It holds the agent, not the stats.</b> <see cref="EnemyRegistry"/> recycles an agent and
/// <c>EnemyAgent.Initialise</c> wipes all three stats, but the <c>Stat</c> <em>instances</em> are
/// built once in the constructor and survive — so a block built over an agent keeps naming the
/// right numbers across a recycle. What it does <em>not</em> survive is meaning: the agent it
/// names may come back as a different archetype, which is why nothing caches one of these across a
/// despawn.
/// </para>
/// <para>
/// Allocates nothing on <see cref="Resolve"/> or <see cref="Has"/>: a jump table over an enum
/// returning a reference the agent already holds.
/// </para>
/// </remarks>
public sealed class CombatantStats : IStatBlock
{
    private readonly EnemyAgent _agent;

    /// <param name="agent">The body whose numbers this addresses.</param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is null.</exception>
    public CombatantStats(EnemyAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">
    /// This agent has no stat at that address — see the remarks on this class. The message names
    /// both the address and the archetype, because <em>"WeaponRange is not a thing a Husk has"</em>
    /// is a content error and a content error has to say which content.
    /// </exception>
    public Stat Resolve(PlayerStat stat)
    {
        return stat switch
        {
            PlayerStat.MaxHp => _agent.Health.MaxHp,
            PlayerStat.MoveSpeed => _agent.MoveSpeed,
            PlayerStat.ContactDamage => _agent.ContactDamage,
            _ => throw new ArgumentOutOfRangeException(
                nameof(stat),
                stat,
                $"'{_agent.Spec.Id}' has no stat at address {stat}. An enemy body carries "
                    + $"{PlayerStat.MaxHp}, {PlayerStat.MoveSpeed} and {PlayerStat.ContactDamage} "
                    + "and nothing else — the rest are player numbers, and minting one here would "
                    + "put a modifier on a number nothing reads."),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Written as its own switch rather than as a try/catch around <see cref="Resolve"/> or as an
    /// <c>Enum.IsDefined</c>: the first turns a contract into control flow, and the second would
    /// answer <see langword="true"/> for every member of the enum, which is exactly the wrong
    /// answer for eight of them. A row walks every member and asserts the two agree.
    /// </remarks>
    public bool Has(PlayerStat stat)
    {
        return stat is PlayerStat.MaxHp or PlayerStat.MoveSpeed or PlayerStat.ContactDamage;
    }
}
