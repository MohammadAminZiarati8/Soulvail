using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using UnityEngine;

namespace Soulvail.Game.Views;

/// <summary>Prebuilt stationary cover, placed and lifted with its archetype.</summary>
public sealed class CoverViews : IDisposable
{
    private readonly Dictionary<ContentId, GameObject> _covers = new Dictionary<ContentId, GameObject>();
    private readonly Dictionary<ContentId, int> _owners = new Dictionary<ContentId, int>();
    private readonly GameObject _root;
    private readonly Transform _parent;
    private readonly IDisposable _spawned;
    private readonly IDisposable _despawned;
    private readonly IDisposable _arrived;
    private bool _disposed;

    /// <summary>Builds each authored cover while its holding root is inactive (AR §18.2).</summary>
    public CoverViews(EnemyLookBook looks, DomainEventHub hub, Transform parent)
    {
        if (looks is null)
        {
            throw new ArgumentNullException(nameof(looks));
        }

        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        _parent = parent;
        _root = new GameObject("CoverViews (inactive)");
        _root.SetActive(false);
        _root.transform.SetParent(parent, false);
        foreach (KeyValuePair<ContentId, EnemyLook> entry in looks.Entries)
        {
            if (entry.Value.Cover != null)
            {
                GameObject cover = UnityEngine.Object.Instantiate(entry.Value.Cover, _root.transform, false);
                cover.SetActive(false);
                _covers.Add(entry.Key, cover);
            }
        }

        _spawned = hub.Subscribe<EnemySpawned>(OnSpawned);
        _despawned = hub.Subscribe<EnemyDespawned>(OnDespawned);
        _arrived = hub.Subscribe<StageArrived>(_ => LiftAll());
    }

    /// <summary>How many archetypes currently have standing cover.</summary>
    public int LiveCount => _owners.Count;

    /// <summary>Changes whenever cover is placed, lifted, or swept at a stage boundary.</summary>
    public int Version { get; private set; }

    private void OnSpawned(EnemySpawned e)
    {
        if (!_covers.TryGetValue(e.SpecId, out GameObject cover))
        {
            return;
        }

        _owners[e.SpecId] = e.Id;
        cover.transform.SetParent(_parent, false);
        cover.transform.SetPositionAndRotation(e.Position.ToUnity(), Quaternion.identity);
        cover.SetActive(true);
        Version++;
    }

    private void OnDespawned(EnemyDespawned e)
    {
        foreach (KeyValuePair<ContentId, int> owner in _owners)
        {
            if (owner.Value == e.Id)
            {
                Lift(_covers[owner.Key]);
                Version++;
                _owners.Remove(owner.Key);
                return;
            }
        }
    }

    private void Lift(GameObject cover)
    {
        cover.SetActive(false);
        cover.transform.SetParent(_root.transform, false);
    }

    private void LiftAll()
    {
        foreach (GameObject cover in _covers.Values)
        {
            Lift(cover);
        }

        _owners.Clear();
        Version++;
    }

    /// <summary>Unsubscribes and destroys every instance, including standing cover.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _spawned.Dispose();
        _despawned.Dispose();
        _arrived.Dispose();
        LiftAll();
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(_root);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(_root);
        }
    }
}
