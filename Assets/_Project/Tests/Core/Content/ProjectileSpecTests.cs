using System;
using NUnit.Framework;
using Soulvail.Core.Content;

namespace Soulvail.Tests.Core.Content;

/// <summary>
/// The two optional blocks an enemy gained in M2-06, and the rule that decides when an archetype is
/// allowed to be without one.
/// </summary>
/// <remarks>
/// <para>
/// The blocks themselves are four numbers and four guards; what is worth a fixture is the
/// <em>agreement</em> rule between a block and a behaviour kind, because it is asymmetric on
/// purpose and the asymmetry is what lets this milestone author a Spitter three tasks before
/// anything can fire one.
/// </para>
/// <para>
/// <c>EnemySpec</c>'s own validation lives in <c>EnemyRegistryTests</c>, which is where every other
/// field of it is checked; the rows here are the ones the two new types brought with them.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ProjectileSpecTests
{
    /// <summary>GD §8.1's Spitter — 14 m standoff, and M2-06's speed and forgiveness.</summary>
    private const float Standoff = 14f;
    private const float Speed = 12f;
    private const float Radius = 1.6f;

    private const float Tolerance = 1e-6f;

    [Test]
    public void Projectile_RecordsAllThree()
    {
        var projectile = new ProjectileSpec(Standoff, Speed, Radius);

        // Three floats in a row is the transposition risk these rows exist for — the same one
        // EnemyDefinitionTests names between windup and recover. 14, 12 and 1.6 are far enough
        // apart that a swapped pair cannot satisfy the other's assertion.
        Assert.That(projectile.StandoffRange, Is.EqualTo(Standoff).Within(Tolerance),
            "GD §8.1: the Spitter fires from 14 m.");
        Assert.That(projectile.Speed, Is.EqualTo(Speed).Within(Tolerance), "M2-06: 12 m/s of flight.");
        Assert.That(projectile.Radius, Is.EqualTo(Radius).Within(Tolerance), "M2-06: 1.6 m of forgiveness.");
    }

    [Test]
    public void Projectile_NonPositiveField_Throws()
    {
        // Zero, negative and NaN on three different fields, so no single guard can cover for a
        // missing one. NaN is the row that matters: !(value > 0f) refuses it and `value <= 0f`
        // would wave it straight through (AR §18.3).
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectileSpec(Standoff, 0f, Radius));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectileSpec(Standoff, Speed, -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectileSpec(float.NaN, Speed, Radius));

        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectileSpec(0f, Speed, Radius));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectileSpec(Standoff, float.NaN, Radius));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectileSpec(Standoff, Speed, float.NaN));

        // Infinity passes a `> 0` test, which is why it is asked about separately: an infinite
        // speed arrives before it is drawn, and an infinite standoff is an archer that never
        // approaches at all.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProjectileSpec(float.PositiveInfinity, Speed, Radius));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProjectileSpec(Standoff, float.PositiveInfinity, Radius));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProjectileSpec(Standoff, Speed, float.PositiveInfinity));

        Assert.DoesNotThrow(() => new ProjectileSpec(Standoff, Speed, Radius));
    }

    [Test]
    public void Explosion_RadiusMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExplosionSpec(0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExplosionSpec(-3f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExplosionSpec(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExplosionSpec(float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExplosionSpec(float.NegativeInfinity));

        var explosion = new ExplosionSpec(3f);

        Assert.That(explosion.Radius, Is.EqualTo(3f).Within(Tolerance), "GD §8.1: the Bloater's blast is 3 m.");
    }

    // ---- The agreement rule: a kind requires its block, a block does not require its kind -------

    [Test]
    public void Spec_SpitterWithoutProjectile_Throws()
    {
        // ArgumentException rather than ArgumentOutOfRangeException, and Assert.Throws is an exact
        // type match: this is not a number out of range, it is an archetype that cannot run.
        var thrown = Assert.Throws<ArgumentException>(
            () => Spec(EnemyBehaviourKind.Spitter, projectile: null));

        Assert.That(thrown.ParamName, Is.EqualTo("projectile"),
            "The message must name the block that is missing, not the behaviour that wanted it.");
    }

    [Test]
    public void Spec_BloaterWithoutExplosion_Throws()
    {
        var thrown = Assert.Throws<ArgumentException>(
            () => Spec(EnemyBehaviourKind.Bloater, explosion: null));

        Assert.That(thrown.ParamName, Is.EqualTo("explosion"));
    }

    [Test]
    public void Spec_StaticWithProjectile_IsLegal()
    {
        // The other direction, and it is deliberately allowed rather than merely unchecked: this is
        // exactly what Spitter.asset is between M2-06 and M2-07b — the numbers authored, the
        // behaviour still Static because nothing can run one yet (rule 3, rule 11). Refusing it
        // would force the data and the AI into a single change.
        EnemySpec spec = null;

        Assert.That(
            () => spec = Spec(EnemyBehaviourKind.Static, projectile: new ProjectileSpec(Standoff, Speed, Radius)),
            Throws.Nothing,
            "An archetype may carry a block its behaviour does not use yet — that is what lets the " +
            "numbers ship one task ahead of the AI.");

        Assert.That(spec.Projectile, Is.Not.Null);
        Assert.That(spec.Explosion, Is.Null);

        // And the same for a blast on something that does not go off, which is Bloater.asset today.
        Assert.That(
            () => Spec(EnemyBehaviourKind.Static, explosion: new ExplosionSpec(3f)),
            Throws.Nothing);
    }

    [Test]
    public void Spec_AggroRangeMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(aggroRange: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(aggroRange: -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(aggroRange: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(aggroRange: float.PositiveInfinity));

        Assert.That(Spec(aggroRange: 30f).AggroRange, Is.EqualTo(30f).Within(Tolerance));
    }

    [Test]
    public void Spec_BlocksAreNullByDefault()
    {
        EnemySpec spec = Spec();

        // Null rather than a zeroed object, which is the whole argument for the shape (rule 1): a
        // reader asks "is there one" and gets an answer, instead of asking "is its radius zero" and
        // having to know that zero means absent.
        Assert.That(spec.Projectile, Is.Null, "A Husk throws nothing.");
        Assert.That(spec.Explosion, Is.Null, "A Husk does not explode.");
    }

    [Test]
    public void Spec_CarriesBothBlocks_WhenGivenBoth()
    {
        // No archetype in V1 does both. The row is here because nothing refuses it and the pair is
        // stored in two separate fields — a spec that silently kept only one would be found by
        // whichever archetype first wanted both, three milestones from now.
        EnemySpec spec = Spec(
            projectile: new ProjectileSpec(Standoff, Speed, Radius),
            explosion: new ExplosionSpec(3f));

        Assert.That(spec.Projectile.StandoffRange, Is.EqualTo(Standoff).Within(Tolerance));
        Assert.That(spec.Explosion.Radius, Is.EqualTo(3f).Within(Tolerance));
    }

    /// <summary>
    /// GD §8.1's Husk with whichever of the four M2-06 fields a row is about overridden, so a
    /// failure names one number rather than a constructor.
    /// </summary>
    private static EnemySpec Spec(
        EnemyBehaviourKind behaviour = EnemyBehaviourKind.Chaser,
        float aggroRange = 30f,
        ProjectileSpec projectile = null,
        ExplosionSpec explosion = null)
        => new EnemySpec(
            new ContentId("enemy.husk"),
            new LocKey("enemy.husk.name"),
            maxHp: 36f,
            moveSpeed: 2f,
            targetPriority: 1,
            threatCost: 4,
            isElite: false,
            contactDamage: 8f,
            reach: 1.2f,
            windupTime: 0.4f,
            recoverTime: 0.6f,
            aggroRange: aggroRange,
            behaviour: behaviour,
            projectile: projectile,
            explosion: explosion);
}
