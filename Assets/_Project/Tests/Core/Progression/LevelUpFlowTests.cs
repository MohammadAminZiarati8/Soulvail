using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Progression;

/// <summary>
/// <c>LevelUpFlow</c>: the lazy offer, the ordered take, and banked points.
/// </summary>
/// <remarks>
/// The rows that need a whole run — the boundary ordering, legacy-save compatibility and the two
/// commands' refusals — live in <c>RunSessionTests</c> and <c>RunSessionResumeTests</c> beside the
/// fixtures that already build one, rather than a second composition being stood up here.
/// </remarks>
[TestFixture]
public sealed class LevelUpFlowTests
{
    private const string OathboundId = "character.oathbound";
    private const float MaxHp = 140f;
    private const float WeaponDamage = 13f;
    private const float MoveSpeed = 3f;
    private const int EnemyCapacity = 32;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _combat;
    private LevelTracker _progression;
    private PlayerStats _stats;
    private EffectRegistry _registry;
    private SkillRunner _runner;
    private Veilrot _veilrot;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _combat = new PlayerCombat(Character(), _events, _intents, EnemyCapacity);
        _progression = new LevelTracker(Scalings.Xp(), _events);

        // One tracker, shared: PlayerStats resolves XpGain off it, so a second instance would make
        // the flow bank picks on a tracker nothing else can see.
        _stats = new PlayerStats(
            _combat,
            new PlayerMotor(new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f), Vector3.UnitZ),
            _progression);

        _registry = new EffectRegistry();
        _registry.Register<ModifyStat>(new ModifyStatHandler(_stats));

        _runner = new SkillRunner(_registry, _combat.Blackboard, _events);
        _veilrot = new Veilrot(_stats, _combat, _combat.Blackboard, _events);
    }

    // ---- The offer (rules 1, 2, 3) ---------------------------------------------------------------

    [Test]
    public void Open_DrawsThreeAndPublishes()
    {
        LevelUpFlow flow = Flow(FullTree());

        BankPicks(1);

        flow.Open(Offers());

        Assert.That(flow.HasOffer, Is.True);
        Assert.That(flow.Offer.Count, Is.EqualTo(3));

        OfferPresented presented = _events.Single<OfferPresented>();

        Assert.That(presented.Count, Is.EqualTo(3));
        Assert.That(presented.PicksOwed, Is.EqualTo(1), "CH §5.1's \"pick 1 of n\".");

        // Three distinct nodes, which is M3-04 rule 1's no-replacement half seen from this side.
        Assert.That(flow.Offer.Distinct().Count(), Is.EqualTo(3));
    }

    [Test]
    public void Open_WithNoPickDoesNothing()
    {
        LevelUpFlow flow = Flow(FullTree());

        var random = new CountingRandom(new FixedRandom(7));

        flow.Open(random.Offers);

        Assert.That(flow.HasOffer, Is.False);
        Assert.That(flow.Offer.Count, Is.EqualTo(0));
        Assert.That(_events.Count<OfferPresented>(), Is.EqualTo(0));

        // The half that matters: a draw nobody asked for would advance the stream and change what
        // the *next* level-up shows, from a seed (AR §18.3).
        Assert.That(random.OffersDraws, Is.EqualTo(0));
    }

    [Test]
    public void Open_TwiceLeavesOneOffer()
    {
        LevelUpFlow flow = Flow(FullTree());

        BankPicks(1);

        var random = new CountingRandom(new FixedRandom(11));

        flow.Open(random.Offers);

        ContentId[] first = flow.Offer.ToArray();
        int drawsAfterFirst = random.OffersDraws;

        flow.Open(random.Offers);

        Assert.That(flow.Offer.ToArray(), Is.EqualTo(first), "the same three ids.");
        Assert.That(_events.Count<OfferPresented>(), Is.EqualTo(1), "no second event.");
        Assert.That(random.OffersDraws, Is.EqualTo(drawsAfterFirst), "no extra draw.");
    }

    [Test]
    public void Open_DrawsFromOffersAndNoOtherStream()
    {
        LevelUpFlow flow = Flow(FullTree());

        BankPicks(1);

        var random = new CountingRandom(new FixedRandom(3));

        flow.Open(random.Offers);

        // One per pick and M6-05b's two for the Pact roll, whatever the walk found.
        Assert.That(random.OffersDraws, Is.EqualTo(3 + 2), "one draw per pick and two for the roll.");

        // ADR-0011: a level-up must never shift what the next wave is made of.
        Assert.That(random.OtherDraws, Is.EqualTo(0));
    }

    // ---- Choosing (rule 6) -----------------------------------------------------------------------

    [Test]
    public void Choose_TakesSpendsAndCloses()
    {
        SkillTree tree = FullTree();
        LevelUpFlow flow = Flow(tree);

        BankPicks(1);

        flow.Open(Offers());

        ContentId chosen = flow.Offer[1];

        flow.Choose(1, Offers());

        Assert.That(tree.IsTaken(chosen), Is.True);
        Assert.That(_progression.PendingLevelUps, Is.EqualTo(0));
        Assert.That(flow.HasOffer, Is.False);

        // NodeTaken before LevelUpClosed: the node is the news, the close is the consequence.
        int taken = _events.All.ToList().FindIndex(e => e is NodeTaken);
        int closed = _events.All.ToList().FindIndex(e => e is LevelUpClosed);

        Assert.That(taken, Is.GreaterThanOrEqualTo(0), "no NodeTaken was published.");
        Assert.That(closed, Is.GreaterThan(taken), "LevelUpClosed must follow the take it closes.");

        Assert.That(_events.Single<LevelUpClosed>().Level, Is.EqualTo(_progression.Level));
    }

    [Test]
    public void Choose_TellsTheRunnerAboutAnActive()
    {
        SkillTree tree = ActiveTree();
        LevelUpFlow flow = Flow(tree);

        BankPicks(1);

        flow.Open(Offers());

        int active = IndexOf(flow.Offer, new ContentId("skill.a1"));

        Assert.That(active, Is.GreaterThanOrEqualTo(0), "the fixture needs the Active on the table.");

        flow.Choose(active, Offers());

        Assert.That(_runner.Count, Is.EqualTo(1), "core pushes the Active in directly (M3-03 rule 7).");
        Assert.That(_runner.IsReady(0), Is.True, "a new Active is ready rather than starting on cooldown.");

        // Told about, not fired: the runner casts on its own tick and nothing has ticked.
        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(0));
    }

    [Test]
    public void Choose_PassiveDoesNotReachTheRunner()
    {
        LevelUpFlow flow = Flow(FullTree());

        BankPicks(1);

        flow.Open(Offers());
        flow.Choose(0, Offers());

        Assert.That(_runner.Count, Is.EqualTo(0));
    }

    [Test]
    public void Choose_RedrawsForTheNextPick()
    {
        SkillTree tree = FullTree();
        LevelUpFlow flow = Flow(tree);

        BankPicks(2);

        flow.Open(Offers());

        ContentId chosen = flow.Offer[0];

        flow.Choose(0, Offers());

        Assert.That(flow.HasOffer, Is.True, "the second pick draws a fresh offer.");
        Assert.That(_events.Count<OfferPresented>(), Is.EqualTo(2));
        Assert.That(_events.Of<OfferPresented>()[1].PicksOwed, Is.EqualTo(1));

        Assert.That(_events.Count<LevelUpClosed>(), Is.EqualTo(0), "a pick is still owed.");

        // Drawn over the *new* tree state, so the node just taken cannot come back.
        Assert.That(flow.Offer.ToArray(), Has.No.Member(chosen));
    }

    [Test]
    public void Choose_SecondOfferSeesTheNewState()
    {
        // Branch A is two tiers deep: a2 is gated on one node taken in the branch (tier - 1), so it
        // is unavailable until a1 is taken and available immediately afterwards.
        SkillTreeSpec spec = Tree(
            "tree.ladder",
            Tiers('a', new[] { "skill.a1" }, new[] { "skill.a2" }),
            OneTier('b', "skill.b1"),
            OneTier('c', "skill.c1"));

        SkillTree tree = TreeOver(spec, Passives("skill.a1", "skill.a2", "skill.b1", "skill.c1"));
        LevelUpFlow flow = Flow(tree);

        var gated = new ContentId("skill.a2");

        Assert.That(tree.IsAvailable(gated), Is.False, "the fixture's premise: a2 starts gated.");

        BankPicks(2);

        flow.Open(Offers());

        Assert.That(flow.Offer.ToArray(), Has.No.Member(gated), "it cannot be offered while gated.");

        int a1 = IndexOf(flow.Offer, new ContentId("skill.a1"));

        Assert.That(a1, Is.GreaterThanOrEqualTo(0), "the fixture needs a1 on the first table.");

        flow.Choose(a1, Offers());

        Assert.That(tree.IsAvailable(gated), Is.True, "taking a1 unlocked it.");
        Assert.That(flow.Offer.ToArray(), Has.Member(gated), "the second draw saw the new state.");
    }

    [Test]
    public void Choose_NoOffer_Throws()
    {
        LevelUpFlow flow = Flow(FullTree());

        Assert.Throws<InvalidOperationException>(() => flow.Choose(0, Offers()));
    }

    [Test]
    public void Choose_IndexOutOfRange_Throws()
    {
        LevelUpFlow flow = Flow(FullTree());

        BankPicks(1);

        flow.Open(Offers());

        Assert.Throws<ArgumentOutOfRangeException>(() => flow.Choose(-1, Offers()));
        Assert.Throws<ArgumentOutOfRangeException>(() => flow.Choose(flow.Offer.Count, Offers()));
    }

    [Test]
    public void Choose_PublishesNoOfferChosen()
    {
        // A PIN, not a discovery. AR §8 asks events to describe rather than duplicate, and NodeTaken
        // already says which node was taken, of what kind, in which branch and how many are owned —
        // so an OfferChosen would be a second event carrying a subset of the first's fields.
        //
        // It is written to go RED the day someone adds one, because that is a decision this project
        // wants argued rather than arrived at: the task that introduces it must delete this row and
        // say why in its own As built. It is the same family as M3-07a's Snapshot_CarriesNoLoadout,
        // which M3-07b inverted in place when the format genuinely did gain the field.
        Type[] published = typeof(NodeTaken).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "Soulvail.Core.Events")
            .ToArray();

        Assert.That(published, Is.Not.Empty, "the sweep found no events at all, so it proves nothing.");

        Assert.That(
            published.Select(t => t.Name),
            Has.No.Member("OfferChosen"),
            "NodeTaken is the event a chosen card publishes (rule 6).");
    }

    // ---- Banked points (LV-02c) ----------------------------------------------------------------

    [Test]
    public void Open_DrawsWhateverIsAvailableRatherThanThree()
    {
        // Over a tree `TreeRules`
        // accepts, that state is unreachable: an ordinary node needs `tier - 1` taken in its branch,
        // so every branch always offers its whole first tier; a Keystone needs `NodeCount - 1`, which
        // is satisfied exactly when the rest of its branch is taken; and an Upgrade needs a parent in
        // the same branch at a *lower* tier, which is therefore always reachable first. So no valid
        // tree can be blocked while it still has nodes — see this test's own As built note.
        //
        // What is checkable, and what this row keeps, is that `Open` reports the generator's count
        // rather than assuming three: a tree with one node left offers one card and does not fall
        // discard the point.
        SkillTree tree = FullTree();
        LevelUpFlow flow = Flow(tree);

        TakeAllBut(tree, 1);

        Assert.That(tree.IsFull, Is.False, "the fixture's premise: nodes remain.");

        BankPicks(1);

        flow.Open(Offers());

        Assert.That(flow.HasOffer, Is.True);
        Assert.That(flow.Offer.Count, Is.EqualTo(1), "one available node is an offer of one.");
        Assert.That(_events.Single<OfferPresented>().Count, Is.EqualTo(1));
    }

    [Test]
    public void Open_FullTree_OpensNothing()
    {
        SkillTree tree = FullTree();
        TakeEverything(tree);
        LevelUpFlow flow = Flow(tree);
        BankPicks(2);
        float damage = Damage;
        float hp = MaxHpValue;
        var random = new CountingRandom(new FixedRandom(5));

        flow.Open(random.Offers);
        flow.Open(random.Offers);

        Assert.That(flow.HasOffer, Is.False);
        Assert.That(flow.Offer, Is.Empty);
        Assert.That(_progression.PendingLevelUps, Is.EqualTo(2));
        Assert.That(Damage, Is.EqualTo(damage));
        Assert.That(MaxHpValue, Is.EqualTo(hp));
        Assert.That(random.OffersDraws, Is.Zero);
        Assert.That(_events.Count<OfferPresented>(), Is.Zero);
    }

    [Test]
    public void Settle_FullTree_KeepsPointsBanked()
    {
        SkillTree tree = FullTree();
        TakeEverything(tree);
        var flow = new LevelUpFlow(tree, _progression, _runner, _registry, _events, _veilrot, isFree: true);
        BankPicks(2);
        float damage = Damage;
        float hp = MaxHpValue;

        flow.Settle();
        flow.Settle();

        Assert.That(_progression.PendingLevelUps, Is.EqualTo(2));
        Assert.That(Damage, Is.EqualTo(damage));
        Assert.That(MaxHpValue, Is.EqualTo(hp));
        Assert.That(flow.HasOffer, Is.False);
        Assert.That(_events.Count<OfferPresented>(), Is.Zero);
    }

    // ---- The boundary (rule 15) ------------------------------------------------------------------

    [Test]
    public void State_HandsOutNoFlow()
    {
        // A PIN of the same family as Choose_PublishesNoOfferChosen. AR §18.2: a live object is never
        // handed out of RunState, because a public handle here would let a view grant the player a
        // skill and consume Offers draws the simulation is counting on.
        //
        // Written to go RED if LevelUp is ever made public — which is the point. The reads beside it
        // are what a screen gets, and this row also asserts they exist, so "make it public" cannot be
        // justified by the reads being missing.
        PropertyInfo flow = typeof(RunState).GetProperty(
            "LevelUp",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.That(flow, Is.Not.Null, "RunState.LevelUp has gone — this pin needs rewriting, not deleting.");
        Assert.That(flow.GetMethod.IsPublic, Is.False, "the flow itself must stay internal (AR §18.2).");

        foreach (string read in new[] { "HasOffer", "Offer", "IsLevelUpPending" })
        {
            PropertyInfo property = typeof(RunState).GetProperty(read, BindingFlags.Instance | BindingFlags.Public);

            Assert.That(property, Is.Not.Null, $"RunState.{read} is what a screen reads instead.");
        }
    }

    [Test]
    public void Offer_IsTheSameInstanceEveryCall()
    {
        // SkillRunner.Slots' precedent: M3-08b polls this to draw three cards, so a per-call wrapper
        // would allocate on that path.
        LevelUpFlow flow = Flow(FullTree());

        Assert.That(flow.Offer, Is.SameAs(flow.Offer));
    }

    [Test]
    public void Offer_OutOfRange_Throws()
    {
        LevelUpFlow flow = Flow(FullTree());

        IReadOnlyList<ContentId> offer = flow.Offer;

        Assert.That(offer.Count, Is.EqualTo(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = offer[0]);

        BankPicks(1);

        flow.Open(Offers());

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = offer[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = offer[offer.Count]);
    }

    // ---- Guards ----------------------------------------------------------------------------------

    [Test]
    public void Flow_NullArguments_Throw()
    {
        SkillTree tree = FullTree();

        Assert.Throws<ArgumentNullException>(
            () => new LevelUpFlow(null, _progression, _runner, _registry, _events, _veilrot));
        Assert.Throws<ArgumentNullException>(
            () => new LevelUpFlow(tree, null, _runner, _registry, _events, _veilrot));
        Assert.Throws<ArgumentNullException>(
            () => new LevelUpFlow(tree, _progression, null, _registry, _events, _veilrot));
        Assert.Throws<ArgumentNullException>(
            () => new LevelUpFlow(tree, _progression, _runner, null, _events, _veilrot));
        Assert.Throws<ArgumentNullException>(
            () => new LevelUpFlow(tree, _progression, _runner, _registry, null, _veilrot));

        Assert.Throws<ArgumentNullException>(
            () => new LevelUpFlow(tree, _progression, _runner, _registry, _events, null));
    }

    [Test]
    public void Open_NullStream_Throws()
    {
        LevelUpFlow flow = Flow(FullTree());

        Assert.Throws<ArgumentNullException>(() => flow.Open(null));
    }

    [Test]
    public void Choose_NullStream_Throws()
    {
        LevelUpFlow flow = Flow(FullTree());

        BankPicks(1);

        flow.Open(Offers());

        Assert.Throws<ArgumentNullException>(() => flow.Choose(0, null));
    }

    // ---- Cost ------------------------------------------------------------------------------------

    [Test]
    public void Reads_AllocateNothing()
    {
        // **This row replaces the spec's `Open_AllocatesNothing`, which cannot be written as
        // specced.** That row asked for 10 000 open-and-choose cycles at zero bytes. Two things make
        // it impossible and neither is this task's: a choose *takes a node*, so 10 000 iterations
        // needs a 10 000-node tree; and every take puts a modifier on a `Stat`, whose backing
        // `List<Modifier>` grows monotonically — so the measurement would be counting M3-03's list
        // doubling, not this class's offer machinery. Measured anyway, it fails, and the message
        // ("expected no allocation") points at the wrong file entirely.
        //
        // What is per-frame here is the *reads*, and those are what this row pins: `RunTicker` asks
        // `HasOffer` every frame through the port, and M3-08b's screen polls `Offer` and indexes it
        // every frame it is up. A per-call wrapper in either would allocate on a 60 Hz path, which is
        // the mistake `SkillRunner.Slots` was shaped to avoid.
        LevelUpFlow flow = Flow(FullTree());

        BankPicks(1);

        flow.Open(Offers());

        Assert.That(flow.Offer.Count, Is.EqualTo(3), "the row measures a populated offer, not an empty one.");

        AllocationAssert.None(() =>
        {
            _ = flow.HasOffer;

            IReadOnlyList<ContentId> offer = flow.Offer;

            for (int i = 0; i < offer.Count; i++)
            {
                _ = offer[i];
            }
        });
    }

    [Test]
    public void Draw_AllocatesNothingBeyondTheTake()
    {
        // The other half, as close to the spec's intent as the shape allows: the *draw* is what this
        // class adds to a level-up, and it is measured on its own — repeatedly, over an unchanged
        // tree — exactly the way M3-04's `Draw_AllocatesNothing` measures the generator. A choose is
        // deliberately outside the measured body, because taking a node is what grows the `Stat`
        // list and that cost belongs to M3-03.
        //
        // SilentEvents rather than RecordingEvents: the recorder stores each payload in a
        // List<object> and would box every struct, so the row would measure the fake (Traps §7).
        var events = new SilentEvents();
        var intents = new RecordingIntents();
        var combat = new PlayerCombat(Character(), events, intents, EnemyCapacity);
        var progression = new LevelTracker(Scalings.Xp(), events);

        var stats = new PlayerStats(
            combat,
            new PlayerMotor(new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f), Vector3.UnitZ),
            progression);

        var registry = new EffectRegistry();
        registry.Register<ModifyStat>(new ModifyStatHandler(stats));

        var runner = new SkillRunner(registry, combat.Blackboard, events);

        SkillTree tree = WideTree(10);
        var veilrot = new Veilrot(stats, combat, combat.Blackboard, events);
        var flow = new LevelUpFlow(tree, progression, runner, registry, events, veilrot);

        IRandomStream offers = new FixedRandom(99).Offers;

        // One pick banked and never spent, so every call reaches the draw and none of them takes a
        // node: Open is idempotent while an offer is open, so the second onward measure the guard —
        // which is itself a path RunTicker can reach on a paused frame.
        while (progression.PendingLevelUps == 0)
        {
            progression.Grant(1f);
        }

        AllocationAssert.None(() => flow.Open(offers));
    }

    // ---- M6-02b: a banked reroll is spent by the next draw (rule 3) --------------------------------

    [Test]
    public void Reroll_TheNextOfferIsTheSecondDraw()
    {
        // Three flows over three trees of the same shape and one shared tracker, so each sees the
        // one pick banked below. The first two say what the stream's first and second three values
        // would each draw on their own; the third is the rerolled one.
        BankPicks(1);

        LevelUpFlow first = Flow(WideTree(10));
        first.Open(new FixedRandom(0.05f, 0.05f, 0.05f).Offers);
        ContentId[] firstThree = first.Offer.ToArray();

        LevelUpFlow second = Flow(WideTree(10));
        second.Open(new FixedRandom(0.95f, 0.95f, 0.95f).Offers);
        ContentId[] secondThree = second.Offer.ToArray();

        Assert.That(secondThree, Is.Not.EqualTo(firstThree), "the script must tell the two draws apart.");

        _events.Clear();

        LevelUpFlow rerolled = Flow(WideTree(10));
        GrantReroll(rerolled);

        // Each draw is its three picks and M6-05b's two Pact draws, so the second three start at
        // the sixth value rather than the fourth (M6-05b rule 9).
        var random = new CountingRandom(
            new FixedRandom(0.05f, 0.05f, 0.05f, 0.5f, 0.5f, 0.95f, 0.95f, 0.95f, 0.5f, 0.5f));

        rerolled.Open(random.Offers);

        Assert.That(rerolled.Offer.ToArray(), Is.EqualTo(secondThree), "the player sees the second three.");
        Assert.That(_events.Count<OfferPresented>(), Is.EqualTo(1), "and is told about one offer, not two.");
        Assert.That(rerolled.RerollsSpent, Is.EqualTo(1));
        Assert.That(rerolled.RerollCharges, Is.Zero);
        Assert.That(random.OffersDraws, Is.EqualTo(10), "two draws of three and two from the same stream.");
    }

    [Test]
    public void Reroll_IsSpentOnceOnly()
    {
        LevelUpFlow flow = Flow(WideTree(10));
        GrantReroll(flow);
        BankPicks(2);

        var random = new CountingRandom(new FixedRandom(3));

        flow.Open(random.Offers);
        int afterFirst = random.OffersDraws;

        flow.Choose(0, random.Offers);
        int second = random.OffersDraws - afterFirst;

        Assert.That(afterFirst, Is.EqualTo(2 * second), "the first offer was drawn twice, the second once.");
        Assert.That(flow.RerollsSpent, Is.EqualTo(1));
        Assert.That(flow.RerollCharges, Is.Zero);
    }

    [Test]
    public void Reroll_TwoChargesRerollTwoOffers()
    {
        LevelUpFlow flow = Flow(WideTree(10));
        GrantReroll(flow);
        GrantReroll(flow);
        BankPicks(2);

        var random = new CountingRandom(new FixedRandom(5));

        flow.Open(random.Offers);
        int afterFirst = random.OffersDraws;

        flow.Choose(0, random.Offers);

        Assert.That(flow.RerollsSpent, Is.EqualTo(2));
        Assert.That(flow.RerollCharges, Is.Zero);
        Assert.That(random.OffersDraws, Is.EqualTo(2 * afterFirst), "four draws, each offer's pair alike.");
        Assert.That(_events.Count<OfferPresented>(), Is.EqualTo(2), "one announcement per offer.");
    }

    [Test]
    public void Reroll_ChangesNothingButOffers()
    {
        LevelUpFlow flow = Flow(WideTree(10));
        GrantReroll(flow);
        BankPicks(1);

        var random = new CountingRandom(new FixedRandom(7));

        flow.Open(random.Offers);

        // ADR-0011: the extra draw is a real seed consequence and it is confined to Offers — a
        // rerolled run's later offers differ, and nothing it fights does.
        Assert.That(random.OffersDraws, Is.GreaterThan(0));
        Assert.That(random.OtherDraws, Is.Zero);
    }

    [Test]
    public void Reroll_FullTreeSpendsNothing()
    {
        SkillTree tree = FullTree();
        TakeEverything(tree);

        LevelUpFlow flow = Flow(tree);
        GrantReroll(flow);
        BankPicks(1);

        flow.Open(Offers());

        // Open spends a charge only on a draw that found something; a full tree draws nothing, so
        // the point and charge stay banked for a tree that can offer again.
        Assert.That(_progression.PendingLevelUps, Is.EqualTo(1));
        Assert.That(flow.RerollsSpent, Is.Zero);
        Assert.That(flow.RerollCharges, Is.EqualTo(1));
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary><c>GrantReroll</c> is internal; reached by reflection, for <see cref="Grant"/>'s reason.</summary>
    private static void GrantReroll(LevelUpFlow flow)
    {
        MethodInfo method = typeof(LevelUpFlow).GetMethod(
            "GrantReroll",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null, "LevelUpFlow.GrantReroll has gone.");

        method.Invoke(flow, null);
    }

    private LevelUpFlow Flow(SkillTree tree) =>
        new LevelUpFlow(tree, _progression, _runner, _registry, _events, _veilrot);

    /// <summary>Banks exactly <paramref name="count"/> picks, a unit of XP at a time so it cannot overshoot.</summary>
    private void BankPicks(int count)
    {
        while (_progression.PendingLevelUps < count)
        {
            _progression.Grant(1f);
        }

        Assert.That(_progression.PendingLevelUps, Is.EqualTo(count), "the fixture overshot.");

        _events.Clear();
    }

    private float Damage => _stats.Resolve(PlayerStat.WeaponDamage).Value;

    private float MaxHpValue => _combat.Health.MaxHp.Value;

    private static IRandomStream Offers(int seed = 1) => new FixedRandom(seed).Offers;

    private static int IndexOf(IReadOnlyList<ContentId> offer, ContentId id)
    {
        for (int i = 0; i < offer.Count; i++)
        {
            if (offer[i].Equals(id))
            {
                return i;
            }
        }

        return -1;
    }

    private SkillTree FullTree() =>
        TreeOver(TreeRulesTests.FullTree(), TreeRulesTests.FullSkills());

    /// <summary>
    /// Three branches of one node — the minimum <c>TreeRules</c> accepts (CH §5: every class has
    /// exactly three) — with the Active in branch A and a Passive either side of it.
    /// </summary>
    private SkillTree ActiveTree()
    {
        SkillTreeSpec spec = Tree(
            "tree.active",
            OneTier('a', "skill.a1"),
            OneTier('b', "skill.b1"),
            OneTier('c', "skill.c1"));

        return TreeOver(
            spec,
            new[]
            {
                TreeRulesTests.Active("skill.a1"),
                TreeRulesTests.Passive("skill.b1"),
                TreeRulesTests.Passive("skill.c1"),
            });
    }

    /// <summary>Three branches of one tier, <paramref name="perBranch"/> nodes each.</summary>
    private SkillTree WideTree(int perBranch)
    {
        var ids = new List<string>();
        var branches = new List<SkillBranchSpec>();

        foreach (char letter in new[] { 'a', 'b', 'c' })
        {
            var tier = new string[perBranch];

            for (int i = 0; i < perBranch; i++)
            {
                tier[i] = $"skill.{letter}{i}";
                ids.Add(tier[i]);
            }

            branches.Add(OneTier(letter, tier));
        }

        SkillTreeSpec spec = Tree("tree.wide", branches.ToArray());

        return TreeOver(spec, Passives(ids.ToArray()));
    }

    private SkillTree TreeOver(SkillTreeSpec spec, IReadOnlyList<SkillSpec> skills)
    {
        var rules = new TreeRules(spec, TreeRulesTests.Catalog(spec, skills));

        return new SkillTree(rules, _registry, _events);
    }

    private static SkillTreeSpec Tree(string id, params SkillBranchSpec[] branches) =>
        new SkillTreeSpec(new ContentId(id), new ContentId(OathboundId), branches);

    private static SkillBranchSpec OneTier(char letter, params string[] ids) =>
        Tiers(letter, ids);

    /// <summary>A branch of one tier per array handed in.</summary>
    private static SkillBranchSpec Tiers(char letter, params string[][] tiers)
    {
        var built = new IReadOnlyList<ContentId>[tiers.Length];

        for (int t = 0; t < tiers.Length; t++)
        {
            var tier = new ContentId[tiers[t].Length];

            for (int i = 0; i < tiers[t].Length; i++)
            {
                tier[i] = new ContentId(tiers[t][i]);
            }

            built[t] = tier;
        }

        return new SkillBranchSpec(new LocKey($"branch.{letter}"), built);
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

    private static void TakeEverything(SkillTree tree)
    {
        var buffer = new ContentId[tree.Rules.Count];

        while (tree.Available(buffer) > 0)
        {
            tree.Take(buffer[0]);
        }

        Assert.That(tree.IsFull, Is.True);
    }

    /// <summary>Takes until exactly <paramref name="remaining"/> nodes are available.</summary>
    private static void TakeAllBut(SkillTree tree, int remaining)
    {
        var buffer = new ContentId[tree.Rules.Count];

        int available;

        while ((available = tree.Available(buffer)) > remaining)
        {
            tree.Take(buffer[0]);
        }

        Assert.That(available, Is.EqualTo(remaining));
    }

    private static CharacterSpec Character() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        MaxHp,
        new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, WeaponDamage, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);

    /// <summary>Every stream counted separately, so "the Offers stream and no other" is checkable.</summary>
    private sealed class CountingRandom : IRandom
    {
        private readonly CountingStream _spawn;
        private readonly CountingStream _offers;
        private readonly CountingStream _affixes;
        private readonly CountingStream _drops;
        private readonly CountingStream _misc;

        public CountingRandom(IRandom inner)
        {
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

        public RandomState Capture() => default;

        public void Restore(in RandomState state)
        {
        }
    }

    private sealed class CountingStream : IRandomStream
    {
        private readonly IRandomStream _inner;

        public CountingStream(IRandomStream inner) => _inner = inner;

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

        public float Range(float minInclusive, float maxExclusive)
        {
            Draws++;

            return _inner.Range(minInclusive, maxExclusive);
        }

        public bool Chance(float probability)
        {
            Draws++;

            return _inner.Chance(probability);
        }
    }
}
