using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using VContainer;
using CoreVector3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// The census that turns the two decoy events into bodies, the third way a decoy leaves that core
/// never announces, and the three things a corpse deliberately is not. Nothing here throws when it
/// is wrong: a decoy with a collider makes the player's own swing damage a stranger, a decoy left
/// standing at a boundary taunts nobody from the last arena's floor for the rest of the run, and a
/// corpse in <see cref="Palette.Danger"/> tells the player that the safest square on the map will
/// hurt them.
/// </summary>
/// <remarks>
/// <para>
/// The prefab is a scene object rather than <c>VFX_Decoy.prefab</c> for every row but the three
/// that are about the shipped asset, which is all <c>IObjectResolver.Instantiate</c> needs — it
/// takes a <see cref="Component"/>, not an asset — and it keeps the fixture from depending on how
/// the art is dressed. <c>Awake</c> never runs in EditMode ([Traps §5](../../../../../Docs/Traps.md)),
/// so these bodies carry their field initialisers and nothing paints them.
/// </para>
/// <para>
/// Every object the fixture or the pool creates is destroyed in the teardown. An EditMode test that
/// leaks a GameObject leaks it into every test that runs after it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class DecoyViewsTests
{
    private const string DecoyPrefabPath = "Assets/_Project/Prefabs/Vfx/VFX_Decoy.prefab";

    private static readonly ContentId HuskId = new ContentId("enemy.husk");

    private readonly List<GameObject> _created = new List<GameObject>();
    private readonly List<EnemyViews> _censuses = new List<EnemyViews>();

    private IObjectResolver _container;
    private DomainEventHub _hub;
    private DecoyView _prefab;
    private DecoyViews _views;

    [SetUp]
    public void CreateCensus()
    {
        // An empty container is enough: Instantiate injects the new object, and nothing on a
        // DecoyView asks to be injected. A real run's resolver differs only in what it holds.
        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();
        _prefab = Template<DecoyView>("DecoyTemplate");
    }

    [TearDown]
    public void DestroyCensus()
    {
        _views?.Dispose();
        _views = null;

        // Views before the hub: disposing them unsubscribes them, and disposing the hub first would
        // leave that dispose pointing at an orphaned channel — harmless here, and the wrong order
        // to write down anywhere.
        for (int i = 0; i < _censuses.Count; i++)
        {
            _censuses[i]?.Dispose();
        }

        _censuses.Clear();

        _hub?.Dispose();
        _hub = null;

        _container?.Dispose();
        _container = null;

        for (int i = 0; i < _created.Count; i++)
        {
            // Unity's ==: a pool disposed above has already destroyed some of these, and a
            // destroyed object is a live reference that only compares equal to null through the
            // engine's operator. DestroyImmediate, not Destroy: in edit mode the latter destroys
            // nothing and logs an error (M0-14).
            if (_created[i] != null)
            {
                Object.DestroyImmediate(_created[i]);
            }
        }

        _created.Clear();
    }

    // ---- The two events (rules 2, 4) -------------------------------------------------------------

    [Test]
    public void Decoy_SpawnRentsAndBinds()
    {
        _views = Census(prewarm: 1);

        Drop(id: 1, at: new CoreVector3(5f, 0f, 5f));

        Assert.That(_views.Count, Is.EqualTo(1));
        Assert.That(_views.TryGet(1, out DecoyView view), Is.True, "The id core issued must resolve.");

        Assert.That(view.Id, Is.EqualTo(1));
        Assert.That(view.IsBound, Is.True);

        // Where the blink *left*, not where it arrived (M5-03 rule 6). A corpse drawn at the
        // player's new position would taunt the arena towards the thing the dodge was escaping.
        Assert.That(view.Position.x, Is.EqualTo(5f).Within(1e-4f));
        Assert.That(view.Position.z, Is.EqualTo(5f).Within(1e-4f));
    }

    [Test]
    public void Decoy_ExpiryReturnsIt()
    {
        _views = Census(prewarm: 1);

        Drop(id: 1, at: new CoreVector3(5f, 0f, 5f));

        Assert.That(_views.TryGet(1, out DecoyView first), Is.True);

        _hub.Publish(new DecoyExpired(1));

        Assert.That(_views.Count, Is.Zero, "A corpse whose three seconds ran out leaves no body.");
        Assert.That(_views.TryGet(1, out _), Is.False);
        Assert.That(first.Id, Is.EqualTo(DecoyView.Unbound), "The body goes back unbound.");
        Assert.That(_views.PooledCount, Is.EqualTo(1), "And it goes back to the pool, not to the bin.");

        Drop(id: 2, at: new CoreVector3(1f, 0f, 1f));

        Assert.That(_views.TryGet(2, out DecoyView second), Is.True);

        Assert.That(
            second,
            Is.SameAs(first),
            "The same body is rented again rather than a second one created — the whole point of "
                + "pooling a thing that is dropped on the frame a dodge is being timed.");

        Assert.That(second.Id, Is.EqualTo(2), "Rebound to the new id, not still answering the old one.");
    }

    /// <summary>
    /// <see cref="LureSystem.Capacity"/>, and the pool never grows past it.
    /// </summary>
    /// <remarks>
    /// Two rather than one because the Shroudstep's cooldown is 2.5 s and its decoy stands for 3, so
    /// two can legitimately overlap for half a second and the second blink of a chase must not be
    /// the one that fails. A third is not something core can ask for — <c>LureSystem.Drop</c>
    /// refuses it silently — so a pool that grew here would be growing for a caller that does not
    /// exist.
    /// </remarks>
    [Test]
    public void Decoy_TwoStandAtOnce()
    {
        _views = Census(prewarm: LureSystem.Capacity);

        Drop(id: 1, at: new CoreVector3(5f, 0f, 5f));
        Drop(id: 2, at: new CoreVector3(-5f, 0f, -5f));

        Assert.That(_views.Count, Is.EqualTo(LureSystem.Capacity));
        Assert.That(_views.TryGet(1, out DecoyView first), Is.True);
        Assert.That(_views.TryGet(2, out DecoyView second), Is.True);

        Assert.That(first, Is.Not.SameAs(second), "Both ids resolved to one body.");
        Assert.That(_views.PooledCount, Is.Zero, "Both bodies are in service, so the pool is empty.");

        // A thousand cycles at the cap produce no third body: the prewarm is the whole of what a run
        // ever holds, so nothing Instantiates after the scene has loaded (AR §14, GD §11.3).
        for (int cycle = 0; cycle < 1_000; cycle++)
        {
            _hub.Publish(new DecoyExpired(1));
            _hub.Publish(new DecoyExpired(2));

            Drop(id: 1, at: new CoreVector3(5f, 0f, 5f));
            Drop(id: 2, at: new CoreVector3(-5f, 0f, -5f));
        }

        Assert.That(
            _views.Count + _views.PooledCount,
            Is.EqualTo(LureSystem.Capacity),
            "Two thousand drops produced a third body, so the pool is being bypassed and every "
                + "Shroudstep is an Instantiate on the frame a dodge is being timed.");
    }

    /// <summary>
    /// The boundary sweep core does not announce — and the correction to rule 4 that
    /// <see cref="Decoy_ClearLeavesNoOrphan"/> cannot make.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>StageFlow.Advance</c> calls <c>LureSystem.Clear</c>, which publishes nothing —
    /// <c>ProjectileSystem.Clear</c>'s silence, for its reason. <b>A decoy is the kind of thing that
    /// can genuinely cross a boundary</b>: 3 s of corpse against 2 s of gate and arrival. Without
    /// this the body stands in the <em>next</em> arena for the rest of the run, the pool is a body
    /// short, and the census's entry for id 1 is overwritten by the next stage's first decoy, which
    /// leaks that body out of the index entirely.
    /// </para>
    /// <para>
    /// <b>The spec's rule 4 says <see cref="DecoyViews.Dispose"/> answers this and it cannot:</b>
    /// this object is disposed when <c>RunScope</c> is, and a stage crossing does not dispose the
    /// run — it swaps an arena underneath one. Both rows are here because they are different
    /// claims, and only this one is about a boundary.
    /// </para>
    /// </remarks>
    [Test]
    public void Decoy_IsSweptAtAStageBoundary()
    {
        _views = Census(prewarm: LureSystem.Capacity);

        Drop(id: 1, at: new CoreVector3(5f, 0f, 5f));
        Drop(id: 2, at: new CoreVector3(-5f, 0f, -5f));

        Assert.That(_views.Count, Is.EqualTo(LureSystem.Capacity));

        _hub.Publish(new StageArrived(2, new ContentId("arena.pillars")));

        Assert.That(_views.Count, Is.Zero, "A corpse followed the player into the next arena.");

        Assert.That(
            _views.PooledCount,
            Is.EqualTo(LureSystem.Capacity),
            "And every body went back to the pool rather than being leaked out of the census.");

        // Core's ids go back to 1 with the bodies, so the next stage's first drop asks for a number
        // the old pair was using. It resolves to a fresh rental rather than to a ghost.
        Drop(id: 1, at: new CoreVector3(9f, 0f, 9f));

        Assert.That(_views.Count, Is.EqualTo(1));
        Assert.That(_views.TryGet(1, out DecoyView view), Is.True);
        Assert.That(view.Position.x, Is.EqualTo(9f).Within(1e-4f));
    }

    /// <summary>Rule 4's row: leaving the Run scene orphans nothing.</summary>
    [Test]
    public void Decoy_ClearLeavesNoOrphan()
    {
        _views = Census(prewarm: LureSystem.Capacity);

        var standing = new List<DecoyView>();

        for (int id = 1; id <= LureSystem.Capacity; id++)
        {
            Drop(id, new CoreVector3(id, 0f, id));

            Assert.That(_views.TryGet(id, out DecoyView view), Is.True);

            standing.Add(view);
        }

        _views.Dispose();

        Assert.That(_views.Count, Is.Zero, "Disposing drops the census with the bodies it indexed.");

        for (int i = 0; i < standing.Count; i++)
        {
            Assert.That(
                standing[i] == null,
                Is.True,
                "A body outlived the run that made it. Unity's == is the check that matters: a "
                    + "destroyed component is a live C# reference.");
        }

        // The subscriptions are gone, so a decoy dropped by a run this object has outlived reaches
        // nothing — and cannot ask a disposed pool for a body.
        Assert.That(() => Drop(id: 99, at: CoreVector3.Zero), Throws.Nothing);
        Assert.That(_views.Count, Is.Zero);

        // Idempotent, like MinionViews' — a double dispose is a disposal-order question nobody
        // should have to answer.
        Assert.That(() => _views.Dispose(), Throws.Nothing);
    }

    // ---- What a corpse is not (rules 1, 2) -------------------------------------------------------

    /// <summary>
    /// <b>Rule 1, the row that proves the swing cannot reach one.</b>
    /// </summary>
    /// <remarks>
    /// M5-03 rule 7 says a decoy cannot be hit, and the whole of what enforces that on the Unity
    /// side is the absence of a shape. <c>ConeOverlapQuery.Query</c> turns a physics hit into an
    /// enemy id through <c>EnemyViews.TryGetId</c> and through nothing else; a collider here would
    /// be swept by the player's own swing, and core would apply the damage to whichever enemy holds
    /// the number that came back — a stranger, silently. <see cref="MinionView"/>'s rule 1 at the
    /// one body in the game with even less business being in that index than a Wight.
    /// </remarks>
    [Test]
    public void Decoy_HasNoCollider()
    {
        GameObject prefab = LoadDecoy();

        Assert.That(
            prefab.GetComponentsInChildren<Collider>(includeInactive: true),
            Is.Empty,
            $"{DecoyPrefabPath} has a collider on it. Nothing in the game is supposed to be able "
                + "to sweep a corpse, and a shape here is what would let the player's own swing "
                + "report a stranger's id to core (M5-03 rule 7).");
    }

    /// <summary>The same claim from the index's side, with a real enemy census beside it.</summary>
    [Test]
    public void Decoy_IsNotInTheEnemyColliderIndex()
    {
        var enemyViews = new EnemyViews(
            _container,
            Template<EnemyView>("EnemyTemplate"),
            null,
            _hub,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()),
            prewarm: 1);

        _censuses.Add(enemyViews);

        _views = Census(prewarm: 1);

        _hub.Publish(new EnemySpawned(1, HuskId, new CoreVector3(1f, 0f, 1f)));

        Drop(id: 1, at: new CoreVector3(5f, 0f, 5f));

        Assert.That(enemyViews.TryGet(1, out EnemyView husk), Is.True);
        Assert.That(_views.TryGet(1, out DecoyView decoy), Is.True);

        Assert.That(
            enemyViews.TryGetId(husk.Body, out int enemyId),
            Is.True,
            "Sanity: the enemy's collider does resolve, so the index is populated and the sweep "
                + "below means something.");

        Assert.That(enemyId, Is.EqualTo(1));

        // Every collider the scene holds, asked about by the one index a swing resolves through.
        // None of them may belong to the corpse — which today is true because it has no shape at
        // all, and this row is what notices the day somebody adds one.
        foreach (Collider collider in Object.FindObjectsByType<Collider>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (!enemyViews.TryGetId(collider, out _))
            {
                continue;
            }

            Assert.That(
                collider.transform.IsChildOf(decoy.transform),
                Is.False,
                "A collider under the corpse resolves to an enemy id. The player's next swing will "
                    + "damage whichever enemy holds that number instead of doing nothing, which is "
                    + "what hitting a decoy is supposed to do.");
        }
    }

    /// <summary>
    /// Rule 2, asserted so a later task has to argue with this row rather than quietly add a clock.
    /// </summary>
    /// <remarks>
    /// Every other census on <c>RunScope</c> is walked once a frame with <c>snapshot.Dt</c> because
    /// each holds something that is interpolating while core counts it down. A decoy interpolates
    /// nothing (M5-03 rule 1), so its whole life is two events — and a <c>Step</c> here would be a
    /// second clock running beside core's, which is the thing AR §18.2 exists to prevent.
    /// </remarks>
    [Test]
    public void Decoy_HasNoStep()
    {
        Assert.That(
            typeof(DecoyViews).GetMethod("Step", BindingFlags.Public | BindingFlags.Instance),
            Is.Null,
            "DecoyViews has grown a Step. A decoy does not move, does not fade and does not count "
                + "anything down (M5-03 rule 1), so a per-frame method here is a clock beside "
                + "core's with nothing to run.");

        // And nothing holds one to call. RunTicker takes the census for the subscription guarantee
        // and discards it with `_ =` (rule 3), so a field of this type appearing is the frame
        // having quietly acquired a seventh thing to walk.
        Assert.That(
            typeof(RunTicker)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(DecoyViews)),
            Is.Empty,
            "RunTicker now keeps the decoy census in a field. It takes it to guarantee the "
                + "subscription and for nothing else — if the frame needs to walk it, rule 2 is "
                + "the thing to argue with first.");
    }

    // ---- The shipped asset (rule 1, Traps §5) ----------------------------------------------------

    /// <summary>
    /// [Traps §5] The row every authored prefab owes: the component resolves rather than
    /// deserialising as null.
    /// </summary>
    /// <remarks>
    /// A <c>MonoBehaviour</c> in a file-scoped namespace cannot be found by Unity 6.3's script
    /// importer, and every asset referencing it loads as null with nothing reported anywhere. The
    /// failure is a decoy prefab that rents, binds and returns perfectly and has no behaviour on it
    /// at all.
    /// </remarks>
    [Test]
    public void Decoy_IsLinkedToAMonoScript()
    {
        GameObject prefab = LoadDecoy();

        Assert.That(
            prefab.GetComponent<DecoyView>(),
            Is.Not.Null,
            $"{DecoyPrefabPath} has no DecoyView on it. If the component is on the prefab in the "
                + "Inspector, the script reference has come back null — check the namespace is a "
                + "block one (Traps §5).");
    }

    /// <summary>
    /// [Rule 1, GD §16.4] A corpse is the player's own cyan at half alpha, and emphatically not
    /// <see cref="Palette.Danger"/>.
    /// </summary>
    /// <remarks>
    /// <b>Both halves are the design.</b> The cyan says <em>yours</em> — it is the player's
    /// silhouette left standing — and the half alpha says <em>dead</em>, which is the only thing
    /// separating it from the player's own body. The red is forbidden here twice over: GD §16.4
    /// reserves it for danger and nothing else ever, and a red shape on the floor that every enemy
    /// in the arena is walking towards would read as the single most dangerous square on the map,
    /// when it is in fact the safest.
    /// </remarks>
    [Test]
    public void Decoy_IsThePlayersCyanAtHalfAlpha()
    {
        GameObject prefab = LoadDecoy();

        var view = prefab.GetComponent<DecoyView>();

        Assert.That(view.Tint.r, Is.EqualTo(Palette.Player.r).Within(1e-3f));
        Assert.That(view.Tint.g, Is.EqualTo(Palette.Player.g).Within(1e-3f));
        Assert.That(view.Tint.b, Is.EqualTo(Palette.Player.b).Within(1e-3f));

        Assert.That(
            view.Tint.a,
            Is.EqualTo(DecoyView.Alpha).Within(1e-3f),
            "A corpse at full alpha is the player standing there, which is exactly the reading the "
                + "half is for.");

        Assert.That(
            Palette.IsDanger(view.Tint),
            Is.False,
            "The corpse is drawn in the colour GD §16.4 reserves for danger. Every enemy in the "
                + "arena walks towards it, so red would mark the safest square on the map as the "
                + "most dangerous one.");

        // And the surface it is drawn on has to be one where alpha means something. An opaque
        // material renders this tint fully solid and nothing anywhere reports it — which is the
        // whole of ledger row 3, arriving at a seventh prefab.
        var renderer = prefab.GetComponentInChildren<MeshRenderer>(true);

        Assert.That(renderer, Is.Not.Null, "The corpse has no renderer, so it is invisible.");

        Assert.That(
            renderer.sharedMaterial.GetFloat("_Surface"),
            Is.EqualTo(1f),
            $"{renderer.sharedMaterial.name} is an opaque surface, so the corpse's half alpha is "
                + "discarded and it draws as solid as the player.");
    }

    // ---- Guards ----------------------------------------------------------------------------------

    [Test]
    public void Constructor_NullDependency_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DecoyViews(null, _prefab, null, _hub));
        Assert.Throws<ArgumentNullException>(() => new DecoyViews(_container, null, null, _hub));
        Assert.Throws<ArgumentNullException>(() => new DecoyViews(_container, _prefab, null, null));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DecoyViews(_container, _prefab, null, _hub, prewarm: -1));
    }

    [Test]
    public void Constructor_DestroyedParent_IsTheSceneRoot()
    {
        var parent = new GameObject("Parent");

        // Taken before the object is destroyed, which is the whole point: afterwards this is a live
        // C# reference to a dead object, and only Unity's == says so. The pool normalises it to the
        // scene root rather than handing Instantiate a destroyed transform.
        Transform held = parent.transform;

        Object.DestroyImmediate(parent);

        Assert.That(
            () => _views = new DecoyViews(_container, _prefab, held, _hub, prewarm: 1),
            Throws.Nothing);

        Assert.That(_views.PooledCount, Is.EqualTo(1));
    }

    [Test]
    public void Decoy_AnUnknownIdIsNotAnError()
    {
        _views = Census(prewarm: 1);

        // MinionViews.Release's rule: an id may legitimately never have had a body made for it —
        // one dropped before this object existed, or one whose body went with a stage boundary. A
        // throw here would end a run over a corpse nobody could see anyway.
        Assert.That(() => _hub.Publish(new DecoyExpired(99)), Throws.Nothing);

        Assert.That(_views.Count, Is.Zero);
        Assert.That(_views.PooledCount, Is.EqualTo(1), "And nothing was returned that was never rented.");
    }

    [Test]
    public void Bind_InvalidId_Throws()
    {
        DecoyView view = Template<DecoyView>("Loose");

        // Ids are issued from 1 and LureSystem spells zero NoLure, so a zero or negative one means
        // the caller invented it — and a body bound to Unbound could never be retired by its own
        // DecoyExpired, which would leave it standing for the rest of the run.
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Bind(DecoyView.Unbound, Vector3.zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Bind(-1, Vector3.zero));
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>A census over the fixture's prefab, hub and container.</summary>
    private DecoyViews Census(int prewarm) =>
        new DecoyViews(_container, _prefab, null, _hub, prewarm);

    /// <summary>
    /// Three seconds, which is <c>Gravecaller.asset</c>'s Shroudstep — a number no row here reads,
    /// because rule 2 is that nothing on this side counts it down.
    /// </summary>
    private void Drop(int id, CoreVector3 at) =>
        _hub.Publish(new DecoySpawned(id, at, duration: 3f));

    private static GameObject LoadDecoy()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DecoyPrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {DecoyPrefabPath}.");

        return prefab;
    }

    /// <summary>
    /// An inactive body carrying <typeparamref name="T"/> — inactive first, so neither
    /// <c>Awake</c> nor <c>Start</c> runs on the template.
    /// </summary>
    private T Template<T>(string name)
        where T : Component
    {
        var root = new GameObject(name);

        root.SetActive(false);

        _created.Add(root);

        return root.AddComponent<T>();
    }
}
