using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Run;

namespace Soulvail.Tests.Core.Run;

/// <summary>
/// Behaviour rule 5's half the compiler cannot keep: the constructor stores what it is given.
/// The other half — immutability — is enforced by <c>readonly struct</c>, where a mutation is a
/// compile error, so a test would only restate the language.
/// </summary>
[TestFixture]
public sealed class PlayerMoveIntentTests
{
    [Test]
    public void Ctor_StoresComponentsVerbatim()
    {
        // Deliberately not unit-length. Core has already decided the facing by the time it
        // builds an intent; a constructor that "helpfully" normalised would put that
        // computation in a second place, and a caller that scaled the vector on purpose would
        // never learn it had been overruled.
        var velocity = new Vector3(3f, 0f, -4f);
        var facing = new Vector3(0f, 0f, 5f);

        var intent = new PlayerMoveIntent(velocity, facing);

        Assert.That(intent.Velocity, Is.EqualTo(velocity));
        Assert.That(intent.Facing, Is.EqualTo(facing), "A normalising constructor would have stored (0, 0, 1).");

        // Nor does it validate. A zero facing is carried, not argued with: whether core may
        // emit one is M0-16's question, and a throw or a substituted default here would answer
        // it for every producer before the first producer exists.
        var still = new PlayerMoveIntent(Vector3.Zero, Vector3.Zero);

        Assert.That(still.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(still.Facing, Is.EqualTo(Vector3.Zero));
    }
}
