using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Num = System.Numerics;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class ShamanViewTests
{
    private GameObject _root;
    private DomainEventHub _hub;
    private ShamanView _view;
    private EnemyHealthBar _bar;
    private Animator _animator;
    private Transform _ring;

    [SetUp]
    public void Rent()
    {
        _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Shaman.prefab"));
        _hub = new DomainEventHub();
        _view = _root.GetComponent<ShamanView>();
        _view.Construct(_hub);
        _root.GetComponent<EnemyView>().Bind(17, Vector3.zero);
        _bar = _root.GetComponent<EnemyHealthBar>();
        _bar.Construct(_hub);
        _bar.Bind(17, false);
        _bar.ConfigureStatuses(null, null);
        _animator = _root.GetComponentInChildren<Animator>();
        _animator.Rebind();
        _animator.Update(0f);
        _root.GetComponentInChildren<EnemyAnimatorView>().Construct(_hub);
        _ring = new SerializedObject(_view).FindProperty("_ring").objectReferenceValue as Transform;
    }

    [TearDown]
    public void Return()
    { _view.OnDespawn(); Object.DestroyImmediate(_root); _hub.Dispose(); }

    [Test]
    public void ShamanView_CastPlaysForItsOwnId()
    {
        _hub.Publish(new ShamanCast(99, .6f));
        Assert.That(_view.CastCount, Is.Zero);
        _hub.Publish(new ShamanCast(17, .6f));
        _view.Step(.3f);
        Assert.That(_view.CastCount, Is.EqualTo(1));
        Assert.That(_view.SeedGlow, Is.EqualTo(.5f).Within(.001f));
        _animator.Update(.1f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Cast") || _animator.GetNextAnimatorStateInfo(0).IsName("Cast"), Is.True);
    }

    [Test]
    public void ShamanView_PulseGrowsTheRingToTheRadius()
    {
        _root.transform.localScale = Vector3.one * 1.27f;
        _hub.Publish(new ShamanPulsed(17, new Num.Vector3(3, 0, 4), 8, 2));
        _view.Step(.2f);
        Assert.That(_ring.lossyScale.x, Is.EqualTo(4f).Within(.001f));
        Assert.That(_ring.position, Is.EqualTo(new Vector3(3, .035f, 4)));
        _view.Step(.2f);
        Assert.That(_ring.lossyScale.x, Is.EqualTo(8f).Within(.001f));
        Assert.That(_ring.gameObject.activeSelf, Is.False);
        var block = new MaterialPropertyBlock();
        _ring.GetComponent<Renderer>().GetPropertyBlock(block);
        Assert.That(block.GetColor("_BaseColor").a, Is.Zero);
    }

    [Test]
    public void ShamanView_PulseIsNoTelegraph()
    {
        Assert.That(_ring.GetComponents<Component>(), Has.Length.EqualTo(2), "Only a transform and a line renderer.");
        Material material = _ring.GetComponent<Renderer>().sharedMaterial;
        Assert.That(material.GetTexture("_BaseMap"), Is.Null);
        Color c = material.GetColor("_BaseColor");
        Assert.That(c.g, Is.GreaterThan(c.r));
        Assert.That(c.b, Is.LessThan(c.g * .6f));
        Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo(1f));
        Assert.That(_root.GetComponentInChildren<Animator>().applyRootMotion, Is.False);
    }

    [Test]
    public void HealthBar_RisesOnEnemyHealed()
    {
        _hub.Publish(new EnemyDamaged(17, 30, .5f, false));
        _hub.Publish(new EnemyHealed(17, 6, .6f));
        Assert.That(_bar.GhostFraction, Is.EqualTo(.6f));
        var fill = new SerializedObject(_bar).FindProperty("_fill").objectReferenceValue as UnityEngine.UI.Image;
        Assert.That(fill.fillAmount, Is.EqualTo(.6f));
    }

    [Test]
    public void StatusView_ShowsAndClearsHaste()
    {
        _hub.Publish(new EnemyStatusChanged(17, EnemyStatus.Hasted, 3.5f));
        var icon = _bar.StatusIcon(EnemyStatus.Hasted);
        Assert.That(icon.gameObject.activeSelf, Is.True);
        Assert.That(icon.GetComponentsInChildren<UnityEngine.UI.Image>().Count(i => i.name == "Haste chevron"), Is.EqualTo(2));
        _hub.Publish(new EnemyStatusChanged(17, EnemyStatus.Hasted, 0f));
        Assert.That(icon.gameObject.activeSelf, Is.False);
    }

    [TestCase(1.2f, false)]
    [TestCase(2f, true)]
    public void AnimatorView_RunsAboveOnePointTwo(float speed, bool running)
    {
        typeof(EnemyView).GetField("_velocity", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_root.GetComponent<EnemyView>(), Vector3.forward * speed);
        _root.GetComponentInChildren<EnemyAnimatorView>().Step(.1f);
        Assert.That(_animator.GetBool("Run"), Is.EqualTo(running));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ShamanView_DespawnMidCastLeavesNothing(bool pulse)
    {
        _hub.Publish(new ShamanCast(17, .6f));
        _view.Step(.3f);
        if (pulse)
        { _hub.Publish(new ShamanPulsed(17, Num.Vector3.Zero, 8, 2)); _view.Step(.1f); }
        _root.GetComponent<EnemyView>().OnDespawn();
        _root.SetActive(false);
        Assert.That(_view.SeedGlow, Is.Zero);
        Assert.That(_ring.gameObject.activeSelf, Is.False);
        Assert.That(_ring.parent, Is.EqualTo(_root.transform));
        Assert.That(_hub.SubscriberCount<ShamanCast>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<ShamanPulsed>(), Is.Zero);
        _root.SetActive(true);
        _root.GetComponent<EnemyView>().OnSpawn();
        _root.GetComponent<EnemyView>().Bind(18, Vector3.zero);
        _hub.Publish(new ShamanCast(17, .6f));
        Assert.That(_view.CastCount, Is.Zero);
        _hub.Publish(new ShamanCast(18, .6f));
        Assert.That(_view.CastCount, Is.EqualTo(1));
    }
}
