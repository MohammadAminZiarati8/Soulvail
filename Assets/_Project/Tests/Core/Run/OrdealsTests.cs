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
/// GD §13.4's Ordeals, from the authored block up: the spec's guards, the schedule, the draw, the
/// stacking rules, the restore, and the one line in <see cref="StageFlow"/> that deals one.
/// </summary>
/// <remarks>
/// <para>
/// <b>One file for the mechanism and the numbers it reads</b> (M6-06a's Files table): the spec is
/// five dials with no behaviour, and splitting them from the schedule's rules would put the two in
/// different places. The <c>Spec_</c>, <c>Schedule_</c>, <c>Deal_</c> and <c>Stack_</c> rows are
/// objects with no world around them; <c>Deal_DrawsOnAffixesAndNothingElse</c> and
/// <c>Deal_HappensBeforeTheComposition</c> drive a real <see cref="StageFlow"/> across boundaries,
/// <c>EssenceWalletTests</c>' flow fixture; and the <c>Restore_</c>, <c>Recorder_</c> and
/// <c>State_</c> rows that name a resume or a save drive a whole <see cref="RunSession"/>.
/// </para>
/// <para>
/// <b><c>Restore</c> is reached by reflection</b>, for <c>EssenceWalletTests</c>' reason: it is
/// <c>internal</c>, and <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c> (AR §18.2).
/// </para>
/// </remarks>
[TestFixture]
public sealed class OrdealsTests
{
    private const string HuskId = "enemy.husk";
    private const string SpitterId = "enemy.spitter";
    private const string BloaterId = "enemy.bloater";
    private const string OathboundId = "character.oathbound";
    private const string ModeId = "mode.test";

    private const int HuskCost = 4;
    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;
    private const int MaxWaves = 5;
    private const float Ring = 10f;
    private const float Frame = 1f / 60f;

    private static readonly Vector3 Door = new Vector3(0f, 0f, 18f);

    private static readonly OrdealScheduleSpec Descent = new OrdealScheduleSpec(25, 10);

    private RecordingEvents _events;

    // The flow fixture's world, EssenceWalletTests' shape.
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
    }

    // ---- The spec (rules 2 and 3) ----------------------------------------------------------------

    [Test]
    public void Spec_CarriesItsDials()
    {
        var spec = new OrdealSpec(
            new ContentId("ordeal.everything"),
            new LocKey("ordeal.everything.name"),
            new LocKey("ordeal.everything.description"),
            essenceMultiplier: 0.6f,
            offerCount: 2,
            concurrencyBonus: 8,
            veilrotMultiplier: 1.5f,
            threatCostTarget: new ContentId(HuskId),
            threatCostMultiplier: 0.5f);

        Assert.That(spec.Id.Value, Is.EqualTo("ordeal.everything"));
        Assert.That(spec.NameKey.Key, Is.EqualTo("ordeal.everything.name"));
        Assert.That(spec.DescriptionKey.Key, Is.EqualTo("ordeal.everything.description"));
        Assert.That(spec.EssenceMultiplier, Is.EqualTo(0.6f));
        Assert.That(spec.OfferCount, Is.EqualTo(2));
        Assert.That(spec.ConcurrencyBonus, Is.EqualTo(8));
        Assert.That(spec.VeilrotMultiplier, Is.EqualTo(1.5f));
        Assert.That(spec.ThreatCostTarget.Value, Is.EqualTo(HuskId));
        Assert.That(spec.ThreatCostMultiplier, Is.EqualTo(0.5f));
    }

    [Test]
    public void Spec_HasNoKindAndNothingDispatchesOnOne()
    {
        // Rule 2: five neutral dials and no kind. Asserted as "no member of either type has an enum
        // in its signature" — a switch needs something to switch on, and neither type has one to
        // offer. Scanning IL for the switch opcode was rejected: a lone 0x45 byte is as likely to
        // be somebody's operand as an instruction.
        const BindingFlags everything =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        foreach (Type type in new[] { typeof(OrdealSpec), typeof(Ordeals) })
        {
            foreach (FieldInfo field in type.GetFields(everything))
            {
                Assert.That(field.FieldType.IsEnum, Is.False, $"{type.Name}.{field.Name} is an enum.");
            }

            foreach (PropertyInfo property in type.GetProperties(everything))
            {
                Assert.That(property.PropertyType.IsEnum, Is.False,
                    $"{type.Name}.{property.Name} is an enum.");
            }

            foreach (MethodBase method in Methods(type, everything))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.ParameterType.IsEnum, Is.False,
                        $"{type.Name}.{method.Name} takes an enum ({parameter.Name}).");
                }
            }
        }
    }

    [Test]
    public void Spec_RefusesANeutralOrdeal()
    {
        var thrown = Assert.Throws<ArgumentException>(() => new OrdealSpec(
            new ContentId("ordeal.nothing"),
            new LocKey("ordeal.nothing.name"),
            new LocKey("ordeal.nothing.description")));

        Assert.That(thrown.Message, Does.Contain("ordeal.nothing"));
    }

    [Test]
    public void Spec_RefusesAHalfNamedThreatTarget()
    {
        Assert.Throws<ArgumentException>(
            () => Ordeal("ordeal.half", threatCostTarget: new ContentId(HuskId)),
            "A target with a multiplier of 1 moves nobody's cost.");

        Assert.Throws<ArgumentException>(
            () => Ordeal("ordeal.half", threatCostMultiplier: 0.5f),
            "A multiplier with no target has nobody to move.");
    }

    [Test]
    public void Spec_RefusesANonPositiveOrNonFiniteMultiplier()
    {
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            // Each alongside a turned dial elsewhere, so the neutral refusal cannot be what fires.
            AssertOutOfRange("essenceMultiplier", () => Ordeal("ordeal.bad", essence: bad, offers: 2));
            AssertOutOfRange("veilrotMultiplier", () => Ordeal("ordeal.bad", veilrot: bad, offers: 2));
            AssertOutOfRange(
                "threatCostMultiplier",
                () => Ordeal("ordeal.bad", threatCostTarget: new ContentId(HuskId), threatCostMultiplier: bad));
        }
    }

    [Test]
    public void Spec_RefusesANegativeOfferCountOrBonus()
    {
        AssertOutOfRange("offerCount", () => Ordeal("ordeal.bad", offers: -1, essence: 0.5f));
        AssertOutOfRange("concurrencyBonus", () => Ordeal("ordeal.bad", concurrency: -1, essence: 0.5f));

        // Zero is legal on both and means "unchanged".
        Assert.DoesNotThrow(() => Ordeal("ordeal.fine", offers: 0, concurrency: 0, essence: 0.5f));
    }

    // ---- The schedule (rule 1) -------------------------------------------------------------------

    [Test]
    public void Schedule_DealsOnItsOwnStages()
    {
        foreach (int stage in new[] { 25, 35, 45 })
        {
            Assert.That(Descent.DealsAt(stage), Is.True, $"stage {stage}");
        }

        foreach (int stage in new[] { 24, 26, 30, 34 })
        {
            Assert.That(Descent.DealsAt(stage), Is.False, $"stage {stage}");
        }
    }

    [Test]
    public void Schedule_RefusesAZeroPeriod()
    {
        AssertOutOfRange("everyNStages", () => _ = new OrdealScheduleSpec(25, 0));
        AssertOutOfRange("firstStage", () => _ = new OrdealScheduleSpec(0, 10));
    }

    [Test]
    public void Schedule_DefaultDealsNever()
    {
        OrdealScheduleSpec none = default;

        Assert.That(none.IsAuthored, Is.False);
        Assert.DoesNotThrow(() => none.DealsAt(25));
        Assert.That(none.DealsAt(25), Is.False);
        Assert.That(none.DealsAt(1), Is.False);
    }

    [Test]
    public void Mode_WithoutOneIsUnchanged()
    {
        // Rule 1: optional and last. The same mode built with and without the block differs in the
        // block and nowhere else.
        ModeSpec without = Mode(default, null);
        ModeSpec with = Mode(Descent, FourOrdeals());

        Assert.That(without.Ordeals, Is.Empty);
        Assert.That(without.OrdealSchedule.IsAuthored, Is.False);

        Assert.That(with.Ordeals, Has.Count.EqualTo(4));
        Assert.That(with.OrdealSchedule.FirstStage, Is.EqualTo(25));

        Assert.That(without.Id, Is.EqualTo(with.Id));
        Assert.That(without.StartingStage, Is.EqualTo(with.StartingStage));
        Assert.That(without.IsEndless, Is.EqualTo(with.IsEndless));
        Assert.That(without.Roster, Has.Count.EqualTo(with.Roster.Count));
        Assert.That(without.Essence.PerStageBase, Is.EqualTo(with.Essence.PerStageBase));
        Assert.That(without.Sanctum.HealPrice, Is.EqualTo(with.Sanctum.HealPrice));
    }

    [Test]
    public void Mode_RefusesAnOrdealListedTwice()
    {
        OrdealSpec famine = Famine();

        Assert.Throws<ArgumentException>(() => Mode(Descent, new[] { famine, famine }));
        Assert.Throws<ArgumentException>(() => Mode(Descent, new OrdealSpec[] { famine, null }));
    }

    // ---- The deal (rules 4 and 5) ----------------------------------------------------------------

    [Test]
    public void Deal_NothingBeforeTheFirstStage()
    {
        FixedRandom random = Counting();
        var ordeals = new Ordeals(Mode(Descent, FourOrdeals()), _events);

        EnterStages(ordeals, random, 2, 24);

        Assert.That(ordeals.Applied, Is.Empty);
        Assert.That(random.Capture().Affixes, Is.Zero, "No draw on Affixes before a boundary.");
        Assert.That(_events.Count<OrdealApplied>(), Is.Zero);
    }

    [Test]
    public void Deal_OneAtTheFirstBoundary()
    {
        FixedRandom random = Counting();
        var ordeals = new Ordeals(Mode(Descent, FourOrdeals()), _events);

        EnterStages(ordeals, random, 2, 25);

        Assert.That(ordeals.Applied, Has.Count.EqualTo(1));
        Assert.That(random.Capture().Affixes, Is.EqualTo(1UL), "One draw.");

        OrdealApplied applied = _events.Single<OrdealApplied>();

        Assert.That(applied.Id, Is.EqualTo(ordeals.Applied[0]));
        Assert.That(applied.Stage, Is.EqualTo(25));
        Assert.That(applied.Count, Is.EqualTo(1));
    }

    [Test]
    public void Deal_OneEveryPeriod()
    {
        var ordeals = new Ordeals(Mode(Descent, FourOrdeals()), _events);

        EnterStages(ordeals, Counting(), 2, 55);

        IReadOnlyList<OrdealApplied> applied = _events.Of<OrdealApplied>();

        Assert.That(applied, Has.Count.EqualTo(4));

        int[] stages = { 25, 35, 45, 55 };

        for (int i = 0; i < 4; i++)
        {
            Assert.That(applied[i].Stage, Is.EqualTo(stages[i]));
            Assert.That(applied[i].Count, Is.EqualTo(i + 1));
        }

        Assert.That(ordeals.Remaining, Is.Zero);
    }

    [Test]
    public void Deal_NeverTheSameTwice()
    {
        // Every draw the lowest value, so every pick is "the first one left" — which, with
        // replacement, would be the same Ordeal four times.
        var ordeals = new Ordeals(Mode(Descent, FourOrdeals()), _events);

        EnterStages(ordeals, new FixedRandom(0).SetAffixes(0f, 0f, 0f, 0f), 2, 55);

        Assert.That(ordeals.Applied, Is.Unique);
        Assert.That(ordeals.Applied, Has.Count.EqualTo(4));
    }

    [Test]
    public void Deal_AnEmptyPoolIsSilent()
    {
        FixedRandom random = Counting();
        var ordeals = new Ordeals(Mode(Descent, FourOrdeals()), _events);

        EnterStages(ordeals, random, 2, 55);

        ulong drawn = random.Capture().Affixes;
        _events.Clear();

        EnterStages(ordeals, random, 56, 65);

        Assert.That(_events.Count<OrdealApplied>(), Is.Zero);
        Assert.That(random.Capture().Affixes, Is.EqualTo(drawn), "No draw on an empty pool.");
        Assert.That(ordeals.Applied, Has.Count.EqualTo(4));
    }

    [Test]
    public void Deal_DrawsOnAffixesAndNothingElse()
    {
        // **The ruling, asserted through a real StageFlow** (M6-01b refused a sixth stream; M6-06a
        // chose Affixes). Two runs from 24 to 45 over the same seed, one stocked and one not: the
        // Affixes stream moves by exactly the three boundaries, and every other stream — Spawn
        // above all, which composes the stages — lands where it did without Ordeals.
        FixedRandom stocked = Separated();
        FixedRandom bare = Separated();

        CrossTo(45, Mode(Descent, FourOrdeals()), stocked);
        CrossTo(45, Mode(default, null), bare);

        RandomState with = stocked.Capture();
        RandomState without = bare.Capture();

        Assert.That(with.Affixes, Is.EqualTo(3UL), "Three boundaries: 25, 35, 45.");
        Assert.That(without.Affixes, Is.Zero);
        Assert.That(with.Spawn, Is.EqualTo(without.Spawn), "The composition did not move.");
        Assert.That(with.Offers, Is.EqualTo(without.Offers));
        Assert.That(with.Drops, Is.EqualTo(without.Drops));
        Assert.That(with.Misc, Is.EqualTo(without.Misc));
    }

    [Test]
    public void Deal_ConsumptionDoesNotDependOnWhatWasDrawn()
    {
        // Rule 4: how far the stream moves is a function of how many stocked boundaries were
        // crossed. Two scripts that pick opposite ends every time deal different orders and spend
        // the same number of draws.
        FixedRandom low = new FixedRandom(0).SetAffixes(0f, 0f, 0f, 0f, 0f, 0f);
        FixedRandom high = new FixedRandom(0).SetAffixes(0.99f, 0.99f, 0.99f, 0.99f, 0.99f, 0.99f);

        var first = new Ordeals(Mode(Descent, FourOrdeals()), _events);
        var second = new Ordeals(Mode(Descent, FourOrdeals()), _events);

        EnterStages(first, low, 2, 80);
        EnterStages(second, high, 2, 80);

        Assert.That(first.Applied[0], Is.Not.EqualTo(second.Applied[0]), "The seeds dealt differently.");
        Assert.That(low.Capture().Affixes, Is.EqualTo(high.Capture().Affixes));
        Assert.That(low.Capture().Affixes, Is.EqualTo(4UL));
    }

    [Test]
    public void Deal_HappensBeforeTheComposition()
    {
        // Rule 5. The advancing frame's draws are logged in order: the composition draws Spawn once
        // per body it buys, and the deal draws Affixes once — and the Affixes draw must come first,
        // or Swarm would be a stage late (M6-06b).
        var log = new List<string>();
        var random = new FixedRandom(0).SetSpawn(Alternating(8_192));

        BuildFlow(
            Mode(Descent, FourOrdeals()),
            random,
            new LoggingStream(random.Spawn, "spawn", log),
            new LoggingStream(random.Affixes, "affixes", log));

        BeginAt(24);
        ClearTheStage();

        Step(StageFlow.ClearTime + Frame);
        _flow.LeaveSanctum(_now);
        _snapshot.PlayerPosition = Door;
        Step(Frame);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Transition));

        log.Clear();

        Step(StageFlow.FadeTime + Frame);

        Assert.That(_flow.Stage, Is.EqualTo(25));
        Assert.That(log, Does.Contain("affixes"), "Nothing was dealt on entering 25.");
        Assert.That(log.IndexOf("affixes"), Is.Zero,
            "The frame that entered 25 drew on Spawn before it dealt: " + string.Join(", ", log));
        Assert.That(log.LastIndexOf("spawn"), Is.GreaterThan(0), "The stage was composed after the deal.");
    }

    [Test]
    public void Stage_RefusesANullSet()
    {
        // Required for the wallet's reason (M6-01a rule 5, M6-06a rule 5): an optional set
        // defaulting to null would let a mis-wired run reach stage 25 and be dealt nothing.
        BuildFlow(Mode(Descent, FourOrdeals()), Separated());

        var thrown = Assert.Throws<ArgumentNullException>(() => new StageFlow(
            _mode,
            _composer,
            _director,
            _enemies,
            _projectiles,
            _player,
            new EssenceWallet(_events),
            ordeals: null,
            new FixedRandom(0).Affixes,
            _events,
            _plan,
            seed: 0));

        Assert.That(thrown.ParamName, Is.EqualTo("ordeals"));
    }

    [Test]
    public void Deal_RefusesANullStreamAndAStageBelowOne()
    {
        var ordeals = new Ordeals(Mode(Descent, FourOrdeals()), _events);

        Assert.Throws<ArgumentNullException>(() => ordeals.OnStageEntered(25, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => ordeals.OnStageEntered(0, Counting().Affixes));
        Assert.Throws<ArgumentNullException>(() => _ = new Ordeals(null, _events));
        Assert.Throws<ArgumentNullException>(() => _ = new Ordeals(Mode(default, null), null));
    }

    // ---- Stacking (rule 3) -----------------------------------------------------------------------

    [Test]
    public void Stack_EssenceAndVeilrotMultiply()
    {
        OrdealSpec a = Ordeal("ordeal.a", essence: 0.6f, veilrot: 1.5f);
        OrdealSpec b = Ordeal("ordeal.b", essence: 0.5f, veilrot: 2f);

        Ordeals ordeals = Restored(new[] { a, b }, a.Id, b.Id);

        Assert.That(ordeals.EssenceMultiplier, Is.EqualTo(0.3f).Within(1e-6f));
        Assert.That(ordeals.VeilrotMultiplier, Is.EqualTo(3f).Within(1e-6f));
    }

    [Test]
    public void Stack_ConcurrencySums()
    {
        OrdealSpec a = Ordeal("ordeal.a", concurrency: 8);
        OrdealSpec b = Ordeal("ordeal.b", concurrency: 4);

        Assert.That(Restored(new[] { a, b }, a.Id, b.Id).ConcurrencyBonus, Is.EqualTo(12));
    }

    [Test]
    public void Stack_OfferCountTakesTheSmallest()
    {
        OrdealSpec two = Ordeal("ordeal.two", offers: 2);
        OrdealSpec one = Ordeal("ordeal.one", offers: 1);
        OrdealSpec other = Ordeal("ordeal.other", essence: 0.6f);

        Ordeals ordeals = Restored(new[] { two, one, other }, two.Id, other.Id, one.Id);

        Assert.That(ordeals.OfferCount, Is.EqualTo(1), "The smallest non-zero, never the product.");
    }

    [Test]
    public void Stack_ThreatCostMultipliesPerTarget()
    {
        OrdealSpec a = Ordeal("ordeal.a", threatCostTarget: new ContentId(HuskId), threatCostMultiplier: 0.5f);
        OrdealSpec b = Ordeal("ordeal.b", threatCostTarget: new ContentId(HuskId), threatCostMultiplier: 0.5f);
        OrdealSpec c = Ordeal("ordeal.c", threatCostTarget: new ContentId(SpitterId), threatCostMultiplier: 1.5f);

        Ordeals ordeals = Restored(new[] { a, b, c }, a.Id, b.Id, c.Id);

        Assert.That(ordeals.ThreatCostMultiplier(new ContentId(HuskId)), Is.EqualTo(0.25f).Within(1e-6f));
        Assert.That(ordeals.ThreatCostMultiplier(new ContentId(SpitterId)), Is.EqualTo(1.5f).Within(1e-6f));
        Assert.That(ordeals.ThreatCostMultiplier(new ContentId(BloaterId)), Is.EqualTo(1f));
    }

    [Test]
    public void Stack_ARunWithNoneIsNeutral()
    {
        var ordeals = new Ordeals(Mode(Descent, FourOrdeals()), _events);

        Assert.That(ordeals.EssenceMultiplier, Is.EqualTo(1f));
        Assert.That(ordeals.OfferCount, Is.Zero);
        Assert.That(ordeals.ConcurrencyBonus, Is.Zero);
        Assert.That(ordeals.VeilrotMultiplier, Is.EqualTo(1f));
        Assert.That(ordeals.ThreatCostMultiplier(new ContentId(HuskId)), Is.EqualTo(1f));
        Assert.That(ordeals.ThreatCostMultiplier(default), Is.EqualTo(1f));
    }

    // ---- Nothing reads them (rule 6) -------------------------------------------------------------

    [Test]
    public void Ordeals_NothingReadsTheDialsYet()
    {
        // **After M6-06a, nothing in Soulvail.Core asks for a number** (rule 6). A sweep of every
        // method body in the assembly for a call to one of the five, PaletteTests' device — so the
        // day M6-06b wires the first reader, this row goes red in its diff rather than nowhere.
        var dials = new List<MethodBase>
        {
            typeof(Ordeals).GetProperty(nameof(Ordeals.EssenceMultiplier))!.GetMethod,
            typeof(Ordeals).GetProperty(nameof(Ordeals.OfferCount))!.GetMethod,
            typeof(Ordeals).GetProperty(nameof(Ordeals.ConcurrencyBonus))!.GetMethod,
            typeof(Ordeals).GetProperty(nameof(Ordeals.VeilrotMultiplier))!.GetMethod,
            typeof(Ordeals).GetMethod(nameof(Ordeals.ThreatCostMultiplier)),
        };

        Assert.That(dials, Has.None.Null, "A dial this row names is gone.");

        const BindingFlags everything =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        var callers = new List<string>();

        foreach (Type type in typeof(Ordeals).Assembly.GetTypes())
        {
            if (type == typeof(Ordeals))
            {
                continue;
            }

            foreach (MethodBase method in Methods(type, everything))
            {
                foreach (MethodBase dial in dials)
                {
                    if (Calls(method, dial))
                    {
                        callers.Add($"{type.FullName}.{method.Name} → {dial.Name}");
                    }
                }
            }
        }

        Assert.That(callers, Is.Empty,
            "Something reads an Ordeal dial, which is M6-06b's diff: " + string.Join("; ", callers));
    }

    // ---- Restore (rule 7) ------------------------------------------------------------------------

    [Test]
    public void Restore_ComesBackSilently()
    {
        RunSession session = Session(Mode(Descent, FourOrdeals()));
        var saved = new[] { new ContentId("ordeal.swarm"), new ContentId("ordeal.famine") };

        session.Start(Resumed(40, saved));

        Assert.That(session.State.OrdealIds, Is.EqualTo(saved), "Both in force, in the saved order.");
        Assert.That(_events.Count<OrdealApplied>(), Is.Zero, "A resume is not news.");

        // In force, not merely listed: the dials answer for both.
        Ordeals held = Held(session.State);

        Assert.That(held.EssenceMultiplier, Is.EqualTo(0.6f).Within(1e-6f));
        Assert.That(held.ConcurrencyBonus, Is.EqualTo(8));
    }

    [Test]
    public void Restore_DropsOneThisBuildNoLongerStocks()
    {
        RunSession session = Session(Mode(Descent, FourOrdeals()));

        Assert.DoesNotThrow(() => session.Start(Resumed(
            40,
            new[] { new ContentId("ordeal.deleted"), new ContentId("ordeal.hunger") })));

        Assert.That(session.State.OrdealIds, Is.EqualTo(new[] { new ContentId("ordeal.hunger") }));
        Assert.That(_events.Count<OrdealApplied>(), Is.Zero);
    }

    [Test]
    public void Restore_AnOlderSaveComesBackEmpty()
    {
        // Rule 7's stated cost: v4 wrote Array.Empty here from M6-01b until this task. A save from
        // then comes back with none — resumed at 34 rather than 30 so the row crosses one boundary
        // rather than five, which is the same claim — and entering 35 deals one.
        RunSession session = Session(Mode(Descent, FourOrdeals()));

        session.Start(Resumed(34, Array.Empty<ContentId>()));

        Assert.That(session.State.OrdealIds, Is.Empty);

        ClearOneStage(session);
        CrossTheBoundary(session);

        Assert.That(session.State.StageIndex, Is.EqualTo(35));
        Assert.That(session.State.OrdealIds, Has.Count.EqualTo(1));
        Assert.That(_events.Single<OrdealApplied>().Stage, Is.EqualTo(35));
    }

    [Test]
    public void Restore_ANameTwiceIsDealtOnce()
    {
        OrdealSpec famine = Famine();

        Ordeals ordeals = Restored(new[] { famine }, famine.Id, famine.Id);

        Assert.That(ordeals.Applied, Has.Count.EqualTo(1));
        Assert.That(ordeals.EssenceMultiplier, Is.EqualTo(0.6f).Within(1e-6f), "Not 0.36.");
    }

    // ---- What the save and the state carry -------------------------------------------------------

    [Test]
    public void Recorder_WritesWhatWasDealt()
    {
        // M6-01b rule 7's last placeholder, replaced. A schedule of every stage from 2, so a run
        // reaches three Ordeals in three boundaries rather than twenty — the same mechanism as
        // Descent's 25 / 10, which the Deal_ rows already cover.
        RunSession session = Session(Mode(new OrdealScheduleSpec(2, 1), FourOrdeals()));

        session.Start(Fresh(1));

        for (int i = 0; i < 3; i++)
        {
            ClearOneStage(session);
            CrossTheBoundary(session);
        }

        _events.Clear();

        ClearOneStage(session);

        RunSnapshot snapshot = _events.Single<RunSnapshotTaken>().Snapshot;

        Assert.That(snapshot.OrdealIds, Has.Count.EqualTo(3));
        Assert.That(snapshot.OrdealIds, Is.EqualTo(session.State.OrdealIds), "In the order dealt.");
    }

    [Test]
    public void State_OrdealIdsForwards()
    {
        RunSession session = Session(Mode(new OrdealScheduleSpec(2, 1), FourOrdeals()));

        session.Start(Fresh(1));

        Assert.That(session.State.OrdealIds, Is.Empty);

        ClearOneStage(session);
        CrossTheBoundary(session);

        Assert.That(session.State.OrdealIds, Has.Count.EqualTo(1));
        Assert.That(session.State.OrdealIds[0], Is.EqualTo(_events.Single<OrdealApplied>().Id));

        // AR §18.2: a read, and the handle stays internal — OnStageEntered is public on the set.
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

            Assert.That(exposed, Is.Not.EqualTo(typeof(Ordeals)),
                $"RunState.{member.Name} hands out the Ordeals themselves.");
        }
    }

    // ---- Allocation (rule 8) ---------------------------------------------------------------------

    [Test]
    public void Deal_AllocatesNothing()
    {
        FixedRandom random = Counting();
        var ordeals = new Ordeals(Mode(Descent, FourOrdeals()), _events);

        EnterStages(ordeals, random, 2, 55);

        IRandomStream affixes = random.Affixes;
        var husk = new ContentId(HuskId);
        float sink = 0f;

        AllocationAssert.None(
            () =>
            {
                ordeals.OnStageEntered(65, affixes);
                ordeals.OnStageEntered(66, affixes);
                sink += ordeals.EssenceMultiplier + ordeals.VeilrotMultiplier
                    + ordeals.OfferCount + ordeals.ConcurrencyBonus
                    + ordeals.ThreatCostMultiplier(husk);
                _ = ordeals.Applied.Count;
            },
            iterations: 100_000);

        Assert.That(sink, Is.GreaterThan(0f));
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

    private static OrdealSpec Famine() => Ordeal("ordeal.famine", essence: 0.6f);

    /// <summary>The four M6-06b ships, with its numbers — <c>Descent.asset</c>'s pool.</summary>
    private static OrdealSpec[] FourOrdeals() => new[]
    {
        Famine(),
        Ordeal("ordeal.vigil", offers: 2),
        Ordeal("ordeal.swarm", concurrency: 8, threatCostTarget: new ContentId(HuskId), threatCostMultiplier: 0.5f),
        Ordeal("ordeal.hunger", veilrot: 1.5f),
    };

    /// <summary>A mode of one Husk a stage, EssenceWalletTests' shape, with the Ordeal block given.</summary>
    private static ModeSpec Mode(OrdealScheduleSpec schedule, IReadOnlyList<OrdealSpec> pool)
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
            ordealSchedule: schedule,
            ordeals: pool);
    }

    private static ContentCatalog Catalog(ModeSpec mode) => new ContentCatalog(
        new[] { Oathbound() },
        new[] { Husk() },
        new[] { mode },
        skills: null,
        trees: null);

    /// <summary>Static, ten hit points: EssenceWalletTests' Husk, for its reasons.</summary>
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

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));

    // ---- Fixture: the set on its own -------------------------------------------------------------

    /// <summary>A random whose Affixes stream is its own, so its position counts only the deal.</summary>
    private static FixedRandom Counting() => new FixedRandom(0).SetAffixes(Alternating(64));

    /// <summary>All five streams separate, so each position counts only its own drawers.</summary>
    private static FixedRandom Separated() => new FixedRandom(0)
        .SetSpawn(Alternating(8_192))
        .SetOffers(Alternating(64))
        .SetAffixes(Alternating(64))
        .SetDrops(Alternating(64))
        .SetMisc(Alternating(64));

    private static void EnterStages(Ordeals ordeals, IRandom random, int from, int to)
    {
        for (int stage = from; stage <= to; stage++)
        {
            ordeals.OnStageEntered(stage, random.Affixes);
        }
    }

    /// <summary>A set over <paramref name="pool"/>, restored holding <paramref name="ids"/>.</summary>
    private Ordeals Restored(OrdealSpec[] pool, params ContentId[] ids)
    {
        var ordeals = new Ordeals(Mode(Descent, pool), _events);

        MethodInfo restore = typeof(Ordeals).GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(restore, Is.Not.Null, "Ordeals.Restore has gone.");

        try
        {
            restore.Invoke(ordeals, new object[] { ids });
        }
        catch (TargetInvocationException e)
        {
            throw e.InnerException!;
        }

        Assert.That(_events.Count<OrdealApplied>(), Is.Zero, "Restore published.");

        return ordeals;
    }

    /// <summary>The run's set, which <c>RunState</c> keeps internal — reached as the recorder cannot.</summary>
    private static Ordeals Held(RunState state)
    {
        PropertyInfo property = typeof(RunState).GetProperty(
            "Ordeals",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(property, Is.Not.Null, "RunState.Ordeals has gone.");

        return (Ordeals)property.GetValue(state);
    }

    // ---- Fixture: a stage ------------------------------------------------------------------------

    private void BuildFlow(
        ModeSpec mode,
        FixedRandom random,
        IRandomStream spawn = null,
        IRandomStream affixes = null)
    {
        _mode = mode;
        _now = 0f;

        ContentCatalog catalog = Catalog(mode);

        _enemies = new EnemySystem(catalog, _events, random, new DepthScaling(mode.Scaling), Capacity);

        _snapshot = new WorldSnapshot(Capacity)
        {
            HasGate = true,
            GatePosition = Door,
            SpawnPoints = Points(8),
        };

        _projectiles = new ProjectileSystem(_events, ProjectileCapacity);
        _player = new PlayerCombat(Oathbound(), _events, new RecordingIntents(), Capacity);
        _director = new SpawnDirector(_enemies, _events);
        _composer = new WaveComposer(catalog, new ThreatBudget(mode.Scaling, DeviceCap));
        _plan = new WavePlan(MaxWaves, Math.Max(1, mode.Roster.Count));
        _spawn = spawn ?? random.Spawn;

        _flow = new StageFlow(
            _mode,
            _composer,
            _director,
            _enemies,
            _projectiles,
            _player,
            new EssenceWallet(_events),
            new Ordeals(_mode, _events),
            affixes ?? random.Affixes,
            _events,
            _plan,
            seed: 0);
    }

    /// <summary>Opens <paramref name="stage"/> and crosses boundaries until the flow stands in <paramref name="target"/>.</summary>
    private void CrossTo(int target, ModeSpec mode, FixedRandom random)
    {
        BuildFlow(mode, random);
        BeginAt(24);

        while (_flow.Stage < target)
        {
            ClearTheStage();
            WalkThroughTheDoor();
        }
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

        Assert.That(_events.Count<EnemySpawned>(), Is.GreaterThan(before),
            "The fixture failed to get the wave standing.");

        _enemies.ApplyDamage(_events.Of<EnemySpawned>()[before].Id, 100_000f, _now, _player);

        Step(Frame);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Clear), "The fixture failed to clear the stage.");
    }

    private void WalkThroughTheDoor()
    {
        Step(StageFlow.ClearTime + Frame);

        _flow.LeaveSanctum(_now);

        _snapshot.PlayerPosition = Door;

        Step(Frame);
        Step(StageFlow.FadeTime + Frame);

        _snapshot.PlayerPosition = Vector3.Zero;

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Arrival), "The fixture failed to cross the boundary.");
    }

    // ---- Fixture: a run --------------------------------------------------------------------------

    private RunSession Session(ModeSpec mode)
    {
        _mode = mode;

        return new RunSession(
            Catalog(mode),
            new FixedRandom(0, Alternating(8_192)),
            _events,
            new RecordingIntents(),
            new RunRecorder(new FixedRandom(0, Alternating(8_192)), new FixedClock(default), _events),
            Capacity,
            DeviceCap,
            ProjectileCapacity);
    }

    private static RunConfig Fresh(int stage) => new RunConfig(
        new ContentId(ModeId),
        new ContentId(OathboundId),
        0,
        stage,
        new SpawnPlan(Array.Empty<SpawnPlan.Entry>()),
        restore: null);

    /// <summary>A resume at <paramref name="stage"/> of a fresh level-1 run holding <paramref name="ordealIds"/>.</summary>
    private static RunConfig Resumed(int stage, IReadOnlyList<ContentId> ordealIds) => new RunConfig(
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
            level: 1,
            xp: 0f,
            pendingLevelUps: 0,
            Array.Empty<ContentId>(),
            new ContentId[SkillRunner.MaxManualSlots],
            default,
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>(),
            ordealIds));

    private static WorldSnapshot SessionSnapshot(float dt, Vector3 playerPosition) =>
        new WorldSnapshot(Capacity)
        {
            Dt = dt,
            PlayerPosition = playerPosition,
            HasGate = true,
            GatePosition = Door,
            SpawnPoints = Points(8),
        };

    /// <summary>EssenceWalletTests' route: walk onto the body and answer the Censer's swing.</summary>
    private void ClearOneStage(RunSession session)
    {
        int spawnedBefore = _events.Count<EnemySpawned>();
        int clearedBefore = _events.Count<StageCleared>();

        for (int i = 0; i < 600 && _events.Count<EnemySpawned>() == spawnedBefore; i++)
        {
            session.Tick(SessionSnapshot(Frame, Vector3.Zero));
        }

        Assert.That(_events.Count<EnemySpawned>(), Is.GreaterThan(spawnedBefore), "The fixture failed to land the wave.");

        EnemySpawned spawned = _events.Of<EnemySpawned>()[spawnedBefore];
        int[] report = { spawned.Id };

        for (int i = 0; i < 600 && _events.Count<StageCleared>() == clearedBefore; i++)
        {
            session.Tick(SessionSnapshot(Frame, spawned.Position));
            session.ReportConeHits(report);
        }

        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(clearedBefore + 1), "The fixture failed to clear the stage.");
    }

    private void CrossTheBoundary(RunSession session)
    {
        int before = _events.Count<StageArrived>();

        for (int i = 0; i < 600 && _events.Count<StageArrived>() == before; i++)
        {
            if (session.IsSanctumOpen)
            {
                session.LeaveSanctum();
            }

            session.Tick(SessionSnapshot(Frame, Door));
        }

        Assert.That(_events.Count<StageArrived>(), Is.EqualTo(before + 1), "The fixture failed to cross the boundary.");
    }

    // ---- Small shared machinery ------------------------------------------------------------------

    private static void AssertOutOfRange(string field, TestDelegate authoring)
    {
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(authoring);

        Assert.That(thrown.ParamName, Is.EqualTo(field));
    }

    private static IEnumerable<MethodBase> Methods(Type type, BindingFlags flags)
    {
        foreach (MethodInfo method in type.GetMethods(flags))
        {
            yield return method;
        }

        foreach (ConstructorInfo constructor in type.GetConstructors(flags))
        {
            yield return constructor;
        }
    }

    /// <summary>Whether <paramref name="method"/>'s body calls <paramref name="target"/> — SkillBarPresenterTests' scan.</summary>
    private static bool Calls(MethodBase method, MethodBase target)
    {
        byte[] il;

        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception)
        {
            return false;
        }

        if (il is null)
        {
            return false;
        }

        for (int i = 0; i + 4 < il.Length; i++)
        {
            // call (0x28) and callvirt (0x6F). Nothing else reaches a property getter.
            if (il[i] != 0x28 && il[i] != 0x6F)
            {
                continue;
            }

            try
            {
                MethodBase called = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));

                if (called == target)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Not a token; the window fell inside somebody else's operand.
            }
        }

        return false;
    }

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

    /// <summary>Forwards every draw and writes its name down, so a row can read the order they came in.</summary>
    private sealed class LoggingStream : IRandomStream
    {
        private readonly IRandomStream _inner;
        private readonly string _name;
        private readonly List<string> _log;

        public LoggingStream(IRandomStream inner, string name, List<string> log)
        {
            _inner = inner;
            _name = name;
            _log = log;
        }

        public float NextFloat()
        {
            _log.Add(_name);
            return _inner.NextFloat();
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            _log.Add(_name);
            return _inner.NextInt(minInclusive, maxExclusive);
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            _log.Add(_name);
            return _inner.Range(minInclusive, maxInclusive);
        }

        public bool Chance(float probability)
        {
            _log.Add(_name);
            return _inner.Chance(probability);
        }
    }
}
