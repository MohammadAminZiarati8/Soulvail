using System;
using System.Numerics;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>
/// The decoys standing in the arena. A place and a moment, like a <see cref="Projectile"/> and for
/// the same reason: nothing about one changes after it is dropped, so there is no per-tick state
/// here beyond "has it expired". See CH §3.2, CC §5 and AR §18.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first thing in this game an enemy walks at that is not the player</b> (M5-03). What makes
/// that cheap is that a decoy is not an <em>entity</em>: it has no health, no registry slot, no
/// place in the snapshot and no body, so nothing can hit it, hurt it or kill it. It is two numbers
/// and a position, and the whole of its effect on the world is the one question
/// <see cref="TryGetLure"/> answers for each living enemy once a tick.
/// </para>
/// <para>
/// <b>It is deliberately not a targeting system and must not become one</b> (M5-03 rule 8).
/// <c>EnemyBlackboard</c> carries no <c>TargetId</c>, there is no threat table, and there is no way
/// for a decoy to pull <em>some</em> enemies and not others — <see cref="TryGetLure"/> is asked the
/// same question about every agent and answers it the same way. The blackboard's four player fields
/// keep their names and mean "where this enemy's quarry is" for exactly as long as a decoy stands;
/// renaming them is M7-01's Choir, which needs a real target id anyway.
/// </para>
/// <para>
/// <b>Shaped after <see cref="ZoneSystem"/> rather than after <see cref="ProjectileSystem"/>, and
/// the difference is the expiry.</b> Parallel arrays, a count, and a retirement that shifts the
/// entries after it down so that the order they sit in stays the order they were dropped in — which
/// is what makes rule 3's tie-break ("ties go to the oldest") a property of the storage rather than
/// of a comparison. A shot's store compacts for a different reason: it resolves arrivals and has to
/// survive a <c>Fire</c> from inside an impact handler.
/// </para>
/// <para>
/// <b>Nothing here allocates.</b> Three preallocated arrays of <see cref="Capacity"/>, sized at
/// construction and never grown, and a linear scan of at most two on a query that runs once per
/// living enemy per tick.
/// </para>
/// </remarks>
public sealed class LureSystem
{
    /// <summary>The most decoys that may stand at once. Two, and rule 4 is why.</summary>
    /// <remarks>
    /// <b>Two rather than one</b>, because the Shroudstep's cooldown is 2.5 s and its decoy stands
    /// for 3, so two can legitimately overlap for half a second and the second blink of a chase
    /// must not be the one that fails. <b>And not eight</b>, which is <see cref="ZoneSystem"/>'s
    /// number: nothing in the design lets a player hold more than two, so headroom here would be
    /// capacity invented for nobody.
    /// </remarks>
    public const int Capacity = 2;

    /// <summary>
    /// The id <see cref="Drop"/> returns when it refused the decoy. Zero, because ids are issued
    /// from 1 — <c>ProjectileSystem.NoProjectile</c>'s rule and <c>EnemyRegistry</c>'s, so a
    /// default-initialised id field reads as "nobody" rather than as the first decoy of the run.
    /// </summary>
    public const int NoLure = 0;

    /// <summary>The first id of a run.</summary>
    private const int FirstId = 1;

    private readonly IDomainEvents _events;

    private readonly int[] _ids = new int[Capacity];
    private readonly Vector3[] _positions = new Vector3[Capacity];
    private readonly float[] _expiresAt = new float[Capacity];

    private int _count;

    private int _nextId = FirstId;

    /// <param name="events">Where <see cref="DecoySpawned"/> and <see cref="DecoyExpired"/> go.</param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    public LureSystem(IDomainEvents events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>How many decoys are standing right now.</summary>
    public int Count => _count;

    /// <summary>
    /// Drops one at <paramref name="position"/>, expiring at <c>now + duration</c>, and publishes
    /// <see cref="DecoySpawned"/>.
    /// </summary>
    /// <remarks>
    /// <b>A refused drop is silent and the newest wins nothing</b> (rule 4). At
    /// <see cref="Capacity"/> this publishes nothing, returns <see cref="NoLure"/>, and the caller
    /// carries on — the Shroudstep still happens, because that is
    /// <c>ProjectileSystem.Fire</c>'s bargain for its reason: one lost decoy is better than an
    /// exception that ends a run. The two standing are kept rather than the newest, so a player
    /// spamming the button cannot shorten a taunt they have already bought.
    /// </remarks>
    /// <param name="position">Where it stands, in world metres — where the blink <em>left</em> (rule 6).</param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <param name="duration">Seconds it stands. Finite and greater than zero.</param>
    /// <returns>Its id, or <see cref="NoLure"/> when the drop was refused.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="position"/> is not finite, <paramref name="duration"/> is not a finite number
    /// greater than zero, or <paramref name="now"/> is not finite.
    /// </exception>
    public int Drop(Vector3 position, float now, float duration)
    {
        Finite(position);
        Positive(duration);
        RequireFinite(now);

        if (_count >= Capacity)
        {
            return NoLure;
        }

        int id = _nextId;
        _nextId++;

        int slot = _count;

        _ids[slot] = id;
        _positions[slot] = position;
        _expiresAt[slot] = now + duration;

        _count++;

        // After the entry exists, so a listener reading Count from inside this event counts this
        // one — ZoneSystem.Spawn's rule, one class over.
        _events.Publish(new DecoySpawned(id, position, duration));

        return id;
    }

    /// <summary>
    /// Retires every decoy whose moment has passed, oldest first, and announces each one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ticked above the enemy behaviours and below the player</b> (rule 10). It runs immediately
    /// above <c>EnemySystem.Ingest</c>, which is where perception happens, so an enemy is never
    /// redirected at a corpse that has already rotted — the one ordering this class has.
    /// </para>
    /// <para>
    /// The walk does not advance across a retirement, because retiring shifts the entry after it
    /// into the slot just vacated — <see cref="ZoneSystem.Tick"/>'s shape. A <see cref="Drop"/> from
    /// inside a published handler appends past the end of the walk and is visited with a moment that
    /// is still in the future, so it cannot expire on the tick it was dropped.
    /// </para>
    /// </remarks>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="now"/> is not finite.</exception>
    public void Tick(float now)
    {
        RequireFinite(now);

        var i = 0;

        while (i < _count)
        {
            if (now >= _expiresAt[i])
            {
                Retire(i);

                continue;
            }

            i++;
        }
    }

    /// <summary>
    /// The decoy an enemy at <paramref name="from"/> should walk at, or <see langword="false"/>
    /// where it should walk at the player. Nearest wins; ties go to the oldest (rule 3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a taunt and not a proximity check.</b> A decoy further away than the player still
    /// takes the enemy — every living enemy in the arena walks at a corpse while one stands, which
    /// is what three seconds of the arena's attention means and what the dodge bought. "Nearest"
    /// decides <em>which</em> decoy, never <em>whether</em>.
    /// </para>
    /// <para>
    /// <b>Ties go to the oldest, and the rule exists so the answer does not depend on array
    /// order.</b> At <see cref="Capacity"/> 2 a tie is nearly unreachable; what it buys is that a
    /// decoy dropped later cannot steal an enemy that is equidistant, which would make two
    /// identical frames disagree. The entries sit in drop order and the comparison is a strict
    /// <c>&lt;</c>, so the oldest keeps the tie without anything having to remember it.
    /// </para>
    /// <para>
    /// XZ (AR §18.4) and compared squared, so the query never takes a square root it would discard.
    /// </para>
    /// </remarks>
    /// <param name="from">Where the enemy asking is standing, in world metres.</param>
    /// <param name="position">The decoy to walk at, or <see langword="default"/> when there is none.</param>
    /// <returns>Whether there was a decoy to walk at.</returns>
    public bool TryGetLure(Vector3 from, out Vector3 position)
    {
        position = default;

        if (_count == 0)
        {
            return false;
        }

        int best = 0;
        float bestSquared = SquaredXZ(_positions[0], from);

        for (int i = 1; i < _count; i++)
        {
            float squared = SquaredXZ(_positions[i], from);

            // A strict `<`, so an equidistant later decoy loses to the earlier one — which is the
            // whole of rule 3. Spelled this way round rather than as a negated `>=` so that a
            // distance which somehow is not a number cannot win either.
            if (squared < bestSquared)
            {
                best = i;
                bestSquared = squared;
            }
        }

        position = _positions[best];

        return true;
    }

    /// <summary>
    /// Forgets every decoy, silently. For the end of a stage or a run.
    /// </summary>
    /// <remarks>
    /// Publishes nothing, for <c>ProjectileSystem.Clear</c>'s reason at the same two moments: at the
    /// end of a run the scope is going away and with it every subscriber a farewell could reach,
    /// and at a stage boundary the arena the decoy was standing in is about to stop existing behind
    /// a covered screen. A decoy stands for 3 s against a boundary's 2 s of gate and arrival, so it
    /// is the one kind of thing that <em>can</em> survive one — which is <c>ProjectileSystem</c>'s
    /// own reason for being cleared there, a bolt aimed at a floor that no longer means anything.
    /// The ids go back to 1 with them, as <c>EnemyRegistry.Clear</c> does.
    /// </remarks>
    public void Clear()
    {
        _count = 0;
        _nextId = FirstId;
    }

    /// <summary>Squared XZ separation. No square root, and no allocation (AR §18.4).</summary>
    private static float SquaredXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;

        return (dx * dx) + (dz * dz);
    }

    /// <summary>Drops the decoy at <paramref name="index"/> and says so.</summary>
    /// <remarks>
    /// Shifted rather than swapped with the last entry, for <see cref="ZoneSystem"/>'s reason and
    /// this class's own: the order the entries sit in is the order they were dropped in, and that
    /// order is rule 3's tie-break.
    /// </remarks>
    private void Retire(int index)
    {
        int id = _ids[index];

        for (int i = index; i < _count - 1; i++)
        {
            _ids[i] = _ids[i + 1];
            _positions[i] = _positions[i + 1];
            _expiresAt[i] = _expiresAt[i + 1];
        }

        _count--;

        // After the state moved, so a listener that asks how many are standing is told the truth.
        _events.Publish(new DecoyExpired(id));
    }

    /// <remarks>
    /// <c>!(value &gt; 0f)</c> rather than <c>value &lt;= 0f</c>, so NaN is refused too (AR §18.3),
    /// and infinity separately because it passes a <c>&gt; 0</c> test. Both failures are the same
    /// one: a decoy whose moment can never be reached stands for the rest of the run, holding one
    /// of two slots and taunting the whole arena, with nothing anywhere reporting it.
    /// </remarks>
    private static void Positive(float duration)
    {
        if (!(duration > 0f) || float.IsInfinity(duration))
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "duration must be a finite number greater than zero. A decoy that never expires is "
                    + "a permanent taunt, and one that expires on the instant it is dropped is a "
                    + "dodge the player paid a cooldown for and got nothing from.");
        }
    }

    /// <remarks>
    /// A non-finite component spreads into every distance <see cref="TryGetLure"/> compares, so a
    /// decoy placed at NaN is one that wins or loses every tie by accident. Refused at the door,
    /// where the caller that invented the position is still on the stack.
    /// </remarks>
    private static void Finite(Vector3 position)
    {
        if (IsFinite(position.X) && IsFinite(position.Y) && IsFinite(position.Z))
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(position),
            position,
            "position must have finite components. A decoy standing at NaN is one every distance "
                + "test answers nonsense about.");
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <remarks>
    /// <c>ProjectileSystem</c>'s answer rather than <c>SkillRunner</c>'s, and for its reason: a
    /// non-finite clock here is not a step that does nothing, it is an expiry that can never be
    /// compared against — every decoy immortal and silent.
    /// </remarks>
    private static void RequireFinite(float now)
    {
        if (float.IsNaN(now) || float.IsInfinity(now))
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now,
                "now must be finite. It is RunState.Time, which is a sum of clamped frame times, "
                    + "so a non-finite one is a mis-wired clock rather than a long session.");
        }
    }
}
