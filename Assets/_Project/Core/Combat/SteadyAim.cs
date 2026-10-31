using System;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>Standing arrows build damage; movement gives up the stack and its guard.</summary>
public sealed class SteadyAim
{
    private readonly IDomainEvents _events;

    public SteadyAim(IDomainEvents events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public Stat PerStack { get; } = new(0f);
    public Stat StopCrit { get; } = new(0f);
    public Stat Guard { get; } = new(0f);
    public Stat MaxStacks { get; } = new(5f);
    public int Stacks { get; private set; }

    public float IncomingMultiplier => PerStack.Value > 0f && Cap > 0 && Stacks == Cap
        ? 1f - Math.Clamp(Guard.Value, 0f, 1f) : 1f;

    private int Cap => !(MaxStacks.Value > 0f) ? 0
        : MaxStacks.Value >= int.MaxValue ? int.MaxValue : (int)MaxStacks.Value;

    public float OnLoose() => OnLoose(out _);

    public float OnLoose(out bool isCrit)
    {
        isCrit = false;
        if (!(PerStack.Value > 0f))
        {
            return 1f;
        }

        float multiplier = 1f + (PerStack.Value * Stacks);
        isCrit = Stacks == 0 && StopCrit.Value > 0f;
        if (isCrit)
        {
            multiplier *= StopCrit.Value;
        }

        SetStacks(Stacks < Cap ? Stacks + 1 : Cap);
        return multiplier;
    }

    public void OnMoved() => SetStacks(0);

    private void SetStacks(int value)
    {
        if (Stacks == value)
        {
            return;
        }

        Stacks = value;
        _events.Publish(new SteadyAimChanged(Stacks, Cap));
    }
}
