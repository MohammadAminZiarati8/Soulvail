using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// GD §12.3 applied to one enemy, and the thing a recycled enemy must forget: <b>ledger row 2</b>.
/// </summary>
/// <remarks>
/// <para>
/// GD §8.1's Husk throughout — 36 HP, 3.5 m/s, 8 contact damage — so a failure reads as "the enemy
/// we ship arrives at the wrong depth" rather than as an arithmetic puzzle. Every expectation is
/// the authored number times a multiplier written out from GD §12.3's formula.
/// </para>
/// <para>
/// Agents come from an <see cref="EnemyRegistry"/> rather than an <c>EnemySystem</c>, because
/// <c>EnemyAgent</c>'s constructor is <c>internal</c> and this fixture needs the one thing a
/// system deliberately does not offer: an <em>unscaled</em> agent, so that
/// <see cref="DepthScaling.Apply"/> is what is under test rather than
/// <c>EnemySystem.Spawn</c> calling it. That a spawn applies it at all is
/// <c>EnemySystemTests.Spawn_AppliesDepth</c>'s row.
/// </para>
/// </remarks>
[TestFixture]
public sealed class DepthScalingTests
{
    private const string HuskId = "enemy.husk";
    private const string WardenId = "enemy.warden";

    private const int Capacity = 8;

    // GD §8.1 and M1-05: the three numbers depth moves.
    private const float HuskMaxHp = 36f;
    private const float HuskMoveSpeed = 3.5f;
    private const float HuskContactDamage = 8f;

    /// <summary>h(20) = 1 + 0.06·19.</summary>
    private const float HpAt20 = 2.14f;

    /// <summary>d(20) = 1 + 0.035·19.</summary>
    private const float DamageAt20 = 1.665f;

    /// <summary>s(20) = 1 + 0.02·floor(20/5).</summary>
    private const float SpeedAt20 = 1.08f;

    private const float Tolerance = 1e-3f;

    private EnemyRegistry _registry;
    private DepthScaling _scaling;

    [SetUp]
    public void SetUp()
    {
        _registry = new EnemyRegistry(Capacity);
        _scaling = new DepthScaling(DesignScaling());
    }

    // ---- Rule 8: the three modifiers -------------------------------------------------------------

    [Test]
    public void Apply_ScalesAllThree()
    {
        EnemyAgent husk = Spawn();

        _scaling.Apply(husk, 20);

        Assert.That(husk.Health.MaxHp.Value, Is.EqualTo(HuskMaxHp * HpAt20).Within(Tolerance));
        Assert.That(husk.ContactDamage.Value, Is.EqualTo(HuskContactDamage * DamageAt20).Within(Tolerance));
        Assert.That(husk.MoveSpeed.Value, Is.EqualTo(HuskMoveSpeed * SpeedAt20).Within(Tolerance));

        // The spec's floats are untouched — they are what a designer typed, and they are the base
        // the stacks above are built on (M2-03 rule 7).
        Assert.That(husk.Spec.MaxHp, Is.EqualTo(HuskMaxHp));
        Assert.That(husk.Spec.MoveSpeed, Is.EqualTo(HuskMoveSpeed));
        Assert.That(husk.Spec.ContactDamage, Is.EqualTo(HuskContactDamage));
    }

    [Test]
    public void Apply_UsesPercentMultFromOneSource()
    {
        EnemyAgent husk = Spawn();

        _scaling.Apply(husk, 20);

        // PercentMult and not PercentAdd, so depth *multiplies* with an Elite's 2.2× rather than
        // pooling with it (GD §8.3). One modifier per stat, all three from the same source object
        // — which is what would let a caller take the whole of depth back off in three calls.
        AssertOneMultFrom(husk.Health.MaxHp, _scaling, HpAt20 - 1f);
        AssertOneMultFrom(husk.ContactDamage, _scaling, DamageAt20 - 1f);
        AssertOneMultFrom(husk.MoveSpeed, _scaling, SpeedAt20 - 1f);
    }

    [Test]
    public void Apply_MultipliesWithAnotherPercentMult()
    {
        EnemyAgent husk = Spawn();

        // An Elite's 2.2× (GD §8.3), applied before depth. It arrives from M7-02 in the real game;
        // here it stands for "something else already multiplies this stat".
        var elite = new object();
        husk.Health.MaxHp.Add(new Modifier(ModifierKind.PercentMult, 1.2f, elite));

        _scaling.Apply(husk, 20);

        // Π(1 + PercentMult): the two factors multiply rather than pooling into ×3.34.
        Assert.That(
            husk.Health.MaxHp.Value,
            Is.EqualTo(HuskMaxHp * 2.2f * HpAt20).Within(Tolerance),
            "Depth must multiply with an Elite's bonus, not pool with it (GD §8.3).");

        // And it is loudly not the pooled answer, which would be 36 × (1 + 1.2 + 1.14).
        Assert.That(husk.Health.MaxHp.Value, Is.Not.EqualTo(HuskMaxHp * 3.34f).Within(Tolerance));
    }

    // ---- Rule 9: the refill ----------------------------------------------------------------------

    [Test]
    public void Apply_RefillsToScaledMax()
    {
        EnemyAgent husk = Spawn();

        Assert.That(husk.Health.Current, Is.EqualTo(HuskMaxHp).Within(Tolerance),
            "Sanity: a freshly spawned agent starts full at its authored maximum.");

        _scaling.Apply(husk, 20);

        // Health.Reset fills Current from MaxHp.Value, so a maximum raised afterwards would leave
        // a stage-20 Husk standing at 36 of 77 hit points — a bar that starts part-filled, with
        // nothing reporting it. AR §18.1's ordering, one layer out.
        Assert.That(husk.Health.Current, Is.EqualTo(husk.Health.MaxHp.Value).Within(Tolerance));
        Assert.That(husk.Health.Fraction, Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void Apply_StageOne_ChangesNothingMeasurable()
    {
        EnemyAgent husk = Spawn();

        _scaling.Apply(husk, 1);

        // h(1) = d(1) = s(1) = 1 exactly, which is what makes stage 1 the archetype as authored.
        // The modifiers still go on — the stack's contents must not depend on the depth — so this
        // row is about the values and deliberately not about the count.
        Assert.That(husk.Health.MaxHp.Value, Is.EqualTo(HuskMaxHp).Within(Tolerance));
        Assert.That(husk.ContactDamage.Value, Is.EqualTo(HuskContactDamage).Within(Tolerance));
        Assert.That(husk.MoveSpeed.Value, Is.EqualTo(HuskMoveSpeed).Within(Tolerance));
        Assert.That(husk.Health.Current, Is.EqualTo(HuskMaxHp).Within(Tolerance));
    }

    [Test]
    public void Apply_DeeperIsStrictlyHarder()
    {
        // Beyond the spec's table. Three assertions of "36 × some multiplier" would all pass on a
        // curve wired to the wrong stat, so this row asks the question the player would: does
        // going deeper make an enemy tougher, harder-hitting and faster, all three?
        EnemyAgent shallow = Spawn();
        EnemyAgent deep = Spawn();

        _scaling.Apply(shallow, 5);
        _scaling.Apply(deep, 30);

        Assert.That(deep.Health.MaxHp.Value, Is.GreaterThan(shallow.Health.MaxHp.Value));
        Assert.That(deep.ContactDamage.Value, Is.GreaterThan(shallow.ContactDamage.Value));
        Assert.That(deep.MoveSpeed.Value, Is.GreaterThan(shallow.MoveSpeed.Value));
    }

    // ---- Rule 10 and ledger row 2: what a recycled agent forgets ---------------------------------

    [Test]
    public void Recycle_ForgetsPreviousScaling()
    {
        EnemyAgent deep = Spawn();

        _scaling.Apply(deep, 40);

        Assert.That(deep.Health.MaxHp.Value, Is.GreaterThan(HuskMaxHp * 3f),
            "Sanity: it really is wearing stage 40's scaling.");

        int id = deep.Id;

        Assert.That(_registry.Despawn(id), Is.True);

        EnemyAgent recycled = Spawn();

        // The same object back off the free list — which is the whole reason this row exists. If
        // the registry had built a fresh agent, nothing here would prove anything.
        Assert.That(recycled, Is.SameAs(deep), "Sanity: the registry recycled rather than rebuilt.");

        // Back at GD §8.1's authored numbers, with no depth applied at all. This is ledger row 2:
        // before M2-03, Stat removed modifiers by source reference only, so every recycled Husk
        // would have arrived wearing the last one's scaling — a silent balance bug.
        Assert.That(recycled.Health.MaxHp.Value, Is.EqualTo(HuskMaxHp).Within(Tolerance));
        Assert.That(recycled.MoveSpeed.Value, Is.EqualTo(HuskMoveSpeed).Within(Tolerance));
        Assert.That(recycled.ContactDamage.Value, Is.EqualTo(HuskContactDamage).Within(Tolerance));

        Assert.That(recycled.Health.MaxHp.ModifierCount, Is.Zero);
        Assert.That(recycled.MoveSpeed.ModifierCount, Is.Zero);
        Assert.That(recycled.ContactDamage.ModifierCount, Is.Zero);

        // And it is full, at the number it is now rather than at the one it used to be.
        Assert.That(recycled.Health.Current, Is.EqualTo(HuskMaxHp).Within(Tolerance));
    }

    [Test]
    public void Recycle_ForgetsAForeignModifier()
    {
        EnemyAgent husk = Spawn();

        // Not DepthScaling's, and that is the point of the no-argument overload: a source token
        // owned by the scaler would leave this one behind, and the next thing to buff an enemy —
        // M7-02's affixes, M3's player-inflicted debuffs — would each have to be enumerated at the
        // recycle point. That list gets one entry short (Stat.RemoveAll).
        var stranger = new object();
        husk.MoveSpeed.Add(new Modifier(ModifierKind.PercentAdd, 0.45f, stranger));
        husk.ContactDamage.Add(new Modifier(ModifierKind.Flat, 100f, stranger));
        husk.Health.MaxHp.Add(new Modifier(ModifierKind.PercentMult, 5f, stranger));

        Assert.That(husk.MoveSpeed.Value, Is.GreaterThan(HuskMoveSpeed), "Sanity: it took.");

        Assert.That(_registry.Despawn(husk.Id), Is.True);

        EnemyAgent recycled = Spawn();

        Assert.That(recycled, Is.SameAs(husk));
        Assert.That(recycled.MoveSpeed.Value, Is.EqualTo(HuskMoveSpeed).Within(Tolerance));
        Assert.That(recycled.ContactDamage.Value, Is.EqualTo(HuskContactDamage).Within(Tolerance));
        Assert.That(recycled.Health.MaxHp.Value, Is.EqualTo(HuskMaxHp).Within(Tolerance));
    }

    [Test]
    public void Recycle_AsAnotherArchetype_TakesItsNumbers()
    {
        // Beyond the spec's table, and it is the older half of the same rule (AR §18.1): a
        // recycled agent may come back as an entirely different archetype, and all three stats
        // have to follow the new spec rather than two of them following the old one.
        EnemyAgent husk = Spawn();

        _scaling.Apply(husk, 40);

        Assert.That(_registry.Despawn(husk.Id), Is.True);

        EnemyAgent warden = _registry.Spawn(Warden(), Vector3.Zero);

        Assert.That(warden, Is.SameAs(husk));
        Assert.That(warden.Health.MaxHp.Value, Is.EqualTo(90f).Within(Tolerance));
        Assert.That(warden.MoveSpeed.Value, Is.EqualTo(2f).Within(Tolerance));
        Assert.That(warden.ContactDamage.Value, Is.EqualTo(22f).Within(Tolerance));
        Assert.That(warden.Health.Current, Is.EqualTo(90f).Within(Tolerance));
    }

    // ---- Guards ----------------------------------------------------------------------------------

    [Test]
    public void Ctor_NullScaling_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DepthScaling(null));
    }

    [Test]
    public void Apply_NullAgent_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _scaling.Apply(null, 1));
    }

    [Test]
    public void Apply_StageBelowOne_Throws_AgentUntouched()
    {
        EnemyAgent husk = Spawn();

        Assert.Throws<ArgumentOutOfRangeException>(() => _scaling.Apply(husk, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _scaling.Apply(husk, -3));

        // Every curve is evaluated before anything is mutated, so a refused stage leaves the agent
        // exactly as it was rather than wearing one modifier out of three — the validate-then-
        // assign order RunSession.Start takes with a config.
        Assert.That(husk.Health.MaxHp.ModifierCount, Is.Zero);
        Assert.That(husk.MoveSpeed.ModifierCount, Is.Zero);
        Assert.That(husk.ContactDamage.ModifierCount, Is.Zero);
        Assert.That(husk.Health.MaxHp.Value, Is.EqualTo(HuskMaxHp).Within(Tolerance));
    }

    // ---- Fixture helpers -------------------------------------------------------------------------

    private EnemyAgent Spawn() => _registry.Spawn(Husk(), Vector3.Zero);

    /// <summary>
    /// Asserts that <paramref name="stat"/> carries exactly one modifier, that it is a
    /// <see cref="ModifierKind.PercentMult"/> of <paramref name="expected"/>, and that
    /// <paramref name="source"/> is who put it there.
    /// </summary>
    private static void AssertOneMultFrom(Stat stat, object source, float expected)
    {
        var modifiers = new List<Modifier>();
        stat.CopyModifiersTo(modifiers);

        Assert.That(modifiers.Count, Is.EqualTo(1));
        Assert.That(modifiers[0].Kind, Is.EqualTo(ModifierKind.PercentMult));
        Assert.That(modifiers[0].Value, Is.EqualTo(expected).Within(Tolerance));
        Assert.That(modifiers[0].Source, Is.SameAs(source),
            "The DepthScaling instance is its own source token, so its modifiers are removable.");
    }

    /// <summary>GD §8.1's Husk.</summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: HuskMaxHp,
        moveSpeed: HuskMoveSpeed,
        targetPriority: 1,
        threatCost: 4,
        isElite: false,
        contactDamage: HuskContactDamage,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Chaser);

    /// <summary>
    /// A second archetype with three different numbers, for the recycle-as-a-stranger row. GD
    /// §8.1's Warden of Ash is not authored anywhere yet (M7-01) — these are its published HP and
    /// plausible neighbours, and nothing here is a claim about its design.
    /// </summary>
    private static EnemySpec Warden() => new EnemySpec(
        new ContentId(WardenId),
        new LocKey("enemy.warden.name"),
        maxHp: 90f,
        moveSpeed: 2f,
        targetPriority: 4,
        threatCost: 14,
        isElite: false,
        contactDamage: 22f,
        reach: 2f,
        windupTime: 0.8f,
        recoverTime: 1f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Chaser);

    /// <summary>
    /// GD §12's curves, written out rather than read from <c>Descent.asset</c> — see
    /// <c>ThreatBudgetTests</c>' helper for why.
    /// </summary>
    private static ScalingSpec DesignScaling() => new ScalingSpec(
        new BudgetCurve(40f, 12f, 0.9f),
        new WaveCurve(2, 5, 2, 5),
        new ConcurrencyCurve(10, 2),
        new StatCurve(0.06f, 4f, 1, 1),
        new StatCurve(0.035f, 3f, 1, 1),
        new StatCurve(0.02f, 1.3f, 5, 0));
}
