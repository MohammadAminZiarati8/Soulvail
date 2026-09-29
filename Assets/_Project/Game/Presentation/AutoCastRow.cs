using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
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
    /// them"</em>: a row of cells under the health bar, one for every skill that fires on its own, each
    /// with its icon, a dark sweep over what is left of its cooldown, and a glow when it is ready.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It exists because the default build is otherwise invisible, and that is GD §16.1's own
    /// argument</b> (M3-10b rule 4). Every skill starts on Auto (CC §6.1) and <em>"a player who never
    /// opens the menu has a complete, playable game with one button"</em> — which means a player who
    /// never opens the menu has every Active firing itself with no readout anywhere but here.
    /// </para>
    /// <para>
    /// <b>Two kinds of member, since UI-01e.</b> The Actives that are <em>not</em> in a slot, walked
    /// as <see cref="RunState.OwnedActiveCount"/> → <see cref="RunState.SkillIdAt"/> →
    /// <see cref="RunState.IsAutoCast"/> (M3-10b rule 5), and the class's volley once a node has given
    /// it one. The Ranger's volley is a Passive node in the tree and its only self-firing skill, so it
    /// is admitted by what core says about the volley — <see cref="RunState.VolleyEvery"/> — and never
    /// by its node's kind. <b>No other Passive makes a cell.</b> A skill switched to Manual leaves this
    /// row and gains a button (M3-10a); the two readouts are <b>disjoint by construction</b>, and
    /// <c>SkillBarPresenter</c> is not allowed to ask what this class asks
    /// (<c>Bar_KnowsNothingAboutAuto</c>).
    /// </para>
    /// <para>
    /// <b>Membership is rebuilt on its events and on the volley's arrival; the sweeps, glows and pips
    /// are polled</b> (rule 6). <c>NodeTaken</c> with <c>Kind == Active</c>, <c>SkillAutoCastChanged</c>
    /// and <c>RunStarted</c> mark the row dirty. The volley's node is a Passive, which
    /// <c>NodeTaken</c> skips without a lookup, so its cell is admitted by the frame that first reads a
    /// volley count. The rebuild is deferred to the next <see cref="Update"/> for M3-09c's reason:
    /// <c>LevelUpFlow.ChooseOffer</c> publishes <c>NodeTaken</c> before <c>SkillRunner.Add</c>, so a
    /// handler that rebuilt at once would miss the cell it had just been told about.
    /// </para>
    /// <para>
    /// <b>Twelve Active cells and one volley cell, authored on <c>Hud.prefab</c> and deactivated,
    /// never instantiated during a run</b> (rule 7). <see cref="SkillRunner.MaxActives"/> is 12, and a
    /// row of cooldowns that hitched on the frame a skill was granted would hitch on exactly the frame
    /// the player is watching it. The volley's cell comes first when it is there, so its arrival moves
    /// the rest by one: a rebuild re-places the cells, which is a rare event rather than a frame's work.
    /// </para>
    /// <para>
    /// <b>No colour is serialized</b> (M3-13a, UI-01e rule 8). The sweep is <see cref="Palette.Ink"/> at
    /// <see cref="_sweepAlpha"/> — the palette is opaque and a view multiplies its own alpha — the glow
    /// is <see cref="Palette.Player"/>, and the frames, icons and pips are the art, untinted. The kind
    /// tint M3-10b drew on a bare 24 dp cell is gone: an icon is the cell's identity now, and only
    /// Actives and the volley reach the row.
    /// </para>
    /// <para>
    /// <b>A nonsense dp field leaves the authored layout alone</b>, <c>SkillBarPresenter.Place</c>'s
    /// answer: these cells <em>are</em> authored on <c>Hud.prefab</c>, at a real size under the
    /// health bar, so there is something honest to fall back to.
    /// </para>
    /// </remarks>
    public sealed class AutoCastRow : MonoBehaviour
    {
        /// <summary>A cell's side in dp — the owner's starting size (UI-01e rule 7).</summary>
        /// <remarks>
        /// A constant beside the field so the row that checks the prefab quotes the rule rather than a
        /// literal of its own — <c>SkillBarPresenter.MinimumSpacingDp</c>'s reason.
        /// </remarks>
        public const float CellSizeDp = 34f;

        /// <summary>The gap between two cells, in dp.</summary>
        public const float GapDp = 4f;

        /// <summary>The most pips drawn under the volley's cell (UI-01e rule 5).</summary>
        /// <remarks>
        /// Five, because the Ranger's Volley counts five and each rank lowers it; a count above five
        /// is drawn as five pips with the shots scaled to them, floored.
        /// </remarks>
        public const int MaxPips = 5;

        /// <summary>How far under the volley's cell the pips sit, in dp.</summary>
        private const float PipDropDp = 3f;

        /// <summary>How tall the pips are, in dp — the art's aspect at a sixth of the cell's width.</summary>
        private const float PipHeightDp = 4f;

        /// <summary>The gap between two pips, in dp.</summary>
        private const float PipGapDp = 1f;

        /// <summary>How far the glow reaches past the cell on every side, as a fraction of the cell.</summary>
        private const float GlowSpread = 0.3f;

        [Tooltip("The twelve Active cells, left to right: each is a container whose frame, glow, " +
                 "icon and sweep are children, and the object switched off when there is no skill " +
                 "for it. Twelve because SkillRunner.MaxActives is twelve.")]
        [SerializeField] private RectTransform[] _cells = new RectTransform[SkillRunner.MaxActives];

        [Tooltip("The twelve icons, in the cells' order.")]
        [SerializeField] private Image[] _icons = new Image[SkillRunner.MaxActives];

        [Tooltip("The twelve sweeps, in the cells' order. Each Image must be Filled / Radial360 from " +
                 "the top, clockwise — fillAmount is the cooldown left, and colour the only other thing " +
                 "written.")]
        [SerializeField] private Image[] _sweeps = new Image[SkillRunner.MaxActives];

        [Tooltip("The twelve ready glows, in the cells' order, each behind its cell's frame. Optional " +
                 "cell by cell: a cell with none simply never glows.")]
        [SerializeField] private Image[] _glows = new Image[SkillRunner.MaxActives];

        [Tooltip("The volley's cell (UI-01e): shown once a node has given the class a volley, first " +
                 "in the row. Optional — a HUD without one draws no volley.")]
        [SerializeField] private RectTransform _volleyCell;

        [Tooltip("The volley cell's icon.")]
        [SerializeField] private Image _volleyIcon;

        [Tooltip("The volley cell's ready glow, behind its frame.")]
        [SerializeField] private Image _volleyGlow;

        [Tooltip("The volley's pips under its cell, left to right: one for each shot it counts, up " +
                 "to five.")]
        [SerializeField] private Image[] _pips = new Image[MaxPips];

        [Tooltip("What a skill with no icon of its own draws — Icon_Placeholder.")]
        [SerializeField] private Sprite _placeholderIcon;

        [Tooltip("A pip not yet charged — ChargePip_Empty.")]
        [SerializeField] private Sprite _pipEmpty;

        [Tooltip("A pip charged by a shot — ChargePip_Full.")]
        [SerializeField] private Sprite _pipFull;

        [Tooltip("How dark the cooldown sweep is over the icon, 0 to 1. The view multiplies its own " +
                 "alpha rather than asking the palette for a faded colour (M3-13a rule 8).")]
        [Range(0f, 1f)]
        [SerializeField] private float _sweepAlpha = 0.55f;

        [Tooltip("How wide and tall one cell is, in dp.")]
        [SerializeField] private float _cellSizeDp = CellSizeDp;

        [Tooltip("The gap between two cells, in dp.")]
        [SerializeField] private float _gapDp = GapDp;

        [Tooltip("Where the row's top-left corner sits, in dp from the safe area's left edge and " +
                 "from its top. Under the health bar, 8 dp below it, and left-aligned with it so the " +
                 "two read as one block (UI-01b rule 12).")]
        [SerializeField] private Vector2 _marginDp = new Vector2(HudLayout.BarLeftDp, HudLayout.SkillRowTopDp);

        private IRunSession _session;
        private SkillIconBook _iconBook;
        private CharacterLookBook _looks;

        private IDisposable _startedSubscription;
        private IDisposable _takenSubscription;
        private IDisposable _switchedSubscription;

        /// <summary>
        /// Which owned active each drawn cell is showing, by its index in the runner's walk order.
        /// Only the first <see cref="_cellCount"/> entries mean anything.
        /// </summary>
        private readonly int[] _cellSkill = new int[SkillRunner.MaxActives];

        /// <summary>The last sweep written to each cell, so an idle frame costs no rebuild.</summary>
        /// <remarks>Seeded outside <c>[0, 1]</c>, so the first frame after a rebuild always draws.</remarks>
        private readonly float[] _shownSweep = new float[SkillRunner.MaxActives];

        /// <summary>The last glow written to each cell: −1 unknown, 0 off, 1 on.</summary>
        private readonly int[] _shownGlow = new int[SkillRunner.MaxActives];

        /// <summary>How many Active cells are on screen — the first <em>n</em> of the twelve.</summary>
        private int _cellCount;

        /// <summary>Whether the volley's cell is on screen.</summary>
        private bool _hasVolley;

        /// <summary>The volley count, the shots and the ready flag as last drawn, so an idle frame writes nothing.</summary>
        private int _shownEvery = -1;
        private int _shownShots = -1;
        private int _shownVolleyGlow = -1;

        /// <summary>Whether the cells were last placed with the volley first, so a rebuild re-places only on a change.</summary>
        private bool? _placedWithVolley;

        /// <summary>Membership is stale and the next <see cref="Update"/> owes a rebuild.</summary>
        /// <remarks>Starts true, so a row whose run began before its <c>Start</c> is right on its first frame.</remarks>
        private bool _dirty = true;

        /// <summary>How many Active cells are drawn right now.</summary>
        public int CellCount => _cellCount;

        /// <summary>Whether the volley's cell is drawn.</summary>
        public bool HasVolleyCell => _hasVolley;

        /// <summary>How many pips the volley's cell shows.</summary>
        public int PipsShown { get; private set; }

        /// <summary>How many of those are full.</summary>
        public int PipsFull { get; private set; }

        /// <param name="session">The run, for the narrow reads this row walks and polls.</param>
        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="icons">What each skill looks like (UI-01e) — built at boot beside the catalog.</param>
        /// <param name="looks">Which node the class's volley is drawn as — <c>CharacterLook.VolleyNode</c>.</param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        /// <remarks>
        /// <b>The catalog left at UI-01e</b>: it was here for the kind behind a tint, and the tint went
        /// with the icons (rule 8). The two books arrived in its place, both root singletons that
        /// <c>RunScope</c> resolves by type from the boot scope.
        /// </remarks>
        [Inject]
        public void Construct(IRunSession session, DomainEventHub hub, SkillIconBook icons, CharacterLookBook looks)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _iconBook = icons ?? throw new ArgumentNullException(nameof(icons));
            _looks = looks ?? throw new ArgumentNullException(nameof(looks));

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
        /// <remarks>In <c>Start</c> rather than <c>Awake</c> for <c>HudPresenter</c>'s reason.</remarks>
        private void Start()
        {
            if (!HasCells())
            {
                throw new MissingReferenceException(
                    $"{nameof(AutoCastRow)} has fewer than {SkillRunner.MaxActives} cells assigned, " +
                    "or a cell is missing its icon or its sweep. Rule 7 sizes this row for the " +
                    "runner's ceiling and drives it from the front, so a hole in the middle is a skill " +
                    "whose cooldown is simply invisible — the exact failure GD §16.1 built this row to fix.");
            }

            if (_session is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(AutoCastRow)} was never injected, so an auto-cast build would be " +
                    "invisible — GD §16.1's whole argument for this row. The component is " +
                    "registered by RunScope — drag this object onto its Auto Cast Row field.");
            }

            Paint();

            // Rebuilt once immediately, because whether RunStarted has already been published
            // depends on the order VContainer's entry points and this component's Start happen to
            // run in. Either path lands here — HudPresenter's argument.
            Rebuild();
        }

        /// <remarks>Explicit rather than left to the hub's disposal, for <c>HudPresenter.OnDestroy</c>'s reason.</remarks>
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
        /// <c>Update</c>, not <c>LateUpdate</c>, for <c>ManualSkillButton</c>'s reason: <c>RunTicker</c>
        /// ticks in the Update phase, and a cell a frame behind its cooldown is wrong at the moment that
        /// matters — the instant the skill fires.
        /// </remarks>
        private void Update()
        {
            RunState state = _session?.State;

            // The volley's node is a Passive, which NodeTaken skips without a lookup — so the frame
            // that first reads a count is what admits its cell (rule 6).
            if (state is not null && _volleyCell != null && (state.VolleyEvery >= 1) != _hasVolley)
            {
                _dirty = true;
            }

            if (_dirty)
            {
                Rebuild();
            }

            DrawSweeps();
            DrawVolley();
        }

        /// <remarks>The opening state, which no event can describe — a resumed run comes back owning whatever <c>TakenNodeIds</c> said.</remarks>
        private void OnRunStarted(RunStarted evt)
        {
            _dirty = true;
        }

        /// <summary>A node was taken: if it can fire, the row owes a cell (rule 6).</summary>
        /// <remarks>A Passive is refused without a lookup — <c>NodeTaken</c> carries the kind.</remarks>
        private void OnNodeTaken(NodeTaken evt)
        {
            if (evt.Kind != SkillKind.Active)
            {
                return;
            }

            _dirty = true;
        }

        /// <summary>A skill moved between Auto and Manual: it joins this row or leaves it (M3-10b rule 5).</summary>
        private void OnAutoCastChanged(SkillAutoCastChanged evt)
        {
            _dirty = true;
        }

        /// <summary>Which cells exist and what each is showing (rule 1).</summary>
        /// <remarks>
        /// A null state is a row in a scene where no run has begun, which is a workflow rather than a
        /// fault — every cell is taken off, so an undressed scene shows a bare HUD.
        /// </remarks>
        private void Rebuild()
        {
            _dirty = false;
            _cellCount = 0;

            RunState state = _session?.State;

            _hasVolley = state is not null && state.VolleyEvery >= 1 && _volleyCell != null;

            if (_volleyCell != null)
            {
                SetShown(_volleyCell.gameObject, _hasVolley);
            }

            if (_hasVolley)
            {
                SetIcon(_volleyIcon, _looks.For(state.CharacterId).VolleyNode);
            }

            _shownEvery = -1;
            _shownShots = -1;
            _shownVolleyGlow = -1;

            int owned = state is null ? 0 : state.OwnedActiveCount;

            for (int index = 0; index < owned && _cellCount < SkillRunner.MaxActives; index++)
            {
                ContentId id = state.SkillIdAt(index);

                // **The read SkillBarPresenter is forbidden and this class is required to make**
                // (M3-10b rule 5, M3-10a rule 11). A Manual skill has a button instead.
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

            if (_placedWithVolley != _hasVolley)
            {
                Place();
            }
        }

        /// <summary>Puts the active at <paramref name="index"/> under cell <paramref name="cell"/>.</summary>
        private void Show(int cell, int index, ContentId id)
        {
            _cellSkill[cell] = index;

            // Forgotten rather than kept, so the first frame after a rebuild draws rather than
            // comparing against what a different skill left behind.
            _shownSweep[cell] = -1f;
            _shownGlow[cell] = -1;

            SetIcon(_icons[cell], id);
            SetShown(_cells[cell].gameObject, true);
        }

        /// <summary>Takes cell <paramref name="cell"/> off the screen (rule 7).</summary>
        /// <remarks>Deactivated rather than made transparent, so it costs no canvas rebuild.</remarks>
        private void Hide(int cell)
        {
            if (_cells[cell] != null)
            {
                SetShown(_cells[cell].gameObject, false);
            }

            _shownSweep[cell] = -1f;
            _shownGlow[cell] = -1;
        }

        /// <summary>A skill's icon, or the placeholder for one without (rule 2).</summary>
        private void SetIcon(Image icon, ContentId skillId)
        {
            if (icon == null)
            {
                return;
            }

            Sprite sprite = _iconBook is null ? null : _iconBook.For(skillId);

            icon.sprite = sprite != null ? sprite : _placeholderIcon;
        }

        /// <summary>
        /// Rules 3 and 4: each drawn cell's sweep is the cooldown left, and its glow is on while the
        /// skill is ready. Only changed cells are written.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The sweep shades what is left, where M3-10b's fill grew back.</b> The mockup and the art's
        /// cooling button draw a cooling skill as a dark wedge over its icon that clears clockwise, so
        /// the fill is <see cref="RunState.SkillCooldownFraction"/> itself: full at the cast, empty when
        /// the skill is live. The information is M3-10b's; the picture is the icon's.
        /// </para>
        /// <para>
        /// <b>Only changed cells are written.</b> Assigning <c>fillAmount</c> or toggling a glow marks
        /// the canvas dirty, and a skill on an eight-second cooldown is idle for most of it.
        /// </para>
        /// </remarks>
        private void DrawSweeps()
        {
            RunState state = _session?.State;

            if (state is null)
            {
                return;
            }

            for (int cell = 0; cell < _cellCount; cell++)
            {
                int index = _cellSkill[cell];
                Image sweep = _sweeps[cell];

                // Clamped rather than trusted, for SkillButton's reason.
                float left = Mathf.Clamp01(state.SkillCooldownFraction(index));

                if (sweep != null && !Mathf.Approximately(left, _shownSweep[cell]))
                {
                    _shownSweep[cell] = left;
                    sweep.fillAmount = left;
                }

                int glow = state.IsSkillReady(index) ? 1 : 0;

                if (glow != _shownGlow[cell])
                {
                    _shownGlow[cell] = glow;

                    if (_glows[cell] != null)
                    {
                        SetShown(_glows[cell].gameObject, glow == 1);
                    }
                }
            }
        }

        /// <summary>
        /// Rules 4 and 5: the volley's pips are its shots toward the next, and its cell glows while the
        /// next shot is the fan.
        /// </summary>
        /// <remarks>
        /// <c>min(VolleyEvery, MaxPips)</c> pips, the first <c>VolleyShotsTowardNext</c> of them full.
        /// Past five the shots are scaled to five and floored, so a count of eight shows a pip filling
        /// every 1.6 shots. Ready fills every pip, which is the moment the count reaches the last one.
        /// Written only when the count, the shots or the flag moves.
        /// </remarks>
        private void DrawVolley()
        {
            RunState state = _session?.State;

            if (!_hasVolley || state is null)
            {
                return;
            }

            int every = state.VolleyEvery;
            int shots = state.VolleyShotsTowardNext;
            bool ready = state.IsVolleyReady;
            int glow = ready ? 1 : 0;

            // Ready fills every pip, and the frame that readies it is not always a frame that moved
            // the shots — a rank taken mid-count readies the volley with the count where it was. So a
            // change of the flag redraws the pips as well as the glow.
            if (every != _shownEvery || shots != _shownShots || glow != _shownVolleyGlow)
            {
                _shownEvery = every;
                _shownShots = shots;

                int shown = Mathf.Clamp(every, 0, Mathf.Min(MaxPips, _pips.Length));
                int full = ready ? shown : every <= MaxPips ? shots : shots * MaxPips / Math.Max(1, every);

                PipsShown = shown;
                PipsFull = Mathf.Clamp(full, 0, shown);

                for (int pip = 0; pip < _pips.Length; pip++)
                {
                    Image image = _pips[pip];

                    if (image == null)
                    {
                        continue;
                    }

                    SetShown(image.gameObject, pip < shown);

                    Sprite sprite = pip < PipsFull ? _pipFull : _pipEmpty;

                    if (image.sprite != sprite)
                    {
                        image.sprite = sprite;
                    }
                }
            }

            if (glow != _shownVolleyGlow)
            {
                _shownVolleyGlow = glow;

                if (_volleyGlow != null)
                {
                    SetShown(_volleyGlow.gameObject, ready);
                }
            }
        }

        /// <summary>The two colours this row writes, once (rule 8).</summary>
        private void Paint()
        {
            Color shade = Palette.Ink;

            shade.a = Mathf.Clamp01(_sweepAlpha);

            for (int cell = 0; cell < SkillRunner.MaxActives; cell++)
            {
                if (_sweeps[cell] != null)
                {
                    _sweeps[cell].color = shade;
                }

                if (_glows[cell] != null)
                {
                    _glows[cell].color = Palette.Player;
                }
            }

            if (_volleyGlow != null)
            {
                _volleyGlow.color = Palette.Player;
            }
        }

        /// <summary>
        /// Rule 7: the cells across in dp from <see cref="_marginDp"/>, the volley's first when it is
        /// there, and its pips under it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Here rather than in the prefab for <c>HudPresenter.Place</c>'s two reasons: a
        /// Scale-With-Screen-Size canvas measures in reference pixels, and thirteen cells doing their own
        /// arithmetic would be thirteen chances to get it wrong.
        /// </para>
        /// <para>
        /// <b>All twelve Active cells are placed, not only the drawn ones</b>, so membership changing is
        /// an activation and never a re-layout — except the volley's arrival, which moves every Active
        /// cell one slot right and is the one reason <see cref="Rebuild"/> places again.
        /// </para>
        /// <para>
        /// <b>A nonsense field leaves the authored layout alone.</b> An unusable margin, gap or cell size
        /// skips the whole row; a gap of 0 is legal and honoured, a cell of 0 is not.
        /// </para>
        /// </remarks>
        private void Place()
        {
            if (_cells is null
                || !IsUsableSize(_cellSizeDp)
                || !IsUsableGap(_gapDp)
                || !IsUsableMargin(_marginDp))
            {
                return;
            }

            _placedWithVolley = _hasVolley;

            float pxPerDp = PixelsPerDp();
            int first = _hasVolley ? 1 : 0;

            PlaceCell(_volleyCell, 0, pxPerDp);

            for (int cell = 0; cell < _cells.Length; cell++)
            {
                PlaceCell(_cells[cell], cell + first, pxPerDp);
            }

            PlaceGlow(_volleyGlow);

            for (int cell = 0; cell < _glows.Length; cell++)
            {
                PlaceGlow(_glows[cell]);
            }

            PlacePips(pxPerDp);
        }

        private void PlaceCell(RectTransform rect, int slot, float pxPerDp)
        {
            if (rect == null)
            {
                return;
            }

            float left = _marginDp.x + (slot * (_cellSizeDp + _gapDp));

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(_cellSizeDp, _cellSizeDp) * pxPerDp;
            rect.anchoredPosition = new Vector2(left * pxPerDp, -_marginDp.y * pxPerDp);
        }

        /// <summary>A glow reaches <see cref="GlowSpread"/> of the cell past it on every side, centred on it.</summary>
        private static void PlaceGlow(Image glow)
        {
            if (glow == null || glow.transform is not RectTransform rect)
            {
                return;
            }

            rect.anchorMin = new Vector2(-GlowSpread, -GlowSpread);
            rect.anchorMax = new Vector2(1f + GlowSpread, 1f + GlowSpread);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>The pips as wide together as the volley's cell, <see cref="PipDropDp"/> under it.</summary>
        private void PlacePips(float pxPerDp)
        {
            int count = _pips is null ? 0 : _pips.Length;

            if (count == 0)
            {
                return;
            }

            float width = (_cellSizeDp - ((count - 1) * PipGapDp)) / count;

            for (int pip = 0; pip < count; pip++)
            {
                if (_pips[pip] == null || _pips[pip].transform is not RectTransform rect)
                {
                    continue;
                }

                // Hung from the cell's bottom-left, so the pips move with the cell.
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(width, PipHeightDp) * pxPerDp;
                rect.anchoredPosition = new Vector2(pip * (width + PipGapDp) * pxPerDp, -PipDropDp * pxPerDp);
            }
        }

        /// <summary>Whether all twelve cells, icons and sweeps are dressed.</summary>
        private bool HasCells()
        {
            if (_cells is null
                || _icons is null
                || _sweeps is null
                || _glows is null
                || _cells.Length < SkillRunner.MaxActives
                || _icons.Length < SkillRunner.MaxActives
                || _sweeps.Length < SkillRunner.MaxActives
                || _glows.Length < SkillRunner.MaxActives)
            {
                return false;
            }

            for (int cell = 0; cell < SkillRunner.MaxActives; cell++)
            {
                if (_cells[cell] == null || _icons[cell] == null || _sweeps[cell] == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static void SetShown(GameObject target, bool shown)
        {
            if (target.activeSelf != shown)
            {
                target.SetActive(shown);
            }
        }

        private static bool IsUsableSize(float dp) => float.IsFinite(dp) && dp > 0f;

        private static bool IsUsableGap(float dp) => float.IsFinite(dp) && dp >= 0f;

        private static bool IsUsableMargin(Vector2 dp) =>
            float.IsFinite(dp.x) && float.IsFinite(dp.y) && dp.x >= 0f && dp.y >= 0f;

        /// <summary>How many canvas units one dp is worth — <c>HudPresenter.PixelsPerDp</c>, for its reason.</summary>
        private float PixelsPerDp()
        {
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            return StickShaper.PixelsPerDp(Screen.dpi) / scale;
        }
    }
}
