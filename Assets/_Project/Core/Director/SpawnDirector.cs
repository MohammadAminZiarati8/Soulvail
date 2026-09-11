using System;
using System.Collections.Generic;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Director;

/// <summary>
/// A composed stage actually happening: waves that start, overlap and clear; bodies that announce
/// themselves before they exist; and positions that are never on top of the player or on top of
/// each other. GD §7.1, §7.3, §11.3 and §12.4.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="WaveComposer"/> decides what a stage is made of; this decides when and where.</b>
/// The split is what makes both testable: a composition is a pure function of the stage and the
/// stream's position, and the pacing below is a pure function of the plan, the clock and where the
/// player is standing. Neither needs the other's fixture.
/// </para>
/// <para>
/// <b>It publishes three things and ends nothing.</b> Waves start, waves clear, spawns are
/// telegraphed — and when the stage is over it says so through <see cref="IsStageComplete"/>,
/// asked rather than announced. Arrival, the seal, the gate and advancing the depth are M2-10's,
/// and a <c>StageCleared</c> published here would be a second answer to a question that must have
/// exactly one (rule 6).
/// </para>
/// <para>
/// <b>Nothing on the per-tick path allocates.</b> The queue, the pending telegraphs, the per-wave
/// id lists and the claim table are all arrays sized from the plan when a stage begins and reused
/// for every stage after — a stage boundary is a moment the player is standing still, and a GC
/// spike there is as visible as one mid-fight (<see cref="WavePlan"/>'s own reasoning).
/// </para>
/// <para>
/// <b>Every draw comes from the <c>Spawn</c> stream and there is exactly one per attempt</b>
/// (ADR-0011, rule 9). Stream consumption that depended on where the player happened to be
/// standing is the one thing a seed cannot survive, so a refused position costs the same single
/// draw as an accepted one — the same rule <c>EnemySystem.ApplyRespawn</c> keeps for the same
/// reason.
/// </para>
/// </remarks>
public sealed class SpawnDirector
{
    /// <summary>
    /// Seconds between a spawn being announced and the body appearing — GD §7.1's ring.
    /// </summary>
    /// <remarks>
    /// Long enough to walk out of, at the Oathbound's 3 m/s: 2.4 m against a 6 m clearance. Whether
    /// it reads as fair warning or as too late with twenty-eight of them on a phone screen is a
    /// device question, and it is on the deferred list rather than answered here.
    /// </remarks>
    public const float TelegraphTime = 0.8f;

    /// <summary>Metres of clearance every spawn owes the player — GD §12.4.</summary>
    /// <remarks>
    /// Measured on XZ, like every other separation in the game (AR §18.4): the height between a
    /// player capsule's centre and an enemy's is a rendering detail that would inflate this.
    /// </remarks>
    public const float MinPlayerDistance = 6f;

    /// <summary>
    /// Metres two simultaneous spawns owe each other — <see href="../../../../Docs/plan/ROADMAP.md">ledger row 9</see>.
    /// </summary>
    /// <remarks>
    /// Roughly two bodies wide. It is checked against <em>claimed</em> points rather than against
    /// living enemies, because the thing being prevented is two rings on one patch of floor and a
    /// Husk that walks away from where it arrived is nobody's problem.
    /// </remarks>
    public const float MinSpawnSeparation = 2f;

    /// <summary>The share of a wave that may still be standing when the next one starts — GD §7.3.</summary>
    /// <remarks>
    /// Rounded up, so a wave of three overlaps at one survivor rather than at zero: rounding down
    /// would make every small wave wait for a total wipe, which is the "clear the room, then wait"
    /// rhythm GD §7.3 exists to remove.
    /// </remarks>
    public const float OverlapFraction = 0.25f;

    /// <summary>Seconds between one body of a wave and the next.</summary>
    /// <remarks>
    /// A wave that dumped eleven rings in a frame would be unreadable, which GD §11.3 and P1 both
    /// refuse. At this spacing a wave of ten arrives over three and a half seconds — a trickle the
    /// player can react to one ring at a time, which is also what makes the 6 m clearance mean
    /// something.
    /// </remarks>
    public const float SpawnInterval = 0.35f;

    /// <summary>Compared squared, so a position check never takes a square root.</summary>
    private const float MinSeparationSquared = MinSpawnSeparation * MinSpawnSeparation;

    /// <inheritdoc cref="MinSeparationSquared" />
    private const float MinPlayerDistanceSquared = MinPlayerDistance * MinPlayerDistance;

    private readonly EnemySystem _enemies;
    private readonly IDomainEvents _events;

    /// <summary>
    /// Where a body may be put. Empty for an arena with no spawning surface, which makes this
    /// object inert rather than an error — see rule 12 on <see cref="Tick"/>.
    /// </summary>
    private readonly IReadOnlyList<Vector3> _spawnPoints;

    /// <summary>
    /// When each spawn point stops being claimed, in simulated run seconds. Negative infinity for
    /// a point nobody holds.
    /// </summary>
    /// <remarks>
    /// One slot per point rather than a list of claims, because a point can only be held by one
    /// spawn at a time: the claim is taken when the ring goes up and released
    /// <see cref="TelegraphTime"/> after the body appears, and nothing can take it in between.
    /// </remarks>
    private readonly float[] _claimExpiry;

    /// <summary>The stage being run, or 0 when there is no plan.</summary>
    private int _stage;

    private WavePlan _plan;

    /// <summary>The wave's bodies in the order they will be telegraphed, round-robin by entry.</summary>
    private ContentId[] _queue = Array.Empty<ContentId>();

    private int _queueCount;
    private int _queueHead;

    /// <summary>How many of each of the current wave's entries are still to be queued.</summary>
    private int[] _remaining = Array.Empty<int>();

    private ContentId[] _pendingSpec = Array.Empty<ContentId>();
    private Vector3[] _pendingPosition = Array.Empty<Vector3>();
    private float[] _pendingFiresAt = Array.Empty<float>();
    private int[] _pendingWave = Array.Empty<int>();
    private int _pendingCount;

    /// <summary>
    /// The ids each wave spawned that are still breathing, wave-major: wave <c>w</c> starts at
    /// <c>(w−1) · _idStride</c>. Compacted as they die, which is what makes a wave's survivor count
    /// free and its clear moment exact.
    /// </summary>
    private int[] _waveIds = Array.Empty<int>();

    private int _idStride;
    private int[] _waveIdCount = Array.Empty<int>();

    /// <summary>How many of each wave's bodies have not been spawned yet — queued or telegraphed.</summary>
    private int[] _waveOutstanding = Array.Empty<int>();

    private bool[] _waveStarted = Array.Empty<bool>();
    private bool[] _waveCleared = Array.Empty<bool>();

    /// <summary>The earliest this stage may telegraph its next body. Rule 3's whole implementation.</summary>
    private float _nextSpawnAt = float.NegativeInfinity;

    /// <param name="enemies">Who brings a body into being, and who knows how many are breathing.</param>
    /// <param name="events">Where the three events go.</param>
    /// <param name="spawnPoints">
    /// Where a body may be put, in world metres. Held rather than copied: it comes from the run's
    /// immutable <c>SpawnPlan</c>, which already made the copy.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A spawn point has a non-finite component. A NaN here is permanent and silent: every distance
    /// comparison against it is false, so the point is neither accepted nor reported — it simply
    /// never receives anything, and the arena looks like it has fewer spawn points than it does.
    /// </exception>
    public SpawnDirector(
        EnemySystem enemies,
        IDomainEvents events,
        IReadOnlyList<Vector3> spawnPoints)
    {
        _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _spawnPoints = spawnPoints ?? throw new ArgumentNullException(nameof(spawnPoints));

        for (int i = 0; i < _spawnPoints.Count; i++)
        {
            if (!IsFinite(_spawnPoints[i]))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(spawnPoints),
                    _spawnPoints[i],
                    $"spawnPoints[{i}] must be finite in every component.");
            }
        }

        _claimExpiry = new float[_spawnPoints.Count];

        ReleaseClaims();
    }

    /// <summary>The depth being run, or 0 before <see cref="Begin"/> and after <see cref="Clear"/>.</summary>
    public int Stage => _stage;

    /// <summary>The wave being spawned, numbered from 1. Zero until the first wave starts.</summary>
    public int Wave { get; private set; }

    /// <summary>How many bodies have been telegraphed and are not yet standing.</summary>
    /// <remarks>
    /// Public because it is half of the concurrency arithmetic (rule 8) and therefore half of what
    /// an overlay showing "alive versus cap" would otherwise mis-state — at these constants it is
    /// at most three.
    /// </remarks>
    public int PendingCount => _pendingCount;

    /// <summary>
    /// Every wave has started, every body has been spawned, and every one of them is dead.
    /// </summary>
    /// <remarks>
    /// <b>M2-10 reads this and nothing else does.</b> It is deliberately not an event: ending a
    /// stage is stage flow's decision, and a stage-cleared event published from here would be a
    /// second publisher of the one fact the gate, the door and the depth all hang off (rule 6).
    /// False while any wave still holds a body, including a wave the overlap left behind three
    /// waves ago.
    /// </remarks>
    public bool IsStageComplete
    {
        get
        {
            if (_plan is null || Wave < _plan.WaveCount)
            {
                return false;
            }

            for (int w = 0; w < _plan.WaveCount; w++)
            {
                if (!_waveCleared[w])
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Adopts a composed stage and starts its first wave.
    /// </summary>
    /// <param name="plan">The stage's composition. Held, not copied — the run owns one for its life.</param>
    /// <param name="now">Simulated run seconds, the same clock everything else in core counts.</param>
    /// <remarks>
    /// <b>Wave 1 starts immediately, with no arrival beat.</b> GD §7.1's two seconds of arrival
    /// belong to M2-10, which has a door to shut and a camera to settle first; a delay inserted
    /// here would be a pause with nothing to explain it, and two tasks would then each own a piece
    /// of the same silence (rule 1).
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="now"/> is not finite.</exception>
    /// <exception cref="ArgumentException">
    /// Nothing has been composed into <paramref name="plan"/>. A plan with no waves would leave
    /// this object reporting a stage it cannot run, and the failure would surface a frame later as
    /// an arena that stays empty.
    /// </exception>
    public void Begin(WavePlan plan, float now)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        RequireFinite(now, nameof(now));

        if (plan.WaveCount < 1)
        {
            throw new ArgumentException(
                "This plan holds no composition — nothing has been composed into it, so it has no "
                    + "waves to run. Call WaveComposer.Compose first.",
                nameof(plan));
        }

        EnsureCapacity(plan);

        _plan = plan;
        _stage = plan.Stage;
        Wave = 0;
        _queueCount = 0;
        _queueHead = 0;
        _pendingCount = 0;

        // Every wave's bookkeeping, not only the ones this stage uses: a stage of two waves
        // adopted after one of five would otherwise read wave 3 as started and cleared. Bounded by
        // WaveCount everywhere, so this is belt and braces — and it is the belt that stops a
        // reused array being the thing a future reader trips over (WavePlan.Begin's reasoning).
        Array.Clear(_waveIdCount, 0, _waveIdCount.Length);
        Array.Clear(_waveOutstanding, 0, _waveOutstanding.Length);
        Array.Clear(_waveStarted, 0, _waveStarted.Length);
        Array.Clear(_waveCleared, 0, _waveCleared.Length);

        // A new stage is a new arena. Claims are released outright rather than left to expire,
        // because the clock they expire against is the run's and a stage boundary does not move it.
        ReleaseClaims();

        _nextSpawnAt = float.NegativeInfinity;

        StartWave(1, now);
    }

    /// <summary>
    /// One tick of the stage: bodies that are due appear, the dead are noticed, the next wave
    /// starts if it may, and one more body is announced if it is time.
    /// </summary>
    /// <param name="now">Simulated run seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <param name="playerPosition">Who every spawn owes <see cref="MinPlayerDistance"/> of clearance.</param>
    /// <param name="spawn">
    /// The <c>Spawn</c> stream, and only ever that one (ADR-0011). A parameter rather than a field
    /// read, so the one draw this method can make is visible in its signature — the shape
    /// <c>EnemySystem.ApplyRespawn</c> already uses.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The order inside the tick is the contract.</b> Telegraphs fire first, so a body due this
    /// tick is standing before anything counts the living; the dead are swept next, so the overlap
    /// rule sees this tick's kills rather than last tick's; the next wave starts after that; and
    /// only then is a new body announced. Written the other way round, a wave would overlap one
    /// tick late and the cap would be measured against a census that was already wrong.
    /// </para>
    /// <para>
    /// <b>A tick with no plan does nothing and says nothing</b> — before <see cref="Begin"/>, after
    /// <see cref="Clear"/>, and for the whole of a mode whose content comes entirely from its
    /// spawn plan.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="spawn"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="now"/> or <paramref name="playerPosition"/> is not finite. Guarded on a
    /// per-frame path, unusually, because the failure it prevents is silence rather than a crash:
    /// every comparison against a NaN is false, so a NaN clock stops the pacing and a NaN position
    /// refuses every spawn point, and in both cases the arena simply stays empty with nothing in
    /// the log (AR §18.3).
    /// </exception>
    public void Tick(float now, Vector3 playerPosition, IRandomStream spawn)
    {
        if (spawn is null)
        {
            throw new ArgumentNullException(nameof(spawn));
        }

        RequireFinite(now, nameof(now));

        if (!IsFinite(playerPosition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerPosition),
                playerPosition,
                "playerPosition must be finite in every component.");
        }

        if (_plan is null)
        {
            return;
        }

        FireDueTelegraphs(now);

        SweepTheDead();

        MaybeStartNextWave(now);

        MaybeTelegraph(now, playerPosition, spawn);
    }

    /// <summary>
    /// Forgets the plan, the wave, the telegraphs in flight and the claims — and says nothing.
    /// </summary>
    /// <remarks>
    /// Silent for <c>EnemySystem.Clear</c>'s reason, at the same moment: the scope is going away
    /// and with it every subscriber an event could reach, so a farewell per pending ring would be
    /// noise at best. A telegraph dropped here never becomes a body, which is the one exception to
    /// rule 7 and is not really one — there is no arena left for it to appear in.
    /// </remarks>
    public void Clear()
    {
        _plan = null;
        _stage = 0;
        Wave = 0;
        _queueCount = 0;
        _queueHead = 0;
        _pendingCount = 0;
        _nextSpawnAt = float.NegativeInfinity;

        // Released rather than left to expire, so the next stage's first tick sees an arena nobody
        // is holding a place in — a claim outlives its stage by 1.6 s otherwise, which is exactly
        // long enough to refuse the opening wave's first position.
        ReleaseClaims();
    }

    /// <summary>Spawns every body whose ring has finished, in the order they were announced.</summary>
    /// <remarks>
    /// <b>Nothing is consulted before the spawn.</b> Not the cap, not the player's position, not
    /// whether the point is still sensible — the promise was made when the ring went up (rule 7).
    /// A player who dodged a telegraph and found nothing there, or found it two metres away, learns
    /// not to trust the next one, and there is no version of that trade worth making.
    /// </remarks>
    private void FireDueTelegraphs(float now)
    {
        int kept = 0;

        for (int i = 0; i < _pendingCount; i++)
        {
            if (_pendingFiresAt[i] > now)
            {
                // Compacted rather than left in place, because the ones that fired are removed
                // from the middle of the run of pendings and the order of the rest must survive.
                if (kept != i)
                {
                    _pendingSpec[kept] = _pendingSpec[i];
                    _pendingPosition[kept] = _pendingPosition[i];
                    _pendingFiresAt[kept] = _pendingFiresAt[i];
                    _pendingWave[kept] = _pendingWave[i];
                }

                kept++;
                continue;
            }

            int wave = _pendingWave[i];

            EnemyAgent agent = _enemies.Spawn(_pendingSpec[i], _pendingPosition[i]);

            int index = wave - 1;

            _waveIds[(index * _idStride) + _waveIdCount[index]] = agent.Id;
            _waveIdCount[index]++;
            _waveOutstanding[index]--;
        }

        _pendingCount = kept;
    }

    /// <summary>
    /// Drops every id whose body has stopped breathing, and announces any wave that just lost its
    /// last one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every uncleared wave is walked, not only the current one.</b> The overlap means a wave
    /// three back can still be holding a survivor, and <see cref="WaveCleared"/> has to be true of
    /// that wave rather than of whichever one happens to be spawning.
    /// </para>
    /// <para>
    /// The cost is one registry lookup per id still being tracked, and an id stops being tracked
    /// the tick it dies — so this is bounded by the living population, which the concurrency cap
    /// bounds in turn (rule 11). A dead id is never looked up twice.
    /// </para>
    /// </remarks>
    private void SweepTheDead()
    {
        for (int w = 0; w < _plan.WaveCount; w++)
        {
            if (!_waveStarted[w] || _waveCleared[w])
            {
                continue;
            }

            int start = w * _idStride;
            int kept = 0;

            for (int i = 0; i < _waveIdCount[w]; i++)
            {
                int id = _waveIds[start + i];

                // Registered is not breathing (AR §18.4): a corpse sits in the registry for
                // EnemySystem.CorpseTime while its dissolve plays, and a wave the player has
                // finished killing must clear on the kill rather than 0.6 s later.
                if (!_enemies.Registry.TryGet(id, out EnemyAgent agent) || !agent.IsAlive)
                {
                    continue;
                }

                _waveIds[start + kept] = id;
                kept++;
            }

            _waveIdCount[w] = kept;

            if (kept > 0 || _waveOutstanding[w] > 0)
            {
                continue;
            }

            _waveCleared[w] = true;

            _events.Publish(new WaveCleared(_stage, w + 1));
        }
    }

    /// <summary>
    /// Starts the next wave once the current one is spent and a quarter or less of it is standing.
    /// </summary>
    /// <remarks>
    /// The queue emptying is checked as well as the survivors, and it is the half that is easy to
    /// forget: a wave whose bodies are still arriving is not "nearly dead", it is barely born, and
    /// without this a wave refused a safe position for a second would overlap itself.
    /// </remarks>
    private void MaybeStartNextWave(float now)
    {
        if (Wave < 1 || Wave >= _plan.WaveCount)
        {
            return;
        }

        int index = Wave - 1;

        // Queued, telegraphed, or both. One number rather than two checks, and it is the same
        // number the clear rule reads — so "this wave is spent" cannot mean two things.
        if (_waveOutstanding[index] > 0)
        {
            return;
        }

        if (_waveIdCount[index] > OverlapThreshold(_plan.BodyCount(Wave)))
        {
            return;
        }

        StartWave(Wave + 1, now);
    }

    /// <summary>
    /// Announces one body, if one is queued, the interval has passed, the arena has room, and there
    /// is somewhere to put it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gates above the draw are in cost order and the draw is the last of them</b>, which is
    /// rule 9's real content: an attempt that never happened consumes nothing, and an attempt that
    /// happened consumes exactly one draw however it ends. A stream whose position depended on how
    /// full the arena was, or on where the player stood, would replay a seed differently the moment
    /// either changed.
    /// </para>
    /// <para>
    /// <b>The cap counts the pending</b> (rule 8). Counting only the living would let every ring in
    /// flight overshoot it — three, at these constants — and the overshoot would land as three
    /// bodies over the number the device was measured at, on exactly the frame the arena is
    /// fullest.
    /// </para>
    /// </remarks>
    private void MaybeTelegraph(float now, Vector3 playerPosition, IRandomStream spawn)
    {
        if (_queueHead >= _queueCount || now < _nextSpawnAt)
        {
            return;
        }

        int points = _spawnPoints.Count;

        // Rule 12: an arena with nowhere to put anything makes this object inert rather than
        // broken, and inert has to include drawing nothing — a seed whose stream advanced
        // according to how an arena was dressed would not replay in a differently dressed one.
        if (points == 0)
        {
            return;
        }

        if (_enemies.LivingCount() + _pendingCount >= _plan.Concurrency)
        {
            return;
        }

        int first = spawn.NextInt(0, points);

        for (int i = 0; i < points; i++)
        {
            int index = first + i;

            if (index >= points)
            {
                index -= points;
            }

            Vector3 candidate = _spawnPoints[index];

            if (DistanceSquaredXZ(candidate, playerPosition) < MinPlayerDistanceSquared)
            {
                continue;
            }

            if (!IsClearOfClaims(candidate, now))
            {
                continue;
            }

            Telegraph(index, candidate, now);

            return;
        }

        // Every point is inside the player's clearance or too close to a ring already up. Nothing
        // is announced and nothing is said about it: the player will move, the claims will expire,
        // and GD §12.4's rule is that a spawn on top of you is worse than a pause.
    }

    /// <summary>Announces the next queued body at <paramref name="position"/> and claims the point.</summary>
    private void Telegraph(int index, Vector3 position, float now)
    {
        ContentId specId = _queue[_queueHead];

        _queueHead++;

        float firesAt = now + TelegraphTime;

        _pendingSpec[_pendingCount] = specId;
        _pendingPosition[_pendingCount] = position;
        _pendingFiresAt[_pendingCount] = firesAt;
        _pendingWave[_pendingCount] = Wave;
        _pendingCount++;

        // Held until TelegraphTime *after* the body lands, not until it lands — ledger row 9. The
        // second ring is what the claim is really about: two bodies appearing a metre apart read as
        // one glitch rather than as two enemies, and the extra 0.8 s is how long it takes to be
        // obvious there are two of them.
        _claimExpiry[index] = firesAt + TelegraphTime;

        _nextSpawnAt = now + SpawnInterval;

        _events.Publish(new SpawnTelegraphed(specId, position, firesAt));
    }

    /// <summary>
    /// Opens wave <paramref name="wave"/>: announces it and queues its bodies, interleaved.
    /// </summary>
    /// <remarks>
    /// <b>Round-robin across the wave's entries</b> (rule 2), so a wave of nine Husks and two
    /// Bloaters arrives mixed rather than as nine of one followed by two of the other. The plan
    /// says what a wave holds and deliberately says nothing about order; this is the whole of the
    /// order.
    /// </remarks>
    private void StartWave(int wave, float now)
    {
        int index = wave - 1;
        int entries = _plan.EntryCount(wave);
        int bodies = _plan.BodyCount(wave);

        for (int e = 0; e < entries; e++)
        {
            _remaining[e] = _plan.Entry(wave, e).Count;
        }

        _queueCount = 0;
        _queueHead = 0;

        while (_queueCount < bodies)
        {
            for (int e = 0; e < entries; e++)
            {
                if (_remaining[e] == 0)
                {
                    continue;
                }

                _queue[_queueCount] = _plan.Entry(wave, e).SpecId;
                _queueCount++;
                _remaining[e]--;
            }
        }

        Wave = wave;
        _waveStarted[index] = true;
        _waveOutstanding[index] = bodies;
        _waveIdCount[index] = 0;

        _events.Publish(new WaveStarted(_stage, wave, _plan.WaveCount));

        // A wave the budget could not afford a single body of is cleared the moment it opens.
        // Legal rather than exceptional: only the *stage* is guaranteed a body (WaveComposer rule
        // 8), and a late wave whose allowance cannot reach the cheapest archetype is empty. Said
        // here rather than left to the sweep, which would take a tick to notice and would report
        // it in the wrong order.
        if (bodies == 0)
        {
            _waveCleared[index] = true;

            _events.Publish(new WaveCleared(_stage, wave));
        }
    }

    /// <summary>
    /// How many of a wave's <paramref name="bodies"/> may still be standing when the next one
    /// starts. Rounded up, so a wave of three overlaps at one.
    /// </summary>
    private static int OverlapThreshold(int bodies) =>
        (int)Math.Ceiling(bodies * (double)OverlapFraction);

    /// <summary>Whether <paramref name="candidate"/> is far enough from every point still held.</summary>
    /// <remarks>
    /// A point's own claim is caught by this without a special case: the distance from a point to
    /// itself is zero, which is inside any positive separation.
    /// </remarks>
    private bool IsClearOfClaims(Vector3 candidate, float now)
    {
        for (int i = 0; i < _claimExpiry.Length; i++)
        {
            if (_claimExpiry[i] <= now)
            {
                continue;
            }

            if (DistanceSquaredXZ(candidate, _spawnPoints[i]) < MinSeparationSquared)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Grows the working arrays to what <paramref name="plan"/> can ask for.</summary>
    /// <remarks>
    /// <para>
    /// Grown rather than reallocated, so the second stage of a run allocates nothing at all: a run
    /// composes into one plan with one capacity, so the first <see cref="Begin"/> is the only one
    /// that can buy anything.
    /// </para>
    /// <para>
    /// A wave can never hold more bodies than the stage's concurrency — <see cref="WaveComposer"/>
    /// stops buying at the cap — and the pending telegraphs cannot outnumber it either, since rule
    /// 8 counts them against it. So one number sizes nearly everything here, and the id table is
    /// the product of it and the wave count.
    /// </para>
    /// </remarks>
    private void EnsureCapacity(WavePlan plan)
    {
        int waves = plan.WaveCount;
        int stride = plan.Concurrency;
        int entries = 0;

        for (int wave = 1; wave <= waves; wave++)
        {
            int count = plan.EntryCount(wave);

            if (count > entries)
            {
                entries = count;
            }
        }

        if (_queue.Length < stride)
        {
            _queue = new ContentId[stride];
            _pendingSpec = new ContentId[stride];
            _pendingPosition = new Vector3[stride];
            _pendingFiresAt = new float[stride];
            _pendingWave = new int[stride];
        }

        if (_remaining.Length < entries)
        {
            _remaining = new int[entries];
        }

        if (_waveIdCount.Length < waves)
        {
            _waveIdCount = new int[waves];
            _waveOutstanding = new int[waves];
            _waveStarted = new bool[waves];
            _waveCleared = new bool[waves];
        }

        // The stride only ever grows, so a later, narrower stage keeps the wider table rather than
        // re-indexing one it already owns — which is why the length is checked against the stride
        // in use and not against the one this plan asked for.
        if (_idStride < stride)
        {
            _idStride = stride;
        }

        if (_waveIds.Length < waves * _idStride)
        {
            _waveIds = new int[waves * _idStride];
        }
    }

    /// <summary>Lets go of every spawn point, whatever its claim had left.</summary>
    private void ReleaseClaims()
    {
        for (int i = 0; i < _claimExpiry.Length; i++)
        {
            _claimExpiry[i] = float.NegativeInfinity;
        }
    }

    /// <summary>The squared XZ separation of two points — AR §18.4, and no square root.</summary>
    private static float DistanceSquaredXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;

        return (dx * dx) + (dz * dz);
    }

    private static bool IsFinite(Vector3 v) =>
        !float.IsNaN(v.X) && !float.IsInfinity(v.X)
        && !float.IsNaN(v.Y) && !float.IsInfinity(v.Y)
        && !float.IsNaN(v.Z) && !float.IsInfinity(v.Z);

    /// <summary>
    /// Refuses a non-finite time. Spelled as a positive test rather than as a negated comparison,
    /// because every comparison against NaN is false and the natural spelling lets one through
    /// (AR §18.3).
    /// </summary>
    private static void RequireFinite(float value, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"{name} must be finite. A NaN clock does not produce a wrong answer, it produces "
                    + "silence: every comparison against it is false, so nothing is ever due.");
        }
    }
}
