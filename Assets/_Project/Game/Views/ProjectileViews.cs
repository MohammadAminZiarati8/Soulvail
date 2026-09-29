using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Pooling;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views;

/// <summary>
/// The bolts in the air, on the Unity side: one body per shot core says exists, rented and returned
/// by the two projectile events. <see cref="EnemyViews"/>' shape, one layer simpler — nothing here
/// reports anything back into the snapshot, because core never lost track of where a shot was.
/// </summary>
/// <remarks>
/// <para>
/// <b>A shot looks like its shooter's class, or like the default bolt</b> (RS-02c). A player's shot
/// carries its class id as <see cref="ProjectileFired.SpecId"/>, so the <see cref="CharacterLookBook"/>
/// can name a prefab for it; an enemy's carries its archetype id, which no class look answers, so it
/// flies the default — as does a class that names nothing. One pool per prefab: the default's is
/// prewarmed to core's projectile capacity, and each class's to <c>classPrewarm</c> while the run is
/// composed (RS-06b; until then a class's was built on its first shot, mid-wave).
/// </para>
/// <para>
/// <b>A shot that names an impact plays it where it hit</b> (RS-06b). A prefab's
/// <see cref="ProjectileView.Impact"/> is pooled beside the prefab's own bodies, prewarmed with them,
/// rented on a <see cref="ProjectileImpacted"/> whose <c>Hit</c> is true, stepped on the same clock
/// as the bolts and returned when it goes out. A miss plays nothing.
/// </para>
/// <para>
/// <b>It is a listener, not a spawner</b>, and the mirror of <see cref="EnemyViews"/> right down to
/// the ordering: <see cref="ProjectileFired"/> arrives after core has recorded the shot, so a body
/// can be rented knowing the id resolves, and <see cref="ProjectileImpacted"/> arrives after the
/// shot has been resolved and dropped, so a body returned here can never be one core still expects
/// to exist. Both subscriptions are taken in the constructor, which is AR §18.1's rule — a
/// <c>Start</c> of its own would be ordered against nothing.
/// </para>
/// <para>
/// <b>It owns no frame of its own.</b> <c>RunTicker</c> calls <see cref="Step"/> with the snapshot's
/// clamped <c>Dt</c>, which is also what puts this object on the dependency chain early enough for
/// the paragraph above: it is held because it is on the frame path, like everything else the ticker
/// holds, rather than being held only so that it exists.
/// </para>
/// <para>
/// Not a <see cref="MonoBehaviour"/>, for <see cref="EnemyViews"/>' reason: it has no frame of its
/// own and nothing in a scene should be able to find it. It is registered in <c>RunScope</c> and
/// disposed with the run, which is what unsubscribes it — a listener that outlived its run would be
/// handed the next run's ids.
/// </para>
/// </remarks>
public sealed class ProjectileViews : IDisposable
{
    /// <summary>
    /// How many impacts the live list holds before it grows: past any hits that can overlap, which
    /// is a volley's worst case landing twice inside one impact's life.
    /// </summary>
    private const int LiveImpactCapacity = 16;

    private readonly IObjectResolver _resolver;
    private readonly Transform _parent;

    /// <summary>The camera an impact faces, or null: every impact is seen from overhead.</summary>
    private readonly Transform _viewer;

    /// <summary>What each class's shot pool and each impact pool is prewarmed to.</summary>
    private readonly int _classPrewarm;

    /// <summary>Class id → the prefab its shots fly, or null: every shot flies the default.</summary>
    private readonly CharacterLookBook _looks;

    /// <summary>The default bolt's pools: every enemy's shot, and every class that names none.</summary>
    private readonly ShotKind _default;

    /// <summary>
    /// Prefab → its pools, the default's included, so two classes naming one prefab share them
    /// and a class naming the default bolt flies the prewarmed bodies (RS-02c rule 2).
    /// </summary>
    private readonly Dictionary<ProjectileView, ShotKind> _kinds;

    /// <summary>Impact prefab → its pool, so two shots naming one impact share it.</summary>
    private readonly Dictionary<ImpactView, ViewPool<ImpactView>> _impactPools =
        new Dictionary<ImpactView, ViewPool<ImpactView>>();

    /// <summary>Every impact still going out, and the pool it goes back to.</summary>
    private readonly List<LiveImpact> _impacts = new List<LiveImpact>(LiveImpactCapacity);

    /// <summary>
    /// Core's id → the body standing in for it and the pool it goes back to. The only index
    /// anything resolves through.
    /// </summary>
    private readonly Dictionary<int, Rental> _byId = new Dictionary<int, Rental>();

    private readonly IDisposable _firedSubscription;
    private readonly IDisposable _impactedSubscription;

    private bool _disposed;

    /// <param name="resolver">
    /// The run's container, handed to every pool so bodies are instantiated through it — the same
    /// rule <see cref="EnemyViews"/> keeps. Nothing on <c>Projectile.prefab</c> takes an
    /// <c>[Inject]</c> today; going through the container anyway is what stops the first component
    /// that does from being silently deaf.
    /// </param>
    /// <param name="prefab">
    /// The default bolt: every enemy's shot, and every class's that names no prefab of its own.
    /// Enemies share it — M2-06 rule 9's argument, one kind of body along; <c>EnemyLookBook</c> is
    /// where a shot per archetype would go.
    /// </param>
    /// <param name="parent">
    /// Where instances are parented, or null for the scene root. Tidiness, and nothing more.
    /// </param>
    /// <param name="hub">The run's event hub, subscribed to for the length of this object's life.</param>
    /// <param name="prewarm">
    /// How many default bodies to build before the run starts. Core's projectile capacity, so the
    /// only <c>Instantiate</c> calls a Spitter causes happen while the scene is still loading rather
    /// than on the frame it releases (AR §14, GD §11.3).
    /// </param>
    /// <param name="looks">
    /// What each class's shots look like, or null for none: every shot flies
    /// <paramref name="prefab"/>, which is what the sandbox and every fixture before RS-02c build
    /// (rule 4). <c>RunScope</c> resolves the boot scope's book by type.
    /// </param>
    /// <param name="classPrewarm">
    /// How many bodies each class's own shot, and each impact any shot names, is prewarmed with
    /// (RS-06b rule 3). <c>RunScope</c> passes two volleys' worth, so the only <c>Instantiate</c>
    /// calls a Ranger causes happen while the scene loads. Zero builds each pool empty.
    /// </param>
    /// <param name="viewer">
    /// The camera an impact faces and is pulled toward, or null: every impact is seen from overhead.
    /// Its rotation is read on each hit, never cached.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="resolver"/>, <paramref name="prefab"/> or <paramref name="hub"/> is null.
    /// <paramref name="parent"/>, <paramref name="looks"/> and <paramref name="viewer"/> may be null;
    /// the others are the run being mis-wired.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="prewarm"/> or <paramref name="classPrewarm"/> is negative.
    /// </exception>
    public ProjectileViews(
        IObjectResolver resolver,
        ProjectileView prefab,
        Transform parent,
        DomainEventHub hub,
        int prewarm = 0,
        CharacterLookBook looks = null,
        int classPrewarm = 0,
        Transform viewer = null)
    {
        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        if (classPrewarm < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(classPrewarm), classPrewarm, "classPrewarm cannot be negative.");
        }

        // The pool makes the resolver, prefab and prewarm checks — a destroyed prefab is a live
        // reference that only compares equal to null through Unity's operator, and a destroyed
        // parent is normalised to the scene root.
        var defaultPool = new ViewPool<ProjectileView>(resolver, prefab, parent, prewarm);

        _resolver = resolver;
        _parent = parent;
        _looks = looks;
        _classPrewarm = classPrewarm;
        _viewer = viewer == null ? null : viewer;

        _default = new ShotKind(defaultPool, ImpactPoolFor(prefab.Impact));
        _kinds = new Dictionary<ProjectileView, ShotKind> { [prefab] = _default };

        // Rule 3: every class's shot is built now, while the scene loads, rather than on the first
        // shot of the first wave. Construction only, so the boxed enumerator is paid once.
        if (looks != null)
        {
            foreach (CharacterLook look in looks.All)
            {
                if (look.Projectile != null)
                {
                    KindFor(look.Projectile);
                }
            }
        }

        _firedSubscription = hub.Subscribe<ProjectileFired>(OnFired);
        _impactedSubscription = hub.Subscribe<ProjectileImpacted>(OnImpacted);
    }

    /// <summary>How many bolts are currently drawn in the arena.</summary>
    public int Count => _byId.Count;

    /// <summary>
    /// How many bodies are sitting in the pools, every prefab's together, waiting to be rented.
    /// </summary>
    /// <remarks>
    /// For <c>DebugOverlay</c>, which shows rented against pooled: the sum of the two is the number
    /// that must stop growing once a fight is under way, and a total that keeps climbing is a pool
    /// being bypassed. There is no other way to see that from inside a run. The dictionary's value
    /// enumerator is a struct, so the sum allocates nothing.
    /// </remarks>
    public int PooledCount
    {
        get
        {
            int pooled = 0;

            foreach (ShotKind kind in _kinds.Values)
            {
                pooled += kind.Shots.CountInactive;
            }

            return pooled;
        }
    }

    /// <summary>How many impacts are going out right now (RS-06b).</summary>
    public int ImpactCount => _impacts.Count;

    /// <summary>The <paramref name="index"/>th impact going out, in no promised order.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not below <see cref="ImpactCount"/>.</exception>
    public ImpactView ImpactAt(int index) => _impacts[index].View;

    /// <summary>
    /// How many bodies of <paramref name="prefab"/> are waiting in its pool: zero for a prefab no
    /// shot has flown yet, since its pool does not exist (rule 2).
    /// </summary>
    /// <remarks>
    /// The one reading that tells the pools apart. A body returned to the wrong one leaves
    /// <see cref="PooledCount"/> exactly where it should be, and is found only when the next Spitter
    /// throws an arrow.
    /// </remarks>
    public int PooledCountOf(ProjectileView prefab)
    {
        // Unity's ==: a destroyed prefab is a live reference that only the engine's operator calls
        // null, and no pool was built for one.
        if (prefab == null)
        {
            return 0;
        }

        return _kinds.TryGetValue(prefab, out ShotKind kind) ? kind.Shots.CountInactive : 0;
    }

    /// <summary>
    /// How many impacts of <paramref name="prefab"/> are waiting in its pool: zero for an impact no
    /// shot names, since its pool does not exist.
    /// </summary>
    public int PooledImpactCountOf(ImpactView prefab)
    {
        // Unity's ==, for PooledCountOf's reason.
        if (prefab == null)
        {
            return 0;
        }

        return _impactPools.TryGetValue(prefab, out ViewPool<ImpactView> pool) ? pool.CountInactive : 0;
    }

    /// <summary>The body standing in for <paramref name="id"/>, if there is one.</summary>
    public bool TryGet(int id, out ProjectileView view)
    {
        if (_byId.TryGetValue(id, out Rental rental))
        {
            view = rental.View;

            return true;
        }

        view = null;

        return false;
    }

    /// <summary>
    /// Steps every bolt in service and every impact going out, and returns each impact that has
    /// gone out. Called once a frame by <c>RunTicker</c>, with <c>snapshot.Dt</c>.
    /// </summary>
    /// <remarks>
    /// The concrete dictionary's value enumerator is a struct, so this <c>foreach</c> allocates
    /// nothing; iterating through an <c>IEnumerable&lt;Rental&gt;</c> would box it, every
    /// frame. Nothing a <see cref="ProjectileView.Step"/> does can publish, so the census cannot be
    /// modified while it is being walked.
    /// </remarks>
    public void Step(float dt)
    {
        foreach (Rental rental in _byId.Values)
        {
            // Unity's ==: a destroyed body is a live reference that only compares equal to null
            // through the engine's operator, and the pool is entitled to have been disposed out
            // from under a frame that is still finishing.
            if (rental.View == null)
            {
                continue;
            }

            rental.View.Step(dt);
        }

        // Backwards, and a finished impact swapped with the last: the one moved into its slot has
        // already been stepped this frame, and nothing shifts along the list (rule 4).
        for (int i = _impacts.Count - 1; i >= 0; i--)
        {
            LiveImpact live = _impacts[i];

            // Unity's ==: a body the scene destroyed is dropped rather than stepped.
            if (live.View != null)
            {
                live.View.Step(dt);

                if (live.View.IsLive)
                {
                    continue;
                }

                live.Pool.Release(live.View);
            }

            int last = _impacts.Count - 1;

            _impacts[i] = _impacts[last];
            _impacts.RemoveAt(last);
        }
    }

    /// <summary>
    /// Stops listening, returns every body in the air to the pool it came from, and destroys every
    /// body — in flight or pooled.
    /// </summary>
    /// <remarks>
    /// <b>Returned first, then destroyed</b> (rule 3), where <see cref="EnemyViews"/> destroys what
    /// is in service as it stands: with a pool per prefab, a body leaves service only through its
    /// own pool's <see cref="ViewPool{T}.Release"/>, so no body is destroyed still bound to a shot.
    /// Each pool still owns every instance it made and destroys the lot, so a body cannot outlive
    /// the run. A body the scene already destroyed is ignored by <c>Release</c>.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _firedSubscription.Dispose();
        _impactedSubscription.Dispose();

        foreach (Rental rental in _byId.Values)
        {
            rental.Kind.Shots.Release(rental.View);
        }

        _byId.Clear();

        foreach (LiveImpact live in _impacts)
        {
            live.Pool.Release(live.View);
        }

        _impacts.Clear();

        foreach (ShotKind kind in _kinds.Values)
        {
            kind.Shots.Dispose();
        }

        _kinds.Clear();

        foreach (ViewPool<ImpactView> pool in _impactPools.Values)
        {
            pool.Dispose();
        }

        _impactPools.Clear();
    }

    private void OnFired(ProjectileFired evt)
    {
        ShotKind kind = KindOf(evt.SpecId);
        ProjectileView view = kind.Shots.Get();

        view.Bind(evt.Id, evt.Origin.ToUnity(), evt.Target.ToUnity(), evt.FlightTime);

        _byId[evt.Id] = new Rental(view, kind);

#if UNITY_EDITOR
        // Thirty objects called "Projectile" in the hierarchy are unreadable the moment one of them
        // misbehaves. Editor-only because it allocates a string per shot, which a wave of Spitters
        // should not pay for on a phone.
        view.name = $"Projectile {evt.Id} ({evt.SpecId})";
#endif
    }

    private void OnImpacted(ProjectileImpacted evt)
    {
        if (!_byId.TryGetValue(evt.Id, out Rental rental))
        {
            // Not an error, for the reason EnemyViews.OnDespawned ignores an unknown despawn: a
            // view may legitimately never have been made for an id — a shot fired before this
            // object existed, or one whose body was destroyed with its pool.
            return;
        }

        _byId.Remove(evt.Id);

        // RS-06b rule 2: a hit plays the shot's impact, a miss plays nothing. The travel is read
        // before the release, which forgets the flight.
        if (evt.Hit && rental.Kind.Impacts != null)
        {
            Vector3 travel = rental.View != null ? rental.View.Travel : Vector3.zero;
            Quaternion viewer = _viewer != null ? _viewer.rotation : ImpactView.Overhead;
            ImpactView impact = rental.Kind.Impacts.Get();

            impact.Play(evt.Position.ToUnity(), travel, viewer, evt.Id);

            _impacts.Add(new LiveImpact(impact, rental.Kind.Impacts));
        }

        rental.Kind.Shots.Release(rental.View);
    }

    /// <summary>
    /// The pools a shot from <paramref name="specId"/> flies from (RS-02c rules 1, 2 and 4): its
    /// class's prefab's, or the default's.
    /// </summary>
    /// <remarks>
    /// Two dictionary lookups on a <see cref="ContentId"/> and a prefab, so nothing allocates once
    /// the pools exist — and a class's exist from construction (RS-06b rule 3).
    /// </remarks>
    private ShotKind KindOf(ContentId specId)
    {
        if (_looks is null)
        {
            return _default;
        }

        ProjectileView prefab = _looks.For(specId).Projectile;

        // Unity's ==: an enemy's id and a class that names nothing both read a real null here, and
        // a prefab deleted from the project reads a live reference only the engine calls null.
        return prefab == null ? _default : KindFor(prefab);
    }

    /// <summary>
    /// <paramref name="prefab"/>'s pools, built on first asking — at construction for every class's
    /// shot — and prewarmed to <see cref="_classPrewarm"/>.
    /// </summary>
    private ShotKind KindFor(ProjectileView prefab)
    {
        if (!_kinds.TryGetValue(prefab, out ShotKind kind))
        {
            kind = new ShotKind(
                new ViewPool<ProjectileView>(_resolver, prefab, _parent, _classPrewarm),
                ImpactPoolFor(prefab.Impact));

            _kinds.Add(prefab, kind);
        }

        return kind;
    }

    /// <summary>The pool of <paramref name="impact"/>, shared by every shot that names it, or null for none.</summary>
    private ViewPool<ImpactView> ImpactPoolFor(ImpactView impact)
    {
        // Unity's ==, for KindOf's reason.
        if (impact == null)
        {
            return null;
        }

        if (!_impactPools.TryGetValue(impact, out ViewPool<ImpactView> pool))
        {
            pool = new ViewPool<ImpactView>(_resolver, impact, _parent, _classPrewarm);

            _impactPools.Add(impact, pool);
        }

        return pool;
    }

    /// <summary>A shot prefab's bodies, and the impact it names, or null.</summary>
    private sealed class ShotKind
    {
        public ShotKind(ViewPool<ProjectileView> shots, ViewPool<ImpactView> impacts)
        {
            Shots = shots;
            Impacts = impacts;
        }

        public ViewPool<ProjectileView> Shots { get; }

        public ViewPool<ImpactView> Impacts { get; }
    }

    /// <summary>A body in the air, and the pools it came from (RS-02c rule 3).</summary>
    private readonly struct Rental
    {
        public Rental(ProjectileView view, ShotKind kind)
        {
            View = view;
            Kind = kind;
        }

        public ProjectileView View { get; }

        public ShotKind Kind { get; }
    }

    /// <summary>An impact going out, and the pool it goes back to.</summary>
    private readonly struct LiveImpact
    {
        public LiveImpact(ImpactView view, ViewPool<ImpactView> pool)
        {
            View = view;
            Pool = pool;
        }

        public ImpactView View { get; }

        public ViewPool<ImpactView> Pool { get; }
    }
}
