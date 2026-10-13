using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// UI-03d: the Jungle plays the free-pick flow, Descent keeps the random three, and the pause
/// menu's View Tree hands its pause to the tree screen and takes it back.
/// </summary>
/// <remarks>
/// The door rows run over a real <c>RunSession</c>, a real <c>RunPause</c> and the shipped
/// <c>Pause.prefab</c> and <c>TreeView.prefab</c>, wired as <c>Run.unity</c> wires them. "No tick in
/// between" is read off <c>Time.timeScale</c> and the holder, which <c>RunPause</c> keeps in step
/// and which the ticker reads: the release and the retake are two calls inside one tap handler, so
/// no frame sits between them.
/// </remarks>
[TestFixture]
public sealed class FreePickModeTests
{
    private const string JunglePath = "Assets/_Project/Data/Modes/Jungle.asset";
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";
    private const string TreePath = "Assets/_Project/Prefabs/UI/TreeView.prefab";
    private const string PausePath = "Assets/_Project/Prefabs/UI/Pause.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";

    private const string ModeId = "mode.test";
    private const string CharacterId = "character.test";
    private const string HuskId = "enemy.husk";
    private const string TreeId = "tree.test";
    private const string ArenaId = "arena.pillars";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private readonly List<GameObject> _spawned = new List<GameObject>();

    private DomainEventHub _hub;
    private RunSession _session;
    private RunPause _pause;
    private SkillTreeSpec _tree;
    private ContentCatalog _catalog;
    private float _restoreTimeScale;

    private TreeViewPresenter _screen;
    private PausePresenter _menu;

    [SetUp]
    public void CreateWorld()
    {
        _restoreTimeScale = Time.timeScale;

        _hub = new DomainEventHub();
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

    // ---- Rule 1: the shipped flags ------------------------------------------------------------------

    [Test]
    public void Jungle_HasFreePickOn()
    {
        var jungle = AssetDatabase.LoadAssetAtPath<ModeDefinition>(JunglePath);

        Assert.That(jungle, Is.Not.Null, $"No ModeDefinition at {JunglePath}.");
        Assert.That(jungle.ToSpec().HasFreePick, Is.True);
    }

    [Test]
    public void Descent_HasFreePickOff()
    {
        var descent = AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath);

        Assert.That(descent, Is.Not.Null, $"No ModeDefinition at {DescentPath}.");
        Assert.That(descent.ToSpec().HasFreePick, Is.False);
    }

    // ---- Rules 2-4: the pause menu's door -----------------------------------------------------------

    [Test]
    public void ViewTree_Free_HandsOverThePause()
    {
        StartRun(free: true, pending: 1);
        Build();
        OpenMenu();

        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Menu));

        TapViewTree();

        Assert.That(_screen.IsOpen, Is.True);
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Tree), "the menu still held, or nothing did.");
        Assert.That(Time.timeScale, Is.Zero, "the run was released between the two.");
        Assert.That(Field<Button>(_menu, "_resume").interactable, Is.False, "the menu is still live under the tree.");
    }

    [Test]
    public void Close_FromMenu_ReturnsToMenu()
    {
        StartRun(free: true, pending: 1);
        Build();
        OpenMenu();
        TapViewTree();

        _screen.Close();

        Assert.That(_screen.IsOpen, Is.False);
        Assert.That(_menu.IsOpen, Is.True);
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Menu), "the menu did not take its pause back.");
        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(Field<Button>(_menu, "_resume").interactable, Is.True, "the menu came back inert.");
    }

    [Test]
    public void Close_FromStar_ReturnsToPlay()
    {
        StartRun(free: true, pending: 1);
        Build();

        _screen.Open();

        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Tree));

        _screen.Close();

        Assert.That(_pause.IsPaused, Is.False, "closing the star's screen left the run stopped.");
        Assert.That(_menu.IsOpen, Is.False);
    }

    [Test]
    public void ViewTree_ShowsPoints_HidesAtZero()
    {
        StartRun(free: true, pending: 2);
        Build();
        OpenMenu();
        Refresh();

        TMP_Text label = Field<TMP_Text>(_menu, "_viewTreeLabel");

        Assert.That(label.text, Is.EqualTo("View Tree (2)"));
        Assert.That(label.color, Is.EqualTo(Palette.Available));

        TapViewTree();

        _session.TakeNode(_tree.Branches[0].Tier(1)[0]);
        _session.TakeNode(_tree.Branches[1].Tier(1)[0]);

        _screen.Close();
        Refresh();

        Assert.That(_session.State.SkillPoints, Is.Zero);
        Assert.That(label.text, Is.EqualTo("View Tree"), "the count stayed at 0.");
        Assert.That(label.color, Is.Not.EqualTo(Palette.Available));
    }

    [Test]
    public void ViewTree_NotFree_Unchanged()
    {
        StartRun(free: false, pending: 0);
        Build();
        OpenMenu();

        TapViewTree();

        Assert.That(_screen.IsOpen, Is.True);
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Menu), "a drawing run's tree took its own pause.");

        _screen.Close();

        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Menu));
        Assert.That(Field<TMP_Text>(_menu, "_viewTreeLabel").text, Does.Not.Contain("("), "a count on a drawing run.");
    }

    // ---- Fixture ------------------------------------------------------------------------------------

    private void StartRun(
        bool free,
        int pending,
        ContentId[] taken = null,
        bool withTree = true)
    {
        _tree = Ranger();
        SkillTreeSpec tree = _tree;

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


    private void Build()
    {
        ILocalizer localizer = Shipped();

        GameObject tree = Spawn(TreePath);

        _screen = tree.GetComponent<TreeViewPresenter>();
        _screen.Construct(_session, _session, _pause, _catalog, localizer);

        GameObject pause = Spawn(PausePath);

        _menu = pause.GetComponent<PausePresenter>();
        _menu.Construct(_session, _pause, _hub, new RecordingLoader(), localizer);

        SetField(_menu, "_treeScreen", _screen);
    }

    private GameObject Spawn(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {path}.");

        GameObject root = Object.Instantiate(prefab);

        _spawned.Add(root);
        root.GetComponent<Canvas>().scaleFactor = 1f;

        return root;
    }

    private static ILocalizer Shipped()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No localization table at {EnglishPath}.");

        return new TableLocalizer(table);
    }

    private void OpenMenu()
    {
        _menu.Open();

        Assert.That(_menu.IsOpen, Is.True, "the menu did not open.");
    }

    private void TapViewTree() => Field<Button>(_menu, "_viewTree").onClick.Invoke();

    /// <summary>One frame of the panel's own poll, which EditMode does not run.</summary>
    private void Refresh() => _menu.GetType()
        .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(_menu, null);

    private sealed class RecordingLoader : SceneLoader
    {
        public override Task LoadAsync(string sceneName) => Task.CompletedTask;
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
