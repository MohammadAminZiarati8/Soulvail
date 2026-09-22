using System;
using System.Collections.Generic;
using System.Numerics;
using Soulvail.Core.Ai;

namespace Soulvail.Core.Run;

/// <summary>
/// Everything core cannot know for itself: where things are, right now. Handed to
/// <c>Tick</c> alongside <c>dt</c> every frame — the one inbound channel that is not a
/// discrete command or fact. See AR §4.2 and ADR-0003.
/// </summary>
/// <remarks>
/// <para>
/// One instance lives for a whole run and is refilled in place each frame:
/// <see cref="Clear"/>, then one <see cref="AddEnemy"/> per enemy the builder can see.
/// Nothing here allocates, because this runs 60 times a second on a phone and a per-frame
/// array is a steady drip into the GC — the kind that shows up as a stutter, not a leak.
/// </para>
/// <para>
/// A class, where AR §4.2 sketches a struct. <see cref="AddEnemy"/> hands back a reference
/// into <see cref="Enemies"/> and advances <see cref="EnemyCount"/>; passed by value, the
/// builder would be filling a copy the ticker never sees. The sketch predates the
/// ref-returning fill.
/// </para>
/// <para>
/// The public mutable fields are a deliberate exception to the project's no-public-fields
/// rule, which exists to stop Unity components leaking their innards. This is a transfer
/// buffer with exactly one writer (M0-16's <c>SnapshotBuilder</c>) and one reader (core);
/// properties would add a call per field per enemy per frame and hide nothing.
/// </para>
/// </remarks>
public sealed class WorldSnapshot
{
    /// <summary>Seconds since the previous tick. Variable, not fixed — see ADR-0003 §4.4.</summary>
    public float Dt;

    /// <summary>
    /// The move stick, already deadzoned and shaped by the input layer, so <c>|v| &lt;= 1</c>.
    /// Core reads intent from it and never re-shapes it.
    /// </summary>
    public Vector2 MoveInput;

    public Vector3 PlayerPosition;

    public Vector3 PlayerVelocity;

    /// <summary>
    /// Where the door out of this arena is, in world metres. Meaningless when <see cref="HasGate"/>
    /// is false.
    /// </summary>
    /// <remarks>
    /// The arena's standing geometry, and the only part of it core can see. It is reported every
    /// frame rather than once, like every other field here: a gate is a fact about the world, and
    /// the moment core started remembering one it would be holding a copy that an arena swap could
    /// leave stale (M2-10 rule 8).
    /// </remarks>
    public Vector3 GatePosition;

    /// <summary>
    /// This arena has a door.
    /// </summary>
    /// <remarks>
    /// False in a scene dressed without one, which parks <c>StageFlow</c> in its <c>Gate</c> phase
    /// rather than throwing — the same bargain <c>SpawnPlan.SpawnPoints</c> makes for an arena with
    /// nowhere to spawn (M2-05 rule 12), and for the same reason: the M0 grey box and every core
    /// fixture that never intends to leave stage 1 are both legal arenas.
    /// </remarks>
    public bool HasGate;

    /// <summary>
    /// Where the director may put a body in the arena that is standing right now, in world metres.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A property of the arena, which is why it arrives here rather than on the run's
    /// <see cref="SpawnPlan"/>.</b> M2-05 put the points on the plan because a run had one room; a
    /// run has one room <em>per stage</em> from M2-11a, so a per-run field would describe the arena
    /// the player is no longer standing in. <c>StageFlow</c> reads this when a stage enters its
    /// wave phase and hands it to <c>SpawnDirector.Begin</c> — the same handover, one scope
    /// narrower.
    /// </para>
    /// <para>
    /// A reference rather than a copy, and rebound every frame like every other field here. The
    /// list on the other end belongs to the arena and does not change while one is standing, so
    /// nothing is copied per frame and nothing is allocated; the director takes its own copy at
    /// <c>Begin</c> precisely so that it is not holding a buffer somebody else may refill (M2-10's
    /// rule about a plan a director is still holding).
    /// </para>
    /// <para>
    /// Empty is a real answer rather than a missing one: an arena with no spawning surface makes
    /// the director inert rather than being an error (M2-05 rule 12), and so does an undressed Run
    /// scene with no arena raised at all.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Vector3> SpawnPoints = NoSpawnPoints;

    /// <summary>How many entries of <see cref="Enemies"/> are live: <c>[0, EnemyCount)</c>.</summary>
    public int EnemyCount;

    /// <summary>
    /// The preallocated enemy slots. Length is <see cref="EnemyCapacity"/> for the life of the
    /// snapshot and the array instance is never replaced — anything past
    /// <see cref="EnemyCount"/> is last frame's leftovers.
    /// </summary>
    public readonly EnemySense[] Enemies;

    /// <summary>How many entries of <see cref="Minions"/> are live: <c>[0, MinionCount)</c>.</summary>
    public int MinionCount;

    /// <summary>
    /// The preallocated Wight slots — the same <see cref="EnemySense"/> struct, because the facts
    /// are the same facts (M5-04a rule 4). Length is <see cref="MinionCapacity"/> for the life of
    /// the snapshot and the array instance is never replaced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A second array rather than more entries in <see cref="Enemies"/>, because the two ids come
    /// from different registries.</b> A Wight's id is <c>MinionSystem</c>'s and an enemy's is
    /// <c>EnemyRegistry</c>'s, and both start at 1 — so one shared array would let a Wight's report
    /// land on a Husk. The same argument that gives the walk its own door on
    /// <c>IIntentSink.MinionMove</c>, arriving from the other side of the boundary.
    /// </para>
    /// <para>
    /// <b>Two of the struct's five fields are deliberately unread</b> (rule 4).
    /// <see cref="EnemySense.PathDirectionToPlayer"/> is a route to the player, which is not where a
    /// Wight is going, and <see cref="EnemySense.HasLineOfSight"/> is a question nothing on the
    /// friendly side asks. Reusing the struct anyway is what keeps <c>SnapshotBuilder</c> filling one
    /// shape instead of two.
    /// </para>
    /// <para>
    /// <b>Sized from <see cref="MinionSystem.MaxConcurrent"/> rather than from a constructor
    /// argument</b>, so the array and the army it carries cannot disagree and no call site had to
    /// change to admit it. The enemy capacity is a device tier's cap and belongs to whoever composes
    /// the run; a Wight's is a property of the one system that raises them.
    /// </para>
    /// </remarks>
    public readonly EnemySense[] Minions = new EnemySense[MinionSystem.MaxConcurrent];

    /// <param name="enemyCapacity">
    /// The device tier's concurrency cap. M0-12 supplies a constant; a tuning field later.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="enemyCapacity"/> is not positive. A snapshot that can hold no enemies
    /// is a configuration mistake, not a valid state to run with.
    /// </exception>
    public WorldSnapshot(int enemyCapacity)
    {
        if (enemyCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(enemyCapacity),
                enemyCapacity,
                "enemyCapacity must be greater than zero.");
        }

        Enemies = new EnemySense[enemyCapacity];
    }

    /// <summary>
    /// How many enemies this snapshot can carry. Read from the array rather than stored, so
    /// the two can never disagree.
    /// </summary>
    public int EnemyCapacity => Enemies.Length;

    /// <summary>
    /// How many Wights this snapshot can carry — <see cref="MinionSystem.MaxConcurrent"/>. Read
    /// from the array rather than stored, so the two can never disagree.
    /// </summary>
    public int MinionCapacity => Minions.Length;

    /// <summary>An arena with nowhere to put a body — what <see cref="Clear"/> leaves behind.</summary>
    /// <remarks>
    /// Static, and safe to be, for <c>SpawnPlan.Empty</c>'s reason: it is immutable, so there is no
    /// mutable static state for a disabled domain reload to carry between plays. A property rather
    /// than a <c>static readonly</c> field for that type's other reason — which naming rule wins
    /// there is not obvious from reading <c>.editorconfig</c>.
    /// </remarks>
    public static IReadOnlyList<Vector3> NoSpawnPoints { get; } =
        Array.AsReadOnly(Array.Empty<Vector3>());

    /// <summary>
    /// Claims the next enemy slot and returns it by reference, for the caller to fill in place.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The snapshot is full. Deliberately loud: silently dropping the enemy would make core
    /// blind to it, and silently growing the array would allocate mid-frame. The builder is
    /// the one that must respect the cap — by choosing which enemies matter, not by hoping.
    /// </exception>
    public ref EnemySense AddEnemy()
    {
        if (EnemyCount >= Enemies.Length)
        {
            throw new InvalidOperationException(
                $"WorldSnapshot is full at {Enemies.Length} enemies. The builder must respect the capacity.");
        }

        int slot = EnemyCount;
        EnemyCount = slot + 1;
        return ref Enemies[slot];
    }

    /// <summary>
    /// Claims the next Wight slot and returns it by reference, for the caller to fill in place.
    /// </summary>
    /// <remarks>
    /// <see cref="AddEnemy"/>'s shape exactly, including the hazard it carries: a writer into a
    /// reused slot assigns <em>every</em> field, zeroes included (AR §18.2), because
    /// <see cref="Clear"/> leaves the contents alone.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The snapshot is full at <see cref="MinionCapacity"/>. Deliberately loud, for
    /// <see cref="AddEnemy"/>'s reason — and unreachable while the builder walks a system that
    /// refuses to stand more than that many up.
    /// </exception>
    public ref EnemySense AddMinion()
    {
        if (MinionCount >= Minions.Length)
        {
            throw new InvalidOperationException(
                $"WorldSnapshot is full at {Minions.Length} minions. The builder must respect the capacity.");
        }

        int slot = MinionCount;
        MinionCount = slot + 1;
        return ref Minions[slot];
    }

    /// <summary>
    /// Resets the snapshot for a fresh frame: scalars to zero, no enemies.
    /// </summary>
    /// <remarks>
    /// The <see cref="Enemies"/> contents are left alone. Clearing them would be a
    /// capacity-sized write every frame to erase data nobody is allowed to read — every
    /// reader stops at <see cref="EnemyCount"/>, and <see cref="AddEnemy"/> hands out slots
    /// to be overwritten, not appended to.
    /// </remarks>
    public void Clear()
    {
        Dt = 0f;
        MoveInput = Vector2.Zero;
        PlayerPosition = Vector3.Zero;
        PlayerVelocity = Vector3.Zero;

        // Cleared like the rest, though the builder rewrites both every frame from a reference it
        // holds for the run. A gate left standing in a snapshot nobody refilled would be a door in
        // an arena that no longer has one, and the flow would walk the player through it.
        GatePosition = Vector3.Zero;
        HasGate = false;

        // Dropped with the gate and for the same reason: an arena's spawn points left standing in a
        // snapshot nobody refilled would be places in a room that has been torn down, and the
        // director would put the next stage's wave in them.
        SpawnPoints = NoSpawnPoints;

        EnemyCount = 0;

        // The Wights go with the enemies, and their array's contents are left alone for the same
        // reason: every reader stops at the count, and AddMinion hands out slots to be overwritten
        // rather than appended to.
        MinionCount = 0;
    }
}
