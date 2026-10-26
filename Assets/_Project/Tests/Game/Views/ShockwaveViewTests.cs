using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// A slam's ring as a ground wave (JG-06c): a band exactly as wide as the bite, an earth lip on its
/// front edge, a fade at the end of its reach, and a clean body back from the pool.
/// </summary>
/// <remarks>
/// The numbers are Old Croaker's shipped ring, written out rather than read from
/// <c>CroakerBehaviour</c>, so a row cannot pass by reading the same constant on both sides.
/// <c>Awake</c> never runs in EditMode (Traps §5): <see cref="ShockwaveView.Bind"/> builds the meshes.
/// </remarks>
[TestFixture]
public sealed class ShockwaveViewTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private const string ShockwavePath = "Assets/_Project/Prefabs/Vfx/VFX_Shockwave.prefab";

    private const float Speed = 7f;
    private const float Ceiling = 6f;
    private const float Thickness = 1.2f;
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
    public void Step_BandMatchesTheSystemsCircle()
    {
        ShockwaveView ring = Ring();

        ring.Bind(Vector3.zero, Speed, Ceiling, Thickness);
        ring.Step(0.5f);

        // ShockwaveSystem.Bite: radius + half the thickness, never past the ceiling.
        float radius = Speed * 0.5f;

        Assert.That(ring.OuterRadius, Is.EqualTo(radius + (Thickness * 0.5f)).Within(Tolerance));
        Assert.That(ring.InnerRadius, Is.EqualTo(radius - (Thickness * 0.5f)).Within(Tolerance));

        for (int j = 0; j < ShockwaveView.Segments; j++)
        {
            Vector3 outer = ring.BandVertex(j * 2);
            Vector3 inner = ring.BandVertex((j * 2) + 1);

            Assert.That(new Vector2(outer.x, outer.z).magnitude, Is.EqualTo(ring.OuterRadius).Within(Tolerance));
            Assert.That(new Vector2(inner.x, inner.z).magnitude, Is.EqualTo(ring.InnerRadius).Within(Tolerance));
            Assert.That(outer.y, Is.Zero, "Flat on the ground.");
            Assert.That(inner.y, Is.Zero);
        }

        ring.Step(0.3f);

        Assert.That(ring.OuterRadius, Is.EqualTo(Ceiling).Within(Tolerance), "Capped at the ceiling.");
    }

    [Test]
    public void Step_InnerRadiusNeverBelowZero()
    {
        ShockwaveView ring = Ring();

        ring.Bind(Vector3.zero, Speed, Ceiling, Thickness);

        Assert.That(ring.InnerRadius, Is.Zero);
        Assert.That(ring.OuterRadius, Is.EqualTo(Thickness * 0.5f).Within(Tolerance),
            "At the landing the band is the bite's half-width round the slam point.");

        ring.Step(0.05f);

        Assert.That(ring.InnerRadius, Is.Zero);
    }

    [Test]
    public void Step_LipCrestAtTheFrontEdge()
    {
        ShockwaveView ring = Ring();

        ring.Bind(Vector3.zero, Speed, Ceiling, Thickness);
        ring.Step(0.4f);

        for (int j = 0; j < ShockwaveView.Segments; j++)
        {
            Vector3 crest = ring.LipCrest(j);

            Assert.That(new Vector2(crest.x, crest.z).magnitude, Is.EqualTo(ring.OuterRadius).Within(Tolerance));
            Assert.That(crest.y, Is.GreaterThan(0.2f), "The crest stands up off the ground.");
        }
    }

    [Test]
    public void Step_FadesThenEndsAtLifetime()
    {
        ShockwaveView ring = Ring();

        ring.Bind(Vector3.zero, Speed, Ceiling, Thickness);

        float lifetime = Ceiling / Speed;
        float baseAlpha = ring.Alpha;

        ring.Step(lifetime * 0.75f);

        Assert.That(ring.Alpha, Is.EqualTo(baseAlpha).Within(Tolerance), "Full strength until the last fifth.");
        Assert.That(ring.LipCrestHeight, Is.EqualTo(ShockwaveView.LipHeight).Within(Tolerance));

        ring.Step(lifetime * 0.15f);

        Assert.That(ring.Alpha, Is.LessThan(baseAlpha * 0.6f), "Fading out.");
        Assert.That(ring.LipCrestHeight, Is.LessThan(ShockwaveView.LipHeight * 0.6f), "The lip sinks with it.");
        Assert.That(ring.IsLive, Is.True);

        Assert.That(ring.Step(lifetime * 0.11f), Is.False, "Gone at MaxRadius / Speed.");
    }

    [Test]
    public void Despawn_ThenBegin_DrawsOnlyTheNewRing()
    {
        ShockwaveView ring = Ring();

        ring.Bind(new Vector3(5f, 0f, 5f), Speed, Ceiling, Thickness);
        ring.Step(0.6f);
        ring.OnDespawn();

        Assert.That(ring.IsLive, Is.False);
        Assert.That(ring.OuterRadius, Is.Zero);
        Assert.That(ring.Thickness, Is.Zero);
        Assert.That(ring.BandVertex(0), Is.EqualTo(Vector3.zero), "The band is folded away.");
        Assert.That(ring.LipCrest(0), Is.EqualTo(Vector3.zero), "The lip is folded away.");

        ring.Bind(Vector3.zero, 4f, 3f);

        Assert.That(ring.Thickness, Is.Zero, "No thickness on this ring: none inherited from the last.");
        Assert.That(ring.OuterRadius, Is.Zero);
        Assert.That(ring.MaxRadius, Is.EqualTo(3f));
    }

    [Test]
    public void Bind_NoThickness_DrawsTheEdgeAlone()
    {
        ShockwaveView ring = Ring();

        ring.Bind(Vector3.zero, Speed, Ceiling);
        ring.Step(0.5f);

        Assert.That(ring.OuterRadius, Is.EqualTo(Speed * 0.5f).Within(Tolerance));
        Assert.That(ring.InnerRadius, Is.EqualTo((Speed * 0.5f) - ShockwaveView.EdgeOnlyBand).Within(Tolerance));
    }

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShockwavePath);
        var view = prefab.GetComponent<ShockwaveView>();

        Assert.That(view, Is.Not.Null, "A null component here is Traps §5's block-namespace trap.");
        Assert.That(view.IsDrawable, Is.True);
        Assert.That(prefab.GetComponent<MeshFilter>(), Is.Not.Null, "The band's mesh is built on this.");

        var lip = (MeshFilter)typeof(ShockwaveView).GetField("_lip", Private).GetValue(view);

        Assert.That(lip, Is.Not.Null, "The lip is dressed.");

        var lipRenderer = lip.GetComponent<MeshRenderer>();

        Assert.That(lipRenderer, Is.Not.Null);
        Assert.That(lipRenderer.sharedMaterial.name, Is.EqualTo("M_Impact"));
    }

    /// <summary>A ring body with a band renderer and a lip child, as the prefab is built.</summary>
    private ShockwaveView Ring()
    {
        var body = Keep(new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer)));
        var lip = new GameObject("Lip", typeof(MeshFilter), typeof(MeshRenderer));

        lip.transform.SetParent(body.transform, false);

        ShockwaveView ring = body.AddComponent<ShockwaveView>();

        typeof(ShockwaveView).GetField("_band", Private).SetValue(ring, body.GetComponent<MeshRenderer>());
        typeof(ShockwaveView).GetField("_lip", Private).SetValue(ring, lip.GetComponent<MeshFilter>());

        return ring;
    }

    private GameObject Keep(GameObject body)
    {
        _spawned.Add(body);

        return body;
    }
}
