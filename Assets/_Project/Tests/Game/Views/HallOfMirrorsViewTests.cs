using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Point = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class HallOfMirrorsViewTests
{
    private const string Prefabs = "Assets/_Project/Prefabs/";
    private DomainEventHub _hub;
    private GameObject _root;

    [SetUp]
    public void SetUp() => _hub = new DomainEventHub();

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_root);
        _hub.Dispose();
    }

    [TestCase(3f, 5f)]
    [TestCase(4f, 7f)]
    public void Ring_MatchesRadius_HoldsForSeconds(float radius, float seconds)
    {
        HallOfMirrorsView view = WarmPool(out LineRenderer ground, out LineRenderer shimmer);
        var center = new Vector3(5f, 2f, -3f);
        Assert.That(ground.enabled, Is.False);
        _hub.Publish(new HallRaised(new Point(center.x, center.y, center.z), radius, seconds));
        Assert.That(ground.enabled && shimmer.enabled, Is.True);
        Assert.That(ground.loop, Is.True);
        for (int i = 0; i < ground.positionCount; i++)
        {
            Vector3 position = ground.transform.TransformPoint(ground.GetPosition(i));
            Assert.That(Vector2.Distance(new Vector2(position.x, position.z), new Vector2(center.x, center.z)),
                Is.EqualTo(radius).Within(0.0001f));
        }

        float startHeight = shimmer.transform.position.y;
        view.Step(0.1f);
        Assert.That(shimmer.transform.position.y, Is.GreaterThan(startHeight));
        view.Step(0.1f);
        Assert.That(shimmer.enabled, Is.False);
        float holdOpacity = ground.startColor.a;
        Assert.That(holdOpacity, Is.GreaterThan(0f));

        view.Step(seconds - 0.21f);
        Assert.That(ground.startColor.a, Is.EqualTo(holdOpacity));
        Assert.That(view.transform.position.x, Is.EqualTo(center.x));
        Assert.That(view.transform.position.z, Is.EqualTo(center.z));
        view.Step(0.16f);
        Assert.That(ground.enabled, Is.True);
        Assert.That(ground.startColor.a, Is.InRange(0.01f, holdOpacity - 0.01f));
        view.Step(0.16f);
        Assert.That(ground.enabled, Is.False);
    }

    [Test]
    public void Ring_ClearsOnBoundary()
    {
        HallOfMirrorsView view = WarmPool(out LineRenderer ground, out LineRenderer shimmer);
        _hub.Publish(new HallRaised(Point.Zero, 3f, 5f));
        _hub.Publish(new StageCleared(20, Point.Zero, default));
        Assert.That(ground.enabled || shimmer.enabled, Is.False);

        _hub.Publish(new HallRaised(Point.One, 3f, 7f));
        Assert.That(_root.transform.GetChild(0), Is.SameAs(view.transform));
        Assert.That(_root.transform.childCount, Is.EqualTo(1));
        Assert.That(ground.enabled && shimmer.enabled, Is.True);
        _hub.Publish(new StageTransitionStarted(21, 1f));
        Assert.That(ground.enabled || shimmer.enabled, Is.False);
    }

    [Test]
    public void Cast_UpperBodyRaise_MovementKept()
    {
        _root = new GameObject("Hall test player");
        var player = _root.AddComponent<PlayerView>();
        Vector3 runningVelocity = Vector3.forward * 3f;
        typeof(PlayerView).GetField("_velocity", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(player, runningVelocity);
        var body = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "Player/Bodies/Ranger.prefab");
        var model = Object.Instantiate(body, _root.transform);
        var view = model.GetComponent<RangerAnimatorView>();
        var animator = model.GetComponentInChildren<Animator>();
        view.Construct(_hub);
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.Update(0f);
        view.Step(0.25f);
        animator.Update(0.1f);
        int upper = animator.GetLayerIndex(RangerAnimatorView.UpperBodyLayer);
        AnimatorStateInfo running = animator.GetCurrentAnimatorStateInfo(0);

        _hub.Publish(new SkillCast(new ContentId("skill.echo-archer.hall-of-mirrors"), 30f, false));
        animator.Update(0f);
        view.Step(0.29f);
        animator.Update(0.29f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(upper).IsName("Upper Body.Cast Up"), Is.True);
        Assert.That(animator.GetLayerWeight(upper), Is.EqualTo(1f));
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).fullPathHash, Is.EqualTo(running.fullPathHash));
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(running.normalizedTime));
        Assert.That(player.Velocity, Is.EqualTo(runningVelocity));

        view.Step(0.02f);
        animator.Update(0.02f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(upper).IsName("Upper Body.Cast Up"), Is.False);
        Assert.That(animator.GetLayerWeight(upper), Is.Zero);
    }

    private HallOfMirrorsView WarmPool(out LineRenderer ground, out LineRenderer shimmer)
    {
        _root = new GameObject("Hall test root");
        _root.SetActive(false);
        var prefab = AssetDatabase.LoadAssetAtPath<HallOfMirrorsView>(Prefabs + "Vfx/VFX_HallRing.prefab");
        Assert.That(prefab, Is.Not.Null);
        var view = Object.Instantiate(prefab, _root.transform, false);
        view.Construct(_hub);
        _root.SetActive(true);
        ground = view.GetComponent<LineRenderer>();
        shimmer = view.transform.GetChild(0).GetComponent<LineRenderer>();
        return view;
    }
}
