using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

public sealed class CallHawk : IEffect
{
    public CallHawk(float damageOfWeapon, float stunSeconds)
    {
        if (!(damageOfWeapon > 0f) || float.IsInfinity(damageOfWeapon))
        {
            throw new ArgumentOutOfRangeException(nameof(damageOfWeapon));
        }

        if (!(stunSeconds >= 0f) || float.IsInfinity(stunSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(stunSeconds));
        }

        DamageOfWeapon = damageOfWeapon;
        StunSeconds = stunSeconds;
    }

    public float DamageOfWeapon { get; }
    public float StunSeconds { get; }
}

public sealed class CallHawkHandler : IEffectHandler<CallHawk>, IEffectCastGuard<CallHawk>
{
    private readonly HawkSystem _hawk;
    private readonly SimulatedClock _clock;

    public CallHawkHandler(HawkSystem hawk, SimulatedClock clock)
    {
        _hawk = hawk ?? throw new ArgumentNullException(nameof(hawk));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool CanCast(CallHawk effect) => _hawk.CanCall;

    public void Apply(CallHawk effect, object source) =>
        _hawk.Call(_clock.Now, effect.DamageOfWeapon, effect.StunSeconds);

    public void Remove(CallHawk effect, object source)
    {
    }
}
