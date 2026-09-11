using System;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// The census that turns the two projectile events into bodies, and the arc those bodies draw.
/// Three things can be wrong here and none of them throws: a bolt is instantiated rather than
/// rented, a landed shot leaves its body hanging in the air, or the picture disagrees with the
/// straight line core measured the flight time along.
/// </summary>
/// <remarks>
/// <para>
/// The prefab is a scene object rather than <c>Projectile.prefab</c>, which is all
/// <c>IObjectResolver.Instantiate</c> needs — it takes a <see cref="Component"/>, not an asset —
/// and it keeps the fixture from depending on how the art is dressed. <c>Awake</c> never runs in
/// EditMode (Traps §5), so these bodies carry the field initialisers: an arc height of 1.2 m and a
/// rest pose at the origin, which is what the real prefab is authored at anyway.
/// </para>
/// <para>
/// Every object the fixture or the pool creates is destroyed in the teardown. An EditMode test that
/// leaks a GameObject leaks it into every test that runs after it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ProjectileViewsTests
{
    private static readonly ContentId Spitter = new ContentId("enemy.spitter");

    /// <summary>
    /// The prefab's own <c>_arcHeight</c>, in metres. Duplicated here rather than read back,
    /// because the row that matters is that the hump is <em>there</em> and is what the field says.
    /// </summary>
    private const float ArcHeight = 1.2f;

    private GameObject _prefabObject;
    private ProjectileView _prefab;
    private IObjectResolver _container;
    private DomainEventHub _hub;
    private ProjectileViews _views;

    [SetUp]
    public void CreateCensus()
    {
        _prefabObject = new GameObject("ProjectilePrefab");
        _prefab = _prefabObject.AddComponent<ProjectileView>();

        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();
    }

    [TearDown]
    public void DestroyCensus()
    {
        _views?.Dispose();
        _views = null;

        _hub?.Dispose();
        _hub = null;

        _container?.Dispose();
        _container = null;

        if (_prefabObject != null)
        {
            Object.DestroyImmediate(_prefabObject);
        }
    }

    [Test]
    public void Fired_RentsAndBindsAtTheOrigin()
    {
        _views = Census(prewarm: 1);

        Fire(id: 1, origin: new Vector3(3f, 0f, 3f), target: new Vector3(9f, 0f, 3f), flightTime: 1f);

        Assert.That(_views.Count, Is.EqualTo(1));

        Assert.That(_views.TryGet(1, out ProjectileView view), Is.True, "The id core issued must resolve.");
        Assert.That(view.Id, Is.EqualTo(1));
        Assert.That(view.IsBound, Is.True);

        // At the origin, not at wherever the pooled body was sitting: a bolt drawn for one frame at
        // the previous shot's landing point is a streak across the arena.
        Assert.That(view.transform.position.x, Is.EqualTo(3f).Within(1e-4f));
        Assert.That(view.transform.position.z, Is.EqualTo(3f).Within(1e-4f));
    }

    [Test]
    public void Fired_TwiceRentsTwoBodies()
    {
        _views = Census(prewarm: 2);

        Fire(id: 1, origin: Vector3.Zero, target: new Vector3(6f, 0f, 0f), flightTime: 1f);
        Fire(id: 2, origin: Vector3.Zero, target: new Vector3(0f, 0f, 6f), flightTime: 1f);

        Assert.That(_views.Count, Is.EqualTo(2));

        Assert.That(_views.TryGet(1, out ProjectileView first), Is.True);
        Assert.That(_views.TryGet(2, out ProjectileView second), Is.True);

        Assert.That(
            second,
            Is.Not.SameAs(first),
            "Two shots in the air are two bodies. One would draw a single bolt for both.");
    }

    [Test]
    public void Impacted_ReleasesTheBody()
    {
        _views = Census(prewarm: 1);

        Fire(id: 1, origin: Vector3.Zero, target: new Vector3(6f, 0f, 0f), flightTime: 1f);

        Assert.That(_views.TryGet(1, out ProjectileView view), Is.True);

        Impact(id: 1, position: new Vector3(6f, 0f, 0f));

        Assert.That(_views.Count, Is.Zero, "A landed shot leaves no body drawn.");
        Assert.That(_views.TryGet(1, out _), Is.False);

        Assert.That(view.Id, Is.EqualTo(ProjectileView.Unbound), "The body goes back unbound.");
        Assert.That(view.IsBound, Is.False);
        Assert.That(_views.PooledCount, Is.EqualTo(1), "And it goes back to the pool, not to the bin.");
    }

    [Test]
    public void Impacted_UnknownId_IsIgnored()
    {
        _views = Census(prewarm: 1);

        // Not an error, for the reason EnemyViews.OnDespawned ignores an unknown despawn: an id may
        // legitimately never have had a body made for it. A throw here would end the run over a
        // bolt nobody could see anyway.
        Assert.That(() => Impact(id: 99, position: Vector3.Zero), Throws.Nothing);

        Assert.That(_views.Count, Is.Zero);
    }

    [Test]
    public void Impacted_ReturnsTheBodyForReuse()
    {
        _views = Census(prewarm: 1);

        Fire(id: 1, origin: Vector3.Zero, target: new Vector3(6f, 0f, 0f), flightTime: 1f);

        Assert.That(_views.TryGet(1, out ProjectileView first), Is.True);

        Impact(id: 1, position: new Vector3(6f, 0f, 0f));

        Fire(id: 2, origin: new Vector3(2f, 0f, 2f), target: new Vector3(8f, 0f, 2f), flightTime: 1f);

        Assert.That(_views.TryGet(2, out ProjectileView second), Is.True);

        Assert.That(
            second,
            Is.SameAs(first),
            "The same body is rented again rather than a second one created — the whole point of "
                + "pooling a thing a Spitter throws three times a stage.");

        Assert.That(second.Id, Is.EqualTo(2), "Rebound to the new id, not still answering the old one.");
        Assert.That(_views.PooledCount, Is.Zero, "Nothing is left waiting once the one body is in service.");
    }

    [Test]
    public void Step_MovesAlongXzLinearly()
    {
        ProjectileView view = Bound(id: 1, origin: Vector3.Zero, target: new Vector3(12f, 0f, 0f), flightTime: 1f);

        view.Step(0.5f);

        // Exactly the point core's own flight time was measured along (M2-07a), because core's hit
        // test is XZ: a picture that eased or led along the ground would show a bolt landing
        // somewhere the damage did not.
        Assert.That(view.transform.position.x, Is.EqualTo(6f).Within(1e-4f));
        Assert.That(view.transform.position.z, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void Step_ArcsOnYOnly()
    {
        ProjectileView view = Bound(id: 1, origin: Vector3.Zero, target: new Vector3(12f, 0f, 0f), flightTime: 1f);

        view.Step(0.5f);

        // 4h·t(1−t) at the midpoint is h, and both endpoints are at zero — so the hump is the whole
        // of what Y carries here. The XZ assertions repeat the row above deliberately: the claim is
        // that the arc changed nothing on the ground plane.
        Assert.That(view.transform.position.y, Is.EqualTo(ArcHeight).Within(1e-4f));
        Assert.That(view.transform.position.x, Is.EqualTo(6f).Within(1e-4f));
        Assert.That(view.transform.position.z, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void Step_YReturnsToTheTargetHeight()
    {
        ProjectileView view = Bound(
            id: 1,
            origin: new Vector3(0f, 1.4f, 0f),
            target: new Vector3(12f, 0f, 0f),
            flightTime: 1f);

        view.Step(0.5f);
        view.Step(0.5f);

        // On the target point exactly, height included — a shot fired from a muzzle above the
        // ground it is aimed at still lands on the ground.
        Assert.That(view.transform.position.x, Is.EqualTo(12f).Within(1e-4f));
        Assert.That(view.transform.position.y, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(view.transform.position.z, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void Step_ZeroFlightTime_DoesNotDivide()
    {
        // A shot fired from the point it is aimed at (M2-07a rule 3). Legal, and usually released
        // on the same frame it was rented.
        ProjectileView view = Bound(id: 1, origin: new Vector3(4f, 0f, 4f), target: new Vector3(4f, 0f, 4f), flightTime: 0f);

        Assert.That(() => view.Step(0.016f), Throws.Nothing);

        UnityEngine.Vector3 position = view.transform.position;

        Assert.That(float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z), Is.False,
            "A zero flight time is clamped to arrival, never divided by — a NaN position is a body "
                + "drawn nowhere for the rest of its life, in silence.");

        Assert.That(position.x, Is.EqualTo(4f).Within(1e-4f));
        Assert.That(position.z, Is.EqualTo(4f).Within(1e-4f));
    }

    [Test]
    public void Step_DoesNotOverrun()
    {
        ProjectileView view = Bound(id: 1, origin: Vector3.Zero, target: new Vector3(12f, 0f, 0f), flightTime: 1f);

        view.Step(1f);
        view.Step(0.5f);

        // The impact event is what returns the body, and it can be a frame late. A bolt that kept
        // flying would sail past something core has already said it hit.
        Assert.That(view.transform.position.x, Is.EqualTo(12f).Within(1e-4f));
        Assert.That(view.transform.position.y, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void Step_FacesAlongTravel()
    {
        ProjectileView view = Bound(id: 1, origin: Vector3.Zero, target: new Vector3(12f, 0f, 0f), flightTime: 1f);

        view.Step(0.1f);

        UnityEngine.Vector3 forward = view.transform.forward;

        Assert.That(forward.x, Is.GreaterThan(0.5f), "A bolt travelling +X points +X-ish rather than at the sky.");

        Quaternion held = view.transform.rotation;

        // A step that moves the body nowhere leaves the rotation alone — EnemyView.Face's rule, and
        // what stops LookRotation being handed a zero vector at the top of the arc's last frame.
        view.Step(0f);

        Assert.That(view.transform.rotation, Is.EqualTo(held), "A zero-length step must not re-aim the body.");
    }

    [Test]
    public void Step_IgnoresUnboundBodies()
    {
        _views = Census(prewarm: 1);

        Fire(id: 1, origin: Vector3.Zero, target: new Vector3(12f, 0f, 0f), flightTime: 1f);

        Assert.That(_views.TryGet(1, out ProjectileView view), Is.True);

        Impact(id: 1, position: new Vector3(12f, 0f, 0f));

        UnityEngine.Vector3 resting = view.transform.position;

        // A body in the pool is standing in for nobody. Stepping it would walk a corpse of a bolt
        // along a flight that ended, invisibly, for as long as the run lasted.
        Assert.That(() => view.Step(0.5f), Throws.Nothing);

        Assert.That(view.transform.position, Is.EqualTo(resting));
    }

    [Test]
    public void Step_AdvancesEveryBoltByTheGivenDt()
    {
        _views = Census(prewarm: 2);

        Fire(id: 1, origin: Vector3.Zero, target: new Vector3(12f, 0f, 0f), flightTime: 1f);
        Fire(id: 2, origin: Vector3.Zero, target: new Vector3(0f, 0f, 12f), flightTime: 1f);

        _views.Step(0.25f);

        Assert.That(_views.TryGet(1, out ProjectileView first), Is.True);
        Assert.That(_views.TryGet(2, out ProjectileView second), Is.True);

        Assert.That(first.transform.position.x, Is.EqualTo(3f).Within(1e-4f));
        Assert.That(second.transform.position.z, Is.EqualTo(3f).Within(1e-4f));
    }

    /// <summary>
    /// Rule 3, as far as an EditMode fixture can reach it: the step a bolt advances by is the one it
    /// was handed, and there is no second clock on the body that could disagree with it.
    /// </summary>
    /// <remarks>
    /// The spec's row names <c>RunTicker</c>, which has no fixture in this project and gains none
    /// here — its frame order is ledger row 8's other half and stays with M2-11b (rule 12). What is
    /// checkable without one is both halves of the actual claim: <c>Step</c> integrates the argument
    /// rather than a frame time, and <see cref="ProjectileView"/> declares no Unity message that
    /// could advance the flight behind the ticker's back. The remaining line — that the ticker
    /// passes <c>snapshot.Dt</c> — is one call site, read in review and watched in manual step 1.
    /// </remarks>
    [Test]
    public void Ticker_StepsWithSnapshotDt()
    {
        ProjectileView view = Bound(id: 1, origin: Vector3.Zero, target: new Vector3(10f, 0f, 0f), flightTime: 1f);

        // Deliberately not a plausible frame time: if anything here read Time.deltaTime instead of
        // the argument, the body would not land on 2 m.
        view.Step(0.2f);

        Assert.That(view.transform.position.x, Is.EqualTo(2f).Within(1e-4f));

        foreach (string message in new[] { "Update", "LateUpdate", "FixedUpdate" })
        {
            Assert.That(
                typeof(ProjectileView).GetMethod(
                    message,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                Is.Null,
                $"ProjectileView declares {message}. A view stepped by RunTicker must own no frame "
                    + "loop of its own, or the bolt advances on the wall clock while core advances "
                    + "on the clamped one — and on a hitching frame the picture lands ahead of the "
                    + "damage (AR §18.1, §18.2).");
        }
    }

    [Test]
    public void Dispose_UnsubscribesAndDestroysEverything()
    {
        _views = Census(prewarm: 1);

        Fire(id: 1, origin: Vector3.Zero, target: new Vector3(6f, 0f, 0f), flightTime: 1f);
        Fire(id: 2, origin: Vector3.Zero, target: new Vector3(0f, 0f, 6f), flightTime: 1f);

        Assert.That(_views.Count, Is.EqualTo(2));

        _views.Dispose();

        Assert.That(_views.Count, Is.Zero, "Disposing drops the census with the bodies it indexed.");

        // The subscriptions are gone, so a shot fired by a run this object has outlived reaches
        // nothing — and cannot ask a disposed pool for a body.
        Assert.That(
            () => Fire(id: 3, origin: Vector3.Zero, target: new Vector3(6f, 0f, 0f), flightTime: 1f),
            Throws.Nothing);

        Assert.That(_views.Count, Is.Zero);

        // Idempotent, like EnemyViews' — a double dispose is a disposal-order question nobody
        // should have to answer.
        Assert.That(() => _views.Dispose(), Throws.Nothing);
    }

    [Test]
    public void Prewarm_InstantiatesUpFront()
    {
        _views = Census(prewarm: 8);

        Assert.That(_views.PooledCount, Is.EqualTo(8), "The whole prewarm exists before the run starts.");
        Assert.That(_views.Count, Is.Zero, "And none of it is drawn until a shot is fired.");

        for (int i = 1; i <= 8; i++)
        {
            Fire(id: i, origin: Vector3.Zero, target: new Vector3(6f, 0f, 0f), flightTime: 1f);
        }

        Assert.That(_views.Count, Is.EqualTo(8));
        Assert.That(
            _views.PooledCount,
            Is.Zero,
            "The first eight rentals came out of the prewarm — nothing was instantiated on a frame "
                + "a Spitter was releasing (AR §14, GD §11.3).");
    }

    [Test]
    public void Constructor_NullDependency_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProjectileViews(null, _prefab, null, _hub));
        Assert.Throws<ArgumentNullException>(() => new ProjectileViews(_container, null, null, _hub));
        Assert.Throws<ArgumentNullException>(() => new ProjectileViews(_container, _prefab, null, null));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProjectileViews(_container, _prefab, null, _hub, prewarm: -1));
    }

    [Test]
    public void Bind_InvalidArgument_Throws()
    {
        ProjectileView view = _prefabObject.GetComponent<ProjectileView>();

        // Ids are issued from 1, so a zero or negative one means the caller invented it — and a
        // body bound to Unbound would be a bolt the census could never find again to release.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => view.Bind(ProjectileView.Unbound, UnityEngine.Vector3.zero, UnityEngine.Vector3.one, 1f));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => view.Bind(-1, UnityEngine.Vector3.zero, UnityEngine.Vector3.one, 1f));

        // Every float door gets a non-finite row (AR §18.3). A NaN flight time makes every progress
        // reading NaN, which no clamp recovers from and nothing reports.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => view.Bind(1, UnityEngine.Vector3.zero, UnityEngine.Vector3.one, float.NaN));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => view.Bind(1, UnityEngine.Vector3.zero, UnityEngine.Vector3.one, float.PositiveInfinity));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => view.Bind(1, UnityEngine.Vector3.zero, UnityEngine.Vector3.one, -1f));
    }

    [Test]
    public void Step_NonFiniteDt_MovesNothing()
    {
        ProjectileView view = Bound(id: 1, origin: Vector3.Zero, target: new Vector3(12f, 0f, 0f), flightTime: 1f);

        view.Step(0.25f);

        UnityEngine.Vector3 held = view.transform.position;

        // Refused rather than thrown on: a bad frame time is the run's problem to report, and the
        // body's job is to not become a permanent NaN because of it.
        view.Step(float.NaN);
        view.Step(float.PositiveInfinity);
        view.Step(-0.5f);

        Assert.That(view.transform.position, Is.EqualTo(held));
    }

    /// <summary>A census over the fixture's prefab, hub and container.</summary>
    private ProjectileViews Census(int prewarm) =>
        new ProjectileViews(_container, _prefab, null, _hub, prewarm);

    /// <summary>
    /// One body, bound by hand rather than through the census — the arc rows are about the flight
    /// and have no opinion about who rented it.
    /// </summary>
    private ProjectileView Bound(int id, Vector3 origin, Vector3 target, float flightTime)
    {
        ProjectileView view = _prefabObject.GetComponent<ProjectileView>();

        view.Bind(id, origin.ToUnity(), target.ToUnity(), flightTime);

        return view;
    }

    private void Fire(int id, Vector3 origin, Vector3 target, float flightTime) =>
        _hub.Publish(new ProjectileFired(id, Spitter, sourceId: 7, origin, target, flightTime));

    private void Impact(int id, Vector3 position) =>
        _hub.Publish(new ProjectileImpacted(id, position, hitPlayer: false));
}
