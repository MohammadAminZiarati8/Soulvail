using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Effects;

// What a Wight is *born with*, and the address book over it. One file for the pair, `ModifyStat.cs`'
// shape and its reason: the recipe and the block that addresses it say half of what they mean apart.
// See CH §3.2, AR §10.1, ADR-0008 and M5-06a rules 1–4.

/// <summary>
/// What the next Wight is born with. Three run-level <see cref="Stat"/>s, seeded from a
/// <see cref="MinionSpec"/> and read by <c>MinionSystem.Spawn</c> — the numbers a Legion node moves,
/// one place rather than once per body.
/// </summary>
/// <remarks>
/// <para>
/// <b>The object that makes <see cref="StatTarget.Minions"/> one line rather than a selection rule</b>
/// (rule 1). M4-01a rule 3 says an effect aimed at <em>someone else</em> is a different primitive
/// with a selection rule — which target, chosen how, what happens when there is none. A recipe
/// escapes that because it is not a someone: there is exactly one per run, it exists for the run's
/// whole life, and it is found without choosing anything.
/// </para>
/// <para>
/// <b>A node changes what being born means, and a Wight already standing does not change</b>
/// (rule 3). That is the whole cost of aiming here, and it is stated rather than discovered: a node
/// taken while three Wights are up buffs the <em>fourth</em>. The lag is bounded by CH §3.2's own
/// twenty seconds, because every Wight in the arena is replaced within a lifespan. The alternative
/// is worse in kind rather than in degree — walking the live agents and adding a
/// <see cref="Modifier"/> to each would put a modifier stack on a body that is about to be recycled,
/// need a removal path for a node nothing removes (CH §7 deleted respec), and leave open what
/// happens to a Wight raised <em>after</em> the walk.
/// </para>
/// <para>
/// <b>It is seeded from a <see cref="MinionSpec"/> and the spec is untouched</b> (rule 4). The spec
/// stays the shared immutable authored data every other spec is (AR §10.1), so two runs of the same
/// class hold two recipes with independent stacks. <see cref="MinionSpec.Reach"/>,
/// <see cref="MinionSpec.AttackInterval"/>, <see cref="MinionSpec.Cap"/> and
/// <see cref="MinionSpec.Lifespan"/> are deliberately <em>not</em> here: the first two are not
/// <see cref="Stat"/>s on an agent either (M5-04a), and the last two are the two addresses this task
/// refuses — they are The Host's and Legion's Keystone-tier numbers, v1 ships no Keystone, and
/// inventing two <see cref="PlayerStat"/> members for content nobody authors is guessing at M7-04's
/// shape.
/// </para>
/// </remarks>
public sealed class MinionRecipe
{
    /// <param name="spec">The class's minions — the run's one <c>minion.wight</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    /// <remarks>
    /// Nothing is validated here beyond the null: <see cref="MinionSpec"/>'s constructor already
    /// refuses a non-positive or non-finite number at the door where it was <em>authored</em>, and a
    /// second opinion on the same three values would be a second account of what a legal minion is.
    /// </remarks>
    public MinionRecipe(MinionSpec spec)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        SpecId = spec.SpecId;

        MaxHp = new Stat(spec.MaxHp);
        MoveSpeed = new Stat(spec.MoveSpeed);
        ContactDamage = new Stat(spec.Damage);
    }

    /// <summary>
    /// Which minion this is the recipe for — <c>minion.wight</c>. Carried so that
    /// <see cref="MinionRecipeStats"/>' refusal can name the content, which is M4-01a rule 2.
    /// </summary>
    public ContentId SpecId { get; }

    /// <summary>The hit points the next Wight stands up with.</summary>
    public Stat MaxHp { get; }

    /// <summary>How fast the next Wight walks, in metres per second.</summary>
    public Stat MoveSpeed { get; }

    /// <summary>
    /// What one of the next Wight's strikes costs an enemy. Where CH §3.2's <em>"+20 % minion
    /// damage"</em> lands.
    /// </summary>
    public Stat ContactDamage { get; }
}

/// <summary>
/// An <see cref="IStatBlock"/> over a <see cref="MinionRecipe"/>: the three numbers a Wight is born
/// with, addressed the same way the player's eleven are.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fourth implementation, and deliberately not a generalisation of the first three</b>
/// (rule 2, M5-04b rule 6 unchanged). <see cref="MinionStats"/> is the mirror of this one level down,
/// and folding the two together would make one refusal message serve two different content errors:
/// <c>"the minion recipe has no stat at address WeaponRange"</c> is a fact about a run's numbers and
/// <c>"'minion.wight' has no stat at address WeaponRange"</c> is a fact about a body. M4-01a rule 2
/// requires the throw to name <em>which</em> content is wrong, and those are different which-es.
/// </para>
/// <para>
/// <b>The same three addresses <see cref="MinionStats"/> answers, and every other one refused.</b>
/// A silently minted <see cref="Stat"/> would be a modifier landing on a number nothing reads
/// (Traps §1), which on this block is worse than on the body's: a node buying nothing would look
/// exactly like a node whose Wights have not been raised yet.
/// </para>
/// <para>
/// Allocates nothing on <see cref="Resolve"/> or <see cref="Has"/>: a jump table over an enum
/// returning a reference the recipe already holds.
/// </para>
/// </remarks>
public sealed class MinionRecipeStats : IStatBlock
{
    private readonly MinionRecipe _recipe;

    /// <param name="recipe">The recipe whose numbers this addresses.</param>
    /// <exception cref="ArgumentNullException"><paramref name="recipe"/> is null.</exception>
    public MinionRecipeStats(MinionRecipe recipe)
    {
        _recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The recipe has no stat at that address — see the remarks on this class. The message names the
    /// address and says it is the <em>recipe</em> rather than a body, because those are the two
    /// places a minion number can live and a designer has to be told which one refused.
    /// </exception>
    public Stat Resolve(PlayerStat stat)
    {
        return stat switch
        {
            PlayerStat.MaxHp => _recipe.MaxHp,
            PlayerStat.MoveSpeed => _recipe.MoveSpeed,
            PlayerStat.ContactDamage => _recipe.ContactDamage,
            _ => throw new ArgumentOutOfRangeException(
                nameof(stat),
                stat,
                $"The minion recipe for '{_recipe.SpecId}' has no stat at address {stat}. A recipe "
                    + $"carries {PlayerStat.MaxHp}, {PlayerStat.MoveSpeed} and "
                    + $"{PlayerStat.ContactDamage} — what a Wight is born with — and nothing else; "
                    + "the rest are player numbers, and minting one here would put a modifier on a "
                    + "number nothing reads."),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Its own switch rather than a try/catch around <see cref="Resolve"/> or an
    /// <c>Enum.IsDefined</c>, for the reason <see cref="MinionStats.Has"/> gives: the first turns a
    /// contract into control flow, and the second would answer <see langword="true"/> for every
    /// member of the enum, which is the wrong answer for nine of them. A row walks every member and
    /// asserts the two agree.
    /// </remarks>
    public bool Has(PlayerStat stat)
    {
        return stat is PlayerStat.MaxHp or PlayerStat.MoveSpeed or PlayerStat.ContactDamage;
    }
}
