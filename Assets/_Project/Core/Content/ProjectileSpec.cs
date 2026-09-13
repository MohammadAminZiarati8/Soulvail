using System;

namespace Soulvail.Core.Content;

/// <summary>
/// What an archetype throws: where it stops to do it, how fast the shot flies, and how much of a
/// near miss still counts. GD §8.1's Spitter is the only instance in M2 — see M2-07a for the
/// projectile itself and M2-07b for the behaviour that fires one.
/// </summary>
/// <remarks>
/// <para>
/// Present on an archetype that fires and absent — <c>null</c>, not a block full of zeroes — on one
/// that does not, exactly as <see cref="ShieldSpec"/> is absent on a class without a shield. A null
/// block says <em>this archetype does not do this</em>; a zeroed one says nothing at all and has to
/// be read against <see cref="EnemyBehaviourKind"/> to be understood, which puts the meaning of the
/// data in a second place. That is why every field here is required to be positive: a projectile
/// that exists is a projectile that arrives somewhere.
/// </para>
/// <para>
/// <b>It carries no damage, and that omission is load-bearing.</b> The shot deals the agent's
/// <c>ContactDamage</c> stat — the one M2-03 put the depth curve's modifiers on — so GD §12.3's
/// scaling reaches a ranged attack for free, and an Elite affix (M7-02) will reach it the same way.
/// A <c>damage</c> field here would be a second number the depth curve does not know about, and the
/// failure would be silent: Spitters that stop mattering somewhere around stage 20, with every
/// individual number still reading correctly.
/// </para>
/// <para>
/// Immutable and shared, for the reason <see cref="EnemySpec"/> gives: one instance describes the
/// archetype and every living Spitter reads it at once.
/// </para>
/// </remarks>
public sealed class ProjectileSpec
{
    /// <param name="standoffRange">
    /// Metres at which it stops approaching and starts firing. 14 for the Spitter (GD §8.1) — well
    /// outside the Oathbound's 3 m cone, which is what makes a Spitter a problem to be closed with
    /// rather than a slower Husk.
    /// </param>
    /// <param name="speed">
    /// Metres per second of flight. With <paramref name="standoffRange"/> this is the dodge window:
    /// 12 m/s across 14 m is a shade under 1.2 s, long enough to react to and short enough that
    /// standing still is not free.
    /// </param>
    /// <param name="radius">
    /// Metres from the impact point that still count as a hit. Not the projectile's drawn size: it
    /// is the forgiveness on a shot, and the number M2-07a's overlap test is written against.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any value is not a finite number greater than zero. A zero <paramref name="speed"/> is a shot
    /// that never arrives, a zero <paramref name="radius"/> is one that can only hit a mathematical
    /// point, and a zero <paramref name="standoffRange"/> is an archer that walks into melee — all
    /// three are better said by passing no spec at all.
    /// </exception>
    public ProjectileSpec(float standoffRange, float speed, float radius)
    {
        StandoffRange = Positive(standoffRange, nameof(standoffRange));
        Speed = Positive(speed, nameof(speed));
        Radius = Positive(radius, nameof(radius));
    }

    /// <summary>Metres it stops at and fires from — 14 for the Spitter (GD §8.1).</summary>
    public float StandoffRange { get; }

    /// <summary>Metres per second of flight; with <see cref="StandoffRange"/>, the dodge window.</summary>
    public float Speed { get; }

    /// <summary>Metres from the impact point that still count as a hit.</summary>
    public float Radius { get; }

    /// <remarks>
    /// <c>!(value &gt; 0f)</c> rather than <c>value &lt;= 0f</c>, so NaN is refused too (AR §18.3):
    /// every comparison against NaN is false, and the natural spelling waves it through. A NaN
    /// <see cref="Speed"/> is a shot whose position stops being a number on its first step, and a
    /// NaN <see cref="Radius"/> makes every hit test answer <em>no</em> — silence rather than a
    /// visible fault, which is the failure mode <c>Stat</c> documents. Infinity is asked about
    /// separately because it passes a <c>&gt; 0</c> test: an infinite <see cref="Speed"/> arrives
    /// before it is drawn, and an infinite <see cref="StandoffRange"/> is an archer that never
    /// approaches at all.
    /// </remarks>
    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero. An archetype that throws " +
                "nothing carries no ProjectileSpec at all.");
        }

        return value;
    }
}

/// <summary>
/// What an archetype does when it goes off: how far the blast reaches. GD §8.1's Bloater is the
/// only instance in M2 — see M2-08 for the fuse that gets it there.
/// </summary>
/// <remarks>
/// Absent on an archetype that does not explode, and carrying no damage, for the two reasons
/// <see cref="ProjectileSpec"/> gives at length: a null block is the only unambiguous way to say
/// "not this one", and the blast deals the agent's <c>ContactDamage</c> so that depth scaling and
/// Elite affixes reach it without a second mechanism.
/// </remarks>
public sealed class ExplosionSpec
{
    /// <param name="radius">
    /// Metres the blast reaches. 3 for the Bloater (GD §8.1) — two and a half times its own 1.2 m
    /// body, so backing out of a lit fuse is a decision with a distance to it.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> is not a finite number greater than zero. A zero radius is an
    /// explosion that can only catch something standing on the exact point it went off at, which is
    /// better said by passing no spec at all.
    /// </exception>
    public ExplosionSpec(float radius)
    {
        Radius = Positive(radius, nameof(radius));
    }

    /// <summary>Metres the blast reaches — 3 for the Bloater (GD §8.1).</summary>
    public float Radius { get; }

    /// <remarks>
    /// <c>!(value &gt; 0f)</c> rather than <c>value &lt;= 0f</c>, so NaN is refused too (AR §18.3):
    /// every comparison against NaN is false, and the natural spelling waves it through. A NaN
    /// radius would spread into every distance test the blast is resolved by and make each of them
    /// answer <em>no</em> — an explosion that goes off and hits nobody, reported nowhere. Infinity
    /// is asked about separately because it passes a <c>&gt; 0</c> test, and an infinite blast is
    /// one that kills the player from the far side of the arena.
    /// </remarks>
    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero. An archetype that does " +
                "not explode carries no ExplosionSpec at all.");
        }

        return value;
    }
}
