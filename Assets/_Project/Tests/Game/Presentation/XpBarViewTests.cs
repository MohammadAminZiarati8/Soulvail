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
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// The progression row of the HUD: the XP strip along the top edge, the level number beside HP, and
/// the one announcement Overflow ever gets. GD §16.1, CH §5.2.
/// </summary>
/// <remarks>
/// <para>
/// <b>One fixture for three readouts, because the spec makes them one row of the HUD</b> — and
/// because all three live on the same prefab, which is what lets the two cross-cutting guard rows
/// (<see cref="Subscribes_FromConstruct"/>, <see cref="Destroy_DropsSubscriptions"/>) cover all
/// <em>three</em> of this task's components rather than the two this file otherwise owns. The row's
/// own membership, fills and tints are <c>AutoCastRowTests</c>'.
/// </para>
/// <para>
/// <b>Over a real <c>RunSession</c> and the shipped <c>Hud.prefab</c></b> — <c>SkillBarPresenterTests</c>'
/// shape and its reasons. The hub <em>is</em> the session's <c>IDomainEvents</c>, so a resumed run's
/// <c>RunStarted</c> and a spent pick's <c>OverflowGranted</c> are published by core rather than
/// staged here; a hand-built HUD would test a second screen that happens to resemble the asset, and
/// the failure Traps §5 describes — a component that deserialises as null with nothing reporting it —
/// is invisible to a fixture that never loads the asset.
/// </para>
/// <para>
/// <b>The two <c>Xp_*</c> event rows publish the sequence rather than earning it</b>, and that is a
/// statement about cost rather than about trust: earning two levels needs a stage's worth of kills,
/// which would make every row here a stage test. What makes the sequence real is M3-01a rule 5 —
/// every <c>LeveledUp</c> first, exactly one <c>XpChanged</c> last — and that ordering is
/// <c>LevelTrackerTests</c>', asserted there over the object that produces it. What is asserted here
/// is the strip's own half: that it draws the event and nothing else, and that nothing it writes is
/// outside <c>[0, 1]</c> even when something hands it a number that is.
/// </para>
/// <para>
/// <b>Three rows are the first assertions ever made about <c>HudPresenter</c>.</b> Nothing has ever
/// tested that class — <c>Place</c> has laid the HP row out since M1-17 with no row watching it — so
/// <see cref="Level_IsPlacedInTheHudRow"/> checks the whole row rather than only the rect this task
/// added, which is the cheapest moment there will ever be to do it.
/// </para>
/// <para>
/// <c>Awake</c> and <c>Start</c> do not run on an instantiated prefab in EditMode (Traps §5), so each
/// component is constructed by hand and its <c>Start</c> invoked where a row needs the placement and
/// the first draw that live there. <see cref="Subscribes_FromConstruct"/> deliberately does not, since
/// its whole claim is that <c>Construct</c> alone is enough.
/// </para>
/// </remarks>
[TestFixture]
public sealed class XpBarViewTests
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

    /// <summary>GD §16.1's strip height, quoted from the shipped constant rather than a literal.</summary>
    private const float StripDp = XpBarView.HeightDp;

    /// <summary>The key the toast draws until M3-14a gives it words — ledger row 9.</summary>
    private const string ToastKey = "ui.overflow.granted";

    /// <summary>
    /// What <see cref="ToastKey"/> resolves to — <c>English.asset</c>'s row, authored here so the
    /// fixture asserts against a table it built rather than against the shipped one.
    /// </summary>
    private const string ToastWord = "Overflow";

    /// <summary>Branch a's whole first tier — <c>LevelUpPresenterTests</c>' five-node tree.</summary>
    private const string NodeOne = "skill.test.one";
    private const string NodeTwo = "skill.test.two";
    private const string NodeThree = "skill.test.three";

    /// <summary>Branch b and branch c, one node each.</summary>
    private const string NodeFour = "skill.test.four";
    private const string NodeFive = "skill.test.five";

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private DomainEventHub _hub;
    private RecordingEvents _spy;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private RunSession _session;
    private RunConfig _config;

    private GameObject _hud;
    private XpBarView _strip;
    private OverflowToast _toast;
    private HudPresenter _presenter;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<IDisposable> _disposables = new List<IDisposable>();
    private readonly List<RunPause> _pauses = new List<RunPause>();

    private float _restoreTimeScale;

    [SetUp]
    public void CreateWorld()
    {
        // The Editor's own globals. One row writes timeScale on purpose and RunPause writes both, so
        // a row that failed part way through would otherwise leave the whole suite after it running
        // at 0 — PausePresenterTests' argument, made in the teardown rather than in each row.
        _restoreTimeScale = Time.timeScale;

        _hub = new DomainEventHub();
        _spy = new RecordingEvents();
        _random = new FixedRandom(7, Alternating(8_192));
        _clock = new FixedClock(Instant);
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
        _strip = null;
        _toast = null;
        _presenter = null;
    }

    // ---- The strip (rules 1, 2) ------------------------------------------------------------------

    [Test]
    public void Xp_FillFollowsTheEvent()
    {
        StartRun(level: 3);
        BuildHud();

        _hub.Publish(new XpChanged(12f, 3, 0.42f));

        // Rule 2: the event carries the fraction already settled, so the strip does no arithmetic
        // and asks the run nothing.
        Assert.That(_strip.Fraction, Is.EqualTo(0.42f).Within(1e-4f));
        Assert.That(Fill().fillAmount, Is.EqualTo(0.42f).Within(1e-4f));
    }

    [Test]
    public void Xp_NeverDrawsAboveOne()
    {
        StartRun(level: 1);
        BuildHud();

        // **M3-01a rule 5's ordering, from this side** (rule 2): a grant worth two levels publishes
        // both thresholds first and exactly one XpChanged last, so the strip is never handed the
        // over-full state that existed in between.
        _hub.Publish(new LeveledUp(2, 1));

        Assert.That(
            _strip.Fraction,
            Is.EqualTo(0f).Within(1e-4f),
            "a LeveledUp moved the strip, so the fill is being driven by two events rather than one.");

        _hub.Publish(new LeveledUp(3, 2));
        _hub.Publish(new XpChanged(240f, 3, 0.42f));

        Assert.That(_strip.Fraction, Is.EqualTo(0.42f).Within(1e-4f));

        // And the strip is the last line of defence rather than a believer: core cannot produce this,
        // and a fillAmount outside [0, 1] is the kind of thing uGUI renders as a full strip rather
        // than as an error — so the one place that could ever show it is the one place that could
        // never report it.
        foreach (float hostile in new[] { 1.4f, 12f, -0.3f, float.PositiveInfinity })
        {
            _hub.Publish(new XpChanged(1f, 3, hostile));

            Assert.That(
                _strip.Fraction,
                Is.InRange(0f, 1f),
                $"a fraction of {hostile} was written through to the strip.");
        }
    }

    [Test]
    public void Xp_DrawsTheOpeningStateOnRunStarted()
    {
        // A resumed run part-way through level 4, which no event describes — nothing has happened
        // yet, and HudPresenter's argument for RunStarted is the same one here.
        CreateRun(level: 4, xp: 30f);
        BuildHud();

        Assert.That(
            _strip.Fraction,
            Is.EqualTo(0f).Within(1e-4f),
            "a strip in a scene where no run has begun drew something.");

        _session.Start(_config);

        float expected = _session.State.XpFraction;

        Assert.That(expected, Is.GreaterThan(0f), "the fixture's premise: the saved run owes a part-level.");
        Assert.That(_strip.Fraction, Is.EqualTo(expected).Within(1e-4f));
    }

    [Test]
    public void Xp_StripIsFourDpAndFullWidth()
    {
        StartRun(level: 1);
        BuildHud();

        var rect = (RectTransform)_strip.transform;
        float pxPerDp = PixelsPerDp();

        // Four **dp**, converted at the screen boundary: a Scale-With-Screen-Size canvas measures in
        // reference pixels, and four of those is a different physical height on every phone (rule 1).
        Assert.That(rect.sizeDelta.y, Is.EqualTo(StripDp * pxPerDp).Within(1e-2f));

        // Full width, and the width is the anchors' rather than this file's arithmetic: 0 to 1 with a
        // zero sizeDelta.x is exactly the safe area's width however wide it turns out to be.
        Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(1f, 1f)));
        Assert.That(rect.sizeDelta.x, Is.EqualTo(0f).Within(1e-3f));

        // The very top edge, with the pivot on it so the strip hangs down rather than half off.
        Assert.That(rect.pivot, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(rect.anchoredPosition, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void Strip_IgnoresANonFiniteHeight()
    {
        StartRun(level: 1);
        BuildHud();

        var rect = (RectTransform)_strip.transform;
        Vector2 placed = rect.anchoredPosition;
        Vector2 sized = rect.sizeDelta;

        // **The strip is authored on Hud.prefab, so leaving the layout alone is an answer**
        // (SkillBarPresenter.Place's and PausePresenter.Place's, not TreeViewPresenter.Layout's) — a
        // tree cell is a runtime clone with nothing to fall back to, and this strip has the prefab's
        // own edge. **Zero is refused with the rest**, which is where this door is narrower than the
        // slot cluster's margin: a margin of 0 is a control flush to the safe area's corner and a
        // legal layout, while a strip 0 dp tall is GD §16.1's one always-there element silently gone.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -4f })
        {
            SetPrivate(_strip, "_heightDp", bad);

            Assert.That(() => Invoke(_strip, "Place"), Throws.Nothing, $"Place threw on {bad}.");

            Assert.That(rect.sizeDelta, Is.EqualTo(sized), $"height {bad}");
            Assert.That(rect.anchoredPosition, Is.EqualTo(placed), $"height {bad}");

            Assert.That(
                float.IsFinite(rect.sizeDelta.y),
                Is.True,
                $"a height of {bad} wrote a non-finite sizeDelta, which is a rect that never renders "
                    + "again.");
        }

        // And it *does* place when the field is usable, so the row above is about nonsense rather
        // than about a strip that has stopped being laid out at all.
        SetPrivate(_strip, "_heightDp", 8f);
        Invoke(_strip, "Place");

        Assert.That(rect.sizeDelta.y, Is.EqualTo(8f * PixelsPerDp()).Within(1e-2f));
    }

    // ---- The level (rule 3) ---------------------------------------------------------------------

    [Test]
    public void Level_ShowsAndUpdates()
    {
        StartRun(level: 1);
        BuildHud();

        Assert.That(LevelText(), Is.EqualTo("1"), "the run's opening level was not drawn.");

        // The first assertion ever made about HudPresenter, and it is about the one event that class
        // gained at M3-10b. The number comes off the event rather than out of the run: LeveledUp is
        // published once per level in threshold order (M3-01a rule 5).
        _hub.Publish(new LeveledUp(2, 1));

        Assert.That(LevelText(), Is.EqualTo("2"));

        // A grant worth two levels writes both in one frame, of which the player sees the second.
        _hub.Publish(new LeveledUp(3, 2));

        Assert.That(LevelText(), Is.EqualTo("3"));
    }

    [Test]
    public void Level_DrawsOnRunStarted()
    {
        CreateRun(level: 7);
        BuildHud();

        _session.Start(_config);

        // A resumed run comes back at the level it had (M3-01b), and no event describes a frame that
        // has not happened yet — HudPresenter's own argument for handling RunStarted at all.
        Assert.That(LevelText(), Is.EqualTo("7"));
    }

    [Test]
    public void Level_IsPlacedInTheHudRow()
    {
        StartRun(level: 1);
        BuildHud();

        float pxPerDp = PixelsPerDp();

        float marginX = Field<Vector2>(_presenter, "_marginDp").x;
        float marginY = Field<Vector2>(_presenter, "_marginDp").y;
        Vector2 barDp = Field<Vector2>(_presenter, "_barSizeDp");
        float textDp = Field<float>(_presenter, "_textWidthDp");
        float ringDp = Field<float>(_presenter, "_ringSizeDp");
        float gapDp = Field<float>(_presenter, "_gapDp");
        float levelDp = Const<float>(typeof(HudPresenter), "LevelWidthDp");

        // **The whole row, not only the rect this task added.** Place() has laid this out untested
        // since M1-17, and one method owning all four is the reason the spec gives for the level not
        // being a component of its own — so the row that finally checks it checks the invariant that
        // reason is about: nothing overlaps, and everything is in order.
        (float Left, float Right)[] spans =
        {
            Span(Rect(_presenter, "_hp")),
            Span(Rect(_presenter, "_hpText")),
            Span(Rect(_presenter, "_shield")),
            Span(Rect(_presenter, "_levelText")),
        };

        for (int i = 1; i < spans.Length; i++)
        {
            Assert.That(
                spans[i].Left,
                Is.GreaterThanOrEqualTo(spans[i - 1].Right - 1e-2f),
                $"rect {i} of the HP row starts before rect {i - 1} ends, so the two overlap.");
        }

        // And the level is exactly one gap past the ring, in dp, which is the arithmetic the method
        // does rather than a position anybody authored.
        float expectedLeft = marginX + barDp.x + gapDp + textDp + gapDp + ringDp + gapDp;

        Assert.That(
            spans[3].Left,
            Is.EqualTo(expectedLeft * pxPerDp).Within(1e-2f),
            "the level is not beside the Aegis ring (GD §16.1's \"top-left, beside HP\").");

        Assert.That(
            spans[3].Right,
            Is.EqualTo((expectedLeft + levelDp) * pxPerDp).Within(1e-2f));

        // Anchored to the parent's top-left with the rest of the row, so SafeAreaFitter insets it on
        // a notched device rather than the screen edge cutting it off.
        var levelRect = Rect(_presenter, "_levelText");

        Assert.That(levelRect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(levelRect.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(levelRect.anchoredPosition.y, Is.EqualTo(-marginY * pxPerDp).Within(1e-2f));
        Assert.That(levelRect.sizeDelta.y, Is.EqualTo(barDp.y * pxPerDp).Within(1e-2f));
    }

    // ---- Overflow (rules 10, 11, 12, 13) ---------------------------------------------------------

    [Test]
    public void Overflow_ToastShowsTheTotal()
    {
        StartRun(level: 1);
        BuildHud();

        Assert.That(_toast.IsShown, Is.False, "the prefab ships the toast up.");

        _hub.Publish(new OverflowGranted(30, 14));

        Assert.That(_toast.IsShown, Is.True);

        // **The running total, not the increment** (rule 10). The fourteenth Overflow at stage 30
        // means "you have +28 % damage"; the increment means "you got 2 % again", which is the
        // difference between the mechanic feeling cumulative and feeling like a consolation prize.
        Assert.That(ToastText(), Does.Contain("14"));
        Assert.That(ToastText(), Does.Not.Contain("30"), "the toast is showing the level, not the total.");

        // **`Toast_DrawsEnglish`** — M3-10b's key row inverted, and ledger row 9's most harmless
        // reader closing (rule 13): "Overflow ×14" was always mostly the number, and the number
        // resolved fine all along. What a player gains here is the word rather than the meaning.
        Assert.That(ToastText(), Does.StartWith(ToastWord));
        Assert.That(ToastText(), Does.Not.StartWith("ui."));
        Assert.That(ToastText(), Is.EqualTo(ToastWord + " ×14"));
    }

    [Test]
    public void Overflow_ToastHidesOnItsOwn()
    {
        StartRun(level: 1);
        BuildHud();

        _hub.Publish(new OverflowGranted(4, 1));

        SetDwell(2f);

        Tick(1f);

        Assert.That(_toast.IsShown, Is.True, "half the dwell is not the whole of it.");

        Tick(1.01f);

        Assert.That(_toast.IsShown, Is.False, "the toast never came down.");
    }

    [Test]
    public void Overflow_ToastSurvivesAPause()
    {
        StartRun(level: 1);
        BuildHud();

        _hub.Publish(new OverflowGranted(4, 1));

        SetDwell(2f);

        // A level-up or a pause can land on top of a toast that is already up — Overflow itself never
        // pauses (M3-08a rule 7) but the level after it may draw cards (rule 12).
        Time.timeScale = 0f;

        Tick(1f);

        Assert.That(_toast.IsShown, Is.True, "the fixture's premise: half a dwell.");

        Tick(4f);

        Assert.That(
            _toast.IsShown,
            Is.False,
            "the toast was still up after five seconds of a stopped game, so its dwell froze with "
                + "the simulation and would resume mid-sentence.");

        Time.timeScale = 1f;

        Assert.That(_toast.IsShown, Is.False, "the toast came back when the game did.");

        // **What the row above cannot show, and this does.** Driving Tick by hand proves the seam
        // rather than the clock, because an EditMode test cannot make unscaled time pass — so the
        // claim that the clock is *unscaled* is made against the IL of Update, which is the only
        // place the delta is chosen.
        Assert.That(
            Reads(typeof(OverflowToast), "get_unscaledDeltaTime"),
            Is.True,
            "OverflowToast never reads Time.unscaledDeltaTime, so its dwell is not the unscaled one "
                + "rule 12 asks for.");

        Assert.That(
            Reads(typeof(OverflowToast), "get_deltaTime"),
            Is.False,
            "OverflowToast reads Time.deltaTime, which is 0 while a level-up or a pause is up — so "
                + "the toast would hang on screen for the length of the screen above it.");
    }

    [Test]
    public void Overflow_ToastTakesNoTouches()
    {
        StartRun(level: 1);
        BuildHud();

        _hub.Publish(new OverflowGranted(4, 1));

        CanvasGroup root = Field<CanvasGroup>(_toast, "_root");

        // Rule 12: the stick and the Charge button are under this canvas and the fight is running the
        // whole time the toast is up, so it must never be in the raycast path.
        Assert.That(root.alpha, Is.EqualTo(1f), "the fixture's premise.");
        Assert.That(root.blocksRaycasts, Is.False);
        Assert.That(root.interactable, Is.False);

        Tick(3f);

        Assert.That(root.alpha, Is.EqualTo(0f));
        Assert.That(root.blocksRaycasts, Is.False, "a hidden toast is still in the raycast path.");
    }

    [Test]
    public void Overflow_NothingPauses()
    {
        // Every node taken and a pick still owed. The level accounts for the five nodes and the pick
        // — M3-08a rule 9's identity refuses a saved run whose level cannot explain what it owns.
        StartRun(level: 7, pending: 1, taken: new[] { NodeOne, NodeTwo, NodeThree, NodeFour, NodeFive });
        BuildHud();

        RunPause pause = NewPause();

        Time.timeScale = 1f;

        _session.OpenLevelUp();

        Assert.That(_spy.Count<OverflowGranted>(), Is.EqualTo(1), "the pick was not spent on Overflow.");
        Assert.That(_spy.Count<OfferPresented>(), Is.Zero, "a full tree drew cards.");

        // **M3-08a rule 7 from this side.** Overflow is silent and instant — no pause, no screen —
        // and this toast is the whole of the announcement, so it has to be able to appear over a
        // running fight. The pause here is a real one this component was never handed, which is
        // LevelUpPresenterTests' inverted-row technique: the claim is enforced rather than recorded.
        Assert.That(_toast.IsShown, Is.True, "the one announcement Overflow gets did not appear.");
        Assert.That(pause.IsPaused, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f), "the game stopped for a level with no choice in it.");

        // Total 1: Level − 1 − TakenNodeCount − PendingLevelUps = 7 − 1 − 5 − 0.
        Assert.That(ToastText(), Does.Contain("1"));
        Assert.That(_session.State.OverflowLevels, Is.EqualTo(1));
    }

    [Test]
    public void Overflow_RepeatsRetriggerTheToast()
    {
        StartRun(level: 1);
        BuildHud();

        SetDwell(2f);

        // Three in a row is not hypothetical: past level 13 a twelve-node tree is full and every
        // banked pick is Overflow, so a stage's worth of levels arrives as a run of these.
        foreach (int total in new[] { 1, 2, 3 })
        {
            _hub.Publish(new OverflowGranted(13 + total, total));

            Assert.That(_toast.IsShown, Is.True, $"the toast was not up for Overflow {total}.");
            Assert.That(
                ToastText(),
                Does.Contain(total.ToString()),
                $"the toast is showing a stale total rather than {total}.");

            // Most of a dwell between them, so the third arrives on a toast that is already up and
            // nearly gone — which is what makes "one timer, restarted" the thing under test.
            Tick(1.5f);
        }

        Assert.That(_toast.IsShown, Is.True, "1.5 s after the third is inside its own two seconds.");

        Tick(0.6f);

        // **One timer, not three queued behind each other** (rule 10): the whole run of three is over
        // two seconds after the last one rather than six after the first.
        Assert.That(_toast.IsShown, Is.False);
    }

    [Test]
    public void Overflow_IgnoresANonFiniteDwell()
    {
        StartRun(level: 1);
        BuildHud();

        // **A duration takes FirstActiveHint.Dwell's answer, not the layout doors'** — and that split
        // is the whole of why this row exists beside Strip_IgnoresANonFiniteHeight. A rect has an
        // authored value to leave alone; a duration does not, and the two failures it answers are
        // both worse than the default: a NaN never satisfies `>=`, so the toast would sit over the
        // fight until the run ended, and a zero or negative dwell would take it down on the frame it
        // went up.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -2f })
        {
            _hub.Publish(new OverflowGranted(4, 1));

            SetDwell(bad);

            Tick(1.9f);

            Assert.That(_toast.IsShown, Is.True, $"{bad} hid the toast before the two-second default.");

            Tick(0.2f);

            Assert.That(_toast.IsShown, Is.False, $"{bad} never hid the toast at all.");
        }
    }

    // ---- Lifetime, for all three of this task's components ---------------------------------------

    [Test]
    public void Subscribes_FromConstruct()
    {
        // Constructed *after* RunStarted, which is the order VContainer can produce: RunScope builds
        // its container from its own Awake and Unity orders no two of those. Start is deliberately
        // not invoked, because the claim is that Construct alone is enough.
        StartRun(level: 3, taken: new[] { NodeTwo });

        GameObject hud = Instantiate();

        var strip = hud.GetComponentInChildren<XpBarView>(true);
        var row = hud.GetComponentInChildren<AutoCastRow>(true);
        var toast = hud.GetComponentInChildren<OverflowToast>(true);

        strip.Construct(_session, _hub);
        row.Construct(_session, _hub, _catalog);
        toast.Construct(_hub, new DictionaryLocalizer(ToastKey, ToastWord));

        _hub.Publish(new XpChanged(9f, 3, 0.33f));

        Assert.That(strip.Fraction, Is.EqualTo(0.33f).Within(1e-4f), "the strip heard nothing.");

        // The row's own event, and it needs the frame the deferred rebuild happens on — see
        // AutoCastRow's remarks for why that deferral is mandatory rather than tidy.
        Invoke(row, "Update");

        Assert.That(row.CellCount, Is.EqualTo(1), "the row heard nothing.");

        _hub.Publish(new OverflowGranted(4, 1));

        Assert.That(toast.IsShown, Is.True, "the toast heard nothing.");
    }

    [Test]
    public void Destroy_DropsSubscriptions()
    {
        StartRun(level: 3, taken: new[] { NodeTwo });

        GameObject hud = Instantiate();

        var strip = hud.GetComponentInChildren<XpBarView>(true);
        var row = hud.GetComponentInChildren<AutoCastRow>(true);
        var toast = hud.GetComponentInChildren<OverflowToast>(true);

        strip.Construct(_session, _hub);
        row.Construct(_session, _hub, _catalog);
        toast.Construct(_hub, new DictionaryLocalizer(ToastKey, ToastWord));

        Assert.That(_hub.SubscriberCount<XpChanged>(), Is.EqualTo(1), "the fixture's premise.");
        Assert.That(_hub.SubscriberCount<OverflowGranted>(), Is.EqualTo(1));
        Assert.That(_hub.SubscriberCount<NodeTaken>(), Is.EqualTo(1));
        Assert.That(_hub.SubscriberCount<SkillAutoCastChanged>(), Is.EqualTo(1));

        // Unity does not call OnDestroy on an object whose Awake never ran, and none did here.
        Invoke(strip, "OnDestroy");
        Invoke(row, "OnDestroy");
        Invoke(toast, "OnDestroy");

        Assert.That(_hub.SubscriberCount<XpChanged>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<OverflowGranted>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<NodeTaken>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<SkillAutoCastChanged>(), Is.Zero);

        // A component destroyed before its scope would otherwise be handed an event for an object
        // Unity has killed, which is the failure the explicit disposal exists for.
        Assert.That(
            () =>
            {
                _hub.Publish(new XpChanged(1f, 3, 0.5f));
                _hub.Publish(new OverflowGranted(4, 1));
                _hub.Publish(new NodeTaken(new ContentId(NodeTwo), SkillKind.Active, 0, 1, 1));
                _hub.Publish(new SkillAutoCastChanged(new ContentId(NodeTwo), false, 0));
            },
            Throws.Nothing);
    }

    [Test]
    public void Construct_RefusesNull()
    {
        StartRun(level: 1);
        BuildHud();

        Assert.Throws<ArgumentNullException>(() => _strip.Construct(null, _hub));
        Assert.Throws<ArgumentNullException>(() => _strip.Construct(_session, null));
        Assert.Throws<ArgumentNullException>(() => _toast.Construct(null, Passthrough()));
        Assert.Throws<ArgumentNullException>(() => _toast.Construct(_hub, null));
    }

    // ---- The asset (Traps §5) --------------------------------------------------------------------

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        // Read off the asset rather than off an instance: the failure Traps §5 describes is a
        // component that deserialises as null with nothing reported anywhere, and it is only visible
        // from here.
        var strip = prefab.GetComponentInChildren<XpBarView>(true);
        var row = prefab.GetComponentInChildren<AutoCastRow>(true);
        var toast = prefab.GetComponentInChildren<OverflowToast>(true);
        var presenter = prefab.GetComponent<HudPresenter>();

        Assert.That(strip, Is.Not.Null, "XpBarView did not load off Hud.prefab (Traps §5).");
        Assert.That(row, Is.Not.Null, "AutoCastRow did not load off Hud.prefab (Traps §5).");
        Assert.That(toast, Is.Not.Null, "OverflowToast did not load off Hud.prefab (Traps §5).");
        Assert.That(presenter, Is.Not.Null, "HudPresenter did not load off Hud.prefab (Traps §5).");

        // **Read back off the saved asset**, not off an object this fixture just dressed: a prefab
        // whose references never bound looks identical in memory to one that did, right up until it
        // is loaded in a build.
        var stripSo = new SerializedObject(strip);
        var stripFill = stripSo.FindProperty("_fill").objectReferenceValue as Image;

        Assert.That(stripFill, Is.Not.Null, "the strip has no fill.");
        Assert.That(stripFill.type, Is.EqualTo(Image.Type.Filled));
        Assert.That(
            stripFill.fillMethod,
            Is.EqualTo(Image.FillMethod.Horizontal),
            "the strip's fill is not Horizontal, so fillAmount draws a ring rather than a bar.");

        Assert.That(stripSo.FindProperty("_heightDp").floatValue, Is.EqualTo(StripDp));

        // Twelve row cells present and wired — 24 serialized Images, because rule 7 authors them
        // rather than cloning a template.
        var rowSo = new SerializedObject(row);
        SerializedProperty fills = rowSo.FindProperty("_fills");
        SerializedProperty strips = rowSo.FindProperty("_kindStrips");

        Assert.That(fills.arraySize, Is.EqualTo(SkillRunner.MaxActives));
        Assert.That(strips.arraySize, Is.EqualTo(SkillRunner.MaxActives));

        for (int cell = 0; cell < SkillRunner.MaxActives; cell++)
        {
            var fill = fills.GetArrayElementAtIndex(cell).objectReferenceValue as Image;
            var body = strips.GetArrayElementAtIndex(cell).objectReferenceValue as Image;

            Assert.That(fill, Is.Not.Null, $"cell {cell + 1} has no fill.");
            Assert.That(body, Is.Not.Null, $"cell {cell + 1} has no body.");

            Assert.That(fill.type, Is.EqualTo(Image.Type.Filled), $"cell {cell + 1} fill is not Filled.");
            Assert.That(
                fill.fillMethod,
                Is.EqualTo(Image.FillMethod.Radial360),
                $"cell {cell + 1} fill is not Radial360, so fillAmount draws a bar rather than a ring.");

            Assert.That(
                body.gameObject.activeSelf,
                Is.False,
                $"cell {cell + 1} ships active, so a run owning nothing shows twelve dots.");
        }

        Assert.That(rowSo.FindProperty("_cellSizeDp").floatValue, Is.EqualTo(AutoCastRow.CellSizeDp));
        Assert.That(rowSo.FindProperty("_gapDp").floatValue, Is.EqualTo(AutoCastRow.GapDp));

        var toastSo = new SerializedObject(toast);
        var toastRoot = toastSo.FindProperty("_root").objectReferenceValue as CanvasGroup;

        Assert.That(toastRoot, Is.Not.Null, "the toast has no CanvasGroup root.");
        Assert.That(toastSo.FindProperty("_text").objectReferenceValue, Is.Not.Null, "no toast label.");
        Assert.That(toastRoot.alpha, Is.EqualTo(0f), "the prefab ships the toast up over the arena.");
        Assert.That(toastRoot.blocksRaycasts, Is.False, "the authored toast is in the raycast path.");

        var hudSo = new SerializedObject(presenter);
        var levelLabel = hudSo.FindProperty("_levelText").objectReferenceValue as TMP_Text;

        Assert.That(levelLabel, Is.Not.Null, "HudPresenter has no level label.");
        Assert.That(levelLabel.font, Is.Not.Null, "the level label has no font asset (Traps §4).");

        // All three hang under the safe area, like everything else on this prefab. `includeInactive`
        // is mandatory rather than defensive: every object on a prefab *asset* reports
        // `activeInHierarchy` false, because it is in no scene — so the default overload answers null
        // for a correctly dressed prefab, which cost M3-10a a red row.
        foreach (Component component in new Component[] { strip, row, toast })
        {
            Assert.That(
                component.GetComponentInParent<Soulvail.Game.Controls.SafeAreaFitter>(includeInactive: true),
                Is.Not.Null,
                $"{component.GetType().Name} is not under a SafeAreaFitter, so a notch can cover it.");
        }

        // **The Charge and the four slot buttons are unchanged**, which is the half of this row that
        // is about the prefab having been saved rather than about the new objects on it. This is the
        // second consecutive task to touch the one asset every screen in the game sorts against.
        var charge = prefab.GetComponentInChildren<Soulvail.Game.Controls.SkillButton>(true);
        var bar = prefab.GetComponentInChildren<SkillBarPresenter>(true);

        Assert.That(charge, Is.Not.Null, "the Charge did not load off Hud.prefab.");
        Assert.That(bar, Is.Not.Null, "SkillBarPresenter did not load off Hud.prefab.");

        Assert.That(new SerializedObject(charge).FindProperty("_sizeDp").floatValue, Is.EqualTo(72f));
        Assert.That(
            new SerializedObject(bar).FindProperty("_buttons").arraySize,
            Is.EqualTo(SkillRunner.MaxManualSlots));
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>
    /// Builds a resumed run at <paramref name="level"/>, without starting it.
    /// </summary>
    /// <remarks>
    /// Resumed rather than fresh for <c>LevelUpPresenterTests</c>' reason: a fresh run is level 1
    /// with nothing owed, and the only way to a level or a pick from there is killing enough of a
    /// stage to cross a threshold — which would make every row here a stage test.
    /// <paramref name="level"/> has to account for what is taken and what is owed (M3-08a rule 9).
    /// </remarks>
    private void CreateRun(int level, float xp = 0f, int pending = 0, string[] taken = null)
    {
        // LevelUpPresenterTests' five-node tree: branch a's whole first tier plus one node each in b
        // and c, and no Keystone or Upgrade, because TreeRules refuses a Keystone that shares a tier.
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

        // The hub is the session's own IDomainEvents, so the screens hear what core published rather
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

        _config = new RunConfig(
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
                new RandomState(101, 102, 103, 104, 105),
                90f,
                6f,
                120f,
                Instant,
                level,
                xp,
                pending,
                takenIds,
                new ContentId[SkillRunner.MaxManualSlots]));
    }

    /// <summary><see cref="CreateRun"/>, started.</summary>
    private void StartRun(int level, float xp = 0f, int pending = 0, string[] taken = null)
    {
        CreateRun(level, xp, pending, taken);

        _session.Start(_config);
    }

    /// <summary>Instantiates the shipped HUD, with its canvas scale pinned.</summary>
    private GameObject Instantiate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        GameObject hud = Object.Instantiate(prefab);

        _spawned.Add(hud);

        // Pinned, because a Canvas instantiated in EditMode has never been driven by its own
        // CanvasScaler and the layout rows measure in dp against this — SkillBarPresenterTests sets
        // it for the same reason.
        hud.GetComponent<Canvas>().scaleFactor = 1f;

        return hud;
    }

    /// <summary>
    /// The shipped HUD, with the strip, the toast and the presenter injected and started — the
    /// screen under test is the asset.
    /// </summary>
    private void BuildHud()
    {
        _hud = Instantiate();

        _strip = _hud.GetComponentInChildren<XpBarView>(true);
        _toast = _hud.GetComponentInChildren<OverflowToast>(true);
        _presenter = _hud.GetComponent<HudPresenter>();

        Assert.That(_strip, Is.Not.Null, "XpBarView did not load off Hud.prefab (Traps §5).");
        Assert.That(_toast, Is.Not.Null, "OverflowToast did not load off Hud.prefab (Traps §5).");
        Assert.That(_presenter, Is.Not.Null, "HudPresenter did not load off Hud.prefab (Traps §5).");

        // What RunScope's RegisterComponent does, and then the Start that Unity does not run on an
        // instantiated prefab in EditMode: the placement and the first draw both live there.
        _strip.Construct(_session, _hub);
        _toast.Construct(_hub, new DictionaryLocalizer(ToastKey, ToastWord));
        _presenter.Construct(_hub, _session, new SceneLoader(), Track(new InputAdapter()), Passthrough());

        Invoke(_strip, "Start");
        Invoke(_toast, "Start");
        Invoke(_presenter, "Start");
    }

    /// <summary>
    /// <paramref name="unscaledDt"/> of the toast's own clock.
    /// </summary>
    /// <remarks>
    /// <c>Tick</c> rather than <c>Update</c>, because <c>Time.unscaledDeltaTime</c> does not advance
    /// in EditMode and there is no player loop to give it frame time — <c>FirstActiveHintTests.Tick</c>'s
    /// bargain, and the reason <c>OverflowToast</c> keeps the split at all.
    /// </remarks>
    private void Tick(float unscaledDt) =>
        typeof(OverflowToast)
            .GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_toast, new object[] { unscaledDt });

    private void SetDwell(float seconds) => SetPrivate(_toast, "_secondsShown", seconds);

    private Image Fill() => Field<Image>(_strip, "_fill");

    private string ToastText() => Field<TMP_Text>(_toast, "_text").text;

    private string LevelText() => Field<TMP_Text>(_presenter, "_levelText").text;

    private RunPause NewPause()
    {
        var pause = new RunPause();

        _pauses.Add(pause);

        return pause;
    }

    /// <summary>The rect of the component or label in <paramref name="field"/>.</summary>
    private static RectTransform Rect(object target, string field) =>
        (RectTransform)Field<Component>(target, field).transform;

    /// <summary>One rect's horizontal extent, in canvas units from the parent's left edge.</summary>
    /// <remarks>
    /// Every rect in the HP row is anchored top-left with a left pivot, which is what makes
    /// <c>anchoredPosition.x</c> the left edge outright — so this is arithmetic rather than a
    /// transform walk, and it fails loudly if the row is ever re-anchored.
    /// </remarks>
    private static (float Left, float Right) Span(RectTransform rect) =>
        (rect.anchoredPosition.x, rect.anchoredPosition.x + rect.sizeDelta.x);

    /// <summary>
    /// How many canvas units one dp is worth here — <c>HudPresenter.PixelsPerDp</c>, with the scale
    /// <see cref="Instantiate"/> pinned.
    /// </summary>
    private static float PixelsPerDp() =>
        Soulvail.Game.Controls.StickShaper.PixelsPerDp(Screen.dpi);

    /// <summary>
    /// Whether <paramref name="type"/>, or anything the compiler nested inside it, calls a method
    /// named <paramref name="member"/> on <see cref="Time"/>.
    /// </summary>
    /// <remarks>
    /// <b>The IL, because the question is which clock was <em>read</em>.</b>
    /// <c>SkillBarPresenterTests.Calls</c>' technique, narrowed to one declaring type: a behavioural
    /// row can show that a dwell elapsed, never which clock it elapsed against, and in EditMode
    /// neither clock advances at all. A token that resolves to nothing is skipped rather than
    /// believed — a four-byte window after an opcode byte that happened to fall inside an operand is
    /// not a call, and the resolve is what says so.
    /// </remarks>
    private static bool Reads(Type type, string member)
    {
        const BindingFlags Everything =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        var types = new List<Type> { type };

        types.AddRange(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));

        foreach (Type candidate in types)
        {
            foreach (MethodBase method in candidate.GetMethods(Everything).Cast<MethodBase>()
                         .Concat(candidate.GetConstructors(Everything)))
            {
                byte[] il = method.GetMethodBody()?.GetILAsByteArray();

                if (il is null)
                {
                    continue;
                }

                for (int i = 0; i + 4 < il.Length; i++)
                {
                    // call (0x28) and callvirt (0x6F). Nothing else reaches a property getter.
                    if (il[i] != 0x28 && il[i] != 0x6F)
                    {
                        continue;
                    }

                    MethodBase called;

                    try
                    {
                        called = candidate.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (called is not null && called.Name == member && called.DeclaringType == typeof(Time))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private T Track<T>(T disposable)
        where T : IDisposable
    {
        _disposables.Add(disposable);

        return disposable;
    }

    private static void Invoke(object target, string method) =>
        target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);

    private static T Field<T>(object target, string name) =>
        (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);

    private static void SetPrivate(object target, string name, object value) =>
        target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);

    /// <summary>A private compile-time constant, which is what a width nobody can pass a value through is.</summary>
    private static T Const<T>(Type type, string name) =>
        (T)type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    /// <summary>
    /// One node of the requested kind, built to what <c>SkillSpec</c> actually accepts —
    /// <c>LevelUpPresenterTests.Node</c>.
    /// </summary>
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

            // CH §5.2's shipped levelling curve, ToReach(N) = 20 + 12·N^1.4.
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
    /// Publishes into the run's hub <em>and</em> into a recorder, so a row can both watch a screen
    /// react and count what core said — <c>LevelUpPresenterTests.ForkedEvents</c>.
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

    /// <summary>The real adapter over an empty table: every key resolves to itself.</summary>
    private static ILocalizer Passthrough() =>
        new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>());

}
