using System;
using System.Collections.Generic;
using System.Globalization;
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
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// The screen that makes six merged tasks reachable: one card per authored class, and the tap that
/// decides which run the player gets. M5-07; CH §3, §6; GD §4.5.
/// </summary>
/// <remarks>
/// <para>
/// <b>The screen under test is the shipped asset</b> — <c>PausePresenterTests</c>' and
/// <c>LevelUpPresenterTests</c>' rule, for its reason: a fixture that built its own hierarchy would
/// go green over a <c>ClassSelect.prefab</c> dressed any way at all, and the cards being
/// <em>authored</em> is rule 3 rather than an implementation detail.
/// </para>
/// <para>
/// <b>The classes under test are the shipped assets too.</b> The numbers in these rows — 140 / 3.0 /
/// 39 against 80 / 3.1 / 36 — are <c>Oathbound.asset</c>'s and <c>Gravecaller.asset</c>'s, converted
/// through <c>ToSpec</c>. That is the comparison the screen exists to let a player make, so a row
/// asserting it against a catalog this file invented would assert nothing.
/// </para>
/// <para>
/// <b><c>Start</c> never runs in EditMode</b>, so the two static labels are unwritten unless a row
/// asks for them: <see cref="Start"/> invokes it by reflection, which is also what puts the screen
/// down the way a shipped build finds it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ClassSelectPresenterTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/ClassSelect.prefab";
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
    private const string DescentId = "mode.descent";

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// CH §3's whole roster and RS-03c's Ranger, which is what <c>ClassSelect.prefab</c> authors
    /// (rule 3).
    /// </summary>
    private const int AuthoredCards = 4;

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

    // ---- Rule 1: Descend opens the screen; the screen starts the run ----------------------------

    /// <summary>
    /// <c>Descend</c> stopped starting a run. It puts a screen up and writes nothing.
    /// </summary>
    [Test]
    public void Menu_DescendOpensTheScreen()
    {
        BuildScreen();

        MenuPresenter menu = Menu(out Button descend, out _);

        descend.onClick.Invoke();

        Assert.That(_presenter.IsOpen, Is.True, "Descend did not open the class-select screen.");

        Assert.That(
            _loader.Asked,
            Is.Empty,
            "Descend loaded a scene. The class is not known yet — that is the whole task.");

        Assert.That(
            _pending.IsSet,
            Is.False,
            "Descend wrote a pending run, so the screen it opened cannot change the class.");

        // And the menu itself is untouched: the screen is a full-screen CanvasGroup over it, so
        // MenuPresenter has no reason to switch its own buttons off (rule 9).
        Assert.That(menu, Is.Not.Null);
        Assert.That(descend.interactable, Is.True);
    }

    // ---- Rule 3: the cards are authored, and none is instantiated --------------------------------

    [Test]
    public void Select_DrawsACardPerAuthoredClass()
    {
        BuildScreen();

        _presenter.Open();

        IReadOnlyList<ClassCard> cards = Cards();

        // cards.Count rather than Has.Count: NUnit reflects for a Count *property* on the runtime
        // type, which is ClassCard[] and carries Length.
        Assert.That(
            cards.Count, Is.EqualTo(AuthoredCards), "the prefab no longer authors CH §3's roster.");

        Assert.That(cards[0].IsShown, Is.True);
        Assert.That(cards[0].CharacterId.Value, Is.EqualTo(OathboundId));

        Assert.That(cards[1].IsShown, Is.True);
        Assert.That(cards[1].CharacterId.Value, Is.EqualTo(GravecallerId));

        // The third is cleared and switched off rather than drawn empty — and it stops standing for
        // anything, so a tap that somehow reached it would report a default id to nobody.
        Assert.That(cards[2].IsShown, Is.False, "the third card is drawn for a class that does not exist.");
        Assert.That(cards[2].CharacterId.Value, Is.Null);
    }

    /// <summary>
    /// Rule 3's overflow: a warning, once, and the screen still opens.
    /// </summary>
    /// <remarks>
    /// <c>EnemyViews.WarnAboutCapacityOnce</c>'s rule, and the reason it is not a throw is blunter
    /// here than there: a menu that refused to open would be a build nobody could play.
    /// </remarks>
    [Test]
    public void Select_MoreClassesThanCardsWarnsOnce()
    {
        BuildScreen(catalog: CatalogOf(AuthoredCards + 1));

        LogAssert.Expect(LogType.Warning, new Regex("More authored classes \\(5\\)"));

        _presenter.Open();

        Assert.That(_presenter.IsOpen, Is.True, "the screen refused to open over a full roster.");

        IReadOnlyList<ClassCard> cards = Cards();

        for (int i = 0; i < cards.Count; i++)
        {
            Assert.That(cards[i].IsShown, Is.True, $"card {i} was not bound.");
        }

        // Twice, and the Console hears about it once: at the cap this is true every time the screen
        // goes up, and a warning per open would bury everything else.
        _presenter.Close();
        _presenter.Open();

        Assert.That(_presenter.IsOpen, Is.True);

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// Rule 3, measured: a hundred open/close cycles build nothing.
    /// </summary>
    /// <remarks>
    /// <b>Object identity and a transform census, not <c>AllocationAssert.None</c>.</b> The spec's
    /// Tests table asks for an allocation probe over the binding path, and that probe cannot pass
    /// and cannot be made to: a card writes five <c>TMP_Text</c>s, and composing <c>"140 HP"</c>
    /// allocates a string before TMP has been reached at all. Nothing in this project asserts a
    /// presenter <em>draw</em> allocation-free — every existing <c>AllocationAssert.None</c> is over
    /// pure computation (<c>ConeOverlapQuery.Query</c>, <c>SnapshotBuilder.Build</c>,
    /// <c>TableLocalizer.Get</c>) — so the honest reading of rule 3 is the one it actually states:
    /// <em>no card is instantiated</em>. That is a count of objects, and this row counts them.
    /// </remarks>
    [Test]
    public void Select_InstantiatesNothing()
    {
        BuildScreen();

        _presenter.Open();

        ClassCard[] before = Copy(Cards());
        int transformsBefore = _screen.GetComponentsInChildren<Transform>(true).Length;

        for (int i = 0; i < 100; i++)
        {
            _presenter.Close();
            _presenter.Open();
        }

        ClassCard[] after = Copy(Cards());

        Assert.That(after, Has.Length.EqualTo(before.Length));

        for (int i = 0; i < before.Length; i++)
        {
            Assert.That(
                after[i],
                Is.SameAs(before[i]),
                $"card {i} is a different object after 100 cycles, so the screen is building them.");
        }

        Assert.That(
            _screen.GetComponentsInChildren<Transform>(true).Length,
            Is.EqualTo(transformsBefore),
            "the screen grew objects. A screen that instantiates is a screen that hitches, at the "
                + "moment the player is about to enter a run (AR §14).");
    }

    // ---- Rule 4: a card draws five things and invents none of them -------------------------------

    [Test]
    public void Select_ACardDrawsItsClass()
    {
        BuildScreen(localizer: Shipped());

        _presenter.Open();

        ClassCard gravecaller = Cards()[1];

        Assert.That(gravecaller.CharacterId.Value, Is.EqualTo(GravecallerId));

        Assert.That(Text(gravecaller, "_name"), Is.EqualTo("Gravecaller"));

        // The sentence CH §3.2 is about, from the shipped table rather than from a fixture one.
        Assert.That(
            Text(gravecaller, "_description"),
            Does.Contain("you are not the damage"),
            "the Gravecaller's card does not say the one thing CH §3.2 says about it.");

        Assert.That(Text(gravecaller, "_hp"), Does.Contain("80"));
        Assert.That(Text(gravecaller, "_speed"), Does.Contain("3.1"));

        // 9 × 4.0. Four hits on a 36 HP Husk, which is M5-02 rule 2's whole argument.
        Assert.That(Text(gravecaller, "_weapon"), Does.Contain("36"));
    }

    /// <summary>The comparison the screen exists to let a player make.</summary>
    [Test]
    public void Select_TheTwoClassesDrawDifferentNumbers()
    {
        BuildScreen(localizer: Shipped());

        _presenter.Open();

        ClassCard oathbound = Cards()[0];
        ClassCard gravecaller = Cards()[1];

        Assert.That(Text(oathbound, "_name"), Is.EqualTo("Oathbound"));

        Assert.That(Text(oathbound, "_hp"), Does.Contain("140"));
        Assert.That(Text(oathbound, "_speed"), Does.Contain("3.0"));
        Assert.That(Text(oathbound, "_weapon"), Does.Contain("39"));

        Assert.That(Text(gravecaller, "_hp"), Does.Contain("80"));
        Assert.That(Text(gravecaller, "_speed"), Does.Contain("3.1"));
        Assert.That(Text(gravecaller, "_weapon"), Does.Contain("36"));

        // Three figures and nine columns: everything else that separates the two is the sentence.
        Assert.That(
            Text(oathbound, "_description"),
            Is.Not.EqualTo(Text(gravecaller, "_description")),
            "both cards draw the same sentence, so the description is not the class's.");
    }

    /// <summary>
    /// The card's arithmetic and the live weapon's agree — rule 4's reason for spelling it out.
    /// </summary>
    /// <remarks>
    /// <c>Weapon.DpsOneSecond</c> is <c>Damage.Value * FireRate.Value</c> over the live <c>Stat</c>s,
    /// which a menu has none of, so the card multiplies the two authored numbers instead. This is
    /// the row that stops the two spellings drifting.
    /// </remarks>
    [Test]
    public void Select_TheCardAndTheLiveWeaponAgree()
    {
        BuildScreen(localizer: Shipped());

        _presenter.Open();

        foreach ((int index, string path) in new[] { (0, OathboundPath), (1, GravecallerPath) })
        {
            CharacterSpec spec = Character(path).ToSpec();
            var live = new Soulvail.Core.Combat.Weapon(spec.Weapon);

            Assert.That(
                Text(Cards()[index], "_weapon"),
                Does.Contain(Mathf.Round(live.DpsOneSecond).ToString("0")),
                $"{path}: the card's figure and Weapon.DpsOneSecond have parted company.");
        }
    }

    // ---- Rules 1, 2: a tap writes the pending run ------------------------------------------------

    [Test]
    public void Select_ATapWritesThePendingRun()
    {
        // Owned, as a v3 profile migrated to v4 owns it: a fresh profile's Gravecaller is priced.
        BuildScreen(profile: Owning(GravecallerId));

        _presenter.Open();

        Tap(Cards()[1]);

        Assert.That(_pending.IsSet, Is.True, "the tap wrote nothing.");
        Assert.That(_pending.CharacterId.Value, Is.EqualTo(GravecallerId));

        // Rule 2: the mode is still the catalog's first, and nothing here invented a mode-select.
        Assert.That(_pending.ModeId.Value, Is.EqualTo(DescentId));

        Assert.That(_loader.Asked, Is.EqualTo(new[] { SceneLoader.Run }));

        // And a fresh run rather than a resume: the snapshot slot stays empty.
        Assert.That(_pending.Snapshot, Is.Null);
    }

    /// <summary>
    /// <c>MenuPresenter.Descend</c>'s double-tap guard, moved rather than copied.
    /// </summary>
    /// <remarks>
    /// Two <em>different</em> cards rather than the same one twice, which is the stronger claim:
    /// uGUI refuses a second touch on the button that was hit and says nothing about the one beside
    /// it, so a guard that lived on the button would let the Oathbound overwrite the Gravecaller
    /// between the tap and the scene coming in.
    /// </remarks>
    [Test]
    public void Select_ATapIsTakenOnce()
    {
        var pending = new PendingRun();
        var slow = new SlowLoader();

        BuildScreen(pending: pending, loader: slow, profile: Owning(GravecallerId));

        _presenter.Open();

        Tap(Cards()[1]);
        Tap(Cards()[0]);

        Assert.That(slow.Attempts, Is.EqualTo(1), "two taps asked for two scene loads.");

        Assert.That(
            pending.CharacterId.Value,
            Is.EqualTo(GravecallerId),
            "the second tap overwrote the first, so the player gets a class they did not choose.");

        // Every card, not just the one that was hit.
        foreach (ClassCard card in Cards())
        {
            if (card.IsShown)
            {
                Assert.That(card.IsInteractable, Is.False, "a card is still live under the load.");
            }
        }

        slow.Finish();
    }

    [Test]
    public void Select_AFailedLoadGivesTheButtonsBack()
    {
        var refusing = new RefusingLoader();

        BuildScreen(loader: refusing);

        _presenter.Open();

        LogAssert.Expect(LogType.Exception, new Regex(RefusingLoader.Message));

        Tap(Cards()[0]);

        Assert.That(_presenter.IsOpen, Is.True, "the screen went down over a load that never happened.");

        foreach (ClassCard card in Cards())
        {
            if (card.IsShown)
            {
                Assert.That(card.IsInteractable, Is.True, "a card is dead and the player is stranded.");
            }
        }

        // And the re-armed card really works, which the interactable flag alone does not say: the
        // latch has to have come down too.
        LogAssert.Expect(LogType.Exception, new Regex(RefusingLoader.Message));

        Tap(Cards()[0]);

        Assert.That(refusing.Attempts, Is.EqualTo(2));
    }

    // ---- Rule 7: Back writes nothing --------------------------------------------------------------

    [Test]
    public void Select_BackWritesNothing()
    {
        BuildScreen();

        _presenter.Open();

        Assert.That(_presenter.IsOpen, Is.True);

        Back().onClick.Invoke();

        Assert.That(_presenter.IsOpen, Is.False, "Back left the screen up.");
        Assert.That(Root().alpha, Is.EqualTo(0f), "the screen is still covering the menu.");
        Assert.That(Root().blocksRaycasts, Is.False, "the menu underneath still cannot be tapped.");

        Assert.That(
            _pending.IsSet,
            Is.False,
            "changing your mind started a run, which is the one thing Back must not do.");

        Assert.That(_loader.Asked, Is.Empty);
    }

    // ---- Rule 6: every authored class is selectable, and v3 is untouched -------------------------

    // ---- M6-09b rule 9: the starter is drawn exactly as M5-07 drew it ------------------------------

    /// <summary>
    /// Replaces M5-07's <c>Select_EveryCardIsSelectable</c>, retired by the task it named: every
    /// card is no longer selectable, and this row keeps the half of it that is still true.
    /// </summary>
    [Test]
    public void ClassSelect_TheStarterIsUnchanged()
    {
        BuildScreen(catalog: ShippedCatalog(), localizer: Shipped());

        _presenter.Open();

        ClassCard oathbound = Cards()[0];

        Assert.That(oathbound.State, Is.EqualTo(ClassCardState.Owned));
        Assert.That(oathbound.IsInteractable, Is.True, "a fresh profile cannot play the starter.");
        Assert.That(Text(oathbound, "_hp"), Is.EqualTo("140 HP"));
        Assert.That(Text(oathbound, "_speed"), Is.EqualTo("3.0 m/s"));
        Assert.That(Text(oathbound, "_weapon"), Is.EqualTo("39 DPS"));
        Assert.That(IsDrawn(oathbound, "_price"), Is.False);
        Assert.That(IsDrawn(oathbound, "_deed"), Is.False);

        // Bind's signature did not move: the locked card is a second overload, not a bool on this
        // one, so the starter's path through ClassCard is the one M5-07 shipped.
        MethodInfo bind = typeof(ClassCard).GetMethod(
            "Bind", new[] { typeof(CharacterSpec), typeof(ILocalizer), typeof(Action<ContentId>) });

        Assert.That(bind, Is.Not.Null, "ClassCard.Bind's M5-07 signature is gone.");
    }

    // ---- M6-09b rules 2, 3: a locked class is drawn, priced, and not played ------------------------

    [Test]
    public void ClassSelect_ALockedClassIsDrawnWithItsNumbers()
    {
        BuildScreen(catalog: ShippedCatalog(), localizer: Shipped());

        _presenter.Open();

        ClassCard emberwright = Cards()[2];

        Assert.That(emberwright.CharacterId.Value, Is.EqualTo(EmberwrightId));
        Assert.That(emberwright.State, Is.EqualTo(ClassCardState.Locked));
        Assert.That(emberwright.IsShown, Is.True, "a locked class was hidden rather than drawn.");
        Assert.That(Text(emberwright, "_name"), Is.EqualTo("Emberwright"));
        Assert.That(Text(emberwright, "_hp"), Is.EqualTo("70 HP"));
        Assert.That(Text(emberwright, "_speed"), Is.EqualTo("3.4 m/s"));

        // 17 × 1.5 = 25.5, drawn to the card's whole-number format rather than retyped here.
        Assert.That(
            Text(emberwright, "_weapon"),
            Is.EqualTo((17f * 1.5f).ToString("0", CultureInfo.InvariantCulture) + " DPS"));

        Assert.That(IsDrawn(emberwright, "_price"), Is.True);
        Assert.That(Text(emberwright, "_price"), Does.Contain("3500"));
    }

    [Test]
    public void ClassSelect_ALockedClassIsNotTappableToPlay()
    {
        BuildScreen(catalog: ShippedCatalog());

        _presenter.Open();

        Tap(Cards()[2]);

        Assert.That(_pending.IsSet, Is.False, "a locked class started a run.");
        Assert.That(_loader.Asked, Is.Empty);
        Assert.That(_store.ProfileWriteCount, Is.Zero, "a tap at 0 Shards spent something.");
    }

    [Test]
    public void ClassSelect_APriceThatCannotBePaidIsDead()
    {
        BuildScreen(catalog: ShippedCatalog(), profile: Holding(3_499), localizer: Shipped());

        _presenter.Open();

        ClassCard emberwright = Cards()[2];

        Assert.That(emberwright.IsInteractable, Is.False, "a price that cannot be paid is live.");
        Assert.That(IsDrawn(emberwright, "_price"), Is.True, "a dead price was hidden rather than drawn.");
        Assert.That(Text(emberwright, "_price"), Does.Contain("3500"));
        Assert.That(PriceColour(emberwright), Is.EqualTo(Palette.Neutral));
    }

    /// <summary>M5-08a rule 4's second door: a handler invoked by hand still asks <c>CanBuy</c>.</summary>
    [Test]
    public void ClassSelect_ATapOnADeadCardSendsNothing()
    {
        BuildScreen(catalog: ShippedCatalog(), profile: Holding(3_499));

        _presenter.Open();

        MethodInfo handler = typeof(ClassSelectPresenter).GetMethod("OnUnlockTapped", Private);

        Assert.DoesNotThrow(
            () => handler.Invoke(_presenter, new object[] { new ContentId(EmberwrightId) }),
            "an unaffordable purchase reached ProfileStore.Unlock and threw out of a click.");

        Assert.That(_store.ProfileWriteCount, Is.Zero);
        Assert.That(_profiles.Current.Shards, Is.EqualTo(3_499));
    }

    [Test]
    public void ClassSelect_APriceThatCanBePaidIsLive()
    {
        BuildScreen(catalog: ShippedCatalog(), profile: Holding(3_500), localizer: Shipped());

        _presenter.Open();

        ClassCard emberwright = Cards()[2];

        Assert.That(emberwright.State, Is.EqualTo(ClassCardState.Locked));
        Assert.That(emberwright.IsInteractable, Is.True);
        Assert.That(Text(emberwright, "_price"), Is.EqualTo("Unlock: 3500 Soul Shards"));
        Assert.That(PriceColour(emberwright), Is.EqualTo(Palette.Essence));
    }

    // ---- M6-09b rules 6, 7, 8: the purchase -----------------------------------------------------------

    [Test]
    public void ClassSelect_BuyingSpendsAndOwns()
    {
        BuildScreen(catalog: ShippedCatalog(), profile: Holding(3_500));

        _presenter.Open();

        Tap(Cards()[2]);

        Assert.That(_store.ProfileWriteCount, Is.EqualTo(1), "a purchase is one write.");
        Assert.That(_profiles.Current.Shards, Is.Zero);
        Assert.That(_profiles.Current.UnlockedCharacterIds, Is.EqualTo(new[] { new ContentId(EmberwrightId) }));
        Assert.That(_presenter.Shards, Is.Zero, "the balance was not redrawn after the purchase.");

        ClassCard emberwright = Cards()[2];

        Assert.That(emberwright.State, Is.EqualTo(ClassCardState.Owned));
        Assert.That(emberwright.IsInteractable, Is.True);
        Assert.That(IsDrawn(emberwright, "_price"), Is.False);
    }

    [Test]
    public void ClassSelect_BuyingDoesNotDescend()
    {
        BuildScreen(catalog: ShippedCatalog(), profile: Holding(3_500));

        _presenter.Open();

        Tap(Cards()[2]);

        Assert.That(_pending.IsSet, Is.False, "the tap that bought the class also started a run.");
        Assert.That(_loader.Asked, Is.Empty);

        // A later frame, and a second tap: now it plays.
        Update();
        Tap(Cards()[2]);

        Assert.That(_pending.CharacterId.Value, Is.EqualTo(EmberwrightId));
        Assert.That(_loader.Asked, Is.EqualTo(new[] { SceneLoader.Run }));
    }

    /// <summary>
    /// Two taps from one <c>EventSystem</c> pass: one purchase, and neither a second purchase nor a
    /// descent — rule 8's second latch.
    /// </summary>
    [Test]
    public void ClassSelect_OnePurchasePerFrame()
    {
        // Enough for both, so the refusal below is the latch and not the balance.
        BuildScreen(catalog: ShippedCatalog(), profile: Holding(5_500));

        _presenter.Open();

        Tap(Cards()[2]);
        Tap(Cards()[2]);
        Tap(Cards()[1]);

        Assert.That(_store.ProfileWriteCount, Is.EqualTo(1), "one pass bought twice.");
        Assert.That(_pending.IsSet, Is.False, "the second half of a double tap on a price started a run.");
        Assert.That(_loader.Asked, Is.Empty);

        // And the latch is a frame, not the screen: the next frame's tap buys.
        Update();
        Tap(Cards()[1]);

        Assert.That(_store.ProfileWriteCount, Is.EqualTo(2));
        Assert.That(_profiles.Current.Shards, Is.Zero);
    }

    [Test]
    public void ClassSelect_BuyingOneKillsTheOther()
    {
        BuildScreen(catalog: ShippedCatalog(), profile: Holding(3_600));

        _presenter.Open();

        Assert.That(Cards()[1].IsInteractable, Is.True, "the Gravecaller should be affordable at 3 600.");

        Tap(Cards()[2]);

        ClassCard gravecaller = Cards()[1];

        Assert.That(gravecaller.State, Is.EqualTo(ClassCardState.Locked));
        Assert.That(
            gravecaller.IsInteractable,
            Is.False,
            "100 Shards left and the Gravecaller's 2 000 still reads as affordable — one card was "
                + "redrawn rather than every card (rule 7).");
    }

    // ---- M6-09b rule 5: the balance ------------------------------------------------------------------

    [Test]
    public void ClassSelect_DrawsTheBalance()
    {
        BuildScreen(catalog: ShippedCatalog(), profile: Holding(650), localizer: Shipped());

        _presenter.Open();

        TMP_Text balance = BalanceLabel();

        Assert.That(balance.text, Is.EqualTo("650 Soul Shards"));
        Assert.That(balance.color, Is.EqualTo(Palette.Essence), "GD §16.4's reward gold.");
        Assert.That(_presenter.Shards, Is.EqualTo(650));
    }

    /// <summary>
    /// Rule 5, measured from the outside: the store moves under an open screen and 120 frames later
    /// the screen has not noticed.
    /// </summary>
    /// <remarks>
    /// <c>ProfileStore.Current</c> is a property on a sealed class, so a read cannot be counted
    /// without a seam this task has no other use for. A screen that polled would pick the new
    /// balance up on its first <c>Update</c>; this one must not.
    /// </remarks>
    [Test]
    public void ClassSelect_TheBalanceIsNotPolled()
    {
        BuildScreen(catalog: ShippedCatalog(), profile: Holding(650), localizer: Shipped());

        _presenter.Open();

        _profiles.Adopt(PlayerProfile.Default.WithShards(9_000));

        for (int i = 0; i < 120; i++)
        {
            Update();
        }

        Assert.That(_presenter.Shards, Is.EqualTo(650), "the screen re-read the profile on a frame.");
        Assert.That(BalanceLabel().text, Is.EqualTo("650 Soul Shards"));
        Assert.That(Cards()[2].IsInteractable, Is.False, "a card was redrawn on a frame.");
    }

    // ---- M6-09b rule 4, and the card's three states ----------------------------------------------

    [Test]
    public void Card_ADepthDeedDrawsItsLine()
    {
        BuildScreen(catalog: ShippedCatalog(), localizer: Shipped());

        _presenter.Open();

        ClassCard emberwright = Cards()[2];

        Assert.That(IsDrawn(emberwright, "_deed"), Is.True);
        Assert.That(Text(emberwright, "_deed"), Is.EqualTo("or reach stage 20"));
    }

    [Test]
    public void Card_ABossDeedDrawsNoLine()
    {
        BuildScreen(catalog: ShippedCatalog(), localizer: Shipped());

        _presenter.Open();

        ClassCard gravecaller = Cards()[1];

        Assert.That(gravecaller.State, Is.EqualTo(ClassCardState.Locked));
        Assert.That(IsDrawn(gravecaller, "_price"), Is.True);
        Assert.That(
            IsDrawn(gravecaller, "_deed"),
            Is.False,
            "the Gravecaller's card promises the Choirmother, which no mode authors until M7-03 — "
                + "the line arrives with the boss, not before it (M6-09b rule 4).");
    }

    [Test]
    public void Card_AnOwnedCardHasNoPriceOrDeed()
    {
        BuildScreen(catalog: ShippedCatalog(), profile: Owning(GravecallerId, EmberwrightId));

        _presenter.Open();

        foreach (ClassCard card in Cards())
        {
            Assert.That(card.State, Is.EqualTo(ClassCardState.Owned), card.CharacterId.ToString());
            Assert.That(IsDrawn(card, "_price"), Is.False, $"{card.CharacterId} is owned and priced.");
            Assert.That(IsDrawn(card, "_deed"), Is.False, $"{card.CharacterId} is owned and has a deed line.");
        }
    }

    /// <summary>GD §14.3 working: a locked card's numbers are the numbers.</summary>
    [Test]
    public void Card_LockedAndOwnedAreTheSameNumbers()
    {
        BuildScreen();

        ClassCard card = Cards()[2];
        CharacterSpec spec = Character(EmberwrightPath).ToSpec();

        // Through the shipped table since M6-10 made the three figures rows: over a pass-through
        // table both states would draw the same three keys, and this row would compare nothing.
        card.BindLocked(spec, 3_500, false, default, Shipped(), _ => { });

        string[] locked = { Text(card, "_hp"), Text(card, "_speed"), Text(card, "_weapon") };

        Assert.That(locked, Is.EqualTo(new[] { "70 HP", "3.4 m/s", "26 DPS" }), "the fixture's premise.");

        card.Bind(spec, Shipped(), _ => { });

        Assert.That(
            new[] { Text(card, "_hp"), Text(card, "_speed"), Text(card, "_weapon") },
            Is.EqualTo(locked));
    }

    [Test]
    public void Card_RepaintsBackToOwned()
    {
        BuildScreen();

        ClassCard card = Cards()[2];
        CharacterSpec spec = Character(EmberwrightPath).ToSpec();

        int chosen = 0;
        int unlocks = 0;

        card.BindLocked(spec, 3_500, true, new LocKey("ui.classselect.locked.deed"), Passthrough(), _ => unlocks++);

        Assert.That(IsDrawn(card, "_price"), Is.True);
        Assert.That(IsDrawn(card, "_deed"), Is.True);

        card.Bind(spec, Passthrough(), _ => chosen++);

        Assert.That(card.State, Is.EqualTo(ClassCardState.Owned));
        Assert.That(IsDrawn(card, "_price"), Is.False);
        Assert.That(IsDrawn(card, "_deed"), Is.False);

        Tap(card);

        Assert.That(chosen, Is.EqualTo(1), "onChosen is not armed after the repaint.");
        Assert.That(unlocks, Is.Zero, "onUnlockTapped was not dropped by the repaint.");
    }

    [Test]
    public void Card_ClearIsStillThird()
    {
        BuildScreen();

        _presenter.Open();

        Assert.That(Cards()[2].State, Is.EqualTo(ClassCardState.Hidden));
        Assert.That(Cards()[2].IsShown, Is.False);
    }

    // ---- M6-09b: the prefab and the strings ------------------------------------------------------

    [Test]
    public void Prefab_IsDressed()
    {
        LoadScreen();

        Assert.That(BalanceLabel(), Is.Not.Null, "the root has no balance label.");

        IReadOnlyList<ClassCard> cards = Cards();

        Assert.That(cards.Count, Is.EqualTo(AuthoredCards));

        for (int i = 0; i < cards.Count; i++)
        {
            Assert.That(Label(cards[i], "_price"), Is.Not.Null, $"card {i} has no price label.");
            Assert.That(Label(cards[i], "_deed"), Is.Not.Null, $"card {i} has no deed label.");
        }
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

    [Test]
    public void Strings_EveryKeyThisScreenDrawsHasARow()
    {
        var shipped = new TableLocalizer(EnglishTable());

        foreach (string key in new[]
        {
            "ui.classselect.balance", "ui.classselect.locked.price",
            "ui.classselect.locked.buy", "ui.classselect.locked.deed",
        })
        {
            Assert.That(shipped.Has(new LocKey(key)), Is.True, $"English.asset has no row for {key}.");
            Assert.That(shipped.Get(new LocKey(key)), Does.Contain("{0}"), $"{key} does not draw its number.");
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

    // ---- M6-09b guard rows ----------------------------------------------------------------------------

    [Test]
    public void BindLocked_RefusesANullArgument()
    {
        BuildScreen();

        ClassCard card = Cards()[2];
        CharacterSpec spec = Character(EmberwrightPath).ToSpec();

        Assert.Throws<ArgumentNullException>(() => card.BindLocked(null, 1, true, default, Passthrough(), _ => { }));
        Assert.Throws<ArgumentNullException>(() => card.BindLocked(spec, 1, true, default, null, _ => { }));
        Assert.Throws<ArgumentNullException>(() => card.BindLocked(spec, 1, true, default, Passthrough(), null));
        Assert.Throws<ArgumentOutOfRangeException>(() => card.BindLocked(spec, 0, true, default, Passthrough(), _ => { }));
    }

    [Test]
    public void Card_AMissingPriceOrDeedLabelIsSilent()
    {
        BuildScreen();

        ClassCard card = Cards()[2];

        typeof(ClassCard).GetField("_price", Private).SetValue(card, null);
        typeof(ClassCard).GetField("_deed", Private).SetValue(card, null);

        Assert.DoesNotThrow(() => card.BindLocked(
            Character(EmberwrightPath).ToSpec(), 3_500, true,
            new LocKey("ui.classselect.locked.deed"), Passthrough(), _ => { }));

        Assert.DoesNotThrow(() => card.Bind(Character(EmberwrightPath).ToSpec(), Passthrough(), _ => { }));
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

    // ---- Rule 7: a Continue takes none of this ---------------------------------------------------

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

        Assert.That(
            _pending.CharacterId.Value,
            Is.EqualTo(GravecallerId),
            "a resumed Gravecaller run came back as something else — the class came off a card "
                + "rather than off the snapshot (M2-14b rule 7).");

        Assert.That(_pending.Snapshot.HasValue, Is.True, "a Continue that is not a resume.");
        Assert.That(_presenter.IsOpen, Is.False);
    }

    /// <summary>
    /// The menu's other half did not move. <c>ResumeFlowTests</c> owns the assertions; this row owns
    /// the claim that they still exist.
    /// </summary>
    /// <remarks>
    /// <b>The spec names a row called <c>Menu_ContinueVisibilityIsUnchanged</c> in a fixture called
    /// <c>MenuPresenterTests</c>, and neither exists.</b> The menu's rows have lived in
    /// <c>ResumeFlowTests</c> since M3-07b, under three names. Copying their assertions here would
    /// make two fixtures that have to be kept in step; naming them is
    /// <c>TableLocalizerTests.RewrittenRows_StillAssertWhatTheyAsserted</c>'s device, and it goes red
    /// the day one is deleted rather than rewritten — which is the whole risk when a task changes
    /// <c>MenuPresenter</c>'s constructor and its <c>OnEnable</c>.
    /// </remarks>
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

            Assert.That(
                method.IsDefined(typeof(TestAttribute), false),
                Is.True,
                $"ResumeFlowTests.{row} is no longer a test.");
        }
    }

    // ---- Rule 5: the description key is required --------------------------------------------------

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

        // The field by name, because the caller cannot act on anything else: a card with a blank
        // half names no asset unless the message does.
        Assert.That(thrown.ParamName, Is.EqualTo("descriptionKey"));
        Assert.That(thrown.Message, Does.Contain("descriptionKey"));

        // And the name key is deliberately not guarded beside it — rule 5's asymmetry, stated here
        // so that adding a guard is a decision rather than a tidy-up.
        Assert.DoesNotThrow(() => Character(OathboundPath).ToSpec());
    }

    // ---- Rules 5, 8: both shipped classes carry one, and it resolves -----------------------------

    [Test]
    public void Assets_BothClassesCarryADescription()
    {
        CharacterSpec oathbound = Character(OathboundPath).ToSpec();
        CharacterSpec gravecaller = Character(GravecallerPath).ToSpec();

        Assert.That(oathbound.DescriptionKey.Key, Is.Not.Null);
        Assert.That(gravecaller.DescriptionKey.Key, Is.Not.Null);

        Assert.That(
            oathbound.DescriptionKey,
            Is.Not.EqualTo(gravecaller.DescriptionKey),
            "both classes point at one sentence, so the card cannot tell them apart.");

        var shipped = new TableLocalizer(EnglishTable());

        foreach (CharacterSpec spec in new[] { oathbound, gravecaller })
        {
            Assert.That(
                shipped.Has(spec.DescriptionKey),
                Is.True,
                $"English.asset has no row for {spec.DescriptionKey}, so {spec.Id} draws its own key.");

            Assert.That(
                shipped.Get(spec.DescriptionKey),
                Is.Not.Empty,
                $"{spec.DescriptionKey} resolves to an empty sentence.");
        }
    }

    // ---- Rule 8: no raw English on the prefab -----------------------------------------------------

    /// <summary>
    /// Every authored label on the prefab is a key <c>English.asset</c> answers, or it is empty.
    /// </summary>
    /// <remarks>
    /// <b><c>StaticLabelWiringTests</c>' claim rather than <c>TableLocalizerTests</c>' — the two are
    /// different assertions and this screen needs both halves.</b> A card's name and description are
    /// content-driven and are therefore authored <em>empty</em>, exactly as <c>LevelUp.prefab</c>'s
    /// <c>OfferCard</c> labels are; the two labels the screen owns carry their key as placeholder
    /// text, which is <c>RunEnd.prefab</c>'s pattern and what makes an undressed field visible in the
    /// Editor rather than blank. So the rule here is: nothing is a sentence somebody typed.
    /// </remarks>
    [Test]
    public void Select_DrawsNoRawEnglish()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        var shipped = new TableLocalizer(EnglishTable());
        TMP_Text[] labels = prefab.GetComponentsInChildren<TMP_Text>(true);

        Assert.That(labels, Is.Not.Empty, "the prefab has no TMP_Text at all, so this row tests nothing.");

        var authored = new List<string>();

        foreach (TMP_Text label in labels)
        {
            if (string.IsNullOrEmpty(label.text))
            {
                continue;
            }

            Assert.That(
                shipped.Has(new LocKey(label.text)),
                Is.True,
                $"'{label.text}' is typed into {PrefabPath} and English.asset does not answer it. "
                    + "AR §11.5: no raw user-facing string anywhere.");

            authored.Add(label.text);
        }

        // And the keys that are there are the two the presenter's own fields claim — not a third
        // somebody dropped on a card, which would draw a fixed string for every class.
        Assert.That(
            authored,
            Is.EquivalentTo(new[] { "ui.classselect.title", "ui.classselect.back" }),
            "a label on this prefab draws a key the screen does not own.");
    }

    /// <summary>Rule 8's four keys, in the shipped table.</summary>
    [Test]
    public void Select_EveryKeyItDrawsResolves()
    {
        var shipped = new TableLocalizer(EnglishTable());

        foreach (string key in new[]
        {
            "ui.classselect.title", "ui.classselect.back",
            "character.oathbound.name", "character.oathbound.description",
            "character.gravecaller.name", "character.gravecaller.description",
        })
        {
            Assert.That(shipped.Has(new LocKey(key)), Is.True, $"English.asset has no row for {key}.");
        }
    }

    /// <summary>The two static labels are words by the time the screen is on.</summary>
    [Test]
    public void Select_ItsOwnTwoLabelsAreWords()
    {
        BuildScreen(localizer: Shipped());

        Start();

        Assert.That(Label("_title"), Is.EqualTo("Choose your class"));
        Assert.That(Label("_backLabel"), Is.EqualTo("Back"));
    }

    // ---- Guard rows, implied rather than listed ---------------------------------------------------

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

    /// <summary>
    /// An undressed screen is named, the way <c>MenuPresenter</c> names an unassigned <c>Button</c>.
    /// </summary>
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
    public void Bind_RefusesANullArgument()
    {
        BuildScreen();

        ClassCard card = Cards()[0];
        CharacterSpec spec = Character(OathboundPath).ToSpec();

        Assert.Throws<ArgumentNullException>(() => card.Bind(null, Passthrough(), _ => { }));
        Assert.Throws<ArgumentNullException>(() => card.Bind(spec, null, _ => { }));
        Assert.Throws<ArgumentNullException>(() => card.Bind(spec, Passthrough(), null));
    }

    /// <summary>
    /// A card bound twice reports one tap, not two — the re-armed listener, which would otherwise
    /// start two runs from one thumb.
    /// </summary>
    [Test]
    public void Card_BoundTwiceReportsOnce()
    {
        BuildScreen();

        ClassCard card = Cards()[0];
        CharacterSpec spec = Character(OathboundPath).ToSpec();

        var taps = 0;

        card.Bind(spec, Passthrough(), _ => taps++);
        card.Bind(spec, Passthrough(), _ => taps++);

        Tap(card);

        Assert.That(taps, Is.EqualTo(1), "the card accumulated a listener per binding.");
    }

    // ---- Fixture -----------------------------------------------------------------------------------

    /// <summary>Instantiates the shipped prefab and injects it — the screen under test is the asset.</summary>
    private void BuildScreen(
        ContentCatalog catalog = null,
        ILocalizer localizer = null,
        PendingRun pending = null,
        SceneLoader loader = null,
        PlayerProfile? profile = null)
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
            _profiles);
    }

    private void LoadScreen()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        _screen = Object.Instantiate(prefab);
        _spawned.Add(_screen);

        _presenter = _screen.GetComponent<ClassSelectPresenter>();

        Assert.That(
            _presenter,
            Is.Not.Null,
            "ClassSelectPresenter did not load off its own prefab (Traps §5).");
    }

    /// <summary>
    /// A menu wired to the screen this fixture built, injected by hand.
    /// </summary>
    /// <remarks>
    /// Built inactive so adding the component cannot fire <c>OnEnable</c> before the fields it
    /// guards are assigned — <c>ResumeFlowTests.Menu</c>'s reason, and <c>OnEnable</c> is then
    /// invoked explicitly, which is what wires the two buttons.
    /// </remarks>
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

    /// <summary>Runs the presenter's <c>Start</c>, which EditMode never does for it.</summary>
    private void Start() =>
        typeof(ClassSelectPresenter).GetMethod("Start", Private).Invoke(_presenter, null);

    /// <summary>Runs the presenter's <c>Update</c> — one frame, as far as the latch is concerned.</summary>
    private void Update() =>
        typeof(ClassSelectPresenter).GetMethod("Update", Private).Invoke(_presenter, null);

    private static void Tap(ClassCard card) => Button(card).onClick.Invoke();

    private static TMP_Text Label(ClassCard card, string field) =>
        (TMP_Text)typeof(ClassCard).GetField(field, Private).GetValue(card);

    /// <summary>Whether a card's line is on screen: its object is active and it says something.</summary>
    private static bool IsDrawn(ClassCard card, string field)
    {
        TMP_Text label = Label(card, field);

        return label != null && label.gameObject.activeSelf && !string.IsNullOrEmpty(label.text);
    }

    private static Color PriceColour(ClassCard card) => Label(card, "_price").color;

    private TMP_Text BalanceLabel() =>
        (TMP_Text)typeof(ClassSelectPresenter).GetField("_balance", Private).GetValue(_presenter);

    /// <summary>A fresh v4 profile with <paramref name="shards"/> banked and nothing owned.</summary>
    private static PlayerProfile Holding(int shards) => PlayerProfile.Default.WithShards(shards);

    /// <summary>A fresh v4 profile that owns <paramref name="ids"/>.</summary>
    private static PlayerProfile Owning(params string[] ids)
    {
        var owned = new ContentId[ids.Length];

        for (int i = 0; i < ids.Length; i++)
        {
            owned[i] = new ContentId(ids[i]);
        }

        return PlayerProfile.Default.WithUnlocked(owned);
    }

    private static Button Button(ClassCard card) =>
        (Button)typeof(ClassCard).GetField("_button", Private).GetValue(card);

    private static string Text(ClassCard card, string field) =>
        ((TMP_Text)typeof(ClassCard).GetField(field, Private).GetValue(card)).text;

    private string Label(string field) =>
        ((TMP_Text)typeof(ClassSelectPresenter).GetField(field, Private).GetValue(_presenter)).text;

    private Button Back() =>
        (Button)typeof(ClassSelectPresenter).GetField("_back", Private).GetValue(_presenter);

    private CanvasGroup Root() =>
        (CanvasGroup)typeof(ClassSelectPresenter).GetField("_root", Private).GetValue(_presenter);

    private IReadOnlyList<ClassCard> Cards() =>
        (ClassCard[])typeof(ClassSelectPresenter).GetField("_cards", Private).GetValue(_presenter);

    /// <summary>
    /// A copy of the live array, so a before-and-after comparison compares the cards rather than the
    /// one array object they both sit in.
    /// </summary>
    private static ClassCard[] Copy(IReadOnlyList<ClassCard> cards)
    {
        var copy = new ClassCard[cards.Count];

        for (int i = 0; i < cards.Count; i++)
        {
            copy[i] = cards[i];
        }

        return copy;
    }

    private static void Set(MenuPresenter menu, string field, Object value) =>
        typeof(MenuPresenter).GetField(field, Private).SetValue(menu, value);

    /// <summary>The shipped catalog: two classes, in the order BootScope lists them.</summary>
    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Character(OathboundPath).ToSpec(), Character(GravecallerPath).ToSpec() },
        new[] { AssetDatabase.LoadAssetAtPath<EnemyDefinition>(HuskPath).ToSpec() },
        new[] { AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath).ToSpec() });

    /// <summary>
    /// The shipped catalog as of RS-03c: all four classes, in BootScope's order, so the prefab's four
    /// cards are all bound.
    /// </summary>
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
    /// A catalog with <paramref name="classes"/> classes, for the capacity row only.
    /// </summary>
    /// <remarks>
    /// The shipped Oathbound re-identified rather than a spec this file wrote from scratch: what the
    /// row is about is the <em>count</em>, and a hand-built spec would be one more thing to keep in
    /// step with <c>Oathbound.asset</c> for no gain.
    /// </remarks>
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

    /// <summary>
    /// A loader whose task does not complete, so a second tap arrives while the first is in flight.
    /// </summary>
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
