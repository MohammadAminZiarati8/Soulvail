using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;
using Position = System.Numerics.Vector3;
using Direction = System.Numerics.Vector2;

namespace Soulvail.Tests.Game.Authoring;

[TestFixture]
public sealed class ShardbackStageTests
{
    private const string Root = "Assets/_Project/";
    private const string DunesPath = Root + "Prefabs/Arenas/Sunscar/";
    private static readonly ContentId Sunscar = new("mode.sunscar");
    private static readonly ContentId Dunes = new("arena.sunscar.dunes");
    private static readonly ContentId Ranger = new("character.ranger");
    private static readonly ContentId Shardback = new("enemy.shardback");
    private static readonly ContentId Boss = new("boss.shardback");
    private static readonly ContentId Beetle = new("enemy.beetle");

    [Test]
    public void Shardback_RegisteredBodyCoverAndName()
    {
        var boot = Boot();
        var enemy = Definitions<EnemyDefinition>(boot, "_enemies").Single(d => d.ToSpec().Id == Shardback);
        Assert.That(enemy.ToSpec().Behaviour, Is.EqualTo(EnemyBehaviourKind.Boss));
        Assert.That(enemy.ToSpec().NameKey.Key, Is.EqualTo("enemy.shardback.name"));
        EnemyLook look = enemy.ToLook();
        Assert.That(look.Body, Is.EqualTo(Load<GameObject>(Root + "Prefabs/Enemies/Shardback.prefab").GetComponent<EnemyView>()));
        Assert.That(look.Body.GetComponent<ShardbackView>(), Is.Not.Null);
        Assert.That(look.Cover, Is.EqualTo(Load<GameObject>(DunesPath + "ShardbackPillars.prefab")));
        Assert.That(look.Cover.GetComponentsInChildren<ShardbackPillar>(true).Select(p => p.Slot),
            Is.EquivalentTo(new[] { 0, 1, 2 }));
        var name = enemy.ToSpec().NameKey;
        Assert.That(Load<LocalizationTable>(Root + "Data/Localisation/English.asset").ToDictionary()[name],
            Is.EqualTo("Shardback"));
        Assert.That(Load<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset").ToDictionary()[name],
            Is.Not.Empty);
        BossSpec boss = Definitions<BossDefinition>(boot, "_bosses").Select(d => d.ToSpec()).Single(b => b.Id == Boss);
        Assert.That(boss.EnemySpecId, Is.EqualTo(Shardback));
        Assert.That(boss.SpawnsCentral, Is.True);
    }

    [Test]
    public void StageTen_RangerDefeatsShardbackAndTakesPortal()
    {
        using var fight = new Fight();
        fight.Until(() => fight.Events.Count<ShardbackFightStarted>() == 1, 10f);
        EnemySpawned boss = fight.Events.Single<EnemySpawned>();
        Assert.That(boss.SpecId, Is.EqualTo(Shardback));
        Assert.That(boss.Position, Is.EqualTo(fight.CentralSpawn));
        Assert.That(fight.Events.Single<StageArrived>().ArenaId, Is.EqualTo(Dunes));
        Assert.That(fight.Events.Count<WaveStarted>(), Is.EqualTo(1));
        Assert.That(fight.Events.Count<SpawnTelegraphed>(), Is.Zero);
        ShardbackPillar[] pillars = fight.StandingPillars;
        Assert.That(pillars.Select(p => p.Slot), Is.EquivalentTo(new[] { 0, 1, 2 }));
        Guid lifetime = fight.Events.Single<ShardbackFightStarted>().Lifetime;
        Assert.That(pillars.All(p => p.BossId == boss.Id && p.Lifetime == lifetime), Is.True);

        fight.Until(() => fight.Events.Of<EnemyDied>().Any(e => e.Id == boss.Id), 115f);
        float elapsed = fight.Session.State.Time - fight.SpawnedAt;
        float firstHit = fight.Events.Of<EnemyDamaged>().First(e => e.Id == boss.Id && e.Amount > 0f).Amount;
        TestContext.WriteLine($"Shardback {fight.BaseHp} base HP; stage-10 DT-01 Ranger level {fight.ArrivalLevel}, " +
            $"first bow hit {firstHit:F2}, no tree picks or actives; {elapsed:F2} s including both beats.");
        Assert.That(elapsed, Is.InRange(75f, 105f));
        Assert.That(fight.Events.Count<PlayerDied>(), Is.Zero);
        Assert.That(fight.Events.Of<ShardbackArmed>().Select(e => e.Move).Distinct(),
            Is.EquivalentTo(new[] { ShardbackMove.Fan, ShardbackMove.Charge, ShardbackMove.Burst }));
        var phases = fight.Events.Of<BossPhaseChanged>();
        Assert.That(phases.Select(e => (e.EnemyId, e.OfPhases, e.Phase)),
            Is.EqualTo(new[] { (boss.Id, 3, 0), (boss.Id, 3, 1), (boss.Id, 3, 2) }));
        Assert.That(phases[0].EntersBelow, Is.EqualTo(new[] { 1f, 0.66f, 0.33f }));
        Assert.That(fight.Events.Of<BossBeatStarted>().Select(e => e.Seconds), Is.EqualTo(new[] { 1.5f, 1.5f }));
        Assert.That(fight.Events.Count<BossBeatEnded>(), Is.EqualTo(2));

        fight.Until(() => fight.Events.Count<StageCleared>() == 1, 10f);
        StageCleared clear = fight.Events.Single<StageCleared>();
        Assert.That(clear.Stage, Is.EqualTo(10));
        Assert.That(clear.GatePosition, Is.EqualTo(fight.Arena.GatePosition.ToNum()));
        Assert.That(clear.NextArenaId, Is.EqualTo(Dunes));
        Assert.That(fight.Events.Count<ModeFinished>(), Is.Zero);
        Assert.That(fight.Events.Count<RunEnded>(), Is.Zero);
        fight.Player = clear.GatePosition;
        fight.Until(() => fight.Events.Count<SpawnTelegraphed>() > 0, 15f);
        Assert.That(fight.Session.State.StageIndex, Is.EqualTo(11));
        Assert.That(fight.Events.Of<StageArrived>().Select(e => (e.Stage, e.ArenaId)),
            Is.EqualTo(new[] { (10, Dunes), (11, Dunes) }));
        Assert.That(fight.StandingPillars, Is.Empty);
    }

    [TestCase(ShardbackMove.Fan, "hit", 14f)]
    [TestCase(ShardbackMove.Fan, "sidestep", 0f)]
    [TestCase(ShardbackMove.Fan, "cover", 0f)]
    [TestCase(ShardbackMove.Charge, "hit", 18f)]
    [TestCase(ShardbackMove.Charge, "sidestep", 0f)]
    [TestCase(ShardbackMove.Charge, "cover", 0f)]
    [TestCase(ShardbackMove.Burst, "hit", 16f)]
    [TestCase(ShardbackMove.Burst, "walk", 0f)]
    public void StageTen_ResolvedAttackChecksRangerDamage(ShardbackMove move, string response, float damage)
    {
        using var fight = new Fight(useBossBody: true);
        if (move == ShardbackMove.Burst)
        {
            fight.Until(() => fight.Events.Count<BossBeatEnded>() == 1, 50f);
        }
        else
        {
            fight.Until(() => fight.Events.Count<EnemySpawned>() == 1, 10f);
            Position boss = fight.BossPosition;
            Position aim = Position.UnitZ;
            if (response == "cover")
            {
                Position pillar = fight.StandingPillars.Single(p => p.Slot == 0).transform.position.ToNum();
                Position delta = pillar - boss;
                aim = Position.Normalize(new Position(delta.X, 0f, delta.Z));
            }
            fight.Player = boss + aim * 6f;
        }

        var noTarget = new ContentId("enemy.none");
        fight.Target = noTarget;
        if (move == ShardbackMove.Charge && response != "cover")
        {
            fight.Until(() => fight.Events.Count<ShardbackArmed>() == 1, 3f);
            Position aimedAt = fight.Player;
            fight.Player += Position.UnitX * 2f;
            fight.Until(() => fight.Events.Count<ShardbackImpact>() == 1, 2f);
            fight.Player = aimedAt;
        }

        fight.Until(() => fight.Events.Of<ShardbackArmed>().Any(e => e.Move == move), 18f);
        ShardbackArmed armed = fight.Events.Of<ShardbackArmed>().Last(e => e.Move == move);
        if (move == ShardbackMove.Burst)
        {
            float radius = response == "walk" ? 2f : 5f;
            Position retreat = OpenRetreat(fight.Arena, armed.Origin, armed.Reach);
            fight.Player = armed.Origin + retreat * radius;
            if (response == "walk")
                fight.Move = new Direction(retreat.X, retreat.Z);
        }
        else
        {
            fight.Until(() => fight.Events.Of<ShardbackClipped>().Any(e => e.Key.Equals(armed.Key)), 0.3f);
            fight.AssertPlayback(armed, "AttackTell");
            ShardbackClipped clipped = fight.Events.Of<ShardbackClipped>().Single(e => e.Key.Equals(armed.Key));
            float along = Position.Dot(fight.Player - armed.Origin, new Position(armed.Facing.X, 0f, armed.Facing.Y));
            if (response == "cover")
                Assert.That(clipped.Centre, Is.LessThan(along - 1f));
            else
                Assert.That(clipped.Centre, Is.GreaterThan(along + 0.5f));
            if (response == "sidestep")
            {
                if (move == ShardbackMove.Fan)
                    fight.Until(() => fight.Session.State.Time >= fight.ArmedAt + 0.45f, 0.55f);
                fight.Move = Direction.UnitX;
            }
        }
        if (move == ShardbackMove.Burst)
            fight.AssertPlayback(armed, "AttackTell");

        int previousHits = fight.Events.Count<PlayerDamaged>();
        fight.Until(() => fight.Events.Of<ShardbackImpact>().Any(e => e.Key.Equals(armed.Key)), 4f);
        fight.AssertPlayback(armed, "AttackEffect");
        PlayerDamaged[] hits = fight.Events.Of<PlayerDamaged>().Skip(previousHits).Where(e => !e.Blocked).ToArray();
        Position atImpact = fight.SampledPlayer - armed.Origin;
        float distance = new Direction(atImpact.X, atImpact.Z).Length();
        Assert.That(hits.Select(e => e.ToHp), Is.EqualTo(damage > 0f ? new[] { damage } : Array.Empty<float>()),
            $"Ranger ended {distance:F2} m from the cast origin.");
        if (response == "walk")
            Assert.That(distance, Is.GreaterThan(armed.Reach));
    }

    private static Position OpenRetreat(ArenaView arena, Position origin, float reach)
    {
        foreach (Position direction in new[] { Position.UnitZ, -Position.UnitZ, Position.UnitX, -Position.UnitX })
        {
            Position end = origin + direction * (reach + 0.6f);
            if (arena.PlayBounds.Contains(new Vector2(end.X, end.Z))
                && !Physics.SphereCast(origin.ToUnity() + Vector3.up * 0.9f, 0.45f, direction.ToUnity(), out _,
                    reach + 0.6f, LayerMask.GetMask("Cover", "Default"), QueryTriggerInteraction.Ignore))
                return direction;
        }
        throw new AssertionException("Expected a clear walking line out of the first burst.");
    }

    [Test]
    public void PhaseBeats_SummonTwoZeroXpBombardiersAndClearSurvivor()
    {
        using var fight = new Fight { Target = Shardback };
        fight.Until(() => fight.Events.Count<BossBeatStarted>() == 1, 50f);
        EnemySpawned[] adds = fight.Events.Of<EnemySpawned>().Where(e => e.SpecId == Beetle).ToArray();
        Assert.That(adds.Length, Is.EqualTo(2));
        Assert.That(fight.Events.Of<BossPhaseChanged>().Last().Phase, Is.EqualTo(1));
        Assert.That(adds.All(e => fight.Session.State.IsEnemyAlive(e.Id)), Is.True);
        AssertCrossedThreshold(fight, 0.66f);

        var before = (fight.Session.State.Level, fight.Session.State.Xp);
        Position arrival = fight.Player;
        fight.Player = Position.Lerp(adds[0].Position, arrival, 0.25f);
        fight.Target = Beetle;
        fight.Until(() => fight.Events.Of<EnemyDied>().Any(e => e.SpecId == Beetle), 10f);
        Assert.That((fight.Session.State.Level, fight.Session.State.Xp), Is.EqualTo(before));
        int survivor = adds.Single(e => fight.Session.State.IsEnemyAlive(e.Id)).Id;

        fight.Player = arrival;
        fight.Target = Shardback;
        fight.Until(() => fight.Events.Count<BossBeatStarted>() == 2, 50f);
        Assert.That(fight.Events.Of<BossPhaseChanged>().Last().Phase, Is.EqualTo(2));
        AssertCrossedThreshold(fight, 0.33f);
        Assert.That(fight.Events.Of<EnemyDespawned>().Any(e => e.Id == survivor), Is.True);
        Assert.That(fight.Session.State.IsEnemyAlive(survivor), Is.False);
        Assert.That(fight.Events.Of<EnemySpawned>().Select(e => e.SpecId), Is.EqualTo(new[] { Shardback, Beetle, Beetle }));
    }

    private static void AssertCrossedThreshold(Fight fight, float threshold)
    {
        int bossId = fight.Events.Single<ShardbackFightStarted>().BossId;
        var hits = fight.Events.Of<EnemyDamaged>().Where(e => e.Id == bossId && e.Amount > 0f).ToArray();
        Assert.That(hits[^2].HpFraction, Is.GreaterThan(threshold));
        Assert.That(hits[^1].HpFraction, Is.LessThanOrEqualTo(threshold));
    }

    private sealed class Fight : IDisposable
    {
        private const float Frame = 1f / 30f;
        private readonly WorldSnapshot _world = new(64);
        private readonly IntentBuffer _intents = new();
        private readonly FightEvents _events = new();
        private readonly Dictionary<int, (ContentId Spec, Position At)> _alive = new();
        private readonly GameObject _room;
        private readonly CoverViews _covers;
        private readonly IObjectResolver _container;
        private readonly ArenaPool _arenas;
        private readonly ShardbackProbeAdapter _probe;
        private readonly bool _useBossBody;
        private readonly PlayerView _playerBody;
        private EnemyView _bossBody;
        private ShardbackView _bossView;
        private Position _player;
        private int _read;

        public Fight(bool useBossBody = false)
        {
            _useBossBody = useBossBody;
            var boot = Boot();
            EnemyDefinition[] enemies = Definitions<EnemyDefinition>(boot, "_enemies");
            CharacterDefinition ranger = Object.Instantiate(Load<CharacterDefinition>(Root + "Data/Characters/Ranger.asset"));
            ContentCatalog catalog;
            try
            {
                var row = new SerializedObject(ranger);
                row.FindProperty("_maxHp").floatValue = 100000f;
                row.ApplyModifiedPropertiesWithoutUndo();
                catalog = new ContentCatalog(new[] { ranger.ToSpec() }, enemies.Select(d => d.ToSpec()).ToArray(),
                    Definitions<ModeDefinition>(boot, "_modes").Select(d => d.ToSpec()).ToArray(),
                    bosses: Definitions<BossDefinition>(boot, "_bosses").Select(d => d.ToSpec()).ToArray());
            }
            finally
            {
                Object.DestroyImmediate(ranger);
            }

            var random = new SeededRandom(12);
            RunSnapshot restore = new TestStart(catalog).At(Sunscar, Ranger, random.Seed, random.Capture(), 10);
            ArrivalLevel = restore.Level;
            BaseHp = catalog.Enemy(Shardback).MaxHp;
            _room = new GameObject("Isolated Sunscar stage probe");
            _room.transform.position = new Vector3(3000f, 0f, 3000f);
            var looks = new EnemyLookBook(enemies.ToDictionary(d => d.ToSpec().Id, d => d.ToLook()));
            _covers = new CoverViews(looks, _events.Hub, _room.transform);
            _container = new ContainerBuilder().Build();
            _playerBody = useBossBody
                ? Object.Instantiate(Load<GameObject>(Root + "Prefabs/Player/Player_Ranger.prefab"), _room.transform)
                    .GetComponent<PlayerView>()
                : new GameObject("Ranger arrival marker").AddComponent<PlayerView>();
            _playerBody.transform.SetParent(_room.transform, false);
            var dunes = Load<GameObject>(DunesPath + "Arena_Dunes.prefab").GetComponent<ArenaView>();
            _arenas = new ArenaPool(_container, new[] { dunes }, _room.transform, _events.Hub, _playerBody);
            _probe = new ShardbackProbeAdapter(LayerMask.GetMask("Cover"), _arenas);
            Session = new RunSession(catalog, random, _events, _intents,
                new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), _events), 64, 28, 64);
            Session.Start(new RunConfig(Sunscar, Ranger, random.Seed, 10, SpawnPlan.Empty, restore));
            Player = _playerBody.Position.ToNum();
        }

        public RunSession Session { get; }
        public ArenaView Arena => _arenas.Active;
        public ShardbackPillar[] StandingPillars => _room.GetComponentsInChildren<ShardbackPillar>();
        public RecordingEvents Events => _events.Recorded;
        public int ArrivalLevel { get; }
        public float BaseHp { get; }
        public Position CentralSpawn => Arena.transform.position.ToNum() + new Position(0f, 0f, 2f);
        public float SpawnedAt { get; private set; }
        public float ArmedAt { get; private set; }
        public Position BossPosition => _bossBody.Position.ToNum();
        public Position SampledPlayer => _world.PlayerPosition;
        public Position Player
        {
            get => _useBossBody ? _playerBody.Position.ToNum() : _player;
            set
            {
                _player = value;
                if (_useBossBody)
                    _playerBody.Teleport(value.ToUnity());
            }
        }
        public Direction Move { get; set; }
        public ContentId? Target { get; set; }

        public void AssertPlayback(in ShardbackArmed armed, string meshName)
        {
            Vector3 heading = new(armed.Facing.X, 0f, armed.Facing.Y);
            Vector3 body = _bossBody.GetComponentInChildren<Animator>().transform.forward;
            MeshRenderer renderer = _bossBody.transform.Find(meshName).GetComponent<MeshRenderer>();
            Assert.That(renderer.enabled, Is.True, meshName);
            Assert.That(Vector3.Angle(body, heading), Is.LessThan(0.1f));
            Assert.That(Vector3.Angle(renderer.transform.forward, heading), Is.LessThan(0.1f));
        }

        public void Until(Func<bool> condition, float seconds)
        {
            for (int i = 0; i < seconds / Frame && !condition(); i++)
                Tick();
            Assert.That(condition(), Is.True, $"Timed out at {Session.State.Time:F2} s.");
        }

        private void Tick()
        {
            _world.Clear();
            _world.Dt = Frame;
            _world.PlayerPosition = Player;
            _world.MoveInput = Move;
            _world.HasGate = true;
            _world.GatePosition = Arena.GatePosition.ToNum();
            _world.SpawnPoints = _arenas.SpawnPoints;
            foreach (var (id, enemy) in _alive)
            {
                bool behindCover = Target.HasValue && enemy.Spec != Target.Value;
                _world.AddEnemy() = new EnemySense
                {
                    Id = id,
                    Position = enemy.At,
                    HasLineOfSight = !behindCover,
                    BehindCover = behindCover
                };
            }
            _intents.Clear();
            Session.Tick(_world);
            if (_useBossBody && _intents.HasPlayerMove)
                _playerBody.Apply(_intents.PlayerMove, Frame);
            for (; _read < Events.All.Count; _read++)
            {
                switch (Events.All[_read])
                {
                    case EnemySpawned spawned:
                        _alive.Add(spawned.Id, (spawned.SpecId, spawned.Position));
                        if (spawned.SpecId == Shardback)
                        {
                            SpawnedAt = Session.State.Time;
                            if (_useBossBody)
                            {
                                var body = Load<GameObject>(Root + "Prefabs/Enemies/Shardback.prefab");
                                _bossBody = Object.Instantiate(body, _room.transform).GetComponent<EnemyView>();
                                _bossBody.Bind(spawned.Id, spawned.Position.ToUnity());
                                _bossView = _bossBody.GetComponent<ShardbackView>();
                                _bossView.Construct(_events.Hub);
                            }
                        }
                        break;
                    case EnemyDied died:
                        _alive.Remove(died.Id);
                        break;
                    case ShardbackArmed:
                        ArmedAt = Session.State.Time;
                        break;
                    case EnemyDespawned gone:
                        _alive.Remove(gone.Id);
                        break;
                }
            }
            foreach (EnemyMoveIntent move in _intents.EnemyMoves)
            {
                if (_alive.TryGetValue(move.Id, out var enemy))
                {
                    if (_bossBody != null && _bossBody.Id == move.Id)
                    {
                        _bossBody.Apply(move, Frame);
                        _bossView.Step(Frame);
                        _alive[move.Id] = (enemy.Spec, _bossBody.Position.ToNum());
                    }
                    else
                        _alive[move.Id] = (enemy.Spec, enemy.At + move.Velocity * Frame);
                }
            }
            if (_intents.LaneProbes.Count > 0)
            {
                Physics.SyncTransforms();
                _probe.Resolve(_intents, Session);
            }
        }

        public void Dispose()
        {
            Session.End();
            _covers.Dispose();
            _arenas.Dispose();
            _container.Dispose();
            _events.Hub.Dispose();
            Object.DestroyImmediate(_room);
            Physics.SyncTransforms();
        }
    }

    private sealed class FightEvents : IDomainEvents
    {
        public RecordingEvents Recorded { get; } = new();
        public DomainEventHub Hub { get; } = new();

        public void Publish<T>(in T evt) where T : struct
        {
            Recorded.Publish(evt);
            Hub.Publish(evt);
        }
    }

    private static SerializedObject Boot() =>
        new(Load<GameObject>(Root + "Prefabs/Composition/BootScope.prefab").GetComponent<BootScope>());

    private static T[] Definitions<T>(SerializedObject boot, string field) where T : ScriptableObject
    {
        SerializedProperty rows = boot.FindProperty(field);
        return Enumerable.Range(0, rows.arraySize).Select(i => (T)rows.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
    }

    private static T Load<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }
}
