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
    /// Empty of its own registrations by design: <c>RunInstaller</c> holds every one that a
    /// headless test can build, which is all of them until M0-16 adds the pieces that need
    /// serialized scene references — <c>SnapshotBuilder</c>, <c>PlayerView</c>, <c>RunTicker</c>
    /// and the input adapter. Those arrive as <c>[SerializeField]</c>s on this class and are
    /// registered here, because a scene reference is the one thing a static installer cannot have.
    /// </para>
    /// </remarks>
    public sealed class RunScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder) => RunInstaller.Install(builder);
    }
}
