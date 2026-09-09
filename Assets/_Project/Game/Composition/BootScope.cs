using Soulvail.Game.Authoring;
using UnityEngine;
using VContainer;
using VContainer.Unity;

// Block namespace, deliberately, against the project's file-scoped convention: Unity 6.3's
// script importer parses a file to find the type it declares, and its parser does not
// understand `namespace X;`. A MonoBehaviour declared that way compiles, but Unity never links a
// MonoScript to it — the prefab below would serialise as `m_Script: {fileID: 0}` and load as
// null, with nothing reporting an error. Verified A/B in one compile cycle (M0-11).
namespace Soulvail.Game.Composition
{
    /// <summary>
    /// The root of the container: everything the app owns for its whole life. Set as
    /// <c>RootLifetimeScope</c> in <c>VContainerSettings</c>, so VContainer instantiates this
    /// prefab before the first scene loads and keeps it under <c>DontDestroyOnLoad</c>.
    /// See AR §7 and ADR-0002.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Being the settings' root is what makes "press Play in any scene" work: a scene
    /// <see cref="LifetimeScope"/> with no parent reference asks the settings for one, which
    /// creates this scope on demand. Nothing in a scene has to reference it, and no scene has to
    /// be entered through another.
    /// </para>
    /// <para>
    /// The registrations themselves live in <c>BootInstaller</c>, not here. This class is the
    /// Unity-side shell that owns the serialized content references and forwards them; the
    /// installer is the part an EditMode test builds a real container from (M0-12). What is added
    /// here is only what cannot exist without the engine: <see cref="SceneLoader"/>, which wraps
    /// <c>SceneManager</c>, and <see cref="BootFlow"/>, which needs VContainer's entry-point
    /// dispatcher to be run at all.
    /// </para>
    /// <para>
    /// This prefab is the only place in the project that names content assets for the container.
    /// A class or an enemy archetype that is not in one of these arrays does not exist as far as a
    /// run is concerned — a spawn plan naming it fails at <c>Start</c> as missing content.
    /// </para>
    /// </remarks>
    public sealed class BootScope : LifetimeScope
    {
        [SerializeField] private CharacterDefinition[] _characters;

        [SerializeField] private EnemyDefinition[] _enemies;

        protected override void Configure(IContainerBuilder builder)
        {
            BootInstaller.Install(builder, _characters, _enemies);

            // Singleton and not Scoped: one loader for the life of the app, resolved from the
            // root by whatever child scope asks for it.
            builder.Register<SceneLoader>(Lifetime.Singleton);

            builder.RegisterEntryPoint<BootFlow>();
        }
    }
}
