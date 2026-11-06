using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using VContainer;
using Vector3N = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class ShadeBurstViewTests
{
    private GameObject _root;
    private DomainEventHub _hub;
    private IObjectResolver _container;
    private ShadeViews _views;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("Shade burst test");
        _hub = new DomainEventHub();
        _container = new ContainerBuilder().Build();
        var shade = AssetDatabase.LoadAssetAtPath<ShadeView>("Assets/_Project/Prefabs/Minions/Shade.prefab");
        var burst = AssetDatabase.LoadAssetAtPath<ShadeBurstView>("Assets/_Project/Prefabs/Vfx/VFX_ShadeBurst.prefab");
        Assert.That(burst, Is.Not.Null);
        _views = new ShadeViews(_container, _hub, shade, _root.transform, _root.AddComponent<PlayerView>(),
            burst, _root.transform);
    }

    [TearDown]
    public void TearDown()
    {
        _views?.Dispose();
        _container.Dispose();
        _hub.Dispose();
        Object.DestroyImmediate(_root);
    }

    [Test]
    public void DecoyShade_LowBowPose_NoAim()
    {
        _hub.Publish(new ShadeAppeared(1, ShadeKind.Decoy, Vector3N.Zero, 3f));
        var shade = _root.GetComponentInChildren<ShadeView>();
        var animator = shade.GetComponent<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Update(0.1f);
        Assert.That(animator.GetBool("Aiming"), Is.False);
        Assert.That(animator.GetLayerWeight(animator.GetLayerIndex("Upper Body")), Is.Zero);
        float idleBowHeight = BowHeight(shade);

        _hub.Publish(new ShadeLoosed(1, Vector3N.UnitX));
        Assert.That(shade.transform.forward, Is.EqualTo(Vector3.forward));

        _views.Step(0.15f);
        float fullAlpha = Alpha(shade);
        _views.Step(0.5f);
        Assert.That(Alpha(shade), Is.InRange(fullAlpha * 0.69f, fullAlpha * 0.71f));

        _hub.Publish(new ShadeFaded(1, Vector3N.Zero, expired: false));
        _hub.Publish(new ShadeAppeared(2, ShadeKind.Image, Vector3N.Zero, 6f));
        Assert.That(_root.GetComponentInChildren<ShadeView>(), Is.SameAs(shade));
        animator.Update(0.1f);
        Assert.That(animator.GetBool("Aiming"), Is.True);
        Assert.That(BowHeight(shade), Is.GreaterThan(idleBowHeight));

        static float BowHeight(ShadeView view) =>
            view.GetComponentsInChildren<Renderer>().Single(renderer => renderer.name == "Bow").transform.position.y;

        static float Alpha(ShadeView view)
        {
            var block = new MaterialPropertyBlock();
            view.GetComponentInChildren<Renderer>().GetPropertyBlock(block);
            return block.GetColor("_BaseColor").a;
        }
    }

    [TestCase(3f)]
    [TestCase(1.75f)]
    public void Burst_RingMatchesRadius(float radius)
    {
        _hub.Publish(new ShadeBurst(new Vector3N(2f, 0.2f, 5f), radius, slows: false));
        ShadeBurstView view = Active().Single();
        Assert.That(view.transform.position, Is.EqualTo(new Vector3(2f, 0.2f + GroundLift.Height, 5f)));
        LineRenderer edge = Edge(view);
        for (int i = 0; i < edge.positionCount; i++)
        {
            Vector3 point = edge.transform.TransformPoint(edge.GetPosition(i)) - view.transform.position;
            Assert.That(point.y, Is.Zero.Within(0.001f));
            Assert.That(point.magnitude + edge.startWidth * 0.5f, Is.EqualTo(radius).Within(0.001f));
        }
        Assert.That(view.GetComponentInChildren<ParticleSystem>().main.playOnAwake, Is.False);
        _views.Step(0.2f);
        Assert.That(edge.startColor.a, Is.EqualTo(0.41f).Within(0.01f));
        _views.Step(0.2f);
        Assert.That(Active(), Is.Empty);
    }

    [Test]
    public void Burst_Slows_EdgeTinted()
    {
        _hub.Publish(new ShadeBurst(Vector3N.Zero, ShadeSystem.BurstRadius, slows: true));
        ShadeBurstView view = Active().Single();
        Color slowEdge = Edge(view).startColor;
        Color smoke = Smoke(view).startColor;
        Assert.That((Color32)slowEdge, Is.EqualTo(new Color32(255, 253, 238, 235)));

        _views.Step(0.4f);
        _hub.Publish(new ShadeBurst(Vector3N.Zero, ShadeSystem.BurstRadius, slows: false));
        Assert.That(Active().Single(), Is.SameAs(view));
        Assert.That(Edge(view).startColor, Is.Not.EqualTo(slowEdge));
        Assert.That(Smoke(view).startColor, Is.EqualTo(smoke));
    }

    [Test]
    public void Pool_NoInstantiate_ClearsOnBoundary()
    {
        ShadeBurstView[] warm = _root.GetComponentsInChildren<ShadeBurstView>(true);
        Assert.That(warm.Length, Is.EqualTo(8));
        Assert.That(Active(), Is.Empty);

        Publish(0);
        ShadeBurstView oldest = Active().Single();
        _views.Step(0.1f);
        for (int i = 1; i <= 8; i++)
        {
            Publish(i);
        }

        Assert.That(Active(), Is.EquivalentTo(warm));
        Assert.That(_root.GetComponentsInChildren<ShadeBurstView>(true), Is.EquivalentTo(warm));
        Assert.That(oldest.transform.position.x, Is.EqualTo(8f));

        _hub.Publish(new StageTransitionStarted(1, 0.5f));
        Assert.That(Active(), Is.Empty);
        Publish(0);
        _hub.Publish(new StageArrived(2, new ContentId("arena.test")));
        Assert.That(Active(), Is.Empty);

        void Publish(int i) => _hub.Publish(new ShadeBurst(new Vector3N(i, 0f, 0f), ShadeSystem.BurstRadius, slows: false));
    }

    private ShadeBurstView[] Active() => _root.GetComponentsInChildren<ShadeBurstView>();

    private static LineRenderer Edge(ShadeBurstView view) => view.transform.Find("Edge").GetComponent<LineRenderer>();

    private static LineRenderer Smoke(ShadeBurstView view) => view.transform.Find("Smoke").GetComponent<LineRenderer>();
}
