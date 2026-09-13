using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// The senses, measured. Every row is a fact core cannot check for itself: if the builder copies
/// the wrong number, or the same number twice, core is simply wrong about the world and nothing
/// downstream can tell.
/// </summary>
/// <remarks>
/// <para>
/// Runs on <see cref="InputTestFixture"/>, which swaps the whole Input System for an isolated one
/// per test — the same arrangement <see cref="InputAdapterTests"/> uses, and for the same reason:
/// the stick has to be settable without touching the Editor's real input state. Deadzones are
/// neutralised in setup so a stick set to (0.3, 0.6) reads back as (0.3, 0.6) rather than as
/// <c>StickDeadzone</c>'s rescaled curve.
/// </para>
/// <para>
/// Every vector is fully qualified, as in <see cref="NumTests"/>: with a <c>using</c> for either
/// vector namespace, a boundary crossing and an identity look the same on the page.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SnapshotBuilderTests : InputTestFixture
{
    private static readonly ContentId HuskId = new ContentId("enemy.husk");

    private GameObject _playerObject;
    private PlayerView _player;
    private Gamepad _gamepad;
    private InputAdapter _input;
    private SnapshotBuilder _builder;
    private WorldSnapshot _snapshot;
    private GameObject _enemyTemplateObject;
    private IObjectResolver _container;
    private DomainEventHub _hub;
    private EnemyViews _enemyViews;
    private NavPathSense _paths;

    /// <summary>The cover raycasts (M2-11b), and the pillar one row puts in the way.</summary>
    private LineOfSightSense _sight;
    private GameObject _pillar;

    /// <summary>The arena prefab this scene raises, and the pool that raises it (M2-11a).</summary>
    private GameObject _arenaTemplateObject;
    private ArenaPool _arenas;

    [SetUp]
    public void CreateBuilder()
    {
        InputSystem.settings.defaultDeadzoneMin = 0f;
        InputSystem.settings.defaultDeadzoneMax = 1f;
        _gamepad = InputSystem.AddDevice<Gamepad>();

        // AddComponent brings the CharacterController with it, per PlayerView's [RequireComponent].
        // Nothing here calls Apply: outside play mode Awake never runs, so the controller the body
        // would move is not wired — which is exactly why the spec leaves Apply to the manual steps.
        _playerObject = new GameObject("Player");
        _player = _playerObject.AddComponent<PlayerView>();

        // A scene object standing in for the prefab, which is all VContainer's Instantiate needs —
        // it takes a Component, not an asset. AddComponent brings the CapsuleCollider with it, per
        // EnemyView's [RequireComponent]; Awake still never runs in edit mode, so the cached Body
        // stays null and EnemyViews' collider index stays empty. That is the one thing about these
        // views an EditMode test cannot reach, and M1-12 is where it starts to matter.
        _enemyTemplateObject = new GameObject("EnemyTemplate");
        EnemyView template = _enemyTemplateObject.AddComponent<EnemyView>();

        // An empty container is enough: Instantiate injects the new object, and nothing on an
        // EnemyView asks to be injected. A real run's resolver differs only in what it holds.
        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();
        _enemyViews = new EnemyViews(_container, template, null, _hub, EmptyLookBook());

        _input = new InputAdapter();

        // A real path cache rather than null, so what these rows exercise is the wiring a run
        // actually has. An EditMode scene has no baked NavMesh, so every search fails and the sense
        // answers with the straight line — which is the guarantee that made M1 playable for
        // eighteen tasks before pathing existed, and worth having a row stand on.
        _paths = new NavPathSense(8);

        // The arena, and the pool that raises it (M2-11a). Where the door is and where a body may
        // be put are both facts about whichever room is standing, so the builder asks the pool
        // rather than holding a transform the scene dressed once.
        _arenaTemplateObject = BuildArena("arena.test", new UnityEngine.Vector3(0f, 0f, 18f));
        _arenas = new ArenaPool(
            _container,
            new[] { _arenaTemplateObject.GetComponent<ArenaView>() },
            null,
            _hub,
            _player);

        // A real cover sense rather than null, for the reason the path cache is real: what these
        // rows exercise is the wiring a run actually has. Nothing is on the Cover layer unless a row
        // puts it there, so every enemy can see and the arena behaves as it did before M2-11b.
        _sight = new LineOfSightSense(
            8,
            1 << LayerMask.NameToLayer(ArenaView.CoverLayerName),
            new PathRefreshBudget(
                LineOfSightSense.DefaultRefreshHz,
                PathRefreshBudget.DefaultMaxPerFrame),
            LineOfSightSense.DefaultRefreshHz);

        _builder = new SnapshotBuilder(_player, _input, _enemyViews, _paths, _arenas, _sight);
        _snapshot = new WorldSnapshot(8);
    }

    [TearDown]
    public void DestroyBuilder()
    {
        // Views before the hub: disposing the views unsubscribes them, and disposing the hub first
        // would leave that dispose pointing at an orphaned channel — harmless here, and the wrong
        // order to write down anywhere.
        _enemyViews?.Dispose();
        _enemyViews = null;

        // With the views and for their reason: the pool holds two subscriptions, and disposing the
        // hub first would leave them pointing at an orphaned channel.
        _arenas?.Dispose();
        _arenas = null;

        if (_arenaTemplateObject != null)
        {
            Object.DestroyImmediate(_arenaTemplateObject);
        }

        _arenaTemplateObject = null;

        if (_pillar != null)
        {
            Object.DestroyImmediate(_pillar);
        }

        _pillar = null;
        _sight = null;

        _hub?.Dispose();
        _hub = null;

        _container?.Dispose();
        _container = null;

        _input?.Dispose();
        _input = null;

        if (_playerObject != null)
        {
            // DestroyImmediate, not Destroy: in edit mode the latter destroys nothing and logs an
            // error, which would leak the object and redden the test (M0-14).
            Object.DestroyImmediate(_playerObject);
        }

        _playerObject = null;

        if (_enemyTemplateObject != null)
        {
            Object.DestroyImmediate(_enemyTemplateObject);
        }

        _enemyTemplateObject = null;

    }

    [Test]
    public void Build_ReportsTheStandingArenasGate()
    {
        Raise("arena.test");

        _builder.Build(_snapshot, 0.02f);

        Assert.That(_snapshot.HasGate, Is.True);
        Assert.That(_snapshot.GatePosition.Z, Is.EqualTo(18f));

        // Read every frame rather than cached at composition, so an arena that moves its door — one
        // that slides open, one raised at a stage boundary — is answered on the frame it moves.
        _arenas.Active.transform.Find("Gate").position = new UnityEngine.Vector3(4f, 0f, 1f);

        _builder.Build(_snapshot, 0.02f);

        Assert.That(_snapshot.GatePosition.X, Is.EqualTo(4f));
        Assert.That(_snapshot.GatePosition.Z, Is.EqualTo(1f));
    }

    [Test]
    public void Build_ReportsTheStandingArenasSpawnPoints()
    {
        Raise("arena.test");

        _builder.Build(_snapshot, 0.02f);

        Assert.That(_snapshot.SpawnPoints.Count, Is.EqualTo(3),
            "Where a body may be put is a fact about the room that is standing, and it reaches " +
            "core the same way the door does (M2-11a rule 6).");

        Assert.That(_snapshot.SpawnPoints[0].X, Is.EqualTo(9f));
    }

    [Test]
    public void Build_NoArenaRaised_SaysSo()
    {
        // Nothing has arrived, so nothing is standing. An undressed scene is a legal arena: core
        // reads HasGate false, sees nowhere to spawn, and parks its stage flow rather than
        // throwing (M2-10 rule 15, M2-05 rule 12).
        _builder.Build(_snapshot, 0.02f);

        Assert.That(_snapshot.HasGate, Is.False);
        Assert.That(_snapshot.GatePosition, Is.EqualTo(System.Numerics.Vector3.Zero),
            "And the position is cleared with it, so a stale door cannot be walked through.");

        Assert.That(_snapshot.SpawnPoints.Count, Is.Zero);
    }

    [Test]
    public void Build_NoPool_SaysSo()
    {
        // A builder composed without a pool at all — a fixture rather than a run, and the same
        // bargain a null NavPathSense makes. The cover sense goes with it, which is the M2-11b half
        // of the same row: an arena with no geometry to raycast is one where everybody can see.
        var builder = new SnapshotBuilder(_player, _input, _enemyViews, _paths, null, null);

        _hub.Publish(new EnemySpawned(1, HuskId, new System.Numerics.Vector3(0f, 0f, 4f)));

        Assert.DoesNotThrow(() => builder.Build(_snapshot, 0.02f));

        Assert.That(_snapshot.HasGate, Is.False);
        Assert.That(_snapshot.SpawnPoints.Count, Is.Zero);

        Assert.That(FindEnemy(1).HasLineOfSight, Is.True,
            "A null sense answers `can see`, never `cannot` — a mis-wired run degrades to the "
                + "arena M2-07b shipped rather than to one where no Spitter ever fires (rule 3).");
    }

    [Test]
    public void Build_DestroyedArena_SaysSo()
    {
        Raise("arena.test");

        // Unity's lifetime check rather than C#'s: a destroyed component is a live C# reference and
        // a dead object, and a plain null comparison would report a door that is not there.
        Object.DestroyImmediate(_arenas.Active.gameObject);

        _builder.Build(_snapshot, 0.02f);

        Assert.That(_snapshot.HasGate, Is.False);
    }

    [Test]
    public void Build_CopiesPlayerPositionAndVelocity()
    {
        _playerObject.transform.position = new UnityEngine.Vector3(3f, 0f, -2f);

        _builder.Build(_snapshot, 0.02f);

        Assert.That(_snapshot.PlayerPosition.X, Is.EqualTo(3f));
        Assert.That(_snapshot.PlayerPosition.Y, Is.EqualTo(0f));
        Assert.That(_snapshot.PlayerPosition.Z, Is.EqualTo(-2f),
            "Z must come from Z — a component swapped at the boundary is a game that walks sideways.");

        Assert.That(_snapshot.Dt, Is.EqualTo(0.02f));
        Assert.That(_snapshot.EnemyCount, Is.Zero, "Nothing has been spawned, so there is nothing to report.");

        // Honesty note, asserted rather than left as a comment: a view that has never applied an
        // intent really does report zero velocity, so this line cannot distinguish a copied zero
        // from a hardcoded one. Making it non-zero needs Apply, which needs Awake, which needs play
        // mode. The value is verified on the device instead (manual step 4, |v| = 5.4); what this
        // row pins is that the field is written at all and that nothing else leaks into it.
        Assert.That(_snapshot.PlayerVelocity, Is.EqualTo(System.Numerics.Vector3.Zero));

        // The adapter was never enabled, so the stick reads zero — which makes the non-zero
        // reading in Build_CopiesMoveInput the thing that proves the wire.
        Assert.That(_snapshot.MoveInput, Is.EqualTo(System.Numerics.Vector2.Zero));
    }

    [Test]
    public void Build_CopiesMoveInput()
    {
        _input.Enable();

        Set(_gamepad.leftStick, new UnityEngine.Vector2(0.3f, 0.6f));

        // Load-bearing: a Value action picks up an already-deflected control through an initial
        // state check that runs on the update *after* the enable (M0-14).
        InputSystem.Update();

        _builder.Build(_snapshot, 0.016f);

        // Stick X → world X, stick Y → world Z. The snapshot carries the pair unrotated; the
        // mapping into world axes is the motor's, and turning it into a camera-relative one is
        // this builder's job the day the camera can yaw.
        Assert.That(_snapshot.MoveInput.X, Is.EqualTo(0.3f).Within(1e-3f));
        Assert.That(_snapshot.MoveInput.Y, Is.EqualTo(0.6f).Within(1e-3f));
    }

    [Test]
    public void Build_ClearsPreviousEnemies()
    {
        // `ref` on both sides, deliberately: bound to a copy, the fill would write to a temporary
        // and the count below would be the only thing this row was really testing (M0-05).
        ref EnemySense first = ref _snapshot.AddEnemy();
        first.Id = 1;
        ref EnemySense second = ref _snapshot.AddEnemy();
        second.Id = 2;
        ref EnemySense third = ref _snapshot.AddEnemy();
        third.Id = 3;

        Assert.That(_snapshot.EnemyCount, Is.EqualTo(3), "Sanity: there is something to clear.");

        _builder.Build(_snapshot, 0.016f);

        // Last frame's enemies are still sitting in the array — Clear does not zero it — and the
        // count is the only thing that keeps core from reading them. If the builder ever stops
        // clearing, every dead enemy stays alive to core forever.
        Assert.That(_snapshot.EnemyCount, Is.Zero);
        Assert.That(_snapshot.Enemies[0].Id, Is.EqualTo(1),
            "Clear leaves the slots alone by design; only the count moves.");
    }

    [Test]
    public void Build_CopiesEnemiesFromViews()
    {
        // Published through the hub rather than by calling EnemyViews directly, because the
        // subscription is half of what M1-07 wired: core announces a spawn and a body appears.
        _hub.Publish(new EnemySpawned(1, HuskId, new System.Numerics.Vector3(4f, 0f, -6f)));
        _hub.Publish(new EnemySpawned(2, HuskId, new System.Numerics.Vector3(-3f, 0f, 5f)));

        Assert.That(_enemyViews.Count, Is.EqualTo(2), "Sanity: two bodies exist to report.");

        _builder.Build(_snapshot, 0.016f);

        Assert.That(_snapshot.EnemyCount, Is.EqualTo(2));

        // By id, never by slot: the order these were written in is the dictionary's, and nothing
        // downstream reads it — core's Ingest looks every entry up by id. A row that asserted
        // Enemies[0].Id == 1 would be pinning an implementation detail that is free to change.
        Assert.That(FindEnemy(1).Position.X, Is.EqualTo(4f));
        Assert.That(FindEnemy(1).Position.Z, Is.EqualTo(-6f),
            "Z from Z — a component swapped here puts every enemy somewhere core cannot reach.");
        Assert.That(FindEnemy(2).Position.X, Is.EqualTo(-3f));
        Assert.That(FindEnemy(2).Position.Z, Is.EqualTo(5f));
    }

    [Test]
    public void Build_OverwritesEveryEnemyFieldOfAReusedSlot()
    {
        // A slot with a previous occupant's senses still in it. Clear() does not zero the array
        // (AR §4.2), so any field CopyInto forgets to assign is silently inherited — and a stale
        // HasLineOfSight belonging to an enemy that despawned two frames ago is the kind of fault
        // that reads as an AI bug for a week.
        ref EnemySense stale = ref _snapshot.AddEnemy();
        stale.Id = 99;
        stale.PathDirectionToPlayer = new System.Numerics.Vector2(0.6f, 0.8f);
        stale.Velocity = new System.Numerics.Vector3(9f, 9f, 9f);

        // Stale *false* since M2-11b, which is the way round that has teeth: `true` is now the
        // answer an enemy on open ground gets, so a leftover `true` would be indistinguishable from
        // a correct one and the row would pass with the assignment deleted.
        stale.HasLineOfSight = false;

        _hub.Publish(new EnemySpawned(1, HuskId, new System.Numerics.Vector3(1f, 0f, 2f)));

        _builder.Build(_snapshot, 0.016f);

        Assert.That(_snapshot.EnemyCount, Is.EqualTo(1));

        EnemySense written = _snapshot.Enemies[0];

        Assert.That(written.Id, Is.EqualTo(1));
        Assert.That(written.HasLineOfSight, Is.True,
            "Nothing is on the Cover layer, so this enemy can see — and it has to be *written* "
                + "that way rather than inherited. EnemyViews stopped writing the field at M2-11b, "
                + "so this assignment is the only one a reused slot gets (AR §18.2).");
        Assert.That(written.Velocity, Is.EqualTo(System.Numerics.Vector3.Zero),
            "An EnemyView reports zero velocity until M1-18 moves it — zero written, not inherited.");

        // The stale (0.6, 0.8) is gone, replaced by a direction from this enemy to this player.
        // With no NavMesh in an EditMode scene the search fails and NavPathSense answers with the
        // straight line: the body is at (1, 0, 2), the player is at the origin, so the unit XZ
        // direction is (−1, −2) normalised.
        System.Numerics.Vector2 path = written.PathDirectionToPlayer;

        Assert.That(path.Length(), Is.EqualTo(1f).Within(1e-4f));
        Assert.That(path.X, Is.EqualTo(-0.4472136f).Within(1e-4f));
        Assert.That(path.Y, Is.EqualTo(-0.8944272f).Within(1e-4f),
            "Y of the XZ direction is world Z. A component swapped here sends every enemy in the "
                + "game sideways.");
    }

    [Test]
    public void Snapshot_CarriesLineOfSight()
    {
        // M2-11b, from this side of the boundary: the pillar is real, the raycast is real, and what
        // reaches core is one bool on the slot beside the path direction. The pair with the row
        // above is what proves the wiring rather than the sense — that one has open ground and
        // arrives `true`, this one has cover and arrives `false`, and neither can pass if
        // EnemyViews is still writing a hard answer of its own.
        _hub.Publish(new EnemySpawned(1, HuskId, new System.Numerics.Vector3(0f, 0f, 10f)));

        // Halfway between the player at the origin and the body at ten metres, 1.5 m tall so it
        // stands across the 1.1 m the ray runs at.
        _pillar = new GameObject("Pillar")
        {
            layer = LayerMask.NameToLayer(ArenaView.CoverLayerName),
        };

        _pillar.transform.position = new UnityEngine.Vector3(0f, 0.75f, 5f);
        _pillar.AddComponent<BoxCollider>().size = new UnityEngine.Vector3(8f, 1.5f, 0.5f);

        Physics.SyncTransforms();

        _builder.Build(_snapshot, 0.016f);

        Assert.That(
            FindEnemy(1).HasLineOfSight,
            Is.False,
            "A pillar between the body and the player has to reach core as a sense, because core "
                + "holds no walls and never will (AR §18.2, ledger row 13).");
    }

    [Test]
    public void Build_ClampsDt()
    {
        // A hitch, a scene load or a phone waking up. Unclamped, core would advance the player
        // 1.6 m in one Move and through whatever was in the way.
        _builder.Build(_snapshot, 0.3f);

        Assert.That(_snapshot.Dt, Is.EqualTo(SnapshotBuilder.MaxDt));
        Assert.That(_snapshot.Dt, Is.EqualTo(0.05f), "50 ms, the spec's floor of 20 fps.");

        // A normal frame passes through untouched — the clamp is a ceiling, not a quantiser.
        _builder.Build(_snapshot, 0.016f);

        Assert.That(_snapshot.Dt, Is.EqualTo(0.016f));
    }

    [Test]
    public void Build_AllocatesNothing()
    {
        // Enabled and deflected on purpose: with the adapter disabled, Move returns early and the
        // measurement would never touch the Input System read that is the only plausible allocator
        // in this path.
        _input.Enable();
        Set(_gamepad.leftStick, new UnityEngine.Vector2(0.3f, 0.6f));
        InputSystem.Update();

        // Two bodies in the census, deliberately: an empty dictionary would skip the loop body
        // entirely and the row would prove nothing about CopyInto. The concrete dictionary's value
        // enumerator is a struct, so walking it must cost nothing per frame.
        _hub.Publish(new EnemySpawned(1, HuskId, new System.Numerics.Vector3(4f, 0f, -6f)));
        _hub.Publish(new EnemySpawned(2, HuskId, new System.Numerics.Vector3(-3f, 0f, 5f)));

        AllocationAssert.None(() => _builder.Build(_snapshot, 0.016f));
    }

    /// <summary>
    /// The snapshot entry for <paramref name="id"/>, or a failed assertion naming it.
    /// </summary>
    private EnemySense FindEnemy(int id)
    {
        // Stops at EnemyCount, never at Enemies.Length: anything past the count is last frame's.
        for (int i = 0; i < _snapshot.EnemyCount; i++)
        {
            if (_snapshot.Enemies[i].Id == id)
            {
                return _snapshot.Enemies[i];
            }
        }

        Assert.Fail($"No enemy with id {id} in the snapshot.");
        return default;
    }

    /// <summary>
    /// A look book with nothing in it, which is all these rows need: every archetype falls back to
    /// <c>EnemyLook.Default</c> and the bodies here carry no <c>EnemyHitFeedback</c> to apply it to.
    /// Required rather than optional on <c>EnemyViews</c> (M2-06), so it is passed rather than
    /// omitted.
    /// </summary>
    private static EnemyLookBook EmptyLookBook()
        => new EnemyLookBook(new Dictionary<ContentId, EnemyLook>());

    /// <summary>Raises an arena the way a run does — by arriving at a stage in it.</summary>
    private void Raise(string arenaId) =>
        _hub.Publish(new StageArrived(1, new ContentId(arenaId)));

    /// <summary>
    /// An arena that passes its own validation: a start, a door, three points well clear of the
    /// start, three pillars on the <c>Cover</c> layer, and a surface with data on it.
    /// </summary>
    /// <remarks>
    /// Dressed in full rather than to the minimum, because <c>ArenaView.OnValidate</c> warns about
    /// a half-authored arena and a fixture has no business producing that warning. The NavMesh data
    /// is an empty instance: what is being asserted is that the surface <em>has</em> some, which is
    /// the thing a designer forgets — baking one here would be measuring Unity's navigation build.
    /// </remarks>
    private static GameObject BuildArena(string id, UnityEngine.Vector3 gate)
    {
        var root = new GameObject(id);

        var start = new GameObject("PlayerStart");
        start.transform.SetParent(root.transform, false);

        var door = new GameObject("Gate");
        door.transform.SetParent(root.transform, false);
        door.transform.localPosition = gate;

        var points = new Transform[3];

        var offsets = new[]
        {
            new UnityEngine.Vector3(9f, 0f, 0f),
            new UnityEngine.Vector3(0f, 0f, 9f),
            new UnityEngine.Vector3(-9f, 0f, 0f),
        };

        for (int i = 0; i < offsets.Length; i++)
        {
            var marker = new GameObject($"Spawn_{i}");
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = offsets[i];
            points[i] = marker.transform;

            var pillar = new GameObject($"Pillar_{i}")
            {
                layer = LayerMask.NameToLayer(ArenaView.CoverLayerName),
            };

            pillar.transform.SetParent(root.transform, false);
        }

        var surface = root.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
        surface.navMeshData = new UnityEngine.AI.NavMeshData();

        ArenaView view = root.AddComponent<ArenaView>();

        var serialized = new UnityEditor.SerializedObject(view);

        serialized.FindProperty("_id").stringValue = id;
        serialized.FindProperty("_playerStart").objectReferenceValue = start.transform;
        serialized.FindProperty("_gate").objectReferenceValue = door.transform;
        serialized.FindProperty("_surface").objectReferenceValue = surface;

        UnityEditor.SerializedProperty array = serialized.FindProperty("_spawnPoints");

        array.arraySize = points.Length;

        for (int i = 0; i < points.Length; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();

        return root;
    }
}
