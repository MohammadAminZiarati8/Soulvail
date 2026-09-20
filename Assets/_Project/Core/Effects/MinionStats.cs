using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

/// <summary>
/// An <see cref="IStatBlock"/> over a <see cref="MinionAgent"/>: the three numbers a Wight's body
/// actually carries, addressed the same way the player's eleven are.
/// </summary>
/// <remarks>
/// <para>
/// <b>The third implementation, and deliberately not a generalisation of the first two</b>
/// (M5-04b rule 6). A shared <c>IHasCombatStats</c> on both agents, or a base class, would remove
/// about ten lines and cost three things. It would put a member on <see cref="EnemyAgent"/> for the
/// benefit of a type an enemy must not know about (M5-04a rule 1). It would make one refusal message
/// serve two very different content errors, where M4-01a rule 2 requires the throw to <em>name the
/// content</em> — <c>"'enemy.husk' has no stat at address WeaponRange"</c> and <c>"'minion.wight'
/// has no stat at address WeaponRange"</c> are different facts for different authors. And it would
/// generalise from two cases to a third, which is one case too early. Three small honest switches
/// beat one clever one.
/// </para>
/// <para>
/// <b>The same three addresses <see cref="CombatantStats"/> answers, and every other one refused.</b>
/// <see cref="PlayerStat.MaxHp"/>, <see cref="PlayerStat.MoveSpeed"/> and
/// <see cref="PlayerStat.ContactDamage"/> are the three <c>Stat</c>s a <see cref="MinionAgent"/>
/// holds (M5-04a rule 2). <see cref="PlayerStat.WeaponRange"/> on a Wight is a question with no
/// answer, and the answer is a throw naming both the address and <c>minion.wight</c> — <b>never a
/// silently created <c>Stat</c></b>, which would be a modifier landing on a number nothing reads
/// (Traps §1).
/// </para>
/// <para>
/// <b><see cref="PlayerStat"/> gains nothing for this, and that is the evidence M4-01a rule 1 was
/// about the design</b> (rule 8). The shared address space was argued there against a parallel
/// <c>EnemyStat</c>; a Wight is the second caller that is not the player and it needs no new member.
/// </para>
/// <para>
/// <b>What a node still cannot do</b> (rule 9). <see cref="ModifyStat"/> carries
/// <see cref="StatTarget"/>'s <c>Player</c> and <c>Self</c>, and
/// <see cref="ModifyStatHandler.Aiming"/> aims one call at one block — neither spells <em>"every
/// Wight I own"</em>, which is exactly what a Legion node saying <em>"+20 % minion damage"</em> is.
/// M4-01a rule 3 rules that an effect aimed at <em>someone else</em> is a different primitive with a
/// selection rule, so inventing a third <see cref="StatTarget"/> member with one authored node
/// behind it would be guessing at the shape. What ships here is that a Wight <em>has</em> an address
/// book and a live <see cref="PlayerStat.ContactDamage"/> a modifier can sit on; who may put one
/// there is M5-06's first question.
/// </para>
/// <para>
/// <b>It holds the agent, not the stats</b> — <see cref="CombatantStats"/>' sentence, and true for
/// the same reason one class over: <c>MinionSystem</c> recycles its bodies and
/// <c>MinionAgent.Initialise</c> wipes all three stacks, but the <c>Stat</c> <em>instances</em> are
/// built once in the agent's constructor and survive, so a block built over an agent keeps naming
/// the right numbers across a recycle. What it does not survive is identity: the body it names comes
/// back with a different id, which is why nothing caches one of these across an expiry.
/// </para>
/// <para>
/// Allocates nothing on <see cref="Resolve"/> or <see cref="Has"/>: a jump table over an enum
/// returning a reference the agent already holds.
/// </para>
/// </remarks>
public sealed class MinionStats : IStatBlock
{
    private readonly MinionAgent _agent;

    /// <param name="agent">The Wight whose numbers this addresses.</param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is null.</exception>
    public MinionStats(MinionAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">
    /// This Wight has no stat at that address — see the remarks on this class. The message names
    /// both the address and the minion, because <em>"WeaponRange is not a thing a Wight has"</em> is
    /// a content error and a content error has to say which content.
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
                $"'{_agent.Spec.SpecId}' has no stat at address {stat}. A minion's body carries "
                    + $"{PlayerStat.MaxHp}, {PlayerStat.MoveSpeed} and {PlayerStat.ContactDamage} "
                    + "and nothing else — the rest are player numbers, and minting one here would "
                    + "put a modifier on a number nothing reads."),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Written as its own switch rather than as a try/catch around <see cref="Resolve"/> or as an
    /// <c>Enum.IsDefined</c>, for the reason <see cref="CombatantStats.Has"/> gives: the first turns
    /// a contract into control flow, and the second would answer <see langword="true"/> for every
    /// member of the enum, which is exactly the wrong answer for nine of them. A row walks every
    /// member and asserts the two agree.
    /// </remarks>
    public bool Has(PlayerStat stat)
    {
        return stat is PlayerStat.MaxHp or PlayerStat.MoveSpeed or PlayerStat.ContactDamage;
    }
}
