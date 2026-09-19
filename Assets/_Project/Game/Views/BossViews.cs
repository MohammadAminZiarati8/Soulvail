using System;
using System.Collections.Generic;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Pooling;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views;

/// <summary>
/// Everything a boss fight puts on screen: the rings its slams send out, the cracks it opens, and
/// the shell it wears while it cannot be hurt. <see cref="ZoneViews"/>' shape three times over, and
/// the one listener for the whole of M4-02's vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// <b>One census for three pools rather than three censuses</b>, and that is a deviation from this
/// task's Files table stated in its <em>As built</em>. The alternative is three more files and three
/// more arguments on <c>RunTicker</c> for objects that are rented by the same fight, retired by the
/// same tick and disposed by the same run; this way the boss's whole presentation has one owner, one
/// step and one place its subscriptions are taken.
/// </para>
/// <para>
/// <b>It is a listener, not a spawner</b>, and all seven subscriptions are taken in the constructor,
/// which is AR §18.1's rule: a <c>Start</c> of its own would be ordered against nothing and would
/// drop the first slam of a fight in silence. Being on <c>RunTicker</c>'s dependency chain is what
/// guarantees the chain is built before core can publish anything — <see cref="ZoneViews"/>'
/// bargain, for its reason.
/// </para>
/// <para>
/// <b>Everything is keyed by id, and for <see cref="ZoneViews"/>' reason.</b> A ring is asked for
/// back by name when it passes, a crack twice — once when it bites and once when it closes — and a
/// shell when the beat ends. The ids are issued from 1 and never reused within a run, so a
/// dictionary is the only index any of this resolves through.
/// </para>
/// <para>
/// <b>Nothing here reads <c>RunState</c>, which is the whole of why M4-02's five events carry what
/// they carry.</b> A ring's origin, speed and ceiling and a crack's place, radius and arm all ride
/// on the event, so a view integrates its own animation and this class holds no clock of core's.
/// The one number that is not core's is <see cref="FissureView"/>'s collapse, which is cosmetic and
/// lives on the prefab.
/// </para>
/// <para>
/// <b>The arena is optional and the boss's own body is not.</b> A run composed without arenas is the
/// undressed Run scene — the fastest iteration loop in the project, which every optional field on
/// <c>RunScope</c> exists to protect — and a hazard is arena content. <see cref="EnemyViews"/> is
/// required because a beat has to hang on something (rule 4), and every run has a census of bodies.
/// </para>
/// <para>
/// Not a <see cref="MonoBehaviour"/>, for <see cref="ZoneViews"/>' reason: it has no frame of its own
/// and nothing in a scene should be able to find it. It is registered in <c>RunScope</c> and disposed
/// with the run, which is what unsubscribes it — a listener that outlived its run would be handed the
/// next run's ids.
/// </para>
/// </remarks>
public sealed class BossViews : IDisposable
{
    private readonly ViewPool<ShockwaveView> _shockwavePool;
    private readonly ViewPool<FissureView> _fissurePool;
    private readonly ViewPool<BossBeatView> _beatPool;

    /// <summary>Core's ring id → the wave standing in for it.</summary>
    private readonly Dictionary<int, ShockwaveView> _shockwaves = new Dictionary<int, ShockwaveView>();

    /// <summary>Core's fissure id → the crack standing in for it.</summary>
    private readonly Dictionary<int, FissureView> _fissures = new Dictionary<int, FissureView>();

    /// <summary>The boss's enemy id → the shell it is wearing.</summary>
    private readonly Dictionary<int, BossBeatView> _beats = new Dictionary<int, BossBeatView>();

    /// <summary>
    /// Scratch for the ids a <see cref="Step"/> found to have run out. Three lists for the life of
    /// the run, cleared and refilled, because a dictionary cannot be written to while it is being
    /// walked and a per-frame list would allocate on every frame of every boss fight (AR §14).
    /// </summary>
    private readonly List<int> _orphans = new List<int>();

    /// <summary>
    /// Where each live ring's front edge was at the end of the previous step, by ring id. What makes
    /// the hazard test a <em>crossing</em> rather than a containment — see <see cref="Step"/>.
    /// </summary>
    private readonly Dictionary<int, float> _lastRadius = new Dictionary<int, float>();

    private readonly EnemyViews _enemies;
    private readonly ArenaPool _arenas;

    private readonly IDisposable _emittedSubscription;
    private readonly IDisposable _passedSubscription;
    private readonly IDisposable _armedSubscription;
    private readonly IDisposable _firedSubscription;
    private readonly IDisposable _closedSubscription;
    private readonly IDisposable _beatStartedSubscription;
    private readonly IDisposable _beatEndedSubscription;

    private bool _disposed;

    /// <param name="resolver">
    /// The run's container, handed to the three pools so bodies are instantiated through it — the
    /// rule <see cref="ZoneViews"/> keeps. Nothing on the three prefabs takes an <c>[Inject]</c>
    /// today; going through the container anyway is what stops the first component that does from
    /// being silently deaf.
    /// </param>
    /// <param name="shockwavePrefab">The one ring prefab. Every slam is this body expanding.</param>
    /// <param name="fissurePrefab">The one crack prefab. Every fissure is this body at its own radius.</param>
    /// <param name="beatPrefab">The one shell prefab. Every beat is this body on whichever boss.</param>
    /// <param name="parent">
    /// Where instances are parented, or null for the scene root. The scene's decal root rather than
    /// the arena's, for <c>TelegraphRings</c>' reason: an arena is torn down and raised again at
    /// every stage boundary, and a body parented to one would be destroyed mid-life by a swap it has
    /// nothing to do with. A shell leaves this root while it is worn and comes back on the way to the
    /// pool (<see cref="BossBeatView.OnDespawn"/>).
    /// </param>
    /// <param name="hub">The run's event hub, subscribed to for the length of this object's life.</param>
    /// <param name="enemies">
    /// The arena's population, so a beat can find the body it belongs on. Required — rule 4 is that
    /// the shell is drawn on the agent, and a census that could not resolve one would draw it
    /// nowhere.
    /// </param>
    /// <param name="arenas">
    /// The arenas, so a ring can knock over what is standing in the room (rule 6). <b>May be
    /// null</b>, and an arena with no hazards is the same thing: see the class remarks.
    /// </param>
    /// <param name="prewarm">
    /// How many bodies of each kind to build before the run starts. Core's own capacities, so a pool
    /// cannot be asked for a body it does not already hold and the only <c>Instantiate</c> calls of a
    /// whole run happen while the scene is still loading rather than on the frame a boss slams
    /// (AR §14, GD §11.3).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="resolver"/>, any of the three prefabs, <paramref name="hub"/> or
    /// <paramref name="enemies"/> is null. <paramref name="parent"/> and <paramref name="arenas"/>
    /// may be; the others are the run being mis-wired.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A prewarm is negative.</exception>
    public BossViews(
        IObjectResolver resolver,
        ShockwaveView shockwavePrefab,
        FissureView fissurePrefab,
        BossBeatView beatPrefab,
        Transform parent,
        DomainEventHub hub,
        EnemyViews enemies,
        ArenaPool arenas,
        int shockwavePrewarm = 4,
        int fissurePrewarm = 8,
        int beatPrewarm = 1)
    {
        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));

        // Optional, and normalised here rather than checked at every use: an undressed Run scene has
        // no arena standing and a hazard is arena content.
        _arenas = arenas;

        // The pools make the resolver, prefab and prewarm checks — a destroyed prefab is a live
        // reference that only compares equal to null through Unity's operator, and a destroyed
        // parent is normalised to the scene root.
        _shockwavePool = new ViewPool<ShockwaveView>(resolver, shockwavePrefab, parent, shockwavePrewarm);
        _fissurePool = new ViewPool<FissureView>(resolver, fissurePrefab, parent, fissurePrewarm);
        _beatPool = new ViewPool<BossBeatView>(resolver, beatPrefab, parent, beatPrewarm);

        _emittedSubscription = hub.Subscribe<ShockwaveEmitted>(OnShockwaveEmitted);
        _passedSubscription = hub.Subscribe<ShockwavePassed>(OnShockwavePassed);
        _armedSubscription = hub.Subscribe<FissureArmed>(OnFissureArmed);
        _firedSubscription = hub.Subscribe<FissureFired>(OnFissureFired);
        _closedSubscription = hub.Subscribe<FissureClosed>(OnFissureClosed);
        _beatStartedSubscription = hub.Subscribe<BossBeatStarted>(OnBeatStarted);
        _beatEndedSubscription = hub.Subscribe<BossBeatEnded>(OnBeatEnded);
    }

    /// <summary>How many rings are travelling across the floor right now.</summary>
    public int ShockwaveCount => _shockwaves.Count;

    /// <summary>How many cracks are on the floor right now, arming or collapsing.</summary>
    public int FissureCount => _fissures.Count;

    /// <summary>How many bosses are wearing a shell right now.</summary>
    public int BeatCount => _beats.Count;

    /// <summary>How many ring bodies are sitting in the pool, waiting to be rented.</summary>
    /// <remarks>
    /// For the reader <c>ProjectileViews.PooledCount</c> exists for: rented plus pooled is the number
    /// of bodies that exist, and it must stop growing once a fight is under way.
    /// </remarks>
    public int PooledShockwaves => _shockwavePool.CountInactive;

    /// <inheritdoc cref="PooledShockwaves" />
    public int PooledFissures => _fissurePool.CountInactive;

    /// <inheritdoc cref="PooledShockwaves" />
    public int PooledBeats => _beatPool.CountInactive;

    /// <summary>The wave standing in for ring <paramref name="id"/>, if there is one.</summary>
    public bool TryGetShockwave(int id, out ShockwaveView view) => _shockwaves.TryGetValue(id, out view);

    /// <summary>The crack standing in for fissure <paramref name="id"/>, if there is one.</summary>
    public bool TryGetFissure(int id, out FissureView view) => _fissures.TryGetValue(id, out view);

    /// <summary>The shell boss <paramref name="enemyId"/> is wearing, if it is wearing one.</summary>
    public bool TryGetBeat(int enemyId, out BossBeatView view) => _beats.TryGetValue(enemyId, out view);

    /// <summary>
    /// Steps everything in service and returns whatever ran out. Called once a frame by
    /// <c>RunTicker</c>, with <c>snapshot.Dt</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two passes per kind rather than one, because a dictionary cannot be written to while it is
    /// being walked — and the concrete dictionary's enumerators are structs, so no pass allocates.
    /// Nothing a view's <c>Step</c> does can publish, so no census can be added to while it is being
    /// walked either. <see cref="ZoneViews.Step"/>'s shape, for its reasons.
    /// </para>
    /// <para>
    /// <b>The hazard test is a crossing, not a containment</b> (rule 6). A ring is compared against
    /// where its own front edge was at the end of the last step, so a hazard is knocked over on the
    /// one frame the wave reaches it rather than on every frame after — which is the same distinction
    /// <c>ShockwaveSystem</c> makes about the edge that bites, and the reason it keeps a previous
    /// radius of its own.
    /// </para>
    /// <para>
    /// The retirements here are the net described on each view: in a run where every event arrives,
    /// <c>ShockwavePassed</c>, <c>FissureClosed</c> and <c>BossBeatEnded</c> have already taken the
    /// body and these passes find nothing.
    /// </para>
    /// </remarks>
    public void Step(float dt)
    {
        StepShockwaves(dt);
        StepFissures(dt);
        StepBeats(dt);
    }

    /// <summary>
    /// Stops listening and destroys every body — on the floor, on a boss, or pooled.
    /// </summary>
    /// <remarks>
    /// The bodies still in service are not returned first, which is <c>EnemyViews</c>' bargain for
    /// its reason: each pool owns every instance it made and destroys the lot, so there is exactly
    /// one owner of the whole set and a body cannot outlive the run. A shell hanging on an enemy is
    /// the one that would otherwise be missed, and it is not: the pool holds it whatever it is
    /// parented to.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _emittedSubscription.Dispose();
        _passedSubscription.Dispose();
        _armedSubscription.Dispose();
        _firedSubscription.Dispose();
        _closedSubscription.Dispose();
        _beatStartedSubscription.Dispose();
        _beatEndedSubscription.Dispose();

        _shockwavePool.Dispose();
        _fissurePool.Dispose();
        _beatPool.Dispose();

        _shockwaves.Clear();
        _fissures.Clear();
        _beats.Clear();
        _lastRadius.Clear();
        _orphans.Clear();
    }

    /// <summary>
    /// A slam has landed: a ring leaving the point the body stood on, at the event's own speed.
    /// </summary>
    /// <remarks>
    /// All three numbers come off the event rather than out of <c>WardenBehaviour</c>, which is why
    /// <c>ShockwaveEmitted</c> carries them: this is exactly the circle <c>ShockwaveSystem</c> is
    /// testing the player against, so walking outside the drawn edge is the same act as walking
    /// outside the one that hurts.
    /// <para>
    /// A second emission on a live id is not something core can do — ids are issued from 1 and never
    /// reused within a run — so the existing wave is retired first rather than leaked. The cost of
    /// being wrong about that is one orphaned body per run; the cost of not handling it is a body
    /// the pool can never take back. <see cref="ZoneViews.OnSpawned"/>'s ruling.
    /// </para>
    /// </remarks>
    private void OnShockwaveEmitted(ShockwaveEmitted evt)
    {
        if (_shockwaves.ContainsKey(evt.Id))
        {
            RetireShockwave(evt.Id);
        }

        ShockwaveView view = _shockwavePool.Get();

        view.Bind(evt.Origin.ToUnity(), evt.Speed, evt.MaxRadius);

        _shockwaves[evt.Id] = view;
        _lastRadius[evt.Id] = 0f;

#if UNITY_EDITOR
        // Four objects called "VFX_Shockwave" in the hierarchy are unreadable the moment one of them
        // misbehaves. Editor-only because it allocates a string per ring, which a phone should not
        // pay for — TelegraphRings' rule, for its reason.
        view.name = $"Shockwave {evt.Id}";
#endif
    }

    /// <summary>The ring reached its ceiling, and core is the authority on that.</summary>
    private void OnShockwavePassed(ShockwavePassed evt)
    {
        RetireShockwave(evt.Id);
    }

    /// <summary>
    /// A crack has opened and is counting down to the bite.
    /// </summary>
    /// <remarks>
    /// The radius and the arm come off the event for <see cref="OnShockwaveEmitted"/>'s reason, and
    /// the arm especially: GD §9.1 rule 1's floor is enforced in core as a clamp, so a Warden
    /// retuned below 0.6 s is still <em>played</em> at 0.6 — and a view drawing the authored number
    /// instead of the clamped one would be a telegraph that ended before the thing it promised.
    /// </remarks>
    private void OnFissureArmed(FissureArmed evt)
    {
        if (_fissures.ContainsKey(evt.Id))
        {
            RetireFissure(evt.Id);
        }

        FissureView view = _fissurePool.Get();

        view.Arm(evt.At.ToUnity(), evt.Radius, evt.ArmSeconds);

        _fissures[evt.Id] = view;

#if UNITY_EDITOR
        view.name = $"Fissure {evt.Id}";
#endif
    }

    /// <summary>
    /// The arm is over. The crack snaps open whether or not it caught anybody, which is
    /// <c>FissureFired</c>'s own reasoning: one that only flashed on a hit would teach the player
    /// that the ones they dodged never fired.
    /// </summary>
    private void OnFissureFired(FissureFired evt)
    {
        if (_fissures.TryGetValue(evt.Id, out FissureView view) && view != null)
        {
            view.Fire();
        }
    }

    /// <summary>The crack's window is shut, and core is the authority on that.</summary>
    private void OnFissureClosed(FissureClosed evt)
    {
        RetireFissure(evt.Id);
    }

    /// <summary>
    /// A boss has become untouchable: a shell on its body for the length of the beat (rule 4).
    /// </summary>
    /// <remarks>
    /// <b>A beat on a boss with no body is dropped rather than drawn somewhere else.</b> That is not
    /// a state a run reaches — <c>EnemySpawned</c> builds the body before any behaviour can publish —
    /// but the alternative to dropping it is a shell at the world origin saying something there
    /// cannot be hurt, and a feedback view must never claim a thing that is not on screen.
    /// </remarks>
    private void OnBeatStarted(BossBeatStarted evt)
    {
        if (_beats.ContainsKey(evt.EnemyId))
        {
            RetireBeat(evt.EnemyId);
        }

        if (!_enemies.TryGet(evt.EnemyId, out EnemyView body) || body == null)
        {
            return;
        }

        BossBeatView view = _beatPool.Get();

        view.Bind(body.transform, evt.Seconds);

        _beats[evt.EnemyId] = view;

#if UNITY_EDITOR
        view.name = $"Beat {evt.EnemyId}";
#endif
    }

    /// <summary>The boss can be hurt again, and core is the authority on that.</summary>
    private void OnBeatEnded(BossBeatEnded evt)
    {
        RetireBeat(evt.EnemyId);
    }

    /// <inheritdoc cref="Step" />
    private void StepShockwaves(float dt)
    {
        _orphans.Clear();

        foreach (KeyValuePair<int, ShockwaveView> entry in _shockwaves)
        {
            ShockwaveView view = entry.Value;

            // Unity's ==: a destroyed body is a live reference that only compares equal to null
            // through the engine's operator, and a pool is entitled to have been disposed out from
            // under a frame that is still finishing.
            if (view == null || !view.Step(dt))
            {
                _orphans.Add(entry.Key);

                continue;
            }

            SweepHazards(entry.Key, view);
        }

        for (int i = 0; i < _orphans.Count; i++)
        {
            RetireShockwave(_orphans[i]);
        }
    }

    /// <inheritdoc cref="Step" />
    private void StepFissures(float dt)
    {
        _orphans.Clear();

        foreach (KeyValuePair<int, FissureView> entry in _fissures)
        {
            FissureView view = entry.Value;

            if (view == null || !view.Step(dt))
            {
                _orphans.Add(entry.Key);
            }
        }

        for (int i = 0; i < _orphans.Count; i++)
        {
            RetireFissure(_orphans[i]);
        }
    }

    /// <inheritdoc cref="Step" />
    private void StepBeats(float dt)
    {
        _orphans.Clear();

        foreach (KeyValuePair<int, BossBeatView> entry in _beats)
        {
            BossBeatView view = entry.Value;

            if (view == null || !view.Step(dt))
            {
                _orphans.Add(entry.Key);
            }
        }

        for (int i = 0; i < _orphans.Count; i++)
        {
            RetireBeat(_orphans[i]);
        }
    }

    /// <summary>
    /// Knocks over whatever this ring's front edge reached since the last step (rule 6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured on XZ, like every distance in the game, and against the ring's <em>previous</em>
    /// radius as well as its current one — see <see cref="Step"/>. A hazard already on its side is
    /// refused by <see cref="ArenaView.Topple"/> rather than tested for here, so a second ring over
    /// the same wreckage costs one comparison and changes nothing.
    /// </para>
    /// <para>
    /// Allocates nothing and costs four comparisons a frame in the build that exists: one arena
    /// authors one hazard and a fight can have four rings up at once.
    /// </para>
    /// </remarks>
    private void SweepHazards(int id, ShockwaveView view)
    {
        float previous = _lastRadius.TryGetValue(id, out float last) ? last : 0f;
        float current = view.Radius;

        _lastRadius[id] = current;

        // Unity's ==: a parked or destroyed arena is a live reference that only compares equal to
        // null through the engine's operator.
        ArenaView arena = _arenas?.Active;

        if (arena == null || arena.HazardCount == 0)
        {
            return;
        }

        Vector3 origin = view.transform.position;

        for (int i = 0; i < arena.HazardCount; i++)
        {
            Vector3 at = arena.HazardPosition(i);

            float dx = at.x - origin.x;
            float dz = at.z - origin.z;
            float distance = Mathf.Sqrt((dx * dx) + (dz * dz));

            if (distance > previous && distance <= current)
            {
                arena.Topple(i);
            }
        }
    }

    /// <summary>Takes the wave standing for <paramref name="id"/> off the floor.</summary>
    /// <remarks>
    /// An id with no body is ignored rather than refused: <see cref="Step"/>'s net and
    /// <see cref="OnShockwavePassed"/> can both reach for the same ring on the same frame, and the
    /// second one is not a bug. <see cref="ZoneViews"/>' ruling, three times over.
    /// </remarks>
    private void RetireShockwave(int id)
    {
        _lastRadius.Remove(id);

        if (!_shockwaves.TryGetValue(id, out ShockwaveView view))
        {
            return;
        }

        _shockwaves.Remove(id);

        // Release tolerates a destroyed body and drops it rather than pooling it, so no check here.
        _shockwavePool.Release(view);
    }

    /// <inheritdoc cref="RetireShockwave" />
    private void RetireFissure(int id)
    {
        if (!_fissures.TryGetValue(id, out FissureView view))
        {
            return;
        }

        _fissures.Remove(id);

        _fissurePool.Release(view);
    }

    /// <inheritdoc cref="RetireShockwave" />
    private void RetireBeat(int enemyId)
    {
        if (!_beats.TryGetValue(enemyId, out BossBeatView view))
        {
            return;
        }

        _beats.Remove(enemyId);

        _beatPool.Release(view);
    }
}
