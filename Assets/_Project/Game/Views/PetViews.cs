using System;
using System.Collections.Generic;
using Soulvail.Core.Ai;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
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
    private readonly Vector3 _bodyScale;
    private readonly IDisposable _spawned;
    private readonly IDisposable _despawned;
    private readonly IDisposable _bit;
    private readonly IDisposable _roared;
    private readonly IDisposable _howled;
    private readonly IDisposable _hurt;
    private readonly IDisposable _heeled;
    private readonly IDisposable _returned;
    private readonly IDisposable _fell;
    private readonly IDisposable _rose;
    private readonly Func<int, float?> _healthFraction;
    private readonly IDisposable _arrived;
    private readonly IDisposable _ended;
    private Vector3? _arenaEntryPosition;

    /// <summary>Raised after a pet body is bound, so a run-local look can dress it on arrival.</summary>
    public event Action<PetView> Spawned;

    public IReadOnlyCollection<PetView> Active => _active.Values;

    public PetViews(IObjectResolver resolver, PetView prefab, Transform parent, DomainEventHub hub,
        PlayerView player = null, ArenaPool arenas = null, Camera camera = null,
        Func<int, float?> healthFraction = null)
    {
        if (prefab == null)
        {
            return;
        }

        _player = player;
        _arenas = arenas;
        _camera = camera;
        _bodyScale = prefab.transform.localScale;
        _healthFraction = healthFraction;
        _pool = new ViewPool<PetView>(resolver, prefab, parent, prewarm: 1);
        _spawned = hub.Subscribe<PetSpawned>(OnSpawned);
        _despawned = hub.Subscribe<PetDespawned>(OnDespawned);
        _bit = hub.Subscribe<PetBit>(OnBit);
        _roared = hub.Subscribe<PetRoared>(evt =>
        {
            if (_active.TryGetValue(evt.PetId, out PetView view))
            {
                view.Roar();
            }
        });
        _howled = hub.Subscribe<PackHowled>(evt =>
        {
            float now = Time.time;
            foreach (PetView view in _active.Values)
            {
                view.Howl(evt.Seconds, now);
            }
        });
        _hurt = hub.Subscribe<PetHurt>(evt =>
        {
            if (_active.TryGetValue(evt.PetId, out PetView view))
            {
                view.PlayHit();
            }
        });
        _heeled = hub.Subscribe<PetHeeled>(evt => SetHeeling(evt.PetId, true));
        _returned = hub.Subscribe<PetReturned>(evt => SetHeeling(evt.PetId, false));
        _fell = hub.Subscribe<PetFell>(evt =>
        {
            if (_active.TryGetValue(evt.PetId, out PetView view))
            {
                view.Fall(evt.Position.ToUnity());
            }
        });
        _rose = hub.Subscribe<PetRose>(evt =>
        {
            if (_active.TryGetValue(evt.PetId, out PetView view))
            {
                view.Rise();
            }
        });
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

    public void ApplyLeaps(IReadOnlyList<PetLeapIntent> leaps)
    {
        for (int i = 0; i < leaps.Count; i++)
        {
            PetLeapIntent leap = leaps[i];
            if (_active.TryGetValue(leap.PetId, out PetView view))
            {
                view.Leap(leap.To.ToUnity(), leap.Seconds);
            }
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
        foreach (PetView view in _active.Values)
        {
            view.StepLeap(dt);
        }
    }

    public void Dispose()
    {
        _spawned?.Dispose();
        _despawned?.Dispose();
        _bit?.Dispose();
        _roared?.Dispose();
        _howled?.Dispose();
        _hurt?.Dispose();
        _heeled?.Dispose();
        _returned?.Dispose();
        _fell?.Dispose();
        _rose?.Dispose();
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
        view.transform.localScale = evt.IsLead ? _bodyScale : _bodyScale * 0.8f;
        view.Bind(evt.Id, position, evt.HealthFraction, _camera, _healthFraction);
        _active.Add(evt.Id, view);
        Spawned?.Invoke(view);
    }

    private void OnDespawned(PetDespawned evt)
    {
        if (_active.Remove(evt.Id, out PetView view))
        {
            _pool.Release(view);
        }
    }

    private void SetHeeling(int petId, bool heeling)
    {
        if (_active.TryGetValue(petId, out PetView view))
        {
            view.SetHeeling(heeling);
        }
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
