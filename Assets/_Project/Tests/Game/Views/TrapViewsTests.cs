using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class TrapViewsTests
{
    private TrapViews _view;
    private DomainEventHub _hub;

    [SetUp]
    public void SetUp()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<TrapViews>("Assets/_Project/Prefabs/Vfx/VFX_HuntersSnare.prefab");
        Assert.That(prefab, Is.Not.Null);
        _view = Object.Instantiate(prefab);
        _hub = new DomainEventHub();
        _view.Construct(_hub, null, null);
    }

    [TearDown]
    public void TearDown()
    {
        if (_view != null)
        {
            _view.Dispose();
            Object.DestroyImmediate(_view.gameObject);
        }
        _hub.Dispose();
    }

    [Test]
    public void Place_ShowsAtPoint()
    {
        _hub.Publish(new TrapPlaced(1, new System.Numerics.Vector3(2f, 0f, -3f)));
        Transform snare = _view.Find(1);

        Assert.That(snare, Is.Not.Null);
        Assert.That(snare.gameObject.activeInHierarchy, Is.True);
        Assert.That(snare.position.y, Is.GreaterThan(0f), "It drops in from just above the floor.");
        _view.Step(0.2f);
        Assert.That(Vector3.Distance(snare.position, new Vector3(2f, 0f, -3f)), Is.LessThan(0.0001f));
        Assert.That(_view.RopeRadius(1), Is.EqualTo(TrapViews.LoopRadius));
        Assert.That(_view.ActiveCount, Is.EqualTo(1));
    }

    [Test]
    public void Armed_Pulses()
    {
        _hub.Publish(new TrapPlaced(1, System.Numerics.Vector3.Zero));
        _view.Step(0.29f);
        Assert.That(_view.IsArmed(1), Is.False, "No ring before it arms.");

        _view.Step(0.02f);
        Assert.That(_view.IsArmed(1), Is.True);
        float low = 1f, high = 0f;
        for (int frame = 0; frame < 120; frame++)
        {
            _view.Step(1f / 60f);
            low = Mathf.Min(low, _view.RingAlpha(1));
            high = Mathf.Max(high, _view.RingAlpha(1));
        }
        Assert.That(high - low, Is.GreaterThan(0.1f), "It pulses.");
        Assert.That(high, Is.LessThanOrEqualTo(0.36f), "Faint.");
        Assert.That(low, Is.GreaterThan(0f));
        var ring = _view.Find(1).Find("Cyan Ring").GetComponent<LineRenderer>();
        Color rgb = ring.startColor;
        rgb.a = 1f;
        Assert.That(Vector4.Distance(rgb, Palette.Player), Is.LessThan(0.0001f));
    }

    [Test]
    public void Snap_ClosesThenHides()
    {
        var feet = new GameObject("Frog").transform;
        try
        {
            feet.position = new Vector3(0.6f, 0f, 0f);
            _view.Place(1, Vector3.zero);
            _view.Step(0.5f);
            _view.Spring(1, feet);

            _view.Step(0.1f);
            Assert.That(_view.IsArmed(1), Is.False, "The ring goes with the snap.");
            Assert.That(_view.RopeRadius(1), Is.LessThan(TrapViews.LoopRadius));
            var rope = _view.Find(1).Find("Rope Loop").GetComponent<LineRenderer>();
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < rope.positionCount; i++)
            {
                centre += rope.GetPosition(i) / rope.positionCount;
            }
            Assert.That(centre.x, Is.GreaterThan(0.3f), "The loop closes toward the victim's feet.");
            Assert.That(centre.y, Is.GreaterThan(0.1f), "It jerks up.");

            Transform snare = _view.Find(1);
            _view.Step(0.06f);
            Assert.That(_view.Find(1), Is.Null);
            Assert.That(snare.gameObject.activeSelf, Is.False);
            Assert.That(_view.ActiveCount, Is.EqualTo(0));
        }
        finally
        {
            Object.DestroyImmediate(feet.gameObject);
        }
    }

    [Test]
    public void Expire_Fades()
    {
        _view.Place(4, Vector3.zero);
        _view.Step(1f);
        _hub.Publish(new TrapExpired(4));
        _view.Step(0.15f);

        Assert.That(_view.Opacity(4), Is.InRange(0.4f, 0.6f));
        Assert.That(_view.Find(4).position.y, Is.LessThan(0f), "It sinks.");
        _view.Step(0.16f);
        Assert.That(_view.Find(4), Is.Null);
        Assert.That(_view.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void Pooled_ClearedOnStage()
    {
        Transform[] first = new Transform[_view.PoolSize];
        for (int i = 0; i < _view.PoolSize; i++)
        {
            first[i] = _view.Place(i + 1, Vector3.right * i);
        }
        Assert.That(_view.PoolSize, Is.EqualTo(4));
        Assert.That(_view.ActiveCount, Is.EqualTo(4));

        _hub.Publish(new StageArrived(2, default));

        Assert.That(_view.ActiveCount, Is.EqualTo(0));
        foreach (Transform snare in first)
        {
            Assert.That(snare.gameObject.activeSelf, Is.False);
        }
        Transform again = _view.Place(9, Vector3.zero);
        Assert.That(System.Array.IndexOf(first, again), Is.GreaterThanOrEqualTo(0), "Reused, not instantiated.");
    }
}
