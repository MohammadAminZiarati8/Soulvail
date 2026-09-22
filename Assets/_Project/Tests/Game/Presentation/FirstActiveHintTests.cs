using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Core.Stage;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// CC §6.3's <em>"Once. Never again."</em>: the callout that fires on the first Active the player
/// ever owns, and never again on this install.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over a real <c>RunSession</c>, a real <c>DomainEventHub</c> and a real <c>ProfileStore</c></b>
/// — <c>LevelUpPresenterTests</c>' shape, one screen on. Every <c>NodeTaken</c> and
/// <c>LevelUpClosed</c> a row reacts to was published by core out of a real <c>ChooseOffer</c>,
/// because the one thing rule 6 is about is what the <em>run</em> thinks it owns, and a fixture that
/// published its own events would be asserting against its own arithmetic.
/// </para>
/// <para>
/// <b>On <see cref="InputTestFixture"/>, which swaps the whole Input System for an isolated one per
/// test</b> — <c>InputAdapterTests</c>' reason, and here it is load-bearing rather than tidy: rule 9
/// puts the dismissal tap on the run's <c>InputAdapter</c>, so a row that pressed nothing would be
/// green against a hint that never reads it, and a row that read the Editor's real input would go
/// red the day the owner's mouse was down when the suite ran.
/// </para>
/// <para>
/// <b>The screen under test is the shipped prefab</b>, instantiated per row. Hand-building one would
/// test a second callout that happens to resemble the asset, and the failure Traps §5 describes — a
/// component that deserialises as null with nothing reporting it — is invisible to a fixture that
/// never loads the asset.
/// </para>
/// <para>
/// <c>Awake</c> and <c>Start</c> do not run in EditMode (Traps §5), so the guards in
/// <c>FirstActiveHint.Start</c> never fire here and the prefab's own alpha 0 is what leaves the
/// callout down at the top of each row. A frame is passed explicitly where a row needs one.
/// </para>
/// </remarks>
[TestFixture]
public sealed class FirstActiveHintTests : InputTestFixture
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/FirstActiveHint.prefab";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string TreeId = "tree.oathbound";
    private const string ArenaId = "arena.pillars";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    /// <summary>
    /// The key rule 8 asks for. Still what the prefab authors as its placeholder, which
    /// <c>Prefab_IsDressed</c> pins; what a player reads is <see cref="HintSentence"/>.
    /// </summary>
    private const string HintKey = "ui.hint.firstActive";

    /// <summary>
    /// What <see cref="HintKey"/> resolves to — <c>English.asset</c>'s row, authored here so the
    /// fixture is asserting against a table it built rather than against the shipped one.
    /// </summary>
    private const string HintSentence = "Skills can be set to Manual: Pause → Skills";

    /// <summary>Branch a's whole first tier: three Actives, so a third is still available.</summary>
    private const string ActiveOne = "skill.test.a1";
    private const string ActiveTwo = "skill.test.a2";
    private const string ActiveThree = "skill.test.a3";

    /// <summary>Branch b and branch c, one Passive each, so every offer holds both kinds.</summary>
    private const string PassiveOne = "skill.test.p1";
    private const string PassiveTwo = "skill.test.p2";

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private DomainEventHub _hub;
    private RecordingEvents _spy;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private RunSession _session;

    private InMemorySaveStore _store;
    private ProfileStore _profiles;

    private Mouse _mouse;
    private InputAdapter _input;

    private GameObject _screen;
    private FirstActiveHint _hint;

    /// <summary>
    /// What the callout resolves its one key through. The whole fixture uses a table holding that
    /// row, because every row here is about a callout a player is reading.
    /// </summary>
    private ILocalizer _localizer;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    [SetUp]
    public void CreateWorld()
    {
        _hub = new DomainEventHub();
        _spy = new RecordingEvents();
        _random = new FixedRandom(7, Alternating(8_192));
        _clock = new FixedClock(Instant);

        _store = new InMemorySaveStore();
        _profiles = new ProfileStore(_store);

        _mouse = InputSystem.AddDevice<Mouse>();

        _input = new InputAdapter();
        _input.Enable();

        _localizer = new DictionaryLocalizer(HintKey, HintSentence);
    }

    /// <summary>The real adapter over an empty table: every key resolves to itself.</summary>
    private static ILocalizer Passthrough() =>
        new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>());

    [TearDown]
    public void DestroyWorld()
    {
        // Before InputTestFixture's own teardown, which NUnit runs after this one — the adapter has
        // to be destroyed while the test input state it was built against still exists.
        _input?.Dispose();
        _input = null;

        foreach (GameObject go in _spawned)
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        _spawned.Clear();

        _hub?.Dispose();
    }

    // ---- The first Active (rules 6, 7, 8) --------------------------------------------------------

    [Test]
    public void Hint_ShowsOnTheFirstActive()
    {
        StartRun(pending: 1);
        BuildHint();

        Take(SkillKind.Active);

        Assert.That(
            _session.State.OwnedActiveCount,
            Is.EqualTo(1),
            "the fixture's premise: this is the player's first Active.");

        Assert.That(_hint.IsShown, Is.True, "the level-up closed and no callout appeared.");

        // **`Hint_DrawsEnglish`** — ledger row 9's third and sharpest reader closing (rule 9): a
        // callout whose entire job is to tell the player something was telling them
        // `ui.hint.firstActive`. The prefab still authors the key as its placeholder, which
        // `Prefab_IsDressed` still pins; what changed is what is written over it on show.
        Assert.That(Text(), Is.EqualTo(HintSentence));
        Assert.That(Text(), Does.Not.StartWith("ui."));
        Assert.That(Text(), Does.Contain("Manual"), "the callout does not name what it is about.");
    }

    [Test]
    public void Hint_WaitsForTheScreenToClose()
    {
        // Two picks, so the first ChooseOffer publishes NodeTaken and redraws rather than closing —
        // which is the only way to stand *between* the two events this class listens to.
        StartRun(pending: 2);
        BuildHint();

        Take(SkillKind.Active);

        Assert.That(_spy.Count<NodeTaken>(), Is.EqualTo(1), "the fixture's premise.");
        Assert.That(_spy.Count<LevelUpClosed>(), Is.Zero, "the fixture's premise: a pick is still owed.");

        // Rule 7: M3-08b's canvas is full-screen and modal, so a callout raised here would either be
        // invisible underneath it or compete with the three cards for GD §13.1's two seconds.
        Assert.That(_hint.IsShown, Is.False);

        Take(SkillKind.Passive);

        Assert.That(_spy.Count<LevelUpClosed>(), Is.EqualTo(1));
        Assert.That(_hint.IsShown, Is.True, "the frame the game starts again is the frame this shows.");
    }

    /// <summary>
    /// The ordering the arming condition rests on, pinned rather than remembered.
    /// </summary>
    /// <remarks>
    /// <c>RunState.OwnedActiveCount</c> is the <em>runner</em>'s tally and
    /// <c>LevelUpFlow.ChooseOffer</c> hands it a new Active <em>after</em> <c>SkillTree.Take</c> has
    /// published <c>NodeTaken</c> — so a handler reads the count from before the node. The hint
    /// therefore arms on 0 rather than on the spec's 1, and if that order is ever changed this row
    /// goes red instead of the callout going quietly missing.
    /// </remarks>
    [Test]
    public void Hint_SeesTheCountBeforeTheRunnerHasIt()
    {
        StartRun(pending: 1);

        int duringEvent = -1;

        using (_hub.Subscribe<NodeTaken>(_ => duringEvent = _session.State.OwnedActiveCount))
        {
            Take(SkillKind.Active);
        }

        Assert.That(duringEvent, Is.Zero, "the runner already had the Active when NodeTaken went out.");
        Assert.That(_session.State.OwnedActiveCount, Is.EqualTo(1), "and it has it by the close.");
    }

    [Test]
    public void Hint_IgnoresAPassive()
    {
        StartRun(pending: 1);
        BuildHint();

        Take(SkillKind.Passive);

        // Rule 6: NodeTaken carries the Kind (M3-03 rule 7), so this needs no catalog lookup and no
        // guess. CC §6.1 gives a Passive no toggle and no button, so there is nothing to point at.
        Assert.That(_session.State.OwnedActiveCount, Is.Zero, "the fixture's premise.");
        Assert.That(_hint.IsShown, Is.False);
    }

    [Test]
    public void Hint_IgnoresTheSecondActive()
    {
        // One Active already owned, restored — the only door RunSession.Start adds one through
        // short of playing a level-up.
        StartRun(pending: 1, taken: new[] { ActiveOne });
        BuildHint();

        Take(SkillKind.Active);

        Assert.That(_session.State.OwnedActiveCount, Is.EqualTo(2), "the fixture's premise.");

        // Rule 6, and the reason the count is read off the run rather than kept here: a hint keeping
        // its own tally would arm on the first NodeTaken of every session.
        Assert.That(_hint.IsShown, Is.False);
    }

    [Test]
    public void Hint_ResumedRunWithActivesDoesNotArm()
    {
        StartRun(pending: 1, taken: new[] { ActiveOne, ActiveTwo });
        BuildHint();

        Take(SkillKind.Active);

        Assert.That(_session.State.OwnedActiveCount, Is.EqualTo(3), "the fixture's premise.");

        // A run restored with two Actives already taken never arms, and it should not: that player
        // has had actives for a while, and being told what they are is not news (rule 6).
        Assert.That(_hint.IsShown, Is.False);
    }

    // ---- The flag (rules 10, 11) -----------------------------------------------------------------

    [Test]
    public void Hint_NeverShowsWhenTheFlagIsSpent()
    {
        _profiles.Adopt(new PlayerProfile(
            PlayerProfile.CurrentVersion, hapticsEnabled: true, seenFirstActiveHint: true,
            shards: 0));

        StartRun(pending: 1);
        BuildHint();

        Take(SkillKind.Active);

        // Rules 6 and 11: once is once across installs, and this is what a second install-worth of
        // runs looks like from inside.
        Assert.That(_session.State.OwnedActiveCount, Is.EqualTo(1), "the fixture's premise.");
        Assert.That(_hint.IsShown, Is.False);
        Assert.That(_store.ProfileWriteCount, Is.Zero, "nothing was spent, so nothing was written.");
    }

    [Test]
    public void Hint_SpendsTheFlagOnShow()
    {
        StartRun(pending: 1);
        BuildHint();

        Take(SkillKind.Active);

        Assert.That(_hint.IsShown, Is.True, "the fixture's premise.");

        // **Before any dismissal** (rule 10). A player who sees the callout and is killed two
        // seconds later has seen it; spending it on dismissal would mean an app killed mid-callout
        // shows it again, which is exactly the "never again" CC §6.3 is asking for.
        Assert.That(_profiles.Current.SeenFirstActiveHint, Is.True);
        Assert.That(_store.ProfileWriteCount, Is.EqualTo(1), "spent once, saved once.");

        PlayerProfile written = _store.LoadProfile().GetAwaiter().GetResult().Value;

        Assert.That(written.SeenFirstActiveHint, Is.True, "and it reached the disk on that frame.");

        // And the other field went with it rather than being reset — the copy-through this whole
        // task is about, exercised by its second writer.
        Assert.That(written.HapticsEnabled, Is.EqualTo(_profiles.Current.HapticsEnabled));
    }

    [Test]
    public void Hint_SurvivesARestart()
    {
        StartRun(pending: 1);
        BuildHint();

        Take(SkillKind.Active);

        Assert.That(_hint.IsShown, Is.True, "the fixture's premise.");

        // The first app is gone, subscriptions and all — otherwise the callout still listening would
        // be the one answering, and this row would prove nothing about the second.
        Destroy();

        // A whole new app over the same disk: a fresh store, the profile loaded the way BootFlow
        // loads it, and a fresh run with a fresh callout in it.
        var reloaded = new ProfileStore(_store);

        reloaded.Adopt(_store.LoadProfile().GetAwaiter().GetResult() ?? PlayerProfile.Default);

        Assert.That(reloaded.Current.SeenFirstActiveHint, Is.True, "the flag came back off the disk.");

        _profiles = reloaded;

        StartRun(pending: 1);
        BuildHint();

        Take(SkillKind.Active);

        // Rule 11, and the whole reason this task bumped a format: once is once across *installs*,
        // not once across runs. A bool on this component would pass every row above and fail here.
        Assert.That(_session.State.OwnedActiveCount, Is.EqualTo(1), "the fixture's premise.");
        Assert.That(_hint.IsShown, Is.False);
    }

    // ---- Dismissal (rule 9) -----------------------------------------------------------------------

    [Test]
    public void Hint_DismissesOnTap()
    {
        ShowIt();

        // The first tick is spent by the guard that exists for this exact frame: LevelUpClosed is
        // published from inside the tap that spent the last pick, so a callout that read the adapter
        // immediately would be dismissed by the tap that caused it.
        Press(_mouse.leftButton);
        Tick(0.01f);

        Assert.That(_hint.IsShown, Is.True, "the frame it went up on is not an answer to it.");

        Release(_mouse.leftButton);
        Press(_mouse.leftButton);
        Tick(0.01f);

        Assert.That(_hint.IsShown, Is.False, "a tap did not take the callout down.");
    }

    [Test]
    public void Hint_DismissesOnItsOwn()
    {
        ShowIt();

        SetDwell(2f);

        Tick(1f);

        Assert.That(_hint.IsShown, Is.True, "half the dwell is not the whole of it.");

        Tick(1f);

        // An auto-dismiss is what keeps a player who ignored the callout from carrying a box around
        // for the rest of the stage — and the clock is unscaled, so a pause taken while reading it
        // spends the dwell rather than freezing it.
        Assert.That(_hint.IsShown, Is.False);
    }

    [Test]
    public void Hint_TakesNoTouches()
    {
        ShowIt();

        CanvasGroup root = Field<CanvasGroup>(_hint, "_root");

        // Rule 9: the run is ticking again by the time this is up, so the callout must never sit in
        // the raycast path of the stick or the Charge button. That is also why the dismissal tap
        // comes through InputAdapter rather than a Button under the panel.
        Assert.That(root.alpha, Is.EqualTo(1f), "the fixture's premise.");
        Assert.That(root.blocksRaycasts, Is.False);
        Assert.That(root.interactable, Is.False);
    }

    [Test]
    public void Hint_IgnoresANonFiniteDwell()
    {
        float[] refused = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -1f };

        foreach (float dp in refused)
        {
            // A fresh install per pass, because the flag is spent by the show and a second pass
            // over a spent one would assert against a callout that never went up.
            ResetInstall();

            ShowIt();

            SetDwell(dp);

            // The default rather than the authored value, and the two failures it answers are
            // different: a NaN never satisfies `>=`, so the callout would sit over the fight until
            // the player happened to tap, and a zero or negative dwell would take it down on the
            // frame it went up — spending CC §6.3's one chance on something nobody could read.
            Tick(3.9f);

            Assert.That(_hint.IsShown, Is.True, $"{dp} dismissed before the default dwell.");

            Tick(0.2f);

            Assert.That(_hint.IsShown, Is.False, $"{dp} never dismissed at all.");
        }
    }

    // ---- Lifetime and guards -----------------------------------------------------------------------

    [Test]
    public void Construct_RefusesNullDependencies()
    {
        StartRun(pending: 1);
        BuildHint();

        Assert.Throws<ArgumentNullException>(() => _hint.Construct(null, _session, _profiles, _input, Passthrough()));
        Assert.Throws<ArgumentNullException>(() => _hint.Construct(_hub, null, _profiles, _input, Passthrough()));
        Assert.Throws<ArgumentNullException>(() => _hint.Construct(_hub, _session, null, _input, Passthrough()));
        Assert.Throws<ArgumentNullException>(() => _hint.Construct(_hub, _session, _profiles, null, Passthrough()));
        Assert.Throws<ArgumentNullException>(() => _hint.Construct(_hub, _session, _profiles, _input, null));
    }

    [Test]
    public void Destroy_DropsSubscriptions()
    {
        StartRun(pending: 1);
        BuildHint();

        Assert.That(_hub.SubscriberCount<NodeTaken>(), Is.EqualTo(1));
        Assert.That(_hub.SubscriberCount<LevelUpClosed>(), Is.EqualTo(1));

        Destroy();

        // A view destroyed before its scope — a scene reload, an arena opened without a run — must
        // not stay in a subscriber list and be handed an event for a component Unity has killed.
        Assert.That(_hub.SubscriberCount<NodeTaken>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<LevelUpClosed>(), Is.Zero);
    }

    [Test]
    public void Construct_TwiceHoldsOneSubscriptionEach()
    {
        StartRun(pending: 1);
        BuildHint();

        _hint.Construct(_hub, _session, _profiles, _input, _localizer ?? Passthrough());

        // VContainer does not inject twice and this fixture does. One subscription each, or the
        // callout would spend its flag once per injection.
        Assert.That(_hub.SubscriberCount<NodeTaken>(), Is.EqualTo(1));
        Assert.That(_hub.SubscriberCount<LevelUpClosed>(), Is.EqualTo(1));
    }

    // ---- The asset (Traps §5) ----------------------------------------------------------------------

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        // Read off the asset rather than trusted in memory: a prefab whose component reference never
        // bound looks identical to one that did until something tries to use it (Traps §5).
        var hint = prefab.GetComponent<FirstActiveHint>();

        Assert.That(hint, Is.Not.Null, "FirstActiveHint did not load off its own prefab.");

        CanvasGroup root = Field<CanvasGroup>(hint, "_root");
        TMP_Text text = Field<TMP_Text>(hint, "_text");

        Assert.That(root, Is.Not.Null, "the root CanvasGroup is not dressed.");
        Assert.That(text, Is.Not.Null, "the label is not dressed.");

        // Down as authored, so a callout someone was editing cannot ship over the arena — and never
        // in the raycast path, in the state the asset ships in as well as the one it is shown in.
        Assert.That(root.alpha, Is.EqualTo(0f));
        Assert.That(root.blocksRaycasts, Is.False);

        Assert.That(
            Field<float>(hint, "_secondsShown"),
            Is.EqualTo(4f),
            "the authored dwell is rule 9's four seconds.");

        // Ledger row 9, ADR-0012: a key, not English, and one word with no space in it so that a
        // sentence typed into the Inspector by mistake fails here rather than on a phone.
        Assert.That(text.text, Is.EqualTo(HintKey));
        Assert.That(HintKey, Does.StartWith("ui."));
        Assert.That(HintKey, Does.Not.Contain(" "));
    }

    // ---- Fixture -------------------------------------------------------------------------------------

    /// <summary>
    /// Starts a run over a three-branch tree, restored with <paramref name="taken"/> already owned
    /// and <paramref name="pending"/> picks owed.
    /// </summary>
    /// <remarks>
    /// Resumed rather than fresh, for <c>SkillsPresenterTests</c>' reason: it is the only door
    /// <c>RunSession.Start</c> adds an owned Active through short of playing a level-up. The level
    /// accounts for every pick the run has earned (M3-08a rule 9) — one per node, plus the ones
    /// still owed — so the identity's guard is satisfied by arithmetic rather than by a large number.
    /// </remarks>
    private void StartRun(int pending, string[] taken = null)
    {
        taken ??= Array.Empty<string>();

        _catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Husk() },
            new[] { Mode() },
            new[]
            {
                Node(ActiveOne, SkillKind.Active),
                Node(ActiveTwo, SkillKind.Active),
                Node(ActiveThree, SkillKind.Active),
                Node(PassiveOne, SkillKind.Passive),
                Node(PassiveTwo, SkillKind.Passive),
            },
            new[] { Tree() });

        // The hub is the session's own IDomainEvents, so the callout hears what core published
        // rather than what the fixture decided to say. The spy rides alongside for counting.
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

        var takenIds = new ContentId[taken.Length];

        for (int i = 0; i < taken.Length; i++)
        {
            takenIds[i] = new ContentId(taken[i]);
        }

        var snapshot = new RunSnapshot(
            RunSnapshot.CurrentVersion,
            new ContentId(ModeId),
            new ContentId(OathboundId),
            _random.Seed,
            1,
            new RandomState(101, 102, 103, 104, 105),
            90f,
            6f,
            120f,
            Instant,
            1 + taken.Length + pending,
            0f,
            pending,
            takenIds,
            new ContentId[SkillRunner.MaxManualSlots],
            default,
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>());

        _session.Start(new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            _random.Seed,
            1,
            SpawnPlan.Empty,
            snapshot));
    }

    /// <summary>
    /// Instantiates the shipped prefab and injects it — the callout under test is the asset.
    /// </summary>
    private void BuildHint()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        _screen = Object.Instantiate(prefab);
        _spawned.Add(_screen);

        _hint = _screen.GetComponent<FirstActiveHint>();

        Assert.That(
            _hint,
            Is.Not.Null,
            "FirstActiveHint did not load off its own prefab (Traps §5).");

        // What RunScope's RegisterComponent does. Start never runs in EditMode, so nothing else on
        // this object has fired and the prefab's authored alpha 0 is what leaves the callout down.
        _hint.Construct(_hub, _session, _profiles, _input, _localizer ?? Passthrough());
    }

    /// <summary>
    /// A new install and a new app: an empty disk, an unspent profile, and a hub nothing from the
    /// previous pass is still listening to.
    /// </summary>
    /// <remarks>
    /// Only the rows that show the callout more than once need this. A callout left subscribed to
    /// the old hub reacts to nothing, which is the point — the one that answers must be the one the
    /// row is about.
    /// </remarks>
    private void ResetInstall()
    {
        _hub?.Dispose();
        _hub = new DomainEventHub();
        _spy = new RecordingEvents();

        _store = new InMemorySaveStore();
        _profiles = new ProfileStore(_store);
    }

    /// <summary>A run, a callout, and the first Active taken — the state the dismissal rows start in.</summary>
    private void ShowIt()
    {
        StartRun(pending: 1);
        BuildHint();

        Take(SkillKind.Active);

        Assert.That(_hint.IsShown, Is.True, "the fixture's premise: the callout is up.");
    }

    /// <summary>
    /// Opens the level-up and spends one pick on the first card of <paramref name="kind"/>.
    /// </summary>
    /// <remarks>
    /// The index is searched for rather than assumed, because which branch lands at which position
    /// is the offer generator's seeded business (M3-04) and a hard-coded index would make every row
    /// here a test of that seed. <c>OpenLevelUp</c> is idempotent while an offer is open, so the
    /// second call in a two-pick row leaves the redraw <c>ChooseOffer</c> already made.
    /// </remarks>
    private void Take(SkillKind kind)
    {
        _session.OpenLevelUp();

        IReadOnlyList<ContentId> offer = _session.State.Offer;

        for (int i = 0; i < offer.Count; i++)
        {
            if (_catalog.Skill(offer[i]).Kind == kind)
            {
                _session.ChooseOffer(i);
                return;
            }
        }

        Assert.Fail($"The offer of {offer.Count} held no {kind}, so this row could not be run.");
    }

    /// <summary>
    /// One frame of the callout being up, with a delta this fixture chooses.
    /// </summary>
    /// <remarks>
    /// <c>Tick</c> rather than <c>Update</c>, because <c>Time.unscaledDeltaTime</c> does not advance
    /// between reflective calls in EditMode and a dwell row driven by it would measure the Editor's
    /// frame time. The adapter is still read from inside <c>Tick</c>, which is what keeps
    /// <see cref="Hint_DismissesOnTap"/> a test of rule 9 rather than of a bool.
    /// </remarks>
    private void Tick(float unscaledDt)
    {
        typeof(FirstActiveHint)
            .GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_hint, new object[] { unscaledDt });
    }

    /// <summary>
    /// The callout's own <c>OnDestroy</c>. Invoked because Unity does not call it on an object whose
    /// <c>Awake</c> never ran, and none does here.
    /// </summary>
    private void Destroy()
    {
        typeof(FirstActiveHint)
            .GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_hint, null);
    }

    private void SetDwell(float seconds) => SetPrivate(_hint, "_secondsShown", seconds);

    private string Text() => Field<TMP_Text>(_hint, "_text").text;

    private static T Field<T>(object target, string name) =>
        (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);

    private static void SetPrivate(object target, string name, object value) =>
        target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);

    /// <summary>One node of the requested kind, built to what <c>SkillSpec</c> accepts.</summary>
    /// <remarks>
    /// An Active must carry an <c>ActiveSpec</c> or it is a skill that cannot fire (CH §4) — found
    /// by the validators rather than read off the spec, which is <c>LevelUpPresenterTests</c>'
    /// finding inherited.
    /// </remarks>
    private static SkillSpec Node(string id, SkillKind kind) => kind == SkillKind.Active
        ? new SkillSpec(
            new ContentId(id),
            new LocKey($"{id}.name"),
            new LocKey($"{id}.desc"),
            SkillKind.Active,
            Array.Empty<IEffect>(),
            new ActiveSpec(
                6f,
                new TriggerSpec(new[]
                {
                    // Below 1.5 rather than a clause that reads as "always": HpFraction is at most
                    // 1, so this holds on every tick of a healthy run and of a hurt one.
                    new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 1.5f),
                }),
                new IEffect[]
                {
                    new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.1f),
                }))
        : new SkillSpec(
            new ContentId(id),
            new LocKey($"{id}.name"),
            new LocKey($"{id}.desc"),
            kind,
            new IEffect[]
            {
                new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f),
            });

    /// <summary>
    /// Three Actives in branch a's first tier and one Passive in each of the other two, so that
    /// every offer holds both kinds and a third Active is still available after two are taken.
    /// </summary>
    private static SkillTreeSpec Tree() => new SkillTreeSpec(
        new ContentId(TreeId),
        new ContentId(OathboundId),
        new[]
        {
            Branch('a', new[] { ActiveOne, ActiveTwo, ActiveThree }),
            Branch('b', new[] { PassiveOne }),
            Branch('c', new[] { PassiveTwo }),
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
        new LocKey("character.oathbound.description"),
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
    /// Publishes into the run's hub <em>and</em> into a recorder, so a row can both watch the
    /// callout react and count what core said.
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
