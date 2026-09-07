using NUnit.Framework;

namespace Soulvail.Tests.Core.Fakes;

/// <summary>
/// Every later core test that involves a roll scripts it through <see cref="FixedRandom"/>, so
/// the fake itself has to be shown to hand back what it was given, in order — an unverified
/// fake would make every claim resting on it vacuous.
/// </summary>
[TestFixture]
public sealed class FixedRandomTests
{
    [Test]
    public void FixedRandom_ReturnsScriptedThenDefault()
    {
        var random = new FixedRandom(0.1f, 0.9f);

        Assert.That(random.Spawn.NextFloat(), Is.EqualTo(0.1f));
        Assert.That(random.Spawn.NextFloat(), Is.EqualTo(0.9f));

        // Exhausted, not wrapped: a test that under-scripts gets a defined middling value
        // rather than a silent replay of its own script.
        Assert.That(random.Spawn.NextFloat(), Is.EqualTo(0.5f));
    }

    [Test]
    public void FixedRandom_NextInt_MapsFloat()
    {
        var random = new FixedRandom(0.99f);

        Assert.That(random.Spawn.NextInt(0, 10), Is.EqualTo(9));
    }

    [Test]
    public void FixedRandom_SetStream_OverridesOnlyThatStream()
    {
        var random = new FixedRandom(0.5f, 0.75f).SetSpawn(0.25f);

        Assert.That(random.Spawn.NextFloat(), Is.EqualTo(0.25f), "Spawn was scripted on its own.");
        Assert.That(random.Offers.NextFloat(), Is.EqualTo(0.5f), "Every other stream keeps the shared script.");

        // The un-overridden streams are one instance, so Offers' draw above advanced Drops too.
        Assert.That(random.Drops.NextFloat(), Is.EqualTo(0.75f));

        // Spawn's own script was one value long, and the shared script it no longer uses is not
        // a fallback — it falls through to the default like any exhausted stream.
        Assert.That(random.Spawn.NextFloat(), Is.EqualTo(0.5f));
    }
}
