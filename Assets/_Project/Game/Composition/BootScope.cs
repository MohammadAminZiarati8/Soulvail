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

        [SerializeField] private ModeDefinition[] _modes;

        /// <remarks>
        /// Empty until M3-12 authors the Oathbound's nodes, and empty is a legal boot rather than a
        /// broken one: the catalog builds, the run plays, and nothing offers a node because there
        /// is no node to offer. A placeholder here would be content offered to a player two tasks
        /// before anything can offer it — or deleted two tasks later (M3-02b rule 7).
        /// </remarks>
        [SerializeField] private SkillDefinition[] _skills;

        /// <remarks>
        /// Empty until M3-12 for <see cref="_skills"/>' reason. A class with no tree is a legal
        /// catalog — <c>TryGetTreeFor</c> simply answers false — which is every catalog between
        /// here and that task.
        /// </remarks>
        [SerializeField] private SkillTreeDefinition[] _trees;

        /// <remarks>
        /// Every authored boss — <c>Data/Enemies/WardenBoss.asset</c>, which is one of them in V1
        /// (GD §9.2's other three are M7's). Empty is a legal boot in the sense that the catalog
        /// builds, and <em>not</em> in the sense that a run plays: <c>RunSession.Start</c> refuses
        /// a run whose mode names a boss this list does not hold, which is what stops the gap
        /// surfacing a hundred seconds into stage 5 (M4-02).
        /// </remarks>
        [SerializeField] private BossDefinition[] _bosses;

        /// <remarks>
        /// <c>Data/Localisation/English.asset</c> — the one language (M3-14a rule 2). Unlike the
        /// six arrays above this is a single asset and it is <b>required</b>: an empty slot here is
        /// a game in which every screen draws its own <c>LocKey</c>, so the installer refuses it by
        /// name rather than letting it become a null reference on a card.
        /// </remarks>
        [SerializeField] private LocalizationTable _localization;

        protected override void Configure(IContainerBuilder builder)
        {
            BootInstaller.Install(
                builder, _characters, _enemies, _modes, _skills, _trees, _localization, _bosses);

            // Singleton and not Scoped: one loader for the life of the app, resolved from the
            // root by whatever child scope asks for it.
            builder.Register<SceneLoader>(Lifetime.Singleton);

            builder.RegisterEntryPoint<BootFlow>();
        }
    }
}
