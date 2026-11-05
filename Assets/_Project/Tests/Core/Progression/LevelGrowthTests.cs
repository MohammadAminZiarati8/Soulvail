using System;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Progression;

/// <summary>
/// LV-01a: every level gained adds a mode-authored fraction of base max HP and weapon damage,
/// pooled with every other percentage, and heals by the HP it added.
/// </summary>
[TestFixture]
public sealed class LevelGrowthTests
{
    private const float MaxHp = 90f;
    private const float WeaponDamage = 13f;
    private const int EnemyCapacity = 32;

    private RecordingEvents _events;
    private PlayerCombat _combat;
    private LevelTracker _progression;
    private PlayerStats _stats;
    private EffectRegistry _registry;

    [SetUp]
    public void SetUp() => Build(new RecordingEvents());

    // ---- Rule 1: per level, of base -------------------------------------------------------------

    [Test]
    public void Grow_Level30_Is187And158()
    {
        Grow(Jungle());

        ReachLevel(30);

        Assert.That(MaxHpValue, Is.EqualTo(MaxHp * 1.87f).Within(0.01f));
        Assert.That(MaxHpValue, Is.EqualTo(168.3f).Within(0.01f));
        Assert.That(Damage, Is.EqualTo(20.54f).Within(0.01f));
    }

    // ---- Rule 2: pooled (ADR-0008) --------------------------------------------------------------

    [Test]
    public void Grow_PoolsWithOtherPercentages()
    {
        Grow(Jungle());
        _registry.Apply(new ModifyStat(PlayerStat.MaxHp, ModifierKind.PercentAdd, 0.10f), new object());

        ReachLevel(11);

        Assert.That(MaxHpValue, Is.EqualTo(MaxHp * 1.40f).Within(0.01f));
        Assert.That(MaxHpValue, Is.Not.EqualTo(MaxHp * 1.30f * 1.10f).Within(0.01f));
    }

    // ---- Rule 3: heals by the gain --------------------------------------------------------------

    [Test]
    public void Grow_HealsByTheGain()
    {
        Grow(Jungle());
        _combat.Health.ApplyDamage(70f, 0f);
        float before = _combat.Health.Current;
        Assert.That(before, Is.LessThan(MaxHp - 3f), "the fixture left no headroom.");

        ReachLevel(2);

        Assert.That(MaxHpValue, Is.EqualTo(92.7f).Within(0.001f));
        Assert.That(_combat.Health.Current, Is.EqualTo(before + 2.7f).Within(0.001f));
    }

    [Test]
    public void Grow_FullStaysFull()
    {
        Grow(Jungle());

        ReachLevel(2);

        Assert.That(_combat.Health.Current, Is.EqualTo(92.7f).Within(0.001f));
    }

    // ---- Rule 4: before LeveledUp, once per level -----------------------------------------------

    [Test]
    public void LeveledUp_SeesTheGrownMaximum()
    {
        var reading = new MaxHpOnLevelUp();
        Build(reading);
        reading.Health = _combat.Health;
        Grow(Jungle());

        ReachLevel(2);

        Assert.That(reading.Seen, Is.EqualTo(92.7f).Within(0.001f));
    }

    [Test]
    public void Grant_TwoLevels_GrowsTwice()
    {
        LevelGrowth growth = Grow(Jungle());
        XpCurve curve = Scalings.Xp();

        _progression.Grant(curve.ToReach(2) + curve.ToReach(3));

        Assert.That(_progression.Level, Is.EqualTo(3));
        Assert.That(growth.Levels, Is.EqualTo(2));
        Assert.That(MaxHpValue, Is.EqualTo(MaxHp * 1.06f).Within(0.001f));
        Assert.That(Damage, Is.EqualTo(WeaponDamage * 1.04f).Within(0.001f));
    }

    // ---- Rule 5: resume -------------------------------------------------------------------------

    [Test]
    public void Restore_GrowsSilentlyWithoutHealing()
    {
        LevelGrowth growth = Grow(Jungle());
        _combat.Health.ApplyDamage(70f, 0f);
        float before = _combat.Health.Current;
        _events.Clear();

        Restore(growth, 11);

        Assert.That(growth.Levels, Is.EqualTo(10));
        Assert.That(MaxHpValue, Is.EqualTo(MaxHp * 1.30f).Within(0.01f));
        Assert.That(Damage, Is.EqualTo(WeaponDamage * 1.20f).Within(0.001f));
        Assert.That(_combat.Health.Current, Is.EqualTo(before).Within(0.001f), "a restore is not a heal.");
        Assert.That(_events.Count<LeveledUp>(), Is.EqualTo(0));
    }

    // ---- Rule 6: default and guards -------------------------------------------------------------

    [Test]
    public void Default_GrowsNothing()
    {
        Grow(default);

        ReachLevel(5);

        Assert.That(MaxHpValue, Is.EqualTo(MaxHp));
        Assert.That(Damage, Is.EqualTo(WeaponDamage));
        Assert.That(default(LevelGrowthSpec).Grows, Is.False);
        Assert.That(Jungle().Grows, Is.True);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-0.01f)]
    public void Spec_RefusesABadFraction(float bad)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new LevelGrowthSpec(bad, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new LevelGrowthSpec(0f, bad));
    }

    [Test]
    public void Constructor_RefusesNulls()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new LevelGrowth(Jungle(), null, _registry, _combat.Health));
        Assert.Throws<ArgumentNullException>(() => _ = new LevelGrowth(Jungle(), _progression, null, _combat.Health));
        Assert.Throws<ArgumentNullException>(() => _ = new LevelGrowth(Jungle(), _progression, _registry, null));
    }

    [Test]
    public void Constructor_RefusesASecondOnOneTracker()
    {
        Grow(Jungle());

        Assert.Throws<InvalidOperationException>(() => Grow(Jungle()));
    }

    // ---- Fixture --------------------------------------------------------------------------------

    private void Build(IDomainEvents events)
    {
        _events = events as RecordingEvents ?? new RecordingEvents();
        _combat = new PlayerCombat(Character(), events, new RecordingIntents(), EnemyCapacity);
        _progression = new LevelTracker(Scalings.Xp(), events);
        _stats = new PlayerStats(
            _combat,
            new PlayerMotor(new MovementSpec(3f, 0.06f, 0.08f, 720f), Vector3.UnitZ),
            _progression);
        _registry = new EffectRegistry();
        _registry.Register<ModifyStat>(new ModifyStatHandler(_stats));
    }

    private LevelGrowth Grow(LevelGrowthSpec spec) =>
        new LevelGrowth(spec, _progression, _registry, _combat.Health);

    /// <summary>The Jungle's authored pair (<c>ModeDefinitionTests.Jungle_CarriesItsLevelGrowth</c>).</summary>
    private static LevelGrowthSpec Jungle() => new LevelGrowthSpec(0.03f, 0.02f);

    private void ReachLevel(int level)
    {
        while (_progression.Level < level)
        {
            _progression.Grant(_progression.XpToNext - _progression.Xp);
        }

        Assert.That(_progression.Level, Is.EqualTo(level), "the fixture overshot.");
    }

    /// <summary><c>Restore</c> is internal and AR §18.2 grants this assembly no access; reflection.</summary>
    private static void Restore(LevelGrowth growth, int level)
    {
        MethodInfo method = typeof(LevelGrowth).GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null, "LevelGrowth.Restore has gone.");

        method.Invoke(growth, new object[] { level });
    }

    private float MaxHpValue => _combat.Health.MaxHp.Value;

    private float Damage => _stats.Resolve(PlayerStat.WeaponDamage).Value;

    private static CharacterSpec Character() => new CharacterSpec(
        new ContentId("character.ranger"),
        new LocKey("character.ranger.name"),
        new LocKey("character.ranger.description"),
        MaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, WeaponDamage, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);

    /// <summary>Reads the player's maximum at the moment <c>LeveledUp</c> is published.</summary>
    private sealed class MaxHpOnLevelUp : IDomainEvents
    {
        public Health Health { get; set; }

        public float Seen { get; private set; } = float.NaN;

        public void Publish<T>(in T evt) where T : struct
        {
            if (evt is LeveledUp && Health is not null)
            {
                Seen = Health.MaxHp.Value;
            }
        }
    }
}
