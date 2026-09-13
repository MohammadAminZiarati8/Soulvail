using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Soulvail.Game.Adapters;

/// <summary>
/// Which way to walk to reach the player, asked of the NavMesh once every tenth of a second per
/// enemy and cached in between. A sense, and the clearest example of AR §3's rule that pathfinding
/// belongs to Unity: core is told a direction and decides what to do about it, and could not have
/// computed one for itself without a copy of the arena's geometry.
/// </summary>
/// <remarks>
/// <para>
/// <b>It reports and never instructs.</b> The value goes into <c>EnemySense.PathDirectionToPlayer</c>
/// alongside positions and velocities, and <c>ChaserBehaviour</c> is free to ignore it — which is
/// exactly what it does while a Husk is winding up a strike. Nothing here moves anything, and no
/// <c>NavMeshAgent</c> exists anywhere in the project: an agent steers, and steering is a decision.
/// </para>
/// <para>
/// <b>Throttled twice, and both throttles are about the same 200 µs.</b> <c>NavMesh.CalculatePath</c>
/// is a synchronous A* through the navigation mesh, and sixty enemies asking sixty times a second is
/// the single most expensive thing an arena could do to a phone. So a path is recomputed at most
/// every <c>1 / refreshHz</c> seconds — a Husk walking at 3 m/s covers 30 cm in that time, far less
/// than the corner radius the route is made of — and at most
/// <see cref="PathRefreshBudget.ForFrame"/> enemies recompute on any one frame. The second limit is
/// what bounds the <em>worst</em> frame rather than the average one: without it, a wave that all
/// spawned together would refresh in lockstep for ever, and every tenth frame would cost sixty path
/// searches.
/// </para>
/// <para>
/// <b>That per-frame limit used to be the constant four, and four was not enough</b> (M2-05, ledger
/// row 5). It sustained 24 enemies at 60 fps and 12 at 30, against M2-04's cap of 28 — so above two
/// dozen the population silently fell behind its own cadence and walked into pillars. The budget
/// now scales with how many enemies are asking and how long the frame took, and
/// <see cref="StalePathCount"/> reports the shortfall when the ceiling binds anyway. Row 5's real
/// complaint was never the number: it was that nothing said the ceiling had been reached.
/// </para>
/// <para>
/// <b>The round-robin is emergent rather than scheduled.</b> Nothing tracks whose turn it is: the
/// first few stale enemies each frame get refreshed and stop being stale, so the next frame's budget
/// falls to the ones behind them, and the whole population rotates through on its own. Twenty-eight
/// enemies at five a frame come round in six frames, which is the tenth of a second the cadence
/// asked for — and it stays that tenth of a second at 30 fps, because the allowance doubles with
/// the step rather than the wait doubling with it.
/// </para>
/// <para>
/// <b>Nothing allocates.</b> One <see cref="NavMeshPath"/> for the life of the object, corners read
/// into a two-element buffer through <c>GetCornersNonAlloc</c> — the <c>corners</c> property builds
/// a fresh array on every read — and a pre-sized slot table with a free list, the same shape as
/// <c>EnemyRegistry</c>'s and for the same reason. The one thing it cannot promise is what happens
/// inside the engine's own path search.
/// </para>
/// </remarks>
public sealed class NavPathSense
{
    /// <summary>
    /// How often one enemy's route is recomputed by default, in times per second.
    /// </summary>
    /// <remarks>
    /// Named rather than left as a literal on the constructor alone, because VContainer does not
    /// honour a C# default value — it resolves every constructor parameter or throws — so
    /// <c>RunScope</c> has to pass this number, and it should be passing the same one the default
    /// says.
    /// </remarks>
    public const float DefaultRefreshHz = 10f;

    /// <summary>
    /// Below this the two points are the same place and there is no direction to give. Not zero,
    /// for the reason <c>EnemySystem</c> gives about its own: normalising a separation of 1e-9
    /// yields a unit vector made of floating-point noise.
    /// </summary>
    private const float MinDirectionDistance = 1e-4f;

    /// <summary>One cached answer, and the two clocks that decide when it is replaced.</summary>
    private struct Entry
    {
        /// <summary>Whose answer this is. Needed to un-index the slot when it is reclaimed.</summary>
        public int Id;

        /// <summary>The last direction computed for this enemy, or zero before the first one.</summary>
        public Vector2 Direction;

        /// <summary>When that direction was computed. Negative infinity means "never".</summary>
        public float LastComputed;

        /// <summary>
        /// The last frame this enemy asked. What makes a slot reclaimable: an enemy that did not
        /// ask this frame is one the arena no longer has.
        /// </summary>
        public float LastSeen;

        /// <summary>
        /// When this enemy was first seen. What staleness is measured from until the first route
        /// is computed — without it, an enemy that appeared a millisecond ago would count as
        /// overdue for ever, and <see cref="StalePathCount"/> would read every spawn as a fault.
        /// </summary>
        public float Arrived;
    }

    private readonly int _capacity;
    private readonly float _refreshInterval;

    /// <summary>
    /// How many recomputes this frame may spend. Built from this object's own
    /// <c>refreshHz</c> rather than injected, so the cadence the cache is kept at and the cadence
    /// the budget is sized for are one number and cannot disagree.
    /// </summary>
    private readonly PathRefreshBudget _budget;

    /// <summary>Enemy id to its slot in <see cref="_entries"/>. Pre-sized, so it never resizes.</summary>
    private readonly Dictionary<int, int> _slotById;

    private readonly Entry[] _entries;

    /// <summary>Slot indices nobody is using, in <c>[0, _freeCount)</c>. Used as a stack.</summary>
    private readonly int[] _free;

    /// <summary>Reused across every search, for the life of this object.</summary>
    private readonly NavMeshPath _path;

    /// <summary>
    /// Where corners are read. Two elements, because the only question asked of a route is which
    /// way it leaves — the rest of it is re-derived from the next position anyway.
    /// </summary>
    private readonly Vector3[] _corners = new Vector3[2];

    private int _freeCount;

    /// <summary>
    /// The <c>time</c> the current frame's calls are carrying. What the per-frame budget is reset
    /// against — every enemy in one frame is asked with the same value, so a change of value is a
    /// change of frame, and no explicit begin-frame call has to be remembered by the caller.
    /// </summary>
    private float _frameTime = float.NegativeInfinity;

    private int _refreshesThisFrame;

    /// <summary>What <see cref="PathRefreshBudget.ForFrame"/> allowed for the current frame.</summary>
    private int _allowedThisFrame = 1;

    /// <summary>How many enemies have asked during the current frame.</summary>
    /// <remarks>
    /// Counted rather than passed in, so the caller keeps the one-call-per-enemy shape it already
    /// has. The population it yields is the <em>previous</em> frame's, which is what the budget is
    /// sized from: a frame's allowance has to be decided before that frame's first caller is
    /// answered, and at 60 fps a wave arriving one body at a time is never more than one body out.
    /// </remarks>
    private int _asksThisFrame;

    private int _asksLastFrame;

    /// <summary>Accumulates <see cref="StalePathCount"/> as the current frame is answered.</summary>
    private int _staleThisFrame;

    /// <param name="capacity">
    /// The most enemies that may be cached at once. Matches the run's enemy capacity: a slot table
    /// smaller than the arena would spend every frame evicting enemies that are still standing.
    /// </param>
    /// <param name="refreshHz">
    /// How often one enemy's route is recomputed, in times per second. Ten is CC's cadence for
    /// things the player cannot see the seams of.
    /// </param>
    /// <param name="maxRefreshesPerFrame">
    /// The ceiling on a single frame's recomputes — see <see cref="PathRefreshBudget"/>. Defaulted
    /// rather than required, because every caller but the composition root wants the number the
    /// budget itself recommends.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="capacity"/> is not positive, <paramref name="refreshHz"/> is not positive
    /// and finite — a zero rate would divide to an infinite interval and quietly never path at all
    /// — or <paramref name="maxRefreshesPerFrame"/> is below 1.
    /// </exception>
    public NavPathSense(
        int capacity,
        float refreshHz = DefaultRefreshHz,
        int maxRefreshesPerFrame = PathRefreshBudget.DefaultMaxPerFrame)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "capacity must be greater than zero.");
        }

        // Negated positive: every comparison against NaN is false, so `<= 0f` would let one through
        // and the interval below would become NaN — which no elapsed time is ever greater than, so
        // nothing would ever be recomputed and every enemy would walk in a straight line for ever.
        if (!(refreshHz > 0f) || float.IsInfinity(refreshHz))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshHz),
                refreshHz,
                "refreshHz must be finite and greater than zero.");
        }

        _capacity = capacity;
        _refreshInterval = 1f / refreshHz;

        // After the rate has been guarded, so its own guard can only ever fire on the ceiling.
        _budget = new PathRefreshBudget(refreshHz, maxRefreshesPerFrame);

        _slotById = new Dictionary<int, int>(capacity);
        _entries = new Entry[capacity];
        _free = new int[capacity];

        for (int i = 0; i < capacity; i++)
        {
            // Filled backwards so the first rentals come out in ascending order, which makes a
            // hierarchy and a debugger read the way the arena was populated.
            _free[i] = capacity - 1 - i;
        }

        _freeCount = capacity;
        _path = new NavMeshPath();
    }

    /// <summary>
    /// Enemies whose path is overdue by more than one full refresh period. Zero when it is keeping
    /// up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The number ledger row 5 was actually missing.</b> A budget that cannot reach the whole
    /// population does not fail — routes simply age, and enemies walk into pillars while the arena
    /// looks stupid for no visible reason. This says how many of them are in that state, and
    /// <c>DebugOverlay</c> puts it on screen; a run that reads anything but zero with a full wave up
    /// is one where the cap and the pathfinder disagree.
    /// </para>
    /// <para>
    /// It is the count for the last <em>completed</em> frame, published as the next one opens, and
    /// deliberately not checked by a test: an EditMode scene has no baked NavMesh, so every search
    /// fails instantly and the number it would assert against is not the one a device produces.
    /// M2-05's manual step 2 is where it is verified.
    /// </para>
    /// </remarks>
    public int StalePathCount { get; private set; }

    /// <summary>
    /// The cached unit XZ direction along the path from <paramref name="from"/> towards
    /// <paramref name="to"/>, recomputing it when this enemy's turn has come round.
    /// </summary>
    /// <param name="enemyId">Whose route this is. Core's id, so the cache survives a pooled body.</param>
    /// <param name="from">Where the enemy is, in world metres.</param>
    /// <param name="to">Where the player is, in world metres.</param>
    /// <param name="time">
    /// Simulated run seconds — the same clock core counts, and constant across every call in one
    /// frame. That second property is load-bearing: it is how the per-frame budget knows a new frame
    /// has begun.
    /// </param>
    /// <returns>
    /// A unit direction, or zero when the two points are the same place. Before an enemy's first
    /// recompute — which the per-frame budget may push out by a frame or two — this is the straight
    /// line towards the player, which is what core would have fallen back to anyway.
    /// </returns>
    public Vector2 DirectionFor(int enemyId, Vector3 from, Vector3 to, float time)
    {
        if (time != _frameTime)
        {
            BeginFrame(time);
        }

        _asksThisFrame++;

        if (!TryGetSlot(enemyId, time, out int slot))
        {
            // Every slot belongs to an enemy that has already asked this frame, so there are more
            // live enemies than this cache was built for. Answered without caching rather than
            // refused: a straight line is what core falls back to in any case, and the arena keeps
            // running. Silent because the capacity mismatch is a composition fact that
            // WorldSnapshot's own capacity warning already reports from the other side — but it is
            // counted as stale, because an enemy with no slot is an enemy whose route is never
            // being computed at all.
            _staleThisFrame++;

            return StraightLine(from, to);
        }

        ref Entry entry = ref _entries[slot];

        entry.LastSeen = time;

        if (_refreshesThisFrame < _allowedThisFrame
            && time - entry.LastComputed >= _refreshInterval)
        {
            _refreshesThisFrame++;

            entry.LastComputed = time;
            entry.Direction = Compute(from, to);
        }

        // Measured after the refresh above, so an enemy whose turn came round this frame is never
        // counted: what this number reports is the ones the budget could not reach. A route that
        // has never been computed is measured from when its enemy arrived rather than from
        // negative infinity, or every spawn would arrive already overdue.
        float due = entry.LastComputed == float.NegativeInfinity ? entry.Arrived : entry.LastComputed;

        if (time - due > 2f * _refreshInterval)
        {
            _staleThisFrame++;
        }

        // The straight line until this enemy's first turn comes round. A zero would read to core as
        // "no path", which produces the same walk — but saying it out loud here keeps the sense
        // honest: the value returned is always a direction towards the player, never an absence.
        return entry.LastComputed == float.NegativeInfinity ? StraightLine(from, to) : entry.Direction;
    }

    /// <summary>
    /// Closes the frame that has just ended and sizes the new one's allowance from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The step is derived rather than passed, from the difference between two frames' times — the
    /// same clamped <c>Dt</c> the snapshot carries, since the caller's clock is the sum of them.
    /// The first frame of a run has no previous one to subtract, and answers zero, which the budget
    /// floors at a single recompute.
    /// </para>
    /// <para>
    /// <see cref="StalePathCount"/> is published here rather than as each enemy is answered, so a
    /// reader between two frames always sees one whole frame's count instead of however much of
    /// this one has been assembled so far.
    /// </para>
    /// </remarks>
    private void BeginFrame(float time)
    {
        float dt = float.IsNegativeInfinity(_frameTime) ? 0f : time - _frameTime;

        // A step that is not positive is a caller whose clock stood still or went backwards — a
        // paused run, or a second run through the same sense. Neither is an error and neither owes
        // the population a refresh, so the budget's floor of one is the whole answer.
        if (!(dt > 0f))
        {
            dt = 0f;
        }

        _frameTime = time;
        _refreshesThisFrame = 0;

        _asksLastFrame = _asksThisFrame;
        _asksThisFrame = 0;

        StalePathCount = _staleThisFrame;
        _staleThisFrame = 0;

        _allowedThisFrame = _budget.ForFrame(_asksLastFrame, dt);
    }

    /// <summary>
    /// Finds <paramref name="enemyId"/>'s slot, claiming a fresh one — reclaiming a stale one if
    /// it must — when the enemy has not been seen before.
    /// </summary>
    /// <returns><see langword="false"/> when every slot is in use by an enemy seen this frame.</returns>
    private bool TryGetSlot(int enemyId, float time, out int slot)
    {
        if (_slotById.TryGetValue(enemyId, out slot))
        {
            return true;
        }

        if (_freeCount == 0)
        {
            ReclaimSlotsNotSeen(time);
        }

        if (_freeCount == 0)
        {
            slot = -1;
            return false;
        }

        _freeCount--;
        slot = _free[_freeCount];

        _entries[slot] = new Entry
        {
            Id = enemyId,
            Direction = Vector2.zero,

            // Never computed, so the first call that has budget for it recomputes immediately
            // rather than waiting out an interval measured from a time it never ran at.
            LastComputed = float.NegativeInfinity,
            LastSeen = time,
            Arrived = time,
        };

        _slotById.Add(enemyId, slot);

        return true;
    }

    /// <summary>
    /// Frees every slot whose enemy did not ask this frame — the ones that have died, despawned, or
    /// left with the run that spawned them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Run only when a new enemy arrives and there is nothing free, which is once per spawn at
    /// worst rather than once per frame. It walks <see cref="_capacity"/> entries and allocates
    /// nothing: the dictionary's removals and the insert that follows reuse its internal free list,
    /// which is why it is pre-sized and never allowed to grow.
    /// </para>
    /// <para>
    /// Ids increase for the life of a run and are never reused (<c>EnemyRegistry</c> rule 1), so a
    /// reclaimed slot can never be handed a stale answer belonging to somebody else. That is the
    /// whole reason this is keyed by core's id rather than by a body.
    /// </para>
    /// </remarks>
    private void ReclaimSlotsNotSeen(float time)
    {
        for (int i = 0; i < _capacity; i++)
        {
            ref Entry entry = ref _entries[i];

            if (entry.Id == 0 || entry.LastSeen == time)
            {
                continue;
            }

            _slotById.Remove(entry.Id);

            entry.Id = 0;

            _free[_freeCount] = i;
            _freeCount++;
        }
    }

    /// <summary>
    /// Asks the NavMesh for a route and reads which way it leaves.
    /// </summary>
    /// <remarks>
    /// A partial or invalid route falls back to the straight line, and so does one with fewer than
    /// two corners — which is what a path already at its destination looks like. All three mean "the
    /// mesh has nothing better to offer than the obvious", and the obvious is exactly what the
    /// enemy did before this class existed.
    /// </remarks>
    private Vector2 Compute(Vector3 from, Vector3 to)
    {
        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _path)
            || _path.status != NavMeshPathStatus.PathComplete)
        {
            return StraightLine(from, to);
        }

        // GetCornersNonAlloc, never the corners property: that one allocates a fresh array on every
        // read, which at a handful of reads a frame for a whole run is the drip AR §14 bans.
        int corners = _path.GetCornersNonAlloc(_corners);

        if (corners < 2)
        {
            return StraightLine(from, to);
        }

        return Normalise(_corners[1] - _corners[0]);
    }

    /// <summary>The direction with no arena in it: straight at the player, on the ground plane.</summary>
    private static Vector2 StraightLine(Vector3 from, Vector3 to) => Normalise(to - from);

    /// <summary>
    /// Flattens <paramref name="delta"/> onto the ground plane and normalises it, or answers zero
    /// when there is no direction to be had.
    /// </summary>
    private static Vector2 Normalise(Vector3 delta)
    {
        var flat = new Vector2(delta.x, delta.z);

        float length = flat.magnitude;

        return length < MinDirectionDistance ? Vector2.zero : flat / length;
    }
}
