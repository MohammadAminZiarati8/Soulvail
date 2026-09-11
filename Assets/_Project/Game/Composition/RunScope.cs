using System;
using Soulvail.Core.Content;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
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

        [Tooltip("The archetype the dummies below are spawned as. Leave empty for an arena that " +
                 "starts bare.")]
        [SerializeField] private EnemyDefinition _dummySpec;

        [Tooltip("Where enemies stand and arrive, in world metres. They are this arena's spawn " +
                 "points from M2-05 on — the director places every wave at one of them — and " +
                 "the dummies dressed into the scene stand at the first few. M2-11 replaces " +
                 "this with points authored on the arena prefab.")]
        [SerializeField] private Vector3[] _dummyPositions;

        [Tooltip("How many enemies the arena keeps breathing. Zero from M2-05 on: the director " +
                 "is the only thing that spawns, and a respawn policy beside it would be a " +
                 "second spawner with its own opinion. Retired entirely by M2-10.")]
        [Min(0)]
        [SerializeField] private int _keepAlive;

        [Tooltip("Seconds of quiet after the last death before the arena refills. The pause the " +
                 "player reads as 'I cleared that'.")]
        [Min(0f)]
        [SerializeField] private float _respawnDelay = 2f;

        [Tooltip("Metres of clearance a respawn needs from the player (GD §12.4). Nothing should " +
                 "ever appear on top of you.")]
        [Min(0f)]
        [SerializeField] private float _minSpawnDistance = 6f;

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
        /// The respawn policy reuses the same positions, and a <c>Keep Alive</c> of zero means an
        /// arena that empties and stays empty — which is what every arena did before M1-19, what
        /// an experiment about a single Husk still wants, and what every arena means again from
        /// M2-05: the director is the only spawner in a run, and a policy refilling behind it
        /// would be a second one with its own opinion about how many enemies there should be.
        /// </para>
        /// <para>
        /// <b>The same positions are handed over twice, and they mean two different things.</b> As
        /// entries they are where the scene's dummies are standing when the player walks in; as
        /// <c>SpawnPoints</c> they are where the director may put a wave. M2-11 separates them for
        /// real, when an arena prefab authors its own spawn ring; until then the dressed positions
        /// are the only geometry anybody knows about, and an arena with no dummy archetype has
        /// neither (<c>SpawnPlan.Empty</c>), which is rule 12's inert director.
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

            var positions = new System.Numerics.Vector3[_dummyPositions.Length];

            for (int i = 0; i < entries.Length; i++)
            {
                positions[i] = _dummyPositions[i].ToNum();
                entries[i] = new SpawnPlan.Entry(specId, positions[i]);
            }

            RespawnPolicy respawn = _keepAlive > 0
                ? new RespawnPolicy(specId, positions, _keepAlive, _respawnDelay, _minSpawnDistance)
                : null;

            return new SpawnPlan(entries, respawn, positions);
        }

        /// <summary>
        /// How many bodies the enemy pool builds before the run starts.
        /// </summary>
        /// <remarks>
        /// The largest of the opening population, the respawn quota and the device cap, plus one.
        /// The quota is what the arena settles at; the opening population can exceed it, because an
        /// arena is allowed to be dressed with more dummies than it keeps alive; and the spare
        /// covers the overlap every kill has — a corpse holds its body for the 0.6 s of its
        /// dissolve while its replacement is already being rented, so without it a steady fight
        /// would instantiate once per death and the pool would have bought nothing.
        /// <para>
        /// <b>The device cap is in there from M2-05</b>, and it is now the number that decides: with
        /// the respawn quota at zero the director is the only spawner, and what it may ask for is
        /// GD §12.2's concurrency bounded by that cap. Sized to the dressed dummies instead, the
        /// pool would instantiate through the whole of wave 1 — a handful of hitches at exactly the
        /// moment the first telegraph rings have to be read.
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

            return Mathf.Max(dressed, Mathf.Max(_keepAlive, BootInstaller.DeviceEnemyCap)) + 1;
        }
    }
}
