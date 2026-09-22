using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and ClassSelect.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The class-select screen: one card per authored class, and the tap that decides which run the
    /// player gets. CH §3, §6; GD §4.5, §16.1; AR §3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It decides nothing about the run beyond which class</b> — AR §3, and
    /// <see cref="MenuPresenter"/>'s own argument: mode, class and seed are choices a player makes,
    /// not outcomes a simulation computes. <b>The mode is still the catalog's first and this screen
    /// invents no mode-select</b> (rule 2). GD §4.5's rule is that a mode is a data object and no
    /// code may assume Descent, so <see cref="FirstModeId"/> is the same stand-in
    /// <c>MenuPresenter.FirstModeId</c> was, moved here with the write it feeds: V1 ships one mode
    /// and there is no mode-select on the roadmap, so the class becomes a choice and the mode stays
    /// a fact. The seed stays wall-clock derived for the reason it always was.
    /// </para>
    /// <para>
    /// <b>The tap that starts the run is here, in one method, and it was moved rather than copied</b>
    /// (rule 1). <c>MenuPresenter.Descend</c> used to set <c>PendingRun</c> from the catalog's first
    /// mode and first class and await the load in one place; the double-tap guard, the
    /// <c>async void</c> and the <c>catch</c> that gives the buttons back all came with the write
    /// they were guarding, so there is still exactly one method in the project that starts a fresh
    /// run.
    /// </para>
    /// <para>
    /// <b>Nothing is instantiated</b> (rule 3). The cards are authored on the prefab and bound, which
    /// is <c>AutoCastRow</c>'s twelve cells and <c>LevelUpPresenter</c>'s three <c>OfferCard</c>s
    /// (M3-10b rule 7, M3-08b) for AR §14's reason at a moment the player is about to enter a run: a
    /// screen that instantiates is a screen that hitches. <b>The prefab carries three</b> — CH §3's
    /// whole roster — and <see cref="Open"/> binds as many as the catalog holds and clears the rest,
    /// so M6-07's Emberwright is a card being filled rather than a prefab being edited.
    /// </para>
    /// <para>
    /// <b>More authored classes than cards is a warning, once, and not a throw</b> — the rule
    /// <c>EnemyViews.WarnAboutCapacityOnce</c> follows, for a blunter reason here: a menu that
    /// refused to open would be a build nobody could play.
    /// </para>
    /// <para>
    /// <b>Every authored class is selectable, and the unlock gate is named rather than invented</b>
    /// (rule 6). CH §6 makes classes the one thing meta-progression buys and <c>PlayerProfile</c>
    /// <b>v3 carries no unlock set</b> — four fields, <c>Version</c>, <c>HapticsEnabled</c>,
    /// <c>SeenFirstActiveHint</c>, <c>Shards</c>. Adding one here would be a v4 and a migration for
    /// a gate nothing can open, since nothing spends a Shard until M6-02 and nothing awards an
    /// achievement at all. So this screen offers what the catalog holds and M6-09 is where a card
    /// learns to be locked.
    /// </para>
    /// <para>
    /// <b>It is a <see cref="CanvasGroup"/> in the Menu scene, not a scene of its own</b> (rule 9).
    /// <c>MenuScope</c> already registers one presenter and <c>SceneLoader</c> knows two scenes; a
    /// third would cost a load, a scope and a build-settings entry for a screen the player is on for
    /// four seconds. <c>PausePresenter</c>'s and <c>SkillsPresenter</c>'s shape.
    /// </para>
    /// <para>
    /// <b>A <c>Continue</c> takes none of this.</b> Mode, class and seed all come off the snapshot
    /// (M2-14b rule 7), so a resumed Gravecaller run resumes as a Gravecaller without this screen
    /// having an opinion — and <c>PendingRun</c> is written on a card's tap and on no other path
    /// through this file.
    /// </para>
    /// </remarks>
    public sealed class ClassSelectPresenter : MonoBehaviour
    {
        /// <summary>
        /// The question the screen asks.
        /// </summary>
        /// <remarks>
        /// Authored here rather than on the prefab — <c>MenuPresenter.TitleKey</c>'s and
        /// <c>PausePresenter.ResumeKey</c>'s reason: these two strings belong to the screen rather
        /// than to any content, and a key in the code cannot drift from the file that draws it. The
        /// prefab keeps the key as its authored text, which is the placeholder pattern that makes an
        /// undressed label visible in the Editor rather than blank.
        /// </remarks>
        private static readonly LocKey TitleKey = new LocKey("ui.classselect.title");

        /// <summary>The way out without choosing (rule 7).</summary>
        private static readonly LocKey BackKey = new LocKey("ui.classselect.back");

        [Tooltip("The screen, switched between alpha 0 and 1. No fade — a menu is not a fight, and " +
                 "a tween here would be the first one in the project's UI.")]
        [SerializeField] private CanvasGroup _root;

        [Tooltip("CH §3's whole roster, authored and never instantiated (rule 3). Open binds as " +
                 "many as the catalog holds and clears the rest.")]
        [SerializeField] private ClassCard[] _cards = new ClassCard[3];

        [Tooltip("Closes without writing anything. The menu comes back exactly as it was.")]
        [SerializeField] private Button _back;

        [Tooltip("\"Choose your class\". Written from ui.classselect.title in Start, so the " +
                 "prefab's own value is a placeholder — see the class remarks.")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("\"Back\". Written from ui.classselect.back in Start.")]
        [SerializeField] private TMP_Text _backLabel;

        private PendingRun _pending;
        private ContentCatalog _catalog;
        private SceneLoader _loader;
        private ILocalizer _localizer;

        /// <summary>
        /// A class has been chosen and the scene load has not answered yet.
        /// </summary>
        /// <remarks>
        /// The latch rather than the buttons' <c>interactable</c>, because the button is only the
        /// first of the two guards: uGUI dispatches both taps of a double tap from one
        /// <c>EventSystem</c> pass, and a handler invoked directly — which is what a test does —
        /// never consults <c>interactable</c> at all. <c>PausePresenter._quitting</c>'s argument.
        /// </remarks>
        private bool _descending;

        /// <summary>
        /// Whether more classes are authored than the prefab has cards. Warned about once.
        /// </summary>
        private bool _warnedAboutCapacity;

        /// <summary>Whether the screen is up. Read by <see cref="MenuPresenter"/> and by the tests.</summary>
        public bool IsOpen { get; private set; }

        /// <param name="pending">
        /// What the next run should be. Written on a card's tap and nowhere else in this file.
        /// </param>
        /// <param name="catalog">
        /// What classes exist, and which mode is first. This screen reads both and writes neither.
        /// </param>
        /// <param name="loader">Where a chosen class goes.</param>
        /// <param name="localizer">
        /// What turns this screen's two keys, and each class's two, into words. Resolved from
        /// <c>BootScope</c> through <c>MenuScope</c>'s parent container — the Menu needs a localizer
        /// and has no run, which is why the port is registered at boot (M3-14a rule 4).
        /// </param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        /// <remarks>
        /// The Back button is wired here rather than in <c>OnEnable</c>, <c>PausePresenter</c>'s
        /// reason: <c>MenuScope</c> builds its container from its own <c>Awake</c> and Unity orders
        /// no two of those. It is dropped in <see cref="OnDestroy"/>.
        /// </remarks>
        [Inject]
        public void Construct(
            PendingRun pending,
            ContentCatalog catalog,
            SceneLoader loader,
            ILocalizer localizer)
        {
            _pending = pending ?? throw new ArgumentNullException(nameof(pending));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

            Wire(_back, Close);
        }

        /// <exception cref="MissingReferenceException">
        /// The root, the card array or the Back button is not dressed.
        /// </exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>PausePresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// dressed" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (_root == null || _back == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(ClassSelectPresenter)} has no root {nameof(CanvasGroup)} or no Back " +
                    $"{nameof(Button)} assigned. A class-select screen that is only partly dressed " +
                    "covers the menu and then offers no way back, which is indistinguishable from " +
                    "a crash.");
            }

            if (_cards is null || _cards.Length == 0)
            {
                throw new MissingReferenceException(
                    $"{nameof(ClassSelectPresenter)} has no cards assigned. Drag the three cards " +
                    "on this prefab onto its Cards array — without them Descend opens a screen " +
                    "with nothing on it and no run can ever start.");
            }

            // The two labels, once, here (M3-14c rule 3). In Start rather than in a draw path
            // because neither changes while the app is running, and in Start rather than Construct
            // because a serialized field is not guaranteed dressed before every Awake has run —
            // this method's own reason for being where it is.
            Write(_title, TitleKey);
            Write(_backLabel, BackKey);

            // Down whatever the prefab was left dressed as, so a screen someone was editing cannot
            // ship covering the menu — HudPresenter's argument for its death panel. Every card goes
            // with it: an authored card left switched on would be a class nobody bound.
            HideScreen();
        }

        /// <remarks>
        /// Explicit rather than left to the scene's teardown: the handler is wired in
        /// <see cref="Construct"/>, so a component injected and then destroyed — which a test does —
        /// would otherwise leave a listener pointing at a dead object.
        /// </remarks>
        private void OnDestroy()
        {
            Unwire(_back, Close);

            ClearCards();
        }

        /// <summary>
        /// Draws one card per authored class and puts the screen up.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Idempotent about its own state and not about the draw: reopening rebinds, which is what
        /// makes a screen closed with <c>Back</c> and opened again show the same two cards live
        /// rather than the dead ones a tap left behind.
        /// </para>
        /// <para>
        /// <b>A catalog with no classes opens an empty screen rather than throwing.</b> That is the
        /// same call the capacity warning makes, one rung down: the Back button still works, so the
        /// player is on a blank screen they can leave instead of in a menu that raised an exception
        /// mid-tap. <c>RunTicker</c> and the old <c>MenuPresenter.FirstCharacterId</c> both throw on
        /// an empty catalog and both are about to start a run with it, which is a different question.
        /// </para>
        /// </remarks>
        public void Open()
        {
            if (_catalog is null)
            {
                return;
            }

            _descending = false;

            Draw();

            IsOpen = true;

            ShowScreen();
        }

        /// <summary>
        /// Takes the screen down without choosing. Nothing is written (rule 7).
        /// </summary>
        /// <remarks>
        /// The Back button's handler, and the way <see cref="MenuPresenter"/> is given its menu back.
        /// <c>PendingRun</c> is untouched on this path — a player who opens the screen and changes
        /// their mind gets the menu back with nothing set.
        /// </remarks>
        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;

            HideScreen();
        }

        /// <summary>
        /// Binds as many cards as the catalog has classes and clears the rest (rule 3).
        /// </summary>
        private void Draw()
        {
            if (_cards is null)
            {
                return;
            }

            int classes = _catalog.Characters.Count;

            if (classes > _cards.Length)
            {
                WarnAboutCapacityOnce(classes);
            }

            for (int i = 0; i < _cards.Length; i++)
            {
                ClassCard card = _cards[i];

                if (card == null)
                {
                    continue;
                }

                if (i < classes)
                {
                    card.Bind(_catalog.Characters[i], _localizer, OnCardChosen);
                }
                else
                {
                    card.Clear();
                }
            }
        }

        /// <summary>
        /// A card was tapped: record the run and load the scene it is played in (rule 1).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The latch goes down before anything else happens</b>, and every card goes dead with
        /// it. uGUI routes a click to the button it hit and says nothing about the ones beside it,
        /// so a second touch arriving during the load would otherwise find a live card and ask for a
        /// second run — <c>MenuPresenter.Descend</c>'s guard with three siblings instead of one.
        /// </para>
        /// <para>
        /// <c>async void</c>, which is otherwise a smell, is exactly right for what is effectively
        /// an event handler: the alternative is a continuation that has to be marshalled back to the
        /// main thread before it may touch a <see cref="Button"/>. Every path out of the await is
        /// handled here, so nothing escapes to the synchronisation context.
        /// </para>
        /// <para>
        /// <b>Nothing touches <c>SavedRun</c> and nothing deletes the file</b> (M2-14b rule 7).
        /// Abandoning a deep run by starting a new one is recorded by the new run's opening write,
        /// one scene later — the argument <c>MenuPresenter</c> made when this code was there.
        /// </para>
        /// </remarks>
        /// <param name="characterId">The class the tapped card was standing for.</param>
        private async void OnCardChosen(ContentId characterId)
        {
            if (_descending)
            {
                return;
            }

            _descending = true;

            SetCardsInteractable(false);

            try
            {
                _pending.Set(FirstModeId(), characterId, Environment.TickCount);

                await _loader.LoadAsync(SceneLoader.Run);
            }
            catch (Exception exception)
            {
                // The load failed, so this object is still alive and the screen is still up. Give
                // the cards back rather than stranding the player on a dead screen.
                _descending = false;

                SetCardsInteractable(true);

                Debug.LogException(exception, this);
            }
        }

        /// <summary>
        /// The mode this run plays: the first the catalog holds, which is Descent because it is the
        /// only one (GD §4.5).
        /// </summary>
        /// <remarks>
        /// <b><c>MenuPresenter.FirstModeId</c>, moved with the write it feeds (rule 2)</b> — the
        /// first, rather than <c>mode.descent</c> written here. GD §4.5's rule is that a mode is a
        /// data object and nothing in the code may assume Descent; an id literal on this screen
        /// would be exactly that assumption, and it would still compile and still run on the day a
        /// second mode ships. When there is a mode-select screen this becomes a second choice; until
        /// then it is a stand-in, and the class beside it has stopped being one.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The catalog holds no modes.</exception>
        private ContentId FirstModeId()
        {
            if (_catalog.Modes.Count == 0)
            {
                throw new InvalidOperationException(
                    "The content catalog holds no modes, so a chosen class has no mode to start a " +
                    "run with. Add a ModeDefinition to BootScope's mode list.");
            }

            return _catalog.Modes[0].Id;
        }

        /// <remarks>
        /// Once per screen, not once per open: at the cap this is true every time the screen goes up
        /// and a warning per open would bury the Console. <c>EnemyViews.WarnAboutCapacityOnce</c>'s
        /// rule, and <b>a warning rather than a throw</b> — a menu that refused to open would be a
        /// build nobody could play.
        /// </remarks>
        private void WarnAboutCapacityOnce(int classes)
        {
            if (_warnedAboutCapacity)
            {
                return;
            }

            _warnedAboutCapacity = true;

            Debug.LogWarning(
                $"More authored classes ({classes}) than the class-select screen has cards " +
                $"({_cards.Length}). The extras cannot be chosen. Add a card to " +
                "ClassSelect.prefab and drag it onto the presenter's Cards array (rule 3).",
                this);
        }

        /// <summary>
        /// Draws <paramref name="key"/> onto <paramref name="label"/>, if both are there.
        /// </summary>
        /// <remarks>
        /// <b>A missing label is silent and a missing localizer falls back to the key</b>, which is
        /// deliberately softer than <see cref="Start"/>'s throws: a screen with no cards is a screen
        /// no run can start from, where a screen with no <em>title</em> is one a player can still
        /// use. <c>MenuPresenter.Write</c>'s exact argument, and <c>ToString()</c> rather than
        /// <c>Key</c> for <c>TableLocalizer.Get</c>'s reason — a <c>default(LocKey)</c>'s
        /// <c>Key</c> is null.
        /// </remarks>
        private void Write(TMP_Text label, LocKey key)
        {
            if (label == null)
            {
                return;
            }

            label.text = _localizer is null ? key.ToString() : _localizer.Get(key);
        }

        /// <remarks>
        /// Alpha 1 on the same call, with no tween and no coroutine. The raycast block goes on with
        /// it: the screen is full-screen, so the menu underneath is covered rather than disabled —
        /// <c>PausePresenter.ShowPanel</c>'s shape, and why <see cref="MenuPresenter"/> does not
        /// have to switch its own buttons off.
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
            if (_root != null)
            {
                _root.alpha = 0f;
                _root.blocksRaycasts = false;
                _root.interactable = false;
            }

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
            if (_cards is null)
            {
                return;
            }

            for (int i = 0; i < _cards.Length; i++)
            {
                if (_cards[i] != null)
                {
                    _cards[i].SetInteractable(value);
                }
            }
        }

        /// <remarks>
        /// Removed before it is added, and with a named method rather than a lambda, so that a
        /// component injected twice — which VContainer does not do and a test does — closes once
        /// rather than once per injection. <c>PausePresenter.Wire</c>'s trick, for its reason.
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
