using System;
using System.Collections.Generic;
using Soulvail.Core.Director;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views;

/// <summary>
/// Every ring on the ground right now. <see cref="ProjectileViews"/>' shape, one layer simpler
/// again: nothing here is bound to an id, because a ring outlives nothing and nothing ever asks for
/// it back.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a listener, not a spawner</b>, and both subscriptions are taken in the constructor,
/// which is AR §18.1's rule: a <c>Start</c> of its own would be ordered against nothing and would
/// drop the opening wave's rings in silence. Being on <c>RunTicker</c>'s dependency chain is what
/// guarantees the chain is built before core can publish anything —
/// <see cref="ProjectileViews"/>' bargain, for its reason.
/// </para>
/// <para>
/// <b>There is no index and no un-rent path.</b> M2-05 rule 7 says a telegraph is a promise the
/// director cannot withdraw, so a ring's whole life is decided at the moment it is rented and it
/// returns itself by running out. That is why this class has no dictionary where
/// <see cref="ProjectileViews"/> has one, and no method where <c>EnemyViews</c> has a despawn: two
/// absences that are the design rule rather than an omission.
/// </para>
/// <para>
/// <b>Both rings are the same colour, and that is the point.</b> GD §16.4 reserves saturated
/// red-orange for danger and <em>"nothing else, ever"</em>; a spawn and a blast are both danger, so
/// both get it. It is also why cyan was available to M2-12a for the held focus — one palette, two
/// sides — and the colour is read from <see cref="ThreatArrows"/> rather than copied here so the
/// number exists once.
/// </para>
/// <para>
/// Not a <see cref="MonoBehaviour"/>, for <see cref="ProjectileViews"/>' reason: it has no frame of
/// its own and nothing in a scene should be able to find it. It is registered in <c>RunScope</c> and
/// disposed with the run, which is what unsubscribes it.
/// </para>
/// </remarks>
public sealed class TelegraphRings : IDisposable
{
    /// <summary>
    /// How long a blast ring stays up after the blast, in seconds. Cosmetic, and the only lifetime
    /// here core does not supply.
    /// </summary>
    /// <remarks>
    /// The spawn ring's duration is <c>SpawnDirector.TelegraphTime</c> and the blast's radius is the
    /// archetype's <c>ExplosionSpec.Radius</c>, both authored and both core's. This is how long the
    /// after-image lingers, which nothing in the simulation has an opinion about — named rather than
    /// inlined so that nobody later reads it as a game-feel constant with a source.
    /// </remarks>
    public const float BlastLingerTime = 0.35f;

    /// <summary>
    /// A spawn ring's radius, in metres: half the clearance the director keeps between two
    /// simultaneous spawns.
    /// </summary>
    /// <remarks>
    /// Rule 8 asks for exactly the circle core tested, and this is the one there is.
    /// <c>SpawnDirector.MinSpawnSeparation</c>'s own note says the thing it prevents is <em>"two
    /// rings on one patch of floor"</em> — so half of it is precisely the disc the director reserved
    /// for this body and nobody else, and two spawn rings can therefore never overlap. Derived rather
    /// than written as 1, so it cannot drift from the separation it is half of.
    /// </remarks>
    public const float SpawnRingRadius = SpawnDirector.MinSpawnSeparation / 2f;

    private readonly ViewPool<TelegraphRingView> _pool;

    /// <summary>
    /// The rings in service. A list rather than a dictionary because nothing is ever looked up:
    /// walked once a frame by index and nowhere else.
    /// </summary>
    private readonly List<TelegraphRingView> _live;

    private readonly IDisposable _telegraphedSubscription;
    private readonly IDisposable _explodedSubscription;

    private bool _disposed;

    /// <param name="resolver">
    /// The run's container, handed to the pool so bodies are instantiated through it — the same rule
    /// <see cref="ProjectileViews"/> keeps. Nothing on <c>VFX_TelegraphRing.prefab</c> takes an
    /// <c>[Inject]</c> today; going through the container anyway is what stops the first component
    /// that does from being silently deaf.
    /// </param>
    /// <param name="prefab">The one ring prefab. Both kinds of ring are the same body.</param>
    /// <param name="parent">
    /// Where instances are parented, or null for the scene root. Tidiness, and nothing more.
    /// </param>
    /// <param name="hub">The run's event hub, subscribed to for the length of this object's life.</param>
    /// <param name="prewarm">
    /// How many bodies to build before the run starts, so the only <c>Instantiate</c> calls of a
    /// whole run happen while the scene is still loading rather than on the frame a wave is announced
    /// (AR §14, GD §11.3).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="resolver"/>, <paramref name="prefab"/> or <paramref name="hub"/> is null.
    /// <paramref name="parent"/> may be null; the others are the run being mis-wired.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="prewarm"/> is negative.</exception>
    public TelegraphRings(
        IObjectResolver resolver,
        TelegraphRingView prefab,
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
        _pool = new ViewPool<TelegraphRingView>(resolver, prefab, parent, prewarm);

        _live = new List<TelegraphRingView>(prewarm);

        _telegraphedSubscription = hub.Subscribe<SpawnTelegraphed>(OnTelegraphed);
        _explodedSubscription = hub.Subscribe<EnemyExploded>(OnExploded);
    }

    /// <summary>How many rings are currently drawn on the floor.</summary>
    public int Count => _live.Count;

    /// <summary>How many bodies are sitting in the pool, waiting to be rented.</summary>
    /// <remarks>
    /// For <c>DebugOverlay</c>, which shows rented against pooled: the sum of the two is the number
    /// of bodies that exist, and it must stop growing once a fight is under way. A total that keeps
    /// climbing is the pool being bypassed, and there is no other way to see that from inside a run —
    /// <see cref="ProjectileViews.PooledCount"/>'s reason, for the same reader.
    /// </remarks>
    public int PooledCount => _pool.CountInactive;

    /// <summary>
    /// Steps every live ring and returns the ones whose time is up. Called once a frame by
    /// <c>RunTicker</c>, with <c>snapshot.Dt</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Walked backwards, because a ring that runs out is removed from the list being walked — and
    /// walked by index rather than with a <c>foreach</c>, so a wave's worth of rings costs no
    /// enumerator on the frames that already have the most happening in them.
    /// </para>
    /// <para>
    /// Nothing a <see cref="TelegraphRingView.Step"/> does can publish, so the census cannot be added
    /// to while it is being walked. That is what makes the backwards walk sufficient rather than
    /// merely convenient.
    /// </para>
    /// </remarks>
    public void Step(float dt)
    {
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            TelegraphRingView ring = _live[i];

            // Unity's ==: a destroyed body is a live reference that only compares equal to null
            // through the engine's operator, and the pool is entitled to have been disposed out from
            // under a frame that is still finishing.
            if (ring == null)
            {
                _live.RemoveAt(i);

                continue;
            }

            ring.Step(dt);

            if (ring.IsLive)
            {
                continue;
            }

            _live.RemoveAt(i);

            _pool.Release(ring);
        }
    }

    /// <summary>
    /// Stops listening and destroys every body — on the floor or pooled.
    /// </summary>
    /// <remarks>
    /// The rings still in service are not returned first, which is <c>EnemyViews</c>' bargain for its
    /// reason: the pool owns every instance it made and destroys the lot, so there is exactly one
    /// owner of the whole set and a body cannot outlive the run.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _telegraphedSubscription.Dispose();
        _explodedSubscription.Dispose();

        _pool.Dispose();

        _live.Clear();
    }

    /// <summary>
    /// Something is about to exist here: a ring that fills as the 0.8 s runs out.
    /// </summary>
    /// <remarks>
    /// The duration is <c>SpawnDirector.TelegraphTime</c> rather than
    /// <c>SpawnTelegraphed.FiresAt</c> minus now, and the difference is a clock this object does not
    /// have: <c>FiresAt</c> is a moment on <c>RunState.Time</c>, which only core holds, and the hub
    /// publishes synchronously from inside <c>session.Tick</c> — so a subscriber cannot be late by
    /// more than the rest of the frame it was published on. The deadline the event carries is there
    /// for a reader that can be later than that, and nothing is yet.
    /// </remarks>
    private void OnTelegraphed(SpawnTelegraphed evt)
    {
        Rent(
            evt.Position.ToUnity(),
            SpawnRingRadius,
            SpawnDirector.TelegraphTime,
            fills: true);

#if UNITY_EDITOR
        // Eleven objects called "TelegraphRing" in the hierarchy are unreadable the moment one of
        // them misbehaves. Editor-only because it allocates a string per ring, which a wave should
        // not pay for on a phone — ProjectileViews' rule, for its reason.
        _live[_live.Count - 1].name = $"Ring {evt.SpecId}";
#endif
    }

    /// <summary>
    /// Something went off: a ring at the blast's real radius, fading from full.
    /// </summary>
    /// <remarks>
    /// The radius comes off the event rather than out of a catalog, which is why
    /// <c>EnemyExploded</c> carries it (M2-08): this is exactly the circle
    /// <c>EnemySystem.ApplyDamage</c> tested the player against, so stepping outside the drawn ring
    /// is the same act as stepping outside the one that hurt.
    /// <para>
    /// Drawn whether or not it caught anybody. A blast that is invisible when it misses teaches the
    /// player that near misses did not happen, which is the event's own reasoning about
    /// <c>HitPlayer</c> — and the reason that field is not read here at all.
    /// </para>
    /// </remarks>
    private void OnExploded(EnemyExploded evt)
    {
        Rent(
            evt.Position.ToUnity(),
            evt.Radius,
            BlastLingerTime,
            fills: false);

#if UNITY_EDITOR
        _live[_live.Count - 1].name = $"Blast {evt.SpecId}";
#endif
    }

    /// <summary>
    /// Puts one ring on the floor.
    /// </summary>
    /// <remarks>
    /// A ring is rented and bound in the same breath and never touched again from outside, which is
    /// the whole of why there is no id here: the only thing that can happen to it next is running
    /// out.
    /// </remarks>
    private void Rent(Vector3 centre, float radius, float duration, bool fills)
    {
        TelegraphRingView ring = _pool.Get();

        ring.Bind(centre, radius, duration, fills, ThreatArrows.Danger);

        _live.Add(ring);
    }
}
