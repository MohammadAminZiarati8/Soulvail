using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Effects;

/// <summary>Starts a timed window in which rolls fire a free volley.</summary>
public sealed class StormRun : IEffect
{
    public const int MaxArrows = 8;

    public StormRun(float damageOfWeapon)
    {
        if (!(damageOfWeapon > 0f) || float.IsInfinity(damageOfWeapon))
        {
            throw new ArgumentOutOfRangeException(nameof(damageOfWeapon));
        }

        DamageOfWeapon = damageOfWeapon;
    }

    public float DamageOfWeapon { get; }
}

internal sealed class StormRunHandler : IEffectHandler<StormRun>
{
    private readonly ContentId _statusId = new("status.storm-run");
    private readonly PlayerCombat _combat;
    private readonly PlayerStats _stats;
    private readonly TimedEffects _timed;
    private readonly SimulatedClock _clock;
    private StatusBoard _status;
    private readonly IDomainEvents _events;
    private StormRun _effect;
    private object _source;
    private float _until;

    internal StormRunHandler(PlayerCombat combat, PlayerStats stats, TimedEffects timed,
        SimulatedClock clock, IDomainEvents events)
    {
        _combat = combat;
        _stats = stats;
        _timed = timed;
        _clock = clock;
        _events = events;
        _combat.Charge.Cooldown.ReserveModifierSlot(this);
    }

    internal void AttachStatus(StatusBoard status) => _status = status;

    internal bool IsActive => _effect is not null;

    internal float DamageMultiplier => _effect.DamageOfWeapon * (1f + _stats.StormRunDamage.Value);

    public void Apply(StormRun effect, object source)
    {
        float duration = _stats.StormRunSeconds.Value;
        float until = _clock.Now + duration;
        if (_combat.IsDead || !(duration > 0f) || !float.IsFinite(until))
        {
            return;
        }

        if (IsActive && (!ReferenceEquals(_effect, effect) || !ReferenceEquals(_source, source)))
        {
            Clear();
        }

        _timed.Hold(effect, source, until);
        if (!IsActive)
        {
            _combat.Charge.Cooldown.Add(new Modifier(ModifierKind.PercentMult, -0.5f, this));
        }

        _effect = effect;
        _source = source;
        _until = until;
        _status.Post(_statusId, isBuff: true, duration, stacks: 1);
        _events.Publish(new StormRunChanged(true, until));
    }

    public void Remove(StormRun effect, object source)
    {
        if (!IsActive || !ReferenceEquals(_effect, effect) || !ReferenceEquals(_source, source))
        {
            return;
        }

        _effect = null;
        _source = null;
        _timed.Release(effect, source);
        _combat.Charge.Cooldown.RemoveAll(this);
        _status.Clear(_statusId);
        _events.Publish(new StormRunChanged(false, _clock.Now));
    }

    // Combat runs before the timed-effects pass. A roll starting at the deadline uses normal cooldown.
    internal void ExpireBeforeRoll(float now)
    {
        if (IsActive && now >= _until)
        {
            Clear();
        }
    }

    internal void Clear()
    {
        if (IsActive)
        {
            _timed.Release(_effect, _source);
        }
    }
}
