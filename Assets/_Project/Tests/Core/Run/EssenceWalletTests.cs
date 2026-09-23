using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Core.Stage;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Run;

/// <summary>
/// GD §15's second currency, from the number up: the wallet's two verbs and the predicate in front
/// of them, the authored table that prices a stage clear, and the one edge in the game that pays.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three fixtures in one file, because they are three depths of one claim</b> (M6-01a's Files
/// table). The <c>Wallet_</c> and <c>Essence_</c> rows are arithmetic over objects with no world
/// around them; the <c>Stage_</c> rows drive a real <see cref="StageFlow"/> to the edge into
/// <c>Clear</c> and read what came out; the <c>Run_</c> rows drive a whole <see cref="RunSession"/>,
/// which is the only way to assert what <c>RunState</c> exposes and what it does not.
/// </para>
/// <para>
/// <b>The flow fixture ticks the frame in the session's order</b> — enemies ingested, the player,
/// the shots, then the director, then the flow — for <c>StageFlowTests</c>' reason: that ordering
/// <em>is</em> the rule, and a fixture that ticked the flow first would let every row here pass
/// while the real game read <c>IsStageComplete</c> a frame stale. It deliberately does not tick the
/// enemy behaviours, which is what lets a boss be stood up and cut down without a
/// <c>WardenBehaviour</c> anywhere near it.
/// </para>
/// <para>
/// <b><c>Restore</c> is reached by reflection</b>, because it is <c>internal</c> and
/// <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c> and deliberately never will (AR
/// §18.2) — <c>LevelUpFlowTests</c>' route to <c>GrantOverflow</c>, for its reason.
/// </para>
/// </remarks>
[TestFixture]
public sealed class EssenceWalletTests
{
    private const string HuskId = "enemy.husk";
    private const string WardenEnemyId = "enemy.warden";
    private const string BossId = "boss.warden";
    private const string OathboundId = "character.oathbound";
    private const string ModeId = "mode.test";

    /// <summary>GD §8.1's threat cost for the one archetype these rows compose from.</summary>
    private const int HuskCost = 4;

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;
    private const int MaxWaves = 5;

    /// <summary>Comfortably outside <see cref="SpawnDirector.MinPlayerDistance"/> of the origin.</summary>
    private const float Ring = 10f;

    /// <summary>GD §15's four numbers, which are also <c>Descent.asset</c>'s.</summary>
    private const int PerStageBase = 20;

    private const int PerStageDepth = 4;
    private const int PerElite = 15;
    private const int PerBoss = 60;

    private static readonly Vector3 Door = new Vector3(0f, 0f, 18f);

    private RecordingEvents _events;
    private ContentCatalog _catalog;
    private ModeSpec _mode;
    private EnemySystem _enemies;
    private ProjectileSystem _projectiles;
    private PlayerCombat _player;
    private SpawnDirector _director;
    private WaveComposer _composer;
    private WavePlan _plan;
    private StageFlow _flow;
    private EssenceWallet _wallet;
    private WorldSnapshot _snapshot;
    private IRandomStream _spawn;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
    }

    // ---- The wallet (rules 1, 7, 8, 9) -----------------------------------------------------------

    [Test]
    public void Wallet_StartsEmpty()
    {
        var wallet = new EssenceWallet(_events);

        Assert.That(wallet.Balance, Is.Zero);
        Assert.That(_events.All, Is.Empty, "A wallet coming into existence is not news.");
    }

    [Test]
    public void Wallet_EarnAdds()
    {
        var wallet = new EssenceWallet(_events);

        wallet.Earn(24);

        Assert.That(wallet.Balance, Is.EqualTo(24));

        EssenceChanged changed = _events.Single<EssenceChanged>();

        // Both numbers, because neither can be derived from the other without the reader keeping
        // state: a float-up wants the delta and a readout wants the balance.
        Assert.That(changed.Balance, Is.EqualTo(24));
        Assert.That(changed.Delta, Is.EqualTo(24));
    }

    [Test]
    public void Wallet_EarnIsSilentForNothing()
    {
        // **Health.Heal's rule, and for its reason** (rule 1). A payment of nothing is not news, and
        // a reader that had to filter zeroes out of EssenceChanged would be a reader that could
        // forget to — which is why the event's Delta is documented as never zero.
        var wallet = new EssenceWallet(_events);

        wallet.Earn(0);
        wallet.Earn(-5);

        Assert.That(wallet.Balance, Is.Zero, "and in particular not −5.");
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Wallet_SpendTakes()
    {
        EssenceWallet wallet = Banked(100);

        wallet.Spend(40);

        Assert.That(wallet.Balance, Is.EqualTo(60));

        EssenceChanged changed = _events.Single<EssenceChanged>();

        Assert.That(changed.Balance, Is.EqualTo(60));
        Assert.That(changed.Delta, Is.EqualTo(-40), "Negative for a purchase, on one event type.");
    }

    [Test]
    public void Wallet_CanAffordAnswersBeforeSpending()
    {
        // **M5-08a's lesson made structural** (rule 7). That task's finding was that a screen may
        // not offer what the model refuses: its guard was correct, atomic and well tested, and the
        // only fault was that nothing asked it before the player committed. This is the asking.
        EssenceWallet wallet = Banked(39);

        Assert.That(wallet.CanAfford(40), Is.False, "One Essence out of reach.");
        Assert.That(wallet.CanAfford(39), Is.True, "Exactly enough is enough.");
        Assert.That(wallet.CanAfford(0), Is.True, "And free is always affordable.");
    }

    [Test]
    public void Wallet_SpendingWhatIsNotThereThrows()
    {
        // The invariant behind the predicate, not an alternative to it (rule 7): a caller that asks
        // first never sees this, and one that does not is a screen offering what the wallet refuses.
        EssenceWallet wallet = Banked(39);

        _events.Clear();

        Assert.Throws<InvalidOperationException>(() => wallet.Spend(40));

        // Loud rather than clamped, and — the half that matters — atomic: nothing was taken and
        // nothing was said, so a caller that swallowed the exception would not have half-paid.
        Assert.That(wallet.Balance, Is.EqualTo(39));
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Wallet_SpendRefusesANegative()
    {
        // A refund is not a purchase. The verb that raises a balance is Earn, and there is exactly
        // one of it — a Spend that accepted a negative would be a second, reachable from every
        // price in M6-02b's shop.
        EssenceWallet wallet = Banked(100);

        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Spend(-1));

        Assert.That(thrown.ParamName, Is.EqualTo("cost"));
        Assert.That(wallet.Balance, Is.EqualTo(100));
    }

    [Test]
    public void Wallet_SpendingEverythingIsLegal()
    {
        EssenceWallet wallet = Banked(40);

        Assert.DoesNotThrow(() => wallet.Spend(40));

        Assert.That(wallet.Balance, Is.Zero);
        Assert.That(_events.Single<EssenceChanged>().Delta, Is.EqualTo(-40));

        // And the other end of rule 1, on the other verb: spending nothing is not news either.
        _events.Clear();

        wallet.Spend(0);

        Assert.That(wallet.Balance, Is.Zero);
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Wallet_RestoreIsSilent()
    {
        // **LevelTracker.Restore and SkillRunner.Restore's shape** (rule 8): a resume is not news,
        // and an EssenceChanged published during RunSession.Start would reach a HUD that has not
        // subscribed yet.
        var wallet = new EssenceWallet(_events);

        Restore(wallet, 317);

        Assert.That(wallet.Balance, Is.EqualTo(317));
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Wallet_RefusesANullEvents()
    {
        Assert.Throws<ArgumentNullException>(() => new EssenceWallet(null));
    }

    [Test]
    public void Wallet_AllocatesNothing()
    {
        // Through SilentEvents rather than RecordingEvents, which boxes every payload into a
        // List<object> and would report the fake's allocation as the wallet's. The real hub fans
        // out through a typed channel per event type and does not box (ADR-0004).
        var wallet = new EssenceWallet(new SilentEvents());

        AllocationAssert.None(
            () =>
            {
                wallet.Earn(24);
                wallet.Spend(24);
            },
            iterations: 100_000);

        Assert.That(wallet.Balance, Is.Zero, "and it is still arithmetic, not just quiet.");
    }

    // ---- GD §15's authored table (rule 2) --------------------------------------------------------

    [Test]
    public void Essence_ForStageClearIsGdFifteen()
    {
        // The formula, in the one place it is written. Not on the wallet, which knows nothing about
        // depth, and not at the call site, which is where a second copy would start.
        EssenceSpec essence = Descent();

        Assert.That(essence.ForStageClear(1, false), Is.EqualTo(24), "20 + 4·1.");
        Assert.That(essence.ForStageClear(5, false), Is.EqualTo(40));
        Assert.That(essence.ForStageClear(10, false), Is.EqualTo(60));
        Assert.That(essence.ForStageClear(30, false), Is.EqualTo(140));

        // A boss stage *is* a stage clear, and pays both terms — which is why this is one method
        // rather than two, and why the boss term is +60 rather than 60.
        Assert.That(essence.ForStageClear(1, true), Is.EqualTo(84));
        Assert.That(essence.ForStageClear(5, true), Is.EqualTo(100));
        Assert.That(essence.ForStageClear(10, true), Is.EqualTo(120));
        Assert.That(essence.ForStageClear(30, true), Is.EqualTo(200));
    }

    [Test]
    public void Essence_RefusesANegativeNumber()
    {
        // Refused where the table is *authored* rather than where it is paid, for OverflowSpec's
        // reason: the symptom of a mode that charges for clearing a stage is a balance nobody can
        // explain, half an hour into a run and a long way from the asset. The message names the
        // field, so a designer reading the Console knows which of the four moved.
        AssertRefuses("perStageBase", () => new EssenceSpec(-1, PerStageDepth, PerElite, PerBoss));
        AssertRefuses("perStageDepth", () => new EssenceSpec(PerStageBase, -1, PerElite, PerBoss));
        AssertRefuses("perElite", () => new EssenceSpec(PerStageBase, PerStageDepth, -1, PerBoss));
        AssertRefuses("perBoss", () => new EssenceSpec(PerStageBase, PerStageDepth, PerElite, -1));

        // And the shape that is legal and looks like the four above: a mode that pays nothing.
        Assert.DoesNotThrow(() => new EssenceSpec(0, 0, 0, 0));
    }

    [Test]
    public void Essence_RefusesAStageBelowOne()
    {
        // Stages are numbered from 1 (GD §8.2), so a stage-zero payment is a hand-edited save
        // reaching the economy — and the honest answer to it is a throw rather than the base rate.
        EssenceSpec essence = Descent();

        Assert.Throws<ArgumentOutOfRangeException>(() => essence.ForStageClear(0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => essence.ForStageClear(-3, true));
    }

    [Test]
    public void Essence_DefaultPaysNothing()
    {
        // **Rule 2's stated cost.** `default(EssenceSpec)` never passed a constructor, so it is
        // legal content rather than a hole — which is what keeps sixty-three ModeSpec fixtures
        // compiling, and why a *shipped* mode is swept at author time instead
        // (ContentValidationTests.EveryShippedMode_PricesItsEssence).
        EssenceSpec none = default;

        Assert.That(none.ForStageClear(9, true), Is.Zero);
        Assert.That(none.PerStageBase, Is.Zero);
        Assert.That(none.PerStageDepth, Is.Zero);
        Assert.That(none.PerElite, Is.Zero);
        Assert.That(none.PerBoss, Is.Zero);
    }

    // ---- The award, at the one edge that makes it (rules 4, 5) -----------------------------------

    [Test]
    public void Stage_ClearPaysOnce()
    {
        BuildFlow(Descent());
        BeginAt(3);

        ClearTheStage();

        Assert.That(_wallet.Balance, Is.EqualTo(32), "20 + 4·3.");

        EssenceChanged paid = _events.Single<EssenceChanged>();

        Assert.That(paid.Delta, Is.EqualTo(32));
        Assert.That(paid.Balance, Is.EqualTo(32));

        // **EnterClear is an edge** (rule 4). The flow parks in Clear for ClearTime and then waits
        // in Gate for as long as the player likes, so three seconds of standing about is three
        // seconds in which a payment made from Tick rather than from Enter would be made again.
        for (int i = 0; i < 180; i++)
        {
            Step(1f / 60f);
        }

        Assert.That(_events.Count<EssenceChanged>(), Is.EqualTo(1));
        Assert.That(_wallet.Balance, Is.EqualTo(32));
    }

    [Test]
    public void Stage_ABossStagePaysTheBossTerm()
    {
        // A mode whose boss comes round every fifth stage, so stage 5 is one and stage 6 is not —
        // asked of the mode, because which stages hold a boss is the mode's authored statement
        // (M4-01b rule 1). No `stage % 5` is computed here either.
        BuildFlow(Descent(), bossEvery: 5);
        BeginAt(5);

        ClearTheBossStage();

        Assert.That(_wallet.Balance, Is.EqualTo(100), "20 + 4·5 + 60 — the stage *and* the boss.");
        Assert.That(_events.Single<EssenceChanged>().Delta, Is.EqualTo(100));

        _events.Clear();

        WalkThroughTheDoor();
        ClearTheStage();

        // And the boss term does not leak into the stage after it, which is the half a single
        // boss-stage row could not see.
        Assert.That(_flow.Stage, Is.EqualTo(6));
        Assert.That(_events.Single<EssenceChanged>().Delta, Is.EqualTo(44), "20 + 4·6, and no boss.");
        Assert.That(_wallet.Balance, Is.EqualTo(144));
    }

    [Test]
    public void Stage_ThePaymentPrecedesTheEvent()
    {
        // **Above the publish, and this is the whole of why** (rule 4): a reader handling
        // StageCleared sees a wallet that has already been paid, so a float-up and a readout react
        // to one tick rather than to two. Asserted through a subscriber rather than through publish
        // order, because publish order is the mechanism and this is the property.
        var watcher = new BalanceWatcher(_events);

        BuildFlow(Descent(), events: watcher);

        watcher.Wallet = _wallet;

        BeginAt(2);

        ClearTheStage();

        Assert.That(watcher.BalanceInsideStageCleared, Is.EqualTo(28),
            "A handler for StageCleared read a wallet that had not been paid yet.");
    }

    [Test]
    public void Stage_AModeThatPricesNothingPaysNothing()
    {
        // Every fixture in the suite that predates this task is this run: a mode with no Essence
        // block clears stages, is paid zero, and says nothing — no throw, and no event carrying a
        // zero delta for a reader to filter (rules 1 and 2).
        BuildFlow(default);
        BeginAt(4);

        Assert.DoesNotThrow(ClearTheStage);

        Assert.That(_wallet.Balance, Is.Zero);
        Assert.That(_events.Count<EssenceChanged>(), Is.Zero);
        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(1), "The stage still cleared.");
    }

    [Test]
    public void Stage_RefusesANullWallet()
    {
        // **Required, unlike the lures and the army beside it** (rule 5). Every run has a wallet, so
        // an optional argument defaulting to null would make a mis-wired run clear stages and be
        // paid nothing, with no throw and no log — the exact failure M5-06a's *As built* deviation 2
        // refused for MinionRecipe, and it costs eleven call sites across two files to refuse here.
        BuildFlow(Descent());

        var thrown = Assert.Throws<ArgumentNullException>(
            () => new StageFlow(
                _mode,
                _composer,
                _director,
                _enemies,
                _projectiles,
                _player,
                essence: null,
                new Ordeals(_mode, _events),
                new FixedRandom(0).Affixes,
                _events,
                _plan,
                seed: 0));

        Assert.That(thrown.ParamName, Is.EqualTo("essence"));
    }

    // ---- What the run exposes of it (rule 6) -----------------------------------------------------

    [Test]
    public void Run_TheWalletIsReadableAndNotReachable()
    {
        // **A scalar read, and the handle is not handed out** (AR §18.2). Earn and Spend are both
        // public on the wallet, so a public handle here would let a view pay itself for a stage it
        // did not clear — the shortest route to abuse of any seal on this class.
        RunSession session = Session(new RecordingIntents());

        session.Start(SessionConfig(1));

        Assert.That(session.State.Essence, Is.Zero, "A run opens with an empty wallet.");

        foreach (MemberInfo member in typeof(RunState).GetMembers(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
        {
            Type exposed = member switch
            {
                PropertyInfo property => property.PropertyType,
                FieldInfo field => field.FieldType,
                MethodInfo method => method.ReturnType,
                _ => null,
            };

            Assert.That(exposed, Is.Not.EqualTo(typeof(EssenceWallet)),
                $"RunState.{member.Name} hands out the wallet itself. The read is Essence; the "
                    + "handle stays internal (rule 6).");
        }
    }

    [Test]
    public void Run_ClearingStagesAccumulates()
    {
        // The whole of GD §15's income through a real run: the depth term is what makes the step
        // change at each stage, and a flat payment would read identically at stage 1 and differently
        // at stage 3. 24, then 52, then 84.
        RunSession session = Session(new RecordingIntents());

        session.Start(SessionConfig(1));

        foreach (int expected in new[] { 24, 52, 84 })
        {
            ClearOneStage(session);

            Assert.That(session.State.Essence, Is.EqualTo(expected),
                $"after clearing stage {session.State.StageIndex}.");

            CrossTheBoundary(session);
        }

        Assert.That(session.State.StageIndex, Is.EqualTo(4), "The fixture crossed three boundaries.");
    }

    // ---- Fixture: the wallet ---------------------------------------------------------------------

    /// <summary>A wallet holding <paramref name="amount"/>, with the payment already forgotten.</summary>
    private EssenceWallet Banked(int amount)
    {
        var wallet = new EssenceWallet(_events);

        wallet.Earn(amount);

        _events.Clear();

        return wallet;
    }

    /// <summary>
    /// <c>Restore</c> is internal; this assembly has no access, so it goes through reflection.
    /// </summary>
    /// <remarks>
    /// Reflection rather than <c>InternalsVisibleTo</c>, which AR §18.2 says
    /// <c>Soulvail.Tests.Core</c> does not have and never will — <c>LevelUpFlowTests</c>' route to
    /// <c>LevelUpFlow.GrantOverflow</c>, written the same way. The resume path itself lands with
    /// M6-01b; this is only so the silence can be asserted the day the method is written.
    /// </remarks>
    private static void Restore(EssenceWallet wallet, int balance)
    {
        MethodInfo method = typeof(EssenceWallet).GetMethod(
            "Restore",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null, "EssenceWallet.Restore has gone.");

        try
        {
            method.Invoke(wallet, new object[] { balance });
        }
        catch (TargetInvocationException e)
        {
            throw e.InnerException!;
        }
    }

    private static EssenceSpec Descent() =>
        new EssenceSpec(PerStageBase, PerStageDepth, PerElite, PerBoss);

    private static void AssertRefuses(string field, TestDelegate authoring)
    {
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(authoring);

        Assert.That(thrown.ParamName, Is.EqualTo(field));
    }

    /// <summary>
    /// Records what the wallet held at the moment <c>StageCleared</c> reached a subscriber.
    /// </summary>
    /// <remarks>
    /// <c>RecordingEvents</c> stores rather than dispatches, so a row about what a <em>handler</em>
    /// sees cannot be written with it alone. This forwards everything to one and reads the balance
    /// on the way past, which is the narrowest thing that answers the question.
    /// </remarks>
    private sealed class BalanceWatcher : IDomainEvents
    {
        private readonly IDomainEvents _inner;

        public BalanceWatcher(IDomainEvents inner)
        {
            _inner = inner;
        }

        /// <summary>The wallet to read. Assigned after the flow is built, which is when one exists.</summary>
        public EssenceWallet Wallet { get; set; }

        /// <summary>What it held inside the handler, or −1 if no <c>StageCleared</c> ever arrived.</summary>
        public int BalanceInsideStageCleared { get; private set; } = -1;

        public void Publish<T>(in T evt)
            where T : struct
        {
            if (evt is StageCleared && Wallet is not null)
            {
                BalanceInsideStageCleared = Wallet.Balance;
            }

            _inner.Publish(in evt);
        }
    }

    // ---- Fixture: a stage -------------------------------------------------------------------------

    /// <summary>Builds the run-sized world the <c>Stage_</c> rows drive.</summary>
    /// <param name="essence">What the mode pays. <c>default</c> is a mode with no economy.</param>
    /// <param name="bossEvery">How often a boss comes round, or 0 for a mode that has none.</param>
    /// <param name="events">
    /// Where everything is published. The fixture's recorder unless a row needs to watch a handler.
    /// </param>
    private void BuildFlow(EssenceSpec essence, int bossEvery = 0, IDomainEvents events = null)
    {
        IDomainEvents sink = events ?? _events;

        _mode = Mode(essence, bossEvery);
        _catalog = Catalog(_mode);
        _now = 0f;

        _enemies = new EnemySystem(
            _catalog,
            sink,
            new FixedRandom(0),
            new DepthScaling(_mode.Scaling),
            Capacity);

        _snapshot = new WorldSnapshot(Capacity)
        {
            HasGate = true,
            GatePosition = Door,
            SpawnPoints = Points(8),
        };

        _projectiles = new ProjectileSystem(sink, ProjectileCapacity);
        _player = new PlayerCombat(Oathbound(), sink, new RecordingIntents(), Capacity);
        _director = new SpawnDirector(_enemies, sink);
        _composer = new WaveComposer(_catalog, new ThreatBudget(_mode.Scaling, DeviceCap));
        _plan = new WavePlan(MaxWaves, Math.Max(1, _mode.Roster.Count));
        _spawn = new FixedRandom(0, Alternating(8_192)).Spawn;
        _wallet = new EssenceWallet(sink);

        _flow = new StageFlow(
            _mode,
            _composer,
            _director,
            _enemies,
            _projectiles,
            _player,
            _wallet,
            new Ordeals(_mode, sink),
            new FixedRandom(0).Affixes,
            sink,
            _plan,
            seed: 0);
    }

    /// <summary>Composes <paramref name="stage"/> and opens it — <c>RunSession.Start</c>'s two lines.</summary>
    private void BeginAt(int stage)
    {
        _composer.Compose(stage, _mode, _plan, _spawn);

        _enemies.Depth = stage;

        _flow.Begin(stage, _now);
    }

    /// <summary>One frame, in <c>RunSession.Tick</c>'s order.</summary>
    private void Step(float dt)
    {
        _now += dt;

        _snapshot.Dt = dt;

        _enemies.Ingest(_snapshot);

        _player.Tick(dt, _now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);

        _projectiles.Tick(_now, _snapshot.PlayerPosition, _player, _enemies);

        _director.Tick(_now, _snapshot.PlayerPosition, _spawn);

        _flow.Tick(_now, _snapshot, _spawn);
    }

    /// <summary>Takes a one-body stage from <c>Arrival</c> to <c>Clear</c>.</summary>
    private void ClearTheStage()
    {
        int before = _events.Count<EnemySpawned>();

        Step(2f);

        for (int i = 0; i < 40 && _events.Count<EnemySpawned>() == before; i++)
        {
            Step(SpawnDirector.SpawnInterval);
        }

        Assert.That(_events.Count<EnemySpawned>(), Is.GreaterThan(before),
            "The fixture failed to get the wave standing.");

        _enemies.ApplyDamage(_events.Of<EnemySpawned>()[before].Id, 10_000f, _now, _player);

        Step(1f / 60f);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Clear), "The fixture failed to clear the stage.");
    }

    /// <summary>
    /// Takes a boss stage from <c>Arrival</c> to <c>Clear</c>: one authored body and no waves.
    /// </summary>
    /// <remarks>
    /// The boss is cut down with <c>ApplyDamage</c> rather than fought, and the fixture never ticks
    /// the enemy behaviours — so there is no <c>WardenBehaviour</c> here and no need for one. What
    /// this row is about is which term the wallet was paid, not how the fight went.
    /// </remarks>
    private void ClearTheBossStage()
    {
        Step(2f);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Waves));

        // The director is ticked before the flow, so the body it was handed a boss id for stands up
        // on the frame after the one that handed it over.
        Step(1f / 60f);

        Assert.That(_director.IsBossStage, Is.True, "The mode did not name a boss for this stage.");
        Assert.That(_director.BossEnemyId, Is.Not.Zero, "The fixture failed to stand the boss up.");

        _enemies.ApplyDamage(_director.BossEnemyId, 100_000f, _now, _player);

        Step(1f / 60f);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Clear), "The fixture failed to kill the boss.");
    }

    /// <summary>From <c>Clear</c> all the way to the next stage's <c>Arrival</c>.</summary>
    private void WalkThroughTheDoor()
    {
        Step(StageFlow.ClearTime + (1f / 60f));

        // Out of the Sanctum, the one way there is (M6-02a rule 3).
        _flow.LeaveSanctum(_now);

        _snapshot.PlayerPosition = Door;

        Step(1f / 60f);
        Step(StageFlow.FadeTime + (1f / 60f));

        _snapshot.PlayerPosition = Vector3.Zero;

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Arrival),
            "The fixture failed to cross the boundary.");
    }

    // ---- Fixture: a run ---------------------------------------------------------------------------

    /// <summary>A whole run, for the two rows that are about what <c>RunState</c> says.</summary>
    private RunSession Session(RecordingIntents intents)
    {
        _mode = Mode(Descent(), bossEvery: 0);
        _catalog = Catalog(_mode);

        return new RunSession(
            _catalog,
            new FixedRandom(0, Alternating(8_192)),
            _events,
            intents,
            new RunRecorder(new FixedRandom(0, Alternating(8_192)), new FixedClock(default), _events),
            Capacity,
            DeviceCap,
            ProjectileCapacity);
    }

    private static RunConfig SessionConfig(int stage) => new RunConfig(
        new ContentId(ModeId),
        new ContentId(OathboundId),
        0,
        stage,
        new SpawnPlan(Array.Empty<SpawnPlan.Entry>()),
        restore: null);

    private static WorldSnapshot SessionSnapshot(float dt, Vector3 playerPosition) =>
        new WorldSnapshot(Capacity)
        {
            Dt = dt,
            PlayerPosition = playerPosition,
            HasGate = true,
            GatePosition = Door,
            SpawnPoints = Points(8),
        };

    /// <summary>
    /// Kills the stage's one body and ticks on until the flow has cleared it, with the player
    /// standing on the corpse and nowhere near the door.
    /// </summary>
    /// <remarks>
    /// The kill goes through <c>ReportConeHits</c>, which is the only route a core test has: a
    /// session's <c>PlayerCombat</c> and <c>EnemySystem</c> are both <c>internal</c> on
    /// <c>RunState</c> and this assembly has no <c>InternalsVisibleTo</c> (AR §18.2). So the player
    /// is walked onto the body, the Censer swings at what is under its nose, and the fixture answers
    /// the swing the way Unity would.
    /// </remarks>
    private void ClearOneStage(RunSession session)
    {
        int spawnedBefore = _events.Count<EnemySpawned>();
        int clearedBefore = _events.Count<StageCleared>();

        for (int i = 0; i < 600 && _events.Count<EnemySpawned>() == spawnedBefore; i++)
        {
            session.Tick(SessionSnapshot(1f / 60f, Vector3.Zero));
        }

        Assert.That(_events.Count<EnemySpawned>(), Is.GreaterThan(spawnedBefore),
            "The fixture failed to land the wave.");

        EnemySpawned spawned = _events.Of<EnemySpawned>()[spawnedBefore];
        int[] report = { spawned.Id };

        for (int i = 0; i < 600 && _events.Count<StageCleared>() == clearedBefore; i++)
        {
            session.Tick(SessionSnapshot(1f / 60f, spawned.Position));
            session.ReportConeHits(report);
        }

        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(clearedBefore + 1),
            "The fixture failed to clear the stage.");
    }

    /// <summary>Walks the run into the door and out the other side of the fade.</summary>
    private void CrossTheBoundary(RunSession session)
    {
        int before = _events.Count<StageArrived>();

        for (int i = 0; i < 600 && _events.Count<StageArrived>() == before; i++)
        {
            // Leaving the Sanctum the way a screen will (M6-02a rule 4).
            if (session.IsSanctumOpen)
            {
                session.LeaveSanctum();
            }

            session.Tick(SessionSnapshot(1f / 60f, Door));
        }

        Assert.That(_events.Count<StageArrived>(), Is.EqualTo(before + 1),
            "The fixture failed to cross the boundary.");
    }

    // ---- Fixture: content -------------------------------------------------------------------------

    /// <summary>
    /// A mode of exactly one Husk a stage, priced by <paramref name="essence"/>.
    /// </summary>
    /// <remarks>
    /// A flat budget and a single wave, so a stage's contents are stated rather than derived —
    /// <c>SpawnDirectorTests</c>' shape, which spells "always this many" by making each curve's step
    /// larger than any stage these rows reach.
    /// </remarks>
    private static ModeSpec Mode(EssenceSpec essence, int bossEvery)
    {
        var scaling = new ScalingSpec(
            new BudgetCurve(HuskCost, 0f, 0f),
            new WaveCurve(1, 1000, 1, 1),
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
            arenas: null,
            bossRoster: bossEvery == 0
                ? null
                : new[] { new BossRosterEntry(new ContentId(BossId), bossEvery) },
            overflow: default,
            essence: essence);
    }

    private static ContentCatalog Catalog(ModeSpec mode) => new ContentCatalog(
        new[] { Oathbound() },
        new[] { Husk(), WardenBody() },
        new[] { mode },
        skills: null,
        trees: null,
        bosses: new[] { Warden() });

    /// <summary>
    /// The Husk, authored <c>Static</c> on purpose: these rows are about payment, and a Chaser would
    /// walk into the player during the two seconds of arrival they tick through. Ten hit points, so
    /// one swing of the Censer ends it.
    /// </summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 10f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: HuskCost,
        xpValue: HuskCost * 3f,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    /// <summary>The body a boss stage stands up. Never ticked here — see <c>ClearTheBossStage</c>.</summary>
    private static EnemySpec WardenBody() => new EnemySpec(
        new ContentId(WardenEnemyId),
        new LocKey("enemy.warden.name"),
        maxHp: 1_000f,
        moveSpeed: 2f,
        targetPriority: 8,
        threatCost: 40,
        xpValue: 120f,
        isElite: false,
        contactDamage: 20f,
        reach: 2.5f,
        windupTime: 0.8f,
        recoverTime: 0.8f,
        aggroRange: 40f,
        behaviour: EnemyBehaviourKind.Boss);

    /// <summary>One phase and no summons: this fixture is about the award, not about the fight.</summary>
    private static BossSpec Warden() => new BossSpec(
        new ContentId(BossId),
        new ContentId(WardenEnemyId),
        new[] { new BossPhaseSpec(1f) },
        1.5f);

    /// <summary>The class every row plays. CC §7's numbers; no row here is about the player.</summary>
    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),

        // The Focus ramp is switched off with a maximum of 1, for RunSessionTests' reason: it would
        // put a modifier and a stream of events into fixtures measuring neither.
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));

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
