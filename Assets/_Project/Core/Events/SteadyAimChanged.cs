namespace Soulvail.Core.Events;

/// <summary>The standing arrow count changed.</summary>
public readonly struct SteadyAimChanged
{
    public readonly int Stacks;
    public readonly int MaxStacks;

    public SteadyAimChanged(int stacks, int maxStacks)
    {
        Stacks = stacks;
        MaxStacks = maxStacks;
    }
}
