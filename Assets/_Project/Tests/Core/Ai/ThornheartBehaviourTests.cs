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

/// <summary>JG-20b rules 1–9; the dispatch row lives in BossDispatchTests.</summary>
[TestFixture]
public sealed class ThornheartBehaviourTests
{
    private const string BodyId = "enemy.thornheart";
    private const string BossId = "boss.thornheart";
    private const string OathboundId = "character.oathbound";
    private const int Capacity = 32;
    private const float Contact = 20f;
    private const float Dt = 0.05f;
    private const float Tolerance = 1e-4f;

    /// <summary>Far from every wall, so nothing bites unless a row moves the player.</summary>
    private static readonly Vector3 Away = new Vector3(0f, 0f, -40f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private ThornSystem _thorns;
    private ProjectileSystem _projectiles;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private ThornheartBehaviour _heart;
    private Vector3 _playerAt;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _thorns = new ThornSystem(_events);
        _projectiles = new ProjectileSystem(_events, 1);
        var catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Body() },
            modes: null, skills: null, trees: null,
            bosses: new[] { Boss() });
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(),
            new DepthScaling(Scalings.Design()), Capacity,
            (agent, _) => new ThornheartBehaviour(agent, _thorns, new FixedRandom().Misc));
        _agent = _enemies.Spawn(new ContentId(BodyId), Vector3.Zero);
        _agent.Blackboard.DirectionToPlayer = Vector2.UnitX;
        _heart = Heart(0.5f);
        _playerAt = Away;
        _now = 0f;
    }

    // Rule 1.
    [Test]
    public void Rooted_NeverMoves_AlwaysVulnerable()
    {
        for (int i = 0; i < 300; i++)
        {
            _intents.Clear();
            Step();
            Assert.That(_intents.CountEnemyMoves(_agent.Id), Is.EqualTo(1));
            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
            Assert.That(_agent.IsVulnerable, Is.True);
        }

        Assert.That(Hit(10f).Applied, Is.GreaterThan(0f), "Never externally invulnerable.");
    }

    // Rule 2.
    [Test]
    public void Rotation_RootsSlamAlternate_WithGaps()
    {
        var times = new List<float>();
        var moves = new List<int>();
        for (int i = 0; i < 400 && times.Count < 4; i++)
        {
            int before = _events.Count<EnemyTelegraph>();
            Step();
            if (_events.Count<EnemyTelegraph>() > before)
            {
                times.Add(_now);
                moves.Add(_events.Of<EnemyTelegraph>()[before].Move);
                Assert.That((int)_heart.Move, Is.EqualTo(moves[moves.Count - 1]));
            }
        }

        Assert.That(moves, Is.EqualTo(new[] { 1, 2, 1, 2 }));
        float rootsCycle = ThornheartBehaviour.RootTelegraphSeconds + ThornheartBehaviour.RecoverSeconds
            + ThornheartBehaviour.AttackGapSeconds;
        float slamCycle = ThornheartBehaviour.SlamTelegraphSeconds + ThornheartBehaviour.RecoverSeconds
            + ThornheartBehaviour.AttackGapSeconds;
        Assert.That(times[0], Is.EqualTo(ThornheartBehaviour.AttackGapSeconds).Within(Dt * 1.5f));
        Assert.That(times[1] - times[0], Is.EqualTo(rootsCycle).Within(Dt * 3.5f));
        Assert.That(times[2] - times[1], Is.EqualTo(slamCycle).Within(Dt * 3.5f));
        Assert.That(times[3] - times[2], Is.EqualTo(rootsCycle).Within(Dt * 3.5f));
    }

    [Test]
    public void Telegraphs_PublishKinds()
    {
        RunUntilTelegraphs(2);
        IReadOnlyList<EnemyTelegraph> telegraphs = _events.Of<EnemyTelegraph>();
        Assert.That(telegraphs[0].Id, Is.EqualTo(_agent.Id));
        Assert.That(telegraphs[0].Move, Is.EqualTo((int)ThornheartMove.Roots));
        Assert.That(telegraphs[0].Duration, Is.EqualTo(ThornheartBehaviour.RootTelegraphSeconds));
        Assert.That(telegraphs[1].Move, Is.EqualTo((int)ThornheartMove.Slam));
        Assert.That(telegraphs[1].Duration, Is.EqualTo(ThornheartBehaviour.SlamTelegraphSeconds));
    }

    // Rule 3.
    [TestCase(0.5f, 0f)]
    [TestCase(0.75f, 10f)]
    [TestCase(0f, -20f)]
    [TestCase(1f, 20f)]
    public void Roots_ThreeSpokes_FirstAtPlayerWithinJitter_EvenlySpaced(float draw, float jitterDeg)
    {
        _heart = Heart(draw);
        RunUntilTelegraphs(1);
        IReadOnlyList<ThornArmed> spokes = _events.Of<ThornArmed>();
        Assert.That(spokes.Count, Is.EqualTo(ThornheartBehaviour.RootSpokes));
        for (int i = 0; i < spokes.Count; i++)
        {
            float angle = (jitterDeg + (i * 120f)) * (MathF.PI / 180f);
            Assert.That(spokes[i].Direction.X, Is.EqualTo(MathF.Cos(angle)).Within(Tolerance), $"spoke {i}");
            Assert.That(spokes[i].Direction.Y, Is.EqualTo(MathF.Sin(angle)).Within(Tolerance), $"spoke {i}");
        }
    }

    [Test]
    public void Roots_SpanInnerToOuter()
    {
        _enemies.Registry.Despawn(_agent.Id);
        _agent = _enemies.Spawn(new ContentId(BodyId), new Vector3(3f, 0f, 4f));
        _agent.Blackboard.DirectionToPlayer = Vector2.UnitX;
        _heart = Heart(0.5f);
        _playerAt = new Vector3(3f + 7f, 0f, 4f);
        RunUntilTelegraphs(1);
        ThornArmed first = _events.Of<ThornArmed>()[0];
        Assert.That(first.Origin.X, Is.EqualTo(3f + ThornheartBehaviour.RootInner).Within(Tolerance));
        Assert.That(first.Origin.Z, Is.EqualTo(4f).Within(Tolerance));
        Assert.That(first.Length, Is.EqualTo(ThornheartBehaviour.RootOuter - ThornheartBehaviour.RootInner));
        Assert.That(first.HalfWidth, Is.EqualTo(ThornheartBehaviour.RootHalfWidth));
        Assert.That(first.ArmSeconds, Is.EqualTo(ThornheartBehaviour.RootTelegraphSeconds));

        float hp = _player.Health.Current;
        RunUntil(() => _events.Count<ThornRose>() > 0);
        Assert.That(hp - _player.Health.Current, Is.EqualTo(Contact * ThornheartBehaviour.RootDamageScale).Within(Tolerance));
    }

    // Rule 4.
    [Test]
    public void Slam_BearingFixedAtTelegraph_LengthAndDamage()
    {
        RunUntilTelegraphs(1);
        _agent.Blackboard.DirectionToPlayer = Vector2.UnitX;
        RunUntilTelegraphs(2);
        int roots = ThornheartBehaviour.RootSpokes;
        _agent.Blackboard.DirectionToPlayer = Vector2.UnitY;
        _playerAt = new Vector3(5f, 0f, 0f);
        _intents.Clear();
        Step();
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitX), "Faces the fixed bearing.");

        ThornArmed slam = _events.Of<ThornArmed>()[roots];
        Assert.That(slam.Origin, Is.EqualTo(_agent.Position));
        Assert.That(slam.Direction, Is.EqualTo(Vector2.UnitX));
        Assert.That(slam.Length, Is.EqualTo(ThornheartBehaviour.SlamLength));
        Assert.That(slam.HalfWidth, Is.EqualTo(ThornheartBehaviour.SlamHalfWidth));
        Assert.That(slam.ArmSeconds, Is.EqualTo(ThornheartBehaviour.SlamTelegraphSeconds));

        float hp = _player.Health.Current;
        RunUntil(() => RoseCount() > roots);
        Assert.That(hp - _player.Health.Current, Is.EqualTo(Contact * ThornheartBehaviour.SlamDamageScale).Within(Tolerance));
    }

    // Rule 5.
    [Test]
    public void Exposed_OnRise_DoubleDamage_ForThreeSeconds()
    {
        _playerAt = new Vector3(5f, 0f, 0f);
        RunUntilSlamRises();
        Assert.That(_heart.IsExposed, Is.True, "Opens on the rise's own tick.");
        Assert.That(_events.Of<ThornRose>()[ThornheartBehaviour.RootSpokes].Hit, Is.True);
        HeartExposed open = _events.Single<HeartExposed>();
        Assert.That(open.BossId, Is.EqualTo(_agent.Id));
        Assert.That(open.Open, Is.True);
        Assert.That(open.Seconds, Is.EqualTo(ThornheartBehaviour.ExposedSeconds));
        Assert.That(Hit(10f).Applied, Is.EqualTo(20f).Within(Tolerance));

        float opened = _now;
        RunUntil(() => !_heart.IsExposed);
        Assert.That(_now - opened, Is.EqualTo(ThornheartBehaviour.ExposedSeconds).Within(Dt * 1.5f));
        Assert.That(_events.Count<HeartExposed>(), Is.EqualTo(2));
        Assert.That(_events.Of<HeartExposed>()[1].Open, Is.False);
        Assert.That(_agent.DamageTaken.ModifierCount, Is.Zero);
        Assert.That(Hit(10f).Applied, Is.EqualTo(10f).Within(Tolerance));
    }

    [Test]
    public void Exposed_Miss_StillOpens()
    {
        RunUntilSlamRises();
        Assert.That(_events.Of<ThornRose>()[ThornheartBehaviour.RootSpokes].Hit, Is.False);
        Assert.That(_heart.IsExposed, Is.True);
        Assert.That(_agent.DamageTaken.Value, Is.EqualTo(ThornheartBehaviour.ExposedDamageTaken).Within(Tolerance));
    }

    // Rule 6.
    [Test]
    public void Roots_SystemFull_SkipsSpoke()
    {
        for (int i = 0; i < ThornSystem.Capacity - 1; i++)
        {
            _thorns.Open(new Vector3(30f, 0f, i), Vector2.UnitX, 1f, 0.5f, 1_000f, 1f, 0f, 0f);
        }

        int filler = _events.Count<ThornArmed>();
        RunUntilTelegraphs(1);
        Assert.That(_events.Count<ThornArmed>() - filler, Is.EqualTo(1), "Two spokes refused.");

        RunUntilTelegraphs(2);
        Assert.That(_events.Of<EnemyTelegraph>()[1].Move, Is.EqualTo((int)ThornheartMove.Slam), "The rotation ran on.");
    }

    // Rule 7, through the census that skips a stunned boss.
    [Test]
    public void Stun_PausesRotationAndExposed()
    {
        _enemies.Registry.Despawn(_agent.Id);
        _agent = _enemies.SpawnBoss(new ContentId(BossId), Vector3.Zero);
        _agent.Blackboard.DirectionToPlayer = Vector2.UnitX;
        _heart = (ThornheartBehaviour)((BossBehaviour)_agent.Behaviour).Inner;

        RunUntilTelegraphs(2, census: true);
        Assert.That(_heart.Move, Is.EqualTo(ThornheartMove.Slam));
        float slamAt = _now;
        Assert.That(_enemies.Stun(_agent.Id, 4f), Is.True);
        int roseBefore = RoseCount();
        while (_agent.CrowdControl.IsStunned)
        {
            Step(census: true);
            if (_agent.CrowdControl.IsStunned)
            {
                Assert.That(_heart.IsExposed, Is.False, "Not ticked while stunned.");
            }
        }

        Assert.That(RoseCount(), Is.GreaterThan(roseBefore), "The armed wall rose on its own clock.");
        if (!_heart.IsExposed)
        {
            Step(census: true);
        }

        Assert.That(_heart.IsExposed, Is.True, "Opens on the first tick back.");
        float openedAt = _now;

        Assert.That(_enemies.Stun(_agent.Id, 4f), Is.True);
        RunUntil(() => !_heart.IsExposed, census: true);
        Assert.That(_now - openedAt, Is.GreaterThan(ThornheartBehaviour.ExposedSeconds + 1f), "The exposed clock paused.");

        RunUntilTelegraphs(3, census: true);
        float slamCycle = ThornheartBehaviour.SlamTelegraphSeconds + ThornheartBehaviour.RecoverSeconds
            + ThornheartBehaviour.AttackGapSeconds;
        Assert.That(_now - slamAt, Is.GreaterThan(slamCycle + 2f), "The rotation paused through both stuns.");
    }

    // Rule 8.
    [Test]
    public void Phase_WithersAll_ClosesHeart()
    {
        RunUntilSlamRises();
        Assert.That(_thorns.ActiveCount, Is.GreaterThan(0));
        int withered = _events.Count<ThornWithered>();
        _heart.OnPhaseEntered(1);
        Assert.That(_thorns.ActiveCount, Is.Zero);
        Assert.That(_events.Count<ThornWithered>(), Is.GreaterThan(withered));
        Assert.That(_heart.IsExposed, Is.False);
        Assert.That(_heart.Move, Is.EqualTo(ThornheartMove.None));
        Assert.That(_agent.DamageTaken.Value, Is.EqualTo(1f));
        Assert.That(_events.Of<HeartExposed>()[_events.Count<HeartExposed>() - 1].Open, Is.False);
    }

    [Test]
    public void Reset_RemovesModifier()
    {
        _agent.DamageTaken.Add(new Modifier(ModifierKind.Flat, 0f, this));
        RunUntilSlamRises();
        Assert.That(_agent.DamageTaken.ModifierCount, Is.EqualTo(2));
        _heart.Reset();
        Assert.That(_agent.DamageTaken.ModifierCount, Is.EqualTo(1), "Only its own modifier goes.");
        Assert.That(_heart.IsExposed, Is.False);
        Assert.That(_heart.Move, Is.EqualTo(ThornheartMove.None));

        int before = _events.Count<EnemyTelegraph>();
        RunUntilTelegraphs(before + 1);
        Assert.That(_events.Of<EnemyTelegraph>()[before].Move, Is.EqualTo((int)ThornheartMove.Roots), "Back to the start.");
    }

    // Rule 9.
    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var thorns = new ThornSystem(silent);
        var heart = new ThornheartBehaviour(_agent, thorns, new FixedRandom().Misc);
        float now = 0f;
        Action step = () =>
        {
            now += Dt;
            thorns.Tick(now, Away, _player);
            _intents.Clear();
            heart.Tick(new EnemyTickContext(Dt, now, _player, _intents, silent, _projectiles, _enemies));
        };

        // A full rotation and an opened heart first, so first-use list growth is not counted.
        for (int i = 0; i < 400; i++)
        {
            step();
        }

        AllocationAssert.None(step);
    }

    private void Step(bool census = false)
    {
        _now += Dt;
        _thorns.Tick(_now, _playerAt, _player);
        var ctx = new EnemyTickContext(Dt, _now, _player, _intents, _events, _projectiles, _enemies);
        if (census)
        {
            _enemies.Tick(ctx);
        }
        else
        {
            _heart.Tick(ctx);
        }
    }

    private void RunUntil(Func<bool> done, bool census = false)
    {
        for (int i = 0; i < 2_000 && !done(); i++)
        {
            Step(census);
        }

        Assert.That(done(), Is.True, "The condition never came.");
    }

    private void RunUntilTelegraphs(int count, bool census = false) =>
        RunUntil(() => _events.Count<EnemyTelegraph>() >= count, census);

    private void RunUntilSlamRises() => RunUntil(() => RoseCount() > ThornheartBehaviour.RootSpokes);

    private int RoseCount() => _events.Count<ThornRose>();

    private ThornheartBehaviour Heart(float draw) =>
        new ThornheartBehaviour(_agent, _thorns, new FixedRandom(draw).Misc);

    private DamageResult Hit(float damage) => _enemies.ApplyDamage(_agent.Id, damage, _now, _player);

    private static BossSpec Boss() => new BossSpec(
        new ContentId(BossId), new ContentId(BodyId), new[] { new BossPhaseSpec(1f) }, 1.5f);

    private static EnemySpec Body() => new EnemySpec(
        new ContentId(BodyId), new LocKey(BodyId + ".name"), 5_000f, 0f, 8, 40, 300f, false,
        Contact, 2.5f, 0.9f, 1.2f, 40f, EnemyBehaviourKind.Boss);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        1_000f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
