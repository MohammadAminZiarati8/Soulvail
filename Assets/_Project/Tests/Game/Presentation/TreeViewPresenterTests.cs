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
/// The tree view: three branches, the player's path, and the two doors into it. CH §5, §5.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over a real <c>RunSession</c> and a real <c>DomainEventHub</c></b>, which is
/// <c>LevelUpPresenterTests</c>' shape and the only shape that would catch the failure this screen
/// has: a cell framed against gating the run does not actually have. Every state a cell draws is
/// cross-checked against <c>RunState</c>'s own reads rather than against a number this file counted.
/// </para>
/// <para>
/// <b>The screens under test are the shipped prefabs</b> — all three of them, instantiated per row.
/// Hand-building one would test a second screen that happens to resemble the asset, and the failure
/// Traps §5 describes — a component that deserialises as null with nothing reporting it — is
/// invisible to a fixture that never loads the asset.
/// </para>
/// <para>
/// <b>The fixture's tree is twenty-seven nodes</b>, which is the shape CH §5 ships and the largest
/// any fixture in this project has built: three branches of two nodes a tier for four tiers plus a
/// keystone. It carries one of each of CH §4's four kinds, because <see cref="Tree_TintsByKind"/>
/// draws them from a legal tree rather than from specs built by hand — and the constraints that
/// forces are real. <c>TreeRules</c> refuses a Keystone that shares its tier with anything, an
/// Upgrade's parent must be in the same branch at a lower tier, and M3-08a's identity guard refuses
/// a saved run whose level cannot explain what it owns, so every <c>StartRun</c> here passes a level
/// of at least <c>1 + taken + pending</c>.
/// </para>
/// <para>
/// <c>Awake</c> and <c>Start</c> do not run in EditMode (Traps §5), so the guards in
/// <c>TreeViewPresenter.Start</c> never fire here and the prefab's own alpha 0 and inactive cell
/// template are what leave the screen down at the top of each row. A frame is passed explicitly
/// where a row needs one.
/// </para>
/// </remarks>
[TestFixture]
public sealed class TreeViewPresenterTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/TreeView.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";
    private const string PausePrefabPath = "Assets/_Project/Prefabs/UI/Pause.prefab";
    private const string LevelUpPrefabPath = "Assets/_Project/Prefabs/UI/LevelUp.prefab";
    private const string SkillsPrefabPath = "Assets/_Project/Prefabs/UI/Skills.prefab";
    private const string HudPrefabPath = "Assets/_Project/Prefabs/UI/Hud.prefab";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string TreeId = "tree.oathbound";
    private const string ArenaId = "arena.pillars";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    /// <summary>CH §5's shape: three branches of 2 × 4 plus a keystone.</summary>
    private const int FullTreeNodes = 27;

    /// <summary>The same, per branch — <c>Tree_DrawsEveryNode</c>'s 9 / 9 / 9.</summary>
    private const int FullBranchNodes = 9;

    /// <summary>M3-12's v1: three branches of two tiers of two, and no keystone anywhere.</summary>
    private const int PartialTreeNodes = 12;

    private const int PartialBranchNodes = 4;

    /// <summary>
    /// Ledger row 9's fourth reader, pinned to the exact string a player would read (rule 7).
    /// </summary>
    private const string ConsecrateKey = "skill.oathbound.consecrate";

    /// <summary>The cell height the prefab ships with, and the field's own default.</summary>
    private const float CellDp = 44f;

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private DomainEventHub _hub;
    private RecordingEvents _spy;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private RunSession _session;
    private SkillTreeSpec _tree;

    private GameObject _screen;
    private TreeViewPresenter _presenter;

    private GameObject _pauseScreen;
    private PausePresenter _pausePresenter;

    private GameObject _levelUpScreen;
    private LevelUpPresenter _levelUpPresenter;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<RunPause> _pauses = new List<RunPause>();

    private float _restoreTimeScale;

    [SetUp]
    public void CreateWorld()
    {
        // The Editor's own globals. RunPause writes both and every row that builds one disposes it,
        // but a row that failed part way through would otherwise leave the whole suite after it
        // running at timeScale 0 — LevelUpPresenterTests' argument, made in the teardown.
        _restoreTimeScale = Time.timeScale;

        _hub = new DomainEventHub();
        _spy = new RecordingEvents();
        _random = new FixedRandom(7, Alternating(8_192));
        _clock = new FixedClock(Instant);
    }

    [TearDown]
    public void DestroyWorld()
    {
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
    }

    // ---- The shape on screen (rules 2, 4) ------------------------------------------------------

    [Test]
    public void Tree_DrawsEveryNode()
    {
        StartRun();
        BuildScreen();

        _presenter.Open(null);

        Assert.That(_presenter.IsOpen, Is.True, "Open left the screen down.");

        TreeNodeButton[] cells = ShownCells();

        Assert.That(cells.Length, Is.EqualTo(FullTreeNodes), "A 27-node tree did not draw 27 cells.");

        // **Three columns of nine, which is what makes this CH §5's tree rather than a list.** The
        // branch a cell hangs under is decided once, when the pool is built, and never re-parented —
        // so a cell in the wrong column is a shape bug that survives every redraw.
        for (int b = 0; b < SkillTreeSpec.BranchCount; b++)
        {
            Assert.That(
                CellsIn(b).Length,
                Is.EqualTo(FullBranchNodes),
                $"Branch {b}'s column does not hold its nine nodes.");
        }

        // **Tier 1 first, in tree order** — SkillTree.Available's own order (AR §18.3), so a cell's
        // index means the same thing on this screen as it does in core.
        for (int b = 0; b < SkillTreeSpec.BranchCount; b++)
        {
            TreeNodeButton[] column = CellsIn(b);
            SkillBranchSpec branch = _tree.Branches[b];

            int index = 0;

            for (int t = 1; t <= branch.TierCount; t++)
            {
                IReadOnlyList<ContentId> tier = branch.Tier(t);

                for (int i = 0; i < tier.Count; i++)
                {
                    Assert.That(
                        column[index].Node,
                        Is.EqualTo(tier[i]),
                        $"Branch {b} position {index} draws the wrong node, so the column is not "
                            + "in tier order.");

                    index++;
                }
            }
        }

        // And the branch's own name heads its column. Resolved through the port as of M3-14a; this
        // screen was built with the empty passthrough, so each key still answers with itself — the
        // claim being made here is about *which* branch heads which column, not about English.
        TMP_Text[] labels = Field<TMP_Text[]>(_presenter, "_branchLabels");

        for (int b = 0; b < SkillTreeSpec.BranchCount; b++)
        {
            Assert.That(labels[b].text, Is.EqualTo(_tree.Branches[b].NameKey.Key));
        }
    }

    [Test]
    public void Tree_DrawsAPartialTree()
    {
        // M3-12's v1 shape: three branches of two tiers of two, no keystone. TreeRules allows it
        // deliberately — refusing it would refuse the milestone's own content.
        StartRun(full: false);
        BuildScreen();

        _presenter.Open(null);

        Assert.That(ShownCells().Length, Is.EqualTo(PartialTreeNodes));

        for (int b = 0; b < SkillTreeSpec.BranchCount; b++)
        {
            Assert.That(
                CellsIn(b).Length,
                Is.EqualTo(PartialBranchNodes),
                $"Branch {b} of a 2/2 tree did not draw four cells.");
        }

        Assert.That(
            EmptyLabel().gameObject.activeSelf,
            Is.False,
            "A tree that exists showed the line that says there is none.");
    }

    [Test]
    public void Tree_NoTreeShowsTheEmptyLine()
    {
        // Every run in the build until M3-12: TryGetTreeFor answers false, so there is nothing to
        // draw and neither door may offer a way in (rule 2).
        StartRun(withTree: false);
        BuildScreen();
        BuildPauseScreen();
        BuildLevelUpScreen();

        _presenter.Open(null);

        Assert.That(_presenter.HasTree, Is.False, "The fixture's premise: this class has no tree.");
        Assert.That(ShownCells(), Is.Empty, "A class with no tree drew cells.");

        Assert.That(
            EmptyLabel().gameObject.activeSelf,
            Is.True,
            "A class with no tree shows a blank screen rather than a line saying so, and an empty "
                + "screen and a broken screen look identical.");

        Assert.That(
            EmptyLabel().text,
            Does.StartWith("ui.tree."),
            "The empty line is English typed into a prefab rather than a LocKey (ADR-0012).");

        // **And both doors take their button away rather than offering a dead one.** A button onto
        // a blank screen is worse than no button, and shipping one dead would make M3-12 look like
        // it did nothing.
        PassAPauseFrame();
        PassALevelUpFrame();

        Assert.That(
            PauseButton("_viewTree").gameObject.activeSelf,
            Is.False,
            "The pause panel offers View Tree for a class with no tree.");

        Assert.That(
            Field<Button>(_levelUpPresenter, "_viewTree").gameObject.activeSelf,
            Is.False,
            "The level-up screen offers View Tree for a class with no tree.");
    }

    // ---- What a cell says (rules 3, 7, 8) ------------------------------------------------------

    [Test]
    public void Tree_StatesAreThree()
    {
        // Two taken in branch a's first tier, which opens that branch's tier 2 and leaves every
        // other branch where it was. Level accounts for the two nodes — M3-08a rule 9's identity.
        StartRun(level: 3, taken: new[] { ConsecrateKey, Node('a', 1, 'b') });
        BuildScreen();

        _presenter.Open(null);

        var states = new Dictionary<NodeState, int>
        {
            [NodeState.Locked] = 0,
            [NodeState.Available] = 0,
            [NodeState.Taken] = 0,
        };

        // **Every cell cross-checked against core rather than counted against a number this file
        // worked out.** A screen and a run that disagree is the whole failure here, and the only
        // authority on which is right is RunState.
        foreach (TreeNodeButton cell in ShownCells())
        {
            NodeState expected = Expected(cell.Node);

            Assert.That(
                cell.State,
                Is.EqualTo(expected),
                $"'{cell.Node}' is drawn as {cell.State} and the run says {expected}.");

            states[expected]++;
        }

        Assert.That(states[NodeState.Taken], Is.EqualTo(2), "The two picked nodes are not Taken.");

        // Branch a's tier 2 opened and branches b and c still offer their first tiers, so the
        // number is not two — and it is asserted against core's own sweep rather than restated
        // here, because a count this file worked out is a second copy of CH §5's gating.
        Assert.That(
            states[NodeState.Available],
            Is.EqualTo(AvailableCount()),
            "The screen's available count disagrees with the run's.");

        Assert.That(
            states[NodeState.Locked],
            Is.EqualTo(FullTreeNodes - states[NodeState.Taken] - states[NodeState.Available]),
            "A cell was drawn as none of the three states.");

        // And all three are actually on screen, so the row cannot pass against a screen that draws
        // everything the same way.
        Assert.That(states[NodeState.Locked], Is.GreaterThan(0));
        Assert.That(states[NodeState.Available], Is.GreaterThan(0));
    }

    [Test]
    public void Tree_TilesByState()
    {
        StartRun(level: 3, taken: new[] { ConsecrateKey, Node('a', 1, 'b') });
        BuildScreen();

        _presenter.Open(null);

        Assert.That(Expected(new ContentId(Node('a', 2, 'b'))), Is.EqualTo(NodeState.Available));
        Assert.That(Expected(new ContentId(Node('a', 4, 'a'))), Is.EqualTo(NodeState.Locked));

        Sprite taken = TileOf(ConsecrateKey);
        Sprite available = TileOf(Node('a', 2, 'b'));
        Sprite locked = TileOf(Node('a', 4, 'a'));

        // Three different tiles, and the tile is the thing CH §5.1's "the player's path
        // highlighted" is made of — two states sharing one is a tree that cannot be read at all.
        Assert.That(
            new[] { locked, available, taken }.Distinct().Count(),
            Is.EqualTo(3),
            "Two of the three node states are drawn on the same tile.");

        Assert.That(taken.name, Is.EqualTo("TreeTile_Taken"));
        Assert.That(available.name, Is.EqualTo("TreeTile_Available"));
        Assert.That(locked.name, Is.EqualTo("TreeTile_Locked"));

        // The art carries the colour; the one tint left is the icon's, dimmed to Palette's locked grey.
        Assert.That(Field<Image>(CellFor(Node('a', 4, 'a')), "_icon").color, Is.EqualTo(Palette.NodeLocked));
        Assert.That(Field<Image>(CellFor(ConsecrateKey), "_icon").color, Is.EqualTo(Color.white));
    }

    // ---- The fourth state: a node Banish took (M6-03b rules 7, 8, 9) -----------------------------

    [Test]
    public void Tree_DrawsABanishedNodeAsBanished()
    {
        // One taken, one banished — and the banished one is tier 1 of branch b, which would otherwise
        // be Available. That is the failure this row exists for: until M6-03b it drew as Locked at
        // best, and as a node the player could still be offered at worst.
        StartRun(level: 2, taken: new[] { ConsecrateKey }, banished: new[] { Node('b', 1, 'a') });
        BuildScreen();

        _presenter.Open(null);

        Assert.That(CellFor(Node('b', 1, 'a')).State, Is.EqualTo(NodeState.Banished));
        Assert.That(CellFor(ConsecrateKey).State, Is.EqualTo(NodeState.Taken));

        TreeNodeButton[] cells = ShownCells();

        Assert.That(cells.Count(c => c.State == NodeState.Banished), Is.EqualTo(1));
        Assert.That(cells.Count(c => c.State == NodeState.Taken), Is.EqualTo(1));

        // The rest are Locked or Available, and each agrees with core.
        foreach (TreeNodeButton cell in cells)
        {
            Assert.That(cell.State, Is.EqualTo(Expected(cell.Node)), $"'{cell.Node}'.");
        }

        Assert.That(TileOf(Node('b', 1, 'a')).name, Is.EqualTo("TreeTile_Locked"));
    }

    [Test]
    public void Tree_TheFourStatesAreDisjoint()
    {
        // A run mid-banish, with a save that tries to be two things at once: Consecrate is both taken
        // and banished, which SkillTree.RestoreBanished drops (M6-02b) — so the take wins and no id is
        // ever two states. A stranger id is dropped the same way.
        StartRun(
            level: 3,
            taken: new[] { ConsecrateKey, Node('a', 1, 'b') },
            banished: new[] { ConsecrateKey, Node('b', 1, 'a'), Node('c', 1, 'b'), "skill.test.stranger" });
        BuildScreen();

        _presenter.Open(null);

        RunState state = _session.State;

        foreach (SkillBranchSpec branch in _tree.Branches)
        {
            for (int t = 1; t <= branch.TierCount; t++)
            {
                foreach (ContentId id in branch.Tier(t))
                {
                    int memberships = (state.TakenNodeIds.Contains(id) ? 1 : 0)
                        + (state.BanishedNodeIds.Contains(id) ? 1 : 0)
                        + (state.IsNodeAvailable(id) ? 1 : 0);

                    Assert.That(memberships, Is.LessThanOrEqualTo(1), $"'{id}' is in two of taken, banished, available.");
                }
            }
        }

        Assert.That(state.BanishedNodeIds, Has.No.Member(new ContentId(ConsecrateKey)), "a taken node was banished.");
        Assert.That(ShownCells().Count(c => c.State == NodeState.Banished), Is.EqualTo(2));
        Assert.That(CellFor(ConsecrateKey).State, Is.EqualTo(NodeState.Taken));
    }

    [Test]
    public void Node_BanishedWearsTheLockedTile()
    {
        StartRun();
        BuildScreen();

        TreeNodeButton cell = _cellTemplateOf();

        cell.Show(_catalog.Skill(new ContentId(ConsecrateKey)), NodeState.Banished, null, 1);

        Assert.That(cell.State, Is.EqualTo(NodeState.Banished));
        Assert.That(Field<Image>(cell, "_tile").sprite, Is.SameAs(Field<Sprite>(cell, "_lockedTile")));
        Assert.That(Field<Image>(cell, "_icon").color, Is.EqualTo(Palette.NodeLocked));
        Assert.That(Field<Image>(cell, "_badge").sprite, Is.SameAs(Field<Sprite>(cell, "_lockBadge")));
    }

    [Test]
    public void Node_BanishedIsDimmerThanLocked()
    {
        Color.RGBToHSV(Palette.NodeBanished, out float banishedHue, out _, out float banished);
        Color.RGBToHSV(Palette.NodeLocked, out float lockedHue, out _, out float locked);

        // Rule 8: gone reads dimmer than out of reach.
        Assert.That(banished, Is.LessThan(locked), "a banished node is no dimmer than a locked one.");

        // Darkened and nothing else — no hue shift toward the Veil's violet.
        Assert.That(banishedHue, Is.EqualTo(lockedHue).Within(0.05f), "NodeBanished shifted hue.");

        Assert.That(Palette.IsDanger(Palette.NodeBanished), Is.False);
        Assert.That(Palette.IsDanger(Palette.NodeLocked), Is.False);
    }

    [Test]
    public void Node_AStateWithNoColourStillThrows()
    {
        StartRun();
        BuildScreen();

        // A fifth member, one past Banished — TreeNodeButton's rule survives the fourth.
        var fifth = (NodeState)(Enum.GetValues(typeof(NodeState)).Cast<int>().Max() + 1);

        Assert.That(Enum.IsDefined(typeof(NodeState), fifth), Is.False, "the fixture's premise.");
        Assert.That(
            () => _cellTemplateOf().Show(_catalog.Skill(new ContentId(ConsecrateKey)), fifth, null, 1),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Tree_DrawsEnglish()
    {
        StartRun();

        CharacterSpec character = _catalog.Character(_session.State.CharacterId);

        BuildScreen(new DictionaryLocalizer(
            character.NameKey.Key, "Paladin",
            _tree.Branches[0].NameKey.Key, "Oath"));

        _presenter.Open(null);

        // **M3-09d's `Tree_DrawsKeysNotEnglish`, inverted** — rewritten at UI-04c, when the tile
        // stopped carrying a node's name: the words left on the screen are the branch headings and the
        // class plate, and both resolve through the port.
        Assert.That(Field<TMP_Text[]>(_presenter, "_branchLabels")[0].text, Is.EqualTo("Oath"));
        Assert.That(Field<TMP_Text>(Field<TreeBlockView>(_presenter, "_block"), "_className").text, Is.EqualTo("Paladin"));

        // And the member is still `Key`. M3-00c recorded the spec's four `.Value` sites two spec
        // groups ago and M3-08b fixed them; this task reads the same member inside TableLocalizer.
        Assert.That(
            typeof(LocKey).GetProperty("Value"),
            Is.Null,
            "LocKey grew a Value member. If the struct really has both now, say which one a cell "
                + "draws.");
    }

    // ---- What it cannot do (rules 1, 10) -------------------------------------------------------

    [Test]
    public void Tree_DrawingRun_SendsNothing()
    {
        // **UI-03c rule 7, rewritten from M3-09d's "no Button, no command port".** The presenter
        // takes IProgressionCommands now and every cell carries a Button, so the read-only view of a
        // drawing run is a state rather than a structure: no cell interactable, and a tap on every
        // one — Available ones included, with a pick owed — sends nothing.
        StartRun(level: 2, pending: 1);
        BuildScreen();

        _presenter.Open(null);

        Assert.That(_session.State.IsFreePick, Is.False, "The fixture's premise: this mode draws.");

        foreach (TreeNodeButton cell in ShownCells())
        {
            Assert.That(cell.IsTakeable, Is.False, $"'{cell.Node}' is takeable in a drawing run.");
            Assert.That(
                Field<Button>(cell, "_button").interactable,
                Is.False,
                $"The cell for '{cell.Node}' can be tapped in a drawing run.");

            Field<Button>(cell, "_button").onClick.Invoke();
        }

        Assert.That(_session.State.TakenNodeCount, Is.Zero, "The tree view took a node.");
        Assert.That(_session.State.PendingLevelUps, Is.EqualTo(1), "The tree view spent the pick.");
        Assert.That(_spy.Count<NodeTaken>(), Is.Zero);
    }

    [Test]
    public void Tree_BuildsOnceOnOpen()
    {
        StartRun();
        BuildScreen();

        _presenter.Open(null);

        // **The claim, asked of the class rather than of a frame counter** — SkillsPresenterTests'
        // phrasing, and rule 10's strongest form. The tree cannot change while this is up: the tick
        // is gated on both doors and the one thing that could change it is the screen underneath.
        Assert.That(
            typeof(TreeViewPresenter)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => m.Name is "Update" or "LateUpdate" or "FixedUpdate"),
            Is.Empty,
            "TreeViewPresenter declares a per-frame method. Rule 10 says it renders no events and "
                + "builds on open.");

        Assert.That(
            typeof(TreeViewPresenter)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => typeof(System.Collections.IEnumerator).IsAssignableFrom(m.ReturnType)),
            Is.Empty,
            "TreeViewPresenter declares a coroutine. Nothing here may wait: the engine clock is "
                + "stopped while this screen is up.");

        // And the consequence, spelled out: a sentinel written over a cell survives every frame the
        // screen is up, because nothing in this class runs on one.
        TreeNodeButton cell = ShownCells()[0];

        Label(cell, "_numeral").text = "sentinel";

        for (int frame = 0; frame < 60; frame++)
        {
            PassAFrame();
        }

        Assert.That(
            Text(cell, "_numeral"),
            Is.EqualTo("sentinel"),
            "Something repainted a cell without being asked, so the tree is being polled.");
    }

    // ---- The first door: the pause panel (rule 5) ----------------------------------------------

    [Test]
    public void Pause_OpensAndReturns()
    {
        StartRun();
        BuildScreen();
        BuildPauseScreen();

        TapIcon();

        Assert.That(_pausePresenter.IsOpen, Is.True, "The fixture's premise: the panel is up.");
        Assert.That(_presenter.IsOpen, Is.False);

        TapViewTreeOnPause();

        Assert.That(_presenter.IsOpen, Is.True, "View Tree did not raise the screen.");
        Assert.That(ShownCells().Length, Is.EqualTo(FullTreeNodes), "It went up without drawing.");

        // The panel's buttons go inert. The tree's canvas sorts above the pause one and covers it
        // anyway; this is the brace that holds when a later screen forgets its scrim.
        foreach (string button in new[] { "_resume", "_quit", "_skills", "_viewTree" })
        {
            Assert.That(PauseButton(button).interactable, Is.False, $"{button} can still be tapped.");
        }

        Assert.That(_pausePresenter.IsOpen, Is.True, "The panel closed itself under the screen.");
        Assert.That(Pause().Holder, Is.EqualTo(PauseReason.Menu));

        TapClose();

        Assert.That(_presenter.IsOpen, Is.False, "Close left the screen up.");

        // **Back on the same frame, through the callback** — and PassAPauseFrame would have done it
        // too, because the poll is still the net under it (rule 5).
        foreach (string button in new[] { "_resume", "_quit", "_skills", "_viewTree" })
        {
            Assert.That(PauseButton(button).interactable, Is.True, $"{button} never came back.");
        }

        // **And it lowered nothing**: a door's open holds no pause (only the star's does, UI-03c),
        // so closing it is a canvas going down rather than a gate being released.
        Assert.That(Pause().IsPaused, Is.True, "Closing the tree resumed the run.");
        Assert.That(Pause().Holder, Is.EqualTo(PauseReason.Menu));
        Assert.That(_pausePresenter.IsOpen, Is.True);
    }

    // ---- The second door: the level-up screen (rules 5, 6) --------------------------------------

    [Test]
    public void LevelUp_OpensAndReturns()
    {
        StartRun(level: 3, pending: 1);
        BuildScreen();
        BuildLevelUpScreen();

        _session.OpenLevelUp();

        Assert.That(_levelUpPresenter.IsShown, Is.True, "The fixture's premise: three cards are up.");

        string[] before = CardKeys();
        bool[] liveBefore = CardInteractables();

        Assert.That(before.Length, Is.EqualTo(3), "The fixture's premise: an offer of three.");

        PassALevelUpFrame();
        TapViewTreeOnLevelUp();

        Assert.That(_presenter.IsOpen, Is.True, "View Tree did not raise the tree.");

        // **Hidden, not destroyed** (rule 6). The offer is state on LevelUpFlow and nothing here
        // touches it, so the cards are still there behind an alpha of 0 — which is also what lets
        // the tree be read over them.
        Assert.That(_levelUpPresenter.IsShown, Is.False, "The cards are still drawn over the tree.");
        Assert.That(LevelUpRoot().alpha, Is.Zero);
        Assert.That(LevelUpRoot().blocksRaycasts, Is.False, "The hidden cards still take touches.");

        foreach (OfferCard card in Cards())
        {
            Assert.That(card.IsShown, Is.True, "A card was destroyed rather than veiled.");
        }

        TapClose();

        Assert.That(_presenter.IsOpen, Is.False, "Close left the tree up.");
        Assert.That(_levelUpPresenter.IsShown, Is.True, "The cards never came back.");

        // **The same three ids, still interactable.** A redraw on the way back would have re-armed
        // cards a tap in the same frame had deliberately killed, which is rule 5's failure.
        Assert.That(CardKeys(), Is.EqualTo(before), "The cards came back drawing different nodes.");
        Assert.That(CardInteractables(), Is.EqualTo(liveBefore), "The cards came back dead or re-armed.");

        Assert.That(
            _spy.Count<OfferPresented>(),
            Is.EqualTo(1),
            "Looking at the tree drew a second offer, so the screen redrew rather than unveiled.");
    }

    [Test]
    public void LevelUp_KeepsThePauseThroughout()
    {
        StartRun(level: 3, pending: 1);
        BuildScreen();
        BuildLevelUpScreen();

        RunPause pause = NewPause();

        _session.OpenLevelUp();

        // Held the way the running game holds it: RunTicker.LevelUpPhase raised it when HasOffer
        // went true and has not lowered it (M3-08a rule 12, ruled at M3-08b).
        pause.Pause(PauseReason.LevelUp);

        Assert.That(Time.timeScale, Is.Zero, "The fixture's premise: the game is stopped.");

        // **Every step, and none of them may throw** — RunPause refuses a second reason, so a tree
        // that took a pause of its own would fail here rather than quietly becoming a second gate.
        Assert.That(() => TapViewTreeOnLevelUp(), Throws.Nothing, "Opening the tree re-entered the pause.");

        Assert.That(pause.IsPaused, Is.True, "The tree lowered the pause it was opened over.");
        Assert.That(pause.Holder, Is.EqualTo(PauseReason.LevelUp));
        Assert.That(Time.timeScale, Is.Zero, "A planning screen resumed the fight underneath itself.");

        Assert.That(() => TapClose(), Throws.Nothing);

        Assert.That(pause.IsPaused, Is.True, "Closing the tree resumed the run under the cards.");
        Assert.That(pause.Holder, Is.EqualTo(PauseReason.LevelUp));
        Assert.That(Time.timeScale, Is.Zero);

        // Held exactly once, which is what "one gate rather than three" means: releasing it once
        // gives the clock back, and a second reason would have thrown on the way in.
        pause.Resume(PauseReason.LevelUp);

        Assert.That(pause.IsPaused, Is.False, "The pause was held more than once.");
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    [Test]
    public void LevelUp_ViewingSpendsNoPick()
    {
        // Two picks owed: one is spent by the tap at the end, and the other must survive the
        // looking. Level accounts for both — M3-08a rule 9's identity.
        StartRun(level: 4, pending: 2);
        BuildScreen();
        BuildLevelUpScreen();

        _session.OpenLevelUp();

        Assert.That(_session.State.PendingLevelUps, Is.EqualTo(2), "The fixture's premise.");

        TapViewTreeOnLevelUp();
        TapClose();

        // **Looking is free**, which is the whole promise of "opt-in, so it never slows down anyone
        // who doesn't care": nothing on this screen sends a command, so nothing about the run moved.
        Assert.That(
            _session.State.PendingLevelUps,
            Is.EqualTo(2),
            "Opening the tree spent a pick.");

        Assert.That(_session.State.TakenNodeCount, Is.Zero, "Opening the tree took a node.");

        PassALevelUpFrame();
        TapCard(0);

        Assert.That(
            _session.State.PendingLevelUps,
            Is.EqualTo(1),
            "The pick after a look cost one more or one fewer than a pick.");

        Assert.That(_session.State.TakenNodeCount, Is.EqualTo(1));
    }

    [Test]
    public void LevelUp_ShowsTheNewStateAfterAPick()
    {
        StartRun(level: 4, pending: 2);
        BuildScreen();
        BuildLevelUpScreen();

        _session.OpenLevelUp();

        ContentId chosen = _session.State.Offer[0];

        // Tier 1 of some branch, because that is all a fresh tree offers — which is what makes this
        // row's "and the one it unlocked" reachable in one pick.
        Assert.That(_tree.TryLocate(chosen, out int branch, out int tier), Is.True);
        Assert.That(tier, Is.EqualTo(1), "A fresh 27-tree offered something above tier 1.");

        TapCard(0);

        Assert.That(_levelUpPresenter.IsShown, Is.True, "The fixture's premise: a second pick was owed.");

        PassALevelUpFrame();
        TapViewTreeOnLevelUp();

        // **The tree reads the state the pick just made**, and the reason it can is an ordering:
        // SkillTree.Record sets _taken, _takenInBranch and _takenIds *before* it publishes
        // NodeTaken, so everything downstream of ChooseOffer sees the node. (That is the opposite of
        // RunState.OwnedActiveCount, which the runner is fed afterwards — M3-09c's finding.)
        Assert.That(
            CellFor(chosen).State,
            Is.EqualTo(NodeState.Taken),
            "The node taken a moment ago still reads as one the player does not own.");

        // Tier 2 of that branch now wants one node of it taken, and one is. The Upgrade among them
        // is excluded by name: its own gate is its parent, not the tier.
        foreach (ContentId id in _tree.Branches[branch].Tier(2))
        {
            if (_catalog.Skill(id).Kind == SkillKind.Upgrade)
            {
                continue;
            }

            Assert.That(
                CellFor(id).State,
                Is.EqualTo(NodeState.Available),
                $"'{id}' is at branch {branch} tier 2 over a branch with one node taken, and the "
                    + "screen still draws it locked.");
        }

        // And the screen is not simply painting everything available: tier 3 of the same branch
        // wants two and has one.
        foreach (ContentId id in _tree.Branches[branch].Tier(3))
        {
            Assert.That(CellFor(id).State, Is.EqualTo(NodeState.Locked));
        }

        // Every cell still agrees with core, which is the claim the three assertions above are
        // instances of.
        foreach (TreeNodeButton cell in ShownCells())
        {
            Assert.That(cell.State, Is.EqualTo(Expected(cell.Node)), $"'{cell.Node}'.");
        }
    }

    // ---- Which door (rule 5) --------------------------------------------------------------------

    [Test]
    public void Close_FromEitherDoorRunsTheRightCallback()
    {
        StartRun(level: 3, pending: 1);
        BuildScreen();

        int first = 0;
        int second = 0;

        _presenter.Open(() => first++);
        _presenter.Close();

        Assert.That(first, Is.EqualTo(1), "The first door's callback never ran.");
        Assert.That(second, Is.Zero, "The second door's callback ran for the first door's open.");

        _presenter.Open(() => second++);
        _presenter.Close();

        Assert.That(second, Is.EqualTo(1), "The second door's callback never ran.");

        // **And the first door's is not run twice**, which is what the callback being cleared before
        // it is invoked buys: a door that came back once must not come back again on the next open.
        Assert.That(first, Is.EqualTo(1), "The previous door's callback ran again.");

        // A Close with nothing open runs nothing at all, so a door closing a screen that is already
        // down cannot restore itself twice.
        _presenter.Close();

        Assert.That(first, Is.EqualTo(1));
        Assert.That(second, Is.EqualTo(1));

        // **And a second Open is refused rather than re-pointed**, because replacing a live callback
        // would strand whichever door is currently waiting on it.
        int third = 0;

        _presenter.Open(() => first++);
        _presenter.Open(() => third++);
        _presenter.Close();

        Assert.That(third, Is.Zero, "A second Open replaced the callback of the door that is waiting.");
        Assert.That(first, Is.EqualTo(2));
    }

    [Test]
    public void Close_GivesTheDoorBackEvenWithNoCallback()
    {
        // **M3-09b's poll, kept as the net under M3-09d's callback** (rule 5). A screen destroyed,
        // never dressed, or closed by something the door did not ask must give the panel and the
        // cards back anyway — a callback on its own leaves dead buttons over a stopped game.
        StartRun(level: 3, pending: 1);
        BuildScreen();
        BuildPauseScreen();
        BuildLevelUpScreen();

        TapIcon();
        TapViewTreeOnPause();

        Assert.That(PauseButton("_resume").interactable, Is.False, "The fixture's premise.");

        // Closed by something the panel never asked, and without the callback running at all.
        SetPrivate(_presenter, "_onClosed", null);
        _presenter.Close();

        PassAPauseFrame();

        Assert.That(
            PauseButton("_resume").interactable,
            Is.True,
            "The panel never came back from a tree that closed without telling it.");

        // And the same from the other door, where the poll has more to give back than a bool.
        _session.OpenLevelUp();
        PassALevelUpFrame();
        TapViewTreeOnLevelUp();

        Assert.That(_levelUpPresenter.IsShown, Is.False, "The fixture's premise: the cards are veiled.");

        SetPrivate(_presenter, "_onClosed", null);
        _presenter.Close();

        PassALevelUpFrame();

        Assert.That(
            _levelUpPresenter.IsShown,
            Is.True,
            "The cards never came back from a tree that closed without telling them, which is a "
                + "stopped game showing nothing at all.");
    }

    // ---- The asset (Traps §5) -------------------------------------------------------------------

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        // Traps §5: a MonoBehaviour declared with a file-scoped namespace compiles and is never
        // linked to a MonoScript, so the component would come back null here with nothing reporting
        // an error anywhere. GetComponent answering is half of what this row proves.
        var presenter = prefab.GetComponent<TreeViewPresenter>();

        Assert.That(
            presenter,
            Is.Not.Null,
            "TreeViewPresenter did not load off its own prefab — the usual cause is a file-scoped "
                + "namespace on a UnityEngine.Object type (Traps §5).");

        // **Read back off the saved asset**, not off an object this fixture just dressed. A prefab
        // whose references never bound looks identical in memory to one that did, right up until it
        // is loaded in a build.
        var so = new SerializedObject(presenter);

        Assert.That(so.FindProperty("_root").objectReferenceValue, Is.Not.Null, "no CanvasGroup root.");
        Assert.That(so.FindProperty("_emptyLabel").objectReferenceValue, Is.Not.Null, "no empty label.");
        Assert.That(so.FindProperty("_close").objectReferenceValue, Is.Not.Null, "no Close button.");

        var template = so.FindProperty("_cellTemplate").objectReferenceValue as TreeNodeButton;

        Assert.That(template, Is.Not.Null, "no cell template.");

        Assert.That(
            template.gameObject.activeSelf,
            Is.False,
            "The cell template ships active, so an unopened screen shows one cell of nothing.");

        var cellSo = new SerializedObject(template);

        foreach (string field in new[] { "_tile", "_icon", "_badge", "_numeral", "_button", "_takenTile", "_availableTile", "_lockedTile", "_checkBadge", "_plusBadge", "_lockBadge" })
        {
            Assert.That(
                cellSo.FindProperty(field).objectReferenceValue,
                Is.Not.Null,
                $"the cell template has no {field}.");
        }

        foreach (string array in new[] { "_branchColumns", "_branchLabels" })
        {
            SerializedProperty property = so.FindProperty(array);

            Assert.That(property, Is.Not.Null, $"TreeView.prefab carries no {array} field.");

            Assert.That(
                property.arraySize,
                Is.EqualTo(SkillTreeSpec.BranchCount),
                $"{array} is not CH §5's three.");

            for (int b = 0; b < property.arraySize; b++)
            {
                Assert.That(
                    property.GetArrayElementAtIndex(b).objectReferenceValue,
                    Is.Not.Null,
                    $"{array}[{b}] is not assigned, so a third of the tree is never drawn.");
            }
        }

        // **Its own canvas, above every screen it can be opened from**, which is the rule rather
        // than this screen's number: HUD 0, hint 40, pause 50, Skills 60, level-up 100, this 110.
        // Sitting below the level-up's would have worked — that screen drops its alpha *and* its
        // raycasts before opening this — but that is a promise made in another file about its
        // runtime state, where a sorting order is a fact about this asset.
        var canvas = prefab.GetComponent<Canvas>();

        Assert.That(canvas, Is.Not.Null, "the screen is not its own canvas.");

        foreach (string below in new[] { HudPrefabPath, PausePrefabPath, SkillsPrefabPath, LevelUpPrefabPath })
        {
            var other = AssetDatabase.LoadAssetAtPath<GameObject>(below);

            Assert.That(
                canvas.sortingOrder,
                Is.GreaterThan(other.GetComponentInChildren<Canvas>(true).sortingOrder),
                $"the tree view sorts at or below {below}, which is a screen it opens over.");
        }

        // A full-screen content root that follows the safe area on a notched landscape phone —
        // rule 9's grid has to fit inside it, and ledger row 4 is whether it does.
        Assert.That(
            prefab.GetComponentInChildren<SafeAreaFitter>(true),
            Is.Not.Null,
            "no SafeAreaFitter on the content root, so a column can land under a notch.");

        // Close is a button, and its label a key (Android Back is the other way out, UI-03c rule 5).
        var close = (Button)so.FindProperty("_close").objectReferenceValue;

        Assert.That(
            close.GetComponentInChildren<TMP_Text>(true).text,
            Does.StartWith("ui.tree."),
            "Close's label is not a LocKey, so this screen is a place raw English is typed into a "
                + "prefab (ledger row 9).");
    }

    // ---- The Close label is a key, and the table answers it (M3-14c, rules 1, 3, 8) --------------

    [Test]
    public void Start_WritesTheCloseLabel()
    {
        StartRun();
        BuildScreen(Shipped());

        RunStart();

        // **The most visible of M3-14b's nine.** This button sits directly under twelve nodes that
        // have read English since M3-14a, so until now the screen shipped a paragraph of words above
        // a raw key — which is the one PROGRESS names as the thing a playtest should not have to
        // find. Pinned against the shipped English.asset so inverting the word is a red row.
        Assert.That(Field<TMP_Text>(_presenter, "_closeLabel").text, Is.EqualTo("Close"));
    }

    [Test]
    public void Start_NoCloseLabelDressed_IsSilent()
    {
        StartRun();
        BuildScreen(Shipped());

        SetPrivate(_presenter, "_closeLabel", null);

        // Softer than the MissingReferenceException Start throws when the Close *button* is absent,
        // and the split is the point: a screen with no way out is indistinguishable from a crash,
        // where a way out with no word on it is still a way out (M3-14c rule 5).
        Assert.That(() => RunStart(), Throws.Nothing);
    }

    // ---- Guard rows ------------------------------------------------------------------------------

    [Test]
    public void Construct_RefusesNullDependencies()
    {
        StartRun();
        BuildScreen();

        SkillIconBook icons = NoIcons();
        CharacterLookBook looks = NoLooks();

        Assert.That(() => _presenter.Construct(null, _session, NewPause(), _catalog, Passthrough(), icons, looks), Throws.ArgumentNullException);
        Assert.That(() => _presenter.Construct(_session, _session, NewPause(), null, Passthrough(), icons, looks), Throws.ArgumentNullException);
        Assert.That(() => _presenter.Construct(_session, _session, NewPause(), _catalog, null, icons, looks), Throws.ArgumentNullException);
        Assert.That(() => _presenter.Construct(_session, _session, NewPause(), _catalog, Passthrough(), null, looks), Throws.ArgumentNullException);
        Assert.That(() => _presenter.Construct(_session, _session, NewPause(), _catalog, Passthrough(), icons, null), Throws.ArgumentNullException);
    }

    [Test]
    public void Show_RefusesNothingToDrawAndNoStateToDrawItIn()
    {
        StartRun();
        BuildScreen();

        TreeNodeButton cell = _cellTemplateOf();

        Assert.That(
            () => cell.Show(null, NodeState.Locked, null, 1),
            Throws.ArgumentNullException,
            "A cell with no node to draw is a presenter that walked past the end of the tree.");

        // **Loud rather than silent**, and the reason is that M6-02 is already named as the task
        // that adds a fourth member: a new state with no colour would draw as Locked, which is a
        // node the player owns reading as one they cannot reach (PlayerStats.Resolve's rule).
        Assert.That(
            () => cell.Show(_catalog.Skill(new ContentId(ConsecrateKey)), (NodeState)7, null, 1),
            Throws.InstanceOf<ArgumentOutOfRangeException>(),
            "A node state with no frame colour was absorbed rather than reported.");
    }

    [Test]
    public void Layout_IgnoresANonFiniteDpField()
    {
        StartRun();
        BuildScreen();

        _presenter.Open(null);

        Vector2[] wanted = Rects();

        // **The two float doors on this screen, and they fall back rather than leaving the layout
        // alone — the opposite of PausePresenter.Place, on purpose.** There is no authored layout to
        // fall back to here: every cell is a clone that starts life on the template's rect, so
        // "leave it alone" is twenty-seven cells stacked on top of each other.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -12f })
        {
            SetPrivate(_presenter, "_cellHeightDp", bad);

            Reopen();

            Assert.That(
                Rects(),
                Is.EqualTo(wanted),
                $"A cell height of {bad} was written through rather than falling back to the "
                    + "default. A NaN reaching sizeDelta is a RectTransform that never renders "
                    + "again, which here is a tree that exists and cannot be seen.");
        }

        SetPrivate(_presenter, "_cellHeightDp", CellDp);
        SetPrivate(_presenter, "_cellGapDp", 0f);

        Reopen();

        Vector2[] noGap = Rects();

        // **The gap falls back to zero and the height does not, and that is per field rather than
        // per class.** A cell height has no honest zero; a gap does — cells touching is a layout.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -3f })
        {
            SetPrivate(_presenter, "_cellGapDp", bad);

            Reopen();

            Assert.That(Rects(), Is.EqualTo(noGap), $"A cell gap of {bad} was written through.");
        }

        foreach (Vector2 size in Rects())
        {
            Assert.That(float.IsFinite(size.x) && float.IsFinite(size.y), Is.True);
        }
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>
    /// Starts a resumed run over CH §5's twenty-seven nodes, carrying whatever a row needs it to
    /// have already spent.
    /// </summary>
    /// <remarks>
    /// Resumed rather than fresh for <c>LevelUpPresenterTests.StartRun</c>'s reason: a fresh run is
    /// level 1 with nothing owed, and the only way to a pick from there is killing enough of a stage
    /// to cross a threshold — which would make every row here a stage test.
    /// <paramref name="level"/> has to account for what is taken and what is owed, or M3-08a rule
    /// 9's identity refuses the run.
    /// </remarks>
    private void StartRun(
        int level = 1,
        int pending = 0,
        string[] taken = null,
        bool withTree = true,
        bool full = true,
        string[] banished = null)
    {
        _tree = full ? FullTree() : PartialTree();

        IReadOnlyList<SkillSpec> skills = full ? FullSkills() : PartialSkills();

        _catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Husk() },
            new[] { Mode() },
            withTree ? skills : Array.Empty<SkillSpec>(),
            withTree ? new[] { _tree } : Array.Empty<SkillTreeSpec>());

        // The hub is the session's own IDomainEvents, so the screens hear what core published
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

        ContentId[] takenIds = (taken ?? Array.Empty<string>())
            .Select(id => new ContentId(id))
            .ToArray();

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
            level,
            0f,
            pending,
            takenIds,
            new ContentId[SkillRunner.MaxManualSlots],
            default,
            (banished ?? Array.Empty<string>()).Select(id => new ContentId(id)).ToArray(),
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
    /// Instantiates the shipped prefab and injects it — the screen under test is the asset.
    /// </summary>
    /// <param name="localizer">
    /// What the cells and the three headings resolve through. Defaults to an <em>empty</em> real
    /// <c>TableLocalizer</c>, which answers every key with itself.
    /// </param>
    private void BuildScreen(ILocalizer localizer = null)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        _screen = Object.Instantiate(prefab);
        _spawned.Add(_screen);

        _presenter = _screen.GetComponent<TreeViewPresenter>();

        Assert.That(
            _presenter,
            Is.Not.Null,
            "TreeViewPresenter did not load off its own prefab (Traps §5).");

        // What RunScope's RegisterComponent does. Start never runs in EditMode, so nothing else on
        // this object has fired.
        _presenter.Construct(_session, _session, NewPause(), _catalog, localizer ?? Passthrough(), NoIcons(), NoLooks());
    }

    /// <summary>The real adapter over an empty table: every key resolves to itself.</summary>
    private static ILocalizer Passthrough() =>
        new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>());

    /// <summary>
    /// The real adapter over the <em>shipped</em> <c>English.asset</c> (M3-14c rule 8).
    /// </summary>
    /// <remarks>
    /// The shipped table rather than a fixture one, on the owner's standard for this task: a row
    /// that pins <em>"Close"</em> against a table this file wrote would go green over an
    /// <c>English.asset</c> that said anything at all.
    /// </remarks>
    private static ILocalizer Shipped()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No localization table at {EnglishPath}.");

        return new TableLocalizer(table);
    }

    /// <summary>The presenter's own <c>Start</c>, which Unity never runs in EditMode.</summary>
    private void RunStart() => Invoke(_presenter, "Start");

    /// <summary>The pause panel, with the cross-prefab reference Run.unity dresses.</summary>
    private void BuildPauseScreen()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PausePrefabPath);

        _pauseScreen = Object.Instantiate(prefab);
        _spawned.Add(_pauseScreen);

        _pausePresenter = _pauseScreen.GetComponent<PausePresenter>();

        _pausePresenter.Construct(_session, NewPause(), _hub, new SceneLoader(), Passthrough());

        // Scene dressing rather than prefab dressing: the two screens are separate root prefabs, so
        // nothing on either asset can carry it (M3-09b's decision, for its reason).
        SetPrivate(_pausePresenter, "_treeScreen", _presenter);
    }

    /// <summary>The level-up screen, with the same cross-prefab reference.</summary>
    private void BuildLevelUpScreen()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LevelUpPrefabPath);

        _levelUpScreen = Object.Instantiate(prefab);
        _spawned.Add(_levelUpScreen);

        _levelUpPresenter = _levelUpScreen.GetComponent<LevelUpPresenter>();

        _levelUpPresenter.Construct(_session, _session, _hub, _catalog, Passthrough());

        SetPrivate(_levelUpPresenter, "_treeScreen", _presenter);
    }

    /// <summary>
    /// The run's one gate, built on first ask: the tree screen and the pause panel share it, as they
    /// do in <c>RunScope</c> (UI-03c).
    /// </summary>
    private RunPause NewPause()
    {
        if (_pauses.Count > 0)
        {
            return _pauses[0];
        }

        var pause = new RunPause();

        _pauses.Add(pause);

        return pause;
    }

    /// <summary>The pause this fixture built, for the rows that assert the gate did not move.</summary>
    private RunPause Pause() => _pauses[0];

    private void TapIcon() => PauseButton("_icon").onClick.Invoke();

    private void TapViewTreeOnPause() => PauseButton("_viewTree").onClick.Invoke();

    private void TapViewTreeOnLevelUp() =>
        Field<Button>(_levelUpPresenter, "_viewTree").onClick.Invoke();

    private void TapClose() => Field<Button>(_presenter, "_close").onClick.Invoke();

    private void TapCard(int index) =>
        Field<Button>(Cards()[index], "_button").onClick.Invoke();

    /// <summary>One frame's worth of this screen's per-frame work, which is none (rule 10).</summary>
    private void PassAFrame()
    {
        MethodInfo update = typeof(TreeViewPresenter)
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

        update?.Invoke(_presenter, null);
    }

    private void PassAPauseFrame() => Invoke(_pausePresenter, "Update");

    private void PassALevelUpFrame() => Invoke(_levelUpPresenter, "Update");

    /// <summary>Closes and reopens, which is the public way to re-run the layout.</summary>
    private void Reopen()
    {
        _presenter.Close();
        _presenter.Open(null);
    }

    private Button PauseButton(string field) => Field<Button>(_pausePresenter, field);

    private TMP_Text EmptyLabel() => Field<TMP_Text>(_presenter, "_emptyLabel");

    private CanvasGroup LevelUpRoot() => Field<CanvasGroup>(_levelUpPresenter, "_root");

    private OfferCard[] Cards() => Field<OfferCard[]>(_levelUpPresenter, "_cards");

    /// <summary>What each drawn card is showing — its node's <c>NameKey</c>, which is its identity.</summary>
    private string[] CardKeys() =>
        Cards().Where(c => c.IsShown).Select(c => Text(c, "_name")).ToArray();

    private bool[] CardInteractables() =>
        Cards().Select(c => Field<Button>(c, "_button").interactable).ToArray();

    private TreeNodeButton _cellTemplateOf() => Field<TreeNodeButton>(_presenter, "_cellTemplate");

    private TreeNodeButton[] ShownCells() =>
        Field<List<TreeNodeButton>>(_presenter, "_cells").Where(c => c.IsShown).ToArray();

    private TreeNodeButton[] CellsIn(int branch) =>
        Field<RectTransform[]>(_presenter, "_branchColumns")[branch]
            .GetComponentsInChildren<TreeNodeButton>(true)
            .Where(c => c.IsShown)
            .ToArray();

    private TreeNodeButton CellFor(ContentId id)
    {
        TreeNodeButton cell = ShownCells().FirstOrDefault(c => c.Node == id);

        Assert.That(cell, Is.Not.Null, $"'{id}' is not on the screen at all.");

        return cell;
    }

    private TreeNodeButton CellFor(string id) => CellFor(new ContentId(id));

    private Vector2[] Rects() =>
        ShownCells().Select(c => ((RectTransform)c.transform).sizeDelta).ToArray();

    private Sprite TileOf(string id) => Field<Image>(CellFor(id), "_tile").sprite;

    private static SkillIconBook NoIcons() => new SkillIconBook(new Dictionary<ContentId, Sprite>());

    private static CharacterLookBook NoLooks() => new CharacterLookBook(new Dictionary<ContentId, CharacterLook>());

    /// <summary>What the run says this node is — the four answers, from core.</summary>
    private NodeState Expected(ContentId id)
    {
        if (_session.State.TakenNodeIds.Contains(id))
        {
            return NodeState.Taken;
        }

        if (_session.State.BanishedNodeIds.Contains(id))
        {
            return NodeState.Banished;
        }

        return _session.State.IsNodeAvailable(id) ? NodeState.Available : NodeState.Locked;
    }

    /// <summary>How many of the tree's nodes core says may be taken right now.</summary>
    private int AvailableCount()
    {
        int count = 0;

        foreach (SkillBranchSpec branch in _tree.Branches)
        {
            for (int t = 1; t <= branch.TierCount; t++)
            {
                foreach (ContentId id in branch.Tier(t))
                {
                    if (_session.State.IsNodeAvailable(id))
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    private static string Node(char branch, int tier, char slot) => $"skill.test.{branch}{tier}{slot}";

    private static string Keystone(char branch) => $"skill.test.{branch}k";

    /// <summary>
    /// CH §5's tree: three branches of two nodes a tier for four tiers, plus a keystone. Branch a
    /// carries one of each of CH §4's four kinds, which is as many constraints as a legal tree can
    /// hold at once.
    /// </summary>
    private static SkillTreeSpec FullTree() => new SkillTreeSpec(
        new ContentId(TreeId),
        new ContentId(OathboundId),
        new[] { FullBranch('a'), FullBranch('b'), FullBranch('c') });

    private static SkillBranchSpec FullBranch(char letter)
    {
        var tiers = new List<IReadOnlyList<ContentId>>();

        for (int tier = 1; tier <= 4; tier++)
        {
            tiers.Add(new[]
            {
                new ContentId(letter == 'a' && tier == 1 ? ConsecrateKey : Node(letter, tier, 'a')),
                new ContentId(Node(letter, tier, 'b')),
            });
        }

        // A Keystone is the sole node of its branch's last tier, or TreeRules refuses the tree
        // (CH §5) — which is the one constraint a 27-node fixture cannot get away with ignoring.
        tiers.Add(new[] { new ContentId(Keystone(letter)) });

        return new SkillBranchSpec(new LocKey($"branch.{letter}"), tiers);
    }

    private static IReadOnlyList<SkillSpec> FullSkills()
    {
        var skills = new List<SkillSpec>();

        foreach (char letter in new[] { 'a', 'b', 'c' })
        {
            for (int tier = 1; tier <= 4; tier++)
            {
                if (letter == 'a' && tier == 1)
                {
                    // Ledger row 9's row draws this one, so its NameKey is the exact string a
                    // player would read rather than the fixture's usual "<id>.name".
                    skills.Add(new SkillSpec(
                        new ContentId(ConsecrateKey),
                        new LocKey(ConsecrateKey),
                        new LocKey($"{ConsecrateKey}.desc"),
                        SkillKind.Active,
                        Array.Empty<IEffect>(),
                        Active()));
                }
                else
                {
                    skills.Add(Passive(Node(letter, tier, 'a')));
                }

                // The Upgrade sits at branch a tier 2 and names the Active below it: a parent has to
                // be in the same branch at a lower tier or TreeRules refuses the tree.
                skills.Add(letter == 'a' && tier == 2
                    ? Upgrade(Node(letter, tier, 'b'), ConsecrateKey)
                    : Passive(Node(letter, tier, 'b')));
            }

            skills.Add(new SkillSpec(
                new ContentId(Keystone(letter)),
                new LocKey($"{Keystone(letter)}.name"),
                new LocKey($"{Keystone(letter)}.desc"),
                SkillKind.Keystone,
                new IEffect[] { Damage(0.2f) }));
        }

        return skills;
    }

    /// <summary>M3-12's v1: three branches of two tiers of two, and no keystone anywhere.</summary>
    private static SkillTreeSpec PartialTree() => new SkillTreeSpec(
        new ContentId(TreeId),
        new ContentId(OathboundId),
        new[] { PartialBranch('a'), PartialBranch('b'), PartialBranch('c') });

    private static SkillBranchSpec PartialBranch(char letter) => new SkillBranchSpec(
        new LocKey($"branch.{letter}"),
        new IReadOnlyList<ContentId>[]
        {
            new[] { new ContentId(Node(letter, 1, 'a')), new ContentId(Node(letter, 1, 'b')) },
            new[] { new ContentId(Node(letter, 2, 'a')), new ContentId(Node(letter, 2, 'b')) },
        });

    private static IReadOnlyList<SkillSpec> PartialSkills()
    {
        var skills = new List<SkillSpec>();

        foreach (char letter in new[] { 'a', 'b', 'c' })
        {
            for (int tier = 1; tier <= 2; tier++)
            {
                skills.Add(Passive(Node(letter, tier, 'a')));
                skills.Add(Passive(Node(letter, tier, 'b')));
            }
        }

        return skills;
    }

    private static SkillSpec Passive(string id) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Passive,
        new IEffect[] { Damage(0.05f) });

    private static SkillSpec Upgrade(string id, string parentId) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Upgrade,
        new IEffect[] { Damage(0.1f) },
        active: null,
        parentId: new ContentId(parentId));

    /// <summary>
    /// The one Active in the tree. Its condition holds on every tick of a healthy run and a hurt
    /// one, which is <c>LevelUpPresenterTests</c>' trick for a trigger that reads as "always".
    /// </summary>
    private static ActiveSpec Active() => new ActiveSpec(
        6f,
        new TriggerSpec(new[]
        {
            new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 1.5f),
        }),
        new IEffect[] { Damage(0.1f) });

    private static ModifyStat Damage(float percent) =>
        new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, percent);

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

            // CH §5.2's shipped levelling curve, ToReach(N) = 20 + 12·N^1.4, written out rather
            // than taken from Tests.Core's `Scalings` — that helper is `internal` and this fixture
            // is in another assembly.
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

    private static T Field<T>(object target, string name) =>
        (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);

    private static void SetPrivate(object target, string name, object value) =>
        target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);

    private static void Invoke(object target, string name) =>
        target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);

    private static TMP_Text Label(object target, string field) => Field<TMP_Text>(target, field);

    private static string Text(object target, string field) => Label(target, field).text;

    /// <summary>
    /// Publishes into the run's hub <em>and</em> into a recorder, so a row can both watch the screen
    /// react and count what core said. <c>LevelUpPresenterTests</c>' fake, for its reason.
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
