using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Progression;

/// <summary>GD §13.3's four services. A closed set of arithmetic, never saved by ordinal.</summary>
/// <remarks>
/// Nothing writes this to disk: the save carries the counters and the banished ids, never which
/// service was bought, so the ordinal is not identity anywhere (CLAUDE.md's ban on enum ordinals as
/// content identity does not bite).
/// </remarks>
public enum SanctumService
{
    /// <summary>A banked reroll of the next offer. Doubles in price with every one bought.</summary>
    Reroll,

    /// <summary>One untaken node out of the run's pool for good. Needs a node — see <see cref="SanctumShop.Banish"/>.</summary>
    Banish,

    /// <summary>Hit points back, up to the maximum.</summary>
    Heal,

    /// <summary>Veilrot off the meter, down to zero. Never ends the Claiming.</summary>
    Cleanse,
}

/// <summary>
/// GD §13.3's shop: what each service costs right now, whether it can be bought, and the verbs that
/// buy it.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="CanBuy"/> is the predicate and <see cref="Buy"/> the invariant behind it</b> —
/// <c>EssenceWallet</c>'s pairing, one object up, and M5-08a's lesson: <em>a screen may not offer
/// what the model refuses.</em> <see cref="CanBuy"/> refuses the unaffordable <b>and</b> the
/// worthless (M6-02b rule 7) — a heal at full health, a cleanse at zero, a banish with nothing left
/// to banish — so M6-03a draws a dead button with a reason rather than taking the player's money.
/// </para>
/// <para>
/// <b>It knows nothing about the Sanctum's phase.</b> Whether the shop is open is
/// <c>RunSession</c>'s question, asked at the port; this object is the arithmetic and the delivery,
/// and a fixture can build one without a stage.
/// </para>
/// <para>
/// <b>Nothing is re-implemented here</b> (rule 6): Heal is <c>Health.Heal</c>, Cleanse is
/// <c>Veilrot.Cleanse</c>, Banish is <c>SkillTree.Banish</c> and Reroll is
/// <c>LevelUpFlow.GrantReroll</c>. What this adds is the price, the refusal and the order.
/// </para>
/// <para>
/// <b>It allocates nothing</b> (rule 10). <see cref="PriceOf"/> and <see cref="CanBuy"/> are read
/// by a screen at 60 Hz; both are integer arithmetic and flag reads.
/// </para>
/// </remarks>
public sealed class SanctumShop
{
    /// <summary>The reroll's price multiplier per purchase — GD §13.3's "doubles per use".</summary>
    /// <remarks>
    /// A rule rather than content (rule 2), beside <c>OfferGenerator.SameBranchPenalty</c> in kind: a
    /// designer who wanted tripling would be changing the shape of the economy, not a number in it.
    /// </remarks>
    public const int RerollDoubling = 2;

    private readonly SanctumSpec _prices;
    private readonly EssenceWallet _wallet;
    private readonly Health _health;
    private readonly Veilrot _veilrot;
    private readonly SkillTree _tree;
    private readonly LevelUpFlow _levelUp;
    private readonly IDomainEvents _events;

    /// <param name="prices">The mode's shop — <c>ModeSpec.Sanctum</c>.</param>
    /// <param name="wallet">Where the price comes out of.</param>
    /// <param name="combat">Whose health Heal restores.</param>
    /// <param name="veilrot">
    /// The run's meter, which Cleanse reduces. Null is legal and means Cleanse is never worth
    /// buying — the spec's one optional argument, from before M6-04 made every run hold a meter.
    /// </param>
    /// <param name="tree">The run's tree, which Banish narrows.</param>
    /// <param name="levelUp">The run's level-up flow, which a Reroll charges.</param>
    /// <param name="events">Where <see cref="SanctumServiceBought"/> goes.</param>
    /// <exception cref="ArgumentNullException">Any argument but <paramref name="veilrot"/> is null.</exception>
    public SanctumShop(
        SanctumSpec prices, EssenceWallet wallet, PlayerCombat combat, Veilrot veilrot,
        SkillTree tree, LevelUpFlow levelUp, IDomainEvents events)
    {
        _prices = prices;
        _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        _health = (combat ?? throw new ArgumentNullException(nameof(combat))).Health;
        _veilrot = veilrot;
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _levelUp = levelUp ?? throw new ArgumentNullException(nameof(levelUp));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>Rerolls bought this run — what the save carries, and what doubles the price.</summary>
    public int RerollsBought { get; private set; }

    /// <summary>Rerolls a draw has spent this run. The flow counts them; this reads it.</summary>
    public int RerollsSpent => _levelUp.RerollsSpent;

    /// <summary>What <paramref name="service"/> costs right now. Saturates rather than overflowing — rule 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="service"/> is not a member.</exception>
    public int PriceOf(SanctumService service)
    {
        RequireMember(service);

        return service switch
        {
            SanctumService.Reroll => RerollPrice(),
            SanctumService.Banish => _prices.BanishPrice,
            SanctumService.Heal => _prices.HealPrice,
            _ => _prices.CleansePrice,
        };
    }

    /// <summary>
    /// Whether <paramref name="service"/> can be bought right now: affordable <b>and</b> worth
    /// something (rule 7). Reroll has no usefulness test — a charge is never wasted.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="service"/> is not a member.</exception>
    public bool CanBuy(SanctumService service)
    {
        int price = PriceOf(service);

        // A saturated reroll is refused outright rather than compared: int.MaxValue is the
        // arithmetic running out, not a price anyone was meant to pay (rule 2).
        if (price == int.MaxValue || !_wallet.CanAfford(price))
        {
            return false;
        }

        return service switch
        {
            SanctumService.Reroll => true,
            SanctumService.Banish => AnythingBanishable(),
            SanctumService.Heal => !_health.IsDead && _health.Fraction < 1f,
            _ => _veilrot is not null && _veilrot.Value > 0f,
        };
    }

    /// <summary>
    /// Buys <paramref name="service"/>: pays, delivers, and publishes <see cref="SanctumServiceBought"/>.
    /// </summary>
    /// <remarks>
    /// The invariant behind <see cref="CanBuy"/>, never an alternative to it. <b>The wallet moves
    /// first</b> (rule 8), so a subscriber inside the event reads a balance already paid.
    /// <see cref="SanctumService.Banish"/> is refused here: it needs a node (rule 5).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="service"/> is not a member.</exception>
    /// <exception cref="InvalidOperationException"><see cref="CanBuy"/> is false, or the service is Banish.</exception>
    public void Buy(SanctumService service)
    {
        RequireMember(service);

        if (service == SanctumService.Banish)
        {
            throw new InvalidOperationException(
                "Banish needs a node, so it is bought through Banish(skillId) rather than Buy — "
                    + "ChooseSplash's asymmetry against ChooseOffer, one screen over.");
        }

        if (!CanBuy(service))
        {
            throw new InvalidOperationException(
                $"{service} cannot be bought: it costs {PriceOf(service)} against {_wallet.Balance} "
                    + "Essence, or there is nothing for it to do. CanBuy answers this before the "
                    + "player commits (M6-02b rule 7), so reaching here is a screen offering what "
                    + "the shop refuses.");
        }

        int price = PriceOf(service);

        _wallet.Spend(price);

        switch (service)
        {
            case SanctumService.Reroll:
                RerollsBought++;
                _levelUp.GrantReroll();
                break;

            case SanctumService.Heal:
                // A partial heal still costs the whole price — the player's call (Heal_CannotOverfill).
                _health.Heal(_prices.HealAmount);
                break;

            default:
                _veilrot.Cleanse(_prices.CleanseAmount);
                break;
        }

        _events.Publish(new SanctumServiceBought(service, price, _wallet.Balance));
    }

    /// <summary>
    /// Every node <see cref="Banish"/> may take, in tree order, and how many were written — rule 5:
    /// every untaken, unbanished node, available or not.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is shorter than <c>TreeRules.Count</c> — <c>SkillTree.Available</c>'s rule.
    /// </exception>
    public int BanishableInto(Span<ContentId> destination) => _tree.Banishable(destination);

    /// <summary>
    /// Pays <see cref="SanctumService.Banish"/>'s price and takes <paramref name="skillId"/> out of
    /// this run's pool for good.
    /// </summary>
    /// <remarks>
    /// Asked, then paid, then banished: the tree's refusal is asked through <c>CanBanish</c> before
    /// the wallet moves, so a refused node costs nothing. <c>NodeBanished</c> goes out from the tree
    /// after the payment and <see cref="SanctumServiceBought"/> last (rule 8).
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="skillId"/> is <c>default</c>.</exception>
    /// <exception cref="InvalidOperationException">Short, or the node cannot be banished.</exception>
    public void Banish(ContentId skillId)
    {
        if (skillId.Value is null)
        {
            throw new ArgumentException("default(ContentId) names no node to banish.", nameof(skillId));
        }

        int price = _prices.BanishPrice;

        if (!_wallet.CanAfford(price))
        {
            throw new InvalidOperationException(
                $"Banish costs {price} against {_wallet.Balance} Essence. CanBuy(Banish) answers this "
                    + "before the player commits (M6-02b rule 7).");
        }

        if (!_tree.CanBanish(skillId))
        {
            throw new InvalidOperationException(
                $"'{skillId}' cannot be banished: it is not a node of this run's tree, or it is "
                    + "already taken or banished. BanishableInto lists the ones that can be.");
        }

        _wallet.Spend(price);
        _tree.Banish(skillId);

        _events.Publish(new SanctumServiceBought(SanctumService.Banish, price, _wallet.Balance));
    }

    /// <summary>What a resumed run comes back with. Silent — rule 9.</summary>
    /// <remarks>
    /// <para>
    /// <b>Public, where the spec drafted it <c>internal</c></b>, for <c>SkillTree.Restore</c>'s
    /// reason: <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c> and never will (AR
    /// §18.2), and <see cref="Buy"/> is already public on this object. The seal that matters is
    /// <c>RunState.Shop</c>, which is <c>internal</c>.
    /// </para>
    /// <para>
    /// Unguarded beyond what a caller could not have meant: <c>RunEconomy</c>'s constructor has
    /// refused negatives and a spent count above the bought one at the file's door.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A count is negative, or more are spent than were bought.
    /// </exception>
    public void Restore(int bought, int spent)
    {
        if (bought < 0 || spent < 0 || spent > bought)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spent),
                spent,
                $"{spent} spent of {bought} bought is not a stock: both are counts, and what is left "
                    + "to use is bought minus spent.");
        }

        RerollsBought = bought;
        _levelUp.RestoreRerolls(bought - spent, spent);
    }

    /// <summary>
    /// <c>RerollPrice × 2^RerollsBought</c>, saturating at <see cref="int.MaxValue"/> (rule 2).
    /// </summary>
    /// <remarks>
    /// A loop of doublings rather than a shift, because a shift by 32 or more wraps in C# and a
    /// hand-edited <c>"rerollsBought": 99</c> would otherwise price the reroll negative, free or
    /// zero. The loop stops the moment the price cannot double again, so it runs at most 31 times.
    /// </remarks>
    private int RerollPrice()
    {
        long price = _prices.RerollPrice;

        for (int i = 0; i < RerollsBought && price > 0; i++)
        {
            price *= RerollDoubling;

            if (price >= int.MaxValue)
            {
                return int.MaxValue;
            }
        }

        return (int)price;
    }

    /// <summary>Whether any node is left to banish — asked without a buffer, so without allocating.</summary>
    private bool AnythingBanishable()
    {
        // TakenCount + BanishedIds.Count is every node that cannot be banished; a tree with room
        // for one more has a banishable node, because the two sets never overlap.
        return _tree.TakenCount + _tree.BanishedIds.Count < _tree.Rules.Count;
    }

    private static void RequireMember(SanctumService service)
    {
        if (service < SanctumService.Reroll || service > SanctumService.Cleanse)
        {
            throw new ArgumentOutOfRangeException(
                nameof(service),
                service,
                "Not one of GD §13.3's four services.");
        }
    }
}
