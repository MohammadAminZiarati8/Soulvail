namespace Soulvail.Core.Combat;

/// <summary>
/// What one call to <see cref="Health.ApplyDamage"/> actually did: how much the shield ate,
/// how much reached HP, whether anything got through at all, and whether it was fatal.
/// </summary>
/// <remarks>
/// <para>
/// A report, not a request — which is why nothing outside core can build one. It exists so
/// that <see cref="Health"/> can stay event-free: the owner of a health component
/// (<c>PlayerCombat</c> in M1-08, <c>EnemySystem</c> in M1-11) reads the result and decides
/// what to publish, because only the owner knows whether this is a <c>PlayerDamaged</c> or an
/// <c>EnemyDamaged</c> and what else belongs in it. See AR §3: core decides, and the same rule
/// applies inside core — a component this reusable should not be naming events.
/// </para>
/// <para>
/// <see cref="ToShield"/> is not merely informational. CH §3.1's Martyr keystone deals damage
/// equal to everything the Aegis absorbed, so a listener that sums this field over a shield's
/// lifetime is the whole implementation of it.
/// </para>
/// <para>
/// A <see langword="readonly"/> struct: <see cref="Health.ApplyDamage"/> is called from the
/// damage path of every hit in the game, and rule 9 requires it to allocate nothing.
/// </para>
/// </remarks>
public readonly struct DamageResult
{
    /// <summary>Damage absorbed by the shield.</summary>
    public readonly float ToShield;

    /// <summary>Damage that reached HP, after the shield took its share.</summary>
    public readonly float ToHp;

    /// <summary>
    /// The target was invulnerable and nothing was applied — hit i-frames, or the external flag
    /// a Charge raises. Distinct from a call that simply did nothing (see <see cref="None"/>):
    /// a blocked hit is a thing that happened, and views flash a shrug rather than a wound.
    /// </summary>
    public readonly bool Blocked;

    /// <summary>
    /// This call took HP to zero. True exactly once per life — a later hit on a dead target
    /// reports <see cref="None"/>, so the owner can publish a death event without guarding
    /// against publishing it twice.
    /// </summary>
    public readonly bool Killed;

    internal DamageResult(float toShield, float toHp, bool blocked, bool killed)
    {
        ToShield = toShield;
        ToHp = toHp;
        Blocked = blocked;
        Killed = killed;
    }

    /// <summary>Total damage this call applied, across shield and HP.</summary>
    public float Applied => ToShield + ToHp;

    /// <summary>
    /// Nothing happened: a non-positive amount, or a target that was already dead. Not the same
    /// as <see cref="Blocked"/>, which says the target was hit and shrugged it off.
    /// </summary>
    public static DamageResult None => default;
}
