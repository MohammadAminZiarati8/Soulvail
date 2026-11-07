using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Soulvail.Game.Views;

public sealed class SwapStreakView : ITickable, IDisposable
{
    private const float Lift = 0.3f;
    private const float Hold = 0.05f;
    private const float Duration = 0.3f;

    private readonly GameObject _pool;
    private readonly LineRenderer _line;
    private readonly IDisposable _swapped;
    private float _elapsed;

    public SwapStreakView(DomainEventHub hub, LineRenderer prefab, Transform parent)
    {
        _pool = new GameObject("Swap streak (pooled)");
        _pool.SetActive(false);
        _pool.transform.SetParent(parent, false);
        _line = Object.Instantiate(prefab, _pool.transform, false);
        _line.gameObject.SetActive(false);
        _pool.SetActive(true);
        _swapped = hub.Subscribe<Swapped>(Show);
    }

    public void Tick() => Step(Time.deltaTime);

    public void Step(float dt)
    {
        if (!_line.gameObject.activeSelf)
        {
            return;
        }

        _elapsed += dt;
        SetAlpha(1f - Mathf.InverseLerp(Hold, Duration, _elapsed));
        if (_elapsed >= Duration)
        {
            _line.gameObject.SetActive(false);
        }
    }

    private void Show(Swapped evt)
    {
        Vector3 lift = Vector3.up * Lift;
        _line.SetPosition(0, evt.PlayerFrom.ToUnity() + lift);
        _line.SetPosition(1, evt.PlayerTo.ToUnity() + lift);
        _elapsed = 0f;
        SetAlpha(1f);
        _line.gameObject.SetActive(true);
    }

    private void SetAlpha(float alpha)
    {
        Color colour = Palette.Player;
        colour.a = alpha;
        _line.startColor = _line.endColor = colour;
    }

    public void Dispose()
    {
        _swapped.Dispose();

        if (Application.isPlaying)
        {
            Object.Destroy(_pool);
        }
        else
        {
            Object.DestroyImmediate(_pool);
        }
    }
}
