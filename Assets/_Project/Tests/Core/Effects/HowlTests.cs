using System;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

[TestFixture]
public sealed class HowlTests
{
    private const int Capacity = 8;
    private static readonly ContentId EnemyId = new("enemy.howl_target");
    private static readonly PetSpec Hound = new(new ContentId("pet.hound"), 70f, 4f, 7f, 0.25f, 1.4f, 8f, 10f, 1.5f);
    private readonly Howl _howl = new(0.25f);
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PetSystem _pets;
    private PlayerCombat _combat;
    private PlayerStats _stats;
    private CharacterSpec _character;
    private EnemySystem _enemies;
    private SimulatedClock _clock;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _character = new CharacterSpec(
            new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
            new LocKey("character.beastmaster.description"), 100f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.ReturningAxe, 10f, 2f, 8f, 360f, 0.4f,
                shotRadius: 0.3f, axeSpeed: 8f, axeCarry: 1, axeHitsPerPass: 3),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Swap, 8f, 0.25f, 6f, 0.15f, 0f, 0f, 0.2f), pet: Hound);
        _combat = new PlayerCombat(_character, _events, _intents, Capacity);
        _stats = new PlayerStats(_combat, new PlayerMotor(_character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), _events));
        _pets = new PetSystem(Hound, _events, _intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
        _enemies = new EnemySystem(Catalog(), _events, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
        _clock = new SimulatedClock();
    }

    [Test]
    public void Howl_HealsStanding_NotFallen()
    {
        _pets.LeadStats.Resolve(PlayerStat.PackHounds).Base = 2f;
        _pets.ApplyDamage(_pets.Pets[0].Id, 35f, 0f, 0);
        _pets.ApplyDamage(_pets.Pets[1].Id, 20f, 0f, 0);
        _pets.ApplyDamage(_pets.Pets[2].Id, 100f, 0f, 0);

        Assert.That(_stats.HowlHeal.Value, Is.EqualTo(0.2f));
        _stats.Resolve(PlayerStat.HowlHeal).Base = 0.3f;
        Assert.That(Runner().Cast(0, 0f, auto: false), Is.True);
        Assert.That(_pets.Pets[0].Health.Current, Is.EqualTo(56f).Within(0.0001f));
        Assert.That(_pets.Pets[1].Health.Current, Is.EqualTo(25.5f).Within(0.0001f));
        Assert.That(_pets.Pets[2].Health.Current, Is.Zero);
        Assert.That(_pets.Pets[2].State, Is.EqualTo(PetState.Fallen));
    }

    [Test]
    public void Howl_BitesFaster_ForSeconds_AndRefreshesWithoutStacking()
    {
        _enemies.Spawn(EnemyId, _pets.Pets[0].Position + 0.1f * Vector3.UnitX);
        Assert.That(_stats.HowlSeconds.Value, Is.EqualTo(4f));
        _stats.Resolve(PlayerStat.HowlSeconds).Base = 2f;
        Assert.That(Runner().Cast(0, 0f, auto: false), Is.True);
        Tick(0f);
        Tick(0.19f);
        Assert.That(_events.Of<PetBit>(), Has.Count.EqualTo(1));
        Tick(0.21f);
        Assert.That(_events.Of<PetBit>(), Has.Count.EqualTo(2));

        _pets.Howl(0f, 0.5f, 2f, 1.5f);
        _pets.Howl(0f, 0.25f, 2f, 3f);
        Tick(4f);
        Tick(4.15f);
        Assert.That(_events.Of<PetBit>(), Has.Count.EqualTo(3));
        Tick(4.18f);
        Assert.That(_events.Of<PetBit>(), Has.Count.EqualTo(4));
        Tick(4.82f);
        Tick(5.01f);
        Assert.That(_events.Of<PetBit>(), Has.Count.EqualTo(5));
        Tick(5.08f);
        Assert.That(_events.Of<PetBit>(), Has.Count.EqualTo(6));
    }

    [Test]
    public void Howl_PublishesAtLead()
    {
        _pets.LeadStats.Resolve(PlayerStat.PackHounds).Base = 1f;
        Vector3 leadPosition = _pets.Pets[0].Position;
        _pets.ApplyDamage(_pets.Pets[0].Id, 100f, 0f, 0);

        Assert.That(Runner().Cast(0, 0f, auto: false), Is.True);
        Assert.That(_events.Single<PackHowled>().Position, Is.EqualTo(leadPosition));
    }

    [Test]
    public void NoStandingPet_Refused_CooldownKept()
    {
        SkillRunner runner = Runner();
        _pets.ApplyDamage(_pets.Pets[0].Id, 100f, 0f, 0);

        Assert.That(runner.Cast(0, 0f, auto: false), Is.False);
        Assert.That(runner.CooldownFraction(0), Is.Zero);
        Assert.That(_events.Of<PackHowled>(), Is.Empty);
    }

    [Test]
    public void Blackboard_LowestFraction_TriggersBelowHalf()
    {
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var random = new FixedRandom(99);
        var session = new RunSession(Catalog(mode), random, _events, _intents,
            new RunRecorder(random, new FixedClock(default), _events), Capacity, Capacity, Capacity);
        session.Start(new RunConfig(mode.Id, _character.Id, 99, 1, SpawnPlan.Empty, restore: null));
        RunState state = session.State;
        ReadState<SkillRunner>(state, "Skills").Add(Skill());
        PetSystem pets = ReadState<PetSystem>(state, "Pets");
        CombatBlackboard board = ReadState<PlayerCombat>(state, "Combat").Blackboard;
        var snapshot = new WorldSnapshot(Capacity) { Dt = 0.125f };
        pets.LeadStats.Resolve(PlayerStat.PackHounds).Base = 2f;
        pets.ApplyDamage(pets.Pets[2].Id, 100f, 0f, 0);
        pets.ApplyDamage(pets.Pets[1].Id, 17.5f, 0f, 0);

        session.Tick(snapshot);
        Assert.That(board.LowestPetHpFraction, Is.EqualTo(0.5f));
        Assert.That(_events.Of<SkillCast>(), Is.Empty);
        pets.ApplyDamage(pets.Pets[1].Id, 4f, state.Time, 0);
        session.Tick(snapshot);
        Assert.That(board.LowestPetHpFraction, Is.LessThan(0.5f));
        Assert.That(_events.Single<SkillCast>().WasAuto, Is.True);
        Assert.That(state.SkillCooldownSeconds(0), Is.EqualTo(14f));

        pets.Clear();
        session.Tick(snapshot);
        Assert.That(board.LowestPetHpFraction, Is.EqualTo(1f));
        board.LowestPetHpFraction = 0.25f;
        board.Reset();
        Assert.That(board.LowestPetHpFraction, Is.EqualTo(1f));
        session.End();
    }

    [Test]
    public void Howl_DoesNotAllocate()
    {
        var events = new SilentEvents();
        var pets = new PetSystem(Hound, events, _intents);
        var handler = new HowlHandler(pets, _stats, _clock);
        var registry = new EffectRegistry();
        registry.Register<Howl>(handler);
        pets.SpawnBeside(Vector3.Zero, 0f);
        pets.LeadStats.Resolve(PlayerStat.PackHounds).Base = 2f;
        object source = new();

        AllocationAssert.None(() =>
        {
            _intents.Clear();
            registry.CanCast(_howl, source);
            registry.Apply(_howl, source);
            pets.Tick(0f, 0f, Vector3.Zero, _enemies, _combat);
        });
    }

    private void Tick(float now) => _pets.Tick(0f, now, Vector3.Zero, _enemies, _combat);

    private SkillRunner Runner()
    {
        var registry = new EffectRegistry();
        registry.Register<Howl>(new HowlHandler(_pets, _stats, _clock));
        var runner = new SkillRunner(registry, _combat.Blackboard, _events);
        runner.Add(Skill());
        return runner;
    }

    private SkillSpec Skill() =>
        new(new ContentId("skill.howl"), new LocKey("skill.howl.name"), new LocKey("skill.howl.desc"),
            SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(14f,
                new TriggerSpec(new[] { new TriggerClause(TriggerField.LowestPetHpFraction, TriggerComparison.Below, 0.5f) }),
                new IEffect[] { _howl }));

    private static T ReadState<T>(RunState state, string property) =>
        (T)typeof(RunState).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);

    private ContentCatalog Catalog(ModeSpec mode = null) =>
        new(new[] { _character }, new[] { new EnemySpec(EnemyId, new LocKey("enemy.test.name"), 1000f, 2f, 1, 4, 5f,
            false, 8f, 1.2f, 0.2f, 0.2f, 30f, EnemyBehaviourKind.Static) }, mode is null ? null : new[] { mode });
}
