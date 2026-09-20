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
/// not, because nothing an enemy walks at is an <em>entity</em> it could hold an id of: the player
/// is not in the registry, and M5-03's corpse decoy is a place and a moment rather than a body. It
/// returns with the first enemy that chooses among <em>agents</em> — the Choir, M7-01, which picks
/// an ally to heal — and that is the task that should pay for renaming the four fields below
/// (<c>DistanceToQuarry</c> and its three siblings), which is forty reader sites across four
/// behaviours and their fixtures. Until then they keep their names and mean "where this enemy's
/// quarry is" for as long as a decoy stands, with <see cref="QuarryIsADecoy"/> saying which.
/// In exchange this carries <see cref="SelfPosition"/>,
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

    /// <summary>
    /// Metres to the player on the ground plane, straight line. XZ only — the height difference
    /// between a player capsule's centre and an enemy's is a rendering detail, and counting it
    /// would inflate every distance a strike or a spell is checked against (M1-06).
    /// </summary>
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

    /// <summary>
    /// Whether the four fields above describe a <em>corpse decoy</em> rather than the player
    /// themselves. False for every enemy in the arena except while a Shroudstep's decoy stands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Perception, not a threat table</b> (M5-03 rule 8). It is written unconditionally in
    /// <c>EnemySystem.Perceive</c> — the one site that writes the four player fields — from the one
    /// question <c>LureSystem.TryGetLure</c> answers the same way about every agent, so there is
    /// still no <c>TargetId</c>, no per-enemy taunt to remember across ticks, and no way for a
    /// decoy to pull <em>some</em> enemies and not others. It is recomputed every tick like
    /// <see cref="HasLineOfSight"/> beside it, and a decoy that rots lowers it on the next one.
    /// </para>
    /// <para>
    /// <b>It exists because one behaviour reaches past perception and hits the player
    /// directly.</b> <c>ChaserBehaviour.EnterStrike</c> calls <c>PlayerCombat.ApplyDamage</c> when
    /// <see cref="DistanceToPlayer"/> is inside its reach — and while that distance is a decoy's, a
    /// Husk standing on a corpse six metres from the player would hit them. Every other way an
    /// enemy hurts the player already resolves against the <em>real</em> player position and
    /// therefore misses by construction: a Spitter's bolt is aimed at <see cref="PlayerPosition"/>
    /// and landed by <c>ProjectileSystem</c> against <c>RunState.PlayerPosition</c>, and a
    /// Bloater's blast is resolved by <c>EnemySystem.Explode</c> against the same. This field is
    /// the one place the asymmetry is paid for, and M7-01's Choir — which brings a real target id —
    /// is where it stops being a special case.
    /// </para>
    /// </remarks>
    public bool QuarryIsADecoy;

    /// <summary>How many other enemies are within 6 m. GD §8.1's clustering pressure reads this.</summary>
    public int AlliesNearby;

    /// <summary>
    /// This enemy's current HP over its live maximum, in <c>[0, 1]</c> — <c>Health.Fraction</c>,
    /// copied on the same tick as <see cref="DistanceToPlayer"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The field that makes this a trigger blackboard as well as a perception one</b> (M4-01a
    /// rule 6). Everything above is what the enemy perceives about the <em>world</em>; this is what
    /// it can be asked about <em>itself</em>, and a boss skill firing <em>"when my own health drops
    /// below 66 %"</em> had nowhere to read from before it.
    /// </para>
    /// <para>
    /// Written in <c>EnemySystem.Perceive</c>, which runs inside <c>Ingest</c> and therefore
    /// <em>above</em> the behaviour step — so a trigger evaluated this tick reads this tick's
    /// health rather than last tick's. Never NaN: <c>Health.Fraction</c> answers zero rather than
    /// dividing by a maximum that is not positive.
    /// </para>
    /// </remarks>
    public float HpFraction;

    /// <summary>
    /// This enemy's shield points over its shield maximum, in <c>[0, 1]</c> —
    /// <c>Health.ShieldFraction</c>, and <b>always exactly zero</b> today.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Zero, and shipped anyway on purpose.</b> No enemy is built with a <c>ShieldSpec</c>
    /// (<c>EnemyAgent</c> passes <c>shield: null</c>) and nothing grants one, so this reads zero for
    /// every agent in the game. It exists so that the shield clause is a clause that is *false*
    /// rather than a clause that is *missing* — which is exactly the <c>TriggerField.Veilrot</c>
    /// trap M3-07a raised and nothing has fixed: a field nothing writes makes a skill that silently
    /// never fires, and the one that is honestly zero makes a skill that never fires *yet*.
    /// </para>
    /// <para>
    /// Copied from <c>Health</c> rather than assigned a literal, so the day something does grant an
    /// enemy shield this is already right instead of already wrong.
    /// </para>
    /// </remarks>
    public float ShieldFraction;

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
        QuarryIsADecoy = false;
        AlliesNearby = 0;
        HpFraction = 0f;
        ShieldFraction = 0f;

        StateTimer = 0f;
        LungeDirection = Vector2.Zero;
        NextAttackAt = 0f;
    }
}
