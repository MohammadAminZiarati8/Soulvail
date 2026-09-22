using System;
using NUnit.Framework;
using Soulvail.Game.Composition;
using UnityEngine;

namespace Soulvail.Tests.Game.Composition;

/// <summary>
/// <c>RunPause</c>: one holder at a time, and the two engine globals restored on every exit.
/// </summary>
/// <remarks>
/// <b>Every row here writes <c>Time.timeScale</c> and <c>Application.targetFrameRate</c>, which are
/// process-wide.</b> The fixture captures both in <c>SetUp</c> and puts them back in
/// <c>TearDown</c> unconditionally — left undone, a row that ended paused would hand a frozen clock
/// to every later fixture in the run and to the Editor itself, and the symptom would be an
/// unrelated suite timing out.
/// </remarks>
[TestFixture]
public sealed class RunPauseTests
{
    private float _timeScale;
    private int _targetFrameRate;

    [SetUp]
    public void SetUp()
    {
        _timeScale = Time.timeScale;
        _targetFrameRate = Application.targetFrameRate;
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = _timeScale;
        Application.targetFrameRate = _targetFrameRate;
    }

    // ---- One holder (rule 12) --------------------------------------------------------------------

    [Test]
    public void Pause_HoldsOneReason()
    {
        var pause = new RunPause();

        Assert.That(pause.IsPaused, Is.False);
        Assert.That(pause.Holder, Is.Null);

        pause.Pause(PauseReason.LevelUp);

        Assert.That(pause.IsPaused, Is.True);
        Assert.That(pause.Holder, Is.EqualTo(PauseReason.LevelUp));
    }

    [Test]
    public void Pause_SecondReason_Throws()
    {
        var pause = new RunPause();

        pause.Pause(PauseReason.LevelUp);

        // A pause menu must not open over a level-up. The alternative to refusing is a counter,
        // which silently makes "resume" mean "resume once".
        Assert.Throws<InvalidOperationException>(() => pause.Pause(PauseReason.Menu));

        Assert.That(pause.Holder, Is.EqualTo(PauseReason.LevelUp), "the refusal left the holder alone.");
    }

    [Test]
    public void Pause_SameReasonTwice_Throws()
    {
        // The implied guard on the dangerous half of rule 12: a second Pause from the *same* reason
        // is refused too, because the alternative is re-capturing the restore values from the
        // already-paused globals — which would make Resume put back a zero timeScale for ever.
        var pause = new RunPause();

        pause.Pause(PauseReason.LevelUp);

        Assert.Throws<InvalidOperationException>(() => pause.Pause(PauseReason.LevelUp));
    }

    [Test]
    public void Resume_WrongReason_Throws()
    {
        var pause = new RunPause();

        pause.Pause(PauseReason.LevelUp);

        Assert.Throws<InvalidOperationException>(() => pause.Resume(PauseReason.Menu));

        Assert.That(pause.IsPaused, Is.True, "a refused resume must not lower the gate.");
    }

    [Test]
    public void Resume_Unheld_Throws()
    {
        var pause = new RunPause();

        Assert.Throws<InvalidOperationException>(() => pause.Resume(PauseReason.LevelUp));
    }

    [Test]
    public void Resume_GivesThePauseBack()
    {
        var pause = new RunPause();

        pause.Pause(PauseReason.LevelUp);
        pause.Resume(PauseReason.LevelUp);

        Assert.That(pause.IsPaused, Is.False);
        Assert.That(pause.Holder, Is.Null);

        // And it can be taken again, by either reason.
        pause.Pause(PauseReason.Menu);

        Assert.That(pause.Holder, Is.EqualTo(PauseReason.Menu));

        pause.Dispose();
    }

    // ---- The two globals (rule 13) ---------------------------------------------------------------

    [Test]
    public void Pause_SetsBothGlobals()
    {
        Time.timeScale = 1f;
        Application.targetFrameRate = 60;

        var pause = new RunPause();

        pause.Pause(PauseReason.LevelUp);

        // timeScale 0 so Animators and particles stop *with* the simulation: a gated tick freezes
        // positions but not clips, and a Husk finishing its wind-up on the spot is a telegraph that
        // is no longer a promise.
        Assert.That(Time.timeScale, Is.EqualTo(0f));
        Assert.That(Application.targetFrameRate, Is.EqualTo(RunPause.PausedFrameRate));
        Assert.That(RunPause.PausedFrameRate, Is.EqualTo(30), "the other half of GD §11.4's sentence.");

        pause.Dispose();
    }

    [Test]
    public void Resume_RestoresWhatItFound()
    {
        // Deliberately not 1 and 60: the values are *read before they are written* and put back, and
        // a row that paused from the defaults would pass identically against a class that hard-coded
        // them. This is the Screen.sleepTimeout precedent in RunTicker.
        Time.timeScale = 0.5f;
        Application.targetFrameRate = 45;

        var pause = new RunPause();

        pause.Pause(PauseReason.LevelUp);
        pause.Resume(PauseReason.LevelUp);

        Assert.That(Time.timeScale, Is.EqualTo(0.5f));
        Assert.That(Application.targetFrameRate, Is.EqualTo(45));
    }

    [Test]
    public void Dispose_RestoresWhilePaused()
    {
        Time.timeScale = 1f;
        Application.targetFrameRate = 60;

        var pause = new RunPause();

        pause.Pause(PauseReason.LevelUp);
        pause.Dispose();

        // Leaving the Run scene while paused must not hand the Menu a frozen clock — and with domain
        // reload disabled on Play, a session ended mid-pause would otherwise carry timeScale 0 into
        // the next one.
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(Application.targetFrameRate, Is.EqualTo(60));
        Assert.That(pause.IsPaused, Is.False);
    }

    [Test]
    public void Dispose_WhenIdle_ChangesNothing()
    {
        Time.timeScale = 0.25f;
        Application.targetFrameRate = 24;

        var pause = new RunPause();

        pause.Dispose();

        // Idempotent, so a scope disposing twice costs nothing — and an unheld Dispose must not
        // stamp a guessed baseline over whatever the app had set.
        Assert.That(Time.timeScale, Is.EqualTo(0.25f));
        Assert.That(Application.targetFrameRate, Is.EqualTo(24));

        pause.Dispose();

        Assert.That(Time.timeScale, Is.EqualTo(0.25f));
    }

    [Test]
    public void Dispose_TwiceWhilePaused_RestoresOnce()
    {
        Time.timeScale = 1f;
        Application.targetFrameRate = 60;

        var pause = new RunPause();

        pause.Pause(PauseReason.LevelUp);
        pause.Dispose();

        // A second Dispose must not re-apply a stale capture over whatever has happened since.
        Time.timeScale = 0.75f;

        pause.Dispose();

        Assert.That(Time.timeScale, Is.EqualTo(0.75f));
    }

    [Test]
    public void Reason_HasAllThreeMembers()
    {
        // Menu had no caller until M3-09 and was written before it, because RunPause's whole
        // contract is that a second reason is refused and a one-member enum cannot express the case.
        // Splash is M5-07a-ii's, and unlike Menu it arrives with its caller: RunTicker.LevelUpPhase
        // raises it for CH §5.4's moment.
        Assert.That(Enum.GetValues(typeof(PauseReason)).Length, Is.EqualTo(3));
        Assert.That(Enum.IsDefined(typeof(PauseReason), PauseReason.Menu), Is.True);
        Assert.That(Enum.IsDefined(typeof(PauseReason), PauseReason.Splash), Is.True);
    }

    /// <summary>
    /// The two reasons <c>RunTicker</c> raises are refused against each other, which is what makes
    /// M5-07a-ii rule 5's release-before-acquire ordering load-bearing rather than tidy.
    /// </summary>
    [Test]
    public void Reason_SplashAndLevelUpCannotBothHoldIt()
    {
        var pause = new RunPause();

        pause.Pause(PauseReason.LevelUp);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => pause.Pause(PauseReason.Splash));

        Assert.That(thrown.Message, Does.Contain(nameof(PauseReason.LevelUp)));
        Assert.That(pause.Holder, Is.EqualTo(PauseReason.LevelUp), "the refusal left the holder alone.");

        // And the other way round, which is the frame a level-up becomes owed under an open splash.
        pause.Resume(PauseReason.LevelUp);
        pause.Pause(PauseReason.Splash);

        Assert.Throws<InvalidOperationException>(() => pause.Pause(PauseReason.LevelUp));
        Assert.That(pause.Holder, Is.EqualTo(PauseReason.Splash));
    }
}
