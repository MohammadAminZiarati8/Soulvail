using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Core.Stage;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Stage;

public sealed partial class StageFlowTests
{
    [Test]
    public void Clear_SlowMotionAfterCleared()
    {
        BeginClearGuideStage();

        Assert.That(IndexOf<StageCleared>(), Is.GreaterThanOrEqualTo(0));
        Assert.That(IndexOf<SlowMotionStarted>(), Is.EqualTo(IndexOf<StageCleared>() + 1));
        Assert.That(_events.Single<SlowMotionStarted>().Scale, Is.EqualTo(0.3f));
    }

    [Test]
    public void SlowMotion_EndsAfterScaledTime()
    {
        BeginClearGuideStage();

        Step(0.29f);
        Assert.That(_events.Count<SlowMotionEnded>(), Is.Zero);
        Step(0.02f);
        Step(0.02f);
        Assert.That(_events.Count<SlowMotionEnded>(), Is.EqualTo(1));
    }

    [Test]
    public void SlowMotion_LastStageToo()
    {
        Build(Mode(budget: HuskCost, waves: 1, concurrency: DeviceCap, endless: false, finalStage: 3));
        BeginAt(3);
        ClearTheStage();

        Assert.That(_flow.IsModeComplete, Is.True);
        Assert.That(_events.Count<SlowMotionStarted>(), Is.EqualTo(1));
    }

    [Test]
    public void SlowMotion_AdvanceEndsIt()
    {
        BeginClearGuideStage(new StageFlowTuning(slowMotionTime: 100f));
        OpenTheDoor();
        _events.Clear();
        _snapshot.PlayerPosition = Door;
        Step(0.1f);

        Assert.That(_events.Count<SlowMotionEnded>(), Is.Zero);
        Step(StageFlow.FadeTime + 0.01f);

        Assert.That(_events.Count<SlowMotionEnded>(), Is.EqualTo(1));
        Assert.That(IndexOf<SlowMotionEnded>(), Is.LessThan(IndexOf<StageArrived>()));
    }

    [Test]
    public void Gate_IdleFourSeconds_HintStrong()
    {
        BeginClearGuideStage();
        OpenTheDoor();

        Step(3.99f);
        Assert.That(_events.Count<GateHintChanged>(), Is.Zero);
        Step(0.02f);
        Step(1f);

        Assert.That(_events.Single<GateHintChanged>().Strong, Is.True);
    }

    [Test]
    public void Gate_MoveAfterHint_Calm()
    {
        BeginStrongHint();
        _events.Clear();
        _snapshot.MoveInput = Vector2.UnitX;

        Step(0.1f);
        Step(0.1f);
        _snapshot.MoveInput = Vector2.Zero;
        Step(3.9f);

        Assert.That(_events.Single<GateHintChanged>().Strong, Is.False);
    }

    [Test]
    public void Gate_Transition_ClearsHint()
    {
        BeginStrongHint();
        _events.Clear();
        _snapshot.PlayerPosition = Door;

        Step(0.1f);

        Assert.That(_events.Single<GateHintChanged>().Strong, Is.False);
        Assert.That(IndexOf<GateHintChanged>(), Is.LessThan(IndexOf<StageTransitionStarted>()));
    }

    [Test]
    public void ClearAndGate_AllocateNothing()
    {
        BeginClearGuideStage();
        _snapshot.Dt = 0f;
        _flow.Tick(_now, _snapshot, _spawn);
        AllocationAssert.None(() => _flow.Tick(_now, _snapshot, _spawn), 1_000);

        OpenTheDoor();
        _snapshot.Dt = 0f;
        _flow.Tick(_now, _snapshot, _spawn);
        AllocationAssert.None(() => _flow.Tick(_now, _snapshot, _spawn), 1_000);
    }

    private void BeginClearGuideStage(StageFlowTuning tuning = null)
    {
        Build(OneHuskStage());
        _flow = Flow(tuning);
        BeginAt(1);
        _events.Clear();
        ClearTheStage();
    }

    private void BeginStrongHint()
    {
        BeginClearGuideStage();
        OpenTheDoor();
        Step(4f);
    }
}
