using System;
using System.Numerics;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>
/// A patch of ground that warns, bites once, and closes — GD §9.2's ground fissures, M4-02 rules
/// 1, 3, 4, 5 and 8. See AR §18.4.
/// </summary>
/// <remarks>
/// <para>
/// <b>It opens where the player was and does not track</b> (rule 3). The position is taken once,
/// in <see cref="Open"/>, and never revised, which is <see cref="ZoneSystem"/>'s rule for the
/// opposite purpose: a zone that followed the player would be a regeneration buff with a circle
/// under it, and a fissure that followed them would be an attack with no answer. Moving is the
/// answer, and it is only an answer if the crack stays put.
/// </para>
/// <para>
/// <b>It arms, fires, then closes, and the arm is the telegraph</b> (rule 1, GD §9.1 rule 1). The
/// arm window is authored rather than constant — <c>WardenBehaviour</c> hands it the body's own
/// <c>EnemySpec.WindupTime</c> — and <c>FissureArmed</c> carries the length so whatever draws the
/// warning cannot disagree with the hazard about how long the player has. <b>Half of rule 1 is
/// missing and this class says so:</b> the rule asks for a visual <em>and</em> an audio cue, and
/// there is no audio system in this project at all (M7).
/// </para>
/// <para>
/// <b>It bites exactly once, at the instant the arm ends</b>, and the open window that follows is
/// how long the crack is drawn rather than a second damage window. One bite is what makes rule 3's
/// safe answer legible — you were standing on it when it went off or you were not — where a crack
/// that hurt continuously would turn a positioning decision into a damage-over-time the player
/// could not read the edges of. The day an open fissure should be an obstacle or a slow, this is
/// where it goes and <see cref="_firedAt"/> is what it hangs on.
/// </para>
/// <para>
/// <b>Everything is scheduled as an absolute time</b> (rule 5), which is <see cref="ZoneSystem"/>'s
/// and <c>Weapon</c>'s discipline: a fissure opened at the same simulated second fires at the same
/// simulated second whether the phone was managing 30 frames or 120, and a tick that swallows both
/// deadlines lands both rather than losing one.
/// </para>
/// <para>
/// <b>Containment is XZ only</b> (AR §18.4), and <b>a ninth fissure drops rather than throwing</b>
/// (rule 8) — both for the reasons <see cref="ShockwaveSystem"/> argues at length.
/// </para>
/// </remarks>
public sealed class FissureSystem
{
    /// <summary>How many cracks may be open at once.</summary>
    /// <remarks>
    /// Eight, which is <see cref="ZoneSystem.Capacity"/>'s number reached the same way rather than
    /// copied: at <c>WardenBehaviour.FissureCooldown</c> and its authored arm and open windows, one
    /// Warden holds at most two of its own, and eight is headroom for the phase-3 Warden, a second
    /// boss (M7) and an arena that opens its own (GD §9.1 rule 6, M4-03). Larger than
    /// <see cref="ShockwaveSystem.Capacity"/> because a crack is a place and a ring is a moment:
    /// eight places on the floor are readable and four overlapping rings are not.
    /// </remarks>
    public const int Capacity = 8;

    /// <summary>The first id of a run. Ids are issued from 1 so that 0 means nobody.</summary>
    private const int FirstId = 1;

    private readonly IDomainEvents _events;

    private readonly int[] _ids = new int[Capacity];
    private readonly Vector3[] _positions = new Vector3[Capacity];
    private readonly float[] _radii = new float[Capacity];
    private readonly float[] _damages = new float[Capacity];
    private readonly float[] _firesAt = new float[Capacity];
    private readonly float[] _closesAt = new float[Capacity];

    /// <summary>
    /// Whether each crack has already gone off, so a tick that swallows both deadlines fires it
    /// once and a tick taken long after it closed does not fire it at all.
    /// </summary>
    private readonly bool[] _firedAt = new bool[Capacity];

    /// <summary>
    /// What the walk decided, so the callouts happen after the bookkeeping —
    /// <see cref="ShockwaveSystem.Tick"/>'s shape and its reason. Two buffers because a fissure
    /// that fires and closes on the same tick owes two announcements in a fixed order.
    /// </summary>
    private readonly int[] _fired = new int[Capacity];

    private readonly float[] _firedDamage = new float[Capacity];

    private readonly int[] _closed = new int[Capacity];

    private int _count;

    private int _nextId = FirstId;

    /// <param name="events">
    /// Where <c>FissureArmed</c>, <c>FissureFired</c> and <c>FissureClosed</c> go.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    public FissureSystem(IDomainEvents events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>How many cracks are open right now, arming ones included.</summary>
    public int ActiveCount => _count;

    /// <summary>Where the fissure at <paramref name="index"/> opened, in world metres.</summary>
    /// <remarks>
    /// An index into the live fissures in the order they were opened, and not a handle — a crack
    /// closing shifts the ones after it down, exactly as <see cref="ZoneSystem"/> does.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">There is no fissure at that index.</exception>
    public Vector3 PositionAt(int index)
    {
        Require(index);

        return _positions[index];
    }

    /// <summary>How far the fissure at <paramref name="index"/> reaches, in metres.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no fissure at that index.</exception>
    public float RadiusAt(int index)
    {
        Require(index);

        return _radii[index];
    }

    /// <summary>
    /// Whether the fissure at <paramref name="index"/> has already gone off — <em>armed</em> while
    /// this is false, <em>open</em> once it is true.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no fissure at that index.</exception>
    public bool HasFiredAt(int index)
    {
        Require(index);

        return _firedAt[index];
    }

    /// <summary>
    /// Opens one at <paramref name="at"/> and publishes <c>FissureArmed</c>.
    /// </summary>
    /// <param name="at">
    /// Where it opens — under the player's feet at this instant, never revised (rule 3). Finite in
    /// every component.
    /// </param>
    /// <param name="radius">How far it reaches, in metres. Finite and greater than zero.</param>
    /// <param name="armSeconds">
    /// The telegraph before it bites — GD §9.1 rule 1's ≥ 0.6 s. Finite and greater than zero.
    /// <b>The floor is not enforced here</b>, deliberately: this class does not know whose attack
    /// it is, and a hazard system refusing a designer's number at runtime would end a run over a
    /// tuning mistake. It is enforced where it can be read — by
    /// <c>WardenBehaviour.MinTelegraphSeconds</c>, and over the shipped asset by
    /// <c>ContentValidationTests</c>.
    /// </param>
    /// <param name="openSeconds">
    /// How long the crack is drawn after it has gone off. Finite and greater than zero.
    /// </param>
    /// <param name="damage">What standing in it costs. Finite and greater than zero.</param>
    /// <param name="now">Simulated run time — <c>RunState.Time</c>, never a wall clock.</param>
    /// <returns>
    /// Its run-stable id, issued from 1 and never reused within a run — or <b>0 when it was
    /// dropped</b> because <see cref="Capacity"/> were already open (rule 8). Nothing is published
    /// for a dropped fissure, because nothing happened.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A number is not finite and greater than zero, or <paramref name="at"/> or
    /// <paramref name="now"/> is not finite.
    /// </exception>
    public int Open(
        Vector3 at,
        float radius,
        float armSeconds,
        float openSeconds,
        float damage,
        float now)
    {
        RequireFinite(at);
        Positive(radius, nameof(radius));
        Positive(armSeconds, nameof(armSeconds));
        Positive(openSeconds, nameof(openSeconds));
        Positive(damage, nameof(damage));
        RequireFinite(now);

        if (_count == Capacity)
        {
            return 0;
        }

        int id = _nextId;
        _nextId++;

        int slot = _count;

        _ids[slot] = id;
        _positions[slot] = at;
        _radii[slot] = radius;
        _damages[slot] = damage;
        _firesAt[slot] = now + armSeconds;
        _closesAt[slot] = now + armSeconds + openSeconds;
        _firedAt[slot] = false;

        _count++;

        // After the entry exists, so a listener reading ActiveCount from inside it counts this one.
        _events.Publish(new FissureArmed(id, at, radius, armSeconds));

        return id;
    }

    /// <summary>
    /// Fires the cracks whose arm is over and closes the ones whose window is. Allocates nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Bookkeeping first, callouts second</b> — <see cref="ShockwaveSystem.Tick"/>'s shape, and
    /// its reason. Survivors compact to the front in the order they were opened.
    /// </para>
    /// <para>
    /// <b>A fissure fires on the tick it closes, if the two land together</b>, and the order is
    /// damage then <c>FissureFired</c> then <c>FissureClosed</c>. The alternative silently makes
    /// every authored fissure harmless whenever one tick swallows both deadlines, which is a 30 fps
    /// phone with a 0.1 s open window — <see cref="ZoneSystem.Tick"/>'s pulse-before-expiry
    /// ruling, reached again because the same coincidence is worth a whole attack here.
    /// </para>
    /// </remarks>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <param name="playerPosition">
    /// Where the player is standing this tick. Handed in rather than held, for
    /// <c>ProjectileSystem.Tick</c>'s reason: this class owns no view of where anything is.
    /// </param>
    /// <param name="player">Who a fissure hurts. The player, this run.</param>
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

                // Zero for a fissure nobody was standing in, which is still a fissure that fired:
                // the event goes out either way and the damage does not.
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
                _positions[write] = _positions[read];
                _radii[write] = _radii[read];
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
                // The direct call ledger row 7 settles, and its result is deliberately not read:
                // what the damage did is PlayerCombat's to announce.
                player.ApplyDamage(_firedDamage[i], now);
            }

            // After the damage, so a listener handling this has already seen the PlayerDamaged —
            // and, if it killed, the PlayerDied — that the same bite caused.
            _events.Publish(new FissureFired(_fired[i]));
        }

        for (int i = 0; i < closedCount; i++)
        {
            _events.Publish(new FissureClosed(_closed[i]));
        }
    }

    /// <summary>
    /// Forgets every fissure, silently. For the end of a run and for a stage boundary.
    /// </summary>
    /// <remarks>
    /// Publishes nothing, for <see cref="ShockwaveSystem.Clear"/>'s reason, and it is called at a
    /// boundary for that method's reason too: a crack belongs to a boss the boundary has just
    /// taken out of the world, where a zone is the player's own ground and survives one (M2-10).
    /// </remarks>
    public void Clear()
    {
        _count = 0;
        _nextId = FirstId;
    }

    /// <remarks>
    /// <c>!(value &gt; 0f)</c> rather than <c>value &lt;= 0f</c>, so NaN is refused too
    /// (AR §18.3), and infinity separately because it passes a <c>&gt; 0</c> test. A NaN arm
    /// window is a crack that never fires and never closes — a slot held for the rest of the run
    /// with nothing logged.
    /// </remarks>
    private static void Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero. A fissure with no extent, "
                    + "no telegraph, no life or no bite is one that is drawn and does nothing.");
        }
    }

    /// <remarks>
    /// Refused rather than trusted, for <see cref="ShockwaveSystem"/>'s reason: a non-finite clock
    /// is a crack that is immortal and silent.
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
    /// The position is refused component by component: a NaN there makes every squared distance
    /// NaN, so the crack fires at nobody and closes on schedule with nothing recording that it
    /// could never have hit anything.
    /// </remarks>
    private static void RequireFinite(Vector3 at)
    {
        if (IsFinite(at.X) && IsFinite(at.Y) && IsFinite(at.Z))
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(at),
            at,
            "at must be finite in every component. It is where the player was standing, which is "
                + "a position core was told rather than one it computed.");
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>Whether the player is standing in the fissure at <paramref name="index"/>.</summary>
    /// <remarks>
    /// Squared distance on the XZ plane (AR §18.4, and every other range check in the project): a
    /// metre of height is a camera's business, and counting it would make standing on a step
    /// escape a crack the player is visibly inside of.
    /// </remarks>
    private bool Contains(int index, Vector3 playerPosition)
    {
        float dx = playerPosition.X - _positions[index].X;
        float dz = playerPosition.Z - _positions[index].Z;

        return (dx * dx) + (dz * dz) <= _radii[index] * _radii[index];
    }

    /// <exception cref="ArgumentOutOfRangeException">There is no fissure at that index.</exception>
    private void Require(int index)
    {
        if (index < 0 || index >= _count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"There is no fissure at that index. {_count} are open.");
        }
    }
}
