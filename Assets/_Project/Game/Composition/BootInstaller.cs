using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Game.Authoring;
using VContainer;

namespace Soulvail.Game.Composition;

/// <summary>
/// Everything the app owns for its whole life: the content catalog, and the slot the menu
/// writes the next run into. Scene-free and static so a test can build the real container and
/// resolve from it — a wiring mistake fails in the Test Runner rather than on a phone. See AR §7
/// and ADR-0002.
/// </summary>
/// <remarks>
/// <para>
/// Static, but not state: this class holds nothing. It is a function from a builder to a set of
/// registrations, which is what lets <c>BootScope</c> (M0-13) and an EditMode test install the
/// same wiring without sharing a MonoBehaviour.
/// </para>
/// <para>
/// The catalog is built here, eagerly, rather than registered as a factory. A broken asset is
/// then a loud failure at boot naming the file, which is what <c>ToSpec</c>'s message was
/// written for (M0-11); deferred to the first resolve it would surface mid-run, one scene later,
/// pointing at whoever asked for content rather than at the asset that is wrong.
/// </para>
/// </remarks>
public static class BootInstaller
{
    /// <summary>
    /// How many enemies a <c>WorldSnapshot</c> can carry — the concurrency cap core is allowed
    /// to see at once. A constant here, a <c>TuningConfig</c> field once M8-03's device tiering
    /// has an opinion about it.
    /// </summary>
    public const int SnapshotEnemyCapacity = 64;

    /// <param name="builder">The root container being built.</param>
    /// <param name="definitions">
    /// Every authored character. Converted immediately; the list is not retained.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="definitions"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A definition is an empty slot, or is not valid content. Thrown from here rather than
    /// swallowed: a catalog missing a class would fail later as a missing content id, which
    /// names the wrong culprit.
    /// </exception>
    public static void Install(IContainerBuilder builder, IReadOnlyList<CharacterDefinition> definitions)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (definitions is null)
        {
            throw new ArgumentNullException(nameof(definitions));
        }

        builder.RegisterInstance(BuildCatalog(definitions));

        // Singleton, and deliberately not Scoped: the menu sets it in one scene and the run
        // scope reads it in the next, so it has to outlive both.
        builder.Register<PendingRun>(Lifetime.Singleton);
    }

    private static ContentCatalog BuildCatalog(IReadOnlyList<CharacterDefinition> definitions)
    {
        var specs = new CharacterSpec[definitions.Count];

        for (int i = 0; i < definitions.Count; i++)
        {
            CharacterDefinition definition = definitions[i];

            // Unity's == rather than `is null`: an empty element of a serialized array is a real
            // null, but a destroyed asset is a live reference that only compares equal to null
            // through the engine's operator. Both are the same mistake to the person reading the
            // Console, so both are caught the same way. Without this, ToSpec on an empty slot
            // throws a NullReferenceException from inside a for-loop with nothing to point at.
            if (definition == null)
            {
                throw new ArgumentException(
                    $"definitions[{i}] is an empty slot. Every character in the boot list must " +
                    "reference a CharacterDefinition asset.",
                    nameof(definitions));
            }

            // Each failure already names its asset (M0-11), so nothing is caught or rewrapped
            // here — a second layer of message would bury the file name that matters.
            specs[i] = definition.ToSpec();
        }

        return new ContentCatalog(specs);
    }
}
