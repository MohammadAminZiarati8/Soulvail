using System;
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
public sealed class PetSurvivalTests
{
    private const int Capacity = 16;
    private static readonly ContentId EnemyId = new("enemy.pet_survival");
    private static readonly ContentId ModeId = new("mode.pet_survival");
    private static readonly ContentId CharacterId = new("character.beastmaster");

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _combat;
    private PetSystem _pets;

    private PetAgent Lead => _pets.Pets[0];

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _enemies = new EnemySystem(Catalog(), _events, new FixedRandom(0.1f),
            new DepthScaling(Scalings.Design()), Capacity);
        _combat = new PlayerCombat(Character(), _events, _intents, Capacity);
        _pets = new PetSystem(Hound(), _events, _intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
    }

    [Test]
    public void BelowHeelAt_Heels_StopsBiting()
    {
        Vector3 player = Vector3.UnitX * 4f;
        EnemyAgent enemy = _enemies.Spawn(EnemyId, Lead.Position);
        Tick(0f, player: player);
        _enemies.Ingest(new WorldSnapshot(Capacity) { PlayerPosition = player }, pets: _pets);
        Assert.That(_pets.HuntersOf(Lead.Id), Is.EqualTo(1));

        Hurt(65f);
        Tick(1f, player: player);
        Assert.That(Lead.State, Is.EqualTo(PetState.Hunt), "The exact threshold still hunts.");
        Assert.That(_events.Count<PetBit>(), Is.EqualTo(2));

        Hurt(1f);
        Tick(2f, player: player);
        Assert.That(Lead.State, Is.EqualTo(PetState.Heel));
        Assert.That(Lead.QuarryId, Is.Zero);
        Assert.That(_events.Count<PetBit>(), Is.EqualTo(2));
        Assert.That(_events.Single<PetHeeled>().PetId, Is.EqualTo(Lead.Id));
        Assert.That(_intents.PetMoves[2].Velocity.Length(), Is.EqualTo(4f).Within(0.001f));

        var snapshot = new WorldSnapshot(Capacity) { PlayerPosition = player };
        _enemies.Ingest(snapshot, pets: _pets, now: 2.99f);
        Assert.That(_pets.HuntersOf(Lead.Id), Is.EqualTo(1));
        _enemies.Ingest(snapshot, pets: _pets, now: 3f);
        Assert.That(_pets.HuntersOf(Lead.Id), Is.Zero);
        Assert.That(enemy.Blackboard.QuarryIsAPet, Is.False);
    }

    [Test]
    public void AtHeel_Regens_SixPercentASecond()
    {
        Hurt(20f);
        Tick(1f, 1f, Vector3.UnitX * 4f);
        Assert.That(Lead.Health.Current, Is.EqualTo(80f), "Outside heel range.");

        Tick(2f, 1f);
        Assert.That(Lead.Health.Current, Is.EqualTo(86f), "Between waves.");
        Tick(5f, 3f);
        Assert.That(Lead.Health.Current, Is.EqualTo(100f), "Regen stops at max HP.");
    }

    [Test]
    public void ReachesReturnAt_HuntsAgain()
    {
        Hurt(66f);
        EnemyAgent enemy = _enemies.Spawn(EnemyId, Lead.Position);
        Tick(1f, 1f);
        Assert.That(Lead.Health.Current, Is.EqualTo(40f));
        Assert.That(Lead.State, Is.EqualTo(PetState.Heel));
        Assert.That(_events.Count<PetReturned>(), Is.Zero);

        Tick(8f, 7f);
        Assert.That(Lead.State, Is.EqualTo(PetState.Hunt));
        Assert.That(Lead.QuarryId, Is.EqualTo(enemy.Id));
        Assert.That(_events.Single<PetReturned>().PetId, Is.EqualTo(Lead.Id));

        _pets.SpawnBeside(Vector3.Zero, 9f);
        Tick(9.1f);
        Assert.That(Lead.QuarryId, Is.EqualTo(enemy.Id));
        Assert.That(_events.Count<PetReturned>(), Is.EqualTo(1));
    }

    [Test]
    public void PlayerNear_RescueFills_TwoSeconds()
    {
        Hurt(100f);
        Tick(0.5f, 0.5f);
        Assert.That(Lead.RescueProgress, Is.EqualTo(0.25f));
        Assert.That(_events.Single<RescueProgressed>().PetId, Is.EqualTo(Lead.Id));

        Tick(3f, 2.5f, Lead.Position + 2.51f * Vector3.UnitX);
        Assert.That(Lead.RescueProgress, Is.EqualTo(0.25f));
        Assert.That(_events.Count<RescueProgressed>(), Is.EqualTo(1));

        Vector3 near = Lead.Position + 2.5f * Vector3.UnitX;
        Tick(4f, 1f, near);
        Assert.That(Lead.State, Is.EqualTo(PetState.Fallen));
        Assert.That(Lead.RescueProgress, Is.EqualTo(0.75f));
        Tick(4.5f, 0.5f, near);
        Assert.That(_events.Of<RescueProgressed>()[2].Fraction, Is.EqualTo(1f));
    }

    [Test]
    public void Rescued_RisesHalfHp_AtHeel()
    {
        _enemies.Spawn(EnemyId, Lead.Position);
        Hurt(100f);
        Tick(2f, 2f);

        Assert.That(Lead.Health.Current, Is.EqualTo(50f));
        Assert.That(Lead.State, Is.EqualTo(PetState.Heel));
        Assert.That(Lead.RescueProgress, Is.Zero);
        Assert.That(_events.Single<PetRose>().PetId, Is.EqualTo(Lead.Id));
        Tick(2.25f, 0.25f);
        Assert.That(Lead.State, Is.EqualTo(PetState.Heel));
        Assert.That(_events.Count<PetBit>(), Is.Zero);
    }

    [Test]
    public void HeadStart_Half_OneSecondLeft()
    {
        _pets.AddRescueHeadStart(Lead.Id, 0.5f);
        Assert.That(_events.Count<RescueProgressed>(), Is.Zero);
        Hurt(100f);
        _pets.AddRescueHeadStart(Lead.Id, 0.5f);
        Assert.That(Lead.RescueProgress, Is.EqualTo(0.5f));
        Tick(1f, 1f);
        Assert.That(_events.Count<PetRose>(), Is.EqualTo(1));

        Hurt(100f);
        _pets.AddRescueHeadStart(Lead.Id, 1.5f);
        var progress = _events.Of<RescueProgressed>();
        Assert.That(progress[progress.Count - 1].Fraction, Is.EqualTo(1f));
        Assert.That(_events.Count<PetRose>(), Is.EqualTo(2));
    }

    [Test]
    public void Unrescued_StaysDown_UntilStageArrived()
    {
        _events.Clear();
        var random = new FixedRandom(1, 0.1f);
        var recorder = new RunRecorder(random, new FixedClock(default), _events);
        var session = new RunSession(Catalog(), random, _events, _intents, recorder, Capacity, Capacity, Capacity);
        session.Start(new RunConfig(ModeId, CharacterId, 1, 1, SpawnPlan.Empty, restore: null));
        PetSpawned opening = _events.Single<PetSpawned>();
        Vector3 enemyPosition = Vector3.UnitX * 7f;
        var snapshot = new WorldSnapshot(Capacity)
        {
            SpawnPoints = new[] { enemyPosition },
            HasGate = true,
            GatePosition = Vector3.Zero,
            Dt = 0.1f,
        };

        for (int i = 0; i < 600 && _events.Count<PetFell>() == 0; i++)
        {
            StepRun(session, snapshot, opening.Id, enemyPosition, Vector3.Zero, attack: false);
        }

        Assert.That(_events.Count<EnemySpawned>(), Is.EqualTo(1));
        Assert.That(_events.Single<PetFell>().PetId, Is.EqualTo(opening.Id));
        for (int i = 0; i < 250; i++)
        {
            StepRun(session, snapshot, opening.Id, enemyPosition, Vector3.Zero, attack: false);
        }

        Assert.That(_events.Count<PetRose>(), Is.Zero);
        Assert.That(_events.Count<RescueProgressed>(), Is.Zero);
        Assert.That(_events.Count<PetSpawned>(), Is.EqualTo(1));

        for (int i = 0; i < 600 && session.State.StageIndex == 1; i++)
        {
            Vector3 player = _events.Count<EnemyDied>() == 0 ? Vector3.UnitX * 4f : Vector3.Zero;
            StepRun(session, snapshot, opening.Id, enemyPosition, player, attack: true);
        }

        Assert.That(session.State.StageIndex, Is.EqualTo(2));
        Assert.That(_events.Count<StageArrived>(), Is.EqualTo(2));
        Assert.That(_events.Count<PetRose>(), Is.Zero);
        var spawned = _events.Of<PetSpawned>();
        Assert.That(spawned.Count, Is.EqualTo(2));
        Assert.That(spawned[1].Id, Is.Not.EqualTo(opening.Id));
        Assert.That(spawned[1].HealthFraction, Is.EqualTo(1f));
    }

    [Test]
    public void ModifyStat_PetReturnAt_Lands()
    {
        var stats = new PlayerStats(_combat, new PlayerMotor(Character().Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), _events));
        var handler = new ModifyStatHandler(stats, pets: _pets);
        var effect = new ModifyStat(PlayerStat.PetReturnAt, ModifierKind.Flat, -0.3f, StatTarget.Pet);
        handler.Apply(effect, this);
        Assert.That(Lead.ReturnAt.Value, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(_pets.LeadStats.Resolve(PlayerStat.PetHeelRegen), Is.SameAs(Lead.HeelRegen));
        Assert.That(_pets.LeadStats.Resolve(PlayerStat.PetRescueSeconds), Is.SameAs(Lead.RescueSeconds));

        Hurt(66f);
        _enemies.Spawn(EnemyId, Lead.Position);
        Tick(3f, 3f);
        Assert.That(Lead.Health.Current, Is.EqualTo(52f));
        Assert.That(Lead.State, Is.EqualTo(PetState.Hunt));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var events = new SilentEvents();
        var pets = new PetSystem(Hound(), events, _intents);
        var enemies = new EnemySystem(Catalog(), events, new FixedRandom(0.1f),
            new DepthScaling(Scalings.Design()), Capacity);
        pets.SpawnBeside(Vector3.Zero, 0f);

        AllocationAssert.None(() =>
        {
            _intents.Clear();
            PetAgent pet = pets.Pets[0];
            pets.ApplyDamage(pet.Id, 34f, 0f, 1);
            pets.Tick(0.1f, 0f, Vector3.Zero, enemies, _combat);
            pets.ApplyDamage(pet.Id, 100f, 0f, 1);
            pets.Tick(1f, 1f, Vector3.Zero, enemies, _combat);
            pets.Tick(1f, 2f, Vector3.Zero, enemies, _combat);
        });
    }

    private void Hurt(float amount) => _pets.ApplyDamage(Lead.Id, amount, 0f, 1);

    private void Tick(float now, float dt = 0.1f, Vector3 player = default) =>
        _pets.Tick(dt, now, player, _enemies, _combat);

    private void StepRun(RunSession session, WorldSnapshot snapshot, int petId, Vector3 enemyPosition,
        Vector3 player, bool attack)
    {
        snapshot.PlayerPosition = player;
        snapshot.PetCount = 0;
        snapshot.EnemyCount = 0;
        _intents.Clear();
        int targetId = 0;
        foreach (EnemySpawned enemy in _events.Of<EnemySpawned>())
        {
            if (!session.State.IsEnemyAlive(enemy.Id))
            {
                continue;
            }

            targetId = enemy.Id;
            ref EnemySense sense = ref snapshot.AddEnemy();
            sense.Id = enemy.Id;
            sense.Position = enemyPosition;
            sense.HasLineOfSight = true;
            ref EnemySense pet = ref snapshot.AddPet();
            pet.Id = petId;
            pet.Position = enemyPosition;
            break;
        }

        session.Tick(snapshot);
        if (attack && targetId != 0 && _intents.ConeHits.Count > 0)
        {
            Span<int> hits = stackalloc int[1] { targetId };
            session.ReportConeHits(hits);
        }
    }

    private static PetSpec Hound() => new PetSpec(new ContentId("pet.hound"), 100f, 4f, 7f, 1f, 1.4f, 1.5f);

    private static CharacterSpec Character() => new CharacterSpec(
        CharacterId, new LocKey("character.beastmaster.name"), new LocKey("character.beastmaster.description"),
        100f, new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        pet: Hound());

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Character() },
        new[]
        {
            new EnemySpec(EnemyId, new LocKey("enemy.pet_survival.name"), 100f, 2f, 1, 4, 5f, false,
                100f, 1.2f, 0.2f, 0.2f, 30f, EnemyBehaviourKind.Chaser),
        },
        new[]
        {
            new ModeSpec(ModeId, new LocKey("mode.pet_survival.name"), 1, true, 0,
                new ScalingSpec(new BudgetCurve(4f, 0f, 0f), new WaveCurve(1, 1000, 1, 1),
                    new ConcurrencyCurve(1, 1000), new StatCurve(0.06f, 4f, 1, 1),
                    new StatCurve(0.035f, 3f, 1, 1), new StatCurve(0.02f, 1.3f, 5, 0)),
                Scalings.Xp(), new[] { new RosterEntry(EnemyId, 1) }, hasSanctum: false),
        });
}
