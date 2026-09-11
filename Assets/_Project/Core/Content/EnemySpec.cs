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

    /// <summary>
    /// Keeps its distance and throws — GD §8.1's Spitter. Implemented in M2-07b, and
    /// <b>nothing authors this until then</b>: <c>Spitter.asset</c> ships <see cref="Static"/>
    /// with its <see cref="EnemySpec.Projectile"/> block already filled in, and M2-07b flips the
    /// one field in the PR that can run it. A placeholder case in the dispatch would make a
    /// Spitter that ignores the player silent instead of loud (M2-06 rule 11).
    /// </summary>
    Spitter,

    /// <summary>
    /// Waddles in, lights a fuse, goes off — GD §8.1's Bloater. Implemented in M2-08, and
    /// unauthored until then for the reason <see cref="Spitter"/> is.
    /// </summary>
    Bloater,
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
/// <b><see cref="AggroRange"/> arrived in M2-06</b>, off <c>ChaserBehaviour</c>'s
/// <c>public const AggroRange = 30f</c>, which predicted the move in its own remarks: <em>"the day
/// an archetype wants to be genuinely unaware until approached, this moves onto the spec with
/// it."</em> Two behaviours now need it, and a Spitter reaching into the chaser's class for a
/// constant would be the wrong dependency in the wrong direction. All three authored archetypes
/// carry 30, so the move changed nothing on screen.
/// </para>
/// <para>
/// <b>An enemy has two optional blocks as of M2-06, where this paragraph used to say it had
/// none.</b> <see cref="Projectile"/> and <see cref="Explosion"/> are <see langword="null"/> on an
/// archetype that throws nothing or does not explode — the bargain <see cref="CharacterSpec"/>
/// makes with its <see cref="ShieldSpec"/>, and for the same reason: a null block says <em>not this
/// one</em> where a zeroed block says nothing at all. Neither carries damage; both deal
/// <see cref="ContactDamage"/>, so GD §12.3's depth curve reaches them without a second mechanism.
/// </para>
/// <para>
/// <b><see cref="ThreatCost"/> arrived in M2-04, with its first reader.</b> It was deliberately
/// absent until then — GD §8.1 has one per archetype, but it is the director's currency rather
/// than the enemy's own property, and a field with no reader is a guess about what its reader will
/// want. <c>WaveComposer</c> is that reader, and it settled the question the other way: a cost is
/// a fact about the creature, which is why it sits here while the depth an archetype is
/// <em>allowed</em> at stays on the mode's <see cref="RosterEntry"/>. <c>TagSet</c> is still
/// absent, for the original reason, until affixes need it (M7-02).
/// </para>
/// </remarks>
public sealed class EnemySpec
{
    /// <summary>The lowest legal <see cref="TargetPriority"/> — GD §8.1's Husk.</summary>
    private const int MinPriority = 1;

    /// <summary>The highest legal <see cref="TargetPriority"/> — GD §8.1's Choir.</summary>
    private const int MaxPriority = 8;

    /// <summary>The lowest legal <see cref="ThreatCost"/>. GD §8.1's cheapest archetype is 4.</summary>
    /// <remarks>
    /// Not a taste judgement: <c>WaveComposer</c> buys bodies until nothing is affordable, so a
    /// free archetype is a loop that never ends (M2-04 rule 12). Unbounded above on purpose — GD
    /// §8.1 stops at the Revenant's 18 and a boss is not paid from this budget at all, so a
    /// ceiling here would be a number no document owns.
    /// </remarks>
    private const int MinThreatCost = 1;

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
    /// <param name="threatCost">
    /// What one of these costs a stage's threat budget, GD §8.1's Threat Cost column: Husk 4,
    /// Spitter 7, Bloater 8, Revenant 18. At least 1.
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
    /// <param name="aggroRange">
    /// Metres within which it notices the player and stops being idle. 30 for all three authored
    /// archetypes, which is comfortably beyond anything the camera shows — so in practice every
    /// enemy in the arena is already coming for you, and the unaware state exists for the
    /// spawner's sake (M2-05) rather than as a stealth mechanic.
    /// </param>
    /// <param name="behaviour">Which behaviour drives it.</param>
    /// <param name="projectile">
    /// What it throws, or <see langword="null"/> for an archetype that throws nothing — which is
    /// every archetype but the Spitter.
    /// </param>
    /// <param name="explosion">
    /// What it does when it goes off, or <see langword="null"/> for an archetype that does not —
    /// which is every archetype but the Bloater.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is <c>default(ContentId)</c>. Refused where the data is built rather
    /// than where it is read, for the reason <see cref="CharacterSpec"/> gives: a spec with no id
    /// would sit in the catalog under a key the catalog then reports as missing.
    /// <para>
    /// Or <paramref name="behaviour"/> needs a block it was not given — see the remarks on
    /// <see cref="Projectile"/> for why that direction is checked and the other one is not.
    /// </para>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxHp"/>, <paramref name="reach"/> or <paramref name="aggroRange"/> is not a
    /// finite number greater than zero; <paramref name="moveSpeed"/>,
    /// <paramref name="contactDamage"/>, <paramref name="windupTime"/> or
    /// <paramref name="recoverTime"/> is negative, NaN or infinite;
    /// <paramref name="targetPriority"/> is outside 1–8; or <paramref name="threatCost"/> is
    /// below 1.
    /// </exception>
    public EnemySpec(
        ContentId id,
        LocKey nameKey,
        float maxHp,
        float moveSpeed,
        int targetPriority,
        int threatCost,
        bool isElite,
        float contactDamage,
        float reach,
        float windupTime,
        float recoverTime,
        float aggroRange,
        EnemyBehaviourKind behaviour,
        ProjectileSpec projectile = null,
        ExplosionSpec explosion = null)
    {
        if (id.Value is null)
        {
            throw new ArgumentException(
                "id must be a valid ContentId; default(ContentId) has none.",
                nameof(id));
        }

        if (behaviour == EnemyBehaviourKind.Spitter && projectile is null)
        {
            throw new ArgumentException(
                "A Spitter must carry a ProjectileSpec — throwing is the whole of what it does, " +
                "and a behaviour with nothing to throw would stand at its standoff range doing " +
                "nothing for the rest of the run.",
                nameof(projectile));
        }

        if (behaviour == EnemyBehaviourKind.Bloater && explosion is null)
        {
            throw new ArgumentException(
                "A Bloater must carry an ExplosionSpec — a fuse with no blast behind it is an " +
                "enemy that walks up, telegraphs, and then simply stops.",
                nameof(explosion));
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

        if (threatCost < MinThreatCost)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threatCost),
                threatCost,
                $"threatCost must be at least {MinThreatCost} — GD §8.1's cheapest archetype, the "
                    + "Husk, is 4. A free archetype is not a cheap one: WaveComposer buys bodies "
                    + "until nothing is affordable, so at zero it would never stop.");
        }

        Id = id;
        NameKey = nameKey;
        MaxHp = Positive(maxHp, nameof(maxHp));
        MoveSpeed = NonNegative(moveSpeed, nameof(moveSpeed));
        TargetPriority = targetPriority;
        ThreatCost = threatCost;
        IsElite = isElite;
        ContactDamage = NonNegative(contactDamage, nameof(contactDamage));
        Reach = Positive(reach, nameof(reach));
        WindupTime = NonNegative(windupTime, nameof(windupTime));
        RecoverTime = NonNegative(recoverTime, nameof(recoverTime));
        AggroRange = Positive(aggroRange, nameof(aggroRange));
        Behaviour = behaviour;
        Projectile = projectile;
        Explosion = explosion;
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

    /// <summary>
    /// What one of these costs a stage's threat budget — GD §8.1's Threat Cost column. Husk 4.
    /// </summary>
    /// <remarks>
    /// Read by <c>WaveComposer</c> and by nothing else. <b>Not a difficulty number for a single
    /// fight:</b> it prices an archetype against the others so that a stage's budget buys a
    /// coherent crowd, which is why the Choir's 16 is four Husks rather than a statement that it is
    /// four times as dangerous alone.
    /// </remarks>
    public int ThreatCost { get; }

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

    /// <summary>
    /// Metres within which it notices the player. 30 for all three authored archetypes.
    /// </summary>
    /// <remarks>
    /// Read by <c>ChaserBehaviour</c>'s idle state, and by whatever M2-07b and M2-08 write. It is a
    /// fact about the creature rather than about the code that moves it, which is why it lives here
    /// now that a second behaviour needs it — the same question <see cref="ThreatCost"/> settled the
    /// same way.
    /// </remarks>
    public float AggroRange { get; }

    /// <summary>Which behaviour drives it.</summary>
    public EnemyBehaviourKind Behaviour { get; }

    /// <summary>
    /// What it throws, or <see langword="null"/> on an archetype that throws nothing.
    /// </summary>
    /// <remarks>
    /// <b>A kind requires its block; a block does not require its kind.</b>
    /// <see cref="EnemyBehaviourKind.Spitter"/> without one of these is refused, because the
    /// behaviour cannot run; the reverse is deliberately legal, and it is exactly what
    /// <c>Spitter.asset</c> is between M2-06 and M2-07b — a filled-in projectile block on an
    /// archetype authored <see cref="EnemyBehaviourKind.Static"/> until there is code to fire it.
    /// Refusing that would mean authoring the numbers and the behaviour in one change, which is the
    /// two-task split this milestone is built on.
    /// </remarks>
    public ProjectileSpec Projectile { get; }

    /// <summary>
    /// What it does when it goes off, or <see langword="null"/> on an archetype that does not.
    /// </summary>
    /// <remarks>Same rule, same reason as <see cref="Projectile"/>, for the Bloater and M2-08.</remarks>
    public ExplosionSpec Explosion { get; }

    /// <remarks>
    /// <c>!(value &gt; 0f)</c> rather than <c>value &lt;= 0f</c>, so NaN is refused too: every
    /// comparison against NaN is false, and the natural spelling waves it through. Infinity is
    /// asked about separately because it passes a <c>&gt; 0</c> test — an infinite
    /// <see cref="MaxHp"/> is an enemy no amount of damage can kill, an infinite
    /// <see cref="Reach"/> is one that strikes from across the arena, and an infinite
    /// <see cref="AggroRange"/> is the one case here that would look perfectly fine — every enemy
    /// already aggros from further than the camera shows — right up until an archetype wanted to
    /// wait.
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
