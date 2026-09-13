using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Soulvail.Core.Content;

// CH §5's tree, as data: three branches of tiers of node ids. Two types in one file, because a
// branch outside a tree is not a thing the game has.

/// <summary>
/// One of a tree's three branches: tiers of node ids, tier 1 first.
/// </summary>
/// <remarks>
/// <para>
/// <b>A tier holds one or more nodes, and that is the owner's ruling at M3-00a rather than a
/// reading of CH §5.</b> CH §5 draws one node a tier, and with its own gating — <em>tier N requires
/// N − 1 nodes already taken in that branch</em> — a one-node tier makes every branch a straight
/// chain: the level-up offer would be the same three heads every time, and §5.1's <em>"3 nodes
/// drawn at random from everything currently available"</em> and §8 Q3's variety rules would have
/// nothing to choose between. So the shape is data, and the shipped Oathbound tree (M3-12) is two
/// nodes a tier for four tiers plus a keystone — nine a branch, twenty-seven a class — which makes
/// every sentence in CH §5–5.1 true at once.
/// </para>
/// <para>
/// <b>CH §5's <em>"8 (7 + 1 Keystone)"</em> is a slip for 9 (8 + 1)</b>, since 3 × 8 is 24 and the
/// same table says 27. Flagged for the owner rather than edited, the GD §12.1 precedent (AR §18.3);
/// the ROADMAP parking lot holds the line.
/// </para>
/// <para>
/// <b>This holds ids, not <see cref="SkillSpec"/>s</b>, so a tree can be built before the nodes it
/// names exist and a node can be authored in one asset while its position is authored in another.
/// Everything that needs the node itself resolves the id against the <see cref="ContentCatalog"/>.
/// </para>
/// <para>
/// <b>Uniqueness is the tree's, not the branch's.</b> A branch cannot see its siblings, so checking
/// "this id appears once" here would catch half the cases and give the other half a different
/// message. <see cref="SkillTreeSpec"/> does it once, for all three.
/// </para>
/// </remarks>
public sealed class SkillBranchSpec
{
    /// <summary>
    /// The tiers, each already wrapped, so <see cref="Tier"/> hands out a view without allocating.
    /// </summary>
    /// <remarks>
    /// Wrapped once at construction rather than per call, for <c>ModeSpec._rosterView</c>'s reason:
    /// an array exposed as <see cref="IReadOnlyList{T}"/> casts straight back to
    /// <see cref="ContentId"/><c>[]</c>, and then the copy protects nothing. M3-09d's tree view asks
    /// for every tier of every branch each time it draws.
    /// </remarks>
    private readonly ReadOnlyCollection<ContentId>[] _tiers;

    /// <param name="nameKey">Localisation key for the branch's display name.</param>
    /// <param name="tiers">
    /// The tiers, tier 1 first: one to <see cref="SkillTreeSpec.MaxTiers"/> of them, each holding at
    /// least one node id. Copied to the last level; the caller's lists are not retained.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="tiers"/> is null, or one of its entries is.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="nameKey"/> is a default value; <paramref name="tiers"/> is empty or holds
    /// more than <see cref="SkillTreeSpec.MaxTiers"/>; a tier is empty; or a node id is
    /// <c>default(ContentId)</c>.
    /// </exception>
    public SkillBranchSpec(LocKey nameKey, IReadOnlyList<IReadOnlyList<ContentId>> tiers)
    {
        // Required for SkillSpec rule 3's reason, one type over: M3-09d draws this string above the
        // branch's column, and a default(LocKey) is a forgotten field rather than a branch that
        // chose to be nameless. Whether the key resolves is M3-14's question.
        if (nameKey.Key is null)
        {
            throw new ArgumentException(
                "A branch needs a nameKey. A default(LocKey) is a forgotten field rather than a "
                    + "branch that chose to be nameless, and M3-09d draws it above the column.",
                nameof(nameKey));
        }

        if (tiers is null)
        {
            throw new ArgumentNullException(nameof(tiers));
        }

        if (tiers.Count == 0)
        {
            throw new ArgumentException(
                "A branch needs at least one tier. A branch with none is a column of the tree that "
                    + "can never be picked from.",
                nameof(tiers));
        }

        if (tiers.Count > SkillTreeSpec.MaxTiers)
        {
            throw new ArgumentException(
                $"A branch holds at most {SkillTreeSpec.MaxTiers} tiers (CH §5's ceiling) and was "
                    + $"given {tiers.Count}.",
                nameof(tiers));
        }

        _tiers = new ReadOnlyCollection<ContentId>[tiers.Count];
        int nodeCount = 0;

        for (int t = 0; t < tiers.Count; t++)
        {
            IReadOnlyList<ContentId> tier = tiers[t];

            if (tier is null)
            {
                throw new ArgumentNullException(
                    nameof(tiers),
                    $"tiers[{t}] is null. An absent tier is not an empty one — gating counts tiers "
                        + "by position, so a hole would renumber every tier below it.");
            }

            if (tier.Count == 0)
            {
                throw new ArgumentException(
                    $"tiers[{t}] is empty. Gating counts tiers by position (CH §5), so an empty "
                        + "tier is an unreachable rung rather than a gap.",
                    nameof(tiers));
            }

            var copy = new ContentId[tier.Count];

            for (int i = 0; i < tier.Count; i++)
            {
                ContentId node = tier[i];

                // default(ContentId) carries a null value straight past the struct's own
                // constructor, so the check is repeated at this end (AR §18.3, M0-08). Left alone
                // it would surface one layer down as the catalog's "no skill with id ''", pointing
                // at content that was never at fault.
                if (node.Value is null)
                {
                    throw new ArgumentException(
                        $"tiers[{t}][{i}] names no skill. A default(ContentId) is not a node.",
                        nameof(tiers));
                }

                copy[i] = node;
            }

            nodeCount += copy.Length;
            _tiers[t] = Array.AsReadOnly(copy);
        }

        NameKey = nameKey;
        NodeCount = nodeCount;
    }

    /// <summary>Localisation key for the branch's display name — never the name itself.</summary>
    public LocKey NameKey { get; }

    /// <summary>How many tiers deep this branch is. One to <see cref="SkillTreeSpec.MaxTiers"/>.</summary>
    public int TierCount => _tiers.Length;

    /// <summary>How many nodes the branch holds in total, across every tier.</summary>
    public int NodeCount { get; }

    /// <summary>The node ids in <paramref name="tier"/>, which is numbered from 1.</summary>
    /// <param name="tier">The tier, 1-based — CH §5's own numbering, so T1 is 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="tier"/> is below 1 or above <see cref="TierCount"/>.
    /// </exception>
    /// <remarks>
    /// 1-based because every sentence about this in CH §5 is — <em>"tier N requires N − 1 nodes
    /// already taken"</em> — and a 0-based door here would make every caller subtract one in a
    /// formula where an off-by-one is a gating bug rather than a crash.
    /// </remarks>
    public IReadOnlyList<ContentId> Tier(int tier)
    {
        if (tier < 1 || tier > _tiers.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tier),
                tier,
                $"Tiers are numbered from 1 (CH §5) and this branch has {_tiers.Length}.");
        }

        return _tiers[tier - 1];
    }
}

/// <summary>
/// One class's whole tree, as authored data: three branches of tiers of node ids. Converted once at
/// boot from a <c>SkillTreeDefinition</c> ScriptableObject (M3-02b) and registered in the
/// <see cref="ContentCatalog"/>. See AR §10.1 and ADR-0006.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a tree <em>is</em>. What may be taken from it is M3-03's</b> — gating, availability, the
/// live set of taken nodes — and what is <em>offered</em> is M3-04's. Nothing here knows whose turn
/// it is.
/// </para>
/// <para>
/// <b>A partial tree is legal.</b> Three branches of four with no keystone is M3-12's v1, and
/// refusing it would refuse the milestone's own content. The shape a class eventually wants is two
/// nodes a tier for four tiers plus a keystone (see <see cref="SkillBranchSpec"/>); nothing here
/// insists on it, and M3-14b is where every <em>shipped</em> tree is measured against the design.
/// </para>
/// <para>
/// <b>Keystone placement is a cross-spec rule and lives elsewhere.</b> This holds ids, not kinds,
/// so <em>"a Keystone is the sole node of its branch's last tier"</em> cannot be asked here without
/// a <see cref="ContentCatalog"/> — and a spec constructor does not take one. <c>TreeRules</c>
/// (M3-03) checks it at <c>Start</c>, before <c>RunStarted</c>, and M3-14b checks it over every
/// authored tree. Stated so nobody adds a catalog to a spec constructor.
/// </para>
/// <para>
/// Immutable and shared, like every spec. Built once at boot; the location index below is built
/// with it.
/// </para>
/// </remarks>
public sealed class SkillTreeSpec
{
    /// <summary>
    /// Branches per class. Three, and the same three for every class — CH §5's <em>"identical
    /// skeleton for every class, so the UI is built once and content varies"</em>.
    /// </summary>
    public const int BranchCount = 3;

    /// <summary>The deepest a branch may go. Eight — CH §5's ceiling.</summary>
    public const int MaxTiers = 8;

    private readonly ReadOnlyCollection<SkillBranchSpec> _branches;

    /// <summary>
    /// Where each node sits, built once so M3-03 and M3-04 can ask per candidate without walking.
    /// </summary>
    private readonly Dictionary<ContentId, Location> _locations;

    /// <param name="id">The tree's stable content id, e.g. <c>tree.oathbound</c>.</param>
    /// <param name="characterId">
    /// The class this tree belongs to. A class has exactly one tree (CH §5), which is what
    /// <c>ContentCatalog.TryGetTreeFor</c> relies on.
    /// </param>
    /// <param name="branches">
    /// Exactly <see cref="BranchCount"/> branches. Copied; the caller's list is not retained.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="branches"/> is null, or one of its entries is.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> or <paramref name="characterId"/> is <c>default(ContentId)</c>;
    /// <paramref name="branches"/> does not hold exactly <see cref="BranchCount"/>; or one node id
    /// appears twice anywhere in the tree.
    /// </exception>
    public SkillTreeSpec(
        ContentId id,
        ContentId characterId,
        IReadOnlyList<SkillBranchSpec> branches)
    {
        if (id.Value is null)
        {
            throw new ArgumentException(
                "id must be a valid ContentId; default(ContentId) names no tree.",
                nameof(id));
        }

        if (characterId.Value is null)
        {
            throw new ArgumentException(
                $"'{id}' names no character. A tree belongs to exactly one class (CH §5), and a "
                    + "tree nobody can reach is content nothing will ever read.",
                nameof(characterId));
        }

        if (branches is null)
        {
            throw new ArgumentNullException(nameof(branches));
        }

        if (branches.Count != BranchCount)
        {
            throw new ArgumentException(
                $"'{id}' has {branches.Count} branches; every class has exactly {BranchCount} "
                    + "(CH §5: identical skeleton for every class, so the UI is built once).",
                nameof(branches));
        }

        var copy = new SkillBranchSpec[BranchCount];
        _locations = new Dictionary<ContentId, Location>();
        int nodeCount = 0;

        for (int b = 0; b < branches.Count; b++)
        {
            SkillBranchSpec branch = branches[b];

            if (branch is null)
            {
                throw new ArgumentNullException(
                    nameof(branches),
                    $"branches[{b}] of '{id}' is null. A tree has three branches, not two and a "
                        + "hole.");
            }

            for (int t = 1; t <= branch.TierCount; t++)
            {
                IReadOnlyList<ContentId> tier = branch.Tier(t);

                for (int i = 0; i < tier.Count; i++)
                {
                    ContentId node = tier[i];

                    // Once in the whole tree, not once per branch: the same node in two places
                    // would give TryLocate two answers and let M3-03's gating count one take
                    // against two branches.
                    if (_locations.ContainsKey(node))
                    {
                        throw new ArgumentException(
                            $"'{id}' lists '{node}' twice. A node sits in exactly one place, or "
                                + "taking it once would satisfy two branches' gating at a time.",
                            nameof(branches));
                    }

                    _locations.Add(node, new Location(b, t));
                }
            }

            nodeCount += branch.NodeCount;
            copy[b] = branch;
        }

        Id = id;
        CharacterId = characterId;
        NodeCount = nodeCount;
        _branches = Array.AsReadOnly(copy);
    }

    /// <summary>Stable identity, e.g. <c>tree.oathbound</c>.</summary>
    public ContentId Id { get; }

    /// <summary>The class this tree belongs to, e.g. <c>character.oathbound</c>.</summary>
    public ContentId CharacterId { get; }

    /// <summary>The three branches, in the order they were authored.</summary>
    /// <remarks>
    /// Order is what M3-09d's three columns and M3-03's branch index both mean, so it is part of
    /// what a tree asset says rather than an implementation detail.
    /// </remarks>
    public IReadOnlyList<SkillBranchSpec> Branches => _branches;

    /// <summary>How many nodes the whole tree holds — the sum over its branches.</summary>
    public int NodeCount { get; }

    /// <summary>
    /// Where <paramref name="skillId"/> sits in this tree, or false if it is not in it.
    /// </summary>
    /// <param name="skillId">The node to locate. <c>default(ContentId)</c> is simply not found.</param>
    /// <param name="branch">The branch index, 0-based — an index into <see cref="Branches"/>.</param>
    /// <param name="tier">The tier, 1-based — what <see cref="SkillBranchSpec.Tier"/> takes.</param>
    /// <remarks>
    /// <para>
    /// A dictionary probe against an index built once at boot, because M3-03 and M3-04 ask this per
    /// candidate: an offer draws from everything currently available, and every candidate's gating
    /// needs its branch and its tier. A walk would be three branches × eight tiers per question.
    /// </para>
    /// <para>
    /// <b>The two numbers are based differently on purpose</b>, and each follows the thing it
    /// addresses: the branch is an index into a list, the tier is CH §5's own 1-based numbering.
    /// Making both 0-based would put an off-by-one inside a gating formula, which fails as a wrong
    /// rule rather than as a crash.
    /// </para>
    /// </remarks>
    public bool TryLocate(ContentId skillId, out int branch, out int tier)
    {
        if (_locations.TryGetValue(skillId, out Location location))
        {
            branch = location.Branch;
            tier = location.Tier;
            return true;
        }

        branch = -1;
        tier = 0;
        return false;
    }

    /// <summary>A node's position: which branch, which tier.</summary>
    /// <remarks>
    /// A private <see langword="readonly"/> struct so the index costs one entry per node and no
    /// object per node, and so nothing boxes on the way out of <see cref="TryLocate"/>.
    /// </remarks>
    private readonly struct Location
    {
        internal Location(int branch, int tier)
        {
            Branch = branch;
            Tier = tier;
        }

        /// <summary>The branch index, 0-based.</summary>
        internal int Branch { get; }

        /// <summary>The tier, 1-based.</summary>
        internal int Tier { get; }
    }
}
