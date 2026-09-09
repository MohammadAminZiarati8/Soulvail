using Soulvail.Core.Content;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
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
    /// scene — the view in it, the loop that drives it, and the two adapters that sit either side
    /// of that loop. <c>SnapshotBuilder</c> and <c>InputAdapter</c> would install cleanly in the
    /// static half, but the builder needs the view and the adapter is only ever read by the
    /// ticker, so keeping the frame's four pieces in one place is worth more than the symmetry.
    /// <c>EnemyViews</c> and the run's <c>SpawnPlan</c> join them for the same reason (M1-07):
    /// both are made of references to this scene's prefab, its parent transform and the positions
    /// dressed into it.
    /// </para>
    /// </remarks>
    public sealed class RunScope : LifetimeScope
    {
        [SerializeField] private PlayerView _playerView;
        [SerializeField] private DebugOverlay _debugOverlay;

        [Tooltip("The one enemy body prefab. Every archetype shares it until M2-06 gives them " +
                 "silhouettes of their own.")]
        [SerializeField] private EnemyView _enemyPrefab;

        [Tooltip("Where spawned enemy bodies are parented. Optional — they go to the scene root " +
                 "without it, which is untidy rather than wrong.")]
        [SerializeField] private Transform _enemyParent;

        [Tooltip("The archetype the dummies below are spawned as. Leave empty for an arena that " +
                 "starts bare.")]
        [SerializeField] private EnemyDefinition _dummySpec;

        [Tooltip("Where the run's dummies stand, in world metres. M1's whole spawner: there is " +
                 "no director until M2-05, so this is how a playtest gets something to shoot at.")]
        [SerializeField] private Vector3[] _dummyPositions;

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

            if (_enemyPrefab == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunScope)} has no {nameof(EnemyView)} prefab assigned. Drag " +
                    "Prefabs/Enemies/Enemy.prefab onto its Enemy Prefab field — without it core " +
                    "spawns enemies that have no body and never report a position.");
            }

            // The scene owns this object's lifetime, and VContainer does not dispose what it did
            // not construct, so registering the live component is exactly right: the container
            // injects it and destroys nothing.
            builder.RegisterComponent(_playerView);

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

            // Types, not instances, so the scope disposes them — the adapter owns a generated
            // actions asset that must be destroyed with the run (M0-14), and EnemyViews owns two
            // subscriptions and every body standing in the arena.
            builder.Register<InputAdapter>(Lifetime.Scoped);
            builder.Register<SnapshotBuilder>(Lifetime.Scoped);

            // The two scene references go by name rather than by type: WithParameter<Transform>
            // would break the moment a second Transform parameter appeared, and the enemy prefab
            // is an EnemyView, which is also what the container would hand a plain type match.
            builder.Register<EnemyViews>(Lifetime.Scoped)
                .WithParameter("prefab", _enemyPrefab)
                .WithParameter("parent", _enemyParent);

            // An instance, and safe to be one — the M0-12 rule is about things the scope must
            // dispose, and a SpawnPlan is immutable, holds no resource and is not IDisposable, as
            // ContentCatalog already is at the root.
            builder.RegisterInstance(BuildSpawnPlan());

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
    }
}
