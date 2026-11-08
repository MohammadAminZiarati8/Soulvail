using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class PackHoundTests
{
    private const int Capacity = 16;
    private static readonly ContentId CharacterId = new("character.beastmaster");
    private static readonly ContentId EnemyId = new("enemy.pack_target");
    private static readonly ContentId ModeId = new("mode.pack_test");
    private static readonly PetSpec Hound = new(new ContentId("pet.hound"), 70f, 4f, 7f, 0.25f, 1.4f, 8f, 10f, 1.5f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _combat;
    private PetSystem _pets;

    private PetAgent Lead => _pets.Pets[0];
    private Stat PackHounds => _pets.LeadStats.Resolve(PlayerStat.PackHounds);
    private Stat PackShare => _pets.LeadStats.Resolve(PlayerStat.PackShare);

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
    }

    [Test]
    public void PackSize_SpawnsBesidePlayer_ClampsAndCutsNewest()
    {
        Assert.That(_pets.Pets.Length, Is.EqualTo(1));
        Assert.That(_events.Single<PetSpawned>().IsLead, Is.True);

        Vector3 player = Vector3.UnitX * 4f;
        _pets.Ingest(new WorldSnapshot(Capacity) { PlayerPosition = player });
        PackHounds.Base = 1.9f;

        PetAgent first = _pets.Pets[1];
        Assert.That(_pets.Pets.Length, Is.EqualTo(2));
        Assert.That(first.IsLead, Is.False);
        Assert.That(_events.Of<PetSpawned>()[1].IsLead, Is.False);
        Assert.That(Vector3.Distance(first.Position, player), Is.EqualTo(Hound.HeelDistance).Within(0.001f));

        PackHounds.Base = 9f;
        Assert.That(_pets.Pets.Length, Is.EqualTo(3));
        int newestId = _pets.Pets[2].Id;
        Assert.That(_pets.Pets[2].Position, Is.Not.EqualTo(first.Position));

        PackHounds.Base = 1f;
        Assert.That(_events.Single<PetDespawned>().Id, Is.EqualTo(newestId));
        Assert.That(_pets.Pets.Length, Is.EqualTo(2));
        Assert.That(_pets.Pets[1], Is.SameAs(first));

        PackHounds.Base = -1f;
        Assert.That(_pets.Pets.Length, Is.EqualTo(1));
        Assert.That(_events.Of<PetDespawned>()[1].Id, Is.EqualTo(first.Id));
    }

    [Test]
    public void Share_FollowsLeadGrowth_HealsGainsAndClampsCuts()
    {
        PackHounds.Base = 1f;
        PetAgent hound = _pets.Pets[1];
        Assert.That(hound.MaxHp.Value, Is.EqualTo(35f));
        Assert.That(hound.ContactDamage.Value, Is.EqualTo(3.5f));

        _pets.ApplyDamage(hound.Id, 10f, 0f, 1);
        Lead.MaxHp.Base = 80f;
        Lead.ContactDamage.Base = 8f;
        Assert.That(hound.MaxHp.Value, Is.EqualTo(40f));
        Assert.That(hound.Health.Current, Is.EqualTo(30f));
        Assert.That(hound.ContactDamage.Value, Is.EqualTo(4f));

        PackShare.Base = 0.75f;
        Assert.That(hound.Health.Current, Is.EqualTo(50f));
        Assert.That(hound.ContactDamage.Value, Is.EqualTo(6f));

        PackShare.Base = 0.25f;
        Assert.That(hound.MaxHp.Value, Is.EqualTo(20f));
        Assert.That(hound.Health.Current, Is.EqualTo(20f));
        Assert.That(hound.ContactDamage.Value, Is.EqualTo(2f));
    }

    [Test]
    public void PackHound_TargetsAndHunts_FallsAndRecovers_WithoutLeadSkills()
    {
        PackHounds.Base = 1f;
        ReportPet(Lead, Vector3.UnitX * -5f);
        _pets.LeadStats.Resolve(PlayerStat.PetBiteStunEvery).Base = 1f;
        PetAgent hound = _pets.Pets[1];
        EnemyAgent nearer = _enemies.Spawn(EnemyId, hound.Position);
        EnemyAgent marked = _enemies.Spawn(EnemyId, hound.Position + Vector3.UnitX * 0.1f);
        EnemyAgent outside = _enemies.Spawn(EnemyId, Vector3.UnitX * 9f);
        _enemies.Mark(marked.Id, 4f);
        _enemies.Mark(outside.Id, 4f);

        var pounce = new Pounce(3f, 7f, 2f, 1f, 1.2f);
        Assert.That(_pets.TryPounce(pounce, 0f, _enemies, _combat), Is.True);
        Tick(0f);
        Assert.That(_events.Single<PetPounced>().PetId, Is.EqualTo(Lead.Id));
        Assert.That(_events.Single<PetBit>().PetId, Is.EqualTo(hound.Id));
        Assert.That(hound.QuarryId, Is.EqualTo(marked.Id));
        Assert.That(marked.CrowdControl.IsStunned, Is.False);
        Perceive(0f);
        Assert.That(marked.Blackboard.QuarryPetId, Is.EqualTo(hound.Id));

        _enemies.Mark(nearer.Id, 4f);
        Tick(0.5f);
        Perceive(0.5f);
        Assert.That(nearer.Blackboard.QuarryPetId, Is.EqualTo(hound.Id));

        EnemyAgent third = _enemies.Spawn(EnemyId, hound.Position - Vector3.UnitX * 0.1f);
        ReportPet(hound, third.Position);
        _enemies.Mark(third.Id, 4f);
        Tick(1f);
        Perceive(1f);
        Assert.That(_pets.BitWithin(hound.Id, third.Id, 1f, PetSystem.HuntMemory), Is.True);
        Assert.That(third.Blackboard.QuarryIsAPet, Is.False);

        _pets.ApplyDamage(hound.Id, hound.MaxHp.Value, 1.1f, marked.Id);
        Assert.That(hound.State, Is.EqualTo(PetState.Fallen));
        Assert.That(_pets.HuntersOf(hound.Id), Is.Zero);
        Tick(3.1f, Hound.RescueSeconds);
        Assert.That(hound.State, Is.EqualTo(PetState.Heel));
        float riseHp = hound.Health.Current;
        Tick(3.2f);
        Assert.That(hound.Health.Current, Is.GreaterThan(riseHp));
    }

    [Test]
    public void StageArrived_AllPetsReturnAtFullHealth()
    {
        PackHounds.Base = 2f;
        foreach (PetAgent pet in _pets.Pets)
        {
            _pets.ApplyDamage(pet.Id, pet.MaxHp.Value, 0f, 1);
        }

        // RunSession uses SpawnBeside on arrival.
        Vector3 player = Vector3.UnitX * 4f;
        _pets.SpawnBeside(player, 1f);

        Assert.That(_pets.Pets.Length, Is.EqualTo(3));
        foreach (PetAgent pet in _pets.Pets)
        {
            Assert.That(pet.Health.Fraction, Is.EqualTo(1f));
            Assert.That(pet.State, Is.EqualTo(PetState.Heel));
            Assert.That(Vector3.Distance(pet.Position, player), Is.EqualTo(Hound.HeelDistance).Within(0.001f));
        }
    }

    [Test]
    public void SwapAndDropHunters_TouchLeadOnly()
    {
        PackHounds.Base = 1f;
        PetAgent hound = _pets.Pets[1];
        Vector3 houndPosition = hound.Position;
        ReportPet(Lead, Vector3.UnitX * -5f);
        EnemyAgent leadHunter = _enemies.Spawn(EnemyId, Lead.Position);
        EnemyAgent packHunter = _enemies.Spawn(EnemyId, houndPosition);
        _enemies.Mark(leadHunter.Id, 4f);
        _enemies.Mark(packHunter.Id, 4f);
        Tick(0f);
        Perceive(0f);
        Assert.That(leadHunter.Blackboard.QuarryPetId, Is.EqualTo(Lead.Id));
        Assert.That(packHunter.Blackboard.QuarryPetId, Is.EqualTo(hound.Id));

        _pets.DropHunters(hound.Id);
        Assert.That(_pets.HuntersOf(hound.Id), Is.EqualTo(1));

        Vector3 playerFrom = Vector3.UnitX;
        Vector3 leadFrom = Lead.Position;
        Assert.That(_pets.SwapTo(playerFrom, 0f, 0.5f), Is.True);
        Swapped swap = _events.Single<Swapped>();
        Assert.That(swap.PetId, Is.EqualTo(Lead.Id));
        Assert.That(swap.PlayerFrom, Is.EqualTo(playerFrom));
        Assert.That(swap.PlayerTo, Is.EqualTo(leadFrom));
        Assert.That(_intents.PetLeaps.Count, Is.EqualTo(1));
        Assert.That(hound.Position, Is.EqualTo(houndPosition));
        Assert.That(_pets.HuntersOf(Lead.Id), Is.Zero);
        Assert.That(_pets.HuntersOf(hound.Id), Is.EqualTo(1));
    }

    [Test]
    public void Rescue_FillsTwoFallenAtOnce()
    {
        PackHounds.Base = 2f;
        PetAgent first = _pets.Pets[1];
        PetAgent second = _pets.Pets[2];
        _pets.ApplyDamage(first.Id, first.MaxHp.Value, 0f, 1);
        _pets.ApplyDamage(second.Id, second.MaxHp.Value, 0f, 1);

        Tick(1f, Hound.RescueSeconds / 2f);
        Assert.That(first.RescueProgress, Is.EqualTo(0.5f));
        Assert.That(second.RescueProgress, Is.EqualTo(0.5f));

        Tick(2f, Hound.RescueSeconds / 2f);
        Assert.That(first.State, Is.EqualTo(PetState.Heel));
        Assert.That(second.State, Is.EqualTo(PetState.Heel));
        Assert.That(_events.Count<PetRose>(), Is.EqualTo(2));
    }

    [Test]
    public void PackHoundKill_PaysXp()
    {
        PackHounds.Base = 1f;
        PetAgent hound = _pets.Pets[1];
        ReportPet(Lead, Vector3.UnitX * -5f);
        EnemyAgent enemy = _enemies.Spawn(EnemyId, hound.Position);
        enemy.Health.ApplyDamage(enemy.Health.Current - 1f, 0f);

        Tick(0f);

        Assert.That(_events.Single<PetBit>().PetId, Is.EqualTo(hound.Id));
        Assert.That(enemy.IsAlive, Is.False);
        Assert.That(_enemies.DrainXp(), Is.EqualTo(5f));
    }

    [Test]
    public void TickAndSpawn_DoNotAllocate()
    {
        var events = new SilentEvents();
        var pets = new PetSystem(Hound, events, _intents);
        pets.SpawnBeside(Vector3.Zero, 0f);
        Stat count = pets.LeadStats.Resolve(PlayerStat.PackHounds);
        var enemies = new EnemySystem(Catalog(Character()), events, new FixedRandom(0.1f),
            new DepthScaling(Scalings.Design()), Capacity);
        enemies.Spawn(EnemyId, pets.Pets[0].Position);

        AllocationAssert.None(() =>
        {
            _intents.Clear();
            count.Base = 2f;
            pets.Tick(0.1f, 0f, Vector3.Zero, enemies, _combat);
            count.Base = 0f;
        });
    }

    private void Tick(float now, float dt = 0.1f) => _pets.Tick(dt, now, Vector3.Zero, _enemies, _combat);

    private void ReportPet(PetAgent pet, Vector3 position)
    {
        var snapshot = new WorldSnapshot(Capacity);
        ref EnemySense sense = ref snapshot.AddPet();
        sense.Id = pet.Id;
        sense.Position = position;
        _pets.Ingest(snapshot);
    }

    private void Perceive(float now)
    {
        var snapshot = new WorldSnapshot(Capacity);
        foreach (EnemyAgent enemy in _enemies.Registry.Alive)
        {
            ref EnemySense sense = ref snapshot.AddEnemy();
            sense.Id = enemy.Id;
            sense.Position = enemy.Position;
            sense.HasLineOfSight = true;
        }

        _enemies.Ingest(snapshot, pets: _pets, now: now);
    }

    private static CharacterSpec Character() => new(
        CharacterId, new LocKey("character.beastmaster.name"), new LocKey("character.beastmaster.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f), pet: Hound);

    private static ContentCatalog Catalog(CharacterSpec character) => new(
        new[] { character },
        new[]
        {
            new EnemySpec(EnemyId, new LocKey("enemy.pack_target.name"), 14f, 2f, 1, 4, 5f,
                false, 8f, 1.2f, 0.4f, 0.6f, 30f, EnemyBehaviourKind.Chaser, isLight: true),
        },
        new[]
        {
            new ModeSpec(ModeId, new LocKey("mode.pack_test.name"), 1, true, 0,
                new ScalingSpec(new BudgetCurve(4f, 0f, 0f), new WaveCurve(1, 1000, 1, 1),
                    new ConcurrencyCurve(1, 1000), new StatCurve(0.06f, 4f, 1, 1),
                    new StatCurve(0.035f, 3f, 1, 1), new StatCurve(0.02f, 1.3f, 5, 0)),
                Scalings.Xp(), new[] { new RosterEntry(EnemyId, 1) }, hasSanctum: false),
        });
}
