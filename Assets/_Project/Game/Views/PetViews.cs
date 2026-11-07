using System;
using System.Collections.Generic;
using Soulvail.Core.Ai;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Pooling;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views;

/// <summary>Mirrors the run's pet census and applies core movement to the active bodies.</summary>
public sealed class PetViews : IDisposable
{
    private readonly Dictionary<int, PetView> _active = new();
    private readonly ViewPool<PetView> _pool;
    private readonly PlayerView _player;
    private readonly ArenaPool _arenas;
    private readonly Camera _camera;
    private readonly IDisposable _spawned;
    private readonly IDisposable _bit;
    private readonly IDisposable _arrived;
    private readonly IDisposable _ended;
    private Vector3? _arenaEntryPosition;

    public PetViews(IObjectResolver resolver, PetView prefab, Transform parent, DomainEventHub hub,
        PlayerView player = null, ArenaPool arenas = null, Camera camera = null)
    {
        if (prefab == null)
        {
            return;
        }

        _player = player;
        _arenas = arenas;
        _camera = camera;
        _pool = new ViewPool<PetView>(resolver, prefab, parent, prewarm: 1);
        _spawned = hub.Subscribe<PetSpawned>(OnSpawned);
        _bit = hub.Subscribe<PetBit>(OnBit);
        _arrived = hub.Subscribe<StageArrived>(OnArrived);
        _ended = hub.Subscribe<RunEnded>(_ => Clear());
    }

    public void CopyInto(WorldSnapshot snapshot)
    {
        foreach (PetView view in _active.Values)
        {
            snapshot.AddPet() = new EnemySense
            {
                Id = view.Id,
                Position = view.transform.position.ToNum(),
                Velocity = view.Velocity.ToNum()
            };
        }
    }

    public void ApplyMoves(IReadOnlyList<EnemyMoveIntent> moves, float dt)
    {
        for (int i = 0; i < moves.Count; i++)
        {
            EnemyMoveIntent move = moves[i];
            if (_active.TryGetValue(move.Id, out PetView view))
            {
                view.Apply(move, dt);
            }
        }
    }

    public void Dispose()
    {
        _spawned?.Dispose();
        _bit?.Dispose();
        _arrived?.Dispose();
        _ended?.Dispose();
        _active.Clear();
        _pool?.Dispose();
    }

    private void OnSpawned(PetSpawned evt)
    {
        if (_active.ContainsKey(evt.Id) || _active.Count == PetSystem.Capacity)
        {
            return;
        }

        Vector3 position = evt.Position.ToUnity();
        // Core places the pet beside its last player pose. ArenaPool has since moved that pose.
        if (_arenaEntryPosition.HasValue && evt.PlayerPosition.HasValue)
        {
            position += _arenaEntryPosition.Value - evt.PlayerPosition.Value.ToUnity();
        }

        PetView view = _pool.Get();
        view.Bind(evt.Id, position, evt.HealthFraction, _camera);
        _active.Add(evt.Id, view);
    }

    private void OnBit(PetBit evt)
    {
        if (_active.TryGetValue(evt.PetId, out PetView view))
        {
            view.PlayBite();
        }
    }

    private void OnArrived(StageArrived evt)
    {
        Clear();
        if (_arenas?.Active != null && _arenas.Active.Id == evt.ArenaId)
        {
            _arenaEntryPosition = _player.Position;
        }
    }

    private void Clear()
    {
        foreach (PetView view in _active.Values)
        {
            _pool.Release(view);
        }

        _active.Clear();
        _arenaEntryPosition = null;
    }
}
