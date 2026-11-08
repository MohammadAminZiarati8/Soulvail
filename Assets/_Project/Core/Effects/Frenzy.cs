using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

/// <summary>
/// BT-06a: every standing animal bites, runs and hits <c>FrenzyBonus</c> harder for
/// <c>FrenzySeconds</c>. A recast refreshes and keeps the larger bonus.
/// </summary>
public sealed class Frenzy : IEffect
{
}

public sealed class FrenzyHandler : IEffectHandler<Frenzy>, IEffectCastGuard<Frenzy>
{
    private readonly PetSystem _pets;
    private readonly PlayerStats _stats;
    private readonly SimulatedClock _clock;

    public FrenzyHandler(PetSystem pets, PlayerStats stats, SimulatedClock clock)
    {
        _pets = pets;
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool CanCast(Frenzy effect) => _pets?.HasStandingPet() == true;

    public void Apply(Frenzy effect, object source) =>
        _pets?.Frenzy(_stats.FrenzyBonus.Value, _stats.FrenzySeconds.Value, _clock.Now);

    public void Remove(Frenzy effect, object source)
    {
    }
}
