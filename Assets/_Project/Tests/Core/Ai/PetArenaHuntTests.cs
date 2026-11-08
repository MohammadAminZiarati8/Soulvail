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

namespace Soulvail.Tests.Core.Ai;

/// <summary>BT-03: animals hunt the whole arena, the last throw's marked target first.</summary>
[TestFixture]
public sealed class PetArenaHuntTests
{
    private const int Capacity = 8;
    private const float Dt = 0.02f;
    private static readonly ContentId CharacterId = new("character.beastmaster");
    private static readonly ContentId ModeId = new("mode.arena_hunt");
    private static readonly ContentId Husk = new("enemy.husk");

    // The throw goes at the enemy nearest the player; the other sits nearer the animal.
    private static readonly Vector3 ThrowAt = new(0f, 0f, 4f);
    private static readonly Vector3 NearPet = new(0f, 0f, -5f);

    private RecordingEvents _events;
    private RecordingIntents _intents;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
    }

    [Test]
    public void Quarry_EnemyTwentyMetresAway_Hunted()
    {
        (PetSystem pets, EnemySystem enemies, PlayerCombat combat) = Standalone();
        EnemyAgent far = enemies.Spawn(Husk, 20f * Vector3.UnitX);

        pets.Tick(Dt, 0f, Vector3.Zero, enemies, combat);

        PetAgent pet = pets.Pets[0];
        Assert.That(pet.QuarryId, Is.EqualTo(far.Id));
        Assert.That(pet.State, Is.EqualTo(PetState.Hunt));
        Assert.That(_intents.PetMoves[^1].Velocity.X, Is.GreaterThan(0f), "It runs at the enemy.");
    }

    [Test]
    public void Pounce_TargetFarFromPlayer_Allowed()
    {
        (PetSystem pets, EnemySystem enemies, PlayerCombat combat) = Standalone();
        Vector3 pet = pets.Pets[0].Position;
        EnemyAgent target = enemies.Spawn(Husk, pet + 4f * Vector3.UnitX);
        enemies.Mark(target.Id, 10f);
        pets.Ingest(new WorldSnapshot(Capacity) { PlayerPosition = new Vector3(-20f, 0f, 0f) });

        Assert.That(pets.TryPounce(new Pounce(3f, 6f, 2f, 0.8f, 0.4f), 0f, enemies, combat), Is.True);
        Assert.That(_events.Single<PetPounced>().EnemyId, Is.EqualTo(target.Id));
    }

    [Test]
    public void Quarry_ThrowTargetMarked_BeatsNearerMarked()
    {
        Hunt hunt = StartHunt();
        hunt.Enemies.Mark(hunt.Other, 30f);
        hunt.Step();
        Assert.That(hunt.Pet.QuarryId, Is.EqualTo(hunt.Other), "Precondition: the marked enemy nearer the animal.");

        hunt.StepUntilThrow();
        Assert.That(hunt.Pet.QuarryId, Is.EqualTo(hunt.Other), "Thrown at unmarked: no switch.");

        hunt.Enemies.Mark(hunt.Thrown, 30f);
        hunt.StepFor(PetSystem.RetargetInterval + Dt);
        Assert.That(hunt.Pet.QuarryId, Is.EqualTo(hunt.Thrown));
    }

    [Test]
    public void Quarry_ThrowTargetUnmarked_NearestMarked()
    {
        Hunt hunt = StartHunt();
        hunt.Enemies.Mark(hunt.Other, 30f);
        hunt.StepUntilThrow();
        hunt.StepFor(PetSystem.RetargetInterval + Dt);

        Assert.That(hunt.Pet.QuarryId, Is.EqualTo(hunt.Other));
    }

    [Test]
    public void ThrowTargetChanges_HuntersSwitchSameTick()
    {
        Hunt hunt = StartHunt();
        hunt.Enemies.Mark(hunt.Thrown, 30f);
        hunt.Enemies.Mark(hunt.Other, 30f);
        hunt.Step();
        Assert.That(hunt.Pet.QuarryId, Is.EqualTo(hunt.Other), "Precondition: the marked enemy nearer the animal.");

        hunt.StepUntilThrow();
        Assert.That(hunt.Pet.QuarryId, Is.EqualTo(hunt.Thrown), "Switched on the throw's own tick.");
        Assert.That(_events.Of<PetRetargeted>()[^1].EnemyId, Is.EqualTo(hunt.Thrown));
    }

    [Test]
    public void ThrowTargetChanges_HeelingPetStays()
    {
        Hunt hunt = StartHunt();
        hunt.Enemies.Mark(hunt.Thrown, 30f);
        hunt.Enemies.Mark(hunt.Other, 30f);
        hunt.Step();
        hunt.Pets.ApplyDamage(hunt.Pet.Id, 0.7f * hunt.Pet.MaxHp.Value, hunt.Session.State.Time, 0);
        hunt.Step();
        Assert.That(_events.Of<PetHeeled>(), Has.Count.EqualTo(1), "Precondition: at heel.");

        hunt.StepUntilThrow();
        Assert.That(hunt.Pet.QuarryId, Is.Zero);
        Assert.That(hunt.Pet.State, Is.EqualTo(PetState.Heel));
    }

    [Test]
    public void Hawk_DivesAtThrowTarget()
    {
        // The hawk circles from +X toward +Z, so by its first dive it is over the other enemy.
        Hunt hunt = StartHunt(throwAt: new Vector3(0f, 0f, -2.5f), other: new Vector3(0f, 0f, 4f));
        var trigger = new TriggerSpec(new[] { new TriggerClause(TriggerField.MarkedEnemies, TriggerComparison.AtLeast, 1f) });
        ReadState<SkillRunner>(hunt.Session.State, "Skills").Add(new SkillSpec(
            new ContentId("skill.hawk"), new LocKey("skill.hawk.name"), new LocKey("skill.hawk.desc"),
            SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(30f, trigger, new IEffect[] { new CallHawk(2f, 0.4f) })));
        hunt.Enemies.Mark(hunt.Thrown, 30f);
        hunt.Enemies.Mark(hunt.Other, 30f);

        hunt.StepUntilThrow();
        for (int i = 0; i < 300 && _events.Count<HawkDove>() == 0; i++)
        {
            hunt.Step();
        }

        HawkDove dive = _events.Single<HawkDove>();
        Vector3 other = hunt.Enemies.Registry.TryGet(hunt.Other, out EnemyAgent agent) ? agent.Position : default;
        Assert.That(Vector3.Distance(dive.From, other), Is.LessThan(Vector3.Distance(dive.From, dive.To)),
            "Precondition: the other marked enemy was nearer the hawk.");
        Assert.That(dive.EnemyId, Is.EqualTo(hunt.Thrown));
    }

    private (PetSystem, EnemySystem, PlayerCombat) Standalone()
    {
        CharacterSpec character = Character();
        var catalog = new ContentCatalog(new[] { character }, new[] { HuskSpec() });
        var enemies = new EnemySystem(catalog, _events, new FixedRandom(0.1f), new DepthScaling(Scalings.Design()), Capacity);
        var combat = new PlayerCombat(character, _events, _intents, Capacity);
        var pets = new PetSystem(character.Pet, _events, _intents);
        pets.SpawnBeside(Vector3.Zero, 0f);
        return (pets, enemies, combat);
    }

    private Hunt StartHunt() => StartHunt(ThrowAt, NearPet);

    private Hunt StartHunt(Vector3 throwAt, Vector3 other)
    {
        CharacterSpec character = Character();
        var mode = new ModeSpec(ModeId, new LocKey("mode.arena_hunt.name"), 1, true, 0,
            Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var catalog = new ContentCatalog(new[] { character }, new[] { HuskSpec() }, new[] { mode });
        var random = new FixedRandom(99);
        var session = new RunSession(catalog, random, _events, _intents,
            new RunRecorder(random, new FixedClock(default), _events), Capacity, Capacity, Capacity);
        session.Start(new RunConfig(ModeId, CharacterId, 99, 1, new SpawnPlan(new[]
        {
            new SpawnPlan.Entry(Husk, throwAt),
            new SpawnPlan.Entry(Husk, other),
        }), restore: null));

        var snapshot = new WorldSnapshot(Capacity) { Dt = Dt };
        foreach (EnemySpawned spawned in _events.Of<EnemySpawned>())
        {
            snapshot.AddEnemy() = new EnemySense { Id = spawned.Id, Position = spawned.Position, HasLineOfSight = true };
        }

        return new Hunt(session, snapshot, _events.Of<EnemySpawned>()[0].Id, _events.Of<EnemySpawned>()[1].Id);
    }

    private static T ReadState<T>(RunState state, string property) =>
        (T)typeof(RunState).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);

    private static CharacterSpec Character() => new(
        CharacterId, new LocKey("character.beastmaster.name"), new LocKey("character.beastmaster.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.ReturningAxe, 10f, 2f, 8f, 360f, 0.4f,
            shotRadius: 0.3f, axeSpeed: 8f, axeCarry: 1, axeHitsPerPass: 3),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Swap, 8f, 0.25f, 6f, 0.15f, 0f, 0f, 0.2f),
        pet: new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 1f, 1.4f, 1.5f));

    private static EnemySpec HuskSpec() => new(Husk, new LocKey("enemy.husk.name"), 100000f, 2f, 1, 4, 5f,
        false, 8f, 1.2f, 0.2f, 0.2f, 30f, EnemyBehaviourKind.Static);

    private sealed class Hunt
    {
        private readonly WorldSnapshot _snapshot;

        public Hunt(RunSession session, WorldSnapshot snapshot, int thrown, int other)
        {
            Session = session;
            _snapshot = snapshot;
            Thrown = thrown;
            Other = other;
            Pets = ReadState<PetSystem>(session.State, "Pets");
            Enemies = ReadState<EnemySystem>(session.State, "Enemies");
            Combat = ReadState<PlayerCombat>(session.State, "Combat");
        }

        public RunSession Session { get; }
        public PetSystem Pets { get; }
        public EnemySystem Enemies { get; }
        public PlayerCombat Combat { get; }
        public int Thrown { get; }
        public int Other { get; }
        public PetAgent Pet => Pets.Pets[0];

        public void Step() => Session.Tick(_snapshot);

        public void StepFor(float seconds)
        {
            for (float t = 0f; t < seconds; t += Dt)
            {
                Step();
            }
        }

        public void StepUntilThrow()
        {
            for (int i = 0; i < 200 && Combat.LastThrowTargetId == 0; i++)
            {
                Step();
            }

            Assert.That(Combat.LastThrowTargetId, Is.EqualTo(Thrown), "Precondition: the throw went at the nearer enemy.");
        }
    }
}
