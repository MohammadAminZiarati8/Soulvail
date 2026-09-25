using System;

namespace Soulvail.Core.Content;

/// <summary>
/// A volley, as authored data: after every <em>n</em> shots the next one is a fan of arrows, each
/// dealing more. How many arrows, how wide the fan and how much more. Null on a class without one,
/// which is every class but the Ranger (RS-03b).
/// </summary>
/// <remarks>
/// <para>
/// <b>The <em>n</em> is not here.</b> It is <c>Volley.Every</c>, a <c>Stat</c> whose base is 0, so a
/// class with this block fires no volley until a node gives it one — the Ranger's Volley passive and
/// its two upgrades are three <c>ModifyStat</c> nodes (RS-03c). Authoring a count here as well would be
/// two places for one number.
/// </para>
/// <para>
/// Optional on <see cref="CharacterSpec"/> for <see cref="KindlingSpec"/>'s reason: a null says
/// <em>this class has no volley</em> in one word. Immutable and shared, for the reason
/// <see cref="WeaponSpec"/> gives. The live arrows and damage are <c>Volley.Arrows</c> and
/// <c>Volley.Damage</c>, seeded from these.
/// </para>
/// </remarks>
public sealed class VolleySpec
{
    /// <summary>
    /// The most arrows one volley may loose — and the length of <c>PlayerCombat</c>'s shot queue,
    /// which is why it is a constant rather than a number a designer picks (rule 6).
    /// </summary>
    public const int MaxArrows = 7;

    /// <summary>The fewest arrows a fan can have. One arrow is an ordinary shot.</summary>
    private const int MinArrows = 2;

    /// <summary>The widest fan, exclusive. At 180° the edge arrows fly sideways from the shooter.</summary>
    private const float MaxFanAngleDeg = 180f;

    /// <param name="arrows">How many arrows the fan holds, in <c>2..</c><see cref="MaxArrows"/>.</param>
    /// <param name="fanAngleDeg">
    /// The whole fan, edge arrow to edge arrow, in degrees: above 0 and below 180.
    /// </param>
    /// <param name="damageMultiplier">
    /// What each arrow deals, as a multiple of an ordinary shot: finite and at least 1. A volley that
    /// dealt less per arrow would be a penalty the player waited for.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Any of the three is out of range — rule 1.</exception>
    public VolleySpec(int arrows, float fanAngleDeg, float damageMultiplier)
    {
        if (arrows < MinArrows || arrows > MaxArrows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(arrows),
                arrows,
                $"arrows must be from {MinArrows} to {MaxArrows}. One arrow is an ordinary shot, and "
                    + $"{MaxArrows} is the length of the shot queue a damage frame writes into.");
        }

        // `!(x > 0f)` so NaN is refused with zero and below; NaN fails the upper test too, but the
        // lower one names the reason a designer can act on.
        if (!(fanAngleDeg > 0f) || !(fanAngleDeg < MaxFanAngleDeg))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fanAngleDeg),
                fanAngleDeg,
                $"fanAngleDeg must be above 0 and below {MaxFanAngleDeg}. A fan of 0° stacks every "
                    + "arrow on the target, and one of 180° or more flies its edge arrows sideways "
                    + "or backwards.");
        }

        if (!(damageMultiplier >= 1f) || float.IsInfinity(damageMultiplier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(damageMultiplier),
                damageMultiplier,
                "damageMultiplier must be a finite number, at least 1. Each arrow of a volley deals "
                    + "an ordinary shot's damage times this.");
        }

        Arrows = arrows;
        FanAngleDeg = fanAngleDeg;
        DamageMultiplier = damageMultiplier;
    }

    /// <summary>How many arrows the fan holds. 3 for the Ranger (RS-03c).</summary>
    public int Arrows { get; }

    /// <summary>The whole fan, edge arrow to edge arrow, in degrees. 30 for the Ranger.</summary>
    public float FanAngleDeg { get; }

    /// <summary>Each arrow's damage as a multiple of an ordinary shot's. 1.5 for the Ranger.</summary>
    public float DamageMultiplier { get; }
}
