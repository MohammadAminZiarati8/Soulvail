using System;
using System.Collections.Generic;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Progression;

// A run's tree as one run reads it: every id resolved once, every cross-spec rule checked once,
// and the structural questions answered without a walk. The live half — what has been taken, what
// may be — is `SkillTree`, beside it.

/// <summary>
/// One run's tree, resolved and cross-checked: a class's <see cref="SkillTreeSpec"/> plus the
/// <see cref="SkillSpec"/> behind every id in it, optionally one branch borrowed from a second
/// class, and the structural questions <see cref="SkillTree"/>'s gating is written in terms of.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built in <c>RunSession.Start</c>'s validation block, before <c>RunStarted</c>, so an
/// authoring mistake refuses the <em>run</em> rather than the pick.</b> Every failure this
/// constructor can report is a content error — a node nobody authored, a keystone in the wrong
/// place, an upgrade whose parent is somewhere its own gating could never reach — and each of them
/// is a thing a designer can only have done once, at author time. Reported at the moment a player
/// is offered the node it would arrive as a crash in a run, long after the mistake was made;
/// reported here it arrives with nothing announced and nothing standing (ledger row 3, M2-02
/// rule 7).
/// </para>
/// <para>
/// <b>Why the cross-checks are here and not on <see cref="SkillTreeSpec"/>.</b> A tree holds ids,
/// not kinds, so <em>"a Keystone is the sole node of its branch's last tier"</em> cannot be asked
/// without a <see cref="ContentCatalog"/> — and a spec constructor does not take one, deliberately
/// (that type's own remarks say so, to stop anyone adding one). This is the object that has both,
/// and it is built once per run rather than once per offer.
/// </para>
/// <para>
/// <b>The two numbers are based differently, following <see cref="SkillTreeSpec.TryLocate"/>:</b>
/// a branch is a 0-based index into <see cref="SkillTreeSpec.Branches"/>, a tier is CH §5's own
/// 1-based numbering. Every message here quotes them the same way, so <em>"branch 1"</em> is the
/// second branch.
/// </para>
/// <para>
/// <b>One branch index space, and it is the <em>run's</em> rather than the tree's</b> (M5-07a-i
/// rule 1). 0–2 are the primary's, in the order its asset authored them; <b>3 is the branch CH §5.4
/// lets a run borrow from a second class</b>, and it is 3 whichever class and whichever branch it
/// came from. <see cref="BranchCount"/> is the number every branch-shaped member here and on
/// <see cref="SkillTree"/> is indexed against; <see cref="SkillTreeSpec.BranchCount"/> stays 3 and
/// means <em>branches per class</em>, which is still true of every authored tree.
/// <b>A second <see cref="TreeRules"/> beside the first was weighed and refused:</b> every caller
/// asking <em>"how many of this branch are taken"</em> would have to know which object to ask,
/// <c>SkillTree.Check</c> would branch on it, and <c>OfferGenerator</c>'s same-branch penalty would
/// stop being able to tell branch 1 of the primary from branch 1 of the borrowed one. One space
/// with a fourth slot is the change; two objects is a fork.
/// </para>
/// <para>
/// <b>Mutable exactly once, at <see cref="InstallSplash"/>, and never on a frame path.</b> Before
/// that it is what M3-03 built: shared authored data behind one dictionary, and the catalog read by
/// the constructor and not retained. The install takes a second catalog for the same reason the
/// constructor takes one — it resolves ids — and does not retain that either.
/// </para>
/// </remarks>
public sealed class TreeRules
{
    /// <summary>What <see cref="SplashBranch"/> answers while no branch has been borrowed.</summary>
    /// <remarks>
    /// The same −1 <see cref="SkillTreeSpec.TryLocate"/> answers for a node it does not hold, and
    /// deliberately: a caller that forgets to check gets an index that fails loudly at the first
    /// array it reaches rather than one that quietly reads branch 0.
    /// </remarks>
    public const int NoSplash = -1;

    /// <summary>
    /// Every node of the run's tree, resolved once — the primary's, and the borrowed branch's once
    /// one is installed. A dictionary because M3-04 asks per candidate.
    /// </summary>
    private readonly Dictionary<ContentId, SkillSpec> _specs;

    /// <summary>
    /// The borrowed branch as this run holds it — its Keystone already dropped — or null while none
    /// is installed.
    /// </summary>
    /// <remarks>
    /// A <see cref="SkillBranchSpec"/> rather than a bare list, so that everything already written
    /// against a branch reads it the same way: <see cref="SkillTree"/>'s flatten walks
    /// <see cref="Branch"/> for all four, and M5-07a-ii's fourth column has a
    /// <see cref="SkillBranchSpec.NameKey"/> to draw without a second shape to special-case.
    /// </remarks>
    private SkillBranchSpec _splash;

    /// <summary>
    /// Each borrowed node's tier, 1-based and the tier it had in its own tree. Null while no branch
    /// is installed.
    /// </summary>
    /// <remarks>
    /// A dictionary for <see cref="SkillTreeSpec.TryLocate"/>'s reason: <see cref="TryLocate"/> is
    /// asked once per candidate per gate check, and a walk of the branch would be a loop inside a
    /// loop on the one path that runs per offer.
    /// </remarks>
    private Dictionary<ContentId, int> _splashTiers;

    /// <param name="tree">The class's tree, as authored.</param>
    /// <param name="catalog">Where each of its node ids resolves to a <see cref="SkillSpec"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="tree"/> or <paramref name="catalog"/> is null.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// A node id in <paramref name="tree"/> names a skill nobody authored. The message names the
    /// tree, the branch, the tier and the id — <see cref="ContentCatalog.TryGetSkill"/> rather than
    /// <see cref="ContentCatalog.Skill"/> is what buys that, because the catalog's own "no skill
    /// with id 'x'" is true and unhelpful when twenty-seven positions could have held it.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A <see cref="SkillKind.Keystone"/> is not the sole node of its branch's last tier, or an
    /// <see cref="SkillKind.Upgrade"/>'s parent is not in the same branch at a lower tier.
    /// </exception>
    public TreeRules(SkillTreeSpec tree, ContentCatalog catalog)
    {
        if (tree is null)
        {
            throw new ArgumentNullException(nameof(tree));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        Tree = tree;
        _specs = new Dictionary<ContentId, SkillSpec>(tree.NodeCount);

        // Two passes, and the split is load-bearing: everything must be resolved before any
        // cross-spec rule is asked, because the upgrade rule reads the *parent's* position and a
        // parent may be authored anywhere in the tree — including in a branch this walk has not
        // reached yet. Asked in one pass, a legal tree would be refused on the strength of branch
        // order alone.
        Resolve(tree, catalog);
        CrossCheck(tree);
    }

    /// <summary>
    /// The primary tree as authored — the class's own shape, its branches and where each node sits.
    /// </summary>
    /// <remarks>
    /// <b>The class's tree, never the run's.</b> A branch borrowed under CH §5.4 is not in it and
    /// never will be: the content side is one tree per class (M3-02a rule 11) and stays that way.
    /// Anything that wants the run's branches asks <see cref="Branch"/> and
    /// <see cref="BranchCount"/>.
    /// </remarks>
    public SkillTreeSpec Tree { get; }

    /// <summary>
    /// How many nodes this run's tree holds. Twenty-seven for a full class (CH §5), plus the
    /// borrowed branch once one is installed — CH §5.4's <em>"one branch minus its Keystone is 7"</em>.
    /// </summary>
    /// <remarks>
    /// Surfaced here because this is the object <see cref="SkillTree"/> is written against: its
    /// <c>IsFull</c> is a comparison against this number and <c>OfferGenerator</c> sizes its buffers
    /// by it. <b>It is no longer <see cref="SkillTreeSpec.NodeCount"/>,</b> which is what a class
    /// authored, and the gap between the two is exactly the borrowed branch.
    /// </remarks>
    public int Count => Tree.NodeCount + (_splash is null ? 0 : _splash.NodeCount);

    /// <summary>
    /// How many branches this run has: three, or four once a branch has been borrowed.
    /// </summary>
    /// <remarks>
    /// <b>The number every branch-shaped member is indexed against</b> — this type's
    /// <see cref="TryLocate"/>, <see cref="NodeCountOf"/> and <see cref="Branch"/>,
    /// <c>SkillTree.TakenInBranch</c>, <c>NodeTaken.Branch</c> and <c>OfferGenerator</c>'s
    /// per-branch counters. <see cref="SkillTreeSpec.BranchCount"/> is the <em>class's</em> number
    /// and the two are equal exactly while nothing is installed; reading the const as the run's
    /// number is what made CH §5.4's borrowed branch an <c>IndexOutOfRangeException</c> rather than
    /// a refusal.
    /// </remarks>
    public int BranchCount => Tree.Branches.Count + (_splash is null ? 0 : 1);

    /// <summary>
    /// The borrowed branch's index, or <see cref="NoSplash"/> while none is installed.
    /// </summary>
    /// <remarks>
    /// Always one past the primary's last, so it is 3 for every tree this game ships — and derived
    /// rather than stored, because a second place recording where the branch went is a second place
    /// for it to be wrong.
    /// </remarks>
    public int SplashBranch => _splash is null ? NoSplash : Tree.Branches.Count;

    /// <summary>
    /// The class the borrowed branch came from, or <c>default</c> while none is installed.
    /// </summary>
    /// <remarks>
    /// The class rather than the tree, because that is what CH §5.4's screen names and what
    /// M5-07a-ii derives its <em>"already splashed"</em> answer against. A class has exactly one
    /// tree, so nothing is lost by carrying the one the player would recognise.
    /// </remarks>
    public ContentId SplashCharacterId { get; private set; }

    /// <summary>
    /// How many of this run's nodes are <see cref="SkillKind.Active"/>. About seven for a full
    /// class — CH §5's 27 nodes at CH §4's ~25 % Active — plus the borrowed branch's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A fact about the tree, deliberately not a rule.</b> <c>RunSession.Start</c> compares it
    /// against <c>SkillRunner.MaxActives</c> and refuses a tree that would not fit, before
    /// <c>RunStarted</c> — so an authoring mistake refuses the <em>run</em> rather than the pick,
    /// which is this class's whole argument. The comparison lives there rather than here because
    /// the capacity is the runner's number: this type answers what the tree <em>is</em>, and a
    /// <c>Combat</c> array size is not one of the tree's own properties.
    /// </para>
    /// <para>
    /// <b>A splash is the one case that check cannot be made at <c>Start</c> for</b>, because the
    /// branch has not been chosen yet — so <see cref="InstallSplash"/> makes it again and refuses
    /// the install rather than the run (M5-07a-i rule 9). That is the exception to the paragraph
    /// above rather than a second opinion: the number compared and the cap compared against are the
    /// same two, and only the moment differs.
    /// </para>
    /// <para>
    /// Counted in <see cref="Resolve"/>'s existing walk, so it costs nothing beyond a comparison
    /// per node on a path that already had each spec in hand.
    /// </para>
    /// </remarks>
    public int ActiveCount { get; private set; }

    /// <summary>The node <paramref name="id"/> names.</summary>
    /// <remarks>
    /// A dictionary probe against the sweep the constructor already did, so this cannot fail for a
    /// node that is in the tree — which is the whole reason the sweep is at <c>Start</c>.
    /// </remarks>
    /// <exception cref="KeyNotFoundException">
    /// <paramref name="id"/> is not in this tree. Distinct from the catalog not holding it: a
    /// stranger here is a caller asking the wrong tree, and by construction every id this tree
    /// holds is present.
    /// </exception>
    public SkillSpec Skill(ContentId id)
    {
        if (!_specs.TryGetValue(id, out SkillSpec spec))
        {
            throw new KeyNotFoundException(
                $"'{id}' is not a node of this run's tree — '{Tree.Id}'"
                    + (_splash is null ? "." : $", plus one branch of '{SplashCharacterId}'.")
                    + " Every id the run holds was resolved at Start or at the splash, so a miss "
                    + "here is a question asked of the wrong tree.");
        }

        return spec;
    }

    /// <summary>
    /// Where <paramref name="id"/> sits, or false if it is not in this tree.
    /// </summary>
    /// <param name="id">The node to locate. <c>default(ContentId)</c> is simply not found.</param>
    /// <param name="branch">
    /// The branch index, 0-based and into <em>this run's</em> branches — so
    /// <see cref="SplashBranch"/> for a borrowed node, and <see cref="NoSplash"/> for a miss.
    /// </param>
    /// <param name="tier">The tier, 1-based.</param>
    /// <remarks>
    /// <para>
    /// The primary first, through <see cref="SkillTreeSpec.TryLocate"/> and the index it built once
    /// at boot, then the borrowed branch. It is here rather than left to callers so that everything
    /// gating needs comes off one object — which is what makes the borrowed branch a fourth legal
    /// value here instead of a second object every caller has to know about.
    /// </para>
    /// <para>
    /// <b>A borrowed node keeps the tier it had in its own tree</b>, so CH §5.4's <em>"gated by the
    /// same tier rule"</em> is the same arithmetic against a different count of takes — see
    /// <see cref="InstallSplash"/> for why dropping the Keystone cannot renumber it.
    /// </para>
    /// </remarks>
    public bool TryLocate(ContentId id, out int branch, out int tier)
    {
        if (Tree.TryLocate(id, out branch, out tier))
        {
            return true;
        }

        if (_splashTiers is not null && _splashTiers.TryGetValue(id, out tier))
        {
            branch = SplashBranch;

            return true;
        }

        branch = NoSplash;
        tier = 0;

        return false;
    }

    /// <summary>The branch at <paramref name="branch"/> of this run's tree.</summary>
    /// <param name="branch">
    /// The branch index, 0-based: 0–2 are the primary's, and <see cref="SplashBranch"/> is the
    /// borrowed one.
    /// </param>
    /// <remarks>
    /// <b>What makes the borrowed branch walkable by everything already written against a branch.</b>
    /// <c>SkillTree</c>'s flatten reads all four through this and no longer touches
    /// <see cref="SkillTreeSpec.Branches"/> at all, so the walk order it promises is one loop rather
    /// than a loop and a special case. The branch it hands back for the borrowed index is the one
    /// <see cref="InstallSplash"/> built — the Keystone already dropped — rather than the one the
    /// other class authored.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="branch"/> is not an index into this run's branches.
    /// </exception>
    public SkillBranchSpec Branch(int branch)
    {
        if (branch < 0 || branch >= BranchCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(branch),
                branch,
                $"Branches are indexed from 0 and this run has {BranchCount} — '{Tree.Id}' has "
                    + $"{Tree.Branches.Count} and "
                    + (_splash is null
                        ? "nothing has been borrowed."
                        : $"'{SplashCharacterId}' lent one."));
        }

        return branch < Tree.Branches.Count ? Tree.Branches[branch] : _splash;
    }

    /// <summary>Whether <paramref name="id"/> is the build-defining node at the end of a branch.</summary>
    /// <remarks>
    /// False for an id that is not in the tree, rather than throwing: every caller of this is
    /// deciding how to gate a candidate it has already located, and a second refusal here would be
    /// the same mistake reported twice.
    /// </remarks>
    public bool IsKeystone(ContentId id) =>
        _specs.TryGetValue(id, out SkillSpec spec) && spec.Kind == SkillKind.Keystone;

    /// <summary>
    /// How many nodes of <paramref name="id"/>'s own branch must already be taken before it may be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>CH §5's tier rule, except for a Keystone, where CH §5 says something stronger.</b> An
    /// ordinary node at tier N wants N − 1; a Keystone wants <em>all preceding</em>, which for a
    /// nine-node branch is 8 where the tier rule would have said 4. The two only coincide in the
    /// one-node-a-tier tree CH §5 draws, which is not the tree this game ships
    /// (<see cref="SkillBranchSpec"/>'s remarks, and the owner's ruling at M3-00a).
    /// </para>
    /// <para>
    /// Stated as a number rather than as a predicate because that is what makes the two rules one
    /// comparison at the gate, and because M3-09d draws <em>"needs 8 of this branch"</em> from it.
    /// </para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException"><paramref name="id"/> is not in this tree.</exception>
    public int RequiredTakenInBranch(ContentId id)
    {
        SkillSpec spec = Skill(id);

        // This run's branches rather than the class's, so a borrowed node is counted against
        // `_takenInBranch[3]` and nowhere else — CH §5.4's "gated by the same tier rule (§5)".
        TryLocate(id, out int branch, out int tier);

        return spec.Kind == SkillKind.Keystone
            ? NodeCountOf(branch) - 1
            : tier - 1;
    }

    /// <summary>
    /// The skill <paramref name="id"/> improves, for an <see cref="SkillKind.Upgrade"/>; false for
    /// every other kind.
    /// </summary>
    /// <remarks>
    /// The <c>Try</c> shape rather than a nullable, because "this kind has no parent" is the
    /// ordinary answer for three kinds out of four and not a failure. <see cref="SkillSpec"/> has
    /// already refused a parent on a non-Upgrade and a missing one on an Upgrade, in both
    /// directions, so this reads the field without re-asking.
    /// </remarks>
    /// <exception cref="KeyNotFoundException"><paramref name="id"/> is not in this tree.</exception>
    public bool TryGetParent(ContentId id, out ContentId parentId)
    {
        SkillSpec spec = Skill(id);

        if (spec.Kind != SkillKind.Upgrade)
        {
            parentId = default;

            return false;
        }

        parentId = spec.ParentId;

        return true;
    }

    /// <summary>How many nodes branch <paramref name="branch"/> holds, across every tier.</summary>
    /// <param name="branch">The branch index, 0-based — into this run's branches.</param>
    /// <remarks>
    /// For the borrowed branch this is CH §5.4's <em>"one branch minus its Keystone is 7"</em>: the
    /// Keystone was dropped at <see cref="InstallSplash"/>, so what is left is what this counts and
    /// what a Keystone's gate would ask for.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="branch"/> is not an index into this run's branches.
    /// </exception>
    public int NodeCountOf(int branch) => Branch(branch).NodeCount;

    /// <summary>
    /// Installs one branch of <paramref name="tree"/> beside the primary, at
    /// <see cref="SplashBranch"/>. CH §5.4's half-tree, once per run.
    /// </summary>
    /// <param name="tree">The second class's tree, as authored. Not this run's own.</param>
    /// <param name="branch">Which of its branches to borrow — an index into its own branches.</param>
    /// <param name="catalog">Where each of that branch's node ids resolves to a <see cref="SkillSpec"/>.</param>
    /// <remarks>
    /// <para>
    /// <b>The Keystone does not come with the branch, and it is dropped here rather than filtered
    /// later.</b> CH §5.4: <em>"what you do not gain: its weapon, its movement skill, its signature
    /// passive, its Veilrot relationship — and its Keystone"</em>, because a splashed Keystone
    /// speaks louder than the primary it is bolted to. Filtering at the offer instead would leave a
    /// node <see cref="TryLocate"/> finds, <c>SkillTree.IsAvailable</c> refuses and the tree view
    /// draws — three places to remember one rule. Dropped here there is one, and everything
    /// downstream simply never hears of the node.
    /// </para>
    /// <para>
    /// <b>Dropping it cannot renumber a tier</b>, which is what lets a borrowed node keep the tier
    /// it had: a Keystone is the sole node of its branch's last tier
    /// (<see cref="RequireKeystonePlacement"/>, over the source tree), so the only tier a drop can
    /// empty is the last one and the tiers below it keep their numbers. A branch that empties a
    /// tier with something still above it is refused rather than renumbered — that is a Keystone in
    /// the wrong place, and <see cref="TreeRules"/> built over that tree would already have said so.
    /// </para>
    /// <para>
    /// <b>Everything is computed into locals and committed at the end</b>, so every refusal below
    /// leaves this run exactly as it was — rule 9's <em>"the run is unaffected"</em>. The two
    /// allocations are one dictionary and one branch, once per run, at a moment the game is paused.
    /// </para>
    /// <para>
    /// <b>The caller tells <c>SkillTree</c> immediately afterwards</b>, through
    /// <c>SkillTree.OnSplashInstalled</c>, and that is a method rather than a subscription for the
    /// reason core never subscribes to its own events. Between the two calls the pair disagrees
    /// about how many nodes the run has, which is why they are one step of one caller and not two
    /// things anybody may do separately.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="tree"/> or <paramref name="catalog"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="branch"/> is not an index into <paramref name="tree"/>'s branches.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tree"/> is this run's own or belongs to its class; a node of that branch is
    /// already in this tree; the branch is a Keystone alone; an Upgrade in it is orphaned by the
    /// Keystone drop; or the branch's Actives would not fit <see cref="SkillRunner.MaxActives"/>.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// A node id of that branch names a skill nobody authored.
    /// </exception>
    /// <exception cref="InvalidOperationException">A branch is already installed.</exception>
    public void InstallSplash(SkillTreeSpec tree, int branch, ContentCatalog catalog)
    {
        if (tree is null)
        {
            throw new ArgumentNullException(nameof(tree));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        // **First, so that a second call throws for being second rather than for whatever its
        // arguments happen to be.** CH §5.4: "Reversible: no. Locked for the run." A silent
        // replacement would leave nodes taken from a branch the run no longer has, and
        // `SkillTree.TakenInBranch(3)` would be counting two different branches' picks together.
        if (_splash is not null)
        {
            throw new InvalidOperationException(
                $"This run has already borrowed a branch of '{SplashCharacterId}'. A splash is "
                    + "locked for the run (CH §5.4), and replacing one would leave every node "
                    + $"already taken from branch {SplashBranch} counted against a branch this run "
                    + "no longer has.");
        }

        if (tree.Id == Tree.Id || tree.CharacterId == Tree.CharacterId)
        {
            throw new ArgumentException(
                $"'{tree.Id}' is this run's own tree. CH §5.4 borrows a branch from a *second* "
                    + "class; a class splashing itself would offer nodes it already holds and "
                    + "count them twice.",
                nameof(tree));
        }

        if (branch < 0 || branch >= tree.Branches.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(branch),
                branch,
                $"Branches are indexed from 0 and '{tree.Id}' has {tree.Branches.Count}.");
        }

        SkillBranchSpec source = tree.Branches[branch];

        var tiers = new List<IReadOnlyList<ContentId>>(source.TierCount);
        var kept = new List<SkillSpec>(source.NodeCount);
        var tierById = new Dictionary<ContentId, int>(source.NodeCount);
        int actives = 0;

        for (int t = 1; t <= source.TierCount; t++)
        {
            IReadOnlyList<ContentId> nodes = source.Tier(t);
            var survivors = new List<ContentId>(nodes.Count);

            for (int i = 0; i < nodes.Count; i++)
            {
                ContentId id = nodes[i];

                if (!catalog.TryGetSkill(id, out SkillSpec spec))
                {
                    throw new KeyNotFoundException(
                        $"'{tree.Id}' names '{id}' at branch {branch}, tier {t}, and no skill with "
                            + "that id is in the catalog. A branch cannot be borrowed out of a "
                            + "tree whose own nodes do not resolve; nothing has been installed.");
                }

                // CH §5.4's "and its Keystone", and the one thing a branch loses on the way over.
                if (spec.Kind == SkillKind.Keystone)
                {
                    continue;
                }

                // `SkillTreeSpec`'s own uniqueness rule, at the seam where it could be broken: two
                // trees each hold an id once, and borrowing is the first thing in the game that can
                // put one id in two branches of one run — where taking it would satisfy two
                // branches' gating at a time.
                if (_specs.ContainsKey(id))
                {
                    throw new ArgumentException(
                        $"Branch {branch} of '{tree.Id}' holds '{id}', which is already a node of "
                            + $"'{Tree.Id}'. A node sits in exactly one place, or taking it once "
                            + "would satisfy two branches' gating at a time.",
                        nameof(tree));
                }

                survivors.Add(id);
                kept.Add(spec);
                tierById.Add(id, t);

                if (spec.Kind == SkillKind.Active)
                {
                    actives++;
                }
            }

            if (survivors.Count == 0)
            {
                continue;
            }

            // A tier below this one was emptied by the Keystone drop, so carrying this one would
            // renumber it — and a tier number *is* the gate (CH §5). Only a Keystone somewhere
            // other than its branch's last tier can produce this, which is content `TreeRules` over
            // that tree would already have refused.
            if (tiers.Count != t - 1)
            {
                throw new ArgumentException(
                    $"Branch {branch} of '{tree.Id}' has a Keystone below tier {t}, so dropping it "
                        + "would renumber the tiers above — and a tier number is the gate (CH §5). "
                        + "A Keystone is the sole node of its branch's last tier.",
                    nameof(tree));
            }

            tiers.Add(survivors);
        }

        if (tiers.Count == 0)
        {
            throw new ArgumentException(
                $"Branch {branch} of '{tree.Id}' is a Keystone and nothing else, so CH §5.4 leaves "
                    + "nothing to borrow. An empty branch would make RequiredTakenInBranch answer "
                    + "−1 and offer the player a column with no nodes in it.",
                nameof(tree));
        }

        RequireNoOrphanedUpgrade(tree, branch, kept, tierById);

        // **`RunSession.Start`'s check, made again at the one moment it could not be made then**
        // (rule 9). A splash arrives mid-run, so the comparison that refuses an over-full tree
        // before `RunStarted` has nothing to say about a branch nobody had chosen yet. Refused here
        // it costs the install; left to `SkillRunner.Add` it would throw on the thirteenth Active,
        // inside `ChooseOffer`, after the node was taken and its effects applied.
        if (ActiveCount + actives > SkillRunner.MaxActives)
        {
            throw new ArgumentException(
                $"'{Tree.Id}' holds {ActiveCount} Active nodes and branch {branch} of '{tree.Id}' "
                    + $"would add {actives}, past the runner's {SkillRunner.MaxActives}. The run "
                    + "keeps the tree it started with.",
                nameof(tree));
        }

        // Built before anything is assigned, so a branch this constructor would refuse leaves the
        // run untouched like every refusal above it.
        var borrowed = new SkillBranchSpec(source.NameKey, tiers);

        for (int i = 0; i < kept.Count; i++)
        {
            _specs.Add(kept[i].Id, kept[i]);
        }

        _splash = borrowed;
        _splashTiers = tierById;
        ActiveCount += actives;
        SplashCharacterId = tree.CharacterId;
    }

    /// <summary>
    /// Resolves every id in the tree, naming where a stranger was asked for.
    /// </summary>
    private void Resolve(SkillTreeSpec tree, ContentCatalog catalog)
    {
        for (int b = 0; b < tree.Branches.Count; b++)
        {
            SkillBranchSpec branch = tree.Branches[b];

            for (int t = 1; t <= branch.TierCount; t++)
            {
                IReadOnlyList<ContentId> nodes = branch.Tier(t);

                for (int i = 0; i < nodes.Count; i++)
                {
                    ContentId id = nodes[i];

                    if (!catalog.TryGetSkill(id, out SkillSpec spec))
                    {
                        throw new KeyNotFoundException(
                            $"'{tree.Id}' names '{id}' at branch {b}, tier {t}, and no skill with "
                                + "that id is in the catalog. Add a SkillDefinition to BootScope's "
                                + "skill list or correct the id; nothing about this run has been "
                                + "announced.");
                    }

                    // The tree already refused one id in two places, so this cannot collide.
                    _specs.Add(id, spec);

                    // Counted in the pass that is already resolving every node, rather than by a
                    // second walk at the one call site that asks (M3-06). This is a *fact* about
                    // the tree and not a rule — whether that many actives fit is the runner's
                    // question, asked where the runner is built.
                    if (spec.Kind == SkillKind.Active)
                    {
                        ActiveCount++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Refuses a borrowed branch holding an Upgrade whose parent did not come over with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one cross-check <see cref="InstallSplash"/> has to re-run, and the Keystone drop is
    /// why.</b> <see cref="RequireParentBelowInBranch"/> already refused a cross-branch parent when
    /// the source tree was built, and a branch borrowed whole keeps its internal relationships — so
    /// an Upgrade in the borrowed branch has its parent in the borrowed branch. What this task adds
    /// is a node that <em>leaves</em>: an Upgrade whose parent was the Keystone just dropped would
    /// be permanently unavailable, which is the exact deadlock that method exists to prevent, and
    /// it would arrive as a column with a node nothing could ever open.
    /// </para>
    /// <para>
    /// Against the shipped content this cannot fire — no authored tree has a Keystone with a child
    /// — and the check is what stops that being an accident rather than a rule.
    /// </para>
    /// </remarks>
    private static void RequireNoOrphanedUpgrade(
        SkillTreeSpec tree,
        int branch,
        IReadOnlyList<SkillSpec> kept,
        Dictionary<ContentId, int> tierById)
    {
        for (int i = 0; i < kept.Count; i++)
        {
            SkillSpec spec = kept[i];

            if (spec.Kind != SkillKind.Upgrade || tierById.ContainsKey(spec.ParentId))
            {
                continue;
            }

            throw new ArgumentException(
                $"Branch {branch} of '{tree.Id}' holds upgrade '{spec.Id}', whose parent "
                    + $"'{spec.ParentId}' is not in the branch this run would borrow — a Keystone "
                    + "does not come with it (CH §5.4). An Upgrade is gated on owning its parent, "
                    + "so the node would be drawn in the tree view and never once offered.",
                nameof(tree));
        }
    }

    /// <summary>
    /// The rules no single spec could check: keystone placement, and an upgrade's parent.
    /// </summary>
    /// <remarks>
    /// Both are about <em>a node and its neighbours</em>, which is why neither could live on
    /// <see cref="SkillSpec"/> — that type can see its own fields and nothing else.
    /// </remarks>
    private void CrossCheck(SkillTreeSpec tree)
    {
        for (int b = 0; b < tree.Branches.Count; b++)
        {
            SkillBranchSpec branch = tree.Branches[b];

            for (int t = 1; t <= branch.TierCount; t++)
            {
                IReadOnlyList<ContentId> nodes = branch.Tier(t);

                for (int i = 0; i < nodes.Count; i++)
                {
                    SkillSpec spec = _specs[nodes[i]];

                    if (spec.Kind == SkillKind.Keystone)
                    {
                        RequireKeystonePlacement(tree, branch, b, t, spec.Id);
                    }

                    if (spec.Kind == SkillKind.Upgrade)
                    {
                        RequireParentBelowInBranch(tree, b, t, spec);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A Keystone is the sole node of its branch's last tier — both halves of one sentence.
    /// </summary>
    /// <remarks>
    /// <b>A last tier that is <em>not</em> a Keystone is legal</b>, and deliberately: three
    /// branches of four ordinary nodes with no keystone at all is M3-12's v1, and refusing it would
    /// refuse the milestone's own content. What is refused is a Keystone somewhere a branch can
    /// still be climbed past — its gating asks for every other node of the branch, so a Keystone
    /// with anything above it is a rung nothing above it could ever need — and a Keystone sharing
    /// its tier, which is two build-defining nodes where CH §5 says three a class.
    /// </remarks>
    private static void RequireKeystonePlacement(
        SkillTreeSpec tree,
        SkillBranchSpec branch,
        int branchIndex,
        int tier,
        ContentId id)
    {
        if (tier != branch.TierCount)
        {
            throw new ArgumentException(
                $"'{tree.Id}' puts keystone '{id}' at branch {branchIndex}, tier {tier}, and that "
                    + $"branch is {branch.TierCount} tiers deep. A Keystone ends a branch (CH §5): "
                    + "its gating asks for every other node of the branch, so anything above it "
                    + "would be unreachable in practice and unexplainable in the tree view.",
                nameof(tree));
        }

        int shared = branch.Tier(tier).Count - 1;

        if (shared != 0)
        {
            throw new ArgumentException(
                $"'{tree.Id}' puts keystone '{id}' in branch {branchIndex}'s last tier alongside "
                    + $"{shared} other node(s). A Keystone is the sole node of its tier — three a "
                    + "class, one a branch (CH §5) — and a tier holding it beside anything else "
                    + "offers a choice the design does not have.",
                nameof(tree));
        }
    }

    /// <summary>
    /// An Upgrade's parent is in the same branch, at a lower tier.
    /// </summary>
    /// <remarks>
    /// <b>This is what stops a branch deadlocking.</b> CH §4's <em>"only offered if you own the
    /// parent"</em> with a parent in another branch makes one branch wait on a pick the player may
    /// never make: the node stays permanently unavailable, the offer silently narrows by one, and
    /// nothing anywhere reports it — the branch simply feels shorter than the tree view says it is.
    /// A parent at the same tier or above is the same failure with a shorter fuse, because its own
    /// tier gate cannot open before the child's.
    /// </remarks>
    private static void RequireParentBelowInBranch(
        SkillTreeSpec tree,
        int branchIndex,
        int tier,
        SkillSpec spec)
    {
        if (!tree.TryLocate(spec.ParentId, out int parentBranch, out int parentTier))
        {
            throw new ArgumentException(
                $"'{tree.Id}' holds upgrade '{spec.Id}' at branch {branchIndex}, tier {tier}, and "
                    + $"its parent '{spec.ParentId}' is not in this tree at all. An Upgrade is "
                    + "gated on owning its parent, so a parent nobody can take here is a node that "
                    + "is never offered.",
                nameof(tree));
        }

        if (parentBranch != branchIndex)
        {
            throw new ArgumentException(
                $"'{tree.Id}' holds upgrade '{spec.Id}' at branch {branchIndex}, tier {tier}, and "
                    + $"its parent '{spec.ParentId}' is in branch {parentBranch}. A parent in "
                    + "another branch makes this branch wait on a pick the player may never make: "
                    + "the node is never offered and nothing reports it (CH §4).",
                nameof(tree));
        }

        if (parentTier >= tier)
        {
            throw new ArgumentException(
                $"'{tree.Id}' holds upgrade '{spec.Id}' at branch {branchIndex}, tier {tier}, and "
                    + $"its parent '{spec.ParentId}' is at tier {parentTier}. A parent sits below "
                    + "its child, or the child's gate could open before the parent's ever does and "
                    + "the upgrade would be offered before the skill it improves.",
                nameof(tree));
        }
    }
}
