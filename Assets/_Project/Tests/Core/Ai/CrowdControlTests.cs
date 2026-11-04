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

/// <summary>MK-06a: enemies can be stunned, rooted and slowed; behaviours obey without knowing.</summary>
[TestFixture]
public sealed class CrowdControlTests
{
    private const string HuskId = "enemy.husk";
    private const string EliteId = "enemy.husk_elite";
    private const string SpitterId = "enemy.spitter";
    private const string PantherId = "enemy.panther";
    private const string BodyId = "enemy.boss_body";
    private const string BossId = "boss.test";
    private const string OathboundId = "character.oathbound";

    private const int Capacity = 64;

    private const float Frame = 1f / 120f;

    private const float Tolerance = 1e-4f;

    private const float Reach = 1.2f;
    private const float HuskWindup = 0.4f;
    private const float Standoff = 8f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;

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
        _clock = 0f;
    }

    // ---- Rule 1 -------------------------------------------------------------------------------------

    [Test]
    public void Boss_ShrugsOffStunRootSlow_EliteTakesFull()
    {
        EnemyAgent boss = _system.SpawnBoss(new ContentId(BossId), Vector3.Zero);
        EnemyAgent elite = _system.Spawn(new ContentId(EliteId), new Vector3(3f, 0f, 0f));
        float speed = boss.MoveSpeed.Value;
        _events.Clear();

        Assert.That(_system.Stun(boss.Id, 2f), Is.True, "A living boss is still a hit.");
        Assert.That(_system.Root(boss.Id, 2f), Is.True);
        Assert.That(_system.Slow(boss.Id, 0.5f, 2f), Is.True);
        Assert.That(_system.Stun(elite.Id, 2f), Is.True);

        Assert.That(_system.Remaining(boss.Id, new ContentId(EnemyStatuses.StunnedKey)), Is.Zero);
        Assert.That(_system.Remaining(boss.Id, new ContentId(EnemyStatuses.RootedKey)), Is.Zero);
        Assert.That(_system.Remaining(boss.Id, new ContentId(EnemyStatuses.SlowedKey)), Is.Zero);
        Assert.That(boss.MoveSpeed.Value, Is.EqualTo(speed));
        Assert.That(_events.Of<EnemyStatusChanged>(), Has.Count.EqualTo(1), "Only the elite's stun is announced.");
        Assert.That(_system.Remaining(elite.Id, new ContentId(EnemyStatuses.StunnedKey)), Is.EqualTo(2f).Within(Tolerance), "Elites take full.");
    }

    // ---- Rule 2 -------------------------------------------------------------------------------------

    [Test]
    public void Stun_FreezesTheBehaviour()
    {
        EnemyAgent husk = WindingUpHusk();

        TickFor(0.1f);

        float timer = husk.Blackboard.StateTimer;
        int telegraphs = _events.Count<EnemyTelegraph>();

        _system.Stun(husk.Id, 1f);

        for (int i = 0; i < 100; i++)
        {
            _intents.Clear();

            Tick();

            Assert.That(husk.Blackboard.StateTimer, Is.EqualTo(timer), "The wind-up is frozen.");
            Assert.That(_intents.CountEnemyMoves(husk.Id), Is.EqualTo(1));
            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero), "It stands.");
        }

        Assert.That(((ChaserBehaviour)husk.Behaviour).State, Is.EqualTo(ChaserState.Windup));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(telegraphs), "Nothing new is promised.");

        TickFor(0.25f);

        Assert.That(_system.Has(husk.Id, new ContentId(EnemyStatuses.StunnedKey)), Is.False);
        Assert.That(husk.Blackboard.StateTimer, Is.GreaterThan(timer), "It resumes where it stopped.");
    }

    [Test]
    public void Stun_NoContactDamage()
    {
        EnemyAgent husk = WindingUpHusk();

        TickFor(HuskWindup - 0.05f);

        float hp = _player.Health.Current;

        _system.Stun(husk.Id, 1f);

        TickFor(0.9f);

        Assert.That(_player.Health.Current, Is.EqualTo(hp), "No strike while stunned.");

        TickFor(0.3f);

        Assert.That(_player.Health.Current, Is.LessThan(hp), "The wind-up finishes afterwards.");
    }

    // ---- Rule 3 -------------------------------------------------------------------------------------

    [Test]
    public void Root_AllowsAttack_ZeroesMove()
    {
        EnemyAgent spitter = _system.Spawn(new ContentId(SpitterId), Vector3.Zero);

        Perceive(spitter, distance: 20f);

        _system.Root(spitter.Id, 10f);

        for (int i = 0; i < 10; i++)
        {
            _intents.Clear();

            Tick();
        }

        Assert.That(((SpitterBehaviour)spitter.Behaviour).State, Is.EqualTo(SpitterState.Approach), "It wants to walk.");
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero), "But it cannot.");
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(new Vector2(0f, 1f)), "It still faces.");

        Perceive(spitter, distance: 7f);

        TickFor(1.5f);

        Assert.That(_projectiles.InFlightCount, Is.EqualTo(1), "Rooted, it still fires.");
        Assert.That(_system.Has(spitter.Id, new ContentId(EnemyStatuses.RootedKey)), Is.True);
    }

    [Test]
    public void Root_StopsALungersDash()
    {
        EnemyAgent panther = _system.Spawn(new ContentId(PantherId), Vector3.Zero);
        var lunger = (LungerBehaviour)panther.Behaviour;

        Perceive(panther, distance: 8f);

        for (int i = 0; lunger.State != LungerState.Dash; i++)
        {
            Assert.That(i, Is.LessThan(500), "Sanity: it dashes.");

            Tick();
        }

        _system.Root(panther.Id, 5f);

        for (int i = 0; lunger.State == LungerState.Dash; i++)
        {
            Assert.That(i, Is.LessThan(500), "The dash plays out.");

            _intents.Clear();

            Tick();

            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero), "On the spot.");
        }

        Assert.That(lunger.State, Is.EqualTo(LungerState.Recover));
    }

    // ---- Rule 4 -------------------------------------------------------------------------------------

    [Test]
    public void Slow_StrongestWins_EndsCleanly()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        float speed = husk.MoveSpeed.Value;

        _system.Slow(husk.Id, 0.3f, 2f);

        Assert.That(husk.MoveSpeed.Value, Is.EqualTo(speed * 0.7f).Within(Tolerance));

        _system.Slow(husk.Id, 0.5f, 1f);

        Assert.That(husk.MoveSpeed.Value, Is.EqualTo(speed * 0.5f).Within(Tolerance), "Strongest wins, never stacks.");

        TickFor(1.2f);

        Assert.That(husk.MoveSpeed.Value, Is.EqualTo(speed * 0.7f).Within(Tolerance), "The weaker one is still active.");

        TickFor(1f);

        Assert.That(_system.Has(husk.Id, new ContentId(EnemyStatuses.SlowedKey)), Is.False);
        Assert.That(husk.MoveSpeed.Value, Is.EqualTo(speed), "Exactly its own modifier comes off.");
    }

    // ---- Rule 5 -------------------------------------------------------------------------------------

    [Test]
    public void Refresh_LongerWins()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        _system.Stun(husk.Id, 2f);
        _system.Stun(husk.Id, 1f);

        Assert.That(_system.Remaining(husk.Id, new ContentId(EnemyStatuses.StunnedKey)), Is.EqualTo(2f).Within(Tolerance));

        _system.Stun(husk.Id, 3f);

        Assert.That(_system.Remaining(husk.Id, new ContentId(EnemyStatuses.StunnedKey)), Is.EqualTo(3f).Within(Tolerance), "Never 5.");
    }

    // ---- Rule 7 -------------------------------------------------------------------------------------

    [Test]
    public void Events_StartRefreshEnd()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        _system.Root(husk.Id, 1f);
        _system.Root(husk.Id, 0.5f);
        _system.Root(husk.Id, 2f);

        TickFor(2.1f);

        IReadOnlyList<EnemyStatusChanged> changes = _events.Of<EnemyStatusChanged>();

        Assert.That(changes.Count, Is.EqualTo(3), "Start, lengthen, end; the shorter refresh is silent.");
        Assert.That(changes[0].Seconds, Is.EqualTo(1f).Within(Tolerance));
        Assert.That(changes[1].Seconds, Is.EqualTo(2f).Within(Tolerance));
        Assert.That(changes[2].Seconds, Is.Zero);

        for (int i = 0; i < changes.Count; i++)
        {
            Assert.That(changes[i].EnemyId, Is.EqualTo(husk.Id));
            Assert.That(changes[i].Status, Is.EqualTo(new ContentId(EnemyStatuses.RootedKey)));
        }
    }

    [Test]
    public void Death_NoEventAfterDied()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        float speed = husk.MoveSpeed.Value;

        _system.Stun(husk.Id, 2f);
        _system.Root(husk.Id, 2f);
        _system.Slow(husk.Id, 0.5f, 2f);

        _system.ApplyDamage(husk.Id, 10_000f, _clock, _player);

        TickFor(0.3f);

        IReadOnlyList<object> all = _events.All;
        int died = -1;

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is EnemyDied)
            {
                died = i;
            }
            else if (died >= 0 && all[i] is EnemyStatusChanged)
            {
                Assert.Fail("A status event followed EnemyDied.");
            }
        }

        Assert.That(died, Is.GreaterThanOrEqualTo(0), "Sanity: it died.");
        Assert.That(husk.CrowdControl.IsStunned, Is.False);
        Assert.That(husk.CrowdControl.IsRooted, Is.False);
        Assert.That(husk.MoveSpeed.Value, Is.EqualTo(speed));
        Assert.That(_system.Stun(husk.Id, 1f), Is.False, "A corpse cannot be stunned.");
    }

    // ---- Rule 8 -------------------------------------------------------------------------------------

    [Test]
    public void Boundary_Clears()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), Vector3.Zero);
        EnemyAgent other = _system.Spawn(new ContentId(HuskId), Vector3.One);

        int huskId = husk.Id;
        float speed = husk.MoveSpeed.Value;

        _system.Stun(huskId, 5f);
        _system.Slow(huskId, 0.5f, 5f);
        _system.Root(other.Id, 5f);

        Assert.That(_system.Despawn(other.Id), Is.True);
        Assert.That(_system.Has(other.Id, new ContentId(EnemyStatuses.RootedKey)), Is.False, "A despawn clears.");

        _system.Clear();

        Assert.That(_system.Has(huskId, new ContentId(EnemyStatuses.StunnedKey)), Is.False);
        Assert.That(_system.Stun(huskId, 1f), Is.False, "Unknown after the boundary.");

        EnemyAgent recycled = _system.Spawn(new ContentId(HuskId), Vector3.Zero);
        EnemyAgent second = _system.Spawn(new ContentId(HuskId), Vector3.One);

        foreach (EnemyAgent agent in new[] { recycled, second })
        {
            Assert.That(agent.CrowdControl.IsStunned, Is.False);
            Assert.That(agent.CrowdControl.IsRooted, Is.False);
            Assert.That(agent.CrowdControl.SlowFraction, Is.Zero);
            Assert.That(agent.MoveSpeed.Value, Is.EqualTo(speed), "No slow survives the recycle.");
        }
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);
        var projectiles = new ProjectileSystem(silent, Capacity);
        var lanes = new LaneSystem(silent);
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), Scaling(), Capacity);

        for (int i = 0; i < 16; i++)
        {
            EnemyAgent agent = system.Spawn(new ContentId(i % 2 == 0 ? HuskId : PantherId), new Vector3(i, 0f, 6f));

            agent.Blackboard.DistanceToPlayer = 6f;
            agent.Blackboard.DirectionToPlayer = new Vector2(0f, 1f);

            switch (i % 4)
            {
                case 0:
                    system.Stun(agent.Id, 1000f);
                    break;
                case 1:
                    system.Root(agent.Id, 1000f);
                    break;
                case 2:
                    system.Slow(agent.Id, 0.3f, 1000f);
                    system.Slow(agent.Id, 0.6f, 0.5f);
                    break;
            }
        }

        float clock = 0f;

        for (int i = 0; i < 240; i++)
        {
            intents.Clear();
            clock += Frame;
            system.Tick(new EnemyTickContext(Frame, clock, player, intents, silent, projectiles, system, lanes));
        }

        AllocationAssert.None(() =>
        {
            intents.Clear();
            clock += Frame;
            lanes.Tick(clock, new Vector3(50f, 0f, 50f), player);
            system.Tick(new EnemyTickContext(Frame, clock, player, intents, silent, projectiles, system, lanes));
        });
    }

    // ---- Fixture helpers ----------------------------------------------------------------------------

    /// <summary>A Husk inside its reach, ticked into its wind-up.</summary>
    private EnemyAgent WindingUpHusk()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), Vector3.Zero);
        var chaser = (ChaserBehaviour)husk.Behaviour;

        Perceive(husk, distance: 1f);

        for (int i = 0; chaser.State != ChaserState.Windup; i++)
        {
            Assert.That(i, Is.LessThan(10), "Sanity: it winds up.");

            Tick();
        }

        return husk;
    }

    /// <summary>The player <paramref name="distance"/> along +Z, in sight.</summary>
    private static void Perceive(EnemyAgent agent, float distance)
    {
        EnemyBlackboard blackboard = agent.Blackboard;

        blackboard.DistanceToPlayer = distance;
        blackboard.DirectionToPlayer = new Vector2(0f, 1f);
        blackboard.HasLineOfSight = true;
        blackboard.SelfPosition = agent.Position;
        blackboard.PlayerPosition = agent.Position + new Vector3(0f, 0f, distance);
    }

    private void Tick()
    {
        _lanes.Tick(_clock, new Vector3(50f, 0f, 50f), _player);
        _system.Tick(new EnemyTickContext(Frame, _clock, _player, _intents, _events, _projectiles, _system, _lanes));

        _clock += Frame;
    }

    private void TickFor(float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            Tick();
        }
    }

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound() },
        new[] { Husk(), Husk(EliteId, isElite: true), Spitter(), Panther(), Body() },
        bosses: new[] { Boss() });

    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());

    private static EnemySpec Husk(string id = HuskId, bool isElite = false) => new EnemySpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        maxHp: 36f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: 4,
        xpValue: 12f,
        isElite: isElite,
        contactDamage: 8f,
        reach: Reach,
        windupTime: HuskWindup,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Chaser);

    private static EnemySpec Spitter() => new EnemySpec(
        new ContentId(SpitterId),
        new LocKey("enemy.spitter.name"),
        maxHp: 28f,
        moveSpeed: 1.6f,
        targetPriority: 3,
        threatCost: 7,
        xpValue: 21f,
        isElite: false,
        contactDamage: 10f,
        reach: Reach,
        windupTime: 0.6f,
        recoverTime: 1.2f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Spitter,
        projectile: new ProjectileSpec(Standoff, 9f, 0.4f));

    private static EnemySpec Panther() => new EnemySpec(
        new ContentId(PantherId),
        new LocKey("enemy.panther.name"),
        maxHp: 44f,
        moveSpeed: 2.4f,
        targetPriority: 4,
        threatCost: 9,
        xpValue: 27f,
        isElite: false,
        contactDamage: 14f,
        reach: Reach,
        windupTime: 0.9f,
        recoverTime: 1f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Lunger,
        lunge: new LungeSpec(9f, 12f, 24f, 0.6f, 2f));

    private static EnemySpec Body() => new EnemySpec(
        new ContentId(BodyId),
        new LocKey("enemy.boss_body.name"),
        maxHp: 900f,
        moveSpeed: 1f,
        targetPriority: 8,
        threatCost: 40,
        xpValue: 120f,
        isElite: false,
        contactDamage: 20f,
        reach: 2f,
        windupTime: 1f,
        recoverTime: 1f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Boss);

    private static BossSpec Boss() => new BossSpec(
        new ContentId(BossId),
        new ContentId(BodyId),
        new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) },
        1.5f);

    /// <summary>The Oathbound of CC §7 with no Aegis, so a strike shows on its hit points.</summary>
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
        null,
        0.5f);
}
