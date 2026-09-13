using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// The census of things the player cannot see, and where on the border of a phone screen each one
/// is pointed at from. Four things can be wrong here and none of them throws: an arrow for something
/// that is on screen, no arrow for something that is about to shoot you, an arrow for a threat
/// behind the camera drawn on the wrong edge, and an arrow under a thumb.
/// </summary>
/// <remarks>
/// <para>
/// The camera is a bare scene camera looking down <c>+Z</c> from the origin rather than the arena's
/// pitched follow camera. Every claim in this fixture is about a projection and a border rect, and
/// the 57° pitch would make each row's arithmetic a second puzzle on top of the one being asserted.
/// The pitch's own consequence — that "behind the camera" is where the player just came from — has
/// its own row.
/// </para>
/// <para>
/// The border is a 1000 × 600 rect, which is not a phone's aspect and is deliberately not: an
/// implementation that treated the two axes as interchangeable passes on a square and fails here.
/// </para>
/// <para>
/// <c>Awake</c> never runs in EditMode (Traps §5), so every body carries its field initialisers and
/// no <c>Canvas</c> exists — which is exactly the fixture the allocation row needs. Everything the
/// fixture or the class creates is destroyed in the teardown; an EditMode test that leaks a
/// GameObject leaks it into every test that runs after it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ThreatArrowsTests
{
    private static readonly ContentId Husk = new ContentId("enemy.husk");

    /// <summary>The border rect's size, in UI units. Deliberately not square — see the remarks.</summary>
    private const float BorderWidth = 1000f;
    private const float BorderHeight = 600f;

    private GameObject _cameraObject;
    private Camera _camera;

    private GameObject _enemyPrefabObject;
    private EnemyView _enemyPrefab;

    private GameObject _arrowPrefabObject;
    private RectTransform _arrowPrefab;

    private GameObject _parentObject;
    private RectTransform _parent;

    private GameObject _playerObject;
    private PlayerView _player;

    private IObjectResolver _container;
    private DomainEventHub _hub;
    private EnemyViews _views;
    private ThreatArrows _arrows;

    private float _now;

    [SetUp]
    public void CreateWorld()
    {
        _cameraObject = new GameObject("Camera");
        _camera = _cameraObject.AddComponent<Camera>();

        // Orthographic-free but plainly aimed: at the origin, looking along +Z, so "behind" is −Z
        // and "left" is −X with no mental arithmetic anywhere in the rows.
        _cameraObject.transform.position = UnityEngine.Vector3.zero;
        _cameraObject.transform.rotation = Quaternion.identity;
        _camera.fieldOfView = 60f;
        _camera.nearClipPlane = 0.1f;
        _camera.farClipPlane = 200f;

        // Pinned rather than inherited. A camera with no target texture takes its aspect from the
        // Game view, so every projection row here would depend on how the Editor window happened to
        // be sized — the same class of trap as reading feel off the wrong aspect (Traps §5).
        _camera.aspect = BorderWidth / BorderHeight;

        _enemyPrefabObject = new GameObject("EnemyPrefab");
        _enemyPrefab = _enemyPrefabObject.AddComponent<EnemyView>();

        _arrowPrefabObject = new GameObject("ArrowPrefab", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _arrowPrefab = (RectTransform)_arrowPrefabObject.transform;

        _parentObject = new GameObject("Arrows", typeof(RectTransform));
        _parent = (RectTransform)_parentObject.transform;
        _parent.sizeDelta = new UnityEngine.Vector2(BorderWidth, BorderHeight);

        _playerObject = new GameObject("Player");
        _player = _playerObject.AddComponent<PlayerView>();

        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();
        _views = new EnemyViews(
            _container,
            _enemyPrefab,
            null,
            _hub,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()),
            prewarm: 8);

        _now = 0f;
    }

    [TearDown]
    public void DestroyWorld()
    {
        _arrows?.Dispose();
        _arrows = null;

        _views?.Dispose();
        _views = null;

        _hub?.Dispose();
        _hub = null;

        _container?.Dispose();
        _container = null;

        Destroy(ref _playerObject);
        Destroy(ref _parentObject);
        Destroy(ref _arrowPrefabObject);
        Destroy(ref _enemyPrefabObject);
        Destroy(ref _cameraObject);
    }

    // ---- The census -----------------------------------------------------------------------------

    [Test]
    public void Arrow_ForAnOffScreenEnemyInRange()
    {
        _arrows = Arrows();

        Spawn(id: 1, new UnityEngine.Vector3(0f, 0f, -12f));

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.EqualTo(1), "Something that can reach the player is behind the camera.");
        Assert.That(Drawn(0).gameObject.activeSelf, Is.True);
    }

    [Test]
    public void Arrow_NoneForAnOnScreenEnemy()
    {
        _arrows = Arrows();

        Spawn(id: 1, new UnityEngine.Vector3(0f, 0f, 12f));

        _arrows.LateTick();

        // GD §12.4's rule is about damage from *outside* the frustum. An arrow on something the
        // player can already see is noise on a 6-inch screen (GD §11.3, P1).
        Assert.That(_arrows.Count, Is.Zero);
    }

    [Test]
    public void Arrow_NoneBeyondThreatRange()
    {
        _arrows = Arrows();

        Spawn(id: 1, new UnityEngine.Vector3(0f, 0f, -17f));

        _arrows.LateTick();

        Assert.That(
            _arrows.Count,
            Is.Zero,
            "Off screen and out of reach is scenery, not a threat — 16 m is the Spitter's standoff "
                + "plus margin, and every archetype in M2 is inside it.");
    }

    [Test]
    public void Arrow_AppearsAsItClosesToRange()
    {
        _arrows = Arrows();

        EnemyView body = Spawn(id: 1, new UnityEngine.Vector3(0f, 0f, -17f));

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.Zero);

        body.transform.position = new UnityEngine.Vector3(0f, 0f, -15f);

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.EqualTo(1), "It walked into reach and the border said so.");
    }

    [Test]
    public void Arrow_PointsAtTheThreat()
    {
        _arrows = Arrows();

        // Off to the left and slightly in front, so it is off screen through the side of the
        // frustum rather than behind it — the ordinary case, and the one the projection handles.
        Spawn(id: 1, new UnityEngine.Vector3(-14f, 0f, 4f));

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.EqualTo(1));

        RectTransform arrow = Drawn(0);

        Assert.That(
            arrow.anchoredPosition.x,
            Is.LessThan(0f),
            "A threat to the left is pointed at from the left half of the border.");

        // The prefab points along its own +Y, so its up vector after the rotation is the direction
        // the arrow is aimed in.
        UnityEngine.Vector3 up = arrow.localRotation * UnityEngine.Vector3.up;

        Assert.That(up.x, Is.LessThan(-0.5f), "And it is rotated towards it, not merely parked there.");
    }

    [Test]
    public void Arrow_BehindTheCameraIsNotMirrored()
    {
        _arrows = Arrows();

        Spawn(id: 1, new UnityEngine.Vector3(0f, 0f, -10f));

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.EqualTo(1));

        // WorldToViewportPoint answers for a negative depth as readily as for a positive one, and
        // the point it returns is reflected through the centre. Taken at face value, a threat
        // directly behind the player is pointed at through the *top* of the screen — the classic
        // projection sign bug, and the one thing about this class that cannot be seen in the
        // Editor without standing in exactly the wrong place.
        Assert.That(
            Drawn(0).anchoredPosition.y,
            Is.LessThan(0f),
            "Behind the camera is behind the player, which on a top-down frame is the bottom edge.");
    }

    // ---- The border -----------------------------------------------------------------------------

    [Test]
    public void Arrow_AvoidsTheThumbCorners()
    {
        _arrows = Arrows();

        // Down and to the left: its natural edge point is the bottom-left corner, which is where
        // a thumb is (GD §12.4, §16.1).
        Spawn(id: 1, new UnityEngine.Vector3(-10f, 0f, -10f));

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.EqualTo(1));

        UnityEngine.Vector2 point = Drawn(0).anchoredPosition;

        Assert.That(InThumbCorner(point), Is.False, "Nothing critical may resolve under a thumb.");
        Assert.That(OnBorder(point), Is.True, "And it slid along the border rather than inwards.");
    }

    [Test]
    public void Arrow_RespectsTheSafeArea()
    {
        // The notch, simulated the way the game actually handles it: the arrow root hangs under the
        // HUD's SafeArea object, so the rect handed to this class is already inset. Shrinking the
        // parent is therefore not a stand-in for the real thing — it *is* the real thing.
        _parent.sizeDelta = new UnityEngine.Vector2(BorderWidth - 160f, BorderHeight - 80f);

        _arrows = Arrows();

        Spawn(id: 1, new UnityEngine.Vector3(-14f, 0f, 2f));

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.EqualTo(1));

        UnityEngine.Vector2 point = Drawn(0).anchoredPosition;

        Assert.That(Mathf.Abs(point.x), Is.LessThanOrEqualTo((BorderWidth - 160f) * 0.5f));
        Assert.That(Mathf.Abs(point.y), Is.LessThanOrEqualTo((BorderHeight - 80f) * 0.5f));
    }

    [Test]
    public void Arrow_StaysOnTheBorderAllTheWayRound()
    {
        _arrows = Arrows();

        EnemyView body = Spawn(id: 1, new UnityEngine.Vector3(0f, 0f, -10f));

        // Manual step 5 as a test: drive the threat round the player and check every frame of it.
        for (int degrees = 0; degrees < 360; degrees += 5)
        {
            float radians = degrees * Mathf.Deg2Rad;

            body.transform.position = new UnityEngine.Vector3(
                Mathf.Cos(radians) * 14f,
                0f,
                Mathf.Sin(radians) * 14f);

            _arrows.LateTick();

            if (_arrows.Count == 0)
            {
                // In front of the camera and inside the frustum: on screen, and correctly ignored.
                continue;
            }

            UnityEngine.Vector2 point = Drawn(0).anchoredPosition;

            Assert.That(OnBorder(point), Is.True, $"At {degrees}° the arrow left the border.");
            Assert.That(InThumbCorner(point), Is.False, $"At {degrees}° the arrow sat under a thumb.");
        }
    }

    // ---- Proximity and colour -------------------------------------------------------------------

    [Test]
    public void Arrow_FadesWithDistance()
    {
        _arrows = Arrows();

        EnemyView body = Spawn(id: 1, new UnityEngine.Vector3(0f, 0f, -15f));

        _arrows.LateTick();

        float farAlpha = Tint(Drawn(0)).a;
        float farScale = Drawn(0).localScale.x;

        body.transform.position = new UnityEngine.Vector3(0f, 0f, -6f);

        _arrows.LateTick();

        Assert.That(Tint(Drawn(0)).a, Is.GreaterThan(farAlpha), "Closer is brighter.");
        Assert.That(Drawn(0).localScale.x, Is.GreaterThan(farScale), "And larger. Nothing on it is text.");
    }

    [Test]
    public void Arrow_IsDangerColoured()
    {
        _arrows = Arrows();

        Spawn(id: 1, new UnityEngine.Vector3(0f, 0f, -10f));

        _arrows.LateTick();

        Color tint = Tint(Drawn(0));

        // GD §16.4 reserves saturated red-orange for danger "and nothing else, ever". The alpha is
        // proximity's channel and is asserted by the row above, so only the hue is checked here.
        Assert.That(tint.r, Is.EqualTo(ThreatArrows.Danger.r).Within(1e-3f));
        Assert.That(tint.g, Is.EqualTo(ThreatArrows.Danger.g).Within(1e-3f));
        Assert.That(tint.b, Is.EqualTo(ThreatArrows.Danger.b).Within(1e-3f));
    }

    [Test]
    public void Arrow_HeldFocusIsCyan()
    {
        _arrows = Arrows();

        // Out past ThreatRange on purpose: a held focus is by definition something the gun could
        // not reach, so the arrow that answers ledger row 12 cannot be conditional on the range
        // that answers row 14.
        Spawn(id: 7, new UnityEngine.Vector3(0f, 0f, -20f));

        Hold(7);

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.EqualTo(1));

        Color tint = Tint(Drawn(0));

        Assert.That(tint.r, Is.EqualTo(ThreatArrows.HeldFocus.r).Within(1e-3f));
        Assert.That(tint.g, Is.EqualTo(ThreatArrows.HeldFocus.g).Within(1e-3f));
        Assert.That(tint.b, Is.EqualTo(ThreatArrows.HeldFocus.b).Within(1e-3f));
    }

    [Test]
    public void Arrow_HeldFocusAndThreatBothDrawn()
    {
        _arrows = Arrows();

        Spawn(id: 7, new UnityEngine.Vector3(-8f, 0f, -8f));
        Spawn(id: 8, new UnityEngine.Vector3(8f, 0f, -8f));

        Hold(7);

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.EqualTo(2));

        // One renderer, one border, two meanings: the thing you picked is over there, and that
        // other thing can hurt you. Answering rows 12 and 14 apart is what would have produced two
        // indicators that did not agree.
        Color first = Tint(Drawn(0));
        Color second = Tint(Drawn(1));

        Assert.That(first.r, Is.EqualTo(ThreatArrows.HeldFocus.r).Within(1e-3f), "The held focus is cyan.");
        Assert.That(second.r, Is.EqualTo(ThreatArrows.Danger.r).Within(1e-3f), "The other one is danger.");
    }

    [Test]
    public void Arrow_NoneForABoltInFlight()
    {
        _arrows = Arrows();

        Spawn(id: 1, new UnityEngine.Vector3(0f, 0f, -10f));

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.EqualTo(1));

        // The shooter dies; its bolt is still in the air and lands where the player can see it,
        // because a bolt's target is the ground under them and does not track (M2-07a rule 4).
        _hub.Publish(new EnemyDied(1, Husk, Vector3.Zero));
        _hub.Publish(new ProjectileFired(
            1,
            Husk,
            sourceId: 1,
            new Vector3(0f, 0f, -10f),
            Vector3.Zero,
            flightTime: 1f));

        _arrows.LateTick();

        Assert.That(
            _arrows.Count,
            Is.Zero,
            "The threat is the shooter, not the shot. A bolt already in the frame it will land in "
                + "needs no arrow of its own (rule 6).");
    }

    [Test]
    public void Arrow_ReleasedOnDespawn()
    {
        _arrows = Arrows();

        Spawn(id: 1, new UnityEngine.Vector3(0f, 0f, -10f));

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.EqualTo(1));

        RectTransform arrow = Drawn(0);

        _hub.Publish(new EnemyDespawned(1));

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.Zero);
        Assert.That(arrow.gameObject.activeSelf, Is.False, "And the instance is parked, not destroyed.");
        Assert.That(_arrows.InstanceCount, Is.EqualTo(4), "The prewarm is intact.");
    }

    // ---- GD §7.3's stall ------------------------------------------------------------------------

    [Test]
    public void Stall_ArrowsForAllSurvivorsAfterEightSeconds()
    {
        _arrows = Arrows();

        // Far past ThreatRange and behind the camera: these two cannot hurt the player and would
        // never earn an arrow on rule 5's terms. Hunting them is the problem GD §7.3 names.
        Spawn(id: 1, new UnityEngine.Vector3(-6f, 0f, -30f));
        Spawn(id: 2, new UnityEngine.Vector3(6f, 0f, -30f));

        _now = 7.9f;
        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.Zero, "Before eight seconds, nothing. The stall is a wait, not a rule.");

        _now = 8.1f;
        _arrows.LateTick();

        Assert.That(
            _arrows.Count,
            Is.EqualTo(2),
            "This is the answer M2-05 refused to paper over with a wave timeout: the fix for "
                + "hunting the last Husk is telling the player where it is.");
    }

    [Test]
    public void Stall_ResetsWhenTheWaveGrows()
    {
        _arrows = Arrows();

        Spawn(id: 1, new UnityEngine.Vector3(-6f, 0f, -30f));
        Spawn(id: 2, new UnityEngine.Vector3(6f, 0f, -30f));

        _now = 6f;
        _arrows.LateTick();

        for (int id = 3; id <= 7; id++)
        {
            Spawn(id, new UnityEngine.Vector3(id, 0f, -30f));
        }

        _now = 10f;
        _arrows.LateTick();

        Assert.That(
            _arrows.Count,
            Is.Zero,
            "A wave arriving is the arena no longer being stalled, so the clock starts again from "
                + "the next time it empties out.");
    }

    [Test]
    public void Stall_DoesNotFireWithFourAlive()
    {
        _arrows = Arrows();

        for (int id = 1; id <= 4; id++)
        {
            Spawn(id, new UnityEngine.Vector3(id, 0f, -30f));
        }

        _now = 20f;
        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.Zero, "Four is not GD §7.3's 'fewer than 3 remain'.");
    }

    [Test]
    public void Stall_SurvivesAKillInsideTheBand()
    {
        _arrows = Arrows();

        Spawn(id: 1, new UnityEngine.Vector3(-6f, 0f, -30f));
        Spawn(id: 2, new UnityEngine.Vector3(6f, 0f, -30f));

        _now = 7f;
        _arrows.LateTick();

        _hub.Publish(new EnemyDespawned(2));

        _now = 8.5f;
        _arrows.LateTick();

        Assert.That(
            _arrows.Count,
            Is.EqualTo(1),
            "Killing one of the survivors is the player making progress on exactly the problem the "
                + "rule exists to end; restarting the eight seconds would punish them for it.");
    }

    // ---- Instances ------------------------------------------------------------------------------

    [Test]
    public void Arrows_PrewarmInstantiatesUpFront()
    {
        _arrows = Arrows(prewarm: 8);

        Assert.That(_arrows.InstanceCount, Is.EqualTo(8), "The whole prewarm exists before the run starts.");
        Assert.That(_arrows.Count, Is.Zero, "And none of it is drawn until something is off screen.");

        for (int id = 1; id <= 8; id++)
        {
            Spawn(id, new UnityEngine.Vector3(id - 4.5f, 0f, -12f));
        }

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.EqualTo(8));
        Assert.That(
            _arrows.InstanceCount,
            Is.EqualTo(8),
            "The first eight arrows came out of the prewarm — nothing was instantiated on a frame "
                + "a Spitter was releasing (AR §14, GD §11.3).");
    }

    [Test]
    public void Arrows_BeyondPrewarm_GrowOnce()
    {
        _arrows = Arrows(prewarm: 2);

        Spawn(id: 1, new UnityEngine.Vector3(-6f, 0f, -12f));
        Spawn(id: 2, new UnityEngine.Vector3(0f, 0f, -12f));
        Spawn(id: 3, new UnityEngine.Vector3(6f, 0f, -12f));

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.EqualTo(3));
        Assert.That(_arrows.InstanceCount, Is.EqualTo(3), "A third was made for the third threat.");

        _arrows.LateTick();

        Assert.That(_arrows.InstanceCount, Is.EqualTo(3), "And kept, so the next frame makes nothing.");
    }

    [Test]
    public void Arrows_AllocateNothingPerFrame()
    {
        _arrows = Arrows(prewarm: 8);

        for (int id = 1; id <= 8; id++)
        {
            Spawn(id, new UnityEngine.Vector3(id - 4.5f, 0f, -12f));
        }

        // Warmed up before measuring: the first frame is where the census array, the graphics
        // lookups and every one-off inside Unity's own call paths get touched.
        _arrows.LateTick();
        _arrows.LateTick();

        int frame = 0;

        AllocationAssert.None(
            () =>
            {
                // The threats move, so the projection, the border walk, the thumb snap and the
                // colour writes are all on the measured path rather than early-returning on an
                // unchanged value.
                _views.TryGet(1, out EnemyView body);

                body.transform.position = new UnityEngine.Vector3(
                    Mathf.Sin(frame * 0.01f) * 12f,
                    0f,
                    -12f);

                frame++;

                _arrows.LateTick();
            },
            iterations: 10_000);
    }

    // ---- Lifetime and guards --------------------------------------------------------------------

    [Test]
    public void Dispose_UnsubscribesAndDestroys()
    {
        _arrows = Arrows();

        Spawn(id: 1, new UnityEngine.Vector3(-6f, 0f, -12f));
        Spawn(id: 2, new UnityEngine.Vector3(6f, 0f, -12f));

        _arrows.LateTick();

        Assert.That(_arrows.Count, Is.EqualTo(2));

        _arrows.Dispose();

        Assert.That(_arrows.Count, Is.Zero);
        Assert.That(_arrows.InstanceCount, Is.Zero, "Every instance it made goes with it.");

        // The subscriptions are gone, so an enemy spawned by a run this object has outlived reaches
        // nothing — and cannot ask a destroyed instance to move.
        Assert.That(() => Spawn(id: 3, new UnityEngine.Vector3(0f, 0f, -12f)), Throws.Nothing);
        Assert.That(() => _arrows.LateTick(), Throws.Nothing);

        Assert.That(_arrows.Count, Is.Zero);

        // Idempotent, like every other Dispose in the project — a double dispose is a disposal-order
        // question nobody should have to answer.
        Assert.That(() => _arrows.Dispose(), Throws.Nothing);
    }

    [Test]
    public void Constructor_NullDependency_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ThreatArrows(null, _arrowPrefab, _parent, _camera, _views, _player, _hub, Clock));

        Assert.Throws<ArgumentNullException>(
            () => new ThreatArrows(_container, null, _parent, _camera, _views, _player, _hub, Clock));

        Assert.Throws<ArgumentNullException>(
            () => new ThreatArrows(_container, _arrowPrefab, null, _camera, _views, _player, _hub, Clock));

        Assert.Throws<ArgumentNullException>(
            () => new ThreatArrows(_container, _arrowPrefab, _parent, null, _views, _player, _hub, Clock));

        Assert.Throws<ArgumentNullException>(
            () => new ThreatArrows(_container, _arrowPrefab, _parent, _camera, null, _player, _hub, Clock));

        Assert.Throws<ArgumentNullException>(
            () => new ThreatArrows(_container, _arrowPrefab, _parent, _camera, _views, null, _hub, Clock));

        Assert.Throws<ArgumentNullException>(
            () => new ThreatArrows(_container, _arrowPrefab, _parent, _camera, _views, _player, null, Clock));

        Assert.Throws<ArgumentNullException>(
            () => new ThreatArrows(_container, _arrowPrefab, _parent, _camera, _views, _player, _hub, null));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ThreatArrows(_container, _arrowPrefab, _parent, _camera, _views, _player, _hub, Clock, prewarm: -1));
    }

    /// <remarks>
    /// The clock is the only non-finite door this class actually has. The other candidate — a body
    /// at a NaN position — cannot be built: Unity refuses a non-finite <c>transform.position</c>
    /// assignment and logs an error instead of storing it, so no arena can produce one and a row
    /// asserting about it would be asserting about an unreachable state. The guard in
    /// <c>TryFrameDirection</c> stays anyway, because a finite position far enough out still
    /// overflows the arithmetic between them.
    /// </remarks>
    [Test]
    public void NonFiniteClock_DoesNotStall()
    {
        _arrows = Arrows();

        Spawn(id: 1, new UnityEngine.Vector3(0f, 0f, -30f));

        _now = float.NaN;

        // Refused rather than thrown on. A NaN wall clock reads as "the stall has not elapsed",
        // which degrades to the game as it was before GD §7.3's rule existed — where the opposite
        // reading would pin an arrow to the border for the rest of the run.
        Assert.That(() => _arrows.LateTick(), Throws.Nothing);

        Assert.That(_arrows.Count, Is.Zero);
    }

    [Test]
    public void ZeroSizedBorder_DoesNotDivide()
    {
        _parent.sizeDelta = UnityEngine.Vector2.zero;

        _arrows = Arrows();

        Spawn(id: 1, new UnityEngine.Vector3(0f, 0f, -12f));

        // A rect with no size is a HUD that has not been laid out yet, which happens for exactly
        // one frame on some canvases. Everything the border walk does divides by a dimension.
        Assert.That(() => _arrows.LateTick(), Throws.Nothing);

        Assert.That(float.IsNaN(Drawn(0).anchoredPosition.x), Is.False);
    }

    // ---- Fixture helpers ------------------------------------------------------------------------

    private ThreatArrows Arrows(int prewarm = 4) =>
        new ThreatArrows(_container, _arrowPrefab, _parent, _camera, _views, _player, _hub, Clock, prewarm);

    private float Clock() => _now;

    /// <summary>One enemy, announced to both censuses the way core announces it.</summary>
    private EnemyView Spawn(int id, UnityEngine.Vector3 position)
    {
        _hub.Publish(new EnemySpawned(id, Husk, position.ToNum()));

        _views.TryGet(id, out EnemyView view);

        return view;
    }

    private void Hold(int id) => _hub.Publish(new TargetChanged(-1, false, false, id));

    /// <summary>The <paramref name="index"/>th arrow currently drawn, in the order it was placed.</summary>
    private RectTransform Drawn(int index)
    {
        int seen = 0;

        for (int i = 0; i < _parent.childCount; i++)
        {
            var child = (RectTransform)_parent.GetChild(i);

            if (!child.gameObject.activeSelf)
            {
                continue;
            }

            if (seen == index)
            {
                return child;
            }

            seen++;
        }

        Assert.Fail($"No arrow at index {index}; only {seen} are drawn.");

        return null;
    }

    private static Color Tint(RectTransform arrow) => arrow.GetComponentInChildren<Graphic>(true).color;

    /// <summary>Within a pixel of the border rect's perimeter.</summary>
    private bool OnBorder(UnityEngine.Vector2 point)
    {
        Rect rect = _parent.rect;

        float halfWidth = (rect.width * 0.5f) - 28f;
        float halfHeight = (rect.height * 0.5f) - 28f;

        bool onVertical = Mathf.Abs(Mathf.Abs(point.x) - halfWidth) < 1f && Mathf.Abs(point.y) <= halfHeight + 1f;
        bool onHorizontal = Mathf.Abs(Mathf.Abs(point.y) - halfHeight) < 1f && Mathf.Abs(point.x) <= halfWidth + 1f;

        return onVertical || onHorizontal;
    }

    /// <summary>Inside either of GD §12.4's two bottom corners.</summary>
    private bool InThumbCorner(UnityEngine.Vector2 point)
    {
        Rect rect = _parent.rect;

        float halfWidth = (rect.width * 0.5f) - 28f;
        float halfHeight = (rect.height * 0.5f) - 28f;

        float thumbWidth = halfWidth * 2f * ThreatArrows.ThumbFraction;
        float thumbHeight = halfHeight * 2f * ThreatArrows.ThumbFraction;

        // A hair inside the box, so a point snapped exactly onto its edge counts as escaped rather
        // than as still caught.
        const float Epsilon = 1e-3f;

        bool inX = point.x < -halfWidth + thumbWidth - Epsilon || point.x > halfWidth - thumbWidth + Epsilon;
        bool inY = point.y < -halfHeight + thumbHeight - Epsilon;

        return inX && inY;
    }

    private static void Destroy(ref GameObject target)
    {
        if (target != null)
        {
            Object.DestroyImmediate(target);
        }

        target = null;
    }
}
