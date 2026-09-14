using System;
using Soulvail.Core.Content;
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
    /// <b>It draws the <c>LocKey</c>, because nothing in the build resolves one.</b>
    /// <c>ILocalizer</c> is an AR §6 port whose adapter is M6-10's, so a card reads
    /// <c>skill.oathbound.consecrate</c> rather than "Consecrate" (ADR-0012, ledger row 9). That is
    /// not a third place raw English is typed into a prefab — <em>nothing</em> English is typed at
    /// all, and the key is data. It does mean GD §13.1's <em>"readable in under two seconds"</em>
    /// cannot be judged until M6-10.
    /// </para>
    /// <para>
    /// <b>The four tints are serialized here and are deliberately not a palette.</b> GD §16.4's
    /// palette still has no file (ledger row 6, owner M3-13a), and inventing one here would be that
    /// task arriving early and unspecified. Four <see cref="Color"/> fields are honest about being
    /// placeholders and are one more caller for the row to absorb. CH §4's four kinds are what a
    /// player is distinguishing at a glance: a Keystone is not a Passive, and the card has two
    /// seconds to say so.
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
        [Tooltip("The node's name. Draws its LocKey until M6-10 — see the class remarks.")]
        [SerializeField] private TMP_Text _name;

        [Tooltip("What the node does. Draws its LocKey until M6-10, for the same reason.")]
        [SerializeField] private TMP_Text _description;

        [Tooltip("The stripe that says which of CH §4's four kinds this is, tinted from the four " +
                 "fields below.")]
        [SerializeField] private Image _kindStrip;

        [Tooltip("The whole card as one button. A card is a target for a thumb that was on the " +
                 "stick a moment ago (ledger row 4), so the hit area is the card rather than a " +
                 "word on it.")]
        [SerializeField] private Button _button;

        [Tooltip("Always on: a stat or a rule change, and about 45 % of a tree (CH §4). " +
                 "Placeholder until M3-13a's Palette — ledger row 6.")]
        [SerializeField] private Color _passive = new Color(0.62f, 0.66f, 0.72f);

        [Tooltip("Grants a skill with a cooldown (CH §4.2). Placeholder until M3-13a.")]
        [SerializeField] private Color _active = new Color(0.36f, 0.72f, 0.85f);

        [Tooltip("Improves a skill already owned. Placeholder until M3-13a.")]
        [SerializeField] private Color _upgrade = new Color(0.45f, 0.78f, 0.55f);

        [Tooltip("Build-defining, end of a branch, three per class (CH §4). Placeholder until " +
                 "M3-13a.")]
        [SerializeField] private Color _keystone = new Color(0.85f, 0.72f, 0.32f);

        /// <summary>Which of the three this is. Reported on a tap, and nothing else reads it.</summary>
        private int _index;

        /// <summary>
        /// Where a tap goes. Held rather than wired through the Inspector so that the presenter can
        /// hand over a method it owns, and so a card that is never shown reports to nobody.
        /// </summary>
        private Action<int> _onChosen;

        /// <summary>Whether the card is currently drawn. <c>Prefab_IsDressed</c>'s reachable read.</summary>
        public bool IsShown => gameObject.activeSelf;

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
        /// <param name="onChosen">Called with <paramref name="index"/> when the card is tapped.</param>
        /// <exception cref="ArgumentNullException"><paramref name="spec"/> or <paramref name="onChosen"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
        public void Show(int index, SkillSpec spec, Action<int> onChosen)
        {
            if (spec is null)
            {
                throw new ArgumentNullException(
                    nameof(spec),
                    "A card with no node to draw is a presenter that read past the end of the "
                        + "offer — the offer may be shorter than three (M3-04 rule 1).");
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

            _index = index;
            _onChosen = onChosen;

            if (_name != null)
            {
                // The key, not English. See the class remarks and ledger row 9.
                _name.text = spec.NameKey.Key;
            }

            if (_description != null)
            {
                _description.text = spec.DescriptionKey.Key;
            }

            if (_kindStrip != null)
            {
                _kindStrip.color = Tint(spec.Kind);
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

        /// <summary>CH §4's four kinds, in four placeholder colours (rule 8, ledger row 6).</summary>
        /// <remarks>
        /// A <c>switch</c> on a <see cref="SkillKind"/> and not on an effect type — the banned shape
        /// is <c>switch (effect.Type)</c>, which is a dispatch that should have been polymorphism.
        /// This is a presentation lookup over a closed enum of four, which is what an enum is for.
        /// </remarks>
        private Color Tint(SkillKind kind) => kind switch
        {
            SkillKind.Passive => _passive,
            SkillKind.Active => _active,
            SkillKind.Upgrade => _upgrade,
            SkillKind.Keystone => _keystone,
            _ => _passive,
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
