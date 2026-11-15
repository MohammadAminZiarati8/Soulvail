using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Support;
using Arena = Soulvail.Tests.Core.Ai.StormCallerBehaviourTests.Arena;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class TornadoFieldTests
{
    private Arena _arena;

    [SetUp]
    public void SetUp()
    {
        _arena = new Arena();
        _arena.Rise();
    }

    [Test]
    public void Drift_HalfSpeedTurnCapped()
    {
        Vector3 origin = _arena.State.TornadoAt(0).Position;
        _arena.Step(1f, -14f * Vector3.UnitZ);
        Vector3 travelled = _arena.State.TornadoAt(0).Position - origin;
        Assert.That(travelled.Length(), Is.EqualTo(1.6f).Within(0.0001f));
        Assert.That(Vector3.Normalize(travelled).Z, Is.EqualTo(0.5f).Within(0.0001f));
    }

    [Test]
    public void Pulse_TenPerSecond()
    {
        Vector3 hero = 3.6f * Vector3.UnitZ;
        _arena.Step(0.49f, hero);
        _arena.Player.ApplyDamage(1f, _arena.Now);
        _arena.Step(0.01f, hero);
        _arena.Step(0.5f, hero);
        Assert.That(_arena.Player.Health.Current, Is.EqualTo(89f));
        var hits = _arena.Events.Of<PlayerDamaged>();
        Assert.That(hits.Count, Is.EqualTo(3));
        Assert.That(hits[0].IsPulse, Is.False);
        Assert.That(hits[1].IsPulse && hits[2].IsPulse, Is.True);
    }

    [Test]
    public void Pulse_OutsideRingNone()
    {
        _arena.Step(1f, 5.6f * Vector3.UnitZ);
        Assert.That(_arena.Player.Health.Current, Is.EqualTo(100f));
        Assert.That(_arena.Events.Count<PlayerDamaged>(), Is.Zero);
    }

    [Test]
    public void Pulse_RollRefused()
    {
        _arena.StartSpaceMove();
        _arena.Step(1f, 3.6f * Vector3.UnitZ);
        Assert.That(_arena.Player.Health.Current, Is.EqualTo(100f));
        Assert.That(_arena.Events.Count<PlayerDamaged>(), Is.Zero);
    }

    [TestCase(2.5f, false, -1f)]
    [TestCase(3.5f, false, 0f)]
    [TestCase(2.5f, true, 0f)]
    [TestCase(0.1f, false, -0.4f)]
    public void Pull_WeakerThanWalking(float distance, bool spaceMove, float expectedZ)
    {
        if (spaceMove)
        {
            _arena.StartSpaceMove();
        }

        _arena.Step(0.25f, (2.4f + distance) * Vector3.UnitZ + 10f * Vector3.UnitY);
        _arena.Motor.Tick(0.25f, Vector2.UnitY, null);
        Assert.That(_arena.Motor.Velocity.Z, Is.EqualTo(3f + expectedZ).Within(0.0001f));
    }

    [Test]
    public void Drift_ClearedAfterTick()
    {
        _arena.Motor.AddDrift(Vector2.UnitX);
        _arena.Motor.Tick(0.02f, Vector2.Zero, null);
        Assert.That(_arena.Motor.Velocity, Is.EqualTo(Vector3.UnitX));
        _arena.Motor.Tick(0.02f, Vector2.Zero, null);
        Assert.That(_arena.Motor.Velocity, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void Pull_OverlappingTornadoesStopAtCentre()
    {
        var arena = new Arena();
        arena.Enemies.Spawn(StormCallerBehaviourTests.CallerId, Vector3.Zero);
        arena.Rise();
        Assert.That(arena.State.TornadoCount, Is.EqualTo(2));
        arena.Step(0.25f, 2.5f * Vector3.UnitZ);
        arena.Motor.Tick(0.25f, Vector2.Zero, null);
        Assert.That(arena.Motor.Velocity.Z, Is.EqualTo(-0.4f).Within(0.0001f));
    }

    [Test]
    public void NeverTouchesEnemies()
    {
        Vector3 position = 3.6f * Vector3.UnitZ;
        EnemyAgent dummy = _arena.Enemies.Spawn(StormCallerBehaviourTests.DummyId, position);
        var hound = new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 1f, 1.4f, 1.5f);
        _arena.Pets = new PetSystem(hound, _arena.Events, _arena.Intents);
        _arena.Pets.SpawnBeside(position, _arena.Now);
        PetAgent pet = _arena.Pets.Pets[0];
        _arena.Snapshot.AddPet() = new EnemySense { Id = pet.Id, Position = position };
        _arena.Pets.Ingest(_arena.Snapshot);
        float enemyHp = dummy.Health.Current;

        for (int i = 0; i < 4; i++)
        {
            _arena.Step(0.5f, position);
        }

        Assert.That(dummy.Health.Current, Is.EqualTo(enemyHp));
        Assert.That(pet.Health.Current, Is.EqualTo(70f));
        Assert.That(dummy.Position, Is.EqualTo(position));
        Assert.That(pet.Position, Is.EqualTo(position));
        Assert.That(_arena.Intents.PetMoves, Is.Empty);
        Assert.That(_arena.Player.Health.Current, Is.LessThan(100f));
    }

    [Test]
    public void NotATarget()
    {
        _arena.Step(0.25f, 3f * Vector3.UnitZ);
        Assert.That(_arena.State.TornadoCount, Is.EqualTo(1));
        // The live tornado is in range while cover excludes its caller from the candidate list.
        _arena.Snapshot.AddEnemy() = new EnemySense { Id = _arena.Caller.Id, BehindCover = true };
        _arena.Enemies.Ingest(_arena.Snapshot, now: _arena.Now);
        _arena.Player.Tick(0.25f, _arena.Now, _arena.Snapshot, _arena.Enemies.Registry.Alive, Vector3.UnitZ);
        Assert.That(_arena.Player.Targeter.CurrentTargetId, Is.EqualTo(-1));
    }

    [Test]
    public void End_After8()
    {
        TornadoReading raised = _arena.State.TornadoAt(0);
        _arena.Step(7.5f);
        Assert.That(_arena.State.TornadoCount, Is.EqualTo(1));
        _arena.Step(0.5f);
        Assert.That(_arena.State.TornadoCount, Is.Zero);
        var ended = _arena.Events.Single<TornadoEnded>();
        Assert.That(ended.TornadoId, Is.EqualTo(raised.Id));
        Assert.That(ended.OwnerId, Is.EqualTo(raised.OwnerId));
        Assert.That(ended.Collapsed, Is.False);
    }

    [Test]
    public void Collapse_OnDeath()
    {
        _arena.Step(0.25f, 3f * Vector3.UnitZ);
        _arena.Motor.Tick(0.25f, Vector2.Zero, null);
        Vector3 position = _arena.State.TornadoAt(0).Position;
        _arena.Hit(10000f);
        Assert.That(_arena.State.TornadoAt(0).Collapsing, Is.True);
        _arena.Step(0.25f, position + 0.5f * Vector3.UnitZ);
        Assert.That(_arena.State.TornadoAt(0).Position, Is.EqualTo(position));
        Assert.That(_arena.Player.Health.Current, Is.EqualTo(100f));
        _arena.Motor.Tick(0.25f, Vector2.Zero, null);
        Assert.That(_arena.Motor.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_arena.State.TornadoCount, Is.EqualTo(1));
        _arena.Step(0.15f, position);
        Assert.That(_arena.Events.Single<TornadoEnded>().Collapsed, Is.True);
        Assert.That(_arena.State.TornadoCount, Is.Zero);
    }

    [Test]
    public void Rechannel_SixAfterEnd()
    {
        _arena.Step(8f);
        _arena.Step(5.75f);
        Assert.That(_arena.Behaviour.State, Is.EqualTo(StormCallerPhase.Hold));
        _arena.Step(0.25f);
        Assert.That(_arena.Behaviour.State, Is.EqualTo(StormCallerPhase.Channel));
    }

    [Test]
    public void Clear_EmptiesField()
    {
        ContentId caller = StormCallerBehaviourTests.CallerId;
        EnemyAgent reserving = _arena.Enemies.Spawn(caller, 16f * Vector3.UnitZ);
        _arena.Step(0.25f, 3f * Vector3.UnitZ);
        Assert.That(((StormCallerBehaviour)reserving.Behaviour).State, Is.EqualTo(StormCallerPhase.Channel));
        _arena.Enemies.Clear();
        Assert.That(_arena.State.TornadoCount, Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => _arena.State.TornadoAt(0));
        Assert.That(_arena.Events.Count<TornadoEnded>(), Is.Zero);
        _arena.Motor.Tick(0.25f, Vector2.Zero, null);
        Assert.That(_arena.Motor.Velocity, Is.EqualTo(Vector3.Zero));

        _arena.Enemies.Spawn(caller, Vector3.Zero);
        _arena.Enemies.Spawn(caller, Vector3.UnitX);
        _arena.Rise();
        Assert.That(_arena.State.TornadoCount, Is.EqualTo(TornadoField.Capacity));
    }

    [Test]
    public void Tick_Cycle_NoAlloc()
    {
        var arena = new Arena(silent: true);
        AllocationAssert.None(() =>
        {
            arena.Behaviour.Reset();
            arena.Player.Reset();
            for (int cycle = 0; cycle < 2; cycle++)
            {
                arena.Rise();
                for (int i = 0; i < 16; i++)
                {
                    arena.Step(0.5f, 3.6f * Vector3.UnitZ);
                    arena.Motor.Tick(0.5f, Vector2.Zero, null);
                }

                // Wait out the cooldown while remaining outside channel reach.
                arena.Step(6f, 20f * Vector3.UnitZ);
                arena.Player.Reset();
            }
        }, 100);
    }
}
