using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Progression;

/// <summary>
/// CH §5.4's borrowed branch as a run plays it: what the walk order becomes, what the tier rule
/// asks of it, and what an offer drawn over four branches does.
/// </summary>
/// <remarks>
/// <para>
/// <b>The index space itself is <c>TreeRulesTests</c>'.</b> That file owns what <c>InstallSplash</c>
/// refuses and where a borrowed node locates; this one owns everything that happens afterwards,
/// which is <c>SkillTree</c> and <c>OfferGenerator</c> — the two objects the borrowed branch has to
/// pass through before a player ever sees it.
/// </para>
/// <para>
/// <b>Every row splashes through <see cref="Splash"/>, which makes both calls.</b>
/// <c>TreeRules.InstallSplash</c> and <c>SkillTree.OnSplashInstalled</c> are one step of one caller
/// — between them the pair disagrees about how many nodes the run has — so a fixture that made them
/// separately would be testing a state production code never reaches.
/// </para>
/// <para>
/// <b>The content is <c>TreeRulesTests</c>' 27-node tree plus a nine-node branch of a second class,
/// eight of which come over.</b> Letters <c>a</c>, <c>b</c> and <c>c</c> are the primary's and
/// <c>x</c> is the borrowed one throughout, so an id says which side of the seam it is on.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SplashBranchTests
{
    /// <summary>The primary's 27 and the borrowed branch's 8.</summary>
    private const int SplashedNodes = 35;

    /// <summary>What a fresh splashed tree offers: tier 1 of four branches, two nodes each.</summary>
    private const int FreshCandidates = 8;

    // ---- The walk order, which is the seed contract (rule 5) -----------------------------------

    [Test]
    public void Tree_TheWalkAppendsRatherThanInterleaves()
    {
        SkillTree tree = Splashed();

        ContentId[] walk = AvailableOf(tree);

        Assert.That(walk.Length, Is.EqualTo(FreshCandidates));

        // **Every primary id before every borrowed one, and the primary's own order untouched.**
        // M3-04 walks this with one draw per pick, so the same seed against the same tree state has
        // to yield the same offer (AR §18.3): interleaving the borrowed branch would change what
        // every seed in the game means, and appending it changes none of them.
        Assert.That(
            walk,
            Is.EqualTo(new[]
            {
                Id(TreeRulesTests.Node('a', 1, 'a')),
                Id(TreeRulesTests.Node('a', 1, 'b')),
                Id(TreeRulesTests.Node('b', 1, 'a')),
                Id(TreeRulesTests.Node('b', 1, 'b')),
                Id(TreeRulesTests.Node('c', 1, 'a')),
                Id(TreeRulesTests.Node('c', 1, 'b')),
                Id(TreeRulesTests.Node('x', 1, 'a')),
                Id(TreeRulesTests.Node('x', 1, 'b')),
            }));

        Assert.That(tree.Rules.Count, Is.EqualTo(SplashedNodes));
    }

    [Test]
    public void Tree_SeedsBeforeTheSplashAreUnchanged()
    {
        // One script, two runs: the same values in the same order is what "one seed" means to a
        // fake that hands its numbers over rather than generating them.
        float[] script = { 0.1f, 0.5f, 0.9f, 0.2f, 0.7f, 0.3f, 0.6f, 0.15f, 0.85f };

        SkillTree never = Primary();
        SkillTree later = Primary();

        var withoutSplash = new OfferGenerator(never.Rules);
        var withSplash = new OfferGenerator(later.Rules);

        IRandomStream neverOffers = new FixedRandom(script).Offers;
        IRandomStream laterOffers = new FixedRandom(script).Offers;

        // Three offers each, taking the first card every time so the two trees stay in step.
        for (int offer = 0; offer < 3; offer++)
        {
            ContentId[] left = Draw(withoutSplash, never, neverOffers, OfferGenerator.DefaultOfferCount);
            ContentId[] right = Draw(withSplash, later, laterOffers, OfferGenerator.DefaultOfferCount);

            Assert.That(right, Is.EqualTo(left), $"Offer {offer} differed before either splashed.");

            never.Take(left[0]);
            later.Take(right[0]);
        }

        Splash(later);

        // **And only afterwards do they diverge.** A draw that walks to the end of the candidates
        // finds a borrowed node in the run that splashed and a primary one in the run that did not
        // — the same number, two different trees.
        ContentId lastOfNever = Draw(withoutSplash, never, Offers(0.99f), 1)[0];
        ContentId lastOfLater = Draw(withSplash, later, Offers(0.99f), 1)[0];

        never.Rules.TryLocate(lastOfNever, out int neverBranch, out _);
        later.Rules.TryLocate(lastOfLater, out int laterBranch, out _);

        Assert.That(neverBranch, Is.LessThan(3), "A run that never splashed has three branches.");
        Assert.That(laterBranch, Is.EqualTo(3), "And the one that did ends its walk in the fourth.");
    }

    [Test]
    public void Tree_TakenFlagsSurviveTheRebuild()
    {
        SkillTree tree = Primary();

        ContentId[] picks =
        {
            Id(TreeRulesTests.Node('a', 1, 'a')),
            Id(TreeRulesTests.Node('a', 1, 'b')),
            Id(TreeRulesTests.Node('a', 2, 'a')),
            Id(TreeRulesTests.Node('b', 1, 'a')),
            Id(TreeRulesTests.Node('c', 1, 'a')),
        };

        for (int i = 0; i < picks.Length; i++)
        {
            tree.Take(picks[i]);
        }

        Splash(tree);

        // **The primary's ordinals do not move, so the flags are copied across by ordinal** — that
        // is the whole of what appending the branch buys, and a rebuild that reordered anything
        // would silently un-take somebody's build.
        for (int i = 0; i < picks.Length; i++)
        {
            Assert.That(tree.IsTaken(picks[i]), Is.True, $"'{picks[i]}' was taken and is not.");
        }

        Assert.That(tree.TakenCount, Is.EqualTo(5));
        Assert.That(tree.TakenIds, Is.EqualTo(picks), "Take order, which is what a save writes down.");
        Assert.That(tree.TakenInBranch(0), Is.EqualTo(3));
        Assert.That(tree.TakenInBranch(1), Is.EqualTo(1));
        Assert.That(tree.TakenInBranch(2), Is.EqualTo(1));
    }

    // ---- The tier rule over the borrowed branch (rule 7) ---------------------------------------

    [Test]
    public void Tree_TakenInBranchThreeStartsAtZero()
    {
        SkillTree tree = Primary();

        // Half the primary, drawn from Available so the row cannot take an order the gating would
        // have refused — which is the state a player arrives at CH §5.4's moment in.
        TakeUntil(tree, 13);

        Splash(tree);

        Assert.That(tree.TakenCount, Is.EqualTo(13));
        Assert.That(tree.TakenInBranch(3), Is.Zero, "Nothing of the borrowed branch has been taken.");

        // So the first splashed node a player can be offered is always a tier-1 one.
        Assert.That(tree.IsAvailable(Id(TreeRulesTests.Node('x', 1, 'a'))), Is.True);
        Assert.That(tree.IsAvailable(Id(TreeRulesTests.Node('x', 2, 'a'))), Is.False);
    }

    [Test]
    public void Tree_ABorrowedTierTwoNeedsABorrowedPick()
    {
        SkillTree tree = Splashed();

        var tierTwo = Id(TreeRulesTests.Node('x', 2, 'a'));

        Assert.That(tree.IsAvailable(tierTwo), Is.False);

        // **Its own branch, and the primary does not count.** CH §5.4: "gated by the same tier rule
        // (§5)" — so a splashed tier-2 node asks for one node of the *splashed* branch, counted in
        // `_takenInBranch[3]` and nowhere else.
        TakeUntil(tree, 6);

        Assert.That(tree.IsAvailable(tierTwo), Is.False, "Six primary picks open nothing borrowed.");

        tree.Take(Id(TreeRulesTests.Node('x', 1, 'a')));

        Assert.That(tree.TakenInBranch(3), Is.EqualTo(1));
        Assert.That(tree.IsAvailable(tierTwo), Is.True);
    }

    [Test]
    public void Tree_ASplashedPickDoesNotOpenThePrimary()
    {
        SkillTree tree = Splashed();

        var before = new int[3];

        for (int branch = 0; branch < 3; branch++)
        {
            before[branch] = tree.TakenInBranch(branch);
        }

        tree.Take(Id(TreeRulesTests.Node('x', 1, 'a')));

        for (int branch = 0; branch < 3; branch++)
        {
            Assert.That(
                tree.TakenInBranch(branch),
                Is.EqualTo(before[branch]),
                $"A borrowed pick moved branch {branch}'s count.");
        }

        Assert.That(tree.TakenInBranch(3), Is.EqualTo(1));

        // And the primary's own tier 2 is still shut, which is the same claim read off the gate.
        Assert.That(tree.IsAvailable(Id(TreeRulesTests.Node('a', 2, 'a'))), Is.False);
    }

    [Test]
    public void Tree_ABorrowedUpgradeStillNeedsItsParent()
    {
        // An Upgrade inside the borrowed branch whose parent came over with it — the ordinary case,
        // and the one `InstallSplash`' orphan check leaves alone.
        SkillBranchSpec branch = TreeRulesTests.Branch(
            'x',
            new[] { TreeRulesTests.Node('x', 1, 'a'), TreeRulesTests.Node('x', 1, 'b') },
            TreeRulesTests.One(TreeRulesTests.Node('x', 2, 'a')));

        IReadOnlyList<SkillSpec> skills = TreeRulesTests.Skills(
            TreeRulesTests.Passive(TreeRulesTests.Node('x', 1, 'a')),
            TreeRulesTests.Passive(TreeRulesTests.Node('x', 1, 'b')),
            TreeRulesTests.Upgrade(TreeRulesTests.Node('x', 2, 'a'), TreeRulesTests.Node('x', 1, 'b')),
            TreeRulesTests.SplashFillerSkills());

        SkillTree tree = Splashed(branch, skills);

        // One of the branch taken, so the tier gate is open and the parent gate is the only one
        // left to fail.
        tree.Take(Id(TreeRulesTests.Node('x', 1, 'a')));

        var thrown = Assert.Throws<InvalidOperationException>(
            () => tree.Take(Id(TreeRulesTests.Node('x', 2, 'a'))));

        Assert.That(thrown.Message, Does.Contain(TreeRulesTests.Node('x', 1, 'b')), "Names the parent.");

        tree.Take(Id(TreeRulesTests.Node('x', 1, 'b')));

        Assert.That(tree.IsAvailable(Id(TreeRulesTests.Node('x', 2, 'a'))), Is.True);
    }

    // ---- Full is both (rule 10) ----------------------------------------------------------------

    [Test]
    public void Tree_IsFullCountsBoth()
    {
        // M3-12's v1 shape for the primary — three branches of four, no keystone — and CH §5.4's
        // seven for the borrowed one, which is the arithmetic the rule states: twelve and seven.
        SkillTreeSpec spec = TreeRulesTests.Tree(
            TreeRulesTests.Shallow('a'),
            TreeRulesTests.Shallow('b'),
            TreeRulesTests.Shallow('c'));

        IReadOnlyList<SkillSpec> primarySkills = TreeRulesTests.Skills(
            TreeRulesTests.ShallowSkills('a'),
            TreeRulesTests.ShallowSkills('b'),
            TreeRulesTests.ShallowSkills('c'));

        SkillBranchSpec branch = TreeRulesTests.Branch(
            'x',
            new[]
            {
                TreeRulesTests.Node('x', 1, 'a'),
                TreeRulesTests.Node('x', 1, 'b'),
                TreeRulesTests.Node('x', 1, 'c'),
            },
            new[] { TreeRulesTests.Node('x', 2, 'a'), TreeRulesTests.Node('x', 2, 'b') },
            new[] { TreeRulesTests.Node('x', 3, 'a'), TreeRulesTests.Node('x', 3, 'b') },
            TreeRulesTests.One(TreeRulesTests.KeystoneId('x')));

        IReadOnlyList<SkillSpec> borrowed = TreeRulesTests.Skills(
            TreeRulesTests.Passive(TreeRulesTests.Node('x', 1, 'a')),
            TreeRulesTests.Passive(TreeRulesTests.Node('x', 1, 'b')),
            TreeRulesTests.Passive(TreeRulesTests.Node('x', 1, 'c')),
            TreeRulesTests.Passive(TreeRulesTests.Node('x', 2, 'a')),
            TreeRulesTests.Passive(TreeRulesTests.Node('x', 2, 'b')),
            TreeRulesTests.Passive(TreeRulesTests.Node('x', 3, 'a')),
            TreeRulesTests.Passive(TreeRulesTests.Node('x', 3, 'b')),
            TreeRulesTests.Keystone(TreeRulesTests.KeystoneId('x')),
            TreeRulesTests.SplashFillerSkills());

        SkillTree tree = TreeOver(spec, primarySkills);

        Assert.That(tree.IsFull, Is.False);

        Splash(tree, branch, borrowed);

        Assert.That(tree.Rules.NodeCountOf(3), Is.EqualTo(7), "CH §5.4: one branch minus its Keystone.");
        Assert.That(tree.Rules.Count, Is.EqualTo(19));

        TakeUntil(tree, 18);

        Assert.That(tree.IsFull, Is.False, "Eighteen of nineteen is not full, and Overflow is not owed.");

        TakeUntil(tree, 19);

        Assert.That(tree.IsFull, Is.True);
        Assert.That(AvailableOf(tree), Is.Empty, "A full tree offers nothing.");
    }

    // ---- The offer over four branches (rule 6) -------------------------------------------------

    [Test]
    public void Offer_DoesNotThrowOnABorrowedNode()
    {
        SkillTree tree = Splashed();
        var generator = new OfferGenerator(tree.Rules);

        // **The failure this task exists to prevent, asserted rather than described.** 0.99 walks
        // the cumulative weights to the end, which is a borrowed node — and the line that runs next
        // is `drawnPerBranch[branch]++`, which was a `stackalloc int[SkillTreeSpec.BranchCount]`
        // three long against a branch index of 3.
        ContentId[] offer = null;

        Assert.That(
            () => offer = Draw(generator, tree, Offers(0.99f, 0.99f, 0.99f), OfferGenerator.DefaultOfferCount),
            Throws.Nothing);

        // Two borrowed and one primary, and the arithmetic is exact: eight candidates of weight 1,
        // then seven of which the surviving borrowed one weighs 0.5, then six of the primary's.
        Assert.That(
            offer,
            Is.EqualTo(new[]
            {
                Id(TreeRulesTests.Node('x', 1, 'b')),
                Id(TreeRulesTests.Node('x', 1, 'a')),
                Id(TreeRulesTests.Node('c', 1, 'b')),
            }));
    }

    [Test]
    public void Offer_DrawsFromBothTrees()
    {
        SkillTree tree = Splashed();
        var generator = new OfferGenerator(tree.Rules);

        // A sweep rather than a sample: eight candidates of equal weight, so the first pick is a
        // function of where the draw lands and nothing else, and two of the eight are borrowed.
        // The expected share is 2/8 exactly, which is a claim rather than a confidence interval.
        const int Draws = 800;

        // Three values a draw since M6-05b — the pick, then the Pact roll's slot and chance, which
        // are spent whatever they find — so the sweep sits on every third and the other two pad.
        const int PerDraw = 1 + 2;

        var script = new float[Draws * PerDraw];

        for (int i = 0; i < Draws; i++)
        {
            script[i * PerDraw] = (i + 0.5f) / Draws;
        }

        IRandomStream offers = new FixedRandom(script).Offers;

        var seen = new Dictionary<ContentId, int>();
        int borrowed = 0;

        for (int i = 0; i < Draws; i++)
        {
            ContentId drawn = Draw(generator, tree, offers, 1)[0];

            tree.Rules.TryLocate(drawn, out int branch, out _);

            if (branch == 3)
            {
                borrowed++;
            }

            seen[drawn] = seen.TryGetValue(drawn, out int count) ? count + 1 : 1;
        }

        Assert.That(borrowed, Is.EqualTo(Draws / 4), "Two of eight equal weights is a quarter.");

        // Both of the borrowed branch's tier-1 nodes, not just whichever one the walk ends on.
        Assert.That(seen[Id(TreeRulesTests.Node('x', 1, 'a'))], Is.EqualTo(Draws / 8));
        Assert.That(seen[Id(TreeRulesTests.Node('x', 1, 'b'))], Is.EqualTo(Draws / 8));
    }

    [Test]
    public void Offer_TheSameBranchPenaltyCountsBranchThree()
    {
        Span<int> drawnPerBranch = stackalloc int[4];

        Assert.That(
            OfferGenerator.Weight(SkillKind.Passive, 3, drawnPerBranch, 0),
            Is.EqualTo(1f),
            "A first node from the borrowed branch weighs what a first node weighs.");

        drawnPerBranch[3] = 2;

        // **Halved per offer already drawn from the same branch, and the borrowed branch is not
        // silently exempt** — a fourth column with no penalty would crowd out the three the player
        // actually chose a class for.
        Assert.That(OfferGenerator.Weight(SkillKind.Passive, 3, drawnPerBranch, 0), Is.EqualTo(0.25f));

        // And it is the borrowed branch's own count: branch 0 is untouched by it.
        Assert.That(OfferGenerator.Weight(SkillKind.Passive, 0, drawnPerBranch, 0), Is.EqualTo(1f));
    }

    [Test]
    public void Offer_BuffersGrowOnceAndThenNotAgain()
    {
        SkillTree tree = Primary();

        // Built before the splash, which is when `LevelUpFlow` builds one: its buffers are sized
        // for twenty-seven nodes and the run is about to hold thirty-five.
        var generator = new OfferGenerator(tree.Rules);

        IRandomStream offers = new FixedRandom().Offers;
        var destination = new ContentId[OfferGenerator.DefaultOfferCount];

        Assert.That(generator.Draw(tree, offers, OfferGenerator.DefaultOfferCount, destination, out _), Is.EqualTo(3));

        Splash(tree);

        // **One grow.** Without it `SkillTree.Available` refuses a destination shorter than the
        // tree — the offer would die with a message about a buffer, on the first pick after the
        // moment CH §5.4's screen closed.
        Assert.That(
            () => generator.Draw(tree, offers, OfferGenerator.DefaultOfferCount, destination, out _),
            Throws.Nothing);

        // **Then none.** An offer is drawn once per level, so one array grow there is not the frame
        // path — but a generator that regrew on every draw would be allocating inside the one call
        // this class promises costs nothing (AR §14).
        AllocationAssert.None(
            () => generator.Draw(tree, offers, OfferGenerator.DefaultOfferCount, destination, out _),
            iterations: 10);
    }

    // ---- Guards ---------------------------------------------------------------------------------

    [Test]
    public void Tree_OnSplashInstalledWithNoSplash_Throws()
    {
        SkillTree tree = Primary();

        // The two calls are one step of one caller, so a rebuild with nothing to rebuild for is a
        // caller that has lost track of which half it is on rather than a no-op to absorb.
        Assert.Throws<InvalidOperationException>(() => tree.OnSplashInstalled());

        Assert.That(tree.Rules.BranchCount, Is.EqualTo(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => tree.TakenInBranch(3));
    }

    // ---- Content --------------------------------------------------------------------------------

    /// <summary>The 27-node tree, with no branch borrowed.</summary>
    private static SkillTree Primary() =>
        TreeOver(TreeRulesTests.FullTree(), TreeRulesTests.FullSkills());

    /// <summary>The 27-node tree with <c>TreeRulesTests.BorrowedBranch</c> installed.</summary>
    private static SkillTree Splashed()
    {
        SkillTree tree = Primary();

        Splash(tree);

        return tree;
    }

    /// <summary>The 27-node tree with <paramref name="branch"/> installed.</summary>
    private static SkillTree Splashed(SkillBranchSpec branch, IReadOnlyList<SkillSpec> skills)
    {
        SkillTree tree = Primary();

        Splash(tree, branch, skills);

        return tree;
    }

    private static SkillTree TreeOver(SkillTreeSpec spec, IReadOnlyList<SkillSpec> skills) =>
        new SkillTree(
            new TreeRules(spec, TreeRulesTests.Catalog(spec, skills)),
            Registry(),
            new SilentEvents());

    private static void Splash(SkillTree tree) =>
        Splash(tree, TreeRulesTests.BorrowedBranch(), TreeRulesTests.BorrowedSkills());

    /// <summary>
    /// The two calls that make a splash, in the order they are made — the shape M5-07a-ii's moment
    /// will have.
    /// </summary>
    private static void Splash(
        SkillTree tree,
        SkillBranchSpec branch,
        IReadOnlyList<SkillSpec> skills)
    {
        TreeRulesTests.Install(tree.Rules, branch, skills);

        tree.OnSplashInstalled();
    }

    /// <summary>Everything available right now, sized by the tree as <c>Available</c> requires.</summary>
    private static ContentId[] AvailableOf(SkillTree tree)
    {
        var buffer = new ContentId[tree.Rules.Count];

        int count = tree.Available(buffer);

        var available = new ContentId[count];

        Array.Copy(buffer, available, count);

        return available;
    }

    /// <summary>One offer, as an array of exactly what was written.</summary>
    private static ContentId[] Draw(
        OfferGenerator generator,
        SkillTree tree,
        IRandomStream offers,
        int count)
    {
        var destination = new ContentId[count];

        int written = generator.Draw(tree, offers, count, destination, out _);

        Assert.That(written, Is.EqualTo(count), "The fixture asked for more than the tree had.");

        return destination;
    }

    /// <summary>Takes from <c>Available</c> until <paramref name="taken"/> nodes are owned.</summary>
    /// <remarks>
    /// Through <c>Available</c> rather than a hand-written order, so a row that needs a part-played
    /// tree does not also have to restate CH §5's gate arithmetic to get there.
    /// </remarks>
    private static void TakeUntil(SkillTree tree, int taken)
    {
        var buffer = new ContentId[tree.Rules.Count];

        while (tree.TakenCount < taken)
        {
            int count = tree.Available(buffer);

            Assert.That(count, Is.GreaterThan(0), $"Nothing was available at {tree.TakenCount}.");

            tree.Take(buffer[0]);
        }
    }

    private static IRandomStream Offers(params float[] values) => new FixedRandom(values).Offers;

    private static EffectRegistry Registry()
    {
        var registry = new EffectRegistry();

        registry.Register<ModifyStat>(new Ignoring());

        return registry;
    }

    private static ContentId Id(string value) => new ContentId(value);

    /// <summary>A handler that answers for the fixture's one primitive and does nothing with it.</summary>
    /// <remarks>
    /// Real registration against a real <c>EffectRegistry</c>, so <c>SkillTree</c>'s constructor
    /// sweep — and the one <c>OnSplashInstalled</c> makes over the borrowed nodes — are satisfied
    /// the way a run satisfies them. No row here reads a number off a player; <c>SkillTreeTests</c>
    /// owns the claim that taking a node moves one.
    /// </remarks>
    private sealed class Ignoring : IEffectHandler<ModifyStat>
    {
        public void Apply(ModifyStat effect, object source)
        {
        }

        public void Remove(ModifyStat effect, object source)
        {
        }
    }
}
