using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Soulvail.Game.Views;

/// <summary>Rents a visual body for each core axe and draws its latest simulated position.</summary>
public sealed class AxeViews : ILateTickable, IDisposable
{
    public delegate ReadOnlySpan<Axe> ReadFlight();

    private readonly GameObject _root;
    private readonly AxeView[] _views = new AxeView[AxeSystem.Capacity];
    private readonly ReadFlight _readFlight;
    private readonly IDisposable[] _subscriptions;

    public AxeViews(DomainEventHub hub, AxeView prefab, Transform parent, ReadFlight readFlight)
    {
        _readFlight = readFlight;
        _root = new GameObject("Axes (pooled)");
        _root.SetActive(false);
        _root.transform.SetParent(parent, false);
        for (int i = 0; i < _views.Length; i++)
        {
            AxeView view = Object.Instantiate(prefab, _root.transform);
            view.gameObject.SetActive(false);
            _views[i] = view;
        }

        _root.SetActive(true);
        _subscriptions = new[]
        {
            hub.Subscribe<AxeThrown>(OnThrown),
            hub.Subscribe<AxeTurned>(evt => Find(evt.Id)?.Turn()),
            hub.Subscribe<AxeCaught>(evt => Find(evt.Id)?.Hide()),
            hub.Subscribe<StageTransitionStarted>(_ => Clear()),
            hub.Subscribe<StageArrived>(_ => Clear()),
        };
    }

    public void LateTick() => Draw(Time.deltaTime);

    public void Draw(float dt)
    {
        ReadOnlySpan<Axe> flight = _readFlight();
        foreach (AxeView view in _views)
        {
            if (view.Id == 0)
            {
                continue;
            }

            bool found = false;
            foreach (ref readonly Axe axe in flight)
            {
                if (axe.Id == view.Id)
                {
                    view.Draw(axe.Position.ToUnity(), dt);
                    found = true;
                    break;
                }
            }

            // Core also clears flight on stage clear, which need not publish individual catches.
            if (!found)
            {
                view.Hide();
            }
        }
    }

    private void OnThrown(AxeThrown evt) => Find(0)?.Show(evt.Id, evt.From.ToUnity());

    private AxeView Find(int id)
    {
        foreach (AxeView view in _views)
        {
            if (view.Id == id)
            {
                return view;
            }
        }

        return null;
    }

    private void Clear()
    {
        foreach (AxeView view in _views)
        {
            if (view.Id != 0)
            {
                view.Hide();
            }
        }
    }

    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        if (_root != null)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(_root);
            }
            else
            {
                Object.DestroyImmediate(_root);
            }
        }
    }
}
