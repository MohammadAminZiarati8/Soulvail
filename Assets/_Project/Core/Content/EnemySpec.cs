using System;

namespace Soulvail.Core.Content;

/// <summary>
/// Which behaviour drives an enemy. Selected by authored data and dispatched once, when the agent
/// is given its behaviour — see AR §9.
/// </summary>
/// <remarks>
/// A closed set, so an enum is the right shape: nothing is ever inserted into this from content,
/// which is the distinction <see cref="ContentId"/> draws between an ordinal and an identity.
/// An archetype is named by its <see cref="EnemySpec.Id"/>; this only says which code moves it.
/// <para>
/// Deliberately unvalidated by <see cref="EnemySpec"/>. The loud place for an unrecognised kind is
/// the dispatch that has to pick a behaviour for it (M1-06), which is the one site that knows the
/// full set — the same shape as <c>Stat.Pool</c>'s unreachable <c>default:</c> throw. A guard here
/// would only repeat that list in a second place, where adding a third kind and forgetting it
/// would reject the new archetype's data instead of pointing at the dispatch that cannot run it.
/// </para>
/// </remarks>
public enum EnemyBehaviourKind
{
    /// <summary>
    /// Stands where it was spawned and does nothing. The chaser dummies of M1 use this until
    /// M1-18 gives them an FSM, so the whole of targeting, cone hits and damage can be judged
    /// against something that holds still.
    /// </summary>
    Static,

    /// <summary>
    /// Beelines at the player and strikes in melee — GD §8.1's Husk. Implemented by
    /// <c>ChaserBehaviour</c> in M1-18.
    /// </summary>
    Chaser,
}

/// <summary>
/// One enemy archetype, as authored data: who it is, what it is worth killing, and the numbers its
/// behaviour plays with. Converted once at boot from an <c>EnemyDefinition</c> ScriptableObject
/// (M1-07) and registered in the <see cref="ContentCatalog"/>. See AR §10.1 and ADR-0006.
/// </summary>
/// <remarks>
/// <para>
/// Immutable and shared: one instance describes the archetype and is read by every living Husk at
/// once, so a mutable field here would be a global variable with a nice name. Runtime state lives
/// on <c>EnemyAgent</c> — current HP on its <c>Health</c>, everything else on its
/// <c>EnemyBlackboard</c>.
/// </para>
/// <para>
/// Raw <see cref="float"/>s rather than <c>Stat</c>s, for the reason <see cref="MovementSpec"/>
/// gives: this is what a designer typed. <see cref="MaxHp"/> seeds the agent's <c>Stat</c>, and the
/// depth scaling of GD §12 and the Elite affixes of M7-02 apply modifiers to that, never here.
/// </para>
/// <para>
/// <b>Five of the Husk's numbers are born in this type.</b> GD §8.1 publishes only its HP (36),
/// threat cost (4) and target priority (1); <see cref="MoveSpeed"/> 3.5,
/// <see cref="ContactDamage"/> 8, <see cref="Reach"/> 1.2, <see cref="WindupTime"/> 0.4 and
/// <see cref="RecoverTime"/> 0.6 appear in no design document, so nothing cross-checks them. They
/// are M1-05's, and M1-18's <c>ChaserBehaviour</c> reads them from here rather than restating them
/// — a second copy of a number no document owns is a number that will drift.
/// </para>
/// <para>
/// <b>Threat cost is deliberately absent.</b> GD §8.1 has one per archetype, but it is the
/// director's currency (M2-03, M2-04) rather than the enemy's own property, and a field with no
/// reader is a guess about what its reader will want. <c>TagSet</c> is absent for the same reason
/// until affixes need it (M7-02).
/// </para>
/// </remarks>
public sealed class EnemySpec
{
    /// <summary>The lowest legal <see cref="TargetPriority"/> — GD §8.1's Husk.</summary>
    private const int MinPriority = 1;

    /// <summary>The highest legal <see cref="TargetPriority"/> — GD §8.1's Choir.</summary>
    private const int MaxPriority = 8;

    /// <param name="id">The archetype's stable content id, e.g. <c>enemy.husk</c>.</param>
    /// <param name="nameKey">Localisation key for the display name.</param>
    /// <param name="maxHp">Starting maximum health. 36 for the Husk (GD §8.1).</param>
    /// <param name="moveSpeed">
    /// Top speed in m/s, or zero for something that does not move — which is what
    /// <see cref="EnemyBehaviourKind.Static"/> means, so zero is legal here in a way it is not on
    /// <see cref="MovementSpec.Speed"/>.
    /// </param>
    /// <param name="targetPriority">
    /// Archetype priority, 1–8 (GD §8.1). The knob that makes auto-aim feel intelligent: it
    /// dominates <c>TargetScorer</c>'s formula on purpose, so a Choir at 11 m outranks a Husk at
    /// 3 m. Husk 1, Choir 8.
    /// </param>
    /// <param name="isElite">Whether this archetype is an Elite (M7-02), worth the scorer's elite bonus.</param>
    /// <param name="contactDamage">
    /// Damage one strike deals. Zero is legal and means an enemy that never hurts the player
    /// directly — GD §8.1's Choir, which only heals and buffs.
    /// </param>
    /// <param name="reach">Metres within which a strike can land. 1.2 for the Husk.</param>
    /// <param name="windupTime">
    /// Seconds of telegraph before the damage frame. 0.4 for the Husk. Zero is legal and means an
    /// untelegraphed hit — never right for a melee enemy, but a contact explosion (the Bloater,
    /// M2-08) has no windup of its own.
    /// </param>
    /// <param name="recoverTime">Seconds of vulnerability after the strike. 0.6 for the Husk.</param>
    /// <param name="behaviour">Which behaviour drives it.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is <c>default(ContentId)</c>. Refused where the data is built rather
    /// than where it is read, for the reason <see cref="CharacterSpec"/> gives: a spec with no id
    /// would sit in the catalog under a key the catalog then reports as missing.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxHp"/> or <paramref name="reach"/> is not a finite number greater than
    /// zero; <paramref name="moveSpeed"/>, <paramref name="contactDamage"/>,
    /// <paramref name="windupTime"/> or <paramref name="recoverTime"/> is negative, NaN or
    /// infinite; or <paramref name="targetPriority"/> is outside 1–8.
    /// </exception>
    public EnemySpec(
        ContentId id,
        LocKey nameKey,
        float maxHp,
        float moveSpeed,
        int targetPriority,
        bool isElite,
        float contactDamage,
        float reach,
        float windupTime,
        float recoverTime,
        EnemyBehaviourKind behaviour)
    {
        if (id.Value is null)
        {
            throw new ArgumentException(
                "id must be a valid ContentId; default(ContentId) has none.",
                nameof(id));
        }

        if (targetPriority < MinPriority || targetPriority > MaxPriority)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetPriority),
                targetPriority,
                $"targetPriority must be between {MinPriority} and {MaxPriority} — GD §8.1's Husk "
                    + "to Choir. It is a designer's ranking, not an open scale: a value outside the "
                    + "range would out-shout or under-shout every archetype at once.");
        }

        Id = id;
        NameKey = nameKey;
        MaxHp = Positive(maxHp, nameof(maxHp));
        MoveSpeed = NonNegative(moveSpeed, nameof(moveSpeed));
        TargetPriority = targetPriority;
        IsElite = isElite;
        ContactDamage = NonNegative(contactDamage, nameof(contactDamage));
        Reach = Positive(reach, nameof(reach));
        WindupTime = NonNegative(windupTime, nameof(windupTime));
        RecoverTime = NonNegative(recoverTime, nameof(recoverTime));
        Behaviour = behaviour;
    }

    /// <summary>Stable identity, e.g. <c>enemy.husk</c>.</summary>
    public ContentId Id { get; }

    /// <summary>Localisation key for the display name — never the name itself.</summary>
    public LocKey NameKey { get; }

    /// <summary>Starting maximum health. Seeds the agent's <c>Stat</c>; 36 for the Husk.</summary>
    public float MaxHp { get; }

    /// <summary>Top speed in metres per second; zero for something that does not move.</summary>
    public float MoveSpeed { get; }

    /// <summary>Archetype priority, 1–8 (GD §8.1). Read by <c>TargetScorer</c> through a candidate.</summary>
    public int TargetPriority { get; }

    /// <summary>Whether this archetype is an Elite (M7-02).</summary>
    public bool IsElite { get; }

    /// <summary>Damage one strike deals; zero for an enemy that never attacks.</summary>
    public float ContactDamage { get; }

    /// <summary>Metres within which a strike can land.</summary>
    public float Reach { get; }

    /// <summary>Seconds of telegraph before the damage frame.</summary>
    public float WindupTime { get; }

    /// <summary>Seconds of vulnerability after the strike.</summary>
    public float RecoverTime { get; }

    /// <summary>Which behaviour drives it.</summary>
    public EnemyBehaviourKind Behaviour { get; }

    /// <remarks>
    /// <c>!(value &gt; 0f)</c> rather than <c>value &lt;= 0f</c>, so NaN is refused too: every
    /// comparison against NaN is false, and the natural spelling waves it through. Infinity is
    /// asked about separately because it passes a <c>&gt; 0</c> test — an infinite
    /// <see cref="MaxHp"/> is an enemy no amount of damage can kill, and an infinite
    /// <see cref="Reach"/> is one that strikes from across the arena.
    /// </remarks>
    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero.");
        }

        return value;
    }

    /// <remarks>
    /// One rung looser than <see cref="Positive"/>: zero is a legitimate value for all four of
    /// these and each means something specific — a stationary enemy, a harmless one, an
    /// untelegraphed strike, no recovery. Negative is not, and would read as a number rather than
    /// as the reversal it would cause. Infinity is refused for the same reason as above: an
    /// infinite windup never reaches its damage frame, and an infinite recovery never ends.
    /// </remarks>
    private static float NonNegative(float value, string paramName)
    {
        if (!(value >= 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number, zero or more.");
        }

        return value;
    }
}
