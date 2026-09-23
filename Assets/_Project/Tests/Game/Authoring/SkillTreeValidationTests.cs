using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Progression;
using Soulvail.Game.Authoring;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// Every shipped tree, measured against CH §5's shape and against the one property no constructor
/// checks: that a player who spends their picks in the wrong order cannot reach a tree that is not
/// full and has nothing left to offer.
/// </summary>
/// <remarks>
/// <para>
/// <b>The split from <c>OathboundTreeTests</c> is exact.</b> That fixture asserts <em>these</em>
/// assets say what M3-12c's table says — twelve nodes, these numbers, this branch. This one asserts
/// the <em>rules</em> every tree obeys, and a retune must break the first without touching the
/// second.
/// </para>
/// <para>
/// <b>What <c>TreeRules</c> already enforces, and why these rows still exist.</b> Its constructor
/// refuses a keystone that is not the sole node of its branch's last tier and an upgrade whose
/// parent is outside its branch or above it — for the tree a run boots with. Nothing asks the
/// question of a tree sitting in <c>Data/</c> that no <c>BootScope</c> holds yet, which is the tree
/// the next task to add a line to the installer will ship.
/// <see cref="EveryTree_CrossChecksAsTreeRules"/> is the row that keeps the two from drifting: if
/// this fixture ever accepted a tree <c>TreeRules</c> refuses, or the reverse, that row is red.
/// </para>
/// <para>
/// <b>Starvation is the reason this file exists</b> (M3-08a rule 2, ledger row 1's last structural
/// half). A branch whose remaining nodes are Upgrades of an untaken parent can be blocked while the
/// tree is half empty; all three blocked at once turns a pick into Overflow, which is CH §5.2's
/// consolation being paid out for an authoring mistake rather than for a full tree. The three rows
/// below walk pick sequences and assert the available set is non-empty until <c>IsFull</c>. <b>Only
/// the exhaustive one is a proof</b> — see its own remarks, and
/// <see cref="NoTree_CanStarve_Sampled"/>'s.
/// </para>
/// <para>
/// <b>And the finding M3-14b made writing them: under today's rules a tree that gets as far as
/// these rows cannot starve, and the proof is three constructors long.</b> <c>SkillBranchSpec</c>
/// refuses an empty tier, so tiers 1…t−1 hold at least t−1 nodes between them; <c>TreeRules</c>
/// refuses an Upgrade whose parent is outside its branch or at or above it, so an Upgrade cannot sit
/// at tier 1 and its parent is always below it; and a Keystone is the sole node of its branch's last
/// tier. Take the untaken node of lowest tier. At tier 1 its gate wants nothing, and it cannot be an
/// Upgrade. Above tier 1 every node below it in its branch is taken, which is at least t−1 — its
/// tier gate — and its parent, if it has one, is among them. A Keystone in that position has its
/// whole branch taken, which is exactly what it asks for. So something is always available.
/// <b>That makes these three rows a regression guard rather than a content check</b>, and the thing
/// they guard is the day a gating rule changes: a cross-branch requirement, a "requires" edge, an
/// Upgrade whose parent is in another tree, or M7-04's nine-tier branches. Any of those breaks a
/// step of the proof, and these rows are what would say so. They are listed here rather than
/// deleted because the argument above is not written down anywhere else, and the next task to add a
/// gate will not know it is standing on it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SkillTreeValidationTests
{
    /// <summary>
    /// The largest tree the exhaustive walk will take on. Twelve nodes is 4 096 subsets, which is a
    /// second; CH §5's full twenty-seven is 134 million, which is why
    /// <see cref="NoTree_CanStarve_Sampled"/> exists and why it is honest about what it proves.
    /// </summary>
    private const int ExhaustiveNodeLimit = 12;

    /// <summary>How many random pick orders the sampled walk tries per tree.</summary>
    private const int SampledSequences = 1_000;

    /// <summary>
    /// Fixed, so a red run is a red run again tomorrow. A seeded sample that reseeded itself each
    /// run would find a starvable tree once and pass on the retry, which is worse than not looking.
    /// </summary>
    private const int Seed = 20260918;

    /// <summary>
    /// The one class <see cref="EveryShippedCharacter_HasATree"/> skips, and the only one it ever
    /// may: M6-07a authored the Emberwright's numbers and M6-08 authors its tree, so for the tasks
    /// between them the project ships a class with no tree on purpose.
    /// </summary>
    /// <remarks>
    /// M5-02's named skip, back exactly as it was one class on (M6-07a rule 9). Named here rather
    /// than expressed as a rule, so that the skip covers this omission and not the next one — and
    /// <see cref="Tree_TheTreelessClassIsStillTreeless"/> is what makes it temporary.
    /// </remarks>
    private const string TreelessUntilM608 = "character.emberwright";

    // ---- Rule 5: CH §5's shape --------------------------------------------------------------------

    [Test]
    public void EveryTree_HasThreeBranches()
    {
        var problems = new List<string>();

        foreach (AuthoredTree tree in EveryTree())
        {
            if (tree.Spec.Branches.Count != SkillTreeSpec.BranchCount)
            {
                // CH §5: "identical skeleton for every class, so the UI is built once". M3-09d's
                // three columns are literally three columns.
                problems.Add(
                    $"{tree.Path}: has {tree.Spec.Branches.Count} branches, and CH §5 says "
                        + $"{SkillTreeSpec.BranchCount}.");

                continue;
            }

            for (int b = 0; b < tree.Spec.Branches.Count; b++)
            {
                SkillBranchSpec branch = tree.Spec.Branches[b];

                if (branch.TierCount < 1 || branch.TierCount > SkillTreeSpec.MaxTiers)
                {
                    problems.Add(
                        $"{tree.Path}: branch {b} is {branch.TierCount} tiers deep, and the range "
                            + $"is 1 to {SkillTreeSpec.MaxTiers}.");

                    continue;
                }

                for (int t = 1; t <= branch.TierCount; t++)
                {
                    if (branch.Tier(t).Count == 0)
                    {
                        problems.Add(
                            $"{tree.Path}: branch {b} tier {t} is empty. An empty tier is a gate "
                                + "with nothing behind it — every node above it needs one more "
                                + "pick than the player can ever spend in that branch.");
                    }
                }
            }
        }

        ContentValidationTests.AssertNoProblems(problems, "Tree shape (CH §5)");
    }

    [Test]
    public void EveryTree_UsesEachIdOnce()
    {
        var problems = new List<string>();

        foreach (AuthoredTree tree in EveryTree())
        {
            var seen = new HashSet<ContentId>();

            for (int b = 0; b < tree.Spec.Branches.Count; b++)
            {
                SkillBranchSpec branch = tree.Spec.Branches[b];

                for (int t = 1; t <= branch.TierCount; t++)
                {
                    foreach (ContentId id in branch.Tier(t))
                    {
                        if (id == default)
                        {
                            // An empty slot in the Inspector's node array, which is what an
                            // unfinished drag leaves behind.
                            problems.Add($"{tree.Path}: branch {b} tier {t} holds an empty slot.");

                            continue;
                        }

                        if (!seen.Add(id))
                        {
                            problems.Add(
                                $"{tree.Path}: '{id}' appears more than once. A node in two places "
                                    + "is a node the player can be offered after taking it.");
                        }
                    }
                }
            }
        }

        ContentValidationTests.AssertNoProblems(problems, "Node ids used once");
    }

    // ---- Rule 6: keystones and upgrades ------------------------------------------------------------

    [Test]
    public void EveryKeystone_IsALastTierSingleton()
    {
        var problems = new List<string>();
        int keystones = 0;

        foreach (AuthoredTree tree in EveryTree())
        {
            for (int b = 0; b < tree.Spec.Branches.Count; b++)
            {
                SkillBranchSpec branch = tree.Spec.Branches[b];

                for (int t = 1; t <= branch.TierCount; t++)
                {
                    foreach (ContentId id in branch.Tier(t))
                    {
                        if (!tree.Catalog.TryGetSkill(id, out SkillSpec spec)
                            || spec.Kind != SkillKind.Keystone)
                        {
                            continue;
                        }

                        keystones++;

                        if (t != branch.TierCount || branch.Tier(t).Count != 1)
                        {
                            problems.Add(
                                $"{tree.Path}: keystone '{id}' sits in branch {b} tier {t} of "
                                    + $"{branch.TierCount}, alongside {branch.Tier(t).Count - 1} "
                                    + "other node(s). CH §5 makes a keystone the sole node of its "
                                    + "branch's last tier — it is what the whole branch was spent "
                                    + "to reach.");
                        }
                    }
                }
            }
        }

        TestContext.WriteLine($"Keystones in the project: {keystones}.");

        ContentValidationTests.AssertNoProblems(problems, "Keystone placement (CH §5)");
    }

    [Test]
    public void ATreeWithNoKeystone_Passes()
    {
        // **A partial tree is legal**, and this row is here to say so out loud rather than to catch
        // anything. M3-12c's v1 is three branches of four with no keystone at all; a validation
        // test that rejected the milestone's own content would be the worst possible version of
        // this task. M3-02a rule 8 is the decision; this is its assertion at the asset level.
        AuthoredTree oathbound = TreeAt("Assets/_Project/Data/Trees/Oathbound.asset");

        int keystones = 0;

        foreach (ContentId id in EveryNodeOf(oathbound.Spec))
        {
            if (oathbound.Catalog.TryGetSkill(id, out SkillSpec spec)
                && spec.Kind == SkillKind.Keystone)
            {
                keystones++;
            }
        }

        Assert.That(keystones, Is.Zero,
            $"{oathbound.Path}: M3-12c ships twelve nodes and no keystone. If that has changed, "
                + "this row is not wrong — it is out of date, and the thing to check is that "
                + "EveryKeystone_IsALastTierSingleton still passes.");

        Assert.That(
            () => new TreeRules(oathbound.Spec, oathbound.Catalog),
            Throws.Nothing,
            $"{oathbound.Path}: a tree with no keystone must still cross-check.");
    }

    [Test]
    public void EveryUpgrade_HasAParentInItsBranchBelow()
    {
        var problems = new List<string>();
        int upgrades = 0;

        foreach (AuthoredTree tree in EveryTree())
        {
            foreach (ContentId id in EveryNodeOf(tree.Spec))
            {
                if (!tree.Catalog.TryGetSkill(id, out SkillSpec spec)
                    || spec.Kind != SkillKind.Upgrade)
                {
                    continue;
                }

                upgrades++;

                tree.Spec.TryLocate(id, out int branch, out int tier);

                if (!tree.Spec.TryLocate(spec.ParentId, out int parentBranch, out int parentTier))
                {
                    problems.Add(
                        $"{tree.Path}: upgrade '{id}' improves '{spec.ParentId}', which is not in "
                            + "this tree at all.");

                    continue;
                }

                if (parentBranch != branch || parentTier >= tier)
                {
                    // An upgrade whose parent sat in another branch would let one branch wait on a
                    // pick the player may never make — which is the starvation rule 9 walks, seen
                    // from the authoring side (M3-03 rule 1, M3-12c rule 2).
                    problems.Add(
                        $"{tree.Path}: upgrade '{id}' is at branch {branch} tier {tier} and its "
                            + $"parent '{spec.ParentId}' is at branch {parentBranch} tier "
                            + $"{parentTier}. A parent belongs in the same branch, lower down.");
                }
            }
        }

        TestContext.WriteLine($"Upgrade nodes in the project: {upgrades}.");

        ContentValidationTests.AssertNoProblems(problems, "Upgrade parents");
    }

    [Test]
    public void EveryTree_CrossChecksAsTreeRules()
    {
        // The anti-drift row (rule 11). The two rows above are written against the assets; TreeRules
        // asks the same two questions of the specs a catalog was handed. If either side ever
        // accepted what the other refused, the disagreement would be the bug and nothing would say
        // which copy was right. This is what makes them one rule with two readers.
        var problems = new List<string>();

        foreach (AuthoredTree tree in EveryTree())
        {
            try
            {
                _ = new TreeRules(tree.Spec, tree.Catalog);
            }
            catch (Exception exception)
            {
                problems.Add($"{tree.Path}: TreeRules refuses it — {exception.Message}");
            }
        }

        ContentValidationTests.AssertNoProblems(problems, "TreeRules cross-check");
    }

    // ---- Rule 7: nodes resolve; orphans are reported -----------------------------------------------

    [Test]
    public void EveryTreeNode_ResolvesToASkill()
    {
        // A tree holding an id the catalog cannot answer is a level-up screen with a blank card, and
        // M3-02b rule 6 is explicit that neither list resolves the other at conversion — so nothing
        // else in the build catches it.
        var problems = new List<string>();

        foreach (AuthoredTree tree in EveryTree())
        {
            foreach (ContentId id in EveryNodeOf(tree.Spec))
            {
                if (!tree.Catalog.TryGetSkill(id, out SkillSpec _))
                {
                    tree.Spec.TryLocate(id, out int branch, out int tier);

                    problems.Add(
                        $"{tree.Path}: branch {branch} tier {tier} names '{id}', and no "
                            + "SkillDefinition in the project carries that id.");
                }
            }
        }

        ContentValidationTests.AssertNoProblems(problems, "Tree nodes resolve");
    }

    [Test]
    public void SkillsOutsideEveryTree_AreReportedNotFailed()
    {
        // **Deliberately permitted.** M6's Sanctum and M6-05's Pacts may grant a skill that is in no
        // tree, and a test that forbade it today would have to be argued down by the task that needs
        // it. So the count is reported rather than refused — what is *asserted* is that the two
        // lists partition the shipped skills, which is what makes the reported number trustworthy
        // rather than a side effect of a walk that missed a branch.
        ContentCatalog catalog = ShippedCatalog();

        var inSomeTree = new HashSet<ContentId>();

        foreach (SkillTreeSpec tree in catalog.Trees)
        {
            foreach (ContentId id in EveryNodeOf(tree))
            {
                inSomeTree.Add(id);
            }
        }

        var orphans = new List<string>();

        foreach (SkillSpec skill in catalog.Skills)
        {
            if (!inSomeTree.Contains(skill.Id))
            {
                orphans.Add(skill.Id.Value);
            }
        }

        TestContext.WriteLine(
            orphans.Count == 0
                ? "Every shipped skill is in a tree."
                : $"{orphans.Count} skill(s) in no tree: {string.Join(", ", orphans)}.");

        Assert.That(
            inSomeTree.Count + orphans.Count,
            Is.EqualTo(catalog.Skills.Count),
            "Every shipped skill is either in a tree or in the orphan list, and nothing is in "
                + "both. If this is wrong the reported count above means nothing.");
    }

    // ---- Rule 8: every shipped character has a tree ------------------------------------------------

    [Test]
    public void EveryShippedCharacter_HasATree()
    {
        // gap (f), and M3-02a rule 11's named obligation: a character with no tree is legal in the
        // catalog and a run-breaking omission in a shipped build. This is what retires M3-03 rule
        // 10's null-tree branch from a real build.
        //
        // **The Gravecaller's named skip is gone as of M5-06b, with the row that dated it.** From
        // M5-02 to M5-06a the project shipped one authored class with no tree on purpose, and this
        // sweep exempted `character.gravecaller` by name — narrowly, so the exemption could not
        // quietly cover the next omission — under `TheTreelessClass_IsStillTreeless`, whose whole
        // job was to go red the day the tree landed. It did, and both halves were deleted rather
        // than moved: the sweep is the stronger check and it is doing the work again.
        //
        // **And it is back for the Emberwright, M6-07a to M6-08** (M6-07a rule 9) — the same skip,
        // by name, with the same row beside it to expire it.
        ContentCatalog catalog = ShippedCatalog();
        var problems = new List<string>();

        foreach (string path in ContentValidationTests.PathsOf<CharacterDefinition>())
        {
            var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

            if (definition == null)
            {
                continue;
            }

            CharacterSpec spec = definition.ToSpec();

            if (spec.Id.Value == TreelessUntilM608)
            {
                continue;
            }

            if (!catalog.TryGetTreeFor(spec.Id, out SkillTreeSpec _))
            {
                problems.Add(
                    $"{path}: '{spec.Id}' has no tree. Every level-up in a run of this class would "
                        + "pay Overflow instead of offering nodes.");
            }
        }

        ContentValidationTests.AssertNoProblems(problems, "Every character has a tree");
    }

    [Test]
    public void Tree_TheTreelessClassIsStillTreeless()
    {
        // **The exemption above, asserted rather than tolerated, so it cannot outlive its reason.**
        // M6-07a ships the Emberwright's numbers and M6-08 ships its tree, so between the two there
        // is one authored class with no tree — which is the state the sweep above exists to refuse,
        // and it is refusing something true. Skipped by name rather than by a rule, because a rule
        // would quietly cover the next omission too.
        //
        // **This row goes red the day M6-08 merges, and the fix is to delete both it and the
        // `continue` above.** That is the point: a green suite is what says the exemption is gone.
        //
        // Unlike M5-02's, this class *is* reachable from a menu — the class-select screen binds a
        // card per catalog entry — and that is safe for a reason already tested: a run with no tree
        // builds no LevelUpFlow and banks its levels (NoTree_CanStarve_*, M3-08a rule 5).
        ContentCatalog catalog = ShippedCatalog();

        Assert.That(
            catalog.TryGetTreeFor(new ContentId(TreelessUntilM608), out SkillTreeSpec _),
            Is.False,
            $"'{TreelessUntilM608}' now has a tree, so EveryShippedCharacter_HasATree no longer "
                + "needs to skip it. M6-08 is merged: delete this row and the skip beside it — the "
                + "sweep is the stronger check and it should be doing the work.");

        // And the skip is present in the sweep, not merely declared: the class is on disk and the
        // sweep would otherwise report it.
        Assert.That(
            ContentValidationTests.PathsOf<CharacterDefinition>(),
            Has.Some.EndsWith("Emberwright.asset"),
            "the exemption names a class that is not authored, so it is covering nothing.");
    }

    [Test]
    public void BothShippedClasses_ResolveTheirOwnTree()
    {
        // **What `TheTreelessClass_IsStillTreeless` turned into when it expired.** That row said
        // "the Gravecaller has no tree, and this exemption is temporary"; it went red at M5-06b as
        // designed. What is worth keeping from it is the other half it also asserted — that a class
        // resolves *its own* tree rather than merely some tree — which is now a claim about two
        // classes instead of a claim about one plus an excuse.
        ContentCatalog catalog = ShippedCatalog();

        foreach (string id in new[] { "character.oathbound", "character.gravecaller" })
        {
            var characterId = new ContentId(id);

            Assert.That(
                catalog.TryGetTreeFor(characterId, out SkillTreeSpec tree),
                Is.True,
                $"'{id}' resolves no tree, so every level-up in a run of it pays Overflow.");

            Assert.That(tree, Is.Not.Null);
            Assert.That(tree.CharacterId, Is.EqualTo(characterId), $"'{id}' got someone else's.");
        }
    }

    [Test]
    public void NullTreeBranch_IsStillReachableInATest()
    {
        // The branch stays, and this row is why: the Editor's direct-Play path can still compose a
        // catalog with no tree at all (M3-12c rule 7), so M3-03 rule 10's handling is live code
        // even though no *shipped* run can reach it. Retiring it from the build is not the same as
        // deleting it.
        ContentCatalog withoutTrees = new ContentCatalog(
            new[] { CharacterAt("Assets/_Project/Data/Characters/Oathbound.asset") });

        Assert.That(
            () => withoutTrees.TryGetTreeFor(new ContentId("character.oathbound"), out _),
            Throws.Nothing);

        Assert.That(
            withoutTrees.TryGetTreeFor(new ContentId("character.oathbound"), out SkillTreeSpec tree),
            Is.False);

        Assert.That(tree, Is.Null);
    }

    // ---- Rule 9: no tree can starve ------------------------------------------------------------------

    [Test]
    public void NoTree_CanStarve_Exhaustively()
    {
        // **The only one of the three that is a proof.** The walk is over reachable *states*, not
        // over sequences: availability depends on the set of taken nodes and nothing else — the tier
        // gate counts a branch's taken, the keystone gate counts its whole branch, the upgrade gate
        // asks whether one particular node is owned — so two orders reaching the same set have the
        // same future, and memoising by set turns 12! orders into at most 2¹² states.
        int walked = 0;

        foreach (AuthoredTree tree in EveryTree())
        {
            var rules = new TreeRules(tree.Spec, tree.Catalog);

            if (rules.Count > ExhaustiveNodeLimit)
            {
                continue;
            }

            Assert.That(rules.Count, Is.LessThanOrEqualTo(64),
                $"{tree.Path}: the visited set is a 64-bit mask.");

            Explore(tree, rules, Ordinals(tree.Spec), new HashSet<ulong>(), new List<ContentId>(), 0UL);
            walked++;
        }

        Assert.That(walked, Is.GreaterThan(0),
            $"No tree in the project has {ExhaustiveNodeLimit} nodes or fewer, so this row proved "
                + "nothing. The sampled row is not a substitute for it — see its remarks.");
    }

    [Test]
    public void NoTree_CanStarve_Sampled()
    {
        // **A pass here is strong evidence and not a proof**, and the distinction starts to matter
        // at M7-04's eighty-one nodes. One thousand sequences from a fixed seed is a large sample of
        // a space that is 2²⁷ states for a full class; a starvable tree that only starves on one
        // rare ordering can survive this row. The exhaustive row is the one that cannot be fooled,
        // and it is the one that covers every tree the project ships today — this row runs over all
        // of them anyway, rather than only over trees too big to enumerate, so that it is never
        // decoration.
        var random = new Random(Seed);
        int sequences = 0;

        foreach (AuthoredTree tree in EveryTree())
        {
            var rules = new TreeRules(tree.Spec, tree.Catalog);
            var buffer = new ContentId[rules.Count];

            for (int s = 0; s < SampledSequences; s++)
            {
                SkillTree live = Fresh(rules);

                while (!live.IsFull)
                {
                    int count = live.Available(buffer);

                    AssertNotStarved(tree, live, count, $"sampled sequence {s}");

                    live.Take(buffer[random.Next(count)]);
                }

                sequences++;
            }
        }

        TestContext.WriteLine($"{sequences} sampled pick sequences walked from seed {Seed}.");
    }

    [Test]
    public void NoTree_CanStarve_SingleBranchFirst()
    {
        // The pathological order, enumerated rather than sampled: a player who pours everything into
        // one branch is exactly the player M3-08a rule 2 was worried about, and there are only six
        // ways to order three branches.
        foreach (AuthoredTree tree in EveryTree())
        {
            var rules = new TreeRules(tree.Spec, tree.Catalog);
            var buffer = new ContentId[rules.Count];

            foreach (int[] order in Orderings(tree.Spec.Branches.Count))
            {
                SkillTree live = Fresh(rules);

                while (!live.IsFull)
                {
                    int count = live.Available(buffer);

                    AssertNotStarved(tree, live, count, $"branch order [{string.Join(",", order)}]");

                    live.Take(EarliestInOrder(tree.Spec, buffer, count, order));
                }
            }
        }
    }

    // ---- The walk -------------------------------------------------------------------------------------

    private static void Explore(
        AuthoredTree tree,
        TreeRules rules,
        IReadOnlyDictionary<ContentId, int> ordinals,
        HashSet<ulong> visited,
        List<ContentId> taken,
        ulong mask)
    {
        SkillTree live = Fresh(rules);
        live.Restore(taken);

        if (live.IsFull)
        {
            return;
        }

        var buffer = new ContentId[rules.Count];
        int count = live.Available(buffer);

        AssertNotStarved(tree, live, count, $"after taking [{string.Join(", ", taken)}]");

        for (int i = 0; i < count; i++)
        {
            ContentId next = buffer[i];
            ulong reached = mask | (1UL << ordinals[next]);

            if (!visited.Add(reached))
            {
                continue;
            }

            taken.Add(next);
            Explore(tree, rules, ordinals, visited, taken, reached);
            taken.RemoveAt(taken.Count - 1);
        }
    }

    private static void AssertNotStarved(AuthoredTree tree, SkillTree live, int count, string how)
    {
        Assert.That(count, Is.GreaterThan(0),
            $"{tree.Path}: starved. {live.TakenCount} of {tree.Spec.NodeCount} nodes taken and "
                + $"nothing is available — {how}. A blocked-but-unfull tree turns the player's pick "
                + "into Overflow, which is CH §5.2's consolation being paid out for an authoring "
                + "mistake. Usually an Upgrade whose parent can be left untaken while the rest of "
                + "its branch is spent (M3-08a rule 2).");
    }

    /// <summary>The first available node belonging to the earliest branch in <paramref name="order"/>.</summary>
    private static ContentId EarliestInOrder(
        SkillTreeSpec spec,
        IReadOnlyList<ContentId> available,
        int count,
        IReadOnlyList<int> order)
    {
        for (int position = 0; position < order.Count; position++)
        {
            for (int i = 0; i < count; i++)
            {
                spec.TryLocate(available[i], out int branch, out int _);

                if (branch == order[position])
                {
                    return available[i];
                }
            }
        }

        // Unreachable: the caller has already asserted that something is available, and every
        // available node is in some branch.
        return available[0];
    }

    /// <summary>Every ordering of <paramref name="count"/> branch indices — six, for three.</summary>
    private static IEnumerable<int[]> Orderings(int count)
    {
        var indices = new int[count];

        for (int i = 0; i < count; i++)
        {
            indices[i] = i;
        }

        return Permute(indices, 0);
    }

    private static IEnumerable<int[]> Permute(int[] indices, int from)
    {
        if (from == indices.Length - 1)
        {
            yield return (int[])indices.Clone();

            yield break;
        }

        for (int i = from; i < indices.Length; i++)
        {
            (indices[from], indices[i]) = (indices[i], indices[from]);

            foreach (int[] permutation in Permute(indices, from + 1))
            {
                yield return permutation;
            }

            (indices[from], indices[i]) = (indices[i], indices[from]);
        }
    }

    private static Dictionary<ContentId, int> Ordinals(SkillTreeSpec spec)
    {
        var ordinals = new Dictionary<ContentId, int>(spec.NodeCount);

        foreach (ContentId id in EveryNodeOf(spec))
        {
            ordinals[id] = ordinals.Count;
        }

        return ordinals;
    }

    // ---- Building over the shipped assets ---------------------------------------------------------------

    private static SkillTree Fresh(TreeRules rules) =>
        new SkillTree(rules, Registry(), new SilentEvents());

    /// <summary>
    /// Every primitive the shipped content can carry, bound to a handler that does nothing.
    /// </summary>
    /// <remarks>
    /// <c>SkillTree</c>'s constructor refuses a tree whose nodes carry a primitive nothing answers
    /// for, which is what makes <c>EffectRegistry.Apply</c>'s throw unreachable in a live run. The
    /// walk needs the gating and not the outcomes — what a node <em>does</em> is M3-12a's and
    /// M3-12b's — so the handlers are empty on purpose, and a new primitive that arrives without a
    /// line here fails loudly rather than silently narrowing the walk.
    /// </remarks>
    private static EffectRegistry Registry()
    {
        var registry = new EffectRegistry();

        registry.Register<ModifyStat>(new Ignoring<ModifyStat>());
        registry.Register<ModifySkillCooldown>(new Ignoring<ModifySkillCooldown>());
        registry.Register<KnockbackOnSwing>(new Ignoring<KnockbackOnSwing>());
        registry.Register<GrantShield>(new Ignoring<GrantShield>());
        registry.Register<SpawnHealZone>(new Ignoring<SpawnHealZone>());

        // **The sixth, and it arrived exactly as the remark above predicted: loudly.** M5-06b's
        // Exhume is the first shipped node to cast a RaiseMinions, and the three starve walks went
        // red together with `no handler is registered for it`.
        //
        // **Registered unconditionally here, unlike in RunSession.Start**, which registers it only
        // for a class that has an army — because these walks are about tier gating and nothing
        // else. Whether a *run* can answer for the primitive is M5-06a rule 11's refusal, which
        // RunSession owns and RunSessionTests asserts; a walk that skipped the Gravecaller's tree
        // for it would stop proving the one thing it exists to prove.
        registry.Register<RaiseMinions>(new Ignoring<RaiseMinions>());

        return registry;
    }

    private static IEnumerable<AuthoredTree> EveryTree()
    {
        ContentCatalog catalog = ShippedCatalog();

        foreach (string path in ContentValidationTests.PathsOf<SkillTreeDefinition>())
        {
            var definition = AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(path);

            if (definition != null)
            {
                yield return new AuthoredTree(path, definition.ToSpec(), catalog);
            }
        }
    }

    private static AuthoredTree TreeAt(string path)
    {
        var definition = AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(path);

        Assert.That(definition, Is.Not.Null, $"No SkillTreeDefinition at {path}.");

        return new AuthoredTree(path, definition.ToSpec(), ShippedCatalog());
    }

    private static CharacterSpec CharacterAt(string path)
    {
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

        Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {path}.");

        return definition.ToSpec();
    }

    /// <summary>
    /// A catalog over <em>every</em> asset in the project rather than over <c>BootScope</c>'s lists.
    /// </summary>
    /// <remarks>
    /// The difference is the whole point of rule 1: an asset in <c>Data/</c> that no installer holds
    /// is content the next task to add a line will ship, and a tree whose nodes only resolve against
    /// a boot list is a tree that breaks the moment somebody registers it.
    /// </remarks>
    private static ContentCatalog ShippedCatalog()
    {
        var characters = new List<CharacterSpec>();
        var enemies = new List<EnemySpec>();
        var modes = new List<ModeSpec>();
        var skills = new List<SkillSpec>();
        var trees = new List<SkillTreeSpec>();

        foreach (string path in ContentValidationTests.PathsOf<CharacterDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

            if (asset != null)
            {
                characters.Add(asset.ToSpec());
            }
        }

        foreach (string path in ContentValidationTests.PathsOf<EnemyDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);

            if (asset != null)
            {
                enemies.Add(asset.ToSpec());
            }
        }

        foreach (string path in ContentValidationTests.PathsOf<ModeDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<ModeDefinition>(path);

            if (asset != null)
            {
                modes.Add(asset.ToSpec());
            }
        }

        foreach (string path in ContentValidationTests.PathsOf<SkillDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<SkillDefinition>(path);

            if (asset != null)
            {
                skills.Add(asset.ToSpec());
            }
        }

        foreach (string path in ContentValidationTests.PathsOf<SkillTreeDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(path);

            if (asset != null)
            {
                trees.Add(asset.ToSpec());
            }
        }

        return new ContentCatalog(characters, enemies, modes, skills, trees);
    }

    private static IEnumerable<ContentId> EveryNodeOf(SkillTreeSpec spec)
    {
        for (int b = 0; b < spec.Branches.Count; b++)
        {
            SkillBranchSpec branch = spec.Branches[b];

            for (int t = 1; t <= branch.TierCount; t++)
            {
                foreach (ContentId id in branch.Tier(t))
                {
                    yield return id;
                }
            }
        }
    }

    /// <summary>One tree asset, its converted spec, and the catalog its ids resolve against.</summary>
    private readonly struct AuthoredTree
    {
        internal AuthoredTree(string path, SkillTreeSpec spec, ContentCatalog catalog)
        {
            Path = path;
            Spec = spec;
            Catalog = catalog;
        }

        internal string Path { get; }

        internal SkillTreeSpec Spec { get; }

        internal ContentCatalog Catalog { get; }
    }

    /// <summary>A handler that answers for a primitive and does nothing with it.</summary>
    private sealed class Ignoring<TEffect> : IEffectHandler<TEffect>
        where TEffect : IEffect
    {
        public void Apply(TEffect effect, object source)
        {
        }

        public void Remove(TEffect effect, object source)
        {
        }
    }
}
