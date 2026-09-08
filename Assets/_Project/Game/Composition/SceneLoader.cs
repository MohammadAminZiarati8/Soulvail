using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Soulvail.Game.Composition;

/// <summary>
/// The one way anything in this project changes scene: an awaitable single-mode load by name.
/// Registered in <c>BootScope</c> and injected where it is needed, so a scene change is a
/// dependency a class declares rather than a static call it can make from anywhere. See AR §7.
/// </summary>
/// <remarks>
/// <para>
/// Pure C# rather than a MonoBehaviour, even though it wraps an engine API. It owns no scene
/// object and needs no update, so being a component would only mean something has to find it —
/// which is the service-locator shape ADR-0002 exists to prevent.
/// </para>
/// <para>
/// The scene names are constants here rather than serialized fields on a scope. A name is only
/// meaningful if it matches an entry in Build Settings, and neither an Inspector string nor a
/// serialized field can promise that; keeping them in one place at least means a rename has one
/// site to fix, and <see cref="LoadAsync"/> turns a stale name into a named failure rather than
/// Unity's bare "Scene couldn't be loaded" error.
/// </para>
/// <para>
/// A bad name faults the returned task instead of throwing from the call. Both reach the same
/// caller, but only the faulted task reaches one that awaited a load started earlier — an
/// awaitable that reports some of its failures synchronously and some through the task is an
/// awaitable with two error paths to remember.
/// </para>
/// </remarks>
public sealed class SceneLoader
{
    /// <summary>The first scene in the build. Shows a splash, then hands off to <see cref="Menu"/>.</summary>
    public const string Boot = "Boot";

    /// <summary>Where a session starts and returns to between runs.</summary>
    public const string Menu = "Menu";

    /// <summary>The scene one run is played in.</summary>
    public const string Run = "Run";

    /// <summary>The name of the scene currently active.</summary>
    public string Active => SceneManager.GetActiveScene().name;

    /// <summary>
    /// Loads <paramref name="sceneName"/> in <see cref="LoadSceneMode.Single"/>, replacing
    /// everything except the objects marked <c>DontDestroyOnLoad</c> — which is what keeps the
    /// root scope alive across the transition.
    /// </summary>
    /// <param name="sceneName">A scene's name, or its full asset path — both are accepted, as
    /// they are by <see cref="SceneManager"/> itself.</param>
    /// <returns>
    /// A task that completes once the scene is loaded and activated, or one faulted with
    /// <see cref="ArgumentException"/> if <paramref name="sceneName"/> is empty or is not a
    /// scene in Build Settings.
    /// </returns>
    public Task LoadAsync(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            return Task.FromException(
                new ArgumentException("A scene name is required.", nameof(sceneName)));
        }

        if (!IsInBuild(sceneName))
        {
            // Checked up front because LoadSceneAsync's own answer to an unknown scene is a
            // Console error and a null AsyncOperation — a NullReferenceException one line later,
            // pointing here rather than at the Build Settings list that is actually wrong.
            return Task.FromException(new ArgumentException(
                $"'{sceneName}' is not a scene in Build Settings, so it cannot be loaded.",
                nameof(sceneName)));
        }

        // RunContinuationsAsynchronously, deliberately: without it the awaiting code resumes
        // inline inside AsyncOperation.completed, which Unity raises from within its own scene
        // management. Anything that then loaded a second scene would be doing it re-entrantly.
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        operation.completed += _ => completion.SetResult(true);

        return completion.Task;
    }

    /// <summary>
    /// Whether the build contains a scene by this name or at this path.
    /// </summary>
    /// <remarks>
    /// Walks the build list rather than asking <c>Application.CanStreamedLevelBeLoaded</c>, which
    /// is the obvious API for this and is inert here: it answered <c>false</c> for all three of
    /// this project's scenes, by name and by full path, with the build list correctly populated
    /// (M0-13). A guard built on it rejects every load there is.
    /// <c>SceneUtility.GetBuildIndexByScenePath</c> is honest but path-only — it answers
    /// <c>-1</c> for a bare name — so the comparison is done here against both forms.
    /// </remarks>
    private static bool IsInBuild(string sceneName)
    {
        int count = SceneManager.sceneCountInBuildSettings;

        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);

            if (path == sceneName ||
                string.Equals(Path.GetFileNameWithoutExtension(path), sceneName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
