using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Soulvail.Tests.Game.Composition;

/// <summary>
/// Builds the real containers the two installers describe and resolves from them. Every row is a
/// registration mistake that would otherwise only show up on a phone: a port wired to a second
/// instance, a run that leaks its subscriptions, a seed that silently comes from nowhere.
/// </summary>
/// <remarks>
/// <para>
/// No <c>LifetimeScope</c> MonoBehaviours anywhere here. The containers are built with
/// <c>new ContainerBuilder()</c> and run scopes with <c>CreateScope</c>, which is the whole
/// argument for the installers being scene-free statics: the wiring is testable without a scene,
/// a Play mode run, or a device.
/// </para>
/// <para>
/// One fixture for the composition module rather than one per type, matching the convention
/// M0-07 settled on for <c>ContentTests</c> — the three types are one unit of wiring and are
/// read together.
/// </para>
/// </remarks>
[TestFixture]
public sealed class InstallerTests
{
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";
    private const string HuskPath = "Assets/_Project/Data/Enemies/Husk.asset";
    private static readonly ContentId OathboundId = new ContentId("character.oathbound");
    private static readonly ContentId HuskId = new ContentId("enemy.husk");

    private readonly List<CharacterDefinition> _created = new List<CharacterDefinition>();
    private readonly List<IDisposable> _containers = new List<IDisposable>();

    [TearDown]
    public void DisposeContainersAndInstances()
    {
        // Scopes before their roots, so a scope never outlives the container it resolves through.
        // Disposing twice is a no-op on both sides — VContainer drains its disposable stack and
        // DomainEventHub guards its own flag — which is what lets the dispose row dispose its own
        // scope and still be torn down normally here.
        for (int i = _containers.Count - 1; i >= 0; i--)
        {
            _containers[i].Dispose();
        }

        _containers.Clear();

        foreach (CharacterDefinition definition in _created)
        {
            if (definition != null)
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        _created.Clear();
    }

    [Test]
    public void Boot_ResolvesCatalog_WithOathbound()
    {
        IObjectResolver container = BuildBoot();

        var catalog = container.Resolve<ContentCatalog>();

        Assert.That(catalog, Is.Not.Null);

        // Character() throws on a miss rather than returning null, so the lookup itself is the
        // assertion — `Is.Not.Null` on its result would be checking something that cannot happen.
        CharacterSpec oathbound = null;
        Assert.That(() => oathbound = catalog.Character(OathboundId), Throws.Nothing,
            "The shipped Oathbound must resolve through the catalog the installer built.");

        Assert.That(oathbound.Id, Is.EqualTo(OathboundId));
        Assert.That(catalog.Characters, Has.Count.EqualTo(1));
    }

    [Test]
    public void Boot_ResolvesCatalog_WithHusk()
    {
        IObjectResolver container = BuildBoot();

        var catalog = container.Resolve<ContentCatalog>();

        // The wire M1-07 added, and the one whose failure is quietest: a catalog installed without
        // its enemies resolves, builds, starts a run and produces an empty arena, which reads as a
        // broken spawner rather than as missing content (M1-06).
        EnemySpec husk = null;
        Assert.That(() => husk = catalog.Enemy(HuskId), Throws.Nothing,
            "The shipped Husk must resolve through the catalog the installer built.");

        Assert.That(husk.Id, Is.EqualTo(HuskId));
        Assert.That(catalog.Enemies, Has.Count.EqualTo(1));
    }

    [Test]
    public void Boot_NullEnemyList_Throws()
    {
        var builder = new ContainerBuilder();

        // Required rather than optional, so that a boot list with no enemies has to say so with an
        // empty array. A silently-omitted list would be indistinguishable from an authored one
        // until a spawn plan named an archetype the catalog had never heard of.
        Assert.Throws<ArgumentNullException>(() =>
            BootInstaller.Install(builder, new[] { LoadOathbound() }, null));
    }

    [Test]
    public void Boot_InvalidDefinition_FailsBuild()
    {
        CharacterDefinition broken = NewDefinition("BrokenBootAsset");

        // [Min] clamps the Inspector and nothing else (M0-11), so a SerializedObject write is how
        // a bad value actually reaches the catalog — from a merge, a hand-edited YAML, or this.
        SetFloat(broken, "_maxHp", 0f);

        var builder = new ContainerBuilder();

        // Install and Build together, because the row's contract is "a broken asset never becomes
        // a running container", not "a particular call throws". Which one throws is asserted
        // below rather than assumed: the catalog is built eagerly, so it is Install.
        var thrown = Assert.Throws<ArgumentException>(() =>
        {
            BootInstaller.Install(builder, new[] { broken }, Array.Empty<EnemyDefinition>());
            Track(builder.Build());
        });

        Assert.That(thrown.Message, Does.Contain("BrokenBootAsset"),
            "The failure must name the asset — the whole point of ToSpec's rewrap (M0-11).");

        // Nothing was registered, so the failure is at composition time and there is no
        // half-built container to resolve from afterwards.
        Assert.That(builder.Count, Is.Zero,
            "A definition that cannot convert must not leave a partly-installed builder behind.");
    }

    [Test]
    public void Boot_PendingRun_IsSingleton()
    {
        IObjectResolver container = BuildBoot();

        var first = container.Resolve<PendingRun>();
        var second = container.Resolve<PendingRun>();

        // Two instances would be the worst kind of wiring bug: the menu writes one, the run reads
        // the other, and the symptom is "the class I picked was ignored" a scene later.
        Assert.That(second, Is.SameAs(first));
    }

    /// <summary>
    /// The M1-20 wire. Haptics are the one feature in the game whose absence is invisible — a
    /// missing registration reads exactly like a phone that does not buzz much — so the
    /// registration itself is what gets asserted, in the Editor, where the platform must resolve
    /// the no-op.
    /// </summary>
    [Test]
    public void Boot_ResolvesHaptics_NullVibratorOffAndroid()
    {
        IObjectResolver container = BuildBoot();

        var vibrator = container.Resolve<IVibrator>();
        var settings = container.Resolve<HapticsSettings>();

        Assert.That(vibrator, Is.InstanceOf<NullVibrator>(),
            "Anywhere but an Android player build the vibrator must be the no-op — an Editor " +
            "playtest has no device to buzz and must never reach for JNI.");

        // Read, never written: flipping it here would persist to the machine running the tests.
        Assert.That(settings, Is.Not.Null);
        Assert.That(container.Resolve<HapticsSettings>(), Is.SameAs(settings),
            "One toggle, or the listener reads a different answer from the one an options screen set.");
    }

    [Test]
    public void Run_ResolvesSession_Scoped()
    {
        IScopedObjectResolver scope = BuildRunScope(seed: 7);

        var first = scope.Resolve<IRunSession>();
        var second = scope.Resolve<IRunSession>();

        Assert.That(first, Is.InstanceOf<RunSession>());
        Assert.That(second, Is.SameAs(first), "One session per run, or two brains disagree.");
        Assert.That(first.IsRunning, Is.False, "Resolving composes a run; it does not start one.");
    }

    [Test]
    public void Run_SessionAndCommands_SameInstance()
    {
        IScopedObjectResolver scope = BuildRunScope(seed: 7);

        var session = scope.Resolve<IRunSession>();
        var commands = scope.Resolve<IPlayerCommands>();

        // Beyond the M1-09 spec's Tests table, and it belongs to this fixture rather than to that
        // task's core tests: the failure it catches is a registration, not a rule. Two instances
        // would mean core ticks one brain while the player's taps land on the other — no error
        // anywhere, and a focus that simply never arrives.
        Assert.That(commands, Is.SameAs(session));
    }

    [Test]
    public void Run_Random_SeededFromPendingRun()
    {
        IScopedObjectResolver scope = BuildRunScope(seed: 123);

        var random = scope.Resolve<IRandom>();

        // The seed is the run. If this wire is wrong, every Daily and every bug repro replays
        // something other than what was recorded, and nothing else in the game would notice.
        Assert.That(random.Seed, Is.EqualTo(123));
    }

    [Test]
    public void Run_WithoutPendingRun_UsesFallbackSeed()
    {
        IObjectResolver boot = BuildBoot();
        IScopedObjectResolver scope = Track(boot.CreateScope(RunInstaller.Install));

        // Before the Resolve, because the factory is lazy: nothing has run yet, and the warning
        // arrives during the resolve below. LogAssert fails at teardown if it never comes, which
        // is what makes this row own rule 6's "never throw, but say so" rather than merely
        // tolerate it — an unexpected warning does not fail a test on its own (M0-11).
        LogAssert.Expect(LogType.Warning, new Regex("Environment.TickCount"));

        var random = scope.Resolve<IRandom>();

        Assert.That(random, Is.Not.Null, "The direct-Play path must produce a run, not an exception.");
    }

    [Test]
    public void Run_EventsPortAndHub_SameInstance()
    {
        IScopedObjectResolver scope = BuildRunScope(seed: 1);

        var port = scope.Resolve<IDomainEvents>();
        var hub = scope.Resolve<DomainEventHub>();

        // Two instances here means core publishes into one hub while every view listens to the
        // other: no errors, no events, a HUD that never updates.
        Assert.That(port, Is.SameAs(hub));
    }

    [Test]
    public void Run_IntentPortAndBuffer_SameInstance()
    {
        IScopedObjectResolver scope = BuildRunScope(seed: 1);

        var port = scope.Resolve<IIntentSink>();
        var buffer = scope.Resolve<IntentBuffer>();

        // Same failure, other direction: core writes its move intent into a buffer no view reads,
        // and the player simply never moves.
        Assert.That(port, Is.SameAs(buffer));
    }

    [Test]
    public void Run_Snapshot_HasConfiguredCapacity()
    {
        IScopedObjectResolver scope = BuildRunScope(seed: 1);

        var snapshot = scope.Resolve<WorldSnapshot>();

        Assert.That(snapshot.EnemyCapacity, Is.EqualTo(BootInstaller.SnapshotEnemyCapacity));
        Assert.That(snapshot.EnemyCapacity, Is.EqualTo(64), "The M0 concurrency cap, per the spec.");
    }

    [Test]
    public void Run_ScopeDispose_DisposesHub()
    {
        IScopedObjectResolver scope = BuildRunScope(seed: 1);

        // Resolved first, deliberately: VContainer tracks an object for disposal at the moment it
        // constructs it, so a hub nobody ever asked for is a hub nobody has to dispose.
        var hub = scope.Resolve<DomainEventHub>();

        scope.Dispose();

        // Rule 8, and the reason the hub is registered as a type rather than as an instance:
        // VContainer skips instances handed to RegisterInstance when it builds the disposal list,
        // so the obvious registration would leave this green-looking wire leaking every
        // subscription from one run into the next.
        Assert.Throws<ObjectDisposedException>(() => hub.Subscribe<RunStartedProbe>(_ => { }));
    }

    [Test]
    public void PendingRun_ReadBeforeSet_Throws()
    {
        var pending = new PendingRun();

        Assert.That(pending.IsSet, Is.False);

        // Exact type match (M0-08): rule 9 says InvalidOperationException, and a default id
        // returned quietly instead would surface a scene later as missing content.
        Assert.Throws<InvalidOperationException>(() => _ = pending.CharacterId);
        Assert.Throws<InvalidOperationException>(() => _ = pending.Seed);

        pending.Set(OathboundId, 42);

        Assert.That(pending.IsSet, Is.True);
        Assert.That(pending.CharacterId, Is.EqualTo(OathboundId));
        Assert.That(pending.Seed, Is.EqualTo(42));

        pending.Clear();

        Assert.That(pending.IsSet, Is.False);
        Assert.Throws<InvalidOperationException>(() => _ = pending.CharacterId);
    }

    /// <summary>
    /// A struct that is not one of the run events, so subscribing to it cannot accidentally
    /// depend on anything the hub already knows about.
    /// </summary>
    private struct RunStartedProbe
    {
    }

    private static CharacterDefinition LoadOathbound()
    {
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(OathboundPath);
        Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {OathboundPath}.");
        return definition;
    }

    private static EnemyDefinition LoadHusk()
    {
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(HuskPath);
        Assert.That(definition, Is.Not.Null, $"No EnemyDefinition at {HuskPath}.");
        return definition;
    }

    /// <summary>
    /// A boot container carrying the project's shipped content — the Oathbound and the Husk —
    /// which is what <c>BootScope</c> hands the installer.
    /// </summary>
    private IObjectResolver BuildBoot()
    {
        var builder = new ContainerBuilder();
        BootInstaller.Install(builder, new[] { LoadOathbound() }, new[] { LoadHusk() });
        return Track(builder.Build());
    }

    /// <summary>
    /// A boot container with a run scope on top, and a pending run already chosen — the shape
    /// every row but the fallback one needs.
    /// </summary>
    private IScopedObjectResolver BuildRunScope(int seed)
    {
        IObjectResolver boot = BuildBoot();
        boot.Resolve<PendingRun>().Set(OathboundId, seed);
        return Track(boot.CreateScope(RunInstaller.Install));
    }

    private T Track<T>(T container) where T : IObjectResolver
    {
        _containers.Add(container);
        return container;
    }

    private CharacterDefinition NewDefinition(string assetName)
    {
        var definition = ScriptableObject.CreateInstance<CharacterDefinition>();

        // CreateInstance leaves `name` empty, and an empty name makes Does.Contain pass against
        // any message at all (M0-11). Naming it is what gives the assertion something to find.
        definition.name = assetName;
        _created.Add(definition);
        return definition;
    }

    private static void SetFloat(CharacterDefinition definition, string field, float value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
