using System;
using Soulvail.Core.Ports;

namespace Soulvail.Game.Adapters;

/// <summary>
/// What a thumb asked for this frame, held until the frame asks. One of CC §6.2's four slot
/// buttons was pressed; <c>RunTicker.CommandPhase</c> is where that becomes a command. See
/// AR §4.3 and §18.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>The tap becomes a command in <c>CommandPhase</c>, never in uGUI's event</b> (M3-10a rule 3).
/// <c>RunTicker</c>'s own remarks say why command adapters are not <c>ITickable</c>s: <em>"the order
/// commands land in — and whether they land before or after the snapshot — would be whatever order
/// <c>RunScope</c> happened to register them in, decided by an edit somewhere else entirely and
/// invisible until something went subtly wrong."</em> A uGUI <c>Button</c> calling
/// <see cref="IPlayerCommands.CastSkill"/> straight from its handler has exactly that defect, because
/// Unity gives no order between the <c>EventSystem</c>'s <c>Update</c> and the ticker's. So the
/// button writes a slot here, the frame polls it at the one point that is written down, and
/// <em>"a tap became a command"</em> has a place rather than a time.
/// </para>
/// <para>
/// <b><see cref="TapToFocusAdapter"/>'s shape, and deliberately not four more Input System
/// actions.</b> The alternative was binding S1–S4 on the virtual gamepad the Charge already uses —
/// but that asset is generated, the Charge needs it because CC §5's 0.15 s buffer measures the age
/// of a press, and a slot press needs no buffer at all (CC §6.2 answers an early tap with dimming).
/// One small class against four bindings and a regenerated asset.
/// </para>
/// <para>
/// <b>One <see cref="int"/> and not a queue</b> (M3-10a rule 4). Two slots tapped in one frame is
/// two thumbs or a bug, and casting both would spend two cooldowns on an input the player did not
/// distinguish — so the last press wins and the other is dropped. A queue would also outlive the
/// frame, which is the cast buffer CC §6.2 explicitly answers with dimming instead.
/// </para>
/// <para>
/// <b>There is no <c>Reset</c>, and that is worth saying out loud.</b> <see cref="Poll"/> clears
/// whether or not it sent anything, so nothing survives a frame the ticker ran; and a press made
/// after the run ended is never polled at all, because <c>CommandPhase</c> sits below
/// <c>RunTicker</c>'s <c>IsRunning</c> guard (rule 10). What is left is a single stale
/// <see cref="int"/> on an object <c>RunScope</c> is about to dispose, which is not a state anything
/// can read.
/// </para>
/// <para>
/// Plain C#, not a <c>MonoBehaviour</c>: it has no frame of its own, no transform, and nothing in a
/// scene should be able to find it. <c>RunScope</c> builds it and disposes it with the run —
/// <see cref="TapToFocusAdapter"/>'s paragraph, for its reason.
/// </para>
/// </remarks>
public sealed class SkillSlotInput
{
    /// <summary>Nobody pressed anything. Negative, so it can never be mistaken for slot 0.</summary>
    private const int NoSlot = -1;

    private readonly IPlayerCommands _commands;

    /// <summary>
    /// The slot a thumb asked for since the last <see cref="Poll"/>, or <see cref="NoSlot"/>.
    /// </summary>
    private int _pressed = NoSlot;

    /// <param name="commands">
    /// The run, as the command port. Not <c>IRunSession</c>: this has no business starting or ending
    /// one — <see cref="TapToFocusAdapter"/>'s split, and the reason <c>RunTicker</c> holds two
    /// handles on the same object.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="commands"/> is null.</exception>
    public SkillSlotInput(IPlayerCommands commands)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    /// <summary>
    /// A slot button was pressed. Called by its uGUI handler, whenever in the frame that happens.
    /// </summary>
    /// <param name="slot">
    /// Which thumb position, from 0. Not validated here: core refuses a slot outside the four, and
    /// refusing it twice would put CC §6.2's ceiling in two places.
    /// </param>
    /// <remarks>
    /// The last press of a frame wins (rule 4) — see the class remarks.
    /// </remarks>
    public void Press(int slot)
    {
        _pressed = slot;
    }

    /// <summary>
    /// Sends at most one <see cref="IPlayerCommands.CastSkill"/> and clears. Called from
    /// <c>RunTicker.CommandPhase</c>, beside <c>TapToFocusAdapter.Poll</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Cleared before the send rather than after it</b>, so that a handler reached from inside
    /// <c>CastSkill</c> — core publishes <c>SkillCast</c> from in there — cannot see a press that has
    /// already been spent, and a throw on the way out cannot leave one armed for the next frame.
    /// </para>
    /// <para>
    /// Allocates nothing, and does nothing at all on the overwhelming majority of frames.
    /// </para>
    /// </remarks>
    public void Poll()
    {
        int slot = _pressed;

        _pressed = NoSlot;

        if (slot < 0)
        {
            return;
        }

        _commands.CastSkill(slot);
    }
}
