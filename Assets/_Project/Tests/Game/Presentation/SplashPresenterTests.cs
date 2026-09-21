using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// CH §5.4's moment on screen: classes, then branches, one tap each — and no way off it but
/// through. M5-07a-ii; GD §13.1, §16.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over a real <c>RunSession</c> and a real <c>DomainEventHub</c></b>, which is
/// <c>LevelUpPresenterTests</c>' shape and the only shape that would catch this screen's own
/// failure: a branch drawn from a class the run cannot actually borrow from. The hub <em>is</em> the
/// session's <c>IDomainEvents</c>, so every event a row reacts to was published by core.
/// </para>
/// <para>
/// <b>The screen under test is the shipped prefab.</b> Hand-building one would test a second screen
/// that happens to resemble the asset, and the failure Traps §5 describes — a component that
/// deserialises as null with nothing reporting it — is invisible to a fixture that never loads it.
/// </para>
/// <para>
/// <b>The pause is not this screen's, and <see cref="Construct_TakesNoRunPause"/> holds that line at
/// the constructor.</b> <c>RunTicker.LevelUpPhase</c> raises and lowers <c>PauseReason.Splash</c>
/// (rule 5), so the run being stopped is a once-a-frame function of core state rather than a
/// consequence of a view having received an event.
/// </para>
/// <para>
/// <c>Awake</c> and <c>Start</c> do not run in EditMode (Traps §5), so the prefab's own alpha 0 is
/// what leaves the screen down at the top of each row, and <see cref="Start"/> is invoked by
/// reflection where a row needs the static label written.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SplashPresenterTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/Splash.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string GravecallerId = "character.gravecaller";
    private const string HuskId = "enemy.husk";
    private const string ArenaId = "arena.pillars";

    private const string OathboundTreeId = "tree.oathbound";
    private const string GravecallerTreeId = "tree.gravecaller";

    private const string LentActive = "skill.gravecaller.exhume";
    private const string LentMaxHp = "skill.gravecaller.grave-vigour";
    private const string LentA = "skill.gravecaller.rot";
    private const string LentB = "skill.gravecaller.legion";
    private const string Unhandled = "skill.gravecaller.shove";
    private const string MinionAimed = "skill.gravecaller.army";
    private const string LenderKeystone = "skill.gravecaller.keystone";

    private const int Capacity = 32;
    private const int DeviceCap = 16;
    private const int ProjectileCapacity = 8;

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private readonly List<GameObject> _spawned = new List<GameObject>();

    private DomainEventHub _hub;
    private RecordingEvents _spy;
    private FixedRandom _random;
    private ContentCatalog _catalog;
    private RunSession _session;

    private GameObject _screen;
    private SplashPresenter _presenter;

    [SetUp]
    public void CreateWorld()
    {
        _hub = new DomainEventHub();
        _spy = new RecordingEvents();
        _random = new FixedRandom(7, Enumerable.Repeat(0.999f, 256).ToArray());
    }

    [TearDown]
    public void DestroyWorld()
    {
        foreach (GameObject go in _spawned)
        {
            // Unity's ==: a destroyed object is a live reference that only compares equal to null
            // through the engine's operator.
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        _spawned.Clear();

        _hub?.Dispose();
    }

    // ---- Rule 7: two pages, one tap each --------------------------------------------------------

    [Test]
    public void Screen_DrawsTheCandidatesThenTheBranches()
    {
        StartRun();
        BuildScreen();

        Assert.That(_presenter.IsShown, Is.False, "the prefab ships down.");

        Open();

        Assert.That(_presenter.IsShown, Is.True, "the moment opened and the screen stayed down.");
        Assert.That(_presenter.IsOnBranchPage, Is.False, "page one is the class page.");

        IReadOnlyList<ClassCard> cards = Cards();

        // One candidate — the shipped two minus this run's own (rule 3) — so one card is bound and
        // the other two are cleared rather than drawn empty.
        Assert.That(cards[0].IsShown, Is.True);
        Assert.That(cards[0].CharacterId.Value, Is.EqualTo(GravecallerId));
        Assert.That(cards[1].IsShown, Is.False);
        Assert.That(cards[2].IsShown, Is.False);

        Tap(cards[0]);

        Assert.That(_presenter.IsOnBranchPage, Is.True, "the tap did not turn the page.");
        Assert.That(_presenter.IsShown, Is.True, "a class tap closed the screen.");
        Assert.That(_session.State.HasSplashed, Is.False, "a class tap borrowed a branch.");

        // Page two: the chosen class's three branches, by their own NameKeys and in branch order.
        Assert.That(
            BranchNames(),
            Is.EqualTo(new[]
            {
                "tree.gravecaller.legion",
                "tree.gravecaller.grave-work",
                "tree.gravecaller.rot",
            }));
    }

    [Test]
    public void Screen_TheBranchCountExcludesTheKeystone()
    {
        StartRun();

        // The shipped table, because the line this row reads is a *format* — a key with no row
        // resolves to itself and carries no placeholder, so a passthrough localizer would draw no
        // number at all and the row would be about nothing.
        BuildScreen(new TableLocalizer(EnglishTable()));

        Open();

        Tap(Cards()[0]);

        // The lender's branch 1 is CH §5's nine — eight nodes and a Keystone — and CH §5.4 does not
        // lend the Keystone (M5-07a-i rule 3). A line drawn from the branch's own NodeCount would
        // promise a node the player cannot have.
        Assert.That(Text(BranchCounts()[1]), Does.Contain("8"));
        Assert.That(Text(BranchCounts()[1]), Does.Not.Contain("9"));

        Assert.That(Text(BranchCounts()[0]), Does.Contain("4"), "the four-node branch.");
        Assert.That(Text(BranchCounts()[2]), Does.Contain("3"), "a branch with no Keystone.");
    }

    [Test]
    public void Screen_HasNoWayOutButThrough()
    {
        StartRun();
        BuildScreen();

        Open();

        // **Every interactive element on this screen either picks a class or picks a branch.**
        // CH §5.4: "the choice is mandatory", "there is no stay-pure option" — so there is no Back
        // on page two, and none on page one either, where ClassSelectPresenter has one.
        var accounted = new HashSet<Button>(BranchButtons().Where(button => button != null));

        foreach (ClassCard card in Cards())
        {
            accounted.Add(ButtonOn(card));
        }

        foreach (Button button in _screen.GetComponentsInChildren<Button>(includeInactive: true))
        {
            Assert.That(
                accounted,
                Does.Contain(button),
                $"'{button.name}' is a button on Splash.prefab that is neither a class card nor a "
                    + "branch row. CH §5.4's choice is mandatory, so a third kind of tap is a way "
                    + "off a screen that must not have one.");
        }

        // And the run is still un-splashed with the screen still up, which is the same claim from
        // the other end: nothing that has been tapped so far has answered the question.
        Tap(Cards()[0]);

        Assert.That(_presenter.IsShown, Is.True);
        Assert.That(_session.State.IsSplashOpen, Is.True, "core still holds the question open.");
        Assert.That(_session.State.HasSplashed, Is.False);
    }

    [Test]
    public void Screen_ATapOnABranchBorrowsItAndCloses()
    {
        StartRun();
        BuildScreen();

        Open();

        Tap(Cards()[0]);

        // The latch a class tap drops is lifted by a frame and by nothing else — see Frame().
        Frame();

        TapBranch(0);

        Assert.That(_session.State.HasSplashed, Is.True, "the tap borrowed nothing.");
        Assert.That(_spy.Count<SplashChosen>(), Is.EqualTo(1));
        Assert.That(_spy.Of<SplashChosen>()[0].NodesGained, Is.EqualTo(4));

        Assert.That(_presenter.IsShown, Is.False, "the screen stayed up over an answered question.");
        Assert.That(_session.State.IsSplashOpen, Is.False);
    }

    /// <summary>
    /// <c>LevelUpPresenter</c>'s latch, one screen over: a second tap in the same
    /// <c>EventSystem</c> pass would reach a <c>ChooseSplash</c> for a moment that is no longer open,
    /// which throws.
    /// </summary>
    [Test]
    public void Screen_ATapIsTakenOnce()
    {
        StartRun();
        BuildScreen();

        Open();

        Tap(Cards()[0]);

        Frame();

        // Two *different* rows rather than the same one twice, which is the stronger claim: uGUI
        // refuses a second touch on the button it hit and says nothing about the ones beside it.
        // No frame between them, which is what a double tap is: uGUI dispatches both from one
        // EventSystem pass, and only a new frame lifts the latch. Left unlatched, the second would
        // reach a ChooseSplash for a moment that is no longer open — which throws.
        TapBranch(0);

        Assert.DoesNotThrow(() => TapBranch(1));

        Assert.That(_spy.Count<SplashChosen>(), Is.EqualTo(1), "two taps borrowed two branches.");
        Assert.That(_spy.Of<SplashChosen>()[0].Branch, Is.Zero, "the second tap overwrote the first.");
    }

    // ---- AR §11.5: no raw English ---------------------------------------------------------------

    [Test]
    public void Screen_DrawsNoRawEnglish()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        var shipped = new TableLocalizer(EnglishTable());
        TMP_Text[] labels = prefab.GetComponentsInChildren<TMP_Text>(true);

        Assert.That(labels, Is.Not.Empty, "the prefab has no TMP_Text at all, so this row tests nothing.");

        var authored = new List<string>();

        foreach (TMP_Text label in labels)
        {
            if (string.IsNullOrEmpty(label.text))
            {
                continue;
            }

            Assert.That(
                shipped.Has(new LocKey(label.text)),
                Is.True,
                $"'{label.text}' is typed into {PrefabPath} and English.asset does not answer it. "
                    + "AR §11.5: no raw user-facing string anywhere.");

            authored.Add(label.text);
        }

        // The two keys the screen's own fields claim, and no third somebody dropped on a card —
        // which would draw a fixed string for every class. The other two keys this screen owns are
        // written per page and per branch, so they are never authored on the asset.
        Assert.That(
            authored,
            Is.EquivalentTo(new[] { "ui.splash.title", "ui.splash.class" }),
            "a label on this prefab draws a key the screen does not own.");
    }

    [Test]
    public void Screen_EveryKeyItDrawsResolves()
    {
        var shipped = new TableLocalizer(EnglishTable());

        foreach (string key in new[]
        {
            "ui.splash.title", "ui.splash.class", "ui.splash.branch", "ui.splash.nodes",
        })
        {
            Assert.That(shipped.Has(new LocKey(key)), Is.True, $"English.asset has no row for {key}.");
        }

        // The node count is a table row rather than a const format (ClassCard's "{0:0} HP" is the
        // other call), so it has to carry the placeholder or every branch would read "nodes".
        Assert.That(shipped.Get(new LocKey("ui.splash.nodes")), Does.Contain("{0}"));
    }

    [Test]
    public void Screen_ItsOwnLabelsAreWords()
    {
        StartRun();
        BuildScreen(new TableLocalizer(EnglishTable()));

        RunStart();

        Assert.That(Label("_title"), Is.EqualTo("Borrow a discipline"));

        Open();

        Assert.That(Label("_prompt"), Is.EqualTo("Choose a class"));

        Tap(Cards()[0]);

        Assert.That(Label("_prompt"), Is.EqualTo("Choose a branch"));
        Assert.That(Text(BranchCounts()[0]), Is.EqualTo("4 nodes"));
    }

    // ---- Rule 5: the gate is the ticker's -------------------------------------------------------

    [Test]
    public void Construct_TakesNoRunPause()
    {
        MethodInfo construct = typeof(SplashPresenter)
            .GetMethod(nameof(SplashPresenter.Construct), BindingFlags.Instance | BindingFlags.Public);

        Assert.That(construct, Is.Not.Null, "SplashPresenter has no public Construct.");

        Assert.That(
            construct.GetParameters().Select(parameter => parameter.ParameterType),
            Has.No.Member(typeof(RunPause)),
            "The screen was handed the pause. RunTicker.LevelUpPhase raises and lowers "
                + "PauseReason.Splash (rule 5), and a screen that could resume the run would be one "
                + "the fight restarts underneath.");

        // And no field either, which is the half a constructor cannot say.
        Assert.That(
            typeof(SplashPresenter)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Select(field => field.FieldType),
            Has.No.Member(typeof(RunPause)));
    }

    // ---- Guards, implied rather than listed -----------------------------------------------------

    [Test]
    public void Construct_RefusesANullDependency()
    {
        StartRun();
        LoadScreen();

        ILocalizer localizer = Passthrough();

        Assert.Throws<ArgumentNullException>(
            () => _presenter.Construct(null, _session, _hub, _catalog, localizer));
        Assert.Throws<ArgumentNullException>(
            () => _presenter.Construct(_session, null, _hub, _catalog, localizer));
        Assert.Throws<ArgumentNullException>(
            () => _presenter.Construct(_session, _session, null, _catalog, localizer));
        Assert.Throws<ArgumentNullException>(
            () => _presenter.Construct(_session, _session, _hub, null, localizer));
        Assert.Throws<ArgumentNullException>(
            () => _presenter.Construct(_session, _session, _hub, _catalog, null));
    }

    [Test]
    public void Start_UndressedScreen_NamesWhatIsMissing()
    {
        StartRun();

        var bare = new GameObject("Bare", typeof(RectTransform));
        bare.SetActive(false);
        _spawned.Add(bare);

        SplashPresenter presenter = bare.AddComponent<SplashPresenter>();
        presenter.Construct(_session, _session, _hub, _catalog, Passthrough());

        TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(
            () => typeof(SplashPresenter).GetMethod("Start", Private).Invoke(presenter, null));

        Assert.That(thrown.InnerException, Is.TypeOf<MissingReferenceException>());
        Assert.That(thrown.InnerException.Message, Does.Contain(nameof(CanvasGroup)));
    }

    [Test]
    public void Screen_IgnoresTheMomentWhenNoRunHasBegun()
    {
        // Pressing Play with the Run scene open, which is a workflow rather than a fault —
        // HudPresenter's argument, and the reason the handler reads State before it draws.
        CreateSession();
        BuildScreen();

        Assert.That(_session.State, Is.Null, "the fixture's premise: nothing has started a run.");

        Assert.DoesNotThrow(() => _hub.Publish(new SplashOffered(6, 6)));

        Assert.That(_presenter.IsShown, Is.False);
    }

    // ---- Fixture -----------------------------------------------------------------------------------

    /// <summary>Opens the moment through the command port, the way <c>RunTicker</c> does.</summary>
    private void Open()
    {
        Assert.That(
            ((IProgressionCommands)_session).IsSplashPending,
            Is.True,
            "the fixture's premise: the moment is owed.");

        ((IProgressionCommands)_session).OpenSplash();
    }

    /// <summary>
    /// A resumed run at the threshold: six of twelve taken, nothing owed, nothing borrowed.
    /// </summary>
    /// <remarks>
    /// Resumed rather than played to, for <c>LevelUpPresenterTests.StartRun</c>'s reason — six nodes
    /// is six level-ups of kills, and a fixture that walked there would be testing the XP curve.
    /// </remarks>
    private void StartRun()
    {
        CreateSession();

        _session.Start(new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            _random.Seed,
            1,
            SpawnPlan.Empty,
            new RunSnapshot(
                RunSnapshot.CurrentVersion,
                new ContentId(ModeId),
                new ContentId(OathboundId),
                _random.Seed,
                1,
                new RandomState(0, 0, 0, 0, 0),
                100f,
                0f,
                120f,
                Instant,
                7,
                0f,
                0,
                SixTaken(),
                new ContentId[SkillRunner.MaxManualSlots])));
    }

    /// <summary>The catalog and the session, with no run started — see the row that needs one.</summary>
    private void CreateSession()
    {
        _catalog = new ContentCatalog(
            new[] { Character(OathboundId, 140f), Character(GravecallerId) },
            new[] { Husk() },
            new[] { Mode() },
            AllSkills(),
            new[] { OathboundTree(), GravecallerTree() });

        var events = new ForkedEvents(_hub, _spy);

        _session = new RunSession(
            _catalog,
            _random,
            events,
            new RecordingIntents(),
            new RunRecorder(_random, new FixedClock(Instant), events),
            Capacity,
            DeviceCap,
            ProjectileCapacity);
    }

    private void BuildScreen(ILocalizer localizer = null)
    {
        LoadScreen();

        // What RunScope's RegisterComponent does. Start never runs in EditMode, so nothing else on
        // this object has fired.
        _presenter.Construct(_session, _session, _hub, _catalog, localizer ?? Passthrough());
    }

    private void LoadScreen()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        _screen = Object.Instantiate(prefab);
        _spawned.Add(_screen);

        _presenter = _screen.GetComponent<SplashPresenter>();

        Assert.That(
            _presenter,
            Is.Not.Null,
            "SplashPresenter did not load off its own prefab (Traps §5).");
    }

    /// <summary>Runs the presenter's <c>Start</c>, which EditMode never does for it.</summary>
    private void RunStart() =>
        typeof(SplashPresenter).GetMethod("Start", Private).Invoke(_presenter, null);

    /// <summary>
    /// One frame of the screen's own <c>Update</c>, which EditMode does not run (Traps §5).
    /// </summary>
    /// <remarks>
    /// It does exactly one thing — lift the tap latch — and that is why it has to be explicit here:
    /// in a run <c>Update</c> fires at <c>timeScale</c> 0 like any other, so a player who taps a
    /// class and then a branch is two frames apart whatever their thumb does.
    /// </remarks>
    private void Frame() =>
        typeof(SplashPresenter).GetMethod("Update", Private).Invoke(_presenter, null);

    private static void Tap(ClassCard card) => ButtonOn(card).onClick.Invoke();

    private void TapBranch(int row) => BranchButtons()[row].onClick.Invoke();

    private static Button ButtonOn(ClassCard card) =>
        (Button)typeof(ClassCard).GetField("_button", Private).GetValue(card);

    private ClassCard[] Cards() => Field<ClassCard[]>("_cards");

    private Button[] BranchButtons() => Field<Button[]>("_branchButtons");

    private TMP_Text[] BranchNamesLabels() => Field<TMP_Text[]>("_branchNames");

    private TMP_Text[] BranchCounts() => Field<TMP_Text[]>("_branchCounts");

    private string[] BranchNames() =>
        BranchNamesLabels().Select(Text).ToArray();

    private string Label(string field) => Text(Field<TMP_Text>(field));

    private static string Text(TMP_Text label) => label == null ? null : label.text;

    private T Field<T>(string name) =>
        (T)typeof(SplashPresenter).GetField(name, Private).GetValue(_presenter);

    private static LocalizationTable EnglishTable()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No LocalizationTable at {EnglishPath}.");

        return table;
    }

    /// <summary>The real adapter over an empty table: every key resolves to itself.</summary>
    private static ILocalizer Passthrough() =>
        new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>());

    // ---- Content ---------------------------------------------------------------------------------

    /// <summary>Six primary ids in an order the gating allows — branch a whole, then branch b's tier 1.</summary>
    private static ContentId[] SixTaken() => new[]
    {
        Primary('a', 1, 'a'), Primary('a', 1, 'b'), Primary('a', 2, 'a'), Primary('a', 2, 'b'),
        Primary('b', 1, 'a'), Primary('b', 1, 'b'),
    };

    /// <summary>M3-12's v1 shape: three branches of two tiers of two, twelve nodes, no Keystone.</summary>
    private static SkillTreeSpec OathboundTree() => new SkillTreeSpec(
        new ContentId(OathboundTreeId),
        new ContentId(OathboundId),
        new[] { PrimaryBranch('a'), PrimaryBranch('b'), PrimaryBranch('c') });

    private static SkillBranchSpec PrimaryBranch(char letter) => new SkillBranchSpec(
        new LocKey($"tree.oathbound.{letter}"),
        new IReadOnlyList<ContentId>[]
        {
            new[] { Primary(letter, 1, 'a'), Primary(letter, 1, 'b') },
            new[] { Primary(letter, 2, 'a'), Primary(letter, 2, 'b') },
        });

    /// <summary>
    /// The lender: a four-node branch, CH §5's nine ending in a Keystone, and three more.
    /// </summary>
    private static SkillTreeSpec GravecallerTree() => new SkillTreeSpec(
        new ContentId(GravecallerTreeId),
        new ContentId(GravecallerId),
        new[]
        {
            new SkillBranchSpec(
                new LocKey("tree.gravecaller.legion"),
                new IReadOnlyList<ContentId>[]
                {
                    new[] { Id(LentActive), Id(LentMaxHp) },
                    new[] { Id(LentA), Id(LentB) },
                }),
            new SkillBranchSpec(
                new LocKey("tree.gravecaller.grave-work"),
                new IReadOnlyList<ContentId>[]
                {
                    new[] { Id(Unhandled), GraveWork(1, 'b') },
                    new[] { GraveWork(2, 'a'), GraveWork(2, 'b') },
                    new[] { GraveWork(3, 'a'), GraveWork(3, 'b') },
                    new[] { GraveWork(4, 'a'), GraveWork(4, 'b') },
                    new[] { Id(LenderKeystone) },
                }),
            new SkillBranchSpec(
                new LocKey("tree.gravecaller.rot"),
                new IReadOnlyList<ContentId>[]
                {
                    new[] { Id(MinionAimed) },
                    new[] { Id("skill.gravecaller.horde") },
                    new[] { Id("skill.gravecaller.blight") },
                }),
        });

    private static IReadOnlyList<SkillSpec> AllSkills()
    {
        var skills = new List<SkillSpec>();

        foreach (char letter in new[] { 'a', 'b', 'c' })
        {
            skills.Add(Passive(Primary(letter, 1, 'a')));
            skills.Add(Passive(Primary(letter, 1, 'b')));
            skills.Add(Passive(Primary(letter, 2, 'a')));
            skills.Add(Passive(Primary(letter, 2, 'b')));
        }

        skills.Add(Passive(Id(LentActive)));
        skills.Add(Passive(Id(LentMaxHp)));
        skills.Add(Passive(Id(LentA)));
        skills.Add(Passive(Id(LentB)));
        skills.Add(Node(Id(Unhandled), new KnockbackOnSwing(2f)));
        skills.Add(Node(
            Id(MinionAimed),
            new ModifyStat(PlayerStat.MaxHp, ModifierKind.PercentAdd, 0.1f, StatTarget.Minions)));
        skills.Add(Passive(Id("skill.gravecaller.horde")));
        skills.Add(Passive(Id("skill.gravecaller.blight")));
        skills.Add(Passive(GraveWork(1, 'b')));

        for (int tier = 2; tier <= 4; tier++)
        {
            skills.Add(Passive(GraveWork(tier, 'a')));
            skills.Add(Passive(GraveWork(tier, 'b')));
        }

        skills.Add(new SkillSpec(
            Id(LenderKeystone),
            new LocKey($"{LenderKeystone}.name"),
            new LocKey($"{LenderKeystone}.desc"),
            SkillKind.Keystone,
            new IEffect[] { new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.2f) }));

        return skills;
    }

    private static SkillSpec Passive(ContentId id) =>
        Node(id, new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f));

    private static SkillSpec Node(ContentId id, IEffect effect) => new SkillSpec(
        id,
        new LocKey($"{id.Value}.name"),
        new LocKey($"{id.Value}.desc"),
        SkillKind.Passive,
        new[] { effect });

    private static CharacterSpec Character(string id, float maxHp = 100f) => new CharacterSpec(
        Id(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.description"),
        maxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        minions: id == GravecallerId
            ? new MinionSpec(
                Id("enemy.wight"), new LocKey("enemy.wight.name"), 4, 20f, 0.25f, 20f, 3f, 4f, 1f, 1.2f)
            : null);

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

    private static ModeSpec Mode() => new ModeSpec(
        new ContentId(ModeId),
        new LocKey("mode.test.name"),
        1,
        true,
        0,
        new ScalingSpec(
            new BudgetCurve(20f, 6f, 0.04f),
            new WaveCurve(2, 1000, 2, 2),
            new ConcurrencyCurve(DeviceCap, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0)),

        // CH §5.2's shipped levelling curve, written out rather than taken from Tests.Core's
        // `Scalings` — that helper is `internal` and this fixture is in another assembly.
        new XpCurve(20f, 12f, 1.4f),
        Array.Empty<RosterEntry>(),
        new[] { new ContentId(ArenaId) },
        null,
        new OverflowSpec(0.02f, 0.02f));

    private static ContentId Primary(char branch, int tier, char slot) =>
        Id($"skill.oathbound.{branch}{tier}{slot}");

    private static ContentId GraveWork(int tier, char slot) =>
        Id($"skill.gravecaller.gw{tier}{slot}");

    private static ContentId Id(string value) => new ContentId(value);

    /// <summary>
    /// Publishes into the run's hub <em>and</em> into a recorder, so a row can both watch the screen
    /// react and count what core said. <c>LevelUpPresenterTests</c>' fake.
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
