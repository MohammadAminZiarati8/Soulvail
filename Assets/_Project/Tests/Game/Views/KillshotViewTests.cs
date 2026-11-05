using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using Soulvail.Game.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using V3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class KillshotViewTests
{
    private KillshotView _view;
    private DomainEventHub _hub;
    private GameObject _bow, _camera;
    private CameraShake _shake;

    [SetUp]
    public void SetUp()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<KillshotView>("Assets/_Project/Prefabs/Vfx/VFX_Killshot.prefab");
        Assert.That(prefab, Is.Not.Null);
        _view = Object.Instantiate(prefab);
        _hub = new DomainEventHub();
        _bow = new GameObject("Bow tip");
        _bow.transform.position = new Vector3(2f, 0.8f, 3f);
        _camera = new GameObject("Camera");
        _shake = _camera.AddComponent<CameraShake>();
        _view.Construct(_hub, null, _bow.transform, _shake);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_view.gameObject);
        Object.DestroyImmediate(_bow);
        Object.DestroyImmediate(_camera);
        _hub.Dispose();
    }

    [Test]
    public void WindUp_KneelsAndGlows_FacesLine()
    {
        GameObject body = Ranger(out RangerAnimatorView pose, out Animator animator);
        try
        {
            pose.Construct(_hub);
            animator.Rebind();
            animator.Update(0f);
            _hub.Publish(new KillshotWindUp(V3.UnitX, 0.7f));
            animator.Update(0.11f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Kneel"), Is.True);
            Assert.That(animator.GetCurrentAnimatorClipInfo(0)[0].clip.name, Is.EqualTo("Crouching"));
            Assert.That(Vector3.Dot(pose.transform.forward, Vector3.right), Is.GreaterThan(0.999f));
            int upper = animator.GetLayerIndex(RangerAnimatorView.UpperBodyLayer);
            Assert.That(animator.GetCurrentAnimatorStateInfo(upper).IsName("Draw"), Is.True);
            var charge = Child<Light>("Charge Light");
            var aim = Child<LineRenderer>("Aim Line");
            Assert.That(charge.enabled && aim.enabled, Is.True);
            Assert.That(aim.startWidth, Is.LessThan(0.04f));
            Assert.That(aim.startColor.a, Is.LessThan(0.2f));
            Assert.That(aim.GetPosition(1) - aim.GetPosition(0), Is.EqualTo(Vector3.right * 30f));
            float initial = charge.intensity;
            _view.Step(0.35f);
            Assert.That(charge.intensity, Is.GreaterThan(initial));
            Assert.That(_view.transform.Find("Charge Glow").gameObject.activeSelf, Is.True);
            _hub.Publish(new TargetChanged(42, false, false, -1));
            animator.Update(0.7f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Kneel"), Is.True,
                "The pose waits for the fire fact, even if the cosmetic timer expires.");
            _hub.Publish(new KillshotFired(V3.Zero, V3.UnitX, 30f, 0));
            animator.Update(0.24f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), Is.True);
            Assert.That(animator.IsInTransition(0), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(body);
        }
    }

    [Test]
    public void Fire_FourTimesArrow_FliesAlongLine_WithFadingWind()
    {
        _hub.Publish(new KillshotWindUp(V3.UnitX, 0.7f));
        _hub.Publish(new KillshotFired(V3.Zero, V3.UnitX, 30f, 2));
        Transform arrow = _view.transform.Find("Killshot Arrow");
        Transform ordinary = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Projectiles/Arrow.prefab").transform.Find("Mesh");
        Assert.That(arrow.localScale, Is.EqualTo(ordinary.localScale * 4f));
        Assert.That(arrow.GetComponent<MeshFilter>().sharedMesh, Is.EqualTo(ordinary.GetComponent<MeshFilter>().sharedMesh));
        Assert.That(arrow.gameObject.activeSelf, Is.True);
        Assert.That(Vector3.Dot(arrow.forward, Vector3.right), Is.GreaterThan(0.999f));
        Vector3 initial = arrow.position;
        _view.Step(0f);
        Assert.That(arrow.position, Is.EqualTo(initial), "Run pause freezes the arrow.");
        Assert.That(Child<Light>("Charge Light").enabled, Is.False);
        Assert.That(Child<LineRenderer>("Aim Line").enabled, Is.False);
        _view.Step(0.1f);
        Assert.That(arrow.position.x, Is.GreaterThan(initial.x + 10f));
        Assert.That(arrow.position.y, Is.EqualTo(_bow.transform.position.y));
        var streak = Child<LineRenderer>("Streak");
        Assert.That(streak.enabled, Is.True);
        Assert.That(streak.sharedMaterial.GetFloat("_Surface"), Is.EqualTo(1f));
        Assert.That(streak.sharedMaterial.GetFloat("_ZWrite"), Is.Zero);
        Assert.That(Vector4.Distance(streak.endColor, new Color(Palette.Wind.r, Palette.Wind.g, Palette.Wind.b, 0.7f)),
            Is.LessThan(0.005f));
        for (int i = 0; i < 3; i++)
        {
            var wind = Child<LineRenderer>("Wind Streak " + i);
            Assert.That(wind.enabled, Is.True);
            Assert.That(wind.GetPosition(2).x, Is.GreaterThan(wind.GetPosition(0).x));
            Assert.That(wind.GetPosition(2).x, Is.LessThan(arrow.position.x + 2.7f));
            Assert.That(wind.startColor.a, Is.Zero);
        }
        _view.Step(0.2f);
        Assert.That(arrow.gameObject.activeSelf, Is.False);
        Assert.That(arrow.position.x + 2.7f,
            Is.EqualTo(_bow.transform.position.x + 0.25f + 30f).Within(0.001f));
        Assert.That(streak.endColor.a, Is.EqualTo(0.35f).Within(0.003f));
        _view.Step(0.201f);
        Assert.That(streak.enabled, Is.False);
        for (int i = 0; i < 3; i++)
        {
            Assert.That(Child<LineRenderer>("Wind Streak " + i).enabled, Is.False);
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void Flight_CancellationClearsArrowAndWind_ThenReuses(int cause)
    {
        _hub.Publish(new KillshotFired(V3.Zero, V3.UnitX, 30f, 0));
        _view.Step(0.08f);
        if (cause == 0)
        { _hub.Publish(new PlayerDied(0f)); }
        else if (cause == 1)
        { _hub.Publish(new StageTransitionStarted(1, 2)); }
        else if (cause == 2)
        { _hub.Publish(new RunEnded(0.2f)); }
        else
        { _hub.Publish(new StageArrived(2, new Soulvail.Core.Content.ContentId("arena.test"))); }
        Transform arrow = _view.transform.Find("Killshot Arrow");
        Assert.That(arrow.gameObject.activeSelf, Is.False);
        Assert.That(Child<LineRenderer>("Streak").enabled, Is.False);
        for (int i = 0; i < 3; i++)
        {
            Assert.That(Child<LineRenderer>("Wind Streak " + i).enabled, Is.False);
        }
        _bow.transform.position = new Vector3(-12f, 1f, -8f);
        _hub.Publish(new KillshotFired(V3.Zero, V3.UnitZ, 30f, 0));
        Assert.That(arrow.gameObject.activeSelf, Is.True);
        Assert.That(arrow.position.x, Is.EqualTo(-12f));
        Assert.That(Vector3.Dot(arrow.forward, Vector3.forward), Is.GreaterThan(0.999f));
        Assert.That(Child<LineRenderer>("Streak").GetPosition(0),
            Is.EqualTo(_bow.transform.position + Vector3.forward * 0.25f));
    }

    [Test]
    public void Fire_JoltsCamera()
    {
        _shake.Kick(CameraShake.SlamStrength);
        _hub.Publish(new KillshotFired(V3.Zero, V3.UnitZ, 30f, 0));
        Assert.That(_shake.Offset.magnitude, Is.GreaterThan(0.35f));
        _shake.Step(0.21f);
        Assert.That(_shake.IsShaking, Is.True);
        _shake.Step(0.02f);
        Assert.That(_shake.IsShaking, Is.False, "Shorter than the slam's 0.4 s.");
        Assert.That(_shake.Offset, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void Execute_Bursts()
    {
        _hub.Publish(new KillshotWindUp(V3.UnitZ, 0.7f));
        _hub.Publish(new KillshotHit(4, new V3(1f, 0f, 6f)));
        _hub.Publish(new KillshotExecuted(4));
        _hub.Publish(new KillshotHit(8, new V3(-1f, 0f, 9f)));
        _hub.Publish(new KillshotExecuted(8));
        var bursts = Child<ParticleSystem>("Execute Bursts");
        var sparks = Child<ParticleSystem>("Hit Sparks");
        Assert.That(bursts.particleCount, Is.EqualTo(36));
        Assert.That(sparks.particleCount, Is.EqualTo(12));
        var particles = new ParticleSystem.Particle[36];
        Assert.That(bursts.GetParticles(particles), Is.EqualTo(36));
        Assert.That(particles[0].position, Is.EqualTo(new Vector3(1f, 0.65f, 6f)));
        Assert.That(particles[18].position, Is.EqualTo(new Vector3(-1f, 0.65f, 9f)));
        Assert.That(Vector3.Distance(particles[0].velocity, particles[9].velocity), Is.GreaterThan(4f),
            "Opposite particles spread into a burst rather than overlapping into a dot.");
        Assert.That(bursts.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));
        _view.Step(0.4f);
        Assert.That(bursts.particleCount + sparks.particleCount, Is.Zero);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void Cancel_EndsCleanly(int cause)
    {
        GameObject body = Ranger(out RangerAnimatorView pose, out Animator animator);
        try
        {
            pose.Construct(_hub);
            animator.Rebind();
            animator.Update(0f);
            _hub.Publish(new KillshotWindUp(V3.UnitX, 0.7f));
            animator.Update(0.11f);
            if (cause == 0)
            {
                _hub.Publish(new PlayerDied(0f));
            }
            else if (cause == 1)
            {
                _hub.Publish(new StageTransitionStarted(1, 2));
            }
            else
            {
                _hub.Publish(new RunEnded(0.2f));
            }
            animator.Update(0.24f);
            Assert.That(_view.IsWindingUp, Is.False);
            Assert.That(Child<Light>("Charge Light").enabled, Is.False);
            Assert.That(Child<LineRenderer>("Aim Line").enabled, Is.False);
            Assert.That(Child<LineRenderer>("Streak").enabled, Is.False);
            Assert.That(pose.transform.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Kneel"), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(body);
        }
    }

    [Test]
    public void Pooled_ReusesSystemsAndUnsubscribes()
    {
        int children = _view.GetComponentsInChildren<Transform>(true).Length;
        for (int cast = 0; cast < 4; cast++)
        {
            _hub.Publish(new KillshotWindUp(V3.UnitX, 0.7f));
            _hub.Publish(new KillshotHit(cast, new V3(cast, 0f, 0f)));
            _hub.Publish(new KillshotFired(V3.Zero, V3.UnitX, 30f, 1));
            _view.Step(0.6f);
        }
        Assert.That(_view.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(children));
        _view.Dispose();
        _hub.Publish(new KillshotWindUp(V3.UnitZ, 0.7f));
        Assert.That(_view.IsWindingUp, Is.False);
        Assert.That(Child<ParticleSystem>("Hit Sparks").particleCount, Is.Zero);
    }

    private T Child<T>(string name) where T : Component => _view.transform.Find(name).GetComponent<T>();

    private GameObject Ranger(out RangerAnimatorView pose, out Animator animator)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Bodies/Ranger.prefab");
        var field = new SerializedObject(prefab.GetComponentInChildren<RangerAnimatorView>(true));
        Assert.That(field.FindProperty("_killshotPrefab").objectReferenceValue, Is.Not.Null);
        GameObject body = Object.Instantiate(prefab);
        pose = body.GetComponentInChildren<RangerAnimatorView>(true);
        animator = body.GetComponentInChildren<Animator>(true);
        // The dedicated view above isolates colour, timing and camera assertions from body placement.
        typeof(RangerAnimatorView).GetField("_killshotPrefab", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pose, null);
        return body;
    }
}
