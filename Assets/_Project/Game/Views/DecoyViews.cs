using System;
using System.Collections.Generic;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views;

/// <summary>
/// Every corpse standing in the arena right now: one <see cref="DecoyView"/> per decoy core says is
/// down, rented on <see cref="DecoySpawned"/> and returned on <see cref="DecoyExpired"/>.
/// <see cref="MinionViews"/>' shape with the frame taken out of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a listener, not a spawner</b>, and all three subscriptions are taken in the constructor,
/// which is AR §18.1's rule: a <c>Start</c> of its own would be ordered against nothing and would
/// drop the first Shroudstep of a run in silence. Being on <c>RunTicker</c>'s dependency chain is
/// what guarantees the chain is built before core can publish anything — <see cref="ProjectileViews"/>'
/// bargain, for its reason.
/// </para>
/// <para>
/// <b>It has no <c>Step</c>, and the absence is the design</b> (rule 2). Every other census on this
/// scope is walked once a frame with <c>snapshot.Dt</c> because each holds something that is
/// interpolating while core counts it down. A decoy interpolates nothing: M5-03 rule 1 says
/// <em>"nothing about one changes after it is dropped"</em>, so the whole life of a body here is
/// <em>appear</em> and <em>disappear</em>, and the two moments arrive as the two events above.
/// <c>DecoyExpired</c> comes out of <c>LureSystem.Tick</c>, which runs immediately above
/// <c>EnemySystem.Ingest</c> (M5-03 rule 10), so the body is gone on the tick the taunt stops rather
/// than a frame later — which is the whole of what a clock here would have bought.
/// </para>
/// <para>
/// <b>And a third way a decoy leaves, which announces nothing at all: the stage boundary.</b>
/// <c>StageFlow.Advance</c> calls <c>LureSystem.Clear</c>, which is silent for
/// <c>ProjectileSystem.Clear</c>'s reason, so at a crossing every decoy is gone from core and every
/// body is still standing here — in the <em>next</em> arena, under ids core has just reset to 1.
/// <b>A decoy is the kind of thing that can genuinely cross one</b>: it stands for 3 s against a
/// boundary's 2 s of gate and arrival, which is the same arithmetic that makes a Wight's twenty
/// seconds reachable (M5-04b, ledger row 9) and is why <see cref="MinionViews"/> answered this
/// first. So this census tears down on <see cref="StageArrived"/> as well — the same event
/// <c>ArenaPool</c> swaps the room on, published by <c>StageFlow.EnterArrival</c> immediately after
/// the sweep it mirrors.
/// </para>
/// <para>
/// <b><see cref="Dispose"/> is not that answer and cannot be</b>, which is a correction to M5-05b
/// rule 4 rather than a refinement of it: this object is disposed when <c>RunScope</c> is, and a
/// stage crossing does not dispose the run — it swaps an arena underneath one. A census relying on
/// <see cref="Dispose"/> alone would be correct about leaving the Run scene and wrong about every
/// boundary inside it. <c>ProjectileViews</c> still has the identical gap and no answer; it is inert
/// by arithmetic rather than by design — a bolt lives well under a boundary's two seconds — and it
/// is on the parking lot.
/// </para>
/// <para>
/// Not a <see cref="MonoBehaviour"/>, for <see cref="ProjectileViews"/>' reason: it has no frame of
/// its own and nothing in a scene should be able to find it. It is registered in <c>RunScope</c> and
/// disposed with the run, which is what unsubscribes it — a listener that outlived its run would be
/// handed the next run's ids.
/// </para>
/// </remarks>
public sealed class DecoyViews : IDisposable
{
    private readonly ViewPool<DecoyView> _pool;

    /// <summary>Core's decoy id → the body standing in for it. The only index anything resolves through.</summary>
    /// <remarks>
    /// Keyed by id rather than held as a list, which is <see cref="ZoneViews"/>' argument at
    /// <see cref="LureSystem.Capacity"/> 2: a retirement shifts core's own entries down, so a census
    /// keyed by anything positional would retire the wrong body the first time two decoys overlapped
    /// and the older one rotted — which is exactly the half-second the capacity exists for.
    /// </remarks>
    private readonly Dictionary<int, DecoyView> _byId = new Dictionary<int, DecoyView>();

    private readonly IDisposable _spawnedSubscription;
    private readonly IDisposable _expiredSubscription;
    private readonly IDisposable _stageSubscription;

    private bool _disposed;

    /// <param name="resolver">
    /// The run's container, handed to the pool so bodies are instantiated through it — the rule
    /// <see cref="MinionViews"/> keeps. Nothing on <c>VFX_Decoy.prefab</c> takes an <c>[Inject]</c>
    /// today; going through the container anyway is what stops the first component that does from
    /// being silently deaf.
    /// </param>
    /// <param name="prefab">
    /// The one corpse body. Every decoy shares it — there is one kind of decoy and one skill that
    /// drops one.
    /// </param>
    /// <param name="parent">
    /// Where instances are parented, or null for the scene root. Tidiness, and nothing more — and
    /// deliberately not the arena, which is torn down and raised again at every boundary (M2-11a).
    /// </param>
    /// <param name="hub">The run's event hub, subscribed to for the length of this object's life.</param>
    /// <param name="prewarm">
    /// How many bodies to build before the run starts. <see cref="LureSystem.Capacity"/> — two, and
    /// there is never a third — so the pool cannot be asked for a body it does not already hold and
    /// the only <c>Instantiate</c> calls of a whole run happen while the scene is loading rather
    /// than on the frame a dodge is being timed (AR §14, GD §11.3).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="resolver"/>, <paramref name="prefab"/> or <paramref name="hub"/> is null.
    /// <paramref name="parent"/> may be null; the others are the run being mis-wired.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="prewarm"/> is negative.</exception>
    public DecoyViews(
        IObjectResolver resolver,
        DecoyView prefab,
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
        _pool = new ViewPool<DecoyView>(resolver, prefab, parent, prewarm);

        _spawnedSubscription = hub.Subscribe<DecoySpawned>(OnSpawned);
        _expiredSubscription = hub.Subscribe<DecoyExpired>(OnExpired);

        // The payload is deliberately unread: which stage has begun and which arena it is fought in
        // are somebody else's questions, and the only fact this census takes from the event is
        // *that* a crossing happened.
        _stageSubscription = hub.Subscribe<StageArrived>(_ => SweepTheField());
    }

    /// <summary>How many corpses are currently standing in the scene.</summary>
    public int Count => _byId.Count;

    /// <summary>How many bodies are sitting in the pool, waiting to be rented.</summary>
    /// <remarks>
    /// <see cref="ProjectileViews.PooledCount"/>'s reason: the sum of this and <see cref="Count"/>
    /// is the number that must stop growing once a run is under way, and a total that keeps climbing
    /// is the pool being bypassed. At a capacity of two that sum is two, for ever.
    /// </remarks>
    public int PooledCount => _pool.CountInactive;

    /// <summary>The body standing in for <paramref name="id"/>, if there is one.</summary>
    public bool TryGet(int id, out DecoyView view) => _byId.TryGetValue(id, out view);

    /// <summary>
    /// Stops listening and destroys every body — standing or pooled. Called when <c>RunScope</c> is
    /// disposed, which is what leaving the Run scene does.
    /// </summary>
    /// <remarks>
    /// The bodies still in service are not returned first, which is <see cref="MinionViews"/>'
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

        _spawnedSubscription.Dispose();
        _expiredSubscription.Dispose();
        _stageSubscription.Dispose();

        _pool.Dispose();

        _byId.Clear();
    }

    /// <remarks>
    /// A second spawn on a live id is not something core can do — ids are issued from 1 and never
    /// reused within a stage — so the existing body is retired first rather than leaked, which is
    /// <see cref="ZoneViews.OnSpawned"/>'s bargain: the cost of being wrong about that is one
    /// orphaned body per run, and the cost of not handling it is a body the pool can never take
    /// back.
    /// </remarks>
    private void OnSpawned(DecoySpawned evt)
    {
        if (_byId.ContainsKey(evt.Id))
        {
            Release(evt.Id);
        }

        // Rented, then bound: the pool hands over a clean body and this is the line that tells it
        // who it is standing in for and where. Bind sets the position, so the body is never drawn at
        // wherever its previous life ended.
        DecoyView view = _pool.Get();

        view.Bind(evt.Id, evt.Position.ToUnity());

        _byId[evt.Id] = view;

#if UNITY_EDITOR
        // Two objects called "Decoy" in the hierarchy are unreadable the moment one of them
        // misbehaves. Editor-only because it allocates a string per drop, which a phone should not
        // pay for — MinionViews' rule, for its reason.
        view.name = $"Decoy {evt.Id}";
#endif
    }

    /// <summary>The decoy has rotted, and core is the authority on that.</summary>
    /// <remarks>
    /// The event's duration is not consulted and no clock here counts it down. Rule 2: core owns the
    /// moment, this owns the body, and a second clock would eventually disagree with the first about
    /// which frame the taunt stopped on.
    /// </remarks>
    private void OnExpired(DecoyExpired evt) => Release(evt.Id);

    /// <summary>
    /// Sends every standing body back to the pool, because core has just swept the field without
    /// saying so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The class remarks carry the argument. What is worth having here is the shape of the failure
    /// this prevents, because none of it throws: a corpse stands in the arena the player has just
    /// walked into, taunting nobody because core has forgotten it; the pool is a body short, so the
    /// next Shroudstep of the run instantiates on the frame a dodge is being timed; and
    /// <see cref="_byId"/>'s entry for id 1 is overwritten by the next stage's first decoy, which
    /// leaks that body out of the census for the rest of the run.
    /// </para>
    /// <para>
    /// The entries are released first and the index cleared after, because a
    /// <see cref="Dictionary{TKey,TValue}"/> cannot be modified while it is enumerated —
    /// <see cref="MinionViews"/>' shape, and at a capacity of two nothing else about it matters.
    /// </para>
    /// </remarks>
    private void SweepTheField()
    {
        if (_byId.Count == 0)
        {
            return;
        }

        foreach (DecoyView view in _byId.Values)
        {
            _pool.Release(view);
        }

        _byId.Clear();
    }

    /// <remarks>
    /// An id nothing bound is not an error, for the reason <c>MinionViews.Release</c> ignores an
    /// unknown despawn: an id may legitimately never have had a body made for it — one dropped
    /// before this object existed, or one whose body went with a stage boundary.
    /// </remarks>
    private void Release(int id)
    {
        if (!_byId.TryGetValue(id, out DecoyView view))
        {
            return;
        }

        _byId.Remove(id);

        // Release tolerates a destroyed body and drops it rather than pooling it, so no check here.
        _pool.Release(view);
    }
}
