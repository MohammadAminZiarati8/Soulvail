using System;
using System.Collections.Generic;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Game.Views;

/// <summary>
/// Every thorn wall in the arena: one <see cref="ThornWallView"/> per wall <c>ThornSystem</c> has
/// open, rented on <see cref="ThornArmed"/>, raised on <see cref="ThornRose"/>, sunk on
/// <see cref="ThornWithered"/> and returned once it has sunk (JG-20e).
/// </summary>
/// <remarks>
/// <para>
/// <b>Prewarmed to <see cref="ThornSystem.Capacity"/> under a deactivated root</b> (rule 1, AR
/// §18.2), so the only <c>Instantiate</c> calls of a run happen while the scene loads. Core never
/// holds more than that many walls, so a further rent is a wiring fault: it is refused and logged
/// once rather than grown.
/// </para>
/// <para>
/// A listener, subscribed in the constructor, for <see cref="DecoyViews"/>' reason; and it sweeps on
/// <see cref="StageArrived"/> for the same one — a crossing swaps the arena under a live run
/// (rule 6). A wall is returned at once there rather than sunk.
/// </para>
/// </remarks>
public sealed class ThornWallViews : IDisposable
{
    private readonly Dictionary<int, ThornWallView> _byId = new Dictionary<int, ThornWallView>(ThornSystem.Capacity);
    private readonly Stack<ThornWallView> _pooled = new Stack<ThornWallView>(ThornSystem.Capacity);
    private readonly List<ThornWallView> _sweep = new List<ThornWallView>(ThornSystem.Capacity);
    private readonly ThornWallView[] _all = new ThornWallView[ThornSystem.Capacity];

    private readonly GameObject _pooledRoot;
    private readonly GameObject _liveRoot;
    private readonly CharacterController _player;
    private readonly Action<ThornWallView> _finished;

    private readonly IDisposable _armedSubscription;
    private readonly IDisposable _roseSubscription;
    private readonly IDisposable _witheredSubscription;
    private readonly IDisposable _stageSubscription;

    private bool _refusalLogged;
    private bool _disposed;

    /// <param name="wallPrefab">FX_ThornWall: carries a <see cref="ThornWallView"/> on its root.</param>
    /// <param name="hub">The run's event hub, subscribed to for this object's life.</param>
    /// <param name="parent">Where walls are parented, or null for the scene root. Never the arena.</param>
    /// <param name="player">Whose controller a risen wall waits to be clear of, or null for no one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wallPrefab"/> or <paramref name="hub"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="wallPrefab"/> has no <see cref="ThornWallView"/>.</exception>
    public ThornWallViews(GameObject wallPrefab, DomainEventHub hub, Transform parent, PlayerView player)
    {
        if (wallPrefab == null)
        {
            throw new ArgumentNullException(nameof(wallPrefab));
        }

        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        if (wallPrefab.GetComponent<ThornWallView>() == null)
        {
            throw new ArgumentException(
                $"{wallPrefab.name} has no {nameof(ThornWallView)} on its root.",
                nameof(wallPrefab));
        }

        // Unity's ==: a destroyed parent becomes a real null, so SetParent is handed the scene root.
        if (parent == null)
        {
            parent = null;
        }

        _player = player != null ? player.GetComponent<CharacterController>() : null;
        _finished = Return;

        _pooledRoot = new GameObject("Thorn walls (pooled)");
        _pooledRoot.SetActive(false);
        _pooledRoot.transform.SetParent(parent, false);

        _liveRoot = new GameObject("Thorn walls");
        _liveRoot.transform.SetParent(parent, false);

        for (int i = 0; i < ThornSystem.Capacity; i++)
        {
            GameObject instance = Object.Instantiate(wallPrefab, _pooledRoot.transform, false);
            ThornWallView view = instance.GetComponent<ThornWallView>();
            view.SetFinishedHandler(_finished);
            view.OnDespawn();
            _all[i] = view;
            _pooled.Push(view);
        }

        _armedSubscription = hub.Subscribe<ThornArmed>(OnArmed);
        _roseSubscription = hub.Subscribe<ThornRose>(OnRose);
        _witheredSubscription = hub.Subscribe<ThornWithered>(OnWithered);
        _stageSubscription = hub.Subscribe<StageArrived>(_ => ReturnAll());
    }

    /// <summary>How many walls are in the arena right now — arming, standing or sinking.</summary>
    public int LiveCount => _byId.Count;

    /// <summary>How many walls wait in the pool. With <see cref="LiveCount"/>, always the capacity.</summary>
    public int PooledCount => _pooled.Count;

    /// <summary>The deactivated root the pool is built under.</summary>
    public Transform PoolRoot => _pooledRoot.transform;

    /// <summary>The wall standing in for <paramref name="id"/>, if there is one.</summary>
    public bool TryGet(int id, out ThornWallView view) => _byId.TryGetValue(id, out view);

    /// <summary>Stops listening and destroys every wall, live or pooled.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _armedSubscription.Dispose();
        _roseSubscription.Dispose();
        _witheredSubscription.Dispose();
        _stageSubscription.Dispose();

        _byId.Clear();
        _pooled.Clear();

        for (int i = 0; i < _all.Length; i++)
        {
            if (_all[i] != null)
            {
                DestroyObject(_all[i].gameObject);
            }

            _all[i] = null;
        }

        DestroyObject(_pooledRoot);
        DestroyObject(_liveRoot);
    }

    private static void DestroyObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        // In edit mode Object.Destroy destroys nothing and logs an error (ViewPool's split).
        if (Application.isPlaying)
        {
            Object.Destroy(target);
        }
        else
        {
            Object.DestroyImmediate(target);
        }
    }

    private void OnArmed(ThornArmed evt)
    {
        if (_byId.ContainsKey(evt.Id))
        {
            Return(_byId[evt.Id]);
        }

        if (_pooled.Count == 0)
        {
            if (!_refusalLogged)
            {
                _refusalLogged = true;
                Debug.LogWarning(
                    $"{nameof(ThornWallViews)}: no wall left for thorn {evt.Id}; ThornSystem holds " +
                    $"at most {ThornSystem.Capacity}. Refused (logged once).");
            }

            return;
        }

        ThornWallView view = _pooled.Pop();

        Vector3 direction = new Vector3(evt.Direction.X, 0f, evt.Direction.Y);
        Quaternion rotation = direction.sqrMagnitude > 0f ? Quaternion.LookRotation(direction) : Quaternion.identity;
        Vector3 centre = evt.Origin.ToUnity() + (direction * (evt.Length * 0.5f));

        view.transform.SetParent(_liveRoot.transform, false);
        view.OnSpawn();
        view.Arm(evt.Id, centre, rotation, evt.Length, evt.HalfWidth, evt.ArmSeconds);

        _byId[evt.Id] = view;
    }

    private void OnRose(ThornRose evt)
    {
        if (_byId.TryGetValue(evt.Id, out ThornWallView view))
        {
            view.Rise(_player);
        }
    }

    private void OnWithered(ThornWithered evt)
    {
        if (_byId.TryGetValue(evt.Id, out ThornWallView view))
        {
            view.Wither();
        }
    }

    /// <summary>A wall has sunk, or is being swept: back to the pool under the inactive root.</summary>
    private void Return(ThornWallView view)
    {
        if (view == null || !_byId.Remove(view.Id))
        {
            return;
        }

        view.OnDespawn();
        view.transform.SetParent(_pooledRoot.transform, false);
        _pooled.Push(view);
    }

    private void ReturnAll()
    {
        if (_byId.Count == 0)
        {
            return;
        }

        _sweep.Clear();
        _sweep.AddRange(_byId.Values);

        for (int i = 0; i < _sweep.Count; i++)
        {
            Return(_sweep[i]);
        }

        _sweep.Clear();
    }
}
