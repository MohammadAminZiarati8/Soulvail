using System;
using System.Collections.Generic;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views;

/// <summary>
/// The army, on the Unity side: one <see cref="MinionView"/> per Wight core says is standing,
/// rented and returned by the minion census events, and copied back into the snapshot's own minion
/// slots every frame. <see cref="EnemyViews"/>' shape on the friendly side of the fight.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a listener, not a spawner</b>, and the ordering is <see cref="EnemyViews"/>'.
/// <see cref="MinionSpawned"/> arrives after the agent is standing, so a body can be rented knowing
/// the id resolves; <see cref="MinionDespawned"/> and <see cref="MinionDied"/> both arrive after it
/// has left the army, so a body returned here can never be one core still expects a position for.
/// All the subscriptions are taken in the constructor, which is AR §18.1's rule — a <c>Start</c> of
/// its own would be ordered against nothing.
/// </para>
/// <para>
/// <b>Two events, one release</b> (rule 2). A clock running out and a killing blow are different
/// facts about the run — M5-04a rule 10 keeps them apart so that M5-06's Second Death cannot fire
/// on a Wight that simply timed out — and the same fact about the body. A census that listened to
/// only one of them would leave a body standing for the rest of the run on the other.
/// </para>
/// <para>
/// <b>And a third way a Wight leaves, which announces nothing at all: the stage boundary.</b>
/// <c>StageFlow.Advance</c> calls <c>MinionSystem.Clear</c>, which is silent for
/// <c>EnemySystem.Clear</c>'s reason, so at a crossing the army is gone from core and every body is
/// still standing here — in the <em>next</em> arena, under ids core has just reset to 1, holding
/// eight of the eight bodies the pool owns. Twenty seconds against a boundary's two makes a Wight
/// the one body in the game that can genuinely cross one (M5-04b, ledger row 9), so this census
/// tears down on <see cref="StageArrived"/> as well — the same event <c>ArenaPool</c> swaps the
/// room on, published by <c>StageFlow.EnterArrival</c> immediately after the sweep it mirrors.
/// <b>The projectile views have the same gap and no answer yet</b>, and so will the decoy view:
/// <c>ProjectileSystem.Clear</c> and <c>LureSystem.Clear</c> are swept at the same boundary and are
/// just as silent. The difference is only reachability — a bolt lives under a second and a decoy
/// three, against a crossing's two — which is why the answer is written here first.
/// </para>
/// <para>
/// Not a <see cref="MonoBehaviour"/>, for <see cref="EnemyViews"/>' reason: it has no frame of its
/// own and nothing in a scene should be able to find it. It is registered in <c>RunScope</c> and
/// disposed with the run, which is what unsubscribes it — a listener that outlived its run would be
/// handed the next run's ids.
/// </para>
/// </remarks>
public sealed class MinionViews : IDisposable
{
    private readonly ViewPool<MinionView> _pool;

    /// <summary>Core's id → the body standing in for it. The only index anything resolves through.</summary>
    /// <remarks>
    /// <b>Deliberately not a second entry in <c>EnemyViews._idByColliderInstance</c></b> (rule 1).
    /// A Wight is not in the collider index at all, which is what makes the player's swing unable
    /// to report one, and this dictionary is the whole of what a minion id resolves through.
    /// </remarks>
    private readonly Dictionary<int, MinionView> _byId = new Dictionary<int, MinionView>();

    private readonly IDisposable _spawnedSubscription;
    private readonly IDisposable _despawnedSubscription;
    private readonly IDisposable _diedSubscription;
    private readonly IDisposable _stageSubscription;

    private bool _warnedAboutCapacity;
    private bool _disposed;

    /// <param name="resolver">
    /// The run's container, handed to the pool so bodies are instantiated through it — the rule
    /// <see cref="EnemyViews"/> keeps. Nothing on <c>Wight.prefab</c> takes an <c>[Inject]</c>
    /// today; going through the container anyway is what stops the first component that does from
    /// being silently deaf.
    /// </param>
    /// <param name="prefab">
    /// The one Wight body. Every minion shares it — <c>EnemyViews</c>' one-prefab argument, and
    /// there is one kind of minion to share it between.
    /// </param>
    /// <param name="parent">
    /// Where instances are parented, or null for the scene root. Tidiness, and nothing more — and
    /// deliberately not the arena, which is torn down and raised again at every boundary (M2-11a).
    /// </param>
    /// <param name="hub">The run's event hub, subscribed to for the length of this object's life.</param>
    /// <param name="prewarm">
    /// How many bodies to build before the run starts. <c>MinionSystem.MaxConcurrent</c>, so the
    /// only <c>Instantiate</c> calls of a whole run happen while the scene is loading rather than
    /// on the tick a kill raises something (AR §14, GD §11.3). Rise puts that tick behind every
    /// fourth kill (M5-04b), which is the busiest moment a frame has.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="resolver"/>, <paramref name="prefab"/> or <paramref name="hub"/> is null.
    /// <paramref name="parent"/> may be null; the others are the run being mis-wired.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="prewarm"/> is negative.</exception>
    public MinionViews(
        IObjectResolver resolver,
        MinionView prefab,
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
        _pool = new ViewPool<MinionView>(resolver, prefab, parent, prewarm);

        _spawnedSubscription = hub.Subscribe<MinionSpawned>(OnSpawned);
        _despawnedSubscription = hub.Subscribe<MinionDespawned>(OnDespawned);
        _diedSubscription = hub.Subscribe<MinionDied>(OnDied);
        // The payload is deliberately unread: which stage has begun and which arena it is fought in
        // are somebody else's questions, and the only fact this census takes from the event is
        // *that* a crossing happened.
        _stageSubscription = hub.Subscribe<StageArrived>(_ => SweepTheArmy());
    }

    /// <summary>How many Wights are currently standing in the scene.</summary>
    public int Count => _byId.Count;

    /// <summary>
    /// How many bodies are sitting in the pool, waiting to be rented.
    /// </summary>
    /// <remarks>
    /// <see cref="ProjectileViews.PooledCount"/>'s reason: the sum of this and <see cref="Count"/>
    /// is the number that must stop growing once a run is under way, and a total that keeps
    /// climbing is the pool being bypassed. There is no other way to see that from inside a run.
    /// </remarks>
    public int PooledCount => _pool.CountInactive;

    /// <summary>The body standing in for <paramref name="id"/>, if there is one.</summary>
    public bool TryGet(int id, out MinionView view) => _byId.TryGetValue(id, out view);

    /// <summary>
    /// Writes one <see cref="EnemySense"/> per standing Wight into the snapshot's minion slots.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="WorldSnapshot.Clear"/> is deliberately <em>not</em> called here — the builder owns
    /// the frame and clears once, and a second clear would erase everything written before this ran.
    /// <see cref="EnemyViews.CopyInto"/>'s rule, and the reason both are called from one method.
    /// </para>
    /// <para>
    /// <b>Every field of the slot is assigned, zeroes included</b> (rule 3, AR §18.2). Slots are
    /// reused and <c>Clear</c> leaves their contents alone, so a field left unwritten carries
    /// whatever the Wight that last occupied that slot put there — a stale sense belonging to
    /// somebody else. Two of the five are written as nothing on purpose:
    /// <c>PathDirectionToPlayer</c> because <c>SnapshotBuilder.WriteSenses</c> is deliberately not
    /// extended to these slots (a Wight walks at an enemy and <c>NavPathSense</c> only ever paths to
    /// the player, so a route computed here would be to the wrong place, costed per Wight per
    /// frame, and read by nothing — M5-04a rule 4); and <c>HasLineOfSight</c> because nothing on the
    /// friendly side asks the question. The zeroes are written; the searches are not run.
    /// </para>
    /// <para>
    /// The concrete dictionary's value enumerator is a struct, so this <c>foreach</c> allocates
    /// nothing; iterating through <c>IEnumerable&lt;MinionView&gt;</c> would box it, every frame.
    /// </para>
    /// </remarks>
    public void CopyInto(WorldSnapshot snapshot)
    {
        foreach (MinionView view in _byId.Values)
        {
            if (snapshot.MinionCount >= snapshot.MinionCapacity)
            {
                WarnAboutCapacityOnce(snapshot.MinionCapacity);
                return;
            }

            ref EnemySense sense = ref snapshot.AddMinion();

            sense.Id = view.Id;
            sense.Position = view.Position.ToNum();
            sense.Velocity = view.Velocity.ToNum();
            sense.PathDirectionToPlayer = System.Numerics.Vector2.Zero;
            sense.HasLineOfSight = false;
        }
    }

    /// <summary>
    /// Stops listening and destroys every body — standing or pooled. Called when <c>RunScope</c> is
    /// disposed, which is what leaving the Run scene does.
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

        _spawnedSubscription.Dispose();
        _despawnedSubscription.Dispose();
        _diedSubscription.Dispose();
        _stageSubscription.Dispose();

        _pool.Dispose();

        _byId.Clear();
    }

    private void OnSpawned(MinionSpawned evt)
    {
        // Rented, then bound: the pool hands over a clean body and this is the line that tells it
        // who it is standing in for and where. Bind sets the position, so the body is never drawn
        // at wherever its previous life ended.
        MinionView view = _pool.Get();

        view.Bind(evt.Id, evt.Position.ToUnity());

        _byId[evt.Id] = view;

#if UNITY_EDITOR
        // Eight objects called "Wight" in the hierarchy are unreadable the moment one of them
        // misbehaves. Editor-only because it allocates a string per raise, which a Gravecaller
        // killing four enemies a second should not pay for on a phone.
        view.name = $"Wight {evt.Id} ({evt.SpecId})";
#endif
    }

    private void OnDespawned(MinionDespawned evt) => Release(evt.Id);

    /// <remarks>
    /// The same line as a despawn, and rule 2's whole content: the clock and the killing blow are
    /// two facts about the run and one fact about the body.
    /// </remarks>
    private void OnDied(MinionDied evt) => Release(evt.Id);

    /// <summary>
    /// Sends every standing body back to the pool, because core has just swept the army without
    /// saying so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The class remarks carry the argument. What is worth having here is the shape of the failure
    /// this prevents, because none of it throws: eight Wights stand in the arena the player has
    /// just walked into, reporting themselves into the snapshot under ids that no longer resolve;
    /// the pool has nothing left to hand out, so the next raise instantiates on a frame the whole
    /// point of the prewarm was to protect; and <see cref="_byId"/>'s entry for id 1 is overwritten
    /// by the next stage's first Wight, which leaks that body out of the census for the rest of the
    /// run.
    /// </para>
    /// <para>
    /// Walked as a list of keys copied into an array? No — <see cref="Dictionary{TKey,TValue}"/>
    /// cannot be modified while it is enumerated, so the entries are released first and the index
    /// is cleared after, which is the same thing without the copy. This runs twice a stage at most,
    /// so the <c>foreach</c> over the struct enumerator is free and nothing else about it matters.
    /// </para>
    /// </remarks>
    private void SweepTheArmy()
    {
        if (_byId.Count == 0)
        {
            return;
        }

        foreach (MinionView view in _byId.Values)
        {
            _pool.Release(view);
        }

        _byId.Clear();
    }

    /// <remarks>
    /// An id nothing bound is not an error, for the reason <c>EnemyViews.OnDespawned</c> ignores an
    /// unknown despawn: an id may legitimately never have had a body made for it — one retired
    /// before this object existed, or one whose body went with a stage boundary.
    /// </remarks>
    private void Release(int id)
    {
        if (!_byId.TryGetValue(id, out MinionView view))
        {
            return;
        }

        _byId.Remove(id);

        _pool.Release(view);
    }

    /// <remarks>
    /// Once per run rather than once per frame, which is <see cref="EnemyViews"/>' rule: at the cap
    /// this would be true every frame for as long as the army is full, and a per-frame warning
    /// would bury the Console and cost more than the bodies it is complaining about.
    /// <para>
    /// <b>Unreachable while core respects its own ceiling</b> — the snapshot's minion array is sized
    /// from <c>MinionSystem.MaxConcurrent</c> and the system refuses a raise above it — so this
    /// firing means the census is holding bodies core has forgotten about, which is the stage
    /// boundary above having failed rather than a cap being too small.
    /// </para>
    /// </remarks>
    private void WarnAboutCapacityOnce(int capacity)
    {
        if (_warnedAboutCapacity)
        {
            return;
        }

        _warnedAboutCapacity = true;

        Debug.LogWarning(
            $"More minion views ({_byId.Count}) than the snapshot can carry ({capacity}). The "
                + "extras are invisible to core this frame, so it cannot see where they are and "
                + "will not walk them. MinionSystem refuses a raise above its own ceiling, so this "
                + "means bodies are standing that core has already retired.");
    }
}
