using Soulvail.Core.Content;

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
/// <b>Four members in M3, four more at M5-07a-ii, and none of M6's.</b> AR §6 has listed
/// <c>Reroll()</c>, <c>Banish(skillId)</c>, <c>BuyHeal()</c> and <c>BuyCleanse()</c> on this row
/// since M0-09, and none of the four is written here: a port grows a member when the mechanic that
/// needs it lands, not before (AR §6, §18.2). They arrive with M6-02, M6-05 and M6-06.
/// </para>
/// <para>
/// <b>CH §5.4's half-tree moment is a second pair of reads and a second pair of commands, and it is
/// deliberately not folded into the four above.</b> A moment that grants no node, spends no pick and
/// draws from no stream is a different object (M5-07a-ii rule 1), and the frame loop has to be able
/// to ask about it separately: <c>RunTicker.LevelUpPhase</c> raises <c>PauseReason.Splash</c> or
/// <c>PauseReason.LevelUp</c> and never both, so it needs each one's answer on its own.
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

    /// <summary>
    /// Whether CH §5.4's half-tree moment is owed with nothing else on the table — the one question
    /// that decides whether this frame opens the splash screen.
    /// </summary>
    /// <remarks>
    /// <b>A single question rather than four</b>, for <see cref="IsLevelUpPending"/>'s reason and
    /// with one more term than it has: the run has a tree, half of it is taken, no branch has been
    /// borrowed, there is a class to borrow from, and no offer is on the table. That last term is
    /// what stops the splash screen opening over a level-up whose second card is still up — the two
    /// cannot hold <c>RunPause</c> at once, and a frame loop reading four reads and combining them
    /// itself would be a second copy of this rule in a file that owns the frame rather than the
    /// mechanic.
    /// <para>
    /// <see langword="false"/> when no run is running, and <see langword="false"/> for ever in a
    /// build with one authored class — CH §5.4's own branch (M5-07a-ii rule 3).
    /// </para>
    /// </remarks>
    bool IsSplashPending { get; }

    /// <summary>
    /// Whether the splash screen is up — what the pause is held against.
    /// </summary>
    /// <remarks>
    /// <see cref="HasOffer"/>'s exact shape, one screen over: <see langword="false"/> when no run is
    /// running, and <see langword="false"/> again the instant the choice is made.
    /// </remarks>
    bool IsSplashOpen { get; }

    /// <summary>
    /// Raises CH §5.4's moment if it is owed: the run stops and asks which class it borrows from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Called on the frame after the tick that crossed the threshold, never inside it</b> —
    /// <see cref="OpenLevelUp"/>'s placement (M3-08a rule 4), and <b>above</b> it, because a splash
    /// and a level-up can be owed on the same frame and the splash is the rarer and more
    /// consequential of the two.
    /// </para>
    /// <para>
    /// <b>It draws nothing</b> (M5-07a-ii rule 4). Every candidate is offered in catalog order, so
    /// unlike <see cref="OpenLevelUp"/> this is idempotent in the strong sense: there is no stream
    /// position for a second call to spend.
    /// </para>
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">No run is running.</exception>
    void OpenSplash();

    /// <summary>
    /// Borrows <paramref name="branch"/> of <paramref name="characterId"/> for the rest of the run.
    /// </summary>
    /// <remarks>
    /// <b>A <c>ContentId</c> and a branch index, not a card position</b> — the asymmetry with
    /// <see cref="ChooseOffer"/> is <c>ClassCard</c>'s against <c>OfferCard</c>'s (M5-07 rule 4).
    /// A level-up card reports a position because three cards are repainted in place between picks;
    /// this screen's two pages stand for a class and one of its branches, and the id is what
    /// <c>TreeRules.InstallSplash</c> resolves the tree from.
    /// <para>
    /// There is no <c>SplashChosen</c> to send back: core publishes one (AR §8).
    /// </para>
    /// </remarks>
    /// <param name="characterId">One of <c>RunState.SplashCandidates</c>.</param>
    /// <param name="branch">Which of that class's branches, 0-based.</param>
    /// <exception cref="System.InvalidOperationException">
    /// No run is running, the class has no tree, or the moment is not open — a view reporting a tap
    /// on a card that cannot exist.
    /// </exception>
    /// <exception cref="System.ArgumentException">
    /// The class is this run's own, is not in the catalog, or has no tree; or the branch carries
    /// something this run cannot apply.
    /// </exception>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// <paramref name="branch"/> is not an index into that class's branches.
    /// </exception>
    void ChooseSplash(ContentId characterId, int branch);
}
