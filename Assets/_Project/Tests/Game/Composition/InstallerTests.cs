using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
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
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";
    private static readonly ContentId OathboundId = new ContentId("character.oathbound");
    private static readonly ContentId HuskId = new ContentId("enemy.husk");
    private static readonly ContentId DescentId = new ContentId("mode.descent");

    /// <remarks>
    /// Typed as <see cref="ScriptableObject"/> rather than <c>CharacterDefinition</c> as of
    /// M3-02b: the skills-and-trees row builds five throwaway assets of three kinds, and they are
    /// destroyed together or they leak into every later fixture in the run.
    /// </remarks>
    private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

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

        foreach (ScriptableObject definition in _created)
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
    public void Boot_ResolvesCatalog_WithDescent()
    {
        IObjectResolver container = BuildBoot();

        var catalog = container.Resolve<ContentCatalog>();

        // The third kind, and the one whose absence is loudest rather than quietest: resolving the
        // mode is the first thing RunSession.Start does, so a catalog installed without its modes
        // cannot start any run at all.
        ModeSpec descent = null;
        Assert.That(() => descent = catalog.Mode(DescentId), Throws.Nothing,
            "The shipped Descent must resolve through the catalog the installer built.");

        Assert.That(descent.Id, Is.EqualTo(DescentId));
        Assert.That(catalog.Modes, Has.Count.EqualTo(1));

        // What MenuPresenter and RunTicker both mean by "the first mode the catalog holds" — the
        // stand-in they use rather than writing mode.descent into the code (GD 4.5).
        Assert.That(catalog.Modes[0].Id, Is.EqualTo(DescentId));
    }

    [Test]
    public void Boot_NullEnemyList_Throws()
    {
        var builder = new ContainerBuilder();

        // Required rather than optional, so that a boot list with no enemies has to say so with an
        // empty array. A silently-omitted list would be indistinguishable from an authored one
        // until a spawn plan named an archetype the catalog had never heard of.
        Assert.Throws<ArgumentNullException>(() =>
            BootInstaller.Install(
                builder,
                new[] { LoadOathbound() },
                null,
                new[] { LoadDescent() },
                Array.Empty<SkillDefinition>(),
                Array.Empty<SkillTreeDefinition>(),
                EmptyTable()));
    }

    [Test]
    public void Boot_NullModeList_Throws()
    {
        var builder = new ContainerBuilder();

        // The same bargain one kind along (M2-02). Omitted, it would build a container that
        // resolves everything, boots to the menu, and throws on the tap that starts a run.
        Assert.Throws<ArgumentNullException>(() =>
            BootInstaller.Install(
                builder,
                new[] { LoadOathbound() },
                Array.Empty<EnemyDefinition>(),
                null,
                Array.Empty<SkillDefinition>(),
                Array.Empty<SkillTreeDefinition>(),
                EmptyTable()));
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
            BootInstaller.Install(
                builder,
                new[] { broken },
                Array.Empty<EnemyDefinition>(),
                Array.Empty<ModeDefinition>(),
                Array.Empty<SkillDefinition>(),
                Array.Empty<SkillTreeDefinition>(),
                EmptyTable());
            Track(builder.Build());
        });

        Assert.That(thrown.Message, Does.Contain("BrokenBootAsset"),
            "The failure must name the asset — the whole point of ToSpec's rewrap (M0-11).");

        // Nothing was registered, so the failure is at composition time and there is no
        // half-built container to resolve from afterwards.
        Assert.That(builder.Count, Is.Zero,
            "A definition that cannot convert must not leave a partly-installed builder behind.");
    }

    /// <summary>
    /// The M3-02b wire, and the fourth and fifth kinds to go through it. A catalog installed
    /// without its skills resolves, boots to the menu, plays a run and offers the player nothing
    /// when they level — which reads as a broken offer rather than as missing content, the same
    /// failure the enemy list has and one screen further from its cause.
    /// </summary>
    [Test]
    public void Install_RegistersSkillsAndTrees()
    {
        // Three nodes, not one, because a legal tree has three branches and no id may appear in
        // two of them (SkillTreeSpec): one node cannot fill three branches. The row still asserts
        // on one of them, which is what "Skill(id) answers" means.
        ModifyStatDefinition effect = NewEffect("BootEffect");
        SkillDefinition first = NewSkill("BootNodeA", "skill.test.a", effect);
        SkillDefinition second = NewSkill("BootNodeB", "skill.test.b", effect);
        SkillDefinition third = NewSkill("BootNodeC", "skill.test.c", effect);
        SkillTreeDefinition tree = NewTree("BootTree", "tree.test", first, second, third);

        var builder = new ContainerBuilder();

        BootInstaller.Install(
            builder,
            new[] { LoadOathbound() },
            Array.Empty<EnemyDefinition>(),
            Array.Empty<ModeDefinition>(),
            new[] { first, second, third },
            new[] { tree },
            EmptyTable());

        var catalog = Track(builder.Build()).Resolve<ContentCatalog>();

        Assert.That(catalog.Skills, Has.Count.EqualTo(3));

        SkillSpec node = null;
        Assert.That(() => node = catalog.Skill(new ContentId("skill.test.a")), Throws.Nothing,
            "A skill in the boot list must resolve through the catalog the installer built.");

        Assert.That(node.Kind, Is.EqualTo(SkillKind.Passive));
        Assert.That(node.Effects, Has.Count.EqualTo(1));

        // The second index, and the one a run actually uses: a run resolves its tree from the
        // class it is playing, never from a tree id anyone typed.
        Assert.That(catalog.TryGetTreeFor(OathboundId, out SkillTreeSpec spec), Is.True,
            "The tree names the Oathbound, so TryGetTreeFor must answer for that class.");

        Assert.That(spec.Id, Is.EqualTo(new ContentId("tree.test")));
        Assert.That(spec.NodeCount, Is.EqualTo(3));
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
    /// <remarks>
    /// As of M2-13b the toggle is built over <see cref="ISaveStore"/> rather than read out of
    /// <c>PlayerPrefs</c>, and as of M3-09c over <c>ProfileStore</c> rather than the port directly —
    /// which is what makes it safe to resolve here at all: constructing either touches no disk and
    /// no registry, and it starts at GD §16.3's default until <c>BootFlow</c> hands the store a
    /// loaded profile. Still never written — a flip here would put a file under
    /// <c>persistentDataPath</c> on the machine running the tests.
    /// </remarks>
    [Test]
    public void Boot_ResolvesHaptics_NullVibratorOffAndroid()
    {
        IObjectResolver container = BuildBoot();

        var vibrator = container.Resolve<IVibrator>();
        var settings = container.Resolve<HapticsSettings>();

        Assert.That(vibrator, Is.InstanceOf<NullVibrator>(),
            "Anywhere but an Android player build the vibrator must be the no-op — an Editor " +
            "playtest has no device to buzz and must never reach for JNI.");

        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.Enabled, Is.True, "GD §16.3's default, before a profile has been applied.");
        Assert.That(container.Resolve<HapticsSettings>(), Is.SameAs(settings),
            "One toggle, or the listener reads a different answer from the one an options screen set.");

        // **And the profile behind it is one object for the app's life** (M3-09c rule 4). A store
        // resolved fresh per request would lose the hint's flag between the level-up that spent it
        // and the boundary that saved it, and would do so silently — the run would simply show the
        // callout again next time.
        var profiles = container.Resolve<ProfileStore>();

        Assert.That(profiles, Is.Not.Null);
        Assert.That(container.Resolve<ProfileStore>(), Is.SameAs(profiles));
        Assert.That(profiles.Current.Version, Is.EqualTo(PlayerProfile.CurrentVersion));
    }

    /// <summary>
    /// The M2-13b wire. Like the clock before it, the only way this registration can be wrong is
    /// by not being there — and the consequence of that would be a save store resolved twice,
    /// which two adapters pointed at one directory would make a race rather than a bug.
    /// </summary>
    [Test]
    public void Container_ResolvesSaveStore()
    {
        IObjectResolver container = BuildBoot();

        var store = container.Resolve<ISaveStore>();

        Assert.That(store, Is.InstanceOf<LocalJsonSaveStore>());

        // Resolving it creates nothing on disk: the directory appears at the first write, which is
        // the first time a player has actually saved something.
        Assert.That(container.Resolve<ISaveStore>(), Is.SameAs(store));
    }

    /// <summary>
    /// The M2-01 wire, and the one honest check that task has: nothing renders, nothing changes
    /// behaviour, and the only way the registration can be wrong is by not being there.
    /// </summary>
    [Test]
    public void Container_ResolvesClock()
    {
        IObjectResolver container = BuildBoot();

        var clock = container.Resolve<IClock>();

        Assert.That(clock, Is.InstanceOf<UnityClock>());

        // Singleton, like the vibrator above and unlike IRandom: a clock is a device the whole
        // app shares. Two instances would not be a visible bug today — which is precisely why it
        // is asserted now rather than discovered when a save's timestamp starts mattering.
        Assert.That(container.Resolve<IClock>(), Is.SameAs(clock));
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

    /// <summary>
    /// The M4-05b wire: the run scope has one thing that banks a payout, and it reaches the profile
    /// that outlives the run.
    /// </summary>
    /// <remarks>
    /// Here rather than in <c>ShardWriterTests</c> because the claim is about the two installers
    /// together — the writer is registered in the run scope and its store is registered in
    /// <c>BootScope</c>, and there is nowhere else in the suite that builds both.
    /// </remarks>
    [Test]
    public void RunScope_ComposesWithTheWriter()
    {
        IObjectResolver boot = BuildBoot();

        boot.Resolve<PendingRun>().Set(DescentId, OathboundId, 7);

        IScopedObjectResolver scope = Track(boot.CreateScope(RunInstaller.Install));

        var writer = scope.Resolve<ShardWriter>();

        Assert.That(writer, Is.Not.Null);
        Assert.That(scope.Resolve<ShardWriter>(), Is.SameAs(writer), "one writer, or a payout is banked twice.");

        // **And its ProfileStore came from BootScope, not from the run** (rule 6). A store
        // registered per run would forget the payout between the death that earned it and the menu
        // that will one day spend it, and would do so in silence.
        Assert.That(scope.Resolve<ProfileStore>(), Is.SameAs(boot.Resolve<ProfileStore>()));

        // And a container with no ProfileStore in reach does not compose the writer at all, loudly
        // — which is the failure mode rule 6 wants over a run that quietly banks nothing. Built
        // bare rather than through RunInstaller so the throw names the missing dependency rather
        // than whichever of the run's other parents happens to be looked up first.
        var bare = new ContainerBuilder();

        bare.Register<DomainEventHub>(Lifetime.Scoped).As<IDomainEvents>().AsSelf();
        bare.Register<ShardWriter>(Lifetime.Scoped);

        IObjectResolver without = Track(bare.Build());

        Assert.That(
            () => without.Resolve<ShardWriter>(),
            Throws.Exception.With.Message.Contains(nameof(ProfileStore)));
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
    public void Run_NullByNameParameter_Resolves()
    {
        // `RunScope` hands `ArenaPool` the `Transform` its bodies are parented under — a scene
        // reference an undressed Run scene does not have — and passes it by name whether or not it
        // is there. VContainer never falls back to a C# default, so an *omitted* parameter fails to
        // compose the run. This row is the check that a **null** one is a value rather than an
        // absence, because the failure otherwise lands on exactly the workflow every optional field
        // on that scope exists to protect: pressing Play in a Run scene nobody has dressed yet.
        // M2-10 had the same row for the gate `Transform` that moved onto the arena prefab.
        var builder = new ContainerBuilder();

        builder.Register<NullParameterProbe>(Lifetime.Scoped)
            .WithParameter("parent", (Transform)null);

        using (IObjectResolver container = builder.Build())
        {
            NullParameterProbe probe = null;

            Assert.DoesNotThrow(() => probe = container.Resolve<NullParameterProbe>());

            Assert.That(probe.ParentIsNull, Is.True, "And it arrives as the null it was given.");
        }
    }

    /// <summary>
    /// A one-argument type for <see cref="Run_NullByNameParameter_Resolves"/>, and nothing else.
    /// </summary>
    /// <remarks>
    /// Nested and private because it exists for that row alone. <c>ArenaPool</c> itself needs a
    /// resolver, a prefab list, an event hub and a <c>PlayerView</c> to be constructed, none of
    /// which this row is about — the question is purely what VContainer does with a null.
    /// </remarks>
    private sealed class NullParameterProbe
    {
        public NullParameterProbe(Transform parent)
        {
            ParentIsNull = parent == null;
        }

        public bool ParentIsNull { get; }
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
        Assert.Throws<InvalidOperationException>(() => _ = pending.ModeId);
        Assert.Throws<InvalidOperationException>(() => _ = pending.CharacterId);
        Assert.Throws<InvalidOperationException>(() => _ = pending.Seed);

        pending.Set(DescentId, OathboundId, 42);

        Assert.That(pending.IsSet, Is.True);
        Assert.That(pending.ModeId, Is.EqualTo(DescentId));
        Assert.That(pending.CharacterId, Is.EqualTo(OathboundId));
        Assert.That(pending.Seed, Is.EqualTo(42));

        pending.Clear();

        Assert.That(pending.IsSet, Is.False);
        Assert.Throws<InvalidOperationException>(() => _ = pending.ModeId);
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

    private static ModeDefinition LoadDescent()
    {
        var definition = AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath);
        Assert.That(definition, Is.Not.Null, $"No ModeDefinition at {DescentPath}.");
        return definition;
    }

    /// <summary>
    /// A boot container carrying the project's shipped content — the Oathbound, the Husk and
    /// Descent — which is what <c>BootScope</c> hands the installer.
    /// </summary>
    private IObjectResolver BuildBoot()
    {
        var builder = new ContainerBuilder();

        // The two M3-02b lists are empty because nothing ships in Data/ until M3-12 (rule 7), and
        // empty is what BootScope.prefab carries too — so this container is exactly the shipped one.
        BootInstaller.Install(
            builder,
            new[] { LoadOathbound() },
            new[] { LoadHusk() },
            new[] { LoadDescent() },
            Array.Empty<SkillDefinition>(),
            Array.Empty<SkillTreeDefinition>(),
            EmptyTable());

        return Track(builder.Build());
    }

    /// <summary>
    /// A boot container with a run scope on top, and a pending run already chosen — the shape
    /// every row but the fallback one needs.
    /// </summary>
    private IScopedObjectResolver BuildRunScope(int seed)
    {
        IObjectResolver boot = BuildBoot();
        boot.Resolve<PendingRun>().Set(DescentId, OathboundId, seed);
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

    /// <summary>
    /// A throwaway effect asset, so the nodes below carry something rather than being refused as
    /// nodes that change nothing.
    /// </summary>
    /// <remarks>
    /// The conversion rules for all four M3-02b types are <c>SkillAuthoringTests</c>'. These three
    /// helpers build only what this fixture's one row needs — a boot list with something in it.
    /// </remarks>
    private ModifyStatDefinition NewEffect(string assetName)
    {
        var effect = ScriptableObject.CreateInstance<ModifyStatDefinition>();
        effect.name = assetName;
        _created.Add(effect);

        var serialized = new SerializedObject(effect);
        serialized.FindProperty("_value").floatValue = 0.1f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return effect;
    }

    private SkillDefinition NewSkill(string assetName, string id, ModifyStatDefinition effect)
    {
        var skill = ScriptableObject.CreateInstance<SkillDefinition>();
        skill.name = assetName;
        _created.Add(skill);

        var serialized = new SerializedObject(skill);
        serialized.FindProperty("_id").stringValue = id;
        serialized.FindProperty("_nameKey").stringValue = $"{id}.name";
        serialized.FindProperty("_descriptionKey").stringValue = $"{id}.description";

        SerializedProperty effects = serialized.FindProperty("_effects");
        effects.arraySize = 1;
        effects.GetArrayElementAtIndex(0).objectReferenceValue = effect;

        serialized.ApplyModifiedPropertiesWithoutUndo();

        return skill;
    }

    /// <summary>
    /// A tree of three one-node branches pointed at the shipped Oathbound — the smallest shape
    /// <c>SkillTreeSpec</c> accepts.
    /// </summary>
    private SkillTreeDefinition NewTree(
        string assetName,
        string id,
        params SkillDefinition[] nodes)
    {
        var tree = ScriptableObject.CreateInstance<SkillTreeDefinition>();
        tree.name = assetName;
        _created.Add(tree);

        var serialized = new SerializedObject(tree);
        serialized.FindProperty("_id").stringValue = id;
        serialized.FindProperty("_character").objectReferenceValue = LoadOathbound();

        SerializedProperty branches = serialized.FindProperty("_branches");
        branches.arraySize = nodes.Length;

        for (int b = 0; b < nodes.Length; b++)
        {
            SerializedProperty branch = branches.GetArrayElementAtIndex(b);
            branch.FindPropertyRelative("_nameKey").stringValue = $"{id}.branch{b}";

            SerializedProperty tiers = branch.FindPropertyRelative("_tiers");
            tiers.arraySize = 1;

            SerializedProperty tierNodes = tiers.GetArrayElementAtIndex(0)
                .FindPropertyRelative("_nodes");
            tierNodes.arraySize = 1;
            tierNodes.GetArrayElementAtIndex(0).objectReferenceValue = nodes[b];
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();

        return tree;
    }

    /// <summary>
    /// A localisation table for a container build. Empty, because nothing in these rows reads a
    /// word — what they assert is that <c>BootInstaller</c> takes one and registers the port.
    /// </summary>
    private static LocalizationTable EmptyTable() =>
        ScriptableObject.CreateInstance<LocalizationTable>();

}
