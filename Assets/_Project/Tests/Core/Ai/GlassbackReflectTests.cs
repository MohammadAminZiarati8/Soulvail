using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Common;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class GlassbackReflectTests
{
    private const int Capacity = 8;
    private const float Frame = 1f / 32f;
    private const float ShineSeconds = 2f;
    private const float BoltDamage = 6f;
    private const float BoltSpeed = 14f;
    private const float BoltRadius = 0.8f;
    private const float BoltMinFlight = 0.3f;
    private const float BoltInterval = 0.4f;
    private const float Tolerance = 0.00001f;

    private static readonly ContentId Glassback = new("enemy.glassback");
    private static readonly ContentId Scorpion = new("enemy.scorpion");
    private static readonly ContentId Bloater = new("enemy.bloater");
    private static readonly ContentId Arrow = new("projectile.arrow");

    private RecordingEvents _events;
    private IDomainEvents _domainEvents;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private PlayerStats _stats;
    private WorldSnapshot _snapshot;
    private EnemyAgent _agent;
    private float _now;

    private ChaserBehaviour Chaser => (ChaserBehaviour)_agent.Behaviour;

    [TestCase(false)]
    [TestCase(true)]
    public void NotShining_HeroDamageIsApplyDamage(bool glassback)
    {
        Build(glassback ? Glassback : Scorpion);
        Step();
        Assert.That(Chaser.State, Is.EqualTo(ChaserState.Chase));
        _events.Clear();

        DamageResult ordinary = _enemies.ApplyDamage(_agent.Id, 10f, _now, _player);
        object[] ordinaryEvents = new System.Collections.Generic.List<object>(_events.All).ToArray();

        _agent.Health.Reset();
        _events.Clear();
        DamageResult hero = Hit();
        Assert.That(hero, Is.EqualTo(ordinary));
        Assert.That(_events.All, Is.EqualTo(ordinaryEvents));
        Assert.That(ordinary.Applied, Is.EqualTo(10f));
    }

    [Test]
    public void Shining_HeroHit_BlocksAndQueues()
    {
        Build();
        Shine();

        DamageResult result = Hit();
        Assert.That(result.Blocked, Is.True);
        Assert.That(result.Applied, Is.Zero);
        Assert.That(_events.Single<EnemyDamaged>().Blocked, Is.True);
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Assert.That(_events.Count<EnemyReflected>(), Is.Zero);
        Step();
        ProjectileFired shot = _events.Single<ProjectileFired>();
        EnemyReflected reflection = _events.Single<EnemyReflected>();
        Assert.That(shot.SpecId, Is.EqualTo(Glassback));
        Assert.That(shot.SourceId, Is.EqualTo(_agent.Id));
        Assert.That(reflection.Id, Is.EqualTo(shot.SourceId));
        Assert.That(reflection.From, Is.EqualTo(shot.Origin));
        Assert.That(reflection.Landing, Is.EqualTo(shot.Target));
        Assert.That(reflection.Seconds, Is.EqualTo(shot.FlightTime));
        _projectiles.Tick(_now, _snapshot.PlayerPosition, _player, _enemies);
        Assert.That(_player.Blackboard.IncomingProjectiles, Is.EqualTo(1));
    }

    [Test]
    public void Bolt_AimsAtHeroWhenHit()
    {
        Build();
        Shine();
        Vector3 landing = new(1f, 2f, 3f);
        _snapshot.PlayerPosition = landing;
        TickPlayer();
        Hit();

        _snapshot.PlayerPosition = Vector3.UnitX * 3f;
        TickPlayer();
        Hit();
        Step();

        ProjectileFired shot = _events.Single<ProjectileFired>();
        Assert.That(shot.Target, Is.EqualTo(landing));
        Assert.That(shot.Origin, Is.EqualTo(_agent.Position));
    }

    [TestCase(0f)]
    [TestCase(1f)]
    public void Bolt_FlightNeverUnderMin(float distance)
    {
        Build(distance: distance);
        Shine();
        Hit();
        float firedAt = _now;
        Step();

        Assert.That(_events.Single<ProjectileFired>().FlightTime, Is.EqualTo(BoltMinFlight));
        _projectiles.Tick(firedAt + BoltMinFlight - Frame, _snapshot.PlayerPosition, _player, _enemies);
        Assert.That(_projectiles.InFlightCount, Is.EqualTo(1));
        _projectiles.Tick(firedAt + BoltMinFlight, _snapshot.PlayerPosition, _player, _enemies);
        Assert.That(_events.Single<ProjectileImpacted>().Hit, Is.True);
    }

    [Test]
    public void Bolt_Numbers()
    {
        const int depth = 10;
        Build(depth: depth);
        Shine();
        Hit();
        float firedAt = _now;
        Step();
        ProjectileFired shot = _events.Single<ProjectileFired>();
        float distance = Vector2.Distance(new Vector2(shot.Origin.X, shot.Origin.Z), new Vector2(shot.Target.X, shot.Target.Z));
        Assert.That(distance / shot.FlightTime, Is.EqualTo(BoltSpeed).Within(Tolerance));
        Assert.That(shot.Radius, Is.EqualTo(BoltRadius));

        float before = _player.Health.Current;
        _projectiles.Tick(firedAt + shot.FlightTime, _snapshot.PlayerPosition, _player, _enemies);
        Assert.That(before - _player.Health.Current,
            Is.EqualTo(BoltDamage * Scalings.Design().Damage.At(depth)).Within(Tolerance));
    }

    [Test]
    public void Interval_CapsBolts()
    {
        Build();
        Shine();

        int count = 0;
        float last = float.NegativeInfinity;
        for (int i = 0; i < 20; i++)
        {
            Assert.That(Hit().Blocked, Is.True);
            Hit();
            float firedAt = _now;
            Step();
            int current = _events.Count<EnemyReflected>();
            if (current != count)
            {
                Assert.That(current, Is.EqualTo(count + 1));
                Assert.That(firedAt - last, Is.GreaterThanOrEqualTo(BoltInterval));
                last = firedAt;
                count = current;
            }

            Step();
            Step();
            if ((i + 1) % 4 == 0)
            {
                // Four hits span 13 frames, just over one bolt interval.
                Step();
            }
        }

        Assert.That(count, Is.EqualTo((int)MathF.Ceiling(ShineSeconds / BoltInterval)));
    }

    [Test]
    public void PetBite_Shining_NoBolt()
    {
        Build(distance: 1f);
        Shine();
        var pets = new PetSystem(new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 1f, 1.4f, 1.5f),
            _domainEvents, _intents);
        pets.SpawnBeside(_agent.Position + (1.5f * Vector3.UnitZ), _now);

        pets.Tick(Frame, _now, _snapshot.PlayerPosition, _enemies, _player, Vector3.UnitZ);

        Assert.That(_events.Single<EnemyDamaged>().Blocked, Is.True);
        Step();
        AssertNoBolt();
    }

    [TestCase("Poison")]
    [TestCase("Hawk")]
    [TestCase("Blast")]
    public void Poison_Hawk_Blast_NoBolt(string source)
    {
        Build();
        Shine();

        switch (source)
        {
            case "Poison":
                Assert.That(_enemies.Poison(_agent.Id, 10f, 1f), Is.True);
                for (int i = 0; i <= Poison.PulseSeconds / Frame; i++)
                {
                    Step();
                }

                Assert.That(_events.Single<EnemyDamaged>().Blocked, Is.True);
                break;

            case "Hawk":
                Assert.That(_enemies.Mark(_agent.Id, 3f), Is.True);
                var hawk = new HawkSystem(_domainEvents, _stats);
                _stats.HawkDiveInterval.Base = Frame;
                hawk.Call(_now, 1f, 0f);
                hawk.Tick(Frame, _now + Frame, _snapshot.PlayerPosition, _enemies, _player);
                hawk.Tick(Frame, _now + Frame + HawkSystem.DiveSeconds, _snapshot.PlayerPosition, _enemies, _player);
                Assert.That(_events.Count<HawkStruck>(), Is.EqualTo(1));
                Assert.That(_events.Single<EnemyDamaged>().Blocked, Is.True);
                break;

            case "Blast":
                EnemyAgent bloater = _enemies.Spawn(Bloater, _agent.Position);
                _enemies.Execute(bloater.Id, _now, _player);
                Assert.That(_events.Count<EnemyExploded>(), Is.EqualTo(1));
                Assert.That(_events.Single<EnemyDamaged>().Id, Is.EqualTo(bloater.Id));
                break;
        }

        Step();
        AssertNoBolt();
    }

    [TestCase(ShotOrigin.Ranger)]
    [TestCase(ShotOrigin.Shade)]
    [TestCase(ShotOrigin.Echo)]
    [TestCase(ShotOrigin.Crossfire)]
    public void Arrow_ShiningShell_ReflectsThroughProjectileSystem(ShotOrigin origin)
    {
        Build(distance: 4f);
        Shine();
        Vector3 landing = Vector3.UnitX;
        var arrow = new Projectile(Arrow, 0, Vector3.Zero, _agent.Position, 32f, 0.5f, 10f,
            ShotSide.AtEnemies, shotOrigin: origin);
        _projectiles.Fire(arrow, _now);
        _events.Clear();

        for (int i = 0; i < 8 && _projectiles.InFlightCount > 0; i++)
        {
            Step();
            _projectiles.Tick(_now, landing, _player, _enemies);
        }

        Assert.That(_events.Single<EnemyDamaged>().Blocked, Is.True);
        Assert.That(_events.Count<EnemyReflected>(), Is.Zero);
        Step();
        Assert.That(_events.Single<EnemyReflected>().Landing, Is.EqualTo(landing));
    }

    [TestCase("Axe")]
    [TestCase("Cone")]
    [TestCase("Charge")]
    public void Axe_And_Swing_Reflect(string weapon)
    {
        var axe = new WeaponSpec(WeaponKind.ReturningAxe, 10f, 2f, 8f, 360f, 0f,
            shotRadius: 0.5f, axeSpeed: 20f, axeCarry: 2, axeHitsPerPass: 3);
        Build(distance: 2f, weapon: weapon == "Axe" ? axe : null);
        Shine();
        int[] hits = { _agent.Id };

        switch (weapon)
        {
            case "Axe":
                var axes = new AxeSystem(_domainEvents, _stats, axe);
                Assert.That(axes.TryThrowPair(Vector3.Zero, _agent.Position, 10f, _now), Is.EqualTo(2));
                axes.Tick(0.125f, _now, Vector3.Zero, _player, _enemies, null);
                break;

            case "Cone":
                for (int i = 0; i < 32 && _player.PendingConeRequestId < 0; i++)
                {
                    TickPlayer();
                    Step();
                }

                Assert.That(_player.PendingConeRequestId, Is.GreaterThanOrEqualTo(0));
                _player.ResolveConeHits(hits, _now, _enemies);
                break;

            case "Charge":
                _player.Charge.Request(_now);
                TickPlayer();
                Assert.That(_player.Charge.IsActive, Is.True);
                _player.ResolveChargeHits(hits, _now, _enemies);
                break;
        }

        Assert.That(_events.Count<EnemyDamaged>(), Is.Positive);
        Assert.That(_events.Of<EnemyDamaged>()[0].Blocked, Is.True);
        Step();
        Hit();
        Step();
        Assert.That(_events.Count<EnemyReflected>(), Is.EqualTo(1));
    }

    [TestCase(0f, false, false, true)]
    [TestCase(1.5f, false, false, false)]
    [TestCase(0f, true, false, false)]
    [TestCase(0f, false, true, true)]
    public void Bolt_LandsOnStillHero_MissesMovedHero(float movement, bool rolling, bool killShooter, bool hurt)
    {
        Build(distance: 1f);
        Shine();
        Hit();
        float firedAt = _now;
        Step();
        if (killShooter)
        {
            Assert.That(_enemies.Execute(_agent.Id, _now, _player).Killed, Is.True);
        }

        float arrivesAt = firedAt + _events.Single<ProjectileFired>().FlightTime;
        if (rolling)
        {
            _now = arrivesAt - Frame;
            _player.Charge.Request(_now);
            TickPlayer();
            Assert.That(_player.Health.IsInvulnerable, Is.True);
        }

        float before = _player.Health.Current;
        _projectiles.Tick(arrivesAt, movement * Vector3.UnitX, _player, _enemies);
        Assert.That(_player.Health.Current, Is.EqualTo(before - (hurt ? BoltDamage : 0f)));
        Assert.That(_projectiles.InFlightCount, Is.Zero);
    }

    [Test]
    public void PoolFull_DropsBolt()
    {
        Build();
        Shine();
        var filler = new Projectile(Glassback, 0, Vector3.Zero, 20f * Vector3.UnitX, 1f, 1f, 1f);
        for (int i = 0; i < _projectiles.Capacity; i++)
        {
            _projectiles.Fire(filler, _now);
        }

        _events.Clear();
        Hit();
        Step();

        Assert.That(_projectiles.InFlightCount, Is.EqualTo(_projectiles.Capacity));
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Assert.That(_events.Count<EnemyReflected>(), Is.Zero);
        _projectiles.Clear();
        Step();
        AssertNoBolt();
    }

    [TestCase("Reset")]
    [TestCase("Death")]
    [TestCase("End")]
    public void Reset_ClearsPending(string action)
    {
        Build();
        Shine();
        Hit();

        switch (action)
        {
            case "Reset":
                Chaser.Reset();
                break;
            case "Death":
                Assert.That(_enemies.Execute(_agent.Id, _now, _player).Killed, Is.True);
                break;
            case "End":
                _now += ShineSeconds;
                break;
        }

        Step();
        AssertNoBolt();
    }

    [Test]
    public void Reflect_NoAlloc()
    {
        Build(events: new SilentEvents());
        Shine();
        AllocationAssert.None(ReflectCycle, iterations: 40);
    }

    private void ReflectCycle()
    {
        for (int i = 0; i < 20; i++)
        {
            Hit();
        }

        Step();
        _projectiles.Clear();
        Chaser.Reset();
        for (int i = 0; i < 32 && Chaser.State != ChaserState.Shine; i++)
        {
            Step();
        }
    }

    private void AssertNoBolt()
    {
        Assert.That(_projectiles.InFlightCount, Is.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Assert.That(_events.Count<EnemyReflected>(), Is.Zero);
    }

    private void Build(ContentId id = default, float distance = 10f, int depth = 1,
        WeaponSpec weapon = null, IDomainEvents events = null)
    {
        _events = new RecordingEvents();
        _domainEvents = events ?? _events;
        _intents = new RecordingIntents();
        _now = 0f;
        _snapshot = new WorldSnapshot(Capacity) { Dt = Frame };
        CharacterSpec character = Character(weapon);
        var shine = new ShineSpec(0.5f, ShineSeconds, 1f, 4f, 12f, 2f,
            BoltDamage, BoltSpeed, BoltRadius, BoltMinFlight, BoltInterval);
        var catalog = new ContentCatalog(new[] { character }, new[]
        {
            Enemy(Glassback, shine: shine),
            Enemy(Scorpion),
            Enemy(Bloater, explosion: new ExplosionSpec(4f)),
        });
        _enemies = new EnemySystem(catalog, _domainEvents, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity)
        {
            Depth = depth,
        };
        _player = new PlayerCombat(character, _domainEvents, _intents, Capacity);
        _stats = new PlayerStats(_player, new PlayerMotor(character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), _domainEvents));
        _projectiles = new ProjectileSystem(_domainEvents, Capacity);
        _agent = _enemies.Spawn(id.Value is null ? Glassback : id, new Vector3(0f, 2f, distance));
    }

    private DamageResult Hit() => _enemies.ApplyHeroDamage(_agent.Id, 10f, _now, _player);

    private void Shine()
    {
        _player.Targeter.Focus(_agent.Id);
        TickPlayer();
        for (int i = 0; i < 32 && Chaser.State != ChaserState.Shine; i++)
        {
            Step();
        }

        Assert.That(Chaser.State, Is.EqualTo(ChaserState.Shine));
        _events.Clear();
    }

    private void TickPlayer() =>
        _player.Tick(0.125f, _now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);

    private void Step()
    {
        _enemies.Ingest(_snapshot, now: _now);
        _intents.Clear();
        _enemies.Tick(new EnemyTickContext(Frame, _now, _player, _intents, _domainEvents, _projectiles, _enemies));
        _now += Frame;
    }

    private static EnemySpec Enemy(ContentId id, ShineSpec shine = null, ExplosionSpec explosion = null) =>
        new(id, new LocKey("enemy.glassback.name"), maxHp: 200f, moveSpeed: 2.2f, targetPriority: 1, threatCost: 4,
            xpValue: 18f, isElite: false, contactDamage: 8f, reach: 1.5f, windupTime: 0.6f, recoverTime: 0.8f,
            aggroRange: 30f, behaviour: EnemyBehaviourKind.Chaser, shine: shine, explosion: explosion);

    private static CharacterSpec Character(WeaponSpec weapon) => new(
        new ContentId("character.oathbound"),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(20f, 3f, 2f, 1f, 1.5f, 0.1f),
        weapon ?? new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        null,
        0.5f);
}
