using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Composition;

/// <summary>
/// The resume path end to end on the Unity side: Boot reads the disk, the Menu offers what it
/// found, the installer puts the generator back, and the ticker builds the config — and clears the
/// pending run, which is a promise M0-12 made and nothing kept until M2-14b.
/// </summary>
/// <remarks>
/// <para>
/// <b>One fixture for four classes</b>, matching <c>InstallerTests</c>' convention: they are one
/// path through the app rather than four units, and the rows read as the sequence a player walks.
/// </para>
/// <para>
/// <b>The one thing this file does not do is tap the buttons.</b> Both handlers load a scene
/// through <c>SceneLoader</c>, and <c>SceneManager.LoadSceneAsync</c> cannot be driven from an
/// EditMode test without taking the Editor's open scene with it. So the rows here assert the two
/// halves the tap is made of — the button's visibility, and what <c>PendingRun</c> ends up
/// holding — and the tap itself is the owner's manual step 2. See the task's <i>As built</i>.
/// <para>
/// <b>What has changed since is that the loader is no longer <c>sealed</c></b> (M3-09a):
/// <c>LoadAsync</c> is <c>virtual</c>, so <c>PausePresenterTests</c> hands its presenter a loader
/// that records instead of loading. These rows were deliberately left as they are — they are about
/// what the Menu decides, not about the load — but a task that wants to tap these two buttons now
/// can.
/// </para>
/// </para>
/// </remarks>
[TestFixture]
public sealed class ResumeFlowTests
{
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";
    private const string HuskPath = "Assets/_Project/Data/Enemies/Husk.asset";
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";

    private static readonly ContentId OathboundId = new ContentId("character.oathbound");
    private static readonly ContentId DescentId = new ContentId("mode.descent");

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly List<IDisposable> _disposables = new List<IDisposable>();
    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void TearEverythingDown()
    {
        for (int i = _disposables.Count - 1; i >= 0; i--)
        {
            _disposables[i].Dispose();
        }

        _disposables.Clear();

        for (int i = 0; i < _created.Count; i++)
        {
            // Unity's ==: a pool disposed above has already destroyed some of these, and a
            // destroyed object is a live reference that only compares equal to null through the
            // engine's operator.
            if (_created[i] != null)
            {
                Object.DestroyImmediate(_created[i]);
            }
        }

        _created.Clear();
    }

    // ---- Boot states the baseline (M3-08a rule 13) ----------------------------------------------

    [Test]
    public void Boot_StatesTheTimeScaleBaseline()
    {
        // **Domain reload is disabled on Play**, so a Play session ended mid-pause leaves
        // Time.timeScale at 0 and the next one would start frozen with nothing to say why. RunPause
        // restores what it found, and this is the belt to that braces: the baseline is *stated* once
        // at boot, beside the frame-rate line, for the same reason a baseline is written down at all.
        float restore = Time.timeScale;

        try
        {
            Time.timeScale = 0f;

            RunBootFlow(new StubStore(), new SavedRun());

            Assert.That(Time.timeScale, Is.EqualTo(1f), "boot must not inherit a frozen clock.");
        }
        finally
        {
            Time.timeScale = restore;
        }
    }

    // ---- Boot reads the disk (rule 6) -----------------------------------------------------------

    [Test]
    public void Boot_LoadsBeforeLeavingBoot()
    {
        var store = new StubStore { Run = Snapshot(stage: 3) };
        var saved = new SavedRun();

        RunBootFlow(store, saved);

        // The load landed synchronously, inside Start. That is what rule 6 rests on: LeaveBoot is
        // the last statement of the same method and the only other caller is a later sceneLoaded,
        // so a run that is present here cannot fail to be present before the Menu is asked for.
        // With a future asynchronous store it would land whenever the disk answered, which is the
        // honest degradation BootFlow's own remarks describe for the profile.
        Assert.That(
            saved.IsPresent,
            Is.True,
            "The run has to be on hand before the Menu exists, or the Continue button appears a "
                + "frame after the thumb that was already moving toward Descend.");

        Assert.That(saved.Value.StageIndex, Is.EqualTo(3));
        Assert.That(store.RunLoads, Is.EqualTo(1), "Read once per launch, not per asker.");
    }

    [Test]
    public void Boot_NoSavedRun_IsAbsent()
    {
        var store = new StubStore { Run = null };
        var saved = new SavedRun();

        Assert.DoesNotThrow(() => RunBootFlow(store, saved));

        Assert.That(saved.IsPresent, Is.False, "A fresh install offers nothing to continue.");
    }

    [Test]
    public void Boot_UnreadableSave_IsAbsent()
    {
        // What LocalJsonSaveStore does with a corrupt file or a version this build does not
        // understand: it deletes it and answers "there is no save" (M2-13b rule 5). Null is the
        // whole vocabulary that reaches here, which is why the Menu has one question to ask and
        // not three.
        var store = new StubStore { Run = null };
        var saved = new SavedRun();

        RunBootFlow(store, saved);

        Assert.That(saved.IsPresent, Is.False);
    }

    [Test]
    public void Boot_FaultedRunLoad_StillReachesTheMenu()
    {
        var store = new StubStore { FaultRunLoad = true };
        var saved = new SavedRun();

        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
            "Could not load the run in progress"));

        // A save that cannot be read has already cost the player the run. The only thing left to
        // decide is whether the app also refuses to start, and it does not.
        Assert.DoesNotThrow(() => RunBootFlow(store, saved));

        Assert.That(saved.IsPresent, Is.False);
    }

    // ---- The Menu offers it (rule 7) ------------------------------------------------------------

    [Test]
    public void Menu_ContinueHiddenWithNoSave()
    {
        var saved = new SavedRun();

        MenuPresenter menu = Menu(saved, out Button descend, out Button @continue);

        Enable(menu);

        Assert.That(
            @continue.gameObject.activeSelf,
            Is.False,
            "A greyed-out Continue on a fresh install advertises a feature the player cannot use "
                + "and cannot find out how to.");

        Assert.That(descend.gameObject.activeSelf, Is.True, "Descend is always there.");
    }

    [Test]
    public void Menu_ContinueShownWithASave()
    {
        var saved = new SavedRun();
        saved.Set(Snapshot(stage: 9));

        MenuPresenter menu = Menu(saved, out _, out Button @continue);

        Enable(menu);

        Assert.That(@continue.gameObject.activeSelf, Is.True);
    }

    [Test]
    public void Menu_VisibilityIsDecidedOnEveryEnable()
    {
        // The Menu is the screen the app comes back to after a death, so the answer changes while
        // this object is alive. Reading it once at injection would leave a Continue on screen for a
        // run that has just been cleared from disk.
        var saved = new SavedRun();
        saved.Set(Snapshot(stage: 9));

        MenuPresenter menu = Menu(saved, out _, out Button @continue);

        Enable(menu);
        Assert.That(@continue.gameObject.activeSelf, Is.True);

        Disable(menu);
        saved.Clear();
        Enable(menu);

        Assert.That(
            @continue.gameObject.activeSelf,
            Is.False,
            "A death clears the saved run, and the next trip through the Menu must not still be "
                + "offering it.");
    }

    [Test]
    public void Menu_ContinueSetsThePendingRun()
    {
        // What the Continue handler does before it awaits anything, asserted on the two objects it
        // does it with. The tap itself is manual step 2 — see the fixture's remarks.
        var pending = new PendingRun();
        RunSnapshot snapshot = Snapshot(stage: 9);

        pending.Resume(snapshot);

        Assert.That(pending.IsSet, Is.True);
        Assert.That(pending.Snapshot.HasValue, Is.True);
        Assert.That(pending.Snapshot.Value.StageIndex, Is.EqualTo(9));

        // Mode, class and seed all come off the snapshot rather than being chosen, which is what
        // makes a resume unable to disagree with the run it is resuming.
        Assert.That(pending.ModeId, Is.EqualTo(snapshot.ModeId));
        Assert.That(pending.CharacterId, Is.EqualTo(snapshot.CharacterId));
        Assert.That(pending.Seed, Is.EqualTo(snapshot.Seed));
    }

    [Test]
    public void Menu_DescendStartsFresh()
    {
        var pending = new PendingRun();
        var saved = new SavedRun();
        saved.Set(Snapshot(stage: 9));

        // A Continue offered and then declined must not leave its snapshot behind.
        pending.Resume(saved.Value);
        pending.Set(DescentId, OathboundId, seed: 11);

        Assert.That(pending.Snapshot, Is.Null, "Descend starts a fresh run, not a resume.");
        Assert.That(pending.Seed, Is.EqualTo(11));

        // **And the saved run is untouched.** The file is overwritten by the new run's opening
        // write on its first frame (M2-14a rule 1), never by the Menu — which is what keeps an
        // ISaveStore and a fire-and-forget delete out of a presenter.
        Assert.That(saved.IsPresent, Is.True);
        Assert.That(saved.Value.StageIndex, Is.EqualTo(9));
    }

    // ---- The installer puts the generator back (rule 3) -----------------------------------------

    [Test]
    public void Installer_RestoresTheGeneratorState()
    {
        const int Seed = 4_242;
        const int Draws = 500;

        // A run that got five hundred draws into its Spawn stream, and the value it would have
        // drawn next.
        var original = new SeededRandom(Seed);

        for (int i = 0; i < Draws; i++)
        {
            original.Spawn.NextFloat();
        }

        RandomState captured = original.Capture();
        float next = original.Spawn.NextFloat();

        IObjectResolver boot = BuildBoot();
        boot.Resolve<PendingRun>().Resume(Snapshot(stage: 9, seed: Seed, random: captured));

        var random = Track(boot.CreateScope(RunInstaller.Install)).Resolve<IRandom>();

        Assert.That(random.Seed, Is.EqualTo(Seed), "The seed selects the sequence; the state is the position on it.");

        Assert.That(
            random.Spawn.NextFloat(),
            Is.EqualTo(next),
            "A resumed run's first draw must be the one the interrupted run was about to make. "
                + "This is the half of ledger row 1 that lives outside core.");
    }

    [Test]
    public void Installer_FreshRunDoesNotRestore()
    {
        const int Seed = 4_242;

        IObjectResolver boot = BuildBoot();
        boot.Resolve<PendingRun>().Set(DescentId, OathboundId, Seed);

        var random = Track(boot.CreateScope(RunInstaller.Install)).Resolve<IRandom>();

        Assert.That(random.Seed, Is.EqualTo(Seed));

        // At draw 0, which is what a generator built from a seed and left alone stands at.
        Assert.That(random.Spawn.NextFloat(), Is.EqualTo(new SeededRandom(Seed).Spawn.NextFloat()));
    }

    // ---- The ticker builds the config, and clears the slot (rules 9, 11) ------------------------

    [Test]
    public void Ticker_BuildsTheResumedConfig()
    {
        const int Seed = 4_242;

        RunSnapshot snapshot = Snapshot(stage: 9, seed: Seed);

        var pending = new PendingRun();
        pending.Resume(snapshot);

        RecordingSession session = StartTicker(pending, new SeededRandom(Seed));

        RunConfig config = session.Config;

        Assert.That(config, Is.Not.Null, "The ticker never started a run.");
        Assert.That(config.ModeId, Is.EqualTo(DescentId));
        Assert.That(config.CharacterId, Is.EqualTo(OathboundId));
        Assert.That(config.Seed, Is.EqualTo(Seed));

        // The saved depth, never the mode's StartingStage — that is the whole difference between
        // a Continue and a Descend on this line.
        Assert.That(config.StageIndex, Is.EqualTo(9));

        Assert.That(config.Restore.HasValue, Is.True);
        Assert.That(config.Restore.Value.StageIndex, Is.EqualTo(9));
        Assert.That(config.Restore.Value.PlayerHp, Is.EqualTo(snapshot.PlayerHp));
    }

    [Test]
    public void Ticker_BuildsTheFreshConfigFromTheMode()
    {
        var pending = new PendingRun();
        pending.Set(DescentId, OathboundId, seed: 11);

        RunConfig config = StartTicker(pending, new SeededRandom(11)).Config;

        Assert.That(config.Restore, Is.Null);

        // GD §4.5: the depth a fresh run begins at is the mode's to state, and nothing in the
        // composition root knows the number itself.
        Assert.That(config.StageIndex, Is.EqualTo(LoadDescent().ToSpec().StartingStage));
    }

    [Test]
    public void Ticker_ClearsThePendingRun()
    {
        var pending = new PendingRun();
        pending.Set(DescentId, OathboundId, seed: 11);

        StartTicker(pending, new SeededRandom(11));

        // **Ledger row 10.** PendingRun.Clear's own doc has said "called once the run has started"
        // since M0-12, and until now nothing called it — so a second trip through the Run scene
        // silently reused the previous run's seed.
        Assert.That(pending.IsSet, Is.False);
        Assert.That(pending.Snapshot, Is.Null);
    }

    [Test]
    public void Ticker_ClearsAfterTheSessionStarted()
    {
        var pending = new PendingRun();
        pending.Resume(Snapshot(stage: 9, seed: 4_242));

        RecordingSession session = StartTicker(pending, new SeededRandom(4_242), spy: pending);

        // Cleared *after* Start returned, never before. RunInstaller.CreateRandom resolves this
        // object while IRandom is first built — during RunSession's construction, upstream of the
        // ticker — so clearing in the installer would pull the seed out from under the config being
        // assembled.
        Assert.That(
            session.PendingWasSetAtStart,
            Is.True,
            "The pending run was already cleared when the session was asked to start, which means "
                + "Clear is running above the line that reads it.");

        Assert.That(pending.IsSet, Is.False, "And it is cleared by the time Start returns.");
    }

    [Test]
    public void DirectPlay_ResumesNothing()
    {
        // Neither singleton set: pressing Play with the Run scene already open, which is how this
        // game is iterated on (M0-12 rule 6). The resume path lives entirely in BootScope and the
        // Menu, so this run is fresh, unseeded and warned about exactly as M0-12 left it.
        var pending = new PendingRun();

        RunConfig config = StartTicker(pending, new SeededRandom(Environment.TickCount)).Config;

        Assert.That(config.Restore, Is.Null, "A run nobody chose cannot be a resume.");
        Assert.That(config.ModeId, Is.EqualTo(DescentId), "The fallback is the first mode the catalog holds.");
        Assert.That(config.CharacterId, Is.EqualTo(OathboundId));
        Assert.That(config.StageIndex, Is.EqualTo(LoadDescent().ToSpec().StartingStage));
    }

    // ---- Twice from one snapshot (rule 10) ------------------------------------------------------

    [Test]
    public void Resume_TwiceFromOneSnapshot()
    {
        var saved = new SavedRun();
        var pending = new PendingRun();

        saved.Set(Snapshot(stage: 9, seed: 4_242));

        // First resume: the Menu hands the snapshot over and the run starts.
        pending.Resume(saved.Value);
        StartTicker(pending, new SeededRandom(4_242));

        // The player is interrupted again before reaching a boundary, so nothing new was written
        // and nothing was deleted. Back at the Menu, the same run is still on offer.
        Assert.That(
            saved.IsPresent,
            Is.True,
            "Resuming must not consume the save. Deleting on read means a second phone call loses "
                + "the run outright, which is the failure GD §7.3 is written about.");

        pending.Resume(saved.Value);

        RunConfig second = StartTicker(pending, new SeededRandom(4_242)).Config;

        Assert.That(second.Restore.HasValue, Is.True);
        Assert.That(second.Restore.Value.StageIndex, Is.EqualTo(9));
        Assert.That(second.Restore.Value.PlayerHp, Is.EqualTo(saved.Value.PlayerHp));
    }

    // ---- Wiring ----------------------------------------------------------------------------------

    [Test]
    public void Boot_SavedRun_IsSingleton()
    {
        IObjectResolver boot = BuildBoot();

        Assert.That(boot.Resolve<SavedRun>(), Is.SameAs(boot.Resolve<SavedRun>()));
    }

    [Test]
    public void Boot_SavedRunAndPendingRun_AreDifferentObjects()
    {
        IObjectResolver boot = BuildBoot();

        // Rule 8, as a registration: they answer different questions and the first
        // Continue-then-back-out would find a single object disagreeing with itself.
        Assert.That((object)boot.Resolve<SavedRun>(), Is.Not.SameAs(boot.Resolve<PendingRun>()));
    }

    [Test]
    public void BootFlow_NullDependency_Throws()
    {
        var loader = new SceneLoader();
        var store = new StubStore();

        // ProfileStore rather than HapticsSettings as of M3-09c: boot hands the loaded profile to
        // the one object that holds a whole one, not to one field's holder.
        var profiles = new ProfileStore(store);
        var saved = new SavedRun();

        Assert.Throws<ArgumentNullException>(() => new BootFlow(null, store, profiles, saved));
        Assert.Throws<ArgumentNullException>(() => new BootFlow(loader, null, profiles, saved));
        Assert.Throws<ArgumentNullException>(() => new BootFlow(loader, store, null, saved));
        Assert.Throws<ArgumentNullException>(() => new BootFlow(loader, store, profiles, null));
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>
    /// Runs <c>BootFlow.Start</c> and disposes it, refusing to run at all if the Editor's open
    /// scene is Boot.
    /// </summary>
    /// <remarks>
    /// The guard is a safety interlock rather than a behaviour claim. <c>Start</c> hands off to the
    /// Menu when the active scene is Boot, and an EditMode test cannot survive
    /// <c>SceneManager.LoadSceneAsync</c> taking the Editor's open scene with it. The active scene
    /// during a test run is whichever one the owner has open, so this is checked rather than
    /// assumed.
    /// </remarks>
    private void RunBootFlow(ISaveStore store, SavedRun saved)
    {
        if (SceneManager.GetActiveScene().name == SceneLoader.Boot)
        {
            Assert.Ignore(
                "The Boot scene is open in the Editor, so running BootFlow here would load the "
                    + "Menu over it. Open any other scene and re-run.");
        }

        var flow = new BootFlow(new SceneLoader(), store, new ProfileStore(store), saved);

        try
        {
            flow.Start();
        }
        finally
        {
            // Drops the sceneLoaded subscription. A fixture that leaked one would leave a dead
            // BootFlow reacting to every later scene load in the Editor.
            flow.Dispose();
        }
    }

    /// <summary>A presenter with both buttons wired and injected by hand.</summary>
    /// <remarks>
    /// The root is built <em>inactive</em>, so adding the component cannot fire <c>OnEnable</c>
    /// before the fields it guards have been assigned — which would make the fixture fail on the
    /// guard doing its job. <c>OnEnable</c> is then invoked explicitly, at the moment a row wants
    /// the menu to go on screen. The children keep their own <c>activeSelf</c> flags, which is what
    /// the visibility rows read.
    /// </remarks>
    private MenuPresenter Menu(SavedRun saved, out Button descend, out Button @continue)
    {
        var root = new GameObject("Menu");

        root.SetActive(false);

        Keep(root);

        var presenter = root.AddComponent<MenuPresenter>();

        descend = NewButton("Descend", root);
        @continue = NewButton("Continue", root);

        // Assigned through reflection because they are [SerializeField] private, which is what the
        // scene does for them — the same route ReticleViewTests takes for the same reason.
        Set(presenter, "_descend", descend);
        Set(presenter, "_continue", @continue);

        // M5-07: OnEnable refuses a menu with no class-select screen, for the reason it refuses one
        // with no Descend button — without it there is no way into a run. Dressed as a bare
        // component because these rows are about the Continue button's visibility and never open it;
        // ClassSelectPresenterTests is what drives the screen itself.
        Set(presenter, "_classSelect", root.AddComponent<ClassSelectPresenter>());

        // The ContentCatalog left this signature at M5-07 with the two methods that read it — the
        // class-select screen resolves its own.
        presenter.Construct(new PendingRun(), saved, new SceneLoader(), Passthrough());

        return presenter;
    }

    private Button NewButton(string name, GameObject parent)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

        child.transform.SetParent(parent.transform, false);

        Keep(child);

        return child.AddComponent<Button>();
    }

    /// <summary>
    /// Builds a <c>RunTicker</c> over a recording session, starts it, and disposes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every dependency below is guarded by the constructor, so all of them have to be real even
    /// though <c>Start</c> touches four. That is the price of the guard doing its job, and it buys
    /// EditMode its first <c>RunTicker</c> fixture — which <c>ProjectileViewsTests</c> noted the
    /// absence of.
    /// </para>
    /// <para>
    /// The session is a fake rather than a real <c>RunSession</c>, deliberately: the rows here are
    /// about what the ticker <em>hands over</em> and what it does afterwards, and a real session
    /// would answer with a run instead of with the config it was given.
    /// </para>
    /// </remarks>
    private RecordingSession StartTicker(PendingRun pending, IRandom random, PendingRun spy = null)
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");

        Assert.That(enemyLayer, Is.GreaterThanOrEqualTo(0), "The project has no Enemy layer.");

        var builder = new ContainerBuilder();
        IObjectResolver container = Track(builder.Build());

        var hub = Track(new DomainEventHub());
        var session = new RecordingSession(spy);

        var root = new GameObject("Player");

        Keep(root);

        // PlayerView first: adding it brings the CharacterController it requires.
        PlayerView player = root.AddComponent<PlayerView>();
        player.Construct(hub);

        ChargeMotion charge = root.AddComponent<ChargeMotion>();

        var enemyViews = Track(new EnemyViews(
            container,
            Template<EnemyView>("EnemyTemplate", enemyLayer),
            null,
            hub,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()),
            prewarm: 0));

        // Empty, and it stays that way: this fixture plays the Oathbound, which raises nothing.
        // It is here because the ticker takes one (M5-05a) — and being on that constructor is what
        // guarantees the census is listening before a run can raise anything.
        var minionViews = Track(new MinionViews(
            container, Template<MinionView>("MinionTemplate"), null, hub, prewarm: 0));

        var projectileViews = Track(new ProjectileViews(
            container, Template<ProjectileView>("ProjectileTemplate"), null, hub));

        var rings = Track(new TelegraphRings(
            container, Template<TelegraphRingView>("RingTemplate"), null, hub, prewarm: 0));

        // Empty, on the rings' terms: nothing in this fixture casts a skill, so no zone is ever
        // spawned. It is here because the ticker takes one (M3-11c).
        var zones = Track(new ZoneViews(
            container, Template<ZoneView>("ZoneTemplate"), null, hub, prewarm: 0));

        // Empty on the zones' terms, and with a null arena pool besides: nothing in this fixture
        // reaches a boss, so no ring, crack or shell is ever rented. It is here because the ticker
        // takes one (M4-03).
        var boss = Track(new BossViews(
            container,
            Template<ShockwaveView>("ShockwaveTemplate"),
            Template<FissureView>("FissureTemplate"),
            Template<BossBeatView>("BeatTemplate"),
            null,
            hub,
            enemyViews,
            null,
            shockwavePrewarm: 0,
            fissurePrewarm: 0,
            beatPrewarm: 0));

        // Empty on the zones' terms: this fixture plays the Oathbound, which has no Shroudstep, so
        // nothing ever drops a corpse. It is here because the ticker takes one (M5-05b).
        var decoys = Track(new DecoyViews(
            container, Template<DecoyView>("DecoyTemplate"), null, hub, prewarm: 0));

        var input = Track(new InputAdapter());

        var cameraObject = new GameObject("Camera");

        Keep(cameraObject);

        var cone = new ConeOverlapQuery(ConeOverlapQuery.DefaultCapacity, 1 << enemyLayer, enemyViews);

        charge.Construct(enemyViews, 1 << enemyLayer);

        var pause = new RunPause();

        var ticker = new RunTicker(
            session,
            session,
            session,
            pause,
            pending,
            Catalog(),
            random,
            new WorldSnapshot(8),
            new SnapshotBuilder(player, input, enemyViews, minionViews, null, null, null),
            new IntentBuffer(),
            player,
            charge,
            enemyViews,
            minionViews,
            projectileViews,
            rings,
            zones,
            boss,
            decoys,
            Track(new SaveWriter(new StubStore(), hub)),

            // M4-05b's writer, on the constructor for the line above's reason. Nothing here dies,
            // so it banks nothing — but a Scoped registration nobody resolves is never constructed,
            // which is exactly what this parameter exists to prevent in the real scope.
            Track(new ShardWriter(new ProfileStore(new StubStore()), hub)),
            input,
            SpawnPlan.Empty,
            new TapToFocusAdapter(input, session, cameraObject.AddComponent<Camera>()),

            // M3-10a's second poller in CommandPhase. Nothing here presses a slot — these rows are
            // about what Start hands over — but the constructor guards every argument, which is what
            // makes this a compile-forced line rather than a choice.
            new SkillSlotInput(session),
            cone);

        try
        {
            ticker.Start();
        }
        finally
        {
            // Restores Screen.sleepTimeout and disables the input adapter. Left undone, every later
            // row in the suite would run with the Editor's sleep timeout changed.
            ticker.Dispose();

            // The same argument for the two globals RunPause writes. Nothing here pauses, so this is
            // belt and braces — and it is the cheap half of a failure that would show up as an
            // unrelated fixture timing out.
            pause.Dispose();
        }

        return session;
    }

    private T Template<T>(string name, int layer = 0)
        where T : Component
    {
        var root = new GameObject(name) { layer = layer };

        // Inactive first, so neither Awake nor Start runs on the template — VContainer deactivates
        // a prefab before instantiating and injects the copy while it is off.
        root.SetActive(false);

        Keep(root);

        return root.AddComponent<T>();
    }

    private IObjectResolver BuildBoot()
    {
        var builder = new ContainerBuilder();

        // The two M3-02b lists are empty because nothing ships in Data/ until M3-12 (rule 7), which
        // is also what BootScope.prefab carries — so this container is the shipped one.
        BootInstaller.Install(
            builder,
            new[] { LoadOathbound() },
            new[] { LoadHusk() },
            new[] { LoadDescent() },
            Array.Empty<SkillDefinition>(),
            Array.Empty<SkillTreeDefinition>(),
            EmptyTable());

        return Track(builder.Build());
    }

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { LoadOathbound().ToSpec() },
        new[] { LoadHusk().ToSpec() },
        new[] { LoadDescent().ToSpec() });

    private static CharacterDefinition LoadOathbound() =>
        AssetDatabase.LoadAssetAtPath<CharacterDefinition>(OathboundPath);

    private static EnemyDefinition LoadHusk() =>
        AssetDatabase.LoadAssetAtPath<EnemyDefinition>(HuskPath);

    private static ModeDefinition LoadDescent() =>
        AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath);

    private static RunSnapshot Snapshot(int stage, int seed = 4_242, RandomState random = default) =>
        new RunSnapshot(
            RunSnapshot.CurrentVersion,
            DescentId,
            OathboundId,
            seed,
            stage,
            random,
            playerHp: 62f,
            playerShield: 9f,
            runTime: 412.5f,
            writtenAt: Instant,
            level: 1,
            xp: 0f,
            pendingLevelUps: 0,
            takenNodeIds: Array.Empty<ContentId>(),
            manualSkillIds: new ContentId[SkillRunner.MaxManualSlots]);

    private static void Set(MenuPresenter presenter, string field, Object value) =>
        typeof(MenuPresenter).GetField(field, Private).SetValue(presenter, value);

    private static void Enable(MenuPresenter presenter) => Invoke(presenter, "OnEnable");

    private static void Disable(MenuPresenter presenter) => Invoke(presenter, "OnDisable");

    private static void Invoke(MenuPresenter presenter, string method) =>
        typeof(MenuPresenter).GetMethod(method, Private).Invoke(presenter, null);

    /// <remarks>
    /// Two names rather than two overloads: C# refuses methods that differ only by a type
    /// parameter's constraints, so <c>Track&lt;T&gt; where T : IDisposable</c> and
    /// <c>Track&lt;T&gt; where T : Object</c> cannot coexist.
    /// </remarks>
    private T Track<T>(T disposable)
        where T : IDisposable
    {
        _disposables.Add(disposable);

        return disposable;
    }

    private T Keep<T>(T o)
        where T : Object
    {
        _created.Add(o);

        return o;
    }

    /// <summary>
    /// A store that answers from fields and counts what it was asked. Nothing here touches a disk:
    /// <c>LocalJsonSaveStoreTests</c> owns the medium.
    /// </summary>
    private sealed class StubStore : ISaveStore
    {
        public RunSnapshot? Run { get; set; }

        public bool FaultRunLoad { get; set; }

        public int RunLoads { get; private set; }

        public Task<PlayerProfile?> LoadProfile() => Task.FromResult<PlayerProfile?>(null);

        public Task SaveProfile(PlayerProfile profile) => Task.CompletedTask;

        public Task<RunSnapshot?> LoadRun()
        {
            RunLoads++;

            // Faults the *task*, the way real I/O fails — never throwing from the call, which a
            // caller could pass with a try and still crash on a device.
            return FaultRunLoad
                ? Task.FromException<RunSnapshot?>(new System.IO.IOException("The fixture failed this read."))
                : Task.FromResult(Run);
        }

        public Task SaveRun(RunSnapshot run) => Task.CompletedTask;

        public Task ClearRun() => Task.CompletedTask;
    }

    /// <summary>
    /// A run, as far as the ticker is concerned: it remembers the config it was handed and, if it
    /// was given one to watch, whether the pending run was still set at that moment.
    /// </summary>
    private sealed class RecordingSession : IRunSession, IPlayerCommands, IProgressionCommands
    {
        private readonly PendingRun _spy;

        public RecordingSession(PendingRun spy)
        {
            _spy = spy;
        }

        public RunConfig Config { get; private set; }

        public bool PendingWasSetAtStart { get; private set; }

        public bool IsRunning { get; private set; }

        /// <summary>
        /// Null, and nothing here reads it. <c>RunState</c>'s constructor is <c>internal</c> and
        /// this assembly has no <c>InternalsVisibleTo</c> — deliberately (AR §18.2) — so a fake
        /// session cannot produce one, and the rows in this file are about the config that is
        /// handed over rather than the run it builds.
        /// </summary>
        public RunState State => null;

        public void Start(RunConfig config)
        {
            Config = config;
            PendingWasSetAtStart = _spy is not null && _spy.IsSet;
            IsRunning = true;
        }

        public void Tick(WorldSnapshot snapshot)
        {
        }

        public void End()
        {
            IsRunning = false;
        }

        public void ReportConeHits(ReadOnlySpan<int> enemyIds)
        {
        }

        public void ReportChargeHits(ReadOnlySpan<int> enemyIds)
        {
        }

        public void FocusTarget(System.Numerics.Vector3 worldPoint)
        {
        }

        public void ClearFocus()
        {
        }

        public void MovementSkill()
        {
        }

        // M3-07a grew IPlayerCommands. Empty like the three above: this fake exists to be resolved
        // from the container, not to be commanded.
        public void CastSkill(int slot)
        {
        }

        public void SetAutoCast(ContentId skillId, bool auto)
        {
        }

        // M3-08a made this fake an IProgressionCommands too, because RunTicker now takes that port
        // and this file builds one. Inert like the commands above: the rows here are about the
        // RunConfig a resume hands over, and a level-up has no part in that — IsLevelUpPending
        // answering false is what keeps the phase a no-op for every row in this file.
        public bool IsLevelUpPending => false;

        public bool HasOffer => false;

        public void OpenLevelUp()
        {
        }

        public void ChooseOffer(int index)
        {
        }

        // And M5-07a-ii grew it again, with CH §5.4's moment. Inert like the four above, for their
        // reason: the rows here are about the RunConfig a resume hands over, and IsSplashPending
        // answering false is what keeps the level-up phase a no-op for every one of them.
        public bool IsSplashPending => false;

        public bool IsSplashOpen => false;

        public void OpenSplash()
        {
        }

        public void ChooseSplash(ContentId characterId, int branch)
        {
        }
    }

    /// <summary>
    /// A localisation table for a container build. Empty, because nothing in these rows reads a
    /// word — what they assert is that <c>BootInstaller</c> takes one and registers the port.
    /// </summary>
    private static LocalizationTable EmptyTable() =>
        ScriptableObject.CreateInstance<LocalizationTable>();

    /// <summary>The real adapter over an empty table: every key resolves to itself.</summary>
    private static ILocalizer Passthrough() => new TableLocalizer(EmptyTable());

}
