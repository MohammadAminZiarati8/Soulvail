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
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// The progression row of the HUD: the XP strip along the top edge and the level number beside HP.
/// </summary>
/// <remarks>
/// <para>
/// The strip and level readout share the shipped HUD prefab. Lifetime rows guard the strip
/// subscriptions; the row's membership, fills and tints are covered by <c>AutoCastRowTests</c>.
/// </para>
/// <para>
/// <b>Over a real <c>RunSession</c> and the shipped <c>Hud.prefab</c></b> — <c>SkillBarPresenterTests</c>'
/// shape and its reasons. The hub <em>is</em> the session's <c>IDomainEvents</c>, so a resumed run's
/// <c>RunStarted</c> is published by core rather than staged here. A hand-built HUD would test a
/// second screen that happens to resemble the asset, and
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
/// tested that class — <c>Place</c> has laid the HP row out since M1-17 with no row watching it. Since
/// UI-01b the level sits in the star, which <see cref="Level_SitsInTheStar"/> checks, and the whole
/// band's arrangement is <c>HudLayoutTests</c>'.
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

    /// <summary>Branch a's whole first tier — <c>LevelUpPresenterTests</c>' five-node tree.</summary>
    private const string NodeOne = "skill.test.one";
    private const string NodeTwo = "skill.test.two";
    private const string NodeThree = "skill.test.three";

    /// <summary>Branch b and branch c, one node each.</summary>
    private const string NodeFour = "skill.test.four";
    private const string NodeFive = "skill.test.five";

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private DomainEventHub _hub;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private RunSession _session;
    private RunConfig _config;

    private GameObject _hud;
    private XpBarView _strip;
    private HudPresenter _presenter;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    /// <summary>The test mode's level cap; 0 unless a MAX row sets it (LV-02a).</summary>
    private int _maxLevel;

    [SetUp]
    public void CreateWorld()
    {
        _maxLevel = 0;

        _hub = new DomainEventHub();
        _random = new FixedRandom(7, Alternating(8_192));
        _clock = new FixedClock(Instant);
    }

    [TearDown]
    public void DestroyWorld()
    {
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

        // UI-01b rule 1: a sliced fill does not draw fillAmount, so its width is its right-hand anchor.
        Assert.That(((RectTransform)Fill().transform).anchorMax.x, Is.EqualTo(0.42f).Within(1e-4f));
        Assert.That(Fill().enabled, Is.True);
        Assert.That(Fill().color, Is.EqualTo(Palette.Player), "the strip's fill is not the player's cyan.");
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
    public void Xp_StripIsEightDpAndFullWidth()
    {
        StartRun(level: 1);
        BuildHud();

        var rect = (RectTransform)_strip.transform;
        float pxPerDp = PixelsPerDp();

        // Eight **dp**, converted at the screen boundary: a Scale-With-Screen-Size canvas measures in
        // reference pixels, and eight of those is a different physical height on every phone (rule
        // 1). It was four until UI-01b, and the four is now the fill inside the art's track.
        Assert.That(StripDp, Is.EqualTo(8f), "the band's strip is 8 dp.");
        Assert.That(rect.sizeDelta.y, Is.EqualTo(StripDp * pxPerDp).Within(1e-2f));

        var well = Field<RectTransform>(_strip, "_well");

        Assert.That(well, Is.Not.Null, "the strip has no well.");
        Assert.That(well.rect.height / pxPerDp, Is.EqualTo(StripDp * (1f - (2f * 0.24f))).Within(0.05f),
            "GD §16.1's fill is about 4 dp, inside the 8 dp track.");

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
    public void Level_SitsInTheStar()
    {
        StartRun(level: 1);
        BuildHud();

        float pxPerDp = PixelsPerDp();

        Vector2 margin = Field<Vector2>(_presenter, "_marginDp");
        float starDp = Field<float>(_presenter, "_starSizeDp");

        RectTransform star = Rect(_presenter, "_levelStar");
        RectTransform level = Rect(_presenter, "_levelText");

        // UI-01b rule 2: the star is 44 dp, the margin in and the row's top down, and the level is the
        // star's own rect — dropped a little, because a star's body is below the middle of its box.
        Assert.That(star.sizeDelta, Is.EqualTo(new Vector2(starDp, starDp) * pxPerDp));
        Assert.That(star.anchoredPosition.x, Is.EqualTo(margin.x * pxPerDp).Within(1e-2f));
        Assert.That(star.anchoredPosition.y, Is.EqualTo(-margin.y * pxPerDp).Within(1e-2f));

        Assert.That(level.sizeDelta, Is.EqualTo(star.sizeDelta), "the level is not the star's size.");
        Assert.That(level.anchoredPosition.x, Is.EqualTo(star.anchoredPosition.x).Within(1e-2f));
        Assert.That(
            star.anchoredPosition.y - level.anchoredPosition.y,
            Is.InRange(0f, 0.1f * starDp * pxPerDp),
            "the level is not in the star.");

        // Anchored to the parent's top-left with the rest of the row, so SafeAreaFitter insets it on
        // a notched device rather than the screen edge cutting it off.
        Assert.That(level.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(level.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));

        // Drawn over the star: a later sibling.
        Assert.That(level.GetSiblingIndex(), Is.GreaterThan(star.GetSiblingIndex()));
        Assert.That(Field<TMP_Text>(_presenter, "_levelText").color, Is.EqualTo(Palette.HudText));
    }

    [Test]
    public void Hud_HoldsNoOverflowToast()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");
        Assert.That(
            prefab.GetComponentsInChildren<Transform>(true).Any(child => child.name == "OverflowToast"),
            Is.False);
    }

    // ---- MAX at the level cap (LV-02a rule 7) ------------------------------------------------------

    [Test]
    public void Max_ShowsAtTheCap()
    {
        _maxLevel = 4;
        StartRun(level: 4);
        BuildHud();

        TMP_Text label = MaxLabel();

        Assert.That(_strip.ShowsMax, Is.True);
        Assert.That(label.gameObject.activeSelf, Is.True);
        Assert.That(label.text, Is.EqualTo("ui.xp.max"), "the key, resolved through the localizer.");
        Assert.That(_strip.Fraction, Is.EqualTo(1f), "a full strip at the cap.");
    }

    [Test]
    public void Max_HiddenBelowTheCap()
    {
        _maxLevel = 4;
        StartRun(level: 3);
        BuildHud();

        Assert.That(_strip.ShowsMax, Is.False);
        Assert.That(MaxLabel().gameObject.activeSelf, Is.False);

        _hub.Publish(new XpChanged(9f, 3, 0.4f));

        Assert.That(MaxLabel().gameObject.activeSelf, Is.False, "below the cap XpChanged keeps it hidden.");
    }

    private TMP_Text MaxLabel()
    {
        var label = new SerializedObject(_strip).FindProperty("_maxLabel").objectReferenceValue as TMP_Text;

        Assert.That(label, Is.Not.Null, "the strip's MAX label is not wired on Hud.prefab.");

        return label;
    }

    // ---- Lifetime, for the XP strip ---------------------------------------

    [Test]
    public void Subscribes_FromConstruct()
    {
        // Constructed *after* RunStarted, which is the order VContainer can produce: RunScope builds
        // its container from its own Awake and Unity orders no two of those. Start is deliberately
        // not invoked, because the claim is that Construct alone is enough.
        StartRun(level: 3, taken: new[] { NodeTwo });

        GameObject hud = Instantiate();

        var strip = hud.GetComponentInChildren<XpBarView>(true);

        strip.Construct(_session, _hub, Passthrough());

        _hub.Publish(new XpChanged(9f, 3, 0.33f));

        Assert.That(strip.Fraction, Is.EqualTo(0.33f).Within(1e-4f), "the strip heard nothing.");
    }

    [Test]
    public void Destroy_DropsSubscriptions()
    {
        StartRun(level: 3, taken: new[] { NodeTwo });

        GameObject hud = Instantiate();

        var strip = hud.GetComponentInChildren<XpBarView>(true);

        strip.Construct(_session, _hub, Passthrough());

        Assert.That(_hub.SubscriberCount<XpChanged>(), Is.EqualTo(1), "the fixture's premise.");

        // Unity does not call OnDestroy on an object whose Awake never ran, and none did here.
        Invoke(strip, "OnDestroy");

        Assert.That(_hub.SubscriberCount<XpChanged>(), Is.Zero);

        // A component destroyed before its scope would otherwise be handed an event for an object
        // Unity has killed, which is the failure the explicit disposal exists for.
        Assert.That(
            () =>
            {
                _hub.Publish(new XpChanged(1f, 3, 0.5f));
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

        Assert.Throws<ArgumentNullException>(() => _strip.Construct(null, _hub, Passthrough()));
        Assert.Throws<ArgumentNullException>(() => _strip.Construct(_session, null, Passthrough()));
        Assert.Throws<ArgumentNullException>(() => _strip.Construct(_session, _hub, null));
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
        var presenter = prefab.GetComponent<HudPresenter>();

        Assert.That(strip, Is.Not.Null, "XpBarView did not load off Hud.prefab (Traps §5).");
        Assert.That(presenter, Is.Not.Null, "HudPresenter did not load off Hud.prefab (Traps §5).");

        // **Read back off the saved asset**, not off an object this fixture just dressed: a prefab
        // whose references never bound looks identical in memory to one that did, right up until it
        // is loaded in a build.
        var stripSo = new SerializedObject(strip);
        var stripFill = stripSo.FindProperty("_fill").objectReferenceValue as Image;

        Assert.That(stripFill, Is.Not.Null, "the strip has no fill.");

        // UI-01b rule 1: a sliced pill in the track's well, whose width is its anchor.
        Assert.That(stripFill.type, Is.EqualTo(Image.Type.Sliced));
        Assert.That(stripFill.sprite, Is.Not.Null);
        Assert.That(stripFill.sprite.name, Is.EqualTo("Fill_BarThin"));

        var track = stripSo.FindProperty("_track").objectReferenceValue as Image;

        Assert.That(track, Is.Not.Null, "the strip has no track.");
        Assert.That(track.sprite.name, Is.EqualTo("XpStrip_Track"));
        Assert.That(track.type, Is.EqualTo(Image.Type.Sliced));
        Assert.That(stripFill.transform.parent, Is.SameAs(stripSo.FindProperty("_well").objectReferenceValue),
            "the fill is not in the well.");

        Assert.That(stripSo.FindProperty("_heightDp").floatValue, Is.EqualTo(StripDp));

        var hudSo = new SerializedObject(presenter);
        var levelLabel = hudSo.FindProperty("_levelText").objectReferenceValue as TMP_Text;

        Assert.That(levelLabel, Is.Not.Null, "HudPresenter has no level label.");
        Assert.That(levelLabel.font, Is.Not.Null, "the level label has no font asset (Traps §4).");

        // The strip hangs under the safe area, like everything else on this prefab. `includeInactive`
        // is mandatory rather than defensive: every object on a prefab *asset* reports
        // `activeInHierarchy` false, because it is in no scene — so the default overload answers null
        // for a correctly dressed prefab, which cost M3-10a a red row.
        foreach (Component component in new Component[] { strip })
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

        Assert.That(charge, Is.Not.Null, "the Charge did not load off Hud.prefab.");

        Assert.That(new SerializedObject(charge).FindProperty("_sizeDp").floatValue, Is.EqualTo(72f));
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
            new[] { Mode(_maxLevel) },
            skills,
            new[] { Tree() });

        _session = new RunSession(
            _catalog,
            _random,
            _hub,
            new RecordingIntents(),
            new RunRecorder(_random, _clock, _hub),
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
                new ContentId[SkillRunner.MaxManualSlots],
                default,
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>()));
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
    /// The shipped HUD, with the strip and the presenter injected and started — the
    /// screen under test is the asset.
    /// </summary>
    private void BuildHud()
    {
        _hud = Instantiate();

        _strip = _hud.GetComponentInChildren<XpBarView>(true);
        _presenter = _hud.GetComponent<HudPresenter>();

        Assert.That(_strip, Is.Not.Null, "XpBarView did not load off Hud.prefab (Traps §5).");
        Assert.That(_presenter, Is.Not.Null, "HudPresenter did not load off Hud.prefab (Traps §5).");

        // What RunScope's RegisterComponent does, and then the Start that Unity does not run on an
        // instantiated prefab in EditMode: the placement and the first draw both live there.
        _strip.Construct(_session, _hub, Passthrough());
        _presenter.Construct(_hub, _session, Passthrough());

        Invoke(_strip, "Start");
        Invoke(_presenter, "Start");
    }

    private Image Fill() => Field<Image>(_strip, "_fill");

    private string LevelText() => Field<TMP_Text>(_presenter, "_levelText").text;

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

    private static ModeSpec Mode(int maxLevel = 0)
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
            new XpCurve(20f, 12f, 1.4f, maxLevel),
            new[] { new RosterEntry(new ContentId(HuskId), 1) },
            new[] { new ArenaEntry(new ContentId(ArenaId)) });
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

    /// <summary>The real adapter over an empty table: every key resolves to itself.</summary>
    private static ILocalizer Passthrough() =>
        new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>());

}
