using System;
using System.Numerics;

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

    /// <summary>How many entries of <see cref="Enemies"/> are live: <c>[0, EnemyCount)</c>.</summary>
    public int EnemyCount;

    /// <summary>
    /// The preallocated enemy slots. Length is <see cref="EnemyCapacity"/> for the life of the
    /// snapshot and the array instance is never replaced — anything past
    /// <see cref="EnemyCount"/> is last frame's leftovers.
    /// </summary>
    public readonly EnemySense[] Enemies;

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
        EnemyCount = 0;
    }
}
