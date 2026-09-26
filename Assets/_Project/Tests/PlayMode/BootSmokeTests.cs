using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Tests.PlayMode.Support;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

namespace Soulvail.Tests.PlayMode;

/// <summary>
/// The few things about this game that cannot be asserted anywhere but in a running player: that a
/// cold start arrives somewhere a player can act, that the root scope survives the scene load it
/// crosses, and that Descend and a class card on that screen start a run.
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
/// <para>
/// <b>Boot's store writes to <c>persistentDataPath</c></b>, and the Descend row starts a run, which
/// saves one. <see cref="SaveShelter"/> moves the machine's saves aside before Play and back after,
/// so every row here meets a fresh install and the owner's files come back byte-identical (RS-03g).
/// </para>
/// </remarks>
[PrebuildSetup(typeof(SaveShelter))]
[PostBuildCleanup(typeof(SaveShelter))]
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
    /// The task's actual goal, end to end: on a cold-started menu, a tap on Descend and one on a
    /// class card put the player in a run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not in M0-17's Tests table, which specs the two above. Added because those two prove the
    /// half of the journey that has no button in it, and this is the half the task exists for.
    /// </para>
    /// <para>
    /// <b>Every control is the one its presenter wires, read from the field</b> (RS-03f rule 1).
    /// Until RS-03f this row tapped the first <see cref="Button"/> under the Menu, which is Continue
    /// whenever a run is on disk: green on a machine with a save, on Continue's guard and on
    /// whatever run the machine held, and red on a fresh install, where Descend has opened class
    /// select rather than starting a run since M5-07.
    /// </para>
    /// <para>
    /// The second-tap assertion is on <see cref="Selectable.interactable"/> rather than on a second
    /// invocation, because <c>onClick.Invoke</c> bypasses that flag — the flag <em>is</em> the
    /// guard, since uGUI is what refuses to route the second touch. Since M5-07 the guard is on
    /// every card rather than on Descend, which has none by design (<c>MenuPresenter.Descend</c>'s
    /// remarks). Continue's own guard is <c>ResumeFlowTests.Menu_ContinueRefusesASecondTap</c>.
    /// </para>
    /// </remarks>
    [UnityTest]
    public IEnumerator Descend_StartsARun_AndRefusesASecondTap()
    {
        yield return LoadBootAndWaitFor(SceneLoader.Menu);

        var presenter = Object.FindAnyObjectByType<MenuPresenter>();
        Assert.That(presenter, Is.Not.Null, "The Menu scene has no MenuPresenter in it.");

        Button descend = Field<Button>(presenter, "_descend");
        Assert.That(descend != null, Is.True, "The Menu's presenter has no Descend button wired.");
        Assert.That(descend.interactable, Is.True, "The Descend button starts out untappable.");

        descend.onClick.Invoke();

        ClassSelectPresenter screen = Field<ClassSelectPresenter>(presenter, "_classSelect");
        Assert.That(screen.IsOpen, Is.True, "Descend did not open the class-select screen.");
        Assert.That(
            SceneManager.GetActiveScene().name,
            Is.EqualTo(SceneLoader.Menu),
            "Descend loaded a scene. Since M5-07 the class is chosen first, one screen along.");

        ClassCard[] cards = Field<ClassCard[]>(screen, "_cards");
        ClassCard card = FirstOwned(cards);
        Assert.That(
            card != null,
            Is.True,
            "No card on the class-select screen plays a run, and the starter is never priced.");

        Field<Button>(card, "_button").onClick.Invoke();

        // Read before the load answers: RunTicker clears the slot once the run has its config.
        PendingRun pending = LifetimeScope.Find<BootScope>().Container.Resolve<PendingRun>();
        Assert.That(pending.IsSet, Is.True, "The card's tap recorded no run.");
        Assert.That(pending.CharacterId, Is.EqualTo(card.CharacterId), "The run is not the tapped class.");
        Assert.That(
            pending.Snapshot,
            Is.Null,
            "The tap resumed the run on disk rather than starting a fresh one.");

        foreach (ClassCard each in cards)
        {
            if (each != null && each.IsShown)
            {
                Assert.That(
                    each.IsInteractable,
                    Is.False,
                    "A card stayed interactable while the run was loading, so a second touch " +
                    "would start a second load.");
            }
        }

        yield return WaitFor(SceneLoader.Run);

        Assert.That(
            SceneManager.GetActiveScene().name,
            Is.EqualTo(SceneLoader.Run),
            $"The card's tap did not reach the Run scene within {TimeoutSeconds} seconds.");

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// The value of <paramref name="owner"/>'s private field <paramref name="name"/> — the control
    /// it wires, which is what a tap on screen reaches (RS-03f rule 1).
    /// </summary>
    /// <remarks>
    /// Reflection, as <c>ResumeFlowTests.Menu</c> dresses the same fields, because they are
    /// <c>[SerializeField] private</c> and a read added to the presenter for a test would be a
    /// claim about the type. A renamed field fails here by name rather than as a null further on.
    /// </remarks>
    private static T Field<T>(object owner, string name)
    {
        FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(field, Is.Not.Null, $"{owner.GetType().Name} has no field {name} any more.");

        return (T)field.GetValue(owner);
    }

    /// <summary>
    /// The first card that plays a run rather than buying a class, or null. The Oathbound on any
    /// profile, since the starter is never priced.
    /// </summary>
    private static ClassCard FirstOwned(ClassCard[] cards)
    {
        foreach (ClassCard card in cards)
        {
            if (card != null && card.State == ClassCardState.Owned)
            {
                return card;
            }
        }

        return null;
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
