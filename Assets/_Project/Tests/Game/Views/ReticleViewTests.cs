using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// The second marker: the faint ring and chevron on the enemy the player tapped that the gun has
/// not taken (ledger row 12). Three things can be wrong here and none of them throws — the marker
/// on the wrong enemy, the marker pulsing so that it reads as the bright one, and the marker
/// surviving the body it was parked on.
/// </summary>
/// <remarks>
/// <para>
/// <c>Awake</c> and <c>LateUpdate</c> never run in EditMode (Traps §5), so the fixture calls them
/// itself — through a delegate bound once in the setup rather than through <c>SendMessage</c>,
/// because <c>SendMessage</c> allocates and the allocation row would then be measuring the test
/// harness rather than the view. <c>Awake</c> is what builds the geometry and writes the held
/// marker's constant look; skipping it would leave every assertion here measuring an empty
/// <see cref="LineRenderer"/>.
/// </para>
/// <para>
/// The reticle is a scene object rather than <c>Reticle.prefab</c>, for <c>ProjectileViewsTests</c>'
/// reason: the claims are about behaviour, and building the hierarchy here keeps the fixture from
/// depending on how the art is dressed. The prefab's own wiring is what the manual steps check.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ReticleViewTests
{
    private static readonly ContentId Husk = new ContentId("enemy.husk");

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private GameObject _reticleObject;
    private ReticleView _reticle;
    private GameObject _heldVisual;
    private GameObject _visual;

    private Action _lateUpdate;

    private GameObject _enemyPrefabObject;
    private EnemyView _enemyPrefab;

    private IObjectResolver _container;
    private DomainEventHub _hub;
    private EnemyViews _views;

    [SetUp]
    public void CreateReticle()
    {
        _enemyPrefabObject = new GameObject("EnemyPrefab");
        _enemyPrefab = _enemyPrefabObject.AddComponent<EnemyView>();

        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();
        _views = new EnemyViews(
            _container,
            _enemyPrefab,
            null,
            _hub,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()),
            prewarm: 4);

        _reticleObject = new GameObject("Reticle");
        _reticle = _reticleObject.AddComponent<ReticleView>();

        _visual = Child(_reticleObject, "Visual");
        _heldVisual = Child(_reticleObject, "HeldVisual");

        Dress(_reticle, _visual, _heldVisual);

        _reticle.Construct(_hub, _views);

        Invoke(_reticle, "Awake");

        _lateUpdate = (Action)Delegate.CreateDelegate(
            typeof(Action),
            _reticle,
            typeof(ReticleView).GetMethod("LateUpdate", Private));
    }

    [TearDown]
    public void DestroyReticle()
    {
        _lateUpdate = null;

        _views?.Dispose();
        _views = null;

        _hub?.Dispose();
        _hub = null;

        _container?.Dispose();
        _container = null;

        Destroy(ref _reticleObject);
        Destroy(ref _enemyPrefabObject);
    }

    [Test]
    public void Reticle_ShowsHeldMarkerOnTheTappedEnemy()
    {
        Spawn(3, new Vector3(0f, 0f, 5f));
        Spawn(7, new Vector3(9f, 0f, 0f));

        Publish(id: 3, isFocused: false, heldFocusId: 7);

        _lateUpdate();

        Assert.That(_heldVisual.activeSelf, Is.True);

        // Two markers on two enemies: the bright ring on what the gun is shooting, the faint one on
        // what the player picked. That is the whole of ledger row 12 on screen.
        Assert.That(_reticle.transform.position.z, Is.EqualTo(5f).Within(1e-4f));
        Assert.That(_heldVisual.transform.position.x, Is.EqualTo(9f).Within(1e-4f));
    }

    [Test]
    public void Reticle_HeldMarkerDoesNotPulse()
    {
        Spawn(3, new Vector3(0f, 0f, 5f));
        Spawn(7, new Vector3(9f, 0f, 0f));

        Publish(id: 3, isFocused: false, heldFocusId: 7);

        _lateUpdate();

        Vector3 held = _heldVisual.transform.localScale;
        Vector3 main = _visual.transform.localScale;

        for (int i = 0; i < 8; i++)
        {
            _lateUpdate();
        }

        // Only IsFocused pulses, and IsFocused and HeldFocusId are never both set — so whenever
        // this marker is up, nothing on screen is moving. A second pulsing shape would make the
        // two read as one state instead of two.
        Assert.That(_heldVisual.transform.localScale, Is.EqualTo(held));
        Assert.That(_visual.transform.localScale, Is.EqualTo(main));
    }

    [Test]
    public void Reticle_HeldMarkerIsFainterThanTheAutoRing()
    {
        Spawn(3, new Vector3(0f, 0f, 5f));
        Spawn(7, new Vector3(9f, 0f, 0f));

        Publish(id: 3, isFocused: false, heldFocusId: 7);

        _lateUpdate();

        float autoAlpha = Alpha("_ring");
        float heldAlpha = Alpha("_heldRing");

        // Three levels of one cyan, and the ordering is the information: focused 1.0, auto 0.6,
        // held 0.35. Brightest is what the gun is on (GD §16.4).
        Assert.That(heldAlpha, Is.LessThan(autoAlpha));
        Assert.That(heldAlpha, Is.GreaterThan(0f), "Fainter, not invisible.");
    }

    [Test]
    public void Reticle_HeldMarkerIsSmallerThanTheRing()
    {
        // Built at a fraction of the radius rather than scaled by a transform, because a transform
        // scale is the pulse's channel and this marker must never pulse.
        Assert.That(Radius("_heldRing"), Is.LessThan(Radius("_ring")));
        Assert.That(Radius("_heldRing"), Is.GreaterThan(0f));
    }

    [Test]
    public void Reticle_HeldMarkerHidesWhenTheFocusLands()
    {
        Spawn(7, new Vector3(9f, 0f, 0f));

        Publish(id: -1, isFocused: false, heldFocusId: 7);

        _lateUpdate();

        Assert.That(_heldVisual.activeSelf, Is.True);

        // The player walked into range and the gun took it. One bright pulsing ring, no second
        // marker — the two fields are two states of one thing.
        Publish(id: 7, isFocused: true, heldFocusId: -1);

        _lateUpdate();

        Assert.That(_heldVisual.activeSelf, Is.False);
        Assert.That(_visual.activeSelf, Is.True);
    }

    [Test]
    public void Reticle_HeldMarkerShowsWithNoTargetAtAll()
    {
        Spawn(7, new Vector3(9f, 0f, 0f));

        // The gun has nothing to shoot and the player is still holding a focus on something out of
        // range. The two facts are independent, and a held marker that hid with the ring would be
        // invisible in exactly the arena this feature is for.
        Publish(id: -1, isFocused: false, heldFocusId: 7);

        _lateUpdate();

        Assert.That(_visual.activeSelf, Is.False);
        Assert.That(_heldVisual.activeSelf, Is.True);
    }

    [Test]
    public void Reticle_HeldMarkerHidesWhenTheBodyGoes()
    {
        Spawn(7, new Vector3(9f, 0f, 0f));

        Publish(id: -1, isFocused: false, heldFocusId: 7);

        _lateUpdate();

        Assert.That(_heldVisual.activeSelf, Is.True);

        _hub.Publish(new EnemyDespawned(7));

        // A miss is not an error: TargetChanged is published from inside core's tick, and the body
        // it names may be returned to the pool in the same frame. Hiding is the honest answer for
        // the frame in between.
        Assert.That(() => _lateUpdate(), Throws.Nothing);

        Assert.That(_heldVisual.activeSelf, Is.False);
    }

    [Test]
    public void Reticle_AllocatesNothingPerFrame()
    {
        Spawn(3, new Vector3(0f, 0f, 5f));
        Spawn(7, new Vector3(9f, 0f, 0f));

        Publish(id: 3, isFocused: false, heldFocusId: 7);

        AllocationAssert.None(_lateUpdate, iterations: 10_000);
    }

    [Test]
    public void Awake_WithoutTheHeldMarker_Throws()
    {
        var bare = new GameObject("BareReticle");

        try
        {
            ReticleView view = bare.AddComponent<ReticleView>();

            GameObject visual = Child(bare, "Visual");

            Set(view, "_visual", visual);
            Set(view, "_ring", Line(visual, "Ring"));
            Set(view, "_chevron", Line(visual, "Chevron"));
            Set(view, "_blockedFirstStroke", Line(visual, "BlockedStrokeA"));
            Set(view, "_blockedSecondStroke", Line(visual, "BlockedStrokeB"));

            // The eight references are checked together, so a prefab that gained the three new
            // fields but never had them dragged in fails loudly at Awake rather than silently
            // drawing nothing — which is the failure mode the whole marker exists to prevent.
            TargetInvocationException thrown =
                Assert.Throws<TargetInvocationException>(() => Invoke(view, "Awake"));

            Assert.That(thrown.InnerException, Is.TypeOf<MissingReferenceException>());
            Assert.That(thrown.InnerException.Message, Does.Contain("_heldVisual"));
        }
        finally
        {
            Object.DestroyImmediate(bare);
        }
    }

    // ---- Fixture helpers ------------------------------------------------------------------------

    private void Publish(int id, bool isFocused, int heldFocusId) =>
        _hub.Publish(new TargetChanged(id, isFocused, false, heldFocusId));

    private void Spawn(int id, Vector3 position) =>
        _hub.Publish(new EnemySpawned(id, Husk, position.ToNum()));

    /// <summary>The alpha the view wrote into <paramref name="field"/>'s property block.</summary>
    private float Alpha(string field)
    {
        var line = (LineRenderer)Get(_reticle, field);

        var block = new MaterialPropertyBlock();

        line.GetPropertyBlock(block);

        return block.GetColor(Shader.PropertyToID("_BaseColor")).a;
    }

    /// <summary>The ring's built radius, read back off its first point.</summary>
    private float Radius(string field)
    {
        var line = (LineRenderer)Get(_reticle, field);

        return line.GetPosition(0).magnitude;
    }

    private static void Dress(ReticleView view, GameObject visual, GameObject heldVisual)
    {
        Set(view, "_visual", visual);
        Set(view, "_ring", Line(visual, "Ring"));
        Set(view, "_chevron", Line(visual, "Chevron"));
        Set(view, "_blockedFirstStroke", Line(visual, "BlockedStrokeA"));
        Set(view, "_blockedSecondStroke", Line(visual, "BlockedStrokeB"));

        Set(view, "_heldVisual", heldVisual);
        Set(view, "_heldRing", Line(heldVisual, "HeldRing"));
        Set(view, "_heldChevron", Line(heldVisual, "HeldChevron"));
    }

    private static void Set(ReticleView view, string field, Object value) =>
        typeof(ReticleView).GetField(field, Private).SetValue(view, value);

    private static Object Get(ReticleView view, string field) =>
        (Object)typeof(ReticleView).GetField(field, Private).GetValue(view);

    private static void Invoke(ReticleView view, string method) =>
        typeof(ReticleView).GetMethod(method, Private).Invoke(view, null);

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);

        child.transform.SetParent(parent.transform, false);

        return child;
    }

    private static LineRenderer Line(GameObject parent, string name) =>
        Child(parent, name).AddComponent<LineRenderer>();

    private static void Destroy(ref GameObject target)
    {
        if (target != null)
        {
            Object.DestroyImmediate(target);
        }

        target = null;
    }
}
