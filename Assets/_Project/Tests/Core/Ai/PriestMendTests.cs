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
public sealed class PriestMendTests
{
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private EnemyAgent _priest;
    private IDomainEvents _sink;
    private float _now;
    private ShamanBehaviour Shaman => (ShamanBehaviour)_priest.Behaviour;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        SetUpShaman(DefaultMend(), _events);
    }

    [Test]
    public void NoMend_ShamanUnchanged()
    {
        SetUpShaman(null, _events);
        EnemyAgent ally = Ally(5f, 50f);

        Tick(2f);
        Tick(0.61f);

        Assert.That(_events.Single<ShamanCast>().ShamanId, Is.EqualTo(_priest.Id));
        Assert.That(_events.Single<ShamanPulsed>().Touched, Is.EqualTo(1));
        Assert.That(_events.Single<EnemyHealed>().Amount, Is.EqualTo(10f));
        Assert.That(ally.MoveSpeed.Value, Is.EqualTo(2.5f));
        Assert.That(_events.Count<PriestMendStarted>(), Is.Zero);
    }

    [Test]
    public void Spec_Guards()
    {
        Assert.Throws<ArgumentException>(() => Spec("chaser", EnemyBehaviourKind.Chaser, DefaultMend()));
        Assert.Throws<ArgumentException>(() => Spec("priest", EnemyBehaviourKind.Shaman, DefaultMend(), withShaman: false));
        float[] values = { 10f, 12f, 0.9f, 3f, 0.08f, 3f, 2f };
        for (int i = 0; i < values.Length; i++)
        {
            float valid = values[i];
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -1f })
            {
                values[i] = invalid;
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    new MendSpec(values[0], values[1], values[2], values[3], values[4], values[5], values[6]));
            }
            values[i] = valid;
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new MendSpec(10f, 12f, 1.01f, 3f, 0.08f, 3f, 2f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MendSpec(10f, 12f, 0.9f, 3f, 1.01f, 3f, 2f));
        Assert.That(new MendSpec(10f, 12f, 1f, 3f, 1f, 3f, 2f).HealPerSecond, Is.EqualTo(1f));
    }

    [Test]
    public void Mend_NoPulseNoHaste()
    {
        EnemyAgent ally = Ally(5f, 20f);
        for (int i = 0; i < 100; i++)
        {
            Tick(0.1f);
            Assert.That(_enemies.Has(ally.Id, new ContentId(EnemyStatuses.HastedKey)), Is.False);
        }

        Assert.That(_events.Count<PriestMendStarted>(), Is.GreaterThan(0));
        Assert.That(_events.Count<ShamanCast>(), Is.Zero);
        Assert.That(_events.Count<ShamanPulsed>(), Is.Zero);
    }

    [Test]
    public void Pick_MostHurtInRange()
    {
        EnemyAgent expected = SpawnHurt(8f, 50f, EnemyBehaviourKind.Static, height: 50f);
        Ally(11f, 30f);
        Ally(5f, 60f);
        Tick(0f);

        PriestMendStarted started = _events.Single<PriestMendStarted>();
        Assert.That(Shaman.MendTargetId, Is.EqualTo(expected.Id));
        Assert.That(started.Id, Is.EqualTo(_priest.Id));
        Assert.That(started.AllyId, Is.EqualTo(expected.Id));
        Assert.That(started.Seconds, Is.EqualTo(3f));
    }

    [Test]
    public void Pick_TieToNearest()
    {
        Ally(8f, 40f);
        EnemyAgent expected = Ally(5f, 40f);
        Tick(0f);
        Assert.That(Shaman.MendTargetId, Is.EqualTo(expected.Id));
    }

    [Test]
    public void Pick_IgnoresHealthyAndBosses()
    {
        Ally(5f, 95f);
        Ally(6f, 90f);
        SpawnHurt(2f, 10f, EnemyBehaviourKind.Boss);
        SpawnHurt(3f, 10f, EnemyBehaviourKind.Shaman);
        Ally(4f, 0f);
        Tick(0f);
        Assert.That(Shaman.MendTargetId, Is.EqualTo(-1));
        Assert.That(_events.Count<PriestMendStarted>(), Is.Zero);
    }

    [Test]
    public void Mend_HealsEightPercentPerSecond()
    {
        EnemyAgent ally = Ally(5f, 20f);
        Tick(0f);
        float reported = 0f;
        float previousReport = float.NegativeInfinity;
        int reportCount = 0;
        for (int i = 0; i < 24; i++)
        {
            Tick(0.125f);
            int count = _events.Count<EnemyHealed>();
            if (count != reportCount)
            {
                Assert.That(count, Is.EqualTo(reportCount + 1));
                Assert.That(_now - previousReport, Is.GreaterThanOrEqualTo(0.25f));
                EnemyHealed heal = _events.Of<EnemyHealed>()[reportCount];
                Assert.That(heal.EnemyId, Is.EqualTo(ally.Id));
                Assert.That(heal.HpFraction, Is.EqualTo(ally.Health.Fraction));
                reported += heal.Amount;
                previousReport = _now;
                reportCount = count;
            }
        }

        Assert.That(ally.Health.Current, Is.EqualTo(44f).Within(0.001f));
        AssertEnd(ally, MendEndReason.Done);
        Tick(0.125f);
        Assert.That(reported + _events.Of<EnemyHealed>()[reportCount].Amount, Is.EqualTo(24f).Within(0.001f));
    }

    [Test]
    public void Mend_EndsWhenFull()
    {
        EnemyAgent ally = Ally(5f, 85f);
        Tick(0f);
        Tick(2f);
        Assert.That(ally.Health.Fraction, Is.EqualTo(1f));
        Assert.That(_events.Single<EnemyHealed>().Amount, Is.EqualTo(15f));
        AssertEnd(ally, MendEndReason.Healed);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Mend_LostOnDeathOrRange(bool kill)
    {
        EnemyAgent ally = Ally(5f, 20f);
        Tick(0f);
        if (kill)
        {
            _enemies.Execute(ally.Id, _now, _player);
        }
        else
        {
            var snapshot = new WorldSnapshot(16);
            snapshot.AddEnemy() = new EnemySense { Id = ally.Id, Position = ally.Position + Vector3.UnitX * 8f };
            _enemies.Ingest(snapshot);
        }
        Tick(0.1f);
        AssertEnd(ally, MendEndReason.Lost);
    }

    [Test]
    public void Mend_BrokenByHeroHit()
    {
        EnemyAgent ally = Ally(5f, 20f);
        Tick(0f);
        _enemies.ApplyHeroDamage(_priest.Id, 5f, _now, _player);
        AssertEnd(ally, MendEndReason.Broken);

        Tick(2f);
        Tick(2.99f);
        Assert.That(_events.Count<PriestMendStarted>(), Is.EqualTo(1));
        Tick(0.02f);
        Assert.That(Shaman.MendTargetId, Is.EqualTo(ally.Id));
    }

    [Test]
    public void Mend_BrokenByPetDamageEvenIfHealedBetweenTicks()
    {
        EnemyAgent ally = Ally(5f, 20f);
        Tick(0f);
        _enemies.ApplyDamageFrom(_priest.Id, 5f, _now, _player, ally.Position);
        _priest.Health.Heal(5f);
        Tick(0.1f);
        Assert.That(_priest.Health.Fraction, Is.EqualTo(1f));
        AssertEnd(ally, MendEndReason.Broken);
    }

    [Test]
    public void Mend_HealthDropBetweenBehaviourTicksBreaks()
    {
        EnemyAgent ally = Ally(5f, 20f);
        Tick(0f);
        _priest.Health.ApplyDamage(5f, _now);
        Tick(0.1f);
        AssertEnd(ally, MendEndReason.Broken);
    }

    [Test]
    public void Mend_BrokenByPoison()
    {
        EnemyAgent ally = Ally(5f, 20f);
        Tick(0f);
        Assert.That(_enemies.Poison(_priest.Id, 4f, 6f), Is.True);
        for (int i = 0; i < 11; i++)
        {
            _now += 0.5f;
            _intents.Clear();
            _enemies.Tick(Context(0.5f));
            Assert.That(Shaman.MendTargetId, Is.EqualTo(-1));
        }
        Assert.That(_priest.Health.Current, Is.LessThan(90f));
        Assert.That(_events.Count<PriestMendStarted>(), Is.EqualTo(1));
        AssertEnd(ally, MendEndReason.Broken);
    }

    [Test]
    public void Mend_DamageBeforeChannelBlocksAndHealingDoesNotBreak()
    {
        EnemyAgent ally = Ally(5f, 20f);
        _enemies.ApplyDamage(_priest.Id, 5f, _now, _player);
        Tick(4.9f);
        Assert.That(Shaman.MendTargetId, Is.EqualTo(-1));
        Tick(0.2f);
        _priest.Health.Heal(5f);
        Tick(0.1f);
        Assert.That(Shaman.MendTargetId, Is.EqualTo(ally.Id));
        Assert.That(_events.Count<PriestMendEnded>(), Is.Zero);
    }

    [Test]
    public void Mend_AnchorIsTarget()
    {
        SpawnHurt(2f, 60f, EnemyBehaviourKind.Bulwark);
        EnemyAgent ally = Ally(5f, 20f);
        Tick(0f);
        Tick(0.6f);
        Assert.That(Shaman.AnchorId, Is.EqualTo(ally.Id));
        Assert.That(Shaman.MendTargetId, Is.EqualTo(ally.Id));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitX));
    }

    [Test]
    public void Mend_FleeEnds()
    {
        EnemyAgent ally = Ally(5f, 20f);
        Tick(0f);
        _priest.Blackboard.DistanceToPlayer = 3f;
        Tick(0.1f);
        AssertEnd(ally, MendEndReason.Lost);
        Assert.That(Shaman.State, Is.EqualTo(ShamanState.Flee));
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.EqualTo(2.8f).Within(0.001f));

        _priest.Blackboard.DistanceToPlayer = 12f;
        Tick(2f);
        Tick(1.1f);
        Assert.That(Shaman.MendTargetId, Is.EqualTo(ally.Id));
    }

    [Test]
    public void Death_MidMend_EndsOnce()
    {
        EnemyAgent ally = Ally(5f, 20f);
        Tick(0f);
        _enemies.ApplyDamage(_priest.Id, 100f, _now, _player);
        AssertEnd(ally, MendEndReason.Lost);
        _enemies.Despawn(_priest.Id);
        Shaman.Reset();
        Assert.That(_events.Count<PriestMendEnded>(), Is.EqualTo(1));
    }

    [Test]
    public void Reset_MidMend_EndsOnce()
    {
        EnemyAgent ally = Ally(5f, 20f);
        Tick(0f);
        Shaman.Reset();
        Shaman.Reset();
        AssertEnd(ally, MendEndReason.Lost);
        Assert.That(Shaman.AnchorId, Is.EqualTo(-1));
    }

    [Test]
    public void Tick_Mend_NoAlloc()
    {
        SetUpShaman(DefaultMend(), new SilentEvents());
        EnemyAgent ally = Ally(5f, 20f);
        AllocationAssert.None(() =>
        {
            ally.Health.ApplyDamage(80f, _now);
            Tick(0f);
            Tick(3f);
            Tick(3f);
        }, 300);
    }

    private void AssertEnd(EnemyAgent ally, MendEndReason reason)
    {
        PriestMendEnded ended = _events.Single<PriestMendEnded>();
        Assert.That(ended.Id, Is.EqualTo(_priest.Id));
        Assert.That(ended.AllyId, Is.EqualTo(ally.Id));
        Assert.That(ended.Reason, Is.EqualTo(reason));
        Assert.That(Shaman.MendTargetId, Is.EqualTo(-1));
    }

    private void SetUpShaman(MendSpec mend, IDomainEvents sink)
    {
        _sink = sink;
        _now = 0f;
        _intents = new RecordingIntents();
        CharacterSpec character = Character();
        EnemySpec priest = Spec("priest", EnemyBehaviourKind.Shaman, mend);
        var catalog = new ContentCatalog(new[] { character }, new[] { priest });
        _enemies = new EnemySystem(catalog, sink, new FixedRandom(), new DepthScaling(Scalings.Design()), 16);
        _player = new PlayerCombat(character, sink, _intents, 16);
        _projectiles = new ProjectileSystem(sink, 16);
        _priest = _enemies.Spawn(priest.Id, new Vector3(0f, 0f, 12f));
        _priest.Blackboard.DistanceToPlayer = 12f;
        _priest.Blackboard.DirectionToPlayer = -Vector2.UnitY;
    }

    private EnemyAgent Ally(float distance, float hp) => SpawnHurt(distance, hp, EnemyBehaviourKind.Static);

    private EnemyAgent SpawnHurt(float distance, float hp, EnemyBehaviourKind kind, float height = 0f)
    {
        EnemyAgent ally = _enemies.Registry.Spawn(Spec("ally", kind), _priest.Position + new Vector3(distance, height, 0f));
        ally.Health.ApplyDamage(100f - hp, _now);
        return ally;
    }

    private void Tick(float dt)
    {
        _now += dt;
        _intents.Clear();
        Shaman.Tick(Context(dt));
    }

    private EnemyTickContext Context(float dt) =>
        new EnemyTickContext(dt, _now, _player, _intents, _sink, _projectiles, _enemies);

    private static MendSpec DefaultMend() => new MendSpec(10f, 12f, 0.9f, 3f, 0.08f, 3f, 2f);

    private static EnemySpec Spec(string name, EnemyBehaviourKind kind, MendSpec mend = null, bool withShaman = true) =>
        new EnemySpec(new ContentId("enemy." + name), new LocKey("enemy." + name + ".name"),
            100f, 2f, 3, 16, 32, false, 0f, 1f, 0.7f, 1f, 30f, kind,
            bulwark: kind == EnemyBehaviourKind.Bulwark ? new BulwarkSpec(60, 75, 45) : null,
            shaman: kind == EnemyBehaviourKind.Shaman && withShaman
                ? new ShamanSpec(8, 3, 0.6f, 0.1f, 0.25f, 3.5f, 11, 2.5f, 4, 1.2f, 1.4f) : null,
            mend: mend);

    private static CharacterSpec Character() => new CharacterSpec(
        new ContentId("character.oathbound"), new LocKey("character.oathbound.name"), new LocKey("character.oathbound.description"), 140f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f), new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
