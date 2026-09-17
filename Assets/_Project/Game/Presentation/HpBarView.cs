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
    /// and instant, and the gold ghost holds the old value for 0.4 s before draining down to meet
    /// it — long enough to be read out of the corner of an eye that is watching the arena.
    /// </para>
    /// <para>
    /// <b>It holds no health.</b> Every number arrives through <see cref="Set"/>; the two floats
    /// kept here are pixels-on-screen, not hit points, and nothing in core can be derived from them.
    /// The presenter is what listens to events — this only knows how to draw one.
    /// </para>
    /// <para>
    /// <b>A heal snaps the ghost, a hit does not.</b> The trail exists to show damage, and a ghost
    /// that lagged <em>upward</em> would draw a gold band over health the player already has —
    /// exactly backwards, and it would read as damage taken at the moment of being healed.
    /// </para>
    /// <para>
    /// <b>Its two colours are <see cref="Palette"/>'s as of M3-13a, and they are no longer fields.</b>
    /// They were serialized <see cref="Color"/>s from M1-17, dressed on <c>Hud.prefab</c> at the same
    /// values — which is the placeholder problem with an extra step: a field initialised from the
    /// palette and then dressed differently would read back as agreeing with itself (Traps §7).
    /// Removing the fields makes the prefab's stored values unreachable rather than contradictory.
    /// The timing field beside them stays, because this task takes colours and nothing else.
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
        /// How long the ghost holds the old value after a hit, in seconds. Restarted by every hit,
        /// so a flurry leaves one trail that drains once rather than a bar that stutters.
        /// </summary>
        private const float GhostDelay = 0.4f;

        /// <summary>How fast the ghost drains once the delay is up, in fraction per second.</summary>
        private const float GhostSpeed = 1.5f;

        [Tooltip("The health fill. Its Image must be Filled / Horizontal with the origin on the " +
                 "left — fillAmount and color are the only things written to it.")]
        [SerializeField] private Image _fill;

        [Tooltip("The damage trail, drawn behind the fill: an earlier sibling, same rect, same " +
                 "fill method. Without it the bar still reads correctly, it just stops saying how " +
                 "much of the drop was a moment ago.")]
        [SerializeField] private Image _ghost;

        [Tooltip("The granted-shield segment, drawn ahead of the fill: same rect, same fill method, " +
                 "a later sibling so it sits over the bar's right-hand end. Optional — a HUD " +
                 "dressed without one draws a correct health bar that simply cannot say the player " +
                 "was granted anything.")]
        [SerializeField] private Image _shieldSegment;

        [Tooltip("How long the blocked flash lasts, in seconds. CC §7's i-frames are half a " +
                 "second; this is the tap that says they were spent, not a bar of them.")]
        [Min(0f)]
        [SerializeField] private float _blockedSeconds = 0.06f;

        /// <summary>The fill as last drawn, in <c>[0, 1]</c>.</summary>
        private float _shown;

        /// <summary>The ghost as last drawn. Never below <see cref="_shown"/> once settled.</summary>
        private float _ghostShown;

        /// <summary>Seconds left before the ghost may start draining. Zero means it is free to.</summary>
        private float _ghostDelay;

        /// <summary>Seconds left of the blocked flash. Zero means the fill is its normal colour.</summary>
        private float _blockedRemaining;

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
        /// full health must not open with a gold band describing damage that never happened.
        /// </summary>
        private bool _hasValue;

        /// <summary>
        /// Draws <paramref name="fraction"/> of health: the fill moves at once, the ghost follows
        /// on its own clock.
        /// </summary>
        /// <param name="fraction">
        /// Current HP over the live maximum, in <c>[0, 1]</c>. Clamped rather than trusted — a
        /// <c>fillAmount</c> outside the range is something uGUI draws as a full or an empty bar
        /// rather than reporting, and a health bar that reads full while it is not is the most
        /// misleading thing on the screen.
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
            _shown = clamped;

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
        /// share — which a second colour of the same value would not be. M3-13a rule 7 prices a new
        /// palette member, and M3-13b spends that once, on <c>EnemyDying</c>.
        /// </para>
        /// <para>
        /// <b>The readout is not told.</b> GD §16.2's player row is <em>"never ambiguous"</em>, and
        /// <c>140/140 (+35)</c> is a third number in a 320 dp row that M3-10b has just added a level
        /// label to. The segment says it; the text does not say it again.
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
        /// Two clocks, and neither is core's: this is presentation, so it runs on
        /// <see cref="Time.deltaTime"/> and nothing that happens here is visible to a rule. Most
        /// frames it does nothing at all and returns after two comparisons.
        /// </remarks>
        private void Update()
        {
            bool dirty = false;

            if (_blockedRemaining > 0f)
            {
                _blockedRemaining -= Time.deltaTime;

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
                    _ghostDelay -= Time.deltaTime;
                }

                if (_ghostDelay <= 0f)
                {
                    _ghostDelay = 0f;
                    _ghostShown = Mathf.Max(_shown, _ghostShown - (GhostSpeed * Time.deltaTime));
                    dirty = true;
                }
            }

            if (dirty)
            {
                Draw();
            }
        }

        /// <remarks>
        /// Every write is guarded by a comparison against what is already on screen, and that is
        /// not merely an optimisation: assigning <c>fillAmount</c> or <c>color</c> marks the graphic
        /// dirty and queues a canvas rebuild, so writing an unchanged value would rebuild the HUD on
        /// every frame of a fight where nothing had changed.
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

            if (!Mathf.Approximately(_fill.fillAmount, _shown))
            {
                _fill.fillAmount = _shown;
            }

            Color colour = _blockedRemaining > 0f ? Palette.PlayerBlocked : Palette.Player;

            if (_fill.color != colour)
            {
                _fill.color = colour;
            }

            if (!Mathf.Approximately(_ghost.fillAmount, _ghostShown))
            {
                _ghost.fillAmount = _ghostShown;
            }

            DrawGrantedShield();
        }

        /// <summary>
        /// Puts the granted-shield segment where the fill ends, as wide as the points are worth.
        /// </summary>
        /// <remarks>
        /// Driven through the rect's anchors rather than through a <c>fillAmount</c>, because what
        /// has to be true is <em>where the segment begins</em> and a filled image can only say how
        /// far it reaches. The segment is switched off rather than drawn at zero width: a rect whose
        /// two anchors are equal is a graphic uGUI still rebuilds and still batches.
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

            if (_shieldSegment.color != Palette.PlayerBlocked)
            {
                _shieldSegment.color = Palette.PlayerBlocked;
            }

            if (_shieldSegment.transform is not RectTransform rect)
            {
                return;
            }

            // Ahead of the fill while there is room, and pressed back against the bar's right-hand
            // end when there is not — which is the case that matters, because Bulwark grants a shield
            // at the moment the player is about to be hit and that is usually at full health. Letting
            // it overhang instead would put an 80 dp band of cyan across the current/max readout that
            // HudPresenter.Place puts 8 dp to the bar's right; collapsing it to nothing, which is what
            // a plain clamp does, would draw no shield at all in exactly the situation the node
            // exists for. So the width is preserved and the *start* gives way: the bar keeps meaning
            // "how much more can I take", and the bright band on its end says "and this part is
            // temporary" — GD §16.2's "never ambiguous", and still in absorption order.
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
