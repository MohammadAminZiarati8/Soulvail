using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Core.Stage;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Stage;

public sealed partial class StageFlowTests
{
    [Test]
    public void Arrival_WithEntry_WalksToStart()
    {
        BeginEntry();
        Step(0.1f);

        Assert.That(_flow.TryEntryInput(_snapshot, out Vector2 input), Is.True);
        Assert.That(input, Is.EqualTo(Vector2.UnitY));
        Assert.That(_events.Count<StageEntrySealed>(), Is.Zero);
    }

    [Test]
    public void Arrival_ReachStart_SealsOnce()
    {
        BeginEntry();
        _snapshot.PlayerPosition = new Vector3(0f, 2f, -0.2f);
        Step(0.1f);
        Step(0.1f);

        Assert.That(_events.Single<StageEntrySealed>().Stage, Is.EqualTo(1));
        Assert.That(_flow.TryEntryInput(_snapshot, out _), Is.False);
    }

    [Test]
    public void Arrival_WalkTimesOut_Seals()
    {
        BeginEntry();
        Step(1.49f);
        Assert.That(_events.Count<StageEntrySealed>(), Is.Zero);

        Step(0.02f);
        Assert.That(_events.Single<StageEntrySealed>().Stage, Is.EqualTo(1));
        Assert.That(_flow.TryEntryInput(_snapshot, out _), Is.False);
    }

    [Test]
    public void Arrival_NoEntry_WavesAt2s()
    {
        Build(OneHuskStage());
        BeginAt(1);
        Step(0f);
        Assert.That(_events.Single<StageEntrySealed>().Stage, Is.EqualTo(1));

        Step(1.99f);
        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Arrival));
        Step(0.01f);
        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Waves));
    }

    [Test]
    public void Arrival_WavesAfterBeat()
    {
        BeginEntry();
        _snapshot.PlayerPosition = _snapshot.PlayerStart;
        Step(1.4f);
        Assert.That(_events.Count<StageEntrySealed>(), Is.EqualTo(1));

        _flow.Tick(2.39f, _snapshot, _spawn);
        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Arrival));
        _flow.Tick(2.4f, _snapshot, _spawn);
        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Waves));
    }

    [Test]
    public void Arrival_AllocatesNothing()
    {
        BeginEntry();
        Step(0.1f);

        AllocationAssert.None(() =>
        {
            _flow.Tick(_now, _snapshot, _spawn);
            _flow.TryEntryInput(_snapshot, out _);
        }, 100);
    }

    [Test]
    public void Session_EntrySteersMotorUntilSeal()
    {
        var intents = new RecordingIntents();
        RunSession session = Session(intents);
        session.Start(SessionConfig(1));
        WorldSnapshot snapshot = SessionSnapshot(0.1f, new Vector3(0f, 0f, -3f));
        snapshot.HasEntry = true;
        snapshot.MoveInput = Vector2.UnitX;

        session.Tick(snapshot);
        Assert.That(intents.LastPlayerMove.Velocity.Z, Is.Positive);
        Assert.That(intents.LastPlayerMove.Velocity.X, Is.Zero);

        snapshot.PlayerPosition = snapshot.PlayerStart;
        session.Tick(snapshot);
        Assert.That(intents.LastPlayerMove.Velocity.X, Is.Positive);
    }

    private void BeginEntry()
    {
        Build(OneHuskStage());
        _snapshot.HasEntry = true;
        _snapshot.PlayerPosition = new Vector3(0f, 0f, -3f);
        BeginAt(1);
    }
}
