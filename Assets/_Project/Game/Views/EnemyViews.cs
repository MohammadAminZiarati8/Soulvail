using System;
using System.Collections.Generic;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;
using VContainer.Unity;

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
/// <b>Instantiate and Destroy are a stopgap.</b> Creating a GameObject per spawn and destroying it
/// per despawn is exactly the per-wave garbage AR §14 bans, and it is accepted only because M1
/// needs eight dummies standing still while targeting and cone hits are built. M1-19's
/// <c>ViewPool</c> replaces both calls with a rent and a return, and <see cref="EnemyView.Bind"/>
/// and <c>Unbind</c> already exist so that change is this file and nothing else.
/// </para>
/// <para>
/// Not a <see cref="MonoBehaviour"/>: it has no frame of its own and nothing in a scene should be
/// able to find it. It is registered in <c>RunScope</c> and disposed with the run, which is what
/// unsubscribes it — a listener that outlived its run would be handed the next run's ids.
/// </para>
/// </remarks>
public sealed class EnemyViews : IDisposable
{
    private readonly IObjectResolver _resolver;
    private readonly EnemyView _prefab;
    private readonly Transform _parent;

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
    /// The run's container, used to instantiate the prefab. <c>resolver.Instantiate</c> rather
    /// than <c>Object.Instantiate</c>, so an <c>[Inject]</c> on a future enemy component is
    /// honoured — a health bar (M3-13) is the first that will want one, and a body created the
    /// plain way would silently never be injected.
    /// </param>
    /// <param name="prefab">The one enemy body prefab. All archetypes share it until M2-06.</param>
    /// <param name="parent">
    /// Where instances are parented, or null for the scene root. A tidiness argument today and
    /// M1-19's pool root tomorrow.
    /// </param>
    /// <param name="hub">The run's event hub, subscribed to for the length of this object's life.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="resolver"/>, <paramref name="prefab"/> or <paramref name="hub"/> is null.
    /// <paramref name="parent"/> may be null; the others are the run being mis-wired.
    /// </exception>
    public EnemyViews(IObjectResolver resolver, EnemyView prefab, Transform parent, DomainEventHub hub)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        // Unity's == rather than `is null`: RunScope supplies this from a serialized field, so an
        // unassigned or destroyed prefab is a live reference that only compares equal to null
        // through the engine's operator.
        _prefab = prefab == null ? throw new ArgumentNullException(nameof(prefab)) : prefab;

        // Normalised to a real null the same way, so `Instantiate(..., parent)` is handed the
        // scene root rather than a destroyed transform.
        _parent = parent == null ? null : parent;

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
    /// Every field of the slot is assigned, including the two that are still zero. Slots are
    /// reused and <c>Clear</c> leaves their contents alone (AR §4.2), so a field left unwritten
    /// carries whatever the enemy that last occupied that slot put there — a stale line of sight
    /// belonging to somebody else. <c>PathDirectionToPlayer</c> and <c>HasLineOfSight</c> become
    /// real senses in M1-19, when the NavMesh exists to answer them.
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
    /// Stops listening and destroys every body still standing. Called when <c>RunScope</c> is
    /// disposed, which is what leaving the Run scene does.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _spawnedSubscription.Dispose();
        _despawnedSubscription.Dispose();

        foreach (EnemyView view in _byId.Values)
        {
            DestroyBody(view);
        }

        _byId.Clear();
        _idByColliderInstance.Clear();
    }

    private void OnSpawned(EnemySpawned evt)
    {
        Vector3 position = evt.Position.ToUnity();

        EnemyView view = _resolver.Instantiate(_prefab, position, Quaternion.identity, _parent);

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

        DestroyBody(view);
    }

    /// <remarks>
    /// <c>Unbind</c> before the destroy, so a view that M1-19 later pools instead of destroying
    /// takes the same path out of service either way.
    /// <para>
    /// The play-mode split is the one <c>InputAdapter</c> makes and for the same reason: in edit
    /// mode <c>Object.Destroy</c> destroys nothing and logs an <em>error</em> rather than
    /// throwing, which would both leak the object and redden any EditMode test that disposes one
    /// (M0-14).
    /// </para>
    /// </remarks>
    private static void DestroyBody(EnemyView view)
    {
        if (view == null)
        {
            return;
        }

        view.Unbind();

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(view.gameObject);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(view.gameObject);
        }
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
