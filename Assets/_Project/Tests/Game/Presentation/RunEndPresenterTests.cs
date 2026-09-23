using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// The run-end screen: which signal opens it, what it draws, the exit, and the overlay it replaces.
/// </summary>
/// <remarks>
/// <para>
/// <b>The screen under test is the shipped prefab</b>, instantiated per row —
/// <c>PausePresenterTests</c>' shape, for its reason. Hand-building a hierarchy would test a second
/// screen that happens to resemble the asset, and the failure Traps §5 describes — a component in a
/// file-scoped namespace that deserialises as null with nothing reporting it — is invisible to a
/// fixture that never loads the asset.
/// </para>
/// <para>
/// <b>No <c>RunSession</c> anywhere in this file, and that is the point rather than a shortcut.</b>
/// This screen renders one event and reads no run state at all (rule 8): every figure it draws rides
/// on <c>ShardsAwarded</c>, so a fixture that built a run would be building something the class
/// cannot reach. The hub is real, because the claim rule 1 makes is about which events reach this
/// component and which do not.
/// </para>
/// <para>
/// <b>The loader records instead of loading.</b> <c>SceneManager.LoadSceneAsync</c> cannot be driven
/// from an EditMode test without taking the Editor's open scene with it, which is why
/// <c>PausePresenterTests</c> records too — and why <see cref="Screen_RearmsAfterAFailedLoad"/> can
/// have a loader that refuses at all.
/// </para>
/// <para>
/// <b>Two rows are about <c>HudPresenter</c> and one is about <c>RunScope</c>, and all three are
/// here on purpose.</b> There is no <c>HudPresenterTests</c> in this project and never has been —
/// <c>TableLocalizerTests</c>' own remarks say so — and the claim those rows make is not about the
/// HUD but about <em>this</em> screen having taken the death path off it (rule 2). The scope row is
/// here rather than in <c>InstallerTests</c> because that fixture's remarks open with
/// <em>"no <c>LifetimeScope</c> MonoBehaviours anywhere here"</em>, and rule 7's claim is about the
/// MonoBehaviour rather than about an installer.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RunEndPresenterTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/RunEnd.prefab";
    private const string HudPrefabPath = "Assets/_Project/Prefabs/UI/Hud.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";

    /// <summary>The five words this screen draws, and the keys they are drawn from (rules 3, 5).</summary>
    private static readonly (string Key, string Word)[] Rows =
    {
        ("ui.death.title", "You died"),
        ("ui.death.hint", "Tap to return"),
        ("ui.runend.depth", "Depth"),
        ("ui.runend.bosses", "Bosses"),
        ("ui.runend.shards", "Soul Shards"),
    };

    private const BindingFlags Everything =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<Object> _created = new List<Object>();

    private DomainEventHub _hub;
    private RecordingLoader _loader;
    private GameObject _screen;
    private RunEndPresenter _presenter;

    [SetUp]
    public void BuildWorld()
    {
        _hub = new DomainEventHub();
        _loader = new RecordingLoader();
    }

    [TearDown]
    public void DestroyWorld()
    {
        _hub?.Dispose();

        foreach (GameObject spawned in _spawned)
        {
            if (spawned != null)
            {
                Object.DestroyImmediate(spawned);
            }
        }

        _spawned.Clear();

        foreach (Object created in _created)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        _created.Clear();
    }

    // ---- Which signal opens it (rule 1) ------------------------------------------------------------

    [Test]
    public void Screen_IsHiddenUntilTheRunEnds()
    {
        BuildScreen();

        Assert.That(_presenter.IsShown, Is.False, "the screen is up over a run nobody has lost yet.");

        CanvasGroup root = Root();

        Assert.That(root.alpha, Is.Zero);

        // **And it takes no touches**, which is the half a fixture that only read the alpha would
        // miss: this canvas sorts above the whole HUD, so a root left at alpha 0 with its raycasts on
        // is invisible and still eats every tap on the stick and the Charge button.
        Assert.That(root.blocksRaycasts, Is.False, "an invisible screen is eating the arena's touches.");
        Assert.That(root.interactable, Is.False);
    }

    [Test]
    public void Screen_OpensOnShardsAwarded()
    {
        BuildScreen();

        _hub.Publish(new ShardsAwarded(220, 12, 2));

        Assert.That(_presenter.IsShown, Is.True, "ShardsAwarded did not open the screen.");
        Assert.That(Root().blocksRaycasts, Is.True, "the screen is up and the button cannot be hit.");

        // The three numbers, in the order a player reads them. They come off the event rather than
        // out of RunState, which is what the event carries them for.
        Assert.That(Text("_depth"), Is.EqualTo("12"));
        Assert.That(Text("_bosses"), Is.EqualTo("2"));
        Assert.That(Text("_shards"), Is.EqualTo("220"));
    }

    /// <summary>Rule 1's first refusal, asserted rather than described.</summary>
    /// <remarks>
    /// <c>RunEnded</c> fires whenever <c>RunScope</c> is torn down, which is every ordinary exit from
    /// the Run scene — quitting through the pause panel, a scene reload, the Editor leaving Play. A
    /// screen hung off it would appear over a scene that is already unloading, which is why
    /// <c>SaveWriter</c>, <c>HudPresenter</c> and <c>PausePresenter</c> have each refused it.
    /// </remarks>
    [Test]
    public void Screen_IgnoresRunEnded()
    {
        BuildScreen();

        _hub.Publish(new RunEnded(90f));

        Assert.That(_presenter.IsShown, Is.False, "a torn-down scope opened the run-end screen.");
        Assert.That(_hub.SubscriberCount<RunEnded>(), Is.Zero, "this screen subscribed to RunEnded.");
    }

    /// <summary>
    /// Rule 1's second refusal, and the new half of the ruling.
    /// </summary>
    /// <remarks>
    /// <c>PlayerDied</c> is published one line earlier than <c>ShardsAwarded</c> and carries no
    /// number, so a screen opened on it would be up for a frame showing three empty rects — the
    /// exact failure a payout screen cannot afford, because the numbers are the whole of what it is
    /// for.
    /// </remarks>
    [Test]
    public void Screen_IgnoresPlayerDied()
    {
        BuildScreen();

        _hub.Publish(new PlayerDied(90f));

        Assert.That(_presenter.IsShown, Is.False, "PlayerDied opened a screen with no numbers on it.");
        Assert.That(_hub.SubscriberCount<PlayerDied>(), Is.Zero, "this screen subscribed to PlayerDied.");

        // And the one it does take, so this row cannot pass on a screen that hears nothing at all.
        Assert.That(_hub.SubscriberCount<ShardsAwarded>(), Is.EqualTo(1));
    }

    // ---- What it draws (rules 5, 6) ----------------------------------------------------------------

    [Test]
    public void Screen_DrawsEveryStringFromTheTable()
    {
        BuildScreen(Shipped());

        foreach ((string key, string word) in Rows)
        {
            Assert.That(
                LabelFor(key).text,
                Is.EqualTo(word),
                $"the label for {key} did not read the shipped table's word.");
        }
    }

    /// <summary>
    /// Rule 5's softer half: a missing word is a screen the player can still leave.
    /// </summary>
    /// <remarks>
    /// <c>MenuPresenter.Write</c>'s answer, and the reason this screen takes its localizer without a
    /// null guard while the hub and the loader both throw: a run-end screen missing a word is still a
    /// screen with a button on it, and throwing here would strand the player on a run that has
    /// already ended.
    /// </remarks>
    [Test]
    public void Screen_FallsBackToTheKey()
    {
        Assert.DoesNotThrow(() => BuildScreen(localizer: null));

        foreach ((string key, string _) in Rows)
        {
            Assert.That(LabelFor(key).text, Is.EqualTo(key), $"{key} did not fall back to its key.");
        }

        // And it is still a screen that works: the event opens it and the button still leaves.
        _hub.Publish(new ShardsAwarded(10, 1, 0));

        Assert.That(_presenter.IsShown, Is.True);

        Tap();

        Assert.That(_loader.Asked, Is.EqualTo(new[] { SceneLoader.Menu }));
    }

    /// <summary>
    /// AR §11.5 on the new asset: no English is typed into it, and every label is a <c>LocKey</c>.
    /// </summary>
    /// <remarks>
    /// Read off disk rather than out of a loaded hierarchy, <c>TableLocalizerTests.AuthoredText</c>'s
    /// reason: the claim is about the <em>asset</em>, and a label written at runtime is fine where a
    /// label with English baked into it is what M6-10 must not inherit. The prefab authors each
    /// label's key as its own placeholder text, which is <c>PausePresenter</c>'s pattern — a field
    /// nobody dressed is then visible in the Editor rather than blank.
    /// </remarks>
    [Test]
    public void Screen_DrawsNoRawString()
    {
        IReadOnlyList<string> authored = AuthoredText(PrefabPath);

        Assert.That(authored, Is.Not.Empty, "the prefab has no TMP_Text at all, so this row tests nothing.");

        foreach ((string key, string word) in Rows)
        {
            Assert.That(authored, Does.Not.Contain(word), $"'{word}' is typed into RunEnd.prefab.");

            // The key really is what the label carries instead, so this row cannot go green on a
            // prefab whose labels are simply empty.
            Assert.That(authored, Does.Contain(key), $"no label on RunEnd.prefab is authored as {key}.");
        }

        // Nothing else on the asset is a sentence either. Every remaining authored value is a key or
        // a number placeholder, and neither has a space in it.
        foreach (string drawn in authored)
        {
            Assert.That(
                drawn,
                Does.Not.Contain(" "),
                $"'{drawn}' is authored on RunEnd.prefab and reads as English.");
        }

        // And the class draws its five words from LocKeys rather than from string literals.
        FieldInfo[] keys = Array.FindAll(
            typeof(RunEndPresenter).GetFields(Everything), f => f.FieldType == typeof(LocKey));

        Assert.That(keys, Has.Length.EqualTo(Rows.Length), "RunEndPresenter's key count moved.");
    }

    /// <summary>
    /// Rule 6: the payout is <c>Palette.Essence</c> and nothing on this screen is a serialized colour.
    /// </summary>
    /// <remarks>
    /// <b>It is that member's first reader.</b> <c>Palette.Essence</c>'s own summary reads
    /// <em>"rewards, Essence, Gates. No reader yet (M6)"</em>, and a Shard payout is a reward — so
    /// this screen needs no eleventh palette member, and the one place the colour is written down
    /// stays the one place the rule is enforceable (M3-13a).
    /// </remarks>
    [Test]
    public void Screen_CarriesNoSerializedColour()
    {
        BuildScreen();

        foreach (FieldInfo field in typeof(RunEndPresenter).GetFields(Everything))
        {
            Assert.That(
                field.FieldType,
                Is.Not.EqualTo(typeof(Color)).And.Not.EqualTo(typeof(Color32)),
                $"RunEndPresenter.{field.Name} is a colour. GD §16.4 is Palette's to enforce.");
        }

        Assert.That(Label("_shards").color, Is.EqualTo(Palette.Essence), "the payout is not Essence.");

        // And the two facts about the run are deliberately not amber: three amber numbers would say
        // nothing about which one matters.
        Assert.That(Label("_depth").color, Is.EqualTo(Palette.Neutral));
        Assert.That(Label("_bosses").color, Is.EqualTo(Palette.Neutral));
    }

    /// <summary>GD §16.4's <em>"nothing else, ever"</em>, over every colour this screen puts on a pixel.</summary>
    [Test]
    public void Screen_IsNotTheDangerColour()
    {
        BuildScreen();

        _hub.Publish(new ShardsAwarded(110, 6, 1));

        foreach (Graphic graphic in _screen.GetComponentsInChildren<Graphic>(true))
        {
            Assert.That(
                Palette.IsDanger(graphic.color),
                Is.False,
                $"{graphic.name} draws GD §16.4's reserved danger colour.");
        }

        // The two the class writes itself, stated separately: the sweep above would pass on a screen
        // whose labels were never tinted at all.
        Assert.That(Palette.IsDanger(Palette.Essence), Is.False);
        Assert.That(Palette.IsDanger(Palette.Neutral), Is.False);
    }

    // ---- What it deliberately does not hold (rules 4, 8) --------------------------------------------

    /// <summary>
    /// Rule 4: the tick is already gated, so this screen holds no pause and cannot reach one.
    /// </summary>
    /// <remarks>
    /// <c>RunTicker.Tick</c> returns on <c>!_session.IsRunning</c> before this screen exists, so a
    /// <c>PauseReason</c> here would be a second answer to a question already settled — and
    /// <c>RunPause.Pause</c> throws on a second reason, so the second answer would be the loud kind.
    /// <c>LevelUpPresenter</c>'s rule 3, reached from the other side.
    /// </remarks>
    [Test]
    public void Screen_TakesNoRunPause()
    {
        AssertDoesNotDependOn(typeof(RunPause), "a pause this screen would be the second holder of");
    }

    /// <summary>
    /// Rule 8: this run's payout and no lifetime total, refused by construction.
    /// </summary>
    /// <remarks>
    /// The banked figure lives in <c>ProfileStore.Current</c> one scope up, and drawing it here would
    /// race <c>ShardWriter</c>: both hang off <c>ShardsAwarded</c>, the hub guarantees no order
    /// between a scoped service and an injected component, and a screen that showed the total
    /// <em>before</em> the write would be wrong every second run and right every other. A lifetime
    /// total belongs beside the thing that spends it — M5-07 or M6-02.
    /// </remarks>
    [Test]
    public void Screen_DrawsNoLifetimeTotal()
    {
        AssertDoesNotDependOn(typeof(ProfileStore), "a lifetime total that would race ShardWriter");
    }

    // ---- The exit (rules 9, 10) --------------------------------------------------------------------

    [Test]
    public void Screen_ReturnsToTheMenuOnce()
    {
        BuildScreen();

        _hub.Publish(new ShardsAwarded(110, 6, 1));

        // Two taps in one frame: uGUI dispatches both from one EventSystem pass, and onClick.Invoke
        // fires whether or not the button is interactable — which is what makes the latch rather than
        // the button the thing under test. A helper that checked `interactable` first would make this
        // row pass against a presenter with no latch at all (PausePresenterTests' finding).
        Tap();
        Tap();

        Assert.That(_loader.Asked, Is.EqualTo(new[] { SceneLoader.Menu }), "the second tap loaded again.");

        Assert.That(
            Button().interactable,
            Is.False,
            "the exit is still live while the scene it asked for is coming in.");
    }

    /// <summary>Rule 10: a failed load re-arms the button rather than stranding the player.</summary>
    /// <remarks>
    /// <c>HudPresenter</c>'s existing <c>catch</c>, moved with the rest of the path rather than
    /// re-invented — a run-end screen over a run that has already ended is the one screen in the game
    /// with nothing behind it to go back to.
    /// </remarks>
    [Test]
    public void Screen_RearmsAfterAFailedLoad()
    {
        var refusing = new RefusingLoader();

        BuildScreen(loader: refusing);

        _hub.Publish(new ShardsAwarded(110, 6, 1));

        LogAssert.Expect(LogType.Exception, new Regex(RefusingLoader.Message));

        Tap();

        Assert.That(_presenter.IsShown, Is.True, "the screen went down over a load that never happened.");
        Assert.That(Button().interactable, Is.True, "the exit is dead and the player is stranded.");

        // And the re-armed button really does work, which the interactable flag alone does not say:
        // the latch has to have come down too.
        Tap();

        Assert.That(refusing.Attempts, Is.EqualTo(2));
        LogAssert.Expect(LogType.Exception, new Regex(RefusingLoader.Message));
    }

    // ---- Guard rows, implied rather than listed ----------------------------------------------------

    [Test]
    public void Construct_RefusesANullHubOrLoader()
    {
        LoadScreen();

        Assert.Throws<ArgumentNullException>(() => _presenter.Construct(null, _loader, Passthrough()));
        Assert.Throws<ArgumentNullException>(() => _presenter.Construct(_hub, null, Passthrough()));

        // And the localizer is deliberately not among them — see Screen_FallsBackToTheKey.
        Assert.DoesNotThrow(() => _presenter.Construct(_hub, _loader, null));
    }

    [Test]
    public void Screen_DrawsNoNegativeFigure()
    {
        BuildScreen();

        // Unreachable through shipped code — ShardPayout cannot produce a negative and
        // PlayerProfile.Shards refuses one — and clamped anyway, because this is the last screen a
        // player sees and the one readout in the game where a minus sign would read as a debt.
        _hub.Publish(new ShardsAwarded(-50, -1, -2));

        Assert.That(Text("_depth"), Is.EqualTo("0"));
        Assert.That(Text("_bosses"), Is.EqualTo("0"));
        Assert.That(Text("_shards"), Is.EqualTo("0"));
    }

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        var presenter = prefab.GetComponent<RunEndPresenter>();

        // Traps §5: a MonoBehaviour in a file-scoped namespace compiles, links no MonoScript, and
        // the prefab's reference to it deserialises as null with nothing reporting an error.
        Assert.That(presenter, Is.Not.Null, "RunEndPresenter did not load off its own prefab (Traps §5).");

        foreach (string field in new[]
        {
            "_root", "_return", "_title", "_returnLabel", "_depthLabel", "_bossLabel", "_shardLabel",
            "_depth", "_bosses", "_shards",
        })
        {
            SerializedProperty slot = new SerializedObject(presenter).FindProperty(field);

            Assert.That(slot, Is.Not.Null, $"RunEndPresenter has no {field}.");
            Assert.That(
                slot.objectReferenceValue,
                Is.Not.Null,
                $"RunEnd.prefab ships with an empty {field} slot.");
        }

        // The canvas sorts above every other screen in the Run scene, so nothing can be drawn over a
        // payout: the HUD is 0, the pause icon 50, the level-up screen 100 and the tree view 110.
        Assert.That(prefab.GetComponent<Canvas>().sortingOrder, Is.GreaterThan(110));
    }

    // ---- The overlay it replaces (rule 2) -----------------------------------------------------------

    /// <summary>
    /// <c>HudPresenter</c> keeps none of the death path, and three dependencies left with it.
    /// </summary>
    /// <remarks>
    /// <b>Reflection rather than a live subscriber count, and the reason is the fixture's premise.</b>
    /// Constructing a <c>HudPresenter</c> needs an <c>IRunSession</c>, and this file deliberately has
    /// no run in it — so what is asserted is the thing a subscription cannot exist without: a handler
    /// that takes a <c>PlayerDied</c>. Nested types are swept too, so a lambda would be caught as
    /// well as a method.
    /// </remarks>
    [Test]
    public void Hud_NoLongerOwnsTheDeathOverlay()
    {
        ParameterInfo[] parameters = typeof(HudPresenter)
            .GetMethod(nameof(HudPresenter.Construct))
            .GetParameters();

        // **Three parameters as of M6-03b, and the third is not the death path coming back.** The
        // death path was the only reader of SceneLoader, InputAdapter and ILocalizer, and all three
        // left with it (rule 2); an ILocalizer returned for GD §16.1's economy readouts — the Essence
        // caption, the meter's caption and the Claiming's name — and the two that are still gone are
        // what this row keeps out.
        Assert.That(
            parameters,
            Has.Length.EqualTo(3),
            "HudPresenter.Construct is not three parameters: the hub, the run, and the localizer "
                + "M6-03b's three captions read.");

        Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(DomainEventHub)));
        Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(IRunSession)));
        Assert.That(parameters[2].ParameterType, Is.EqualTo(typeof(ILocalizer)));

        foreach (Type gone in new[] { typeof(SceneLoader), typeof(InputAdapter) })
        {
            foreach (FieldInfo field in typeof(HudPresenter).GetFields(Everything))
            {
                Assert.That(
                    field.FieldType,
                    Is.Not.EqualTo(gone),
                    $"HudPresenter still holds a {gone.Name}, so it still owns part of the death path.");
            }
        }

        // The only words it draws are M6-03b's three, and neither death key is among them — those
        // live on this screen now (M4-06 rule 3).
        string[] keys = typeof(HudPresenter).GetFields(Everything)
            .Where(field => field.FieldType == typeof(LocKey))
            .Select(field => ((LocKey)field.GetValue(null)).Key)
            .ToArray();

        Assert.That(keys, Is.EquivalentTo(new[] { "ui.hud.essence", "ui.hud.veilrot", "ui.hud.claimed" }));

        AssertNothingHandles(typeof(HudPresenter), typeof(PlayerDied));

        // And this screen is where the handler went, so the two halves of rule 2 cannot both be
        // deleted by one careless edit.
        AssertNothingHandles(typeof(RunEndPresenter), typeof(PlayerDied));
        Assert.That(
            typeof(RunEndPresenter).GetMethod("OnShardsAwarded", Private),
            Is.Not.Null,
            "RunEndPresenter has no ShardsAwarded handler, so nothing opens the run-end screen.");
    }

    [Test]
    public void Hud_PrefabHasNoOverlay()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPrefabPath}.");

        foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
        {
            Assert.That(
                child.name,
                Does.Not.Contain("Death"),
                $"'{child.name}' is still on Hud.prefab. The overlay was replaced, not stacked (rule 2).");
        }

        IReadOnlyList<string> authored = AuthoredText(HudPrefabPath);

        Assert.That(authored, Is.Not.Empty, "the prefab has no TMP_Text at all, so this row tests nothing.");

        // **No authored English at all**, which on this asset reduces to: nothing on it is a
        // sentence. What is left is "140/140", a level, a key placeholder and four empty labels —
        // number formats and LocKey spellings, none of which has a space in it.
        foreach (string drawn in authored)
        {
            Assert.That(
                drawn,
                Does.Not.Contain(" "),
                $"'{drawn}' is authored on Hud.prefab and reads as English (rules 2, 3).");
        }
    }

    [Test]
    public void Table_CarriesTheRunEndRows()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No localization table at {EnglishPath}.");

        var shipped = new TableLocalizer(table);

        foreach ((string key, string word) in Rows)
        {
            Assert.That(shipped.Has(new LocKey(key)), Is.True, $"English.asset has no row for {key}.");
            Assert.That(shipped.Get(new LocKey(key)), Is.EqualTo(word));
        }

        // **The two death rows kept their keys** (rule 3). They are the right words, the table
        // already carried them, and retiring them to coin synonyms would break a shipped row for
        // nothing — so this row is what says a later task may not quietly rename them.
        Assert.That(shipped.Get(new LocKey("ui.death.title")), Is.EqualTo("You died"));
        Assert.That(shipped.Get(new LocKey("ui.death.hint")), Is.EqualTo("Tap to return"));

        // And no row this screen draws takes an argument: nothing in this project's localisation
        // does, and inventing it here would be M6-10 arriving early (rule 5).
        foreach ((string key, string _) in Rows)
        {
            Assert.That(shipped.Get(new LocKey(key)), Does.Not.Contain("{0}"));
        }
    }

    // ---- The scope refuses to compose without it (rule 7) -------------------------------------------

    /// <summary>
    /// Rule 7: the first <em>required</em> screen on <c>RunScope</c>, and it fails loudly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>FirstActiveHint</c>, <c>TreeViewPresenter</c> and the reticle are optional because a scene
    /// dressed without them still plays; a scene dressed without this one strands the player on a
    /// dead run with no way out of it, because rule 2 took the tap away from <c>HudPresenter</c> and
    /// nothing else in the Run scene loads the Menu.
    /// </para>
    /// <para>
    /// <b>Both directions, because one alone would pass on a guard that refuses everything.</b> With
    /// the field unset the message names it; with the field dressed the scope walks past it and stops
    /// at the next undressed thing instead, which is what says the guard was reached <em>and</em>
    /// passed.
    /// </para>
    /// </remarks>
    [Test]
    public void RunScope_RefusesToComposeWithoutTheScreen()
    {
        Assert.That(
            Compose(dressTheScreen: false),
            Does.Contain("Run End Presenter"),
            "RunScope composed a run with no way out of a death on it.");

        string next = Compose(dressTheScreen: true);

        Assert.That(
            next,
            Does.Not.Contain("Run End Presenter"),
            "the screen was dressed and the scope still refused it.");

        Assert.That(next, Does.Contain("Cover Layer"), "the fixture's premise: the walk carried on.");
    }

    // ---- Fixture ------------------------------------------------------------------------------------

    /// <summary>
    /// Instantiates the shipped prefab — the screen under test is the asset.
    /// </summary>
    private void LoadScreen()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        _screen = Object.Instantiate(prefab);
        _spawned.Add(_screen);

        _presenter = _screen.GetComponent<RunEndPresenter>();

        Assert.That(
            _presenter,
            Is.Not.Null,
            "RunEndPresenter did not load off its own prefab (Traps §5).");
    }

    /// <summary>
    /// The prefab, injected and started — what <c>RunScope</c> and Unity do between them.
    /// </summary>
    /// <remarks>
    /// <c>Start</c> is invoked by hand because Unity never runs it in EditMode, and it is where the
    /// five words, the two tints and the opening <c>Hide</c> all happen.
    /// </remarks>
    private void BuildScreen(ILocalizer localizer = null, SceneLoader loader = null)
    {
        LoadScreen();

        _presenter.Construct(_hub, loader ?? _loader, localizer);

        Invoke(_presenter, "Start");
    }

    /// <summary>
    /// Runs <c>RunScope.Configure</c> over a scope dressed with everything the guards before rule 7's
    /// need, and returns the message it refused with.
    /// </summary>
    /// <remarks>
    /// The five prerequisites are references rather than working objects — the guards only ask
    /// whether a field is empty — so a bare <c>GameObject</c> with the right component on it is
    /// exactly what each of them wants. Nothing here is a <c>LifetimeScope</c> that has awoken:
    /// <c>Configure</c> is invoked directly, which is the only way to reach it without entering Play.
    /// </remarks>
    private string Compose(bool dressTheScreen)
    {
        var host = new GameObject("RunScope");
        _spawned.Add(host);

        var scope = host.AddComponent<RunScope>();
        var serialized = new SerializedObject(scope);

        Set(serialized, "_playerView", Component<PlayerView>("Player"));
        Set(serialized, "_chargeMotion", Component<ChargeMotion>("Player"));
        Set(serialized, "_enemyPrefab", Component<EnemyView>("Enemy"));

        // M5-05a's guard sits with the other body prefabs, above rule 7's, so it joins this list.
        // Without the line, the row below reads back the *Wight's* refusal and looks like rule 7
        // having been deleted.
        Set(serialized, "_minionPrefab", Component<MinionView>("Wight"));

        // And M5-05b's, for the identical reason one task later — the corpse's guard sits with the
        // body prefabs too. This is the second time this list has grown for a new view, which is
        // what the comment above predicted rather than a coincidence.
        Set(serialized, "_decoyPrefab", Component<DecoyView>("Decoy"));

        Set(serialized, "_projectilePrefab", Component<ProjectileView>("Projectile"));
        Set(serialized, "_camera", Component<Camera>("Main Camera"));

        if (dressTheScreen)
        {
            Set(serialized, "_runEndPresenter", Component<RunEndPresenter>("RunEnd"));
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();

        MethodInfo configure = typeof(RunScope).GetMethod("Configure", Private);

        Assert.That(configure, Is.Not.Null, "RunScope has no Configure, so this row tests nothing.");

        try
        {
            configure.Invoke(scope, new object[] { new ContainerBuilder() });
        }
        catch (TargetInvocationException invocation)
        {
            return invocation.InnerException?.Message ?? string.Empty;
        }

        Assert.Fail("RunScope composed without a cover mask, so the fixture's premise is wrong.");

        return string.Empty;
    }

    private static void Set(SerializedObject serialized, string field, Object value)
    {
        SerializedProperty slot = serialized.FindProperty(field);

        Assert.That(slot, Is.Not.Null, $"RunScope has no {field}.");

        slot.objectReferenceValue = value;
    }

    private T Component<T>(string name)
        where T : Component
    {
        var host = new GameObject(name);
        _spawned.Add(host);

        return host.AddComponent<T>();
    }

    /// <summary>The real adapter over an empty table: every key resolves to itself.</summary>
    private ILocalizer Passthrough()
    {
        var table = ScriptableObject.CreateInstance<LocalizationTable>();
        _created.Add(table);

        return new TableLocalizer(table);
    }

    /// <summary>
    /// The real adapter over the <em>shipped</em> <c>English.asset</c>.
    /// </summary>
    /// <remarks>
    /// <c>PausePresenterTests.Shipped</c>'s reason: a row that pinned <em>"Soul Shards"</em> against
    /// a table this file wrote would go green over an <c>English.asset</c> that said anything at all.
    /// </remarks>
    private static ILocalizer Shipped()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No localization table at {EnglishPath}.");

        return new TableLocalizer(table);
    }

    /// <summary>
    /// A tap on the exit, through the button the player's thumb would hit.
    /// </summary>
    /// <remarks>
    /// <c>onClick.Invoke</c> rather than a synthetic pointer event: it fires whether or not the
    /// button is interactable, which is the same thing that happens when two taps are dispatched from
    /// one <c>EventSystem</c> pass — and is what makes the latch rather than the flag the thing under
    /// test.
    /// </remarks>
    private void Tap() => Button().onClick.Invoke();

    private Button Button() => Field<Button>(_presenter, "_return");

    private CanvasGroup Root() => Field<CanvasGroup>(_presenter, "_root");

    private TMP_Text Label(string field) => Field<TMP_Text>(_presenter, field);

    private string Text(string field) => Label(field).text;

    /// <summary>The label a key is drawn onto, so the rows read as the screen does.</summary>
    private TMP_Text LabelFor(string key) => key switch
    {
        "ui.death.title" => Label("_title"),
        "ui.death.hint" => Label("_returnLabel"),
        "ui.runend.depth" => Label("_depthLabel"),
        "ui.runend.bosses" => Label("_bossLabel"),
        "ui.runend.shards" => Label("_shardLabel"),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "no label draws that key."),
    };

    /// <summary>
    /// <paramref name="forbidden"/> appears nowhere on <see cref="RunEndPresenter"/> — not as a
    /// field, not as an injected parameter.
    /// </summary>
    private static void AssertDoesNotDependOn(Type forbidden, string what)
    {
        foreach (FieldInfo field in typeof(RunEndPresenter).GetFields(Everything))
        {
            Assert.That(
                field.FieldType,
                Is.Not.EqualTo(forbidden),
                $"RunEndPresenter.{field.Name} holds {what}.");
        }

        foreach (MethodInfo method in typeof(RunEndPresenter).GetMethods(Everything))
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                Assert.That(
                    parameter.ParameterType,
                    Is.Not.EqualTo(forbidden),
                    $"RunEndPresenter.{method.Name} takes {what}.");
            }
        }
    }

    /// <summary>Nothing on <paramref name="owner"/> — or inside it — can handle <paramref name="evt"/>.</summary>
    private static void AssertNothingHandles(Type owner, Type evt)
    {
        var types = new List<Type> { owner };
        types.AddRange(owner.GetNestedTypes(Everything));

        foreach (Type type in types)
        {
            foreach (MethodInfo method in type.GetMethods(Everything))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(
                        parameter.ParameterType,
                        Is.Not.EqualTo(evt),
                        $"{type.Name}.{method.Name} takes a {evt.Name}, so it can subscribe to one.");
                }
            }
        }
    }

    /// <summary>Every <c>m_text:</c> value in an asset's YAML — what a player would actually read.</summary>
    /// <remarks>
    /// <c>TableLocalizerTests.AuthoredText</c>, copied for its reason: only the <c>m_text:</c> lines,
    /// because a well-named GameObject is not a localisation failure and a row that said otherwise
    /// would force the asset to be renamed to pass.
    /// </remarks>
    private static IReadOnlyList<string> AuthoredText(string path)
    {
        Assert.That(System.IO.File.Exists(path), Is.True, $"No asset at {path}.");

        const string Marker = "  m_text: ";

        var drawn = new List<string>();

        foreach (string line in System.IO.File.ReadAllLines(path))
        {
            if (line.StartsWith(Marker, StringComparison.Ordinal))
            {
                drawn.Add(line.Substring(Marker.Length).Trim());
            }
        }

        return drawn;
    }

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, Private).Invoke(target, null);

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Private).GetValue(target);

    /// <summary>A <c>SceneLoader</c> that records rather than loading.</summary>
    /// <remarks>
    /// <c>PausePresenterTests.RecordingLoader</c>'s reason: <c>SceneManager.LoadSceneAsync</c> cannot
    /// be driven from an EditMode row without taking the Editor's open scene with it.
    /// </remarks>
    private sealed class RecordingLoader : SceneLoader
    {
        public List<string> Asked { get; } = new List<string>();

        public override Task LoadAsync(string sceneName)
        {
            Asked.Add(sceneName);

            return Task.CompletedTask;
        }
    }

    /// <summary>A loader that refuses, so rule 10's catch has something to catch.</summary>
    /// <remarks>
    /// It throws synchronously rather than returning a faulted <c>Task</c>, and the two are the same
    /// thing here: the call sits inside the presenter's <c>try</c> either way, and an awaited
    /// already-completed task continues on the same stack. Synchronous is the one an EditMode row can
    /// assert about without pumping anything.
    /// </remarks>
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
