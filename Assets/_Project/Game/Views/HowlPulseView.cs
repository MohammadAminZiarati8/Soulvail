using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Soulvail.Game.Views;

public sealed class HowlPulseView : ITickable, IDisposable
{
    private const int Segments = 64;
    private const float Lift = 0.06f;
    private const float Radius = 6f;
    private const float Duration = 0.5f;

    private readonly GameObject _pool;
    private readonly LineRenderer _line;
    private readonly IDisposable _howled;
    private readonly Vector3[] _unitRing = new Vector3[Segments + 1];
    private readonly Vector3[] _positions = new Vector3[Segments + 1];
    private float _elapsed;

    public HowlPulseView(DomainEventHub hub, LineRenderer prefab, Transform parent)
    {
        _pool = new GameObject("Howl pulse (pooled)");
        _pool.SetActive(false);
        _pool.transform.SetParent(parent, false);
        _line = Object.Instantiate(prefab, _pool.transform, false);
        _line.gameObject.SetActive(false);
        _pool.SetActive(true);

        _line.positionCount = _positions.Length;
        for (int i = 0; i < Segments; i++)
        {
            float angle = (float)i / Segments * (2f * Mathf.PI);
            _unitRing[i] = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f);
        }
        _unitRing[Segments] = _unitRing[0];

        _howled = hub.Subscribe<PackHowled>(Show);
    }

    public void Tick() => Step(Time.deltaTime);

    public void Step(float dt)
    {
        if (!_line.gameObject.activeSelf)
        {
            return;
        }

        _elapsed += dt;
        Draw(Mathf.Clamp01(_elapsed / Duration));
        if (_elapsed >= Duration)
        {
            _line.gameObject.SetActive(false);
        }
    }

    private void Show(PackHowled evt)
    {
        _line.transform.position = evt.Position.ToUnity() + Vector3.up * Lift;
        _elapsed = 0f;
        Draw(0f);
        _line.gameObject.SetActive(true);
    }

    private void Draw(float progress)
    {
        float radius = Radius * progress;
        for (int i = 0; i < _positions.Length; i++)
        {
            _positions[i] = _unitRing[i] * radius;
        }
        _line.SetPositions(_positions);

        Color colour = Palette.Player;
        colour.a = 1f - progress;
        _line.startColor = _line.endColor = colour;
    }

    public void Dispose()
    {
        _howled.Dispose();
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
