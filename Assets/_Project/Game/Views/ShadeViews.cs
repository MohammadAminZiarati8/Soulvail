using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Soulvail.Game.Views;

public sealed class ShadeViews : ITickable, IDisposable
{
    private readonly ViewPool<ShadeView> _pool;
    private readonly PlayerView _player;
    private readonly ViewPool<ShadeBurstView> _burstPool;
    private readonly ShadeBurstView[] _bursts = new ShadeBurstView[ShadeSystem.Capacity];
    private readonly IDisposable _burst;
    private readonly IDisposable _appeared;
    private readonly IDisposable _loosed;
    private readonly IDisposable _faded;
    private readonly IDisposable _transition;
    private readonly IDisposable _arrived;
    private readonly (int Id, ShadeView View)[] _slots = new (int, ShadeView)[ShadeSystem.Capacity];

    public ShadeViews(IObjectResolver resolver, DomainEventHub hub, ShadeView prefab, Transform parent,
        PlayerView player, ShadeBurstView burstPrefab = null, Transform burstParent = null)
    {
        _player = player;
        _pool = new ViewPool<ShadeView>(resolver, prefab, parent, ShadeSystem.Capacity);
        if (burstPrefab != null)
        {
            _burstPool = new ViewPool<ShadeBurstView>(resolver, burstPrefab, burstParent, _bursts.Length);
            _burst = hub.Subscribe<ShadeBurst>(OnBurst);
        }

        _appeared = hub.Subscribe<ShadeAppeared>(OnAppeared);
        _loosed = hub.Subscribe<ShadeLoosed>(OnLoosed);
        _faded = hub.Subscribe<ShadeFaded>(OnFaded);
        _transition = hub.Subscribe<StageTransitionStarted>(_ => Clear());
        _arrived = hub.Subscribe<StageArrived>(_ => Clear());
    }

    public void Tick() => Step(Time.deltaTime);

    public void Step(float dt)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].View != null && _slots[i].View.Step(dt))
            {
                Release(i);
            }
        }

        for (int i = 0; i < _bursts.Length; i++)
        {
            if (_bursts[i] != null && _bursts[i].Step(dt))
            {
                ReleaseBurst(i);
            }
        }
    }

    private void OnBurst(ShadeBurst evt)
    {
        int slot = 0;
        for (int i = 0; i < _bursts.Length; i++)
        {
            if (_bursts[i] == null)
            {
                slot = i;
                break;
            }

            if (_bursts[i].Remaining < _bursts[slot].Remaining)
            {
                slot = i;
            }
        }

        // When all eight are visible, give the most faded ring to the new burst.
        ReleaseBurst(slot);
        ShadeBurstView view = _burstPool.Get();
        view.Flash(evt.Position.ToUnity(), evt.Radius, evt.Slows);
        _bursts[slot] = view;
    }

    private void ReleaseBurst(int slot)
    {
        if (_bursts[slot] != null)
        {
            _burstPool.Release(_bursts[slot]);
            _bursts[slot] = null;
        }
    }

    private void OnAppeared(ShadeAppeared evt)
    {
        int slot = -1;
        int oldestFade = -1;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].View == null)
            {
                slot = i;
                break;
            }

            if (_slots[i].View.IsFading && (oldestFade < 0 || _slots[i].Id < _slots[oldestFade].Id))
            {
                oldestFade = i;
            }
        }

        // A new core shade can reuse the seat of an expired one still fading on screen.
        if (slot < 0)
        {
            if (oldestFade < 0)
            {
                return;
            }
            slot = oldestFade;
            Release(slot);
        }

        ShadeView view = _pool.Get();
        view.Appear(evt.Position.ToUnity(), _player.transform.rotation, evt.Kind);
        _slots[slot] = (evt.Id, view);
    }

    private void OnLoosed(ShadeLoosed evt)
    {
        int slot = Find(evt.Id);
        if (slot >= 0 && !_slots[slot].View.IsFading)
        {
            _slots[slot].View.Loose(evt.Target.ToUnity());
        }
    }

    private void OnFaded(ShadeFaded evt)
    {
        int slot = Find(evt.Id);
        if (slot < 0)
        {
            return;
        }

        if (evt.Expired)
        {
            _slots[slot].View.Fade();
        }
        else
        {
            Release(slot);
        }
    }

    private int Find(int id)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Id == id && _slots[i].View != null)
            {
                return i;
            }
        }
        return -1;
    }

    private void Release(int slot)
    {
        _pool.Release(_slots[slot].View);
        _slots[slot] = default;
    }

    private void Clear()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].View != null)
            {
                Release(i);
            }
        }

        for (int i = 0; i < _bursts.Length; i++)
        {
            ReleaseBurst(i);
        }
    }

    public void Dispose()
    {
        _appeared.Dispose();
        _loosed.Dispose();
        _faded.Dispose();
        _transition.Dispose();
        _arrived.Dispose();
        _burst?.Dispose();
        _burstPool?.Dispose();
        _pool.Dispose();
    }
}
