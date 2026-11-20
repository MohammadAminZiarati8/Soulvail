using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
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
using UnityEngine.AI;
using VContainer;
using Direction = System.Numerics.Vector2;
using Object = UnityEngine.Object;
using Position = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Authoring;

[TestFixture]
public sealed class HierophantStageTests
{
    private const string Root = "Assets/_Project/";
    private const float Frame = 1f / 30f;
    private static readonly ContentId Sunscar = new("mode.sunscar");
    private static readonly ContentId Dunes = new("arena.sunscar.dunes");
    private static readonly ContentId Ranger = new("character.ranger");
    private static readonly ContentId Hierophant = new("enemy.hierophant");
    private static readonly ContentId Priest = new("enemy.jackal-priest");
    private static readonly ContentId Slinger = new("enemy.jackal");
    private static readonly ContentId NoTarget = new("enemy.none");

    [Test]
    public void Schedule_RegistersStage15Only()
    {
        var catalog = Catalog(out EnemyDefinition body);
        var bossId = new ContentId("boss.hierophant");
        BossSpec boss = catalog.Boss(bossId);
        Assert.That(boss.EnemySpecId, Is.EqualTo(Hierophant));
        Assert.That(boss.SpawnsCentral, Is.True);
        Assert.That(body.ToSpec().Behaviour, Is.EqualTo(EnemyBehaviourKind.Boss));
        Assert.That(body.ToLook().Body.GetComponent<HierophantView>(), Is.Not.Null);
        var name = body.ToSpec().NameKey;
        Assert.That(name.Key, Is.EqualTo("enemy.hierophant.name"));
        Assert.That(Load<LocalizationTable>("Data/Localisation/English.asset").ToDictionary()[name],
            Is.EqualTo("Sunscar Hierophant"));
        Assert.That(Load<LocalizationTable>("Data/Localisation/Pseudo.asset").ToDictionary()[name], Is.Not.Empty);

        AssertSchedule(catalog.Mode(Sunscar), (5, "boss.duneclaw"), (10, "boss.shardback"), (15, bossId.Value));
        AssertSchedule(catalog.Mode(new ContentId("mode.jungle")), (5, "boss.croaker"), (10, "boss.mother_pitcher"),
            (15, "boss.mossguard_warlord"), (20, "boss.thornheart"), (25, "boss.gravemaw"), (30, "boss.apex"));
        Assert.That(catalog.Mode(Sunscar).TryGetScriptedStage(14, out _), Is.True);
        Assert.That(catalog.Mode(Sunscar).TryGetScriptedStage(16, out _), Is.True);
    }

    [Test]
    public void Phases_SummonAndClearJackals()
    {
        using var fight = new Fight { Target = Hierophant };
        fight.Until(() => fight.Events.Count<BossBeatStarted>() == 1, 50f);
        EnemySpawned boss = fight.Events.Of<EnemySpawned>().Single(e => e.SpecId == Hierophant);
        EnemySpawned priest = fight.Events.Of<EnemySpawned>().Single(e => e.SpecId == Priest);
        EnemySpawned slinger = fight.Events.Of<EnemySpawned>().Single(e => e.SpecId == Slinger);
        AssertThreshold(fight, boss.Id, 0.66f);
        Assert.That(fight.Session.State.IsEnemyAlive(priest.Id), Is.True);
        Assert.That(fight.Session.State.IsEnemyAlive(slinger.Id), Is.True);
        var progress = (fight.Session.State.Level, fight.Session.State.Xp);

        // The boss is now wounded, but a priest with only a healthy slinger will not mend it.
        fight.Target = NoTarget;
        fight.Until(() => fight.Events.Count<BossBeatEnded>() == 1, 2f);
        Assert.That(fight.Events.Count<PriestMendStarted>(), Is.Zero);
        fight.Target = Slinger;
        fight.Until(() => fight.Events.Of<EnemyDamaged>().Any(e => e.Id == slinger.Id && !e.Killed), 4f);
        fight.Target = NoTarget;
        fight.Until(() => fight.Events.Of<EnemyHealed>().Any(e => e.EnemyId == slinger.Id), 2f);
        PriestMendStarted mend = fight.Events.Single<PriestMendStarted>();
        Assert.That((mend.Id, mend.AllyId), Is.EqualTo((priest.Id, slinger.Id)));

        fight.Target = Priest;
        fight.Until(() => fight.Events.Count<PriestMendEnded>() > 0, 3f);
        PriestMendEnded broken = fight.Events.Of<PriestMendEnded>()[0];
        Assert.That((broken.Id, broken.AllyId, broken.Reason), Is.EqualTo((priest.Id, slinger.Id, MendEndReason.Broken)));
        fight.Target = Slinger;
        fight.Until(() => !fight.Session.State.IsEnemyAlive(slinger.Id), 5f);
        Assert.That(fight.Events.Of<EnemyDied>().Any(e => e.Id == slinger.Id), Is.True);
        Assert.That((fight.Session.State.Level, fight.Session.State.Xp), Is.EqualTo(progress));

        fight.Target = Hierophant;
        fight.Until(() => fight.Events.Count<BossBeatStarted>() == 2, 50f);
        AssertThreshold(fight, boss.Id, 0.33f);
        Assert.That(fight.Events.Of<EnemyDespawned>().Any(e => e.Id == priest.Id), Is.True);
        Assert.That(fight.Session.State.IsEnemyAlive(priest.Id), Is.False);
        Assert.That(fight.Events.Of<EnemySpawned>().Select(e => e.SpecId), Is.EqualTo(new[] { Hierophant, Priest, Slinger }));
    }

    [Test]
    public void BossDeath_ClearsBothJackalsWithoutTheirXp()
    {
        using var fight = new Fight(useFinisher: true) { Target = Hierophant };
        fight.Until(() => fight.Events.Count<BossBeatStarted>() == 1, 20f);
        int[] adds = fight.Events.Of<EnemySpawned>().Where(e => e.SpecId != Hierophant).Select(e => e.Id).ToArray();
        Assert.That(adds.Length, Is.EqualTo(2));
        Assert.That(adds.All(fight.Session.State.IsEnemyAlive), Is.True);

        fight.Until(() => fight.Events.Count<StageCleared>() == 1, 20f);
        Assert.That(fight.Events.Count<BossBeatStarted>(), Is.EqualTo(1), "The finishing hit skipped the second beat.");
        Assert.That(fight.Events.Of<EnemyDied>().Skip(1).Select(e => e.Id), Is.EquivalentTo(adds));
        Assert.That(adds.All(id => !fight.Session.State.IsEnemyAlive(id)), Is.True);
        Assert.That(fight.Events.Single<XpChanged>().Gained, Is.EqualTo(fight.BossXp));
    }

    [Test]
    public void RangerProbe_WithinTargetAndOpensStage16()
    {
        using var fight = new Fight { Target = Hierophant };
        fight.Until(() => fight.Events.Count<EnemySpawned>() == 1, 10f);
        EnemySpawned boss = fight.Events.Single<EnemySpawned>();
        Assert.That((boss.SpecId, boss.Position), Is.EqualTo((Hierophant, new Position(0f, 0f, 2f))));
        Assert.That(fight.Events.Single<StageArrived>().ArenaId, Is.EqualTo(Dunes));
        Assert.That(fight.Events.Count<SpawnTelegraphed>(), Is.Zero);
        Assert.That(fight.ArrivalPoints, Is.EqualTo(fight.ArrivalLevel - 1));

        fight.Until(() => fight.Events.Of<EnemyDied>().Any(e => e.Id == boss.Id), 115f);
        float elapsed = fight.Session.State.Time - fight.SpawnedAt;
        float firstHit = fight.Events.Of<EnemyDamaged>().First(e => e.Id == boss.Id && e.Amount > 0f).Amount;
        TestContext.WriteLine($"DT-01 Stage 15 Ranger level {fight.ArrivalLevel}, first bow hit {firstHit:F2}, " +
            $"{fight.BaseHp} authored HP, {elapsed:F2} s from spawn through both beats. Points banked, no actives.");
        Assert.That(elapsed, Is.InRange(75f, 105f));
        Assert.That(fight.Events.Count<PlayerDied>(), Is.Zero);
        Assert.That(fight.Events.Of<BossBeatStarted>().Select(e => e.Seconds), Is.EqualTo(new[] { 1.5f, 1.5f }));
        Assert.That(fight.Events.Count<BossBeatEnded>(), Is.EqualTo(2));
        Assert.That(fight.Events.Of<BossPhaseChanged>().Select(e => e.Phase), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(fight.Events.Of<EnemySpawned>().All(e => !fight.Session.State.IsEnemyAlive(e.Id)), Is.True);

        fight.Until(() => fight.Events.Count<StageCleared>() == 1, 10f);
        StageCleared clear = fight.Events.Single<StageCleared>();
        Assert.That((clear.Stage, clear.NextArenaId), Is.EqualTo((15, Dunes)));
        Assert.That(clear.GatePosition, Is.EqualTo(fight.Arena.GatePosition.ToNum()));
        Assert.That(clear.GatePosition.Z, Is.GreaterThan(fight.Arena.PlayerStart.z));
        fight.WalkToGate();
        fight.Until(() => fight.Events.Count<SpawnTelegraphed>() > 0, 15f);
        Assert.That(fight.Events.Of<StageArrived>().Select(e => (e.Stage, e.ArenaId)),
            Is.EqualTo(new[] { (15, Dunes), (16, Dunes) }));
    }

    [Test]
    public void SealLayout_AllowsWalkingEscape()
    {
        using var fight = new Fight { Target = Hierophant };
        fight.Until(() => fight.Events.Count<HierophantArmed>() > 0, 10f);
        HierophantArmed first = fight.Events.Of<HierophantArmed>()[0];
        HierophantSealsPlaced seals = fight.Events.Single<HierophantSealsPlaced>();
        foreach (Direction centre in new[] { seals.West, seals.Centre, seals.East })
            CheckStrip(fight, new Vector3(centre.X, 0f, centre.Y), first);

        fight.Player.Teleport(fight.Arena.PlayerStart);
        fight.Until(() => fight.Events.Count<BossBeatStarted>() == 2, 95f);
        fight.Target = NoTarget;
        fight.ReturnToLane = false;
        fight.Player.Teleport(new Vector3(seals.Centre.X, 0f, seals.Centre.Y));
        int warnings = fight.Events.Count<HierophantArmed>();
        int impacts = fight.Events.Count<HierophantImpact>();
        int hits = fight.Events.Count<PlayerDamaged>();
        fight.Until(() => fight.Events.Count<HierophantArmed>() == warnings + 2, 5f);
        HierophantArmed[] pair = fight.Events.Of<HierophantArmed>().Skip(warnings).ToArray();
        Assert.That(pair.Select(e => e.Move), Is.All.EqualTo(HierophantMove.SunSeal));
        Assert.That(pair[1].SealSlot, Is.Not.EqualTo(pair[0].SealSlot));
        Assert.That(pair.Select(e => e.Seconds), Is.All.EqualTo(first.Seconds));
        fight.Until(() => fight.Events.Count<HierophantImpact>() == impacts + 2, 2f);
        Assert.That(fight.Events.Count<PlayerDamaged>(), Is.EqualTo(hits), "The Ranger walks through both warnings without a hit.");
        // Use the geometry emitted by each live warning, including the second strip chosen after walking.
        foreach (HierophantArmed armed in pair)
            CheckStrip(fight, armed.Origin.ToUnity(), armed);
    }

    private static void CheckStrip(Fight fight, Vector3 centre, HierophantArmed warning)
    {
        Physics.SyncTransforms();
        int tested = 0;
        for (float z = -warning.Reach; z <= warning.Reach; z += 0.5f)
        {
            for (float x = -warning.HalfWidth; x <= warning.HalfWidth; x += 0.5f)
            {
                Vector3 sample = centre + new Vector3(x, 0f, z);
                if (!NavMesh.SamplePosition(sample, out NavMeshHit walkable, 0.1f, NavMesh.AllAreas))
                {
                    Assert.That((x, z), Is.Not.EqualTo((0f, 0f)), $"Seal centre {centre} must be walkable.");
                    continue;
                }

                Assert.That(WalkAcross(fight, walkable.position, centre.x, warning, -1)
                    || WalkAcross(fight, walkable.position, centre.x, warning, 1), Is.True,
                    $"No long-edge escape for {sample} in the strip centred at {centre}.");
                tested++;
            }
        }
        TestContext.WriteLine($"Seal x={centre.x:F1}. Verified {tested} walkable samples within {warning.Seconds:F2} s.");
    }

    private static bool WalkAcross(Fight fight, Vector3 start, float centreX, HierophantArmed warning, int sign)
    {
        float edge = centreX + sign * (warning.HalfWidth + fight.PlayerRadius);
        var end = new Vector3(edge, start.y, start.z);
        if (NavMesh.Raycast(start, end, out _, NavMesh.AllAreas))
            return false;

        fight.Player.Teleport(start);
        var motor = new PlayerMotor(fight.Movement, Position.UnitZ);
        for (int frame = 0; frame < (int)(warning.Seconds / Frame); frame++)
        {
            motor.Tick(Frame, new Direction(sign, 0f), null);
            fight.Player.Apply(new PlayerMoveIntent(motor.Velocity, Position.UnitZ), Frame);
            if (sign * (fight.Player.Position.x - edge) >= 0f)
                return true;
        }
        return false;
    }

    private static void AssertThreshold(Fight fight, int id, float threshold)
    {
        var hits = fight.Events.Of<EnemyDamaged>().Where(e => e.Id == id && e.Amount > 0f).ToArray();
        Assert.That(hits[^2].HpFraction, Is.GreaterThan(threshold));
        Assert.That(hits[^1].HpFraction, Is.LessThanOrEqualTo(threshold));
    }

    private static void AssertSchedule(ModeSpec mode, params (int Stage, string Id)[] expected)
    {
        var actual = new List<(int, string)>();
        for (int stage = 1; stage <= mode.FinalStage; stage++)
        {
            if (mode.TryGetBossFor(stage, out ContentId boss))
                actual.Add((stage, boss.Value));
        }
        Assert.That(actual, Is.EqualTo(expected));
    }

    private static ContentCatalog Catalog(out EnemyDefinition body, bool useFinisher = false)
    {
        var boot = new SerializedObject(Load<GameObject>("Prefabs/Composition/BootScope.prefab").GetComponent<BootScope>());
        EnemyDefinition[] enemies = Definitions<EnemyDefinition>(boot, "_enemies");
        body = enemies.Single(d => d.ToSpec().Id == Hierophant);
        var ranger = Load<CharacterDefinition>("Data/Characters/Ranger.asset");
        CharacterSpec character = ranger.ToSpec();
        if (useFinisher)
        {
            var copy = Object.Instantiate(ranger);
            try
            {
                // Two arrows at this depth: one across 66%, one lethal before the 33% beat can clear adds.
                var serialized = new SerializedObject(copy);
                serialized.FindProperty("_weaponDamage").floatValue = body.ToSpec().MaxHp * 0.72f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                character = copy.ToSpec();
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }
        return new ContentCatalog(new[] { character },
            enemies.Select(d => d.ToSpec()).ToArray(),
            Definitions<ModeDefinition>(boot, "_modes").Select(d => d.ToSpec()).ToArray(),
            skills: Definitions<SkillDefinition>(boot, "_skills").Select(d => d.ToSpec()).ToArray(),
            trees: new[] { Load<SkillTreeDefinition>("Data/Trees/Ranger.asset").ToSpec() },
            bosses: Definitions<BossDefinition>(boot, "_bosses").Select(d => d.ToSpec()).ToArray());
    }

    private static T[] Definitions<T>(SerializedObject boot, string field) where T : ScriptableObject
    {
        SerializedProperty rows = boot.FindProperty(field);
        return Enumerable.Range(0, rows.arraySize).Select(i => (T)rows.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
    }

    private static T Load<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(Root + path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }

    private sealed class Fight : IDisposable
    {
        private readonly WorldSnapshot _world = new(64);
        private readonly IntentBuffer _intents = new();
        private readonly FightEvents _events = new();
        private readonly Dictionary<int, (ContentId Spec, Position At, Position Velocity)> _alive = new();
        private readonly GameObject _room = new("Hierophant stage probe");
        private readonly IObjectResolver _container = new ContainerBuilder().Build();
        private readonly ArenaPool _arenas;
        private readonly NavMeshDataInstance _navigation;
        private HierophantArmed? _warning;
        private Vector3? _waypoint;
        private int _read;

        public Fight(bool useFinisher = false)
        {
            ContentCatalog catalog = Catalog(out _, useFinisher);
            var random = new SeededRandom(12);
            RunSnapshot restore = new TestStart(catalog).At(Sunscar, Ranger, random.Seed, random.Capture(), 15);
            BaseHp = catalog.Enemy(Hierophant).MaxHp;
            BossXp = catalog.Enemy(Hierophant).XpValue;
            Movement = catalog.Character(Ranger).Movement;
            Player = Object.Instantiate(Load<GameObject>("Prefabs/Player/Player_Ranger.prefab"), _room.transform)
                .GetComponent<PlayerView>();
            PlayerRadius = Player.GetComponent<CharacterController>().radius;
            var dunes = Load<GameObject>("Prefabs/Arenas/Sunscar/Arena_Dunes.prefab").GetComponent<ArenaView>();
            _arenas = new ArenaPool(_container, new[] { dunes }, _room.transform, _events.Hub, Player);
            Session = new RunSession(catalog, random, _events, _intents,
                new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), _events), 64, 28, 64);
            Session.Start(new RunConfig(Sunscar, Ranger, random.Seed, 15, SpawnPlan.Empty, restore));
            ArrivalLevel = Session.State.Level;
            ArrivalPoints = Session.State.SkillPoints;
            _navigation = NavMesh.AddNavMeshData(Arena.Surface.navMeshData, Arena.Surface.transform.position,
                Arena.Surface.transform.rotation);
            Physics.SyncTransforms();
        }

        public RunSession Session { get; }
        public ArenaView Arena => _arenas.Active;
        public PlayerView Player { get; }
        public float PlayerRadius { get; }
        public MovementSpec Movement { get; }
        public RecordingEvents Events => _events.Recorded;
        public int ArrivalLevel { get; }
        public int ArrivalPoints { get; }
        public float BaseHp { get; }
        public float BossXp { get; }
        public float SpawnedAt { get; private set; }
        public ContentId? Target { get; set; }
        public bool ReturnToLane { get; set; } = true;

        public void WalkToGate()
        {
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(Player.Position, Arena.GatePosition, NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
            foreach (Vector3 corner in path.corners.Skip(1))
            {
                _waypoint = corner;
                Until(() => Session.State.StageIndex == 16 || Vector3.Distance(Player.Position, corner) < 0.25f, 15f);
                if (Session.State.StageIndex == 16)
                    break;
            }
            _waypoint = null;
        }

        public void Until(Func<bool> condition, float seconds)
        {
            for (int i = 0; i < seconds / Frame && !condition() && Events.Count<PlayerDied>() == 0; i++)
                Tick();
            Assert.That(condition(), Is.True, $"Timed out at {Session.State.Time:F2} s, Ranger HP {Session.State.PlayerHp:F2} " +
                $"at {Player.Position}, {Events.Count<EnemyDamaged>()} enemy hits and {Events.Count<PlayerDamaged>()} player hits.");
        }

        private void Tick()
        {
            _world.Clear();
            _world.Dt = Frame;
            _world.PlayerPosition = Player.Position.ToNum();
            _world.PlayerVelocity = Player.Velocity.ToNum();
            _world.MoveInput = Walk(_world.PlayerPosition);
            _world.HasGate = Arena.HasGate;
            _world.GatePosition = Arena.GatePosition.ToNum();
            _world.SpawnPoints = _arenas.SpawnPoints;
            foreach (var (id, enemy) in _alive)
            {
                // This is the open firing lane probe. Isolate a target without changing weapon damage or cadence.
                bool covered = Target.HasValue && Target.Value != enemy.Spec;
                _world.AddEnemy() = new EnemySense
                {
                    Id = id,
                    Position = enemy.At,
                    Velocity = enemy.Velocity,
                    HasLineOfSight = !covered,
                    BehindCover = covered,
                };
            }

            _intents.Clear();
            Session.Tick(_world);
            if (_intents.HasPlayerMove)
                Player.Apply(_intents.PlayerMove, Frame);
            for (; _read < Events.All.Count; _read++)
            {
                switch (Events.All[_read])
                {
                    case EnemySpawned spawned:
                        _alive.Add(spawned.Id, (spawned.SpecId, spawned.Position, Position.Zero));
                        if (spawned.SpecId == Hierophant)
                            SpawnedAt = Session.State.Time;
                        break;
                    case EnemyDied died:
                        _alive.Remove(died.Id);
                        break;
                    case EnemyDespawned gone:
                        _alive.Remove(gone.Id);
                        break;
                    case HierophantArmed armed:
                        _warning = armed;
                        break;
                    case HierophantImpact:
                    case BossBeatStarted:
                        _warning = null;
                        break;
                }
            }
            foreach (EnemyMoveIntent move in _intents.EnemyMoves)
            {
                if (_alive.TryGetValue(move.Id, out var enemy))
                    _alive[move.Id] = (enemy.Spec, enemy.At + move.Velocity * Frame, move.Velocity);
            }
        }

        private Direction Walk(Position player)
        {
            if (_waypoint is Vector3 waypoint)
            {
                var route = new Direction(waypoint.x - player.X, waypoint.z - player.Z);
                return route.LengthSquared() > 0.01f ? Direction.Normalize(route) : Direction.Zero;
            }

            if (_warning is not HierophantArmed armed)
            {
                // Return between tells so successive sweep retreats cannot pin the probe against the arena boundary.
                var towardLane = new Direction(2.5f - player.X, -5f - player.Z);
                return ReturnToLane && SpawnedAt > 0f && Session.State.StageIndex == 15
                    && Events.Count<StageCleared>() == 0 && towardLane.LengthSquared() > 0.09f
                        ? Direction.Normalize(towardLane) : Direction.Zero;
            }

            var offset = new Direction(player.X - armed.Origin.X, player.Z - armed.Origin.Z);
            if (armed.Move == HierophantMove.SunSeal)
            {
                return MathF.Abs(offset.Y) <= armed.Reach && MathF.Abs(offset.X) < armed.HalfWidth + PlayerRadius
                    ? new Direction(offset.X < 0f ? -1f : 1f, 0f) : Direction.Zero;
            }

            return offset.Length() < armed.Reach + 0.15f ? Direction.Normalize(offset) : Direction.Zero;
        }

        public void Dispose()
        {
            Session.End();
            _navigation.Remove();
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
}
