using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and TreeView.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// CH §5.1's <em>"View Tree"</em>: the whole tree in three columns with the player's path
    /// highlighted, opened from the pause panel or from the level-up screen, read-only. GD §13.1,
    /// §16.4.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read-only, and that is the design rather than a simplification</b> (rule 1). CH §5.1 chose
    /// random-from-available <em>over</em> free-pick precisely so a level-up is a two-second
    /// decision, and a tree you could tap to buy from would be the free-pick screen the design
    /// refused, arriving through the back door. Nothing here sends a command and this class takes no
    /// <c>IProgressionCommands</c> and no <c>IPlayerCommands</c>: a screen that cannot spend a pick
    /// cannot be argued into spending one. <c>TreeNodeView</c> carries no <see cref="Button"/>
    /// either, so the refusal is structural at both ends and a row pins it.
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
    /// <b>It holds no pause and must not.</b> <c>RunPause</c> holds one reason at a time (M3-08a
    /// rule 12) and is already held by whichever screen the player opened first —
    /// <c>PauseReason.Menu</c> from the panel, <c>PauseReason.LevelUp</c> from the ticker — so a
    /// screen raised over a screen raised over a stopped game is one gate rather than three
    /// (M3-09b rule 10, for the second time). <b>The level-up screen does not lower its pause to
    /// show this either</b>: a tree view that resumed the game underneath itself would be the
    /// opposite of what a planning screen is for. <c>RunPause</c> is deliberately not a dependency
    /// of this class and a row pins its absence at the constructor.
    /// </para>
    /// <para>
    /// <b>It renders no events at all</b> (rule 10), and declares no <c>Update</c> — which is the
    /// strongest way to say it, <c>SkillsPresenter</c>'s phrasing. The tree cannot change while this
    /// is up: the tick is gated on both doors, and the one thing that could change it — taking a
    /// node — is the screen underneath, which is hidden. Built on open from reads.
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
    /// <b>No scroll and no zoom</b> (rule 9). Three columns of at most eight tiers is a fixed grid
    /// that either fits a landscape safe area or does not, and finding out is a device question
    /// (ledger row 4). Cells shrink to fit their column, so the honest failure mode is <em>small</em>
    /// rather than <em>clipped</em>; M7-04's eighty-one nodes are when a tree earns a pinch-zoom.
    /// </para>
    /// <para>
    /// <b>Close is a button and the Android back gesture is not wired</b> (rule 11). The app has
    /// never run outside the Editor (ledger row 4) and <c>InputAdapter</c> carries no back binding;
    /// adding one for this screen alone would be the first of three inconsistent answers. Named
    /// rather than forgotten.
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
        private const float DefaultCellHeightDp = 44f;

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
        [SerializeField] private TreeNodeView _cellTemplate;

        [Tooltip("\"This class has no skill tree\", as a LocKey. Shown instead of the columns for a " +
                 "class with none, because an empty screen and a broken screen look identical.")]
        [SerializeField] private TMP_Text _emptyLabel;

        [Tooltip("Back to whichever door this was opened by. It lowers no pause — this screen " +
                 "never held one.")]
        [SerializeField] private Button _close;

        [Tooltip("\"Close\". Written from ui.tree.close in Start, so the prefab's own value is a " +
                 "placeholder — the empty label's pattern (M3-14c).")]
        [SerializeField] private TMP_Text _closeLabel;

        [Tooltip("How tall one tier's row wants to be, in dp. A ceiling rather than a size: a " +
                 "branch taller than its column shrinks to fit (rule 9). Applied at runtime for " +
                 "the reason HudPresenter applies its own — a Scale-With-Screen-Size canvas " +
                 "measures in reference pixels. A guess until a phone exists — ledger row 4.")]
        [SerializeField] private float _cellHeightDp = DefaultCellHeightDp;

        [Tooltip("The gap between two cells, in dp, both down a column and across a tier. A guess " +
                 "until a phone exists — ledger row 4.")]
        [Min(0f)]
        [SerializeField] private float _cellGapDp = 6f;

        private IRunSession _session;
        private ContentCatalog _catalog;

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
        private readonly List<TreeNodeView> _cells = new List<TreeNodeView>();

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
        /// <c>TreeNodeView.Show</c>, because a cell is a runtime clone nothing injects individually
        /// (M3-14a rule 11) — and used directly for the three column headings, which are this
        /// class's own.
        /// </param>
        [Inject]
        public void Construct(IRunSession session, ContentCatalog catalog, ILocalizer localizer)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

            Wire(_close, Close);
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
            if (_root == null || _cellTemplate == null || _emptyLabel == null || _close == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(TreeViewPresenter)} is missing its root, its cell template, its empty " +
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
        /// <b>It drops one button handler and nothing else.</b> There is no subscription (rule 10)
        /// and no pause to lower — this screen never held one — so unlike
        /// <c>PausePresenter.OnDestroy</c> there is no global here that something else has to
        /// restore.
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

                return;
            }

            Build(tree);
            Layout(tree);

            int index = 0;

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

                        _cells[index].Show(_catalog.Skill(id), StateOf(state, id), _localizer);

                        index++;
                    }
                }
            }

            for (int i = index; i < _cells.Count; i++)
            {
                _cells[i].Hide();
            }
        }

        /// <summary>
        /// Rule 3's three answers for one node, from the two reads <c>RunState</c> hands out.
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
                        TreeNodeView cell = Instantiate(_cellTemplate, parent);

                        cell.name = $"Cell{b}_{t}_{i}";
                        cell.Hide();

                        _cells.Add(cell);
                    }
                }
            }
        }

        /// <summary>
        /// Lays each branch out down its own column: one row per tier, the tier's nodes side by side
        /// (rule 4), shrinking to fit (rule 9).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Here rather than in the prefab for <c>HudPresenter.Place</c>'s two reasons, and a third
        /// this screen adds: a Scale-With-Screen-Size canvas measures in reference pixels, so a
        /// 44 dp cell authored as 44 of those is a different physical size on every phone; twenty-
        /// seven rects that have to agree about where each other are is twenty-seven chances to
        /// overlap; and the cells do not exist at author time at all, being clones made on first
        /// open.
        /// </para>
        /// <para>
        /// <b>Both float doors fall back to a value rather than leaving the layout alone, and that
        /// is the opposite of <c>PausePresenter.Place</c> on purpose.</b> There is no authored layout
        /// to fall back to: every cell is a clone that starts life on the template's rect, so
        /// "leave it alone" is twenty-seven cells stacked on top of each other. The two fall back
        /// differently because the fields differ — a cell <em>height</em> has no honest zero, so it
        /// takes <see cref="DefaultCellHeightDp"/> (<c>FirstActiveHint.Dwell</c>'s answer), while a
        /// <em>gap</em> does, so an unusable one is 0 and the grid is cells touching, which is a
        /// layout rather than a fault.
        /// </para>
        /// </remarks>
        private void Layout(SkillTreeSpec tree)
        {
            if (_cells.Count == 0)
            {
                return;
            }

            float pxPerDp = PixelsPerDp();
            float gapPx = CellGap * pxPerDp;
            float wantedPx = CellHeight * pxPerDp;

            int index = 0;

            for (int b = 0; b < tree.Branches.Count; b++)
            {
                SkillBranchSpec branch = tree.Branches[b];
                RectTransform column = ColumnRect(b);

                float rowPx = RowHeight(column, branch.TierCount, wantedPx, gapPx);

                for (int t = 1; t <= branch.TierCount; t++)
                {
                    IReadOnlyList<ContentId> tier = branch.Tier(t);

                    // A tier is a row and its nodes share that row's width in equal shares
                    // (M3-02a rule 7): CH §5's one-node-a-tier drawing is one case of this, not the
                    // shape the game ships.
                    float share = 1f / tier.Count;

                    for (int i = 0; i < tier.Count; i++)
                    {
                        if (index >= _cells.Count)
                        {
                            return;
                        }

                        if (_cells[index].transform is RectTransform rect)
                        {
                            rect.anchorMin = new Vector2(i * share, 1f);
                            rect.anchorMax = new Vector2((i + 1) * share, 1f);
                            rect.pivot = new Vector2(0.5f, 1f);

                            // Stretched across its share of the column and inset by the gap, so the
                            // column's own width is the prefab's business and never arithmetic here.
                            rect.sizeDelta = new Vector2(-gapPx, rowPx);
                            rect.anchoredPosition = new Vector2(0f, -(t - 1) * (rowPx + gapPx));
                        }

                        index++;
                    }
                }
            }
        }

        /// <summary>
        /// How tall one tier's row may be: what it wants, or what the column has room for (rule 9).
        /// </summary>
        /// <remarks>
        /// A column with no height yet — an undressed prefab, or a canvas that has never been
        /// through a layout pass — keeps the wanted height rather than collapsing to nothing, which
        /// is the difference between a screen that is too small and a screen that is not there.
        /// </remarks>
        private static float RowHeight(RectTransform column, int rows, float wantedPx, float gapPx)
        {
            if (column == null || rows <= 0)
            {
                return wantedPx;
            }

            float available = column.rect.height;

            if (!float.IsFinite(available) || available <= 0f)
            {
                return wantedPx;
            }

            float fitted = (available - ((rows - 1) * gapPx)) / rows;

            return float.IsFinite(fitted) && fitted > 0f && fitted < wantedPx ? fitted : wantedPx;
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
        }

        private void HideScreen()
        {
            if (_root == null)
            {
                return;
            }

            _root.alpha = 0f;
            _root.blocksRaycasts = false;
            _root.interactable = false;
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
