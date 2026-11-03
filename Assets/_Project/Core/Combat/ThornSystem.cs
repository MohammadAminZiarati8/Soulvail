using System;
using System.Numerics;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>
/// A line of thorns that warns, bites once as it rises, stands as a wall, then withers —
/// Thornheart's roots and slam, and later the Thornweaver's. JG-20a rules 1–7.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="LaneSystem"/>'s shape</b>: geometry fixed in <see cref="Open"/>, absolute
/// deadlines in simulated seconds, one bite at the instant the arm ends, a compacting table and
/// callouts after the bookkeeping. What differs is the afterlife: a lane closes 0.2 s after it bites,
/// a wall stands for seconds.
/// </para>
/// <para>
/// <b>Core holds the promise, not the collider</b> (AR §18.2). The standing wall the body walks into
/// is Unity's (JG-20e); <see cref="Blocks"/> is here so a placer can keep its next wall out of the
/// last one, and it is a question about the table rather than about the arena.
/// </para>
/// <para>Containment and distance are XZ only (AR §18.4).</para>
/// </remarks>
public sealed class ThornSystem
{
    /// <summary>How many walls may be live at once, arming ones included.</summary>
    /// <remarks>Thornheart's four roots and a slam fan, with headroom for a Thornweaver.</remarks>
    public const int Capacity = 12;

    /// <summary>GD §9.1 rule 1's shortest telegraph, enforced here rather than by the caller.</summary>
    public const float MinArmSeconds = 0.6f;

    /// <summary>Ids are issued from 1 so that 0 means refused.</summary>
    private const int FirstId = 1;

    private readonly IDomainEvents _events;

    private readonly int[] _ids = new int[Capacity];
    private readonly Vector3[] _origins = new Vector3[Capacity];
    private readonly Vector2[] _directions = new Vector2[Capacity];
    private readonly float[] _lengths = new float[Capacity];
    private readonly float[] _halfWidths = new float[Capacity];
    private readonly float[] _damages = new float[Capacity];
    private readonly float[] _risesAt = new float[Capacity];
    private readonly float[] _withersAt = new float[Capacity];
    private readonly bool[] _standing = new bool[Capacity];
    private readonly int[] _owners = new int[Capacity];

    // What a tick or a sweep decided, so the callouts happen after the table is consistent.
    private readonly int[] _rose = new int[Capacity];
    private readonly bool[] _roseInside = new bool[Capacity];
    private readonly float[] _roseDamage = new float[Capacity];
    private readonly int[] _withered = new int[Capacity];

    // Its own buffer, so a sweep a listener starts from inside Tick's callouts cannot overwrite them.
    private readonly int[] _swept = new int[Capacity];

    private int _count;

    private int _nextId = FirstId;

    /// <param name="events">Where <c>ThornArmed</c>, <c>ThornRose</c> and <c>ThornWithered</c> go.</param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    public ThornSystem(IDomainEvents events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>How many walls are live, arming and standing.</summary>
    public int ActiveCount => _count;

    /// <summary>Where the wall at <paramref name="index"/> starts. An index in opening order.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no wall at that index.</exception>
    public Vector3 OriginAt(int index)
    {
        Require(index);

        return _origins[index];
    }

    /// <summary>Which way the wall at <paramref name="index"/> runs, a unit vector on XZ.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no wall at that index.</exception>
    public Vector2 DirectionAt(int index)
    {
        Require(index);

        return _directions[index];
    }

    /// <summary>How far the wall at <paramref name="index"/> runs, in metres.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no wall at that index.</exception>
    public float LengthAt(int index)
    {
        Require(index);

        return _lengths[index];
    }

    /// <summary>Half the width of the wall at <paramref name="index"/>, in metres.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no wall at that index.</exception>
    public float HalfWidthAt(int index)
    {
        Require(index);

        return _halfWidths[index];
    }

    /// <summary>Whether the wall at <paramref name="index"/> has risen and is standing.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no wall at that index.</exception>
    public bool IsStandingAt(int index)
    {
        Require(index);

        return _standing[index];
    }

    /// <summary>Places a wall and publishes <c>ThornArmed</c> (rule 1).</summary>
    /// <param name="origin">Where it starts. Finite in every component.</param>
    /// <param name="direction">Which way it runs on XZ (X, Z). Normalised here; zero is refused.</param>
    /// <param name="length">Metres. Finite and greater than zero.</param>
    /// <param name="halfWidth">Metres. Finite and greater than zero.</param>
    /// <param name="armSeconds">The warning. Finite and at least <see cref="MinArmSeconds"/>.</param>
    /// <param name="standSeconds">How long it stands after rising. Finite and greater than zero.</param>
    /// <param name="damage">The rise's one bite. Finite and not negative; zero is a wall with no bite.</param>
    /// <param name="now">Simulated run time.</param>
    /// <param name="ownerId">The enemy that planted it, carried on <c>ThornArmed</c>; −1 for none (JG-24a rule 7).</param>
    /// <returns>
    /// Its run-stable id, from 1 — or <b>0 when refused</b>: the table is full and every wall in it is
    /// still arming (rule 4). Nothing is published for a refused wall.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">An argument is out of range.</exception>
    public int Open(
        Vector3 origin,
        Vector2 direction,
        float length,
        float halfWidth,
        float armSeconds,
        float standSeconds,
        float damage,
        float now,
        int ownerId = -1)
    {
        RequireFinite(origin, nameof(origin));
        Positive(length, nameof(length));
        Positive(halfWidth, nameof(halfWidth));
        Positive(standSeconds, nameof(standSeconds));
        RequireFinite(now, nameof(now));

        if (!(armSeconds >= MinArmSeconds) || float.IsInfinity(armSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(armSeconds),
                armSeconds,
                $"armSeconds must be finite and at least {MinArmSeconds} (GD §9.1 rule 1).");
        }

        if (!(damage >= 0f) || float.IsInfinity(damage))
        {
            throw new ArgumentOutOfRangeException(
                nameof(damage),
                damage,
                "damage must be finite and not negative.");
        }

        if (!IsFinite(direction.X) || !IsFinite(direction.Y) || !(direction.LengthSquared() > 0f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction),
                direction,
                "direction must be finite with a length on XZ. A wall that runs nowhere blocks nothing.");
        }

        if (_count == Capacity && !WitherOldestStanding())
        {
            return 0;
        }

        Vector2 flat = Vector2.Normalize(direction);

        int id = _nextId;
        _nextId++;

        int slot = _count;

        _ids[slot] = id;
        _origins[slot] = origin;
        _directions[slot] = flat;
        _lengths[slot] = length;
        _halfWidths[slot] = halfWidth;
        _damages[slot] = damage;
        _risesAt[slot] = now + armSeconds;
        _withersAt[slot] = now + armSeconds + standSeconds;
        _standing[slot] = false;
        _owners[slot] = ownerId;

        _count++;

        // After the entry exists, so a listener reading ActiveCount from inside counts this one.
        _events.Publish(new ThornArmed(id, origin, flat, length, halfWidth, armSeconds, ownerId));

        return id;
    }

    /// <summary>
    /// Whether <paramref name="point"/> is within a live wall's half-width plus
    /// <paramref name="clearance"/> of its centre segment, on XZ (rule 5). Arming walls count.
    /// </summary>
    /// <param name="point">The point asked about.</param>
    /// <param name="clearance">Extra metres, finite and not negative — a body's radius.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="clearance"/> is out of range.</exception>
    public bool Blocks(Vector3 point, float clearance)
    {
        if (!(clearance >= 0f) || float.IsInfinity(clearance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(clearance),
                clearance,
                "clearance must be finite and not negative.");
        }

        for (int i = 0; i < _count; i++)
        {
            float dx = point.X - _origins[i].X;
            float dz = point.Z - _origins[i].Z;

            Vector2 along = _directions[i];

            float t = Math.Clamp((dx * along.X) + (dz * along.Y), 0f, _lengths[i]);

            float ox = dx - (along.X * t);
            float oz = dz - (along.Y * t);

            float reach = _halfWidths[i] + clearance;

            if ((ox * ox) + (oz * oz) <= reach * reach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the wall <paramref name="id"/> is still arming or standing.</summary>
    public bool IsLive(int id) => SlotOf(id) >= 0;

    /// <summary>
    /// Drops one wall by id, arming or standing, with <see cref="WitherAll"/>'s <c>ThornWithered</c>
    /// (JG-24a rule 7) — a Thornweaver's death.
    /// </summary>
    /// <returns>Whether there was such a wall.</returns>
    public bool Wither(int id)
    {
        int slot = SlotOf(id);

        if (slot < 0)
        {
            return false;
        }

        for (int j = slot + 1; j < _count; j++)
        {
            Move(j, j - 1);
        }

        _count--;

        _events.Publish(new ThornWithered(id));

        return true;
    }

    /// <summary>
    /// Drops every wall, arming ones included, publishing <c>ThornWithered</c> for each in slot
    /// order (rule 6). A boss beat.
    /// </summary>
    public void WitherAll()
    {
        int count = _count;

        for (int i = 0; i < count; i++)
        {
            _swept[i] = _ids[i];
        }

        _count = 0;

        for (int i = 0; i < count; i++)
        {
            _events.Publish(new ThornWithered(_swept[i]));
        }
    }

    /// <summary>
    /// Raises the walls whose arm is over and withers the ones whose stand is (rules 2, 3, 7).
    /// Allocates nothing.
    /// </summary>
    /// <remarks>
    /// <see cref="LaneSystem.Tick"/>'s order: a wall rises on the tick it withers if one tick
    /// swallows both deadlines, and the order is the bite, every <c>ThornRose</c>, then every
    /// <c>ThornWithered</c>.
    /// </remarks>
    /// <param name="now">Simulated run time, in seconds.</param>
    /// <param name="playerPosition">Where the player is standing this tick.</param>
    /// <param name="player">Who a rising wall bites.</param>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="now"/> is not finite.</exception>
    public void Tick(float now, Vector3 playerPosition, PlayerCombat player)
    {
        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        RequireFinite(now, nameof(now));

        int roseCount = 0;
        int witheredCount = 0;
        int write = 0;

        for (int read = 0; read < _count; read++)
        {
            if (!_standing[read] && now >= _risesAt[read])
            {
                _standing[read] = true;

                _rose[roseCount] = _ids[read];
                _roseInside[roseCount] = Contains(read, playerPosition);
                _roseDamage[roseCount] = _damages[read];

                roseCount++;
            }

            if (now >= _withersAt[read])
            {
                _withered[witheredCount] = _ids[read];
                witheredCount++;

                continue;
            }

            if (write != read)
            {
                Move(read, write);
            }

            write++;
        }

        _count = write;

        for (int i = 0; i < roseCount; i++)
        {
            bool hit = _roseInside[i];

            // Through the player's own door, so a roll's i-frames answer it (rule 2).
            if (hit && _roseDamage[i] > 0f)
            {
                hit = !player.ApplyDamage(_roseDamage[i], now).Blocked;
            }

            _events.Publish(new ThornRose(_rose[i], hit));
        }

        for (int i = 0; i < witheredCount; i++)
        {
            _events.Publish(new ThornWithered(_withered[i]));
        }
    }

    /// <summary>
    /// Drops every wall for a stage boundary or the end of a run, publishing <c>ThornWithered</c> for
    /// each in slot order (rule 6). Ids carry on, so a view still fading a wall never meets its id
    /// again.
    /// </summary>
    public void Clear() => WitherAll();

    /// <summary>
    /// Makes room in a full table by withering the oldest standing wall (rule 4). Slots are in
    /// opening order, so the first standing one is the oldest. An arming wall is never dropped.
    /// </summary>
    /// <returns>Whether a slot was freed.</returns>
    private bool WitherOldestStanding()
    {
        for (int i = 0; i < _count; i++)
        {
            if (!_standing[i])
            {
                continue;
            }

            int id = _ids[i];

            for (int j = i + 1; j < _count; j++)
            {
                Move(j, j - 1);
            }

            _count--;

            _events.Publish(new ThornWithered(id));

            return true;
        }

        return false;
    }

    private void Move(int from, int to)
    {
        _ids[to] = _ids[from];
        _origins[to] = _origins[from];
        _directions[to] = _directions[from];
        _lengths[to] = _lengths[from];
        _halfWidths[to] = _halfWidths[from];
        _damages[to] = _damages[from];
        _risesAt[to] = _risesAt[from];
        _withersAt[to] = _withersAt[from];
        _standing[to] = _standing[from];
        _owners[to] = _owners[from];
    }

    private int SlotOf(int id)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_ids[i] == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Whether the player is inside the wall at <paramref name="index"/> — <c>LaneSystem</c>'s maths.</summary>
    private bool Contains(int index, Vector3 playerPosition)
    {
        float dx = playerPosition.X - _origins[index].X;
        float dz = playerPosition.Z - _origins[index].Z;

        Vector2 along = _directions[index];

        float forward = (dx * along.X) + (dz * along.Y);
        float across = (dx * along.Y) - (dz * along.X);

        return forward >= 0f
            && forward <= _lengths[index]
            && MathF.Abs(across) <= _halfWidths[index];
    }

    /// <remarks>Negated, so NaN is refused too (AR §18.3); infinity separately.</remarks>
    private static void Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero.");
        }
    }

    private static void RequireFinite(float value, string paramName)
    {
        if (!IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} must be finite.");
        }
    }

    private static void RequireFinite(Vector3 value, string paramName)
    {
        if (IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z))
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            paramName,
            value,
            $"{paramName} must be finite in every component.");
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <exception cref="ArgumentOutOfRangeException">There is no wall at that index.</exception>
    private void Require(int index)
    {
        if (index < 0 || index >= _count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"There is no wall at that index. {_count} are live.");
        }
    }
}
