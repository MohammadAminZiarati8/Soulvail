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
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
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

        Assert.That(cellsBefore, Is.EqualTo(SkillRunner.MaxActives), "the prefab authors twelve cells.");
        Assert.That(imagesBefore, Is.EqualTo(SkillRunner.MaxActives * 2), "a body and a fill each.");

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

        // Twelve 24 dp cells with 4 dp gaps is 332 dp, which fits a landscape safe area under a
        // 320 dp health bar — the arithmetic rule 7 sizes the row by.
        float pxPerDp = PixelsPerDp();
        var first = (RectTransform)Strips()[0].transform;
        var last = (RectTransform)Strips()[SkillRunner.MaxActives - 1].transform;

        Assert.That(
            (last.anchoredPosition.x + last.sizeDelta.x - first.anchoredPosition.x) / pxPerDp,
            Is.EqualTo(332f).Within(0.01f));
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
    public void Row_FillsTrackTheirSkills()
    {
        // A fires itself on the first tick and B never does, which is what makes "that cell's fill
        // moves and the other's does not" a claim about the mapping rather than about two fills that
        // happen to be at different places.
        StartRun(actives: 2, firesImmediately: new[] { true, false });
        Row();

        Assert.That(_row.CellCount, Is.EqualTo(2), "the fixture's premise.");

        Image a = Fills()[0];
        Image b = Fills()[1];

        Assert.That(a.fillAmount, Is.EqualTo(1f).Within(1e-3f), "a live skill draws a full ring.");
        Assert.That(b.fillAmount, Is.EqualTo(1f).Within(1e-3f));

        // One tick: the runner casts at most one a tick, in take order (M3-06), and only A's trigger
        // is ever met.
        TickRun(Frame);
        PassAFrame();

        // Empty the instant the cast starts: the fill is 1 − fraction, which is SkillButton's and
        // ManualSkillButton's convention and deliberately the same one, because the player can have
        // a cell and a button on screen at once.
        Assert.That(a.fillAmount, Is.EqualTo(0f).Within(1e-3f), "A's cell did not follow its cast.");
        Assert.That(b.fillAmount, Is.EqualTo(1f).Within(1e-3f), "B's cell moved for A's cast.");

        // Half the wait gone, half the ring back. The number is core's — SkillCooldownFraction
        // measured against the *current* effective cooldown — and this row only draws it.
        TickRun(FirstCooldown / 2f);
        PassAFrame();

        Assert.That(a.fillAmount, Is.EqualTo(0.5f).Within(0.05f));
        Assert.That(b.fillAmount, Is.EqualTo(1f).Within(1e-3f));

        // Stopped a little short of ready on purpose: A's trigger holds on every tick, so ticking
        // past its cooldown would cast it again and the row would honestly be near-empty rather
        // than full. What the last step is about is that the cell keeps recovering, which a recast
        // would hide.
        TickRun(1f);
        PassAFrame();

        Assert.That(a.fillAmount, Is.EqualTo(0.9f).Within(0.05f));
        Assert.That(b.fillAmount, Is.EqualTo(1f).Within(1e-3f));
    }

    [Test]
    public void Row_WritesOnlyChangedFills()
    {
        // Two Actives whose triggers are never met, so the run is idle and every cell's fraction
        // stays at 0 for the whole row.
        StartRun(actives: 2, firesImmediately: new[] { false, false });
        Row();

        PassAFrame();

        // A sentinel nothing should overwrite. Assigning fillAmount marks the graphic dirty and
        // queues a canvas rebuild, so a row that wrote an unchanged value would rebuild the HUD on
        // every frame of every second a skill is not being cast — which, for a skill on an
        // eight-second cooldown, is most of them. Twelve cells make that argument twelve times over.
        Fills()[0].fillAmount = 0.42f;
        Fills()[1].fillAmount = 0.37f;

        for (int frame = 0; frame < 60; frame++)
        {
            PassAFrame();
        }

        Assert.That(
            Fills()[0].fillAmount,
            Is.EqualTo(0.42f).Within(1e-4f),
            "the row repainted a frame in which nothing had changed.");

        Assert.That(Fills()[1].fillAmount, Is.EqualTo(0.37f).Within(1e-4f));

        // And it *does* write when something changes, so the row above is about redundancy rather
        // than about a row that has stopped drawing.
        SetPrivate(_row, "_dirty", true);
        PassAFrame();

        Assert.That(Fills()[0].fillAmount, Is.EqualTo(1f).Within(1e-3f));
    }

    [Test]
    public void Row_TintsByKind()
    {
        StartRun(actives: 1);
        Row();

        // **A live cell is always the Active tint, because rule 5's membership admits nothing else**
        // — the runner holds Actives and only Actives, so the other three of ledger row 6's four
        // colours are unreachable through a run. That is worth saying rather than working around:
        // the lookup is still total, for TreeNodeView.Frame's reason, and this is where it is proved
        // to answer four different things.
        Assert.That(Strips()[0].color, Is.EqualTo(Tint(SkillKind.Active)));

        var tints = new[]
        {
            Tint(SkillKind.Passive),
            Tint(SkillKind.Active),
            Tint(SkillKind.Upgrade),
            Tint(SkillKind.Keystone),
        };

        Assert.That(
            tints.Distinct().Count(),
            Is.EqualTo(4),
            "two of CH §4's four kinds share a tint, so a player could not tell them apart.");

        // **And they are Palette's four, which is what M3-13a changed here.** This used to compare
        // the row's four serialized fields against OfferCard's, field name by field name, because
        // this was the *third* copy of the same four values — M3-10b rule 9's whole complaint, and
        // the state ledger row 6 predicted. There is one answer now, so the row asserts the answer
        // rather than the agreement, and there is nothing left for a fourth screen to copy.
        Assert.That(Tint(SkillKind.Passive), Is.EqualTo(Palette.KindPassive));
        Assert.That(Tint(SkillKind.Active), Is.EqualTo(Palette.KindActive));
        Assert.That(Tint(SkillKind.Upgrade), Is.EqualTo(Palette.KindUpgrade));
        Assert.That(Tint(SkillKind.Keystone), Is.EqualTo(Palette.KindKeystone));
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
            SetPrivate(_row, "_marginDp", new Vector2(bad, 48f));

            Assert.That(() => Place(), Throws.Nothing, $"Place threw on a margin of {bad}.");
        }

        SetPrivate(_row, "_marginDp", new Vector2(16f, 48f));
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

        // Under the health bar rather than over it: HudPresenter's row is 16 dp down and 24 dp tall,
        // so the first honest place for this one is 40 and it ships at 48.
        HudPresenter hud = _hud.GetComponent<HudPresenter>();
        Vector2 barSize = Field<Vector2>(hud, "_barSizeDp");
        Vector2 barMargin = Field<Vector2>(hud, "_marginDp");

        Assert.That(
            margin.y,
            Is.GreaterThanOrEqualTo(barMargin.y + barSize.y),
            "the auto-cast row overlaps the health bar.");
    }

    // ---- Guards ----------------------------------------------------------------------------------

    [Test]
    public void Construct_RefusesNull()
    {
        StartRun(actives: 1);
        Row();

        Assert.Throws<ArgumentNullException>(() => _row.Construct(null, _hub, _catalog));
        Assert.Throws<ArgumentNullException>(() => _row.Construct(_session, null, _catalog));
        Assert.Throws<ArgumentNullException>(() => _row.Construct(_session, _hub, null));
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
    private void CreateRun(int actives, int[] manual = null, bool[] firesImmediately = null)
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

        SkillSpec second = PassiveNode("skill.test.p0");
        SkillSpec third = PassiveNode("skill.test.p1");

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
            new[] { Oathbound() },
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
                slots));
    }

    /// <summary><see cref="CreateRun"/>, started.</summary>
    private void StartRun(int actives, int[] manual = null, bool[] firesImmediately = null)
    {
        CreateRun(actives, manual, firesImmediately);

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
        _row.Construct(_session, _hub, _catalog);

        Invoke(_row, "Start");

        return _row;
    }

    /// <summary>The slot bar on the same HUD, injected — for the one row about both readouts.</summary>
    private SkillBarPresenter Bar()
    {
        var bar = _hud.GetComponentInChildren<SkillBarPresenter>(true);

        Assert.That(bar, Is.Not.Null, "SkillBarPresenter did not load off Hud.prefab (Traps §5).");

        bar.Construct(_session, new SkillSlotInput(_session), _hub, _catalog);

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

    private Image[] Fills() => Field<Image[]>(_row, "_fills");

    private Image[] Strips() => Field<Image[]>(_row, "_kindStrips");

    private bool[] Shown() => Strips().Select(s => s != null && s.gameObject.activeSelf).ToArray();

    /// <summary>Which owned active cell <paramref name="cell"/> is drawing, by runner index.</summary>
    private int CellSkill(int cell) => Field<int[]>(_row, "_cellSkill")[cell];

    /// <summary>
    /// The row's own kind lookup, invoked directly. <c>BindingFlags.Static</c> as of M3-13a: the
    /// method read four serialized fields until this task and reads <c>Palette</c> now, so it holds
    /// no instance state left to be an instance method for.
    /// </summary>
    private static Color Tint(SkillKind kind) =>
        (Color)typeof(AutoCastRow)
            .GetMethod("Tint", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { kind });

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
}
