using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Composition;

/// <summary>
/// Everything the app owns for its whole life: the content catalog — characters and enemy
/// archetypes — and the slot the menu writes the next run into. Scene-free and static so a test
/// can build the real container and resolve from it: a wiring mistake fails in the Test Runner
/// rather than on a phone. See AR §7 and ADR-0002.
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
    /// <param name="characters">
    /// Every authored character. Converted immediately; the list is not retained.
    /// </param>
    /// <param name="enemies">
    /// Every authored enemy archetype, converted the same way. Required rather than optional,
    /// unlike <see cref="ContentCatalog"/>'s own parameter: this method has two call sites, and
    /// letting one of them omit its enemies silently would produce a catalog whose only symptom
    /// is an arena that never fills — the one failure a playtest cannot tell apart from a broken
    /// spawner (M1-06). An empty list is how a boot list with no enemies says so out loud.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// A definition is an empty slot, or is not valid content. Thrown from here rather than
    /// swallowed: a catalog missing a class would fail later as a missing content id, which
    /// names the wrong culprit.
    /// </exception>
    public static void Install(
        IContainerBuilder builder,
        IReadOnlyList<CharacterDefinition> characters,
        IReadOnlyList<EnemyDefinition> enemies)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (characters is null)
        {
            throw new ArgumentNullException(nameof(characters));
        }

        if (enemies is null)
        {
            throw new ArgumentNullException(nameof(enemies));
        }

        builder.RegisterInstance(new ContentCatalog(
            Convert(characters, definition => definition.ToSpec(), "character", nameof(characters)),
            Convert(enemies, definition => definition.ToSpec(), "enemy", nameof(enemies))));

        // The wall clock, at the root: it is a device the whole app shares, not something a run
        // owns — the same argument as the vibrator below, and the opposite of IRandom, which is
        // Scoped because a seed *is* a run (RunInstaller). Nothing consumes it yet; the first
        // reader is the save store's timestamp (M2-13a).
        builder.Register<UnityClock>(Lifetime.Singleton).As<IClock>();

        // Singleton, and deliberately not Scoped: the menu sets it in one scene and the run
        // scope reads it in the next, so it has to outlive both.
        builder.Register<PendingRun>(Lifetime.Singleton);

        // Haptics live at the root rather than in the run, both of them. The vibrator is one
        // device and holds one JNI handle for the app's life, and the preference has to survive
        // leaving a run — a toggle that reset itself every time the player descended would be
        // worse than none. What is scoped to a run is the listener, which RunScope registers.
        //
        // The platform decides which vibrator by compilation, not by a runtime check. !UNITY_EDITOR
        // is the load-bearing half: UNITY_ANDROID is defined in the Editor whenever the active build
        // target is Android, where UnityPlayer.currentActivity does not exist.
#if UNITY_ANDROID && !UNITY_EDITOR
        builder.Register<AndroidVibrator>(Lifetime.Singleton).As<IVibrator>();
#else
        builder.Register<NullVibrator>(Lifetime.Singleton).As<IVibrator>();
#endif

        // A factory rather than a plain type registration, so this reads the platform store rather
        // than whichever constructor VContainer would have picked — the class has none that are
        // public, exactly so that the choice between "persisted" and "in memory" has to be made out
        // loud (M1-20). PlayerPrefs is the stopgap until M2-13's ISaveStore.
        builder.Register<HapticsSettings>(_ => HapticsSettings.FromPlayerPrefs(), Lifetime.Singleton);
    }

    /// <summary>
    /// Converts one kind's authored assets into the specs core consumes, refusing an empty slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One method for every kind, taking the conversion as a delegate — the same trade
    /// <see cref="ContentCatalog"/>'s own indexer makes, for the same reason: it runs once per
    /// kind at boot, so the delegate costs nothing that matters, and the third kind inherits the
    /// guard and the message instead of a third copy of this loop.
    /// </para>
    /// <para>
    /// The null check casts to <see cref="UnityEngine.Object"/> first, and that cast is
    /// load-bearing rather than decorative. C# resolves <c>==</c> on a type parameter as
    /// reference equality — a user-defined operator on the constraint's base type is not
    /// considered — so <c>definition == null</c> inside a generic method would compile, read
    /// exactly like the non-generic version it replaced, and quietly stop catching a destroyed
    /// asset, which is a live reference that only Unity's operator calls null.
    /// </para>
    /// </remarks>
    private static TSpec[] Convert<TDefinition, TSpec>(
        IReadOnlyList<TDefinition> definitions,
        Func<TDefinition, TSpec> toSpec,
        string kind,
        string paramName)
        where TDefinition : ScriptableObject
    {
        var specs = new TSpec[definitions.Count];

        for (int i = 0; i < definitions.Count; i++)
        {
            TDefinition definition = definitions[i];

            if ((UnityEngine.Object)definition == null)
            {
                throw new ArgumentException(
                    $"{paramName}[{i}] is an empty slot. Every {kind} in the boot list must " +
                    $"reference a {typeof(TDefinition).Name} asset.",
                    paramName);
            }

            // Each failure already names its asset (M0-11), so nothing is caught or rewrapped
            // here — a second layer of message would bury the file name that matters.
            specs[i] = toSpec(definition);
        }

        return specs;
    }
}
