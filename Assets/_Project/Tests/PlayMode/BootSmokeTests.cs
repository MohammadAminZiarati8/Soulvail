using System.Collections;
using NUnit.Framework;
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer.Unity;

namespace Soulvail.Tests.PlayMode;

/// <summary>
/// The few things about this game that cannot be asserted anywhere but in a running player: that a
/// cold start arrives somewhere a player can act, that the root scope survives the scene load it
/// crosses, and that the one button on that screen starts a run.
/// </summary>
/// <remarks>
/// <para>
/// AR §15 puts the PlayMode count at "near zero", and these three are the exception it names by
/// implication: nothing in an EditMode test can fail when the Boot scene is dropped from Build
/// Settings, when <c>VContainerSettings</c> falls out of Preloaded Assets, or when a serialized
/// reference in the Menu scene comes back null — and each of those ships a build that opens on a
/// black screen with nothing in the Console.
/// </para>
/// <para>
/// Every test starts by loading Boot, so each one begins from the same place regardless of the
/// order the runner picks and regardless of the scene the one before it left behind.
/// </para>
/// </remarks>
public sealed class BootSmokeTests
{
    /// <summary>
    /// How long a scene transition is allowed to take before it counts as never having happened.
    /// Generous on purpose: this is a deadlock detector, not a performance budget.
    /// </summary>
    private const float TimeoutSeconds = 5f;

    /// <summary>The name Unity gives the scene that <c>DontDestroyOnLoad</c> objects are moved to.</summary>
    private const string DontDestroyOnLoadScene = "DontDestroyOnLoad";

    [UnityTest]
    public IEnumerator Boot_ReachesMenu_WithinFiveSeconds()
    {
        yield return LoadBootAndWaitFor(SceneLoader.Menu);

        Assert.That(
            SceneManager.GetActiveScene().name,
            Is.EqualTo(SceneLoader.Menu),
            $"Boot did not hand off to the Menu within {TimeoutSeconds} seconds. A build that does " +
            "this opens on a black screen.");

        LogAssert.NoUnexpectedReceived();
    }

    [UnityTest]
    public IEnumerator RootScope_SurvivesSceneLoad()
    {
        yield return LoadBootAndWaitFor(SceneLoader.Menu);

        LifetimeScope root = LifetimeScope.Find<BootScope>();

        Assert.That(
            root,
            Is.Not.Null,
            "No BootScope exists after the Boot to Menu transition. Either VContainerSettings is " +
            "not in Preloaded Assets, or its RootLifetimeScope is unassigned.");

        Assert.That(
            root.gameObject.scene.name,
            Is.EqualTo(DontDestroyOnLoadScene),
            "The root scope is a scene object rather than a DontDestroyOnLoad one, so the next " +
            "scene load takes the whole container with it.");
    }

    /// <summary>
    /// The task's actual goal, end to end: one tap on a cold-started menu puts the player in a run.
    /// </summary>
    /// <remarks>
    /// Not in M0-17's Tests table, which specs the two above. Added because those two prove the
    /// half of the journey that has no button in it, and this is the half the task exists for.
    /// The second-tap assertion is on <see cref="Selectable.interactable"/> rather than on a second
    /// invocation, because <c>onClick.Invoke</c> bypasses that flag — the flag <em>is</em> the
    /// guard, since uGUI is what refuses to route the second touch.
    /// </remarks>
    [UnityTest]
    public IEnumerator Descend_StartsARun_AndRefusesASecondTap()
    {
        yield return LoadBootAndWaitFor(SceneLoader.Menu);

        var presenter = Object.FindAnyObjectByType<MenuPresenter>();
        Assert.That(presenter, Is.Not.Null, "The Menu scene has no MenuPresenter in it.");

        Button descend = presenter.GetComponentInChildren<Button>();
        Assert.That(descend, Is.Not.Null, "The Menu scene has no Descend button under the presenter.");
        Assert.That(descend.interactable, Is.True, "The Descend button starts out untappable.");

        descend.onClick.Invoke();

        Assert.That(
            descend.interactable,
            Is.False,
            "Descend stayed interactable while the run was loading, so a second touch would start " +
            "a second load.");

        yield return WaitFor(SceneLoader.Run);

        Assert.That(
            SceneManager.GetActiveScene().name,
            Is.EqualTo(SceneLoader.Run),
            $"Descend did not reach the Run scene within {TimeoutSeconds} seconds.");

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// Loads Boot the way a cold launch does and waits for the app to arrive at
    /// <paramref name="sceneName"/> under its own steam.
    /// </summary>
    private static IEnumerator LoadBootAndWaitFor(string sceneName)
    {
        SceneManager.LoadScene(SceneLoader.Boot);

        // LoadScene is deferred to the end of the frame, so the scene is not there yet.
        yield return null;

        yield return WaitFor(sceneName);
    }

    /// <summary>
    /// Yields until <paramref name="sceneName"/> is the active scene, or until the timeout — which
    /// is left for the caller to assert on, so the failure names the transition rather than the
    /// helper.
    /// </summary>
    private static IEnumerator WaitFor(string sceneName)
    {
        // Unscaled and wall-clock: a test that timed out in game seconds would hang forever the
        // first time something pauses the game.
        float deadline = Time.realtimeSinceStartup + TimeoutSeconds;

        while (SceneManager.GetActiveScene().name != sceneName && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }
}
