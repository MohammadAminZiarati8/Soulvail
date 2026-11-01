using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Ai;

/// <summary>
/// One agent's stun, root and slow timers (MK-06a). Owned by <see cref="EnemyAgent"/>; written only
/// through <see cref="EnemySystem"/>, which is also what makes the behaviours obey it — no behaviour
/// reads this.
/// </summary>
/// <remarks>
/// <para>
/// <b>A duration refreshes to the longer and never stacks</b> (rule 5). Stun and root are one timer
/// each. Slows keep a small table because the strongest <em>active</em> one wins (rule 4): a short
/// 0.5 over a long 0.3 is ×0.5 until it ends, then ×0.7. A slow that is both weaker and shorter than
/// another can never be the strongest active one, so it is dropped on arrival; that keeps the table
/// a staircase, and four slots is more distinct slows than any skill set applies at once. A full
/// table gives up its shortest entry.
/// </para>
/// <para>
/// The slow is one <see cref="ModifierKind.PercentMult"/> of <c>−fraction</c> on
/// <see cref="EnemyAgent.MoveSpeed"/>, sourced to this instance, so ending removes exactly it.
/// Nothing here allocates once the stat's list has held one modifier more than it otherwise would.
/// </para>
/// </remarks>
public sealed class CrowdControl
{
    /// <summary>The strongest slow there is: never a full stop, which is what a root is for.</summary>
    public const float MaxSlow = 0.9f;

    private const int SlowSlots = 4;

    private readonly Stat _moveSpeed;
    private readonly float[] _slowFraction = new float[SlowSlots];
    private readonly float[] _slowRemaining = new float[SlowSlots];

    private readonly object _hasteSource = new();
    private float _hasted;
    private float _hasteFraction;
    private int _slowCount;
    private float _stunned;
    private float _rooted;

    /// <summary>The fraction on <see cref="_moveSpeed"/> now; 0 when no modifier is on it.</summary>
    private float _applied;

    internal CrowdControl(Stat moveSpeed)
    {
        _moveSpeed = moveSpeed;
    }

    public bool IsStunned => _stunned > 0f;

    public bool IsRooted => _rooted > 0f;

    /// <summary>The strongest active slow, or 0.</summary>
    public float SlowFraction => _applied;

    public bool Has(EnemyStatus status) => Remaining(status) > 0f;

    /// <summary>Seconds left; for a slow, of the longest one.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not one of the three.</exception>
    public float Remaining(EnemyStatus status) => status switch
    {
        EnemyStatus.Stunned => _stunned,
        EnemyStatus.Rooted => _rooted,
        EnemyStatus.Slowed => LongestSlow(),
        EnemyStatus.Hasted => _hasted,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a crowd-control status."),
    };

    /// <returns>The new remaining when it started or lengthened, else 0 (nothing to announce).</returns>
    internal float Stun(float seconds) => Lengthen(ref _stunned, seconds);

    /// <returns>As <see cref="Stun"/>.</returns>
    internal float Root(float seconds) => Lengthen(ref _rooted, seconds);

    /// <returns>The longest slow's remaining when that grew, else 0.</returns>
    internal float Slow(float fraction, float seconds)
    {
        float before = LongestSlow();

        AddSlow(fraction, seconds);
        Apply();

        float after = LongestSlow();

        return after > before ? after : 0f;
    }

    /// <summary>Counts every timer down by <paramref name="dt"/> and announces what ended.</summary>
    internal float Haste(float fraction, float seconds)
    {
        float strongest = MathF.Max(fraction, _hasteFraction);
        if (strongest != _hasteFraction)
        {
            _moveSpeed.RemoveAll(_hasteSource);
            _moveSpeed.Add(new Modifier(ModifierKind.PercentMult, strongest, _hasteSource));
            _hasteFraction = strongest;
        }
        _hasted = MathF.Max(_hasted, seconds);
        return _hasted;
    }

    internal void Tick(float dt, int enemyId, IDomainEvents events)
    {
        if (Expire(ref _stunned, dt))
        {
            events.Publish(new EnemyStatusChanged(enemyId, EnemyStatus.Stunned, 0f));
        }

        if (Expire(ref _rooted, dt))
        {
            events.Publish(new EnemyStatusChanged(enemyId, EnemyStatus.Rooted, 0f));
        }

        if (Expire(ref _hasted, dt))
        {
            _moveSpeed.RemoveAll(_hasteSource);
            _hasteFraction = 0f;
            events.Publish(new EnemyStatusChanged(enemyId, EnemyStatus.Hasted, 0f));
        }

        if (_slowCount == 0)
        {
            return;
        }

        int write = 0;

        for (int read = 0; read < _slowCount; read++)
        {
            float remaining = _slowRemaining[read] - dt;

            if (!(remaining > 0f))
            {
                continue;
            }

            _slowFraction[write] = _slowFraction[read];
            _slowRemaining[write] = remaining;
            write++;
        }

        _slowCount = write;

        Apply();

        if (_slowCount == 0)
        {
            events.Publish(new EnemyStatusChanged(enemyId, EnemyStatus.Slowed, 0f));
        }
    }

    /// <summary>Everything off, silently: a death, a despawn, a recycle (rules 7–8).</summary>
    internal void Reset()
    {
        _moveSpeed.RemoveAll(_hasteSource);
        _hasted = 0f;
        _hasteFraction = 0f;
        _stunned = 0f;
        _rooted = 0f;
        _slowCount = 0;

        Apply();
    }

    private static float Lengthen(ref float remaining, float seconds)
    {
        if (!(seconds > remaining))
        {
            return 0f;
        }

        remaining = seconds;

        return seconds;
    }

    /// <returns>True on the tick it ran out.</returns>
    private static bool Expire(ref float remaining, float dt)
    {
        if (!(remaining > 0f))
        {
            return false;
        }

        remaining -= dt;

        if (remaining > 0f)
        {
            return false;
        }

        remaining = 0f;

        return true;
    }

    private void AddSlow(float fraction, float seconds)
    {
        for (int i = 0; i < _slowCount; i++)
        {
            if (_slowFraction[i] >= fraction && _slowRemaining[i] >= seconds)
            {
                return;
            }
        }

        int write = 0;

        for (int read = 0; read < _slowCount; read++)
        {
            if (_slowFraction[read] <= fraction && _slowRemaining[read] <= seconds)
            {
                continue;
            }

            _slowFraction[write] = _slowFraction[read];
            _slowRemaining[write] = _slowRemaining[read];
            write++;
        }

        _slowCount = write;

        int slot = _slowCount;

        if (slot == SlowSlots)
        {
            slot = 0;

            for (int i = 1; i < _slowCount; i++)
            {
                if (_slowRemaining[i] < _slowRemaining[slot])
                {
                    slot = i;
                }
            }
        }
        else
        {
            _slowCount++;
        }

        _slowFraction[slot] = fraction;
        _slowRemaining[slot] = seconds;
    }

    /// <summary>Puts the strongest active slow on the stat, touching it only when that changed.</summary>
    private void Apply()
    {
        float strongest = 0f;

        for (int i = 0; i < _slowCount; i++)
        {
            strongest = MathF.Max(strongest, _slowFraction[i]);
        }

        if (strongest == _applied)
        {
            return;
        }

        if (_applied > 0f)
        {
            _moveSpeed.RemoveAll(this);
        }

        if (strongest > 0f)
        {
            _moveSpeed.Add(new Modifier(ModifierKind.PercentMult, -strongest, this));
        }

        _applied = strongest;
    }

    private float LongestSlow()
    {
        float longest = 0f;

        for (int i = 0; i < _slowCount; i++)
        {
            longest = MathF.Max(longest, _slowRemaining[i]);
        }

        return longest;
    }
}
