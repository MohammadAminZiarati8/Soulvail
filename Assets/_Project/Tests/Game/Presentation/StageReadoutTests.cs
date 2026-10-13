using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-01d: the stage chip and the enemies-left chip, top-right on the shipped HUD.
/// </summary>
/// <remarks>
/// <para>
/// <b>The events are published by hand and the count is set by hand</b>, which is the cost-over-trust
/// statement <c>XpBarViewTests</c> makes: reaching a real stage 3, wave 2 or a real last-five would
/// make every row a stage test. The numbers' own truth is core's — <c>StageArrived</c> and
/// <c>WaveStarted</c> are <c>StageFlowTests</c>' and <c>SpawnDirectorTests</c>', and
/// <c>RunState.EnemiesLeft</c> is UI-01c's — and what is asserted here is the chip's half: that it
/// draws what it is told, and nothing else.
/// </para>
/// <para>
/// <b><c>EnemiesLeft</c> is written through its internal setter by reflection</b>, the one handle a
/// Tests.Game fixture has on a <c>RunState</c> read (no <c>InternalsVisibleTo</c>, AR §18.2). A session
/// that is never ticked leaves the value where the row put it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class StageReadoutTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";
    private const string PausePath = "Assets/_Project/Prefabs/UI/Pause.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";
    private const string FontPath = "Assets/_Project/Art/UI/Fonts/LilitaOne_SDF.asset";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private readonly List<GameObject> _spawned = new List<GameObject>();

    private DomainEventHub _hub;
    private FixedRandom _random;
    private FixedClock _clock;
    private RunSession _session;
    private StageReadout _readout;

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
        _readout = null;
    }

    // ---- The stage chip (rules 1, 2) -------------------------------------------------------------

    [Test]
    public void Stage_ArrivalReadsTheStageAlone()
    {
        Build(stage: 1, Shipped());

        _hub.Publish(new StageArrived(3, default));

        Assert.That(Text("_stageText").text, Is.EqualTo("STAGE 3"));
        Assert.That(Text("_waveText").gameObject.activeSelf, Is.False, "the arrival drew a wave.");

        // Alone, the stage line takes the whole chip.
        var rect = (RectTransform)Text("_stageText").transform;

        Assert.That(rect.anchorMin, Is.EqualTo(Vector2.zero));
        Assert.That(rect.anchorMax, Is.EqualTo(Vector2.one));
    }

    [Test]
    public void Stage_AWaveReadsTwoLines()
    {
        Build(stage: 1, Shipped());

        _hub.Publish(new StageArrived(3, default));
        _hub.Publish(new WaveStarted(3, 2, 3));

        Assert.That(Text("_stageText").text, Is.EqualTo("STAGE 3"));
        Assert.That(Text("_waveText").gameObject.activeSelf, Is.True);
        Assert.That(Text("_waveText").text, Is.EqualTo("Wave 2/3"));
        Assert.That(((RectTransform)Text("_stageText").transform).anchorMin.y, Is.GreaterThan(0.4f), "the stage line is not over the wave.");
    }

    [Test]
    public void Stage_ANewStageForgetsTheWave()
    {
        Build(stage: 1, Shipped());

        _hub.Publish(new WaveStarted(3, 2, 3));
        _hub.Publish(new StageArrived(4, default));

        Assert.That(Text("_stageText").text, Is.EqualTo("STAGE 4"));
        Assert.That(Text("_waveText").gameObject.activeSelf, Is.False, "stage 4 still reads stage 3's wave.");
        Assert.That(_readout.Wave, Is.Zero);
    }

    [Test]
    public void Stage_ABossStageReadsOneOfOne()
    {
        Build(stage: 1, Shipped());

        // What SpawnDirector.TickBoss publishes when the boss stands: one wave of one body.
        _hub.Publish(new WaveStarted(5, 1, 1));

        Assert.That(Text("_waveText").text, Is.EqualTo("Wave 1/1"));
    }

    [Test]
    public void Stage_OpensOnTheRunsStage()
    {
        Build(stage: 7, Shipped());

        // A resume always opens in an arrival, so there is no wave to restore.
        Assert.That(Text("_stageText").text, Is.EqualTo("STAGE 7"));
        Assert.That(Text("_waveText").gameObject.activeSelf, Is.False);
    }

    // ---- The enemies-left chip (rule 3) ----------------------------------------------------------

    [Test]
    public void Enemies_ShownForTheLastFive()
    {
        Build(stage: 1, Shipped());

        GameObject chip = Field<RectTransform>(_readout, "_enemiesChip").gameObject;

        SetLeft(6);
        Frame();
        Assert.That(chip.activeSelf, Is.False, "six is not the last five.");

        SetLeft(5);
        Frame();
        Assert.That(chip.activeSelf, Is.True);
        Assert.That(Text("_enemiesText").text, Is.EqualTo("5"));

        SetLeft(1);
        Frame();
        Assert.That(Text("_enemiesText").text, Is.EqualTo("1"));

        SetLeft(0);
        Frame();
        Assert.That(chip.activeSelf, Is.False, "a cleared stage still shows a skull.");
    }

    [Test]
    public void Enemies_WritesOnlyWhenTheCountMoves()
    {
        Build(stage: 1, Shipped());

        SetLeft(3);
        Frame();

        // A text the chip would overwrite if it wrote on every frame, left as a marker.
        Text("_enemiesText").text = "marker";

        for (int i = 0; i < 10; i++)
        {
            Frame();
        }

        Assert.That(Text("_enemiesText").text, Is.EqualTo("marker"), "an unchanged count was written again.");

        SetLeft(2);
        Frame();

        Assert.That(Text("_enemiesText").text, Is.EqualTo("2"));
    }

    // ---- Words, colours and cost (rules 4, 5) ----------------------------------------------------

    [Test]
    public void Strings_AreKeysAndResolve()
    {
        ILocalizer english = Shipped();

        Assert.That(english.Get(new LocKey("ui.hud.stage")), Is.EqualTo("STAGE {0:0}"));
        Assert.That(english.Get(new LocKey("ui.hud.wave")), Is.EqualTo("Wave {0:0}/{1:0}"));
    }

    [Test]
    public void Strings_AMissingRowDrawsTheKey()
    {
        // A table with no row answers the key, and the chip draws what it is handed — so a missing
        // row reads as a key on screen rather than as nothing.
        Build(stage: 2, new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>()));

        Assert.That(Text("_stageText").text, Is.EqualTo("ui.hud.stage"));
    }

    [Test]
    public void Readout_AllocatesNothingPerFrame()
    {
        Build(stage: 1, Shipped());

        _hub.Publish(new WaveStarted(1, 1, 2));

        MethodInfo update = typeof(StageReadout).GetMethod("Update", Private);
        var frame = (Action)Delegate.CreateDelegate(typeof(Action), _readout, update);

        SetLeftNoAlloc(4);
        frame();

        // **Frames on which the count holds, which is nearly all of them.** A frame that moves the count
        // writes through TMP's float overload, which allocates nothing on a phone and a string in the
        // Editor: SetText ends with `#if UNITY_EDITOR m_text = InternalTextBackingArrayToString()`, so
        // the Inspector can show it (Traps §9). So an Editor row over a changing count measures that
        // line; this one measures the chip, and Enemies_WritesOnlyWhenTheCountMoves pins that the
        // write happens on a change alone.
        AllocationAssert.None(() => frame());
    }

    [Test]
    public void Colours_AreThePalettes()
    {
        Build(stage: 1, Shipped());

        Assert.That(Text("_stageText").color, Is.EqualTo(Palette.HudCaption));
        Assert.That(Text("_waveText").color, Is.EqualTo(Palette.HudText));
        Assert.That(Text("_enemiesText").color, Is.EqualTo(Palette.HudText));
    }

    // ---- Placement (rule 6) ----------------------------------------------------------------------

    [Test]
    public void Chips_ArePlacedInDpFromTheTopRight()
    {
        Build(stage: 1, Shipped());

        float px = StickShaper.PixelsPerDp(Screen.dpi);

        AssertPlaced(Field<RectTransform>(_readout, "_stageChip"), HudLayout.StageChipRightDp, HudLayout.StageChipTopDp,
            HudLayout.StageChipWidthDp, HudLayout.StageChipHeightDp, px);
        AssertPlaced(Field<RectTransform>(_readout, "_enemiesChip"), HudLayout.EnemiesChipRightDp, HudLayout.EnemiesChipTopDp,
            HudLayout.EnemiesChipWidthDp, HudLayout.EnemiesChipHeightDp, px);

        Assert.That(Text("_waveText").fontSize, Is.EqualTo(14f * px).Within(1e-3f), "the wave is not 14 dp.");
        Assert.That(Text("_enemiesText").fontSize, Is.EqualTo(14f * px).Within(1e-3f), "the count is not 14 dp.");
    }

    [Test]
    public void Chips_ClearTheAndroidFloor()
    {
        Build(stage: 1, Shipped());

        _hub.Publish(new WaveStarted(1, 1, 2));

        float px = StickShaper.PixelsPerDp(Screen.dpi);

        // Drawn in dp, so the floor is a dp: M4-07's 12 sp, which on a phone is 12 dp at the default
        // font scale. The caption is the smallest word on the band.
        foreach (string field in new[] { "_stageText", "_waveText", "_enemiesText" })
        {
            Assert.That(Text(field).fontSize / px, Is.GreaterThanOrEqualTo(12f - 1e-3f), $"{field} is under Android's floor.");
        }
    }

    [Test]
    public void Chips_ANonFiniteFieldLeavesTheChipAlone()
    {
        Build(stage: 1, Shipped());

        RectTransform chip = Field<RectTransform>(_readout, "_stageChip");

        chip.anchoredPosition = new Vector2(-3f, -3f);
        chip.sizeDelta = new Vector2(5f, 5f);

        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, 0f, -8f })
        {
            SetField(_readout, "_stageChipSizeDp", new Vector2(bad, 36f));

            Assert.That(() => Invoke(_readout, "Place"), Throws.Nothing, $"Place threw on {bad}.");
            Assert.That(chip.sizeDelta, Is.EqualTo(new Vector2(5f, 5f)), $"size {bad}");
            Assert.That(chip.anchoredPosition, Is.EqualTo(new Vector2(-3f, -3f)), $"size {bad}");
        }
    }

    // ---- The asset (rules 5–7) -------------------------------------------------------------------

    [Test]
    public void Prefab_IsDressed()
    {
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        var readout = hud.GetComponentInChildren<StageReadout>(true);

        Assert.That(readout, Is.Not.Null, "StageReadout did not load off Hud.prefab (Traps §5).");

        var so = new SerializedObject(readout);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        foreach (string field in new[] { "_stageText", "_waveText", "_enemiesText" })
        {
            var text = so.FindProperty(field).objectReferenceValue as TMP_Text;

            Assert.That(text, Is.Not.Null, $"{field} is not wired.");
            Assert.That(text.font, Is.SameAs(font), $"{field} is not in Lilita One.");
            Assert.That(text.text, Does.Not.Contain(" "), $"{field} is authored with a sentence (RunEndPresenterTests' rule).");
        }

        var stageChip = so.FindProperty("_stageChip").objectReferenceValue as RectTransform;
        var enemiesChip = so.FindProperty("_enemiesChip").objectReferenceValue as RectTransform;

        Assert.That(stageChip.GetComponent<Image>().sprite.name, Is.EqualTo("Pill_Dark"));
        Assert.That(stageChip.GetComponent<Image>().type, Is.EqualTo(Image.Type.Sliced));
        Assert.That(enemiesChip.GetComponent<Image>().sprite.name, Is.EqualTo("Chip_Counter"));
        Assert.That(enemiesChip.gameObject.activeSelf, Is.False, "the prefab ships the skull chip up.");
        Assert.That(enemiesChip.GetComponentInChildren<Image>(true), Is.Not.Null);

        var pause = AssetDatabase.LoadAssetAtPath<GameObject>(PausePath);
        var icon = new SerializedObject(pause.GetComponent<PausePresenter>()).FindProperty("_icon").objectReferenceValue as Button;

        Assert.That(icon, Is.Not.Null, "the pause presenter has no icon.");

        var image = icon.GetComponent<Image>();

        Assert.That(image.sprite.name, Is.EqualTo("Button_Pause"));
        Assert.That(image.color, Is.EqualTo(Color.white), "the pause button's art is tinted.");
        Assert.That(icon.GetComponentsInChildren<TMP_Text>(true), Is.Empty, "the old \"II\" glyph is still on the art's own glyph.");
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>The shipped HUD's readout, over a run begun at <paramref name="stage"/>, started.</summary>
    private void Build(int stage, ILocalizer localizer)
    {
        var catalog = new ContentCatalog(new[] { Oathbound() }, new[] { Husk() }, new[] { Mode() });

        _session = new RunSession(
            catalog,
            _random,
            _hub,
            new RecordingIntents(),
            new RunRecorder(_random, _clock, _hub),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        _session.Start(new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            _random.Seed,
            stage,
            SpawnPlan.Empty,
            null));

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        GameObject hud = Object.Instantiate(prefab);

        _spawned.Add(hud);

        hud.GetComponent<Canvas>().scaleFactor = 1f;

        _readout = hud.GetComponentInChildren<StageReadout>(true);

        Assert.That(_readout, Is.Not.Null, "StageReadout did not load off Hud.prefab (Traps §5).");

        _readout.Construct(_hub, _session, localizer);

        Invoke(_readout, "Start");
    }

    private static readonly PropertyInfo EnemiesLeft = typeof(RunState).GetProperty(nameof(RunState.EnemiesLeft));

    private void SetLeft(int left) => EnemiesLeft.SetValue(_session.State, left);

    /// <summary>
    /// <see cref="SetLeft"/> through the setter's own delegate, so the row measures the chip and not a
    /// boxing reflection call. Built once.
    /// </summary>
    private void SetLeftNoAlloc(int left)
    {
        _setter ??= (Action<RunState, int>)Delegate.CreateDelegate(
            typeof(Action<RunState, int>),
            EnemiesLeft.GetSetMethod(nonPublic: true));

        _setter(_session.State, left);
    }

    private Action<RunState, int> _setter;

    private void Frame() => Invoke(_readout, "Update");

    private TMP_Text Text(string field) => Field<TMP_Text>(_readout, field);

    private static void AssertPlaced(RectTransform rect, float rightDp, float topDp, float widthDp, float heightDp, float px)
    {
        Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(1f, 1f)), rect.name);
        Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(1f, 1f)), rect.name);
        Assert.That(rect.pivot, Is.EqualTo(new Vector2(1f, 1f)), rect.name);
        Assert.That(rect.anchoredPosition.x, Is.EqualTo(-rightDp * px).Within(0.5f * px), rect.name);
        Assert.That(rect.anchoredPosition.y, Is.EqualTo(-topDp * px).Within(0.5f * px), rect.name);
        Assert.That(rect.sizeDelta.x, Is.EqualTo(widthDp * px).Within(0.5f * px), rect.name);
        Assert.That(rect.sizeDelta.y, Is.EqualTo(heightDp * px).Within(0.5f * px), rect.name);
    }

    private static ILocalizer Shipped()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No localization table at {EnglishPath}.");

        return new TableLocalizer(table);
    }

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, Private).Invoke(target, null);

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Private).GetValue(target);

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));

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
            new[] { new ArenaEntry(new ContentId("arena.pillars")) });
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
