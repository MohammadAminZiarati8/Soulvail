using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Soulvail.Game.Pooling;

/// <summary>
/// Something a <see cref="ViewPool{T}"/> can put into service and take back out again.
/// </summary>
/// <remarks>
/// Two methods rather than <c>OnEnable</c> and <c>OnDisable</c>, and the difference matters: Unity
/// runs those whenever the object is activated, including on the frame it is created and while a
/// scene is being torn down, so a rent-and-return rule written there would fire at moments that are
/// not rents or returns. These fire exactly once per life in the pool, from the two methods that
/// mean it.
/// </remarks>
public interface IPoolable
{
    /// <summary>Called as the instance is put into service, before it is activated.</summary>
    void OnSpawn();

    /// <summary>
    /// Called as the instance is taken out of service, before it is deactivated. Everything the
    /// previous life changed and the next one would inherit is undone here.
    /// </summary>
    void OnDespawn();
}

/// <summary>
/// A fixed set of bodies, rented and returned instead of created and destroyed. What AR §14 asks
/// for in place of an <c>Instantiate</c> per spawn and a <c>Destroy</c> per death.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a pool at all.</b> A wave is a GameObject per enemy, several times a minute, for the
/// length of a run — and every one of them is a managed allocation, a native object, and eventually
/// a garbage collection during a fight. On a phone that collection is a visible hitch at exactly
/// the moment the player is timing a dodge. Renting costs a stack pop and a <c>SetActive</c>.
/// </para>
/// <para>
/// <b>Instantiated through the container, never through <c>Object.Instantiate</c>.</b> That is what
/// runs <c>[Inject]</c> on the prefab's components — <c>EnemyHitFeedback</c> takes the run's event
/// hub that way — and it happens once, when the instance is created, rather than on every rent. A
/// pooled body therefore keeps its subscriptions for the life of the pool, which is the whole
/// reason those subscriptions are filtered by id.
/// </para>
/// <para>
/// <b>It owns every instance it made, including the rented ones.</b> <see cref="Dispose"/> destroys
/// the lot, so a caller that is holding rented instances when the run ends does not have to return
/// them first. That is what makes the pool, rather than its caller, the one answer to "who destroys
/// these" — and it is why <see cref="CountActive"/> is derived rather than counted.
/// </para>
/// <para>
/// Generic over <typeparamref name="T"/> rather than written against <c>EnemyView</c>, because the
/// second caller is already visible: M2-09's projectiles are the same rent-and-return with a
/// different component on the end, and a pool that knew about enemies would be copied rather than
/// reused.
/// </para>
/// </remarks>
/// <typeparam name="T">
/// The component the pool hands out. Constrained to <see cref="IPoolable"/> so the two callbacks
/// cannot be forgotten, and to <see cref="Component"/> so the pool can reach the
/// <see cref="GameObject"/> it has to activate.
/// </typeparam>
public sealed class ViewPool<T> : IDisposable
    where T : Component, IPoolable
{
    private readonly IObjectResolver _resolver;
    private readonly T _prefab;
    private readonly Transform _parent;

    /// <summary>
    /// Every instance this pool has created, rented or not. Only ever walked by
    /// <see cref="Dispose"/> — the per-frame paths touch <see cref="_inactive"/> and nothing else.
    /// </summary>
    private readonly List<T> _all;

    /// <summary>
    /// The instances waiting to be rented, used as a stack so a burst reuses the most recently
    /// returned bodies and leaves the rest cold. The same shape, for the same reason, as
    /// <c>EnemyRegistry</c>'s free list.
    /// </summary>
    private readonly Stack<T> _inactive;

    private bool _disposed;

    /// <param name="resolver">
    /// The run's container. Instances are created through it so <c>[Inject]</c> runs; a body made
    /// the plain way would silently never be injected.
    /// </param>
    /// <param name="prefab">What to instantiate. Never handed out itself.</param>
    /// <param name="parent">
    /// Where instances are parented, or null for the scene root. Tidiness in the hierarchy, and
    /// nothing more — the pool does not use the parent to find anything.
    /// </param>
    /// <param name="prewarm">
    /// How many instances to create up front. Paid at the run's loading moment rather than
    /// mid-wave, which is the point of the whole class: the first twelve spawns of a run would
    /// otherwise allocate exactly as the old code did, just once instead of repeatedly.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="resolver"/> or <paramref name="prefab"/> is null. <paramref name="parent"/>
    /// may be null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="prewarm"/> is negative.</exception>
    public ViewPool(IObjectResolver resolver, T prefab, Transform parent, int prewarm)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        // Unity's == rather than `is null`: an unassigned or destroyed prefab is a live reference
        // that only compares equal to null through the engine's operator.
        _prefab = prefab == null ? throw new ArgumentNullException(nameof(prefab)) : prefab;

        // Normalised to a real null the same way, so Instantiate is handed the scene root rather
        // than a destroyed transform.
        _parent = parent == null ? null : parent;

        if (prewarm < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(prewarm),
                prewarm,
                "prewarm cannot be negative.");
        }

        _all = new List<T>(prewarm);
        _inactive = new Stack<T>(prewarm);

        for (int i = 0; i < prewarm; i++)
        {
            T instance = Create();

            instance.gameObject.SetActive(false);

            _inactive.Push(instance);
        }
    }

    /// <summary>How many instances are currently rented out.</summary>
    /// <remarks>
    /// Derived rather than counted, so it cannot disagree with the two collections it is about.
    /// </remarks>
    public int CountActive => _all.Count - _inactive.Count;

    /// <summary>How many instances are sitting in the pool, waiting to be rented.</summary>
    public int CountInactive => _inactive.Count;

    /// <summary>
    /// Rents an instance: a returned one if there is one, a freshly created one if there is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IPoolable.OnSpawn"/> runs before the object is activated, so whatever it resets
    /// is already true by the time <c>OnEnable</c> and the first <c>Update</c> see the body. The
    /// caller still positions it afterwards — the pool has no opinion about where anything goes.
    /// </para>
    /// <para>
    /// Growing past <paramref name="prewarm"/> is allowed rather than refused. A pool that threw
    /// when it ran short would turn an under-estimated prewarm into a dead run; this way the cost
    /// is one allocation, once, and the Profiler says so.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    public T Get()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ViewPool<T>));
        }

        T instance = _inactive.Count > 0 ? _inactive.Pop() : Create();

        instance.OnSpawn();

        instance.gameObject.SetActive(true);

        return instance;
    }

    /// <summary>
    /// Returns <paramref name="instance"/> to the pool: out of service, deactivated, ready to be
    /// rented again.
    /// </summary>
    /// <remarks>
    /// <see cref="IPoolable.OnDespawn"/> runs before the object is deactivated, so a reset that
    /// touches a component which refuses to work while disabled still works. A destroyed instance is
    /// dropped rather than pooled — returning it would hand the next rent a null body — and a null
    /// one is ignored, so a caller cleaning up after a wave can return unconditionally.
    /// </remarks>
    public void Release(T instance)
    {
        // Unity's ==: a destroyed component is a live reference that only compares equal to null
        // through the engine's operator, and pooling one would be pooling nothing.
        if (instance == null)
        {
            return;
        }

        instance.OnDespawn();

        if (_disposed)
        {
            // The pool is gone and so is everything it owned; there is nowhere to put this back.
            // Not an error: disposal order between a pool and its caller is not worth constraining.
            return;
        }

        instance.gameObject.SetActive(false);

        _inactive.Push(instance);
    }

    /// <summary>
    /// Destroys every instance this pool created, rented or not, and refuses further rentals.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        for (int i = 0; i < _all.Count; i++)
        {
            T instance = _all[i];

            if (instance == null)
            {
                continue;
            }

            // The play-mode split InputAdapter makes and for the same reason: in edit mode
            // Object.Destroy destroys nothing and logs an *error* rather than throwing, which would
            // both leak the object and redden any EditMode test that disposes a pool (M0-14).
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance.gameObject);
            }
        }

        _all.Clear();
        _inactive.Clear();
    }

    /// <summary>
    /// Instantiates one instance through the container and records it as this pool's to destroy.
    /// </summary>
    /// <remarks>
    /// The four-argument overload, and not the three-argument one: that one reparents the instance
    /// to null when the resolver's origin is a <c>LifetimeScope</c>, which would take every body out
    /// of the parent this pool was given.
    /// </remarks>
    private T Create()
    {
        Transform prefabTransform = _prefab.transform;

        T instance = _resolver.Instantiate(
            _prefab,
            prefabTransform.position,
            prefabTransform.rotation,
            _parent);

        _all.Add(instance);

        return instance;
    }
}
