using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// Every assertion reads through the buffer rather than through a local copy of the intent.
/// A copy would be a faithful reading today — the property returns by value — but M0-05's
/// ref-returning <c>AddEnemy</c> showed how fast that stops being true when an accessor changes
/// shape, and the assertion that names the buffer is the one that keeps testing the buffer.
/// </summary>
[TestFixture]
public sealed class IntentBufferTests
{
    private IntentBuffer _buffer;

    /// <summary>The same instance as <see cref="_buffer"/>, seen from core's side of the port.</summary>
    private IIntentSink _sink;

    [SetUp]
    public void SetUp()
    {
        _buffer = new IntentBuffer();

        // Writing is an explicit implementation, so it is reachable only through the port —
        // which is exactly how core will hold it.
        _sink = _buffer;
    }

    [Test]
    public void Fresh_HasNoPlayerMove()
    {
        Assert.That(
            _buffer.HasPlayerMove,
            Is.False,
            "Nothing has been written, so there is nothing for a view to apply.");
    }

    [Test]
    public void PlayerMove_StoresIntent()
    {
        _sink.PlayerMove(new PlayerMoveIntent(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f)));

        Assert.That(_buffer.HasPlayerMove, Is.True);
        Assert.That(_buffer.PlayerMove.Velocity, Is.EqualTo(new Vector3(1f, 0f, 0f)));
        Assert.That(
            _buffer.PlayerMove.Facing,
            Is.EqualTo(new Vector3(0f, 0f, 1f)),
            "Velocity and facing are separate values and must not be conflated on the way in.");
    }

    [Test]
    public void PlayerMove_Twice_LastWins()
    {
        _sink.PlayerMove(new PlayerMoveIntent(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f)));
        _sink.PlayerMove(new PlayerMoveIntent(new Vector3(0f, 0f, -2f), new Vector3(-1f, 0f, 0f)));

        Assert.That(_buffer.HasPlayerMove, Is.True);

        // The session emits one per tick, so this never happens in practice. The rule exists so
        // that if it ever does, the answer is defined rather than "whichever the buffer merged".
        Assert.That(_buffer.PlayerMove.Velocity, Is.EqualTo(new Vector3(0f, 0f, -2f)));
        Assert.That(_buffer.PlayerMove.Facing, Is.EqualTo(new Vector3(-1f, 0f, 0f)));
    }

    [Test]
    public void Clear_ResetsFlag()
    {
        _sink.PlayerMove(new PlayerMoveIntent(Vector3.UnitX, Vector3.UnitZ));
        Assert.That(_buffer.HasPlayerMove, Is.True, "Sanity: there is something to clear.");

        _buffer.Clear();

        Assert.That(_buffer.HasPlayerMove, Is.False);

        // The stored intent is deliberately asserted neither way. Clear leaves it alone today,
        // but the contract is that PlayerMove is *undefined* while the flag is down — pinning
        // the leftover here would turn an implementation detail into a promise.
    }

    [Test]
    public void Fresh_HasNoConeHits()
    {
        Assert.That(_buffer.ConeHits, Is.Empty, "Most ticks have no swing in them — three a second do.");
    }

    [Test]
    public void ConeHit_Accumulates_InOrder()
    {
        _sink.ConeHit(Cone(1));
        _sink.ConeHit(Cone(2));

        // Accumulated, not overwritten, and this is the one place the two intents differ. A move
        // intent is a state and only the newest matters; a cone is a question the body owes an
        // answer to, so dropping one because a second arrived in the same tick would lose a whole
        // swing's damage. M1-15's Charge is the first thing that can produce two.
        Assert.That(_buffer.ConeHits.Count, Is.EqualTo(2));
        Assert.That(_buffer.ConeHits[0].RequestId, Is.EqualTo(1));
        Assert.That(_buffer.ConeHits[1].RequestId, Is.EqualTo(2));
        Assert.That(_buffer.ConeHits[0].Range, Is.EqualTo(8f));
        Assert.That(_buffer.ConeHits[0].AngleDeg, Is.EqualTo(60f));
    }

    [Test]
    public void Clear_EmptiesConeHits()
    {
        _sink.ConeHit(Cone(1));
        Assert.That(_buffer.ConeHits, Is.Not.Empty, "Sanity: there is something to clear.");

        _buffer.Clear();

        // Genuinely emptied, where HasPlayerMove is only lowered — a list has no flag to check
        // first, so leftovers would read as this tick's swings and the body would sweep the same
        // cone every frame until the next one replaced it.
        Assert.That(_buffer.ConeHits, Is.Empty);
    }

    [Test]
    public void WriteAndClear_AllocateNothing()
    {
        var intent = new PlayerMoveIntent(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f));
        ConeHitIntent cone = Cone(1);

        AllocationAssert.None(() =>
        {
            _sink.PlayerMove(in intent);
            _sink.ConeHit(in cone);
            _buffer.Clear();
        });

        Assert.That(
            _buffer.HasPlayerMove,
            Is.False,
            "Sanity: the measured body really ran the whole write-then-clear cycle.");

        // The cone list is preallocated and Clear keeps its capacity, so the add above never grows
        // an array — which is what makes the measurement above mean anything.
        Assert.That(_buffer.ConeHits, Is.Empty);
    }

    /// <summary>The Censer's wedge (CC §7), swung from the origin along +Z.</summary>
    private static ConeHitIntent Cone(int requestId) => new ConeHitIntent(
        requestId,
        Vector3.Zero,
        new Vector2(0f, 1f),
        8f,
        60f);
}
