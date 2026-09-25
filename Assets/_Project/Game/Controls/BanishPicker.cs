using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Sanctum.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Controls
{
    /// <summary>
    /// GD §13.3's <em>"remove a node from this run's offer pool"</em>, as a list of what is left.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The second page of the Sanctum</b>, <c>SplashPresenter</c>'s two-page shape (M6-03a rule 7):
    /// Banish takes an argument and the other three services do not, so it is two taps.
    /// </para>
    /// <para>
    /// <b>Its rows are a name and a button and nothing else, and they are not <c>TreeNodeView</c></b>
    /// (rule 8). That class carries no <c>Button</c> on purpose — CH §5.1's tree is not a screen you
    /// can buy from — and putting a tap on it for this list would delete half of that decision.
    /// </para>
    /// <para>
    /// <b>Nothing is instantiated.</b> The rows are authored on the prefab — twelve, which is
    /// <c>TreeRules.Count</c> for every tree this build ships — and a longer list draws the first
    /// <see cref="Capacity"/> and no more. CH §5's full tree is 27, and M7-04 is the task that authors
    /// it and has to give this list a scroll.
    /// </para>
    /// </remarks>
    public sealed class BanishPicker : MonoBehaviour
    {
        private static readonly LocKey PromptKey = new LocKey("ui.sanctum.banish.prompt");
        private static readonly LocKey CancelKey = new LocKey("ui.sanctum.banish.cancel");

        [Tooltip("\"Choose a node to banish\", from ui.sanctum.banish.prompt.")]
        [SerializeField] private TMP_Text _prompt;

        [Tooltip("One button per banishable node, in tree order. Twelve: the shipped trees' size.")]
        [SerializeField] private Button[] _rows = new Button[12];

        [Tooltip("Each row's node name, beside its button.")]
        [SerializeField] private TMP_Text[] _names = new TMP_Text[12];

        [Tooltip("Back to the four services, spending nothing.")]
        [SerializeField] private Button _cancel;

        [Tooltip("The cancel button's word, from ui.sanctum.banish.cancel.")]
        [SerializeField] private TMP_Text _cancelLabel;

        /// <summary>The ids the rows are drawing, row for row. Sized once, to <see cref="Capacity"/>.</summary>
        private ContentId[] _drawn;

        private int _count;
        private Action<ContentId> _onPicked;
        private Action _onCancelled;

        /// <summary>How many rows this prefab authors — the bound rule 7 is written against.</summary>
        public int Capacity => _rows is null ? 0 : _rows.Length;

        /// <summary>Whether the list is up.</summary>
        public bool IsOpen => gameObject.activeSelf;

        /// <summary>How many rows the last <see cref="Open"/> drew.</summary>
        public int DrawnCount => IsOpen ? _count : 0;

        /// <summary>
        /// Draws <paramref name="count"/> of <paramref name="candidates"/> and reports a tap.
        /// Rows past <paramref name="count"/> are hidden, never drawn empty.
        /// </summary>
        /// <param name="candidates">What <c>BanishableInto</c> wrote, in tree order.</param>
        /// <param name="count">How many of them are real.</param>
        /// <param name="catalog">What a node is called.</param>
        /// <param name="localizer">What turns a name into words.</param>
        /// <param name="onPicked">Called with the tapped row's node.</param>
        /// <param name="onCancelled">Called when the list is left without a pick.</param>
        /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
        public void Open(
            IReadOnlyList<ContentId> candidates, int count, ContentCatalog catalog,
            ILocalizer localizer, Action<ContentId> onPicked, Action onCancelled)
        {
            if (candidates is null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            if (catalog is null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (localizer is null)
            {
                throw new ArgumentNullException(nameof(localizer));
            }

            _onPicked = onPicked ?? throw new ArgumentNullException(nameof(onPicked));
            _onCancelled = onCancelled ?? throw new ArgumentNullException(nameof(onCancelled));

            if (_drawn is null || _drawn.Length != Capacity)
            {
                _drawn = new ContentId[Capacity];
            }

            // A real limit rather than a bug (rule 7): the first Capacity, and never a throw.
            _count = Math.Max(0, Math.Min(Math.Min(count, candidates.Count), Capacity));

            Write(_prompt, localizer, PromptKey);
            Write(_cancelLabel, localizer, CancelKey);

            for (int i = 0; i < Capacity; i++)
            {
                Button row = _rows[i];
                bool drawn = i < _count;

                _drawn[i] = drawn ? candidates[i] : default;

                if (row != null)
                {
                    row.gameObject.SetActive(drawn);
                    row.interactable = drawn;
                    row.onClick.RemoveAllListeners();

                    if (drawn)
                    {
                        int index = i;
                        row.onClick.AddListener(() => Pick(index));
                    }
                }

                TMP_Text name = _names is not null && i < _names.Length ? _names[i] : null;

                if (drawn && name != null)
                {
                    name.text = localizer.Get(catalog.Skill(candidates[i]).NameKey);
                }
            }

            if (_cancel != null)
            {
                _cancel.onClick.RemoveListener(Cancel);
                _cancel.onClick.AddListener(Cancel);
            }

            gameObject.SetActive(true);
        }

        /// <summary>Takes the list down and drops both callbacks. Sends nothing.</summary>
        public void Close()
        {
            _onPicked = null;
            _onCancelled = null;
            _count = 0;

            for (int i = 0; i < Capacity; i++)
            {
                if (_rows[i] != null)
                {
                    _rows[i].onClick.RemoveAllListeners();
                }
            }

            if (_cancel != null)
            {
                _cancel.onClick.RemoveListener(Cancel);
            }

            gameObject.SetActive(false);
        }

        private void Pick(int index)
        {
            if (index < 0 || index >= _count)
            {
                return;
            }

            _onPicked?.Invoke(_drawn[index]);
        }

        private void Cancel()
        {
            _onCancelled?.Invoke();
        }

        private static void Write(TMP_Text label, ILocalizer localizer, LocKey key)
        {
            if (label != null)
            {
                label.text = localizer.Get(key);
            }
        }
    }
}
