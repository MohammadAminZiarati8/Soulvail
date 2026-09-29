using NUnit.Framework;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// One arrow's hit (RS-06b): a white star, pale shards with a cyan glint thrown along the arrow's
/// travel, and a thin white ring, placed on the body it hit and gone in under a third of a second.
/// Nothing here throws when it is wrong — a spray thrown backwards, a flash that never goes out and a
/// shard in a reserved colour all draw perfectly well — so every rule is read back off the view.
/// </summary>
/// <remarks>
/// <para>
/// The impact is a scene object carrying the field initialisers, which are the numbers
/// <c>VFX_ArrowImpact.prefab</c> ships with; the last three rows read the prefab itself.
/// <c>Awake</c> never runs in EditMode (Traps §5), so the mesh is built by the first
/// <see cref="ImpactView.Play"/>, and destroyed here by hand: Unity sends no <c>OnDestroy</c> to a
/// component it never sent an <c>Awake</c>.
/// </para>
/// <para>
/// The viewer is the follow camera's pose, 57° down and looking along +Z (<c>FollowCamera</c>'s
/// defaults, GD §5.1).
/// </para>
/// </remarks>
[TestFixture]
public sealed class ImpactViewTests
{
    private const string ImpactPath = "Assets/_Project/Prefabs/Vfx/VFX_ArrowImpact.prefab";
    private const string ArrowPath = "Assets/_Project/Prefabs/Projectiles/Arrow.prefab";
    private const string BoltPath = "Assets/_Project/Prefabs/Projectiles/Projectile.prefab";

    /// <summary>The initialisers' placement: 0.6 m up, 0.6 m toward the camera.</summary>
    private const float Height = 0.6f;
    private const float TowardViewer = 0.6f;

    /// <summary>The initialisers' spread either side of the travel, in degrees.</summary>
    private const float SpreadDeg = 50f;

    /// <summary>"Gone well inside half a second" (the owner), as a ceiling.</summary>
    private const float MaxLifetime = 0.35f;

    /// <summary>The thin cyan accent, as a ceiling on the ring's width.</summary>
    private const float MaxRingWidth = 0.1f;

    private const float Frame = 1f / 60f;

    private static Quaternion Camera => Quaternion.Euler(57f, 0f, 0f);

    private GameObject _object;
    private ImpactView _impact;

    [SetUp]
    public void CreateImpact()
    {
        _object = new GameObject("Impact");
        _object.AddComponent<MeshRenderer>();

        MeshFilter filter = _object.AddComponent<MeshFilter>();

        _impact = _object.AddComponent<ImpactView>();

        using var serialized = new SerializedObject(_impact);

        serialized.FindProperty("_filter").objectReferenceValue = filter;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void DestroyImpact()
    {
        if (_object == null)
        {
            return;
        }

        Mesh mesh = _object.GetComponent<MeshFilter>().sharedMesh;

        if (mesh != null)
        {
            Object.DestroyImmediate(mesh);
        }

        Object.DestroyImmediate(_object);
    }

    [Test]
    public void Play_CentresOnTheBodyInFrontOfIt()
    {
        _impact.Play(new Vector3(2f, 0f, 3f), new Vector3(0f, 0f, 8f), Camera, seed: 1);

        // Rule 6: up off the feet the arrow lands at, then toward the camera, so the flash is drawn
        // in front of the body rather than inside it.
        Vector3 expected = new Vector3(2f, Height, 3f) - (Camera * Vector3.forward * TowardViewer);

        Assert.That(_impact.IsLive, Is.True);
        Assert.That(Vector3.Distance(_impact.transform.position, expected), Is.LessThan(1e-4f));
    }

    [Test]
    public void Play_ThrowsTheShardsAlongTheTravel()
    {
        // Rule 6: across, against and diagonal to the camera; the height of the flight is ignored.
        foreach (Vector3 travel in new[] { new Vector3(6f, 0f, 0f), new Vector3(0f, 0f, -6f), new Vector3(4f, 1f, 4f) })
        {
            _impact.Play(Vector3.zero, travel, Camera, seed: 7);

            StepFor(0.08f);

            Vector3 heading = new Vector3(travel.x, 0f, travel.z).normalized;

            for (int i = 0; i < ImpactView.ShardCount; i++)
            {
                Vector3 offset = _impact.ShardOffset(i);
                Vector3 flat = new Vector3(offset.x, 0f, offset.z);

                Assert.That(Vector3.Dot(flat, heading), Is.GreaterThan(0.05f),
                    $"Shard {i} of a hit travelling {travel} went nowhere along it.");
                Assert.That(Vector3.Angle(flat, heading), Is.LessThanOrEqualTo(SpreadDeg + 0.01f),
                    $"Shard {i} left the spread either side of {travel}.");
            }
        }
    }

    [Test]
    public void Play_TheSameShotRepeatsAndTheNextDiffers()
    {
        _impact.Play(Vector3.zero, Vector3.forward, Camera, seed: 11);
        StepFor(0.05f);
        Vector3 first = _impact.ShardOffset(0);

        _impact.Play(Vector3.zero, Vector3.forward, Camera, seed: 11);
        StepFor(0.05f);
        Vector3 again = _impact.ShardOffset(0);

        _impact.Play(Vector3.zero, Vector3.forward, Camera, seed: 12);
        StepFor(0.05f);
        Vector3 next = _impact.ShardOffset(0);

        // Rule 6: a volley's three hits differ, and a test's hit is the same each run.
        Assert.That(Vector3.Distance(first, again), Is.LessThan(1e-5f));
        Assert.That(Vector3.Distance(first, next), Is.GreaterThan(1e-3f));
    }

    [Test]
    public void Step_TheStarGoesOutFirstAndTheHitIsGoneWithinItsLifetime()
    {
        _impact.Play(Vector3.zero, Vector3.forward, Camera, seed: 3);

        StepFor(0.02f);

        Assert.That(_impact.StarScale, Is.GreaterThan(0.5f), "The star pops at once.");
        Assert.That(_impact.RingRadius, Is.GreaterThan(0f));

        StepFor(0.1f);

        // Rule 6: the flash is the first thing to go, so it never sits over the next target.
        Assert.That(_impact.StarScale, Is.Zero, "The star is out by its tenth of a second.");
        Assert.That(_impact.IsLive, Is.True, "The ring and the shards are still going out.");

        float elapsed = _impact.Age;

        while (_impact.IsLive && elapsed < 1f)
        {
            _impact.Step(Frame);
            elapsed += Frame;
        }

        Assert.That(_impact.IsLive, Is.False);
        Assert.That(elapsed, Is.LessThanOrEqualTo(_impact.Lifetime + Frame));
        Assert.That(_impact.Lifetime, Is.LessThanOrEqualTo(MaxLifetime));

        AssertFolded("A finished hit draws nothing.");
    }

    [Test]
    public void Step_ABadStepDoesNothing()
    {
        _impact.Play(Vector3.zero, Vector3.forward, Camera, seed: 3);
        _impact.Step(0.05f);

        float age = _impact.Age;

        _impact.Step(0f);
        _impact.Step(-0.1f);
        _impact.Step(float.NaN);
        _impact.Step(float.PositiveInfinity);

        Assert.That(_impact.Age, Is.EqualTo(age));
        Assert.That(_impact.IsLive, Is.True);
    }

    [Test]
    public void OnDespawn_ForgetsThePlay()
    {
        _impact.Play(new Vector3(5f, 0f, 5f), Vector3.right, Camera, seed: 3);
        _impact.Step(0.05f);

        _impact.OnDespawn();

        // Rule 4: a body back in the pool is one the pool never handed out (AR §18.4).
        Assert.That(_impact.IsLive, Is.False);
        Assert.That(_impact.Age, Is.Zero);
        Assert.That(_impact.StarScale, Is.Zero);
        Assert.That(_impact.ShardOffset(0), Is.EqualTo(Vector3.zero));
        Assert.That(_impact.transform.position, Is.EqualTo(Vector3.zero));

        AssertFolded("A pooled body draws nothing of its last hit.");
    }

    [Test]
    public void Mesh_IsWhiteWithACyanGlintOnEachShard()
    {
        _impact.Play(Vector3.zero, Vector3.forward, Camera, seed: 3);

        Color32[] colours = _object.GetComponent<MeshFilter>().sharedMesh.colors32;
        Color32 cyan = Palette.Player;
        int cyanCount = 0;

        // Rule 7: every vertex is a shade of white or the player's cyan. Red-orange, gold and
        // violet are GD §16.4's, and none is a shade of either. The ring is white: a cyan ring round
        // an enemy reads as the reticle.
        foreach (Color32 colour in colours)
        {
            Assert.That(Palette.IsDanger(colour), Is.False);

            if (colour.r == cyan.r && colour.g == cyan.g && colour.b == cyan.b)
            {
                cyanCount++;

                continue;
            }

            Assert.That(colour.r == colour.g && colour.g == colour.b, Is.True, $"{colour} is not a shade of white.");
            Assert.That(colour.r, Is.GreaterThanOrEqualTo(150), $"{colour} is too dark to read as a flash.");
        }

        // One face of each shard, three vertices a face: the accent, and nothing else is cyan.
        Assert.That(cyanCount, Is.EqualTo(ImpactView.ShardCount * 3));
        Assert.That(_impact.RingWidth, Is.LessThanOrEqualTo(MaxRingWidth), "And the ring is thin.");
    }

    [Test]
    public void Shipped_TheArrowNamesItsImpactAndTheBoltNone()
    {
        var impact = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactPath);
        var arrow = AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPath);
        var bolt = AssetDatabase.LoadAssetAtPath<GameObject>(BoltPath);

        Assert.That(impact, Is.Not.Null, $"No prefab at {ImpactPath}.");

        // Rule 1: the arrow names its own impact. The default bolt, every enemy's shot and the
        // other classes', names none, so they land exactly as before.
        Assert.That(arrow.GetComponent<ProjectileView>().Impact, Is.EqualTo(impact.GetComponent<ImpactView>()));
        Assert.That(bolt.GetComponent<ProjectileView>().Impact == null, Is.True);
    }

    [Test]
    public void Shipped_TheImpactIsOneOpaqueDrawCall()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {ImpactPath}.");

        MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);

        // Rule 6: one renderer, one material, opaque — no transparent layer, no shadow.
        Assert.That(renderers.Length, Is.EqualTo(1));

        MeshRenderer renderer = renderers[0];
        Material material = renderer.sharedMaterial;

        Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(1));
        Assert.That(material.name, Is.EqualTo("M_Impact"));
        Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Particles/Unlit"));
        Assert.That(material.GetFloat("_Surface"), Is.Zero, "Opaque.");
        Assert.That(material.renderQueue, Is.LessThan((int)RenderQueue.Transparent));
        Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
        Assert.That(renderer.receiveShadows, Is.False);

        using var serialized = new SerializedObject(prefab.GetComponent<ImpactView>());

        Assert.That(serialized.FindProperty("_filter").objectReferenceValue,
            Is.EqualTo(prefab.GetComponent<MeshFilter>()), "The view draws with its own filter.");
    }

    [Test]
    public void Shipped_TheImpactIsQuickAndThin()
    {
        var impact = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactPath).GetComponent<ImpactView>();

        // Rule 6 and 7, on the numbers that ship rather than the initialisers above.
        Assert.That(impact.Lifetime, Is.LessThanOrEqualTo(MaxLifetime));

        using var serialized = new SerializedObject(impact);

        Assert.That(serialized.FindProperty("_ringWidth").floatValue, Is.LessThanOrEqualTo(MaxRingWidth));
    }

    private void StepFor(float seconds)
    {
        for (float t = 0f; t < seconds - 1e-6f; t += Frame)
        {
            _impact.Step(Frame);
        }
    }

    private void AssertFolded(string message)
    {
        foreach (Vector3 vertex in _object.GetComponent<MeshFilter>().sharedMesh.vertices)
        {
            Assert.That(vertex, Is.EqualTo(Vector3.zero), message);
        }
    }
}
