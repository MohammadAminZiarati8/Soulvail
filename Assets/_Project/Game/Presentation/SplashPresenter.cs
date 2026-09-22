using System;
using System.Collections.Generic;
using System.Globalization;
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
// find the type in a file-scoped namespace, and Splash.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// CH §5.4's half-tree moment, on screen: classes, then branches, one tap each. The game is
    /// already stopped and the branch is already decided by the time this hides itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It renders two events and owns no progression</b> — <c>LevelUpPresenter</c>'s bargain.
    /// <c>SplashOffered</c> shows page one, <c>SplashChosen</c> hides the screen. Nothing here
    /// decides when the moment happens, what may be borrowed or what a branch is worth:
    /// <c>RunState.SplashCandidates</c> carries the classes, <c>RunState.SplashBranchesOf</c> carries
    /// the branches with their post-Keystone counts, and <c>ContentCatalog.Character</c> carries what
    /// a card draws.
    /// </para>
    /// <para>
    /// <b>It does not hold the pause</b> (M3-08a rule 12, and the owner's ruling at M3-08b). The gate
    /// is <c>RunTicker.LevelUpPhase</c>, which raises <c>PauseReason.Splash</c> when
    /// <c>IsSplashOpen</c> goes true and lowers it when it goes false — so the run being stopped is a
    /// once-a-frame pure function of core state rather than a consequence of a view having received
    /// an event, and a presenter that is absent, destroyed or never dressed costs a missing screen
    /// loudly instead of a run that ticks on with a choice nobody can make. <c>RunPause</c> is
    /// deliberately not a dependency of this class and a row pins its absence.
    /// </para>
    /// <para>
    /// <b>There is no way out but through</b> (rule 7). CH §5.4: <em>"the choice is mandatory"</em>,
    /// <em>"there is no stay-pure option"</em>. So there is no Back on either page — not on page two,
    /// and not on page one either, where <c>ClassSelectPresenter</c> has one — and every interactive
    /// element on this screen either picks a class or picks a branch. A row sweeps the prefab for a
    /// third kind.
    /// </para>
    /// <para>
    /// <b>The cards are <c>ClassCard</c>, the same control the class-select screen established</b>
    /// (M5-07): a name, a sentence and three numbers. What a player is being asked here is <em>which
    /// class</em>, which is the question that card was built for — and CH §5.4's <em>"what you do not
    /// gain"</em> is the reason the numbers on it are honest rather than misleading, because its
    /// weapon, its movement skill and its signature passive are exactly the things that do not come
    /// with the branch. The three figures are a description of the class the branch is borrowed
    /// <em>from</em>, not a promise about the run.
    /// </para>
    /// <para>
    /// <b>Nothing is instantiated</b> (<c>ClassSelectPresenter</c> rule 3). Three cards and three
    /// branch rows are authored on the prefab and bound; CH §5's three branches are a fixed number
    /// and CH §3's roster is three, so neither page can ever want a fourth.
    /// </para>
    /// <para>
    /// <b>No fade, in or out.</b> <c>Time.timeScale</c> is 0 while this is up, so a scaled animation
    /// would freeze half-played and an unscaled one is a second clock — M3-08b rule 4's argument for
    /// the fifth time.
    /// </para>
    /// <para>
    /// <b>It subscribes in <see cref="Construct"/>, not <c>OnEnable</c></b> —
    /// <c>LevelUpPresenter</c>'s reason: <c>RunScope</c> builds its container from its own
    /// <c>Awake</c> and Unity orders no two of those. Dropped in <c>OnDestroy</c>.
    /// </para>
    /// </remarks>
    public sealed class SplashPresenter : MonoBehaviour
    {
        /// <summary>What the screen is for, above both pages.</summary>
        /// <remarks>
        /// Authored here rather than on the prefab — <c>ClassSelectPresenter.TitleKey</c>'s reason:
        /// these four strings belong to the screen rather than to any content, and a key in the code
        /// cannot drift from the file that draws it. The prefab keeps each key as its authored text,
        /// which is the placeholder pattern that makes an undressed label visible in the Editor.
        /// </remarks>
        private static readonly LocKey TitleKey = new LocKey("ui.splash.title");

        /// <summary>Page one's prompt.</summary>
        private static readonly LocKey ClassKey = new LocKey("ui.splash.class");

        /// <summary>Page two's prompt.</summary>
        private static readonly LocKey BranchKey = new LocKey("ui.splash.branch");

        /// <summary>
        /// <em>"7 nodes"</em> — what a branch is worth, <b>after</b> its Keystone is dropped
        /// (rules 7, 8).
        /// </summary>
        /// <remarks>
        /// <b>A table row rather than a <c>const</c> format, which is where this differs from
        /// <c>ClassCard.HpFormat</c>.</b> <em>"140 HP"</em>, <em>"3.0 m/s"</em> and <em>"39 DPS"</em>
        /// are a number and a unit; <em>"nodes"</em> is an English word, and AR §11.5 does not have
        /// an exception for a word that happens to sit next to a number. The row carries the
        /// placeholder, so a translation may put it wherever its language wants it.
        /// </remarks>
        private static readonly LocKey NodesKey = new LocKey("ui.splash.nodes");

        [Tooltip("The whole screen, switched between alpha 0 and 1. No fade — see the class " +
                 "remarks: timeScale is 0 while this is up, so a scaled tween would freeze.")]
        [SerializeField] private CanvasGroup _root;

        [Tooltip("\"Borrow a discipline\". Written from ui.splash.title in Start, so the prefab's " +
                 "own value is a placeholder — see the class remarks.")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("The prompt under the title, rewritten per page: ui.splash.class on page one and " +
                 "ui.splash.branch on page two.")]
        [SerializeField] private TMP_Text _prompt;

        [Tooltip("Page one: the classes that may be borrowed from. Switched off when page two is up.")]
        [SerializeField] private GameObject _classPage;

        [Tooltip("CH §3's whole roster, authored and never instantiated. Open binds as many as the " +
                 "run offers and clears the rest — with the shipped two that is one card.")]
        [SerializeField] private ClassCard[] _cards = new ClassCard[3];

        [Tooltip("Page two: the chosen class's branches. Switched off until a class is tapped.")]
        [SerializeField] private GameObject _branchPage;

        [Tooltip("CH §5's three branches, in branch order. Three and never four: a class has " +
                 "exactly SkillTreeSpec.BranchCount of them, whichever class it is.")]
        [SerializeField] private Button[] _branchButtons = new Button[SkillTreeSpec.BranchCount];

        [Tooltip("Each branch's name, drawn from SkillBranchSpec.NameKey through ILocalizer.")]
        [SerializeField] private TMP_Text[] _branchNames = new TMP_Text[SkillTreeSpec.BranchCount];

        [Tooltip("Each branch's node count *after* the Keystone is dropped, from ui.splash.nodes. " +
                 "CH §5.4 does not lend the Keystone, so this is never the branch's own number.")]
        [SerializeField] private TMP_Text[] _branchCounts = new TMP_Text[SkillTreeSpec.BranchCount];

        private IRunSession _session;
        private IProgressionCommands _progression;
        private ContentCatalog _catalog;
        private ILocalizer _localizer;

        private IDisposable _offeredSubscription;
        private IDisposable _chosenSubscription;

        /// <summary>The class whose branches page two is drawing, or <c>default</c> on page one.</summary>
        private ContentId _lender;

        /// <summary>What <see cref="RunState.SplashBranchesOf"/> answered for <see cref="_lender"/>.</summary>
        /// <remarks>
        /// Held so a tap on row <em>i</em> sends that option's own <c>Branch</c> rather than
        /// <em>i</em>. They are the same number today — <c>SplashFlow.BranchesOf</c> walks a class's
        /// branches in order — and the day a class is allowed to withhold one they would not be.
        /// </remarks>
        private IReadOnlyList<SplashOption> _options;

        /// <summary>
        /// A choice has been sent and the command has not been answered by a new frame yet.
        /// </summary>
        /// <remarks>
        /// <b>Cleared in <see cref="Update"/> rather than in a handler</b> — <c>LevelUpPresenter</c>'s
        /// latch, for its reason: uGUI dispatches both taps of a double tap from one
        /// <c>EventSystem</c> pass with no <c>Update</c> of ours between them, so only a new frame
        /// can lift it. Here the second tap would reach a <c>ChooseSplash</c> for a moment that is no
        /// longer open, which throws — a screen whose last act is an exception.
        /// </remarks>
        private bool _choosing;

        /// <param name="session">The run, for the two reads the pages draw from.</param>
        /// <param name="progression">Where a tap goes. The only thing this class asks of the run.</param>
        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="catalog">
        /// What a class <em>is</em>. <c>RunState.SplashCandidates</c> hands out ids and nothing else,
        /// so the name, the sentence and the three numbers are looked up here — AR §18.2's rule that
        /// state hands out reads rather than handles.
        /// </param>
        /// <param name="localizer">
        /// What turns this screen's four keys, each class's two and each branch's one into words.
        /// Held by this class and handed to each card on <c>ClassCard.Bind</c>, because a card is a
        /// template nothing injects individually (M3-14a rule 11).
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

            _offeredSubscription = hub.Subscribe<SplashOffered>(OnSplashOffered);
            _chosenSubscription = hub.Subscribe<SplashChosen>(OnSplashChosen);

            for (int i = 0; i < BranchRows; i++)
            {
                Wire(i);
            }
        }

        /// <summary>Whether the screen is currently up. The one read a test needs.</summary>
        public bool IsShown => _root != null && _root.alpha > 0f;

        /// <summary>Whether page two is the one being drawn.</summary>
        /// <remarks>
        /// Read by the tests and by nothing in the game. A field-backed answer rather than the
        /// page's <c>activeSelf</c>, because a screen that is down has both pages off and that is
        /// not the same as being on page one.
        /// </remarks>
        public bool IsOnBranchPage => IsShown && _options is not null;

        /// <summary>How many branch rows the prefab authors — three, and never four.</summary>
        private int BranchRows =>
            _branchButtons is null ? 0 : _branchButtons.Length;

        /// <exception cref="MissingReferenceException">
        /// The root, the card array or the branch rows are not dressed.
        /// </exception>
        /// <exception cref="InvalidOperationException">Nothing injected this presenter.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>LevelUpPresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// dressed" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (_root == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(SplashPresenter)} has no root {nameof(CanvasGroup)} assigned. A " +
                    "half-tree screen that is only partly dressed stops the game and then shows the " +
                    "player nothing, which is indistinguishable from a crash — and unlike a " +
                    "level-up there is no way past it, because CH §5.4's choice is mandatory.");
            }

            if (_cards is null || _cards.Length == 0)
            {
                throw new MissingReferenceException(
                    $"{nameof(SplashPresenter)} has no cards assigned. Drag the three cards on " +
                    "Splash.prefab onto its Cards array — without them the run stops for a choice " +
                    "that cannot be made, and the only way out is to leave the scene.");
            }

            if (BranchRows == 0 || _branchNames is null || _branchCounts is null)
            {
                throw new MissingReferenceException(
                    $"{nameof(SplashPresenter)} has no branch rows assigned. Drag the three rows " +
                    "on Splash.prefab onto its Branch Buttons, Branch Names and Branch Counts " +
                    "arrays — without them a class can be chosen and its branches never can.");
            }

            if (_session is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(SplashPresenter)} was never injected, so it would never hear the " +
                    "moment. The component is registered by RunScope — drag this object onto its " +
                    "Splash Presenter field.");
            }

            // The one label that never changes, here (M3-14c rule 3). The prompt is per page and
            // stays with whichever page is drawn.
            Write(_title, TitleKey);

            // Down whatever the prefab was left dressed as, so a screen someone was editing cannot
            // ship covering the arena — HudPresenter's argument for its death panel.
            HideScreen();
        }

        /// <remarks>
        /// Explicit rather than left to the hub's disposal: a screen destroyed before its scope — a
        /// scene reload, an arena opened without a run — would otherwise stay in two subscriber
        /// lists and be handed events for a component Unity has killed.
        /// <para>
        /// It drops the subscriptions and the three branch handlers, and nothing else. A scope torn
        /// down with the screen up leaves the pause held, and <c>RunPause.Dispose</c> restores both
        /// globals unconditionally (M3-08a rule 13) — so this file must not depend on being the one
        /// that resumes, and under the ruling above it could not be.
        /// </para>
        /// </remarks>
        private void OnDestroy()
        {
            _offeredSubscription?.Dispose();
            _chosenSubscription?.Dispose();

            _offeredSubscription = null;
            _chosenSubscription = null;

            for (int i = 0; i < BranchRows; i++)
            {
                Unwire(i);
            }

            ClearCards();
        }

        /// <remarks>
        /// One bool per frame while the screen is up, and it is the half a handler cannot do — see
        /// <see cref="_choosing"/>. Runs at <c>timeScale</c> 0 because <c>Update</c> does.
        /// </remarks>
        private void Update()
        {
            _choosing = false;
        }

        /// <summary>
        /// The moment is on the table: draw page one and put the screen up.
        /// </summary>
        private void OnSplashOffered(SplashOffered evt)
        {
            RunState state = _session?.State;

            if (state is null)
            {
                // A screen in a scene where no run has begun, which is a workflow rather than a
                // fault — HudPresenter's argument, reached by pressing Play with the Run scene open.
                return;
            }

            DrawClasses(state.SplashCandidates);

            ShowScreen();
        }

        /// <summary>
        /// A branch has been borrowed. The screen closes and <c>RunTicker</c> lifts the gate on the
        /// next frame.
        /// </summary>
        private void OnSplashChosen(SplashChosen evt)
        {
            HideScreen();
        }

        /// <summary>Binds one card per candidate class and clears the rest.</summary>
        /// <remarks>
        /// <b>One candidate still draws the page</b> (rule 3). With the shipped two there is exactly
        /// one card and nothing auto-chooses: skipping the page would be a special case for a build
        /// state, and one tap that costs nothing reads identically at three classes.
        /// </remarks>
        private void DrawClasses(IReadOnlyList<ContentId> candidates)
        {
            _lender = default;
            _options = null;

            SetActive(_branchPage, false);
            SetActive(_classPage, true);

            Write(_prompt, ClassKey);

            for (int i = 0; i < _cards.Length; i++)
            {
                ClassCard card = _cards[i];

                if (card == null)
                {
                    continue;
                }

                if (i < candidates.Count)
                {
                    card.Bind(_catalog.Character(candidates[i]), _localizer, OnClassChosen);
                }
                else
                {
                    card.Clear();
                }
            }
        }

        /// <summary>
        /// A class was tapped: draw its branches (rule 7). Nothing is sent to core by this tap.
        /// </summary>
        /// <remarks>
        /// The cards go dead before the page swaps, for <see cref="_choosing"/>'s reason one page
        /// earlier: uGUI routes a click to the button it hit and says nothing about the ones beside
        /// it, so a second thumb landing in the same pass would redraw page two for a different class
        /// under a finger already on its way to a branch.
        /// </remarks>
        private void OnClassChosen(ContentId characterId)
        {
            if (_choosing || _options is not null)
            {
                return;
            }

            _choosing = true;

            RunState state = _session?.State;

            if (state is null)
            {
                return;
            }

            SetCardsInteractable(false);

            _lender = characterId;
            _options = state.SplashBranchesOf(characterId);

            DrawBranches();

            SetActive(_classPage, false);
            SetActive(_branchPage, true);

            Write(_prompt, BranchKey);
        }

        /// <summary>Writes the three rows of page two.</summary>
        /// <remarks>
        /// A row past the end of <see cref="_options"/> is switched off rather than drawn empty —
        /// <c>LevelUpPresenter.Draw</c>'s rule. It cannot happen against a shipped tree, where a
        /// class has exactly <c>SkillTreeSpec.BranchCount</c> branches and the prefab authors that
        /// many; it is written because the alternative is an <c>IndexOutOfRangeException</c> inside
        /// an event handler on a stopped game.
        /// </remarks>
        private void DrawBranches()
        {
            for (int i = 0; i < BranchRows; i++)
            {
                Button row = _branchButtons[i];
                bool drawn = i < _options.Count;

                // **A branch the run cannot borrow is drawn and dead, never hidden** (M5-08a rules 2
                // and 5). Before that task this read `row.interactable = drawn`, and a refused branch
                // was a live button that threw out of its own click handler — on a screen with no way
                // off it but through, which made a dead end out of a caught exception.
                bool borrowable = drawn && _options[i].Borrowable;

                if (row != null)
                {
                    SetActive(row.gameObject, drawn);
                    row.interactable = borrowable;
                }

                if (!drawn)
                {
                    continue;
                }

                SplashOption option = _options[i];

                Write(At(_branchNames, i), option.NameKey);

                TMP_Text count = At(_branchCounts, i);

                if (count != null)
                {
                    // Through the table rather than a const format — see NodesKey. A key with no row
                    // resolves to its own text, which has no placeholder, and string.Format leaves
                    // such a string alone: a missing row is a row that reads as a key rather than a
                    // screen that throws.
                    //
                    // **The refusal takes the node count's place rather than a line of its own**
                    // (rule 5): the number is what the row promises, so where a row promises nothing
                    // it should say why instead. The branch keeps its name either way, because
                    // reading what you cannot have is half of what CH §5.4's screen is for.
                    count.text = option.Borrowable
                        ? string.Format(
                            CultureInfo.InvariantCulture, _localizer.Get(NodesKey), option.NodeCount)
                        : _localizer.Get(option.RefusedKey);
                }
            }
        }

        /// <summary>A branch was tapped: borrow it, unless this frame already sent one.</summary>
        private void OnBranchChosen(int row)
        {
            if (_choosing || _options is null || row < 0 || row >= _options.Count)
            {
                return;
            }

            // **The second door, and it is deliberately not the only one** (M5-08a rule 4). Redraw
            // already leaves a refused row non-interactable, so this is unreachable through the UI;
            // it is here because the alternative to an unreachable guard is an ArgumentException out
            // of a click handler, which is exactly the defect this task exists to remove.
            if (!_options[row].Borrowable)
            {
                return;
            }

            _choosing = true;

            SetBranchesInteractable(false);

            _progression.ChooseSplash(_lender, _options[row].Branch);
        }

        /// <summary>
        /// Draws <paramref name="key"/> onto <paramref name="label"/>, if both are there.
        /// </summary>
        /// <remarks>
        /// <b>A missing label is silent and a missing localizer falls back to the key</b> —
        /// <c>ClassSelectPresenter.Write</c>'s semantics, deliberately softer than
        /// <see cref="Start"/>'s throws: this screen refuses to open without its cards, because a
        /// choice nobody can make stops the run for good, and shrugs at a missing word.
        /// </remarks>
        private void Write(TMP_Text label, LocKey key)
        {
            if (label != null)
            {
                label.text = _localizer is null ? key.ToString() : _localizer.Get(key);
            }
        }

        /// <remarks>
        /// Alpha 1 on the same call, with no tween and no coroutine. The raycast block goes on with
        /// it: the canvas is full-screen, so the stick and the slot buttons are covered rather than
        /// disabled — <c>LevelUpPresenter.ShowScreen</c>'s shape.
        /// </remarks>
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
            if (_root != null)
            {
                _root.alpha = 0f;
                _root.blocksRaycasts = false;
                _root.interactable = false;
            }

            _lender = default;
            _options = null;

            SetActive(_classPage, false);
            SetActive(_branchPage, false);

            ClearCards();
        }

        /// <summary>Takes every card off the screen, bound or not.</summary>
        private void ClearCards()
        {
            if (_cards is null)
            {
                return;
            }

            for (int i = 0; i < _cards.Length; i++)
            {
                if (_cards[i] != null)
                {
                    _cards[i].Clear();
                }
            }
        }

        private void SetCardsInteractable(bool value)
        {
            for (int i = 0; i < _cards.Length; i++)
            {
                if (_cards[i] != null)
                {
                    _cards[i].SetInteractable(value);
                }
            }
        }

        private void SetBranchesInteractable(bool value)
        {
            for (int i = 0; i < BranchRows; i++)
            {
                if (_branchButtons[i] != null)
                {
                    _branchButtons[i].interactable = value;
                }
            }
        }

        private static void SetActive(GameObject page, bool value)
        {
            // Unity's ==: an unassigned or destroyed page is a live reference that only compares
            // equal to null through the engine's operator.
            if (page != null && page.activeSelf != value)
            {
                page.SetActive(value);
            }
        }

        private static TMP_Text At(TMP_Text[] labels, int index) =>
            labels is not null && index < labels.Length ? labels[index] : null;

        /// <remarks>
        /// Removed before it is added, so a component injected twice — which VContainer does not do
        /// and a test does — reports one tap once rather than once per injection.
        /// <c>PausePresenter.Wire</c>'s trick, with a captured row index because the three rows share
        /// one handler and uGUI's <c>UnityAction</c> carries no sender.
        /// </remarks>
        private void Wire(int row)
        {
            Button button = _branchButtons[row];

            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnBranchChosen(row));
        }

        private void Unwire(int row)
        {
            if (_branchButtons[row] != null)
            {
                _branchButtons[row].onClick.RemoveAllListeners();
            }
        }
    }
}
