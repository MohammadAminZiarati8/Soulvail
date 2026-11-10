using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class ScorpionTellViewTests
{
    private const string Project = "Assets/_Project/";
    private const string ControllerPath = Project + "Animation/Controllers/AC_Scorpion.controller";
    private const string MaterialPath = Project + "Materials/Enemies/M_ScorpionTell.mat";
    private const int Id = 17;
    private const float Windup = 0.6f;

    private GameObject _root;
    private DomainEventHub _hub;
    private ScorpionTellView _view;
    private Renderer _tip;
    private Renderer _strip;

    [TearDown]
    public void CleanUp()
    {
        if (_root != null)
        {
            Object.DestroyImmediate(_root);
        }
        _hub?.Dispose();
    }

    [Test]
    public void Telegraph_Mine_ShowsTipAndStrip()
    {
        CreateBody();
        AssertShowing(false);
        Telegraph();
        AssertShowing(true);
    }

    [Test]
    public void Telegraph_OtherId_Ignored()
    {
        CreateBody();
        _hub.Publish(new EnemyTelegraph(Id + 1, Windup));
        AssertShowing(false);
        _hub.Publish(new EnemyTelegraph(Id, 0f));
        AssertShowing(false);
    }

    [Test]
    public void Strip_SizedAheadOfBody()
    {
        CreateBody();
        _root.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        Telegraph();

        Transform strip = _strip.transform;
        Assert.That(strip.parent, Is.EqualTo(_root.transform));
        Assert.That(strip.localPosition, Is.EqualTo(new Vector3(0f, 0.075f, 0.9f)));
        Assert.That(strip.localScale, Is.EqualTo(new Vector3(1f, 1.8f, 1f)));
        Assert.That(Vector3.Dot(strip.up, _root.transform.forward), Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void Strip_AlphaRamps()
    {
        CreateBody();
        Color sharedColour = _strip.sharedMaterial.GetColor("_BaseColor");
        Telegraph();
        Assert.That(Alpha(_strip), Is.EqualTo(0.3f).Within(1e-5f));
        _view.Step(Windup / 2f);
        Assert.That(Alpha(_strip), Is.EqualTo(0.6f).Within(1e-5f));
        Assert.That(Alpha(_tip), Is.EqualTo(1f));
        _view.Step(Windup / 2f);
        Assert.That(Alpha(_strip), Is.EqualTo(0.9f).Within(1e-5f));
        Assert.That(_strip.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(sharedColour));
    }

    [Test]
    public void Duration_Elapsed_Hides()
    {
        CreateBody();
        Telegraph();
        _view.Step(Windup / 2f);
        AssertShowing(true);
        _view.Step(Windup / 2f);
        AssertShowing(false);
    }

    [Test]
    public void Died_Hides()
    {
        CreateBody();
        Telegraph();
        var scorpion = new ContentId("enemy.scorpion");
        _hub.Publish(new EnemyDied(Id + 1, scorpion, System.Numerics.Vector3.Zero));
        AssertShowing(true);
        _hub.Publish(new EnemyDied(Id, scorpion, System.Numerics.Vector3.Zero));
        AssertShowing(false);
    }

    [Test]
    public void Disable_Hides()
    {
        CreateBody();
        Telegraph();
        _root.SetActive(false);
        // Unity does not dispatch ordinary MonoBehaviour lifecycle messages in EditMode.
        typeof(ScorpionTellView).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_view, null);
        AssertShowing(false);
        Telegraph();
        AssertShowing(false);
        _root.SetActive(true);
        AssertShowing(false);
        Telegraph();
        AssertShowing(true);
    }

    [Test]
    public void Prefab_WiresTheViews()
    {
        GameObject prefab = Prefab();
        var animation = new SerializedObject(prefab.GetComponentInChildren<EnemyAnimatorView>(true));
        Assert.That(animation.FindProperty("_windupFromSeconds").floatValue, Is.Zero);
        Assert.That(animation.FindProperty("_strikeSeconds").floatValue, Is.EqualTo(Windup));
        Assert.That(animation.FindProperty("_hops").boolValue, Is.False);
        var animator = (Animator)animation.FindProperty("_animator").objectReferenceValue;
        Assert.That(animator.applyRootMotion, Is.False);
        Assert.That(animator.runtimeAnimatorController, Is.EqualTo(AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)));

        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.Zero);
        Assert.That(feedback.FindProperty("_telegraphSwell").floatValue, Is.EqualTo(1f));
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue, Is.InstanceOf<SkinnedMeshRenderer>());

        var tell = new SerializedObject(prefab.GetComponent<ScorpionTellView>());
        Renderer tip = RendererField(tell, "_tip");
        Renderer strip = RendererField(tell, "_strip");
        Assert.That(tip.transform.parent.name, Is.EqualTo("Stinger"));
        Assert.That(strip.transform.parent, Is.EqualTo(prefab.transform));
        Assert.That(tip.enabled || strip.enabled, Is.False);
        Assert.That(tip.sharedMaterial, Is.EqualTo(strip.sharedMaterial));
    }

    [Test]
    public void Controller_ChainsWindupIntoStrike()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimatorStateMachine baseLayer = controller.layers[0].stateMachine;
        var states = baseLayer.states.ToDictionary(child => child.state.name, child => child.state);
        AnimatorState locomotion = states["Locomotion"];
        Assert.That(baseLayer.defaultState, Is.EqualTo(locomotion));
        var blend = (BlendTree)locomotion.motion;
        Assert.That(blend.blendParameter, Is.EqualTo("Speed"));
        Assert.That(locomotion.speedParameter, Is.EqualTo("WalkRate"));
        Assert.That(blend.children.Select(child => child.motion.name), Is.EqualTo(new[] { "Idle", "Skitter" }));

        AnimatorState windup = states["Attack"];
        AnimatorState strike = states["Strike"];
        var trigger = baseLayer.anyStateTransitions.Single(t => t.destinationState == windup);
        Assert.That(trigger.conditions.Select(c => c.parameter), Does.Contain("Attack"));
        Assert.That(trigger.hasExitTime, Is.False);
        Assert.That(trigger.duration, Is.Zero);
        Assert.That(windup.motion.name, Is.EqualTo("Windup"));
        Assert.That(windup.speedParameterActive, Is.True);
        Assert.That(windup.speedParameter, Is.EqualTo("AttackRate"));
        var chain = windup.transitions.Single();
        Assert.That(chain.destinationState, Is.EqualTo(strike));
        Assert.That(chain.hasExitTime, Is.True);
        Assert.That(chain.exitTime, Is.EqualTo(1f));
        Assert.That(chain.duration, Is.Zero);
        Assert.That(strike.motion.name, Is.EqualTo("Strike"));
        Assert.That(strike.speedParameterActive, Is.False);
        Assert.That(strike.speed, Is.EqualTo(1f));
        Assert.That(strike.transitions.Single().destinationState, Is.EqualTo(locomotion));
        Assert.That(strike.transitions.Single().exitTime, Is.EqualTo(1f));
        Assert.That(states["Death"].motion.name, Is.EqualTo("Death"));
        Assert.That(controller.layers.Single(layer => layer.name == "Flinch").stateMachine.states
            .Single(child => child.state.name == "Hit").state.motion.name, Is.EqualTo("Hit"));
    }

    [Test]
    public void TellMaterial_IsDanger()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Color colour = material.GetColor("_BaseColor");
        Assert.That(Palette.IsDanger(colour), Is.True);
        Assert.That(colour.a, Is.EqualTo(1f));
        Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Unlit"));
        Assert.That(material.GetTag("RenderType", false), Is.EqualTo("Transparent"));
        Assert.That(material.GetFloat("_ZWrite"), Is.Zero);
    }

    private static GameObject Prefab() =>
        AssetDatabase.LoadAssetAtPath<GameObject>(Project + "Prefabs/Enemies/Scorpion.prefab");

    private void CreateBody()
    {
        _root = Object.Instantiate(Prefab());
        _view = _root.GetComponent<ScorpionTellView>();
        var fields = new SerializedObject(_view);
        _tip = RendererField(fields, "_tip");
        _strip = RendererField(fields, "_strip");
        _root.GetComponent<EnemyView>().Bind(Id, Vector3.zero);
        _hub = new DomainEventHub();
        _view.Construct(_hub);
    }

    private static Renderer RendererField(SerializedObject obj, string field) =>
        (Renderer)obj.FindProperty(field).objectReferenceValue;

    private void Telegraph() => _hub.Publish(new EnemyTelegraph(Id, Windup));

    private void AssertShowing(bool expected)
    {
        Assert.That(_view.IsShowing, Is.EqualTo(expected));
        Assert.That(_tip.enabled, Is.EqualTo(expected));
        Assert.That(_strip.enabled, Is.EqualTo(expected));
    }

    private static float Alpha(Renderer renderer)
    {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        return block.GetColor("_BaseColor").a;
    }
}
