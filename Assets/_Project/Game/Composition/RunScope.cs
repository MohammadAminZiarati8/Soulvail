using System;
using System.Collections.Generic;
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

        [Tooltip("The dash, on the Player object. Not optional, unlike the reticle and the glow: " +
                 "without it a Charge moves nothing and sweeps nobody, and it would fail silently.")]
        [SerializeField] private ChargeMotion _chargeMotion;

        [Tooltip("The Charge button on the HUD. Optional — an arena without a HUD is playable " +
                 "from a keyboard, it just cannot be dashed with a thumb.")]
        [SerializeField] private SkillButton _skillButton;

        [Tooltip("The player's row on the HUD: health, the Aegis, and the death overlay. Optional " +
                 "on the same terms as the reticle — an arena without one plays exactly the same, " +
                 "it just cannot say how the player is doing and has no way out of a death except " +
                 "leaving the scene.")]
        [SerializeField] private HudPresenter _hudPresenter;

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

        [Tooltip("Drives the character model's Animator from the fight core has already decided. " +
                 "On the Player object, and optional on the same terms as the glow: without it " +
                 "the run plays identically, the body just never changes pose.")]
        [SerializeField] private PlayerAnimatorView _playerAnimator;

        [Tooltip("The one enemy body prefab. Every archetype shares it until M2-06 gives them " +
                 "silhouettes of their own.")]
        [SerializeField] private EnemyView _enemyPrefab;

        [Tooltip("Where spawned enemy bodies are parented. Optional — they go to the scene root " +
                 "without it, which is untidy rather than wrong.")]
        [SerializeField] private Transform _enemyParent;

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

        [Tooltip("The archetype the dummies below are spawned as. Leave empty for an arena that " +
                 "starts bare.")]
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
            // without it nothing says how much health is left, and a death leaves an arena that has
            // stopped ticking with no tap back to the menu. It stays optional anyway, because
            // pressing Play in an undressed Run scene is the iteration workflow every other
            // optional field on this scope exists to protect.
            if (_hudPresenter != null)
            {
                builder.RegisterComponent(_hudPresenter);
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

            // Optional on the same terms again. Animation is a pure consequence here — it reads
            // combat events and the body's speed, and publishes nothing back — so an arena with a
            // grey capsule instead of a character plays exactly the same fight.
            if (_playerAnimator != null)
            {
                builder.RegisterComponent(_playerAnimator);
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

            // Scoped rather than the default Singleton. Inside a child scope the two behave
            // identically — a singleton registered here still resolves and disposes scope-locally
            // — so the only thing the label can do is tell the truth about the lifetime, and this
            // object's lifetime is one run.
            builder.RegisterEntryPoint<RunTicker>(Lifetime.Scoped);
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
        /// Nothing at all for an arena with no archetype dressed into it. That scene's plan is
        /// <see cref="SpawnPlan.Empty"/>, so core will never ask for a body — and building twelve
        /// of them anyway would make an undressed Run scene, the fastest iteration loop in the
        /// project, the slowest one to enter.
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
