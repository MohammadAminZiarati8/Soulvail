using System;
using System.Numerics;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>
/// A strip of ground that warns, bites once, and closes — the line counterpart of
/// <see cref="FissureSystem"/>, first used by Old Croaker's tongue lash. JG-04e rules 1–3.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="FissureSystem"/>'s lifecycle and its reasons</b>: the geometry is fixed in
/// <see cref="Open"/> and never revised, every deadline is an absolute simulated second, a lane
/// bites exactly once at the instant its arm ends, and a full table drops a new lane rather than
/// throwing. Only the shape differs — a rectangle from an origin along a direction — and the open
/// window, which is a constant here because the bite is a strike rather than a crack left behind.
/// </para>
/// <para>
/// <b>A lane, not a cone</b>: on a portrait screen a strip along the player's line reads whole, and
/// the safe answer is one sidestep (GD §9.1 rule 2).
/// </para>
/// <para>Containment is XZ only (AR §18.4).</para>
/// </remarks>
public sealed class LaneSystem
{
    /// <summary>How many lanes may be open at once.</summary>
    /// <remarks>
    /// Four: one Croaker holds at most one of its own (a 0.8 s arm and a 0.2 s window against a 4 s
    /// cooldown), and the rest is headroom for a second boss and the Strangler's vine walls.
    /// </remarks>
    public const int Capacity = 4;

    /// <summary>How long a fired lane stays open, in seconds — drawn, not biting (rule 1).</summary>
    public const float OpenSeconds = 0.2f;

    /// <summary>The first id of a run. Ids are issued from 1 so that 0 means nobody.</summary>
    private const int FirstId = 1;

    private readonly IDomainEvents _events;

    private readonly int[] _ids = new int[Capacity];
    private readonly Vector3[] _origins = new Vector3[Capacity];
    private readonly Vector2[] _directions = new Vector2[Capacity];
    private readonly float[] _lengths = new float[Capacity];
    private readonly float[] _halfWidths = new float[Capacity];
    private readonly float[] _damages = new float[Capacity];
    private readonly float[] _firesAt = new float[Capacity];
    private readonly float[] _closesAt = new float[Capacity];
    private readonly bool[] _firedAt = new bool[Capacity];

    /// <summary>
    /// What the walk decided, so the callouts happen after the bookkeeping —
    /// <see cref="FissureSystem.Tick"/>'s shape and its reason.
    /// </summary>
    private readonly int[] _fired = new int[Capacity];

    private readonly float[] _firedDamage = new float[Capacity];

    private readonly int[] _closed = new int[Capacity];

    private int _count;

    private int _nextId = FirstId;

    /// <param name="events">Where <c>LaneArmed</c>, <c>LaneFired</c> and <c>LaneClosed</c> go.</param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    public LaneSystem(IDomainEvents events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>How many lanes are open right now, arming ones included.</summary>
    public int ActiveCount => _count;

    /// <summary>Where the lane at <paramref name="index"/> starts, in world metres.</summary>
    /// <remarks>An index in opening order, not a handle — <see cref="FissureSystem.PositionAt"/>'s rule.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">There is no lane at that index.</exception>
    public Vector3 OriginAt(int index)
    {
        Require(index);

        return _origins[index];
    }

    /// <summary>Which way the lane at <paramref name="index"/> runs, a unit vector on XZ.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no lane at that index.</exception>
    public Vector2 DirectionAt(int index)
    {
        Require(index);

        return _directions[index];
    }

    /// <summary>How far the lane at <paramref name="index"/> runs, in metres.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no lane at that index.</exception>
    public float LengthAt(int index)
    {
        Require(index);

        return _lengths[index];
    }

    /// <summary>Half the width of the lane at <paramref name="index"/>, in metres.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no lane at that index.</exception>
    public float HalfWidthAt(int index)
    {
        Require(index);

        return _halfWidths[index];
    }

    /// <summary>Whether the lane at <paramref name="index"/> has gone off.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no lane at that index.</exception>
    public bool HasFiredAt(int index)
    {
        Require(index);

        return _firedAt[index];
    }

    /// <summary>Opens a lane and publishes <c>LaneArmed</c>.</summary>
    /// <param name="origin">Where it starts — the attacker's mouth. Finite in every component.</param>
    /// <param name="direction">
    /// Which way it runs. Only X and Z are read, and they are normalised here; a direction with no
    /// length on XZ is refused.
    /// </param>
    /// <param name="length">How far it runs, in metres. Finite and greater than zero.</param>
    /// <param name="halfWidth">Half its width, in metres. Finite and greater than zero.</param>
    /// <param name="armSeconds">
    /// The telegraph before it bites — GD §9.1 rule 1's ≥ 0.6 s, enforced by the caller for
    /// <see cref="FissureSystem.Open"/>'s reason. Finite and greater than zero.
    /// </param>
    /// <param name="damage">What standing in it costs. Finite and greater than zero.</param>
    /// <param name="now">Simulated run time — <c>RunState.Time</c>, never a wall clock.</param>
    /// <param name="tongue">
    /// Whether a tongue strikes down it when it fires — Old Croaker's lash, not a Lunger's dash
    /// (JG-07a rule 12). Carried on <c>LaneArmed</c> for the view; the bite is the same either way.
    /// </param>
    /// <returns>
    /// Its run-stable id, issued from 1 — or <b>0 when it was dropped</b> because
    /// <see cref="Capacity"/> were already open. Nothing is published for a dropped lane.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">An argument is out of range.</exception>
    public int Open(
        Vector3 origin,
        Vector3 direction,
        float length,
        float halfWidth,
        float armSeconds,
        float damage,
        float now,
        bool tongue = false)
    {
        RequireFinite(origin, nameof(origin));
        RequireFinite(direction, nameof(direction));
        Positive(length, nameof(length));
        Positive(halfWidth, nameof(halfWidth));
        Positive(armSeconds, nameof(armSeconds));
        Positive(damage, nameof(damage));
        RequireFinite(now);

        var flat = new Vector2(direction.X, direction.Z);

        if (!(flat.LengthSquared() > 0f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction),
                direction,
                "direction must have a length on XZ. A lane that runs nowhere hits nobody.");
        }

        if (_count == Capacity)
        {
            return 0;
        }

        flat = Vector2.Normalize(flat);

        int id = _nextId;
        _nextId++;

        int slot = _count;

        _ids[slot] = id;
        _origins[slot] = origin;
        _directions[slot] = flat;
        _lengths[slot] = length;
        _halfWidths[slot] = halfWidth;
        _damages[slot] = damage;
        _firesAt[slot] = now + armSeconds;
        _closesAt[slot] = now + armSeconds + OpenSeconds;
        _firedAt[slot] = false;

        _count++;

        // After the entry exists, so a listener reading ActiveCount from inside it counts this one.
        _events.Publish(new LaneArmed(id, origin, flat, length, halfWidth, armSeconds, tongue));

        return id;
    }

    /// <summary>
    /// Fires the lanes whose arm is over and closes the ones whose window is. Allocates nothing.
    /// </summary>
    /// <remarks>
    /// <see cref="FissureSystem.Tick"/>'s order: a lane fires on the tick it closes if one tick
    /// swallows both deadlines, and the order is damage, then <c>LaneFired</c>, then <c>LaneClosed</c>.
    /// </remarks>
    /// <param name="now">Simulated run time, in seconds.</param>
    /// <param name="playerPosition">Where the player is standing this tick.</param>
    /// <param name="player">Who a lane hurts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="now"/> is not finite.</exception>
    public void Tick(float now, Vector3 playerPosition, PlayerCombat player)
    {
        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        RequireFinite(now);

        int firedCount = 0;
        int closedCount = 0;
        int write = 0;

        for (int read = 0; read < _count; read++)
        {
            if (!_firedAt[read] && now >= _firesAt[read])
            {
                _firedAt[read] = true;

                _fired[firedCount] = _ids[read];
                _firedDamage[firedCount] = Contains(read, playerPosition) ? _damages[read] : 0f;

                firedCount++;
            }

            if (now >= _closesAt[read])
            {
                _closed[closedCount] = _ids[read];
                closedCount++;

                continue;
            }

            if (write != read)
            {
                _ids[write] = _ids[read];
                _origins[write] = _origins[read];
                _directions[write] = _directions[read];
                _lengths[write] = _lengths[read];
                _halfWidths[write] = _halfWidths[read];
                _damages[write] = _damages[read];
                _firesAt[write] = _firesAt[read];
                _closesAt[write] = _closesAt[read];
                _firedAt[write] = _firedAt[read];
            }

            write++;
        }

        _count = write;

        for (int i = 0; i < firedCount; i++)
        {
            if (_firedDamage[i] > 0f)
            {
                player.ApplyDamage(_firedDamage[i], now);
            }

            _events.Publish(new LaneFired(_fired[i]));
        }

        for (int i = 0; i < closedCount; i++)
        {
            _events.Publish(new LaneClosed(_closed[i]));
        }
    }

    /// <summary>
    /// Forgets every lane, silently, and restarts the ids. For the end of a run and for a stage
    /// boundary. A lane cleared before it fired never bites.
    /// </summary>
    /// <remarks>
    /// Publishes nothing, for <see cref="FissureSystem.Clear"/>'s reason: it runs after
    /// <c>RunEnded</c>, when every listener is going away. The strip a view drew retires on its own
    /// clock, the arm plus <see cref="OpenSeconds"/>, which <c>LaneArmed</c> carries.
    /// </remarks>
    public void Clear()
    {
        _count = 0;
        _nextId = FirstId;
    }

    /// <remarks>Negated, so NaN is refused too (AR §18.3); infinity separately.</remarks>
    private static void Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero. A lane with no extent, "
                    + "no telegraph or no bite is one that is drawn and does nothing.");
        }
    }

    private static void RequireFinite(float now)
    {
        if (!IsFinite(now))
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now,
                "now must be finite. It is RunState.Time, a sum of clamped frame times.");
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

    /// <summary>Whether the player is standing in the lane at <paramref name="index"/>, on XZ.</summary>
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

    /// <exception cref="ArgumentOutOfRangeException">There is no lane at that index.</exception>
    private void Require(int index)
    {
        if (index < 0 || index >= _count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"There is no lane at that index. {_count} are open.");
        }
    }
}
