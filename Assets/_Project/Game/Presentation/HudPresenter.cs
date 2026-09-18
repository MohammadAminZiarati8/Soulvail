using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Stage;
using Soulvail.Game.Adapters;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using TMPro;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Hud.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The player's own row: health, the Aegis, the level, and what happens when the health runs
    /// out. GD §16.1 and §16.2 — the first thing in the game that tells the player how they are
    /// doing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It renders events and holds no health.</b> Every number on screen arrives as a
    /// <c>PlayerDamaged</c>, a <c>PlayerShieldChanged</c> or a <c>RunStarted</c>, and the two views
    /// below it are handed fractions. Nothing here polls a bar into agreement with core each frame,
    /// which is the point: if the HUD and the game ever disagree, exactly one event was missed and
    /// the fault is in this file rather than anywhere in a simulation.
    /// </para>
    /// <para>
    /// <b>The opening state is the one exception, and it has to be.</b> A run's first frame has no
    /// event to describe it — nothing has happened yet — so <c>RunStarted</c> is handled by reading
    /// <c>RunState</c>'s four narrow health reads. That is the same bargain <c>SkillButton</c> makes
    /// with the cooldown: core still owns the number, the handle to <c>PlayerCombat</c> stays
    /// <c>internal</c>, and nothing here can hurt or heal anyone.
    /// </para>
    /// <para>
    /// <b>There is no cooldown readout, deliberately.</b> GD §16.1 is explicit that the movement
    /// skill gets a radial fill on its own button and <em>never</em> a separate one, and M1-16's
    /// <c>SkillButton</c> already draws it. A second readout here would be a second answer to when
    /// the player may dash.
    /// </para>
    /// <para>
    /// <b>Death is the first thing that ends a run from inside one.</b> Core does the ending — the
    /// session stops running and publishes <c>RunEnded</c> on the tick the killing blow lands — and
    /// this class does the only part that is presentation: it puts the overlay up, waits for a tap,
    /// and asks for the Menu scene, which disposes <c>RunScope</c> and with it every subscription
    /// the run made. It listens for <c>PlayerDied</c> rather than <c>RunEnded</c> on purpose, since
    /// the second of those also fires when a scope is torn down for any other reason and the overlay
    /// has no business appearing over a scene that is already unloading.
    /// </para>
    /// <para>
    /// <b>The two strings on the overlay stopped being raw English at M3-14a</b> (rule 8). They were
    /// typed into <c>Hud.prefab</c> from M1-17 and were the second and last place in the project
    /// where that was allowed; they are now <c>ui.death.title</c> and <c>ui.death.hint</c>, written
    /// from the table when the overlay goes up. <b>The HP readout is deliberately not among them</b>:
    /// <see cref="HpFormat"/> is a number format rather than a sentence, and it survives localisation
    /// unchanged — which is the distinction the parking-lot line drew and this task keeps.
    /// </para>
    /// <para>
    /// <b>The level is a fourth rect in this row rather than a fourth component</b> (M3-10b rule 3).
    /// GD §16.1 puts it <em>"top-left, beside HP"</em>, and <see cref="Place"/> is deliberately one
    /// method for the whole row — see its own remarks. A second class laying out the same row would
    /// be a fourth chance for two rects to overlap, so the label lives here with the bar, the
    /// readout and the ring; the XP <em>strip</em> is <c>XpBarView</c>, because it is a full-width
    /// element on the opposite edge and shares no arithmetic with any of them.
    /// </para>
    /// </remarks>
    public sealed class HudPresenter : MonoBehaviour
    {
        /// <summary>
        /// The <c>current/max</c> readout. <c>{0:0}</c> rather than <c>{0}</c> is load-bearing:
        /// TMP treats an unformatted placeholder as "up to nine decimal places", so the natural
        /// spelling renders 139.5 HP as <c>139.5</c>, while the <c>0</c> asks for a padded integer
        /// and rounds. Passed to the float overload, which formats straight into TMP's backing
        /// array and allocates nothing.
        /// </summary>
        private const string HpFormat = "{0:0}/{1:0}";

        /// <summary><em>"You died"</em> — the death overlay's headline (rule 8).</summary>
        /// <remarks>
        /// Authored here rather than on the prefab, <c>FirstActiveHint.HintKey</c>'s reason: the
        /// string this screen owns cannot drift out of the code that owns it, and the row that
        /// checks it is then asserting against something other than the asset it reads.
        /// </remarks>
        private static readonly LocKey DeathTitleKey = new LocKey("ui.death.title");

        /// <summary><em>"Tap to return"</em> — the instruction beneath it (rule 8).</summary>
        private static readonly LocKey DeathHintKey = new LocKey("ui.death.hint");

        /// <summary>
        /// The level, on its own. <c>{0:0}</c> for <see cref="HpFormat"/>'s reason, and passed to
        /// TMP's float overload so it allocates nothing.
        /// </summary>
        private const string LevelFormat = "{0:0}";

        /// <summary>
        /// How wide the level label is, in dp.
        /// </summary>
        /// <remarks>
        /// <b>A constant rather than a serialized field, and that is deliberate.</b> Every other
        /// number in this row is an Inspector door the owner can tune on a device (ledger row 4) —
        /// and each of those doors owes a non-finite row that <see cref="Place"/> has never had, this
        /// method having laid the row out untested since M1-17. A width that no caller can pass a
        /// value through owes none: it is <c>LevelUpFlow.OverflowDamage</c>'s argument, applied to a
        /// layout. Two digits at the readout's own point size fit inside 48 with room to spare, and
        /// a level past 99 is stage 60-odd.
        /// </remarks>
        private const float LevelWidthDp = 48f;

        [Tooltip("The health bar, with its fill and ghost. The one thing on screen the player is " +
                 "never allowed to be unsure about.")]
        [SerializeField] private HpBarView _hp;

        [Tooltip("The Aegis ring beside the bar.")]
        [SerializeField] private ShieldRingView _shield;

        [Tooltip("The current/max readout beside the bar.")]
        [SerializeField] private TMP_Text _hpText;

        [Tooltip("The player's level, beside the Aegis ring — GD §16.1's \"top-left, beside HP\". " +
                 "Optional on the same terms as the fade: a HUD without one plays exactly the same " +
                 "fight, it just cannot say which level the player is on. The strip that fills " +
                 "toward the next one is XpBarView, on the top edge.")]
        [SerializeField] private TMP_Text _levelText;

        [Tooltip("The death panel: \"You died\" and \"Tap to return\". Hidden until it is needed. " +
                 "Its two strings come from the table as of M3-14a — see the two labels below.")]
        [SerializeField] private GameObject _deathOverlay;

        [Tooltip("\"You died\", written from ui.death.title when the overlay goes up. Optional: a " +
                 "death overlay with no headline still takes the tap that returns to the Menu.")]
        [SerializeField] private TMP_Text _deathTitle;

        [Tooltip("\"Tap to return\", written from ui.death.hint. Optional on the same terms — and " +
                 "it is the more costly of the two to lose, because it is the instruction.")]
        [SerializeField] private TMP_Text _deathHint;

        [Tooltip("The full-screen black cover the stage transition fades behind. Optional on the " +
                 "same terms as the reticle: without it a run still crosses every boundary, the " +
                 "arena just swaps in plain sight.")]
        [SerializeField] private CanvasGroup _fade;

        [Tooltip("The health bar's size in dp — GD §16.1's large top-left bar. Applied at runtime " +
                 "for the reason SkillButton applies its own: a Scale-With-Screen-Size canvas " +
                 "measures in reference pixels, which are a different physical size on every phone.")]
        [SerializeField] private Vector2 _barSizeDp = new Vector2(320f, 24f);

        [Tooltip("How wide the current/max readout is, in dp. Tall as the bar, and beside it.")]
        [Min(1f)]
        [SerializeField] private float _textWidthDp = 96f;

        [Tooltip("The Aegis ring's diameter in dp.")]
        [Min(1f)]
        [SerializeField] private float _ringSizeDp = 40f;

        [Tooltip("Where the row's top-left corner sits, in dp from the safe area's left edge and " +
                 "from its top. The bottom corners belong to thumbs (GD §16.1) — nothing here may " +
                 "move into them.")]
        [SerializeField] private Vector2 _marginDp = new Vector2(16f, 16f);

        [Tooltip("The gap between the bar, the readout and the ring, in dp.")]
        [Min(0f)]
        [SerializeField] private float _gapDp = 8f;

        private IRunSession _session;
        private SceneLoader _loader;
        private InputAdapter _input;
        private ILocalizer _localizer;

        private IDisposable _startedSubscription;
        private IDisposable _damagedSubscription;
        private IDisposable _shieldSubscription;
        private IDisposable _diedSubscription;
        private IDisposable _stageArrivedSubscription;
        private IDisposable _transitionSubscription;
        private IDisposable _leveledSubscription;
        private IDisposable _grantedSubscription;
        private IDisposable _grantExpiredSubscription;

        /// <summary>Where the cover is heading: 1 while the screen is closing, 0 while it opens.</summary>
        private float _fadeTarget;

        /// <summary>Alpha per second, so a zero or negative duration snaps rather than divides.</summary>
        private float _fadeSpeed;

        /// <summary>The overlay is up and the next tap goes back to the menu.</summary>
        private bool _awaitingTap;

        /// <summary>
        /// The frame the overlay went up on. A tap made on that same frame is not an answer to a
        /// panel the player has not seen yet — core publishes the death from inside the Update
        /// phase, so a thumb already coming down would otherwise dismiss the overlay in the frame
        /// it appeared.
        /// </summary>
        private int _deathFrame = -1;

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="session">
        /// The run, for the four health reads a <c>RunStarted</c> has no payload for. Not
        /// <c>IPlayerCommands</c>: a HUD asks the game for nothing.
        /// </param>
        /// <param name="loader">Where the death overlay's tap goes.</param>
        /// <param name="input">
        /// The run's input adapter, for that one tap. Deliberately not a <c>Button</c> under the
        /// overlay: this is the only class in <c>Soulvail.Game</c> allowed to read the Input System
        /// and its own remarks name the HUD as a reader, so going through it keeps that rule intact
        /// — and it makes "tap anywhere" literally anywhere, including over the stick and the
        /// Charge button, which a uGUI raycast would have to be layered above.
        /// </param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        /// <remarks>
        /// Subscribed here rather than in <c>OnEnable</c>, which is what the M1-17 spec sketched and
        /// what <c>ReticleView</c>, <c>FocusGlowView</c> and <c>EnemyHitFeedback</c> all had to move
        /// for the same reason: <c>RunScope</c> injects this from its own <c>Awake</c>, and Unity
        /// gives no order between two <c>Awake</c> calls — so an <c>OnEnable</c> subscription would
        /// be reaching for a hub that may not have arrived yet. Dropped in <c>OnDestroy</c>.
        /// </remarks>
        /// <param name="localizer">
        /// What turns the death overlay's two keys into words (M3-14a rule 8). Resolved from
        /// <c>BootScope</c>, one scope up.
        /// </param>
        [Inject]
        public void Construct(
            DomainEventHub hub,
            IRunSession session,
            SceneLoader loader,
            InputAdapter input,
            ILocalizer localizer)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _startedSubscription = hub.Subscribe<RunStarted>(OnRunStarted);
            _damagedSubscription = hub.Subscribe<PlayerDamaged>(OnPlayerDamaged);
            _shieldSubscription = hub.Subscribe<PlayerShieldChanged>(OnShieldChanged);
            _diedSubscription = hub.Subscribe<PlayerDied>(OnPlayerDied);

            // The level, which is the one event this class gained at M3-10b (rule 3). Not
            // `XpChanged`: that carries a fraction, it arrives on every kill rather than on every
            // level, and the strip it belongs to is XpBarView's — so subscribing to it here would be
            // a second reader of one number in two files.
            _leveledSubscription = hub.Subscribe<LeveledUp>(OnLeveledUp);

            // The two halves of the stage transition, and they are deliberately not symmetrical:
            // core says how long it is giving the screen to close, and says nothing at all about
            // opening it again — the next arrival *is* the world having been swapped, so there is
            // nothing left to wait for (M2-10 rule 9).
            _transitionSubscription = hub.Subscribe<StageTransitionStarted>(OnTransitionStarted);
            _stageArrivedSubscription = hub.Subscribe<StageArrived>(OnStageArrived);

            // The granted shield, which is M3-13b's two (rule 11). Both carry `Total` rather than
            // their own delta — what went on, and what is *left* after one came off — so the bar is
            // told the whole pool every time and never has to add up two sources itself. That is why
            // there is no polling here and no third event: a grant lapsing while another still runs
            // is a ShieldGrantExpired carrying a non-zero Total, which is the only way that case can
            // be got right.
            _grantedSubscription = hub.Subscribe<ShieldGranted>(OnShieldGranted);
            _grantExpiredSubscription = hub.Subscribe<ShieldGrantExpired>(OnShieldGrantExpired);
        }

        /// <exception cref="MissingReferenceException">A view, the readout or the overlay is not dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this presenter.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for the reason <c>SkillButton</c> gives: that is
        /// the earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// injected" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (_hp == null || _shield == null || _hpText == null || _deathOverlay == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(HudPresenter)} is missing one of its four pieces. Drag the HP bar, " +
                    "the Aegis ring, the current/max text and the death panel onto it — a HUD " +
                    "that is only partly dressed is worse than none, because the part that is " +
                    "missing looks like a game that has stopped rather than a field that is empty.");
            }

            if (_session is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(HudPresenter)} was never injected, so nothing on it would ever " +
                    "move. The component is registered by RunScope — drag this object onto its " +
                    "Hud Presenter field.");
            }

            Place();

            // Down whatever the prefab was left dressed as, so a panel someone was editing cannot
            // ship covering the arena.
            _deathOverlay.SetActive(false);

            // Down whatever the prefab was left dressed as, for the same reason: a cover someone
            // was editing must not ship over the arena.
            if (_fade != null)
            {
                _fade.alpha = 0f;
                _fade.blocksRaycasts = false;
            }

            // Drawn once immediately, because whether RunStarted has already been published depends
            // on the order VContainer's entry points and this component's Start happen to run in.
            // Either path lands here; a HUD in a scene with no run at all simply has nothing to
            // draw yet, and says so by staying at whatever the prefab shows.
            Redraw();
        }

        /// <remarks>
        /// Explicit, rather than left to the hub's disposal: a HUD destroyed before its scope — a
        /// scene reload, an arena opened without a run — would otherwise stay in every one of its
        /// subscriber lists and be handed events for a component Unity has killed.
        /// </remarks>
        private void OnDestroy()
        {
            _startedSubscription?.Dispose();
            _damagedSubscription?.Dispose();
            _shieldSubscription?.Dispose();
            _diedSubscription?.Dispose();
            _stageArrivedSubscription?.Dispose();
            _transitionSubscription?.Dispose();
            _leveledSubscription?.Dispose();
            _grantedSubscription?.Dispose();
            _grantExpiredSubscription?.Dispose();

            _grantedSubscription = null;
            _grantExpiredSubscription = null;
            _startedSubscription = null;
            _damagedSubscription = null;
            _shieldSubscription = null;
            _diedSubscription = null;
            _stageArrivedSubscription = null;
            _transitionSubscription = null;
            _leveledSubscription = null;
        }

        /// <remarks>
        /// Does nothing at all until the player is dead, and then does one thing: reads the tap that
        /// takes them back. Everything else on this HUD is driven by events.
        /// </remarks>
        private void Update()
        {
            TickFade();

            if (!_awaitingTap || Time.frameCount <= _deathFrame)
            {
                return;
            }

            if (!_input.FocusPressedThisFrame)
            {
                return;
            }

            // Lowered before the load is asked for, so a second tap arriving while the scene is
            // still coming in cannot start a second load — the same guard MenuPresenter makes by
            // taking its button's interactability away first.
            _awaitingTap = false;

            ReturnToMenu();
        }

        private void OnRunStarted(RunStarted evt)
        {
            Redraw();
        }

        /// <summary>
        /// The player is in the door: close the screen, in the seconds core is giving us.
        /// </summary>
        private void OnTransitionStarted(StageTransitionStarted evt)
        {
            _fadeTarget = 1f;

            // A duration that is zero or worse snaps. Dividing by it would hand the cover an
            // infinite or NaN speed, and a NaN alpha is a canvas group that never renders again.
            _fadeSpeed = evt.Duration > 0f ? 1f / evt.Duration : float.PositiveInfinity;
        }

        /// <summary>
        /// The world underneath has been swapped: open the screen again.
        /// </summary>
        /// <remarks>
        /// <c>StageArrived</c> carries no duration, because opening is not something core is waiting
        /// on — so the beat is <c>StageFlow.FadeTime</c>, read from the same constant core closed it
        /// over. It fires for the first stage of a run too (M2-10 rule 4), which is what makes a
        /// cover someone left dressed opaque in the prefab open itself rather than hide the game.
        /// </remarks>
        private void OnStageArrived(StageArrived evt)
        {
            _fadeTarget = 0f;
            _fadeSpeed = 1f / StageFlow.FadeTime;
        }

        /// <summary>
        /// Moves the cover towards wherever the last stage event pointed it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// On <c>Time.deltaTime</c> rather than on the clamped <c>dt</c> core counts in, and the
        /// difference only ever errs safe: core's step is clamped to <c>SnapshotBuilder.MaxDt</c>,
        /// so on a hitching frame simulated time advances <em>more slowly</em> than the wall clock
        /// and the cover is already opaque when the swap happens. The other way round would show
        /// the player the arena being replaced.
        /// </para>
        /// <para>
        /// A HUD with no cover dressed does nothing here, which is a run that crosses every boundary
        /// in plain sight — playable, and exactly what an undressed test scene wants.
        /// </para>
        /// </remarks>
        private void TickFade()
        {
            if (_fade == null)
            {
                return;
            }

            float alpha = Mathf.MoveTowards(_fade.alpha, _fadeTarget, _fadeSpeed * Time.deltaTime);

            if (Mathf.Approximately(alpha, _fade.alpha))
            {
                return;
            }

            _fade.alpha = alpha;

            // Taken out of the raycast path the moment it is not covering anything, so a cleared
            // cover cannot eat the tap that dismisses the death overlay.
            _fade.blocksRaycasts = alpha > 0f;
        }

        /// <summary>
        /// Rule 1: a hit moves the bars and the readout — unless it was turned away, in which case
        /// it moves nothing and says so with a flash.
        /// </summary>
        /// <remarks>
        /// The fractions come off the event rather than out of <c>RunState</c>, which is what the
        /// event carries them for: a listener that had only the split would have to keep its own
        /// copy of the player's health to derive them. Only the integer readout, which no event
        /// carries, is read from the run.
        /// </remarks>
        private void OnPlayerDamaged(PlayerDamaged evt)
        {
            if (evt.Blocked)
            {
                // Nothing was applied, so nothing on the bars changed and both fractions on the
                // event are the ones already drawn. The flash is the whole of what happened.
                _hp.FlashBlocked();
                return;
            }

            _hp.Set(evt.HpFraction);
            _shield.Set(evt.ShieldFraction);
            WriteHp();
        }

        /// <summary>
        /// Rule 3: a level was crossed, and the event already carries which one.
        /// </summary>
        /// <remarks>
        /// <c>LeveledUp</c> rather than a read of <see cref="RunState.Level"/>, and the event is
        /// published once per level in the order the thresholds were crossed (M3-01a rule 5) — so a
        /// grant worth two levels writes <em>"2"</em> and then <em>"3"</em> in one frame, of which
        /// the player sees the second. The alternative is a handler that reads the run and is
        /// therefore right by coincidence.
        /// </remarks>
        private void OnLeveledUp(LeveledUp evt)
        {
            WriteLevel(evt.Level);
        }

        /// <remarks>
        /// The quiet half of the Aegis: damage that hits the shield arrives as a
        /// <c>PlayerDamaged</c> carrying the same fraction, so this one is the refill coming back on
        /// its own — roughly twenty-five events over the two seconds it takes, rather than one a
        /// frame.
        /// </remarks>
        private void OnShieldChanged(PlayerShieldChanged evt)
        {
            _shield.Set(evt.Fraction);
        }

        /// <summary>
        /// Rule 11: Bulwark put points on the player, and the bar says so.
        /// </summary>
        /// <remarks>
        /// <c>Total</c> rather than <c>Amount</c>, because two grants can be standing at once and the
        /// bar draws the pool. The Aegis ring is deliberately not touched — M3-11a rule 5 from this
        /// side.
        /// </remarks>
        private void OnShieldGranted(ShieldGranted evt)
        {
            WriteGrantedShield(evt.Total);
        }

        /// <summary>
        /// Rule 11: a grant came off. <c>Total</c> is what is <em>left</em>, not zero.
        /// </summary>
        /// <remarks>
        /// The event's own remarks say why: another source may still be holding shield, and a view
        /// that assumed an expiry emptied the pool would erase points the player still has.
        /// </remarks>
        private void OnShieldGrantExpired(ShieldGrantExpired evt)
        {
            WriteGrantedShield(evt.Total);
        }

        /// <summary>
        /// Draws <paramref name="points"/> of granted shield over the player's live maximum.
        /// </summary>
        /// <remarks>
        /// The maximum comes from the run rather than from the event, for <see cref="WriteHp"/>'s
        /// reason: no event carries it, and a bar sized against a stale one would be wrong for the
        /// whole of a Vitality node's life. A zero or non-finite maximum draws nothing rather than
        /// dividing — a run with no player has nothing to say about their shield.
        /// </remarks>
        private void WriteGrantedShield(float points)
        {
            RunState state = _session?.State;

            if (state is null)
            {
                return;
            }

            float max = state.PlayerMaxHp;

            _hp.SetGrantedShield(max > 0f ? points / max : 0f);
        }

        /// <remarks>
        /// Core has already ended the run by the time this arrives — <c>RunSession.Tick</c> calls
        /// <c>End</c> on the same tick, and <c>RunTicker</c> stops ticking — so there is nothing to
        /// stop here. The capsule stands where it fell, which is where the run ended.
        /// </remarks>
        private void OnPlayerDied(PlayerDied evt)
        {
            // Written here rather than at Start, so the two labels cannot be left carrying whatever
            // the prefab was dressed with by someone editing it — and written before the overlay
            // goes up rather than after, so no frame can show the placeholder.
            WriteDeathStrings();

            _deathOverlay.SetActive(true);

            _awaitingTap = true;
            _deathFrame = Time.frameCount;
        }

        /// <summary>The death overlay's two strings, from the table (rule 8).</summary>
        /// <remarks>
        /// Both labels are optional and a null localizer falls back to the key —
        /// <c>MenuPresenter.Write</c>'s answer, for its reason: a death overlay missing a word is a
        /// player who can still tap out of it, and throwing here would strand them on a run that has
        /// already ended. <c>ToString()</c> rather than <c>Key</c>, for <c>TableLocalizer.Get</c>'s.
        /// </remarks>
        private void WriteDeathStrings()
        {
            if (_deathTitle != null)
            {
                _deathTitle.text = _localizer is null
                    ? DeathTitleKey.ToString()
                    : _localizer.Get(DeathTitleKey);
            }

            if (_deathHint != null)
            {
                _deathHint.text = _localizer is null
                    ? DeathHintKey.ToString()
                    : _localizer.Get(DeathHintKey);
            }
        }

        /// <summary>Everything the run can currently say about the player, drawn at once.</summary>
        /// <remarks>
        /// The one place that reads core's state rather than an event, and only for the values a
        /// <c>RunStarted</c> cannot carry. A null state is a HUD in a scene where no run has begun,
        /// which is a workflow rather than a fault — pressing Play with the Run scene open reaches
        /// this a moment before the session starts.
        /// </remarks>
        private void Redraw()
        {
            RunState state = _session?.State;

            if (state is null)
            {
                return;
            }

            _hp.Set(state.PlayerHpFraction);
            _shield.Set(state.PlayerShieldFraction);
            WriteHp();
            WriteLevel(state.Level);

            // The opening state of the granted shield, and it is zero on every resume there will ever
            // be: TimedEffects.Clear forgets every grant at the run's end (M3-11a rule 8) and nothing
            // restores one. Read anyway, on this class's standing reason — whether RunStarted has
            // already been published depends on an order Unity does not give — so that the HUD never
            // has to know which of those two facts is keeping it correct.
            WriteGrantedShield(state.PlayerGrantedShield);
        }

        /// <remarks>
        /// TMP's float overload, which writes into its own backing array: the alternative — an
        /// interpolated string — would allocate one on every hit, which is a phone's worth of
        /// garbage over a run made almost entirely of being hit.
        /// </remarks>
        private void WriteHp()
        {
            RunState state = _session?.State;

            if (state is null)
            {
                return;
            }

            _hpText.SetText(HpFormat, state.PlayerHp, state.PlayerMaxHp);
        }

        /// <remarks>
        /// TMP's float overload for <see cref="WriteHp"/>'s reason, and a null label is a HUD dressed
        /// without one — which is optional on the fade's terms (rule 3, and the field's tooltip).
        /// </remarks>
        private void WriteLevel(int level)
        {
            if (_levelText == null)
            {
                return;
            }

            _levelText.SetText(LevelFormat, level);
        }

        /// <summary>
        /// Lays the row out left to right in dp: bar, readout, ring, level.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Here rather than in the prefab, and in one method rather than four, for two reasons. The
        /// first is <c>SkillButton</c>'s: a Scale-With-Screen-Size canvas measures in reference
        /// pixels, so a bar authored at 320 of those is a different physical width on every phone —
        /// and a HUD whose two halves scaled by different rules would be visibly wrong on a tablet,
        /// where the Charge button is sized in real dp. The second is that the rects have to
        /// agree about where each other are, and four components each doing their own arithmetic
        /// is four chances for them to overlap.
        /// </para>
        /// <para>
        /// Anchored to the parent's top-left so the row follows the safe area rather than the
        /// screen: <c>SafeAreaFitter</c> insets that rect on a notched device, and in landscape the
        /// cutout eats into exactly this corner on one of the two rotations.
        /// </para>
        /// <para>
        /// <b>The level went on the end rather than into the middle</b> (M3-10b rule 3), so that
        /// adding it moved nothing that was already here: every existing rect lands where it landed
        /// before, which matters on a method that has laid this row out untested since M1-17. It is
        /// still <em>"beside HP"</em> in GD §16.1's sense — the whole row is the HP readout — and
        /// <c>Level_IsPlacedInTheHudRow</c> is the first assertion ever made about any of it.
        /// </para>
        /// </remarks>
        private void Place()
        {
            float pxPerDp = PixelsPerDp();

            float left = _marginDp.x;

            left = PlaceRect(_hp.transform as RectTransform, left, _marginDp.y, _barSizeDp, pxPerDp);
            left += _gapDp;

            left = PlaceRect(
                _hpText.transform as RectTransform,
                left,
                _marginDp.y,
                new Vector2(_textWidthDp, _barSizeDp.y),
                pxPerDp);
            left += _gapDp;

            // Centred on the bar's centre line rather than on its top edge, so a ring taller than
            // the bar sits beside it instead of hanging off it.
            float ringTop = _marginDp.y + ((_barSizeDp.y - _ringSizeDp) * 0.5f);

            left = PlaceRect(
                _shield.transform as RectTransform,
                left,
                ringTop,
                new Vector2(_ringSizeDp, _ringSizeDp),
                pxPerDp);
            left += _gapDp;

            // Tall as the bar and back on the bar's own top edge rather than the ring's centre
            // line, so the number reads as part of the row rather than as a caption under the ring.
            // A HUD dressed without a label places nothing and the row is unchanged — PlaceRect's
            // own null answer, reached through a field that is allowed to be empty.
            PlaceRect(
                _levelText == null ? null : _levelText.transform as RectTransform,
                left,
                _marginDp.y,
                new Vector2(LevelWidthDp, _barSizeDp.y),
                pxPerDp);
        }

        /// <summary>
        /// Pins one rect's top-left corner <paramref name="leftDp"/> in and <paramref name="topDp"/>
        /// down from the parent's, and returns where the next one starts.
        /// </summary>
        private static float PlaceRect(
            RectTransform rect,
            float leftDp,
            float topDp,
            Vector2 sizeDp,
            float pxPerDp)
        {
            // A component whose transform is not a RectTransform is dressed wrong, and the guards
            // in Start are what say so — this only has to not throw on the way there.
            if (rect == null)
            {
                return leftDp + sizeDp.x;
            }

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = sizeDp * pxPerDp;
            rect.anchoredPosition = new Vector2(leftDp * pxPerDp, -topDp * pxPerDp);

            return leftDp + sizeDp.x;
        }

        /// <summary>
        /// How many canvas units one dp is worth: device pixels per dp, divided by the canvas scale
        /// so the result is the requested physical size rather than that many reference pixels.
        /// </summary>
        private float PixelsPerDp()
        {
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            return StickShaper.PixelsPerDp(Screen.dpi) / scale;
        }

        /// <summary>
        /// Leaves the run for the menu, which disposes <c>RunScope</c> and ends everything with it.
        /// </summary>
        /// <remarks>
        /// <c>async void</c>, which is otherwise a smell and is exactly right for what is
        /// effectively an event handler — the same call <c>MenuPresenter.Descend</c> makes in the
        /// other direction, and every path out of the await is handled here.
        /// </remarks>
        private async void ReturnToMenu()
        {
            try
            {
                await _loader.LoadAsync(SceneLoader.Menu);
            }
            catch (Exception exception)
            {
                // The load failed, so this object is still alive and the overlay is still up. Arm
                // the tap again rather than stranding the player on a dead screen with a run that
                // has already ended.
                _awaitingTap = true;
                _deathFrame = Time.frameCount;

                Debug.LogException(exception, this);
            }
        }
    }
}
