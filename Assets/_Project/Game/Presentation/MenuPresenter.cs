using System;
using Soulvail.Core.Content;
using Soulvail.Game.Composition;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, so the Menu scene's reference to this
// component would silently deserialise as null (M0-11).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The menu, which is one button. Tapping <c>Descend</c> records what the next run should be
    /// and loads the Run scene. See AR §3 — this is presentation: it decides nothing about the run
    /// beyond which mode, which class and which seed, and all three are choices a player makes,
    /// not outcomes a simulation computes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mode and the class are both the first their kind in the catalog, because there is
    /// neither a mode-select nor a class-select screen yet (M5-07 builds the second; the first is
    /// not planned, since V1 ships one mode). The seed is wall-clock derived, because no mode
    /// supplies one yet — Daily mode is where a seed stops being arbitrary.
    /// </para>
    /// <para>
    /// The two strings on screen are raw English, and this stub is the only place in the project
    /// where that is allowed. M6-10 replaces them with <see cref="LocKey"/>s; the parking lot in
    /// ROADMAP.md carries the reminder.
    /// </para>
    /// </remarks>
    public sealed class MenuPresenter : MonoBehaviour
    {
        [SerializeField] private Button _descend;

        private PendingRun _pending;
        private ContentCatalog _catalog;
        private SceneLoader _loader;

        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        [Inject]
        public void Construct(PendingRun pending, ContentCatalog catalog, SceneLoader loader)
        {
            _pending = pending ?? throw new ArgumentNullException(nameof(pending));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
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

            _descend.onClick.AddListener(Descend);
        }

        private void OnDisable()
        {
            if (_descend != null)
            {
                _descend.onClick.RemoveListener(Descend);
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

            try
            {
                _pending.Set(FirstModeId(), FirstCharacterId(), Environment.TickCount);

                await _loader.LoadAsync(SceneLoader.Run);
            }
            catch (Exception exception)
            {
                // The load failed, so this object is still alive and the menu is still on screen.
                // Give the button back rather than stranding the player on a dead menu.
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
