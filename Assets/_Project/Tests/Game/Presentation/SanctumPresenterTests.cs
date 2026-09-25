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
using CoreVector3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// GD §13.3's Sanctum on screen: four priced rows, three refusals that read as prices, a banish
/// list, and one way out. M6-03a.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over a real <c>RunSession</c> that has really reached the Sanctum</b> — one Husk killed, the
/// clear beat waited out — which is <c>SplashPresenterTests</c>' shape one step further. The screen
/// owns no economy (rule 1), so every price and every refusal a row draws here is
/// <c>SanctumShop</c>'s answer through the port; a fixture that faked the port would be testing a
/// second shop.
/// </para>
/// <para>
/// <b>The screen under test is the shipped prefab</b>, for Traps §5's reason. <c>Start</c> and
/// <c>Update</c> do not run in EditMode, so the prefab's own alpha 0 is what leaves the screen down
/// and <see cref="Frame"/> lifts the tap latch by reflection.
/// </para>
/// <para>
/// <b>The pause is not tested here.</b> It is <c>RunTicker.SanctumPhase</c>'s, and the rows that hold
/// it are in <c>FrameOrderTests</c> (PlayMode), beside the level-up's. This fixture pins only that the
/// presenter cannot reach a <c>RunPause</c> at all.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SanctumPresenterTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/Sanctum.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string TreeId = "tree.oathbound";

    private const float MaxHp = 100f;
    private const float Hurt = 50f;
    private const float SomeRot = 10f;

    private const int Capacity = 32;
    private const int DeviceCap = 16;
    private const int ProjectileCapacity = 8;
    private const int HuskCost = 4;

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);
    private static readonly CoreVector3 Door = new CoreVector3(0f, 0f, 18f);

    /// <summary>The eighteen keys this screen draws — rule 3's four among them (ledger row 7), and M6-11d's fifth.</summary>
    private static readonly string[] ScreenKeys =
    {
        "ui.sanctum.title", "ui.sanctum.balance", "ui.sanctum.leave",
        "ui.sanctum.reroll", "ui.sanctum.reroll.detail",
        "ui.sanctum.banish", "ui.sanctum.banish.detail",
        "ui.sanctum.heal", "ui.sanctum.heal.detail",
        "ui.sanctum.cleanse", "ui.sanctum.cleanse.detail",
        "ui.sanctum.refused.short", "ui.sanctum.refused.full",
        "ui.sanctum.refused.clean", "ui.sanctum.refused.nothing",
        "ui.sanctum.refused.complete",
        "ui.sanctum.banish.prompt", "ui.sanctum.banish.cancel",
    };

    private readonly List<GameObject> _spawned = new List<GameObject>();

    private DomainEventHub _hub;
    private RecordingEvents _spy;
    private FixedRandom _random;
    private ContentCatalog _catalog;
    private RunSession _session;
    private RecordingPort _port;

    private GameObject _screen;
    private SanctumPresenter _presenter;

    [SetUp]
    public void CreateWorld()
    {
        _hub = new DomainEventHub();
        _spy = new RecordingEvents();
        _random = new FixedRandom(7, Enumerable.Range(0, 8_192).Select(i => i % 2 == 0 ? 0.25f : 0.75f).ToArray());
        _port = null;
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

    // ---- Rule 1: one event opens it -----------------------------------------------------------

    [Test]
    public void Sanctum_OpensOnTheEvent()
    {
        EnterSanctum(essence: 84, hp: Hurt, localizer: new TableLocalizer(EnglishTable()));

        Assert.That(_presenter.IsShown, Is.True);
        Assert.That(Rows().All(row => row.IsShown), Is.True, "four rows drawn.");
        Assert.That(Text(Field<TMP_Text>("_balance")), Is.EqualTo("84 Essence"));
        Assert.That(_spy.Of<SanctumOpened>().Single().Essence, Is.EqualTo(84), "the event's balance.");
    }

    /// <summary>
    /// The finding this task made: inside <c>SanctumOpened</c>'s own publish the port still reports
    /// the shop shut, so a draw there would open the Sanctum with every row dead.
    /// </summary>
    /// <remarks>
    /// <c>StageFlow</c> publishes the event from inside its tick and <c>RunSession</c> copies the
    /// phase onto <c>RunState.IsSanctumOpen</c> after that tick returns. The first half of this row
    /// pins that ordering so the day core moves the write, this row says the deferral can go; the
    /// second half is the deferral working.
    /// </remarks>
    [Test]
    public void Sanctum_TheFirstDrawWaitsForTheShopToOpen()
    {
        bool openAtPublish = true;
        bool canBuyAtPublish = true;

        CreateSession();

        using IDisposable probe = _hub.Subscribe<SanctumOpened>(_ =>
        {
            openAtPublish = _session.IsSanctumOpen;
            canBuyAtPublish = _session.CanBuy(SanctumService.Reroll);
        });

        BuildScreen();
        StartRun(essence: 500);
        KillTheHusk();
        TickUntilTheSanctum();

        Assert.That(openAtPublish, Is.False, "core now opens the shop before it announces it — the deferral can go.");
        Assert.That(canBuyAtPublish, Is.False);

        Assert.That(_presenter.IsShown, Is.False, "drawn inside the event, against a shop the port calls shut.");

        Frame();

        Assert.That(_presenter.IsShown, Is.True);
        Assert.That(Row("_reroll").IsAffordable, Is.True, "500 buys a reroll once the shop is open.");
    }

    [Test]
    public void Sanctum_DrawsTheFourPrices()
    {
        EnterSanctum(essence: 500, hp: Hurt, rerollsBought: 1);

        // PriceOf's answers, and each field holds the service it is named for.
        Assert.That(Price(Row("_reroll")), Is.EqualTo("50"), "25 doubled once.");
        Assert.That(Price(Row("_banish")), Is.EqualTo("40"));
        Assert.That(Price(Row("_heal")), Is.EqualTo("40"));
        Assert.That(Price(Row("_cleanse")), Is.EqualTo("60"));

        Assert.That(Row("_reroll").Service, Is.EqualTo(SanctumService.Reroll));
        Assert.That(Row("_banish").Service, Is.EqualTo(SanctumService.Banish));
        Assert.That(Row("_heal").Service, Is.EqualTo(SanctumService.Heal));
        Assert.That(Row("_cleanse").Service, Is.EqualTo(SanctumService.Cleanse));

        Assert.That(Rows().All(row => row.IsAffordable), Is.True, "500 buys any of them.");
    }

    // ---- Rule 3: a refusal is a price with a reason -------------------------------------------

    /// <remarks>
    /// <b>24 banked, not the spec's 39</b> — M6-02b's finding: 39 affords the 25 reroll. 24 is the
    /// largest balance that refuses all four.
    /// </remarks>
    [Test]
    public void Sanctum_ARowThatCannotBeAffordedIsDrawnAndDead()
    {
        EnterSanctum(essence: 24, hp: Hurt);

        foreach (ServiceRow row in Rows())
        {
            Assert.That(row.IsShown, Is.True, $"{row.Service} was hidden rather than refused.");
            Assert.That(row.IsAffordable, Is.False, $"{row.Service} is live on 24 Essence.");
            Assert.That(Detail(row), Is.EqualTo("ui.sanctum.refused.short"), $"{row.Service}'s reason.");
        }

        Assert.That(Rows().Select(Price), Is.EqualTo(new[] { "25", "40", "40", "60" }));
    }

    [Test]
    public void Sanctum_HealAtFullHealthSaysWhy()
    {
        EnterSanctum(essence: 500, hp: MaxHp);

        Assert.That(Row("_heal").IsAffordable, Is.False);
        Assert.That(Detail(Row("_heal")), Is.EqualTo("ui.sanctum.refused.full"));

        Assert.That(Row("_reroll").IsAffordable, Is.True);
        Assert.That(Row("_banish").IsAffordable, Is.True);
        Assert.That(Row("_cleanse").IsAffordable, Is.True);
    }

    [Test]
    public void Sanctum_CleanseAtZeroRotSaysWhy()
    {
        EnterSanctum(essence: 500, hp: Hurt, veilrot: 0f);

        Assert.That(Row("_cleanse").IsAffordable, Is.False);
        Assert.That(Detail(Row("_cleanse")), Is.EqualTo("ui.sanctum.refused.clean"));
    }

    [Test]
    public void Sanctum_BanishWithNothingLeftSaysWhy()
    {
        EnterSanctum(essence: 500, hp: Hurt, taken: 12);

        Assert.That(Row("_banish").IsAffordable, Is.False);
        Assert.That(Detail(Row("_banish")), Is.EqualTo("ui.sanctum.refused.nothing"));
    }

    /// <summary>M6-11d rule 3: the stage-37 Sanctum that sold five rerolls a full tree could never spend.</summary>
    [Test]
    public void Sanctum_AFinishedTreeSaysWhy()
    {
        EnterSanctum(essence: 500, hp: Hurt, taken: 12);

        ServiceRow reroll = Row("_reroll");

        Assert.That(reroll.IsShown, Is.True, "refused, not hidden.");
        Assert.That(reroll.IsAffordable, Is.False, "500 Essence, and nothing left for a charge to be spent on.");
        Assert.That(Detail(reroll), Is.EqualTo("ui.sanctum.refused.complete"));
        Assert.That(Price(reroll), Is.EqualTo("25"), "M5-08a rule 5: the price stays.");

        Assert.That(
            new TableLocalizer(EnglishTable()).Get(new LocKey("ui.sanctum.refused.complete")),
            Is.EqualTo("Nothing left to offer"));
    }

    [Test]
    public void Sanctum_ARefusalKeepsTheNameAndThePrice()
    {
        EnterSanctum(essence: 500, hp: MaxHp);

        ServiceRow heal = Row("_heal");

        Assert.That(heal.IsAffordable, Is.False, "the fixture's premise.");
        Assert.That(Name(heal), Is.EqualTo("ui.sanctum.heal"), "M5-08a rule 5: the name stays.");
        Assert.That(Price(heal), Is.EqualTo("40"), "and so does the price.");
    }

    [Test]
    public void Sanctum_ATapOnADeadRowSendsNothing()
    {
        EnterSanctum(essence: 500, hp: MaxHp);

        // onClick.Invoke would bypass interactable anyway; this goes one door further in.
        Assert.DoesNotThrow(() => InvokeRowTapped(SanctumService.Heal));

        Assert.That(_spy.Count<SanctumServiceBought>(), Is.Zero, "a refused Heal was bought.");
        Assert.That(_session.State.Essence, Is.EqualTo(500));
    }

    // ---- Rule 2: redrawn after every tap ------------------------------------------------------

    [Test]
    public void Sanctum_BuyingRedrawsEveryRow()
    {
        EnterSanctum(essence: 65, hp: Hurt, localizer: new TableLocalizer(EnglishTable()));

        Assert.That(Row("_cleanse").IsAffordable, Is.True, "the fixture's premise: 65 buys a Cleanse.");

        Tap(Row("_heal"));

        Assert.That(_session.State.Essence, Is.EqualTo(25));
        Assert.That(Text(Field<TMP_Text>("_balance")), Is.EqualTo("25 Essence"));
        Assert.That(Price(Row("_heal")), Is.EqualTo("40"), "Heal's price does not move.");

        // Nothing touched the Cleanse row, and it went dead: the redraw is every row, not the one tapped.
        Assert.That(Row("_cleanse").IsAffordable, Is.False);
        Assert.That(Detail(Row("_cleanse")), Is.EqualTo("Not enough Essence"));
    }

    [Test]
    public void Sanctum_ARerollRedrawsItsOwnPrice()
    {
        EnterSanctum(essence: 100, hp: Hurt);

        Tap(Row("_reroll"));

        Assert.That(Price(Row("_reroll")), Is.EqualTo("50"));
        Assert.That(_session.State.RerollsBought, Is.EqualTo(1));
    }

    // ---- Rules 6 and 12: straight down the port, once a frame ---------------------------------

    [Test]
    public void Sanctum_OneTapPerFrame()
    {
        EnterSanctum(essence: 500, hp: Hurt, recordPort: true);

        // Two different rows in one EventSystem pass — no Frame() between them.
        Tap(Row("_heal"));
        Tap(Row("_reroll"));

        Assert.That(_port.Log.Count(entry => entry.StartsWith("buy:", StringComparison.Ordinal)), Is.EqualTo(1));
        Assert.That(_spy.Count<SanctumServiceBought>(), Is.EqualTo(1));
    }

    [Test]
    public void Sanctum_LeavingClosesIt()
    {
        EnterSanctum(essence: 500, hp: Hurt, recordPort: true);

        TapButton(Field<Button>("_leave"));

        Assert.That(_port.Log, Is.EqualTo(new[] { "leave" }));
        Assert.That(_presenter.IsShown, Is.False);
        Assert.That(_session.IsSanctumOpen, Is.False, "core heard it on the tap.");

        Frame();

        TapButton(Field<Button>("_leave"));

        Assert.That(_port.Log, Is.EqualTo(new[] { "leave" }), "a closed screen sent a second Leave.");
    }

    /// <remarks>
    /// Each command is in the port's log the moment its tap returns, with no frame between — which
    /// is the only way it can arrive while <c>RunTicker.Tick</c> returns above <c>CommandPhase</c>.
    /// That the ticker <em>does</em> return there while the Sanctum holds the pause is
    /// <c>FrameOrderTests.Pause_TheSanctumHoldsIt</c>'s.
    /// </remarks>
    [Test]
    public void Sanctum_CommandsBypassTheCommandPhase()
    {
        EnterSanctum(essence: 500, hp: Hurt, recordPort: true);

        Tap(Row("_heal"));
        Assert.That(_port.Log.Last(), Is.EqualTo("buy:Heal"));

        Frame();
        Tap(Row("_banish"));
        Assert.That(_presenter.IsPicking, Is.True, "Banish opened the list rather than buying.");
        Assert.That(_port.Log.Count, Is.EqualTo(1), "the first tap of a banish sends nothing.");

        Frame();
        TapButton(PickerRows()[0]);
        Assert.That(_port.Log.Last(), Does.StartWith("banish:"));

        Frame();
        TapButton(Field<Button>("_leave"));
        Assert.That(_port.Log.Last(), Is.EqualTo("leave"));

        Assert.That(_port.Log.Count, Is.EqualTo(3));
    }

    // ---- Rule 9: full-screen, no tween --------------------------------------------------------

    [Test]
    public void Sanctum_CoversTheStickAndTheButtons()
    {
        EnterSanctum(essence: 500, hp: Hurt);

        var root = Field<CanvasGroup>("_root");
        var rect = (RectTransform)root.transform;

        Assert.That(rect.anchorMin, Is.EqualTo(Vector2.zero));
        Assert.That(rect.anchorMax, Is.EqualTo(Vector2.one));
        Assert.That(_screen.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));

        // Shown on the event's own call, with no frame for a tween to run in.
        Assert.That(root.alpha, Is.EqualTo(1f));
        Assert.That(root.blocksRaycasts, Is.True);

        // Covered rather than disabled: the presenter holds no reference to the HUD's controls.
        Type[] held = typeof(SanctumPresenter).GetFields(Private).Select(field => field.FieldType).ToArray();

        Assert.That(held, Has.No.Member(typeof(FloatingStick)));
        Assert.That(held, Has.No.Member(typeof(SkillButton)));

        TapButton(Field<Button>("_leave"));

        Assert.That(root.alpha, Is.EqualTo(0f));
        Assert.That(root.blocksRaycasts, Is.False);
    }

    // ---- Rule 7: Banish is two taps -----------------------------------------------------------

    [Test]
    public void Banish_OpensTheList()
    {
        EnterSanctum(essence: 500, hp: Hurt, taken: 3);

        Tap(Row("_banish"));

        Assert.That(_presenter.IsPicking, Is.True);
        Assert.That(Field<GameObject>("_servicePage").activeSelf, Is.False, "the four rows are veiled.");

        Button[] rows = PickerRows();

        Assert.That(rows.Count(row => row.gameObject.activeSelf), Is.EqualTo(9));

        // In tree order, by the node's own NameKey — the first untaken node is branch a tier 2.
        Assert.That(Text(PickerNames()[0]), Is.EqualTo($"{Taken(12)[3].Value}.name"));
    }

    [Test]
    public void Banish_PickingSpends()
    {
        EnterSanctum(essence: 500, hp: Hurt, taken: 3);

        Tap(Row("_banish"));
        Frame();

        ContentId expected = Taken(12)[3];

        TapButton(PickerRows()[0]);

        Assert.That(_session.State.IsNodeBanished(expected), Is.True, "that row's node, and no other.");
        Assert.That(_presenter.IsPicking, Is.False);
        Assert.That(Field<GameObject>("_servicePage").activeSelf, Is.True);
        Assert.That(_session.State.Essence, Is.EqualTo(460));
        Assert.That(_spy.Of<SanctumServiceBought>().Single().Service, Is.EqualTo(SanctumService.Banish));
    }

    [Test]
    public void Banish_CancelSpendsNothing()
    {
        EnterSanctum(essence: 500, hp: Hurt, taken: 3);

        Tap(Row("_banish"));
        Frame();

        TapButton(PickerField<Button>("_cancel"));

        Assert.That(_presenter.IsPicking, Is.False);
        Assert.That(_presenter.IsShown, Is.True);
        Assert.That(Field<GameObject>("_servicePage").activeSelf, Is.True);
        Assert.That(_session.State.Essence, Is.EqualTo(500));
        Assert.That(_spy.Count<SanctumServiceBought>(), Is.Zero);
    }

    [Test]
    public void Banish_DrawsAtMostItsCapacity()
    {
        CreateSession();
        LoadScreen();

        BanishPicker picker = Field<BanishPicker>("_picker");
        ContentId[] tree = Taken(12);
        ContentId[] longer = Enumerable.Range(0, picker.Capacity + 15).Select(i => tree[i % tree.Length]).ToArray();

        Assert.DoesNotThrow(
            () => picker.Open(longer, longer.Length, _catalog, Passthrough(), _ => { }, () => { }));

        Assert.That(
            picker.DrawnCount,
            Is.EqualTo(picker.Capacity),
            "A banish list longer than the prefab's rows draws the first Capacity and no more. CH §5's "
                + "full tree is 27 nodes and M7-04 authors it — that task gives this list a scroll.");
    }

    [Test]
    public void Banish_RowsPastTheCountAreHidden()
    {
        EnterSanctum(essence: 500, hp: Hurt, taken: 9);

        Tap(Row("_banish"));

        Button[] rows = PickerRows();

        Assert.That(rows.Length, Is.EqualTo(12));
        Assert.That(rows.Take(3).All(row => row.gameObject.activeSelf), Is.True);
        Assert.That(rows.Skip(3).All(row => !row.gameObject.activeSelf), Is.True, "never drawn empty.");
    }

    // ---- Rule 8: the picker is not the tree ---------------------------------------------------

    [Test]
    public void Picker_IsNotATreeCell()
    {
        const BindingFlags everything =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        Type[] referenced = typeof(BanishPicker).GetFields(everything).Select(field => field.FieldType)
            .Concat(typeof(BanishPicker).GetMethods(everything)
                .SelectMany(method => method.GetParameters()).Select(parameter => parameter.ParameterType))
            .ToArray();

        Assert.That(referenced, Has.No.Member(typeof(TreeNodeView)));
        Assert.That(referenced, Has.No.Member(typeof(TreeNodeView[])));

        Assert.That(
            typeof(TreeNodeView).GetFields(everything).Select(field => field.FieldType),
            Has.No.Member(typeof(Button)),
            "TreeNodeView grew a Button. CH §5.1's tree is not a screen you buy from (M3-09d rule 1).");
    }

    // ---- Rules 4 and 5: the pause is the ticker's ---------------------------------------------

    [Test]
    public void Pause_ThePresenterCannotReachIt()
    {
        MethodInfo construct = typeof(SanctumPresenter)
            .GetMethod(nameof(SanctumPresenter.Construct), BindingFlags.Instance | BindingFlags.Public);

        Assert.That(construct, Is.Not.Null);

        Assert.That(
            construct.GetParameters().Select(parameter => parameter.ParameterType),
            Has.No.Member(typeof(RunPause)),
            "The Sanctum screen was handed the pause. RunTicker.SanctumPhase raises and lowers it (rule 5).");

        Assert.That(
            typeof(SanctumPresenter)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Select(field => field.FieldType),
            Has.No.Member(typeof(RunPause)));
    }

    // ---- The prefab ---------------------------------------------------------------------------

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        var presenter = prefab.GetComponent<SanctumPresenter>();

        Assert.That(presenter, Is.Not.Null, "SanctumPresenter did not load off its own prefab (Traps §5).");

        ServiceRow[] rows =
        {
            Of<ServiceRow>(presenter, "_reroll"), Of<ServiceRow>(presenter, "_banish"),
            Of<ServiceRow>(presenter, "_heal"), Of<ServiceRow>(presenter, "_cleanse"),
        };

        Assert.That(rows, Has.All.Not.Null);
        Assert.That(rows.Distinct().Count(), Is.EqualTo(4), "two services share a row.");
        Assert.That(prefab.GetComponentsInChildren<ServiceRow>(true).Length, Is.EqualTo(4));

        Assert.That(Of<CanvasGroup>(presenter, "_root"), Is.Not.Null);
        Assert.That(Of<TMP_Text>(presenter, "_balance"), Is.Not.Null);
        Assert.That(Of<Button>(presenter, "_leave"), Is.Not.Null);

        var picker = Of<BanishPicker>(presenter, "_picker");

        Assert.That(picker, Is.Not.Null);
        Assert.That(picker.Capacity, Is.EqualTo(12), "TreeRules.Count for every tree this build ships.");
        Assert.That(picker.gameObject.activeSelf, Is.False, "the list ships closed.");

        Assert.That(Of<CanvasGroup>(presenter, "_root").alpha, Is.Zero, "the screen ships down.");
    }

    [Test]
    public void Prefab_CarriesNoSerializedColour()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assembly game = typeof(SanctumPresenter).Assembly;

        Type[] ours = prefab.GetComponentsInChildren<MonoBehaviour>(true)
            .Select(component => component.GetType())
            .Where(type => type.Assembly == game)
            .Distinct()
            .ToArray();

        Assert.That(ours, Has.Member(typeof(SanctumPresenter)));
        Assert.That(ours, Has.Member(typeof(ServiceRow)));
        Assert.That(ours, Has.Member(typeof(BanishPicker)));

        foreach (Type type in ours)
        {
            string[] colours = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(Color) || field.FieldType == typeof(Color[]))
                .Where(field => field.GetCustomAttribute<SerializeField>() is not null
                    || (field.IsPublic && field.GetCustomAttribute<NonSerializedAttribute>() is null))
                .Select(field => field.Name)
                .ToArray();

            Assert.That(colours, Is.Empty, $"{type.Name} carries a serialized Color; every colour is Palette's.");
        }
    }

    [Test]
    public void Prefab_DrawsNoRawEnglish()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var shipped = new TableLocalizer(EnglishTable());

        string[] authored = prefab.GetComponentsInChildren<TMP_Text>(true)
            .Select(label => label.text)
            .Where(text => !string.IsNullOrEmpty(text))
            .ToArray();

        Assert.That(
            authored,
            Is.EquivalentTo(new[]
            {
                "ui.sanctum.title", "ui.sanctum.leave", "ui.sanctum.banish.prompt", "ui.sanctum.banish.cancel",
            }),
            "every authored label is one of the screen's own keys, as its placeholder.");

        foreach (string key in authored)
        {
            Assert.That(shipped.Has(new LocKey(key)), Is.True, $"English.asset has no row for {key}.");
        }
    }

    // ---- Ledger row 7 -------------------------------------------------------------------------

    [Test]
    public void Strings_EveryKeyThisScreenDrawsHasARow()
    {
        var shipped = new TableLocalizer(EnglishTable());

        foreach (string key in ScreenKeys)
        {
            Assert.That(shipped.Has(new LocKey(key)), Is.True, $"English.asset has no row for {key}.");
        }

        Assert.That(shipped.Get(new LocKey("ui.sanctum.balance")), Does.Contain("{0}"));
    }

    [Test]
    public void Screen_ItsOwnLabelsAreWords()
    {
        CreateSession();
        BuildScreen(new TableLocalizer(EnglishTable()));
        StartRun(essence: 0);

        RunStart();

        Assert.That(Text(Field<TMP_Text>("_title")), Is.EqualTo("Sanctum"));
        Assert.That(Text(Field<TMP_Text>("_leaveLabel")), Is.EqualTo("Descend"));
        Assert.That(_presenter.IsShown, Is.False, "Start put the screen down.");
    }

    // ---- Guards, implied rather than listed ---------------------------------------------------

    [Test]
    public void Construct_RefusesANullDependency()
    {
        CreateSession();
        LoadScreen();

        ILocalizer localizer = Passthrough();

        Assert.Throws<ArgumentNullException>(() => _presenter.Construct(null, _session, _hub, _catalog, localizer));
        Assert.Throws<ArgumentNullException>(() => _presenter.Construct(_session, null, _hub, _catalog, localizer));
        Assert.Throws<ArgumentNullException>(() => _presenter.Construct(_session, _session, null, _catalog, localizer));
        Assert.Throws<ArgumentNullException>(() => _presenter.Construct(_session, _session, _hub, null, localizer));
        Assert.Throws<ArgumentNullException>(() => _presenter.Construct(_session, _session, _hub, _catalog, null));
    }

    [Test]
    public void Start_UndressedScreen_NamesWhatIsMissing()
    {
        CreateSession();

        var bare = new GameObject("Bare", typeof(RectTransform));
        bare.SetActive(false);
        _spawned.Add(bare);

        SanctumPresenter presenter = bare.AddComponent<SanctumPresenter>();
        presenter.Construct(_session, _session, _hub, _catalog, Passthrough());

        TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(
            () => typeof(SanctumPresenter).GetMethod("Start", Private).Invoke(presenter, null));

        Assert.That(thrown.InnerException, Is.TypeOf<MissingReferenceException>());
        Assert.That(thrown.InnerException.Message, Does.Contain(nameof(CanvasGroup)));
    }

    [Test]
    public void Screen_IgnoresTheShopWhenNoRunHasBegun()
    {
        CreateSession();
        BuildScreen();

        Assert.That(_session.State, Is.Null, "the fixture's premise.");

        Assert.DoesNotThrow(() => _hub.Publish(new SanctumOpened(1, 24)));
        Assert.DoesNotThrow(Frame);

        Assert.That(_presenter.IsShown, Is.False);
    }

    [Test]
    public void Row_Show_RefusesNulls()
    {
        CreateSession();
        LoadScreen();

        ServiceRow row = Row("_heal");

        Assert.Throws<ArgumentNullException>(
            () => row.Show(SanctumService.Heal, 40, true, new LocKey("ui.sanctum.heal.detail"), null, _ => { }));
        Assert.Throws<ArgumentNullException>(
            () => row.Show(SanctumService.Heal, 40, true, new LocKey("ui.sanctum.heal.detail"), Passthrough(), null));
    }

    [Test]
    public void Picker_Open_RefusesNulls()
    {
        CreateSession();
        LoadScreen();

        BanishPicker picker = Field<BanishPicker>("_picker");
        ContentId[] ids = Taken(12);
        ILocalizer localizer = Passthrough();

        Assert.Throws<ArgumentNullException>(() => picker.Open(null, 0, _catalog, localizer, _ => { }, () => { }));
        Assert.Throws<ArgumentNullException>(() => picker.Open(ids, 1, null, localizer, _ => { }, () => { }));
        Assert.Throws<ArgumentNullException>(() => picker.Open(ids, 1, _catalog, null, _ => { }, () => { }));
        Assert.Throws<ArgumentNullException>(() => picker.Open(ids, 1, _catalog, localizer, null, () => { }));
        Assert.Throws<ArgumentNullException>(() => picker.Open(ids, 1, _catalog, localizer, _ => { }, null));
    }

    // ---- Fixture ------------------------------------------------------------------------------

    /// <summary>
    /// A resumed stage-1 run with the given wallet, health and meter, walked into the Sanctum: the
    /// one Husk is killed and the clear beat waited out, so <c>SanctumOpened</c> comes from core.
    /// </summary>
    private void EnterSanctum(
        int essence,
        float hp,
        float veilrot = SomeRot,
        int rerollsBought = 0,
        int taken = 3,
        ILocalizer localizer = null,
        bool recordPort = false)
    {
        CreateSession();

        _port = recordPort ? new RecordingPort(_session) : null;

        BuildScreen(localizer);
        StartRun(essence, hp, veilrot, rerollsBought, taken);

        KillTheHusk();
        TickUntilTheSanctum();

        // The presenter's own next frame is where the opening draw lands — see
        // Sanctum_TheFirstDrawWaitsForTheShopToOpen.
        Frame();

        Assert.That(_presenter.IsShown, Is.True, "the Sanctum opened and the screen stayed down.");
    }

    private void TickUntilTheSanctum()
    {
        for (int i = 0; i < 1_200 && _spy.Count<SanctumOpened>() == 0; i++)
        {
            _session.Tick(Snapshot(CoreVector3.Zero));
        }

        Assert.That(_session.IsSanctumOpen, Is.True, "the fixture failed to reach the Sanctum.");
    }

    private void KillTheHusk()
    {
        for (int i = 0; i < 1_200 && _spy.Count<EnemySpawned>() == 0; i++)
        {
            _session.Tick(Snapshot(CoreVector3.Zero));
        }

        Assert.That(_spy.Count<EnemySpawned>(), Is.EqualTo(1), "the fixture failed to land the Husk.");

        EnemySpawned husk = _spy.Of<EnemySpawned>()[0];
        int[] report = { husk.Id };

        for (int i = 0; i < 1_200 && _spy.Count<EnemyDied>() == 0; i++)
        {
            _session.Tick(Snapshot(husk.Position));
            _session.ReportConeHits(report);
        }

        Assert.That(_spy.Count<EnemyDied>(), Is.EqualTo(1), "the fixture failed to kill the Husk.");
    }

    private void StartRun(int essence, float hp = Hurt, float veilrot = SomeRot, int rerollsBought = 0, int taken = 3)
    {
        _session.Start(new RunConfig(
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
                new RandomState(0, 0, 0, 0, 0),
                hp,
                0f,
                0f,
                Instant,
                taken + 1,
                0f,
                0,
                Taken(taken),
                new ContentId[SkillRunner.MaxManualSlots],
                new RunEconomy(essence, veilrot, rerollsBought, 0),
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>())));
    }

    private void CreateSession()
    {
        _catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Husk() },
            new[] { Mode() },
            TreeSkills(),
            new[] { Tree() });

        var events = new ForkedEvents(_hub, _spy);

        _session = new RunSession(
            _catalog,
            _random,
            events,
            new RecordingIntents(),
            new RunRecorder(_random, new FixedClock(Instant), events),
            Capacity,
            DeviceCap,
            ProjectileCapacity);
    }

    private void BuildScreen(ILocalizer localizer = null)
    {
        LoadScreen();

        IProgressionCommands progression = _port is null ? _session : _port;

        _presenter.Construct(_session, progression, _hub, _catalog, localizer ?? Passthrough());
    }

    private void LoadScreen()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        _screen = Object.Instantiate(prefab);
        _spawned.Add(_screen);

        _presenter = _screen.GetComponent<SanctumPresenter>();

        Assert.That(_presenter, Is.Not.Null, "SanctumPresenter did not load off its own prefab (Traps §5).");
    }

    private static WorldSnapshot Snapshot(CoreVector3 player) =>
        new WorldSnapshot(Capacity)
        {
            Dt = 1f / 60f,
            PlayerPosition = player,
            HasGate = true,
            GatePosition = Door,
            SpawnPoints = Enumerable.Range(0, 8)
                .Select(i => new CoreVector3(MathF.Cos(i * 0.785f) * 6f, 0f, MathF.Sin(i * 0.785f) * 6f))
                .ToArray(),
        };

    /// <summary>Runs the presenter's <c>Start</c>, which EditMode never does for it.</summary>
    private void RunStart() => typeof(SanctumPresenter).GetMethod("Start", Private).Invoke(_presenter, null);

    /// <summary>One frame of the screen's own <c>Update</c> — it lifts the tap latch and nothing else.</summary>
    private void Frame() => typeof(SanctumPresenter).GetMethod("Update", Private).Invoke(_presenter, null);

    private void InvokeRowTapped(SanctumService service) =>
        typeof(SanctumPresenter).GetMethod("OnRowTapped", Private).Invoke(_presenter, new object[] { service });

    private static void Tap(ServiceRow row) => TapButton(Of<Button>(row, "_button"));

    private static void TapButton(Button button) => button.onClick.Invoke();

    private ServiceRow Row(string field) => Field<ServiceRow>(field);

    private ServiceRow[] Rows() => new[] { Row("_reroll"), Row("_banish"), Row("_heal"), Row("_cleanse") };

    private Button[] PickerRows() => PickerField<Button[]>("_rows");

    private TMP_Text[] PickerNames() => PickerField<TMP_Text[]>("_names");

    private T PickerField<T>(string name) => Of<T>(Field<BanishPicker>("_picker"), name);

    private static string Name(ServiceRow row) => Text(Of<TMP_Text>(row, "_name"));

    private static string Price(ServiceRow row) => Text(Of<TMP_Text>(row, "_price"));

    private static string Detail(ServiceRow row) => Text(Of<TMP_Text>(row, "_detail"));

    private static string Text(TMP_Text label) => label == null ? null : label.text;

    private T Field<T>(string name) => Of<T>(_presenter, name);

    private static T Of<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Private).GetValue(target);

    private static LocalizationTable EnglishTable()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No LocalizationTable at {EnglishPath}.");

        return table;
    }

    /// <summary>The real adapter over an empty table: every key resolves to itself.</summary>
    private static ILocalizer Passthrough() =>
        new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>());

    // ---- Content ------------------------------------------------------------------------------

    /// <summary>All twelve nodes in an order the gating allows — branch by branch, tier 1 first.</summary>
    private static ContentId[] Taken(int count)
    {
        var all = new List<ContentId>();

        foreach (char branch in new[] { 'a', 'b', 'c' })
        {
            all.Add(Node(branch, 1, 'a'));
            all.Add(Node(branch, 1, 'b'));
            all.Add(Node(branch, 2, 'a'));
            all.Add(Node(branch, 2, 'b'));
        }

        return all.Take(count).ToArray();
    }

    /// <summary>
    /// M3-12's v1 shape: three branches of two tiers of two. Three taken is a1a, a1b and a2a, so the
    /// first banishable node in tree order is a2b — <c>Taken(12)[3]</c>.
    /// </summary>
    private static SkillTreeSpec Tree() => new SkillTreeSpec(
        new ContentId(TreeId),
        new ContentId(OathboundId),
        new[] { Branch('a'), Branch('b'), Branch('c') });

    private static SkillBranchSpec Branch(char letter) => new SkillBranchSpec(
        new LocKey($"tree.oathbound.{letter}"),
        new IReadOnlyList<ContentId>[]
        {
            new[] { Node(letter, 1, 'a'), Node(letter, 1, 'b') },
            new[] { Node(letter, 2, 'a'), Node(letter, 2, 'b') },
        });

    private static IReadOnlyList<SkillSpec> TreeSkills() =>
        Taken(12).Select(id => new SkillSpec(
            id,
            new LocKey($"{id.Value}.name"),
            new LocKey($"{id.Value}.desc"),
            SkillKind.Passive,
            new IEffect[] { new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f) }))
        .ToArray();

    private static ContentId Node(char branch, int tier, char slot) =>
        new ContentId($"skill.oathbound.{branch}{tier}{slot}");

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey($"{OathboundId}.name"),
        new LocKey($"{OathboundId}.description"),
        MaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));

    /// <summary>
    /// One Husk that dies to one swing and never lands a blow — a thirty-second wind-up — so the
    /// health a row is asked about is exactly the health the snapshot restored.
    /// </summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 10f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: HuskCost,
        xpValue: 0.5f,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 30f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    /// <summary>
    /// A stage of exactly one Husk, Descent's shop, and no Essence paid on the clear — so the balance
    /// a row is priced against is exactly the one the snapshot restored.
    /// </summary>
    private static ModeSpec Mode() => new ModeSpec(
        new ContentId(ModeId),
        new LocKey("mode.test.name"),
        1,
        true,
        0,
        new ScalingSpec(
            new BudgetCurve(HuskCost, 0f, 0f),
            new WaveCurve(1, 1000, 1, 1),
            new ConcurrencyCurve(DeviceCap, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0)),
        new XpCurve(20f, 12f, 1.4f),
        new[] { new RosterEntry(new ContentId(HuskId), 1) },
        overflow: new OverflowSpec(0.02f, 0.02f),
        sanctum: new SanctumSpec(25, 40, 40, 30f, 60, 15f));

    /// <summary>Publishes into the run's hub and into a recorder — <c>SplashPresenterTests</c>' fake.</summary>
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
            _second.Publish(evt);
            _first.Publish(evt);
        }
    }

    /// <summary>
    /// The real session's port, with every command the screen sends written down on the way through.
    /// </summary>
    private sealed class RecordingPort : IProgressionCommands
    {
        private readonly IProgressionCommands _inner;

        public RecordingPort(IProgressionCommands inner)
        {
            _inner = inner;
        }

        public List<string> Log { get; } = new List<string>();

        public bool IsLevelUpPending => _inner.IsLevelUpPending;

        public bool HasOffer => _inner.HasOffer;

        public bool IsSplashPending => _inner.IsSplashPending;

        public bool IsSplashOpen => _inner.IsSplashOpen;

        public bool IsSanctumOpen => _inner.IsSanctumOpen;

        public void OpenLevelUp() => _inner.OpenLevelUp();

        public void ChooseOffer(int index) => _inner.ChooseOffer(index);

        public void OpenSplash() => _inner.OpenSplash();

        public void ChooseSplash(ContentId characterId, int branch) => _inner.ChooseSplash(characterId, branch);

        public void LeaveSanctum()
        {
            Log.Add("leave");
            _inner.LeaveSanctum();
        }

        public int PriceOf(SanctumService service) => _inner.PriceOf(service);

        public bool CanBuy(SanctumService service) => _inner.CanBuy(service);

        public void Buy(SanctumService service)
        {
            Log.Add($"buy:{service}");
            _inner.Buy(service);
        }

        public void Banish(ContentId skillId)
        {
            Log.Add($"banish:{skillId.Value}");
            _inner.Banish(skillId);
        }

        public int BanishableInto(Span<ContentId> destination) => _inner.BanishableInto(destination);
    }
}
