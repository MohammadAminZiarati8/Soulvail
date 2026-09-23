using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
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
using Soulvail.Tests.Core.Support;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// GD §16.1's two economy readouts — the Veilrot meter down the right edge and the Essence counter
/// in the corner — and the presenter that drives both. M6-03b.
/// </summary>
/// <remarks>
/// <para>
/// <b>The <c>Hud_*</c> rows live here because there is no <c>HudPresenterTests</c></b>: the HUD's
/// rows have always lived with the view they are about (<c>BossBarViewTests</c>,
/// <c>GrantedShieldTests</c>, <c>XpBarViewTests</c>), and the meter is this file's view.
/// </para>
/// <para>
/// <b>Over the shipped <c>Hud.prefab</c> and a real <c>RunSession</c></b> — <c>BossBarViewTests</c>'
/// shape and reasons: the hub is the session's own <c>IDomainEvents</c>, and a component that
/// deserialises as null (Traps §5) is invisible to a fixture that never loads the asset.
/// <c>Awake</c> and <c>Start</c> do not run on an instantiated prefab in EditMode, so <c>Start</c> is
/// invoked by hand where a row needs the placement.
/// </para>
/// </remarks>
[TestFixture]
public sealed class VeilrotMeterViewTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    private static readonly string[] Keys = { "ui.hud.essence", "ui.hud.veilrot", "ui.hud.claimed" };

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private DomainEventHub _hub;
    private FixedRandom _random;
    private FixedClock _clock;
    private RunSession _session;

    private GameObject _hud;
    private HudPresenter _presenter;
    private VeilrotMeterView _meter;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    [SetUp]
    public void CreateWorld()
    {
        _hub = new DomainEventHub();
        _random = new FixedRandom(11, Alternating(8_192));
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
        _presenter = null;
        _meter = null;
    }

    // ---- Rule 1: a view ------------------------------------------------------------------------

    [Test]
    public void Meter_SubscribesToNothing()
    {
        const BindingFlags Everything =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        // HpBarView's and BossBarView's bargain: HudPresenter holds the subscriptions, and a
        // prefab-dressed view that reached for the hub would need injecting on its own.
        Type[] forbidden = { typeof(DomainEventHub), typeof(IDisposable), typeof(IRunSession) };

        foreach (FieldInfo field in typeof(VeilrotMeterView).GetFields(Everything))
        {
            Assert.That(
                forbidden.Any(type => type.IsAssignableFrom(field.FieldType)),
                Is.False,
                $"VeilrotMeterView holds a {field.FieldType.Name}, so it is listening for itself.");
        }

        Assert.That(
            typeof(VeilrotMeterView).GetMethods(Everything)
                .Where(method => method.GetCustomAttribute<InjectAttribute>() is not null),
            Is.Empty,
            "VeilrotMeterView has an [Inject] method, so RunScope would have to register it.");
    }

    // ---- The fill ------------------------------------------------------------------------------

    [Test]
    public void Meter_DrawsTheValue()
    {
        BuildMeter();

        _meter.Set(42f, false);

        Assert.That(_meter.Fill, Is.EqualTo(0.42f).Within(1e-4f));
        Assert.That(Fill().fillAmount, Is.EqualTo(0.42f).Within(1e-4f), "the fill image disagrees.");
    }

    [Test]
    public void Meter_ClampsAtBothEnds()
    {
        BuildMeter();

        Assert.That(() => _meter.Set(-5f, false), Throws.Nothing);
        Assert.That(_meter.Fill, Is.EqualTo(0f));

        Assert.That(() => _meter.Set(140f, false), Throws.Nothing);
        Assert.That(_meter.Fill, Is.EqualTo(1f));
    }

    [Test]
    public void Meter_IgnoresANonFiniteValue()
    {
        BuildMeter();

        _meter.Set(60f, false);

        // HpBarView.Set's bargain: a NaN reaching fillAmount is a graphic that never draws again, and
        // Mathf.Clamp01 does not catch one — every comparison against NaN is false (AR §18.3).
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.That(() => _meter.Set(bad, false), Throws.Nothing, $"{bad} threw.");
            Assert.That(_meter.Fill, Is.EqualTo(0.6f).Within(1e-4f), $"{bad} moved the meter.");
            Assert.That(float.IsFinite(Fill().fillAmount), Is.True, $"{bad} reached fillAmount.");
        }
    }

    // ---- Rule 3: the marks ---------------------------------------------------------------------

    [Test]
    public void Meter_DrawsFourMarks()
    {
        BuildMeter();

        _meter.PlaceThresholds(1f);

        Assert.That(_meter.MarkCount, Is.EqualTo(4));

        float[] expected = { 0.25f, 0.5f, 0.75f, 1f };

        List<Image> marks = LiveMarks();

        Assert.That(marks.Count, Is.EqualTo(4), "the marks on screen disagree with the count.");

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.That(_meter.MarkFraction(i), Is.EqualTo(expected[i]).Within(1e-4f));

            // On the rect as well as the number: "the meter was told" and "the meter drew it there"
            // are two claims, and only the second is what a player sees.
            var rect = (RectTransform)marks[i].transform;

            Assert.That(rect.anchorMin.y, Is.EqualTo(expected[i]).Within(1e-4f));
            Assert.That(rect.anchorMax.y, Is.EqualTo(expected[i]).Within(1e-4f));
            Assert.That(rect.anchorMin.x, Is.EqualTo(0f), "a mark does not span the meter's width.");
            Assert.That(rect.anchorMax.x, Is.EqualTo(1f));
        }

        // Placed twice, still four: a second layout pass re-places rather than re-clones.
        _meter.PlaceThresholds(2f);

        Assert.That(_meter.MarkCount, Is.EqualTo(4));
        Assert.That(LiveMarks().Count, Is.EqualTo(4), "a second layout doubled the marks.");
    }

    [Test]
    public void Meter_ReadsTheThresholdsThroughTheDoor()
    {
        // **Rule 3.** M6-04's drafted `static readonly float[] Thresholds` is a handle any caller can
        // write through, which is static mutable state (AR §7). Constants are literals and cannot be
        // written, so they are the one kind of public static field allowed.
        FieldInfo[] fields = typeof(Veilrot)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => !field.IsLiteral)
            .ToArray();

        Assert.That(
            fields.Select(field => field.Name),
            Is.Empty,
            "Veilrot exposes a public static field that is not a constant — the array rule 3 refused.");

        Assert.That(Veilrot.ThresholdCount, Is.EqualTo(4));

        // The implied guards.
        Assert.Throws<ArgumentOutOfRangeException>(() => Veilrot.Threshold(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Veilrot.Threshold(Veilrot.ThresholdCount));
    }

    // ---- Rule 4: the Claiming is a mark --------------------------------------------------------

    [Test]
    public void Meter_TheClaimingIsAMarkAndNotAFullBar()
    {
        BuildMeter();

        // M6-04 rule 6's odd state: claimed, then cleansed to 40.
        _meter.Set(40f, isClaimed: true);

        Assert.That(_meter.Fill, Is.EqualTo(0.4f).Within(1e-4f), "the Claiming drew the bar full.");
        Assert.That(_meter.IsClaimed, Is.True);
        Assert.That(ClaimedMark().gameObject.activeSelf, Is.True, "the Claiming's mark is not up.");
    }

    [Test]
    public void Meter_TheMarkDoesNotComeOff()
    {
        BuildMeter();

        _meter.Set(100f, true);
        _meter.Set(40f, true);

        Assert.That(_meter.IsClaimed, Is.True, "the mark came off with the value.");

        // And not even when told false: RunState.IsClaimed never goes false again, so a caller that
        // said so would be wrong, and the meter would be telling the player the gamble was refundable.
        _meter.Set(40f, false);

        Assert.That(_meter.IsClaimed, Is.True);
        Assert.That(ClaimedMark().gameObject.activeSelf, Is.True);
    }

    // ---- Rule 5: the colour --------------------------------------------------------------------

    [Test]
    public void Meter_IsViolet()
    {
        BuildMeter();

        _meter.Set(50f, false);

        Assert.That(SameColour(Fill().color, Palette.Veilrot), Is.True, "the fill is not Palette.Veilrot.");

        // #A855F7 to eight bits, so the meter is GD §16.4's hex and not merely whatever the palette
        // holds today.
        Assert.That(Mathf.RoundToInt(Fill().color.r * 255f), Is.EqualTo(0xA8));
        Assert.That(Mathf.RoundToInt(Fill().color.g * 255f), Is.EqualTo(0x55));
        Assert.That(Mathf.RoundToInt(Fill().color.b * 255f), Is.EqualTo(0xF7));
    }

    [Test]
    public void Meter_IsNeverTheDangerColour()
    {
        BuildMeter();

        _meter.PlaceThresholds(1f);
        _meter.Set(100f, true);

        Graphic[] graphics = _meter.GetComponentsInChildren<Graphic>(true);

        Assert.That(graphics.Length, Is.GreaterThanOrEqualTo(7), "track, fill, template, four marks, cap.");

        // GD §16.4's "for nothing else, ever" — and the Claiming is exactly the state that tempts it.
        foreach (Graphic graphic in graphics)
        {
            Assert.That(Palette.IsDanger(graphic.color), Is.False, $"{graphic.name} is #FF4A1F.");
        }
    }

    // ---- Rule 2: the presenter -----------------------------------------------------------------

    [Test]
    public void Hud_DrawsTheEssenceCounter()
    {
        BuildHud();

        _hub.Publish(new EssenceChanged(84, 24));

        Assert.That(EssenceText().text, Is.EqualTo("84"));
        Assert.That(SameColour(EssenceText().color, Palette.Essence), Is.True, "the counter is not reward gold.");

        _hub.Publish(new EssenceChanged(44, -40));

        Assert.That(EssenceText().text, Is.EqualTo("44"), "a purchase did not move the counter.");
    }

    [Test]
    public void Hud_SeedsBothFromTheRunOnStart()
    {
        CreateRun();

        _hud = Instantiate();
        Bind();

        int essenceEvents = 0;
        int veilrotEvents = 0;

        using IDisposable essence = _hub.Subscribe<EssenceChanged>(_ => essenceEvents++);
        using IDisposable veilrot = _hub.Subscribe<VeilrotChanged>(_ => veilrotEvents++);

        // A **resumed** run: the wallet and the meter are restored silently (M6-01a rule 8, M6-04
        // rule 9), so RunStarted is the only thing that can draw them.
        _session.Start(Config(Resumed(essence: 317, veilrot: 78f)));

        Assert.That(essenceEvents, Is.Zero, "the fixture's premise: a restore publishes no Essence.");
        Assert.That(veilrotEvents, Is.Zero, "the fixture's premise: a restore publishes no Veilrot.");

        Assert.That(EssenceText().text, Is.EqualTo("317"), "a resumed run's balance was drawn as zero.");
        Assert.That(_meter.Fill, Is.EqualTo(0.78f).Within(1e-4f), "a resumed run's meter was drawn as zero.");
        Assert.That(_meter.IsClaimed, Is.False);

        // And from Start, for the other order VContainer and Unity may run in.
        Invoke(_presenter, "Start");

        Assert.That(EssenceText().text, Is.EqualTo("317"));
        Assert.That(_meter.Fill, Is.EqualTo(0.78f).Within(1e-4f));
    }

    [Test]
    public void Hud_RaisesTheClaimingOnItsEvent()
    {
        BuildHud(Shipped());

        _hub.Publish(new VeilrotChanged(100f, 20f));

        Assert.That(_meter.IsClaimed, Is.False, "the value alone raised the mark.");
        Assert.That(ClaimedLabel().gameObject.activeSelf, Is.False);

        _hub.Publish(new ClaimingBegan(200f));

        Assert.That(_meter.IsClaimed, Is.True);
        Assert.That(ClaimedLabel().gameObject.activeSelf, Is.True, "ui.hud.claimed is not drawn.");
        Assert.That(ClaimedLabel().text, Is.EqualTo("Claimed"), "the Claiming's name is not the shipped word.");
        Assert.That(Palette.IsDanger(ClaimedLabel().color), Is.False, "rule 5, for the word as well.");

        // Rule 4 through the presenter: Cleanse from 100 to 40, and the mark stays.
        _hub.Publish(new VeilrotChanged(40f, -60f));

        Assert.That(_meter.Fill, Is.EqualTo(0.4f).Within(1e-4f));
        Assert.That(_meter.IsClaimed, Is.True, "a cleanse took the Claiming's mark off.");
        Assert.That(ClaimedLabel().gameObject.activeSelf, Is.True);
    }

    // ---- Rule 10: dp ---------------------------------------------------------------------------

    [Test]
    public void Hud_PlacesBothInDp()
    {
        (float height, float inset) at1 = PlacedAt(1f);
        (float height, float inset) at2 = PlacedAt(2f);

        // A canvas scaled twice as large needs half the canvas units for the same physical size.
        Assert.That(at1.height, Is.EqualTo(at2.height * 2f).Within(1e-2f), "the meter's height ignores the scale.");
        Assert.That(at1.inset, Is.EqualTo(at2.inset * 2f).Within(1e-2f), "the counter's inset ignores the scale.");

        float pxPerDp = StickShaper.PixelsPerDp(Screen.dpi);

        Assert.That(at1.height, Is.EqualTo(160f * pxPerDp).Within(1e-2f));
        Assert.That(at1.inset, Is.EqualTo(68f * pxPerDp).Within(1e-2f));
    }

    [Test]
    public void Hud_ANonFiniteDpFieldIsIgnored()
    {
        BuildHud();

        var meterRect = (RectTransform)_meter.transform;
        var counterRect = (RectTransform)EssenceText().transform;

        Vector2 meterSize = meterRect.sizeDelta;
        Vector2 meterAt = meterRect.anchoredPosition;
        Vector2 counterSize = counterRect.sizeDelta;
        Vector2 counterAt = counterRect.anchoredPosition;

        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -10f })
        {
            SetPrivate(_presenter, "_meterSizeDp", new Vector2(12f, bad));
            SetPrivate(_presenter, "_essenceSizeDp", new Vector2(bad, 24f));

            Assert.That(() => Invoke(_presenter, "Place"), Throws.Nothing, $"Place threw on {bad}.");

            Assert.That(meterRect.sizeDelta, Is.EqualTo(meterSize), $"meter height {bad}");
            Assert.That(meterRect.anchoredPosition, Is.EqualTo(meterAt), $"meter height {bad}");
            Assert.That(counterRect.sizeDelta, Is.EqualTo(counterSize), $"counter width {bad}");
            Assert.That(counterRect.anchoredPosition, Is.EqualTo(counterAt), $"counter width {bad}");
        }

        // An inset has an honest zero — flush to the edge — and no honest NaN.
        SetPrivate(_presenter, "_meterSizeDp", new Vector2(12f, 160f));
        SetPrivate(_presenter, "_essenceSizeDp", new Vector2(96f, 24f));
        SetPrivate(_presenter, "_meterInsetDp", new Vector2(float.NaN, 76f));
        Invoke(_presenter, "Place");

        Assert.That(meterRect.anchoredPosition, Is.EqualTo(meterAt), "a NaN inset moved the meter.");
        Assert.That(float.IsFinite(meterRect.anchoredPosition.x), Is.True);
    }

    // ---- Rule 11: nothing per frame ------------------------------------------------------------

    [Test]
    public void Hud_AllocatesNothingPerFrame()
    {
        BuildHud();

        _hub.Publish(new VeilrotChanged(60f, 60f));
        _hub.Publish(new EssenceChanged(84, 84));

        MethodInfo update = typeof(HudPresenter).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        var frame = (Action)Delegate.CreateDelegate(typeof(Action), _presenter, update);

        int n = 0;

        // The frame, and the meter's own Set on every one of them — stronger than the claim, which is
        // that Set only runs on an event.
        AllocationAssert.None(() =>
        {
            frame();
            _meter.Set((n++ & 1) == 0 ? 60f : 61f, false);
        });
    }

    // ---- The strings and the asset -------------------------------------------------------------

    [Test]
    public void Strings_EveryKeyThisTaskDrawsHasARow()
    {
        ILocalizer english = Shipped();

        // Ledger row 7's three, and a key with no row resolves to itself.
        foreach (string key in Keys)
        {
            string word = english.Get(new LocKey(key));

            Assert.That(word, Is.Not.EqualTo(key), $"{key} has no row in English.asset.");
            Assert.That(word, Is.Not.Empty);
        }

        BuildHud(Shipped());

        Assert.That(Label("_essenceLabel").text, Is.EqualTo("Essence"));
        Assert.That(Label("_veilrotLabel").text, Is.EqualTo("Veilrot"));
        Assert.That(Label("_claimedLabel").text, Is.EqualTo("Claimed"));
    }

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        var meter = prefab.GetComponentInChildren<VeilrotMeterView>(true);
        var presenter = prefab.GetComponent<HudPresenter>();

        Assert.That(meter, Is.Not.Null, "VeilrotMeterView did not load off Hud.prefab (Traps §5).");
        Assert.That(presenter, Is.Not.Null);

        var mso = new SerializedObject(meter);
        var fill = mso.FindProperty("_fill").objectReferenceValue as Image;
        var track = mso.FindProperty("_track").objectReferenceValue as Image;
        var mark = mso.FindProperty("_mark").objectReferenceValue as Image;
        var cap = mso.FindProperty("_claimedMark").objectReferenceValue as Image;

        Assert.That(fill, Is.Not.Null, "the meter has no fill.");
        Assert.That(track, Is.Not.Null, "the meter has no track.");
        Assert.That(mark, Is.Not.Null, "the meter has no mark template.");
        Assert.That(cap, Is.Not.Null, "the meter has no Claiming mark.");

        Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
        Assert.That(fill.fillMethod, Is.EqualTo(Image.FillMethod.Vertical), "the meter fills sideways.");
        Assert.That(fill.fillOrigin, Is.EqualTo((int)Image.OriginVertical.Bottom), "the meter fills from the top.");

        foreach (Image image in new[] { fill, track, mark, cap })
        {
            Assert.That(image.sprite, Is.Not.Null, $"{image.name} has no sprite, so it draws nothing.");
            Assert.That(image.raycastTarget, Is.False, $"{image.name} eats taps meant for the arena.");
        }

        Assert.That(mark.gameObject.activeSelf, Is.False, "the mark template ships active.");
        Assert.That(cap.gameObject.activeSelf, Is.False, "the prefab ships the run Claimed.");

        // The four marks are built at runtime from the template, so a dressed meter is one that
        // places four — asserted on an instance.
        GameObject hud = Instantiate();
        var live = hud.GetComponentInChildren<VeilrotMeterView>(true);

        live.PlaceThresholds(1f);

        Assert.That(live.MarkCount, Is.EqualTo(4));

        var hso = new SerializedObject(presenter);

        foreach (string field in new[] { "_meter", "_essenceText", "_essenceLabel", "_veilrotLabel", "_claimedLabel" })
        {
            Assert.That(hso.FindProperty(field).objectReferenceValue, Is.Not.Null, $"HudPresenter has no {field}.");
        }

        var claimed = (TMP_Text)hso.FindProperty("_claimedLabel").objectReferenceValue;

        Assert.That(claimed.gameObject.activeSelf, Is.False, "the Claiming's name ships up.");

        // Every authored word is a key, never English (ADR-0012).
        foreach (string field in new[] { "_essenceLabel", "_veilrotLabel", "_claimedLabel" })
        {
            var label = (TMP_Text)hso.FindProperty(field).objectReferenceValue;

            Assert.That(label.text, Does.StartWith("ui.hud."), $"{field} ships raw English.");
            Assert.That(label.raycastTarget, Is.False, $"{field} eats taps.");
        }

        Assert.That(meter.GetComponentInParent<SafeAreaFitter>(true), Is.Not.Null, "the meter is outside the safe area.");

        // On top of everything M4-04 left.
        Assert.That(prefab.GetComponentInChildren<BossBarView>(true), Is.Not.Null, "BossBarView is gone.");
        Assert.That(prefab.GetComponentInChildren<XpBarView>(true), Is.Not.Null, "XpBarView is gone.");
        Assert.That(prefab.GetComponentInChildren<AutoCastRow>(true), Is.Not.Null, "AutoCastRow is gone.");
        Assert.That(prefab.GetComponentInChildren<OverflowToast>(true), Is.Not.Null, "OverflowToast is gone.");
        Assert.That(prefab.GetComponentInChildren<SkillBarPresenter>(true), Is.Not.Null, "SkillBarPresenter is gone.");
    }

    // ---- Guard rows ----------------------------------------------------------------------------

    [Test]
    public void Construct_RefusesNulls()
    {
        CreateRun();

        _hud = Instantiate();
        var presenter = _hud.GetComponent<HudPresenter>();

        Assert.Throws<ArgumentNullException>(() => presenter.Construct(null, _session, Passthrough()));
        Assert.Throws<ArgumentNullException>(() => presenter.Construct(_hub, null, Passthrough()));
        Assert.Throws<ArgumentNullException>(() => presenter.Construct(_hub, _session, null));
    }

    [Test]
    public void Hud_AnUndressedReadoutIsSilent()
    {
        StartRun();

        _hud = Instantiate();
        _presenter = _hud.GetComponent<HudPresenter>();
        _presenter.Construct(_hub, _session, Passthrough());

        // SkillBarPresenter's bargain: an undressed readout is a silent readout, not a stopped run.
        foreach (string field in new[] { "_meter", "_essenceText", "_essenceLabel", "_veilrotLabel", "_claimedLabel" })
        {
            SetPrivate(_presenter, field, null);
        }

        Assert.That(() => Invoke(_presenter, "Start"), Throws.Nothing);
        Assert.That(
            () =>
            {
                _hub.Publish(new EssenceChanged(10, 10));
                _hub.Publish(new VeilrotChanged(30f, 30f));
                _hub.Publish(new ClaimingBegan(100f));
            },
            Throws.Nothing);
    }

    [Test]
    public void Hud_DropsItsEconomySubscriptions()
    {
        BuildHud();

        Assert.That(_hub.SubscriberCount<EssenceChanged>(), Is.EqualTo(1));
        Assert.That(_hub.SubscriberCount<VeilrotChanged>(), Is.EqualTo(1));
        Assert.That(_hub.SubscriberCount<ClaimingBegan>(), Is.EqualTo(1));

        Invoke(_presenter, "OnDestroy");

        Assert.That(_hub.SubscriberCount<EssenceChanged>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<VeilrotChanged>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<ClaimingBegan>(), Is.Zero);
    }

    // ---- Fixture -------------------------------------------------------------------------------

    /// <summary>The shipped HUD's meter alone, over no run — the meter needs none.</summary>
    private void BuildMeter()
    {
        _hud = Instantiate();
        _meter = _hud.GetComponentInChildren<VeilrotMeterView>(true);

        Assert.That(_meter, Is.Not.Null, "VeilrotMeterView did not load off Hud.prefab (Traps §5).");
    }

    /// <summary>A started run and the shipped HUD over it, injected and started.</summary>
    private void BuildHud(ILocalizer localizer = null)
    {
        StartRun();

        _hud = Instantiate();
        Bind(localizer);

        Invoke(_presenter, "Start");
    }

    /// <summary>What RunScope's RegisterComponent does.</summary>
    private void Bind(ILocalizer localizer = null)
    {
        _presenter = _hud.GetComponent<HudPresenter>();
        _meter = _hud.GetComponentInChildren<VeilrotMeterView>(true);

        Assert.That(_presenter, Is.Not.Null, "HudPresenter did not load off Hud.prefab (Traps §5).");
        Assert.That(_meter, Is.Not.Null, "VeilrotMeterView did not load off Hud.prefab (Traps §5).");

        _presenter.Construct(_hub, _session, localizer ?? Passthrough());
    }

    /// <summary>The meter's height and the counter's right inset, laid out at one canvas scale.</summary>
    private (float Height, float Inset) PlacedAt(float scale)
    {
        StartRun();

        _hud = Instantiate();
        _hud.GetComponent<Canvas>().scaleFactor = scale;
        Bind();

        Invoke(_presenter, "Start");

        var meterRect = (RectTransform)_meter.transform;
        var counterRect = (RectTransform)EssenceText().transform;

        return (meterRect.sizeDelta.y, -counterRect.anchoredPosition.x);
    }

    private void CreateRun()
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
    }

    private void StartRun()
    {
        CreateRun();

        _session.Start(Config(restore: null));
    }

    private RunConfig Config(RunSnapshot? restore) => new RunConfig(
        new ContentId(ModeId),
        new ContentId(OathboundId),
        _random.Seed,
        1,
        SpawnPlan.Empty,
        restore);

    /// <summary>A saved run at stage 1 carrying <paramref name="essence"/> and <paramref name="veilrot"/>.</summary>
    private RunSnapshot Resumed(int essence, float veilrot) => new RunSnapshot(
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
        1,
        0f,
        0,
        Array.Empty<ContentId>(),
        new ContentId[SkillRunner.MaxManualSlots],
        new RunEconomy(essence, veilrot, 0, 0),
        Array.Empty<ContentId>(),
        Array.Empty<ContentId>(),
        Array.Empty<ContentId>());

    private GameObject Instantiate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        GameObject hud = Object.Instantiate(prefab);

        _spawned.Add(hud);

        // Pinned: an EditMode canvas has never been driven by its own CanvasScaler.
        hud.GetComponent<Canvas>().scaleFactor = 1f;

        return hud;
    }

    private Image Fill() => Field<Image>(_meter, "_fill");

    private Image ClaimedMark() => Field<Image>(_meter, "_claimedMark");

    private List<Image> LiveMarks() =>
        Field<List<Image>>(_meter, "_marks")
            .Where(mark => mark != null && mark.gameObject.activeSelf)
            .ToList();

    private TMP_Text EssenceText() => Field<TMP_Text>(_presenter, "_essenceText");

    private TMP_Text ClaimedLabel() => Field<TMP_Text>(_presenter, "_claimedLabel");

    private TMP_Text Label(string field) => Field<TMP_Text>(_presenter, field);

    /// <summary>Two colours to eight bits a channel, alpha ignored — <c>Palette.IsDanger</c>'s resolution.</summary>
    private static bool SameColour(Color left, Color right) =>
        Mathf.RoundToInt(left.r * 255f) == Mathf.RoundToInt(right.r * 255f)
        && Mathf.RoundToInt(left.g * 255f) == Mathf.RoundToInt(right.g * 255f)
        && Mathf.RoundToInt(left.b * 255f) == Mathf.RoundToInt(right.b * 255f);

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

    private static ILocalizer Passthrough() =>
        new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>());

    private static ILocalizer Shipped()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No localization table at {EnglishPath}.");

        return new TableLocalizer(table);
    }

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
