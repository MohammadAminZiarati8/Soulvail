using System;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class StormCallerBehaviourTests
{
    private Arena _arena;

    [SetUp]
    public void SetUp() => _arena = new Arena();

    [Test]
    public void Spec_Guards()
    {
        Assert.Throws<ArgumentException>(() => Enemy(EnemyBehaviourKind.StormCaller, null));
        Assert.Throws<ArgumentException>(() => Enemy(EnemyBehaviourKind.Chaser, Spec()));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(hold: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(hold: float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(hold: 0f));
        Assert.Throws<ArgumentException>(() => Spec(hold: 6f));
        Assert.Throws<ArgumentException>(() => Spec(hold: 16f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(pull: 1.5f));
    }

    [Test]
    public void Hold_WalksTo12()
    {
        _arena.Step(2f, 20f * Vector3.UnitZ);
        Assert.That(_arena.Intents.LastEnemyMove.Velocity, Is.EqualTo(2f * Vector3.UnitZ));
        Assert.That(_arena.Intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitY));
        // Occupied slots keep this caller in Hold once it walks into channel range.
        _arena.Enemies.Spawn(CallerId, 32f * Vector3.UnitZ);
        _arena.Enemies.Spawn(CallerId, 33f * Vector3.UnitZ);
        _arena.Step(0.25f, 20f * Vector3.UnitZ);
        _arena.Step(3f, 20f * Vector3.UnitZ, body: 4f * Vector3.UnitZ);
        Assert.That(_arena.Intents.EnemyMoves[0].Velocity.Z, Is.EqualTo(4f / 3f));
        _arena.Step(0.25f, 20f * Vector3.UnitZ, body: 8f * Vector3.UnitZ);
        Assert.That(_arena.Intents.EnemyMoves[0].Velocity, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void Flee_Inside6()
    {
        _arena.Step(0.2f, 5f * Vector3.UnitZ);
        var phase = _arena.Events.Single<StormCallerPhaseChanged>();
        Assert.That(phase.Phase, Is.EqualTo(StormCallerPhase.Flee));
        Assert.That(phase.Seconds, Is.EqualTo(1.2f));
        Assert.That(_arena.Intents.LastEnemyMove.Velocity, Is.EqualTo(-2.8f * Vector3.UnitZ));
        _arena.Step(0.99f, 14f * Vector3.UnitZ);
        Assert.That(_arena.Behaviour.State, Is.EqualTo(StormCallerPhase.Flee));
        _arena.Step(0.02f, 14f * Vector3.UnitZ);
        Assert.That(_arena.Behaviour.State, Is.EqualTo(StormCallerPhase.Hold));
    }

    [Test]
    public void Channel_StartsInReach()
    {
        _arena.Step(0.25f, 14f * Vector3.UnitZ);
        var phase = _arena.Events.Single<StormCallerPhaseChanged>();
        Assert.That(phase.Id, Is.EqualTo(_arena.Caller.Id));
        Assert.That(phase.Phase, Is.EqualTo(StormCallerPhase.Channel));
        Assert.That(phase.Seconds, Is.EqualTo(2f));
        Assert.That(_arena.Intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_arena.State.TornadoCount, Is.Zero);
    }

    [Test]
    public void Channel_HitBreaks()
    {
        _arena.Step(0.25f);
        _arena.Step(1f);
        _arena.Hit(0f);
        Assert.That(_arena.Behaviour.State, Is.EqualTo(StormCallerPhase.Channel));
        _arena.Hit(1f);
        _arena.Hit(1f);
        AssertBroken();
    }

    [Test]
    public void Channel_FleeBreaks()
    {
        _arena.Step(0.25f);
        _arena.Step(1f, 5f * Vector3.UnitZ);
        Assert.That(_arena.Behaviour.State, Is.EqualTo(StormCallerPhase.Flee));
        AssertBroken();
    }

    [Test]
    public void Channel_RisesTornado()
    {
        _arena.Step(0.25f);
        _arena.Step(1f);
        Assert.That(_arena.State.TornadoCount, Is.Zero);
        _arena.Step(1f, 14f * Vector3.UnitX);
        var rose = _arena.Events.Single<TornadoRose>();
        Assert.That(rose.OwnerId, Is.EqualTo(_arena.Caller.Id));
        Assert.That(rose.Position, Is.EqualTo(2f * Vector3.UnitX));
        Assert.That(rose.Radius, Is.EqualTo(1.5f));
        Assert.That(rose.Seconds, Is.EqualTo(8f));
        Assert.That(_arena.State.TornadoAt(0).Position, Is.EqualTo(rose.Position));
    }

    [Test]
    public void Cap_TwoInTheArena()
    {
        EnemyAgent second = _arena.Enemies.Spawn(CallerId, Vector3.UnitX);
        EnemyAgent third = _arena.Enemies.Spawn(CallerId, -Vector3.UnitX);
        _arena.Step(0.25f);
        Assert.That(_arena.Behaviour.State, Is.EqualTo(StormCallerPhase.Channel));
        Assert.That(((StormCallerBehaviour)second.Behaviour).State, Is.EqualTo(StormCallerPhase.Channel));
        Assert.That(((StormCallerBehaviour)third.Behaviour).State, Is.EqualTo(StormCallerPhase.Hold));
        _arena.Step(2f);
        Assert.That(_arena.State.TornadoCount, Is.EqualTo(TornadoField.Capacity));
    }

    [Test]
    public void Cap_OnePerCaller()
    {
        _arena.Rise();
        _arena.Step(0.25f);
        Assert.That(_arena.Behaviour.State, Is.EqualTo(StormCallerPhase.Hold));
        Assert.That(_arena.State.TornadoCount, Is.EqualTo(1));
        EnemyAgent second = _arena.Enemies.Spawn(CallerId, Vector3.UnitX);
        _arena.Step(0.25f);
        Assert.That(((StormCallerBehaviour)second.Behaviour).State, Is.EqualTo(StormCallerPhase.Channel));
    }

    [Test]
    public void Reset_Caller()
    {
        _arena.Step(0.25f);
        _arena.Hit(1f);
        _arena.Behaviour.Reset();
        Assert.That(_arena.Behaviour.State, Is.EqualTo(StormCallerPhase.Hold));
        _arena.Step(0.25f);
        Assert.That(_arena.Behaviour.State, Is.EqualTo(StormCallerPhase.Channel));
        _arena.Behaviour.Reset();
        _arena.Step(0.25f);
        Assert.That(_arena.Behaviour.State, Is.EqualTo(StormCallerPhase.Channel), "reset releases the channel's slot");
    }

    private void AssertBroken()
    {
        Assert.That(_arena.Events.Single<StormChannelBroken>().Id, Is.EqualTo(_arena.Caller.Id));
        _arena.Step(2.75f);
        Assert.That(_arena.State.TornadoCount, Is.Zero);
        Assert.That(_arena.Behaviour.State, Is.EqualTo(StormCallerPhase.Hold));
        Assert.That(_arena.Events.Count<TornadoRose>(), Is.Zero);
        _arena.Step(0.25f);
        Assert.That(_arena.Behaviour.State, Is.EqualTo(StormCallerPhase.Channel));
    }

    private static StormCallSpec Spec(float hold = 12f, float pull = 3f) =>
        new(hold, 6f, 1.2f, 1.4f, 16f, 2f, 3f, 6f, 2f, 1.6f, 60f, 8f, 1.5f, 10f, pull, 1f, 0.4f);

    internal static readonly ContentId CallerId = new("enemy.caller");
    internal static readonly ContentId DummyId = new("enemy.dummy");

    private static EnemySpec Enemy(EnemyBehaviourKind kind, StormCallSpec spec) =>
        new(spec is null ? DummyId : CallerId, new LocKey("enemy.test.name"),
            55f, 2f, 7, 15, 31f, false, 0f, 1f, 0f, 0f, 40f, kind, stormCall: spec);

    internal sealed class Arena
    {
        private const int Capacity = 8;
        private readonly IDomainEvents _events;
        private readonly ProjectileSystem _projectiles;

        internal Arena(bool silent = false)
        {
            _events = silent ? new SilentEvents() : Events;
            var hero = new CharacterSpec(new ContentId("character.test"), new LocKey("character.test.name"),
                new LocKey("character.test.description"), 100f, Movement,
                new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
                new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
                new FocusSpec(0.4f, 1f, 1f),
                new MovementSkillSpec(MovementSkillKind.Charge, 10f, 1f, 2.5f, 0.15f, 20f, 5f, 0.05f),
                hitIFrames: 0.2f);
            var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
                1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
            EnemySpec[] enemies = { Enemy(EnemyBehaviourKind.StormCaller, Spec()), Enemy(EnemyBehaviourKind.Static, null) };
            var catalog = new ContentCatalog(new[] { hero }, enemies, new[] { mode });
            var random = new FixedRandom(0.5f);
            var recorder = new RunRecorder(random, new FixedClock(default), _events);
            var session = new RunSession(catalog, random, _events, Intents, recorder, Capacity, Capacity, Capacity);
            session.Start(new RunConfig(mode.Id, hero.Id, 0, 1, new SpawnPlan(Array.Empty<SpawnPlan.Entry>()), restore: null));
            State = session.State;
            Enemies = RunService<EnemySystem>("Enemies");
            Player = RunService<PlayerCombat>("Combat");
            Motor = RunService<PlayerMotor>("Motor");
            Caller = Enemies.Spawn(CallerId, Vector3.Zero);
            _projectiles = new ProjectileSystem(_events, Capacity);
        }

        private static MovementSpec Movement => new(3f, 0.06f, 0.08f, 720f);
        internal RecordingEvents Events { get; } = new();
        internal RecordingIntents Intents { get; } = new();
        internal RunState State { get; }
        internal EnemySystem Enemies { get; }
        internal EnemyAgent Caller { get; }
        internal StormCallerBehaviour Behaviour => (StormCallerBehaviour)Caller.Behaviour;
        internal PlayerCombat Player { get; }
        internal PlayerMotor Motor { get; }
        internal WorldSnapshot Snapshot { get; } = new(Capacity);
        internal PetSystem Pets { get; set; }
        internal float Now { get; private set; }

        // Use the real run wiring while driving this tick phase in isolation.
        private T RunService<T>(string property) => (T)typeof(RunState)
            .GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(State);

        internal void Step(float dt, Vector3? position = null, Vector3? body = null)
        {
            Now += dt;
            Snapshot.Dt = dt;
            Snapshot.PlayerPosition = position ?? 14f * Vector3.UnitZ;
            Snapshot.EnemyCount = 0;
            if (body.HasValue)
            {
                Snapshot.AddEnemy() = new EnemySense { Id = Caller.Id, Position = body.Value };
            }

            Enemies.Ingest(Snapshot, pets: Pets, now: Now);
            Intents.Clear();
            Enemies.Tick(new EnemyTickContext(dt, Now, Player, Intents, _events, _projectiles, Enemies, pets: Pets));
        }

        internal void Rise()
        {
            Step(0.25f);
            Step(2f);
        }

        internal void Hit(float damage) => Enemies.ApplyDamage(Caller.Id, damage, Now, Player);

        internal void StartSpaceMove()
        {
            Player.Charge.Request(Now);
            Player.Tick(0.01f, Now, Snapshot, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);
            Assert.That(Player.Charge.IsActive, Is.True);
        }
    }
}
