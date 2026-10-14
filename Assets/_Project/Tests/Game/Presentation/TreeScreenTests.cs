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
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-03c: the tree screen in a free run — the star opens it over its own pause, the grid grows up
/// from tier 1, and a banked point takes any open node.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over a real <c>RunSession</c>, the shipped <c>TreeView.prefab</c> and the shipped
/// <c>Hud.prefab</c></b> — <c>PointBadgeTests</c>' shape. Points arrive through a resumed snapshot
/// carrying the level, the nodes taken and the points owed, so every row starts where its rule is.
/// </para>
/// <para>
/// <b>On <see cref="InputTestFixture"/></b> for the one row that presses Android Back, which the
/// Input System reports as Escape: the fixture gives each row an isolated input system, so the
/// screen's own Back action cannot hear a key from anywhere else.
/// </para>
/// <para>
/// <c>Awake</c> and <c>Start</c> do not run in EditMode (Traps §5); nothing here needs them.
/// </para>
/// </remarks>
[TestFixture]
public sealed partial class TreeScreenTests : InputTestFixture
{
    private const string TreePath = "Assets/_Project/Prefabs/UI/TreeView.prefab";
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";

    private const string ModeId = "mode.test";
    private const string CharacterId = "character.test";
    private const string HuskId = "enemy.husk";
    private const string TreeId = "tree.test";
    private const string ArenaId = "arena.pillars";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    /// <summary>A phone held upright, in dp — <c>HudLayoutTests.Portrait</c>.</summary>
    private static readonly Vector2 Portrait = new Vector2(411f, 914f);

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private readonly List<GameObject> _spawned = new List<GameObject>();

    private DomainEventHub _hub;
    private readonly Dictionary<ContentId, Sprite> _iconMap = new Dictionary<ContentId, Sprite>();
    private readonly Dictionary<ContentId, CharacterLook> _lookMap = new Dictionary<ContentId, CharacterLook>();
    private RunSession _session;
    private RunPause _pause;
    private SkillTreeSpec _tree;
    private ContentCatalog _catalog;
    private float _restoreTimeScale;

    private TreeViewPresenter _screen;
    private HudPresenter _hud;

    [SetUp]
    public void CreateWorld()
    {
        // RunPause writes timeScale; a row that failed part way would freeze every later one.
        _restoreTimeScale = Time.timeScale;

        _hub = new DomainEventHub();
        _iconMap.Clear();
        _lookMap.Clear();
        _pause = new RunPause();
    }

    [TearDown]
    public void DestroyWorld()
    {
        _pause?.Dispose();
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

    // ---- Rule 1: the star, and the screen's own pause ---------------------------------------------

    [Test]
    public void Star_Free_OpensAndPauses()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        TapStar();

        Assert.That(_screen.IsOpen, Is.True, "the star did not open the tree.");
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Tree));
        Assert.That(Time.timeScale, Is.Zero);
    }

    [Test]
    public void Star_Free_OpensAtZeroPoints()
    {
        StartRun(Ranger(), free: true, pending: 0);
        Build();

        TapStar();

        Assert.That(_screen.IsOpen, Is.True, "rule 1: it opens at 0 points too, to look.");
    }

    [Test]
    public void Star_NotFree_DoesNothing()
    {
        StartRun(Ranger(), free: false, pending: 1);
        Build();

        TapStar();

        Assert.That(_screen.IsOpen, Is.False, "a drawing run's star opened the tree.");
        Assert.That(_pause.IsPaused, Is.False);
    }

    [Test]
    public void Open_PauseHeld_DoesNotOpenOrThrow()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        _pause.Pause(PauseReason.Menu);

        Assert.That(() => _screen.Open(), Throws.Nothing);
        Assert.That(() => TapStar(), Throws.Nothing);

        Assert.That(_screen.IsOpen, Is.False);
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Menu), "the menu's pause was stolen.");
    }

    [Test]
    public void Close_ReleasesTree()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        TapStar();
        TapClose();

        Assert.That(_screen.IsOpen, Is.False);
        Assert.That(_pause.IsPaused, Is.False, "closing the screen kept the run stopped.");
        Assert.That(Time.timeScale, Is.EqualTo(_restoreTimeScale));
    }

    // ---- Rule 2: the grid -------------------------------------------------------------------------

    [Test]
    public void Grid_TierOneAtBottom()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        _screen.Open();

        Assert.That(Shown().Length, Is.EqualTo(9), "the Ranger is a plain 3×3.");

        for (int b = 0; b < SkillTreeSpec.BranchCount; b++)
        {
            float[] heights = Enumerable.Range(1, 3)
                .Select(t => Centre(CellFor(_tree.Branches[b].Tier(t)[0])).y)
                .ToArray();

            Assert.That(heights[0], Is.LessThan(heights[1]), $"branch {b}: tier 1 is not below tier 2.");
            Assert.That(heights[1], Is.LessThan(heights[2]), $"branch {b}: tier 2 is not below tier 3.");
        }

        // A row per tier: every branch's tier t sits on one line, and the columns run left to right.
        for (int t = 1; t <= 3; t++)
        {
            Vector2[] row = _tree.Branches.Select(branch => Centre(CellFor(branch.Tier(t)[0]))).ToArray();

            Assert.That(row[1].y, Is.EqualTo(row[0].y).Within(0.01f), $"tier {t} is not one row.");
            Assert.That(row[2].y, Is.EqualTo(row[0].y).Within(0.01f), $"tier {t} is not one row.");
            Assert.That(row[0].x, Is.LessThan(row[1].x));
            Assert.That(row[1].x, Is.LessThan(row[2].x));
        }

        // The branch's name heads its column.
        TMP_Text[] labels = Field<TMP_Text[]>(_screen, "_branchLabels");

        for (int b = 0; b < SkillTreeSpec.BranchCount; b++)
        {
            Assert.That(labels[b].text, Is.EqualTo(_tree.Branches[b].NameKey.Key));
        }
    }

    [Test]
    public void Grid_TwelveNodeTree_Draws()
    {
        StartRun(Oathbound(), free: true, pending: 1);
        Build();

        _screen.Open();

        TreeNodeButton[] cells = Shown();
        ContentId[] nodes = Nodes(_tree).ToArray();

        Assert.That(cells.Length, Is.EqualTo(12));
        Assert.That(cells.Select(c => c.Node), Is.EquivalentTo(nodes), "every node, one cell each.");

        // A tier's two nodes sit side by side, on one row, without overlapping.
        foreach (SkillBranchSpec branch in _tree.Branches)
        {
            for (int t = 1; t <= branch.TierCount; t++)
            {
                Rect left = WorldRect(CellFor(branch.Tier(t)[0]));
                Rect right = WorldRect(CellFor(branch.Tier(t)[1]));

                Assert.That(left.center.y, Is.EqualTo(right.center.y).Within(0.01f));
                Assert.That(left.xMax, Is.LessThanOrEqualTo(right.xMin + 0.01f), "a tier's nodes overlap.");
            }
        }
    }

    // ---- Rule 3: state from core ------------------------------------------------------------------

    [Test]
    public void Cells_MatchCore()
    {
        ContentId first = TreeFirst(Ranger());

        StartRun(Ranger(), free: true, pending: 1, taken: new[] { first });
        Build();

        _screen.Open();

        var seen = new HashSet<NodeState>();

        foreach (TreeNodeButton cell in Shown())
        {
            NodeState expected = Expected(cell.Node);

            Assert.That(cell.State, Is.EqualTo(expected), $"'{cell.Node}'.");

            // The plus badge exactly while it can be taken, a point being banked; a lock while locked.
            var badge = Field<Image>(cell, "_badge");

            Assert.That(badge.gameObject.activeSelf && badge.sprite.name == "TreeBadge_Plus", Is.EqualTo(expected == NodeState.Available), $"'{cell.Node}' plus.");
            Assert.That(badge.gameObject.activeSelf && badge.sprite.name == "TreeBadge_Lock", Is.EqualTo(expected == NodeState.Locked), $"'{cell.Node}' lock.");

            seen.Add(expected);
        }

        Assert.That(seen, Is.EquivalentTo(new[] { NodeState.Taken, NodeState.Available, NodeState.Locked }));
        Assert.That(CellFor(first).State, Is.EqualTo(NodeState.Taken));
        Assert.That(Field<Image>(CellFor(first), "_tile").sprite.name, Is.EqualTo("TreeTile_Taken"), "a taken node is not lit.");
        Assert.That(Field<Image>(CellFor(LockedNode()), "_tile").sprite.name, Is.EqualTo("TreeTile_Locked"));
    }

    [Test]
    public void Cells_NoPoint_NoPlus()
    {
        StartRun(Ranger(), free: true, pending: 0);
        Build();

        _screen.Open();

        Assert.That(Shown().Any(c => c.State == NodeState.Available), Is.True, "the fixture's premise.");

        foreach (TreeNodeButton cell in Shown())
        {
            Assert.That(PlusShown(cell), Is.False, $"'{cell.Node}' shows a plus at 0 points.");
            Assert.That(cell.IsTakeable, Is.False);
        }
    }

    // ---- Rules 4, 6: the take ---------------------------------------------------------------------

    [Test]
    public void Learn_AvailableWithPoint_Takes()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        _screen.Open();

        ContentId tier1 = _tree.Branches[0].Tier(1)[0];
        ContentId tier2 = _tree.Branches[0].Tier(2)[0];

        Assert.That(CellFor(tier2).State, Is.EqualTo(NodeState.Locked), "the fixture's premise.");

        Take(CellFor(tier1));

        Assert.That(_session.State.TakenNodeIds, Has.Member(tier1));
        Assert.That(_session.State.SkillPoints, Is.Zero);

        // Redrawn from reads made after TakeNode returned: taken, and the tier it opened shows open.
        Assert.That(CellFor(tier1).State, Is.EqualTo(NodeState.Taken));
        Assert.That(CellFor(tier2).State, Is.EqualTo(NodeState.Available));
        Assert.That(CellFor(tier2).IsTakeable, Is.False, "takeable with no point left.");

        // Rule 6: the last point spent leaves the screen up and the pause held.
        Assert.That(_screen.IsOpen, Is.True, "spending the last point closed the screen.");
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Tree));
    }

    [Test]
    public void Learn_LockedTakenOrNoPoint_SendsNothing()
    {
        ContentId first = TreeFirst(Ranger());

        StartRun(Ranger(), free: true, pending: 1, taken: new[] { first });
        Build();

        _screen.Open();

        int taken = _session.State.TakenNodeCount;

        // Locked and taken: nothing, and no throw.
        Assert.That(() => Take(CellFor(_tree.Branches[0].Tier(3)[0])), Throws.Nothing);
        Assert.That(() => Take(CellFor(first)), Throws.Nothing);

        Assert.That(_session.State.TakenNodeCount, Is.EqualTo(taken));
        Assert.That(_session.State.SkillPoints, Is.EqualTo(1));

        // Spend the point, then tap another open node with none left.
        Take(CellFor(_tree.Branches[1].Tier(1)[0]));

        Assert.That(_session.State.SkillPoints, Is.Zero);

        ContentId open = _tree.Branches[2].Tier(1)[0];

        Assert.That(CellFor(open).State, Is.EqualTo(NodeState.Available));
        Assert.That(() => Take(CellFor(open)), Throws.Nothing);
        Assert.That(_session.State.TakenNodeIds, Has.No.Member(open), "a tap with no point took a node.");
    }

    // ---- Rule 5: the header and Back --------------------------------------------------------------

    [Test]
    public void Header_ShowsPointsLeft()
    {
        StartRun(Ranger(), free: true, pending: 2);
        Build(Shipped());

        _screen.Open();

        TMP_Text points = Field<TMP_Text>(_screen, "_pointsLabel");

        Assert.That(points.gameObject.activeSelf, Is.True);
        Assert.That(points.text, Is.EqualTo("2"));

        Take(CellFor(_tree.Branches[0].Tier(1)[0]));

        Assert.That(points.text, Is.EqualTo("1"), "the count was read before the point was spent.");
        Assert.That(Field<Button>(_screen, "_close"), Is.Not.Null);
    }

    [Test]
    public void Back_Closes()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();

        StartRun(Ranger(), free: true, pending: 1);
        Build();

        _screen.Open();

        Press(keyboard.escapeKey);

        Assert.That(_screen.IsOpen, Is.False, "Android Back left the screen up.");
        Assert.That(_pause.IsPaused, Is.False);
    }

    // ---- Rules 7, 8: not free, no tree ------------------------------------------------------------

    [Test]
    public void NotFree_IsReadOnly()
    {
        StartRun(Ranger(), free: false, pending: 1);
        Build(Shipped());

        // The pause panel's door — the only way into a drawing run's tree.
        _screen.Open(null);

        Assert.That(_screen.IsOpen, Is.True);

        foreach (TreeNodeButton cell in Shown())
        {
            Assert.That(PlusShown(cell), Is.False);

            Take(cell);

            Assert.That(Field<TreeDetailCard>(_screen, "_card").LearnShown, Is.False, "a drawing run offers Learn.");
        }

        Assert.That(_session.State.TakenNodeCount, Is.Zero, "a drawing run's tree sent a command.");
        Assert.That(_session.State.PendingLevelUps, Is.EqualTo(1));
        Assert.That(Field<TMP_Text>(_screen, "_pointsLabel").text, Is.EqualTo("0"), "a drawing run counts points.");
    }

    [Test]
    public void NoTree_LineAndInertStar()
    {
        StartRun(Ranger(), free: true, pending: 1, withTree: false);
        Build();

        TapStar();

        Assert.That(_screen.IsOpen, Is.False, "the star opened a class with no tree.");
        Assert.That(_pause.IsPaused, Is.False);

        _screen.Open(null);

        TMP_Text empty = Field<TMP_Text>(_screen, "_emptyLabel");

        Assert.That(empty.gameObject.activeSelf, Is.True);
        Assert.That(empty.text, Is.EqualTo("ui.tree.none"));
        Assert.That(Shown(), Is.Empty);
    }

    // ---- Rule 9: portrait -------------------------------------------------------------------------

    [Test]
    public void Layout_FitsPortrait()
    {
        foreach (SkillTreeSpec shape in new[] { Ranger(), Oathbound() })
        {
            DestroyWorld();
            CreateWorld();
            StartRun(shape, free: true, pending: 1);
            Build();

            RectTransform safe = Safe();
            float px = StickShaper.PixelsPerDp(Screen.dpi);

            safe.anchorMin = new Vector2(0.5f, 0.5f);
            safe.anchorMax = new Vector2(0.5f, 0.5f);
            safe.pivot = new Vector2(0.5f, 0.5f);
            safe.sizeDelta = Portrait * px;
            safe.anchoredPosition = Vector2.zero;

            _screen.Open();
            Canvas.ForceUpdateCanvases();

            Rect area = WorldRect(safe);
            var header = new[]
            {
                WorldRect((RectTransform)Field<Button>(_screen, "_close").transform),
                WorldRect(Field<TMP_Text>(_screen, "_pointsLabel").rectTransform),
            };

            Assert.That(Shown().Length, Is.EqualTo(Nodes(_tree).Count()));

            foreach (TreeNodeButton cell in Shown())
            {
                Rect r = WorldRect(cell);

                Assert.That(
                    r.xMin >= area.xMin - 0.5f && r.xMax <= area.xMax + 0.5f
                        && r.yMin >= area.yMin - 0.5f && r.yMax <= area.yMax + 0.5f,
                    Is.True,
                    $"'{cell.Node}' {r} leaves the 9:20 safe area {area}.");

                Assert.That(r.width > 0f && r.height > 0f, Is.True, $"'{cell.Node}' has no size.");

                foreach (Rect piece in header)
                {
                    Assert.That(r.Overlaps(piece), Is.False, $"'{cell.Node}' runs under the header.");
                }
            }

            foreach (Rect piece in header)
            {
                Assert.That(piece.xMin >= area.xMin - 0.5f && piece.xMax <= area.xMax + 0.5f, Is.True, "the header leaves the safe area.");
            }

            Assert.That(header[0].Overlaps(header[1]), Is.False, "Close sits on the points.");

            // Sizes are dp, and every word on the screen clears Android's 12 sp floor (M4-07).
            foreach (TMP_Text text in _screen.GetComponentsInChildren<TMP_Text>(false))
            {
                Assert.That(text.fontSize / px, Is.GreaterThanOrEqualTo(12f - 1e-3f), $"{text.name} is under the floor.");
            }
        }
    }

    // ---- Fixture ----------------------------------------------------------------------------------

    private void StartRun(
        SkillTreeSpec tree,
        bool free,
        int pending,
        ContentId[] taken = null,
        bool withTree = true)
    {
        _tree = tree;

        SkillSpec[] skills = Nodes(tree).Select(id => Skill(id.Value)).ToArray();

        _catalog = new ContentCatalog(
            new[] { Character() },
            new[] { Husk() },
            new[] { Mode(free) },
            withTree ? skills : Array.Empty<SkillSpec>(),
            withTree ? new[] { tree } : Array.Empty<SkillTreeSpec>());

        var random = new FixedRandom(7, Alternating(8_192));
        var clock = new FixedClock(Instant);

        _session = new RunSession(
            _catalog,
            random,
            _hub,
            new RecordingIntents(),
            new RunRecorder(random, clock, _hub),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        ContentId[] takenIds = taken ?? Array.Empty<ContentId>();

        // 1 + taken + pending, or the run owes Overflow it never earned (M3-08a rule 9).
        int level = 1 + takenIds.Length + pending;

        _session.Start(new RunConfig(
            new ContentId(ModeId),
            new ContentId(CharacterId),
            random.Seed,
            1,
            SpawnPlan.Empty,
            new RunSnapshot(
                RunSnapshot.CurrentVersion,
                new ContentId(ModeId),
                new ContentId(CharacterId),
                random.Seed,
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
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>())));
    }

    /// <summary>Both shipped prefabs, injected, with the star wired to the screen as Run.unity wires it.</summary>
    private void Build(ILocalizer localizer = null)
    {
        localizer ??= new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>());

        GameObject tree = Spawn(TreePath);

        _screen = tree.GetComponent<TreeViewPresenter>();

        Assert.That(_screen, Is.Not.Null, "TreeViewPresenter did not load off its prefab (Traps §5).");

        _screen.Construct(_session, _session, _pause, _catalog, localizer, new SkillIconBook(_iconMap), new CharacterLookBook(_lookMap));

        GameObject hud = Spawn(HudPath);

        _hud = hud.GetComponent<HudPresenter>();
        _hud.Construct(_hub, _session, localizer);

        SetField(_hud, "_treeScreen", _screen);
    }

    private GameObject Spawn(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {path}.");

        GameObject root = Object.Instantiate(prefab);

        _spawned.Add(root);

        // Pinned, because a Canvas instantiated in EditMode has never been driven by its scaler.
        root.GetComponent<Canvas>().scaleFactor = 1f;

        return root;
    }

    private static ILocalizer Shipped()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No localization table at {EnglishPath}.");

        return new TableLocalizer(table);
    }

    private void TapStar()
    {
        var star = Field<Button>(_hud, "_starButton");

        Assert.That(star, Is.Not.Null, "Hud.prefab's star is not a button.");

        star.onClick.Invoke();
    }

    private void TapClose() => Field<Button>(_screen, "_close").onClick.Invoke();

    private static bool PlusShown(TreeNodeButton cell)
    {
        var badge = Field<Image>(cell, "_badge");

        return badge.gameObject.activeSelf && badge.sprite.name == "TreeBadge_Plus";
    }

    /// <summary>
    /// A tap as the EventSystem would deliver it to an interactable cell; <c>Invoke</c> ignores
    /// <c>interactable</c>, which is what makes the cell's own guard part of what a row checks.
    /// </summary>
    private static void Tap(TreeNodeButton cell) => Field<Button>(cell, "_button").onClick.Invoke();

    /// <summary>Select the cell, then press Learn — what a take is since UI-04d.</summary>
    private void Take(TreeNodeButton cell)
    {
        Tap(cell);
        Field<Button>(Field<TreeDetailCard>(_screen, "_card"), "_learn").onClick.Invoke();
    }

    private RectTransform Safe() =>
        (RectTransform)_screen.GetComponentInChildren<SafeAreaFitter>(true).transform;

    private TreeNodeButton[] Shown() =>
        Field<List<TreeNodeButton>>(_screen, "_cells").Where(c => c.IsShown).ToArray();

    private TreeNodeButton CellFor(ContentId id)
    {
        TreeNodeButton cell = Shown().FirstOrDefault(c => c.Node == id);

        Assert.That(cell, Is.Not.Null, $"'{id}' is not on the screen.");

        return cell;
    }

    private ContentId LockedNode() => _tree.Branches[0].Tier(3)[0];

    private NodeState Expected(ContentId id)
    {
        if (_session.State.TakenNodeIds.Contains(id))
        {
            return NodeState.Taken;
        }

        return _session.State.IsNodeAvailable(id) ? NodeState.Available : NodeState.Locked;
    }

    private static Vector2 Centre(TreeNodeButton cell) => WorldRect(cell).center;

    private static Rect WorldRect(Component piece) => WorldRect((RectTransform)piece.transform);

    private static Rect WorldRect(RectTransform rect)
    {
        var corners = new Vector3[4];

        rect.GetWorldCorners(corners);

        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);

    private static void SetField(object target, string name, object value) =>
        target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);

    // ---- Content ----------------------------------------------------------------------------------

    private static ContentId TreeFirst(SkillTreeSpec tree) => tree.Branches[0].Tier(1)[0];

    private static IEnumerable<ContentId> Nodes(SkillTreeSpec tree)
    {
        foreach (SkillBranchSpec branch in tree.Branches)
        {
            for (int t = 1; t <= branch.TierCount; t++)
            {
                foreach (ContentId id in branch.Tier(t))
                {
                    yield return id;
                }
            }
        }
    }

    /// <summary>The Ranger's shape: three branches of three tiers of one.</summary>
    private static SkillTreeSpec Ranger() => Tree(tiers: 3, perTier: 1);

    /// <summary>The Oathbound's twelve: three branches of two tiers of two.</summary>
    private static SkillTreeSpec Oathbound() => Tree(tiers: 2, perTier: 2);

    private static SkillTreeSpec Tree(int tiers, int perTier) => new SkillTreeSpec(
        new ContentId(TreeId),
        new ContentId(CharacterId),
        "abc".Select(letter => new SkillBranchSpec(
                new LocKey($"branch.{letter}"),
                Enumerable.Range(1, tiers)
                    .Select(t => (IReadOnlyList<ContentId>)Enumerable.Range(0, perTier)
                        .Select(i => new ContentId($"skill.test.{letter}{t}{(char)('a' + i)}"))
                        .ToArray())
                    .ToArray()))
            .ToArray());

    private static SkillSpec Skill(string id) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Passive,
        new IEffect[]
        {
            new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f),
        });

    private static CharacterSpec Character() => new CharacterSpec(
        new ContentId(CharacterId),
        new LocKey("character.test.name"),
        new LocKey("character.test.description"),
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
