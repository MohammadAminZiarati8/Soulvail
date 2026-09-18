using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and LevelUp.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// GD §13.1's progression moment, on screen: the game is already stopped, three nodes are
    /// already drawn, and this turns them into three things a thumb can hit — then gets out of the
    /// way and lets the frame start again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It renders two events and owns no progression.</b> <c>OfferPresented</c> shows or redraws,
    /// <c>LevelUpClosed</c> hides. Nothing here counts a pick, decides what is available or knows
    /// what a node does: <c>RunState.Offer</c> carries the ids and <c>ContentCatalog.Skill</c>
    /// carries the rest, which is <c>HudPresenter</c>'s bargain and the reason a disagreement
    /// between screen and game can only be a missed event in this file.
    /// </para>
    /// <para>
    /// <b>It does not hold the pause, and the spec's rule 3 said it should.</b> The gate lives in
    /// <c>RunTicker.LevelUpPhase</c>, which raises <c>PauseReason.LevelUp</c> when <c>HasOffer</c>
    /// goes true and lowers it when it goes false (M3-08a rule 12, and the owner's ruling at
    /// M3-08b). Rule 3's stated reason — <em>"a pause raised in one class and lowered in another is
    /// how a screen ends up closed over a frozen game"</em> — argues against <em>split</em>
    /// ownership, and the ticker holds both halves eight lines apart, so the reason is satisfied and
    /// only the nomination of which file changed. What it buys is that the run being stopped is a
    /// once-a-frame pure function of core state rather than a consequence of a view having received
    /// an event: a presenter that is absent, destroyed or never dressed into the scene costs a
    /// missing screen, loudly, instead of a run that ticks on with an offer nobody can ever spend.
    /// It is also what makes rule 12 below true rather than a caveat. <c>RunPause</c> is deliberately
    /// <em>not</em> a dependency of this class, and a test pins its absence.
    /// </para>
    /// <para>
    /// <b>It subscribes in <see cref="Construct"/>, not <c>OnEnable</c></b> —
    /// <c>HudPresenter</c>'s reason: <c>RunScope</c> builds its container from its own <c>Awake</c>
    /// and Unity orders no two of those, so an <c>OnEnable</c> subscription reaches for a hub that
    /// may not exist yet. Dropped in <c>OnDestroy</c>.
    /// </para>
    /// <para>
    /// <b>No fade, in or out.</b> <c>Time.timeScale</c> is 0 while the screen is up (M3-08a rule
    /// 13), so a scaled animation would freeze mid-way and an unscaled one is a second clock in a
    /// file whose whole job is to be brief — GD §13.1 budgets <em>"a couple of seconds"</em> for the
    /// entire interruption. M2-10's 0.3 s stage fade is the precedent for a transition the player is
    /// not deciding anything during; this is the opposite case.
    /// </para>
    /// <para>
    /// <b>It takes no touches away from the game</b>, because the game is not running: the canvas is
    /// full-screen and the tick is gated, so the stick and the Charge button are inert underneath it
    /// rather than needing to be disabled (M3-08a rule 14).
    /// </para>
    /// </remarks>
    public sealed class LevelUpPresenter : MonoBehaviour
    {
        /// <summary>
        /// CH §5.1's header. <c>{0:0}</c> rather than <c>{0}</c> for <c>HudPresenter</c>'s reason:
        /// TMP treats an unformatted placeholder as "up to nine decimal places", so the natural
        /// spelling renders level 7 as <c>7</c> only by luck of the float overload.
        /// </summary>
        private const string LevelFormat = "Level {0:0}";

        /// <summary>
        /// <em>"Pick i of n"</em> — what makes a double level-up legible (M3-08a rule 3) rather than
        /// a card that surprises the player by reappearing.
        /// </summary>
        private const string PickFormat = "Pick {0:0} of {1:0}";

        [Tooltip("The whole screen, switched between alpha 0 and 1. No fade — see the class " +
                 "remarks: timeScale is 0 while this is up, so a scaled tween would freeze.")]
        [SerializeField] private CanvasGroup _root;

        [Tooltip("The three cards, in the order they are drawn. Exactly three: M3-04 draws at most " +
                 "OfferGenerator.DefaultOfferCount, and an offer shorter than that leaves the rest " +
                 "hidden rather than drawn empty (rule 6).")]
        [SerializeField] private OfferCard[] _cards = new OfferCard[3];

        [Tooltip("\"Level N\", from RunState.Level.")]
        [SerializeField] private TMP_Text _levelLabel;

        [Tooltip("\"Pick i of n\", from OfferPresented.PicksOwed.")]
        [SerializeField] private TMP_Text _pickLabel;

        [Tooltip("One card's size in dp. Applied at runtime for the reason HudPresenter applies " +
                 "its own: a Scale-With-Screen-Size canvas measures in reference pixels, which are " +
                 "a different physical size on every phone. A guess until a phone exists — ledger " +
                 "row 4.")]
        [SerializeField] private Vector2 _cardSizeDp = new Vector2(200f, 260f);

        [Tooltip("The gap between two cards, in dp. A guess until a phone exists — ledger row 4.")]
        [Min(0f)]
        [SerializeField] private float _cardGapDp = 20f;

        [Tooltip("Up to CH §5.1's tree view (M3-09d) — the line M3-08b's Out of scope reserved. " +
                 "The cards are hidden, not destroyed, and looking spends no pick: see OpenTree.")]
        [SerializeField] private Button _viewTree;

        [Tooltip("The tree view itself. Dressed in Run.unity rather than on this prefab, because " +
                 "the two are separate root prefabs — PausePresenter's wiring decision, for its " +
                 "reason. Optional twice over: without it, and for a class with no tree, the " +
                 "toggle is taken off this screen rather than left dead (M3-09d rule 2).")]
        [SerializeField] private TreeViewPresenter _treeScreen;

        private IRunSession _session;
        private IProgressionCommands _progression;
        private ContentCatalog _catalog;

        /// <summary>What a node is called. Handed to each pooled cell on its Show (M3-14a rule 11).</summary>
        private ILocalizer _localizer;

        private IDisposable _offerSubscription;
        private IDisposable _closedSubscription;

        /// <summary>
        /// How many picks this level-up episode started with — the <em>n</em> of "pick i of n".
        /// </summary>
        /// <remarks>
        /// Latched on the first <c>OfferPresented</c> of an episode rather than read off each one,
        /// because <c>OfferPresented.PicksOwed</c> counts <em>down</em>: two picks owed arrive as
        /// <c>PicksOwed</c> 2 and then 1, and a header written straight off the payload would read
        /// "Pick 2 of 2" and then "Pick 1 of 1".
        /// </remarks>
        private int _episodeTotal;

        /// <summary>
        /// A card has been tapped and the command has not been answered by a new frame yet.
        /// </summary>
        /// <remarks>
        /// <b>Cleared in <see cref="Update"/> rather than in the <c>OfferPresented</c> handler, and
        /// that is the whole of rule 5.</b> <c>ChooseOffer</c> runs synchronously and may repaint
        /// all three cards for a second pick before it returns, so a handler that cleared this flag
        /// would re-arm the cards inside the very call the first tap started — and a double tap in
        /// one frame would spend the second pick on whatever now sits at that index, a node the
        /// player never saw. uGUI dispatches both taps of a double tap from one
        /// <c>EventSystem</c> pass, with no <c>Update</c> of ours between them, so a latch that
        /// only a new frame can lift is what refuses the second. The cards are made
        /// <em>interactable</em> again by the redraw immediately, so nothing looks dead; it is the
        /// command that waits a frame.
        /// </remarks>
        private bool _choosing;

        /// <summary>
        /// The tree view is up over this screen and the cards are waiting behind it (M3-09d rule 6).
        /// </summary>
        /// <remarks>
        /// <b>A field rather than a read of the root's alpha, because the two mean different
        /// things.</b> <see cref="IsShown"/> is <em>"is the player looking at cards"</em> and goes
        /// false the moment the veil drops; this is <em>"there is an episode behind the veil"</em>,
        /// and it is what stops <see cref="Update"/>'s net putting a closed screen back up.
        /// </remarks>
        private bool _veiled;

        /// <param name="session">
        /// The run, for the two reads a card needs — the offer's ids and the level. Not
        /// <c>IPlayerCommands</c>: a screen asks the game for nothing about the player's body.
        /// </param>
        /// <param name="progression">Where a tap goes. The only thing this class asks of the run.</param>
        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="catalog">
        /// What a node <em>is</em>. <c>RunState.Offer</c> hands out ids and nothing else, so the
        /// name, the description and the kind are looked up here — AR §18.2's rule that state hands
        /// out reads rather than handles, for the eighth time.
        /// </param>
        /// <param name="localizer">
        /// What a node is <em>called</em>. Held by this class and handed to each card on
        /// <c>OfferCard.Show</c>, because a card is a pooled template nothing injects individually
        /// (M3-14a rule 11) — the presenter that owns the pool is the one thing injected.
        /// </param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        [Inject]
        public void Construct(
            IRunSession session,
            IProgressionCommands progression,
            DomainEventHub hub,
            ContentCatalog catalog,
            ILocalizer localizer)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _offerSubscription = hub.Subscribe<OfferPresented>(OnOfferPresented);
            _closedSubscription = hub.Subscribe<LevelUpClosed>(OnLevelUpClosed);

            Wire(_viewTree, OpenTree);
        }

        /// <exception cref="MissingReferenceException">The root, a card or a label is not dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this presenter.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>HudPresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// injected" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (_root == null || _levelLabel == null || _pickLabel == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(LevelUpPresenter)} is missing its root or one of its two labels. A " +
                    "level-up screen that is only partly dressed stops the game and then shows the " +
                    "player nothing, which is indistinguishable from a crash.");
            }

            if (_cards is null || _cards.Length == 0)
            {
                throw new MissingReferenceException(
                    $"{nameof(LevelUpPresenter)} has no cards assigned. Drag the three " +
                    $"{nameof(OfferCard)} objects onto its Cards field — without them the run " +
                    "stops for a level-up that cannot be chosen, and the only way out is to kill " +
                    "the app.");
            }

            if (_session is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(LevelUpPresenter)} was never injected, so it would never hear an " +
                    "offer. The component is registered by RunScope — drag this object onto its " +
                    "Level Up Presenter field.");
            }

            Place();

            // Down whatever the prefab was left dressed as, so a screen someone was editing cannot
            // ship covering the arena — HudPresenter's argument for its death panel and its fade.
            HideScreen();
        }

        /// <remarks>
        /// Explicit rather than left to the hub's disposal: a screen destroyed before its scope — a
        /// scene reload, an arena opened without a run — would otherwise stay in two subscriber
        /// lists and be handed events for a component Unity has killed.
        /// <para>
        /// <b>It drops the subscriptions and the one button handler, and nothing else.</b> A scope
        /// torn down with the screen up leaves the pause held, and <c>RunPause.Dispose</c> restores
        /// both globals unconditionally (M3-08a rule 13) — VContainer orders no two disposals, so
        /// this file must not depend on being the one that resumes. Under the owner's ruling it
        /// could not be, which is what makes this rule true rather than a caveat.
        /// </para>
        /// </remarks>
        private void OnDestroy()
        {
            _offerSubscription?.Dispose();
            _closedSubscription?.Dispose();

            _offerSubscription = null;
            _closedSubscription = null;

            Unwire(_viewTree, OpenTree);
        }

        /// <remarks>
        /// <para>
        /// One bool per frame while the screen is up, and it is the half of rule 5 a handler cannot
        /// do — see <see cref="_choosing"/>. Runs at <c>timeScale</c> 0 because <c>Update</c> does.
        /// </para>
        /// <para>
        /// <b>And two reads of the tree view, which are M3-09b's poll arriving on this screen.</b>
        /// The first is the net under <see cref="OpenTree"/>'s callback: a tree that is destroyed or
        /// closed by something this file did not ask gives the cards back anyway, where a callback
        /// alone would leave a stopped game showing nothing at all. The second is rule 2 — a class
        /// with no tree is not offered the button — asked here rather than at <c>Start</c> because
        /// the run has not necessarily begun by then, <c>PausePresenter.RefreshPanel</c>'s reason.
        /// </para>
        /// </remarks>
        private void Update()
        {
            _choosing = false;

            if (_veiled && (_treeScreen == null || !_treeScreen.IsOpen))
            {
                OnTreeClosed();
            }

            RefreshTreeButton();
        }

        /// <summary>
        /// Three nodes are on the table: draw them, or redraw them for the next pick.
        /// </summary>
        private void OnOfferPresented(OfferPresented evt)
        {
            RunState state = _session?.State;

            if (state is null)
            {
                // A screen in a scene where no run has begun, which is a workflow rather than a
                // fault — HudPresenter's argument, reached by pressing Play with the Run scene open.
                return;
            }

            // Latched only when the screen was not already up, so the second offer of a double
            // level-up keeps the n it started with — see the field's remarks.
            if (!IsShown)
            {
                _episodeTotal = evt.PicksOwed;
            }

            // Belt and braces against an episode whose first event was somehow missed: a zero or
            // negative total would render "Pick 1 of 0".
            if (_episodeTotal < evt.PicksOwed)
            {
                _episodeTotal = evt.PicksOwed;
            }

            Draw(state, evt.Count);
            WriteHeader(state.Level, evt.PicksOwed);

            ShowScreen();
        }

        /// <summary>
        /// Nothing more is owed. The screen closes and <c>RunTicker</c> lifts the gate on the next
        /// frame.
        /// </summary>
        private void OnLevelUpClosed(LevelUpClosed evt)
        {
            _episodeTotal = 0;

            HideScreen();
        }

        /// <summary>
        /// Puts <paramref name="count"/> of the offer's ids on cards and hides the rest.
        /// </summary>
        /// <remarks>
        /// <c>count</c> comes off the event and the ids come off the state, and the two are read on
        /// the same frame for a reason: <c>RunState.Offer</c> is a live view over one buffer the
        /// next draw rewrites, safe only because it is read on a frame that is not ticking. Nothing
        /// here holds it across a <c>ChooseOffer</c> — it is indexed and the specs are resolved
        /// immediately.
        /// </remarks>
        private void Draw(RunState state, int count)
        {
            IReadOnlyList<ContentId> offer = state.Offer;

            for (int i = 0; i < _cards.Length; i++)
            {
                OfferCard card = _cards[i];

                if (card == null)
                {
                    continue;
                }

                // Rule 6: M3-04 rule 1 writes fewer than three when the tree is nearly exhausted,
                // and the rest are hidden rather than drawn empty — CC §6.2's "unused slots are not
                // drawn", applied to the other screen with a variable number of things on it.
                // Bounded by the offer as well as by the count, because the two disagreeing would
                // be an IndexOutOfRange inside an event handler.
                if (i >= count || i >= offer.Count)
                {
                    card.Hide();
                    continue;
                }

                card.Show(i, _catalog.Skill(offer[i]), _localizer, OnCardChosen);
            }
        }

        /// <summary>
        /// A card was tapped: spend the pick, unless this frame already spent one.
        /// </summary>
        /// <remarks>
        /// The cards go dead <em>before</em> the command rather than after it, which is rule 5's
        /// whole point: <c>ChooseOffer</c> runs synchronously and may repaint all three on its way
        /// through, so anything done afterwards would be undoing the redraw.
        /// </remarks>
        private void OnCardChosen(int index)
        {
            if (_choosing)
            {
                return;
            }

            _choosing = true;

            for (int i = 0; i < _cards.Length; i++)
            {
                if (_cards[i] != null)
                {
                    _cards[i].SetInteractable(false);
                }
            }

            _progression.ChooseOffer(index);
        }

        private void WriteHeader(int level, int picksOwed)
        {
            if (_levelLabel != null)
            {
                _levelLabel.SetText(LevelFormat, level);
            }

            if (_pickLabel == null)
            {
                return;
            }

            // i counts up while PicksOwed counts down: two owed arrive as 2 then 1, and the player
            // is on pick 1 and then pick 2 of the same 2.
            int index = (_episodeTotal - picksOwed) + 1;

            _pickLabel.SetText(PickFormat, index, _episodeTotal);
        }

        /// <summary>Whether the screen is currently up. The one read a test needs and rule 1 allows.</summary>
        public bool IsShown => _root != null && _root.alpha > 0f;

        /// <summary>
        /// Raises CH §5.1's tree view over this screen. The View Tree toggle's handler — the line
        /// M3-08b's <em>Out of scope</em> reserved for M3-09d.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The cards are hidden, not destroyed, and no pick is spent by looking</b> (M3-09d
        /// rule 6). The offer is state on <c>LevelUpFlow</c> (M3-08a) and nothing here touches it:
        /// the veil is the root's alpha and its raycasts, so every card keeps its index, its id, its
        /// listener and its <c>interactable</c>, and coming back is <see cref="ShowScreen"/> rather
        /// than a redraw. <b>Nothing may call <see cref="Draw"/> on the way back</b> — repainting
        /// would re-arm cards that a tap in the same frame had deliberately killed, and re-latching
        /// <see cref="_choosing"/> is exactly the failure rule 5 exists to refuse. A player who
        /// plans and then picks has lost nothing, which is the whole promise of <em>"opt-in, so it
        /// never slows down anyone who doesn't care"</em>.
        /// </para>
        /// <para>
        /// <b>It does not lower the pause and must not</b> (M3-09d rule 5). The gate is
        /// <c>RunTicker.LevelUpPhase</c>'s and is a once-a-frame function of <c>HasOffer</c>, which
        /// is still true behind the veil — so the game stays stopped, the holder stays
        /// <c>PauseReason.LevelUp</c>, and a tree view that resumed the fight underneath itself
        /// would be the opposite of what a planning screen is for. This class still cannot reach a
        /// <c>RunPause</c> at all, which is what makes that true rather than a promise.
        /// </para>
        /// </remarks>
        public void OpenTree()
        {
            if (_treeScreen == null || _veiled || !IsShown || _treeScreen.IsOpen)
            {
                return;
            }

            _veiled = true;

            Veil();

            _treeScreen.Open(OnTreeClosed);
        }

        /// <summary>
        /// The tree gave the cards back: the same three, still interactable, still the same ids.
        /// </summary>
        /// <remarks>
        /// Rule 5's callback and <see cref="Update"/>'s net both land here, which is why it is
        /// idempotent: whichever arrives first does the work and the other returns.
        /// </remarks>
        private void OnTreeClosed()
        {
            if (!_veiled)
            {
                return;
            }

            _veiled = false;

            ShowScreen();
        }

        /// <summary>Rule 2 from this side: a class with no tree is offered no toggle.</summary>
        private void RefreshTreeButton()
        {
            if (_viewTree == null)
            {
                return;
            }

            bool offered = _treeScreen != null && _treeScreen.HasTree;

            if (_viewTree.gameObject.activeSelf != offered)
            {
                _viewTree.gameObject.SetActive(offered);
            }
        }

        /// <remarks>
        /// Alpha 1 on the same call, with no tween and no coroutine — rule 4. The raycast block goes
        /// on with it, which is rule 11: the canvas is full-screen, so the stick and the Charge
        /// button are covered rather than disabled.
        /// </remarks>
        private void ShowScreen()
        {
            if (_root == null)
            {
                return;
            }

            _root.alpha = 1f;
            _root.blocksRaycasts = true;
            _root.interactable = true;
        }

        /// <remarks>
        /// <b>The veil plus the cards, and the split is M3-09d rule 6's</b>: closing an episode
        /// takes the cards off the screen, where opening the tree over it deliberately does not.
        /// The two were one method until the tree view needed the half that comes back.
        /// </remarks>
        private void HideScreen()
        {
            _veiled = false;

            Veil();

            if (_root == null)
            {
                return;
            }

            for (int i = 0; i < _cards.Length; i++)
            {
                if (_cards[i] != null)
                {
                    _cards[i].Hide();
                }
            }
        }

        /// <summary>
        /// Takes the screen out of sight and out of the raycaster, leaving what is on it alone.
        /// </summary>
        /// <remarks>
        /// <b>Alpha <em>and</em> <c>blocksRaycasts</c>, and that pair is what a sorting order would
        /// otherwise have to buy.</b> A canvas left at alpha 0 with its raycasts off draws nothing
        /// and takes no touches, so the tree view is fully visible and fully tappable over it —
        /// <c>TreeViewPresenter</c> still sorts above this one anyway, for the reason its own
        /// remarks give.
        /// </remarks>
        private void Veil()
        {
            if (_root == null)
            {
                return;
            }

            _root.alpha = 0f;
            _root.blocksRaycasts = false;
            _root.interactable = false;
        }

        /// <summary>
        /// Lays the cards out in a centred row, in dp (rule 10).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Here rather than in the prefab for <c>HudPresenter.Place</c>'s two reasons: a
        /// Scale-With-Screen-Size canvas measures in reference pixels, so a card authored at 200 of
        /// those is a different physical width on every phone; and three rects that have to agree
        /// about where each other are is three chances to overlap if each does its own arithmetic.
        /// </para>
        /// <para>
        /// <b>A non-finite or non-positive dp field leaves the prefab's authored layout alone</b>
        /// rather than writing it through. These are Inspector doors — the owner is meant to tune
        /// them on a device — and a NaN reaching <c>sizeDelta</c> is a <c>RectTransform</c> that
        /// never renders again, which on this screen is a stopped game showing nothing. The same
        /// argument <c>HudPresenter</c> makes about a NaN alpha and <c>SafeAreaFitter</c> about a
        /// zero-sized screen.
        /// </para>
        /// </remarks>
        private void Place()
        {
            if (!IsUsableSize(_cardSizeDp.x) || !IsUsableSize(_cardSizeDp.y) || !IsUsableGap(_cardGapDp))
            {
                return;
            }

            float pxPerDp = PixelsPerDp();

            float totalDp = (_cards.Length * _cardSizeDp.x) + ((_cards.Length - 1) * _cardGapDp);
            float leftDp = -totalDp * 0.5f;

            for (int i = 0; i < _cards.Length; i++)
            {
                if (_cards[i] == null)
                {
                    continue;
                }

                if (_cards[i].transform is RectTransform rect)
                {
                    // Centred on the parent, so the row follows the safe area rather than the
                    // screen: SafeAreaFitter insets the content root on a notched device.
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0f, 0.5f);
                    rect.sizeDelta = _cardSizeDp * pxPerDp;
                    rect.anchoredPosition = new Vector2(leftDp * pxPerDp, 0f);
                }

                leftDp += _cardSizeDp.x + _cardGapDp;
            }
        }

        private static bool IsUsableSize(float dp) => float.IsFinite(dp) && dp > 0f;

        private static bool IsUsableGap(float dp) => float.IsFinite(dp) && dp >= 0f;

        /// <summary>
        /// How many canvas units one dp is worth — <c>HudPresenter.PixelsPerDp</c>, for its reason.
        /// </summary>
        private float PixelsPerDp()
        {
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            return StickShaper.PixelsPerDp(Screen.dpi) / scale;
        }

        /// <remarks>
        /// Removed before it is added, and with a named method rather than a lambda, so that a
        /// component injected twice — which VContainer does not do and a test does — reports one tap
        /// once rather than once per injection. <c>PausePresenter.Wire</c>'s trick, for its reason.
        /// </remarks>
        private static void Wire(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(handler);
            button.onClick.AddListener(handler);
        }

        private static void Unwire(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(handler);
            }
        }
    }
}
