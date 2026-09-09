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

        [Tooltip("The fill's normal colour. Cyan is the player, everywhere in the game (GD §16.4).")]
        [SerializeField] private Color _fillColour = new Color(0.133f, 0.827f, 0.933f, 1f);

        [Tooltip("What the fill flashes to when a hit is turned away. A brightened cyan rather " +
                 "than a new hue: the bar is already the player's colour, so the only thing left " +
                 "to say 'that one did not land' with is brightness. Red-orange is reserved for " +
                 "danger and may never be used here (GD §16.4).")]
        [SerializeField] private Color _blockedColour = new Color(0.78f, 0.98f, 1f, 1f);

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

            Color colour = _blockedRemaining > 0f ? _blockedColour : _fillColour;

            if (_fill.color != colour)
            {
                _fill.color = colour;
            }

            if (!Mathf.Approximately(_ghost.fillAmount, _ghostShown))
            {
                _ghost.fillAmount = _ghostShown;
            }
        }
    }
}
