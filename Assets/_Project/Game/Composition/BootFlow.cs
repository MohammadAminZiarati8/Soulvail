using System;
using System.Threading;
using System.Threading.Tasks;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Soulvail.Game.Composition;

/// <summary>
/// What happens once the container is up: the app's frame rate is set, and the Boot scene hands
/// off to the Menu. Registered as an entry point on <c>BootScope</c>, so it runs once per app
/// launch rather than once per scene. See AR §7.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="IStartable"/> rather than a MonoBehaviour's <c>Start</c>, because the root scope
/// is a prefab VContainer instantiates before the first scene loads: there is no scene object to
/// hang this on that would reliably exist at that moment, and an entry point gets its
/// dependencies injected by constructor like everything else in core.
/// </para>
/// <para>
/// The hand-off is conditional on Boot being the scene we are in, and that is the whole reason
/// this class is not just a line in the Boot scene. Pressing Play with the Run scene open is how
/// this game is iterated on; an unconditional load would send every one of those sessions to the
/// Menu instead. Same decision as <c>RunInstaller</c>'s unseeded-run fallback (M0-12) — the
/// direct-Play path is a first-class workflow, not an accident to be corrected.
/// </para>
/// <para>
/// The condition is checked twice: once when this starts, and again whenever a scene finishes
/// loading. Only the second one fires on a cold launch <em>into</em> Boot from somewhere else,
/// and that is not a hypothetical — the root scope is built when the app's first scene loads
/// (VContainer's <c>OnFirstSceneLoaded</c>), so under the PlayMode test runner, whose first scene
/// is its own, this had already run and returned before the test could load Boot at all (M0-17).
/// It is also what would make a later soft restart back to Boot go anywhere.
/// </para>
/// </remarks>
public sealed class BootFlow : IStartable, IDisposable
{
    /// <summary>
    /// The frame rate the whole app runs at. A constant rather than a tuning field: 60 is the
    /// target the design is written against, and M8-03's device tiering is the task that earns
    /// the right to ask for a different one.
    /// </summary>
    private const int TargetFrameRate = 60;

    private readonly SceneLoader _loader;
    private readonly ISaveStore _store;
    private readonly ProfileStore _profiles;
    private readonly SavedRun _savedRun;
    private readonly TableLocalizer _localizer;

    /// <param name="loader">Which scene the app is in, and how it leaves Boot.</param>
    /// <param name="store">The disk. Read twice here, and written nowhere.</param>
    /// <param name="profiles">
    /// Where the loaded profile goes. <c>ProfileStore</c> rather than <c>HapticsSettings</c> as of
    /// M3-09c — the profile has more than one field in it now, and handing it to one field's holder
    /// is how the others get lost (rule 3).
    /// </param>
    /// <param name="savedRun">What the disk said about a run in progress, at launch.</param>
    /// <param name="localizer">
    /// The adapter rather than the port, because this is the one caller of
    /// <see cref="TableLocalizer.SetLocale"/> (M6-10 rule 6). It starts on the device's language and
    /// is moved to the profile's here, before the Menu exists.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public BootFlow(
        SceneLoader loader, ISaveStore store, ProfileStore profiles, SavedRun savedRun,
        TableLocalizer localizer)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _savedRun = savedRun ?? throw new ArgumentNullException(nameof(savedRun));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <summary>
    /// Sets the frame rate, loads the player's profile and the run in progress, then leaves Boot
    /// for the Menu if that is where the app started, and listens for Boot being loaded later.
    /// </summary>
    public void Start()
    {
        Application.targetFrameRate = TargetFrameRate;

        // Stated rather than assumed, beside the line above and for the same reason a baseline is
        // written down once (M3-08a rule 13). RunPause takes the clock to 0 and restores what it
        // found — but **domain reload is disabled on Play**, so a Play session ended mid-pause
        // leaves the static at 0 and the next one would start frozen with nothing to say why. The
        // cost is one assignment per boot; the alternative is an Editor that occasionally will not
        // move and a morning spent on it.
        Time.timeScale = 1f;

        SceneManager.sceneLoaded += OnSceneLoaded;

        LoadProfile();
        LoadRun();

        if (_loader.Active == SceneLoader.Boot)
        {
            LeaveBoot();
        }
    }

    /// <summary>
    /// Drops the scene subscription. Called by VContainer when the root scope is disposed, which
    /// is the app shutting down.
    /// </summary>
    public void Dispose()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <remarks>
    /// The loaded scene is read from the argument rather than from <see cref="SceneLoader.Active"/>,
    /// which is the same answer for a single-mode load and the wrong one for any additive load a
    /// later milestone adds.
    /// </remarks>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SceneLoader.Boot)
        {
            LeaveBoot();
        }
    }

    /// <summary>
    /// Reads the stored profile once and hands it to the one object that holds a live one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Here, because this is the one place in the app with a legitimate reason to wait on the
    /// disk.</b> Every later reader of a setting gets a value that is already correct, and no
    /// screen has to cope with one arriving late.
    /// </para>
    /// <para>
    /// <b>To <c>ProfileStore</c>, not to a feature.</b> The profile has two fields as of M3-09c and
    /// will have more; a boot that handed each field to its own holder would need a line here per
    /// field, and every one of those holders would have to author a whole profile to write its own
    /// back (M3-09c rule 3).
    /// </para>
    /// <para>
    /// <b>The continuation is synchronous, so with today's local store the profile is applied
    /// before <see cref="LeaveBoot"/> runs on the line below.</b> Against a future asynchronous
    /// store it would land whenever the disk answered, which is the honest degradation: the
    /// settings are at GD §16.3's defaults until then, never at a wrong stored value.
    /// </para>
    /// <para>
    /// A missing profile is <see cref="PlayerProfile.Default"/> and is deliberately not written
    /// back on the spot — the first file appears when the player first changes something, and
    /// until then a fresh install has no profile, which is the truth.
    /// </para>
    /// <para>
    /// <b>The profile's language is read here, once, and nothing changes it after</b> (M6-10
    /// rule 6). The localizer was built on the device's language because the container exists
    /// before this read does. An empty locale leaves it there; any other tag moves it, and a tag
    /// this build ships no table for reads English silently, with the profile left as written so
    /// the language comes back if the table does (rule 7). Every label is written in <c>Start</c>
    /// (M3-14c rule 3), so this has to land before the Menu loads. With today's synchronous store it
    /// does.
    /// </para>
    /// </remarks>
    private void LoadProfile()
    {
        _store.LoadProfile().ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    // The app still starts, at the defaults. A profile that cannot be read is a
                    // lost preference, not a reason to refuse to launch.
                    Debug.LogError(
                        "Could not load the player profile: " +
                        task.Exception?.GetBaseException().Message);

                    return;
                }

                PlayerProfile profile = task.Result ?? PlayerProfile.Default;

                _profiles.Adopt(profile);

                if (!string.IsNullOrEmpty(profile.Locale))
                {
                    _localizer.SetLocale(profile.Locale);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Reads the run in progress once, so the Menu knows whether it has a <c>Continue</c> to offer
    /// before it is on screen (M2-14b rule 6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Here rather than in the Menu, and Boot exists for exactly this.</b> A Menu that appeared
    /// first and then grew a second button a frame later is the shape that gets tapped through by
    /// accident — the player's thumb is already moving toward where <c>Descend</c> was. The cost is
    /// a local file read of a few hundred bytes, on the one screen in the game with nothing to be
    /// smooth about.
    /// </para>
    /// <para>
    /// <b>Three different things all mean "there is nothing to continue", and all three land
    /// here.</b> A fresh install has no file; a run that ended deleted its own; and a file this
    /// build cannot read is refused and discarded by the store rather than returned half-understood
    /// (M2-13b rule 5). <see cref="SavedRun"/> is left absent for each, so the Menu has one
    /// question to ask instead of three.
    /// </para>
    /// <para>
    /// A faulted load is logged and swallowed for <see cref="LoadProfile"/>'s reason, one step
    /// sharper: a save that cannot be read has already cost the player the run, and the only thing
    /// left to decide is whether the app also refuses to start.
    /// </para>
    /// </remarks>
    private void LoadRun()
    {
        _store.LoadRun().ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError(
                        "Could not load the run in progress: " +
                        task.Exception?.GetBaseException().Message);

                    return;
                }

                if (task.Result is RunSnapshot snapshot)
                {
                    _savedRun.Set(snapshot);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void LeaveBoot()
    {
        // The task is deliberately observed rather than discarded. A faulted load here means the
        // Menu scene is missing from Build Settings, and an unobserved task exception surfaces —
        // if at all — from the finalizer thread, long after the frame that could explain it.
        _loader.LoadAsync(SceneLoader.Menu).ContinueWith(
            static task => Debug.LogException(task.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
