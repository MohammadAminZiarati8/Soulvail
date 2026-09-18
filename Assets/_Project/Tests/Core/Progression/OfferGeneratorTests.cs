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
/// <c>OfferGenerator</c>: three from what the tree makes available, weighted for variety, from the
/// <c>Offers</c> stream and no other.
/// </summary>
/// <remarks>
/// <para>
/// <b>The weight table is tested directly and the draw is tested against it.</b>
/// <c>OfferGenerator.Weight</c> is a pure static, so the five <c>Weight_</c> rows are arithmetic
/// rather than an inference from ten thousand samples; the two rows that make a draw land somewhere
/// uniform weights would not — <see cref="Draw_UsesTheWeights"/> and
/// <see cref="Draw_BoostsAnActiveThroughTheTable"/> — are what pin <c>Draw</c> to that same table
/// rather than to a second copy of it.
/// </para>
/// <para>
/// <b>No row here is about effects.</b> The registry is a real <c>EffectRegistry</c> over a handler
/// that does nothing, because every take in this file is made to change what is <em>available</em>
/// and no row reads a number off a player. <c>SkillTreeTests</c> is where a take moves a stat, and
/// duplicating its <c>PlayerStats</c> fixture here would be a page of numbers nothing asserts.
/// </para>
/// <para>
/// <b>There is no non-finite row, and that is not an omission.</b> The implied guard rows are a
/// validation row per new type, a null row per public constructor and a non-finite row per
/// <c>float</c> door — and this class has no <c>float</c> door: <c>Weight</c> takes a kind, an int,
/// a span of ints and an int, and <c>Draw</c> takes no <c>float</c> at all. The only float in the
/// class is the one it reads back out of the stream. M3-03 said the same for the same reason.
/// </para>
/// <para>
/// <b>The counting stream and the LCG are private to this file on purpose.</b> They are
/// <c>SpawnDirectorTests</c>' shape and its second copy; the ROADMAP parking lot promotes a shared
/// fake to <c>Tests/Core/Fakes/</c> on the third, and the spec's Out of scope says so in as many
/// words. Two is not the day.
/// </para>
/// </remarks>
[TestFixture]
public sealed class OfferGeneratorTests
{
    /// <summary>Every position of <c>TreeRulesTests</c>' 27-node tree, so a row can size by it.</summary>
    private const int FullTreeNodes = 27;

    /// <summary>The class every custom tree in this file claims to belong to.</summary>
    private const string CharacterId = "character.oathbound";

    private const string TrioId = "tree.trio";
    private const string WeightsId = "tree.weights";
    private const string SpreadId = "tree.spread";
    private const string BoostId = "tree.boost";

    private const float Tolerance = 1e-4f;

    // ---- From the available, never from the tree (rule 1) ---------------------------------------

    [Test]
    public void Draw_FromAvailableOnly()
    {
        SkillTree tree = FullTree();
        OfferGenerator generator = GeneratorFor(tree);

        // Six of twenty-seven: two nodes a tier, tier 1 of three branches. The buffer the generator
        // holds is sized by the *tree*, not by the offer, which is what lets `Available` fill it.
        ContentId[] available = AvailableOf(tree);

        Assert.That(available.Length, Is.EqualTo(6), "the fresh 27-tree opens with tier 1 of three branches.");

        var destination = new ContentId[OfferGenerator.DefaultOfferCount];

        int written = generator.Draw(tree, Offers(0.05f, 0.5f, 0.95f), OfferGenerator.DefaultOfferCount, destination);

        Assert.That(written, Is.EqualTo(3));

        for (int i = 0; i < written; i++)
        {
            Assert.That(available, Contains.Item(destination[i]), $"offer {i} is not an available node.");
            Assert.That(tree.IsAvailable(destination[i]), Is.True, $"offer {i} could not be taken.");
        }

        Assert.That(destination[0], Is.Not.EqualTo(destination[1]));
        Assert.That(destination[0], Is.Not.EqualTo(destination[2]));
        Assert.That(destination[1], Is.Not.EqualTo(destination[2]));
    }

    [Test]
    public void Draw_NothingAvailable_NoDraw()
    {
        SkillTree tree = FullTree();

        TakeEverything(tree);

        Assert.That(tree.IsFull, Is.True, "the row is about a tree with nothing left to offer.");

        OfferGenerator generator = GeneratorFor(tree);
        var random = new CountingRandom(new FixedRandom());
        var destination = new ContentId[OfferGenerator.DefaultOfferCount];

        int written = generator.Draw(tree, random.Offers, OfferGenerator.DefaultOfferCount, destination);

        Assert.That(written, Is.EqualTo(0));

        // Not "no offer": no *draw*. A draw taken anyway would move the stream by an amount that
        // depended on how full the tree was, which is the one thing a seed cannot survive.
        Assert.That(random.OffersDraws, Is.EqualTo(0), "a call with nothing to pick made a draw.");
    }

    // ---- One draw per pick, without replacement (rule 2) ----------------------------------------

    [Test]
    public void Draw_NeverRepeatsWithinAnOffer()
    {
        SkillTree tree = SpreadTree();
        OfferGenerator generator = GeneratorFor(tree);

        Assert.That(AvailableOf(tree).Length, Is.EqualTo(8));

        var destination = new ContentId[OfferGenerator.DefaultOfferCount];

        for (int seed = 1; seed <= 1_000; seed++)
        {
            int written = generator.Draw(tree, new Lcg(seed), OfferGenerator.DefaultOfferCount, destination);

            Assert.That(written, Is.EqualTo(3), $"seed {seed} offered {written}.");

            Assert.That(destination[0], Is.Not.EqualTo(destination[1]), $"seed {seed} repeated a node.");
            Assert.That(destination[0], Is.Not.EqualTo(destination[2]), $"seed {seed} repeated a node.");
            Assert.That(destination[1], Is.Not.EqualTo(destination[2]), $"seed {seed} repeated a node.");
        }
    }

    [Test]
    public void Draw_FewerThanCountWhenScarce()
    {
        SkillTree tree = TrioTree();

        tree.Take(new ContentId(N('a', 1)));

        Assert.That(AvailableOf(tree).Length, Is.EqualTo(2), "the row is about two candidates and three asked for.");

        OfferGenerator generator = GeneratorFor(tree);
        var random = new CountingRandom(new FixedRandom());
        var destination = new ContentId[OfferGenerator.DefaultOfferCount];

        int written = generator.Draw(tree, random.Offers, OfferGenerator.DefaultOfferCount, destination);

        Assert.That(written, Is.EqualTo(2));

        // Exactly two, not "at most three": the third pick does not happen, so it cannot spend a
        // draw on finding that out. This is rule 2 from the scarce end, and `Draw_OneDrawPerPick`
        // is the same rule from the other.
        Assert.That(random.OffersDraws, Is.EqualTo(2), "the pick that was never made still drew.");
    }

    [Test]
    public void Draw_OneDrawPerPickAndOnlyOffers()
    {
        SkillTree tree = FullTree();
        OfferGenerator generator = GeneratorFor(tree);

        var random = new CountingRandom(new FixedRandom(0.1f, 0.4f, 0.8f));
        var destination = new ContentId[OfferGenerator.DefaultOfferCount];

        generator.Draw(tree, random.Offers, OfferGenerator.DefaultOfferCount, destination);

        Assert.That(random.OffersDraws, Is.EqualTo(3), "three offers cost three draws, whatever the walk found.");

        // ADR-0011: a reroll or a Pact must never shift what the next stage is made of, and a wave
        // must never change what the next screen shows.
        Assert.That(random.OtherDraws, Is.EqualTo(0), "something outside the Offers stream was drawn on.");
    }

    [Test]
    public void Draw_IsDeterministic()
    {
        SkillTree tree = FullTree();
        OfferGenerator generator = GeneratorFor(tree);

        float[] script = { 0.13f, 0.77f, 0.41f };

        var first = new ContentId[OfferGenerator.DefaultOfferCount];
        var second = new ContentId[OfferGenerator.DefaultOfferCount];

        int a = generator.Draw(tree, Offers(script), OfferGenerator.DefaultOfferCount, first);
        int b = generator.Draw(tree, Offers(script), OfferGenerator.DefaultOfferCount, second);

        Assert.That(a, Is.EqualTo(3));
        Assert.That(b, Is.EqualTo(3));

        // The same ids in the same order: the order is part of the answer, because M3-08 draws
        // three cards left to right and a resumed run has to show the same three in the same places.
        for (int i = 0; i < a; i++)
        {
            Assert.That(second[i], Is.EqualTo(first[i]), $"offer {i} differed between two identical draws.");
        }

        Assert.That(first[0], Is.Not.EqualTo(first[1]), "a script that offered one node three times proves nothing.");
    }

    // ---- The weights, through a draw (rule 3) ---------------------------------------------------

    [Test]
    public void Draw_UsesTheWeights()
    {
        SkillTree tree = WeightsTree();
        OfferGenerator generator = GeneratorFor(tree);

        // Five candidates in tree order: a1, a2, a3, b1, c1. The first pick's 0.05 × 5 = 0.25 lands
        // in a1, which leaves a2, a3, b1, c1 with one offer already drawn from branch A.
        //
        // The second pick's 0.7 is the float that separates the table from a flat one:
        //   weighted  — 0.5 + 0.5 + 1 + 1 = 3.0, cumulative 0.5 / 1.0 / 2.0 / 3.0, 0.7 × 3 = 2.1 → c1
        //   uniform   — 1 + 1 + 1 + 1 = 4.0, cumulative 1 / 2 / 3 / 4,      0.7 × 4 = 2.8 → b1
        // The spec's 0.99 landed in c1 under both (2.97 and 3.96), so it was green against a
        // generator that ignored the table completely — corrected here, intent unchanged.
        var destination = new ContentId[2];

        int written = generator.Draw(tree, Offers(0.05f, 0.7f), 2, destination);

        Assert.That(written, Is.EqualTo(2));
        Assert.That(destination[0], Is.EqualTo(new ContentId(N('a', 1))), "the first pick did not land where the arithmetic says.");
        Assert.That(
            destination[1],
            Is.EqualTo(new ContentId(N('c', 1))),
            "the second pick landed where flat weights would put it — branch A's penalty was not applied.");
    }

    [Test]
    public void Draw_BoostsAnActiveThroughTheTable()
    {
        SkillTree tree = BoostTree();
        OfferGenerator generator = GeneratorFor(tree);

        Assert.That(tree.OwnedActives, Is.EqualTo(0), "the boost only applies below two owned Actives.");

        ContentId[] available = AvailableOf(tree);

        Assert.That(available.Length, Is.EqualTo(2), "the row needs exactly the Active and the Passive.");

        // Two candidates in tree order: a1 (Active), a2 (Passive), and nothing drawn yet.
        //   weighted — 2 + 1 = 3.0, cumulative 2.0 / 3.0, 0.6 × 3 = 1.8 → a1, the Active
        //   uniform  — 1 + 1 = 2.0, cumulative 1.0 / 2.0, 0.6 × 2 = 1.2 → a2, the Passive
        //
        // `Draw_UsesTheWeights` pins the branch penalty and this pins the Active boost, because a
        // Draw that inlined one half of the table and forgot the other would pass on that row
        // alone. Two copies of one rule is how they come to disagree (M3-03's precedent).
        var destination = new ContentId[1];

        int written = generator.Draw(tree, Offers(0.6f), 1, destination);

        Assert.That(written, Is.EqualTo(1));
        Assert.That(
            destination[0],
            Is.EqualTo(new ContentId(N('a', 1))),
            "the pick landed where flat weights would put it — the Active boost was not applied.");
    }

    [Test]
    public void Draw_SpreadsBranches()
    {
        SkillTree tree = SpreadTree();
        OfferGenerator generator = GeneratorFor(tree);

        const int Seeds = 10_000;

        var destination = new ContentId[OfferGenerator.DefaultOfferCount];
        int allThreeFromA = 0;

        for (int seed = 1; seed <= Seeds; seed++)
        {
            int written = generator.Draw(tree, new Lcg(seed), OfferGenerator.DefaultOfferCount, destination);

            Assert.That(written, Is.EqualTo(3));

            if (BranchOf(tree, destination[0]) == 0
                && BranchOf(tree, destination[1]) == 0
                && BranchOf(tree, destination[2]) == 0)
            {
                allThreeFromA++;
            }
        }

        double rate = allThreeFromA / (double)Seeds;

        // Four available in A and two each in B and C.
        //   uniform-from-8 — C(4,3) / C(8,3) = 4 / 56 = 7.14 %
        //   the weights    — 0.5 × (1.5 / 5.5) × (0.5 / 4.5) = 1.52 %
        // Asserted against the weighted figure rather than against the spec's 3 % ceiling, because
        // a ceiling alone would also be met by a generator that never offered three from one branch
        // at all — which is the hard rule GD §13.1 does not want.
        Assert.That(
            rate,
            Is.EqualTo(0.0152).Within(0.005),
            $"{allThreeFromA} of {Seeds} offers were all from branch A ({rate:P2}). The weights say "
                + "≈1.52 % and uniform-from-8 would say 7.14 %.");

        Assert.That(rate, Is.LessThan(0.03), "the spec's ceiling, which the weighted figure clears by a factor of two.");
    }

    // ---- count, and what it costs (rules 5, 6) --------------------------------------------------

    [Test]
    public void Draw_CountIsAParameter()
    {
        SkillTree tree = FullTree();
        OfferGenerator generator = GeneratorFor(tree);

        // GD §13.4's Vigil offers two, and that is M6-06 passing 2 rather than a flag on this class.
        var destination = new ContentId[2];

        int written = generator.Draw(tree, Offers(0.2f, 0.8f), 2, destination);

        Assert.That(written, Is.EqualTo(2));
        Assert.That(destination[0], Is.Not.EqualTo(destination[1]));
    }

    [Test]
    public void Draw_AllocatesNothing()
    {
        SkillTree tree = FullTree();
        OfferGenerator generator = GeneratorFor(tree);

        // An empty script, so the stream returns 0.5 forever and never allocates either. The buffer
        // is a heap array built out here and converted to a Span at the call site inside the body:
        // a lambda cannot close over a ref struct, so a `stackalloc` here could not be measured at
        // all (Traps §7).
        IRandomStream offers = new FixedRandom().Offers;
        var destination = new ContentId[OfferGenerator.DefaultOfferCount];

        AllocationAssert.None(() => generator.Draw(tree, offers, OfferGenerator.DefaultOfferCount, destination));
    }

    // ---- Guards -------------------------------------------------------------------------------

    [Test]
    public void Draw_Guards()
    {
        SkillTree tree = FullTree();
        OfferGenerator generator = GeneratorFor(tree);

        var destination = new ContentId[OfferGenerator.DefaultOfferCount];

        Assert.Throws<ArgumentNullException>(
            () => generator.Draw(null, Offers(0.5f), 3, destination));

        Assert.Throws<ArgumentNullException>(
            () => generator.Draw(tree, null, 3, destination));

        // An offer of nothing is not an offer: M3-08 does not call this for a level with no pick.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => generator.Draw(tree, Offers(0.5f), 0, destination));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => generator.Draw(tree, Offers(0.5f), -1, destination));

        // Refused rather than truncated, `SkillTree.Available`'s rule one layer up: a buffer that
        // could not hold the offer would narrow it with nothing to say so.
        Assert.Throws<ArgumentException>(
            () => generator.Draw(tree, Offers(0.5f), 3, new ContentId[2]));
    }

    [Test]
    public void Draw_RefusesAnotherTreesRules()
    {
        // The generator is built over the eight-node tree and handed the three-node one. Nothing
        // would fail on its own: the candidate buffer is *longer* than the wrong tree needs, so
        // `Available` fills it happily and three ids from a tree this generator has never seen come
        // back looking exactly like an offer. Only a bigger wrong tree fails loudly.
        var generator = new OfferGenerator(SpreadTree().Rules);
        SkillTree other = TrioTree();

        var destination = new ContentId[OfferGenerator.DefaultOfferCount];

        ArgumentException named = Assert.Throws<ArgumentException>(
            () => generator.Draw(other, Offers(0.5f), 3, destination));

        Assert.That(named.Message, Does.Contain(SpreadId));
        Assert.That(named.Message, Does.Contain(TrioId), "the refusal does not name the tree it was handed.");

        // And the same refusal for a second TreeRules over the *same* spec, which is the wiring
        // mistake that would otherwise look right in every message: a tree is built once per run.
        var twin = new OfferGenerator(TrioTree().Rules);

        ArgumentException same = Assert.Throws<ArgumentException>(
            () => twin.Draw(other, Offers(0.5f), 3, destination));

        Assert.That(same.Message, Does.Contain("a second TreeRules"));
    }

    [Test]
    public void Generator_NullRules_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OfferGenerator(null));
    }

    // ---- The table, without a stream (rule 3) ---------------------------------------------------

    [Test]
    public void Weight_Base()
    {
        Assert.That(
            OfferGenerator.Weight(SkillKind.Passive, 0, new[] { 0, 0, 0 }, 0),
            Is.EqualTo(1f).Within(Tolerance));

        // A Keystone weighs the same: its branch gate has already made it the rarest thing in the
        // tree, and a second thumb on the scale would be that argument counted twice.
        Assert.That(
            OfferGenerator.Weight(SkillKind.Keystone, 0, new[] { 0, 0, 0 }, 0),
            Is.EqualTo(1f).Within(Tolerance));
    }

    [Test]
    public void Weight_SameBranchHalves()
    {
        Assert.That(
            OfferGenerator.Weight(SkillKind.Passive, 1, new[] { 0, 1, 0 }, 0),
            Is.EqualTo(0.5f).Within(Tolerance));

        // A third from one branch is possible and four times less likely than the first: GD §13.1
        // wants variety, not a rule that forbids a build.
        Assert.That(
            OfferGenerator.Weight(SkillKind.Passive, 1, new[] { 0, 2, 0 }, 0),
            Is.EqualTo(0.25f).Within(Tolerance));

        // And the penalty is the *candidate's own* branch, not any branch drawn from.
        Assert.That(
            OfferGenerator.Weight(SkillKind.Passive, 0, new[] { 0, 2, 0 }, 0),
            Is.EqualTo(1f).Within(Tolerance));
    }

    [Test]
    public void Weight_ActiveBoostedBelowTwo()
    {
        Assert.That(
            OfferGenerator.Weight(SkillKind.Active, 0, new[] { 0, 0, 0 }, 0),
            Is.EqualTo(2f).Within(Tolerance));

        Assert.That(
            OfferGenerator.Weight(SkillKind.Active, 0, new[] { 0, 0, 0 }, 1),
            Is.EqualTo(2f).Within(Tolerance));

        // CH §8 Q3's "if you own fewer than 2": the second one owned is where it stops.
        Assert.That(
            OfferGenerator.Weight(SkillKind.Active, 0, new[] { 0, 0, 0 }, 2),
            Is.EqualTo(1f).Within(Tolerance));

        Assert.That(
            OfferGenerator.Weight(SkillKind.Active, 0, new[] { 0, 0, 0 }, 5),
            Is.EqualTo(1f).Within(Tolerance));
    }

    [Test]
    public void Weight_UpgradeIsOne()
    {
        // An Upgrade's parent gate has already made it a considered offer rather than a random one.
        Assert.That(
            OfferGenerator.Weight(SkillKind.Upgrade, 0, new[] { 0, 0, 0 }, 0),
            Is.EqualTo(1f).Within(Tolerance));

        // Including while the player owns no Actives, which is the boost an Upgrade does not get.
        Assert.That(
            OfferGenerator.Weight(SkillKind.Upgrade, 2, new[] { 0, 0, 0 }, 0),
            Is.EqualTo(1f).Within(Tolerance));
    }

    [Test]
    public void Weight_Compounds()
    {
        // × 2 for the Active, × 0.5 for the one already drawn from its branch: exactly 1, which is
        // the number a version that applied only one of the two could not produce.
        Assert.That(
            OfferGenerator.Weight(SkillKind.Active, 0, new[] { 1, 0, 0 }, 0),
            Is.EqualTo(1f).Within(Tolerance));

        Assert.That(
            OfferGenerator.Weight(SkillKind.Active, 0, new[] { 2, 0, 0 }, 0),
            Is.EqualTo(0.5f).Within(Tolerance));
    }

    [Test]
    public void Weight_Guards()
    {
        // Branches are 0-based indices into drawnPerBranch, which holds one per branch.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OfferGenerator.Weight(SkillKind.Passive, -1, new[] { 0, 0, 0 }, 0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => OfferGenerator.Weight(SkillKind.Passive, 3, new[] { 0, 0, 0 }, 0));

        // A count of things that happened is not negative, and absorbing it would hide the mistake
        // that made it — the loop would simply skip and the weight would look right.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OfferGenerator.Weight(SkillKind.Passive, 0, new[] { -1, 0, 0 }, 0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => OfferGenerator.Weight(SkillKind.Passive, 0, new[] { 0, 0, 0 }, -1));

        // A fifth kind added without a line in the table would otherwise weigh 0 and never be
        // offered, and nothing anywhere would say so (`PlayerStats.Resolve`'s reason).
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OfferGenerator.Weight((SkillKind)99, 0, new[] { 0, 0, 0 }, 0));
    }

    // ---- Fixture --------------------------------------------------------------------------------

    /// <summary>The 27-node tree, over a registry that can answer for what its nodes carry.</summary>
    private static SkillTree FullTree() =>
        TreeOver(TreeRulesTests.FullTree(), TreeRulesTests.FullSkills());

    /// <summary>Three branches of one node each: the smallest tree that can run out of candidates.</summary>
    private static SkillTree TrioTree()
    {
        SkillTreeSpec spec = Tree(
            TrioId,
            OneTier('a', N('a', 1)),
            OneTier('b', N('b', 1)),
            OneTier('c', N('c', 1)));

        return TreeOver(spec, Passives(N('a', 1), N('b', 1), N('c', 1)));
    }

    /// <summary>Three available in A and one each in B and C — five candidates in tree order.</summary>
    private static SkillTree WeightsTree()
    {
        SkillTreeSpec spec = Tree(
            WeightsId,
            OneTier('a', N('a', 1), N('a', 2), N('a', 3)),
            OneTier('b', N('b', 1)),
            OneTier('c', N('c', 1)));

        return TreeOver(spec, Passives(N('a', 1), N('a', 2), N('a', 3), N('b', 1), N('c', 1)));
    }

    /// <summary>Four available in A and two each in B and C — the spec's statistical fixture.</summary>
    /// <remarks>
    /// Every node Passive, so the only term in the weight is the branch penalty and the 1.52 %
    /// figure is the penalty's alone.
    /// </remarks>
    private static SkillTree SpreadTree()
    {
        SkillTreeSpec spec = Tree(
            SpreadId,
            OneTier('a', N('a', 1), N('a', 2), N('a', 3), N('a', 4)),
            OneTier('b', N('b', 1), N('b', 2)),
            OneTier('c', N('c', 1), N('c', 2)));

        return TreeOver(
            spec,
            Passives(N('a', 1), N('a', 2), N('a', 3), N('a', 4), N('b', 1), N('b', 2), N('c', 1), N('c', 2)));
    }

    /// <summary>One Active and one Passive available, and nothing else left in the tree.</summary>
    /// <remarks>
    /// B and C hold one node each and both are taken in the builder, which is what reduces the
    /// candidate set to two without either of them being an Active — so <c>OwnedActives</c> is still
    /// 0 when the row draws.
    /// </remarks>
    private static SkillTree BoostTree()
    {
        SkillTreeSpec spec = Tree(
            BoostId,
            OneTier('a', N('a', 1), N('a', 2)),
            OneTier('b', N('b', 1)),
            OneTier('c', N('c', 1)));

        var skills = new List<SkillSpec>
        {
            ActiveNode(N('a', 1)),
            TreeRulesTests.Passive(N('a', 2)),
            TreeRulesTests.Passive(N('b', 1)),
            TreeRulesTests.Passive(N('c', 1)),
        };

        SkillTree tree = TreeOver(spec, skills);

        tree.Take(new ContentId(N('b', 1)));
        tree.Take(new ContentId(N('c', 1)));

        return tree;
    }

    private static SkillTree TreeOver(SkillTreeSpec spec, IReadOnlyList<SkillSpec> skills)
    {
        var rules = new TreeRules(spec, TreeRulesTests.Catalog(spec, skills));

        return new SkillTree(rules, Registry(), new SilentEvents());
    }

    /// <summary>A generator over the tree's own rules, which is how M3-08 builds one.</summary>
    private static OfferGenerator GeneratorFor(SkillTree tree) => new OfferGenerator(tree.Rules);

    /// <summary>Everything available right now, sized by the tree as <c>Available</c> requires.</summary>
    private static ContentId[] AvailableOf(SkillTree tree)
    {
        var buffer = new ContentId[tree.Rules.Count];

        int count = tree.Available(buffer);

        var available = new ContentId[count];

        Array.Copy(buffer, available, count);

        return available;
    }

    /// <summary>Takes whatever is available until nothing is, a node at a time.</summary>
    /// <remarks>
    /// Through <c>Available</c> rather than through a hand-written order, so the row that needs a
    /// full tree does not also have to restate CH §5's gate arithmetic to get there.
    /// </remarks>
    private static void TakeEverything(SkillTree tree)
    {
        var buffer = new ContentId[tree.Rules.Count];

        int count;

        while ((count = tree.Available(buffer)) > 0)
        {
            tree.Take(buffer[0]);
        }

        Assert.That(count, Is.EqualTo(0));
    }

    private static int BranchOf(SkillTree tree, ContentId id)
    {
        tree.Rules.TryLocate(id, out int branch, out _);

        return branch;
    }

    private static IRandomStream Offers(params float[] values) => new FixedRandom(values).Offers;

    private static EffectRegistry Registry()
    {
        var registry = new EffectRegistry();

        registry.Register<ModifyStat>(new Ignoring());

        return registry;
    }

    private static string N(char branch, int slot) => $"skill.{branch}{slot}";

    private static SkillTreeSpec Tree(string id, params SkillBranchSpec[] branches) =>
        new SkillTreeSpec(new ContentId(id), new ContentId(CharacterId), branches);

    /// <summary>A branch of exactly one tier, which is every custom tree in this file.</summary>
    /// <remarks>
    /// One tier deep, so every node in it is gated on nothing and the whole branch is available at
    /// once — which is what lets a row say "eight candidates" without taking anything first.
    /// </remarks>
    private static SkillBranchSpec OneTier(char letter, params string[] ids)
    {
        var tier = new ContentId[ids.Length];

        for (int i = 0; i < ids.Length; i++)
        {
            tier[i] = new ContentId(ids[i]);
        }

        return new SkillBranchSpec(new LocKey($"branch.{letter}"), new IReadOnlyList<ContentId>[] { tier });
    }

    private static IReadOnlyList<SkillSpec> Passives(params string[] ids)
    {
        var skills = new SkillSpec[ids.Length];

        for (int i = 0; i < ids.Length; i++)
        {
            skills[i] = TreeRulesTests.Passive(ids[i]);
        }

        return skills;
    }

    /// <summary>An Active, so <c>SkillKind.Active</c> can reach the table through a real draw.</summary>
    private static SkillSpec ActiveNode(string id) => new SkillSpec(
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
            new IEffect[] { TreeRulesTests.Damage(0.05f) }));

    /// <summary>A handler that accepts a <c>ModifyStat</c> and does nothing with it.</summary>
    /// <remarks>
    /// Real registration against a real <c>EffectRegistry</c>, so <c>SkillTree</c>'s constructor
    /// sweep is satisfied the way a run satisfies it — and a no-op body, because no row in this file
    /// reads a number off a player. <c>SkillTreeTests</c> owns the claim that taking a node moves
    /// one.
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

    /// <summary>A stream that says how many times it was drawn from.</summary>
    /// <remarks>
    /// <c>SpawnDirectorTests</c>' shape, private here for its reason: this is the second copy and
    /// the parking lot promotes a shared fake on the third.
    /// </remarks>
    private sealed class CountingStream : IRandomStream
    {
        private readonly IRandomStream _inner;

        public CountingStream(IRandomStream inner)
        {
            _inner = inner;
        }

        public int Draws { get; private set; }

        public float NextFloat()
        {
            Draws++;

            return _inner.NextFloat();
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            Draws++;

            return _inner.NextInt(minInclusive, maxExclusive);
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            Draws++;

            return _inner.Range(minInclusive, maxInclusive);
        }

        public bool Chance(float probability)
        {
            Draws++;

            return _inner.Chance(probability);
        }
    }

    /// <summary>Every stream counted separately, so "the Offers stream and no other" is checkable.</summary>
    private sealed class CountingRandom : IRandom
    {
        private readonly IRandom _inner;

        private readonly CountingStream _spawn;
        private readonly CountingStream _offers;
        private readonly CountingStream _affixes;
        private readonly CountingStream _drops;
        private readonly CountingStream _misc;

        public CountingRandom(IRandom inner)
        {
            _inner = inner;
            Seed = inner.Seed;

            _spawn = new CountingStream(inner.Spawn);
            _offers = new CountingStream(inner.Offers);
            _affixes = new CountingStream(inner.Affixes);
            _drops = new CountingStream(inner.Drops);
            _misc = new CountingStream(inner.Misc);
        }

        public int Seed { get; }

        public IRandomStream Spawn => _spawn;

        public IRandomStream Offers => _offers;

        public IRandomStream Affixes => _affixes;

        public IRandomStream Drops => _drops;

        public IRandomStream Misc => _misc;

        public int OffersDraws => _offers.Draws;

        public int OtherDraws => _spawn.Draws + _affixes.Draws + _drops.Draws + _misc.Draws;

        /// <summary>
        /// Straight through and deliberately not counted: a capture is a read of where a stream
        /// stands, not a draw from it.
        /// </summary>
        public RandomState Capture() => _inner.Capture();

        /// <inheritdoc cref="Capture" />
        public void Restore(in RandomState state) => _inner.Restore(state);
    }

    /// <summary>
    /// A generator, for the two rows that need ten thousand different answers rather than a script.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Knuth's 64-bit LCG constants, reading the top 24 bits so the low-order correlation an LCG is
    /// known for never reaches the value. One value is discarded at construction, because adjacent
    /// seeds otherwise start adjacent and a thousand of them in a row would not be a sample of
    /// anything.
    /// </para>
    /// <para>
    /// Not <c>SeededRandom</c>, which lives in <c>Soulvail.Game</c> and is not referenced from this
    /// assembly — and not <c>FixedRandom</c>, whose script is the point of it. Private, for
    /// <see cref="CountingStream"/>'s reason.
    /// </para>
    /// </remarks>
    private sealed class Lcg : IRandomStream
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong Increment = 1442695040888963407UL;

        /// <summary>2^-24, the step between two adjacent values this stream can return.</summary>
        private const float Scale = 1f / 16_777_216f;

        private ulong _state;

        internal Lcg(int seed)
        {
            _state = (uint)seed;

            NextFloat();
        }

        public float NextFloat()
        {
            _state = (_state * Multiplier) + Increment;

            // The top 24 bits, which is [0, 2^24 − 1], so the value is in [0, 1) and never 1 —
            // `IRandomStream`'s contract, and what lets a cumulative walk fall through to the last
            // candidate only on a rounding edge rather than on every maximum draw.
            return (_state >> 40) * Scale;
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxExclusive),
                    maxExclusive,
                    $"maxExclusive must be greater than minInclusive ({minInclusive}).");
            }

            return minInclusive + (int)(NextFloat() * (maxExclusive - minInclusive));
        }

        public float Range(float minInclusive, float maxInclusive) =>
            minInclusive + ((maxInclusive - minInclusive) * NextFloat());

        public bool Chance(float probability) => NextFloat() < probability;
    }
}
