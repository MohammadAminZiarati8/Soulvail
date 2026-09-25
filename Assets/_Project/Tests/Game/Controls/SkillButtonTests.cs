using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Controls;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Controls;

/// <summary>
/// RS-03a rule 9: a class with no movement skill has no button. The rest of RS-03a is core's and is
/// in <c>HoldFireTests</c> and <c>MovementSkillNoneTests</c>.
/// </summary>
/// <remarks>
/// <b>Over the shipped <c>Hud.prefab</c> and a real <c>RunSession</c></b>, <c>VeilrotMeterViewTests</c>'
/// shape and reasons: the button is the one dressed on the HUD, and a component that deserialised as
/// null would be invisible to a fixture that built its own. <c>Start</c> does not run on an
/// instantiated prefab in EditMode, so it is invoked by hand.
/// </remarks>
[TestFixture]
public sealed class SkillButtonTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";

    private const string ModeId = "mode.test";
    private const string RollerId = "character.roller";
    private const string HuskId = "enemy.husk";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    private DomainEventHub _hub;

    [SetUp]
    public void CreateHub()
    {
        _hub = new DomainEventHub();
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
    }

    [Test]
    public void SkillButton_HiddenOnANoneClass()
    {
        (SkillButton button, CanvasGroup group) = Bound(MovementSkillKind.None);

        Assert.That(group.alpha, Is.Zero, "no button for a class with no movement skill.");
        Assert.That(group.blocksRaycasts, Is.False, "and nothing in the way of a thumb.");

        Invoke(button, "Update");

        Assert.That(group.alpha, Is.Zero, "still hidden a frame later.");
        Assert.That(group.blocksRaycasts, Is.False);
    }

    [Test]
    public void SkillButton_ShownOnAClassWithADash()
    {
        // The control: every class that ships keeps its button, live and tappable.
        (_, CanvasGroup group) = Bound(MovementSkillKind.Charge);

        Assert.That(group.alpha, Is.EqualTo(1f));
        Assert.That(group.blocksRaycasts, Is.True);
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    /// <summary>
    /// The HUD's button, injected with a run of a class whose movement skill is
    /// <paramref name="kind"/>, and started — what <c>RunScope</c> and a first frame do.
    /// </summary>
    private (SkillButton Button, CanvasGroup Group) Bound(MovementSkillKind kind)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        GameObject hud = Object.Instantiate(prefab);

        _spawned.Add(hud);

        // Pinned: an EditMode canvas has never been driven by its own CanvasScaler.
        hud.GetComponent<Canvas>().scaleFactor = 1f;

        SkillButton button = hud.GetComponentInChildren<SkillButton>(true);

        Assert.That(button, Is.Not.Null, "SkillButton did not load off Hud.prefab (Traps §5).");

        var group = (CanvasGroup)typeof(SkillButton)
            .GetField("_group", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(button);

        Assert.That(group, Is.Not.Null, "Sanity: the button's group is dressed.");

        button.Construct(Started(kind));

        Invoke(button, "Start");

        return (button, group);
    }

    private RunSession Started(MovementSkillKind kind)
    {
        var random = new FixedRandom(3);
        var catalog = new ContentCatalog(new[] { Roller(kind) }, new[] { Husk() }, new[] { Mode() });

        var session = new RunSession(
            catalog,
            random,
            _hub,
            new RecordingIntents(),
            new RunRecorder(random, new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L)), _hub),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        session.Start(new RunConfig(
            new ContentId(ModeId), new ContentId(RollerId), random.Seed, 1, SpawnPlan.Empty, restore: null));

        return session;
    }

    private static void Invoke(object target, string method) =>
        target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);

    private static CharacterSpec Roller(MovementSkillKind kind) => new(
        new ContentId(RollerId),
        new LocKey("character.roller.name"),
        new LocKey("character.roller.description"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(kind, 6f, 0.3f, 3f, 0.15f, 0f, 0f, 0.05f),
        null,
        0.5f);

    private static EnemySpec Husk() => new(
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
            new[] { new ContentId("arena.pillars") });
    }
}
