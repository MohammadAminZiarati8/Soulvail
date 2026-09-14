using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Progression;

namespace Soulvail.Tests.Core.Progression;

/// <summary>
/// <c>TreeRules</c>: the sweep that refuses a badly authored tree at <c>Start</c>, and the
/// structural questions <c>SkillTree</c>'s gating is written in terms of.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every malformed tree here is built out of specs that are individually legal.</b> That is the
/// point of the fixture rather than a convenience: <c>SkillSpec</c> and <c>SkillTreeSpec</c> have
/// already refused everything a single asset can get wrong in isolation, so what is left — and all
/// this type exists for — are the rules about <em>a node and its neighbours</em>. A row that failed
/// because a spec constructor threw would be testing M3-02a again.
/// </para>
/// <para>
/// <b>A branch is 0-based and a tier is 1-based</b>, following <c>SkillTreeSpec.TryLocate</c>, so
/// <em>"branch 1"</em> in an expected message is the second branch. The rows that assert a message
/// say so out loud, because the two numbers next to each other are exactly where a reader would
/// assume they match.
/// </para>
/// </remarks>
[TestFixture]
public sealed class TreeRulesTests
{
    private const string TreeId = "tree.test";
    private const string CharacterId = "character.oathbound";

    // ---- Resolving every node (rule 1) ---------------------------------------------------------

    [Test]
    public void Rules_ResolvesEveryNode()
    {
        IReadOnlyList<SkillSpec> skills = FullSkills();
        SkillTreeSpec tree = FullTree();

        var rules = new TreeRules(tree, Catalog(tree, skills));

        // CH §5's shape: two nodes a tier for four tiers plus a keystone, nine a branch, three
        // branches. The number is asserted rather than assumed because everything below counts
        // against it.
        Assert.That(rules.Count, Is.EqualTo(27));
        Assert.That(skills.Count, Is.EqualTo(27), "The fixture authored one spec per position.");

        // Resolved, not merely counted: every id in the tree answers with the spec that was
        // registered under it, which is what makes `Skill` a probe rather than a lookup that can
        // fail mid-run.
        for (int i = 0; i < skills.Count; i++)
        {
            Assert.That(
                rules.Skill(skills[i].Id),
                Is.SameAs(skills[i]),
                $"'{skills[i].Id}' resolved to a different instance than the catalog holds.");
        }
    }

    [Test]
    public void Rules_UnknownNode_ThrowsNamingWhere()
    {
        SkillTreeSpec tree = FullTree();

        // One position left unauthored, and deliberately not the first: branch index 1 is the
        // *second* branch, and tier 3 is the third tier, so a message that named the position by
        // walk order or got either base wrong would read differently.
        var missing = new ContentId(Node('b', 3, 'a'));

        List<SkillSpec> skills = new List<SkillSpec>(FullSkills());

        Assert.That(
            skills.RemoveAll(spec => spec.Id == missing),
            Is.EqualTo(1),
            "The fixture failed to remove the one node this row is about.");

        var thrown = Assert.Throws<KeyNotFoundException>(
            () => new TreeRules(tree, Catalog(tree, skills)));

        Assert.That(thrown.Message, Does.Contain(TreeId), "Which tree.");
        Assert.That(thrown.Message, Does.Contain(missing.Value), "Which node.");
        Assert.That(thrown.Message, Does.Contain("branch 1"), "0-based, so the second branch.");
        Assert.That(thrown.Message, Does.Contain("tier 3"), "1-based, so the third tier.");
    }

    // ---- Keystone placement (rule 1) -----------------------------------------------------------

    [Test]
    public void Rules_KeystoneNotInLastTier_Throws()
    {
        // A keystone at tier 2 of a three-tier branch: its gating asks for every other node of the
        // branch, so the tier above it could never be a rung anything needed.
        SkillTreeSpec tree = Tree(
            Branch('a', One(Node('a', 1, 'a')), One(KeystoneId('a')), One(Node('a', 3, 'a'))),
            Plain('b'),
            Plain('c'));

        IReadOnlyList<SkillSpec> skills = Skills(
            Passive(Node('a', 1, 'a')),
            Keystone(KeystoneId('a')),
            Passive(Node('a', 3, 'a')),
            PlainSkills('b'),
            PlainSkills('c'));

        var thrown = Assert.Throws<ArgumentException>(
            () => new TreeRules(tree, Catalog(tree, skills)));

        Assert.That(thrown.Message, Does.Contain(KeystoneId('a')));
        Assert.That(thrown.Message, Does.Contain("tiers deep"));
    }

    [Test]
    public void Rules_KeystoneSharesItsTier_Throws()
    {
        // In the last tier, so the row above's check passes and this one is the reason it throws.
        SkillTreeSpec tree = Tree(
            Branch(
                'a',
                One(Node('a', 1, 'a')),
                new[] { KeystoneId('a'), Node('a', 2, 'b') }),
            Plain('b'),
            Plain('c'));

        IReadOnlyList<SkillSpec> skills = Skills(
            Passive(Node('a', 1, 'a')),
            Keystone(KeystoneId('a')),
            Passive(Node('a', 2, 'b')),
            PlainSkills('b'),
            PlainSkills('c'));

        var thrown = Assert.Throws<ArgumentException>(
            () => new TreeRules(tree, Catalog(tree, skills)));

        Assert.That(thrown.Message, Does.Contain(KeystoneId('a')));
        Assert.That(thrown.Message, Does.Contain("sole node"));
    }

    [Test]
    public void Rules_LastTierNeedNotBeKeystone()
    {
        // Three branches of two tiers of two, every node a Passive and no keystone anywhere. This
        // is M3-12's v1 shape, so refusing it would refuse the milestone's own content — which is
        // why the keystone rule is written as "a Keystone ends its branch" and never as "a branch
        // ends in a Keystone".
        SkillTreeSpec tree = Tree(Shallow('a'), Shallow('b'), Shallow('c'));

        IReadOnlyList<SkillSpec> skills = Skills(
            ShallowSkills('a'),
            ShallowSkills('b'),
            ShallowSkills('c'));

        TreeRules rules = null;

        Assert.DoesNotThrow(() => rules = new TreeRules(tree, Catalog(tree, skills)));

        Assert.That(rules.Count, Is.EqualTo(12), "Three branches of two tiers of two.");

        // And nothing in it is treated as a keystone, so nothing in it is gated as one.
        for (int i = 0; i < skills.Count; i++)
        {
            Assert.That(rules.IsKeystone(skills[i].Id), Is.False);
        }
    }

    // ---- An upgrade's parent (rule 1) ----------------------------------------------------------

    [Test]
    public void Rules_UpgradeParentInOtherBranch_Throws()
    {
        // The deadlock the rule exists to stop: branch A's tier 2 waits on a pick in branch B that
        // the player may never make, the offer silently narrows by one, and nothing reports it.
        SkillTreeSpec tree = Tree(
            Branch('a', One(Node('a', 1, 'a')), One(Node('a', 2, 'a'))),
            Plain('b'),
            Plain('c'));

        IReadOnlyList<SkillSpec> skills = Skills(
            Passive(Node('a', 1, 'a')),
            Upgrade(Node('a', 2, 'a'), Node('b', 1, 'a')),
            PlainSkills('b'),
            PlainSkills('c'));

        var thrown = Assert.Throws<ArgumentException>(
            () => new TreeRules(tree, Catalog(tree, skills)));

        Assert.That(thrown.Message, Does.Contain(Node('a', 2, 'a')));
        Assert.That(thrown.Message, Does.Contain("another branch"));
    }

    [Test]
    public void Rules_UpgradeParentAtSameOrHigherTier_Throws()
    {
        // Same tier: the parent's own gate opens at exactly the moment the child's does, so the
        // child can be offered before the skill it improves is owned.
        SkillTreeSpec sameTier = Tree(
            Branch('a', new[] { Node('a', 1, 'a'), Node('a', 1, 'b') }),
            Plain('b'),
            Plain('c'));

        IReadOnlyList<SkillSpec> sameTierSkills = Skills(
            Passive(Node('a', 1, 'a')),
            Upgrade(Node('a', 1, 'b'), Node('a', 1, 'a')),
            PlainSkills('b'),
            PlainSkills('c'));

        Assert.Throws<ArgumentException>(
            () => new TreeRules(sameTier, Catalog(sameTier, sameTierSkills)));

        // Higher: the parent's gate opens strictly later than the child's, which is the same
        // failure with a longer fuse.
        SkillTreeSpec higher = Tree(
            Branch('a', One(Node('a', 1, 'a')), One(Node('a', 2, 'a'))),
            Plain('b'),
            Plain('c'));

        IReadOnlyList<SkillSpec> higherSkills = Skills(
            Upgrade(Node('a', 1, 'a'), Node('a', 2, 'a')),
            Passive(Node('a', 2, 'a')),
            PlainSkills('b'),
            PlainSkills('c'));

        var thrown = Assert.Throws<ArgumentException>(
            () => new TreeRules(higher, Catalog(higher, higherSkills)));

        Assert.That(thrown.Message, Does.Contain("below"));
    }

    [Test]
    public void Rules_UpgradeParentBelowInBranch_IsLegal()
    {
        SkillTreeSpec tree = Tree(
            Branch('a', One(Node('a', 1, 'a')), One(Node('a', 2, 'a'))),
            Plain('b'),
            Plain('c'));

        IReadOnlyList<SkillSpec> skills = Skills(
            Passive(Node('a', 1, 'a')),
            Upgrade(Node('a', 2, 'a'), Node('a', 1, 'a')),
            PlainSkills('b'),
            PlainSkills('c'));

        TreeRules rules = null;

        Assert.DoesNotThrow(() => rules = new TreeRules(tree, Catalog(tree, skills)));

        Assert.That(rules.TryGetParent(new ContentId(Node('a', 2, 'a')), out ContentId parent), Is.True);
        Assert.That(parent.Value, Is.EqualTo(Node('a', 1, 'a')));
    }

    [Test]
    public void Rules_UpgradeParentNotInTheTree_Throws()
    {
        // The parent is authored — it is in the catalog — and simply is not a node of this tree, so
        // no amount of playing could ever own it. Its own message, because "not in this tree at
        // all" and "in the wrong branch" are different mistakes with different fixes.
        SkillTreeSpec tree = Tree(
            Branch('a', One(Node('a', 1, 'a')), One(Node('a', 2, 'a'))),
            Plain('b'),
            Plain('c'));

        IReadOnlyList<SkillSpec> skills = Skills(
            Passive(Node('a', 1, 'a')),
            Upgrade(Node('a', 2, 'a'), "skill.stranger"),
            Passive("skill.stranger"),
            PlainSkills('b'),
            PlainSkills('c'));

        var thrown = Assert.Throws<ArgumentException>(
            () => new TreeRules(tree, Catalog(tree, skills)));

        Assert.That(thrown.Message, Does.Contain("not in this tree"));
    }

    // ---- What gating asks for (rule 2) ---------------------------------------------------------

    [Test]
    public void Rules_RequiredTaken_Tier()
    {
        TreeRules rules = FullRules();

        // CH §5: tier N requires N − 1 nodes already taken in that branch.
        Assert.That(rules.RequiredTakenInBranch(new ContentId(Node('a', 1, 'a'))), Is.EqualTo(0));
        Assert.That(rules.RequiredTakenInBranch(new ContentId(Node('a', 3, 'a'))), Is.EqualTo(2));
        Assert.That(rules.RequiredTakenInBranch(new ContentId(Node('c', 4, 'b'))), Is.EqualTo(3));
    }

    [Test]
    public void Rules_RequiredTaken_Keystone()
    {
        TreeRules rules = FullRules();

        // **CH §5's "all preceding", which is stronger than the tier rule and not equal to it.** A
        // nine-node branch's keystone sits at tier 5, so the tier rule would have said 4; every
        // other node of the branch is 8. The two only coincide in the one-node-a-tier tree CH §5
        // draws, which is not the tree this game ships.
        Assert.That(rules.NodeCountOf(0), Is.EqualTo(9), "The fixture's branches are nine deep.");

        Assert.That(rules.RequiredTakenInBranch(new ContentId(KeystoneId('a'))), Is.EqualTo(8));

        Assert.That(
            rules.RequiredTakenInBranch(new ContentId(KeystoneId('a'))),
            Is.Not.EqualTo(4),
            "4 is what the tier rule alone would say, so this row fails if the keystone exception "
                + "is ever quietly dropped.");
    }

    [Test]
    public void Rules_TryLocateAndParent()
    {
        SkillTreeSpec tree = Tree(
            Branch('a', One(Node('a', 1, 'a')), One(Node('a', 2, 'a'))),
            Plain('b'),
            Plain('c'));

        IReadOnlyList<SkillSpec> skills = Skills(
            Passive(Node('a', 1, 'a')),
            Upgrade(Node('a', 2, 'a'), Node('a', 1, 'a')),
            PlainSkills('b'),
            PlainSkills('c'));

        var rules = new TreeRules(tree, Catalog(tree, skills));

        // A member, with both bases as documented: branch 0, tier 2.
        Assert.That(rules.TryLocate(new ContentId(Node('a', 2, 'a')), out int branch, out int tier), Is.True);
        Assert.That(branch, Is.EqualTo(0));
        Assert.That(tier, Is.EqualTo(2));

        // The second branch really is index 1, which is the claim every message in this file makes.
        Assert.That(rules.TryLocate(new ContentId(Node('b', 1, 'a')), out branch, out _), Is.True);
        Assert.That(branch, Is.EqualTo(1));

        // A stranger, and a default — neither is found, and neither throws: a caller filtering
        // candidates wants an answer.
        Assert.That(rules.TryLocate(new ContentId("skill.ghost"), out _, out _), Is.False);
        Assert.That(rules.TryLocate(default, out _, out _), Is.False);

        // TryGetParent is true for the Upgrade and false for every other kind, which is
        // `SkillSpec`'s both-directions rule read back out.
        Assert.That(rules.TryGetParent(new ContentId(Node('a', 2, 'a')), out ContentId parent), Is.True);
        Assert.That(parent.Value, Is.EqualTo(Node('a', 1, 'a')));

        Assert.That(rules.TryGetParent(new ContentId(Node('a', 1, 'a')), out parent), Is.False);
        Assert.That(parent.Value, Is.Null, "A kind with no parent answers with a default, not a stale id.");
    }

    [Test]
    public void Rules_IsKeystone()
    {
        TreeRules rules = FullRules();

        Assert.That(rules.IsKeystone(new ContentId(KeystoneId('b'))), Is.True);
        Assert.That(rules.IsKeystone(new ContentId(Node('b', 4, 'a'))), Is.False);

        // False rather than a throw for a stranger, for TryLocate's reason.
        Assert.That(rules.IsKeystone(new ContentId("skill.ghost")), Is.False);
    }

    // ---- Guards ---------------------------------------------------------------------------------

    [Test]
    public void Rules_NullArguments_Throw()
    {
        SkillTreeSpec tree = FullTree();
        ContentCatalog catalog = Catalog(tree, FullSkills());

        Assert.Throws<ArgumentNullException>(() => new TreeRules(null, catalog));
        Assert.Throws<ArgumentNullException>(() => new TreeRules(tree, null));
    }

    [Test]
    public void Rules_StrangerQuestions_Throw()
    {
        TreeRules rules = FullRules();

        // The three members that cannot answer about a node they do not hold. Loud rather than
        // zero, because a caller asking the wrong tree is a wiring mistake and a silent 0 from
        // RequiredTakenInBranch would read as "available immediately".
        Assert.Throws<KeyNotFoundException>(() => rules.Skill(new ContentId("skill.ghost")));
        Assert.Throws<KeyNotFoundException>(() => rules.RequiredTakenInBranch(new ContentId("skill.ghost")));
        Assert.Throws<KeyNotFoundException>(() => rules.TryGetParent(new ContentId("skill.ghost"), out _));
    }

    [Test]
    public void Rules_NodeCountOfAStrangerBranch_Throws()
    {
        TreeRules rules = FullRules();

        Assert.That(rules.NodeCountOf(0), Is.EqualTo(9));
        Assert.That(rules.NodeCountOf(2), Is.EqualTo(9));

        // 0-based, so 3 is one past the end of a three-branch tree.
        Assert.Throws<ArgumentOutOfRangeException>(() => rules.NodeCountOf(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rules.NodeCountOf(3));
    }

    [Test]
    public void Rules_CountsTheActives()
    {
        // **A fact about the tree, deliberately not a rule** (M3-06). `RunSession.Start` compares
        // it against `SkillRunner.MaxActives` and refuses a tree that would not fit, which is what
        // makes the runner's own capacity throw unreachable in a live run. The comparison lives
        // there rather than here because a Combat array size is not one of the tree's properties.
        Assert.That(
            FullRules().ActiveCount,
            Is.EqualTo(0),
            "The 27-node fixture is all Passives and Keystones, so the count is a real zero rather "
                + "than an uninitialised one — which is why the row below plants some.");

        SkillTreeSpec tree = FullTree();

        var skills = new List<SkillSpec>(FullSkills());

        // Three of branch 'a' swapped for Actives, in place, so the tree's shape is untouched and
        // only the kinds move.
        for (int tier = 1; tier <= 3; tier++)
        {
            string id = Node('a', tier, 'a');

            skills[skills.FindIndex(s => s.Id.Equals(new ContentId(id)))] = Active(id);
        }

        Assert.That(new TreeRules(tree, Catalog(tree, skills)).ActiveCount, Is.EqualTo(3));
    }

    // ---- Content --------------------------------------------------------------------------------

    /// <summary>The 27-node tree CH §5 ships: three branches of 2/2/2/2 plus a keystone.</summary>
    internal static SkillTreeSpec FullTree() => Tree(FullBranch('a'), FullBranch('b'), FullBranch('c'));

    /// <summary>One spec per position of <see cref="FullTree"/>, all Passive but the keystones.</summary>
    internal static IReadOnlyList<SkillSpec> FullSkills()
    {
        var skills = new List<SkillSpec>();

        foreach (char letter in Letters)
        {
            for (int tier = 1; tier <= 4; tier++)
            {
                skills.Add(Passive(Node(letter, tier, 'a')));
                skills.Add(Passive(Node(letter, tier, 'b')));
            }

            skills.Add(Keystone(KeystoneId(letter)));
        }

        return skills;
    }

    internal static TreeRules FullRules()
    {
        SkillTreeSpec tree = FullTree();

        return new TreeRules(tree, Catalog(tree, FullSkills()));
    }

    /// <summary>
    /// A catalog holding <paramref name="skills"/> and <paramref name="tree"/> and no characters.
    /// </summary>
    /// <remarks>
    /// No <c>CharacterSpec</c>, because nothing in this file resolves one: the catalog indexes a
    /// tree by the class it names without asking whether that class exists, and building a full
    /// character here would be a page of numbers no row reads.
    /// </remarks>
    internal static ContentCatalog Catalog(SkillTreeSpec tree, IReadOnlyList<SkillSpec> skills) =>
        new ContentCatalog(
            Array.Empty<CharacterSpec>(),
            null,
            null,
            skills,
            new[] { tree });

    internal static string Node(char branch, int tier, char slot) => $"skill.{branch}{tier}{slot}";

    internal static string KeystoneId(char branch) => $"skill.{branch}k";

    internal static SkillSpec Passive(string id) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Passive,
        new IEffect[] { Damage(0.05f) });

    internal static SkillSpec Keystone(string id) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Keystone,
        new IEffect[] { Damage(0.2f) });

    /// <summary>An Active in the same position a <see cref="Passive"/> would have held.</summary>
    internal static SkillSpec Active(string id) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Active,
        Array.Empty<IEffect>(),
        new ActiveSpec(
            8f,
            new TriggerSpec(new[]
            {
                new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 0.6f),
            }),
            new IEffect[] { Damage(0.5f) }));

    internal static SkillSpec Upgrade(string id, string parentId) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Upgrade,
        new IEffect[] { Damage(0.1f) },
        parentId: new ContentId(parentId));

    internal static ModifyStat Damage(float percent) =>
        new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, percent);

    /// <summary>The three branch letters, in index order.</summary>
    private static readonly char[] Letters = { 'a', 'b', 'c' };

    private static SkillTreeSpec Tree(params SkillBranchSpec[] branches) =>
        new SkillTreeSpec(new ContentId(TreeId), new ContentId(CharacterId), branches);

    /// <summary>A branch from tiers written as arrays of id strings, tier 1 first.</summary>
    private static SkillBranchSpec Branch(char letter, params string[][] tiers)
    {
        var copy = new IReadOnlyList<ContentId>[tiers.Length];

        for (int t = 0; t < tiers.Length; t++)
        {
            var ids = new ContentId[tiers[t].Length];

            for (int i = 0; i < tiers[t].Length; i++)
            {
                ids[i] = new ContentId(tiers[t][i]);
            }

            copy[t] = ids;
        }

        return new SkillBranchSpec(new LocKey($"branch.{letter}"), copy);
    }

    /// <summary>One branch of <see cref="FullTree"/>: four tiers of two, then the keystone.</summary>
    private static SkillBranchSpec FullBranch(char letter) => Branch(
        letter,
        new[] { Node(letter, 1, 'a'), Node(letter, 1, 'b') },
        new[] { Node(letter, 2, 'a'), Node(letter, 2, 'b') },
        new[] { Node(letter, 3, 'a'), Node(letter, 3, 'b') },
        new[] { Node(letter, 4, 'a'), Node(letter, 4, 'b') },
        One(KeystoneId(letter)));

    /// <summary>
    /// A filler branch: two tiers of one Passive each, so a row about branch A can supply the other
    /// two legally without saying anything about them.
    /// </summary>
    private static SkillBranchSpec Plain(char letter) =>
        Branch(letter, One(Node(letter, 1, 'a')), One(Node(letter, 2, 'a')));

    private static SkillSpec[] PlainSkills(char letter) => new[]
    {
        Passive(Node(letter, 1, 'a')),
        Passive(Node(letter, 2, 'a')),
    };

    /// <summary>
    /// M3-12's v1 shape for one branch: two tiers of two, every node ordinary and no keystone.
    /// </summary>
    private static SkillBranchSpec Shallow(char letter) => Branch(
        letter,
        new[] { Node(letter, 1, 'a'), Node(letter, 1, 'b') },
        new[] { Node(letter, 2, 'a'), Node(letter, 2, 'b') });

    private static SkillSpec[] ShallowSkills(char letter) => new[]
    {
        Passive(Node(letter, 1, 'a')),
        Passive(Node(letter, 1, 'b')),
        Passive(Node(letter, 2, 'a')),
        Passive(Node(letter, 2, 'b')),
    };

    private static string[] One(string id) => new[] { id };

    /// <summary>
    /// Flattens specs and groups of specs into one list, so a row can read as the content it
    /// authored plus "and two ordinary branches".
    /// </summary>
    private static IReadOnlyList<SkillSpec> Skills(params object[] parts)
    {
        var skills = new List<SkillSpec>();

        for (int i = 0; i < parts.Length; i++)
        {
            switch (parts[i])
            {
                case SkillSpec spec:
                    skills.Add(spec);
                    break;

                case SkillSpec[] group:
                    skills.AddRange(group);
                    break;

                default:
                    throw new ArgumentException(
                        $"parts[{i}] is a {parts[i]?.GetType().Name ?? "null"}, which this helper "
                            + "does not know how to flatten.",
                        nameof(parts));
            }
        }

        return skills;
    }
}
