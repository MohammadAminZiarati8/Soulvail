using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
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
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Authoring;

[TestFixture]
public sealed class MatriarchStageTests
{
    private const string Root = "Assets/_Project/";
    private const int Seed = 12;
    private const int Stage = 25;
    private const float Frame = 1f / 60f;
    private const float ProbeLimit = 600f;
    private const float BossMaxHp = 2080f;
    private static readonly ContentId Sunscar = new("mode.sunscar");
    private static readonly ContentId Dunes = new("arena.sunscar.dunes");
    private static readonly ContentId Matriarch = new("enemy.carrion_matriarch");
    private static readonly ContentId Vulture = new("enemy.vulture");

    [Test]
    public void Schedule_Stage25OpensGateToStage26()
    {
        using var fight = new Fight("Ranger", belowLevelCap: true);
        var bossId = new ContentId("boss.carrion_matriarch");
        BossSpec boss = fight.Catalog.Boss(bossId);
        Assert.That((boss.EnemySpecId, boss.SpawnsCentral, boss.UsesAuthoredMaxHp),
            Is.EqualTo((Matriarch, true, true)));
        Assert.That(fight.Look.Body.GetComponent<MatriarchView>(), Is.Not.Null);
        Assert.That(fight.Look.Projectile, Is.Null);
        var name = fight.Catalog.Enemy(Matriarch).NameKey;
        Assert.That(name.Key, Is.EqualTo("enemy.carrion_matriarch.name"));
        Assert.That(Load<LocalizationTable>("Data/Localisation/English.asset").ToDictionary()[name],
            Is.EqualTo("Carrion Matriarch"));
        Assert.That(Load<LocalizationTable>("Data/Localisation/Pseudo.asset").ToDictionary()[name], Is.Not.Empty);
        Assert.That(fight.Catalog.Mode(Sunscar).TryGetBossFor(Stage, out ContentId scheduled), Is.True);
        Assert.That(scheduled, Is.EqualTo(bossId));
        var jungle = fight.Catalog.Mode(new ContentId("mode.jungle"));
        Assert.That(Enumerable.Range(1, jungle.FinalStage)
            .Select(stage => jungle.TryGetBossFor(stage, out ContentId id) ? id.Value : null)
            .Where(id => id != null), Is.EqualTo(new[] { "boss.croaker", "boss.mother_pitcher",
                "boss.mossguard_warlord", "boss.thornheart", "boss.gravemaw", "boss.apex" }));

        fight.Until(() => fight.Events.Count<EnemySpawned>() > 0, 10f);
        EnemySpawned spawn = fight.Events.Single<EnemySpawned>();
        Assert.That((spawn.SpecId, spawn.Position), Is.EqualTo((Matriarch, new System.Numerics.Vector3(0f, 0f, 2f))));
        fight.Win();
        EnemyDamaged firstHit = fight.Events.Of<EnemyDamaged>().First(hit => hit.Id == spawn.Id && hit.Amount > 0f);
        Assert.That(firstHit.HpFraction, Is.EqualTo(1f - firstHit.Amount / BossMaxHp).Within(0.00001f));
        Assert.That(fight.Events.Of<BossPhaseChanged>().Select(e => e.Phase), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(fight.Events.Count<BossBeatStarted>(), Is.EqualTo(2));
        Assert.That(fight.Events.Count<BossBeatEnded>(), Is.EqualTo(2));
        Assert.That(fight.BeatHealth, Has.Count.EqualTo(2));
        Assert.That(fight.BeatHealth[0], Is.InRange(0.33f, 0.66f));
        Assert.That(fight.BeatHealth[1], Is.InRange(0f, 0.33f));
        Assert.That(fight.Brood, Is.Not.Empty);
        foreach (int id in fight.Brood)
        {
            Assert.That(fight.Session.State.IsEnemyAlive(id), Is.False);
            Assert.That(fight.Events.Of<EnemyDespawned>().Any(e => e.Id == id), Is.True);
        }

        ModeSpec mode = fight.Catalog.Mode(Sunscar);
        Assert.That(fight.Events.Single<XpChanged>().Gained,
            Is.EqualTo(mode.CampaignXp.BossFraction * mode.Xp.ToReach(fight.ArrivalLevel + 1)));
        Assert.That(fight.Session.State.Level - fight.ArrivalLevel, Is.InRange(0, 1));

        StageCleared clear = fight.Events.Single<StageCleared>();
        Assert.That((clear.Stage, clear.NextArenaId, clear.GatePosition), Is.EqualTo((Stage, Dunes, fight.Arena.GatePosition.ToNum())));
        Assert.That(fight.Arena.GatePosition.z, Is.GreaterThan(fight.Arena.PlayerStart.z));
        fight.WalkToGate();
        fight.Until(() => fight.Events.Of<WaveStarted>().Any(e => e.Stage == Stage + 1), 10f);
        Assert.That(fight.Events.Of<StageArrived>().Select(e => (e.Stage, e.ArenaId)),
            Is.EqualTo(new[] { (Stage, Dunes), (Stage + 1, Dunes) }));
        WaveStarted next = fight.Events.Of<WaveStarted>().First(e => e.Stage == Stage + 1);
        Assert.That((next.Wave, next.WaveCount), Is.EqualTo((1, 5)));
    }

    [Test]
    public void Prefab_DoublesVisualScaleWithOriginalCollisionAndWarnings()
    {
        GameObject prefab = Load<GameObject>("Prefabs/Enemies/CarrionMatriarch.prefab");
        Transform root = prefab.transform;
        Assert.That(root.localScale, Is.EqualTo(Vector3.one));
        Assert.That(root.Find("Model").localScale, Is.EqualTo(Vector3.one * 3.3f));
        Assert.That(root.Find("FX_Vulture_Shadow").localScale, Is.EqualTo(Vector3.one * 3.3f));
        Assert.That(root.Find("Locked Footprint").localScale, Is.EqualTo(Vector3.one));
        Assert.That(root.Find("Dive Fill").localScale, Is.EqualTo(Vector3.one));
        var controller = prefab.GetComponent<CharacterController>();
        Assert.That(controller.radius, Is.EqualTo(0.513f));
        Assert.That(controller.height, Is.EqualTo(0.82f));
        var trigger = prefab.GetComponent<CapsuleCollider>();
        Assert.That(trigger.radius, Is.EqualTo(0.6235f).Within(0.00001f));
        Assert.That(trigger.height, Is.EqualTo(0.89f));
    }

    [Test]
    public void Arena_TellsAndBroodHaveWalkableAnswers()
    {
        CharacterSpec slowest = new[] { "Ranger", "Oathbound", "Beastmaster" }
            .Select(name => Load<CharacterDefinition>($"Data/Characters/{name}.asset").ToSpec())
            .OrderBy(spec => spec.Movement.Speed).First();
        using (var fight = new Fight("Ranger"))
        {
            fight.Win();
            fight.ReplayWarnings(slowest.Movement);
        }

        foreach (string name in new[] { "Oathbound", "Beastmaster" })
        {
            using var broodProbe = new Fight(name);
            broodProbe.Until(() => broodProbe.BroodTimes.Count > 0, ProbeLimit);
            Assert.That(broodProbe.Brood.Count, Is.EqualTo(2));
            Assert.That(broodProbe.Brood.All(id => broodProbe.Events.Of<VulturePhaseChanged>()
                .Any(phase => phase.Id == id && phase.Phase == VulturePhase.Grounded)), Is.True);
            Assert.That(broodProbe.Brood.All(id => broodProbe.Events.Of<EnemyDied>().Any(died => died.Id == id)), Is.True);
            TestContext.WriteLine($"{name} L{broodProbe.ArrivalLevel}, entry weapon: first brood in {broodProbe.BroodTimes[0]:F2} s.");
        }
    }

    [Test]
    public void Stage25_ProbeMeasuresFullFight()
    {
        using var fight = new Fight("Ranger");
        Assert.That(fight.Session.State.SkillPoints, Is.EqualTo(fight.ArrivalLevel - 1));
        Assert.That(fight.Session.State.TakenNodeCount, Is.Zero);
        fight.Win();
        float hp = fight.Catalog.Enemy(Matriarch).MaxHp;
        float elapsed = fight.KilledAt - fight.SpawnedAt;
        TestContext.WriteLine($"Seed {Seed}, Ranger L{fight.ArrivalLevel}, base bow, no skill points spent, {hp:F0} boss HP, " +
            $"{fight.BroodTimes.Count} flights, {elapsed:F2} s spawn to kill, " +
            $"brood clears (s): {string.Join(", ", fight.BroodTimes.Select(t => t.ToString("F2")))}.");
        Assert.That(hp, Is.EqualTo(BossMaxHp));
        Assert.That(elapsed, Is.GreaterThan(0f));
        Assert.That(fight.BroodTimes, Is.Not.Empty);
        Assert.That(fight.Events.Count<BossBeatEnded>(), Is.EqualTo(2));
        Assert.That(fight.Events.Of<MatriarchImpact>().Count(e => e.Move == MatriarchMove.CarrionDive),
            Is.EqualTo(fight.BroodTimes.Count));
        Assert.That(fight.Events.Of<EnemyDied>().Count(e => e.SpecId == Vulture), Is.EqualTo(fight.Brood.Count));
        int cap = fight.Catalog.Mode(Sunscar).Xp.MaxLevel;
        Assert.That(fight.ArrivalLevel, Is.EqualTo(cap));
        Assert.That(fight.Events.Count<XpChanged>(), Is.Zero);
        Assert.That(fight.Catalog.Enemy(Matriarch).XpValue,
            Is.LessThanOrEqualTo(fight.Catalog.Mode(Sunscar).Xp.ToReach(cap)));
        Assert.That(fight.Session.State.Level - fight.ArrivalLevel, Is.InRange(0, 1));
    }

    private static T Load<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(Root + path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }

    private static T[] Definitions<T>(SerializedObject boot, string field) where T : ScriptableObject
    {
        SerializedProperty rows = boot.FindProperty(field);
        return Enumerable.Range(0, rows.arraySize).Select(i => (T)rows.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
    }

    private readonly struct Footprint
    {
        public Footprint(Vector3 origin, Vector3 target, float width, float radius, float deadline)
        {
            Origin = origin;
            Target = target;
            Width = width;
            Radius = radius;
            Deadline = deadline;
        }

        public Vector3 Origin { get; }
        public Vector3 Target { get; }
        public float Width { get; }
        public float Radius { get; }
        public float Deadline { get; }

        public bool Covers(Vector3 point, float margin)
        {
            if (Radius > 0f)
            {
                return Ground(point - Target).sqrMagnitude <= (Radius + margin) * (Radius + margin);
            }

            Vector3 lane = Ground(Target - Origin);
            Vector3 offset = Ground(point - Origin);
            float along = Vector3.Dot(lane.normalized, offset);
            return along >= -margin && along <= lane.magnitude + margin
                && Mathf.Abs(Vector3.Cross(lane.normalized, offset).y) <= Width * 0.5f + margin;
        }
    }

    private static Vector3 Ground(Vector3 position) => new(position.x, 0f, position.z);

    private sealed class Fight : IDisposable
    {
        private readonly WorldSnapshot _world = new(64);
        private readonly IntentBuffer _intents = new();
        private readonly FightEvents _events = new();
        private readonly GameObject _room = new("Matriarch stage probe");
        private readonly IObjectResolver _container;
        private readonly ArenaPool _arenas;
        private readonly NavMeshDataInstance _navigation;
        private readonly EnemyViews _enemies;
        private readonly CoverViews _covers;
        private readonly SnapshotBuilder _snapshots;
        private readonly InputAdapter _input = new();
        private readonly PlayerView _player;
        private readonly ConeOverlapQuery _cones;
        private readonly int[] _coneHits = new int[32];
        private readonly CharacterSpec _character;
        private readonly EnemySpec _vulture;
        private readonly OpenGroundSense _openGround;
        private readonly int _cover = LayerMask.GetMask("Cover");
        private readonly Dictionary<int, Footprint> _warnings = new();
        private readonly Dictionary<int, Vector3> _landings = new();
        private readonly Dictionary<int, float> _groundedUntil = new();
        private readonly List<(Vector3 Player, float Time, Footprint[] Warnings)> _overlaps = new();
        private readonly Dictionary<MatriarchMove, MatriarchArmed> _tells = new();
        private readonly List<string> _damage = new();
        private Vector3? _escape;
        private Vector3? _waypoint;
        private bool _walkingToGate;
        private float _flightAt;
        private float _bossHealth = 1f;
        private bool _airborne;
        private int _boss;
        private MatriarchMove _bossMove;
        private int _read;

        public Fight(string name, bool belowLevelCap = false)
        {
            var boot = new SerializedObject(Load<GameObject>("Prefabs/Composition/BootScope.prefab").GetComponent<BootScope>());
            EnemyDefinition[] enemies = Definitions<EnemyDefinition>(boot, "_enemies");
            var looks = new EnemyLookBook(enemies.ToDictionary(d => d.ToSpec().Id, d => d.ToLook()));
            Look = looks.For(Matriarch);
            _character = Load<CharacterDefinition>($"Data/Characters/{name}.asset").ToSpec();
            Catalog = new ContentCatalog(new[] { _character }, enemies.Select(d => d.ToSpec()).ToArray(),
                modes: Definitions<ModeDefinition>(boot, "_modes").Select(d => d.ToSpec()).ToArray(),
                skills: Definitions<SkillDefinition>(boot, "_skills").Select(d => d.ToSpec()).ToArray(),
                trees: new[] { Load<SkillTreeDefinition>($"Data/Trees/{name}.asset").ToSpec() },
                bosses: Definitions<BossDefinition>(boot, "_bosses").Select(d => d.ToSpec()).ToArray());
            _vulture = Catalog.Enemy(Vulture);
            var random = new SeededRandom(Seed);
            RunSnapshot start = new TestStart(Catalog).At(Sunscar, _character.Id, Seed, random.Capture(), Stage);
            Session = new RunSession(Catalog, random, _events, _intents,
                new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), _events), 64, 28, 64);
            var builder = new ContainerBuilder();
            builder.RegisterInstance(_events.Hub);
            builder.RegisterInstance<IRunSession>(Session);
            _container = builder.Build();
            _player = Object.Instantiate(Load<GameObject>("Prefabs/Player/Player_Ranger.prefab"), _room.transform)
                .GetComponent<PlayerView>();
            var dunes = Load<GameObject>("Prefabs/Arenas/Sunscar/Arena_Dunes.prefab").GetComponent<ArenaView>();
            _arenas = new ArenaPool(_container, new[] { dunes }, _room.transform, _events.Hub, _player);
            _enemies = new EnemyViews(_container, Load<GameObject>("Prefabs/Enemies/Enemy.prefab").GetComponent<EnemyView>(),
                _room.transform, _events.Hub, looks);
            _covers = new CoverViews(looks, _events.Hub, _room.transform);
            _openGround = new OpenGroundSense(_cover, _covers);
            var sight = new LineOfSightSense(64, _cover, new PathRefreshBudget(10f, 4));
            _snapshots = new SnapshotBuilder(_player, _input, _enemies, null, null, _arenas, sight, _openGround);
            _cones = new ConeOverlapQuery(_coneHits.Length, LayerMask.GetMask("Enemy"), _enemies);

            if (belowLevelCap)
            {
                // A level-capped DT-01 start discards every grant, so use an uncapped Ranger
                // for the reward assertion, with the same stage, seed and unspent build.
                int level = Catalog.Mode(Sunscar).Xp.MaxLevel - 1;
                start = new RunSnapshot(start.Version, start.ModeId, start.CharacterId, start.Seed, start.StageIndex,
                    start.Random, start.PlayerHp, start.PlayerShield, start.RunTime, start.WrittenAt,
                    level, 0f, level - 1, start.TakenNodeIds, start.ManualSkillIds, start.Economy,
                    start.BanishedNodeIds, start.PactedNodeIds, start.OrdealIds);
            }

            Session.Start(new RunConfig(Sunscar, _character.Id, Seed, Stage, SpawnPlan.Empty, start));
            ArrivalLevel = Session.State.Level;
            if (!NavMesh.SamplePosition(Arena.PlayerStart, out _, 0.3f, NavMesh.AllAreas))
            {
                var surface = Arena.Surface;
                _navigation = NavMesh.AddNavMeshData(surface.navMeshData, surface.transform.position, surface.transform.rotation);
            }
        }

        public ContentCatalog Catalog { get; }
        public EnemyLook Look { get; }
        public RunSession Session { get; }
        public ArenaView Arena => _arenas.Active;
        public RecordingEvents Events => _events.Recorded;
        public int ArrivalLevel { get; }
        public float SpawnedAt { get; private set; }
        public float KilledAt { get; private set; }
        public List<int> Brood { get; } = new();
        public List<float> BroodTimes { get; } = new();
        public List<float> BeatHealth { get; } = new();

        public void Win() => Until(() => Events.Count<StageCleared>() > 0, ProbeLimit);

        public void Until(Func<bool> condition, float seconds)
        {
            for (int i = 0; i < Mathf.CeilToInt(seconds / Frame) && !condition() && Events.Count<PlayerDied>() == 0; i++)
            {
                Step();
            }

            Assert.That(condition(), Is.True, $"At {Session.State.Time:F2} s, {_character.Id} HP {Session.State.PlayerHp:F1}, " +
                $"boss HP fraction {Events.Of<EnemyDamaged>().LastOrDefault(hit => hit.Id == _boss).HpFraction:F2}, " +
                $"position {_player.Position}, {_openGround.Spots.Count} floor samples, brood deaths {Events.Of<EnemyDied>().Count(e => e.SpecId == Vulture)}/{Brood.Count}, " +
                $"brood times [{string.Join(", ", BroodTimes)}], pending landings [{string.Join(", ", _landings.Values)}], " +
                $"damage (time: amount) [{string.Join(", ", _damage)}].");
        }

        public void WalkToGate()
        {
            _walkingToGate = true;
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(_player.Position, Arena.GatePosition, NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
            foreach (Vector3 corner in path.corners.Skip(1))
            {
                _waypoint = corner;
                Until(() => Session.State.StageIndex > Stage || Ground(_player.Position - corner).sqrMagnitude < 0.04f, 12f);
            }

            _waypoint = Arena.GatePosition;
            Until(() => Session.State.StageIndex == Stage + 1, 6f);
            _waypoint = null;
        }

        public void ReplayWarnings(MovementSpec movement)
        {
            Assert.That(_tells.Keys, Is.EquivalentTo(Enum.GetValues(typeof(MatriarchMove))));
            Vector3[] spots = _openGround.Spots.Select(p => p.ToUnity()).ToArray();
            Vector3 open = spots.OrderBy(p => Ground(p - new Vector3(0f, 0f, -6f)).sqrMagnitude).First();
            Vector3 edge = spots.Where(p => p.x > 15f).OrderBy(p => Mathf.Abs(p.z)).First();
            Vector3 cover = spots.Where(p => Physics.CheckSphere(p + Vector3.up, 1.8f, _cover, QueryTriggerInteraction.Ignore))
                .OrderBy(p => Ground(p - open).sqrMagnitude).First();

            foreach (var (place, sample) in new[] { ("open", open), ("cover", cover), ("edge", edge) })
            {
                foreach (var (move, tell) in _tells)
                {
                    // Replay the recorded shape from each position, with the lane running along the
                    // east wall for the boundary case and a centred impact for each circular tell.
                    Vector3 launch = tell.Radius > 0f ? sample : sample - Vector3.forward * 4f;
                    Vector3 end = tell.Radius > 0f ? sample : launch + Vector3.forward * Ground(tell.Target.ToUnity() - tell.Origin.ToUnity()).magnitude;
                    var replay = new Footprint(launch, end, tell.Width, tell.Radius, tell.Seconds);
                    Assert.That(replay.Covers(sample, 0f), Is.True);
                    ReplayEscape(sample, movement, 0f, new[] { replay }, $"{place}: {move}");
                }
            }

            Assert.That(_overlaps, Is.Not.Empty, "Record rain while at least one brood dive lane is armed.");
            int replayed = 0;
            foreach (var (player, time, warnings) in _overlaps)
            {
                if (warnings.Any(warning => warning.Covers(player, 0f)))
                {
                    ReplayEscape(player, movement, time, warnings, $"Rain and brood at {time:F2} s");
                    replayed++;
                }
            }
            Assert.That(replayed, Is.GreaterThan(0));
            Assert.That(Events.Of<VultureDiveLocked>().Select(e => e.Id).Distinct(), Is.EquivalentTo(Brood));
            TestContext.WriteLine($"Replayed {replayed} rain/dive overlaps at {movement.Speed:F1} m/s.");
        }

        private void ReplayEscape(Vector3 start, MovementSpec movement, float issuedAt, Footprint[] warnings, string label)
        {
            Footprint[] impacts = warnings.OrderBy(warning => warning.Deadline).ToArray();
            float seconds = impacts[^1].Deadline - issuedAt;
            Vector3? exit = FindExit(start, WalkBudget(movement.Speed, seconds), (from, to) =>
                impacts.All(warning => !warning.Covers(Vector3.MoveTowards(from, to,
                    WalkBudget(movement.Speed, warning.Deadline - issuedAt)), 0.08f)));
            string deadlines = string.Join(", ", impacts.Select(warning => $"{warning.Deadline - issuedAt:F2} s"));
            Assert.That(exit, Is.Not.Null, $"{label}, {start}, deadlines [{deadlines}]");
            _player.Teleport(start);
            Physics.SyncTransforms();
            var motor = new PlayerMotor(movement, System.Numerics.Vector3.UnitZ);
            // The impact sees the previous movement step's position.
            int impact = 0;
            for (int frame = 1; impact < impacts.Length; frame++)
            {
                while (impact < impacts.Length && frame * Frame >= impacts[impact].Deadline - issuedAt)
                {
                    Assert.That(impacts[impact].Covers(_player.Position, 0f), Is.False,
                        $"{label}, {start} to {_player.Position}, impact at {frame * Frame:F2} s");
                    impact++;
                }

                motor.Tick(Frame, Toward(_player.Position, exit.Value).ToNumXZ(), null);
                _player.Apply(new PlayerMoveIntent(motor.Velocity, motor.Facing), Frame);
            }
        }

        private void Step()
        {
            Physics.SyncTransforms();
            _snapshots.Build(_world, Frame);
            _world.MoveInput = Move().ToNumXZ();
            _intents.Clear();
            Session.Tick(_world);
            if (_intents.HasPlayerMove)
            {
                _player.Apply(_intents.PlayerMove, Frame);
            }
            foreach (EnemyMoveIntent move in _intents.EnemyMoves)
            {
                _enemies.ApplyMove(move, Frame);
            }
            Physics.SyncTransforms();
            foreach (ConeHitIntent cone in _intents.ConeHits)
            {
                int count = _cones.Query(cone, _coneHits);
                Session.ReportConeHits(_coneHits.AsSpan(0, count));
            }
            for (; _read < Events.All.Count; _read++)
            {
                ReadEvent(Events.All[_read]);
            }
        }

        private void ReadEvent(object evt)
        {
            float now = Session.State.Time;
            switch (evt)
            {
                case EnemySpawned spawn when spawn.SpecId == Matriarch:
                    _boss = spawn.Id;
                    SpawnedAt = now;
                    break;
                case EnemySpawned spawn when spawn.SpecId == Vulture:
                    Brood.Add(spawn.Id);
                    break;
                case EnemyDied death when death.Id == _boss:
                    KilledAt = now;
                    break;
                case EnemyDied death when death.SpecId == Vulture:
                    _landings.Remove(death.Id);
                    _groundedUntil.Remove(death.Id);
                    _warnings.Remove(death.Id);
                    if (Brood.All(id => !Session.State.IsEnemyAlive(id)))
                    {
                        BroodTimes.Add(now - _flightAt);
                    }
                    break;
                case MatriarchFlightChanged flight:
                    _airborne = flight.Airborne;
                    if (flight.Airborne)
                    {
                        _flightAt = now;
                    }
                    break;
                case EnemyDamaged hit when hit.Id == _boss && hit.Amount > 0f:
                    _bossHealth = hit.HpFraction;
                    break;
                case BossBeatStarted:
                    BeatHealth.Add(_bossHealth);
                    _warnings.Remove(_boss);
                    break;
                case MatriarchArmed tell:
                    var footprint = new Footprint(tell.Origin.ToUnity(), tell.Target.ToUnity(), tell.Width, tell.Radius, now + tell.Seconds);
                    _bossMove = tell.Move;
                    _tells[tell.Move] = tell;
                    _warnings[_boss] = footprint;
                    if (tell.Move == MatriarchMove.FeatherRain)
                    {
                        RecordOverlap(now);
                    }
                    _escape = null;
                    break;
                case MatriarchTellCleared:
                    _warnings.Remove(_boss);
                    break;
                case PlayerDamaged damage when damage.ToHp > 0f || damage.ToShield > 0f:
                    _damage.Add($"{now:F2}: {damage.ToHp + damage.ToShield:F1}");
                    break;
                case VultureDiveLocked dive:
                    Vector3 toward = new(dive.Direction.X, 0f, dive.Direction.Y);
                    Vector3 origin = dive.Origin.ToUnity();
                    _landings[dive.Id] = dive.Landing.ToUnity();
                    _warnings[dive.Id] = new Footprint(origin, origin + toward * _vulture.Dive.LineLength,
                        _vulture.Dive.LineHalfWidth * 2f, 0f, now + dive.Seconds);
                    if (_bossMove == MatriarchMove.FeatherRain && _warnings.ContainsKey(_boss))
                    {
                        RecordOverlap(now);
                    }
                    _escape = null;
                    break;
                case VulturePhaseChanged phase when phase.Phase == VulturePhase.Grounded:
                    _groundedUntil[phase.Id] = now + phase.Seconds;
                    break;
                case VulturePhaseChanged phase when phase.Phase == VulturePhase.Climb:
                    _landings.Remove(phase.Id);
                    _groundedUntil.Remove(phase.Id);
                    _warnings.Remove(phase.Id);
                    break;
            }
        }

        private void RecordOverlap(float now)
        {
            Footprint[] active = _warnings.Values.Where(warning => warning.Deadline > now).ToArray();
            if (active.Length > 1)
            {
                _overlaps.Add((_player.Position, now, active));
            }
        }

        private Vector3 Move()
        {
            Vector3 at = _player.Position;
            if (_walkingToGate)
            {
                return _waypoint is Vector3 corner ? Toward(at, corner) : Vector3.zero;
            }

            float now = Session.State.Time;
            Footprint[] active = _warnings.Values.Where(warning => warning.Deadline >= now - Frame).ToArray();
            float deadline = active.Where(warning => warning.Covers(at, 0.32f))
                .Select(warning => warning.Deadline).DefaultIfEmpty(float.PositiveInfinity).Min();
            if (float.IsFinite(deadline))
            {
                _escape ??= FindExit(at, WalkBudget(_character.Movement.Speed, deadline - now), active)
                    ?? FindExit(at, 5f, active);
                return _escape is Vector3 exit ? Toward(at, exit) : Vector3.zero;
            }

            _escape = null;
            // Anticipate the landing while the vulture is locked, then let the normal weapon fire
            // from inside its own range. The melee probe uses the authored cone and hit query.
            foreach (var pair in _landings.OrderBy(pair => Ground(pair.Value - at).sqrMagnitude))
            {
                if (_groundedUntil.TryGetValue(pair.Key, out float until) && until > now &&
                    _enemies.TryGet(pair.Key, out EnemyView grounded) && CanAttack(at, grounded.Position))
                {
                    return Vector3.zero;
                }
            }

            Vector3? target = null;
            if (_landings.Count > 0)
            {
                target = _landings.Values.OrderBy(point => Ground(point - at).sqrMagnitude).First();
            }
            else if (!_airborne && _enemies.TryGet(_boss, out EnemyView boss))
            {
                target = boss.Position;
            }

            // Regain the centre between dives instead of letting successive dodges drift into a corner.
            if (_airborne && target == null && Ground(at).sqrMagnitude > 36f)
            {
                return Steer(at, new Vector3(0f, 0f, -2f), active);
            }

            return target is Vector3 destination && !CanAttack(at, destination)
                ? Steer(at, destination, active) : Vector3.zero;
        }

        private bool CanAttack(Vector3 from, Vector3 to) =>
            Ground(to - from).sqrMagnitude <= (_character.Weapon.Range - 0.2f) * (_character.Weapon.Range - 0.2f)
            && !_world.TraceCover(from.ToNum(), to.ToNum()).HasValue;

        private static Vector3 Steer(Vector3 from, Vector3 to, IReadOnlyList<Footprint> warnings)
        {
            var path = new NavMeshPath();
            if (NavMesh.SamplePosition(from, out NavMeshHit start, 1.5f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(to, out NavMeshHit end, 1.5f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) && path.corners.Length > 1)
            {
                Vector3 direction = Toward(from, path.corners[1]);
                if (!warnings.Any(warning => warning.Covers(from + direction * 0.6f, 0.25f)))
                {
                    return direction;
                }
            }

            return Vector3.zero;
        }

        private static Vector3? FindExit(Vector3 from, float budget, IReadOnlyList<Footprint> warnings) =>
            FindExit(from, budget, (_, end) =>
                warnings.All(warning => !warning.Covers(end, warning.Radius > 0f ? 0.2f : 0.4f)));

        private static Vector3? FindExit(Vector3 from, float budget, Func<Vector3, Vector3, bool> clearsWarnings)
        {
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, 1.5f, NavMesh.AllAreas))
            {
                return null;
            }

            for (float distance = 0.4f; distance <= budget; distance += 0.1f)
            {
                for (int i = 0; i < 32; i++)
                {
                    Vector3 end = start.position + Quaternion.Euler(0f, i * (360f / 32f), 0f) * Vector3.forward * distance;
                    if (clearsWarnings(start.position, end)
                        && NavMesh.SamplePosition(end, out _, 0.12f, NavMesh.AllAreas)
                        && !NavMesh.Raycast(start.position, end, out _, NavMesh.AllAreas))
                    {
                        return end;
                    }
                }
            }

            return null;
        }

        private static float WalkBudget(float speed, float seconds) => speed * Mathf.Max(0f, seconds - 0.08f);

        private static Vector3 Toward(Vector3 from, Vector3 to)
        {
            Vector3 offset = Ground(to - from);
            return offset.sqrMagnitude < 0.0025f ? Vector3.zero : offset.normalized;
        }

        public void Dispose()
        {
            _input.Dispose();
            _covers.Dispose();
            _enemies.Dispose();
            _navigation.Remove();
            _arenas.Dispose();
            _container.Dispose();
            Object.DestroyImmediate(_room);
        }
    }

    private sealed class FightEvents : IDomainEvents
    {
        public RecordingEvents Recorded { get; } = new();
        public DomainEventHub Hub { get; } = new();

        public void Publish<T>(in T evt) where T : struct
        {
            Recorded.Publish(evt);
            // EditMode runs the physical adapters without the cosmetics that require Awake.
            if (evt is EnemySpawned or EnemyDespawned or StageArrived or StageCleared)
            {
                Hub.Publish(evt);
            }
        }
    }
}
