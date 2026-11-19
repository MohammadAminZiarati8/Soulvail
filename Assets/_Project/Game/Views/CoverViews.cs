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
    private const int PillarCount = 3;
    private readonly Dictionary<ContentId, (GameObject View, ShardbackPillar[] Pillars)> _covers = new();
    private readonly Dictionary<ContentId, int> _owners = new Dictionary<ContentId, int>();
    private readonly GameObject _root;
    private readonly Transform _parent;
    private readonly IDisposable _spawned;
    private readonly IDisposable _despawned;
    private readonly IDisposable _arrived;
    private readonly IDisposable _fightStarted;
    private readonly IDisposable _pillarBroken;
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
        try
        {
            int coverLayer = LayerMask.NameToLayer("Cover");
            foreach (KeyValuePair<ContentId, EnemyLook> entry in looks.Entries)
            {
                if (entry.Value.Cover != null)
                {
                    GameObject cover = UnityEngine.Object.Instantiate(entry.Value.Cover, _root.transform, false);
                    cover.SetActive(false);
                    _covers.Add(entry.Key, (cover, CapturePillars(cover, coverLayer)));
                }
            }
        }
        catch
        {
            DestroyRoot();
            throw;
        }

        _spawned = hub.Subscribe<EnemySpawned>(OnSpawned);
        _despawned = hub.Subscribe<EnemyDespawned>(OnDespawned);
        _arrived = hub.Subscribe<StageArrived>(_ => LiftAll());
        _fightStarted = hub.Subscribe<ShardbackFightStarted>(OnFightStarted);
        _pillarBroken = hub.Subscribe<ShardbackPillarBroken>(OnPillarBroken);
    }

    private static ShardbackPillar[] CapturePillars(GameObject cover, int coverLayer)
    {
        ShardbackPillar[] markers = cover.GetComponentsInChildren<ShardbackPillar>(true);
        if (markers.Length == 0)
        {
            return markers;
        }

        if (markers.Length != PillarCount)
        {
            throw new InvalidOperationException("Marked Shardback cover needs exactly three pillars.");
        }

        var slots = new ShardbackPillar[PillarCount];
        foreach (ShardbackPillar marker in markers)
        {
            int slot = marker.Slot;
            if (slot < 0 || slot >= slots.Length || slots[slot] != null)
            {
                throw new InvalidOperationException("Author each Shardback pillar slot from 0 to 2 exactly once.");
            }

            marker.Capture(cover.transform, coverLayer);
            slots[slot] = marker;
        }

        return slots;
    }

    /// <summary>How many archetypes currently have standing cover.</summary>
    public int LiveCount => _owners.Count;

    /// <summary>Changes when cover is placed, lifted, swept, restored, or broken.</summary>
    public int Version { get; private set; }

    private void OnSpawned(EnemySpawned e)
    {
        if (_root == null || !_covers.TryGetValue(e.SpecId, out var cover) || cover.View == null)
        {
            return;
        }

        if (_owners.TryGetValue(e.SpecId, out int owner) && owner != e.Id)
        {
            ClearBindings(cover.Pillars);
        }

        _owners[e.SpecId] = e.Id;
        cover.View.transform.SetParent(_parent, false);
        cover.View.transform.SetPositionAndRotation(e.Position.ToUnity(), Quaternion.identity);
        cover.View.SetActive(true);
        Version++;
    }

    private ShardbackPillar[] FindPillars(int bossId)
    {
        if (_root != null)
        {
            foreach (KeyValuePair<ContentId, int> owner in _owners)
            {
                if (owner.Value == bossId)
                {
                    var cover = _covers[owner.Key];
                    if (cover.View != null && cover.Pillars.Length != 0)
                    {
                        return cover.Pillars;
                    }
                }
            }
        }

        return null;
    }

    private void OnFightStarted(ShardbackFightStarted e)
    {
        ShardbackPillar[] pillars = FindPillars(e.BossId);
        if (pillars == null || e.BossId <= 0 || e.Lifetime == Guid.Empty)
        {
            return;
        }

        foreach (ShardbackPillar pillar in pillars)
        {
            if (pillar == null || pillar.Lifetime == e.Lifetime)
            {
                return;
            }
        }

        foreach (ShardbackPillar pillar in pillars)
        {
            pillar.Restore();
            pillar.Bind(e.BossId, e.Lifetime);
        }

        Version++;
    }

    private void OnPillarBroken(ShardbackPillarBroken e)
    {
        if (e.Slot < 0 || e.Slot >= PillarCount || e.BossId <= 0 || e.Lifetime == Guid.Empty)
        {
            return;
        }

        ShardbackPillar[] pillars = FindPillars(e.BossId);
        ShardbackPillar pillar = pillars?[e.Slot];
        if (pillar != null && pillar.BossId == e.BossId && pillar.Lifetime == e.Lifetime && !pillar.IsBroken)
        {
            pillar.Break();
            Version++;
        }
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

    private static void ClearBindings(ShardbackPillar[] pillars)
    {
        foreach (ShardbackPillar pillar in pillars)
        {
            if (pillar != null)
            {
                pillar.Unbind();
            }
        }
    }

    private void Lift((GameObject View, ShardbackPillar[] Pillars) cover)
    {
        ClearBindings(cover.Pillars);
        // A scene unload destroys the cover before the scope disposes this. Throwing here would abort
        // the container's disposal and leave every tickable of the dead run ticking for good.
        if (cover.View == null || _root == null)
        {
            return;
        }

        cover.View.SetActive(false);
        cover.View.transform.SetParent(_root.transform, false);
    }

    private void LiftAll()
    {
        foreach (var cover in _covers.Values)
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
        _fightStarted.Dispose();
        _pillarBroken.Dispose();
        LiftAll();
        DestroyRoot();
    }

    private void DestroyRoot()
    {
        if (_root == null)
        {
            return;
        }

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
