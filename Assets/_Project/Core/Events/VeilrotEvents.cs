namespace Soulvail.Core.Events;

// GD §10's corruption meter, as domain events. Event structs are grouped per module — the one
// accepted exception to one type per file, because an event is three lines and reading a module's
// vocabulary in one place is worth more than the rule. `EconomyEvents.cs`' precedent, one file
// over. See AR §5, §8 and <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// Every event here says what happened, never what to do, and carries only what a listener cannot
// look up for itself. They are `readonly struct`s with public readonly fields and a constructor:
// no properties, no logic, no behaviour to go wrong, and nothing to allocate when they cross
// `IDomainEvents` by `in`.
//
// **Nothing here is published by a restore**, which is `Veilrot.Restore`'s rule (M6-04 rule 9) and
// `EssenceWallet.Restore`'s before it: a resume is not news, and a `ClaimingBegan` raised inside
// `RunSession.Start` would reach a HUD that has not subscribed yet.

/// <summary>
/// The meter moved. GD §10's Veilrot changing, from a Pact, an Ordeal or GD §13.3's Cleanse.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries both numbers for <see cref="EssenceChanged"/>'s reason</b> (M6-01a rule 1):
/// neither can be derived from the other without the reader keeping state. A float-up wants the
/// delta — <em>"+20 Rot"</em> over the card — and M6-03b's meter down the right edge wants the
/// value. A reader given only the value would have to remember the previous one to animate
/// anything; one given only the delta would have to sum every event it had ever seen.
/// </para>
/// <para>
/// <b><see cref="Delta"/> is never zero</b>, which is <c>Gain</c> and <c>Cleanse</c>'s rule 1: a
/// movement of nothing is not news, and a <c>Gain</c> of 20 at 100 moves nothing and says nothing.
/// It is positive for a gain and negative for a cleanse, on one event type.
/// </para>
/// </remarks>
public readonly struct VeilrotChanged
{
    /// <summary>What the meter reads once this movement is counted, in <c>[0, 100]</c>.</summary>
    public readonly float Value;

    /// <summary>Positive for a gain, negative for a cleanse. Never zero — rule 1.</summary>
    public readonly float Delta;

    public VeilrotChanged(float value, float delta)
    {
        Value = value;
        Delta = delta;
    }
}

/// <summary>
/// One of GD §10.2's four rows was entered or left.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries a direction because GD §13.3's Cleanse goes back down.</b> Three of the four
/// states are recomputed from the meter rather than latched (rule 2), so cleansing from 78 to 63
/// gives the 20 % maximum hit points back and this is what says so.
/// </para>
/// <para>
/// <b>The 100 row is published in both directions too, and the Claiming still does not end.</b>
/// This event describes the <em>meter</em> crossing a line; whether the Claiming is on is
/// <c>RunState.IsClaimed</c>, which never goes false again (rule 6). A run cleansed from 100 to 40
/// therefore publishes <c>(100, entered: false)</c> and stays Claimed, which is the one state
/// combination that looks like a bug and is not.
/// </para>
/// <para>
/// <b>The 50 row is published and does nothing else</b>, and that is this milestone's one honest
/// gap: GD §10.2 has a Revenant stalk the player there, GD §19 puts the Revenant in V2, and a
/// threshold nobody can build is shipped crossed and otherwise silent rather than substituted.
/// </para>
/// </remarks>
public readonly struct VeilrotThresholdCrossed
{
    /// <summary>Which row — 25, 50, 75 or 100.</summary>
    public readonly float Threshold;

    /// <summary>True when the meter rose into the row, false when it fell out of it.</summary>
    public readonly bool Entered;

    public VeilrotThresholdCrossed(float threshold, bool entered)
    {
        Threshold = threshold;
        Entered = entered;
    }
}

/// <summary>
/// GD §10.2's last row closed: the meter reached 100 and the run bought ninety seconds of godhood
/// with the rest of its life. Published once per run, after the 100 crossing — rule 6.
/// </summary>
/// <remarks>
/// <b>It carries the maximum rather than leaving a reader to sample one.</b> The drain is 1 % of
/// <em>this</em> number per second rather than 1 % of whatever the maximum is at the time (rule 7),
/// because GD §10.3's <em>"90 seconds of godhood"</em> is what a fixed denominator gives and what a
/// compounding one never would — the compounding version is asymptotic and kills nobody. So a bar
/// drawing the countdown needs the number the countdown is against, and sampling it later would
/// read a maximum the drain has already shrunk.
/// </remarks>
public readonly struct ClaimingBegan
{
    /// <summary>
    /// What the drain is 1 % of, per second: the live maximum as it stood the instant the Claiming
    /// began, with the 75 row's −20 % already on it.
    /// </summary>
    public readonly float MaxHpAtClaiming;

    public ClaimingBegan(float maxHpAtClaiming)
    {
        MaxHpAtClaiming = maxHpAtClaiming;
    }
}
