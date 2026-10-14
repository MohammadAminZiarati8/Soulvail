using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// The class-select screen: one class at a time, and the button that decides which run the player
/// gets. M5-07, M6-09b, RS-05c, UI-05c; CH §3, §6; GD §4.5.
/// </summary>
/// <remarks>
/// <para>
/// <b>The screen under test is the shipped asset</b> — <c>PausePresenterTests</c>' and
/// <c>LevelUpPresenterTests</c>' rule, for its reason: a fixture that built its own hierarchy would
/// go green over a <c>ClassSelect.prefab</c> dressed any way at all.
/// </para>
/// <para>
/// <b>The classes under test are the shipped assets too.</b> A row asserting a price against a
/// catalog this file invented would assert nothing.
/// </para>
/// <para>
/// <b><c>Start</c> never runs in EditMode</b>, so <see cref="Start"/> invokes it by reflection, which
/// is also what puts the screen down the way a shipped build finds it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ClassSelectPresenterTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/ClassSelect.prefab";
    private const string StagePath = "Assets/_Project/Prefabs/UI/CharacterStage.prefab";
    private const string RangerBodyPath = "Assets/_Project/Prefabs/Player/Bodies/Ranger.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";
    private const string GravecallerPath = "Assets/_Project/Data/Characters/Gravecaller.asset";
    private const string EmberwrightPath = "Assets/_Project/Data/Characters/Emberwright.asset";
    private const string RangerPath = "Assets/_Project/Data/Characters/Ranger.asset";
    private const string HuskPath = "Assets/_Project/Data/Enemies/Husk.asset";
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";

    private const string OathboundId = "character.oathbound";
    private const string GravecallerId = "character.gravecaller";
    private const string EmberwrightId = "character.emberwright";
    private const string RangerId = "character.ranger";
    private const string DescentId = "mode.descent";

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    private readonly List<Object> _spawned = new List<Object>();

    private GameObject _screen;
    private ClassSelectPresenter _presenter;
    private PendingRun _pending;
    private RecordingLoader _loader;
    private InMemorySaveStore _store;
    private ProfileStore _profiles;

    [SetUp]
    public void Reset()
    {
        _pending = new PendingRun();
        _loader = new RecordingLoader();
        _store = new InMemorySaveStore();
        _profiles = new ProfileStore(_store);
    }

    [TearDown]
    public void DestroySpawned()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            // Unity's ==: a destroyed object is a live reference that only compares equal to null
            // through the engine's operator.
            if (_spawned[i] != null)
            {
                Object.DestroyImmediate(_spawned[i]);
            }
        }

        _spawned.Clear();
    }

    // ---- Descend opens the screen; the screen starts the run -------------------------------------

    [Test]
    public void Menu_DescendOpensTheScreen()
    {
        BuildScreen();

        MenuPresenter menu = Menu(out Button descend, out _);

        descend.onClick.Invoke();

        Assert.That(_presenter.IsOpen, Is.True, "Descend did not open the class-select screen.");
        Assert.That(_loader.Asked, Is.Empty, "Descend loaded a scene. The class is not known yet.");
        Assert.That(_pending.IsSet, Is.False, "Descend wrote a pending run.");
        Assert.That(menu, Is.Not.Null);
        Assert.That(descend.interactable, Is.True);
    }

    // ---- Rule 1: one class on screen ---------------------------------------------------------------

    [Test]
    public void Open_ShowsTheFirstOwnedClass()
    {
        // Emberwright and Gravecaller are priced on a fresh profile; the Oathbound is the first the
        // player owns, third in this roster.
        BuildScreen(
            catalog: ShippedCatalog(),
            localizer: Shipped(),
            roster: new ClassSelectRoster(new[]
            {
                new ContentId(EmberwrightId), new ContentId(GravecallerId), new ContentId(OathboundId),
            }));

        _presenter.Open();

        Assert.That(_presenter.Index, Is.EqualTo(2), "the page did not open on the first owned class.");
        Assert.That(PageText("_name"), Is.EqualTo("Oathbound"));
        Assert.That(Page().ButtonState, Is.EqualTo(PageButton.Choose));
    }

    [Test]
    public void Open_WithNothingOwnedShowsTheFirstClass()
    {
        BuildScreen(
            catalog: ShippedCatalog(),
            roster: new ClassSelectRoster(new[] { new ContentId(EmberwrightId), new ContentId(GravecallerId) }));

        _presenter.Open();

        Assert.That(_presenter.Index, Is.Zero);
    }

    // ---- Rule 2: arrows and dots --------------------------------------------------------------------

    [Test]
    public void Next_Previous_StepAndClampAtTheEnds()
    {
        BuildScreen(catalog: CatalogOf(3));

        _presenter.Open();

        Assert.That(_presenter.Index, Is.Zero);
        Assert.That(Page().PreviousShown, Is.False, "the arrow at the first class is drawn.");
        Assert.That(Page().NextShown, Is.True);
        Assert.That(Page().DotsShown, Is.EqualTo(3));

        PressNext();

        Assert.That(_presenter.Index, Is.EqualTo(1));
        Assert.That(Page().PreviousShown, Is.True);
        Assert.That(Page().NextShown, Is.True);

        PressNext();

        Assert.That(_presenter.Index, Is.EqualTo(2));
        Assert.That(Page().NextShown, Is.False, "the arrow at the last class is drawn.");

        PressNext();

        Assert.That(_presenter.Index, Is.EqualTo(2), "next wrapped.");

        PressPrevious();
        PressPrevious();
        PressPrevious();

        Assert.That(_presenter.Index, Is.Zero, "previous wrapped.");
    }

    [Test]
    public void OneClass_HasNoArrowsOrDots()
    {
        BuildScreen(
            catalog: ShippedCatalog(),
            roster: new ClassSelectRoster(new[] { new ContentId(RangerId) }));

        _presenter.Open();

        Assert.That(Page().PreviousShown, Is.False);
        Assert.That(Page().NextShown, Is.False);
        Assert.That(Page().DotsShown, Is.Zero);
    }

    // ---- Rule 3: swipe on the name band, drag on the preview ----------------------------------------

    [Test]
    public void Swipe_OnTheNameBandSteps_DragOnThePreviewRotates()
    {
        CharacterPreview preview = BuildPreview();
        var look = new CharacterLook(AssetDatabase.LoadAssetAtPath<GameObject>(RangerBodyPath));

        BuildScreen(
            catalog: CatalogOf(3),
            preview: preview,
            looks: new CharacterLookBook(new Dictionary<ContentId, CharacterLook>
            {
                [new ContentId("character.stand-in-0")] = look,
                [new ContentId("character.stand-in-1")] = look,
                [new ContentId("character.stand-in-2")] = look,
            }));

        _presenter.Open();

        CharacterPage page = Page();
        var band = (RectTransform)typeof(CharacterPage).GetField("_nameBand", Private).GetValue(page);
        float yaw = preview.Body.transform.localEulerAngles.y;

        // A drag on the preview turns the body, however far it travels, and never steps.
        Drag(page, page.Preview.gameObject, deltaX: 10f, travelled: 4f * CharacterPage.SwipePixels);

        Assert.That(_presenter.Index, Is.Zero, "a drag on the preview stepped.");
        Assert.That(preview.Body.transform.localEulerAngles.y, Is.Not.EqualTo(yaw).Within(0.01f), "the drag did not turn the body.");

        // A short swipe on the band does nothing.
        yaw = preview.Body.transform.localEulerAngles.y;
        Drag(page, band.gameObject, deltaX: -3f, travelled: -0.25f * CharacterPage.SwipePixels);

        Assert.That(_presenter.Index, Is.Zero, "a swipe shorter than 48 dp stepped.");

        // A long one, leftwards, steps forward — and a swipe never turns the model.
        Drag(page, band.gameObject, deltaX: -30f, travelled: -2f * CharacterPage.SwipePixels);

        Assert.That(_presenter.Index, Is.EqualTo(1), "a swipe past 48 dp did not step.");
        Assert.That(Mathf.DeltaAngle(preview.Body.transform.localEulerAngles.y, 0f), Is.Zero.Within(0.01f), "a new class stands facing front.");

        // And back, rightwards.
        Drag(page, band.gameObject, deltaX: 30f, travelled: 2f * CharacterPage.SwipePixels);

        Assert.That(_presenter.Index, Is.Zero);
    }

    // ---- Rule 4: the preview -------------------------------------------------------------------------

    [Test]
    public void Preview_ShowsTheClassBody_HidesOnClose()
    {
        CharacterPreview preview = BuildPreview();
        var look = new CharacterLook(AssetDatabase.LoadAssetAtPath<GameObject>(RangerBodyPath));

        BuildScreen(
            catalog: ShippedCatalog(),
            roster: new ClassSelectRoster(new[] { new ContentId(RangerId) }),
            preview: preview,
            looks: new CharacterLookBook(new Dictionary<ContentId, CharacterLook> { [new ContentId(RangerId)] = look }));

        _presenter.Open();

        Assert.That(preview.IsShowing, Is.True, "the class's body is not on the pedestal.");
        Assert.That(preview.Texture, Is.Not.Null);
        Assert.That(Page().Preview.texture, Is.SameAs(preview.Texture), "the image shows another texture.");

        _presenter.Close();

        Assert.That(preview.IsShowing, Is.False, "the body outlived the page.");
    }

    // ---- Rule 5: name and role ----------------------------------------------------------------------

    [Test]
    public void Header_NameAndRole()
    {
        BuildScreen(
            catalog: ShippedCatalog(),
            localizer: Shipped(),
            roster: new ClassSelectRoster(new[] { new ContentId(RangerId), new ContentId(OathboundId) }));

        _presenter.Open();

        Assert.That(PageText("_name"), Is.EqualTo(Shipped().Get(Character(RangerPath).ToSpec().NameKey)));
        Assert.That(PageText("_roleLabel"), Is.EqualTo("Ranged"));
        Assert.That(PageImage("_roleBadge").gameObject.activeSelf, Is.True, "the Ranger's badge is imported and not drawn.");

        PressNext();

        Assert.That(PageText("_name"), Is.EqualTo("Oathbound"));
        Assert.That(PageText("_roleLabel"), Is.EqualTo("Melee"));
        Assert.That(
            PageImage("_roleBadge").gameObject.activeSelf,
            Is.False,
            "a role with no sprite imported drew a blank badge.");
    }

    // ---- Rule 6: the button --------------------------------------------------------------------------

    [Test]
    public void Choose_StartsTheRunOnce()
    {
        var pending = new PendingRun();
        var slow = new SlowLoader();

        BuildScreen(
            catalog: ShippedCatalog(),
            pending: pending,
            loader: slow,
            roster: new ClassSelectRoster(new[] { new ContentId(RangerId) }),
            localizer: Shipped());

        _presenter.Open();

        Assert.That(PageText("_primaryLabel"), Is.EqualTo("Choose"));
        Assert.That(PageImage("_primaryFace").sprite.name, Is.EqualTo("Button_Green"));

        Page().Primary.onClick.Invoke();
        Page().Primary.onClick.Invoke();

        Assert.That(slow.Attempts, Is.EqualTo(1), "two taps asked for two scene loads.");
        Assert.That(pending.CharacterId.Value, Is.EqualTo(RangerId));
        Assert.That(pending.ModeId.Value, Is.EqualTo(DescentId));
        Assert.That(pending.Snapshot, Is.Null, "a fresh run, not a resume.");
        Assert.That(Page().Primary.interactable, Is.False, "the button is live under the load.");

        slow.Finish();
    }

    [Test]
    public void Choose_AFailedLoadGivesTheButtonBack()
    {
        var refusing = new RefusingLoader();

        BuildScreen(loader: refusing);

        _presenter.Open();

        LogAssert.Expect(LogType.Exception, new Regex(RefusingLoader.Message));

        Page().Primary.onClick.Invoke();

        Assert.That(_presenter.IsOpen, Is.True, "the screen went down over a load that never happened.");
        Assert.That(Page().Primary.interactable, Is.True, "the button is dead and the player is stranded.");

        LogAssert.Expect(LogType.Exception, new Regex(RefusingLoader.Message));

        Page().Primary.onClick.Invoke();

        Assert.That(refusing.Attempts, Is.EqualTo(2), "the latch did not come down with the button.");
    }

    [Test]
    public void Unlock_BuysRedrawsStartsNothing()
    {
        BuildScreen(
            catalog: ShippedCatalog(),
            profile: Holding(3_500),
            localizer: Shipped(),
            roster: new ClassSelectRoster(new[] { new ContentId(EmberwrightId) }));

        _presenter.Open();

        Assert.That(Page().ButtonState, Is.EqualTo(PageButton.Unlock));
        Assert.That(PageText("_primaryLabel"), Is.EqualTo("Unlock"));
        Assert.That(PageImage("_primaryFace").sprite.name, Is.EqualTo("Button_Violet"));
        Assert.That(PageText("_price"), Is.EqualTo("3500"));
        Assert.That(PageRect("_pill").gameObject.activeSelf, Is.True, "the price pill is hidden on a priced class.");
        Assert.That(PageText("_price"), Is.Not.Empty);
        Assert.That(BalanceLabel().text, Is.EqualTo("3500"));

        Page().Primary.onClick.Invoke();

        Assert.That(_store.ProfileWriteCount, Is.EqualTo(1), "a purchase is one write.");
        Assert.That(_profiles.Current.Shards, Is.Zero);
        Assert.That(_profiles.Current.UnlockedCharacterIds, Is.EqualTo(new[] { new ContentId(EmberwrightId) }));
        Assert.That(_presenter.Shards, Is.Zero, "the balance was not redrawn after the purchase.");
        Assert.That(BalanceLabel().text, Is.EqualTo("0"));
        Assert.That(Page().ButtonState, Is.EqualTo(PageButton.Choose), "the page did not redraw as owned.");
        Assert.That(PageRect("_pill").gameObject.activeSelf, Is.False);
        Assert.That(_pending.IsSet, Is.False, "the tap that bought the class also started a run.");
        Assert.That(_loader.Asked, Is.Empty);

        // A later frame, and a second tap: now it plays.
        Update();
        Page().Primary.onClick.Invoke();

        Assert.That(_pending.CharacterId.Value, Is.EqualTo(EmberwrightId));
        Assert.That(_loader.Asked, Is.EqualTo(new[] { SceneLoader.Run }));
    }

    /// <summary>Two taps from one <c>EventSystem</c> pass: one purchase, and no descent.</summary>
    [Test]
    public void Unlock_OnePurchasePerFrame()
    {
        BuildScreen(
            catalog: ShippedCatalog(),
            profile: Holding(5_500),
            roster: new ClassSelectRoster(new[] { new ContentId(EmberwrightId), new ContentId(GravecallerId) }));

        _presenter.Open();

        Page().Primary.onClick.Invoke();
        Page().Primary.onClick.Invoke();

        Assert.That(_store.ProfileWriteCount, Is.EqualTo(1), "one pass bought twice.");
        Assert.That(_pending.IsSet, Is.False, "the second half of a double tap on a price started a run.");

        // The latch is a frame, not the screen: the next frame's step and tap buy the other class.
        Update();
        PressNext();
        Page().Primary.onClick.Invoke();

        Assert.That(_store.ProfileWriteCount, Is.EqualTo(2));
        Assert.That(_profiles.Current.Shards, Is.Zero);
    }

    [Test]
    public void Locked_Unpayable_IsDead()
    {
        BuildScreen(
            catalog: ShippedCatalog(),
            profile: Holding(1_999),
            localizer: Shipped(),
            roster: new ClassSelectRoster(new[] { new ContentId(EmberwrightId), new ContentId(GravecallerId) }));

        _presenter.Open();

        Assert.That(Page().ButtonState, Is.EqualTo(PageButton.Dead));
        Assert.That(Page().Primary.interactable, Is.False, "a price that cannot be paid is live.");
        Assert.That(PageImage("_primaryFace").sprite.name, Is.EqualTo("Button_Grey"));
        Assert.That(PageText("_price"), Is.EqualTo("3500"), "a dead price was hidden rather than drawn.");
        Assert.That(PageLabel("_price").color, Is.EqualTo(Palette.Neutral));
        Assert.That(PageText("_deed"), Is.EqualTo("or reach stage 20"));

        // M5-08a rule 4's second door: a handler invoked by hand still asks CanBuy.
        MethodInfo handler = typeof(ClassSelectPresenter).GetMethod("OnUnlockTapped", Private);

        Assert.DoesNotThrow(
            () => handler.Invoke(_presenter, new object[] { new ContentId(EmberwrightId) }),
            "an unaffordable purchase reached ProfileStore.Unlock and threw out of a click.");

        Page().Primary.onClick.Invoke();

        Assert.That(_store.ProfileWriteCount, Is.Zero);
        Assert.That(_profiles.Current.Shards, Is.EqualTo(1_999));
        Assert.That(_pending.IsSet, Is.False, "a locked class started a run.");

        // The boss deed draws no line: the Choirmother arrives with M7-03.
        PressNext();

        Assert.That(Page().ButtonState, Is.EqualTo(PageButton.Dead));
        Assert.That(PageLabel("_deed").gameObject.activeSelf, Is.False, "the Gravecaller promises a boss no mode authors.");
    }

    [Test]
    public void Locked_PayableOnePurchaseKillsTheOtherPrice()
    {
        BuildScreen(
            catalog: ShippedCatalog(),
            profile: Holding(3_600),
            roster: new ClassSelectRoster(new[] { new ContentId(EmberwrightId), new ContentId(GravecallerId) }));

        _presenter.Open();
        PressNext();

        Assert.That(Page().ButtonState, Is.EqualTo(PageButton.Unlock), "the Gravecaller should be affordable at 3 600.");

        PressPrevious();
        Page().Primary.onClick.Invoke();
        PressNext();

        Assert.That(
            Page().ButtonState,
            Is.EqualTo(PageButton.Dead),
            "100 Shards left and the Gravecaller's 2 000 still reads as affordable.");
    }

    // ---- Rule 7: the lock badge ---------------------------------------------------------------------

    [Test]
    public void Locked_ShowsTheBadge()
    {
        BuildScreen(
            catalog: ShippedCatalog(),
            roster: new ClassSelectRoster(new[] { new ContentId(OathboundId), new ContentId(EmberwrightId) }));

        _presenter.Open();

        Assert.That(PageImage("_lockBadge").gameObject.activeSelf, Is.False, "an owned class is badged locked.");

        PressNext();

        Assert.That(PageImage("_lockBadge").gameObject.activeSelf, Is.True, "a locked class has no badge.");
        Assert.That(PageText("_name"), Is.Not.Empty, "the body and name show as they are.");
    }

    // ---- Rule 8: back and the balance ---------------------------------------------------------------

    [Test]
    public void Back_ClosesWritingNothing()
    {
        CharacterPreview preview = BuildPreview();

        BuildScreen(preview: preview);

        _presenter.Open();

        Assert.That(_presenter.IsOpen, Is.True);

        Back().onClick.Invoke();

        Assert.That(_presenter.IsOpen, Is.False, "Back left the screen up.");
        Assert.That(Root().alpha, Is.EqualTo(0f), "the screen is still covering the menu.");
        Assert.That(Root().blocksRaycasts, Is.False, "the menu underneath still cannot be tapped.");
        Assert.That(_pending.IsSet, Is.False, "changing your mind started a run.");
        Assert.That(_loader.Asked, Is.Empty);
        Assert.That(_store.ProfileWriteCount, Is.Zero);
        Assert.That(preview.IsShowing, Is.False);
    }

    [Test]
    public void Balance_OnlyOnOpenAndAfterBuy()
    {
        BuildScreen(catalog: ShippedCatalog(), profile: Holding(650));

        _presenter.Open();

        Assert.That(BalanceLabel().text, Is.EqualTo("650"));
        Assert.That(BalanceLabel().color, Is.EqualTo(Palette.Essence), "GD §16.4's reward gold.");
        Assert.That(_presenter.Shards, Is.EqualTo(650));

        // The store moves under an open screen; 120 frames and two steps later the screen has not
        // noticed, because a screen that polled would pick the new balance up on its first Update.
        _profiles.Adopt(PlayerProfile.Default.WithShards(9_000));

        for (int i = 0; i < 120; i++)
        {
            Update();
        }

        PressNext();
        PressPrevious();

        Assert.That(_presenter.Shards, Is.EqualTo(650), "the screen re-read the profile.");
        Assert.That(BalanceLabel().text, Is.EqualTo("650"));
        Assert.That(Page().ButtonState, Is.EqualTo(PageButton.Choose));
    }

    // ---- Rule 9: it fits 9:20 ------------------------------------------------------------------------

    [Test]
    public void Page_FitsPortrait()
    {
        const float Px = 2f;

        // A 9:20 canvas at the Menu's match 0.5: 966 × 2146 units.
        var canvas = new GameObject("Canvas", typeof(RectTransform));
        _spawned.Add(canvas);

        var canvasRect = (RectTransform)canvas.transform;
        canvasRect.sizeDelta = new Vector2(966f, 2146f);

        _screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), canvas.transform);
        _spawned.Add(_screen);
        _presenter = _screen.GetComponent<ClassSelectPresenter>();

        // The middle of a three-class roster with a price and a deed: every piece is on screen.
        _profiles.Adopt(Holding(3_499));
        _presenter.Construct(
            _pending,
            ShippedCatalog(),
            _loader,
            Passthrough(),
            _profiles,
            new ClassSelectRoster(new[] { new ContentId(OathboundId), new ContentId(EmberwrightId), new ContentId(RangerId) }));

        _presenter.Open();
        PressNext();

        Transform safe = _screen.transform.Find("Safe");
        var safeRect = (RectTransform)safe;
        safeRect.anchorMin = Vector2.zero;
        safeRect.anchorMax = Vector2.one;
        safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;

        var pageRect = (RectTransform)Page().transform;
        pageRect.offsetMax = new Vector2(0f, -72f * Px);

        Page().Layout(Px);
        PressPrevious();
        PressNext();

        Rect screen = WorldRect(canvasRect);

        var pieces = new Dictionary<string, Rect>
        {
            ["name band"] = WorldRect(PageRect("_nameBand")),
            ["preview"] = WorldRect((RectTransform)Page().Preview.transform),
            ["previous"] = WorldRect((RectTransform)PageButtonField("_previous").transform),
            ["next"] = WorldRect((RectTransform)PageButtonField("_next").transform),
            ["dots"] = WorldRect(PageRect("_dots")),
            ["deed"] = WorldRect(PageLabel("_deed").rectTransform),
            ["button"] = WorldRect((RectTransform)Page().Primary.transform),
            ["pill"] = WorldRect(PageRect("_pill")),
        };

        foreach (KeyValuePair<string, Rect> piece in pieces)
        {
            Assert.That(
                piece.Value.xMin >= screen.xMin - 0.5f && piece.Value.xMax <= screen.xMax + 0.5f
                    && piece.Value.yMin >= screen.yMin - 0.5f && piece.Value.yMax <= screen.yMax + 0.5f,
                Is.True,
                $"{piece.Key} at {piece.Value} leaves the 9:20 screen {screen}.");
        }

        var names = new List<string>(pieces.Keys);

        for (int i = 0; i < names.Count; i++)
        {
            for (int j = 0; j < i; j++)
            {
                Rect a = pieces[names[i]];
                Rect b = pieces[names[j]];
                bool overlaps = a.xMin < b.xMax - 0.5f && b.xMin < a.xMax - 0.5f && a.yMin < b.yMax - 0.5f && b.yMin < a.yMax - 0.5f;

                Assert.That(overlaps, Is.False, $"{names[i]} at {a} overlaps {names[j]} at {b}.");
            }
        }

        foreach (TMP_Text label in Page().GetComponentsInChildren<TMP_Text>(false))
        {
            Assert.That(label.fontSize, Is.GreaterThanOrEqualTo(12f * Px), $"{label.name} is under 12 sp.");
        }

        Assert.That(BalanceLabel().fontSize, Is.GreaterThanOrEqualTo(12f * StickShaper.PixelsPerDp(Screen.dpi)), "the balance is under 12 sp.");
        Assert.That(pieces["button"].height, Is.GreaterThanOrEqualTo(48f * Px), "the button's hit area is under 48 dp.");
        Assert.That(pieces["previous"].height, Is.GreaterThanOrEqualTo(48f * Px));
        Assert.That(pieces["next"].height, Is.GreaterThanOrEqualTo(48f * Px));
    }

    // ---- Shape, strings, assets ----------------------------------------------------------------------

    [Test]
    public void Prefab_IsDressed()
    {
        LoadScreen();

        Assert.That(Page(), Is.Not.Null, "the root has no page.");
        Assert.That(BalanceLabel(), Is.Not.Null, "the root has no balance label.");
        Assert.That(Back(), Is.Not.Null);
        Assert.That(Page().Primary, Is.Not.Null);
        Assert.That(Page().Preview, Is.Not.Null);
    }

    /// <summary><c>Views_CarryNoSerializedColour</c>'s rule: every colour is <c>Palette</c>.</summary>
    [Test]
    public void Prefab_CarriesNoSerializedColour()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        foreach (MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
        {
            Type type = behaviour.GetType();

            if (type.Namespace is null || !type.Namespace.StartsWith("Soulvail", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (FieldInfo field in type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.That(
                    field.FieldType == typeof(Color) || field.FieldType == typeof(Color32),
                    Is.False,
                    $"{type.Name}.{field.Name} is a serialized colour. Read Palette instead.");
            }
        }
    }

    /// <summary>Nothing is typed into the prefab: every label is written from a key or a number.</summary>
    [Test]
    public void Select_DrawsNoRawEnglish()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        TMP_Text[] labels = prefab.GetComponentsInChildren<TMP_Text>(true);

        Assert.That(labels, Is.Not.Empty, "the prefab has no TMP_Text at all, so this row tests nothing.");

        foreach (TMP_Text label in labels)
        {
            Assert.That(
                label.text,
                Is.Empty,
                $"'{label.text}' is typed into {PrefabPath}. AR §11.5: no raw user-facing string anywhere.");
        }
    }

    [Test]
    public void Strings_EveryKeyThisScreenDrawsHasARow()
    {
        var shipped = new TableLocalizer(EnglishTable());

        foreach (string key in new[]
        {
            "ui.classselect.choose", "ui.classselect.unlock", "ui.classselect.role.ranged",
            "ui.classselect.role.melee", "ui.classselect.role.caster", "ui.classselect.locked.deed",
            "character.oathbound.name", "character.oathbound.description",
            "character.gravecaller.name", "character.gravecaller.description",
        })
        {
            Assert.That(shipped.Has(new LocKey(key)), Is.True, $"English.asset has no row for {key}.");
        }

        Assert.That(shipped.Get(new LocKey("ui.classselect.locked.deed")), Does.Contain("{0}"));

        foreach (string retired in new[] { "ui.classselect.title", "ui.classselect.back", "ui.classselect.balance" })
        {
            Assert.That(shipped.Has(new LocKey(retired)), Is.False, $"{retired} is retired and still has a row.");
        }
    }

    [Test]
    public void ClassSelect_AllocatesNothingPerFrame()
    {
        BuildScreen(catalog: ShippedCatalog(), profile: Holding(650));

        _presenter.Open();

        var update = (Action)Delegate.CreateDelegate(
            typeof(Action), _presenter, typeof(ClassSelectPresenter).GetMethod("Update", Private));

        AllocationAssert.None(update);
    }

    [Test]
    public void Select_InstantiatesNothingButDots()
    {
        BuildScreen(catalog: ShippedCatalog());

        _presenter.Open();

        int transforms = _screen.GetComponentsInChildren<Transform>(true).Length;

        for (int i = 0; i < 100; i++)
        {
            _presenter.Close();
            _presenter.Open();
            PressNext();
        }

        Assert.That(
            _screen.GetComponentsInChildren<Transform>(true).Length,
            Is.EqualTo(transforms),
            "the screen grew objects. A screen that instantiates is a screen that hitches (AR §14).");
    }

    [Test]
    public void Select_AnEmptyCatalogStillOpens()
    {
        BuildScreen(catalog: new ContentCatalog(
            Array.Empty<CharacterSpec>(),
            Array.Empty<EnemySpec>(),
            new[] { AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath).ToSpec() }));

        _presenter.Open();

        Assert.That(_presenter.IsOpen, Is.True);

        Back().onClick.Invoke();

        Assert.That(_presenter.IsOpen, Is.False, "Back could not leave an empty screen.");
    }

    // ---- A Continue takes none of this ----------------------------------------------------------------

    [Test]
    public void Continue_DoesNotOpenTheScreen()
    {
        BuildScreen();

        var saved = new SavedRun();
        saved.Set(Snapshot(OathboundId));

        Menu(out _, out Button @continue, saved);

        @continue.onClick.Invoke();

        Assert.That(_presenter.IsOpen, Is.False, "a resume opened the class-select screen.");
        Assert.That(_loader.Asked, Is.EqualTo(new[] { SceneLoader.Run }), "the Run scene did not load.");
    }

    [Test]
    public void Continue_ResumesTheSavedClass()
    {
        BuildScreen();

        var saved = new SavedRun();
        saved.Set(Snapshot(GravecallerId));

        Menu(out _, out Button @continue, saved);

        @continue.onClick.Invoke();

        Assert.That(_pending.IsSet, Is.True);
        Assert.That(_pending.CharacterId.Value, Is.EqualTo(GravecallerId));
        Assert.That(_pending.Snapshot.HasValue, Is.True, "a Continue that is not a resume.");
        Assert.That(_presenter.IsOpen, Is.False);
    }

    /// <summary>
    /// The menu's other half did not move. <c>ResumeFlowTests</c> owns the assertions; this row owns
    /// the claim that they still exist.
    /// </summary>
    [Test]
    public void Menu_ContinueVisibilityIsUnchanged()
    {
        Type fixture = typeof(ClassSelectPresenterTests).Assembly
            .GetType("Soulvail.Tests.Game.Composition.ResumeFlowTests");

        Assert.That(fixture, Is.Not.Null, "ResumeFlowTests is gone.");

        foreach (string row in new[]
        {
            "Menu_ContinueHiddenWithNoSave",
            "Menu_ContinueShownWithASave",
            "Menu_VisibilityIsDecidedOnEveryEnable",
        })
        {
            MethodInfo method = fixture.GetMethod(
                row, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null, $"ResumeFlowTests.{row} was deleted rather than kept.");
            Assert.That(method.IsDefined(typeof(TestAttribute), false), Is.True, $"ResumeFlowTests.{row} is no longer a test.");
        }
    }

    // ---- The spec's description key ----------------------------------------------------------------------

    [Test]
    public void Spec_DescriptionKeyIsRequired()
    {
        ArgumentException thrown = Assert.Throws<ArgumentException>(
            () => new CharacterSpec(
                new ContentId(OathboundId),
                new LocKey("character.oathbound.name"),
                default,
                140f,
                new MovementSpec(3f, 0.06f, 0.08f, 720f),
                new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
                new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
                new FocusSpec(0.4f, 1f, 1.3f),
                new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f)));

        Assert.That(thrown.ParamName, Is.EqualTo("descriptionKey"));
        Assert.That(thrown.Message, Does.Contain("descriptionKey"));
        Assert.DoesNotThrow(() => Character(OathboundPath).ToSpec());
    }

    [Test]
    public void Assets_BothClassesCarryADescription()
    {
        CharacterSpec oathbound = Character(OathboundPath).ToSpec();
        CharacterSpec gravecaller = Character(GravecallerPath).ToSpec();

        Assert.That(oathbound.DescriptionKey.Key, Is.Not.Null);
        Assert.That(gravecaller.DescriptionKey.Key, Is.Not.Null);
        Assert.That(oathbound.DescriptionKey, Is.Not.EqualTo(gravecaller.DescriptionKey));

        var shipped = new TableLocalizer(EnglishTable());

        foreach (CharacterSpec spec in new[] { oathbound, gravecaller })
        {
            Assert.That(shipped.Has(spec.DescriptionKey), Is.True, $"English.asset has no row for {spec.DescriptionKey}.");
            Assert.That(shipped.Get(spec.DescriptionKey), Is.Not.Empty);
        }
    }

    // ---- Guard rows ---------------------------------------------------------------------------------------

    [Test]
    public void Construct_RefusesANullDependency()
    {
        LoadScreen();

        ContentCatalog catalog = Catalog();

        Assert.Throws<ArgumentNullException>(
            () => _presenter.Construct(null, catalog, _loader, Passthrough(), _profiles));
        Assert.Throws<ArgumentNullException>(
            () => _presenter.Construct(_pending, null, _loader, Passthrough(), _profiles));
        Assert.Throws<ArgumentNullException>(
            () => _presenter.Construct(_pending, catalog, null, Passthrough(), _profiles));
        Assert.Throws<ArgumentNullException>(
            () => _presenter.Construct(_pending, catalog, _loader, null, _profiles));
        Assert.Throws<ArgumentNullException>(
            () => _presenter.Construct(_pending, catalog, _loader, Passthrough(), null));
    }

    [Test]
    public void Start_UndressedScreen_NamesWhatIsMissing()
    {
        var bare = new GameObject("Bare", typeof(RectTransform));
        bare.SetActive(false);
        _spawned.Add(bare);

        ClassSelectPresenter presenter = bare.AddComponent<ClassSelectPresenter>();
        presenter.Construct(_pending, Catalog(), _loader, Passthrough(), _profiles);

        TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(
            () => typeof(ClassSelectPresenter).GetMethod("Start", Private).Invoke(presenter, null));

        Assert.That(thrown.InnerException, Is.TypeOf<MissingReferenceException>());
        Assert.That(thrown.InnerException.Message, Does.Contain("CanvasGroup"));
    }

    [Test]
    public void Page_Show_RefusesANullLocalizer()
    {
        BuildScreen();

        Assert.Throws<ArgumentNullException>(
            () => Page().Show(new ClassPageModel(Character(OathboundPath).ToSpec(), 0, 1, PageButton.Choose, 0, null, RoleKind.Melee), null));
    }

    // ---- ClassCard: still the Splash screen's, so its own rows stay --------------------------------------

    /// <summary>GD §14.3 working: a locked card's numbers are the numbers.</summary>
    [Test]
    public void Card_LockedAndOwnedAreTheSameNumbers()
    {
        ClassCard card = NewCard();
        CharacterSpec spec = Character(EmberwrightPath).ToSpec();

        card.BindLocked(spec, 3_500, false, default, Shipped(), _ => { });

        string[] locked = { CardText(card, "_hp"), CardText(card, "_speed"), CardText(card, "_weapon") };

        Assert.That(locked, Is.EqualTo(new[] { "70 HP", "3.4 m/s", "26 DPS" }), "the fixture's premise.");

        card.Bind(spec, Shipped(), _ => { });

        Assert.That(new[] { CardText(card, "_hp"), CardText(card, "_speed"), CardText(card, "_weapon") }, Is.EqualTo(locked));
    }

    [Test]
    public void Card_RepaintsBackToOwned()
    {
        ClassCard card = NewCard();
        CharacterSpec spec = Character(EmberwrightPath).ToSpec();

        int chosen = 0;
        int unlocks = 0;

        card.BindLocked(spec, 3_500, true, new LocKey("ui.classselect.locked.deed"), Passthrough(), _ => unlocks++);
        card.Bind(spec, Passthrough(), _ => chosen++);

        Assert.That(card.State, Is.EqualTo(ClassCardState.Owned));

        CardButton(card).onClick.Invoke();

        Assert.That(chosen, Is.EqualTo(1), "onChosen is not armed after the repaint.");
        Assert.That(unlocks, Is.Zero, "onUnlockTapped was not dropped by the repaint.");
    }

    [Test]
    public void Card_BoundTwiceReportsOnce()
    {
        ClassCard card = NewCard();
        CharacterSpec spec = Character(OathboundPath).ToSpec();

        var taps = 0;

        card.Bind(spec, Passthrough(), _ => taps++);
        card.Bind(spec, Passthrough(), _ => taps++);

        CardButton(card).onClick.Invoke();

        Assert.That(taps, Is.EqualTo(1), "the card accumulated a listener per binding.");
    }

    [Test]
    public void Card_RefusesANullArgument()
    {
        ClassCard card = NewCard();
        CharacterSpec spec = Character(EmberwrightPath).ToSpec();

        Assert.Throws<ArgumentNullException>(() => card.Bind(null, Passthrough(), _ => { }));
        Assert.Throws<ArgumentNullException>(() => card.Bind(spec, null, _ => { }));
        Assert.Throws<ArgumentNullException>(() => card.Bind(spec, Passthrough(), null));
        Assert.Throws<ArgumentNullException>(() => card.BindLocked(null, 1, true, default, Passthrough(), _ => { }));
        Assert.Throws<ArgumentNullException>(() => card.BindLocked(spec, 1, true, default, null, _ => { }));
        Assert.Throws<ArgumentNullException>(() => card.BindLocked(spec, 1, true, default, Passthrough(), null));
        Assert.Throws<ArgumentOutOfRangeException>(() => card.BindLocked(spec, 0, true, default, Passthrough(), _ => { }));
    }

    // ---- Fixture -----------------------------------------------------------------------------------------

    /// <summary>Instantiates the shipped prefab and injects it — the screen under test is the asset.</summary>
    private void BuildScreen(
        ContentCatalog catalog = null,
        ILocalizer localizer = null,
        PendingRun pending = null,
        SceneLoader loader = null,
        PlayerProfile? profile = null,
        ClassSelectRoster roster = null,
        CharacterPreview preview = null,
        CharacterLookBook looks = null)
    {
        LoadScreen();

        if (pending != null)
        {
            _pending = pending;
        }

        // Adopted rather than saved, so a row's write count starts at zero.
        _profiles.Adopt(profile ?? PlayerProfile.Default);

        // What MenuScope's RegisterComponent does. Start never runs in EditMode, so nothing else on
        // this object has fired.
        _presenter.Construct(
            _pending,
            catalog ?? Catalog(),
            loader ?? _loader,
            localizer ?? Passthrough(),
            _profiles,
            roster,
            preview,
            looks);
    }

    private void LoadScreen()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        _screen = Object.Instantiate(prefab);
        _spawned.Add(_screen);

        _presenter = _screen.GetComponent<ClassSelectPresenter>();

        Assert.That(_presenter, Is.Not.Null, "ClassSelectPresenter did not load off its own prefab (Traps §5).");
    }

    private CharacterPreview BuildPreview()
    {
        var stage = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(StagePath));
        _spawned.Add(stage);

        return stage.GetComponent<CharacterPreview>();
    }

    /// <summary>
    /// A menu wired to the screen this fixture built, injected by hand. Built inactive so adding the
    /// component cannot fire <c>OnEnable</c> before the fields it guards are assigned.
    /// </summary>
    private MenuPresenter Menu(out Button descend, out Button @continue, SavedRun saved = null)
    {
        var root = new GameObject("Menu");
        root.SetActive(false);
        _spawned.Add(root);

        MenuPresenter menu = root.AddComponent<MenuPresenter>();

        descend = NewButton("Descend", root);
        @continue = NewButton("Continue", root);

        Set(menu, "_descend", descend);
        Set(menu, "_continue", @continue);
        Set(menu, "_classSelect", _presenter);

        menu.Construct(_pending, saved ?? new SavedRun(), _loader, Passthrough());

        typeof(MenuPresenter).GetMethod("OnEnable", Private).Invoke(menu, null);

        return menu;
    }

    private Button NewButton(string name, GameObject parent)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        child.transform.SetParent(parent.transform, false);
        _spawned.Add(child);

        return child.AddComponent<Button>();
    }

    /// <summary>A bare <see cref="ClassCard"/> with the labels a card draws, wired by reflection.</summary>
    private ClassCard NewCard()
    {
        var root = new GameObject("Card", typeof(RectTransform));
        _spawned.Add(root);

        ClassCard card = root.AddComponent<ClassCard>();

        foreach (string field in new[] { "_name", "_description", "_hp", "_speed", "_weapon", "_price", "_deed" })
        {
            var label = new GameObject(field, typeof(RectTransform));
            label.transform.SetParent(root.transform, false);

            typeof(ClassCard).GetField(field, Private).SetValue(card, label.AddComponent<TextMeshProUGUI>());
        }

        var button = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        button.transform.SetParent(root.transform, false);

        typeof(ClassCard).GetField("_button", Private).SetValue(card, button.AddComponent<Button>());

        return card;
    }

    private static Button CardButton(ClassCard card) =>
        (Button)typeof(ClassCard).GetField("_button", Private).GetValue(card);

    private static string CardText(ClassCard card, string field) =>
        ((TMP_Text)typeof(ClassCard).GetField(field, Private).GetValue(card)).text;

    /// <summary>One drag from a press on <paramref name="onto"/>, ending <paramref name="travelled"/> pixels away.</summary>
    private static void Drag(CharacterPage page, GameObject onto, float deltaX, float travelled)
    {
        var press = new PointerEventData(null)
        {
            position = new Vector2(500f, 500f),
            pressPosition = new Vector2(500f, 500f),
            pointerPressRaycast = new RaycastResult { gameObject = onto },
        };

        page.OnBeginDrag(press);

        press.delta = new Vector2(deltaX, 0f);
        press.position = new Vector2(500f + travelled, 500f);

        page.OnDrag(press);
        page.OnEndDrag(press);
    }

    private void PressNext() => PageButtonField("_next").onClick.Invoke();

    private void PressPrevious() => PageButtonField("_previous").onClick.Invoke();

    /// <summary>Runs the presenter's <c>Update</c> — one frame, as far as the latch is concerned.</summary>
    private void Update() =>
        typeof(ClassSelectPresenter).GetMethod("Update", Private).Invoke(_presenter, null);

    private CharacterPage Page() =>
        (CharacterPage)typeof(ClassSelectPresenter).GetField("_page", Private).GetValue(_presenter);

    private Button PageButtonField(string field) =>
        (Button)typeof(CharacterPage).GetField(field, Private).GetValue(Page());

    private TMP_Text PageLabel(string field) =>
        (TMP_Text)typeof(CharacterPage).GetField(field, Private).GetValue(Page());

    private string PageText(string field) => PageLabel(field).text;

    private Image PageImage(string field) =>
        (Image)typeof(CharacterPage).GetField(field, Private).GetValue(Page());

    private RectTransform PageRect(string field) =>
        (RectTransform)typeof(CharacterPage).GetField(field, Private).GetValue(Page());

    private TMP_Text BalanceLabel() =>
        (TMP_Text)typeof(ClassSelectPresenter).GetField("_balance", Private).GetValue(_presenter);

    private Button Back() =>
        (Button)typeof(ClassSelectPresenter).GetField("_back", Private).GetValue(_presenter);

    private CanvasGroup Root() =>
        (CanvasGroup)typeof(ClassSelectPresenter).GetField("_root", Private).GetValue(_presenter);

    private static Rect WorldRect(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    /// <summary>A fresh v4 profile with <paramref name="shards"/> banked and nothing owned.</summary>
    private static PlayerProfile Holding(int shards) => PlayerProfile.Default.WithShards(shards);

    private static void Set(MenuPresenter menu, string field, Object value) =>
        typeof(MenuPresenter).GetField(field, Private).SetValue(menu, value);

    /// <summary>The shipped catalog: two classes, in the order BootScope lists them.</summary>
    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Character(OathboundPath).ToSpec(), Character(GravecallerPath).ToSpec() },
        new[] { AssetDatabase.LoadAssetAtPath<EnemyDefinition>(HuskPath).ToSpec() },
        new[] { AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath).ToSpec() });

    /// <summary>The shipped catalog as of RS-03c: all four classes, in BootScope's order.</summary>
    private static ContentCatalog ShippedCatalog() => new ContentCatalog(
        new[]
        {
            Character(OathboundPath).ToSpec(),
            Character(GravecallerPath).ToSpec(),
            Character(EmberwrightPath).ToSpec(),
            Character(RangerPath).ToSpec(),
        },
        new[] { AssetDatabase.LoadAssetAtPath<EnemyDefinition>(HuskPath).ToSpec() },
        new[] { AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath).ToSpec() });

    /// <summary>
    /// A catalog with <paramref name="classes"/> classes, ids <c>character.stand-in-N</c>: the
    /// shipped Oathbound re-identified, so the row is about the count and not a spec this file wrote.
    /// </summary>
    private static ContentCatalog CatalogOf(int classes)
    {
        CharacterSpec template = Character(OathboundPath).ToSpec();
        var specs = new CharacterSpec[classes];

        for (int i = 0; i < classes; i++)
        {
            specs[i] = new CharacterSpec(
                new ContentId($"character.stand-in-{i}"),
                template.NameKey,
                template.DescriptionKey,
                template.MaxHp,
                template.Movement,
                template.Targeting,
                template.Weapon,
                template.Focus,
                template.MovementSkill,
                template.Shield,
                template.HitIFrames,
                template.Minions);
        }

        return new ContentCatalog(
            specs,
            new[] { AssetDatabase.LoadAssetAtPath<EnemyDefinition>(HuskPath).ToSpec() },
            new[] { AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath).ToSpec() });
    }

    private static CharacterDefinition Character(string path)
    {
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

        Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {path}.");

        return definition;
    }

    private static LocalizationTable EnglishTable()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No LocalizationTable at {EnglishPath}.");

        return table;
    }

    /// <summary>The real adapter over an empty table: every key resolves to itself.</summary>
    private static ILocalizer Passthrough() =>
        new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>());

    /// <summary>The real adapter over the <em>shipped</em> table — what the word rows pin against.</summary>
    private static ILocalizer Shipped() => new TableLocalizer(EnglishTable());

    private static RunSnapshot Snapshot(string characterId) => new RunSnapshot(
        RunSnapshot.CurrentVersion,
        new ContentId(DescentId),
        new ContentId(characterId),
        seed: 4_242,
        stageIndex: 9,
        random: default,
        playerHp: 62f,
        playerShield: 9f,
        runTime: 412.5f,
        writtenAt: Instant,
        level: 1,
        xp: 0f,
        pendingLevelUps: 0,
        takenNodeIds: Array.Empty<ContentId>(),
        manualSkillIds: new ContentId[Soulvail.Core.Combat.SkillRunner.MaxManualSlots],
        default,
        Array.Empty<ContentId>(),
        Array.Empty<ContentId>(),
        Array.Empty<ContentId>());

    /// <summary>A loader that records rather than loading — <c>PausePresenterTests</c>' shape.</summary>
    private sealed class RecordingLoader : SceneLoader
    {
        public List<string> Asked { get; } = new List<string>();

        public override Task LoadAsync(string sceneName)
        {
            Asked.Add(sceneName);

            return Task.CompletedTask;
        }
    }

    /// <summary>A loader whose task does not complete, so a second tap arrives while the first is in flight.</summary>
    private sealed class SlowLoader : SceneLoader
    {
        private readonly TaskCompletionSource<bool> _gate = new TaskCompletionSource<bool>();

        public int Attempts { get; private set; }

        public override Task LoadAsync(string sceneName)
        {
            Attempts++;

            return _gate.Task;
        }

        public void Finish() => _gate.TrySetResult(true);
    }

    /// <summary>A loader that refuses — <c>RunEndPresenterTests</c>' shape.</summary>
    private sealed class RefusingLoader : SceneLoader
    {
        public const string Message = "the scene load was refused";

        public int Attempts { get; private set; }

        public override Task LoadAsync(string sceneName)
        {
            Attempts++;

            throw new InvalidOperationException(Message);
        }
    }
}
