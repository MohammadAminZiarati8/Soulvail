using System;
using System.Collections.Generic;

namespace Soulvail.Core.Common;

/// <summary>
/// The one generic finite state machine: game flow, enemy AI, boss phases.
/// Flat by design — no hierarchy, no guards, no transition table. A behaviour that
/// needs more than roughly eight states wants splitting, not a bigger machine.
/// </summary>
/// <remarks>
/// A <see cref="Transition"/> requested from inside a handler is deferred until that
/// handler returns, so handlers never observe a half-applied transition and the call
/// stack cannot recurse. <see cref="Tick"/> allocates nothing.
/// </remarks>
public sealed class StateMachine<TState> where TState : struct, Enum
{
    private readonly Dictionary<TState, List<Action>> _onEnter = new();
    private readonly Dictionary<TState, List<Action>> _onExit = new();
    private readonly Dictionary<TState, List<Action<float>>> _onTick = new();

    /// <summary>
    /// The current state's tick handlers, resolved on transition. Keeps <see cref="Tick"/>
    /// off the dictionary entirely, so it cannot allocate whatever the runtime's enum
    /// comparer does. Null when the current state has none.
    /// </summary>
    private List<Action<float>> _currentTickHandlers;

    /// <summary>Depth of handler invocation; transitions requested above zero are deferred.</summary>
    private int _handlerDepth;
    private bool _hasPendingTransition;
    private TState _pendingTransition;

    public StateMachine(TState initial)
    {
        Current = initial;
    }

    public TState Current { get; private set; }

    /// <summary>Seconds accumulated in <see cref="Current"/>; reset to zero on every transition.</summary>
    public float TimeInState { get; private set; }

    public bool IsStarted { get; private set; }

    public void OnEnter(TState state, Action handler) => Register(_onEnter, state, handler);

    public void OnExit(TState state, Action handler) => Register(_onExit, state, handler);

    public void OnTick(TState state, Action<float> handler)
    {
        List<Action<float>> handlers = Register(_onTick, state, handler);

        // Registering for the state we are already in must take effect immediately.
        if (EqualityComparer<TState>.Default.Equals(state, Current))
        {
            _currentTickHandlers = handlers;
        }
    }

    /// <summary>Invokes the initial state's enter handlers. Required once, before <see cref="Tick"/>.</summary>
    public void Start()
    {
        if (IsStarted)
        {
            throw new InvalidOperationException("StateMachine is already started.");
        }

        IsStarted = true;
        Invoke(_onEnter, Current);
        ApplyPendingTransition();
    }

    /// <summary>
    /// Moves to <paramref name="to"/>, running exit then enter handlers.
    /// Returns false — running nothing — when already in that state.
    /// </summary>
    public bool Transition(TState to)
    {
        if (EqualityComparer<TState>.Default.Equals(to, Current))
        {
            return false;
        }

        if (_handlerDepth > 0)
        {
            // Inside a handler: remember it and let the handler finish. Last request wins.
            _hasPendingTransition = true;
            _pendingTransition = to;
            return true;
        }

        Apply(to);
        ApplyPendingTransition();
        return true;
    }

    public void Tick(float dt)
    {
        if (!IsStarted)
        {
            throw new InvalidOperationException("StateMachine.Start() must be called before Tick().");
        }

        TimeInState += dt;

        List<Action<float>> handlers = _currentTickHandlers;
        if (handlers is not null)
        {
            _handlerDepth++;
            try
            {
                for (int i = 0; i < handlers.Count; i++)
                {
                    handlers[i](dt);
                }
            }
            finally
            {
                _handlerDepth--;
            }
        }

        ApplyPendingTransition();
    }

    private void Apply(TState to)
    {
        Invoke(_onExit, Current);

        Current = to;
        TimeInState = 0f;
        _currentTickHandlers = _onTick.TryGetValue(to, out List<Action<float>> tickHandlers) ? tickHandlers : null;

        Invoke(_onEnter, to);
    }

    /// <summary>Drains transitions requested from inside handlers, including ones they request in turn.</summary>
    private void ApplyPendingTransition()
    {
        while (_hasPendingTransition)
        {
            TState to = _pendingTransition;
            _hasPendingTransition = false;

            if (EqualityComparer<TState>.Default.Equals(to, Current))
            {
                continue;
            }

            Apply(to);
        }
    }

    private void Invoke(Dictionary<TState, List<Action>> table, TState state)
    {
        if (!table.TryGetValue(state, out List<Action> handlers))
        {
            return;
        }

        _handlerDepth++;
        try
        {
            for (int i = 0; i < handlers.Count; i++)
            {
                handlers[i]();
            }
        }
        finally
        {
            _handlerDepth--;
        }
    }

    private static List<THandler> Register<THandler>(
        Dictionary<TState, List<THandler>> table,
        TState state,
        THandler handler)
    {
        if (!table.TryGetValue(state, out List<THandler> handlers))
        {
            handlers = new List<THandler>();
            table[state] = handlers;
        }

        handlers.Add(handler);
        return handlers;
    }
}
