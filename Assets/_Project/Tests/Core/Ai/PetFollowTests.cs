using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>BT-04: out of a fight the pack walks its slots, through <see cref="PetSystem.Tick"/>.</summary>
[TestFixture]
public sealed class PetFollowTests
{
    private const int Capacity = 16;
    private const float Dt = 0.05f;
    private static readonly ContentId CharacterId = new("character.beastmaster");
    private static readonly ContentId EnemyId = new("enemy.follow_target");
    private static readonly PetSpec Hound = new(new ContentId("pet.hound"), 70f, 4f, 7f, 1f, 1.4f, 1.5f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _combat;
    private PetSystem _pets;
    private WorldSnapshot _snapshot;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        CharacterSpec character = Character();
        _combat = new PlayerCombat(character, _events, _intents, Capacity);
        _enemies = new EnemySystem(Catalog(character), _events, new FixedRandom(0.1f),
            new DepthScaling(Scalings.Design()), Capacity);
        _pets = new PetSystem(Hound, _events, _intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
        _pets.LeadStats.Resolve(PlayerStat.PackHounds).Base = 2f;
        _snapshot = new WorldSnapshot(Capacity);
        _now = 0f;
    }

    [Test]
    public void Idle_PetsSettleOnSlots()
    {
        Assert.That(_pets.Pets.Length, Is.EqualTo(3), "Precondition: lead and two hounds.");

        StepFor(6f);

        for (int index = 0; index < _pets.Pets.Length; index++)
        {
            Vector3 slot = Slot(index);
            Assert.That(Vector3.Distance(_pets.Pets[index].Position, slot), Is.LessThanOrEqualTo(PackFormation.DeadZone),
                $"Pet {index} on its slot.");
        }

        Assert.That(Vector3.Distance(_pets.Pets[0].Position, Vector3.Zero), Is.GreaterThan(1f), "Beside him, not on him.");
        for (int a = 0; a < _pets.Pets.Length; a++)
        {
            for (int b = a + 1; b < _pets.Pets.Length; b++)
            {
                Assert.That(Vector3.Distance(_pets.Pets[a].Position, _pets.Pets[b].Position),
                    Is.GreaterThanOrEqualTo(PackFormation.Spacing), $"Pets {a} and {b} not stacked.");
            }
        }
    }

    [Test]
    public void WithinDeadZone_NoMove()
    {
        PetAgent lead = _pets.Pets[0];
        Report(lead, Slot(0) + new Vector3(0.3f, 0f, 0.2f));

        Step();

        Assert.That(LastMove(lead.Id).Velocity, Is.EqualTo(Vector3.Zero));

        Report(lead, Slot(0) + new Vector3(0.8f, 0f, 0f));
        Step();
        Vector3 easing = LastMove(lead.Id).Velocity;
        Assert.That(easing.X, Is.LessThan(0f), "Outside the dead zone it walks back.");
        Assert.That(easing.Length(), Is.EqualTo(PackFormation.ArrivalSpeed(0.8f, Hound.MoveSpeed)).Within(1e-3f),
            "Eased, not at full speed.");
    }

    [Test]
    public void Separation_FallenSkipped()
    {
        PetAgent lead = _pets.Pets[0];
        PetAgent fallen = _pets.Pets[1];
        PetAgent standing = _pets.Pets[2];
        Report(fallen, Slot(0));
        _pets.ApplyDamage(fallen.Id, 10f * fallen.MaxHp.Value, _now, 0);
        Assert.That(fallen.State, Is.EqualTo(PetState.Fallen), "Precondition.");

        Report(lead, Slot(0));
        Report(standing, Slot(2));
        Step();
        Assert.That(LastMove(lead.Id).Velocity, Is.EqualTo(Vector3.Zero), "A body under it does not push.");

        Report(standing, Slot(0) + new Vector3(0.3f, 0f, 0f));
        Step();
        Assert.That(LastMove(lead.Id).Velocity.X, Is.LessThan(0f), "A standing animal on it does.");
    }

    [Test]
    public void Tick_NoAllocation()
    {
        Step();
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            _pets.Tick(Dt, _now, Vector3.Zero, _enemies, _combat, Vector3.UnitX);
            _now += Dt;
        });
    }

    private Vector3 Slot(int index)
    {
        Vector2 offset = _pets.FormationFacing == Vector2.UnitY ? PackFormation.SlotOffset(index)
            : throw new AssertionException("These rows keep him facing +Z.");
        return new Vector3(offset.X, 0f, offset.Y);
    }

    private void Report(PetAgent pet, Vector3 position)
    {
        _snapshot.Clear();
        ref EnemySense sense = ref _snapshot.AddPet();
        sense.Id = pet.Id;
        sense.Position = position;
        _pets.Ingest(_snapshot);
    }

    private void Step()
    {
        _pets.Tick(Dt, _now, Vector3.Zero, _enemies, _combat, Vector3.UnitZ);
        _now += Dt;
    }

    // Moves every standing pet by its last intent, the way its view would.
    private void StepFor(float seconds)
    {
        for (float t = 0f; t < seconds; t += Dt)
        {
            _intents.Clear();
            Step();
            _snapshot.Clear();
            foreach (PetAgent pet in _pets.Pets)
            {
                ref EnemySense sense = ref _snapshot.AddPet();
                sense.Id = pet.Id;
                sense.Position = pet.Position + LastMove(pet.Id).Velocity * Dt;
            }

            _pets.Ingest(_snapshot);
        }
    }

    private EnemyMoveIntent LastMove(int petId)
    {
        for (int i = _intents.PetMoves.Count - 1; i >= 0; i--)
        {
            if (_intents.PetMoves[i].Id == petId)
            {
                return _intents.PetMoves[i];
            }
        }

        throw new AssertionException($"Pet {petId} was given no move.");
    }

    private static ContentCatalog Catalog(CharacterSpec character) => new(
        new[] { character },
        new[]
        {
            new EnemySpec(EnemyId, new LocKey("enemy.follow_target.name"),
                14f, 2f, 1, 4, 5f, false, 8f, 1.2f, 0.4f, 0.6f, 30f, EnemyBehaviourKind.Static),
        });

    private static CharacterSpec Character() => new(
        CharacterId, new LocKey("character.beastmaster.name"), new LocKey("character.beastmaster.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        pet: Hound);
}
