using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and Hud.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Controls
{
    /// <summary>
    /// The one-thumb control the whole game rests on: a joystick fixed at one place on the screen,
    /// read from that centre wherever in the left region the thumb lands. CC §2.1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The owner's ruling of 2026-10-13 replaced the floating stick (CC §2.1–2.3) with a fixed one.
    /// The floating behaviour — origin planted where the thumb landed, recentred behind it — is
    /// kept behind <c>_floating</c> rather than deleted. The class keeps its name for the same
    /// reason: <c>Hud.prefab</c> and its tests refer to it.
    /// </para>
    /// <para>
    /// Every number lives in <see cref="StickShaper"/>; this class owns only what needs a frame
    /// and a pointer — which finger has the stick, where its origin currently is, and where the
    /// two sprites go. That split is what makes the feel testable at all.
    /// </para>
    /// <para>
    /// It feeds the Input System and never reads it. Deflection is written to
    /// <c>&lt;Gamepad&gt;/leftStick</c> on a virtual gamepad the base class creates, which the
    /// <c>Move</c> action already binds and <c>InputAdapter</c> already reads (M0-14) — the two
    /// halves are wired through the actions asset and know nothing about each other. This is also
    /// why M0-14's <c>Touch</c> control scheme lists <c>Gamepad</c>: on a phone, this *is* the
    /// gamepad.
    /// </para>
    /// <para>
    /// Single-capture, not single-touch. One finger owns the stick and other pointers are ignored
    /// *by this component*; buttons elsewhere on the HUD are separate raycast targets and keep
    /// working, which is the multi-touch requirement in CC §2.1.
    /// </para>
    /// </remarks>
    public sealed class FloatingStick : OnScreenControl,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>
        /// No real pointer can carry this id — touches count up from 0 and the mouse is −1 — so it
        /// is unambiguous as "nothing has the stick".
        /// </summary>
        private const int NoPointer = int.MinValue;

        /// <summary>The ring's diameter. Twice <see cref="StickShaper.MaxRadiusDp"/>, not by coincidence.</summary>
        private const float BaseDiameterDp = 2f * StickShaper.MaxRadiusDp;

        /// <summary>The knob's diameter. CC §2.1.</summary>
        private const float KnobDiameterDp = 52f;

        [InputControl(layout = "Vector2")]
        [SerializeField] private string _controlPath = "<Gamepad>/leftStick";

        [Tooltip("The virtual joystick region — the left 45% of the screen. This component sits on it.")]
        [SerializeField] private RectTransform _region;

        [Tooltip("Off: the stick is fixed at Fixed Offset Dp and always visible (CC §2.1). On: the old floating stick, planted under the thumb and recentred behind it.")]
        [SerializeField] private bool _floating;

        [Tooltip("The fixed stick's centre in dp from the region's bottom-left, which is the safe area's. Tune on device.")]
        [SerializeField] private Vector2 _fixedOffsetDp = new Vector2(110f, 110f);

        [Tooltip("The 120 dp ring that appears under the thumb, centred on the stick's origin.")]
        [SerializeField] private RectTransform _base;

        [Tooltip("The 52 dp disc that tracks the thumb, clamped to the full-speed threshold.")]
        [SerializeField] private RectTransform _knob;

        private Canvas _canvas;
        private int _pointerId = NoPointer;

        /// <summary>The stick's origin in screen pixels — where the thumb landed, then recentred.</summary>
        private Vector2 _originPx;

        private Vector2 _reported;

        private readonly Vector3[] _corners = new Vector3[4];

        /// <inheritdoc/>
        protected override string controlPathInternal
        {
            get => _controlPath;
            set => _controlPath = value;
        }

        private void Awake()
        {
            // Cached once: the render mode decides whether screen-point conversion needs a camera,
            // and asking per drag frame would be a GetComponent in the hot path.
            _canvas = _region != null ? _region.GetComponentInParent<Canvas>() : null;

            float pxPerDp = StickShaper.PixelsPerDp(Screen.dpi);
            Resize(_base, BaseDiameterDp, pxPerDp);
            Resize(_knob, KnobDiameterDp, pxPerDp);

            EnsureDressedAspect();
            Rest();
        }

        // The region's layout is not final in Awake (safe area, canvas scaler), and it changes on
        // rotation, so a resting fixed stick follows its region rather than trusting one placement.
        private void LateUpdate()
        {
            if (!_floating && _pointerId == NoPointer)
            {
                Vector2 before = _originPx;
                Rest();
                if (before != _originPx)
                {
                    float pxPerDp = StickShaper.PixelsPerDp(Screen.dpi);
                    Resize(_base, BaseDiameterDp, pxPerDp);
                    Resize(_knob, KnobDiameterDp, pxPerDp);
                }
            }
        }

        /// <summary>
        /// The idle look: nothing in floating mode, base and knob at the fixed centre otherwise.
        /// </summary>
        private void Rest()
        {
            if (_floating)
            {
                Show(false);
                return;
            }

            _originPx = FixedCentrePx();
            Show(true);
            PlaceAt(_base, _originPx);
            PlaceAt(_knob, _originPx);
        }

        /// <summary>
        /// The fixed centre in screen pixels: the region's bottom-left corner, which is the safe
        /// area's, plus the offset in dp — so it holds in both orientations and at any density.
        /// </summary>
        private Vector2 FixedCentrePx()
        {
            if (_region == null)
            {
                return Vector2.zero;
            }

            _region.GetWorldCorners(_corners);
            Vector2 corner = RectTransformUtility.WorldToScreenPoint(EventCamera, _corners[0]);
            return corner + _fixedOffsetDp * StickShaper.PixelsPerDp(Screen.dpi);
        }

        private void EnsureDressedAspect()
        {
            if (_base != null && _base.TryGetComponent(out Image baseImage))
            {
                baseImage.preserveAspect = true;
            }

            if (_knob != null && _knob.TryGetComponent(out Image knobImage))
            {
                knobImage.preserveAspect = true;
            }
        }

        /// <summary>
        /// Claims the stick for this finger and plants the origin where it landed. The first
        /// report is deliberately zero: touching down is not yet movement.
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (_pointerId != NoPointer)
            {
                // Another finger already owns the stick. Ignoring the second one is what keeps a
                // stray palm from teleporting the origin mid-fight.
                return;
            }

            _pointerId = eventData.pointerId;
            _originPx = _floating ? eventData.position : FixedCentrePx();

            // Sized here rather than in the prefab because a Scale-With-Screen-Size canvas measures
            // in reference pixels, not dp, and the ring's radius is load-bearing: at 120 dp across
            // its edge sits exactly at MaxRadiusDp, so it shows the player where recentring starts.
            float pxPerDp = StickShaper.PixelsPerDp(Screen.dpi);
            Resize(_base, BaseDiameterDp, pxPerDp);
            Resize(_knob, KnobDiameterDp, pxPerDp);

            Show(true);
            PlaceAt(_base, _originPx);
            PlaceAt(_knob, _originPx);

            if (_floating)
            {
                Report(Vector2.zero);
                return;
            }

            // A fixed stick is read from its centre, so touching down away from it is already a
            // deflection; there is no origin the touch just created to measure from.
            Drive(eventData.position, pxPerDp, StickShaper.MaxRadiusDp);
        }

        /// <summary>
        /// The frame loop of the stick: pull the origin along if the thumb has outrun it, report
        /// the shaped deflection, and move the two sprites to match.
        /// </summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId)
            {
                return;
            }

            float pxPerDp = StickShaper.PixelsPerDp(Screen.dpi);

            if (!_floating)
            {
                // No recentring: past the rim the knob clamps and the stick keeps reading full.
                Drive(eventData.position, pxPerDp, StickShaper.MaxRadiusDp);
                return;
            }

            Vector2 touchPx = eventData.position;

            // Recentring is done in dp and written back in pixels, so the 60 dp radius is the same
            // physical distance on a 320 dpi phone as on a 440 dpi one.
            _originPx = StickShaper.Recenter(_originPx / pxPerDp, touchPx / pxPerDp) * pxPerDp;

            PlaceAt(_base, _originPx);

            // The knob stops at the full-speed threshold rather than at the ring's edge: it shows
            // how much speed the player is asking for, and past 40 dp there is no more to ask for.
            Drive(touchPx, pxPerDp, StickShaper.FullSpeedDp);
        }

        /// <summary>Writes the deflection to the virtual gamepad, remembering it so a test can read what was sent.</summary>
        private void Report(Vector2 value)
        {
            _reported = value;
            SendValueToControl(value);
        }

        /// <summary>Reports the deflection of a touch from the origin and moves the knob, clamped to a radius.</summary>
        private void Drive(Vector2 touchPx, float pxPerDp, float knobRadiusDp)
        {
            Vector2 deltaPx = touchPx - _originPx;
            Report(StickShaper.Shape(deltaPx / pxPerDp));
            PlaceAt(_knob, _originPx + Vector2.ClampMagnitude(deltaPx, knobRadiusDp * pxPerDp));
        }

        /// <summary>Releases the stick when the finger that owns it lifts. Other ups are not ours.</summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId)
            {
                return;
            }

            Release();
        }

        /// <summary>
        /// Drops the stick when the HUD goes away mid-drag, before the base class tears the
        /// virtual device down.
        /// </summary>
        /// <remarks>
        /// Order matters: <c>base.OnDisable</c> nulls the control, after which
        /// <c>SendValueToControl</c> is a silent no-op. Releasing first is what guarantees the last
        /// value the device ever sees is zero — otherwise a stick disabled at full deflection can
        /// leave the player walking into a wall through the whole of the next scene.
        /// </remarks>
        protected override void OnDisable()
        {
            Release();
            base.OnDisable();
        }

        /// <summary>
        /// Sends zero, returns the visuals to rest (hidden when floating) and gives up the capture. Idempotent, so a pointer-up
        /// followed by a disable in the same frame costs one event, not two.
        /// </summary>
        private void Release()
        {
            if (_pointerId == NoPointer)
            {
                return;
            }

            _pointerId = NoPointer;
            Report(Vector2.zero);
            Rest();
        }

        private void Show(bool visible)
        {
            if (_base != null)
            {
                _base.gameObject.SetActive(visible);
            }

            if (_knob != null)
            {
                _knob.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// Moves a sprite to a screen position. Correct only while the target is a child of
        /// <see cref="_region"/> anchored to its centre, which is how Hud.prefab is built:
        /// <c>anchoredPosition</c> is then measured from the same origin the conversion returns.
        /// </summary>
        private void PlaceAt(RectTransform target, Vector2 screenPx)
        {
            if (target == null || _region == null)
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_region, screenPx, EventCamera, out Vector2 local))
            {
                target.anchoredPosition = local;
            }
        }

        /// <summary>
        /// Sets a square sprite's size from a measurement in dp, undoing the canvas scale so the
        /// result is the requested physical size rather than that many reference pixels.
        /// </summary>
        private void Resize(RectTransform target, float diameterDp, float pxPerDp)
        {
            if (target == null)
            {
                return;
            }

            float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            float side = diameterDp * pxPerDp / scale;
            target.sizeDelta = new Vector2(side, side);
        }

        /// <summary>
        /// The camera screen points are relative to: none for an overlay canvas, which is what
        /// Hud.prefab uses, and the canvas's own camera for anything else.
        /// </summary>
        private Camera EventCamera =>
            _canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : _canvas.worldCamera;
    }
}
