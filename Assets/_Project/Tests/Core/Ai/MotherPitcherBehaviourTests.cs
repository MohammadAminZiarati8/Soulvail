using System;
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

/// <summary>JG-12b rules 1�7; rule 8 lives in BossDispatchTests.</summary>
[TestFixture]
public sealed class MotherPitcherBehaviourTests
{
    private const string BodyId = "enemy.mother_pitcher";
    private const string BudId = "enemy.pitcher_bud";
    private const string FrogId = "enemy.frog";
    private const string OathboundId = "character.oathbound";
    private const string BossId = "boss.mother_pitcher";
    private const int Capacity = 32;
    private const float Tolerance = 1e-4f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private MotherPitcherBehaviour _mother;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, 8);
        var catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Body(BodyId, 1000f), Body(BudId, 10f), Body(FrogId, 10f) },
            modes: null, skills: null, trees: null,
            bosses: new[] { Boss() });
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(),
            new DepthScaling(Scalings.Design()), Capacity, (agent, _) => Mother(agent));
        _agent = _enemies.Spawn(new ContentId(BodyId), Vector3.Zero);
        _mother = Mother(_agent);
    }

    // Rule 1: the shared damage door and pooling reset.
    [Test]
    public void DamageTaken_ScalesTheHit()
    {
        _agent.DamageTaken.Add(new Modifier(ModifierKind.PercentMult, -0.8f, this));
        float before = _agent.Health.Current;
        DamageResult result = Hit(_agent, 50f);
        Assert.That(before - _agent.Health.Current, Is.EqualTo(10f).Within(Tolerance));
        Assert.That(result.Applied, Is.EqualTo(10f).Within(Tolerance));
        Assert.That(_events.Of<EnemyDamaged>()[0].Amount, Is.EqualTo(10f).Within(Tolerance));
    }

    [Test]
    public void DamageTaken_ClearedOnInitialise()
    {
        _agent.DamageTaken.Add(new Modifier(ModifierKind.PercentMult, -0.8f, this));
        _enemies.Registry.Despawn(_agent.Id);
        EnemyAgent recycled = _enemies.Spawn(new ContentId(BodyId), Vector3.Zero);
        Assert.That(recycled, Is.SameAs(_agent));
        Assert.That(recycled.DamageTaken.Value, Is.EqualTo(1f));
        Assert.That(recycled.DamageTaken.ModifierCount, Is.Zero);
    }

    // Rule 2: first tick, once per life, without a phase-change beat.
    [Test]
    public void Boss_OpeningPhase_SummonsOnItsFirstTick()
    {
        var boss = new BossBehaviour(_agent, Boss());
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(1));
        boss.Tick(Context());
        boss.Tick(Context());
        Assert.That(boss.AddCount, Is.EqualTo(3));
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(4));
        Assert.That(boss.IsInBeat, Is.False);
        boss.Reset();
        boss.Tick(Context());
        boss.Tick(Context());
        Assert.That(boss.AddCount, Is.EqualTo(3));
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(7));
    }

    // Rule 3: exactly one rooted intent of her own, facing each new player direction; buds write theirs.
    [Test]
    public void Mother_StandsRootedFacingThePlayer()
    {
        Bud();
        for (int i = 0; i < 8; i++)
        {
            var direction = new Vector2(MathF.Cos(i), MathF.Sin(i));
            _agent.Blackboard.DirectionToPlayer = direction;
            _intents.Clear();
            _mother.Tick(Context());
            Assert.That(_intents.CountEnemyMoves(_agent.Id), Is.EqualTo(1));
            Assert.That(_intents.LastEnemyMove.Id, Is.EqualTo(_agent.Id));
            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
            Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(direction));
        }
    }

    // JG-13b rule 3: every living bud stands rooted, turned to her.
    [Test]
    public void Mother_TurnsHerBudsToFaceHer()
    {
        EnemyAgent east = _enemies.Spawn(new ContentId(BudId), new Vector3(4f, 0f, 0f));
        EnemyAgent south = _enemies.Spawn(new ContentId(BudId), new Vector3(0f, 0f, -5f));
        _intents.Clear();
        _mother.Tick(Context());
        AssertFacesHer(east, new Vector2(-1f, 0f));
        AssertFacesHer(south, new Vector2(0f, 1f));
    }

    // Rule 4: any living bud, exactly one owned PercentMult, corpses excluded.
    [Test]
    public void Mother_BudAlive_TakesAFifth()
    {
        Bud();
        Bud();
        _agent.DamageTaken.Add(new Modifier(ModifierKind.Flat, 0f, this));
        _mother.Tick(Context());
        _mother.Tick(Context());
        Assert.That(_mother.IsShielded, Is.True);
        Assert.That(_agent.DamageTaken.ModifierCount, Is.EqualTo(2));
        var modifiers = new System.Collections.Generic.List<Modifier>();
        _agent.DamageTaken.CopyModifiersTo(modifiers);
        Assert.That(modifiers[1].Source, Is.SameAs(_mother));
        Assert.That(modifiers[1].Kind, Is.EqualTo(ModifierKind.PercentMult));
        Assert.That(modifiers[1].Value, Is.EqualTo(-0.8f));
        Assert.That(Hit(_agent, 100f).Applied, Is.EqualTo(20f).Within(Tolerance));
    }

    [Test]
    public void Mother_LastBudDies_TakesFull()
    {
        EnemyAgent first = Bud();
        EnemyAgent last = Bud();
        _enemies.Spawn(new ContentId(FrogId), Vector3.Zero);
        _mother.Tick(Context());
        Hit(first, 100f);
        _mother.Tick(Context());
        Assert.That(_mother.IsShielded, Is.True);
        Hit(last, 100f);
        _mother.Tick(Context());
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(4), "Corpses are still registered.");
        Assert.That(_mother.IsShielded, Is.False);
        Assert.That(_agent.DamageTaken.ModifierCount, Is.Zero);
        Assert.That(Hit(_agent, 100f).Applied, Is.EqualTo(100f));
    }

    // Rule 5: the real wrapper suppresses inner ticks during the crossing beat.
    [Test]
    public void Mother_Crossing66_RegrowsTheShield()
    {
        _enemies.Registry.Despawn(_agent.Id);
        _agent = _enemies.SpawnBoss(new ContentId(BossId), Vector3.Zero);
        var boss = (BossBehaviour)_agent.Behaviour;
        _mother = (MotherPitcherBehaviour)boss.Inner;
        _enemies.Tick(Context());
        foreach (EnemyAgent add in _enemies.Registry.Alive)
        {
            if (add.Spec.Id == new ContentId(BudId))
            {
                Hit(add, 100f);
            }
        }

        _enemies.Tick(Context());
        Assert.That(_mother.IsShielded, Is.False);
        Hit(_agent, 350f);
        _enemies.Tick(Context());
        Assert.That(boss.Phase, Is.EqualTo(1));
        Assert.That(boss.IsInBeat, Is.True);
        _enemies.Tick(Context(2f));
        Assert.That(boss.IsInBeat, Is.False);
        Assert.That(_mother.IsShielded, Is.True);
        Assert.That(Hit(_agent, 100f).Applied, Is.EqualTo(20f).Within(Tolerance));
    }

    // Rule 6: reset removes only this behaviour's modifier and can re-arm.
    [Test]
    public void Mother_Reset_DropsTheShield()
    {
        Bud();
        _agent.DamageTaken.Add(new Modifier(ModifierKind.Flat, 0f, this));
        _mother.Tick(Context());
        _mother.Reset();
        _mother.Reset();
        Assert.That(_mother.IsShielded, Is.False);
        Assert.That(_agent.DamageTaken.ModifierCount, Is.EqualTo(1));
        Assert.That(_agent.DamageTaken.Value, Is.EqualTo(1f));
        _mother.Tick(Context());
        Assert.That(_mother.IsShielded, Is.True);
        Assert.That(_agent.DamageTaken.ModifierCount, Is.EqualTo(2));
    }

    // Rule 7: repeated shield raises and removals as well as a living-bud scan.
    [Test]
    public void Mother_Tick_AllocatesNothing()
    {
        Bud();
        Bud();
        Bud();
        var ctx = Context();
        _mother.Tick(ctx);
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            _mother.Tick(ctx);
            _mother.Reset();
            _intents.Clear();
            _mother.Tick(ctx);
        });
    }

    private void AssertFacesHer(EnemyAgent bud, Vector2 facing)
    {
        Assert.That(_intents.CountEnemyMoves(bud.Id), Is.EqualTo(1), $"bud {bud.Id}'s intents");
        foreach (EnemyMoveIntent intent in _intents.EnemyMoves)
        {
            if (intent.Id == bud.Id)
            {
                Assert.That(intent.Velocity, Is.EqualTo(Vector3.Zero));
                Assert.That(intent.FacingXZ.X, Is.EqualTo(facing.X).Within(Tolerance));
                Assert.That(intent.FacingXZ.Y, Is.EqualTo(facing.Y).Within(Tolerance));
            }
        }
    }

    private MotherPitcherBehaviour Mother(EnemyAgent agent) => new MotherPitcherBehaviour(
        agent, _projectiles, new FixedRandom().Misc, new ContentId(BudId), new ContentId(FrogId));

    private EnemyAgent Bud() => _enemies.Spawn(new ContentId(BudId), Vector3.Zero);

    private DamageResult Hit(EnemyAgent agent, float damage) =>
        _enemies.ApplyDamage(agent.Id, damage, 0f, _player);

    private EnemyTickContext Context(float dt = 0.1f) => new EnemyTickContext(
        dt, 0f, _player, _intents, _events, _projectiles, _enemies);

    private static BossSpec Boss() => new BossSpec(
        new ContentId(BossId), new ContentId(BodyId),
        new[]
        {
            new BossPhaseSpec(1f, new[] { new AddWave(new ContentId(BudId), 3) }),
            new BossPhaseSpec(0.66f, new[] { new AddWave(new ContentId(BudId), 3) }),
        }, 1.5f);

    private static EnemySpec Body(string id, float hp) => new EnemySpec(
        new ContentId(id), new LocKey(id + ".name"), hp, 0f, 1, 4, 12f, false,
        0f, 1f, 0.6f, 0.6f, 40f, id == BodyId ? EnemyBehaviourKind.Boss : EnemyBehaviourKind.Static);

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
