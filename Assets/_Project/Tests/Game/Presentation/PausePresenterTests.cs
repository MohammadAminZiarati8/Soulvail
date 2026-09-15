using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Core.Stage;
using Soulvail.Game.Adapters;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// The pause screen: the gate's second holder, and GD §7.3's <em>"pause anywhere, instantly"</em>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over a real <c>RunSession</c>, a real <c>DomainEventHub</c> and a real <c>RunPause</c></b> —
/// <c>LevelUpPresenterTests</c>' shape. The pause is the object this screen shares with
/// <c>RunTicker</c>, and a fake one would let every row about <em>who is holding it</em> pass against
/// a screen that holds nothing.
/// </para>
/// <para>
/// <b>The screen under test is the shipped prefab</b>, instantiated per row, rather than a hierarchy
/// this file builds. Hand-building one would test a second screen that happens to resemble the
/// asset, and the failure Traps §5 describes — a component that deserialises as null with nothing
/// reporting it — is invisible to a fixture that never loads the asset.
/// </para>
/// <para>
/// <b>This screen owns both halves of its pause, and M3-08b's owns neither</b>, which is the one
/// thing a reader of both files has to be told rather than left to infer. The level-up gate is
/// <c>RunTicker.LevelUpPhase</c>'s because <em>stopped</em> is a once-a-frame pure function of
/// <c>HasOffer</c> there; a pause menu has no core state behind it at all, so there is nothing for a
/// ticker to read and the halves belong here. The rows below hold that line from both directions:
/// this presenter raises and lowers <c>PauseReason.Menu</c>, and it never touches
/// <c>PauseReason.LevelUp</c> even when that is what is holding the pause.
/// </para>
/// <para>
/// <b>The loader records instead of loading.</b> <c>SceneManager.LoadSceneAsync</c> cannot be driven
/// from an EditMode test without taking the Editor's open scene with it (<c>ResumeFlowTests</c>'
/// finding, which is why its own two taps are the owner's manual step). <c>SceneLoader.LoadAsync</c>
/// is <c>virtual</c> as of this task so that <see cref="RecordingLoader"/> can capture
/// <c>Time.timeScale</c> and the pause holder <em>at the moment the load is asked for</em> — which is
/// the ordering rule 7 is about, and is not observable from outside the call.
/// </para>
/// <para>
/// <c>Awake</c> and <c>Start</c> do not run in EditMode (Traps §5), so the guards in
/// <c>PausePresenter.Start</c> never fire here and the prefab's own alpha 0 is what leaves the panel
/// down at the top of each row. A frame — and <c>Start</c>'s <c>Place</c> — is passed explicitly
/// where a row needs one.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PausePresenterTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/Pause.prefab";
    private const string HudPrefabPath = "Assets/_Project/Prefabs/UI/Hud.prefab";
    private const string LevelUpPrefabPath = "Assets/_Project/Prefabs/UI/LevelUp.prefab";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string TreeId = "tree.oathbound";
    private const string ArenaId = "arena.pillars";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    private const float Frame = 1f / 60f;

    /// <summary>The icon's authored side, in dp. The prefab's value and the field's default.</summary>
    private const float IconDp = 44f;

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    /// <summary>Branch a's whole first tier, so all three are available on the first pick.</summary>
    private const string NodeOne = "skill.test.one";
    private const string NodeTwo = "skill.test.two";
    private const string NodeThree = "skill.test.three";

    /// <summary>Branch b and branch c, one node each.</summary>
    private const string NodeFour = "skill.test.four";
    private const string NodeFive = "skill.test.five";

    private DomainEventHub _hub;
    private RecordingEvents _spy;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private RunSession _session;

    private RunPause _pause;
    private RecordingLoader _loader;

    private GameObject _screen;
    private PausePresenter _presenter;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<RunPause> _pauses = new List<RunPause>();
    private readonly List<IDisposable> _disposables = new List<IDisposable>();
    private readonly List<string> _directories = new List<string>();

    private float _restoreTimeScale;
    private int _restoreTargetFrameRate;

    [SetUp]
    public void CreateWorld()
    {
        // The Editor's own globals. RunPause writes both and the teardown disposes every pause this
        // fixture builds, but a row that failed part way through would otherwise leave the whole
        // suite after it running at timeScale 0 — LevelUpPresenterTests' argument, and the frame
        // rate joins it here because this is the first fixture whose rows assert on it.
        _restoreTimeScale = Time.timeScale;
        _restoreTargetFrameRate = Application.targetFrameRate;

        _hub = new DomainEventHub();
        _spy = new RecordingEvents();
        _random = new FixedRandom(7, Alternating(8_192));
        _clock = new FixedClock(Instant);

        _pause = NewPause();
        _loader = new RecordingLoader(_pause);
    }

    [TearDown]
    public void DestroyWorld()
    {
        for (int i = _disposables.Count - 1; i >= 0; i--)
        {
            _disposables[i].Dispose();
        }

        _disposables.Clear();

        foreach (RunPause pause in _pauses)
        {
            pause.Dispose();
        }

        _pauses.Clear();

        Time.timeScale = _restoreTimeScale;
        Application.targetFrameRate = _restoreTargetFrameRate;

        foreach (GameObject go in _spawned)
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        _spawned.Clear();

        foreach (string directory in _directories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        _directories.Clear();

        _hub?.Dispose();
    }

    // ---- Both halves of the pause, in this file (rules 2, 3) -----------------------------------

    [Test]
    public void Icon_OpensAndHoldsTheMenuPause()
    {
        StartRun(level: 2, pending: 0);
        BuildScreen();

        Assert.That(_pause.IsPaused, Is.False, "the fixture's premise: nothing holds the pause.");

        TapIcon();

        // **The first caller PauseReason.Menu has ever had.** M3-08a wrote the member so that
        // RunPause's contract — a second reason is refused — could be expressed by an enum with
        // something to refuse; this is the task that gives it something to refuse *with*.
        Assert.That(_pause.IsPaused, Is.True, "the icon did not stop the run.");
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Menu));

        Assert.That(_presenter.IsOpen, Is.True, "IsOpen disagrees with the pause it is meant to be.");
        Assert.That(Alpha(), Is.EqualTo(1f), "the panel stayed down over a stopped run.");
        Assert.That(Panel().blocksRaycasts, Is.True, "the panel does not take the touches under it.");
    }

    [Test]
    public void Open_StopsTheRun()
    {
        StartRun(level: 2, pending: 0);
        BuildScreen();

        float before = _session.State.Time;

        TickLikeTheTicker();

        Assert.That(
            _session.State.Time,
            Is.GreaterThan(before),
            "the fixture's premise: an ungated frame moves the clock.");

        TapIcon();

        float atPause = _session.State.Time;

        // Two frames of exactly what RunTicker.Tick does with a held pause: it returns before the
        // snapshot is built (M3-08a rule 4), so core is not ticked at all. **The gate is copied here
        // rather than reached for, and that is the row's point** — nothing in PausePresenter skips a
        // tick, and nothing in it may; this file raises a flag and draws a panel.
        TickLikeTheTicker();
        TickLikeTheTicker();

        Assert.That(
            _session.State.Time,
            Is.EqualTo(atPause),
            "a gated frame moved the simulated clock, so RunState.Time is no longer play time.");

        Assert.That(_session.IsRunning, Is.True, "the pause ended the run rather than stopping it.");
    }

    [Test]
    public void Resume_LowersItAndTheRunContinues()
    {
        Time.timeScale = 1f;
        Application.targetFrameRate = 60;

        StartRun(level: 2, pending: 0);
        BuildScreen();

        TapIcon();

        Assert.That(_pause.IsPaused, Is.True, "the fixture's premise.");

        TapResume();

        Assert.That(Alpha(), Is.Zero, "the panel stayed up over a running game.");
        Assert.That(Panel().blocksRaycasts, Is.False, "the panel still eats the touches under it.");
        Assert.That(_presenter.IsOpen, Is.False);

        // Both halves in this file: Pause(Menu) in Open, Resume(Menu) in Close. A pause raised in one
        // class and lowered in another is how a screen ends up closed over a frozen game.
        Assert.That(_pause.IsPaused, Is.False, "Resume closed the panel and left the run frozen.");
        Assert.That(_pause.Holder, Is.Null);
        Assert.That(Time.timeScale, Is.EqualTo(1f));

        float before = _session.State.Time;

        TickLikeTheTicker();

        Assert.That(
            _session.State.Time,
            Is.GreaterThan(before),
            "the run stopped being ticked once the panel closed.");
    }

    // ---- The icon is live only when nothing holds the pause (rule 4) ----------------------------

    [Test]
    public void Icon_IsNotInteractableWhileTheLevelUpScreenHolds()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen();

        // What the running game does: RunTicker.LevelUpPhase publishes the offer and raises
        // PauseReason.LevelUp, both inside one frame, before anything else is ticked.
        _session.OpenLevelUp();
        _pause.Pause(PauseReason.LevelUp);

        PassAFrame();

        Assert.That(
            Icon().interactable,
            Is.False,
            "the pause icon is live over a level-up, so a thumb can reach it.");

        // **And the guard behind the guard.** RunPause.Pause throws on a second reason (M3-08a rule
        // 12) and that throw is deliberately loud — a pause menu must never be the thing that trips
        // it. In practice M3-08b's canvas sorts above this one and takes the touch anyway; this is
        // the brace that holds when a later screen forgets its scrim.
        Assert.That(() => _presenter.Open(), Throws.Nothing, "Open let RunPause's refusal escape.");

        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.LevelUp), "the menu took the level-up's pause.");
        Assert.That(_presenter.IsOpen, Is.False);
        Assert.That(Alpha(), Is.Zero, "the pause panel drew itself over the level-up screen.");
    }

    [Test]
    public void Icon_ReturnsWhenTheLevelUpCloses()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen();

        _session.OpenLevelUp();
        _pause.Pause(PauseReason.LevelUp);

        PassAFrame();

        Assert.That(Icon().interactable, Is.False, "the fixture's premise.");

        _session.ChooseOffer(0);

        Assert.That(_session.State.HasOffer, Is.False, "the fixture's premise: nothing more is owed.");

        // The ticker's next frame, which is where the level-up's half is released.
        _pause.Resume(PauseReason.LevelUp);

        PassAFrame();

        Assert.That(
            Icon().interactable,
            Is.True,
            "the icon stayed dead after the level-up let go, so the run can never be paused again.");

        // And it is not merely enabled — it works.
        TapIcon();

        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Menu));
    }

    // ---- RunPause owns the globals and this file writes neither (rule 3) -----------------------

    [Test]
    public void Open_SetsNoGlobalsItself()
    {
        Time.timeScale = 1f;
        Application.targetFrameRate = 60;

        StartRun(level: 2, pending: 0);
        BuildScreen();

        TapIcon();

        // They moved, and the values are RunPause's: timeScale 0 so Animators and particles freeze
        // *with* the simulation, and GD §11.4's 30 fps under a screen with no fight to draw.
        Assert.That(Time.timeScale, Is.Zero, "the run was stopped and the clock kept running.");
        Assert.That(Application.targetFrameRate, Is.EqualTo(RunPause.PausedFrameRate));

        // **Now prove this file is not a second writer.** Two sentinels nothing in the game would
        // ever set, then every path the presenter has while it is open: a redundant Open and a frame
        // of its own Update. A second writer is how one of the two globals stops being restored.
        Time.timeScale = 0.5f;
        Application.targetFrameRate = 77;

        _presenter.Open();
        PassAFrame();

        Assert.That(Time.timeScale, Is.EqualTo(0.5f), "PausePresenter wrote Time.timeScale.");
        Assert.That(Application.targetFrameRate, Is.EqualTo(77), "PausePresenter wrote targetFrameRate.");

        _presenter.Close();

        // And what comes back is what RunPause captured when it took the pause, not the pair this
        // fixture wrote a moment ago — which is only true if RunPause is the only writer there is.
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(Application.targetFrameRate, Is.EqualTo(60));
    }

    [Test]
    public void Open_ShowsInstantly()
    {
        StartRun(level: 2, pending: 0);
        BuildScreen();

        Assert.That(Alpha(), Is.Zero, "the fixture's premise: the prefab ships with the panel down.");

        TapIcon();

        // Alpha 1 on the same call. GD §7.3 says *instantly*, and timeScale is 0 the moment the
        // pause is held — a scaled animation would freeze half-played and an unscaled one is a
        // second clock (M3-08b rule 4's argument, and the same answer).
        Assert.That(Alpha(), Is.EqualTo(1f), "the panel faded in rather than appearing.");

        Assert.That(
            _screen.GetComponentInChildren<Animator>(true),
            Is.Null,
            "the pause screen carries an Animator, which cannot run at timeScale 0.");

        Assert.That(
            typeof(PausePresenter)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => typeof(System.Collections.IEnumerator).IsAssignableFrom(m.ReturnType)),
            Is.Empty,
            "PausePresenter declares a coroutine. Nothing here may wait: the engine clock is "
                + "stopped and an unscaled one is a second clock (rule 5).");
    }

    // ---- The asset (Traps §5, rules 6, 11) ------------------------------------------------------

    [Test]
    public void Panel_HasResumeAndQuitOnly()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        // Traps §5: a MonoBehaviour declared with a file-scoped namespace compiles and is never
        // linked to a MonoScript, so the component would come back null here with nothing reporting
        // an error anywhere. GetComponent answering is half of what this row proves.
        var presenter = prefab.GetComponent<PausePresenter>();

        Assert.That(
            presenter,
            Is.Not.Null,
            "PausePresenter did not load off its own prefab — the usual cause is a file-scoped "
                + "namespace on a UnityEngine.Object type (Traps §5).");

        // **Read back off the saved asset**, not off an object this fixture just dressed. A prefab
        // whose references never bound looks identical in memory to one that did, right up until it
        // is loaded in a build.
        var so = new SerializedObject(presenter);

        Assert.That(so.FindProperty("_icon").objectReferenceValue, Is.Not.Null, "no icon button.");
        Assert.That(so.FindProperty("_panel").objectReferenceValue, Is.Not.Null, "no panel CanvasGroup.");
        Assert.That(so.FindProperty("_resume").objectReferenceValue, Is.Not.Null, "no Resume button.");
        Assert.That(so.FindProperty("_quit").objectReferenceValue, Is.Not.Null, "no Quit button.");

        var panel = (CanvasGroup)so.FindProperty("_panel").objectReferenceValue;

        // **Rule 6, and the row two later tasks each extend by one.** M3-09b adds Skills and M3-09c
        // adds View Tree, as a small edit to this prefab and this file each. A button that does
        // nothing is worse than a button that is not there yet, and shipping three dead buttons now
        // would make two later tasks look like they did nothing.
        Button[] panelButtons = panel.GetComponentsInChildren<Button>(true);

        Assert.That(
            panelButtons.Length,
            Is.EqualTo(2),
            "the pause panel does not hold exactly Resume and Quit. M3-09b and M3-09c each make "
                + "this three and then four — if that is what happened, move this number with them.");

        Assert.That(
            prefab.GetComponentsInChildren<Button>(true).Length,
            Is.EqualTo(3),
            "the prefab holds a button that is neither the icon nor one of the panel's two.");

        // Keys, not English. ILocalizer has no implementation until M3-14a, and OfferCard set the
        // precedent one task ago — ADR-0012, ledger row 9. HudPresenter's remarks call the raw
        // English exception closed at two places (its own overlay and MenuPresenter), and this
        // screen is deliberately not a third.
        foreach (Button button in panelButtons)
        {
            string text = button.GetComponentInChildren<TMP_Text>(true).text;

            Assert.That(
                text,
                Does.StartWith("ui.pause."),
                $"{button.name}'s label is not a LocKey, so this screen is a third place raw "
                    + "English is typed into a prefab (ledger row 9).");

            Assert.That(text, Does.Not.Contain(" "), $"{button.name}'s label is a sentence, not a key.");
        }

        // Its own canvas, between the HUD's and the level-up's: above the HUD so the panel covers
        // the stick and the Charge button, below the level-up so rule 4's belt is a sorting order
        // rather than a promise.
        var canvas = prefab.GetComponent<Canvas>();

        Assert.That(canvas, Is.Not.Null, "the screen is not its own canvas.");

        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
        var levelUp = AssetDatabase.LoadAssetAtPath<GameObject>(LevelUpPrefabPath);

        Assert.That(
            canvas.sortingOrder,
            Is.GreaterThan(hud.GetComponentInChildren<Canvas>(true).sortingOrder),
            "the pause screen sorts at or below the HUD, so its panel cannot cover the stick.");

        Assert.That(
            canvas.sortingOrder,
            Is.LessThan(levelUp.GetComponentInChildren<Canvas>(true).sortingOrder),
            "the pause screen sorts at or above the level-up, so its icon draws over the cards.");

        // Rule 11's mechanism: the icon hangs under a safe-area rect, so a notch that eats one of
        // the two top corners in landscape moves it rather than covering it.
        var fitter = prefab.GetComponentInChildren<SafeAreaFitter>(true);

        Assert.That(fitter, Is.Not.Null, "no SafeAreaFitter, so the icon can land under a cutout.");

        var icon = (Button)so.FindProperty("_icon").objectReferenceValue;

        Assert.That(
            icon.transform.parent,
            Is.EqualTo(fitter.transform),
            "the icon does not hang under the safe area, so nothing insets it on a notched phone.");
    }

    // ---- Quit (rules 7, 8) -----------------------------------------------------------------------

    [Test]
    public void Quit_LowersThePauseThenLoads()
    {
        Time.timeScale = 1f;
        Application.targetFrameRate = 60;

        StartRun(level: 2, pending: 0);
        BuildScreen();

        TapIcon();

        Assert.That(Time.timeScale, Is.Zero, "the fixture's premise.");

        TapQuit();

        Assert.That(_loader.Asked, Is.EqualTo(new[] { SceneLoader.Menu }), "Quit asked for another scene.");

        // **The ordering is the whole rule, and it is only observable from inside the call.**
        // RunPause.Dispose is the backstop rather than the mechanism (M3-08b rule 12's reasoning):
        // VContainer orders no two disposals, so this file must not be the only thing that restores
        // a zeroed timeScale — and it must not skip doing so either.
        Assert.That(
            _loader.PausedAtCall.Single(),
            Is.False,
            "the load was asked for while the pause was still held.");

        Assert.That(
            _loader.TimeScaleAtCall.Single(),
            Is.EqualTo(1f),
            "the Menu was handed a frozen clock, which a scene load is not a place to find out.");

        Assert.That(_pause.IsPaused, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(_presenter.IsOpen, Is.False);
    }

    [Test]
    public void Quit_SecondTapStartsNoSecondLoad()
    {
        StartRun(level: 2, pending: 0);
        BuildScreen();

        TapIcon();

        // Two taps in one frame: uGUI dispatches both from one EventSystem pass, and onClick.Invoke
        // fires whether or not the button is interactable — which is exactly what makes the latch
        // rather than the button the thing under test here. A helper that checked `interactable`
        // first would make this row pass against a presenter with no latch at all.
        TapQuit();
        TapQuit();

        Assert.That(_loader.Asked.Count, Is.EqualTo(1), "the second tap started a second scene load.");

        Assert.That(
            Field<Button>(_presenter, "_quit").interactable,
            Is.False,
            "Quit is still live while the scene it asked for is coming in.");

        Assert.That(
            Field<Button>(_presenter, "_resume").interactable,
            Is.False,
            "Resume is still live during a Quit — MenuPresenter's argument with the nouns swapped.");
    }

    [Test]
    public void Quit_LeavesTheRunOnDisk()
    {
        string directory = NewDirectory();
        var store = new CountingStore(new LocalJsonSaveStore(directory));

        // Built before the run starts, because the opening snapshot is published from inside
        // RunSession.Start — SaveWriter's own reason for subscribing in its constructor.
        Track(new SaveWriter(store, _hub));

        StartRun(level: 4, pending: 0, stage: 3);

        string runPath = Path.Combine(directory, LocalJsonSaveStore.RunFileName);

        Assert.That(store.RunWriteCount, Is.GreaterThanOrEqualTo(1), "the fixture's premise: a run was written.");
        Assert.That(File.Exists(runPath), Is.True, "the fixture's premise: there is a file to keep.");

        BuildScreen();

        TapIcon();
        TapQuit();

        // **Quitting is not dying, and this is the row that makes that checkable rather than
        // hopeful.** SaveWriter clears the file on PlayerDied and deliberately not on RunEnded
        // (SaveWriter.cs:128), so leaving through this panel ends the session and leaves the last
        // boundary's snapshot where Continue can find it. This task adds no deletion and no write.
        Assert.That(store.ClearCount, Is.Zero, "Quit deleted the player's run.");
        Assert.That(store.RunWriteCount, Is.EqualTo(1), "Quit wrote a save of its own.");
        Assert.That(File.Exists(runPath), Is.True, "the run file is gone after a Quit.");

        RunSnapshot? reread = store.LoadRun().GetAwaiter().GetResult();

        Assert.That(reread.HasValue, Is.True, "the file no longer decodes.");
        Assert.That(reread.Value.StageIndex, Is.EqualTo(3));
        Assert.That(reread.Value.Level, Is.EqualTo(4));

        // **And the contrast, without which the row above proves nothing.** The same store, the same
        // writer, one event later: a death does clear it. So ClearCount staying at zero through a
        // Quit is this screen's doing rather than a store that never clears anything.
        _hub.Publish(new PlayerDied(_session.State.Time));

        Assert.That(store.ClearCount, Is.EqualTo(1), "a death did not clear the run either.");
        Assert.That(File.Exists(runPath), Is.False);
    }

    // ---- Death, underneath a screen that stops the world (rule 9) -------------------------------

    [Test]
    public void Death_ClosesAndRetiresTheIcon()
    {
        Time.timeScale = 1f;

        StartRun(level: 2, pending: 0);
        BuildScreen();

        TapIcon();

        Assert.That(_presenter.IsOpen, Is.True, "the fixture's premise.");

        // Death is the one thing that can happen underneath a screen that stops the world: the tick
        // that killed the player completes before the pause takes effect (M3-08a rule 11).
        _hub.Publish(new PlayerDied(12.5f));

        Assert.That(Alpha(), Is.Zero, "the pause panel stayed up over the death overlay.");
        Assert.That(_presenter.IsOpen, Is.False);
        Assert.That(_pause.IsPaused, Is.False, "the run was left frozen under a death overlay.");
        Assert.That(Time.timeScale, Is.EqualTo(1f));

        // Gone, not merely greyed — a pause icon over a death overlay is two screens arguing about
        // whose tap it was.
        Assert.That(Icon().gameObject.activeSelf, Is.False, "the icon survived the death.");

        PassAFrame();
        PassAFrame();

        Assert.That(Icon().gameObject.activeSelf, Is.False, "a later frame put the icon back.");

        _presenter.Open();

        Assert.That(_presenter.IsOpen, Is.False, "the screen reopened over a run that has ended.");
        Assert.That(_pause.IsPaused, Is.False);
    }

    // ---- Lifetime -------------------------------------------------------------------------------

    [Test]
    public void Subscribes_FromConstruct()
    {
        // The run starts *first*, and the screen is composed after it — which is the order RunScope's
        // Awake and this component's OnEnable can happen in, since Unity orders no two Awake calls.
        // An OnEnable subscription would have been made against a hub that did not exist yet.
        StartRun(level: 2, pending: 0);

        Assert.That(_spy.Count<RunStarted>(), Is.EqualTo(1), "the fixture's premise.");

        BuildScreen();

        _hub.Publish(new PlayerDied(1f));

        Assert.That(
            Icon().gameObject.activeSelf,
            Is.False,
            "a presenter constructed after RunStarted heard nothing, so the subscription is not "
                + "being made in Construct (rule 9).");
    }

    [Test]
    public void Destroy_DropsSubscriptions()
    {
        StartRun(level: 2, pending: 0);
        BuildScreen();

        Assert.That(_hub.SubscriberCount<PlayerDied>(), Is.EqualTo(1), "the fixture's premise.");

        // **Invoked rather than waited for**, for LevelUpPresenterTests' reason: Unity does not call
        // OnDestroy on an object whose Awake never ran, and Awake does not run in EditMode (Traps
        // §5) — so DestroyImmediate alone would leave the subscription in place and this row would
        // pass against a presenter with no OnDestroy at all.
        Destroy();

        Object.DestroyImmediate(_screen);

        Assert.That(() => _hub.Publish(new PlayerDied(2f)), Throws.Nothing);

        Assert.That(_hub.SubscriberCount<PlayerDied>(), Is.Zero);
    }

    [Test]
    public void Destroy_WhilePaused_LeavesTheAppRunnable()
    {
        Time.timeScale = 1f;

        StartRun(level: 2, pending: 0);
        BuildScreen();

        TapIcon();

        Assert.That(Time.timeScale, Is.Zero, "the fixture's premise.");

        // The screen dies with the pause still held, and its OnDestroy deliberately never runs —
        // the claim is that the app comes back even then, which is the worse of the two cases.
        // VContainer orders no two disposals, so this file must not be the only thing that restores
        // a zeroed timeScale; RunPause.Dispose restores both globals unconditionally, which is what
        // stops the Menu inheriting a frozen clock with domain reload disabled on Play.
        Object.DestroyImmediate(_screen);

        _pause.Dispose();

        Assert.That(Time.timeScale, Is.EqualTo(1f), "the app was left frozen with no screen up.");
        Assert.That(_pause.IsPaused, Is.False);
    }

    // ---- Placement (rule 11) ---------------------------------------------------------------------

    [Test]
    public void Icon_IsPlacedInDpFromTheSafeArea()
    {
        StartRun(level: 2, pending: 0);
        BuildScreen();

        var canvas = _screen.GetComponent<Canvas>();
        canvas.scaleFactor = 2f;

        Assert.That(
            canvas.scaleFactor,
            Is.EqualTo(2f),
            "the fixture's premise: the canvas kept the scale this row set on it.");

        Place();

        // SkillButton's argument: a Scale-With-Screen-Size canvas measures in reference pixels, and
        // a 44 dp icon authored as 44 of those is a different physical size on every phone — and
        // GD §5.2 is making a claim about a thumb, which is a physical object.
        float pxPerDp = StickShaper.PixelsPerDp(Screen.dpi) / 2f;

        RectTransform rect = IconRect();

        Assert.That(rect.sizeDelta.x, Is.EqualTo(IconDp * pxPerDp).Within(0.001f));
        Assert.That(rect.sizeDelta.y, Is.EqualTo(IconDp * pxPerDp).Within(0.001f));

        // Top-right, anchored and pivoted there, so SafeAreaFitter's inset moves it rather than
        // clipping it — in landscape the cutout eats exactly one of the two top corners.
        Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(1f, 1f)));
        Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(1f, 1f)));
        Assert.That(rect.pivot, Is.EqualTo(new Vector2(1f, 1f)));

        Assert.That(rect.anchoredPosition.x, Is.EqualTo(-16f * pxPerDp).Within(0.001f));
        Assert.That(rect.anchoredPosition.y, Is.EqualTo(-16f * pxPerDp).Within(0.001f));

        Assert.That(
            rect.parent.GetComponent<SafeAreaFitter>(),
            Is.Not.Null,
            "the icon's parent is not the safe-area rect, so nothing insets it.");
    }

    // ---- Guard rows ------------------------------------------------------------------------------

    [Test]
    public void Construct_RefusesNullDependencies()
    {
        StartRun(level: 2, pending: 0);
        BuildScreen();

        Assert.That(
            () => _presenter.Construct(null, _pause, _hub, _loader),
            Throws.ArgumentNullException);

        Assert.That(
            () => _presenter.Construct(_session, null, _hub, _loader),
            Throws.ArgumentNullException);

        Assert.That(
            () => _presenter.Construct(_session, _pause, null, _loader),
            Throws.ArgumentNullException);

        Assert.That(
            () => _presenter.Construct(_session, _pause, _hub, null),
            Throws.ArgumentNullException);
    }

    [Test]
    public void Place_IgnoresANonFiniteDpField()
    {
        StartRun(level: 2, pending: 0);
        BuildScreen();

        RectTransform rect = IconRect();

        Vector2 sizeBefore = rect.sizeDelta;
        Vector2 positionBefore = rect.anchoredPosition;

        // **The float doors on this screen.** Both dp fields are serialized so the owner can tune the
        // target on a device (rule 11, ledger row 4), which makes them Inspector doors rather than
        // constants — and a NaN reaching sizeDelta is a RectTransform that never renders again,
        // which here is a pause icon that cannot be found on a run that cannot be paused.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -12f })
        {
            SetPrivate(_presenter, "_iconSizeDp", bad);

            Assert.That(() => Place(), Throws.Nothing, $"Place threw on an icon size of {bad}.");

            Assert.That(
                float.IsFinite(rect.sizeDelta.x) && float.IsFinite(rect.sizeDelta.y),
                Is.True,
                $"an icon size of {bad} wrote a non-finite sizeDelta.");

            Assert.That(
                rect.sizeDelta,
                Is.EqualTo(sizeBefore),
                $"an icon size of {bad} was written through rather than leaving the prefab's "
                    + "authored layout alone.");
        }

        SetPrivate(_presenter, "_iconSizeDp", IconDp);

        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -12f })
        {
            SetPrivate(_presenter, "_iconMarginDp", new Vector2(bad, 16f));

            Assert.That(() => Place(), Throws.Nothing, $"Place threw on a margin of {bad}.");

            Assert.That(
                rect.anchoredPosition,
                Is.EqualTo(positionBefore),
                $"a margin of {bad} was written through into the icon's position.");

            Assert.That(rect.sizeDelta, Is.EqualTo(sizeBefore), $"a margin of {bad} resized the icon.");
        }

        // A zero margin is allowed — an icon flush to the safe area's corner is a layout, not a
        // fault — so the margin's door is one notch wider than the size's and is checked separately.
        SetPrivate(_presenter, "_iconMarginDp", Vector2.zero);

        Assert.That(() => Place(), Throws.Nothing);
        Assert.That(rect.anchoredPosition, Is.EqualTo(Vector2.zero), "a zero margin was refused.");
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>
    /// Starts a resumed run, so a row can be at level 4 on stage 3 without playing three stages.
    /// </summary>
    /// <remarks>
    /// Resumed rather than fresh for <c>LevelUpPresenterTests</c>' reason, and
    /// <paramref name="level"/> has to account for what is taken and what is owed: M3-08a rule 9's
    /// identity refuses a saved run whose level cannot explain its own nodes.
    /// </remarks>
    private void StartRun(int level, int pending, string[] taken = null, int stage = 1)
    {
        IReadOnlyList<SkillSpec> skills = new[]
        {
            Node(NodeOne, SkillKind.Passive),
            Node(NodeTwo, SkillKind.Active),
            Node(NodeThree, SkillKind.Passive),
            Node(NodeFour, SkillKind.Passive),
            Node(NodeFive, SkillKind.Passive),
        };

        _catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Husk() },
            new[] { Mode() },
            skills,
            new[] { Tree() });

        // The hub is the session's own IDomainEvents, so the screen hears what core published rather
        // than what the fixture decided to say. The spy rides alongside for counting.
        var events = new ForkedEvents(_hub, _spy);

        _session = new RunSession(
            _catalog,
            _random,
            events,
            new RecordingIntents(),
            new RunRecorder(_random, _clock, events),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        ContentId[] takenIds = (taken ?? Array.Empty<string>())
            .Select(id => new ContentId(id))
            .ToArray();

        var snapshot = new RunSnapshot(
            RunSnapshot.CurrentVersion,
            new ContentId(ModeId),
            new ContentId(OathboundId),
            _random.Seed,
            stage,
            new RandomState(101, 102, 103, 104, 105),
            90f,
            6f,
            120f,
            Instant,
            level,
            0f,
            pending,
            takenIds,
            new ContentId[SkillRunner.MaxManualSlots]);

        _session.Start(new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            _random.Seed,
            stage,
            SpawnPlan.Empty,
            snapshot));
    }

    /// <summary>
    /// Instantiates the shipped prefab and injects it — the screen under test is the asset.
    /// </summary>
    private void BuildScreen()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        _screen = Object.Instantiate(prefab);
        _spawned.Add(_screen);

        _presenter = _screen.GetComponent<PausePresenter>();

        Assert.That(
            _presenter,
            Is.Not.Null,
            "PausePresenter did not load off its own prefab (Traps §5).");

        // What RunScope's RegisterComponent does. Start never runs in EditMode, so nothing else on
        // this object has fired — including Place and the panel's opening Hide.
        _presenter.Construct(_session, _pause, _hub, _loader);
    }

    /// <summary>
    /// One frame of exactly what <c>RunTicker.Tick</c> does, gate included.
    /// </summary>
    /// <remarks>
    /// The gate is copied here rather than reached for, because <c>RunTicker</c> cannot be driven
    /// from an EditMode row — it reads <c>Time.deltaTime</c> and builds a snapshot from a scene. That
    /// the ticker really does return on a held pause is <c>FrameOrderTests</c>' claim and M3-08a's
    /// rows; what this fixture is for is the other half — that nothing in <c>PausePresenter</c> skips
    /// a tick of its own (rule 3).
    /// </remarks>
    private void TickLikeTheTicker()
    {
        if (!_session.IsRunning || _pause.IsPaused)
        {
            return;
        }

        _session.Tick(World(Frame));
    }

    /// <summary>
    /// A tap on the icon, through the button the player's thumb would hit.
    /// </summary>
    /// <remarks>
    /// <c>onClick.Invoke</c> rather than a synthetic pointer event, and the difference matters to
    /// rules 4 and 7: <c>Invoke</c> fires whether or not the button is interactable, which is the
    /// same thing that happens when two taps are dispatched from one <c>EventSystem</c> pass. A
    /// helper that checked <c>interactable</c> first would make
    /// <see cref="Quit_SecondTapStartsNoSecondLoad"/> pass against a presenter with no latch at all.
    /// </remarks>
    private void TapIcon() => Field<Button>(_presenter, "_icon").onClick.Invoke();

    private void TapResume() => Field<Button>(_presenter, "_resume").onClick.Invoke();

    private void TapQuit() => Field<Button>(_presenter, "_quit").onClick.Invoke();

    /// <summary>
    /// One frame's worth of the presenter's own <c>Update</c> — what re-reads the pause holder.
    /// </summary>
    /// <remarks>
    /// Invoked rather than waited for, because EditMode has no player loop. A row that needed a real
    /// frame would have to be a PlayMode row, and PROGRESS → Known issues is the standing argument
    /// against adding one of those without a reason.
    /// </remarks>
    private void PassAFrame() => Invoke("Update");

    /// <summary>The presenter's own <c>OnDestroy</c>. See <see cref="Destroy_DropsSubscriptions"/>.</summary>
    private void Destroy() => Invoke("OnDestroy");

    private void Place() => Invoke("Place");

    private void Invoke(string method) =>
        typeof(PausePresenter)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_presenter, null);

    private CanvasGroup Panel() => Field<CanvasGroup>(_presenter, "_panel");

    private float Alpha() => Panel().alpha;

    private Button Icon() => Field<Button>(_presenter, "_icon");

    private RectTransform IconRect() => (RectTransform)Icon().transform;

    private RunPause NewPause()
    {
        var pause = new RunPause();

        _pauses.Add(pause);

        return pause;
    }

    private T Track<T>(T disposable)
        where T : IDisposable
    {
        _disposables.Add(disposable);

        return disposable;
    }

    private string NewDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "soulvail-m3-09a-" + Guid.NewGuid().ToString("N"));

        _directories.Add(directory);

        return directory;
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);

    private static void SetPrivate(object target, string name, object value) =>
        target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);

    private static WorldSnapshot World(float dt) => new WorldSnapshot(Capacity)
    {
        Dt = dt,
        PlayerPosition = Vector3.Zero,
        HasGate = false,
        SpawnPoints = Array.Empty<Vector3>(),
    };

    /// <summary>One node of the requested kind, built to what <c>SkillSpec</c> actually accepts.</summary>
    private static SkillSpec Node(string id, SkillKind kind) => kind switch
    {
        SkillKind.Active => new SkillSpec(
            new ContentId(id),
            new LocKey($"{id}.name"),
            new LocKey($"{id}.desc"),
            SkillKind.Active,
            Array.Empty<IEffect>(),
            new ActiveSpec(
                6f,
                new TriggerSpec(new[]
                {
                    new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 1.5f),
                }),
                new IEffect[]
                {
                    new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.1f),
                })),

        _ => new SkillSpec(
            new ContentId(id),
            new LocKey($"{id}.name"),
            new LocKey($"{id}.desc"),
            kind,
            new IEffect[]
            {
                new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f),
            }),
    };

    /// <summary>Five nodes over three branches — <c>LevelUpPresenterTests</c>' tree.</summary>
    private static SkillTreeSpec Tree() => new SkillTreeSpec(
        new ContentId(TreeId),
        new ContentId(OathboundId),
        new[]
        {
            Branch('a', new[] { NodeOne, NodeTwo, NodeThree }),
            Branch('b', new[] { NodeFour }),
            Branch('c', new[] { NodeFive }),
        });

    private static SkillBranchSpec Branch(char letter, string[] tier)
    {
        var ids = new ContentId[tier.Length];

        for (int i = 0; i < tier.Length; i++)
        {
            ids[i] = new ContentId(tier[i]);
        }

        return new SkillBranchSpec(
            new LocKey($"branch.{letter}"),
            new IReadOnlyList<ContentId>[] { ids });
    }

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        140f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),

        // The Focus ramp is switched off with a maximum of 1, for RunSessionTests' reason: it would
        // put a modifier and a stream of events into a fixture measuring neither.
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        new ShieldSpec(30f, 3f, 1f));

    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 10f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    private static ModeSpec Mode()
    {
        var scaling = new ScalingSpec(
            new BudgetCurve(20f, 6f, 0.04f),
            new WaveCurve(2, 1000, 2, 2),
            new ConcurrencyCurve(DeviceCap, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0));

        return new ModeSpec(
            new ContentId(ModeId),
            new LocKey("mode.test.name"),
            startingStage: 1,
            isEndless: true,
            finalStage: 0,
            scaling,
            new XpCurve(20f, 12f, 1.4f),
            new[] { new RosterEntry(new ContentId(HuskId), 1) },
            new[] { new ContentId(ArenaId) });
    }

    private static float[] Alternating(int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = i % 2 == 0 ? 0.1f : 0.9f;
        }

        return values;
    }

    /// <summary>
    /// A <c>SceneLoader</c> that records the ask instead of answering it.
    /// </summary>
    /// <remarks>
    /// It captures <c>Time.timeScale</c> and the pause holder <em>inside</em> the call, which is the
    /// only place rule 7's ordering is observable: everything the presenter does before the load is
    /// synchronous, and everything after it happens in a scene that no longer exists.
    /// </remarks>
    private sealed class RecordingLoader : SceneLoader
    {
        private readonly RunPause _pause;

        public RecordingLoader(RunPause pause)
        {
            _pause = pause;
        }

        public List<string> Asked { get; } = new List<string>();

        public List<float> TimeScaleAtCall { get; } = new List<float>();

        public List<bool> PausedAtCall { get; } = new List<bool>();

        public override Task LoadAsync(string sceneName)
        {
            Asked.Add(sceneName);
            TimeScaleAtCall.Add(Time.timeScale);
            PausedAtCall.Add(_pause != null && _pause.IsPaused);

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A real <c>LocalJsonSaveStore</c> with a tally in front of it.
    /// </summary>
    /// <remarks>
    /// The real adapter rather than <c>InMemorySaveStore</c>, because rule 8's claim is about a
    /// <em>file</em>: what <see cref="Quit_LeavesTheRunOnDisk"/> has to show is that the run still
    /// decodes off disk after a Quit, and an in-memory store would prove only that a field was not
    /// nulled. The tally is what turns "the file is there" into "nothing asked for it to go".
    /// </remarks>
    private sealed class CountingStore : ISaveStore
    {
        private readonly ISaveStore _inner;

        public CountingStore(ISaveStore inner)
        {
            _inner = inner;
        }

        public int RunWriteCount { get; private set; }

        public int ClearCount { get; private set; }

        public Task<PlayerProfile?> LoadProfile() => _inner.LoadProfile();

        public Task SaveProfile(PlayerProfile profile) => _inner.SaveProfile(profile);

        public Task<RunSnapshot?> LoadRun() => _inner.LoadRun();

        public Task SaveRun(RunSnapshot run)
        {
            RunWriteCount++;

            return _inner.SaveRun(run);
        }

        public Task ClearRun()
        {
            ClearCount++;

            return _inner.ClearRun();
        }
    }

    /// <summary>
    /// Publishes into the run's hub <em>and</em> into a recorder, so a row can both watch the screen
    /// react and count what core said.
    /// </summary>
    private sealed class ForkedEvents : IDomainEvents
    {
        private readonly IDomainEvents _first;
        private readonly IDomainEvents _second;

        public ForkedEvents(IDomainEvents first, IDomainEvents second)
        {
            _first = first;
            _second = second;
        }

        public void Publish<T>(in T evt)
            where T : struct
        {
            // The recorder first, so that a handler which throws still leaves the tally honest.
            _second.Publish(evt);
            _first.Publish(evt);
        }
    }
}
