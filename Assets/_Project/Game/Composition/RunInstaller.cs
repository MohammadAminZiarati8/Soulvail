using System;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Composition;

/// <summary>
/// Everything one run owns: the brain, its generator, and the two channels either side of it.
/// Installed into a child scope, so disposing that scope ends the run and takes every
/// subscription with it — the answer to "what owns run state" (ADR-0002). See AR §7.
/// </summary>
/// <remarks>
/// <para>
/// Nothing scene-related is registered here. <c>PlayerView</c>, <c>SnapshotBuilder</c>,
/// <c>RunTicker</c> and <c>InputAdapter</c> need serialized scene references, so <c>RunScope</c>
/// registers them itself in M0-16. What is left is exactly the part a headless test can build,
/// which is the point of the split.
/// </para>
/// <para>
/// Every registration is <see cref="Lifetime.Scoped"/>, and each is registered as a <em>type</em>
/// rather than as a pre-built instance. That is load-bearing for rule 8, not a style choice:
/// VContainer only adds an object to a scope's disposal list when the container constructed it,
/// and explicitly skips instances handed to <c>RegisterInstance</c> (they are assumed to be owned
/// by whoever created them). Registering the hub as an instance would compile, resolve, and
/// silently never be disposed — every run leaking every subscription from the run before it.
/// </para>
/// </remarks>
public static class RunInstaller
{
    /// <param name="builder">The run scope being built.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static void Install(IContainerBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        // As the port and as itself: core publishes through IDomainEvents, while presenters and
        // views subscribe through the concrete hub. AsSelf() is required — As<T>() alone leaves
        // the implementation type registered as a null guard, and Resolve<DomainEventHub>()
        // would fail on a container that plainly contains one.
        builder.Register<DomainEventHub>(Lifetime.Scoped).As<IDomainEvents>().AsSelf();

        // Same shape, and the same reason it is worth having both: core holds IIntentSink and
        // can only write, a view holds IntentBuffer and can only read (M0-06).
        builder.Register<IntentBuffer>(Lifetime.Scoped).As<IIntentSink>().AsSelf();

        // A factory rather than RegisterInstance, so the capacity is read once per run scope and
        // the snapshot's lifetime is genuinely the run's. RegisterInstance is hardwired to
        // Lifetime.Singleton, which would be the one registration here that did not say what it
        // means.
        builder.Register<WorldSnapshot>(
            _ => new WorldSnapshot(BootInstaller.SnapshotEnemyCapacity),
            Lifetime.Scoped);

        builder.Register<IRandom>(CreateRandom, Lifetime.Scoped);

        builder.Register<IRunSession, RunSession>(Lifetime.Scoped);
    }

    /// <summary>
    /// Builds the run's generator from whatever the menu chose, or from the clock when nobody
    /// chose anything.
    /// </summary>
    /// <remarks>
    /// The fallback exists for one workflow: pressing Play with the Run scene already open, which
    /// is how this game gets iterated on and which no menu ran before. Throwing there would make
    /// the fastest loop in development the one that does not work, so it warns instead and the
    /// run is simply not reproducible — said out loud, because a silently unseeded run looks
    /// exactly like a broken seed. M0-13 rule 1 is the same decision for the scene, and M0-16's
    /// <c>RunTicker</c> applies the matching fallback for the character
    /// (<c>pending.IsSet ? pending.CharacterId : catalog.Characters[0].Id</c>).
    /// </remarks>
    private static IRandom CreateRandom(IObjectResolver resolver)
    {
        var pending = resolver.Resolve<PendingRun>();

        if (pending.IsSet)
        {
            return new SeededRandom(pending.Seed);
        }

        int fallbackSeed = Environment.TickCount;

        Debug.LogWarning(
            "No pending run was set, so this run is seeded from Environment.TickCount " +
            $"({fallbackSeed}) and cannot be replayed. This is the direct-Play path; a run " +
            "started from the menu carries its own seed.");

        return new SeededRandom(fallbackSeed);
    }
}
