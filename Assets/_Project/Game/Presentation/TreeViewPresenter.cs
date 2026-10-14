using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and TreeView.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The tree screen: the whole tree as a grid, a column per branch and tier 1 at the bottom, with
    /// the player's path highlighted. In a free run the level star opens it over its own pause and a
    /// banked point takes any open node (UI-03c); in a drawing run it is CH §5.1's read-only
    /// <em>"View Tree"</em>, opened from the pause panel or the level-up screen. GD §13.1, §16.4.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It spends a point only in a free run, and asks core every time</b> (UI-03c rules 4, 7).
    /// CH §5.1's random-from-available is superseded for a mode with <c>HasFreePick</c>; for every
    /// other mode this is still the read-only view, so a cell is takeable only when
    /// <c>RunState.IsFreePick</c>, a point is banked and core says the node is available — and the
    /// tap handler asks all three again before it sends <c>TakeNode</c>. <b>The points are read after
    /// <c>TakeNode</c> returns, never in a <c>NodeTaken</c> handler</b>: core publishes that event
    /// before it spends the point (UI-03b's As built 2).
    /// </para>
    /// <para>
    /// <b>The shape comes from the catalog and the state from the run</b> (rule 2).
    /// <c>ContentCatalog.TryGetTreeFor</c> gives the <see cref="SkillTreeSpec"/> — three branches of
    /// tiers of ids (M3-02a) — and each id resolves through <c>ContentCatalog.Skill</c> for its keys
    /// and its kind. That resolve cannot fail here: <c>TreeRules</c> resolved every node of this
    /// tree at <c>RunSession.Start</c>, before <c>RunStarted</c>, which is what that sweep is for.
    /// <b>A class with no tree shows one line saying so and neither door offers its button</b> —
    /// that is every run in the build until M3-12, and a button onto a blank screen is worse than no
    /// button.
    /// </para>
    /// <para>
    /// <b>Two doors, one screen, and each gets itself back</b> (rule 5). <see cref="Open(Action)"/>
    /// takes the callback, so the pause panel and the level-up screen each hand in their own return
    /// — and that is the one thing a poll cannot do, because a poll knows the screen went down and
    /// not which way it came in. <b>M3-09b ruled against a callback for the Skills screen and that
    /// ruling is not overturned; it is answered on its own terms.</b> Its reason was that a screen
    /// destroyed, never dressed, or closed by something the caller did not ask gives the panel back
    /// anyway, where a callback leaves dead buttons over a paused run with no way out. So both
    /// doors keep a once-a-frame poll of <see cref="IsOpen"/> as the <em>net</em>, and use the
    /// callback for the half the net cannot do. The callback decides <em>which</em> door; the poll
    /// guarantees <em>a</em> door.
    /// </para>
    /// <para>
    /// <b>Two ways in, and only the star's holds a pause.</b> <see cref="Open()"/> is the level
    /// star's: it holds <c>PauseReason.Tree</c> itself, <c>PausePresenter</c>'s pattern, and only
    /// when nothing else holds the pause — otherwise it does not open, because <c>RunPause</c> throws
    /// for a second holder (M3-08a rule 12). <see cref="Open(Action)"/> is the two older doors', and
    /// holds none: the panel's <c>Menu</c> or the ticker's <c>LevelUp</c> is already up underneath.
    /// <see cref="Close"/> resumes <c>Tree</c> only if it is the holder.
    /// </para>
    /// <para>
    /// <b>It renders no events at all</b> (rule 10), and declares no <c>Update</c> — which is the
    /// strongest way to say it, <c>SkillsPresenter</c>'s phrasing. The tree changes under this screen
    /// only by a take made on it, which redraws on the spot. Built on open from reads.
    /// </para>
    /// <para>
    /// <b>Its canvas sorts above every screen it can be opened from, and that is the rule rather
    /// than this screen's number.</b> HUD 0, the first-active hint 40, the pause panel 50, the
    /// Skills screen 60, the level-up 100, this 110. Sitting below the level-up's would have worked
    /// today — <see cref="LevelUpPresenter"/> drops its root's alpha <em>and</em> its
    /// <c>blocksRaycasts</c> before opening this, so an alpha-0 canvas at 100 draws nothing and
    /// takes no touches — but that is a promise made in another file about its runtime state, where
    /// a sorting order is a fact about this asset. The failure it would produce is a tree drawn
    /// underneath the cards on a stopped game, which is the family this project keeps refusing.
    /// </para>
    /// <para>
    /// <b>No scroll and no zoom</b> (UI-03c rule 9). Three columns of at most eight tiers is a fixed
    /// grid that fits a 9:20 safe area; cells shrink to fit their column, so the honest failure mode
    /// is <em>small</em> rather than <em>clipped</em>, and how small is a device question.
    /// </para>
    /// <para>
    /// <b>Close is a button, and Android Back closes too</b> (UI-03c rule 5). Back arrives as the
    /// Input System's Escape key; it is one <c>InputAction</c> of this screen's own, enabled only
    /// while the screen is up, so its callback is the whole of the wiring and no <c>Update</c> polls
    /// for it.
    /// </para>
    /// <para>
    /// <b>No fade, in or out.</b> <c>Time.timeScale</c> is 0 while this is up, so a scaled animation
    /// would freeze half-played and an unscaled one is a second clock — M3-08b rule 4's argument and
    /// M3-09a's and M3-09b's, for the fourth time.
    /// </para>
    /// </remarks>
    public sealed class TreeViewPresenter : MonoBehaviour
    {
        /// <summary>The cell height a non-finite or non-positive <see cref="_cellHeightDp"/> falls back to.</summary>
        private const float DefaultCellHeightDp = 64f;

        // The screen's chrome in dp (UI-03c rule 9). Every word clears Android's 12 sp floor (M4-07);
        // the rest are guesses until a phone exists.
        private const float HeaderMarginDp = 12f;
        private const float HeaderHeightDp = 40f;
        private const float HeaderTextDp = 18f;

        // The stone frame (UI-04b). The top margin is deeper than the others because the ribbon is
        // centred on the frame's top edge and must stay inside the safe area; the border is the
        // width the sliced art's rim is drawn at.
        private const float FrameTopMarginDp = 36f;
        private const float FrameBorderDp = 32f;
        private const float ContentInsetDp = 28f;
        private const float RibbonWidthDp = 170f;
        private const float RibbonHeightDp = 56f;
        private const float RibbonTextInsetDp = 30f;
        private const float RibbonTextDp = 16f;
        private const float PillWidthDp = 80f;
        private const float PillHeightDp = 28f;
        private const float PillPadDp = 12f;
        private const float PillIconDp = 20f;
        private const float PillTextDp = 16f;
        private const float CloseSizeDp = 48f;
        private const float PieceGapDp = 4f;

        /// <summary>Border widths of the sliced art, in sprite pixels (UI-04a rule 3).</summary>
        private const float PanelBorderPx = 110f;
        private const float RibbonBorderPx = 200f;
        private const float RibbonSpriteHeightPx = 267f;
        private const float PillBorderPx = 105f;
        /// <summary>The room kept under the block for UI-04d's card, in dp (UI-04c rule 10).</summary>
        private const float CardSpaceDp = 190f;

        // A safe area shorter than this cannot stack the block over the full card, so the card goes
        // compact (UI-07d): 320 dp tall is the look pass's phone, 432 dp the next one up.
        private const float CompactBelowDp = 480f;
        private const float CompactBottomDp = 16f;
        private const float CompactGapDp = 8f;
        private const float CompactTopGapDp = 2f;
        private const float CompactCellGapDp = 4f;

        // A safe area taller than it is wide (UI-08e): the popup is the safe width less this a side,
        // the block sits over the full card, and the card keeps the bottom, under the thumb.
        private const float PortraitSideDp = 16f;
        private const float PortraitCardGapDp = 8f;

        /// <summary>
        /// <em>"This class has no skill tree yet."</em> — shown instead of the columns for a class
        /// with none (M3-09d rule 2).
        /// </summary>
        /// <remarks>
        /// Authored here rather than on the prefab, <c>SkillsPresenter.EmptyKey</c>'s reason.
        /// </remarks>
        private static readonly LocKey NoTreeKey = new LocKey("ui.tree.none");

        /// <summary>
        /// Back to whichever door this screen was opened by (M3-14c).
        /// </summary>
        /// <remarks>
        /// <see cref="NoTreeKey"/>'s reason, and the most visible of M3-14b's nine: this button sits
        /// directly under twelve nodes that have read English since M3-14a, so the screen has been
        /// shipping a paragraph of words above a key. The prefab keeps the key as its authored text,
        /// which is <see cref="NoTreeKey"/>'s own pattern.
        /// </remarks>
        private static readonly LocKey CloseKey = new LocKey("ui.tree.close");

        /// <summary><em>"Points: {0}"</em> — the header's count of banked points (UI-03c rule 5).</summary>
        private static readonly LocKey PointsKey = new LocKey("ui.tree.points");

        /// <summary><em>"Skill Tree"</em> — the ribbon's title (UI-04b rule 2).</summary>
        private static readonly LocKey TitleKey = new LocKey("ui.tree.title");

        /// <summary>Android Back, as the Input System reports it.</summary>
        private const string BackBinding = "<Keyboard>/escape";

        [Tooltip("The whole screen, switched between alpha 0 and 1. No fade — see the class " +
                 "remarks: timeScale is 0 while this is up, so a scaled tween would freeze.")]
        [SerializeField] private CanvasGroup _root;

        [Tooltip("Where each branch's cells are parented, in branch order — CH §5's three columns. " +
                 "The rects are authored on the prefab, so how wide a column is and where it sits " +
                 "is the asset's business and only what happens *inside* one is this file's.")]
        [SerializeField] private RectTransform[] _branchColumns = new RectTransform[SkillTreeSpec.BranchCount];

        [Tooltip("The heading above each column, in branch order. Draws SkillBranchSpec.NameKey " +
                 "resolved through ILocalizer — \"Oath\", \"Censure\", \"Judgment\".")]
        [SerializeField] private TMP_Text[] _branchLabels = new TMP_Text[SkillTreeSpec.BranchCount];

        [Tooltip("The cell every node is cloned from, and never shown itself. One clone per node " +
                 "is built under its own column on first open and reused after that (rule 4).")]
        [SerializeField] private TreeNodeButton _cellTemplate;

        [Tooltip("The block the grid, its links, row labels and the class plate are drawn by (UI-04c).")]
        [SerializeField] private TreeBlockView _block;

        [Tooltip("The card under the block: the selected node and its Learn button (UI-04d).")]
        [SerializeField] private TreeDetailCard _card;

        [Tooltip("The pill's count of banked points, from ui.tree.points: a bare number. A drawing " +
                 "run reads 0 (UI-04b rule 5).")]
        [SerializeField] private TMP_Text _pointsLabel;

        [Tooltip("The stone popup: a sliced Popup_Panel filling the safe area less its margin.")]
        [SerializeField] private RectTransform _frame;

        [Tooltip("The sliced Popup_TitleRibbon, centred on the frame's top edge.")]
        [SerializeField] private RectTransform _ribbon;

        [Tooltip("The ribbon's title, from ui.tree.title.")]
        [SerializeField] private TMP_Text _titleLabel;

        [Tooltip("The points pill: a sliced Pill_Dark right of the ribbon.")]
        [SerializeField] private RectTransform _pill;

        [Tooltip("Icon_SkillPoint, left in the pill.")]
        [SerializeField] private RectTransform _pillIcon;

        [Tooltip("\"This class has no skill tree\", as a LocKey. Shown instead of the columns for a " +
                 "class with none, because an empty screen and a broken screen look identical.")]
        [SerializeField] private TMP_Text _emptyLabel;

        [Tooltip("Back to whichever door this was opened by, and gives back the Tree pause when " +
                 "the star's Open took it.")]
        [SerializeField] private Button _close;

        [Tooltip("\"Close\". Written from ui.tree.close in Start, so the prefab's own value is a " +
                 "placeholder — the empty label's pattern (M3-14c).")]
        [SerializeField] private TMP_Text _closeLabel;

        [Tooltip("How wide and tall one tile wants to be, in dp. A ceiling rather than a size: a " +
                 "block taller or narrower than that shrinks its tiles to fit (UI-04c rule 10). " +
                 "Applied at runtime for the reason HudPresenter applies its own — a " +
                 "Scale-With-Screen-Size canvas measures in reference pixels. A guess until a " +
                 "phone exists — ledger row 4.")]
        [SerializeField] private float _cellHeightDp = DefaultCellHeightDp;

        [Tooltip("The gap between two cells, in dp, both down a column and across a tier. A guess " +
                 "until a phone exists — ledger row 4.")]
        [Min(0f)]
        [SerializeField] private float _cellGapDp = 6f;

        private IRunSession _session;
        private IProgressionCommands _progression;
        private RunPause _pause;
        private ContentCatalog _catalog;
        private SkillIconBook _icons;
        private CharacterLookBook _looks;

        /// <summary>Android Back while the screen is up; created on first show (UI-03c rule 5).</summary>
        private InputAction _back;

        /// <summary>What a node is called. Handed to each pooled cell on its Show (M3-14a rule 11).</summary>
        private ILocalizer _localizer;

        /// <summary>
        /// One cell per node, in tree order, built once on first open.
        /// </summary>
        /// <remarks>
        /// <c>SkillsPresenter._rows</c>'s shape one screen on, and instantiated on first open for
        /// its reason: twenty-seven small rects are cheap, and the frame this screen opens on is one
        /// where the simulation is already stopped. Tree order — branch 0 tier 1 in authored order,
        /// then tier 2, then branch 1 — is <c>SkillTree.Available</c>'s order and
        /// <c>SkillTree.Flatten</c>'s, so a cell's index means the same thing here as it does in
        /// core. The parents never change, because a run's class cannot.
        /// </remarks>
        private readonly List<TreeNodeButton> _cells = new List<TreeNodeButton>();

        /// <summary>Canvas units to the dp when set above 0, else read from the screen (UI-07d rule 4).</summary>
        private float _pxPerDpOverride;

        /// <summary>Whether the last layout was the short-screen one: compact card, no plate.</summary>
        private bool _compact;

        /// <summary>Whether the last layout was for a safe area taller than it is wide.</summary>
        private bool _portrait;

        /// <summary>The tapped node, or <c>default</c>; cleared on open and on <see cref="Close"/>.</summary>
        private ContentId _selected;

        /// <summary>How the door that opened this gets itself back — rule 5's whole mechanism.</summary>
        private Action _onClosed;

        /// <summary>This run's tree, or null for a class with none. Resolved once.</summary>
        private SkillTreeSpec _tree;

        /// <summary>
        /// Whether <see cref="_tree"/> has been asked for yet.
        /// </summary>
        /// <remarks>
        /// A flag rather than a null check, because null is the answer for a class with no tree and
        /// asking the catalog once a frame for a miss is work <see cref="HasTree"/>'s callers do
        /// every frame of a run.
        /// </remarks>
        private bool _resolved;

        /// <summary>The node whose card is showing, or <c>default</c> when none is (UI-04d rule 2).</summary>
        public ContentId Selected => _selected;

        /// <summary>Whether the last layout used the compact card for a short screen (UI-07d).</summary>
        public bool IsCompact => _compact;

        /// <summary>Whether the last layout was the portrait one: safe area taller than wide (UI-08e).</summary>
        public bool IsPortrait => _portrait;

        /// <summary>
        /// Lays the screen out with <paramref name="canvasUnitsPerDp"/> instead of the screen's own,
        /// redrawing if it is open: what a device would have, for a layout check (UI-07d rule 4).
        /// Zero or less goes back to the screen's.
        /// </summary>
        public void UsePixelsPerDp(float canvasUnitsPerDp)
        {
            _pxPerDpOverride = float.IsFinite(canvasUnitsPerDp) && canvasUnitsPerDp > 0f ? canvasUnitsPerDp : 0f;

            if (IsOpen && _session?.State is not null)
            {
                Draw(_session.State);
            }
        }

        /// <summary>Whether the screen is currently up — both doors' poll (rule 5).</summary>
        public bool IsOpen => _root != null && _root.alpha > 0f;

        /// <summary>
        /// Whether this run's class has a tree at all — rule 2's answer, and what both doors read to
        /// decide whether to offer their button.
        /// </summary>
        /// <remarks>
        /// Cheap after the first successful resolve, because both doors ask it once a frame: a run
        /// cannot change class, so the catalog is probed once and the answer is a field read
        /// afterwards. False before the run has started, which is honest rather than convenient —
        /// there is no tree to view yet either.
        /// </remarks>
        public bool HasTree => Resolve() is not null;

        /// <param name="session">
        /// The run, for the two reads a cell needs — what is taken and what is available. Not the
        /// tree itself: <c>RunState.Tree</c> is <c>internal</c> and hands out narrow reads
        /// (AR §18.2), which is the whole reason <c>IsNodeAvailable</c> exists.
        /// </param>
        /// <param name="catalog">
        /// What the tree <em>is</em> and what a node <em>is</em>. <c>RunState</c> hands out ids, so
        /// the shape, the keys and the kinds are looked up here — <c>LevelUpPresenter</c>'s bargain,
        /// two screens on.
        /// </param>
        /// <param name="progression">
        /// <c>TakeNode</c>, and nothing else of it — a free run's tap (UI-03c rule 4).
        /// </param>
        /// <param name="pause">The gate the star's <see cref="Open()"/> holds (UI-03c rule 1).</param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        /// <remarks>
        /// <b>No <c>DomainEventHub</c>, and that is rule 10 at the constructor</b>: this screen
        /// renders no events, so there is nothing to subscribe to and nothing to drop. The one
        /// handler wired here is the Close button's, for <c>PausePresenter.Construct</c>'s reason —
        /// <c>RunScope</c> builds its container from its own <c>Awake</c> and Unity orders no two of
        /// those.
        /// </remarks>
        /// <param name="localizer">
        /// What each node and each branch is called. Held here and handed to every cell on
        /// <c>TreeNodeButton.Show</c>, because a cell is a runtime clone nothing injects individually
        /// (M3-14a rule 11) — and used directly for the three column headings, which are this
        /// class's own.
        /// </param>
        /// <param name="icons">Each skill's icon, for its tile (UI-04c rule 3).</param>
        /// <param name="looks">The class's emblem, for the plate under the block (rule 8).</param>
        [Inject]
        public void Construct(
            IRunSession session,
            IProgressionCommands progression,
            RunPause pause,
            ContentCatalog catalog,
            ILocalizer localizer,
            SkillIconBook icons,
            CharacterLookBook looks)
        {
            _icons = icons ?? throw new ArgumentNullException(nameof(icons));
            _looks = looks ?? throw new ArgumentNullException(nameof(looks));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
            _pause = pause ?? throw new ArgumentNullException(nameof(pause));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

            Wire(_close, Close);

            _card?.Bind(Learn);
        }

        /// <exception cref="MissingReferenceException">The root, a column, the template or Close is not dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this presenter.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>HudPresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// injected" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (_root == null || _cellTemplate == null || _block == null || _emptyLabel == null || _close == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(TreeViewPresenter)} is missing its root, its cell template, its block, its empty " +
                    "label or its Close button. A tree view that is only partly dressed opens over " +
                    "a stopped game, shows nothing, and cannot be closed — which is " +
                    "indistinguishable from a crash.");
            }

            if (!HasColumns())
            {
                throw new MissingReferenceException(
                    $"{nameof(TreeViewPresenter)} has fewer than {SkillTreeSpec.BranchCount} " +
                    "branch columns or labels assigned. Every class has exactly three branches " +
                    "(CH §5: identical skeleton for every class, so the UI is built once), and a " +
                    "missing column is a third of the tree that is never drawn.");
            }

            if (_session is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(TreeViewPresenter)} was never injected, so it would draw nothing and " +
                    "neither door would offer its button. The component is registered by RunScope " +
                    "— drag this object onto its Tree View Presenter field.");
            }

            // The Close label, once, here (M3-14c rule 3) — through Resolve rather than through a
            // Write of its own, because this file already has that method and two would be the
            // drift the helper exists to prevent. The empty label is not written here for the
            // reason it never was: Draw owns it, because *whether it is shown* is a function of
            // whether this class has a tree at all.
            if (_closeLabel != null)
            {
                _closeLabel.text = Resolve(CloseKey);
            }

            // Down whatever the prefab was left dressed as, so a screen someone was editing cannot
            // ship covering the arena — HudPresenter's argument, for the fifth screen.
            _cellTemplate.gameObject.SetActive(false);

            HideScreen();
        }

        /// <remarks>
        /// <b>It drops one button handler and the Back action.</b> There is no subscription (rule 10),
        /// and a <c>Tree</c> pause still held is given back by <c>RunPause.Dispose</c> with the
        /// scope — <c>PausePresenter.OnDestroy</c>'s bargain for <c>Menu</c>.
        /// <para>
        /// <b>It does not run the callback.</b> A screen destroyed with a door waiting on it is
        /// exactly the case M3-09b's poll was written for, and the door's own once-a-frame read of
        /// <see cref="IsOpen"/> is what gives the panel or the cards back — invoking the callback
        /// from a teardown would mean a door coming back on a frame the scope may already be
        /// disposing.
        /// </para>
        /// </remarks>
        private void OnDestroy()
        {
            _onClosed = null;

            Unwire(_close, Close);

            if (_back != null)
            {
                _back.performed -= OnBack;
                _back.Dispose();
                _back = null;
            }
        }

        /// <summary>
        /// The level star's door (UI-03c rule 1): draws the tree and holds <c>PauseReason.Tree</c>
        /// until <see cref="Close"/>. Opens at 0 points too, to look.
        /// </summary>
        /// <remarks>
        /// <b>Refused, quietly, when anything else holds the pause</b> — a level-up, the panel, the
        /// Sanctum. <c>RunPause.Pause</c> would throw for a second holder, and a tap that threw is a
        /// dead button at best; <c>PausePresenter.Open</c>'s guard, for its reason.
        /// </remarks>
        public void Open() => OpenHoldingPause(null);

        /// <summary>
        /// The pause panel's door in a free run (UI-03d rule 2): holds <c>PauseReason.Tree</c> like
        /// <see cref="Open()"/>, and runs <paramref name="onClosed"/> after <see cref="Close"/>.
        /// </summary>
        /// <remarks>
        /// <b>The caller must have released its own pause first</b>, in the same call, so
        /// <c>RunPause</c> never holds two and the run never ticks between them. Refused like
        /// <see cref="Open()"/> when anything still holds the pause; check <see cref="IsOpen"/>
        /// afterwards to learn which.
        /// </remarks>
        public void OpenHoldingPause(Action onClosed)
        {
            if (IsOpen || _pause is null || _pause.IsPaused || _session?.State is null)
            {
                return;
            }

            _pause.Pause(PauseReason.Tree);

            Open(onClosed);
        }

        /// <summary>
        /// Draws the tree and puts the screen up. <paramref name="onClosed"/> is how the door that
        /// opened it gets itself back (rule 5).
        /// </summary>
        /// <param name="onClosed">
        /// Run once, by <see cref="Close"/>, after the screen is down. Null is legal and means a
        /// caller with nothing to restore.
        /// </param>
        /// <remarks>
        /// <b>A second open is refused rather than re-pointed.</b> Both doors already check
        /// <see cref="IsOpen"/> before calling, so this is belt and braces — but the failure it
        /// refuses is the one that matters: replacing a live callback would strand whichever door is
        /// currently waiting, which is a panel of dead buttons over a stopped game.
        /// </remarks>
        public void Open(Action onClosed)
        {
            if (IsOpen)
            {
                return;
            }

            RunState state = _session?.State;

            if (state is null)
            {
                // A screen in a scene where no run has begun, which is a workflow rather than a
                // fault — HudPresenter's argument, reached by pressing Play with the Run scene open.
                return;
            }

            _onClosed = onClosed;
            _selected = default;

            Draw(state);
            ShowScreen();
        }

        /// <summary>
        /// Takes the screen down and hands the door back. The Close button's handler, and what each
        /// door calls when it goes away underneath this one.
        /// </summary>
        /// <remarks>
        /// <b>The screen goes down before the callback runs</b>, so the door restoring itself reads
        /// <see cref="IsOpen"/> as false — <c>PausePresenter.RefreshPanel</c> is exactly that read,
        /// and running it a line earlier would give the panel back inert. The callback is cleared
        /// before it is invoked for <c>OfferCard.Raise</c>'s reason: a handler that re-entered would
        /// otherwise be handed the same delegate twice.
        /// </remarks>
        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            HideScreen();

            _selected = default;

            // Only the star's own pause: a door's Menu or LevelUp is that door's to give back.
            if (_pause != null && _pause.Holder == PauseReason.Tree)
            {
                _pause.Resume(PauseReason.Tree);
            }

            Action onClosed = _onClosed;

            _onClosed = null;

            onClosed?.Invoke();
        }

        /// <summary>
        /// Every node of the tree on a cell, in tree order, from two reads (rules 2, 3, 4).
        /// </summary>
        private void Draw(RunState state)
        {
            SkillTreeSpec tree = Resolve();

            WriteBranchLabels(tree);

            // Written on every draw rather than in Start, which EditMode never runs (Traps §5).
            if (_titleLabel != null)
            {
                _titleLabel.text = Resolve(TitleKey);
            }

            // Read here, after any TakeNode has returned — never in a NodeTaken handler, which core
            // publishes before it spends the point (UI-03b's As built 2).
            bool free = state.IsFreePick;
            int points = free ? state.SkillPoints : 0;

            WritePoints(points);

            // Before Arrange, which reads the block's size.
            PlaceChrome();

            if (_emptyLabel != null)
            {
                // Written rather than left to the prefab — SkillsPresenter.EmptyKey's reason, and
                // the same bargain: this class already holds a field for this label, so resolving it
                // costs a line. Close still draws ui.tree.close, because that one has no field.
                _emptyLabel.text = Resolve(NoTreeKey);

                _emptyLabel.gameObject.SetActive(tree is null);
            }

            if (tree is null)
            {
                // Rule 2: no cells at all rather than an empty grid. Neither door offers its button
                // in this state, so this is the undressed-scene path rather than a shipped one.
                HideCells();

                if (_card != null)
                {
                    _card.gameObject.SetActive(false);
                }

                return;
            }

            Build(tree);

            if (_block != null)
            {
                float px = PixelsPerDp();

                _block.Arrange(tree, _cells, _branchColumns, _branchLabels, px, CellHeight * px, (_compact ? Mathf.Min(CellGap, CompactCellGapDp) : CellGap) * px);

                if (_portrait && !_compact)
                {
                    FitBlockToContent(px);
                }
            }

            int index = 0;

            SkillSpec selectedSpec = null;
            Sprite selectedIcon = null;
            NodeState selectedState = NodeState.Locked;
            int selectedTier = 0;
            int selectedTiers = 0;

            for (int b = 0; b < tree.Branches.Count; b++)
            {
                SkillBranchSpec branch = tree.Branches[b];

                for (int t = 1; t <= branch.TierCount; t++)
                {
                    IReadOnlyList<ContentId> tier = branch.Tier(t);

                    for (int i = 0; i < tier.Count; i++)
                    {
                        if (index >= _cells.Count)
                        {
                            // Unreachable: the pool is built by this same walk over the same tree,
                            // and a run cannot change class. Bounded anyway, because the two
                            // disagreeing would be an IndexOutOfRange on a stopped game.
                            return;
                        }

                        ContentId id = tier[i];
                        SkillSpec spec = _catalog.Skill(id);
                        NodeState nodeState = StateOf(state, id);
                        Sprite icon = IconOf(id);

                        // Rule 3 and rule 7 in one flag: a drawing run's cells are never takeable.
                        _cells[index].Show(spec, nodeState, icon, t, points > 0);
                        _cells[index].SetSelected(id == _selected);

                        if (id == _selected)
                        {
                            selectedSpec = spec;
                            selectedIcon = icon;
                            selectedState = nodeState;
                            selectedTier = t;
                            selectedTiers = branch.TierCount;
                        }

                        index++;
                    }
                }
            }

            for (int i = index; i < _cells.Count; i++)
            {
                _cells[i].Hide();
            }

            if (_block != null)
            {
                _block.Show(
                    tree,
                    id => StateOf(state, id),
                    _localizer,
                    _looks?.For(state.CharacterId).Emblem,
                    _catalog.Character(state.CharacterId).NameKey);
            }

            DrawCard(selectedSpec, selectedIcon, selectedState, selectedTier, selectedTiers, free, points > 0);
        }

        /// <summary>The card under the block: the selected node, or the hint (UI-04d rules 2, 3).</summary>
        private void DrawCard(SkillSpec spec, Sprite icon, NodeState nodeState, int tier, int tiers, bool free, bool canLearn)
        {
            if (_card == null)
            {
                return;
            }

            _card.gameObject.SetActive(true);
            _card.Layout(PixelsPerDp(), _compact);

            if (spec is null || _localizer is null)
            {
                _card.ShowHint(_localizer);

                return;
            }

            _card.Show(spec, icon, nodeState, tier, tiers, canLearn, _localizer, free);
        }

        /// <summary>The node's icon from the book, else the block's placeholder (UI-04c rule 3).</summary>
        private Sprite IconOf(ContentId id)
        {
            Sprite icon = _icons?.For(id);

            return icon != null ? icon : _block != null ? _block.Placeholder : null;
        }

        /// <summary>
        /// A cell's tap (UI-04d rule 1): selects the node, in any state, and sends nothing.
        /// </summary>
        private void OnCellTapped(ContentId id)
        {
            RunState state = _session?.State;

            if (!IsOpen || state is null)
            {
                return;
            }

            _selected = id;

            Draw(state);
        }

        /// <summary>
        /// The card's Learn (UI-04d rule 5): one point on the selected node, then the screen redrawn
        /// with the selection kept.
        /// </summary>
        /// <remarks>
        /// Every condition the card already checked is asked of core again, because
        /// <c>TakeNode</c> throws for a refused node and a stale card is one frame from being one.
        /// Spending the last point leaves the screen up; a full tree's leftover points are the
        /// ticker's <c>SettleFullTree</c>, as before.
        /// </remarks>
        private void Learn()
        {
            RunState state = _session?.State;

            if (!IsOpen || state is null || _progression is null || _selected == default
                || !state.IsFreePick || state.SkillPoints <= 0 || !state.IsNodeAvailable(_selected))
            {
                return;
            }

            _progression.TakeNode(_selected);

            Draw(state);
        }

        /// <summary>The pill's count, tinted while there is a point to spend (UI-04b rule 3).</summary>
        private void WritePoints(int points)
        {
            if (_pointsLabel == null)
            {
                return;
            }

            // Always shown (UI-04b rule 5): a drawing run's points are 0, passed in by Draw.
            _pointsLabel.text = _localizer is null ? PointsKey.ToString() : _localizer.Format(PointsKey, points);
            _pointsLabel.color = points > 0 ? Palette.Available : Color.white;
        }

        /// <summary>
        /// The four answers for one node, from the three reads <c>RunState</c> hands out.
        /// </summary>
        /// <remarks>
        /// <b>Taken is asked first and by a scan</b>, because <c>RunState</c> has no
        /// <c>IsNodeTaken</c> and M3-09d adds exactly one read rather than two: a taken node is
        /// never <em>available</em> (<c>SkillTree.Check</c> refuses it at the first gate), so the
        /// scan and the read cannot both answer true and the order is about cost rather than
        /// correctness. Twenty-seven nodes against at most twenty-seven taken ids is a walk of a
        /// list on a frame nothing is ticking — AR §14 permits it here for
        /// <c>SkillRow.WriteTrigger</c>'s reason, and a <c>HashSet</c> built per open would allocate
        /// to save a comparison nobody is timing.
        /// <para>
        /// <b>Banished is the fourth answer, and the order of the checks still cannot matter</b>
        /// (M6-03b rule 9): <c>SkillTree.Banish</c> refuses a taken node and <c>Check</c> answers
        /// closed for a banished one (M6-02b rules 4, 5), so no id is ever two of taken, banished and
        /// available. A second scan of the same shape — at most the banished count per cell.
        /// </para>
        /// </remarks>
        private static NodeState StateOf(RunState state, ContentId id)
        {
            IReadOnlyList<ContentId> taken = state.TakenNodeIds;

            for (int i = 0; i < taken.Count; i++)
            {
                if (taken[i] == id)
                {
                    return NodeState.Taken;
                }
            }

            IReadOnlyList<ContentId> banished = state.BanishedNodeIds;

            for (int i = 0; i < banished.Count; i++)
            {
                if (banished[i] == id)
                {
                    return NodeState.Banished;
                }
            }

            // CH §5's gating, asked of core rather than re-derived here — the whole reason
            // RunState.IsNodeAvailable was added (rule 3).
            return state.IsNodeAvailable(id) ? NodeState.Available : NodeState.Locked;
        }

        /// <summary>
        /// Clones one cell per node under its own branch's column, once (rule 4).
        /// </summary>
        /// <remarks>
        /// Built in tree order and never re-parented, so a cell's index is its position in
        /// <c>SkillTree</c>'s own walk for the life of the run. A tree with more nodes than the pool
        /// cannot happen — a run has one class and one tree — so there is no growth path here and
        /// no branch of code for one.
        /// </remarks>
        private void Build(SkillTreeSpec tree)
        {
            if (_cells.Count > 0 || _cellTemplate == null)
            {
                return;
            }

            for (int b = 0; b < tree.Branches.Count; b++)
            {
                Transform parent = ColumnOf(b);
                SkillBranchSpec branch = tree.Branches[b];

                for (int t = 1; t <= branch.TierCount; t++)
                {
                    IReadOnlyList<ContentId> tier = branch.Tier(t);

                    for (int i = 0; i < tier.Count; i++)
                    {
                        TreeNodeButton cell = Instantiate(_cellTemplate, parent);

                        cell.name = $"Cell{b}_{t}_{i}";
                        cell.Bind(OnCellTapped);
                        cell.Hide();

                        _cells.Add(cell);
                    }
                }
            }
        }

        /// <summary>
        /// The header, the block under it and every word on the screen, in dp (UI-03c rules 5, 9).
        /// </summary>
        /// <remarks>
        /// The prefab authored these in reference units of a 1920×1080 landscape canvas, which on a
        /// 9:20 phone ran the header past the safe area while the cells, already in dp, shrank to
        /// fit. Placed here so the whole screen is one unit.
        /// The columns hang from the header's bottom edge, so a cell can never run under it.
        /// </remarks>
        private void PlaceChrome()
        {
            float px = PixelsPerDp();
            Rect safeRect = _frame != null && _frame.parent is RectTransform safeArea ? safeArea.rect : default;

            // An unmeasured safe area keeps the landscape layout, as it keeps the full card.
            _portrait = float.IsFinite(safeRect.width) && safeRect.width > 0f && safeRect.height > safeRect.width;

            float margin = (_portrait ? PortraitSideDp : HeaderMarginDp) * px;
            float header = HeaderHeightDp * px;

            float top = FrameTopMarginDp * px;
            float gap = PieceGapDp * px;
            float closeSize = CloseSizeDp * px;
            float pillWidth = PillWidthDp * px;
            float ribbonWidth = RibbonWidthDp * px;
            float ribbonHeight = RibbonHeightDp * px;

            // UI-04b rule 1: the frame fills the safe area less its margins.
            if (_frame != null)
            {
                _frame.anchorMin = Vector2.zero;
                _frame.anchorMax = Vector2.one;
                _frame.offsetMin = new Vector2(margin, margin);
                _frame.offsetMax = new Vector2(-margin, -top);

                SetBorder(_frame, PanelBorderPx, FrameBorderDp * px);
            }

            // The header pieces sit on the frame's top edge: Close and the pill from the right, the
            // ribbon centred unless the pill would run under it (rules 2 to 4).
            float closeX = -(margin + gap + (closeSize / 2f));
            float pillRight = -(margin + gap + closeSize + gap);

            if (_close != null && _close.transform is RectTransform close)
            {
                PlaceOnEdge(close, 1f, 0.5f, new Vector2(closeSize, closeSize), new Vector2(closeX, -top));
            }

            if (_closeLabel != null)
            {
                _closeLabel.fontSize = HeaderTextDp * px;
            }

            if (_pill != null)
            {
                PlaceOnEdge(_pill, 1f, 1f, new Vector2(pillWidth, PillHeightDp * px), new Vector2(pillRight, -top));
                SetBorder(_pill, PillBorderPx, PillHeightDp * px / 2f);
            }

            if (_pillIcon != null)
            {
                float iconSize = PillIconDp * px;

                _pillIcon.anchorMin = new Vector2(0f, 0.5f);
                _pillIcon.anchorMax = new Vector2(0f, 0.5f);
                _pillIcon.pivot = new Vector2(0f, 0.5f);
                _pillIcon.sizeDelta = new Vector2(iconSize, iconSize);
                _pillIcon.anchoredPosition = new Vector2(PillPadDp * px, 0f);
            }

            if (_pointsLabel != null)
            {
                RectTransform count = _pointsLabel.rectTransform;

                count.anchorMin = Vector2.zero;
                count.anchorMax = Vector2.one;
                count.offsetMin = new Vector2((PillPadDp + PillIconDp + PieceGapDp) * px, 0f);
                count.offsetMax = new Vector2(-PillPadDp * px, 0f);
                _pointsLabel.fontSize = PillTextDp * px;
                _pointsLabel.alignment = TextAlignmentOptions.Center;
            }

            if (_ribbon != null)
            {
                float width = _ribbon.parent is RectTransform safe ? safe.rect.width : 0f;
                float shift = 0f;

                if (float.IsFinite(width) && width > 0f)
                {
                    // Keep the ribbon's right edge a gap short of the pill's left one.
                    float pillLeftFromRight = -pillRight + pillWidth + gap;

                    shift = Mathf.Min(0f, (width / 2f) - pillLeftFromRight - (ribbonWidth / 2f));
                }

                PlaceOnEdge(_ribbon, 0.5f, 0.5f, new Vector2(ribbonWidth, ribbonHeight), new Vector2(shift, -top));
                SetBorder(_ribbon, RibbonBorderPx, ribbonHeight * RibbonBorderPx / RibbonSpriteHeightPx);
            }

            if (_titleLabel != null)
            {
                RectTransform title = _titleLabel.rectTransform;

                title.anchorMin = Vector2.zero;
                title.anchorMax = Vector2.one;
                title.offsetMin = new Vector2(RibbonTextInsetDp * px, 0f);
                title.offsetMax = new Vector2(-RibbonTextInsetDp * px, 0f);
                _titleLabel.fontSize = RibbonTextDp * px;
                _titleLabel.alignment = TextAlignmentOptions.Center;
            }

            if (_emptyLabel != null)
            {
                RectTransform empty = _emptyLabel.rectTransform;

                empty.anchorMin = new Vector2(0f, 0.5f);
                empty.anchorMax = new Vector2(1f, 0.5f);
                empty.sizeDelta = new Vector2(-2f * margin, header);
                _emptyLabel.fontSize = HeaderTextDp * px;
            }

            // The block fills the frame's content area, less the room UI-04d's card takes under
            // it (UI-04c rule 10); everything inside it is the block's to place.
            if (_block != null && _block.transform is RectTransform grid)
            {
                float inset = margin + (ContentInsetDp * px);
                float safeHeight = safeRect.height;

                // A safe area too short for the block over the full card gets the compact one
                // (UI-07d); an unmeasured one keeps the full card.
                _compact = float.IsFinite(safeHeight) && safeHeight > 0f && safeHeight / px < CompactBelowDp;
                _block.Compact = _compact;

                float bottom = _compact ? margin + (CompactBottomDp * px) : inset;
                float cardHeight = (_compact ? TreeDetailCard.CompactHeightDp : TreeDetailCard.HeightDp) * px;
                float above = _compact
                    ? bottom + cardHeight + (CompactGapDp * px)
                    : inset + (CardSpaceDp * px);
                float below = _compact
                    ? top + (ribbonHeight / 2f) + (CompactTopGapDp * px)
                    : top + (ribbonHeight / 2f) + margin;

                if (_card != null && _card.transform is RectTransform card)
                {
                    card.anchorMin = new Vector2(0f, 0f);
                    card.anchorMax = new Vector2(1f, 0f);
                    card.pivot = new Vector2(0.5f, 0f);
                    card.offsetMin = new Vector2(inset, bottom);
                    card.offsetMax = new Vector2(-inset, bottom + cardHeight);
                }

                grid.anchorMin = Vector2.zero;
                grid.anchorMax = Vector2.one;
                grid.offsetMin = new Vector2(inset, above);
                grid.offsetMax = new Vector2(-inset, -below);
            }
        }

        /// <summary>
        /// In portrait, shrinks the block's rect to what <c>TreeBlockView.Arrange</c> drew in it and
        /// centres that in the band between the header and the card (UI-08e rule 2).
        /// </summary>
        /// <remarks>
        /// A tall phone leaves the band twice the height the tiles need, and the block hangs
        /// everything from its top edge, so without this the class plate ends half a screen above the
        /// card. Measured from the block's own children rather than re-derived, so the block's
        /// arithmetic stays in <c>TreeBlockView</c>; everything in it is anchored to its top, so
        /// moving both edges by the same amount moves the drawing and does not re-flow it.
        /// </remarks>
        private void FitBlockToContent(float px)
        {
            if (_block == null || _block.transform is not RectTransform grid)
            {
                return;
            }

            Rect band = grid.rect;
            float lowest = band.yMax;
            var corners = new Vector3[4];

            for (int i = 0; i < grid.childCount; i++)
            {
                if (grid.GetChild(i) is not RectTransform child || !child.gameObject.activeSelf)
                {
                    continue;
                }

                child.GetWorldCorners(corners);

                for (int c = 0; c < corners.Length; c++)
                {
                    lowest = Mathf.Min(lowest, grid.InverseTransformPoint(corners[c]).y);
                }
            }

            float content = band.yMax - lowest;

            if (!float.IsFinite(content) || content <= 0f || content >= band.height - (PortraitCardGapDp * px))
            {
                return;
            }

            float spare = (band.height - content) / 2f;

            grid.offsetMin = new Vector2(grid.offsetMin.x, grid.offsetMin.y + spare);
            grid.offsetMax = new Vector2(grid.offsetMax.x, grid.offsetMax.y - spare);
        }

        /// <summary>
        /// A header piece hung from the safe area's top edge at horizontal anchor
        /// <paramref name="anchorX"/>, vertically centred on <paramref name="position"/>'s y.
        /// </summary>
        private static void PlaceOnEdge(RectTransform rect, float anchorX, float pivotX, Vector2 size, Vector2 position)
        {
            rect.anchorMin = new Vector2(anchorX, 1f);
            rect.anchorMax = new Vector2(anchorX, 1f);
            rect.pivot = new Vector2(pivotX, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        /// <summary>
        /// Draws a sliced sprite's border <paramref name="wanted"/> canvas units wide: the border is
        /// <paramref name="borderPx"/> sprite pixels, so this is the multiplier that scales it to dp.
        /// </summary>
        private static void SetBorder(RectTransform rect, float borderPx, float wanted)
        {
            var image = rect.GetComponent<Image>();

            if (image != null && wanted > 0f)
            {
                image.pixelsPerUnitMultiplier = borderPx / wanted;
            }
        }

        private void WriteBranchLabels(SkillTreeSpec tree)
        {
            if (_branchLabels is null)
            {
                return;
            }

            for (int b = 0; b < _branchLabels.Length; b++)
            {
                if (_branchLabels[b] == null)
                {
                    continue;
                }

                // English, as of M3-14a — the three keys rule 7 names beside the twenty-four node
                // ones. Still the reason SkillBranchSpec refuses a default(LocKey): a branch with no
                // key would head a column with a blank rather than with a word.
                _branchLabels[b].text = tree is not null && b < tree.Branches.Count
                    ? Resolve(tree.Branches[b].NameKey)
                    : string.Empty;
            }
        }

        /// <summary>
        /// <paramref name="key"/> in words, or its own text when nothing injected this presenter.
        /// </summary>
        /// <remarks>
        /// Guarded rather than assumed because <see cref="Draw"/> is reachable from a fixture that
        /// never called <see cref="Construct"/>, and a heading that threw would take the whole screen
        /// with it. The fallback is <c>ToString()</c> and not <c>Key</c> for
        /// <c>TableLocalizer.Get</c>'s reason — a <c>default(LocKey)</c>'s <c>Key</c> is null.
        /// </remarks>
        private string Resolve(LocKey key) =>
            _localizer is null ? key.ToString() : _localizer.Get(key);

        private void HideCells()
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i] != null)
                {
                    _cells[i].Hide();
                }
            }
        }

        /// <summary>This run's tree, resolved once — null for a class with none (rule 2).</summary>
        private SkillTreeSpec Resolve()
        {
            if (_resolved)
            {
                return _tree;
            }

            RunState state = _session?.State;

            if (state is null || _catalog is null)
            {
                // Not an answer yet, so nothing is cached: the run has not started and the class it
                // will be played as is not decided here.
                return null;
            }

            _catalog.TryGetTreeFor(state.CharacterId, out _tree);
            _resolved = true;

            return _tree;
        }

        private bool HasColumns() =>
            _branchColumns is not null
            && _branchColumns.Length >= SkillTreeSpec.BranchCount
            && _branchLabels is not null
            && _branchLabels.Length >= SkillTreeSpec.BranchCount;

        /// <summary>Where branch <paramref name="branch"/>'s cells hang.</summary>
        /// <remarks>
        /// The template's own parent is the fallback rather than this object's transform, for
        /// <c>SkillsPresenter.EnsureRows</c>' reason: a screen whose columns were re-parented in the
        /// prefab still puts its cells somewhere a layout can see them.
        /// </remarks>
        private Transform ColumnOf(int branch)
        {
            RectTransform column = ColumnRect(branch);

            return column != null ? column : _cellTemplate.transform.parent;
        }

        private RectTransform ColumnRect(int branch) =>
            _branchColumns is not null && branch >= 0 && branch < _branchColumns.Length
                ? _branchColumns[branch]
                : null;

        /// <remarks>
        /// Alpha 1 on the same call, with no tween and no coroutine. The raycast block goes on with
        /// it: the canvas is full-screen, so whichever screen is underneath is covered rather than
        /// disabled — and both doors take their own buttons inert anyway, because belt and braces is
        /// what M3-09a's icon guard is made of.
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

            if (_back is null)
            {
                _back = new InputAction("TreeBack", InputActionType.Button, BackBinding);
                _back.performed += OnBack;
            }

            _back.Enable();
        }

        private void OnBack(InputAction.CallbackContext context) => Close();

        private void HideScreen()
        {
            if (_root == null)
            {
                return;
            }

            _root.alpha = 0f;
            _root.blocksRaycasts = false;
            _root.interactable = false;

            _back?.Disable();
        }

        /// <summary><see cref="_cellHeightDp"/> with the Inspector field's mistakes answered.</summary>
        private float CellHeight =>
            float.IsFinite(_cellHeightDp) && _cellHeightDp > 0f ? _cellHeightDp : DefaultCellHeightDp;

        /// <summary><see cref="_cellGapDp"/> with the same door, and a zero that is legal.</summary>
        private float CellGap =>
            float.IsFinite(_cellGapDp) && _cellGapDp >= 0f ? _cellGapDp : 0f;

        /// <summary>
        /// How many canvas units one dp is worth — <c>HudPresenter.PixelsPerDp</c>, for its reason.
        /// </summary>
        private float PixelsPerDp()
        {
            if (_pxPerDpOverride > 0f)
            {
                return _pxPerDpOverride;
            }

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
