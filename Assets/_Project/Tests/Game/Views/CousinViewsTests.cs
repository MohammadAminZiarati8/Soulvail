using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// The cousins' new moves, drawn (JG-21e): the Bullfrog's landing ring, clip and arc, and a chain
/// bomber's link and fuse. One test per behaviour rule.
/// </summary>
/// <remarks>
/// Bodies are built here rather than loaded from prefabs, <c>EnemyAnimatorViewTests</c>' and
/// <c>EnemyHitFeedbackTests</c>' way: the claim is what the views do with the events, not how the
/// art is dressed. <c>Awake</c> never runs in EditMode (Traps §5), so it is invoked where a view
/// needs it. Everything created is destroyed in the teardown.
/// </remarks>
[TestFixture]
public sealed class CousinViewsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string LinkMaterialPath = "Assets/_Project/Materials/Enemies/M_ChainLink.mat";
    private const int Id = 21;

    private static readonly int AttackId = Animator.StringToHash("Attack");
    private static readonly int LeapId = Animator.StringToHash("Leap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private DomainEventHub _hub;
    private GameObject _root;
    private Object _extra;
    private IObjectResolver _container;
    private TelegraphRings _rings;
    private ChainLinkViews _links;

    [SetUp]
    public void CreateHub()
    {
        _hub = new DomainEventHub();
    }

    [TearDown]
    public void DestroyAll()
    {
        _rings?.Dispose();
        _rings = null;
        _links?.Dispose();
        _links = null;
        _hub?.Dispose();
        _hub = null;
        _container?.Dispose();
        _container = null;

        if (_root != null)
        {
            Object.DestroyImmediate(_root);
        }

        if (_extra != null)
        {
            Object.DestroyImmediate(_extra);
        }
    }

    // ---- Rule 1: the landing ring --------------------------------------------------------------

    [Test]
    public void LeapAimed_RentsRingForCrouchAndAir()
    {
        _root = new GameObject("Decals");
        var prefabObject = new GameObject("RingPrefab");
        prefabObject.transform.SetParent(_root.transform, false);
        TelegraphRingView prefab = prefabObject.AddComponent<TelegraphRingView>();
        typeof(TelegraphRingView).GetField("_quad", Private)
            .SetValue(prefab, prefabObject.AddComponent<MeshRenderer>());

        _container = new ContainerBuilder().Build();
        _rings = new TelegraphRings(_container, prefab, _root.transform, _hub, prewarm: 1);

        _hub.Publish(new EnemyLeapAimed(Id, Vector3.Zero, new Vector3(3f, 0f, 4f), 1.6f, 0.6f, 0.5f));

        Assert.That(_rings.Count, Is.EqualTo(1));

        TelegraphRingView ring = LiveRing();
        Assert.That(ring.transform.position.x, Is.EqualTo(3f).Within(1e-4f));
        Assert.That(ring.transform.position.z, Is.EqualTo(4f).Within(1e-4f));
        Assert.That(ring.Radius, Is.EqualTo(1.6f).Within(1e-4f));
        Assert.That(ring.Fill, Is.EqualTo(0f).Within(1e-4f), "A landing ring fills, like a lob's.");

        _rings.Step(1.05f);
        Assert.That(_rings.Count, Is.EqualTo(1), "Still up through the crouch and most of the flight.");

        _rings.Step(0.1f);
        Assert.That(_rings.Count, Is.EqualTo(0), "Gone once 1.1 s have run.");
    }

    // ---- Rule 2: the clip ---------------------------------------------------------------------

    [Test]
    public void LeaperTelegraph_PlaysLeap()
    {
        (EnemyView _, Animator animator, _) = AnimatedBody();

        _hub.Publish(new EnemyLeapAimed(Id, Vector3.Zero, new Vector3(3f, 0f, 0f), 1.6f, 0.6f, 0.5f));
        _hub.Publish(new EnemyTelegraph(Id, 0.6f, move: 1));

        Assert.That(animator.GetBool(LeapId), Is.True, "A leaper's crouch plays Leap.");
        Assert.That(animator.GetBool(AttackId), Is.False, "…instead of the strike.");
    }

    [Test]
    public void LeaperTelegraph_OnAPlainFrogPlaysNothingNew()
    {
        (EnemyView _, Animator animator, _) = AnimatedBody();

        _hub.Publish(new EnemyTelegraph(Id, 0.6f, move: 1));

        Assert.That(animator.GetBool(LeapId), Is.False, "No leap was aimed, so no Leap.");
        Assert.That(animator.GetBool(AttackId), Is.True, "A Frog's wind-up is unchanged.");
    }

    // ---- Rule 3: the arc ----------------------------------------------------------------------

    [Test]
    public void HopArc_PeaksAndReturns()
    {
        (EnemyView body, _, Transform model) = AnimatedBody();

        _hub.Publish(new EnemyLeapAimed(Id, Vector3.Zero, new Vector3(3f, 0f, 0f), 1.6f, 0.6f, 0.5f));

        body.StepHop(0.6f);
        Assert.That(model.localPosition.y, Is.EqualTo(0f).Within(1e-4f), "On the ground through the crouch.");

        body.StepHop(0.25f);
        Assert.That(model.localPosition.y, Is.EqualTo(EnemyView.HopPeak).Within(1e-3f), "Peaks mid-air.");
        Assert.That(body.transform.position.y, Is.EqualTo(0f).Within(1e-6f), "The controller never rises.");

        body.StepHop(0.25f);
        Assert.That(model.localPosition.y, Is.EqualTo(0f).Within(1e-4f), "Down on landing.");
    }

    // ---- Rule 4: the link ---------------------------------------------------------------------

    [Test]
    public void ChainLit_DrawsLinkThatFades()
    {
        _links = Links(capacity: 16);

        _hub.Publish(new EnemyChainLit(Id, 7, new Vector3(1f, 0f, 2f), new Vector3(4f, 0f, 6f), 0.3f));

        Assert.That(_links.ActiveCount, Is.EqualTo(1));

        LineRenderer line = ActiveLine();
        Assert.That(line.GetPosition(0), Is.EqualTo(new UnityEngine.Vector3(1f, ChainLinkViews.Lift, 2f)));
        Assert.That(line.GetPosition(1), Is.EqualTo(new UnityEngine.Vector3(4f, ChainLinkViews.Lift, 6f)));
        Assert.That(line.startWidth, Is.EqualTo(0.15f).Within(1e-5f));
        Assert.That(ChainLinkViews.Lift, Is.EqualTo(0.3f).Within(1e-5f));

        float before = LinkStrength(line);
        _links.Tick(0.15f);
        Assert.That(LinkStrength(line), Is.LessThan(before), "It fades over the fuse.");

        _links.Tick(0.16f);
        Assert.That(_links.ActiveCount, Is.EqualTo(0), "Back in the pool once the fuse has run.");
    }

    // ---- Rule 5: the fuse flash ---------------------------------------------------------------

    [Test]
    public void ChainLit_FlashesUntilDeath()
    {
        _root = new GameObject("Bomber");
        GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.DestroyImmediate(mesh.GetComponent<Collider>());
        mesh.transform.SetParent(_root.transform, false);
        var renderer = mesh.GetComponent<MeshRenderer>();
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        _extra = material;
        renderer.sharedMaterial = material;

        EnemyView view = _root.AddComponent<EnemyView>();
        EnemyHitFeedback feedback = _root.AddComponent<EnemyHitFeedback>();
        typeof(EnemyHitFeedback).GetField("_renderer", Private).SetValue(feedback, renderer);
        typeof(EnemyHitFeedback).GetField("_dissolveMaterial", Private).SetValue(feedback, material);
        feedback.Construct(_hub);
        typeof(EnemyHitFeedback).GetMethod("Awake", Private).Invoke(feedback, null);
        view.Bind(Id, UnityEngine.Vector3.zero);

        _hub.Publish(new EnemyChainLit(Id, 7, Vector3.Zero, Vector3.Zero, 0.3f));
        TickFeedback(feedback, 0.01f);

        Assert.That(feedback.FuseLit, Is.True);
        Assert.That(Palette.IsDanger(BaseColour(renderer)), Is.True, "On: red-orange.");

        TickFeedback(feedback, 0.0625f);
        Assert.That(Palette.IsDanger(BaseColour(renderer)), Is.False, "Off half of each 8 Hz beat.");

        _hub.Publish(new EnemyDied(Id, default, Vector3.Zero));

        Assert.That(feedback.FuseLit, Is.False, "A death ends the fuse.");
    }

    // ---- Rule 6: the pool ---------------------------------------------------------------------

    [Test]
    public void Links_ReuseOldestPastCapacity()
    {
        _links = Links(capacity: 16);

        int built = _root.GetComponentsInChildren<LineRenderer>(true).Length;

        for (int i = 0; i < 17; i++)
        {
            _hub.Publish(new EnemyChainLit(i + 1, 0, Vector3.Zero, new Vector3(i, 0f, 0f), 0.3f));
        }

        Assert.That(_links.ActiveCount, Is.EqualTo(16));
        Assert.That(_root.GetComponentsInChildren<LineRenderer>(true).Length, Is.EqualTo(built),
            "Nothing is built after the constructor.");

        bool oldestRedrawn = false;
        bool newestDrawn = false;

        foreach (LineRenderer line in _root.GetComponentsInChildren<LineRenderer>(false))
        {
            oldestRedrawn |= Mathf.Approximately(line.GetPosition(1).x, 0f);
            newestDrawn |= Mathf.Approximately(line.GetPosition(1).x, 16f);
        }

        Assert.That(newestDrawn, Is.True, "The 17th is drawn…");
        Assert.That(oldestRedrawn, Is.False, "…over the first.");
    }

    // ---- Rule 7: reserved colours -------------------------------------------------------------

    [Test]
    public void CousinViews_UseNoReservedColour()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(LinkMaterialPath);

        Assert.That(material, Is.Not.Null, LinkMaterialPath);

        Color colour = material.GetColor(BaseColorId);

        Assert.That(Palette.IsDanger(colour), Is.True, "Links are danger red-orange.");
        Assert.That(Palette.IsDanger(Palette.Danger), Is.True, "Rings and fuse flashes are too.");

        foreach (Color reserved in new[] { Palette.Player, Palette.Veilrot, Palette.Essence })
        {
            float distance = Mathf.Abs(colour.r - reserved.r) + Mathf.Abs(colour.g - reserved.g)
                + Mathf.Abs(colour.b - reserved.b);
            Assert.That(distance, Is.GreaterThan(0.3f), $"Nowhere near reserved {reserved}.");
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private (EnemyView Body, Animator Animator, Transform Model) AnimatedBody()
    {
        var controller = new AnimatorController { name = "AC_CousinDouble", hideFlags = HideFlags.HideAndDontSave };
        controller.AddLayer("Base Layer");
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("WalkRate", AnimatorControllerParameterType.Float);
        controller.AddParameter("AttackRate", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Leap", AnimatorControllerParameterType.Trigger);
        _extra = controller;

        _root = new GameObject("Bullfrog");
        EnemyView body = _root.AddComponent<EnemyView>();

        var model = new GameObject("Model");
        model.transform.SetParent(_root.transform, false);

        Animator animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        EnemyAnimatorView view = model.AddComponent<EnemyAnimatorView>();
        typeof(EnemyAnimatorView).GetField("_animator", Private).SetValue(view, animator);
        view.Construct(_hub);

        body.Bind(Id, UnityEngine.Vector3.zero);

        return (body, animator, model.transform);
    }

    private ChainLinkViews Links(int capacity)
    {
        _root = new GameObject("Decals");
        var template = new GameObject("LinkTemplate");
        template.transform.SetParent(_root.transform, false);
        template.SetActive(false);
        LineRenderer prefab = template.AddComponent<LineRenderer>();

        // The template stays inactive, so only the pooled links are counted below.
        return new ChainLinkViews(_hub, prefab, _root.transform, capacity);
    }

    private TelegraphRingView LiveRing()
    {
        foreach (TelegraphRingView ring in _root.GetComponentsInChildren<TelegraphRingView>(false))
        {
            if (ring.IsLive)
            {
                return ring;
            }
        }

        Assert.Fail("No live ring.");

        return null;
    }

    private LineRenderer ActiveLine()
    {
        LineRenderer[] lines = _root.GetComponentsInChildren<LineRenderer>(false);

        Assert.That(lines.Length, Is.EqualTo(1));

        return lines[0];
    }

    private static float LinkStrength(LineRenderer line)
    {
        var block = new MaterialPropertyBlock();
        line.GetPropertyBlock(block);

        return block.GetColor(BaseColorId).r;
    }

    private static Color BaseColour(Renderer renderer)
    {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);

        return block.GetColor(BaseColorId);
    }

    private static void TickFeedback(EnemyHitFeedback feedback, float dt) =>
        typeof(EnemyHitFeedback).GetMethod("Tick", Private).Invoke(feedback, new object[] { dt });
}
