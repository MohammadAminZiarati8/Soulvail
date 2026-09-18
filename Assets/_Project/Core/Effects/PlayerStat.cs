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
/// <b>Five of those arrived at M3-12a</b>, which is M3-05 rule 5's promise kept: the numbers it
/// deferred became addressable in the task whose nodes wanted them. <c>Weapon.Range</c> and
/// <c>Weapon.ConeAngleDeg</c> stopped being forwarded floats, <c>ChargeSkill.Damage</c> was
/// promoted off the spec at the line <c>PlayerCombat.ResolveChargeHits</c> had been flagging since
/// M1-15, <c>Health.ShieldRechargeDelay</c> became the one reachable Aegis number, and
/// <see cref="HealPerKill"/> was added outright.
/// </para>
/// <para>
/// <b>What is still deliberately not here</b>, each with the task that would claim it and the
/// reason it waits: <c>MovementSkillSpec.Knockback</c>, a positioning number that would need its
/// own playtest; <c>ShieldSpec.Max</c>, because raising a maximum without filling it is the trap
/// <c>Handler_MaxHpMovesHealthLive</c> pins; and <c>ShieldSpec.RefillPerSecond</c>, which is
/// Unbroken's keystone and should arrive whole rather than half-reachable.
/// </para>
/// <para>
/// <b>It stopped being only the player's at M4-01a, and the name is now the one thing about it
/// that lies.</b> <see cref="IStatBlock"/> made this the address space <em>every</em> combatant is
/// addressed in — the enemy mirror this paragraph used to promise was weighed and refused (M4-01a
/// rule 1), because two enums mean two dropdowns on <c>ModifyStatDefinition</c> and a designer who
/// has to know which. What that costs is <see cref="ContactDamage"/>: <b>the first member no
/// <see cref="PlayerStats"/> answers</b>, so <see cref="PlayerStats.Resolve"/>'s loud default is
/// now reachable by a legal member rather than only by a stale ordinal, and
/// <c>Stats_ResolveEveryMember</c> carries one named exception. Renaming the type to
/// <c>StatId</c> is the honest fix and is a <em>move</em> — every authored asset serialises this
/// enum by ordinal — so it is the owner's to rule (M4-01a's <em>Out of scope</em>).
/// </para>
/// <para>
/// <b>Ordinals are appended, never inserted.</b> <c>ModifyStatDefinition</c> serialises
/// <c>_stat</c> as the raw <see cref="int"/> (<c>_stat: 0</c> in every shipped asset), so
/// reordering this enum silently re-points every authored effect. <c>OnValidate</c> catches an
/// ordinal that is no longer a member; it cannot catch one that is now a <em>different</em>
/// member.
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

    /// <summary>
    /// How far the basic attack's arc reaches, in metres — <c>Weapon.Range</c>. Feeds the swing's
    /// wedge and the targeter's in-range test both, so a longer weapon also acquires sooner.
    /// </summary>
    WeaponRange,

    /// <summary>
    /// The full opening angle of the basic attack's arc, in degrees — <c>Weapon.ConeAngleDeg</c>,
    /// clamped into <c>[0, 360]</c> where the intent is built.
    /// </summary>
    WeaponConeAngle,

    /// <summary>What one pass-through of the dash deals — <c>ChargeSkill.Damage</c>.</summary>
    ChargeDamage,

    /// <summary>
    /// Seconds without being hit before the Aegis refills — <c>Health.ShieldRechargeDelay</c>, and
    /// the only one of its three numbers a node may name.
    /// </summary>
    ShieldRechargeDelay,

    /// <summary>
    /// Hit points restored per enemy killed — <c>PlayerCombat.HealPerKill</c>, base <b>zero</b>.
    /// </summary>
    /// <remarks>
    /// <b>A percentage modifier on this one does nothing</b>: a base of zero times any percentage
    /// is zero, so only <c>ModifierKind.Flat</c> can move it. See <c>PlayerCombat.HealPerKill</c>,
    /// which carries the full warning and names the node that has to obey it.
    /// </remarks>
    HealPerKill,

    /// <summary>
    /// What one strike of an enemy body costs — <c>EnemyAgent.ContactDamage</c>. <b>No player has
    /// one</b>, so <see cref="PlayerStats.Resolve"/> refuses it and only
    /// <see cref="CombatantStats"/> answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The member that makes this enum's name wrong</b>, and the price of one shared address
    /// space rather than two — see the remarks on the enum. It is last, and a new member goes after
    /// it, because the ordinal is what every authored asset stores.
    /// </para>
    /// <para>
    /// <b>A node that authors this does nothing and then throws</b>, at the moment a player picks
    /// it: it passes <c>Enum.IsDefined</c> at the authoring door and falls out of
    /// <see cref="PlayerStats.Resolve"/>'s default. That is the loud direction to fail in, and it
    /// is the same bargain M4-01a rule 2 makes on the other side — a Husk asked for
    /// <see cref="WeaponRange"/> refuses rather than inventing one.
    /// </para>
    /// </remarks>
    ContactDamage,
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
/// <para>
/// <b><see cref="IStatBlock"/> since M4-01a, and the edit was the declaration and one new method.</b>
/// <see cref="Resolve"/> is byte-for-byte the method the interface names and no existing address
/// moved — the widening is that <em>something else</em> can now be aimed at, not that this answers
/// differently. <see cref="Has"/> is the one addition, and it exists because a handler must be able
/// to ask before it applies rather than catch after.
/// </para>
/// </remarks>
public sealed class PlayerStats : IStatBlock
{
    private readonly PlayerCombat _combat;
    private readonly PlayerMotor _motor;
    private readonly LevelTracker _progression;

    /// <param name="combat">
    /// The player's health, weapon and dash — nine of the eleven addresses, because
    /// <c>PlayerCombat</c> is what owns the objects that own them, and as of M3-12a it owns one of
    /// the numbers itself.
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
            PlayerStat.WeaponRange => _combat.Weapon.Range,
            PlayerStat.WeaponConeAngle => _combat.Weapon.ConeAngleDeg,
            PlayerStat.ChargeDamage => _combat.Charge.Damage,
            PlayerStat.ShieldRechargeDelay => _combat.Health.ShieldRechargeDelay,
            PlayerStat.HealPerKill => _combat.HealPerKill,
            _ => throw new ArgumentOutOfRangeException(
                nameof(stat),
                stat,
                "No live stat is registered for this address. A new PlayerStat member needs a "
                    + "line here as well — see the enum's remarks. PlayerStat.ContactDamage is the "
                    + "one member that is meant to land here: it is an enemy body's number, and "
                    + "only CombatantStats answers it."),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The eleven the switch above answers, written out rather than derived from
    /// <c>Enum.IsDefined</c>: the enum holds one member this table deliberately does not have, so
    /// "is it a member" and "does the player have one" stopped being the same question at M4-01a.
    /// A row walks every member and asserts this agrees with <see cref="Resolve"/> exactly.
    /// </remarks>
    public bool Has(PlayerStat stat)
    {
        return stat switch
        {
            PlayerStat.MaxHp => true,
            PlayerStat.WeaponDamage => true,
            PlayerStat.FireRate => true,
            PlayerStat.MoveSpeed => true,
            PlayerStat.MovementSkillCooldown => true,
            PlayerStat.XpGain => true,
            PlayerStat.WeaponRange => true,
            PlayerStat.WeaponConeAngle => true,
            PlayerStat.ChargeDamage => true,
            PlayerStat.ShieldRechargeDelay => true,
            PlayerStat.HealPerKill => true,
            _ => false,
        };
    }
}
