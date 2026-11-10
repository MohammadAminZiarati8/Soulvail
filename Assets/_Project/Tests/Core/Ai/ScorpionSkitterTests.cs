using System;
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
public sealed class ScorpionSkitterTests
{
    private const int Capacity = 16;
    private const float Frame = 1f / 32f;
    private const float Speed = 2.2f;
    private const float RingRadius = 4f;
    private const float SpreadTimeout = 3f;
    private const float WindupTime = 0.6f;
    private const float ContactDamage = 8f;

    private static readonly ContentId Scorpion = new ContentId("enemy.scorpion");
    private static readonly ContentId Frog = new ContentId("enemy.frog");

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private IDomainEvents _domainEvents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private WorldSnapshot _snapshot;
    private PetSystem _pets;
    private Vector3 _petAt;
    private EnemyAgent _agent;
    private Vector3 _body;
    private Vector3 _playerAt;
    private float _clock;

    private ChaserBehaviour Behaviour => (ChaserBehaviour)_agent.Behaviour;
    private ChaserState State => Behaviour.State;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        Build(_events);
    }

    [Test]
    public void NoSkitter_ChaserUnchanged()
    {
        Spawn(Frog, Vector3.UnitZ * 5f);

        for (int i = 0; i < 120; i++)
        {
            Step();
            Assert.That(State, Is.Not.EqualTo(ChaserState.Spread));
            if (i == 0)
            {
                Assert.That(State, Is.EqualTo(ChaserState.Chase));
            }
        }
    }

    [Test]
    public void SlotOffset_FourSlots()
    {
        float[] expected = { -60f, -20f, 20f, 60f, -60f };

        for (int id = 0; id < expected.Length; id++)
        {
            Assert.That(SkitterSpec.SlotOffsetDegrees(id, 120f), Is.EqualTo(expected[id]));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => SkitterSpec.SlotOffsetDegrees(-1, 120f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SkitterSpec(4f, 181f, 3f, 1.8f, 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SkitterSpec(4f, 120f, 3f, 1.8f, float.NaN));
    }

    [Test]
    public void Spread_FourIds_FourBearings()
    {
        Vector3 start = new(0f, 0f, 8f);
        Spawn(Scorpion, start);
        for (int i = 1; i < SkitterSpec.SlotCount; i++)
        {
            _system.Spawn(Scorpion, start);
        }

        TickSystem();
        TickSystem();

        Assert.That(_intents.EnemyMoves.Count, Is.EqualTo(SkitterSpec.SlotCount));
        var bearings = new Vector2[SkitterSpec.SlotCount];
        foreach (EnemyMoveIntent move in _intents.EnemyMoves)
        {
            int slot = move.Id % SkitterSpec.SlotCount;
            float radians = ((slot * 40f) - 60f) * (MathF.PI / 180f);
            var bearing = new Vector2(-MathF.Sin(radians), MathF.Cos(radians));
            bearings[slot] = bearing;
            Vector2 expected = Vector2.Normalize((RingRadius * bearing) - new Vector2(start.X, start.Z));

            Assert.That(Vector2.Distance(move.FacingXZ, expected), Is.LessThan(1e-5f));
            Assert.That(move.Velocity.Length(), Is.EqualTo(Speed).Within(1e-5f));
        }

        for (int i = 1; i < bearings.Length; i++)
        {
            Assert.That(Vector2.Dot(bearings[i - 1], bearings[i]), Is.LessThanOrEqualTo(MathF.Cos(40f * MathF.PI / 180f) + 1e-5f));
        }
    }

    [Test]
    public void Spread_GoalFollowsHero()
    {
        BeginSpread();
        Vector3 goal = FanGoal();
        _playerAt += Vector3.UnitX * 2f;
        Vector3 delta = goal + (Vector3.UnitX * 2f) - _body;

        Step();

        Vector3 expected = Vector3.Normalize(delta) * Speed;
        Assert.That(Vector3.Distance(_intents.LastEnemyMove.Velocity, expected), Is.LessThan(1e-5f));
    }

    [Test]
    public void Spread_Arrives_Chases()
    {
        BeginSpread();
        _body = FanGoal() + (Vector3.UnitZ * 0.5f);

        Step();

        Assert.That(State, Is.EqualTo(ChaserState.Chase));
    }

    [Test]
    public void Spread_Timeout_Chases()
    {
        BeginSpread();

        int ticks = (int)(SpreadTimeout / Frame);
        for (int i = 0; i < ticks; i++)
        {
            Assert.That(State, Is.EqualTo(ChaserState.Spread));
            _playerAt -= Vector3.UnitZ * Speed * Frame * 2f;
            Step();
        }

        Assert.That(State, Is.EqualTo(ChaserState.Chase));
    }

    [Test]
    public void Spread_HeroInReach_WindsUp()
    {
        BeginWindup();

        Assert.That(State, Is.EqualTo(ChaserState.Windup));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void Recover_ReturnsToChase_NotSpread()
    {
        BeginWindup();
        TickUntil(ChaserState.Recover);
        TickUntil(ChaserState.Chase);
    }

    [Test]
    public void Windup_LocksFacing_NoCancel()
    {
        BeginWindup();
        Assert.That(Behaviour.LockedFacing, Is.EqualTo(Vector2.UnitY));
        _playerAt = _body + (Vector3.UnitX * 3f);

        for (int i = 0; i < (int)(WindupTime / Frame); i++)
        {
            Step();
            Assert.That(State, Is.EqualTo(ChaserState.Windup));
            Assert.That(Behaviour.LockedFacing, Is.EqualTo(Vector2.UnitY));
            Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitY));
            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        }

        Step();

        Assert.That(State, Is.EqualTo(ChaserState.Strike));
        Assert.That(Behaviour.LockedFacing, Is.EqualTo(Vector2.Zero));
        Assert.That(_events.Single<EnemyTelegraph>().Duration, Is.EqualTo(WindupTime));
    }

    [Test]
    public void Strike_InStrip_Hits()
    {
        BeginWindup();
        _playerAt = _body + new Vector3(0.4f, 5f, 1.6f);

        TickUntil(ChaserState.Recover);

        PlayerDamaged hit = _events.Single<PlayerDamaged>();
        Assert.That(hit.ToHp + hit.ToShield, Is.EqualTo(ContactDamage));
    }

    [Test]
    public void Strike_SideStepped_Misses()
    {
        BeginWindup();
        _playerAt = _body + new Vector3(0.6f, 0f, 1f);

        TickUntil(ChaserState.Recover);

        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    [Test]
    public void Strike_PetInStrip_HitsPet()
    {
        Spawn(Scorpion, Vector3.Zero);
        _playerAt = Vector3.UnitZ * 8f;
        var hound = new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 0.25f, 1.4f, 1.5f);
        _pets = new PetSystem(hound, _events, _intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
        _system.Mark(_agent.Id, 5f);
        ReportPet();
        _pets.Tick(Frame, _clock, _body, _system, _player);
        Assert.That(_events.Count<PetBit>(), Is.EqualTo(1));
        PetAgent pet = _pets.Pets[0];
        float hp = pet.Health.Current;
        _petAt = Vector3.UnitZ;

        TickUntil(ChaserState.Windup);
        Assert.That(_agent.Blackboard.QuarryPetId, Is.EqualTo(pet.Id));
        _petAt = new Vector3(0.4f, 0f, 1.6f);
        TickUntil(ChaserState.Recover);

        Assert.That(pet.Health.Current, Is.EqualTo(hp - ContactDamage));
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    [Test]
    public void Reset_ClearsSpreadAndFacing()
    {
        BeginWindup();

        Behaviour.Reset();

        Assert.That(State, Is.EqualTo(ChaserState.Idle));
        Assert.That(Behaviour.LockedFacing, Is.EqualTo(Vector2.Zero));

        // Start another aggro from the opposite side and check the new fan goal.
        _body = -Vector3.UnitZ * 5f;
        _playerAt = Vector3.Zero;
        Step();
        Vector3 expected = Vector3.Normalize(-FanGoal() - _body) * Speed;
        Step();
        Assert.That(Vector3.Distance(_intents.LastEnemyMove.Velocity, expected), Is.LessThan(1e-5f));
    }

    [Test]
    public void Tick_Skitter_NoAlloc()
    {
        Build(new SilentEvents());
        Vector3 start = Vector3.UnitZ * 5f;
        Spawn(Scorpion, start);
        TickUntil(ChaserState.Recover, 200);
        Behaviour.Reset();
        _body = start;

        AllocationAssert.None(Step, iterations: 200);

        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
        Assert.That(State, Is.EqualTo(ChaserState.Windup).Or.EqualTo(ChaserState.Strike).Or.EqualTo(ChaserState.Recover));
    }

    private void Build(IDomainEvents events)
    {
        _domainEvents = events;
        _intents = new RecordingIntents();
        CharacterSpec character = Oathbound();
        var catalog = new ContentCatalog(new[] { character }, new[]
        {
            Spec(Scorpion, new SkitterSpec(RingRadius, 120f, SpreadTimeout, 1.8f, 0.5f)),
            Spec(Frog, null),
        });

        _system = new EnemySystem(catalog, events, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
        _player = new PlayerCombat(character, events, _intents, Capacity);
        _projectiles = new ProjectileSystem(events, Capacity);
        _snapshot = new WorldSnapshot(Capacity);
        _clock = 0f;
        _playerAt = Vector3.Zero;
        _petAt = Vector3.Zero;
        _pets = null;
    }

    private void Spawn(ContentId id, Vector3 body)
    {
        _body = body;
        _agent = _system.Spawn(id, body);
    }

    private void BeginSpread()
    {
        Spawn(Scorpion, Vector3.UnitZ * 5f);
        Step();
        Assert.That(State, Is.EqualTo(ChaserState.Spread));
    }

    private void BeginWindup()
    {
        BeginSpread();
        _playerAt = _body + Vector3.UnitZ;
        Step();
    }

    // The fixture begins with the body north of its quarry and with no height offset.
    private Vector3 FanGoal()
    {
        float radians = SkitterSpec.SlotOffsetDegrees(_agent.Id, 120f) * (MathF.PI / 180f);
        return _playerAt + (RingRadius * new Vector3(-MathF.Sin(radians), 0f, MathF.Cos(radians)));
    }

    private void TickUntil(ChaserState state, int ticks = 100)
    {
        for (int i = 0; i < ticks && State != state; i++)
        {
            Step();
        }

        Assert.That(State, Is.EqualTo(state));
    }

    private void Step()
    {
        TickSystem();
        _body += _intents.LastEnemyMove.Velocity * Frame;
    }

    private void TickSystem()
    {
        _snapshot.Clear();
        _snapshot.Dt = Frame;
        _snapshot.PlayerPosition = _playerAt;

        foreach (EnemyAgent agent in _system.Registry.Alive)
        {
            ref EnemySense sense = ref _snapshot.AddEnemy();
            sense.Id = agent.Id;
            sense.Position = agent == _agent ? _body : agent.Position;
            sense.HasLineOfSight = true;
        }

        if (_pets != null)
        {
            ReportPet();
        }

        _system.Ingest(_snapshot, pets: _pets, now: _clock);
        _intents.Clear();
        _system.Tick(new EnemyTickContext(Frame, _clock, _player, _intents, _domainEvents, _projectiles, _system, pets: _pets));
        _clock += Frame;
    }

    private void ReportPet()
    {
        ref EnemySense sense = ref _snapshot.AddPet();
        sense.Id = _pets.Pets[0].Id;
        sense.Position = _petAt;
        _pets.Ingest(_snapshot);
    }

    private static EnemySpec Spec(ContentId id, SkitterSpec skitter) => new EnemySpec(
        id,
        new LocKey("enemy.scorpion.name"),
        maxHp: 36f,
        moveSpeed: Speed,
        targetPriority: 1,
        threatCost: 4,
        xpValue: 18f,
        isElite: false,
        contactDamage: ContactDamage,
        reach: 1.5f,
        windupTime: WindupTime,
        recoverTime: 0.8f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Chaser,
        hopTime: skitter == null ? 0.3f : 0f,
        hopRestTime: skitter == null ? 0.2f : 0f,
        skitter: skitter);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId("character.oathbound"),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);
}
