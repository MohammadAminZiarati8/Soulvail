using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;

namespace Soulvail.Game.Adapters;

/// <summary>uGUI records gestures here; only RunTicker.CommandPhase sends commands.</summary>
public sealed class SkillArcInput
{
    public const float LongPressSeconds = 0.45f;
    private readonly IPlayerCommands _commands;
    private readonly IRunSession _session;
    private readonly IVibrator _vibrator;
    private readonly HapticsSettings _haptics;
    private ContentId _pending;
    private bool _toggle;

    public SkillArcInput(IPlayerCommands commands, IRunSession session = null,
        IVibrator vibrator = null, HapticsSettings haptics = null)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _session = session;
        _vibrator = vibrator;
        _haptics = haptics;
    }

    public void Tap(ContentId id) => Record(id, false);
    public void Toggle(ContentId id) => Record(id, true);

    private void Record(ContentId id, bool toggle)
    {
        _pending = id;
        _toggle = toggle;
    }

    public void Poll()
    {
        ContentId id = _pending;
        bool toggle = _toggle;
        _pending = default;
        if (id == default)
        {
            return;
        }

        if (!toggle)
        {
            _commands.CastSkillNow(id);
            return;
        }

        var state = _session?.State;
        if (state is null || !_session.IsRunning)
        {
            return;
        }

        for (int i = 0; i < state.TakenNodeIds.Count; i++)
        {
            if (state.TakenNodeIds[i] != id)
            {
                continue;
            }

            bool auto = !state.IsAutoCast(id);
            // Refuse silently at the six-circle capacity.
            if (!auto && state.ManualSlotCount >= Soulvail.Core.Combat.SkillRunner.MaxManualSlots)
            {
                return;
            }

            _commands.SetAutoCast(id, auto);
            if (_haptics?.Enabled == true)
            {
                _vibrator?.Pulse(20, 0.25f);
            }

            return;
        }
    }
}
