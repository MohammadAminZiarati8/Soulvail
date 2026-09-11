using System;
using System.Collections.Generic;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
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
    private readonly ViewPool<ProjectileView> _pool;

    /// <summary>Core's id → the body standing in for it. The only index anything resolves through.</summary>
    private readonly Dictionary<int, ProjectileView> _byId = new Dictionary<int, ProjectileView>();

    private readonly IDisposable _firedSubscription;
    private readonly IDisposable _impactedSubscription;

    private bool _disposed;

    /// <param name="resolver">
    /// The run's container, handed to the pool so bodies are instantiated through it — the same
    /// rule <see cref="EnemyViews"/> keeps. Nothing on <c>Projectile.prefab</c> takes an
    /// <c>[Inject]</c> today; going through the container anyway is what stops the first component
    /// that does from being silently deaf.
    /// </param>
    /// <param name="prefab">
    /// The one bolt prefab. Every archetype shares it — M2-06 rule 9's argument for enemies, one
    /// kind of body along, and a shot per archetype arrives with the art rather than before it.
    /// </param>
    /// <param name="parent">
    /// Where instances are parented, or null for the scene root. Tidiness, and nothing more.
    /// </param>
    /// <param name="hub">The run's event hub, subscribed to for the length of this object's life.</param>
    /// <param name="prewarm">
    /// How many bodies to build before the run starts. Core's projectile capacity, so the only
    /// <c>Instantiate</c> calls of a whole run happen while the scene is still loading rather than
    /// on the frame a Spitter releases (AR §14, GD §11.3).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="resolver"/>, <paramref name="prefab"/> or <paramref name="hub"/> is null.
    /// <paramref name="parent"/> may be null; the others are the run being mis-wired.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="prewarm"/> is negative.</exception>
    public ProjectileViews(
        IObjectResolver resolver,
        ProjectileView prefab,
        Transform parent,
        DomainEventHub hub,
        int prewarm = 0)
    {
        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        // The pool makes the resolver, prefab and prewarm checks — a destroyed prefab is a live
        // reference that only compares equal to null through Unity's operator, and a destroyed
        // parent is normalised to the scene root.
        _pool = new ViewPool<ProjectileView>(resolver, prefab, parent, prewarm);

        _firedSubscription = hub.Subscribe<ProjectileFired>(OnFired);
        _impactedSubscription = hub.Subscribe<ProjectileImpacted>(OnImpacted);
    }

    /// <summary>How many bolts are currently drawn in the arena.</summary>
    public int Count => _byId.Count;

    /// <summary>
    /// How many bodies are sitting in the pool, waiting to be rented.
    /// </summary>
    /// <remarks>
    /// For <c>DebugOverlay</c>, which shows rented against pooled: the sum of the two is the number
    /// that must stop growing once a fight is under way, and a total that keeps climbing is the pool
    /// being bypassed. There is no other way to see that from inside a run.
    /// </remarks>
    public int PooledCount => _pool.CountInactive;

    /// <summary>The body standing in for <paramref name="id"/>, if there is one.</summary>
    public bool TryGet(int id, out ProjectileView view) => _byId.TryGetValue(id, out view);

    /// <summary>
    /// Steps every bolt in service. Called once a frame by <c>RunTicker</c>, with
    /// <c>snapshot.Dt</c>.
    /// </summary>
    /// <remarks>
    /// The concrete dictionary's value enumerator is a struct, so this <c>foreach</c> allocates
    /// nothing; iterating through <c>IEnumerable&lt;ProjectileView&gt;</c> would box it, every
    /// frame. Nothing a <see cref="ProjectileView.Step"/> does can publish, so the census cannot be
    /// modified while it is being walked.
    /// </remarks>
    public void Step(float dt)
    {
        foreach (ProjectileView view in _byId.Values)
        {
            // Unity's ==: a destroyed body is a live reference that only compares equal to null
            // through the engine's operator, and the pool is entitled to have been disposed out
            // from under a frame that is still finishing.
            if (view == null)
            {
                continue;
            }

            view.Step(dt);
        }
    }

    /// <summary>
    /// Stops listening and destroys every body — in flight or pooled.
    /// </summary>
    /// <remarks>
    /// The bodies still in service are not returned first, which is <see cref="EnemyViews"/>'
    /// bargain for its reason: the pool owns every instance it made and destroys the lot, so there
    /// is exactly one owner of the whole set and a body cannot outlive the run.
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

        _pool.Dispose();

        _byId.Clear();
    }

    private void OnFired(ProjectileFired evt)
    {
        ProjectileView view = _pool.Get();

        view.Bind(evt.Id, evt.Origin.ToUnity(), evt.Target.ToUnity(), evt.FlightTime);

        _byId[evt.Id] = view;

#if UNITY_EDITOR
        // Thirty objects called "Projectile" in the hierarchy are unreadable the moment one of them
        // misbehaves. Editor-only because it allocates a string per shot, which a wave of Spitters
        // should not pay for on a phone.
        view.name = $"Projectile {evt.Id} ({evt.SpecId})";
#endif
    }

    private void OnImpacted(ProjectileImpacted evt)
    {
        if (!_byId.TryGetValue(evt.Id, out ProjectileView view))
        {
            // Not an error, for the reason EnemyViews.OnDespawned ignores an unknown despawn: a
            // view may legitimately never have been made for an id — a shot fired before this
            // object existed, or one whose body was destroyed with its pool.
            return;
        }

        _byId.Remove(evt.Id);

        _pool.Release(view);
    }
}
