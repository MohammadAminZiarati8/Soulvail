using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEngine;
using VContainer;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// The one physics query in the combat loop, measured. Every row here is a fact core cannot check
/// for itself: if the wedge is the wrong shape, or the same enemy is reported twice, core damages
/// the wrong things and nothing downstream can tell.
/// </summary>
/// <remarks>
/// <para>
/// The angle rows are pure arithmetic and need no scene. <see cref="Query_ReturnsUniqueIds_InCone"/>
/// does: it builds three real bodies with real trigger colliders on the Enemy layer, because the
/// half of this class that can be wrong in a way the arithmetic cannot — the layer mask, the
/// trigger interaction, the collider-to-id lookup — only exists once physics is involved.
/// </para>
/// <para>
/// Every vector here is <c>UnityEngine</c>'s, and the one <c>System.Numerics</c> value in the file
/// is fully qualified where it is built. With a <c>using</c> for both namespaces a boundary
/// crossing and an identity look the same on the page (M0-05).
/// </para>
/// </remarks>
[TestFixture]
public sealed class ConeOverlapQueryTests
{
    private static readonly ContentId HuskId = new ContentId("enemy.husk");

    /// <summary>The Censer's reach (CC §7) — the number the wedge rows are written against.</summary>
    private const float Range = 8f;

    /// <summary>The Censer's arc: ±30° either side of the facing.</summary>
    private const float Angle = 60f;

    private GameObject _templateObject;
    private IObjectResolver _container;
    private DomainEventHub _hub;
    private EnemyViews _views;
    private ConeOverlapQuery _query;
    private int[] _ids;

    [SetUp]
    public void CreateQuery()
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");

        Assert.That(
            enemyLayer,
            Is.GreaterThanOrEqualTo(0),
            "The project has no Enemy layer, so nothing a swing sweeps can be on it (M1-07).");

        // A scene object standing in for the prefab, which is all VContainer's Instantiate needs —
        // it takes a Component, not an asset. AddComponent brings the CapsuleCollider with it, per
        // EnemyView's [RequireComponent], and Body resolves it lazily because Awake never runs
        // outside play mode (M1-12).
        _templateObject = new GameObject("EnemyTemplate");
        _templateObject.layer = enemyLayer;

        // Parked far outside every wedge below. The template is a live scene object with a live
        // collider, so physics finds it like any other — and left at the origin it would sit inside
        // the apex of every cone in this fixture.
        _templateObject.transform.position = new Vector3(1000f, 0f, 1000f);

        EnemyView template = _templateObject.AddComponent<EnemyView>();

        // A trigger, exactly as Enemy.prefab authors it — which is the whole reason the query asks
        // for QueryTriggerInteraction.Collide. Set through Body so the lazy resolve is exercised
        // here too rather than only inside EnemyViews.
        template.Body.isTrigger = true;

        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();
        _views = new EnemyViews(_container, template, null, _hub, EmptyLookBook());

        _query = new ConeOverlapQuery(ConeOverlapQuery.DefaultCapacity, 1 << enemyLayer, _views);
        _ids = new int[_query.Capacity];
    }

    [TearDown]
    public void DestroyQuery()
    {
        _views.Dispose();

        if (_templateObject != null)
        {
            Object.DestroyImmediate(_templateObject);
        }
    }

    [Test]
    public void IsInCone_Inside()
    {
        Assert.That(
            ConeOverlapQuery.IsInCone(Vector3.zero, Vector2.up, new Vector3(0f, 0f, 5f), Range, Angle),
            Is.True,
            "Straight ahead and well inside the reach.");
    }

    /// <remarks>
    /// A tenth of a degree either side of the edge, which is far wider than float error and far
    /// narrower than any mistake worth making: a half-angle used where a full one belongs would put
    /// the boundary at 60°, not 30°, and both of these rows would agree.
    /// </remarks>
    [Test]
    public void IsInCone_AtEdgeAngle()
    {
        Assert.That(PointAt(29.9f, 5f), Is.True, "29.9° is inside a 60° arc.");
        Assert.That(PointAt(30.1f, 5f), Is.False, "30.1° is outside it.");
    }

    [Test]
    public void IsInCone_BeyondRange()
    {
        Assert.That(
            ConeOverlapQuery.IsInCone(Vector3.zero, Vector2.up, new Vector3(0f, 0f, 8f), Range, Angle),
            Is.True,
            "Exactly at the reach is within it.");

        Assert.That(
            ConeOverlapQuery.IsInCone(Vector3.zero, Vector2.up, new Vector3(0f, 0f, 8.1f), Range, Angle),
            Is.False,
            "A tenth of a metre past it is not.");
    }

    [Test]
    public void IsInCone_Behind()
    {
        Assert.That(
            ConeOverlapQuery.IsInCone(Vector3.zero, Vector2.up, new Vector3(0f, 0f, -3f), Range, Angle),
            Is.False,
            "Close enough to touch, and behind you — CC §4.2's arc is the arc.");
    }

    /// <remarks>
    /// The rule every enemy sense already follows (M1-06): the height between a player capsule's
    /// centre and an enemy's is a rendering detail, and counting it would shorten every swing by an
    /// amount nobody authored.
    /// </remarks>
    [Test]
    public void IsInCone_IgnoresY()
    {
        Assert.That(
            ConeOverlapQuery.IsInCone(Vector3.zero, Vector2.up, new Vector3(0f, 5f, 4f), Range, Angle),
            Is.True,
            "Five metres up and four ahead is four away.");
    }

    [Test]
    public void IsInCone_AtOrigin()
    {
        Assert.That(
            ConeOverlapQuery.IsInCone(Vector3.zero, Vector2.up, Vector3.zero, Range, Angle),
            Is.True,
            "A dummy standing on the player is in front of the player.");
    }

    /// <remarks>
    /// A negative range is not "nothing within it": squaring turns −1 into 1, so an unguarded test
    /// would silently treat a nonsense argument as a plausible one-metre wedge. Same trap M1-09's
    /// focus radius fell into.
    /// </remarks>
    [Test]
    public void IsInCone_NonPositiveRangeHitsNothing()
    {
        Assert.That(
            ConeOverlapQuery.IsInCone(Vector3.zero, Vector2.up, new Vector3(0f, 0f, 0.5f), -1f, Angle),
            Is.False,
            "A negative reach must not become a one-metre one.");
    }

    [Test]
    public void Query_ReturnsUniqueIds_InCone()
    {
        int ahead = Spawn(1, new Vector3(0f, 0f, 3f));
        int offToTheSide = Spawn(2, new Vector3(2f, 0f, 4f));
        Spawn(3, new Vector3(0f, 0f, -3f));

        // Colliders are placed by the transform writes above, and the physics scene does not know
        // about those until it is told — without this the query sweeps the positions the bodies
        // were instantiated at.
        Physics.SyncTransforms();

        int count = _query.Query(Cone(Vector3.zero, Vector2.up), _ids);

        Assert.That(count, Is.EqualTo(2), "Two in the wedge; the third is behind the player.");
        Assert.That(Written(count), Is.EquivalentTo(new[] { ahead, offToTheSide }));
    }

    /// <remarks>
    /// The one thing a wide sweep must not do is charge the same enemy twice — CC §4.2's "one
    /// damage event per enemy per swing". Core deduplicates as well (M1-11), and the belt and
    /// braces are deliberate: this is where a multi-collider enemy would first go wrong, and core's
    /// buffer is sized on the assumption that this end already deduplicated.
    /// </remarks>
    [Test]
    public void Query_ReportsEachEnemyOnce()
    {
        int only = Spawn(1, new Vector3(0f, 0f, 2f));

        Physics.SyncTransforms();

        int count = _query.Query(Cone(Vector3.zero, Vector2.up), _ids);

        Assert.That(count, Is.EqualTo(1));
        Assert.That(_ids[0], Is.EqualTo(only));
    }

    /// <remarks>
    /// A miss is an answer, not a silence: <c>RunTicker</c> reports a count of zero exactly as it
    /// reports a count of five, because the report is what retires core's pending request.
    /// </remarks>
    [Test]
    public void Query_FindsNothingWhenTheArenaIsEmpty()
    {
        Physics.SyncTransforms();

        Assert.That(_query.Query(Cone(Vector3.zero, Vector2.up), _ids), Is.Zero);
    }

    [Test]
    public void Query_AllocatesNothing()
    {
        Spawn(1, new Vector3(0f, 0f, 3f));
        Spawn(2, new Vector3(2f, 0f, 4f));
        Spawn(3, new Vector3(0f, 0f, -3f));

        Physics.SyncTransforms();

        ConeHitIntent cone = Cone(Vector3.zero, Vector2.up);

        // A thousand rather than the default ten thousand: each iteration is a real physics
        // overlap, and the claim — steady-state allocation is zero — is settled long before then.
        AllocationAssert.None(() => _query.Query(cone, _ids), 1_000);
    }

    /// <summary>The first <paramref name="count"/> ids the query wrote, as an array to assert on.</summary>
    private int[] Written(int count)
    {
        var written = new int[count];

        System.Array.Copy(_ids, written, count);

        return written;
    }

    /// <summary>Spawns a body through the census event, exactly as a run does.</summary>
    private int Spawn(int id, Vector3 position)
    {
        _hub.Publish(new EnemySpawned(id, HuskId, position.ToNum()));

        return id;
    }

    private static ConeHitIntent Cone(Vector3 origin, Vector2 facingXZ) =>
        new ConeHitIntent(1, origin.ToNum(), facingXZ.ToNum(), Range, Angle);

    /// <summary>A point <paramref name="distance"/> away, <paramref name="degrees"/> off +Z.</summary>
    private static bool PointAt(float degrees, float distance)
    {
        float radians = degrees * Mathf.Deg2Rad;

        var point = new Vector3(Mathf.Sin(radians) * distance, 0f, Mathf.Cos(radians) * distance);

        return ConeOverlapQuery.IsInCone(Vector3.zero, Vector2.up, point, Range, Angle);
    }

    /// <summary>
    /// A look book with nothing in it, which is all these rows need: every archetype falls back to
    /// <c>EnemyLook.Default</c> and the bodies here carry no <c>EnemyHitFeedback</c> to apply it to.
    /// Required rather than optional on <c>EnemyViews</c> (M2-06), so it is passed rather than
    /// omitted.
    /// </summary>
    private static EnemyLookBook EmptyLookBook()
        => new EnemyLookBook(new Dictionary<ContentId, EnemyLook>());
}
