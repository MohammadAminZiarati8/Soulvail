using System;
using System.Globalization;
using System.Text;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Skills.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Controls
{
    /// <summary>
    /// One line of CC §6.3's Skills screen: a name, the cooldown in seconds, the auto-cast
    /// condition written out, and the Auto/Manual switch. CC §6.1, §6.2, §6.3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It reports a tap and decides nothing.</b> The switch calls back with the skill's id and
    /// the state the player asked for; it does not move a slot, count one, or send a command. The
    /// presenter owns all three, because the full-slots question (CC §6.2) has to be asked
    /// <em>before</em> the command and a row cannot know what the other rows hold.
    /// </para>
    /// <para>
    /// <b>It draws English as of M3-14a, and the trigger line is where that pays</b> —
    /// <c>OfferCard</c>'s paragraph, and this is ledger row 9's second and sharper reader. A trigger
    /// line is a key <em>and</em> a number, so the <em>composition</em> was the half that had to be
    /// settled early and the words were the half that could wait: <see cref="TriggerText.KeyFor"/>
    /// gives the key, <see cref="TriggerText.UnitOf"/> says what kind of number goes beside it, and
    /// this file decides only how that number <em>looks</em> (ADR-0012). The row read
    /// <c>trigger.hpFraction.below 60 %</c> until this task and reads <em>"Player HP below 60 %"</em>
    /// now. <b>M6-10 is still what makes the key a format string with the number <em>inside</em>
    /// it</b>, which is the word-order problem this shape was chosen to survive; one English table
    /// does not need it, because English puts the number last here anyway.
    /// </para>
    /// <para>
    /// <b>The switch is a <see cref="Toggle"/> rather than two buttons</b>, because CC §6.1 has
    /// exactly two states and one of them is always current. A pair of buttons would need a third
    /// piece of state to say which is selected, and the thing a thumb is aiming at on a 56 dp row is
    /// one target rather than two.
    /// </para>
    /// <para>
    /// <b>The callback is dropped in <see cref="Hide"/> and re-armed in <see cref="Show"/>.</b>
    /// <c>OfferCard</c>'s trick for its reason: a pooled row is repainted rather than rebuilt, and a
    /// row that kept its previous listener would report one tap twice.
    /// </para>
    /// <para>
    /// Every piece is written through a Unity-null check rather than assumed. A row is dressed on
    /// the prefab and <c>SkillsPresenter.Start</c> is what refuses an undressed screen by name; this
    /// class only has to not throw on the way there — <c>OfferCard</c>'s bargain.
    /// </para>
    /// </remarks>
    public sealed class SkillRow : MonoBehaviour
    {
        /// <summary>
        /// The cooldown, in seconds. <c>{0:0.0}</c> rather than <c>{0}</c> for
        /// <c>HudPresenter</c>'s reason: TMP treats an unformatted placeholder as "up to nine
        /// decimal places", so 3.2 would render as <c>3.2000000</c> on a bad day.
        /// </summary>
        private const string CooldownFormat = "{0:0.0} s";

        /// <summary>
        /// Which of CC §6.2's four thumb positions this skill sits in — <em>"S1"</em>, <em>"S3"</em>.
        /// </summary>
        /// <remarks>
        /// A position rather than a rank (rule 11): the number is the slot's index plus one, and a
        /// hole is legal and ordinary (M3-07a rule 3). A row reading <em>"2nd manual skill"</em>
        /// would be the compaction the whole slot design refuses.
        /// </remarks>
        private const string SlotFormat = "S{0:0}";

        [Tooltip("The skill's name, resolved through ILocalizer — see the class remarks.")]
        [SerializeField] private TMP_Text _name;

        [Tooltip("The wait in seconds after CH §4.1's 40 % floor. Seconds rather than a radial " +
                 "fill because the tick is gated while this screen is up, so a fill would be a " +
                 "frozen ring saying nothing.")]
        [SerializeField] private TMP_Text _cooldown;

        [Tooltip("CC §6.4's authored condition, written out: one resolved LocKey per clause with " +
                 "its threshold beside it.")]
        [SerializeField] private TMP_Text _trigger;

        [Tooltip("CC §6.1's two states. On is Auto — the default for every owned active — and off " +
                 "is Manual, which takes one of the four thumb slots.")]
        [SerializeField] private Toggle _autoManual;

        [Tooltip("\"S1\"–\"S4\" while the skill is Manual, and blank while it is Auto.")]
        [SerializeField] private TMP_Text _slot;

        /// <summary>Which skill this row is currently drawing. Reported on a tap.</summary>
        private ContentId _skillId;

        /// <summary>
        /// Where a tap goes: the skill's id and the state the player asked for.
        /// </summary>
        /// <remarks>
        /// Held rather than wired through the Inspector, <c>OfferCard._onChosen</c>'s reason: the
        /// presenter hands over a method it owns, and a row that is not currently drawn reports to
        /// nobody.
        /// </remarks>
        private Action<ContentId, bool> _onSwitched;

        /// <summary>
        /// True while the toggle is being written by <see cref="Show"/> rather than by a thumb.
        /// </summary>
        /// <remarks>
        /// <b>The whole of why a redraw does not send a command.</b> <see cref="Toggle.isOn"/>
        /// raises <c>onValueChanged</c> when it is assigned, so painting a row from
        /// <c>SkillAutoCastChanged</c> would call straight back into the presenter and send the
        /// command again — and the cancel path, which puts the switch back where it was, would send
        /// the very command it exists to refuse.
        /// </remarks>
        private bool _painting;

        /// <summary>Whether the row is currently drawn. <c>Prefab_IsDressed</c>'s reachable read.</summary>
        public bool IsShown => gameObject.activeSelf;

        /// <summary>The skill this row is drawing, or <c>default</c> while it is hidden.</summary>
        public ContentId SkillId => _skillId;

        /// <summary>
        /// Draws <paramref name="spec"/> as one row and reports the switch to
        /// <paramref name="onSwitched"/>.
        /// </summary>
        /// <param name="spec">The owned active to draw. Its <c>Active</c> carries the trigger.</param>
        /// <param name="cooldownSeconds">
        /// What the wait actually is after the floor — <c>RunState.SkillCooldownSeconds</c>. A
        /// non-finite one leaves the label blank rather than writing <c>NaN s</c> at the player.
        /// </param>
        /// <param name="isAuto">Whether it currently fires itself (CC §6.1).</param>
        /// <param name="slot">
        /// Which of the four thumb positions it holds, from 0 — and any negative number for none,
        /// which is what <c>SkillAutoCastChanged.Slot</c> already says with −1.
        /// </param>
        /// <param name="localizer">
        /// What turns the name and every trigger clause's key into words.
        /// </param>
        /// <param name="onSwitched">
        /// Called with the skill's id and the state the player asked for. Never called by this
        /// method itself — see <see cref="_painting"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="spec"/>, <paramref name="localizer"/> or <paramref name="onSwitched"/> is
        /// null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="spec"/> is not an <see cref="SkillKind.Active"/>. Only an active reaches
        /// the runner at all (M3-06 rule 5), so CC §6.1's <em>"passive skills have no toggle and no
        /// button"</em> is true by construction — and a caller that got here with one has read past
        /// the runner rather than out of it.
        /// </exception>
        public void Show(
            SkillSpec spec,
            float cooldownSeconds,
            bool isAuto,
            int slot,
            ILocalizer localizer,
            Action<ContentId, bool> onSwitched)
        {
            if (spec is null)
            {
                throw new ArgumentNullException(
                    nameof(spec),
                    "A row with no skill to draw is a presenter that read past the end of the "
                        + "runner — OwnedActiveCount is the bound.");
            }

            if (localizer is null)
            {
                throw new ArgumentNullException(
                    nameof(localizer),
                    "A row with no localizer would draw its name and its whole trigger line as "
                        + "keys, which is the screen whose entire job is to explain telling the "
                        + "player nothing (M3-14a rule 1).");
            }

            if (onSwitched is null)
            {
                throw new ArgumentNullException(
                    nameof(onSwitched),
                    "A row with nobody to report to is a switch the player can flip and nothing "
                        + "happens, which is the failure here that looks like a frozen screen.");
            }

            if (spec.Kind != SkillKind.Active)
            {
                throw new ArgumentException(
                    $"'{spec.Id}' is a {spec.Kind} and this screen lists only Actives. A Passive "
                        + "has no cooldown, no trigger and no toggle (CC §6.1), and never reaches "
                        + "the runner these rows are built from.",
                    nameof(spec));
            }

            _skillId = spec.Id;
            _onSwitched = onSwitched;

            if (_name != null)
            {
                // English, as of M3-14a. See the class remarks.
                _name.text = localizer.Get(spec.NameKey);
            }

            WriteCooldown(cooldownSeconds);
            WriteTrigger(spec.Active.Trigger, localizer);
            WriteSlot(isAuto, slot);

            // Painted rather than tapped: the assignment below raises onValueChanged, and without
            // the latch a redraw would send the command it is redrawing from.
            _painting = true;

            try
            {
                if (_autoManual != null)
                {
                    // Re-armed every draw and cleared first, OfferCard's reason: a pooled row would
                    // otherwise carry every previous draw's listener and report one tap n times.
                    _autoManual.onValueChanged.RemoveListener(Raise);
                    _autoManual.onValueChanged.AddListener(Raise);

                    _autoManual.isOn = isAuto;
                    _autoManual.interactable = true;
                }
            }
            finally
            {
                _painting = false;
            }

            gameObject.SetActive(true);
        }

        /// <summary>
        /// Takes the row off the screen — a pooled row past the end of what the player owns
        /// (rule 7).
        /// </summary>
        public void Hide()
        {
            // Dropped rather than left dangling, so a hidden row cannot report into a presenter
            // that has stopped listening.
            _onSwitched = null;
            _skillId = default;

            if (_autoManual != null)
            {
                _autoManual.onValueChanged.RemoveListener(Raise);
            }

            gameObject.SetActive(false);
        }

        /// <summary>
        /// Turns the row's switch on or off without changing what it draws.
        /// </summary>
        /// <remarks>
        /// What rule 10 spends: the prompt is modal within the screen, so the list underneath it
        /// goes inert rather than gaining a canvas of its own. <c>OfferCard.SetInteractable</c>'s
        /// shape, for a different modality.
        /// </remarks>
        /// <param name="value">Whether the switch may be flipped.</param>
        public void SetInteractable(bool value)
        {
            if (_autoManual != null)
            {
                _autoManual.interactable = value;
            }
        }

        private void WriteCooldown(float seconds)
        {
            if (_cooldown == null)
            {
                return;
            }

            // A non-finite wait is a stack that made it unmeasurable (CooldownRules returns an
            // infinite `live` unchanged), and "NaN s" on a row is worse than a blank one: the
            // player cannot act on either, and only one of them looks like a bug in the game
            // rather than in the number.
            if (!float.IsFinite(seconds))
            {
                _cooldown.text = string.Empty;

                return;
            }

            _cooldown.SetText(CooldownFormat, seconds);
        }

        /// <summary>
        /// CC §6.4's condition as a line: one key and one number per clause, conjoined.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Built with a <see cref="StringBuilder"/> and not <c>SetText</c></b>, because the line
        /// is a key of unknown length plus a number and TMP's format overloads take numbers only.
        /// It allocates, and this is a path AR §14 permits one on: a row is painted when the screen
        /// opens and when a switch moves, never per frame (rule 4).
        /// </para>
        /// <para>
        /// <b>Two clauses are joined by <c>" + "</c> and never by the word "and"</b>, which would be
        /// English typed into a view (ADR-0012). <c>TriggerSpec</c> is conjunction-only and capped
        /// at two (M3-02a), so this is the whole grammar there is.
        /// </para>
        /// </remarks>
        private void WriteTrigger(TriggerSpec trigger, ILocalizer localizer)
        {
            if (_trigger == null)
            {
                return;
            }

            var line = new StringBuilder();

            for (int i = 0; i < trigger.Clauses.Count; i++)
            {
                TriggerClause clause = trigger.Clauses[i];

                if (i > 0)
                {
                    line.Append(" + ");
                }

                line
                    .Append(localizer.Get(TriggerText.KeyFor(clause.Field, clause.Comparison)))
                    .Append(' ')
                    .Append(Threshold(clause));
            }

            _trigger.text = line.ToString();
        }

        /// <summary>
        /// One clause's threshold, written the way its <see cref="TriggerUnit"/> says (rule 2).
        /// </summary>
        /// <remarks>
        /// <b>The culture is invariant and deliberately so.</b> A run's numbers are not localised
        /// until M6-10 gives the project a localiser and a culture to ask; formatting against the
        /// device's culture in the meantime would make a test that reads <em>"60 %"</em> pass in
        /// Ireland and fail in France, which is a failure about the machine rather than the game.
        /// </remarks>
        private static string Threshold(TriggerClause clause)
        {
            float value = clause.Threshold;

            return TriggerText.UnitOf(clause.Field) switch
            {
                TriggerUnit.Fraction => (value * 100f).ToString("0", CultureInfo.InvariantCulture) + " %",

                TriggerUnit.Seconds => value.ToString("0.#", CultureInfo.InvariantCulture) + " s",

                // Count and Points are both whole numbers on screen and differ only in what they
                // mean, which is the localiser's business rather than the formatter's: "3" enemies
                // and "50" Veilrot are written the same way and read differently because the key
                // beside them says so.
                _ => value.ToString("0", CultureInfo.InvariantCulture),
            };
        }

        private void WriteSlot(bool isAuto, int slot)
        {
            if (_slot == null)
            {
                return;
            }

            // Blank rather than a dash for an Auto skill: there is no slot to name, and a
            // placeholder glyph is a thing a player would try to read.
            if (isAuto || slot < 0)
            {
                _slot.text = string.Empty;

                return;
            }

            _slot.SetText(SlotFormat, slot + 1);
        }

        /// <remarks>
        /// A named method rather than a lambda so that <c>RemoveListener</c> can find it — a closure
        /// would be a different delegate every draw and the row would accumulate one listener per
        /// paint (<c>OfferCard.Raise</c>'s reason).
        /// <para>
        /// <paramref name="isAuto"/> is the state the toggle now shows, which is what the player
        /// asked for. The presenter decides whether it may be granted.
        /// </para>
        /// </remarks>
        private void Raise(bool isAuto)
        {
            if (_painting)
            {
                return;
            }

            _onSwitched?.Invoke(_skillId, isAuto);
        }
    }
}
