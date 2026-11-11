using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using Vector3Core = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

public sealed class AntlionViewTests
{
    private const string Root = "Assets/_Project/";
    private const string Prefab = "Prefabs/Enemies/Antlion.prefab";
    private const string Model = "Art/Enemies/Antlion.fbx";
    private const string Controller = "Animation/Controllers/AC_Antlion.controller";
    private const string PhaseParameter = "AntlionPhase";
    private const int Id = 41;

    private readonly ContentId _spec = new("enemy.antlion");
    private GameObject _instance;
    private DomainEventHub _events;
    private EnemyView _enemy;
    private EnemyHealthBar _bar;
    private AntlionView _view;
    private Renderer _body;
    private ParticleSystem _ripple;
    private ParticleSystem _swirl;
    private ParticleSystem _burst;
    private TelegraphRingView _ring;
    private TrailRenderer _trail;

    [TearDown]
    public void Dispose()
    {
        if (_instance != null)
        {
            Object.DestroyImmediate(_instance);
        }

        _events?.Dispose();
    }

    [Test]
    public void View_Tunnel_HidesBodyShowsRipple()
    {
        Spawn();
        var statusView = _instance.GetComponent<EnemyStatusView>();
        using var serialized = new SerializedObject(statusView);
        _bar.ConfigureStatuses(Reference<StatusIconBook>(serialized, "_icons"),
            Reference<Sprite>(serialized, "_sweepSprite"));
        _events.Publish(new EnemyStatusChanged(Id, new EnemyStatuses().Poisoned, 2f, 1));
        Assert.That(_bar.StatusIcon(0).gameObject.activeSelf, Is.True);

        Phase(AntlionPhase.Tunnel);
        _view.Step(0.1f);

        Assert.That(_body.enabled, Is.False);
        Assert.That(_ripple.isPlaying, Is.True);
        Assert.That(_trail.emitting, Is.True);
        Assert.That(_trail.time, Is.EqualTo(1f));
        Assert.That(_bar.IsShown, Is.False);
        Assert.That(_bar.StatusIcon(0).gameObject.activeSelf, Is.False);

        Phase(AntlionPhase.Surfaced);
        Assert.That(_bar.StatusIcon(0).gameObject.activeSelf, Is.True);
    }

    [Test]
    public void View_Ripple_FollowsAgent()
    {
        Spawn();
        Phase(AntlionPhase.Dig, 0.6f);
        _view.Step(0.39f);
        Assert.That(_body.enabled, Is.True);
        Assert.That(_ripple.isPlaying, Is.False);
        _view.Step(0.02f);
        Assert.That(_ripple.isPlaying, Is.True);

        Phase(AntlionPhase.Tunnel);
        _instance.transform.position += Vector3.right * 2f;
        _view.Step(0.1f);

        Assert.That(_ripple.transform.position, Is.EqualTo(_instance.transform.position));
        Assert.That(_ripple.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));
    }

    [Test]
    public void View_RingArmed_DrawsRingAndSwirl()
    {
        Spawn();
        _instance.transform.localScale = Vector3.one * 1.5f;
        Phase(AntlionPhase.Arm);
        Arm();
        _instance.transform.SetPositionAndRotation(Vector3.left * 2f, Quaternion.Euler(0f, 90f, 0f));
        _view.Step(0.4f);

        Assert.That(_ring.transform.position.x, Is.EqualTo(3f).Within(1e-4f));
        Assert.That(_ring.transform.position.z, Is.EqualTo(4f).Within(1e-4f));
        Assert.That(_ring.Radius, Is.EqualTo(1.6f));
        Assert.That(_ring.Fill, Is.EqualTo(0.5f).Within(1e-4f));
        Assert.That(_ring.transform.lossyScale.x, Is.EqualTo(1.6f).Within(1e-4f));
        Assert.That(_ring.Colour, Is.EqualTo(Palette.Danger));
        Assert.That(_ring.GetComponent<Renderer>().enabled, Is.True);
        Assert.That(_swirl.isPlaying, Is.True);
        Assert.That(Vector3.Distance(_swirl.transform.position, new Vector3(3f, 0f, 4f)), Is.LessThan(1e-4f));

        _view.Step(0.5f);
        Assert.That(_ring.Fill, Is.GreaterThan(0.99f));
        Assert.That(_swirl.isPlaying, Is.True);
    }

    [Test]
    public void View_Surfaced_ShowsBody()
    {
        Spawn();
        Phase(AntlionPhase.Arm);
        Arm();
        Phase(AntlionPhase.Surfaced);
        _instance.transform.position = new Vector3(3f, 0f, 4f);
        _view.Step(0.1f);

        Assert.That(_body.enabled, Is.True);
        Assert.That(_ripple.isEmitting, Is.False);
        Assert.That(_trail.emitting, Is.False);
        Assert.That(_ring.IsLive, Is.False);
        Assert.That(_ring.GetComponent<Renderer>().enabled, Is.False);
        Assert.That(_swirl.isPlaying, Is.False);
        Assert.That(_burst.isPlaying, Is.True);
        Assert.That(Vector3.Distance(_burst.transform.position, new Vector3(3f, 0f, 4f)), Is.LessThan(1e-4f));
        Assert.That(_bar.IsShown, Is.True);
    }

    [Test]
    public void View_OtherId_Ignored()
    {
        Spawn();
        Phase(AntlionPhase.Tunnel, id: Id + 1);
        Arm(Id + 1);

        Assert.That(_body.enabled, Is.True);
        Assert.That(_ripple.isPlaying, Is.False);
        Assert.That(_ring.IsLive, Is.False);
        Assert.That(_swirl.isPlaying, Is.False);
    }

    [Test]
    public void View_Death_ClearsEffects()
    {
        Spawn();
        Phase(AntlionPhase.Surfaced);
        _view.Step(0.1f);
        Assert.That(_burst.isPlaying, Is.True);
        _events.Publish(new EnemyDied(Id, _spec, Vector3Core.Zero));
        Assert.That(_burst.isPlaying, Is.False);
        Phase(AntlionPhase.Tunnel);
        Assert.That(_body.enabled, Is.True);

        _view.Bind(Id + 1);
        Phase(AntlionPhase.Arm, id: Id + 1);
        Arm(Id + 1);
        _events.Publish(new EnemyDied(Id + 1, _spec, Vector3Core.Zero));
        _view.Step(0.1f);

        Assert.That(_ripple.isPlaying, Is.False);
        Assert.That(_trail.emitting, Is.False);
        Assert.That(_ring.IsLive, Is.False);
        Assert.That(_swirl.isPlaying, Is.False);
        Assert.That(_burst.isPlaying, Is.False);
    }

    [Test]
    public void View_PooledReuse_ResetsToDig()
    {
        Spawn();
        Phase(AntlionPhase.Arm);
        Arm();
        _instance.SetActive(false);
        _enemy.Unbind();
        _bar.Bind(Id + 1, true);
        _enemy.Bind(Id + 1, Vector3.zero);
        _instance.SetActive(true);
        _view.Bind(Id + 1);

        Assert.That(_body.enabled, Is.True);
        Assert.That(_bar.IsShown, Is.True);
        Assert.That(_ripple.isPlaying, Is.False);
        Assert.That(_trail.emitting, Is.False);
        Assert.That(_ring.IsLive, Is.False);
        Phase(AntlionPhase.Tunnel);
        Assert.That(_body.enabled, Is.True);
        Phase(AntlionPhase.Tunnel, id: Id + 1);
        Assert.That(_body.enabled, Is.False);
    }

    [Test]
    public void Animator_AntlionPhase_Set()
    {
        Spawn();
        var animator = _instance.GetComponentInChildren<Animator>();
        animator.Rebind();
        var view = animator.GetComponent<EnemyAnimatorView>();
        view.Construct(_events);

        Phase(AntlionPhase.Tunnel, id: Id + 1);
        Assert.That(animator.GetInteger(PhaseParameter), Is.Zero);
        foreach ((AntlionPhase phase, string state) in new[]
        {
            (AntlionPhase.Dig, "DigIn"), (AntlionPhase.Tunnel, "Hidden"),
            (AntlionPhase.Arm, "Hidden"), (AntlionPhase.Surfaced, "Erupt"),
        })
        {
            Phase(phase);
            Assert.That(animator.GetInteger(PhaseParameter), Is.EqualTo((int)phase));
            animator.Update(0.05f);
            animator.Update(0.05f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName(state), Is.True, state);
        }

        animator.Update(0.5f);
        animator.Update(0.1f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Surfaced"), Is.True);

        view.Forget();
        Assert.That(animator.GetInteger(PhaseParameter), Is.Zero);
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void Controller_EveryStateIsTheModels()
    {
        AnimatorController controller = Load<AnimatorController>(Controller);
        var states = controller.layers.SelectMany(layer => layer.stateMachine.states.Select(child => child.state)).ToArray();
        Assert.That(states.All(state => !state.writeDefaultValues), Is.True);
        Assert.That(states.Where(state => state.motion == null).Select(state => state.name),
            Is.EquivalentTo(new[] { "Hidden", "Empty" }));
        Assert.That(states.Single(state => state.name == "Hit").speed, Is.EqualTo(1.4f));
        Assert.That(states.Single(state => state.name == "Death").speed, Is.EqualTo(1.5f));
        var locomotion = states.Single(state => state.name == "Locomotion");
        var blend = (BlendTree)locomotion.motion;
        Assert.That(blend.blendParameter, Is.EqualTo("Speed"));
        Assert.That(blend.children.Select(child => child.motion.name), Is.EqualTo(new[] { "Idle", "Walk" }));
        var clips = states.Select(state => state.motion).OfType<AnimationClip>()
            .Concat(blend.children.Select(child => (AnimationClip)child.motion)).ToArray();
        Assert.That(clips.Select(clip => clip.name),
            Is.EquivalentTo(new[] { "Idle", "Walk", "DigIn", "Erupt", "Surfaced", "Hit", "Death" }));
        Assert.That(clips.All(clip => AssetDatabase.GetAssetPath(clip) == Root + Model), Is.True);
    }

    [Test]
    public void Prefab_WiresTheView()
    {
        GameObject body = Load<GameObject>(Prefab);
        GameObject glassback = Load<GameObject>("Prefabs/Enemies/Glassback.prefab");
        Assert.That(body.GetComponents<Component>().Select(component => component.GetType()),
            Is.EquivalentTo(glassback.GetComponents<Component>().Select(component => component.GetType())
                .Append(typeof(AntlionView))));
        var animator = body.GetComponentInChildren<Animator>();
        Assert.That(animator.runtimeAnimatorController, Is.SameAs(Load<AnimatorController>(Controller)));
        Assert.That(animator.applyRootMotion, Is.False);
        Assert.That(animator.GetComponent<EnemyAnimatorView>(), Is.Not.Null);

        using var view = new SerializedObject(body.GetComponent<AntlionView>());
        foreach (string field in new[] { "_ripple", "_swirl", "_mound", "_trail", "_burst", "_ring", "_ringRenderer", "_armEffects" })
        {
            Assert.That(view.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
        }

        var renderers = view.FindProperty("_body");
        Assert.That(renderers.arraySize, Is.EqualTo(1));
        Assert.That(renderers.GetArrayElementAtIndex(0).objectReferenceValue, Is.SameAs(body.GetComponentInChildren<SkinnedMeshRenderer>()));
        using var feedback = new SerializedObject(body.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.Zero);
    }

    [Test]
    public void Effects_DrawOverTheStorm()
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
            Assert.That(stormMaterial.renderQueue, Is.EqualTo(2900));

            GameObject body = Load<GameObject>(Prefab);
            foreach (string effect in new[] { "FX_Antlion_Ripple", "FX_Antlion_Swirl", "VFX_TelegraphRing" })
            {
                var root = body.GetComponentsInChildren<Transform>(true).Single(child => child.name == effect);
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.That(renderer.sharedMaterial.renderQueue, Is.GreaterThan(stormMaterial.renderQueue), renderer.name);
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(parked);
            Object.DestroyImmediate(stormMaterial);
        }
    }

    [Test]
    public void Effects_NoCyan()
    {
        var body = Load<GameObject>(Prefab);
        using var view = new SerializedObject(body.GetComponent<AntlionView>());
        var colours = new List<Color> { view.FindProperty("_moundTint").colorValue };
        foreach (string field in new[] { "_ripple", "_swirl", "_burst" })
        {
            colours.Add(Reference<ParticleSystem>(view, field).main.startColor.color);
        }

        TrailRenderer trail = Reference<TrailRenderer>(view, "_trail");
        colours.AddRange(trail.colorGradient.colorKeys.Select(key => key.color));
        foreach (Color colour in colours)
        {
            Color.RGBToHSV(colour, out float hue, out _, out _);
            Assert.That(hue, Is.InRange(26f / 360f, 55f / 360f), colour.ToString());
        }
    }

    private void Spawn()
    {
        _events = new DomainEventHub();
        _instance = Object.Instantiate(Load<GameObject>(Prefab));
        _enemy = _instance.GetComponent<EnemyView>();
        _enemy.Bind(Id, Vector3.zero);
        _bar = _enemy.HealthBar;
        _bar.Construct(_events);
        _bar.Bind(Id, true);
        _view = _instance.GetComponent<AntlionView>();
        _view.Construct(_events);
        using var view = new SerializedObject(_view);
        _body = (Renderer)view.FindProperty("_body").GetArrayElementAtIndex(0).objectReferenceValue;
        _ripple = Reference<ParticleSystem>(view, "_ripple");
        _swirl = Reference<ParticleSystem>(view, "_swirl");
        _burst = Reference<ParticleSystem>(view, "_burst");
        _ring = Reference<TelegraphRingView>(view, "_ring");
        _trail = Reference<TrailRenderer>(view, "_trail");
    }

    private void Phase(AntlionPhase phase, float seconds = 0.8f, int id = Id) =>
        _events.Publish(new AntlionPhaseChanged(id, phase, seconds));

    private void Arm(int id = Id) =>
        _events.Publish(new AntlionRingArmed(id, new Vector3Core(3f, 0f, 4f), 1.6f, 0.8f));

    private static T Reference<T>(SerializedObject view, string field) where T : Object =>
        (T)view.FindProperty(field).objectReferenceValue;

    private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(Root + path);
}
