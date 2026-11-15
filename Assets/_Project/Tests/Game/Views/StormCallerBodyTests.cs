using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;
using Point = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

public sealed class StormCallerBodyTests
{
    private const string Root = "Assets/_Project/";
    private const string CallerPath = "Prefabs/Enemies/StormCaller.prefab";
    private const string FunnelPath = "Prefabs/Vfx/FX_Tornado.prefab";
    private const string SpiralPath = "Prefabs/Vfx/FX_StormCaller_Spiral.prefab";
    private const int CallerId = 7;

    private readonly List<GameObject> _objects = new();
    private DomainEventHub _events;
    private StormCallerView _caller;
    private ParticleSystem _spiral;
    private Animator _animator;
    private TornadoViews _tornadoes;
    private RunSession _run;
    private EnemySystem _enemies;
    private EnemyAgent _owner;
    private WorldSnapshot _snapshot;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private RecordingIntents _intents;
    private float _now;

    [SetUp]
    public void SetUp() => _events = new DomainEventHub();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject obj in _objects)
        {
            Object.DestroyImmediate(obj);
        }

        _objects.Clear();
        _run?.End();
        _events.Dispose();
        _now = 0f;
    }

    [Test]
    public void View_Channel_PlaysSpiral()
    {
        SpawnCaller();
        _events.Publish(new StormCallerPhaseChanged(CallerId, StormCallerPhase.Channel, 2f));
        _caller.Step(0f);
        Assert.That(_animator.GetBool("Channeling"), Is.True);
        Assert.That(_spiral.isPlaying, Is.True);
        Assert.That(_spiral.transform.parent.name, Is.EqualTo("Glow"));
        var grains = Grains(_spiral);
        Assert.That(grains, Has.Length.EqualTo(1));
        Assert.That(grains[0].position, Is.EqualTo(_spiral.transform.position));

        _caller.Step(0.5f);
        Assert.That(Grains(_spiral).Max(p => p.position.y), Is.GreaterThan(_spiral.transform.position.y + 0.3f));
        _events.Publish(new StormCallerPhaseChanged(CallerId, StormCallerPhase.Flee, 1.2f));
        Assert.That(_animator.GetBool("Channeling"), Is.False);
        Assert.That(_spiral.isEmitting, Is.False);
    }

    [Test]
    public void View_Broken_StopsSpiral()
    {
        SpawnCaller();
        _events.Publish(new StormCallerPhaseChanged(CallerId, StormCallerPhase.Channel, 2f));
        _caller.Step(0.3f);
        int count = _spiral.particleCount;
        _events.Publish(new StormCallerPhaseChanged(CallerId, StormCallerPhase.Hold, 0f));
        _events.Publish(new StormChannelBroken(CallerId));
        Assert.That(_animator.GetBool("Channeling"), Is.False);
        Assert.That(_spiral.isEmitting, Is.False);
        var scatter = Grains(_spiral);
        Assert.That(scatter, Has.Length.EqualTo(count).And.Not.Empty);
        Assert.That(scatter, Has.All.Matches<ParticleSystem.Particle>(p =>
            Mathf.Abs(p.remainingLifetime - 0.15f) < 1e-4f && p.velocity.sqrMagnitude > 0f));
        _spiral.Simulate(0.16f, false, false);
        Assert.That(_spiral.particleCount, Is.Zero);
    }

    [Test]
    public void View_OtherId_Ignored()
    {
        SpawnCaller();
        _events.Publish(new StormCallerPhaseChanged(CallerId + 1, StormCallerPhase.Channel, 2f));
        _caller.Step(0.2f);
        Assert.That(_spiral.particleCount, Is.Zero);
        Assert.That(_animator.GetBool("Channeling"), Is.False);

        _events.Publish(new StormCallerPhaseChanged(CallerId, StormCallerPhase.Channel, 2f));
        _events.Publish(new StormChannelBroken(CallerId + 1));
        Assert.That(_animator.GetBool("Channeling"), Is.True);
    }

    [Test]
    public void Tornado_RoseShowsFunnelAndRing()
    {
        SpawnPool();
        _events.Publish(Rose(1));
        _tornadoes.Step(0.15f);
        ParticleSystem dust = LiveDust();
        Assert.That(_tornadoes.ShownCount, Is.EqualTo(1));
        Assert.That(dust.transform.position, Is.EqualTo(new Vector3(2f, 0f, 4f)));
        var ring = dust.transform.parent.GetComponentInChildren<TelegraphRingView>();
        Assert.That(ring.transform.localScale.x, Is.EqualTo(3f));
        Assert.That(ring.transform.position.y, Is.InRange(0f, 0.1f));
        var properties = new MaterialPropertyBlock();
        ring.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
        Color tint = properties.GetColor("_BaseColor");
        Assert.That(tint.r, Is.EqualTo(Palette.Danger.r).Within(1e-5f));
        Assert.That(tint.g, Is.EqualTo(Palette.Danger.g).Within(1e-5f));
        Assert.That(tint.b, Is.EqualTo(Palette.Danger.b).Within(1e-5f));
        Assert.That(tint.a, Is.EqualTo(1f));
        float halfHeight = Height(dust);
        Assert.That(halfHeight, Is.GreaterThan(0f));
        _tornadoes.Step(0.15f);
        Assert.That(Height(dust), Is.EqualTo(halfHeight * 2f).Within(0.001f));
    }

    [Test]
    public void Tornado_FollowsTheReading()
    {
        StartRun();
        SpawnPool();
        Rise();
        Vector3 before = LiveDust().transform.position;
        TickEnemies(0.625f);
        _tornadoes.Step(0.625f);
        Vector3 after = LiveDust().transform.position;
        Assert.That(after.z - before.z, Is.EqualTo(1f).Within(0.001f));
        Assert.That(after, Is.EqualTo(_run.State.TornadoAt(0).Position.ToUnity()));
        AllocationAssert.None(() => _tornadoes.Step(0.01f), iterations: 100);
    }

    [Test]
    public void Tornado_RingDrawsOverFunnel()
    {
        SpawnPool();
        _events.Publish(Rose(1));
        _tornadoes.Step(0.3f);
        ParticleSystem dust = LiveDust();
        Material funnel = dust.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        Material ring = dust.transform.parent.GetComponentInChildren<MeshRenderer>().sharedMaterial;
        Assert.That(ring.renderQueue, Is.GreaterThan(funnel.renderQueue));
        float baseRadius = Grains(dust).Where(p => p.position.y < 0.11f)
            .Max(p => new Vector2(p.position.x, p.position.z).magnitude + p.startSize * 0.5f);
        Assert.That(baseRadius * 2f, Is.LessThan(0.6f));
        Assert.That(funnel.color.a, Is.LessThan(0.6f));
        Vector3 grain = Grains(dust)[0].position;
        _tornadoes.Step(0.125f);
        Assert.That(Vector3.Angle(grain, Grains(dust)[0].position), Is.GreaterThanOrEqualTo(45f));
    }

    [Test]
    public void Tornado_CollapseTakes04()
    {
        SpawnPool();
        _events.Publish(Rose(1));
        _tornadoes.Step(0.3f);
        var dust = LiveDust();
        float height = Height(dust);
        _events.Publish(new TornadoEnded(1, CallerId, true));
        Assert.That(dust.transform.parent.GetComponentInChildren<TelegraphRingView>(), Is.Null);
        _tornadoes.Step(0.2f);
        Assert.That(Height(dust), Is.EqualTo(height * 0.5f).Within(0.001f));
        _tornadoes.Step(0.25f);
        Assert.That(_tornadoes.ShownCount, Is.Zero);
        Assert.That(dust.gameObject.activeInHierarchy, Is.False);
    }

    [Test]
    public void Tornado_CoreCollapseHidesRingBeforeEnded()
    {
        StartRun();
        SpawnPool();
        Rise();
        _tornadoes.Step(0.3f);
        ParticleSystem dust = LiveDust();
        float height = Height(dust);
        _enemies.ApplyDamage(_owner.Id, 200f, _now, _player);
        TickEnemies(0.01f);
        Assert.That(_run.State.TornadoAt(0).Collapsing, Is.True);
        _tornadoes.Step(0.01f);
        Assert.That(dust.transform.parent.GetComponentInChildren<TelegraphRingView>(), Is.Null);
        TickEnemies(0.2f);
        _tornadoes.Step(0.2f);
        Assert.That(Height(dust), Is.EqualTo(height * 0.5f).Within(0.001f));
        TickEnemies(0.21f);
        _tornadoes.Step(0.21f);
        Assert.That(_tornadoes.ShownCount, Is.Zero);
    }

    [Test]
    public void Tornado_TimeoutThinsOver03()
    {
        SpawnPool();
        _events.Publish(Rose(1));
        _tornadoes.Step(0.3f);
        ParticleSystem dust = LiveDust();
        float height = Height(dust);
        _events.Publish(new TornadoEnded(1, CallerId, false));
        _tornadoes.Step(0.15f);
        var renderer = dust.GetComponent<ParticleSystemRenderer>();
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        Assert.That(properties.GetColor("_BaseColor").a, Is.EqualTo(renderer.sharedMaterial.color.a * 0.5f).Within(0.001f));
        Assert.That(Height(dust), Is.EqualTo(height).Within(0.001f));
        _tornadoes.Step(0.15f);
        Assert.That(_tornadoes.ShownCount, Is.Zero);
    }

    [Test]
    public void Tornado_TwoAtMost()
    {
        SpawnPool();
        var pool = _tornadoes.GetComponentsInChildren<ParticleSystem>(true);
        _events.Publish(Rose(1));
        _events.Publish(Rose(2));
        _events.Publish(Rose(3));
        _tornadoes.Step(0.3f);
        Assert.That(_tornadoes.ShownCount, Is.EqualTo(2));
        Assert.That(_tornadoes.GetComponentsInChildren<ParticleSystem>(true), Is.EqualTo(pool));
        _events.Publish(new TornadoEnded(1, CallerId, false));
        _events.Publish(Rose(4));
        Assert.That(_tornadoes.ShownCount, Is.EqualTo(2));
        Assert.That(_tornadoes.GetComponentsInChildren<ParticleSystem>(true), Is.EqualTo(pool));
        _events.Publish(new StageCleared(1, default, default));
        Assert.That(_tornadoes.ShownCount, Is.Zero);
        _events.Publish(Rose(5));
        _events.Publish(new StageTransitionStarted(1, 0.3f));
        Assert.That(_tornadoes.ShownCount, Is.Zero);
    }

    [Test]
    public void Tornado_UnlikeTheStorm()
    {
        Color funnel = FunnelRenderer().sharedMaterial.color;
        var ground = Asset<Material>("Materials/Environment/M_SunscarSand.mat");
        Assert.That(Luma(funnel), Is.LessThan(Luma(StormPrefab().SandTint)));
        Assert.That(Luma(funnel), Is.LessThan(Luma(ground.color)));
        Color.RGBToHSV(funnel, out float hue, out float saturation, out float value);
        Assert.That(hue, Is.InRange(0.045f, 0.115f));
        Assert.That(saturation, Is.GreaterThan(0.35f));
        Assert.That(value, Is.LessThan(0.6f));
    }

    [Test]
    public void Effects_DrawOverTheStorm()
    {
        GameObject dunes = Track(Object.Instantiate(StormPrefab().transform.root.gameObject));
        var arena = dunes.GetComponent<ArenaView>();
        var storm = dunes.GetComponentInChildren<SunscarSandstormView>(true);
        storm.Bind(arena, dunes.transform);
        int stormQueue = storm.GetComponent<ParticleSystemRenderer>().sharedMaterial.renderQueue;
        var spiral = Asset<GameObject>(SpiralPath).GetComponent<ParticleSystemRenderer>();
        var ring = Asset<GameObject>(FunnelPath).GetComponentInChildren<MeshRenderer>(true);
        Assert.That(spiral.sharedMaterial.renderQueue, Is.GreaterThan(stormQueue));
        Assert.That(FunnelRenderer().sharedMaterial.renderQueue, Is.GreaterThan(stormQueue));
        Assert.That(ring.sharedMaterial.renderQueue, Is.GreaterThan(stormQueue));
    }

    [Test]
    public void Controller_EveryStateIsRigMedium()
    {
        var controller = Asset<AnimatorController>("Animation/Controllers/AC_StormCaller.controller");
        var baseStates = controller.layers[0].stateMachine.states.Select(child => child.state).ToArray();
        var locomotion = (BlendTree)baseStates.Single(state => state.name == "Locomotion").motion;
        Assert.That(locomotion.children.Select(child => child.motion.name),
            Is.EqualTo(new[] { "Idle_B", "Walking_B", "Running_A" }));
        Assert.That(baseStates.Single(state => state.name == "Death").motion.name, Is.EqualTo("Death_A"));
        var channel = baseStates.Single(state => state.name == "Channeling");
        Assert.That(channel.motion.name, Is.EqualTo("Ranged_Magic_Spellcasting"));
        Assert.That(((AnimationClip)channel.motion).isLooping, Is.True);
        Assert.That(channel.transitions.Single().conditions.Single().parameter, Is.EqualTo("Channeling"));
        Assert.That(controller.layers[1].stateMachine.states.Single(child => child.state.name == "Hit").state.motion.name,
            Is.EqualTo("Hit_A"));
        Assert.That(controller.layers.SelectMany(layer => layer.stateMachine.states),
            Has.All.Matches<ChildAnimatorState>(child => !child.state.writeDefaultValues));
        Assert.That(controller.animationClips,
            Has.All.Matches<AnimationClip>(clip => AssetDatabase.GetAssetPath(clip).Contains("/Rig_Medium/")));
    }

    [Test]
    public void Prefab_WiresTheView()
    {
        GameObject prefab = Asset<GameObject>(CallerPath);
        var caller = prefab.GetComponent<StormCallerView>();
        var spiral = (ParticleSystem)new SerializedObject(caller).FindProperty("_spiral").objectReferenceValue;
        Assert.That(spiral.transform.parent.name, Is.EqualTo("Glow"));
        Transform staff = spiral.transform.parent.parent;
        Assert.That(staff.name, Is.EqualTo("CallerStaff"));
        Assert.That(staff.parent.name, Is.EqualTo("handslot.r"));
        var animatorView = prefab.GetComponentInChildren<EnemyAnimatorView>(true);
        var animator = (Animator)new SerializedObject(animatorView).FindProperty("_animator").objectReferenceValue;
        Assert.That(animator.runtimeAnimatorController.name, Is.EqualTo("AC_StormCaller"));
        Assert.That(animator.applyRootMotion, Is.False);
        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.Zero);
        Assert.That(feedback.FindProperty("_renderer").objectReferenceValue, Is.TypeOf<SkinnedMeshRenderer>());
        var expected = Asset<GameObject>("Prefabs/Enemies/JackalPriest.prefab").GetComponents<Component>()
            .Select(component => component.GetType()).Where(type => type != typeof(PriestMendView));
        Assert.That(prefab.GetComponents<Component>().Select(component => component.GetType()),
            Is.EquivalentTo(expected.Append(typeof(StormCallerView))));
    }

    private void SpawnCaller()
    {
        var body = Track(Object.Instantiate(Asset<GameObject>(CallerPath)));
        body.GetComponent<EnemyView>().Bind(CallerId, Vector3.zero);
        _caller = body.GetComponent<StormCallerView>();
        _caller.Construct(_events);
        _spiral = body.GetComponentInChildren<ParticleSystem>();
        _animator = body.GetComponentInChildren<Animator>();
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.Rebind();
    }

    private void SpawnPool()
    {
        _tornadoes = Track(new GameObject("Test Tornado Views")).AddComponent<TornadoViews>();
        var config = new SerializedObject(_tornadoes);
        config.FindProperty("_funnelPrefab").objectReferenceValue = Asset<GameObject>(FunnelPath);
        config.ApplyModifiedPropertiesWithoutUndo();
        _tornadoes.Construct(_run, _events);
    }

    private void StartRun()
    {
        var caller = new EnemySpec(new ContentId("enemy.test.caller"), new LocKey("enemy.test.name"),
            55f, 2f, 7, 15, 31f, false, 0f, 1f, 0f, 0f, 40f, EnemyBehaviourKind.StormCaller,
            stormCall: new StormCallSpec(12f, 6f, 1.2f, 1.4f, 16f, 2f, 3f, 6f, 2f, 1.6f,
                60f, 8f, 1.5f, 10f, 3f, 1f, 0.4f));
        var hero = new CharacterSpec(new ContentId("character.test"), new LocKey("character.test.name"),
            new LocKey("character.test.description"), 100f, new MovementSpec(3f, 0.06f, 0.08f, 720f),
            new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f), new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Charge, 10f, 1f, 2.5f, 0.15f, 20f, 5f, 0.05f), hitIFrames: 0.2f);
        var scaling = new ScalingSpec(new BudgetCurve(40f, 12f, 0.9f), new WaveCurve(2, 5, 2, 5),
            new ConcurrencyCurve(10, 2), new StatCurve(0.06f, 4f, 1, 1), new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0));
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"), 1, true, 0,
            scaling, new XpCurve(20f, 12f, 1.4f), Array.Empty<RosterEntry>());
        var random = new FixedRandom(0.5f);
        _intents = new RecordingIntents();
        _run = new RunSession(new ContentCatalog(new[] { hero }, new[] { caller }, new[] { mode }),
            random, _events, _intents, new RunRecorder(random, new FixedClock(default), _events), 8, 8, 8);
        _run.Start(new RunConfig(mode.Id, hero.Id, 0, 1, new SpawnPlan(Array.Empty<SpawnPlan.Entry>()), restore: null));
        _enemies = ReadRunService<EnemySystem>("Enemies");
        _player = ReadRunService<PlayerCombat>("Combat");
        _projectiles = new ProjectileSystem(_events, 8);
        _snapshot = new WorldSnapshot(8) { PlayerPosition = 14f * Point.UnitZ };
        _owner = _enemies.Spawn(caller.Id, Point.Zero);
    }

    private T ReadRunService<T>(string property) => (T)typeof(RunState)
        .GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_run.State);

    private void Rise()
    {
        TickEnemies(0.25f);
        TickEnemies(2f);
    }

    private void TickEnemies(float dt)
    {
        _now += dt;
        _snapshot.Dt = dt;
        _enemies.Ingest(_snapshot, now: _now);
        _enemies.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies));
    }

    private GameObject Track(GameObject obj)
    {
        _objects.Add(obj);
        return obj;
    }

    private ParticleSystem LiveDust() => _tornadoes.GetComponentInChildren<ParticleSystem>();

    private static ParticleSystem.Particle[] Grains(ParticleSystem dust)
    {
        var grains = new ParticleSystem.Particle[dust.particleCount];
        dust.GetParticles(grains);
        return grains;
    }

    private static float Height(ParticleSystem dust) => Grains(dust).Max(p => p.position.y);
    private static float Luma(Color colour) => 0.2126f * colour.r + 0.7152f * colour.g + 0.0722f * colour.b;

    private static TornadoRose Rose(int id) => new(id, CallerId, new Point(2f, 0f, 4f), 1.5f, 8f);

    private static ParticleSystemRenderer FunnelRenderer() =>
        Asset<GameObject>(FunnelPath).GetComponentInChildren<ParticleSystemRenderer>(true);

    private static SunscarSandstormView StormPrefab() =>
        Asset<GameObject>("Prefabs/Arenas/Sunscar/Arena_Dunes.prefab").GetComponentInChildren<SunscarSandstormView>(true);

    private static T Asset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(Root + path);
}
