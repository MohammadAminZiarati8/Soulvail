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
public sealed class ShadeViewsTests
{
    private const string Root = "Assets/_Project/";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    private GameObject _parent;
    private PlayerView _player;
    private IObjectResolver _container;
    private DomainEventHub _hub;
    private ShadeViews _views;
    private ShadeView _prefab;

    [SetUp]
    public void SetUp()
    {
        _parent = new GameObject("Shade test");
        _player = _parent.AddComponent<PlayerView>();
        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();
        _prefab = AssetDatabase.LoadAssetAtPath<ShadeView>(Root + "Prefabs/Minions/Shade.prefab");
        Assert.That(_prefab, Is.Not.Null);
        _views = new ShadeViews(_container, _hub, _prefab, _parent.transform, _player);
    }

    [TearDown]
    public void TearDown()
    {
        _views?.Dispose();
        _hub?.Dispose();
        _container?.Dispose();
        Object.DestroyImmediate(_parent);
    }

    [Test]
    public void Shade_EveryRendererUsesShadeMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/Characters/M_Shade.mat");
        var renderers = _prefab.GetComponentsInChildren<Renderer>(true);
        Assert.That(renderers.Length, Is.GreaterThan(1));
        Assert.That(renderers.SelectMany(renderer => renderer.sharedMaterials), Is.All.SameAs(material));
        Assert.That(renderers.Select(renderer => renderer.name), Does.Contain("Bow"));
        Assert.That(renderers.Select(renderer => renderer.name), Does.Not.Contain("Ranger_Quiver"));
        Assert.That(_prefab.GetComponentInChildren<Collider>(true), Is.Null);
        Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Unlit"));
        Assert.That(material.GetTag("RenderType", false), Is.EqualTo("Transparent"));
    }

    [Test]
    public void ShadeTint_NotVioletLimeRedGold()
    {
        Color tint = _prefab.GetComponentInChildren<Renderer>(true).sharedMaterial.GetColor(BaseColor);
        Assert.That(tint.b, Is.GreaterThan(tint.g));
        Assert.That(tint.g, Is.GreaterThan(2f * tint.r));
        Assert.That(tint.b, Is.LessThan(0.6f));
        Assert.That(tint.a, Is.InRange(0.2f, 0.65f));
    }

    [Test]
    public void Appeared_ShowsAtPosition_FadesIn()
    {
        var position = new Vector3N(2f, 0f, 5f);
        _hub.Publish(new ShadeAppeared(1, ShadeKind.Image, position, 6f));
        ShadeView view = Active().Single();
        Assert.That(view.transform.position, Is.EqualTo(new Vector3(2f, 0f, 5f)));
        Assert.That(Alpha(view), Is.Zero);
        _views.Step(0.075f);
        Assert.That(Alpha(view), Is.EqualTo(0.23f).Within(0.001f));
        _views.Step(0.075f);
        Assert.That(Alpha(view), Is.EqualTo(0.46f).Within(0.001f));
        var animator = view.GetComponent<Animator>();
        animator.Update(0f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(animator.GetLayerIndex("Upper Body")).IsName("Upper Body.Aim"), Is.True);
    }

    [Test]
    public void Loosed_FacesTarget_PlaysRelease()
    {
        Appear(1);
        ShadeView view = Active().Single();
        _hub.Publish(new ShadeLoosed(1, new Vector3N(4f, 2f, 0f)));
        Assert.That(Vector3.Dot(view.transform.forward, Vector3.right), Is.EqualTo(1f).Within(0.001f));
        var animator = view.GetComponent<Animator>();
        animator.Update(0f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(animator.GetLayerIndex("Upper Body")).IsName("Upper Body.Release"), Is.True);
    }

    [Test]
    public void Faded_Expired_FadesOut()
    {
        Appear(1);
        ShadeView view = Active().Single();
        _views.Step(0.15f);
        _hub.Publish(new ShadeFaded(1, Vector3N.Zero, expired: true));
        _views.Step(0.15f);
        Assert.That(Alpha(view), Is.EqualTo(0.23f).Within(0.001f));
        Assert.That(view.gameObject.activeSelf, Is.True);
        _views.Step(0.15f);
        Assert.That(view.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void Faded_NotExpired_HidesAtOnce()
    {
        Appear(1);
        _hub.Publish(new ShadeFaded(1, Vector3N.Zero, expired: false));
        Assert.That(Active(), Is.Empty);
    }

    [TestCase(ShotOrigin.Shade)]
    [TestCase(ShotOrigin.Echo)]
    public void Trail_ByShotOrigin(ShotOrigin origin)
    {
        var arrow = AssetDatabase.LoadAssetAtPath<ProjectileView>(Root + "Prefabs/Projectiles/Arrow.prefab");
        using var shots = new ProjectileViews(_container, arrow, _parent.transform, _hub, prewarm: 1);
        Fire(1, ShotOrigin.Ranger);
        Assert.That(shots.TryGet(1, out ProjectileView original), Is.True);
        var trail = original.GetComponentInChildren<TrailRenderer>();
        Color ranger = trail.colorGradient.Evaluate(0f);

        _hub.Publish(new ProjectileImpacted(1, Vector3N.Zero, hit: false));
        Fire(2, origin);
        Assert.That(shots.TryGet(2, out ProjectileView shade), Is.True);
        Assert.That(shade, Is.SameAs(original));
        Color smoky = trail.colorGradient.Evaluate(0f);
        Assert.That(smoky.b, Is.LessThan(ranger.b));
        Assert.That(smoky.a, Is.LessThan(ranger.a));

        _hub.Publish(new ProjectileImpacted(2, Vector3N.Zero, hit: false));
        Fire(3, ShotOrigin.Ranger);
        Assert.That(trail.colorGradient.Evaluate(0f), Is.EqualTo(ranger));

        void Fire(int id, ShotOrigin shotOrigin)
        {
            _hub.Publish(new ProjectileFired(id, default, 0, Vector3N.Zero, Vector3N.UnitX, 1f, shotOrigin: shotOrigin));
        }
    }

    [Test]
    public void Cast_UpperBodyRelease_MovementKept()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Player/Bodies/Ranger.prefab");
        GameObject body = Object.Instantiate(source, _parent.transform);
        var animator = body.GetComponent<Animator>();
        var view = body.GetComponent<RangerAnimatorView>();
        view.Construct(_hub);

        animator.SetFloat("MoveZ", 1f);
        animator.Update(0.1f);
        float legTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        _hub.Publish(new SkillCast(new ContentId("skill.echo-archer.mirror-image"), 14f, false));
        animator.Update(0.01f);

        Assert.That(animator.GetCurrentAnimatorStateInfo(animator.GetLayerIndex("Upper Body")).IsName("Upper Body.Release"), Is.True);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(legTime));
        Assert.That(animator.GetFloat("MoveZ"), Is.EqualTo(1f));
    }

    [Test]
    public void Pool_EightWarm_NoInstantiate()
    {
        ShadeView[] warm = _parent.GetComponentsInChildren<ShadeView>(true);
        Assert.That(warm.Length, Is.EqualTo(8));
        Assert.That(Active(), Is.Empty);

        for (int id = 1; id <= 8; id++)
        {
            Appear(id);
        }
        Assert.That(Active(), Is.EquivalentTo(warm));

        _hub.Publish(new ShadeFaded(1, Vector3N.Zero, expired: true));
        Appear(9);
        Assert.That(Active(), Is.EquivalentTo(warm));
        Assert.That(_parent.GetComponentsInChildren<ShadeView>(true), Is.EquivalentTo(warm));
    }

    [Test]
    public void StageBoundary_HidesAll()
    {
        Appear(1);
        Appear(2);
        _hub.Publish(new ShadeFaded(2, Vector3N.Zero, expired: true));
        _hub.Publish(new StageTransitionStarted(1, 0.5f));
        Assert.That(Active(), Is.Empty);

        Appear(3);
        _hub.Publish(new StageArrived(2, new ContentId("arena.test")));
        Assert.That(Active(), Is.Empty);
    }

    private void Appear(int id) => _hub.Publish(new ShadeAppeared(id, ShadeKind.Image, Vector3N.Zero, 6f));

    private ShadeView[] Active() => _parent.GetComponentsInChildren<ShadeView>();

    private static float Alpha(ShadeView view)
    {
        var block = new MaterialPropertyBlock();
        view.GetComponentInChildren<Renderer>().GetPropertyBlock(block);
        return block.GetColor(BaseColor).a;
    }
}
