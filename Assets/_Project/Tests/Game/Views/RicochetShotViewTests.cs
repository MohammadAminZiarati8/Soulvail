using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using VContainer;
using Point = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

public sealed class RicochetShotViewTests
{
    private ProjectileView _arrow;
    private ProjectileViews _views;
    private DomainEventHub _hub;
    private IObjectResolver _resolver;

    [SetUp]
    public void SetUp()
    {
        _arrow = AssetDatabase.LoadAssetAtPath<ProjectileView>("Assets/_Project/Prefabs/Projectiles/Arrow.prefab");
        Assert.That(_arrow, Is.Not.Null);
        Assert.That(_arrow.RicochetSpark, Is.Not.Null);
        _hub = new DomainEventHub();
        _resolver = new ContainerBuilder().Build();
        _views = new ProjectileViews(_resolver, _arrow, null, _hub, prewarm: 1, classPrewarm: 4);
    }

    [TearDown]
    public void TearDown()
    {
        _views?.Dispose();
        _resolver?.Dispose();
    }

    [Test]
    public void View_ChainArrow_UsesBrightTrail()
    {
        Fire(1);
        TrailRenderer trail = Trail(1);
        float basicTime = trail.time;
        float basicWidth = trail.widthMultiplier;
        _hub.Publish(new ProjectileImpacted(1, Point.Zero, false));

        Fire(2, ricochet: true);
        Assert.That(Trail(2), Is.SameAs(trail));
        Assert.That(trail.time, Is.GreaterThan(basicTime));
        Assert.That(trail.widthMultiplier, Is.GreaterThan(basicWidth));
        Assert.That(trail.startColor, Is.EqualTo(Color.white));
        Assert.That(trail.endColor.g, Is.GreaterThan(trail.endColor.r));
    }

    [Test]
    public void View_BasicArrow_Unchanged()
    {
        Fire(1);
        TrailRenderer trail = Trail(1);
        float time = trail.time;
        float width = trail.widthMultiplier;
        Color start = trail.startColor;
        Color end = trail.endColor;
        _hub.Publish(new ProjectileImpacted(1, Point.Zero, false));

        Fire(2, ricochet: true);
        _hub.Publish(new ProjectileImpacted(2, Point.Zero, false));

        Fire(3);
        Assert.That(Trail(3), Is.SameAs(trail));
        Assert.That(trail.time, Is.EqualTo(time));
        Assert.That(trail.widthMultiplier, Is.EqualTo(width));
        Assert.That(trail.startColor, Is.EqualTo(start));
        Assert.That(trail.endColor, Is.EqualTo(end));
    }

    [Test]
    public void View_SparkAtEachBounce_NotAtFirst()
    {
        Fire(1, ricochet: true);
        Assert.That(_views.ImpactCount, Is.Zero);
        for (int id = 2; id <= 3; id++)
        {
            var bounce = new Point(id, 0f, -id);
            _hub.Publish(new ProjectileImpacted(id - 1, bounce, true, bounces: true));
            Assert.That(_views.ImpactCount, Is.EqualTo(id - 2));

            Fire(id, ricochet: true, chainIndex: id - 1, origin: bounce);
            Assert.That(_views.ImpactCount, Is.EqualTo(id - 1));
            Vector3 position = _views.ImpactAt(id - 2).transform.position;
            Assert.That(position.x, Is.EqualTo(bounce.X).Within(0.001f));
            Assert.That(position.z, Is.EqualTo(bounce.Z).Within(0.001f));
        }

        int ordinaryPool = _views.PooledImpactCountOf(_arrow.Impact);
        _hub.Publish(new ProjectileImpacted(3, Point.One, true));
        Assert.That(_views.ImpactCount, Is.EqualTo(3));
        Assert.That(_views.PooledImpactCountOf(_arrow.Impact), Is.EqualTo(ordinaryPool - 1));
    }

    [Test]
    public void View_Pooled_ClearsOnBoundary()
    {
        int warmed = _views.PooledImpactCountOf(_arrow.RicochetSpark);
        Assert.That(warmed, Is.EqualTo(4));

        Fire(1, ricochet: true, chainIndex: 1);
        ImpactView spark = _views.ImpactAt(0);
        Assert.That(_views.PooledImpactCountOf(_arrow.RicochetSpark), Is.EqualTo(warmed - 1));
        _hub.Publish(new StageTransitionStarted(12, 0.5f));
        Assert.That(_views.ImpactCount, Is.Zero);
        Assert.That(spark.IsLive, Is.False);
        Assert.That(_views.PooledImpactCountOf(_arrow.RicochetSpark), Is.EqualTo(warmed));

        _hub.Publish(new ProjectileImpacted(1, Point.Zero, false));
        Fire(2, ricochet: true, chainIndex: 1);
        Assert.That(_views.ImpactAt(0), Is.SameAs(spark));
        _views.Step(spark.Lifetime + 0.01f);
        Assert.That(_views.ImpactCount, Is.Zero);
        Assert.That(_views.PooledImpactCountOf(_arrow.RicochetSpark), Is.EqualTo(warmed));
    }

    private void Fire(int id, bool ricochet = false, int chainIndex = 0, Point origin = default)
    {
        _hub.Publish(new ProjectileFired(id, new ContentId("character.ranger"), 0,
            origin, origin + Point.UnitX, 0.2f, isRicochet: ricochet, chainIndex: chainIndex));
    }

    private TrailRenderer Trail(int id)
    {
        Assert.That(_views.TryGet(id, out ProjectileView view), Is.True);
        return view.GetComponentInChildren<TrailRenderer>();
    }
}
