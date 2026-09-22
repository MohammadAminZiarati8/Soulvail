using System;
using System.Collections.Generic;
using System.Numerics;
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
/// <c>SanctumShop</c>: GD §13.3's four services, their prices, their refusals and their order.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shop over live objects, and no run.</b> A wallet, a player, a meter, a tree of twelve and
/// a level-up flow, wired the way <c>RunSession.Start</c> wires them — so every row reads the thing
/// a purchase changed rather than a fake of it.
/// </para>
/// <para>
/// <b>Where the other rows live.</b> What a reroll does to a draw is <c>LevelUpFlowTests</c>'; the
/// banished node over ten thousand draws is <c>OfferGeneratorTests</c>'; everything that needs a
/// kill and a resume is <c>RunSessionResumeTests</c>'; the prices on the asset are
/// <c>ModeDefinitionTests</c>' and <c>ContentValidationTests</c>'.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SanctumShopTests
{
    private const string OathboundId = "character.oathbound";
    private const float MaxHp = 200f;
    private const int Capacity = 8;

    /// <summary>GD §13.3's shop — Descent's numbers, written out (M5-06b rule 10's bargain).</summary>
    private static readonly SanctumSpec Descent = new SanctumSpec(25, 40, 40, 30f, 60, 15f);

    private RecordingEvents _events;
    private PlayerCombat _combat;
    private LevelTracker _progression;
    private EffectRegistry _registry;
    private SkillRunner _runner;
    private Veilrot _veilrot;
    private EssenceWallet _wallet;
    private SkillTree _tree;
    private LevelUpFlow _levelUp;
    private SanctumShop _shop;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();

        Build(TwelveTree(), TwelveSkills());
    }

    // ---- Rule 2: the doubling, and the saturation ------------------------------------------------

    [TestCase(0, 25)]
    [TestCase(1, 50)]
    [TestCase(2, 100)]
    [TestCase(5, 800)]
    public void Reroll_PriceDoubles(int bought, int price)
    {
        _shop.Restore(bought, 0);

        Assert.That(_shop.PriceOf(SanctumService.Reroll), Is.EqualTo(price));
    }

    [Test]
    public void Reroll_PriceSaturates()
    {
        // A hand-edited `"rerollsBought": 99` — RunEconomy guards it as non-negative and nothing
        // bounds it above. A shift would wrap and price the reroll negative, free or zero.
        _shop.Restore(bought: 99, spent: 0);
        _wallet.Earn(int.MaxValue);

        int price = _shop.PriceOf(SanctumService.Reroll);

        Assert.That(price, Is.EqualTo(int.MaxValue));
        Assert.That(price, Is.Positive);
        Assert.That(_shop.CanBuy(SanctumService.Reroll), Is.False, "the arithmetic running out is not a price.");
    }

    [Test]
    public void Reroll_BuyingBanksACharge()
    {
        _wallet.Earn(25);
        _events.Clear();

        _shop.Buy(SanctumService.Reroll);

        Assert.That(_wallet.Balance, Is.Zero);
        Assert.That(_shop.RerollsBought, Is.EqualTo(1));
        Assert.That(_levelUp.RerollCharges, Is.EqualTo(1), "banked, not spent — nothing has been offered.");
        Assert.That(_shop.RerollsSpent, Is.Zero);

        SanctumServiceBought bought = _events.Single<SanctumServiceBought>();

        Assert.That(bought.Service, Is.EqualTo(SanctumService.Reroll));
        Assert.That(bought.Price, Is.EqualTo(25));
        Assert.That(bought.Essence, Is.Zero);
        Assert.That(_shop.PriceOf(SanctumService.Reroll), Is.EqualTo(50), "and the next one costs double.");
    }

    // ---- Rules 4, 5: Banish ----------------------------------------------------------------------

    [Test]
    public void Banish_TakesTheNodeOutOfThePool()
    {
        var node = new ContentId(TreeRulesTests.Node('a', 1, 'a'));

        _wallet.Earn(40);
        Assert.That(_tree.IsAvailable(node), Is.True, "the fixture's own claim: it was on offer.");

        _shop.Banish(node);

        Assert.That(_tree.IsBanished(node), Is.True);
        Assert.That(_tree.IsAvailable(node), Is.False);
        Assert.That(AvailableIds(), Has.No.Member(node), "Available never writes it — rule 4.");
        Assert.That(_tree.BanishedIds, Is.EqualTo(new[] { node }));
        Assert.That(_wallet.Balance, Is.Zero);
        Assert.That(_events.Single<NodeBanished>().SkillId, Is.EqualTo(node));
        Assert.That(_events.Single<SanctumServiceBought>().Service, Is.EqualTo(SanctumService.Banish));
    }

    [Test]
    public void Banish_AcceptsAnUnavailableNode()
    {
        // Tier 2 with nothing taken: CH §5 closes it, and GD §13.3's "offer pool" is everything
        // untaken, so it is still banishable — the tier-4 case the service exists for (rule 5).
        var tierTwo = new ContentId(TreeRulesTests.Node('b', 2, 'a'));

        _wallet.Earn(40);
        Assert.That(_tree.IsAvailable(tierTwo), Is.False, "the fixture's own claim.");

        Assert.DoesNotThrow(() => _shop.Banish(tierTwo));
        Assert.That(_tree.IsBanished(tierTwo), Is.True);
    }

    [Test]
    public void Banish_RefusesATakenNode()
    {
        var node = new ContentId(TreeRulesTests.Node('a', 1, 'a'));

        _tree.Take(node);
        _wallet.Earn(40);

        Assert.Throws<InvalidOperationException>(() => _shop.Banish(node));
        Assert.That(_wallet.Balance, Is.EqualTo(40), "nothing is spent.");
        Assert.That(_tree.IsBanished(node), Is.False);
    }

    [Test]
    public void Banish_RefusesItTwice()
    {
        var node = new ContentId(TreeRulesTests.Node('a', 1, 'a'));

        _wallet.Earn(80);
        _shop.Banish(node);

        Assert.Throws<InvalidOperationException>(() => _shop.Banish(node));
        Assert.That(_wallet.Balance, Is.EqualTo(40), "the second is refused before the wallet moves.");
        Assert.That(_tree.BanishedIds, Has.Count.EqualTo(1));
    }

    [Test]
    public void Banish_RefusesAStranger()
    {
        // A node of another class's tree — TreeRulesTests' borrowed branch — is a node of no tree
        // this run has.
        var stranger = new ContentId(TreeRulesTests.Node('x', 1, 'a'));

        _wallet.Earn(40);

        var refused = Assert.Throws<InvalidOperationException>(() => _shop.Banish(stranger));

        Assert.That(refused.Message, Does.Contain(stranger.Value));
        Assert.That(_wallet.Balance, Is.EqualTo(40));
        Assert.Throws<ArgumentException>(() => _tree.Banish(stranger), "and the tree's own door says so too.");
    }

    [Test]
    public void Banish_OrphansAnUpgrade()
    {
        // Branch a: tier 1 is a1a and a1b; tier 2 holds a1a's Upgrade and a2b.
        string parent = TreeRulesTests.Node('a', 1, 'a');
        string upgrade = TreeRulesTests.Node('a', 2, 'a');

        var spec = TreeRulesTests.Tree(
            TreeRulesTests.Shallow('a'),
            TreeRulesTests.Shallow('b'),
            TreeRulesTests.Shallow('c'));

        IReadOnlyList<SkillSpec> skills = TreeRulesTests.Skills(
            TreeRulesTests.Passive(parent),
            TreeRulesTests.Passive(TreeRulesTests.Node('a', 1, 'b')),
            TreeRulesTests.Upgrade(upgrade, parent),
            TreeRulesTests.Passive(TreeRulesTests.Node('a', 2, 'b')),
            TreeRulesTests.ShallowSkills('b'),
            TreeRulesTests.ShallowSkills('c'));

        Build(spec, skills);
        _wallet.Earn(40);

        _shop.Banish(new ContentId(parent));

        // Rule 5's stated cost, asserted rather than discovered: the tier is open, the parent can
        // never be owned, so the Upgrade is never available again.
        _tree.Take(new ContentId(TreeRulesTests.Node('a', 1, 'b')));

        Assert.That(_tree.IsAvailable(new ContentId(TreeRulesTests.Node('a', 2, 'b'))), Is.True, "the tier is open.");
        Assert.That(_tree.IsAvailable(new ContentId(upgrade)), Is.False);
        Assert.That(AvailableIds(), Has.No.Member(new ContentId(upgrade)));
    }

    [Test]
    public void Banish_ThroughBuyThrows()
    {
        _wallet.Earn(40);

        var refused = Assert.Throws<InvalidOperationException>(() => _shop.Buy(SanctumService.Banish));

        Assert.That(refused.Message, Does.Contain("Banish(skillId)"));
        Assert.That(_wallet.Balance, Is.EqualTo(40));
    }

    [Test]
    public void Banish_BanishableIntoListsTheUntaken()
    {
        var a1a = new ContentId(TreeRulesTests.Node('a', 1, 'a'));
        var a1b = new ContentId(TreeRulesTests.Node('a', 1, 'b'));
        var c2b = new ContentId(TreeRulesTests.Node('c', 2, 'b'));

        _tree.Take(a1a);
        _tree.Take(a1b);
        _wallet.Earn(40);
        _shop.Banish(c2b);

        var buffer = new ContentId[12];
        int written = _shop.BanishableInto(buffer);

        var expected = new List<ContentId>();

        foreach (char branch in new[] { 'a', 'b', 'c' })
        {
            foreach (int tier in new[] { 1, 2 })
            {
                foreach (char slot in new[] { 'a', 'b' })
                {
                    var id = new ContentId(TreeRulesTests.Node(branch, tier, slot));

                    if (id != a1a && id != a1b && id != c2b)
                    {
                        expected.Add(id);
                    }
                }
            }
        }

        Assert.That(written, Is.EqualTo(9));
        Assert.That(new ArraySegment<ContentId>(buffer, 0, written), Is.EqualTo(expected), "in tree order.");
    }

    [Test]
    public void Banish_BanishableIntoRefusesAShortBuffer()
    {
        Assert.Throws<ArgumentException>(() => _shop.BanishableInto(new ContentId[3]));
    }

    // ---- Rules 6, 7: Heal and Cleanse ------------------------------------------------------------

    [Test]
    public void Heal_Restores()
    {
        _combat.ApplyDamage(100f, 0f);
        _wallet.Earn(40);

        _shop.Buy(SanctumService.Heal);

        Assert.That(_combat.Health.Current, Is.EqualTo(130f));
        Assert.That(_wallet.Balance, Is.Zero);
    }

    [Test]
    public void Heal_IsRefusedAtFullHealth()
    {
        _wallet.Earn(40);

        Assert.That(_shop.CanBuy(SanctumService.Heal), Is.False, "40 Essence for nothing — rule 7.");
        Assert.Throws<InvalidOperationException>(() => _shop.Buy(SanctumService.Heal));
        Assert.That(_wallet.Balance, Is.EqualTo(40));
    }

    [Test]
    public void Heal_CannotOverfill()
    {
        _combat.ApplyDamage(10f, 0f);
        _wallet.Earn(40);

        _shop.Buy(SanctumService.Heal);

        Assert.That(_combat.Health.Current, Is.EqualTo(MaxHp));
        Assert.That(_wallet.Balance, Is.Zero, "a partial heal is the player's call, and costs the whole price.");
    }

    [Test]
    public void Cleanse_Reduces()
    {
        _veilrot.Gain(40f);
        _wallet.Earn(60);

        _shop.Buy(SanctumService.Cleanse);

        Assert.That(_veilrot.Value, Is.EqualTo(25f));
        Assert.That(_wallet.Balance, Is.Zero);
    }

    [Test]
    public void Cleanse_IsRefusedAtZero()
    {
        _wallet.Earn(60);

        Assert.That(_shop.CanBuy(SanctumService.Cleanse), Is.False);
        Assert.Throws<InvalidOperationException>(() => _shop.Buy(SanctumService.Cleanse));
        Assert.That(_wallet.Balance, Is.EqualTo(60));
    }

    [Test]
    public void Cleanse_AtEightWastesSeven()
    {
        _veilrot.Gain(8f);
        _wallet.Earn(60);

        Assert.DoesNotThrow(() => _shop.Buy(SanctumService.Cleanse));
        Assert.That(_veilrot.Value, Is.Zero, "clamped — M6-04 rule 3.");
        Assert.That(_wallet.Balance, Is.Zero);
    }

    [Test]
    public void Shop_RefusesWhatCannotBeAfforded()
    {
        // Every service worth something, so the only refusal left is the price. **24, where the
        // spec's row says 39**: 39 affords the 25 reroll, so no single balance of 39 refuses all
        // four. 24 is the largest that does — one short of the cheapest price.
        _combat.ApplyDamage(50f, 0f);
        _veilrot.Gain(40f);
        _wallet.Earn(24);

        foreach (SanctumService service in AllFour())
        {
            Assert.That(_shop.CanBuy(service), Is.False, $"{service} at 24.");
        }

        Assert.Throws<InvalidOperationException>(() => _shop.Buy(SanctumService.Reroll));
        Assert.Throws<InvalidOperationException>(() => _shop.Buy(SanctumService.Heal));
        Assert.Throws<InvalidOperationException>(() => _shop.Buy(SanctumService.Cleanse));
        Assert.Throws<InvalidOperationException>(
            () => _shop.Banish(new ContentId(TreeRulesTests.Node('a', 1, 'a'))));

        Assert.That(_wallet.Balance, Is.EqualTo(24));
    }

    // ---- Rules 8, 10 -----------------------------------------------------------------------------

    [Test]
    public void Shop_TheWalletMovesBeforeTheEvent()
    {
        var reader = new BalanceReader();

        _wallet = new EssenceWallet(_events);
        _shop = new SanctumShop(Descent, _wallet, _combat, _veilrot, _tree, _levelUp, reader);
        reader.Wallet = _wallet;

        _combat.ApplyDamage(50f, 0f);
        _wallet.Earn(100);

        _shop.Buy(SanctumService.Heal);

        Assert.That(reader.Read, Is.EqualTo(new[] { 60 }), "the subscriber read the paid balance.");
    }

    [Test]
    public void Shop_CanBuyAllocatesNothing()
    {
        _combat.ApplyDamage(50f, 0f);
        _veilrot.Gain(40f);
        _wallet.Earn(100);

        // 25 000 iterations of four services, each read twice: 100 000 reads of CanBuy alone.
        AllocationAssert.None(
            () =>
            {
                _shop.CanBuy(SanctumService.Reroll);
                _shop.CanBuy(SanctumService.Banish);
                _shop.CanBuy(SanctumService.Heal);
                _shop.CanBuy(SanctumService.Cleanse);
                _shop.PriceOf(SanctumService.Reroll);
                _shop.PriceOf(SanctumService.Banish);
                _shop.PriceOf(SanctumService.Heal);
                _shop.PriceOf(SanctumService.Cleanse);
            },
            25_000);
    }

    // ---- Guards ----------------------------------------------------------------------------------

    [Test]
    public void Shop_RefusesANullArgument()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SanctumShop(Descent, null, _combat, _veilrot, _tree, _levelUp, _events));
        Assert.Throws<ArgumentNullException>(
            () => new SanctumShop(Descent, _wallet, null, _veilrot, _tree, _levelUp, _events));
        Assert.Throws<ArgumentNullException>(
            () => new SanctumShop(Descent, _wallet, _combat, _veilrot, null, _levelUp, _events));
        Assert.Throws<ArgumentNullException>(
            () => new SanctumShop(Descent, _wallet, _combat, _veilrot, _tree, null, _events));
        Assert.Throws<ArgumentNullException>(
            () => new SanctumShop(Descent, _wallet, _combat, _veilrot, _tree, _levelUp, null));

        // The spec's one optional argument: a shop with no meter never sells a Cleanse.
        var noMeter = new SanctumShop(Descent, _wallet, _combat, null, _tree, _levelUp, _events);
        _wallet.Earn(60);

        Assert.That(noMeter.CanBuy(SanctumService.Cleanse), Is.False);
    }

    [Test]
    public void Shop_RefusesAServiceThatIsNotOne()
    {
        var stranger = (SanctumService)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => _shop.PriceOf(stranger));
        Assert.Throws<ArgumentOutOfRangeException>(() => _shop.CanBuy(stranger));
        Assert.Throws<ArgumentOutOfRangeException>(() => _shop.Buy(stranger));
    }

    [Test]
    public void Banish_RefusesADefaultId()
    {
        _wallet.Earn(40);

        Assert.Throws<ArgumentException>(() => _shop.Banish(default));
        Assert.Throws<ArgumentException>(() => _tree.Banish(default));
        Assert.That(_wallet.Balance, Is.EqualTo(40));
    }

    [Test]
    public void Restore_RefusesANegativeStock()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _shop.Restore(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _shop.Restore(-1, 0));
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>A run's shop and everything it reaches, over <paramref name="spec"/>.</summary>
    private void Build(SkillTreeSpec spec, IReadOnlyList<SkillSpec> skills)
    {
        CharacterSpec character = Oathbound();

        _combat = new PlayerCombat(character, _events, new RecordingIntents(), Capacity);
        _progression = new LevelTracker(Scalings.Xp(), _events);

        var stats = new PlayerStats(
            _combat,
            new PlayerMotor(character.Movement, Vector3.UnitZ),
            _progression);

        _registry = new EffectRegistry();
        _registry.Register<ModifyStat>(new ModifyStatHandler(stats));

        _runner = new SkillRunner(_registry, _combat.Blackboard, _events);
        _veilrot = new Veilrot(stats, _combat, _combat.Blackboard, _events);
        _wallet = new EssenceWallet(_events);

        _tree = new SkillTree(
            new TreeRules(spec, TreeRulesTests.Catalog(spec, skills)),
            _registry,
            _events);

        _levelUp = new LevelUpFlow(
            _tree, _progression, _runner, _registry, _events, new OverflowSpec(0.02f, 0.02f));

        _shop = new SanctumShop(Descent, _wallet, _combat, _veilrot, _tree, _levelUp, _events);
    }

    /// <summary>M3-12's shape: three branches of two tiers of two — a tree of twelve.</summary>
    private static SkillTreeSpec TwelveTree() => TreeRulesTests.Tree(
        TreeRulesTests.Shallow('a'),
        TreeRulesTests.Shallow('b'),
        TreeRulesTests.Shallow('c'));

    private static IReadOnlyList<SkillSpec> TwelveSkills() => TreeRulesTests.Skills(
        TreeRulesTests.ShallowSkills('a'),
        TreeRulesTests.ShallowSkills('b'),
        TreeRulesTests.ShallowSkills('c'));

    private List<ContentId> AvailableIds()
    {
        var buffer = new ContentId[_tree.Rules.Count];
        int count = _tree.Available(buffer);

        return new List<ContentId>(new ArraySegment<ContentId>(buffer, 0, count));
    }

    private static IEnumerable<SanctumService> AllFour() => new[]
    {
        SanctumService.Reroll, SanctumService.Banish, SanctumService.Heal, SanctumService.Cleanse,
    };

    /// <summary>A 200 HP Oathbound with no Aegis, so a hit lands on hit points and nothing else.</summary>
    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        MaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f));

    /// <summary>A subscriber that reads the wallet from inside <see cref="SanctumServiceBought"/>.</summary>
    private sealed class BalanceReader : IDomainEvents
    {
        public EssenceWallet Wallet { get; set; }

        public List<int> Read { get; } = new List<int>();

        public void Publish<T>(in T evt)
            where T : struct
        {
            if (evt is SanctumServiceBought)
            {
                Read.Add(Wallet.Balance);
            }
        }
    }
}
