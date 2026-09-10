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
/// The arena's population, on the Unity side: one <see cref="EnemyView"/> per enemy core says
/// exists, created and destroyed by the census events, and copied back into the snapshot every
/// frame. The other half of AR §4.3's loop for enemies — core decides <em>who</em>, this decides
/// nothing and reports <em>where</em>.
/// </summary>
/// <remarks>
/// <para>
/// It is a listener, not a spawner. Nothing here chooses that an enemy should exist: core's
/// <c>EnemySystem</c> does, and publishes <see cref="EnemySpawned"/> after the agent is in the
/// registry, so this can create the body knowing the id already resolves. The mirror holds on the
/// way out — <see cref="EnemyDespawned"/> arrives after the agent has left, so a body destroyed
/// here can never be one core still expects a position for.
/// </para>
/// <para>
/// <b>It rents and returns; it never creates or destroys.</b> A GameObject per spawn and a
/// <c>Destroy</c> per death was M1-07's accepted stopgap — exactly the per-wave garbage AR §14
/// bans — and M1-19 closed it: a <see cref="ViewPool{T}"/> holds every body for the length of the
/// run, and <see cref="EnemySpawned"/> and <see cref="EnemyDespawned"/> move them in and out of
/// service. Nothing allocates after the pool is warm, which is what lets an arena keep replacing
/// the dead for as long as the player survives.
/// </para>
/// <para>
/// Not a <see cref="MonoBehaviour"/>: it has no frame of its own and nothing in a scene should be
/// able to find it. It is registered in <c>RunScope</c> and disposed with the run, which is what
/// unsubscribes it — a listener that outlived its run would be handed the next run's ids.
/// </para>
/// </remarks>
public sealed class EnemyViews : IDisposable
{
    private readonly ViewPool<EnemyView> _pool;

    /// <summary>Core's id → the body standing in for it. The only index anything outside resolves through.</summary>
    private readonly Dictionary<int, EnemyView> _byId = new Dictionary<int, EnemyView>();

    /// <summary>
    /// Collider instance id → core's enemy id, for M1-12's overlap query.
    /// </summary>
    /// <remarks>
    /// Keyed by <c>GetInstanceID()</c> rather than by the <see cref="Collider"/> itself: Unity's
    /// <c>==</c> is overloaded to treat a destroyed object as null, but <c>Equals</c> and
    /// <c>GetHashCode</c> — which is what a dictionary uses — are not, so a destroyed collider
    /// would still hash to its old bucket. An int has no such second identity.
    /// </remarks>
    private readonly Dictionary<int, int> _idByColliderInstance = new Dictionary<int, int>();

    private readonly IDisposable _spawnedSubscription;
    private readonly IDisposable _despawnedSubscription;

    private bool _warnedAboutCapacity;
    private bool _disposed;

    /// <param name="resolver">
    /// The run's container, handed to the pool so bodies are instantiated through it. That is what
    /// runs an <c>[Inject]</c> on an enemy component — <c>EnemyHitFeedback</c> takes the event hub
    /// that way — and a body created the plain way would silently never be injected.
    /// </param>
    /// <param name="prefab">The one enemy body prefab. All archetypes share it until M2-06.</param>
    /// <param name="parent">
    /// Where instances are parented, or null for the scene root. The pool's root, and a tidiness
    /// argument — nothing looks anything up through it.
    /// </param>
    /// <param name="hub">The run's event hub, subscribed to for the length of this object's life.</param>
    /// <param name="prewarm">
    /// How many bodies to build before the run starts. The arena's steady-state population, so the
    /// only <c>Instantiate</c> calls of a whole run happen while the scene is still loading rather
    /// than on the frame a wave lands.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="resolver"/>, <paramref name="prefab"/> or <paramref name="hub"/> is null.
    /// <paramref name="parent"/> may be null; the others are the run being mis-wired.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="prewarm"/> is negative.</exception>
    public EnemyViews(
        IObjectResolver resolver,
        EnemyView prefab,
        Transform parent,
        DomainEventHub hub,
        int prewarm = 0)
    {
        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        // The pool makes the same three checks this constructor used to, and in the same way — a
        // destroyed prefab is a live reference that only compares equal to null through Unity's
        // operator, and a destroyed parent is normalised to the scene root.
        _pool = new ViewPool<EnemyView>(resolver, prefab, parent, prewarm);

        // Subscribed in the constructor, which is what makes the ordering safe: RunTicker takes
        // SnapshotBuilder, SnapshotBuilder takes this, so this object exists and is listening
        // before RunTicker.Start can ask core to spawn anything. Subscribing later — from a Start
        // of its own — would drop the whole opening population on the floor.
        _spawnedSubscription = hub.Subscribe<EnemySpawned>(OnSpawned);
        _despawnedSubscription = hub.Subscribe<EnemyDespawned>(OnDespawned);
    }

    /// <summary>How many bodies are currently standing in the scene.</summary>
    public int Count => _byId.Count;

    /// <summary>The body standing in for <paramref name="id"/>, if there is one.</summary>
    public bool TryGet(int id, out EnemyView view) => _byId.TryGetValue(id, out view);

    /// <summary>
    /// Which enemy <paramref name="collider"/> belongs to, if it belongs to one at all.
    /// </summary>
    /// <remarks>
    /// The answer M1-12's cone-overlap query needs, without a <c>GetComponent</c> per collider per
    /// swing. It is an instance method on the object that already owns the census rather than a
    /// static on <see cref="EnemyView"/> as the M1-07 spec sketched: a static dictionary is
    /// mutable static state, which this project bans outright, and with domain reload disabled on
    /// Play it would carry one session's colliders into the next with nothing in the log.
    /// </remarks>
    public bool TryGetId(Collider collider, out int id)
    {
        if (collider == null)
        {
            id = EnemyView.Unbound;
            return false;
        }

        return _idByColliderInstance.TryGetValue(collider.GetInstanceID(), out id);
    }

    /// <summary>
    /// Writes one <see cref="EnemySense"/> per live body into <paramref name="snapshot"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="WorldSnapshot.Clear"/> is deliberately <em>not</em> called here — the builder
    /// owns the frame and clears once, and a second clear would erase the player fields it wrote
    /// before this ran.
    /// </para>
    /// <para>
    /// Every field of the slot is assigned, including the two written as zero here. Slots are
    /// reused and <c>Clear</c> leaves their contents alone (AR §4.2), so a field left unwritten
    /// carries whatever the enemy that last occupied that slot put there — a stale line of sight
    /// belonging to somebody else. <c>PathDirectionToPlayer</c> is overwritten a moment later by
    /// <see cref="SnapshotBuilder"/>, which is the only place that knows where the player is; the
    /// zero written here is what a run without a baked NavMesh reports, and it has to be written
    /// rather than inherited for exactly the reason above. <c>HasLineOfSight</c> stays false until
    /// something answers it — CC §3.1 still skips line of sight deliberately.
    /// </para>
    /// <para>
    /// Order is the dictionary's and that is safe: core's <c>Ingest</c> looks every entry up by
    /// id, and its second pass walks the registry rather than the snapshot, so nothing downstream
    /// can observe the order these were written in. Spawn order — which <em>does</em> decide the
    /// targeter's tie-break — is the registry's and is untouched by this.
    /// </para>
    /// </remarks>
    public void CopyInto(WorldSnapshot snapshot)
    {
        // The concrete dictionary's value enumerator is a struct, so this foreach allocates
        // nothing. Iterating through IEnumerable<EnemyView> would box it, every frame.
        foreach (EnemyView view in _byId.Values)
        {
            if (snapshot.EnemyCount >= snapshot.EnemyCapacity)
            {
                WarnAboutCapacityOnce(snapshot.EnemyCapacity);
                return;
            }

            ref EnemySense sense = ref snapshot.AddEnemy();

            sense.Id = view.Id;
            sense.Position = view.Position.ToNum();
            sense.Velocity = view.Velocity.ToNum();
            sense.PathDirectionToPlayer = System.Numerics.Vector2.Zero;
            sense.HasLineOfSight = false;
        }
    }

    /// <summary>
    /// Stops listening and destroys every body — standing, pooled, or mid-dissolve. Called when
    /// <c>RunScope</c> is disposed, which is what leaving the Run scene does.
    /// </summary>
    /// <remarks>
    /// The bodies still in service are not returned first. The pool owns every instance it created
    /// and destroys the lot, so returning them would be twelve resets on objects about to stop
    /// existing — and the one thing that must not happen, a body outliving the run, cannot, because
    /// there is exactly one owner of the whole set.
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

        _pool.Dispose();

        _byId.Clear();
        _idByColliderInstance.Clear();
    }

    private void OnSpawned(EnemySpawned evt)
    {
        Vector3 position = evt.Position.ToUnity();

        // Rented, then bound: the pool hands over a clean body and this is the line that tells it
        // who it is standing in for and where. Bind sets the position, so the body is never drawn
        // at wherever its previous life ended — the two calls are in the same frame, before
        // anything renders.
        EnemyView view = _pool.Get();

        view.Bind(evt.Id, position);

        _byId[evt.Id] = view;

        // Body resolves itself when Awake has not run, which outside play mode it never does
        // (M1-12), so this index is filled in an EditMode test as well as in a run. Still guarded:
        // a prefab that somehow carries no collider should leave that enemy unhittable rather than
        // take the whole run down at the first spawn.
        if (view.Body != null)
        {
            _idByColliderInstance[view.Body.GetInstanceID()] = evt.Id;
        }

#if UNITY_EDITOR
        // Eight objects called "Enemy" in the hierarchy are unreadable the moment one of them
        // misbehaves. Editor-only because it allocates a string per spawn, which a wave of sixty
        // should not pay for on a phone.
        view.name = $"Enemy {evt.Id} ({evt.SpecId})";
#endif
    }

    private void OnDespawned(EnemyDespawned evt)
    {
        if (!_byId.TryGetValue(evt.Id, out EnemyView view))
        {
            // Not an error, for the reason EnemySystem.Despawn returns false rather than throwing:
            // an id may be retired without a body ever having been made for it.
            return;
        }

        _byId.Remove(evt.Id);

        if (view != null && view.Body != null)
        {
            _idByColliderInstance.Remove(view.Body.GetInstanceID());
        }

        // The collider index is dropped *before* the return, and it goes back in on the next rental
        // under whatever id this body is given then. The instance id itself does not change — the
        // object is the same one — so leaving the entry behind would have a corpse in the pool
        // answering "which enemy is this collider?" with the name of the enemy that died in it.
        _pool.Release(view);
    }

    /// <remarks>
    /// Once per run, not once per frame: at the cap this is true every frame for as long as the
    /// arena is full, and a per-frame warning would bury the Console and cost more than the
    /// enemies it is complaining about.
    /// </remarks>
    private void WarnAboutCapacityOnce(int capacity)
    {
        if (_warnedAboutCapacity)
        {
            return;
        }

        _warnedAboutCapacity = true;

        Debug.LogWarning(
            $"More enemy views ({_byId.Count}) than the snapshot can carry ({capacity}). The " +
            "extras are invisible to core this frame — it cannot see their positions, so nothing " +
            "will target them. The spawner is what must respect the cap (GD §11).");
    }
}
