using System;
using System.Numerics;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>
/// One shot in flight: a point and a moment, not a body.
/// </summary>
/// <remarks>
/// <para>
/// Everything about a shot is decided when it is fired and nothing about it changes afterwards —
/// where it came from, where it lands, how forgiving the landing is and what it costs. There is no
/// position here because core never holds one: <see cref="ProjectileSystem"/> keeps the arrival
/// time and the view draws the arc from the <see cref="ProjectileFired"/> it was handed
/// (<see cref="ProjectileSystem"/>, rule 2).
/// </para>
/// <para>
/// Guarded like a spec rather than like an internal record, and it is public for one reason: this
/// is the door every number a shot carries comes through. A NaN <see cref="Speed"/> makes an
/// arrival time that is never at or before <c>now</c>, so the shot occupies a slot for the rest of
/// the run and nothing reports it; a NaN <see cref="Radius"/> makes every hit test answer
/// <em>no</em>. Both are silence rather than a visible fault, which is the failure mode AR §18.3
/// exists to refuse.
/// </para>
/// </remarks>
public readonly struct Projectile
{
    /// <param name="specId">The archetype that fired it — what a view picks a mesh from.</param>
    /// <param name="sourceId">
    /// The enemy that fired it, or 0 for nobody. A record, never a handle: see
    /// <see cref="SourceId"/>.
    /// </param>
    /// <param name="origin">Where it leaves from, in world metres.</param>
    /// <param name="target">The point on the ground it is aimed at, in world metres.</param>
    /// <param name="speed">Metres per second of flight. Finite and greater than zero.</param>
    /// <param name="radius">
    /// Metres from <paramref name="target"/> that still count as a hit. Finite and greater than
    /// zero — a zero radius is a shot that can only catch a mathematical point.
    /// </param>
    /// <param name="damage">
    /// What it deals on arrival: the shooter's <c>ContactDamage</c> as of the moment it fired
    /// (M2-06 rule 5). Finite and not negative; zero is legal and lands nothing, which is what
    /// <c>PlayerCombat.ApplyDamage</c> already does with it.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A position is not finite, <paramref name="speed"/> or <paramref name="radius"/> is not a
    /// finite number greater than zero, or <paramref name="damage"/> is not a finite number of at
    /// least zero.
    /// </exception>
    public Projectile(
        ContentId specId,
        int sourceId,
        Vector3 origin,
        Vector3 target,
        float speed,
        float radius,
        float damage)
    {
        SpecId = specId;
        SourceId = sourceId;
        Origin = Finite(origin, nameof(origin));
        Target = Finite(target, nameof(target));
        Speed = Positive(speed, nameof(speed));
        Radius = Positive(radius, nameof(radius));
        Damage = NotNegative(damage, nameof(damage));
    }

    /// <summary>Which archetype fired it, e.g. <c>enemy.spitter</c>.</summary>
    public ContentId SpecId { get; }

    /// <summary>
    /// Which enemy fired it, or 0 for nobody.
    /// </summary>
    /// <remarks>
    /// <b>A record of who fired, never resolved against the registry</b> (rule 5). A shot outlives
    /// its shooter: killing a Spitter after it has released does not un-fire the bolt the player is
    /// already dodging, so an id that has despawned is normal here rather than an error. Anything
    /// wanting the shooter's position wants <see cref="Origin"/>.
    /// </remarks>
    public int SourceId { get; }

    /// <summary>Where it left from, in world metres.</summary>
    public Vector3 Origin { get; }

    /// <summary>The point on the ground it arrives at. Decided once and never tracked (rule 4).</summary>
    public Vector3 Target { get; }

    /// <summary>Metres per second of flight. With the distance, the dodge window.</summary>
    public float Speed { get; }

    /// <summary>Metres from <see cref="Target"/> that still count as a hit.</summary>
    public float Radius { get; }

    /// <summary>What it deals on arrival — the shooter's <c>ContactDamage</c> as of the shot.</summary>
    public float Damage { get; }

    /// <remarks>
    /// <c>!(value &gt; 0f)</c> rather than <c>value &lt;= 0f</c>, so NaN is refused too (AR §18.3),
    /// and infinity separately because it passes a <c>&gt; 0</c> test.
    /// </remarks>
    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero. A shot with neither speed "
                    + "nor extent is one that never arrives anywhere.");
        }

        return value;
    }

    /// <inheritdoc cref="Positive" />
    private static float NotNegative(float value, string paramName)
    {
        if (!(value >= 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number of at least zero. Zero is legal and deals "
                    + "nothing; NaN would be a hit that lands in silence.");
        }

        return value;
    }

    /// <remarks>
    /// A non-finite component in either position spreads into the flight time and then into every
    /// distance test the arrival is resolved by, so it is refused at the door rather than diagnosed
    /// from a shot that hangs in the air for the rest of the run.
    /// </remarks>
    private static Vector3 Finite(Vector3 value, string paramName)
    {
        if (IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z))
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(
            paramName,
            value,
            $"{paramName} must have finite components. A shot aimed at NaN never lands and nothing "
                + "reports it.");
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>
/// The shots that are already in the air, and who decides they landed. One per run, owned by
/// <c>RunState</c>, ticked once a frame after the enemy behaviours and before the death check. See
/// GD §8.1, §12.4, AR §18.1 and M2-07a.
/// </summary>
/// <remarks>
/// <para>
/// <b>Core decides that a shot landed, and calls <c>PlayerCombat.ApplyDamage</c> directly</b>
/// (<see href="../../../../Docs/plan/ROADMAP.md">ledger row 7</see>, ruled at M2-07a rule 1). Unity
/// owes core a <em>fact</em> only when the answer depends on colliders core does not hold — a cone
/// is a wedge and a dash is a swept line, so both of those are reported; an arrival is a point and
/// a moment core computed itself, so nothing is asked. The three things that route would have cost
/// are worth naming: damage would move into the frame's physics phase, one step after the tick that
/// decided it; enemy damage would stop being reproducible from a seed, which is the one property
/// M2-13 and M2-14 are being built to preserve; and an enemy would need a body with a trigger before
/// it could hurt anybody, which is core waiting on the senses for something it had already decided
/// (AR §3).
/// </para>
/// <para>
/// <b>The price is that core holds no walls, so a shot passes through a cover pillar</b> — GD §7.2
/// says it must not. The fix is a <em>sense</em> rather than a fact: <c>EnemySense.HasLineOfSight</c>
/// has been carried unfilled since M0-05, and M2-11b fills it so that a Spitter will not begin a
/// wind-up it cannot see through. Ledger row 13, owner M2-11.
/// </para>
/// <para>
/// <b>A shot is a point and a moment, not a body.</b> <see cref="Fire"/> computes the impact point
/// and the arrival time once and nothing about the shot changes afterwards, so there is no per-tick
/// position in core and no per-tick intent: the view is handed origin, target and flight time in one
/// event and draws the arc itself (M2-09), the same bargain <c>EnemyKnockbackIntent</c> makes for a
/// shove.
/// </para>
/// <para>
/// <b>Nothing here allocates and nothing here draws from <c>IRandom</c>.</b> Two preallocated arrays
/// sized at construction, neither of which ever grows. A shot's spread, if there is ever one, is a
/// change to what a seed means (ADR-0011); today the shot goes exactly where it was aimed, which is
/// also why this class takes no generator at all.
/// </para>
/// </remarks>
public sealed class ProjectileSystem
{
    /// <summary>
    /// The id <see cref="Fire"/> returns when it refused the shot. Zero, because ids are issued
    /// from 1 — the same rule <c>EnemyRegistry</c> keeps, so a default-initialised id field reads
    /// as "nobody" rather than as the first bolt of the run.
    /// </summary>
    public const int NoProjectile = 0;

    /// <summary>The first id of a run.</summary>
    private const int FirstId = 1;

    private readonly IDomainEvents _events;

    /// <summary>
    /// The shots in flight, in the order they were fired, in <c>[0, <see cref="InFlightCount"/>)</c>.
    /// </summary>
    private readonly InFlight[] _shots;

    /// <summary>
    /// The shots this tick has taken out of <see cref="_shots"/> and not yet resolved.
    /// </summary>
    /// <remarks>
    /// <b>A second array rather than a clever walk of the first, and it is what makes rule 8's
    /// reason true.</b> Landing a shot publishes, and publishing reaches subscribers, so anything
    /// that happens inside <see cref="Land"/> must not be able to disturb a shot that has not been
    /// examined yet. <see cref="Tick"/> therefore does all of its bookkeeping first — due shots move
    /// here, survivors compact to the front of <see cref="_shots"/> — and only then resolves them,
    /// by which point the store is already consistent and a <see cref="Fire"/> from inside an impact
    /// handler appends to it correctly.
    /// <para>
    /// The spec's wording was "an array walked backwards on removal". That spelling cannot also
    /// keep rule 6's oldest-first order, since a backwards walk lands the newest first; this keeps
    /// both, at the cost of one more array of the same capacity.
    /// </para>
    /// </remarks>
    private readonly InFlight[] _landing;

    private int _count;

    private int _nextId = FirstId;

    /// <param name="events">Where <c>ProjectileFired</c> and <c>ProjectileImpacted</c> go.</param>
    /// <param name="capacity">
    /// The most shots that may be in the air at once. From <c>BootInstaller</c>, beside the enemy
    /// cap, because it is the same kind of number: what this device is allowed to have happening at
    /// once. At M2-04's concurrency cap of 28 with one shot in the air per Spitter, the array cannot
    /// fill without something already being wrong.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public ProjectileSystem(IDomainEvents events, int capacity)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));

        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "capacity must be greater than zero. A run allowed no projectiles is one whose "
                    + "Spitters fire in silence and are never reported doing it.");
        }

        Capacity = capacity;

        _shots = new InFlight[capacity];
        _landing = new InFlight[capacity];
    }

    /// <summary>The most shots that may be in the air at once.</summary>
    public int Capacity { get; }

    /// <summary>How many shots are in the air right now.</summary>
    public int InFlightCount => _count;

    /// <summary>
    /// Puts a shot in the air and publishes <c>ProjectileFired</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The flight is computed here, once.</b> <c>FlightTime</c> is the XZ distance over the
    /// speed (AR §18.4 — the height between a Spitter's muzzle and the ground it aims at is a
    /// rendering detail, and counting it would inflate every dodge window in the game). That number
    /// is what makes a shot dodgeable: the Spitter's 14 m at 12 m/s is 1.17 s against a blast radius
    /// of 1.6 m, so any movement at all clears it, which is GD §8.1's <em>"punishes standing
    /// still"</em> stated as arithmetic.
    /// </para>
    /// <para>
    /// <b>A shot fired from inside its own radius arrives on the next tick rather than instantly.</b>
    /// A zero distance is a zero flight time — never a division, because the speed is guarded
    /// positive at <see cref="Projectile"/>'s door — and a zero flight time is a shot that has
    /// already arrived by the time anything looks.
    /// </para>
    /// <para>
    /// <b>A refused shot is silent and costs nothing</b> (rule 7). At <see cref="Capacity"/> in
    /// flight this publishes nothing, returns <see cref="NoProjectile"/>, and the caller carries on:
    /// one lost bolt is better than an exception that ends the run, and a caller that wanted to know
    /// has the return value.
    /// </para>
    /// </remarks>
    /// <param name="shot">The shot, with every number it will ever have.</param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <returns>Its id, or <see cref="NoProjectile"/> when the shot was refused.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="now"/> is not finite.</exception>
    public int Fire(in Projectile shot, float now)
    {
        RequireFinite(now);

        if (_count >= Capacity)
        {
            return NoProjectile;
        }

        float dx = shot.Target.X - shot.Origin.X;
        float dz = shot.Target.Z - shot.Origin.Z;
        float flightTime = MathF.Sqrt((dx * dx) + (dz * dz)) / shot.Speed;

        int id = _nextId;
        _nextId++;

        _shots[_count] = new InFlight(id, now + flightTime, shot);
        _count++;

        _events.Publish(new ProjectileFired(
            id,
            shot.SpecId,
            shot.SourceId,
            shot.Origin,
            shot.Target,
            flightTime));

        return id;
    }

    /// <summary>
    /// Lands every shot whose arrival has passed, oldest first, and tells the blackboard how many
    /// are left.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Landing one means: is the player inside the radius, and if so, hurt them.</b> The distance
    /// is XZ (AR §18.4), the comparison is inclusive, and <c>ProjectileImpacted</c> is published
    /// either way — the view has to stop existing whether or not the shot hit anything.
    /// </para>
    /// <para>
    /// <b>i-frames are not consulted here.</b> <c>PlayerCombat.ApplyDamage</c> owns that question
    /// and publishes a blocked <c>PlayerDamaged</c> either way, exactly as
    /// <c>ChaserBehaviour.EnterStrike</c> leaves it: a shot that arrives during a dodge is still a
    /// shot that arrived, and the dodge is what made it harmless rather than what made it miss.
    /// </para>
    /// <para>
    /// <b>The blackboard's <c>IncomingProjectiles</c> is written here and nowhere else</b> (rule 9).
    /// The field has existed since M1-08 saying "zero until M2-07 gives something the means to fire
    /// one", and this is that; it is CC §6.4's Bulwark trigger and M3's auto-cast reads it.
    /// <c>PlayerCombat</c> deliberately does not touch it, so there is one owner for one field, and
    /// the tick order is what makes the value this frame's rather than last frame's.
    /// </para>
    /// </remarks>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>.</param>
    /// <param name="playerPosition">
    /// Where the body last reported the player to be — <c>RunState.PlayerPosition</c>. Passed in
    /// rather than held, for the reason <c>PlayerCombat.ResolveConeHits</c> is handed the world each
    /// time it is asked about it: this class owns no view of where anything is.
    /// </param>
    /// <param name="player">Who a landing shot hurts, and who owns the blackboard it reports to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="now"/> is not finite.</exception>
    public void Tick(float now, Vector3 playerPosition, PlayerCombat player)
    {
        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        RequireFinite(now);

        // Bookkeeping first, callouts second — see _landing. Survivors compact to the front in fire
        // order, which is the order they were in, so nothing here can reorder what is left.
        int landingCount = 0;
        int write = 0;

        for (int read = 0; read < _count; read++)
        {
            // Negated, so a shot whose arrival somehow is not a number lands rather than living
            // for ever: the guards above make that unreachable, and this is which way it fails if
            // one is ever removed.
            if (_shots[read].ArrivesAt > now)
            {
                _shots[write] = _shots[read];
                write++;

                continue;
            }

            _landing[landingCount] = _shots[read];
            landingCount++;
        }

        _count = write;

        for (int i = 0; i < landingCount; i++)
        {
            Land(_landing[i], now, playerPosition, player);
        }

        // After the landings, so a shot fired from inside an impact handler is counted and a shot
        // that just landed is not.
        player.Blackboard.IncomingProjectiles = _count;
    }

    /// <summary>
    /// Forgets every shot in flight, silently. For the end of a run only.
    /// </summary>
    /// <remarks>
    /// Publishes nothing, for <c>EnemySystem.Clear</c>'s reason at the same moment: the scope is
    /// going away and with it every subscriber an event could reach, so a farewell per shot would be
    /// noise. The ids go back to 1 with them, as <c>EnemyRegistry.Clear</c> does — a run is where
    /// "never reused" applies, and the next run gets a fresh object anyway.
    /// </remarks>
    public void Clear()
    {
        _count = 0;
        _nextId = FirstId;
    }

    /// <summary>Resolves one arrival: the hit test, the damage, and the event that says it is over.</summary>
    /// <param name="now">
    /// This tick's clock, not the shot's own <c>ArrivesAt</c>. The two differ by at most a frame,
    /// and <c>Health</c> believes the last time it was told: handing it the earlier one would start
    /// the i-frames in the past and, worse, walk its clock backwards against the
    /// <c>PlayerCombat.Tick</c> that already ran at <paramref name="now"/> this frame.
    /// </param>
    private void Land(in InFlight flight, float now, Vector3 playerPosition, PlayerCombat player)
    {
        Projectile shot = flight.Shot;

        float dx = playerPosition.X - shot.Target.X;
        float dz = playerPosition.Z - shot.Target.Z;

        bool hit = (dx * dx) + (dz * dz) <= shot.Radius * shot.Radius;

        if (hit)
        {
            // The direct call ledger row 7 settles. Its result is deliberately not read: what the
            // damage did is PlayerCombat's to announce, and a shot that was blocked by i-frames is
            // still a shot that arrived.
            player.ApplyDamage(shot.Damage, now);
        }

        // After the damage, so a listener handling this has already seen the PlayerDamaged — and,
        // if it killed, the PlayerDied — that the same arrival caused.
        _events.Publish(new ProjectileImpacted(flight.Id, shot.Target, hit));
    }

    /// <remarks>
    /// A non-finite clock is the one input that makes a shot immortal: an arrival computed from NaN
    /// is never at or before any <c>now</c>, so the slot is held for the rest of the run and nothing
    /// reports it.
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

    /// <summary>One shot and the two things the store knows about it that the shot does not.</summary>
    private readonly struct InFlight
    {
        public InFlight(int id, float arrivesAt, in Projectile shot)
        {
            Id = id;
            ArrivesAt = arrivesAt;
            Shot = shot;
        }

        /// <summary>The run-stable id, issued from 1 and never reused within a run.</summary>
        public int Id { get; }

        /// <summary>
        /// When it arrives, in simulated run seconds. Computed once at <see cref="Fire"/> and never
        /// revised — which is what "a point and a moment" means in practice.
        /// </summary>
        public float ArrivesAt { get; }

        public Projectile Shot { get; }
    }
}
