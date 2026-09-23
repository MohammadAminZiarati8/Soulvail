using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Core.Ports;
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
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// The level-up screen: three cards, a tap, and the frame that starts again. GD §13.1, CH §5.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over a real <c>RunSession</c> and a real <c>DomainEventHub</c></b>, which is
/// <c>ThreatArrowsTests</c>' shape and the only shape that would catch the failure this screen has:
/// a card drawn from an offer the run does not actually hold. The hub <em>is</em> the session's
/// <c>IDomainEvents</c>, so every event a row reacts to was published by core rather than by the
/// fixture.
/// </para>
/// <para>
/// <b>The screen under test is the shipped prefab</b>, instantiated per row, rather than a hierarchy
/// this file builds. Hand-building one would test a second screen that happens to resemble the
/// asset, and the failure Traps §5 describes — a component that deserialises as null with nothing
/// reporting it — is invisible to a fixture that never loads the asset.
/// </para>
/// <para>
/// <b>The pause is not this screen's, and several rows exist to hold that line.</b>
/// <c>RunTicker.LevelUpPhase</c> raises and lowers <c>PauseReason.LevelUp</c> (M3-08a rule 12, and
/// the owner's ruling at M3-08b), so the spec's rule 3 was rewritten rather than implemented. The
/// rows that would have asserted the presenter pausing now assert that it does not — and
/// <see cref="Construct_TakesNoRunPause"/> pins the ruling at the constructor, where an `if` cannot
/// creep back in.
/// </para>
/// <para>
/// <c>Awake</c> and <c>Start</c> do not run in EditMode (Traps §5), so the guards in
/// <c>LevelUpPresenter.Start</c> never fire here and the prefab's own alpha 0 is what leaves the
/// screen down at the top of each row. A frame is passed explicitly where a row needs one.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LevelUpPresenterTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/LevelUp.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string TreeId = "tree.oathbound";
    private const string ArenaId = "arena.pillars";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    private const float Frame = 1f / 60f;

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    /// <summary>Branch a's whole first tier, so all three are available on the first pick.</summary>
    private const string NodeOne = "skill.test.one";
    private const string NodeTwo = "skill.test.two";
    private const string NodeThree = "skill.test.three";

    /// <summary>Branch b and branch c, one node each — what is left when branch a is emptied.</summary>
    private const string NodeFour = "skill.test.four";
    private const string NodeFive = "skill.test.five";

    private DomainEventHub _hub;
    private RecordingEvents _spy;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private RunSession _session;

    private GameObject _screen;
    private LevelUpPresenter _presenter;
    private OfferCard[] _cards;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<RunPause> _pauses = new List<RunPause>();

    private float _restoreTimeScale;

    [SetUp]
    public void CreateWorld()
    {
        // The Editor's own globals. RunPause writes both and every row that builds one disposes it,
        // but a row that fails part way through would otherwise leave the whole suite after it
        // running at timeScale 0 — ResumeFlowTests' argument, made in the teardown rather than in
        // each row.
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

    // ---- The offer on screen (rules 1, 6, 7) ---------------------------------------------------

    [Test]
    public void Offer_ShowsThreeCards()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen();

        _session.OpenLevelUp();

        Assert.That(_presenter.IsShown, Is.True, "an offer was published and the screen stayed down.");

        IReadOnlyList<ContentId> offer = _session.State.Offer;

        Assert.That(offer.Count, Is.EqualTo(3), "the fixture's premise: a five-node tree offers three.");

        for (int i = 0; i < 3; i++)
        {
            Assert.That(_cards[i].IsShown, Is.True, $"card {i} was not drawn.");

            // Each card names *its own* spec's key, in the offer's order — the failure this row
            // exists for is three cards that all draw the first id, which looks fine until a tap
            // spends the pick on something else.
            Assert.That(
                NameOn(_cards[i]),
                Is.EqualTo(_catalog.Skill(offer[i]).NameKey.Key),
                $"card {i} drew a different node from the one at offer position {i}.");
        }
    }

    [Test]
    public void Offer_ShowsFewerWhenScarce()
    {
        // Branch a emptied, so only branch b and branch c are left: two available, and M3-04 rule 1
        // writes two rather than padding to three.
        StartRun(level: 5, pending: 1, taken: new[] { NodeOne, NodeTwo, NodeThree });
        BuildScreen();

        _session.OpenLevelUp();

        Assert.That(_session.State.Offer.Count, Is.EqualTo(2), "the fixture's premise.");

        Assert.That(_cards[0].IsShown, Is.True);
        Assert.That(_cards[1].IsShown, Is.True);

        // CC §6.2's "unused slots are not drawn", applied to the other screen with a variable
        // number of things on it. Drawn empty, the third card is a button that spends a pick on
        // whatever ChooseOffer decides index 2 means — which is an ArgumentOutOfRangeException.
        Assert.That(_cards[2].IsShown, Is.False, "the third card was drawn for an offer of two.");
    }

    [Test]
    public void Card_DrawsEnglish()
    {
        StartRun(level: 2, pending: 1);

        // Every node in the fixture's tree gets a word, so the row does not have to know which
        // three the seeded draw picks — and the word differs per node, which is what makes the
        // assertion below discriminating rather than merely non-empty.
        var words = new Dictionary<string, string>
        {
            [NodeOne] = "Consecrate",
            [NodeTwo] = "Bulwark",
            [NodeThree] = "Zealotry",
            [NodeFour] = "Long Reach",
            [NodeFive] = "Unbowed",
        };

        var pairs = new List<string>();

        foreach (KeyValuePair<string, string> word in words)
        {
            pairs.Add($"{word.Key}.name");
            pairs.Add(word.Value);
            pairs.Add($"{word.Key}.desc");
            pairs.Add($"What {word.Value} does.");
        }

        BuildScreen(new DictionaryLocalizer(pairs.ToArray()));

        _session.OpenLevelUp();

        SkillSpec spec = _catalog.Skill(_session.State.Offer[0]);
        string expected = words[spec.Id.Value];

        // **M3-08b's `Card_DrawsTheKeyNotEnglish`, inverted** (M3-14a rule 9). That row pinned the
        // key deliberately so that resolving it would be a red row somebody had to argue with
        // rather than a silent improvement — this is the argument, and ledger row 9's first reader
        // closing. `RewrittenRows_StillAssertWhatTheyAsserted` in TableLocalizerTests is what makes
        // deleting this row fail rather than only renaming it.
        Assert.That(NameOn(_cards[0]), Is.EqualTo(expected));
        Assert.That(NameOn(_cards[0]), Does.Not.StartWith("skill."));
        Assert.That(DescriptionOn(_cards[0]), Is.EqualTo($"What {expected} does."));

        // And the member is still `Key`. The spec wrote `spec.NameKey.Value` in four places and
        // LocKey has no such member — M3-00c recorded it two spec groups ago, M3-08b fixed it, and
        // this task reads the same member one layer down, inside TableLocalizer.
        Assert.That(
            typeof(LocKey).GetProperty("Value"),
            Is.Null,
            "LocKey grew a Value member. The spec's four `.Value` sites were corrected to `.Key`; "
                + "if the struct really has both now, say which one a card draws.");
    }

    /// <summary>
    /// The other half of the row above: a key with no row still reaches the card as itself.
    /// </summary>
    /// <remarks>
    /// <b>Rule 1 from the screen's side, and it is why rule 7's correctness is not testable here.</b>
    /// A missing row is indistinguishable from a wrong key on a card, which is exactly what makes
    /// "every key has a row" M3-14b's job rather than this task's — see M3-14a's As built.
    /// </remarks>
    [Test]
    public void Card_MissingRowFallsBackToTheKey()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen(new DictionaryLocalizer());

        _session.OpenLevelUp();

        SkillSpec spec = _catalog.Skill(_session.State.Offer[0]);

        Assert.That(NameOn(_cards[0]), Is.EqualTo(spec.NameKey.Key));
        Assert.That(NameOn(_cards[0]), Does.StartWith("skill."));
    }

    [Test]
    public void Card_TintsByKind()
    {
        var card = BuildLooseCard();

        Color passive = TintFor(card, SkillKind.Passive);
        Color active = TintFor(card, SkillKind.Active);
        Color upgrade = TintFor(card, SkillKind.Upgrade);
        Color keystone = TintFor(card, SkillKind.Keystone);

        var all = new[] { passive, active, upgrade, keystone };

        // Four different colours — CH §4's four kinds are what a player distinguishes at a glance,
        // and two kinds sharing a tint is the one failure here that still looks like a working
        // screen.
        Assert.That(
            all.Distinct().Count(),
            Is.EqualTo(4),
            "two of CH §4's four kinds are drawn in the same colour, so the strip says nothing.");

        // **And they are Palette's, which is what M3-13a changed here.** Until this task the four
        // were serialized fields on this component, copied value for value onto TreeNodeView and
        // AutoCastRow — the third copy, which is where ledger row 6 said a palette stops being one.
        // The row asserted only that they *differed*; it now also says which four they are, which is
        // the claim that stops a screen quietly drifting from the other two again.
        Assert.That(passive, Is.EqualTo(Palette.KindPassive));
        Assert.That(active, Is.EqualTo(Palette.KindActive));
        Assert.That(upgrade, Is.EqualTo(Palette.KindUpgrade));
        Assert.That(keystone, Is.EqualTo(Palette.KindKeystone));
    }

    // ---- A Pact on the card (M6-05b rules 5, 7, 8) -----------------------------------------------

    [Test]
    public void Card_DrawsAPactViolet()
    {
        var card = BuildLooseCard();

        card.Show(0, Node(NodeOne, SkillKind.Keystone), Passthrough(), _ => { }, isPact: true);

        Outline frame = Field<Outline>(card, "_pactFrame");

        Assert.That(card.IsPact, Is.True);
        Assert.That(frame.enabled, Is.True, "a Pact card has no frame.");
        Assert.That(frame.effectColor, Is.EqualTo(Palette.Veilrot), "GD §16.4 gives Pacts one violet.");

        // The stripe stays CH §4's: a corrupted Keystone is still a Keystone, and the stripe is the
        // only thing on the card that says so.
        Assert.That(Field<Image>(card, "_kindStrip").color, Is.EqualTo(Palette.KindKeystone));
    }

    [Test]
    public void Card_DrawsThePactsDescriptionAndTheCleanName()
    {
        var card = BuildLooseCard();
        var localizer = new DictionaryLocalizer(
            $"{NodeOne}.name", "Keen Censer",
            $"{NodeOne}.desc", "+15% censer damage.",
            $"{NodeOne}.pact", "+45% censer damage, but -25 maximum health.");

        card.Show(0, Node(NodeOne, SkillKind.Passive), localizer, _ => { }, isPact: true);

        // M6-05a rule 4: the player recognises the node they were offered clean before.
        Assert.That(NameOn(card), Is.EqualTo("Keen Censer"));
        Assert.That(DescriptionOn(card), Is.EqualTo("+45% censer damage, but -25 maximum health."));
    }

    [Test]
    public void Card_DrawsTheRotPrice()
    {
        var card = BuildLooseCard();

        card.Show(0, Node(NodeOne, SkillKind.Passive), Shipped(), _ => { }, isPact: true);

        // Through the shipped English.asset, so inverting either word is a red row.
        Assert.That(RotOn(card), Does.EndWith("+15 Rot"));
        Assert.That(RotOn(card), Is.EqualTo("Pact · +15 Rot"));

        card.Show(0, Node(NodeOne, SkillKind.Passive), Shipped(), _ => { }, isPact: false);

        Assert.That(RotOn(card), Is.Empty, "a clean card named a price.");
    }

    [Test]
    public void Card_RepaintsBackToClean()
    {
        var card = BuildLooseCard();
        SkillSpec spec = Node(NodeOne, SkillKind.Passive);

        card.Show(0, spec, Passthrough(), _ => { }, isPact: true);
        card.Show(0, spec, Passthrough(), _ => { }, isPact: false);

        // A second pick repaints the same three objects in place, and the second may be clean.
        Assert.That(card.IsPact, Is.False);
        Assert.That(Field<Outline>(card, "_pactFrame").enabled, Is.False, "the violet frame stayed on.");
        Assert.That(DescriptionOn(card), Is.EqualTo(spec.DescriptionKey.Key), "the Pact's description stayed.");
        Assert.That(RotOn(card), Is.Empty, "the price stayed.");
    }

    [Test]
    public void Card_RefusesAPactItCannotDraw()
    {
        var card = BuildLooseCard();

        Assert.That(
            () => card.Show(0, Node(NodeTwo, SkillKind.Active), Passthrough(), _ => { }, isPact: true),
            Throws.ArgumentException,
            "a presenter read past the model and the card drew it clean.");
    }

    [Test]
    public void Card_CarriesNoSerializedColour()
    {
        // Views_CarryNoSerializedColour, still: the frame's colour is written from Palette on every
        // draw, so the Outline component's own serialized colour is never what a player sees.
        FieldInfo[] colours = typeof(OfferCard)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(Color) || f.FieldType == typeof(Color[]))
            .ToArray();

        Assert.That(colours.Select(f => f.Name), Is.Empty);
    }

    [Test]
    public void Presenter_PassesThePactThrough()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen();

        _session.OpenLevelUp();

        // The real publish first: RunState's read and the event agree (M6-05b rule 5).
        Assert.That(_session.State.PactIndex, Is.EqualTo(_spy.Single<OfferPresented>().PactIndex));

        IReadOnlyList<ContentId> offer = _session.State.Offer;
        int pact = Enumerable.Range(0, offer.Count).Last(i => _catalog.Skill(offer[i]).HasPact);

        _hub.Publish(new OfferPresented(offer.Count, 1, pactIndex: pact));

        for (int i = 0; i < offer.Count; i++)
        {
            Assert.That(_cards[i].IsPact, Is.EqualTo(i == pact), $"card {i} was drawn wrong.");
        }
    }

    // ---- The pause is not this screen's (the owner's ruling; the spec's rule 3 rewritten) -------

    [Test]
    public void Construct_TakesNoRunPause()
    {
        MethodInfo construct = typeof(LevelUpPresenter).GetMethod(nameof(LevelUpPresenter.Construct));

        Assert.That(construct, Is.Not.Null);

        // **The pin row for the owner's ruling.** The gate lives in RunTicker.LevelUpPhase, which
        // holds both halves; this screen renders a gate it does not own. A presenter that cannot
        // reach a RunPause cannot raise one, cannot lower one, and cannot grow an `if` that guards
        // against the ticker already holding it — which is the two-owners state M3-08a deviation 12
        // exists to refuse. A task that moves the gate back here must delete this row and argue it.
        Assert.That(
            construct.GetParameters().Select(p => p.ParameterType),
            Has.None.EqualTo(typeof(RunPause)),
            "LevelUpPresenter took a RunPause. The gate is RunTicker's (M3-08a rule 12, ruled at "
                + "M3-08b) — if that changed, LevelUpPhase's raise and release have to be deleted "
                + "rather than guarded, and FrameOrderTests' four Frame_* rows rewritten with it.");

        Assert.That(
            typeof(LevelUpPresenter).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Select(f => f.FieldType),
            Has.None.EqualTo(typeof(RunPause)),
            "LevelUpPresenter holds a RunPause field, so it can reach the gate by another route.");
    }

    [Test]
    public void Offer_TouchesNoPause()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen();

        RunPause pause = NewPause();

        Time.timeScale = 1f;

        _session.OpenLevelUp();

        Assert.That(_presenter.IsShown, Is.True, "the fixture's premise: the screen went up.");

        // Nothing in this file took the pause. In the running game RunTicker has already raised it
        // by the time the cards are visible — LevelUpPhase publishes the offer and then reads
        // HasOffer, both inside one frame — and this row is about who did it, not whether it
        // happened. `Frame_LevelUpPhaseRunsAboveCommands` owns the "it happened" half.
        Assert.That(pause.IsPaused, Is.False, "the presenter raised the pause. The gate is the ticker's.");
        Assert.That(pause.Holder, Is.Null);
        Assert.That(Time.timeScale, Is.EqualTo(1f), "the presenter wrote an engine global.");
    }

    [Test]
    public void Offer_RedrawRepaintsAndNeverPauses()
    {
        // Two picks owed, so choosing the first redraws for the second rather than closing.
        StartRun(level: 3, pending: 2);
        BuildScreen();

        RunPause pause = NewPause();

        // Held the way the running game holds it: the ticker raised it when the first offer landed
        // and has not lowered it, because HasOffer is still true through the redraw.
        _session.OpenLevelUp();
        pause.Pause(PauseReason.LevelUp);

        string before = NameOn(_cards[0]);

        Assert.That(
            () => Tap(0),
            Throws.Nothing,
            "the redraw re-entered the pause. Under the spec's rule 3 the presenter would have "
                + "called Pause on the second OfferPresented and RunPause would have thrown.");

        Assert.That(_presenter.IsShown, Is.True, "the screen closed on a pick that was not the last.");
        Assert.That(_spy.Count<LevelUpClosed>(), Is.Zero, "nothing more was owed after the first pick.");

        // Still held, exactly once, by the same holder — and this file did not touch it.
        Assert.That(pause.IsPaused, Is.True);
        Assert.That(pause.Holder, Is.EqualTo(PauseReason.LevelUp));

        Assert.That(
            NameOn(_cards[0]),
            Is.Not.EqualTo(before),
            "the second pick redrew the same node, so the first card was never repainted.");
    }

    // ---- The tap (rules 1, 5) ------------------------------------------------------------------

    [Test]
    public void Tap_SendsChooseOfferWithTheIndex()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen();

        _session.OpenLevelUp();

        ContentId middle = _session.State.Offer[1];

        Tap(1);

        // An index, not a ContentId — the card knows which of the three was hit and nothing about
        // what is under it (IProgressionCommands.ChooseOffer's reason). The node that ended up
        // taken is how this row checks the index survived the trip.
        IReadOnlyList<NodeTaken> taken = _spy.Of<NodeTaken>();

        Assert.That(taken.Count, Is.EqualTo(1), "one tap, one node.");
        Assert.That(taken[0].SkillId, Is.EqualTo(middle), "the middle card spent the pick on another node.");
    }

    [Test]
    public void Tap_DisablesEveryCardImmediately()
    {
        StartRun(level: 3, pending: 2);
        BuildScreen();

        _session.OpenLevelUp();

        bool[] duringCommand = null;

        // Read from *inside* the command, on NodeTaken, which core publishes part way through
        // ChooseOffer. Read afterwards the assertion would be worthless: a redraw for the second
        // pick re-enables all three before the call returns, so "non-interactable after" is only
        // ever true on the last pick.
        using (_hub.Subscribe<NodeTaken>(_ => duringCommand = Interactables()))
        {
            Tap(0);
        }

        Assert.That(duringCommand, Is.Not.Null, "the command never reached core.");
        Assert.That(
            duringCommand,
            Is.All.False,
            "a card was still live while ChooseOffer was running. A second tap landing there "
                + "spends the next pick on a node the player never saw (rule 5).");
    }

    [Test]
    public void Tap_ReEnablesOnTheNextOffer()
    {
        StartRun(level: 3, pending: 2);
        BuildScreen();

        _session.OpenLevelUp();
        Tap(0);

        Assert.That(_presenter.IsShown, Is.True, "the fixture's premise: a second pick was owed.");

        // Back on the moment the redraw lands, so nothing on screen looks dead while the player is
        // being asked a second question. The *command* is still latched for the rest of the frame —
        // that is Tap_TwiceSpendsOnePick's half.
        Assert.That(
            Interactables().Take(_session.State.Offer.Count),
            Is.All.True,
            "the redrawn cards came back dead, so the second pick could not be made.");
    }

    [Test]
    public void Tap_TwiceSpendsOnePick()
    {
        StartRun(level: 3, pending: 2);
        BuildScreen();

        _session.OpenLevelUp();

        Assert.That(_session.State.PendingLevelUps, Is.EqualTo(2), "the fixture's premise.");

        // A scripted double tap *in one frame*: uGUI dispatches both from one EventSystem pass, so
        // no Update runs between them and the latch is still down. The second tap lands on cards
        // that have already been repainted for the second pick, which is exactly the node the
        // player never saw.
        Tap(0);
        Tap(0);

        Assert.That(
            _spy.Count<NodeTaken>(),
            Is.EqualTo(1),
            "the second tap of a double tap spent the second pick.");

        Assert.That(
            _session.State.PendingLevelUps,
            Is.EqualTo(1),
            "two picks were spent in one frame.");

        // And a genuinely new frame lifts the latch, so the player can answer the second question.
        PassAFrame();
        Tap(0);

        Assert.That(_spy.Count<NodeTaken>(), Is.EqualTo(2), "a new frame did not re-arm the cards.");
        Assert.That(_session.State.PendingLevelUps, Is.Zero);
    }

    // ---- Closing, and the frame that starts again (rules 1, 3) ---------------------------------

    [Test]
    public void Closed_HidesTheScreen()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen();

        RunPause pause = NewPause();

        _session.OpenLevelUp();
        pause.Pause(PauseReason.LevelUp);

        Tap(0);

        Assert.That(_spy.Count<LevelUpClosed>(), Is.EqualTo(1), "nothing closed the level-up.");
        Assert.That(_presenter.IsShown, Is.False, "the screen stayed up after the last pick.");

        foreach (OfferCard card in _cards)
        {
            Assert.That(card.IsShown, Is.False, "a card outlived the screen that owns it.");
        }

        // **Still paused, and that is correct rather than a leak.** The gate is lowered by
        // RunTicker.LevelUpPhase on the next frame, when HasOffer reads false — the screen closing
        // and the run resuming are one frame apart, in that order, which is the order that can
        // never show a closed screen over a frozen game.
        Assert.That(
            pause.IsPaused,
            Is.True,
            "the presenter lowered the pause. Releasing it here is the half the ticker owns.");

        Assert.That(_session.State.HasOffer, Is.False, "and the ticker's next read will lower it.");
    }

    [Test]
    public void Closed_LeavesTheRunTicking()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen();

        _session.OpenLevelUp();
        Tap(0);

        float before = _session.State.Time;

        // **One tick, and the frame that lifts the gate is not this fixture's problem.** M3-08a
        // found that the frame which lowers the pause still ticks core with `Dt` 0, because
        // `Time.deltaTime` was already scaled to zero before that frame began. That is a fact about
        // `RunTicker` reading the engine clock — this row drives `RunSession.Tick` directly with a
        // synthetic snapshot carrying its own Dt, so no engine clock is involved and one tick moves
        // the simulated clock. A second tick here would be padding, not honesty.
        _session.Tick(World(Frame));

        Assert.That(
            _session.State.Time,
            Is.GreaterThan(before),
            "the run stopped being ticked once the screen closed.");
    }

    [Test]
    public void Header_ShowsLevelAndPickCount()
    {
        StartRun(level: 7, pending: 2);
        BuildScreen();

        _session.OpenLevelUp();

        Assert.That(LevelText(), Is.EqualTo("Level 7"));

        // "Pick i of n", and i counts *up* while OfferPresented.PicksOwed counts down — two owed
        // arrive as PicksOwed 2 and then 1. A header written straight off the payload would read
        // "Pick 2 of 2" and then "Pick 1 of 1", which is what makes a double level-up illegible.
        Assert.That(PickText(), Is.EqualTo("Pick 1 of 2"));

        Tap(0);

        Assert.That(_presenter.IsShown, Is.True, "the fixture's premise: a second pick was owed.");
        Assert.That(PickText(), Is.EqualTo("Pick 2 of 2"));
        Assert.That(LevelText(), Is.EqualTo("Level 7"), "taking a node is not a level.");
    }

    [Test]
    public void Offer_ShowsInstantly()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen();

        Assert.That(Alpha(), Is.Zero, "the fixture's premise: the prefab ships with the screen down.");

        _session.OpenLevelUp();

        // Alpha 1 on the same call. timeScale is 0 while this is up (M3-08a rule 13), so a scaled
        // tween would freeze half way and an unscaled one is a second clock in a file whose whole
        // job is to be brief — GD §13.1 budgets a couple of seconds for the entire interruption.
        Assert.That(Alpha(), Is.EqualTo(1f), "the screen faded in rather than appearing.");

        Assert.That(
            _screen.GetComponentInChildren<Animator>(true),
            Is.Null,
            "the level-up screen carries an Animator, which cannot run at timeScale 0.");

        Assert.That(
            typeof(LevelUpPresenter)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => typeof(System.Collections.IEnumerator).IsAssignableFrom(m.ReturnType)),
            Is.Empty,
            "LevelUpPresenter declares a coroutine. Nothing here may wait: the engine clock is "
                + "stopped and an unscaled one is a second clock (rule 4).");
    }

    // ---- The two ways nothing is shown (M3-08a rules 5 and 7, from this side) -------------------

    [Test]
    public void Overflow_ShowsNothing()
    {
        // Every node taken, and a pick still owed. Level accounts for the five nodes and the pick —
        // M3-08a rule 9's identity refuses a saved run whose level cannot explain what it owns.
        StartRun(
            level: 7,
            pending: 1,
            taken: new[] { NodeOne, NodeTwo, NodeThree, NodeFour, NodeFive });

        BuildScreen();

        RunPause pause = NewPause();

        Time.timeScale = 1f;

        _session.OpenLevelUp();

        Assert.That(_spy.Count<OverflowGranted>(), Is.EqualTo(1), "the pick was not spent on Overflow.");
        Assert.That(_spy.Count<OfferPresented>(), Is.Zero, "a full tree drew cards.");

        // Silent and instant: GD §13.1's pause exists to let someone *choose*, and a card with one
        // button would tax the player for the game having run out of nodes (CH §5.2).
        Assert.That(_presenter.IsShown, Is.False, "the screen opened for an Overflow level.");
        Assert.That(pause.IsPaused, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f), "the game stopped for a level with no choice in it.");
    }

    [Test]
    public void NoTree_ShowsNothing()
    {
        // Every run in the build until M3-12: a class whose catalog holds no tree banks its levels
        // and must not pause, draw or throw.
        StartRun(level: 4, pending: 3, withTree: false);
        BuildScreen();

        RunPause pause = NewPause();

        Time.timeScale = 1f;

        float before = _session.State.Time;

        for (int i = 0; i < 10; i++)
        {
            // What RunTicker does every frame, in the order it does it.
            if (_session.IsLevelUpPending)
            {
                _session.OpenLevelUp();
            }

            _session.Tick(World(Frame));
        }

        Assert.That(_presenter.IsShown, Is.False, "the screen opened for a class with no tree.");
        Assert.That(_spy.Count<OfferPresented>(), Is.Zero);
        Assert.That(pause.IsPaused, Is.False);

        Assert.That(
            _session.State.Time,
            Is.GreaterThan(before),
            "the run stopped ticking for picks it can never spend.");

        Assert.That(
            _session.State.PendingLevelUps,
            Is.EqualTo(3),
            "the banked picks were spent with no tree to spend them on.");
    }

    // ---- Lifetime (rules 2, 12) ----------------------------------------------------------------

    [Test]
    public void Subscribes_FromConstruct()
    {
        // The run starts *first*, and the screen is composed after it — which is the order
        // RunScope's Awake and this component's OnEnable can happen in, since Unity orders no two
        // Awake calls. An OnEnable subscription would have been made against a hub that did not
        // exist yet, and this row would see nothing rendered.
        StartRun(level: 2, pending: 1);

        Assert.That(_spy.Count<RunStarted>(), Is.EqualTo(1), "the fixture's premise.");

        BuildScreen();

        _session.OpenLevelUp();

        Assert.That(
            _presenter.IsShown,
            Is.True,
            "a presenter constructed after RunStarted heard nothing, so the subscription is not "
                + "being made in Construct (rule 2).");
    }

    [Test]
    public void Destroy_DropsSubscriptions()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen();

        Assert.That(_hub.SubscriberCount<OfferPresented>(), Is.EqualTo(1), "the fixture's premise.");

        // **Invoked rather than waited for, and the reason is a finding rather than a shortcut.**
        // Unity does not call OnDestroy on an object whose Awake never ran, and Awake does not run
        // in EditMode (Traps §5) — so `DestroyImmediate` alone leaves both subscriptions in place
        // and this row would pass against a presenter with no OnDestroy at all. What is under test
        // is what the method does; that Unity calls it on a real teardown is the engine's contract,
        // not this file's.
        Destroy();

        Object.DestroyImmediate(_screen);

        // Left in the hub's subscriber list, this would be handed an event for a component Unity
        // has killed — which throws from inside core's publish, on the frame a player levels up.
        Assert.That(() => _session.OpenLevelUp(), Throws.Nothing);

        Assert.That(_hub.SubscriberCount<OfferPresented>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<LevelUpClosed>(), Is.Zero);
    }

    [Test]
    public void Destroy_WhilePaused_LeavesTheAppRunnable()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen();

        RunPause pause = NewPause();

        _session.OpenLevelUp();
        pause.Pause(PauseReason.LevelUp);

        Assert.That(Time.timeScale, Is.Zero, "the fixture's premise.");

        // The screen dies with the pause still held. VContainer orders no two disposals, so this
        // must not depend on the presenter being the one that resumes — and under the owner's
        // ruling it cannot be. RunPause.Dispose restores both globals unconditionally, which is
        // what stops the Menu inheriting a frozen clock with domain reload disabled on Play.
        //
        // OnDestroy is deliberately *not* invoked here, unlike in Destroy_DropsSubscriptions: the
        // claim is that the app comes back even if the presenter's teardown never runs at all,
        // which is the worse of the two cases and the one rule 12 is written against.
        Object.DestroyImmediate(_screen);

        pause.Dispose();

        Assert.That(Time.timeScale, Is.EqualTo(1f), "the app was left frozen with no screen up.");
        Assert.That(pause.IsPaused, Is.False);
    }

    // ---- The asset (Traps §5, rule 9) -----------------------------------------------------------

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        // Traps §5: a MonoBehaviour declared with a file-scoped namespace compiles and is never
        // linked to a MonoScript, so the component would come back null here with nothing reporting
        // an error anywhere. GetComponent answering is half of what this row proves.
        var presenter = prefab.GetComponent<LevelUpPresenter>();

        Assert.That(
            presenter,
            Is.Not.Null,
            "LevelUpPresenter did not load off its own prefab — the usual cause is a file-scoped "
                + "namespace on a UnityEngine.Object type (Traps §5).");

        // **Read back off the saved asset**, not off an object this fixture just dressed. A prefab
        // whose references never bound looks identical in memory to one that did, right up until it
        // is loaded in a build.
        var so = new SerializedObject(presenter);

        Assert.That(so.FindProperty("_root").objectReferenceValue, Is.Not.Null, "no CanvasGroup root.");
        Assert.That(so.FindProperty("_levelLabel").objectReferenceValue, Is.Not.Null, "no level label.");
        Assert.That(so.FindProperty("_pickLabel").objectReferenceValue, Is.Not.Null, "no pick label.");

        SerializedProperty cards = so.FindProperty("_cards");

        Assert.That(cards, Is.Not.Null, "LevelUp.prefab carries no _cards field.");
        Assert.That(cards.arraySize, Is.EqualTo(OfferGenerator.DefaultOfferCount));

        for (int i = 0; i < cards.arraySize; i++)
        {
            var card = cards.GetArrayElementAtIndex(i).objectReferenceValue as OfferCard;

            Assert.That(card, Is.Not.Null, $"card {i} is not assigned.");

            var cardSo = new SerializedObject(card);

            Assert.That(cardSo.FindProperty("_name").objectReferenceValue, Is.Not.Null, $"card {i}: no name text.");
            Assert.That(cardSo.FindProperty("_description").objectReferenceValue, Is.Not.Null, $"card {i}: no description.");
            Assert.That(cardSo.FindProperty("_kindStrip").objectReferenceValue, Is.Not.Null, $"card {i}: no kind strip.");
            Assert.That(cardSo.FindProperty("_button").objectReferenceValue, Is.Not.Null, $"card {i}: no button.");

            // M6-05b: the Pact frame and its price.
            Assert.That(cardSo.FindProperty("_pactFrame").objectReferenceValue, Is.Not.Null, $"card {i}: no Pact frame.");
            Assert.That(cardSo.FindProperty("_rot").objectReferenceValue, Is.Not.Null, $"card {i}: no Rot label.");
        }

        // Rule 9: its own canvas, above the HUD's, so the screen can be switched off whole and
        // cannot end up underneath the death overlay Hud.prefab already holds.
        var canvas = prefab.GetComponent<Canvas>();

        Assert.That(canvas, Is.Not.Null, "the screen is not its own canvas.");

        var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/Hud.prefab");
        Canvas hudCanvas = hud.GetComponentInChildren<Canvas>(true);

        Assert.That(
            canvas.sortingOrder,
            Is.GreaterThan(hudCanvas.sortingOrder),
            "the level-up screen sorts at or below the HUD, so the death overlay can cover it.");

        // Rule 9 again, and rule 11's mechanism: a full-screen content root that follows the safe
        // area on a notched landscape phone.
        Assert.That(
            prefab.GetComponentInChildren<SafeAreaFitter>(true),
            Is.Not.Null,
            "no SafeAreaFitter on the content root, so a card can land under a notch.");
    }

    // ---- The View Tree label is a key, and the table answers it (M3-14c, rules 1, 3, 4, 8) -------

    [Test]
    public void Start_WritesTheViewTreeLabel()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen(Shipped());

        TMP_Text label = Field<TMP_Text>(_presenter, "_viewTreeLabel");

        // **The state the button is actually in when Start runs**, and it is why this row exists at
        // all: RefreshTreeButton switches the toggle off for a class with no tree and has not run
        // yet, so the write lands on a component whose object may well be inactive. That is Traps
        // §1's family — an API that takes a value has not agreed to honour it — and if this row is
        // ever red the write moves to RefreshTreeButton and M3-14c rule 3 gains a stated exception.
        label.gameObject.SetActive(false);

        RunStart();

        // Pinned against the shipped English.asset rather than a fixture table: inverting the word
        // has to be a red row somebody argues with. `ui.levelup.tree` and `ui.pause.tree` carry the
        // same word today and stay two keys — see LevelUpPresenter.ViewTreeKey's remarks.
        Assert.That(
            label.text,
            Is.EqualTo("View Tree"),
            "the write did not land on an inactive object.");

        label.gameObject.SetActive(true);

        Assert.That(
            label.text,
            Is.EqualTo("View Tree"),
            "the text did not survive the object being switched back on.");
    }

    [Test]
    public void Start_NoViewTreeLabelDressed_IsSilent()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen(Shipped());

        SetPrivate(_presenter, "_viewTreeLabel", null);

        // Softer than the three MissingReferenceExceptions Start throws above it, deliberately: a
        // screen with no cards stops the run for good, where a button with no *label* is a screen
        // the player can still use. MenuPresenter.Write's argument (M3-14c rule 5).
        Assert.That(() => RunStart(), Throws.Nothing);
    }

    // ---- Guard rows -----------------------------------------------------------------------------

    [Test]
    public void Construct_RefusesNullDependencies()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen();

        Assert.That(
            () => _presenter.Construct(null, _session, _hub, _catalog, Passthrough()),
            Throws.ArgumentNullException);

        Assert.That(
            () => _presenter.Construct(_session, null, _hub, _catalog, Passthrough()),
            Throws.ArgumentNullException);

        Assert.That(
            () => _presenter.Construct(_session, _session, null, _catalog, Passthrough()),
            Throws.ArgumentNullException);

        Assert.That(
            () => _presenter.Construct(_session, _session, _hub, null, Passthrough()),
            Throws.ArgumentNullException);

        Assert.That(
            () => _presenter.Construct(_session, _session, _hub, _catalog, null),
            Throws.ArgumentNullException,
            "a presenter with no localizer would hand every card a null and NullReference inside "
                + "an event handler (M3-14a).");
    }

    [Test]
    public void Show_RefusesNothingToDrawAndNobodyToTell()
    {
        var card = BuildLooseCard();

        Assert.That(
            () => card.Show(0, null, Passthrough(), _ => { }, isPact: false),
            Throws.ArgumentNullException,
            "a card with no node to draw is a presenter that read past the end of a short offer.");

        Assert.That(
            () => card.Show(0, Node(NodeOne, SkillKind.Passive), Passthrough(), null, isPact: false),
            Throws.ArgumentNullException,
            "a card with nobody to report to is one the player can tap while nothing happens — the "
                + "one failure here that is indistinguishable from a frozen game.");

        Assert.That(
            () => card.Show(-1, Node(NodeOne, SkillKind.Passive), Passthrough(), _ => { }, isPact: false),
            Throws.InstanceOf<ArgumentOutOfRangeException>(),
            "a card's index is its position in the offer, from 0.");
    }

    [Test]
    public void Place_IgnoresANonFiniteDpField()
    {
        StartRun(level: 2, pending: 1);
        BuildScreen();

        var before = new Vector2[_cards.Length];

        for (int i = 0; i < _cards.Length; i++)
        {
            before[i] = ((RectTransform)_cards[i].transform).sizeDelta;
        }

        // **The float door on this screen.** The dp fields are serialized so the owner can tune a
        // card on a device (rule 10, ledger row 4), which makes them an Inspector door rather than a
        // constant — and a NaN reaching sizeDelta is a RectTransform that never renders again. On
        // this screen that is a stopped game showing nothing, which reads exactly like a crash.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -12f })
        {
            SetPrivate(_presenter, "_cardSizeDp", new Vector2(bad, 260f));

            Assert.That(() => Place(), Throws.Nothing, $"Place threw on a card width of {bad}.");

            for (int i = 0; i < _cards.Length; i++)
            {
                Vector2 size = ((RectTransform)_cards[i].transform).sizeDelta;

                Assert.That(
                    float.IsFinite(size.x) && float.IsFinite(size.y),
                    Is.True,
                    $"a card width of {bad} wrote a non-finite sizeDelta, which is a rect that "
                        + "never renders again.");

                Assert.That(
                    size,
                    Is.EqualTo(before[i]),
                    $"a card width of {bad} was written through rather than leaving the prefab's "
                        + "authored layout alone.");
            }
        }

        // The gap is allowed to be zero — three cards touching is a layout, not a fault — so its
        // door is narrower than the size's and is checked separately.
        SetPrivate(_presenter, "_cardSizeDp", new Vector2(200f, 260f));
        SetPrivate(_presenter, "_cardGapDp", float.NaN);

        Assert.That(() => Place(), Throws.Nothing);

        for (int i = 0; i < _cards.Length; i++)
        {
            Assert.That(
                ((RectTransform)_cards[i].transform).anchoredPosition.x,
                Is.Not.NaN,
                "a NaN gap was written through into a card's position.");
        }
    }

    // ---- Fixture --------------------------------------------------------------------------------

    /// <summary>
    /// Starts a resumed run carrying <paramref name="pending"/> unspent picks, so a row can open a
    /// level-up without playing a stage to earn one.
    /// </summary>
    /// <remarks>
    /// Resumed rather than fresh because a fresh run is level 1 with nothing owed, and the only way
    /// to a pick from there is killing enough of a stage to cross a threshold — which would make
    /// every row here a stage test. <paramref name="level"/> has to account for what is taken and
    /// what is owed: M3-08a rule 9's identity refuses a saved run whose level cannot explain its
    /// own nodes, and it caught seven pre-existing fixtures on its first day.
    /// </remarks>
    private void StartRun(int level, int pending, string[] taken = null, bool withTree = true)
    {
        // No Keystone and no Upgrade in the *tree*, and that is a constraint rather than a
        // simplification: TreeRules refuses a Keystone that shares a tier with anything (CH §5),
        // and branch a's tier deliberately holds three so an untouched tree offers three. The four
        // kinds are still all exercised — Card_TintsByKind builds its specs directly, because a
        // tint is a property of one card rather than of a legal tree.
        IReadOnlyList<SkillSpec> skills = new[]
        {
            Node(NodeOne, SkillKind.Passive),
            Node(NodeTwo, SkillKind.Active),
            Node(NodeThree, SkillKind.Passive),
            Node(NodeFour, SkillKind.Passive),
            Node(NodeFive, SkillKind.Passive),
        };

        _catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Husk() },
            new[] { Mode() },
            withTree ? skills : Array.Empty<SkillSpec>(),
            withTree ? new[] { Tree() } : Array.Empty<SkillTreeSpec>());

        // The hub is the session's own IDomainEvents, so the screen hears what core published
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
            Array.Empty<ContentId>(),
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
    /// What the cards resolve their keys through. Defaults to an <em>empty</em> real
    /// <c>TableLocalizer</c>, which answers every key with itself — so every row written before
    /// M3-14a still asserts exactly what it asserted, and only the rows that pass a table are about
    /// English.
    /// </param>
    private void BuildScreen(ILocalizer localizer = null)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        _screen = Object.Instantiate(prefab);
        _spawned.Add(_screen);

        _presenter = _screen.GetComponent<LevelUpPresenter>();

        Assert.That(
            _presenter,
            Is.Not.Null,
            "LevelUpPresenter did not load off its own prefab (Traps §5).");

        _cards = Field<OfferCard[]>(_presenter, "_cards");

        // What RunScope's RegisterComponent does. Start never runs in EditMode, so nothing else on
        // this object has fired.
        _presenter.Construct(_session, _session, _hub, _catalog, localizer ?? Passthrough());
    }

    /// <summary>
    /// The real adapter over an empty table: every key resolves to itself.
    /// </summary>
    /// <remarks>
    /// The real <c>TableLocalizer</c> rather than a fake, deliberately — it costs nothing and it
    /// means the seven fixtures that only need <em>a</em> localizer are exercising the shipped miss
    /// path rather than a stand-in's imitation of it.
    /// </remarks>
    private static ILocalizer Passthrough() =>
        new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>());

    /// <summary>A card with no presenter around it, for the rows that are about one card.</summary>
    private OfferCard BuildLooseCard()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var screen = Object.Instantiate(prefab);

        _spawned.Add(screen);

        return Field<OfferCard[]>(screen.GetComponent<LevelUpPresenter>(), "_cards")[0];
    }

    /// <summary>
    /// A tap on card <paramref name="index"/>, through the button the player's thumb would hit.
    /// </summary>
    /// <remarks>
    /// <c>onClick.Invoke</c> rather than a synthetic pointer event, and the difference matters to
    /// rule 5: <c>Invoke</c> fires whether or not the button is interactable, which is the same
    /// thing that happens when two taps are dispatched from one <c>EventSystem</c> pass. A helper
    /// that checked <c>interactable</c> first would make <see cref="Tap_TwiceSpendsOnePick"/> pass
    /// against a presenter with no latch at all.
    /// </remarks>
    private void Tap(int index)
    {
        Field<Button>(_cards[index], "_button").onClick.Invoke();
    }

    /// <summary>
    /// One frame's worth of the presenter's own <c>Update</c> — what lifts rule 5's latch.
    /// </summary>
    /// <remarks>
    /// Invoked rather than waited for, because EditMode has no player loop. A row that needed a
    /// real frame would have to be a PlayMode row, and PROGRESS → Known issues is the standing
    /// argument against adding one of those without a reason.
    /// </remarks>
    private void PassAFrame()
    {
        typeof(LevelUpPresenter)
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_presenter, null);
    }

    /// <summary>
    /// The presenter's own <c>OnDestroy</c>. Invoked for <see cref="Destroy_DropsSubscriptions"/>'
    /// reason: Unity does not call it on an object whose <c>Awake</c> never ran, and none does here.
    /// </summary>
    private void Destroy()
    {
        typeof(LevelUpPresenter)
            .GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_presenter, null);
    }

    private void Place()
    {
        typeof(LevelUpPresenter)
            .GetMethod("Place", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_presenter, null);
    }

    /// <summary>The presenter's own <c>Start</c>, which Unity never runs in EditMode.</summary>
    private void RunStart()
    {
        typeof(LevelUpPresenter)
            .GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_presenter, null);
    }

    /// <summary>
    /// The real adapter over the <em>shipped</em> <c>English.asset</c> (M3-14c rule 8).
    /// </summary>
    /// <remarks>
    /// The shipped table rather than a fixture one, on the owner's standard for this task: a row
    /// that pins <em>"View Tree"</em> against a table this file wrote would go green over an
    /// <c>English.asset</c> that said anything at all.
    /// </remarks>
    private static ILocalizer Shipped()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No localization table at {EnglishPath}.");

        return new TableLocalizer(table);
    }

    private bool[] Interactables() =>
        _cards.Select(c => Field<Button>(c, "_button").interactable).ToArray();

    private float Alpha() => Field<CanvasGroup>(_presenter, "_root").alpha;

    private string LevelText() => Field<TMP_Text>(_presenter, "_levelLabel").text;

    private string PickText() => Field<TMP_Text>(_presenter, "_pickLabel").text;

    private static string NameOn(OfferCard card) => Field<TMP_Text>(card, "_name").text;

    private static string DescriptionOn(OfferCard card) => Field<TMP_Text>(card, "_description").text;

    private static string RotOn(OfferCard card) => Field<TMP_Text>(card, "_rot").text;

    private static Color TintFor(OfferCard card, SkillKind kind)
    {
        card.Show(0, Node($"skill.test.{kind}".ToLowerInvariant(), kind), Passthrough(), _ => { }, isPact: false);

        return Field<Image>(card, "_kindStrip").color;
    }

    private RunPause NewPause()
    {
        var pause = new RunPause();

        _pauses.Add(pause);

        return pause;
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);

    private static void SetPrivate(object target, string name, object value) =>
        target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);

    private static WorldSnapshot World(float dt) => new WorldSnapshot(Capacity)
    {
        Dt = dt,
        PlayerPosition = Vector3.Zero,
        HasGate = false,
        SpawnPoints = Array.Empty<Vector3>(),
    };

    /// <summary>
    /// One node of the requested kind, built to what <c>SkillSpec</c> actually accepts.
    /// </summary>
    /// <remarks>
    /// The two shapes here are not decoration: an Active must carry an <c>ActiveSpec</c> or it is a
    /// skill that cannot fire, and an Upgrade must name a parent or it could never be gated on
    /// anything (CH §4). Both were found by the validators rather than read off the spec.
    /// </remarks>
    private static SkillSpec Node(string id, SkillKind kind) => kind switch
    {
        SkillKind.Active => new SkillSpec(
            new ContentId(id),
            new LocKey($"{id}.name"),
            new LocKey($"{id}.desc"),
            SkillKind.Active,
            Array.Empty<IEffect>(),
            new ActiveSpec(
                6f,
                new TriggerSpec(new[]
                {
                    // Below 1.5 rather than a clause that reads as "always": HpFraction is at most
                    // 1, so this holds on every tick of a healthy run and of a hurt one.
                    new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 1.5f),
                }),
                new IEffect[]
                {
                    new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.1f),
                })),

        SkillKind.Upgrade => new SkillSpec(
            new ContentId(id),
            new LocKey($"{id}.name"),
            new LocKey($"{id}.desc"),
            SkillKind.Upgrade,
            new IEffect[]
            {
                new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f),
            },
            active: null,
            parentId: new ContentId(NodeOne)),

        // Every node that may carry a Pact carries one (M6-05b), so a real offer can roll one onto
        // any card but the Active's — and 15 Rot, so a take moves the meter without crossing 25.
        _ => new SkillSpec(
            new ContentId(id),
            new LocKey($"{id}.name"),
            new LocKey($"{id}.desc"),
            kind,
            new IEffect[]
            {
                new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f),
            },
            pact: new PactSpec(
                new IEffect[]
                {
                    new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.15f),
                },
                15f,
                new LocKey($"{id}.pact"))),
    };

    /// <summary>
    /// Five nodes over three branches, with branch a's whole first tier available at once so that
    /// an untouched tree offers three and an emptied branch a offers exactly two.
    /// </summary>
    private static SkillTreeSpec Tree() => new SkillTreeSpec(
        new ContentId(TreeId),
        new ContentId(OathboundId),
        new[]
        {
            Branch('a', new[] { NodeOne, NodeTwo, NodeThree }),
            Branch('b', new[] { NodeFour }),
            Branch('c', new[] { NodeFive }),
        });

    private static SkillBranchSpec Branch(char letter, string[] tier)
    {
        var ids = new ContentId[tier.Length];

        for (int i = 0; i < tier.Length; i++)
        {
            ids[i] = new ContentId(tier[i]);
        }

        return new SkillBranchSpec(
            new LocKey($"branch.{letter}"),
            new IReadOnlyList<ContentId>[] { ids });
    }

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
            new[] { new ContentId(ArenaId) });
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

    /// <summary>
    /// Publishes into the run's hub <em>and</em> into a recorder, so a row can both watch the screen
    /// react and count what core said.
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
