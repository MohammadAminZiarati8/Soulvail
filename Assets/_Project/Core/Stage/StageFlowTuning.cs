using System;

namespace Soulvail.Core.Stage;

public sealed class StageFlowTuning
{
    public static StageFlowTuning Default { get; } = new();

    public float SlowMotionScale { get; }
    public float SlowMotionTime { get; }
    public float IdleHintTime { get; }
    public float IdleInputDeadzone { get; }

    public StageFlowTuning(
        float slowMotionScale = 0.3f,
        float slowMotionTime = 1f,
        float idleHintTime = 4f,
        float idleInputDeadzone = 0.1f)
    {
        SlowMotionScale = Positive(slowMotionScale, nameof(slowMotionScale));
        SlowMotionTime = Positive(slowMotionTime, nameof(slowMotionTime));
        IdleHintTime = Positive(idleHintTime, nameof(idleHintTime));
        IdleInputDeadzone = Positive(idleInputDeadzone, nameof(idleInputDeadzone));
    }

    private static float Positive(float value, string name) =>
        float.IsFinite(value) && value > 0f ? value : throw new ArgumentOutOfRangeException(name);
}
