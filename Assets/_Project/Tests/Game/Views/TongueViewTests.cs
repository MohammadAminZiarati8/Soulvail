using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// Old Croaker's drawn tongue (JG-06a): laid along the lane that fired, out, held, back, and hidden.
/// </summary>
/// <remarks>
/// <c>Awake</c> never runs in EditMode (Traps §5), so the bodies here are built bare and
/// <see cref="TongueView.Fire"/> finds its own filter and renderer — the same path a body takes
/// when its fields were never dragged in.
/// </remarks>
[TestFixture]
public sealed class TongueViewTests
{
    private const string TonguePath = "Assets/_Project/Prefabs/Vfx/VFX_Tongue.prefab";

    private const float Tolerance = 1e-3f;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    [TearDown]
    public void DestroyAll()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null)
            {
                Object.DestroyImmediate(_spawned[i]);
            }
        }

        _spawned.Clear();
    }

    [Test]
    public void Fire_PointsAlongTheLane_FromTheMouth()
    {
        TongueView tongue = Body("Tongue");
        var origin = new Vector3(2f, 0f, -1f);

        tongue.Fire(origin, new Vector2(1f, 0f), 9f);

        var mouth = new Vector3(2f + TongueView.MouthForward, TongueView.MouthHeight, -1f);
        var tip = new Vector3(2f + 9f, TongueView.TipHeight, -1f);

        Assert.That(Vector3.Distance(tongue.transform.position, mouth), Is.LessThan(Tolerance),
            "The tongue leaves the mouth, above and ahead of the body's centre.");
        Assert.That(Vector3.Angle(tongue.transform.forward, tip - mouth), Is.LessThan(0.1f),
            "It points at the lane's far end, sloping down to the ground.");
        Assert.That(tongue.FullLength, Is.EqualTo(Vector3.Distance(mouth, tip)).Within(Tolerance));
        Assert.That(tongue.GetComponent<MeshRenderer>().enabled, Is.True);
    }

    [Test]
    public void Step_Shoots_Holds_Retracts()
    {
        TongueView tongue = Body("Tongue");

        tongue.Fire(Vector3.zero, new Vector2(0f, 1f), 9f);

        Assert.That(tongue.Length, Is.EqualTo(0f).Within(Tolerance), "It starts in the mouth.");

        tongue.Step(TongueView.ShootSeconds * 0.5f);
        float halfway = tongue.Length;

        Assert.That(halfway, Is.GreaterThan(0f).And.LessThan(tongue.FullLength));

        tongue.Step(TongueView.ShootSeconds * 0.5f);
        Assert.That(tongue.Length, Is.EqualTo(tongue.FullLength).Within(Tolerance), "Full length when shot.");

        tongue.Step(TongueView.HoldSeconds * 0.9f);
        Assert.That(tongue.Length, Is.EqualTo(tongue.FullLength).Within(Tolerance), "Held.");

        tongue.Step((TongueView.HoldSeconds * 0.1f) + (TongueView.RetractSeconds * 0.5f));
        Assert.That(tongue.Length, Is.GreaterThan(0f).And.LessThan(tongue.FullLength), "Coming back.");
        Assert.That(tongue.IsLive, Is.True);
    }

    [Test]
    public void Step_PastTheLife_Hides()
    {
        TongueView tongue = Body("Tongue");

        tongue.Fire(Vector3.zero, new Vector2(0f, 1f), 9f);
        tongue.Step(TongueView.Lifetime + 0.01f);

        Assert.That(tongue.IsLive, Is.False);
        Assert.That(tongue.Length, Is.EqualTo(0f));
        Assert.That(tongue.GetComponent<MeshRenderer>().enabled, Is.False);
    }

    [Test]
    public void Fire_Again_Restarts()
    {
        TongueView tongue = Body("Tongue");

        tongue.Fire(Vector3.zero, new Vector2(0f, 1f), 9f);
        tongue.Step(TongueView.Lifetime + 0.01f);
        tongue.Fire(Vector3.zero, new Vector2(-1f, 0f), 6f);

        Assert.That(tongue.IsLive, Is.True);
        Assert.That(tongue.Length, Is.EqualTo(0f).Within(Tolerance), "A second strike starts in the mouth.");
        Assert.That(Vector3.Angle(tongue.transform.forward, Vector3.left), Is.LessThan(30f));
    }

    [Test]
    public void Step_NonFiniteOrNegative_DoesNothing()
    {
        TongueView tongue = Body("Tongue");

        tongue.Fire(Vector3.zero, new Vector2(0f, 1f), 9f);
        tongue.Step(float.NaN);
        tongue.Step(float.PositiveInfinity);
        tongue.Step(-1f);

        Assert.That(tongue.IsLive, Is.True);
        Assert.That(tongue.Length, Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void BossViews_LaneFired_FiresATongue()
    {
        IObjectResolver container = new ContainerBuilder().Build();
        var hub = new DomainEventHub();
        var enemies = new EnemyViews(
            container,
            Keep(new GameObject("EnemyPrefab")).AddComponent<EnemyView>(),
            null,
            hub,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()),
            prewarm: 0);

        var views = new BossViews(
            container,
            Decal<ShockwaveView>("ShockwavePrefab"),
            Decal<FissureView>("FissurePrefab"),
            Decal<BossBeatView>("BeatPrefab"),
            Keep(new GameObject("Decals")).transform,
            hub,
            enemies,
            null,
            shockwavePrewarm: 0,
            fissurePrewarm: 0,
            beatPrewarm: 0,
            tonguePrefab: Body("TonguePrefab"));

        try
        {
            hub.Publish(new LaneArmed(
                7,
                new System.Numerics.Vector3(1f, 0f, 1f),
                new System.Numerics.Vector2(0f, 1f),
                9f,
                0.8f,
                0.8f));

            Assert.That(views.TongueCount, Is.EqualTo(0), "Nothing strikes while the lane arms.");

            hub.Publish(new LaneFired(7));

            Assert.That(views.TongueCount, Is.EqualTo(1));

            TongueView tongue = views.Tongue(0);

            Assert.That(tongue.IsLive, Is.True);
            Assert.That(
                Vector3.Distance(
                    tongue.transform.position,
                    new Vector3(1f, TongueView.MouthHeight, 1f + TongueView.MouthForward)),
                Is.LessThan(Tolerance),
                "The tongue leaves the mouth of the lane's own origin.");

            views.Step(TongueView.Lifetime + 0.01f);

            Assert.That(views.TongueCount, Is.EqualTo(0), "Back in the mouth, back in the pool.");
        }
        finally
        {
            views.Dispose();
            enemies.Dispose();
            hub.Dispose();
            container.Dispose();
        }
    }

    [Test]
    public void BossViews_WithNoTonguePrefab_StillDrawsTheLane()
    {
        IObjectResolver container = new ContainerBuilder().Build();
        var hub = new DomainEventHub();
        var enemies = new EnemyViews(
            container,
            Keep(new GameObject("EnemyPrefab")).AddComponent<EnemyView>(),
            null,
            hub,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()),
            prewarm: 0);

        var views = new BossViews(
            container,
            Decal<ShockwaveView>("ShockwavePrefab"),
            Decal<FissureView>("FissurePrefab"),
            Decal<BossBeatView>("BeatPrefab"),
            null,
            hub,
            enemies,
            null,
            shockwavePrewarm: 0,
            fissurePrewarm: 0,
            beatPrewarm: 0);

        try
        {
            hub.Publish(new LaneArmed(
                3, System.Numerics.Vector3.Zero, new System.Numerics.Vector2(1f, 0f), 9f, 0.8f, 0.8f));
            hub.Publish(new LaneFired(3));

            Assert.That(views.LaneCount, Is.EqualTo(1));
            Assert.That(views.TongueCount, Is.EqualTo(0));
        }
        finally
        {
            views.Dispose();
            enemies.Dispose();
            hub.Dispose();
            container.Dispose();
        }
    }

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TonguePath);

        Assert.That(prefab, Is.Not.Null, TonguePath);
        Assert.That(prefab.GetComponent<TongueView>(), Is.Not.Null,
            "A null component here is Traps §5's block-namespace trap.");
        Assert.That(prefab.GetComponent<MeshFilter>(), Is.Not.Null);

        var renderer = prefab.GetComponent<MeshRenderer>();

        Assert.That(renderer, Is.Not.Null);
        Assert.That(renderer.sharedMaterial, Is.Not.Null);
        Assert.That(renderer.sharedMaterial.name, Is.EqualTo("M_Impact"));
    }

    /// <summary>A bare tongue body: the filter and renderer it finds for itself.</summary>
    private TongueView Body(string name)
    {
        var body = Keep(new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)));

        return body.AddComponent<TongueView>();
    }

    /// <summary>A hazard prefab stand-in: a quad body with the component on it.</summary>
    private T Decal<T>(string name)
        where T : Component
    {
        GameObject body = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));

        body.name = name;

        return body.AddComponent<T>();
    }

    private GameObject Keep(GameObject body)
    {
        _spawned.Add(body);

        return body;
    }
}
