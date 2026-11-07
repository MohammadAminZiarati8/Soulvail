using System;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

[TestFixture]
public sealed class PounceTests
{
    private const int Capacity = 8;
    private const float LeapSeconds = 0.35f;
    private static readonly ContentId Light = new("enemy.frog");
    private static readonly ContentId Heavy = new("enemy.bullfrog");
    private static readonly ContentId Boss = new("boss.test");
    private static readonly ContentId Body = new("enemy.boss_body");

    private readonly Pounce _pounce = new(3f, 6f, 2f, 0.8f, LeapSeconds);
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _combat;
    private PetSystem _pets;
    private CharacterSpec _character;
    private PounceHandler _handler;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        var hound = new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 0.25f, 1.4f, 8f, 10f, 1.5f);
        _character = new CharacterSpec(
            new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
            new LocKey("character.beastmaster.description"), 100f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.ReturningAxe, 10f, 2f, 8f, 360f, 0.4f,
                shotRadius: 0.3f, axeSpeed: 8f, axeCarry: 1, axeHitsPerPass: 3),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Swap, 8f, 0.25f, 6f, 0.15f, 0f, 0f, 0.2f), pet: hound);
        _enemies = Enemies(_events);
        _combat = new PlayerCombat(_character, _events, _intents, Capacity);
        _pets = new PetSystem(hound, _events, _intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
        _handler = new PounceHandler(_pets, _enemies, _combat, new SimulatedClock());
    }

    [Test]
    public void Pick_NearestMarkedInBand_OrRefuseWithoutCooldown()
    {
        var effects = new EffectRegistry();
        effects.Register<Pounce>(_handler);
        var skills = new SkillRunner(effects, _combat.Blackboard, _events);
        skills.Add(Skill(_pounce));

        Spawn(Light, 3f, marked: false);
        Spawn(Light, 2.99f);
        Spawn(Light, 6.01f);
        EnemyAgent dead = Spawn(Light, 3f);
        dead.Health.ApplyDamage(dead.Health.Current, 0f);
        Assert.That(skills.Cast(0, 0f, auto: false), Is.False);
        Assert.That(skills.CooldownFraction(0), Is.Zero);

        Spawn(Light, 6f);
        EnemyAgent near = Spawn(Light, 3f);
        _pets.Ingest(new WorldSnapshot(Capacity) { PlayerPosition = new Vector3(-8f, 0f, 0f) });
        Assert.That(skills.Cast(0, 0f, auto: false), Is.False, "Both candidates are outside the leash.");
        _pets.Ingest(new WorldSnapshot(Capacity));
        Assert.That(skills.Cast(0, 0f, auto: false), Is.True);
        Assert.That(_events.Single<PetPounced>().EnemyId, Is.EqualTo(near.Id));
    }

    [Test]
    public void Fallen_Heeling_OrLeaping_Refused()
    {
        Spawn(Light, 4f);
        PetAgent pet = _pets.Pets[0];
        Assert.That(_handler.CanCast(_pounce), Is.True, "A healthy pet idle at heel can cast.");
        Assert.That(_pets.SwapTo(pet.Position, 0f, 0.25f), Is.True);
        Assert.That(_handler.CanCast(_pounce), Is.False);
        Tick(0.25f);

        _pets.ApplyDamage(pet.Id, 69f, 1f, 0);
        Tick(1f);
        Assert.That(pet.State, Is.EqualTo(PetState.Heel));
        Assert.That(_handler.CanCast(_pounce), Is.False);

        _pets.ApplyDamage(pet.Id, 100f, 2f, 0);
        Assert.That(pet.State, Is.EqualTo(PetState.Fallen));
        Assert.That(_handler.CanCast(_pounce), Is.False);
    }

    [Test]
    public void Leap_PublishesPounced_DoesNotBite_AndIsNotHuntable()
    {
        PetAgent pet = _pets.Pets[0];
        Vector3 from = pet.Position;
        EnemyAgent hunter = _enemies.Spawn(Light, from - Vector3.UnitZ);
        Tick(0f);
        var snapshot = new WorldSnapshot(Capacity);
        _enemies.Ingest(snapshot, pets: _pets, now: 0f);
        Assert.That(hunter.Blackboard.QuarryPetId, Is.EqualTo(pet.Id));

        EnemyAgent otherHunter = _enemies.Spawn(Light, hunter.Position);
        _enemies.Mark(otherHunter.Id, 2f);
        const float launch = 0.5f;
        Tick(launch);
        Assert.That(_pets.BitWithin(pet.Id, otherHunter.Id, launch, PetSystem.HuntMemory), Is.True);
        EnemyAgent target = Spawn(Light, 4f);
        _events.Clear();

        Assert.That(_pets.TryPounce(_pounce, launch, _enemies, _combat), Is.True);
        PetPounced pounced = _events.Single<PetPounced>();
        Assert.That((pounced.PetId, pounced.EnemyId, pounced.From, pounced.To),
            Is.EqualTo((pet.Id, target.Id, from, target.Position)));
        Assert.That(_intents.PetLeaps, Has.Count.EqualTo(1));
        Assert.That(_intents.PetLeaps[0].To, Is.EqualTo(target.Position));
        Assert.That(_intents.PetLeaps[0].Seconds, Is.EqualTo(LeapSeconds));

        float airborne = launch + LeapSeconds / 2f;
        Tick(airborne);
        _enemies.Ingest(snapshot, pets: _pets, now: airborne);
        Assert.That(_pets.HuntersOf(pet.Id), Is.Zero);
        Assert.That(hunter.Blackboard.QuarryIsAPet, Is.False);
        Assert.That(otherHunter.Blackboard.QuarryIsAPet, Is.False);
        Assert.That(_events.Of<PetBit>(), Is.Empty);
        Assert.That(_events.Of<PetLanded>(), Is.Empty);
    }

    [Test]
    public void Land_DoubleBite_StunsOnlyLight_AndSkipsDead()
    {
        (ContentId Id, bool Stuns, bool Dies)[] cases =
        {
            (Light, true, false),
            (Heavy, false, false),
            (Boss, false, false),
            (Light, false, true),
        };
        foreach ((ContentId id, bool stuns, bool dies) in cases)
        {
            _enemies = Enemies(_events);
            _pets.SpawnBeside(Vector3.Zero, 0f);
            Vector3 position = _pets.Pets[0].Position + 4f * Vector3.UnitX;
            EnemyAgent target = id == Boss ? _enemies.SpawnBoss(Boss, position) : _enemies.Spawn(id, position);
            _enemies.Mark(target.Id, 2f);
            Assert.That(_pets.TryPounce(_pounce, 0f, _enemies, _combat), Is.True);
            if (dies)
            {
                target.Health.ApplyDamage(target.Health.Current, 0f);
            }

            float before = target.Health.Current;
            _events.Clear();
            Tick(LeapSeconds);
            Assert.That(_events.Single<PetLanded>().EnemyId, Is.EqualTo(target.Id));
            Assert.That(target.Health.Current, Is.EqualTo(before - (dies ? 0f : 14f)));
            Assert.That(target.CrowdControl.IsStunned, Is.EqualTo(stuns));
            if (stuns)
            {
                Assert.That(_events.Single<EnemyStatusChanged>().Seconds, Is.EqualTo(0.8f));
            }
        }
    }

    [Test]
    public void AfterLanding_HuntsTarget()
    {
        EnemyAgent target = Spawn(Light, 4f);
        Assert.That(_pets.TryPounce(_pounce, 0f, _enemies, _combat), Is.True);
        Spawn(Light, 4.1f);
        Tick(LeapSeconds);
        Tick(LeapSeconds + 0.01f);
        Assert.That(_pets.Pets[0].State, Is.EqualTo(PetState.Hunt));
        Assert.That(_pets.Pets[0].QuarryId, Is.EqualTo(target.Id));
    }

    [Test]
    public void Blackboard_CountsCandidates_AndAutoCastsAtOneWithSharedGap()
    {
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var random = new FixedRandom(99);
        var session = new RunSession(Catalog(mode), random, _events, _intents,
            new RunRecorder(random, new FixedClock(default), _events), Capacity, Capacity, Capacity);
        session.Start(new RunConfig(mode.Id, _character.Id, 99, 1, SpawnPlan.Empty, restore: null));
        RunState state = session.State;
        SkillRunner skills = ReadState<SkillRunner>(state, "Skills");
        EnemySystem enemies = ReadState<EnemySystem>(state, "Enemies");
        CombatBlackboard board = ReadState<PlayerCombat>(state, "Combat").Blackboard;
        skills.Add(Skill(_pounce));
        skills.Add(Skill(_pounce, "skill.other_pounce"));
        Vector3 from = _events.Of<PetSpawned>()[^1].Position;
        EnemyAgent first = enemies.Spawn(Light, from + new Vector3(3f, 100f, 0f));
        EnemyAgent second = enemies.Spawn(Light, from + new Vector3(6f, 100f, 0f));
        var snapshot = new WorldSnapshot(Capacity) { Dt = 0.125f };
        session.Tick(snapshot);
        Assert.That(board.MarkedInPounceReach, Is.Zero);
        Assert.That(_events.Of<SkillCast>(), Is.Empty);

        enemies.Mark(first.Id, 2f);
        session.Tick(snapshot);
        Assert.That(board.MarkedInPounceReach, Is.EqualTo(1));
        Assert.That(_events.Single<SkillCast>().WasAuto, Is.True);
        Assert.That(state.SkillCooldownSeconds(0), Is.EqualTo(8f));

        enemies.Mark(second.Id, 2f);
        snapshot.Dt = 0.375f;
        session.Tick(snapshot);
        Assert.That(board.MarkedInPounceReach, Is.EqualTo(2));
        snapshot.Dt = 0.0625f;
        session.Tick(snapshot);
        Assert.That(board.MarkedInPounceReach, Is.EqualTo(1));
        Assert.That(_events.Of<SkillCast>(), Has.Count.EqualTo(1));
        session.Tick(snapshot);
        Assert.That(_events.Of<SkillCast>(), Has.Count.EqualTo(2));
        session.End();
    }

    [Test]
    public void Pounce_DoesNotAllocate()
    {
        var events = new SilentEvents();
        EnemySystem enemies = Enemies(events);
        var intents = new RecordingIntents();
        var combat = new PlayerCombat(_character, events, intents, Capacity);
        var pets = new PetSystem(_character.Pet, events, intents);
        EnemyAgent target = enemies.Spawn(Light, new Vector3(4f, 0f, -1.5f));
        target.Health.MaxHp.Base = 1e8f;
        target.Health.Reset();
        enemies.Mark(target.Id, 2f);

        bool cast = false;
        AllocationAssert.None(() =>
        {
            intents.Clear();
            pets.SpawnBeside(Vector3.Zero, 0f);
            cast = pets.TryPounce(_pounce, 0f, enemies, combat);
            pets.Tick(0f, LeapSeconds, Vector3.Zero, enemies, combat);
        });
        Assert.That(cast, Is.True);
        Assert.That(target.Health.Current, Is.LessThan(1e8f));
    }

    private EnemyAgent Spawn(ContentId id, float distance, bool marked = true)
    {
        EnemyAgent enemy = _enemies.Spawn(id, _pets.Pets[0].Position + new Vector3(distance, 100f, 0f));
        if (marked)
        {
            _enemies.Mark(enemy.Id, 2f);
        }

        return enemy;
    }

    private void Tick(float now) => _pets.Tick(0f, now, Vector3.Zero, _enemies, _combat);

    private static T ReadState<T>(RunState state, string property) =>
        (T)typeof(RunState).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);

    private EnemySystem Enemies(IDomainEvents events) =>
        new(Catalog(), events, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);

    private ContentCatalog Catalog(ModeSpec mode = null) => new(new[] { _character },
        new[] { Enemy(Light, EnemyBehaviourKind.Chaser, true), Enemy(Heavy, EnemyBehaviourKind.Static),
            Enemy(Body, EnemyBehaviourKind.Boss) }, mode is null ? null : new[] { mode },
        bosses: new[] { new BossSpec(Boss, Body, new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) }, 1.5f) });

    private static EnemySpec Enemy(ContentId id, EnemyBehaviourKind kind, bool light = false) =>
        new(id, new LocKey("enemy.test.name"), 100f, 2f, 1, 4, 5f,
            false, 8f, 1.2f, 0.2f, 0.2f, 30f, kind, isLight: light);

    private static SkillSpec Skill(Pounce pounce, string id = "skill.pounce") =>
        new(new ContentId(id), new LocKey("skill.pounce.name"), new LocKey("skill.pounce.desc"),
            SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(8f,
                new TriggerSpec(new[] { new TriggerClause(TriggerField.MarkedInPounceReach, TriggerComparison.AtLeast, 1f) }),
                new IEffect[] { pounce }));
}
