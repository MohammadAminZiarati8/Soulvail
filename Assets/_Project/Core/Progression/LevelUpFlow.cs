using System;
using System.Collections;
using System.Collections.Generic;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Progression;

// What finally spends the picks `LevelTracker` has been banking since M3-01a. `OfferGenerator` says
// which nodes a player would be shown and `SkillTree` says what taking one does; this is the object
// that decides *when* to ask, what happens when there is nothing left to ask about, and what a
// chosen card costs. `StageFlow`'s shape — a small state machine owned by `RunState`, driven from
// outside, publishing rather than being subscribed to.

/// <summary>
/// A run's level-up flow: the offer on the table, the pick it is for, and CH §5.2's Overflow for a
/// pick with nothing left to spend it on.
/// </summary>
/// <remarks>
/// <para>
/// <b>The draw is lazy, and that is the whole of M3-04's inherited rule.</b> <see cref="Open"/> is
/// called on the frame <em>after</em> the tick that earned the level, never inside it. The boundary
/// snapshot is taken on entering <c>Clear</c> — the same tick as a stage's last kill, which is the
/// kill most likely to level (AR §18.1, M2-14a) — so it captures the <c>Offers</c> stream before
/// any draw, and a run killed with a pick owed rolls the same three nodes on resume. Drawn in-tick
/// instead, the capture would record a position the draw had already advanced and killing the app
/// would be a free reroll. Nothing here reads a clock; <c>RunTicker</c> owns the <em>when</em>.
/// </para>
/// <para>
/// <b><see cref="Open"/> asks the generator, and the answer decides everything —
/// <c>SkillTree.IsFull</c> is deliberately not the test.</b> M3-04 rule 1 writes nothing and makes
/// no draw when nothing is available, and <em>"nothing available"</em> is not the same as
/// <em>"tree full"</em>: a branch whose remaining nodes are Upgrades of an untaken parent can be
/// blocked while the tree is half empty. The honest condition is what <c>Draw</c> returned.
/// </para>
/// <para>
/// <b>Overflow is silent and instant: no pause, no screen.</b> CH §5.2 makes it a consolation for a
/// level with nowhere to go, and GD §13.1's pause exists to let someone <em>choose</em>. Pausing a
/// fight to show a card with one button would tax the player for the game having run out of nodes.
/// It is announced instead, through <see cref="OverflowGranted"/>, so M3-10's HUD or M3-13 can toast
/// it — which keeps CH §5.2's <em>"levelling never stops meaning something"</em> visible without
/// stopping the game.
/// </para>
/// <para>
/// <b>Nothing here is stored on the snapshot and <c>RunSnapshot.CurrentVersion</c> stays 3.</b>
/// Every pick a run has earned is spent on a node, spent on Overflow, or unspent, so the Overflow
/// count is <em>derived</em> on resume — see <see cref="GrantOverflow"/>. The offer is not saved
/// either: the lazy draw means a resumed run re-draws the identical three from the same stream
/// position, which is a stronger guarantee than storing them and one that survives a content change.
/// </para>
/// <para>
/// <b>This object is never handed out of <c>RunState</c></b> (AR §18.2, the seventh time).
/// <see cref="Choose"/> takes a node and <see cref="Open"/> consumes the run's <c>Offers</c> stream;
/// a public handle would let a view grant the player a skill and spend draws the simulation is
/// counting on. <c>RunState</c> exposes scalar reads and <see cref="Offer"/> instead.
/// </para>
/// </remarks>
public sealed class LevelUpFlow
{
    private readonly SkillTree _tree;
    private readonly LevelTracker _progression;
    private readonly SkillRunner _runner;
    private readonly EffectRegistry _effects;
    private readonly IDomainEvents _events;
    private readonly OfferGenerator _generator;

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
    /// The two effects, built once and applied repeatedly. They are immutable and identical every
    /// time — <c>EffectRegistry.Apply</c> adds a fresh <c>Modifier</c> per call whatever instance it
    /// is handed — so constructing a pair per Overflow level would allocate on a path that has no
    /// reason to.
    /// </summary>
    private readonly ModifyStat _overflowDamage;
    private readonly ModifyStat _overflowMaxHp;

    private int _count;

    /// <param name="tree">The run's live tree. Never null — a class without one builds no flow.</param>
    /// <param name="progression">Where the picks are banked and spent.</param>
    /// <param name="runner">Told directly about a chosen Active, because core does not subscribe to
    /// its own events (M3-03 rule 7).</param>
    /// <param name="effects">Where Overflow's two modifiers go on.</param>
    /// <param name="events">Where the three announcements go out.</param>
    /// <param name="overflow">
    /// CH §5.2's Overflow for the mode being played — what a spare level is worth
    /// (<see cref="ModeSpec.Overflow"/>). <b>An argument rather than two <c>const</c>s, and the
    /// <c>const</c>s are gone rather than kept as defaults</b> (M5-06b rules 8 and 10): a default
    /// beside an authored value is a second place the number lives, and the next reader would not
    /// know which one the game used. A zeroed spec is ordinary and means Overflow is worth nothing
    /// in this mode.
    /// </param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    public LevelUpFlow(SkillTree tree, LevelTracker progression, SkillRunner runner,
                       EffectRegistry effects, IDomainEvents events, OverflowSpec overflow)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _events = events ?? throw new ArgumentNullException(nameof(events));

        _generator = new OfferGenerator(tree.Rules);
        _offerView = new OfferView(this);

        // Built here rather than per grant for the reason the fields say, and read off the mode
        // rather than off a constant as of M5-06b: OverflowSpec has already refused a NaN, an
        // infinity and a negative, so nothing below has an opinion about the two numbers.
        _overflowDamage = new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, overflow.Damage);
        _overflowMaxHp = new ModifyStat(PlayerStat.MaxHp, ModifierKind.PercentAdd, overflow.MaxHp);
    }

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

    /// <summary>
    /// How many levels this run has spent on CH §5.2's Overflow rather than on a node.
    /// </summary>
    public int OverflowLevels { get; private set; }

    /// <summary>
    /// Opens the level-up for the pick that is owed: draws an offer, or spends the pick as Overflow,
    /// or does nothing when nothing is owed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It loops while picks are owed and stops at the first that draws something</b>, because a
    /// grant can cross several thresholds at once (M3-01a rule 4) and the first two may both be
    /// Overflow. At most one offer is on the table at a time, and <c>OfferPresented.PicksOwed</c> is
    /// what CH §5.1's screen shows as <em>"pick 1 of n"</em>.
    /// </para>
    /// <para>
    /// Idempotent while an offer is open: no second event, and — the half that matters — no second
    /// draw, because a draw the player never sees is a seed divergence (AR §18.3).
    /// </para>
    /// </remarks>
    /// <param name="offers">The run's <c>Offers</c> stream, and no other (ADR-0011).</param>
    /// <exception cref="ArgumentNullException"><paramref name="offers"/> is null.</exception>
    public void Open(IRandomStream offers)
    {
        if (offers is null)
        {
            throw new ArgumentNullException(nameof(offers));
        }

        // Rule 3, and it is above the loop rather than inside it: an offer already on the table is
        // the answer to this call, whatever else is owed behind it.
        if (_count > 0)
        {
            return;
        }

        while (_progression.PendingLevelUps > 0)
        {
            int drawn = _generator.Draw(_tree, offers, _offer.Length, _offer);

            if (drawn > 0)
            {
                _count = drawn;
                _events.Publish(new OfferPresented(drawn, _progression.PendingLevelUps));

                return;
            }

            // Nothing available for this pick. Spend it on CH §5.2 and ask again for the next one —
            // the tree cannot have opened up in between, but the loop is written against what
            // `Draw` answers rather than against that assumption (rule 2).
            GrantOne();
        }
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

        _tree.Take(id);
        _progression.SpendLevelUp();

        SkillSpec spec = _tree.Rules.Skill(id);

        if (spec.Kind == SkillKind.Active)
        {
            _runner.Add(spec);
        }

        // Cleared before the redraw, or Open would see its own stale offer and return immediately.
        _count = 0;

        Open(offers);

        // Open leaves exactly one of two states: an offer up, or every pick spent. So an empty
        // table here means nothing more is owed, and the screen closes.
        if (_count == 0)
        {
            _events.Publish(new LevelUpClosed(_progression.Level));
        }
    }

    /// <summary>
    /// Puts <paramref name="times"/> Overflow levels on, silently and without spending a pick — the
    /// resume path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Silent, for the reason the whole restore block is</b> (AR §18.1): nothing may publish
    /// before <c>RunStarted</c>, and a resumed run's Overflow was earned in a previous session and
    /// is not news. It spends nothing because the pick was already spent in that session — the
    /// snapshot's <c>PendingLevelUps</c> is restored separately by <c>LevelTracker.Restore</c>.
    /// </para>
    /// <para>
    /// <b>Its caller derives the count rather than reading it</b>, because no field carries it:
    /// <c>Overflow = Level − 1 − TakenNodeCount − PendingLevelUps</c>, the exact mirror of M3-03
    /// rule 6. See <c>RunSession.Start</c>, which also owns the refusal when that does not add up.
    /// </para>
    /// </remarks>
    /// <param name="times">How many levels. Zero is ordinary and does nothing.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="times"/> is negative.</exception>
    internal void GrantOverflow(int times)
    {
        if (times < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(times), times, "Overflow cannot be negative.");
        }

        for (int i = 0; i < times; i++)
        {
            ApplyOneOverflow();
        }
    }

    /// <summary>Spends one owed pick on Overflow and announces it.</summary>
    private void GrantOne()
    {
        ApplyOneOverflow();

        _progression.SpendLevelUp();

        // Published last, after the modifiers are on and the pick is spent, so a handler reading the
        // state it is being told about sees the settled one — `NodeTaken`'s rule.
        _events.Publish(new OverflowGranted(_progression.Level, OverflowLevels));
    }

    /// <summary>
    /// The arithmetic both paths share: two <c>PercentAdd</c> modifiers under one source, so ten
    /// Overflow levels at Descent's authored 2 % are ×1.20 rather than 1.02¹⁰ (GD §13.1's
    /// <em>"additively within a family"</em>, ADR-0008's order). Raising <c>MaxHp</c> is
    /// deliberately <b>not</b> a heal (M3-05).
    /// </summary>
    private void ApplyOneOverflow()
    {
        OverflowLevels++;

        // `this` is the source for the whole run, so the ten modifiers pool rather than stacking
        // multiplicatively and a single RemoveAll would take them all back (M3-05 rule 7).
        _effects.Apply(_overflowDamage, this);
        _effects.Apply(_overflowMaxHp, this);
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
