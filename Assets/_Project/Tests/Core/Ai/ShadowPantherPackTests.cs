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

/// <summary>JG-21b: Shadow panthers pair up, the flanker swings wide, and both lunge together.</summary>
[TestFixture]
public sealed class ShadowPantherPackTests
{
    private const string ShadowId = "enemy.shadowpanther";
    private const string PantherId = "enemy.panther";
    private const string OathboundId = "character.oathbound";

    private const int Capacity = 64;
    private const float Frame = 1f / 120f;
    private const float Tolerance = 1e-4f;

    private const float MoveSpeed = 6f;
    private const float WindupTime = 0.9f;
    private const float Range = 9f;
    private const float PartnerRadius = 16f;
    private const float FlankAngle = 90f;
    private const float FlankTimeout = 3f;

    /// <summary>Where the lanes are told the player stands: well clear, so a long run never kills them.</summary>
    private static readonly Vector3 FarAway = new Vector3(500f, 0f, 500f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;
    private WorldSnapshot _snapshot;
    private Dictionary<int, Vector3> _positions;

    /// <summary>An agent whose body does not move however it is asked: a flanker stuck on a wall.</summary>
    private int _pinned;

    private float _clock;

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
        _pinned = -1;
        _clock = 0f;
    }

    // ---- Rule 1: pairing ----------------------------------------------------------------------------

    [Test]
    public void Pair_NearestSameSpecLowerIdLeads()
    {
        EnemyAgent first = Spawn(ShadowId, new Vector3(5f, 0f, 0f));
        EnemyAgent second = Spawn(ShadowId, new Vector3(9f, 0f, 0f));
        EnemyAgent third = Spawn(ShadowId, new Vector3(20f, 0f, 0f));

        Step();

        Assert.That(first.Blackboard.PartnerId, Is.EqualTo(second.Id));
        Assert.That(second.Blackboard.PartnerId, Is.EqualTo(first.Id), "Both record each other.");
        Assert.That(third.Blackboard.PartnerId, Is.EqualTo(-1), "The odd one out hunts alone (rule 7).");
        Assert.That(Lunger(first).IsFlanker, Is.False, "The lower id leads.");
        Assert.That(Lunger(second).IsFlanker, Is.True);
    }

    [Test]
    public void Pair_IgnoresOtherSpecs()
    {
        EnemyAgent shadow = Spawn(ShadowId, new Vector3(0f, 0f, 15f));
        EnemyAgent panther = Spawn(PantherId, new Vector3(3f, 0f, 15f));

        StepFor(1f);

        Assert.That(shadow.Blackboard.PartnerId, Is.EqualTo(-1));
        Assert.That(panther.Blackboard.PartnerId, Is.EqualTo(-1));
    }

    // ---- Rule 2: the leader -------------------------------------------------------------------------

    [Test]
    public void Leader_IsAPlainLunger()
    {
        EnemyAgent leader = Spawn(ShadowId, new Vector3(0f, 0f, 15f));
        Spawn(ShadowId, new Vector3(6f, 0f, 15f));

        List<EnemyMoveIntent> paired = Record(leader.Id, seconds: 3f);

        Assert.That(Lunger(leader).IsFlanker, Is.False, "Sanity: it led.");
        Assert.That(leader.Blackboard.PartnerId, Is.Not.EqualTo(-1), "Sanity: it was paired.");

        SetUp();

        EnemyAgent lone = Spawn(PantherId, new Vector3(0f, 0f, 15f));

        List<EnemyMoveIntent> alone = Record(lone.Id, seconds: 3f);

        Assert.That(paired.Count, Is.EqualTo(alone.Count));

        for (int i = 0; i < paired.Count; i++)
        {
            Assert.That(Vector3.Distance(paired[i].Velocity, alone[i].Velocity), Is.LessThan(Tolerance), $"Tick {i}.");
            Assert.That(Vector2.Distance(paired[i].FacingXZ, alone[i].FacingXZ), Is.LessThan(Tolerance), $"Tick {i}.");
        }
    }

    // ---- Rule 3: the flanker's goal -----------------------------------------------------------------

    [Test]
    public void Flanker_GoesToTheSide()
    {
        EnemyAgent leader = Spawn(ShadowId, new Vector3(0f, 0f, 12f));
        EnemyAgent flanker = Spawn(ShadowId, new Vector3(4f, 0f, 9f));

        // Held where it stands, so the leader neither moves nor winds up and the flanker settles.
        _system.Stun(leader.Id, 60f);

        LungerBehaviour behaviour = Lunger(flanker);

        Step();

        Assert.That(behaviour.State, Is.EqualTo(LungerState.Flank), "Sanity: it swings rather than stalks.");

        StepUntil(() => behaviour.State != LungerState.Flank, seconds: FlankTimeout - 0.5f);

        Vector3 position = _positions[flanker.Id];

        Assert.That(behaviour.State, Is.EqualTo(LungerState.Windup), "At its goal it strikes (rule 5).");
        Assert.That(DegreesBetween(position, leader.Position), Is.InRange(80f, 100f));
        Assert.That(Flat(position).Length(), Is.EqualTo(Range - 1f).Within(1f));
        Assert.That(position.X, Is.GreaterThan(0f), "The side it was already nearer.");
    }

    // ---- Rule 4: together ---------------------------------------------------------------------------

    [Test]
    public void Pair_WindsUpTogether()
    {
        EnemyAgent leader = Spawn(ShadowId, new Vector3(0f, 0f, 12f));
        EnemyAgent flanker = Spawn(ShadowId, new Vector3(6f, 0f, 6f));

        LungerBehaviour lead = Lunger(leader);
        LungerBehaviour flank = Lunger(flanker);

        StepUntil(() => lead.State == LungerState.Windup, seconds: 2f);

        Assert.That(lead.State, Is.EqualTo(LungerState.Windup), "Sanity: the leader wound up.");
        Assert.That(flank.State, Is.EqualTo(LungerState.Windup), "The same tick.");

        IReadOnlyList<LaneArmed> lanes = _events.Of<LaneArmed>();

        Assert.That(lanes.Count, Is.EqualTo(2), "Each on its own lane.");
        Assert.That(Vector2.Dot(lanes[0].Direction, lanes[1].Direction), Is.LessThan(0.6f), "From two angles.");
    }

    // ---- Rule 5: alone after all --------------------------------------------------------------------

    [Test]
    public void Flanker_TimesOut()
    {
        EnemyAgent leader = Spawn(ShadowId, new Vector3(0f, 0f, 20f));
        EnemyAgent flanker = Spawn(ShadowId, new Vector3(0f, 0f, 7f));

        _system.Stun(leader.Id, 60f);
        _pinned = flanker.Id;

        LungerBehaviour behaviour = Lunger(flanker);

        StepFor(FlankTimeout - 0.1f);

        Assert.That(behaviour.State, Is.EqualTo(LungerState.Flank), "Short of its goal, inside the timeout.");

        StepFor(0.1f + 2f * Frame);

        Assert.That(behaviour.State, Is.EqualTo(LungerState.Windup), "By the timeout it strikes alone.");
    }

    // ---- Rule 6: partner dies -----------------------------------------------------------------------

    [Test]
    public void PartnerDies_SurvivorHuntsAlone()
    {
        EnemyAgent leader = Spawn(ShadowId, new Vector3(0f, 0f, 20f));
        EnemyAgent flanker = Spawn(ShadowId, new Vector3(0f, 0f, 15f));

        _system.Stun(leader.Id, 60f);

        LungerBehaviour behaviour = Lunger(flanker);

        StepFor(0.2f);

        Assert.That(behaviour.State, Is.EqualTo(LungerState.Flank), "Sanity: flanking.");

        _system.Execute(leader.Id, _clock, _player);

        _pinned = flanker.Id;

        Step();

        Assert.That(behaviour.State, Is.EqualTo(LungerState.Stalk));
        Assert.That(flanker.Blackboard.PartnerId, Is.EqualTo(-1));

        EnemyAgent newcomer = Spawn(ShadowId, _positions[flanker.Id] + new Vector3(2f, 0f, 0f));

        StepFor(0.6f);

        Assert.That(flanker.Blackboard.PartnerId, Is.EqualTo(newcomer.Id), "It may pair again.");
    }

    // ---- Rule 7: no PackSpec ------------------------------------------------------------------------

    [Test]
    public void NoPackSpec_IsTheOldLunger()
    {
        EnemyAgent first = Spawn(PantherId, new Vector3(0f, 0f, 15f));
        EnemyAgent second = Spawn(PantherId, new Vector3(3f, 0f, 15f));

        for (int i = 0; i < 240; i++)
        {
            Step();

            Assert.That(Lunger(second).State, Is.Not.EqualTo(LungerState.Flank));
        }

        Assert.That(first.Blackboard.PartnerId, Is.EqualTo(-1));
        Assert.That(second.Blackboard.PartnerId, Is.EqualTo(-1));
    }

    // ---- Rule 8: no allocation ----------------------------------------------------------------------

    [Test]
    public void Tick_DoesNotAllocate()
    {
        // SilentEvents, for LungerBehaviourTests' reason: the recording fakes box every payload.
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);
        var projectiles = new ProjectileSystem(silent, Capacity);
        var lanes = new LaneSystem(silent);
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), Scaling(), Capacity);
        var snapshot = new WorldSnapshot(Capacity);

        snapshot.Dt = Frame;
        snapshot.PlayerPosition = Vector3.Zero;

        // Eight pairs on a ring, half in range: leaders cycle, flankers swing, time out and strike.
        for (int i = 0; i < 16; i++)
        {
            float angle = i * (MathF.PI / 8f);
            float radius = i % 4 < 2 ? 7f : 14f;

            EnemyAgent agent = system.Spawn(
                new ContentId(ShadowId),
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
            lanes.Tick(clock, FarAway, player);
            system.Tick(new EnemyTickContext(Frame, clock, player, intents, silent, projectiles, system, lanes));
            clock += Frame;
        }

        AllocationAssert.None(() =>
        {
            intents.Clear();
            lanes.Tick(clock, FarAway, player);
            system.Tick(new EnemyTickContext(Frame, clock, player, intents, silent, projectiles, system, lanes));
            clock += Frame;
        });
    }

    // ---- Fixture helpers ----------------------------------------------------------------------------

    private EnemyAgent Spawn(string id, Vector3 position)
    {
        EnemyAgent agent = _system.Spawn(new ContentId(id), position);

        _positions[agent.Id] = position;

        return agent;
    }

    private static LungerBehaviour Lunger(EnemyAgent agent) => (LungerBehaviour)agent.Behaviour;

    /// <summary>
    /// One frame as <c>RunSession</c> orders it: the bodies report where they are, the lanes tick,
    /// the enemies decide, and the bodies carry out what they were asked.
    /// </summary>
    private void Step()
    {
        _snapshot.Clear();
        _snapshot.Dt = Frame;
        _snapshot.PlayerPosition = Vector3.Zero;

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
        _lanes.Tick(_clock, FarAway, _player);
        _system.Tick(new EnemyTickContext(Frame, _clock, _player, _intents, _events, _projectiles, _system, _lanes));

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

    private List<EnemyMoveIntent> Record(int id, float seconds)
    {
        var moves = new List<EnemyMoveIntent>();
        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            Step();

            foreach (EnemyMoveIntent move in _intents.EnemyMoves)
            {
                if (move.Id == id)
                {
                    moves.Add(move);
                }
            }
        }

        return moves;
    }

    private static Vector2 Flat(Vector3 position) => new Vector2(position.X, position.Z);

    /// <summary>Degrees between two bearings from the player at the origin.</summary>
    private static float DegreesBetween(Vector3 a, Vector3 b)
    {
        Vector2 x = Vector2.Normalize(Flat(a));
        Vector2 y = Vector2.Normalize(Flat(b));

        return MathF.Acos(Math.Clamp(Vector2.Dot(x, y), -1f, 1f)) * (180f / MathF.PI);
    }

    private static ContentCatalog Catalog() =>
        new ContentCatalog(new[] { Oathbound() }, new[] { Panther(ShadowId, pack: true), Panther(PantherId, pack: false) });

    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());

    /// <summary>JG-07a's Lunger, quicker on its feet; with or without JG-21b's pack block.</summary>
    private static EnemySpec Panther(string id, bool pack) => new EnemySpec(
        new ContentId(id),
        new LocKey(id + ".name"),
        maxHp: 44f,
        moveSpeed: MoveSpeed,
        targetPriority: 4,
        threatCost: 9,
        xpValue: 27f,
        isElite: false,
        contactDamage: 14f,
        reach: 1.2f,
        windupTime: WindupTime,
        recoverTime: 1f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Lunger,
        lunge: new LungeSpec(Range, 12f, 24f, 0.6f, 2f),
        pack: pack ? new PackSpec(PartnerRadius, FlankAngle, FlankTimeout) : null);

    /// <summary>The Oathbound of CC §7 — the player the panthers hunt.</summary>
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
