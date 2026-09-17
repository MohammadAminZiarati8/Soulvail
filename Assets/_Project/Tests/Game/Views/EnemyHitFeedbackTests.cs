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
/// GD §16.2's first row: a basic enemy that darkens as it dies, and does so without ever arriving at
/// the one colour GD §16.4 reserves for danger.
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
/// what the tint looks like <em>after</em> a flash or a telegraph is a claim about elapsed time.
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

    /// <summary>The Husk's shipped tint — <c>Husk.asset</c>, and <c>M_BoneGrey</c>'s own colour.</summary>
    private static readonly Color HuskTint = new Color(0.43137255f, 0.41568628f, 0.3882353f, 1f);

    /// <summary>The Bloater's shipped tint — the reddest archetype in the build, and the one that matters.</summary>
    private static readonly Color BloaterTint = new Color(0.6392157f, 0.34117648f, 0.21960784f, 1f);

    /// <summary>The Spitter's shipped tint, for the third of the three.</summary>
    private static readonly Color SpitterTint = new Color(0.60784316f, 0.69803923f, 0.5686275f, 1f);

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

    // ---- The tint itself (rules 1–3) -------------------------------------------------------------

    [Test]
    public void Tint_FullHealthIsTheArchetypeColour()
    {
        _feedback.SetArchetypeLook(HuskTint, 1f);

        Rest(1f);

        // At full health the composition is an identity rather than a rounding — BodyColour returns
        // _liveColour itself — so a freshly spawned body looks exactly as M2-06 authored it and the
        // three archetypes still tell themselves apart (GD §8.1, rule 2). The tolerance is the
        // *property block's*, not the lerp's: a MaterialPropertyBlock round-trips a channel at about
        // one ULP (0.431372553 goes in, 0.4313725 comes back), which is why Color.Equals is false on
        // a value that was never changed and why every colour row in this file compares per channel.
        AssertColour(HuskTint, Painted());
    }

    [Test]
    public void Tint_DarkensAsHealthFalls()
    {
        _feedback.SetArchetypeLook(HuskTint, 1f);

        Color three = Rest(0.75f);
        Color half = Rest(0.5f);
        Color quarter = Rest(0.25f);

        Assert.That(Luminance(half), Is.LessThan(Luminance(three)), "0.5 is darker than 0.75.");
        Assert.That(Luminance(quarter), Is.LessThan(Luminance(half)), "0.25 is darker than 0.5.");

        // And it is darkening *toward* the named colour rather than merely downward, which is the
        // half of rule 2 a luminance comparison alone would not catch: a body multiplied toward black
        // would pass the two rows above and fail this one.
        Assert.That(
            Distance(quarter, Palette.EnemyDying),
            Is.LessThan(Distance(three, Palette.EnemyDying)),
            "A quarter-health body is nearer Palette.EnemyDying than a three-quarter-health one.");
    }

    [Test]
    public void Tint_NeverApproachesDanger()
    {
        // Every archetype the build ships, because the row samples the lerp's intermediates and the
        // reddest body is the one that can cross. The Bloater is that body: it ships at (0.639,
        // 0.341, 0.220), whose green is 0.051 from Danger's and whose blue is 0.098 from it, both
        // inside the band *before any tint is applied at all*.
        foreach ((string name, Color tint) in new[]
                 {
                     ("Husk", HuskTint), ("Spitter", SpitterTint), ("Bloater", BloaterTint),
                 })
        {
            _feedback.SetArchetypeLook(tint, 1f);

            for (int step = 0; step <= 20; step++)
            {
                float hp = 1f - (step * 0.05f);

                Color painted = Rest(hp);

                Assert.That(
                    Palette.IsDanger(painted),
                    Is.False,
                    $"{name} at hp {hp:0.00} is drawn in GD §16.4's reserved colour.");

                // NOT ALL THREE channels within 0.15 — which is a claim about a colour *being* the
                // danger colour. The other reading, "no channel within 0.15", is red on a Bloater
                // standing at full health in the build that exists, and would therefore be a row
                // about coincidence rather than about readability. Red is the whole of the margin:
                // as hp falls the Bloater's green crosses Danger's exactly and its blue converges to
                // 0.012 away, so neither can carry a distance claim, while red stays at least 0.36
                // clear at every step of every archetype.
                bool r = Mathf.Abs(painted.r - Palette.Danger.r) < 0.15f;
                bool g = Mathf.Abs(painted.g - Palette.Danger.g) < 0.15f;
                bool b = Mathf.Abs(painted.b - Palette.Danger.b) < 0.15f;

                Assert.That(
                    r && g && b,
                    Is.False,
                    $"{name} at hp {hp:0.00} is within 0.15 of Danger on all three channels.");

                Assert.That(
                    Mathf.Abs(painted.r - Palette.Danger.r),
                    Is.GreaterThan(0.15f),
                    $"{name} at hp {hp:0.00}: red is the channel with room, and it has run out.");
            }
        }
    }

    // ---- The tint against the three effects that were already here (rule 1) ----------------------

    [Test]
    public void Tint_SurvivesAFlash()
    {
        _feedback.SetArchetypeLook(BloaterTint, 1f);

        // The hit flashes white and the flash then expires, which is the M1-12 behaviour the tint
        // shares a handler with and must not disturb.
        Damage(0.3f);

        AssertColour(Palette.HitFlash, Painted(), "A hit still flashes white.");

        Tick(1f);

        Color tinted = Painted();

        // And it returns to the *tinted* colour, not to the archetype's. This is the row that would
        // go red if a second component painted the tint straight onto the renderer: the first flash
        // to end would restore bone rust and the body would un-die.
        AssertColour(tinted, Painted());

        Assert.That(
            Distance(Painted(), BloaterTint),
            Is.GreaterThan(0.05f),
            "And not to the archetype's: this is the row a second writer of the tint would redden.");
    }

    [Test]
    public void Tint_SurvivesATelegraph()
    {
        _feedback.SetArchetypeLook(BloaterTint, 1f);

        Damage(0.3f);
        Tick(1f);

        Color tinted = Painted();
        Vector3 rest = _body.transform.localScale;

        _hub.Publish(new EnemyTelegraph(Id, 0.4f));

        Tick(0.2f);

        Assert.That(_body.transform.localScale.x, Is.GreaterThan(rest.x), "The body swells.");
        AssertColour(tinted, Painted(), "A wind-up is a shape change and not a colour one.");

        Tick(0.3f);

        // The snap back, and the colour never moved: the telegraph is deliberately not a red one,
        // because saturated red-orange is reserved for danger (GD §16.4) — the same ruling that put
        // EnemyDying where it is.
        Assert.That(_body.transform.localScale, Is.EqualTo(rest));
        AssertColour(tinted, Painted());
    }

    [Test]
    public void Tint_DissolveFadesFromTheTintedColour()
    {
        _feedback.SetArchetypeLook(BloaterTint, 1f);

        Damage(0.1f);
        Tick(1f);

        Color tinted = Painted();

        _hub.Publish(new EnemyDied(Id, Bloater, System.Numerics.Vector3.Zero));

        Color opening = Painted();

        // The corpse starts at the colour it died in, opaque, and fades from there. Both the rgb and
        // the alpha come off the composed colour: a dissolve that took its rgb from the archetype
        // would snap a nearly-dead body back to full rust for the half-second it takes to go.
        Assert.That(opening.r, Is.EqualTo(tinted.r).Within(1e-4f));
        Assert.That(opening.g, Is.EqualTo(tinted.g).Within(1e-4f));
        Assert.That(opening.b, Is.EqualTo(tinted.b).Within(1e-4f));
        Assert.That(opening.a, Is.EqualTo(1f).Within(1e-4f));

        Tick(0.25f);

        Color halfway = Painted();

        Assert.That(halfway.a, Is.LessThan(opening.a), "The alpha ramps.");
        Assert.That(halfway.r, Is.EqualTo(tinted.r).Within(1e-4f), "And only the alpha ramps.");
    }

    [Test]
    public void Tint_KillingBlowDoesNotFlash()
    {
        _feedback.SetArchetypeLook(BloaterTint, 1f);

        Damage(0.4f);
        Tick(1f);

        // The existing M1-12 rule, unchanged by the tint sharing a handler with it: EnemyDied arrives
        // in the same call and the dissolve is the answer to it, so one frame of white under a fade
        // that is already starting would read as a glitch rather than as a hit.
        _hub.Publish(new EnemyDamaged(Id, 12f, 0f, killed: true));

        Assert.That(
            Distance(Painted(), Palette.HitFlash),
            Is.GreaterThan(0.05f),
            "A killing blow does not flash.");

        // And the tint did move, because the killing blow carries an HpFraction like every other hit
        // — so the body the dissolve is about to fade is the one the last hit left, rather than the
        // one before it.
        Assert.That(
            Distance(Painted(), Palette.EnemyDying),
            Is.LessThan(0.01f),
            "A body at zero HP is drawn at Palette.EnemyDying.");
    }

    // ---- What a rental has to forget (rule 4, AR §18.4) ------------------------------------------

    [Test]
    public void Tint_ResetForgetsIt()
    {
        _feedback.SetArchetypeLook(BloaterTint, 1f);

        Damage(0.05f);
        Tick(1f);

        Assert.That(
            Distance(Painted(), BloaterTint),
            Is.GreaterThan(0.05f),
            "Setup: the body is nearly dead.");

        _feedback.ResetVisuals();

        // The third entry on AR §18.4's list. Without it a Bloater that died at 5 % HP goes back to
        // the pool nearly black, and the next Husk rented from that body spawns looking half dead —
        // a rendering bug three systems from its cause.
        AssertColour(BloaterTint, Painted());
    }

    [Test]
    public void Tint_ArchetypeLookRebasesIt()
    {
        _feedback.SetArchetypeLook(BloaterTint, 1f);

        Damage(0.05f);
        Tick(1f);

        _feedback.SetArchetypeLook(HuskTint, 1f);

        // The rental, not the reset, is what undoes it — which is why the restore is *absolute*
        // rather than accumulated. Every rental is told what it is standing in for, so a body is
        // never asked to remember what it was.
        AssertColour(HuskTint, Painted());
    }

    [Test]
    public void Tint_IsForgottenBeforeThePropertyBlockExists()
    {
        // A body that has never woken up: no Awake, so no property block and nothing to paint. Both
        // clears run ahead of the block guard, which is what makes the invariant hold for a fixture
        // that rents and returns without building a whole prefab — _liveScale's own argument, applied
        // to the third thing on the list.
        var bare = new GameObject("Bare");

        try
        {
            bare.AddComponent<EnemyView>();

            var feedback = bare.AddComponent<EnemyHitFeedback>();

            feedback.SetArchetypeLook(BloaterTint, 1f);

            Assert.That(Tint(feedback), Is.EqualTo(0f));

            typeof(EnemyHitFeedback).GetField("_damageTint", Private).SetValue(feedback, 0.9f);

            feedback.ResetVisuals();

            Assert.That(Tint(feedback), Is.EqualTo(0f));
        }
        finally
        {
            Object.DestroyImmediate(bare);
        }
    }

    // ---- Doors (guard rows) ----------------------------------------------------------------------

    [Test]
    public void Tint_IgnoresANonFiniteFraction()
    {
        _feedback.SetArchetypeLook(HuskTint, 1f);

        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Damage(0.5f);
            Damage(bad);

            // Read as unhurt rather than clamped. Mathf.Clamp01 is two comparisons and every
            // comparison against NaN is false, so a NaN passes straight through both bounds and would
            // reach the lerp (AR §18.3, and Palette.Quantise's own note one layer down).
            Assert.That(Tint(_feedback), Is.EqualTo(0f), $"{bad} was let into the lerp.");
        }
    }

    [Test]
    public void Tint_IgnoresOtherEnemies()
    {
        _feedback.SetArchetypeLook(HuskTint, 1f);

        _hub.Publish(new EnemyDamaged(Id + 1, 5f, 0.1f, killed: false));

        Assert.That(Tint(_feedback), Is.EqualTo(0f));
        AssertColour(HuskTint, Painted());
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

    private void Damage(float hpFraction) =>
        _hub.Publish(new EnemyDamaged(Id, 5f, hpFraction, killed: false));

    /// <summary>
    /// The colour the body <em>rests</em> at after a hit of <paramref name="hpFraction"/>: the hit
    /// itself paints M1-12's white flash, so every claim about the tint has to let that expire first.
    /// </summary>
    private Color Rest(float hpFraction)
    {
        Damage(hpFraction);
        Tick(1f);

        return Painted();
    }

    private void Tick(float dt) =>
        typeof(EnemyHitFeedback).GetMethod("Tick", Private).Invoke(_feedback, new object[] { dt });

    private static float Tint(EnemyHitFeedback feedback) =>
        (float)typeof(EnemyHitFeedback).GetField("_damageTint", Private).GetValue(feedback);

    /// <summary>The colour actually written into the renderer's property block.</summary>
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
    private static float Luminance(Color colour) =>
        (0.2126f * colour.r) + (0.7152f * colour.g) + (0.0722f * colour.b);

    private static float Distance(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

    private void Set(string field, Object value) =>
        typeof(EnemyHitFeedback).GetField(field, Private).SetValue(_feedback, value);

    private void Invoke(string method) =>
        typeof(EnemyHitFeedback).GetMethod(method, Private).Invoke(_feedback, null);
}
