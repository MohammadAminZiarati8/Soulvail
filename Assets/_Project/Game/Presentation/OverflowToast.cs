using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using TMPro;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Hud.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The only thing that ever says a CH §5.2 Overflow level happened: two seconds of
    /// <em>"Overflow ×N"</em>, and no pause.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One toast, two seconds, no pause</b> (M3-10b rule 10). M3-08a rule 7 made Overflow silent
    /// and instant — <em>"no pause, no screen"</em> — and announced only through
    /// <c>OverflowGranted</c>; this is the announcement. GD §13.1's pause exists to let someone
    /// <em>choose</em>, and a card with one button would tax the player for the game having run out
    /// of nodes.
    /// </para>
    /// <para>
    /// <b>It reads the running total rather than the increment</b> (rule 10), which the event carries
    /// for exactly this reason. The fourteenth one at stage 30 means <em>"you have +28 % damage"</em>
    /// and the increment means <em>"you got 2 % again"</em> — the difference between the mechanic
    /// feeling cumulative and feeling like a consolation prize.
    /// </para>
    /// <para>
    /// <b>It is the only place the player learns about half of stage 30's power</b> (rule 11). Ledger
    /// row 1's arithmetic says Overflow supplies ≈ +28 % damage and +28 % max HP by stage 30 against
    /// the +52 % a Husk needs — <em>"Overflow alone supplies more than half of it"</em> — so a player
    /// who never sees it announced has no way to know their character got stronger without picking
    /// anything, which is precisely CH §5.2's <em>"levelling never stops meaning something"</em>
    /// failing quietly.
    /// </para>
    /// <para>
    /// <b>The clock is unscaled, and the split into <see cref="Tick"/> is
    /// <c>FirstActiveHint</c>'s</b> (rule 12). The toast fires while the game is running — Overflow
    /// never pauses — but a level-up or a pause can land on top of it and <c>Time.timeScale</c> goes
    /// to 0 there (M3-08a rule 13), so a toast counting scaled seconds would hang on screen for the
    /// length of the screen above it. Taking the delta as an argument is what makes the dwell
    /// drivable without a real clock, because an EditMode test cannot make unscaled time pass.
    /// </para>
    /// <para>
    /// <b>It takes no touches</b> (rule 12): <c>blocksRaycasts</c> false in every state, like every
    /// other non-interactive overlay in the project, because the stick and the Charge button are
    /// under this canvas and the fight is running.
    /// </para>
    /// <para>
    /// <b>The string is a <c>LocKey</c> plus a number, and as of M3-14a the key resolves</b> (rule
    /// 13, ledger row 9) — which here was always the nearly-harmless case: <em>"Overflow ×14"</em>
    /// is mostly the number, and the number resolved fine all along. <b>The format is therefore
    /// built per <see cref="Construct"/> rather than once statically</b>, because the resolved word
    /// is not known until a localizer exists; it is still built once per component and never per
    /// toast, which is what the cached-format bargain below was for.
    /// </para>
    /// <para>
    /// <b>Injected rather than handed the port on a draw call</b>, which is <c>FirstActiveHint</c>'s
    /// position and the opposite of the four pooled cells' (M3-14a rule 11): this is one component
    /// on one prefab and <c>RunScope</c> injects it.
    /// </para>
    /// </remarks>
    public sealed class OverflowToast : MonoBehaviour
    {
        /// <summary>
        /// What the toast says: <em>"Overflow"</em>, followed by the running total (rule 13).
        /// </summary>
        /// <remarks>
        /// Authored here and written on show rather than left to the prefab, for
        /// <c>FirstActiveHint.HintKey</c>'s reason: the one string this overlay has cannot drift out
        /// of the code that owns it, and the row that checks it is then asserting against something
        /// other than the asset it is reading.
        /// </remarks>
        private static readonly LocKey ToastKey = new LocKey("ui.overflow.granted");

        /// <summary>The format a toast falls back to when nothing injected this component.</summary>
        private const string UnresolvedSuffix = " ×{0:0}";

        /// <summary>
        /// <see cref="ToastKey"/> resolved, with a placeholder for the total, built once per
        /// injection.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Cached so that a toast costs no string per Overflow level: <c>TMP_Text.SetText</c>'s float
        /// overload formats straight into TMP's own backing array, where an interpolated string would
        /// allocate one — <c>HudPresenter.HpFormat</c>'s bargain. <c>{0:0}</c> rather than <c>{0}</c>
        /// for its reason too: TMP reads an unformatted placeholder as "up to nine decimal places",
        /// so the natural spelling would render the fourteenth Overflow as <c>14.0</c>.
        /// </para>
        /// <para>
        /// <b>An instance field as of M3-14a, where it was <c>static readonly</c> before.</b> The
        /// resolved word is not knowable until a localizer exists, so the concatenation moves into
        /// <see cref="Construct"/> — still once per component and never per toast, which is the part
        /// of the bargain that mattered.
        /// </para>
        /// </remarks>
        private string _toastFormat = ToastKey + UnresolvedSuffix;

        /// <summary>The dwell a non-finite or non-positive <see cref="_secondsShown"/> falls back to.</summary>
        private const float DefaultSecondsShown = 2f;

        [Tooltip("The toast, switched between alpha 0 and 1. No fade: the run is ticking the whole " +
                 "time this is up, and a tween here would be a second clock.")]
        [SerializeField] private CanvasGroup _root;

        [Tooltip("The one line: the resolved ui.overflow.granted and the running total beside it.")]
        [SerializeField] private TMP_Text _text;

        [Tooltip("How long the toast stays up, in UNSCALED seconds — see the class remarks for why " +
                 "unscaled. GD §13.1's two seconds.")]
        [SerializeField] private float _secondsShown = DefaultSecondsShown;

        private IDisposable _overflowSubscription;

        /// <summary>Unscaled seconds since the toast went up.</summary>
        private float _elapsed;

        /// <summary>Whether the toast is up. The one read a test needs.</summary>
        public bool IsShown => _root != null && _root.alpha > 0f;

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="localizer">
        /// What turns <see cref="ToastKey"/> into a word. Read once, here, into
        /// <see cref="_toastFormat"/> — see its remarks for why that is not per toast.
        /// </param>
        /// <exception cref="ArgumentNullException">Either dependency is null.</exception>
        /// <remarks>
        /// <b>Two dependencies, and still no <c>IRunSession</c>.</b> Unlike every other readout on this
        /// prefab there is no opening state to draw: a resumed run's Overflow total is in
        /// <c>RunState.OverflowLevels</c> and showing a toast for it on the first frame would be the
        /// game announcing something that happened in a previous session. So there is no
        /// <c>RunStarted</c> handler here and nothing to read the run for.
        /// </remarks>
        [Inject]
        public void Construct(DomainEventHub hub, ILocalizer localizer)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            if (localizer is null)
            {
                throw new ArgumentNullException(nameof(localizer));
            }

            _toastFormat = localizer.Get(ToastKey) + UnresolvedSuffix;

            // Disposed before it is replaced, so a component injected twice — which VContainer does
            // not do and a test does — holds one subscription rather than two.
            _overflowSubscription?.Dispose();
            _overflowSubscription = hub.Subscribe<OverflowGranted>(OnOverflowGranted);
        }

        /// <exception cref="MissingReferenceException">The root or the label is not dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this toast.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>HudPresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// injected" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (_root == null || _text == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(OverflowToast)} is missing its root or its label. This is the only " +
                    "thing in the game that ever says an Overflow level happened, and by stage 30 " +
                    "that is more than half the power the player has gained (ledger row 1).");
            }

            if (_overflowSubscription is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(OverflowToast)} was never injected, so every Overflow level would be " +
                    "granted in complete silence. The component is registered by RunScope — drag " +
                    "this object onto its Overflow Toast field.");
            }

            // Down whatever the prefab was left dressed as, so a toast someone was editing cannot
            // ship over the arena — HudPresenter's argument, for the ninth screen.
            Hide();
        }

        /// <remarks>
        /// Explicit rather than left to the hub's disposal, for <c>HudPresenter.OnDestroy</c>'s
        /// reason: a view destroyed before its scope would otherwise stay in a subscriber list and be
        /// handed an event for a component Unity has killed.
        /// </remarks>
        private void OnDestroy()
        {
            _overflowSubscription?.Dispose();
            _overflowSubscription = null;
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>One frame of the toast being up: the dwell, and nothing else.</summary>
        /// <remarks>
        /// Split out of <see cref="Update"/> and taking its own delta, so the dwell can be driven to
        /// its end without a real clock — <c>FirstActiveHint.Tick</c>'s bargain with a fixture, and
        /// the reason <c>Overflow_ToastSurvivesAPause</c> is writable at all. The delta is
        /// <em>trusted</em> rather than guarded, which is M3-07a's answer followed rather than
        /// re-argued: the engine's clock is the door, and every other tick in the project trusts the
        /// one it is handed. The Inspector field below it is a different matter — see
        /// <see cref="Dwell"/>.
        /// </remarks>
        private void Tick(float unscaledDt)
        {
            if (!IsShown)
            {
                return;
            }

            _elapsed += unscaledDt;

            if (_elapsed >= Dwell)
            {
                Hide();
            }
        }

        /// <summary>
        /// How long the toast is up for, in unscaled seconds, with the Inspector field's mistakes
        /// answered.
        /// </summary>
        /// <remarks>
        /// <b>A non-finite or non-positive dwell falls back to the default rather than being used</b>
        /// — <c>FirstActiveHint.Dwell</c>'s answer, and the one place on this task where a layout's
        /// <em>leave it alone</em> would be wrong. A duration has no authored rect to fall back to: a
        /// NaN never satisfies <c>&gt;=</c>, so the toast would sit over the fight until the run
        /// ended, and a zero or negative dwell would take it down on the frame it went up. A toast
        /// that never hides is worse than one that hides at the default.
        /// </remarks>
        private float Dwell =>
            float.IsFinite(_secondsShown) && _secondsShown > 0f ? _secondsShown : DefaultSecondsShown;

        /// <summary>
        /// A pick was spent on Overflow: say so, with the running total (rules 10, 11).
        /// </summary>
        /// <remarks>
        /// <b>One timer, restarted.</b> Three Overflow levels in a row — a full tree and three banked
        /// picks, which is every level past 13 once M3-12 ships twelve nodes — is one toast that
        /// re-reads the latest total and starts its two seconds again, rather than three toasts
        /// queueing behind each other for six.
        /// </remarks>
        private void OnOverflowGranted(OverflowGranted evt)
        {
            Show(evt.Total);
        }

        private void Show(int total)
        {
            if (_root == null)
            {
                return;
            }

            if (_text != null)
            {
                // English and the number, as of M3-14a. See the class remarks and ledger row 9.
                _text.SetText(_toastFormat, total);
            }

            _elapsed = 0f;

            _root.alpha = 1f;

            // Rule 12, in both states and not just this one: the stick and the Charge button are
            // under this canvas and the fight is running, so the toast must never be in the raycast
            // path — FirstActiveHint.Show's rule, for its reason.
            _root.blocksRaycasts = false;
            _root.interactable = false;
        }

        private void Hide()
        {
            _elapsed = 0f;

            if (_root == null)
            {
                return;
            }

            _root.alpha = 0f;
            _root.blocksRaycasts = false;
            _root.interactable = false;
        }
    }
}
