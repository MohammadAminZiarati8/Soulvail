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
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Progression;

/// <summary>
/// <c>SkillTree</c>: CH §5's gating, what taking a node does to the player's numbers, and what a
/// resumed run's picks come back through.
/// </summary>
/// <remarks>
/// <para>
/// <b>Against a real <c>EffectRegistry</c> over a real <c>PlayerCombat</c> throughout</b>, for
/// <c>ModifyStatTests</c>' reason: the claim these rows make is that taking a node moves the number
/// a player would see, and a recording fake handler would prove only that something was handed to
/// it. 13 damage × 1.15 = 14.95 is the arithmetic, and it goes all the way through the modifier
/// stack.
/// </para>
/// <para>
/// <b>The content is <c>TreeRulesTests</c>' 27-node tree</b>, shared rather than rebuilt, because
/// the gating rows need a tree with layered tiers and a keystone at the end of a nine-node branch —
/// which is the one shape where CH §5's two rules give different numbers.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SkillTreeTests
{
    private const string OathboundId = "character.oathbound";

    /// <summary>What <see cref="StartRun"/> needs beyond a tree: a mode, a roster and an arena.</summary>
    private const string ModeId = "mode.test";

    private const string HuskId = "enemy.husk";

    private const string ArenaId = "arena.pillars";

    /// <summary>The Oathbound's authored damage. Every effect row's arithmetic starts here.</summary>
    private const float WeaponDamage = 13f;

    private const float MaxHp = 140f;
    private const float MoveSpeed = 3f;
    private const int EnemyCapacity = 8;

    /// <summary>The two capacities <c>RunSession</c>'s constructor wants beyond the enemy one.</summary>
    private const int DeviceCap = 8;

    private const int ProjectileCapacity = 8;

    private const float Tolerance = 1e-3f;

    /// <summary>A fixed instant, so a resumed run's clock is not the machine's.</summary>
    private static readonly DateTimeOffset Instant =
        DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    /// <summary>Every position of the 27-node tree, so a row can size a buffer by it.</summary>
    private const int FullTreeNodes = 27;

    private RecordingEvents _events;
    private RecordingIntents _intents;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
    }

    // ---- Availability, fresh and after a take (rules 2, 3) -------------------------------------

    [Test]
    public void Fresh_AvailableIsEveryTierOne()
    {
        SkillTree tree = FullTree();

        var destination = new ContentId[FullTreeNodes];

        int count = tree.Available(destination);

        // Two nodes a tier × three branches, and nothing else: tier 2 wants one node of its branch
        // taken, and a keystone wants eight. **Six at once is what makes M3-04's draw a draw** —
        // CH §5's one-node-a-tier reading would offer three heads every time (M3-02a rule 7).
        Assert.That(count, Is.EqualTo(6));

        // **In tree order**, and it is load-bearing rather than cosmetic: M3-04 walks this with one
        // draw per pick, so the same seed against the same state has to yield the same offer
        // (AR §18.3).
        Assert.That(
            new[] { destination[0], destination[1], destination[2], destination[3], destination[4], destination[5] },
            Is.EqualTo(new[]
            {
                Id(TreeRulesTests.Node('a', 1, 'a')),
                Id(TreeRulesTests.Node('a', 1, 'b')),
                Id(TreeRulesTests.Node('b', 1, 'a')),
                Id(TreeRulesTests.Node('b', 1, 'b')),
                Id(TreeRulesTests.Node('c', 1, 'a')),
                Id(TreeRulesTests.Node('c', 1, 'b')),
            }),
            "Branch 0's tier 1 in authored order, then branch 1's, then branch 2's.");

        Assert.That(tree.TakenCount, Is.Zero);
        Assert.That(tree.IsFull, Is.False);
        Assert.That(tree.TakenIds, Is.Empty);
    }

    [Test]
    public void Take_OpensTheNextTier()
    {
        SkillTree tree = FullTree();

        tree.Take(Id(TreeRulesTests.Node('a', 1, 'a')));

        // Branch A only: the other tier-1 node, and both tier-2 nodes, because one node of the
        // branch is now taken and tier 2 asks for one.
        Assert.That(
            AvailableInBranch(tree, 0),
            Is.EqualTo(new[]
            {
                Id(TreeRulesTests.Node('a', 1, 'b')),
                Id(TreeRulesTests.Node('a', 2, 'a')),
                Id(TreeRulesTests.Node('a', 2, 'b')),
            }));

        // And nothing about the other two branches moved, which is what "in that branch" means.
        Assert.That(AvailableInBranch(tree, 1), Has.Length.EqualTo(2));
        Assert.That(tree.TakenInBranch(0), Is.EqualTo(1));
        Assert.That(tree.TakenInBranch(1), Is.Zero);
    }

    [Test]
    public void Take_Unavailable_ThrowsNamingTheGate()
    {
        SkillTree tree = FullTree();

        // A tier-3 node at the start of a run: it wants two nodes of its branch and has none.
        var thrown = Assert.Throws<InvalidOperationException>(
            () => tree.Take(Id(TreeRulesTests.Node('a', 3, 'a'))));

        // **Which gate, not "unavailable".** M3-08's ChooseOffer is the caller, and a wrong index
        // there should read as what was wrong.
        Assert.That(thrown.Message, Does.Contain("tier 3"));
        Assert.That(thrown.Message, Does.Contain("needs 2"));
        Assert.That(thrown.Message, Does.Contain("has 0"));

        // Refused means refused: nothing was recorded and no event escaped.
        Assert.That(tree.TakenCount, Is.Zero);
        Assert.That(tree.IsTaken(Id(TreeRulesTests.Node('a', 3, 'a'))), Is.False);
        Assert.That(_events.Count<NodeTaken>(), Is.Zero);
    }

    [Test]
    public void Take_Twice_ThrowsNamingTaken()
    {
        SkillTree tree = FullTree();

        var first = Id(TreeRulesTests.Node('a', 1, 'a'));

        tree.Take(first);

        var thrown = Assert.Throws<InvalidOperationException>(() => tree.Take(first));

        Assert.That(thrown.Message, Does.Contain("already taken"));

        // The first take is intact: a refused second must not double-count the branch or the list.
        Assert.That(tree.TakenCount, Is.EqualTo(1));
        Assert.That(tree.TakenInBranch(0), Is.EqualTo(1));
    }

    [Test]
    public void Take_UnknownNode_Throws()
    {
        SkillTree tree = FullTree();

        // Loud, and distinct from an unavailable node: a stranger is a caller asking the wrong tree
        // or a save naming content this build dropped, not a gate that has not opened.
        Assert.Throws<KeyNotFoundException>(() => tree.Take(Id("skill.ghost")));
        Assert.Throws<KeyNotFoundException>(() => tree.Take(default));
    }

    [Test]
    public void Keystone_NeedsEveryOtherNode()
    {
        SkillTree tree = FullTree();

        var keystone = Id(TreeRulesTests.KeystoneId('a'));

        // Seven of branch A's eight ordinary nodes. Seven rather than four is the point: the tier
        // rule alone would already have opened a tier-5 node at four.
        TakeOrdinary(tree, 'a', 7);

        Assert.That(tree.TakenInBranch(0), Is.EqualTo(7));
        Assert.That(
            tree.IsAvailable(keystone),
            Is.False,
            "CH §5's 'all preceding' is eight for a nine-node branch, and seven is not eight.");

        // The eighth.
        TakeOrdinary(tree, 'a', 8);

        Assert.That(tree.TakenInBranch(0), Is.EqualTo(8));
        Assert.That(tree.IsAvailable(keystone), Is.True);

        // And it is genuinely takeable, not merely reported so.
        Assert.DoesNotThrow(() => tree.Take(keystone));
        Assert.That(tree.TakenInBranch(0), Is.EqualTo(9));
    }

    [Test]
    public void Keystone_Refusal_NamesTheKeystoneGate()
    {
        SkillTree tree = FullTree();

        // Four taken, so the *tier* rule would let a tier-5 node through and only the keystone rule
        // refuses it — which is what makes the message worth asserting.
        TakeOrdinary(tree, 'a', 4);

        var thrown = Assert.Throws<InvalidOperationException>(
            () => tree.Take(Id(TreeRulesTests.KeystoneId('a'))));

        Assert.That(thrown.Message, Does.Contain("keystone"));
        Assert.That(thrown.Message, Does.Contain("every other node"));
        Assert.That(thrown.Message, Does.Contain("8"));
    }

    [Test]
    public void Upgrade_NeedsItsParent()
    {
        // A tree whose branch A tier 2 holds an upgrade to its tier-1 first node.
        var parent = Id(TreeRulesTests.Node('a', 1, 'a'));
        var other = Id(TreeRulesTests.Node('a', 1, 'b'));
        var upgrade = Id(TreeRulesTests.Node('a', 2, 'a'));

        List<SkillSpec> skills = new List<SkillSpec>(TreeRulesTests.FullSkills());

        Replace(skills, upgrade, TreeRulesTests.Upgrade(upgrade.Value, parent.Value));

        SkillTree tree = TreeOver(TreeRulesTests.FullTree(), skills);

        // The *other* tier-1 node, so the tier gate is satisfied and the parent gate is the only
        // thing left standing.
        tree.Take(other);

        Assert.That(tree.TakenInBranch(0), Is.EqualTo(1), "Tier 2's own gate is open.");
        Assert.That(
            tree.IsAvailable(upgrade),
            Is.False,
            "An Upgrade is only offered once you own the skill it improves (CH §4).");

        var thrown = Assert.Throws<InvalidOperationException>(() => tree.Take(upgrade));

        Assert.That(thrown.Message, Does.Contain(parent.Value));
        Assert.That(thrown.Message, Does.Contain("not owned"));

        tree.Take(parent);

        Assert.That(tree.IsAvailable(upgrade), Is.True);
    }

    // ---- What taking one does (rule 4) ---------------------------------------------------------

    [Test]
    public void Take_AppliesEffectsWithTheSpecAsSource()
    {
        PlayerCombat combat = Combat();
        var registry = Registry(combat);

        var node = Id(TreeRulesTests.Node('a', 1, 'a'));

        List<SkillSpec> skills = new List<SkillSpec>(TreeRulesTests.FullSkills());
        SkillSpec spec = Node(node, SkillKind.Passive, TreeRulesTests.Damage(0.15f));

        Replace(skills, node, spec);

        SkillTree tree = TreeOver(TreeRulesTests.FullTree(), skills, registry);

        Assert.That(combat.Weapon.Damage.Value, Is.EqualTo(WeaponDamage).Within(Tolerance));

        tree.Take(node);

        // 13 × 1.15, all the way through the modifier stack onto the number a swing reads.
        Assert.That(combat.Weapon.Damage.Value, Is.EqualTo(14.95f).Within(Tolerance));

        // **The SkillSpec is the source** (M3-05 rule 7): shared, immutable, taken once per run, so
        // it is a reference stable for the run's life and never confused with another node's. It is
        // also what a future removal would take back by.
        var modifiers = new List<Modifier>();

        combat.Weapon.Damage.CopyModifiersTo(modifiers);

        Assert.That(modifiers, Has.Count.EqualTo(1));
        Assert.That(
            modifiers[0].Source,
            Is.SameAs(spec),
            "Not the tree, not the id, not a fresh token — the spec instance itself.");

        Assert.That(modifiers[0].Kind, Is.EqualTo(ModifierKind.PercentAdd));
        Assert.That(modifiers[0].Value, Is.EqualTo(0.15f).Within(Tolerance));
    }

    [Test]
    public void Take_AppliesEveryEffectOfANode()
    {
        PlayerCombat combat = Combat();
        var registry = Registry(combat);

        var node = Id(TreeRulesTests.Node('a', 1, 'a'));

        List<SkillSpec> skills = new List<SkillSpec>(TreeRulesTests.FullSkills());

        // "+2 damage and +15 %" is two ModifyStats from one source, which is ModifyStat's own
        // remark: the arithmetic already knows how to pool two contributions from one node.
        Replace(
            skills,
            node,
            Node(
                node,
                SkillKind.Passive,
                new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.Flat, 2f),
                TreeRulesTests.Damage(0.15f)));

        SkillTree tree = TreeOver(TreeRulesTests.FullTree(), skills, registry);

        tree.Take(node);

        // Flat first, then the percentage — ADR-0008's stack order, not the authored order.
        Assert.That(combat.Weapon.Damage.Value, Is.EqualTo(17.25f).Within(Tolerance));
        Assert.That(combat.Weapon.Damage.ModifierCount, Is.EqualTo(2));
    }

    [Test]
    public void Take_PublishesNodeTakenLast()
    {
        PlayerCombat combat = Combat();
        var registry = Registry(combat);

        var node = Id(TreeRulesTests.Node('a', 1, 'a'));

        List<SkillSpec> skills = new List<SkillSpec>(TreeRulesTests.FullSkills());
        SkillSpec spec = Node(node, SkillKind.Passive, TreeRulesTests.Damage(0.15f));

        Replace(skills, node, spec);

        int countFromTheHandler = -1;
        float damageFromTheHandler = float.NaN;

        var watching = new WatchingEvents();

        SkillTree tree = TreeOver(TreeRulesTests.FullTree(), skills, registry, watching);

        watching.On<NodeTaken>(_ =>
        {
            countFromTheHandler = tree.TakenCount;
            damageFromTheHandler = combat.Weapon.Damage.Value;
        });

        tree.Take(node);

        // **Last**, so a handler reading the state from inside the event sees the node it is being
        // told about — EnemyDied's ordering and its reason. A HUD flashing the new number, and
        // M3-08's screen closing itself, both read from here.
        Assert.That(countFromTheHandler, Is.EqualTo(1), "The node was recorded before the event.");
        Assert.That(
            damageFromTheHandler,
            Is.EqualTo(14.95f).Within(Tolerance),
            "And its effects were already on.");

        NodeTaken published = watching.Log.Single<NodeTaken>();

        Assert.That(published.SkillId, Is.EqualTo(node));
        Assert.That(published.Kind, Is.EqualTo(SkillKind.Passive));
        Assert.That(published.Branch, Is.Zero, "0-based, as TryLocate is.");
        Assert.That(published.Tier, Is.EqualTo(1), "1-based, as CH §5 is.");
        Assert.That(published.TakenCount, Is.EqualTo(1));
    }

    [Test]
    public void Take_CountsActives()
    {
        var passive = Id(TreeRulesTests.Node('a', 1, 'a'));
        var active = Id(TreeRulesTests.Node('a', 1, 'b'));

        List<SkillSpec> skills = new List<SkillSpec>(TreeRulesTests.FullSkills());

        Replace(skills, active, Active(active));

        SkillTree tree = TreeOver(TreeRulesTests.FullTree(), skills);

        Assert.That(tree.OwnedActives, Is.Zero);

        tree.Take(passive);

        Assert.That(tree.OwnedActives, Is.Zero, "A Passive is not a skill you can fire.");

        tree.Take(active);

        Assert.That(tree.OwnedActives, Is.EqualTo(1));
        Assert.That(tree.TakenCount, Is.EqualTo(2), "Both were taken; only one was an Active.");
    }

    [Test]
    public void Tree_UnregisteredEffect_ThrowsAtConstruction()
    {
        var node = Id(TreeRulesTests.Node('b', 2, 'a'));

        List<SkillSpec> skills = new List<SkillSpec>(TreeRulesTests.FullSkills());

        Replace(skills, node, Node(node, SkillKind.Passive, new Unregistered()));

        SkillTreeSpec spec = TreeRulesTests.FullTree();
        var rules = new TreeRules(spec, TreeRulesTests.Catalog(spec, skills));

        // A registry that can answer for ModifyStat and nothing else, so the *only* node the sweep
        // can trip on is the one this row planted. An empty registry would trip on the fixture's
        // very first node instead, and the row would pass while proving nothing about the planted
        // one.
        var thrown = Assert.Throws<KeyNotFoundException>(
            () => new SkillTree(rules, Registry(Combat()), _events));

        // **At construction, which is at Start, which is before RunStarted** — so the node is
        // reported as content rather than as a crash part way through a Take.
        Assert.That(thrown.Message, Does.Contain(node.Value), "Which node.");
        Assert.That(thrown.Message, Does.Contain(nameof(Unregistered)), "And which primitive.");
        Assert.That(thrown.Message, Does.Contain("takes"), "And which of its two lists.");
    }

    [Test]
    public void Tree_UnregisteredCastEffect_ThrowsAtConstruction()
    {
        var node = Id(TreeRulesTests.Node('c', 1, 'a'));

        List<SkillSpec> skills = new List<SkillSpec>(TreeRulesTests.FullSkills());

        Replace(skills, node, ActiveCasting(node, new Unregistered()));

        SkillTreeSpec spec = TreeRulesTests.FullTree();
        var rules = new TreeRules(spec, TreeRulesTests.Catalog(spec, skills));

        // Both lists are swept, and the cast half matters more than the take half: an Active's cast
        // effects are applied by M3-06's runner, so the first cast of a skill taken twenty minutes
        // earlier is the worst possible moment to discover a missing Register line.
        //
        // A registry that answers for ModifyStat, for the reason the row above gives: this node's
        // *take* list is empty and only its cast list is planted, so an empty registry would trip
        // on a different node entirely.
        var thrown = Assert.Throws<KeyNotFoundException>(
            () => new SkillTree(rules, Registry(Combat()), _events));

        Assert.That(thrown.Message, Does.Contain(node.Value));
        Assert.That(thrown.Message, Does.Contain("casts"));
    }

    // ---- Filling it up (rules 2, 3) ------------------------------------------------------------

    [Test]
    public void IsFull_AfterEveryNode()
    {
        SkillTree tree = FullTree();

        var destination = new ContentId[FullTreeNodes];

        // Drawn from Available each time rather than from a hand-written order, so the row cannot
        // pass on an order the gating would have refused.
        for (int taken = 0; taken < FullTreeNodes; taken++)
        {
            int count = tree.Available(destination);

            Assert.That(count, Is.GreaterThan(0), $"Nothing was available with {taken} taken.");

            tree.Take(destination[0]);
        }

        Assert.That(tree.TakenCount, Is.EqualTo(FullTreeNodes));
        Assert.That(tree.IsFull, Is.True);
        Assert.That(tree.Available(destination), Is.Zero, "A full tree offers nothing.");

        // Every branch finished, keystone included — which is the half a count alone would not say.
        for (int branch = 0; branch < 3; branch++)
        {
            Assert.That(tree.TakenInBranch(branch), Is.EqualTo(9));
            Assert.That(tree.IsTaken(Id(TreeRulesTests.KeystoneId((char)('a' + branch)))), Is.True);
        }
    }

    [Test]
    public void Available_AllocatesNothing()
    {
        SkillTree tree = FullTree();

        tree.Take(Id(TreeRulesTests.Node('a', 1, 'a')));

        // A heap array outside the measured body, converting to a Span at the call site inside it:
        // AllocationAssert cannot measure a stackalloc, because a lambda cannot close over a ref
        // struct (Traps §7).
        var destination = new ContentId[FullTreeNodes];

        AllocationAssert.None(() => tree.Available(destination));

        // The probe measured something real: a body that returned nothing would pass this row
        // whatever it allocated.
        Assert.That(tree.Available(destination), Is.EqualTo(3 + 2 + 2));

        // **And over a splashed tree**, because the walk is what M3-04 runs once per pick and
        // CH §5.4's borrowed branch put two more dictionary probes on it — one per candidate, in
        // `TreeRules.TryLocate`. A fourth branch that cost an allocation here would cost it on the
        // one path this class promises costs none (M5-07a-i rule 11).
        TreeRulesTests.Install(tree.Rules);
        tree.OnSplashInstalled();

        var widened = new ContentId[tree.Rules.Count];

        AllocationAssert.None(() => tree.Available(widened));

        Assert.That(tree.Available(widened), Is.EqualTo(3 + 2 + 2 + 2), "And two more, borrowed.");
    }

    [Test]
    public void Available_ShortDestination_Throws()
    {
        SkillTree tree = FullTree();

        // Refused rather than truncated: a caller asking what is available wants all of it, and a
        // short buffer would silently narrow M3-04's offer with no symptom anywhere.
        var thrown = Assert.Throws<ArgumentException>(
            () => tree.Available(new ContentId[FullTreeNodes - 1]));

        Assert.That(thrown.Message, Does.Contain("26"));
        Assert.That(thrown.Message, Does.Contain("27"));

        // Exactly the tree's size is enough, even though only six are available.
        Assert.That(tree.Available(new ContentId[FullTreeNodes]), Is.EqualTo(6));
    }

    // ---- Restore (rule 5) ----------------------------------------------------------------------

    [Test]
    public void Restore_ReplaysInOrder()
    {
        PlayerCombat combat = Combat();
        var registry = Registry(combat);

        var first = Id(TreeRulesTests.Node('a', 1, 'a'));
        var second = Id(TreeRulesTests.Node('a', 2, 'a'));
        var third = Id(TreeRulesTests.Node('b', 1, 'b'));

        List<SkillSpec> skills = new List<SkillSpec>(TreeRulesTests.FullSkills());

        Replace(skills, first, Node(first, SkillKind.Passive, TreeRulesTests.Damage(0.15f)));

        SkillTree tree = TreeOver(TreeRulesTests.FullTree(), skills, registry);

        tree.Restore(new[] { first, second, third });

        Assert.That(tree.TakenCount, Is.EqualTo(3));
        Assert.That(tree.IsTaken(first), Is.True);
        Assert.That(tree.IsTaken(second), Is.True);
        Assert.That(tree.IsTaken(third), Is.True);

        Assert.That(tree.TakenInBranch(0), Is.EqualTo(2));
        Assert.That(tree.TakenInBranch(1), Is.EqualTo(1));

        // **In order**, because that is what the list means and what the next save writes back.
        Assert.That(tree.TakenIds, Is.EqualTo(new[] { first, second, third }));

        // **Every restored node's effects are applied** — that is how a resumed run's modifiers
        // come back, and without it every passive in a save would be silently forgotten. The first
        // node was swapped for a +15 %; the other two are the fixture's default +5 % each, so the
        // arithmetic is 13 × (1 + 0.15 + 0.05 + 0.05). **Three modifiers, not one**, which is what
        // makes this row fail if the replay stops after the first entry.
        Assert.That(combat.Weapon.Damage.Value, Is.EqualTo(16.25f).Within(Tolerance));
        Assert.That(combat.Weapon.Damage.ModifierCount, Is.EqualTo(3));

        // **And no events.** Nothing may publish before RunStarted: a NodeTaken here would announce
        // as news a node the player picked in a previous session, to a HUD that has not drawn yet.
        Assert.That(_events.Count<NodeTaken>(), Is.Zero);
    }

    [Test]
    public void Restore_GateBreakingOrder_Throws()
    {
        SkillTree tree = FullTree();

        // A tier-2 node with nothing beneath it. A save whose own picks its own rules would have
        // refused is corrupt rather than merely old, and a tree that disagrees with itself is worse
        // than a refused resume — every later offer would be drawn against gating already false.
        var thrown = Assert.Throws<ArgumentException>(
            () => tree.Restore(new[] { Id(TreeRulesTests.Node('a', 2, 'a')) }));

        Assert.That(thrown.Message, Does.Contain("entry 0"), "Which entry of the saved order.");
        Assert.That(thrown.Message, Does.Contain("tier 2"), "And which gate it broke.");
    }

    [Test]
    public void Restore_GateBreakingOrder_NamesTheEntry()
    {
        SkillTree tree = FullTree();

        // Legal, legal, then an out-of-order third: the index in the message is what points a
        // reader at the byte of the save that is wrong.
        Assert.Throws<ArgumentException>(() => tree.Restore(new[]
        {
            Id(TreeRulesTests.Node('a', 1, 'a')),
            Id(TreeRulesTests.Node('a', 2, 'a')),
            Id(TreeRulesTests.Node('b', 3, 'a')),
        }));
    }

    [Test]
    public void Restore_UnknownNode_Throws()
    {
        SkillTree tree = FullTree();

        // A node this build no longer ships. Content validation's answer at Start, and M2-13a rule
        // 10's argument for not refusing it down in the DTO, where an unknown id cannot be told
        // apart from a typo.
        Assert.Throws<KeyNotFoundException>(() => tree.Restore(new[] { Id("skill.ghost") }));
    }

    [Test]
    public void Restore_EmptyIsOrdinary()
    {
        SkillTree tree = FullTree();

        // Every run that has not taken a node, which is every run in this build until M3-12.
        Assert.DoesNotThrow(() => tree.Restore(Array.Empty<ContentId>()));

        Assert.That(tree.TakenCount, Is.Zero);
        Assert.That(tree.TakenIds, Is.Empty);
    }

    [Test]
    public void Restore_Null_Throws()
    {
        SkillTree tree = FullTree();

        Assert.Throws<ArgumentNullException>(() => tree.Restore(null));
    }

    // ---- Reads and guards -----------------------------------------------------------------------

    [Test]
    public void Tree_TakenIdsIsAView()
    {
        SkillTree tree = FullTree();

        IReadOnlyList<ContentId> view = tree.TakenIds;

        Assert.That(view, Is.Empty);

        tree.Take(Id(TreeRulesTests.Node('a', 1, 'a')));

        // The same instance, now reporting one: a view rather than a copy, so the read costs
        // nothing per call. The copy that matters is RunSnapshot's, which is where a save needs one.
        Assert.That(tree.TakenIds, Is.SameAs(view));
        Assert.That(view, Has.Count.EqualTo(1));
    }

    [Test]
    public void Tree_IsTakenAndIsAvailableAnswerForAStranger()
    {
        SkillTree tree = FullTree();

        // False rather than a throw, for TreeRules.IsKeystone's reason: a caller filtering
        // candidates wants an answer, and a stranger is neither taken nor available.
        Assert.That(tree.IsTaken(Id("skill.ghost")), Is.False);
        Assert.That(tree.IsAvailable(Id("skill.ghost")), Is.False);
        Assert.That(tree.IsTaken(default), Is.False);
        Assert.That(tree.IsAvailable(default), Is.False);
    }

    [Test]
    public void Tree_TakenInBranchOfAStranger_Throws()
    {
        SkillTree tree = FullTree();

        Assert.Throws<ArgumentOutOfRangeException>(() => tree.TakenInBranch(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => tree.TakenInBranch(3));
    }

    [Test]
    public void Tree_NullArguments_Throw()
    {
        TreeRules rules = TreeRulesTests.FullRules();
        var registry = new EffectRegistry();

        registry.Register<ModifyStat>(new ModifyStatHandler(Stats(Combat())));

        Assert.Throws<ArgumentNullException>(() => new SkillTree(null, registry, _events));
        Assert.Throws<ArgumentNullException>(() => new SkillTree(rules, null, _events));
        Assert.Throws<ArgumentNullException>(() => new SkillTree(rules, registry, null));
    }

    [Test]
    public void State_HandsOutNoTree()
    {
        // AR §18.2's question for the seventh time, and the plainest case: Take is public on
        // SkillTree, so a public handle would let a view grant the player a node and bypass the
        // whole of CH §5's gating. Asserted by reflection rather than by "it does not compile", for
        // the reason every other seal in this project is — a refactor that widened it would
        // otherwise be caught by nothing.
        Assert.That(
            typeof(RunState).GetProperty("Tree"),
            Is.Null,
            "Tree must not be public — like Combat, Motor, Enemies, Projectiles, Progression and "
                + "Effects.");

        // And the four reads that stand in for it are — the fourth added by M3-09d, whose tree view
        // needed availability and got a read rather than the handle.
        Assert.That(typeof(RunState).GetProperty("TakenNodeCount"), Is.Not.Null);
        Assert.That(typeof(RunState).GetProperty("IsTreeFull"), Is.Not.Null);
        Assert.That(typeof(RunState).GetProperty("TakenNodeIds"), Is.Not.Null);
        Assert.That(typeof(RunState).GetMethod("IsNodeAvailable"), Is.Not.Null);
    }

    // ---- RunState.IsNodeAvailable (M3-09d rule 3) -----------------------------------------------
    //
    // Here rather than in a RunStateTests that does not exist, and beside the object it delegates
    // to — M3-09b's precedent, which put `State_ExposesCooldownSeconds` in `SkillRunnerTests` for
    // the same reason. A test cannot build a `RunState` (the constructor is `internal` and
    // `Soulvail.Tests.Core` has no `InternalsVisibleTo`, AR §18.2), so these three go through a real
    // `RunSession` over a restored `RunSnapshot`, which is the only door there is.

    [Test]
    public void State_AnswersAvailability()
    {
        ContentId tierOne = Id(TreeRulesTests.Node('a', 1, 'a'));
        ContentId tierTwo = Id(TreeRulesTests.Node('a', 2, 'a'));
        ContentId tierThree = Id(TreeRulesTests.Node('a', 3, 'a'));

        RunSession fresh = StartRun();

        // A fresh tree offers its first tiers and nothing else: tier 2 wants one node of its branch
        // taken and tier 3 wants two (CH §5, tier N requires N − 1).
        Assert.That(fresh.State.IsNodeAvailable(tierOne), Is.True);
        Assert.That(fresh.State.IsNodeAvailable(tierTwo), Is.False);
        Assert.That(fresh.State.IsNodeAvailable(tierThree), Is.False);

        // The same tree with branch a's first tier already owned, through the door a resumed run
        // uses — `SkillTree.Restore` replays the takes against the same gating, so this is the run
        // the player would have after two picks rather than a state a test assembled.
        RunSession taken = StartRun(
            level: 3,
            taken: new[] { TreeRulesTests.Node('a', 1, 'a'), TreeRulesTests.Node('a', 1, 'b') });

        Assert.That(
            taken.State.IsNodeAvailable(tierTwo),
            Is.True,
            "Two nodes of branch a are taken and tier 2 asks for one, so the read is a live "
                + "question about the run rather than a fact about the tree (M3-09d rule 3).");

        // And a node already owned is not *available*, which is what lets the tree view ask its two
        // questions in either order and get one answer.
        Assert.That(taken.State.IsNodeAvailable(tierOne), Is.False, "A taken node reads available.");

        // Nothing about the other branches moved, which is what "in that branch" means.
        Assert.That(taken.State.IsNodeAvailable(Id(TreeRulesTests.Node('b', 2, 'a'))), Is.False);
    }

    [Test]
    public void State_AvailabilityWithNoTree()
    {
        // Every run in the build until M3-12: TryGetTreeFor answers false, RunState.Tree is null,
        // and the read has to answer rather than throw — nothing downstream should have to ask
        // which kind of run it is in (M3-09d rules 2, 3).
        RunSession session = StartRun(withTree: false);

        Assert.That(session.State.TakenNodeIds, Is.Empty, "The fixture's premise: no tree.");

        Assert.That(
            () => session.State.IsNodeAvailable(Id(TreeRulesTests.Node('a', 1, 'a'))),
            Throws.Nothing);

        Assert.That(session.State.IsNodeAvailable(Id(TreeRulesTests.Node('a', 1, 'a'))), Is.False);
        Assert.That(session.State.IsNodeAvailable(default), Is.False);
    }

    [Test]
    public void State_AvailabilityForAStranger()
    {
        RunSession session = StartRun();

        // SkillTree.IsAvailable's own rule, carried through the read: a caller deciding how to draw
        // a candidate wants an answer, and an id this tree does not hold is not available. A throw
        // here would make an authoring mistake in one node a crash on a screen showing twenty-seven.
        Assert.That(() => session.State.IsNodeAvailable(Id("skill.ghost")), Throws.Nothing);

        Assert.That(session.State.IsNodeAvailable(Id("skill.ghost")), Is.False);
        Assert.That(session.State.IsNodeAvailable(default), Is.False);
    }

    // ---- M6-02b: the third flag survives the one rebuild the tree ever does ------------------------

    [Test]
    public void Banish_SurvivesASplash()
    {
        SkillTree tree = FullTree();
        var banished = Id(TreeRulesTests.Node('a', 1, 'a'));

        tree.Banish(banished);

        // OnSplashInstalled rebuilds the flag arrays by ordinal; a copy that forgot the third
        // would hand the banished node back the moment CH §5.4's branch arrived.
        TreeRulesTests.Install(tree.Rules);
        tree.OnSplashInstalled();

        Assert.That(tree.IsBanished(banished), Is.True);
        Assert.That(tree.IsAvailable(banished), Is.False);
        Assert.That(tree.BanishedIds, Is.EqualTo(new[] { banished }));

        // And the borrowed nodes arrive banishable, which is what a Sanctum after the splash sells.
        Assert.That(tree.CanBanish(Id(TreeRulesTests.Node('x', 1, 'a'))), Is.True);
    }

    [Test]
    public void Banish_RefusesToBeTaken()
    {
        SkillTree tree = FullTree();
        var banished = Id(TreeRulesTests.Node('a', 1, 'a'));

        tree.Banish(banished);

        var refused = Assert.Throws<InvalidOperationException>(() => tree.Take(banished));

        Assert.That(refused.Message, Does.Contain("banished"), "Take's refusal names the gate that closed.");
    }

    // ---- Fixture --------------------------------------------------------------------------------

    private static ContentId Id(string value) => new ContentId(value);

    /// <summary>
    /// A real run over the 27-node tree, because a test cannot build a <c>RunState</c> — the
    /// constructor is <c>internal</c> and this assembly has no <c>InternalsVisibleTo</c> (AR §18.2).
    /// </summary>
    /// <remarks>
    /// Resumed rather than fresh, for <c>SkillRunnerTests.StartSession</c>'s reason: a saved
    /// <c>TakenNodeIds</c> is the only door a test has for putting nodes into a run without playing
    /// one. <paramref name="level"/> has to account for what is taken, or M3-08a rule 9's identity
    /// refuses the snapshot.
    /// </remarks>
    private RunSession StartRun(int level = 1, string[] taken = null, bool withTree = true)
    {
        SkillTreeSpec tree = TreeRulesTests.FullTree();

        var catalog = new ContentCatalog(
            new[] { Character() },
            new[] { Husk() },
            new[] { Mode() },
            withTree ? TreeRulesTests.FullSkills() : Array.Empty<SkillSpec>(),
            withTree ? new[] { tree } : Array.Empty<SkillTreeSpec>());

        var random = new FixedRandom(7, new[] { 0.1f, 0.9f, 0.3f, 0.7f, 0.5f });
        var clock = new FixedClock(Instant);

        var session = new RunSession(
            catalog,
            random,
            _events,
            _intents,
            new RunRecorder(random, clock, _events),
            EnemyCapacity,
            DeviceCap,
            ProjectileCapacity);

        var takenIds = new ContentId[taken?.Length ?? 0];

        for (int i = 0; i < takenIds.Length; i++)
        {
            takenIds[i] = Id(taken[i]);
        }

        var snapshot = new RunSnapshot(
            RunSnapshot.CurrentVersion,
            Id(ModeId),
            Id(OathboundId),
            random.Seed,
            1,
            new RandomState(101, 102, 103, 104, 105),
            90f,
            0f,
            120f,
            Instant,
            level,
            0f,
            0,
            takenIds,
            new ContentId[SkillRunner.MaxManualSlots],
            default,
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>());

        session.Start(new RunConfig(
            Id(ModeId),
            Id(OathboundId),
            random.Seed,
            1,
            SpawnPlan.Empty,
            snapshot));

        return session;
    }

    private static EnemySpec Husk() => new EnemySpec(
        Id(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 10f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    private static ModeSpec Mode() => new ModeSpec(
        Id(ModeId),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        Scalings.Design(),
        Scalings.Xp(),
        new[] { new RosterEntry(Id(HuskId), 1) },
        new[] { Id(ArenaId) });

    /// <summary>The 27-node tree over a registry that can answer for <c>ModifyStat</c>.</summary>
    private SkillTree FullTree() => TreeOver(TreeRulesTests.FullTree(), TreeRulesTests.FullSkills());

    private SkillTree TreeOver(
        SkillTreeSpec spec,
        IReadOnlyList<SkillSpec> skills,
        EffectRegistry registry = null,
        IDomainEvents events = null)
    {
        var rules = new TreeRules(spec, TreeRulesTests.Catalog(spec, skills));

        return new SkillTree(rules, registry ?? Registry(Combat()), events ?? _events);
    }

    /// <summary>A registry that can apply a <c>ModifyStat</c> to <paramref name="combat"/>.</summary>
    private EffectRegistry Registry(PlayerCombat combat)
    {
        var registry = new EffectRegistry();

        registry.Register<ModifyStat>(new ModifyStatHandler(Stats(combat)));

        return registry;
    }

    private PlayerStats Stats(PlayerCombat combat) => new PlayerStats(
        combat,
        new PlayerMotor(new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f), Vector3.UnitZ),
        new LevelTracker(Scalings.Xp(), _events));

    private PlayerCombat Combat() =>
        new PlayerCombat(Character(), _events, _intents, EnemyCapacity);

    /// <summary>CC §7's class at the owner's retuned numbers — 13 damage is what every row counts from.</summary>
    private static CharacterSpec Character() => new CharacterSpec(
        Id(OathboundId),
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

    /// <summary>A node of whatever kind, carrying the effects a row cares about.</summary>
    private static SkillSpec Node(ContentId id, SkillKind kind, params IEffect[] effects) =>
        new SkillSpec(
            id,
            new LocKey($"{id.Value}.name"),
            new LocKey($"{id.Value}.desc"),
            kind,
            effects);

    /// <summary>An Active whose cast does something the registry knows about.</summary>
    private static SkillSpec Active(ContentId id) => new SkillSpec(
        id,
        new LocKey($"{id.Value}.name"),
        new LocKey($"{id.Value}.desc"),
        SkillKind.Active,
        Array.Empty<IEffect>(),
        new ActiveSpec(
            8f,
            new TriggerSpec(new[]
            {
                new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 0.6f),
            }),
            new IEffect[] { TreeRulesTests.Damage(0.05f) }));

    /// <summary>An Active whose cast carries <paramref name="onCast"/> and nothing else.</summary>
    private static SkillSpec ActiveCasting(ContentId id, IEffect onCast) => new SkillSpec(
        id,
        new LocKey($"{id.Value}.name"),
        new LocKey($"{id.Value}.desc"),
        SkillKind.Active,
        Array.Empty<IEffect>(),
        new ActiveSpec(
            8f,
            new TriggerSpec(new[]
            {
                new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 0.6f),
            }),
            new[] { onCast }));

    /// <summary>Swaps the spec registered under <paramref name="id"/> for <paramref name="spec"/>.</summary>
    /// <remarks>
    /// Asserted rather than assumed: a row that silently added a twenty-eighth node instead of
    /// replacing one would pass while testing a tree it did not mean to build.
    /// </remarks>
    private static void Replace(List<SkillSpec> skills, ContentId id, SkillSpec spec)
    {
        int at = skills.FindIndex(candidate => candidate.Id == id);

        Assert.That(at, Is.GreaterThanOrEqualTo(0), $"'{id}' is not one of the fixture's nodes.");

        skills[at] = spec;
    }

    /// <summary>
    /// Takes ordinary nodes of one branch, in tree order, until <paramref name="count"/> are owned.
    /// </summary>
    /// <remarks>
    /// Its own walk rather than <c>Available</c>, so a keystone row cannot accidentally take the
    /// keystone it is about — the tree's last tier is skipped by construction here.
    /// </remarks>
    private static void TakeOrdinary(SkillTree tree, char branch, int count)
    {
        for (int tier = 1; tier <= 4; tier++)
        {
            foreach (char slot in new[] { 'a', 'b' })
            {
                if (tree.TakenInBranch(branch - 'a') >= count)
                {
                    return;
                }

                var id = Id(TreeRulesTests.Node(branch, tier, slot));

                if (!tree.IsTaken(id))
                {
                    tree.Take(id);
                }
            }
        }
    }

    /// <summary>Everything available in one branch, in tree order.</summary>
    private static ContentId[] AvailableInBranch(SkillTree tree, int branch)
    {
        var destination = new ContentId[FullTreeNodes];

        int count = tree.Available(destination);

        var mine = new List<ContentId>();

        for (int i = 0; i < count; i++)
        {
            tree.Rules.TryLocate(destination[i], out int at, out _);

            if (at == branch)
            {
                mine.Add(destination[i]);
            }
        }

        return mine.ToArray();
    }

    /// <summary>A primitive nothing registers a handler for.</summary>
    /// <remarks>
    /// Its own type rather than a <c>ModifyStat</c> against an empty registry, because the row is
    /// about a primitive with no handler and a reader should not have to check which registry was
    /// passed to know that.
    /// </remarks>
    private sealed class Unregistered : IEffect
    {
    }

    /// <summary>
    /// A <see cref="IDomainEvents"/> that records and also hands each payload to whoever asked for
    /// that type. <c>RunSessionResumeTests</c>' fake, for its reason: the real
    /// <c>DomainEventHub</c> lives in <c>Soulvail.Game</c>, which this assembly does not reference.
    /// </summary>
    private sealed class WatchingEvents : IDomainEvents
    {
        private readonly Dictionary<Type, Action<object>> _handlers =
            new Dictionary<Type, Action<object>>();

        internal RecordingEvents Log { get; } = new RecordingEvents();

        internal void On<T>(Action<T> handler)
            where T : struct
        {
            _handlers[typeof(T)] = payload => handler((T)payload);
        }

        public void Publish<T>(in T evt)
            where T : struct
        {
            Log.Publish(in evt);

            if (_handlers.TryGetValue(typeof(T), out Action<object> handler))
            {
                handler(evt);
            }
        }
    }
}
