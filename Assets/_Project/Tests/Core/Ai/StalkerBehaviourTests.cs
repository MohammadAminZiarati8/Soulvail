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

/// <summary>JG-22a: the Chameleon stalker goes behind, hides, and strikes down a warned lane.</summary>
[TestFixture]
public sealed class StalkerBehaviourTests
{
    private const string StalkerId = "enemy.stalker";
    private const string OathboundId = "character.oathbound";

    private const int Capacity = 64;
    private const float Frame = 1f / 120f;

    private const float Behind = 3f;
    private const float CloakDelay = 0.4f;
    private const float StrikeLength = 5f;
    private const float WindupTime = 0.45f;
    private const float RevealSeconds = 1.5f;
    private const float ContactDamage = 12f;

    /// <summary>The player looks north.</summary>
    private static readonly Vector2 North = new Vector2(0f, 1f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;
    private WorldSnapshot _snapshot;
    private Dictionary<int, Vector3> _positions;
    private Vector3 _playerPosition;
    private Vector2 _playerFacing;
    private float _clock;

    /// <summary>An agent whose body does not move however it is asked.</summary>
    private int _pinned;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), Scaling(), Capacity);
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, Capacity);
        _lanes = new LaneSystem(_events);
        _snapshot = new WorldSnapshot(Capacity);
        _positions = new Dictionary<int, Vector3>();
        _playerPosition = Vector3.Zero;
        _playerFacing = North;
        _clock = 0f;
        _pinned = -1;
    }

    // ---- Rule 1 -------------------------------------------------------------------------------------

    [Test]
    public void Prowl_GoesBehindThePlayer()
    {
        EnemyAgent agent = Spawn(new Vector3(6f, 0f, 4f));
        StalkerBehaviour stalker = Stalker(agent);

        Assert.That(stalker.State, Is.EqualTo(StalkerState.Prowl));
        Assert.That(agent.IsHidden, Is.False, "Prowling is visible (rule 8).");

        StepUntil(() => stalker.State == StalkerState.Lurk, seconds: 5f);

        Vector3 position = _positions[agent.Id];

        Assert.That(stalker.State, Is.EqualTo(StalkerState.Lurk));
        Assert.That(position.X, Is.EqualTo(0f).Within(StalkerBehaviour.ArriveDistance));
        Assert.That(position.Z, Is.EqualTo(-Behind).Within(StalkerBehaviour.ArriveDistance), "3 m south.");
    }

    // ---- Rule 2 -------------------------------------------------------------------------------------

    [Test]
    public void Lurk_HidesAfterDelay()
    {
        // No facing, so no "behind": it hides and stays hidden rather than winding up.
        _playerFacing = Vector2.Zero;
        EnemyAgent agent = Spawn(new Vector3(0f, 0f, -Behind));
        StalkerBehaviour stalker = Stalker(agent);
        Lock(agent);

        Step();
        Assert.That(stalker.State, Is.EqualTo(StalkerState.Lurk), "Sanity: already on its spot.");

        StepFor(0.39f);
        Assert.That(agent.IsHidden, Is.False, "0.39 s still.");

        StepFor(0.02f);
        Assert.That(agent.IsHidden, Is.True, "0.41 s still.");
        Assert.That(_events.Of<EnemyHiddenChanged>().Count, Is.EqualTo(1));
        Assert.That(_events.Of<EnemyHiddenChanged>()[0].Hidden, Is.True);
    }

    [Test]
    public void Lurk_DriftReprowls()
    {
        _playerFacing = Vector2.Zero;
        EnemyAgent agent = Spawn(new Vector3(0f, 0f, -Behind));
        StalkerBehaviour stalker = Stalker(agent);
        Lock(agent);

        StepFor(CloakDelay + 0.05f);
        Assert.That(agent.IsHidden, Is.True, "Sanity: hidden.");

        _playerPosition = new Vector3(0f, 0f, 4f);
        Step();

        Assert.That(stalker.State, Is.EqualTo(StalkerState.Prowl));
        Assert.That(agent.IsHidden, Is.False);
        Assert.That(_events.Of<EnemyHiddenChanged>()[^1].Hidden, Is.False);
    }

    // ---- Rules 3 and 7 ------------------------------------------------------------------------------

    [Test]
    public void Strike_OnlyFromBehind()
    {
        EnemyAgent agent = Spawn(new Vector3(0f, 0f, -Behind));
        StalkerBehaviour stalker = Stalker(agent);

        StepUntil(() => stalker.State == StalkerState.Windup, seconds: 1f);
        Assert.That(stalker.State, Is.EqualTo(StalkerState.Windup), "3 m behind: it strikes.");

        SetUp();

        // In front of the player, pinned where it stands: it prowls and never winds up.
        EnemyAgent front = Spawn(new Vector3(0f, 0f, Behind));
        StalkerBehaviour ahead = Stalker(front);
        _pinned = front.Id;

        for (int i = 0; i < (int)(2f / Frame); i++)
        {
            Step();
            Assert.That(ahead.State, Is.EqualTo(StalkerState.Prowl), $"Tick {i}.");
        }

        Assert.That(_events.Of<EnemyTelegraph>(), Is.Empty);
    }

    [Test]
    public void Windup_ArmsLaneAndShows()
    {
        EnemyAgent agent = Spawn(new Vector3(0f, 0f, -Behind));
        StalkerBehaviour stalker = Stalker(agent);

        StepUntil(() => stalker.State == StalkerState.Windup, seconds: 1f);

        Assert.That(stalker.State, Is.EqualTo(StalkerState.Windup));
        Assert.That(agent.IsHidden, Is.False, "Visible while it winds up (rule 8).");
        Assert.That(_events.Of<EnemyTelegraph>().Count, Is.EqualTo(1));

        IReadOnlyList<LaneArmed> lanes = _events.Of<LaneArmed>();
        Assert.That(lanes.Count, Is.EqualTo(1));
        Assert.That(lanes[0].Length, Is.EqualTo(StrikeLength));
        Assert.That(lanes[0].Direction.Y, Is.EqualTo(1f).Within(1e-3f), "Toward the player.");
    }

    // ---- Rule 4 -------------------------------------------------------------------------------------

    [Test]
    public void Strike_LaneDamages()
    {
        EnemyAgent agent = Spawn(new Vector3(0f, 0f, -Behind));
        StalkerBehaviour stalker = Stalker(agent);
        StepUntil(() => stalker.State == StalkerState.Recover, seconds: 3f);

        Assert.That(stalker.State, Is.EqualTo(StalkerState.Recover), "Sanity: it struck.");
        Assert.That(_positions[agent.Id].Z, Is.EqualTo(-Behind + StrikeLength).Within(0.05f), "Crossed the lane.");

        StepUntil(() => stalker.State == StalkerState.Prowl, seconds: 3f);
        Assert.That(stalker.State, Is.EqualTo(StalkerState.Prowl), "Recover, then prowl.");

        PlayerDamaged hit = _events.Single<PlayerDamaged>();
        Assert.That(hit.ToShield + hit.ToHp, Is.EqualTo(ContactDamage).Within(1e-3f), "Contact once.");
    }

    [Test]
    public void Strike_CooldownCountsFromTheStrikesEnd()
    {
        EnemyAgent agent = Spawn(new Vector3(0f, 0f, -Behind));
        StalkerBehaviour stalker = Stalker(agent);

        StepUntil(() => stalker.State == StalkerState.Recover, seconds: 3f);
        float strikeEnd = _clock;

        StepUntil(() => _events.Count<EnemyTelegraph>() == 2, seconds: 10f);

        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(2), "Sanity: it struck again.");
        Assert.That(_clock - strikeEnd, Is.GreaterThanOrEqualTo(3f));
    }

    // ---- Rule 5 -------------------------------------------------------------------------------------

    [Test]
    public void Hidden_IsNotTargeted()
    {
        EnemyAgent agent = Spawn(new Vector3(0f, 0f, -Behind));
        _playerFacing = Vector2.Zero; // No "behind", so it lurks hidden rather than striking.
        Lock(agent);

        StepFor(CloakDelay + 0.05f);
        Assert.That(agent.IsHidden, Is.True, "Sanity: hidden.");

        _player.Tick(Frame, _clock, _snapshot, _system.Registry.Alive, Vector3.UnitZ);

        Assert.That(_player.Targeter.CurrentTargetId, Is.EqualTo(-1), "Not even held as blocked.");
        Assert.That(_player.Targeter.IsCurrentBlocked, Is.False);
    }

    // ---- Rule 6 -------------------------------------------------------------------------------------

    [Test]
    public void Hidden_StillTakesDamage()
    {
        EnemyAgent agent = Spawn(new Vector3(0f, 0f, -Behind));
        _playerFacing = Vector2.Zero;
        Lock(agent);

        StepFor(CloakDelay + 0.05f);
        Assert.That(agent.IsHidden, Is.True, "Sanity: hidden.");

        // Through the one door every hit reaches an enemy by, a Volley arrow's included.
        DamageResult hit = _system.ApplyDamage(agent.Id, 5f, _clock, _player);

        Assert.That(hit.Applied, Is.EqualTo(5f));
        Assert.That(agent.IsHidden, Is.False, "Damage reveals.");
        Assert.That(_events.Of<EnemyHiddenChanged>()[^1].Hidden, Is.False);

        StepFor(RevealSeconds - 0.05f);
        Assert.That(agent.IsHidden, Is.False, "Revealed for 1.5 s.");

        StepFor(0.1f);
        Assert.That(agent.IsHidden, Is.True, "Then it hides again.");
    }

    [Test]
    public void Death_ClearsHiddenSilently()
    {
        EnemyAgent agent = Spawn(new Vector3(0f, 0f, -Behind));
        _playerFacing = Vector2.Zero;
        Lock(agent);

        StepFor(CloakDelay + 0.05f);
        int published = _events.Of<EnemyHiddenChanged>().Count;

        _system.ApplyDamage(agent.Id, 1000f, _clock, _player);

        Assert.That(agent.IsHidden, Is.False);
        Assert.That(_events.Of<EnemyHiddenChanged>().Count, Is.EqualTo(published), "Silently (rule 8).");
    }

    // ---- Rule 9 -------------------------------------------------------------------------------------

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);
        var projectiles = new ProjectileSystem(silent, Capacity);
        var lanes = new LaneSystem(silent);
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), Scaling(), Capacity);
        var snapshot = new WorldSnapshot(Capacity);

        snapshot.Dt = Frame;
        snapshot.PlayerPosition = Vector3.Zero;

        // Eight round the player, half already behind: they prowl, lurk, hide and strike.
        for (int i = 0; i < 8; i++)
        {
            float angle = i * (MathF.PI / 4f);
            float radius = i % 2 == 0 ? 3f : 8f;

            EnemyAgent agent = system.Spawn(
                new ContentId(StalkerId),
                new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius));

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
            lanes.Tick(clock, Vector3.Zero, player);
            system.Tick(new EnemyTickContext(
                Frame, clock, player, intents, silent, projectiles, system, lanes, playerFacing: North));
            clock += Frame;
        }

        AllocationAssert.None(() =>
        {
            intents.Clear();
            lanes.Tick(clock, Vector3.Zero, player);
            system.Tick(new EnemyTickContext(
                Frame, clock, player, intents, silent, projectiles, system, lanes, playerFacing: North));
            clock += Frame;
        });
    }

    // ---- Fixture helpers ----------------------------------------------------------------------------

    private EnemyAgent Spawn(Vector3 position)
    {
        EnemyAgent agent = _system.Spawn(new ContentId(StalkerId), position);
        _positions[agent.Id] = position;
        return agent;
    }

    private void Lock(EnemyAgent agent) => _pinned = agent.Id;

    private static StalkerBehaviour Stalker(EnemyAgent agent) => (StalkerBehaviour)agent.Behaviour;

    /// <summary>One frame as <c>RunSession</c> orders it: sense, lanes, decide, move.</summary>
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
        _lanes.Tick(_clock, _playerPosition, _player);
        _system.Tick(new EnemyTickContext(
            Frame, _clock, _player, _intents, _events, _projectiles, _system, _lanes, playerFacing: _playerFacing));

        foreach (EnemyMoveIntent move in _intents.EnemyMoves)
        {
            if (move.Id != _pinned && _positions.ContainsKey(move.Id))
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
        new ContentCatalog(new[] { Oathbound() }, new[] { Chameleon() });

    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());

    /// <summary>JG-22d's authored numbers.</summary>
    private static EnemySpec Chameleon() => new EnemySpec(
        new ContentId(StalkerId),
        new LocKey(StalkerId + ".name"),
        maxHp: 40f,
        moveSpeed: 4f,
        targetPriority: 5,
        threatCost: 10,
        xpValue: 30f,
        isElite: false,
        contactDamage: ContactDamage,
        reach: 1.2f,
        windupTime: WindupTime,
        recoverTime: 0.8f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Stalker,
        stalker: new StalkerSpec(Behind, CloakDelay, 4f, 110f, StrikeLength, 18f, 0.6f, RevealSeconds, 3f));

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
