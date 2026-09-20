using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEditor;
using UnityEngine;
using VContainer;
using CoreVector2 = System.Numerics.Vector2;
using CoreVector3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// The census that turns the three minion events into bodies, what those bodies report back into
/// the snapshot, and the two things a Wight deliberately is not. Almost nothing here throws when it
/// is wrong: a Wight in the enemy pool makes the player's own swing damage a stranger, a Wight in
/// the enemy slots makes the arena's difficulty budget count the player's army against them, and a
/// slot written short carries a fact about whoever stood there last.
/// </summary>
/// <remarks>
/// <para>
/// The prefab is a scene object rather than <c>Wight.prefab</c> for every row but the two that are
/// about the shipped asset, which is all <c>IObjectResolver.Instantiate</c> needs — it takes a
/// <see cref="Component"/>, not an asset — and it keeps the fixture from depending on how the art
/// is dressed. <c>Awake</c> never runs in EditMode ([Traps §5](../../../../../Docs/Traps.md)), so
/// these bodies carry their field initialisers and nothing paints them.
/// </para>
/// <para>
/// <b>Four rows are about <c>SnapshotBuilder</c> rather than about this census</b>, and they are
/// here rather than in <c>SnapshotBuilderTests</c> because the claim each makes is about the
/// <em>minions</em>: where they land, what they do not inflate, and what is deliberately not
/// computed for them. The builder is real in all four — a fixture that faked it would be asserting
/// its own arrangement.
/// </para>
/// <para>
/// Every object the fixture or the pool creates is destroyed in the teardown. An EditMode test that
/// leaks a GameObject leaks it into every test that runs after it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class MinionViewsTests
{
    private const string WightPrefabPath = "Assets/_Project/Prefabs/Minions/Wight.prefab";

    /// <summary>
    /// Every shipped <c>EnemyDefinition</c>'s body scale, so rule 8's "smaller than every
    /// archetype" is measured rather than asserted from memory.
    /// </summary>
    private const string EnemyDefinitionFolder = "Assets/_Project/Data/Enemies";

    private static readonly ContentId WightId = new ContentId("minion.wight");
    private static readonly ContentId HuskId = new ContentId("enemy.husk");

    private readonly List<GameObject> _created = new List<GameObject>();
    private readonly List<EnemyViews> _censuses = new List<EnemyViews>();
    private readonly List<InputAdapter> _inputs = new List<InputAdapter>();

    private IObjectResolver _container;
    private DomainEventHub _hub;
    private MinionView _prefab;
    private MinionViews _views;

    [SetUp]
    public void CreateCensus()
    {
        // An empty container is enough: Instantiate injects the new object, and nothing on a
        // MinionView asks to be injected. A real run's resolver differs only in what it holds.
        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();
        _prefab = Template<MinionView>("WightTemplate");
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

        for (int i = 0; i < _inputs.Count; i++)
        {
            _inputs[i]?.Dispose();
        }

        _inputs.Clear();

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

    // ---- The census (rules 1, 2) ----------------------------------------------------------------

    [Test]
    public void Minions_SpawnRentsAndBinds()
    {
        _views = Census(prewarm: 1);

        Raise(id: 1, at: new CoreVector3(4f, 0f, 4f));

        Assert.That(_views.Count, Is.EqualTo(1));
        Assert.That(_views.TryGet(1, out MinionView view), Is.True, "The id core issued must resolve.");

        Assert.That(view.Id, Is.EqualTo(1));
        Assert.That(view.IsBound, Is.True);

        // At the corpse, not at wherever the pooled body was sitting: a Wight drawn for one frame
        // at the previous one's grave is a body that teleports on the tick it is raised.
        Assert.That(view.Position.x, Is.EqualTo(4f).Within(1e-4f));
        Assert.That(view.Position.z, Is.EqualTo(4f).Within(1e-4f));

        Assert.That(view.Velocity, Is.EqualTo(Vector3.zero), "A Wight stands still until core walks it.");
    }

    [Test]
    public void Minions_DespawnReturnsToThePool()
    {
        _views = Census(prewarm: 1);

        Raise(id: 1, at: new CoreVector3(4f, 0f, 4f));

        Assert.That(_views.TryGet(1, out MinionView first), Is.True);

        _hub.Publish(new MinionDespawned(1));

        Assert.That(_views.Count, Is.Zero, "A Wight whose twenty seconds ran out leaves no body.");
        Assert.That(_views.TryGet(1, out _), Is.False);
        Assert.That(first.Id, Is.EqualTo(MinionView.Unbound), "The body goes back unbound.");
        Assert.That(_views.PooledCount, Is.EqualTo(1), "And it goes back to the pool, not to the bin.");

        Raise(id: 2, at: new CoreVector3(1f, 0f, 1f));

        Assert.That(_views.TryGet(2, out MinionView second), Is.True);

        Assert.That(
            second,
            Is.SameAs(first),
            "The same body is rented again rather than a second one created — the whole point of "
                + "pooling a thing that stands up behind every fourth kill (M5-04b).");

        Assert.That(second.Id, Is.EqualTo(2), "Rebound to the new id, not still answering the old one.");
    }

    /// <summary>
    /// Rule 2: a clock running out and a killing blow are two facts about the run and one fact
    /// about the body.
    /// </summary>
    /// <remarks>
    /// A census that listened to only one of them would leave a Wight standing in the arena for the
    /// rest of the run, reporting itself into the snapshot under an id core no longer resolves.
    /// Nothing about that throws.
    /// </remarks>
    [Test]
    public void Minions_ADeathReturnsItToo()
    {
        _views = Census(prewarm: 1);

        Raise(id: 1, at: new CoreVector3(4f, 0f, 4f));

        Assert.That(_views.TryGet(1, out MinionView view), Is.True);

        _hub.Publish(new MinionDied(1, new CoreVector3(4f, 0f, 4f)));

        Assert.That(_views.Count, Is.Zero);
        Assert.That(_views.TryGet(1, out _), Is.False);
        Assert.That(view.Id, Is.EqualTo(MinionView.Unbound));
        Assert.That(_views.PooledCount, Is.EqualTo(1));
    }

    [Test]
    public void Minions_AnUnknownIdIsNotAnError()
    {
        _views = Census(prewarm: 1);

        // EnemyViews.OnDespawned's rule: an id may legitimately never have had a body made for it.
        // A throw here would end a run over a Wight nobody could see anyway.
        Assert.That(() => _hub.Publish(new MinionDespawned(99)), Throws.Nothing);
        Assert.That(() => _hub.Publish(new MinionDied(99, CoreVector3.Zero)), Throws.Nothing);

        Assert.That(_views.Count, Is.Zero);
        Assert.That(_views.PooledCount, Is.EqualTo(1), "And nothing was returned that was never rented.");
    }

    /// <summary>
    /// The pool is warm before the run starts and nothing is built afterwards — AR §14, and the
    /// reason the prewarm is <see cref="MinionSystem.MaxConcurrent"/> rather than a guess.
    /// </summary>
    /// <remarks>
    /// <b>Two claims, measured two ways, because only one of them is a GC question.</b> The
    /// rent-and-return half is about <c>Instantiate</c>, which the pool's own counts answer
    /// exactly: eight thousand rentals that produce no ninth body came out of the prewarm. The
    /// per-frame half is <see cref="MinionViews.CopyInto"/> — the only method on this class a
    /// frame calls — and that is what <c>AllocationAssert</c> is pointed at: rule 11's claim is
    /// that the concrete dictionary's struct value enumerator is what the <c>foreach</c> walks,
    /// and an <c>IEnumerable&lt;MinionView&gt;</c> there would box one sixty times a second.
    /// </remarks>
    [Test]
    public void Minions_PrewarmAllocatesNothingLater()
    {
        _views = Census(prewarm: MinionSystem.MaxConcurrent);

        Assert.That(_views.PooledCount, Is.EqualTo(MinionSystem.MaxConcurrent));

        for (int cycle = 0; cycle < 1_000; cycle++)
        {
            for (int i = 1; i <= MinionSystem.MaxConcurrent; i++)
            {
                Raise(i, new CoreVector3(i, 0f, 0f));
            }

            Assert.That(_views.Count, Is.EqualTo(MinionSystem.MaxConcurrent));

            for (int i = 1; i <= MinionSystem.MaxConcurrent; i++)
            {
                _hub.Publish(new MinionDespawned(i));
            }
        }

        Assert.That(
            _views.PooledCount,
            Is.EqualTo(MinionSystem.MaxConcurrent),
            "Eight thousand rentals produced a ninth body, so the pool is being bypassed and every "
                + "raise is an Instantiate on the frame a kill landed (AR §14, GD §11.3).");

        for (int i = 1; i <= MinionSystem.MaxConcurrent; i++)
        {
            Raise(i, new CoreVector3(i, 0f, 0f));
        }

        var snapshot = new WorldSnapshot(64);

        AllocationAssert.None(() =>
        {
            snapshot.Clear();
            _views.CopyInto(snapshot);
        });
    }

    // ---- The snapshot (rules 3, 4) ---------------------------------------------------------------

    /// <summary>
    /// Rule 3's stale-slot rule: <c>Clear</c> leaves the minion array's contents alone, so every
    /// field of a reused slot is assigned, zeroes included (AR §18.2).
    /// </summary>
    [Test]
    public void Minions_CopyIntoWritesEveryFieldOfItsSlot()
    {
        _views = Census(prewarm: 2);

        var snapshot = new WorldSnapshot(8);

        // A slot left behind by a Wight that stood somewhere else, with a route and an answer about
        // cover written into it by hand. Nothing in the game writes those two for a minion — that
        // is rule 3 — which is exactly why a leftover would survive for ever if this did not
        // overwrite it.
        ref EnemySense stale = ref snapshot.AddMinion();

        stale.Id = 99;
        stale.Position = new CoreVector3(9f, 0f, 9f);
        stale.Velocity = new CoreVector3(3f, 0f, 3f);
        stale.PathDirectionToPlayer = new CoreVector2(0.6f, 0.8f);
        stale.HasLineOfSight = true;

        snapshot.Clear();

        Raise(id: 1, at: new CoreVector3(1f, 0f, 1f));

        _views.CopyInto(snapshot);

        Assert.That(snapshot.MinionCount, Is.EqualTo(1));

        EnemySense sense = snapshot.Minions[0];

        Assert.That(sense.Id, Is.EqualTo(1));
        Assert.That(sense.Position.X, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(sense.Position.Z, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(sense.Velocity, Is.EqualTo(CoreVector3.Zero));

        Assert.That(
            sense.PathDirectionToPlayer,
            Is.EqualTo(CoreVector2.Zero),
            "A route belonging to a Wight that stood somewhere else two frames ago. Nothing "
                + "recomputes this for a minion, so an unwritten field is wrong for the rest of "
                + "the run (rule 3).");

        Assert.That(
            sense.HasLineOfSight,
            Is.False,
            "And the cover answer with it. Nothing on the friendly side reads this, which is "
                + "precisely why nothing would ever overwrite a stale one.");
    }

    /// <summary>
    /// Rule 4: the Wights land in their own slots, and the enemy count is the swarm's alone.
    /// </summary>
    /// <remarks>
    /// Where this goes wrong is not a crash. <c>ConcurrencyCurve</c> and <c>ThreatBudget</c> price a
    /// stage against how many bodies are in it, and a Wight counted among them would make raising
    /// an army <em>reduce</em> the stage's difficulty — GD §11.2's device-independence rule
    /// inverted from inside the game (rule 5).
    /// </remarks>
    [Test]
    public void Minions_AreNotInTheEnemySlots()
    {
        Frame frame = BuildFrame(enemies: 3, wights: 2);

        frame.Builder.Build(frame.Snapshot, 0.02f);

        Assert.That(frame.Snapshot.EnemyCount, Is.EqualTo(3), "Two Wights displaced nobody and joined nobody.");
        Assert.That(frame.Snapshot.MinionCount, Is.EqualTo(2));

        for (int i = 0; i < frame.Snapshot.EnemyCount; i++)
        {
            Assert.That(
                frame.Snapshot.Enemies[i].Position.Z,
                Is.LessThan(5f),
                "A Wight is standing in an enemy slot. Core would read it as something to fight, "
                    + "the player's targeter would aim at it, and the director would count it "
                    + "towards the stage being complete.");
        }
    }

    /// <summary>
    /// Rule 4's ordering, asserted rather than assumed: the census is copied in after the enemies,
    /// so the sight budget is sized from the swarm.
    /// </summary>
    /// <remarks>
    /// <c>LineOfSightSense</c>'s per-frame allowance is computed from the population it is handed,
    /// which is <c>snapshot.EnemyCount</c>. A Wight inside that number would buy raycasts for
    /// bodies that never ask a question, on exactly the frames the budget exists to protect.
    /// <para>
    /// <b>Ten and eight rather than the three and two the spec's row names, and the reason is
    /// arithmetic.</b> The budget is <c>ceil(population × refreshHz × dt)</c> floored at one, so at
    /// three bodies and five it rounds to the same number and the row could not have failed. The
    /// expected count is derived from a real <c>PathRefreshBudget</c> rather than written down, so
    /// a retuned cadence moves the assertion with it.
    /// </para>
    /// </remarks>
    [Test]
    public void Minions_DoNotInflateTheSightBudget()
    {
        const int Enemies = 10;
        const int Wights = MinionSystem.MaxConcurrent;

        Frame frame = BuildFrame(Enemies, Wights);

        var budget = new PathRefreshBudget(
            LineOfSightSense.DefaultRefreshHz,
            PathRefreshBudget.DefaultMaxPerFrame);

        int forTheSwarm = budget.ForFrame(Enemies, SnapshotBuilder.MaxDt);
        int forBoth = budget.ForFrame(Enemies + Wights, SnapshotBuilder.MaxDt);

        Assert.That(
            forBoth,
            Is.GreaterThan(forTheSwarm),
            "The fixture's premise: at these numbers the budget has to be able to tell the two "
                + "populations apart, or the assertion below is true either way.");

        frame.Builder.Build(frame.Snapshot, SnapshotBuilder.MaxDt);

        Assert.That(
            frame.Sight.RaycastsLastFrame,
            Is.EqualTo(forTheSwarm),
            "The cover sense was sized for eighteen bodies rather than ten, so the Wights are in "
                + "the enemy census and the frame is buying raycasts for bodies that never ask.");
    }

    /// <summary>
    /// Rule 3's other half: no path is computed for a Wight, because the only route
    /// <c>NavPathSense</c> knows how to measure is one to the player.
    /// </summary>
    /// <remarks>
    /// <b>Measured by contrast rather than by counting.</b> Both senses are sealed, so no fixture
    /// can subclass one to tally its calls — but an EditMode scene has no baked NavMesh, so
    /// <c>DirectionFor</c> answers the straight line towards the player, which is never zero for a
    /// body standing away from them. An enemy at the same offset therefore comes back with a
    /// direction and a Wight comes back with the zero its census wrote, and the difference is the
    /// search having been run for one and not the other.
    /// </remarks>
    [Test]
    public void Minions_HaveNoPathSearch()
    {
        Frame frame = BuildFrame(enemies: 1, wights: 2);

        frame.Builder.Build(frame.Snapshot, 0.02f);

        Assert.That(
            frame.Snapshot.Enemies[0].PathDirectionToPlayer,
            Is.Not.EqualTo(CoreVector2.Zero),
            "Sanity: the builder did run a search for the enemy, so a zero below means something.");

        for (int i = 0; i < frame.Snapshot.MinionCount; i++)
        {
            Assert.That(
                frame.Snapshot.Minions[i].PathDirectionToPlayer,
                Is.EqualTo(CoreVector2.Zero),
                "A route to the player was computed for a Wight — which is not where it is going "
                    + "(M5-04a rule 4), is costed per Wight per frame, and is read by nothing.");
        }
    }

    // ---- What a Wight is not (rules 1, 5) --------------------------------------------------------

    /// <summary>
    /// <b>Rule 1, the row that proves the swing cannot reach one.</b>
    /// </summary>
    /// <remarks>
    /// <c>ConeOverlapQuery.Query</c> turns a physics hit into an enemy id through
    /// <c>EnemyViews.TryGetId</c> and through nothing else. A Wight in that index would have the
    /// player's own swing report <em>its</em> number to <c>RunSession.ReportConeHits</c>, and core
    /// would apply the damage to whichever enemy holds it — a stranger, silently, on the first
    /// swing of the first Gravecaller run.
    /// </remarks>
    [Test]
    public void Minions_AreNotInTheColliderIndex()
    {
        Frame frame = BuildFrame(enemies: 1, wights: 1);

        Assert.That(frame.Minions.TryGet(1, out MinionView wight), Is.True);
        Assert.That(frame.Enemies.TryGet(1, out EnemyView husk), Is.True);

        Assert.That(
            frame.Enemies.TryGetId(husk.Body, out int enemyId),
            Is.True,
            "Sanity: the enemy's collider does resolve, so the index is populated and a false "
                + "below is the Wight's absence rather than an empty dictionary.");

        Assert.That(enemyId, Is.EqualTo(1));

        Assert.That(
            frame.Enemies.TryGetId(wight.Body, out _),
            Is.False,
            "The Wight's collider resolves to an enemy id. The player's next swing will damage "
                + "whichever enemy holds that number instead of the Wight, which is not in "
                + "EnemyRegistry at all.");
    }

    [Test]
    public void Minions_AreNotRentedFromTheEnemyPool()
    {
        Frame frame = BuildFrame(enemies: 1, wights: MinionSystem.MaxConcurrent);

        Assert.That(
            frame.Enemies.Count,
            Is.EqualTo(1),
            "Eight Wights joined the enemy census, which is the second half of rule 1's mistake.");

        Assert.That(frame.Minions.Count, Is.EqualTo(MinionSystem.MaxConcurrent));

        for (int minion = 1; minion <= MinionSystem.MaxConcurrent; minion++)
        {
            Assert.That(frame.Minions.TryGet(minion, out MinionView body), Is.True);

            Assert.That(
                frame.Enemies.TryGet(1, out EnemyView enemy) && ReferenceEquals(enemy.gameObject, body.gameObject),
                Is.False,
                "The two pools handed out the same instance, so one body is standing in for an "
                    + "enemy and a Wight at once.");
        }
    }

    /// <summary>
    /// [CH §8 q1] Rule 5's ruling, as arithmetic: eight Wights and a full mid-tier arena stand
    /// together and neither displaces the other.
    /// </summary>
    /// <remarks>
    /// <b>What this row carries forward is the number.</b> At the shipped caps this build can put
    /// 28 enemies + 8 Wights + the player = <b>37 bodies</b> in one arena, and whether that clears
    /// GD §11.3's draw-call ceiling is a phone's answer, on ledger row 3. What is settled here is
    /// only that the two censuses are independent — a Wight costs none of
    /// <c>ThreatBudget</c>'s spend and cannot push an enemy out of the snapshot.
    /// </remarks>
    [Test]
    public void Minions_CapIsEightAndTheEnemyCapIsUntouched()
    {
        const int MidTierArena = 28;

        Frame frame = BuildFrame(enemies: MidTierArena, wights: MinionSystem.MaxConcurrent);

        frame.Builder.Build(frame.Snapshot, 0.02f);

        Assert.That(frame.Snapshot.EnemyCount, Is.EqualTo(MidTierArena));
        Assert.That(frame.Snapshot.MinionCount, Is.EqualTo(MinionSystem.MaxConcurrent));

        Assert.That(
            frame.Snapshot.MinionCapacity,
            Is.EqualTo(MinionSystem.MaxConcurrent),
            "The army's own ceiling is the system's, sized from the constant so the two cannot "
                + "disagree the day a Legion node moves the cap (M5-04a rule 3).");
    }

    /// <summary>
    /// The boundary sweep core does not announce. [M5-04b's finding, ledger row 9]
    /// </summary>
    /// <remarks>
    /// <c>StageFlow.Advance</c> calls <c>MinionSystem.Clear</c>, which publishes nothing —
    /// <c>EnemySystem.Clear</c>'s silence, for its reason. Without this the whole army stands in
    /// the <em>next</em> arena for the rest of the run: the pool is empty so the next raise
    /// instantiates, and the census's entry for id 1 is overwritten by the next stage's first
    /// Wight, which leaks that body out of the index entirely.
    /// </remarks>
    [Test]
    public void Minions_AreSweptAtAStageBoundary()
    {
        _views = Census(prewarm: MinionSystem.MaxConcurrent);

        for (int i = 1; i <= MinionSystem.MaxConcurrent; i++)
        {
            Raise(i, new CoreVector3(i, 0f, 0f));
        }

        Assert.That(_views.Count, Is.EqualTo(MinionSystem.MaxConcurrent));

        _hub.Publish(new StageArrived(2, new ContentId("arena.pillars")));

        Assert.That(_views.Count, Is.Zero, "A Wight followed the player into the next arena.");

        Assert.That(
            _views.PooledCount,
            Is.EqualTo(MinionSystem.MaxConcurrent),
            "And every body went back to the pool rather than being leaked out of the census.");

        // Core's ids go back to 1 with the bodies, so the next stage's first raise asks for a
        // number the old army was using. It resolves to a fresh rental rather than to a ghost.
        Raise(id: 1, at: new CoreVector3(5f, 0f, 5f));

        Assert.That(_views.Count, Is.EqualTo(1));
        Assert.That(_views.TryGet(1, out MinionView view), Is.True);
        Assert.That(view.Position.x, Is.EqualTo(5f).Within(1e-4f));
    }

    // ---- The shipped asset (rule 8) ---------------------------------------------------------------

    /// <summary>
    /// [Traps §5] The row every authored prefab owes: the component resolves rather than
    /// deserialising as null.
    /// </summary>
    /// <remarks>
    /// A <c>MonoBehaviour</c> in a file-scoped namespace cannot be found by Unity 6.3's script
    /// importer, and every asset referencing it loads as null with nothing reported anywhere. The
    /// failure is a Wight prefab that rents, binds and walks perfectly and has no behaviour on it
    /// at all.
    /// </remarks>
    [Test]
    public void Minions_AreLinkedToAMonoScript()
    {
        GameObject prefab = LoadWight();

        Assert.That(
            prefab.GetComponent<MinionView>(),
            Is.Not.Null,
            $"{WightPrefabPath} has no MinionView on it. If the component is on the prefab in the "
                + "Inspector, the script reference has come back null — check the namespace is a "
                + "block one (Traps §5).");
    }

    /// <summary>
    /// [Rule 8, GD §16.4, CH §3.2] A Wight is the player's own cyan, and smaller than every
    /// archetype.
    /// </summary>
    /// <remarks>
    /// <b>The collision with the player's colour is the point, and the separator is size.</b> GD
    /// §16.4 reserves <c>#22D3EE</c> for the player and the safe things around them, and CH §3.2
    /// asks for Wights that are unmistakably the player's at phone scale — so hue cannot also say
    /// <em>which</em> cyan thing is the player, and 0.7 is what does. Pinned here so a recolour or
    /// a resize is a deliberate edit with a red row under it rather than a drift.
    /// </remarks>
    [Test]
    public void Minions_AreTintedThePlayersCyan()
    {
        GameObject prefab = LoadWight();

        var view = prefab.GetComponent<MinionView>();

        Assert.That(view.Tint, Is.EqualTo(Palette.Player), "GD §16.4's player cyan, and nothing near it.");

        float scale = prefab.transform.localScale.x;

        Assert.That(
            prefab.transform.localScale,
            Is.EqualTo(new Vector3(scale, scale, scale)),
            "A Wight is scaled uniformly; a body squashed on one axis is a different silhouette.");

        foreach ((string archetype, float bodyScale) in ShippedBodyScales())
        {
            Assert.That(
                scale,
                Is.LessThan(bodyScale),
                $"A Wight is not smaller than the {archetype}, so at phone scale the only thing "
                    + "separating the player's army from the swarm is a hue the player already "
                    + "owns (CH §3.2's watch item).");
        }
    }

    // ---- Guards -------------------------------------------------------------------------------

    [Test]
    public void Constructor_NullDependency_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MinionViews(null, _prefab, null, _hub));
        Assert.Throws<ArgumentNullException>(() => new MinionViews(_container, null, null, _hub));
        Assert.Throws<ArgumentNullException>(() => new MinionViews(_container, _prefab, null, null));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MinionViews(_container, _prefab, null, _hub, prewarm: -1));
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
            () => _views = new MinionViews(_container, _prefab, held, _hub, prewarm: 1),
            Throws.Nothing);

        Assert.That(_views.PooledCount, Is.EqualTo(1));
    }

    [Test]
    public void Bind_InvalidId_Throws()
    {
        MinionView view = Template<MinionView>("Loose");

        // Ids are issued from 1, so a zero or negative one means the caller invented it — and a
        // body bound to Unbound would report itself into the snapshot under an id core can never
        // resolve, which core ignores in silence.
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Bind(MinionView.Unbound, Vector3.zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Bind(-1, Vector3.zero));
    }

    [Test]
    public void Apply_NonFiniteDt_MovesNothing()
    {
        MinionView view = Template<MinionView>("Loose");

        view.transform.position = new Vector3(2f, 0f, 2f);
        view.Bind(1, new Vector3(2f, 0f, 2f));

        var intent = new EnemyMoveIntent(1, new CoreVector3(4f, 0f, 0f), new CoreVector2(1f, 0f));

        // Refused rather than thrown on: a bad frame time is the run's problem to report, and the
        // body's job is to not become a permanent NaN because of it. Nothing moves in EditMode
        // anyway — Awake never ran, so the controller is unwired — which is why the row asserts the
        // position is unchanged rather than that the guard was reached.
        Assert.That(() => view.Apply(intent, float.NaN), Throws.Nothing);
        Assert.That(() => view.Apply(intent, float.PositiveInfinity), Throws.Nothing);
        Assert.That(() => view.Apply(intent, -0.5f), Throws.Nothing);

        Assert.That(view.transform.position, Is.EqualTo(new Vector3(2f, 0f, 2f)));
        Assert.That(view.Velocity, Is.EqualTo(Vector3.zero), "And a refused step wrote no velocity either.");
    }

    [Test]
    public void Views_DisposeDestroysEveryBody()
    {
        _views = Census(prewarm: MinionSystem.MaxConcurrent);

        var standing = new List<MinionView>();

        for (int i = 1; i <= MinionSystem.MaxConcurrent; i++)
        {
            Raise(i, new CoreVector3(i, 0f, 0f));

            Assert.That(_views.TryGet(i, out MinionView view), Is.True);

            standing.Add(view);
        }

        // Four back in the pool, four still in service: Dispose owns both halves, so a caller
        // holding rented bodies does not have to return them first.
        for (int i = 1; i <= 4; i++)
        {
            _hub.Publish(new MinionDespawned(i));
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

        // The subscriptions are gone, so a Wight raised by a run this object has outlived reaches
        // nothing — and cannot ask a disposed pool for a body.
        Assert.That(() => Raise(id: 99, at: CoreVector3.Zero), Throws.Nothing);
        Assert.That(_views.Count, Is.Zero);

        // Idempotent, like EnemyViews' — a double dispose is a disposal-order question nobody
        // should have to answer.
        Assert.That(() => _views.Dispose(), Throws.Nothing);
    }

    // ---- Fixture ------------------------------------------------------------------------------

    /// <summary>A census over the fixture's prefab, hub and container.</summary>
    private MinionViews Census(int prewarm) =>
        new MinionViews(_container, _prefab, null, _hub, prewarm);

    private void Raise(int id, CoreVector3 at) =>
        _hub.Publish(new MinionSpawned(id, WightId, at, lifespan: 20f));

    /// <summary>
    /// A real frame: a player, both censuses, the two derived senses, and a builder wired to all of
    /// them — with <paramref name="enemies"/> standing near the origin and <paramref name="wights"/>
    /// well behind them.
    /// </summary>
    /// <remarks>
    /// The two populations are separated along +Z so that
    /// <see cref="Minions_AreNotInTheEnemySlots"/> can tell them apart by position rather than by
    /// trusting the index it is testing.
    /// </remarks>
    private Frame BuildFrame(int enemies, int wights)
    {
        GameObject playerObject = Keep(new GameObject("Player"));
        var player = playerObject.AddComponent<PlayerView>();

        var enemyViews = new EnemyViews(
            _container,
            Template<EnemyView>("EnemyTemplate"),
            null,
            _hub,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()),
            prewarm: enemies);

        _views = Census(prewarm: wights);

        var paths = new NavPathSense(64);

        var sight = new LineOfSightSense(
            64,
            1 << LayerMask.NameToLayer(ArenaView.CoverLayerName),
            new PathRefreshBudget(
                LineOfSightSense.DefaultRefreshHz,
                PathRefreshBudget.DefaultMaxPerFrame),
            LineOfSightSense.DefaultRefreshHz);

        var input = new InputAdapter();

        _inputs.Add(input);
        _censuses.Add(enemyViews);

        for (int i = 1; i <= enemies; i++)
        {
            _hub.Publish(new EnemySpawned(i, HuskId, new CoreVector3(i, 0f, 0f)));
        }

        for (int i = 1; i <= wights; i++)
        {
            Raise(i, new CoreVector3(i, 0f, 20f));
        }

        return new Frame(
            new SnapshotBuilder(player, input, enemyViews, _views, paths, null, sight),
            new WorldSnapshot(64),
            enemyViews,
            _views,
            sight);
    }

    /// <summary>What <see cref="BuildFrame"/> hands back: one frame's worth of wiring.</summary>
    private readonly struct Frame
    {
        public Frame(
            SnapshotBuilder builder,
            WorldSnapshot snapshot,
            EnemyViews enemies,
            MinionViews minions,
            LineOfSightSense sight)
        {
            Builder = builder;
            Snapshot = snapshot;
            Enemies = enemies;
            Minions = minions;
            Sight = sight;
        }

        public SnapshotBuilder Builder { get; }

        public WorldSnapshot Snapshot { get; }

        public EnemyViews Enemies { get; }

        public MinionViews Minions { get; }

        public LineOfSightSense Sight { get; }
    }

    private static GameObject LoadWight()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WightPrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {WightPrefabPath}.");

        return prefab;
    }

    /// <summary>
    /// Every shipped archetype's authored body scale, read off the assets rather than quoted.
    /// </summary>
    /// <remarks>
    /// Read from the definitions so that a retuned Bloater — or a new archetype smaller than any of
    /// today's — is what reddens the row, rather than a list here quietly going out of date.
    /// </remarks>
    private static IEnumerable<(string Archetype, float BodyScale)> ShippedBodyScales()
    {
        string[] guids = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { EnemyDefinitionFolder });

        Assert.That(guids.Length, Is.GreaterThan(0), $"No EnemyDefinition under {EnemyDefinitionFolder}.");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);

            if (definition == null)
            {
                continue;
            }

            var serialized = new SerializedObject(definition);
            SerializedProperty scale = serialized.FindProperty("_bodyScale");

            if (scale is null)
            {
                continue;
            }

            yield return (definition.name, scale.floatValue);
        }
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

        Keep(root);

        return root.AddComponent<T>();
    }

    private GameObject Keep(GameObject o)
    {
        _created.Add(o);

        return o;
    }
}
