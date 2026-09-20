using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

// The seam this task exists for. Everything else a boss needs from the combat machinery is
// already target-agnostic — Health is the same class for both, EffectRegistry.Apply takes an
// `object source`, SkillRunner names no player at all — and the one thing that was not is that a
// ModifyStat could address the player and nothing else, for ever.

/// <summary>
/// What a <see cref="ModifyStat"/> needs of whatever it is aimed at: an address book that turns a
/// <see cref="PlayerStat"/> into the live <c>Stat</c> behind it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately one question, asked two ways.</b> This is not a combatant and must never grow
/// into one: it has no health, no position and no identity, because the moment it does, every
/// handler that only wanted to move a number starts depending on the whole of whoever holds it.
/// <see cref="PlayerStats"/> and <see cref="CombatantStats"/> are the two implementations, and the
/// second is what proves the first was an interface rather than a rename.
/// </para>
/// <para>
/// <b>It addresses by <see cref="PlayerStat"/>, and a parallel <c>EnemyStat</c> was weighed and
/// refused</b> (M4-01a rule 1): the two sets overlap, a second enum means
/// <c>ModifyStatDefinition</c> needs a second dropdown and a designer needs to know which, and
/// <em>"enum ordinals as content identity"</em> is already banned. The cost of one shared address
/// space is that not every block answers every address — which is what <see cref="Has"/> is for,
/// and what <see cref="Resolve"/> is loud about.
/// </para>
/// <para>
/// <b>Not every address, and a block that has none says so.</b> A Husk has no
/// <see cref="PlayerStat.WeaponRange"/>; that is a question with no answer rather than a number
/// waiting to be created. Inventing a <c>Stat</c> on demand would put a modifier on a number
/// nothing reads — an effect that reports success and changes nothing, which is Traps §1's family
/// and is invisible to every check this project owns.
/// </para>
/// </remarks>
public interface IStatBlock
{
    /// <summary>
    /// The live <c>Stat</c> behind <paramref name="stat"/> — the very instance, never a copy of its
    /// value, so a modifier put on it moves the number the game reads.
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// This block has no stat at that address. Loud rather than null, and loud rather than a
    /// freshly minted <c>Stat</c> — see the remarks on this interface.
    /// </exception>
    Stat Resolve(PlayerStat stat);

    /// <summary>
    /// Whether <see cref="Resolve"/> would answer, so a caller can refuse without catching.
    /// </summary>
    /// <remarks>
    /// Exactly the negation of "<see cref="Resolve"/> throws" — no address may answer one way here
    /// and the other way there, which every implementation owes a row to.
    /// </remarks>
    bool Has(PlayerStat stat);
}
