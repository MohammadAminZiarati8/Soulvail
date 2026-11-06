using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class FadingBurstTests
{
    private const int Capacity = 8;
    private const float WeaponDamage = 20f;
    private static readonly ContentId BossBodyId = new("enemy.burst_boss");
    private static readonly ContentId BossId = new("boss.burst_test");
    private static readonly ContentId GuardId = new("enemy.burst_guard");

    private RecordingEvents _events;
    private EnemySystem _enemies;
    private ProjectileSystem _projectiles;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private PlayerStats _stats;
    private ShadeSystem _shades;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        Initialise(_events);
    }

    [TestCase(ShadeKind.Image)]
    [TestCase(ShadeKind.Decoy)]
    [TestCase(ShadeKind.Crossfire)]
    [TestCase(ShadeKind.Hall)]
    public void Expired_Bursts_EnemiesInThreeMetres(ShadeKind kind)
    {
        LearnRank(1);
        _player.Weapon.Damage.Base = WeaponDamage;
        EnemyAgent near = Spawn(Vector3.UnitZ);
        EnemyAgent touching = Spawn(new Vector3(near.Spec.BodyRadius + 3f, 20f, 0f));
        float hp = near.Health.Current;
        float edgeHp = touching.Health.Current;
        _shades.Spawn(kind, Vector3.Zero, 0f, 1f, 0f);
        _shades.Tick(1f, _enemies, _player);
        _shades.Tick(2f, _enemies, _player);

        Assert.That(near.Health.Current, Is.EqualTo(hp - 30f));
        Assert.That(touching.Health.Current, Is.EqualTo(edgeHp - 30f));
        Assert.That(_events.Count<ShadeBurst>(), Is.EqualTo(1));
    }

    [Test]
    public void Enemy_AtFourMetres_Untouched()
    {
        LearnRank(1);
        EnemyAgent far = Spawn(4f * Vector3.UnitZ);
        float hp = far.Health.Current;

        ExpireShade();

        Assert.That(far.Health.Current, Is.EqualTo(hp));
        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
    }

    [Test]
    public void Burst_Kills_PayXp()
    {
        LearnRank(1);
        EnemyAgent enemy = Spawn(Vector3.UnitX);
        enemy.Health.ApplyDamage(enemy.Health.Current - 1f, 0f);

        ExpireShade();

        Assert.That(enemy.IsAlive, Is.False);
        Assert.That(_enemies.DrainXp(), Is.EqualTo(enemy.Spec.XpValue));
        Assert.That(_events.Single<EnemyDied>().Id, Is.EqualTo(enemy.Id));
    }

    [Test]
    public void Burst_GuardedEnemy_AsAnyPlayerHit()
    {
        LearnRank(3);
        EnemyAgent guard = _enemies.Spawn(GuardId, Vector3.UnitX);
        guard.Health.SetExternalInvulnerable(true);
        float hp = guard.Health.Current;

        ExpireShade();

        Assert.That(guard.Health.Current, Is.EqualTo(hp));
        Assert.That(_events.Single<EnemyDamaged>().Blocked, Is.True);
        Assert.That(_enemies.Remaining(guard.Id, _enemies.Statuses.Slowed.Id), Is.Zero);
    }

    [Test]
    public void Replaced_NoBurst()
    {
        LearnRank(3);
        Spawn(Vector3.Zero);
        for (int i = 0; i <= ShadeSystem.Capacity; i++)
        {
            _shades.Spawn(ShadeKind.Crossfire, Vector3.Zero, 0f, 10f, 0f);
        }

        Assert.That(_events.Single<ShadeFaded>().Expired, Is.False);
        Assert.That(_events.Count<ShadeBurst>(), Is.Zero);
        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
    }

    [Test]
    public void Cleared_NoBurst()
    {
        LearnRank(3);
        Spawn(Vector3.Zero);
        _shades.Spawn(ShadeKind.Decoy, Vector3.Zero, 0f, 1f, 0f);

        _shades.Clear();
        _shades.Tick(1f, _enemies, _player);

        Assert.That(_events.Single<ShadeFaded>().Expired, Is.False);
        Assert.That(_events.Count<ShadeBurst>(), Is.Zero);
        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
    }

    [TestCase(false, TestName = "Rank3_Slows_FortyPercent_TwoSeconds")]
    [TestCase(true, TestName = "Rank3_Boss_HalfSlow")]
    public void Rank3_Slows(bool isBoss)
    {
        LearnRank(3);
        EnemyAgent enemy = isBoss ? _enemies.SpawnBoss(BossId, Vector3.UnitX) : Spawn(Vector3.UnitX);
        float speed = enemy.MoveSpeed.Value;
        float duration = isBoss ? 1f : 2f;

        ExpireShade();

        Assert.That(enemy.MoveSpeed.Value, Is.EqualTo(speed * 0.6f).Within(1e-5f));
        Assert.That(_enemies.Remaining(enemy.Id, _enemies.Statuses.Slowed.Id), Is.EqualTo(duration));
        Assert.That(_events.Single<ShadeBurst>().Slows, Is.True);
        TickEnemies(duration - 0.25f, duration + 0.75f, _events);
        Assert.That(enemy.MoveSpeed.Value, Is.EqualTo(speed * 0.6f).Within(1e-5f));
        TickEnemies(0.25f, duration + 1f, _events);
        Assert.That(enemy.MoveSpeed.Value, Is.EqualTo(speed));
    }

    [Test]
    public void Burst_PublishedAfterFaded_EvenWithNoEnemy()
    {
        LearnRank(1);
        Vector3 position = Vector3.One;
        _shades.Spawn(ShadeKind.Crossfire, position, 0f, 1f, 0f);

        _shades.Tick(1f, _enemies, _player);

        Assert.That(_events.All[^2], Is.TypeOf<ShadeFaded>());
        Assert.That(_events.Single<ShadeFaded>().Expired, Is.True);
        Assert.That(_events.All[^1], Is.TypeOf<ShadeBurst>());
        ShadeBurst burst = _events.Single<ShadeBurst>();
        Assert.That(burst.Position, Is.EqualTo(position));
        Assert.That(burst.Radius, Is.EqualTo(3f));
        Assert.That(burst.Slows, Is.False);
    }

    [TestCase(2, 40f, TestName = "Rank2_TwoTimesWeapon")]
    [TestCase(3, 50f, TestName = "Rank3_TwoAndAHalfTimesWeapon")]
    public void Rank_DamageUsesLiveWeapon(int rank, float expected)
    {
        LearnRank(rank);
        Spawn(Vector3.Zero);
        _shades.Spawn(ShadeKind.Crossfire, Vector3.Zero, 0f, 1f, 0f);
        _player.Weapon.Damage.Base = WeaponDamage;

        _shades.Tick(1f, _enemies, _player);

        Assert.That(_events.Single<EnemyDamaged>().Amount, Is.EqualTo(expected));
    }

    [Test]
    public void Unlearned_DoesNotBurst()
    {
        Spawn(Vector3.Zero);

        ExpireShade();

        Assert.That(_stats.Resolve(PlayerStat.FadeBurst).Base, Is.Zero);
        Assert.That(_stats.Resolve(PlayerStat.FadeBurstSlow).Base, Is.Zero);
        Assert.That(_events.Single<ShadeFaded>().Expired, Is.True);
        Assert.That(_events.Count<ShadeBurst>(), Is.Zero);
        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
    }

    [Test]
    public void Burst_DoesNotAllocate()
    {
        var events = new SilentEvents();
        Initialise(events);
        LearnRank(3);
        EnemyAgent enemy = Spawn(Vector3.UnitZ);
        float now = 0f;

        AllocationAssert.None(() =>
        {
            enemy.Health.Reset();
            TickEnemies(2f, now, events);
            _shades.Spawn(ShadeKind.Crossfire, Vector3.Zero, now, 1f, 0f);
            _shades.Tick(now + 1f, _enemies, _player);
            now += 3f;
        });
    }

    private void Initialise(IDomainEvents events)
    {
        CharacterSpec character = ShadeSystemTests.Character();
        var bossBody = new EnemySpec(BossBodyId, new LocKey("enemy.burst_boss.name"),
            900f, 2f, 8, 40, 120f, false, 20f, 2f, 1f, 1f, 30f, EnemyBehaviourKind.Boss);
        var guard = new EnemySpec(GuardId, new LocKey("enemy.burst_guard.name"),
            1000f, 2f, 3, 14, 12f, false, 8f, 1.2f, 0.7f, 1.5f, 30f, EnemyBehaviourKind.Bulwark,
            bulwark: new BulwarkSpec(60f, 75f, 45f));
        var boss = new BossSpec(BossId, BossBodyId, new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) }, 1.5f);
        var catalog = new ContentCatalog(new[] { character }, new[] { ShadeSystemTests.Body(), bossBody, guard },
            bosses: new[] { boss });
        _intents = new RecordingIntents();
        _player = new PlayerCombat(character, events, _intents, Capacity);
        _stats = new PlayerStats(_player, new PlayerMotor(character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), events));
        _enemies = new EnemySystem(catalog, events, new FixedRandom(7), new DepthScaling(Scalings.Design()), Capacity);
        _projectiles = new ProjectileSystem(events, Capacity);
        _shades = new ShadeSystem(_projectiles, new LureSystem(events), _stats, character, events);
    }

    private void TickEnemies(float dt, float now, IDomainEvents events)
    {
        _intents.Clear();
        _enemies.Tick(new EnemyTickContext(dt, now, _player, _intents, events, _projectiles, _enemies));
    }

    private EnemyAgent Spawn(Vector3 position) => _enemies.Spawn(ShadeSystemTests.EnemyId, position);

    private void ExpireShade()
    {
        _player.Weapon.Damage.Base = WeaponDamage;
        _shades.Spawn(ShadeKind.Crossfire, Vector3.Zero, 0f, 1f, 0f);
        _shades.Tick(1f, _enemies, _player);
    }

    private void LearnRank(int rank)
    {
        Stat damage = _stats.Resolve(PlayerStat.FadeBurst);
        damage.Add(new Modifier(ModifierKind.Flat, 1.5f, this));
        for (int i = 1; i < rank; i++)
        {
            damage.Add(new Modifier(ModifierKind.Flat, 0.5f, this));
        }

        if (rank == 3)
        {
            _stats.Resolve(PlayerStat.FadeBurstSlow).Add(new Modifier(ModifierKind.Flat, 0.4f, this));
        }
    }
}
