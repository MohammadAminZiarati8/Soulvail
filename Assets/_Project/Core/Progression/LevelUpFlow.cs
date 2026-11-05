using System;
using System.Collections;
using System.Collections.Generic;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Progression;

// What finally spends the picks `LevelTracker` has been banking since M3-01a. `OfferGenerator` says
// which nodes a player would be shown and `SkillTree` says what taking one does; this is the object
// that decides *when* to ask, what happens when there is nothing left to ask about, and what a
// chosen card costs. `StageFlow`'s shape — a small state machine owned by `RunState`, driven from
// outside, publishing rather than being subscribed to.

/// <summary>
/// A run's lazy level-up offers and ordered skill picks. Points stay banked when no node is available.
/// </summary>
/// <remarks>
/// Draws happen after the tick that earns a level, preserving the saved Offers stream position.
/// RunState exposes scalar reads and the offer view rather than the live flow.
/// </remarks>
public sealed class LevelUpFlow
{
    private readonly SkillTree _tree;
    private readonly LevelTracker _progression;
    private readonly SkillRunner _runner;
    private readonly EffectRegistry _effects;
    private readonly IDomainEvents _events;
    private readonly Veilrot _veilrot;
    private readonly OfferGenerator _generator;

    /// <summary>The run's Ordeals, for Vigil's card count. Null for a flow built without one.</summary>
    private readonly Ordeals _ordeals;

    /// <summary>
    /// The offer's ids. One buffer for the life of the run, rewritten by every draw — which is safe
    /// because it is read on a frame that is not ticking (M3-08a rule 15), and is named here for the
    /// reason <c>WorldSnapshot</c>'s reuse is named everywhere else.
    /// </summary>
    private readonly ContentId[] _offer = new ContentId[OfferGenerator.DefaultOfferCount];

    /// <summary>
    /// Wrapped once at construction rather than per call: M3-08b polls <see cref="Offer"/> to draw
    /// three cards, and a per-call wrapper would allocate on that path (<c>SkillRunner.Slots</c>'
    /// precedent).
    /// </summary>
    private readonly OfferView _offerView;

    /// <summary>
    /// An uninstalled path counts as room when checking whether the run has a full tree.
    /// </summary>
    private readonly PathFlow _path;

    private int _count;

    /// <summary>
    /// Which of <see cref="_offer"/> is a Pact, or <c>-1</c>. Written by the same draw that writes
    /// the ids, and reset with them (M6-05b rule 5).
    /// </summary>
    private int _pactIndex = -1;

    /// <param name="tree">The run's live tree. Never null — a class without one builds no flow.</param>
    /// <param name="progression">Where the picks are banked and spent.</param>
    /// <param name="runner">Told directly about a chosen Active, because core does not subscribe to
    /// its own events (M3-03 rule 7).</param>
    /// <param name="effects">Where a chosen Pact applies its effects.</param>
    /// <param name="events">Where the three announcements go out.</param>
    /// <param name="veilrot">
    /// GD §10's meter, which a corrupted take pays into (M6-05b rule 6). Required — every run has
    /// one, and a null would be a run that took Pacts for free (M6-01a rule 5's argument).
    /// </param>
    /// <param name="ordeals">
    /// What this run has been dealt, for GD §13.4's Vigil — or <see langword="null"/> for none, which
    /// is a true statement below the mode's first Ordeal stage rather than a mis-wiring (M6-06b
    /// rule 5, and the stated reason it differs from <paramref name="veilrot"/>'s ruling).
    /// </param>
    /// <param name="isFree">
    /// Whether a level banks a point to spend on any open node rather than opening a draw of three
    /// (<see cref="ModeSpec.HasFreePick"/>, UI-03a). Optional, and off omitted: a flow built
    /// without it is the one every earlier fixture built.
    /// </param>
    /// <param name="path">The run's path choice, or null for a class without paths.</param>
    /// <exception cref="ArgumentNullException">A required reference argument is null.</exception>
    public LevelUpFlow(SkillTree tree, LevelTracker progression, SkillRunner runner,
                       EffectRegistry effects, IDomainEvents events,
                       Veilrot veilrot, Ordeals ordeals = null, bool isFree = false, PathFlow path = null)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _veilrot = veilrot ?? throw new ArgumentNullException(nameof(veilrot));
        _ordeals = ordeals;

        IsFree = isFree;
        _path = path;

        _generator = new OfferGenerator(tree.Rules);
        _offerView = new OfferView(this);
    }

    /// <summary>
    /// Whether this run spends banked points on a node of the player's choosing rather than drawing
    /// an offer — the mode's <c>HasFreePick</c>. Fixed for the run.
    /// </summary>
    public bool IsFree { get; }

    // Future path nodes count as room even before they have been installed. A level gate never
    // makes a node disappear from the run's capacity.
    internal bool IsFull => (_path is null || _path.Candidates.Count == 0 ||
        _path.ChosenId.Value is not null) && _tree.IsFull;

    /// <summary>Whether an offer is on the table.</summary>
    public bool HasOffer => _count > 0;

    /// <summary>
    /// The ids currently offered, in the order they were drawn. Empty when there is no offer, and
    /// shorter than three when the tree had fewer available nodes than that (M3-04 rule 1).
    /// </summary>
    /// <remarks>
    /// A live view over one buffer the next draw rewrites, not a copy. Nothing may hold it across a
    /// <see cref="Choose"/>.
    /// </remarks>
    public IReadOnlyList<ContentId> Offer => _offerView;

    /// <summary>Which offered card is a Pact, or <c>-1</c>. Empty offer answers <c>-1</c>.</summary>
    public int PactIndex => _count > 0 ? _pactIndex : -1;

    /// <summary>Rerolls bought in the Sanctum and not yet spent by a draw (M6-02b rule 3).</summary>
    public int RerollCharges { get; private set; }

    /// <summary>How many offers this run has rerolled — what the save's <c>RerollsSpent</c> is.</summary>
    public int RerollsSpent { get; private set; }

    /// <summary>
    /// Banks one reroll, to be spent by the next draw that finds something — M6-02b rule 3.
    /// </summary>
    /// <remarks>
    /// <b>A charge, not a button.</b> GD §13.3 sells the reroll between stages, before there is an
    /// offer to look at, so the player never sees the three they avoided. <c>SanctumShop.Buy</c> is
    /// the one caller; the price is its.
    /// </remarks>
    internal void GrantReroll()
    {
        RerollCharges++;
    }

    /// <summary>
    /// What a resumed run comes back with: <paramref name="spent"/> rerolls already used, and
    /// <paramref name="charges"/> still banked. Silent, and unguarded for
    /// <c>EssenceWallet.Restore</c>'s reason — <c>RunEconomy</c> already refused a negative stock.
    /// </summary>
    internal void RestoreRerolls(int charges, int spent)
    {
        RerollCharges = charges;
        RerollsSpent = spent;
    }

    /// <summary>
    /// Draws one offer when a point is owed and nodes are available; otherwise keeps points banked.
    /// Idempotent while an offer is open, so unseen draws cannot advance the saved stream.
    /// </summary>
    /// <param name="offers">The run's <c>Offers</c> stream, and no other (ADR-0011).</param>
    /// <exception cref="ArgumentNullException"><paramref name="offers"/> is null.</exception>
    public void Open(IRandomStream offers)
    {
        if (offers is null)
        {
            throw new ArgumentNullException(nameof(offers));
        }

        // A free run never draws: its points wait for Take
        // (UI-03a rule 2). Guarded here as well as by RunState.IsLevelUpPending, so a stray call
        // cannot spend a draw the simulation is not counting on.
        if (IsFree)
        {
            return;
        }

        // Rule 3, and it is above the loop rather than inside it: an offer already on the table is
        // the answer to this call, whatever else is owed behind it.
        if (_count > 0)
        {
            return;
        }

        int cards = CardsToOffer();

        if (_progression.PendingLevelUps > 0)
        {
            int drawn = _generator.Draw(_tree, offers, cards, _offer, out int pactIndex);

            if (drawn > 0)
            {
                // **M6-02b rule 3: a banked reroll is spent here, and only on a draw that found
                // something** — a full tree spends no charge, because there is no
                // offer to reroll. The first three are discarded unseen and the second draw comes
                // from the same Offers stream, so the only thing the extra draw moves is which
                // nodes this run is offered later (ADR-0011). The tree has not changed between the
                // two draws, so the second finds something whenever the first did.
                //
                // **And the Pact is rolled again with it** (M6-05b rule 9): each draw costs
                // picks + 2, so a rerolled level spends twice that, and the second three may be
                // corrupted where the first were not.
                if (RerollCharges > 0)
                {
                    RerollCharges--;
                    RerollsSpent++;

                    drawn = _generator.Draw(_tree, offers, cards, _offer, out pactIndex);
                }

                // **A run with the meter off is offered no Pact** (RS-05a rule 3). Dropped after
                // the draw rather than skipped inside it, so the roll still spends its two draws and
                // a seed deals the same cards with the meter on or off (M6-05b rule 1).
                if (!_veilrot.IsOn)
                {
                    pactIndex = -1;
                }

                _count = drawn;
                _pactIndex = pactIndex;
                _events.Publish(new OfferPresented(drawn, _progression.PendingLevelUps, pactIndex));

                return;
            }
        }
    }

    /// <summary>
    /// How many cards a level-up draws: GD §13.4's Vigil's count when one is dealt, and the buffer's
    /// three otherwise (M6-06b rule 2).
    /// </summary>
    /// <remarks>
    /// <b>A number passed to <c>Draw</c> and nothing else</b> — <c>OfferGenerator.DefaultOfferCount</c>'s
    /// own remarks called it. The buffer stays a three-array, so a count authored above three is held
    /// to it, and <c>Draw</c> already clamps to what is available. The screen needs nothing: it hides
    /// every card past <c>OfferPresented.Count</c> (M3-08b rule 6).
    /// </remarks>
    private int CardsToOffer()
    {
        int vigil = _ordeals is null ? 0 : _ordeals.OfferCount;

        return vigil > 0 && vigil < _offer.Length ? vigil : _offer.Length;
    }

    /// <summary>
    /// Takes the offered node at <paramref name="index"/>, spends the pick, and opens the next one.
    /// </summary>
    /// <remarks>
    /// <b>One ordered sequence.</b> <c>SkillTree.Take</c> first, which publishes <c>NodeTaken</c>
    /// with the effects already applied (M3-03 rule 4); then the pick is spent; then, if the node is
    /// an Active, the runner is told <em>directly</em>, because core does not subscribe to its own
    /// events (M3-03 rule 7). Then the offer is cleared and <see cref="Open"/> runs again, so the
    /// second card of a double level-up can be the node the first one just unlocked. A pick that
    /// refuses would otherwise leave the node owned, its effects on and the pick spent — which is
    /// exactly why M3-06 ruled an over-capacity tree refuses the run at <c>Start</c> rather than
    /// letting <c>SkillRunner.Add</c> throw from here.
    /// </remarks>
    /// <param name="index">Which card, from 0.</param>
    /// <param name="offers">The run's <c>Offers</c> stream, for the redraw.</param>
    /// <exception cref="ArgumentNullException"><paramref name="offers"/> is null.</exception>
    /// <exception cref="InvalidOperationException">No offer is open.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the offer.</exception>
    public void Choose(int index, IRandomStream offers)
    {
        if (offers is null)
        {
            throw new ArgumentNullException(nameof(offers));
        }

        if (_count == 0)
        {
            throw new InvalidOperationException(
                "No offer is open, so there is nothing to choose. Open the level-up first — a view "
                + "sending ChooseOffer without a card to send it for is a wiring mistake.");
        }

        if (index < 0 || index >= _count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, $"The offer holds {_count} card(s), so the index must be in [0, {_count}).");
        }

        ContentId id = _offer[index];
        SkillSpec spec = _tree.Rules.Skill(id);

        // **M6-05b rule 6: one take and one gain, in that order.** The position the player tapped
        // is corrupted only if the model said so when it drew — a view cannot ask for a Pact it was
        // not offered. `Take` applies the Pact's effects and publishes `NodeTaken`; then the meter
        // moves, so a `VeilrotChanged` reader finds the node already owned. A gain that reaches 100
        // begins the Claiming from here, on a screen the player is looking at, which is M6-04 rule
        // 6's latch closing on the tick the meter got there.
        bool asPact = index == _pactIndex;

        _tree.Take(id, asPact);

        if (asPact)
        {
            _veilrot.Gain(spec.Pact.Veilrot);
        }

        _progression.SpendLevelUp();

        if (spec.Kind == SkillKind.Active)
        {
            _runner.Add(spec);
        }

        // Cleared before the redraw, or Open would see its own stale offer and return immediately.
        _count = 0;
        _pactIndex = -1;

        Open(offers);

        // Open leaves exactly one of two states: an offer up, or every pick spent. So an empty
        // table here means nothing more is owed, and the screen closes.
        if (_count == 0)
        {
            _events.Publish(new LevelUpClosed(_progression.Level));
        }
    }

    /// <summary>
    /// Spends one banked point on <paramref name="id"/>, with no draw (UI-03a rule 4).
    /// </summary>
    /// <remarks>
    /// <see cref="Choose"/>'s order, minus the offer: <c>SkillTree.Take</c> first, so a node the tree
    /// refuses throws before the point is spent; then the point; then, for an Active, the runner.
    /// Never a Pact — a free run offers none, and a corrupted take is a choice on a card.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The run is not free, no point is banked, or the tree refuses <paramref name="id"/>.
    /// </exception>
    /// <exception cref="KeyNotFoundException"><paramref name="id"/> is not in this tree.</exception>
    public void Take(ContentId id)
    {
        if (!IsFree)
        {
            throw new InvalidOperationException(
                "This run draws its level-ups, so there is no point to spend on a node of the "
                    + "player's choosing. A view sent TakeNode on a mode without a free pick.");
        }

        if (_progression.PendingLevelUps <= 0)
        {
            throw new InvalidOperationException(
                "No skill point is banked, so there is nothing to spend on a node. A view sent "
                    + "TakeNode without a point to send it for.");
        }

        _tree.Take(id);

        _progression.SpendLevelUp();

        SkillSpec spec = _tree.Rules.Skill(id);

        if (spec.Kind == SkillKind.Active)
        {
            _runner.Add(spec);
        }
    }

    /// <summary>Leaves unspent points banked, including when the tree is full.</summary>
    public void Settle()
    {
    }

    /// <summary>
    /// A live, allocation-free <see cref="IReadOnlyList{T}"/> over the flow's one offer buffer,
    /// honouring the current count rather than the array's length.
    /// </summary>
    /// <remarks>
    /// A class rather than an <c>ArraySegment</c>: the segment is a struct, so returning one as an
    /// interface would box on every read of <see cref="Offer"/>.
    /// </remarks>
    private sealed class OfferView : IReadOnlyList<ContentId>
    {
        private readonly LevelUpFlow _flow;

        internal OfferView(LevelUpFlow flow) => _flow = flow;

        public int Count => _flow._count;

        public ContentId this[int index] =>
            index >= 0 && index < _flow._count
                ? _flow._offer[index]
                : throw new ArgumentOutOfRangeException(
                    nameof(index), index, $"The offer holds {_flow._count} card(s).");

        public IEnumerator<ContentId> GetEnumerator()
        {
            for (int i = 0; i < _flow._count; i++)
            {
                yield return _flow._offer[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
