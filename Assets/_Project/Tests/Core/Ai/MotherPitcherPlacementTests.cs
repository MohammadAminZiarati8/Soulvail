using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>JG-13c rules 6–7: wrapper delegation, open ground and the empty-report fallback.</summary>
[TestFixture]
public sealed class MotherPitcherPlacementTests
{
    private const string OathboundId = "character.oathbound";
    private const string BodyId = "enemy.mother_pitcher";
    private const string BudId = "enemy.pitcher_bud";
    private const string FrogId = "enemy.frog";
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private FixedRandom _random;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Oathbound(), _events, _intents, 32);
        _projectiles = new ProjectileSystem(_events, 8);
        _random = new FixedRandom(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
        _enemies = new EnemySystem(new ContentCatalog(new[] { Oathbound() },
            new[] { Body(BodyId), Body(BudId), Body(FrogId) }),
            _events, new FixedRandom(), new DepthScaling(Scalings.Design()), 32);
        _agent = _enemies.Spawn(new ContentId(BodyId), new Vector3(10f, 2f, 10f));
    }

    [Test]
    public void Boss_WithoutAPlacer_KeepsTheRing()
    {
        // Both shipped non-placing inners keep their deterministic ring and never ask for ground.
        IEnemyBehaviour[] inners = { new WardenBehaviour(_agent, new ShockwaveSystem(_events), new FissureSystem(_events), _random.Misc),
            new CroakerBehaviour(_agent, new ShockwaveSystem(_events), new FissureSystem(_events), new LaneSystem(_events), _random.Misc) };
        foreach (IEnemyBehaviour inner in inners)
        {
            var boss = new BossBehaviour(_agent, Spec(), inner);
            boss.Tick(Context(Array.Empty<Vector3>()));
            AssertRing(3f);
            Assert.That(_random.Capture().Misc, Is.Zero);
            ClearAdds();
        }
    }

    [Test]
    public void Mother_Summons_StandOnOpenGround()
    {
        var ground = new Vector3[289];
        int index = 0;
        for (int x = -8; x <= 8; x++)
        {
            for (int z = -8; z <= 8; z++)
            {
                ground[index++] = _agent.Position + new Vector3(x, 0f, z);
            }
        }

        var boss = Mother();
        var ctx = Context(ground);
        _player.Blackboard.PlayerPosition = _agent.Position + new Vector3(-5f, 0f, 0f);
        boss.Tick(ctx);
        AssertPlacement(ground, 3);
        // Cross a phase: its three buds and two Frogs share the same placement path.
        _agent.DamageTaken.RemoveAll();
        _enemies.ApplyDamage(_agent.Id, 70f, 0f, _player);
        boss.Tick(ctx);
        _enemies.Tick(ctx); // Finish deferred phase despawns as the real behaviour pass does.
        Assert.That(boss.Phase, Is.EqualTo(1));
        AssertPlacement(ground, 5);
        Assert.That(_random.Capture().Misc, Is.EqualTo(8ul));
    }

    [Test]
    public void Mother_NoGround_SummonsFourMetresOut()
    {
        var boss = Mother();
        var ctx = Context(Array.Empty<Vector3>());
        boss.Tick(ctx);
        AssertRing(4f);
        Assert.That(_random.Capture().Misc, Is.Zero);
    }

    private void AssertPlacement(Vector3[] ground, int expected)
    {
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(expected + 1));
        ReadOnlySpan<EnemyAgent> agents = _enemies.Registry.Alive;
        for (int i = 1; i < agents.Length; i++)
        {
            Vector3 spot = agents[i].Position;
            Assert.That(ground, Does.Contain(spot));
            Assert.That(FlatDistance(spot, _agent.Position), Is.InRange(4f, 8f));
            Assert.That(FlatDistance(spot, _player.Blackboard.PlayerPosition), Is.GreaterThanOrEqualTo(3f));
            for (int j = 0; j < i; j++)
            {
                Assert.That(FlatDistance(spot, agents[j].Position), Is.GreaterThanOrEqualTo(1.5f));
            }
        }
    }

    private void AssertRing(float radius)
    {
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(4));
        for (int i = 1; i < 4; i++)
        {
            float angle = (i - 1) * (2f * MathF.PI / 3f);
            Vector3 expected = _agent.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * radius;
            Assert.That(Vector3.Distance(_enemies.Registry.Alive[i].Position, expected), Is.LessThan(1e-4f));
        }
    }

    private void ClearAdds()
    {
        while (_enemies.Registry.AliveCount > 1)
        {
            _enemies.Registry.Despawn(_enemies.Registry.Alive[1].Id);
        }
    }

    private float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
    private BossBehaviour Mother() => new BossBehaviour(_agent, Spec(),
        new MotherPitcherBehaviour(_agent, _projectiles, _random.Misc, new ContentId(BudId), new ContentId(FrogId)));
    private EnemyTickContext Context(Vector3[] ground) => new EnemyTickContext(
        0.1f, 0f, _player, _intents, _events, _projectiles, _enemies, openGround: ground);
    private BossSpec Spec() => new BossSpec(new ContentId("boss.mother_pitcher"), new ContentId(BodyId),
        new[] { new BossPhaseSpec(1f, new[] { new AddWave(new ContentId(BudId), 3) }),
            new BossPhaseSpec(0.66f, new[] { new AddWave(new ContentId(BudId), 3), new AddWave(new ContentId(FrogId), 2) }) }, 1.5f);
    private EnemySpec Body(string id) => new EnemySpec(new ContentId(id), new LocKey(id + ".name"),
        100f, 0f, 1, 4, 12f, false, 0f, 1f, 0.6f, 0.6f, 40f, EnemyBehaviourKind.Static);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);
}
