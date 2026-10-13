using System;
using System.Collections.Generic;
using System.Linq;
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
/// UI-03a: a mode with a free pick banks a point for each level and opens nothing, and a command
/// spends a point on any open node — no offer, no draw.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row drives a real session</b>, for <c>RunSessionResumeTests</c>' reason: <c>RunState</c>'s
/// constructor is <c>internal</c> and <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c>,
/// deliberately (AR §18.2).
/// </para>
/// <para>
/// <b>Levels arrive through a save rather than by playing to them.</b> A snapshot carrying a level,
/// the nodes taken and the points still owed puts the run exactly where each rule is about, in one
/// call — the same bargain <c>SplashFlowTests</c> strikes, and for its reason: a fixture that fought
/// its way there would be testing the XP curve.
/// </para>
/// </remarks>
[TestFixture]
public sealed class FreePickTests
{
    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string FirstArenaId = "arena.test.first";
    private const string SecondArenaId = "arena.test.second";
    private const string TreeId = "tree.oathbound";

    /// <summary>
    /// The tree: branch a holds two nodes at tier 1 and one at tier 2, b holds the one Active, c one
    /// more Passive. Tier 2 wants one of its branch taken (CH §5), so <see cref="NodeTier2"/> is the
    /// locked node until <see cref="NodeA1"/> or <see cref="NodeA2"/> is taken.
    /// </summary>
    private const string NodeA1 = "skill.test.a1";
    private const string NodeA2 = "skill.test.a2";
    private const string NodeTier2 = "skill.test.a3";
    private const string NodeActive = "skill.test.b1";
    private const string NodeC1 = "skill.test.c1";

    private const int TreeSize = 5;

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;
    private const float MaxHp = 100f;
    private const float Frame = 1f / 60f;
    private const float Ring = 10f;

    private static readonly Vector3 Door = new Vector3(0f, 0f, 18f);

    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    private RecordingEvents _events;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private RunSession _session;

    private IProgressionCommands Progression => _session;

    // ---- Rule 1: the flag off changes nothing ----------------------------------------------------

    [Test]
    public void NotFree_IsUnchanged()
    {
        Assert.That(Mode(free: false).HasFreePick, Is.False, "Off is what a mode that never says is.");

        Start(free: false, level: 3, pending: 2);

        RunState state = _session.State;

        Assert.That(state.Level, Is.EqualTo(3));
        Assert.That(state.IsFreePick, Is.False);
        Assert.That(state.IsLevelUpPending, Is.True, "A drawing run still owes its offer.");
        Assert.That(Progression.SkillPoints, Is.Zero, "And owes no free point.");

        Progression.OpenLevelUp();

        Assert.That(state.HasOffer, Is.True, "The offer still opens.");
        Assert.That(_events.Count<OfferPresented>(), Is.EqualTo(1));

        Assert.Throws<InvalidOperationException>(() => Progression.TakeNode(new ContentId(NodeA1)));
    }

    // ---- Rule 2: no offer, no draw ---------------------------------------------------------------

    [Test]
    public void Free_LevelUp_OpensAndDrawsNothing()
    {
        Start(free: true, level: 3, pending: 2);

        RunState state = _session.State;
        RandomState before = _random.Capture();

        Assert.That(state.PendingLevelUps, Is.EqualTo(2), "Two levels, banked.");
        Assert.That(state.IsLevelUpPending, Is.False, "Nothing is owed to a screen.");
        Assert.That(state.IsSplashPending, Is.False);
        Assert.That(Progression.IsLevelUpPending, Is.False);
        Assert.That(Progression.IsSplashPending, Is.False);

        // The frame, as RunTicker.LevelUpPhase runs it: it asks, and a stray open is also harmless.
        _session.Tick(Sense());
        Progression.OpenLevelUp();
        Progression.OpenSplash();

        Assert.That(state.HasOffer, Is.False);
        Assert.That(state.Offer, Is.Empty);
        Assert.That(_events.Count<OfferPresented>(), Is.Zero);
        Assert.That(_events.Count<SplashOffered>(), Is.Zero);
        Assert.That(state.PendingLevelUps, Is.EqualTo(2), "And no point was spent.");
        Assert.That(_random.Capture().Offers, Is.EqualTo(before.Offers), "The Offers stream did not move.");
    }

    // ---- Rule 3: the count -----------------------------------------------------------------------

    [Test]
    public void SkillPoints_FollowPending_ZeroWithoutTree()
    {
        Assert.That(Progression.SkillPoints, Is.Zero, "Zero outside a run.");

        Start(free: true, level: 3, pending: 2);

        Assert.That(Progression.SkillPoints, Is.EqualTo(2));
        Assert.That(_session.State.SkillPoints, Is.EqualTo(2));

        Progression.TakeNode(new ContentId(NodeA1));

        Assert.That(Progression.SkillPoints, Is.EqualTo(1), "It follows PendingLevelUps down.");

        // A class with no tree banks its levels and has nothing to spend them on.
        _session.End();
        Start(free: true, level: 3, pending: 2, withTree: false);

        Assert.That(_session.State.PendingLevelUps, Is.EqualTo(2), "Banked, as it always was.");
        Assert.That(_session.State.IsFreePick, Is.False, "There is no flow for a pick to be free of.");
        Assert.That(Progression.SkillPoints, Is.Zero);

        _session.End();

        Assert.That(Progression.SkillPoints, Is.Zero, "And zero again once the run is over.");
    }

    // ---- Rules 4 and 5: TakeNode -----------------------------------------------------------------

    [Test]
    public void TakeNode_Open_TakesAndSpendsOne()
    {
        Start(free: true, level: 3, pending: 2);

        Progression.TakeNode(new ContentId(NodeA1));

        RunState state = _session.State;

        Assert.That(state.TakenNodeIds.Select(id => id.Value), Is.EqualTo(new[] { NodeA1 }));
        Assert.That(state.SkillPoints, Is.EqualTo(1));
        Assert.That(_events.Count<NodeTaken>(), Is.EqualTo(1));
        Assert.That(_events.Count<OverflowGranted>(), Is.Zero, "A taken point is not an Overflow one.");
        Assert.That(state.OverflowLevels, Is.Zero);
    }

    [Test]
    public void TakeNode_Locked_ThrowsKeepsPoint()
    {
        Start(free: true, level: 3, pending: 2);

        RunState state = _session.State;

        Assert.Throws<InvalidOperationException>(
            () => Progression.TakeNode(new ContentId(NodeTier2)),
            "Tier 2 wants a node of its branch first.");

        Assert.Throws<KeyNotFoundException>(
            () => Progression.TakeNode(new ContentId("skill.test.nobody")),
            "A stranger to the tree.");

        Progression.TakeNode(new ContentId(NodeA1));

        Assert.Throws<InvalidOperationException>(
            () => Progression.TakeNode(new ContentId(NodeA1)),
            "A node cannot be taken twice.");

        Assert.That(state.SkillPoints, Is.EqualTo(1), "Three refusals, and only the one take spent a point.");
        Assert.That(state.TakenNodeCount, Is.EqualTo(1));

        // And the gate opens once its branch has a node in it.
        Assert.DoesNotThrow(() => Progression.TakeNode(new ContentId(NodeTier2)));
        Assert.That(state.SkillPoints, Is.Zero);
    }

    [Test]
    public void TakeNode_NoPointOrNotFree_Throws()
    {
        var id = new ContentId(NodeA1);

        Assert.Throws<InvalidOperationException>(() => Progression.TakeNode(id), "No run is running.");

        Start(free: true, level: 1, pending: 0);

        Assert.Throws<InvalidOperationException>(() => Progression.TakeNode(id), "No point is banked.");
        Assert.That(_session.State.TakenNodeCount, Is.Zero);

        _session.End();
        Start(free: false, level: 3, pending: 2);

        Assert.Throws<InvalidOperationException>(() => Progression.TakeNode(id), "The mode draws.");
        Assert.That(_session.State.TakenNodeCount, Is.Zero);
        Assert.That(_session.State.PendingLevelUps, Is.EqualTo(2), "And its picks are untouched.");

        _session.End();
        Start(free: true, level: 3, pending: 2, withTree: false);

        Assert.Throws<InvalidOperationException>(() => Progression.TakeNode(id), "The class has no tree.");
    }

    [Test]
    public void TakeNode_Active_ReachesRunner()
    {
        Start(free: true, level: 2, pending: 1);

        RunState state = _session.State;

        Assert.That(state.OwnedActiveCount, Is.Zero);

        Progression.TakeNode(new ContentId(NodeActive));

        // Choose's order: the take, the point, then the runner is told directly.
        Assert.That(state.OwnedActiveCount, Is.EqualTo(1));
        Assert.That(state.SkillIdAt(0).Value, Is.EqualTo(NodeActive));
        Assert.That(state.SkillPoints, Is.Zero);
    }

    // ---- Rule 6: a full tree pays Overflow -------------------------------------------------------

    [Test]
    public void Settle_NothingAvailable_SpendsAsOverflow()
    {
        // The whole tree taken and two points still owed: level 8 earns seven picks, five on nodes.
        Start(free: true, level: 8, pending: 2, taken: Everything());

        RunState state = _session.State;

        Assert.That(state.OverflowLevels, Is.Zero, "The fixture's premise: nothing was spare yet.");
        Assert.That(state.SkillPoints, Is.EqualTo(2));

        Progression.SettleFullTree();

        Assert.That(state.OverflowLevels, Is.EqualTo(2));
        Assert.That(state.SkillPoints, Is.Zero);
        Assert.That(_events.Count<OverflowGranted>(), Is.EqualTo(2), "Each point is announced.");

        // Idempotent: nothing is left to settle.
        Progression.SettleFullTree();

        Assert.That(state.OverflowLevels, Is.EqualTo(2));
        Assert.That(_events.Count<OverflowGranted>(), Is.EqualTo(2));
    }

    [Test]
    public void Settle_NodeAvailable_SpendsNothing()
    {
        Start(free: true, level: 3, pending: 2);

        Progression.SettleFullTree();

        Assert.That(_session.State.SkillPoints, Is.EqualTo(2));
        Assert.That(_session.State.OverflowLevels, Is.Zero);
        Assert.That(_events.Count<OverflowGranted>(), Is.Zero);

        // And a run that draws is none of its business, however full its tree.
        _session.End();
        Start(free: false, level: 8, pending: 2, taken: Everything());

        Progression.SettleFullTree();

        Assert.That(_session.State.PendingLevelUps, Is.EqualTo(2), "A drawing run settles through OpenLevelUp.");
        Assert.That(_session.State.OverflowLevels, Is.Zero);

        // A question the frame loop asks every frame, so a finished run answers it quietly.
        _session.End();

        Assert.DoesNotThrow(() => Progression.SettleFullTree());
    }

    // ---- Rule 7: Reroll and Banish act on offers -------------------------------------------------

    [Test]
    public void Free_RerollAndBanish_Refused()
    {
        var banishable = new ContentId[TreeSize];

        // The control: the same Sanctum on a drawing run sells both, so the refusal below is the
        // flag's and not the fixture's.
        OpenTheSanctum(free: false);

        Assert.That(Progression.CanBuy(SanctumService.Reroll), Is.True);
        Assert.That(Progression.CanBuy(SanctumService.Banish), Is.True);
        Assert.That(Progression.BanishableInto(banishable), Is.EqualTo(TreeSize));

        _session.End();
        OpenTheSanctum(free: true);

        int essence = _session.State.Essence;

        Assert.That(Progression.CanBuy(SanctumService.Reroll), Is.False);
        Assert.That(Progression.CanBuy(SanctumService.Banish), Is.False);
        Assert.That(Progression.BanishableInto(banishable), Is.Zero, "No node is a candidate for a draw.");

        Assert.Throws<InvalidOperationException>(() => Progression.Buy(SanctumService.Reroll));
        Assert.Throws<InvalidOperationException>(() => Progression.Banish(new ContentId(NodeA1)));

        Assert.That(_session.State.Essence, Is.EqualTo(essence), "Nothing was charged.");
        Assert.That(_session.State.RerollsBought, Is.Zero);
        Assert.That(_session.State.IsNodeBanished(new ContentId(NodeA1)), Is.False);
    }

    // ---- Rule 8: a resume ------------------------------------------------------------------------

    [Test]
    public void Resume_KeepsBankedPoints()
    {
        // Level 8 earns seven picks: two on nodes, three still owed, so two were spare.
        Start(
            free: true,
            level: 8,
            pending: 3,
            taken: new[] { new ContentId(NodeA1), new ContentId(NodeActive) });

        RunState state = _session.State;

        Assert.That(state.SkillPoints, Is.EqualTo(3), "The banked points came back.");
        Assert.That(state.TakenNodeCount, Is.EqualTo(2));
        Assert.That(state.OverflowLevels, Is.EqualTo(2), "And the Overflow sum is still derived.");

        Assert.That(
            state.TakenNodeCount + state.OverflowLevels + state.SkillPoints,
            Is.EqualTo(state.Level - 1),
            "Level − 1 = Taken + Overflow + Pending, with no field of its own.");

        Assert.That(state.OwnedActiveCount, Is.EqualTo(1), "The Active was replayed into the runner.");
        Assert.That(_events.Count<OverflowGranted>(), Is.Zero, "A resume is silent.");
        Assert.That(_events.Count<NodeTaken>(), Is.Zero);
        Assert.That(_events.Count<OfferPresented>(), Is.Zero);

        // The points are still spendable after it.
        Progression.TakeNode(new ContentId(NodeA2));

        Assert.That(Progression.SkillPoints, Is.EqualTo(2));
    }

    // ---- Allocation ------------------------------------------------------------------------------

    [Test]
    public void TakeNode_AllocatesNothing()
    {
        var order = new[]
        {
            new ContentId(NodeA1),
            new ContentId(NodeA2),
            new ContentId(NodeTier2),
            new ContentId(NodeC1),
        };

        // The per-frame half first, while a node is available: what RunTicker asks every frame a
        // point is banked. Repeatable, so it takes AllocationAssert's full run.
        Start(free: true, level: 5, pending: 4, silent: true);

        AllocationAssert.None(() =>
        {
            _ = Progression.SkillPoints;
            _ = Progression.IsLevelUpPending;
            _ = Progression.IsSplashPending;
            Progression.SettleFullTree();
        });

        // The take itself. A node can only be taken once, so the body walks the tree: the warm-up
        // call takes the first and the one measured iteration takes the second.
        int next = 0;

        AllocationAssert.None(() => Progression.TakeNode(order[next++]), iterations: 1);

        Assert.That(_session.State.TakenNodeCount, Is.EqualTo(2), "The fixture took two.");
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>A free (or drawing) run, over a save that puts it where a rule is about.</summary>
    private void Start(
        bool free,
        int level,
        int pending,
        IReadOnlyList<ContentId> taken = null,
        bool withTree = true,
        int essence = 0,
        bool silent = false)
    {
        Build(free, withTree, silent);

        _session.Start(new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            _random.Seed,
            1,
            SpawnPlan.Empty,
            Snapshot(level, pending, taken, essence)));
    }

    /// <param name="silent">
    /// Publish to <see cref="SilentEvents"/> instead of recording, for the one row that measures
    /// allocation: the recorder boxes every payload, which would be the fake's cost and not core's.
    /// </param>
    private void Build(bool free, bool withTree, bool silent)
    {
        _events = new RecordingEvents();
        IDomainEvents sink = silent ? new SilentEvents() : _events;
        _random = new FixedRandom(1, Alternating(8_192));
        _clock = new FixedClock(Instant);

        _catalog = withTree
            ? new ContentCatalog(
                new[] { Oathbound() },
                new[] { Husk() },
                new[] { Mode(free) },
                TreeSkills(),
                new[] { Tree() })
            : new ContentCatalog(new[] { Oathbound() }, new[] { Husk() }, new[] { Mode(free) });

        _session = new RunSession(
            _catalog,
            _random,
            sink,
            new RecordingIntents(),
            new RunRecorder(_random, _clock, sink),
            Capacity,
            DeviceCap,
            ProjectileCapacity);
    }

    /// <summary>
    /// A run that has cleared its first stage and is standing in the Sanctum with Essence to spend —
    /// the one place Reroll and Banish are sold.
    /// </summary>
    private void OpenTheSanctum(bool free)
    {
        Start(free, level: 1, pending: 0, essence: 500);

        var report = new int[1];
        int killed = _events.Count<EnemyDied>();

        for (int i = 0; i < 9_000 && _events.Count<StageCleared>() == 0; i++)
        {
            IReadOnlyList<EnemySpawned> spawned = _events.Of<EnemySpawned>();

            bool standing = spawned.Count > killed;
            Vector3 where = standing ? spawned[killed].Position : Vector3.Zero;

            if (standing)
            {
                report[0] = spawned[killed].Id;
            }

            _session.Tick(Sense(where));

            if (!standing || !_session.IsRunning)
            {
                continue;
            }

            _session.ReportConeHits(report);

            if (_events.Count<EnemyDied>() > killed)
            {
                killed++;
            }
        }

        Assert.That(_events.Count<StageCleared>(), Is.GreaterThan(0), "The fixture failed to clear the stage.");

        for (int i = 0; i < 900 && !_session.IsSanctumOpen; i++)
        {
            _session.Tick(Sense());
        }

        Assert.That(_session.IsSanctumOpen, Is.True, "The fixture failed to reach the Sanctum.");
    }

    private static RunSnapshot Snapshot(int level, int pending, IReadOnlyList<ContentId> taken, int essence) =>
        new RunSnapshot(
            RunSnapshot.CurrentVersion,
            new ContentId(ModeId),
            new ContentId(OathboundId),
            1,
            1,
            new RandomState(101, 102, 103, 104, 105),
            MaxHp,
            0f,
            0f,
            Instant,
            level,
            0f,
            pending,
            taken ?? Array.Empty<ContentId>(),
            new ContentId[SkillRunner.MaxManualSlots],
            new RunEconomy(essence, 0f, 0, 0),
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>());

    /// <summary>Every node in an order the gating allows: tier 1 before tier 2.</summary>
    private static ContentId[] Everything() => new[]
    {
        new ContentId(NodeA1),
        new ContentId(NodeA2),
        new ContentId(NodeTier2),
        new ContentId(NodeActive),
        new ContentId(NodeC1),
    };

    private static WorldSnapshot Sense() => Sense(Vector3.Zero);

    private static WorldSnapshot Sense(Vector3 playerPosition) =>
        new WorldSnapshot(Capacity)
        {
            Dt = Frame,
            PlayerPosition = playerPosition,
            HasGate = true,
            GatePosition = Door,
            SpawnPoints = Points(8),
        };

    // ---- Content ---------------------------------------------------------------------------------

    private static ModeSpec Mode(bool free)
    {
        var scaling = new ScalingSpec(
            new BudgetCurve(20f, 6f, 0.04f),
            new WaveCurve(2, 1000, 2, 2),
            new ConcurrencyCurve(DeviceCap, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0));

        return new ModeSpec(
            new ContentId(ModeId),
            new LocKey("mode.test.name"),
            startingStage: 1,
            isEndless: true,
            finalStage: 0,
            scaling,
            Scalings.Xp(),
            new[] { new RosterEntry(new ContentId(HuskId), 1) },
            new[] { new ArenaEntry(new ContentId(FirstArenaId)), new ArenaEntry(new ContentId(SecondArenaId)) },
            bossRoster: null,
            overflow: new OverflowSpec(0.02f, 0.02f),
            sanctum: new SanctumSpec(25, 40, 40, 30f, 60, 15f),
            hasFreePick: free);
    }

    /// <summary>Authored <c>Static</c>, so a body stays where the wave put it for the kill walk to find.</summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
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

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        MaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        new ShieldSpec(12f, 3f, 1f));

    private static SkillTreeSpec Tree() => new SkillTreeSpec(
        new ContentId(TreeId),
        new ContentId(OathboundId),
        new[]
        {
            new SkillBranchSpec(
                new LocKey("branch.a"),
                new IReadOnlyList<ContentId>[]
                {
                    new[] { new ContentId(NodeA1), new ContentId(NodeA2) },
                    new[] { new ContentId(NodeTier2) },
                }),
            Branch('b', NodeActive),
            Branch('c', NodeC1),
        });

    private static SkillBranchSpec Branch(char letter, string id) => new SkillBranchSpec(
        new LocKey($"branch.{letter}"),
        new IReadOnlyList<ContentId>[] { new[] { new ContentId(id) } });

    private static IReadOnlyList<SkillSpec> TreeSkills() => new[]
    {
        TreeRulesTests.Passive(NodeA1),
        TreeRulesTests.Passive(NodeA2),
        TreeRulesTests.Passive(NodeTier2),
        TreeRulesTests.Active(NodeActive),
        TreeRulesTests.Passive(NodeC1),
    };

    /// <summary><paramref name="count"/> points on a ring, clear of the origin and of each other.</summary>
    private static IReadOnlyList<Vector3> Points(int count)
    {
        var points = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            double angle = 2d * Math.PI * i / count;

            points[i] = new Vector3((float)(Ring * Math.Cos(angle)), 0f, (float)(Ring * Math.Sin(angle)));
        }

        return points;
    }

    /// <summary><paramref name="count"/> values alternating between the first and last candidate.</summary>
    private static float[] Alternating(int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = i % 2 == 0 ? 0.1f : 0.9f;
        }

        return values;
    }
}
