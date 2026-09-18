using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;

namespace Soulvail.Tests.Core.Content;

/// <summary>
/// <c>SkillTreeSpec</c> and <c>SkillBranchSpec</c>: the shape CH §5 actually ships, the guards that
/// keep a tree readable, and the locate that M3-03 and M3-04 lean on.
/// </summary>
/// <remarks>
/// The trees here hold ids and nothing resolves them — a tree names nodes, and whether those nodes
/// exist is the catalog's question and M3-03's sweep. That is the same separation rule 9 states for
/// Keystone placement, and it is why no row in this file builds a <c>SkillSpec</c>.
/// </remarks>
[TestFixture]
public sealed class SkillTreeSpecTests
{
    // ---- Rule 7: the layered shape, which is the owner's ruling at M3-00a -------------------------

    [Test]
    public void Tree_RecordsShape()
    {
        // Two nodes a tier for four tiers plus a keystone — nine a branch, twenty-seven a class.
        // CH §5's own table says 27 and also says "8 (7 + 1 Keystone)" a branch, and 3 × 8 is 24;
        // the layered shape is what makes every sentence in CH §5–5.1 true at once. The doc line is
        // flagged for the owner, not edited (the GD §12.1 precedent).
        SkillTreeSpec tree = Tree(Branch("a", 2, 2, 2, 2, 1), Branch("b", 2, 2, 2, 2, 1), Branch("c", 2, 2, 2, 2, 1));

        Assert.That(tree.NodeCount, Is.EqualTo(27));
        Assert.That(tree.Branches.Count, Is.EqualTo(SkillTreeSpec.BranchCount));
        Assert.That(SkillTreeSpec.BranchCount, Is.EqualTo(3));

        SkillBranchSpec first = tree.Branches[0];

        Assert.That(first.TierCount, Is.EqualTo(5));
        Assert.That(first.NodeCount, Is.EqualTo(9));
        Assert.That(first.Tier(1).Count, Is.EqualTo(2), "A tier holds one or more nodes (rule 7).");
        Assert.That(first.Tier(5).Count, Is.EqualTo(1), "The keystone tier holds exactly one.");

        // Tier is 1-based, CH §5's own numbering, and both ends are refused.
        Assert.That(first.Tier(1)[0], Is.EqualTo(new ContentId("skill.a.t1.n0")));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Tier(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Tier(6));

        Assert.That(first.NameKey, Is.EqualTo(new LocKey("tree.oathbound.a")));
    }

    // ---- Rule 8: exactly three branches, one to eight tiers, ids that appear once ----------------

    [Test]
    public void Tree_TwoBranches_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new SkillTreeSpec(
                TreeId(),
                CharacterId(),
                new[] { Branch("a", 1), Branch("b", 1) }));
    }

    [Test]
    public void Tree_FourBranches_Throws()
    {
        // Refused as loudly as two, because CH §5's whole claim for a fixed count is that the UI is
        // built once: a fourth branch is a screen nobody drew.
        Assert.Throws<ArgumentException>(
            () => new SkillTreeSpec(
                TreeId(),
                CharacterId(),
                new[] { Branch("a", 1), Branch("b", 1), Branch("c", 1), Branch("d", 1) }));
    }

    [Test]
    public void Branch_NineTiers_Throws()
    {
        Assert.That(SkillTreeSpec.MaxTiers, Is.EqualTo(8));

        Assert.Throws<ArgumentException>(() => Branch("a", 1, 1, 1, 1, 1, 1, 1, 1, 1));

        // Eight is the ceiling rather than one below it, or the guard above would be satisfied by a
        // constructor that refused the shape CH §5 actually draws.
        Assert.DoesNotThrow(() => Branch("a", 1, 1, 1, 1, 1, 1, 1, 1));
    }

    [Test]
    public void Branch_EmptyTier_Throws()
    {
        // Gating counts tiers by position (CH §5), so an empty tier is an unreachable rung rather
        // than a gap that closes itself.
        Assert.Throws<ArgumentException>(
            () => new SkillBranchSpec(
                BranchName("a"),
                new IReadOnlyList<ContentId>[] { Tier("a", 1, 2), Array.Empty<ContentId>() }));
    }

    [Test]
    public void Branch_NoTiers_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new SkillBranchSpec(BranchName("a"), Array.Empty<IReadOnlyList<ContentId>>()));
    }

    [Test]
    public void Tree_DuplicateIdAcrossBranches_Throws()
    {
        // The case a branch cannot see for itself, which is why uniqueness is checked once by the
        // tree rather than three times by its branches.
        var shared = new ContentId("skill.shared.node");

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new SkillTreeSpec(
                TreeId(),
                CharacterId(),
                new[]
                {
                    new SkillBranchSpec(BranchName("a"), new IReadOnlyList<ContentId>[] { new[] { shared } }),
                    new SkillBranchSpec(BranchName("b"), new IReadOnlyList<ContentId>[] { new[] { shared } }),
                    Branch("c", 1),
                }));

        Assert.That(ex.Message, Does.Contain("skill.shared.node"));
    }

    [Test]
    public void Tree_DuplicateIdWithinATier_Throws()
    {
        var shared = new ContentId("skill.shared.node");

        Assert.Throws<ArgumentException>(
            () => new SkillTreeSpec(
                TreeId(),
                CharacterId(),
                new[]
                {
                    new SkillBranchSpec(
                        BranchName("a"),
                        new IReadOnlyList<ContentId>[] { new[] { shared, shared } }),
                    Branch("b", 1),
                    Branch("c", 1),
                }));
    }

    [Test]
    public void Tree_DefaultNodeId_Throws()
    {
        // default(ContentId) carries a null value past the struct's own constructor (AR §18.3), and
        // left alone it would surface as the catalog's "no skill with id ''" — pointing at content
        // that was never at fault.
        Assert.Throws<ArgumentException>(
            () => new SkillBranchSpec(
                BranchName("a"),
                new IReadOnlyList<ContentId>[] { new[] { default(ContentId) } }));
    }

    [Test]
    public void Tree_PartialBranchesAreLegal()
    {
        // Three branches of four with no keystone is M3-12's v1, and refusing it would refuse the
        // milestone's own content.
        SkillTreeSpec tree = Tree(Branch("a", 1, 1, 1, 1), Branch("b", 1, 1, 1, 1), Branch("c", 1, 1, 1, 1));

        Assert.That(tree.NodeCount, Is.EqualTo(12));
        Assert.That(tree.Branches[0].TierCount, Is.EqualTo(4));
    }

    // ---- Rule 10: locate is a probe, not a walk ---------------------------------------------------

    [Test]
    public void Tree_TryLocate()
    {
        SkillTreeSpec tree = Tree(Branch("a", 2, 2, 2, 2, 1), Branch("b", 2, 2, 2, 2, 1), Branch("c", 2, 2, 2, 2, 1));

        Assert.That(
            tree.TryLocate(new ContentId("skill.b.t3.n0"), out int branch, out int tier),
            Is.True);
        Assert.That(branch, Is.EqualTo(1), "The branch index is 0-based — an index into Branches.");
        Assert.That(tier, Is.EqualTo(3), "The tier is 1-based — CH §5's own numbering.");

        // And the node really is where it says it is.
        Assert.That(tree.Branches[branch].Tier(tier), Does.Contain(new ContentId("skill.b.t3.n0")));

        // The second node of a tier locates to the same rung, which is the whole of rule 7.
        Assert.That(tree.TryLocate(new ContentId("skill.b.t3.n1"), out int sibling, out int sameTier), Is.True);
        Assert.That(sibling, Is.EqualTo(1));
        Assert.That(sameTier, Is.EqualTo(3));

        Assert.That(tree.TryLocate(new ContentId("skill.stranger.node"), out branch, out tier), Is.False);
        Assert.That(tree.TryLocate(default, out _, out _), Is.False, "A default id is simply not in it.");
    }

    // ---- Guards ----------------------------------------------------------------------------------

    [Test]
    public void Tree_Guards()
    {
        SkillBranchSpec[] three = { Branch("a", 1), Branch("b", 1), Branch("c", 1) };

        Assert.Throws<ArgumentException>(() => new SkillTreeSpec(default, CharacterId(), three));
        Assert.Throws<ArgumentException>(() => new SkillTreeSpec(TreeId(), default, three));
        Assert.Throws<ArgumentNullException>(() => new SkillTreeSpec(TreeId(), CharacterId(), null));

        // Assert.Throws is an exact type match (Traps §7), and a null *entry* is a different
        // exception from a null *list* only if the code says so — here both are argument-null,
        // because a branch that is not there is not a branch with bad contents.
        Assert.Throws<ArgumentNullException>(
            () => new SkillTreeSpec(
                TreeId(), CharacterId(), new[] { Branch("a", 1), null, Branch("c", 1) }));
    }

    [Test]
    public void Branch_Guards()
    {
        // The branch's own name key, refused for SkillSpec rule 3's reason one type over: M3-09d
        // draws it above the column, and a default(LocKey) is a forgotten field.
        Assert.Throws<ArgumentException>(
            () => new SkillBranchSpec(default, new IReadOnlyList<ContentId>[] { Tier("a", 1, 1) }));

        Assert.Throws<ArgumentNullException>(() => new SkillBranchSpec(BranchName("a"), null));

        Assert.Throws<ArgumentNullException>(
            () => new SkillBranchSpec(
                BranchName("a"),
                new IReadOnlyList<ContentId>[] { Tier("a", 1, 1), null }));
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    private static ContentId TreeId() => new ContentId("tree.oathbound");

    private static ContentId CharacterId() => new ContentId("character.oathbound");

    private static LocKey BranchName(string branch) => new LocKey("tree.oathbound." + branch);

    private static SkillTreeSpec Tree(params SkillBranchSpec[] branches) =>
        new SkillTreeSpec(TreeId(), CharacterId(), branches);

    /// <summary>
    /// A branch whose tiers hold the given counts — <c>Branch("a", 2, 2, 2, 2, 1)</c> is CH §5's
    /// shipped shape. Ids are <c>skill.&lt;branch&gt;.t&lt;tier&gt;.n&lt;index&gt;</c>, so every id
    /// in a tree built from distinct branch letters is unique without the fixture having to say so.
    /// </summary>
    private static SkillBranchSpec Branch(string branch, params int[] tierSizes)
    {
        var tiers = new IReadOnlyList<ContentId>[tierSizes.Length];

        for (int t = 0; t < tierSizes.Length; t++)
        {
            tiers[t] = Tier(branch, t + 1, tierSizes[t]);
        }

        return new SkillBranchSpec(BranchName(branch), tiers);
    }

    private static IReadOnlyList<ContentId> Tier(string branch, int tier, int size)
    {
        var ids = new ContentId[size];

        for (int i = 0; i < size; i++)
        {
            ids[i] = new ContentId($"skill.{branch}.t{tier}.n{i}");
        }

        return ids;
    }
}
