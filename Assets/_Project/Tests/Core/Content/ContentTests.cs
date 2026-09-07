using System;
using NUnit.Framework;
using Soulvail.Core.Content;

namespace Soulvail.Tests.Core.Content;

/// <summary>
/// The Content module's fixture: one module, one test file. <see cref="MovementSpec"/> is the
/// module's first type; M0-08 adds <c>ContentId</c>, <c>LocKey</c>, <c>CharacterSpec</c> and
/// <c>ContentCatalog</c> here, which is the arrangement its Files table already calls for.
/// </summary>
[TestFixture]
public sealed class ContentTests
{
    [Test]
    public void Spec_NonPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(0f, 0.06f, 0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0f, 0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0.06f, 0f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0.06f, 0.08f, 0f));

        // Negative is the same mistake as zero, and the two times are divisors — a negative
        // accel time yields a negative rate, which drives the velocity away from its target.
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(-5.4f, 0.06f, 0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, -0.06f, 0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0.06f, -0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0.06f, 0.08f, -720f));

        // NaN has to be rejected too, and it is the case the obvious `value <= 0f` guard lets
        // through: every comparison against NaN is false. One NaN reaching the motor turns its
        // velocity and facing to NaN on the first tick, and NaN survives all later arithmetic —
        // the character would never move again, with nothing in the log to say why.
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(float.NaN, 0.06f, 0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, float.NaN, 0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0.06f, float.NaN, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0.06f, 0.08f, float.NaN));

        // The Oathbound's own numbers (CC §7) must pass, or every guard above would be satisfied
        // by a constructor that rejected everything.
        Assert.DoesNotThrow(() => new MovementSpec(5.4f, 0.06f, 0.08f, 720f));
    }

    [Test]
    public void Spec_StoresValues()
    {
        var spec = new MovementSpec(5.4f, 0.06f, 0.08f, 720f);

        Assert.That(spec.Speed, Is.EqualTo(5.4f));
        Assert.That(spec.AccelTime, Is.EqualTo(0.06f));
        Assert.That(spec.DecelTime, Is.EqualTo(0.08f));
        Assert.That(spec.TurnSpeedDeg, Is.EqualTo(720f));
    }
}
