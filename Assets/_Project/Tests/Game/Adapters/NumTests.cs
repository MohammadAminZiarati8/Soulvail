using NUnit.Framework;
using Soulvail.Game.Adapters;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// Every vector is fully qualified here for the same reason it is in <see cref="Num"/>: with
/// a <c>using</c> for either namespace, a broken conversion and a correct one look identical.
/// The values are all exactly representable as floats, so equality can be exact.
/// </summary>
[TestFixture]
public sealed class NumTests
{
    [Test]
    public void Num_Vector3_RoundTrips()
    {
        var original = new UnityEngine.Vector3(1.5f, -2f, 3f);

        System.Numerics.Vector3 converted = original.ToNum();

        Assert.That(converted.X, Is.EqualTo(1.5f), "Components must map in order, not by name or luck.");
        Assert.That(converted.Y, Is.EqualTo(-2f));
        Assert.That(converted.Z, Is.EqualTo(3f));

        UnityEngine.Vector3 roundTripped = converted.ToUnity();

        Assert.That(roundTripped.x, Is.EqualTo(original.x));
        Assert.That(roundTripped.y, Is.EqualTo(original.y));
        Assert.That(roundTripped.z, Is.EqualTo(original.z));
    }

    [Test]
    public void Num_Vector2_RoundTrips()
    {
        var original = new UnityEngine.Vector2(0.25f, -1f);

        System.Numerics.Vector2 converted = original.ToNum();

        Assert.That(converted.X, Is.EqualTo(0.25f));
        Assert.That(converted.Y, Is.EqualTo(-1f));

        UnityEngine.Vector2 roundTripped = converted.ToUnity();

        Assert.That(roundTripped.x, Is.EqualTo(original.x));
        Assert.That(roundTripped.y, Is.EqualTo(original.y));
    }

    [Test]
    public void Num_ToNumXZ_DropsY()
    {
        var world = new UnityEngine.Vector3(1f, 99f, 2f);

        System.Numerics.Vector2 ground = world.ToNumXZ();

        Assert.That(ground.X, Is.EqualTo(1f));

        // Z, not Y: the ground plane is XZ in Unity, and picking up Y here would send everything
        // core steers sideways into the sky.
        Assert.That(ground.Y, Is.EqualTo(2f));
    }
}
