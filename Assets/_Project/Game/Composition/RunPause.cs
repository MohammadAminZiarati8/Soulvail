using System;
using UnityEngine;

namespace Soulvail.Game.Composition;

// Who holds the pause, and the two engine globals GD §11.4 asks for. The *gate* itself is one line
// in `RunTicker` — this object owns nothing about the frame except the answer to "is anybody
// holding it", because an object that both decided the pause and skipped the tick would be two
// decisions in one place and neither testable without the other.

/// <summary>
/// Why the run is paused. One member per thing that may hold the pause, so a refusal can name the
/// holder.
/// </summary>
/// <remarks>
/// <see cref="Menu"/> had no caller until M3-09 and was written before it, because
/// <see cref="RunPause"/>'s whole contract is that a second reason is refused — and a one-member
/// enum cannot express the case the refusal exists for.
/// <para>
/// <see cref="Splash"/> is the third and it is a <em>reason</em> rather than a second level-up
/// (M5-07a-ii rules 1 and 5): <c>RunTicker.LevelUpPhase</c> raises whichever of the two is wanted
/// and never both, so a refusal there can name which screen is holding the run.
/// </para>
/// <para>
/// <see cref="Sanctum"/> is the fourth, and M6-03a's ruling rather than core's: M6-02a declined to
/// stop the run in the shop, and <c>RunTicker.SanctumPhase</c> stops it anyway, so cooldowns do not
/// recover for free and a phone set down in the shop drops to 30 fps.
/// </para>
/// </remarks>
public enum PauseReason
{
    /// <summary>A level-up is on the table and the player is choosing (GD §11.4, CH §5.1).</summary>
    LevelUp,

    /// <summary>The pause menu is open (GD §5.2, §7.3). M3-09's, and unused until then.</summary>
    Menu,

    /// <summary>
    /// CH §5.4's half-tree moment is on the table: the run has stopped to ask which class it borrows
    /// a branch from, and the choice is mandatory (M5-07a-ii).
    /// </summary>
    Splash,

    /// <summary>
    /// GD §13.3's shop is up. Untimed, so the run is gated rather than clocked slowly — M6-03a rule
    /// 4: an untimed room that keeps ticking is a room that pays you to wait.
    /// </summary>
    Sanctum,
}

/// <summary>
/// The run's pause: one holder at a time, and the two globals a paused run sets.
/// </summary>
/// <remarks>
/// <para>
/// <b>It gates the tick rather than clocking it at zero</b>, which is GD §11.4's <em>"idles the
/// simulation"</em> read literally because the literal reading is also the correct one.
/// <c>RunTicker.Tick</c> returns before the snapshot is built, so core is not ticked at all and
/// AR §18.1's frame order is untouched — that order is a statement about the inside of a tick, and
/// skipping whole frames reorders nothing. A <c>Dt = 0</c> tick would still walk the entire
/// pipeline: targeting re-resolves, the director and the flow are asked, and a <c>Weapon</c> whose
/// next swing was already due fires once on the frame the screen opens. It would also spend a
/// snapshot build per frame on the one screen GD §11.4 wants cheap.
/// </para>
/// <para>
/// <b>A gated frame therefore costs zero simulated seconds.</b> <c>RunState.Time</c> sums each
/// tick's <c>Dt</c> and a skipped frame contributes none, so GD §7.3's 40–75 s stage band measured
/// from <c>RunState.Time</c> is <em>play</em> time while a stopwatch measures play plus every
/// screen. From this task on the two are different numbers, and M3-15 has to say which it quotes
/// (ledger row 8).
/// </para>
/// <para>
/// <b>One holder, and a second reason is refused rather than counted.</b> A pause menu must not open
/// over a level-up. The alternative to refusing is a counter, which silently makes <em>"resume"</em>
/// mean <em>"resume once"</em> — and the bug that produces is a run that stays frozen after the
/// screen the player closed, with nothing to say who is still holding it.
/// </para>
/// <para>
/// <b>Both globals are read before they are written and put back to what they were</b>, the
/// <c>Screen.sleepTimeout</c> precedent in <c>RunTicker</c>. <see cref="Dispose"/> restores
/// unconditionally, because leaving the Run scene while paused must not hand the Menu a frozen
/// clock — and with domain reload disabled on Play, a session ended mid-pause would otherwise carry
/// a zero <c>timeScale</c> into the next one. <c>BootFlow</c> states the baseline for the same
/// reason.
/// </para>
/// <para>
/// The Input System stays enabled throughout and the stick keeps reading; with the tick gated it
/// moves nobody, and M3-08b's full-screen canvas takes the touches anyway.
/// </para>
/// </remarks>
public sealed class RunPause : IDisposable
{
    /// <summary>
    /// What a paused run drops to, which is the other half of GD §11.4's sentence. A menu has no
    /// fight to draw and no reason to hold a phone at 60.
    /// </summary>
    public const int PausedFrameRate = 30;

    private PauseReason? _holder;

    private float _restoreTimeScale = 1f;
    private int _restoreTargetFrameRate;

    /// <summary>Whether anything is holding the pause.</summary>
    public bool IsPaused => _holder.HasValue;

    /// <summary>Who is holding it, or null when nothing is.</summary>
    public PauseReason? Holder => _holder;

    /// <summary>
    /// Takes the pause for <paramref name="reason"/>: stops the simulation being ticked, freezes
    /// Animators and particles, and drops the frame rate.
    /// </summary>
    /// <remarks>
    /// <c>Time.timeScale</c> goes to 0 so that Animators and particles stop <em>with</em> the
    /// simulation. A gated tick freezes positions but not clips, and a Husk finishing its wind-up
    /// animation on the spot is a telegraph that is no longer a promise.
    /// </remarks>
    /// <param name="reason">Who is taking it.</param>
    /// <exception cref="InvalidOperationException">Another reason already holds it.</exception>
    public void Pause(PauseReason reason)
    {
        if (_holder.HasValue)
        {
            throw new InvalidOperationException(
                $"The run is already paused by {_holder.Value}, so {reason} cannot take it. One "
                + "holder at a time: a counter here would make Resume mean \"resume once\".");
        }

        _holder = reason;

        // Read before written, so a Resume puts back whatever was actually there rather than the
        // values this class would have guessed.
        _restoreTimeScale = Time.timeScale;
        _restoreTargetFrameRate = Application.targetFrameRate;

        Time.timeScale = 0f;
        Application.targetFrameRate = PausedFrameRate;
    }

    /// <summary>
    /// Gives the pause back and restores both globals to what they were when it was taken.
    /// </summary>
    /// <param name="reason">Who is giving it back. Must be the holder.</param>
    /// <exception cref="InvalidOperationException">
    /// Nothing holds the pause, or <paramref name="reason"/> is not what does.
    /// </exception>
    public void Resume(PauseReason reason)
    {
        if (!_holder.HasValue)
        {
            throw new InvalidOperationException(
                $"Nothing is holding the pause, so {reason} cannot resume it.");
        }

        if (_holder.Value != reason)
        {
            throw new InvalidOperationException(
                $"The pause is held by {_holder.Value}, so {reason} cannot resume it.");
        }

        Restore();
    }

    /// <summary>
    /// Restores both globals whether or not the pause is held, on the way out of the run.
    /// </summary>
    /// <remarks>
    /// Unconditional on purpose: a run left while paused — a death overlay, a scene change, the
    /// Editor leaving Play — must not hand whatever comes next a frozen clock. Idempotent, so a
    /// scope disposing twice costs nothing.
    /// </remarks>
    public void Dispose()
    {
        if (_holder.HasValue)
        {
            Restore();
        }
    }

    private void Restore()
    {
        Time.timeScale = _restoreTimeScale;
        Application.targetFrameRate = _restoreTargetFrameRate;

        _holder = null;
    }
}
