using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Controls;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Hud.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// CC §6.2's four thumb positions as a cluster beside the Charge: which of S1–S4 exist, where
    /// they sit, and what each one is showing. GD §5.2, §16.1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An empty slot is not drawn at all</b> (M3-10a rule 5). CC §6.2 literally: <em>"unused slots
    /// are not drawn — a player with zero manual skills sees exactly one button, and one with two
    /// sees three."</em> The object is deactivated rather than made transparent, so it takes no
    /// touches and costs no canvas rebuild — and that is the state of all four in every run in this
    /// build, because nothing in <c>Data/Trees</c> ships an Active until M3-12.
    /// </para>
    /// <para>
    /// <b>It redraws from <c>SkillAutoCastChanged</c> and from <c>RunStarted</c>, and from nothing
    /// else</b> (rule 6). The event carries the id, the state and the slot (M3-07a rule 9) and is the
    /// only thing that can change which buttons exist; <c>RunStarted</c> is there for
    /// <c>HudPresenter</c>'s reason — a resumed run comes back with slots already filled (M3-07b) and
    /// there is no event describing a frame that has not happened yet. The <em>cooldowns</em> are not
    /// redrawn here at all: each button polls its own two numbers, because a fill slides continuously
    /// and an event carrying it would be an event per frame.
    /// </para>
    /// <para>
    /// <b>Nothing here knows about Auto</b> (rule 11). This class never asks
    /// <c>RunState.IsAutoCast</c>: an auto-cast skill holds no slot, so it produces no button by
    /// construction. GD §16.1's <em>"auto-cast skills need visible cooldowns"</em> is a different
    /// readout in a different corner and it is M3-10b's.
    /// </para>
    /// <para>
    /// <b>Laid out from the Charge button's corner, in dp, from serialized offsets</b> (rule 8).
    /// CC §6.2's diagram is a cluster — S1 low and left of the Charge, S2–S4 arcing above it — and
    /// the offsets are fields so the owner can move the whole arc on a real phone without a rebuild.
    /// The arithmetic is <c>SkillButton.Place</c>'s, divided by the canvas scale for the same reason,
    /// and anchored bottom-right so <c>SafeAreaFitter</c> insets the cluster away from the gesture
    /// bar.
    /// </para>
    /// <para>
    /// <b>The shipped arc is 140 dp of radius and that number is forced rather than chosen.</b>
    /// CC §6.2's table gives 60 dp buttons with 12 dp of clearance, so two centres may never be
    /// closer than 72 dp — and four of those spread over the quarter turn from <em>left of the
    /// Charge</em> to <em>directly above it</em> need a radius of at least
    /// <c>72 / (2 · sin 15°) ≈ 139</c>. So this is the <em>tightest</em> cluster CC §6.2 permits
    /// rather than a comfortable one, the top button sits about 236 dp above the safe area's bottom
    /// edge, and whether a thumb reaches that far without the hand leaving the stick is precisely
    /// the device question this task carries (ledger row 4).
    /// </para>
    /// <para>
    /// <b>A nonsense dp field leaves the authored layout alone</b>, which is <c>PausePresenter.Place</c>'s
    /// answer rather than <c>TreeViewPresenter.Layout</c>'s — and the difference is that these four
    /// buttons <em>are</em> authored on <c>Hud.prefab</c>, at a real size in a real corner, so there is
    /// something honest to fall back to. A tree cell is a runtime clone that starts life on its
    /// template's rect, so leaving twenty-seven of those alone is twenty-seven cells stacked on each
    /// other; leaving a slot button alone is the prefab's cluster. All three doors —
    /// <see cref="_anchorMarginDp"/>, the four <see cref="_offsetsDp"/> and each button's own
    /// <c>SizeDp</c> — therefore take the same answer, and one row walks NaN, both infinities and a
    /// negative through them.
    /// </para>
    /// </remarks>
    public sealed class SkillBarPresenter : MonoBehaviour
    {
        /// <summary>CC §6.2's minimum gap between two slot buttons, in dp.</summary>
        /// <remarks>
        /// Read by <c>Button_SpacingMeetsTheMinimum</c> rather than by this class: the offsets are
        /// the owner's to tune on a phone (rule 8), and the number is here so the row that checks
        /// them quotes CC §6.2 rather than a literal of its own.
        /// </remarks>
        public const float MinimumSpacingDp = 12f;

        [Tooltip("The four slot buttons in thumb order — S1 first. Exactly four, because CC §6.2 " +
                 "draws four fixed positions and SkillRunner refuses a fifth.")]
        [SerializeField]
        private ManualSkillButton[] _buttons = new ManualSkillButton[SkillRunner.MaxManualSlots];

        [Tooltip("Where each button's centre sits, in dp *left of* and *above* the anchor below — " +
                 "CC §6.2's cluster. A quarter arc of radius 140 dp around the Charge: S1 level " +
                 "with it and to its left, S4 straight above it. Fields rather than prefab " +
                 "positions so the whole arc can be moved on a real phone without a rebuild " +
                 "(ledger row 4).")]
        [SerializeField]
        private Vector2[] _offsetsDp =
        {
            new Vector2(140f, 0f),
            new Vector2(121f, 70f),
            new Vector2(70f, 121f),
            new Vector2(0f, 140f),
        };

        [Tooltip("The corner the offsets are measured from, in dp in from the safe area's right " +
                 "edge and up from its bottom. The Charge button's own margin, so the cluster is " +
                 "placed relative to the button it clusters around.")]
        [SerializeField] private Vector2 _anchorMarginDp = new Vector2(96f, 96f);

        private IRunSession _session;
        private SkillSlotInput _input;
        private ContentCatalog _catalog;

        private IDisposable _startedSubscription;
        private IDisposable _switchedSubscription;

        /// <param name="session">
        /// The run, for the one read that decides which buttons exist and the two each button polls.
        /// Not the runner — <c>RunState</c> hands out scalar reads and keeps the handle (AR §18.2).
        /// </param>
        /// <param name="input">
        /// Where a press is written. <b>Not <c>IPlayerCommands</c></b>: neither this class nor a
        /// button may turn a tap into a command, because <em>when</em> that happens is
        /// <c>RunTicker.CommandPhase</c>'s to decide (rule 3).
        /// </param>
        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="catalog">
        /// What a skill <em>is</em>: <c>RunState</c> hands out ids, so the label's key is looked up
        /// here — <c>SkillsPresenter</c>'s bargain, one screen over.
        /// </param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        [Inject]
        public void Construct(
            IRunSession session,
            SkillSlotInput input,
            DomainEventHub hub,
            ContentCatalog catalog)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            // Disposed before they are replaced, so a component injected twice — which VContainer
            // does not do and a test does — holds one subscription each rather than two.
            _startedSubscription?.Dispose();
            _startedSubscription = hub.Subscribe<RunStarted>(OnRunStarted);

            _switchedSubscription?.Dispose();
            _switchedSubscription = hub.Subscribe<SkillAutoCastChanged>(OnAutoCastChanged);

            Bind();
        }

        /// <exception cref="MissingReferenceException">Fewer than four buttons are dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this presenter.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>HudPresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// injected" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (!HasButtons())
            {
                throw new MissingReferenceException(
                    $"{nameof(SkillBarPresenter)} has fewer than {SkillRunner.MaxManualSlots} slot " +
                    "buttons assigned. CC §6.2 draws four fixed thumb positions and core refuses a " +
                    "fifth manual skill, so a missing button is a slot the player can fill from the " +
                    "Skills screen and then has no way to cast.");
            }

            if (_session is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(SkillBarPresenter)} was never injected, so every manual skill would " +
                    "be uncastable. The component is registered by RunScope — drag this object onto " +
                    "its Skill Bar field.");
            }

            Bind();
            Place();

            // Drawn once immediately, because whether RunStarted has already been published depends
            // on the order VContainer's entry points and this component's Start happen to run in.
            // Either path lands here — HudPresenter's argument, for the sixth screen.
            Redraw();
        }

        /// <remarks>
        /// Explicit rather than left to the hub's disposal, for <c>SkillsPresenter.OnDestroy</c>'s
        /// reason: a component destroyed before its scope would otherwise stay in a subscriber list
        /// and be handed an event for an object Unity has killed. The buttons drop their own
        /// handlers.
        /// </remarks>
        private void OnDestroy()
        {
            _startedSubscription?.Dispose();
            _startedSubscription = null;

            _switchedSubscription?.Dispose();
            _switchedSubscription = null;
        }

        /// <remarks>
        /// The opening state, which no event can describe: a resumed run comes back with slots
        /// already filled (M3-07b rule 7), and a fresh one comes back with four empty ones.
        /// </remarks>
        private void OnRunStarted(RunStarted evt)
        {
            Redraw();
        }

        /// <summary>
        /// A skill moved between Auto and Manual: redraw all four (rule 6).
        /// </summary>
        /// <remarks>
        /// <b>All four rather than the slot the event names</b>, and the payload is used for nothing.
        /// Redrawing one would be right for a move <em>into</em> a slot and wrong for a move out of
        /// one — the event carries −1 then, because it says where the skill <em>is</em> — so the
        /// class would need two paths to say one thing. Four reads of
        /// <c>RunState.ManualSlotAt</c> on a tap the player just made is not work worth splitting,
        /// and it is the whole of why nothing here has to track which button held what.
        /// </remarks>
        private void OnAutoCastChanged(SkillAutoCastChanged evt)
        {
            Redraw();
        }

        /// <summary>
        /// Which of the four are drawn and what each is showing, from one read per slot (rules 5, 6).
        /// </summary>
        /// <remarks>
        /// A null state is a bar in a scene where no run has begun, which is a workflow rather than a
        /// fault — <c>HudPresenter</c>'s argument, reached by pressing Play with the Run scene open.
        /// Every button is taken off in that case rather than left at whatever the prefab was dressed
        /// with, so an undressed scene shows the Charge alone.
        /// </remarks>
        private void Redraw()
        {
            RunState state = _session?.State;

            for (int slot = 0; slot < SkillRunner.MaxManualSlots; slot++)
            {
                ManualSkillButton button = ButtonAt(slot);

                if (button == null)
                {
                    continue;
                }

                if (state is null)
                {
                    button.ShowEmpty();

                    continue;
                }

                ContentId id = state.ManualSlotAt(slot);

                // Empty, and never asked about: CC §6.2 draws only the occupied slots, which is what
                // makes the hole in the middle of the cluster the point rather than a gap (M3-07a
                // rule 3, from the screen's side).
                if (id == default)
                {
                    button.ShowEmpty();

                    continue;
                }

                // The resolve cannot fail in a live run — TreeRules resolved every node of this tree
                // at RunSession.Start, before RunStarted, which is what that sweep is for — so the
                // miss branch is a scene composed against a catalog that does not hold the run's
                // content, and an undrawn button is better than a throw inside an event handler.
                button.Show(_catalog is not null && _catalog.TryGetSkill(id, out SkillSpec spec)
                    ? spec
                    : null);
            }
        }

        /// <summary>Tells each button which slot it is and where to write a press (rule 3).</summary>
        /// <remarks>
        /// Called from both <see cref="Construct"/> and <see cref="Start"/>, because Unity orders
        /// neither against the other: <c>RunScope</c> builds its container from its own <c>Awake</c>,
        /// so injection can land either side of this object's <c>Start</c>. Binding twice is
        /// harmless — <c>ManualSkillButton.Bind</c> removes its handler before adding it.
        /// </remarks>
        private void Bind()
        {
            if (_buttons is null)
            {
                return;
            }

            for (int slot = 0; slot < _buttons.Length; slot++)
            {
                if (_buttons[slot] != null)
                {
                    _buttons[slot].Bind(slot, _input, _session);
                }
            }
        }

        /// <summary>
        /// Sizes and places the four in dp: each <c>SizeDp</c> across, its centre at its own offset
        /// from <see cref="_anchorMarginDp"/> in the safe area's bottom-right corner (rule 8).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Here rather than in the prefab for <c>SkillButton.Place</c>'s two reasons: a
        /// Scale-With-Screen-Size canvas measures in reference pixels, so 60 of those is a different
        /// physical size on every phone — and CC §6.2's number is 60 dp because that is how big a
        /// thumb is, which does not scale with the display. Anchored to the parent's bottom-right so
        /// the cluster follows the safe area rather than the screen, since a button pinned to the
        /// screen ends up under the gesture bar on exactly the phones that have one.
        /// </para>
        /// <para>
        /// <b>A nonsense field leaves the authored layout alone</b> rather than substituting a
        /// constant — see the class remarks for why this screen gets <c>PausePresenter</c>'s answer
        /// and the tree view does not. An unusable anchor skips the whole cluster; an unusable offset
        /// or size skips its own button and leaves the other three placed, because three buttons
        /// where the owner put them and one where the prefab put it is strictly more legible than
        /// four in a heap.
        /// </para>
        /// </remarks>
        private void Place()
        {
            if (_buttons is null || !IsUsableMargin(_anchorMarginDp))
            {
                return;
            }

            float pxPerDp = PixelsPerDp();

            for (int slot = 0; slot < _buttons.Length; slot++)
            {
                ManualSkillButton button = _buttons[slot];

                if (button == null || button.transform is not RectTransform rect)
                {
                    continue;
                }

                if (!IsUsableSize(button.SizeDp) || !IsUsableOffset(OffsetAt(slot)))
                {
                    continue;
                }

                Vector2 offset = OffsetAt(slot);
                float side = button.SizeDp * pxPerDp;

                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(side, side);

                // Measured from the Charge's own corner and then out along the arc: x runs left, to
                // match the right-edge anchor, and y runs up.
                rect.anchoredPosition = new Vector2(
                    -(_anchorMarginDp.x + offset.x) * pxPerDp,
                    (_anchorMarginDp.y + offset.y) * pxPerDp);
            }
        }

        /// <summary>This slot's authored offset, or zero when the array is short of four.</summary>
        /// <remarks>
        /// Zero rather than a throw for the reason the guard in <see cref="Start"/> is about buttons
        /// rather than offsets: a short offsets array puts a button on top of the Charge, which is
        /// visible the moment the scene is opened, while a missing <em>button</em> is a slot that
        /// cannot be cast and looks like core refusing the command.
        /// </remarks>
        private Vector2 OffsetAt(int slot) =>
            _offsetsDp is not null && slot >= 0 && slot < _offsetsDp.Length
                ? _offsetsDp[slot]
                : Vector2.zero;

        private ManualSkillButton ButtonAt(int slot) =>
            _buttons is not null && slot >= 0 && slot < _buttons.Length ? _buttons[slot] : null;

        private bool HasButtons()
        {
            if (_buttons is null || _buttons.Length < SkillRunner.MaxManualSlots)
            {
                return false;
            }

            for (int slot = 0; slot < SkillRunner.MaxManualSlots; slot++)
            {
                if (_buttons[slot] == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsUsableSize(float dp) => float.IsFinite(dp) && dp > 0f;

        private static bool IsUsableOffset(Vector2 dp) => float.IsFinite(dp.x) && float.IsFinite(dp.y);

        private static bool IsUsableMargin(Vector2 dp) =>
            float.IsFinite(dp.x) && float.IsFinite(dp.y) && dp.x >= 0f && dp.y >= 0f;

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
