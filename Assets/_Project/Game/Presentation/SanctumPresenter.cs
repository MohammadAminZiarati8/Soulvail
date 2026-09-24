using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Sanctum.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// GD §13.3's Sanctum, on screen: four priced rows a thumb can hit, a refusal that reads as a
    /// price rather than a broken button, and one way out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It renders one event, reads the run, and owns no economy</b> (M6-03a rule 1).
    /// <c>SanctumOpened</c> draws and shows — on the next frame, see <see cref="OnSanctumOpened"/> —
    /// and the Leave tap sends <c>LeaveSanctum</c> and hides. Every
    /// price, every refusal and every delivery is <c>SanctumShop</c>'s, asked through
    /// <see cref="IProgressionCommands"/> — so a disagreement between this screen and the game can
    /// only be a missed redraw in this file.
    /// </para>
    /// <para>
    /// <b>It redraws on open and after every tap, and on nothing else</b> (rule 2). While the shop is
    /// up the run is paused and the tick is gated, so nothing but this file can move the balance, the
    /// hit points or the Veilrot a row's answer is computed from.
    /// </para>
    /// <para>
    /// <b>It does not hold the pause</b> (rules 4 and 5). <c>RunTicker.SanctumPhase</c> raises
    /// <c>PauseReason.Sanctum</c> off <c>IsSanctumOpen</c>, <c>LevelUpPresenter</c>'s ruling one screen
    /// over; <c>RunPause</c> is not a dependency here and a row pins its absence.
    /// </para>
    /// <para>
    /// <b>Every command goes straight down the port</b> (rule 6): the ticker returns above
    /// <c>CommandPhase</c> while the pause is held, so a queued command would never arrive.
    /// </para>
    /// <para>
    /// <b>No fade, in or out</b> (rule 9): <c>Time.timeScale</c> is 0 while this is up. The canvas is
    /// full-screen and blocks raycasts, so the stick and the skill buttons are covered, not disabled.
    /// </para>
    /// </remarks>
    public sealed class SanctumPresenter : MonoBehaviour
    {
        /// <summary>The screen's heading.</summary>
        private static readonly LocKey TitleKey = new LocKey("ui.sanctum.title");

        /// <summary><em>"84 Essence"</em> — a table row rather than a format, <c>SplashPresenter.NodesKey</c>'s reason.</summary>
        private static readonly LocKey BalanceKey = new LocKey("ui.sanctum.balance");

        /// <summary>The one way out.</summary>
        private static readonly LocKey LeaveKey = new LocKey("ui.sanctum.leave");

        private static readonly LocKey RerollDetailKey = new LocKey("ui.sanctum.reroll.detail");
        private static readonly LocKey BanishDetailKey = new LocKey("ui.sanctum.banish.detail");
        private static readonly LocKey HealDetailKey = new LocKey("ui.sanctum.heal.detail");
        private static readonly LocKey CleanseDetailKey = new LocKey("ui.sanctum.cleanse.detail");

        /// <summary>
        /// Rule 3's five refusals. <em>Short</em> is the balance's; the other four are each a service's
        /// own — <em>complete</em> the Reroll's, since M6-11d.
        /// </summary>
        private static readonly LocKey RefusedShortKey = new LocKey("ui.sanctum.refused.short");
        private static readonly LocKey RefusedFullKey = new LocKey("ui.sanctum.refused.full");
        private static readonly LocKey RefusedCleanKey = new LocKey("ui.sanctum.refused.clean");
        private static readonly LocKey RefusedNothingKey = new LocKey("ui.sanctum.refused.nothing");
        private static readonly LocKey RefusedCompleteKey = new LocKey("ui.sanctum.refused.complete");

        [Tooltip("The whole screen, switched between alpha 0 and 1. No fade — timeScale is 0 while " +
                 "this is up, so a scaled tween would freeze (rule 9).")]
        [SerializeField] private CanvasGroup _root;

        [Tooltip("\"Sanctum\". Written from ui.sanctum.title in Start.")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("The balance, in Palette.Essence — off SanctumOpened on the first draw and off " +
                 "RunState.Essence after every tap (rule 2).")]
        [SerializeField] private TMP_Text _balance;

        [Tooltip("The four rows' parent, veiled while the banish list is up (rule 7).")]
        [SerializeField] private GameObject _servicePage;

        [Tooltip("GD §13.3's reroll, banked for the next offer.")]
        [SerializeField] private ServiceRow _reroll;

        [Tooltip("GD §13.3's banish. Opens the picker rather than buying — it needs a node.")]
        [SerializeField] private ServiceRow _banish;

        [Tooltip("GD §13.3's heal.")]
        [SerializeField] private ServiceRow _heal;

        [Tooltip("GD §13.3's cleanse.")]
        [SerializeField] private ServiceRow _cleanse;

        [Tooltip("The second page: every node Banish may take (rule 7).")]
        [SerializeField] private BanishPicker _picker;

        [Tooltip("Leaves the Sanctum. The only way out, and the command RunTicker lifts the gate on.")]
        [SerializeField] private Button _leave;

        [Tooltip("The Leave button's word, from ui.sanctum.leave.")]
        [SerializeField] private TMP_Text _leaveLabel;

        private IRunSession _session;
        private IProgressionCommands _progression;
        private ContentCatalog _catalog;
        private ILocalizer _localizer;

        private IDisposable _openedSubscription;

        /// <summary>Where <c>BanishableInto</c> writes. Sized to the tree on the first open, then reused.</summary>
        private ContentId[] _banishable = Array.Empty<ContentId>();

        /// <summary>
        /// A tap has been sent and a new frame has not begun. <c>LevelUpPresenter._choosing</c>, for
        /// its reason: uGUI dispatches both taps of a double tap from one <c>EventSystem</c> pass
        /// (rule 12), and a second <c>Buy</c> in that pass would spend money the player did not
        /// mean to.
        /// </summary>
        private bool _choosing;

        /// <summary>
        /// <c>SanctumOpened</c> has been heard and the screen has not been drawn yet — see
        /// <see cref="OnSanctumOpened"/> for why the draw waits.
        /// </summary>
        private bool _opening;

        /// <summary>The balance <c>SanctumOpened</c> carried, for the first draw (rule 2).</summary>
        private int _openingEssence;

        /// <param name="session">The run, for the balance after a tap and the tree's size.</param>
        /// <param name="progression">Where every tap goes, and what every row asks.</param>
        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="catalog">What a banishable node is called.</param>
        /// <param name="localizer">What turns this screen's keys into words.</param>
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

            _openedSubscription?.Dispose();
            _openedSubscription = hub.Subscribe<SanctumOpened>(OnSanctumOpened);

            if (_leave != null)
            {
                _leave.onClick.RemoveListener(OnLeave);
                _leave.onClick.AddListener(OnLeave);
            }
        }

        /// <summary>Whether the screen is up. The read <c>RunTicker</c> does <b>not</b> use — rule 4.</summary>
        public bool IsShown => _root != null && _root.alpha > 0f;

        /// <summary>Whether the Banish list is over the four rows.</summary>
        public bool IsPicking => IsShown && _picker != null && _picker.IsOpen;

        /// <exception cref="MissingReferenceException">The root, a row, the picker or Leave is not dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this presenter.</exception>
        /// <remarks>In <c>Start</c> for <c>LevelUpPresenter</c>'s reason: every <c>Awake</c> has run by then.</remarks>
        private void Start()
        {
            if (_root == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(SanctumPresenter)} has no root {nameof(CanvasGroup)} assigned. The run " +
                    "stops in every Sanctum whether or not this screen draws, so an undressed one is " +
                    "a frozen game with nothing on it.");
            }

            if (_reroll == null || _banish == null || _heal == null || _cleanse == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(SanctumPresenter)} is missing a {nameof(ServiceRow)}. Drag the four rows " +
                    "on Sanctum.prefab onto Reroll, Banish, Heal and Cleanse.");
            }

            if (_picker == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(SanctumPresenter)} has no {nameof(BanishPicker)} assigned — Banish would " +
                    "be a row that can be tapped and never bought.");
            }

            if (_leave == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(SanctumPresenter)} has no Leave button assigned. It is the only way out " +
                    "of the Sanctum, so without it the run stops for good at the first stage cleared.");
            }

            if (_session is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(SanctumPresenter)} was never injected, so it would never hear the shop " +
                    "open. The component is registered by RunScope — drag this object onto its " +
                    "Sanctum Presenter field.");
            }

            Write(_title, TitleKey);
            Write(_leaveLabel, LeaveKey);

            // Down whatever the prefab was left dressed as — HudPresenter's argument.
            HideScreen();
        }

        /// <remarks>
        /// Drops the subscription and the Leave handler, and nothing else: a scope torn down with the
        /// screen up leaves the pause held, and <c>RunPause.Dispose</c> restores both globals.
        /// </remarks>
        private void OnDestroy()
        {
            _openedSubscription?.Dispose();
            _openedSubscription = null;

            if (_leave != null)
            {
                _leave.onClick.RemoveListener(OnLeave);
            }
        }

        /// <remarks>
        /// The latch, and the one draw an opening owes — nothing is polled. The tick is gated, so a
        /// per-frame redraw would recompute sixty times a second an answer only this file changes
        /// (rule 12); the opening draw is a single bool read a frame until it lands, which is the
        /// frame after the one the shop opened on.
        /// </remarks>
        private void Update()
        {
            _choosing = false;

            if (_opening && _progression is not null && _progression.IsSanctumOpen)
            {
                _opening = false;

                ClosePicker();

                // The event's balance for the first frame — the reason SanctumOpened carries it (rule 2).
                Draw(_openingEssence);

                ShowScreen();
            }
        }

        /// <summary>The shop is opening: the screen is drawn and put up on this presenter's next frame.</summary>
        /// <remarks>
        /// <b>Not drawn here, and that is a finding rather than a preference.</b> <c>StageFlow</c>
        /// publishes this event from inside its own tick, and <c>RunSession</c> copies the phase onto
        /// <c>RunState.IsSanctumOpen</c> only after that tick returns — so inside this handler the
        /// port still reports the shop shut and <c>CanBuy</c> refuses all four, and a draw here opens
        /// the Sanctum with every row dead. The draw waits for <see cref="Update"/>, which asks
        /// <c>IsSanctumOpen</c> first. It costs one frame, and it is the frame the run pauses on.
        /// </remarks>
        private void OnSanctumOpened(SanctumOpened evt)
        {
            if (_session?.State is null)
            {
                // A screen in a scene where no run has begun — HudPresenter's argument.
                return;
            }

            _opening = true;
            _openingEssence = evt.Essence;
        }

        /// <summary>A service row was tapped.</summary>
        /// <remarks>
        /// <b><c>CanBuy</c> is asked again before sending</b> — M5-08a rule 4's second door. A dead
        /// row is already non-interactable, so this is unreachable through the UI; the alternative to
        /// an unreachable guard is an <c>InvalidOperationException</c> out of a <c>Button.onClick</c>.
        /// </remarks>
        private void OnRowTapped(SanctumService service)
        {
            if (_choosing || !IsShown || IsPicking || !_progression.IsSanctumOpen)
            {
                return;
            }

            if (!_progression.CanBuy(service))
            {
                return;
            }

            _choosing = true;

            if (service == SanctumService.Banish)
            {
                // Buy(Banish) throws by design (M6-02b rule 5) — the node is the second tap.
                OpenPicker();

                return;
            }

            _progression.Buy(service);

            Redraw();
        }

        /// <summary>A node was picked from the list: banish it, and give the four rows back.</summary>
        private void OnPicked(ContentId skillId)
        {
            if (_choosing || !IsPicking)
            {
                return;
            }

            _choosing = true;

            if (_progression.IsSanctumOpen && _progression.CanBuy(SanctumService.Banish))
            {
                _progression.Banish(skillId);
            }

            ClosePicker();

            Redraw();
        }

        /// <summary>The list was left without a pick. Spends nothing.</summary>
        private void OnPickCancelled()
        {
            ClosePicker();
        }

        /// <summary>Leave: the one command that closes the shop, sent straight down the port (rule 6).</summary>
        private void OnLeave()
        {
            if (_choosing || !IsShown)
            {
                return;
            }

            _choosing = true;

            if (_progression.IsSanctumOpen)
            {
                _progression.LeaveSanctum();
            }

            HideScreen();
        }

        /// <summary>Every row again, off the run's own balance — rule 2's after-a-tap half.</summary>
        private void Redraw()
        {
            RunState state = _session.State;

            if (state is not null)
            {
                Draw(state.Essence);
            }
        }

        private void Draw(int balance)
        {
            if (_balance != null)
            {
                _balance.text = _localizer.Format(BalanceKey, balance);
                _balance.color = Palette.Essence;
            }

            DrawRow(_reroll, SanctumService.Reroll, balance);
            DrawRow(_banish, SanctumService.Banish, balance);
            DrawRow(_heal, SanctumService.Heal, balance);
            DrawRow(_cleanse, SanctumService.Cleanse, balance);
        }

        private void DrawRow(ServiceRow row, SanctumService service, int balance)
        {
            if (row == null)
            {
                return;
            }

            int price = _progression.PriceOf(service);
            bool canBuy = _progression.CanBuy(service);

            row.Show(service, price, canBuy, Detail(service, price, canBuy, balance), _localizer, OnRowTapped);
        }

        /// <summary>
        /// What the detail line says: what the service does, or why it cannot be had (rule 3).
        /// </summary>
        /// <remarks>
        /// <c>CanBuy</c> false plus one read. The balance decides <em>short</em>; otherwise the
        /// service names its own refusal, because <c>SanctumShop.CanBuy</c> refuses exactly one
        /// worthless case per service — the Reroll's, a tree with nothing left to offer, since M6-11d.
        /// </remarks>
        private static LocKey Detail(SanctumService service, int price, bool canBuy, int balance)
        {
            if (canBuy)
            {
                return service switch
                {
                    SanctumService.Reroll => RerollDetailKey,
                    SanctumService.Banish => BanishDetailKey,
                    SanctumService.Heal => HealDetailKey,
                    _ => CleanseDetailKey,
                };
            }

            if (balance < price)
            {
                return RefusedShortKey;
            }

            return service switch
            {
                SanctumService.Reroll => RefusedCompleteKey,
                SanctumService.Banish => RefusedNothingKey,
                SanctumService.Heal => RefusedFullKey,
                SanctumService.Cleanse => RefusedCleanKey,
                _ => RefusedShortKey,
            };
        }

        /// <summary>Rule 7: the list over the four rows, in tree order.</summary>
        private void OpenPicker()
        {
            RunState state = _session.State;

            if (state is null || _picker == null)
            {
                return;
            }

            // Sized to the tree once, on the first Banish of the run — the tree does not change size.
            if (_banishable.Length != state.TreeNodeCount)
            {
                _banishable = new ContentId[state.TreeNodeCount];
            }

            int count = _progression.BanishableInto(_banishable);

            SetActive(_servicePage, false);

            _picker.Open(_banishable, count, _catalog, _localizer, OnPicked, OnPickCancelled);
        }

        private void ClosePicker()
        {
            if (_picker != null && _picker.IsOpen)
            {
                _picker.Close();
            }

            SetActive(_servicePage, true);
        }

        private void Write(TMP_Text label, LocKey key)
        {
            if (label != null)
            {
                label.text = _localizer is null ? key.ToString() : _localizer.Get(key);
            }
        }

        /// <remarks>Alpha 1 on the same call, with the raycast block — <c>LevelUpPresenter.ShowScreen</c>.</remarks>
        private void ShowScreen()
        {
            if (_root != null)
            {
                _root.alpha = 1f;
                _root.blocksRaycasts = true;
                _root.interactable = true;
            }
        }

        private void HideScreen()
        {
            _opening = false;

            if (_root != null)
            {
                _root.alpha = 0f;
                _root.blocksRaycasts = false;
                _root.interactable = false;
            }

            if (_picker != null && _picker.IsOpen)
            {
                _picker.Close();
            }

            SetActive(_servicePage, true);
        }

        private static void SetActive(GameObject page, bool value)
        {
            if (page != null && page.activeSelf != value)
            {
                page.SetActive(value);
            }
        }
    }
}
