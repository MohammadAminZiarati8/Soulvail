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
/// M6-05b: GD §13.2's roll on a level-up offer, the two draws it always spends, and the Veilrot a
/// corrupted take pays.
/// </summary>
/// <remarks>
/// <para>
/// <b>The scripts below lean on one fact about the order of the draws</b>: an offer of
/// <c>picks</c> cards reads <c>picks</c> values for the walk, then one for the slot, then one for the
/// chance. So a script of <c>(a, b, c, slot, chance)</c> is a whole three-card offer, a slot value
/// of 0.5 lands on card 1 of 3, and a chance value of 0 always rolls one — <c>Chance</c> is
/// <c>NextFloat() &lt; p</c>.
/// </para>
/// <para>
/// <b>The flow rows build their own small composition rather than a run</b>, which is
/// <c>LevelUpFlowTests</c>' shape: the meter is a real <c>Veilrot</c> over real stats, so the
/// Claiming row reaches the real latch.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PactOfferTests
{
    private const string OathboundId = "character.oathbound";
    private const float MaxHp = 140f;
    private const float WeaponDamage = 13f;
    private const float MoveSpeed = 3f;
    private const int EnemyCapacity = 32;

    /// <summary>What a clean node adds to weapon damage.</summary>
    private const float CleanDamage = 0.15f;

    /// <summary>What its Pact adds instead — GD §13.2's own worked example.</summary>
    private const float PactDamage = 0.45f;

    /// <summary>What every Pact in these trees costs.</summary>
    private const float PactRot = 15f;

    /// <summary>A script that rolls a Pact onto card 1 of 3: three picks, the slot, the chance.</summary>
    private static readonly float[] RollsCardOne = { 0.1f, 0.4f, 0.8f, 0.5f, 0f };

    private RecordingEvents _recorded;
    private HookedEvents _events;
    private PlayerCombat _combat;
    private LevelTracker _progression;
    private PlayerStats _stats;
    private EffectRegistry _registry;
    private SkillRunner _runner;
    private Veilrot _veilrot;

    [SetUp]
    public void SetUp()
    {
        _recorded = new RecordingEvents();
        _events = new HookedEvents(_recorded);
        _combat = new PlayerCombat(Character(), _events, new RecordingIntents(), EnemyCapacity);
        _progression = new LevelTracker(Scalings.Xp(), _events);

        _stats = new PlayerStats(
            _combat,
            new PlayerMotor(new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f), Vector3.UnitZ),
            _progression);

        _registry = new EffectRegistry();
        _registry.Register<ModifyStat>(new ModifyStatHandler(_stats));

        _runner = new SkillRunner(_registry, _combat.Blackboard, _events);
        _veilrot = new Veilrot(_stats, _combat, _combat.Blackboard, _events);
    }

    // ---- The roll and what it costs (rules 1, 2, 3, 4) --------------------------------------------

    [Test]
    public void Offer_SpendsTwoMoreDrawsThanPicks()
    {
        SkillTree tree = Wide(4, pacted: true);
        var random = new CountingRandom(new FixedRandom(0.1f, 0.4f, 0.8f));

        int written = Generator(tree).Draw(tree, random.Offers, 3, new ContentId[3], out _);

        Assert.That(written, Is.EqualTo(3), "the fixture's premise: a tree of twelve offers three.");
        Assert.That(random.OffersDraws, Is.EqualTo(5), "three picks, then the slot and the chance.");
        Assert.That(random.OtherDraws, Is.Zero, "the roll drew on a stream that is not Offers (ADR-0011).");
    }

    [Test]
    public void Offer_SpendsTheSameTwoWhenThereIsNoPact()
    {
        SkillTree tree = Wide(4, pacted: false);
        var random = new CountingRandom(new FixedRandom(0.1f, 0.4f, 0.8f, 0.5f, 0f));

        Generator(tree).Draw(tree, random.Offers, 3, new ContentId[3], out int pactIndex);

        // The chance said yes and nothing can be a Pact — and the stream moved by two anyway, which
        // is the whole of rule 1.
        Assert.That(random.OffersDraws, Is.EqualTo(5));
        Assert.That(pactIndex, Is.EqualTo(-1));
    }

    [Test]
    public void Offer_SpendsNothingOnAnEmptyTree()
    {
        SkillTree tree = Wide(1, pacted: true);
        TakeEverything(tree);

        var random = new CountingRandom(new FixedRandom(0f, 0f, 0f, 0f, 0f));

        int written = Generator(tree).Draw(tree, random.Offers, 3, new ContentId[3], out int pactIndex);

        Assert.That(written, Is.Zero);
        Assert.That(random.OffersDraws, Is.Zero, "a roll for no card moved the stream (rule 2).");
        Assert.That(pactIndex, Is.EqualTo(-1));
    }

    [Test]
    public void Offer_ConsumptionDoesNotDependOnCoverage()
    {
        // The same ids and shape, and only which nodes carry a Pact block differs: the hazard rule 1
        // exists for is a content edit changing what a seed replays.
        SkillTree covered = Wide(4, pacted: true);
        SkillTree bare = Wide(4, pacted: false);

        float[] script = { 0.3f, 0.6f, 0.2f, 0.5f, 0f, 0.7f, 0.1f, 0.9f };

        var first = new CountingRandom(new FixedRandom(script));
        var second = new CountingRandom(new FixedRandom(script));

        var a = new ContentId[3];
        var b = new ContentId[3];

        Generator(covered).Draw(covered, first.Offers, 3, a, out int coveredPact);
        Generator(bare).Draw(bare, second.Offers, 3, b, out int barePact);

        Assert.That(a, Is.EqualTo(b), "the same script offered different nodes.");
        Assert.That(first.OffersDraws, Is.EqualTo(second.OffersDraws));

        // And the next value off each stream is the same one, which is the replay itself.
        Assert.That(first.Offers.NextFloat(), Is.EqualTo(second.Offers.NextFloat()));

        Assert.That(coveredPact, Is.EqualTo(1), "the script rolls card 1, which the covered tree can corrupt.");
        Assert.That(barePact, Is.EqualTo(-1));
    }

    [Test]
    public void Offer_AtMostOneCardIsAPact()
    {
        SkillTree tree = Wide(4, pacted: true);
        OfferGenerator generator = Generator(tree);
        var stream = new Lcg(20260923);
        var offer = new ContentId[3];

        for (int i = 0; i < 10_000; i++)
        {
            int written = generator.Draw(tree, stream, offer.Length, offer, out int pactIndex);

            // One index, so never two cards — and never one outside what was written.
            Assert.That(pactIndex, Is.InRange(-1, written - 1), $"draw {i} named card {pactIndex} of {written}.");
        }
    }

    [Test]
    public void Offer_RollsAboutAQuarter()
    {
        SkillTree tree = Wide(4, pacted: true);
        OfferGenerator generator = Generator(tree);
        var stream = new Lcg(71);
        var offer = new ContentId[3];
        int pacts = 0;

        for (int i = 0; i < 10_000; i++)
        {
            generator.Draw(tree, stream, offer.Length, offer, out int pactIndex);

            if (pactIndex >= 0)
            {
                pacts++;
            }
        }

        // Every node carries a block, so coverage is 1 and the rate is PactChance itself.
        Assert.That(pacts / 10_000f, Is.EqualTo(OfferGenerator.PactChance).Within(0.02f));
    }

    [Test]
    public void Offer_ANodeWithoutAPactIsNotOne()
    {
        // Three nodes, one a branch, so every offer is all three; only branch a's is clean.
        SkillTree tree = Trio(cleanBranch: 'a');
        var clean = new ContentId("skill.a0");

        float[] walk = { 0.1f, 0.4f, 0.8f };

        // Where the walk puts the clean node depends only on the three walk values.
        var probe = new ContentId[3];
        Generator(tree).Draw(tree, new FixedRandom(walk).Offers, 3, probe, out _);

        int cleanSlot = Array.IndexOf(probe, clean);
        int corruptSlot = (cleanSlot + 1) % 3;

        Assert.That(cleanSlot, Is.GreaterThanOrEqualTo(0), "the fixture's premise: the clean node is offered.");

        Assert.That(
            DrawWithRoll(tree, walk, cleanSlot),
            Is.EqualTo(-1),
            "the slot landed on a node with no Pact block while the chance said yes, and a Pact was offered.");

        // And the control, so the row above cannot pass against a roll that never says yes.
        Assert.That(DrawWithRoll(tree, walk, corruptSlot), Is.EqualTo(corruptSlot));
    }

    [Test]
    public void Offer_ABanishedNodeIsNeverAPact()
    {
        SkillTree tree = Wide(2, pacted: true);
        var banished = new ContentId("skill.a1");
        tree.Banish(banished);

        OfferGenerator generator = Generator(tree);
        var stream = new Lcg(20260924);
        var offer = new ContentId[3];
        int seen = 0;

        for (int i = 0; i < 10_000; i++)
        {
            int written = generator.Draw(tree, stream, offer.Length, offer, out _);

            for (int j = 0; j < written; j++)
            {
                if (offer[j] == banished)
                {
                    seen++;
                }
            }
        }

        // Neither corrupted nor clean: the roll is over what Available already allowed (rule 4).
        Assert.That(seen, Is.Zero);
    }

    [Test]
    public void Flow_AVigilOfferStillRollsTwo()
    {
        SkillTree tree = Wide(4, pacted: true);
        var random = new CountingRandom(new FixedRandom(0.1f, 0.4f, 0.9f, 0f));

        int written = Generator(tree).Draw(tree, random.Offers, 2, new ContentId[2], out int pactIndex);

        // GD §13.4's two cards are `count` 2 at the call site, and the roll is over *written* cards.
        Assert.That(written, Is.EqualTo(2));
        Assert.That(random.OffersDraws, Is.EqualTo(4));
        Assert.That(pactIndex, Is.InRange(-1, 1));
    }

    [Test]
    public void Offer_AllocatesNothing()
    {
        SkillTree tree = Wide(4, pacted: true);
        OfferGenerator generator = Generator(tree);
        var stream = new Lcg(5);
        var offer = new ContentId[3];

        // Warmed once, so the measurement is the steady state rather than a first call.
        generator.Draw(tree, stream, offer.Length, offer, out _);

        AllocationAssert.None(() => generator.Draw(tree, stream, offer.Length, offer, out _), 100_000);
    }

    // ---- The flow: which card, and what taking it costs (rules 5, 6, 9) ----------------------------

    [Test]
    public void Flow_PublishesThePactIndex()
    {
        LevelUpFlow flow = Flow(Wide(4, pacted: true));
        BankPicks(1);

        flow.Open(new FixedRandom(RollsCardOne).Offers);

        OfferPresented presented = _recorded.Single<OfferPresented>();

        Assert.That(presented.PactIndex, Is.EqualTo(1));
        Assert.That(flow.PactIndex, Is.EqualTo(presented.PactIndex), "the event and the read disagree.");
    }

    [Test]
    public void Flow_EmptyOfferAnswersMinusOne()
    {
        LevelUpFlow flow = Flow(Wide(4, pacted: true));

        Assert.That(flow.PactIndex, Is.EqualTo(-1), "no offer was ever opened.");

        BankPicks(1);
        flow.Open(new FixedRandom(RollsCardOne).Offers);
        flow.Choose(0, new FixedRandom().Offers);

        Assert.That(flow.HasOffer, Is.False, "the fixture's premise: the one pick is spent.");
        Assert.That(flow.PactIndex, Is.EqualTo(-1), "a closed offer still named a Pact.");
    }

    [Test]
    public void Flow_TakingThePactCardAppliesTheCorruptedEffects()
    {
        SkillTree tree = Wide(4, pacted: true);
        LevelUpFlow flow = Flow(tree);
        BankPicks(1);

        flow.Open(new FixedRandom(RollsCardOne).Offers);
        ContentId id = flow.Offer[1];

        flow.Choose(1, new FixedRandom().Offers);

        Assert.That(tree.IsPact(id), Is.True);
        Assert.That(Damage, Is.EqualTo(WeaponDamage * (1f + PactDamage)).Within(1e-4f), "the clean effects went on.");
    }

    [Test]
    public void Flow_TakingAnotherCardIsClean()
    {
        SkillTree tree = Wide(4, pacted: true);
        LevelUpFlow flow = Flow(tree);
        BankPicks(1);

        flow.Open(new FixedRandom(RollsCardOne).Offers);
        ContentId id = flow.Offer[0];

        flow.Choose(0, new FixedRandom().Offers);

        Assert.That(tree.IsPact(id), Is.False);
        Assert.That(Damage, Is.EqualTo(WeaponDamage * (1f + CleanDamage)).Within(1e-4f));
        Assert.That(_veilrot.Value, Is.Zero, "a clean take paid Rot.");
        Assert.That(_recorded.Count<VeilrotChanged>(), Is.Zero);
    }

    [Test]
    public void Flow_TakingThePactPaysItsRot()
    {
        LevelUpFlow flow = Flow(Wide(4, pacted: true));
        BankPicks(1);

        flow.Open(new FixedRandom(RollsCardOne).Offers);
        flow.Choose(1, new FixedRandom().Offers);

        Assert.That(_veilrot.Value, Is.EqualTo(PactRot));
        Assert.That(_recorded.Count<VeilrotChanged>(), Is.EqualTo(1));
    }

    [Test]
    public void Flow_TheMeterMovesAfterTheNodeIsOwned()
    {
        SkillTree tree = Wide(4, pacted: true);
        LevelUpFlow flow = Flow(tree);
        BankPicks(1);

        flow.Open(new FixedRandom(RollsCardOne).Offers);
        ContentId id = flow.Offer[1];

        bool? ownedWhenTheMeterMoved = null;

        _events.OnPublish = evt =>
        {
            if (evt is VeilrotChanged)
            {
                ownedWhenTheMeterMoved = tree.TakenIds.Contains(id);
            }
        };

        flow.Choose(1, new FixedRandom().Offers);

        Assert.That(ownedWhenTheMeterMoved, Is.True, "VeilrotChanged fired before the node was owned, or not at all.");
    }

    [Test]
    public void Flow_APactThatReachesAHundredClaims()
    {
        LevelUpFlow flow = Flow(Wide(4, pacted: true));

        _veilrot.Gain(88f);

        // Two picks, so the screen is still up once the first is spent.
        BankPicks(2);

        flow.Open(new FixedRandom(RollsCardOne).Offers);
        flow.Choose(1, new FixedRandom().Offers);

        Assert.That(_veilrot.Value, Is.EqualTo(Veilrot.Max), "88 + 15 clamps at 100.");
        Assert.That(_veilrot.IsClaimed, Is.True);
        Assert.That(_recorded.Count<ClaimingBegan>(), Is.EqualTo(1));

        // M6-04 rule 6's latch closed on a level-up screen the player is looking at.
        Assert.That(flow.HasOffer, Is.True, "the second pick's offer is not up.");
        Assert.That(_recorded.Count<LevelUpClosed>(), Is.Zero);
    }

    [Test]
    public void Flow_RefusesANullMeter()
    {
        SkillTree tree = Wide(4, pacted: true);

        Assert.Throws<ArgumentNullException>(
            () => new LevelUpFlow(tree, _progression, _runner, _registry, _events, Overflow(), null));
    }

    [Test]
    public void Flow_ARerolledOfferRollsAgain()
    {
        // What the second three would be on their own: the same walk values, a fresh tree.
        float[] secondWalk = { 0.95f, 0.6f, 0.3f };

        var alone = new ContentId[3];
        SkillTree reference = Wide(4, pacted: true);
        Generator(reference).Draw(reference, new FixedRandom(secondWalk).Offers, 3, alone, out _);

        LevelUpFlow flow = Flow(Wide(4, pacted: true));
        GrantReroll(flow);
        BankPicks(1);

        // The first draw's chance says no (0.9); the second's says yes, onto card 1 (0.5, then 0).
        var random = new CountingRandom(new FixedRandom(
            0.1f, 0.4f, 0.8f, 0.5f, 0.9f,
            secondWalk[0], secondWalk[1], secondWalk[2], 0.5f, 0f));

        flow.Open(random.Offers);

        Assert.That(random.OffersDraws, Is.EqualTo(10), "a rerolled level spends 2 × (picks + 2).");
        Assert.That(flow.Offer.ToArray(), Is.EqualTo(alone), "the player sees the second three.");
        Assert.That(flow.PactIndex, Is.EqualTo(1), "the second three were not rolled for on their own.");
        Assert.That(_recorded.Single<OfferPresented>().PactIndex, Is.EqualTo(1));
    }

    // ---- Fixture --------------------------------------------------------------------------------

    private float Damage => _stats.Resolve(PlayerStat.WeaponDamage).Value;

    private LevelUpFlow Flow(SkillTree tree) =>
        new LevelUpFlow(tree, _progression, _runner, _registry, _events, Overflow(), _veilrot);

    private static OverflowSpec Overflow() => new OverflowSpec(0.02f, 0.02f);

    private static OfferGenerator Generator(SkillTree tree) => new OfferGenerator(tree.Rules);

    /// <summary>A fresh generator over <paramref name="tree"/>, rolling onto <paramref name="slot"/>.</summary>
    private static int DrawWithRoll(SkillTree tree, float[] walk, int slot)
    {
        float slotValue = (slot + 0.5f) / 3f;
        var stream = new FixedRandom(walk[0], walk[1], walk[2], slotValue, 0f).Offers;

        Generator(tree).Draw(tree, stream, 3, new ContentId[3], out int pactIndex);

        return pactIndex;
    }

    /// <summary>Three branches of one tier, <paramref name="perBranch"/> Passives each.</summary>
    private SkillTree Wide(int perBranch, bool pacted)
    {
        var branches = new List<SkillBranchSpec>();
        var skills = new List<SkillSpec>();

        foreach (char letter in new[] { 'a', 'b', 'c' })
        {
            var tier = new string[perBranch];

            for (int i = 0; i < perBranch; i++)
            {
                tier[i] = $"skill.{letter}{i}";
                skills.Add(Passive(tier[i], pacted));
            }

            branches.Add(TreeRulesTests.Branch(letter, tier));
        }

        return Over(TreeRulesTests.Tree(branches.ToArray()), skills);
    }

    /// <summary>One node a branch, every one carrying a Pact but <paramref name="cleanBranch"/>'s.</summary>
    private SkillTree Trio(char cleanBranch)
    {
        var branches = new List<SkillBranchSpec>();
        var skills = new List<SkillSpec>();

        foreach (char letter in new[] { 'a', 'b', 'c' })
        {
            string id = $"skill.{letter}0";

            skills.Add(Passive(id, pacted: letter != cleanBranch));
            branches.Add(TreeRulesTests.Branch(letter, new[] { id }));
        }

        return Over(TreeRulesTests.Tree(branches.ToArray()), skills);
    }

    private SkillTree Over(SkillTreeSpec spec, IReadOnlyList<SkillSpec> skills) =>
        new SkillTree(new TreeRules(spec, TreeRulesTests.Catalog(spec, skills)), _registry, _events);

    /// <summary>
    /// A Passive worth +15 % damage, whose Pact — when it has one — is +45 % for 15 Rot.
    /// </summary>
    private static SkillSpec Passive(string id, bool pacted) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Passive,
        new IEffect[] { TreeRulesTests.Damage(CleanDamage) },
        pact: pacted
            ? new PactSpec(new IEffect[] { TreeRulesTests.Damage(PactDamage) }, PactRot, new LocKey($"{id}.pact"))
            : null);

    private static void TakeEverything(SkillTree tree)
    {
        var buffer = new ContentId[tree.Rules.Count];

        while (tree.Available(buffer) > 0)
        {
            tree.Take(buffer[0]);
        }
    }

    /// <summary>Banks exactly <paramref name="count"/> picks, a unit of XP at a time.</summary>
    private void BankPicks(int count)
    {
        while (_progression.PendingLevelUps < count)
        {
            _progression.Grant(1f);
        }

        Assert.That(_progression.PendingLevelUps, Is.EqualTo(count), "the fixture overshot.");

        _recorded.Clear();
    }

    /// <summary><c>GrantReroll</c> is internal; reached by reflection, <c>LevelUpFlowTests</c>' way.</summary>
    private static void GrantReroll(LevelUpFlow flow)
    {
        MethodInfo method = typeof(LevelUpFlow).GetMethod(
            "GrantReroll",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null, "LevelUpFlow.GrantReroll has gone.");

        method.Invoke(flow, null);
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

    /// <summary>
    /// Forwards to a recorder and lets a row look at each event as it is published — the one thing
    /// <c>RecordingEvents</c> cannot say is what the world looked like at that moment.
    /// </summary>
    private sealed class HookedEvents : IDomainEvents
    {
        private readonly IDomainEvents _inner;

        public HookedEvents(IDomainEvents inner) => _inner = inner;

        public Action<object> OnPublish { get; set; }

        public void Publish<T>(in T evt)
            where T : struct
        {
            _inner.Publish(evt);
            OnPublish?.Invoke(evt);
        }
    }

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

    /// <summary>
    /// A generator, for the rows that need ten thousand different answers rather than a script —
    /// <c>OfferGeneratorTests.Lcg</c>'s constants, private for the same reason.
    /// </summary>
    private sealed class Lcg : IRandomStream
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong Increment = 1442695040888963407UL;
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

            return (_state >> 40) * Scale;
        }

        public int NextInt(int minInclusive, int maxExclusive) =>
            minInclusive + (int)(NextFloat() * (maxExclusive - minInclusive));

        public float Range(float minInclusive, float maxInclusive) =>
            minInclusive + ((maxInclusive - minInclusive) * NextFloat());

        public bool Chance(float probability) => NextFloat() < probability;
    }
}
