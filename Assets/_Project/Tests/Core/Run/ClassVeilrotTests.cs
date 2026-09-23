using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Progression;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Run;

/// <summary>
/// M6-07c: CH §3's third identity axis — the Oathbound resists the Veil, the Gravecaller feeds on it,
/// the Emberwright spends it — as five dials on one block read by the meter and the shop.
/// </summary>
/// <remarks>
/// <para>
/// <b>The three relationships are written out here</b>, CH §3's numbers exactly (rule 2). The rows
/// that read them off the shipped assets are in <c>Soulvail.Tests.Game</c>'s
/// <c>CharacterDefinitionTests</c>, because this assembly cannot open an asset (M0-10); the two meet
/// at the numbers.
/// </para>
/// <para>
/// <b>The cast a Rot price buys is <c>PaidCastTests</c>'</b>, one file over.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ClassVeilrotTests
{
    private const string OathboundId = "character.oathbound";
    private const string GravecallerId = "character.gravecaller";
    private const string EmberwrightId = "character.emberwright";
    private const string PlainId = "character.plain";
    private const string HuskId = "enemy.husk";
    private const string ModeId = "mode.test";

    private const float BaseMaxHp = 200f;
    private const float BaseWeaponDamage = 13f;
    private const int Capacity = 16;
    private const int DeviceCap = 8;
    private const int ProjectileCapacity = 4;
    private const float Frame = 1f / 60f;

    /// <summary>A 15-Rot Pact — GD §13.2's example, and the row every class takes.</summary>
    private const float PactRot = 15f;

    /// <summary>GD §13.3's shop — Descent's numbers, written out, <c>SanctumShopTests</c>' own.</summary>
    private static readonly SanctumSpec Descent = new SanctumSpec(25, 40, 40, 30f, 60, 15f);

    private RecordingEvents _events;
    private PlayerCombat _combat;
    private PlayerStats _stats;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
    }

    // ---- The three shipped relationships (rule 2), shared with PaidCastTests ---------------------

    /// <summary>CH §3.1: <em>Resists</em> — −40 % from Pacts, cleanses at half price.</summary>
    internal static VeilrotSpec Oathbound() => new VeilrotSpec(gainMultiplier: 0.6f, cleansePriceMultiplier: 0.5f);

    /// <summary>CH §3.2: <em>Thrives</em> — starts at 15, +50 % faster, +1 % damage a point.</summary>
    internal static VeilrotSpec Gravecaller() =>
        new VeilrotSpec(startingVeilrot: 15f, gainMultiplier: 1.5f, damagePerPoint: 0.01f);

    /// <summary>CH §3.3: <em>Spends</em> — 5 Veilrot to cast through a cooldown.</summary>
    internal static VeilrotSpec Emberwright() => new VeilrotSpec(instantCastCost: 5f);

    // ---- The spec (rule 1) -----------------------------------------------------------------------

    [Test]
    public void Spec_CarriesItsFive()
    {
        var spec = new VeilrotSpec(10f, 1.5f, 0.5f, 0.01f, 5f);

        Assert.That(spec.StartingVeilrot, Is.EqualTo(10f));
        Assert.That(spec.GainMultiplier, Is.EqualTo(1.5f));
        Assert.That(spec.CleansePriceMultiplier, Is.EqualTo(0.5f));
        Assert.That(spec.DamagePerPoint, Is.EqualTo(0.01f));
        Assert.That(spec.InstantCastCost, Is.EqualTo(5f));
    }

    [Test]
    public void Spec_DefaultsAreNeutral()
    {
        // A relationship that is no relationship: a class the Veil treats ordinarily authors null
        // instead (M6-06a rule 3's refusal).
        Assert.Throws<ArgumentException>(() => new VeilrotSpec());
        Assert.Throws<ArgumentException>(() => new VeilrotSpec(0f, 1f, 1f, 0f, 0f));
    }

    [Test]
    public void Spec_RefusesAnImpossibleDial()
    {
        void Refused(string field, TestDelegate build)
        {
            ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(build, field);

            Assert.That(thrown.ParamName, Is.EqualTo(field));
        }

        Refused("startingVeilrot", () => new VeilrotSpec(startingVeilrot: 100f));
        Refused("startingVeilrot", () => new VeilrotSpec(startingVeilrot: 150f));
        Refused("startingVeilrot", () => new VeilrotSpec(startingVeilrot: -1f));
        Refused("startingVeilrot", () => new VeilrotSpec(startingVeilrot: float.NaN));

        foreach (float bad in new[] { 0f, -0.5f, float.NaN, float.PositiveInfinity })
        {
            Refused("gainMultiplier", () => new VeilrotSpec(gainMultiplier: bad, instantCastCost: 5f));
            Refused("cleansePriceMultiplier", () => new VeilrotSpec(cleansePriceMultiplier: bad, instantCastCost: 5f));
        }

        foreach (float bad in new[] { -0.01f, float.NaN, float.PositiveInfinity })
        {
            Refused("damagePerPoint", () => new VeilrotSpec(damagePerPoint: bad));
            Refused("instantCastCost", () => new VeilrotSpec(instantCastCost: bad));
        }
    }

    [Test]
    public void Spec_AcceptsAStartJustBelowMax()
    {
        Assert.DoesNotThrow(() => new VeilrotSpec(startingVeilrot: 99.9f));

        // At Max, a class would open Claimed and begin every run on a hundred-second clock.
        Assert.Throws<ArgumentOutOfRangeException>(() => new VeilrotSpec(startingVeilrot: Veilrot.Max));
    }

    // ---- What is not built, and what the dials rest on -------------------------------------------

    [Test]
    public void Relationship_TheOathboundsThirdClauseIsNotBuilt()
    {
        string[] dials = typeof(VeilrotSpec)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            dials,
            Is.EqualTo(new[]
            {
                "CleansePriceMultiplier", "DamagePerPoint", "GainMultiplier", "InstantCastCost", "StartingVeilrot",
            }),
            "VeilrotSpec grew a dial. If it scales a Pact's effects, it is CH §3.1's \"Pact effects are "
                + "25 % weaker for him\" — the multiplication M6-05a refused (IEffect has no member to "
                + "scale), and here it would also turn a Pact's authored downside into a discount. It "
                + "is a parking-lot line beside GD §13.2's table-versus-examples contradiction; read "
                + "M6-07c's refusal before building it.");
    }

    [Test]
    public void Relationship_OnlyAPactRaisesTheMeter()
    {
        MethodInfo gain = typeof(Veilrot).GetMethod(nameof(Veilrot.Gain));

        Assert.That(gain, Is.Not.Null, "Veilrot.Gain has gone.");

        MethodBase[] callers = typeof(Veilrot).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(Everything).Cast<MethodBase>().Concat(type.GetConstructors(Everything)))
            .Where(method => Calls(method, gain))
            .ToArray();

        string named = string.Join(", ", callers.Select(m => $"{m.DeclaringType?.Name}.{m.Name}"));

        Assert.That(
            callers.Length,
            Is.EqualTo(1),
            $"Veilrot.Gain has {callers.Length} caller(s) in Soulvail.Core: {named}. M6-07c collapsed CH "
                + "§3.1's \"from Pact nodes\" and CH §3.2's unscoped \"+50 % faster\" into one multiplier "
                + "because a Pact was the only thing that raised the meter. A second source — most "
                + "likely M7-01's Revenant, GD §10.2's 50 threshold — means the Oathbound's dial has to "
                + "become source-scoped.");

        Assert.That(callers[0].DeclaringType, Is.EqualTo(typeof(LevelUpFlow)), "the one caller is the Pact take.");
    }

    // ---- The start (rule 3) ----------------------------------------------------------------------

    [Test]
    public void Start_TheGravecallerOpensAtFifteen()
    {
        RunSession session = Session();
        session.Start(Fresh(GravecallerId));

        Assert.That(session.State.Veilrot, Is.EqualTo(15f));
        Assert.That(_events.Count<VeilrotChanged>(), Is.Zero, "a class's start is not news (M6-04 rule 9).");
        Assert.That(_events.Count<VeilrotThresholdCrossed>(), Is.Zero);
        Assert.That(Rot(session).EnemySpeedBonus, Is.Zero, "15 is under the 25 row.");
    }

    [Test]
    public void Start_TheOtherTwoOpenAtZero()
    {
        foreach (string id in new[] { OathboundId, EmberwrightId })
        {
            _events.Clear();

            RunSession session = Session();
            session.Start(Fresh(id));

            Assert.That(session.State.Veilrot, Is.Zero, id);
        }
    }

    [Test]
    public void Start_ANullBlockOpensAtZero()
    {
        RunSession session = Session();
        session.Start(Fresh(PlainId));

        Assert.That(session.State.Veilrot, Is.Zero);

        // And every M6-04 number is what a class with no relationship still gets.
        Veilrot meter = Meter(null);
        Stat damage = WeaponDamage();

        meter.Gain(PactRot);

        Assert.That(meter.Value, Is.EqualTo(PactRot), "an unmultiplied gain.");
        Assert.That(meter.InstantCastCost, Is.Zero);
        Assert.That(damage.Value, Is.EqualTo(BaseWeaponDamage), "no damage rides the meter.");
    }

    [Test]
    public void Restore_DoesNotReapplyTheStart()
    {
        // M2-14b's class of bug: a resume that re-applies a fresh run's opening on top of the save.
        RunSession session = Session();
        session.Start(Resumed(GravecallerId, veilrot: 40f));

        Assert.That(session.State.Veilrot, Is.EqualTo(40f), "40, not 55.");
        Assert.That(_events.Count<VeilrotChanged>(), Is.Zero);
    }

    // ---- The gain and the cleanse (rule 4) -------------------------------------------------------

    [Test]
    public void Gain_IsMultipliedByTheClass()
    {
        Veilrot oathbound = Meter(Oathbound());
        oathbound.Gain(PactRot);

        Veilrot gravecaller = Meter(Gravecaller());
        Restore(gravecaller, 0f);
        gravecaller.Gain(PactRot);

        Veilrot emberwright = Meter(Emberwright());
        emberwright.Gain(PactRot);

        Assert.That(oathbound.Value, Is.EqualTo(9f).Within(1e-4f), "0.6 × 15.");
        Assert.That(gravecaller.Value, Is.EqualTo(22.5f).Within(1e-4f), "1.5 × 15.");
        Assert.That(emberwright.Value, Is.EqualTo(15f).Within(1e-4f), "unmoved.");
    }

    [Test]
    public void Gain_StacksWithHunger()
    {
        Veilrot meter = Meter(Gravecaller(), Hunger());
        Restore(meter, 0f);

        meter.Gain(PactRot);

        Assert.That(meter.Value, Is.EqualTo(33.75f).Within(1e-4f), "15 × 1.5 × 1.5 — M6-06b's multiplier survives a second.");
    }

    [Test]
    public void Gain_TheClampStillWins()
    {
        Veilrot meter = Meter(Oathbound());
        Restore(meter, 95f);

        meter.Gain(20f);

        Assert.That(meter.Value, Is.EqualTo(Veilrot.Max), "12 on 95 is 107, clamped.");
        Assert.That(meter.IsClaimed, Is.True);
        Assert.That(_events.Count<ClaimingBegan>(), Is.EqualTo(1));
    }

    [Test]
    public void Cleanse_IsNotMultipliedByTheGainDial()
    {
        Veilrot meter = Meter(Gravecaller());
        Restore(meter, 40f);

        meter.Cleanse(15f);

        Assert.That(meter.Value, Is.EqualTo(25f), "25, not 17.5: a cleanse is not a gain.");
    }

    [Test]
    public void Price_ACleanseIsNeverFree()
    {
        Assert.That(
            CleansePrice(Descent, new VeilrotSpec(cleansePriceMultiplier: 0.001f)),
            Is.EqualTo(1),
            "60 × 0.001 rounds to 0, and a free service is M6-02b rule 7's worthless half.");
    }

    [Test]
    public void Price_TheOtherThreeServicesAreUnmoved()
    {
        foreach (VeilrotSpec relationship in new[] { Oathbound(), Gravecaller(), Emberwright() })
        {
            SanctumShop shop = Shop(Descent, relationship);

            Assert.That(shop.PriceOf(SanctumService.Reroll), Is.EqualTo(25));
            Assert.That(shop.PriceOf(SanctumService.Banish), Is.EqualTo(40));
            Assert.That(shop.PriceOf(SanctumService.Heal), Is.EqualTo(40));

            shop.Restore(bought: 2, spent: 0);

            Assert.That(shop.PriceOf(SanctumService.Reroll), Is.EqualTo(100), "and the doubling.");
        }

        Assert.That(CleansePrice(Descent, Oathbound()), Is.EqualTo(30));
        Assert.That(CleansePrice(Descent, Gravecaller()), Is.EqualTo(60));
        Assert.That(CleansePrice(Descent, Emberwright()), Is.EqualTo(60));
    }

    // ---- The spend (rule 5) ----------------------------------------------------------------------

    [Test]
    public void Spend_TakesItOff()
    {
        Veilrot meter = Meter(Emberwright());
        Restore(meter, 40f);

        meter.Spend(5f);

        Assert.That(meter.Value, Is.EqualTo(35f));

        VeilrotChanged changed = _events.Single<VeilrotChanged>();

        Assert.That(changed.Delta, Is.EqualTo(-5f));
        Assert.That(changed.Value, Is.EqualTo(35f));
    }

    [Test]
    public void Spend_RefusesWhatIsNotThere()
    {
        Veilrot meter = Meter(Emberwright());
        Restore(meter, 3f);

        Assert.That(meter.CanSpend(5f), Is.False);
        Assert.Throws<InvalidOperationException>(() => meter.Spend(5f));
        Assert.That(meter.Value, Is.EqualTo(3f), "unmoved.");
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Spend_IsNotACleanse()
    {
        Veilrot cleansed = Meter(Emberwright());
        Restore(cleansed, 3f);

        Veilrot spent = Meter(Emberwright());
        Restore(spent, 3f);

        cleansed.Cleanse(5f);

        Assert.That(cleansed.Value, Is.Zero, "a cleanse clamps and wastes the remainder.");
        Assert.Throws<InvalidOperationException>(() => spent.Spend(5f), "a spend is a price.");
    }

    [Test]
    public void Spend_LosesAThresholdOnTheWayDown()
    {
        Veilrot meter = Meter(Emberwright());
        Restore(meter, 26f);

        meter.Spend(5f);

        Assert.That(meter.Value, Is.EqualTo(21f));
        Assert.That(_events.Single<VeilrotThresholdCrossed>().Entered, Is.False);

        EnemyAgent husk = Enemies(meter).Spawn(new ContentId(HuskId), Vector3.Zero);

        var stack = new List<Modifier>();
        husk.MoveSpeed.CopyModifiersTo(stack);

        Assert.That(
            stack.Exists(m => ReferenceEquals(m.Source, meter)),
            Is.False,
            "the next spawn carries no Veilrot speed — the Emberwright paying twice, as rule 5 says.");
    }

    [Test]
    public void Spend_NeverUnClaims()
    {
        Veilrot meter = Meter(Emberwright());
        meter.Gain(Veilrot.Max);
        meter.Cleanse(90f);

        meter.Spend(5f);

        Assert.That(meter.Value, Is.EqualTo(5f));
        Assert.That(meter.IsClaimed, Is.True, "M6-04 rule 6.");
    }

    [Test]
    public void Spend_RefusesANonPrice()
    {
        Veilrot meter = Meter(Emberwright());
        Restore(meter, 40f);

        foreach (float bad in new[] { 0f, -5f, float.NaN, float.PositiveInfinity })
        {
            Assert.That(meter.CanSpend(bad), Is.False, $"{bad}");
            Assert.Throws<ArgumentOutOfRangeException>(() => meter.Spend(bad), $"{bad}");
        }

        Assert.That(meter.Value, Is.EqualTo(40f));
    }

    // ---- The damage that rides the meter (rule 9) -----------------------------------------------

    [TestCase(0f, 1.00f)]
    [TestCase(30f, 1.30f)]
    [TestCase(60f, 1.60f)]
    public void Damage_RidesTheMeter(float rot, float multiplier)
    {
        Veilrot meter = Meter(Gravecaller());
        Restore(meter, rot);

        Assert.That(WeaponDamage().Value / BaseWeaponDamage, Is.EqualTo(multiplier).Within(1e-5f));
    }

    [Test]
    public void Damage_PoolsAdditively()
    {
        Veilrot meter = Meter(Gravecaller());
        Restore(meter, 60f);

        var stack = new List<Modifier>();
        WeaponDamage().CopyModifiersTo(stack);

        Assert.That(stack.Count, Is.EqualTo(1), "one modifier, rewritten — never sixty.");
        Assert.That(stack[0].Kind, Is.EqualTo(ModifierKind.PercentAdd), "ADR-0008: pooled.");
        Assert.That(WeaponDamage().Value / BaseWeaponDamage, Is.EqualTo(1.60f).Within(1e-5f), "not 1.01⁶⁰ = 1.82.");
    }

    [Test]
    public void Damage_FollowsAGainAndACleanse()
    {
        Veilrot meter = Meter(Gravecaller());

        Assert.That(WeaponDamage().Value / BaseWeaponDamage, Is.EqualTo(1.15f).Within(1e-5f), "the start of 15 rides too.");

        meter.Gain(10f);
        Assert.That(WeaponDamage().Value / BaseWeaponDamage, Is.EqualTo(1.30f).Within(1e-5f), "15 + 1.5 × 10.");

        meter.Cleanse(30f);
        Assert.That(WeaponDamage().Value, Is.EqualTo(BaseWeaponDamage), "and nothing at nothing.");
    }

    [Test]
    public void Damage_DoesNotRewritePerTick()
    {
        Veilrot meter = Meter(Gravecaller());
        Restore(meter, 40f);

        int changes = 0;
        WeaponDamage().Changed += _ => changes++;

        float now = 0f;

        for (int i = 0; i < 600; i++)
        {
            now += Frame;
            meter.Tick(Frame, now);
        }

        Assert.That(changes, Is.Zero, "a still meter writes nothing.");
    }

    [Test]
    public void Damage_AClaimedGravecallerIsFourTimesBase()
    {
        Veilrot meter = Meter(Gravecaller());
        meter.Gain(Veilrot.Max);

        Assert.That(meter.IsClaimed, Is.True);
        Assert.That(
            WeaponDamage().Value / BaseWeaponDamage,
            Is.EqualTo(4f).Within(1e-5f),
            "×2.0 from 100 Rot at +1 % a point (PercentAdd) times ×2.0 from the Claiming's +100 % "
                + "(PercentMult): CH §3.2's \"rushes to 100 on purpose\" as arithmetic, and the first "
                + "number M8-05's balance pass should look at.");
    }

    [Test]
    public void Damage_IsZeroForTheOtherTwo()
    {
        foreach (VeilrotSpec relationship in new[] { Oathbound(), Emberwright() })
        {
            Veilrot meter = Meter(relationship);
            Restore(meter, 80f);

            var stack = new List<Modifier>();
            WeaponDamage().CopyModifiersTo(stack);

            Assert.That(stack, Is.Empty, "no meter-sourced modifier on the weapon below the Claiming.");
            Assert.That(WeaponDamage().Value, Is.EqualTo(BaseWeaponDamage));
        }
    }

    // ---- The wiring (rule 6) ---------------------------------------------------------------------

    [Test]
    public void Run_TheBlockReachesAllThree()
    {
        RunSession session = Session();
        session.Start(Fresh(OathboundId));

        CharacterSpec character = session.State.Character;
        Veilrot meter = Rot(session);
        SanctumShop shop = Internal<SanctumShop>(session.State, "Shop");
        SkillRunner runner = Internal<SkillRunner>(session.State, "Skills");

        Assert.That(character.Veilrot, Is.Not.Null, "the fixture's Oathbound authors one.");
        Assert.That(Field<VeilrotSpec>(meter, "_relationship"), Is.SameAs(character.Veilrot), "the meter.");
        Assert.That(shop, Is.Not.Null, "the fixture's Oathbound has a tree, so a shop.");
        Assert.That(Field<VeilrotSpec>(shop, "_relationship"), Is.SameAs(character.Veilrot), "the shop.");
        Assert.That(Field<Veilrot>(runner, "_veilrot"), Is.SameAs(meter), "the runner, which reads the block through the meter.");
    }

    // ---- Allocation (rule 10) --------------------------------------------------------------------

    [Test]
    public void Veilrot_AllocatesNothing()
    {
        var silent = new SilentEvents();
        Veilrot meter = Meter(new VeilrotSpec(10f, 1.5f, 0.5f, 0.01f, 5f), events: silent);

        // Up 6, down 5, down 1: every verb, a weapon rewrite on each, and the meter back where it was.
        AllocationAssert.None(
            () =>
            {
                meter.Gain(4f);
                meter.Spend(5f);
                meter.Cleanse(1f);
            },
            iterations: 100_000);

        Assert.That(meter.Value, Is.EqualTo(10f).Within(1e-3f));
    }

    // ---- Public helper for Soulvail.Tests.Game ----------------------------------------------------

    /// <summary>
    /// What <paramref name="prices"/>' Cleanse costs a class with <paramref name="relationship"/>,
    /// through a live shop — <c>CharacterDefinitionTests</c>' route to <c>Descent.asset</c>.
    /// </summary>
    public static int CleansePrice(SanctumSpec prices, VeilrotSpec relationship) =>
        new ClassVeilrotTests { _events = new RecordingEvents() }.Shop(prices, relationship).PriceOf(SanctumService.Cleanse);

    // ---- Fixture: a player and a meter -----------------------------------------------------------

    /// <summary>A fresh player and a meter over it — every call its own, so two meters never share a stat.</summary>
    private Veilrot Meter(VeilrotSpec relationship, Ordeals ordeals = null, IDomainEvents events = null)
    {
        IDomainEvents sink = events ?? _events;
        CharacterSpec character = Class(PlainId, null);

        _combat = new PlayerCombat(character, sink, new RecordingIntents(), Capacity);
        _stats = new PlayerStats(
            _combat,
            new PlayerMotor(character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), sink));

        return new Veilrot(_stats, _combat, _combat.Blackboard, sink, ordeals, relationship);
    }

    private Stat WeaponDamage() => _stats.Resolve(PlayerStat.WeaponDamage);

    /// <summary>A shop wired the way <c>RunSession.Start</c> wires one, over a tree of twelve.</summary>
    private SanctumShop Shop(SanctumSpec prices, VeilrotSpec relationship)
    {
        Veilrot meter = Meter(relationship);
        var progression = new LevelTracker(Scalings.Xp(), _events);
        var registry = new EffectRegistry();
        registry.Register<ModifyStat>(new ModifyStatHandler(_stats));

        var runner = new SkillRunner(registry, _combat.Blackboard, _events, meter);

        SkillTreeSpec spec = TreeRulesTests.Tree(
            TreeRulesTests.Shallow('a'),
            TreeRulesTests.Shallow('b'),
            TreeRulesTests.Shallow('c'));

        var tree = new SkillTree(
            new TreeRules(
                spec,
                TreeRulesTests.Catalog(
                    spec,
                    TreeRulesTests.Skills(
                        TreeRulesTests.ShallowSkills('a'),
                        TreeRulesTests.ShallowSkills('b'),
                        TreeRulesTests.ShallowSkills('c')))),
            registry,
            _events);

        var levelUp = new LevelUpFlow(
            tree, progression, runner, registry, _events, new OverflowSpec(0.02f, 0.02f), meter);

        return new SanctumShop(prices, new EssenceWallet(_events), _combat, meter, tree, levelUp, _events, relationship);
    }

    /// <summary>A dealt Hunger, the way a resume deals one — <c>OrdealEffectsTests</c>' route.</summary>
    private Ordeals Hunger()
    {
        var hunger = new ContentId("ordeal.hunger");
        var mode = new ModeSpec(
            new ContentId(ModeId),
            new LocKey("mode.test.name"),
            startingStage: 1,
            isEndless: true,
            finalStage: 0,
            Scalings.Design(),
            Scalings.Xp(),
            Array.Empty<RosterEntry>(),
            ordeals: new[]
            {
                new OrdealSpec(hunger, new LocKey("ordeal.hunger.name"), new LocKey("ordeal.hunger.description"), veilrotMultiplier: 1.5f),
            });

        var ordeals = new Ordeals(mode, _events);
        MethodInfo restore = typeof(Ordeals).GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(restore, Is.Not.Null, "Ordeals.Restore has gone.");

        restore.Invoke(ordeals, new object[] { new[] { hunger } });

        return ordeals;
    }

    /// <summary><c>Veilrot.Restore</c> is internal; silent, so a row starts from a number and not a history.</summary>
    private void Restore(Veilrot meter, float value)
    {
        MethodInfo restore = typeof(Veilrot).GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(restore, Is.Not.Null, "Veilrot.Restore has gone.");

        restore.Invoke(meter, new object[] { value });

        _events.Clear();
    }

    private EnemySystem Enemies(Veilrot meter) => new EnemySystem(
        Catalog(),
        _events,
        new FixedRandom(0),
        new DepthScaling(Scalings.Design()),
        Capacity,
        veilrot: meter);

    // ---- Fixture: a run --------------------------------------------------------------------------

    private RunSession Session() => new RunSession(
        Catalog(),
        new FixedRandom(0),
        _events,
        new RecordingIntents(),
        new RunRecorder(new FixedRandom(0), new FixedClock(default), _events),
        Capacity,
        DeviceCap,
        ProjectileCapacity);

    private static RunConfig Fresh(string characterId) => new RunConfig(
        new ContentId(ModeId),
        new ContentId(characterId),
        0,
        1,
        new SpawnPlan(Array.Empty<SpawnPlan.Entry>()),
        null);

    private static RunConfig Resumed(string characterId, float veilrot) => new RunConfig(
        new ContentId(ModeId),
        new ContentId(characterId),
        0,
        1,
        new SpawnPlan(Array.Empty<SpawnPlan.Entry>()),
        new RunSnapshot(
            RunSnapshot.CurrentVersion,
            new ContentId(ModeId),
            new ContentId(characterId),
            0,
            1,
            new RandomState(101, 102, 103, 104, 105),
            BaseMaxHp,
            0f,
            0f,
            default,
            1,
            0f,
            0,
            Array.Empty<ContentId>(),
            new ContentId[SkillRunner.MaxManualSlots],
            new RunEconomy(0, veilrot, 0, 0),
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>()));

    private static Veilrot Rot(RunSession session) => Internal<Veilrot>(session.State, "Rot");

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

    // ---- Fixture: content ------------------------------------------------------------------------

    /// <summary>
    /// Four classes with one body between them and the three relationships — so every difference a
    /// row sees is the Veil's. Only the Oathbound has a tree (<c>TreeRulesTests.FullTree</c> is
    /// authored against its id), so only its run has a shop.
    /// </summary>
    private static ContentCatalog Catalog() => new ContentCatalog(
        new[]
        {
            Class(OathboundId, Oathbound()),
            Class(GravecallerId, Gravecaller()),
            Class(EmberwrightId, Emberwright()),
            Class(PlainId, null),
        },
        new[] { Husk() },
        new[] { Mode() },
        TreeRulesTests.FullSkills(),
        new[] { TreeRulesTests.FullTree() });

    /// <summary>An empty roster, so a run composes nothing — these rows are about the meter.</summary>
    private static ModeSpec Mode() => new ModeSpec(
        new ContentId(ModeId),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>(),
        sanctum: Descent);

    /// <summary><c>VeilrotTests</c>' body — no Aegis, Focus switched off — carrying <paramref name="veilrot"/>.</summary>
    private static CharacterSpec Class(string id, VeilrotSpec veilrot) => new CharacterSpec(
        new ContentId(id),
        new LocKey(id + ".name"),
        new LocKey(id + ".description"),
        BaseMaxHp,
        new MovementSpec(4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, BaseWeaponDamage, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        veilrot: veilrot);

    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 36f,
        moveSpeed: 3.5f,
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

    // ---- The IL sweep ----------------------------------------------------------------------------

    private const BindingFlags Everything =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Whether <paramref name="method"/>'s body calls <paramref name="target"/> — <c>PaletteTests.Reads</c>'
    /// technique aimed at <c>call</c> and <c>callvirt</c> rather than <c>ldsfld</c>.
    /// </summary>
    private static bool Calls(MethodBase method, MethodInfo target)
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

        Type[] typeArgs = method.DeclaringType is { IsGenericType: true } owner ? owner.GetGenericArguments() : null;
        Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

        for (int i = 0; i + 4 < il.Length; i++)
        {
            // call (0x28) and callvirt (0x6F).
            if (il[i] != 0x28 && il[i] != 0x6F)
            {
                continue;
            }

            try
            {
                if (method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1), typeArgs, methodArgs) == target)
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
}
