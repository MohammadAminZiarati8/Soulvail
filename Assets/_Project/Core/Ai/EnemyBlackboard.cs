using System.Numerics;

namespace Soulvail.Core.Ai;

/// <summary>
/// One enemy's working state: what it was told about the world this tick, and what its own
/// behaviour is remembering. One per agent, never shared. See AR §9.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two halves with two different writers, and the split is the whole discipline.</b> Perception
/// is written once per tick by <c>EnemySystem</c>'s snapshot ingestion (M1-06) and is read-only to
/// behaviours; working memory is written only by the agent's own behaviour and is never touched by
/// ingestion. Nothing else writes either half. Cross-agent communication is events, not this —
/// AR §9 is explicit that a blackboard is not a bus.
/// </para>
/// <para>
/// Public mutable fields, a deliberate exception to the project's no-public-fields rule for the
/// same reason <c>WorldSnapshot</c> is: this is a transfer buffer with one writer per field and a
/// handful of readers, rewritten every tick for every living enemy, and properties would add a
/// call per field per enemy per frame while hiding nothing. The rule exists to stop Unity
/// components leaking their innards; there is no component within reach of this type.
/// </para>
/// <para>
/// <b>Where this differs from AR §9's sketch.</b> The sketch carries a <c>TargetId</c>; this does
/// not, because every enemy in V1 attacks the player and nothing else, so the field would have one
/// legal value. It returns with the first enemy that chooses among targets — the Choir, M7-01,
/// which picks an ally to heal. In exchange this carries <see cref="SelfPosition"/>,
/// <see cref="SelfVelocity"/>, <see cref="PlayerPosition"/>, <see cref="DistanceToPlayer"/> and
/// <see cref="NextAttackAt"/>, which the sketch predates.
/// </para>
/// <para>
/// Positions are <see cref="Vector3"/> because that is what a snapshot reports, and directions are
/// <see cref="Vector2"/> because movement is on the XZ plane — the same split
/// <c>EnemySense</c> makes, for the same reason: a direction with a Y component would let a
/// behaviour steer an enemy into the floor.
/// </para>
/// </remarks>
public sealed class EnemyBlackboard
{
    // ---- Perception: written by snapshot ingestion (M1-06), read-only to behaviours -----------

    /// <summary>Where this enemy is, as last reported by the view.</summary>
    public Vector3 SelfPosition;

    /// <summary>How fast it is actually moving, as last reported — not what core asked for.</summary>
    public Vector3 SelfVelocity;

    /// <summary>Where the player is.</summary>
    public Vector3 PlayerPosition;

    /// <summary>Metres to the player, straight line.</summary>
    public float DistanceToPlayer;

    /// <summary>
    /// Unit XZ direction to the player, straight line. Zero when the two are in the same place,
    /// which a behaviour must treat as "no direction" rather than normalising into a NaN.
    /// </summary>
    public Vector2 DirectionToPlayer;

    /// <summary>
    /// Unit XZ direction to the player along the NavMesh (M1-19), or zero when there is no path or
    /// no NavMesh yet. Behaviours fall back to <see cref="DirectionToPlayer"/> when it is zero, so
    /// a chaser works before pathfinding lands and works better after.
    /// </summary>
    public Vector2 PathDirectionToPlayer;

    /// <summary>Whether the player is visible from here.</summary>
    public bool HasLineOfSight;

    /// <summary>How many other enemies are within 6 m. GD §8.1's clustering pressure reads this.</summary>
    public int AlliesNearby;

    // ---- Working memory: written by the agent's own behaviour ---------------------------------

    /// <summary>
    /// Seconds spent in the current behaviour state. Owned by the FSM that set it, and cleared by
    /// that FSM on transition — nothing outside the behaviour interprets it.
    /// </summary>
    public float StateTimer;

    /// <summary>
    /// The direction a committed dash is travelling, captured on entering a telegraph and consumed
    /// on the dash itself, so a Lunger (M7-01) commits to where the player *was*. Unused until
    /// then; the Husk's strike does not move it.
    /// </summary>
    public Vector2 LungeDirection;

    /// <summary>
    /// Simulated run time at which this enemy may strike again — the same seconds
    /// <c>Health</c> and <c>Targeter</c> are handed, never a wall clock. Zero on a fresh
    /// blackboard, which is in the past for every real <c>now</c> and therefore reads as "may
    /// strike immediately", so a spawned enemy is not gifted a free cooldown.
    /// </summary>
    public float NextAttackAt;

    /// <summary>
    /// Back to a blank blackboard: every field to its default.
    /// </summary>
    /// <remarks>
    /// What a recycled agent gets instead of a new blackboard (<c>EnemyRegistry</c> rule 8).
    /// Perception is included even though ingestion overwrites all of it on the next tick, because
    /// "the next tick" is not guaranteed to come before something reads it — a behaviour given its
    /// first update in the same tick as the spawn would otherwise read the previous occupant's
    /// distance to the player and strike at nothing.
    /// </remarks>
    public void Reset()
    {
        SelfPosition = Vector3.Zero;
        SelfVelocity = Vector3.Zero;
        PlayerPosition = Vector3.Zero;
        DistanceToPlayer = 0f;
        DirectionToPlayer = Vector2.Zero;
        PathDirectionToPlayer = Vector2.Zero;
        HasLineOfSight = false;
        AlliesNearby = 0;

        StateTimer = 0f;
        LungeDirection = Vector2.Zero;
        NextAttackAt = 0f;
    }
}
