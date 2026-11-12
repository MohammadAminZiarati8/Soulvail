using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

public sealed class PriestBodyTests
{
    private const string Root = "Assets/_Project/";
    private const string BodyPath = "Prefabs/Enemies/JackalPriest.prefab";
    private const string ControllerPath = "Animation/Controllers/AC_JackalPriest.controller";
    private const string MaterialPath = "Materials/Enemies/M_MendBeam.mat";
    private const string Mending = "Mending";
    private const int Id = 51;
    private const int AllyId = 52;

    private readonly List<Object> _created = new();
    private GameObject _instance;
    private DomainEventHub _events;
    private IObjectResolver _container;
    private EnemyViews _views;
    private PriestMendView _view;
    private LineRenderer _beam;
    private Renderer _disc;
    private Transform _glow;

    [TearDown]
    public void Dispose()
    {
        if (_instance != null)
        {
            Object.DestroyImmediate(_instance);
        }

        _views?.Dispose();
        _events?.Dispose();
        _container?.Dispose();
        foreach (Object created in _created)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        _created.Clear();
    }

    [Test]
    public void Mend_Started_BeamOn()
    {
        Spawn();
        Assert.That(_beam.enabled, Is.False);
        Start();
        _view.Step(0.1f);
        Assert.That(_beam.enabled, Is.True);
        Assert.That(_beam.GetPosition(0), Is.EqualTo(_glow.position));
        Assert.That(_beam.GetPosition(1).x, Is.EqualTo(6f).Within(1e-4f));
        Assert.That(_beam.GetPosition(1).y, Is.GreaterThan(0.3f));
        Assert.That(_view.DiscLit, Is.True);
        Assert.That(Emission().maxColorComponent, Is.GreaterThan(0f));
    }

    [Test]
    public void Mend_BeamFollowsAlly()
    {
        Spawn();
        Start();
        _view.Step(0.1f);
        _views.TryGet(AllyId, out EnemyView ally);
        ally.transform.position += new Vector3(2f, 0f, 0f);
        _view.Step(0.1f);
        Assert.That(_beam.GetPosition(1).x, Is.EqualTo(8f).Within(1e-4f));
    }

    [Test]
    public void Mend_Done_Fades()
    {
        Spawn();
        Start();
        _view.Step(0.1f);
        End(MendEndReason.Done);
        _view.Step(0.1f);
        Assert.That(_beam.enabled, Is.True);
        Assert.That(_view.BeamIntensity, Is.EqualTo(0.5f).Within(1e-4f));
        _view.Step(0.11f);
        Assert.That(_beam.enabled, Is.False);
        Assert.That(_view.DiscLit, Is.False);
        Assert.That(Emission().maxColorComponent, Is.Zero);
        foreach (MendEndReason reason in new[] { MendEndReason.Healed, MendEndReason.Lost })
        {
            Start();
            End(reason);
            _view.Step(0.21f);
            Assert.That(_beam.enabled, Is.False, reason.ToString());
        }
    }

    [Test]
    public void Mend_Broken_Flickers()
    {
        Spawn();
        Start();
        _view.Step(0.1f);
        End(MendEndReason.Broken);
        var seen = new List<bool>();
        for (int i = 0; i < 4; i++)
        {
            _view.Step(0.0375f * 0.5f);
            seen.Add(_beam.enabled);
            _view.Step(0.0375f * 0.5f - 0.0001f);
            seen.Add(_beam.enabled);
        }

        Assert.That(seen, Is.EqualTo(new[] { true, true, false, false, true, true, false, false }));
        _view.Step(0.05f);
        Assert.That(_beam.enabled, Is.False);
        Assert.That(_view.DiscLit, Is.False);
    }

    [Test]
    public void Mend_OtherId_Ignored()
    {
        Spawn();
        _events.Publish(new PriestMendStarted(Id + 100, AllyId, 2f));
        _view.Step(0.1f);
        Assert.That(_beam.enabled, Is.False);
        Start();
        _view.Step(0.1f);
        _events.Publish(new PriestMendEnded(Id + 100, AllyId, MendEndReason.Done));
        _view.Step(0.5f);
        Assert.That(_beam.enabled, Is.True);
        Assert.That(_view.BeamIntensity, Is.EqualTo(1f));
    }

    [Test]
    public void Mend_LimeIsTheShamans()
    {
        Spawn();
        Start();
        _view.Step(0.1f);
        Assert.That(ShamanView.Lime, Is.EqualTo(new Color(.75f, .9f, .38f, 1f)));
        var block = new MaterialPropertyBlock();
        _beam.GetPropertyBlock(block);
        Color beam = block.GetColor("_BaseColor");
        Assert.That(beam.r, Is.EqualTo(ShamanView.Lime.r).Within(1e-4f));
        Assert.That(beam.g, Is.EqualTo(ShamanView.Lime.g).Within(1e-4f));
        Assert.That(beam.b, Is.EqualTo(ShamanView.Lime.b).Within(1e-4f));
        Color glow = Emission();
        Assert.That(glow.g / glow.r, Is.EqualTo(ShamanView.Lime.g / ShamanView.Lime.r).Within(1e-4f));
        Assert.That(glow.b / glow.r, Is.EqualTo(ShamanView.Lime.b / ShamanView.Lime.r).Within(1e-4f));
    }

    [Test]
    public void Mend_SharedMaterialUntouched()
    {
        var asset = Load<Material>(MaterialPath);
        string file = Path.GetFullPath(Root + MaterialPath);
        string before = File.ReadAllText(file);
        Color colour = asset.GetColor("_BaseColor");
        int queue = asset.renderQueue;
        Spawn();
        Start();
        _view.Step(0.1f);
        End(MendEndReason.Done);
        _view.Step(0.3f);
        Assert.That(_beam.sharedMaterial, Is.SameAs(asset));
        Assert.That(asset.GetColor("_BaseColor"), Is.EqualTo(colour));
        Assert.That(asset.renderQueue, Is.EqualTo(queue));
        Assert.That(File.ReadAllText(file), Is.EqualTo(before));
        Assert.That(Load<Material>("Materials/Enemies/M_Enemy.mat").GetColor("_EmissionColor").maxColorComponent, Is.Zero);
    }

    [Test]
    public void Mend_DrawsOverTheStorm()
    {
        GameObject dunes = Load<GameObject>("Prefabs/Arenas/Sunscar/Arena_Dunes.prefab");
        var parked = new GameObject("Storm test");
        parked.SetActive(false);
        Material stormMaterial = null;
        try
        {
            var arena = Object.Instantiate(dunes, parked.transform).GetComponent<ArenaView>();
            var storm = arena.GetComponentInChildren<SunscarSandstormView>(true);
            storm.Bind(arena, parked.transform);
            stormMaterial = storm.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Assert.That(Load<Material>(MaterialPath).renderQueue, Is.GreaterThan(stormMaterial.renderQueue));
        }
        finally
        {
            Object.DestroyImmediate(parked);
            Object.DestroyImmediate(stormMaterial);
        }
    }

    [Test]
    public void Animator_Mending_Set()
    {
        Spawn();
        Animator animator = _instance.GetComponentInChildren<Animator>();
        animator.Rebind();
        animator.GetComponent<EnemyAnimatorView>().Construct(_events);
        Assert.That(animator.GetBool(Mending), Is.False);
        Start();
        Assert.That(animator.GetBool(Mending), Is.True);
        _events.Publish(new PriestMendStarted(Id + 100, AllyId, 2f));
        End(MendEndReason.Done);
        Assert.That(animator.GetBool(Mending), Is.False);
        Start();
        End(MendEndReason.Broken);
        Assert.That(animator.GetBool(Mending), Is.False);
    }

    [Test]
    public void Controller_EveryStateIsRigMedium()
    {
        var controller = Load<AnimatorController>(ControllerPath);
        AnimatorStateMachine locomotion = controller.layers[0].stateMachine;
        var clips = new Dictionary<string, string>();
        foreach (ChildAnimatorState child in locomotion.states.Concat(controller.layers[1].stateMachine.states))
        {
            AnimatorState state = child.state;
            Assert.That(state.writeDefaultValues, Is.False, state.name);
            if (state.motion is BlendTree tree)
            {
                foreach (ChildMotion motion in tree.children)
                {
                    clips[motion.motion.name] = AssetDatabase.GetAssetPath(motion.motion);
                }
            }
            else
            {
                clips[state.motion?.name ?? state.name] = state.motion == null ? null : AssetDatabase.GetAssetPath(state.motion);
            }
        }

        Assert.That(clips.Keys, Is.EquivalentTo(new[]
        {
            "Idle_B", "Walking_B", "Running_A", "Ranged_Magic_Spellcasting", "Death_A", "Hit_A", "Empty",
        }));
        foreach ((string name, string path) in clips.Where(pair => pair.Value != null))
        {
            Assert.That(path, Does.StartWith("Assets/ThirdParty/KayKit/Animations/Rig_Medium/"), name);
        }

        Assert.That(controller.parameters.Single(p => p.name == Mending).type, Is.EqualTo(AnimatorControllerParameterType.Bool));
        AnimatorState mend = locomotion.states.Single(s => s.state.name == "Mend").state;
        Assert.That(mend.motion.name, Is.EqualTo("Ranged_Magic_Spellcasting"));
        Assert.That(((AnimationClip)mend.motion).isLooping, Is.True);
        var tree2 = (BlendTree)locomotion.states.Single(s => s.state.name == "Locomotion").state.motion;
        Assert.That(tree2.children.Select(c => c.motion.name), Is.EqualTo(new[] { "Idle_B", "Walking_B", "Running_A" }));
    }

    [Test]
    public void Prefab_WiresTheView()
    {
        var body = Load<GameObject>(BodyPath);
        using var view = new SerializedObject(body.GetComponent<PriestMendView>());
        var beam = (LineRenderer)view.FindProperty("_beam").objectReferenceValue;
        var glow = (Transform)view.FindProperty("_glow").objectReferenceValue;
        var disc = (Renderer)view.FindProperty("_disc").objectReferenceValue;
        Assert.That(beam, Is.Not.Null);
        Assert.That(beam.sharedMaterial, Is.SameAs(Load<Material>(MaterialPath)));
        Assert.That(beam.positionCount, Is.EqualTo(2));
        Assert.That(beam.startWidth, Is.EqualTo(0.15f).Within(1e-4f));
        Assert.That(beam.enabled, Is.False);
        Assert.That(glow, Is.Not.Null);
        Assert.That(glow.name, Is.EqualTo("Glow"));
        Assert.That(disc, Is.Not.Null);
        Assert.That(glow.IsChildOf(disc.transform), Is.True);
        Assert.That(disc.transform.parent.name, Is.EqualTo("handslot.r"));
        using var feedback = new SerializedObject(body.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.Zero);
        var model = body.transform.Find("Model");
        Assert.That(model.GetComponent<Animator>().runtimeAnimatorController, Is.SameAs(Load<AnimatorController>(ControllerPath)));
        Assert.That(model.GetComponent<Animator>().applyRootMotion, Is.False);
        Assert.That(model.GetComponent<EnemyAnimatorView>(), Is.Not.Null);
        Assert.That(body.GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
    }

    [Test]
    public void Pooled_Reuse_ResetsOff()
    {
        Spawn();
        Start();
        _view.Step(0.1f);
        _view.OnDespawn();
        Assert.That(_beam.enabled, Is.False);
        Assert.That(_view.DiscLit, Is.False);
        Assert.That(Emission().maxColorComponent, Is.Zero);
        _view.Bind(Id + 1, _events, _views);
        _view.Step(0.1f);
        Assert.That(_beam.enabled, Is.False);
        _events.Publish(new PriestMendStarted(Id, AllyId, 2f));
        Assert.That(_beam.enabled, Is.False);
        Start(Id + 1);
        End(MendEndReason.Done, Id + 1);
        _events.Publish(new EnemyDied(Id + 1, new ContentId("enemy.jackal-priest"), System.Numerics.Vector3.Zero));
        Assert.That(_beam.enabled, Is.False);
    }

    private void Spawn()
    {
        _events = new DomainEventHub();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(_events);
        _container = builder.Build();

        var parent = new GameObject("Enemies");
        _created.Add(parent);
        var template = new GameObject("Ally template");
        template.transform.SetParent(parent.transform, false);
        template.transform.position = new Vector3(1000f, 0f, 1000f);
        var allyView = template.AddComponent<EnemyView>();
        allyView.Body.isTrigger = true;
        _views = new EnemyViews(_container, allyView, parent.transform, _events,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()));
        _events.Publish(new EnemySpawned(AllyId, new ContentId("enemy.jackal"), new System.Numerics.Vector3(6f, 0f, 0f)));

        _instance = Object.Instantiate(Load<GameObject>(BodyPath));
        _instance.GetComponent<EnemyView>().Bind(Id, Vector3.zero);
        _view = _instance.GetComponent<PriestMendView>();
        _view.Construct(_events, _views);
        using var serialized = new SerializedObject(_view);
        _beam = (LineRenderer)serialized.FindProperty("_beam").objectReferenceValue;
        _glow = (Transform)serialized.FindProperty("_glow").objectReferenceValue;
        _disc = (Renderer)serialized.FindProperty("_disc").objectReferenceValue;
    }

    private void Start(int id = Id) => _events.Publish(new PriestMendStarted(id, AllyId, 2f));

    private void End(MendEndReason reason, int id = Id) => _events.Publish(new PriestMendEnded(id, AllyId, reason));

    private Color Emission()
    {
        var block = new MaterialPropertyBlock();
        _disc.GetPropertyBlock(block);
        return block.GetColor("_EmissionColor");
    }

    private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(Root + path);
}
