using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Progression;

// A level-up's offer: three nodes drawn from what the tree makes available, weighted for variety.
// The tree says what *may* be taken (`SkillTree.Available`); this says which of them the player is
// shown. Nothing here decides *when* to draw — that is M3-08, and the draw is lazy.

/// <summary>
/// Draws a level-up's offer: up to <c>count</c> distinct available nodes, weighted for variety,
/// from the <see cref="IRandom.Offers"/> stream and no other.
/// </summary>
/// <remarks>
/// <para>
/// <b>The candidate set is <see cref="SkillTree.Available"/> and nothing else</b> (CH §5.1,
/// GD §13.1). Never the whole tree, so a node whose tier gate is shut or whose parent is not owned
/// cannot occupy one of three slots the player was owed; and <c>count</c> is clamped to what is
/// actually there, so a nearly-full tree offers two rather than one live node and two blanks.
/// </para>
/// <para>
/// <b>One draw per pick, whatever the walk then finds</b> (AR §18.3). Each pick makes exactly one
/// <see cref="IRandomStream.NextFloat"/> and then walks the remaining candidates' cumulative
/// weights in <see cref="SkillTree.Available"/>'s order; the chosen one is removed and the next pick
/// walks what is left. Consumption is therefore a function of the pick count alone — three offers
/// cost three draws whether the tree holds six candidates or twenty-seven — which is the property
/// both spawners already owe for the same reason. Consumption that depended on how full the tree
/// was is the one thing a seed cannot survive: the same seed would replay differently the moment
/// the player had taken a different node an hour earlier.
/// </para>
/// <para>
/// <b>The order of the walk is the tree's, and it is load-bearing.</b>
/// <see cref="SkillTree.Available"/> fills in branch-then-tier-then-authored order and this class
/// never sorts, shuffles or reorders what it is handed — removal is a compaction, so the survivors
/// keep their relative order, the same rule enemy despawn already follows (AR §18.3). Anything that
/// reorders the candidates changes what every seed means.
/// </para>
/// <para>
/// <b>Passed a stream, never holding one</b> (ADR-0011, <c>RunSession</c>'s shape for
/// <see cref="IRandom.Spawn"/>). Offers are stream 1 and nothing else draws on it, so a reroll
/// (M6-02) or a Pact (M6-05) cannot shift what the next stage is made of, and a wave cannot change
/// what the next level-up screen shows.
/// </para>
/// <para>
/// <b>Allocates nothing after construction, bar one regrow a run.</b> Two buffers sized
/// <see cref="TreeRules.Count"/> and a per-branch counter each on the stack, refilled per call. A
/// level-up screen is a moment the frame is already spending on UI, but the buffers exist so that a
/// reroll — a second <see cref="Draw"/> against the same generator — costs nothing either. The
/// exception is a run that borrows CH §5.4's branch: the tree grows, and the first draw after it
/// replaces both buffers once (see <see cref="Draw"/>).
/// </para>
/// <para>
/// <b>Everything is indexed against <see cref="TreeRules.BranchCount"/>, which is the run's number
/// of branches, and never against <see cref="SkillTreeSpec.BranchCount"/>, which is the class's.</b>
/// The two are equal until a branch is borrowed, and the difference is what made the borrowed
/// branch fail here rather than be refused where it was chosen.
/// </para>
/// <para>
/// <b>Which stat a node moves is not read here.</b> The generator sees kinds and branches. A weight
/// on <em>"the player has no damage yet"</em> would be a second opinion about balance expressed
/// inside a draw, where nobody would look for it; M3-12's tree layout is where that opinion belongs.
/// </para>
/// <para>
/// <b>Built once beside the tree, and the pairing is checked on every call.</b> M3-08 builds a
/// generator over the same <see cref="TreeRules"/> the run's <see cref="SkillTree"/> was built over.
/// See <see cref="Draw"/> for why that is enforced rather than assumed.
/// </para>
/// </remarks>
public sealed class OfferGenerator
{
    /// <summary>How many nodes a level-up offers: three (CH §5.1).</summary>
    /// <remarks>
    /// A default rather than a constant the code reads, because GD §13.4's Vigil offers two — that
    /// is M6-06 passing 2 to <see cref="Draw"/>, not a flag here.
    /// </remarks>
    public const int DefaultOfferCount = 3;

    /// <summary>The multiplier per offer already drawn this call from the same branch.</summary>
    public const float SameBranchPenalty = 0.5f;

    /// <summary>The multiplier on an Active while the player owns fewer than two.</summary>
    public const float ActiveBoost = 2f;

    /// <summary>How many Actives are worth having before the boost stops (CH §8 Q3).</summary>
    public const int ActivesWorthBoosting = 2;

    /// <summary>The rules this generator's buffers were sized by, and the tree it answers for.</summary>
    private readonly TreeRules _rules;

    /// <summary>
    /// Everything available at the moment of the call, and then what is left as picks are made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sized by <see cref="TreeRules.Count"/> because <see cref="SkillTree.Available"/> refuses a
    /// destination shorter than the whole tree rather than truncating it — a short buffer would
    /// silently narrow the offer, which is a content-shaped bug with no symptom (M3-03).
    /// </para>
    /// <para>
    /// <b>That count can grow, which is why neither buffer is <see langword="readonly"/>.</b>
    /// <c>LevelUpFlow</c> builds this generator in its own constructor — before a run could have
    /// borrowed CH §5.4's branch — so both are sized from a number that is three branches' worth at
    /// the time and four later. They are regrown in <see cref="Draw"/>, once, on the first pick
    /// after a splash.
    /// </para>
    /// </remarks>
    private ContentId[] _candidates;

    /// <summary>Each remaining candidate's weight for the pick being made, indexed as above.</summary>
    /// <remarks>
    /// Refilled per pick rather than per call: every pick changes at least one branch's drawn count,
    /// so every survivor's weight is a different number the next time round.
    /// </remarks>
    private float[] _weights;

    /// <param name="rules">
    /// The class's tree, resolved and cross-checked — the same instance the <see cref="SkillTree"/>
    /// passed to <see cref="Draw"/> was built over.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public OfferGenerator(TreeRules rules)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));

        _candidates = new ContentId[rules.Count];
        _weights = new float[rules.Count];
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with up to <paramref name="count"/> distinct available
    /// nodes and returns how many were written.
    /// </summary>
    /// <param name="tree">
    /// The run's tree, which must be the one built over this generator's <see cref="TreeRules"/>.
    /// </param>
    /// <param name="offers">The <see cref="IRandom.Offers"/> stream, passed in rather than held.</param>
    /// <param name="count">
    /// How many to offer. Three for a level-up (<see cref="DefaultOfferCount"/>), two for a Vigil
    /// (GD §13.4). Clamped to what is available, so the return value is what to read.
    /// </param>
    /// <param name="destination">Where the ids go. Must hold <paramref name="count"/>.</param>
    /// <remarks>
    /// <para>
    /// <b>Nothing available writes nothing and makes no draw.</b> There is no pick to make one for,
    /// and a draw taken anyway would move the stream by an amount that depended on the state of the
    /// tree — which is the consumption rule read from the other end.
    /// </para>
    /// <para>
    /// <b>The tree is checked against the rules this generator was built over, and that is a
    /// deliberate exception to trusting the composition root.</b> The two objects arrive separately
    /// — the rules once at construction, the tree on every call — so the pairing is re-asserted here
    /// rather than established once, which is <see cref="SkillTree.Restore"/>'s shape rather than
    /// <c>RunState</c>'s. The failure it prevents is asymmetric and silent: only a <em>larger</em>
    /// wrong tree trips <see cref="SkillTree.Available"/>'s short-buffer refusal, so a same-sized or
    /// smaller one writes another class's ids into a correctly sized buffer and returns a plausible
    /// count. The first symptom would be M3-08's <c>ChooseOffer</c> handing
    /// <see cref="SkillTree.Take"/> a node that tree has never heard of, in a run, at the moment the
    /// player taps it — with a message about content validation. One reference comparison per
    /// level-up screen buys the message at the seam that caused it.
    /// </para>
    /// <para>
    /// <b>How many Actives the player owns is read once, at the top.</b> A node drawn into this
    /// offer is not owned — the player has not chosen it, and may not — so
    /// <see cref="ActiveBoost"/> answers the same for all three picks. The <em>drawn</em> term is
    /// the same-branch penalty, which is the one thing this call accumulates.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="tree"/> or <paramref name="offers"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below 1.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is shorter than <paramref name="count"/>, or
    /// <paramref name="tree"/> was not built over this generator's rules.
    /// </exception>
    public int Draw(SkillTree tree, IRandomStream offers, int count, Span<ContentId> destination)
    {
        if (tree is null)
        {
            throw new ArgumentNullException(nameof(tree));
        }

        if (offers is null)
        {
            throw new ArgumentNullException(nameof(offers));
        }

        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "An offer of nothing is not an offer. Do not call this for a level with no pick.");
        }

        if (destination.Length < count)
        {
            throw new ArgumentException(
                $"destination holds {destination.Length} and {count} were asked for. Size it to "
                    + "count: a buffer that could not hold the offer would narrow it silently.",
                nameof(destination));
        }

        RequireOwnTree(tree);

        // **The one allocation this class makes after construction, and it happens at most once a
        // run.** A branch borrowed under CH §5.4 grows the tree the buffers were sized for, and
        // `Available` refuses a destination shorter than the tree rather than truncating it — so a
        // generator built before the splash would fail with a message about a buffer. An offer is
        // drawn once per level and this is not the frame path (AR §14, and `EnemyRegistry`'s own
        // bargain); the next draw finds the buffers already big enough and grows nothing.
        if (_candidates.Length < _rules.Count)
        {
            _candidates = new ContentId[_rules.Count];
            _weights = new float[_rules.Count];
        }

        int remaining = tree.Available(_candidates);
        int picks = count < remaining ? count : remaining;

        // Counters rather than a field, because they mean nothing between calls: a branch's penalty
        // is "already drawn *this call*". Sized by the *run's* branches — three, or four once a
        // branch has been borrowed — and never by `SkillTreeSpec.BranchCount`, which is what a
        // class authors: read as the run's number it made a borrowed node an
        // IndexOutOfRangeException at `drawnPerBranch[branch]++` below. On the stack, so it costs
        // no allocation (AR §7), and at most four.
        Span<int> drawnPerBranch = stackalloc int[_rules.BranchCount];

        int ownedActives = tree.OwnedActives;

        for (int written = 0; written < picks; written++)
        {
            int chosen = Pick(offers, remaining, drawnPerBranch, ownedActives);

            destination[written] = _candidates[chosen];

            _rules.TryLocate(_candidates[chosen], out int branch, out _);
            drawnPerBranch[branch]++;

            // Compacted rather than swapped with the last, so the survivors keep the order
            // `Available` promised: a swap would reorder them and change what every seed means
            // (AR §18.3, and enemy despawn's rule for the same reason).
            for (int i = chosen; i < remaining - 1; i++)
            {
                _candidates[i] = _candidates[i + 1];
            }

            remaining--;
        }

        return picks;
    }

    /// <summary>
    /// CH §8 Q3's soft variety, as a table: what one candidate is worth against the others.
    /// </summary>
    /// <param name="kind">The candidate's kind.</param>
    /// <param name="branch">The candidate's branch index, 0-based.</param>
    /// <param name="drawnPerBranch">How many offers this call has already drawn from each branch.</param>
    /// <param name="ownedActives">How many Actives the player owns — owns, not has been offered.</param>
    /// <remarks>
    /// <para>
    /// <b>Base 1, halved per offer already drawn from the same branch.</b> A third node from one
    /// branch is possible and four times less likely than the first: GD §13.1 wants variety, not a
    /// rule that forbids a build, and a hard "one per branch" would make the third slot a lottery
    /// between two branches the player is not playing.
    /// </para>
    /// <para>
    /// <b>Doubled for an Active while the player owns fewer than
    /// <see cref="ActivesWorthBoosting"/>.</b> A first active is what turns a stat sheet into a
    /// build (CH §4), and CH §8 Q3's own worked example. An Upgrade weighs 1: its parent gate has
    /// already made it a considered offer rather than a random one, so a second thumb on the scale
    /// would be the same argument counted twice.
    /// </para>
    /// <para>
    /// <b>Pure and static, so the table is a test rather than an inference from ten thousand
    /// draws</b> — and so <see cref="Draw"/> asks it rather than inlining it. M3-03 paid the same
    /// price deliberately: two copies of one rule is how they come to disagree.
    /// </para>
    /// <para>
    /// The penalty is applied by repeated multiplication rather than by a power, because the count
    /// is small, the result is then exact in <see cref="float"/>, and a reader can see 1, 0.5, 0.25
    /// in it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="branch"/> is not an index into <paramref name="drawnPerBranch"/>; that
    /// branch's drawn count or <paramref name="ownedActives"/> is negative; or
    /// <paramref name="kind"/> is not a kind this table has a line for.
    /// </exception>
    public static float Weight(
        SkillKind kind,
        int branch,
        ReadOnlySpan<int> drawnPerBranch,
        int ownedActives)
    {
        if (branch < 0 || branch >= drawnPerBranch.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(branch),
                branch,
                $"Branches are indexed from 0 and drawnPerBranch holds {drawnPerBranch.Length}.");
        }

        int drawn = drawnPerBranch[branch];

        if (drawn < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(drawnPerBranch),
                drawn,
                $"Branch {branch} reports {drawn} offers drawn. A count of things that happened is "
                    + "not negative, and treating it as zero would hide the mistake that made it.");
        }

        if (ownedActives < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ownedActives),
                ownedActives,
                "A player owns no fewer than zero Actives.");
        }

        float weight = kind switch
        {
            // The one kind the table has an opinion about.
            SkillKind.Active => ownedActives < ActivesWorthBoosting ? ActiveBoost : 1f,

            SkillKind.Passive or SkillKind.Upgrade or SkillKind.Keystone => 1f,

            // Loud rather than silent, for `PlayerStats.Resolve`'s reason: a fifth kind added
            // without a line here would otherwise weigh 0 and never be offered, and nothing
            // anywhere would say so.
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "No weight is written for this kind."),
        };

        for (int i = 0; i < drawn; i++)
        {
            weight *= SameBranchPenalty;
        }

        return weight;
    }

    /// <summary>
    /// One pick: exactly one draw, then a cumulative walk of the remaining candidates in tree order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The draw is made before the walk and unconditionally</b>, which is the whole of rule 2:
    /// where it lands changes what is chosen and never how much of the stream was spent.
    /// </para>
    /// <para>
    /// <b>The last candidate is the answer the walk starts from, not a failure case.</b>
    /// <see cref="IRandomStream.NextFloat"/> never returns 1, so <c>target</c> is below the total in
    /// exact arithmetic — but the total is a sum of floats and the walk re-adds them in the same
    /// order, so the last comparison can miss by an ulp. Falling to the last candidate is then the
    /// right answer rather than an index nobody wrote to.
    /// </para>
    /// </remarks>
    private int Pick(
        IRandomStream offers,
        int remaining,
        ReadOnlySpan<int> drawnPerBranch,
        int ownedActives)
    {
        float total = 0f;

        for (int i = 0; i < remaining; i++)
        {
            SkillSpec spec = _rules.Skill(_candidates[i]);

            _rules.TryLocate(_candidates[i], out int branch, out _);

            _weights[i] = Weight(spec.Kind, branch, drawnPerBranch, ownedActives);
            total += _weights[i];
        }

        float target = offers.NextFloat() * total;

        float cumulative = 0f;

        for (int i = 0; i < remaining - 1; i++)
        {
            cumulative += _weights[i];

            if (target < cumulative)
            {
                return i;
            }
        }

        return remaining - 1;
    }

    /// <summary>
    /// Refuses a tree this generator's buffers and weights were not built for.
    /// </summary>
    private void RequireOwnTree(SkillTree tree)
    {
        if (ReferenceEquals(tree.Rules, _rules))
        {
            return;
        }

        string what = tree.Rules.Tree.Id == _rules.Tree.Id
            ? $"a second TreeRules over '{_rules.Tree.Id}'"
            : $"the rules for '{tree.Rules.Tree.Id}'";

        throw new ArgumentException(
            $"This generator was built over '{_rules.Tree.Id}' and was handed {what}. A generator "
                + "and the tree it draws from are built from one TreeRules, together, or the offer "
                + "is filled with ids the tree cannot take — and only a bigger wrong tree would "
                + "fail loudly.",
            nameof(tree));
    }
}
