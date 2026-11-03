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

/// <summary>
/// JG-21d: an explosion with a chain radius lights every other bomber near it, and each goes off
/// <see cref="EnemySystem.ChainFuseSeconds"/> later through <see cref="EnemySystem.Execute"/>.
/// Rules 1–7 and the spec's refusal.
/// </summary>
/// <remarks>
/// The authored block (blast 2.5 m, chain 4 m) on a <c>Static</c> body, so nothing but the chain
/// kills anything; the bombers stand 50 m north of the player at the origin, so no blast reaches
/// it. The Bloater row is the exception and says why.
/// </remarks>
[TestFixture]
public sealed class ChainBomberTests
{
    private const string BomberId = "enemy.chain_bomber";
    private const string BloaterId = "enemy.chain_bloater";
    private const string FrogId = "enemy.frog";
    private const string PuffballId = "enemy.puffball";
    private const string OathboundId = "character.oathbound";

    private const int Capacity = 64;
    private const float Frame = 1f / 120f;

    private const float Blast = 2.5f;
    private const float Chain = 4f;
    private const float XpValue = 9f;

    /// <summary>The Bloater row's own fuse — well inside the chain's 0.3 s.</summary>
    private const float ShortFuse = 0.1f;

    private const int AllocationGroup = 17;

    private static readonly Vector3 North = new(0f, 0f, 50f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private WorldSnapshot _snapshot;
    private float _clock;

    // The allocation row's state, as fields so the measured closure captures no fresh locals.
    private EnemySystem _allocSystem;
    private PlayerCombat _allocPlayer;
    private ProjectileSystem _allocProjectiles;
    private RecordingIntents _allocIntents;
    private SilentEvents _silent;
    private EnemyAgent[] _leaders;
    private int _nextLeader;
    private float _allocClock;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), Scaling(), Capacity);
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, 8);
        _snapshot = new WorldSnapshot(Capacity);
        _clock = 0f;
    }

    // ---- Rule 1: lighting ----------------------------------------------------------------------

    [Test]
    public void Chain_LightsBombersInRadius()
    {
        EnemyAgent source = Spawn(BomberId, North);
        EnemyAgent near = Spawn(BomberId, North + new Vector3(3f, 0f, 0f));
        EnemyAgent far = Spawn(BomberId, North + new Vector3(-5f, 0f, 0f));

        _system.Execute(source.Id, _clock, _player);

        EnemyChainLit lit = _events.Single<EnemyChainLit>();

        Assert.That(lit.Id, Is.EqualTo(near.Id));
        Assert.That(lit.ByEnemyId, Is.EqualTo(source.Id));
        Assert.That(lit.From, Is.EqualTo(source.Position));
        Assert.That(lit.To, Is.EqualTo(near.Position));
        Assert.That(lit.Seconds, Is.EqualTo(EnemySystem.ChainFuseSeconds));
        Assert.That(near.ChainFuseAt, Is.EqualTo(_clock + EnemySystem.ChainFuseSeconds));
        Assert.That(float.IsPositiveInfinity(far.ChainFuseAt), "5 m is outside the 4 m chain.");
    }

    [Test]
    public void Chain_IgnoresNonBombers()
    {
        EnemyAgent source = Spawn(BomberId, North);
        EnemyAgent frog = Spawn(FrogId, North + new Vector3(2f, 0f, 0f));

        float hp = frog.Health.Current;

        _system.Execute(source.Id, _clock, _player);
        RunUntil(1f);

        Assert.That(_events.Count<EnemyChainLit>(), Is.Zero);
        Assert.That(frog.IsAlive);
        Assert.That(frog.Health.Current, Is.EqualTo(hp), "Lighting never damages by itself (rule 5).");
    }

    // ---- Rule 2: burning -----------------------------------------------------------------------

    [Test]
    public void Chain_BurnsAfterFuseAndPays()
    {
        EnemyAgent source = Spawn(BomberId, North);
        EnemyAgent lit = Spawn(BomberId, North + new Vector3(3f, 0f, 0f));

        _system.Execute(source.Id, _clock, _player);
        _system.DrainXp();
        _system.DrainKills();

        RunUntil(0.29f);

        Assert.That(lit.IsAlive, "Still burning at 0.29 s.");

        RunUntil(0.31f);

        Assert.That(lit.IsAlive, Is.False, "Gone off by 0.31 s.");
        Assert.That(_system.DrainXp(), Is.EqualTo(XpValue), "A chain death pays like any death.");
        Assert.That(_system.DrainKills(), Is.EqualTo(1));

        IReadOnlyList<EnemyExploded> blasts = _events.Of<EnemyExploded>();

        Assert.That(blasts.Count, Is.EqualTo(2));
        Assert.That(blasts[1].Id, Is.EqualTo(lit.Id));
    }

    [Test]
    public void Chain_Propagates()
    {
        EnemyAgent a = Spawn(BomberId, North);
        EnemyAgent b = Spawn(BomberId, North + new Vector3(3f, 0f, 0f));
        EnemyAgent c = Spawn(BomberId, North + new Vector3(6f, 0f, 0f));

        _system.Execute(a.Id, _clock, _player);

        Assert.That(float.IsPositiveInfinity(c.ChainFuseAt), "6 m from A: only B is lit at first.");

        float bDied = float.NaN;
        float cDied = float.NaN;

        while (_clock < 1f && c.IsAlive)
        {
            Tick();

            if (!b.IsAlive && float.IsNaN(bDied))
            {
                bDied = _clock;
            }

            if (!c.IsAlive)
            {
                cDied = _clock;
            }
        }

        Assert.That(bDied, Is.EqualTo(EnemySystem.ChainFuseSeconds).Within(2f * Frame));
        Assert.That(cDied - bDied, Is.EqualTo(EnemySystem.ChainFuseSeconds).Within(2f * Frame));
    }

    // ---- Rule 3: once each ---------------------------------------------------------------------

    [Test]
    public void Chain_LightsEachOnce()
    {
        // A 3 m square: sides inside the chain, the 4.24 m diagonal outside it.
        EnemyAgent[] ring =
        {
            Spawn(BomberId, North),
            Spawn(BomberId, North + new Vector3(3f, 0f, 0f)),
            Spawn(BomberId, North + new Vector3(3f, 0f, 3f)),
            Spawn(BomberId, North + new Vector3(0f, 0f, 3f)),
        };

        _system.Execute(ring[0].Id, _clock, _player);
        RunUntil(2f);

        var litIds = new HashSet<int>();

        foreach (EnemyChainLit lit in _events.Of<EnemyChainLit>())
        {
            Assert.That(litIds.Add(lit.Id), $"Enemy {lit.Id} was lit twice.");
        }

        Assert.That(litIds.Count, Is.EqualTo(3));
        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(4));
        Assert.That(_events.Count<EnemyExploded>(), Is.EqualTo(4));

        foreach (EnemyAgent agent in ring)
        {
            Assert.That(agent.IsAlive, Is.False);
        }
    }

    // ---- Rule 4: its own fuse first ------------------------------------------------------------

    [Test]
    public void Chain_OwnFuseWins()
    {
        // The Bloater needs a player to fuse on, so this row stands next to it — through Ingest,
        // which is also what its blast is measured against.
        EnemyAgent bloater = Spawn(BloaterId, new Vector3(0f, 0f, 1.5f));
        EnemyAgent source = Spawn(BomberId, new Vector3(0f, 0f, 4.5f));
        var behaviour = (BloaterBehaviour)bloater.Behaviour;

        for (int i = 0; i < 120 && behaviour.State != BloaterState.Fuse; i++)
        {
            SenseAndTick();
        }

        Assert.That(behaviour.State, Is.EqualTo(BloaterState.Fuse), "Sanity: the Bloater has lit its own fuse.");

        float litAt = _clock;
        _system.Execute(source.Id, _clock, _player);

        Assert.That(_events.Count<EnemyChainLit>(), Is.EqualTo(1), "The fusing Bloater is lit.");

        while (_clock < litAt + EnemySystem.ChainFuseSeconds - Frame && bloater.IsAlive)
        {
            SenseAndTick();
        }

        Assert.That(bloater.IsAlive, Is.False, "Its own 0.1 s fuse ends first.");

        while (_clock < litAt + 1f)
        {
            SenseAndTick();
        }

        int deaths = 0;

        foreach (EnemyDied died in _events.Of<EnemyDied>())
        {
            deaths += died.Id == bloater.Id ? 1 : 0;
        }

        Assert.That(deaths, Is.EqualTo(1), "Not killed twice.");
    }

    // ---- Rule 6: no chain ----------------------------------------------------------------------

    [Test]
    public void ZeroChain_IsTheOldExplosion()
    {
        EnemyAgent puffball = Spawn(PuffballId, North);
        EnemyAgent neighbour = Spawn(BomberId, North + new Vector3(1f, 0f, 0f));

        _system.Execute(puffball.Id, _clock, _player);
        RunUntil(1f);

        Assert.That(_events.Count<EnemyExploded>(), Is.EqualTo(1));
        Assert.That(_events.Count<EnemyChainLit>(), Is.Zero);
        Assert.That(neighbour.IsAlive);
    }

    // ---- Rule 7: no allocation -----------------------------------------------------------------

    [Test]
    public void Tick_DoesNotAllocate()
    {
        _silent = new SilentEvents();
        _allocIntents = new RecordingIntents();
        _allocSystem = new EnemySystem(Catalog(), _silent, new FixedRandom(), Scaling(), Capacity);
        _allocPlayer = new PlayerCombat(Oathbound(), _silent, _allocIntents, Capacity);
        _allocProjectiles = new ProjectileSystem(_silent, 8);
        _leaders = new EnemyAgent[2];
        _nextLeader = 0;
        _allocClock = 0f;

        // Two tight groups far apart: each leader's blast lights the other sixteen in its group.
        for (int g = 0; g < _leaders.Length; g++)
        {
            Vector3 origin = North + new Vector3(g * 100f, 0f, 0f);

            for (int i = 0; i < AllocationGroup; i++)
            {
                EnemyAgent agent = _allocSystem.Spawn(
                    new ContentId(BomberId), origin + new Vector3((i % 4) * 0.5f, 0f, (i / 4) * 0.5f));

                if (i == 0)
                {
                    _leaders[g] = agent;
                }
            }
        }

        // The first call warms up on group 0; the measured call lights and burns group 1.
        AllocationAssert.None(BurnNextGroup, iterations: 1);

        Assert.That(_allocSystem.LivingCount(), Is.Zero, "Sanity: both groups went off.");
    }

    // ---- The spec's refusal ---------------------------------------------------------------------

    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Spec_RefusesBadChainRadius(float chainRadius) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExplosionSpec(Blast, chainRadius));

    [Test]
    public void Spec_DefaultsToNoChain() => Assert.That(new ExplosionSpec(Blast).ChainRadius, Is.Zero);

    // ---- Helpers -------------------------------------------------------------------------------

    /// <summary>Lights one group, then ticks until its sixteen have all gone off.</summary>
    private void BurnNextGroup()
    {
        EnemyAgent leader = _leaders[_nextLeader++];

        _allocSystem.Execute(leader.Id, _allocClock, _allocPlayer);

        float until = _allocClock + EnemySystem.ChainFuseSeconds + (4f * Frame);

        while (_allocClock < until)
        {
            _allocIntents.Clear();
            _allocSystem.Tick(new EnemyTickContext(
                Frame, _allocClock, _allocPlayer, _allocIntents, _silent, _allocProjectiles, _allocSystem));
            _allocClock += Frame;
        }
    }

    private EnemyAgent Spawn(string id, Vector3 position) => _system.Spawn(new ContentId(id), position);

    private void Tick()
    {
        _intents.Clear();
        _system.Tick(new EnemyTickContext(Frame, _clock, _player, _intents, _events, _projectiles, _system));
        _clock += Frame;
    }

    private void RunUntil(float seconds)
    {
        while (_clock <= seconds)
        {
            Tick();
        }
    }

    /// <summary>The player at the origin, every registered agent sensed where it stands.</summary>
    private void SenseAndTick()
    {
        _snapshot.Clear();
        _snapshot.Dt = Frame;
        _snapshot.PlayerPosition = Vector3.Zero;

        ReadOnlySpan<EnemyAgent> agents = _system.Registry.Alive;

        for (int i = 0; i < agents.Length; i++)
        {
            ref EnemySense sense = ref _snapshot.AddEnemy();

            sense.Id = agents[i].Id;
            sense.Position = agents[i].Position;
            sense.Velocity = Vector3.Zero;
            sense.PathDirectionToPlayer = Vector2.Zero;
            sense.HasLineOfSight = true;
        }

        _system.Ingest(_snapshot);

        Tick();
    }

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound() },
        new[]
        {
            Enemy(BomberId, EnemyBehaviourKind.Static, new ExplosionSpec(Blast, Chain)),
            Enemy(BloaterId, EnemyBehaviourKind.Bloater, new ExplosionSpec(Blast, Chain), ShortFuse),
            Enemy(FrogId, EnemyBehaviourKind.Static, null),
            Enemy(PuffballId, EnemyBehaviourKind.Static, new ExplosionSpec(Blast)),
        });

    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());

    private static EnemySpec Enemy(
        string id, EnemyBehaviourKind behaviour, ExplosionSpec explosion, float windup = 0.8f) => new EnemySpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        maxHp: 20f,
        moveSpeed: 2.2f,
        targetPriority: 3,
        threatCost: 5,
        xpValue: XpValue,
        isElite: false,
        contactDamage: 10f,
        reach: 2f,
        windupTime: windup,
        recoverTime: 0.5f,
        aggroRange: 30f,
        behaviour: behaviour,
        explosion: explosion);

    /// <summary>The fixture's Oathbound, no Aegis.</summary>
    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 16f, 4f, 0.05f),
        null,
        0.5f);
}
