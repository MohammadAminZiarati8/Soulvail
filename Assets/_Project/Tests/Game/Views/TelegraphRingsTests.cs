using System;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// The census that turns a telegraph and a blast into circles on the floor. Four things can be wrong
/// here and none of them throws: a ring that never appears, a ring that never leaves, a ring that
/// counts down at the wrong rate, and a ring whose radius is not the one core tested.
/// </summary>
/// <remarks>
/// <para>
/// The prefab is a scene object rather than <c>VFX_TelegraphRing.prefab</c>, for
/// <c>ProjectileViewsTests</c>' reason: <see cref="IObjectResolver.Instantiate"/> takes a
/// <see cref="Component"/> rather than an asset, and building the body here keeps the fixture from
/// depending on how the art is dressed. The prefab's own wiring is what the manual steps check —
/// and what <c>RunScope</c> refuses at composition time.
/// </para>
/// <para>
/// Rings are read back through the parent they are pooled under rather than through an accessor on
/// the census, because there is no accessor and there should not be: nothing in the game ever asks
/// for a ring back (rule 3). An active child of the decal root is a ring in service, which is the
/// same fact the pool's own <c>SetActive</c> is keeping.
/// </para>
/// <para>
/// <c>Awake</c> never runs in EditMode (Traps §5). Nothing here needs it to: the rest pose, rotation
/// and scale a ring is returned to are field initialisers describing a body authored at the origin at
/// unit scale, which is both what this fixture builds and what the real prefab is.
/// </para>
/// <para>
/// Every object the fixture or the pool creates is destroyed in the teardown. An EditMode test that
/// leaks a GameObject leaks it into every test that runs after it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class TelegraphRingsTests
{
    private static readonly ContentId Husk = new ContentId("enemy.husk");
    private static readonly ContentId Bloater = new ContentId("enemy.bloater");

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// The director's telegraph, duplicated here rather than read through the class under test — the
    /// claim in several rows below is that a spawn ring lasts <em>exactly</em> as long as the
    /// telegraph core is running, and reading it from the same constant on both sides would make that
    /// true by construction.
    /// </summary>
    private const float TelegraphSeconds = 0.8f;

    /// <inheritdoc cref="TelegraphSeconds" />
    private const float LingerSeconds = 0.35f;

    /// <summary>Half of <c>SpawnDirector.MinSpawnSeparation</c>, duplicated for the same reason.</summary>
    private const float SpawnRadius = 1f;

    private GameObject _prefabObject;
    private TelegraphRingView _prefab;
    private GameObject _parentObject;
    private IObjectResolver _container;
    private DomainEventHub _hub;
    private TelegraphRings _rings;

    [SetUp]
    public void CreateCensus()
    {
        _prefabObject = new GameObject("TelegraphRingPrefab");
        _prefab = _prefabObject.AddComponent<TelegraphRingView>();

        // Dressed the way the real prefab is, so the colour and alpha writes are exercised rather
        // than skipped — ReticleViewTests' idiom for a serialized field a fixture has to fill.
        typeof(TelegraphRingView)
            .GetField("_quad", Private)
            .SetValue(_prefab, _prefabObject.AddComponent<MeshRenderer>());

        _parentObject = new GameObject("Decals");

        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();
    }

    [TearDown]
    public void DestroyCensus()
    {
        _rings?.Dispose();
        _rings = null;

        _hub?.Dispose();
        _hub = null;

        _container?.Dispose();
        _container = null;

        if (_prefabObject != null)
        {
            Object.DestroyImmediate(_prefabObject);
        }

        if (_parentObject != null)
        {
            Object.DestroyImmediate(_parentObject);
        }
    }

    [Test]
    public void Telegraphed_RentsAFillingRing()
    {
        _rings = Census(prewarm: 1);

        Telegraph(new Vector3(4f, 0f, 4f));

        Assert.That(_rings.Count, Is.EqualTo(1));

        TelegraphRingView ring = Single();

        // Where the body will appear, not near it. M2-05 rule 7's promise is that the ring and the
        // body are the same place, and a ring drawn approximately would make the 6 m clearance a
        // statement about a circle nobody could see.
        Assert.That(ring.transform.position.x, Is.EqualTo(4f).Within(1e-4f));
        Assert.That(ring.transform.position.z, Is.EqualTo(4f).Within(1e-4f));

        Assert.That(ring.IsLive, Is.True);
        Assert.That(ring.Radius, Is.EqualTo(SpawnRadius).Within(1e-4f));

        // Filling: the disc is empty on the frame it appears and the fill is what counts down.
        Assert.That(ring.Fill, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(ring.Alpha, Is.EqualTo(1f).Within(1e-4f));

        // The duration is the director's, read off the far end: at half the telegraph the fill is
        // half, which is only true if the ring was given 0.8 s and nothing else.
        ring.Step(TelegraphSeconds * 0.5f);

        Assert.That(ring.Fill, Is.EqualTo(0.5f).Within(1e-3f));
    }

    [Test]
    public void Telegraphed_ReturnsItselfWhenTheTimeIsUp()
    {
        _rings = Census(prewarm: 1);

        Telegraph(new Vector3(4f, 0f, 4f));

        _rings.Step(TelegraphSeconds - 0.01f);

        Assert.That(_rings.Count, Is.EqualTo(1), "A ring inside its telegraph is still on the floor.");
        Assert.That(_rings.PooledCount, Is.Zero);

        _rings.Step(0.02f);

        Assert.That(_rings.Count, Is.Zero, "And it takes itself off when the time is up.");
        Assert.That(_rings.PooledCount, Is.EqualTo(1), "Back to the pool, not to the bin.");
    }

    [Test]
    public void Telegraphed_FillsLinearly()
    {
        _rings = Census(prewarm: 1);

        Telegraph(Vector3.Zero);

        _rings.Step(TelegraphSeconds * 0.5f);

        TelegraphRingView ring = Single();

        // Linear, because the fill *is* the clock: eased, the ring would be lying about how much
        // time was left at every moment except the two ends (GD §7.1).
        Assert.That(ring.Fill, Is.EqualTo(0.5f).Within(0.01f));

        // And the drawn size is the fill, which is the half of the claim a player can see. A quad is
        // one unit across, so the diameter is twice the radius reached so far.
        Assert.That(ring.transform.localScale.x, Is.EqualTo(SpawnRadius).Within(0.02f));
    }

    [Test]
    public void Telegraphed_HasNoCancelPath()
    {
        _rings = Census(prewarm: 1);

        Telegraph(Vector3.Zero);

        // M2-05 rule 7: a telegraph is a promise the director cannot withdraw, so there is no way to
        // end a ring early and no id to end one by. Asserted as an *absence*, because that is what
        // the rule looks like in code — and because the natural thing for a later reader to add is a
        // Cancel for symmetry with EnemyViews' despawn, which would quietly make the ring a warning
        // instead of a promise.
        foreach (string forbidden in new[] { "Cancel", "Clear", "Remove", "Release", "Unbind", "Abort" })
        {
            Assert.That(
                typeof(TelegraphRings).GetMethod(forbidden, BindingFlags.Instance | BindingFlags.Public),
                Is.Null,
                $"TelegraphRings exposes {forbidden}. A ring goes out of service by running out and "
                    + "by nothing else (M2-05 rule 7).");

            Assert.That(
                typeof(TelegraphRingView).GetMethod(forbidden, BindingFlags.Instance | BindingFlags.Public),
                Is.Null,
                $"TelegraphRingView exposes {forbidden}. The same rule, one layer down.");
        }

        Assert.That(_rings.Count, Is.EqualTo(1), "And the ring is still there after all that asking.");
    }

    [Test]
    public void Exploded_RentsAFadingRing()
    {
        _rings = Census(prewarm: 1);

        Explode(Vector3.Zero, radius: 3f);

        Assert.That(_rings.Count, Is.EqualTo(1));

        TelegraphRingView ring = Single();

        // Exactly three metres, because that is the circle EnemySystem tested the player against
        // (rule 8). A ring drawn a little larger to look better would make "I was outside it" false
        // about the only circle in the game the player is asked to judge under pressure.
        Assert.That(ring.Radius, Is.EqualTo(3f).Within(1e-4f));

        // Full size on its first frame and not filling: the damage has already happened, so there is
        // nothing left to count down to.
        Assert.That(ring.Fill, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(ring.transform.localScale.x, Is.EqualTo(6f).Within(1e-3f));
    }

    [Test]
    public void Exploded_LingersThenReturns()
    {
        _rings = Census(prewarm: 1);

        Explode(Vector3.Zero, radius: 3f);

        _rings.Step(LingerSeconds - 0.01f);

        Assert.That(_rings.Count, Is.EqualTo(1));

        _rings.Step(0.02f);

        Assert.That(_rings.Count, Is.Zero);
        Assert.That(_rings.PooledCount, Is.EqualTo(1));
    }

    [Test]
    public void Exploded_FadesFromFull()
    {
        _rings = Census(prewarm: 1);

        Explode(Vector3.Zero, radius: 3f);

        TelegraphRingView ring = Single();

        // A frame that did not advance the simulation must not advance the picture of it either.
        _rings.Step(0f);

        Assert.That(ring.Alpha, Is.EqualTo(1f).Within(1e-4f), "The flash starts at full brightness.");

        _rings.Step(LingerSeconds * 0.5f);

        Assert.That(ring.Alpha, Is.EqualTo(0.5f).Within(0.02f), "And it is half gone half way through.");

        // The size never moves, which is the other half of "fading, not filling": a blast ring that
        // shrank as it faded would be drawing a smaller circle than the one that hurt.
        Assert.That(ring.transform.localScale.x, Is.EqualTo(6f).Within(1e-3f));
    }

    [Test]
    public void Rings_CoexistAndAreIndependent()
    {
        _rings = Census(prewarm: 2);

        Telegraph(new Vector3(4f, 0f, 0f));
        Explode(new Vector3(-4f, 0f, 0f), radius: 3f);

        Assert.That(_rings.Count, Is.EqualTo(2));

        // Told apart by their radii rather than by their order, because the order is the pool's
        // business and no rule here depends on it.
        TelegraphRingView telegraph = Live(radius: SpawnRadius);
        TelegraphRingView blast = Live(radius: 3f);

        _rings.Step(0.4f);

        // One step, two different lifetimes: 0.4 s is half of the telegraph and past the whole of the
        // linger, so the same call must leave one ring half-drawn and take the other away.
        Assert.That(telegraph.IsLive, Is.True);
        Assert.That(telegraph.Fill, Is.EqualTo(0.5f).Within(0.01f));

        Assert.That(blast.IsLive, Is.False);
        Assert.That(_rings.Count, Is.EqualTo(1));
        Assert.That(_rings.PooledCount, Is.EqualTo(1));
    }

    [Test]
    public void Rings_ElevenAtOnce()
    {
        _rings = Census(prewarm: 11);

        // The wave cap M2-04 priced (28) is not the ring cap: at SpawnInterval spacing a wave has
        // about three telegraphs up at a time. Eleven is the readability question GD §11.3 asks —
        // whether overlapping additive quads still read as separate countdowns — and it is a device
        // step. What is checkable here is that eleven is eleven bodies and not twelve.
        for (int i = 0; i < 11; i++)
        {
            Telegraph(new Vector3(i * 3f, 0f, 0f));
        }

        Assert.That(_rings.Count, Is.EqualTo(11));
        Assert.That(_rings.PooledCount, Is.Zero, "The whole prewarm is in service.");

        Assert.That(
            _rings.Count + _rings.PooledCount,
            Is.EqualTo(11),
            "Nothing was instantiated beyond the prewarm — a wave's worth of rings must not each "
                + "cost an Instantiate on the frame the wave is announced (AR §14, GD §11.3).");
    }

    /// <summary>
    /// Rule 4, as far as an EditMode fixture can reach it: a ring advances by the step it is handed,
    /// and there is no second clock on the body that could disagree with it.
    /// </summary>
    /// <remarks>
    /// The remaining line — that <c>RunTicker</c> passes <c>snapshot.Dt</c> — is one call site, read
    /// in review and pinned in PlayMode by <c>FrameOrderTests</c>, which now builds a census of these
    /// as the ticker's seventeenth argument.
    /// </remarks>
    [Test]
    public void Ticker_StepsWithSnapshotDt()
    {
        _rings = Census(prewarm: 1);

        Telegraph(Vector3.Zero);

        TelegraphRingView ring = Single();

        // Deliberately not a plausible frame time: anything reading Time.deltaTime instead of the
        // argument would not land the fill on a quarter.
        _rings.Step(TelegraphSeconds * 0.25f);

        Assert.That(ring.Fill, Is.EqualTo(0.25f).Within(1e-3f));

        foreach (string message in new[] { "Update", "LateUpdate", "FixedUpdate" })
        {
            Assert.That(
                typeof(TelegraphRingView).GetMethod(
                    message,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                Is.Null,
                $"TelegraphRingView declares {message}. A view stepped by RunTicker must own no "
                    + "frame loop of its own, or the ring fills on the wall clock while core counts "
                    + "the telegraph down on the clamped one — and then the ring finishes before or "
                    + "after the body it promised (AR §18.1, §18.2).");
        }
    }

    [Test]
    public void Ring_LiesFlat()
    {
        _rings = Census(prewarm: 1);

        Telegraph(new Vector3(4f, 0f, 4f));

        TelegraphRingView ring = Single();

        // A quad's face is its local −Z, so a decal lying on the floor has that pointing at the sky.
        Assert.That(
            UnityEngine.Vector3.Dot(-ring.transform.forward, UnityEngine.Vector3.up),
            Is.EqualTo(1f).Within(1e-3f),
            "The ring must lie on the arena floor, not stand up in it.");

        // It cannot billboard, and this is why rather than a promise that it does not: the camera is
        // fixed at 57° (GD §5.1), so there is nothing to turn towards — and a body with no camera
        // and no frame loop has no way to turn anyway. The frame-loop half is asserted above.
        foreach (FieldInfo field in typeof(TelegraphRingView).GetFields(
            Private | BindingFlags.Static | BindingFlags.Public))
        {
            Assert.That(
                typeof(Camera).IsAssignableFrom(field.FieldType),
                Is.False,
                $"TelegraphRingView holds a Camera in {field.Name}. A ground decal under a fixed "
                    + "camera has no reason to know one exists (rule 9).");
        }
    }

    [Test]
    public void Ring_IsDangerColoured()
    {
        _rings = Census(prewarm: 2);

        Telegraph(new Vector3(4f, 0f, 0f));
        Explode(new Vector3(-4f, 0f, 0f), radius: 3f);

        // Both, and the same: GD §16.4 reserves saturated red-orange for danger and nothing else,
        // and a spawn and a blast are both danger. It is read from the one place the palette lives
        // rather than copied, which is also what keeps cyan meaning the player (M2-12a).
        foreach (TelegraphRingView ring in Live())
        {
            Assert.That(ring.Colour.r, Is.EqualTo(ThreatArrows.Danger.r).Within(1e-3f));
            Assert.That(ring.Colour.g, Is.EqualTo(ThreatArrows.Danger.g).Within(1e-3f));
            Assert.That(ring.Colour.b, Is.EqualTo(ThreatArrows.Danger.b).Within(1e-3f));
        }
    }

    [Test]
    public void Ring_ComesBackClean()
    {
        _rings = Census(prewarm: 1);

        Explode(new Vector3(9f, 0f, 9f), radius: 3f);

        TelegraphRingView first = Single();

        _rings.Step(LingerSeconds + 0.01f);

        // In the pool: standing in for nothing, at no size, invisible, and back where it was made.
        Assert.That(first.IsLive, Is.False);
        Assert.That(first.Radius, Is.Zero);
        Assert.That(first.Fill, Is.Zero);
        Assert.That(first.Alpha, Is.Zero);
        Assert.That(first.transform.position, Is.EqualTo(UnityEngine.Vector3.zero));
        Assert.That(first.transform.localScale, Is.EqualTo(UnityEngine.Vector3.one));

        Telegraph(new Vector3(-2f, 0f, 5f));

        TelegraphRingView second = Single();

        Assert.That(second, Is.SameAs(first), "The same body is rented again rather than a second made.");

        // And it arrives as a fresh one would. Everything below is a thing the previous life left
        // set: a radius of 3, a fill of 1, and a position nine metres away (AR §18.4).
        Assert.That(second.Radius, Is.EqualTo(SpawnRadius).Within(1e-4f));
        Assert.That(second.Fill, Is.Zero, "A spawn ring starts empty, however full the last one ended.");
        Assert.That(second.Alpha, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(second.transform.position.x, Is.EqualTo(-2f).Within(1e-4f));
        Assert.That(second.transform.position.z, Is.EqualTo(5f).Within(1e-4f));
    }

    [Test]
    public void Prewarm_InstantiatesUpFront()
    {
        _rings = Census(prewarm: 8);

        Assert.That(_rings.PooledCount, Is.EqualTo(8), "The whole prewarm exists before the run starts.");
        Assert.That(_rings.Count, Is.Zero, "And none of it is drawn until something is announced.");

        for (int i = 0; i < 8; i++)
        {
            Telegraph(new Vector3(i * 3f, 0f, 0f));
        }

        Assert.That(_rings.Count, Is.EqualTo(8));
        Assert.That(
            _rings.PooledCount,
            Is.Zero,
            "The first eight rentals came out of the prewarm — nothing was instantiated on a frame a "
                + "wave was being announced (AR §14, GD §11.3).");
    }

    [Test]
    public void Step_AllocatesNothing()
    {
        _rings = Census(prewarm: 11);

        for (int i = 0; i < 11; i++)
        {
            Telegraph(new Vector3(i * 3f, 0f, 0f));
        }

        // A step small enough that all eleven are still live after the whole measured run — 10 001
        // iterations is 0.1 s, well inside the 0.8 s telegraph. That matters: a step that expired
        // them would spend most of the measurement walking an empty list and would not touch the
        // scale and colour writes this row is about.
        AllocationAssert.None(() => _rings.Step(1e-5f));

        Assert.That(_rings.Count, Is.EqualTo(11), "All eleven survived the measurement.");
    }

    [Test]
    public void Dispose_UnsubscribesAndDestroys()
    {
        _rings = Census(prewarm: 2);

        Telegraph(new Vector3(4f, 0f, 0f));
        Explode(new Vector3(-4f, 0f, 0f), radius: 3f);

        Assert.That(_rings.Count, Is.EqualTo(2));

        _rings.Dispose();

        Assert.That(_rings.Count, Is.Zero, "Disposing drops the census with the bodies it held.");

        // The subscriptions are gone, so a telegraph published into a run this object has outlived
        // reaches nothing — and cannot ask a disposed pool for a body.
        Assert.That(() => Telegraph(Vector3.Zero), Throws.Nothing);
        Assert.That(() => Explode(Vector3.Zero, radius: 3f), Throws.Nothing);

        Assert.That(_rings.Count, Is.Zero);

        // Idempotent, like ProjectileViews' — a double dispose is a disposal-order question nobody
        // should have to answer.
        Assert.That(() => _rings.Dispose(), Throws.Nothing);
    }

    [Test]
    public void Constructor_NullDependency_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TelegraphRings(null, _prefab, null, _hub));
        Assert.Throws<ArgumentNullException>(() => new TelegraphRings(_container, null, null, _hub));
        Assert.Throws<ArgumentNullException>(() => new TelegraphRings(_container, _prefab, null, null));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TelegraphRings(_container, _prefab, null, _hub, prewarm: -1));
    }

    [Test]
    public void Bind_InvalidArgument_Throws()
    {
        // Every float door gets a non-finite row (AR §18.3). Neither of these has a meaningful zero
        // either: a ring of no size is a promise nobody can see, and one of no length is a telegraph
        // nobody can read — and a zero duration is also the division Progress refuses to do.
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _prefab.Bind(UnityEngine.Vector3.zero, bad, 0.8f, true, ThreatArrows.Danger),
                $"A radius of {bad} must be refused at the door.");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => _prefab.Bind(UnityEngine.Vector3.zero, 1f, bad, true, ThreatArrows.Danger),
                $"A duration of {bad} must be refused at the door.");
        }
    }

    [Test]
    public void Step_NonFiniteDt_ChangesNothing()
    {
        _rings = Census(prewarm: 1);

        Telegraph(Vector3.Zero);

        _rings.Step(0.2f);

        TelegraphRingView ring = Single();

        float held = ring.Fill;

        // Refused rather than thrown on: a bad frame time is the run's problem to report, and a
        // ring's job is to not become a permanent NaN because of it — which no later comparison
        // would be true about and nothing would log.
        _rings.Step(float.NaN);
        _rings.Step(float.PositiveInfinity);
        _rings.Step(-0.5f);

        Assert.That(ring.Fill, Is.EqualTo(held).Within(1e-6f));
        Assert.That(ring.IsLive, Is.True, "And it is still on the floor, not silently retired.");
        Assert.That(_rings.Count, Is.EqualTo(1));
    }

    [Test]
    public void SpawnRingRadius_IsHalfTheDirectorsSeparation()
    {
        // Rule 8 asks for the circle core tested, and for a spawn that is the disc the director
        // reserved: MinSpawnSeparation is checked between claimed points, so half of it is the
        // largest ring two simultaneous spawns can each own without overlapping. Derived rather than
        // written as 1, so the two cannot drift apart — this row is what says they have not.
        Assert.That(
            TelegraphRings.SpawnRingRadius,
            Is.EqualTo(SpawnDirector.MinSpawnSeparation / 2f).Within(1e-6f));

        Assert.That(TelegraphRings.SpawnRingRadius, Is.EqualTo(SpawnRadius).Within(1e-6f));
    }

    /// <summary>A census over the fixture's prefab, parent, hub and container.</summary>
    private TelegraphRings Census(int prewarm) =>
        new TelegraphRings(_container, _prefab, _parentObject.transform, _hub, prewarm);

    /// <summary>
    /// Every ring in service, read off the decal root. An active child is a rented body and an
    /// inactive one is a pooled body, which is the pool's own bookkeeping rather than a second copy
    /// of it.
    /// </summary>
    private TelegraphRingView[] Live() =>
        _parentObject.GetComponentsInChildren<TelegraphRingView>();

    /// <summary>The one ring in service, asserting that there is exactly one.</summary>
    private TelegraphRingView Single()
    {
        TelegraphRingView[] live = Live();

        Assert.That(live.Length, Is.EqualTo(1), "Expected exactly one ring on the floor.");

        return live[0];
    }

    /// <summary>The ring in service with the given radius, asserting that exactly one has it.</summary>
    private TelegraphRingView Live(float radius)
    {
        TelegraphRingView found = null;

        foreach (TelegraphRingView ring in Live())
        {
            if (Mathf.Abs(ring.Radius - radius) < 1e-3f)
            {
                Assert.That(found, Is.Null, $"Two rings of radius {radius} are in service.");

                found = ring;
            }
        }

        Assert.That(found, Is.Not.Null, $"No ring of radius {radius} is in service.");

        return found;
    }

    private void Telegraph(Vector3 position) =>
        _hub.Publish(new SpawnTelegraphed(Husk, position, firesAt: 12f));

    private void Explode(Vector3 centre, float radius) =>
        _hub.Publish(new EnemyExploded(5, Bloater, centre, radius, hitPlayer: false));
}
