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
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CoreVector3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// GD §16.1's auto-cast cooldowns: which Actives are in the row, what each cell is showing, and what
/// leaves it when a skill becomes the player's to cast.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over a real <c>RunSession</c>, a real <c>SkillRunner</c> and the shipped <c>Hud.prefab</c></b> —
/// <c>SkillBarPresenterTests</c>' shape and its reasons. Every row here is about what core actually
/// holds and whether it fires itself, which a fake session would agree with whatever the row did.
/// </para>
/// <para>
/// <b>This is the class that is <em>allowed</em> to read <c>RunState.IsAutoCast</c>, and the fixture
/// says so out loud</b> (M3-10b rule 5). <c>SkillBarPresenterTests.Bar_KnowsNothingAboutAuto</c> walks
/// the IL of <c>SkillBarPresenter</c> and <c>ManualSkillButton</c> and asserts neither calls it; the
/// two readouts are disjoint by construction, and <see cref="Row_AndButtonsAreDisjoint"/> is the same
/// claim from the side that does the asking.
/// </para>
/// <para>
/// <b>Every membership row passes a frame, and that is the deferral rather than the fixture being
/// polite.</b> <c>LevelUpFlow.ChooseOffer</c> publishes <c>NodeTaken</c> from inside
/// <c>SkillTree.Take</c> and hands the spec to <c>SkillRunner.Add</c> afterwards, so a handler reading
/// <c>OwnedActiveCount</c> from inside the event sees the count from <em>before</em> the node —
/// M3-09c's finding. The row therefore marks itself stale and rebuilds in <c>Update</c>, which is
/// where the fills are read anyway; a row that rebuilt in the handler would miss the very cell it had
/// just been told about.
/// </para>
/// <para>
/// <c>Awake</c> and <c>Start</c> do not run on an instantiated prefab in EditMode (Traps §5), so
/// <see cref="Row"/> invokes <c>Start</c> by hand — the placement and the first membership build both
/// live there.
/// </para>
/// </remarks>
[TestFixture]
public sealed class AutoCastRowTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string TreeId = "tree.oathbound";
    private const string ArenaId = "arena.pillars";

    /// <summary>The volley node the fixture's class may take (UI-01e).</summary>
    private const string VolleyNodeId = "skill.test.volley";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    private const float Frame = 1f / 60f;

    /// <summary>The first active's authored cooldown; the <em>i</em>th is this plus <em>i</em>.</summary>
    /// <remarks>
    /// Above CH §4.1's floor of 40 % on its own base, so <c>EffectiveCooldownOf</c> hands this number
    /// straight back and the fill arithmetic below is the one a reader can do in their head.
    /// </remarks>
    private const float FirstCooldown = 2.5f;

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private DomainEventHub _hub;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private RunSession _session;
    private RunConfig _config;

    private string[] _activeIds;

    /// <summary>The icons <see cref="Row"/> hands the row, by skill id — empty unless a row adds one.</summary>
    private readonly Dictionary<ContentId, Sprite> _iconMap = new Dictionary<ContentId, Sprite>();

    /// <summary>The node the fixture's class draws its volley as, or <c>default</c>.</summary>
    private ContentId _volleyNode;

    private GameObject _hud;
    private AutoCastRow _row;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    [SetUp]
    public void CreateWorld()
    {
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
        _row = null;
    }

    // ---- Which cells exist (rules 5, 6, 7) -------------------------------------------------------

    /// <summary>UI-01e rule 2: a cell wears its skill's own icon, from the book built at boot.</summary>
    /// <remarks>
    /// <b>This replaced <c>AutoCastRow_IsStillIconless</c></b>, whose subject was the absence of icons
    /// and which said so: ledger row 9's seventh reader could only close with art. UI-01e is the art.
    /// </remarks>
    [Test]
    public void Row_DrawsEachSkillsIcon()
    {
        StartRun(actives: 2);

        Sprite art = TestIcon();

        _iconMap[new ContentId(_activeIds[0])] = art;

        Row();

        Assert.That(Icons()[0].sprite, Is.SameAs(art), "the cell does not wear its skill's icon.");
    }

    [Test]
    public void Row_ASkillWithNoIconDrawsThePlaceholder()
    {
        StartRun(actives: 2);
        Row();

        var placeholder = Field<Sprite>(_row, "_placeholderIcon");

        Assert.That(placeholder, Is.Not.Null, "the prefab dresses no placeholder.");
        Assert.That(placeholder.name, Is.EqualTo("Icon_Placeholder"));
        Assert.That(Icons()[0].sprite, Is.SameAs(placeholder));
        Assert.That(Icons()[1].sprite, Is.SameAs(placeholder));
    }

    [Test]
    public void Row_ShowsOneCellPerAutoActive()
    {
        // Two Actives, both on Auto, which is what CC §6.1's default gives every player who never
        // opens a menu — and without this row their whole build is invisible (rule 4).
        StartRun(actives: 2);
        Row();

        Assert.That(_row.CellCount, Is.EqualTo(2));
        Assert.That(Shown(), Is.EqualTo(new[] { true, true }.Concat(Off(10)).ToArray()));

        // And the take itself, through core rather than staged here: NodeTaken carries the Kind
        // (M3-03 rule 7), and the frame is the deferred rebuild — see the fixture remarks.
        _hub.Publish(new NodeTaken(new ContentId(_activeIds[0]), SkillKind.Active, 0, 1, 1));
        PassAFrame();

        Assert.That(_row.CellCount, Is.EqualTo(2), "a NodeTaken for a skill already owned added a cell.");
    }

    [Test]
    public void Row_ExcludesAManualSkill()
    {
        StartRun(actives: 2);
        Row();

        Assert.That(_row.CellCount, Is.EqualTo(2), "the fixture's premise: both start on Auto.");

        // The event core publishes when the player flips CC §6.1's switch — it carries the id, the
        // state and the slot (M3-07a rule 9), and the row uses none of them: it rebuilds.
        Commands().SetAutoCast(new ContentId(_activeIds[0]), auto: false);
        PassAFrame();

        Assert.That(_row.CellCount, Is.EqualTo(1), "a Manual skill kept its cell, so its cooldown is "
            + "drawn twice — once here and once on its slot button.");

        // The row is packed from the front, so the skill that stayed slid into cell 0 rather than
        // leaving a hole. That is the opposite of CC §6.2's four fixed thumb positions, and
        // deliberately: a cell is not something a thumb has to find.
        Assert.That(Shown()[0], Is.True);
        Assert.That(Shown()[1], Is.False);
    }

    [Test]
    public void Row_TakesASkillBack()
    {
        StartRun(actives: 2);
        Row();

        Commands().SetAutoCast(new ContentId(_activeIds[0]), auto: false);
        PassAFrame();

        Assert.That(_row.CellCount, Is.EqualTo(1), "the fixture's premise.");

        // Back to Auto, and the event carries −1 because it says where the skill *is* (M3-07a rule 9).
        Commands().SetAutoCast(new ContentId(_activeIds[0]), auto: true);
        PassAFrame();

        Assert.That(_row.CellCount, Is.EqualTo(2), "the row did not take the skill back.");
    }

    [Test]
    public void Row_AndButtonsAreDisjoint()
    {
        // A manual, B auto.
        StartRun(actives: 2, manual: new[] { 0, -1, -1, -1 });
        Row();

        SkillBarPresenter bar = Bar();

        // **The two readouts never show one cooldown twice** (rule 5), which is HudPresenter's rule
        // for the Charge — "a second readout here would be a second answer to when the player may
        // dash" — applied to the skills. The bar draws a button because a slot is occupied and this
        // row draws a cell because a skill is Auto, and no skill can be both.
        Assert.That(_row.CellCount, Is.EqualTo(1), "B has no cell.");
        Assert.That(Buttons(bar).Count(b => b != null && b.IsShown), Is.EqualTo(1), "A has no button.");

        // A is the one with a button: it is in slot 0.
        Assert.That(
            _session.State.ManualSlotAt(0),
            Is.EqualTo(new ContentId(_activeIds[0])),
            "the fixture's premise.");

        // And the cell is B's, which is the half that makes this about disjointness rather than
        // about two counts that happen to add up. The cell holds runner index 1, not 0.
        Assert.That(CellSkill(0), Is.EqualTo(1), "the row drew the Manual skill rather than the Auto one.");

        // Flip them and the two swap, both ways, in one frame.
        Commands().SetAutoCast(new ContentId(_activeIds[0]), auto: true);
        Commands().SetAutoCast(new ContentId(_activeIds[1]), auto: false);
        PassAFrame();

        Assert.That(_row.CellCount, Is.EqualTo(1));
        Assert.That(CellSkill(0), Is.EqualTo(0), "the row did not follow the switch.");
        Assert.That(Buttons(bar).Count(b => b != null && b.IsShown), Is.EqualTo(1));
    }

    [Test]
    public void Row_IgnoresPassives()
    {
        // A tree is mostly passives (CH §4's ~45 %), and a Passive can never fire — so it has no
        // cooldown to draw and NodeTaken's Kind is what says so without a catalog lookup (rule 6).
        StartRun(actives: 0);
        Row();

        Assert.That(_row.CellCount, Is.Zero, "the fixture's premise: a run owning no Actives.");

        _hub.Publish(new NodeTaken(new ContentId("skill.test.p90"), SkillKind.Passive, 0, 1, 1));
        PassAFrame();

        Assert.That(_row.CellCount, Is.Zero, "a Passive was given a cooldown readout.");

        // And the row is genuinely empty rather than drawing twelve dots, which is every run in this
        // build: nothing in Data/Trees ships an Active until M3-12.
        Assert.That(Shown(), Is.EqualTo(Off(SkillRunner.MaxActives)));
    }

    [Test]
    public void Row_HoldsTwelve()
    {
        // SkillRunner.MaxActives, which is the ceiling core refuses a tree past — so twelve is the
        // most this row can ever be asked for (rule 7).
        StartRun(actives: SkillRunner.MaxActives);
        Row();

        Assert.That(_row.CellCount, Is.EqualTo(SkillRunner.MaxActives));
        Assert.That(Shown().Count(shown => shown), Is.EqualTo(SkillRunner.MaxActives));

        // **Nothing instantiated after Start** (rule 7). The cells are authored on Hud.prefab and
        // deactivated, which is the opposite of SkillsPresenter, TreeViewPresenter and
        // LevelUpPresenter — all three clone a template — because twelve is a fixed, knowable number
        // and a row of cooldowns that hitched on the frame a skill was granted would hitch on
        // exactly the frame the player is watching it.
        int cellsBefore = _row.transform.childCount;
        int imagesBefore = _row.GetComponentsInChildren<Image>(includeInactive: true).Length;

        // Twelve Active cells and the volley's, each a glow, a frame and an icon; the Active cells a
        // sweep as well, and the volley's cell five pips (UI-01e).
        Assert.That(cellsBefore, Is.EqualTo(SkillRunner.MaxActives + 1), "the prefab authors twelve cells and the volley's.");
        Assert.That(imagesBefore, Is.EqualTo((SkillRunner.MaxActives * 4) + 3 + AutoCastRow.MaxPips), "four images a cell, and the volley's eight.");

        for (int frame = 0; frame < 5; frame++)
        {
            PassAFrame();
        }

        Assert.That(
            _row.transform.childCount,
            Is.EqualTo(cellsBefore),
            "the row instantiated a cell after Start; the twelve are meant to be authored.");

        Assert.That(
            _row.GetComponentsInChildren<Image>(includeInactive: true).Length,
            Is.EqualTo(imagesBefore),
            "the row instantiated an Image after Start.");

        // Twelve 34 dp cells with 4 dp gaps is 452 dp, which a landscape safe area holds beside the
        // band's right-hand end; a 9:20 portrait holds seven, which HudLayoutTests pins (UI-01e rule 7).
        float pxPerDp = PixelsPerDp();
        var first = (RectTransform)Strips()[0].transform;
        var last = (RectTransform)Strips()[SkillRunner.MaxActives - 1].transform;

        Assert.That(
            (last.anchoredPosition.x + last.sizeDelta.x - first.anchoredPosition.x) / pxPerDp,
            Is.EqualTo(452f).Within(0.01f));
    }

    [Test]
    public void Row_DrawsTheOpeningStateOnRunStarted()
    {
        // The row exists *before* the run does, which is the order VContainer can produce and the
        // only one in which the event path is the thing under test.
        CreateRun(actives: 2);
        Row();

        Assert.That(
            _row.CellCount,
            Is.Zero,
            "a row in a scene where no run has begun drew something.");

        // A resumed run comes back owning whatever TakenNodeIds said (M3-03), and nothing publishes a
        // NodeTaken for a restore — so RunStarted is the only thing that can say so.
        _session.Start(_config);
        PassAFrame();

        Assert.That(_row.CellCount, Is.EqualTo(2));
    }

    // ---- What a cell is showing (rules 6, 9) -----------------------------------------------------

    [Test]
    public void Row_SweepIsWhatIsLeftOfTheCooldown()
    {
        // A fires itself on the first tick and B never does, which is what makes "that cell's sweep
        // moves and the other's does not" a claim about the mapping rather than about two sweeps that
        // happen to be at different places.
        StartRun(actives: 2, firesImmediately: new[] { true, false });
        Row();

        Assert.That(_row.CellCount, Is.EqualTo(2), "the fixture's premise.");

        Image a = Fills()[0];
        Image b = Fills()[1];

        Assert.That(a.fillAmount, Is.EqualTo(0f).Within(1e-3f), "a live skill is shaded.");
        Assert.That(b.fillAmount, Is.EqualTo(0f).Within(1e-3f));

        TickRun(Frame);
        PassAFrame();

        // **Full the instant the cast starts, where M3-10b's fill was empty** (UI-01e rule 3). The
        // sweep is a dark wedge over the icon that clears clockwise — the mockup's and the art's
        // cooling button's picture — so its fill is the cooldown left rather than one minus it. The
        // number is core's either way.
        Assert.That(a.fillAmount, Is.EqualTo(1f).Within(1e-3f), "A's cell did not shade on its cast.");
        Assert.That(b.fillAmount, Is.EqualTo(0f).Within(1e-3f), "B's cell moved for A's cast.");

        TickRun(FirstCooldown / 2f);
        PassAFrame();

        Assert.That(a.fillAmount, Is.EqualTo(0.5f).Within(0.05f));
        Assert.That(b.fillAmount, Is.EqualTo(0f).Within(1e-3f));

        // Stopped short of ready on purpose, for the old row's reason: A's trigger holds every tick.
        TickRun(1f);
        PassAFrame();

        Assert.That(a.fillAmount, Is.EqualTo(0.1f).Within(0.05f));
        Assert.That(b.fillAmount, Is.EqualTo(0f).Within(1e-3f));
    }

    [Test]
    public void Row_WritesOnlyChangedSweeps()
    {
        StartRun(actives: 2, firesImmediately: new[] { false, false });
        Row();

        PassAFrame();

        // A sentinel nothing should overwrite: assigning fillAmount queues a canvas rebuild, and a
        // skill on an eight-second cooldown is idle for most of it.
        Fills()[0].fillAmount = 0.42f;
        Fills()[1].fillAmount = 0.37f;

        for (int frame = 0; frame < 60; frame++)
        {
            PassAFrame();
        }

        Assert.That(Fills()[0].fillAmount, Is.EqualTo(0.42f).Within(1e-4f), "the row repainted a frame in which nothing had changed.");
        Assert.That(Fills()[1].fillAmount, Is.EqualTo(0.37f).Within(1e-4f));

        // And it *does* write when something changes, so the row above is about redundancy.
        SetPrivate(_row, "_dirty", true);
        PassAFrame();

        Assert.That(Fills()[0].fillAmount, Is.EqualTo(0f).Within(1e-3f));
    }

    [Test]
    public void Row_GlowsWhenReady()
    {
        StartRun(actives: 2, firesImmediately: new[] { true, false });
        Row();

        PassAFrame();

        // Both live: both glow (UI-01e rule 4).
        Assert.That(Glows()[0].gameObject.activeSelf, Is.True, "a ready skill does not glow.");
        Assert.That(Glows()[1].gameObject.activeSelf, Is.True);

        TickRun(Frame);
        PassAFrame();

        Assert.That(Glows()[0].gameObject.activeSelf, Is.False, "a cooling skill glows.");
        Assert.That(Glows()[1].gameObject.activeSelf, Is.True);
    }

    [Test]
    public void Row_CarriesNoColourOfItsOwn()
    {
        StartRun(actives: 1);
        Row();

        // UI-01e rule 8: the sweep is the art's ink at the row's own alpha — the palette is opaque
        // and a view multiplies its own — and the glow is the player's cyan. The kind tint went with
        // the icons: a cell's identity is its picture now.
        Color shade = Palette.Ink;

        shade.a = Field<float>(_row, "_sweepAlpha");

        Assert.That(Fills()[0].color, Is.EqualTo(shade));
        Assert.That(Glows()[0].color, Is.EqualTo(Palette.Player));
        Assert.That(Palette.IsDanger(Fills()[0].color), Is.False);
        Assert.That(typeof(AutoCastRow).GetMethod("Tint", BindingFlags.Static | BindingFlags.NonPublic), Is.Null,
            "the kind tint is back; a cell's identity is its icon (rule 8).");
    }

    // ---- The volley (UI-01e rules 1, 2, 4, 5) ----------------------------------------------------

    [Test]
    public void Volley_HasNoCellUntilANodeGivesOne()
    {
        StartRun(actives: 0, volleyEvery: 0);
        Row();
        PassAFrame();

        Assert.That(_row.HasVolleyCell, Is.False, "a volley nobody has given drew a cell.");

        StartRun(actives: 0, volleyEvery: 5);
        Row();
        PassAFrame();

        Assert.That(_row.HasVolleyCell, Is.True, "the volley has no cell.");
        Assert.That(Field<RectTransform>(_row, "_volleyCell").gameObject.activeSelf, Is.True);
    }

    [Test]
    public void Volley_ComesFirst()
    {
        StartRun(actives: 1, volleyEvery: 5);
        Row();
        PassAFrame();

        float px = PixelsPerDp();
        Vector2 margin = Field<Vector2>(_row, "_marginDp");
        var volley = Field<RectTransform>(_row, "_volleyCell");
        var active = (RectTransform)Strips()[0].transform;

        Assert.That(volley.anchoredPosition.x, Is.EqualTo(margin.x * px).Within(1e-2f), "the volley is not first.");
        Assert.That(
            active.anchoredPosition.x,
            Is.EqualTo((margin.x + AutoCastRow.CellSizeDp + AutoCastRow.GapDp) * px).Within(1e-2f),
            "the Active did not move over for the volley.");
    }

    [Test]
    public void Volley_DrawsItsNodesIcon()
    {
        StartRun(actives: 0, volleyEvery: 5);

        Sprite art = TestIcon();

        _iconMap[new ContentId(VolleyNodeId)] = art;
        _volleyNode = new ContentId(VolleyNodeId);

        Row();
        PassAFrame();

        Assert.That(Field<Image>(_row, "_volleyIcon").sprite, Is.SameAs(art), "the volley does not wear its node's icon.");
    }

    [Test]
    public void Volley_PipsAreTheShotsTowardTheNext()
    {
        StartRun(actives: 0, volleyEvery: 5);
        Row();

        Loose(3);
        PassAFrame();

        Assert.That(_row.PipsShown, Is.EqualTo(5));
        Assert.That(_row.PipsFull, Is.EqualTo(3));
        Assert.That(Pips()[2].sprite, Is.SameAs(Field<Sprite>(_row, "_pipFull")));
        Assert.That(Pips()[3].sprite, Is.SameAs(Field<Sprite>(_row, "_pipEmpty")));
    }

    [Test]
    public void Volley_PipsFollowARank()
    {
        StartRun(actives: 0, volleyEvery: 4);
        Row();
        PassAFrame();

        Assert.That(_row.PipsShown, Is.EqualTo(4), "Volley II's four shots are not four pips.");
        Assert.That(Pips()[4].gameObject.activeSelf, Is.False);
    }

    [Test]
    public void Volley_PipsScalePastFive()
    {
        StartRun(actives: 0, volleyEvery: 8);
        Row();

        Loose(4);
        PassAFrame();

        Assert.That(_row.PipsShown, Is.EqualTo(5));
        Assert.That(_row.PipsFull, Is.EqualTo(2), "four of eight shots is two and a half of five pips, floored.");
    }

    [Test]
    public void Volley_ReadyFillsThePipsAndGlows()
    {
        StartRun(actives: 0, volleyEvery: 5);
        Row();

        Loose(5);
        PassAFrame();

        Assert.That(_session.State.IsVolleyReady, Is.True, "the fixture's premise: five shots ready the fan.");
        Assert.That(_row.PipsFull, Is.EqualTo(5));
        Assert.That(Field<Image>(_row, "_volleyGlow").gameObject.activeSelf, Is.True, "a ready volley does not glow.");
    }

    // ---- Where the cells sit (rule 7) ------------------------------------------------------------

    [Test]
    public void Row_IgnoresANonFiniteDpField()
    {
        StartRun(actives: SkillRunner.MaxActives);
        Row();

        var rect = (RectTransform)Strips()[0].transform;
        Vector2 placed = rect.anchoredPosition;
        Vector2 sized = rect.sizeDelta;
        Vector2 margin = Field<Vector2>(_row, "_marginDp");

        // **These twelve cells are authored on Hud.prefab, so leaving the layout alone is an answer**
        // (SkillBarPresenter.Place's, not TreeViewPresenter.Layout's) — a tree cell is a runtime
        // clone with nothing to fall back to, and a row cell has the prefab's own position under the
        // health bar. All three doors take the same answer: the placement simply does not happen.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -24f })
        {
            SetPrivate(_row, "_cellSizeDp", bad);

            Assert.That(() => Place(), Throws.Nothing, $"Place threw on a cell size of {bad}.");

            Assert.That(rect.sizeDelta, Is.EqualTo(sized), $"cell size {bad}");
            Assert.That(rect.anchoredPosition, Is.EqualTo(placed), $"cell size {bad}");
        }

        SetPrivate(_row, "_cellSizeDp", AutoCastRow.CellSizeDp);

        // **A gap of 0 is legal and honoured**, which is where this door is narrower than the cell
        // size's: twelve cells touching is a layout the owner is entitled to ask for, where a row of
        // 0 dp cells is this readout silently absent.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -4f })
        {
            SetPrivate(_row, "_gapDp", bad);

            Assert.That(() => Place(), Throws.Nothing, $"Place threw on a gap of {bad}.");
            Assert.That(rect.anchoredPosition, Is.EqualTo(placed), $"gap {bad}");
        }

        SetPrivate(_row, "_gapDp", 0f);
        Place();

        Assert.That(
            ((RectTransform)Strips()[1].transform).anchoredPosition.x - rect.anchoredPosition.x,
            Is.EqualTo(AutoCastRow.CellSizeDp * PixelsPerDp()).Within(1e-2f),
            "a gap of 0 was refused rather than honoured, so touching cells are not a layout the "
                + "owner can ask for.");

        SetPrivate(_row, "_gapDp", AutoCastRow.GapDp);

        // A margin of 0 is legal for the same reason — a row flush to the safe area's corner — so
        // only nonsense and a negative are refused.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -16f })
        {
            SetPrivate(_row, "_marginDp", new Vector2(bad, margin.y));

            Assert.That(() => Place(), Throws.Nothing, $"Place threw on a margin of {bad}.");
        }

        // The shipped margin back, read at the top rather than typed here: UI-01b moved it under the
        // new bar, and a literal of the old one is what this row compared against until then.
        SetPrivate(_row, "_marginDp", margin);
        Place();

        Assert.That(rect.anchoredPosition, Is.EqualTo(placed), "the authored layout did not come back.");
    }

    [Test]
    public void Row_IsPlacedInDp()
    {
        StartRun(actives: SkillRunner.MaxActives);
        Row();

        float pxPerDp = PixelsPerDp();
        Vector2 margin = Field<Vector2>(_row, "_marginDp");

        for (int cell = 0; cell < SkillRunner.MaxActives; cell++)
        {
            var rect = (RectTransform)Strips()[cell].transform;

            // 24 dp square, converted at the screen boundary — a Scale-With-Screen-Size canvas
            // measures in reference pixels, and 24 of those is a different physical size on every
            // phone (HudPresenter.Place's reason, for the twelfth rect on this prefab).
            Assert.That(
                rect.sizeDelta.x,
                Is.EqualTo(AutoCastRow.CellSizeDp * pxPerDp).Within(1e-2f),
                $"cell {cell + 1} is not {AutoCastRow.CellSizeDp} dp across.");

            Assert.That(rect.sizeDelta.y, Is.EqualTo(rect.sizeDelta.x).Within(1e-3f));

            // Anchored top-left with the HP row, so SafeAreaFitter insets it on a notched device.
            Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));

            float expected = margin.x + (cell * (AutoCastRow.CellSizeDp + AutoCastRow.GapDp));

            Assert.That(
                rect.anchoredPosition.x,
                Is.EqualTo(expected * pxPerDp).Within(1e-2f),
                $"cell {cell + 1} is not its own place in the row.");

            Assert.That(rect.anchoredPosition.y, Is.EqualTo(-margin.y * pxPerDp).Within(1e-2f));
        }

        // Under the health bar rather than over it (UI-01b rule 12): the bar is centred on the star's
        // centre line, so its bottom is the star's top plus half the star plus half the bar — 46 dp —
        // and the row ships 8 dp under it, left-aligned with it: 6 at UI-01b, and 8 since UI-01d put
        // the stage chip's bottom edge at 53.
        HudPresenter hud = _hud.GetComponent<HudPresenter>();
        Vector2 barSize = Field<Vector2>(hud, "_barSizeDp");
        Vector2 starMargin = Field<Vector2>(hud, "_marginDp");
        float star = Field<float>(hud, "_starSizeDp");
        float gap = Field<float>(hud, "_gapDp");
        float barBottom = starMargin.y + ((star - barSize.y) * 0.5f) + barSize.y;

        Assert.That(margin.y, Is.GreaterThanOrEqualTo(barBottom), "the auto-cast row overlaps the health bar.");
        Assert.That(margin.y - barBottom, Is.EqualTo(8f).Within(1e-3f), "the row is not 8 dp under the bar.");
        Assert.That(margin.x, Is.EqualTo(starMargin.x + star + gap).Within(1e-3f), "the row is not under the bar's left edge.");
    }

    // ---- Guards ----------------------------------------------------------------------------------

    [Test]
    public void Construct_RefusesNull()
    {
        StartRun(actives: 1);
        Row();

        // The catalog left at UI-01e with the kind tint; the two books came in its place.
        Assert.Throws<ArgumentNullException>(() => _row.Construct(null, _hub, IconBook(), LookBook()));
        Assert.Throws<ArgumentNullException>(() => _row.Construct(_session, null, IconBook(), LookBook()));
        Assert.Throws<ArgumentNullException>(() => _row.Construct(_session, _hub, null, LookBook()));
        Assert.Throws<ArgumentNullException>(() => _row.Construct(_session, _hub, IconBook(), null));
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>
    /// Builds a resumed run owning <paramref name="actives"/> actives, without starting it.
    /// </summary>
    /// <param name="manual">
    /// Which owned active sits in each of CC §6.2's four slots, by its index in
    /// <paramref name="actives"/> — and −1 for an empty one. A skill in a slot is Manual, so it is
    /// exactly the set this row must <em>not</em> hold.
    /// </param>
    /// <param name="firesImmediately">
    /// Whether each active's trigger is ever met. <c>true</c> is CC §6.4's always-true clause and
    /// <c>false</c> is one <c>HpFraction</c> can never satisfy, which is what lets one cell's fill
    /// move while another's demonstrably does not.
    /// </param>
    /// <remarks>
    /// Resumed rather than fresh for <c>SkillBarPresenterTests</c>' reason: it is the only door
    /// <c>RunSession.Start</c> adds a restored Active through short of playing a level-up, and the
    /// only one that can state a loadout. The level accounts for the nodes (M3-08a rule 9).
    /// </remarks>
    private void CreateRun(int actives, int[] manual = null, bool[] firesImmediately = null, int volleyEvery = 0)
    {
        _activeIds = new string[actives];

        var skills = new List<SkillSpec>();
        var activeTier = new List<ContentId>();
        var taken = new List<ContentId>();

        for (int i = 0; i < actives; i++)
        {
            _activeIds[i] = $"skill.test.a{i}";

            bool fires = firesImmediately is null || i >= firesImmediately.Length || firesImmediately[i];

            SkillSpec node = ActiveNode(_activeIds[i], FirstCooldown + i, fires);

            skills.Add(node);
            activeTier.Add(node.Id);
            taken.Add(node.Id);
        }

        // A branch needs at least one node and a tree wants exactly three branches, so a run owning
        // no actives borrows a passive for branch a.
        if (activeTier.Count == 0)
        {
            SkillSpec filler = PassiveNode("skill.test.p90");

            skills.Add(filler);
            activeTier.Add(filler.Id);
        }

        // The volley's node (UI-01e): a Passive that gives the class's volley its count, taken like
        // the actives, so the row meets a volley exactly as a run does — through a node.
        SkillSpec second = volleyEvery > 0 ? VolleyNode(volleyEvery) : PassiveNode("skill.test.p0");
        SkillSpec third = PassiveNode("skill.test.p1");

        if (volleyEvery > 0)
        {
            taken.Add(second.Id);
        }

        skills.Add(second);
        skills.Add(third);

        var tree = new SkillTreeSpec(
            new ContentId(TreeId),
            new ContentId(OathboundId),
            new[]
            {
                new SkillBranchSpec(new LocKey("branch.a"), new IReadOnlyList<ContentId>[] { activeTier }),
                new SkillBranchSpec(new LocKey("branch.b"), new IReadOnlyList<ContentId>[] { new[] { second.Id } }),
                new SkillBranchSpec(new LocKey("branch.c"), new IReadOnlyList<ContentId>[] { new[] { third.Id } }),
            });

        _catalog = new ContentCatalog(
            new[] { Oathbound(volleyEvery > 0) },
            new[] { Husk() },
            new[] { Mode() },
            skills,
            new[] { tree });

        _session = new RunSession(
            _catalog,
            _random,
            _hub,
            new RecordingIntents(),
            new RunRecorder(_random, _clock, _hub),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        var slots = new ContentId[SkillRunner.MaxManualSlots];

        if (manual is not null)
        {
            for (int slot = 0; slot < slots.Length && slot < manual.Length; slot++)
            {
                if (manual[slot] >= 0)
                {
                    slots[slot] = new ContentId(_activeIds[manual[slot]]);
                }
            }
        }

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
                1 + taken.Count,
                0f,
                0,
                taken.ToArray(),
                slots,
                default,
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>()));
    }

    /// <summary><see cref="CreateRun"/>, started.</summary>
    private void StartRun(int actives, int[] manual = null, bool[] firesImmediately = null, int volleyEvery = 0)
    {
        CreateRun(actives, manual, firesImmediately, volleyEvery);

        _session.Start(_config);
    }

    /// <summary>
    /// Instantiates the shipped HUD and injects the row on it — the screen under test is the asset.
    /// </summary>
    private AutoCastRow Row()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        _hud = Object.Instantiate(prefab);
        _spawned.Add(_hud);

        _row = _hud.GetComponentInChildren<AutoCastRow>(true);

        Assert.That(_row, Is.Not.Null, "AutoCastRow did not load off Hud.prefab (Traps §5).");

        // Pinned, because a Canvas instantiated in EditMode has never been driven by its own
        // CanvasScaler and the layout rows measure in dp against this.
        _hud.GetComponent<Canvas>().scaleFactor = 1f;

        // What RunScope's RegisterComponent does, and then the Start that Unity does not run on an
        // instantiated prefab in EditMode: the placement and the first membership build live there.
        _row.Construct(_session, _hub, IconBook(), LookBook());

        Invoke(_row, "Start");

        return _row;
    }

    /// <summary>The slot bar on the same HUD, injected — for the one row about both readouts.</summary>
    private SkillBarPresenter Bar()
    {
        var bar = _hud.GetComponentInChildren<SkillBarPresenter>(true);

        Assert.That(bar, Is.Not.Null, "SkillBarPresenter did not load off Hud.prefab (Traps §5).");

        bar.Construct(_session, new SkillSlotInput(_session), _hub, _catalog, Passthrough());

        Invoke(bar, "Start");

        return bar;
    }

    /// <summary>One frame of the row's own <c>Update</c> — the rebuild, then the fills.</summary>
    /// <remarks>
    /// Invoked rather than waited for, because EditMode has no player loop. This is also the frame
    /// the deferred rebuild happens on; see the fixture remarks for why the deferral is mandatory.
    /// </remarks>
    private void PassAFrame() => Invoke(_row, "Update");

    private void Place() => Invoke(_row, "Place");

    /// <summary>Advances the run by <paramref name="seconds"/> of simulated time.</summary>
    private void TickRun(float seconds)
    {
        int frames = Mathf.Max(1, Mathf.CeilToInt(seconds / Frame));

        for (int i = 0; i < frames; i++)
        {
            _session.Tick(new WorldSnapshot(Capacity)
            {
                Dt = Frame,
                PlayerPosition = CoreVector3.Zero,
            });
        }
    }

    private Image[] Fills() => Field<Image[]>(_row, "_sweeps");

    private RectTransform[] Strips() => Field<RectTransform[]>(_row, "_cells");

    private Image[] Icons() => Field<Image[]>(_row, "_icons");

    private Image[] Glows() => Field<Image[]>(_row, "_glows");

    private Image[] Pips() => Field<Image[]>(_row, "_pips");

    private SkillIconBook IconBook() => new SkillIconBook(_iconMap);

    private CharacterLookBook LookBook() => new CharacterLookBook(new Dictionary<ContentId, CharacterLook>
    {
        [new ContentId(OathboundId)] = new CharacterLook(null, null, _volleyNode),
    });

    /// <summary>A sprite that is not the placeholder: the Ranger's own volley icon, off the art folder.</summary>
    private static Sprite TestIcon()
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/UI/Icons/Icon_Volley.png");

        Assert.That(sprite, Is.Not.Null, "the fixture's sprite is missing.");

        return sprite;
    }

    /// <summary>
    /// Looses <paramref name="shots"/> ordinary shots on the run's volley — what <c>PlayerCombat</c>
    /// does once per damage frame — through the internal combat handle, which a Tests.Game fixture
    /// reaches by reflection alone (AR §18.2).
    /// </summary>
    private void Loose(int shots)
    {
        object combat = typeof(RunState)
            .GetProperty("Combat", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(_session.State);

        var volley = (Volley)combat.GetType().GetProperty("Volley").GetValue(combat);

        Assert.That(volley, Is.Not.Null, "the fixture's class has no volley.");

        for (int i = 0; i < shots; i++)
        {
            volley.Loose();
        }
    }

    private bool[] Shown() => Strips().Select(s => s != null && s.gameObject.activeSelf).ToArray();

    /// <summary>Which owned active cell <paramref name="cell"/> is drawing, by runner index.</summary>
    private int CellSkill(int cell) => Field<int[]>(_row, "_cellSkill")[cell];

    private IPlayerCommands Commands() => _session;

    private static ManualSkillButton[] Buttons(SkillBarPresenter bar) =>
        Field<ManualSkillButton[]>(bar, "_buttons");

    /// <summary>
    /// How many canvas units one dp is worth here — <c>AutoCastRow.PixelsPerDp</c>, with the scale
    /// <see cref="Row"/> pinned.
    /// </summary>
    private static float PixelsPerDp() => StickShaper.PixelsPerDp(Screen.dpi);

    private static bool[] Off(int count) => Enumerable.Repeat(false, count).ToArray();

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

    /// <summary>
    /// One Active whose trigger either always holds or never does.
    /// </summary>
    /// <remarks>
    /// <c>HpFraction</c> is at most 1, so <em>below 1.5</em> holds on every tick of a healthy run and
    /// of a hurt one, while <em>below 0</em> can never hold at all — <c>LevelUpPresenterTests.Node</c>'s
    /// always-true clause, with the switch this fixture needs beside it.
    /// </remarks>
    private static SkillSpec ActiveNode(string id, float cooldown, bool fires) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Active,
        Array.Empty<IEffect>(),
        new ActiveSpec(
            cooldown,
            new TriggerSpec(new[]
            {
                new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, fires ? 1.5f : 0f),
            }),
            new IEffect[]
            {
                new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.1f),
            }));

    private static SkillSpec PassiveNode(string id) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Passive,
        new IEffect[]
        {
            new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f),
        });

    private static CharacterSpec Oathbound(bool volley = false) => new CharacterSpec(
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
        new ShieldSpec(30f, 3f, 1f),
        volley: volley ? new VolleySpec(5, 30f, 1.5f) : null);

    /// <summary>The volley's node: a Passive that gives the class's volley <paramref name="every"/>.</summary>
    private static SkillSpec VolleyNode(int every) => new SkillSpec(
        new ContentId(VolleyNodeId),
        new LocKey($"{VolleyNodeId}.name"),
        new LocKey($"{VolleyNodeId}.desc"),
        SkillKind.Passive,
        new IEffect[]
        {
            new ModifyStat(PlayerStat.VolleyEvery, ModifierKind.Flat, every),
        });

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
