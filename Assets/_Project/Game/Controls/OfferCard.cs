using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and LevelUp.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Controls
{
    /// <summary>
    /// One card on the level-up screen: a name, a description, a stripe saying what kind of node it
    /// is, and a button that reports which of the three was hit. GD §13.1, CH §4, §5.1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It holds an index and never a <c>ContentId</c>.</b> What the player tapped is a
    /// <em>position on a screen</em>, and <c>IProgressionCommands.ChooseOffer</c> takes exactly that
    /// for <c>CastSkill</c>'s reason — the card does not have to be right about what is currently
    /// underneath it, only about which of the three it is. That also means a card is safe to repaint
    /// in place for a second pick: the index it reports is still the index of the card that was hit.
    /// </para>
    /// <para>
    /// <b>It draws English as of M3-14a, and it is handed the port rather than injected</b>
    /// (M3-14a rule 11). A card is a pooled template instantiated from a prefab field and nothing
    /// injects one individually — the same fact that decided M3-13a rule 2 against an injected
    /// palette — so <c>ILocalizer</c> arrives as one more argument on <see cref="Show"/>, which
    /// already takes a <c>SkillSpec</c>, and <c>LevelUpPresenter</c> is the one thing injected.
    /// Until this task the card drew <c>spec.NameKey.Key</c> and a player read
    /// <c>skill.oathbound.consecrate.name</c> (ADR-0012, ledger row 9); <b>this is the screen GD
    /// §13.1's <em>"readable in under two seconds"</em> was always about</b>, and it is now
    /// something that can actually be judged.
    /// </para>
    /// <para>
    /// <b>The four tints were serialized here and are <see cref="Palette"/>'s as of M3-13a.</b> They
    /// were four <see cref="Color"/> fields honest about being placeholders (M3-08b rule 8), and the
    /// same four values were then copied onto <c>TreeNodeView</c> and <c>AutoCastRow</c> — the third
    /// copy, which is exactly where ledger row 6 said a palette stops being one. CH §4's four kinds
    /// are what a player is distinguishing at a glance: a Keystone is not a Passive, and the card has
    /// two seconds to say so — on this screen, on the tree and in the auto-cast row, in one colour
    /// each rather than three that agree by inspection.
    /// </para>
    /// <para>
    /// Every piece is written through a Unity-null check rather than assumed. A card is dressed on
    /// the prefab and <c>LevelUpPresenter.Start</c> is what refuses an undressed screen by name; this
    /// class only has to not throw on the way there, which is the bargain <c>HudPresenter</c> makes
    /// with its fade cover.
    /// </para>
    /// </remarks>
    public sealed class OfferCard : MonoBehaviour
    {
        [Tooltip("The node's name, resolved through ILocalizer — see the class remarks.")]
        [SerializeField] private TMP_Text _name;

        [Tooltip("What the node does, resolved the same way. GD §13.1 budgets two seconds for " +
                 "reading this, which is what the table's row length is written against.")]
        [SerializeField] private TMP_Text _description;

        [Tooltip("The stripe that says which of CH §4's four kinds this is, tinted from the four " +
                 "fields below.")]
        [SerializeField] private Image _kindStrip;

        [Tooltip("The whole card as one button. A card is a target for a thumb that was on the " +
                 "stick a moment ago (ledger row 4), so the hit area is the card rather than a " +
                 "word on it.")]
        [SerializeField] private Button _button;

        [Tooltip("GD §13.2's corrupted frame: an Outline on the card's own Image, switched on and " +
                 "tinted Palette.Veilrot for a Pact and off for a clean node. Its colour is written " +
                 "on every draw, so whatever the prefab holds is never what a player sees.")]
        [SerializeField] private Outline _pactFrame;

        [Tooltip("\"Pact · +15 Rot\" on a corrupted card, empty on a clean one — the one number " +
                 "GD §13.2 puts after every example it gives.")]
        [SerializeField] private TMP_Text _rot;

        /// <summary>
        /// What the frame label says: <em>"Pact · +{0:0} Rot"</em> in English, so a corrupted card
        /// does not rest on colour alone.
        /// </summary>
        /// <remarks>
        /// <b>One row since M6-10, where it was two words and a <c>const</c> format joining them</b>
        /// (<c>"{0} · +{1:0} {2}"</c>). That format was waiting for <c>ILocalizer.Format</c>, and
        /// it fixed the word order: a language that puts the number before the word <em>Pact</em>
        /// could not. The retired <c>ui.offer.rot</c> row went with it.
        /// </remarks>
        private static readonly LocKey PactKey = new LocKey("ui.offer.pact");

        /// <summary>Which of the three this is. Reported on a tap, and nothing else reads it.</summary>
        private int _index;

        /// <summary>
        /// Where a tap goes. Held rather than wired through the Inspector so that the presenter can
        /// hand over a method it owns, and so a card that is never shown reports to nobody.
        /// </summary>
        private Action<int> _onChosen;

        /// <summary>Whether the card is currently drawn. <c>Prefab_IsDressed</c>'s reachable read.</summary>
        public bool IsShown => gameObject.activeSelf;

        /// <summary>How it was last drawn. The read every Pact row asserts against.</summary>
        public bool IsPact { get; private set; }

        /// <summary>
        /// Draws <paramref name="spec"/> as card number <paramref name="index"/> and reports a tap
        /// to <paramref name="onChosen"/>.
        /// </summary>
        /// <remarks>
        /// Repaints in place rather than being hidden and shown again, because a second pick of a
        /// double level-up redraws the same three objects (M3-08a rule 6) and a card that flickered
        /// between the two would read as the screen having closed and reopened.
        /// </remarks>
        /// <param name="index">Which card this is, from 0 — what a tap reports.</param>
        /// <param name="spec">The node to draw.</param>
        /// <param name="localizer">What turns the spec's two <c>LocKey</c>s into words.</param>
        /// <param name="onChosen">Called with <paramref name="index"/> when the card is tapped.</param>
        /// <param name="isPact">
        /// Whether to draw <paramref name="spec"/>'s corrupted form: the Pact's description, its Rot
        /// price, and <see cref="Palette.Veilrot"/> on the frame (M6-05b rule 7). The name and the
        /// kind stripe stay the clean node's — a corrupted Keystone is still a Keystone, and the
        /// player is meant to recognise the node they were offered clean before.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="spec"/>, <paramref name="localizer"/> or <paramref name="onChosen"/> is
        /// null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="isPact"/> is true and <paramref name="spec"/> has no Pact.
        /// </exception>
        public void Show(int index, SkillSpec spec, ILocalizer localizer, Action<int> onChosen, bool isPact)
        {
            if (spec is null)
            {
                throw new ArgumentNullException(
                    nameof(spec),
                    "A card with no node to draw is a presenter that read past the end of the "
                        + "offer — the offer may be shorter than three (M3-04 rule 1).");
            }

            if (localizer is null)
            {
                throw new ArgumentNullException(
                    nameof(localizer),
                    "A card with no localizer would silently draw its two keys, which is a screen "
                        + "that looks like a build with no content rather than one with no wiring "
                        + "— and it is exactly the state M3-14a exists to end.");
            }

            if (onChosen is null)
            {
                throw new ArgumentNullException(
                    nameof(onChosen),
                    "A card with nobody to report to is a card the player can tap and nothing "
                        + "happens, which is the one failure here that looks like a frozen game.");
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index, "A card's index is its position in the offer, from 0.");
            }

            if (isPact && !spec.HasPact)
            {
                throw new ArgumentException(
                    $"'{spec.Id}' was handed to a card as a Pact and carries none. The model rolls a "
                        + "Pact only onto a node with a block (M6-05b rule 3), so this is a presenter "
                        + "reading past the model — drawing it clean would hide that.",
                    nameof(isPact));
            }

            _index = index;
            _onChosen = onChosen;
            IsPact = isPact;

            if (_name != null)
            {
                // English, as of M3-14a. A key with no row resolves to its own text rather than to
                // nothing, so a missing row is a card that diagnoses itself (M3-14a rule 1). The
                // clean node's name either way (M6-05a rule 4).
                _name.text = localizer.Get(spec.NameKey);
            }

            if (_description != null)
            {
                _description.text = localizer.Get(isPact ? spec.Pact.DescriptionKey : spec.DescriptionKey);
            }

            if (_kindStrip != null)
            {
                _kindStrip.color = Tint(spec.Kind);
            }

            // Every piece below is written on a clean draw too — switched off, emptied — because a
            // card is repainted in place for a second pick and the second may be clean.
            if (_pactFrame != null)
            {
                _pactFrame.effectColor = Palette.Veilrot;
                _pactFrame.enabled = isPact;
            }

            if (_rot != null)
            {
                // A string per draw, and a draw is a tap: nothing here runs per frame.
                _rot.text = isPact ? localizer.Format(PactKey, spec.Pact.Veilrot) : string.Empty;
                _rot.color = Palette.Veilrot;
            }

            // Re-armed every draw, and cleared first: a card repainted for a second pick would
            // otherwise carry the previous draw's listener as well and report the tap twice.
            if (_button != null)
            {
                _button.onClick.RemoveListener(Raise);
                _button.onClick.AddListener(Raise);
            }

            SetInteractable(true);

            gameObject.SetActive(true);
        }

        /// <summary>
        /// Takes the card off the screen: an offer of fewer than three leaves the rest hidden rather
        /// than drawn empty (CC §6.2, rule 6).
        /// </summary>
        public void Hide()
        {
            // Dropped rather than left dangling, so a hidden card cannot report into a presenter
            // that has stopped listening.
            _onChosen = null;

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
        /// What rule 5 spends: every card goes dead the moment one of them is tapped, because
        /// <c>ChooseOffer</c> runs synchronously and may repaint all three for a second pick before
        /// the thumb has left the glass. A second tap landing on a card that has not been repainted
        /// yet would spend the next pick on a node the player never saw.
        /// </remarks>
        /// <param name="value">Whether the card may be tapped.</param>
        public void SetInteractable(bool value)
        {
            if (_button != null)
            {
                _button.interactable = value;
            }
        }

        /// <summary>CH §4's four kinds, in <see cref="Palette"/>'s four tints.</summary>
        /// <remarks>
        /// A <c>switch</c> on a <see cref="SkillKind"/> and not on an effect type — the banned shape
        /// is <c>switch (effect.Type)</c>, which is a dispatch that should have been polymorphism.
        /// This is a presentation lookup over a closed enum of four, which is what an enum is for.
        /// The four were serialized fields from M3-08b and the same four values were copied into
        /// <c>TreeNodeView</c> and <c>AutoCastRow</c>; as of M3-13a there is one of them.
        /// </remarks>
        private static Color Tint(SkillKind kind) => kind switch
        {
            SkillKind.Passive => Palette.KindPassive,
            SkillKind.Active => Palette.KindActive,
            SkillKind.Upgrade => Palette.KindUpgrade,
            SkillKind.Keystone => Palette.KindKeystone,
            _ => Palette.KindPassive,
        };

        /// <remarks>
        /// A named method rather than a lambda, so that <c>RemoveListener</c> in
        /// <see cref="Show"/> and <see cref="Hide"/> can actually find it — a closure would be a
        /// different delegate every draw and the card would accumulate one listener per pick.
        /// </remarks>
        private void Raise()
        {
            _onChosen?.Invoke(_index);
        }
    }
}
