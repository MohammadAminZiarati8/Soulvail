using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;

namespace Soulvail.Core.Progression;

/// <summary>
/// What a level gained is worth on its own, before any pick: a mode-authored fraction of base
/// maximum hit points and weapon damage, and a heal by exactly the hit points it added (LV-01a).
/// One per run, built by <c>RunSession.Start</c>; it attaches itself to the run's
/// <see cref="LevelTracker"/>, which calls <see cref="Grow"/> once per level crossed and before it
/// publishes <c>LeveledUp</c>, so a handler reads the grown numbers.
/// </summary>
/// <remarks>
/// Two <c>PercentAdd</c> modifiers per level under one source (<see langword="this"/>), so thirty
/// levels pool with each other and with every node and Overflow percentage (ADR-0008) rather than
/// compounding. Skills that read <c>WeaponDamage</c> grow with it for free.
/// </remarks>
public sealed class LevelGrowth
{
    private readonly EffectRegistry _effects;
    private readonly Health _health;
    private readonly ModifyStat _maxHp;
    private readonly ModifyStat _damage;

    /// <param name="spec">The mode's growth; <c>default</c> grows nothing.</param>
    /// <param name="progression">The run's tracker, which this attaches to.</param>
    /// <param name="effects">Where the two modifiers go on.</param>
    /// <param name="health">The player's, healed by each level's gain.</param>
    /// <exception cref="ArgumentNullException">Any reference is null.</exception>
    public LevelGrowth(LevelGrowthSpec spec, LevelTracker progression, EffectRegistry effects, Health health)
    {
        if (progression is null)
        {
            throw new ArgumentNullException(nameof(progression));
        }

        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        Spec = spec;

        // Built once, for LevelUpFlow's Overflow reason: a pair per level would allocate on the
        // tick that crosses it.
        _maxHp = new ModifyStat(PlayerStat.MaxHp, ModifierKind.PercentAdd, spec.MaxHp);
        _damage = new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, spec.Damage);

        progression.Attach(this);
    }

    /// <summary>The mode's growth, as authored.</summary>
    public LevelGrowthSpec Spec { get; }

    /// <summary>How many levels' growth is on.</summary>
    public int Levels { get; private set; }

    /// <summary>
    /// One level's growth, and a heal by exactly the maximum it added. Silent: the caller is
    /// <c>LevelTracker.Grant</c>, whose <c>LeveledUp</c> is the announcement.
    /// </summary>
    internal void Grow()
    {
        float before = _health.MaxHp.Value;

        Apply();

        _health.Heal(_health.MaxHp.Value - before);
    }

    /// <summary>
    /// The resume path: every level above 1 grown at once, silently and without a heal — the save's
    /// hit points are restored after this by <c>Health.Restore</c> (AR §18.1), and a heal here would
    /// be undone there anyway.
    /// </summary>
    /// <param name="level">The restored level, from 1.</param>
    internal void Restore(int level)
    {
        for (int i = 1; i < level; i++)
        {
            Apply();
        }
    }

    private void Apply()
    {
        Levels++;
        _effects.Apply(_maxHp, this);
        _effects.Apply(_damage, this);
    }
}
