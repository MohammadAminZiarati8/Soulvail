using System;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
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

        // What composes a snapshot, and the reason RunSession does not hold a clock (M2-14a rule
        // 12). Scoped rather than singleton: it captures *this* run's generator, so a recorder that
        // outlived the run would be holding the streams of a run that is over.
        builder.Register<RunRecorder>(Lifetime.Scoped);

        // And what puts one somewhere. Scoped so its subscriptions die with the run, and registered
        // as a plain type rather than an entry point because it has no frame to be part of — it
        // subscribes in its constructor and is constructed by being on RunTicker's dependency
        // chain, which is what guarantees it is listening before the opening snapshot is taken
        // (AR §18.1).
        //
        // ISaveStore is resolved from the parent scope: BootInstaller registers the one
        // LocalJsonSaveStore the app owns, and a run writing through a second one would be two
        // objects renaming the same file.
        builder.Register<SaveWriter>(Lifetime.Scoped);

        // One brain behind two ports (M1-09). The frame loop holds IRunSession and can start, tick
        // and end a run; an input adapter holds IPlayerCommands and can only ask for a focus. Two
        // registrations of RunSession would be two brains — core would tick one and the player's
        // taps would land on the other, with no error anywhere and a focus that simply never
        // arrived — so it is registered once and named twice.
        //
        // The enemy capacity comes from the same constant the snapshot above was built with, and
        // by name rather than by type: core's registry has to be able to hold every enemy the
        // snapshot can carry, or an enemy exists that core cannot see the position of. A second
        // int parameter later would make WithParameter<int> ambiguous, so the name is the wire.
        // The device cap joins it, by name for the same reason and from the same place: M2-04
        // priced 28 against a measured path refresh and ally count, and M2-05 is the first task
        // with something that spends it — the concurrency curve a stage is composed under. The
        // projectile capacity is the third, from the same place for the same reason (M2-07a rule 7).
        // Three ints on one registration is exactly the ambiguity WithParameter<int> would
        // introduce, and is why every one of them is wired by name.
        builder.Register<RunSession>(Lifetime.Scoped)
            .As<IRunSession>()
            .As<IPlayerCommands>()
            .WithParameter("enemyCapacity", BootInstaller.SnapshotEnemyCapacity)
            .WithParameter("deviceEnemyCap", BootInstaller.DeviceEnemyCap)
            .WithParameter("projectileCapacity", BootInstaller.ProjectileCapacity);
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
