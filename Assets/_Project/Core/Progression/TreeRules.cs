using System;
using System.Collections.Generic;
using Soulvail.Core.Content;

namespace Soulvail.Core.Progression;

// A class's tree as one run reads it: every id resolved once, every cross-spec rule checked once,
// and the structural questions answered without a walk. The live half — what has been taken, what
// may be — is `SkillTree`, beside it.

/// <summary>
/// One class's tree, resolved and cross-checked: the <see cref="SkillTreeSpec"/> plus the
/// <see cref="SkillSpec"/> behind every id in it, and the structural questions
/// <see cref="SkillTree"/>'s gating is written in terms of.
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
/// Immutable once built, and holding only references to shared authored data: two of these over one
/// tree would be identical. It is per run only because the run is what has a class. The catalog is
/// read by the constructor and not retained — everything this object answers is in hand once the
/// sweep is done.
/// </para>
/// </remarks>
public sealed class TreeRules
{
    /// <summary>
    /// Every node of the tree, resolved once. A dictionary because M3-04 asks per candidate.
    /// </summary>
    private readonly Dictionary<ContentId, SkillSpec> _specs;

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

    /// <summary>The tree as authored — the shape, the branches and where each node sits.</summary>
    public SkillTreeSpec Tree { get; }

    /// <summary>How many nodes the whole tree holds. Twenty-seven for a full class (CH §5).</summary>
    /// <remarks>
    /// The same number as <see cref="SkillTreeSpec.NodeCount"/>, surfaced here because this is the
    /// object <see cref="SkillTree"/> is written against and its <c>IsFull</c> is a comparison
    /// against it.
    /// </remarks>
    public int Count => Tree.NodeCount;

    /// <summary>
    /// How many of this tree's nodes are <see cref="SkillKind.Active"/>. About seven for a full
    /// class — CH §5's 27 nodes at CH §4's ~25 % Active.
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
                $"'{id}' is not a node of '{Tree.Id}'. Every id this tree holds was resolved at "
                    + "Start, so a miss here is a question asked of the wrong tree.");
        }

        return spec;
    }

    /// <summary>
    /// Where <paramref name="id"/> sits, or false if it is not in this tree.
    /// </summary>
    /// <param name="id">The node to locate. <c>default(ContentId)</c> is simply not found.</param>
    /// <param name="branch">The branch index, 0-based.</param>
    /// <param name="tier">The tier, 1-based.</param>
    /// <remarks>
    /// A forward to <see cref="SkillTreeSpec.TryLocate"/>, and it is here rather than left to
    /// callers so that everything gating needs comes off one object. The index it reads was built
    /// once at boot.
    /// </remarks>
    public bool TryLocate(ContentId id, out int branch, out int tier) =>
        Tree.TryLocate(id, out branch, out tier);

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

        Tree.TryLocate(id, out int branch, out int tier);

        return spec.Kind == SkillKind.Keystone
            ? Tree.Branches[branch].NodeCount - 1
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
    /// <param name="branch">The branch index, 0-based.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="branch"/> is not an index into <see cref="SkillTreeSpec.Branches"/>.
    /// </exception>
    public int NodeCountOf(int branch)
    {
        if (branch < 0 || branch >= Tree.Branches.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(branch),
                branch,
                $"Branches are indexed from 0 and '{Tree.Id}' has {Tree.Branches.Count}.");
        }

        return Tree.Branches[branch].NodeCount;
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
