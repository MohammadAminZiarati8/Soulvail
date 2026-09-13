using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Soulvail.Game.Arena;

/// <summary>
/// The arenas, on the Unity side: one standing at a time, the rest either not yet built or parked
/// inactive. <c>EnemyViews</c>' shape at the scale of a whole room.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a listener, not a chooser.</b> Which arena a stage is fought in is
/// <c>ModeSpec.ArenaFor</c>'s answer, derived from the run's seed and the depth and carried on
/// <see cref="StageArrived"/>; this raises whatever it is told to. That is what makes a resumed run
/// land in the room it left without a byte of saved state.
/// </para>
/// <para>
/// <b>Two events, two jobs, and the split is the owner's rendering rule.</b>
/// <see cref="StageArrived"/> raises the arena and places the player;
/// <see cref="StageCleared"/> opens the door and builds the <em>next</em> arena inactive. Nothing of
/// a stage the player has not reached is ever rendered, lit or ticked: it is instantiated under a
/// deactivated root, so its <c>Awake</c> never runs — which is also what keeps its
/// <c>NavMeshSurface</c> from adding a second set of navigation data on top of the arena being
/// fought in.
/// </para>
/// <para>
/// <b>Every arena stands at the same place and only one is active.</b> That is what makes "the next
/// stage is not visible" true by construction rather than by camera placement, and it is why the
/// swap needs M2-10's fade at all. Laying the arenas out side by side with the door between them was
/// rejected: GD §7.2 forbids the corridor it needs, the fixed 57° camera would clip the wall between
/// them, and two arenas resident and rendered is the cost the swap exists to avoid.
/// </para>
/// <para>
/// Not a <see cref="MonoBehaviour"/>: it has no frame of its own and nothing in a scene should be
/// able to find it. It is registered in <c>RunScope</c> and disposed with the run.
/// </para>
/// </remarks>
public sealed class ArenaPool : IDisposable
{
    private readonly IObjectResolver _resolver;
    private readonly PlayerView _player;

    /// <summary>Arena id → the prefab that carries it. Built once, from the scope's list.</summary>
    private readonly Dictionary<ContentId, ArenaView> _prefabs =
        new Dictionary<ContentId, ArenaView>();

    /// <summary>Arena id → the body raised for it, standing or parked. Never more than one each.</summary>
    private readonly Dictionary<ContentId, ArenaView> _built =
        new Dictionary<ContentId, ArenaView>();

    /// <summary>Where the standing arena lives. Active, always.</summary>
    private readonly Transform _activeRoot;

    /// <summary>
    /// Where every other arena lives. <b>Deactivated, and that is load-bearing rather than tidy:</b>
    /// a body instantiated under an inactive parent never runs <c>Awake</c> or <c>OnEnable</c>, so a
    /// prepared arena's renderers are never enabled and its <c>NavMeshSurface</c> never adds its
    /// data. Instantiating into the scene and deactivating a line later would do both, for a frame,
    /// on top of the arena the player is still fighting in.
    /// </summary>
    private readonly Transform _parkedRoot;

    private readonly IDisposable _arrivedSubscription;
    private readonly IDisposable _clearedSubscription;

    /// <summary>
    /// The standing arena's spawn points in core's own vectors, refilled when one is raised.
    /// </summary>
    /// <remarks>
    /// Converted once per raise rather than once per frame: the snapshot carries this list by
    /// reference every frame (<c>WorldSnapshot.SpawnPoints</c>), and a per-frame conversion of eight
    /// points would be an allocation sixty times a second for numbers that cannot change.
    /// </remarks>
    private System.Numerics.Vector3[] _points = Array.Empty<System.Numerics.Vector3>();

    private ReadOnlyCollection<System.Numerics.Vector3> _pointsView =
        Array.AsReadOnly(Array.Empty<System.Numerics.Vector3>());

    private bool _warnedAboutEmptyRoster;
    private bool _disposed;

    /// <param name="resolver">
    /// The run's container. Arenas are instantiated through it so any <c>[Inject]</c> on a prefab's
    /// components runs — the same reason <c>ViewPool</c> does it, and the same silent failure if it
    /// did not.
    /// </param>
    /// <param name="prefabs">
    /// Every arena this scene can raise, one prefab per id. May be empty, which leaves the run in
    /// whatever the scene was dressed with — that is the M0 grey box and the iteration workflow, not
    /// a fault.
    /// </param>
    /// <param name="parent">Where the two roots are parented, or null for the scene root.</param>
    /// <param name="hub">The run's event hub, subscribed to for the length of this object's life.</param>
    /// <param name="player">
    /// The body, moved to each arena's own start. Required rather than optional: an arena swap that
    /// left the player standing where the last room's floor used to be is the one failure this class
    /// cannot report.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="resolver"/>, <paramref name="hub"/> or <paramref name="player"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// An element of <paramref name="prefabs"/> is unassigned, carries an id that does not parse, or
    /// repeats an id another prefab already claims. All three are a mis-dragged Inspector field, and
    /// all three would otherwise surface as "no prefab carries arena.x" with the prefab in plain
    /// sight.
    /// </exception>
    public ArenaPool(
        IObjectResolver resolver,
        IReadOnlyList<ArenaView> prefabs,
        Transform parent,
        DomainEventHub hub,
        PlayerView player)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        // Unity's ==, not `is null`: a destroyed component is a live reference that only compares
        // equal to null through the engine's operator.
        _player = player == null ? throw new ArgumentNullException(nameof(player)) : player;

        IndexPrefabs(prefabs);

        Transform root = parent == null ? null : parent;

        _activeRoot = MakeRoot("Arena (standing)", root, active: true);
        _parkedRoot = MakeRoot("Arena (parked)", root, active: false);

        // Subscribed from the constructor, never from a Start of its own. RunTicker takes
        // SnapshotBuilder, SnapshotBuilder takes this, so this object is listening before
        // RunTicker.Start can ask core to open a run — and the opening StageArrived is published
        // from inside that call (AR §18.1).
        _arrivedSubscription = hub.Subscribe<StageArrived>(OnStageArrived);
        _clearedSubscription = hub.Subscribe<StageCleared>(OnStageCleared);
    }

    /// <summary>The arena currently standing, or null before the first <see cref="StageArrived"/>.</summary>
    public ArenaView Active { get; private set; }

    /// <summary>How many arena bodies exist, standing or parked. Never more than the roster.</summary>
    public int Count => _built.Count;

    /// <summary>
    /// The standing arena's spawn points in core's vectors, or an empty list when nothing is
    /// standing. What <c>SnapshotBuilder</c> hands to the snapshot every frame.
    /// </summary>
    public IReadOnlyList<System.Numerics.Vector3> SpawnPoints => _pointsView;

    /// <summary>
    /// Stops listening and destroys every body it raised, standing or parked, and the two roots.
    /// </summary>
    /// <remarks>
    /// <c>EnemyViews</c>' bargain, for its reason: one owner for the whole set, so nothing can
    /// outlive the run that built it. Called when <c>RunScope</c> is disposed, which is what leaving
    /// the Run scene does.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _arrivedSubscription.Dispose();
        _clearedSubscription.Dispose();

        foreach (ArenaView view in _built.Values)
        {
            Destroy(view == null ? null : view.gameObject);
        }

        _built.Clear();
        _prefabs.Clear();

        Active = null;

        _points = Array.Empty<System.Numerics.Vector3>();
        _pointsView = Array.AsReadOnly(_points);

        Destroy(_activeRoot == null ? null : _activeRoot.gameObject);
        Destroy(_parkedRoot == null ? null : _parkedRoot.gameObject);
    }

    /// <summary>
    /// Raises the stage's arena, places the player in it and seals it (M2-11a rule 5).
    /// </summary>
    /// <remarks>
    /// The player is moved on every arrival, including one that re-raises the arena already
    /// standing: a stage boundary puts them at the start of the room, and a single-arena roster is
    /// not a reason to leave them wherever the last wave chased them to.
    /// </remarks>
    private void OnStageArrived(StageArrived evt)
    {
        if (_disposed || evt.ArenaId.Value is null)
        {
            return;
        }

        ArenaView next = Obtain(evt.ArenaId);

        if (next == null)
        {
            return;
        }

        if (!ReferenceEquals(next, Active))
        {
            Park(Active);
            Raise(next);
        }

        _player.Teleport(next.PlayerStart);

        next.Seal();

        AdoptSpawnPoints(next);
    }

    /// <summary>
    /// Opens this arena's door and builds the next one, inactive, during the clear beat.
    /// </summary>
    private void OnStageCleared(StageCleared evt)
    {
        if (_disposed)
        {
            return;
        }

        if (Active != null)
        {
            Active.Open();
        }

        if (evt.NextArenaId.Value is null)
        {
            return;
        }

        // Prepared and nothing else: it is built under the parked root, so it is not raised, not
        // rendered and not navigable until the StageArrived that lands behind an opaque screen.
        Obtain(evt.NextArenaId);
    }

    /// <summary>
    /// The body for <paramref name="id"/> — the one already built, or a fresh one parked inactive.
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// No prefab in this scene carries that id. Loud, and naming both the id and what the scene does
    /// have: an arena a mode rosters and a scene cannot raise is a content mistake, and the symptom
    /// otherwise is a stage boundary that swaps nothing (rule 4, at the Unity end).
    /// </exception>
    private ArenaView Obtain(ContentId id)
    {
        if (_built.TryGetValue(id, out ArenaView existing) && existing != null)
        {
            return existing;
        }

        if (!_prefabs.TryGetValue(id, out ArenaView prefab))
        {
            // An empty roster is the undressed Run scene and the iteration workflow, not a mistake:
            // there is nothing to raise, so whatever the scene was dressed with stays standing. It
            // is said once, because every stage boundary would otherwise repeat it.
            if (_prefabs.Count == 0)
            {
                WarnAboutEmptyRosterOnce(id);

                return null;
            }

            throw new KeyNotFoundException(
                $"No arena prefab carries the id '{id}', which this stage was composed for. The "
                    + $"scene offers: {string.Join(", ", _prefabs.Keys)}. Add the prefab to "
                    + "RunScope's Arena Prefabs list, or correct the mode's arena roster.");
        }

        // Instantiated under the *parked* root, which is deactivated — so Awake never runs, no
        // renderer is enabled and no NavMesh data is added until this body is raised.
        ArenaView view = _resolver.Instantiate(
            prefab,
            _parkedRoot.position,
            _parkedRoot.rotation,
            _parkedRoot);

#if UNITY_EDITOR
        // Three objects called "Arena_Pillars(Clone)" in the hierarchy are unreadable the moment one
        // of them misbehaves. Editor-only: it allocates a string, and a run raises few enough
        // arenas that the cost is invisible either way — the rule is the same one EnemyViews keeps.
        view.name = prefab.name;
#endif

        _built[id] = view;

        return view;
    }

    /// <summary>Stands <paramref name="view"/> up: under the active root, and switched on.</summary>
    private void Raise(ArenaView view)
    {
        // worldPositionStays: false, so the body keeps the local transform it was instantiated with
        // rather than being re-fitted to the new parent — the two roots share a transform, so every
        // arena stands at exactly the same place (rule 6).
        view.transform.SetParent(_activeRoot, worldPositionStays: false);

        view.gameObject.SetActive(true);

        Active = view;
    }

    /// <summary>Takes <paramref name="view"/> out of service: switched off, back under the parked root.</summary>
    /// <remarks>
    /// Deactivated before it is reparented, so its <c>NavMeshSurface</c> has removed its data before
    /// the incoming arena's is added — two sets of navigation data over one patch of floor is a
    /// frame of enemies pathing through geometry that is not there.
    /// </remarks>
    private void Park(ArenaView view)
    {
        if (view == null)
        {
            return;
        }

        view.gameObject.SetActive(false);

        view.transform.SetParent(_parkedRoot, worldPositionStays: false);
    }

    /// <summary>Converts the standing arena's points into the list the snapshot carries.</summary>
    private void AdoptSpawnPoints(ArenaView view)
    {
        IReadOnlyList<Vector3> points = view.SpawnPoints;

        if (_points.Length != points.Count)
        {
            _points = new System.Numerics.Vector3[points.Count];
            _pointsView = Array.AsReadOnly(_points);
        }

        for (int i = 0; i < points.Count; i++)
        {
            _points[i] = points[i].ToNum();
        }
    }

    /// <summary>Indexes the scene's arena prefabs by the id each one carries.</summary>
    private void IndexPrefabs(IReadOnlyList<ArenaView> prefabs)
    {
        if (prefabs is null)
        {
            return;
        }

        for (int i = 0; i < prefabs.Count; i++)
        {
            ArenaView prefab = prefabs[i];

            if (prefab == null)
            {
                throw new ArgumentException(
                    $"prefabs[{i}] is unassigned. An empty row in RunScope's arena list is a "
                        + "mis-dragged field, and it would surface as an arena the mode rosters "
                        + "and the scene cannot raise.",
                    nameof(prefabs));
            }

            ContentId id;

            try
            {
                id = prefab.Id;
            }
            catch (ArgumentException inner)
            {
                // Rewrapped with the prefab's name, for ModeDefinition's reason: "'' is not a valid
                // content id" with a stack trace through a container tells a designer nothing about
                // which asset to open.
                throw new ArgumentException(
                    $"Arena prefab '{prefab.name}' has an invalid id: {inner.Message}",
                    nameof(prefabs),
                    inner);
            }

            if (_prefabs.ContainsKey(id))
            {
                throw new ArgumentException(
                    $"Two arena prefabs carry the id '{id}', and '{prefab.name}' is the second. An "
                        + "id names one room.",
                    nameof(prefabs));
            }

            _prefabs[id] = prefab;
        }
    }

    /// <summary>Makes one of the two roots, parented to the scope's arena root.</summary>
    private static Transform MakeRoot(string name, Transform parent, bool active)
    {
        var root = new GameObject(name);

        root.transform.SetParent(parent, worldPositionStays: false);
        root.SetActive(active);

        return root.transform;
    }

    private static void Destroy(GameObject go)
    {
        if (go == null)
        {
            return;
        }

        // The play-mode split ViewPool, InputAdapter and EnemyViews all make: in edit mode
        // Object.Destroy destroys nothing and logs an error rather than throwing, which would both
        // leak the object and redden any EditMode test that disposes one of these (M0-14).
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(go);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    /// <remarks>
    /// Once per run, not once per boundary: a scene with no arena prefabs says so at every stage,
    /// and the message is about the scene rather than about this stage.
    /// </remarks>
    private void WarnAboutEmptyRosterOnce(ContentId id)
    {
        if (_warnedAboutEmptyRoster)
        {
            return;
        }

        _warnedAboutEmptyRoster = true;

        Debug.LogWarning(
            $"This run's mode rosters arenas — stage 1 asked for '{id}' — but RunScope has no "
            + "arena prefabs, so nothing is raised and the scene stays as it was dressed. That is "
            + "the undressed-scene workflow; drag the arena prefabs onto RunScope to see the "
            + "rooms change.");
    }
}
