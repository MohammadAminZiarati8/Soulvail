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
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using CoreVector3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// CC §6.2's four slot buttons: which of them exist, what each is showing, what a tap on one
/// becomes, and where in the frame it becomes it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over a real <c>RunSession</c>, a real <c>SkillRunner</c> and the shipped <c>Hud.prefab</c></b> —
/// <c>SkillsPresenterTests</c>' shape and its reasons. A fake session would agree with whatever the
/// bar did, and the one thing every row here is about is what core actually holds in a slot; a
/// hand-built bar would test a second screen that happens to resemble the asset, and the failure
/// Traps §5 describes — a component that deserialises as null with nothing reporting it — is
/// invisible to a fixture that never loads the asset.
/// </para>
/// <para>
/// <b>Three rows drive a whole <c>RunTicker</c></b>, which is <c>ResumeFlowTests</c>' twenty-argument
/// fixture built once more, because M3-10a rule 3's claim is not <em>"the command arrives"</em> but
/// <em>"it arrives at one known point in the frame"</em> — and there is no way to observe that
/// without the object that owns the frame. The PlayMode half of the same claim is
/// <c>FrameOrderTests.Frame_SlotPollSitsBesideTapToFocus</c>.
/// </para>
/// <para>
/// <c>Awake</c> and <c>Start</c> do not run on an instantiated prefab in EditMode (Traps §5), so
/// <see cref="Bar"/> invokes <c>Start</c> by hand — the bar does its binding, its placement and its
/// first draw there, and every row would otherwise be testing an object that had never been told
/// which slot each button is.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SkillBarPresenterTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string TreeId = "tree.oathbound";
    private const string ArenaId = "arena.pillars";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    private const float Frame = 1f / 60f;

    /// <summary>The first active's authored cooldown; the <em>i</em>th is this plus <em>i</em>.</summary>
    /// <remarks>
    /// Above CH §4.1's floor of 40 % on its own base, so <c>EffectiveCooldownOf</c> hands this number
    /// straight back and the fill arithmetic below is the one a reader can do in their head.
    /// </remarks>
    private const float FirstCooldown = 2.5f;

    /// <summary>The button diameter the prefab ships, and the field's default. CC §6.2.</summary>
    private const float ButtonDp = 60f;

    /// <summary>Where this fixture's player stands — a kilometre out, like <c>FrameOrderTests</c>'.</summary>
    private static readonly Vector3 Origin = new Vector3(1000f, 0f, 1000f);

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private DomainEventHub _hub;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private RunSession _session;

    /// <summary>
    /// What the buttons resolve their labels through, when a row cares. Null means the empty
    /// passthrough, which answers every key with itself — so every row written before M3-14a still
    /// asserts exactly what it asserted.
    /// </summary>
    private ILocalizer _localizer;
    private RunConfig _config;
    private SkillSlotInput _input;

    private string[] _activeIds;

    private GameObject _hud;
    private SkillBarPresenter _bar;

    /// <summary>The snapshot the ticker fills, held so a row can look at it mid-frame.</summary>
    private WorldSnapshot _tickerSnapshot;

    /// <summary>
    /// Every cast core has published this row, collected from the first frame of the fixture.
    /// </summary>
    /// <remarks>
    /// Subscribed in <c>SetUp</c> rather than on first use, and that is not tidiness: a counter that
    /// armed itself lazily would read zero for casts made before the row asked, which is exactly the
    /// shape <see cref="Input_PressDoesNotOutliveTheFrame"/> is measuring. Counted through the hub
    /// rather than off the runner, because what every input row is about is whether a command
    /// <em>reached</em> core — a cooling slot is refused <em>after</em> it arrived, and the runner's
    /// state cannot tell that apart from a command never sent.
    /// </remarks>
    private readonly List<SkillCast> _casts = new List<SkillCast>();

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<IDisposable> _disposables = new List<IDisposable>();
    private readonly List<RunPause> _pauses = new List<RunPause>();

    private float _restoreTimeScale;
    private int _restoreTargetFrameRate;

    [SetUp]
    public void CreateWorld()
    {
        // The Editor's own globals. RunPause writes both, and a row that failed part way through
        // would otherwise leave the whole suite after it running at timeScale 0 —
        // PausePresenterTests' argument, inherited twice over.
        _restoreTimeScale = Time.timeScale;
        _restoreTargetFrameRate = Application.targetFrameRate;

        _hub = new DomainEventHub();
        _random = new FixedRandom(7, Alternating(8_192));
        _clock = new FixedClock(Instant);

        _casts.Clear();
        _localizer = null;

        Track(_hub.Subscribe<SkillCast>(evt => _casts.Add(evt)));
    }

    /// <summary>The real adapter over an empty table: every key resolves to itself.</summary>
    private static ILocalizer Passthrough() =>
        new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>());

    [TearDown]
    public void DestroyWorld()
    {
        for (int i = _disposables.Count - 1; i >= 0; i--)
        {
            _disposables[i].Dispose();
        }

        _disposables.Clear();

        foreach (RunPause pause in _pauses)
        {
            pause.Dispose();
        }

        _pauses.Clear();

        Time.timeScale = _restoreTimeScale;
        Application.targetFrameRate = _restoreTargetFrameRate;

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
        _bar = null;
        _input = null;
        _tickerSnapshot = null;
    }

    // ---- Which buttons exist (rules 5, 6) --------------------------------------------------------

    [Test]
    public void Bar_DrawsOnlyOccupiedSlots()
    {
        // A in S1, C in S3, and the hole in S2 is legal and ordinary (M3-07a rule 3) — CC §6.2 draws
        // four fixed positions, so compacting would slide S3's skill under the thumb that had
        // learned S2.
        StartRun(actives: 3, manual: new[] { 0, -1, 2, -1 });
        Bar();

        Assert.That(Shown(), Is.EqualTo(new[] { true, false, true, false }));

        Assert.That(Buttons()[0].Label, Is.EqualTo(Key(0)));
        Assert.That(Buttons()[2].Label, Is.EqualTo(Key(2)));
    }

    [Test]
    public void Bar_DrawsNoneWhenNothingIsManual()
    {
        // **Every run in the build**: nothing in Data/Trees ships an Active until M3-12, so every
        // owned skill is on Auto and the player sees exactly one button — the Charge.
        StartRun(actives: 3);
        Bar();

        Assert.That(Shown(), Is.EqualTo(new[] { false, false, false, false }));

        // And the Charge is untouched by any of it. It is a different class on a different object
        // with a different rule about tapping while cooling (rule 1), and this bar has no business
        // reaching it.
        SkillButton charge = _hud.GetComponentInChildren<SkillButton>(true);

        Assert.That(charge, Is.Not.Null, "The Charge did not load off Hud.prefab (Traps §5).");
        Assert.That(charge.gameObject.activeSelf, Is.True, "The bar took the Charge button off.");
        Assert.That(Field<float>(charge, "_sizeDp"), Is.EqualTo(72f), "CC §6.2's movement-skill size.");
    }

    [Test]
    public void Bar_RedrawsFromTheEvent()
    {
        StartRun(actives: 1);
        Bar();

        Assert.That(Shown()[0], Is.False, "The fixture's premise: A starts on Auto.");

        // The one event that can change which buttons exist, published by core rather than staged by
        // the fixture — SkillAutoCastChanged carries the id, the state and the slot (M3-07a rule 9).
        Commands().SetAutoCast(new ContentId(_activeIds[0]), auto: false);

        Assert.That(Shown(), Is.EqualTo(new[] { true, false, false, false }));
        Assert.That(Buttons()[0].Label, Is.EqualTo(Key(0)));
    }

    [Test]
    public void Bar_HidesOnTheWayBack()
    {
        StartRun(actives: 2, manual: new[] { 0, -1, 1, -1 });
        Bar();

        Assert.That(Shown(), Is.EqualTo(new[] { true, false, true, false }), "The fixture's premise.");

        // Back to Auto frees the slot, and the event says where the skill *is* — which is nowhere,
        // so it carries −1 rather than the slot it left.
        Commands().SetAutoCast(new ContentId(_activeIds[0]), auto: true);

        Assert.That(Shown(), Is.EqualTo(new[] { false, false, true, false }));

        // And nothing else moved: S3 still holds what it held, which is the whole of why a slot is a
        // position rather than a place in a list.
        Assert.That(Buttons()[2].Label, Is.EqualTo(Key(1)));
    }

    [Test]
    public void Bar_DrawsTheOpeningStateOnRunStarted()
    {
        // The bar exists *before* the run does, which is the order VContainer can produce and the
        // only one in which the event path is the thing under test.
        CreateRun(actives: 3, manual: new[] { 0, -1, 2, -1 });
        Bar();

        Assert.That(
            Shown(),
            Is.EqualTo(new[] { false, false, false, false }),
            "A bar in a scene where no run has begun drew something.");

        // A resumed run comes back with slots already filled (M3-07b), and no event describes a
        // frame that has not happened yet — so RunStarted is the only thing that can say so.
        _session.Start(_config);

        Assert.That(Shown(), Is.EqualTo(new[] { true, false, true, false }));
        Assert.That(Buttons()[0].Label, Is.EqualTo(Key(0)));
        Assert.That(Buttons()[2].Label, Is.EqualTo(Key(2)));
    }

    // ---- What one button is showing (rules 2, 7) -------------------------------------------------

    [Test]
    public void Button_FillTracksTheSlot()
    {
        StartRun(actives: 1, manual: new[] { 0, -1, -1, -1 });
        Bar();

        ManualSkillButton button = Buttons()[0];
        Image fill = Field<Image>(button, "_radialFill");

        Assert.That(fill.fillAmount, Is.EqualTo(1f).Within(1e-3f), "A live skill draws a full ring.");

        Commands().CastSkill(0);

        Draw(button);

        // Empty the instant the cast starts: the fill is 1 − fraction, which is SkillButton's
        // convention and deliberately the same one, because M3-10a draws both side by side.
        Assert.That(fill.fillAmount, Is.EqualTo(0f).Within(1e-3f));

        // Half the wait gone, half the ring back. The number is core's — RunState.SlotCooldownFraction
        // measured against the *current* effective cooldown — and this class only draws it.
        TickRun(FirstCooldown / 2f);
        Draw(button);

        Assert.That(fill.fillAmount, Is.EqualTo(0.5f).Within(0.05f));

        TickRun(FirstCooldown);
        Draw(button);

        Assert.That(fill.fillAmount, Is.EqualTo(1f).Within(1e-3f));
    }

    [Test]
    public void Button_DimsAndGoesDeadWhileCooling()
    {
        StartRun(actives: 1, manual: new[] { 0, -1, -1, -1 });
        Bar();

        ManualSkillButton button = Buttons()[0];
        CanvasGroup group = Field<CanvasGroup>(button, "_group");
        Button ui = Field<Button>(button, "_button");

        Assert.That(group.alpha, Is.EqualTo(1f).Within(1e-3f));
        Assert.That(ui.interactable, Is.True);

        Commands().CastSkill(0);
        Draw(button);

        // **CC §6.2's "unavailable: 40 % opacity, no tap response", both halves** (rule 7) — and the
        // second half is the line SkillButton deliberately does not have, because CC §5's 0.15 s
        // buffer only exists if an early press on the *Charge* actually reaches core.
        Assert.That(group.alpha, Is.EqualTo(0.4f).Within(1e-3f));
        Assert.That(ui.interactable, Is.False);

        TickRun(FirstCooldown + Frame);
        Draw(button);

        Assert.That(group.alpha, Is.EqualTo(1f).Within(1e-3f));
        Assert.That(ui.interactable, Is.True);
    }

    [Test]
    public void Button_CoolingTapSendsNothing()
    {
        StartRun(actives: 1, manual: new[] { 0, -1, -1, -1 });
        Bar();

        ManualSkillButton button = Buttons()[0];

        Commands().CastSkill(0);
        Draw(button);

        int castsBefore = CountCasts();

        // Straight through the handler rather than through `interactable`, which uGUI would have
        // refused for us. The claim is that a cooling slot does not send — not that a flag somebody
        // could clear from anywhere happens to be down.
        Field<Button>(button, "_button").onClick.Invoke();

        Assert.That(
            Field<int>(_input, "_pressed"),
            Is.EqualTo(-1),
            "The buffer took a press from a cooling button, so the poll would spend a cooldown the "
                + "screen had already said no to.");

        _input.Poll();

        Assert.That(CountCasts(), Is.EqualTo(castsBefore), "A cooling tap reached core.");
    }

    [Test]
    public void Button_WritesOnlyOnChange()
    {
        StartRun(actives: 1, manual: new[] { 0, -1, -1, -1 });
        Bar();

        ManualSkillButton button = Buttons()[0];
        Image fill = Field<Image>(button, "_radialFill");

        Draw(button);

        // A sentinel nothing should overwrite. Assigning fillAmount marks the graphic dirty and
        // queues a canvas rebuild, so a button that wrote an unchanged value would rebuild the HUD
        // on every frame of every second a skill is not being cast — SkillButton's argument with
        // four more buttons hanging off it.
        fill.fillAmount = 0.42f;

        for (int frame = 0; frame < 60; frame++)
        {
            Draw(button);
        }

        Assert.That(
            fill.fillAmount,
            Is.EqualTo(0.42f).Within(1e-4f),
            "The button repainted a frame in which nothing had changed.");

        // And it *does* write when something changes, so the row above is about redundancy rather
        // than about a button that has stopped drawing.
        Commands().CastSkill(0);
        Draw(button);

        Assert.That(fill.fillAmount, Is.EqualTo(0f).Within(1e-3f));
    }

    // ---- The tap, and when it becomes a command (rules 3, 4, 10) ---------------------------------

    [Test]
    public void Input_PressIsSentInCommandPhase()
    {
        StartRun(actives: 1, manual: new[] { 0, -1, -1, -1 });
        Bar();

        RunTicker ticker = Ticker();

        CoreVector3 snapshotAtCommandTime = CoreVector3.One;
        int casts = 0;

        Track(_hub.Subscribe<SkillCast>(_ =>
        {
            casts++;
            snapshotAtCommandTime = _tickerSnapshot.PlayerPosition;
        }));

        // A thumb, whenever in the frame uGUI reported it.
        Field<Button>(Buttons()[0], "_button").onClick.Invoke();

        Assert.That(casts, Is.Zero, "The press reached core without a frame asking for it.");

        ticker.Tick();

        Assert.That(casts, Is.EqualTo(1), "The slot poll never ran, so a tap on S1 casts nothing.");

        // **The claim, and it is not merely that the command landed** (rule 3). The player stands a
        // kilometre from the origin and the snapshot still read zero when CastSkill arrived, so
        // nothing had written this frame's senses into it yet — the command is above the snapshot
        // build, which is where CommandPhase sits and where AR §18.1 says it has to.
        Assert.That(
            snapshotAtCommandTime.X,
            Is.EqualTo(0f).Within(1e-3f),
            "The command landed after the snapshot was built, so a cast would be acted on against "
                + "senses taken before the player asked for it.");

        Assert.That(
            _tickerSnapshot.PlayerPosition.X,
            Is.EqualTo(Origin.x).Within(1e-2f),
            "Sanity: the frame built its snapshot, so the assertion above is about ordering.");
    }

    [Test]
    public void Input_PressOutsideATickIsHeld()
    {
        StartRun(actives: 1, manual: new[] { 0, -1, -1, -1 });
        Bar();

        RunTicker ticker = Ticker();

        _input.Press(0);

        Assert.That(CountCasts(), Is.Zero, "A press turned itself into a command with no frame.");

        ticker.Tick();

        Assert.That(CountCasts(), Is.EqualTo(1), "The held press was dropped rather than sent.");
    }

    [Test]
    public void Input_TwoPressesInOneFrameSendOne()
    {
        StartRun(actives: 2, manual: new[] { 0, -1, 1, -1 });
        Bar();

        var sent = new List<ContentId>();

        Track(_hub.Subscribe<SkillCast>(evt => sent.Add(evt.SkillId)));

        // Two thumbs, or a bug. Casting both would spend two cooldowns on an input the player did
        // not distinguish (rule 4), so the buffer holds one int and the last press wins.
        _input.Press(0);
        _input.Press(2);

        _input.Poll();

        Assert.That(sent, Is.EqualTo(new[] { new ContentId(_activeIds[1]) }));
    }

    [Test]
    public void Input_PollClearsEvenWhenEmpty()
    {
        StartRun(actives: 1, manual: new[] { 0, -1, -1, -1 });

        _input.Poll();
        _input.Poll();

        Assert.That(CountCasts(), Is.Zero);
        Assert.That(Field<int>(_input, "_pressed"), Is.EqualTo(-1));
    }

    [Test]
    public void Input_PressDoesNotOutliveTheFrame()
    {
        StartRun(actives: 1, manual: new[] { 0, -1, -1, -1 });

        _input.Press(0);

        _input.Poll();
        _input.Poll();

        // Exactly one, and it is the *cleared on Poll whether or not it sent* half of rule 4 rather
        // than the cooldown refusing the second: core answers a cooling CastSlot with false rather
        // than a throw, so a buffer that kept the press would look identical here and differ the
        // moment the cooldown ended.
        Assert.That(CountCasts(), Is.EqualTo(1));
    }

    [Test]
    public void Input_NoCommandAfterTheRunEnds()
    {
        StartRun(actives: 1, manual: new[] { 0, -1, -1, -1 });
        Bar();

        RunTicker ticker = Ticker();

        _session.End();

        _input.Press(0);

        // **RunTicker's guard is what saves it** (rule 10). CommandPhase sits below the IsRunning
        // check, so the poll never happens — and the next assertion is what makes that a claim about
        // the guard rather than about the buffer being empty.
        Assert.DoesNotThrow(() => ticker.Tick());

        Assert.That(CountCasts(), Is.Zero);

        Assert.Throws<InvalidOperationException>(
            () => ((IPlayerCommands)_session).CastSkill(0),
            "IPlayerCommands stopped refusing a command outside a run, so the guard above is no "
                + "longer the thing keeping this row green.");
    }

    // ---- Where they sit (rule 8) -----------------------------------------------------------------

    [Test]
    public void Button_IsPlacedInDp()
    {
        StartRun(actives: 4, manual: new[] { 0, 1, 2, 3 });
        Bar();

        float pxPerDp = PixelsPerDp();
        Vector2[] offsets = Field<Vector2[]>(_bar, "_offsetsDp");
        Vector2 margin = Field<Vector2>(_bar, "_anchorMarginDp");

        for (int slot = 0; slot < SkillRunner.MaxManualSlots; slot++)
        {
            var rect = (RectTransform)Buttons()[slot].transform;

            // 60 dp across, converted at the screen boundary — a Scale-With-Screen-Size canvas
            // measures in reference pixels, and 60 of those is a different physical size on every
            // phone (SkillButton.Place's reason, and a thumb does not scale with the display).
            Assert.That(
                rect.sizeDelta.x,
                Is.EqualTo(ButtonDp * pxPerDp).Within(1e-2f),
                $"S{slot + 1} is not 60 dp across.");

            Assert.That(rect.sizeDelta.y, Is.EqualTo(rect.sizeDelta.x).Within(1e-3f));

            // Anchored bottom-right, so SafeAreaFitter insets the cluster away from the gesture bar
            // rather than the screen edge putting a button under it.
            Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(1f, 0f)));

            Assert.That(
                rect.anchoredPosition.x,
                Is.EqualTo(-(margin.x + offsets[slot].x) * pxPerDp).Within(1e-2f),
                $"S{slot + 1} is not at its serialized offset from the Charge's corner.");

            Assert.That(
                rect.anchoredPosition.y,
                Is.EqualTo((margin.y + offsets[slot].y) * pxPerDp).Within(1e-2f));
        }
    }

    [Test]
    public void Button_SpacingMeetsTheMinimum()
    {
        StartRun(actives: 4, manual: new[] { 0, 1, 2, 3 });
        Bar();

        Vector2[] offsets = Field<Vector2[]>(_bar, "_offsetsDp");

        // CC §6.2's table: 60 dp buttons with 12 dp of clearance, so two centres may never be closer
        // than 72 dp. The offsets are the owner's to tune on a phone (ledger row 4) and this is the
        // one thing about them that is not a matter of taste.
        float minimum = ButtonDp + SkillBarPresenter.MinimumSpacingDp;

        for (int a = 0; a < offsets.Length; a++)
        {
            for (int b = a + 1; b < offsets.Length; b++)
            {
                Assert.That(
                    Vector2.Distance(offsets[a], offsets[b]),
                    Is.GreaterThanOrEqualTo(minimum),
                    $"S{a + 1} and S{b + 1} are closer than {minimum} dp, so one thumb covers both.");
            }
        }

        // And each against the Charge, which sits at the anchor itself: a 72 dp button beside a
        // 60 dp one wants (72 + 60) / 2 + 12 between centres.
        float fromCharge = ((72f + ButtonDp) / 2f) + SkillBarPresenter.MinimumSpacingDp;

        for (int a = 0; a < offsets.Length; a++)
        {
            Assert.That(
                offsets[a].magnitude,
                Is.GreaterThanOrEqualTo(fromCharge),
                $"S{a + 1} crowds the Charge button it is clustered around.");
        }
    }

    [Test]
    public void Bar_LeavesTheLayoutAloneForANonFiniteDpField()
    {
        StartRun(actives: 4, manual: new[] { 0, 1, 2, 3 });
        Bar();

        var rect = (RectTransform)Buttons()[0].transform;
        Vector2 placed = rect.anchoredPosition;
        Vector2 sized = rect.sizeDelta;

        // **These buttons are authored on Hud.prefab, so leaving the layout alone is an answer**
        // (PausePresenter.Place's, not TreeViewPresenter.Layout's) — a tree cell is a runtime clone
        // with nothing to fall back to, and a slot button has the prefab's own cluster. Every one of
        // the three doors takes the same answer: the placement simply does not happen.
        // Zero is deliberately absent: a margin of 0 is a button flush against the safe area's
        // corner, which is a layout the owner is entitled to ask for. Only nonsense is refused.
        foreach (float bad in new[]
                 {
                     float.NaN, float.PositiveInfinity, float.NegativeInfinity, -12f,
                 })
        {
            SetPrivate(_bar, "_anchorMarginDp", new Vector2(bad, 96f));
            Invoke(_bar, "Place");

            Assert.That(rect.anchoredPosition, Is.EqualTo(placed), $"anchor margin {bad}");
            Assert.That(rect.sizeDelta, Is.EqualTo(sized), $"anchor margin {bad}");
        }

        SetPrivate(_bar, "_anchorMarginDp", new Vector2(96f, 96f));

        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            SetPrivate(_bar, "_offsetsDp", new[]
            {
                new Vector2(bad, 0f), new Vector2(121f, 70f), new Vector2(70f, 121f), new Vector2(0f, 140f),
            });

            Invoke(_bar, "Place");

            Assert.That(rect.anchoredPosition, Is.EqualTo(placed), $"offset {bad}");
        }

        SetPrivate(_bar, "_offsetsDp", new[]
        {
            new Vector2(140f, 0f), new Vector2(121f, 70f), new Vector2(70f, 121f), new Vector2(0f, 140f),
        });

        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, 0f, -60f })
        {
            SetPrivate(Buttons()[0], "_sizeDp", bad);
            Invoke(_bar, "Place");

            Assert.That(rect.sizeDelta, Is.EqualTo(sized), $"size {bad}");
            Assert.That(rect.anchoredPosition, Is.EqualTo(placed), $"size {bad}");
        }
    }

    // ---- What it says, and what it refuses to know (rules 1, 9, 11) ------------------------------

    [Test]
    public void Bar_LabelsAreWords()
    {
        StartRun(actives: 1, manual: new[] { 0, -1, -1, -1 }, activeIds: new[] { "skill.oathbound.consecrate" });

        _localizer = new DictionaryLocalizer("skill.oathbound.consecrate.name", "Consecrate");

        Bar();

        // **M3-10a's `Bar_LabelsAreKeys`, inverted** — ledger row 9's fifth reader, and the one
        // whose closure is a *device* question rather than a table one (rule 9). A 60 dp circle
        // could never hold `skill.oathbound.consecrate.name`; whether it holds "Consecrate"
        // legibly at 8 pt under a thumb is [ledger row 4] and nobody has looked at it on a phone.
        Assert.That(Buttons()[0].Label, Is.EqualTo("Consecrate"));
        Assert.That(Buttons()[0].Label, Does.Not.StartWith("skill."));
    }

    /// <summary>A slot whose name has no row still puts the key under the thumb (rule 1).</summary>
    [Test]
    public void Bar_MissingRowFallsBackToTheKey()
    {
        StartRun(actives: 1, manual: new[] { 0, -1, -1, -1 }, activeIds: new[] { "skill.oathbound.consecrate" });
        Bar();

        Assert.That(Buttons()[0].Label, Is.EqualTo("skill.oathbound.consecrate.name"));
    }

    [Test]
    public void Bar_KnowsNothingAboutAuto()
    {
        StartRun(actives: 2, manual: new[] { 0, -1, -1, -1 });
        Bar();

        // Two owned actives, one Manual and one Auto: exactly one button, and the Auto one produced
        // none by virtue of holding no slot rather than by being asked about (rule 11).
        Assert.That(Shown(), Is.EqualTo(new[] { true, false, false, false }));

        // And the class cannot have asked, which is the half a behavioural row cannot show. GD
        // §16.1's "auto-cast skills need visible cooldowns" is a different readout in a different
        // corner and it is M3-10b's; a bar that read IsAutoCast would be that task starting here.
        MethodInfo isAutoCast = typeof(RunState).GetMethod(nameof(RunState.IsAutoCast));

        Assert.That(isAutoCast, Is.Not.Null, "RunState.IsAutoCast is gone — this row is out of date.");

        Assert.That(
            Calls(typeof(SkillBarPresenter), isAutoCast),
            Is.False,
            "SkillBarPresenter calls RunState.IsAutoCast.");

        Assert.That(
            Calls(typeof(ManualSkillButton), isAutoCast),
            Is.False,
            "ManualSkillButton calls RunState.IsAutoCast.");
    }

    [Test]
    public void Button_SharesNoBaseClassWithTheCharge()
    {
        // **Rule 1 is a refusal, and this is where it is written down.** The two differ on exactly
        // one axis — SkillButton stays tappable while cooling so CC §5's 0.15 s buffer stays
        // reachable, and this one does not because CC §6.2's rule was written for it — so a shared
        // base would put the single behaviour that differs behind a virtual and invite the next
        // reader to unify them.
        Assert.That(typeof(ManualSkillButton).BaseType, Is.EqualTo(typeof(MonoBehaviour)));
        Assert.That(typeof(SkillButton).BaseType, Is.EqualTo(typeof(MonoBehaviour)));

        Assert.That(
            typeof(ManualSkillButton).IsAssignableFrom(typeof(SkillButton)),
            Is.False);

        Assert.That(
            typeof(SkillButton).IsAssignableFrom(typeof(ManualSkillButton)),
            Is.False);

        // The one thing they do share, reached from either side's own layout rather than from a
        // common parent.
        Assert.That(
            typeof(StickShaper).GetMethod(nameof(StickShaper.PixelsPerDp)),
            Is.Not.Null,
            "The single piece of sharing rule 1 permits is gone.");
    }

    [Test]
    public void Construct_RefusesNull()
    {
        StartRun(actives: 1);
        Bar();

        Assert.Throws<ArgumentNullException>(() => new SkillSlotInput(null));

        Assert.Throws<ArgumentNullException>(
            () => _bar.Construct(null, _input, _hub, _catalog, Passthrough()));

        Assert.Throws<ArgumentNullException>(
            () => _bar.Construct(_session, null, _hub, _catalog, Passthrough()));

        Assert.Throws<ArgumentNullException>(
            () => _bar.Construct(_session, _input, null, _catalog, Passthrough()));

        Assert.Throws<ArgumentNullException>(
            () => _bar.Construct(_session, _input, _hub, null, Passthrough()));

        Assert.Throws<ArgumentNullException>(
            () => _bar.Construct(_session, _input, _hub, _catalog, null));
    }

    // ---- The asset (Traps §5) ---------------------------------------------------------------------

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        // Read off the asset rather than off an instance: the failure Traps §5 describes is a
        // component that deserialises as null with nothing reported anywhere, and it is only visible
        // from here.
        var bar = prefab.GetComponentInChildren<SkillBarPresenter>(true);

        Assert.That(bar, Is.Not.Null, "SkillBarPresenter did not load off Hud.prefab (Traps §5).");

        var buttons = Field<ManualSkillButton[]>(bar, "_buttons");

        Assert.That(buttons, Is.Not.Null.And.Length.EqualTo(SkillRunner.MaxManualSlots));

        for (int slot = 0; slot < buttons.Length; slot++)
        {
            Assert.That(buttons[slot], Is.Not.Null, $"S{slot + 1} is not assigned.");

            Assert.That(Field<Image>(buttons[slot], "_radialFill"), Is.Not.Null, $"S{slot + 1} fill");
            Assert.That(Field<CanvasGroup>(buttons[slot], "_group"), Is.Not.Null, $"S{slot + 1} group");
            Assert.That(Field<Button>(buttons[slot], "_button"), Is.Not.Null, $"S{slot + 1} button");
            Assert.That(Field<TMP_Text>(buttons[slot], "_label"), Is.Not.Null, $"S{slot + 1} label");

            Assert.That(
                Field<float>(buttons[slot], "_sizeDp"),
                Is.EqualTo(ButtonDp),
                $"S{slot + 1} is not CC §6.2's 60 dp.");

            Image fill = Field<Image>(buttons[slot], "_radialFill");

            Assert.That(fill.type, Is.EqualTo(Image.Type.Filled), $"S{slot + 1} fill is not Filled");
            Assert.That(
                fill.fillMethod,
                Is.EqualTo(Image.FillMethod.Radial360),
                $"S{slot + 1} fill is not Radial360, so fillAmount draws a bar rather than a ring.");
        }

        Assert.That(
            Field<Vector2[]>(bar, "_offsetsDp"),
            Is.Not.Null.And.Length.EqualTo(SkillRunner.MaxManualSlots));

        // **The Charge is unchanged**, which is the half of this row that is about the prefab having
        // been edited rather than about the new objects on it. Hud.prefab had not been touched since
        // M2-12a, and it is the one asset every screen in the game sorts against.
        var charge = prefab.GetComponentInChildren<SkillButton>(true);

        Assert.That(charge, Is.Not.Null, "The Charge did not load off Hud.prefab.");
        Assert.That(Field<Image>(charge, "_radialFill"), Is.Not.Null);
        Assert.That(Field<CanvasGroup>(charge, "_group"), Is.Not.Null);
        Assert.That(Field<float>(charge, "_sizeDp"), Is.EqualTo(72f));
        Assert.That(Field<Vector2>(charge, "_marginDp"), Is.EqualTo(new Vector2(96f, 96f)));

        var chargeRect = (RectTransform)charge.transform;

        Assert.That(chargeRect.anchoredPosition, Is.EqualTo(new Vector2(-240f, 240f)));
        Assert.That(chargeRect.sizeDelta, Is.EqualTo(new Vector2(180f, 180f)));

        // And the cluster hangs under the safe area, like everything else on this prefab, so a
        // gesture bar moves it rather than covering it. `includeInactive` is mandatory rather than
        // defensive: every object on a prefab *asset* reports `activeInHierarchy` false, because it
        // is in no scene — so the default overload answers null for a correctly dressed prefab.
        Assert.That(
            bar.GetComponentInParent<SafeAreaFitter>(includeInactive: true),
            Is.Not.Null,
            "The skill bar is not under a SafeAreaFitter, so S1 can land under the gesture bar.");
    }

    // ---- Fixture -------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a resumed run owning <paramref name="actives"/> actives, without starting it.
    /// </summary>
    /// <param name="manual">
    /// Which owned active sits in each of the four slots, by its index in
    /// <paramref name="actives"/> — and −1 for an empty one, because a hole is legal and ordinary
    /// (M3-07a rule 3).
    /// </param>
    /// <remarks>
    /// Resumed rather than fresh for <c>SkillsPresenterTests</c>' reason: it is the only door
    /// <c>RunSession.Start</c> adds a restored Active through short of playing a level-up, and the
    /// only one that can state a loadout. The level accounts for the nodes (M3-08a rule 9).
    /// </remarks>
    private void CreateRun(int actives, int[] manual = null, string[] activeIds = null)
    {
        _activeIds = new string[actives];

        var skills = new List<SkillSpec>();
        var activeTier = new List<ContentId>();
        var taken = new List<ContentId>();

        for (int i = 0; i < actives; i++)
        {
            _activeIds[i] = activeIds is not null && i < activeIds.Length
                ? activeIds[i]
                : $"skill.test.a{i}";

            SkillSpec node = ActiveNode(_activeIds[i], FirstCooldown + i);

            skills.Add(node);
            activeTier.Add(node.Id);
            taken.Add(node.Id);
        }

        // A branch needs at least one node and a tree wants exactly three branches, so a run owning
        // no actives borrows a passive for branch a.
        if (activeTier.Count == 0)
        {
            SkillSpec filler = PassiveNode("skill.test.p90");

            skills.Add(filler);
            activeTier.Add(filler.Id);
        }

        SkillSpec second = PassiveNode("skill.test.p0");
        SkillSpec third = PassiveNode("skill.test.p1");

        skills.Add(second);
        skills.Add(third);

        var tree = new SkillTreeSpec(
            new ContentId(TreeId),
            new ContentId(OathboundId),
            new[]
            {
                new SkillBranchSpec(new LocKey("branch.a"), new IReadOnlyList<ContentId>[] { activeTier }),
                new SkillBranchSpec(new LocKey("branch.b"), new IReadOnlyList<ContentId>[] { new[] { second.Id } }),
                new SkillBranchSpec(new LocKey("branch.c"), new IReadOnlyList<ContentId>[] { new[] { third.Id } }),
            });

        _catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Husk() },
            new[] { Mode() },
            skills,
            new[] { tree });

        _session = new RunSession(
            _catalog,
            _random,
            _hub,
            new RecordingIntents(),
            new RunRecorder(_random, _clock, _hub),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        _input = new SkillSlotInput(_session);

        var slots = new ContentId[SkillRunner.MaxManualSlots];

        if (manual is not null)
        {
            for (int slot = 0; slot < slots.Length && slot < manual.Length; slot++)
            {
                if (manual[slot] >= 0)
                {
                    slots[slot] = new ContentId(_activeIds[manual[slot]]);
                }
            }
        }

        _config = new RunConfig(
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
                new RandomState(101, 102, 103, 104, 105),
                90f,
                6f,
                120f,
                Instant,
                1 + taken.Count,
                0f,
                0,
                taken.ToArray(),
                slots));
    }

    /// <summary><see cref="CreateRun"/>, started.</summary>
    private void StartRun(int actives, int[] manual = null, string[] activeIds = null)
    {
        CreateRun(actives, manual, activeIds);

        _session.Start(_config);
    }

    /// <summary>
    /// Instantiates the shipped HUD and injects the bar on it — the screen under test is the asset.
    /// </summary>
    private SkillBarPresenter Bar()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        _hud = Object.Instantiate(prefab);
        _spawned.Add(_hud);

        _bar = _hud.GetComponentInChildren<SkillBarPresenter>(true);

        Assert.That(_bar, Is.Not.Null, "SkillBarPresenter did not load off Hud.prefab (Traps §5).");

        // Pinned, because a Canvas instantiated in EditMode has never been driven by its own
        // CanvasScaler and the layout rows measure in dp against this — SkillsPresenterTests sets it
        // for the same reason.
        _hud.GetComponent<Canvas>().scaleFactor = 1f;

        // What RunScope's RegisterComponent does, and then the Start that Unity does not run on an
        // instantiated prefab in EditMode: the binding, the placement and the first draw all live
        // there.
        _bar.Construct(_session, _input, _hub, _catalog, _localizer ?? Passthrough());

        Invoke(_bar, "Start");

        return _bar;
    }

    /// <summary>
    /// A whole <c>RunTicker</c> over this fixture's session — <c>ResumeFlowTests.StartTicker</c>'s
    /// twenty-one arguments, six of them scene objects.
    /// </summary>
    /// <remarks>
    /// Every dependency is guarded by the constructor, so all of them have to be real even though
    /// three rows touch two. That is the price of the guard doing its job, and it buys the only
    /// EditMode observation of <em>where in the frame</em> a slot press becomes a command.
    /// </remarks>
    private RunTicker Ticker()
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");

        Assert.That(enemyLayer, Is.GreaterThanOrEqualTo(0), "The project has no Enemy layer.");

        var builder = new ContainerBuilder();
        IObjectResolver container = Track(builder.Build());

        var root = new GameObject("Player");

        root.transform.position = Origin;

        _spawned.Add(root);

        // PlayerView first: adding it brings the CharacterController it requires.
        PlayerView player = root.AddComponent<PlayerView>();
        player.Construct(_hub);

        ChargeMotion charge = root.AddComponent<ChargeMotion>();

        var enemyViews = Track(new EnemyViews(
            container,
            Template<EnemyView>("EnemyTemplate", enemyLayer),
            null,
            _hub,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()),
            prewarm: 0));

        var projectileViews = Track(new ProjectileViews(
            container, Template<ProjectileView>("ProjectileTemplate"), null, _hub));

        var rings = Track(new TelegraphRings(
            container, Template<TelegraphRingView>("RingTemplate"), null, _hub, prewarm: 0));

        // Empty, on the rings' terms: this fixture casts through the bar rather than through core,
        // so no ZoneSpawned is ever published. It is here because the ticker takes one (M3-11c).
        var zones = Track(new ZoneViews(
            container, Template<ZoneView>("ZoneTemplate"), null, _hub, prewarm: 0));

        // Empty on the zones' terms, and with a null arena pool besides: nothing in this fixture
        // reaches a boss, so no ring, crack or shell is ever rented. It is here because the ticker
        // takes one (M4-03).
        var boss = Track(new BossViews(
            container,
            Template<ShockwaveView>("ShockwaveTemplate"),
            Template<FissureView>("FissureTemplate"),
            Template<BossBeatView>("BeatTemplate"),
            null,
            _hub,
            enemyViews,
            null,
            shockwavePrewarm: 0,
            fissurePrewarm: 0,
            beatPrewarm: 0));

        var input = Track(new InputAdapter());

        var cameraObject = new GameObject("Camera");

        _spawned.Add(cameraObject);

        var cone = new ConeOverlapQuery(ConeOverlapQuery.DefaultCapacity, 1 << enemyLayer, enemyViews);

        charge.Construct(enemyViews, 1 << enemyLayer);

        var pause = new RunPause();

        _pauses.Add(pause);

        _tickerSnapshot = new WorldSnapshot(Capacity);

        return new RunTicker(
            _session,
            _session,
            _session,
            pause,
            new PendingRun(),
            _catalog,
            _random,
            _tickerSnapshot,
            new SnapshotBuilder(player, input, enemyViews, null, null, null),
            new IntentBuffer(),
            player,
            charge,
            enemyViews,
            projectileViews,
            rings,
            zones,
            boss,
            Track(new SaveWriter(new InertSaveStore(), _hub)),
            input,
            SpawnPlan.Empty,
            new TapToFocusAdapter(input, _session, cameraObject.AddComponent<Camera>()),
            _input,
            cone);
    }

    /// <summary>Advances the run by <paramref name="seconds"/> of simulated time.</summary>
    /// <remarks>
    /// Straight into the session rather than through the ticker: what the cooldown rows are about is
    /// the number <c>RunState</c> hands a button, and the frame that carries it is another row's.
    /// </remarks>
    private void TickRun(float seconds)
    {
        int frames = Mathf.CeilToInt(seconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            _session.Tick(new WorldSnapshot(Capacity)
            {
                Dt = Frame,
                PlayerPosition = CoreVector3.Zero,
            });
        }
    }

    /// <summary>One frame of a button's <c>Update</c> — what re-reads its two numbers.</summary>
    private static void Draw(ManualSkillButton button) => Invoke(button, "Update");

    private ManualSkillButton[] Buttons() => Field<ManualSkillButton[]>(_bar, "_buttons");

    private bool[] Shown() => Buttons().Select(b => b != null && b.IsShown).ToArray();

    private IPlayerCommands Commands() => _session;

    private string Key(int active) => $"{_activeIds[active]}.name";

    /// <summary>How many casts core has published this row — see <see cref="_casts"/>.</summary>
    private int CountCasts() => _casts.Count;

    /// <summary>
    /// How many canvas units one dp is worth here — <c>SkillBarPresenter.PixelsPerDp</c>, with the
    /// scale <see cref="Bar"/> pinned.
    /// </summary>
    private static float PixelsPerDp() => StickShaper.PixelsPerDp(Screen.dpi);

    private T Template<T>(string name, int layer = 0)
        where T : Component
    {
        var root = new GameObject(name) { layer = layer };

        // Inactive first, so neither Awake nor Start runs on the template — VContainer deactivates a
        // prefab before instantiating and injects the copy while it is off.
        root.SetActive(false);

        _spawned.Add(root);

        return root.AddComponent<T>();
    }

    private T Track<T>(T disposable)
        where T : IDisposable
    {
        _disposables.Add(disposable);

        return disposable;
    }

    /// <summary>
    /// Whether <paramref name="type"/>, or anything the compiler nested inside it, calls
    /// <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// <b>The IL, because the question is about a call rather than about a field.</b> Every
    /// <c>call</c> and <c>callvirt</c> in the type's own bodies is resolved back through the module
    /// that holds it, which is the only way to say "this class never asks that question" — a
    /// behavioural row can show that the answer was not used, never that it was not obtained. A token
    /// that resolves to nothing is skipped rather than believed: a four-byte window after an opcode
    /// byte that happened to fall inside an operand is not a call, and the resolve is what says so.
    /// </remarks>
    private static bool Calls(Type type, MethodInfo target)
    {
        const BindingFlags Everything =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        var types = new List<Type> { type };

        types.AddRange(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));

        foreach (Type candidate in types)
        {
            foreach (MethodBase method in candidate.GetMethods(Everything).Cast<MethodBase>()
                         .Concat(candidate.GetConstructors(Everything)))
            {
                MethodBody body = method.GetMethodBody();

                byte[] il = body?.GetILAsByteArray();

                if (il is null)
                {
                    continue;
                }

                for (int i = 0; i + 4 < il.Length; i++)
                {
                    // call (0x28) and callvirt (0x6F). Nothing else reaches an instance method.
                    if (il[i] != 0x28 && il[i] != 0x6F)
                    {
                        continue;
                    }

                    MethodBase called;

                    try
                    {
                        called = candidate.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (called is not null
                        && called.Name == target.Name
                        && called.DeclaringType == target.DeclaringType)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

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

    /// <summary>One Active with CC §6.4's Consecrate condition — HP below 60 %.</summary>
    private static SkillSpec ActiveNode(string id, float cooldown) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Active,
        Array.Empty<IEffect>(),
        new ActiveSpec(
            cooldown,
            new TriggerSpec(new[]
            {
                new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 0.6f),
            }),
            new IEffect[]
            {
                new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.1f),
            }));

    private static SkillSpec PassiveNode(string id) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Passive,
        new IEffect[]
        {
            new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f),
        });

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
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

    /// <summary>An <see cref="ISaveStore"/> that accepts everything and keeps nothing.</summary>
    /// <remarks>
    /// <c>FrameOrderTests.InertSaveStore</c>'s twin: nothing here publishes a snapshot, so every
    /// method is a stand-in for a dependency the ticker's constructor guards rather than a behaviour
    /// under test.
    /// </remarks>
    private sealed class InertSaveStore : ISaveStore
    {
        public Task<PlayerProfile?> LoadProfile() => Task.FromResult<PlayerProfile?>(null);

        public Task SaveProfile(PlayerProfile profile) => Task.CompletedTask;

        public Task<RunSnapshot?> LoadRun() => Task.FromResult<RunSnapshot?>(null);

        public Task SaveRun(RunSnapshot run) => Task.CompletedTask;

        public Task ClearRun() => Task.CompletedTask;
    }
}
