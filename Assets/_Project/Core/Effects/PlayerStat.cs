using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Progression;

namespace Soulvail.Core.Effects;

// The address table: the closed set of player numbers an effect may name, and where each of them
// lives this run. The enum sits beside its resolver for `MovementSkillSpec.cs`' reason — a
// selector and the one thing that knows the full set are read together or not at all.

/// <summary>
/// Every player number that exists as a <see cref="Stat"/> in code, as an address an authored
/// effect can name.
/// </summary>
/// <remarks>
/// <para>
/// <b>A closed set, which is what makes an enum right here</b> (ADR-0010): these are not content.
/// Nothing inserts a member from an asset, nothing writes one to a save — a <see cref="ModifyStat"/>
/// is saved as the node that carries it, by <c>ContentId</c> — and a new member means new code on
/// the object that owns the number, not new data. Content identity stays a <c>ContentId</c>
/// everywhere it matters.
/// </para>
/// <para>
/// <b>Adding one is four edits and the suite checks you made all of them:</b> a <see cref="Stat"/>
/// on the core object that owns the number, a member here, a line in
/// <see cref="PlayerStats.Resolve"/>, and a row in <c>Stats_ResolveEveryMember</c> — which walks
/// this enum, so a member added without a resolver line fails in the suite rather than at the
/// moment a player picks the node.
/// </para>
/// <para>
/// <b>What is deliberately not here yet</b>, each waiting for the task whose node wants it (M3-05
/// rule 5): <c>Weapon.Range</c> and <c>Weapon.ConeAngleDeg</c>, which are forwarded floats by
/// <c>Weapon</c>'s own remark; <c>MovementSkillSpec.Damage</c> and <c>Knockback</c>, read off the
/// spec at the line <c>PlayerCombat.ResolveChargeHits</c> already flags; and the Aegis's three
/// numbers, authored on <c>ShieldSpec</c>. Each becomes a <see cref="Stat"/> on its owner the day
/// M3-12 authors the node that moves it — Wide Censure, Charge damage and Unbroken are the likely
/// three.
/// </para>
/// </remarks>
public enum PlayerStat
{
    /// <summary>The player's maximum hit points — <c>Health.MaxHp</c>.</summary>
    MaxHp,

    /// <summary>Damage per swing of the basic attack — <c>Weapon.Damage</c>.</summary>
    WeaponDamage,

    /// <summary>Swings per second — <c>Weapon.FireRate</c>, which M1-13's Focus ramp also moves.</summary>
    FireRate,

    /// <summary>Top movement speed in metres per second — <c>PlayerMotor.Speed</c>.</summary>
    MoveSpeed,

    /// <summary>
    /// Seconds between dashes — <c>ChargeSkill.Cooldown</c>, under M3-06's 40 % floor.
    /// </summary>
    MovementSkillCooldown,

    /// <summary>The multiplier on every experience grant — <c>LevelTracker.XpGain</c>.</summary>
    XpGain,
}

/// <summary>
/// Where each <see cref="PlayerStat"/> lives this run. Built once in <c>RunSession.Start</c> from
/// the live objects, held by every handler that has to reach a player number.
/// </summary>
/// <remarks>
/// <para>
/// It exists so that an effect can be authored data. A <see cref="ModifyStat"/> shared across every
/// run cannot hold a <see cref="Stat"/>, because a <see cref="Stat"/> belongs to a player who dies
/// with their run; it holds an <em>address</em>, and this is the one object that turns an address
/// into the run's actual number (AR §10.1).
/// </para>
/// <para>
/// It hands out live <see cref="Stat"/>s, which looks like the handle AR §18.2 refuses and is not:
/// a <see cref="Stat"/> is arithmetic with no <c>Tick</c> and nothing to advance twice, and putting
/// a modifier on one is precisely what a caller who reached this object came to do. The seal is one
/// layer out — <c>RunState.Effects</c> is <c>internal</c>, so nothing outside core can get here at
/// all.
/// </para>
/// <para>
/// Allocates nothing on <see cref="Resolve"/>: a jump table over an enum returning a reference it
/// already holds.
/// </para>
/// </remarks>
public sealed class PlayerStats
{
    private readonly PlayerCombat _combat;
    private readonly PlayerMotor _motor;
    private readonly LevelTracker _progression;

    /// <param name="combat">
    /// The player's health, weapon and dash — four of the six addresses, because
    /// <c>PlayerCombat</c> is what owns the objects that own them.
    /// </param>
    /// <param name="motor">The player's movement, which owns its own top speed.</param>
    /// <param name="progression">The run's level tracker, which owns the experience multiplier.</param>
    /// <exception cref="ArgumentNullException">Any of the three is null.</exception>
    public PlayerStats(PlayerCombat combat, PlayerMotor motor, LevelTracker progression)
    {
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _motor = motor ?? throw new ArgumentNullException(nameof(motor));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
    }

    /// <summary>
    /// The live <see cref="Stat"/> <paramref name="stat"/> addresses — the very instance, never a
    /// copy of its value.
    /// </summary>
    /// <remarks>
    /// A switch with a loud <c>default</c>, <c>Stat.Pool</c>'s shape and for its reason: a member
    /// added to <see cref="PlayerStat"/> without a line here would otherwise be silently dropped
    /// from every effect that names it, and a node the player took would do nothing at all.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="stat"/> is not a member this table knows.
    /// </exception>
    public Stat Resolve(PlayerStat stat)
    {
        return stat switch
        {
            PlayerStat.MaxHp => _combat.Health.MaxHp,
            PlayerStat.WeaponDamage => _combat.Weapon.Damage,
            PlayerStat.FireRate => _combat.Weapon.FireRate,
            PlayerStat.MoveSpeed => _motor.Speed,
            PlayerStat.MovementSkillCooldown => _combat.Charge.Cooldown,
            PlayerStat.XpGain => _progression.XpGain,
            _ => throw new ArgumentOutOfRangeException(
                nameof(stat),
                stat,
                "No live stat is registered for this address. A new PlayerStat member needs a "
                    + "line here as well — see the enum's remarks."),
        };
    }
}
