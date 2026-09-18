using System;
using Soulvail.Core.Combat;
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
// find the type in a file-scoped namespace, and Skills.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// CC §6.3's <em>"Pause → Skills"</em>: every active the player owns, its cooldown, its
    /// auto-cast condition written out, and the Auto/Manual switch — plus CC §6.2's full-slots
    /// question, asked by this screen rather than refused by core.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It holds no pause, and that is rule 10 rather than an omission.</b> The pause is already
    /// held once, by whichever screen the player opened first — <c>PausePresenter</c> — and
    /// <c>RunPause.Pause</c> throws on a second reason (M3-08a rule 12). A prompt raised over a
    /// screen raised over a paused game is one gate, not three. <c>RunPause</c> is deliberately
    /// <em>not</em> a dependency of this class, and a test pins its absence at the constructor,
    /// where an <c>if</c> cannot creep back in — <c>LevelUpPresenter</c>'s precedent for the same
    /// claim.
    /// </para>
    /// <para>
    /// <b>Built on open and never polled</b> (rule 4). Nothing changes underneath this screen while
    /// it is up — the tick is gated (M3-08a rule 10) — so a per-frame poll would redraw an
    /// unchanging list sixty times a second on the one screen GD §11.4 wants cheap. This class
    /// therefore declares no <c>Update</c> at all, which is the strongest way to say it.
    /// </para>
    /// <para>
    /// <b>It redraws from <c>SkillAutoCastChanged</c></b> (rule 5), which M3-07a rule 9 publishes
    /// carrying the id, the state and the slot precisely so a list needs no second read. The only
    /// thing that can change while the screen is open is what the screen itself did, and hearing it
    /// back as an event is what keeps the row and the runner from disagreeing.
    /// </para>
    /// <para>
    /// <b>Only owned actives appear, and no filter says so.</b> The runner holds nothing else
    /// (M3-06 rule 5), so CC §6.1's <em>"passive skills have no toggle and no button"</em> is true
    /// by construction. A run owning none shows one line rather than an empty panel, because an
    /// empty panel and a broken panel look identical — <b>and that is every run until M3-11 and
    /// M3-12 author an active</b>, which is what makes it worth a row rather than a remark.
    /// </para>
    /// <para>
    /// <b>Nothing here writes a save</b> (rule 12). The toggles are persisted by M3-07b at the next
    /// boundary, from the runner's own state; a screen that saved on close would be a second writer
    /// for a field whose only author is the recorder.
    /// </para>
    /// <para>
    /// <b>It subscribes in <see cref="Construct"/>, not <c>OnEnable</c></b> — <c>HudPresenter</c>'s
    /// reason: <c>RunScope</c> builds its container from its own <c>Awake</c> and Unity orders no
    /// two of those, so an <c>OnEnable</c> subscription reaches for a hub that may not exist yet.
    /// Dropped in <c>OnDestroy</c>.
    /// </para>
    /// <para>
    /// <b>No fade, in or out.</b> <c>Time.timeScale</c> is 0 while this is up, so a scaled animation
    /// would freeze half-played and an unscaled one is a second clock — M3-08b rule 4's argument and
    /// M3-09a's, for the third time.
    /// </para>
    /// </remarks>
    public sealed class SkillsPresenter : MonoBehaviour
    {
        [Tooltip("The whole screen, switched between alpha 0 and 1. No fade — see the class " +
                 "remarks: timeScale is 0 while this is up, so a scaled tween would freeze.")]
        [SerializeField] private CanvasGroup _root;

        [Tooltip("The list. A ScrollRect because MaxActives is twelve and a landscape phone's " +
                 "safe area holds about five 56 dp rows — a list that silently clipped the sixth " +
                 "would hide a skill the player owns (rule 7).")]
        [SerializeField] private ScrollRect _scroll;

        /// <summary>
        /// CC §6.3's <em>"you own no actives yet"</em> line (M3-09b rule 6, M3-14a rule 7).
        /// </summary>
        /// <remarks>
        /// Authored here rather than on the prefab, <c>FirstActiveHint.HintKey</c>'s reason: the one
        /// string on this screen that belongs to the screen rather than to a skill cannot drift out
        /// of the code that owns it.
        /// </remarks>
        private static readonly LocKey EmptyKey = new LocKey("ui.skills.empty");

        [Tooltip("The row every list entry is cloned from, and never shown itself. Twelve clones " +
                 "are built under the scroll's content on first open and reused after that.")]
        [SerializeField] private SkillRow _rowTemplate;

        [Tooltip("\"You own no active skills yet.\" Shown instead of the list for a run owning " +
                 "none, because an empty panel and a broken panel look identical. Written from " +
                 "ui.skills.empty on every draw, so the prefab's own value is a placeholder.")]
        [SerializeField] private TMP_Text _emptyLabel;

        [Tooltip("Back to the pause panel. It lowers no pause — this screen never held one.")]
        [SerializeField] private Button _close;

        [Tooltip("CC §6.2's \"Manual slots full — which skill goes back to auto?\". Modal within " +
                 "this screen and holding no pause of its own (rule 10).")]
        [SerializeField] private GameObject _prompt;

        [Tooltip("The four current occupants, in thumb order. Flipping one back to Auto is how " +
                 "the player answers the question.")]
        [SerializeField] private SkillRow[] _promptRows = new SkillRow[SkillRunner.MaxManualSlots];

        [Tooltip("Dismisses the prompt without sending anything and puts the switch back where it " +
                 "was (rule 9).")]
        [SerializeField] private Button _promptCancel;

        [Tooltip("One row's height in dp. Applied at runtime for the reason HudPresenter applies " +
                 "its own: a Scale-With-Screen-Size canvas measures in reference pixels, which " +
                 "are a different physical size on every phone — and rule 7 is making a claim " +
                 "about a thumb. A guess until a phone exists — ledger row 4.")]
        [SerializeField] private float _rowHeightDp = 56f;

        private IRunSession _session;
        private IPlayerCommands _commands;
        private ContentCatalog _catalog;

        /// <summary>What a node is called. Handed to each pooled cell on its Show (M3-14a rule 11).</summary>
        private ILocalizer _localizer;

        private IDisposable _switchedSubscription;

        /// <summary>
        /// The list's pooled rows, built once on first open — <c>MaxActives</c> of them.
        /// </summary>
        /// <remarks>
        /// <c>ViewPool</c>'s shape at a much smaller scale, and instantiated on first open rather
        /// than in <c>Start</c> for the reason the prewarms in <c>RunScope</c> are sized the way
        /// they are: twelve small rects are cheap, and the frame this screen opens on is one where
        /// the simulation is already stopped.
        /// </remarks>
        private SkillRow[] _rows;

        /// <summary>
        /// The skill waiting on the prompt's answer — the one the player asked to make Manual when
        /// all four slots were full.
        /// </summary>
        private ContentId _requested;

        /// <param name="session">
        /// The run, for the five reads this list is built from. Not the runner — <c>RunState</c>
        /// hands out scalar reads and keeps the handle (AR §18.2).
        /// </param>
        /// <param name="commands">
        /// Where a switch goes. The only thing this class asks of the run, and it asks
        /// <c>ManualSlotCount</c> first every time (rule 9).
        /// </param>
        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="catalog">
        /// What a skill <em>is</em>: <c>RunState</c> hands out ids, so the name, the kind and the
        /// trigger are looked up here — <c>LevelUpPresenter</c>'s bargain, one screen on.
        /// </param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        /// <param name="localizer">
        /// What a skill is called and what its trigger clauses say. Held here and handed to each row
        /// on <c>SkillRow.Show</c>, because a row is a pooled clone nothing injects individually
        /// (M3-14a rule 11).
        /// </param>
        [Inject]
        public void Construct(
            IRunSession session,
            IPlayerCommands commands,
            DomainEventHub hub,
            ContentCatalog catalog,
            ILocalizer localizer)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            // Disposed before it is replaced, so a component injected twice — which VContainer does
            // not do and a test does — holds one subscription rather than two.
            _switchedSubscription?.Dispose();
            _switchedSubscription = hub.Subscribe<SkillAutoCastChanged>(OnAutoCastChanged);

            Wire(_close, Close);
            Wire(_promptCancel, Cancel);
        }

        /// <summary>Whether the screen is currently up — <c>PausePresenter</c>'s poll (rule 10).</summary>
        public bool IsOpen => _root != null && _root.alpha > 0f;

        /// <summary>Whether CC §6.2's question is on the table. The one read a test needs.</summary>
        public bool IsPromptUp => _prompt != null && _prompt.activeSelf;

        /// <exception cref="MissingReferenceException">The root, the list or the prompt is not dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this presenter.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>HudPresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// injected" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (_root == null || _scroll == null || _rowTemplate == null || _emptyLabel == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(SkillsPresenter)} is missing its root, its scroll rect, its row " +
                    "template or its empty label. A Skills screen that is only partly dressed " +
                    "opens over a stopped game and lists nothing, which is indistinguishable from " +
                    "a run that owns no actives.");
            }

            if (_prompt == null || _promptRows is null || _promptRows.Length == 0)
            {
                throw new MissingReferenceException(
                    $"{nameof(SkillsPresenter)} has no prompt or no prompt rows assigned. CC §6.2's " +
                    "\"which skill goes back to auto?\" is mandatory rather than optional — core " +
                    "refuses the fifth manual skill with a throw (M3-07a rule 2), so without this " +
                    "the fifth switch is an unhandled exception inside a UI callback.");
            }

            if (_session is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(SkillsPresenter)} was never injected, so it would list nothing and " +
                    "send nothing. The component is registered by RunScope — drag this object onto " +
                    "its Skills Presenter field.");
            }

            // Down whatever the prefab was left dressed as, so a screen someone was editing cannot
            // ship covering the arena — HudPresenter's argument, for the fourth screen.
            _rowTemplate.gameObject.SetActive(false);

            HideScreen();
        }

        /// <remarks>
        /// Explicit rather than left to the hub's disposal: a screen destroyed before its scope — a
        /// scene reload, an arena opened without a run — would otherwise stay in a subscriber list
        /// and be handed an event for a component Unity has killed.
        /// <para>
        /// <b>It drops the subscription and the two handlers, and lowers nothing.</b> There is
        /// nothing to lower: this screen never held a pause (rule 10), so unlike
        /// <c>PausePresenter.OnDestroy</c> there is no global here that something else has to
        /// restore.
        /// </para>
        /// </remarks>
        private void OnDestroy()
        {
            _switchedSubscription?.Dispose();
            _switchedSubscription = null;

            Unwire(_close, Close);
            Unwire(_promptCancel, Cancel);
        }

        /// <summary>
        /// Draws every active the player owns and puts the screen up. <c>PausePresenter</c>'s
        /// Skills button calls this.
        /// </summary>
        /// <remarks>
        /// <b>It raises no pause and checks for none.</b> Whether the game is stopped is the
        /// business of whoever opened the screen underneath this one; a Skills screen over a running
        /// game would be a legible thing to want (M3-10 may), and refusing it here would be this
        /// file deciding that for a task that has not been written.
        /// </remarks>
        public void Open()
        {
            if (_session is null || _session.State is null)
            {
                // A screen in a scene where no run has begun, which is a workflow rather than a
                // fault — HudPresenter's argument, reached by pressing Play with the Run scene open.
                return;
            }

            Draw();
            ShowScreen();
        }

        /// <summary>
        /// Takes the screen down, prompt and all. The Close button's handler, and what
        /// <c>PausePresenter</c> calls when the panel underneath goes away.
        /// </summary>
        /// <remarks>
        /// <b>It lowers no pause</b> (rule 10). <c>PausePresenter</c> still holds
        /// <c>PauseReason.Menu</c> and the player is back on its panel, which is the whole reason
        /// this screen was allowed not to take one.
        /// </remarks>
        public void Close()
        {
            HidePrompt();
            HideScreen();
        }

        /// <summary>
        /// A switch moved on the list: send the command, or ask CC §6.2's question instead.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The full-slots question is asked here and it is the whole of M3-07a rule 2's other
        /// half</b> (rule 9). <c>SkillRunner.SetAutoCast</c> refuses a fifth manual skill with an
        /// <c>InvalidOperationException</c>, so the count is read <em>before</em> the command and
        /// the prompt is shown <em>instead of</em> sending it. Core never sees a fifth request, its
        /// throw stays unreachable in a live build, and it stays loud for the caller that forgets to
        /// ask — the bargain <c>SpendLevelUp</c> and <c>ChooseOffer</c> already make.
        /// </para>
        /// <para>
        /// Nothing is drawn here. The row is repainted when the event comes back (rule 5), which is
        /// what keeps the switch and the runner from disagreeing; a row that painted itself
        /// optimistically would show Manual for a command that threw.
        /// </para>
        /// </remarks>
        private void OnRowSwitched(ContentId skillId, bool isAuto)
        {
            RunState state = _session?.State;

            if (state is null || IsPromptUp)
            {
                return;
            }

            // Back to Auto frees a slot and can never be refused, so it needs no question.
            if (isAuto)
            {
                _commands.SetAutoCast(skillId, true);

                return;
            }

            if (state.ManualSlotCount >= SkillRunner.MaxManualSlots)
            {
                ShowPrompt(skillId, state);

                return;
            }

            _commands.SetAutoCast(skillId, false);
        }

        /// <summary>
        /// The player answered the prompt: the victim goes back to Auto, then the skill they asked
        /// for takes the slot it freed.
        /// </summary>
        /// <remarks>
        /// <b>Two ordinary commands in that order, and no mechanism of its own</b> (rule 9). Core
        /// cannot tell this apart from two taps a minute apart, which is exactly why
        /// <c>SkillAutoCastChanged</c> says nothing about <em>why</em> a switch moved (M3-07a rule
        /// 9). The prompt goes down first, so the redraw each command triggers repaints a list
        /// rather than fighting a modal.
        /// </remarks>
        private void OnPromptRowSwitched(ContentId victimId, bool isAuto)
        {
            // A prompt row means something only when it is flipped *to* Auto: that is the player
            // nominating the skill that gives up its slot.
            if (!IsPromptUp || !isAuto || victimId == default)
            {
                return;
            }

            ContentId requested = _requested;

            HidePrompt();

            _commands.SetAutoCast(victimId, true);

            if (requested != default)
            {
                _commands.SetAutoCast(requested, false);
            }
        }

        /// <summary>
        /// The prompt was dismissed: send nothing, and put the switch back where it was (rule 9).
        /// </summary>
        private void Cancel()
        {
            if (!IsPromptUp)
            {
                return;
            }

            HidePrompt();

            // The redraw is what restores the switch. It is painted from core, which never heard
            // about the tap, so the fifth skill comes back Auto with no slot — and SkillRow's own
            // paint latch is what stops that assignment sending the command this method refused.
            Draw();
        }

        /// <summary>
        /// Something moved: repaint the list from core (rule 5).
        /// </summary>
        /// <remarks>
        /// Guarded on the screen being up rather than drawing unconditionally, because the event is
        /// published by every <c>SetAutoCast</c> in the run — M3-10's four buttons will send them
        /// too — and repainting twelve hidden rows for a screen nobody is looking at is work rule 4
        /// exists to refuse.
        /// </remarks>
        private void OnAutoCastChanged(SkillAutoCastChanged evt)
        {
            if (!IsOpen)
            {
                return;
            }

            Draw();
        }

        /// <summary>
        /// Every owned active on a row, in take order, from the five reads (rule 4).
        /// </summary>
        private void Draw()
        {
            RunState state = _session?.State;

            if (state is null)
            {
                return;
            }

            EnsureRows();

            int owned = state.OwnedActiveCount;
            int drawn = owned < _rows.Length ? owned : _rows.Length;

            for (int i = 0; i < _rows.Length; i++)
            {
                SkillRow row = _rows[i];

                if (row == null)
                {
                    continue;
                }

                if (i >= drawn)
                {
                    row.Hide();

                    continue;
                }

                ContentId id = state.SkillIdAt(i);

                row.Show(
                    _catalog.Skill(id),
                    state.SkillCooldownSeconds(i),
                    state.IsAutoCast(id),
                    SlotOf(state, id),
                    _localizer,
                    OnRowSwitched);
            }

            Layout(drawn);

            if (_emptyLabel != null)
            {
                // Written rather than left to the prefab, FirstActiveHint.HintKey's reason: this is
                // the only string on the screen that is not a skill's, and rule 7 names it. The
                // other three static labels on Skills.prefab still draw their keys — see the
                // finding in M3-14a's As built, which is about a serialized field this class does
                // not have rather than about a missing row.
                _emptyLabel.text = _localizer is null
                    ? EmptyKey.ToString()
                    : _localizer.Get(EmptyKey);

                _emptyLabel.gameObject.SetActive(owned == 0);
            }
        }

        /// <summary>
        /// CC §6.2's question, over the four current occupants.
        /// </summary>
        /// <remarks>
        /// <b>Modal within the screen and holding no pause of its own</b> (rule 10): the list's
        /// switches and the Close button go inert rather than a second canvas going up, because
        /// <c>RunPause</c> is already held by M3-09a and would throw on a second reason.
        /// </remarks>
        private void ShowPrompt(ContentId requested, RunState state)
        {
            if (_prompt == null || _promptRows is null)
            {
                return;
            }

            _requested = requested;

            for (int slot = 0; slot < _promptRows.Length; slot++)
            {
                SkillRow row = _promptRows[slot];

                if (row == null)
                {
                    continue;
                }

                // Bounded by the runner's ceiling as well as by the array, because the two
                // disagreeing would be an ArgumentOutOfRangeException inside a UI callback.
                ContentId occupant = slot < SkillRunner.MaxManualSlots
                    ? state.ManualSlotAt(slot)
                    : default;

                if (occupant == default)
                {
                    row.Hide();

                    continue;
                }

                // Drawn Manual and in its slot, which is what it is — so flipping it is the player
                // saying "this one goes back to auto" in the same gesture the list uses.
                row.Show(
                    _catalog.Skill(occupant),
                    state.SkillCooldownSeconds(IndexOf(state, occupant)),
                    isAuto: false,
                    slot,
                    _localizer,
                    OnPromptRowSwitched);
            }

            _prompt.SetActive(true);

            SetListInteractable(false);
        }

        private void HidePrompt()
        {
            _requested = default;

            if (_promptRows is not null)
            {
                for (int i = 0; i < _promptRows.Length; i++)
                {
                    if (_promptRows[i] != null)
                    {
                        _promptRows[i].Hide();
                    }
                }
            }

            if (_prompt != null)
            {
                _prompt.SetActive(false);
            }

            SetListInteractable(true);
        }

        /// <summary>
        /// Builds the pool on first open — <c>MaxActives</c> rows under the scroll's content.
        /// </summary>
        private void EnsureRows()
        {
            if (_rows is not null)
            {
                return;
            }

            _rows = new SkillRow[SkillRunner.MaxActives];

            if (_rowTemplate == null)
            {
                return;
            }

            // The template's own parent rather than the ScrollRect's content property, so a screen
            // whose content rect was re-parented in the prefab still puts its rows where the
            // template lives. They are the same object in the shipped asset.
            Transform parent = _rowTemplate.transform.parent;

            _rowTemplate.gameObject.SetActive(false);

            for (int i = 0; i < _rows.Length; i++)
            {
                SkillRow row = Instantiate(_rowTemplate, parent);

                row.name = $"Row{i}";
                row.Hide();

                _rows[i] = row;
            }
        }

        /// <summary>
        /// Stacks <paramref name="count"/> rows down the content rect in dp and sizes it to them
        /// (rule 7).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Here rather than in the prefab for <c>HudPresenter.Place</c>'s two reasons: a
        /// Scale-With-Screen-Size canvas measures in reference pixels, so a 56 dp row authored as 56
        /// of those is a different physical height on every phone; and twelve rects that have to
        /// agree about where each other are is twelve chances to overlap if each does its own
        /// arithmetic.
        /// </para>
        /// <para>
        /// <b>A non-finite or non-positive dp field leaves the prefab's authored layout alone</b>
        /// rather than writing it through — <c>LevelUpPresenter.Place</c>'s and
        /// <c>PausePresenter.Place</c>'s paragraph, and the same failure: a NaN reaching
        /// <c>sizeDelta</c> is a <c>RectTransform</c> that never renders again, which here is a list
        /// of skills that exists and cannot be seen.
        /// </para>
        /// </remarks>
        private void Layout(int count)
        {
            if (_rows is null || !IsUsableHeight(_rowHeightDp))
            {
                return;
            }

            float rowPx = _rowHeightDp * PixelsPerDp();

            for (int i = 0; i < _rows.Length; i++)
            {
                if (_rows[i] == null || _rows[i].transform is not RectTransform rect)
                {
                    continue;
                }

                // Anchored across the top of the content and hung downwards, so the list grows
                // down from row 0 whatever the content's width turns out to be.
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(0f, rowPx);
                rect.anchoredPosition = new Vector2(0f, -i * rowPx);
            }

            if (_scroll != null && _scroll.content != null)
            {
                // The whole list's height, which is what makes the twelfth row reachable and the
                // sixth visible at all: a content rect left at the viewport's height would clip
                // every row past it with nothing to scroll (rule 7).
                Vector2 size = _scroll.content.sizeDelta;

                _scroll.content.sizeDelta = new Vector2(size.x, count * rowPx);
            }
        }

        /// <summary>Which of CC §6.2's four slots holds <paramref name="id"/>, or −1 for none.</summary>
        /// <remarks>
        /// A scan of four rather than a read off the runner, because <c>RunState</c> exposes the
        /// slot table by position and never by skill — which is the right way round for M3-10's
        /// buttons and one loop of four for this screen (AR §18.2).
        /// </remarks>
        private static int SlotOf(RunState state, ContentId id)
        {
            for (int slot = 0; slot < SkillRunner.MaxManualSlots; slot++)
            {
                if (state.ManualSlotAt(slot) == id)
                {
                    return slot;
                }
            }

            return -1;
        }

        /// <summary>Where <paramref name="id"/> sits in the runner's take order, or 0 if nowhere.</summary>
        /// <remarks>
        /// Zero rather than −1 for the miss, because the only caller feeds it straight to
        /// <c>SkillCooldownSeconds</c>, which throws on a negative index. It cannot miss — a slot
        /// holds an owned active or it is empty (<c>SkillRunner.SetAutoCast</c> is the only writer
        /// of either) — and a wrong number on a prompt row is a better failure than an exception
        /// inside a UI callback.
        /// </remarks>
        private static int IndexOf(RunState state, ContentId id)
        {
            for (int i = 0; i < state.OwnedActiveCount; i++)
            {
                if (state.SkillIdAt(i) == id)
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>Rule 10: the list goes inert under the prompt rather than behind a canvas.</summary>
        private void SetListInteractable(bool value)
        {
            if (_rows is not null)
            {
                for (int i = 0; i < _rows.Length; i++)
                {
                    if (_rows[i] != null)
                    {
                        _rows[i].SetInteractable(value);
                    }
                }
            }

            if (_close != null)
            {
                _close.interactable = value;
            }
        }

        /// <remarks>
        /// Alpha 1 on the same call, with no tween and no coroutine. The raycast block goes on with
        /// it: the canvas is full-screen, so the pause panel underneath is covered rather than
        /// disabled — and <c>PausePresenter</c> disables it anyway, because belt and braces is what
        /// M3-09a's own icon guard is made of.
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
