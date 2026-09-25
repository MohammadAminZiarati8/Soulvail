using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Composition;
using Soulvail.Game.Views;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

namespace Soulvail.Tests.PlayMode;

/// <summary>
/// RS-02b rules 3, 4 and 6, and RS-03c rule 3, on the shipped <c>Run.unity</c>: a run raises the body its class names
/// under the player, one of it, injected, and the class it raised it for is the class it started.
/// </summary>
/// <remarks>
/// <para>
/// <b>PlayMode because the rule is <c>RunScope</c>'s build callback</b>, which needs the root scope
/// and the scene: <c>Configure</c> cannot run without a parent (<c>RunSceneTests</c>' remark).
/// </para>
/// <para>
/// <b>The class row sets <c>PendingRun</c> where the class-select screen would</b>, one call
/// before the load <c>ClassSelectPresenter.OnCardChosen</c> makes. Tapping the card is not the
/// question here, and on a profile that has not bought the Gravecaller its card buys rather than
/// descends. Its two cases are a class that wears the Knight and RS-03c's Ranger, which wears its
/// own body. Like <c>BootSmokeTests</c>, every row starts a run, which writes the real
/// <c>run.json</c> (ROADMAP parking lot).
/// </para>
/// </remarks>
public sealed class RunBodyTests
{
    /// <summary>How long a scene transition may take before it counts as never having happened.</summary>
    private const float TimeoutSeconds = 5f;

    private const string GravecallerId = "character.gravecaller";
    private const string RangerId = "character.ranger";

    private static readonly ContentId Oathbound = new ContentId("character.oathbound");

    /// <summary>
    /// A class that wears the Knight, and RS-03c's Ranger, which wears its own body — one case each.
    /// </summary>
    [UnityTest]
    public IEnumerator Run_WearsTheBodyItsClassNames([Values(GravecallerId, RangerId)] string classId)
    {
        SceneManager.LoadScene(SceneLoader.Boot);

        yield return null;
        yield return WaitFor(SceneLoader.Menu);

        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneLoader.Menu), "Boot did not reach the Menu.");

        IObjectResolver root = LifetimeScope.Find<BootScope>().Container;
        ContentCatalog catalog = root.Resolve<ContentCatalog>();
        var chosen = new ContentId(classId);

        root.Resolve<PendingRun>().Set(catalog.Modes[0].Id, chosen, seed: 7);

        yield return LoadTheRun();

        if (classId == RangerId)
        {
            AssertTheRunWears(chosen, "Ranger", "AC_Ranger", typeof(RangerAnimatorView));
        }
        else
        {
            AssertTheRunWearsTheKnight(chosen);
        }

        LogAssert.NoUnexpectedReceived();
    }

    [UnityTest]
    public IEnumerator Run_ADirectPlayWearsTheFirstClassesBody()
    {
        // A root left by an earlier fixture is cleared of any run it was about to start, so this is
        // the Run scene opened with nothing pending — pressing Play with Run.unity open.
        BootScope root = LifetimeScope.Find<BootScope>() as BootScope;

        if (root != null)
        {
            root.Container.Resolve<PendingRun>().Clear();
        }

        // RunInstaller.CreateRandom's warning, and the evidence that nothing was pending: the
        // direct-Play path is the only one that invents its seed.
        LogAssert.Expect(LogType.Warning, new Regex("^No pending run was set"));

        yield return LoadTheRun();

        ContentCatalog catalog = LifetimeScope.Find<BootScope>().Container.Resolve<ContentCatalog>();

        Assert.That(catalog.Characters[0].Id, Is.EqualTo(Oathbound), "The catalog's first class is the Oathbound.");

        AssertTheRunWearsTheKnight(Oathbound);

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// One body under the player, at identity: the Knight, its <see cref="PlayerAnimatorView"/>
    /// injected — and a run started as <paramref name="expected"/>.
    /// </summary>
    private static void AssertTheRunWearsTheKnight(ContentId expected) =>
        AssertTheRunWears(expected, "Knight", "AC_Player", typeof(PlayerAnimatorView));

    /// <summary>
    /// One body under the player, at identity: the prefab named <paramref name="bodyName"/>, playing
    /// <paramref name="controller"/>, its <paramref name="view"/> injected — and a run started as
    /// <paramref name="expected"/>.
    /// </summary>
    private static void AssertTheRunWears(ContentId expected, string bodyName, string controller, System.Type view)
    {
        var scope = Object.FindFirstObjectByType<RunScope>();

        Assert.That(scope, Is.Not.Null, "The Run scene has no RunScope.");

        Assert.That(
            scope.Container.Resolve<IRunSession>().State.CharacterId,
            Is.EqualTo(expected),
            "The run started as the class the body was raised for (rule 3).");

        PlayerView player = scope.Container.Resolve<PlayerView>();
        Animator[] bodies = player.GetComponentsInChildren<Animator>(true);

        Assert.That(bodies, Has.Length.EqualTo(1), "Exactly one body stands under the player.");

        Transform body = bodies[0].transform;

        Assert.That(body.parent, Is.SameAs(player.transform), "Raised directly under the PlayerView.");
        Assert.That(body.name, Is.EqualTo(bodyName), $"Bodies/{bodyName}, named after its prefab.");
        Assert.That(body.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(body.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(bodies[0].runtimeAnimatorController.name, Is.EqualTo(controller));

        Component animatorView = body.GetComponent(view);

        Assert.That(animatorView, Is.Not.Null, $"The {bodyName} carries its animator view.");
        Assert.That(
            (bool)view
                .GetField("_injected", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(animatorView),
            Is.True,
            "RunScope injected the body it raised.");
    }

    /// <summary>
    /// Loads <c>Run.unity</c> and yields until it is up and every <c>Start</c> has run.
    /// </summary>
    /// <remarks>
    /// The frame after <c>LoadScene</c> is the one the new scene replaces the old one in, so the wait
    /// below cannot be satisfied by a Run scene an earlier row left behind.
    /// </remarks>
    private static IEnumerator LoadTheRun()
    {
        SceneManager.LoadScene(SceneLoader.Run);

        yield return null;
        yield return WaitFor(SceneLoader.Run);

        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneLoader.Run), "The Run scene did not load.");

        // One frame for every Start, RunTicker's included.
        yield return null;
    }

    /// <summary>
    /// Yields until <paramref name="sceneName"/> is active or the timeout passes, which the caller
    /// asserts on. <c>BootSmokeTests.WaitFor</c>, on the wall clock for its reason.
    /// </summary>
    private static IEnumerator WaitFor(string sceneName)
    {
        float deadline = Time.realtimeSinceStartup + TimeoutSeconds;

        while (SceneManager.GetActiveScene().name != sceneName && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }
}
