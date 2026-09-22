using Soulvail.Core.Progression;

namespace Soulvail.Core.Events;

// The economy module's domain events. Event structs are grouped per module — the one accepted
// exception to one type per file, because an event is three lines and reading a module's
// vocabulary in one place is worth more than the rule. `ProgressionEvents.cs`' precedent. See AR
// §5, §8 and <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// Every event here says what happened, never what to do, and carries only what a listener cannot
// look up for itself. They are `readonly struct`s with public readonly fields and a constructor:
// no properties, no logic, no behaviour to go wrong, and nothing to allocate when they cross
// `IDomainEvents` by `in`.
//
// **The file opened with one member and is named for the module rather than for it** — M6-02b
// added what the four Sanctum services publish, which is why it was never `EssenceChangedEvent.cs`.

/// <summary>
/// The wallet moved. GD §15's second currency changing hands, from a payment or a purchase.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries both numbers because neither can be derived from the other without the reader
/// keeping state.</b> A float-up wants the delta — <em>"+24"</em> over the door — and a readout
/// wants the balance. A reader given only the balance would have to remember the previous one to
/// animate anything, and a reader given only the delta would have to sum every event it had ever
/// seen to draw a number.
/// </para>
/// <para>
/// <b>There is no reason field, and that is a decision rather than an omission</b> (M6-01a rule 1).
/// AR §8 asks events to describe rather than duplicate: every payment is published on the same tick
/// as the <c>StageCleared</c> that caused it, and every spend M6-02b adds is published on the same
/// tick as its own purchase event. A reason enum here would be a third way of saying what two
/// events already say, and it would need a member per future source.
/// </para>
/// <para>
/// <b>Nothing publishes this during a restore</b>, which is <c>EssenceWallet.Restore</c>'s rule: a
/// balance earned in a previous session is not news, and the HUD that would draw it has not
/// subscribed yet.
/// </para>
/// </remarks>
public readonly struct EssenceChanged
{
    /// <summary>What the run holds once this movement is counted. Never negative.</summary>
    public readonly int Balance;

    /// <summary>Positive for a payment, negative for a purchase. Never zero — rule 1.</summary>
    public readonly int Delta;

    public EssenceChanged(int balance, int delta)
    {
        Balance = balance;
        Delta = delta;
    }
}

/// <summary>
/// A Sanctum service was bought: GD §13.3's four, paid for and delivered.
/// </summary>
/// <remarks>
/// <para>
/// <b>Published after the wallet has moved and after the service has landed</b> (M6-02b rule 8),
/// so a subscriber reading the balance, the hit points or the meter from inside it reads what the
/// purchase left — <see cref="EssenceChanged"/> has already gone out, on the same tick.
/// </para>
/// <para>
/// <b>It carries the price paid rather than leaving the reader to ask <c>PriceOf</c></b>, because
/// the reroll's price has already doubled by the time anyone could ask. A Banish also publishes
/// <see cref="NodeBanished"/>, which carries the node.
/// </para>
/// </remarks>
public readonly struct SanctumServiceBought
{
    /// <summary>Which of the four.</summary>
    public readonly SanctumService Service;

    /// <summary>What it cost, in Essence.</summary>
    public readonly int Price;

    /// <summary>What the wallet holds after paying.</summary>
    public readonly int Essence;

    public SanctumServiceBought(SanctumService service, int price, int essence)
    {
        Service = service;
        Price = price;
        Essence = essence;
    }
}
