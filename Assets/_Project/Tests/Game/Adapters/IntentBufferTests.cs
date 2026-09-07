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
    public void WriteAndClear_AllocateNothing()
    {
        var intent = new PlayerMoveIntent(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f));

        AllocationAssert.None(() =>
        {
            _sink.PlayerMove(in intent);
            _buffer.Clear();
        });

        Assert.That(
            _buffer.HasPlayerMove,
            Is.False,
            "Sanity: the measured body really ran the whole write-then-clear cycle.");
    }
}
