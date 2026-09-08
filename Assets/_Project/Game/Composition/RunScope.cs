using Soulvail.Game.Adapters;
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
    /// </para>
    /// </remarks>
    public sealed class RunScope : LifetimeScope
    {
        [SerializeField] private PlayerView _playerView;
        [SerializeField] private DebugOverlay _debugOverlay;

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
            // actions asset that must be destroyed with the run (M0-14).
            builder.Register<InputAdapter>(Lifetime.Scoped);
            builder.Register<SnapshotBuilder>(Lifetime.Scoped);

            // Scoped rather than the default Singleton. Inside a child scope the two behave
            // identically — a singleton registered here still resolves and disposes scope-locally
            // — so the only thing the label can do is tell the truth about the lifetime, and this
            // object's lifetime is one run.
            builder.RegisterEntryPoint<RunTicker>(Lifetime.Scoped);
        }
    }
}
