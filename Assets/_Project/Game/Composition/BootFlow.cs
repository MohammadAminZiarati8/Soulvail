using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
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
/// The hand-off is conditional on already being in Boot, and that is the whole reason this class
/// is not just a line in the Boot scene. Pressing Play with the Run scene open is how this game
/// is iterated on; an unconditional load would send every one of those sessions to the Menu
/// instead. Same decision as <c>RunInstaller</c>'s unseeded-run fallback (M0-12) — the direct-Play
/// path is a first-class workflow, not an accident to be corrected.
/// </para>
/// </remarks>
public sealed class BootFlow : IStartable
{
    /// <summary>
    /// The frame rate the whole app runs at. A constant rather than a tuning field: 60 is the
    /// target the design is written against, and M8-03's device tiering is the task that earns
    /// the right to ask for a different one.
    /// </summary>
    private const int TargetFrameRate = 60;

    private readonly SceneLoader _loader;

    /// <exception cref="ArgumentNullException"><paramref name="loader"/> is null.</exception>
    public BootFlow(SceneLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    /// <summary>
    /// Sets the frame rate, then loads the Menu — but only when this is a cold launch that
    /// started in Boot.
    /// </summary>
    public void Start()
    {
        Application.targetFrameRate = TargetFrameRate;

        if (_loader.Active != SceneLoader.Boot)
        {
            return;
        }

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
