using System;
using System.Collections.Generic;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEngine;
using VContainer;
using VContainer.Unity;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and the Run scene's reference to this
// component would silently deserialise as null (M0-11).
namespace Soulvail.Game.Composition
{
    /// <summary>
    /// The scope one run lives in. A child of <see cref="BootScope"/>, created with the Run scene
    /// and disposed when the scene unloads — which is what ends the run, along with every
    /// subscription and every object the run built. See AR §7 and ADR-0002.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No parent is assigned in the scene. A <see cref="LifetimeScope"/> without one asks
    /// <c>VContainerSettings</c> for the root, so this resolves to <see cref="BootScope"/> whether
    /// the Run scene was reached through the Menu or opened directly and played.
    /// </para>
    /// <para>
    /// The split with <c>RunInstaller</c> is by what needs a scene. Everything a headless test can
    /// build lives there; what is registered here is the half that cannot exist without this
    /// scene — the view in it, the loop that drives it, and the adapters that sit either side of
    /// that loop. <c>SnapshotBuilder</c> and <c>InputAdapter</c> would install cleanly in the
    /// static half, but the builder needs the view and the adapter is only ever read by the
    /// ticker, so keeping the frame's pieces in one place is worth more than the symmetry.
    /// <c>TapToFocusAdapter</c> has no choice: it needs this scene's camera (M1-09).
    /// <c>EnemyViews</c> and the run's <c>SpawnPlan</c> join them for the same reason (M1-07):
    /// both are made of references to this scene's prefab, its parent transform and the positions
    /// dressed into it.
    /// </para>
    /// </remarks>
    public sealed class RunScope : LifetimeScope
    {
        /// <summary>
        /// How many screen-edge arrows exist before the run starts.
        /// </summary>
        /// <remarks>
        /// Eight rather than the device cap: every arrow is an off-screen threat inside 16 m, and a
        /// wave that puts more than eight of those behind the camera at once is a wave the player
        /// has already lost. The set grows past this if it ever happens and keeps what it grew, so
        /// the number is a prediction rather than a limit — it only decides whether the
        /// <c>Instantiate</c> happens while the scene loads or on a frame a Spitter is winding up
        /// (AR §14, GD §11.3).
        /// </remarks>
        private const int ThreatArrowPrewarm = 8;

        /// <summary>
        /// How many ground rings exist before the run starts.
        /// </summary>
        /// <remarks>
        /// Eight, and the arithmetic is the director's: a spawn ring is up for
        /// <c>SpawnDirector.TelegraphTime</c> (0.8 s) and the bodies of a wave are spaced
        /// <c>SpawnInterval</c> (0.35 s) apart, so a wave has three of them on the floor at once at
        /// the very most. The rest is headroom for blast rings, which arrive with deaths rather than
        /// on a clock and so have no ceiling worth computing — several Bloaters can be killed in one
        /// swing. The set grows past this if it ever happens and keeps what it grew, so the number
        /// only decides whether the <c>Instantiate</c> happens while the scene loads or on the frame
        /// a wave is announced (AR §14, GD §11.3).
        /// </remarks>
        private const int TelegraphRingPrewarm = 8;

        [SerializeField] private PlayerView _playerView;

        [Tooltip("The body a run wears when its class names none: Prefabs/Player/Bodies/Knight. " +
                 "Required, like the Player View — the body is raised under it at the start of " +
                 "every run, and without one the player would be an invisible capsule.")]
        [SerializeField] private GameObject _defaultBody;

        [Tooltip("The dash, on the Player object. Not optional, unlike the reticle and the glow: " +
                 "without it a Charge moves nothing and sweeps nobody, and it would fail silently.")]
        [SerializeField] private ChargeMotion _chargeMotion;

        [Tooltip("The Charge button on the HUD. Optional — an arena without a HUD is playable " +
                 "from a keyboard, it just cannot be dashed with a thumb.")]
        [SerializeField] private SkillButton _skillButton;

        [Tooltip("CC §6.2's four slot buttons, clustered beside the Charge on the HUD. Optional on " +
                 "the Charge button's terms — an arena without one plays the same fight and every " +
                 "skill the player set to Manual is uncastable, which is the whole of what this " +
                 "object is for.")]
        [SerializeField] private SkillBarPresenter _skillBar;

        [Tooltip("The player's row on the HUD: health, the Aegis, the level and the boss band. " +
                 "Optional on the same terms as the reticle — an arena without one plays exactly " +
                 "the same, it just cannot say how the player is doing. The way out of a death is " +
                 "the run-end screen's as of M4-06, and that one is required.")]
        [SerializeField] private HudPresenter _hudPresenter;

        [Tooltip("The run-end screen: the payout and one button back to the Menu, on its own canvas " +
                 "above every other. Required, unlike every optional screen below it — a scene " +
                 "dressed without it strands the player on a dead run with no way out, because the " +
                 "death overlay's tap left HudPresenter at M4-06.")]
        [SerializeField] private RunEndPresenter _runEndPresenter;

        [Tooltip("GD §16.1's XP strip, along the HUD's top edge. Optional on the HUD's terms — a " +
                 "scene without one plays the same fight and simply never says how close the next " +
                 "level is.")]
        [SerializeField] private XpBarView _xpBar;

        [Tooltip("GD §16.1's auto-cast cooldowns, under the health bar. Optional on the HUD's " +
                 "terms, and the one here whose absence costs the *default* build the most: every " +
                 "skill starts on Auto (CC §6.1), so a scene without this row is one where a " +
                 "player who never opens a menu has no readout of their build at all.")]
        [SerializeField] private AutoCastRow _autoCastRow;

        [Tooltip("The Overflow announcement, on the HUD. Optional on the HUD's terms — a scene " +
                 "without one grants CH §5.2's Overflow silently, which by stage 30 is more than " +
                 "half the power the player has gained (ledger row 1).")]
        [SerializeField] private OverflowToast _overflowToast;

        [Tooltip("The level-up screen: three cards and a header, on its own canvas above the HUD. " +
                 "Optional on the HUD's terms — but read its registration below before leaving it " +
                 "empty, because what its absence costs is not what the HUD's costs.")]
        [SerializeField] private LevelUpPresenter _levelUpPresenter;

        [Tooltip("CH §5.4's half-tree moment: classes, then branches, on its own canvas above the " +
                 "level-up's. Optional on the level-up screen's terms — and read its registration " +
                 "below before leaving it empty, because a run that reaches six nodes without it " +
                 "stops for good.")]
        [SerializeField] private SplashPresenter _splashPresenter;

        [Tooltip("GD §13.3's Sanctum: four priced services and a Leave button, on its own canvas " +
                 "between the pause's and the level-up's. Optional on the splash screen's terms — " +
                 "and read its registration below, because a run without it stops at the first " +
                 "stage it clears.")]
        [SerializeField] private SanctumPresenter _sanctumPresenter;

        [Tooltip("The pause screen: the top-right icon and the panel behind it, on its own canvas " +
                 "between the HUD's and the level-up's. Optional on the HUD's terms — a scene " +
                 "without one plays exactly the same fight, it just cannot be stopped from inside.")]
        [SerializeField] private PausePresenter _pausePresenter;

        [Tooltip("The Skills screen: CC §6.3's list, on its own canvas between the pause panel's " +
                 "and the level-up's. Optional on the pause screen's terms — a scene without one " +
                 "plays the same fight and its pause panel simply does not offer the button.")]
        [SerializeField] private SkillsPresenter _skillsPresenter;

        [Tooltip("The tree view: CH §5.1's three branches on their own canvas above every other " +
                 "screen, because it is the only one with two doors into it. Optional on the pause " +
                 "screen's terms — a scene without one plays the same fight and neither door " +
                 "offers its button.")]
        [SerializeField] private TreeViewPresenter _treeViewPresenter;

        [Tooltip("CC §6.3's one-time callout, on its own canvas under the pause icon. Optional on " +
                 "the same terms as everything below it — a scene without one plays the same fight " +
                 "and never tells the player about Manual. Its flag lives on the profile, so a run " +
                 "played without this object still has the hint unspent.")]
        [SerializeField] private FirstActiveHint _firstActiveHint;

        [SerializeField] private DebugOverlay _debugOverlay;

        [Tooltip("The camera the arena is seen through. Assigned rather than found: Camera.main " +
                 "is a tagged scene lookup, which is FindObjectOfType wearing a hat.")]
        [SerializeField] private Camera _camera;

        [Tooltip("The ring drawn under the current target. Optional, like the overlay — an arena " +
                 "without one is playable, just harder to read.")]
        [SerializeField] private ReticleView _reticle;

        [Tooltip("The cyan disc under a planted character (CC §4.3). On the Player object, and " +
                 "optional on the same terms as the reticle: without it the Focus ramp still " +
                 "runs, it is just invisible.")]
        [SerializeField] private FocusGlowView _focusGlow;

        [Tooltip("The shell drawn while a granted shield is up (CC §6.4). On the Player object, " +
                 "and optional on the same terms as the glow: without it Bulwark still absorbs " +
                 "exactly as much, the player just cannot see that it is there.")]
        [SerializeField] private BulwarkView _bulwark;

        [Tooltip("The one enemy body prefab. Every archetype shares it until M2-06 gives them " +
                 "silhouettes of their own.")]
        [SerializeField] private EnemyView _enemyPrefab;

        [Tooltip("Where spawned enemy bodies are parented. Optional — they go to the scene root " +
                 "without it, which is untidy rather than wrong.")]
        [SerializeField] private Transform _enemyParent;

        [Tooltip("The one Wight body prefab (CH §3.2). There is one kind of minion, and it shares " +
                 "the enemy body's mesh at 0.7 of its scale in the player's own cyan — smaller " +
                 "than every archetype, so size is what separates a Wight from the player.")]
        [SerializeField] private MinionView _minionPrefab;

        [Tooltip("Where standing Wights are parented. Optional — they go to the scene root " +
                 "without it, which is untidy rather than wrong. Deliberately not the arena: one " +
                 "is torn down and raised again at every stage boundary (M2-11a).")]
        [SerializeField] private Transform _minionParent;

        [Tooltip("The corpse a Shroudstep leaves (CH §3.2, M5-03). The shared body at the player's " +
                 "silhouette, in the player's own cyan at half alpha — theirs, and dead. Not a " +
                 "floor decal: a flat patch an enemy walks at would read as a hazard, and that " +
                 "colour is reserved.")]
        [SerializeField] private DecoyView _decoyPrefab;

        [Tooltip("The one bolt prefab. Every archetype's shot shares it, the same argument the " +
                 "enemy prefab makes — a body per kind of shot arrives with the art.")]
        [SerializeField] private ProjectileView _projectilePrefab;

        [Tooltip("Where bolts in flight are parented. Optional on the same terms as the enemy " +
                 "parent — they go to the scene root without it.")]
        [SerializeField] private Transform _projectileParent;

        [Tooltip("The layers a swing sweeps: the Enemy layer, and nothing else. Authored rather " +
                 "than looked up by name, so a renamed layer is a visible diff instead of a " +
                 "string that stops resolving.")]
        [SerializeField] private LayerMask _enemyLayer;

        [Tooltip("The layers that block an enemy's shot: the Cover layer M2-11a puts an arena's " +
                 "pillars on, and nothing else. Never the Enemy layer — a Spitter that could not " +
                 "fire because a Husk stood in front of it reads as broken (GD §7.2).")]
        [SerializeField] private LayerMask _coverLayer;

        [Tooltip("The archetype the dummies below are spawned as — and what the enemy pool's " +
                 "prewarm keys on, so empty means no bodies built up front. An arena that starts " +
                 "bare empties the list below and keeps this, as Run.unity does (M6-11c).")]
        [SerializeField] private EnemyDefinition _dummySpec;

        [Tooltip("Where the dummies dressed into this scene stand when the player walks in, in " +
                 "world metres. Not spawn points: where a wave may arrive is authored on the " +
                 "arena prefab from M2-11a on, because a run has one room per stage.")]
        [SerializeField] private Vector3[] _dummyPositions;

        [Tooltip("One screen-edge arrow (GD §16.1). Optional on the same terms as the reticle — " +
                 "an arena without one plays identically, it just cannot say where the thing " +
                 "shooting at you from off screen is standing.")]
        [SerializeField] private RectTransform _threatArrowPrefab;

        [Tooltip("Where arrows are parented: the Arrows object on the HUD, under SafeArea. Its " +
                 "rect is the border they are placed on, which is how a notch is avoided without " +
                 "a second copy of the safe-area arithmetic.")]
        [SerializeField] private RectTransform _threatArrowRoot;

        [Tooltip("The one ground ring prefab (GD §7.1). Both kinds of ring — the spawn countdown " +
                 "and the blast flash — are the same body, told apart by how they are bound.")]
        [SerializeField] private TelegraphRingView _telegraphRingPrefab;

        [Tooltip("Where ground decals are parented: the Decals object in this scene. Optional — " +
                 "they go to the scene root without it, which is untidy rather than wrong.")]
        [SerializeField] private Transform _decalRoot;

        [Tooltip("The one zone prefab (CC §6.4). Every zone is this body at the radius its own " +
                 "event carries, so a 3.5 m Consecrate and a 6 m anything share it.")]
        [SerializeField] private ZoneView _zonePrefab;

        [Tooltip("The one shield-slam ring prefab (GD §9.2). Every ring is this body expanding at " +
                 "the speed its own event carries, so a retuned Warden needs no second prefab.")]
        [SerializeField] private ShockwaveView _shockwavePrefab;

        [Tooltip("The one fissure prefab (GD §9.2). Both of a crack's states — the arm and the " +
                 "bite — are this body, told apart by shape rather than by brightness.")]
        [SerializeField] private FissureView _fissurePrefab;

        [Tooltip("The shell a boss wears while it cannot be hurt (GD §9.1 rule 3). It hangs on the " +
                 "boss's own body rather than on a canvas, so its size follows the archetype's.")]
        [SerializeField] private BossBeatView _bossBeatPrefab;

        [Tooltip("Every arena this run may be played in, one prefab per arena id. Empty leaves " +
                 "the run in whatever the scene was dressed with, which is the M0 grey box and " +
                 "the undressed-scene iteration workflow.")]
        [SerializeField] private ArenaView[] _arenaPrefabs = Array.Empty<ArenaView>();

        [Tooltip("Where raised and parked arenas are parented. Optional — they go to the scene " +
                 "root without it, which is untidy rather than wrong.")]
        [SerializeField] private Transform _arenaRoot;

        protected override void Configure(IContainerBuilder builder)
        {
            RunInstaller.Install(builder);

            if (_playerView == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no {nameof(PlayerView)} assigned. Drag the Player " +
                    "object in this scene onto its Player View field — without it the run has no " +
                    "body to move and no position to report.");
            }

            // Guarded like the Player View and for its sentence, one layer out: the view moves the
            // capsule and the body is what the player sees of it (RS-02b rule 4).
            if (_defaultBody == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no default body assigned. Drag " +
                    "Prefabs/Player/Bodies/Knight.prefab onto its Default Body field — without it " +
                    "a class that names no body of its own is played by an invisible capsule.");
            }

            if (_chargeMotion == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no {nameof(ChargeMotion)} assigned. Drag the Player " +
                    "object in this scene onto its Charge Motion field — without it a Charge is " +
                    "decided by core, paid for on the cooldown, and never happens to the body.");
            }

            if (_enemyPrefab == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no {nameof(EnemyView)} prefab assigned. Drag " +
                    "Prefabs/Enemies/Enemy.prefab onto its Enemy Prefab field — without it core " +
                    "spawns enemies that have no body and never report a position.");
            }

            // Guarded like the enemy prefab and for its sentence, one side of the fight over: core
            // raises Wights whether or not anything can draw them (M5-04b), so a scene dressed
            // without this field is one where a quarter of a Gravecaller's kills stand an
            // *invisible* body up that fights for twenty seconds. Nothing on screen would report
            // it, and the enemies would simply start dying to nobody.
            if (_minionPrefab == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no {nameof(MinionView)} prefab assigned. Drag " +
                    "Prefabs/Minions/Wight.prefab onto its Minion Prefab field — without it a " +
                    "Gravecaller's Wights are raised, walk, and kill with no body anywhere in the " +
                    "scene (CH §3.2).");
            }

            // Guarded like the Wight prefab above and for a quieter version of its sentence: core
            // drops a decoy whether or not anything can draw one (M5-03), so a scene dressed
            // without this field is one where every Shroudstep taunts the whole arena for three
            // seconds with nothing on the floor to say why the swarm walked off. The evidence of a
            // decoy would be the arena walking the wrong way, which is what DecoySpawned's own
            // remarks have said since M5-03.
            if (_decoyPrefab == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no {nameof(DecoyView)} prefab assigned. Drag " +
                    "Prefabs/Vfx/VFX_Decoy.prefab onto its Decoy Prefab field — without it a " +
                    "Gravecaller's Shroudstep pulls every enemy in the arena towards a corpse " +
                    "nobody can see (CH §3.2).");
            }

            if (_projectilePrefab == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no {nameof(ProjectileView)} prefab assigned. Drag " +
                    "Prefabs/Projectiles/Projectile.prefab onto its Projectile Prefab field — " +
                    "without it a Spitter's bolt is a swell, a pause, and damage arriving out of " +
                    "nowhere about a second later.");
            }

            // The scene owns this object's lifetime, and VContainer does not dispose what it did
            // not construct, so registering the live component is exactly right: the container
            // injects it and destroys nothing.
            builder.RegisterComponent(_playerView);

            // The body (RS-02b rule 4). A build callback, because which body is a question about
            // the class, and the class is known only once the container exists: PendingRun and the
            // look book live at the root. It runs before any entry point starts, so the body is
            // standing and injected before RunTicker.Start publishes a thing.
            builder.RegisterBuildCallback(RaiseBody);

            // Guarded like the player view rather than treated as optional, because the tap-to-focus
            // adapter cannot be built without it and the whole run scope would fail to compose. The
            // message names the field, so the fix is obvious rather than a null deep inside
            // VContainer's resolution.
            if (_camera == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no {nameof(Camera)} assigned. Drag Main Camera in " +
                    "this scene onto its Camera field — without it a tap on the arena cannot be " +
                    "turned into a place on the ground, so tap-to-focus has nothing to resolve.");
            }

            // **Required, and it is the first screen on this scope that is** (M4-06 rule 7).
            // FirstActiveHint, TreeViewPresenter and the reticle are optional because a scene dressed
            // without them still plays; a scene dressed without this one strands the player on a dead
            // run with no way out of it, because M4-06 rule 2 took the tap away from HudPresenter and
            // nothing else in the Run scene loads the Menu. So the scope refuses to compose, the way
            // it already refuses an empty cover mask and a missing threat arrow — the "fails loudly
            // rather than silently" bargain, applied where the cost is the app rather than a hint.
            //
            // Guarded here rather than left to the component's own Start, which is the other half of
            // why: a screen that is on the prefab but not dressed onto this field is never injected,
            // so its Start would throw one frame into a run the player has already started, and the
            // run they lose is the one that would have paid.
            if (_runEndPresenter == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no {nameof(RunEndPresenter)} assigned. Drag the RunEnd "
                        + "object in this scene onto its Run End Presenter field — without it a dead "
                        + "run shows the player nothing and offers no way back to the Menu, which is "
                        + "indistinguishable from the app having hung.");
            }

            // The scene owns its lifetime, so the container injects it and destroys nothing — every
            // other RegisterComponent on this scope's bargain.
            builder.RegisterComponent(_runEndPresenter);

            // Registered only when it is there, and deliberately not guarded like the view above.
            // The body is load-bearing — without it the run has nothing to move — while the overlay
            // is a development aid that a scene is entitled not to have, and M0-19's release build
            // is a scene that effectively does not. RegisterComponent forces the injection through a
            // build callback, so Construct runs here, during this Awake, rather than whenever
            // something first resolves it.
            if (_debugOverlay != null)
            {
                builder.RegisterComponent(_debugOverlay);
            }

            // Optional on the same terms, and the one here with the most to lose by being absent:
            // without it nothing says how much health is left. **What its absence no longer costs is
            // the way out of a death** — that moved to the required screen above at M4-06, which is
            // what makes this field's optionality honest rather than a hole. It stays optional
            // because pressing Play in an undressed Run scene is the iteration workflow every other
            // optional field on this scope exists to protect.
            if (_hudPresenter != null)
            {
                builder.RegisterComponent(_hudPresenter);
            }

            // The other three readouts on Hud.prefab (M3-10b), all optional on the HUD's terms and
            // all with the same shape: live components in this scene, so the container injects them
            // and destroys nothing. Their *absence* differs though, and the difference is worth the
            // three lines — the strip costs a progress bar, the toast costs an announcement, and the
            // row costs the legibility of the whole default build, because every skill starts on
            // Auto (CC §6.1) and nothing else in the game draws an auto-cast cooldown.
            //
            // Each one throws from its own Start if it is on the prefab and not dressed here, which
            // is SkillBarPresenter's bargain: an undressed field is a silent readout rather than a
            // loud one, so the component says so itself and names the field to drag.
            if (_xpBar != null)
            {
                builder.RegisterComponent(_xpBar);
            }

            if (_autoCastRow != null)
            {
                builder.RegisterComponent(_autoCastRow);
            }

            if (_overflowToast != null)
            {
                builder.RegisterComponent(_overflowToast);
            }

            // Optional on the HUD's terms — the undressed Run scene is the fastest iteration loop
            // in the project and every optional field on this scope exists to protect it — but the
            // cost of its absence is deliberately written down here, because it is not the HUD's
            // cost and a later reader would assume it was.
            //
            // The gate is RunTicker's, not this screen's (M3-08a rule 12, and the owner's ruling at
            // M3-08b): the run stops whenever core has an offer on the table, whether or not
            // anything is drawing it. So a scene dressed without this presenter does not play "the
            // same without a level-up screen" — on the first pick it stops dead, shows nothing, and
            // the only way out is to leave the scene.
            //
            // It is still optional rather than guarded, and that is a statement about *when* rather
            // than about the risk: `IsLevelUpPending` requires a tree, nothing in Data/Trees ships
            // until M3-12, so no run in the current build can reach that state at all. M3-12 is the
            // task that should turn this into a MissingReferenceException beside the threat arrows',
            // because that is the task that makes the failure reachable.
            if (_levelUpPresenter != null)
            {
                builder.RegisterComponent(_levelUpPresenter);
            }

            // CH §5.4's moment (M5-07a-ii). The level-up screen's registration exactly, including
            // the half that is not obvious: the gate is RunTicker's rather than this screen's, so
            // the run stops whenever core has the moment open whether or not anything is drawing it
            // — and a scene dressed without this presenter does not play "the same without a splash
            // screen". On the sixth node it stops dead, shows nothing, and **there is no way past
            // it at all**, because CH §5.4's choice is mandatory and nothing else can answer it.
            //
            // It is still optional rather than guarded, and that is the same statement about *when*
            // the level-up screen's line makes: this is the undressed-Run-scene workflow every
            // optional field on this scope protects, and M0-19's release build. **Unlike the
            // level-up's, the failure it guards is reachable today** — both shipped classes have a
            // tree — which is why the tooltip says so and why M5-08's checklist walks a run to six
            // nodes.
            if (_splashPresenter != null)
            {
                builder.RegisterComponent(_splashPresenter);
            }

            // GD §13.3's shop (M6-03a). The splash's registration exactly, and its cost is sharper:
            // RunTicker.SanctumPhase pauses the run whenever core has the shop open, and the Leave
            // button on this screen is the only thing in the build that sends LeaveSanctum — the
            // debug overlay's door stand-in went with this task. So a scene dressed without it stops
            // dead at the first stage it clears. Optional anyway, for the undressed-Run-scene
            // workflow every optional field here protects.
            if (_sanctumPresenter != null)
            {
                builder.RegisterComponent(_sanctumPresenter);
            }

            // Optional, and — unlike the level-up screen directly above — its absence really does
            // cost only what it looks like it costs. This screen holds both halves of its own pause
            // (M3-09a rule 2), so nothing raises a Menu pause that this object is not there to
            // lower: a scene dressed without it plays the same fight and simply cannot be stopped
            // from inside. That is the undressed-Run-scene workflow every optional field here
            // protects, and it is also the M0-19 release build, which has no pause icon either way
            // until M8-03 gives the panel something else to hold.
            if (_pausePresenter != null)
            {
                builder.RegisterComponent(_pausePresenter);
            }

            // Optional, and its absence costs exactly what it looks like: this screen holds no
            // pause at all (M3-09b rule 10), lists what the player owns and sends two commands the
            // run is perfectly playable without. A scene dressed without it has a pause panel that
            // takes its own Skills button off rather than offering a dead one — PausePresenter.Start
            // does that, which is why the link between the two lives in Run.unity rather than here:
            // they are separate root prefabs, so a serialized cross-prefab reference has to be
            // dressed in the scene either way.
            //
            // It stays optional rather than guarded for the same reason the level-up screen did
            // until M3-12: nothing in Data/Trees ships an Active yet, so there is no run in the
            // current build whose Skills list would have a single row in it.
            if (_skillsPresenter != null)
            {
                builder.RegisterComponent(_skillsPresenter);
            }

            // Optional, and its absence costs exactly what it looks like: this screen sends nothing
            // at all (M3-09d rule 1), holds no pause (rule 5) and renders no events (rule 10), so a
            // scene dressed without it plays the same fight and neither door offers its button —
            // both take it off rather than leaving a dead one, which is PausePresenter.Start's rule
            // for the Skills button applied twice.
            //
            // **It is the first screen with two doors, and both links live in Run.unity** for the
            // reason the Skills screen's does: the pause panel, the level-up screen and this are
            // three separate root prefabs, so a cross-prefab reference has to be dressed in the
            // scene either way and an optional [Inject] would stop the whole scope composing.
            //
            // It stays optional rather than guarded on the Skills screen's terms, and with a
            // sharper version of the same argument: nothing in Data/Trees ships until M3-12, so
            // `TryGetTreeFor` answers false for every run in the current build and there is no tree
            // for this screen to draw even when it is dressed.
            if (_treeViewPresenter != null)
            {
                builder.RegisterComponent(_treeViewPresenter);
            }

            // Optional on the same terms, and the one here whose absence costs the *player* the
            // least and the design the most: a run without it plays identically and never tells the
            // player that CC §6.1's switch exists. **It resolves ProfileStore by type from
            // BootScope**, the way EnemyViews resolves EnemyLookBook a few lines below — a profile
            // outlives a run, and a store registered here would forget the flag between the
            // level-up that spent it and the boundary that saved it (M3-09c rule 4). That is also
            // the one dependency of this component whose absence fails loudly rather than silently:
            // a run scope built against a container with no ProfileStore does not compose at all.
            if (_firstActiveHint != null)
            {
                builder.RegisterComponent(_firstActiveHint);
            }

            // Optional for the same reason and on the same terms: a scene dressed without a
            // reticle plays, it just cannot show what the gun is aimed at, and a test scene is
            // entitled to be that.
            if (_reticle != null)
            {
                builder.RegisterComponent(_reticle);
            }

            // Optional on the same terms, and for a slightly stronger reason: the ramp is core's
            // and runs whether or not anything draws it, so a scene without a glow plays at
            // exactly the same speed — it just cannot show the player why.
            if (_focusGlow != null)
            {
                builder.RegisterComponent(_focusGlow);
            }

            // Optional on the same terms as the two above, and for the glow's reason: the grant is
            // core's and absorbs the same damage whether or not anything draws it, so a scene
            // without a shell plays the identical fight — the player just cannot see why they
            // survived the bolt.
            if (_bulwark != null)
            {
                builder.RegisterComponent(_bulwark);
            }

            // Optional, and the odd one out among these: it is not a decoration but an *input*, so
            // a scene without it is one the Charge can only be pressed on with a keyboard. That is
            // exactly the Editor iteration workflow, which is why it is allowed to be missing —
            // and why the phone build having one is an M1-16 manual step rather than a compile-time
            // guarantee.
            if (_skillButton != null)
            {
                builder.RegisterComponent(_skillButton);
            }

            // Optional on the Charge button's terms and for the same reason — it is an *input*
            // rather than a decoration, so a scene without it is one where a Manual skill has no way
            // to be cast at all. It stays optional rather than guarded because the undressed Run
            // scene is the fastest iteration loop in the project, and because nothing in
            // Data/Trees ships an Active until M3-12: every run in the current build has four empty
            // slots, so this object's absence and its presence look identical on screen.
            //
            // Registered here rather than in RunInstaller for every other component's reason: it is
            // a live object in this scene, and the container injects it and destroys nothing.
            if (_skillBar != null)
            {
                builder.RegisterComponent(_skillBar);
            }

            // Types, not instances, so the scope disposes them — the adapter owns a generated
            // actions asset that must be destroyed with the run (M0-14), and EnemyViews owns two
            // subscriptions and every body standing in the arena.
            builder.Register<InputAdapter>(Lifetime.Scoped);

            // The arenas, and the one registration on this scope whose absence would be silent: the
            // builder resolves it by type, so a run composed without it would report no door and no
            // spawn points and simply never leave stage 1.
            //
            // Registered here rather than in RunInstaller, which is where the spec put it. Two of
            // its five arguments are references to *this scene* — the prefab list and the root the
            // bodies are parented under — and the static installer is deliberately the half a
            // headless test can build. EnemyViews and ProjectileViews are here for exactly that
            // reason, and this is the same shape as both.
            builder.Register<ArenaPool>(Lifetime.Scoped)
                .WithParameter("prefabs", (IReadOnlyList<ArenaView>)_arenaPrefabs)
                .WithParameter("parent", _arenaRoot);

            // The gate parameter is gone: where the door is became a question about whichever arena
            // is standing, so the builder takes the pool above and asks it every frame (M2-11a).
            builder.Register<SnapshotBuilder>(Lifetime.Scoped);

            // By name, like the enemy prefab below: WithParameter<Camera> would be the same kind of
            // fragile type match, and this adapter's other two arguments are already resolved.
            builder.Register<TapToFocusAdapter>(Lifetime.Scoped)
                .WithParameter("camera", _camera);

            // The scene references go by name rather than by type: WithParameter<Transform> would
            // break the moment a second Transform parameter appeared, and the enemy prefab is an
            // EnemyView, which is also what the container would hand a plain type match. The
            // prewarm goes by name for the same reason an int always does here.
            //
            // Prewarmed to the arena's steady-state population, so every Instantiate a run will
            // ever do happens while the scene is still loading. One more than the quota, because a
            // corpse holds its body for the 0.6 s of its dissolve while the replacement is already
            // being rented — without the spare, every single kill would instantiate.
            //
            // The look book is the one argument here with no WithParameter and that is deliberate:
            // it is registered at the root by BootInstaller, built from the same definitions the
            // catalog is, and resolving it by type from the parent scope is what stops the run
            // owning a second copy of what an archetype looks like (M2-06). It is also the one
            // argument whose absence fails loudly — a run scope built against a container with no
            // EnemyLookBook does not compose at all.
            builder.Register<EnemyViews>(Lifetime.Scoped)
                .WithParameter("prefab", _enemyPrefab)
                .WithParameter("parent", _enemyParent)
                .WithParameter("prewarm", PrewarmCount());

            // The army (M5-05a). The census above's registration exactly, for its reasons — two of
            // its arguments are references to this scene, and the static installer is deliberately
            // the half a headless test can build.
            //
            // Prewarmed to MinionSystem.MaxConcurrent, which is the whole of what can ever stand at
            // once whatever a Legion node says, so the pool cannot be asked for a body it does not
            // already hold. Sized from the constant rather than a literal, so the two cannot
            // disagree the day the ceiling moves — and flat rather than conditional on the arena
            // being dressed, unlike PrewarmCount below, because eight bodies cost a fraction of the
            // enemy pool and the moment a raise happens is the moment a hitch cannot be afforded:
            // Rise puts one behind every fourth kill (M5-04b).
            builder.Register<MinionViews>(Lifetime.Scoped)
                .WithParameter("prefab", _minionPrefab)
                .WithParameter("parent", _minionParent)
                .WithParameter("prewarm", MinionSystem.MaxConcurrent);

            // The corpses (M5-05b). The army's registration exactly, for its reasons — two of its
            // arguments are references to this scene, and the static installer is deliberately the
            // half a headless test can build.
            //
            // Prewarmed to LureSystem.Capacity, which is two and is never a third: the Shroudstep's
            // cooldown is 2.5 s against a decoy's 3, so two can legitimately overlap for half a
            // second and nothing in the design lets a player hold more. Sized from the constant so
            // the pool and core's own ceiling cannot disagree — and parented under the decal root
            // rather than the arena, because an arena is torn down and raised again at every stage
            // boundary (M2-11a) and a corpse parented to one would be destroyed mid-life by a swap
            // it has nothing to do with.
            builder.Register<DecoyViews>(Lifetime.Scoped)
                .WithParameter("prefab", _decoyPrefab)
                .WithParameter("parent", _decalRoot)
                .WithParameter("prewarm", LureSystem.Capacity);

            // The same three-argument shape as the census above, and registered here rather than in
            // RunInstaller for the reason EnemyViews is: two of its arguments are references to
            // this scene, and the static installer is deliberately the half a headless test can
            // build.
            //
            // Prewarmed to core's own projectile capacity — the most shots that may be in the air
            // at once, so the pool cannot be asked for a body it does not already hold and every
            // Instantiate a run will ever do happens while the scene is loading. Flat rather than
            // conditional on the arena being dressed, unlike PrewarmCount below: thirty-two small
            // bodies with no controller and no collider cost a fraction of one enemy, and a run
            // that starts firing is exactly the moment a hitch cannot be afforded.
            builder.Register<ProjectileViews>(Lifetime.Scoped)
                .WithParameter("prefab", _projectilePrefab)
                .WithParameter("parent", _projectileParent)
                .WithParameter("prewarm", BootInstaller.ProjectileCapacity);

            // Sized to the snapshot's capacity rather than to the quota above: the cache is keyed
            // by enemy id and evicts only what stopped asking, so a table smaller than the arena
            // would thrash on exactly the frames that are already the most expensive.
            // All three arguments are passed, including the two the constructor has defaults for:
            // VContainer resolves every parameter from the container or a WithParameter and never
            // falls back to a C# default, so an omitted one fails to compose the run — as an
            // omitted maxRefreshesPerFrame did the moment M2-05 added it, with the PlayMode smoke
            // test as the only thing that noticed.
            builder.Register<NavPathSense>(Lifetime.Scoped)
                .WithParameter("capacity", BootInstaller.SnapshotEnemyCapacity)
                .WithParameter("refreshHz", NavPathSense.DefaultRefreshHz)
                .WithParameter("maxRefreshesPerFrame", PathRefreshBudget.DefaultMaxPerFrame);

            // Guarded here as well as in the sense's own constructor, for the reason the enemy
            // layer below is guarded twice: the constructor can only say "this mask is empty", and
            // this can say which field on which object to fix. An empty cover mask is not a
            // degraded run — it is the game M2-11a shipped, where a pillar is scenery and a Spitter
            // shoots through it, and the failure is completely silent.
            if (_coverLayer.value == 0)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no Cover Layer set. Choose the " +
                    $"'{ArenaView.CoverLayerName}' layer on its Cover Layer field — a Spitter " +
                    "raycasts that mask before it winds up, so an empty one means cover blocks " +
                    "nothing (GD §7.2).");
            }

            // The cover raycasts (M2-11b). Sized to the snapshot's capacity for NavPathSense's
            // reason — the cache is keyed by enemy id and evicts only what stopped asking — and
            // given a budget of its own rather than sharing the path cache's: the two spend their
            // allowances on different frames and a shared counter would let a busy frame of
            // pathfinding silently switch cover off.
            //
            // The cadence is written once and read twice, which is deliberate. The budget is sized
            // for the rate the cache is kept at, and the two disagreeing is a fault nothing would
            // report: the sense would simply fall behind its own cadence.
            builder.Register<LineOfSightSense>(Lifetime.Scoped)
                .WithParameter("capacity", BootInstaller.SnapshotEnemyCapacity)
                .WithParameter("cover", _coverLayer)
                .WithParameter(
                    "budget",
                    new PathRefreshBudget(
                        LineOfSightSense.DefaultRefreshHz,
                        PathRefreshBudget.DefaultMaxPerFrame))
                .WithParameter("refreshHz", LineOfSightSense.DefaultRefreshHz);

            // Guarded here as well as in the query's own constructor, because the two failures read
            // differently: the constructor can only say "this mask is empty", while this can say
            // which field on which object to fix. An empty mask is not a degraded run — it is a
            // Censer that swings three times a second and never touches anything.
            if (_enemyLayer.value == 0)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no Enemy Layer set. Choose the Enemy layer on its " +
                    "Enemy Layer field — a swing sweeps that mask, so an empty one means no " +
                    "attack in the game can ever hit anything.");
            }

            // By name like the two above: WithParameter<int> would break the moment a second int
            // parameter appeared, and the mask is a LayerMask, which is what a plain type match
            // would hand any other LayerMask argument as well.
            builder.Register<ConeOverlapQuery>(Lifetime.Scoped)
                .WithParameter("capacity", ConeOverlapQuery.DefaultCapacity)
                .WithParameter("enemyLayer", _enemyLayer);

            // The same mask, from the same field, handed to the dash's sweep. Passed rather than
            // registered as a LayerMask of its own: a bare mask in the container would be resolved
            // by type, and the first task that needs a second one — M1-19's walls — would silently
            // hand the wrong layers to whichever of the two asked first. Registered here and not
            // above because the guard the mask has to pass is a few lines up.
            builder.RegisterComponent(_chargeMotion)
                .WithParameter("enemyLayer", _enemyLayer);

            // An instance, and safe to be one — the M0-12 rule is about things the scope must
            // dispose, and a SpawnPlan is immutable, holds no resource and is not IDisposable, as
            // ContentCatalog already is at the root.
            builder.RegisterInstance(BuildSpawnPlan());

            // The one entry point in the run that gameplay knows nothing about: it reads four
            // events and buzzes a phone (M1-20). Scoped, so a run's subscriptions die with it.
            //
            // The clock is passed by name, like every other parameter on this scope, and passed at
            // all for the reason NavPathSense's refresh rate is: VContainer resolves every argument
            // from the container or a WithParameter and never falls back to a C# default. Cast to
            // Func<float> so the by-name (string, object) overload is the one that binds — the
            // generic overload takes a factory of IObjectResolver, which this is not.
            builder.RegisterEntryPoint<HapticsListener>(Lifetime.Scoped)
                .WithParameter("clock", (Func<float>)(() => Time.realtimeSinceStartup));

            // The screen-edge arrows (M2-12a). Registered here rather than in RunInstaller, which
            // is where the spec put it, for the reason ArenaPool and the two view censuses are
            // here: four of its arguments are references to *this scene* — the arrow prefab, the
            // HUD root they hang under, the camera and the player's body — and the static installer
            // is deliberately the half a headless test can build. There is no version of this
            // registration that fits there.
            //
            // Required rather than optional like the reticle and the glow, and the reason is that
            // this one is not a decoration: GD §12.4's on-screen rule is an invariant — "no damage
            // originates from outside the camera frustum without a visible edge indicator" — and a
            // Spitter has been able to break it since M2-07b. A run composed without arrows is a
            // run that is unfair in a way nothing on screen would report, so the scope refuses it
            // the way it already refuses an empty cover mask.
            if (_threatArrowPrefab == null || _threatArrowRoot == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no threat arrow prefab or arrow root assigned. Drag " +
                    "Prefabs/UI/ThreatArrow.prefab onto its Threat Arrow Prefab field and the " +
                    "HUD's Arrows object onto its Threat Arrow Root field — without them an enemy " +
                    "can damage the player from outside the camera frustum with nothing on screen " +
                    "to say where it is (GD §12.4).");
            }

            // The clock is a wall clock and is passed the same way HapticsListener's is, cast to
            // Func<float> so the by-name (string, object) overload binds rather than the generic
            // one, which takes a factory of IObjectResolver. GD §7.3's eight seconds are a cosmetic
            // view timer — AR §18.2's named exception to snapshot.Dt — so a wall clock is the right
            // clock here rather than a shortcut.
            //
            // AsSelf() alongside the entry point, because DebugOverlay resolves the concrete type
            // for its arrow count: RegisterEntryPoint alone registers only the ILateTickable it is
            // driven through, and a Resolve<ThreatArrows> on a container that plainly holds one
            // would fail.
            builder.RegisterEntryPoint<ThreatArrows>(Lifetime.Scoped)
                .AsSelf()
                .WithParameter("arrowPrefab", _threatArrowPrefab)
                .WithParameter("parent", _threatArrowRoot)
                .WithParameter("camera", _camera)
                .WithParameter("clock", (Func<float>)(() => Time.realtimeSinceStartup))
                .WithParameter("prewarm", ThreatArrowPrewarm);

            // The ground rings (M2-12b). Registered here rather than in RunInstaller, which is where
            // the spec put it, for the reason ArenaPool, the two view censuses and the arrows above
            // are here: two of its arguments are references to *this scene*, and the static installer
            // is deliberately the half a headless test can build.
            //
            // Required rather than optional like the reticle and the glow, on the arrows' argument
            // rather than on theirs: GD §9.1 rule 1 — everything is telegraphed — is an invariant,
            // and this is the only thing in the game that draws a spawn telegraph. A run composed
            // without rings is a run where bodies appear out of nowhere, which is unfair in a way
            // nothing on screen would report.
            //
            // The second half of the guard is the one that would otherwise be silent. A prefab whose
            // quad was never dragged into its field rents, binds, steps and returns perfectly and
            // draws nothing at all, so the run looks exactly like the one before this task existed.
            if (_telegraphRingPrefab == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no {nameof(TelegraphRingView)} prefab assigned. Drag " +
                    "Prefabs/Vfx/VFX_TelegraphRing.prefab onto its Telegraph Ring Prefab field — " +
                    "without it a wave's bodies appear with no ring first and a Bloater's blast has " +
                    "no circle, so neither is something the player could have read (GD §7.1, §9.1).");
            }

            if (!_telegraphRingPrefab.IsDrawable)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)}'s telegraph ring prefab has no quad assigned. Drag the " +
                    $"{nameof(MeshRenderer)} on VFX_TelegraphRing.prefab onto its own Quad field — " +
                    "without it every ring in the run is timed, sized and returned correctly and " +
                    "none of them is ever visible.");
            }

            // Prewarmed for the reason the two censuses above are, and parented under the scene's
            // decal root rather than the arena's: an arena is torn down and raised again at every
            // stage boundary (M2-11a), and a ring parented to one would be destroyed mid-life by a
            // swap it has nothing to do with.
            builder.Register<TelegraphRings>(Lifetime.Scoped)
                .WithParameter("prefab", _telegraphRingPrefab)
                .WithParameter("parent", _decalRoot)
                .WithParameter("prewarm", TelegraphRingPrewarm);

            // The zones (M3-11c). The rings' registration exactly, for the rings' reasons — two of
            // its arguments are references to *this scene*, and a decal parented to the arena would
            // be destroyed mid-life by a stage swap it has nothing to do with (M2-11a).
            //
            // Required rather than optional, and on a weaker argument than the rings': a spawn
            // telegraph is GD §9.1's invariant, while a zone the player cannot see is *only* a skill
            // that appears to do nothing. That is still the whole of what M3-11b shipped — CC §6.4's
            // Consecrate asks the player to stop moving, and ground they cannot see is ground they
            // have no reason to stand on.
            if (_zonePrefab == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no {nameof(ZoneView)} prefab assigned. Drag " +
                    "Prefabs/Vfx/VFX_ConsecrateZone.prefab onto its Zone Prefab field — without " +
                    "it a Consecrate heals exactly as much and there is nothing on the floor to " +
                    "tell the player where to stand (CC §6.4).");
            }

            // The second half of the guard is the one that would otherwise be silent: a prefab whose
            // quad was never dragged into its field places, sizes, pulses and retires perfectly and
            // draws nothing at all.
            if (!_zonePrefab.IsDrawable)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)}'s zone prefab has no quad assigned. Drag the " +
                    $"{nameof(MeshRenderer)} on VFX_ConsecrateZone.prefab onto its own Quad " +
                    "field — without it every zone in the run is placed, sized and returned " +
                    "correctly and none of them is ever visible.");
            }

            // Prewarmed to core's own zone capacity, which is ProjectileViews' argument rather than
            // the rings': eight is the most zones that can exist at once, so the pool cannot be asked
            // for a body it does not already hold and every Instantiate a run will ever do happens
            // while the scene is loading.
            builder.Register<ZoneViews>(Lifetime.Scoped)
                .WithParameter("prefab", _zonePrefab)
                .WithParameter("parent", _decalRoot)
                .WithParameter("prewarm", ZoneSystem.Capacity);

            // The boss fight's three views (M4-03). The zones' registration three times over, for
            // the zones' reasons — the arguments are references to *this scene*, and a body parented
            // to the arena would be destroyed mid-life by a stage swap it has nothing to do with
            // (M2-11a).
            //
            // Required rather than optional, and on the telegraph rings' argument rather than the
            // zones': GD §9.1 rule 1 — everything is telegraphed — is an invariant, and until this
            // task nothing in Soulvail.Game subscribed to any of M4-02's five hazard events at all.
            // A run composed without these is the build the owner playtested, where a ring takes 22
            // hit points off the player with nothing on the floor to say it was coming.
            //
            // The second half of each guard is the one that would otherwise be silent: a prefab
            // whose renderer was never dragged into its field binds, steps and returns perfectly and
            // draws nothing, so the fight looks exactly like the one before this task existed.
            RequireHazardPrefab(
                _shockwavePrefab,
                _shockwavePrefab != null && _shockwavePrefab.IsDrawable,
                nameof(ShockwaveView),
                "Prefabs/Vfx/VFX_Shockwave.prefab",
                "Shockwave Prefab",
                "a shield-slam's ring is invisible and the player is hit by ground they were never "
                    + "shown (GD §9.1 rule 2)");

            RequireHazardPrefab(
                _fissurePrefab,
                _fissurePrefab != null && _fissurePrefab.IsDrawable,
                nameof(FissureView),
                "Prefabs/Vfx/VFX_Fissure.prefab",
                "Fissure Prefab",
                "a crack arms and bites under the player's feet with no telegraph at all "
                    + "(GD §9.1 rule 1)");

            RequireHazardPrefab(
                _bossBeatPrefab,
                _bossBeatPrefab != null && _bossBeatPrefab.IsDrawable,
                nameof(BossBeatView),
                "Prefabs/Vfx/VFX_BossBeat.prefab",
                "Boss Beat Prefab",
                "a boss spends 1.5 s taking no damage with nothing on screen to say why "
                    + "(GD §9.1 rule 3)");

            // Prewarmed to core's own capacities, which is ZoneViews' argument: four rings and eight
            // cracks are the most that can exist at once, so a pool cannot be asked for a body it
            // does not already hold. One shell, because one boss is what a stage holds — the pool
            // grows past it if M7's Choirmother ever needs two, and the cost of being wrong is one
            // Instantiate rather than a refusal.
            builder.Register<BossViews>(Lifetime.Scoped)
                .WithParameter("shockwavePrefab", _shockwavePrefab)
                .WithParameter("fissurePrefab", _fissurePrefab)
                .WithParameter("beatPrefab", _bossBeatPrefab)
                .WithParameter("parent", _decalRoot)
                .WithParameter("shockwavePrewarm", ShockwaveSystem.Capacity)
                .WithParameter("fissurePrewarm", FissureSystem.Capacity)
                .WithParameter("beatPrewarm", 1);

            // Scoped rather than the default Singleton. Inside a child scope the two behave
            // identically — a singleton registered here still resolves and disposes scope-locally
            // — so the only thing the label can do is tell the truth about the lifetime, and this
            // object's lifetime is one run.
            builder.RegisterEntryPoint<RunTicker>(Lifetime.Scoped);
        }

        /// <summary>
        /// Raises the body the run's class names under the player, and injects it (RS-02b rule 4).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The class is <see cref="RunCharacter.Choose"/>'s</b>, the call <c>RunTicker.Start</c>
        /// makes a moment later, so the body worn and the class started are one answer (rule 3). A
        /// class whose look names no body wears <see cref="_defaultBody"/>.
        /// </para>
        /// <para>
        /// <b>At identity under the <see cref="PlayerView"/>: its position and rotation, not its
        /// scale.</b> A body's scale is the model's own — 0.66 for KayKit's Rig_Medium at the
        /// capsule's height — and rides on the prefab's root, which is where the Knight carried it
        /// when it was built into <c>Player.prefab</c>. Named after its prefab rather than left as a
        /// clone, so the hierarchy says which body stands there.
        /// </para>
        /// <para>
        /// <b>Injected with <c>InjectGameObject</c></b>, which is what reaches the animator view's
        /// <c>Construct</c>: a body is not a registration, so nothing else in the container would.
        /// </para>
        /// </remarks>
        private void RaiseBody(IObjectResolver container)
        {
            ContentId characterId = RunCharacter.Choose(
                container.Resolve<PendingRun>(),
                container.Resolve<ContentCatalog>());

            GameObject prefab = container.Resolve<CharacterLookBook>().For(characterId).Body;

            // Unity's ==: a body prefab deleted from the project is a live reference only the
            // engine's operator calls null.
            if (prefab == null)
            {
                prefab = _defaultBody;
            }

            GameObject body = Instantiate(prefab, _playerView.transform, false);

            body.name = prefab.name;
            body.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            container.InjectGameObject(body);
        }

        /// <summary>
        /// Refuses a boss-hazard prefab that is missing or undressed, naming the field to drag and
        /// what the run costs without it.
        /// </summary>
        /// <param name="prefab">The serialized reference, which may be null or destroyed.</param>
        /// <param name="isDrawable">
        /// Whether the prefab's own renderer field is filled. Read at the call site rather than here,
        /// because the three views share no base type and a common interface for one bool would be a
        /// seam invented for a guard.
        /// </param>
        /// <param name="typeName">The component the field holds, for the message.</param>
        /// <param name="assetPath">Where the prefab lives, so the fix is a drag rather than a hunt.</param>
        /// <param name="fieldName">The field's inspector label.</param>
        /// <param name="cost">What a run without it is, in one clause. The half that is not obvious.</param>
        /// <remarks>
        /// One method for three prefabs rather than six blocks: the guards differ only in their
        /// nouns, and three copies of this shape is how the second one drifts from the first.
        /// </remarks>
        /// <exception cref="MissingReferenceException">Either half of the guard fails.</exception>
        private static void RequireHazardPrefab(
            Component prefab,
            bool isDrawable,
            string typeName,
            string assetPath,
            string fieldName,
            string cost)
        {
            // Unity's ==: an unassigned or destroyed prefab is a live reference that only compares
            // equal to null through the engine's operator.
            if (prefab == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no {typeName} prefab assigned. Drag {assetPath} onto "
                        + $"its {fieldName} field — without it {cost}.");
            }

            if (!isDrawable)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)}'s {typeName} prefab has no renderer assigned. Drag the "
                        + $"renderer on {assetPath} onto its own field — without it every one of "
                        + "them in the run is timed, sized and returned correctly and none of them "
                        + "is ever visible.");
            }
        }

        /// <summary>
        /// Turns the serialized dummy fields into the plan core is started with.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Built here rather than in <c>RunInstaller</c> because it is scene data: which dummies
        /// stand where is a property of this arena, and the static installer is deliberately the
        /// half a headless test can build.
        /// </para>
        /// <para>
        /// An unassigned archetype or an empty position list yields <see cref="SpawnPlan.Empty"/>
        /// rather than a throw. That is the same bargain <c>RunInstaller</c> makes with the seed
        /// and <c>RunTicker</c> with the class (M0-12 rule 6): pressing Play in a Run scene that
        /// has not been dressed yet is a workflow, not a mistake. It is silent rather than
        /// warning, unlike the seed, because an empty arena is visible on screen the instant the
        /// run starts — nothing is standing in it.
        /// </para>
        /// <para>
        /// There is no respawn policy any more. M2-05 made the director the only spawner and set
        /// <c>Keep Alive</c> to zero; M2-10 deleted the type, because a second spawner that keeps
        /// twelve bodies breathing regardless of the wave plan is a bug waiting for someone to set
        /// the field back to 12. An arena now empties and stays empty until the next wave is due.
        /// </para>
        /// <para>
        /// <b>These positions are no longer spawn points as well.</b> Until M2-11a the same list was
        /// handed over twice and meant two things — where the scene's dummies stand, and where the
        /// director may put a wave — because a run had one room and nothing else knew any geometry.
        /// An arena prefab authors its own points now, so this is only the first of those.
        /// </para>
        /// </remarks>
        private SpawnPlan BuildSpawnPlan()
        {
            if (_dummySpec == null || _dummyPositions is null || _dummyPositions.Length == 0)
            {
                return SpawnPlan.Empty;
            }

            // Validated here, not at the first spawn: ContentId's constructor is what checks the
            // grammar, and doing it once per run in the composition root means a malformed id in
            // the asset fails while the scope is being built, naming the id, rather than one
            // frame later from inside core's spawn loop.
            var specId = new ContentId(_dummySpec.Id);

            var entries = new SpawnPlan.Entry[_dummyPositions.Length];

            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = new SpawnPlan.Entry(specId, _dummyPositions[i].ToNum());
            }

            return new SpawnPlan(entries);
        }

        /// <summary>
        /// How many bodies the enemy pool builds before the run starts.
        /// </summary>
        /// <remarks>
        /// The larger of the opening population and the device cap, plus one. The opening population
        /// can exceed the cap, because an arena is allowed to be dressed with more dummies than a
        /// wave may ever hold; and the spare covers the overlap every kill has — a corpse holds its
        /// body for the 0.6 s of its dissolve while its replacement is already being rented, so
        /// without it a steady fight would instantiate once per death and the pool would have
        /// bought nothing.
        /// <para>
        /// <b>The device cap is the number that decides.</b> The director is the only spawner, and
        /// what it may ask for is GD §12.2's concurrency bounded by that cap. Sized to the dressed
        /// dummies instead, the pool would instantiate through the whole of wave 1 — a handful of
        /// hitches at exactly the moment the first telegraph rings have to be read.
        /// </para>
        /// <para>
        /// Nothing at all when no archetype is assigned, whatever the position list says. The
        /// director still spawns into such a scene, so its pool instantiates on demand — which is
        /// why the shipped <c>Run.unity</c> dresses no dummy and keeps the archetype (M6-11c), and
        /// <c>RunSceneTests</c> holds it there.
        /// </para>
        /// </remarks>
        private int PrewarmCount()
        {
            if (_dummySpec == null)
            {
                return 0;
            }

            int dressed = _dummyPositions is null ? 0 : _dummyPositions.Length;

            return Mathf.Max(dressed, BootInstaller.DeviceEnemyCap) + 1;
        }
    }
}
