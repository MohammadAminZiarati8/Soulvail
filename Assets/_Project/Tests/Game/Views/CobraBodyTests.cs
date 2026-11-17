using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Numerics = System.Numerics;

namespace Soulvail.Tests.Game.Views;

public sealed class CobraBodyTests
{
    private const string Project = "Assets/_Project/";
    private const string CobraPath = "Prefabs/Enemies/Cobra.prefab";
    private const int CobraId = 72;
    private readonly List<GameObject> _objects = new();
    private readonly Vector3 _apex = new(20f, 0f, 25f);
    private DomainEventHub _events;
    private EnemyView _body;
    private CobraView _view;
    private SprayConeView _fan;
    private ParticleSystem _spray;
    private Animator _animator;

    [SetUp]
    public void SetUp() => _events = new DomainEventHub();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject root in _objects)
        {
            Object.DestroyImmediate(root);
        }

        _objects.Clear();
        _events.Dispose();
    }

    [Test]
    public void View_ConeArmed_ArmsTheFan()
    {
        SpawnCobra();
        var cover = new GameObject("Cover", typeof(BoxCollider));
        _objects.Add(cover);
        cover.layer = LayerMask.NameToLayer("Cover");
        cover.transform.position = _apex + new Vector3(2f, 1.1f, 0f);
        Physics.SyncTransforms();

        Arm(CobraId);
        _fan.Step(0.35f);

        Assert.That(_fan.IsLive, Is.True);
        Assert.That(_fan.transform.position, Is.EqualTo(_apex + Vector3.up * SprayConeView.GroundOffset));
        Assert.That(_fan.Fill, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(_fan.Range, Is.EqualTo(5f));
        Assert.That(Vector3.Angle(Vector3.right, _fan.RayDirection(0)), Is.EqualTo(30f).Within(0.01f));
        Assert.That(_fan.IsBlocked(SprayConeView.Rays / 2), Is.True);
        Assert.That(_fan.IsBlocked(0), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void View_Spat_StopsFanPlaysSpray(bool hit)
    {
        SpawnCobra();
        Arm(CobraId);
        _body.transform.rotation = Quaternion.LookRotation(Vector3.back);

        _events.Publish(new CobraSpat(CobraId, hit));

        Assert.That(_fan.IsLive, Is.False);
        Assert.That(_spray.isPlaying, Is.True);
        var particles = new ParticleSystem.Particle[_spray.main.maxParticles];
        int count = _spray.GetParticles(particles);
        Assert.That(count, Is.Positive);
        foreach (ParticleSystem.Particle drop in particles.Take(count))
        {
            Assert.That(drop.position, Is.EqualTo(_apex + Vector3.up * 0.5f));
            Assert.That(Vector3.Angle(Vector3.right, Vector3.ProjectOnPlane(drop.velocity, Vector3.up)),
                Is.LessThanOrEqualTo(30.01f));
        }
    }

    [Test]
    public void View_OtherId_Ignored()
    {
        SpawnCobra();
        Arm(CobraId + 1);
        Assert.That(_fan.IsLive, Is.False);

        Arm(CobraId);
        _events.Publish(new CobraSpat(CobraId + 1, true));
        Die(CobraId + 1);
        Assert.That(_fan.IsLive, Is.True);
        Assert.That(_spray.isPlaying, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void View_Death_ClearsEffects(bool afterSpit)
    {
        SpawnCobra();
        Phase(CobraPhase.Rear);
        Arm(CobraId);
        if (afterSpit)
        {
            _events.Publish(new CobraSpat(CobraId, false));
        }

        Die(CobraId);
        Assert.That(_fan.IsLive, Is.False);
        Assert.That(_spray.isPlaying, Is.False);
        Assert.That(_spray.particleCount, Is.Zero);
        Assert.That(_animator.GetInteger("CobraPhase"), Is.Zero);
        Assert.That(_animator.GetBool("Dead"), Is.True);
    }

    [Test]
    public void View_PooledReuse_Resets()
    {
        SpawnCobra();
        Phase(CobraPhase.Rear);
        Arm(CobraId);
        _events.Publish(new CobraSpat(CobraId, false));
        _body.OnDespawn();
        _body.gameObject.SetActive(false);
        _body.gameObject.SetActive(true);
        _body.Bind(CobraId + 1, _apex);
        Arm(CobraId);
        _animator.Update(0f);

        Assert.That(_fan.IsLive, Is.False);
        Assert.That(_spray.isPlaying, Is.False);
        Assert.That(_spray.particleCount, Is.Zero);
        Assert.That(_animator.GetInteger("CobraPhase"), Is.Zero);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), Is.True);
        Arm(CobraId + 1);
        Assert.That(_fan.IsLive, Is.True);

        _view.Bind(CobraId + 2);
        Assert.That(_fan.IsLive, Is.False);
        Arm(CobraId + 1);
        Assert.That(_fan.IsLive, Is.False);
        Arm(CobraId + 2);
        Assert.That(_fan.IsLive, Is.True);
    }

    [Test]
    public void Animator_CobraPhase_Set()
    {
        SpawnCobra();
        Phase(CobraPhase.Hold);
        Assert.That(_animator.GetInteger("CobraPhase"), Is.EqualTo(0));
        Phase(CobraPhase.Close);
        Assert.That(_animator.GetInteger("CobraPhase"), Is.EqualTo(1));
        Phase(CobraPhase.Rear);
        Assert.That(_animator.GetInteger("CobraPhase"), Is.EqualTo(2));
        _animator.Update(0.01f);
        _animator.Update(0.9f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Rear"), Is.True);

        _events.Publish(new CobraSpat(CobraId, false));
        Phase(CobraPhase.Recover);
        Assert.That(_animator.GetInteger("CobraPhase"), Is.EqualTo(3));
        _animator.Update(0.01f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Spit"), Is.True);
        _animator.Update(0.75f);
        _animator.Update(0.15f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), Is.True);
    }

    [Test]
    public void Controller_EveryStateIsTheModels()
    {
        var controller = Load<AnimatorController>("Animation/Controllers/AC_Cobra.controller");
        var states = controller.layers.SelectMany(layer => layer.stateMachine.states).Select(child => child.state).ToArray();
        Assert.That(states.Select(state => state.name),
            Is.EquivalentTo(new[] { "Locomotion", "Rear", "Spit", "Death", "Hit", "Empty" }));
        Assert.That(states.All(state => !state.writeDefaultValues), Is.True);
        Assert.That(controller.animationClips.Select(clip => clip.name),
            Is.EquivalentTo(new[] { "Idle", "Slither", "Rear", "Spit", "Hit", "Death" }));
        Assert.That(controller.animationClips.All(clip => AssetDatabase.GetAssetPath(clip) == Project + "Art/Enemies/Cobra.fbx"),
            Is.True);
    }

    [Test]
    public void Prefab_WiresTheView()
    {
        var prefab = Load<GameObject>(CobraPath);
        var view = new SerializedObject(prefab.GetComponent<CobraView>());
        Assert.That(view.FindProperty("_cone").objectReferenceValue, Is.InstanceOf<SprayConeView>());
        Assert.That(view.FindProperty("_spit").objectReferenceValue, Is.InstanceOf<ParticleSystem>());
        var feedback = new SerializedObject(prefab.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.Zero);
        Assert.That(prefab.GetComponentInChildren<Animator>(true).applyRootMotion, Is.False);
    }

    [Test]
    public void Effects_DrawOverTheStorm()
    {
        var dunes = Spawn("Prefabs/Arenas/Sunscar/Arena_Dunes.prefab");
        dunes.SetActive(false);
        var storm = dunes.GetComponentInChildren<SunscarSandstormView>(true);
        storm.Bind(dunes.GetComponent<ArenaView>(), dunes.transform);
        Material stormMaterial = storm.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        try
        {
            var prefab = Load<GameObject>(CobraPath);
            Material fan = prefab.GetComponentInChildren<SprayConeView>(true).GetComponent<MeshRenderer>().sharedMaterial;
            var spray = prefab.GetComponentInChildren<ParticleSystem>(true);
            Assert.That(fan.renderQueue, Is.GreaterThan(stormMaterial.renderQueue));
            Assert.That(spray.GetComponent<ParticleSystemRenderer>().sharedMaterial.renderQueue,
                Is.GreaterThan(stormMaterial.renderQueue));
            Assert.That(spray.main.startColor.color, Is.EqualTo((Color)(Color32)Palette.Danger));
        }
        finally
        {
            Object.DestroyImmediate(stormMaterial);
        }
    }

    [Test]
    public void StatusRow_PoisonIconIsVioletDrop()
    {
        var row = Spawn("Prefabs/UI/Hud.prefab").GetComponentInChildren<StatusRow>(true);
        var poison = new ContentId("status.poisoned");
        row.Draw(0, new StatusEntry(poison, false, 2f, 4f, 2));

        Assert.That(AssetDatabase.GetAssetPath(row.Icon(0).sprite), Is.EqualTo(Project + "Art/UI/Hud/T_Status_Poisoned.png"));
        Assert.That(row.Icon(0).overrideSprite, Is.Not.EqualTo(row.Icon(0).sprite));
        Assert.That(row.Icon(0).color, Is.EqualTo(StatusRow.PoisonTint));
        Assert.That(row.Stacks(0).enabled, Is.True);
        Assert.That(row.Stacks(0).text, Is.EqualTo("2"));
        Assert.That(row.Sweep(0).fillAmount, Is.EqualTo(0.5f));
        row.Draw(0, new StatusEntry(poison, false, 1f, 4f, 2));
        Assert.That(row.Sweep(0).fillAmount, Is.EqualTo(0.25f));

        row.Draw(0, new StatusEntry(new ContentId("status.steady-aim"), true, 1f, 4f, 1));
        Assert.That(row.Icon(0).overrideSprite, Is.EqualTo(row.Icon(0).sprite));
        Assert.That(row.Icon(0).color, Is.EqualTo(Color.white));
        Assert.That(row.Stacks(0).enabled, Is.False);
    }

    [Test]
    public void PoisonTint_OutsideReservedBands()
    {
        Color.RGBToHSV(StatusRow.PoisonTint, out float hue, out float saturation, out float value);
        Assert.That(hue * 360f, Is.InRange(250f, 285f));
        Assert.That(saturation, Is.InRange(0.1f, 0.35f));
        Assert.That(value, Is.GreaterThan(0.9f));
        Assert.That(StatusRow.PoisonTint, Is.Not.EqualTo(Palette.Veilrot));
    }

    [Test]
    public void Pulse_NoFlinchNoHaptic()
    {
        var knight = Spawn("Prefabs/Player/Bodies/Knight.prefab").GetComponentInChildren<PlayerAnimatorView>(true);
        var ranger = Spawn("Prefabs/Player/Bodies/Ranger.prefab").GetComponentInChildren<RangerAnimatorView>(true);
        var beastmaster = Spawn("Prefabs/Characters/Beastmaster.prefab").GetComponentInChildren<BeastmasterAnimatorView>(true);
        knight.Construct(_events);
        ranger.Construct(_events);
        beastmaster.Construct(_events);
        Animator[] animators = { GetAnimator(knight), GetAnimator(ranger), GetAnimator(beastmaster) };
        foreach (Animator animator in animators)
        {
            animator.Rebind();
            animator.Update(0f);
        }

        float now = 0f;
        var vibrator = new CountingVibrator();
        using var listener = new HapticsListener(_events, vibrator, HapticsSettings.InMemory(true), () => now);
        listener.Start();
        _events.Publish(new PlayerDamaged(0f, 3f, 0.8f, 0f, blocked: false, isPulse: true));
        now = 1f;
        listener.Tick();
        Assert.That(animators.All(animator => !animator.GetBool("Hit")), Is.True);
        Assert.That(vibrator.Count, Is.Zero);

        _events.Publish(new PlayerDamaged(0f, 3f, 0.7f, 0f, blocked: false));
        now = 2f;
        listener.Tick();
        Assert.That(animators.All(animator => animator.GetBool("Hit")), Is.True);
        Assert.That(vibrator.Count, Is.EqualTo(1));
    }

    private void SpawnCobra()
    {
        var root = Spawn(CobraPath);
        _body = root.GetComponent<EnemyView>();
        _view = root.GetComponent<CobraView>();
        _fan = root.GetComponentInChildren<SprayConeView>();
        _spray = root.GetComponentInChildren<ParticleSystem>();
        _animator = root.GetComponentInChildren<Animator>();
        _view.Construct(_events);
        root.GetComponentInChildren<EnemyAnimatorView>().Construct(_events);
        _body.Bind(CobraId, _apex);
        _animator.Rebind();
        _animator.Update(0f);
    }

    private void Phase(CobraPhase phase) => _events.Publish(new CobraPhaseChanged(CobraId, phase, 0.7f));

    private void Arm(int id) => _events.Publish(new CobraConeArmed(id,
        new Numerics.Vector3(_apex.x, _apex.y, _apex.z), Numerics.Vector2.UnitX, 30f, 5f, 0.7f));

    private void Die(int id) => _events.Publish(new EnemyDied(id, new ContentId("enemy.cobra"), Numerics.Vector3.Zero));

    // A body saved inactive is injected by its pool but never woken, so its feedback throws on every
    // enemy hit and telegraph in the arena until the first one of its kind spawns.
    [Test]
    public void EnemyBodies_AreSavedActive()
    {
        string[] asleep = AssetDatabase.FindAssets("t:Prefab", new[] { Project + "Prefabs/Enemies" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !AssetDatabase.LoadAssetAtPath<GameObject>(path).activeSelf)
            .ToArray();
        Assert.That(asleep, Is.Empty);
    }

    // The base body's grey placeholder capsule must not draw over the snake.
    [Test]
    public void Cobra_HidesTheBasePlaceholderMesh()
    {
        var cobra = Load<GameObject>("Prefabs/Enemies/Cobra.prefab");
        Assert.That(cobra.transform.Find("Mesh").gameObject.activeSelf, Is.False);
    }

    private GameObject Spawn(string path)
    {
        var root = Object.Instantiate(Load<GameObject>(path));
        _objects.Add(root);
        root.SetActive(true);
        return root;
    }

    private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(Project + path);

    private static Animator GetAnimator(Component view) =>
        (Animator)new SerializedObject(view).FindProperty("_animator").objectReferenceValue;

    private sealed class CountingVibrator : IVibrator
    {
        public int Count { get; private set; }
        public void Pulse(int milliseconds, float amplitude01) => Count++;
    }
}
