using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Point = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class WhirlwindViewTests
{
    private const string Prefabs = "Assets/_Project/Prefabs/";
    private const string Spine = "Rig_Medium/root/hips/spine";
    private DomainEventHub _hub;
    private WhirlwindView _view;
    private GameObject _player;
    private int _spins;

    [SetUp]
    public void SetUp()
    {
        _hub = new DomainEventHub();
        _spins = 0;
    }

    [TearDown]
    public void TearDown()
    {
        if (_view != null)
        {
            _view.Dispose();
            Object.DestroyImmediate(_view.gameObject);
        }

        if (_player != null)
        {
            Object.DestroyImmediate(_player);
        }

        _hub.Dispose();
    }

    [Test]
    public void View_OneSwirlPerRing_AtThePlayer()
    {
        WarmPool();
        float now = Time.time;
        _hub.Publish(new WhirlwindRingFired(new Point(4f, 0f, -1f)));
        Transform first = ActiveSwirl();
        Assert.That(first.position, Is.EqualTo(new Vector3(4f, 0.035f, -1f)));
        Assert.That(first.GetComponentsInChildren<LineRenderer>(), Has.Length.EqualTo(3));

        _view.Step(now + 0.249f);
        Assert.That(ActiveSwirl(), Is.SameAs(first));
        _view.Step(now + 0.25f);
        Assert.That(first.gameObject.activeSelf, Is.False);

        _hub.Publish(new WhirlwindRingFired(new Point(8f, 0f, 3f)));
        Assert.That(ActiveSwirl().position, Is.EqualTo(new Vector3(8f, 0.035f, 3f)));
        Assert.That(_spins, Is.EqualTo(2));
    }

    [Test]
    public void View_Pooled_ClearsOnBoundary()
    {
        WarmPool();
        var pooled = _view.transform.Cast<Transform>().ToArray();
        Assert.That(pooled, Has.Length.EqualTo(3));
        for (int i = 0; i < 6; i++)
        {
            _hub.Publish(new WhirlwindRingFired(Point.Zero));
            Assert.That(pooled, Does.Contain(ActiveSwirl()));
            _hub.Publish(new StageCleared(1, Point.Zero, default));
            Assert.That(pooled.Any(swirl => swirl.gameObject.activeSelf), Is.False);
        }

        Assert.That(_view.transform.Cast<Transform>(), Is.EqualTo(pooled));
        Assert.That(_spins, Is.EqualTo(6));
    }

    [Test]
    public void Animator_SpinOnUpperLayer_LegsKeepRunning()
    {
        GameObject body = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "Player/Bodies/Ranger.prefab");
        Assert.That(body, Is.Not.Null);
        _player = new GameObject("Whirlwind test player");
        PlayerView player = _player.AddComponent<PlayerView>();
        typeof(PlayerView).GetField("_velocity", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(player, Vector3.forward * 3f);

        var model = Object.Instantiate(body, _player.transform);
        var view = model.GetComponent<RangerAnimatorView>();
        var animator = model.GetComponentInChildren<Animator>();
        view.Construct(_hub);
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.Update(0f);
        view.Step(0.25f);
        animator.Update(0.1f);

        int upper = animator.GetLayerIndex(RangerAnimatorView.UpperBodyLayer);
        Transform spine = animator.transform.Find(Spine);
        Transform thigh = animator.transform.Find("Rig_Medium/root/hips/upperleg.l");
        Quaternion startingLeg = thigh.localRotation;
        AnimatorStateInfo running = animator.GetCurrentAnimatorStateInfo(0);

        _hub.Publish(new WhirlwindRingFired(Point.Zero));
        animator.Update(0f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(upper).IsName("Upper Body.Whirlwind"), Is.True);
        Assert.That(animator.GetLayerWeight(upper), Is.EqualTo(1f));
        Quaternion firstPose = spine.localRotation;
        view.Step(0.125f);
        animator.Update(0.125f);

        Assert.That(Quaternion.Angle(firstPose, spine.localRotation), Is.GreaterThan(90f));
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).fullPathHash, Is.EqualTo(running.fullPathHash));
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(running.normalizedTime));
        Assert.That(Quaternion.Angle(startingLeg, thigh.localRotation), Is.GreaterThan(1f));

        _hub.Publish(new StageTransitionStarted(2, 1f));
        animator.Update(0f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(upper).IsName("Upper Body.Whirlwind"), Is.False);
        Assert.That(animator.GetLayerWeight(upper), Is.Zero);
    }

    private void WarmPool()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<WhirlwindView>(Prefabs + "Vfx/VFX_Whirlwind.prefab");
        Assert.That(prefab, Is.Not.Null);
        _view = Object.Instantiate(prefab);
        _view.Construct(_hub, show =>
        {
            if (show)
            {
                _spins++;
            }
        });
    }

    private Transform ActiveSwirl() =>
        _view.transform.Cast<Transform>().Single(swirl => swirl.gameObject.activeSelf);
}
