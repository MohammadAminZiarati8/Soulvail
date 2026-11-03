using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class StalkerViewTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Stalker.prefab";
    private const string CloakPath = "Assets/_Project/Materials/Enemies/M_StalkerCloak.mat";
    private GameObject _root;
    private DomainEventHub _hub;
    private StalkerView _view;
    private EnemyHealthBar _bar;
    private Renderer _body;
    private Material _cloak;
    private Material _live;

    [SetUp]
    public void Rent()
    {
        _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        _hub = new DomainEventHub();
        _root.GetComponent<EnemyView>().Bind(17, Vector3.zero);
        _bar = _root.GetComponent<EnemyHealthBar>();
        _bar.Construct(_hub);
        _bar.Bind(17, false);
        _bar.ConfigureStatuses(null, null);
        _view = _root.GetComponent<StalkerView>();
        _view.Bind(17, _hub, _bar);
        var serialized = new SerializedObject(_view);
        _body = serialized.FindProperty("_body").objectReferenceValue as Renderer;
        _cloak = serialized.FindProperty("_cloak").objectReferenceValue as Material;
        _live = _body.sharedMaterial;
    }

    [TearDown]
    public void Return()
    { _view.OnDespawn(); Object.DestroyImmediate(_root); _hub.Dispose(); }

    [Test]
    public void Hidden_BlendsToCloak()
    {
        _hub.Publish(new EnemyHiddenChanged(99, true));
        _view.Step(.3f);
        Assert.That(_view.CloakAmount, Is.Zero, "Another id is ignored.");

        _hub.Publish(new EnemyHiddenChanged(17, true));
        _view.Step(.15f);
        Assert.That(_view.CloakAmount, Is.EqualTo(.5f).Within(.001f));
        _view.Step(.15f);
        Assert.That(_view.CloakAmount, Is.EqualTo(1f).Within(.001f));
        Assert.That(_body.sharedMaterial == _cloak, Is.True);

        _hub.Publish(new EnemyHiddenChanged(17, false));
        _view.Step(.075f);
        Assert.That(_view.CloakAmount, Is.EqualTo(.5f).Within(.001f));
        _view.Step(.075f);
        Assert.That(_view.CloakAmount, Is.Zero);
        Assert.That(_body.sharedMaterial == _live, Is.True, "A seen stalker draws its own material.");
    }

    [Test]
    public void Cloak_NeverFullyClear()
    {
        Assert.That(_cloak == AssetDatabase.LoadAssetAtPath<Material>(CloakPath), Is.True);
        Assert.That(_cloak.shader.name, Is.EqualTo("Soulvail/Cloak"));
        Assert.That(_cloak.GetFloat("_RimAlpha"), Is.GreaterThanOrEqualTo(.25f));
        Assert.That(_cloak.GetFloat("_BodyAlpha"), Is.GreaterThanOrEqualTo(.08f));
        Assert.That(_cloak.GetFloat("_ShimmerSpeed"), Is.GreaterThan(0f), "A still stalker still shimmers.");
        Assert.That(_cloak.GetTexture("_BaseMap") == _live.GetTexture("_BaseMap"), Is.True, "Atlas-sampled.");

        _hub.Publish(new EnemyHiddenChanged(17, true));
        _view.Step(.5f);
        var block = new MaterialPropertyBlock();
        _body.GetPropertyBlock(block);
        Assert.That(block.GetFloat("_Cloak"), Is.EqualTo(1f));
    }

    [Test]
    public void Hidden_HidesTheBar()
    {
        _hub.Publish(new EnemyDamaged(17, 4f, .6f, false));
        Assert.That(_bar.IsShown, Is.True);

        _hub.Publish(new EnemyHiddenChanged(17, true));
        Assert.That(_bar.IsShown, Is.False);
        Assert.That(_bar.GetComponentInChildren<CanvasGroup>().alpha, Is.Zero);

        _hub.Publish(new EnemyHiddenChanged(17, false));
        Assert.That(_bar.IsShown, Is.True);
        var fill = new SerializedObject(_bar).FindProperty("_fill").objectReferenceValue as UnityEngine.UI.Image;
        Assert.That(fill.fillAmount, Is.EqualTo(.6f).Within(.001f));
    }

    [Test]
    public void Telegraph_RevealsAndFlashes()
    {
        _hub.Publish(new EnemyHiddenChanged(17, true));
        _view.Step(.3f);
        _hub.Publish(new EnemyTelegraph(17, .5f));
        Assert.That(_view.CloakAmount, Is.Zero, "Snaps, never blends.");
        Assert.That(_view.FlashRemaining, Is.EqualTo(.1f).Within(.001f));
        var block = new MaterialPropertyBlock();
        _body.GetPropertyBlock(block);
        Assert.That(block.GetFloat("_Flash"), Is.EqualTo(1f));
        Assert.That(block.GetFloat("_Cloak"), Is.Zero);

        _view.Step(.1f);
        Assert.That(_view.FlashRemaining, Is.Zero);
        Assert.That(_body.sharedMaterial == _live, Is.True);
    }

    [Test]
    public void Damaged_DropsCloak()
    {
        _hub.Publish(new EnemyHiddenChanged(17, true));
        _view.Step(.3f);
        _hub.Publish(new EnemyDamaged(17, 1f, .9f, false));
        Assert.That(_view.CloakAmount, Is.Zero);
        Assert.That(_body.sharedMaterial == _live, Is.True);
        Assert.That(_bar.IsShown, Is.True);
        _view.Step(.3f);
        Assert.That(_view.CloakAmount, Is.Zero, "It stays shown until core hides it again.");
    }

    [Test]
    public void Despawn_ResetsEverything()
    {
        _hub.Publish(new EnemyHiddenChanged(17, true));
        _view.Step(.15f);
        _hub.Publish(new EnemyTelegraph(17, .5f));
        _hub.Publish(new EnemyHiddenChanged(17, true));
        _root.GetComponent<EnemyView>().OnDespawn();

        Assert.That(_body.sharedMaterial == _live, Is.True);
        Assert.That(_view.CloakAmount, Is.Zero);
        Assert.That(_view.FlashRemaining, Is.Zero);
        _bar.Bind(18, false);
        _hub.Publish(new EnemyDamaged(18, 1f, .5f, false));
        Assert.That(_bar.IsShown, Is.True, "The next tenant's bar is on.");
        _hub.Publish(new EnemyHiddenChanged(17, true));
        _view.Step(.3f);
        Assert.That(_view.CloakAmount, Is.Zero, "The old id no longer reaches it.");
    }

    [Test]
    public void Lurking_Crouches()
    {
        var animator = _root.GetComponentInChildren<Animator>();
        animator.Rebind();
        animator.Update(0f);
        var legs = _root.GetComponentInChildren<EnemyAnimatorView>();
        legs.Construct(_hub);
        _hub.Publish(new EnemyHiddenChanged(17, true));
        legs.Step(.1f);
        Assert.That(animator.GetBool("Crouch"), Is.True);
        _hub.Publish(new EnemyHiddenChanged(17, false));
        legs.Step(.1f);
        Assert.That(animator.GetBool("Crouch"), Is.False);
    }

    [Test]
    public void Prefab_WiresTheBodyAndClips()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        var view = new SerializedObject(prefab.GetComponent<StalkerView>());
        Assert.That(view.FindProperty("_body").objectReferenceValue, Is.EqualTo(feedback.FindProperty("_renderer").objectReferenceValue));
        Assert.That(prefab.GetComponent<ShamanView>(), Is.Null);

        var legs = new SerializedObject(prefab.GetComponentInChildren<EnemyAnimatorView>());
        var controller = (AnimatorController)prefab.GetComponentInChildren<Animator>().runtimeAnimatorController;
        Assert.That(controller.name, Is.EqualTo("AC_Stalker"));
        string[] states = { "Locomotion", "Crouch", "Death" };
        foreach (string state in states)
        { Assert.That(System.Array.Exists(controller.layers[0].stateMachine.states, s => s.state.name == state), Is.True, state); }
        AnimatorStateTransition strike = System.Array.Find(controller.layers[1].stateMachine.anyStateTransitions, t => t.destinationState.name == "Attack");
        float clip = strike.destinationState.motion.averageDuration;
        Assert.That(strike.offset * clip, Is.EqualTo(legs.FindProperty("_windupFromSeconds").floatValue).Within(.01f));
        Assert.That(controller.layers[1].name, Is.EqualTo("Strike"));
        Assert.That(controller.layers[2].name, Is.EqualTo(FlinchLayer.Name));
    }
}
