using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Soulvail.Game.Views;

/// <summary>Prewarms the short aim lines drawn from each shade's shot.</summary>
public sealed class CrossfireView : ITickable, IDisposable
{
    private const float Lifetime = 0.15f;
    private const float Width = 0.055f;
    private const float Lift = 0.12f;

    private readonly GameObject _root;
    private readonly LineRenderer[] _lines;
    private readonly float[] _remaining;
    private readonly IDisposable _fired;
    private readonly IDisposable _transition;
    private readonly IDisposable _arrived;

    public CrossfireView(DomainEventHub hub, Material material, Transform parent, int capacity)
    {
        _root = new GameObject("Crossfire lines (pooled)");
        _root.SetActive(false);
        _root.transform.SetParent(parent, false);
        _lines = new LineRenderer[capacity];
        _remaining = new float[capacity];

        for (int i = 0; i < capacity; i++)
        {
            var body = new GameObject("Crossfire aim line");
            body.transform.SetParent(_root.transform, false);
            var line = body.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = Width;
            line.endWidth = Width;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            _lines[i] = line;
        }

        _root.SetActive(true);
        _fired = hub.Subscribe<ProjectileFired>(OnFired);
        _transition = hub.Subscribe<StageTransitionStarted>(_ => Clear());
        _arrived = hub.Subscribe<StageArrived>(_ => Clear());
    }

    public void Tick() => Step(Time.deltaTime);

    public void Step(float dt)
    {
        for (int i = 0; i < _lines.Length; i++)
        {
            if (_remaining[i] > 0f)
            {
                _remaining[i] = Mathf.Max(0f, _remaining[i] - dt);
                _lines[i].enabled = _remaining[i] > 0f;
            }
        }
    }

    private void OnFired(ProjectileFired evt)
    {
        if (evt.ShotOrigin != ShotOrigin.Crossfire)
        {
            return;
        }

        // Reuse the line closest to expiring if a burst fills the entire pool.
        int slot = 0;
        for (int i = 0; i < _lines.Length && _remaining[slot] > 0f; i++)
        {
            if (_remaining[i] < _remaining[slot])
            {
                slot = i;
            }
        }

        LineRenderer line = _lines[slot];
        Vector3 lift = Vector3.up * Lift;
        line.SetPosition(0, evt.Origin.ToUnity() + lift);
        line.SetPosition(1, evt.Target.ToUnity() + lift);
        _remaining[slot] = Lifetime;
        line.enabled = true;
    }

    private void Clear()
    {
        Array.Clear(_remaining, 0, _remaining.Length);
        for (int i = 0; i < _lines.Length; i++)
        {
            _lines[i].enabled = false;
        }
    }

    public void Dispose()
    {
        _fired.Dispose();
        _transition.Dispose();
        _arrived.Dispose();

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
