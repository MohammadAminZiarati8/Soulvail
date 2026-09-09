using System;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and Hud.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Controls
{
    /// <summary>
    /// The Charge button: 72 dp under the right thumb, with a radial fill that says when it is
    /// live. CC §5 and §6.2 — the one button a player who never opens a menu ever needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It has no path into gameplay at all.</b> The press is carried by an
    /// <c>OnScreenButton</c> sitting on the same object, which writes
    /// <c>&lt;Gamepad&gt;/buttonSouth</c> on a virtual gamepad; the <c>MovementSkill</c> action
    /// already binds that, <c>InputAdapter</c> already reads it and <c>RunTicker</c> already turns
    /// it into a command. This class draws the cooldown and nothing else. It is the same split
    /// <see cref="FloatingStick"/> makes — the control feeds the Input System and never reads it —
    /// and it is what lets Space in the Editor and a thumb on a phone be the same press.
    /// </para>
    /// <para>
    /// <b>It stays tappable while it is cooling</b>, which is the one place CC §6.2's generic
    /// "unavailable: 40 % opacity, no tap response" is deliberately not followed. The movement
    /// skill has a 0.15 s input buffer (CC §5) and the buffer only exists if the early press
    /// actually reaches core: a button that swallowed it would make the buffer unreachable through
    /// the very control it was written for. So the press always goes through, and core decides —
    /// <c>ChargeSkill</c> keeps a press that is merely early and drops one that has gone stale.
    /// The dimming is the whole of the feedback, and it is honest: nothing visible happens for a
    /// tap made too soon.
    /// </para>
    /// <para>
    /// <b>It reads one number per frame and holds none.</b> The fill comes from
    /// <see cref="RunState.MovementSkillCooldownFraction"/>, sampled in <c>Update</c> rather than
    /// driven by an event, because a fill slides continuously for two and a half seconds — an event
    /// carrying it would be an event per frame. Everything about <em>when</em> a dash may happen
    /// stays in core; this cannot make the button lie about it, because it owns no clock to be
    /// wrong with.
    /// </para>
    /// </remarks>
    public sealed class SkillButton : MonoBehaviour
    {
        /// <summary>Opacity while the cooldown is running. CC §6.2.</summary>
        private const float CoolingAlpha = 0.4f;

        [Tooltip("The radial sweep drawn over the button. Its Image must be Filled / Radial360 — " +
                 "fillAmount is the only thing written to it.")]
        [SerializeField] private Image _radialFill;

        [Tooltip("The whole button's opacity: 100 % when the Charge is live, 40 % while it is " +
                 "cooling. Raycasts are deliberately left on either way — see the class remarks.")]
        [SerializeField] private CanvasGroup _group;

        [Tooltip("The button's diameter in dp. 72 is CC §6.2's movement-skill size; the four skill " +
                 "slots of M3-10 are 60.")]
        [Min(1f)]
        [SerializeField] private float _sizeDp = 72f;

        [Tooltip("Where the button's centre sits, in dp from the safe area's right edge and from " +
                 "its bottom. CC §6.2's natural thumb rest — tune on device.")]
        [SerializeField] private Vector2 _marginDp = new Vector2(96f, 96f);

        private IRunSession _session;

        /// <summary>
        /// The last fraction written, so a frame that changed nothing costs no canvas rebuild.
        /// Seeded outside <c>[0, 1]</c> so the first frame always draws.
        /// </summary>
        private float _shownFraction = -1f;

        /// <param name="session">
        /// The run, read for one number a frame. Not <c>IPlayerCommands</c>: this button sends
        /// nothing — the press goes through the Input System, as the class remarks explain.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
        [Inject]
        public void Construct(IRunSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <exception cref="MissingReferenceException">The fill or the group is not dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this button.</exception>
        /// <remarks>
        /// Checked in <c>Start</c> rather than <c>Awake</c> for the reason every other injected view
        /// gives: <c>RunScope</c> builds its container from its own <c>Awake</c>, and Unity gives no
        /// order between two of those.
        /// </remarks>
        private void Start()
        {
            if (_radialFill == null || _group == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(SkillButton)} is missing its fill or its canvas group. Drag the Fill " +
                    "child onto Radial Fill and this object's CanvasGroup onto Group — without " +
                    "them the Charge is a button with no cooldown on it, which is worse than no " +
                    "button at all.");
            }

            if (_session is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(SkillButton)} was never injected, so its cooldown would never move. " +
                    "The component is registered by RunScope — drag this object onto its Skill " +
                    "Button field.");
            }

            Place();

            // Drawn once immediately, so a button that shows nothing is unambiguous evidence that
            // the run never started rather than a first frame that has not come round yet.
            Draw(Fraction());
        }

        /// <summary>
        /// Sizes and positions the button in dp: <see cref="_sizeDp"/> across, its centre
        /// <see cref="_marginDp"/> in from the safe area's bottom-right corner.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Done here rather than in the prefab for the reason <see cref="FloatingStick"/> resizes
        /// its ring at runtime: a Scale-With-Screen-Size canvas measures in reference pixels, and 72
        /// reference pixels is a different physical size on every phone. A touch target is the one
        /// kind of UI where that difference is not cosmetic — CC §6.2's number is 72 dp because
        /// that is how big a thumb is, and a thumb does not scale with the display.
        /// </para>
        /// <para>
        /// Anchored to the parent's bottom-right so it follows the safe area rather than the screen:
        /// <c>SafeAreaFitter</c> insets that rect on a notched device, and a button pinned to the
        /// screen instead would end up under the gesture bar on exactly the phones that have one.
        /// </para>
        /// </remarks>
        private void Place()
        {
            var rect = transform as RectTransform;

            if (rect == null)
            {
                return;
            }

            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            // The canvas scale is divided out for the reason FloatingStick divides it out: the
            // result has to be the requested physical size, not that many reference pixels.
            float pxPerDp = StickShaper.PixelsPerDp(Screen.dpi) / scale;
            float side = _sizeDp * pxPerDp;

            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(side, side);
            rect.anchoredPosition = new Vector2(-_marginDp.x * pxPerDp, _marginDp.y * pxPerDp);
        }

        /// <remarks>
        /// <c>Update</c>, not <c>LateUpdate</c>: <c>RunTicker</c> is an <c>ITickable</c> and so runs
        /// in the Update phase, and a button one frame behind the cooldown it draws is exactly as
        /// wrong at the moment that matters — the instant it becomes live — as one that is right the
        /// rest of the time is useful. The worst case is a single frame of lag against a fill that
        /// takes two and a half seconds, which no eye resolves; the check is cheap enough that
        /// paying it in the same phase is not worth arguing about.
        /// </remarks>
        private void Update()
        {
            Draw(Fraction());
        }

        /// <summary>
        /// How much cooldown is left, or zero when there is no run to ask.
        /// </summary>
        /// <remarks>
        /// A button drawn before the first <c>Start</c> — the frame the Run scene loads, or a HUD
        /// left in a scene with no run in it — reads as live rather than as permanently cooling.
        /// That is the honest of the two: there is no cooldown running, because there is nothing to
        /// dash with.
        /// </remarks>
        private float Fraction()
        {
            RunState state = _session.State;

            return state is null ? 0f : state.MovementSkillCooldownFraction;
        }

        /// <summary>
        /// Rule 5: the fill is <c>1 − fraction</c> — empty the instant a dash starts, full when the
        /// button is live again — and the whole button dims while it is cooling.
        /// </summary>
        /// <remarks>
        /// The fraction is clamped rather than trusted. Core guarantees <c>[0, 1]</c> and there is
        /// no path by which it would not, but a <c>fillAmount</c> outside the range is the kind of
        /// thing uGUI renders as an empty or a full ring rather than as an error — and a Charge
        /// button that reads "ready" while it is not is the most misleading thing on the screen.
        /// </remarks>
        private void Draw(float fraction)
        {
            float clamped = Mathf.Clamp01(fraction);

            // Not merely an optimisation: assigning fillAmount marks the graphic dirty and queues a
            // canvas rebuild, so writing an unchanged value would rebuild the HUD every frame of
            // the two and a half seconds in every three that nothing is happening.
            if (Mathf.Approximately(clamped, _shownFraction))
            {
                return;
            }

            _shownFraction = clamped;

            _radialFill.fillAmount = 1f - clamped;

            // Alpha only. blocksRaycasts and interactable are deliberately untouched, so an early
            // tap still reaches core and the input buffer keeps working — see the class remarks.
            _group.alpha = clamped > 0f ? CoolingAlpha : 1f;
        }
    }
}
