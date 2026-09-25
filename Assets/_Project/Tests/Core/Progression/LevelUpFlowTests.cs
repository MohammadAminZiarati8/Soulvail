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
/// <c>LevelUpFlow</c>: the lazy offer, the ordered take, and CH §5.2's Overflow.
/// </summary>
/// <remarks>
/// The rows that need a whole run — the boundary ordering, the resume derivation and the two
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

    /// <summary>
    /// What <c>Descent.asset</c> authors per Overflow level, written out — the number every row
    /// below reasons with, and no longer a <c>const</c> anybody can reach from here.
    /// </summary>
    /// <remarks>
    /// <b>Two fixtures meeting at a number, which is this assembly's standing bargain and not a new
    /// one</b> (M5-06b rule 10). Until M5-06b these rows read <c>LevelUpFlow.OverflowDamage</c>, so
    /// a retune moved the test with the code and neither could disagree with the other — which also
    /// meant a retune was a rebuild, which is what ledger row 5(i) was about.
    /// <c>Soulvail.Tests.Core</c> cannot reach <c>AssetDatabase</c> (M0-10), so it cannot open the
    /// mode; <c>GravecallerTreeTests.Overflow_ComesFromTheMode</c> is the row in the assembly that
    /// can, and a retune that moved one and not the other reddens one of the two. That is the same
    /// arrangement <c>TimeToKillTests</c> has had with <c>KeenCenser.asset</c> since M3-12c.
    /// </remarks>
    private const float OverflowPerLevel = 0.02f;

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
        Assert.That(_events.Count<OverflowGranted>(), Is.EqualTo(0));

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

    // ---- Overflow (rules 2, 7, 8) ----------------------------------------------------------------

    [Test]
    public void Open_FullTree_GrantsOverflow()
    {
        SkillTree tree = FullTree();
        LevelUpFlow flow = Flow(tree);

        TakeEverything(tree);

        Assert.That(tree.IsFull, Is.True, "the fixture's premise.");

        float damageBefore = Damage;
        float maxHpBefore = MaxHpValue;

        BankPicks(1);

        var random = new CountingRandom(new FixedRandom(5));

        flow.Open(random.Offers);

        Assert.That(flow.HasOffer, Is.False, "Overflow puts nothing on a table (rule 7).");
        Assert.That(random.OffersDraws, Is.EqualTo(0), "nothing was drawn, so no draw was spent.");

        OverflowGranted granted = _events.Single<OverflowGranted>();

        Assert.That(granted.Total, Is.EqualTo(1));
        Assert.That(granted.Level, Is.EqualTo(_progression.Level));

        Assert.That(flow.OverflowLevels, Is.EqualTo(1));
        Assert.That(_progression.PendingLevelUps, Is.EqualTo(0), "the pick was spent.");

        // **+2 % of the BASE, not of the current value**, and the difference is the whole point of
        // PercentAdd. This tree's 27 nodes already pool to +180 %, so one Overflow level takes the
        // stat from ×2.80 to ×2.82 — a ×1.007 change, not a ×1.02 one. Asserting the *delta* against
        // the base is what says that out loud; a row written as `before × 1.02` would have been red
        // against correct code, and was.
        Assert.That(Damage - damageBefore, Is.EqualTo(WeaponDamage * OverflowPerLevel).Within(0.0001f));
        Assert.That(MaxHpValue - maxHpBefore, Is.EqualTo(MaxHp * OverflowPerLevel).Within(0.0001f));
    }

    [Test]
    public void Open_DrawsWhateverIsAvailableRatherThanThree()
    {
        // **This row replaces the spec's `Open_BlockedButNotFull_GrantsOverflow`, which cannot be
        // built.** That row wanted a tree with nodes left and none available. Over a tree `TreeRules`
        // accepts, that state is unreachable: an ordinary node needs `tier - 1` taken in its branch,
        // so every branch always offers its whole first tier; a Keystone needs `NodeCount - 1`, which
        // is satisfied exactly when the rest of its branch is taken; and an Upgrade needs a parent in
        // the same branch at a *lower* tier, which is therefore always reachable first. So no valid
        // tree can be blocked while it still has nodes — see this test's own As built note.
        //
        // What is checkable, and what this row keeps, is that `Open` reports the generator's count
        // rather than assuming three: a tree with one node left offers one card and does not fall
        // through to Overflow.
        SkillTree tree = FullTree();
        LevelUpFlow flow = Flow(tree);

        TakeAllBut(tree, 1);

        Assert.That(tree.IsFull, Is.False, "the fixture's premise: nodes remain.");

        BankPicks(1);

        flow.Open(Offers());

        Assert.That(flow.HasOffer, Is.True);
        Assert.That(flow.Offer.Count, Is.EqualTo(1), "one available node is an offer of one.");
        Assert.That(_events.Single<OfferPresented>().Count, Is.EqualTo(1));
        Assert.That(_events.Count<OverflowGranted>(), Is.EqualTo(0), "a drawable pick is never Overflow.");
    }

    [Test]
    public void Overflow_PoolsAdditively()
    {
        // **Nothing taken**, deliberately: over a tree whose 27 nodes already pool to +180 % the
        // ×1.20 would be buried in a ×2.80, and the row would read as arithmetic nobody can check by
        // eye. With a clean stack, ten Overflow levels is ×1.20 exactly and the contrast with 1.02¹⁰
        // is visible in the numbers themselves.
        LevelUpFlow flow = Flow(FullTree());

        int modifiersBefore = _stats.Resolve(PlayerStat.WeaponDamage).ModifierCount;

        Grant(flow, 10);

        Assert.That(flow.OverflowLevels, Is.EqualTo(10));

        // GD §13.1's "additively within a family" and ADR-0008's order — ten sources pooled under one
        // PercentAdd family, so the tenth is worth what the first was.
        Assert.That(Damage, Is.EqualTo(WeaponDamage * 1.20f).Within(0.0001f));
        Assert.That(MaxHpValue, Is.EqualTo(MaxHp * 1.20f).Within(0.0001f));

        Assert.That(
            Damage,
            Is.Not.EqualTo(WeaponDamage * MathF.Pow(1.02f, 10)).Within(0.0001f),
            "multiplicative stacking would have exponentiated behind the designer's back.");

        Assert.That(
            _stats.Resolve(PlayerStat.WeaponDamage).ModifierCount - modifiersBefore,
            Is.EqualTo(10),
            "one modifier per Overflow level, all under the flow as a single source.");
    }

    [Test]
    public void Overflow_MaxHpIsNotAHeal()
    {
        SkillTree tree = FullTree();
        LevelUpFlow flow = Flow(tree);

        TakeEverything(tree);

        // 100 of 140 before the modifier goes on. Any tree damage the take applied is to WeaponDamage
        // only, so the maximum here is still the class's.
        //
        // **The shield is paid off first, and it has to be read rather than assumed**: CC §7's class
        // carries a 30-point Aegis, so a flat 40 damage lands 10 on the bar and leaves 130 — which is
        // exactly what the first version of this row asserted its way past.
        _combat.ApplyDamage(_combat.Health.Shield + (MaxHp - 100f), 0f);

        Assert.That(_combat.Health.Current, Is.EqualTo(100f).Within(0.001f), "the fixture's premise.");

        BankPicks(1);

        flow.Open(Offers());

        Assert.That(MaxHpValue, Is.EqualTo(142.8f).Within(0.001f));
        Assert.That(_combat.Health.Current, Is.EqualTo(100f).Within(0.001f), "raising the ceiling is not a heal.");
    }

    [Test]
    public void Overflow_ClearsEveryPendingPick()
    {
        SkillTree tree = FullTree();
        LevelUpFlow flow = Flow(tree);

        TakeEverything(tree);

        BankPicks(3);

        flow.Open(Offers());

        Assert.That(_events.Count<OverflowGranted>(), Is.EqualTo(3));
        Assert.That(_progression.PendingLevelUps, Is.EqualTo(0));
        Assert.That(flow.HasOffer, Is.False);
    }

    [Test]
    public void Overflow_AnnouncesTheRunningTotal()
    {
        SkillTree tree = FullTree();
        LevelUpFlow flow = Flow(tree);

        TakeEverything(tree);

        BankPicks(3);

        flow.Open(Offers());

        Assert.That(
            _events.Of<OverflowGranted>().Select(g => g.Total).ToArray(),
            Is.EqualTo(new[] { 1, 2, 3 }),
            "the running total, in order, so a toast needs no tally of its own.");
    }

    [Test]
    public void Overflow_RestoreIsSilentAndSpendsNothing()
    {
        // GrantOverflow's own contract, which the resume path in RunSessionResumeTests depends on:
        // it puts the modifiers on and announces nothing, because a resumed run's Overflow was
        // earned in a previous session and is not news (AR §18.1's restore block).
        SkillTree tree = FullTree();
        LevelUpFlow flow = Flow(tree);

        float damageBefore = Damage;

        BankPicks(2);

        Grant(flow, 14);

        Assert.That(flow.OverflowLevels, Is.EqualTo(14));
        Assert.That(Damage, Is.EqualTo(damageBefore * 1.28f).Within(0.0001f));

        Assert.That(_events.Count<OverflowGranted>(), Is.EqualTo(0), "silent.");
        Assert.That(_progression.PendingLevelUps, Is.EqualTo(2), "it spends no pick.");
    }

    [Test]
    public void Overflow_ZeroIsOrdinary()
    {
        LevelUpFlow flow = Flow(FullTree());

        float damageBefore = Damage;

        Grant(flow, 0);

        Assert.That(flow.OverflowLevels, Is.EqualTo(0));
        Assert.That(Damage, Is.EqualTo(damageBefore).Within(0.0001f));
    }

    [Test]
    public void Overflow_Negative_Throws()
    {
        LevelUpFlow flow = Flow(FullTree());

        Assert.Throws<ArgumentOutOfRangeException>(() => Grant(flow, -1));
    }

    // ---- Ledger row 5(i): what a level is worth is the mode's (M5-06b rules 8–11) ----------------

    [Test]
    public void Overflow_TheConstantsAreGone()
    {
        // **The row that stops the literal coming back.** ADR-0006 says every number is in an
        // asset; these two were `public const float` from M3-08a to M5-06b, and rule 10's ruling is
        // that they are *deleted* rather than left as defaults — a default beside an authored value
        // is a second place the number lives, and the next reader would not know which one the game
        // used. Reflection over every member, not just the public ones: a private const would be
        // the same fault, quieter.
        foreach (string name in new[] { "OverflowDamage", "OverflowMaxHp" })
        {
            Assert.That(
                typeof(LevelUpFlow).GetField(
                    name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
                Is.Null,
                $"LevelUpFlow.{name} is back. What a level is worth is the mode's statement "
                    + "(GD §4.5) and lives on ModeDefinition — a constant here would be a second "
                    + "copy that the game may or may not be the one reading.");
        }
    }

    [Test]
    public void Overflow_ARetunedModeMovesTheGrant()
    {
        // **The whole point of row 5(i), asserted.** The same ten levels over the same clean stack,
        // under a mode that authors 5 % rather than 2 %: ×1.50, not ×1.20. Before this task the
        // only way to get this number was to edit `Core/Progression/` and rebuild.
        LevelUpFlow flow = Flow(FullTree(), new OverflowSpec(0.05f, 0.05f));

        Grant(flow, 10);

        Assert.That(Damage, Is.EqualTo(WeaponDamage * 1.50f).Within(0.0001f));
        Assert.That(MaxHpValue, Is.EqualTo(MaxHp * 1.50f).Within(0.0001f));

        // And the control, so the row cannot pass on a flow that ignores its argument and happens
        // to be right: the shipped pair over the same ten levels is the ×1.20 above.
        Assert.That(
            WeaponDamage * 1.50f,
            Is.Not.EqualTo(WeaponDamage * (1f + (10f * OverflowPerLevel))).Within(0.0001f));
    }

    [Test]
    public void Overflow_DamageAndMaxHpAreTwoNumbers()
    {
        // **The ledger called them "the 2 %", singular, for a milestone**, and they are two fields
        // that happen to ship equal. A mode is free to move one — a Boss Rush that pays for spare
        // levels in damage alone is the obvious shape — so this is the row that says the pair is
        // not one number wearing two names.
        LevelUpFlow flow = Flow(FullTree(), new OverflowSpec(0.05f, 0f));

        Grant(flow, 10);

        Assert.That(Damage, Is.EqualTo(WeaponDamage * 1.50f).Within(0.0001f));
        Assert.That(MaxHpValue, Is.EqualTo(MaxHp).Within(0.0001f), "maxHp was authored at zero.");
    }

    [Test]
    public void Overflow_AZeroedModeGrantsNothing()
    {
        // `default(OverflowSpec)` is legal content, which is why the constructor has no null row for
        // its sixth argument (see Flow_NullArguments_Throw). A mode that never mentioned Overflow is
        // a mode whose spare levels are worth nothing — still counted, still announced, worth zero.
        SkillTree tree = FullTree();
        LevelUpFlow flow = Flow(tree, default);

        TakeEverything(tree);

        float damageBefore = Damage;
        float maxHpBefore = MaxHpValue;

        BankPicks(1);

        flow.Open(Offers());

        Assert.That(flow.OverflowLevels, Is.EqualTo(1), "the pick was still spent on Overflow.");
        Assert.That(_events.Count<OverflowGranted>(), Is.EqualTo(1), "and still announced.");

        Assert.That(Damage, Is.EqualTo(damageBefore).Within(0.0001f));
        Assert.That(MaxHpValue, Is.EqualTo(maxHpBefore).Within(0.0001f));
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

        foreach (string read in new[] { "HasOffer", "Offer", "IsLevelUpPending", "OverflowLevels" })
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
            () => new LevelUpFlow(null, _progression, _runner, _registry, _events, Overflow(), _veilrot));
        Assert.Throws<ArgumentNullException>(
            () => new LevelUpFlow(tree, null, _runner, _registry, _events, Overflow(), _veilrot));
        Assert.Throws<ArgumentNullException>(
            () => new LevelUpFlow(tree, _progression, null, _registry, _events, Overflow(), _veilrot));
        Assert.Throws<ArgumentNullException>(
            () => new LevelUpFlow(tree, _progression, _runner, null, _events, Overflow(), _veilrot));
        Assert.Throws<ArgumentNullException>(
            () => new LevelUpFlow(tree, _progression, _runner, _registry, null, Overflow(), _veilrot));

        // And the sixth argument has no null row, because it cannot be one: an OverflowSpec is a
        // struct, its zeroed form is legal content (a mode whose spare levels are worth nothing),
        // and every value that is not passes its own constructor. Overflow_AZeroedModeGrantsNothing
        // is what the missing row would have been.
        Assert.DoesNotThrow(
            () => new LevelUpFlow(tree, _progression, _runner, _registry, _events, default, _veilrot));
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
            _ = flow.OverflowLevels;

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
        var flow = new LevelUpFlow(tree, progression, runner, registry, events, Overflow(), veilrot);

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
    public void Reroll_OnAnOverflowLevelSpendsNothing()
    {
        SkillTree tree = FullTree();
        TakeEverything(tree);

        LevelUpFlow flow = Flow(tree);
        GrantReroll(flow);
        BankPicks(1);

        flow.Open(Offers());

        // Open spends a charge only on a draw that found something; a full tree draws nothing, so
        // the pick becomes Overflow and the charge waits for a tree that can offer again.
        Assert.That(_events.Count<OverflowGranted>(), Is.EqualTo(1));
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

    private LevelUpFlow Flow(SkillTree tree) => Flow(tree, Overflow());

    private LevelUpFlow Flow(SkillTree tree, OverflowSpec overflow) =>
        new LevelUpFlow(tree, _progression, _runner, _registry, _events, overflow, _veilrot);

    /// <summary><c>Descent.asset</c>'s pair, as a spec — see <see cref="OverflowPerLevel"/>.</summary>
    private static OverflowSpec Overflow() =>
        new OverflowSpec(OverflowPerLevel, OverflowPerLevel);

    /// <summary><c>GrantOverflow</c> is internal; this assembly has no access, so it goes through the port's own route.</summary>
    /// <remarks>
    /// Reflection rather than <c>InternalsVisibleTo</c>, which AR §18.2 says <c>Soulvail.Tests.Core</c>
    /// does not have and never will. The resume path itself is covered end to end in
    /// <c>RunSessionResumeTests</c>; this is only so the three rows above can reach the silent grant
    /// without standing up a whole run.
    /// </remarks>
    private static void Grant(LevelUpFlow flow, int times)
    {
        MethodInfo method = typeof(LevelUpFlow).GetMethod(
            "GrantOverflow",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null, "LevelUpFlow.GrantOverflow has gone.");

        try
        {
            method.Invoke(flow, new object[] { times });
        }
        catch (TargetInvocationException e)
        {
            throw e.InnerException!;
        }
    }

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
