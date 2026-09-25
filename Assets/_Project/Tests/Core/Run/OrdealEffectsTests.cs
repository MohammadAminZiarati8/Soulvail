using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Core.Stage;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Progression;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Run;

/// <summary>
/// M6-06b: GD §13.4's four Ordeals, each turning one dial in one system, and the two it refuses.
/// </summary>
/// <remarks>
/// <para>
/// <b>One file for all four, grouped by Ordeal rather than by the class each one lands in</b> (the
/// spec's Files table). A reader asking "what does Swarm do" finds it in one block; the classes'
/// own fixtures stay about the classes. Every row builds its dealt set the way a resume does —
/// <c>Ordeals.Restore</c>, reached by reflection for <c>OrdealsTests</c>' reason — so a row names
/// exactly which Ordeals are in force rather than steering a schedule into dealing them.
/// </para>
/// <para>
/// <b>What this assembly cannot reach is in <c>Soulvail.Tests.Game</c></b>: the level-up screen
/// drawing two cards (<c>LevelUpPresenterTests.Vigil_TheScreenDrawsTwoCards</c>), and the asset and
/// prefab halves of the two refusals (<c>OrdealRefusalTests</c>).
/// </para>
/// </remarks>
[TestFixture]
public sealed class OrdealEffectsTests
{
    private const string HuskId = "enemy.husk";
    private const string SpitterId = "enemy.spitter";
    private const string OathboundId = "character.oathbound";
    private const string ModeId = "mode.test";

    private const int HuskCost = 4;
    private const int SpitterCost = 6;
    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int HighTierCap = 40;
    private const int Uncapped = 1000;
    private const int ProjectileCapacity = 8;
    private const float Ring = 10f;
    private const float Frame = 1f / 60f;
    private const float MaxHp = 140f;
    private const float WeaponDamage = 13f;
    private const float MoveSpeed = 3f;

    /// <summary>What every Pact in the flow rows' trees costs.</summary>
    private const float PactRot = 15f;

    private static readonly Vector3 Door = new Vector3(0f, 0f, 18f);

    private static readonly ContentId Famine = new ContentId("ordeal.famine");
    private static readonly ContentId Vigil = new ContentId("ordeal.vigil");
    private static readonly ContentId Swarm = new ContentId("ordeal.swarm");
    private static readonly ContentId Hunger = new ContentId("ordeal.hunger");

    private RecordingEvents _events;

    // The flow and meter fixture, PactOfferTests' shape.
    private PlayerCombat _combat;
    private LevelTracker _progression;
    private PlayerStats _stats;
    private EffectRegistry _registry;
    private SkillRunner _runner;

    // The stage fixture, OrdealsTests' shape.
    private ModeSpec _mode;
    private EnemySystem _enemies;
    private ProjectileSystem _projectiles;
    private PlayerCombat _player;
    private SpawnDirector _director;
    private WaveComposer _composer;
    private WavePlan _plan;
    private StageFlow _flow;
    private WorldSnapshot _snapshot;
    private IRandomStream _spawn;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();

        _combat = new PlayerCombat(Character(), _events, new RecordingIntents(), Capacity);
        _progression = new LevelTracker(Scalings.Xp(), _events);

        _stats = new PlayerStats(
            _combat,
            new PlayerMotor(new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f), Vector3.UnitZ),
            _progression);

        _registry = new EffectRegistry();
        _registry.Register<ModifyStat>(new ModifyStatHandler(_stats));

        _runner = new SkillRunner(_registry, _combat.Blackboard, _events);
    }

    // ---- Famine (rule 1) -------------------------------------------------------------------------

    [Test]
    public void Famine_TakesFortyPercent()
    {
        BuildFlow(StageMode(FourOrdeals()), Famine);
        BeginAt(10);

        ClearTheStage();

        // GD §15's 20 + 4·10 is 60, and 0.6 of it is 36 — one event, carrying exactly that.
        EssenceChanged paid = _events.Single<EssenceChanged>();

        Assert.That(paid.Delta, Is.EqualTo(36));
        Assert.That(paid.Balance, Is.EqualTo(36));
    }

    [Test]
    public void Famine_RoundsToTheNearestEssence()
    {
        BuildFlow(StageMode(FourOrdeals()), Famine);
        BeginAt(1);

        ClearTheStage();

        // 24 × 0.6 is 14.4: a whole Essence, the nearer one, and not the ceiling.
        Assert.That(_events.Single<EssenceChanged>().Delta, Is.EqualTo(14));
    }

    [Test]
    public void Famine_NeverPaysZero()
    {
        // Two Famines at a tenth each: 24 × 0.01 is 0.24, which rounds to nothing.
        OrdealSpec[] stacked =
        {
            Ordeal("ordeal.famine", essence: 0.1f),
            Ordeal("ordeal.dearth", essence: 0.1f),
        };

        BuildFlow(StageMode(stacked), Famine, new ContentId("ordeal.dearth"));
        BeginAt(1);

        ClearTheStage();

        // Floored at one, and published — a clear the HUD never heard about would read as a stage
        // that was never cleared (EssenceWallet.Earn is silent for zero).
        EssenceChanged paid = _events.Single<EssenceChanged>();

        Assert.That(paid.Delta, Is.EqualTo(1));
    }

    [Test]
    public void Famine_LeavesTheFormulaAlone()
    {
        BuildFlow(StageMode(FourOrdeals()), Famine);
        BeginAt(10);

        ClearTheStage();

        // The multiplier is at the award, never in the spec: the formula still answers 60.
        Assert.That(_mode.Essence.ForStageClear(10, bossStage: false), Is.EqualTo(60));
    }

    [Test]
    public void Famine_IsInertBeforeItIsDealt()
    {
        // Famine in the pool and not dealt — the first twenty-four stages of every Descent run.
        BuildFlow(StageMode(FourOrdeals()));
        BeginAt(3);

        ClearTheStage();

        Assert.That(_events.Single<EssenceChanged>().Delta, Is.EqualTo(32), "20 + 4·3, as M6-01a left it.");
    }

    // ---- Vigil (rule 2) --------------------------------------------------------------------------

    [Test]
    public void Vigil_OffersTwo()
    {
        LevelUpFlow flow = Flow(Wide(4, pacted: false), Dealt(Vigil));
        BankPicks(1);

        flow.Open(new FixedRandom(3).Offers);

        OfferPresented presented = _events.Single<OfferPresented>();

        Assert.That(presented.Count, Is.EqualTo(2), "a tree of twelve, under Vigil.");
        Assert.That(flow.Offer.Count, Is.EqualTo(2));
        Assert.That(flow.Offer.Distinct().Count(), Is.EqualTo(2), "two ids, not one twice.");
    }

    [Test]
    public void Vigil_StillRollsItsPact()
    {
        LevelUpFlow flow = Flow(Wide(4, pacted: true), Dealt(Vigil));
        BankPicks(1);

        var offers = new CountingStream(new FixedRandom(0.1f, 0.4f, 0.9f, 0f).Offers);

        flow.Open(offers);

        // M6-05b rule 10: a Vigil offer is two picks and the same two roll draws.
        Assert.That(offers.Draws, Is.EqualTo(4));
        Assert.That(flow.PactIndex, Is.InRange(-1, 1));
        Assert.That(_events.Single<OfferPresented>().PactIndex, Is.EqualTo(flow.PactIndex));
    }

    [Test]
    public void Vigil_ClampsToWhatIsAvailable()
    {
        SkillTree tree = Wide(1, pacted: false);
        tree.Take(new ContentId("skill.a0"));
        tree.Take(new ContentId("skill.b0"));

        LevelUpFlow flow = Flow(tree, Dealt(Vigil));
        BankPicks(1);

        Assert.DoesNotThrow(() => flow.Open(new FixedRandom(3).Offers));

        Assert.That(flow.Offer.Count, Is.EqualTo(1));
        Assert.That(_events.Single<OfferPresented>().Count, Is.EqualTo(1));
    }

    // ---- Swarm (rules 3 and 4) -------------------------------------------------------------------

    [Test]
    public void Swarm_RaisesTheCap()
    {
        ModeSpec mode = ComposerMode(Scalings.Design(), FourOrdeals(), Roster(HuskId, SpitterId));

        // C(25) = min(10 + 12, 28) = 22; Swarm adds eight and the device keeps it at 28.
        Assert.That(Compose(25, mode, DeviceCap, Dealt(mode, Swarm)).Concurrency, Is.EqualTo(28));
        Assert.That(Compose(25, mode, DeviceCap, ordeals: null).Concurrency, Is.EqualTo(22));
    }

    [Test]
    public void Swarm_StopsGivingBodiesWhenTheDeviceCapBinds()
    {
        ModeSpec mode = ComposerMode(Scalings.Design(), FourOrdeals(), Roster(HuskId, SpitterId));

        // C(36) = min(10 + 18, 28) = 28 already, so "device permitting" says no: GD §11.2's rule
        // working, not the Ordeal failing — and the report a playtest would file, answered here.
        Assert.That(Compose(36, mode, DeviceCap, Dealt(mode, Swarm)).Concurrency, Is.EqualTo(28));
        Assert.That(Compose(36, mode, DeviceCap, ordeals: null).Concurrency, Is.EqualTo(28));
    }

    [Test]
    public void Swarm_HalvesTheNamedArchetype()
    {
        // One wave, no cap in reach, a budget of 24: the body count is the budget over the cost.
        ModeSpec husks = ComposerMode(Flat(24f), FourOrdeals(), Roster(HuskId));
        ModeSpec spitters = ComposerMode(Flat(24f), FourOrdeals(), Roster(SpitterId));

        Assert.That(Compose(1, husks, Uncapped, ordeals: null).BodyCount(1), Is.EqualTo(6), "24 / 4.");
        Assert.That(Compose(1, husks, Uncapped, Dealt(husks, Swarm)).BodyCount(1), Is.EqualTo(12), "24 / 2.");

        Assert.That(Compose(1, spitters, Uncapped, ordeals: null).BodyCount(1), Is.EqualTo(4));
        Assert.That(
            Compose(1, spitters, Uncapped, Dealt(spitters, Swarm)).BodyCount(1),
            Is.EqualTo(4),
            "Swarm names the Husk; a Spitter costs what it always did.");
    }

    [Test]
    public void Swarm_BuysMoreOfTheCheapOne()
    {
        ModeSpec mode = ComposerMode(Flat(200f), FourOrdeals(), Roster(HuskId, SpitterId));

        // Stage 2, the first at which both are eligible — it opens with the Spitter's introduction.
        WavePlan without = Compose(2, mode, Uncapped, ordeals: null);
        WavePlan with = Compose(2, mode, Uncapped, Dealt(mode, Swarm));

        Assert.That(Count(with, HuskId), Is.GreaterThan(Count(without, HuskId)));

        // The stage's threat is the budget's, not Swarm's: every point is bought or left unspent,
        // each at the price it had in that composition.
        Assert.That(Threat(without, HuskCost) + without.UnspentThreat, Is.EqualTo(200f).Within(1e-3f));
        Assert.That(Threat(with, HuskCost / 2) + with.UnspentThreat, Is.EqualTo(200f).Within(1e-3f));
    }

    [Test]
    public void Swarm_ACostNeverFallsBelowOne()
    {
        OrdealSpec[] pool = { Ordeal("ordeal.plague", threatCostTarget: new ContentId(HuskId), threatCostMultiplier: 0.01f) };
        ModeSpec mode = ComposerMode(Flat(20f), pool, Roster(HuskId));

        // 4 × 0.01 rounds to 0 and is floored at 1. That this line returns at all is half the row:
        // a free Husk never lowers the allowance, and the walk would end only at the cap.
        WavePlan plan = Compose(1, mode, Uncapped, Dealt(mode, new ContentId("ordeal.plague")));

        Assert.That(plan.BodyCount(1), Is.EqualTo(20), "twenty Husks at one threat each.");
        Assert.That(plan.UnspentThreat, Is.Zero, "and the budget is spent.");
    }

    [Test]
    public void Swarm_IsInertBeforeItIsDealt()
    {
        ModeSpec mode = ComposerMode(Scalings.Design(), FourOrdeals(), Roster(HuskId, SpitterId));

        // A set holding Swarm in its pool and nothing dealt, against no set at all.
        string held = Describe(Compose(10, mode, DeviceCap, new Ordeals(mode, _events)));
        string none = Describe(Compose(10, mode, DeviceCap, ordeals: null));

        Assert.That(held, Is.EqualTo(none));
    }

    // ---- Hunger (rule 6) -------------------------------------------------------------------------

    [Test]
    public void Hunger_RaisesAGain()
    {
        Veilrot meter = Meter(Dealt(Hunger));

        meter.Gain(10f);

        Assert.That(meter.Value, Is.EqualTo(15f).Within(1e-5f));
        Assert.That(_events.Single<VeilrotChanged>().Delta, Is.EqualTo(15f).Within(1e-5f));
    }

    [Test]
    public void Hunger_TheClampStillWins()
    {
        Veilrot meter = Meter(Dealt(Hunger));
        RestoreMeter(meter, 95f);

        meter.Gain(10f);

        Assert.That(meter.Value, Is.EqualTo(Veilrot.Max), "95 + 15 clamps at 100.");
        Assert.That(meter.IsClaimed, Is.True);
        Assert.That(_events.Count<ClaimingBegan>(), Is.EqualTo(1));
    }

    [Test]
    public void Hunger_DoesNotDiscountACleanse()
    {
        Veilrot meter = Meter(Dealt(Hunger));
        RestoreMeter(meter, 40f);

        meter.Cleanse(15f);

        Assert.That(meter.Value, Is.EqualTo(25f).Within(1e-5f), "a cleanse is not a gain.");
    }

    [Test]
    public void Hunger_IsSilentForNothing()
    {
        Veilrot meter = Meter(Dealt(Hunger));

        meter.Gain(0f);
        meter.Gain(-5f);
        meter.Gain(float.NaN);

        Assert.That(meter.Value, Is.Zero);
        Assert.That(_events.Count<VeilrotChanged>(), Is.Zero, "M6-04 rule 1 survives the multiplier.");
    }

    // ---- The run (rules 5 and 7) -----------------------------------------------------------------

    [Test]
    public void Run_TheSetReachesAllThree()
    {
        RunSession session = Session(RunMode(), HighTierCap);
        session.Start(Resumed(30, level: 2, pending: 1, Hunger));

        Ordeals held = Held(session.State);

        Assert.That(held, Is.Not.Null);
        Assert.That(Field<Ordeals>(Internal<LevelUpFlow>(session.State, "LevelUp"), "_ordeals"), Is.SameAs(held), "the flow.");
        Assert.That(Field<Ordeals>(Internal<Veilrot>(session.State, "Rot"), "_ordeals"), Is.SameAs(held), "the meter.");

        // The composer holds none — Compose takes it — so the flow that calls it is what is checked,
        // and Run_AResumedStageIsComposedUnderItsOrdeals checks the call RunSession makes itself.
        StageFlow flow = Field<StageFlow>(session, "_flow");

        Assert.That(Field<Ordeals>(flow, "_ordeals"), Is.SameAs(held), "the stage flow that composes.");
    }

    [Test]
    public void Run_AResumedStageIsComposedUnderItsOrdeals()
    {
        // The opening composition runs before anything else in Start, so the set has to be restored
        // above it: a resumed stage-25 run holding Swarm opens on a stage composed with it.
        RunSession withSwarm = Session(RunMode(), HighTierCap);
        withSwarm.Start(Resumed(25, level: 1, pending: 0, Swarm));

        RunSession without = Session(RunMode(), HighTierCap);
        without.Start(Resumed(25, level: 1, pending: 0));

        Assert.That(Plan(without).Concurrency, Is.EqualTo(22), "C(25) on a high-tier phone.");
        Assert.That(Plan(withSwarm).Concurrency, Is.EqualTo(30), "and Swarm's eight on top.");
    }

    [Test]
    public void Run_ANullSetMeansNoOrdeals()
    {
        LevelUpFlow flow = Flow(Wide(4, pacted: false), ordeals: null);
        BankPicks(1);
        flow.Open(new FixedRandom(3).Offers);

        Veilrot meter = Meter(ordeals: null);
        meter.Gain(10f);

        ModeSpec mode = ComposerMode(Scalings.Design(), FourOrdeals(), Roster(HuskId, SpitterId));
        WavePlan plan = Compose(25, mode, DeviceCap, ordeals: null);

        Assert.That(flow.Offer.Count, Is.EqualTo(3), "three cards.");
        Assert.That(meter.Value, Is.EqualTo(10f), "an unmultiplied gain.");
        Assert.That(plan.Concurrency, Is.EqualTo(22), "an unchanged cap.");
    }

    [Test]
    public void Run_FourAtOnce()
    {
        RunSession session = Session(RunMode(), HighTierCap);
        session.Start(Resumed(55, level: 2, pending: 1, Famine, Vigil, Swarm, Hunger));

        // Swarm: C(55) = min(10 + 27, 40) = 37, and +8 is held to the high tier's 40.
        Assert.That(Plan(session).Concurrency, Is.EqualTo(HighTierCap));

        // Vigil: two cards.
        session.OpenLevelUp();

        Assert.That(session.State.Offer.Count, Is.EqualTo(2));

        session.ChooseOffer(0);

        // Hunger: half again.
        Veilrot meter = Internal<Veilrot>(session.State, "Rot");
        meter.Gain(10f);

        Assert.That(meter.Value, Is.EqualTo(15f).Within(1e-5f));

        // Famine: 20 + 4·55 = 240, and 0.6 of it.
        _events.Clear();
        ClearOneStage(session);

        Assert.That(_events.Single<EssenceChanged>().Delta, Is.EqualTo(144));
    }

    // ---- Echo's refusal, pinned ------------------------------------------------------------------

    [Test]
    public void Waves_TheFourthIsNotACopy()
    {
        // GD §12's budget and wave curves with no cap in reach, so each wave spends its own share.
        var scaling = new ScalingSpec(
            new BudgetCurve(40f, 12f, 0.9f),
            new WaveCurve(2, 5, 2, 5),
            new ConcurrencyCurve(Uncapped, Uncapped),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0));

        ModeSpec mode = ComposerMode(scaling, FourOrdeals(), Roster(HuskId));
        Ordeals all = Dealt(mode, Famine, Vigil, Swarm, Hunger);
        const int cost = HuskCost / 2;

        for (int stage = 25; stage <= 45; stage += 5)
        {
            WavePlan plan = Compose(stage, mode, Uncapped, all);
            float budget = scaling.Budget.At(stage);

            Assert.That(plan.WaveCount, Is.EqualTo(5), $"W({stage}).");

            // Wave 4 is worth 4 shares of 15, to within one body — never wave 3's 3 shares again.
            Assert.That(
                plan.BodyCount(4) * cost,
                Is.EqualTo(budget * 4f / 15f).Within(cost),
                $"wave 4 of stage {stage} spent something other than its own share.");

            Assert.That(plan.BodyCount(4), Is.GreaterThan(plan.BodyCount(3)), $"stage {stage}'s wave 4 is wave 3 again.");
        }
    }

    // ---- Allocation (rule 8) ---------------------------------------------------------------------

    [Test]
    public void Effects_AllocateNothing()
    {
        var silent = new SilentEvents();

        // The award: StageFlow's own method, as a delegate made once so the call is not reflection.
        BuildFlow(StageMode(FourOrdeals()), Famine, Vigil, Swarm, Hunger);

        MethodInfo underOrdeals = typeof(StageFlow).GetMethod("UnderOrdeals", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(underOrdeals, Is.Not.Null, "StageFlow.UnderOrdeals has gone.");

        var award = (Func<int, int>)Delegate.CreateDelegate(typeof(Func<int, int>), _flow, underOrdeals);

        // The gain, on a meter that never reaches a threshold: up and back down each time.
        ModeSpec composed = ComposerMode(Flat(40f), FourOrdeals(), Roster(HuskId, SpitterId));
        Ordeals all = Dealt(composed, Famine, Vigil, Swarm, Hunger);

        var combat = new PlayerCombat(Character(), silent, new RecordingIntents(), Capacity);
        var stats = new PlayerStats(
            combat,
            new PlayerMotor(new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f), Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), silent));
        var meter = new Veilrot(stats, combat, combat.Blackboard, silent, all);

        // The composition, into one plan.
        var composer = new WaveComposer(Catalog(composed), new ThreatBudget(composed.Scaling, DeviceCap));
        var plan = new WavePlan(composed.Scaling.Waves.Max, composed.Roster.Count);
        IRandomStream spawn = new FixedRandom(Alternating(64)).Spawn;
        int sink = 0;

        AllocationAssert.None(
            () =>
            {
                sink += award(60);
                meter.Gain(1f);
                meter.Cleanse(1.5f);
                composer.Compose(2, composed, plan, spawn, all);
            },
            iterations: 100_000);

        Assert.That(sink, Is.GreaterThan(0));
    }

    // ---- Fixture: content ------------------------------------------------------------------------

    private static OrdealSpec Ordeal(
        string id,
        float essence = 1f,
        int offers = 0,
        int concurrency = 0,
        float veilrot = 1f,
        ContentId threatCostTarget = default,
        float threatCostMultiplier = 1f) => new OrdealSpec(
        new ContentId(id),
        new LocKey(id + ".name"),
        new LocKey(id + ".description"),
        essence,
        offers,
        concurrency,
        veilrot,
        threatCostTarget,
        threatCostMultiplier);

    /// <summary>The four this build ships, with GD §13.4's numbers — <c>Descent.asset</c>'s pool.</summary>
    private static OrdealSpec[] FourOrdeals() => new[]
    {
        Ordeal("ordeal.famine", essence: 0.6f),
        Ordeal("ordeal.vigil", offers: 2),
        Ordeal("ordeal.swarm", concurrency: 8, threatCostTarget: new ContentId(HuskId), threatCostMultiplier: 0.5f),
        Ordeal("ordeal.hunger", veilrot: 1.5f),
    };

    /// <summary>
    /// <paramref name="ids"/> introduced at stages 1, 2, 3… — GD §8.2 admits one new archetype a
    /// stage, so a row wanting both composes from stage 2.
    /// </summary>
    private static RosterEntry[] Roster(params string[] ids) =>
        ids.Select((id, i) => new RosterEntry(new ContentId(id), i + 1)).ToArray();

    /// <summary>One wave of a fixed budget, with a cap nothing reaches unless the row passes one.</summary>
    private static ScalingSpec Flat(float budget) => new ScalingSpec(
        new BudgetCurve(budget, 0f, 0f),
        new WaveCurve(1, 1000, 1, 1),
        new ConcurrencyCurve(Uncapped, Uncapped),
        new StatCurve(0.06f, 4f, 1, 1),
        new StatCurve(0.035f, 3f, 1, 1),
        new StatCurve(0.02f, 1.3f, 5, 0));

    /// <summary>A mode for the composer rows: the curves and roster given, the pool given.</summary>
    private static ModeSpec ComposerMode(ScalingSpec scaling, OrdealSpec[] pool, RosterEntry[] roster) =>
        new ModeSpec(
            new ContentId(ModeId),
            new LocKey("mode.test.name"),
            startingStage: 1,
            isEndless: true,
            finalStage: 0,
            scaling,
            Scalings.Xp(),
            roster,
            ordeals: pool);

    /// <summary>One Husk a stage and GD §15's income — EssenceWalletTests' stage, with a pool.</summary>
    private static ModeSpec StageMode(OrdealSpec[] pool) => new ModeSpec(
        new ContentId(ModeId),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        new ScalingSpec(
            new BudgetCurve(HuskCost, 0f, 0f),
            new WaveCurve(1, 1000, 1, 1),
            new ConcurrencyCurve(DeviceCap, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0)),
        Scalings.Xp(),
        Roster(HuskId),
        essence: new EssenceSpec(20, 4, 15, 60),
        ordeals: pool);

    /// <summary>
    /// The run rows' mode: one Husk a stage, GD §12.2's concurrency curve, GD §15's income and the
    /// four Ordeals.
    /// </summary>
    private static ModeSpec RunMode() => new ModeSpec(
        new ContentId(ModeId),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        new ScalingSpec(
            new BudgetCurve(HuskCost, 0f, 0f),
            new WaveCurve(1, 1000, 1, 1),
            new ConcurrencyCurve(10, 2),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0)),
        Scalings.Xp(),
        Roster(HuskId),
        essence: new EssenceSpec(20, 4, 15, 60),
        ordeals: FourOrdeals());

    private static ContentCatalog Catalog(ModeSpec mode) => new ContentCatalog(
        new[] { Character() },
        new[] { Enemy(HuskId, HuskCost), Enemy(SpitterId, SpitterCost) },
        new[] { mode },
        TreeRulesTests.FullSkills(),
        new[] { TreeRulesTests.FullTree() });

    /// <summary>Static, ten hit points: EssenceWalletTests' Husk, for its reasons.</summary>
    private static EnemySpec Enemy(string id, int cost) => new EnemySpec(
        new ContentId(id),
        new LocKey(id + ".name"),
        maxHp: 10f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: cost,
        xpValue: cost * 3f,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    private static CharacterSpec Character() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        MaxHp,
        new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, WeaponDamage, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));

    // ---- Fixture: a dealt set --------------------------------------------------------------------

    /// <summary>A set over the four, holding <paramref name="ids"/>.</summary>
    private Ordeals Dealt(params ContentId[] ids) => Dealt(ComposerMode(Flat(40f), FourOrdeals(), Roster(HuskId)), ids);

    /// <summary>A set over <paramref name="mode"/>'s pool, holding <paramref name="ids"/> — the way a resume does.</summary>
    private Ordeals Dealt(ModeSpec mode, params ContentId[] ids)
    {
        var ordeals = new Ordeals(mode, _events);

        MethodInfo restore = typeof(Ordeals).GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(restore, Is.Not.Null, "Ordeals.Restore has gone.");

        restore.Invoke(ordeals, new object[] { ids });

        Assert.That(ordeals.Applied.Count, Is.EqualTo(ids.Length), "the fixture dealt something the pool does not hold.");

        return ordeals;
    }

    // ---- Fixture: the composer -------------------------------------------------------------------

    private static WavePlan Compose(int stage, ModeSpec mode, int deviceCap, Ordeals ordeals)
    {
        var composer = new WaveComposer(Catalog(mode), new ThreatBudget(mode.Scaling, deviceCap));
        var plan = new WavePlan(mode.Scaling.Waves.Max, mode.Roster.Count);

        composer.Compose(stage, mode, plan, new FixedRandom(Alternating(8_192)).Spawn, ordeals);

        return plan;
    }

    private static int Count(WavePlan plan, string id)
    {
        int count = 0;

        for (int wave = 1; wave <= plan.WaveCount; wave++)
        {
            for (int i = 0; i < plan.EntryCount(wave); i++)
            {
                WaveEntry entry = plan.Entry(wave, i);

                if (entry.SpecId.Value == id)
                {
                    count += entry.Count;
                }
            }
        }

        return count;
    }

    /// <summary>What <paramref name="plan"/> bought, with a Husk at <paramref name="huskCost"/>.</summary>
    private static float Threat(WavePlan plan, int huskCost) =>
        (Count(plan, HuskId) * huskCost) + (Count(plan, SpitterId) * SpitterCost);

    /// <summary>Every number a reader can see on <paramref name="plan"/>, in one string.</summary>
    private static string Describe(WavePlan plan)
    {
        var text = new System.Text.StringBuilder();

        text.Append($"stage {plan.Stage} waves {plan.WaveCount} cap {plan.Concurrency} unspent {plan.UnspentThreat:R};");

        for (int wave = 1; wave <= plan.WaveCount; wave++)
        {
            for (int i = 0; i < plan.EntryCount(wave); i++)
            {
                WaveEntry entry = plan.Entry(wave, i);

                text.Append($" w{wave}:{entry.SpecId.Value}×{entry.Count}");
            }
        }

        return text.ToString();
    }

    // ---- Fixture: the flow and the meter ---------------------------------------------------------

    private LevelUpFlow Flow(SkillTree tree, Ordeals ordeals) => new LevelUpFlow(
        tree,
        _progression,
        _runner,
        _registry,
        _events,
        new OverflowSpec(0.02f, 0.02f),
        new Veilrot(_stats, _combat, _combat.Blackboard, _events),
        ordeals);

    private Veilrot Meter(Ordeals ordeals) =>
        new Veilrot(_stats, _combat, _combat.Blackboard, _events, ordeals);

    /// <summary><c>Veilrot.Restore</c> is internal; silent, so the row starts from a number and not a history.</summary>
    private static void RestoreMeter(Veilrot meter, float value)
    {
        MethodInfo restore = typeof(Veilrot).GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(restore, Is.Not.Null, "Veilrot.Restore has gone.");

        restore.Invoke(meter, new object[] { value, false });
    }

    /// <summary>Three branches of one tier, <paramref name="perBranch"/> Passives each — PactOfferTests' tree.</summary>
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

                skills.Add(new SkillSpec(
                    new ContentId(tier[i]),
                    new LocKey($"{tier[i]}.name"),
                    new LocKey($"{tier[i]}.desc"),
                    SkillKind.Passive,
                    new IEffect[] { TreeRulesTests.Damage(0.15f) },
                    pact: pacted
                        ? new PactSpec(new IEffect[] { TreeRulesTests.Damage(0.45f) }, PactRot, new LocKey($"{tier[i]}.pact"))
                        : null));
            }

            branches.Add(TreeRulesTests.Branch(letter, tier));
        }

        SkillTreeSpec spec = TreeRulesTests.Tree(branches.ToArray());

        return new SkillTree(new TreeRules(spec, TreeRulesTests.Catalog(spec, skills)), _registry, _events);
    }

    /// <summary>Banks exactly <paramref name="count"/> picks, a unit of XP at a time.</summary>
    private void BankPicks(int count)
    {
        while (_progression.PendingLevelUps < count)
        {
            _progression.Grant(1f);
        }

        Assert.That(_progression.PendingLevelUps, Is.EqualTo(count), "the fixture overshot.");

        _events.Clear();
    }

    // ---- Fixture: a stage ------------------------------------------------------------------------

    private void BuildFlow(ModeSpec mode, params ContentId[] dealt)
    {
        _mode = mode;
        _now = 0f;

        var random = new FixedRandom(0, Alternating(8_192));
        ContentCatalog catalog = Catalog(mode);

        _enemies = new EnemySystem(catalog, _events, random, new DepthScaling(mode.Scaling), Capacity);

        _snapshot = new WorldSnapshot(Capacity)
        {
            HasGate = true,
            GatePosition = Door,
            SpawnPoints = Points(8),
        };

        _projectiles = new ProjectileSystem(_events, ProjectileCapacity);
        _player = new PlayerCombat(Character(), _events, new RecordingIntents(), Capacity);
        _director = new SpawnDirector(_enemies, _events);
        _composer = new WaveComposer(catalog, new ThreatBudget(mode.Scaling, DeviceCap));
        _plan = new WavePlan(mode.Scaling.Waves.Max, mode.Roster.Count);
        _spawn = random.Spawn;

        _flow = new StageFlow(
            _mode,
            _composer,
            _director,
            _enemies,
            _projectiles,
            _player,
            new EssenceWallet(_events),
            Dealt(mode, dealt),
            random.Affixes,
            _events,
            _plan,
            seed: 0);

        _events.Clear();
    }

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

    private void ClearTheStage()
    {
        int before = _events.Count<EnemySpawned>();

        Step(2f);

        for (int i = 0; i < 40 && _events.Count<EnemySpawned>() == before; i++)
        {
            Step(SpawnDirector.SpawnInterval);
        }

        Assert.That(_events.Count<EnemySpawned>(), Is.GreaterThan(before), "The fixture failed to get the wave standing.");

        _enemies.ApplyDamage(_events.Of<EnemySpawned>()[before].Id, 100_000f, _now, _player);

        Step(Frame);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Clear), "The fixture failed to clear the stage.");
    }

    // ---- Fixture: a run --------------------------------------------------------------------------

    private RunSession Session(ModeSpec mode, int deviceCap) => new RunSession(
        Catalog(mode),
        new FixedRandom(0, Alternating(8_192)),
        _events,
        new RecordingIntents(),
        new RunRecorder(new FixedRandom(0, Alternating(8_192)), new FixedClock(default), _events),
        Capacity,
        deviceCap,
        ProjectileCapacity);

    /// <summary>
    /// A resume at <paramref name="stage"/>, at <paramref name="level"/> with <paramref name="pending"/>
    /// picks unspent and no node taken, holding <paramref name="ordealIds"/>.
    /// </summary>
    private static RunConfig Resumed(int stage, int level, int pending, params ContentId[] ordealIds) => new RunConfig(
        new ContentId(ModeId),
        new ContentId(OathboundId),
        0,
        stage,
        new SpawnPlan(Array.Empty<SpawnPlan.Entry>()),
        new RunSnapshot(
            RunSnapshot.CurrentVersion,
            new ContentId(ModeId),
            new ContentId(OathboundId),
            0,
            stage,
            default,
            playerHp: 100f,
            playerShield: 0f,
            runTime: 600f,
            writtenAt: default,
            level: level,
            xp: 0f,
            pendingLevelUps: pending,
            Array.Empty<ContentId>(),
            new ContentId[SkillRunner.MaxManualSlots],
            default,
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>(),
            ordealIds));

    /// <summary>OrdealsTests' route, body by body — Swarm makes it two Husks.</summary>
    private void ClearOneStage(RunSession session) => OrdealsTests.ClearOneStage(session, _events);

    private static WavePlan Plan(RunSession session) =>
        Field<WavePlan>(Field<StageFlow>(session, "_flow"), "_plan");

    /// <summary>The run's set, which <c>RunState</c> keeps internal.</summary>
    private static Ordeals Held(RunState state) => Internal<Ordeals>(state, "Ordeals");

    private static T Internal<T>(RunState state, string property)
        where T : class
    {
        PropertyInfo info = typeof(RunState).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(info, Is.Not.Null, $"RunState.{property} has gone.");

        return (T)info.GetValue(state);
    }

    private static T Field<T>(object owner, string name)
        where T : class
    {
        FieldInfo info = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(info, Is.Not.Null, $"{owner.GetType().Name}.{name} has gone.");

        return (T)info.GetValue(owner);
    }

    // ---- Small shared machinery ------------------------------------------------------------------

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

    private static float[] Alternating(int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = i % 2 == 0 ? 0.1f : 0.9f;
        }

        return values;
    }

    /// <summary>Counts every draw it forwards.</summary>
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
}
