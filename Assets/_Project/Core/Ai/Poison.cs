using System;

namespace Soulvail.Core.Ai;

/// <summary>
/// One agent's poison (ST-03a): stacks that share a timer and a pulse clock. Owned by
/// <see cref="EnemyAgent"/>; written only through <see cref="EnemySystem"/>, which turns each pulse
/// into damage through the normal door.
/// </summary>
public sealed class Poison
{
    public const int MaxStacks = 5;

    public const float PulseSeconds = 0.5f;

    private float _pulseClock;

    public int Stacks { get; private set; }

    public float Remaining { get; private set; }

    /// <summary>The strongest applied during this poisoning; 0 while clean.</summary>
    public float DamagePerStackPerSecond { get; private set; }

    /// <summary>What the last pulse (or the pulses of the last <see cref="Tick"/>) dealt each.</summary>
    internal float PulseDamage { get; private set; }

    /// <returns>True when the stack count or the remaining time grew.</returns>
    internal bool Apply(float damagePerStackPerSecond, float seconds, int maxStacks = MaxStacks)
    {
        bool grew = false;

        if (Stacks == 0)
        {
            _pulseClock = 0f;
            DamagePerStackPerSecond = 0f;
        }

        if (Stacks < maxStacks)
        {
            Stacks++;
            grew = true;
        }

        if (damagePerStackPerSecond > DamagePerStackPerSecond)
        {
            DamagePerStackPerSecond = damagePerStackPerSecond;
        }

        if (seconds > Remaining)
        {
            Remaining = seconds;
            grew = true;
        }

        return grew;
    }

    /// <summary>
    /// Counts the timer down and works out how many pulses came due. A pulse the timer ends before
    /// never fires.
    /// </summary>
    /// <param name="pulses">Pulses due, each worth <see cref="PulseDamage"/>.</param>
    /// <returns>True on the tick the poison ran out; the stacks are cleared by then.</returns>
    internal bool Tick(float dt, out int pulses)
    {
        pulses = 0;

        if (Stacks == 0)
        {
            return false;
        }

        _pulseClock += MathF.Min(dt, Remaining);
        Remaining -= dt;

        while (_pulseClock >= PulseSeconds)
        {
            _pulseClock -= PulseSeconds;
            pulses++;
        }

        if (pulses > 0)
        {
            PulseDamage = Stacks * DamagePerStackPerSecond * PulseSeconds;
        }

        if (Remaining > 0f)
        {
            return false;
        }

        Reset();

        return true;
    }

    /// <summary>Everything off, silently: a death, a despawn, a recycle.</summary>
    internal void Reset()
    {
        Stacks = 0;
        Remaining = 0f;
        DamagePerStackPerSecond = 0f;
        _pulseClock = 0f;
    }
}
