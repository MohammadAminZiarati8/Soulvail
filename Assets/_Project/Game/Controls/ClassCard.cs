using System;
using System.Globalization;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
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
    /// <b><see cref="Bind"/> takes no <c>bool interactable</c>, deliberately</b> (rule 6). CH §6
    /// makes classes the one thing meta-progression buys, and <c>PlayerProfile</c> v3 carries no
    /// unlock set — so every authored class is selectable, M6-09 is where a card learns to be
    /// locked, and a parameter with one legal value would be a promise that task has to keep rather
    /// than a feature this one shipped.
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
        /// Hit points, as the card says them. A number format rather than a sentence, so it is a
        /// <c>const</c> here rather than a row in <c>English.asset</c> — <c>SkillRow</c>'s
        /// <c>"{0:0.0} s"</c> and <c>HudPresenter</c>'s <c>"{0:0}/{1:0}"</c>, and the distinction
        /// <c>TableLocalizerTests</c> draws when it keeps the HP readout out of AR §11.5's sweep.
        /// </summary>
        private const string HpFormat = "{0:0} HP";

        /// <summary>Metres a second, to one place: 3.0 and 3.1 are a real difference here.</summary>
        private const string SpeedFormat = "{0:0.0} m/s";

        /// <summary>Damage a second, rounded. See the class remarks on where the product comes from.</summary>
        private const string DpsFormat = "{0:0} DPS";

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

        /// <summary>
        /// Where a tap goes. Held rather than wired through the Inspector so that the presenter can
        /// hand over a method it owns, and so a cleared card reports to nobody.
        /// </summary>
        private Action<ContentId> _onChosen;

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

            CharacterId = spec.Id;
            _onChosen = onChosen;

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
                _hp.text = string.Format(CultureInfo.InvariantCulture, HpFormat, spec.MaxHp);
            }

            if (_speed != null)
            {
                _speed.text = string.Format(
                    CultureInfo.InvariantCulture, SpeedFormat, spec.Movement.Speed);
            }

            if (_weapon != null)
            {
                _weapon.text = string.Format(
                    CultureInfo.InvariantCulture, DpsFormat, DpsOf(spec));
            }

            // Re-armed every draw, and cleared first: a card bound twice — which a screen reopened
            // does — would otherwise carry the previous binding's listener as well and report the
            // tap twice, starting two runs.
            if (_button != null)
            {
                _button.onClick.RemoveListener(Raise);
                _button.onClick.AddListener(Raise);
            }

            SetInteractable(true);

            gameObject.SetActive(true);
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
            CharacterId = default;

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
            _onChosen?.Invoke(CharacterId);
        }
    }
}
