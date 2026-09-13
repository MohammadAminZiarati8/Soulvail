using System;
using System.Collections.Generic;
using UnityEngine;

namespace Soulvail.Game.Adapters;

/// <summary>
/// Whether each enemy can see the player, asked of the colliders once every tenth of a second per
/// enemy and cached in between. A sense in <see cref="NavPathSense"/>'s exact shape, and ledger
/// row 13's answer: GD §7.2 makes cover block enemy projectiles, and this is the only route to
/// that which does not make core hold a copy of the arena.
/// </summary>
/// <remarks>
/// <para>
/// <b>A sense, not a fact, and the difference is the word "standing".</b> AR §18.2 draws the line:
/// Unity owes core a <em>fact</em> when the answer depends on colliders core cannot see, and a
/// <em>sense</em> when the geometric question is a standing one rather than an instant. "Is there a
/// pillar between this Spitter and the player" is standing — it is true for as long as neither of
/// them moves — so it rides on the snapshot beside <c>PathDirectionToPlayer</c>, which is the same
/// shape of question about the same geometry. <c>EnemySense.HasLineOfSight</c> has existed unfilled
/// since M0-05, and CC §3.1's <em>"line-of-sight check: skip for V1, revisit when cover arrives"</em>
/// is the note that was waiting for this. The two alternatives were rejected at M2-07a and are not
/// re-argued here: a projectile view raycasting and reporting a block is the fact route, and core
/// holding a wall list is a second world model.
/// </para>
/// <para>
/// <b>Unknown means it can see</b> (<see cref="HasLineOfSight"/>), and the failure mode is what
/// decides that. A mis-wired sense answering <em>false</em> means no Spitter anywhere ever fires and
/// the archetype silently does nothing; one answering <em>true</em> degrades to exactly the
/// behaviour that shipped in M2-07b, which is visible, diagnosable and already playtested.
/// <c>NavPathSense</c> makes the same choice for the same reason — no path yet is the straight line,
/// not paralysis.
/// </para>
/// <para>
/// <b>The ray is three-dimensional, and it is the one perception in this project that is</b>
/// (AR §18.4). Every <em>distance</em> stays XZ — the height between two capsules is a rendering
/// detail and counting it would inflate every range check — but occlusion is not a separation, and a
/// pillar is a solid with a height. Both ends of the ray are lifted to <see cref="EyeHeight"/> so
/// that neither the floor nor a tier's lip can decide a fight; what is between them is then asked in
/// full 3D.
/// </para>
/// <para>
/// <b>The mask is <c>Cover</c> and nothing else.</b> Not the ground, not the arena's walls, and
/// above all not enemies: a Spitter that could not fire because a Husk was standing in front of it
/// would read as broken, and GD §7.2 makes cover a property of the arena rather than of the crowd.
/// M2-11a putting the pillars on a layer of their own is what makes one mask sufficient.
/// </para>
/// <para>
/// <b>Throttled exactly as the path cache is, and by the same class.</b> One refresh per enemy per
/// <c>1 / refreshHz</c> seconds, round-robin, inside a <see cref="PathRefreshBudget"/> that scales
/// with the population and the step — 5 raycasts a frame at 28 enemies and 60 fps, 10 at 30. A
/// 100 ms stale answer about a pillar that cannot move is imperceptible; 28 raycasts every frame on
/// a phone is not free.
/// </para>
/// <para>
/// <b>Nothing allocates.</b> A pre-sized slot table with a free list — <c>EnemyRegistry</c>'s shape,
/// and <c>NavPathSense</c>'s — one <see cref="RaycastHit"/> reused for the life of the object, and
/// the non-allocating <c>Physics.Raycast</c> overload.
/// </para>
/// </remarks>
public sealed class LineOfSightSense
{
    /// <summary>
    /// How high off each end's own floor the ray runs, in metres.
    /// </summary>
    /// <remarks>
    /// Chest height on a 2 m capsule: low enough that GD §7.2's 1.5 m pillar blocks it, high enough
    /// that the floor, a kerb or a tier's lip does not. Both ends are lifted by it, so a ray between
    /// two bodies standing on the same floor is horizontal — which is what stops the geometry
    /// underneath either of them deciding whether a Spitter may fire.
    /// </remarks>
    public const float EyeHeight = 1.1f;

    /// <summary>
    /// How often one enemy's line of sight is re-measured by default, in times per second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately the same number as <c>NavPathSense.DefaultRefreshHz</c> and deliberately not a
    /// reference to it: the two caches answer different questions and are free to diverge, and the
    /// day one of them has to, a shared constant would move both in silence.
    /// </para>
    /// <para>
    /// Named rather than left as a literal on the constructor alone, because VContainer does not
    /// honour a C# default value — it resolves every constructor parameter or throws — so
    /// <c>RunScope</c> has to pass this number, and it should be passing the same one the default
    /// says.
    /// </para>
    /// </remarks>
    public const float DefaultRefreshHz = 10f;

    /// <summary>
    /// Below this the two points are the same place and there is nothing to be between them. Not
    /// zero, because a ray of length 1e-9 has a direction made of floating-point noise.
    /// </summary>
    private const float MinRayLength = 1e-4f;

    /// <summary>One cached answer, and the clocks that decide when it is replaced.</summary>
    private struct Entry
    {
        /// <summary>Whose answer this is. Needed to un-index the slot when it is reclaimed.</summary>
        public int Id;

        /// <summary>Whether cover stood between the two points at <see cref="LastMeasured"/>.</summary>
        public bool Blocked;

        /// <summary>When that was measured. Negative infinity means "never" — see the class remarks.</summary>
        public float LastMeasured;

        /// <summary>
        /// The last frame this enemy asked. What makes a slot reclaimable: an enemy that did not
        /// ask this frame is one the arena no longer has.
        /// </summary>
        public float LastSeen;
    }

    private readonly int _capacity;
    private readonly LayerMask _cover;
    private readonly PathRefreshBudget _budget;
    private readonly float _refreshInterval;

    /// <summary>Enemy id to its slot in <see cref="_entries"/>. Pre-sized, so it never resizes.</summary>
    private readonly Dictionary<int, int> _slotById;

    private readonly Entry[] _entries;

    /// <summary>Slot indices nobody is using, in <c>[0, _freeCount)</c>. Used as a stack.</summary>
    private readonly int[] _free;

    /// <summary>Reused across every query, for the life of this object.</summary>
    private RaycastHit _hit;

    private int _freeCount;

    /// <summary>
    /// The <c>time</c> the current frame's calls are carrying. What the per-frame budget is reset
    /// against — every enemy in one frame is asked with the same value, so a change of value is a
    /// change of frame and no explicit begin-frame call has to be remembered by the caller.
    /// </summary>
    private float _frameTime = float.NegativeInfinity;

    private int _allowedThisFrame = 1;

    /// <param name="capacity">
    /// The most enemies that may be cached at once. Matches the run's enemy capacity: a slot table
    /// smaller than the arena would spend every frame evicting enemies that are still standing.
    /// </param>
    /// <param name="cover">
    /// The layers that block a shot — the <c>Cover</c> layer M2-11a put the pillars on, and nothing
    /// else. See the class remarks on why enemies are not on it.
    /// </param>
    /// <param name="budget">
    /// How many raycasts one frame may spend. The same class <c>NavPathSense</c> throttles with,
    /// taken rather than built so the composition root states the cadence once for both.
    /// </param>
    /// <param name="refreshHz">
    /// How often one enemy's answer is re-measured, in times per second. Must be the rate
    /// <paramref name="budget"/> was sized for, or the cache and its allowance describe different
    /// games.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="budget"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="capacity"/> is not positive, or <paramref name="refreshHz"/> is not finite
    /// and positive — a zero rate divides to an infinite interval and would quietly never measure
    /// anything at all.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="cover"/> is empty. Guarded here as well as at the composition root, because
    /// the two failures read differently: this can only say "this mask is empty", while
    /// <c>RunScope</c> can say which field on which object to fix. An empty mask is not a degraded
    /// run — it is a game in which GD §7.2's cover silently does nothing, which is precisely the
    /// state this class exists to leave.
    /// </exception>
    public LineOfSightSense(
        int capacity,
        LayerMask cover,
        PathRefreshBudget budget,
        float refreshHz = DefaultRefreshHz)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "capacity must be greater than zero.");
        }

        if (cover.value == 0)
        {
            throw new ArgumentException(
                "cover must name at least one layer. An empty mask blocks nothing, so every "
                    + "Spitter would shoot straight through every pillar (GD §7.2).",
                nameof(cover));
        }

        _budget = budget ?? throw new ArgumentNullException(nameof(budget));

        // Negated positive: every comparison against NaN is false, so `<= 0f` would let one through
        // and the interval below would become NaN — which no elapsed time is ever greater than, so
        // nothing would ever be re-measured and every answer would be the first one for ever
        // (AR §18.3).
        if (!(refreshHz > 0f) || float.IsInfinity(refreshHz))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshHz),
                refreshHz,
                "refreshHz must be finite and greater than zero.");
        }

        _capacity = capacity;
        _cover = cover;
        _refreshInterval = 1f / refreshHz;

        _slotById = new Dictionary<int, int>(capacity);
        _entries = new Entry[capacity];
        _free = new int[capacity];

        for (int i = 0; i < capacity; i++)
        {
            // Filled backwards so the first rentals come out in ascending order, which makes a
            // debugger read the way the arena was populated.
            _free[i] = capacity - 1 - i;
        }

        _freeCount = capacity;
    }

    /// <summary>
    /// Raycasts spent on the frame in progress, after the budget clamped them.
    /// </summary>
    /// <remarks>
    /// Counted live rather than published as the frame closes, unlike
    /// <c>NavPathSense.StalePathCount</c>: read between two frames — which is where
    /// <c>DebugOverlay</c> reads it, after the tick that asked every enemy — it is the count for the
    /// frame that has just been answered, which is what the name says. A run showing 28 here with a
    /// full arena is one where the budget is not being applied at all.
    /// </remarks>
    public int RaycastsLastFrame { get; private set; }

    /// <summary>
    /// How many of the enemies asked this frame had cover between them and the player. Zero in an
    /// arena nobody is hiding in.
    /// </summary>
    /// <remarks>
    /// The number that says the feature is alive. A raycast count proves the budget is working; this
    /// proves the mask, the eye height and the geometry agree — a run where a player standing behind
    /// a pillar never moves this off zero has a sense that is measuring the wrong thing, and without
    /// it the only symptom is a Spitter that keeps firing.
    /// </remarks>
    public int BlockedCount { get; private set; }

    /// <summary>
    /// Whether <paramref name="enemyId"/> can see <paramref name="to"/> from <paramref name="from"/>,
    /// as of its last measurement.
    /// </summary>
    /// <param name="enemyId">Whose answer this is. Core's id, so the cache survives a pooled body.</param>
    /// <param name="from">Where the enemy is, in world metres, at its own floor.</param>
    /// <param name="to">Where the player is, in world metres, at their own floor.</param>
    /// <param name="time">
    /// Simulated run seconds — the same clock core counts, and constant across every call in one
    /// frame. That second property is load-bearing: it is how the per-frame budget knows a new frame
    /// has begun.
    /// </param>
    /// <param name="activeCount">
    /// How many enemies are asking this frame. Passed rather than counted, unlike
    /// <c>NavPathSense</c>'s: this sense's caller already holds the number — it is
    /// <c>WorldSnapshot.EnemyCount</c>, decided before the first enemy is answered — so the budget
    /// can be sized from <em>this</em> frame's population rather than from the previous one's.
    /// </param>
    /// <param name="dt">
    /// Seconds this frame took. The snapshot's clamped step, never <c>Time.deltaTime</c> (AR §18.2).
    /// </param>
    /// <returns>
    /// <see langword="true"/> when nothing on the cover mask stood between the two points at the
    /// last measurement — and <see langword="true"/> when there has never been one, which is the
    /// permissive default the class remarks argue for.
    /// </returns>
    public bool HasLineOfSight(
        int enemyId,
        Vector3 from,
        Vector3 to,
        float time,
        int activeCount,
        float dt)
    {
        if (time != _frameTime)
        {
            BeginFrame(time, activeCount, dt);
        }

        if (!TryGetSlot(enemyId, time, out int slot))
        {
            // Every slot belongs to an enemy that has already asked this frame, so there are more
            // live enemies than this cache was built for. Answered without caching rather than
            // refused, and answered *true*: a capacity mismatch is a composition fault that
            // WorldSnapshot's own capacity warning already reports from the other side, and the one
            // thing it must not do is silently switch off an archetype.
            return true;
        }

        ref Entry entry = ref _entries[slot];

        entry.LastSeen = time;

        if (RaycastsLastFrame < _allowedThisFrame
            && time - entry.LastMeasured >= _refreshInterval)
        {
            RaycastsLastFrame++;

            entry.LastMeasured = time;
            entry.Blocked = IsBlocked(from, to);
        }

        if (entry.Blocked)
        {
            BlockedCount++;
        }

        return !entry.Blocked;
    }

    /// <summary>
    /// Forgets every cached answer. Called when the arena underneath the ids has been swapped.
    /// </summary>
    /// <remarks>
    /// <b>About geometry, not identity.</b> Ids are never reused within a run
    /// (<c>EnemyRegistry</c>'s rule), so a slot can never be handed a stale answer belonging to
    /// somebody else — what goes wrong without this is subtler and worse: an answer about the
    /// pillars of the room the player has just left, held for up to a tenth of a second in a room
    /// that has different ones. No answer is better than that, because no answer means "it can see",
    /// which is the state the arena was in before this class existed.
    /// </remarks>
    public void Clear()
    {
        _slotById.Clear();

        for (int i = 0; i < _capacity; i++)
        {
            _entries[i] = default;
            _free[i] = _capacity - 1 - i;
        }

        _freeCount = _capacity;

        // So the next call opens a frame rather than continuing the one the old arena was torn down
        // in — otherwise the first frame in the new room inherits whatever allowance was left.
        _frameTime = float.NegativeInfinity;
    }

    /// <summary>Opens a frame and sizes its allowance.</summary>
    private void BeginFrame(float time, int activeCount, float dt)
    {
        _frameTime = time;

        RaycastsLastFrame = 0;
        BlockedCount = 0;

        // A population or a step that arrives negative is a caller whose clock went backwards or
        // whose census has not been written yet; neither is an error worth throwing a frame over,
        // and the budget's own floor of one is the whole answer.
        int population = activeCount < 0 ? 0 : activeCount;
        float step = !(dt > 0f) || float.IsInfinity(dt) ? 0f : dt;

        _allowedThisFrame = _budget.ForFrame(population, step);
    }

    /// <summary>
    /// Finds <paramref name="enemyId"/>'s slot, claiming a fresh one — reclaiming a stale one if it
    /// must — when the enemy has not been seen before.
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

            // Never measured, so the first call that has budget for it measures immediately rather
            // than waiting out an interval counted from a time it never ran at — and answers "can
            // see" until then.
            Blocked = false,
            LastMeasured = float.NegativeInfinity,
            LastSeen = time,
        };

        _slotById.Add(enemyId, slot);

        return true;
    }

    /// <summary>
    /// Frees every slot whose enemy did not ask this frame — the ones that have died, despawned, or
    /// left with the stage that spawned them.
    /// </summary>
    /// <remarks>
    /// <c>NavPathSense.ReclaimSlotsNotSeen</c>'s shape and its guarantees: run only when a new enemy
    /// arrives and nothing is free, walking <see cref="_capacity"/> entries and allocating nothing,
    /// because the dictionary is pre-sized and never allowed to grow.
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
    /// Whether anything on the cover mask stands between the two points, at eye height on both ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Triggers are ignored outright. A trigger volume is a region, not a wall, and GD §7.2's cover
    /// is a solid the player can stand behind — the one trigger collider anywhere near this question
    /// is the one every <c>EnemyView</c> carries, which is not on the mask anyway.
    /// </para>
    /// <para>
    /// The non-allocating overload, writing into a field rather than a local, for the reason
    /// <c>NavPathSense</c> reads corners into a buffer: the alternative is a per-enemy cost on
    /// exactly the frames that already have the most happening in them (AR §14).
    /// </para>
    /// </remarks>
    private bool IsBlocked(Vector3 from, Vector3 to)
    {
        Vector3 origin = from;
        Vector3 target = to;

        origin.y += EyeHeight;
        target.y += EyeHeight;

        Vector3 delta = target - origin;

        float distance = delta.magnitude;

        if (distance < MinRayLength)
        {
            return false;
        }

        return Physics.Raycast(
            origin,
            delta / distance,
            out _hit,
            distance,
            _cover,
            QueryTriggerInteraction.Ignore);
    }
}
