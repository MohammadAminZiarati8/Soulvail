using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

public sealed class PackTacticsTests
{
    private const int Capacity = 8;
    private const float BiteInterval = 0.5f;
    private static readonly ContentId EnemyId = new("enemy.pack_target");
    private static readonly ContentId BossBodyId = new("enemy.warden");
    private static readonly ContentId BossId = new("boss.warden");
    private static readonly PetSpec Hound = new(new ContentId("pet.hound"), 70f, 4f, 8f, BiteInterval, 1.4f, 8f, 10f, 1.5f);

    private SilentEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _combat;
    private PetSystem _pets;

    [SetUp]
    public void SetUp()
    {
        _events = new SilentEvents();
        _intents = new RecordingIntents();
        var character = new CharacterSpec(
            new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
            new LocKey("character.beastmaster.description"), 100f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f),
            new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f), pet: Hound);
        var boss = new BossSpec(BossId, BossBodyId, new[] { new BossPhaseSpec(1f) }, 1.5f);
        var mode = new ModeSpec(new ContentId("mode.pack_test"), new LocKey("mode.pack_test.name"), 1, true, 0,
            new ScalingSpec(new BudgetCurve(4f, 0f, 0f), new WaveCurve(1, 1000, 1, 1),
                new ConcurrencyCurve(1, 1000), new StatCurve(0.06f, 4f, 1, 1),
                new StatCurve(0.035f, 3f, 1, 1), new StatCurve(0.02f, 1.3f, 5, 0)),
            Scalings.Xp(), new[] { new RosterEntry(EnemyId, 1) }, hasSanctum: false);
        var catalog = new ContentCatalog(new[] { character },
            new[] { Body(EnemyId, EnemyBehaviourKind.Chaser), Body(BossBodyId, EnemyBehaviourKind.Boss) },
            new[] { mode }, bosses: new[] { boss });

        _combat = new PlayerCombat(character, _events, _intents, Capacity);
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(0.1f), new DepthScaling(Scalings.Design()), Capacity);
        _pets = new PetSystem(Hound, _events, _intents);
        _pets.SpawnBeside(Vector3.Zero, 0f);
        Set(PlayerStat.PackHounds, 1f);
        foreach (PetAgent pet in _pets.Pets)
        {
            ReportPosition(pet, Vector3.Zero);
        }
    }

    [Test]
    public void TwoOnOne_BonusApplies()
    {
        Set(PlayerStat.PackTactics, 0.2f);
        EnemyAgent target = _enemies.Spawn(EnemyId, Vector3.Zero);
        float now = -BiteInterval;
        AllocationAssert.None(() =>
        {
            target.Health.Reset();
            Tick(now += BiteInterval);
        });

        Assert.That(DamageTo(target), Is.EqualTo(14.4f).Within(0.001f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Alone_NoBonus(bool packmateOutOfReach)
    {
        Set(PlayerStat.PackTactics, 0.2f);
        if (packmateOutOfReach)
        {
            ReportPosition(_pets.Pets[1], Vector3.UnitX * 4f);
        }
        else
        {
            Set(PlayerStat.PackHounds, 0f);
        }

        EnemyAgent target = _enemies.Spawn(EnemyId, Vector3.Zero);
        float now = -BiteInterval;
        AllocationAssert.None(() =>
        {
            target.Health.Reset();
            Tick(now += BiteInterval);
        });

        Assert.That(DamageTo(target), Is.EqualTo(8f).Within(0.001f));
    }

    [Test]
    public void TwoBiting_Slowed_Refreshed()
    {
        Set(PlayerStat.PackTacticsSlow, 0.3f);
        EnemyAgent target = _enemies.Spawn(EnemyId, Vector3.Zero);
        float now = -BiteInterval;
        var projectiles = new ProjectileSystem(_events, Capacity);
        var lanes = new LaneSystem(_events);
        float enemyClock = 0f;
        float afterFirstBite = 0f;
        float afterRefresh = 0f;
        AllocationAssert.None(() =>
        {
            target.Health.Reset();
            Tick(now += BiteInterval);
            afterFirstBite = target.CrowdControl.SlowFraction;
            AgeStatuses(BiteInterval);
            Tick(now += BiteInterval);
            AgeStatuses(0.75f);
            afterRefresh = target.CrowdControl.SlowFraction;
            AgeStatuses(0.25f);
        });

        Assert.That(afterFirstBite, Is.EqualTo(0.3f));
        Assert.That(afterRefresh, Is.EqualTo(0.3f));
        Assert.That(target.CrowdControl.SlowFraction, Is.Zero);

        void AgeStatuses(float dt)
        {
            enemyClock += dt;
            _intents.Clear();
            _enemies.Tick(new EnemyTickContext(dt, enemyClock, _combat, _intents, _events, projectiles, _enemies, lanes));
        }
    }

    [Test]
    public void Boss_NotSlowed()
    {
        Set(PlayerStat.PackTacticsSlow, 0.3f);
        EnemyAgent boss = _enemies.SpawnBoss(BossId, Vector3.Zero);
        float now = -BiteInterval;
        AllocationAssert.None(() =>
        {
            boss.Health.Reset();
            Tick(now += BiteInterval);
        });

        Assert.That(DamageTo(boss), Is.EqualTo(12f).Within(0.001f));
        Assert.That(boss.CrowdControl.SlowFraction, Is.Zero);
    }

    [TestCase(0.7f, 7f)]
    [TestCase(-1f, 1f)]
    [TestCase(2f, 10f)]
    public void PetDamageTaken_Scales_Clamped(float multiplier, float expected)
    {
        Set(PlayerStat.PetDamageTaken, multiplier);
        foreach (PetAgent pet in _pets.Pets)
        {
            float hp = pet.Health.Current;
            DamageResult damage = _pets.ApplyDamage(pet.Id, 10f, 0f, 1);
            Assert.That(damage.Applied, Is.EqualTo(expected).Within(0.001f));
            Assert.That(pet.Health.Current, Is.EqualTo(hp - expected).Within(0.001f));
        }
    }

    private void Tick(float now)
    {
        _intents.Clear();
        _pets.Tick(BiteInterval, now, Vector3.Zero, _enemies, _combat);
    }

    private void ReportPosition(PetAgent pet, Vector3 position)
    {
        var snapshot = new WorldSnapshot(Capacity);
        ref EnemySense sense = ref snapshot.AddPet();
        sense.Id = pet.Id;
        sense.Position = position;
        _pets.Ingest(snapshot);
    }

    private void Set(PlayerStat stat, float value) => _pets.LeadStats.Resolve(stat).Base = value;

    private static float DamageTo(EnemyAgent target) => target.Health.MaxHp.Value - target.Health.Current;

    private static EnemySpec Body(ContentId id, EnemyBehaviourKind behaviour) =>
        new(id, new LocKey(id.Value + ".name"), 100f, 2f, 1, 4, 5f, false, 8f, 1.2f, 0.4f, 0.6f, 30f, behaviour);
}
