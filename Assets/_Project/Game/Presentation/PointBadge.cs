using System;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Hud.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The unspent skill points, as a green count on the level star, and the "LEVEL UP!" toast that
    /// says a level happened without stopping the run (UI-03b).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A dumb view: it reads core and sends nothing.</b> Spending the points is UI-03c's, and the
    /// star stays a picture until then. It is handed the run's <see cref="RunState"/> and the hub by
    /// <c>HudPresenter</c> — <see cref="Bind"/> — rather than injected, because the presenter is the
    /// one thing on this prefab that already knows when a run starts and which state it is.
    /// </para>
    /// <para>
    /// <b>The events say <em>when</em>; the number is read.</b> <c>NodeTaken</c> is published from
    /// inside <c>SkillTree.Take</c>, <em>before</em> <c>LevelUpFlow.Take</c> spends the point, so a
    /// handler that read <see cref="RunState.SkillPoints"/> there would draw one too many for good.
    /// The three events therefore only mark the badge stale, and <see cref="LateUpdate"/> writes it
    /// once, after the call that raised them has returned. That is still event-driven — nothing is
    /// polled into agreement, an unchanged number is not written, and a frame with no event does one
    /// bool test — and it coalesces a grant worth two levels into one write.
    /// </para>
    /// <para>
    /// <b>It lives on a host that is never hidden</b>, and shows and hides its own children, because
    /// a deactivated object receives no <c>Update</c> and the toast's clock is one.
    /// </para>
    /// <para>
    /// <b>The toast's clock is unscaled and the split into <see cref="Tick"/> is
    /// <c>OverflowToast</c>'s</b>: a pause lands on top of a level-up often, and a toast counting
    /// scaled seconds would hang on screen for as long as the screen above it. It blocks no touch —
    /// the stick is under it and the fight is running.
    /// </para>
    /// <para>
    /// <b>Off in a mode that draws its level-ups</b> (rule 5): the old offer screen owns that flow,
    /// and a count of points nobody can spend would be a lie. <see cref="RunState.IsFreePick"/> is
    /// asked, so a run with no tree is off for the same reason.
    /// </para>
    /// </remarks>
    public sealed class PointBadge : MonoBehaviour
    {
        /// <summary>The count, on its own. <c>HudPresenter.LevelFormat</c>'s spelling and reason.</summary>
        private const string CountFormat = "{0:0}";

        /// <summary>The dwell a non-finite or non-positive <see cref="_toastSeconds"/> falls back to.</summary>
        private const float DefaultToastSeconds = 1.5f;

        [Tooltip("The green disc and its count, shown while any point is banked. Switched on and off " +
                 "rather than this component, which has to keep ticking the toast.")]
        [SerializeField] private RectTransform _badge;

        [Tooltip("The disc's image. Tinted Palette.Available.")]
        [SerializeField] private Image _disc;

        [Tooltip("The count inside the disc.")]
        [SerializeField] private TMP_Text _count;

        [Tooltip("The toast, switched between alpha 0 and 1. No fade: the run is ticking the whole " +
                 "time this is up, and a tween here would be a second clock.")]
        [SerializeField] private CanvasGroup _toast;

        [Tooltip("The toast's one line.")]
        [SerializeField] private TMP_Text _toastText;

        [Tooltip("How long the toast stays up, in UNSCALED seconds.")]
        [SerializeField] private float _toastSeconds = DefaultToastSeconds;

        private RunState _state;
        private DomainEventHub _hub;

        private IDisposable _leveledSubscription;
        private IDisposable _takenSubscription;
        private IDisposable _overflowSubscription;
        private IDisposable _endedSubscription;

        /// <summary>The number drawn; 0 means the badge is hidden.</summary>
        private int _shown;

        /// <summary>Whether an event has made the number stale since it was last written.</summary>
        private bool _stale;

        /// <summary>Unscaled seconds since the toast went up.</summary>
        private float _elapsed;

        /// <summary>How many times the count's text has been written. The one read a test needs.</summary>
        private int _writes;

        /// <summary>The number drawn on the badge; 0 means hidden.</summary>
        public int Shown => _shown;

        /// <summary>Whether the "LEVEL UP!" toast is up.</summary>
        public bool ToastVisible => _toast != null && _toast.alpha > 0f;

        /// <summary>The disc, for <c>HudPresenter</c> to place on the star's corner.</summary>
        public RectTransform Badge => _badge;

        /// <summary>The count, for <c>HudPresenter</c> to size in dp.</summary>
        public TMP_Text Count => _count;

        /// <summary>The toast's line, for <c>HudPresenter</c> to size in dp.</summary>
        public TMP_Text ToastLabel => _toastText;

        /// <summary>The toast, for <c>HudPresenter</c> to place above the arena's centre.</summary>
        public RectTransform Toast => _toast == null ? null : _toast.transform as RectTransform;

        /// <summary>
        /// Points the badge at a run and draws what that run is holding now. Called again for each run,
        /// and on <c>Start</c>, by <c>HudPresenter</c>; the subscriptions are made once per hub.
        /// </summary>
        /// <param name="state">The run, or null for none — which draws nothing.</param>
        /// <param name="hub">The run's event hub.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hub"/> is null.</exception>
        /// <remarks>
        /// <b>Drawn immediately, not on the next <c>LateUpdate</c></b>: a resumed run restores its banked
        /// points silently (rule 6), so the badge must read them before the first tick.
        /// </remarks>
        public void Bind(RunState state, DomainEventHub hub)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            if (!ReferenceEquals(hub, _hub))
            {
                Unsubscribe();

                _hub = hub;
                _leveledSubscription = hub.Subscribe<LeveledUp>(OnLeveledUp);
                _takenSubscription = hub.Subscribe<NodeTaken>(OnNodeTaken);
                _overflowSubscription = hub.Subscribe<OverflowGranted>(OnOverflowGranted);
                _endedSubscription = hub.Subscribe<RunEnded>(OnRunEnded);
            }

            _state = state;

            if (_disc != null)
            {
                _disc.color = Palette.Available;
            }

            if (_count != null)
            {
                _count.color = Palette.HudText;
            }

            if (_toastText != null)
            {
                _toastText.color = Palette.Available;
            }

            HideToast();
            Refresh();
        }

        /// <summary>The toast's word, written once by <c>HudPresenter</c> from the localiser.</summary>
        public void SetToastText(string text)
        {
            if (_toastText != null)
            {
                _toastText.text = text;
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            if (_stale)
            {
                Refresh();
            }
        }

        /// <summary>One frame of the toast being up: the dwell, and nothing else.</summary>
        /// <remarks>
        /// Taking its own delta so a fixture can drive it to its end without a real clock —
        /// <c>OverflowToast.Tick</c>'s bargain. The delta is trusted; the Inspector's dwell is not.
        /// </remarks>
        private void Tick(float unscaledDt)
        {
            if (!ToastVisible)
            {
                return;
            }

            _elapsed += unscaledDt;

            if (_elapsed >= Dwell)
            {
                HideToast();
            }
        }

        /// <summary>
        /// A non-finite or non-positive dwell falls back to the default: a duration has no authored
        /// rect to leave alone, and a NaN would never satisfy <c>&gt;=</c>.
        /// </summary>
        private float Dwell =>
            float.IsFinite(_toastSeconds) && _toastSeconds > 0f ? _toastSeconds : DefaultToastSeconds;

        private bool IsFree => _state is not null && _state.IsFreePick;

        private void OnLeveledUp(LeveledUp evt)
        {
            if (!IsFree)
            {
                return;
            }

            _stale = true;

            ShowToast();
        }

        private void OnNodeTaken(NodeTaken evt)
        {
            _stale |= IsFree;
        }

        private void OnOverflowGranted(OverflowGranted evt)
        {
            _stale |= IsFree;
        }

        /// <summary>Rule 6: the run is over, so both clear.</summary>
        private void OnRunEnded(RunEnded evt)
        {
            _state = null;

            HideToast();
            Refresh();
        }

        /// <summary>Writes the run's points, if they are not what is drawn already (rule 4).</summary>
        private void Refresh()
        {
            _stale = false;

            int points = IsFree ? _state.SkillPoints : 0;

            if (points == _shown)
            {
                return;
            }

            _shown = points;

            if (_count != null)
            {
                _writes++;

                if (points > 0)
                {
                    _count.SetText(CountFormat, points);
                }
            }

            if (_badge != null && _badge.gameObject.activeSelf != points > 0)
            {
                _badge.gameObject.SetActive(points > 0);
            }
        }

        /// <summary>One timer, restarted: a second level re-reads nothing and starts the dwell again.</summary>
        private void ShowToast()
        {
            if (_toast == null)
            {
                return;
            }

            _elapsed = 0f;

            _toast.alpha = 1f;

            // In both states, FirstActiveHint.Show's rule: the stick is under this canvas.
            _toast.blocksRaycasts = false;
            _toast.interactable = false;
        }

        private void HideToast()
        {
            _elapsed = 0f;

            if (_toast == null)
            {
                return;
            }

            _toast.alpha = 0f;
            _toast.blocksRaycasts = false;
            _toast.interactable = false;
        }

        private void Unsubscribe()
        {
            _leveledSubscription?.Dispose();
            _takenSubscription?.Dispose();
            _overflowSubscription?.Dispose();
            _endedSubscription?.Dispose();

            _leveledSubscription = null;
            _takenSubscription = null;
            _overflowSubscription = null;
            _endedSubscription = null;
            _hub = null;
        }
    }
}
