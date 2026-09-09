using System;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
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
    /// The player's own row: health, the Aegis, and what happens when the health runs out. GD §16.1
    /// and §16.2 — the first thing in the game that tells the player how they are doing.
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
    /// The two strings on the overlay are raw English, in the prefab. That is the second and last
    /// place in the project where it is allowed (<c>MenuPresenter</c> is the first); M6-10 replaces
    /// them with <c>LocKey</c>s and the parking lot in ROADMAP.md carries the reminder.
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

        [Tooltip("The health bar, with its fill and ghost. The one thing on screen the player is " +
                 "never allowed to be unsure about.")]
        [SerializeField] private HpBarView _hp;

        [Tooltip("The Aegis ring beside the bar.")]
        [SerializeField] private ShieldRingView _shield;

        [Tooltip("The current/max readout beside the bar.")]
        [SerializeField] private TMP_Text _hpText;

        [Tooltip("The death panel: \"You died\" and \"Tap to return\". Hidden until it is needed, " +
                 "and its two strings are raw English until M6-10.")]
        [SerializeField] private GameObject _deathOverlay;

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

        private IDisposable _startedSubscription;
        private IDisposable _damagedSubscription;
        private IDisposable _shieldSubscription;
        private IDisposable _diedSubscription;

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
        [Inject]
        public void Construct(
            DomainEventHub hub,
            IRunSession session,
            SceneLoader loader,
            InputAdapter input)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _input = input ?? throw new ArgumentNullException(nameof(input));

            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _startedSubscription = hub.Subscribe<RunStarted>(OnRunStarted);
            _damagedSubscription = hub.Subscribe<PlayerDamaged>(OnPlayerDamaged);
            _shieldSubscription = hub.Subscribe<PlayerShieldChanged>(OnShieldChanged);
            _diedSubscription = hub.Subscribe<PlayerDied>(OnPlayerDied);
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

            // Drawn once immediately, because whether RunStarted has already been published depends
            // on the order VContainer's entry points and this component's Start happen to run in.
            // Either path lands here; a HUD in a scene with no run at all simply has nothing to
            // draw yet, and says so by staying at whatever the prefab shows.
            Redraw();
        }

        /// <remarks>
        /// Explicit, rather than left to the hub's disposal: a HUD destroyed before its scope — a
        /// scene reload, an arena opened without a run — would otherwise stay in four subscriber
        /// lists and be handed events for a component Unity has killed.
        /// </remarks>
        private void OnDestroy()
        {
            _startedSubscription?.Dispose();
            _damagedSubscription?.Dispose();
            _shieldSubscription?.Dispose();
            _diedSubscription?.Dispose();

            _startedSubscription = null;
            _damagedSubscription = null;
            _shieldSubscription = null;
            _diedSubscription = null;
        }

        /// <remarks>
        /// Does nothing at all until the player is dead, and then does one thing: reads the tap that
        /// takes them back. Everything else on this HUD is driven by events.
        /// </remarks>
        private void Update()
        {
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

        /// <remarks>
        /// Core has already ended the run by the time this arrives — <c>RunSession.Tick</c> calls
        /// <c>End</c> on the same tick, and <c>RunTicker</c> stops ticking — so there is nothing to
        /// stop here. The capsule stands where it fell, which is where the run ended.
        /// </remarks>
        private void OnPlayerDied(PlayerDied evt)
        {
            _deathOverlay.SetActive(true);

            _awaitingTap = true;
            _deathFrame = Time.frameCount;
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

        /// <summary>
        /// Lays the row out left to right in dp: bar, readout, ring.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Here rather than in the prefab, and in one method rather than three, for two reasons. The
        /// first is <c>SkillButton</c>'s: a Scale-With-Screen-Size canvas measures in reference
        /// pixels, so a bar authored at 320 of those is a different physical width on every phone —
        /// and a HUD whose two halves scaled by different rules would be visibly wrong on a tablet,
        /// where the Charge button is sized in real dp. The second is that the three rects have to
        /// agree about where each other are, and three components each doing their own arithmetic
        /// is three chances for them to overlap.
        /// </para>
        /// <para>
        /// Anchored to the parent's top-left so the row follows the safe area rather than the
        /// screen: <c>SafeAreaFitter</c> insets that rect on a notched device, and in landscape the
        /// cutout eats into exactly this corner on one of the two rotations.
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

            PlaceRect(
                _shield.transform as RectTransform,
                left,
                ringTop,
                new Vector2(_ringSizeDp, _ringSizeDp),
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
