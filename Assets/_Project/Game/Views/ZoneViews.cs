using System;
using System.Collections.Generic;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views;

/// <summary>
/// Every patch of ground on the floor right now: one decal per zone core says exists, rented by
/// <see cref="ZoneSpawned"/> and returned by <see cref="ZoneExpired"/>.
/// <see cref="TelegraphRings"/>' shape with <see cref="ProjectileViews"/>' index, and the index is
/// the difference that matters.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is keyed by id, and that is not a detail.</b> <see cref="TelegraphRings"/> holds a plain
/// list because a ring outlives nothing and nothing ever asks for it back; a zone is asked for back
/// by name, six seconds after it was placed. The zone events carry an <c>Id</c> issued from 1 rather
/// than an index into core's table (M3-11b, the spec's <c>Index</c> corrected) precisely so this
/// dictionary can exist: a retirement shifts every entry after it, so a census keyed by position
/// would retire the wrong decal the first time two zones overlapped and the older one ended.
/// </para>
/// <para>
/// <b>It is a listener, not a spawner</b>, and all three subscriptions are taken in the constructor,
/// which is AR §18.1's rule: a <c>Start</c> of its own would be ordered against nothing and would
/// drop the first Consecrate of a run in silence. Being on <c>RunTicker</c>'s dependency chain is
/// what guarantees the chain is built before core can publish anything — <see cref="ProjectileViews"/>'
/// bargain, for its reason.
/// </para>
/// <para>
/// <b>A pulse flashes only when something was actually healed.</b> <see cref="ZoneHealed"/> carries
/// what <c>Health.Heal</c> returned, which is zero at full health and zero for the dead (M3-11b
/// rule 8), and a flash on a wasted pulse would tell the player they were being healed when they
/// were not. That is the one thing a feedback view must never do, and it is read here rather than in
/// <see cref="ZoneView"/> because the decal is handed decisions, not events.
/// </para>
/// <para>
/// <b>It owns no frame of its own.</b> <c>RunTicker</c> calls <see cref="Step"/> with the snapshot's
/// clamped <c>Dt</c>, beside the bolts and the rings and for their reason (M2-09 rule 3, M2-12b
/// rule 4): core ran every zone's life on that step, so a decal faded on <c>Time.deltaTime</c> would
/// out-live or under-live the zone on exactly the hitching frames the clamp exists for. Nothing below
/// it in the frame reads it — a decal has no collider — so it joins the frame's purely cosmetic half
/// rather than its physical one.
/// </para>
/// <para>
/// Not a <see cref="MonoBehaviour"/>, for <see cref="ProjectileViews"/>' reason: it has no frame of
/// its own and nothing in a scene should be able to find it. It is registered in <c>RunScope</c> and
/// disposed with the run, which is what unsubscribes it — a listener that outlived its run would be
/// handed the next run's ids.
/// </para>
/// </remarks>
public sealed class ZoneViews : IDisposable
{
    private readonly ViewPool<ZoneView> _pool;

    /// <summary>Core's zone id → the decal standing in for it. The only index anything resolves through.</summary>
    private readonly Dictionary<int, ZoneView> _byId = new Dictionary<int, ZoneView>();

    /// <summary>
    /// Scratch for the ids <see cref="Step"/> found to have run out. One list for the life of the
    /// run, cleared and refilled, because a dictionary cannot be written to while it is being walked
    /// and a per-frame list would allocate on every frame of every run (AR §14).
    /// </summary>
    private readonly List<int> _orphans = new List<int>();

    private readonly IDisposable _spawnedSubscription;
    private readonly IDisposable _healedSubscription;
    private readonly IDisposable _expiredSubscription;

    private bool _disposed;

    /// <param name="resolver">
    /// The run's container, handed to the pool so bodies are instantiated through it — the same rule
    /// <see cref="TelegraphRings"/> keeps. Nothing on <c>VFX_ConsecrateZone.prefab</c> takes an
    /// <c>[Inject]</c> today; going through the container anyway is what stops the first component
    /// that does from being silently deaf.
    /// </param>
    /// <param name="prefab">The one zone prefab. Every zone is this body at its own radius.</param>
    /// <param name="parent">
    /// Where instances are parented, or null for the scene root. Tidiness, and nothing more.
    /// </param>
    /// <param name="hub">The run's event hub, subscribed to for the length of this object's life.</param>
    /// <param name="prewarm">
    /// How many bodies to build before the run starts. Core's zone capacity, so the pool cannot be
    /// asked for a body it does not already hold and the only <c>Instantiate</c> calls of a whole run
    /// happen while the scene is still loading rather than on the frame a skill fires (AR §14,
    /// GD §11.3).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="resolver"/>, <paramref name="prefab"/> or <paramref name="hub"/> is null.
    /// <paramref name="parent"/> may be null; the others are the run being mis-wired.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="prewarm"/> is negative.</exception>
    public ZoneViews(
        IObjectResolver resolver,
        ZoneView prefab,
        Transform parent,
        DomainEventHub hub,
        int prewarm = 8)
    {
        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        // The pool makes the resolver, prefab and prewarm checks — a destroyed prefab is a live
        // reference that only compares equal to null through Unity's operator, and a destroyed
        // parent is normalised to the scene root.
        _pool = new ViewPool<ZoneView>(resolver, prefab, parent, prewarm);

        _spawnedSubscription = hub.Subscribe<ZoneSpawned>(OnSpawned);
        _healedSubscription = hub.Subscribe<ZoneHealed>(OnHealed);
        _expiredSubscription = hub.Subscribe<ZoneExpired>(OnExpired);
    }

    /// <summary>How many decals are currently drawn on the floor.</summary>
    public int Count => _byId.Count;

    /// <summary>How many bodies are sitting in the pool, waiting to be rented.</summary>
    /// <remarks>
    /// For <c>DebugOverlay</c>'s reader, which shows rented against pooled: the sum of the two is the
    /// number of bodies that exist, and it must stop growing once a fight is under way.
    /// <see cref="ProjectileViews.PooledCount"/>'s reason, for the same reader.
    /// </remarks>
    public int PooledCount => _pool.CountInactive;

    /// <summary>The decal standing in for <paramref name="id"/>, if there is one.</summary>
    public bool TryGet(int id, out ZoneView view) => _byId.TryGetValue(id, out view);

    /// <summary>
    /// Steps every decal in service and returns the ones whose own countdown is over. Called once a
    /// frame by <c>RunTicker</c>, with <c>snapshot.Dt</c>.
    /// </summary>
    /// <remarks>
    /// Two passes rather than one, because a dictionary cannot be written to while it is being
    /// walked — and the concrete dictionary's enumerators are structs, so neither pass allocates.
    /// Nothing a <see cref="ZoneView.Step"/> does can publish, so the census cannot be added to while
    /// it is being walked either.
    /// <para>
    /// The retirements here are the net described on <see cref="ZoneView.Step"/>: in a run where
    /// every event arrives, <see cref="OnExpired"/> has already taken the decal and this pass finds
    /// nothing.
    /// </para>
    /// </remarks>
    public void Step(float dt)
    {
        _orphans.Clear();

        foreach (KeyValuePair<int, ZoneView> entry in _byId)
        {
            ZoneView view = entry.Value;

            // Unity's ==: a destroyed body is a live reference that only compares equal to null
            // through the engine's operator, and the pool is entitled to have been disposed out from
            // under a frame that is still finishing.
            if (view == null || !view.Step(dt))
            {
                _orphans.Add(entry.Key);
            }
        }

        for (int i = 0; i < _orphans.Count; i++)
        {
            Retire(_orphans[i]);
        }
    }

    /// <summary>
    /// Stops listening and destroys every body — on the floor or pooled.
    /// </summary>
    /// <remarks>
    /// The decals still in service are not returned first, which is <c>EnemyViews</c>' bargain for
    /// its reason: the pool owns every instance it made and destroys the lot, so there is exactly one
    /// owner of the whole set and a body cannot outlive the run.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _spawnedSubscription.Dispose();
        _healedSubscription.Dispose();
        _expiredSubscription.Dispose();

        _pool.Dispose();

        _byId.Clear();
        _orphans.Clear();
    }

    /// <summary>
    /// Ground has been laid: a decal at the zone's own position, at its own radius, for its own life.
    /// </summary>
    /// <remarks>
    /// Every one of those three numbers comes off the event rather than out of a catalog or off the
    /// prefab, which is why <see cref="ZoneSpawned"/> carries them: this is exactly the circle
    /// <c>ZoneSystem</c> heals inside, so standing outside the drawn disc is the same act as standing
    /// outside the one that heals.
    /// <para>
    /// A second spawn on a live id is not something core can do — ids are issued from 1 and never
    /// reused within a run — so the existing decal is retired first rather than leaked. The cost of
    /// being wrong about that is one orphaned body per run; the cost of not handling it is a body
    /// the pool can never take back.
    /// </para>
    /// </remarks>
    private void OnSpawned(ZoneSpawned evt)
    {
        if (_byId.ContainsKey(evt.Id))
        {
            Retire(evt.Id);
        }

        ZoneView view = _pool.Get();

        view.Place(evt.Position.ToUnity(), evt.Radius, evt.Duration);

        _byId[evt.Id] = view;

#if UNITY_EDITOR
        // Eight objects called "ConsecrateZone" in the hierarchy are unreadable the moment one of
        // them misbehaves. Editor-only because it allocates a string per zone, which a phone should
        // not pay for — TelegraphRings' rule, for its reason.
        view.name = $"Zone {evt.Id}";
#endif
    }

    /// <summary>
    /// A pulse landed. It flashes only if it actually restored something.
    /// </summary>
    /// <remarks>
    /// Zero is the honest answer at full health and for the dead, and it is common rather than rare:
    /// a player who stands in their own Consecrate at full health gets twelve of them. Rule 4 — and
    /// the reason the event carries the amount at all.
    /// </remarks>
    private void OnHealed(ZoneHealed evt)
    {
        if (!(evt.Amount > 0f))
        {
            return;
        }

        if (_byId.TryGetValue(evt.Id, out ZoneView view) && view != null)
        {
            view.Pulse();
        }
    }

    /// <summary>The zone's time is up, and core is the authority on that.</summary>
    private void OnExpired(ZoneExpired evt)
    {
        Retire(evt.Id);
    }

    /// <summary>Takes the decal standing for <paramref name="id"/> off the floor.</summary>
    /// <remarks>
    /// An id with no decal is ignored rather than refused: <see cref="Step"/>'s net and
    /// <see cref="OnExpired"/> can both reach for the same zone on the same frame, and the second one
    /// is not a bug.
    /// </remarks>
    private void Retire(int id)
    {
        if (!_byId.TryGetValue(id, out ZoneView view))
        {
            return;
        }

        _byId.Remove(id);

        // Release tolerates a destroyed body and drops it rather than pooling it, so no check here.
        _pool.Release(view);
    }
}
