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
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Progression;

/// <summary>
/// CH §5.4's half-tree moment: when it is owed, who may be borrowed from, the one verb that locks
/// it, and what a resumed run derives rather than reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two halves, and the seam is what each one can reach.</b> The <c>Splash_</c> rows drive a
/// <see cref="SplashFlow"/> directly, because the numbers rule 2 is about —
/// <c>TreeRules.BranchCount</c>, <c>SplashBranch</c>, a branch's node count — live on objects
/// <c>RunState</c> keeps <c>internal</c> (AR §18.2, and <c>Soulvail.Tests.Core</c> has no
/// <c>InternalsVisibleTo</c>, deliberately). The <c>Run_</c> and <c>Resume_</c> rows drive a real
/// <c>RunSession</c>, because the moment's <em>placement</em> — built before <c>RunStarted</c>,
/// installed before the replay, answered through the command port — is that class's and cannot be
/// asserted anywhere else.
/// </para>
/// <para>
/// <b>The run rows reach the threshold through a save rather than by playing to it.</b> Six nodes
/// is six level-ups of kills; a snapshot carrying six taken ids puts the tree exactly where CH §5.4
/// asks about it, deterministically and in one call. <c>RunSessionResumeTests</c>' bargain, and for
/// its reason: a fixture that walked there would be testing the XP curve.
/// </para>
/// <para>
/// <b>The content is this file's and is deliberately twelve nodes, not twenty-seven.</b> M3-12's v1
/// shape is what the game ships and what the owner will playtest, so <c>OpensAt</c> is <b>six</b> in
/// almost every row here — and <see cref="Splash_TheThresholdIsAFractionNotALevel"/> is the one that
/// puts a 27-node tree beside it, because <em>the same beat at both content sizes</em> is the whole
/// of CH §5.4's argument.
/// </para>
/// <para>
/// <b>Every <c>Offers</c> draw in this file is scripted to the end of the cumulative weights.</b>
/// The borrowed branch is <em>appended</em> to the flattened walk (M5-07a-i rule 5), so the last
/// candidate is always a borrowed one once a branch is installed — which is what makes <em>"the
/// offer holds the node this row is about"</em> a fact rather than a retry loop.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SplashFlowTests
{
    private const string OathboundId = "character.oathbound";
    private const string GravecallerId = "character.gravecaller";
    private const string ModeId = "mode.test";

    private const string OathboundTreeId = "tree.oathbound";
    private const string GravecallerTreeId = "tree.gravecaller";

    /// <summary>The lent branch's four ids, in the order the lender's tree authors them.</summary>
    private const string LentActive = "skill.gravecaller.exhume";
    private const string LentMaxHp = "skill.gravecaller.grave-vigour";
    private const string LentA = "skill.gravecaller.rot";
    private const string LentB = "skill.gravecaller.legion";

    /// <summary>The lender's other two branches, which this run may not have. See the tree.</summary>
    private const string Unhandled = "skill.gravecaller.shove";
    private const string MinionAimed = "skill.gravecaller.army";

    /// <summary>The Keystone at the head of the lender's nine-node branch, which does not come over.</summary>
    private const string LenderKeystone = "skill.gravecaller.keystone";

    /// <summary>What the borrowed max-HP node adds. Flat, so the arithmetic is exact.</summary>
    private const float LentMaxHpBonus = 15f;

    /// <summary>The class's own hit points, and never a number a row restores to.</summary>
    private const float OathboundMaxHp = 140f;

    private const int Capacity = 32;
    private const int DeviceCap = 16;
    private const int ProjectileCapacity = 8;

    private const float Frame = 1f / 60f;

    /// <summary>An <c>Offers</c> value that walks the cumulative weights to the end — see the class.</summary>
    private const float LastCandidate = 0.999f;

    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    private RecordingEvents _events;
    private FixedRandom _random;
    private ContentCatalog _catalog;
    private RunSession _session;

    // ---- Rule 2: the threshold ------------------------------------------------------------------

    [Test]
    public void Splash_FiresAtHalfTheTree()
    {
        World world = Build();

        Assert.That(world.Flow.OpensAt, Is.EqualTo(6), "Twelve nodes, halved and rounded up.");

        TakeUntil(world.Tree, 5);

        Assert.That(world.Flow.IsPending, Is.False, "Five of twelve is not half.");

        TakeUntil(world.Tree, 6);

        Assert.That(world.Flow.IsPending, Is.True);
    }

    [Test]
    public void Splash_TheThresholdIsAFractionNotALevel()
    {
        // CH §5.4's own argument: "a twelve-node tree fills at level 13 and a twenty-seven-node one
        // at level 28, so any constant is wrong at one of the two scales."
        Assert.That(Build().Flow.OpensAt, Is.EqualTo(6));
        Assert.That(BuildOverFullTrees().Flow.OpensAt, Is.EqualTo(14), "ceil(27 × 0.5).");

        Assert.That(SplashFlow.Threshold, Is.EqualTo(0.5f));

        // **And no level number appears anywhere**, which is the half a pair of numbers cannot say:
        // the flow is handed the tree and nothing that counts levels, so there is no constant for a
        // second content size to be wrong at.
        Assert.That(
            typeof(SplashFlow).GetConstructors().Single().GetParameters()
                .Select(parameter => parameter.ParameterType),
            Has.No.Member(typeof(LevelTracker)),
            "SplashFlow was handed a LevelTracker, so the threshold could become a level.");

        Assert.That(
            typeof(SplashFlow)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)
                .Select(member => member.Name)
                .Where(name => name.Contains("Level")),
            Is.Empty,
            "A member of SplashFlow names a level. CH §5.4's threshold is a fraction of the tree.");
    }

    [Test]
    public void Splash_TheThresholdIsThePrimarysCount()
    {
        World world = Build();

        TakeUntil(world.Tree, 6);

        world.Flow.Open();
        world.Flow.Choose(new ContentId(GravecallerId), 0);

        // **The primary's count, never the run's.** `TreeRules.Count` is 16 now; had the threshold
        // been read off it, a moment already spent would come round again at eight.
        Assert.That(world.Flow.OpensAt, Is.EqualTo(6), "The threshold moved with the borrowed branch.");
        Assert.That(world.Tree.Rules.Count, Is.EqualTo(16));
        Assert.That(world.Flow.IsPending, Is.False);
    }

    [Test]
    public void Splash_HappensOnce()
    {
        World world = Build();

        TakeUntil(world.Tree, 6);

        world.Flow.Open();
        world.Flow.Choose(new ContentId(GravecallerId), 0);

        Assert.That(world.Flow.HasSplashed, Is.True);

        // Every node the run can still take — the whole of both trees it holds.
        TakeUntil(world.Tree, 16);

        Assert.That(world.Flow.IsPending, Is.False);
        Assert.That(world.Flow.IsOpen, Is.False);

        world.Flow.Open();

        Assert.That(world.Events.Count<SplashOffered>(), Is.EqualTo(1), "The moment came round again.");

        Assert.Throws<InvalidOperationException>(
            () => world.Flow.Choose(new ContentId(GravecallerId), 1));
    }

    // ---- Rule 3: who may be borrowed from -------------------------------------------------------

    [Test]
    public void Splash_ANoCandidateRunNeverOpens()
    {
        // CH §5.4's own branch: "If you have unlocked nothing, the moment does not happen. A player
        // holding only the Oathbound never sees this screen." One authored class is exactly that,
        // and it is the state this game shipped in from M3-12 to M5-02.
        World world = Build(secondClass: false);

        Assert.That(world.Flow.Candidates, Is.Empty);

        TakeUntil(world.Tree, 6);

        Assert.That(world.Flow.IsPending, Is.False, "A run with nothing to borrow is never owed one.");

        world.Flow.Open();

        Assert.That(world.Flow.IsOpen, Is.False);
        Assert.That(world.Events.Count<SplashOffered>(), Is.Zero);
    }

    [Test]
    public void Splash_OffersEveryAuthoredClassButItsOwn()
    {
        // A Gravecaller run, so the answer is the *other* class rather than whichever one the
        // fixture happens to build first.
        World world = BuildGravecallerRun();

        Assert.That(
            world.Flow.Candidates.Select(id => id.Value),
            Is.EqualTo(new[] { OathboundId }));
    }

    [Test]
    public void Splash_OneCandidateStillDrawsThePage()
    {
        World world = Build();

        Assert.That(world.Flow.Candidates, Has.Count.EqualTo(1), "The shipped two, minus this run's own.");

        TakeUntil(world.Tree, 6);

        world.Flow.Open();

        // **Nothing auto-chooses.** Skipping the page for a single card would be a special case for
        // a build state; drawn, it is one tap that reads identically at three classes.
        Assert.That(world.Flow.IsOpen, Is.True);
        Assert.That(world.Flow.HasSplashed, Is.False);
        Assert.That(world.Events.Count<SplashChosen>(), Is.Zero);
    }

    // ---- Rule 4: it opens without drawing -------------------------------------------------------

    [Test]
    public void Splash_OpenIsIdempotent()
    {
        World world = Build();

        TakeUntil(world.Tree, 6);

        world.Flow.Open();
        world.Flow.Open();

        Assert.That(world.Events.Count<SplashOffered>(), Is.EqualTo(1));

        SplashOffered offered = world.Events.Of<SplashOffered>()[0];

        Assert.That(offered.TakenCount, Is.EqualTo(6));
        Assert.That(offered.Threshold, Is.EqualTo(6));
    }

    // ---- The choice ------------------------------------------------------------------------------

    [Test]
    public void Splash_ChooseInstallsTheBranch()
    {
        World world = Build();

        TakeUntil(world.Tree, 6);

        world.Flow.Open();
        world.Flow.Choose(new ContentId(GravecallerId), 0);

        Assert.That(world.Tree.Rules.BranchCount, Is.EqualTo(4));
        Assert.That(world.Tree.Rules.SplashBranch, Is.EqualTo(3));
        Assert.That(world.Tree.Rules.SplashCharacterId.Value, Is.EqualTo(GravecallerId));
        Assert.That(world.Flow.IsOpen, Is.False, "The screen closes on the choice.");

        Assert.That(world.Events.Count<SplashChosen>(), Is.EqualTo(1));

        SplashChosen chosen = world.Events.Of<SplashChosen>()[0];

        Assert.That(chosen.CharacterId.Value, Is.EqualTo(GravecallerId));
        Assert.That(chosen.Branch, Is.Zero, "The lender's own index, not the run's 3.");
        Assert.That(chosen.NodesGained, Is.EqualTo(4));
    }

    [Test]
    public void Splash_BranchesOfDropsTheKeystone()
    {
        World world = Build();

        IReadOnlyList<SplashOption> branches = world.Flow.BranchesOf(new ContentId(GravecallerId));

        // branches.Count rather than Has.Count: NUnit reflects for a Count *property* on the
        // runtime type, which is SplashOption[] and carries Length (ClassSelectPresenterTests' wart).
        Assert.That(branches.Count, Is.EqualTo(SkillTreeSpec.BranchCount));

        Assert.That(branches[0].Branch, Is.Zero);
        Assert.That(branches[0].NameKey.Key, Is.EqualTo("tree.gravecaller.legion"));
        Assert.That(branches[0].NodeCount, Is.EqualTo(4));

        // **The lender's branch 1 is CH §5's nine — eight nodes and a Keystone — and CH §5.4 does
        // not lend the Keystone.** "One branch minus its Keystone is 7" at the design's own shape;
        // this fixture's branch is one node wider, so the number to draw is 8.
        Assert.That(
            branches[1].NodeCount,
            Is.EqualTo(8),
            "The Keystone came over, so a player is promised a node they cannot have.");

        Assert.That(branches[2].NodeCount, Is.EqualTo(3), "A branch with no Keystone loses nothing.");
    }

    // ---- M5-08a: the screen and the model ask one question --------------------------------------

    /// <summary>
    /// M5-08a rule 1. The offer now carries the answer <see cref="SplashFlow.Choose"/> would give, so
    /// a branch that would throw is drawn dead instead of tapped and thrown.
    /// </summary>
    /// <remarks>
    /// Found by playing M5-08's checklist rather than by reading: an Oathbound borrowing the
    /// Gravecaller was offered three branches of which <b>two</b> threw an <c>ArgumentException</c>
    /// out of a <c>Button.onClick</c>, on a screen M5-07a-ii built with no way off it but through.
    /// The sweep was always correct; only <c>Choose</c> ran it.
    /// </remarks>
    [Test]
    public void Splash_BranchesOfMarksTheBranchesThisRunCannotBorrow()
    {
        World world = Build();

        IReadOnlyList<SplashOption> branches = world.Flow.BranchesOf(new ContentId(GravecallerId));

        // Branch 0 is plain, branch 1 opens on a primitive nobody registered, branch 2 opens on a
        // ModifyStat aimed at minions — see GravecallerTree's remarks.
        Assert.That(branches[0].Borrowable, Is.True, "Branch 0 carries nothing this run refuses.");
        Assert.That(branches[0].RefusedKey, Is.EqualTo(default(LocKey)));

        Assert.That(branches[1].Borrowable, Is.False);
        Assert.That(
            branches[1].RefusedKey,
            Is.EqualTo(new LocKey(SplashFlow.RefusedPrimitiveKeyId)),
            "An effect with no handler is a different mistake from one aimed at minions, and the "
                + "screen has to be able to say which.");

        Assert.That(branches[2].Borrowable, Is.False);
        Assert.That(branches[2].RefusedKey, Is.EqualTo(new LocKey(SplashFlow.RefusedMinionsKeyId)));
    }

    /// <summary>
    /// M5-08a rule 2, and it is <see cref="SplashFlow.BranchesOf"/>'s own standing remark: a refused
    /// branch is listed and drawn dead, never omitted.
    /// </summary>
    /// <remarks>
    /// Hiding it would make a mis-authored tree look like a two-branch class, and CH §5.4's screen is
    /// as much about seeing what the other class <em>is</em> as about taking part of it.
    /// </remarks>
    [Test]
    public void Splash_BranchesOfListsARefusedBranchRatherThanHidingIt()
    {
        World world = Build();

        IReadOnlyList<SplashOption> branches = world.Flow.BranchesOf(new ContentId(GravecallerId));

        Assert.That(branches.Count, Is.EqualTo(SkillTreeSpec.BranchCount));

        Assert.That(
            branches.Count(option => option.Borrowable),
            Is.EqualTo(1),
            "One of three — the state M5-08's playtest walked into and nothing reported.");

        // Every branch keeps its name whether or not it can be taken: rule 5's half of the screen.
        Assert.That(branches.All(option => option.NameKey != default), Is.True);
    }

    /// <summary>
    /// M5-08a rule 1's other half: a refusal is about <em>this run</em> rather than about the branch,
    /// so a class that raises minions finds the same tree wholly borrowable.
    /// </summary>
    [Test]
    public void Splash_AClassThatRaisesFindsEveryBranchBorrowable()
    {
        World raiser = BuildGravecallerRun();

        IReadOnlyList<SplashOption> branches = raiser.Flow.BranchesOf(new ContentId(OathboundId));

        Assert.That(
            branches.All(option => option.Borrowable),
            Is.True,
            "The Oathbound authors no minion effects, so nothing in its tree can be refused — the "
                + "asymmetry M5-08 measured across two played runs, pinned.");
    }

    /// <summary>
    /// M5-08a rule 4. The predicate is the gate; the exception stays the invariant, so a caller that
    /// reaches <see cref="SplashFlow.Choose"/> without consulting the offer still fails loudly rather
    /// than installing half a branch.
    /// </summary>
    [Test]
    public void Splash_ChooseStillThrowsForABranchTheOfferMarkedRefused()
    {
        World world = Opened();

        IReadOnlyList<SplashOption> branches = world.Flow.BranchesOf(new ContentId(GravecallerId));

        Assert.That(branches[1].Borrowable, Is.False, "Guard: this row is about a refused branch.");

        Assert.Catch<ArgumentException>(() => world.Flow.Choose(new ContentId(GravecallerId), 1));

        Assert.That(world.Tree.Rules.BranchCount, Is.EqualTo(3), "Nothing was installed.");
    }

    [Test]
    public void Splash_ChooseIsRefusedBeforeItIsOpen()
    {
        World world = Build();

        TakeUntil(world.Tree, 6);

        Assert.That(world.Flow.IsPending, Is.True, "Owed, and still not open.");

        Assert.Throws<InvalidOperationException>(
            () => world.Flow.Choose(new ContentId(GravecallerId), 0));

        Assert.That(world.Flow.HasSplashed, Is.False);
        Assert.That(world.Tree.Rules.BranchCount, Is.EqualTo(3), "Nothing was installed.");
    }

    [Test]
    public void Splash_RefusesItsOwnClass()
    {
        World world = Opened();

        ArgumentException thrown = Assert.Catch<ArgumentException>(
            () => world.Flow.Choose(new ContentId(OathboundId), 0));

        Assert.That(thrown.Message, Does.Contain(OathboundId));
        Assert.That(world.Tree.Rules.BranchCount, Is.EqualTo(3));
    }

    [Test]
    public void Splash_RefusesAnUnauthoredClassOrBranch()
    {
        World world = Opened();

        ArgumentException stranger = Assert.Catch<ArgumentException>(
            () => world.Flow.Choose(new ContentId("character.nobody"), 0));

        Assert.That(stranger.Message, Does.Contain("character.nobody"));

        // Branch 3 of a three-branch tree. AOORE rather than a bare ArgumentException, which is
        // house style for an index (M5-07a-i deviation 4) and is an ArgumentException all the same.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Flow.Choose(new ContentId(GravecallerId), SkillTreeSpec.BranchCount));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Flow.Choose(new ContentId(GravecallerId), -1));

        Assert.That(world.Tree.Rules.BranchCount, Is.EqualTo(3), "Nothing was installed.");
        Assert.That(world.Flow.IsOpen, Is.True, "A refusal leaves the screen up to try again.");
    }

    [Test]
    public void Splash_RefusesABranchWhoseEffectsHaveNoHandler()
    {
        // The primary's nodes all carry ModifyStat, which this run registers; the lender's branch 1
        // carries a KnockbackOnSwing, which it does not. SkillTree's constructor swept the primary
        // at Start and cannot have seen this one.
        World world = Opened();

        ArgumentException thrown = Assert.Catch<ArgumentException>(
            () => world.Flow.Choose(new ContentId(GravecallerId), 1));

        Assert.That(thrown.Message, Does.Contain(nameof(KnockbackOnSwing)));
        Assert.That(thrown.Message, Does.Contain(Unhandled));

        // **And the branch is not installed** (rule 10). OnSplashInstalled's own sweep would have
        // caught this one moment later — with TreeRules already committed and the pair disagreeing
        // about how many nodes the run has.
        Assert.That(world.Tree.Rules.BranchCount, Is.EqualTo(3));
        Assert.That(world.Flow.HasSplashed, Is.False);
        Assert.That(world.Tree.Rules.Count, Is.EqualTo(12));
    }

    /// <summary>
    /// M5-07a-i's handed-over finding, closed: <c>RunSession.RequireNoMinionTarget</c> sweeps the
    /// <em>primary</em> tree at <c>Start</c>, and a branch borrowed mid-run never meets it.
    /// </summary>
    /// <remarks>
    /// <c>SkillTree.OnSplashInstalled</c>'s sweep cannot catch this one either:
    /// <c>EffectRegistry.CanApply</c> answers <em>"is there a handler for this type"</em> and a
    /// <c>ModifyStat</c> handler is registered. Left to the pick, <c>ModifyStatHandler</c> would
    /// throw at the moment the card is tapped, with the node owned and its effects half on — which
    /// is exactly the failure M5-06a rule 5 exists to move off the pick.
    /// </remarks>
    [Test]
    public void Splash_RefusesABranchAimedAtMinionsThisClassCannotRaise()
    {
        World world = Opened();

        ArgumentException thrown = Assert.Catch<ArgumentException>(
            () => world.Flow.Choose(new ContentId(GravecallerId), 2));

        Assert.That(thrown.Message, Does.Contain(nameof(StatTarget.Minions)));
        Assert.That(thrown.Message, Does.Contain(OathboundId));
        Assert.That(thrown.Message, Does.Contain(MinionAimed));

        Assert.That(world.Tree.Rules.BranchCount, Is.EqualTo(3), "Nothing was installed.");

        // And a class that *does* raise takes a branch without complaint, which is what makes the
        // row above about the run rather than about the branch.
        World raiser = BuildGravecallerRun();

        TakeUntil(raiser.Tree, raiser.Flow.OpensAt);

        raiser.Flow.Open();

        Assert.DoesNotThrow(() => raiser.Flow.Choose(new ContentId(OathboundId), 0));
    }

    // ---- Rule 11: the state hands out scalars ---------------------------------------------------

    [Test]
    public void State_HandsOutScalars()
    {
        Type[] surface = typeof(RunState)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.PropertyType)
            .Concat(typeof(RunState)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(method => method.ReturnType))
            .ToArray();

        Assert.That(
            surface,
            Has.No.Member(typeof(SplashFlow)),
            "RunState hands out the flow. A view holding one could spend CH §5.4's one "
                + "irreversible decision (AR §18.2).");

        // The four reads that stand in for it, so the row goes red if they are quietly removed too.
        foreach (string read in new[]
        {
            nameof(RunState.IsSplashPending),
            nameof(RunState.IsSplashOpen),
            nameof(RunState.HasSplashed),
            nameof(RunState.SplashCandidates),
        })
        {
            Assert.That(
                typeof(RunState).GetProperty(read),
                Is.Not.Null,
                $"RunState.{read} is gone, so the screen has to reach for the flow.");
        }

        Assert.That(typeof(RunState).GetMethod(nameof(RunState.SplashBranchesOf)), Is.Not.Null);
    }

    // ---- The run: what the choice widens --------------------------------------------------------

    [Test]
    public void Run_TheOfferWidensAfterTheChoice()
    {
        StartResumed(SixTaken(), level: 8, pendingLevelUps: 1);

        RunState state = _session.State;
        var progression = (IProgressionCommands)_session;

        Assert.That(state.IsNodeAvailable(new ContentId(LentActive)), Is.False, "A stranger to the tree.");

        Assert.That(
            state.SplashBranchesOf(new ContentId(GravecallerId))[0].NodeCount,
            Is.EqualTo(4),
            "The four-node branch this row borrows.");

        progression.OpenSplash();
        progression.ChooseSplash(new ContentId(GravecallerId), 0);

        Assert.That(state.HasSplashed, Is.True);
        Assert.That(state.IsNodeAvailable(new ContentId(LentActive)), Is.True);
        Assert.That(state.IsNodeAvailable(new ContentId(LentMaxHp)), Is.True);

        progression.OpenLevelUp();

        Assert.That(
            state.Offer.Select(id => id.Value).ToArray(),
            Has.Some.EqualTo(LentMaxHp).And.Some.EqualTo(LentActive),
            "The offer did not widen: the next level-up draws from sixteen nodes, not twelve.");
    }

    [Test]
    public void Run_ABorrowedNodesEffectsApply()
    {
        StartResumed(SixTaken(), level: 8, pendingLevelUps: 1);

        RunState state = _session.State;
        var progression = (IProgressionCommands)_session;

        float before = state.PlayerMaxHp;

        progression.OpenSplash();
        progression.ChooseSplash(new ContentId(GravecallerId), 0);
        progression.OpenLevelUp();
        progression.ChooseOffer(IndexOf(state.Offer, LentMaxHp));

        Assert.That(state.TakenNodeIds.Last().Value, Is.EqualTo(LentMaxHp));

        // A borrowed node is taken through SkillTree.Take like any other, so its effects go on
        // through the same EffectRegistry (rule 10).
        Assert.That(state.PlayerMaxHp, Is.EqualTo(before + LentMaxHpBonus).Within(1e-3f));
    }

    [Test]
    public void Run_ABorrowedActiveReachesTheRunner()
    {
        StartResumed(SixTaken(), level: 8, pendingLevelUps: 1);

        RunState state = _session.State;
        var progression = (IProgressionCommands)_session;

        Assert.That(state.OwnedActiveCount, Is.Zero, "The primary tree holds no Active at all.");

        progression.OpenSplash();
        progression.ChooseSplash(new ContentId(GravecallerId), 0);
        progression.OpenLevelUp();
        progression.ChooseOffer(IndexOf(state.Offer, LentActive));

        // **No second take path and no second event** (rule 10): an Active from a borrowed branch
        // reaches the runner through LevelUpFlow.Choose's existing "tell the runner directly".
        Assert.That(state.OwnedActiveCount, Is.EqualTo(1));
        Assert.That(state.SkillIdAt(0).Value, Is.EqualTo(LentActive));

        _session.Tick(Sense());

        Assert.That(
            _events.Of<SkillCast>().Select(cast => cast.SkillId.Value).ToArray(),
            Has.Some.EqualTo(LentActive),
            "The borrowed Active is owned and never fires.");
    }

    [Test]
    public void Run_TheTickThatCrossedTheThresholdFinishes()
    {
        // Five taken and a pick owed, so the *sixth* node is taken by this row rather than restored.
        StartResumed(SixTaken().Take(5).ToArray(), level: 7, pendingLevelUps: 1);

        RunState state = _session.State;
        var progression = (IProgressionCommands)_session;

        Assert.That(state.IsSplashPending, Is.False, "Five of twelve is not half.");

        progression.OpenLevelUp();
        progression.ChooseOffer(0);

        Assert.That(state.TakenNodeCount, Is.EqualTo(6));

        // **Owed, and not open.** The moment is raised by RunTicker.LevelUpPhase at the top of the
        // next frame (rule 4), never by the take that crossed the threshold.
        Assert.That(state.IsSplashPending, Is.True);
        Assert.That(state.IsSplashOpen, Is.False);
        Assert.That(_events.Count<SplashOffered>(), Is.Zero);

        // And the tick that follows runs in full: the moment costs the run nothing until something
        // opens it.
        float time = state.Time;

        _session.Tick(Sense());

        Assert.That(state.Time, Is.GreaterThan(time), "The tick was cut short.");
        Assert.That(_events.Count<SplashOffered>(), Is.Zero);

        progression.OpenSplash();

        Assert.That(_events.Count<SplashOffered>(), Is.EqualTo(1));
        Assert.That(state.IsSplashOpen, Is.True);
    }

    /// <summary>
    /// Rule 5's premise, from core's side: both can be owed on one frame, so something has to
    /// choose. <c>RunTicker</c> is what chooses, and <c>FrameOrderTests</c> is where that is
    /// asserted against a real <c>RunPause</c>.
    /// </summary>
    [Test]
    public void Run_BothCanBeOwedOnOneFrame()
    {
        StartResumed(SixTaken(), level: 8, pendingLevelUps: 1);

        RunState state = _session.State;

        Assert.That(state.IsLevelUpPending, Is.True);
        Assert.That(state.IsSplashPending, Is.True);

        // **And the splash steps aside while an offer is on the table**, which is the other half:
        // two screens cannot hold one RunPause, so the pick already drawn is finished first.
        ((IProgressionCommands)_session).OpenLevelUp();

        Assert.That(state.HasOffer, Is.True);
        Assert.That(state.IsSplashPending, Is.False, "The splash would open over a level-up card.");
    }

    [Test]
    public void Run_DrawsNothingFromAnyStream()
    {
        StartResumed(SixTaken(), level: 7, pendingLevelUps: 0);

        var progression = (IProgressionCommands)_session;

        RandomState before = _random.Capture();

        progression.OpenSplash();
        progression.ChooseSplash(new ContentId(GravecallerId), 0);

        RandomState after = _random.Capture();

        // Every candidate is offered in catalog order, so there is no draw and therefore no seed
        // question: Open is idempotent in the strong sense LevelUpFlow.Open has to work for.
        Assert.That(after.Spawn, Is.EqualTo(before.Spawn));
        Assert.That(after.Offers, Is.EqualTo(before.Offers));
        Assert.That(after.Affixes, Is.EqualTo(before.Affixes));
        Assert.That(after.Drops, Is.EqualTo(before.Drops));
        Assert.That(after.Misc, Is.EqualTo(before.Misc));

        // The structural half, which is what makes the five above true for ever rather than today:
        // the flow is handed no generator at all.
        Type[] dependencies = typeof(SplashFlow).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.That(dependencies, Has.No.Member(typeof(IRandom)));
        Assert.That(dependencies, Has.No.Member(typeof(IRandomStream)));
    }

    // ---- Rule 6: the resume derives the branch --------------------------------------------------

    [Test]
    public void Resume_DerivesTheBranchFromTheSavedIds()
    {
        ContentId[] saved = FourPrimaryAndTwoBorrowed();

        StartResumed(saved, level: 7, pendingLevelUps: 0);

        RunState state = _session.State;

        Assert.That(state.HasSplashed, Is.True, "The branch was not derived from the saved ids.");
        Assert.That(state.TakenNodeCount, Is.EqualTo(6), "And all six replayed.");
        Assert.That(state.TakenNodeIds, Is.EqualTo(saved));
        Assert.That(state.IsSplashPending, Is.False);

        // The borrowed branch is back whole, not only the two nodes the save named.
        Assert.That(state.IsNodeAvailable(new ContentId(LentA)), Is.True);
    }

    [Test]
    public void Resume_InstallsBeforeItReplays()
    {
        ContentId[] saved = FourPrimaryAndTwoBorrowed();

        Assert.DoesNotThrow(() => StartResumed(saved, level: 7, pendingLevelUps: 0));

        // **The ordering asserted rather than described**: the same ids replayed into a tree that
        // has borrowed nothing throw, naming content validation — which is what a resume would do if
        // SplashFlow.Restore ran after SkillTree.Restore rather than before it.
        var unsplashed = new SkillTree(
            new TreeRules(OathboundTree(), _catalog),
            Registry(),
            new SilentEvents());

        Assert.Throws<KeyNotFoundException>(() => unsplashed.Restore(saved));
    }

    [Test]
    public void Resume_ASaveWithNoBorrowedIdsComesBackUnsplashed()
    {
        // **Rule 6's stated cost, asserted rather than hidden.** A run saved after the choice and
        // before its first borrowed pick carries no foreign id, so there is nothing to derive from
        // and the moment is owed again. The window is narrow and the failure is a second choice
        // rather than a broken tree.
        StartResumed(SixTaken(), level: 7, pendingLevelUps: 0);

        Assert.That(_session.State.HasSplashed, Is.False);
        Assert.That(_session.State.IsSplashPending, Is.True, "The moment is owed a second time.");
    }

    /// <remarks>
    /// <b>Renamed from <c>Resume_TheFormatIsStillVersionThree</c> at M6-01b</b>, which is the task
    /// that bumped the format to 4 — and the rename is the row being read correctly rather than
    /// weakened. Rule 6's claim was never <em>"this format never moves"</em>; it was <em>"this
    /// object puts nothing in it"</em>, and the version number was standing in for that because
    /// nothing else had asked the format to move. v4 came, added six fields for M6's mechanics, and
    /// **still carries no field for the borrowed branch** — which is what the sweep below has always
    /// been the real statement of.
    /// </remarks>
    [Test]
    public void Resume_TheFormatCarriesNoBorrowedBranch()
    {
        Assert.That(
            typeof(RunSnapshot)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Where(name => name.Contains("Splash") || name.Contains("Borrow")),
            Is.Empty,
            "RunSnapshot gained a field for the borrowed branch. CH §5.4's branch is derived from "
                + "TakenNodeIds, so a field and a migration step would be paid for a single "
                + "enum-sized fact (rule 6).");

        // And the run format has moved since, which is what makes the sweep above a claim about
        // this object rather than a claim about the format standing still.
        Assert.That(RunSnapshot.CurrentVersion, Is.GreaterThan(3), "M6-01b is v4.");
    }

    [Test]
    public void Resume_IsSilent()
    {
        StartResumed(FourPrimaryAndTwoBorrowed(), level: 7, pendingLevelUps: 0);

        Assert.That(_session.State.HasSplashed, Is.True, "The fixture's premise.");

        // Nothing may publish before RunStarted, and a branch borrowed in a previous session is not
        // news — SkillTree.Restore's rule, one object over.
        Assert.That(_events.Count<SplashOffered>(), Is.Zero);
        Assert.That(_events.Count<SplashChosen>(), Is.Zero);
        Assert.That(_events.Count<NodeTaken>(), Is.Zero);
        Assert.That(_events.Count<RunStarted>(), Is.EqualTo(1));
    }

    // ---- Guards, implied rather than listed -----------------------------------------------------

    [Test]
    public void Flow_RefusesANullDependency()
    {
        World world = Build();

        var own = new ContentId(OathboundId);

        Assert.Throws<ArgumentNullException>(
            () => new SplashFlow(null, world.Catalog, world.Effects, own, world.Events));
        Assert.Throws<ArgumentNullException>(
            () => new SplashFlow(world.Tree, null, world.Effects, own, world.Events));
        Assert.Throws<ArgumentNullException>(
            () => new SplashFlow(world.Tree, world.Catalog, null, own, world.Events));
        Assert.Throws<ArgumentNullException>(
            () => new SplashFlow(world.Tree, world.Catalog, world.Effects, own, null));

        Assert.Throws<ArgumentNullException>(() => world.Flow.TryDerive(null, out _, out _));
    }

    [Test]
    public void Flow_RestoreTwiceThrows()
    {
        World world = Build();

        world.Flow.Restore(new ContentId(GravecallerId), 0);

        Assert.That(world.Flow.HasSplashed, Is.True);
        Assert.That(world.Events.All, Is.Empty, "Restore is silent.");

        Assert.Throws<InvalidOperationException>(
            () => world.Flow.Restore(new ContentId(GravecallerId), 1));
    }

    [Test]
    public void Flow_TryDeriveAnswersFalseForAStranger()
    {
        World world = Build();

        // A saved id that belongs to nobody is left for SkillTree.Restore's existing refusal, which
        // is what names it — a build that has dropped a class should fail there, on content
        // validation, rather than here on a derivation that has nothing to say.
        Assert.That(
            world.Flow.TryDerive(new[] { new ContentId("skill.nobody") }, out _, out int branch),
            Is.False);

        Assert.That(branch, Is.EqualTo(TreeRules.NoSplash));
    }

    // ---- Fixture: the pure half ------------------------------------------------------------------

    /// <summary>What a <c>Splash_</c> row drives: a tree, the registry behind it, and the flow.</summary>
    private sealed class World
    {
        internal RecordingEvents Events;
        internal ContentCatalog Catalog;
        internal EffectRegistry Effects;
        internal SkillTree Tree;
        internal SplashFlow Flow;
    }

    /// <summary>An Oathbound run over the twelve-node tree, with the Gravecaller to borrow from.</summary>
    private static World Build(bool secondClass = true)
    {
        var characters = secondClass
            ? new[] { Character(OathboundId), Character(GravecallerId) }
            : new[] { Character(OathboundId) };

        SkillTreeSpec[] trees = secondClass
            ? new[] { OathboundTree(), GravecallerTree() }
            : new[] { OathboundTree() };

        return WorldOver(
            OathboundTree(),
            new ContentId(OathboundId),
            new ContentCatalog(characters, null, null, AllSkills(), trees),
            Registry());
    }

    /// <summary>
    /// The same pair at CH §5's full twenty-seven nodes, for the threshold row alone.
    /// </summary>
    /// <remarks>
    /// <c>TreeRulesTests.FullTree</c> is also the Oathbound's, so it replaces this file's twelve-node
    /// tree rather than joining it — a catalog holds one tree per class (CH §5).
    /// </remarks>
    private static World BuildOverFullTrees()
    {
        SkillTreeSpec primary = TreeRulesTests.FullTree();

        var skills = new List<SkillSpec>(TreeRulesTests.FullSkills());
        skills.AddRange(LenderSkills());

        return WorldOver(
            primary,
            new ContentId(OathboundId),
            new ContentCatalog(
                new[] { Character(OathboundId), Character(GravecallerId) },
                null,
                null,
                skills,
                new[] { primary, GravecallerTree() }),
            Registry());
    }

    /// <summary>A Gravecaller run, which is the mirror: the Oathbound is the one candidate.</summary>
    /// <remarks>
    /// Its registry answers for <c>KnockbackOnSwing</c> as well, because the Gravecaller's own tree
    /// carries one — a run whose <em>primary</em> tree holds a primitive nobody registered is
    /// refused by <c>SkillTree</c>'s constructor, which is a different rule and not this one.
    /// </remarks>
    private static World BuildGravecallerRun() =>
        WorldOver(
            GravecallerTree(),
            new ContentId(GravecallerId),
            new ContentCatalog(
                new[] { Character(OathboundId), Character(GravecallerId) },
                null,
                null,
                AllSkills(),
                new[] { OathboundTree(), GravecallerTree() }),
            Registry(withKnockback: true));

    private static World WorldOver(
        SkillTreeSpec primary,
        ContentId own,
        ContentCatalog catalog,
        EffectRegistry effects)
    {
        var events = new RecordingEvents();

        var tree = new SkillTree(new TreeRules(primary, catalog), effects, events);

        return new World
        {
            Events = events,
            Catalog = catalog,
            Effects = effects,
            Tree = tree,
            Flow = new SplashFlow(tree, catalog, effects, own, events),
        };
    }

    /// <summary>A world at the threshold with the screen up, which is where most refusals live.</summary>
    private static World Opened()
    {
        World world = Build();

        TakeUntil(world.Tree, world.Flow.OpensAt);

        world.Flow.Open();

        return world;
    }

    /// <summary>
    /// The registry a run of this fixture's Oathbound has: <c>ModifyStat</c> and nothing else.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow — the lender's branch that carries a <c>KnockbackOnSwing</c> is what
    /// <see cref="Splash_RefusesABranchWhoseEffectsHaveNoHandler"/> is about, and a registry that
    /// answered for everything would make that row untestable.
    /// </remarks>
    private static EffectRegistry Registry(bool withKnockback = false)
    {
        var registry = new EffectRegistry();

        registry.Register<ModifyStat>(new Ignoring<ModifyStat>());

        if (withKnockback)
        {
            registry.Register<KnockbackOnSwing>(new Ignoring<KnockbackOnSwing>());
        }

        return registry;
    }

    /// <summary>Takes from <c>Available</c> until <paramref name="taken"/> nodes are owned.</summary>
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

    // ---- Fixture: the run half -------------------------------------------------------------------

    /// <summary>Starts a resumed run carrying <paramref name="taken"/>, over this file's content.</summary>
    private void StartResumed(IReadOnlyList<ContentId> taken, int level, int pendingLevelUps)
    {
        _events = new RecordingEvents();
        _random = new FixedRandom(7, Enumerable.Repeat(LastCandidate, 256).ToArray());

        _catalog = new ContentCatalog(
            new[] { Character(OathboundId, OathboundMaxHp), Character(GravecallerId) },
            null,
            new[] { Mode() },
            AllSkills(),
            new[] { OathboundTree(), GravecallerTree() });

        _session = new RunSession(
            _catalog,
            _random,
            _events,
            new RecordingIntents(),
            new RunRecorder(_random, new FixedClock(Instant), _events),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        _session.Start(new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            _random.Seed,
            3,
            SpawnPlan.Empty,
            Saved(taken, level, pendingLevelUps)));
    }

    private RunSnapshot Saved(IReadOnlyList<ContentId> taken, int level, int pendingLevelUps) =>
        new RunSnapshot(
            RunSnapshot.CurrentVersion,
            new ContentId(ModeId),
            new ContentId(OathboundId),
            _random.Seed,
            3,
            new RandomState(0, 0, 0, 0, 0),
            100f,
            0f,
            120f,
            Instant,
            level,
            0f,
            pendingLevelUps,
            taken,
            new ContentId[SkillRunner.MaxManualSlots],
            default,
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>());

    /// <summary>One frame of the world, with nothing in it: these rows are about the tree.</summary>
    private static WorldSnapshot Sense() =>
        new WorldSnapshot(Capacity)
        {
            Dt = Frame,
            PlayerPosition = Vector3.Zero,
        };

    private static int IndexOf(IReadOnlyList<ContentId> offer, string id)
    {
        for (int i = 0; i < offer.Count; i++)
        {
            if (offer[i].Value == id)
            {
                return i;
            }
        }

        Assert.Fail(
            $"'{id}' is not in the offer, so this row cannot take it. The offer was "
                + string.Join(", ", offer.Select(entry => entry.Value)));

        return -1;
    }

    /// <summary>
    /// Six primary ids in an order the gating allows — branch a whole, then branch b's tier 1.
    /// </summary>
    private static ContentId[] SixTaken() => new[]
    {
        Primary('a', 1, 'a'), Primary('a', 1, 'b'), Primary('a', 2, 'a'), Primary('a', 2, 'b'),
        Primary('b', 1, 'a'), Primary('b', 1, 'b'),
    };

    /// <summary>Four primary ids and two borrowed ones — what a splashed save carries.</summary>
    private static ContentId[] FourPrimaryAndTwoBorrowed() => new[]
    {
        Primary('a', 1, 'a'), Primary('a', 1, 'b'), Primary('a', 2, 'a'), Primary('a', 2, 'b'),
        Id(LentActive), Id(LentMaxHp),
    };

    // ---- Content ---------------------------------------------------------------------------------

    /// <summary>
    /// M3-12's v1 shape: three branches of two tiers of two, twelve nodes, no Keystone. The tree the
    /// game ships, so <c>OpensAt</c> is six.
    /// </summary>
    private static SkillTreeSpec OathboundTree() => new SkillTreeSpec(
        new ContentId(OathboundTreeId),
        new ContentId(OathboundId),
        new[] { PrimaryBranch('a'), PrimaryBranch('b'), PrimaryBranch('c') });

    private static SkillBranchSpec PrimaryBranch(char letter) => new SkillBranchSpec(
        new LocKey($"tree.oathbound.{letter}"),
        new IReadOnlyList<ContentId>[]
        {
            new[] { Primary(letter, 1, 'a'), Primary(letter, 1, 'b') },
            new[] { Primary(letter, 2, 'a'), Primary(letter, 2, 'b') },
        });

    /// <summary>
    /// The lender: a four-node branch worth borrowing, and two branches this run cannot have.
    /// </summary>
    /// <remarks>
    /// <b>Branch 1 carries a primitive nobody registered and branch 2 aims at minions</b>, which are
    /// the two refusals <see cref="Splash_RefusesABranchWhoseEffectsHaveNoHandler"/> and
    /// <see cref="Splash_RefusesABranchAimedAtMinionsThisClassCannotRaise"/> are about. <b>Branch
    /// 1</b> also ends in a Keystone, so <see cref="Splash_BranchesOfDropsTheKeystone"/> has one to
    /// drop — this line said branch 2 until M5-08a read it against the tree below.
    /// <b>So exactly one of the three is borrowable by a class that raises no minions</b>, which is
    /// the shape <see cref="Splash_BranchesOfListsARefusedBranchRatherThanHidingIt"/> pins and the
    /// shape M5-08's playtest walked into on a screen with no way out.
    /// </remarks>
    private static SkillTreeSpec GravecallerTree() => new SkillTreeSpec(
        new ContentId(GravecallerTreeId),
        new ContentId(GravecallerId),
        new[]
        {
            new SkillBranchSpec(
                new LocKey("tree.gravecaller.legion"),
                new IReadOnlyList<ContentId>[]
                {
                    new[] { Id(LentActive), Id(LentMaxHp) },
                    new[] { Id(LentA), Id(LentB) },
                }),
            new SkillBranchSpec(
                new LocKey("tree.gravecaller.grave-work"),
                new IReadOnlyList<ContentId>[]
                {
                    new[] { Id(Unhandled), GraveWork(1, 'b') },
                    new[] { GraveWork(2, 'a'), GraveWork(2, 'b') },
                    new[] { GraveWork(3, 'a'), GraveWork(3, 'b') },
                    new[] { GraveWork(4, 'a'), GraveWork(4, 'b') },
                    new[] { Id(LenderKeystone) },
                }),
            new SkillBranchSpec(
                new LocKey("tree.gravecaller.rot"),
                new IReadOnlyList<ContentId>[]
                {
                    new[] { Id(MinionAimed) },
                    new[] { Id("skill.gravecaller.horde") },
                    new[] { Id("skill.gravecaller.blight") },
                }),
        });

    private static ContentId GraveWork(int tier, char slot) =>
        Id($"skill.gravecaller.gw{tier}{slot}");

    private static IReadOnlyList<SkillSpec> AllSkills()
    {
        var skills = new List<SkillSpec>();

        foreach (char letter in new[] { 'a', 'b', 'c' })
        {
            skills.Add(Passive(Primary(letter, 1, 'a')));
            skills.Add(Passive(Primary(letter, 1, 'b')));
            skills.Add(Passive(Primary(letter, 2, 'a')));
            skills.Add(Passive(Primary(letter, 2, 'b')));
        }

        skills.AddRange(LenderSkills());

        return skills;
    }

    private static IReadOnlyList<SkillSpec> LenderSkills()
    {
        var skills = new List<SkillSpec>
        {
            ActiveNode(LentActive),
            Node(Id(LentMaxHp), new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, LentMaxHpBonus)),
            Passive(Id(LentA)),
            Passive(Id(LentB)),
            Node(Id(Unhandled), new KnockbackOnSwing(2f)),
            Node(
                Id(MinionAimed),
                new ModifyStat(PlayerStat.MaxHp, ModifierKind.PercentAdd, 0.1f, StatTarget.Minions)),
            Passive(Id("skill.gravecaller.horde")),
            Passive(Id("skill.gravecaller.blight")),
            new SkillSpec(
                Id(LenderKeystone),
                new LocKey($"{LenderKeystone}.name"),
                new LocKey($"{LenderKeystone}.desc"),
                SkillKind.Keystone,
                new IEffect[] { new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.2f) }),
        };

        skills.Add(Passive(GraveWork(1, 'b')));

        for (int tier = 2; tier <= 4; tier++)
        {
            skills.Add(Passive(GraveWork(tier, 'a')));
            skills.Add(Passive(GraveWork(tier, 'b')));
        }

        return skills;
    }

    private static SkillSpec Passive(ContentId id) =>
        Node(id, new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f));

    private static SkillSpec Node(ContentId id, IEffect effect) => new SkillSpec(
        id,
        new LocKey($"{id.Value}.name"),
        new LocKey($"{id.Value}.desc"),
        SkillKind.Passive,
        new[] { effect });

    /// <summary>
    /// An Active whose trigger always holds, so one tick after it is taken is enough to see it fire.
    /// </summary>
    private static SkillSpec ActiveNode(string id) => new SkillSpec(
        Id(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Active,
        Array.Empty<IEffect>(),
        new ActiveSpec(
            8f,
            new TriggerSpec(new[]
            {
                // Below 1.5 rather than a clause that reads as "always": HpFraction is at most 1.
                new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 1.5f),
            }),
            new IEffect[] { new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.5f) }));

    /// <summary>
    /// A class with no <c>MinionSpec</c> unless it is the Gravecaller — the run-scoped fact the
    /// minion refusal turns on.
    /// </summary>
    private static CharacterSpec Character(string id, float maxHp = 100f) => new CharacterSpec(
        Id(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.description"),
        maxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        minions: id == GravecallerId
            ? new MinionSpec(
                Id("enemy.wight"), new LocKey("enemy.wight.name"), 4, 20f, 0.25f, 20f, 3f, 4f, 1f, 1.2f)
            : null);

    /// <summary>
    /// An endless mode with an empty roster: nothing to compose, so no stage flow and no waves.
    /// </summary>
    /// <remarks>
    /// Legal since M2-02 — a mode whose content comes entirely from its spawn plan — and exactly
    /// what these rows want: the moment is about the tree, and a fixture that also composed a fight
    /// would be timing one.
    /// </remarks>
    private static ModeSpec Mode() => new ModeSpec(
        new ContentId(ModeId),
        new LocKey("mode.test.name"),
        1,
        true,
        0,
        new ScalingSpec(
            new BudgetCurve(20f, 6f, 0.04f),
            new WaveCurve(2, 1000, 2, 2),
            new ConcurrencyCurve(DeviceCap, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0)),
        Scalings.Xp(),
        Array.Empty<RosterEntry>(),
        new[] { new ContentId("arena.pillars") },
        null,
        new OverflowSpec(0.02f, 0.02f));

    private static ContentId Primary(char branch, int tier, char slot) =>
        Id($"skill.oathbound.{branch}{tier}{slot}");

    private static ContentId Id(string value) => new ContentId(value);

    /// <summary>A handler that answers for a primitive and does nothing with it.</summary>
    /// <remarks>
    /// Used only by the pure half: the <c>Run_</c> rows go through a real <c>RunSession</c>, whose
    /// own <c>ModifyStatHandler</c> is what moves the numbers they assert.
    /// </remarks>
    private sealed class Ignoring<T> : IEffectHandler<T>
        where T : IEffect
    {
        public void Apply(T effect, object source)
        {
        }

        public void Remove(T effect, object source)
        {
        }
    }
}
