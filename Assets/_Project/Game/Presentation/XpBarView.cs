using System;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Controls;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Hud.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// GD §16.1's other half of the player's own row: a thin strip along the very top edge that says
    /// how close the next level is. <em>"Fills toward the next level. Full width, 4px, ignorable but
    /// always there."</em>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Four <em>dp</em>, not four pixels</b> (M3-10b rule 1). Every touch target and every bar in
    /// this project has been measured in dp since M1-16, because a reference pixel is a different
    /// physical size on every phone and a strip that vanished on a dense screen would be the one HUD
    /// element nobody noticed was broken. The number is serialized so the owner can disagree with it
    /// on a real device (ledger row 4).
    /// </para>
    /// <para>
    /// <b>Driven by <c>XpChanged</c> and nothing else</b> (rule 2). The event carries the fraction
    /// already settled — M3-01a rule 5 publishes every <c>LeveledUp</c> a grant earned first and
    /// exactly one <c>XpChanged</c> last — so this strip never has to draw a fraction above 1 and
    /// never has to ask the run anything. <c>LeveledUp</c> is not subscribed here at all: the level
    /// <em>number</em> is <c>HudPresenter</c>'s, because it is a fourth rect in the row that class
    /// lays out (rule 3).
    /// </para>
    /// <para>
    /// <b><c>RunStarted</c> is the one exception, for <c>HudPresenter</c>'s reason</b>: a resumed run
    /// comes back part-way through a level and no event describes a frame that has not happened yet.
    /// One narrow read, <see cref="RunState.XpFraction"/>, and the handle to <c>LevelTracker</c> stays
    /// <c>internal</c> (AR §18.2).
    /// </para>
    /// <para>
    /// <b>It declares no <c>Update</c>.</b> Experience arrives in grants rather than continuously —
    /// one <c>XpChanged</c> a kill, not one a frame — so unlike a cooldown fill there is nothing here
    /// to poll. That is the opposite half of <c>SkillButton</c>'s bargain and is why the strip is
    /// event-driven where <c>AutoCastRow</c>'s cells are sampled.
    /// </para>
    /// <para>
    /// <b>A nonsense <see cref="_heightDp"/> leaves the authored layout alone</b>, which is
    /// <c>PausePresenter.Place</c>'s and <c>SkillBarPresenter.Place</c>'s answer rather than
    /// <c>TreeViewPresenter.Layout</c>'s — and the difference is that this strip <em>is</em> authored
    /// on <c>Hud.prefab</c>, at a real height across a real edge, so there is something honest to fall
    /// back to. A tree cell is a runtime clone that starts life on its template's rect, where leaving
    /// it alone would be twenty-seven cells stacked on each other.
    /// </para>
    /// <para>
    /// <b>It hangs under the <c>SafeAreaFitter</c> like everything else on this prefab</b>, so
    /// <em>"the very top edge"</em> is the top edge of the area the player can actually see. Outside
    /// the fitter a landscape cutout would sit <em>over</em> the strip on one of the two rotations
    /// rather than beside it; whether 4 dp is legible beside a notch even so is ledger row 4's, and
    /// it is why the height is a field.
    /// </para>
    /// </remarks>
    public sealed class XpBarView : MonoBehaviour
    {
        /// <summary>GD §16.1's height, and what a nonsense field is <em>not</em> replaced by.</summary>
        /// <remarks>
        /// A constant beside the field rather than a fallback the field falls back to: see the class
        /// remarks. It is here so the row that checks the prefab quotes GD §16.1 rather than a literal
        /// of its own.
        /// </remarks>
        public const float HeightDp = 4f;

        [Tooltip("The strip itself. Its Image must be Filled / Horizontal — fillAmount is the only " +
                 "thing written to it.")]
        [SerializeField] private Image _fill;

        [Tooltip("How tall the strip is, in dp. GD §16.1's 4 — deliberately ignorable, and a field " +
                 "because whether 4 dp is visible at all beside a notch is a device question " +
                 "(ledger row 4).")]
        [SerializeField] private float _heightDp = HeightDp;

        private IRunSession _session;

        private IDisposable _startedSubscription;
        private IDisposable _xpSubscription;

        /// <summary>
        /// The last fraction written, so a grant that moved nothing costs no canvas rebuild. Seeded
        /// outside <c>[0, 1]</c> so the first draw always happens.
        /// </summary>
        private float _shownFraction = -1f;

        /// <summary>How full the strip is drawing, in <c>[0, 1]</c>. The one read a test needs.</summary>
        public float Fraction => _fill == null ? 0f : _fill.fillAmount;

        /// <param name="session">
        /// The run, for the one read a <c>RunStarted</c> has no payload for. Not
        /// <c>IPlayerCommands</c>: a HUD asks the game for nothing — <c>HudPresenter.Construct</c>'s
        /// words.
        /// </param>
        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        /// <remarks>
        /// Subscribed here rather than in <c>OnEnable</c> — <c>HudPresenter</c>'s reason:
        /// <c>RunScope</c> builds its container from its own <c>Awake</c> and Unity orders no two of
        /// those, so an <c>OnEnable</c> subscription reaches for a hub that may not exist yet.
        /// </remarks>
        [Inject]
        public void Construct(IRunSession session, DomainEventHub hub)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));

            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            // Disposed before they are replaced, so a component injected twice — which VContainer
            // does not do and a test does — holds one subscription each rather than two.
            _startedSubscription?.Dispose();
            _startedSubscription = hub.Subscribe<RunStarted>(OnRunStarted);

            _xpSubscription?.Dispose();
            _xpSubscription = hub.Subscribe<XpChanged>(OnXpChanged);
        }

        /// <exception cref="MissingReferenceException">The strip is not dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this view.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>HudPresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// injected" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (_fill == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(XpBarView)} has no fill image assigned. Drag the strip's Fill object " +
                    "onto its Fill field — without it the one thing in the game that says how close " +
                    "the next level is says nothing, and GD §16.1's strip is exactly the element " +
                    "nobody would notice had stopped moving.");
            }

            if (_session is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(XpBarView)} was never injected, so the strip would sit at whatever " +
                    "the prefab was left dressed as for the whole run. The component is registered " +
                    "by RunScope — drag this object onto its Xp Bar field.");
            }

            Place();

            // Drawn once immediately, because whether RunStarted has already been published depends
            // on the order VContainer's entry points and this component's Start happen to run in.
            // Either path lands here; a strip in a scene with no run at all simply has nothing to
            // draw yet, and says so by staying at whatever the prefab shows — HudPresenter's
            // argument, for the seventh screen.
            Redraw();
        }

        /// <remarks>
        /// Explicit rather than left to the hub's disposal, for <c>HudPresenter.OnDestroy</c>'s
        /// reason: a view destroyed before its scope — a scene reload, an arena opened without a run
        /// — would otherwise stay in two subscriber lists and be handed events for a component Unity
        /// has killed.
        /// </remarks>
        private void OnDestroy()
        {
            _startedSubscription?.Dispose();
            _startedSubscription = null;

            _xpSubscription?.Dispose();
            _xpSubscription = null;
        }

        /// <remarks>
        /// The opening state, which no event can describe: a resumed run comes back part-way through
        /// a level (M3-01b) and a fresh one comes back at the very start of one.
        /// </remarks>
        private void OnRunStarted(RunStarted evt)
        {
            Redraw();
        }

        /// <summary>Rule 2: experience moved, and the event already carries where it landed.</summary>
        private void OnXpChanged(XpChanged evt)
        {
            Draw(evt.Fraction);
        }

        /// <remarks>
        /// A null state is a strip in a scene where no run has begun, which is a workflow rather than
        /// a fault — pressing Play with the Run scene open reaches this a moment before the session
        /// starts.
        /// </remarks>
        private void Redraw()
        {
            RunState state = _session?.State;

            if (state is null)
            {
                return;
            }

            Draw(state.XpFraction);
        }

        /// <summary>Writes <paramref name="fraction"/> to the strip, if it changed.</summary>
        /// <remarks>
        /// <para>
        /// <b>Clamped rather than trusted</b>, for <c>SkillButton</c>'s reason: core guarantees
        /// <c>[0, 1)</c> and M3-01a rule 5's ordering is what makes that true, but a
        /// <c>fillAmount</c> outside the range is the kind of thing uGUI renders as an empty or a
        /// full strip rather than as an error — so the one place that could ever show it is the one
        /// place that cannot report it.
        /// </para>
        /// <para>
        /// <b>Only a changed value is written.</b> Assigning <c>fillAmount</c> marks the graphic
        /// dirty and queues a canvas rebuild, and a kill that lands on a level boundary publishes
        /// several events in one frame — <c>ManualSkillButton.Draw</c>'s argument, arriving here
        /// through a different door.
        /// </para>
        /// </remarks>
        private void Draw(float fraction)
        {
            if (_fill == null)
            {
                return;
            }

            float clamped = Mathf.Clamp01(fraction);

            if (Mathf.Approximately(clamped, _shownFraction))
            {
                return;
            }

            _shownFraction = clamped;
            _fill.fillAmount = clamped;
        }

        /// <summary>
        /// Rule 1: <see cref="_heightDp"/> tall in dp, stretched across the parent's top edge.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Here rather than in the prefab for <c>HudPresenter.Place</c>'s first reason: a
        /// Scale-With-Screen-Size canvas measures in reference pixels, so a strip authored at four of
        /// those is a different physical height on every phone — and 4 dp is GD §16.1's number
        /// because that is how thin a thing can be and still be seen, which does not scale with the
        /// display.
        /// </para>
        /// <para>
        /// The width is left to the anchors rather than computed: <c>anchorMin.x</c> 0 against
        /// <c>anchorMax.x</c> 1 with a zero <c>sizeDelta.x</c> is <em>exactly the parent's width</em>
        /// however wide the safe area turns out to be, where arithmetic would be this file's second
        /// copy of what <c>SafeAreaFitter</c> already knows.
        /// </para>
        /// <para>
        /// <b>A nonsense height leaves the authored layout alone</b> — see the class remarks. Zero is
        /// refused with the rest, and that is the difference from <c>SkillBarPresenter</c>'s margin:
        /// a margin of 0 is a control flush to the safe area's edge, which is a layout the owner is
        /// entitled to ask for, while a strip 0 dp tall is GD §16.1's one always-there element
        /// silently absent.
        /// </para>
        /// </remarks>
        private void Place()
        {
            if (transform is not RectTransform rect || !IsUsableHeight(_heightDp))
            {
                return;
            }

            float pxPerDp = PixelsPerDp();

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(0f, _heightDp * pxPerDp);
            rect.anchoredPosition = Vector2.zero;
        }

        private static bool IsUsableHeight(float dp) => float.IsFinite(dp) && dp > 0f;

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
