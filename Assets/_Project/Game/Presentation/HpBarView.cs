using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Hud.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The player's health bar: a fill that snaps, and a ghost behind it that lags. GD §16.1 and
    /// §16.2 — "always-on bar, top-left, large, with ghost-damage trail. Never ambiguous."
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The ghost is the whole point of this class.</b> A bar that only snapped would tell the
    /// player what their health <em>is</em>; the trail tells them what just happened to it, which is
    /// the thing you actually need in the half-second after a Husk connects. So the fill is exact
    /// and instant, and the pale ghost holds the old value for 0.4 s before draining down to meet
    /// it — long enough to be read out of the corner of an eye that is watching the arena.
    /// </para>
    /// <para>
    /// <b>It holds no health.</b> Every number arrives through <see cref="Set"/>; the floats kept
    /// here are widths on a screen, not hit points, and nothing in core can be derived from them.
    /// The presenter is what listens to events — this only knows how to draw one.
    /// </para>
    /// <para>
    /// <b>A heal snaps the ghost, a hit does not.</b> The trail exists to show damage, and a ghost
    /// that lagged <em>upward</em> would draw a band over health the player already has — exactly
    /// backwards, and it would read as damage taken at the moment of being healed.
    /// </para>
    /// <para>
    /// <b>As of UI-01b it is drawn in the owner's art, and a width is an anchor rather than a
    /// <c>fillAmount</c>.</b> The fill, the ghost and the gloss are sliced pills inside the frame's
    /// well, and each one's right-hand anchor is its fraction. A horizontal <c>Filled</c> image cuts
    /// a pill's round end square, and a <c>Filled</c> image does not slice at all, so its round ends
    /// stretch with the bar. A sliced image narrower than its borders shrinks them rather than
    /// overlapping them, so a sliver of health is drawn as a sliver.
    /// </para>
    /// <para>
    /// <b>Its colours are <see cref="Palette"/>'s and none is serialized</b> (M3-13a). The ghost joined
    /// them at UI-01b as <see cref="Palette.PlayerGhost"/>: it had been the prefab's 50 % gold since
    /// M1-17, a colour no code wrote, and gold means rewards. The timing fields beside them stay,
    /// because a feel number is the owner's to tune and a colour is the palette's.
    /// </para>
    /// <para>
    /// <b>As of M3-13b it also draws what Bulwark put on the player</b> — a segment ahead of the
    /// fill, in absorption order, driven by <c>ShieldGranted</c> and <c>ShieldGrantExpired</c>. It is
    /// still a bar that holds no health: the segment is a third width, and
    /// <see cref="SetGrantedShield"/>'s remarks are where the case for it being here rather than on
    /// the Aegis ring lives.
    /// </para>
    /// </remarks>
    public sealed class HpBarView : MonoBehaviour
    {
        /// <summary>
        /// Below this fraction the fill beats (UI-01b rule 8). GD §16.3 puts the low state at 25 %.
        /// </summary>
        public const float LowFraction = 0.25f;

        /// <summary>
        /// How long the ghost holds the old value after a hit, in seconds. Restarted by every hit,
        /// so a flurry leaves one trail that drains once rather than a bar that stutters.
        /// </summary>
        private const float GhostDelay = 0.4f;

        /// <summary>How fast the ghost drains once the delay is up, in fraction per second.</summary>
        private const float GhostSpeed = 1.5f;

        /// <summary>
        /// How far the well sits in from each end of the frame, as a fraction of the bar's height —
        /// past the frame's end caps. The mockup's 34 px on a 66 px bar, rounded.
        /// </summary>
        private const float WellEndInset = 0.5f;

        /// <summary>How far the well sits in from the frame's top and bottom, as a fraction of its height.</summary>
        private const float WellEdgeInset = 0.23f;

        /// <summary>How far the gloss sits in from each end of the fill, as a fraction of the well's height.</summary>
        private const float GlossInset = 0.25f;

        [Tooltip("The health fill: a sliced Fill_Bar in the well. Its right-hand anchor is the " +
                 "fraction, and its colour is the only other thing written to it.")]
        [SerializeField] private Image _fill;

        [Tooltip("The damage trail, drawn behind the fill: an earlier sibling in the well, sliced " +
                 "like it. Without it the bar still reads correctly, it just stops saying how " +
                 "much of the drop was a moment ago.")]
        [SerializeField] private Image _ghost;

        [Tooltip("The granted-shield segment, drawn ahead of the fill: a later sibling in the well, " +
                 "so it sits over the fill's right-hand end. Optional — a HUD dressed without one " +
                 "draws a correct health bar that simply cannot say the player was granted anything.")]
        [SerializeField] private Image _shieldSegment;

        [Tooltip("The frame: a sliced HealthBar_Frame across the whole bar. Its dark well is the " +
                 "track. Optional — without it the fills draw on nothing, which is legible and ugly.")]
        [SerializeField] private Image _frame;

        [Tooltip("Where the fills live, inset in the frame past its end caps. Optional — without it " +
                 "the fills span the whole bar.")]
        [SerializeField] private RectTransform _well;

        [Tooltip("The shine over the fill's top half: a sliced Gloss_Streak that follows the fill's " +
                 "width. Optional.")]
        [SerializeField] private Image _gloss;

        [Tooltip("How long the blocked flash lasts, in seconds. CC §7's i-frames are half a " +
                 "second; this is the tap that says they were spent, not a bar of them.")]
        [Min(0f)]
        [SerializeField] private float _blockedSeconds = 0.06f;

        [Tooltip("How many times a second the fill beats below a quarter of its health. A feel " +
                 "number, the owner's to tune on a device; zero or less holds it still.")]
        [Min(0f)]
        [SerializeField] private float _lowPulseHz = 2f;

        /// <summary>The fill as last drawn, in <c>[0, 1]</c>.</summary>
        private float _shown;

        /// <summary>The ghost as last drawn. Never below <see cref="_shown"/> once settled.</summary>
        private float _ghostShown;

        /// <summary>Seconds left before the ghost may start draining. Zero means it is free to.</summary>
        private float _ghostDelay;

        /// <summary>Seconds left of the blocked flash. Zero means the fill is its normal colour.</summary>
        private float _blockedRemaining;

        /// <summary>Seconds the fill has been low, which is where its beat is up to.</summary>
        private float _lowClock;

        /// <summary>
        /// Granted shield points over the live maximum, in <c>[0, 1]</c>. Zero hides the segment.
        /// </summary>
        /// <remarks>
        /// A width on a bar, not a pool of points — the same thing <see cref="_shown"/> is, and for
        /// the same reason: nothing in core can be derived from it.
        /// </remarks>
        private float _granted;

        /// <summary>
        /// Whether <see cref="Set"/> has ever been called. The first value snaps both bars rather
        /// than trailing from whatever the prefab was authored at — a run that begins on less than
        /// full health must not open with a ghost describing damage that never happened.
        /// </summary>
        private bool _hasValue;

        /// <summary>The fill as drawn, in <c>[0, 1]</c>.</summary>
        public float Fraction => _shown;

        /// <summary>The ghost as drawn, in <c>[0, 1]</c>.</summary>
        public float GhostFraction => _ghostShown;

        /// <summary>
        /// The fill is above nothing and below <see cref="LowFraction"/>, so it beats (UI-01b rule 8).
        /// At zero it does not: a player with no health is dead, and <c>RunEndPresenter</c> is on screen.
        /// </summary>
        public bool IsLow => _shown > 0f && _shown < LowFraction;

        /// <summary>
        /// Draws <paramref name="fraction"/> of health: the fill moves at once, the ghost follows
        /// on its own clock.
        /// </summary>
        /// <param name="fraction">
        /// Current HP over the live maximum, in <c>[0, 1]</c>. Clamped rather than trusted — a width
        /// outside the range is something uGUI draws past the frame rather than reporting, and a
        /// health bar that reads full while it is not is the most misleading thing on the screen.
        /// </param>
        public void Set(float fraction)
        {
            float clamped = Mathf.Clamp01(fraction);

            if (!_hasValue)
            {
                _hasValue = true;
                _ghostShown = clamped;
                _ghostDelay = 0f;
            }
            else if (clamped > _shown)
            {
                // A heal. The ghost goes with it — see the class remarks.
                _ghostShown = clamped;
                _ghostDelay = 0f;
            }
            else if (clamped < _shown)
            {
                // A hit. The ghost stays where it is and the wait starts again from the top, so
                // three hits in a second leave one trail from the health before the first of them.
                _ghostDelay = GhostDelay;
            }

            // An unchanged value deliberately touches neither the ghost nor its clock: a redraw
            // that moved nothing would cut short a trail that is still being read.
            bool wasLow = IsLow;

            _shown = clamped;

            // A beat starts from the player's own cyan, so crossing into the low state reads as the
            // bar beginning to beat rather than as it jumping to a colour mid-beat.
            if (IsLow && !wasLow)
            {
                _lowClock = 0f;
            }

            Draw();
        }

        /// <summary>
        /// Something hit the player and was turned away: flash, and do not move the bar.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Set"/> because a blocked hit is not a health value — nothing
        /// changed, and calling <see cref="Set"/> with the unchanged fraction would say so by
        /// saying nothing at all. CC §7 gives the Oathbound half a second of i-frames after every
        /// hit, and a player who cannot see them spent concludes the game dropped a hit.
        /// </remarks>
        public void FlashBlocked()
        {
            _blockedRemaining = _blockedSeconds;

            Draw();
        }

        /// <summary>
        /// Granted shield points over the live maximum, drawn ahead of the fill. 0 hides it.
        /// </summary>
        /// <param name="fraction">
        /// Granted points over <c>MaxHp</c>, in <c>[0, 1]</c>. Clamped rather than trusted, for the
        /// reason <see cref="Set"/> clamps — and a non-finite value is read as <em>none</em>, because
        /// <c>Mathf.Clamp01</c> is two comparisons and every comparison against NaN is false (AR
        /// §18.3), so a NaN would pass through both bounds and into an anchor.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>A segment on the HP bar, and deliberately not a second arc on the Aegis ring.</b>
        /// M3-11a rule 5 is explicit that granted points are <em>"deliberately not the Aegis"</em>:
        /// the ring draws <c>ShieldSpec</c>'s 30 points, its 4 s delay and its 15/s refill, and CH
        /// §3.1 makes that the Oathbound's signature. A second arc would make one readout mean two
        /// pools with different rules.
        /// </para>
        /// <para>
        /// <b>Ahead of the fill, which is also where the points actually are.</b>
        /// <c>ApplyDamage</c> spends granted shield first, then the Aegis, then hit points — so the
        /// bar reads in absorption order rather than in an order chosen for looks.
        /// </para>
        /// <para>
        /// <b>Its colour is <see cref="Palette.PlayerBlocked"/> rather than a member of its own.</b>
        /// That is already this bar's <em>"that one did not land"</em> cyan, which is exactly what a
        /// granted shield is about to make true; it stays inside GD §16.4's player family, and being
        /// a brightened <see cref="Palette.Player"/> it is separable from the fill at the seam the two
        /// share.
        /// </para>
        /// <para>
        /// <b>The readout is not told.</b> GD §16.2's player row is <em>"never ambiguous"</em>, and
        /// <c>140 / 140 (+35)</c> would be a third number inside a 22 dp bar. The segment says it; the
        /// text does not say it again.
        /// </para>
        /// </remarks>
        public void SetGrantedShield(float fraction)
        {
            float clamped = float.IsFinite(fraction) ? Mathf.Clamp01(fraction) : 0f;

            if (Mathf.Approximately(clamped, _granted))
            {
                return;
            }

            _granted = clamped;

            Draw();
        }

        /// <summary>
        /// Insets the well in the frame and fits every slice to the bar's height (UI-01b rule 4).
        /// </summary>
        /// <remarks>
        /// <c>HudPresenter.Place</c>'s to call, once it has sized the bar: the insets are fractions of
        /// the bar's height because the frame's end caps scale with it, and a slice's borders are drawn
        /// at the scale <see cref="HudLayout.FitSlices"/> gives them from the image's own height.
        /// Nothing here reads a dp — the presenter has already turned them into a rect.
        /// </remarks>
        public void Fit()
        {
            HudLayout.FitSlices(_frame);

            if (_well != null && transform is RectTransform bar)
            {
                float height = bar.rect.height;

                if (float.IsFinite(height) && height > 0f)
                {
                    var ends = new Vector2(WellEndInset * height, WellEdgeInset * height);

                    _well.anchorMin = Vector2.zero;
                    _well.anchorMax = Vector2.one;
                    _well.pivot = new Vector2(0.5f, 0.5f);
                    _well.offsetMin = ends;
                    _well.offsetMax = -ends;
                }
            }

            HudLayout.FitSlices(_ghost);
            HudLayout.FitSlices(_fill);
            HudLayout.FitSlices(_shieldSegment);

            // The gloss is half the well's height, so its ends are fitted after it has been drawn at
            // that height.
            Draw();
            HudLayout.FitSlices(_gloss);
        }

        /// <exception cref="MissingReferenceException">The fill or the ghost is not dressed.</exception>
        /// <remarks>
        /// Checked in <c>Start</c> rather than <c>Awake</c> for the reason every other view in this
        /// project gives: <c>RunScope</c> builds its container from its own <c>Awake</c>, and Unity
        /// gives no order between two of those.
        /// </remarks>
        private void Start()
        {
            if (_fill == null || _ghost == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(HpBarView)} is missing its fill or its ghost. Drag the Fill and " +
                    "Ghost children of the HP bar onto them — without both, the one thing the " +
                    "player is never allowed to be unsure about stops being drawn.");
            }
        }

        /// <remarks>
        /// Three clocks, and none is core's: this is presentation, so it runs on
        /// <see cref="Time.deltaTime"/> and nothing that happens here is visible to a rule. The body
        /// is <see cref="Tick"/>, so a fixture can drive it with a delta of its choosing.
        /// </remarks>
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>Advances the blocked flash, the ghost and the low beat by <paramref name="dt"/>.</summary>
        /// <remarks>
        /// Most frames it does nothing at all and returns after three comparisons. A low bar redraws
        /// every frame, because its colour moves every frame — which is the one state where that is
        /// the point.
        /// </remarks>
        private void Tick(float dt)
        {
            bool dirty = false;

            if (_blockedRemaining > 0f)
            {
                _blockedRemaining -= dt;

                if (_blockedRemaining <= 0f)
                {
                    _blockedRemaining = 0f;
                    dirty = true;
                }
            }

            if (_ghostShown > _shown)
            {
                if (_ghostDelay > 0f)
                {
                    _ghostDelay -= dt;
                }

                if (_ghostDelay <= 0f)
                {
                    _ghostDelay = 0f;
                    _ghostShown = Mathf.Max(_shown, _ghostShown - (GhostSpeed * dt));
                    dirty = true;
                }
            }

            if (IsLow)
            {
                _lowClock += dt;
                dirty = true;
            }

            if (dirty)
            {
                Draw();
            }
        }

        /// <remarks>
        /// Every write is guarded by a comparison against what is already on screen, and that is
        /// not merely an optimisation: assigning an anchor or a <c>color</c> marks the graphic dirty
        /// and queues a canvas rebuild, so writing an unchanged value would rebuild the HUD on every
        /// frame of a fight where nothing had changed.
        /// </remarks>
        private void Draw()
        {
            // Unity's ==, and a silent return: Set can be called before Start has run — core
            // publishes RunStarted from an entry point, and Unity gives no order against a
            // component's own Start — so an undressed reference here is reported by the guard in
            // Start rather than by a null on the first event of the run.
            if (_fill == null || _ghost == null)
            {
                return;
            }

            DrawWidth(_fill, _shown);
            Paint(_fill, FillColour());

            DrawWidth(_ghost, _ghostShown);
            Paint(_ghost, Palette.PlayerGhost);

            DrawGloss();
            DrawGrantedShield();
        }

        /// <summary>
        /// The fill's colour: the blocked flash over everything, then the low beat, then the player's
        /// own cyan (UI-01b rules 7 and 8).
        /// </summary>
        /// <remarks>
        /// <b>The beat is a mix of two palette members of one family, and never leaves it.</b>
        /// <c>Color.Lerp</c> between <see cref="Palette.Player"/> and <see cref="Palette.PlayerLow"/>
        /// moves every channel between the two, so no colour it passes through is warmer than either
        /// — which is the whole of the owner's <em>"must never turn red-orange"</em>. A nonsense beat
        /// rate holds the fill still at the player's cyan rather than writing a NaN colour.
        /// </remarks>
        private Color FillColour()
        {
            if (_blockedRemaining > 0f)
            {
                return Palette.PlayerBlocked;
            }

            if (!IsLow || !float.IsFinite(_lowPulseHz) || _lowPulseHz <= 0f)
            {
                return Palette.Player;
            }

            float beat = 0.5f - (0.5f * Mathf.Cos(2f * Mathf.PI * _lowPulseHz * _lowClock));

            return Color.Lerp(Palette.Player, Palette.PlayerLow, beat);
        }

        /// <summary>
        /// Anchors <paramref name="image"/> from the well's left edge to <paramref name="fraction"/> of
        /// it, and switches it off at zero.
        /// </summary>
        /// <remarks>
        /// Switched off rather than drawn at zero width: a rect whose two anchors are equal is a
        /// graphic uGUI still rebuilds and still batches, and a sliced pill with no width draws its
        /// two end caps as a dot.
        /// </remarks>
        private static void DrawWidth(Image image, float fraction)
        {
            bool any = fraction > 0f;

            if (image.enabled != any)
            {
                image.enabled = any;
            }

            if (!any || image.transform is not RectTransform rect)
            {
                return;
            }

            var max = new Vector2(fraction, 1f);

            if (rect.anchorMin != Vector2.zero || rect.anchorMax != max)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = max;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
        }

        private static void Paint(Image image, Color colour)
        {
            if (image.color != colour)
            {
                image.color = colour;
            }
        }

        /// <summary>
        /// The shine over the fill's top half, as wide as the fill less a quarter of the well's height
        /// at each end, and gone while the fill is narrower than the well is tall (UI-01b rule 6).
        /// </summary>
        private void DrawGloss()
        {
            if (_gloss == null || _gloss.transform is not RectTransform rect)
            {
                return;
            }

            Rect well = _well != null ? _well.rect : ((RectTransform)transform).rect;

            bool shown = _shown > 0f && (_shown * well.width) >= well.height;

            if (_gloss.enabled != shown)
            {
                _gloss.enabled = shown;
            }

            if (!shown)
            {
                return;
            }

            float inset = GlossInset * well.height;
            var min = new Vector2(0f, 0.5f);
            var max = new Vector2(_shown, 1f);

            if (rect.anchorMin != min || rect.anchorMax != max || !Mathf.Approximately(rect.offsetMin.x, inset))
            {
                rect.anchorMin = min;
                rect.anchorMax = max;
                rect.offsetMin = new Vector2(inset, 0f);
                rect.offsetMax = new Vector2(-inset, 0f);
            }
        }

        /// <summary>
        /// Puts the granted-shield segment where the fill ends, as wide as the points are worth.
        /// </summary>
        /// <remarks>
        /// Driven through the rect's anchors, which is how the fill is drawn too now — what has to be
        /// true is <em>where the segment begins</em>. The segment is switched off rather than drawn
        /// at zero width, for <see cref="DrawWidth"/>'s reason.
        /// </remarks>
        private void DrawGrantedShield()
        {
            if (_shieldSegment == null)
            {
                return;
            }

            bool any = _granted > 0f;

            if (_shieldSegment.enabled != any)
            {
                _shieldSegment.enabled = any;
            }

            if (!any)
            {
                return;
            }

            Paint(_shieldSegment, Palette.PlayerBlocked);

            if (_shieldSegment.transform is not RectTransform rect)
            {
                return;
            }

            // Ahead of the fill while there is room, and pressed back against the well's right-hand
            // end when there is not — which is the case that matters, because Bulwark grants a shield
            // at the moment the player is about to be hit and that is usually at full health. Letting
            // it overhang instead would run a band of cyan off the bar and into the frame; collapsing
            // it to nothing, which is what a plain clamp does, would draw no shield at all in exactly
            // the situation the node exists for. So the width is preserved and the *start* gives way:
            // the bar keeps meaning "how much more can I take", and the bright band on its end says
            // "and this part is temporary" — GD §16.2's "never ambiguous", and still in absorption
            // order.
            float end = Mathf.Min(1f, _shown + _granted);

            var min = new Vector2(Mathf.Max(0f, end - _granted), 0f);
            var max = new Vector2(end, 1f);

            if (rect.anchorMin != min || rect.anchorMax != max)
            {
                rect.anchorMin = min;
                rect.anchorMax = max;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
        }
    }
}
