using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Stage;
using Soulvail.Game.Adapters;
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
    /// <b>Death is no longer this class's, and that is M4-06 rule 2.</b> From M1-17 to M4-05b this
    /// presenter owned the two-word death overlay: it subscribed to <c>PlayerDied</c>, put a panel
    /// up, read a tap off <c>InputAdapter</c> and asked <c>SceneLoader</c> for the Menu.
    /// <c>RunEndPresenter</c> owns all of it now, opening on <c>ShardsAwarded</c> instead so that the
    /// screen cannot be up before its numbers are — and the overlay was <b>replaced rather than
    /// stacked</b>, because two screens for one death is the worst outcome available. <b>Three
    /// dependencies left with it</b>: <c>SceneLoader</c>, <c>InputAdapter</c> and <c>ILocalizer</c>,
    /// each of which the death path was the only reader of, so <see cref="Construct"/> dropped from
    /// five parameters to two and <see cref="Update"/> now does nothing but <see cref="TickFade"/>.
    /// </para>
    /// <para>
    /// <b>The HUD drew no word from M4-06 to M6-03b, and draws three now.</b> <see cref="HpFormat"/>,
    /// <see cref="LevelFormat"/> and <see cref="EssenceFormat"/> are number formats rather than
    /// sentences and survive localisation unchanged, which is the distinction M3-14a drew. The two
    /// death keys that were here moved to <c>RunEndPresenter</c> (M4-06 rule 3) and the
    /// <c>ILocalizer</c> left with them; it came back at M6-03b for the Essence counter's caption,
    /// the meter's caption and the Claiming's name — all three written once at <c>Start</c>, never
    /// per event.
    /// </para>
    /// <para>
    /// <b>GD §16.1's two economy readouts are driven from here, on the boss band's terms</b>
    /// (M6-03b). The Essence counter sits top-right, left of the pause icon; the
    /// <see cref="VeilrotMeterView"/> hangs down the right edge under it. Both are handed numbers by
    /// events — <c>EssenceChanged</c>, <c>VeilrotChanged</c>, <c>ClaimingBegan</c> — and seeded
    /// from <c>RunState</c> on <c>RunStarted</c>, because a resumed run's wallet and meter are
    /// restored <em>silently</em> (M6-01a rule 8, M6-04 rule 9): a screen that only listened would
    /// draw a resumed run at zero on both.
    /// </para>
    /// <para>
    /// <b>The boss's bar is driven from here, and it is not the player's own row</b> (M4-04). GD
    /// §16.2's segmented band is a <see cref="BossBarView"/>, a dumb view handed fractions and flags
    /// exactly as <see cref="_hp"/> and <see cref="_shield"/> are — which is why <c>RunScope</c>
    /// registers nothing new for it and why the five boss subscriptions land in this class. Two
    /// reasons, and the second is the load-bearing one. The first is that this presenter already owns
    /// every rect on this prefab that has to agree with another about where it is, and rule 8's
    /// question is exactly that: the band, the 4 dp XP strip above it and the HP row below it are one
    /// stack. The second is that <b>there is no boss-spawn signal to subscribe to</b> — M4-00a ruled
    /// that <c>EnemySpawned</c> gains no <c>IsBoss</c> flag, deliberately, so what announces a boss is
    /// <c>BossPhaseChanged</c>, published once for phase 0 the moment the body stands up. The
    /// departure <em>is</em> <c>EnemyDied</c>; there is no <c>BossDied</c>. The fill comes off
    /// <c>EnemyDamaged.HpFraction</c>, the same event <c>EnemyHealthBar</c> has read since M3-13b, and
    /// nothing here reads <c>RunState</c> for it: a corpse's blackboard fraction is frozen rather than
    /// zeroed (M4-01a), so the event is the only honest source.
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

        /// <summary>
        /// The level, on its own. <c>{0:0}</c> for <see cref="HpFormat"/>'s reason, and passed to
        /// TMP's float overload so it allocates nothing.
        /// </summary>
        private const string LevelFormat = "{0:0}";

        /// <summary>
        /// The Essence balance, on its own — <see cref="LevelFormat"/>'s spelling for its reasons.
        /// </summary>
        private const string EssenceFormat = "{0:0}";

        /// <summary>
        /// How tall the two captions and the Claiming's name are, in dp. A constant for
        /// <see cref="LevelWidthDp"/>'s reason: no caller passes a value through it. Room for 28 pt,
        /// which is 12.36 dp and the smallest size on this prefab that clears Android's floor.
        /// </summary>
        private const float CaptionHeightDp = 20f;

        /// <summary>How wide the meter's caption and the Claiming's name are, in dp.</summary>
        private const float MeterWordWidthDp = 80f;

        /// <summary>GD §16.1's <em>"small counter"</em>, captioned.</summary>
        private static readonly LocKey EssenceKey = new LocKey("ui.hud.essence");

        /// <summary>The meter's caption, under it.</summary>
        private static readonly LocKey VeilrotKey = new LocKey("ui.hud.veilrot");

        /// <summary>What the Claiming is called, beside the meter once it has begun — rule 5's word.</summary>
        private static readonly LocKey ClaimedKey = new LocKey("ui.hud.claimed");

        /// <summary>
        /// How wide the level label is, in dp.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A constant rather than a serialized field, and that is deliberate.</b> Every other
        /// number in this row is an Inspector door the owner can tune on a device (ledger row 4) —
        /// and each of those doors owes a non-finite row that <see cref="Place"/> has never had, this
        /// method having laid the row out untested since M1-17. A width that no caller can pass a
        /// value through owes none. Two digits at the readout's own point size fit inside 48 with
        /// room to spare, and a level past 99 is stage 60-odd.
        /// </para>
        /// <para>
        /// <b>This used to cite <c>LevelUpFlow.OverflowDamage</c> as the same argument, and that
        /// citation died at M5-06b</b>: Overflow's two numbers were exactly the case ADR-0006 was
        /// about — a number a designer wants to retune — and they are now
        /// <c>ModeDefinition</c> fields. A layout width that nothing in the game reads twice is
        /// still not one, which is why this constant stayed.
        /// </para>
        /// </remarks>
        private const float LevelWidthDp = 48f;

        /// <summary>
        /// No boss is being followed. <c>EnemyRegistry</c> issues ids from 1, so 0 is an id nothing can
        /// ever have.
        /// </summary>
        /// <remarks>
        /// A constant of this class rather than <c>EnemyView.Unbound</c>, which holds the same number
        /// for the same reason: that field is <c>Soulvail.Game.Views</c>' and reaching across for it
        /// would put a namespace edge into a HUD to save declaring a zero.
        /// </remarks>
        private const int NoBoss = 0;

        [Tooltip("The health bar, with its fill and ghost. The one thing on screen the player is " +
                 "never allowed to be unsure about.")]
        [SerializeField] private HpBarView _hp;

        [Tooltip("The Aegis ring beside the bar.")]
        [SerializeField] private ShieldRingView _shield;

        [Tooltip("The current/max readout beside the bar.")]
        [SerializeField] private TMP_Text _hpText;

        [Tooltip("GD §16.2's segmented boss band, across the top of the screen under the XP strip. " +
                 "Optional on the same terms as the fade: a HUD without one plays exactly the same " +
                 "boss fight, the player just cannot see a phase transition coming.")]
        [SerializeField] private BossBarView _bossBar;

        [Tooltip("The player's level, beside the Aegis ring — GD §16.1's \"top-left, beside HP\". " +
                 "Optional on the same terms as the fade: a HUD without one plays exactly the same " +
                 "fight, it just cannot say which level the player is on. The strip that fills " +
                 "toward the next one is XpBarView, on the top edge.")]
        [SerializeField] private TMP_Text _levelText;

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

        [Tooltip("GD §16.1's Veilrot meter, down the right edge. Optional on the boss band's terms: " +
                 "a HUD without one plays the same run, the player just cannot see the Veil.")]
        [SerializeField] private VeilrotMeterView _meter;

        [Tooltip("The meter's caption, under it.")]
        [SerializeField] private TMP_Text _veilrotLabel;

        [Tooltip("The Claiming's name, beside the meter's top. Hidden until the Claiming begins.")]
        [SerializeField] private TMP_Text _claimedLabel;

        [Tooltip("GD §16.1's Essence counter, top-right. Optional on the same terms as the meter.")]
        [SerializeField] private TMP_Text _essenceText;

        [Tooltip("The counter's caption, under it.")]
        [SerializeField] private TMP_Text _essenceLabel;

        [Tooltip("The meter's width and height in dp. Applied at runtime for the health bar's reason.")]
        [SerializeField] private Vector2 _meterSizeDp = new Vector2(12f, 160f);

        [Tooltip("Where the meter's top-right corner sits, in dp in from the safe area's right edge " +
                 "and down from its top. Under the pause icon, which owns the corner itself.")]
        [SerializeField] private Vector2 _meterInsetDp = new Vector2(16f, 76f);

        [Tooltip("The Essence counter's width and height in dp.")]
        [SerializeField] private Vector2 _essenceSizeDp = new Vector2(96f, 24f);

        [Tooltip("Where the counter's top-right corner sits, in dp in from the safe area's right " +
                 "edge and down from its top — left of the 44 dp pause icon and its 16 dp margin.")]
        [SerializeField] private Vector2 _essenceInsetDp = new Vector2(68f, 16f);

        private IRunSession _session;
        private ILocalizer _localizer;

        private IDisposable _startedSubscription;
        private IDisposable _damagedSubscription;
        private IDisposable _shieldSubscription;
        private IDisposable _stageArrivedSubscription;
        private IDisposable _transitionSubscription;
        private IDisposable _leveledSubscription;
        private IDisposable _grantedSubscription;
        private IDisposable _grantExpiredSubscription;
        private IDisposable _bossPhaseSubscription;
        private IDisposable _bossBeatStartedSubscription;
        private IDisposable _bossBeatEndedSubscription;
        private IDisposable _enemyDamagedSubscription;
        private IDisposable _enemyDiedSubscription;
        private IDisposable _essenceSubscription;
        private IDisposable _veilrotSubscription;
        private IDisposable _claimingSubscription;

        /// <summary>The meter's last value, so <c>ClaimingBegan</c> can raise the mark without moving it.</summary>
        private float _veilrot;

        /// <summary>Whether the Claiming has begun — <c>RunState.IsClaimed</c>'s latch, mirrored.</summary>
        private bool _isClaimed;

        /// <summary>Where the cover is heading: 1 while the screen is closing, 0 while it opens.</summary>
        private float _fadeTarget;

        /// <summary>Alpha per second, so a zero or negative duration snaps rather than divides.</summary>
        private float _fadeSpeed;

        /// <summary>
        /// The boss the band is following, or <see cref="NoBoss"/>. What every boss handler filters on.
        /// </summary>
        /// <remarks>
        /// <c>EnemyDamaged</c> and <c>EnemyDied</c> are published for every body in the arena, so the
        /// filter is what stops a Husk dying at 66 % health from taking the Warden's bar down —
        /// <c>EnemyHealthBar</c>'s bound id, one screen up.
        /// </remarks>
        private int _bossId = NoBoss;

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="session">
        /// The run, for the four health reads a <c>RunStarted</c> has no payload for. Not
        /// <c>IPlayerCommands</c>: a HUD asks the game for nothing.
        /// </param>
        /// <param name="localizer">
        /// The three captions' words (M6-03b) — written once at <c>Start</c>, never per event.
        /// </param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        /// <remarks>
        /// <para>
        /// Subscribed here rather than in <c>OnEnable</c>, which is what the M1-17 spec sketched and
        /// what <c>ReticleView</c>, <c>FocusGlowView</c> and <c>EnemyHitFeedback</c> all had to move
        /// for the same reason: <c>RunScope</c> injects this from its own <c>Awake</c>, and Unity
        /// gives no order between two <c>Awake</c> calls — so an <c>OnEnable</c> subscription would
        /// be reaching for a hub that may not have arrived yet. Dropped in <c>OnDestroy</c>.
        /// </para>
        /// <para>
        /// <b>Two parameters, down from five</b> (M4-06 rule 2). <c>SceneLoader</c>,
        /// <c>InputAdapter</c> and <c>ILocalizer</c> left with the death overlay, because the death
        /// path was the only reader of all three. <b>Three again as of M6-03b</b>, and the third is
        /// an <c>ILocalizer</c> for GD §16.1's economy readouts rather than for a death screen.
        /// </para>
        /// </remarks>
        [Inject]
        public void Construct(DomainEventHub hub, IRunSession session, ILocalizer localizer)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _startedSubscription = hub.Subscribe<RunStarted>(OnRunStarted);
            _damagedSubscription = hub.Subscribe<PlayerDamaged>(OnPlayerDamaged);
            _shieldSubscription = hub.Subscribe<PlayerShieldChanged>(OnShieldChanged);

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

            // GD §16.2's boss band, which is five events and no sixth (M4-04 rules 1, 2, 3 and 5).
            // BossPhaseChanged is the *arrival*: it is published for phase 0 the moment the body
            // stands up as well as at each threshold, which is what makes M4-00a's refusal to put an
            // IsBoss flag on EnemySpawned pay for itself. The two beat events are rule 5. EnemyDamaged
            // is the fill and EnemyDied is the departure — the boss is an ordinary agent (M4-01b rule
            // 2), so there is no BossSpawned and no BossDied to subscribe to instead.
            _bossPhaseSubscription = hub.Subscribe<BossPhaseChanged>(OnBossPhaseChanged);
            _bossBeatStartedSubscription = hub.Subscribe<BossBeatStarted>(OnBossBeatStarted);
            _bossBeatEndedSubscription = hub.Subscribe<BossBeatEnded>(OnBossBeatEnded);
            _enemyDamagedSubscription = hub.Subscribe<EnemyDamaged>(OnEnemyDamaged);
            _enemyDiedSubscription = hub.Subscribe<EnemyDied>(OnEnemyDied);

            // GD §16.1's economy readouts (M6-03b rule 2). Each event carries the value after the
            // movement, so nothing here keeps a sum; RunStarted seeds all three from the run.
            _essenceSubscription = hub.Subscribe<EssenceChanged>(OnEssenceChanged);
            _veilrotSubscription = hub.Subscribe<VeilrotChanged>(OnVeilrotChanged);
            _claimingSubscription = hub.Subscribe<ClaimingBegan>(OnClaimingBegan);
        }

        /// <exception cref="MissingReferenceException">A view or the readout is not dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this presenter.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for the reason <c>SkillButton</c> gives: that is
        /// the earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// injected" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (_hp == null || _shield == null || _hpText == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(HudPresenter)} is missing one of its three pieces. Drag the HP bar, " +
                    "the Aegis ring and the current/max text onto it — a HUD that is only partly " +
                    "dressed is worse than none, because the part that is missing looks like a " +
                    "game that has stopped rather than a field that is empty.");
            }

            if (_session is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(HudPresenter)} was never injected, so nothing on it would ever " +
                    "move. The component is registered by RunScope — drag this object onto its " +
                    "Hud Presenter field.");
            }

            Place();
            WriteWords();

            // Down whatever the prefab was left dressed as, so a cover someone was editing cannot
            // ship over the arena.
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
            _stageArrivedSubscription?.Dispose();
            _transitionSubscription?.Dispose();
            _leveledSubscription?.Dispose();
            _grantedSubscription?.Dispose();
            _grantExpiredSubscription?.Dispose();
            _bossPhaseSubscription?.Dispose();
            _bossBeatStartedSubscription?.Dispose();
            _bossBeatEndedSubscription?.Dispose();
            _enemyDamagedSubscription?.Dispose();
            _enemyDiedSubscription?.Dispose();
            _essenceSubscription?.Dispose();
            _veilrotSubscription?.Dispose();
            _claimingSubscription?.Dispose();

            _essenceSubscription = null;
            _veilrotSubscription = null;
            _claimingSubscription = null;
            _grantedSubscription = null;
            _grantExpiredSubscription = null;
            _bossPhaseSubscription = null;
            _bossBeatStartedSubscription = null;
            _bossBeatEndedSubscription = null;
            _enemyDamagedSubscription = null;
            _enemyDiedSubscription = null;
            _startedSubscription = null;
            _damagedSubscription = null;
            _shieldSubscription = null;
            _stageArrivedSubscription = null;
            _transitionSubscription = null;
            _leveledSubscription = null;
        }

        /// <remarks>
        /// <b>One call, and that is M4-06 rule 2 seen from the cheapest angle.</b> This method read
        /// the Input System every frame of every run so that the death overlay could take a tap; the
        /// overlay is <c>RunEndPresenter</c>'s now and takes a <c>Button</c>, so all that is left is
        /// the stage cover. Everything else on this HUD is driven by events.
        /// </remarks>
        private void Update()
        {
            TickFade();
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
            // cover cannot eat a touch meant for the stick, the Charge button or the pause icon.
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
        /// Rules 1, 2 and 4: a boss is in a phase, and the first one it is ever in is how we learn
        /// there is a boss at all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Only a fight this class was not already following resizes the band.</b> The event is
        /// published at phase 0 <em>and</em> at each crossing, and the count and the thresholds are the
        /// same fight's every time — so a crossing is handled by doing nothing here and letting the
        /// <c>BossBeatStarted</c> that follows it one line later be what the player sees. Binding again
        /// would be harmless (<see cref="BossBarView.Bind"/> leaves the fill alone) and saying so is
        /// cheaper than relying on it.
        /// </para>
        /// <para>
        /// <b>A second boss replaces the first rather than queueing behind it.</b> No mode authors two
        /// at once and GD §9.1 has one boss a stage, so this is a statement about what the band does if
        /// that ever changes: it follows the newest, because a bar showing a boss the player is no
        /// longer fighting is worse than one that switched.
        /// </para>
        /// </remarks>
        private void OnBossPhaseChanged(BossPhaseChanged evt)
        {
            if (_bossBar == null || evt.EnemyId == _bossId)
            {
                return;
            }

            _bossBar.Bind(evt.OfPhases, evt.EntersBelow);

            // Adopted only if the band actually went up. A count below 1 is a broken reading the band
            // refuses to draw (BossSpec makes it unreachable from an asset), and remembering the id
            // anyway would make this class deaf to the next honest event about the same boss.
            _bossId = _bossBar.IsShown ? evt.EnemyId : NoBoss;
        }

        /// <summary>Rule 5: the boss cannot be hurt, and the band says so.</summary>
        private void OnBossBeatStarted(BossBeatStarted evt)
        {
            if (_bossBar == null || evt.EnemyId != _bossId)
            {
                return;
            }

            _bossBar.SetBeat(true);
        }

        /// <summary>Rule 5's other half: it can be hurt again.</summary>
        /// <remarks>
        /// Published whether or not anything was watching, and never when the boss dies during a beat
        /// (the event's own remarks) — so there is no path where the band is left dimmed over a fight
        /// that has resumed, and none where it waits for an end that will not come.
        /// </remarks>
        private void OnBossBeatEnded(BossBeatEnded evt)
        {
            if (_bossBar == null || evt.EnemyId != _bossId)
            {
                return;
            }

            _bossBar.SetBeat(false);
        }

        /// <summary>
        /// Rule 3: the boss took a hit, and the fraction on the event is the whole of the band's fill.
        /// </summary>
        /// <remarks>
        /// <c>EnemyDamaged</c> rather than a second source, which is the point of rule 3's <em>"one
        /// quantity"</em>: this is the same event <c>EnemyHealthBar</c> has drawn since M3-13b, it
        /// carries the fraction <em>after</em> the hit, and a hit that landed on nobody publishes
        /// nothing at all. Every other body in the arena is filtered out by the id.
        /// </remarks>
        private void OnEnemyDamaged(EnemyDamaged evt)
        {
            if (_bossBar == null || _bossId == NoBoss || evt.Id != _bossId)
            {
                return;
            }

            _bossBar.SetFraction(evt.HpFraction);
        }

        /// <summary>Rule 2: the boss is dead, so the band leaves.</summary>
        /// <remarks>
        /// <b><c>EnemyDied</c> rather than <c>EnemyDespawned</c></b>, and the gap between them is
        /// <c>EnemySystem.CorpseTime</c>: the body dissolves for a beat after the killing blow, and a
        /// full-width band hanging over a boss the player has already killed would read as a fight that
        /// had not finished. There is no <c>BossDied</c> — this is it, with the id the phase event has
        /// been naming all fight.
        /// </remarks>
        private void OnEnemyDied(EnemyDied evt)
        {
            if (_bossBar == null || _bossId == NoBoss || evt.Id != _bossId)
            {
                return;
            }

            _bossId = NoBoss;

            _bossBar.Hide();
        }

        /// <summary>M6-03b rule 2: the wallet moved, and the event carries where it landed.</summary>
        private void OnEssenceChanged(EssenceChanged evt)
        {
            WriteEssence(evt.Balance);
        }

        /// <summary>M6-03b rule 2: the meter moved, up or down, and the event carries where to.</summary>
        private void OnVeilrotChanged(VeilrotChanged evt)
        {
            _veilrot = evt.Value;

            WriteMeter();
        }

        /// <summary>
        /// M6-03b rule 4: the Claiming began. The mark goes up and the fill stays where
        /// <c>VeilrotChanged</c> left it.
        /// </summary>
        private void OnClaimingBegan(ClaimingBegan evt)
        {
            _isClaimed = true;

            WriteMeter();
        }

        /// <summary>The counter, in GD §16.4's reward gold.</summary>
        /// <remarks>TMP's float overload for <see cref="WriteHp"/>'s reason: it allocates nothing.</remarks>
        private void WriteEssence(int balance)
        {
            if (_essenceText == null)
            {
                return;
            }

            _essenceText.color = Palette.Essence;
            _essenceText.SetText(EssenceFormat, balance);
        }

        /// <summary>The meter and the Claiming's name, from the two values this class mirrors.</summary>
        /// <remarks>
        /// The name is <see cref="Palette.Veilrot"/> and never <see cref="Palette.Danger"/> — rule 5,
        /// which is about the word as much as the bar.
        /// </remarks>
        private void WriteMeter()
        {
            if (_meter != null)
            {
                _meter.Set(_veilrot, _isClaimed);
            }

            if (_claimedLabel != null)
            {
                _claimedLabel.color = Palette.Veilrot;

                if (_claimedLabel.gameObject.activeSelf != _isClaimed)
                {
                    _claimedLabel.gameObject.SetActive(_isClaimed);
                }
            }
        }

        /// <summary>The three captions, once. A missing label is a HUD dressed without it.</summary>
        private void WriteWords()
        {
            Write(_essenceLabel, EssenceKey, Palette.Essence);
            Write(_veilrotLabel, VeilrotKey, Palette.Veilrot);
            Write(_claimedLabel, ClaimedKey, Palette.Veilrot);
        }

        private void Write(TMP_Text label, LocKey key, Color colour)
        {
            if (label == null)
            {
                return;
            }

            label.text = _localizer is null ? key.ToString() : _localizer.Get(key);
            label.color = colour;
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

            // M6-03b rule 2: the wallet and the meter are restored silently on a resume, so this is
            // the only way a resumed run's 317 Essence and 78 Veilrot ever reach the screen.
            _veilrot = state.Veilrot;
            _isClaimed = state.IsClaimed;

            WriteEssence(state.Essence);
            WriteMeter();
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

            PlaceRightEdge(pxPerDp);
        }

        /// <summary>
        /// M6-03b rule 10: the Essence counter and the Veilrot meter, in dp from the top-right corner.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A second method rather than more of <see cref="Place"/>'s row, because nothing here shares
        /// an edge with it: the row reads left to right from the top-left, and these two hang from the
        /// top-right around the pause icon, which <c>PausePresenter</c> places 44 dp across at a 16 dp
        /// margin. So the counter's default inset is 68 dp from the right and the meter's is 76 dp
        /// down — beside the icon and under it respectively.
        /// </para>
        /// <para>
        /// <b>A nonsense dp field leaves the prefab's layout alone</b>, <c>XpBarView.Place</c>'s
        /// answer: both readouts are authored on <c>Hud.prefab</c>, so there is something honest to
        /// fall back to, where writing a NaN into a <c>sizeDelta</c> is a rect that never renders
        /// again. The meter's marks are placed either way — they are fractions of whatever the meter
        /// is.
        /// </para>
        /// </remarks>
        private void PlaceRightEdge(float pxPerDp)
        {
            if (IsUsableSize(_essenceSizeDp) && IsUsableInset(_essenceInsetDp))
            {
                PlaceFromRight(_essenceText, _essenceInsetDp, _essenceSizeDp, pxPerDp);
                PlaceFromRight(
                    _essenceLabel,
                    new Vector2(_essenceInsetDp.x, _essenceInsetDp.y + _essenceSizeDp.y),
                    new Vector2(_essenceSizeDp.x, CaptionHeightDp),
                    pxPerDp);
            }

            if (IsUsableSize(_meterSizeDp) && IsUsableInset(_meterInsetDp))
            {
                PlaceFromRight(_meter, _meterInsetDp, _meterSizeDp, pxPerDp);

                // The caption right-aligned under the meter, and the Claiming's name beside its top —
                // both wider than a 12 dp bar, so both hang to the bar's left rather than over it.
                PlaceFromRight(
                    _veilrotLabel,
                    new Vector2(_meterInsetDp.x, _meterInsetDp.y + _meterSizeDp.y),
                    new Vector2(MeterWordWidthDp, CaptionHeightDp),
                    pxPerDp);
                PlaceFromRight(
                    _claimedLabel,
                    new Vector2(_meterInsetDp.x + _meterSizeDp.x + _gapDp, _meterInsetDp.y),
                    new Vector2(MeterWordWidthDp, CaptionHeightDp),
                    pxPerDp);
            }

            if (_meter != null)
            {
                _meter.PlaceThresholds(pxPerDp);
            }
        }

        /// <summary>
        /// Pins one component's top-right corner <paramref name="insetDp"/> in from the parent's.
        /// </summary>
        private static void PlaceFromRight(Component piece, Vector2 insetDp, Vector2 sizeDp, float pxPerDp)
        {
            if (piece == null || piece.transform is not RectTransform rect)
            {
                return;
            }

            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = sizeDp * pxPerDp;
            rect.anchoredPosition = new Vector2(-insetDp.x * pxPerDp, -insetDp.y * pxPerDp);
        }

        private static bool IsUsableSize(Vector2 dp) =>
            float.IsFinite(dp.x) && float.IsFinite(dp.y) && dp.x > 0f && dp.y > 0f;

        private static bool IsUsableInset(Vector2 dp) =>
            float.IsFinite(dp.x) && float.IsFinite(dp.y) && dp.x >= 0f && dp.y >= 0f;

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
    }
}
