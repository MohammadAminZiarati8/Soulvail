using System;
using System.Collections.Generic;
using Soulvail.Game.Controls;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Hud.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// GD §16.2's last unbuilt row: <em>"a big segmented bar across the top of the screen, one segment
    /// per phase, so the player can see a phase transition coming."</em>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One quantity, with the phases marked on it</b> (M4-04 rule 3). The fill is the boss's whole
    /// health fraction across the whole band and the segments are <em>seams</em> drawn over it — not
    /// N bars that empty one at a time. That distinction is the entire point of the element: a player
    /// watching segments drain in turn can see how much of <em>this</em> segment is left and has no
    /// idea how close the next threshold is, which is the one thing <em>"see a transition coming"</em>
    /// asks for.
    /// </para>
    /// <para>
    /// <b>The seams sit where the asset put the phases, not at even spacing</b> (rule 4). A three-phase
    /// boss at 1.0 / 0.8 / 0.25 gets marks at 0.8 and 0.25, which is nothing like thirds. Both numbers
    /// arrive on <c>BossPhaseChanged</c> — the count as <c>OfPhases</c>, the thresholds as
    /// <c>EntersBelow</c> — so a four-phase Archon at M7 needs no edit here and neither does an
    /// unevenly authored one. <b>The shipped Warden could not have told the two rules apart</b>: it
    /// authors 1.0 / 0.66 / 0.33 and even spacing would have put the marks at 0.667 and 0.333, under
    /// a hundredth away. <c>Bar_MarksSitAtTheAuthoredThresholds</c> drives uneven thresholds for
    /// exactly that reason.
    /// </para>
    /// <para>
    /// <b>It is dumb and <c>HudPresenter</c> drives it</b> — <c>HpBarView</c>'s and
    /// <c>ShieldRingView</c>'s bargain rather than <c>XpBarView</c>'s. Nothing is injected here, no
    /// event reaches this class, and <c>RunScope</c> registers nothing new; the presenter that already
    /// owns this prefab's layout holds the subscriptions and the boss's id, and hands this view
    /// fractions and flags. The reason is rule 8: the band and the XP strip have to agree about where
    /// each other are, and two components each deciding their own top edge is two chances for them to
    /// overlap — which is <c>HudPresenter.Place</c>'s own argument for laying the HP row out in one
    /// method.
    /// </para>
    /// <para>
    /// <b>It draws no string at all, which is how rule 7 is met rather than waived.</b> The boss's
    /// name, portrait and intro card are out of scope — GD §16.2 asks for a bar — so there is nothing
    /// for an <c>ILocalizer</c> to resolve and no <c>ILocalizer</c> is wired. A localizer taken for a
    /// view with no words would be a dependency nothing used, which is worse than none:
    /// <c>Bar_DrawsNoRawString</c> asserts the absence rather than the resolution, over both this
    /// class and the band on the asset. The day the bar says <em>"Warden of Ash"</em> it gains a
    /// <c>LocKey</c> and a fifth constructor argument, the way <c>PausePresenter</c> did at M3-14c.
    /// </para>
    /// <para>
    /// <b>No colour is serialized and none of them is <see cref="Palette.Danger"/></b> (rule 6). The
    /// fill is <see cref="Palette.Boss"/> — the tenth colour, argued in its own remarks — the track is
    /// that colour at <see cref="_trackAlpha"/>, and the seams are <see cref="Palette.Neutral"/>. The
    /// two alphas are feel numbers rather than colour, which is the split M3-13a rule 8 drew and
    /// <c>Views_KeepTheirTimingFields</c> pins elsewhere.
    /// </para>
    /// <para>
    /// <b>It declares no <c>Update</c>.</b> A boss's health arrives as <c>EnemyDamaged</c> and its beat
    /// as two events, so there is nothing to poll — <c>XpBarView</c>'s reasoning, and the opposite half
    /// of <c>SkillButton</c>'s cooldown bargain.
    /// </para>
    /// <para>
    /// <b>How it reads is not claimed here.</b> This is the first readout added since M3-15 found the
    /// HUD too cramped to read, ledger row 1 is inherited rather than escaped, and rule 8 forbids this
    /// task from ticking a readability row. What <em>is</em> settled is the arrangement: the band sits
    /// between the 4 dp XP strip on the very top edge and the HP row 16 dp down, at
    /// <see cref="_topDp"/> and <see cref="_heightDp"/>, and <c>Hud_StripAndBarDoNotOverlap</c> is the
    /// half of the question the Editor can answer. Whether it is legible at phone size beside a
    /// landscape notch is M4-07's.
    /// </para>
    /// </remarks>
    public sealed class BossBarView : MonoBehaviour
    {
        /// <summary>
        /// The most segments the band will ever draw, and therefore the most seams it will ever make.
        /// </summary>
        /// <remarks>
        /// <b>A refusal ceiling rather than a layout number.</b> <c>BossSpec</c> refuses an empty phase
        /// list and puts the rest in order, but nothing anywhere caps how many there may be — so a
        /// count that arrived wrong would be answered by creating that many <c>GameObject</c>s on the
        /// HUD. Twelve is far past GD §9's four-phase Archon and small enough that hitting it is a bug
        /// rather than a cost. A count above it draws twelve; the fight still plays.
        /// </remarks>
        public const int MaxSegments = 12;

        /// <summary>
        /// How far down from the safe area's top edge the band starts, in dp — the default clears
        /// <c>XpBarView.HeightDp</c> by one.
        /// </summary>
        /// <remarks>
        /// <b>A constant beside the field so the row that checks the arrangement quotes the intent
        /// rather than a literal of its own</b> — <c>XpBarView.HeightDp</c>'s reason. The stack it
        /// belongs to is 0–4 dp XP strip, 5–15 dp this band, 16 dp down the HP row; see
        /// <see cref="_topDp"/>.
        /// </remarks>
        public const float TopDp = XpBarView.HeightDp + 1f;

        /// <summary>How tall the band is, in dp.</summary>
        /// <inheritdoc cref="TopDp" />
        public const float HeightDp = 10f;

        [Tooltip("The band's root. Its alpha is the whole of \"is there a boss\" and \"is it in a " +
                 "beat\", and it is never in the raycast path — a readout takes no touches.")]
        [SerializeField] private CanvasGroup _root;

        [Tooltip("The empty part of the band, behind the fill. Without it the marks ahead of the " +
                 "fill would have nothing to sit on, which is the half of the bar that says a " +
                 "transition is coming.")]
        [SerializeField] private Image _track;

        [Tooltip("The fill. Its Image must be Filled / Horizontal with the origin on the left — " +
                 "fillAmount and color are the only things written to it.")]
        [SerializeField] private Image _fill;

        [Tooltip("One seam, inactive, cloned once per segment the first time a fight needs that " +
                 "many. Its rect is re-anchored per mark; nothing else about it is written.")]
        [SerializeField] private Image _mark;

        [Tooltip("How tall the band is, in dp. Applied at runtime for the reason SkillButton " +
                 "applies its own: a Scale-With-Screen-Size canvas measures in reference pixels, " +
                 "which are a different physical size on every phone.")]
        [SerializeField] private float _heightDp = HeightDp;

        [Tooltip("How far below the safe area's top edge the band starts, in dp. It has to clear " +
                 "the 4 dp XP strip above it and the HP row 16 dp below it — rule 8, and manual " +
                 "step 4 is moving this number if they ever collide.")]
        [SerializeField] private float _topDp = TopDp;

        [Tooltip("How wide one seam is, in dp. Two is thin enough to read as a division rather " +
                 "than as a gap in the bar.")]
        [Min(0f)]
        [SerializeField] private float _markWidthDp = 2f;

        [Tooltip("How visible the empty part of the band is, 0 to 1. The view multiplies its own " +
                 "alpha rather than asking the palette for a faded colour (M3-13a rule 8).")]
        [Range(0f, 1f)]
        [SerializeField] private float _trackAlpha = 0.22f;

        [Tooltip("How bright the whole band is while the boss is invulnerable, 0 to 1. Rule 5: a " +
                 "bar that looked identical through a beat would be telling the player their hits " +
                 "are landing when they are not.")]
        [Range(0f, 1f)]
        [SerializeField] private float _beatAlpha = 0.4f;

        /// <summary>
        /// The seams, in the order they were made. Reused for every later fight, so a run with four
        /// bosses in it clones nothing after the first — see <see cref="Bind"/>.
        /// </summary>
        private readonly List<Image> _marks = new List<Image>(MaxSegments - 1);

        /// <summary>
        /// Where each live seam is drawn, as a health fraction. The authored threshold, so a row can
        /// ask what the bar was told rather than reading it back off a rect it also wrote.
        /// </summary>
        private readonly List<float> _markFractions = new List<float>(MaxSegments - 1);

        /// <summary>The fill as last drawn, in <c>[0, 1]</c>.</summary>
        private float _fraction = 1f;

        /// <summary>Whether the band is placed and dressed yet — see <see cref="Bind"/>.</summary>
        private bool _placed;

        /// <summary>How many segments the band is drawing. Zero when there is no boss.</summary>
        /// <remarks>
        /// <c>BossPhaseChanged.OfPhases</c>, clamped to <see cref="MaxSegments"/>, and deliberately
        /// not <c>EntersBelow.Count</c>: rule 1 names the count's source, and a fight that supplied
        /// one and not the other is answered by the one the rule names.
        /// </remarks>
        public int SegmentCount { get; private set; }

        /// <summary>Whether the band is on screen at all.</summary>
        public bool IsShown => SegmentCount > 0;

        /// <summary>The boss is in an invulnerable beat, and the band says so (rule 5).</summary>
        public bool IsInBeat { get; private set; }

        /// <summary>How full the band is drawing, in <c>[0, 1]</c>.</summary>
        public float Fraction => _fraction;

        /// <summary>
        /// How many seams are drawn — one fewer than <see cref="SegmentCount"/> when the thresholds
        /// arrived, and none when they did not.
        /// </summary>
        /// <remarks>
        /// The outermost phase's threshold is <c>BossSpec.FirstPhaseEntersBelow</c>, which is 1 and is
        /// therefore the band's right-hand end rather than a line drawn on it. So three segments are
        /// two seams, which is the arithmetic rule 4 is about.
        /// </remarks>
        public int MarkCount => _markFractions.Count;

        /// <summary>
        /// The health fraction seam <paramref name="index"/> is drawn at, counting from the full end.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// There is no such seam. A caller asking about one is reading a band it has not been handed.
        /// </exception>
        public float MarkFraction(int index)
        {
            if (index < 0 || index >= _markFractions.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    $"The band is drawing {_markFractions.Count} seams.");
            }

            return _markFractions[index];
        }

        /// <summary>
        /// A boss exists: draw a band of <paramref name="ofPhases"/> segments with a seam at each
        /// threshold in <paramref name="entersBelow"/> past the first.
        /// </summary>
        /// <param name="ofPhases">
        /// <c>BossPhaseChanged.OfPhases</c> (rule 1). Below 1 hides the band rather than drawing one
        /// segment: a fight with no phases is a broken reading, and an empty band across the top of
        /// the screen says less than no band at all. Above <see cref="MaxSegments"/> is clamped.
        /// </param>
        /// <param name="entersBelow">
        /// Every phase's own threshold, outermost first (rule 4). <see langword="null"/>, short or
        /// nonsense draws <em>fewer seams</em> rather than evenly spaced ones — even spacing is the
        /// thing rule 4 exists to refuse, and a mark in the wrong place is worse than a missing one
        /// because the player would plan around it.
        /// </param>
        /// <remarks>
        /// <b>Idempotent about the fill, which is what makes it safe on every crossing.</b>
        /// <c>BossPhaseChanged</c> is published at phase 0 <em>and</em> at each threshold, and
        /// <c>HudPresenter</c> only calls this for a boss it was not already following — but a second
        /// call for the same fight must not refill the bar, so the fraction is left where it was and
        /// only a genuinely new segment count resets it.
        /// </remarks>
        public void Bind(int ofPhases, IReadOnlyList<float> entersBelow)
        {
            // Called before Start on a HUD whose Awake has not run — a fixture, and a scene where
            // RunScope injects the presenter from its own Awake. Placing here rather than waiting
            // means the first frame the band is visible is already laid out.
            EnsurePlaced();

            if (ofPhases < 1)
            {
                Hide();

                return;
            }

            int segments = Mathf.Min(ofPhases, MaxSegments);

            if (segments != SegmentCount)
            {
                _fraction = 1f;
            }

            SegmentCount = segments;
            IsInBeat = false;

            BuildMarks(segments, entersBelow);
            Draw();
        }

        /// <summary>
        /// Rule 3: the boss's whole health fraction, across the whole band.
        /// </summary>
        /// <param name="fraction">
        /// <c>EnemyDamaged.HpFraction</c> — the same number <c>EnemyHealthBar</c> has read since
        /// M3-13b, and never a blackboard's copy of it: a corpse's reading is frozen rather than
        /// zeroed (M4-01a), so the event is the only honest source.
        /// </param>
        /// <remarks>
        /// <b>A non-finite fraction keeps the last good fill.</b> <c>Mathf.Clamp01</c> is two
        /// comparisons and every comparison against NaN is false, so a NaN would pass straight
        /// through both bounds into <c>fillAmount</c> — and unlike <c>EnemyHealthBar</c>, which reads
        /// one as full, the last good value is the right answer here: a boss bar is up for two minutes
        /// and snapping it back to full would read as the fight restarting (AR §18.3).
        /// </remarks>
        public void SetFraction(float fraction)
        {
            if (!float.IsFinite(fraction))
            {
                return;
            }

            _fraction = Mathf.Clamp01(fraction);

            Draw();
        }

        /// <summary>
        /// Rule 5: the invulnerable beat, on or off. Ignored while no band is drawn.
        /// </summary>
        /// <remarks>
        /// <b>The band dims rather than changing colour, and that is the palette's own rule rather
        /// than a shortcut.</b> Every member of <c>Palette</c> is opaque and <em>"a view that wants
        /// transparency multiplies its own"</em> (M3-13a rule 8) — <c>ThreatArrows</c> ramps by
        /// distance, <c>ZoneView</c> carries two alphas, <c>BulwarkView</c> a maximum. A second colour
        /// for the beat would be an eleventh palette member for a state lasting 1.5 s, and the two
        /// colours nearby are both already taken on an enemy body: <c>Palette.Neutral</c> is M4-03's
        /// beat shell and M3-13b's enemy fill, and <c>Palette.EnemyDying</c> is the dying tint.
        /// <b>Whether a dim reads as <em>"you cannot hurt it"</em> at phone size is M4-07's</b>, and it
        /// is one field.
        /// </remarks>
        public void SetBeat(bool inBeat)
        {
            IsInBeat = inBeat;

            Draw();
        }

        /// <summary>
        /// There is no boss: the band leaves. Idempotent.
        /// </summary>
        /// <remarks>
        /// Back to the state a HUD opens in rather than merely invisible — <c>EnemyHealthBar.Unbind</c>'s
        /// bargain (AR §18.4). The seams themselves are kept and reused; what is forgotten is the count,
        /// the beat and where the marks were, so a second boss with a different phase list cannot
        /// inherit the first one's divisions.
        /// </remarks>
        public void Hide()
        {
            SegmentCount = 0;
            IsInBeat = false;
            _fraction = 1f;

            _markFractions.Clear();

            for (int i = 0; i < _marks.Count; i++)
            {
                if (_marks[i] != null && _marks[i].gameObject.activeSelf)
                {
                    _marks[i].gameObject.SetActive(false);
                }
            }

            Draw();
        }

        /// <exception cref="MissingReferenceException">The root, the track, the fill or the seam is unassigned.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>HudPresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run. There is no
        /// <em>"nothing injected me"</em> branch here because nothing injects this view — see the class
        /// remarks.
        /// </remarks>
        private void Start()
        {
            // Unity's == rather than `is null`: an unassigned serialized reference is a live object
            // that only compares equal to null through the engine's operator.
            if (_root == null || _track == null || _fill == null || _mark == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(BossBarView)} is missing one of its four pieces. Drag the band's " +
                    "CanvasGroup, its track, its fill and one seam onto it — a partly dressed boss " +
                    "bar is worse than none, because a band with no track cannot show the player a " +
                    "threshold they have not reached yet, which is the whole of GD §16.2's row.");
            }

            EnsurePlaced();

            if (IsShown)
            {
                // Bound before Start, which the injection order permits: RunScope builds its container
                // from its own Awake and Unity orders no two of those. Redrawn rather than reset — the
                // alternative is a HUD that erases a fight it has already been told about.
                Draw();

                return;
            }

            // Down whatever the prefab was left dressed as, so a band someone was editing cannot ship
            // across the top of an arena with no boss in it.
            Hide();
        }

        /// <summary>Places and dresses the band once, whichever of <c>Start</c> or <see cref="Bind"/> is first.</summary>
        private void EnsurePlaced()
        {
            if (_placed)
            {
                return;
            }

            _placed = true;

            Place();

            // A readout is never in the raycast path, in any state: the stick and the Charge button
            // are under this canvas and the fight runs for the whole time the band is up. Written once
            // rather than per draw, because nothing ever changes it — OverflowToast's rule, made
            // structural.
            if (_root != null)
            {
                _root.blocksRaycasts = false;
                _root.interactable = false;
            }

            // The template is furniture and never drawn, whatever the prefab was left dressed as —
            // the same guard the death overlay gets in HudPresenter.Start, for the same reason.
            if (_mark != null && _mark.gameObject.activeSelf)
            {
                _mark.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Rule 1 and rule 4: <paramref name="segments"/> segments, and a seam at each threshold past
        /// the outermost.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Seams are made on demand and never destroyed.</b> The first fight of a run clones as
        /// many as it needs — two, for the shipped Warden — and every later boss reuses them, so
        /// nothing is instantiated again unless a deeper boss turns up with more phases than any
        /// before it. That is <c>ZoneViews</c>' rent-and-return with the return implied, and it is
        /// cheap in the one place it happens: a boss spawn is already the frame a body, a look and a
        /// behaviour are all being built. Nothing here runs per frame.
        /// </para>
        /// <para>
        /// <b>A threshold that is not a drawable fraction is skipped rather than clamped into one.</b>
        /// <c>BossSpec</c> already refuses an empty list and an out-of-order one, so this is a guard
        /// rather than a case — but a NaN would produce an anchor that never renders again, and a mark
        /// invented at 0 or 1 would sit on the band's own end and read as a threshold the fight does
        /// not have.
        /// </para>
        /// </remarks>
        private void BuildMarks(int segments, IReadOnlyList<float> entersBelow)
        {
            _markFractions.Clear();

            // The outermost phase enters at 1 — the band's own full end — so the seams are the
            // thresholds after it, and there are never more of them than there are segments to divide.
            int wanted = entersBelow is null ? 0 : Mathf.Min(segments, entersBelow.Count);

            for (int i = 1; i < wanted; i++)
            {
                float threshold = entersBelow[i];

                if (!float.IsFinite(threshold) || threshold <= 0f || threshold >= 1f)
                {
                    continue;
                }

                _markFractions.Add(threshold);
            }

            float pxPerDp = PixelsPerDp();

            for (int i = 0; i < _markFractions.Count; i++)
            {
                Image mark = MarkAt(i);

                if (mark == null)
                {
                    continue;
                }

                PlaceMark((RectTransform)mark.transform, _markFractions[i], pxPerDp);

                if (!mark.gameObject.activeSelf)
                {
                    mark.gameObject.SetActive(true);
                }
            }

            for (int i = _markFractions.Count; i < _marks.Count; i++)
            {
                if (_marks[i] != null && _marks[i].gameObject.activeSelf)
                {
                    _marks[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>Seam <paramref name="index"/>, cloned from the template if this fight is the first to need it.</summary>
        private Image MarkAt(int index)
        {
            while (_marks.Count <= index)
            {
                if (_mark == null)
                {
                    return null;
                }

                // Parented to this band rather than to the template's own parent, and appended, so
                // every seam is a later sibling than the fill and therefore drawn over it — uGUI draws
                // in hierarchy order and a seam behind the fill would vanish the moment the bar was
                // full.
                Image clone = Instantiate(_mark, transform);

                clone.name = $"Mark{_marks.Count + 1}";
                clone.gameObject.SetActive(false);

                _marks.Add(clone);
            }

            return _marks[index];
        }

        /// <summary>
        /// Rule 1's height and rule 8's offset: a full-width band <see cref="_heightDp"/> tall,
        /// <see cref="_topDp"/> below the parent's top edge.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Here rather than in the prefab for <c>XpBarView.Place</c>'s and <c>HudPresenter.Place</c>'s
        /// reason: a Scale-With-Screen-Size canvas measures in reference pixels, so a band authored at
        /// ten of those is a different physical height on every phone — and the number that has to
        /// clear a 4 dp strip above it and a 24 dp row below it is a number in dp or it is nothing.
        /// </para>
        /// <para>
        /// The width is the anchors' rather than this file's arithmetic: <c>anchorMin.x</c> 0 against
        /// <c>anchorMax.x</c> 1 with a zero <c>sizeDelta.x</c> is exactly the parent's width however
        /// wide the safe area turns out to be — <c>XpBarView</c>'s words, and the band hangs under the
        /// same <c>SafeAreaFitter</c>, so <em>"across the top of the screen"</em> means across the part
        /// of it a landscape notch has not eaten.
        /// </para>
        /// <para>
        /// <b>A nonsense height or offset leaves the authored layout alone</b>, which is
        /// <c>XpBarView.Place</c>'s answer for its reason: the band <em>is</em> authored on
        /// <c>Hud.prefab</c> at a real height across a real edge, so there is something honest to fall
        /// back to. Zero is refused with the rest for the height — a band 0 dp tall is GD §16.2's row
        /// silently absent — and accepted for the offset, because 0 dp down is a band flush to the top
        /// edge, which is a layout the owner is entitled to ask for even though it would then sit under
        /// the strip.
        /// </para>
        /// </remarks>
        private void Place()
        {
            if (transform is not RectTransform rect
                || !float.IsFinite(_heightDp)
                || _heightDp <= 0f
                || !float.IsFinite(_topDp))
            {
                return;
            }

            float pxPerDp = PixelsPerDp();

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(0f, _heightDp * pxPerDp);
            rect.anchoredPosition = new Vector2(0f, -_topDp * pxPerDp);
        }

        /// <summary>
        /// Pins one seam across the band's full height at <paramref name="fraction"/> of its width.
        /// </summary>
        /// <remarks>
        /// <b>Anchored fractionally rather than positioned in pixels</b>, which is what makes rule 4
        /// survive a screen: a seam at <c>anchorMin.x</c> 0.8 is at 80 % of whatever the safe area
        /// turns out to be, on every device and after every rotation, where arithmetic against a
        /// measured width would be a second copy of what the anchors already know. Centred on the
        /// threshold rather than started at it, so the line straddles the number rather than sitting
        /// just past it.
        /// </remarks>
        private void PlaceMark(RectTransform rect, float fraction, float pxPerDp)
        {
            rect.anchorMin = new Vector2(fraction, 0f);
            rect.anchorMax = new Vector2(fraction, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(_markWidthDp * pxPerDp, 0f);
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>Everything the band currently says, written at once.</summary>
        /// <remarks>
        /// Every write is guarded by a comparison against what is already on screen, and that is not
        /// merely an optimisation: assigning <c>fillAmount</c>, <c>color</c> or <c>alpha</c> marks the
        /// graphic dirty and queues a canvas rebuild, and a boss taking a flurry of hits publishes
        /// several <c>EnemyDamaged</c> in one frame — <c>EnemyHealthBar.Draw</c>'s argument, on the
        /// screen rather than in the world.
        /// </remarks>
        private void Draw()
        {
            if (_root == null || _track == null || _fill == null)
            {
                return;
            }

            float alpha = Alpha();

            if (!Mathf.Approximately(_root.alpha, alpha))
            {
                _root.alpha = alpha;
            }

            if (!Mathf.Approximately(_fill.fillAmount, _fraction))
            {
                _fill.fillAmount = _fraction;
            }

            if (_fill.color != Palette.Boss)
            {
                _fill.color = Palette.Boss;
            }

            // The palette is opaque and this view multiplies its own alpha — M3-13a rule 8. The track
            // is the same bone as the fill so the band reads as one element with a part of it spent,
            // rather than as two bars of different colours meeting at the fill's edge.
            Color track = Palette.Boss;

            track.a = Mathf.Clamp01(_trackAlpha);

            if (_track.color != track)
            {
                _track.color = track;
            }

            // The seams are Palette.Neutral, GD §16.4's "everything else", and that is the contrast
            // that makes one legible: Palette.Boss is the same bone brightened, so a seam is a darker
            // line of the same family on the filled part and a darker line still against the faint
            // track ahead of it.
            for (int i = 0; i < _marks.Count; i++)
            {
                if (_marks[i] != null && _marks[i].color != Palette.Neutral)
                {
                    _marks[i].color = Palette.Neutral;
                }
            }
        }

        /// <summary>
        /// How bright the band is: nothing with no boss, <see cref="_beatAlpha"/> through a beat, and
        /// full otherwise.
        /// </summary>
        private float Alpha()
        {
            if (!IsShown)
            {
                return 0f;
            }

            return IsInBeat ? Mathf.Clamp01(_beatAlpha) : 1f;
        }

        /// <summary>
        /// How many canvas units one dp is worth — <c>HudPresenter.PixelsPerDp</c>, for its reason.
        /// </summary>
        private float PixelsPerDp()
        {
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            return StickShaper.PixelsPerDp(Screen.dpi) / scale;
        }
    }
}
