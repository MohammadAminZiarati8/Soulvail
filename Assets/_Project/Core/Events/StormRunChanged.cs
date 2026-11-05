namespace Soulvail.Core.Events;

/// <summary>Storm Run began or ended, with the simulated time of its expiry or end.</summary>
public readonly struct StormRunChanged
{
    public StormRunChanged(bool active, float until)
    {
        Active = active;
        Until = until;
    }

    public bool Active { get; }
    public float Until { get; }
}
