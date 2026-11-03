using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>JG-24a: the Thornweaver holds its distance and plants walls beyond the player.</summary>
[TestFixture]
public sealed class ThornweaverBehaviourTests
{
    private const string WeaverId = "enemy.thornweaver";
    private const string LongStandId = "enemy.thornweaver_long";
    private const string OathboundId = "character.oathbound";

    private const int Capacity = 64;
    private const float Frame = 1f / 120f;

    private const float Hold = 10f;
    private const float CastSeconds = 0.8f;
    private const float WallLength = 5f;
    private const float WallHalfWidth = 0.4f;
    private const float Arm = 0.9f;
    private const float Beyond = 2.5f;
    private const float ContactDamage = 9f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private ThornSystem _thorns;
    private WorldSnapshot _snapshot;
    private Dictionary<int, Vector3> _positions;
    private HashSet<int> _pinned;
    private Vector3 _playerPosition;
    private float _clock;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), Scaling(), Capacity);
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, Capacity);
        _thorns = new ThornSystem(_events);
        _snapshot = new WorldSnapshot(Capacity);
        _positions = new Dictionary<int, Vector3>();
        _pinned = new HashSet<int>();
        _playerPosition = Vector3.Zero;
        _clock = 0f;
    }

    // ---- Rule 1 -------------------------------------------------------------------------------------

    [TestCase(20f)]
    [TestCase(5f)]
    public void Holds_AtTen(float start)
    {
        EnemyAgent agent = Spawn(new Vector3(0f, 0f, -start));
        ThornweaverBehaviour weaver = Weaver(agent);

        StepFor(8f);

        Assert.That(Flat(_positions[agent.Id]).Length(), Is.EqualTo(Hold).Within(0.5f), "Settles at 10 m.");
        Assert.That(weaver.State, Is.Not.EqualTo(ThornweaverState.Approach));

        EnemyMoveIntent last = LastMove(agent.Id);
        Assert.That(last.FacingXZ.Y, Is.EqualTo(1f).Within(1e-3f), "Faces the player, north.");
    }

    // ---- Rule 2 -------------------------------------------------------------------------------------

    [Test]
    public void Cast_OnIntervalInRange()
    {
        EnemyAgent near = Pin(Spawn(new Vector3(0f, 0f, -12f)));

        StepFor(ThornweaverBehaviour.FirstCastSeconds - 0.02f);
        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero, "Not before 2 s.");

        StepFor(0.04f);
        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(1), "At 2 s, 12 m away.");
        Assert.That(_events.Of<EnemyTelegraph>()[0].Id, Is.EqualTo(near.Id));
        Assert.That(Weaver(near).State, Is.EqualTo(ThornweaverState.Cast));

        SetUp();
        Pin(Spawn(new Vector3(0f, 0f, -16f)));

        StepFor(6f);
        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero, "16 m is outside 14.");
    }

    // ---- Rule 3 -------------------------------------------------------------------------------------

    [Test]
    public void Wall_BeyondPlayerAcrossTheLine()
    {
        EnemyAgent agent = Pin(Spawn(new Vector3(0f, 0f, -10f)));

        StepUntil(() => _events.Count<EnemyTelegraph>() == 1, 3f);

        // The player steps north during the cast; the wall follows where they end it.
        _playerPosition = new Vector3(0f, 0f, 1f);
        StepUntil(() => _events.Count<ThornArmed>() == 1, 1f);

        ThornArmed armed = _events.Of<ThornArmed>()[0];
        Vector2 centre = new Vector2(armed.Origin.X, armed.Origin.Z) + armed.Direction * (armed.Length * 0.5f);

        Assert.That(centre.X, Is.EqualTo(0f).Within(1e-3f));
        Assert.That(centre.Y, Is.EqualTo(1f + Beyond).Within(1e-3f), "2.5 m north of the player.");
        Assert.That(MathF.Abs(armed.Direction.X), Is.EqualTo(1f).Within(1e-3f), "Runs east–west.");
        Assert.That(armed.Length, Is.EqualTo(WallLength));
        Assert.That(armed.HalfWidth, Is.EqualTo(WallHalfWidth));
        Assert.That(armed.ArmSeconds, Is.EqualTo(Arm));
        Assert.That(armed.OwnerId, Is.EqualTo(agent.Id));
        Assert.That(Weaver(agent).WallCount, Is.EqualTo(1));
    }

    // ---- Rule 4 -------------------------------------------------------------------------------------

    [Test]
    public void Wall_RefusedSpendsTheCast()
    {
        for (int i = 0; i < ThornSystem.Capacity; i++)
        {
            _thorns.Open(new Vector3(40f, 0f, 0f), Vector2.UnitY, 1f, 0.2f, 60f, 1f, 0f, 0f);
        }

        EnemyAgent agent = Pin(Spawn(new Vector3(0f, 0f, -12f)));

        StepFor(ThornweaverBehaviour.FirstCastSeconds + CastSeconds + 0.1f);

        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(1), "Sanity: it cast.");
        Assert.That(_events.Count<ThornArmed>(), Is.EqualTo(ThornSystem.Capacity), "Refused: nothing armed.");
        Assert.That(Weaver(agent).WallCount, Is.Zero);
        Assert.That(Weaver(agent).State, Is.Not.EqualTo(ThornweaverState.Cast));

        StepUntil(() => _events.Count<EnemyTelegraph>() == 2, 6f);

        Assert.That(_clock, Is.EqualTo(ThornweaverBehaviour.FirstCastSeconds + 5f).Within(0.05f), "5 s later.");
    }

    // ---- Rules 2 and 5 ------------------------------------------------------------------------------

    [Test]
    public void Walls_CappedAtTwo()
    {
        EnemyAgent agent = Pin(Spawn(new Vector3(0f, 0f, -12f), LongStandId));
        ThornweaverBehaviour weaver = Weaver(agent);

        StepFor(14f);

        Assert.That(weaver.WallCount, Is.EqualTo(2));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(2), "The 12 s cast is held back.");

        _thorns.Wither(_events.Of<ThornArmed>()[0].Id);
        Assert.That(weaver.WallCount, Is.EqualTo(1), "A withered wall stops counting.");

        Step();
        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(3), "Room again: it casts.");

        _thorns.Clear();
        Assert.That(weaver.WallCount, Is.Zero, "A cleared wall stops counting.");
    }

    // ---- Rule 6 -------------------------------------------------------------------------------------

    [Test]
    public void Death_WithersOnlyItsWalls()
    {
        EnemyAgent south = Pin(Spawn(new Vector3(0f, 0f, -12f)));
        EnemyAgent north = Pin(Spawn(new Vector3(0f, 0f, 12f)));

        StepUntil(() => _events.Count<ThornArmed>() == 2, 4f);

        int southWall = WallOf(south.Id);
        int northWall = WallOf(north.Id);
        int before = _events.Count<ThornWithered>();

        _system.ApplyDamage(south.Id, 1000f, _clock, _player);

        Assert.That(_events.Count<ThornWithered>(), Is.EqualTo(before + 1));
        Assert.That(_events.Of<ThornWithered>()[before].Id, Is.EqualTo(southWall));
        Assert.That(_thorns.IsLive(southWall), Is.False);
        Assert.That(_thorns.IsLive(northWall), Is.True, "The other weaver's wall stands.");
        Assert.That(Weaver(north).WallCount, Is.EqualTo(1));
    }

    // ---- Rule 8 -------------------------------------------------------------------------------------

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);
        var projectiles = new ProjectileSystem(silent, Capacity);
        var thorns = new ThornSystem(silent);
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), Scaling(), Capacity);
        var snapshot = new WorldSnapshot(Capacity);

        snapshot.Dt = Frame;
        snapshot.PlayerPosition = Vector3.Zero;

        // Four round the player at 12 m: they hold, cast and plant.
        for (int i = 0; i < 4; i++)
        {
            float angle = i * (MathF.PI / 2f);
            EnemyAgent agent = system.Spawn(
                new ContentId(WeaverId), new Vector3(MathF.Cos(angle) * 12f, 0f, MathF.Sin(angle) * 12f));

            ref EnemySense sense = ref snapshot.AddEnemy();

            sense.Id = agent.Id;
            sense.Position = agent.Position;
            sense.HasLineOfSight = true;
        }

        system.Ingest(snapshot);

        float clock = 0f;

        for (int i = 0; i < 1200; i++)
        {
            intents.Clear();
            thorns.Tick(clock, Vector3.Zero, player);
            system.Tick(new EnemyTickContext(Frame, clock, player, intents, silent, projectiles, system, thorns: thorns));
            clock += Frame;
        }

        // Six seconds inside one measurement, so a cast and a plant are in it.
        AllocationAssert.None(() =>
        {
            for (int i = 0; i < 720; i++)
            {
                intents.Clear();
                thorns.Tick(clock, Vector3.Zero, player);
                system.Tick(new EnemyTickContext(Frame, clock, player, intents, silent, projectiles, system, thorns: thorns));
                clock += Frame;
            }
        });
    }

    // ---- Fixture helpers ----------------------------------------------------------------------------

    private EnemyAgent Spawn(Vector3 position, string id = WeaverId)
    {
        EnemyAgent agent = _system.Spawn(new ContentId(id), position);
        _positions[agent.Id] = position;
        return agent;
    }

    private EnemyAgent Pin(EnemyAgent agent)
    {
        _pinned.Add(agent.Id);
        return agent;
    }

    private static ThornweaverBehaviour Weaver(EnemyAgent agent) => (ThornweaverBehaviour)agent.Behaviour;

    private static Vector2 Flat(Vector3 v) => new Vector2(v.X, v.Z);

    private int WallOf(int ownerId)
    {
        foreach (ThornArmed armed in _events.Of<ThornArmed>())
        {
            if (armed.OwnerId == ownerId)
            {
                return armed.Id;
            }
        }

        throw new AssertionException($"No wall owned by {ownerId}.");
    }

    private EnemyMoveIntent LastMove(int id)
    {
        EnemyMoveIntent found = default;
        foreach (EnemyMoveIntent move in _intents.EnemyMoves)
        {
            if (move.Id == id)
            {
                found = move;
            }
        }

        return found;
    }

    /// <summary>One frame as <c>RunSession</c> orders it: sense, thorns, decide, move.</summary>
    private void Step()
    {
        _snapshot.Clear();
        _snapshot.Dt = Frame;
        _snapshot.PlayerPosition = _playerPosition;

        foreach (EnemyAgent agent in _system.Registry.Alive)
        {
            if (!agent.IsAlive)
            {
                continue;
            }

            ref EnemySense sense = ref _snapshot.AddEnemy();

            sense.Id = agent.Id;
            sense.Position = _positions[agent.Id];
            sense.Velocity = Vector3.Zero;
            sense.PathDirectionToPlayer = Vector2.Zero;
            sense.HasLineOfSight = true;
        }

        _system.Ingest(_snapshot);

        _intents.Clear();
        _thorns.Tick(_clock, new Vector3(50f, 0f, 50f), _player);
        _system.Tick(new EnemyTickContext(
            Frame, _clock, _player, _intents, _events, _projectiles, _system, thorns: _thorns));

        foreach (EnemyMoveIntent move in _intents.EnemyMoves)
        {
            if (!_pinned.Contains(move.Id) && _positions.ContainsKey(move.Id))
            {
                _positions[move.Id] += move.Velocity * Frame;
            }
        }

        _clock += Frame;
    }

    private void StepFor(float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            Step();
        }
    }

    private void StepUntil(Func<bool> done, float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames && !done(); i++)
        {
            Step();
        }
    }

    private static ContentCatalog Catalog() =>
        new ContentCatalog(new[] { Oathbound() }, new[] { Weaver(WeaverId, 5f), Weaver(LongStandId, 30f) });

    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());

    /// <summary>JG-24a's authored numbers; <paramref name="stand"/> varies for the cap test.</summary>
    private static EnemySpec Weaver(string id, float stand) => new EnemySpec(
        new ContentId(id),
        new LocKey(id + ".name"),
        maxHp: 45f,
        moveSpeed: 3f,
        targetPriority: 6,
        threatCost: 12,
        xpValue: 30f,
        isElite: false,
        contactDamage: ContactDamage,
        reach: 1.2f,
        windupTime: 0.5f,
        recoverTime: 0.5f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Thornweaver,
        thornweaver: new ThornweaverSpec(Hold, 14f, 5f, CastSeconds, WallLength, WallHalfWidth, Arm, stand, Beyond, 2));

    /// <summary>The Oathbound of CC §7.</summary>
    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
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
