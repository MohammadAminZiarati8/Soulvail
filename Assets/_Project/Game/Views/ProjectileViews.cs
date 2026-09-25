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
/// prewarmed, and a class's is built on its first shot.
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
    private readonly IObjectResolver _resolver;
    private readonly Transform _parent;

    /// <summary>Class id → the prefab its shots fly, or null: every shot flies the default.</summary>
    private readonly CharacterLookBook _looks;

    /// <summary>The default bolt's pool: every enemy's shot, and every class that names none.</summary>
    private readonly ViewPool<ProjectileView> _defaultPool;

    /// <summary>
    /// Prefab → its pool, the default's included, so two classes naming one prefab share a pool
    /// and a class naming the default bolt flies the prewarmed bodies (rule 2).
    /// </summary>
    private readonly Dictionary<ProjectileView, ViewPool<ProjectileView>> _pools;

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
    /// <exception cref="ArgumentNullException">
    /// <paramref name="resolver"/>, <paramref name="prefab"/> or <paramref name="hub"/> is null.
    /// <paramref name="parent"/> and <paramref name="looks"/> may be null; the others are the run
    /// being mis-wired.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="prewarm"/> is negative.</exception>
    public ProjectileViews(
        IObjectResolver resolver,
        ProjectileView prefab,
        Transform parent,
        DomainEventHub hub,
        int prewarm = 0,
        CharacterLookBook looks = null)
    {
        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        // The pool makes the resolver, prefab and prewarm checks — a destroyed prefab is a live
        // reference that only compares equal to null through Unity's operator, and a destroyed
        // parent is normalised to the scene root.
        _defaultPool = new ViewPool<ProjectileView>(resolver, prefab, parent, prewarm);

        _resolver = resolver;
        _parent = parent;
        _looks = looks;

        _pools = new Dictionary<ProjectileView, ViewPool<ProjectileView>> { [prefab] = _defaultPool };

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

            foreach (ViewPool<ProjectileView> pool in _pools.Values)
            {
                pooled += pool.CountInactive;
            }

            return pooled;
        }
    }

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

        return _pools.TryGetValue(prefab, out ViewPool<ProjectileView> pool) ? pool.CountInactive : 0;
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
    /// Steps every bolt in service. Called once a frame by <c>RunTicker</c>, with
    /// <c>snapshot.Dt</c>.
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
            rental.Pool.Release(rental.View);
        }

        _byId.Clear();

        foreach (ViewPool<ProjectileView> pool in _pools.Values)
        {
            pool.Dispose();
        }

        _pools.Clear();
    }

    private void OnFired(ProjectileFired evt)
    {
        ViewPool<ProjectileView> pool = PoolFor(evt.SpecId);
        ProjectileView view = pool.Get();

        view.Bind(evt.Id, evt.Origin.ToUnity(), evt.Target.ToUnity(), evt.FlightTime);

        _byId[evt.Id] = new Rental(view, pool);

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

        rental.Pool.Release(rental.View);
    }

    /// <summary>
    /// The pool a shot from <paramref name="specId"/> flies from (rules 1, 2 and 4): its class's
    /// prefab's, built here on that prefab's first shot, or the default's.
    /// </summary>
    /// <remarks>
    /// Two dictionary lookups on a <see cref="ContentId"/> and a prefab, so nothing allocates once
    /// the pool exists. A class's pool is not prewarmed: this object does not know which class is
    /// playing, and a class that names a prefab fires one shot at a time.
    /// </remarks>
    private ViewPool<ProjectileView> PoolFor(ContentId specId)
    {
        if (_looks is null)
        {
            return _defaultPool;
        }

        ProjectileView prefab = _looks.For(specId).Projectile;

        // Unity's ==: an enemy's id and a class that names nothing both read a real null here, and
        // a prefab deleted from the project reads a live reference only the engine calls null.
        if (prefab == null)
        {
            return _defaultPool;
        }

        if (!_pools.TryGetValue(prefab, out ViewPool<ProjectileView> pool))
        {
            pool = new ViewPool<ProjectileView>(_resolver, prefab, _parent, prewarm: 0);

            _pools.Add(prefab, pool);
        }

        return pool;
    }

    /// <summary>A body in the air, and the pool it goes back to (rule 3).</summary>
    private readonly struct Rental
    {
        public Rental(ProjectileView view, ViewPool<ProjectileView> pool)
        {
            View = view;
            Pool = pool;
        }

        public ProjectileView View { get; }

        public ViewPool<ProjectileView> Pool { get; }
    }
}
