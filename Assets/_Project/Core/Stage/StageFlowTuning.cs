using System;

namespace Soulvail.Core.Stage;

public sealed class StageFlowTuning
{
    public static StageFlowTuning Default { get; } = new();

    public float SlowMotionScale { get; }
    public float SlowMotionTime { get; }
    public float IdleHintTime { get; }
    public float IdleInputDeadzone { get; }

    public float EntryWalkMaxTime { get; }
    public float EntryArriveRadius { get; }
    public float EntryBeatTime { get; }

    public StageFlowTuning(
        float slowMotionScale = 0.3f,
        float slowMotionTime = 1f,
        float idleHintTime = 4f,
        float idleInputDeadzone = 0.1f,
        float entryWalkMaxTime = 1.5f,
        float entryArriveRadius = 0.3f,
        float entryBeatTime = 1f)
    {
        SlowMotionScale = Positive(slowMotionScale, nameof(slowMotionScale));
        SlowMotionTime = Positive(slowMotionTime, nameof(slowMotionTime));
        IdleHintTime = Positive(idleHintTime, nameof(idleHintTime));
        IdleInputDeadzone = Positive(idleInputDeadzone, nameof(idleInputDeadzone));
        EntryWalkMaxTime = Positive(entryWalkMaxTime, nameof(entryWalkMaxTime));
        EntryArriveRadius = Positive(entryArriveRadius, nameof(entryArriveRadius));
        EntryBeatTime = Positive(entryBeatTime, nameof(entryBeatTime));
    }

    private static float Positive(float value, string name) =>
        float.IsFinite(value) && value > 0f ? value : throw new ArgumentOutOfRangeException(name);
}
