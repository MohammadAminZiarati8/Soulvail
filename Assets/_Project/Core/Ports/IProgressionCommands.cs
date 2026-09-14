namespace Soulvail.Core.Ports;

/// <summary>
/// What the player asks of their own progression, as opposed to of their character
/// (<see cref="IPlayerCommands"/>) or of the run (<see cref="IRunSession"/>). AR §6's third inbound
/// command port; <c>RunSession</c> implements all three.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separate from <see cref="IPlayerCommands"/> for that port's own reason, read one level up.</b>
/// A tap on the stick and a tap on a level-up card are both thumbs, but the objects that send them
/// have nothing in common: an input adapter holds the first for the whole run, and a screen that
/// exists for two seconds holds the second. Registering both to one instance keeps there being
/// exactly one brain (see <c>RunInstaller</c>); keeping them as two interfaces is what stops the
/// level-up screen being handed <c>MovementSkill</c> and the input adapter being handed the tree.
/// </para>
/// <para>
/// <b>Four members in M3 and none of M6's.</b> AR §6 has listed <c>Reroll()</c>,
/// <c>Banish(skillId)</c>, <c>BuyHeal()</c> and <c>BuyCleanse()</c> on this row since M0-09, and
/// none of the four is written here: a port grows a member when the mechanic that needs it lands,
/// not before (AR §6, §18.2). They arrive with M6-02, M6-05 and M6-06.
/// </para>
/// <para>
/// <b>Two of the four are reads, and they are here rather than on <c>RunState</c> because the frame
/// loop is what asks them.</b> <c>RunTicker</c> decides <em>when</em> a level-up opens (M3-08a
/// rule 4), so it needs the two questions above the decision — and <c>RunState</c>'s constructor is
/// <c>internal</c> with no <c>InternalsVisibleTo</c> (AR §18.2), which makes the state unreachable
/// to the one fixture that owns the frame order. <c>RunState</c> carries the same two reads for the
/// screens that draw them (M3-08b, M3-10); this port carries them for the object that writes the
/// frame down. Neither hands out the flow itself — see AR §18.2 and <c>LevelUpFlow</c>'s remarks.
/// </para>
/// <para>
/// The two <em>commands</em> throw when no run is running, for <see cref="IPlayerCommands"/>'
/// reason: a command arriving outside a run is a wiring mistake, and a silent no-op would hide it.
/// The two <em>reads</em> answer <see langword="false"/> instead, because they are polled every
/// frame by an object that is allowed to be ticked after a run has ended.
/// </para>
/// </remarks>
public interface IProgressionCommands
{
    /// <summary>
    /// Whether a pick is owed with no offer on the table and a tree to spend it on — the one
    /// question that decides whether this frame opens a level-up.
    /// </summary>
    /// <remarks>
    /// <b>A single question rather than three</b>, because a run whose class has no tree banks its
    /// levels and must not pause, draw or throw (M3-08a rule 5) — and that is every run in the build
    /// until M3-12. A caller asking "are picks owed?" alone would pause an empty screen for the
    /// whole of this milestone.
    /// <para>
    /// <see langword="false"/> when no run is running.
    /// </para>
    /// </remarks>
    bool IsLevelUpPending { get; }

    /// <summary>
    /// Whether an offer is currently on the table — what the gate holds the pause against.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> when no run is running, and <see langword="false"/> for an Overflow
    /// level, which is granted silently and never puts anything on a table (M3-08a rule 7).
    /// </remarks>
    bool HasOffer { get; }

    /// <summary>
    /// Draws the offer for the pick that is owed, or spends it as Overflow (CH §5.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Called on the frame after the tick that earned the level, never inside it</b>, and that
    /// ordering is the whole of M3-04's inherited rule. The boundary snapshot is taken on entering
    /// <c>Clear</c> — the same tick as a stage's last kill, which is the kill most likely to level —
    /// so it captures the <c>Offers</c> stream <em>before</em> any draw, and a run killed with a
    /// pick owed rolls the same three nodes on resume. Drawn in-tick instead, the capture would
    /// record a position the draw had already advanced, and killing the app would be a free reroll.
    /// </para>
    /// <para>
    /// Idempotent while an offer is open: calling it again leaves the same three ids and spends no
    /// draw. It loops while picks are owed and stops at the first that draws something, so at most
    /// one offer is ever on the table.
    /// </para>
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">No run is running.</exception>
    void OpenLevelUp();

    /// <summary>
    /// Takes the offered node at <paramref name="index"/> and spends the pick.
    /// </summary>
    /// <remarks>
    /// An index into the offer and not a <c>ContentId</c>, for <c>IPlayerCommands.CastSkill</c>'s
    /// reason: a card is a position on a screen, and the screen knows which one was tapped rather
    /// than what is currently under it. There is no <c>OfferChosen</c> event — <c>NodeTaken</c>
    /// already says what happened, and AR §8 asks events to describe rather than duplicate.
    /// </remarks>
    /// <param name="index">Which card, from 0, within the offer currently on the table.</param>
    /// <exception cref="System.InvalidOperationException">
    /// No run is running, or no offer is open — a view sending this without a card to send it for.
    /// </exception>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// <paramref name="index"/> is outside the offer, which may be shorter than three when the tree
    /// had fewer available nodes than that (M3-04 rule 1).
    /// </exception>
    void ChooseOffer(int index);
}
