using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Hud.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Controls
{
    /// <summary>
    /// One of CC §6.2's four thumb positions: a 60 dp button for whatever skill is in its slot, with
    /// a radial fill that says when it is live — and no tap response at all while it is cooling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A sibling of <see cref="SkillButton"/>, not a generalisation of it, and the one axis they
    /// differ on is the one that matters</b> (M3-10a rule 1). The Charge <em>stays tappable while it
    /// is cooling</em>, deliberately and against CC §6.2's generic rule, because CC §5 gives the
    /// movement skill a 0.15 s input buffer and the buffer only exists if the early press actually
    /// reaches core — its own remarks say so. A slot button is the case that exception was carved out
    /// of: CC §6.2's <em>"unavailable: 40 % opacity, no tap response"</em> applies to it exactly, so
    /// this class does the opposite (rule 7). A shared base class would put the single behaviour that
    /// differs behind a <c>virtual</c> and invite the next reader to unify them. All the two share is
    /// <see cref="StickShaper.PixelsPerDp"/>, reached from either side's own layout, and a row pins
    /// that there is no base class between them.
    /// </para>
    /// <para>
    /// <b>It reads two numbers a frame and holds none.</b> Both come from <see cref="RunState"/> —
    /// <see cref="RunState.SlotCooldownFraction"/> and <see cref="RunState.IsSlotReady"/> — sampled
    /// in <see cref="Update"/> rather than driven by an event, because a fill slides continuously for
    /// seconds and an event carrying it would be an event per frame. That is <see cref="SkillButton"/>'s
    /// bargain word for word; what M3-10a added is that the reads are addressed by <em>slot</em>,
    /// because a button is a fixed thumb position and the skill under it changes (rule 2).
    /// </para>
    /// <para>
    /// <b>It sends nothing directly.</b> The press is written into <see cref="SkillSlotInput"/> and
    /// turned into a command by <c>RunTicker.CommandPhase</c>, so <em>"a tap became a command"</em>
    /// happens at the one point in the frame that is written down (rule 3). This class therefore
    /// holds no <see cref="IPlayerCommands"/> at all.
    /// </para>
    /// <para>
    /// <b>Its label is a word as of M3-14a, and this is the reader whose closure is a device
    /// question rather than a table one</b> (M3-10a rule 9, M3-14a rule 9). A 60 dp circle with
    /// <c>skill.oathbound.consecrate.name</c> in it was never a design; <em>"Bulwark"</em> in an
    /// auto-sizing 8–18 pt label <em>plausibly</em> is, and <b>plausibly is the whole of the
    /// change</b> — every other reader ledger row 9 counts closes the moment the table has a row,
    /// and this one closes only if the word is legible under a thumb on a six-inch screen, which is
    /// [ledger row 4] and which nobody has looked at. M7's icon is the fallback that rule named in
    /// advance. <b>Not to be confused with the 24 dp auto-cast cell beside it</b>
    /// (<c>AutoCastRow</c>), where nothing fits at all and no table can help: that is row 9's
    /// seventh reader and it stays open.
    /// </para>
    /// <para>
    /// <b>Nothing here knows about Auto</b> (rule 11). An auto-cast skill holds no slot, so it
    /// produces no button by construction rather than by a check.
    /// </para>
    /// </remarks>
    public sealed class ManualSkillButton : MonoBehaviour
    {
        /// <summary>Opacity while the cooldown is running. CC §6.2.</summary>
        private const float CoolingAlpha = 0.4f;

        /// <summary>No slot — what <see cref="Slot"/> reads before <see cref="Bind"/>.</summary>
        private const int Unbound = -1;

        [Tooltip("The radial sweep drawn over the button. Its Image must be Filled / Radial360 — " +
                 "fillAmount is the only thing written to it.")]
        [SerializeField] private Image _radialFill;

        [Tooltip("The whole button's opacity: 100 % when the skill is live, 40 % while it is " +
                 "cooling. Unlike the Charge's, raycasts go with it — see the class remarks.")]
        [SerializeField] private CanvasGroup _group;

        [Tooltip("The button itself. Made non-interactable while the skill is cooling, which is the " +
                 "half of CC §6.2's rule the Charge deliberately does not follow.")]
        [SerializeField] private Button _button;

        [Tooltip("What is under the thumb: the skill's name, resolved through ILocalizer. Whether " +
                 "it is legible at 8 pt is a device question — see the class remarks.")]
        [SerializeField] private TMP_Text _label;

        [Tooltip("The button's diameter in dp. 60 is CC §6.2's slot size; the Charge beside it is " +
                 "72. Where it sits is SkillBarPresenter's — this is only how big it is.")]
        [Min(1f)]
        [SerializeField] private float _sizeDp = 60f;

        private SkillSlotInput _input;
        private IRunSession _session;

        /// <summary>
        /// The last fraction written, so a frame that changed nothing costs no canvas rebuild.
        /// Seeded outside <c>[0, 1]</c> so the first frame always draws.
        /// </summary>
        private float _shownFraction = -1f;

        /// <summary>
        /// Whether the button was last drawn as live. A <see cref="bool"/> cannot be seeded outside
        /// its own range, so <see cref="_drawn"/> carries the "nothing yet" half instead.
        /// </summary>
        private bool _shownReady;

        /// <summary>Whether anything has been drawn at all since this object was built.</summary>
        private bool _drawn;

        private int _slot = Unbound;

        /// <summary>Which of CC §6.2's four thumb positions this button is, or −1 before binding.</summary>
        public int Slot => _slot;

        /// <summary>
        /// The button's authored diameter in dp, as the Inspector holds it. What
        /// <c>SkillBarPresenter</c> sizes the rect from, and it is deliberately raw — that class owns
        /// one answer to a nonsense layout field and owns it for all three of them.
        /// </summary>
        public float SizeDp => _sizeDp;

        /// <summary>Whether the button is on the screen at all (rule 5).</summary>
        public bool IsShown => gameObject.activeSelf;

        /// <summary>What the label currently reads. The one read <c>Bar_LabelsAreWords</c> needs.</summary>
        public string Label => _label == null ? string.Empty : _label.text;

        /// <summary>
        /// Tells this button which slot it is, where to write a press, and what to read a cooldown
        /// from. Called once, by <c>SkillBarPresenter</c>.
        /// </summary>
        /// <param name="slot">Which thumb position, from 0. S1 is slot 0.</param>
        /// <param name="input">The frame's one-press buffer — see the class remarks (rule 3).</param>
        /// <param name="session">The run, read for two numbers a frame. Never a command port.</param>
        /// <remarks>
        /// Unguarded, unlike an <c>[Inject]</c> constructor: the only caller is a presenter that has
        /// already refused an undressed screen by name, and a button bound with nulls simply draws
        /// nothing and sends nothing — which is what an unbound one does anyway.
        /// </remarks>
        public void Bind(int slot, SkillSlotInput input, IRunSession session)
        {
            _slot = slot;
            _input = input;
            _session = session;

            // Removed before it is added, and with a named method rather than a lambda, so a button
            // bound twice — which the presenter does not do and a test does — reports one tap once
            // rather than once per binding. PausePresenter.Wire's trick, for its reason.
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnPressed);
                _button.onClick.AddListener(OnPressed);
            }
        }

        /// <summary>
        /// Takes the button off the screen entirely — CC §6.2's <em>"unused slots are not drawn"</em>
        /// (rule 5).
        /// </summary>
        /// <remarks>
        /// Deactivated rather than made transparent, so it takes no touches and costs no canvas
        /// rebuild. That is the state of all four in every run in this build, because nothing in
        /// <c>Data/Trees</c> ships an Active until M3-12 — and it is why
        /// <see cref="RunState.SlotCooldownFraction"/> has to tolerate an empty slot rather than
        /// refuse one.
        /// </remarks>
        public void ShowEmpty()
        {
            gameObject.SetActive(false);

            // Forgotten rather than kept, so the next Show draws on its first frame rather than
            // comparing against what a different skill left behind.
            _drawn = false;
        }

        /// <summary>Puts <paramref name="spec"/> under the thumb and draws it immediately.</summary>
        /// <param name="spec">
        /// The skill now in this slot. Null takes the button off instead, which is the same answer an
        /// empty slot gets — a presenter that could not resolve an id has nothing to draw either.
        /// </param>
        /// <param name="localizer">What turns the skill's name key into a word.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="localizer"/> is null. <b>Guarded although <see cref="Bind"/> is not</b>,
        /// and the two are different questions: a button bound with nulls draws nothing and sends
        /// nothing, which is what an unbound one does anyway, where a button <em>shown</em> without
        /// a localizer would put a key under a thumb — the exact state M3-14a exists to end, in the
        /// one place it was never legible to begin with.
        /// </exception>
        public void Show(SkillSpec spec, ILocalizer localizer)
        {
            if (localizer is null)
            {
                throw new ArgumentNullException(nameof(localizer));
            }

            if (spec is null)
            {
                ShowEmpty();

                return;
            }

            if (_label != null)
            {
                // A word, as of M3-14a. See the class remarks and ledger row 9.
                _label.text = localizer.Get(spec.NameKey);
            }

            gameObject.SetActive(true);

            // Drawn once immediately, so a button that shows a full ring the frame it appears is the
            // cooldown being genuinely zero rather than a first frame that has not come round yet —
            // SkillButton.Start's argument.
            _drawn = false;

            Draw();
        }

        /// <remarks>
        /// <c>Update</c>, not <c>LateUpdate</c>, for <see cref="SkillButton"/>'s reason:
        /// <c>RunTicker</c> is an <c>ITickable</c> and so runs in the Update phase, and a button one
        /// frame behind the cooldown it draws is exactly as wrong at the moment that matters — the
        /// instant it becomes live — as it is useful the rest of the time. It does not run at all
        /// while the slot is empty, because the object is deactivated (rule 5).
        /// </remarks>
        private void Update()
        {
            Draw();
        }

        /// <remarks>
        /// The handler is dropped explicitly rather than left to the destroyed <see cref="Button"/>,
        /// for the reason every screen in this project drops its own: a component destroyed before
        /// its scope would otherwise be reachable from a live <c>onClick</c> list.
        /// </remarks>
        private void OnDestroy()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnPressed);
            }
        }

        /// <summary>
        /// A thumb landed: write the slot into the frame's buffer, unless the skill is cooling.
        /// </summary>
        /// <remarks>
        /// <b>The refusal is here as well as on <c>interactable</c>, and both halves are on
        /// purpose</b> (rule 7). uGUI already declines to fire a non-interactable button, so this
        /// looks redundant — but that makes the screen's <em>no</em> a property of a flag somebody
        /// could clear from anywhere, where the rule is that a cooling slot does not send. Core's
        /// tolerant answer stays the backstop underneath both: <c>CastSlot</c> returns false for a
        /// cooling skill rather than throwing, which covers the one-frame race where a cooldown
        /// starts between the tap and the poll.
        /// </remarks>
        private void OnPressed()
        {
            if (_input is null || _slot < 0 || !IsReady())
            {
                return;
            }

            _input.Press(_slot);
        }

        /// <summary>
        /// Rule 7: the fill is <c>1 − fraction</c> — empty the instant a cast starts, full when the
        /// skill is live again — and the whole button dims <em>and goes dead</em> while it is cooling.
        /// </summary>
        /// <remarks>
        /// The fraction is clamped rather than trusted, for <see cref="SkillButton"/>'s reason:
        /// core guarantees <c>[0, 1]</c> and there is no path by which it would not, but a
        /// <c>fillAmount</c> outside the range is the kind of thing uGUI renders as an empty or a full
        /// ring rather than as an error.
        /// </remarks>
        private void Draw()
        {
            float clamped = Mathf.Clamp01(Fraction());
            bool ready = IsReady();

            // Not merely an optimisation: assigning fillAmount marks the graphic dirty and queues a
            // canvas rebuild, so writing an unchanged value would rebuild the HUD on every frame of
            // every second a skill is not being cast — which, for a skill on an eight-second
            // cooldown, is most of them (rule 2, SkillButton's argument with four more buttons on it).
            if (_drawn && Mathf.Approximately(clamped, _shownFraction) && ready == _shownReady)
            {
                return;
            }

            _drawn = true;
            _shownFraction = clamped;
            _shownReady = ready;

            if (_radialFill != null)
            {
                _radialFill.fillAmount = 1f - clamped;
            }

            if (_group != null)
            {
                _group.alpha = ready ? 1f : CoolingAlpha;
            }

            // **The line SkillButton deliberately does not have** (rule 1). CC §6.2's "no tap
            // response" is this, and the Charge's exemption is CC §5's input buffer.
            if (_button != null)
            {
                _button.interactable = ready;
            }
        }

        /// <summary>
        /// How much cooldown is left under this slot, or zero when there is no run to ask.
        /// </summary>
        /// <remarks>
        /// A button drawn before the run started — the frame the Run scene loads, or a HUD left in a
        /// scene with no run in it — reads as having no cooldown running, which is the honest of the
        /// two answers: there is nothing to cast.
        /// </remarks>
        private float Fraction()
        {
            RunState state = _slot < 0 ? null : _session?.State;

            return state is null ? 0f : state.SlotCooldownFraction(_slot);
        }

        /// <summary>
        /// Whether the slot may fire right now. <b>False before the run starts</b>, which is the
        /// opposite of <see cref="Fraction"/>'s answer and deliberately so: a full ring on a dead
        /// button says "nothing is cooling", and a button that would send a command into a run that
        /// does not exist is the thing rule 10 is about.
        /// </summary>
        private bool IsReady()
        {
            RunState state = _slot < 0 ? null : _session?.State;

            return state is not null && state.IsSlotReady(_slot);
        }
    }
}
