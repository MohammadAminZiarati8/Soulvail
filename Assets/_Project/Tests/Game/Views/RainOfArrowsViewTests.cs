using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class RainOfArrowsViewTests
{
    private RainOfArrowsView _view;
    private DomainEventHub _hub;

    [SetUp]
    public void SetUp()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<RainOfArrowsView>("Assets/_Project/Prefabs/Vfx/VFX_RainOfArrows.prefab");
        Assert.That(prefab, Is.Not.Null);
        _view = Object.Instantiate(prefab);
        _hub = new DomainEventHub();
        _view.Construct(_hub, null, null);
    }

    [TearDown]
    public void TearDown()
    {
        if (_view != null)
        {
            _view.Dispose();
            Object.DestroyImmediate(_view.gameObject);
        }
        _hub.Dispose();
    }

    [Test]
    public void View_RingMatchesRadiusAndDuration()
    {
        var centre = new Vector3(3f, 0f, -2f);
        _view.Bind(centre, 2f, 2f, false, Vector3.up, Vector3.up);
        Transform ring = _view.transform.Find("Cyan Rim");
        Assert.That(ring.position, Is.EqualTo(centre + Vector3.up * 0.075f));
        Assert.That(ring.localScale, Is.EqualTo(new Vector3(4f, 4f, 1f)));
        var properties = new MaterialPropertyBlock();
        ring.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
        Assert.That(Vector4.Distance(properties.GetColor("_BaseColor"), Palette.Player), Is.LessThan(0.00001f));
        _view.Step(1.99f);
        Assert.That(_view.IsLive, Is.True);
        Assert.That(_view.Radius, Is.EqualTo(2f));
        _view.Step(0.01f);
        Assert.That(_view.IsLive, Is.False);
        Assert.That(_view.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void View_RainsInsideTheRing()
    {
        var centre = new Vector3(3f, 0f, -2f);
        _view.Bind(centre, 1.5f, 2f, false, Vector3.up, Vector3.up);
        var arrows = _view.GetComponentsInChildren<ProjectileView>(true);
        for (int frame = 0; frame < 119; frame++)
        {
            _view.Step(1f / 60f);
            for (int slot = 1; slot < arrows.Length; slot++)
            {
                if (!arrows[slot].gameObject.activeSelf)
                {
                    continue;
                }
                Vector3 offset = _view.LandingAt(slot) - centre;
                Assert.That(new Vector2(offset.x, offset.z).magnitude, Is.LessThanOrEqualTo(1.50001f));
                Assert.That(arrows[slot].transform.forward.y, Is.LessThan(-0.95f));
                Assert.That(arrows[slot].transform.localScale, Is.EqualTo(Vector3.one * 0.8f));
            }
        }
        Assert.That(_view.SpawnedArrows, Is.InRange(29, 30));
        Assert.That(_view.GetComponentInChildren<ParticleSystem>(true).particleCount, Is.GreaterThan(0));
    }

    [Test]
    public void View_RainWaitsForTheSkywardStreak()
    {
        _view.Bind(Vector3.zero, 1.5f, 2f, false, Vector3.up, new Vector3(0f, 0.8660254f, 0.5f));
        var streak = _view.GetComponentsInChildren<ProjectileView>(true)[0];
        Assert.That(streak.gameObject.activeSelf, Is.True);
        Assert.That(streak.transform.forward.y, Is.EqualTo(0.8660254f).Within(0.001f));
        var trail = streak.GetComponentInChildren<TrailRenderer>();
        Assert.That(trail.startColor, Is.EqualTo(Palette.Player));
        _view.Step(0.14f);
        Assert.That(_view.SpawnedArrows, Is.Zero);
        _view.Step(0.02f);
        Assert.That(streak.gameObject.activeSelf, Is.False);
        Assert.That(_view.SpawnedArrows, Is.EqualTo(1));
    }

    [Test]
    public void View_Rank3_TintsFill()
    {
        _view.Bind(Vector3.zero, 2f, 2f, true, Vector3.up, Vector3.up);
        Assert.That(_view.FillColour, Is.EqualTo(new Color(0.65f, 0.85f, 1f, 0.14f)));
        var properties = new MaterialPropertyBlock();
        _view.transform.Find("Faint Fill").GetComponent<MeshRenderer>().GetPropertyBlock(properties);
        Assert.That(properties.GetColor("_BaseColor"), Is.EqualTo(_view.FillColour));
        _view.Bind(Vector3.zero, 1.5f, 2f, false, Vector3.up, Vector3.up);
        Assert.That(_view.FillColour.a, Is.EqualTo(0.06f));
    }

    [Test]
    public void View_Pooled_EndsWithZone()
    {
        int[] ids = _view.GetComponentsInChildren<Transform>(true).Select(t => t.GetInstanceID()).ToArray();
        for (int cast = 0; cast < 20; cast++)
        {
            _view.Bind(Vector3.right * cast, 1.5f, 2f, false, Vector3.up, Vector3.up);
            for (int frame = 0; frame < 40; frame++)
            {
                _view.Step(0.05f);
            }
            _view.Step(0.01f);
            Assert.That(_view.IsLive, Is.False);
            Assert.That(_view.GetComponentInChildren<ParticleSystem>(true).particleCount, Is.Zero);
            Assert.That(_view.GetComponentsInChildren<ProjectileView>(true).All(a => !a.gameObject.activeSelf), Is.True);
        }
        Assert.That(_view.PoolSize, Is.EqualTo(8));
        Assert.That(_view.GetComponentsInChildren<Transform>(true).Select(t => t.GetInstanceID()).ToArray(), Is.EqualTo(ids));
    }

    [Test]
    public void View_CastEvent_AndBoundaries()
    {
        var cast = new RainOfArrowsCast(new System.Numerics.Vector3(2f, 0f, 4f), 1.5f, 2f);
        _hub.Publish(cast);
        Assert.That(_view.IsLive, Is.True);
        _hub.Publish(new StageTransitionStarted(12, 0.5f));
        Assert.That(_view.IsLive, Is.False);
        _hub.Publish(cast);
        _hub.Publish(new StageArrived(13, default));
        Assert.That(_view.IsLive, Is.False);
        _hub.Publish(cast);
        _hub.Publish(new RunEnded(2f));
        Assert.That(_view.IsLive, Is.False);
        _view.Dispose();
        Object.DestroyImmediate(_view.gameObject);
        Assert.That(_hub.SubscriberCount<RainOfArrowsCast>(), Is.Zero);
    }

    [Test]
    public void CastUp_UsesTheRigClipOnTheUpperBodyOnly()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animation/Controllers/AC_Ranger.controller");
        Assert.That(controller.parameters.Any(p => p.name == "CastUp" && p.type == AnimatorControllerParameterType.Trigger), Is.True);
        AnimatorStateMachine upper = controller.layers[1].stateMachine;
        AnimatorState cast = upper.states.Single(s => s.state.name == "Cast Up").state;
        Assert.That(cast.motion.name, Is.EqualTo("Ranged_Bow_Release_Up"));
        Assert.That(upper.anyStateTransitions.Any(t => t.destinationState == cast && t.conditions.Any(c => c.parameter == "CastUp")), Is.True);
        Assert.That(controller.layers[0].stateMachine.states.Any(s => s.state.name == "Cast Up"), Is.False);
        var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Bodies/Ranger.prefab");
        var so = new SerializedObject(body.GetComponentInChildren<RangerAnimatorView>(true));
        Assert.That(so.FindProperty("_rainPrefab").objectReferenceValue, Is.Not.Null);
    }
}
