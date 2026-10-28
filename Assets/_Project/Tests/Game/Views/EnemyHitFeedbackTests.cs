using System;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// The archetype colour remains fixed through damage, flash, wind-up and dissolve (JG-08a).
/// </summary>
/// <remarks>
/// <para>
/// <b>The first test file this component has ever had.</b> M1-12 shipped it, M1-19 pooled it and
/// M2-06 gave it the archetype look, and all three were covered from the outside — by
/// <c>PoolingLifecycleTests</c> in PlayMode and by <c>EnemyLookTests</c> against the assets.
/// M3-13b is the first task whose claim is about the <em>colour the body rests at</em>, which is a
/// composition rather than an event, and that needs a fixture of its own.
/// </para>
/// <para>
/// <c>Awake</c> and <c>Update</c> do not run in EditMode (Traps §5), so this calls <c>Awake</c>
/// itself and drives the private <c>Tick(dt)</c> the component gained for exactly this reason —
/// <c>Time.deltaTime</c> is not something an EditMode fixture can advance, and every claim about
/// what the body looks like <em>after</em> a flash or a telegraph is a claim about elapsed time.
/// </para>
/// <para>
/// The body is built here rather than loaded from <c>Enemy.prefab</c>, for
/// <c>PoolingLifecycleTests</c>' reason: the claims are about behaviour, and building the hierarchy
/// keeps the fixture from depending on how the art is dressed.
/// </para>
/// </remarks>
[TestFixture]
public sealed class EnemyHitFeedbackTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    /// <summary>The Husk's shipped tint — <c>Husk.asset</c>, and <c>M_BoneGrey</c>'s own colour.</summary>
    private static readonly Color HuskTint = new Color(0.43137255f, 0.41568628f, 0.3882353f, 1f);

    /// <summary>The Bloater's shipped tint — the reddest archetype in the build, and the one that matters.</summary>
    private static readonly Color BloaterTint = new Color(0.6392157f, 0.34117648f, 0.21960784f, 1f);

    private const int Id = 7;

    private static readonly ContentId Bloater = new ContentId("enemy.bloater");

    private GameObject _body;
    private EnemyView _view;
    private EnemyHitFeedback _feedback;
    private Renderer _renderer;
    private Material _live;
    private Material _dissolve;
    private DomainEventHub _hub;

    [SetUp]
    public void CreateBody()
    {
        _hub = new DomainEventHub();

        _body = new GameObject("Enemy");

        GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);

        Object.DestroyImmediate(mesh.GetComponent<Collider>());

        mesh.transform.SetParent(_body.transform, false);

        _renderer = mesh.GetComponent<MeshRenderer>();

        _live = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        _live.SetColor(BaseColorId, HuskTint);

        _dissolve = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        _renderer.sharedMaterial = _live;

        // EnemyView first: EnemyHitFeedback [RequireComponent]s it.
        _view = _body.AddComponent<EnemyView>();
        _feedback = _body.AddComponent<EnemyHitFeedback>();

        Set("_renderer", _renderer);
        Set("_dissolveMaterial", _dissolve);

        _feedback.Construct(_hub);

        Invoke("Awake");

        _view.Bind(Id, Vector3.zero);
    }

    [TearDown]
    public void DestroyBody()
    {
        _hub?.Dispose();
        _hub = null;

        if (_body != null)
        {
            Object.DestroyImmediate(_body);
        }

        _body = null;

        if (_live != null)
        {
            Object.DestroyImmediate(_live);
        }

        if (_dissolve != null)
        {
            Object.DestroyImmediate(_dissolve);
        }

        _live = null;
        _dissolve = null;
    }

    // ---- The archetype colour through damage and effects (JG-08a) ------------------------------

    [TestCase(1f)]
    [TestCase(0.5f)]
    [TestCase(0.01f)]
    [TestCase(0f)]
    public void Colour_UnchangedAtAnyHealth(float hpFraction)
    {
        _feedback.SetArchetypeLook(HuskTint, 1f);
        Damage(hpFraction);
        Tick(1f);
        AssertColour(HuskTint, Painted());
    }

    [Test]
    public void Colour_FlashAndTelegraphReturnToTheArchetype()
    {
        _feedback.SetArchetypeLook(BloaterTint, 1f);
        Damage(0.01f);
        AssertColour(Palette.HitFlash, Painted(), "A hit still flashes white.");
        Tick(1f);
        AssertColour(BloaterTint, Painted(), "The flash returns to the archetype.");

        Vector3 rest = _body.transform.localScale;
        _hub.Publish(new EnemyTelegraph(Id, 0.4f));
        Tick(0.2f);
        Assert.That(_body.transform.localScale.x, Is.GreaterThan(rest.x));
        AssertColour(BloaterTint, Painted());
        Tick(0.3f);
        Assert.That(_body.transform.localScale, Is.EqualTo(rest));
        AssertColour(BloaterTint, Painted(), "The wind-up returns to the archetype.");
    }

    [Test]
    public void Colour_DissolveFadesFromTheArchetype()
    {
        _feedback.SetArchetypeLook(BloaterTint, 1f);
        Damage(0.01f);
        Tick(1f);
        _hub.Publish(new EnemyDamaged(Id, 12f, 0f, killed: true));
        AssertColour(BloaterTint, Painted(), "A killing blow does not flash or darken.");
        _hub.Publish(new EnemyDied(Id, Bloater, System.Numerics.Vector3.Zero));
        AssertColour(BloaterTint, Painted());
        Tick(0.25f);
        Color halfway = Painted();
        Assert.That(halfway.a, Is.LessThan(1f));
        Assert.That(halfway.r, Is.EqualTo(BloaterTint.r).Within(1e-4f));
        Assert.That(halfway.g, Is.EqualTo(BloaterTint.g).Within(1e-4f));
        Assert.That(halfway.b, Is.EqualTo(BloaterTint.b).Within(1e-4f));
    }

    // ---- The glow (M7-05h rule 6) ---------------------------------------------------------------

    [Test]
    public void Glow_RestsAtBlack()
    {
        _feedback.SetArchetypeLook(HuskTint, 1f);

        AssertColour(Glowing(), Color.black, "A body at rest emits nothing.");
    }

    [Test]
    public void Glow_FlashIsWhite()
    {
        _feedback.SetArchetypeLook(HuskTint, 1f);

        Damage(0.8f);

        AssertColour(Glowing(), Palette.HitFlash,
            "White through emission, which is how a textured body flashes at all — its texture times " +
            "a white base colour is only its texture.");

        Tick(1f);

        AssertColour(Glowing(), Color.black, "And back to black when the flash ends.");
    }

    [Test]
    public void Glow_WindUpRisesToDangerAndDropsAtTheStrike()
    {
        _feedback.SetArchetypeLook(HuskTint, 1f);
        float glow = (float)typeof(EnemyHitFeedback).GetField("_telegraphGlow", Private).GetValue(_feedback);

        _hub.Publish(new EnemyTelegraph(Id, 0.4f));
        Tick(0.2f);

        Color halfway = Palette.Danger * (glow * 0.5f);
        halfway.a = 1f;

        AssertColour(Glowing(), halfway, "Halfway through the wind-up, half the glow, in danger's colour (GD §16.4).");

        Tick(0.3f);

        AssertColour(Glowing(), Color.black, "It drops with the swell: the release is the strike.");
    }

    [Test]
    public void Glow_AFlashDuringAWindUpReturnsToTheGlow()
    {
        _feedback.SetArchetypeLook(HuskTint, 1f);

        _hub.Publish(new EnemyTelegraph(Id, 0.4f));
        Tick(0.1f);
        Damage(0.8f);
        Tick(0.02f);

        AssertColour(Glowing(), Palette.HitFlash, "The flash wins while it lasts.");

        Tick(0.1f);

        Assert.That(Glowing().r, Is.GreaterThan(0f), "Then the wind-up's glow comes back, not black.");
        Assert.That(Glowing().r, Is.GreaterThan(Glowing().b), "In danger's red-orange.");
    }

    [Test]
    public void Glow_ResetAndDeathWriteBlack()
    {
        _feedback.SetArchetypeLook(HuskTint, 1f);

        _hub.Publish(new EnemyTelegraph(Id, 0.4f));
        Tick(0.2f);
        _feedback.ResetVisuals();

        AssertColour(Glowing(), Color.black, "A pooled body forgets its glow (AR §18.4).");

        _hub.Publish(new EnemyTelegraph(Id, 0.4f));
        Tick(0.2f);
        _hub.Publish(new EnemyDied(Id, Bloater, System.Numerics.Vector3.Zero));

        AssertColour(Glowing(), Color.black, "A corpse does not glow.");
    }

    [Test]
    public void Glow_TheBaseColourIsUntouched()
    {
        _feedback.SetArchetypeLook(HuskTint, 1f);

        _hub.Publish(new EnemyTelegraph(Id, 0.4f));
        Tick(0.2f);

        AssertColour(Painted(), HuskTint,
            "The glow is emission's alone, so a material with emission off — every capsule — looks as it did.");
    }

    // ---- The Puffball's switches (JG-03b rules 6–7) ----------------------------------------------

    [Test]
    public void Glow_SporeWindUpRisesToSporeGlow()
    {
        _feedback.SetArchetypeLook(HuskTint, 1f);
        typeof(EnemyHitFeedback).GetField("_sporeTelegraph", Private).SetValue(_feedback, true);
        float glow = (float)typeof(EnemyHitFeedback).GetField("_telegraphGlow", Private).GetValue(_feedback);

        _hub.Publish(new EnemyTelegraph(Id, 0.4f));
        Tick(0.2f);

        Color halfway = Palette.SporeGlow * (glow * 0.5f);
        halfway.a = 1f;

        AssertColour(Glowing(), halfway, "Halfway through the wind-up, half the glow, cyan-white (the owner, 2026-10-22).");

        Tick(0.3f);

        AssertColour(Glowing(), Color.black, "It drops at the strike, as the danger glow does.");
    }

    [Test]
    public void Glow_SporeGlowIsNotDanger()
    {
        Assert.That(Palette.IsDanger(Palette.SporeGlow), Is.False);
        Assert.That(Distance(Palette.SporeGlow, Palette.Danger), Is.GreaterThan(1f),
            "Cyan-white and red-orange are told apart at a glance.");
    }

    [Test]
    public void Burst_PlaysOnItsOwnDeath()
    {
        ParticleSystem burst = Burst();

        _hub.Publish(new EnemyDied(Id + 1, Bloater, System.Numerics.Vector3.Zero));

        Assert.That(burst.isPlaying, Is.False, "Another body's death plays nothing here.");

        _hub.Publish(new EnemyDied(Id, Bloater, System.Numerics.Vector3.Zero));

        Assert.That(burst.isPlaying, Is.True, "This body's death plays its burst.");
    }

    [Test]
    public void Burst_ResetClearsIt()
    {
        ParticleSystem burst = Burst();

        _hub.Publish(new EnemyDied(Id, Bloater, System.Numerics.Vector3.Zero));
        burst.Emit(5);

        _feedback.ResetVisuals();

        Assert.That(burst.isPlaying, Is.False, "A pooled body's burst stops (AR §18.4).");
        Assert.That(burst.particleCount, Is.Zero, "…and leaves nothing behind for the next rental.");
    }

    [Test]
    public void Construct_RejectsANullHub()
    {
        var bare = new GameObject("Bare");

        try
        {
            bare.AddComponent<EnemyView>();

            var feedback = bare.AddComponent<EnemyHitFeedback>();

            Assert.Throws<ArgumentNullException>(() => feedback.Construct(null));
        }
        finally
        {
            Object.DestroyImmediate(bare);
        }
    }

    // ---- Fixture helpers ------------------------------------------------------------------------

    /// <summary>A particle system under the body, named as its death burst, idle until told.</summary>
    private ParticleSystem Burst()
    {
        var holder = new GameObject("Burst");
        holder.transform.SetParent(_body.transform, false);

        ParticleSystem burst = holder.AddComponent<ParticleSystem>();
        burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = burst.main;
        main.playOnAwake = false;

        Set("_deathBurst", burst);

        return burst;
    }

    private Color Glowing()
    {
        var block = new MaterialPropertyBlock();

        _renderer.GetPropertyBlock(block);

        return block.GetColor(EmissionColorId);
    }

    private void Damage(float hpFraction) =>
        _hub.Publish(new EnemyDamaged(Id, 5f, hpFraction, killed: false));

    private void Tick(float dt) =>
        typeof(EnemyHitFeedback).GetMethod("Tick", Private).Invoke(_feedback, new object[] { dt });

    private Color Painted()
    {
        var block = new MaterialPropertyBlock();

        _renderer.GetPropertyBlock(block);

        return block.GetColor(BaseColorId);
    }

    /// <summary>
    /// Two colours agree to the resolution a <see cref="MaterialPropertyBlock"/> keeps.
    /// </summary>
    /// <remarks>
    /// Per channel with a tolerance rather than <c>Is.EqualTo</c>, because <c>Color.Equals</c> is
    /// exact and the block is not: setting <c>0.431372553</c> and reading it straight back gives
    /// <c>0.4313725</c>. Unity's own <c>operator ==</c> would pass, and NUnit does not use it —
    /// which is a difference that costs a red row on a value nothing ever changed.
    /// </remarks>
    private static void AssertColour(Color expected, Color actual, string because = null)
    {
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(1e-4f), because ?? "red");
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(1e-4f), because ?? "green");
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(1e-4f), because ?? "blue");
        Assert.That(actual.a, Is.EqualTo(expected.a).Within(1e-4f), because ?? "alpha");
    }

    /// <summary>Rec. 709 luminance — "darker" as an eye reads it rather than as a sum.</summary>
    private static float Distance(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

    private void Set(string field, Object value) =>
        typeof(EnemyHitFeedback).GetField(field, Private).SetValue(_feedback, value);

    private void Invoke(string method) =>
        typeof(EnemyHitFeedback).GetMethod(method, Private).Invoke(_feedback, null);
}
