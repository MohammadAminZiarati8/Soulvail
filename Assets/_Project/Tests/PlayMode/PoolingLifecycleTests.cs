using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Views;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Soulvail.Tests.PlayMode;

/// <summary>
/// A pooled body, through a whole life and out the other side: rented, bound, hurt, killed,
/// dissolved, returned — and then asserted to be exactly as clean as one the pool has never handed
/// out. Ledger row 8's pooled-view half, for both families.
/// </summary>
/// <remarks>
/// <para>
/// <b>It has to be PlayMode, and that is the point.</b> <see cref="EnemyHitFeedback"/> captures the
/// live colour, the live material and the prefab's scale in <c>Awake</c>, which never runs on an
/// object instantiated in EditMode, and the dissolve advances in <c>Update</c>, which never ticks
/// there. An EditMode test of <c>EnemyView.OnDespawn</c> therefore asserts that nothing was undone
/// because nothing had happened — precisely the shape of test that passes against a broken feature,
/// and the reason the method the code itself calls <em>"the most dangerous method here"</em> has had
/// no coverage at all since M1-19.
/// </para>
/// <para>
/// <b>It builds its bodies rather than loading the shipped prefabs.</b> A PlayMode assembly is
/// compiled for every platform and so cannot reach <c>AssetDatabase</c>, and <c>Resources.Load</c>
/// is banned. What is under test is the reset chain in the components, not how the art is dressed —
/// and the thing that would otherwise be missed, a new field on a prefab that remembers something,
/// is caught here the moment it joins the reset list (AR §18.4).
/// </para>
/// <para>
/// The templates are created inactive, which is what keeps them templates: VContainer deactivates a
/// prefab before instantiating it and injects the copy while it is off, so an inactive template
/// still produces a correctly injected instance and never runs its own <c>Start</c> — which for an
/// enemy body would throw, because nothing injected the template.
/// </para>
/// </remarks>
public sealed class PoolingLifecycleTests
{
    private static readonly ContentId Husk = new ContentId("enemy.husk");

    /// <summary>URP's colour property, the one every effect on an enemy writes through.</summary>
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private readonly List<Object> _created = new List<Object>();

    private IObjectResolver _container;
    private DomainEventHub _hub;

    [SetUp]
    public void CreateScope()
    {
        var builder = new ContainerBuilder();

        // The one registration a pooled enemy body actually needs: EnemyHitFeedback takes the hub
        // through [Inject], and a body created outside a container is silently deaf to every hit.
        builder.Register<DomainEventHub>(Lifetime.Scoped).As<IDomainEvents>().AsSelf();

        _container = builder.Build();
        _hub = _container.Resolve<DomainEventHub>();
    }

    [TearDown]
    public void DestroyScope()
    {
        _container?.Dispose();
        _container = null;
        _hub = null;

        for (int i = 0; i < _created.Count; i++)
        {
            // Unity's ==: a pool disposed inside the test has already destroyed some of these, and
            // a destroyed object is a live reference that only compares equal to null through the
            // engine's operator.
            if (_created[i] != null)
            {
                Object.Destroy(_created[i]);
            }
        }

        _created.Clear();
    }

    /// <summary>
    /// Row 8. The full life of an enemy body, and the assertion that none of it survives the
    /// return.
    /// </summary>
    [UnityTest]
    public IEnumerator EnemyView_ComesBackCleanAfterAFullLife()
    {
        EnemyView template = EnemyTemplate(out Material live, out Material dissolve);

        using var pool = new ViewPool<EnemyView>(_container, template, null, prewarm: 1);

        EnemyView body = pool.Get();
        Track(body.gameObject);

        body.Bind(1, new Vector3(3f, 0f, 3f));

        // The frame that runs Awake, OnEnable and Start on the rented body — everything
        // EnemyHitFeedback reads about what "clean" means is captured there.
        yield return null;

        Renderer renderer = body.GetComponentInChildren<Renderer>();

        // The live material's own colour, which is what the reset restores. Read from the material
        // rather than from the renderer's property block, because nothing has written a block yet:
        // an unwritten block answers every colour with zero, and asserting against that would be
        // asserting that a clean body is drawn invisible.
        Color cleanColour = live.GetColor(BaseColorId);
        Vector3 cleanScale = body.transform.localScale;

        // A hit, a wind-up, and then the killing blow — the three things a life does to how a body
        // looks, in the order a fight does them.
        _hub.Publish(new EnemyDamaged(1, amount: 5f, hpFraction: 0.5f, killed: false));

        yield return null;

        _hub.Publish(new EnemyTelegraph(1, duration: 0.4f));

        yield return null;
        yield return null;

        Assert.That(
            body.transform.localScale.y,
            Is.GreaterThan(cleanScale.y),
            "Sanity: the wind-up must actually swell the body, or the reset below undoes nothing.");

        _hub.Publish(new EnemyDied(1, Husk, new System.Numerics.Vector3(3f, 0f, 3f)));

        // Several frames of dissolve, which is what an EditMode test cannot have: the stretch and
        // the fade are written from Update, one frame at a time.
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        Assert.That(renderer.sharedMaterial, Is.SameAs(dissolve), "Sanity: the corpse is on the transparent material.");
        Assert.That(ColourOf(renderer).a, Is.LessThan(cleanColour.a), "Sanity: the corpse has begun to fade.");
        Assert.That(body.Body.enabled, Is.False, "Sanity: a death takes the body out of the physics query.");

        pool.Release(body);

        EnemyView reused = pool.Get();

        Assert.That(reused, Is.SameAs(body), "Sanity: the pool handed back the same body.");

        Assert.That(reused.Id, Is.EqualTo(EnemyView.Unbound), "A rented body stands in for nobody until it is bound.");
        Assert.That(reused.transform.localScale, Is.EqualTo(cleanScale), "The dissolve's stretch is not inherited.");
        Assert.That(renderer.sharedMaterial, Is.SameAs(live), "The corpse's transparent material is swapped back off.");
        Color restored = ColourOf(renderer);

        // Channel by channel with a tolerance rather than a whole-colour comparison: a Color that
        // has been through a material and a property block comes back equal to the eye and unequal
        // in the low bits, and NUnit's EqualTo on a struct is exact.
        Assert.That(restored.r, Is.EqualTo(cleanColour.r).Within(1e-3f), "The archetype's tint comes back.");
        Assert.That(restored.g, Is.EqualTo(cleanColour.g).Within(1e-3f));
        Assert.That(restored.b, Is.EqualTo(cleanColour.b).Within(1e-3f));
        Assert.That(restored.a, Is.EqualTo(cleanColour.a).Within(1e-3f), "And so does the alpha — a half-faded Husk reads as a rendering bug three systems from its cause.");
        Assert.That(reused.Body.enabled, Is.True, "The collider comes back on, or the next enemy in this body is unhittable for ever.");
        Assert.That(reused.Velocity, Is.EqualTo(Vector3.zero), "No shove is still in flight.");

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// The other half of the enemy reset, and the one with a clock on it: a shove caught mid-slide
    /// when the body is returned.
    /// </summary>
    [UnityTest]
    public IEnumerator EnemyView_ComesBackCleanAfterAKnockback()
    {
        EnemyView template = EnemyTemplate(out _, out _);

        using var pool = new ViewPool<EnemyView>(_container, template, null, prewarm: 1);

        EnemyView body = pool.Get();
        Track(body.gameObject);

        body.Bind(1, Vector3.zero);

        yield return null;

        // Five metres over 0.15 s, released after a single frame — so the slide is still playing
        // when the body goes back to the pool, which is exactly the case M1-19 left uncovered.
        body.Knockback(Vector2.right, distance: 5f);

        yield return null;

        pool.Release(body);

        EnemyView reused = pool.Get();

        var landing = new Vector3(-4f, 0f, 2f);

        reused.Bind(2, landing);

        Assert.That(reused.Velocity, Is.EqualTo(Vector3.zero), "A rebound body is not still carrying the last one's speed.");

        yield return null;
        yield return null;

        // The shove would otherwise have kept sliding this body +X for the rest of its 0.15 s. Only
        // the ground plane is asserted: there is no floor in this scene, so the y belongs to gravity.
        Assert.That(reused.transform.position.x, Is.EqualTo(landing.x).Within(0.05f), "No slide was inherited.");
        Assert.That(reused.transform.position.z, Is.EqualTo(landing.z).Within(0.05f));
        Assert.That(reused.Velocity, Is.EqualTo(Vector3.zero));

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>The same rule, one family along: a bolt that flew its whole flight and came back.</summary>
    [UnityTest]
    public IEnumerator ProjectileView_ComesBackCleanAfterAFullFlight()
    {
        ProjectileView template = ProjectileTemplate();

        using var pool = new ViewPool<ProjectileView>(_container, template, null, prewarm: 2);

        // A second body, rented and never used, is what "as clean as a fresh one" is measured
        // against — rather than against a pose this fixture asserts from memory.
        ProjectileView fresh = pool.Get();
        Track(fresh.gameObject);

        ProjectileView body = pool.Get();
        Track(body.gameObject);

        yield return null;

        body.Bind(1, new Vector3(2f, 1f, 2f), new Vector3(12f, 0f, 2f), flightTime: 1f);

        body.Step(0.5f);
        body.Step(0.5f);

        Assert.That(body.transform.position.x, Is.EqualTo(12f).Within(1e-3f), "Sanity: the bolt flew.");
        Assert.That(body.transform.rotation, Is.Not.EqualTo(fresh.transform.rotation), "Sanity: and tipped over on the way.");

        pool.Release(body);

        ProjectileView reused = pool.Get();

        Assert.That(reused, Is.SameAs(body), "Sanity: the pool handed back the same body.");

        Assert.That(reused.Id, Is.EqualTo(ProjectileView.Unbound));
        Assert.That(reused.transform.position, Is.EqualTo(fresh.transform.position), "A returned bolt is back where a fresh one starts.");
        Assert.That(reused.transform.rotation, Is.EqualTo(fresh.transform.rotation), "And pointing the way a fresh one points.");

        // The elapsed time is asserted through what it would do rather than through a field nothing
        // else needs: a body that kept its last flight's clock would arrive at the far end of this
        // one immediately instead of at its midpoint.
        reused.Bind(2, Vector3.zero, new Vector3(10f, 0f, 0f), flightTime: 1f);
        reused.Step(0.5f);

        Assert.That(reused.transform.position.x, Is.EqualTo(5f).Within(1e-3f), "The previous flight's elapsed time is not inherited.");

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// AR §14, stated as the number that matters: twelve lives through a pool of four create four
    /// bodies, not twelve.
    /// </summary>
    /// <remarks>
    /// Three rounds of four concurrent bolts rather than twelve consecutive ones. A pool rented and
    /// returned one at a time hands back the same instance every time and would pass this row with
    /// a prewarm of one — the question is whether the <em>fifth</em> body is ever created, and only
    /// a full set in the air at once asks it.
    /// </remarks>
    [UnityTest]
    public IEnumerator Pool_ReusesRatherThanInstantiating()
    {
        ProjectileView template = ProjectileTemplate();

        using var pool = new ViewPool<ProjectileView>(_container, template, null, prewarm: 4);

        yield return null;

        var seen = new HashSet<int>();
        var inFlight = new List<ProjectileView>(4);

        for (int round = 0; round < 3; round++)
        {
            for (int i = 0; i < 4; i++)
            {
                ProjectileView body = pool.Get();

                Track(body.gameObject);
                seen.Add(body.GetInstanceID());
                inFlight.Add(body);

                body.Bind((round * 4) + i + 1, Vector3.zero, new Vector3(6f, 0f, 0f), flightTime: 0.5f);
                body.Step(0.5f);
            }

            for (int i = 0; i < inFlight.Count; i++)
            {
                pool.Release(inFlight[i]);
            }

            inFlight.Clear();

            yield return null;
        }

        Assert.That(
            seen,
            Has.Count.EqualTo(4),
            "Twelve lives must come out of the four bodies the prewarm built — a fifth instance "
                + "means the pool was bypassed and a run allocates per shot (AR §14).");

        Assert.That(pool.CountActive, Is.Zero);
        Assert.That(pool.CountInactive, Is.EqualTo(4), "The pool still holds exactly what it started with.");

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// The smallest object <see cref="EnemyView"/> and <see cref="EnemyHitFeedback"/> will accept:
    /// a body, a mesh to tint, and the two materials the dissolve swaps between.
    /// </summary>
    /// <remarks>
    /// The two serialized references are written by reflection, which is the honest price of this
    /// fixture: they have no public setter — correctly, since a prefab is where they belong — and a
    /// PlayMode assembly cannot reach <c>SerializedObject</c> to write them the way an EditMode one
    /// would.
    /// </remarks>
    private EnemyView EnemyTemplate(out Material live, out Material dissolve)
    {
        var root = new GameObject("EnemyTemplate");

        // Inactive first, so neither Awake nor Start runs on the template — see the class remarks.
        root.SetActive(false);

        GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);

        // A primitive ships a collider, and a second capsule on the body would be a second thing
        // for a sweep to find.
        Object.DestroyImmediate(mesh.GetComponent<Collider>());

        mesh.transform.SetParent(root.transform, false);

        var renderer = mesh.GetComponent<MeshRenderer>();

        live = Track(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        live.SetColor(BaseColorId, new Color(0.43f, 0.42f, 0.39f, 1f));

        dissolve = Track(new Material(Shader.Find("Universal Render Pipeline/Lit")));

        renderer.sharedMaterial = live;

        // EnemyView first: EnemyHitFeedback [RequireComponent]s it, and adding it brings the
        // CapsuleCollider and the CharacterController the body needs with it.
        var view = root.AddComponent<EnemyView>();
        var feedback = root.AddComponent<EnemyHitFeedback>();

        SetPrivate(feedback, "_renderer", renderer);
        SetPrivate(feedback, "_dissolveMaterial", dissolve);

        Track(root);

        return view;
    }

    /// <summary>The bolt's equivalent: a transform and the component under test, and nothing else.</summary>
    private ProjectileView ProjectileTemplate()
    {
        var root = new GameObject("ProjectileTemplate");

        root.SetActive(false);

        var view = root.AddComponent<ProjectileView>();

        Track(root);

        return view;
    }

    private static Color ColourOf(Renderer renderer)
    {
        var block = new MaterialPropertyBlock();

        renderer.GetPropertyBlock(block);

        return block.GetColor(BaseColorId);
    }

    private static void SetPrivate(Object target, string field, object value) =>
        target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);

    private T Track<T>(T o) where T : Object
    {
        _created.Add(o);

        return o;
    }
}
