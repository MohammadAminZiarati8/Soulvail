using System;
using System.Collections.Generic;
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
using Soulvail.Tests.Core.Run;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// M6-07c rules 6–8: CH §3.3's Emberwright spends 5 Veilrot to cast an ability through its cooldown —
/// once per cooldown, never rescheduling it, and only when the trigger asks.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row opens the same way</b>: one active, its trigger met, a free cast at time zero, so the
/// active is on cooldown with its condition true — the one state a price can buy through. The
/// trigger is <c>EnemiesWithin6m ≥ 1</c> written straight onto the blackboard, because nothing here
/// ticks the combat object that would otherwise overwrite it.
/// </para>
/// <para>
/// <b>The three relationships are <c>ClassVeilrotTests</c>'</b>, CH §3's numbers written out once.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PaidCastTests
{
    private const string ActiveId = "skill.test.bought";
    private const float Frame = 1f / 60f;
    private const int Capacity = 8;
    private const float BaseWeaponDamage = 13f;

    /// <summary>Longer than every row's ten seconds, so one cooldown is all a row ever sees.</summary>
    private const float LongCooldown = 20f;

    private RecordingEvents _events;
    private PlayerCombat _combat;
    private PlayerStats _stats;
    private EffectRegistry _registry;
    private Veilrot _meter;
    private SkillRunner _runner;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
    }

    // ---- Rule 7: the paid cast -------------------------------------------------------------------

    [Test]
    public void Paid_FiresThroughACooldown()
    {
        Build(ClassVeilrotTests.Emberwright(), rot: 40f);
        OpenOnCooldown(LongCooldown);

        Step();

        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(1));

        CastBought bought = _events.Single<CastBought>();

        Assert.That(bought.SkillId, Is.EqualTo(new ContentId(ActiveId)));
        Assert.That(bought.Cost, Is.EqualTo(5f));
        Assert.That(bought.VeilrotAfter, Is.EqualTo(35f));
        Assert.That(_runner.PaidCasts, Is.EqualTo(1));
        Assert.That(_runner.WasPaidFor(0), Is.True);
        Assert.That(_meter.Value, Is.EqualTo(35f));

        // Published after the SkillCast it paid for.
        Assert.That(_events.All[_events.All.Count - 1], Is.InstanceOf<CastBought>());
    }

    [Test]
    public void Paid_DoesNotRescheduleTheCooldown()
    {
        Build(ClassVeilrotTests.Emberwright(), rot: 40f);
        OpenOnCooldown(LongCooldown);

        Step();

        // The free cast at 0 set the clock to 20; a frame later it has 20 − 1/60 left, bought or not.
        float expected = (LongCooldown - _now) / LongCooldown;

        Assert.That(_runner.PaidCasts, Is.EqualTo(1), "the fixture's premise.");
        Assert.That(_runner.CooldownFraction(0), Is.EqualTo(expected).Within(1e-6f));
        Assert.That(_events.Single<SkillCast>().Cooldown, Is.EqualTo(LongCooldown - _now).Within(1e-5f), "the wait actually left.");
        Assert.That(_runner.IsReady(0), Is.False);
    }

    [Test]
    public void Paid_OncePerCooldown()
    {
        Build(ClassVeilrotTests.Emberwright(), rot: 40f);
        OpenOnCooldown(LongCooldown);

        Steps(600);

        Assert.That(_runner.PaidCasts, Is.EqualTo(1), "one, not 600 — the governor is the whole reason this is a rule.");
        Assert.That(_events.Count<CastBought>(), Is.EqualTo(1));
        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(1));
        Assert.That(_meter.Value, Is.EqualTo(35f), "5 Rot, not 3 000.");
    }

    [Test]
    public void Paid_AgainAfterTheCooldownTurnsOver()
    {
        const float cooldown = 2f;

        Build(ClassVeilrotTests.Emberwright(), rot: 40f);
        AddActive(cooldown);
        Hold(true);

        // Ten seconds from zero on an exact clock (i / 60, never a running sum): the free casts land
        // at 0, 2, 4, 6 and 8, and each is bought once more a frame later.
        for (int i = 0; i < 600; i++)
        {
            _now = i / 60f;
            _runner.Tick(Frame, _now);
        }

        var order = new List<char>();

        for (int i = 0; i < _events.All.Count; i++)
        {
            if (_events.All[i] is SkillCast)
            {
                bool paid = i + 1 < _events.All.Count && _events.All[i + 1] is CastBought;

                order.Add(paid ? 'P' : 'F');
            }
        }

        Assert.That(new string(order.ToArray()), Is.EqualTo("FPFPFPFPFP"), "free and bought, alternating.");
        Assert.That(_runner.PaidCasts, Is.EqualTo(5));
        Assert.That(_meter.Value, Is.EqualTo(15f), "five prices of 5.");
    }

    [Test]
    public void Paid_StopsWhenTheMeterIsShort()
    {
        Build(ClassVeilrotTests.Emberwright(), rot: 3f);
        OpenOnCooldown(LongCooldown);

        Assert.DoesNotThrow(() => Steps(60), "CanSpend before Spend.");

        Assert.That(_events.All, Is.Empty, "nothing cast, nothing published.");
        Assert.That(_runner.PaidCasts, Is.Zero);
        Assert.That(_meter.Value, Is.EqualTo(3f));
    }

    [Test]
    public void Paid_StopsTheMomentTheMeterRunsOut()
    {
        const float cooldown = 2f;

        Build(ClassVeilrotTests.Emberwright(), rot: 12f);
        AddActive(cooldown);
        Hold(true);

        for (int i = 0; i < 600; i++)
        {
            _now = i / 60f;
            _runner.Tick(Frame, _now);
        }

        // 12 buys two, and the 2 left buys nothing: five free casts and two bought.
        Assert.That(_runner.PaidCasts, Is.EqualTo(2));
        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(7));
        Assert.That(_meter.Value, Is.EqualTo(2f));
    }

    [Test]
    public void Paid_NeverHappensForTheOtherTwoClasses()
    {
        foreach (VeilrotSpec relationship in new[] { ClassVeilrotTests.Oathbound(), ClassVeilrotTests.Gravecaller() })
        {
            _events = new RecordingEvents();

            Build(relationship, rot: 40f);
            OpenOnCooldown(LongCooldown);

            Steps(600);

            Assert.That(_runner.PaidCasts, Is.Zero);
            Assert.That(_events.All, Is.Empty);
            Assert.That(_meter.Value, Is.EqualTo(40f), "the meter unmoved.");
        }
    }

    [Test]
    public void Paid_NeverHappensWithoutAMeter()
    {
        Build(ClassVeilrotTests.Emberwright(), rot: 40f, runnerHasMeter: false);
        OpenOnCooldown(LongCooldown);

        Assert.DoesNotThrow(() => Steps(600));

        Assert.That(_runner.PaidCasts, Is.Zero);
        Assert.That(_events.All, Is.Empty);
    }

    // ---- Rule 8: the machine pays, the player does not -------------------------------------------

    [Test]
    public void Paid_ManualIsStillRefused()
    {
        Build(ClassVeilrotTests.Emberwright(), rot: 40f);
        OpenOnCooldown(LongCooldown);

        bool cast = _runner.Cast(0, Frame, auto: false);

        Assert.That(cast, Is.False);
        Assert.That(_events.All, Is.Empty);
        Assert.That(_meter.Value, Is.EqualTo(40f));
        Assert.That(_runner.PaidCasts, Is.Zero);
    }

    [Test]
    public void Paid_ATriggerThatIsFalseBuysNothing()
    {
        Build(ClassVeilrotTests.Emberwright(), rot: 40f);
        OpenOnCooldown(LongCooldown);

        Hold(false);
        Steps(600);

        Assert.That(_events.All, Is.Empty, "the price buys the cooldown, never the condition.");
        Assert.That(_meter.Value, Is.EqualTo(40f));
    }

    [Test]
    public void Paid_AManualSkillIsNeverBought()
    {
        Build(ClassVeilrotTests.Emberwright(), rot: 40f);
        OpenOnCooldown(LongCooldown);

        _runner.SetAutoCast(new ContentId(ActiveId), auto: false);
        _events.Clear();

        Steps(600);

        Assert.That(_events.All, Is.Empty, "the branch sits below the Manual rule, not beside it.");
        Assert.That(_meter.Value, Is.EqualTo(40f));
    }

    [Test]
    public void Paid_ReadsTheCostOffTheMeter()
    {
        Build(ClassVeilrotTests.Emberwright(), rot: 40f);

        Assert.That(_meter.InstantCastCost, Is.EqualTo(5f));

        Build(ClassVeilrotTests.Oathbound(), rot: 40f);

        Assert.That(_meter.InstantCastCost, Is.Zero);
    }

    // ---- Rule 10 ---------------------------------------------------------------------------------

    [Test]
    public void Paid_AllocatesNothing()
    {
        var silent = new SilentEvents();

        Build(ClassVeilrotTests.Emberwright(), rot: 40f, events: silent);

        // A cooldown of three frames, so a free cast and a bought one come round every few ticks
        // and both doors are inside the measurement. The price is paid back after each purchase so
        // the meter never runs dry, and the cast's own buff is taken off, SkillRunnerTests' way.
        SkillSpec skill = AddActive(0.05f);
        Hold(true);

        Stat damage = _stats.Resolve(PlayerStat.WeaponDamage);
        float now = 0f;

        AllocationAssert.None(
            () =>
            {
                int before = _runner.PaidCasts;

                now += Frame;
                _runner.Tick(Frame, now);
                damage.RemoveAll(skill.Active);

                if (_runner.PaidCasts != before)
                {
                    _meter.Gain(5f);
                }
            },
            iterations: 100_000);

        Assert.That(_runner.PaidCasts, Is.GreaterThan(10_000), "the probe measured purchases, not a no-op.");
        Assert.That(damage.Value, Is.EqualTo(BaseWeaponDamage));
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    private void Build(VeilrotSpec relationship, float rot, bool runnerHasMeter = true, IDomainEvents events = null)
    {
        IDomainEvents sink = events ?? _events;
        CharacterSpec character = Character();

        _combat = new PlayerCombat(character, sink, new RecordingIntents(), Capacity);
        _stats = new PlayerStats(
            _combat,
            new PlayerMotor(character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), sink));

        _registry = new EffectRegistry();
        _registry.Register<ModifyStat>(new ModifyStatHandler(_stats));

        _meter = new Veilrot(_stats, _combat, _combat.Blackboard, sink, relationship: relationship);
        RestoreMeter(rot);

        _runner = new SkillRunner(_registry, _combat.Blackboard, sink, runnerHasMeter ? _meter : null);
        _now = 0f;
    }

    /// <summary>One active, its trigger met, cast free at zero — then the log is cleared.</summary>
    private void OpenOnCooldown(float cooldown)
    {
        AddActive(cooldown);
        Hold(true);

        _runner.Tick(Frame, 0f);

        Assert.That(_runner.IsReady(0), Is.False, "the free cast did not happen.");

        _events.Clear();
    }

    private SkillSpec AddActive(float cooldown)
    {
        var skill = new SkillSpec(
            new ContentId(ActiveId),
            new LocKey(ActiveId + ".name"),
            new LocKey(ActiveId + ".desc"),
            SkillKind.Active,
            Array.Empty<IEffect>(),
            new ActiveSpec(
                cooldown,
                new TriggerSpec(new[] { new TriggerClause(TriggerField.EnemiesWithin6m, TriggerComparison.AtLeast, 1f) }),
                new IEffect[] { new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.5f) }));

        _runner.Add(skill);

        return skill;
    }

    private void Hold(bool met) => _combat.Blackboard.EnemiesWithin6m = met ? 1 : 0;

    private void Step()
    {
        _now += Frame;
        _runner.Tick(Frame, _now);
    }

    private void Steps(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Step();
        }
    }

    /// <summary><c>Veilrot.Restore</c> is internal; silent, so the meter starts from a number.</summary>
    private void RestoreMeter(float value)
    {
        MethodInfo restore = typeof(Veilrot).GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(restore, Is.Not.Null, "Veilrot.Restore has gone.");

        restore.Invoke(_meter, new object[] { value, false });
    }

    /// <summary><c>ClassVeilrotTests</c>' body: no Aegis, Focus off, the relationship on the meter rather than here.</summary>
    private static CharacterSpec Character() => new CharacterSpec(
        new ContentId("character.emberwright"),
        new LocKey("character.emberwright.name"),
        new LocKey("character.emberwright.description"),
        200f,
        new MovementSpec(4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, BaseWeaponDamage, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
