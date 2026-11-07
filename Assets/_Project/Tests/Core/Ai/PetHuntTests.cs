using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class PetHuntTests
{
    private const int Capacity = 16;
    private static readonly ContentId Frog = new("enemy.frog");
    private static readonly ContentId Bullfrog = new("enemy.bullfrog");
    private static readonly ContentId Panther = new("enemy.panther");
    private static readonly ContentId Spitter = new("enemy.spitter");
    private static readonly ContentId BossBody = new("enemy.boss");
    private static readonly ContentId Boss = new("boss.hunt_test");
    private static readonly Vector3 PlayerPosition = Vector3.UnitX * 6f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private PetSystem _pets;
    private WorldSnapshot _snapshot;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;
    private IDomainEvents _domainEvents;

    private PetAgent Lead => _pets.Pets[0];

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        Build(_events);
    }

    [TestCase("enemy.frog")]
    [TestCase("enemy.panther")]
    public void Bitten_NearerThanPlayer_MeleeHunts(string id)
    {
        EnemyAgent enemy = SpawnBitten(new ContentId(id), Vector3.Zero);
        Vector3 petPosition = new(0f, 20f, 4f);
        Perceive(0f, petPosition, PlayerPosition);

        Assert.That(enemy.Blackboard.QuarryIsAPet, Is.True);
        Assert.That(enemy.Blackboard.QuarryPetId, Is.EqualTo(Lead.Id));
        Assert.That(enemy.Blackboard.PlayerPosition, Is.EqualTo(petPosition));
        Assert.That(enemy.Blackboard.DistanceToPlayer, Is.EqualTo(4f));
        Assert.That(enemy.Blackboard.PathDirectionToPlayer, Is.EqualTo(Vector2.Zero));
        Assert.That(enemy.Blackboard.DirectionToPlayer, Is.EqualTo(Vector2.UnitY));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Hunters_ReturnToPlayer_WhenBiteExpiresOrPlayerIsNearer(bool playerNearer)
    {
        EnemyAgent enemy = SpawnBitten(Frog, Vector3.Zero);
        Vector3 pet = Vector3.UnitZ * 2f;
        Step(0f, pet, PlayerPosition);
        Step(0.1f, pet, PlayerPosition);
        Assert.That(_intents.EnemyMoves[^1].Velocity.Z, Is.Positive);

        Vector3 player = playerNearer ? Vector3.UnitX : PlayerPosition;
        Perceive(playerNearer ? 0.2f : PetSystem.HuntMemory, pet, player);

        Assert.That(enemy.Blackboard.QuarryIsAPet, Is.False);
        Assert.That(enemy.Blackboard.PlayerPosition, Is.EqualTo(player));
        Assert.That(_pets.HuntersOf(Lead.Id), Is.Zero);
        var hunts = _events.Of<PetHunted>();
        Assert.That(hunts.Count, Is.EqualTo(2));
        Assert.That(hunts[0].Started, Is.True);
        Assert.That(hunts[1].Started, Is.False);
        Assert.That(hunts[1].EnemyId, Is.EqualTo(enemy.Id));
    }

    [Test]
    public void ThirdHunter_StaysOnPlayer()
    {
        EnemyAgent first = SpawnBitten(Frog, Vector3.Zero);
        EnemyAgent second = SpawnBitten(Frog, Vector3.UnitZ, 0.5f);
        EnemyAgent third = SpawnBitten(Frog, -Vector3.UnitZ, 1f);

        Perceive(1f, Vector3.Zero, PlayerPosition);
        Assert.That(first.Blackboard.QuarryIsAPet, Is.True);
        Assert.That(second.Blackboard.QuarryIsAPet, Is.True);
        Assert.That(third.Blackboard.QuarryIsAPet, Is.False);
        Assert.That(_pets.HuntersOf(Lead.Id), Is.EqualTo(PetSystem.MaxHunters));

        Perceive(2f, Vector3.Zero, PlayerPosition);
        Assert.That(first.Blackboard.QuarryIsAPet, Is.False);
        Assert.That(third.Blackboard.QuarryIsAPet, Is.True);
    }

    [TestCase("enemy.frog", 8f)]
    [TestCase("enemy.bullfrog", 12f)]
    [TestCase("enemy.panther", 8f)]
    public void HuntingMelee_HitsPetOnly(string id, float damage)
    {
        EnemyAgent enemy = SpawnBitten(new ContentId(id), Vector3.Zero);
        Vector3 pet = id == Bullfrog.Value ? Vector3.UnitZ * 3f : Vector3.UnitZ;
        float playerHp = _player.Health.Current;

        Vector3 position = enemy.Position;
        for (int frame = 0; frame < 20 && _events.Count<PetHurt>() == 0; frame++)
        {
            _intents.Clear();
            Step(frame * 0.1f, pet, Vector3.UnitZ * 5f, position);
            if (enemy.Behaviour is ChaserBehaviour { State: ChaserState.Air })
            {
                position += _intents.EnemyMoves[^1].Velocity * 0.1f;
            }
        }

        PetHurt hurt = _events.Single<PetHurt>();
        Assert.That(hurt.PetId, Is.EqualTo(Lead.Id));
        Assert.That(hurt.EnemyId, Is.EqualTo(enemy.Id));
        Assert.That(hurt.Amount, Is.EqualTo(damage).Within(1e-4f));
        Assert.That(_player.Health.Current, Is.EqualTo(playerHp));
        if (id == Bullfrog.Value)
        {
            Assert.That(_events.Count<EnemyLeapAimed>(), Is.EqualTo(1));
        }
    }

    [Test]
    public void PlayerHunter_And_Bolt_NeverHurtPet()
    {
        EnemyAgent frog = _enemies.Spawn(Frog, Vector3.Zero);
        Vector3 position = Vector3.UnitZ;
        for (int frame = 0; frame < 5; frame++)
        {
            Step(frame * 0.1f, position, position);
        }

        Assert.That(frog.Blackboard.QuarryIsAPet, Is.False);
        Assert.That(_player.Health.Current, Is.LessThan(_player.Health.MaxHp.Value));
        _projectiles.Fire(new Projectile(Spitter, frog.Id, Vector3.Zero, position, 10f, 1f, 8f), 0.5f);
        _projectiles.Tick(0.7f, position, _player, _enemies);

        Assert.That(_events.Count<ProjectileImpacted>(), Is.EqualTo(1));
        Assert.That(_events.Count<PetHurt>(), Is.Zero);
        Assert.That(Lead.Health.Current, Is.EqualTo(Lead.MaxHp.Value));
    }

    [Test]
    public void ZeroHp_Falls_FreesHunters_AndStageSpawnRevives()
    {
        EnemyAgent enemy = SpawnBitten(Frog, Vector3.Zero);
        Vector3 position = Vector3.UnitZ;
        Perceive(0.5f, position, PlayerPosition);
        int petId = Lead.Id;

        DamageResult result = _pets.ApplyDamage(petId, 100f, 0.5f, enemy.Id);
        Assert.That(result.Killed, Is.True);
        Assert.That(Lead.State, Is.EqualTo(PetState.Fallen));
        Assert.That(_events.Single<PetFell>().Position, Is.EqualTo(position));
        Assert.That(_pets.HuntersOf(petId), Is.Zero);

        _intents.Clear();
        _events.Clear();
        Perceive(3f, Vector3.UnitZ * 4f, PlayerPosition);
        _pets.Tick(0.1f, 3f, PlayerPosition, _enemies, _player);
        Assert.That(enemy.Blackboard.QuarryIsAPet, Is.False);
        Assert.That(Lead.Position, Is.EqualTo(position));
        Assert.That(_intents.PetMoves, Is.Empty);
        Assert.That(_events.Count<PetBit>(), Is.Zero);

        _pets.SpawnBeside(PlayerPosition, 4f);
        Assert.That(Lead.State, Is.EqualTo(PetState.Heel));
        Assert.That(Lead.Health.Current, Is.EqualTo(Lead.MaxHp.Value));
        Assert.That(Lead.Id, Is.Not.EqualTo(petId));
    }

    [Test]
    public void SpitterAndBossNeverHunt_BossAddHunts()
    {
        EnemyAgent spitter = SpawnBitten(Spitter, Vector3.Zero);
        EnemyAgent boss = _enemies.SpawnBoss(Boss, Vector3.UnitZ);
        Bite(boss, 0.5f);
        EnemyAgent add = _enemies.SpawnSummon(Frog, -Vector3.UnitZ);
        Bite(add, 1f);

        Perceive(1f, Vector3.Zero, PlayerPosition);

        Assert.That(_pets.BitWithin(Lead.Id, spitter.Id, 1f, PetSystem.HuntMemory), Is.True);
        Assert.That(_pets.BitWithin(Lead.Id, boss.Id, 1f, PetSystem.HuntMemory), Is.True);
        Assert.That(spitter.Blackboard.QuarryIsAPet, Is.False);
        Assert.That(boss.Blackboard.QuarryIsAPet, Is.False);
        Assert.That(add.Blackboard.QuarryIsAPet, Is.True);
    }

    [Test]
    public void DropHunters_AllReturn_NoRehuntForTwoSeconds()
    {
        EnemyAgent first = SpawnBitten(Frog, Vector3.Zero);
        EnemyAgent second = SpawnBitten(Frog, Vector3.UnitZ, 0.5f);
        Perceive(1f, Vector3.Zero, PlayerPosition);
        Assert.That(_pets.HuntersOf(Lead.Id), Is.EqualTo(2));

        _pets.DropHunters(Lead.Id);
        Assert.That(_pets.HuntersOf(Lead.Id), Is.Zero);
        Bite(first, 2f);
        Bite(second, 2.5f);
        Perceive(2.99f, Vector3.Zero, PlayerPosition);
        Assert.That(first.Blackboard.QuarryIsAPet, Is.False);
        Assert.That(second.Blackboard.QuarryIsAPet, Is.False);

        Perceive(3f, Vector3.Zero, PlayerPosition);
        Assert.That(first.Blackboard.QuarryIsAPet, Is.True);
        Assert.That(second.Blackboard.QuarryIsAPet, Is.True);
    }

    [Test]
    public void PerceiveAndStrikes_DoNotAllocate()
    {
        Build(new SilentEvents());
        SpawnBitten(Frog, Vector3.Zero);
        float now = 0f;
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            // Re-bite to keep a hunter active across repeated strike and recover cycles.
            EnemyAgent enemy = _enemies.Registry.Alive[0];
            Bite(enemy, now);
            Step(now, Vector3.UnitZ, PlayerPosition);
            now += 0.1f;
        }, iterations: 25);
    }

    private EnemyAgent SpawnBitten(ContentId id, Vector3 position, float now = 0f)
    {
        EnemyAgent enemy = _enemies.Spawn(id, position);
        Bite(enemy, now);
        return enemy;
    }

    private void Bite(EnemyAgent enemy, float now)
    {
        _enemies.Mark(enemy.Id, 5f);
        ReportPet(enemy.Position);
        _pets.Tick(0.1f, now, enemy.Position, _enemies, _player);
    }

    private void ReportPet(Vector3 position)
    {
        _snapshot.PetCount = 0;
        ref EnemySense sense = ref _snapshot.AddPet();
        sense.Id = Lead.Id;
        sense.Position = position;
        _pets.Ingest(_snapshot);
    }

    private void Perceive(float now, Vector3 pet, Vector3 player, Vector3? enemyPosition = null)
    {
        _snapshot.Clear();
        _snapshot.PlayerPosition = player;
        ReportPet(pet);
        foreach (EnemyAgent enemy in _enemies.Registry.Alive)
        {
            ref EnemySense sense = ref _snapshot.AddEnemy();
            sense.Id = enemy.Id;
            sense.Position = enemyPosition ?? enemy.Position;
            sense.HasLineOfSight = true;
            sense.PathDirectionToPlayer = Vector2.UnitX;
        }

        _enemies.Ingest(_snapshot, pets: _pets, now: now);
    }

    private void Step(float now, Vector3 pet, Vector3 player, Vector3? enemyPosition = null)
    {
        Perceive(now, pet, player, enemyPosition);
        _lanes.Tick(now, player, _player);
        _enemies.Tick(new EnemyTickContext(0.1f, now, _player, _intents, _domainEvents,
            _projectiles, _enemies, _lanes, pets: _pets));
    }

    private void Build(IDomainEvents events)
    {
        _domainEvents = events;
        var hound = new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 0.25f, 1.4f, 8f, 10f, 1.5f);
        var character = new CharacterSpec(
            new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
            new LocKey("character.beastmaster.description"), 100f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f),
            new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f), pet: hound);
        var catalog = new ContentCatalog(new[] { character }, new[]
        {
            Spec(Frog, EnemyBehaviourKind.Chaser),
            Spec(Bullfrog, EnemyBehaviourKind.Chaser, leap: new LeapSpec(2f, 5f, 0.2f, 0.2f, 1.5f, 1.5f, 3f)),
            Spec(Panther, EnemyBehaviourKind.Lunger, lunge: new LungeSpec(5f, 6f, 12f, 1f, 2f)),
            Spec(Spitter, EnemyBehaviourKind.Spitter, projectile: new ProjectileSpec(5f, 10f, 1f)),
            Spec(BossBody, EnemyBehaviourKind.Boss),
        }, bosses: new[] { new BossSpec(Boss, BossBody, new[] { new BossPhaseSpec(1f) }, 1.5f) });
        _enemies = new EnemySystem(catalog, events, new FixedRandom(0.1f), new DepthScaling(Scalings.Design()), Capacity);
        _player = new PlayerCombat(character, events, _intents, Capacity);
        _pets = new PetSystem(hound, events, _intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
        _snapshot = new WorldSnapshot(Capacity);
        _projectiles = new ProjectileSystem(events, Capacity);
        _lanes = new LaneSystem(events);
    }

    private static EnemySpec Spec(ContentId id, EnemyBehaviourKind kind, LeapSpec leap = null,
        LungeSpec lunge = null, ProjectileSpec projectile = null) =>
        new EnemySpec(id, new LocKey(id.Value + ".name"), 100f, 2f, 1, 4, 5f, false, 8f,
            1.2f, lunge is null ? 0.2f : 0.6f, 0.2f, 30f, kind,
            projectile: projectile, lunge: lunge, leap: leap);
}
