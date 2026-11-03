using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Num = System.Numerics;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class ThornweaverViewTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Thornweaver.prefab";
    private const string RootMaterialPath = "Assets/_Project/Materials/Enemies/M_ThornRoot.mat";
    private static readonly ContentId Weaver = new ContentId("enemy.thornweaver");
    private GameObject _root;
    private DomainEventHub _hub;
    private ThornweaverView _view;
    private Animator _animator;
    private LineRenderer _line;
    private Renderer _crown;

    [SetUp]
    public void Rent()
    {
        _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        _hub = new DomainEventHub();
        _root.GetComponent<EnemyView>().Bind(17, Vector3.zero);
        _view = _root.GetComponent<ThornweaverView>();
        _view.Bind(17, _hub);
        _animator = _root.GetComponentInChildren<Animator>();
        _animator.Rebind();
        _animator.Update(0f);
        _root.GetComponentInChildren<EnemyAnimatorView>().Construct(_hub);
        var serialized = new SerializedObject(_view);
        _line = serialized.FindProperty("_root").objectReferenceValue as LineRenderer;
        _crown = serialized.FindProperty("_crown").objectReferenceValue as Renderer;
    }

    [TearDown]
    public void Return()
    { _view.OnDespawn(); Object.DestroyImmediate(_root); _hub.Dispose(); }

    // Wall 4 runs 6 m along +X from (2, 0, 8): its midpoint is (5, 0, 8).
    private static ThornArmed Wall(int id, int owner) =>
        new ThornArmed(id, new Num.Vector3(2f, 0f, 8f), Num.Vector2.UnitX, 6f, 0.6f, 0.8f, owner);

    [Test]
    public void Telegraph_PlaysCastAndGlows()
    {
        _hub.Publish(new EnemyTelegraph(99, 0.9f));
        _view.Step(0.9f);
        Assert.That(_view.CastCount, Is.Zero, "Another id is ignored.");
        Assert.That(_view.CrownGlow, Is.Zero);
        Assert.That(_crown.enabled, Is.False, "A dark crown draws nothing.");
        Renderer body = new SerializedObject(_root.GetComponent<EnemyHitFeedback>()).FindProperty("_renderer").objectReferenceValue as Renderer;
        Assert.That(_crown == body, Is.False, "The hit flash keeps the body's emission to itself.");

        _hub.Publish(new EnemyTelegraph(17, 0.9f));
        _view.Step(0.45f);
        Assert.That(_view.CrownGlow, Is.EqualTo(0.5f).Within(0.001f));
        _view.Step(0.45f);
        Assert.That(_view.CastCount, Is.EqualTo(1));
        Assert.That(_view.CrownGlow, Is.EqualTo(1f).Within(0.001f));
        Assert.That(_crown.enabled, Is.True);
        _animator.Update(0.1f);
        Assert.That(
            _animator.GetCurrentAnimatorStateInfo(0).IsName("Cast") || _animator.GetNextAnimatorStateInfo(0).IsName("Cast"),
            Is.True);
    }

    [Test]
    public void ThornArmed_OwnDrawsRoot()
    {
        _hub.Publish(Wall(3, 40));
        _hub.Publish(Wall(5, -1));
        _view.Step(0.25f);
        Assert.That(_view.RootVisible, Is.False, "Thornheart's wall and an unowned wall draw no root.");

        _hub.Publish(new EnemyTelegraph(17, 0.9f));
        _view.Step(0.9f);
        _hub.Publish(Wall(4, 17));
        Assert.That(_view.RootVisible, Is.True);
        Assert.That(_view.RootEnd.x, Is.EqualTo(5f).Within(0.001f));
        Assert.That(_view.RootEnd.z, Is.EqualTo(8f).Within(0.001f));
        Assert.That(_view.RootEnd.y, Is.GreaterThan(0f).And.LessThan(0.1f), "On the floor, lifted.");

        _view.Step(0.125f);
        Assert.That(_view.RootGrowth, Is.EqualTo(0.5f).Within(0.001f));
        _view.Step(0.125f);
        Assert.That(_view.RootGrowth, Is.EqualTo(1f).Within(0.001f));
        Vector3 tip = _line.GetPosition(_line.positionCount - 1);
        Assert.That(Vector3.Distance(tip, _view.RootEnd), Is.LessThan(0.001f));
        Assert.That(Vector3.Distance(_line.GetPosition(0), _root.transform.position), Is.LessThan(0.1f), "From its feet.");
        Assert.That(_view.CrownGlow, Is.Zero, "The crown has dropped back.");

        _hub.Publish(new ThornRose(3, false));
        Assert.That(_view.RootVisible, Is.True, "Another wall rising leaves this root.");
        _hub.Publish(new ThornRose(4, false));
        Assert.That(_view.RootVisible, Is.False, "Gone when the wall stands.");
        Assert.That(_line.enabled, Is.False);
    }

    [Test]
    public void Root_IsNotADangerColour()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(RootMaterialPath);
        Assert.That(_line.sharedMaterial == material, Is.True);
        Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Unlit"));
        Color colour = material.GetColor("_BaseColor");
        Assert.That(Palette.IsDanger(colour), Is.False);
        Color.RGBToHSV(colour, out float hue, out float saturation, out _);
        Assert.That(hue * 360f, Is.InRange(70f, 140f), "Moss green.");
        Assert.That(saturation, Is.GreaterThan(0.3f));
        Assert.That(colour.g, Is.GreaterThan(colour.r).And.GreaterThan(colour.b));
    }

    [Test]
    public void Death_MidCastStopsGlow()
    {
        _hub.Publish(new EnemyTelegraph(17, 0.9f));
        _view.Step(0.45f);
        Assert.That(_view.CrownGlow, Is.GreaterThan(0f));
        _hub.Publish(new EnemyDied(17, Weaver, Num.Vector3.Zero));
        _view.Step(0.45f);
        Assert.That(_view.CrownGlow, Is.Zero);
        Assert.That(_crown.enabled, Is.False);
        _animator.Update(0.1f);
        Assert.That(_animator.GetBool("Dead"), Is.True, "The death takes the clip from the cast.");

        _hub.Publish(Wall(6, 17));
        Assert.That(_view.RootVisible, Is.False, "A dead weaver arms nothing new.");
    }

    [Test]
    public void Death_RootFadesWithItsWall()
    {
        _hub.Publish(Wall(4, 17));
        _view.Step(0.25f);
        _hub.Publish(new EnemyDied(17, Weaver, Num.Vector3.Zero));
        _view.Step(0.1f);
        Assert.That(_view.RootVisible, Is.True, "A drawn root outlives its weaver.");
        _hub.Publish(new ThornWithered(4));
        Assert.That(_view.RootVisible, Is.False);
    }

    [Test]
    public void Despawn_ResetsEverything()
    {
        _hub.Publish(new EnemyTelegraph(17, 0.9f));
        _view.Step(0.45f);
        _hub.Publish(Wall(4, 17));
        _view.Step(0.1f);
        _root.GetComponent<EnemyView>().OnDespawn();
        _root.SetActive(false);
        Assert.That(_line.enabled, Is.False);
        Assert.That(_line.positionCount, Is.Zero);
        Assert.That(_view.CrownGlow, Is.Zero);
        Assert.That(_crown.enabled, Is.False);
        Assert.That(_view.CastCount, Is.Zero);
        Assert.That(_hub.SubscriberCount<ThornArmed>(), Is.Zero);

        _root.SetActive(true);
        _root.GetComponent<EnemyView>().OnSpawn();
        _root.GetComponent<EnemyView>().Bind(18, Vector3.zero);
        _hub.Publish(Wall(7, 17));
        Assert.That(_view.RootVisible, Is.False, "The old id is forgotten.");
        _hub.Publish(Wall(8, 18));
        Assert.That(_view.RootVisible, Is.True, "The next life draws its own.");
        _animator.Update(0.1f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Cast"), Is.False);
    }
}
