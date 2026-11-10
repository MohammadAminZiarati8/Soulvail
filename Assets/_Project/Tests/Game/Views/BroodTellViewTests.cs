using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class BroodTellViewTests
{
    private const string Project = "Assets/_Project/";
    private const string MotherModel = Project + "Art/Enemies/BroodMother.fbx";
    private const int Id = 17;
    private const float Duration = 0.5f;

    private GameObject _root;
    private DomainEventHub _hub;
    private BroodTellView _view;
    private Transform _brood;

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
    public void Swelling_Mine_Grows()
    {
        CreateBody();
        Swell();
        Assert.That(_view.IsSwelling, Is.True);
        Assert.That(_brood.localScale, Is.EqualTo(Vector3.one));
        _view.Step(Duration / 2f);
        Assert.That(_brood.localScale.x, Is.GreaterThan(1f));
    }

    [Test]
    public void Swelling_OtherId_Ignored()
    {
        CreateBody();
        _hub.Publish(new EnemyBroodSwelling(Id + 1, Duration));
        _hub.Publish(new EnemyBroodSwelling(Id, float.NaN));
        _view.Step(Duration / 2f);
        Assert.That(_view.IsSwelling, Is.False);
        Assert.That(_brood.localScale, Is.EqualTo(Vector3.one));
    }

    [Test]
    public void Swelling_Pulses()
    {
        CreateBody();
        Swell();

        var samples = new float[101];
        samples[0] = _brood.localScale.x;
        for (int i = 1; i < samples.Length; i++)
        {
            _view.Step(Duration / 100f);
            samples[i] = _brood.localScale.x;
        }

        var peaks = Enumerable.Range(1, samples.Length - 2)
            .Where(i => samples[i] > samples[i - 1] && samples[i] > samples[i + 1])
            .Select(i => samples[i]).ToArray();
        Assert.That(peaks, Has.Length.EqualTo(2));
        Assert.That(peaks[0], Is.LessThan(peaks[1]));
        Assert.That(peaks[1], Is.EqualTo(1.35f).Within(0.005f));
    }

    [Test]
    public void Burst_HoldsScale()
    {
        CreateBody();
        Swell();
        _view.Step(Duration / 2f);

        _hub.Publish(new EnemyBroodBurst(Id + 1, System.Numerics.Vector3.Zero));
        Assert.That(_view.IsSwelling, Is.True);
        Vector3 scale = _brood.localScale;
        _hub.Publish(new EnemyBroodBurst(Id, System.Numerics.Vector3.Zero));
        _view.Step(Duration);

        Assert.That(_view.IsSwelling, Is.False);
        Assert.That(_brood.localScale, Is.EqualTo(scale));
    }

    [Test]
    public void Disable_Restores()
    {
        CreateBody();
        Swell();
        _view.Step(Duration / 2f);
        _hub.Publish(new EnemyBroodBurst(Id, System.Numerics.Vector3.Zero));

        _root.SetActive(false);
        InvokeLifecycle("OnDisable");
        Assert.That(_brood.localScale, Is.EqualTo(Vector3.one));
        Swell();
        Assert.That(_view.IsSwelling, Is.False);

        _root.SetActive(true);
        InvokeLifecycle("OnEnable");
        _root.GetComponent<EnemyView>().Bind(Id + 1, Vector3.zero);
        Swell();
        Assert.That(_view.IsSwelling, Is.False);
        Assert.That(_brood.localScale, Is.EqualTo(Vector3.one));
        _hub.Publish(new EnemyBroodSwelling(Id + 1, Duration));
        Assert.That(_view.IsSwelling, Is.True);
    }

    [Test]
    public void NoSwelling_HumpStill()
    {
        CreateBody();
        _hub.Publish(new EnemyDied(Id, new ContentId("enemy.broodmother"), System.Numerics.Vector3.Zero));
        _view.Step(0.6f);
        Assert.That(_view.IsSwelling, Is.False);
        Assert.That(_brood.localScale, Is.EqualTo(Vector3.one));
    }

    [Test]
    public void Mother_WiresTheViews()
    {
        GameObject prefab = Prefab("BroodMother");
        Animator animator = CheckBody(prefab, "BroodMother", 1f, 2.4f, 0.7f);
        var tell = new SerializedObject(prefab.GetComponent<ScorpionTellView>());
        // SS-07b measured the refitted stinger tip along the bone's local Y axis.
        Assert.That(RendererField(tell, "_tip").transform.localPosition, Is.EqualTo(new Vector3(0f, 0.473435f, 0f)));
        Transform brood = BroodBone(prefab.GetComponent<BroodTellView>());
        Assert.That(brood.name, Is.EqualTo("Brood"));
        Assert.That(brood.IsChildOf(animator.transform), Is.True);
        Assert.That(brood.childCount, Is.Zero);
        Assert.That(brood.localScale, Is.EqualTo(Vector3.one));
        Assert.That(animator.runtimeAnimatorController, Is.EqualTo(Controller("BroodMother")));

        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_dissolveSeconds").floatValue, Is.GreaterThan(Duration));
    }

    [Test]
    public void Scorpling_ScaledChildNotRoot()
    {
        GameObject prefab = Prefab("Scorpling");
        Animator animator = CheckBody(prefab, "Scorpion", 0.55f, 1.2f, 0.35f);
        Assert.That(animator.runtimeAnimatorController, Is.EqualTo(Controller("Scorpion")));
        Assert.That(prefab.GetComponent<BroodTellView>(), Is.Null);
    }

    [Test]
    public void Controller_WindupSlowed()
    {
        AnimatorController controller = Controller("BroodMother");
        AnimatorController scorpion = Controller("Scorpion");
        Assert.That(controller.layers.Select(layer => layer.name), Is.EqualTo(scorpion.layers.Select(layer => layer.name)));
        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            AnimatorStateMachine original = scorpion.layers.Single(item => item.name == layer.name).stateMachine;
            Assert.That(layer.stateMachine.states.Select(child => child.state.name),
                Is.EquivalentTo(original.states.Select(child => child.state.name)));
        }

        var states = controller.layers[0].stateMachine.states.ToDictionary(child => child.state.name, child => child.state);
        AnimatorState windup = states["Attack"];
        Assert.That(windup.motion.name, Is.EqualTo("Windup"));
        Assert.That(windup.speed, Is.EqualTo(0.75f));
        Assert.That(windup.speedParameterActive, Is.True);
        Assert.That(windup.speedParameter, Is.EqualTo("AttackRate"));
        Assert.That(windup.transitions.Single().destinationState, Is.EqualTo(states["Strike"]));
        Assert.That(windup.transitions.Single().exitTime, Is.EqualTo(1f));

        var motion = (BlendTree)states["Locomotion"].motion;
        Assert.That(motion.blendParameter, Is.EqualTo("Speed"));
        Assert.That(motion.children.Select(child => child.motion.name), Is.EqualTo(new[] { "Idle", "Skitter" }));
        Assert.That(controller.animationClips.Select(AssetDatabase.GetAssetPath), Is.All.EqualTo(MotherModel));
        Assert.That(controller.animationClips.Select(clip => clip.name),
            Is.EquivalentTo(new[] { "Idle", "Skitter", "Windup", "Strike", "Hit", "Death" }));

        var view = new SerializedObject(Prefab("BroodMother").GetComponentInChildren<EnemyAnimatorView>(true));
        Assert.That(view.FindProperty("_windupFromSeconds").floatValue, Is.Zero);
        Assert.That(view.FindProperty("_strikeSeconds").floatValue * windup.speed,
            Is.EqualTo(windup.motion.averageDuration).Within(1e-5f));
    }

    [Test]
    public void Bodies_DrawOverTheStorm()
    {
        GameObject dunes = AssetDatabase.LoadAssetAtPath<GameObject>(Project + "Prefabs/Arenas/Sunscar/Arena_Dunes.prefab");
        _root = Object.Instantiate(dunes);
        var storm = _root.GetComponentInChildren<SunscarSandstormView>(true);
        storm.Bind(_root.GetComponent<ArenaView>(), _root.transform);
        Material grains = storm.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        try
        {
            Assert.That(grains.GetFloat("_ZWrite"), Is.Zero);
            Assert.That(grains.shader.name, Is.EqualTo("Universal Render Pipeline/Particles/Unlit"));
            Assert.That(storm.GetComponent<ParticleSystemRenderer>().renderMode, Is.EqualTo(ParticleSystemRenderMode.HorizontalBillboard));
            foreach (string name in new[] { "BroodMother", "Scorpling" })
            {
                GameObject prefab = Prefab(name);
                var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
                var body = (Renderer)feedback.FindProperty("_renderer").objectReferenceValue;
                Material live = body.sharedMaterial;
                Assert.That(live.GetTag("RenderType", false), Is.EqualTo("Opaque"));
                Assert.That(live.GetFloat("_ZWrite"), Is.EqualTo(1f));
                Assert.That(live.renderQueue, Is.LessThan(grains.renderQueue));

                var dissolve = (Material)feedback.FindProperty("_dissolveMaterial").objectReferenceValue;
                Assert.That(dissolve.renderQueue, Is.GreaterThan(grains.renderQueue));
                var tell = new SerializedObject(prefab.GetComponent<ScorpionTellView>());
                Assert.That(RendererField(tell, "_strip").sharedMaterial.renderQueue, Is.GreaterThan(grains.renderQueue));
            }
        }
        finally
        {
            Object.DestroyImmediate(grains);
        }
    }

    private static Animator CheckBody(GameObject prefab, string model, float scale, float length, float halfWidth)
    {
        Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(prefab.GetComponent<EnemyView>(), Is.Not.Null);
        var animator = prefab.GetComponentInChildren<Animator>(true);
        Assert.That(animator.transform.parent, Is.EqualTo(prefab.transform));
        Assert.That(animator.transform.localScale, Is.EqualTo(Vector3.one * scale));
        Assert.That(animator.applyRootMotion, Is.False);
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(animator.gameObject),
            Is.EqualTo(Project + "Art/Enemies/" + model + ".fbx"));

        var animation = new SerializedObject(animator.GetComponent<EnemyAnimatorView>());
        Assert.That(animation.FindProperty("_animator").objectReferenceValue, Is.EqualTo(animator));
        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.Zero);
        Assert.That(feedback.FindProperty("_telegraphSwell").floatValue, Is.EqualTo(1f));
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue,
            Is.EqualTo(animator.GetComponentInChildren<SkinnedMeshRenderer>(true)));

        var tell = new SerializedObject(prefab.GetComponent<ScorpionTellView>());
        Renderer tip = RendererField(tell, "_tip");
        Renderer strip = RendererField(tell, "_strip");
        Assert.That(tip.transform.parent.name, Is.EqualTo("Stinger"));
        Assert.That(tip.transform.IsChildOf(animator.transform), Is.True);
        Assert.That(tip.enabled || strip.enabled, Is.False);
        Assert.That(strip.transform.parent, Is.EqualTo(prefab.transform));
        Assert.That(tell.FindProperty("_stripLength").floatValue, Is.EqualTo(length));
        Assert.That(tell.FindProperty("_stripHalfWidth").floatValue, Is.EqualTo(halfWidth));
        Assert.That(strip.transform.localScale, Is.EqualTo(new Vector3(2f * halfWidth, length, 1f)));
        return animator;
    }

    private void CreateBody()
    {
        _root = Object.Instantiate(Prefab("BroodMother"));
        _root.SetActive(true);
        _root.GetComponent<EnemyView>().Bind(Id, Vector3.zero);
        _view = _root.GetComponent<BroodTellView>();
        _brood = BroodBone(_view);
        _hub = new DomainEventHub();
        _view.Construct(_hub);
    }

    private void Swell() => _hub.Publish(new EnemyBroodSwelling(Id, Duration));

    // EditMode does not dispatch ordinary MonoBehaviour lifecycle messages.
    private void InvokeLifecycle(string name) =>
        typeof(BroodTellView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_view, null);

    private static Transform BroodBone(BroodTellView view) =>
        (Transform)new SerializedObject(view).FindProperty("_brood").objectReferenceValue;

    private static Renderer RendererField(SerializedObject obj, string field) =>
        (Renderer)obj.FindProperty(field).objectReferenceValue;

    private static GameObject Prefab(string name) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(Project + "Prefabs/Enemies/" + name + ".prefab");

    private static AnimatorController Controller(string name) =>
        AssetDatabase.LoadAssetAtPath<AnimatorController>(Project + "Animation/Controllers/AC_" + name + ".controller");
}
