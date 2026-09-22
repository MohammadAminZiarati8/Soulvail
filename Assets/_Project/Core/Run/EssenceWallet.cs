using System;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Run;

/// <summary>
/// One run's Essence: what it has been paid and what it has left. GD §15's second currency, and the
/// only one that is spent inside the run it was earned in.
/// </summary>
/// <remarks>
/// <para>
/// <b>It knows nothing about where the money came from.</b> GD §15's formula — <c>20 + 4·n</c> a
/// stage, <c>+60</c> a boss — lives on <see cref="Content.EssenceSpec"/>, because it is a mode's
/// authored statement about itself (ADR-0006) and this object has no depth to apply it to. What
/// arrives here is a number, and the one rule it enforces is that the balance never goes negative.
/// </para>
/// <para>
/// <b><see cref="CanAfford"/> is the predicate and <see cref="Spend"/> is the invariant behind it,
/// and that pairing is M5-08a's lesson made structural.</b> That task's finding was that <em>a
/// screen may not offer what the model refuses</em>: its guard was correct, atomic and well tested,
/// and the only fault was that nothing asked it before the player committed. So the refusal ships
/// as a question a caller can ask from the first line of the economy rather than as an exception
/// discovered on a phone. <see cref="Spend"/> still throws — the invariant survives — and M6-03a is
/// what makes a button that cannot be afforded undrawable rather than unhappy.
/// </para>
/// <para>
/// <b>It allocates nothing.</b> An <see langword="int"/> field, a comparison, and a
/// <see langword="readonly"/> struct published by value. Neither verb is on a per-frame path at
/// all: a run that clears no stage this tick does not call <see cref="Earn"/>.
/// </para>
/// <para>
/// <b>Nothing spends it until M6-02b</b>, which is M4-01a's bargain: the half that ships used by
/// nothing is the half whose review is about the arithmetic.
/// </para>
/// </remarks>
public sealed class EssenceWallet
{
    private readonly IDomainEvents _events;

    private int _balance;

    /// <param name="events">Where <see cref="EssenceChanged"/> goes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    public EssenceWallet(IDomainEvents events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>What the player has, never negative.</summary>
    public int Balance => _balance;

    /// <summary>
    /// Pays the run <paramref name="amount"/> Essence.
    /// </summary>
    /// <param name="amount">
    /// Points to pay. Zero and negative do nothing and publish nothing — rule 1.
    /// </param>
    /// <remarks>
    /// <b><c>Health.Heal</c>'s rule, and for its reason.</b> A payment of nothing is not news, and a
    /// reader that had to filter zeroes out of <see cref="EssenceChanged"/> would be a reader that
    /// could forget to. A <em>negative</em> is refused the same way rather than thrown at, because
    /// the one thing it could mean — a fine — is <see cref="Spend"/>'s job, which has a balance to
    /// check it against.
    /// </remarks>
    public void Earn(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        _balance += amount;

        _events.Publish(new EssenceChanged(_balance, amount));
    }

    /// <summary>
    /// Whether <paramref name="cost"/> can be paid right now.
    /// </summary>
    /// <remarks>
    /// <b>The predicate a screen reads before it draws a button</b> (rule 7). It answers rather than
    /// throws for every input, including a negative <paramref name="cost"/> — asking whether
    /// something affordable is affordable is not a mistake worth a stack trace, and
    /// <see cref="Spend"/> is where a negative meets a door.
    /// </remarks>
    public bool CanAfford(int cost) => cost <= _balance;

    /// <summary>
    /// Takes <paramref name="cost"/> out of the wallet.
    /// </summary>
    /// <param name="cost">
    /// Points to spend. Zero leaves the balance where it was and publishes nothing, for
    /// <see cref="Earn"/>'s reason.
    /// </param>
    /// <remarks>
    /// The invariant behind <see cref="CanAfford"/>, not an alternative to it (rule 7). A caller
    /// that asks first never sees either exception; one that does not is a screen offering what the
    /// model refuses, which is the defect this pair exists to make unreachable.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="cost"/> is negative. A refund is not a purchase, and the verb that pays a
    /// run is <see cref="Earn"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The balance is short. Loud rather than clamped: a purchase that quietly took what was there
    /// would hand over the goods for less than the price.
    /// </exception>
    public void Spend(int cost)
    {
        if (cost < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cost),
                cost,
                "A cost is zero or more. Paying the run is Earn, which is the only verb that "
                    + "raises a balance.");
        }

        if (cost > _balance)
        {
            throw new InvalidOperationException(
                $"Spending {cost} Essence against a balance of {_balance}. CanAfford answers this "
                    + "before the player commits (M6-01a rule 7), so reaching here is a screen "
                    + "offering what the wallet refuses rather than something the player did.");
        }

        if (cost == 0)
        {
            return;
        }

        _balance -= cost;

        _events.Publish(new EssenceChanged(_balance, -cost));
    }

    /// <summary>
    /// Puts the wallet back where a saved run left it. M6-01b's, and no event — rule 8.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>LevelTracker.Restore</c> and <c>SkillRunner.Restore</c>'s shape (M3-01b rule 7, M3-07b
    /// rule 6): a resume is not news, and an <see cref="EssenceChanged"/> published during
    /// <c>RunSession.Start</c> would reach a HUD that has not subscribed yet.
    /// </para>
    /// <para>
    /// <b>Unguarded, deliberately</b>, like <c>LevelTracker.Restore</c> and for its reason (AR
    /// §18.2): it is <c>internal</c>, the one caller will be three lines of core, and the number it
    /// passes has already been guarded by <c>RunSnapshot</c>'s constructor — which is the real
    /// boundary, because it came out of a file. A second copy of that guard here is the first place
    /// the two could disagree.
    /// </para>
    /// <para>
    /// <b>It is written here rather than at M6-01b</b> because the alternative is that task editing
    /// this file for one method.
    /// </para>
    /// </remarks>
    internal void Restore(int balance)
    {
        _balance = balance;
    }
}
