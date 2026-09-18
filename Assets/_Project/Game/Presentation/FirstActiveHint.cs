using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using TMPro;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and FirstActiveHint.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// CC §6.3's <em>"Once. Never again."</em>: the first time the player ever owns an Active, a
    /// callout under the pause icon tells them skills can be set to Manual — and then never again,
    /// on this install.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Once is once across installs, not across runs</b> (rule 11), which is the whole reason
    /// M3-09c bumped a save format. The flag lives on <see cref="PlayerProfile"/> and is reached
    /// through <c>ProfileStore</c>; a <c>bool</c> on this component would be a hint that came back
    /// every time the player descended, which is the other, worse reading of CC §6.3. Stated out
    /// loud so that a later reader does not "simplify" it back.
    /// </para>
    /// <para>
    /// <b>It arms on <c>NodeTaken</c> and shows on <c>LevelUpClosed</c></b> (rule 7). M3-08b's
    /// canvas is full-screen and modal with the tick gated, so a callout underneath it would be
    /// invisible and one above it would compete with the three cards for the two seconds GD §13.1
    /// budgets. The frame the level-up closes is the frame the game starts again, and the first
    /// moment the pause icon this points at is on screen and reachable.
    /// </para>
    /// <para>
    /// <b>It asks the run how many Actives are owned rather than counting them itself</b> (rule 6).
    /// A run restored with two Actives already taken never arms, and it should not — that player has
    /// had actives for a while. Counting locally would arm on the first <c>NodeTaken</c> of every
    /// resumed run instead. <c>NodeTaken</c> carries the <c>Kind</c> (M3-03 rule 7), so a Passive is
    /// refused without a catalog lookup. <b>The number it compares against is 0, not 1, and
    /// <see cref="OnNodeTaken"/> says why</b> — the count it reads is the runner's, and the runner
    /// is handed the new Active after this event goes out.
    /// </para>
    /// <para>
    /// <b>The flag is spent when the callout is <em>shown</em>, not when it is dismissed</b> (rule
    /// 10), and saved on that frame. A player who sees it and is killed two seconds later has seen
    /// it; re-showing it would be the game disagreeing with them about their own memory, and
    /// spending it on dismissal would mean an app killed mid-callout shows it again — which is
    /// exactly the <em>"Never again"</em> CC §6.3 is asking for.
    /// </para>
    /// <para>
    /// <b>It takes no touches from the game</b> (rule 9). The run is ticking again by the time this
    /// is up, so <c>blocksRaycasts</c> stays false in every state and the dismissal tap comes through
    /// the run's <c>InputAdapter</c> — the way <c>HudPresenter</c> reads the death tap, and for its
    /// reason: <em>"tap anywhere"</em> has to include the stick and the Charge button, which a uGUI
    /// raycast would have to be layered above. <c>InputAdapter</c> remains the one class in
    /// <c>Soulvail.Game</c> that reads the Input System; this is its second <em>reader</em>, which is
    /// a different claim.
    /// </para>
    /// <para>
    /// <b>The dwell is unscaled, and that is a deliberate second clock rather than an oversight.</b>
    /// M3-08b rule 4 and M3-09a both argued against one, and both were about animations over a
    /// <c>timeScale</c> of 0. This is the opposite case: the callout outlives a pause the player may
    /// very reasonably take <em>while reading it</em> — GD §7.3's <em>"pause anywhere"</em> is one
    /// tap away, and it is what this hint is pointing at. A scaled dwell would freeze the countdown
    /// with the fight, which is defensible, and then resume it mid-sentence, which is not; an
    /// unscaled one spends its four seconds whether or not the world is moving.
    /// </para>
    /// <para>
    /// <b>The string is resolved as of M3-14a, and this was ledger row 9 at its sharpest</b> — a
    /// callout whose entire job is to tell the player something, telling them
    /// <c>ui.hint.firstActive</c>. <b>Unlike the four pooled cells, this one is injected</b>
    /// (M3-14a rule 11): it is a single component <c>RunScope</c> dresses and injects, so the port
    /// arrives on <see cref="Construct"/> beside the hub rather than as an argument on a draw call.
    /// </para>
    /// </remarks>
    public sealed class FirstActiveHint : MonoBehaviour
    {
        /// <summary>
        /// What the callout says — <em>"Skills can be set to Manual: Pause → Skills"</em>, which is
        /// <c>English.asset</c>'s row for this key (rule 8).
        /// </summary>
        /// <remarks>
        /// Authored here and written on show rather than left to the prefab, so the one string this
        /// screen has cannot drift out of the code that owns it — and so <c>Hint_DrawsEnglish</c> is
        /// asserting against something other than the asset it is reading.
        /// </remarks>
        private static readonly LocKey HintKey = new LocKey("ui.hint.firstActive");

        /// <summary>The dwell a non-finite or non-positive <see cref="_secondsShown"/> falls back to.</summary>
        private const float DefaultSecondsShown = 4f;

        [Tooltip("The callout, switched between alpha 0 and 1. No fade: the run is ticking again " +
                 "by the time this is up, and a tween here would be a third clock.")]
        [SerializeField] private CanvasGroup _root;

        [Tooltip("The one line, resolved through ILocalizer from ui.hint.firstActive.")]
        [SerializeField] private TMP_Text _text;

        [Tooltip("How long the callout stays up with no tap, in UNSCALED seconds — see the class " +
                 "remarks for why unscaled. A guess until a phone exists: whether a corner callout " +
                 "is noticed at all in the seconds after a level-up is ledger row 4.")]
        [SerializeField] private float _secondsShown = DefaultSecondsShown;

        private IRunSession _session;
        private ProfileStore _profiles;
        private InputAdapter _input;
        private ILocalizer _localizer;

        private IDisposable _takenSubscription;
        private IDisposable _closedSubscription;

        /// <summary>A first Active has been taken and the level-up screen has not closed yet.</summary>
        private bool _armed;

        /// <summary>Unscaled seconds since the callout went up.</summary>
        private float _elapsed;

        /// <summary>
        /// How many of this component's own ticks the callout has survived.
        /// </summary>
        /// <remarks>
        /// The first one does not count, and that is <c>HudPresenter._deathFrame</c>'s guard in the
        /// shape this class can use: <c>LevelUpClosed</c> is published from inside the tap that
        /// spent the last pick, so if this component's <c>Update</c> happens to run after the
        /// <c>EventSystem</c> in the same frame, <c>FocusPressedThisFrame</c> is still reporting
        /// <em>that</em> press — and the callout would be dismissed by the tap that caused it.
        /// </remarks>
        private int _ticksShown;

        /// <summary>Whether the callout is up. The one read a test and a later screen need.</summary>
        public bool IsShown => _root != null && _root.alpha > 0f;

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="session">
        /// The run, for the one read that decides whether this is a <em>first</em> Active —
        /// <c>RunState.OwnedActiveCount</c>. Not <c>IPlayerCommands</c>: a hint asks the game for
        /// nothing.
        /// </param>
        /// <param name="profiles">
        /// The live profile, resolved by type from <c>BootScope</c> — <c>EnemyViews</c>' bargain
        /// with <c>EnemyLookBook</c>, one scope on. A store scoped to the run would forget the write
        /// between the level-up that spent the flag and the boundary that saved it (rule 4).
        /// </param>
        /// <param name="input">
        /// The run's input adapter, for the dismissal tap. See the class remarks: through the
        /// adapter rather than a <c>Button</c>, so the callout can be tapped away from anywhere
        /// while taking no touches from the fight.
        /// </param>
        /// <param name="localizer">
        /// What turns <see cref="HintKey"/> into a sentence. Resolved from <c>BootScope</c> one
        /// scope up, like <paramref name="profiles"/> — a table outlives a run for the reason a
        /// profile does (M3-14a rule 4).
        /// </param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        /// <remarks>
        /// Subscribed here rather than in <c>OnEnable</c> — <c>HudPresenter</c>'s reason:
        /// <c>RunScope</c> builds its container from its own <c>Awake</c> and Unity orders no two of
        /// those, so an <c>OnEnable</c> subscription reaches for a hub that may not exist yet.
        /// </remarks>
        [Inject]
        public void Construct(
            DomainEventHub hub,
            IRunSession session,
            ProfileStore profiles,
            InputAdapter input,
            ILocalizer localizer)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            // Disposed before they are replaced, so a component injected twice — which VContainer
            // does not do and a test does — holds one subscription each rather than two.
            _takenSubscription?.Dispose();
            _takenSubscription = hub.Subscribe<NodeTaken>(OnNodeTaken);

            _closedSubscription?.Dispose();
            _closedSubscription = hub.Subscribe<LevelUpClosed>(OnLevelUpClosed);
        }

        /// <exception cref="MissingReferenceException">The root or the label is not dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this hint.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>HudPresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// injected" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (_root == null || _text == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(FirstActiveHint)} is missing its root or its label. A callout that " +
                    "is only partly dressed spends CC §6.3's one chance to say this and shows the " +
                    "player nothing.");
            }

            if (_profiles is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(FirstActiveHint)} was never injected, so it would spend nothing and " +
                    "show nothing. The component is registered by RunScope — drag this object onto " +
                    "its First Active Hint field.");
            }

            // Down whatever the prefab was left dressed as, so a callout someone was editing cannot
            // ship over the arena — HudPresenter's argument, for the fifth screen.
            Hide();
        }

        /// <remarks>
        /// Explicit rather than left to the hub's disposal: a view destroyed before its scope — a
        /// scene reload, an arena opened without a run — would otherwise stay in a subscriber list
        /// and be handed an event for a component Unity has killed.
        /// </remarks>
        private void OnDestroy()
        {
            _takenSubscription?.Dispose();
            _takenSubscription = null;

            _closedSubscription?.Dispose();
            _closedSubscription = null;
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// One frame of the callout being up: the dwell, then the tap.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Split out of <see cref="Update"/> and taking its own delta, so the dwell can be driven to
        /// its end without a real clock — <c>LevelUpPresenter.PassAFrame</c>'s bargain with a
        /// fixture, one number wider. The delta is <em>trusted</em> rather than guarded, which is
        /// M3-07a's answer followed rather than re-argued: the engine's clock is the door, and every
        /// other tick in the project trusts the one it is handed. The Inspector field below it is a
        /// different matter — see <see cref="Dwell"/>.
        /// </para>
        /// <para>
        /// The dwell is checked before the tap, so a callout whose last frame also carries a press
        /// goes down once rather than arguing with itself about which one did it.
        /// </para>
        /// </remarks>
        private void Tick(float unscaledDt)
        {
            if (!IsShown)
            {
                return;
            }

            _elapsed += unscaledDt;

            if (_elapsed >= Dwell)
            {
                Hide();
                return;
            }

            // The first tick does not count — see _ticksShown.
            if (_ticksShown++ == 0)
            {
                return;
            }

            if (_input != null && _input.FocusPressedThisFrame)
            {
                Hide();
            }
        }

        /// <summary>
        /// How long the callout is up for, in unscaled seconds, with the Inspector field's mistakes
        /// answered.
        /// </summary>
        /// <remarks>
        /// <b>A non-finite or non-positive dwell falls back to the default rather than being used.</b>
        /// This is an Inspector door — the owner is meant to tune it on a device (ledger row 4) — and
        /// the two ways it could be wrong are both worse than the default. A NaN never satisfies
        /// <c>&gt;=</c>, so the callout would sit over the fight until the player happened to tap;
        /// a zero or negative dwell would take it down on the frame it went up, spending CC §6.3's
        /// one chance on something nobody could read. <c>PausePresenter.Place</c>'s rule with the
        /// opposite answer, because there the authored layout was the safe thing to fall back to and
        /// here it is the constant.
        /// </remarks>
        private float Dwell =>
            float.IsFinite(_secondsShown) && _secondsShown > 0f ? _secondsShown : DefaultSecondsShown;

        /// <summary>
        /// A node was taken: arm if it is the player's first Active and they have not been told.
        /// </summary>
        /// <remarks>
        /// Every refusal here is silent. This is a hint, and the only thing a wrong answer costs is
        /// that it is not shown — which is what <em>"never again"</em> looks like from the inside on
        /// every run but one.
        /// </remarks>
        private void OnNodeTaken(NodeTaken evt)
        {
            if (_armed || IsShown || evt.Kind != SkillKind.Active)
            {
                return;
            }

            RunState state = _session?.State;

            // **Rule 6's condition, at the number this event actually carries.** The rule reads
            // "OwnedActiveCount is 1"; the count here is <b>0</b> for the first Active, and that is
            // not a correction of the rule but of an assumption about ordering.
            // <c>RunState.OwnedActiveCount</c> is the <em>runner</em>'s tally, and
            // <c>LevelUpFlow.ChooseOffer</c> hands a new Active to the runner <em>after</em>
            // <c>SkillTree.Take</c> has published this event — so what a handler reads is the count
            // before this node. "The run owned no Actives until now" is what <em>first</em> means,
            // and it is also the reading that behaves when a player takes two Actives out of one
            // level-up, where a check at the close would see 2 and say nothing.
            // <c>Hint_SeesTheCountBeforeTheRunnerHasIt</c> is what fails if that order ever moves.
            if (state is null || state.OwnedActiveCount != 0)
            {
                return;
            }

            if (_profiles is null || _profiles.Current.SeenFirstActiveHint)
            {
                return;
            }

            _armed = true;
        }

        /// <summary>
        /// The level-up screen has closed and the run is ticking again: show the callout (rule 7).
        /// </summary>
        private void OnLevelUpClosed(LevelUpClosed evt)
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;

            Show();
        }

        /// <remarks>
        /// <b>The flag is spent first and the callout goes up second</b> (rule 10). The order is the
        /// difference between "shown" meaning what the player saw and meaning what survived the
        /// frame: a write that happened after the alpha could be lost to a kill in between, and the
        /// hint would come back for someone who has already read it.
        /// </remarks>
        private void Show()
        {
            // Re-checked rather than trusted from the arming frame, because a level-up is a frame
            // the game is stopped for and any number of them can sit between the two events.
            if (_root == null || _profiles is null || _profiles.Current.SeenFirstActiveHint)
            {
                return;
            }

            _profiles.Save(_profiles.Current.WithSeenFirstActiveHint(true));

            if (_text != null)
            {
                // English, as of M3-14a. Guarded rather than assumed because Show is reachable from
                // a fixture that never injected — and a callout that threw here would take the frame
                // the level-up closed on with it, which is worse than a callout reading its key.
                _text.text = _localizer is null ? HintKey.ToString() : _localizer.Get(HintKey);
            }

            _elapsed = 0f;
            _ticksShown = 0;

            _root.alpha = 1f;

            // Rule 9, in both states and not just this one: the stick and the Charge button are
            // under this canvas and the fight is running again, so the callout must never be in the
            // raycast path. That is also why the tap comes through InputAdapter.
            _root.blocksRaycasts = false;
            _root.interactable = false;
        }

        /// <remarks>
        /// Nothing is saved here. The flag was spent when the callout went up (rule 10), so
        /// dismissing it is a canvas going down and nothing else.
        /// </remarks>
        private void Hide()
        {
            _elapsed = 0f;
            _ticksShown = 0;

            if (_root == null)
            {
                return;
            }

            _root.alpha = 0f;
            _root.blocksRaycasts = false;
            _root.interactable = false;
        }
    }
}
