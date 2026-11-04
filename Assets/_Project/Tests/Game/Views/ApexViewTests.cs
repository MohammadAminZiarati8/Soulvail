using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEditor;
using UnityEngine;
using Num = System.Numerics;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class ApexViewTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Apex.prefab";
    private const int Id = 30;
    private const int LaneId = 407;
    private GameObject _root;
    private EnemyView _body;
    private ApexView _view;
    private Animator _animator;
    private DomainEventHub _hub;

    [SetUp]
    public void Rent()
    {
        _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        _body = _root.GetComponent<EnemyView>();
        _body.Bind(Id, Vector3.zero);
        _animator = _root.GetComponentInChildren<Animator>();
        _animator.Rebind();
        _animator.Update(0f);
        _hub = new DomainEventHub();
        _view = _root.GetComponent<ApexView>();
        _view.Construct(_hub);
        _root.GetComponent<EnemyHitFeedback>().Construct(_hub);
        Invoke(_root.GetComponent<EnemyHitFeedback>(), "Awake");
    }

    [TearDown]
    public void Return()
    {
        Object.DestroyImmediate(_root);
        _hub.Dispose();
    }

    [Test]
    public void Lane_PlaysCrouchThenDash()
    {
        // A lane id is independent of its attacker's id. Even a colliding numeric id is foreign.
        _hub.Publish(Lane(Id, new Num.Vector3(8f, 0f, 0f)));
        _hub.Publish(new LaneFired(Id));
        State("Idle");
        _hub.Publish(new EnemyTelegraph(Id, 0.8f, 1));
        State("Crouch");
        _hub.Publish(Lane(Id, new Num.Vector3(8f, 0f, 0f)));
        _hub.Publish(new LaneFired(Id));
        State("Crouch");
        _hub.Publish(Lane(LaneId, Num.Vector3.Zero));
        _hub.Publish(new LaneFired(LaneId + 1));
        State("Crouch");
        _hub.Publish(new LaneFired(LaneId));
        State("Dash");
        Assert.That(_view.EyesLit, Is.False);
    }

    [Test]
    public void Rake_Telegraph_PlaysRake()
    {
        _hub.Publish(new EnemyTelegraph(Id + 1, 1.5f, 2));
        State("Idle");
        _hub.Publish(new EnemyTelegraph(Id, 1.5f, 2));
        State("Rake");
        Assert.That(_animator.GetFloat("RakeRate"), Is.EqualTo(1f / 1.5f).Within(0.001f));
    }

    [Test]
    public void Crouch_Pounce_Land_Play()
    {
        RealPounce();
        State("Crouch");
        _view.Step(0.59f);
        State("Crouch");
        _view.Step(0.02f);
        State("Pounce");
        _body.StepHop(0.85f);
        Assert.That(_animator.transform.localPosition.y, Is.GreaterThan(0.8f), "EnemyView supplies the arc.");
        Land(1.2f);
        State("Land");
        Assert.That(_animator.transform.localPosition.y, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void Phase_PlaysRoar()
    {
        RealPounce();
        _view.Step(0.65f);
        _hub.Publish(new BossPhaseChanged(Id + 1, 1, 2));
        State("Pounce");
        _hub.Publish(new BossPhaseChanged(Id, 1, 2));
        State("Roar");
        Assert.That(_view.EyesLit || _view.IsStreakPlaying || _view.IsAirborne || _view.InWindow, Is.False);
        Assert.That(_animator.transform.localPosition.y, Is.Zero);
        _hub.Publish(new LaneFired(LaneId));
        State("Roar");
    }

    [Test]
    public void RealCrouch_EyesLitDanger_UntilLanding()
    {
        RealPounce();
        AssertEyes(true);
        _view.Step(0.65f);
        AssertEyes(true);
        _view.Step(0.65f);
        AssertEyes(true, "Only the landing event extinguishes the tell, even after the predicted flight.");
        _hub.Publish(new ApexLanded(Id + 1, Num.Vector3.Zero, 1f));
        AssertEyes(true);
        Land(1.2f);
        AssertEyes(false);
    }

    [Test]
    public void Feint_EyesDark_TailFlicks()
    {
        Transform tail = Bone("tail_3");
        _hub.Publish(new ApexCrouched(Id, true, 0.6f));
        _hub.Publish(new EnemyTelegraph(Id, 0.6f, 4));
        State("Crouch");
        Quaternion before = tail.localRotation;
        _view.Step(0.08f);
        Assert.That(Quaternion.Angle(before, tail.localRotation), Is.GreaterThan(10f));
        Assert.That(_view.IsTailFlicking, Is.True);
        AssertEyes(false);
        Assert.That(_view.IsStreakPlaying, Is.False);
        _view.Step(0.6f);
        Assert.That(_view.IsTailFlicking, Is.False);
        var feedback = new SerializedObject(_root.GetComponent<EnemyHitFeedback>());
        Assert.That(feedback.FindProperty("_telegraphGlow").floatValue, Is.Zero, "No red-orange body flash gives the feint away.");
        Assert.That(feedback.FindProperty("_telegraphSwell").floatValue, Is.EqualTo(1f));
    }

    [Test]
    public void Streak_OnlyWhileDashingOrAirborne()
    {
        Assert.That(_view.IsStreakPlaying, Is.False);
        _hub.Publish(new EnemyTelegraph(Id, 0.8f, 1));
        _hub.Publish(Lane(LaneId, Num.Vector3.Zero));
        Assert.That(_view.IsStreakPlaying, Is.False);
        Move(12f, 0.01f);
        _hub.Publish(new LaneFired(LaneId));
        _view.Step(0.01f);
        Assert.That(_view.IsStreakPlaying, Is.True);
        Move(0f, 0.01f);
        _view.Step(0.01f);
        Assert.That(_view.IsStreakPlaying, Is.False, "A dash stopped at stone ends emission before the lane closes.");
        RealPounce();
        Assert.That(_view.IsStreakPlaying, Is.False);
        _view.Step(0.65f);
        Assert.That(_view.IsStreakPlaying, Is.True);
        Land(1f);
        Assert.That(_view.IsStreakPlaying, Is.False);
    }

    [Test]
    public void Lane_StoneEndpointAndPhaseCancel_IgnoreOldLanes()
    {
        _hub.Publish(new EnemyTelegraph(Id, 0.8f, 1));
        _hub.Publish(Lane(LaneId, Num.Vector3.Zero));
        Move(12f, 0.01f);
        _hub.Publish(new LaneFired(LaneId));
        _root.transform.position = new Vector3(0f, 0f, 4f);
        _view.Step(0.01f);
        Assert.That(_view.IsStreakPlaying, Is.False, "The probe-cut end stops the streak even before the next sensed velocity.");
        _hub.Publish(new EnemyTelegraph(Id, 0.8f, 1));
        _hub.Publish(Lane(LaneId + 1, new Num.Vector3(0f, 0f, 4f)));
        _hub.Publish(new BossPhaseChanged(Id, 1, 2));
        _hub.Publish(new LaneFired(LaneId + 1));
        _hub.Publish(new LaneFired(LaneId));
        State("Roar");
        Assert.That(_view.IsStreakPlaying, Is.False, "A phase-cancelled dash leaves its hazard alive but never moves the body.");
    }
    [Test]
    public void Streak_NotCyanNotDanger()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Vfx/FX_Apex_Streak.prefab");
        ParticleSystem particles = prefab.GetComponent<ParticleSystem>();
        Color colour = particles.main.startColor.color;
        Color.RGBToHSV(colour, out float hue, out _, out float value);
        Assert.That(hue, Is.InRange(0.7f, 0.85f), "Violet, outside the cyan band.");
        Assert.That(value, Is.LessThan(0.35f), "A dark streak.");
        Assert.That(Palette.IsDanger(colour), Is.False);
        Assert.That(particles.main.playOnAwake, Is.False);
        Assert.That(particles.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));
        Assert.That(prefab.GetComponent<ParticleSystemRenderer>().sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(Color.white));
    }

    [Test]
    public void Landed_BrightensForWindow_ThenRestores()
    {
        Renderer renderer = _root.GetComponentInChildren<SkinnedMeshRenderer>();
        var original = new MaterialPropertyBlock();
        Color tint = new Color(0.7f, 0.6f, 0.5f, 1f);
        original.SetColor("_BaseColor", tint);
        renderer.SetPropertyBlock(original);
        float standingHead = Bone("head").position.y;
        Land(1.2f);
        _view.Step(0.1f);
        Assert.That(BaseColour(renderer, 0).r, Is.EqualTo(tint.r * 1.15f).Within(0.0001f));
        _view.Step(0.1f);
        Assert.That(BaseColour(renderer, 0).r, Is.EqualTo(tint.r * 1.15f).Within(0.0001f), "No compounding per frame.");
        Evaluate(0.5f);
        State("Window");
        Assert.That(Bone("head").position.y, Is.LessThan(standingHead - 0.03f), "Head low in the opening.");
        float chest = Bone("chest").localScale.y;
        Evaluate(0.15f);
        Assert.That(Mathf.Abs(Bone("chest").localScale.y - chest), Is.GreaterThan(0.001f), "Panting while the window is open.");
        _view.Step(1.01f);
        Assert.That(_view.InWindow, Is.False);
        var slot = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(slot, 0);
        Assert.That(slot.isEmpty, Is.True);
        Assert.That(BaseColour(renderer).r, Is.EqualTo(tint.r).Within(0.0001f));
        Evaluate(0.2f);
        State("Idle");
    }

    [Test]
    public void Despawn_ClearsEyesStreakAndWindow()
    {
        RealPounce();
        _view.Step(0.65f);
        Despawn();
        AssertEyes(false);
        Assert.That(_view.IsStreakPlaying || _view.IsAirborne || _view.IsTailFlicking || _view.InWindow, Is.False);
        Assert.That(_root.GetComponentsInChildren<ParticleSystem>().All(p => p.particleCount == 0), Is.True);
        _root.SetActive(true);
        _body.Bind(Id + 1, Vector3.zero);
        _animator.Update(0f);
        State("Idle");
        _hub.Publish(new ApexCrouched(Id, false, 1f));
        AssertEyes(false);
        _hub.Publish(new ApexLanded(Id + 1, Num.Vector3.Zero, 1f));
        Assert.That(_view.InWindow, Is.True);
        Despawn();
        Assert.That(_view.InWindow, Is.False);
        var block = new MaterialPropertyBlock();
        _root.GetComponentInChildren<SkinnedMeshRenderer>().GetPropertyBlock(block, 0);
        Assert.That(block.isEmpty, Is.True);
    }

    [Test]
    public void HitAndDeath_RespectIdentityAndClearMove()
    {
        RealPounce();
        _hub.Publish(new EnemyDamaged(Id, 5f, 0.5f, false, true));
        _animator.Update(0f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(1).IsName("Empty"), Is.True);
        _hub.Publish(new EnemyDamaged(Id, 5f, 0.5f, false));
        _animator.Update(0f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(1).IsName("Hit"), Is.True);
        State("Crouch", "The flinch layer preserves the move.");
        _hub.Publish(new EnemyDied(Id, new ContentId("enemy.apex"), Num.Vector3.Zero));
        State("Death");
        AssertEyes(false);
        Assert.That(_view.IsStreakPlaying || _view.InWindow, Is.False);
        _hub.Publish(new ApexCrouched(Id, false, 0.6f));
        State("Death");
    }

    [Test]
    public void Locomotion_RunsAboveHalfAMetrePerSecond()
    {
        Move(1f, 0.1f);
        _view.Step(0.1f);
        Evaluate(0.2f);
        State("Run");
        Move(0.4f, 0.1f);
        _view.Step(0.1f);
        Evaluate(0.2f);
        State("Idle");
        Assert.That(_animator.applyRootMotion, Is.False);
    }

    [Test]
    public void SteadyViews_AllocateNothing()
    {
        RealPounce();
        AllocationAssert.None(() => _view.Step(0.000001f), 1000);
        Land(100f);
        AllocationAssert.None(() => _view.Step(0.000001f), 1000);
    }

    [Test]
    public void Destroy_Unsubscribes()
    {
        Assert.That(_hub.SubscriberCount<ApexCrouched>(), Is.EqualTo(1));
        Invoke(_view, "OnDestroy"); // EditMode does not dispatch MonoBehaviour lifecycle messages.
        Object.DestroyImmediate(_root);
        Assert.That(_hub.SubscriberCount<ApexCrouched>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<LaneArmed>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<EnemyLeapAimed>(), Is.Zero);
    }

    private static void Invoke(MonoBehaviour target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

    private static LaneArmed Lane(int id, Num.Vector3 origin) => new(id, origin, Num.Vector2.UnitY, 4f, 0.8f, 0.8f);

    private void RealPounce()
    {
        _hub.Publish(new ApexCrouched(Id, false, 0.6f));
        _hub.Publish(new EnemyLeapAimed(Id, Num.Vector3.Zero, new Num.Vector3(0f, 0f, 5f), 2f, 0.6f, 0.5f));
        _hub.Publish(new EnemyTelegraph(Id, 0.6f, 3));
    }

    private void Land(float seconds) => _hub.Publish(new ApexLanded(Id, Num.Vector3.Zero, seconds));

    private void Despawn()
    {
        _body.OnDespawn();
        _root.SetActive(false); // ViewPool calls EnemyView's IPoolable, then deactivates the body.
        Invoke(_view, "OnDisable"); // The callback runs after deactivation; EditMode dispatches it explicitly.
    }

    private void Move(float speed, float dt) => _body.Apply(new EnemyMoveIntent(Id, new Num.Vector3(0f, 0f, speed), Num.Vector2.UnitY), dt);

    private Transform Bone(string name) => _root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);

    private void State(string name, string message = null)
    {
        _animator.Update(0f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName(name), Is.True, message ?? name);
    }

    private void Evaluate(float dt)
    {
        for (int i = 0; i < Mathf.CeilToInt(dt / 0.01f); i++)
        {
            _animator.Update(0.01f);
        }
    }

    private void AssertEyes(bool lit, string message = null)
    {
        Assert.That(_view.EyesLit, Is.EqualTo(lit), message);
        Renderer[] eyes = _root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("Eye")).ToArray();
        Assert.That(eyes, Has.Length.EqualTo(2));
        foreach (Renderer eye in eyes)
        {
            Assert.That(eye.enabled, Is.EqualTo(lit));
            Assert.That(eye.transform.parent.name, Is.EqualTo("Head"));
            var block = new MaterialPropertyBlock();
            eye.GetPropertyBlock(block);
            Color emission = block.GetColor("_EmissionColor") / ApexView.EyeIntensity;
            Assert.That(emission.r, Is.EqualTo(Palette.Vertex(Palette.Danger).r).Within(0.001f));
            Assert.That(emission.g, Is.EqualTo(Palette.Vertex(Palette.Danger).g).Within(0.001f));
            Assert.That(emission.b, Is.EqualTo(Palette.Vertex(Palette.Danger).b).Within(0.001f));
            Assert.That(eye.sharedMaterial.IsKeywordEnabled("_EMISSION"), Is.True);
            ParticleSystem halo = eye.GetComponentInChildren<ParticleSystem>();
            Assert.That(halo, Is.Not.Null, "A local bloom works with the Run camera post-processing off.");
            Assert.That(halo.particleCount, Is.EqualTo(lit ? 1 : 0));
        }
    }

    private static Color BaseColour(Renderer renderer, int slot = -1)
    {
        var block = new MaterialPropertyBlock();
        if (slot < 0)
        { renderer.GetPropertyBlock(block); }
        else
        { renderer.GetPropertyBlock(block, slot); }
        return block.GetColor("_BaseColor");
    }
}
