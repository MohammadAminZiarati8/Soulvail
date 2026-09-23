using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
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
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Aliased rather than imported: core speaks System.Numerics (ADR-0001) and this file measures rects,
// so both Vector3s are in scope and neither may be the ambiguous one.
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// GD §16.2's segmented boss band: one segment per phase, one fill across all of them, and a beat
/// the player can see. M4-04.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over the shipped <c>Hud.prefab</c> and a real <c>RunSession</c></b> — <c>XpBarViewTests</c>'
/// shape and its reasons. The hub <em>is</em> the session's <c>IDomainEvents</c>, so the presenter
/// hears what core would publish rather than what a hand-built screen was handed, and the failure
/// Traps §5 describes — a component that deserialises as null with nothing reporting it — is
/// invisible to a fixture that never loads the asset.
/// </para>
/// <para>
/// <b>The boss events are published rather than earned, and one row is the exception that makes the
/// rest honest.</b> Reaching stage 5 with a live Warden costs four stages of kills, which would make
/// every row here a run test; what the published rows assert is the band's own half — that it draws
/// what the event said and nothing else. <see cref="Bar_LearnsItsMarksFromARealFight"/> closes the
/// other half by driving the band from a real <see cref="BossPhases"/> built over a real
/// <see cref="BossSpec"/>, so the thresholds come out of an authored phase list rather than out of an
/// array this file typed. The last link — that <c>BossBehaviour</c> publishes those thresholds — is
/// <c>BossPhasesTests.Phase_IsAnnouncedWhenTheBossStandsUp</c>'s, asserted over a boss that actually
/// stood up.
/// </para>
/// <para>
/// <b>Every threshold driven here is uneven on purpose.</b> The shipped Warden authors 1.0 / 0.66 /
/// 0.33, and even spacing on three segments would put the marks at 0.667 and 0.333 — under a
/// hundredth away, which is to say <b>the only boss that exists could not have told rule 4 from the
/// thing rule 4 forbids</b>. So <see cref="Bar_MarksSitAtTheAuthoredThresholds"/> drives the spec's
/// own 1.0 / 0.8 / 0.25, where thirds and the truth are nowhere near each other.
/// </para>
/// <para>
/// <c>Awake</c> and <c>Start</c> do not run on an instantiated prefab in EditMode (Traps §5), so each
/// component is constructed by hand and its <c>Start</c> invoked where a row needs the placement and
/// the first draw that live there.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BossBarViewTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string ArenaId = "arena.pillars";

    private const string WardenBossId = "boss.warden";
    private const string WardenEnemyId = "enemy.warden";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    /// <summary>The boss the presenter follows in every row. Any positive id would do.</summary>
    private const int BossEnemyId = 7;

    /// <summary>A Husk standing beside it, so every filter has something to refuse.</summary>
    private const int OtherEnemyId = 9;

    /// <summary>The Warden's own phases, as <c>Warden.asset</c> authors them.</summary>
    private static readonly float[] WardenPhases = { 1f, 0.66f, 0.33f };

    /// <summary>
    /// The spec's own uneven row — 1.0 / 0.8 / 0.25. Nothing like thirds, which is the point.
    /// </summary>
    private static readonly float[] UnevenPhases = { 1f, 0.8f, 0.25f };

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private DomainEventHub _hub;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private RunSession _session;
    private RunConfig _config;

    private GameObject _hud;
    private HudPresenter _presenter;
    private BossBarView _bar;
    private XpBarView _strip;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<IDisposable> _disposables = new List<IDisposable>();

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
        for (int i = _disposables.Count - 1; i >= 0; i--)
        {
            _disposables[i].Dispose();
        }

        _disposables.Clear();

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
        _bar = null;
        _strip = null;
    }

    // ---- Rules 1 and 2: when the band is there at all ---------------------------------------------

    [Test]
    public void Bar_IsHiddenUntilABossExists()
    {
        BuildHud();

        // Rule 2: nothing puts the band on screen but a boss, and a run's ordinary life — a stage
        // arriving, a Husk dying, the player being hit — says nothing about one.
        _hub.Publish(new StageArrived(1, new ContentId(ArenaId)));
        _hub.Publish(new EnemySpawned(OtherEnemyId, new ContentId(HuskId), Vector3.Zero));
        _hub.Publish(new EnemyDamaged(OtherEnemyId, 4f, 0.5f, false));
        _hub.Publish(new EnemyDied(OtherEnemyId, new ContentId(HuskId), Vector3.Zero));

        Assert.That(_bar.IsShown, Is.False, "the band drew itself over a run with no boss in it.");
        Assert.That(_bar.SegmentCount, Is.Zero);
        Assert.That(Root().alpha, Is.EqualTo(0f), "a hidden band is still drawing.");

        // **And it takes no touches, in any state.** The stick and the Charge button are under this
        // canvas and the fight runs for the whole time a boss is alive, so a full-width band in the
        // raycast path would eat a third of the screen's input — OverflowToast's rule, on a wider
        // element.
        Assert.That(Root().blocksRaycasts, Is.False);
        Assert.That(Root().interactable, Is.False);

        foreach (Graphic graphic in BandGraphics())
        {
            Assert.That(
                graphic.raycastTarget,
                Is.False,
                $"{graphic.name} in the band is a raycast target, so it swallows taps meant for the "
                    + "arena underneath it.");
        }
    }

    [Test]
    public void Bar_DrawsNothingBeforeAnyEvent()
    {
        BuildHud();

        // **The whole of rule 1's refusal, and it is a statement about M4-00a rather than about this
        // view.** EnemySpawned gained no IsBoss flag on purpose — M3-13b's IsElite is a *look* and a
        // boss is not one — so a body standing up tells the HUD nothing at all. A band that guessed
        // three segments here would be right about the only boss that ships and wrong about the first
        // one that is not the Warden.
        _hub.Publish(new EnemySpawned(BossEnemyId, new ContentId(WardenEnemyId), Vector3.Zero));

        Assert.That(
            _bar.IsShown,
            Is.False,
            "the band appeared on a spawn, so it is guessing rather than being told.");
        Assert.That(_bar.SegmentCount, Is.Zero, "the band guessed a segment count.");
        Assert.That(_bar.MarkCount, Is.Zero);

        // Damage arriving before any phase event still moves nothing: there is no fight to draw.
        _hub.Publish(new EnemyDamaged(BossEnemyId, 100f, 0.9f, false));

        Assert.That(_bar.IsShown, Is.False);
    }

    [Test]
    public void Bar_TakesItsSegmentCountFromTheEvent()
    {
        BuildHud();

        // Rule 1: M4-01b put OfPhases on the event precisely so this view could learn it, so a
        // four-phase boss needs no edit here — and four is a number no shipped asset has, which is
        // what makes the row fail against a constant three.
        Announce(BossEnemyId, ofPhases: 4, entersBelow: new[] { 1f, 0.75f, 0.5f, 0.2f });

        Assert.That(_bar.IsShown, Is.True, "the band never appeared.");
        Assert.That(_bar.SegmentCount, Is.EqualTo(4), "the band drew a count of its own rather than the event's.");

        // Four segments are three seams: the outermost phase enters at 1, which is the band's own end
        // rather than a line drawn on it.
        Assert.That(_bar.MarkCount, Is.EqualTo(3));
        Assert.That(LiveMarks().Count, Is.EqualTo(3), "the seams on screen disagree with the count.");
    }

    [Test]
    public void Bar_LeavesOnDeath()
    {
        BuildHud();

        Announce(BossEnemyId, WardenPhases);

        Assert.That(_bar.IsShown, Is.True, "the fixture's premise.");

        // **EnemyDied and not EnemyDespawned** (rule 2). There is no BossDied — the boss is an
        // ordinary agent (M4-01b rule 2) — and the gap between the two enemy events is
        // EnemySystem.CorpseTime, which the body spends dissolving. A band left up over a boss the
        // player has already killed reads as a fight that has not finished.
        _hub.Publish(new EnemyDied(BossEnemyId, new ContentId(WardenEnemyId), Vector3.Zero));

        Assert.That(_bar.IsShown, Is.False, "the band outlived the boss.");
        Assert.That(_bar.SegmentCount, Is.Zero);
        Assert.That(_bar.MarkCount, Is.Zero, "the dead boss's seams are still on screen.");
        Assert.That(Root().alpha, Is.EqualTo(0f));
        Assert.That(LiveMarks(), Is.Empty);
    }

    [Test]
    public void Bar_IgnoresEveryOtherEnemy()
    {
        BuildHud();

        Announce(BossEnemyId, WardenPhases);

        _hub.Publish(new EnemyDamaged(BossEnemyId, 400f, 0.5f, false));

        Assert.That(_bar.Fraction, Is.EqualTo(0.5f).Within(1e-4f), "the fixture's premise.");

        // **EnemyDamaged and EnemyDied are published for every body in the arena**, and a boss phase
        // summons Husks into it by the handful (M4-01b's AddWave) — so an unfiltered band would be
        // driven by whichever add was hit last and taken down by the first one to die.
        _hub.Publish(new EnemyDamaged(OtherEnemyId, 8f, 0.1f, false));

        Assert.That(
            _bar.Fraction,
            Is.EqualTo(0.5f).Within(1e-4f),
            "a Husk's health moved the boss's band.");

        _hub.Publish(new EnemyDied(OtherEnemyId, new ContentId(HuskId), Vector3.Zero));

        Assert.That(_bar.IsShown, Is.True, "a Husk dying took the boss's band down.");

        // And the beat events are filtered on the same id, which matters the day two bosses share an
        // arena: a beat belonging to somebody else must not dim this fight's readout.
        _hub.Publish(new BossBeatStarted(OtherEnemyId, 1.5f));

        Assert.That(_bar.IsInBeat, Is.False, "another boss's beat dimmed this one's band.");
    }

    // ---- Rule 3: one quantity across the whole band -----------------------------------------------

    [Test]
    public void Bar_FillIsTotalHealthNotPerSegment()
    {
        BuildHud();

        Announce(BossEnemyId, WardenPhases);

        Assert.That(_bar.Fraction, Is.EqualTo(1f).Within(1e-4f), "a boss stands up at full health.");

        // Rule 3: half the **whole** band, not the whole of the middle segment. Segments that emptied
        // one at a time would hide how close the next threshold is, which is the only thing GD §16.2's
        // row asks the element to say.
        _hub.Publish(new EnemyDamaged(BossEnemyId, 400f, 0.5f, false));

        Assert.That(_bar.Fraction, Is.EqualTo(0.5f).Within(1e-4f));
        Assert.That(Fill().fillAmount, Is.EqualTo(0.5f).Within(1e-4f));

        // **And structurally there is one bar rather than three**, which is the half of rule 3 a
        // fraction cannot show: exactly one Filled image in the whole band, stretched across all of it,
        // and the seams are Simple images drawn over the top.
        Image[] filled = BandGraphics().OfType<Image>()
            .Where(image => image.type == Image.Type.Filled)
            .ToArray();

        Assert.That(
            filled.Length,
            Is.EqualTo(1),
            "the band holds more than one Filled image, so the segments are separate bars — which is "
                + "the shape rule 3 exists to refuse.");

        var fillRect = (RectTransform)filled[0].transform;

        Assert.That(fillRect.anchorMin.x, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(fillRect.anchorMax.x, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(
            filled[0].fillMethod,
            Is.EqualTo(Image.FillMethod.Horizontal),
            "the fill is not Horizontal, so fillAmount draws a ring rather than a bar.");
        Assert.That(
            filled[0].fillOrigin,
            Is.EqualTo((int)Image.OriginHorizontal.Left),
            "the fill drains from the wrong end, so a mark at 0.66 is two-thirds from the wrong side.");
    }

    [Test]
    public void Bar_IgnoresANonFiniteFraction()
    {
        BuildHud();

        Announce(BossEnemyId, WardenPhases);

        _hub.Publish(new EnemyDamaged(BossEnemyId, 400f, 0.5f, false));

        // **The last good fill is kept, which is the opposite of EnemyHealthBar's answer and is right
        // for the opposite reason.** That bar reads a non-finite fraction as full because it is up for
        // 2.25 s over a body that is about to die; this one is up for the two minutes GD §9.1 rule 5
        // gives a fight, and snapping it back to full would read as the boss healing to maximum.
        // Mathf.Clamp01 catches none of these: every comparison against NaN is false (AR §18.3).
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.That(
                () => _hub.Publish(new EnemyDamaged(BossEnemyId, 1f, bad, false)),
                Throws.Nothing,
                $"{bad} threw.");

            Assert.That(_bar.Fraction, Is.EqualTo(0.5f).Within(1e-4f), $"{bad} moved the fill.");
            Assert.That(float.IsFinite(Fill().fillAmount), Is.True, $"{bad} reached fillAmount.");
        }

        // And a fraction outside the range is clamped rather than written through: core cannot produce
        // one, and a fillAmount above 1 is the kind of thing uGUI renders as a full bar rather than as
        // an error — so the one place that could show it is the one place that could never report it.
        foreach (float hostile in new[] { 1.4f, 12f, -0.3f })
        {
            _hub.Publish(new EnemyDamaged(BossEnemyId, 1f, hostile, false));

            Assert.That(_bar.Fraction, Is.InRange(0f, 1f), $"{hostile} was written through.");
        }
    }

    // ---- Rule 4: the marks are the asset's, not thirds --------------------------------------------

    [Test]
    public void Bar_MarksSitAtTheAuthoredThresholds()
    {
        BuildHud();

        // 1.0 / 0.8 / 0.25 — the spec's own row, and deliberately not the Warden's 1.0 / 0.66 / 0.33,
        // which even spacing would reproduce to within a hundredth. See the class remarks.
        Announce(BossEnemyId, UnevenPhases);

        Assert.That(_bar.SegmentCount, Is.EqualTo(3));
        Assert.That(_bar.MarkCount, Is.EqualTo(2));

        Assert.That(_bar.MarkFraction(0), Is.EqualTo(0.8f).Within(1e-4f));
        Assert.That(_bar.MarkFraction(1), Is.EqualTo(0.25f).Within(1e-4f));

        // **Asserted on the rects as well as on the numbers**, because "the bar was told" and "the bar
        // drew it there" are two claims and only the second is what a player sees. The seams are
        // anchored fractionally, so 0.8 of the safe area is 0.8 of it on every device.
        List<Image> marks = LiveMarks();

        Assert.That(marks.Count, Is.EqualTo(2), "two thresholds, two seams.");

        Assert.That(((RectTransform)marks[0].transform).anchorMin.x, Is.EqualTo(0.8f).Within(1e-4f));
        Assert.That(((RectTransform)marks[0].transform).anchorMax.x, Is.EqualTo(0.8f).Within(1e-4f));
        Assert.That(((RectTransform)marks[1].transform).anchorMin.x, Is.EqualTo(0.25f).Within(1e-4f));

        // And the thing rule 4 forbids: thirds. 1/3 and 2/3 are 0.0833 and 0.1333 away from the two
        // authored values, so a band that spaced its segments evenly fails this outright.
        Assert.That(
            _bar.MarkFraction(0),
            Is.Not.EqualTo(2f / 3f).Within(1e-2f),
            "the first seam is at two-thirds, so the band is spacing its segments evenly rather than "
                + "reading the phases (rule 4).");
        Assert.That(_bar.MarkFraction(1), Is.Not.EqualTo(1f / 3f).Within(1e-2f));

        // Each seam spans the band's full height, so it reads as a division rather than as a tick.
        foreach (Image mark in marks)
        {
            var rect = (RectTransform)mark.transform;

            Assert.That(rect.anchorMin.y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(rect.anchorMax.y, Is.EqualTo(1f).Within(1e-4f));
        }
    }

    [Test]
    public void Bar_LearnsItsMarksFromARealFight()
    {
        BuildHud();

        // **The row that makes every other one honest.** Everything above drives the band with an
        // array this file typed; this drives it with the object BossBehaviour publishes, built over a
        // BossSpec whose phases were authored the way Warden.asset authors its own. If BossPhases ever
        // stops handing out the spec's numbers, this is where it shows.
        var spec = new BossSpec(
            new ContentId(WardenBossId),
            new ContentId(WardenEnemyId),
            new[]
            {
                new BossPhaseSpec(BossSpec.FirstPhaseEntersBelow),
                new BossPhaseSpec(0.8f),
                new BossPhaseSpec(0.25f),
            },
            beatSeconds: 1.5f);

        var phases = new BossPhases(spec);

        _hub.Publish(new BossPhaseChanged(BossEnemyId, 0, phases.PhaseCount, phases.EntersBelow));

        Assert.That(_bar.SegmentCount, Is.EqualTo(3), "the band did not size itself from a real fight.");
        Assert.That(_bar.MarkCount, Is.EqualTo(2));
        Assert.That(_bar.MarkFraction(0), Is.EqualTo(0.8f).Within(1e-4f));
        Assert.That(_bar.MarkFraction(1), Is.EqualTo(0.25f).Within(1e-4f));
    }

    [Test]
    public void Bar_SurvivesANonsenseThresholdList()
    {
        BuildHud();

        // **A missing or broken list draws fewer seams rather than evenly spaced ones**, which is the
        // one decision this guard makes rather than merely surviving: a mark in the wrong place is
        // worse than a missing one, because the player would plan around it. BossSpec already refuses
        // an empty list and an out-of-order one, so every case here is unreachable from an asset — but
        // a NaN anchor is a rect that never renders again, which is not a failure a view may launder.
        var hostile = new (string Name, float[] Thresholds, int Seams)[]
        {
            ("null", null, 0),
            ("empty", Array.Empty<float>(), 0),
            ("first only", new[] { 1f }, 0),
            ("a NaN", new[] { 1f, float.NaN, 0.3f }, 1),
            ("an infinity", new[] { 1f, float.PositiveInfinity, 0.3f }, 1),
            ("a zero", new[] { 1f, 0f, 0.3f }, 1),
            ("out of order", new[] { 1f, 0.2f, 0.9f }, 2),
            ("longer than the count", new[] { 1f, 0.7f, 0.4f, 0.1f, 0.05f }, 2),
        };

        foreach ((string name, float[] thresholds, int seams) in hostile)
        {
            _hub.Publish(new EnemyDied(BossEnemyId, new ContentId(WardenEnemyId), Vector3.Zero));

            Assert.That(
                () => Announce(BossEnemyId, ofPhases: 3, entersBelow: thresholds),
                Throws.Nothing,
                $"{name} threw.");

            Assert.That(_bar.SegmentCount, Is.EqualTo(3), $"{name} changed the segment count.");
            Assert.That(_bar.MarkCount, Is.EqualTo(seams), $"{name} drew the wrong number of seams.");

            for (int i = 0; i < _bar.MarkCount; i++)
            {
                Assert.That(
                    _bar.MarkFraction(i),
                    Is.InRange(float.Epsilon, 1f),
                    $"{name} put a seam at a fraction the band cannot draw.");
            }

            foreach (Image mark in LiveMarks())
            {
                Assert.That(
                    float.IsFinite(((RectTransform)mark.transform).anchorMin.x),
                    Is.True,
                    $"{name} wrote a non-finite anchor, which is a rect that never renders again.");
            }
        }

        // Out of order is drawn where the list says rather than sorted, which is what "the mapping is
        // read rather than assumed" means from the other side: a list a designer typed out of order is
        // a list they meant something else by, and BossSpec refuses it at the asset — so a view that
        // quietly sorted one would be hiding the fault the catalog already reports.
        _hub.Publish(new EnemyDied(BossEnemyId, new ContentId(WardenEnemyId), Vector3.Zero));

        Announce(BossEnemyId, ofPhases: 3, entersBelow: new[] { 1f, 0.2f, 0.9f });

        Assert.That(_bar.MarkFraction(0), Is.EqualTo(0.2f).Within(1e-4f));
        Assert.That(_bar.MarkFraction(1), Is.EqualTo(0.9f).Within(1e-4f));
    }

    [Test]
    public void Bar_RefusesASegmentCountBelowOne()
    {
        BuildHud();

        // A fight with no phases is a broken reading, and an empty band across the top of the screen
        // says less than no band at all — so it hides rather than drawing one segment. Unreachable from
        // an asset: BossSpec refuses an empty phase list.
        foreach (int bad in new[] { 0, -1, int.MinValue })
        {
            Assert.That(
                () => Announce(BossEnemyId, ofPhases: bad, entersBelow: WardenPhases),
                Throws.Nothing,
                $"a count of {bad} threw.");

            Assert.That(_bar.IsShown, Is.False, $"a count of {bad} drew a band.");
            Assert.That(_bar.SegmentCount, Is.Zero);
        }
    }

    [Test]
    public void Bar_ClampsAnAbsurdSegmentCount()
    {
        BuildHud();

        // The ceiling is a refusal rather than a layout number: nothing caps how many phases a
        // BossSpec may hold, so a count that arrived wrong would otherwise be answered by creating
        // that many GameObjects on the HUD. The fight still plays.
        Announce(BossEnemyId, ofPhases: int.MaxValue, entersBelow: WardenPhases);

        Assert.That(_bar.SegmentCount, Is.EqualTo(BossBarView.MaxSegments));
        Assert.That(_bar.MarkCount, Is.LessThan(BossBarView.MaxSegments));
        Assert.That(LiveMarks().Count, Is.EqualTo(_bar.MarkCount));
    }

    // ---- Rule 5: the beat ------------------------------------------------------------------------

    [Test]
    public void Bar_SaysSoDuringABeat()
    {
        BuildHud();

        Announce(BossEnemyId, WardenPhases);

        _hub.Publish(new EnemyDamaged(BossEnemyId, 340f, 0.66f, false));

        float restingAlpha = Root().alpha;

        Assert.That(restingAlpha, Is.EqualTo(1f), "the fixture's premise: a live boss's band is full brightness.");

        // The crossing, in the order core publishes it: the phase first so a handler that sizes itself
        // has done so, then the beat (BossBehaviour.EnterPhase's own comment).
        Announce(BossEnemyId, WardenPhases, phase: 1);
        _hub.Publish(new BossBeatStarted(BossEnemyId, 1.5f));

        Assert.That(_bar.IsInBeat, Is.True);

        // **Visibly different at the same fill**, which is rule 5's exact claim: the fraction has not
        // moved, and a band that looked identical would be telling the player their hits are landing
        // when GD §9.1 rule 3 has made the boss untouchable.
        Assert.That(_bar.Fraction, Is.EqualTo(0.66f).Within(1e-4f), "the crossing refilled the band.");
        Assert.That(
            Root().alpha,
            Is.LessThan(restingAlpha),
            "the band looks the same through a beat, so it is saying the boss can be hurt.");

        _hub.Publish(new BossBeatEnded(BossEnemyId));

        Assert.That(_bar.IsInBeat, Is.False);
        Assert.That(Root().alpha, Is.EqualTo(restingAlpha), "the beat never gave the band back.");
        Assert.That(_bar.Fraction, Is.EqualTo(0.66f).Within(1e-4f));

        // And a second crossing does not resize or refill anything: the count and the marks are the
        // same fight's, so BossPhaseChanged after the first is handled by doing nothing.
        _hub.Publish(new EnemyDamaged(BossEnemyId, 170f, 0.33f, false));
        Announce(BossEnemyId, WardenPhases, phase: 2);

        Assert.That(_bar.Fraction, Is.EqualTo(0.33f).Within(1e-4f), "a later crossing refilled the band.");
        Assert.That(_bar.SegmentCount, Is.EqualTo(3));
        Assert.That(_bar.MarkCount, Is.EqualTo(2));
    }

    // ---- Rule 6: the palette ----------------------------------------------------------------------

    [Test]
    public void Bar_CarriesNoSerializedColour()
    {
        const BindingFlags Everything =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        FieldInfo[] colours = typeof(BossBarView).GetFields(Everything)
            .Where(f => f.FieldType == typeof(Color) || f.FieldType == typeof(Color32)
                || f.FieldType == typeof(Color[]))
            .Where(f => !f.IsStatic)
            .ToArray();

        // M3-13a rule 8, and the palette's own "the tenth colour is a field here": a field defaulted
        // from the palette and then dressed differently on a prefab is the placeholder problem with an
        // extra step, and worse — a read-back row would agree with the dressed value whatever it was
        // (Traps §7).
        Assert.That(
            colours.Select(f => f.Name),
            Is.Empty,
            "BossBarView carries a serialized Color. Every colour it draws is Palette's: the fill is "
                + "Palette.Boss, the track is that colour at its own alpha, and the seams are "
                + "Palette.Neutral.");

        // The two alphas *are* allowed, and saying so here is what stops the row above being read as
        // "the view may hold no numbers about how it looks" — rule 8 is explicit that a view that wants
        // transparency multiplies its own.
        foreach (string kept in new[] { "_trackAlpha", "_beatAlpha" })
        {
            Assert.That(
                typeof(BossBarView).GetField(kept, Everything),
                Is.Not.Null,
                $"BossBarView lost {kept}, which is a feel number rather than a colour — the day the "
                    + "beat needs to read louder on a phone it is this field rather than an eleventh "
                    + "palette member.");
        }
    }

    [Test]
    public void Bar_IsNotTheDangerColour()
    {
        BuildHud();

        Announce(BossEnemyId, UnevenPhases);

        _hub.Publish(new EnemyDamaged(BossEnemyId, 400f, 0.5f, false));

        var drawn = new List<(string Name, Color Colour)>
        {
            ("fill", Fill().color),
            ("track", Track().color),
        };

        List<Image> marks = LiveMarks();

        Assert.That(marks, Is.Not.Empty, "the fixture's premise: the band is drawing seams.");

        for (int i = 0; i < marks.Count; i++)
        {
            drawn.Add(($"seam {i + 1}", marks[i].color));
        }

        _hub.Publish(new BossBeatStarted(BossEnemyId, 1.5f));

        drawn.Add(("fill in a beat", Fill().color));
        drawn.Add(("track in a beat", Track().color));

        // **Rule 6's outright ban.** A permanent red-orange band across the top of the screen is the
        // most visible breach of GD §16.4's "for nothing else, ever" this project could ship, and it
        // would be up for the whole of the 75–120 s GD §9.1 rule 5 gives a boss fight. IsDanger ignores
        // alpha on purpose, so dimming through a beat is not a way around it.
        foreach ((string name, Color colour) in drawn)
        {
            Assert.That(
                Palette.IsDanger(colour),
                Is.False,
                $"the band's {name} is GD §16.4's reserved #FF4A1F.");
        }

        // And it is the palette's colours rather than merely not the forbidden one, which is the half
        // of rule 6 an absence cannot show.
        Assert.That(SameColour(Fill().color, Palette.Boss), Is.True, "the fill is not Palette.Boss.");
        Assert.That(SameColour(Track().color, Palette.Boss), Is.True, "the track is not Palette.Boss.");
        Assert.That(
            Track().color.a,
            Is.LessThan(Fill().color.a),
            "the track is as opaque as the fill, so the spent and unspent halves of the band cannot "
                + "be told apart.");

        foreach (Image mark in marks)
        {
            Assert.That(
                SameColour(mark.color, Palette.Neutral),
                Is.True,
                "a seam is not Palette.Neutral, which is the contrast that makes one legible on a "
                    + "band drawn in a brightened bone.");
        }

        // Palette.Boss is a *tenth member* rather than a second name for one that was taken:
        // Palette.Neutral is already an enemy's health bar (M3-13b) and M4-03's invulnerable-beat
        // shell, and Palette.EnemyDying is the dying tint. A boss's band in any of those would say
        // "an ordinary enemy".
        Assert.That(SameColour(Palette.Boss, Palette.Neutral), Is.False);
        Assert.That(SameColour(Palette.Boss, Palette.EnemyDying), Is.False);
        Assert.That(SameColour(Palette.Boss, Palette.Player), Is.False);
    }

    // ---- Rule 7: no raw strings, because there are no strings -------------------------------------

    [Test]
    public void Bar_DrawsNoRawString()
    {
        BuildHud();

        Announce(BossEnemyId, WardenPhases);

        const BindingFlags Everything =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        // **Rule 7 is met by the band drawing no words at all, and that is stated rather than waived.**
        // GD §16.2 asks for a bar; the boss's name, portrait and intro card are out of scope, and a
        // name needs a LocKey *and* a design answer about whether bosses are announced (M7). So there
        // is nothing for an ILocalizer to resolve — and wiring one nothing used would be worse than
        // none, because the next reader would inherit a dependency that proves nothing.
        Assert.That(
            typeof(BossBarView).GetFields(Everything)
                .Where(f => f.FieldType == typeof(string) || f.FieldType == typeof(LocKey)
                    || typeof(TMP_Text).IsAssignableFrom(f.FieldType)),
            Is.Empty,
            "BossBarView gained a label or a string. If the band now says something, every word of it "
                + "is a LocKey through ILocalizer (M3-14a, AR §11.5) and this row is what has to "
                + "change with it — a fifth Construct argument, the way PausePresenter took one at "
                + "M3-14c.");

        Assert.That(
            typeof(BossBarView).GetFields(Everything).Any(f => f.FieldType == typeof(ILocalizer)),
            Is.False,
            "BossBarView holds an ILocalizer and draws no words, so it is carrying a dependency "
                + "nothing uses.");

        // And the asset agrees: no label anywhere under the band, so nobody can type a word onto it
        // without this row going red — which is the failure M3-14a spent a task undoing.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        var authored = prefab.GetComponentInChildren<BossBarView>(true);

        Assert.That(authored, Is.Not.Null, "BossBarView did not load off Hud.prefab (Traps §5).");
        Assert.That(
            authored.GetComponentsInChildren<TMP_Text>(true),
            Is.Empty,
            "the authored band carries a label. A prefab drawing a raw word would undo the thing "
                + "M3-14c just finished.");

        Assert.That(
            _bar.GetComponentsInChildren<TMP_Text>(true),
            Is.Empty,
            "the running band grew a label, so a seam clone is carrying one.");
    }

    // ---- Rule 8: the arrangement, which is all the Editor can answer ------------------------------

    [Test]
    public void Hud_StripAndBarDoNotOverlap()
    {
        BuildHud();

        Announce(BossEnemyId, WardenPhases);

        float pxPerDp = PixelsPerDp();

        (float Top, float Bottom) strip = VerticalSpan((RectTransform)_strip.transform);
        (float Top, float Bottom) band = VerticalSpan((RectTransform)_bar.transform);

        // **Rule 8's collision, asserted in the Editor even though legibility cannot be.** The band is
        // a full-width element on the same edge as GD §16.1's 4 dp XP strip, which M3-10b put on the
        // very top of the safe area — and both are serialized dp, so if they ever met the fix is one
        // number (manual step 4).
        Assert.That(
            band.Top,
            Is.GreaterThanOrEqualTo(strip.Bottom - 1e-2f),
            "the boss band starts before the XP strip ends, so the band covers GD §16.1's one "
                + "always-there element.");

        // And the HP row below it, which the spec does not name and which shares the same top edge:
        // HudPresenter.Place puts the bar, the readout, the ring and the level at _marginDp.y down.
        float hpRowTop = Field<Vector2>(_presenter, "_marginDp").y * pxPerDp;

        Assert.That(
            band.Bottom,
            Is.LessThanOrEqualTo(hpRowTop + 1e-2f),
            "the boss band runs into the HP row, which is the collision rule 8 did not name and the "
                + "one a full-width element was always going to reach first.");

        // The arrangement this task shipped, stated as a number rather than as a description: 0–4 dp
        // the XP strip, 5–15 dp the band, 16 dp down the HP row. Two 1 dp gaps, on a HUD M3-15 already
        // found too cramped to read — **whether that is legible is M4-07's and this task may not tick
        // it** (ledger row 1).
        Assert.That(strip.Top, Is.EqualTo(0f).Within(1e-2f));
        Assert.That(strip.Bottom, Is.EqualTo(XpBarView.HeightDp * pxPerDp).Within(1e-2f));
        Assert.That(band.Top, Is.EqualTo(BossBarView.TopDp * pxPerDp).Within(1e-2f));
        Assert.That(
            band.Bottom,
            Is.EqualTo((BossBarView.TopDp + BossBarView.HeightDp) * pxPerDp).Within(1e-2f));

        // Full width, and the width is the anchors' rather than this class's arithmetic — XpBarView's
        // words: 0 to 1 with a zero sizeDelta.x is exactly the safe area's width however wide it turns
        // out to be, on both rotations.
        var bandRect = (RectTransform)_bar.transform;

        Assert.That(bandRect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(bandRect.anchorMax, Is.EqualTo(new Vector2(1f, 1f)));
        Assert.That(bandRect.sizeDelta.x, Is.EqualTo(0f).Within(1e-3f));
        Assert.That(bandRect.pivot, Is.EqualTo(new Vector2(0f, 1f)));

        // Under the same SafeAreaFitter as everything else on this prefab, so "across the top of the
        // screen" means across the part of it a landscape cutout has not eaten. `includeInactive` is
        // mandatory rather than defensive — see XpBarViewTests.Prefab_IsDressed.
        Assert.That(
            _bar.GetComponentInParent<SafeAreaFitter>(includeInactive: true),
            Is.Not.Null,
            "the band is not under a SafeAreaFitter, so a notch can cover it.");
    }

    [Test]
    public void Band_IgnoresANonFiniteHeight()
    {
        BuildHud();

        var rect = (RectTransform)_bar.transform;
        Vector2 sized = rect.sizeDelta;
        Vector2 placed = rect.anchoredPosition;

        // **The band is authored on Hud.prefab, so leaving the layout alone is an answer** —
        // XpBarView.Place's, for its reason. Zero is refused with the rest for the height (a band 0 dp
        // tall is GD §16.2's row silently absent) and the offset's zero is legal, because 0 dp down is
        // a band flush to the top edge and that is a layout the owner is entitled to ask for.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -10f })
        {
            SetPrivate(_bar, "_heightDp", bad);

            Assert.That(() => Invoke(_bar, "Place"), Throws.Nothing, $"Place threw on {bad}.");

            Assert.That(rect.sizeDelta, Is.EqualTo(sized), $"height {bad}");
            Assert.That(rect.anchoredPosition, Is.EqualTo(placed), $"height {bad}");
            Assert.That(
                float.IsFinite(rect.sizeDelta.y),
                Is.True,
                $"a height of {bad} wrote a non-finite sizeDelta, which is a rect that never renders "
                    + "again.");
        }

        SetPrivate(_bar, "_heightDp", BossBarView.HeightDp);
        SetPrivate(_bar, "_topDp", float.NaN);
        Invoke(_bar, "Place");

        Assert.That(rect.anchoredPosition, Is.EqualTo(placed), "a NaN offset moved the band.");

        // And it *does* place when both fields are usable, so the rows above are about nonsense rather
        // than about a band that has stopped being laid out at all.
        SetPrivate(_bar, "_topDp", 20f);
        SetPrivate(_bar, "_heightDp", 14f);
        Invoke(_bar, "Place");

        Assert.That(rect.sizeDelta.y, Is.EqualTo(14f * PixelsPerDp()).Within(1e-2f));
        Assert.That(rect.anchoredPosition.y, Is.EqualTo(-20f * PixelsPerDp()).Within(1e-2f));
    }

    // ---- Lifetime and the asset -------------------------------------------------------------------

    [Test]
    public void Presenter_DropsItsBossSubscriptions()
    {
        StartRun();

        GameObject hud = Instantiate();

        var presenter = hud.GetComponent<HudPresenter>();

        presenter.Construct(_hub, _session, Passthrough());

        Assert.That(_hub.SubscriberCount<BossPhaseChanged>(), Is.EqualTo(1), "the fixture's premise.");
        Assert.That(_hub.SubscriberCount<BossBeatStarted>(), Is.EqualTo(1));
        Assert.That(_hub.SubscriberCount<BossBeatEnded>(), Is.EqualTo(1));
        Assert.That(_hub.SubscriberCount<EnemyDamaged>(), Is.EqualTo(1));
        Assert.That(_hub.SubscriberCount<EnemyDied>(), Is.EqualTo(1));

        // Unity does not call OnDestroy on an object whose Awake never ran, and none did here.
        Invoke(presenter, "OnDestroy");

        Assert.That(_hub.SubscriberCount<BossPhaseChanged>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<BossBeatStarted>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<BossBeatEnded>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<EnemyDamaged>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<EnemyDied>(), Is.Zero);

        // **EnemyDamaged is the one that makes this row load-bearing rather than tidy.** It is
        // published for every body in the arena on every hit, so a HUD destroyed before its scope — a
        // scene reload, an arena opened without a run — would be handed one several times a frame for
        // a component Unity has already killed.
        Assert.That(
            () =>
            {
                _hub.Publish(new BossPhaseChanged(BossEnemyId, 0, 3, WardenPhases));
                _hub.Publish(new BossBeatStarted(BossEnemyId, 1.5f));
                _hub.Publish(new BossBeatEnded(BossEnemyId));
                _hub.Publish(new EnemyDamaged(BossEnemyId, 1f, 0.5f, false));
                _hub.Publish(new EnemyDied(BossEnemyId, new ContentId(WardenEnemyId), Vector3.Zero));
            },
            Throws.Nothing);
    }

    [Test]
    public void Construct_RefusesANullHub()
    {
        BuildHud();

        // The implied guard row. The band still needs no dependency of its own — it is a dumb view
        // this presenter hands fractions to, which is why RunScope registers nothing new for it
        // (M4-04 rule 7). M4-06 rule 2 took the death overlay and three dependencies out of this
        // class; M6-03b brought an ILocalizer back for the economy readouts' captions. The arity is
        // asserted by `Hud_NoLongerOwnsTheDeathOverlay` in `RunEndPresenterTests`.
        Assert.Throws<ArgumentNullException>(() => _presenter.Construct(null, _session, Passthrough()));
        Assert.Throws<ArgumentNullException>(() => _presenter.Construct(_hub, null, Passthrough()));
        Assert.Throws<ArgumentNullException>(() => _presenter.Construct(_hub, _session, null));
    }

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        // Read off the asset rather than off an instance: the failure Traps §5 describes is a
        // component that deserialises as null with nothing reported anywhere, and it is only visible
        // from here.
        var band = prefab.GetComponentInChildren<BossBarView>(true);
        var presenter = prefab.GetComponent<HudPresenter>();

        Assert.That(band, Is.Not.Null, "BossBarView did not load off Hud.prefab (Traps §5).");
        Assert.That(presenter, Is.Not.Null, "HudPresenter did not load off Hud.prefab (Traps §5).");

        var bandSo = new SerializedObject(band);
        var root = bandSo.FindProperty("_root").objectReferenceValue as CanvasGroup;
        var track = bandSo.FindProperty("_track").objectReferenceValue as Image;
        var fill = bandSo.FindProperty("_fill").objectReferenceValue as Image;
        var mark = bandSo.FindProperty("_mark").objectReferenceValue as Image;

        Assert.That(root, Is.Not.Null, "the band has no CanvasGroup root.");
        Assert.That(track, Is.Not.Null, "the band has no track, so nothing shows where the seams are.");
        Assert.That(fill, Is.Not.Null, "the band has no fill.");
        Assert.That(mark, Is.Not.Null, "the band has no seam template.");

        Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
        Assert.That(
            fill.fillMethod,
            Is.EqualTo(Image.FillMethod.Horizontal),
            "the band's fill is not Horizontal, so fillAmount draws a ring rather than a bar.");
        Assert.That(fill.fillOrigin, Is.EqualTo((int)Image.OriginHorizontal.Left));

        // A sprite on all three, because an Image with none renders nothing and a band that drew
        // nothing would pass every behavioural row in this file.
        Assert.That(track.sprite, Is.Not.Null, "the track has no sprite, so it draws nothing.");
        Assert.That(fill.sprite, Is.Not.Null, "the fill has no sprite, so it draws nothing.");
        Assert.That(mark.sprite, Is.Not.Null, "the seam has no sprite, so it draws nothing.");

        Assert.That(root.alpha, Is.EqualTo(0f), "the prefab ships the band across the top of the arena.");
        Assert.That(root.blocksRaycasts, Is.False, "the authored band is in the raycast path.");
        Assert.That(
            mark.gameObject.activeSelf,
            Is.False,
            "the seam template ships active, so a run with no boss shows one line at whatever "
                + "fraction it was authored at.");

        Assert.That(bandSo.FindProperty("_heightDp").floatValue, Is.EqualTo(BossBarView.HeightDp));
        Assert.That(bandSo.FindProperty("_topDp").floatValue, Is.EqualTo(BossBarView.TopDp));

        var hudSo = new SerializedObject(presenter);

        Assert.That(
            hudSo.FindProperty("_bossBar").objectReferenceValue,
            Is.Not.Null,
            "HudPresenter has no boss bar, so the five boss events reach nothing.");

        // **The rest of the prefab is unchanged**, which is the half of this row that is about the
        // asset having been saved rather than about the objects added to it. This is the fourth task to
        // touch the one asset every screen in the game sorts against.
        Assert.That(prefab.GetComponentInChildren<XpBarView>(true), Is.Not.Null, "XpBarView is gone.");
        Assert.That(prefab.GetComponentInChildren<AutoCastRow>(true), Is.Not.Null, "AutoCastRow is gone.");
        Assert.That(prefab.GetComponentInChildren<OverflowToast>(true), Is.Not.Null, "OverflowToast is gone.");
        Assert.That(
            prefab.GetComponentInChildren<SkillBarPresenter>(true),
            Is.Not.Null,
            "SkillBarPresenter is gone.");
        Assert.That(
            new SerializedObject(prefab.GetComponentInChildren<XpBarView>(true))
                .FindProperty("_heightDp").floatValue,
            Is.EqualTo(XpBarView.HeightDp),
            "the XP strip's height moved, which is the number rule 8's arrangement is measured "
                + "against.");
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>
    /// Publishes the phase event a fight would, for a boss with <paramref name="entersBelow"/> phases.
    /// </summary>
    private void Announce(int enemyId, float[] entersBelow, int phase = 0) =>
        Announce(enemyId, entersBelow.Length, entersBelow, phase);

    /// <inheritdoc cref="Announce(int, float[], int)" />
    private void Announce(int enemyId, int ofPhases, float[] entersBelow, int phase = 0) =>
        _hub.Publish(new BossPhaseChanged(enemyId, phase, ofPhases, entersBelow));

    /// <summary>A fresh run at level 1, built and started.</summary>
    /// <remarks>
    /// Fresh rather than resumed — <c>XpBarViewTests</c>' reason inverted: nothing here is about a
    /// level or a pick, so the cheapest honest run is the one a player would get from
    /// <em>Descend</em>. The boss itself is never spawned; the presenter is driven by the events one
    /// would publish, and <c>Bar_LearnsItsMarksFromARealFight</c> is what stops that being a fixture
    /// asserting against itself.
    /// </remarks>
    private void CreateRun()
    {
        _catalog = new ContentCatalog(new[] { Oathbound() }, new[] { Husk(), Warden() }, new[] { Mode() });

        _session = new RunSession(
            _catalog,
            _random,
            _hub,
            new RecordingIntents(),
            new RunRecorder(_random, _clock, _hub),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        _config = new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            _random.Seed,
            1,
            SpawnPlan.Empty,
            restore: null);
    }

    /// <summary><see cref="CreateRun"/>, started.</summary>
    private void StartRun()
    {
        CreateRun();

        _session.Start(_config);
    }

    /// <summary>Instantiates the shipped HUD, with its canvas scale pinned.</summary>
    private GameObject Instantiate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        GameObject hud = Object.Instantiate(prefab);

        _spawned.Add(hud);

        // Pinned, because a Canvas instantiated in EditMode has never been driven by its own
        // CanvasScaler and the layout rows measure in dp against this — XpBarViewTests' reason.
        hud.GetComponent<Canvas>().scaleFactor = 1f;

        return hud;
    }

    /// <summary>
    /// A started run and the shipped HUD over it, injected and started — the screen under test is the
    /// asset.
    /// </summary>
    private void BuildHud()
    {
        StartRun();

        _hud = Instantiate();

        _presenter = _hud.GetComponent<HudPresenter>();
        _bar = _hud.GetComponentInChildren<BossBarView>(true);
        _strip = _hud.GetComponentInChildren<XpBarView>(true);

        Assert.That(_presenter, Is.Not.Null, "HudPresenter did not load off Hud.prefab (Traps §5).");
        Assert.That(_bar, Is.Not.Null, "BossBarView did not load off Hud.prefab (Traps §5).");
        Assert.That(_strip, Is.Not.Null, "XpBarView did not load off Hud.prefab (Traps §5).");

        // What RunScope's RegisterComponent does, and then the Start that Unity does not run on an
        // instantiated prefab in EditMode: the placement and the first draw both live there. The band
        // is deliberately *not* constructed — nothing injects it, which is the whole of why RunScope
        // registers nothing new.
        _presenter.Construct(_hub, _session, Passthrough());
        _strip.Construct(_session, _hub);

        Invoke(_presenter, "Start");
        Invoke(_strip, "Start");
        Invoke(_bar, "Start");
    }

    private CanvasGroup Root() => Field<CanvasGroup>(_bar, "_root");

    private Image Track() => Field<Image>(_bar, "_track");

    private Image Fill() => Field<Image>(_bar, "_fill");

    /// <summary>The seams the band is currently drawing, in the order it drew them.</summary>
    private List<Image> LiveMarks() =>
        Field<List<Image>>(_bar, "_marks")
            .Where(mark => mark != null && mark.gameObject.activeSelf)
            .ToList();

    /// <summary>Every graphic in the band, template and clones included.</summary>
    private Graphic[] BandGraphics() => _bar.GetComponentsInChildren<Graphic>(true);

    /// <summary>
    /// One rect's vertical extent, in canvas units down from the parent's top edge.
    /// </summary>
    /// <remarks>
    /// Both elements on this edge are anchored to the top with a top pivot, which is what makes
    /// <c>-anchoredPosition.y</c> the top edge outright — so this is arithmetic rather than a transform
    /// walk, and it fails loudly if either is ever re-anchored. <c>XpBarViewTests.Span</c>, turned
    /// ninety degrees.
    /// </remarks>
    private static (float Top, float Bottom) VerticalSpan(RectTransform rect)
    {
        Assert.That(
            rect.pivot.y,
            Is.EqualTo(1f).Within(1e-4f),
            $"{rect.name} is no longer pivoted on its top edge, so this arithmetic is wrong.");

        float top = -rect.anchoredPosition.y;

        return (top, top + rect.sizeDelta.y);
    }

    /// <summary>Two colours to eight bits a channel, alpha ignored — <c>Palette.IsDanger</c>'s resolution.</summary>
    private static bool SameColour(Color left, Color right) =>
        Mathf.RoundToInt(left.r * 255f) == Mathf.RoundToInt(right.r * 255f)
        && Mathf.RoundToInt(left.g * 255f) == Mathf.RoundToInt(right.g * 255f)
        && Mathf.RoundToInt(left.b * 255f) == Mathf.RoundToInt(right.b * 255f);

    /// <summary>
    /// How many canvas units one dp is worth here — <c>BossBarView.PixelsPerDp</c>, with the scale
    /// <see cref="Instantiate"/> pinned.
    /// </summary>
    private static float PixelsPerDp() => StickShaper.PixelsPerDp(Screen.dpi);

    private T Track<T>(T disposable)
        where T : IDisposable
    {
        _disposables.Add(disposable);

        return disposable;
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

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),

        // The Focus ramp is switched off with a maximum of 1 — RunSessionTests' reason: it would put a
        // modifier and a stream of events into a fixture measuring neither.
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

    /// <summary>
    /// The Warden's body. Present so the events this fixture publishes name content the catalog holds,
    /// the way a live run's would; no row spawns it.
    /// </summary>
    /// <remarks>
    /// The numbers are <c>Warden.asset</c>'s own, to the value, so a row that ever does spawn one is
    /// spawning the shipped body — <c>targetPriority</c> is 8 because GD §8.1's ranking stops there and
    /// <c>EnemySpec</c> refuses a 9. The behaviour is authored <c>Static</c> rather than the asset's
    /// kind, on <c>BossPhasesTests</c>' reason: nothing here is about what the body does.
    /// </remarks>
    private static EnemySpec Warden() => new EnemySpec(
        new ContentId(WardenEnemyId),
        new LocKey("enemy.warden.name"),
        maxHp: 3200f,
        moveSpeed: 1.8f,
        targetPriority: 8,
        threatCost: 40,
        xpValue: 300f,
        isElite: false,
        contactDamage: 22f,
        reach: 2.5f,
        windupTime: 0.9f,
        recoverTime: 1.2f,
        aggroRange: 40f,
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

            // CH §5.2's shipped levelling curve, ToReach(N) = 20 + 12·N^1.4.
            new XpCurve(20f, 12f, 1.4f),
            new[] { new RosterEntry(new ContentId(HuskId), 1) },
            new[] { new ContentId(ArenaId) });
    }

    /// <summary>The real adapter over an empty table: every key resolves to itself.</summary>
    private static ILocalizer Passthrough() =>
        new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>());

    /// <summary>A long scripted stream, so no row runs the fake random dry — <c>XpBarViewTests</c>'.</summary>
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
