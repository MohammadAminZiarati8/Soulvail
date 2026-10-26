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
/// A slam's landing felt (JG-06b): the camera's thud, the earth-and-dust burst, and <c>BossViews</c>
/// putting both on every <c>ShockwaveEmitted</c>.
/// </summary>
/// <remarks><c>Awake</c> never runs in EditMode (Traps §5): the bodies find their own filters.</remarks>
[TestFixture]
public sealed class SlamImpactTests
{
    private const string DustPath = "Assets/_Project/Prefabs/Vfx/VFX_SlamDust.prefab";

    private const float Frame = 1f / 60f;

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
    public void NoKick_NoOffset()
    {
        CameraShake shake = Shake();

        shake.Step(Frame);

        Assert.That(shake.IsShaking, Is.False);
        Assert.That(shake.Offset, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void Kick_Shakes_ThenRestsAtZero()
    {
        CameraShake shake = Shake();

        shake.Kick(CameraShake.SlamStrength);

        Assert.That(shake.IsShaking, Is.True);
        Assert.That(shake.Offset.magnitude, Is.GreaterThan(0f), "The first swing is immediate.");

        float largest = 0f;

        for (float t = 0f; t < CameraShake.Seconds - Frame; t += Frame)
        {
            largest = Mathf.Max(largest, shake.Offset.magnitude);
            shake.Step(Frame);
        }

        Assert.That(largest, Is.LessThanOrEqualTo(CameraShake.SlamStrength + 1e-5f),
            "Never a longer swing than the strength.");

        shake.Step(Frame * 2f);

        Assert.That(shake.IsShaking, Is.False);
        Assert.That(shake.Offset, Is.EqualTo(Vector3.zero), "At rest by Seconds.");
    }

    [Test]
    public void Kick_DuringShake_TakesTheStronger()
    {
        CameraShake strong = Shake();
        CameraShake twice = Shake();

        strong.Kick(0.4f);

        twice.Kick(0.4f);
        twice.Step(0.1f);
        twice.Kick(0.1f);

        Assert.That(twice.Offset, Is.EqualTo(strong.Offset),
            "A weak kick mid-shake restarts at the stronger, not at the weak one.");

        CameraShake doubled = Shake();

        doubled.Kick(0.4f);
        doubled.Kick(0.4f);

        Assert.That(doubled.Offset, Is.EqualTo(strong.Offset), "Two kicks never add up.");
    }

    [Test]
    public void Kick_NonPositiveOrNonFinite_IsIgnored()
    {
        CameraShake shake = Shake();

        shake.Kick(0f);
        shake.Kick(-1f);
        shake.Kick(float.NaN);
        shake.Kick(float.PositiveInfinity);

        Assert.That(shake.IsShaking, Is.False);
    }

    [Test]
    public void Dust_Plays_ThenEnds()
    {
        SlamDustView dust = Dust("Dust");
        var at = new Vector3(3f, 0f, -2f);

        dust.Play(at, 5);

        Assert.That(dust.IsLive, Is.True);
        Assert.That(dust.transform.position, Is.EqualTo(at));

        dust.Step(0.15f);

        float highest = 0f;

        for (int i = 0; i < SlamDustView.ChunkCount; i++)
        {
            highest = Mathf.Max(highest, dust.ChunkOffset(i).y);
        }

        Assert.That(highest, Is.GreaterThan(0.3f), "The chunks are thrown up.");

        Assert.That(dust.Step(SlamDustView.Lifetime - 0.15f - 0.05f), Is.True);

        for (int i = 0; i < SlamDustView.ChunkCount; i++)
        {
            Assert.That(dust.ChunkOffset(i).y, Is.LessThan(0.05f), $"Chunk {i} has come back down.");
        }

        for (int i = 0; i < SlamDustView.BillowCount; i++)
        {
            Vector3 offset = dust.BillowOffset(i);

            Assert.That(new Vector2(offset.x, offset.z).magnitude, Is.GreaterThan(SlamDustView.StartRadius),
                $"Billow {i} has rolled out.");
        }

        Assert.That(dust.Step(0.1f), Is.False, "Gone by Lifetime.");
        Assert.That(dust.IsLive, Is.False);
    }

    [Test]
    public void Dust_Replayed_StartsClean()
    {
        SlamDustView dust = Dust("Dust");

        dust.Play(Vector3.zero, 1);
        dust.Step(0.3f);
        dust.OnDespawn();

        Assert.That(dust.IsLive, Is.False);
        Assert.That(dust.Step(Frame), Is.False, "A pooled burst does not run.");

        dust.Play(Vector3.one, 1);

        SlamDustView fresh = Dust("Fresh");

        fresh.Play(Vector3.one, 1);

        for (int i = 0; i < SlamDustView.ChunkCount; i++)
        {
            Assert.That(dust.ChunkOffset(i), Is.EqualTo(fresh.ChunkOffset(i)),
                "A replayed burst is the same as a new one: nothing of its last life.");
        }
    }

    [Test]
    public void BossViews_ShockwaveEmitted_ShakesAndBursts()
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

        CameraShake shake = Shake();

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
            slamDustPrefab: Dust("DustPrefab"),
            shake: shake);

        try
        {
            hub.Publish(new ShockwaveEmitted(4, new System.Numerics.Vector3(2f, 0f, 5f), 7f, 6f));

            Assert.That(shake.IsShaking, Is.True, "The landing thuds.");
            Assert.That(views.DustCount, Is.EqualTo(1));
            Assert.That(views.Dust(0).transform.position, Is.EqualTo(new Vector3(2f, 0f, 5f)),
                "The ground bursts at the ring's origin.");

            views.Step(SlamDustView.Lifetime + 0.01f);

            Assert.That(views.DustCount, Is.EqualTo(0), "Gone, and back in the pool.");
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
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DustPath);

        Assert.That(prefab, Is.Not.Null, DustPath);
        Assert.That(prefab.GetComponent<SlamDustView>(), Is.Not.Null,
            "A null component here is Traps §5's block-namespace trap.");

        var renderer = prefab.GetComponent<MeshRenderer>();

        Assert.That(renderer, Is.Not.Null);
        Assert.That(renderer.sharedMaterial, Is.Not.Null);
        Assert.That(renderer.sharedMaterial.name, Is.EqualTo("M_Impact"));
    }

    private CameraShake Shake() => Keep(new GameObject("Camera")).AddComponent<CameraShake>();

    private SlamDustView Dust(string name) =>
        Keep(new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer))).AddComponent<SlamDustView>();

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
