using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
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
    /// GD §16.1's <em>"auto-cast skills need visible cooldowns even though the player doesn't trigger
    /// them"</em>: a row of small radial fills under the health bar, one for every Active the player
    /// is <em>not</em> casting themselves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It exists because the default build is otherwise invisible, and that is GD §16.1's own
    /// argument</b> (M3-10b rule 4). Every skill starts on Auto (CC §6.1) and <em>"a player who never
    /// opens the menu has a complete, playable game with one button"</em> — which means a player who
    /// never opens the menu has every Active firing itself with no readout anywhere. This row is the
    /// only thing in M3 that makes an auto-cast build legible, and it matters more than the S1–S4
    /// buttons, which exist only for players who opted out of the default.
    /// </para>
    /// <para>
    /// <b>Membership is exactly the Actives that are <em>not</em> in a slot</b> (rule 5), walked as
    /// <see cref="RunState.OwnedActiveCount"/> → <see cref="RunState.SkillIdAt"/> →
    /// <see cref="RunState.IsAutoCast"/>. A skill switched to Manual leaves this row and gains a
    /// button (M3-10a); a skill switched back does the reverse. The two readouts are <b>disjoint by
    /// construction</b>, so nothing on screen ever shows one cooldown twice — which is the rule
    /// <c>HudPresenter</c> states for the Charge (<em>"a second readout here would be a second answer
    /// to when the player may dash"</em>), applied to the skills. <b>This class is the one that is
    /// allowed to ask</b>: <c>SkillBarPresenter</c> and <c>ManualSkillButton</c> are not, and
    /// <c>Bar_KnowsNothingAboutAuto</c> walks their IL to prove it (M3-10a rule 11).
    /// </para>
    /// <para>
    /// <b>Membership is rebuilt from two events and the fills are polled</b> (rule 6).
    /// <c>NodeTaken</c> with <c>Kind == Active</c> adds a cell and <c>SkillAutoCastChanged</c> moves
    /// one in or out — both carry what is needed (M3-03 rule 7, M3-07a rule 9). The fills are read
    /// every frame from <see cref="RunState.SkillCooldownFraction"/>, <c>SkillButton</c>'s bargain: a
    /// fill slides continuously and an event per frame is not an event.
    /// </para>
    /// <para>
    /// <b>The rebuild is deferred to the next <see cref="Update"/> rather than done in the handler,
    /// and that is a correctness fix rather than a batching one.</b> <c>LevelUpFlow.ChooseOffer</c>
    /// calls <c>SkillTree.Take</c> — which publishes <c>NodeTaken</c> — and hands the spec to
    /// <c>SkillRunner.Add</c> <em>afterwards</em>, so a handler reading
    /// <see cref="RunState.OwnedActiveCount"/> from inside the event sees the count from
    /// <em>before</em> the node. That is M3-09c's finding, and a row that rebuilt immediately would
    /// simply miss the cell it was told about until the next event arrived. One flag, one rebuild, at
    /// the point in the frame where the fills are read anyway — and <c>Update</c> runs at a
    /// <c>timeScale</c> of 0, so the row is correct before the level-up screen has even closed.
    /// </para>
    /// <para>
    /// <b>Sized for twelve and drawn for as many as there are</b> (rule 7).
    /// <see cref="SkillRunner.MaxActives"/> is 12 and CH §4's ~25 % of 27 is about seven; twelve 24 dp
    /// cells with 4 dp gaps is 332 dp, which fits a landscape safe area under a 320 dp health bar.
    /// <b>The cells are authored on <c>Hud.prefab</c> and deactivated, never instantiated during a
    /// run</b> — which is the opposite of <c>SkillsPresenter</c>, <c>TreeViewPresenter</c> and
    /// <c>LevelUpPresenter</c>, all of which clone a template. Twelve cells is a fixed, small,
    /// knowable number where twenty-seven tree nodes are not, and a row of cooldowns that hitched on
    /// the frame a skill was granted would hitch on exactly the frame the player is watching it.
    /// </para>
    /// <para>
    /// <b>A cell has no icon and no text, and that is ledger row 9's worst corner</b> (rule 8). There
    /// are no skill icons, and a 24 dp cell cannot hold <c>skill.oathbound.consecrate</c> — it cannot
    /// hold <em>"Consecrate"</em> either. So a cell is a radial fill and a kind tint, and which skill
    /// it is is not communicated at all until art exists. This is <b>worse than the card and the
    /// button</b>, where a key at least occupies the space its text will: here there is no space, so
    /// M3-14a's table does not fix it and only icons will.
    /// </para>
    /// <para>
    /// <b>Four kind tints, serialized, still not a palette</b> (rule 9). M3-08b rule 8's argument for
    /// the third time: <c>OfferCard</c> has four, <c>TreeNodeView</c> has four plus three states, and
    /// this row has four — the same values, so M3-13a inherits one answer to CH §4's four kinds
    /// rather than three that have quietly drifted. <b>Only <see cref="_active"/> is reachable in a
    /// live run</b>, because rule 5's membership admits Actives and nothing else; the lookup is total
    /// anyway, for <c>TreeNodeView.Frame</c>'s reason — a closed enum answered in one place beats a
    /// silent wrong colour if this row ever draws something else.
    /// </para>
    /// <para>
    /// <b>A nonsense dp field leaves the authored layout alone</b>, which is
    /// <c>SkillBarPresenter.Place</c>'s answer rather than <c>TreeViewPresenter.Layout</c>'s: these
    /// twelve cells <em>are</em> authored on <c>Hud.prefab</c>, at a real size under the health bar,
    /// so there is something honest to fall back to.
    /// </para>
    /// </remarks>
    public sealed class AutoCastRow : MonoBehaviour
    {
        /// <summary>GD §16.1's cell size, and ledger row 9's worst corner. CH §4.</summary>
        /// <remarks>
        /// A constant beside the field so the row that checks the prefab quotes rule 7 rather than a
        /// literal of its own — <c>SkillBarPresenter.MinimumSpacingDp</c>'s reason.
        /// </remarks>
        public const float CellSizeDp = 24f;

        /// <summary>The gap between two cells, in dp.</summary>
        public const float GapDp = 4f;

        [Tooltip("The twelve radial sweeps, left to right. Each Image must be Filled / Radial360 — " +
                 "fillAmount is the only thing written to it. Twelve because SkillRunner.MaxActives " +
                 "is twelve; a run owning three draws three and deactivates the rest.")]
        [SerializeField] private Image[] _fills = new Image[SkillRunner.MaxActives];

        [Tooltip("The twelve cell bodies, left to right, tinted by CH §4's kind — and each one is " +
                 "also the object that is switched off when there is no skill for it. Same order as " +
                 "the fills above.")]
        [SerializeField] private Image[] _kindStrips = new Image[SkillRunner.MaxActives];

        [Tooltip("How wide and tall one cell is, in dp. 24 is rule 7's number; whether a 24 dp " +
                 "radial fill reads as a cooldown or as a dot is a device question (ledger row 4).")]
        [SerializeField] private float _cellSizeDp = CellSizeDp;

        [Tooltip("The gap between two cells, in dp. Twelve cells and eleven gaps at these numbers " +
                 "is 332 dp, which fits under a 320 dp health bar.")]
        [SerializeField] private float _gapDp = GapDp;

        [Tooltip("Where the row's top-left corner sits, in dp from the safe area's left edge and " +
                 "from its top. Under the health bar (16 + 24 + 8), and left-aligned with it so the " +
                 "two read as one block.")]
        [SerializeField] private Vector2 _marginDp = new Vector2(16f, 48f);

        [Tooltip("Always on: a stat or a rule change, and about 45 % of a tree (CH §4). " +
                 "Placeholder until M3-13a's Palette — ledger row 6. Unreachable in this row, " +
                 "because rule 5 admits only Actives.")]
        [SerializeField] private Color _passive = new Color(0.62f, 0.66f, 0.72f);

        [Tooltip("Grants a skill with a cooldown (CH §4.2) — the only kind this row ever draws. " +
                 "Placeholder until M3-13a.")]
        [SerializeField] private Color _active = new Color(0.36f, 0.72f, 0.85f);

        [Tooltip("Improves a skill already owned. Placeholder until M3-13a. Unreachable here.")]
        [SerializeField] private Color _upgrade = new Color(0.45f, 0.78f, 0.55f);

        [Tooltip("Build-defining, end of a branch, three per class (CH §4). Placeholder until " +
                 "M3-13a. Unreachable here.")]
        [SerializeField] private Color _keystone = new Color(0.85f, 0.72f, 0.32f);

        private IRunSession _session;
        private ContentCatalog _catalog;

        private IDisposable _startedSubscription;
        private IDisposable _takenSubscription;
        private IDisposable _switchedSubscription;

        /// <summary>
        /// Which owned active each drawn cell is showing, by its index in the runner's walk order.
        /// Only the first <see cref="_cellCount"/> entries mean anything.
        /// </summary>
        /// <remarks>
        /// The runner's index rather than the skill's id, because
        /// <see cref="RunState.SkillCooldownFraction"/> is addressed by index — which is exactly the
        /// mapping M3-10a had to put in <c>RunState</c> for the <em>slot</em> case and does not have
        /// to here, since take order is the row's own order.
        /// </remarks>
        private readonly int[] _cellSkill = new int[SkillRunner.MaxActives];

        /// <summary>The last fraction written to each cell, so an idle frame costs no rebuild.</summary>
        /// <remarks>
        /// Seeded outside <c>[0, 1]</c>, like <c>ManualSkillButton._shownFraction</c>, so the first
        /// frame after a rebuild always draws — a cell whose skill has changed must not compare
        /// against what the previous one left behind.
        /// </remarks>
        private readonly float[] _shownFraction = new float[SkillRunner.MaxActives];

        /// <summary>How many cells are on screen — the first <em>n</em> of the twelve.</summary>
        private int _cellCount;

        /// <summary>Membership is stale and the next <see cref="Update"/> owes a rebuild.</summary>
        /// <remarks>
        /// Starts true, so a row whose run began before its <c>Start</c> is right on its first frame
        /// even if no event ever arrives.
        /// </remarks>
        private bool _dirty = true;

        /// <summary>How many cells are drawn right now. The one read a test needs.</summary>
        public int CellCount => _cellCount;

        /// <param name="session">
        /// The run, for the four narrow reads this row walks. Not the runner — <c>RunState</c> hands
        /// out scalar reads and keeps the handle (AR §18.2), and this row needed no twelfth read
        /// because M3-09b's and M3-10a's eleven already cover it.
        /// </param>
        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="catalog">
        /// What a skill <em>is</em>: <c>RunState</c> hands out ids, so the kind behind a tint is
        /// looked up here — <c>SkillBarPresenter</c>'s bargain, one readout over.
        /// </param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        [Inject]
        public void Construct(IRunSession session, DomainEventHub hub, ContentCatalog catalog)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            // Disposed before they are replaced, so a component injected twice — which VContainer
            // does not do and a test does — holds one subscription each rather than two.
            _startedSubscription?.Dispose();
            _startedSubscription = hub.Subscribe<RunStarted>(OnRunStarted);

            _takenSubscription?.Dispose();
            _takenSubscription = hub.Subscribe<NodeTaken>(OnNodeTaken);

            _switchedSubscription?.Dispose();
            _switchedSubscription = hub.Subscribe<SkillAutoCastChanged>(OnAutoCastChanged);
        }

        /// <exception cref="MissingReferenceException">Fewer than twelve cells are dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this row.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>HudPresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// injected" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (!HasCells())
            {
                throw new MissingReferenceException(
                    $"{nameof(AutoCastRow)} has fewer than {SkillRunner.MaxActives} cells assigned, " +
                    "or a cell is missing its fill. Rule 7 sizes this row for the runner's ceiling " +
                    "and drives it from the front, so a hole in the middle is a skill whose cooldown " +
                    "is simply invisible — which is the exact failure GD §16.1 built this row to fix.");
            }

            if (_session is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(AutoCastRow)} was never injected, so an auto-cast build would be " +
                    "invisible — GD §16.1's whole argument for this row. The component is " +
                    "registered by RunScope — drag this object onto its Auto Cast Row field.");
            }

            Place();

            // Rebuilt once immediately, because whether RunStarted has already been published
            // depends on the order VContainer's entry points and this component's Start happen to
            // run in. Either path lands here — HudPresenter's argument, for the eighth screen.
            Rebuild();
        }

        /// <remarks>
        /// Explicit rather than left to the hub's disposal, for <c>HudPresenter.OnDestroy</c>'s
        /// reason: a view destroyed before its scope would otherwise stay in three subscriber lists
        /// and be handed events for a component Unity has killed.
        /// </remarks>
        private void OnDestroy()
        {
            _startedSubscription?.Dispose();
            _startedSubscription = null;

            _takenSubscription?.Dispose();
            _takenSubscription = null;

            _switchedSubscription?.Dispose();
            _switchedSubscription = null;
        }

        /// <remarks>
        /// <c>Update</c>, not <c>LateUpdate</c>, for <c>ManualSkillButton</c>'s reason:
        /// <c>RunTicker</c> is an <c>ITickable</c> and so runs in the Update phase, and a cell one
        /// frame behind the cooldown it draws is exactly as wrong at the moment that matters — the
        /// instant the skill fires — as it is useful the rest of the time.
        /// </remarks>
        private void Update()
        {
            if (_dirty)
            {
                Rebuild();
            }

            DrawFills();
        }

        /// <remarks>
        /// The opening state, which no event can describe: a resumed run comes back owning whatever
        /// <c>TakenNodeIds</c> said (M3-03), and nothing publishes a <c>NodeTaken</c> for a restore.
        /// </remarks>
        private void OnRunStarted(RunStarted evt)
        {
            _dirty = true;
        }

        /// <summary>
        /// A node was taken: if it can fire, the row owes a cell (rule 6).
        /// </summary>
        /// <remarks>
        /// <b>A Passive is refused without a catalog lookup</b>, because <c>NodeTaken</c> carries the
        /// <c>Kind</c> (M3-03 rule 7) — and a tree is mostly passives, so most takes cost nothing
        /// here at all. The rebuild itself is deferred; see the class remarks for why that is
        /// mandatory rather than tidy.
        /// </remarks>
        private void OnNodeTaken(NodeTaken evt)
        {
            if (evt.Kind != SkillKind.Active)
            {
                return;
            }

            _dirty = true;
        }

        /// <summary>
        /// A skill moved between Auto and Manual: it joins this row or leaves it (rule 5).
        /// </summary>
        /// <remarks>
        /// The payload is used for nothing, which is <c>SkillBarPresenter.OnAutoCastChanged</c>'s
        /// answer for its reason: the row is packed from the front, so one skill leaving slides every
        /// cell after it — and a walk of twelve on a tap the player just made is not work worth
        /// splitting into two paths.
        /// </remarks>
        private void OnAutoCastChanged(SkillAutoCastChanged evt)
        {
            _dirty = true;
        }

        /// <summary>
        /// Which cells exist and what each is showing, from one walk of the owned actives (rule 5).
        /// </summary>
        /// <remarks>
        /// A null state is a row in a scene where no run has begun, which is a workflow rather than a
        /// fault — <c>HudPresenter</c>'s argument. Every cell is taken off in that case rather than
        /// left at whatever the prefab was dressed with, so an undressed scene shows a bare HUD.
        /// </remarks>
        private void Rebuild()
        {
            _dirty = false;
            _cellCount = 0;

            RunState state = _session?.State;

            int owned = state is null ? 0 : state.OwnedActiveCount;

            for (int index = 0; index < owned && _cellCount < SkillRunner.MaxActives; index++)
            {
                ContentId id = state.SkillIdAt(index);

                // **The read SkillBarPresenter is forbidden and this class is required to make**
                // (rule 5, M3-10a rule 11). A Manual skill has a button in the corner instead, so
                // the two readouts are disjoint and no cooldown is ever shown twice.
                if (!state.IsAutoCast(id))
                {
                    continue;
                }

                Show(_cellCount, index, id);

                _cellCount++;
            }

            for (int cell = _cellCount; cell < SkillRunner.MaxActives; cell++)
            {
                Hide(cell);
            }
        }

        /// <summary>Puts the active at <paramref name="index"/> under cell <paramref name="cell"/>.</summary>
        private void Show(int cell, int index, ContentId id)
        {
            _cellSkill[cell] = index;

            // Forgotten rather than kept, so the first frame after a rebuild draws rather than
            // comparing against what a different skill left behind — ManualSkillButton.ShowEmpty's
            // rule, with twelve cells that reshuffle instead of four that do not.
            _shownFraction[cell] = -1f;

            Image strip = _kindStrips[cell];

            if (strip != null)
            {
                // The resolve cannot fail in a live run — TreeRules resolved every node of this tree
                // at RunSession.Start, before RunStarted — so the miss branch is a scene composed
                // against a catalog that does not hold the run's content. Active is the honest
                // answer there rather than a throw inside an event handler: everything this row
                // draws is one (rule 5).
                SkillKind kind = _catalog is not null && _catalog.TryGetSkill(id, out SkillSpec spec)
                    ? spec.Kind
                    : SkillKind.Active;

                strip.color = Tint(kind);
                strip.gameObject.SetActive(true);
            }
        }

        /// <summary>Takes cell <paramref name="cell"/> off the screen entirely (rule 7).</summary>
        /// <remarks>
        /// Deactivated rather than made transparent, so it takes no touches and costs no canvas
        /// rebuild — <c>ManualSkillButton.ShowEmpty</c>'s reason. That is the state of all twelve in
        /// every run in this build, because nothing in <c>Data/Trees</c> ships an Active until M3-12.
        /// </remarks>
        private void Hide(int cell)
        {
            Image strip = _kindStrips[cell];

            if (strip != null)
            {
                strip.gameObject.SetActive(false);
            }

            _shownFraction[cell] = -1f;
        }

        /// <summary>
        /// Rule 6: each drawn cell's fill is <c>1 − fraction</c> — empty the instant a cast starts,
        /// full when the skill is live again.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The same convention as <c>SkillButton</c> and <c>ManualSkillButton</c>, deliberately: the
        /// player can have a cell and a button on screen at once, and two fills that emptied in
        /// opposite directions would be two answers to one question.
        /// </para>
        /// <para>
        /// <b>Only changed cells are written.</b> Assigning <c>fillAmount</c> marks the graphic dirty
        /// and queues a canvas rebuild, so writing an unchanged value would rebuild the HUD on every
        /// frame of every second a skill is not being cast — which, for a skill on an eight-second
        /// cooldown, is most of them. Twelve cells make that argument twelve times over.
        /// </para>
        /// </remarks>
        private void DrawFills()
        {
            RunState state = _session?.State;

            if (state is null)
            {
                return;
            }

            for (int cell = 0; cell < _cellCount; cell++)
            {
                Image fill = _fills[cell];

                if (fill == null)
                {
                    continue;
                }

                // Clamped rather than trusted, for SkillButton's reason: core guarantees [0, 1] and
                // there is no path by which it would not, but a fillAmount outside the range is the
                // kind of thing uGUI renders as an empty or a full ring rather than as an error.
                float clamped = Mathf.Clamp01(state.SkillCooldownFraction(_cellSkill[cell]));

                if (Mathf.Approximately(clamped, _shownFraction[cell]))
                {
                    continue;
                }

                _shownFraction[cell] = clamped;
                fill.fillAmount = 1f - clamped;
            }
        }

        /// <summary>
        /// Rule 7: twelve cells across in dp, left to right from <see cref="_marginDp"/> in the safe
        /// area's top-left corner.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Here rather than in the prefab for <c>HudPresenter.Place</c>'s two reasons: a
        /// Scale-With-Screen-Size canvas measures in reference pixels, so a cell authored at 24 of
        /// those is a different physical size on every phone — and the twelve have to agree about
        /// where each other are, which twelve components each doing their own arithmetic would be
        /// twelve chances to get wrong.
        /// </para>
        /// <para>
        /// <b>All twelve are placed, not only the drawn ones.</b> The row is packed from the front, so
        /// a cell's position is a property of its index and never of what is in it — which means
        /// membership changing is an activation and never a re-layout.
        /// </para>
        /// <para>
        /// <b>A nonsense field leaves the authored layout alone</b> rather than substituting a
        /// constant — <c>SkillBarPresenter.Place</c>'s answer, for its reason. An unusable margin or
        /// gap skips the whole row, because every cell's position depends on both; an unusable cell
        /// size does too. <b>A gap of 0 is legal and honoured</b> — twelve cells touching is a
        /// layout — where a cell size of 0 is not, because a row of 0 dp cells is this readout
        /// silently absent.
        /// </para>
        /// </remarks>
        private void Place()
        {
            if (_kindStrips is null
                || !IsUsableSize(_cellSizeDp)
                || !IsUsableGap(_gapDp)
                || !IsUsableMargin(_marginDp))
            {
                return;
            }

            float pxPerDp = PixelsPerDp();
            var size = new Vector2(_cellSizeDp, _cellSizeDp);

            for (int cell = 0; cell < _kindStrips.Length; cell++)
            {
                Image strip = _kindStrips[cell];

                if (strip == null || strip.transform is not RectTransform rect)
                {
                    continue;
                }

                float left = _marginDp.x + (cell * (_cellSizeDp + _gapDp));

                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = size * pxPerDp;
                rect.anchoredPosition = new Vector2(left * pxPerDp, -_marginDp.y * pxPerDp);
            }
        }

        /// <summary>CH §4's four kinds, in the four colours ledger row 6 is counting.</summary>
        /// <remarks>
        /// A <c>switch</c> on a <see cref="SkillKind"/> and not on an effect type — the banned shape
        /// is <c>switch (effect.Type)</c>, which is a dispatch that should have been polymorphism.
        /// This is a presentation lookup over a closed enum of four, which is what an enum is for.
        /// <c>OfferCard.Tint</c>'s words, and the same four values.
        /// </remarks>
        private Color Tint(SkillKind kind) => kind switch
        {
            SkillKind.Passive => _passive,
            SkillKind.Active => _active,
            SkillKind.Upgrade => _upgrade,
            SkillKind.Keystone => _keystone,
            _ => _active,
        };

        /// <summary>Whether all twelve cells and all twelve fills are dressed.</summary>
        private bool HasCells()
        {
            if (_fills is null
                || _kindStrips is null
                || _fills.Length < SkillRunner.MaxActives
                || _kindStrips.Length < SkillRunner.MaxActives)
            {
                return false;
            }

            for (int cell = 0; cell < SkillRunner.MaxActives; cell++)
            {
                if (_fills[cell] == null || _kindStrips[cell] == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsUsableSize(float dp) => float.IsFinite(dp) && dp > 0f;

        private static bool IsUsableGap(float dp) => float.IsFinite(dp) && dp >= 0f;

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
