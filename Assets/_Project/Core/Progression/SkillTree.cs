using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Progression;

// The live half of a run's tree: what has been taken, what may be, and the one verb that takes a
// node. The shape and the rules are `TreeRules`, beside it.

/// <summary>
/// One run's tree as it stands: which nodes are taken, which may be taken now, and the verb that
/// takes one — recording it, putting its effects into force, and saying so.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per run, and it dies with the run.</b> The <see cref="TreeRules"/> it reads are shared,
/// immutable authored data; everything mutable here is this player's progress through them. The
/// effects it applies go through a <see cref="EffectRegistry"/> holding <em>this</em> run's live
/// objects, which is the whole of the split ADR-0009 buys (AR §10.1).
/// </para>
/// <para>
/// <b>The registry cannot throw from inside <see cref="Take"/>, and the constructor is what buys
/// that.</b> It asks <see cref="EffectRegistry.CanApply"/> of every take effect and every cast
/// effect of every node in the tree, and refuses the tree otherwise — at <c>Start</c>, before
/// <c>RunStarted</c>. The reason is that <see cref="Take"/> records the node <em>before</em> it
/// applies the effects, so a throw part-way down a node's effect list would leave the node owned,
/// one modifier on, the other not, and no <see cref="NodeTaken"/> published. That is not a state
/// this class is willing to be left in, and the check that prevents it costs one sweep per run.
/// </para>
/// <para>
/// <b>What that check does and does not promise, ruled at M3-03.</b>
/// <see cref="EffectRegistry.CanApply"/> answers <em>"is there a handler for this effect's
/// type"</em> and nothing more, so what is guaranteed unreachable is the registry's own
/// <see cref="KeyNotFoundException"/> — together with its null guards, which
/// <see cref="SkillSpec"/> has already made unreachable by refusing a null effect entry and by
/// being the non-null <c>source</c> itself. Whether a <em>handler</em> can then apply what it was
/// given is the primitive's own door, per primitive: <c>ModifyStatDefinition.ToEffect</c> refuses a
/// <c>PlayerStat</c> that is not a member (M3-02b), which closes the only path a designer can
/// reach, and it is also the only path any production code takes — that conversion is the single
/// non-test constructor of a <c>ModifyStat</c> in the project. <b>Widening
/// <c>IEffectHandler&lt;T&gt;</c> with a <c>CanApply(TEffect)</c> member was considered here and
/// deliberately not built:</b> for every primitive that exists it would duplicate a check with
/// worse diagnostics (a node id, where the authoring door names the asset file and the field), and
/// most implementations would be <c>=&gt; true</c> — a member that is nearly always true is a
/// rubber stamp, which would make this promise look structural while still resting on each author.
/// <b>The primitive that should pay for it is the first one whose applicability is a
/// <em>run-scoped</em> question</b> — one no authoring check could answer because the answer
/// depends on the run rather than on the asset — and that task will have a second implementer to
/// write the member against. Until then, a new primitive owes its own door, and this paragraph is
/// the rule it owes it to.
/// </para>
/// <para>
/// <b>Everything is an array or a count, and nothing here allocates after construction.</b>
/// <see cref="Available"/> is walked by M3-04 once per pick against a
/// <see cref="bool"/><c>[]</c> of flags, and the walk order is the tree's own — branch 0 tier 1 in
/// authored order, then tier 2, then branch 1 — because M3-04 makes one draw against it and the
/// same seed against the same tree state has to produce the same offer (AR §18.3).
/// </para>
/// <para>
/// <b>The one exception is <see cref="OnSplashInstalled"/></b>, which rebuilds those arrays when
/// CH §5.4's borrowed branch arrives: once per run, at a moment the game is paused, and never on a
/// frame path. Everything else here is written against <see cref="TreeRules.BranchCount"/> and
/// <see cref="TreeRules.Count"/> — the <em>run's</em> numbers — so the fourth branch is a value
/// rather than a case.
/// </para>
/// </remarks>
public sealed class SkillTree
{
    /// <summary>Every node of the tree in tree order, with the position each sits at.</summary>
    /// <remarks>
    /// <para>
    /// Flattened once at construction so that the two questions asked per candidate — what kind is
    /// it, and which branch and tier is it in — are array reads rather than two dictionary probes
    /// through <see cref="Rules"/>. The order <em>is</em> the contract
    /// <see cref="Available"/> states.
    /// </para>
    /// <para>
    /// <b>Not <see langword="readonly"/>, and <see cref="OnSplashInstalled"/> is the only reason.</b>
    /// A branch borrowed mid-run appends nodes after the primary's, so the four arrays below are
    /// rebuilt once, at a moment the game is paused — never on a frame path and never twice.
    /// </para>
    /// </remarks>
    private Node[] _nodes;

    /// <summary>Where each id sits in <see cref="_nodes"/>, for the questions asked by id.</summary>
    private Dictionary<ContentId, int> _index;

    /// <summary>One flag per node, indexed as <see cref="_nodes"/> is.</summary>
    private bool[] _taken;

    /// <summary>How many nodes of each branch are taken — the left-hand side of every tier gate.</summary>
    /// <remarks>
    /// One entry per branch <em>this run</em> has (<see cref="TreeRules.BranchCount"/>), so the
    /// borrowed branch's picks are counted at index 3 and nowhere else.
    /// </remarks>
    private int[] _takenInBranch;

    /// <summary>The ids in take order, which is what a save writes down.</summary>
    private readonly List<ContentId> _takenIds;

    /// <summary>The same list as a view, wrapped once so <see cref="TakenIds"/> allocates nothing.</summary>
    private readonly ReadOnlyCollection<ContentId> _takenIdsView;

    /// <summary>
    /// One flag per node, indexed as <see cref="_nodes"/> is — GD §13.3's Banish, the third flag
    /// beside <see cref="_taken"/> (M6-02b rule 4).
    /// </summary>
    private bool[] _banished;

    /// <summary>The banished ids in banish order, which is what a save writes down.</summary>
    private readonly List<ContentId> _banishedIds;

    /// <summary>Wrapped once, for <see cref="_takenIdsView"/>'s reason.</summary>
    private readonly ReadOnlyCollection<ContentId> _banishedIdsView;

    private readonly EffectRegistry _effects;
    private readonly IDomainEvents _events;

    /// <param name="rules">The class's tree, resolved and cross-checked.</param>
    /// <param name="effects">
    /// Which handler answers for which effect, this run. Every effect in the tree is checked
    /// against it here — see this class's remarks for exactly what that buys.
    /// </param>
    /// <param name="events">Where <see cref="NodeTaken"/> goes.</param>
    /// <exception cref="ArgumentNullException">Any of the three is null.</exception>
    /// <exception cref="KeyNotFoundException">
    /// A node carries an effect nothing in this run knows how to apply. The message names the node
    /// and the effect type, because the fix is a <c>Register</c> line and the useful half of the
    /// diagnostic is <em>which</em> primitive was never registered.
    /// </exception>
    public SkillTree(TreeRules rules, EffectRegistry effects, IDomainEvents events)
    {
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _events = events ?? throw new ArgumentNullException(nameof(events));

        _nodes = Flatten(rules);
        _index = new Dictionary<ContentId, int>(_nodes.Length);
        _taken = new bool[_nodes.Length];
        _takenInBranch = new int[rules.BranchCount];
        _takenIds = new List<ContentId>(_nodes.Length);
        _takenIdsView = new ReadOnlyCollection<ContentId>(_takenIds);
        _banished = new bool[_nodes.Length];
        _banishedIds = new List<ContentId>();
        _banishedIdsView = new ReadOnlyCollection<ContentId>(_banishedIds);

        Reindex();

        RequireHandlers(_nodes, from: 0);
    }

    /// <summary>The class's tree, resolved and cross-checked — the shape and the rules.</summary>
    public TreeRules Rules { get; }

    /// <summary>How many nodes have been taken, this run.</summary>
    public int TakenCount => _takenIds.Count;

    /// <summary>Whether every node of the run's tree has been taken.</summary>
    /// <remarks>
    /// <para>
    /// What M3-08 reads to know that a pick has nothing to buy and becomes Overflow instead
    /// (CH §5.2). It is a comparison against <see cref="TreeRules.Count"/> rather than a flag,
    /// because a flag would be a second place the count is recorded.
    /// </para>
    /// <para>
    /// <b>It counts the borrowed branch too, which is what makes it widen for free.</b> A run that
    /// completes its own twelve and its borrowed seven is full at nineteen; one that never splashed
    /// is full at twelve. <c>LevelUpFlow</c> is untouched by that — it asks
    /// <c>OfferGenerator.Draw</c> what is available and never this.
    /// </para>
    /// </remarks>
    public bool IsFull => _takenIds.Count == _nodes.Length;

    /// <summary>How many nodes of branch <paramref name="branch"/> have been taken.</summary>
    /// <param name="branch">
    /// The branch index, 0-based and into <em>this run's</em> branches — so
    /// <see cref="TreeRules.SplashBranch"/> is legal once one is installed and not before.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="branch"/> is not an index into this run's branches.
    /// </exception>
    public int TakenInBranch(int branch)
    {
        if (branch < 0 || branch >= _takenInBranch.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(branch),
                branch,
                $"Branches are indexed from 0 and this run has {_takenInBranch.Length}.");
        }

        return _takenInBranch[branch];
    }

    /// <summary>How many <see cref="SkillKind.Active"/> nodes the player owns.</summary>
    /// <remarks>
    /// Counted here because this is the object that knows a node was taken, and read by M3-06's
    /// runner and M3-10a's four slots. Nothing in this class listens to
    /// <see cref="NodeTaken"/> to maintain it — core has no business subscribing to its own
    /// events, which is <c>RunSession</c>'s own remark on <c>PlayerDied</c>.
    /// </remarks>
    public int OwnedActives { get; private set; }

    /// <summary>The ids in the order they were taken — a read-only view, never a copy.</summary>
    /// <remarks>
    /// Take order, because that is what a save writes down and what <see cref="Restore"/> replays:
    /// the gates were satisfied in this order once, so replaying it satisfies them again
    /// (M3-01b rule 5).
    /// </remarks>
    public IReadOnlyList<ContentId> TakenIds => _takenIdsView;

    /// <summary>Whether <paramref name="id"/> has been taken this run.</summary>
    /// <remarks>
    /// False for an id that is not in this tree, rather than throwing, for the reason
    /// <see cref="TreeRules.IsKeystone"/> gives: a caller filtering candidates wants an answer, and
    /// a stranger is not taken.
    /// </remarks>
    public bool IsTaken(ContentId id) => _index.TryGetValue(id, out int ordinal) && _taken[ordinal];

    /// <summary>Whether <paramref name="id"/> has been banished this run.</summary>
    /// <remarks>False for a stranger, as <see cref="IsTaken"/> is and for its reason.</remarks>
    public bool IsBanished(ContentId id) =>
        _index.TryGetValue(id, out int ordinal) && _banished[ordinal];

    /// <summary>The banished ids, in banish order — a read-only view, never a copy.</summary>
    /// <remarks>What <c>RunRecorder</c> writes and <see cref="RestoreBanished"/> replays.</remarks>
    public IReadOnlyList<ContentId> BanishedIds => _banishedIdsView;

    /// <summary>
    /// Whether <paramref name="id"/> may be banished: a node of this tree, not taken, not already
    /// banished. Availability is not asked (M6-02b rule 5).
    /// </summary>
    /// <remarks>
    /// The question <see cref="Banish"/> would refuse on, asked without throwing, so a caller that
    /// must take money before the banish lands can ask first — <c>SanctumShop.Banish</c>. False for
    /// a stranger.
    /// </remarks>
    public bool CanBanish(ContentId id) =>
        _index.TryGetValue(id, out int ordinal) && !_taken[ordinal] && !_banished[ordinal];

    /// <summary>
    /// Fills <paramref name="destination"/> with every node <see cref="CanBanish"/> would accept, in
    /// tree order, and returns how many were written.
    /// </summary>
    /// <remarks>
    /// <see cref="Available"/>'s shape and its refusal of a short buffer, for its reason; allocates
    /// nothing. Rule 5: every untaken node, available or not.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is shorter than <see cref="TreeRules.Count"/>.
    /// </exception>
    public int Banishable(Span<ContentId> destination)
    {
        RequireWholeTree(destination.Length);

        int written = 0;

        for (int ordinal = 0; ordinal < _nodes.Length; ordinal++)
        {
            if (!_taken[ordinal] && !_banished[ordinal])
            {
                destination[written] = _nodes[ordinal].Spec.Id;
                written++;
            }
        }

        return written;
    }

    /// <summary>
    /// Takes <paramref name="id"/> out of this run's pool for good (GD §13.3), and publishes
    /// <see cref="NodeBanished"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A third flag, not a filter on the generator</b> (M6-02b rule 4): <see cref="Check"/>
    /// closes for a banished ordinal, so <see cref="IsAvailable"/>, <see cref="Available"/> and
    /// therefore <c>OfferGenerator</c> all agree without the generator being edited.
    /// </para>
    /// <para>
    /// <b>Any untaken node, available or not, and the consequences are the player's</b> (rule 5).
    /// Banishing an Upgrade's parent makes the Upgrade unreachable, and banishing enough of a
    /// branch makes its Keystone unreachable; <c>LevelUpFlow.Open</c> already spends a pick with
    /// nothing to draw on Overflow. Nothing here takes that back — GD §13.3 says <em>permanently</em>.
    /// </para>
    /// <para>
    /// Public, sealed one layer out at <c>RunState.Tree</c>, for <see cref="Take"/>'s reason. The
    /// price is <c>SanctumShop</c>'s; this is the verb.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is <c>default</c>, not a node of this tree, already taken, or already
    /// banished.
    /// </exception>
    public void Banish(ContentId id)
    {
        if (id.Value is null)
        {
            throw new ArgumentException("default(ContentId) names no node to banish.", nameof(id));
        }

        if (!_index.TryGetValue(id, out int ordinal))
        {
            throw new ArgumentException(
                $"'{id}' is not a node of '{Rules.Tree.Id}', so there is nothing to banish.",
                nameof(id));
        }

        if (_taken[ordinal])
        {
            throw new ArgumentException(
                $"'{id}' is already taken. Banish removes a node from the offers; it does not "
                    + "take one back (GD §13.3, and CH §7 deleted respec).",
                nameof(id));
        }

        if (_banished[ordinal])
        {
            throw new ArgumentException($"'{id}' is already banished.", nameof(id));
        }

        _banished[ordinal] = true;
        _banishedIds.Add(id);

        _events.Publish(new NodeBanished(id));
    }

    /// <summary>
    /// Replays a save's banishes, silently. Drops what it cannot apply (M6-02b rule 9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>After <see cref="Restore"/>, never before</b>: an id in both lists is dropped from this
    /// one — the take is the stronger fact and a save carrying both is corrupt rather than stale —
    /// and that is only knowable once the takes are in.
    /// </para>
    /// <para>
    /// <b>Drops rather than throws</b>, unlike <see cref="Restore"/>: an id this build no longer
    /// ships, a duplicate, or one also taken is skipped — <c>SkillRunner.Restore</c>'s silent-drop
    /// rule (M3-07b rule 6). A banish narrows the offers and grants nothing, so losing one costs
    /// the player 40 Essence of sculpting rather than a broken tree. <b>Silent</b>: no
    /// <see cref="NodeBanished"/> before <c>RunStarted</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="banished"/> is null.</exception>
    public void RestoreBanished(IReadOnlyList<ContentId> banished)
    {
        if (banished is null)
        {
            throw new ArgumentNullException(nameof(banished));
        }

        for (int i = 0; i < banished.Count; i++)
        {
            ContentId id = banished[i];

            if (id.Value is null
                || !_index.TryGetValue(id, out int ordinal)
                || _taken[ordinal]
                || _banished[ordinal])
            {
                continue;
            }

            _banished[ordinal] = true;
            _banishedIds.Add(id);
        }
    }

    /// <summary>Whether <paramref name="id"/> may be taken right now.</summary>
    /// <remarks>
    /// CH §5's gating in one question: not already taken, enough of its own branch taken, and — for
    /// an <see cref="SkillKind.Upgrade"/> — its parent owned. False for a stranger, as
    /// <see cref="IsTaken"/> is. The same predicate <see cref="Available"/> walks, so the two cannot
    /// disagree.
    /// </remarks>
    public bool IsAvailable(ContentId id) =>
        _index.TryGetValue(id, out int ordinal) && Check(ordinal) == Gate.Open;

    /// <summary>
    /// Fills <paramref name="destination"/> with every node that may be taken right now, in tree
    /// order, and returns how many were written.
    /// </summary>
    /// <param name="destination">
    /// Where the ids go. Must be able to hold the whole tree — see the remarks.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The order is load-bearing rather than cosmetic.</b> Branch 0's tier 1 in authored order,
    /// then its tier 2, then branch 1, and a borrowed branch last of all: M3-04 walks this with one
    /// draw per pick, so the same seed against the same tree state has to yield the same offer
    /// (AR §18.3's <em>one draw whatever it then finds</em>). Anything that reorders this changes
    /// what every seed means — which is why <see cref="OnSplashInstalled"/> appends rather than
    /// interleaves.
    /// </para>
    /// <para>
    /// <b>A destination too short to hold the tree is refused rather than truncated.</b> A caller
    /// asking what is available wants all of it, and a short buffer would silently narrow the offer
    /// — which is a content-shaped bug with no symptom. Sizing by <see cref="TreeRules.Count"/> is
    /// the contract; twenty-seven is the number until a branch is borrowed and thirty-four after,
    /// and with layered tiers the answer can be six on the first pick. <b>A buffer sized before a
    /// splash is one the tree now refuses</b>, and <c>OfferGenerator</c> is the one caller that
    /// holds one across the moment — it regrows on the next draw.
    /// </para>
    /// <para>
    /// Allocates nothing: a walk over arrays built at construction against the flags, with no
    /// enumerator and no closure. <c>Available_AllocatesNothing</c> is the row, and it measures
    /// with <c>AllocationAssert.None</c> because nothing else can on Unity's Mono (Traps §7).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is shorter than <see cref="TreeRules.Count"/>.
    /// </exception>
    public int Available(Span<ContentId> destination)
    {
        RequireWholeTree(destination.Length);

        int written = 0;

        for (int ordinal = 0; ordinal < _nodes.Length; ordinal++)
        {
            if (Check(ordinal) == Gate.Open)
            {
                destination[written] = _nodes[ordinal].Spec.Id;
                written++;
            }
        }

        return written;
    }

    /// <summary>
    /// Takes <paramref name="id"/>: records it, puts its effects into force, and publishes
    /// <see cref="NodeTaken"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Gate, record, effects, event — and the event is last on purpose</b>, so a handler reading
    /// <see cref="TakenCount"/> or the player's damage from inside it sees the node it is being told
    /// about. <c>EnemyDied</c>'s ordering, and the same reason.
    /// </para>
    /// <para>
    /// <b>The refusal names which gate failed</b> — taken, tier, keystone or parent — because
    /// M3-08's <c>ChooseOffer</c> is the caller, and a wrong index there should read as <em>what</em>
    /// was wrong rather than as "that node is not available".
    /// </para>
    /// <para>
    /// Public, and the seal is one layer out at <c>RunState.Tree</c>, which is <c>internal</c>: a
    /// public handle on this object would let a view grant the player a node (AR §18.2, rule 8).
    /// </para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException"><paramref name="id"/> is not in this tree.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="id"/> is not available — already taken, not enough of its branch taken, a
    /// keystone short of its branch, or an upgrade whose parent is not owned.
    /// </exception>
    public void Take(ContentId id)
    {
        int ordinal = Locate(id);
        Gate gate = Check(ordinal);

        if (gate != Gate.Open)
        {
            throw new InvalidOperationException(Refusal(ordinal, gate));
        }

        Record(ordinal, publish: true);
    }

    /// <summary>
    /// Replays <paramref name="takenInOrder"/> as a sequence of takes, silently — what a resumed
    /// run's picks come back through.
    /// </summary>
    /// <param name="takenInOrder">
    /// The ids a save carried, in take order. Empty is ordinary: it is every run that has not taken
    /// a node.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>It runs before <c>Health.Restore(hp, shield)</c>, and that is an AR §18.1 row rather than
    /// a preference.</b> A <c>+20 max HP</c> node has to be on the stat before the absolute hit
    /// points saved under it are clamped against the maximum, or a run saved at 150 of 160 comes
    /// back at 140 of 160 — the player silently loses the difference, once per resume, and the only
    /// visible symptom is a bar that is slightly shorter than it was. It is also why this task
    /// depends on M3-01b rather than the other way round.
    /// </para>
    /// <para>
    /// <b>Silent, because nothing may publish before <c>RunStarted</c></b> — a
    /// <see cref="NodeTaken"/> raised here would reach a HUD that has not drawn yet, and would
    /// announce as news a node the player picked in a previous session.
    /// <c>LevelTracker.Restore</c>'s rule, one object over.
    /// </para>
    /// <para>
    /// <b>The gates are enforced, and an order that breaks one is refused.</b> A save whose own
    /// picks its own rules would not allow is corrupt rather than merely old, and a tree that
    /// disagrees with itself is worse than a refused resume: every later offer would be drawn
    /// against gating that is already false. A node this build no longer ships throws instead —
    /// that is content validation's answer at <c>Start</c> and M2-13a rule 10's argument for not
    /// refusing it down in the DTO, where an unknown id cannot be told from a typo.
    /// </para>
    /// <para>
    /// <b>Public, where the spec drafted it <c>internal</c>.</b> <c>Soulvail.Tests.Core</c> has no
    /// <c>InternalsVisibleTo</c> and deliberately never will (AR §18.2, M0-10), so an
    /// <c>internal</c> member here could not be tested at the level the Files table assigns it to.
    /// It costs nothing to open, and that is the point rather than a concession: <see cref="Take"/>
    /// is already public, so anything holding this object could grant every node in a legal order
    /// by hand — an <c>internal</c> <see cref="Restore"/> beside a public <see cref="Take"/> would
    /// be a lock on the back door of an open front one. The seal that matters is one layer out, at
    /// <c>RunState.Tree</c>, which is <c>internal</c>: nothing outside core can reach a
    /// <see cref="SkillTree"/> to call either. This is M3-05's shape exactly — <c>PlayerStats</c> is
    /// public and hands out live <c>Stat</c>s, sealed by <c>RunState.Effects</c> being
    /// <c>internal</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="takenInOrder"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// An entry is not available at the point it is replayed, so the saved order breaks a gate the
    /// same tree once let through.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// An entry is not a node of this tree — content this build no longer ships.
    /// </exception>
    public void Restore(IReadOnlyList<ContentId> takenInOrder)
    {
        if (takenInOrder is null)
        {
            throw new ArgumentNullException(nameof(takenInOrder));
        }

        for (int i = 0; i < takenInOrder.Count; i++)
        {
            ContentId id = takenInOrder[i];

            int ordinal = Locate(id);
            Gate gate = Check(ordinal);

            if (gate != Gate.Open)
            {
                throw new ArgumentException(
                    $"The saved take order is not one this tree would have allowed: entry {i} is "
                        + Refusal(ordinal, gate),
                    nameof(takenInOrder));
            }

            Record(ordinal, publish: false);
        }
    }

    /// <summary>
    /// Rebuilds the flattened walk to include the branch <see cref="Rules"/> has just borrowed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Called by <see cref="TreeRules"/>' owner immediately after
    /// <see cref="TreeRules.InstallSplash"/>, never independently.</b> Between the two calls the
    /// pair disagrees about how many nodes the run has, so they are one step of one caller. It is a
    /// method rather than a subscription for the reason core never subscribes to its own events
    /// (<c>RunSession</c>'s own remark on <c>PlayerDied</c>).
    /// </para>
    /// <para>
    /// <b>The borrowed branch is appended after the primary's three, never interleaved, and that is
    /// the seed contract rather than a preference.</b> <see cref="Available"/>'s walk order is what
    /// M3-04 draws against, so the same seed against the same tree state has to yield the same
    /// offer (AR §18.3). Appending means every seed's meaning <em>before</em> the splash is
    /// unchanged and every seed's meaning after it is a function of one authored order.
    /// </para>
    /// <para>
    /// <b>The flags already set are copied across by ordinal, because the primary's ordinals do not
    /// move.</b> That is the whole of what appending buys: <see cref="_taken"/> and
    /// <see cref="_takenInBranch"/> grow, and every entry in them still means what it meant.
    /// </para>
    /// <para>
    /// <b>The borrowed nodes are swept for handlers before anything is assigned</b>, for the
    /// constructor's reason one moment later in the run: <see cref="Record"/> writes the node down
    /// before it applies the effects, so a borrowed node carrying a primitive this run never
    /// registered would leave the node owned and its effects half on. Swept before the commit, a
    /// missing <c>Register</c> line leaves this object exactly as it was.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No branch is installed on <see cref="Rules"/>, so there is nothing to rebuild for.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// A borrowed node carries an effect nothing in this run knows how to apply.
    /// </exception>
    public void OnSplashInstalled()
    {
        if (Rules.SplashBranch == TreeRules.NoSplash)
        {
            throw new InvalidOperationException(
                "No branch has been borrowed, so there is nothing to rebuild. This is called by "
                    + "whoever called TreeRules.InstallSplash, immediately after it and never on "
                    + "its own.");
        }

        if (_nodes.Length == Rules.Count)
        {
            return;
        }

        Node[] widened = Flatten(Rules);

        RequireHandlers(widened, from: _nodes.Length);

        var taken = new bool[widened.Length];
        var banished = new bool[widened.Length];
        var takenInBranch = new int[Rules.BranchCount];

        Array.Copy(_taken, taken, _taken.Length);
        Array.Copy(_banished, banished, _banished.Length);
        Array.Copy(_takenInBranch, takenInBranch, _takenInBranch.Length);

        _nodes = widened;
        _taken = taken;
        _banished = banished;
        _takenInBranch = takenInBranch;

        Reindex();
    }

    /// <summary>
    /// Records the node, applies its effects and — for a live take — says so.
    /// </summary>
    /// <remarks>
    /// One method for both callers, because a restore that recorded or applied anything differently
    /// from a take would be a resumed run that is not the run that was saved. The only difference
    /// the two are allowed is the event, which is the parameter.
    /// </remarks>
    private void Record(int ordinal, bool publish)
    {
        Node node = _nodes[ordinal];
        SkillSpec spec = node.Spec;

        // Recorded before the effects go on, which is what the constructor's CanApply sweep exists
        // to make safe: a handler that threw half way down the list would otherwise leave the node
        // owned and its effects partly applied.
        _taken[ordinal] = true;
        _takenInBranch[node.Branch]++;
        _takenIds.Add(spec.Id);

        // **With the SkillSpec as the source** (M3-05 rule 7): it is shared, immutable and taken
        // once per run, so it is a reference stable for the run's life and never confused with
        // another node's. It is also what a future removal would take back by.
        for (int i = 0; i < spec.Effects.Count; i++)
        {
            _effects.Apply(spec.Effects[i], spec);
        }

        if (spec.Kind == SkillKind.Active)
        {
            OwnedActives++;
        }

        if (publish)
        {
            _events.Publish(
                new NodeTaken(spec.Id, spec.Kind, node.Branch, node.Tier, _takenIds.Count));
        }
    }

    /// <summary>
    /// CH §5's gating, asked once and in one place so that <see cref="IsAvailable"/>,
    /// <see cref="Available"/>, <see cref="Take"/> and <see cref="Restore"/> cannot disagree.
    /// </summary>
    /// <remarks>
    /// It returns <em>which</em> gate closed rather than a bool, so the two throwing callers can
    /// each say so in their own words without either of them re-deriving the rule — and so the
    /// happy path builds no string at all.
    /// </remarks>
    private Gate Check(int ordinal)
    {
        if (_taken[ordinal])
        {
            return Gate.Taken;
        }

        // M6-02b rule 4: closed for good, whatever the branch count says.
        if (_banished[ordinal])
        {
            return Gate.Banished;
        }

        Node node = _nodes[ordinal];
        SkillSpec spec = node.Spec;

        // Asked of TreeRules rather than recomputed here, at the cost of two dictionary probes per
        // candidate: the keystone exception to the tier rule is the kind of arithmetic that gets
        // copied and then drifts, and an offer is drawn once per pick rather than once per frame.
        if (_takenInBranch[node.Branch] < Rules.RequiredTakenInBranch(spec.Id))
        {
            return spec.Kind == SkillKind.Keystone ? Gate.Keystone : Gate.Tier;
        }

        // The kind is read straight off the spec rather than through TryGetParent, which would be a
        // third probe to learn something already in hand.
        if (spec.Kind == SkillKind.Upgrade && !IsTaken(spec.ParentId))
        {
            return Gate.Parent;
        }

        return Gate.Open;
    }

    /// <summary>The ordinal <paramref name="id"/> sits at, or a refusal naming the tree.</summary>
    private int Locate(ContentId id)
    {
        if (!_index.TryGetValue(id, out int ordinal))
        {
            throw new KeyNotFoundException(
                $"'{id}' is not a node of '{Rules.Tree.Id}'. A node this build no longer ships is "
                    + "content validation's answer at Start rather than something to absorb here.");
        }

        return ordinal;
    }

    /// <summary>Why <paramref name="gate"/> closed, in words, for whoever is about to throw.</summary>
    /// <remarks>
    /// Built only on a failure path, so the gate check itself stays allocation-free.
    /// </remarks>
    private string Refusal(int ordinal, Gate gate)
    {
        Node node = _nodes[ordinal];
        SkillSpec spec = node.Spec;

        return gate switch
        {
            Gate.Taken => $"'{spec.Id}' is already taken; nothing in V1 takes a node twice.",

            Gate.Banished =>
                $"'{spec.Id}' was banished in the Sanctum and is out of this run for good (GD §13.3).",

            Gate.Tier =>
                $"'{spec.Id}' is at branch {node.Branch}, tier {node.Tier}, which needs "
                    + $"{Rules.RequiredTakenInBranch(spec.Id)} nodes of that branch taken and has "
                    + $"{_takenInBranch[node.Branch]} (CH §5: tier N requires N − 1).",

            Gate.Keystone =>
                $"'{spec.Id}' is branch {node.Branch}'s keystone, which needs every other node of "
                    + $"that branch — {Rules.RequiredTakenInBranch(spec.Id)} of them — and has "
                    + $"{_takenInBranch[node.Branch]} (CH §5: all preceding).",

            Gate.Parent =>
                $"'{spec.Id}' is an upgrade to '{spec.ParentId}', which is not owned. An Upgrade is "
                    + "only offered once you own the skill it improves (CH §4).",

            // Unreachable while Gate.Open is filtered out by both callers, and loud rather than
            // silent for `PlayerStats.Resolve`'s reason: a member added here without a line would
            // otherwise refuse a node with an empty explanation.
            _ => throw new ArgumentOutOfRangeException(
                nameof(gate),
                gate,
                "No refusal is written for this gate."),
        };
    }

    /// <summary>
    /// <see cref="Available"/>'s refusal, shared with <see cref="Banishable"/>: a buffer that could
    /// not hold the whole tree would silently narrow the answer rather than fail.
    /// </summary>
    private void RequireWholeTree(int length)
    {
        if (length < _nodes.Length)
        {
            throw new ArgumentException(
                $"destination holds {length} and this tree has {_nodes.Length} nodes. "
                    + "Size it to the tree: a buffer that could not hold every node would "
                    + "silently narrow the answer rather than fail.",
                "destination");
        }
    }

    /// <summary>Rebuilds <see cref="_index"/> from <see cref="_nodes"/> as it now stands.</summary>
    /// <remarks>
    /// Rebuilt whole rather than appended to, even though appending would be correct — the
    /// primary's ordinals never move — because a map built once from the array it maps cannot drift
    /// from it, and this runs twice a run at most.
    /// </remarks>
    private void Reindex()
    {
        _index.Clear();

        for (int ordinal = 0; ordinal < _nodes.Length; ordinal++)
        {
            _index.Add(_nodes[ordinal].Spec.Id, ordinal);
        }
    }

    /// <summary>
    /// Refuses the tree if anything from <paramref name="from"/> on carries an effect this run has
    /// no handler for.
    /// </summary>
    /// <param name="nodes">The flattened walk to sweep — the live one, or the widened one.</param>
    /// <param name="from">
    /// The first ordinal to ask about. 0 at construction; the old length at a splash, because
    /// everything below it was swept when the run started and the specs have not moved.
    /// </param>
    /// <remarks>
    /// Both lists, take and cast: an Active's cast effects are applied by M3-06's runner rather
    /// than by <see cref="Take"/>, and the first cast of a skill taken twenty minutes earlier is
    /// the worst possible moment to discover a missing <c>Register</c> line.
    /// </remarks>
    private void RequireHandlers(Node[] nodes, int from)
    {
        for (int ordinal = from; ordinal < nodes.Length; ordinal++)
        {
            SkillSpec spec = nodes[ordinal].Spec;

            RequireHandlers(spec, spec.Effects, "takes");

            if (spec.Active is not null)
            {
                RequireHandlers(spec, spec.Active.OnCast, "casts");
            }
        }
    }

    private void RequireHandlers(SkillSpec spec, IReadOnlyList<IEffect> effects, string when)
    {
        for (int i = 0; i < effects.Count; i++)
        {
            IEffect effect = effects[i];

            if (_effects.CanApply(effect))
            {
                continue;
            }

            throw new KeyNotFoundException(
                $"'{spec.Id}' {when} a '{effect.GetType().Name}' and no handler is registered for "
                    + "it, so taking the node would throw part way through. A primitive is a file "
                    + "and a Register line in RunSession.Start; nothing about this run has been "
                    + "announced.");
        }
    }

    /// <summary>
    /// Flattens the run's tree into tree order, which is the order <see cref="Available"/> promises.
    /// </summary>
    /// <remarks>
    /// <b>Every branch the <em>run</em> has, through <see cref="TreeRules.Branch"/>.</b> A borrowed
    /// branch is index 3, so it falls out of this loop appended after the primary's three with no
    /// special case — which is the whole reason <see cref="TreeRules"/> hands one back as a
    /// <see cref="SkillBranchSpec"/> rather than as a shape of its own.
    /// </remarks>
    private static Node[] Flatten(TreeRules rules)
    {
        var nodes = new Node[rules.Count];
        int ordinal = 0;

        for (int b = 0; b < rules.BranchCount; b++)
        {
            SkillBranchSpec branch = rules.Branch(b);

            for (int t = 1; t <= branch.TierCount; t++)
            {
                IReadOnlyList<ContentId> tier = branch.Tier(t);

                for (int i = 0; i < tier.Count; i++)
                {
                    nodes[ordinal] = new Node(rules.Skill(tier[i]), b, t);
                    ordinal++;
                }
            }
        }

        return nodes;
    }

    /// <summary>One node and where it sits — what a gate check needs without a probe.</summary>
    /// <remarks>
    /// A <see langword="readonly"/> struct in an array, so the whole flattened tree is one
    /// allocation and nothing boxes on the way out of it.
    /// </remarks>
    private readonly struct Node
    {
        internal Node(SkillSpec spec, int branch, int tier)
        {
            Spec = spec;
            Branch = branch;
            Tier = tier;
        }

        /// <summary>The node itself — its kind, its effects, its parent.</summary>
        internal SkillSpec Spec { get; }

        /// <summary>The branch index, 0-based.</summary>
        internal int Branch { get; }

        /// <summary>The tier, 1-based.</summary>
        internal int Tier { get; }
    }

    /// <summary>Which of CH §5's gates is closed, or none.</summary>
    /// <remarks>
    /// Private, because it is a way of explaining a refusal rather than something a caller decides
    /// anything on — M3-08 gets a message, M3-09d gets <see cref="IsAvailable"/>, and neither has a
    /// reason to branch on which gate it was.
    /// </remarks>
    private enum Gate
    {
        /// <summary>The node may be taken.</summary>
        Open,

        /// <summary>It is already owned.</summary>
        Taken,

        /// <summary>It was banished — GD §13.3, M6-02b.</summary>
        Banished,

        /// <summary>Not enough of its branch is taken for its tier.</summary>
        Tier,

        /// <summary>It is a keystone and its branch is not finished.</summary>
        Keystone,

        /// <summary>It is an upgrade and the skill it improves is not owned.</summary>
        Parent,
    }
}
