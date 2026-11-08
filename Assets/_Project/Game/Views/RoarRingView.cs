using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Soulvail.Game.Views;

public sealed class RoarRingView : ITickable, IDisposable
{
    private const int Segments = 64;
    private const float Lift = 0.06f;
    private const float Duration = 0.4f;
    private const float InitialScale = 0.75f;

    private readonly GameObject _pool;
    private readonly LineRenderer _line;
    private readonly IDisposable _roared;
    private float _radius;
    private float _elapsed;

    public RoarRingView(DomainEventHub hub, LineRenderer prefab, Transform parent)
    {
        _pool = new GameObject("Roar ring (pooled)");
        _pool.SetActive(false);
        _pool.transform.SetParent(parent, false);
        _line = Object.Instantiate(prefab, _pool.transform, false);
        _line.gameObject.SetActive(false);
        _pool.SetActive(true);
        _roared = hub.Subscribe<PetRoared>(Show);
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

    private void Show(PetRoared evt)
    {
        _line.transform.position = evt.Position.ToUnity() + Vector3.up * Lift;
        _radius = evt.Radius;
        _elapsed = 0f;
        Draw(0f);
        _line.gameObject.SetActive(true);
    }

    private void Draw(float progress)
    {
        float radius = _radius * Mathf.Lerp(InitialScale, 1f, progress);
        _line.positionCount = Segments + 1;
        for (int i = 0; i <= Segments; i++)
        {
            float angle = (float)i / Segments * (2f * Mathf.PI);
            _line.SetPosition(i, new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f) * radius);
        }

        Color colour = Palette.Player;
        colour.a = 1f - progress;
        _line.startColor = _line.endColor = colour;
    }

    public void Dispose()
    {
        _roared.Dispose();

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
