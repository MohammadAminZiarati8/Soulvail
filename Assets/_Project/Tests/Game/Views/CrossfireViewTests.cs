using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using VContainer;
using Point = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

public sealed class CrossfireViewTests
{
    private const string Root = "Assets/_Project/";
    private static readonly ContentId Ranger = new("character.ranger");
    private static readonly Point Target = new(0f, 0f, 3f);
    private GameObject _parent;
    private DomainEventHub _hub;
    private CrossfireView _lines;

    [SetUp]
    public void SetUp()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/M_CrossfireLine.mat");
        Assert.That(material, Is.Not.Null);
        _parent = new GameObject("Crossfire test");
        _hub = new DomainEventHub();
        _lines = new CrossfireView(_hub, material, _parent.transform, ShadeSystem.Capacity);
    }

    [TearDown]
    public void TearDown()
    {
        _lines?.Dispose();
        if (_parent != null)
        {
            Object.DestroyImmediate(_parent);
        }
    }

    [Test]
    public void Line_PerCrossfireArrow_EndsAfterPointOneFive()
    {
        Fire(1, ShotOrigin.Crossfire, Point.UnitX);
        Fire(2, ShotOrigin.Crossfire, -Point.UnitX);

        LineRenderer[] lines = ActiveLines();
        Assert.That(lines.Length, Is.EqualTo(2));
        Assert.That(lines.Select(line => line.GetPosition(0).x), Is.EquivalentTo(new[] { -1f, 1f }));
        Assert.That(lines[0].GetPosition(1), Is.EqualTo(lines[1].GetPosition(1)));
        Assert.That(lines[0].GetPosition(1).z, Is.EqualTo(Target.Z));

        _lines.Step(0.149f);
        Assert.That(ActiveLines().Length, Is.EqualTo(2));
        _lines.Step(0.002f);
        Assert.That(ActiveLines(), Is.Empty);
    }

    [TestCase(ShotOrigin.Ranger)]
    [TestCase(ShotOrigin.Shade)]
    [TestCase(ShotOrigin.Echo)]
    public void Line_NotForOtherOrigins(ShotOrigin origin)
    {
        Fire(1, origin, Point.Zero);
        Assert.That(ActiveLines(), Is.Empty);
    }

    [Test]
    public void CrossfireArrow_DoubleScale()
    {
        var arrow = AssetDatabase.LoadAssetAtPath<ProjectileView>(Root + "Prefabs/Projectiles/Arrow.prefab");
        var bolt = AssetDatabase.LoadAssetAtPath<ProjectileView>(Root + "Prefabs/Projectiles/Projectile.prefab");
        var looks = new CharacterLookBook(new Dictionary<ContentId, CharacterLook>
        {
            { Ranger, new CharacterLook(null, arrow) },
        });
        using var resolver = new ContainerBuilder().Build();
        using var views = new ProjectileViews(resolver, bolt, _parent.transform, _hub, prewarm: 2,
            looks: looks, classPrewarm: 1);
        Assert.That(views.PooledCountOf(arrow), Is.EqualTo(2));

        Fire(1, ShotOrigin.Ranger, Point.Zero);
        Assert.That(views.TryGet(1, out ProjectileView ordinary), Is.True);
        var trail = ordinary.GetComponentInChildren<TrailRenderer>();
        float time = trail.time;
        float width = trail.widthMultiplier;
        Color color = trail.startColor;
        Vector3 scale = ordinary.transform.localScale;
        _hub.Publish(new ProjectileImpacted(1, Target, hit: false));

        Fire(2, ShotOrigin.Crossfire, Point.UnitX);
        Assert.That(views.TryGet(2, out ProjectileView heavy), Is.True);
        Assert.That(heavy, Is.SameAs(ordinary));
        Assert.That(heavy.transform.localScale, Is.EqualTo(2f * scale));
        Assert.That(trail.time, Is.GreaterThan(time));
        Assert.That(trail.startColor.r, Is.LessThan(color.r));
        _hub.Publish(new ProjectileImpacted(2, Target, hit: false));

        Fire(3, ShotOrigin.Ranger, Point.Zero);
        Assert.That(views.TryGet(3, out ProjectileView reused), Is.True);
        Assert.That(reused, Is.SameAs(ordinary));
        Assert.That(reused.transform.localScale, Is.EqualTo(scale));
        Assert.That(trail.time, Is.EqualTo(time));
        Assert.That(trail.widthMultiplier, Is.EqualTo(width));
        Assert.That(trail.startColor, Is.EqualTo(color));
    }

    [Test]
    public void Cast_UpperBodyRelease_MovementKept()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Player/Bodies/Ranger.prefab");
        GameObject body = Object.Instantiate(source, _parent.transform);
        var animator = body.GetComponent<Animator>();
        body.GetComponent<RangerAnimatorView>().Construct(_hub);

        animator.SetFloat("MoveZ", 1f);
        animator.Update(0.1f);
        float legTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        _hub.Publish(new SkillCast(new ContentId("skill.echo-archer.crossfire"), 12f, false));
        animator.Update(0.01f);

        int upperBody = animator.GetLayerIndex("Upper Body");
        Assert.That(animator.GetCurrentAnimatorStateInfo(upperBody).IsName("Upper Body.Release"), Is.True);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(legTime));
        Assert.That(animator.GetFloat("MoveZ"), Is.EqualTo(1f));
    }

    [Test]
    public void Pool_NoInstantiate_ClearsOnBoundary()
    {
        LineRenderer[] warm = _parent.GetComponentsInChildren<LineRenderer>(true);
        Assert.That(warm.Length, Is.EqualTo(ShadeSystem.Capacity));
        for (int i = 1; i <= warm.Length; i++)
        {
            Fire(i, ShotOrigin.Crossfire, new Point(i, 0f, 0f));
        }
        Assert.That(ActiveLines(), Is.EquivalentTo(warm));

        _hub.Publish(new StageTransitionStarted(18, 0.5f));
        Assert.That(ActiveLines(), Is.Empty);
        Fire(warm.Length + 1, ShotOrigin.Crossfire, Point.Zero);
        Assert.That(ActiveLines(), Is.SubsetOf(warm));
        Assert.That(ActiveLines().Length, Is.EqualTo(1));

        _hub.Publish(new StageArrived(19, new ContentId("arena.test")));
        Assert.That(ActiveLines(), Is.Empty);
        Assert.That(_parent.GetComponentsInChildren<LineRenderer>(true), Is.EquivalentTo(warm));
    }

    private LineRenderer[] ActiveLines() =>
        _parent.GetComponentsInChildren<LineRenderer>().Where(line => line.enabled).ToArray();

    private void Fire(int id, ShotOrigin origin, Point from) =>
        _hub.Publish(new ProjectileFired(id, Ranger, 0, from, Target, 0.2f, shotOrigin: origin));
}
