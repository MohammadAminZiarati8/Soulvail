using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer.Unity;

namespace Soulvail.Game.Composition;

/// <summary>Applies the core's slow-motion edges around the pause owner's hold.</summary>
public sealed class TimeScaleDriver : ILateTickable, IDisposable
{
    private readonly RunPause _pause;
    private readonly IDisposable _started;
    private readonly IDisposable _ended;
    private readonly IDisposable _runEnded;
    private float _target = 1f;
    private float _appliedTarget = 1f;
    private float _terminalBeatRemaining;
    private bool _terminalBeat;

    public TimeScaleDriver(DomainEventHub hub, RunPause pause)
    {
        _pause = pause;
        _started = hub.Subscribe<SlowMotionStarted>(OnStarted);
        _ended = hub.Subscribe<SlowMotionEnded>(_ => SetTarget(1f));
        _runEnded = hub.Subscribe<RunEnded>(_ => OnRunEnded());
    }

    private void OnStarted(SlowMotionStarted evt)
    {
        _terminalBeatRemaining = evt.RealSeconds;
        SetTarget(evt.Scale);
    }

    private void OnRunEnded()
    {
        // A finite mode stops ticking on the clear frame. Finish that visual beat here.
        _terminalBeat = _target != 1f && _terminalBeatRemaining > 0f;
        if (!_terminalBeat)
        {
            SetTarget(1f);
        }
    }

    public void LateTick()
    {
        if (_pause.IsPaused)
        {
            return;
        }

        if (_terminalBeat)
        {
            _terminalBeatRemaining -= Time.unscaledDeltaTime;
            if (_terminalBeatRemaining <= 0f)
            {
                _terminalBeat = false;
                _target = 1f;
            }
        }

        Apply();
    }

    private void SetTarget(float scale)
    {
        _target = scale;
        Apply();
    }

    private void Apply()
    {
        if (!_pause.IsPaused && _appliedTarget != _target)
        {
            Time.timeScale = _target;
            _appliedTarget = _target;
        }
    }

    public void Dispose()
    {
        _started.Dispose();
        _ended.Dispose();
        _runEnded.Dispose();

        // Release a held pause before restoring the baseline so its saved scale cannot win teardown.
        _pause.Dispose();
        Time.timeScale = 1f;
    }
}
