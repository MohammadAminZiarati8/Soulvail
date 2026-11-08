using System.Numerics;
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

[TestFixture]
public sealed class PetSystemTests
{
    private const int Capacity = 16;
    private static readonly ContentId CharacterId = new("character.beastmaster");
    private static readonly ContentId ModeId = new("mode.pet_test");
    private static readonly ContentId EnemyId = new("enemy.pet_target");

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _combat;
    private PetSystem _pets;
    private WorldSnapshot _snapshot;

    private PetAgent Lead => _pets.Pets[0];

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        ContentCatalog catalog = Catalog();
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(0.1f),
            new DepthScaling(Scalings.Design()), Capacity);
        _combat = new PlayerCombat(Character(), _events, _intents, Capacity);
        _pets = new PetSystem(Hound(), _events, _intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
        _snapshot = new WorldSnapshot(Capacity);
    }

    [Test]
    public void Spawn_StageArrived_FullHp()
    {
        RunSession session = StartRun();
        PetSpawned opening = _events.Single<PetSpawned>();
        Assert.That(opening.Position, Is.EqualTo(-1.5f * Vector3.UnitZ));

        _snapshot.SpawnPoints = new[] { Vector3.UnitX * 12f };
        _snapshot.HasGate = true;
        _snapshot.GatePosition = Vector3.Zero;
        _snapshot.Dt = 0.25f;
        for (int i = 0; i < 600 && session.State.StageIndex == 1; i++)
        {
            _snapshot.PlayerPosition = Vector3.Zero;
            _snapshot.PetCount = 0;
            foreach (EnemySpawned enemy in _events.Of<EnemySpawned>())
            {
                if (!session.State.IsEnemyAlive(enemy.Id))
                {
                    continue;
                }

                _snapshot.PlayerPosition = enemy.Position;
                ref EnemySense sense = ref _snapshot.AddPet();
                sense.Id = opening.Id;
                sense.Position = enemy.Position;
                break;
            }

            session.Tick(_snapshot);
        }

        Assert.That(session.State.StageIndex, Is.EqualTo(2));
        Assert.That(_events.Count<StageArrived>(), Is.EqualTo(2));
        Assert.That(_events.Count<PetSpawned>(), Is.EqualTo(2));
        Assert.That(_events.Of<PetSpawned>()[1].Id, Is.Not.EqualTo(opening.Id));

        Lead.Health.ApplyDamage(10f, 0f);
        _pets.SpawnBeside(Vector3.Zero, 1f);
        Assert.That(_pets.Pets.Length, Is.EqualTo(1));
        Assert.That(Lead.Health.Current, Is.EqualTo(Lead.MaxHp.Value));
    }

    [Test]
    public void NoPetSpec_NoOps()
    {
        var pets = new PetSystem(null, _events, _intents);
        _events.Clear();
        pets.SpawnBeside(Vector3.Zero, 0f);
        pets.Ingest(_snapshot);
        pets.Tick(0.1f, 0f, Vector3.Zero, null, null);
        pets.Clear();

        Assert.That(pets.Pets.Length, Is.Zero);
        Assert.That(pets.LeadStats, Is.Null);
        Assert.That(_events.All, Is.Empty);
        Assert.That(_intents.PetMoves, Is.Empty);
    }

    [Test]
    public void Target_MarkedBeforeNearer()
    {
        EnemyAgent nearPlayer = _enemies.Spawn(EnemyId, Vector3.UnitX);
        EnemyAgent farMarked = _enemies.Spawn(EnemyId, Vector3.UnitZ * 4f);
        EnemyAgent nearPetMarked = _enemies.Spawn(EnemyId, -Vector3.UnitZ * 3f);
        Tick(0f);
        Assert.That(Lead.QuarryId, Is.EqualTo(nearPlayer.Id));

        _enemies.Mark(farMarked.Id, 4f);
        _enemies.Mark(nearPetMarked.Id, 4f);
        Tick(PetSystem.RetargetInterval / 2f);
        Assert.That(Lead.QuarryId, Is.EqualTo(nearPlayer.Id));

        Tick(PetSystem.RetargetInterval);
        Assert.That(Lead.QuarryId, Is.EqualTo(nearPetMarked.Id));
        Assert.That(_events.Of<PetRetargeted>()[1].EnemyId, Is.EqualTo(nearPetMarked.Id));
    }

    [Test]
    public void NoEnemy_Heels()
    {
        ReportPet(-Vector3.UnitZ * 3f);
        Tick(0f);

        Assert.That(Lead.Position, Is.EqualTo(-Vector3.UnitZ * 3f));
        Assert.That(Lead.State, Is.EqualTo(PetState.Heel));
        Assert.That(_intents.PetMoves[0].Velocity, Is.EqualTo(Vector3.UnitZ * 4f));

        ReportPet(-Vector3.UnitZ * 1.5f);
        Tick(0.1f);
        Assert.That(_intents.PetMoves[1].Velocity, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void Bite_AtInterval_PaysXp()
    {
        EnemyAgent enemy = _enemies.Spawn(EnemyId, Lead.Position);
        Tick(0f);
        Tick(0.99f);
        Assert.That(_events.Count<PetBit>(), Is.EqualTo(1));
        Assert.That(_events.Single<PetBit>().Amount, Is.EqualTo(7f));
        Assert.That(enemy.IsAlive, Is.True);
        Assert.That(_enemies.DrainXp(), Is.Zero);

        Tick(1f);
        Assert.That(_events.Count<PetBit>(), Is.EqualTo(2));
        Assert.That(enemy.IsAlive, Is.False);
        Assert.That(_enemies.DrainXp(), Is.EqualTo(5f));
    }

    [Test]
    public void BossAdd_KilledByPet_PaysNothing()
    {
        EnemyAgent enemy = _enemies.SpawnSummon(EnemyId, Lead.Position);
        Tick(0f);
        Tick(1f);

        Assert.That(enemy.IsAlive, Is.False);
        Assert.That(_enemies.DrainXp(), Is.Zero);
    }

    [Test]
    public void Pet_NotInRegistry()
    {
        Assert.That(Lead.Id, Is.Positive);
        Assert.That(_enemies.Registry.TryGet(Lead.Id, out _), Is.False);
        Assert.That(_enemies.Registry.Alive.Length, Is.Zero);
    }

    [Test]
    public void LevelUp_GrowsPetHpAndBite()
    {
        var progression = new LevelTracker(Scalings.Xp(), _events);
        var effects = new EffectRegistry();
        effects.Register<ModifyStat>(StatHandler(progression, _pets));
        _ = new LevelGrowth(new LevelGrowthSpec(0.03f, 0.02f), progression, effects, _combat.Health, _pets);

        Lead.Health.ApplyDamage(10f, 0f);
        progression.Grant(Scalings.Xp().ToReach(2));

        Assert.That(Lead.MaxHp.Value, Is.EqualTo(72.1f).Within(0.001f));
        Assert.That(Lead.Health.Current, Is.EqualTo(62.1f).Within(0.001f));
        Assert.That(Lead.ContactDamage.Value, Is.EqualTo(7.14f).Within(0.001f));
    }

    [Test]
    public void ModifyStat_Pet_Lands_OrNoOps()
    {
        var progression = new LevelTracker(Scalings.Xp(), _events);
        ModifyStatHandler handler = StatHandler(progression, _pets);
        var effect = new ModifyStat(PlayerStat.ContactDamage, ModifierKind.Flat, 2f, StatTarget.Pet);
        handler.Apply(effect, this);
        Assert.That(Lead.ContactDamage.Value, Is.EqualTo(9f));
        handler.Remove(effect, this);
        Assert.That(Lead.ContactDamage.Value, Is.EqualTo(7f));

        _pets.Clear();
        Assert.DoesNotThrow(() => handler.Apply(effect, this));
        Assert.DoesNotThrow(() => handler.Remove(effect, this));

        ModifyStatHandler noPet = StatHandler(progression, null);
        Assert.DoesNotThrow(() => noPet.Apply(effect, this));
        Assert.DoesNotThrow(() => noPet.Remove(effect, this));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var events = new SilentEvents();
        var pets = new PetSystem(Hound(), events, _intents);
        pets.SpawnBeside(Vector3.Zero, 0f);
        var enemies = new EnemySystem(Catalog(), events, new FixedRandom(0.1f),
            new DepthScaling(Scalings.Design()), Capacity);
        EnemyAgent enemy = enemies.Spawn(EnemyId, pets.Pets[0].Position);
        enemy.Health.MaxHp.Add(new Modifier(ModifierKind.Flat, 1_000_000f, this));
        enemy.Health.Reset();
        enemies.Mark(enemy.Id, 4f);
        float now = 0f;
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            pets.Tick(0.1f, now, Vector3.Zero, enemies, _combat);
            now += PetSystem.RetargetInterval;
        });
    }

    private ModifyStatHandler StatHandler(LevelTracker progression, PetSystem pets)
    {
        var stats = new PlayerStats(_combat, new PlayerMotor(Character().Movement, Vector3.UnitZ), progression);
        return new ModifyStatHandler(stats, pets: pets);
    }

    private void ReportPet(Vector3 position)
    {
        _snapshot.Clear();
        ref EnemySense sense = ref _snapshot.AddPet();
        sense.Id = Lead.Id;
        sense.Position = position;
        _pets.Ingest(_snapshot);
    }

    private void Tick(float now) => _pets.Tick(0.1f, now, Vector3.Zero, _enemies, _combat);

    private RunSession StartRun(bool hasPet = true)
    {
        _events.Clear();
        var random = new FixedRandom(1, 0.1f);
        var recorder = new RunRecorder(random, new FixedClock(default), _events);
        var session = new RunSession(Catalog(hasPet), random, _events, _intents, recorder, Capacity, Capacity, Capacity);
        session.Start(new RunConfig(ModeId, CharacterId, 1, 1, SpawnPlan.Empty, restore: null));
        return session;
    }

    private static ContentCatalog Catalog(bool hasPet = true) => new ContentCatalog(
        new[] { Character(hasPet) },
        new[]
        {
            new EnemySpec(EnemyId, new LocKey("enemy.pet_target.name"),
                14f, 2f, 1, 4, 5f, false, 8f, 1.2f, 0.4f, 0.6f, 30f, EnemyBehaviourKind.Static),
        },
        new[]
        {
            new ModeSpec(ModeId, new LocKey("mode.pet_test.name"), 1, true, 0,
                OneEnemyScaling(), Scalings.Xp(), new[] { new RosterEntry(EnemyId, 1) },
                hasSanctum: false, levelGrowth: new LevelGrowthSpec(0.03f, 0.02f)),
        });

    private static ScalingSpec OneEnemyScaling() => new ScalingSpec(
        new BudgetCurve(4f, 0f, 0f),
        new WaveCurve(1, 1000, 1, 1),
        new ConcurrencyCurve(1, 1000),
        new StatCurve(0.06f, 4f, 1, 1),
        new StatCurve(0.035f, 3f, 1, 1),
        new StatCurve(0.02f, 1.3f, 5, 0));

    private static CharacterSpec Character(bool hasPet = true) => new CharacterSpec(
        CharacterId,
        new LocKey("character.beastmaster.name"),
        new LocKey("character.beastmaster.description"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        pet: hasPet ? Hound() : null);

    private static PetSpec Hound() => new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 1f, 1.4f, 1.5f);
}
