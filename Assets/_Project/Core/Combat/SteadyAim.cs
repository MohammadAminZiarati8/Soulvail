using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>Standing arrows build damage; movement gives up the stack and its guard.</summary>
public sealed class SteadyAim
{
    public const string StatusKey = "status.steady-aim";
    public const string GuardStatusKey = "status.steady-aim.guard";

    private readonly IDomainEvents _events;
    private readonly ContentId _statusId = new(StatusKey);
    private readonly ContentId _guardStatusId = new(GuardStatusKey);
    private StatusBoard _status;

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

    /// <summary>Mirrors the stacks and the guard onto the player's status row (MK-05).</summary>
    public void AttachStatus(StatusBoard status)
    {
        _status = status ?? throw new ArgumentNullException(nameof(status));
        SyncStatus();
    }

    private void SetStacks(int value)
    {
        // Synced even when unchanged: a Guard rank bought at full stacks shows on the next arrow.
        bool changed = Stacks != value;
        Stacks = value;
        SyncStatus();
        if (!changed)
        {
            return;
        }

        _events.Publish(new SteadyAimChanged(Stacks, Cap));
    }

    private void SyncStatus()
    {
        if (_status is null)
        {
            return;
        }

        if (Stacks > 0)
        {
            _status.Post(_statusId, true, float.PositiveInfinity, Stacks);
        }
        else
        {
            _status.Clear(_statusId);
        }

        if (Guard.Value > 0f && IncomingMultiplier < 1f)
        {
            _status.Post(_guardStatusId, true, float.PositiveInfinity, 1);
        }
        else
        {
            _status.Clear(_guardStatusId);
        }
    }
}
