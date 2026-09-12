using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Views;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using VContainer;

namespace Soulvail.Tests.Game.Arena;

/// <summary>
/// Which arena is standing, when the next one is built, and what is true of the one that is not.
/// </summary>
/// <remarks>
/// <para>
/// <b>Against the shipped prefabs, not against fixtures.</b> Two of these rows are about Unity's
/// own behaviour rather than about this code — that a body instantiated under a deactivated parent
/// never adds its NavMesh, and that raising it does — and a synthetic arena with an empty
/// <c>NavMeshData</c> would pass both of them vacuously. The prefabs are also what the owner's
/// manual steps walk through, so a row that broke against one of them is a row worth having.
/// </para>
/// <para>
/// EditMode rather than PlayMode, which is unusual for something that activates GameObjects and
/// asks the navigation system questions. It works because <c>NavMeshSurface</c> is an
/// <c>[ExecuteAlways]</c> component: its data goes in when the object is enabled and comes out when
/// it is disabled, in the Editor exactly as in a build. Nothing here needs a frame.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ArenaPoolTests
{
    private const string PillarsPath = "Assets/_Project/Prefabs/Arenas/Arena_Pillars.prefab";
    private const string TieredPath = "Assets/_Project/Prefabs/Arenas/Arena_Tiered.prefab";

    private static readonly ContentId Pillars = new ContentId("arena.pillars");
    private static readonly ContentId Tiered = new ContentId("arena.tiered");

    /// <summary>A place only the tiered arena's NavMesh covers: the top of its dais.</summary>
    private static readonly Vector3 OnTheDais = new Vector3(0f, 0.6f, 0f);

    private IObjectResolver _container;
    private DomainEventHub _hub;
    private GameObject _playerObject;
    private PlayerView _player;
    private GameObject _parent;
    private ArenaPool _pool;

    [SetUp]
    public void CreatePool()
    {
        // An empty container is enough: Instantiate injects the new object, and nothing on an
        // arena asks to be injected. A real run's resolver differs only in what it holds.
        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();

        _playerObject = new GameObject("Player");
        _player = _playerObject.AddComponent<PlayerView>();

        _parent = new GameObject("Arenas");

        _pool = new ArenaPool(_container, Prefabs(), _parent.transform, _hub, _player);
    }

    [TearDown]
    public void DestroyPool()
    {
        // The pool before the hub: disposing it unsubscribes, and disposing the hub first would
        // leave that pointing at an orphaned channel.
        _pool?.Dispose();
        _pool = null;

        _hub?.Dispose();
        _hub = null;

        _container?.Dispose();
        _container = null;

        DestroyIfLive(_parent);
        _parent = null;

        DestroyIfLive(_playerObject);
        _playerObject = null;
    }

    // ---- Raising one (rule 5, 6, 8) ------------------------------------------------------------

    [Test]
    public void Arrived_RaisesTheArena()
    {
        Arrive(1, Pillars);

        Assert.That(_pool.Active, Is.Not.Null);
        Assert.That(_pool.Active.Id, Is.EqualTo(Pillars));
        Assert.That(_pool.Active.gameObject.activeInHierarchy, Is.True,
            "Raised means standing, lit and navigable — not merely built.");

        Assert.That(_pool.Count, Is.EqualTo(1), "And nothing else was built to do it.");
    }

    [Test]
    public void Arrived_PlacesThePlayer()
    {
        _playerObject.transform.position = new Vector3(9f, 0f, 9f);

        Arrive(1, Pillars);

        Assert.That(_playerObject.transform.position, Is.EqualTo(_pool.Active.PlayerStart),
            "A stage boundary puts the player at the start of the room it opens (rule 6). Core " +
            "holds no player position, so nothing has to be told: it reads the new one off the " +
            "next snapshot.");
    }

    [Test]
    public void Arrived_DeactivatesTheOutgoingArena()
    {
        Arrive(1, Pillars);

        ArenaView pillars = _pool.Active;

        Arrive(2, Tiered);

        Assert.That(pillars.gameObject.activeInHierarchy, Is.False);
        Assert.That(_pool.Active.Id, Is.EqualTo(Tiered));
        Assert.That(_pool.Active.gameObject.activeInHierarchy, Is.True);

        Assert.That(ActiveCount(), Is.EqualTo(1),
            "Exactly one arena stands at a time, which is what makes 'the next stage is not " +
            "visible' true by construction rather than by where the camera is pointed (rule 6).");
    }

    [Test]
    public void Arrived_ReusesAParkedArena()
    {
        Arrive(1, Pillars);

        ArenaView first = _pool.Active;

        Arrive(2, Tiered);
        Arrive(3, Pillars);

        Assert.That(_pool.Active, Is.SameAs(first),
            "GD §7.2's pool of 8-12 makes repeats the normal case over a long run, so an arena is " +
            "built once and raised again (rule 8).");

        Assert.That(_pool.Count, Is.EqualTo(2), "And nothing was instantiated the third time.");
    }

    [Test]
    public void Arrived_SealsIt()
    {
        Arrive(1, Pillars);

        Assert.That(Child(_pool.Active, "Gate/Barrier").activeSelf, Is.True, "Barrier up.");
        Assert.That(Child(_pool.Active, "Gate/Door").activeSelf, Is.False, "Door shut.");
    }

    [Test]
    public void Arrived_UnknownId_Throws()
    {
        // Rule 4's failure, at the Unity end: an arena the mode rosters and the scene cannot raise
        // is a content mistake, and the alternative symptom is a boundary that swaps nothing.
        var thrown = Assert.Throws<KeyNotFoundException>(() => Arrive(1, new ContentId("arena.void")));

        Assert.That(thrown.Message, Does.Contain("arena.void"));
        Assert.That(thrown.Message, Does.Contain("arena.pillars"),
            "And it names what the scene does have, or the fix is a search rather than a read.");
    }

    [Test]
    public void Arrived_NoArenaId_LeavesTheSceneAlone()
    {
        // A mode with no arena roster answers default, and that means "leave the room standing" —
        // every M0 and M1 grey box, and every scene dressed for one experiment (rule 3).
        Arrive(1, default);

        Assert.That(_pool.Active, Is.Null);
        Assert.That(_pool.Count, Is.Zero);
    }

    // ---- Preparing the next one (rule 5) -------------------------------------------------------

    [Test]
    public void Cleared_PreparesTheNextInactive()
    {
        Arrive(1, Pillars);

        ArenaView pillars = _pool.Active;

        Clear(1, Tiered);

        Assert.That(_pool.Count, Is.EqualTo(2), "The next arena exists during the clear beat.");
        Assert.That(_pool.Active, Is.SameAs(pillars),
            "And the one being stood in is still the one standing.");

        Assert.That(ActiveCount(), Is.EqualTo(1));
    }

    [Test]
    public void Cleared_PrepareIsNotRaise()
    {
        Arrive(1, Pillars);

        Assert.That(SamplesOnTheDais(), Is.False, "The tiered arena is not up yet.");

        Clear(1, Tiered);

        // The owner's rendering rule, as the two things that can actually be observed: the body is
        // not in the hierarchy's active set, so nothing of it is drawn or lit, and its NavMesh was
        // never added — which is also what stops two arenas' navigation data sitting on top of each
        // other while the player is still fighting in one of them.
        Assert.That(ActiveCount(), Is.EqualTo(1));
        Assert.That(SamplesOnTheDais(), Is.False,
            "A prepared arena adds no NavMesh, because it is instantiated under a deactivated root " +
            "and never runs an OnEnable (rule 5).");
    }

    [Test]
    public void Arrived_AfterPrepare_RaisesTheSameBody()
    {
        Arrive(1, Pillars);
        Clear(1, Tiered);

        int built = _pool.Count;

        Arrive(2, Tiered);

        Assert.That(_pool.Count, Is.EqualTo(built), "The prepared body is the one that is raised.");
        Assert.That(SamplesOnTheDais(), Is.True,
            "And raising it is what puts its NavMesh in, so the enemies in it can path (rule 7).");
    }

    [Test]
    public void Arrived_WithoutPrepare_StillRaises()
    {
        // No StageCleared first: a run that never had a clear beat — the opening stage of every
        // run — still gets its arena, instantiated on the spot.
        Arrive(2, Tiered);

        Assert.That(_pool.Active.Id, Is.EqualTo(Tiered));
        Assert.That(_pool.Active.gameObject.activeInHierarchy, Is.True);
    }

    [Test]
    public void Cleared_OpensIt()
    {
        Arrive(1, Pillars);

        Clear(1, Tiered);

        Assert.That(Child(_pool.Active, "Gate/Barrier").activeSelf, Is.False, "Barrier down.");
        Assert.That(Child(_pool.Active, "Gate/Door").activeSelf, Is.True, "Door open.");
    }

    [Test]
    public void Cleared_NoNextArena_StillOpens()
    {
        // The final stage of a finite mode names no next arena. The door still opens, because the
        // stage was still cleared.
        Arrive(1, Pillars);

        Clear(1, default);

        Assert.That(Child(_pool.Active, "Gate/Barrier").activeSelf, Is.False);
        Assert.That(_pool.Count, Is.EqualTo(1));
    }

    [Test]
    public void Cleared_BeforeAnyArrival_DoesNothing()
    {
        Assert.DoesNotThrow(() => Clear(1, default));

        Assert.That(_pool.Active, Is.Null);
    }

    // ---- The points core is told about (rule 6) ------------------------------------------------

    [Test]
    public void SpawnPoints_AreTheStandingArenas()
    {
        Arrive(1, Pillars);

        ArenaView pillars = _pool.Active;

        Assert.That(_pool.SpawnPoints.Count, Is.EqualTo(pillars.SpawnPoints.Count));
        Assert.That(_pool.SpawnPoints[0].X, Is.EqualTo(pillars.SpawnPoints[0].x));

        Arrive(2, Tiered);

        Assert.That(_pool.SpawnPoints.Count, Is.EqualTo(_pool.Active.SpawnPoints.Count),
            "They move with the room, which is the whole reason they stopped being a property of " +
            "the run (rule 6).");

        Assert.That(_pool.SpawnPoints.Count, Is.Not.EqualTo(pillars.SpawnPoints.Count),
            "The two shipped arenas author different numbers of them, so this row can fail.");
    }

    [Test]
    public void SpawnPoints_BeforeAnyArrival_AreEmpty()
    {
        Assert.That(_pool.SpawnPoints, Is.Empty);
    }

    // ---- Lifetime (rule 12) --------------------------------------------------------------------

    [Test]
    public void Dispose_DestroysEveryBody()
    {
        Arrive(1, Pillars);
        Clear(1, Tiered);

        ArenaView standing = _pool.Active;

        _pool.Dispose();

        Assert.That(standing == null, Is.True,
            "Unity's lifetime check, not C#'s: a destroyed component is a live reference.");

        Assert.That(_pool.Active, Is.Null);
        Assert.That(_pool.Count, Is.Zero);
        Assert.That(_parent.transform.childCount, Is.Zero, "Including the roots it made.");

        Assert.That(SamplesOnTheDais(), Is.False);

        // And it has stopped listening, so a run whose scope is being torn down cannot be handed
        // another stage on the way out.
        Assert.DoesNotThrow(() => Arrive(2, Tiered));

        Assert.That(_pool.Active, Is.Null);

        _pool = null;
    }

    [Test]
    public void Dispose_Twice_IsSafe()
    {
        Arrive(1, Pillars);

        _pool.Dispose();

        Assert.DoesNotThrow(() => _pool.Dispose());

        _pool = null;
    }

    // ---- Guards --------------------------------------------------------------------------------

    [Test]
    public void Ctor_NullDependency_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ArenaPool(null, Prefabs(), _parent.transform, _hub, _player));

        Assert.Throws<ArgumentNullException>(
            () => new ArenaPool(_container, Prefabs(), _parent.transform, null, _player));

        Assert.Throws<ArgumentNullException>(
            () => new ArenaPool(_container, Prefabs(), _parent.transform, _hub, null),
            "An arena swap that left the player standing where the last room's floor used to be is " +
            "the one failure this class cannot report.");
    }

    [Test]
    public void Ctor_NoPrefabs_IsLegal()
    {
        // The undressed Run scene, which is the fastest iteration loop in the project: there is
        // nothing to raise, so whatever the scene was dressed with stays standing.
        using (var empty = new ArenaPool(_container, Array.Empty<ArenaView>(), null, _hub, _player))
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("arena prefabs"));

            _hub.Publish(new StageArrived(1, Pillars));

            Assert.That(empty.Active, Is.Null);
            Assert.That(empty.Count, Is.Zero);
        }
    }

    [Test]
    public void Ctor_UnassignedPrefab_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new ArenaPool(_container, new ArenaView[1], _parent.transform, _hub, _player),
            "An empty row in RunScope's arena list is a mis-dragged field, and it would surface as " +
            "an arena the mode rosters and the scene cannot raise.");
    }

    [Test]
    public void Ctor_DuplicateId_Throws()
    {
        ArenaView pillars = Prefab(PillarsPath);

        Assert.Throws<ArgumentException>(
            () => new ArenaPool(
                _container, new[] { pillars, pillars }, _parent.transform, _hub, _player));
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private void Arrive(int stage, ContentId arena) =>
        _hub.Publish(new StageArrived(stage, arena));

    private void Clear(int stage, ContentId next) =>
        _hub.Publish(new StageCleared(stage, System.Numerics.Vector3.Zero, next));

    private static IReadOnlyList<ArenaView> Prefabs() =>
        new[] { Prefab(PillarsPath), Prefab(TieredPath) };

    private static ArenaView Prefab(string path)
    {
        var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(go, Is.Not.Null, $"No arena prefab at {path}.");

        return go.GetComponent<ArenaView>();
    }

    /// <summary>How many arena bodies are in the hierarchy's active set, under either root.</summary>
    private int ActiveCount()
    {
        int count = 0;

        foreach (ArenaView view in _parent.GetComponentsInChildren<ArenaView>(includeInactive: true))
        {
            if (view.gameObject.activeInHierarchy)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Whether the navigation system has anything at the top of the tiered arena's dais — which is
    /// true only while that arena is raised, and is how "no NavMesh was added" is observable at all.
    /// </summary>
    private static bool SamplesOnTheDais() =>
        NavMesh.SamplePosition(OnTheDais, out _, 0.5f, NavMesh.AllAreas);

    private static GameObject Child(ArenaView arena, string path)
    {
        Transform child = arena.transform.Find(path);

        Assert.That(child, Is.Not.Null, $"No '{path}' under {arena.name}.");

        return child.gameObject;
    }

    private static void DestroyIfLive(GameObject go)
    {
        if (go != null)
        {
            // DestroyImmediate, not Destroy: in edit mode the latter destroys nothing and logs an
            // error, which would leak the object and redden the test (M0-14).
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
