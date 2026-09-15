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
/// CC §6.3's Skills screen: every active the player owns, its trigger in words, and the switch.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over a real <c>RunSession</c>, a real <c>DomainEventHub</c> and a real <c>SkillRunner</c>
/// behind them</b> — <c>LevelUpPresenterTests</c>' and <c>PausePresenterTests</c>' shape. The
/// commands this screen sends go through <see cref="RecordingCommands"/>, which is a tally
/// <em>in front of</em> the session rather than a stand-in for it: every row about slots would pass
/// against a fake that agreed with whatever the screen did, and the one thing CC §6.2's prompt is
/// about is what the runner will actually accept.
/// </para>
/// <para>
/// <b>Both screens under test are the shipped prefabs</b>, instantiated per row. Hand-building one
/// would test a second screen that happens to resemble the asset, and the failure Traps §5
/// describes — a component that deserialises as null with nothing reporting it — is invisible to a
/// fixture that never loads the asset.
/// </para>
/// <para>
/// <b>This screen holds no pause, and several rows exist to hold that line.</b> The gate is
/// <c>PausePresenter</c>'s (M3-09a rule 2) and <c>RunPause</c> throws on a second reason (M3-08a
/// rule 12), so the rows that could have asserted this presenter pausing assert instead that it
/// does not — and <see cref="Prompt_RaisesNoSecondPause"/> pins it at the constructor, where an
/// <c>if</c> cannot creep back in.
/// </para>
/// <para>
/// <b>The two screens are linked the way <c>Run.unity</c> links them</b>: a serialized
/// <c>_skillsScreen</c> on <c>PausePresenter</c>, set here by reflection because a cross-prefab
/// reference is scene dressing rather than prefab dressing. That is the wiring decision M3-09b
/// made, and <see cref="Open_FromThePausePanel"/> is what would fail if it were changed without
/// this file being changed with it.
/// </para>
/// <para>
/// <c>Awake</c> and <c>Start</c> do not run in EditMode (Traps §5), so the guards in
/// <c>SkillsPresenter.Start</c> never fire here and the prefab's own alpha 0, inactive prompt and
/// inactive row template are what leave the screen down at the top of each row.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SkillsPresenterTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/Skills.prefab";
    private const string PausePrefabPath = "Assets/_Project/Prefabs/UI/Pause.prefab";
    private const string LevelUpPrefabPath = "Assets/_Project/Prefabs/UI/LevelUp.prefab";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string TreeId = "tree.oathbound";
    private const string ArenaId = "arena.pillars";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    /// <summary>The row height the prefab ships with, and the field's own default.</summary>
    private const float RowDp = 56f;

    /// <summary>
    /// Rule 7's claim about a landscape phone: about five rows fit, and the sixth has to be
    /// scrolled to.
    /// </summary>
    private const int RowsThatFit = 5;

    /// <summary>The first active's authored cooldown; the <em>i</em>th is this plus <em>i</em>.</summary>
    private const float FirstCooldown = 2.5f;

    /// <summary>Consecrate's condition (CC §6.4): HP below 60 %.</summary>
    private const float HpThreshold = 0.6f;

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private DomainEventHub _hub;
    private RecordingEvents _spy;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private RunSession _session;
    private RecordingCommands _commands;

    private string[] _activeIds;

    private GameObject _screen;
    private SkillsPresenter _presenter;

    private GameObject _pauseScreen;
    private PausePresenter _pausePresenter;
    private RunPause _pause;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<RunPause> _pauses = new List<RunPause>();
    private readonly List<IDisposable> _disposables = new List<IDisposable>();

    private float _restoreTimeScale;
    private int _restoreTargetFrameRate;

    [SetUp]
    public void CreateWorld()
    {
        // The Editor's own globals. RunPause writes both and the teardown disposes every pause this
        // fixture builds, but a row that failed part way through would otherwise leave the whole
        // suite after it running at timeScale 0 — PausePresenterTests' argument, inherited.
        _restoreTimeScale = Time.timeScale;
        _restoreTargetFrameRate = Application.targetFrameRate;

        _hub = new DomainEventHub();
        _spy = new RecordingEvents();
        _random = new FixedRandom(7, Alternating(8_192));
        _clock = new FixedClock(Instant);
    }

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
    }

    // ---- The list (rules 4, 6, 7) ----------------------------------------------------------------

    [Test]
    public void List_ShowsOneRowPerOwnedActive()
    {
        StartRun(actives: 2);
        BuildScreen();

        _presenter.Open();

        SkillRow[] shown = Shown();

        Assert.That(shown.Length, Is.EqualTo(2), "One row per owned active, and no more.");

        for (int i = 0; i < shown.Length; i++)
        {
            // Its key, its seconds and its trigger key — CC §6.3's four minus the switch, which is
            // its own row. The name is a LocKey rather than English (ADR-0012, ledger row 9).
            Assert.That(Text(shown[i], "_name"), Is.EqualTo($"{_activeIds[i]}.name"));

            Assert.That(
                Text(shown[i], "_cooldown"),
                Is.EqualTo($"{(FirstCooldown + i).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} s"),
                "The row shows the wait in seconds after the floor, not a fraction (rule 3).");

            Assert.That(
                Text(shown[i], "_trigger"),
                Does.StartWith("trigger.hpFraction.below"),
                "The trigger line is a key rather than a sentence (rules 1, 2).");
        }

        Assert.That(
            _emptyLabelOf(_presenter).gameObject.activeSelf,
            Is.False,
            "The empty line is up over a run that owns two actives.");
    }

    [Test]
    public void List_ShowsNoPassives()
    {
        // The runner holds nothing but Actives (M3-06 rule 5), so CC §6.1's "passive skills have no
        // toggle and no button" is true by construction and needs no filter — which is exactly why
        // it needs a row: a filter someone adds later would pass every other row in this file.
        StartRun(actives: 1, takenPassives: 2);
        BuildScreen();

        Assert.That(
            _session.State.TakenNodeCount,
            Is.EqualTo(3),
            "The fixture's premise: three nodes were taken.");

        _presenter.Open();

        Assert.That(Shown().Length, Is.EqualTo(1), "A Passive was drawn a row of its own.");
    }

    [Test]
    public void List_EmptyRunShowsTheEmptyLine()
    {
        // **The state of every run in the build that exists**, because nothing in Data/Trees
        // authors an Active until M3-11 and M3-12. An empty panel and a broken panel look
        // identical, and this is the row that keeps the first from becoming the second.
        StartRun(actives: 0, takenPassives: 1);
        BuildScreen();

        _presenter.Open();

        Assert.That(Shown().Length, Is.Zero);
        Assert.That(_presenter.IsOpen, Is.True, "The screen refused to open over a run owning none.");

        Assert.That(
            _emptyLabelOf(_presenter).gameObject.activeSelf,
            Is.True,
            "A run owning no actives shows a blank panel rather than a line saying so.");

        Assert.That(
            _emptyLabelOf(_presenter).text,
            Does.StartWith("ui.skills."),
            "The empty line is English typed into a prefab rather than a LocKey (ADR-0012).");
    }

    [Test]
    public void List_DoesNotPollWhileOpen()
    {
        StartRun(actives: 2);
        BuildScreen();

        _presenter.Open();

        // **The claim, asked of the class rather than of a frame counter.** Rule 4 is that the list
        // is built on open and never polled, and the strongest way to check that is that there is
        // no per-frame method to poll from: a screen with an Update would redraw an unchanging list
        // sixty times a second on the one screen GD §11.4 wants cheap.
        Assert.That(
            typeof(SkillsPresenter)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => m.Name is "Update" or "LateUpdate" or "FixedUpdate"),
            Is.Empty,
            "SkillsPresenter declares a per-frame method. Rule 4 says the list is built on open.");

        Assert.That(
            typeof(SkillsPresenter)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => typeof(System.Collections.IEnumerator).IsAssignableFrom(m.ReturnType)),
            Is.Empty,
            "SkillsPresenter declares a coroutine. Nothing here may wait: the engine clock is "
                + "stopped while this screen is up.");

        // And the consequence, spelled out: a sentinel written over the row survives every frame
        // the screen is up, because nothing in this class runs on one.
        SkillRow row = Shown()[0];

        Label(row, "_name").text = "sentinel";

        for (int frame = 0; frame < 60; frame++)
        {
            PassAFrame();
        }

        Assert.That(
            Text(row, "_name"),
            Is.EqualTo("sentinel"),
            "Something repainted the row without being asked, so the list is being polled.");
    }

    [Test]
    public void Row_ShowsTheSlotAsAPosition()
    {
        // A in S1, C in S3, and the hole in S2 is legal and ordinary (M3-07a rule 3).
        StartRun(actives: 3, manual: new[] { 0, -1, 2, -1 });
        BuildScreen();

        _presenter.Open();

        SkillRow[] shown = Shown();

        // **"S1" and "S3", not "1" and "2"** (rule 11). The number is the slot's index plus one, so
        // a row reading "2nd manual skill" would be the compaction the whole slot design refuses.
        Assert.That(Text(shown[0], "_slot"), Is.EqualTo("S1"));
        Assert.That(Text(shown[1], "_slot"), Is.Empty, "An Auto skill was given a slot to show.");
        Assert.That(Text(shown[2], "_slot"), Is.EqualTo("S3"));

        Assert.That(Toggle(shown[0]).isOn, Is.False, "A Manual skill's switch reads Auto.");
        Assert.That(Toggle(shown[1]).isOn, Is.True, "An Auto skill's switch reads Manual.");
    }

    // ---- The switch (rules 5, 8) -----------------------------------------------------------------

    [Test]
    public void Switch_SendsSetAutoCast()
    {
        StartRun(actives: 2);
        BuildScreen();

        _presenter.Open();

        Flip(Shown()[0], auto: false);

        Assert.That(_commands.AutoCasts.Count, Is.EqualTo(1), "One tap, one command.");
        Assert.That(_commands.AutoCasts[0].Id, Is.EqualTo(new ContentId(_activeIds[0])));
        Assert.That(_commands.AutoCasts[0].Auto, Is.False);

        // The row reports a tap and asks nothing else: it does not move a slot, count one, or
        // decide anything (rule 8).
        Assert.That(_commands.OtherCalls, Is.Zero, "The screen sent a command about the body.");
    }

    [Test]
    public void Switch_RedrawsFromTheEvent()
    {
        StartRun(actives: 2);
        BuildScreen();

        _presenter.Open();

        // **The command is swallowed rather than forwarded**, which is the only way to observe
        // "after the event arrives, and not before": SetAutoCast runs synchronously and publishes
        // before it returns, so a forwarded command would make the two moments the same one.
        _commands.Forward = false;

        Flip(Shown()[0], auto: false);

        Assert.That(_commands.AutoCasts.Count, Is.EqualTo(1), "The fixture's premise: it was sent.");

        Assert.That(
            Text(Shown()[0], "_slot"),
            Is.Empty,
            "The row painted itself a slot for a command that core never heard — an optimistic "
                + "redraw would show Manual for a command that threw (rule 5).");

        // Now core really does it, and the event comes back carrying the id, the state and the slot
        // (M3-07a rule 9) — which is what a list redraws from without a second read.
        _session.SetAutoCast(new ContentId(_activeIds[0]), auto: false);

        Assert.That(Text(Shown()[0], "_slot"), Is.EqualTo("S1"));
        Assert.That(Toggle(Shown()[0]).isOn, Is.False);
    }

    [Test]
    public void Switch_ToAutoFreesTheSlotAndMovesNothing()
    {
        // A in S1, B in S2, C in S3 — M3-07a rule 3 from this side.
        StartRun(actives: 3, manual: new[] { 0, 1, 2, -1 });
        BuildScreen();

        _presenter.Open();

        Assert.That(Text(Shown()[1], "_slot"), Is.EqualTo("S2"), "The fixture's premise.");

        Flip(Shown()[1], auto: true);

        // **S1 and S3 stay where they were, and S2 is empty.** CC §6.2 draws four fixed thumb
        // positions, so compacting on a removal would slide S3's skill under the thumb that had
        // learned S2 — a silent re-bind of muscle memory as the reward for dropping a skill.
        Assert.That(Text(Shown()[0], "_slot"), Is.EqualTo("S1"));
        Assert.That(Text(Shown()[1], "_slot"), Is.Empty);
        Assert.That(Text(Shown()[2], "_slot"), Is.EqualTo("S3"));

        Assert.That(_session.State.ManualSlotCount, Is.EqualTo(2));
        Assert.That(_session.State.ManualSlotAt(1), Is.EqualTo(default(ContentId)));
    }

    // ---- The prompt (rules 9, 10) -----------------------------------------------------------------

    [Test]
    public void Switch_AtFourShowsThePromptAndSendsNothing()
    {
        StartRun(actives: 5, manual: new[] { 0, 1, 2, 3 });
        BuildScreen();

        _presenter.Open();

        Assert.That(_session.State.ManualSlotCount, Is.EqualTo(4), "The fixture's premise.");

        Flip(Shown()[4], auto: false);

        // **Zero commands.** The count is read before the command and the prompt is shown *instead
        // of* sending it, so core never sees a fifth request — its InvalidOperationException stays
        // unreachable in a live build and stays loud for the caller that forgets to ask.
        Assert.That(
            _commands.AutoCasts,
            Is.Empty,
            "The screen sent the fifth request rather than asking the question (rule 9).");

        Assert.That(_presenter.IsPromptUp, Is.True, "CC §6.2's question was never asked.");

        SkillRow[] occupants = PromptRows();

        Assert.That(occupants.Length, Is.EqualTo(4), "The prompt does not list the four occupants.");

        for (int slot = 0; slot < occupants.Length; slot++)
        {
            Assert.That(occupants[slot].SkillId, Is.EqualTo(_session.State.ManualSlotAt(slot)));
            Assert.That(Text(occupants[slot], "_slot"), Is.EqualTo($"S{slot + 1}"));
        }

        // Rule 10's mechanism: the list goes inert underneath rather than a second canvas going up.
        foreach (SkillRow row in Shown())
        {
            Assert.That(
                Toggle(row).interactable,
                Is.False,
                "The list is still live under the prompt, so a second switch could be flipped.");
        }
    }

    [Test]
    public void Prompt_ChoosingSendsTwoCommandsInOrder()
    {
        StartRun(actives: 5, manual: new[] { 0, 1, 2, 3 });
        BuildScreen();

        _presenter.Open();

        Flip(Shown()[4], auto: false);

        ContentId victim = _session.State.ManualSlotAt(1);
        ContentId requested = new ContentId(_activeIds[4]);

        Assert.That(_presenter.IsPromptUp, Is.True, "The fixture's premise.");

        Flip(PromptRows()[1], auto: true);

        // **Two ordinary commands, in that order, and no mechanism of its own.** Core cannot tell
        // this apart from two taps a minute apart, which is why SkillAutoCastChanged says nothing
        // about *why* a switch moved (M3-07a rule 9).
        Assert.That(_commands.AutoCasts.Count, Is.EqualTo(2));
        Assert.That(_commands.AutoCasts[0].Id, Is.EqualTo(victim));
        Assert.That(_commands.AutoCasts[0].Auto, Is.True, "The victim was not sent back to Auto first.");
        Assert.That(_commands.AutoCasts[1].Id, Is.EqualTo(requested));
        Assert.That(_commands.AutoCasts[1].Auto, Is.False);

        Assert.That(_presenter.IsPromptUp, Is.False, "The prompt stayed up over its own answer.");

        // And the requested skill landed in the freed slot rather than anywhere else, which is
        // SetAutoCast's "lowest free slot" seen from the screen.
        Assert.That(_session.State.ManualSlotAt(1), Is.EqualTo(requested));
        Assert.That(_session.State.ManualSlotCount, Is.EqualTo(4));
        Assert.That(_session.State.IsAutoCast(victim), Is.True);

        Assert.That(Text(Shown()[4], "_slot"), Is.EqualTo("S2"));
        Assert.That(Text(Shown()[1], "_slot"), Is.Empty, "The victim's row still names a slot.");
    }

    [Test]
    public void Prompt_CancelSendsNothingAndRestoresTheSwitch()
    {
        StartRun(actives: 5, manual: new[] { 0, 1, 2, 3 });
        BuildScreen();

        _presenter.Open();

        Flip(Shown()[4], auto: false);

        Assert.That(_presenter.IsPromptUp, Is.True, "The fixture's premise.");
        Assert.That(Toggle(Shown()[4]).isOn, Is.False, "The fixture's premise: the thumb moved it.");

        TapCancel();

        Assert.That(_commands.AutoCasts, Is.Empty, "Cancelling sent a command.");
        Assert.That(_presenter.IsPromptUp, Is.False);

        // **The switch goes back where it was**, and it goes back from core rather than from a
        // remembered value: the fifth skill is still Auto because nothing was ever sent.
        Assert.That(_session.State.IsAutoCast(new ContentId(_activeIds[4])), Is.True);
        Assert.That(Toggle(Shown()[4]).isOn, Is.True, "The switch stayed where the thumb left it.");
        Assert.That(Text(Shown()[4], "_slot"), Is.Empty);

        // And the list is live again — the prompt took its interactability rather than a canvas.
        Assert.That(Toggle(Shown()[0]).interactable, Is.True);
    }

    [Test]
    public void Prompt_RaisesNoSecondPause()
    {
        StartRun(actives: 5, manual: new[] { 0, 1, 2, 3 });
        BuildScreen();
        BuildPauseScreen();

        TapIcon();

        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Menu), "The fixture's premise.");

        _pausePresenter.OpenSkills();

        Assert.That(() => Flip(Shown()[4], auto: false), Throws.Nothing, "The prompt tripped RunPause.");

        Assert.That(_presenter.IsPromptUp, Is.True);

        // **One gate, not three.** A prompt raised over a screen raised over a paused game holds
        // the pause once, by the screen the player opened first — and RunPause.Pause throws on a
        // second reason (M3-08a rule 12).
        Assert.That(_pause.IsPaused, Is.True);
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Menu));

        // And the constructor is where that is pinned, because an `if` can creep back into a
        // method and cannot creep into a parameter list. LevelUpPresenterTests' precedent.
        MethodInfo construct = typeof(SkillsPresenter).GetMethod(nameof(SkillsPresenter.Construct));

        Assert.That(
            construct.GetParameters().Select(p => p.ParameterType),
            Has.No.Member(typeof(RunPause)),
            "SkillsPresenter took a RunPause. The pause is held once, by the pause menu (rule 10).");
    }

    // ---- The panel underneath (M3-09a rule 6, rule 10) --------------------------------------------

    [Test]
    public void Open_FromThePausePanel()
    {
        StartRun(actives: 2);
        BuildScreen();
        BuildPauseScreen();

        TapIcon();

        Assert.That(_pausePresenter.IsOpen, Is.True, "The fixture's premise: the panel is up.");
        Assert.That(_presenter.IsOpen, Is.False);

        TapSkills();

        Assert.That(_presenter.IsOpen, Is.True, "The Skills button did not raise the screen.");
        Assert.That(Shown().Length, Is.EqualTo(2), "It went up without drawing the list.");

        // **The panel's other buttons go inert.** The Skills canvas sorts above the pause one and
        // covers it anyway; this is the brace that holds when a later screen forgets its scrim —
        // M3-09a's own argument for the icon's second guard.
        Assert.That(PauseButton("_resume").interactable, Is.False);
        Assert.That(PauseButton("_quit").interactable, Is.False);
        Assert.That(PauseButton("_skills").interactable, Is.False, "Skills can be tapped twice.");

        Assert.That(_pausePresenter.IsOpen, Is.True, "The panel closed itself under the screen.");
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Menu));
    }

    [Test]
    public void Close_ReturnsToThePausePanel()
    {
        StartRun(actives: 2);
        BuildScreen();
        BuildPauseScreen();

        TapIcon();
        TapSkills();

        Assert.That(_presenter.IsOpen, Is.True, "The fixture's premise.");

        TapClose();

        Assert.That(_presenter.IsOpen, Is.False, "Close left the screen up.");

        // A poll rather than a callback (M3-09a's shape, and its As built's argument for it): the
        // panel reads the screen once a frame, which is what gives it back even when something this
        // file did not ask for closed the screen.
        PassAPauseFrame();

        Assert.That(PauseButton("_resume").interactable, Is.True, "The panel never came back.");
        Assert.That(PauseButton("_quit").interactable, Is.True);
        Assert.That(PauseButton("_skills").interactable, Is.True);

        // **And it lowered nothing**, which is the whole of rule 10: this screen never held a pause,
        // so closing it is a canvas going down rather than a gate being released.
        Assert.That(_pause.IsPaused, Is.True, "Closing the Skills screen resumed the run.");
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Menu));
        Assert.That(_pausePresenter.IsOpen, Is.True);
    }

    // ---- Layout (rule 7) ---------------------------------------------------------------------------

    [Test]
    public void Screen_ScrollsPastFive()
    {
        // CH §4 puts ~25 % of 27 nodes at Active, so a full tree is about seven and the runner's
        // ceiling is twelve. Eight is past what a landscape phone's safe area holds, and a list that
        // silently clipped the sixth would hide a skill the player owns.
        StartRun(actives: 8);
        BuildScreen();

        float rowPx = RowDp * PixelsPerDp();

        ScrollRect scroll = ScrollOf(_presenter);
        RectTransform viewport = scroll.viewport;

        // Pinned to five rows rather than measured, because a Canvas in EditMode has never been
        // driven by its own CanvasScaler and the viewport's stretched rect would be whatever the
        // last layout pass left. Five is rule 7's own number.
        viewport.anchorMin = new Vector2(0.5f, 0.5f);
        viewport.anchorMax = new Vector2(0.5f, 0.5f);
        viewport.pivot = new Vector2(0.5f, 0.5f);
        viewport.sizeDelta = new Vector2(800f, RowsThatFit * rowPx);

        _presenter.Open();

        Assert.That(Shown().Length, Is.EqualTo(8), "Eight owned actives, eight rows.");

        Assert.That(
            scroll.content.rect.height,
            Is.EqualTo(8f * rowPx).Within(0.01f),
            "The content rect is not the whole list's height, so the rows past it are unreachable.");

        Assert.That(
            scroll.content.rect.height,
            Is.GreaterThan(viewport.rect.height),
            "Eight rows fit inside five rows' worth of viewport, which means nothing is scrolling.");

        Assert.That(scroll.vertical, Is.True, "The list does not scroll vertically.");
    }

    [Test]
    public void Layout_IgnoresANonFiniteDpField()
    {
        StartRun(actives: 3);
        BuildScreen();

        _presenter.Open();

        ScrollRect scroll = ScrollOf(_presenter);

        Vector2 contentBefore = scroll.content.sizeDelta;
        Vector2 rowBefore = ((RectTransform)Shown()[0].transform).sizeDelta;

        Assert.That(contentBefore.y, Is.GreaterThan(0f), "The fixture's premise: a laid-out list.");

        // **The float door on this screen.** The dp field is serialized so the owner can tune the
        // row height on a device (rule 7, ledger row 4), which makes it an Inspector door rather
        // than a constant — and a NaN reaching sizeDelta is a RectTransform that never renders
        // again, which here is a list of skills that exists and cannot be seen.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -12f })
        {
            SetPrivate(_presenter, "_rowHeightDp", bad);

            Assert.That(() => _presenter.Open(), Throws.Nothing, $"Open threw on a row height of {bad}.");

            Assert.That(
                float.IsFinite(scroll.content.sizeDelta.y),
                Is.True,
                $"A row height of {bad} wrote a non-finite content height.");

            Assert.That(
                scroll.content.sizeDelta,
                Is.EqualTo(contentBefore),
                $"A row height of {bad} was written through rather than leaving the layout alone.");

            Assert.That(
                ((RectTransform)Shown()[0].transform).sizeDelta,
                Is.EqualTo(rowBefore),
                $"A row height of {bad} resized a row.");
        }
    }

    // ---- What this screen does not do (rules 1, 2, 12) --------------------------------------------

    [Test]
    public void Screen_WritesNoSave()
    {
        var store = new CountingStore(new InMemorySaveStore());

        // Built before the run starts, because the opening snapshot is published from inside
        // RunSession.Start — SaveWriter's own reason for subscribing in its constructor.
        Track(new SaveWriter(store, _hub));

        StartRun(actives: 3);
        BuildScreen();

        int opening = store.RunWriteCount;

        Assert.That(opening, Is.GreaterThanOrEqualTo(1), "The fixture's premise: a run was written.");

        _presenter.Open();

        Flip(Shown()[0], auto: false);
        Flip(Shown()[1], auto: false);

        _presenter.Close();

        // **The toggles are persisted by M3-07b at the next boundary, from the runner's own state**
        // (rule 12). A screen that saved on close would be a second writer for a field whose only
        // author is the recorder — and the switches really did move, which is what stops this row
        // being green against a screen that did nothing at all.
        Assert.That(store.RunWriteCount, Is.EqualTo(opening), "The Skills screen wrote a save.");
        Assert.That(store.ClearCount, Is.Zero);

        Assert.That(_session.State.ManualSlotCount, Is.EqualTo(2), "Neither switch actually moved.");
    }

    [Test]
    public void Card_DrawsTheKeyNotEnglish()
    {
        StartRun(actives: 1, activeIds: new[] { "skill.oathbound.consecrate" });
        BuildScreen();

        _presenter.Open();

        SkillRow row = Shown()[0];

        // **Ledger row 9's second and sharper reader**, visible here rather than a surprise at
        // M3-15: a trigger line is a LocKey *and* a formatted number, so the composition has to
        // survive M6-10 rather than just the key. Today the row reads the key and the number beside
        // it; at M6-10 the key resolves to "Player HP below {0}" and the number slots into it.
        Assert.That(Text(row, "_name"), Does.StartWith("skill.oathbound.consecrate"));

        Assert.That(
            Text(row, "_trigger"),
            Is.EqualTo("trigger.hpFraction.below 60 %"),
            "The row is not drawing CC §6.4's condition as a key and a formatted threshold.");

        // Core said what kind of number it is; the screen decided what it looks like (rule 2).
        Assert.That(TriggerText.UnitOf(TriggerField.HpFraction), Is.EqualTo(TriggerUnit.Fraction));
    }

    // ---- Lifetime -----------------------------------------------------------------------------------

    [Test]
    public void Subscribes_FromConstruct()
    {
        // The run starts *first*, and the screen is composed after it — which is the order
        // RunScope's Awake and this component's OnEnable can happen in, since Unity orders no two
        // Awake calls. An OnEnable subscription would have been made against a hub that did not
        // exist yet.
        StartRun(actives: 2);

        Assert.That(_spy.Count<RunStarted>(), Is.EqualTo(1), "The fixture's premise.");

        BuildScreen();

        _presenter.Open();

        Assert.That(Text(Shown()[0], "_slot"), Is.Empty, "The fixture's premise: nothing is Manual.");

        // Moved by core rather than by the screen, so a presenter that heard nothing would still
        // be showing the draw it made on open.
        _session.SetAutoCast(new ContentId(_activeIds[0]), auto: false);

        Assert.That(
            Text(Shown()[0], "_slot"),
            Is.EqualTo("S1"),
            "A presenter constructed after RunStarted heard nothing, so the subscription is not "
                + "being made in Construct (rule 5).");
    }

    [Test]
    public void Destroy_DropsSubscriptions()
    {
        StartRun(actives: 2);
        BuildScreen();

        Assert.That(
            _hub.SubscriberCount<SkillAutoCastChanged>(),
            Is.EqualTo(1),
            "The fixture's premise.");

        // **Invoked rather than waited for**, for LevelUpPresenterTests' reason: Unity does not
        // call OnDestroy on an object whose Awake never ran, and Awake does not run in EditMode
        // (Traps §5) — so DestroyImmediate alone would leave the subscription in place and this row
        // would pass against a presenter with no OnDestroy at all.
        Destroy();

        Object.DestroyImmediate(_screen);

        Assert.That(
            () => _hub.Publish(new SkillAutoCastChanged(new ContentId(_activeIds[0]), true, -1)),
            Throws.Nothing);

        Assert.That(_hub.SubscriberCount<SkillAutoCastChanged>(), Is.Zero);
    }

    // ---- Guard rows ----------------------------------------------------------------------------------

    [Test]
    public void Construct_RefusesNullDependencies()
    {
        StartRun(actives: 1);
        BuildScreen();

        Assert.That(() => _presenter.Construct(null, _commands, _hub, _catalog), Throws.ArgumentNullException);
        Assert.That(() => _presenter.Construct(_session, null, _hub, _catalog), Throws.ArgumentNullException);
        Assert.That(() => _presenter.Construct(_session, _commands, null, _catalog), Throws.ArgumentNullException);
        Assert.That(() => _presenter.Construct(_session, _commands, _hub, null), Throws.ArgumentNullException);
    }

    [Test]
    public void Row_RefusesANullSpecOrCallbackAndAPassive()
    {
        StartRun(actives: 1, takenPassives: 1);
        BuildScreen();

        SkillRow row = RowTemplateOf(_presenter);

        Action<ContentId, bool> handler = (_, _) => { };

        // A row with no skill to draw is a presenter that read past the end of the runner; a row
        // with nobody to report to is a switch the player can flip while nothing happens, which is
        // the failure here that looks like a frozen screen. OfferCard.Show's pair, one screen on.
        Assert.That(() => row.Show(null, 1f, true, -1, handler), Throws.ArgumentNullException);

        Assert.That(
            () => row.Show(_catalog.Skill(new ContentId(_activeIds[0])), 1f, true, -1, null),
            Throws.ArgumentNullException);

        // And a Passive, which CC §6.1 says has no toggle and no button. It cannot arrive from the
        // runner (M3-06 rule 5), so a caller that got here with one has read past the runner rather
        // than out of it — loud rather than a row with a switch that means nothing.
        Assert.That(
            () => row.Show(_catalog.Skill(new ContentId(PassiveId(0))), 1f, true, -1, handler),
            Throws.ArgumentException);
    }

    // ---- The asset (Traps §5) ------------------------------------------------------------------------

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        // Traps §5: a MonoBehaviour declared with a file-scoped namespace compiles and is never
        // linked to a MonoScript, so the component would come back null here with nothing reporting
        // an error anywhere. GetComponent answering is half of what this row proves.
        var presenter = prefab.GetComponent<SkillsPresenter>();

        Assert.That(
            presenter,
            Is.Not.Null,
            "SkillsPresenter did not load off its own prefab — the usual cause is a file-scoped "
                + "namespace on a UnityEngine.Object type (Traps §5).");

        // **Read back off the saved asset**, not off an object this fixture just dressed. A prefab
        // whose references never bound looks identical in memory to one that did, right up until it
        // is loaded in a build.
        var so = new SerializedObject(presenter);

        foreach (string field in new[]
                 {
                     "_root", "_scroll", "_rowTemplate", "_emptyLabel", "_close", "_prompt",
                     "_promptCancel",
                 })
        {
            Assert.That(
                so.FindProperty(field).objectReferenceValue,
                Is.Not.Null,
                $"{field} is not dressed on Skills.prefab.");
        }

        SerializedProperty promptRows = so.FindProperty("_promptRows");

        Assert.That(
            promptRows.arraySize,
            Is.EqualTo(SkillRunner.MaxManualSlots),
            "The prompt does not hold one row per thumb position.");

        for (int i = 0; i < promptRows.arraySize; i++)
        {
            Assert.That(
                promptRows.GetArrayElementAtIndex(i).objectReferenceValue,
                Is.Not.Null,
                $"Prompt row {i} is not dressed.");
        }

        // The row template is a template: it never draws itself, so it ships inactive.
        var template = (SkillRow)so.FindProperty("_rowTemplate").objectReferenceValue;

        Assert.That(
            template.gameObject.activeSelf,
            Is.False,
            "The row template ships active, so an undrawn row is on screen before the first open.");

        Assert.That(
            prefab.GetComponentsInChildren<SkillRow>(true).Length,
            Is.EqualTo(SkillRunner.MaxManualSlots + 1),
            "The prefab holds a SkillRow that is neither the template nor one of the prompt's four.");

        // Keys, not English — ADR-0012 and ledger row 9. OfferCard set the precedent at M3-08b and
        // the pause panel followed at M3-09a; this screen is deliberately not the place that breaks
        // the run.
        foreach (TMP_Text label in new[]
                 {
                     (TMP_Text)so.FindProperty("_emptyLabel").objectReferenceValue,
                     ((Button)so.FindProperty("_close").objectReferenceValue)
                         .GetComponentInChildren<TMP_Text>(true),
                     ((Button)so.FindProperty("_promptCancel").objectReferenceValue)
                         .GetComponentInChildren<TMP_Text>(true),
                 })
        {
            Assert.That(
                label.text,
                Does.StartWith("ui.skills."),
                $"{label.name}'s text is not a LocKey, so this screen is a third place raw English "
                    + "is typed into a prefab (ledger row 9).");

            Assert.That(label.text, Does.Not.Contain(" "), $"{label.name}'s text is a sentence.");
        }

        // Its own canvas, above the pause panel's and below the level-up's: above the panel because
        // it is raised over it, below the level-up because a level-up cannot happen while the tick
        // is gated and the ordering should stay true if one ever could.
        var canvas = prefab.GetComponent<Canvas>();

        Assert.That(canvas, Is.Not.Null, "The screen is not its own canvas.");

        var pause = AssetDatabase.LoadAssetAtPath<GameObject>(PausePrefabPath);
        var levelUp = AssetDatabase.LoadAssetAtPath<GameObject>(LevelUpPrefabPath);

        Assert.That(
            canvas.sortingOrder,
            Is.GreaterThan(pause.GetComponentInChildren<Canvas>(true).sortingOrder),
            "The Skills screen sorts at or below the pause panel it is raised over.");

        Assert.That(
            canvas.sortingOrder,
            Is.LessThan(levelUp.GetComponentInChildren<Canvas>(true).sortingOrder),
            "The Skills screen sorts at or above the level-up screen.");

        Assert.That(
            prefab.GetComponentInChildren<SafeAreaFitter>(true),
            Is.Not.Null,
            "No SafeAreaFitter, so a row can land under a cutout.");
    }

    // ---- Fixture -------------------------------------------------------------------------------------

    /// <summary>
    /// Starts a resumed run owning <paramref name="actives"/> actives and
    /// <paramref name="takenPassives"/> passives.
    /// </summary>
    /// <remarks>
    /// Resumed rather than fresh for <c>PausePresenterTests</c>' reason, and because it is the only
    /// door <c>RunSession.Start</c> adds a restored Active through short of playing a level-up. The
    /// level accounts for the nodes (M3-08a rule 9): every pick a run has earned is spent on a node,
    /// spent on Overflow, or unspent, so one level per node and nothing owed is the shape that says
    /// "these nodes were paid for".
    /// </remarks>
    /// <param name="manual">
    /// Which owned active sits in each of the four slots, by its index in
    /// <paramref name="actives"/> — and −1 for an empty one, because a hole is legal and ordinary
    /// (M3-07a rule 3).
    /// </param>
    private void StartRun(
        int actives,
        int takenPassives = 0,
        int[] manual = null,
        string[] activeIds = null)
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

        // Branch a holds the actives — or, for a run owning none, one passive, because a branch
        // needs at least one node and SkillTreeSpec wants exactly three branches.
        if (activeTier.Count == 0)
        {
            SkillSpec filler = PassiveNode(PassiveId(90));

            skills.Add(filler);
            activeTier.Add(filler.Id);
        }

        var passiveTier = new List<ContentId>();

        for (int i = 0; i < Math.Max(takenPassives, 1); i++)
        {
            SkillSpec node = PassiveNode(PassiveId(i));

            skills.Add(node);
            passiveTier.Add(node.Id);

            if (i < takenPassives)
            {
                taken.Add(node.Id);
            }
        }

        SkillSpec spare = PassiveNode(PassiveId(99));

        skills.Add(spare);

        var tree = new SkillTreeSpec(
            new ContentId(TreeId),
            new ContentId(OathboundId),
            new[]
            {
                new SkillBranchSpec(new LocKey("branch.a"), new IReadOnlyList<ContentId>[] { activeTier }),
                new SkillBranchSpec(new LocKey("branch.b"), new IReadOnlyList<ContentId>[] { passiveTier }),
                new SkillBranchSpec(
                    new LocKey("branch.c"),
                    new IReadOnlyList<ContentId>[] { new[] { spare.Id } }),
            });

        _catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Husk() },
            new[] { Mode() },
            skills,
            new[] { tree });

        // The hub is the session's own IDomainEvents, so the screen hears what core published rather
        // than what the fixture decided to say. The spy rides alongside for counting.
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
            1 + taken.Count,
            0f,
            0,
            taken.ToArray(),
            slots);

        _session.Start(new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            _random.Seed,
            1,
            SpawnPlan.Empty,
            snapshot));

        _commands = new RecordingCommands(_session);
    }

    /// <summary>
    /// Instantiates the shipped prefab and injects it — the screen under test is the asset.
    /// </summary>
    private void BuildScreen()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PrefabPath}.");

        _screen = Object.Instantiate(prefab);
        _spawned.Add(_screen);

        _presenter = _screen.GetComponent<SkillsPresenter>();

        Assert.That(
            _presenter,
            Is.Not.Null,
            "SkillsPresenter did not load off its own prefab (Traps §5).");

        // Pinned, because a Canvas instantiated in EditMode has never been driven by its own
        // CanvasScaler and the layout rows measure in dp against this — PausePresenterTests sets it
        // for the same reason with a different number.
        _screen.GetComponent<Canvas>().scaleFactor = 1f;

        // What RunScope's RegisterComponent does. Start never runs in EditMode, so nothing else on
        // this object has fired.
        _presenter.Construct(_session, _commands, _hub, _catalog);
    }

    /// <summary>
    /// The pause screen underneath, linked the way <c>Run.unity</c> links the two.
    /// </summary>
    private void BuildPauseScreen()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PausePrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PausePrefabPath}.");

        _pauseScreen = Object.Instantiate(prefab);
        _spawned.Add(_pauseScreen);

        _pausePresenter = _pauseScreen.GetComponent<PausePresenter>();

        _pause = new RunPause();
        _pauses.Add(_pause);

        _pausePresenter.Construct(_session, _pause, _hub, new SceneLoader());

        // The cross-prefab reference, which is scene dressing rather than prefab dressing: the two
        // screens are separate root prefabs, so nothing on either asset can carry it.
        SetPrivate(_pausePresenter, "_skillsScreen", _presenter);
    }

    private void TapIcon() => PauseButton("_icon").onClick.Invoke();

    private void TapSkills() => PauseButton("_skills").onClick.Invoke();

    private void TapClose() => Field<Button>(_presenter, "_close").onClick.Invoke();

    private void TapCancel() => Field<Button>(_presenter, "_promptCancel").onClick.Invoke();

    /// <summary>
    /// Moves a row's switch, through the control the player's thumb would hit.
    /// </summary>
    /// <remarks>
    /// <c>Toggle.isOn</c> rather than a synthetic pointer event, and it fires
    /// <c>onValueChanged</c> whether or not the toggle is interactable — which is the same thing
    /// that happens when two taps are dispatched from one <c>EventSystem</c> pass. A helper that
    /// checked <c>interactable</c> first would make the prompt rows' own guard untestable.
    /// </remarks>
    private static void Flip(SkillRow row, bool auto) => Toggle(row).isOn = auto;

    /// <summary>One frame's worth of this screen's per-frame work, which is none (rule 4).</summary>
    private static void PassAFrame()
    {
    }

    /// <summary>One frame of the pause screen's <c>Update</c> — what re-reads this screen.</summary>
    private void PassAPauseFrame() => Invoke(_pausePresenter, "Update");

    /// <summary>The presenter's own <c>OnDestroy</c>. See <see cref="Destroy_DropsSubscriptions"/>.</summary>
    private void Destroy() => Invoke(_presenter, "OnDestroy");

    private static void Invoke(object target, string method) =>
        target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);

    private Button PauseButton(string field) => Field<Button>(_pausePresenter, field);

    /// <summary>Every list row currently drawn, in the order the screen drew them.</summary>
    private SkillRow[] Shown() =>
        Field<SkillRow[]>(_presenter, "_rows")?.Where(r => r != null && r.IsShown).ToArray()
        ?? Array.Empty<SkillRow>();

    private SkillRow[] PromptRows() =>
        Field<SkillRow[]>(_presenter, "_promptRows").Where(r => r != null && r.IsShown).ToArray();

    private static SkillRow RowTemplateOf(SkillsPresenter presenter) =>
        Field<SkillRow>(presenter, "_rowTemplate");

    private static ScrollRect ScrollOf(SkillsPresenter presenter) =>
        Field<ScrollRect>(presenter, "_scroll");

    private static TMP_Text _emptyLabelOf(SkillsPresenter presenter) =>
        Field<TMP_Text>(presenter, "_emptyLabel");

    private static TMP_Text Label(SkillRow row, string field) => Field<TMP_Text>(row, field);

    private static string Text(SkillRow row, string field) => Label(row, field).text;

    private static Toggle Toggle(SkillRow row) => Field<Toggle>(row, "_autoManual");

    /// <summary>
    /// How many canvas units one dp is worth here — <c>SkillsPresenter.PixelsPerDp</c>, with the
    /// scale this fixture pinned.
    /// </summary>
    private static float PixelsPerDp() => StickShaper.PixelsPerDp(Screen.dpi);

    private T Track<T>(T disposable)
        where T : IDisposable
    {
        _disposables.Add(disposable);

        return disposable;
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);

    private static void SetPrivate(object target, string name, object value) =>
        target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);

    private static string PassiveId(int i) => $"skill.test.p{i}";

    /// <summary>
    /// One Active with CC §6.4's Consecrate condition — HP below 60 %, which is what
    /// <see cref="Card_DrawsTheKeyNotEnglish"/> reads back as <em>"60 %"</em>.
    /// </summary>
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
                new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, HpThreshold),
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

    /// <summary>
    /// The run's own <c>IPlayerCommands</c> with a tally in front of it.
    /// </summary>
    /// <remarks>
    /// <b>The real session rather than a stand-in</b>, <c>PausePresenterTests.CountingStore</c>'s
    /// shape and its reason: every row about slots would pass against a fake that agreed with
    /// whatever the screen did, and the one thing CC §6.2's prompt is about is what the runner will
    /// actually accept. <see cref="Forward"/> is what lets one row observe the gap between a
    /// command being sent and its event arriving, which is otherwise the same instant.
    /// </remarks>
    private sealed class RecordingCommands : IPlayerCommands
    {
        private readonly IPlayerCommands _inner;

        public RecordingCommands(IPlayerCommands inner)
        {
            _inner = inner;
        }

        /// <summary>Whether a recorded command is also passed on. True except where a row says so.</summary>
        public bool Forward { get; set; } = true;

        public List<(ContentId Id, bool Auto)> AutoCasts { get; } = new List<(ContentId, bool)>();

        /// <summary>Anything this screen has no business sending. Expected to stay zero.</summary>
        public int OtherCalls { get; private set; }

        public void FocusTarget(Vector3 worldPoint)
        {
            OtherCalls++;
            _inner.FocusTarget(worldPoint);
        }

        public void ClearFocus()
        {
            OtherCalls++;
            _inner.ClearFocus();
        }

        public void MovementSkill()
        {
            OtherCalls++;
            _inner.MovementSkill();
        }

        public void CastSkill(int slot)
        {
            OtherCalls++;
            _inner.CastSkill(slot);
        }

        public void SetAutoCast(ContentId skillId, bool auto)
        {
            AutoCasts.Add((skillId, auto));

            if (Forward)
            {
                _inner.SetAutoCast(skillId, auto);
            }
        }
    }

    /// <summary>
    /// A real <c>InMemorySaveStore</c> with a tally in front of it — rule 12's row.
    /// </summary>
    private sealed class CountingStore : ISaveStore
    {
        private readonly ISaveStore _inner;

        public CountingStore(ISaveStore inner)
        {
            _inner = inner;
        }

        public int RunWriteCount { get; private set; }

        public int ClearCount { get; private set; }

        public Task<PlayerProfile?> LoadProfile() => _inner.LoadProfile();

        public Task SaveProfile(PlayerProfile profile) => _inner.SaveProfile(profile);

        public Task<RunSnapshot?> LoadRun() => _inner.LoadRun();

        public Task SaveRun(RunSnapshot run)
        {
            RunWriteCount++;

            return _inner.SaveRun(run);
        }

        public Task ClearRun()
        {
            ClearCount++;

            return _inner.ClearRun();
        }
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
