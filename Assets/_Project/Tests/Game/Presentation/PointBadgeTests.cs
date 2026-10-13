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
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Core.Stage;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-03b: the green count of unspent skill points on the level star, and the "LEVEL UP!" toast.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over a real <c>RunSession</c> and the shipped <c>Hud.prefab</c></b>, <c>XpBarViewTests</c>' shape
/// and its reasons: the badge is bound by <c>HudPresenter</c>, and a hand-built one would test a screen
/// that happens to resemble the asset. The hub <em>is</em> the session's <c>IDomainEvents</c>, so
/// <c>NodeTaken</c> and <c>OverflowGranted</c> are published by core, in core's order — which is what
/// makes <see cref="Badge_FollowsNodeTaken"/> a row about the badge rather than about the fixture:
/// <c>NodeTaken</c> is published <em>before</em> the point is spent.
/// </para>
/// <para>
/// <b>Levels arrive through a save.</b> A snapshot carrying the level, the nodes taken and the points
/// still owed puts the run where each rule is about, which is <c>FreePickTests</c>' bargain. A
/// <c>LeveledUp</c> published here is therefore a stand-in for the toast's trigger only — the count is
/// always the run's own.
/// </para>
/// <para>
/// <c>Awake</c>, <c>Start</c> and <c>LateUpdate</c> do not run on an instantiated prefab in EditMode
/// (Traps §5), so each is invoked by hand, and the toast's clock is driven through <c>Tick</c>.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PointBadgeTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string TreeId = "tree.oathbound";
    private const string ArenaId = "arena.pillars";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    private static readonly string[] Nodes =
    {
        "skill.test.one", "skill.test.two", "skill.test.three", "skill.test.four", "skill.test.five",
    };

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private readonly List<GameObject> _spawned = new List<GameObject>();

    private DomainEventHub _hub;
    private RunSession _session;
    private RunConfig _config;
    private float _restoreTimeScale;

    private GameObject _hud;
    private HudPresenter _presenter;
    private PointBadge _badge;

    [SetUp]
    public void CreateWorld()
    {
        // The Editor's own global: one row writes timeScale on purpose, so a row that failed part way
        // would leave every later one frozen — XpBarViewTests' argument.
        _restoreTimeScale = Time.timeScale;

        _hub = new DomainEventHub();
    }

    [TearDown]
    public void DestroyWorld()
    {
        Time.timeScale = _restoreTimeScale;

        foreach (GameObject go in _spawned)
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        _spawned.Clear();

        _hub?.Dispose();

        _session = null;
        _presenter = null;
        _badge = null;
    }

    // ---- Rules 1, 4: the count --------------------------------------------------------------------

    [Test]
    public void Badge_ShowsPoints_HidesAtZero()
    {
        StartRun(free: true, pending: 0);
        BuildHud();

        Assert.That(_badge.Shown, Is.Zero);
        Assert.That(DiscObject().activeSelf, Is.False, "a badge with nothing to say is on screen.");

        // The same HUD over a run holding three: a fresh world, because the HUD follows one session.
        DestroyWorld();
        CreateWorld();
        StartRun(free: true, pending: 3);
        BuildHud();

        Assert.That(_badge.Shown, Is.EqualTo(3));
        Assert.That(DiscObject().activeSelf, Is.True);
        Assert.That(CountText(), Is.EqualTo("3"));
    }

    [Test]
    public void Badge_FollowsNodeTaken()
    {
        StartRun(free: true, pending: 3);
        BuildHud();

        Assert.That(_badge.Shown, Is.EqualTo(3));

        _session.TakeNode(new ContentId(Nodes[0]));
        Flush();

        Assert.That(_session.State.SkillPoints, Is.EqualTo(2), "the fixture's premise: a node spent a point.");
        Assert.That(
            _badge.Shown,
            Is.EqualTo(2),
            "NodeTaken is published before the point is spent, so a handler that read the run there "
                + "drew 3 for good.");
        Assert.That(CountText(), Is.EqualTo("2"));
    }

    [Test]
    public void Badge_FollowsOverflow()
    {
        // A full tree, two points owed: level 8 is 1 + 5 taken + 2 pending.
        StartRun(free: true, pending: 2, taken: Nodes);
        BuildHud();

        Assert.That(_badge.Shown, Is.EqualTo(2));

        _session.SettleFullTree();
        Flush();

        Assert.That(_session.State.SkillPoints, Is.Zero, "the fixture's premise: both points became Overflow.");
        Assert.That(_badge.Shown, Is.Zero);
        Assert.That(DiscObject().activeSelf, Is.False);
    }

    [Test]
    public void Badge_SameNumber_IsNotRewritten()
    {
        StartRun(free: true, pending: 3);
        BuildHud();

        int written = Writes();

        Assert.That(written, Is.EqualTo(1), "the opening draw is one write.");

        // An event that moved nothing, twice: the run still holds 3.
        _hub.Publish(new NodeTaken(new ContentId(Nodes[0]), SkillKind.Passive, 0, 1, 1));
        Flush();
        _hub.Publish(new OverflowGranted(4, 1));
        Flush();

        Assert.That(Writes(), Is.EqualTo(written), "an unchanged number was written again (UI-01d).");
    }

    // ---- Rule 3: the toast ------------------------------------------------------------------------

    [Test]
    public void Toast_LastsOnePointFiveUnscaledSeconds()
    {
        StartRun(free: true, pending: 1);
        BuildHud();

        Time.timeScale = 0f;

        _hub.Publish(new LeveledUp(2, 1));

        Assert.That(_badge.ToastVisible, Is.True);

        // Unscaled seconds handed in directly: a scaled clock would never get past the first.
        Tick(1.4f);

        Assert.That(_badge.ToastVisible, Is.True, "gone before 1.5 s.");

        Tick(0.2f);

        Assert.That(_badge.ToastVisible, Is.False, "still up at 1.6 s.");
    }

    [Test]
    public void Toast_SecondLevel_Restarts()
    {
        StartRun(free: true, pending: 2);
        BuildHud();

        _hub.Publish(new LeveledUp(2, 1));
        Tick(1.0f);
        _hub.Publish(new LeveledUp(3, 2));

        Assert.That(_badge.ToastVisible, Is.True);

        Tick(1.0f);

        Assert.That(_badge.ToastVisible, Is.True, "the second level did not restart the dwell.");

        Tick(0.6f);

        Assert.That(_badge.ToastVisible, Is.False);
    }

    [Test]
    public void Toast_BlocksNoTouch()
    {
        StartRun(free: true, pending: 1);
        BuildHud();

        _hub.Publish(new LeveledUp(2, 1));
        Flush();

        Graphic[] graphics = _badge.GetComponentsInChildren<Graphic>(true);

        Assert.That(graphics, Is.Not.Empty, "the badge draws nothing.");

        foreach (Graphic graphic in graphics)
        {
            Assert.That(graphic.raycastTarget, Is.False, $"{graphic.name} would eat a touch meant for the stick.");
        }

        var toast = _badge.Toast.GetComponent<CanvasGroup>();

        Assert.That(toast.blocksRaycasts, Is.False);
        Assert.That(toast.interactable, Is.False);
    }

    [Test]
    public void Toast_SaysTheLocalisedWord()
    {
        StartRun(free: true, pending: 1);
        BuildHud();

        // Passthrough resolves a key to itself, so the word on screen is the key the code asked for.
        Assert.That(_badge.Toast.GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("ui.hud.levelup"));
    }

    [Test]
    public void Text_ClearsTheAndroidFloor()
    {
        StartRun(free: true, pending: 1);
        BuildHud();

        float px = Soulvail.Game.Controls.StickShaper.PixelsPerDp(Screen.dpi);

        // Drawn in dp, so the floor is a dp: M4-07's 12 sp, which is 12 dp at the default font scale.
        Assert.That(_badge.Count.fontSize / px, Is.GreaterThanOrEqualTo(12f - 1e-3f), "the count is under Android's floor.");
        Assert.That(_badge.ToastLabel.fontSize / px, Is.GreaterThanOrEqualTo(12f - 1e-3f), "the toast is under Android's floor.");
    }

    // ---- Rule 2: the colour -----------------------------------------------------------------------

    [Test]
    public void Badge_IsPaletteGreen()
    {
        StartRun(free: true, pending: 1);
        BuildHud();

        var disc = Field<Image>(_badge, "_disc");

        Assert.That(disc.color, Is.EqualTo(Palette.Available));
        Assert.That(Palette.IsDanger(Palette.Available), Is.False, "the point badge is the reserved red-orange.");
        Assert.That(Palette.Available.g, Is.GreaterThan(Palette.Available.r));
        Assert.That(Palette.Available.g, Is.GreaterThan(Palette.Available.b));
    }

    // ---- Rules 5, 6: when it is off, and when it clears --------------------------------------------

    [Test]
    public void NotFree_BadgeAndToastStayOff()
    {
        StartRun(free: false, pending: 2);
        BuildHud();

        _hub.Publish(new LeveledUp(3, 2));
        _hub.Publish(new NodeTaken(new ContentId(Nodes[0]), SkillKind.Passive, 0, 1, 1));
        _hub.Publish(new OverflowGranted(4, 1));
        Flush();

        Assert.That(_badge.Shown, Is.Zero, "a drawing run counted points nobody can spend.");
        Assert.That(DiscObject().activeSelf, Is.False);
        Assert.That(_badge.ToastVisible, Is.False, "the old offer screen owns this flow.");
    }

    [Test]
    public void Resume_ShowsBankedPoints()
    {
        // The HUD up before the run starts, as a scene is: the badge reads the points off RunStarted,
        // not off any tick.
        CreateRun(free: true, pending: 3);
        BuildHud();

        Assert.That(_badge.Shown, Is.Zero, "a badge in a scene where no run has begun drew something.");

        _session.Start(_config);

        Assert.That(_badge.Shown, Is.EqualTo(3), "the banked points were restored silently and never drawn.");
        Assert.That(_badge.ToastVisible, Is.False, "a resume announced a level from a previous session.");
    }

    [Test]
    public void RunEnd_ClearsBoth()
    {
        StartRun(free: true, pending: 2);
        BuildHud();

        _hub.Publish(new LeveledUp(3, 2));

        Assert.That(_badge.Shown, Is.EqualTo(2));
        Assert.That(_badge.ToastVisible, Is.True);

        _hub.Publish(new RunEnded(10f));

        Assert.That(_badge.Shown, Is.Zero);
        Assert.That(_badge.ToastVisible, Is.False);
    }

    // ---- Fixture ----------------------------------------------------------------------------------

    private void StartRun(bool free, int pending, string[] taken = null)
    {
        CreateRun(free, pending, taken);

        _session.Start(_config);
    }

    /// <summary>A resumed run, not started — <c>XpBarViewTests.CreateRun</c>'s shape.</summary>
    private void CreateRun(bool free, int pending, string[] taken = null)
    {
        IReadOnlyList<SkillSpec> skills = Nodes.Select(Node).ToArray();

        var catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Husk() },
            new[] { Mode(free) },
            skills,
            new[] { Tree() });

        var random = new FixedRandom(7, Alternating(8_192));
        var clock = new FixedClock(Instant);

        _session = new RunSession(
            catalog,
            random,
            _hub,
            new RecordingIntents(),
            new RunRecorder(random, clock, _hub),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        // A level has to account for what is taken and what is owed, or the run owes Overflow it never
        // earned (M3-08a rule 9): 1 + taken + pending. A full tree with two owed is level 8.
        string[] takenIds = taken ?? Array.Empty<string>();
        int accounted = 1 + takenIds.Length + pending;

        _config = new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            random.Seed,
            1,
            SpawnPlan.Empty,
            new RunSnapshot(
                RunSnapshot.CurrentVersion,
                new ContentId(ModeId),
                new ContentId(OathboundId),
                random.Seed,
                1,
                new RandomState(101, 102, 103, 104, 105),
                90f,
                6f,
                120f,
                Instant,
                accounted,
                0f,
                pending,
                takenIds.Select(id => new ContentId(id)).ToArray(),
                new ContentId[SkillRunner.MaxManualSlots],
                default,
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>()));
    }

    /// <summary>The shipped HUD, with its presenter injected and started — the screen under test is the asset.</summary>
    private void BuildHud()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        _hud = Object.Instantiate(prefab);
        _spawned.Add(_hud);

        // Pinned, because a Canvas instantiated in EditMode has never been driven by its own scaler.
        _hud.GetComponent<Canvas>().scaleFactor = 1f;

        _presenter = _hud.GetComponent<HudPresenter>();
        _badge = _hud.GetComponentInChildren<PointBadge>(true);

        Assert.That(_presenter, Is.Not.Null, "HudPresenter did not load off Hud.prefab (Traps §5).");
        Assert.That(_badge, Is.Not.Null, "PointBadge did not load off Hud.prefab (Traps §5).");
        Assert.That(Field<PointBadge>(_presenter, "_badge"), Is.SameAs(_badge), "the presenter is not wired to the badge.");

        _presenter.Construct(_hub, _session, new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>()));

        Invoke(_presenter, "Start");
    }

    /// <summary>What the frame does after the call that raised an event: <c>LateUpdate</c>.</summary>
    private void Flush() => Invoke(_badge, "LateUpdate");

    private void Tick(float unscaledDt) =>
        typeof(PointBadge)
            .GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_badge, new object[] { unscaledDt });

    private GameObject DiscObject() => _badge.Badge.gameObject;

    private string CountText() => _badge.Count.text;

    private int Writes() => Field<int>(_badge, "_writes");

    private static void Invoke(object target, string method) =>
        target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);

    private static T Field<T>(object target, string name) =>
        (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);

    // ---- Content ----------------------------------------------------------------------------------

    private static SkillSpec Node(string id) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Passive,
        new IEffect[]
        {
            new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f),
        });

    /// <summary>One branch of three, one of one, one of one — every node tier 1, so any order is legal.</summary>
    private static SkillTreeSpec Tree() => new SkillTreeSpec(
        new ContentId(TreeId),
        new ContentId(OathboundId),
        new[]
        {
            Branch('a', new[] { Nodes[0], Nodes[1], Nodes[2] }),
            Branch('b', new[] { Nodes[3] }),
            Branch('c', new[] { Nodes[4] }),
        });

    private static SkillBranchSpec Branch(char letter, string[] tier) => new SkillBranchSpec(
        new LocKey($"branch.{letter}"),
        new IReadOnlyList<ContentId>[] { tier.Select(id => new ContentId(id)).ToArray() });

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
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

    private static ModeSpec Mode(bool free)
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
            new[] { new ArenaEntry(new ContentId(ArenaId)) },
            overflow: new OverflowSpec(0.02f, 0.02f),
            hasFreePick: free);
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
}
