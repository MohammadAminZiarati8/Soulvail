using System;
using System.Globalization;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Sanctum.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Controls
{
    /// <summary>One of GD §13.3's four services, priced, and honest about whether it can be had.</summary>
    /// <remarks>
    /// <para>
    /// <b>It decides nothing.</b> The price, whether it may be bought and what the detail line says
    /// all arrive on <see cref="Show"/> from <c>SanctumPresenter</c>, which asks
    /// <c>SanctumShop</c> through the port. What this adds is the drawing: a name, a price in GD
    /// §16.4's reward gold, and one line that is either what the service does or why it cannot be had
    /// (M6-03a rule 3).
    /// </para>
    /// <para>
    /// <b>A refused row keeps its name and its price</b> — <c>SplashPresenter.DrawBranches</c>' shape
    /// (M5-08a rule 5): reading what you cannot have is half of what a shop is for, so only the
    /// detail line changes and the button goes dead.
    /// </para>
    /// </remarks>
    public sealed class ServiceRow : MonoBehaviour
    {
        private static readonly LocKey RerollKey = new LocKey("ui.sanctum.reroll");
        private static readonly LocKey BanishKey = new LocKey("ui.sanctum.banish");
        private static readonly LocKey HealKey = new LocKey("ui.sanctum.heal");
        private static readonly LocKey CleanseKey = new LocKey("ui.sanctum.cleanse");

        [Tooltip("The service's name, from ui.sanctum.<service>.")]
        [SerializeField] private TMP_Text _name;

        [Tooltip("What it costs right now, in Palette.Essence. Drawn on a refused row too.")]
        [SerializeField] private TMP_Text _price;

        [Tooltip("What the service does, or — on a refused row — why it cannot be had.")]
        [SerializeField] private TMP_Text _detail;

        [Tooltip("The whole row as one button. Dead when the shop refuses the service.")]
        [SerializeField] private Button _button;

        private Action<SanctumService> _onTapped;

        /// <summary>Which service this row is. Reported on a tap — <c>OfferCard</c>'s index, named.</summary>
        public SanctumService Service { get; private set; }

        /// <summary>Whether the row is currently drawn.</summary>
        public bool IsShown => gameObject.activeSelf;

        /// <summary>Whether its button is live — <c>SanctumShop.CanBuy</c> and nothing else (rule 3).</summary>
        public bool IsAffordable => _button != null && _button.interactable;

        /// <summary>Draws <paramref name="service"/> and reports a tap to <paramref name="onTapped"/>.</summary>
        /// <param name="service">Which of the four this row is.</param>
        /// <param name="price">What it costs right now — <c>SanctumShop.PriceOf</c>.</param>
        /// <param name="canBuy">Whether it may be bought — <c>SanctumShop.CanBuy</c>.</param>
        /// <param name="detail">
        /// What it does, or why it cannot be had. One key either way — rule 3.
        /// </param>
        /// <param name="localizer">What turns the name and the detail into words.</param>
        /// <param name="onTapped">Called with <paramref name="service"/> when the row is tapped.</param>
        /// <exception cref="ArgumentNullException"><paramref name="localizer"/> or <paramref name="onTapped"/> is null.</exception>
        public void Show(
            SanctumService service, int price, bool canBuy, LocKey detail,
            ILocalizer localizer, Action<SanctumService> onTapped)
        {
            if (localizer is null)
            {
                throw new ArgumentNullException(
                    nameof(localizer),
                    "A row with no localizer would draw its keys — a shop that reads like a build "
                        + "with no content.");
            }

            _onTapped = onTapped ?? throw new ArgumentNullException(
                nameof(onTapped),
                "A row with nobody to report to is a price the player can tap and nothing happens.");

            Service = service;

            if (_name != null)
            {
                _name.text = localizer.Get(NameOf(service));
            }

            if (_price != null)
            {
                // A string per draw, and a draw is a tap: nothing here runs per frame (rule 12).
                _price.text = price.ToString(CultureInfo.InvariantCulture);
                _price.color = Palette.Essence;
            }

            if (_detail != null)
            {
                _detail.text = localizer.Get(detail);
            }

            if (_button != null)
            {
                _button.onClick.RemoveListener(Raise);
                _button.onClick.AddListener(Raise);
                _button.interactable = canBuy;
            }

            gameObject.SetActive(true);
        }

        /// <summary>Takes the row off the screen and drops its listener.</summary>
        public void Hide()
        {
            _onTapped = null;

            if (_button != null)
            {
                _button.onClick.RemoveListener(Raise);
            }

            gameObject.SetActive(false);
        }

        /// <summary>Turns the button off without changing what the row draws.</summary>
        public void SetInteractable(bool value)
        {
            if (_button != null)
            {
                _button.interactable = value;
            }
        }

        /// <remarks>
        /// A presentation lookup over a closed enum of four — <c>OfferCard.Tint</c>'s shape and
        /// <c>SanctumShop.PriceOf</c>'s, never a dispatch that should have been polymorphism.
        /// </remarks>
        private static LocKey NameOf(SanctumService service) => service switch
        {
            SanctumService.Reroll => RerollKey,
            SanctumService.Banish => BanishKey,
            SanctumService.Heal => HealKey,
            _ => CleanseKey,
        };

        /// <remarks>A named method, so <see cref="Hide"/> can find it — <c>OfferCard.Raise</c>'s reason.</remarks>
        private void Raise()
        {
            _onTapped?.Invoke(Service);
        }
    }
}
