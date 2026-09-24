using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and ClassSelect.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Controls
{
    /// <summary>
    /// One class, as a card: a name, a sentence, three numbers and a button. CH §3, GD §16.1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It holds a <see cref="ContentId"/> where <c>OfferCard</c> holds an index</b>, and the
    /// difference is what each one is standing for. A level-up card reports a <em>position on a
    /// screen</em> because <c>IProgressionCommands.ChooseOffer</c> takes exactly that, and three
    /// cards are repainted in place between picks. This card reports a <em>class</em>: nothing
    /// repaints it mid-screen, the id is what <c>PendingRun.Set</c> wants, and an index would have
    /// to be resolved back through the catalog by the one object that already has the id in hand.
    /// </para>
    /// <para>
    /// <b>It draws five things and invents none of them</b> (M5-07 rule 4). The name and the
    /// description come from <see cref="ILocalizer"/>; the three figures come off the
    /// <see cref="CharacterSpec"/> — <c>MaxHp</c>, <c>Movement.Speed</c>, and the weapon's damage
    /// times its swings a second. <b>That last product is <c>Weapon.DpsOneSecond</c>'s arithmetic
    /// spelled out over the spec</b>, because that property lives on the live <c>Weapon</c>
    /// (<c>PlayerCombat.cs</c>) and a menu has no run to have one — so
    /// <c>ClassSelectPresenterTests</c> asserts the card and the live weapon agree, which is what
    /// stops the two spellings drifting.
    /// </para>
    /// <para>
    /// <b>Three figures and not nine.</b> CH §3's table has nine columns; a phone card has room for
    /// the three a player can act on, and everything else that separates two classes is a sentence
    /// in the description rather than a fourth row of numbers.
    /// </para>
    /// <para>
    /// <b>It is handed the port rather than injected</b> — <c>OfferCard</c>'s rule (M3-14a rule 11)
    /// for a reason that holds here twice over: the cards are authored on the prefab and nothing
    /// injects one individually, so <see cref="ILocalizer"/> arrives as one more argument on
    /// <see cref="Bind"/>, which already takes a <see cref="CharacterSpec"/>.
    /// </para>
    /// <para>
    /// <b>Two overloads rather than a <c>bool</c> on <see cref="Bind"/></b> (M6-09b). An owned card
    /// plays and a locked one buys, so the two differ in what a tap <em>means</em>, not only in
    /// whether it is live — and <see cref="Bind"/>'s signature is unchanged, so the starter's path
    /// through this file is the one M5-07 shipped. <see cref="BindLocked"/> keeps the name and the
    /// three numbers and adds a price and, where a deed can be done, one line saying so (M6-09b
    /// rules 2 and 4). It decides none of it: the price, whether it can be paid, and whether the
    /// deed line exists are all the presenter's arguments.
    /// </para>
    /// <para>
    /// Every piece is written through a Unity-null check rather than assumed:
    /// <c>ClassSelectPresenter.Start</c> is what refuses an undressed screen by name, and this class
    /// only has to not throw on the way there — <c>OfferCard</c>'s bargain.
    /// </para>
    /// </remarks>
    public sealed class ClassCard : MonoBehaviour
    {
        /// <summary>
        /// Hit points, as the card says them: <c>"{0:0} HP"</c> in English.
        /// </summary>
        /// <remarks>
        /// <b>A row since M6-10, where it was a <c>const</c></b>. It was kept out of the table as a
        /// number format rather than a sentence, beside <c>HudPresenter</c>'s <c>"{0:0}/{1:0}"</c>.
        /// But <c>HP</c> and <c>DPS</c> are English words, and under the pseudo-locale they are
        /// exactly what M6-10 rule 3 exists to find. The HUD's readout is digits and a slash, and it
        /// stays a <c>const</c>.
        /// </remarks>
        private static readonly LocKey HpKey = new LocKey("ui.classselect.hp");

        /// <summary>
        /// Metres a second, to one place: 3.0 and 3.1 are a real difference here. M6-10 rule 5's own
        /// example — <c>3.4 m/s</c> is <c>3,4 m/s</c> to a German reader.
        /// </summary>
        private static readonly LocKey SpeedKey = new LocKey("ui.classselect.speed");

        /// <summary>Damage a second, rounded. See the class remarks on where the product comes from.</summary>
        private static readonly LocKey DpsKey = new LocKey("ui.classselect.dps");

        /// <summary>The price on a card whose balance is short: a figure, and nothing to tap.</summary>
        private static readonly LocKey PriceKey = new LocKey("ui.classselect.locked.price");

        /// <summary>
        /// The price on a card that can be bought — the same figure, said as an offer, so a live
        /// locked card reads as a purchase rather than as a class to play (M6-09b rule 3).
        /// </summary>
        private static readonly LocKey BuyKey = new LocKey("ui.classselect.locked.buy");

        [Tooltip("The class's name, resolved through ILocalizer — see the class remarks.")]
        [SerializeField] private TMP_Text _name;

        [Tooltip("One line about the class, resolved the same way. This is where everything CH §3's " +
                 "table says and the three figures below cannot goes.")]
        [SerializeField] private TMP_Text _description;

        [Tooltip("Starting maximum health, off the spec.")]
        [SerializeField] private TMP_Text _hp;

        [Tooltip("Movement speed in metres a second, off the spec.")]
        [SerializeField] private TMP_Text _speed;

        [Tooltip("Damage a second: the weapon's damage times its swings a second, which is " +
                 "Weapon.DpsOneSecond's arithmetic over the authored numbers.")]
        [SerializeField] private TMP_Text _weapon;

        [Tooltip("The whole card as one button. A card is a target for a thumb, so the hit area is " +
                 "the card rather than a word on it — OfferCard's rule.")]
        [SerializeField] private Button _button;

        [Tooltip("What a locked class costs, in Soul Shards. Switched off on an owned card.")]
        [SerializeField] private TMP_Text _price;

        [Tooltip("What else would earn a locked class — drawn only for a deed this build can do " +
                 "(M6-09b rule 4). Switched off otherwise.")]
        [SerializeField] private TMP_Text _deed;

        /// <summary>
        /// Where a tap goes. Held rather than wired through the Inspector so that the presenter can
        /// hand over a method it owns, and so a cleared card reports to nobody.
        /// </summary>
        private Action<ContentId> _onChosen;

        /// <summary>
        /// Where a tap on a locked card goes. Exactly one of this and <see cref="_onChosen"/> is set
        /// while the card is drawn, so a card cannot both buy and play.
        /// </summary>
        private Action<ContentId> _onUnlockTapped;

        /// <summary>Which of the three states this card was last drawn in.</summary>
        public ClassCardState State { get; private set; }

        /// <summary>
        /// The id this card is standing for, or <c>default</c> when it is not in use.
        /// </summary>
        public ContentId CharacterId { get; private set; }

        /// <summary>Whether the card is currently drawn. <c>OfferCard.IsShown</c>'s reachable read.</summary>
        public bool IsShown => gameObject.activeSelf;

        /// <summary>
        /// Draws <paramref name="spec"/> and becomes tappable.
        /// </summary>
        /// <param name="spec">The class to draw.</param>
        /// <param name="localizer">What turns the spec's two <see cref="LocKey"/>s into words.</param>
        /// <param name="onChosen">
        /// Called with <see cref="CharacterId"/> when the card is tapped. It is the presenter's
        /// method, and it is what writes <c>PendingRun</c> — this file writes nothing.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="spec"/>, <paramref name="localizer"/> or <paramref name="onChosen"/> is
        /// null.
        /// </exception>
        public void Bind(CharacterSpec spec, ILocalizer localizer, Action<ContentId> onChosen)
        {
            if (spec is null)
            {
                throw new ArgumentNullException(
                    nameof(spec),
                    "A card with no class to draw is a presenter that read past the end of the "
                        + "catalog — the prefab carries three cards and the catalog may hold fewer "
                        + "(rule 3).");
            }

            if (localizer is null)
            {
                throw new ArgumentNullException(
                    nameof(localizer),
                    "A card with no localizer would silently draw its two keys, which is a screen "
                        + "that looks like a build with no content rather than one with no wiring.");
            }

            if (onChosen is null)
            {
                throw new ArgumentNullException(
                    nameof(onChosen),
                    "A card with nobody to report to is a card the player can tap and no run "
                        + "starts, which is the one failure here that looks like a frozen game.");
            }

            _onChosen = onChosen;
            _onUnlockTapped = null;

            DrawClass(spec, localizer);

            // An owned card has no price and nothing to prove: both lines go, so a card bought a
            // moment ago stops reading as a shop the frame it is redrawn.
            Hide(_price);
            Hide(_deed);

            State = ClassCardState.Owned;

            Arm(true);
        }

        /// <summary>
        /// Draws <paramref name="spec"/> as a class the player does not own: its numbers, its
        /// price, what proves it, and a button that buys rather than one that plays — M6-09b rule 2.
        /// </summary>
        /// <param name="spec">The class to draw.</param>
        /// <param name="price">What it costs, in Soul Shards. Above zero — a free class is owned.</param>
        /// <param name="affordable">
        /// Whether the price can be paid right now — <c>ClassUnlocks.CanBuy</c> and nothing else.
        /// It is the button's <c>interactable</c>, and the price line's wording.
        /// </param>
        /// <param name="deed">
        /// The one line that says what else would earn it, or <c>default</c> for a class no deed can
        /// reach — rule 4. Formatted with the spec's <c>UnlockSpec.DeedStage</c>.
        /// </param>
        /// <param name="localizer">What turns every key on the card into words.</param>
        /// <param name="onUnlockTapped">
        /// Called with <see cref="CharacterId"/> when the card is tapped. The presenter's method; it
        /// re-asks <c>CanBuy</c> before it spends anything.
        /// </param>
        /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="price"/> is not above zero.</exception>
        public void BindLocked(
            CharacterSpec spec, int price, bool affordable, LocKey deed,
            ILocalizer localizer, Action<ContentId> onUnlockTapped)
        {
            if (spec is null)
            {
                throw new ArgumentNullException(
                    nameof(spec), "A locked card with no class to draw sells nothing.");
            }

            if (localizer is null)
            {
                throw new ArgumentNullException(
                    nameof(localizer),
                    "A locked card with no localizer would draw its price as a key.");
            }

            if (onUnlockTapped is null)
            {
                throw new ArgumentNullException(
                    nameof(onUnlockTapped),
                    "A priced card with nobody to report to takes a tap and spends nothing, which "
                        + "reads as a broken shop.");
            }

            if (price <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(price),
                    price,
                    "A locked class has a price above zero; a free class is owned and is drawn by "
                        + "Bind (M6-09a rule 3).");
            }

            _onChosen = null;
            _onUnlockTapped = onUnlockTapped;

            DrawClass(spec, localizer);

            if (_price != null)
            {
                _price.text = localizer.Format(affordable ? BuyKey : PriceKey, price);

                // GD §16.4's reward gold when it can be had — RunEndPresenter's and the Sanctum's
                // colour for the same currency — and the neutral grey of a fact when it cannot.
                _price.color = affordable ? Palette.Essence : Palette.Neutral;
                _price.gameObject.SetActive(true);
            }

            if (_deed != null)
            {
                if (deed.Key is null)
                {
                    Hide(_deed);
                }
                else
                {
                    // Through the row, so a language that says "reach stage 20" with the number
                    // elsewhere can put it there (M6-10 rule 5).
                    _deed.text = localizer.Format(deed, spec.Unlock?.DeedStage ?? 0);
                    _deed.gameObject.SetActive(true);
                }
            }

            State = ClassCardState.Locked;

            Arm(affordable);
        }

        /// <summary>The name, the sentence and the three numbers — the same in every state (rule 2).</summary>
        private void DrawClass(CharacterSpec spec, ILocalizer localizer)
        {
            CharacterId = spec.Id;

            if (_name != null)
            {
                // A key with no row resolves to its own text rather than to nothing, so a missing
                // row is a card that diagnoses itself (M3-14a rule 1).
                _name.text = localizer.Get(spec.NameKey);
            }

            if (_description != null)
            {
                _description.text = localizer.Get(spec.DescriptionKey);
            }

            if (_hp != null)
            {
                _hp.text = localizer.Format(HpKey, spec.MaxHp);
            }

            if (_speed != null)
            {
                _speed.text = localizer.Format(SpeedKey, spec.Movement.Speed);
            }

            if (_weapon != null)
            {
                _weapon.text = localizer.Format(DpsKey, DpsOf(spec));
            }
        }

        /// <summary>Wires the one listener, sets the button, and shows the card.</summary>
        private void Arm(bool interactable)
        {
            // Re-armed every draw, and cleared first: a card bound twice — which a screen reopened
            // does — would otherwise carry the previous binding's listener as well and report the
            // tap twice, starting two runs.
            if (_button != null)
            {
                _button.onClick.RemoveListener(Raise);
                _button.onClick.AddListener(Raise);
            }

            SetInteractable(interactable);

            gameObject.SetActive(true);
        }

        private static void Hide(TMP_Text label)
        {
            if (label != null)
            {
                label.text = string.Empty;
                label.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Stops standing for anything and switches off (rule 3).
        /// </summary>
        /// <remarks>
        /// The prefab carries CH §3's whole roster of three and the catalog holds two, so this is
        /// the ordinary state of the third card rather than an error path — and it is what makes the
        /// Emberwright's arrival at M6-07 a card being filled rather than a prefab being edited.
        /// </remarks>
        public void Clear()
        {
            // Dropped rather than left dangling, so a cleared card cannot report into a presenter
            // that has stopped listening.
            _onChosen = null;
            _onUnlockTapped = null;
            CharacterId = default;
            State = ClassCardState.Hidden;

            if (_button != null)
            {
                _button.onClick.RemoveListener(Raise);
            }

            gameObject.SetActive(false);
        }

        /// <summary>
        /// Turns the card's button on or off without changing what it draws.
        /// </summary>
        /// <remarks>
        /// What the tap guard spends: every card goes dead the moment one of them is tapped, because
        /// the scene load that follows is awaited and a second thumb landing during it would ask for
        /// a second run. <c>OfferCard.SetInteractable</c>'s argument, one screen earlier.
        /// </remarks>
        /// <param name="value">Whether the card may be tapped.</param>
        public void SetInteractable(bool value)
        {
            if (_button != null)
            {
                _button.interactable = value;
            }
        }

        /// <summary>
        /// Whether the card's button is live. Read by the tests, and by nothing in the game.
        /// </summary>
        public bool IsInteractable => _button != null && _button.interactable;

        /// <summary>
        /// What the weapon does in a second, off the authored numbers.
        /// </summary>
        /// <remarks>
        /// <c>Weapon.DpsOneSecond</c> is <c>Damage.Value * FireRate.Value</c> over the live
        /// <c>Stat</c>s, which a menu has none of. This is the same product over the spec those
        /// stats are seeded from, which is why the two agree on a fresh run and why a row asserts
        /// it rather than assuming it.
        /// </remarks>
        private static float DpsOf(CharacterSpec spec) =>
            spec.Weapon.Damage * spec.Weapon.SwingsPerSecond;

        /// <remarks>
        /// A named method rather than a lambda, so that <c>RemoveListener</c> in <see cref="Bind"/>
        /// and <see cref="Clear"/> can actually find it — a closure would be a different delegate
        /// every draw and the card would accumulate one listener per binding. <c>OfferCard</c>'s
        /// trick, for its reason.
        /// </remarks>
        private void Raise()
        {
            if (State == ClassCardState.Locked)
            {
                _onUnlockTapped?.Invoke(CharacterId);
                return;
            }

            _onChosen?.Invoke(CharacterId);
        }
    }
}
