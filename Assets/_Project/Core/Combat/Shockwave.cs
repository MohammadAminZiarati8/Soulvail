using System;
using System.Numerics;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>
/// An expanding ring of ground that hurts whoever it passes over, once. GD §9.2's shield-slam,
/// M4-02 rules 2, 4, 5 and 8. See AR §18.4.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately the same shape as <see cref="ZoneSystem"/></b> — parallel arrays, a count, ids
/// issued from 1, absolute times, a <c>Tick</c> that allocates nothing — because the two are the
/// same kind of object: a thing on the floor with its own life, which nothing holds and nothing
/// takes back. What differs is what the floor does and who owns it, and both differences are
/// argued below.
/// </para>
/// <para>
/// <b>It expands from where the body stood, not from the body</b> (rule 2). The origin is taken
/// once, in <see cref="Emit"/>, and never revised: a ring that followed the Warden would be
/// inescapable by walking, and GD §9.1 rule 2 asks every attack to have a safe answer that costs
/// positioning rather than health. Walking out is that answer, and it is only an answer if the
/// ring is anchored to a place.
/// </para>
/// <para>
/// <b>The leading edge bites, and a body is bitten at most once</b> (rule 2). The nominal radius
/// is <c>speed × (now − emitted)</c> and the edge is half a <c>thickness</c> ahead of it; a body
/// is hit on the first tick that edge has reached it, and a flag per ring makes sure it is not hit
/// again while the band is still over it. <b>Asking whether the edge has <em>passed</em> rather
/// than whether the body is <em>inside the band</em> is what makes rule 5 exact:</b> a band test
/// can step over a body between two frames when <c>speed × dt</c> exceeds the thickness, so the
/// same fight would hit at 120 fps and miss at 30. The edge is monotonic, so it cannot.
/// </para>
/// <para>
/// <b>Beyond <see cref="MaxRadius"/> is safe ground, exactly.</b> The hit test refuses a body
/// outside the ceiling even though the band's front edge has reached it, which is what makes
/// <em>"walk out of it"</em> a rule a player can learn rather than a tolerance they have to feel
/// out. The ring retires the instant its nominal radius reaches the ceiling, by which point the
/// edge is half a thickness past it and everything inside has been swept.
/// </para>
/// <para>
/// <b>Containment is XZ only</b> (AR §18.4), the rule every other range check in the project
/// follows: a metre of height is a camera's business, and counting it would let a step in the
/// arena floor make a ring pass under the player's feet.
/// </para>
/// <para>
/// <b>A fifth ring drops, and this is deliberately the opposite of <see cref="ZoneSystem"/>'s
/// ninth zone, which throws</b> (rule 8). A ninth zone was unreachable at Consecrate's cooldown,
/// so a refusal there could only ever mean a bug; a boss is different — an add-heavy phase plus a
/// player-placed zone plus a slam is exactly where a ceiling gets hit, and a run-ending exception
/// during the first boss fight is the worst outcome available. <see cref="Emit"/> answers 0 and
/// the fight carries on, which is <c>ProjectileSystem.Fire</c>'s bargain for the same reason.
/// </para>
/// <para>
/// <b>The player is the only body it can hurt in V1, and the class says so rather than implying
/// it.</b> It takes a <see cref="PlayerCombat"/> on the tick — <c>ProjectileSystem.Tick</c>'s
/// shape — and no <c>EnemySystem</c>. A shockwave that hurt the boss's own adds would be a
/// friendly-fire model invented for one caster, which is the guess <see cref="ZoneSystem"/>
/// refused to make about healing.
/// </para>
/// </remarks>
public sealed class ShockwaveSystem
{
    /// <summary>How many rings may be expanding at once.</summary>
    /// <remarks>
    /// Four, and chosen against the Warden rather than picked: one boss at a slam cooldown of
    /// <c>WardenBehaviour.SlamCooldown</c> cannot have two of its own, and four is headroom for
    /// the second boss (M7) and for an arena hazard that slams back (GD §9.1 rule 6, M4-03).
    /// Deliberately smaller than <see cref="ZoneSystem.Capacity"/>'s eight: a ring is a moment
    /// rather than a place, and four of them overlapping is already unreadable on a phone.
    /// </remarks>
    public const int Capacity = 4;

    /// <summary>The first id of a run. Ids are issued from 1 so that 0 means nobody.</summary>
    private const int FirstId = 1;

    private readonly IDomainEvents _events;

    private readonly int[] _ids = new int[Capacity];
    private readonly Vector3[] _origins = new Vector3[Capacity];
    private readonly float[] _speeds = new float[Capacity];
    private readonly float[] _maxRadii = new float[Capacity];
    private readonly float[] _damages = new float[Capacity];
    private readonly float[] _halfThickness = new float[Capacity];
    private readonly float[] _emittedAt = new float[Capacity];

    /// <summary>
    /// Whether each ring has already hurt the player — the once-per-body discipline
    /// <c>ConeOverlapQuery</c> has, with one body in it.
    /// </summary>
    /// <remarks>
    /// A flag per <em>ring</em> rather than a set per ring, because V1 has exactly one thing a
    /// shockwave can hurt. The day an ally exists (M5-04's Wights) this becomes a small bitmask
    /// keyed by body, which is a change to this array and to <see cref="Bite"/> and to nothing
    /// else.
    /// </remarks>
    private readonly bool[] _bitten = new bool[Capacity];

    /// <summary>
    /// What the walk decided, so the callouts happen after the bookkeeping —
    /// <c>ProjectileSystem.Tick</c>'s shape, and its reason: a handler that emits a ring from
    /// inside <c>ShockwavePassed</c> must not be able to write into the table being compacted.
    /// </summary>
    private readonly float[] _biting = new float[Capacity];

    private readonly int[] _retired = new int[Capacity];

    private int _count;

    private int _nextId = FirstId;

    /// <param name="events">Where <c>ShockwaveEmitted</c> and <c>ShockwavePassed</c> go.</param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    public ShockwaveSystem(IDomainEvents events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>How many rings are expanding right now.</summary>
    public int ActiveCount => _count;

    /// <summary>Where the ring at <paramref name="index"/> expands from, in world metres.</summary>
    /// <remarks>
    /// An index into the live rings in the order they were emitted, and it is <em>not</em> a
    /// handle: a ring retiring shifts the ones after it down, exactly as <see cref="ZoneSystem"/>
    /// does, so anything following one ring across ticks follows the id on the event.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">There is no ring at that index.</exception>
    public Vector3 OriginAt(int index)
    {
        Require(index);

        return _origins[index];
    }

    /// <summary>
    /// How wide the ring at <paramref name="index"/> has grown at <paramref name="now"/>, in
    /// metres.
    /// </summary>
    /// <remarks>
    /// Computed from the emission instant rather than accumulated (rule 5), so a ring is the same
    /// size at the same simulated second whatever the frame rate was on the way there.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// There is no ring at that index, or <paramref name="now"/> is not finite.
    /// </exception>
    public float RadiusAt(int index, float now)
    {
        Require(index);
        RequireFinite(now);

        return Radius(index, now);
    }

    /// <summary>
    /// Sends a ring out from <paramref name="origin"/> and publishes <c>ShockwaveEmitted</c>.
    /// </summary>
    /// <param name="origin">
    /// The slam point — where the body was standing at this instant, never where it is later
    /// (rule 2). Finite in every component.
    /// </param>
    /// <param name="speed">How fast the ring grows, in m/s. Finite and greater than zero.</param>
    /// <param name="maxRadius">
    /// How far it reaches before it is over. Beyond this is safe ground, exactly. Finite and
    /// greater than zero.
    /// </param>
    /// <param name="damage">What it costs a body it passes over. Finite and greater than zero.</param>
    /// <param name="thickness">
    /// The band's width in metres. Its front edge is half of this ahead of the nominal radius, and
    /// the edge is what bites — see the class remarks. Finite and greater than zero.
    /// </param>
    /// <param name="now">Simulated run time — <c>RunState.Time</c>, never a wall clock.</param>
    /// <returns>
    /// Its run-stable id, issued from 1 and never reused within a run — or <b>0 when the ring was
    /// dropped</b> because <see cref="Capacity"/> were already expanding (rule 8). Nothing is
    /// published for a dropped ring, because nothing happened.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A number is not finite and greater than zero, or <paramref name="origin"/> or
    /// <paramref name="now"/> is not finite.
    /// </exception>
    public int Emit(
        Vector3 origin,
        float speed,
        float maxRadius,
        float damage,
        float thickness,
        float now)
    {
        RequireFinite(origin);
        Positive(speed, nameof(speed));
        Positive(maxRadius, nameof(maxRadius));
        Positive(damage, nameof(damage));
        Positive(thickness, nameof(thickness));
        RequireFinite(now);

        // Rule 8, and the whole of it: dropped, not thrown, and not queued either — a ring banked
        // and released later would arrive from a point the boss left seconds ago.
        if (_count == Capacity)
        {
            return 0;
        }

        int id = _nextId;
        _nextId++;

        int slot = _count;

        _ids[slot] = id;
        _origins[slot] = origin;
        _speeds[slot] = speed;
        _maxRadii[slot] = maxRadius;
        _damages[slot] = damage;
        _halfThickness[slot] = thickness * 0.5f;
        _emittedAt[slot] = now;
        _bitten[slot] = false;

        _count++;

        // After the entry exists, so a listener reading ActiveCount from inside it counts this one
        // — ZoneSystem.Spawn's rule, one layer over.
        _events.Publish(new ShockwaveEmitted(id, origin, speed, maxRadius));

        return id;
    }

    /// <summary>
    /// Grows every ring, hurts whoever an edge has just reached, and retires the ones that are
    /// over. Allocates nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Bookkeeping first, callouts second</b> — <c>ProjectileSystem.Tick</c>'s shape and its
    /// reason: <c>PlayerCombat.ApplyDamage</c> and <c>ShockwavePassed</c> can both reach a handler
    /// that emits another ring, and a table being compacted underneath one is a table that loses
    /// an entry. Survivors compact to the front in emission order, so nothing here reorders what
    /// is left.
    /// </para>
    /// <para>
    /// <b>A ring that bites and retires on the same tick does both</b>, damage first: the edge
    /// reaching the last body inside the ceiling and the radius reaching the ceiling are one
    /// instant apart at most, and the order that costs the player nothing is the wrong one.
    /// </para>
    /// </remarks>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <param name="playerPosition">
    /// Where the player is standing this tick. Handed in rather than held, for
    /// <c>ProjectileSystem.Tick</c>'s reason: this class owns no view of where anything is.
    /// </param>
    /// <param name="player">Who a ring hurts. The player, this run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="now"/> is not finite.</exception>
    public void Tick(float now, Vector3 playerPosition, PlayerCombat player)
    {
        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        RequireFinite(now);

        int bitingCount = 0;
        int retiredCount = 0;
        int write = 0;

        for (int read = 0; read < _count; read++)
        {
            float radius = Radius(read, now);

            if (!_bitten[read] && Bite(read, radius, playerPosition))
            {
                _bitten[read] = true;

                _biting[bitingCount] = _damages[read];
                bitingCount++;
            }

            // Retired the instant the nominal radius reaches the ceiling: the edge is half a
            // thickness past it by then, so everything standing inside has been swept.
            if (radius >= _maxRadii[read])
            {
                _retired[retiredCount] = _ids[read];
                retiredCount++;

                continue;
            }

            if (write != read)
            {
                _ids[write] = _ids[read];
                _origins[write] = _origins[read];
                _speeds[write] = _speeds[read];
                _maxRadii[write] = _maxRadii[read];
                _damages[write] = _damages[read];
                _halfThickness[write] = _halfThickness[read];
                _emittedAt[write] = _emittedAt[read];
                _bitten[write] = _bitten[read];
            }

            write++;
        }

        _count = write;

        for (int i = 0; i < bitingCount; i++)
        {
            // The direct call ledger row 7 settles, and its result is deliberately not read: what
            // the damage did is PlayerCombat's to announce, and a ring turned away by i-frames is
            // still a ring that passed.
            player.ApplyDamage(_biting[i], now);
        }

        for (int i = 0; i < retiredCount; i++)
        {
            _events.Publish(new ShockwavePassed(_retired[i]));
        }
    }

    /// <summary>
    /// Forgets every ring, silently. For the end of a run and for a stage boundary.
    /// </summary>
    /// <remarks>
    /// Publishes nothing, for <c>ZoneSystem.Clear</c>'s reason: the scope is going away and with
    /// it every subscriber an event could reach. <b>Unlike a zone, a ring does not survive a stage
    /// boundary</b> — a zone is the player's own ground and outlives the wave it was cast during
    /// (M2-10), while a ring belongs to a boss that the boundary has just taken out of the world.
    /// </remarks>
    public void Clear()
    {
        _count = 0;
        _nextId = FirstId;
    }

    /// <remarks>
    /// <c>!(value &gt; 0f)</c> rather than <c>value &lt;= 0f</c>, so NaN is refused too (AR §18.3),
    /// and infinity separately because it passes a <c>&gt; 0</c> test. A NaN speed makes a ring
    /// whose radius is never a number — every containment test answers <em>no</em> and the retire
    /// comparison answers <em>no</em>, which is a slot held for the rest of the run with nothing
    /// logged. An infinite one sweeps the arena in the frame it was emitted.
    /// </remarks>
    private static void Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero. A ring with no speed, no "
                    + "ceiling, no width or no damage is one that is drawn and does nothing.");
        }
    }

    /// <remarks>
    /// Refused rather than trusted, which is <c>ProjectileSystem</c>'s and
    /// <see cref="ZoneSystem"/>'s answer: a non-finite clock is not a step that does nothing, it
    /// is a ring that is immortal and silent.
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

    /// <remarks>
    /// The origin is refused component by component for the reason the clock is: a NaN there makes
    /// every squared distance NaN, so the ring passes over nobody and retires on schedule with
    /// nothing anywhere recording that it did nothing.
    /// </remarks>
    private static void RequireFinite(Vector3 origin)
    {
        if (IsFinite(origin.X) && IsFinite(origin.Y) && IsFinite(origin.Z))
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(origin),
            origin,
            "origin must be finite in every component. It is where a body was standing, which is "
                + "a position core was told rather than one it computed.");
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>How wide the ring at <paramref name="index"/> is at <paramref name="now"/>.</summary>
    private float Radius(int index, float now) => _speeds[index] * (now - _emittedAt[index]);

    /// <summary>
    /// Whether this ring's front edge has just reached the player, inside the ceiling.
    /// </summary>
    /// <remarks>
    /// Squared distances throughout and no square root, like every other range check in the
    /// project — and XZ only (AR §18.4): a metre of height is a camera's business, and counting it
    /// would let a step in the floor make a ring pass under the player's feet.
    /// </remarks>
    private bool Bite(int index, float radius, Vector3 playerPosition)
    {
        float dx = playerPosition.X - _origins[index].X;
        float dz = playerPosition.Z - _origins[index].Z;

        float distanceSquared = (dx * dx) + (dz * dz);

        // Outside the ceiling is safe ground, exactly — rule 2's answer, and the reason this is
        // asked before the edge rather than after it.
        if (distanceSquared > _maxRadii[index] * _maxRadii[index])
        {
            return false;
        }

        float edge = radius + _halfThickness[index];

        return distanceSquared <= edge * edge;
    }

    /// <exception cref="ArgumentOutOfRangeException">There is no ring at that index.</exception>
    private void Require(int index)
    {
        if (index < 0 || index >= _count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"There is no shockwave at that index. {_count} are expanding.");
        }
    }
}
