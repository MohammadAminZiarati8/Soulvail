using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// <c>EnemyViews</c> with a body per archetype (M7-05g): one pool per body, a spawn wearing its
/// archetype's body, and a body going back to the pool it came from.
/// </summary>
/// <remarks>
/// Two scene objects stand in for the prefabs, as <c>ConeOverlapQueryTests</c>' one does — VContainer's
/// <c>Instantiate</c> takes a component, not an asset. Each carries <see cref="EnemyHitFeedback"/> so
/// the look can be seen to land on whichever body is rented; <c>Awake</c> never runs in EditMode, and
/// <c>SetArchetypeLook</c> writes the scale regardless (Traps §5).
/// </remarks>
[TestFixture]
public sealed class EnemyViewsTests
{
    private const int Prewarm = 3;

    private static readonly ContentId Husk = new ContentId("enemy.husk");
    private static readonly ContentId Rootling = new ContentId("enemy.rootling");
    private static readonly ContentId Sapling = new ContentId("enemy.sapling");

    private readonly List<Object> _created = new List<Object>();

    private GameObject _parent;
    private EnemyView _shared;
    private EnemyView _own;
    private IObjectResolver _container;
    private DomainEventHub _hub;
    private EnemyViews _views;

    [SetUp]
    public void CreateTemplates()
    {
        _parent = Track(new GameObject("Enemies"));
        _shared = Template("SharedBody");
        _own = Template("OwnBody");
        _hub = new DomainEventHub();

        // The hub is what EnemyHitFeedback's [Inject] takes, and a body is instantiated through the
        // container exactly as the run's are.
        var builder = new ContainerBuilder();
        builder.RegisterInstance(_hub);
        _container = builder.Build();
    }

    [TearDown]
    public void DestroyEverything()
    {
        _views?.Dispose();
        _views = null;
        _hub?.Dispose();
        _hub = null;
        _container?.Dispose();
        _container = null;

        foreach (Object created in _created)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        _created.Clear();
    }

    [Test]
    public void Book_ListsEachBodyOnce()
    {
        // Rule 2's input: two archetypes wearing one body are one pool, and an archetype with none
        // adds nothing.
        EnemyLookBook book = Book((Rootling, _own), (Sapling, _own), (Husk, null));

        Assert.That(book.Bodies, Is.EqualTo(new[] { _own }));
    }

    [Test]
    public void Views_PrewarmEveryBodysPool()
    {
        Build(Book((Rootling, _own), (Husk, null)));

        Assert.That(_views.PooledCountOf(null), Is.EqualTo(Prewarm), "The shared body's pool.");
        Assert.That(_views.PooledCountOf(_own), Is.EqualTo(Prewarm),
            "A body of its own is built before the run starts, not on the frame its wave lands.");
        Assert.That(_parent.transform.childCount, Is.EqualTo(Prewarm * 2));
    }

    [Test]
    public void Views_ABodyEqualToTheSharedOneIsTheSharedPool()
    {
        Build(Book((Rootling, _shared)));

        Assert.That(_parent.transform.childCount, Is.EqualTo(Prewarm), "One pool, not two of the same prefab.");
    }

    [Test]
    public void Spawn_WearsItsArchetypesBody()
    {
        Build(Book((Rootling, _own), (Husk, null)));

        Spawn(1, Rootling);
        Spawn(2, Husk);

        Assert.That(View(1).name, Does.StartWith("Enemy 1"), "Sanity: the census found it.");
        Assert.That(IsInstanceOf(View(1), _own), Is.True, "The Rootling wears its own body.");
        Assert.That(IsInstanceOf(View(2), _shared), Is.True, "The Husk wears the shared one.");
        Assert.That(_views.PooledCountOf(_own), Is.EqualTo(Prewarm - 1));
        Assert.That(_views.PooledCountOf(null), Is.EqualTo(Prewarm - 1));
    }

    [Test]
    public void Spawn_StillAppliesTheLookToABody()
    {
        Build(new EnemyLookBook(new Dictionary<ContentId, EnemyLook>
        {
            [Rootling] = new EnemyLook(Color.white, 1.2f, _own),
        }));

        Spawn(1, Rootling);

        Assert.That(View(1).transform.localScale.x, Is.EqualTo(1.2f).Within(1e-5f),
            "A body of its own is still scaled by its look, which is what a Bloater-sized variant needs.");
    }

    [Test]
    public void Despawn_ReturnsTheBodyToItsOwnPool()
    {
        Build(Book((Rootling, _own), (Husk, null)));

        Spawn(1, Rootling);
        EnemyView rootlingBody = View(1);
        _hub.Publish(new EnemyDespawned(1));

        Assert.That(_views.PooledCountOf(_own), Is.EqualTo(Prewarm), "Back in its own pool.");

        Spawn(2, Husk);

        Assert.That(View(2), Is.Not.SameAs(rootlingBody), "A Rootling's body never stands in for a Husk.");

        // The pool is a stack, so the body just returned is the next one out.
        Spawn(3, Rootling);

        Assert.That(View(3), Is.SameAs(rootlingBody));
    }

    [Test]
    public void Census_FindsABodiedEnemy()
    {
        Build(Book((Rootling, _own)));

        Spawn(7, Rootling);
        EnemyView view = View(7);

        Assert.That(_views.TryGetId(view.Body, out int id), Is.True);
        Assert.That(id, Is.EqualTo(7));
        Assert.That(_views.Count, Is.EqualTo(1));

        var snapshot = new WorldSnapshot(4);
        _views.CopyInto(snapshot);

        Assert.That(snapshot.EnemyCount, Is.EqualTo(1));
        Assert.That(snapshot.Enemies[0].Id, Is.EqualTo(7));
    }

    [Test]
    public void Dispose_DestroysEveryPool()
    {
        Build(Book((Rootling, _own), (Husk, null)));

        Spawn(1, Rootling);
        Spawn(2, Husk);

        _views.Dispose();
        _views = null;

        Assert.That(_parent.transform.childCount, Is.Zero, "Every body of every pool, standing or pooled.");
    }

    private void Build(EnemyLookBook book) =>
        _views = new EnemyViews(_container, _shared, _parent.transform, _hub, book, Prewarm);

    private void Spawn(int id, ContentId spec) =>
        _hub.Publish(new EnemySpawned(id, spec, new Vector3(id, 0f, 0f).ToNum()));

    private EnemyView View(int id)
    {
        Assert.That(_views.TryGet(id, out EnemyView view), Is.True, $"No body for {id}.");

        return view;
    }

    /// <summary>
    /// Whether <paramref name="view"/> was made from <paramref name="template"/>. The templates carry
    /// a marker child with their own name, which every instance copies.
    /// </summary>
    private static bool IsInstanceOf(EnemyView view, EnemyView template) =>
        view.transform.Find(template.name + "_Marker") != null;

    private EnemyView Template(string name)
    {
        GameObject template = Track(new GameObject(name));

        // Parked away from the origin, where a live template collider would sit in every query.
        template.transform.position = new Vector3(1000f, 0f, 1000f);

        EnemyView view = template.AddComponent<EnemyView>();
        view.Body.isTrigger = true;

        template.AddComponent<EnemyHitFeedback>();

        var marker = new GameObject(name + "_Marker");
        marker.transform.SetParent(template.transform, false);

        return view;
    }

    private GameObject Track(GameObject created)
    {
        _created.Add(created);

        return created;
    }

    private static EnemyLookBook Book(params (ContentId Id, EnemyView Body)[] looks) =>
        new EnemyLookBook(looks.ToDictionary(l => l.Id, l => new EnemyLook(EnemyLook.Default.Tint, 1f, l.Body)));
}
