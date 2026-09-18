using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Composition;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, so the Menu scene's reference to this
// component would silently deserialise as null (M0-11).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The menu, which is two buttons — one of them usually hidden. Tapping <c>Descend</c> records
    /// what the next run should be and loads the Run scene; tapping <c>Continue</c> does the same
    /// for the run that was on disk at launch. See AR §3 — this is presentation: it decides nothing
    /// about the run beyond which mode, which class and which seed, and all three are choices a
    /// player makes, not outcomes a simulation computes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mode and the class are both the first their kind in the catalog, because there is
    /// neither a mode-select nor a class-select screen yet (M5-07 builds the second; the first is
    /// not planned, since V1 ships one mode). The seed is wall-clock derived, because no mode
    /// supplies one yet — Daily mode is where a seed stops being arbitrary. A <c>Continue</c>
    /// chooses none of the three: all of them come off the snapshot (M2-14b rule 7).
    /// </para>
    /// <para>
    /// <b><c>Descend</c> with a save present does not delete it</b>, and the absence of that line is
    /// deliberate. The new run's opening write overwrites the file on its first frame (M2-14a rule
    /// 1), so the abandonment is recorded by the thing that already records every run — and the
    /// alternative would put an <c>ISaveStore</c> and a fire-and-forget delete into a presenter.
    /// A confirmation dialog before abandoning a deep run is real UX and it is M8-02's, with the
    /// string M6-10 owns.
    /// </para>
    /// <para>
    /// <b>The three strings on screen stopped being raw English at M3-14a</b> (rule 8). They were
    /// typed into <c>Menu.unity</c> from M0-17, and the ROADMAP's parking-lot line said they would
    /// become <see cref="LocKey"/>s <em>"when <c>ILocalizer</c> and the English tables land"</em> —
    /// which is this task, three milestones early. <b>Three, not the two that line names</b>:
    /// <c>Continue</c> was added by M3-07b's resume flow and counted by nobody. So M6-10 inherits
    /// no English typed into this scene, and AR §11.5's <em>"no raw user-facing string anywhere"</em>
    /// is a thing a test asserts here rather than an aspiration. Showing anything <em>about</em> the
    /// saved run on the button — "Stage 9, 12 minutes ago" — needs a format as well as a key, and is
    /// still M8-02's.
    /// </para>
    /// </remarks>
    public sealed class MenuPresenter : MonoBehaviour
    {
        /// <summary>The game's name. Shared with <c>Boot.unity</c>'s splash, which nothing resolves.</summary>
        private static readonly LocKey TitleKey = new LocKey("ui.app.title");

        /// <summary>The button that starts a new run.</summary>
        private static readonly LocKey DescendKey = new LocKey("ui.menu.descend");

        /// <summary>The button that resumes the run on disk (M3-07b).</summary>
        private static readonly LocKey ContinueKey = new LocKey("ui.menu.continue");

        [SerializeField] private Button _descend;

        /// <summary>
        /// Offered only when there is a run to continue, by being switched off entirely rather than
        /// merely made non-interactable: a greyed-out <c>Continue</c> on a fresh install advertises
        /// a feature the player cannot use and cannot find out how to.
        /// </summary>
        [SerializeField] private Button _continue;

        [Tooltip("The game's name over the menu. Written from ui.app.title on enable, so the " +
                 "scene's own value is a placeholder — see the class remarks.")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("The Descend button's label, written from ui.menu.descend.")]
        [SerializeField] private TMP_Text _descendLabel;

        [Tooltip("The Continue button's label, written from ui.menu.continue.")]
        [SerializeField] private TMP_Text _continueLabel;

        private PendingRun _pending;
        private SavedRun _saved;
        private ContentCatalog _catalog;
        private SceneLoader _loader;
        private ILocalizer _localizer;

        /// <param name="localizer">
        /// What turns this screen's three keys into words (M3-14a rule 8). Resolved from
        /// <c>BootScope</c>, which is why the localizer is registered there rather than in
        /// <c>RunScope</c> — <b>the Menu needs it and has no run</b> (rule 4).
        /// </param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        [Inject]
        public void Construct(
            PendingRun pending,
            SavedRun saved,
            ContentCatalog catalog,
            SceneLoader loader,
            ILocalizer localizer)
        {
            _pending = pending ?? throw new ArgumentNullException(nameof(pending));
            _saved = saved ?? throw new ArgumentNullException(nameof(saved));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        /// <remarks>
        /// Subscribed here and dropped in <see cref="OnDisable"/> rather than held for the object's
        /// life: AR §8's rule for views, and the reason a disabled menu cannot start a run.
        /// </remarks>
        private void OnEnable()
        {
            if (_descend == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(MenuPresenter)} has no {nameof(Button)} assigned. Drag the Descend " +
                    "button in this scene onto its Descend field — without it the menu has no way " +
                    "into a run.");
            }

            if (_continue == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(MenuPresenter)} has no Continue {nameof(Button)} assigned. Drag the " +
                    "Continue button in this scene onto its Continue field — without it a run that " +
                    "survived an app kill can never be resumed, and nothing would say so.");
            }

            _descend.onClick.AddListener(Descend);
            _continue.onClick.AddListener(Continue);

            // Written on every enable rather than once at injection, for the reason the Continue
            // button's visibility is decided here: this is the screen the app comes back to after a
            // death, and drawing it at the moment it goes on screen is the only reading that is true
            // when the player sees it. Three labels, and none of them typed into the scene (rule 8).
            Write(_title, TitleKey);
            Write(_descendLabel, DescendKey);
            Write(_continueLabel, ContinueKey);

            // Decided here, on every enable, rather than once at injection. The Menu is the screen
            // the app comes back to after a death, so the answer changes while this object is
            // alive — and reading it at the moment the menu goes on screen is the only reading that
            // is true when the player sees it.
            _continue.gameObject.SetActive(_saved.IsPresent);
        }

        /// <summary>
        /// Draws <paramref name="key"/> onto <paramref name="label"/>, if both are there.
        /// </summary>
        /// <remarks>
        /// <b>A missing label is silent and a missing localizer falls back to the key</b>, which is
        /// deliberately softer than the two <c>MissingReferenceException</c>s above it: a button with
        /// no <c>Button</c> is a menu with no way into a run, where a button with no <em>label</em>
        /// is a menu the player can still use. <c>TreeViewPresenter.Resolve</c>'s answer, and
        /// <c>ToString()</c> rather than <c>Key</c> for <c>TableLocalizer.Get</c>'s reason.
        /// </remarks>
        private void Write(TMP_Text label, LocKey key)
        {
            if (label == null)
            {
                return;
            }

            label.text = _localizer is null ? key.ToString() : _localizer.Get(key);
        }

        private void OnDisable()
        {
            if (_descend != null)
            {
                _descend.onClick.RemoveListener(Descend);
            }

            if (_continue != null)
            {
                _continue.onClick.RemoveListener(Continue);
            }
        }

        /// <summary>
        /// Records the run and loads the scene it is played in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The button stops accepting taps before anything else happens, and that ordering is the
        /// whole guard against a double-tap: uGUI routes a click to the button it hit, so a second
        /// touch arriving during the load finds a control that is no longer interactable. Nothing
        /// after the await touches this object, because by then the scene it lived in is gone.
        /// </para>
        /// <para>
        /// <c>async void</c>, which is otherwise a smell, is exactly right for an event handler:
        /// the alternative is a continuation that has to be marshalled back to the main thread
        /// before it may touch a <see cref="Button"/>. Every path out of the await is handled here,
        /// so nothing escapes to the synchronisation context.
        /// </para>
        /// </remarks>
        private async void Descend()
        {
            _descend.interactable = false;

            // Its sibling too, for the reason above with the nouns swapped: uGUI refuses a second
            // touch on the button that was hit, and says nothing about the one beside it.
            _continue.interactable = false;

            try
            {
                // Nothing touches SavedRun and nothing deletes the file (M2-14b rule 7). Abandoning
                // a deep run is recorded by the new run's opening write, one scene later.
                _pending.Set(FirstModeId(), FirstCharacterId(), Environment.TickCount);

                await _loader.LoadAsync(SceneLoader.Run);
            }
            catch (Exception exception)
            {
                // The load failed, so this object is still alive and the menu is still on screen.
                // Give the buttons back rather than stranding the player on a dead menu.
                _descend.interactable = true;
                _continue.interactable = true;
                Debug.LogException(exception, this);
            }
        }

        /// <summary>
        /// Records the run on disk as the one to play, and loads the scene it is played in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The same path <see cref="Descend"/> takes, differing in one call.</b> The mode, the
        /// class and the seed all come off the snapshot rather than being chosen here, which is
        /// what makes a resume unable to disagree with the run it is resuming — and is why this
        /// method reads nothing from the catalog at all.
        /// </para>
        /// <para>
        /// <b>Nothing is deleted, here or anywhere on this path</b> (M2-14b rule 10). The file
        /// survives until the next stage boundary overwrites it or death clears it, so a player
        /// interrupted twice inside one stage resumes twice. The cost is that a resumed run rewinds
        /// to the top of its stage each time, which the owner ruled acceptable at M2-00e.
        /// </para>
        /// </remarks>
        private async void Continue()
        {
            // Both buttons, not just this one: the guard below is about not starting two runs, and
            // a Descend tapped during the load would start the other kind.
            _continue.interactable = false;
            _descend.interactable = false;

            try
            {
                // Read before the await, while this object is certainly alive, and read through
                // Value rather than guarded here — the button is only on screen because IsPresent
                // was true, so a throw at this line is a wiring fault worth hearing about.
                _pending.Resume(_saved.Value);

                await _loader.LoadAsync(SceneLoader.Run);
            }
            catch (Exception exception)
            {
                _continue.interactable = true;
                _descend.interactable = true;
                Debug.LogException(exception, this);
            }
        }

        /// <summary>
        /// The mode this run plays: the first the catalog holds, which is Descent because it is
        /// the only one (GD §4.5).
        /// </summary>
        /// <remarks>
        /// The first, rather than <c>mode.descent</c> written here. GD §4.5's rule is that a mode
        /// is a data object and nothing in the code may assume Descent — an id literal in the
        /// menu would be exactly that assumption, and it would still compile and still run on the
        /// day a second mode ships. When there is a mode-select screen this becomes the player's
        /// choice; until then it is the same stand-in <see cref="FirstCharacterId"/> is.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The catalog holds no modes.</exception>
        private ContentId FirstModeId()
        {
            if (_catalog.Modes.Count == 0)
            {
                throw new InvalidOperationException(
                    "The content catalog holds no modes, so Descend has no mode to start a run " +
                    "with. Add a ModeDefinition to BootScope's mode list.");
            }

            return _catalog.Modes[0].Id;
        }

        /// <summary>
        /// The class this run plays: the first the catalog holds, until there is a screen to
        /// choose one with (M5-07).
        /// </summary>
        /// <exception cref="InvalidOperationException">The catalog holds no characters.</exception>
        private ContentId FirstCharacterId()
        {
            if (_catalog.Characters.Count == 0)
            {
                // The same failure RunTicker names, from the other side of the scene load. Guarded
                // rather than left to IndexOutOfRangeException, which would name a list rather
                // than the asset list that is actually empty.
                throw new InvalidOperationException(
                    "The content catalog holds no characters, so Descend has no class to start a " +
                    "run with. Add a CharacterDefinition to BootScope's character list.");
            }

            return _catalog.Characters[0].Id;
        }
    }
}
