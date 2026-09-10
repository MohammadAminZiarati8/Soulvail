using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Game.Pooling;
using Soulvail.Tests.Core.Support;
using UnityEngine;
using VContainer;

namespace Soulvail.Tests.Game.Pooling;

/// <summary>
/// The pool that replaces an <c>Instantiate</c> per spawn and a <c>Destroy</c> per death. Three
/// things can be wrong with it and all three are invisible in a playtest: it hands out a second
/// object instead of reusing the first, it forgets to tell a body it has changed hands, or it
/// creates its prewarm active and drops twelve enemies into the arena at the start of the run.
/// </summary>
/// <remarks>
/// <para>
/// The rented component is a stand-in rather than <c>EnemyView</c>. What is under test is the
/// rent-and-return itself — the two callbacks, the reuse, the activation — and a real enemy body
/// would bring a <c>CharacterController</c>, a collider, a dissolve and an injected event hub to a
/// fixture that has no opinion about any of them. <c>EnemyView</c>'s own reset is exercised where it
/// lives, through the census.
/// </para>
/// <para>
/// The prefab is a scene object, which is all <c>IObjectResolver.Instantiate</c> needs — it takes a
/// <c>Component</c>, not an asset. Every object either the pool or this fixture creates is destroyed
/// in the teardown, because an EditMode test that leaks a GameObject leaks it into every test that
/// runs after it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ViewPoolTests
{
    private GameObject _prefabObject;
    private CountingPoolable _prefab;
    private IObjectResolver _container;
    private ViewPool<CountingPoolable> _pool;

    [SetUp]
    public void CreatePool()
    {
        _prefabObject = new GameObject("PoolablePrefab");
        _prefab = _prefabObject.AddComponent<CountingPoolable>();

        _container = new ContainerBuilder().Build();
    }

    [TearDown]
    public void DestroyPool()
    {
        _pool?.Dispose();
        _pool = null;

        if (_prefabObject != null)
        {
            Object.DestroyImmediate(_prefabObject);
        }
    }

    [Test]
    public void Pool_ReusesReleasedInstance()
    {
        _pool = Pool(prewarm: 0);

        CountingPoolable first = _pool.Get();

        Assert.That(_pool.CountActive, Is.EqualTo(1));
        Assert.That(_pool.CountInactive, Is.Zero);

        _pool.Release(first);

        Assert.That(_pool.CountActive, Is.Zero);
        Assert.That(_pool.CountInactive, Is.EqualTo(1));

        CountingPoolable second = _pool.Get();

        Assert.That(
            second,
            Is.SameAs(first),
            "A returned body is rented again rather than replaced. The whole point of the class.");

        Assert.That(
            _pool.CountInactive,
            Is.Zero,
            "Nothing is left waiting once the only instance is back in service.");
    }

    [Test]
    public void Pool_CallsSpawnAndDespawn()
    {
        _pool = Pool(prewarm: 0);

        CountingPoolable instance = _pool.Get();

        Assert.That(instance.SpawnCount, Is.EqualTo(1));
        Assert.That(instance.DespawnCount, Is.Zero);
        Assert.That(
            instance.gameObject.activeSelf,
            Is.True,
            "OnSpawn runs before the activation, so whatever it reset is already true by the time "
                + "OnEnable sees the body.");

        _pool.Release(instance);

        Assert.That(instance.SpawnCount, Is.EqualTo(1));
        Assert.That(instance.DespawnCount, Is.EqualTo(1));
        Assert.That(instance.gameObject.activeSelf, Is.False);

        Assert.That(
            instance.Order,
            Is.EqualTo(new[] { "spawn", "despawn" }),
            "Once each, in that order — a body that was told it despawned twice would undo a "
                + "reset the next rental had already relied on.");
    }

    [Test]
    public void Pool_PrewarmCreatesInactive()
    {
        _pool = Pool(prewarm: 5);

        Assert.That(_pool.CountInactive, Is.EqualTo(5));
        Assert.That(_pool.CountActive, Is.Zero);

        // Rented and returned so the fixture can look at all five without the pool handing the
        // same one back twice. Every one of them must have been created switched off: the cost of
        // prewarming is paid at the run's loading moment, and five bodies standing in the arena
        // before the run has spawned anything is not a cost, it is a bug.
        var rented = new List<CountingPoolable>();

        for (int i = 0; i < 5; i++)
        {
            CountingPoolable instance = _pool.Get();

            Assert.That(
                instance.WasActiveBeforeSpawn,
                Is.False,
                "A prewarmed instance sits inactive until it is rented.");

            rented.Add(instance);
        }

        Assert.That(_pool.CountInactive, Is.Zero);
        Assert.That(_pool.CountActive, Is.EqualTo(5));

        for (int i = 0; i < rented.Count; i++)
        {
            _pool.Release(rented[i]);
        }
    }

    /// <summary>
    /// Rule 8's half that an EditMode test can actually measure: a rent and a return, once the pool
    /// is warm, allocate nothing.
    /// </summary>
    /// <remarks>
    /// A hundred iterations rather than the default ten thousand, because each one crosses into the
    /// engine twice to toggle a GameObject. Amortised growth is not the risk here — the pool's
    /// collections never grow once the prewarm covers the rental — so the cheap version of the
    /// measurement is enough to catch the mistake that matters, which is a rent that instantiates.
    /// </remarks>
    [Test]
    public void Pool_RentAndReturn_DoesNotAllocate()
    {
        _pool = Pool(prewarm: 1);

        AllocationAssert.None(
            () =>
            {
                CountingPoolable instance = _pool.Get();
                _pool.Release(instance);
            },
            iterations: 100);

        Assert.That(_pool.CountInactive, Is.EqualTo(1), "Sanity: the pool still holds its one body.");
    }

    private ViewPool<CountingPoolable> Pool(int prewarm) =>
        new ViewPool<CountingPoolable>(_container, _prefab, null, prewarm);

    /// <summary>
    /// A body that does nothing but remember what the pool told it, and when.
    /// </summary>
    private sealed class CountingPoolable : MonoBehaviour, IPoolable
    {
        /// <summary>
        /// How many transitions <see cref="Order"/> keeps. Small and fixed, so the list is created
        /// at its final size and never grows — a stub that allocated on its own would fail the
        /// allocation row above for a reason that has nothing to do with the pool.
        /// </summary>
        private const int OrderCapacity = 4;

        private readonly List<string> _order = new List<string>(OrderCapacity);

        public int SpawnCount { get; private set; }

        public int DespawnCount { get; private set; }

        /// <summary>
        /// Whether the GameObject was already active when <see cref="OnSpawn"/> ran. The only
        /// moment a prewarmed instance's activity can be observed from inside the pool's own
        /// sequence, since <c>Get</c> switches it on immediately afterwards.
        /// </summary>
        public bool WasActiveBeforeSpawn { get; private set; }

        public IReadOnlyList<string> Order => _order;

        public void OnSpawn()
        {
            WasActiveBeforeSpawn = gameObject.activeSelf;

            SpawnCount++;
            Record("spawn");
        }

        public void OnDespawn()
        {
            DespawnCount++;
            Record("despawn");
        }

        private void Record(string transition)
        {
            if (_order.Count < OrderCapacity)
            {
                _order.Add(transition);
            }
        }
    }
}
