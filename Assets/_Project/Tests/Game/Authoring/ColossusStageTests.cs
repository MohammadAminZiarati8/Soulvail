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
using Object = UnityEngine.Object;
using Position = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Authoring;

[TestFixture]
public sealed class ColossusStageTests
{
    private const string Root = "Assets/_Project/";
    private const int Seed = 12;
    private const int Stage = 20;
    private const float Frame = 1f / 60f;
    private static readonly ContentId Sunscar = new("mode.sunscar");
    private static readonly ContentId Dunes = new("arena.sunscar.dunes");
    private static readonly ContentId Ranger = new("character.ranger");
    private static readonly ContentId Boss = new("boss.sundial_colossus");
    private static readonly ContentId Colossus = new("enemy.sundial_colossus");
    private static readonly ContentId Slinger = new("enemy.jackal");

    [Test]
    public void Schedule_RegistersStage20Only()
    {
        ContentCatalog catalog = Catalog(out var looks);
        BossSpec boss = catalog.Boss(Boss);
        Assert.That((boss.EnemySpecId, boss.SpawnsCentral, boss.UsesAuthoredMaxHp),
            Is.EqualTo((Colossus, true, true)));
        EnemySpec body = catalog.Enemy(Colossus);
        Assert.That(body.MaxHp, Is.EqualTo(2800f));
        Assert.That(body.Behaviour, Is.EqualTo(EnemyBehaviourKind.Boss));
        Assert.That(looks[Colossus].Body.GetComponent<ColossusView>(), Is.Not.Null);
        Assert.That(body.NameKey.Key, Is.EqualTo("enemy.sundial_colossus.name"));
        Assert.That(Load<LocalizationTable>("Data/Localisation/English.asset").ToDictionary()[body.NameKey],
            Is.EqualTo("Sundial Colossus"));
        Assert.That(Load<LocalizationTable>("Data/Localisation/Pseudo.asset").ToDictionary()[body.NameKey], Is.Not.Empty);

        ModeSpec sunscar = catalog.Mode(Sunscar);
        Assert.That(body.XpValue, Is.LessThan(sunscar.Xp.ToReach(30)));
        AssertSchedule(sunscar, (5, "boss.duneclaw"), (10, "boss.shardback"), (15, "boss.hierophant"),
            (Stage, Boss.Value));
        AssertSchedule(catalog.Mode(new ContentId("mode.jungle")), (5, "boss.croaker"),
            (10, "boss.mother_pitcher"), (15, "boss.mossguard_warlord"), (20, "boss.thornheart"),
            (25, "boss.gravemaw"), (30, "boss.apex"));
        Assert.That(sunscar.ArenaFor(Stage, Seed), Is.EqualTo(Dunes));
        foreach (int stage in new[] { 19, 21 })
        {
            Assert.That(sunscar.TryGetScriptedStage(stage, out var script), Is.True);
            Assert.That(script.WaveCount, Is.EqualTo(4));
            Assert.That(script.Spawns.Sum(spawn => spawn.Count), Is.EqualTo(26));
        }
    }

    [Test]
    public void Beats_SummonOnceAndClearAdds()
    {
        using (var fight = new Fight())
        {
            fight.OnlyTarget = Colossus;
            fight.Until(() => fight.Events.Count<BossBeatStarted>() == 1, 75f);
            Assert.That(fight.LastBossHit.HpFraction, Is.InRange(0.33f, 0.66f));
            EnemySpawned[] adds = fight.Events.Of<EnemySpawned>().Where(e => e.SpecId == Slinger).ToArray();
            Assert.That(adds.Length, Is.EqualTo(2));
            int hits = fight.Events.Of<EnemyDamaged>().Count(e => e.Id == fight.BossId && e.Amount > 0f);
            fight.FinishBeat(1);
            Assert.That(fight.Events.Of<EnemyDamaged>().Count(e => e.Id == fight.BossId && e.Amount > 0f),
                Is.EqualTo(hits));

            float xp = fight.Session.State.Xp;
            fight.OnlyTarget = Slinger;
            fight.Until(() => fight.Events.Of<EnemyDied>().Any(e => e.SpecId == Slinger), 30f);
            Assert.That(fight.Session.State.Xp, Is.EqualTo(xp));
            Assert.That(fight.Session.State.Level, Is.EqualTo(fight.ArrivalLevel));
            int survivor = adds.Single(e => fight.Events.Of<EnemyDied>().All(d => d.Id != e.Id)).Id;

            fight.OnlyTarget = Colossus;
            fight.Until(() => fight.Events.Count<BossBeatStarted>() == 2, 90f);
            Assert.That(fight.LastBossHit.HpFraction, Is.LessThanOrEqualTo(0.33f));
            Assert.That(fight.Events.Of<EnemyDespawned>().Any(e => e.Id == survivor), Is.True);
            fight.FinishBeat(2);
            fight.Until(() => fight.Events.Of<EnemyDamaged>().Count(e => e.Id == fight.BossId && e.Amount > 0f)
                > hits + 1, 15f);
            Assert.That(fight.Events.Of<EnemySpawned>().Count(e => e.SpecId == Slinger), Is.EqualTo(2));
        }

        using (var fight = new Fight(firstHitFraction: 0.75f))
        {
            fight.OnlyTarget = Colossus;
            fight.Until(() => fight.Events.Count<BossBeatStarted>() == 1, 15f);
            Assert.That(fight.Events.Of<BossPhaseChanged>().Select(e => e.Phase), Is.EqualTo(new[] { 0, 2 }));
            Assert.That(fight.Events.Of<EnemySpawned>().Select(e => e.SpecId), Is.EqualTo(new[] { Colossus }));
            fight.FinishBeat(1);
        }

        using (var fight = new Fight(firstHitFraction: 0.55f))
        {
            fight.OnlyTarget = Colossus;
            fight.Until(() => fight.Events.Count<StageCleared>() == 1, 25f);
            Assert.That(fight.Events.Of<BossPhaseChanged>().Select(e => e.Phase), Is.EqualTo(new[] { 0, 1 }));
            var adds = fight.Events.Of<EnemySpawned>().Where(e => e.SpecId == Slinger).Select(e => e.Id).ToArray();
            Assert.That(adds.Length, Is.EqualTo(2));
            Assert.That(fight.Events.Of<EnemyDied>().Skip(1).Select(e => e.Id), Is.EquivalentTo(adds));
            Assert.That(adds.All(id => !fight.Session.State.IsEnemyAlive(id)), Is.True);
            Assert.That(fight.Events.Single<XpChanged>().Gained, Is.EqualTo(fight.BossXp));
        }
    }

    [Test]
    public void RangerProbe_MeetsPacingAndOpensStage21()
    {
        using var fight = new Fight();
        Assert.That(fight.Session.State.SkillPoints, Is.EqualTo(fight.ArrivalLevel - 1));
        Assert.That(fight.Session.State.TakenNodeCount, Is.Zero);
        fight.Until(() => fight.BossId != 0, 10f);
        var spawn = fight.Events.Single<EnemySpawned>();
        Assert.That((spawn.SpecId, spawn.Position), Is.EqualTo((Colossus, new Position(0f, 0f, 2f))));
        fight.Until(() => fight.Events.Count<StageCleared>() == 1, 245f);
        EnemyDamaged firstHit = fight.Events.Of<EnemyDamaged>().First(hit => hit.Id == spawn.Id && hit.Amount > 0f);
        Assert.That(firstHit.Amount / (1f - firstHit.HpFraction), Is.EqualTo(fight.BossHp).Within(0.1f));
        float elapsed = fight.KilledAt - fight.SpawnedAt;
        TestContext.WriteLine($"Seed {Seed}, Ranger L{fight.ArrivalLevel}, {fight.BossHp:F0} HP, " +
            $"first bow hit {fight.FirstHitAt - fight.SpawnedAt:F2} s, spawn to kill {elapsed:F2} s, " +
            $"Ranger L{fight.Session.State.Level} after kill.");
        var breaks = fight.Events.Of<ColossusPlateHit>().Where(hit => hit.RemainingFraction == 0f).ToArray();
        TestContext.WriteLine($"Dodge-only probe broke {breaks.Length} plates.");
        Assert.That(elapsed, Is.InRange(175f, 230f));
        Assert.That(fight.Events.Of<ColossusArmed>().Count(attack => attack.Move == ColossusMove.SundialRay),
            Is.GreaterThan(1));
        Assert.That(fight.Events.Count<BossBeatEnded>(), Is.EqualTo(2));
        Assert.That(fight.Session.State.Level - fight.ArrivalLevel, Is.InRange(0, 1));
        Assert.That(fight.Events.Single<XpChanged>().Gained, Is.EqualTo(fight.BossXp));
        StageCleared clear = fight.Events.Single<StageCleared>();
        Assert.That((clear.Stage, clear.NextArenaId), Is.EqualTo((Stage, Dunes)));
        Assert.That(fight.Arena.GatePosition.z, Is.GreaterThan(fight.Arena.PlayerStart.z));
        int spawnCount = fight.Events.Count<EnemySpawned>();
        fight.WalkToGate();
        fight.Until(() => fight.Events.Count<EnemySpawned>() > spawnCount, 15f);
        Assert.That(fight.Events.Of<StageArrived>().Select(e => (e.Stage, e.ArenaId)),
            Is.EqualTo(new[] { (Stage, Dunes), (Stage + 1, Dunes) }));
        WaveStarted wave = fight.Events.Of<WaveStarted>().First(e => e.Stage == Stage + 1);
        Assert.That((wave.Wave, wave.WaveCount), Is.EqualTo((1, 4)));
        Assert.That(fight.Events.Of<EnemySpawned>().Skip(spawnCount).Select(e => e.SpecId),
            Is.SubsetOf(new[] { "enemy.hooktail-scorpion", "enemy.beetle", "enemy.viper", "enemy.antlion" }
                .Select(id => new ContentId(id))));
    }

    [TestCase(ColossusShoulder.Left)]
    [TestCase(ColossusShoulder.Right)]
    public void RangerAutoFire_BreaksTheFlankedShoulder(ColossusShoulder shoulder)
    {
        using var fight = new Fight { Flank = shoulder };
        fight.Until(() => fight.Events.Of<ColossusPlateHit>()
            .Any(hit => hit.Shoulder == shoulder && hit.RemainingFraction == 0f), 80f);
        TestContext.WriteLine($"{shoulder} shoulder broken at {fight.Session.State.Time - fight.SpawnedAt:F2} s after spawn.");
        Assert.That(fight.Events.Of<ProjectileFired>().Any(shot => shot.SourceId == 0 && shot.ShotOrigin == ShotOrigin.Ranger
            && Fight.GroundDistanceSquared(shot.Target.ToUnity(), fight.BossPosition) < 1f), Is.True);
    }

    [Test]
    public void SundialRay_AllowsASidestepAtNormalRangerWalkSpeed()
    {
        using var fight = new Fight();
        fight.Fire = false;
        fight.AutomaticMovement = false;
        fight.Until(() => fight.BossId != 0, 10f);
        fight.Place(new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, -8f));
        fight.Until(() => fight.Warning?.Move == ColossusMove.SundialRay, 5f);
        fight.WalkOut(fight.Warning.Value, "Sundial Ray");
        Assert.That(fight.Events.Count<PlayerDamaged>(), Is.Zero);
    }

    [Test]
    public void Warnings_AllowWalkSpeedEscapeInDunes()
    {
        var samples = new[]
        {
            (Name: "open", Boss: new Vector3(0f, 0f, 2f), Player: new Vector3(0f, 0f, -0.6f)),
            (Name: "wall", Boss: new Vector3(14.65f, 0f, 0f), Player: new Vector3(17.25f, 0f, 0f)),
            (Name: "cover", Boss: new Vector3(2.6f, 0f, -2.7f), Player: new Vector3(5.2f, 0f, -2.7f)),
        };
        foreach (var sample in samples)
        {
            using var fight = new Fight(firstHitFraction: 0.75f);
            fight.OnlyTarget = Colossus;
            fight.Until(() => fight.Events.Count<BossBeatStarted>() == 1, 15f);
            fight.Fire = false;
            fight.AutomaticMovement = false;
            fight.Place(sample.Boss, sample.Player);
            fight.FinishBeat(1);
            ColossusArmed forward = fight.Warning.Value;
            Assert.That(forward.Move, Is.EqualTo(ColossusMove.Sweep), sample.Name);
            fight.WalkOut(forward, sample.Name);
            fight.Until(() => fight.Warning?.Move == ColossusMove.ReverseSweep, 2f);
            ColossusArmed reverse = fight.Warning.Value;
            Assert.That((reverse.Origin, reverse.Facing), Is.EqualTo((forward.Origin, forward.Facing)), sample.Name);
            // Re-enter the marked arc during the fresh tell, then walk clear again.
            fight.WalkInto(reverse, sample.Name);
            fight.WalkOut(reverse, sample.Name);
            int impacts = fight.Events.Count<ColossusImpact>();
            Assert.That(fight.Events.Of<ColossusImpact>().Skip(impacts - 6).Select(e => (e.Move, e.Sector)),
                Is.EqualTo(new[]
                {
                    (ColossusMove.Sweep, 0), (ColossusMove.Sweep, 1), (ColossusMove.Sweep, 2),
                    (ColossusMove.ReverseSweep, 2), (ColossusMove.ReverseSweep, 1), (ColossusMove.ReverseSweep, 0),
                }), sample.Name);
            fight.Waypoint = sample.Player;
            fight.Until(() => fight.Warning?.Move == ColossusMove.Stomp, 4f);
            fight.WalkOut(fight.Warning.Value, sample.Name);
            Assert.That(fight.Events.Count<PlayerDamaged>(), Is.Zero, sample.Name);
        }
    }

    private static void AssertSchedule(ModeSpec mode, params (int Stage, string Id)[] expected)
    {
        var actual = new List<(int, string)>();
        for (int stage = 1; stage <= expected[^1].Stage; stage++)
        {
            if (mode.TryGetBossFor(stage, out ContentId boss))
            {
                actual.Add((stage, boss.Value));
            }
        }
        Assert.That(actual, Is.EqualTo(expected));
    }

    private static ContentCatalog Catalog(out Dictionary<ContentId, EnemyLook> looks, float firstHitFraction = 0f)
    {
        var boot = new SerializedObject(Load<GameObject>("Prefabs/Composition/BootScope.prefab").GetComponent<BootScope>());
        var definitions = Definitions<EnemyDefinition>(boot, "_enemies");
        looks = definitions.ToDictionary(d => d.ToSpec().Id, d => d.ToLook());
        EnemySpec[] enemies = definitions.Select(d => d.ToSpec()).ToArray();
        ModeSpec[] modes = Definitions<ModeDefinition>(boot, "_modes").Select(d => d.ToSpec()).ToArray();
        var ranger = Load<CharacterDefinition>("Data/Characters/Ranger.asset");
        CharacterSpec character = ranger.ToSpec();
        if (firstHitFraction > 0f)
        {
            // Use ordinary projectile resolution with controlled hit sizes for skips and lethal crossings.
            var copy = Object.Instantiate(ranger);
            try
            {
                var serialized = new SerializedObject(copy);
                serialized.FindProperty("_weaponDamage").floatValue =
                    enemies.Single(e => e.Id == Colossus).MaxHp * firstHitFraction
                    / (1.5f * (1f - new ColossusStats().PlateDamageReduction.Value));
                serialized.ApplyModifiedPropertiesWithoutUndo();
                character = copy.ToSpec();
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }
        return new ContentCatalog(new[] { character }, enemies, modes,
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
        private readonly Dictionary<int, (ContentId Spec, EnemyView View)> _alive = new();
        private readonly Dictionary<int, ProjectileFired> _slingerShots = new();
        private readonly GameObject _room = new("Colossus stage probe");
        private readonly IObjectResolver _container = new ContainerBuilder().Build();
        private readonly ArenaPool _arenas;
        private readonly NavMeshDataInstance _navigation;
        private readonly Dictionary<ContentId, EnemyLook> _looks;
        private readonly PlayerView _player;
        private readonly CharacterController _controller;
        private readonly Func<Position, Position, Position?> _traceCover;
        private readonly int _cover = LayerMask.GetMask("Cover");
        private readonly int _solid = LayerMask.GetMask("Cover", "Default");
        private Vector3? _escape;
        private float _beatAt;
        private int _read;
        private ColossusView _colossusView;
        private bool _guardedHit;

        public Fight(float firstHitFraction = 0f)
        {
            ContentCatalog catalog = Catalog(out _looks, firstHitFraction);
            var random = new SeededRandom(Seed);
            RunSnapshot restore = new TestStart(catalog).At(Sunscar, Ranger, Seed, random.Capture(), Stage);
            EnemySpec boss = catalog.Enemy(Colossus);
            BossHp = boss.MaxHp;
            ModeSpec mode = catalog.Mode(Sunscar);
            BossXp = mode.CampaignXp.BossFraction * mode.Xp.ToReach(restore.Level + 1);
            _player = Object.Instantiate(Load<GameObject>("Prefabs/Player/Player_Ranger.prefab"), _room.transform)
                .GetComponent<PlayerView>();
            _controller = _player.GetComponent<CharacterController>();
            var dunes = Load<GameObject>("Prefabs/Arenas/Sunscar/Arena_Dunes.prefab").GetComponent<ArenaView>();
            _arenas = new ArenaPool(_container, new[] { dunes }, _room.transform, _events.Hub, _player);
            _traceCover = TraceCover;
            Session = new RunSession(catalog, random, _events, _intents,
                new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), _events), 64, 28, 64);
            Session.Start(new RunConfig(Sunscar, Ranger, Seed, Stage, SpawnPlan.Empty, restore));
            ArrivalLevel = Session.State.Level;
            _navigation = NavMesh.AddNavMeshData(Arena.Surface.navMeshData, Arena.Surface.transform.position,
                Arena.Surface.transform.rotation);
            Physics.SyncTransforms();
        }

        public RunSession Session { get; }
        public ArenaView Arena => _arenas.Active;
        public RecordingEvents Events => _events.Recorded;
        public int ArrivalLevel { get; }
        public float BossHp { get; }
        public float BossXp { get; }
        public int BossId { get; private set; }
        public Vector3 BossPosition => _alive[BossId].View.Position;
        public float SpawnedAt { get; private set; }
        public float FirstHitAt { get; private set; }
        public float KilledAt { get; private set; }
        public EnemyDamaged LastBossHit => Events.Of<EnemyDamaged>().Last(e => e.Id == BossId && e.Amount > 0f);
        public ColossusArmed? Warning { get; private set; }
        public ContentId? OnlyTarget { get; set; }
        public bool Fire { get; set; } = true;
        public bool AutomaticMovement { get; set; } = true;
        public Vector3? Waypoint { get; set; }
        public ColossusShoulder? Flank { get; set; }

        public void FinishBeat(int number)
        {
            Assert.That(Events.Of<BossBeatStarted>()[number - 1].Seconds, Is.EqualTo(3f));
            float began = _beatAt;
            Until(() => Events.Count<BossBeatEnded>() == number, 3.1f);
            Assert.That(Session.State.Time - began, Is.EqualTo(3f).Within(Frame * 1.1f));
        }

        public void Place(Vector3 boss, Vector3 player)
        {
            _alive[BossId].View.Bind(BossId, boss);
            _player.Teleport(player);
            Physics.SyncTransforms();
        }

        public void WalkOut(ColossusArmed attack, string sample)
        {
            int impacts = Events.Count<ColossusImpact>();
            int strikes = attack.Move is ColossusMove.Stomp or ColossusMove.SundialRay ? 1 : 3;
            Assert.That(IsClear(attack, _player.Position, 0f), Is.False, sample);
            Waypoint = FindExit(attack, _player.Position);
            Assert.That(Waypoint.HasValue, Is.True, $"{sample} {attack.Move} has no clear walking route.");
            Until(() => Events.Count<ColossusImpact>() > impacts, attack.Seconds + Frame * 2f);
            Assert.That(IsClear(attack, _world.PlayerPosition.ToUnity(), _controller.radius), Is.True,
                $"{sample} {attack.Move} at {_world.PlayerPosition}, exit {Waypoint}");
            Until(() => Events.Count<ColossusImpact>() == impacts + strikes, 1f);
            Waypoint = null;
        }

        public void WalkInto(ColossusArmed attack, string sample)
        {
            // Coming straight toward the boss cannot cross an angular edge. Aim toward the
            // centre line at the same range to step just inside the side of the arc.
            var facing = new Vector3(attack.Facing.X, 0f, attack.Facing.Y);
            float range = Vector3.Distance(attack.Origin.ToUnity(), _player.Position);
            Waypoint = attack.Origin.ToUnity() + facing * Mathf.Min(range, attack.Radius - _controller.radius);
            Until(() => !IsClear(attack, _player.Position, 0f), attack.Seconds / 2f);
            Assert.That(Events.Of<ColossusImpact>().Last().Move, Is.EqualTo(ColossusMove.Sweep), sample);
        }

        public void WalkToGate()
        {
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(_player.Position, Arena.GatePosition, NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
            Fire = false;
            foreach (Vector3 corner in path.corners.Skip(1))
            {
                Waypoint = corner;
                Until(() => Session.State.StageIndex == Stage + 1 || GroundDistanceSquared(_player.Position, corner) < 0.15f * 0.15f,
                    15f);
                if (Session.State.StageIndex == Stage + 1)
                {
                    break;
                }
            }
            // Reaching the last corner can precede the end of the portal fade.
            Waypoint = Arena.GatePosition;
            Until(() => Session.State.StageIndex == Stage + 1, 5f);
            Waypoint = null;
        }

        public void Until(Func<bool> condition, float seconds)
        {
            for (int i = 0; i < Mathf.CeilToInt(seconds / Frame) && !condition() && Events.Count<PlayerDied>() == 0; i++)
            {
                Tick();
            }
            Assert.That(condition(), Is.True, $"At {Session.State.Time:F2} s, Ranger HP {Session.State.PlayerHp:F2}, " +
                $"player {_player.Position}, boss HP fraction {(FirstHitAt > 0f ? LastBossHit.HpFraction : 1f):F2}, " +
                $"warning {Warning?.Move}, hits {Events.Count<EnemyDamaged>()}, player hits {Events.Count<PlayerDamaged>()}.");
        }

        private void Tick()
        {
            _world.Clear();
            _world.Dt = Frame;
            _world.PlayerPosition = _player.Position.ToNum();
            _world.PlayerVelocity = _player.Velocity.ToNum();
            _world.MoveInput = Move().ToNumXZ();
            _world.HasGate = Arena.HasGate;
            _world.GatePosition = Arena.GatePosition.ToNum();
            _world.SpawnPoints = _arenas.SpawnPoints;
            _world.TraceCover = _traceCover;
            foreach (var (id, enemy) in _alive)
            {
                Position at = enemy.View.Position.ToNum();
                bool blocked = TraceCover(at, _world.PlayerPosition).HasValue;
                _world.AddEnemy() = new EnemySense
                {
                    Id = id,
                    Position = at,
                    Velocity = enemy.View.Velocity.ToNum(),
                    ShoulderAxis = id == BossId && _colossusView != null ? _colossusView.ShoulderAxis : default,
                    HasLineOfSight = !blocked,
                    BehindCover = blocked || !Fire || (OnlyTarget.HasValue && OnlyTarget.Value != enemy.Spec),
                };
            }
            _intents.Clear();
            Session.Tick(_world);
            if (_intents.HasPlayerMove)
            {
                _player.Apply(_intents.PlayerMove, Frame);
            }
            for (; _read < Events.All.Count; _read++)
            {
                ReadEvent(Events.All[_read]);
            }
            foreach (EnemyMoveIntent move in _intents.EnemyMoves)
            {
                if (_alive.TryGetValue(move.Id, out var enemy))
                {
                    enemy.View.Apply(move, Frame);
                }
            }
            _colossusView?.Step(Frame);
            Physics.SyncTransforms();
        }

        private void ReadEvent(object evt)
        {
            switch (evt)
            {
                case EnemySpawned spawn:
                    EnemyLook look = _looks[spawn.SpecId];
                    EnemyView view = Object.Instantiate(look.Body, _room.transform);
                    view.transform.localScale *= look.BodyScale;
                    view.Bind(spawn.Id, spawn.Position.ToUnity());
                    _alive.Add(spawn.Id, (spawn.SpecId, view));
                    if (spawn.SpecId == Colossus)
                    {
                        BossId = spawn.Id;
                        SpawnedAt = Session.State.Time;
                        _colossusView = view.GetComponent<ColossusView>();
                        _colossusView.Construct(_events.Hub);
                    }
                    break;
                case EnemyDespawned gone:
                    if (_alive.Remove(gone.Id, out var enemy))
                    {
                        Object.DestroyImmediate(enemy.View.gameObject);
                    }
                    break;
                case EnemyDamaged hit when hit.Id == BossId && hit.Amount > 0f && FirstHitAt == 0f:
                    FirstHitAt = Session.State.Time;
                    break;
                case ColossusPlateHit hit when hit.RemainingFraction == 1f:
                    _guardedHit = true;
                    break;
                case EnemyDied died when died.Id == BossId:
                    KilledAt = Session.State.Time;
                    break;
                case ProjectileFired shot when shot.SourceId != 0 && shot.SpecId == Slinger:
                    _slingerShots.Add(shot.Id, shot);
                    break;
                case ProjectileImpacted impact:
                    _slingerShots.Remove(impact.Id);
                    break;
                case ColossusArmed armed:
                    Warning = armed;
                    _escape = null;
                    break;
                case ColossusImpact impact when impact.Sector == (impact.Move == ColossusMove.Sweep ? 2 :
                    impact.Move == ColossusMove.ReverseSweep ? 0 : -1):
                    Warning = null;
                    _escape = null;
                    break;
                case BossBeatStarted:
                    _beatAt = Session.State.Time;
                    Warning = null;
                    _escape = null;
                    break;
            }
        }

        private Vector3 Move()
        {
            Vector3 position = _player.Position;
            if (Waypoint is Vector3 waypoint)
            {
                return Toward(position, waypoint);
            }
            if (!AutomaticMovement || !_alive.TryGetValue(BossId, out var boss))
            {
                return Vector3.zero;
            }
            if (Flank is ColossusShoulder shoulder)
            {
                return MoveToShoulder(position, boss.View.Position, shoulder);
            }
            if (Warning is ColossusArmed armed)
            {
                if (!IsClear(armed, position, _controller.radius))
                {
                    _escape ??= FindExit(armed, position);
                    if (_escape is Vector3 exit)
                    {
                        return Toward(position, exit);
                    }
                }
            }

            foreach (ProjectileFired shot in _slingerShots.Values)
            {
                Vector3 target = shot.Target.ToUnity();
                float clearance = shot.Radius + _controller.radius;
                if (GroundDistanceSquared(position, target) < clearance * clearance)
                {
                    Vector3? dodge = FindClearStep(position, position - target,
                        end => GroundDistanceSquared(end, target) > (clearance + 0.16f) * (clearance + 0.16f)
                            && (Warning is not ColossusArmed tell || IsClear(tell, end, _controller.radius)));
                    if (dodge.HasValue)
                    {
                        return Toward(position, dodge.Value);
                    }
                }
            }
            if (Warning.HasValue)
            {
                return Vector3.zero;
            }
            Vector3 offset = boss.View.Position - position;
            offset.y = 0f;
            if (offset.sqrMagnitude > 11.3f * 11.3f)
            {
                return offset.normalized;
            }
            return Vector3.zero;
        }

        private Vector3 MoveToShoulder(Vector3 position, Vector3 boss, ColossusShoulder shoulder)
        {
            if (!_guardedHit)
            {
                if (GroundDistanceSquared(position, boss) > 8.8f * 8.8f)
                {
                    return Toward(position, boss);
                }
                if (Warning is ColossusArmed ray && ray.Move == ColossusMove.SundialRay
                    && !IsClear(ray, position, _controller.radius))
                {
                    return new Vector3(ray.Facing.Y, 0f, -ray.Facing.X);
                }
                return Vector3.zero;
            }
            if (Warning is ColossusArmed attack)
            {
                Vector3 forward = new(attack.Facing.X, 0f, attack.Facing.Y);
                float angle = shoulder == ColossusShoulder.Right ? 61f : -61f;
                Vector3 destination = attack.Origin.ToUnity() + Quaternion.Euler(0f, angle, 0f) * forward * 4.7f;
                return GroundDistanceSquared(position, destination) > 0.11f
                    ? Toward(position, destination) : Vector3.zero;
            }
            return GroundDistanceSquared(position, boss) > 25f ? Toward(position, boss) : Vector3.zero;
        }

        private static Vector3 Toward(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            return delta.sqrMagnitude > 0.0036f ? delta.normalized : Vector3.zero;
        }

        private Vector3? FindExit(ColossusArmed attack, Vector3 player)
            => FindClearStep(player, player - attack.Origin.ToUnity(),
                end => IsClear(attack, end, _controller.radius + 0.12f));

        private Vector3? FindClearStep(Vector3 player, Vector3 outward, Func<Vector3, bool> clear)
        {
            float radius = _controller.radius;
            Vector3 bottom = player + _controller.center + Vector3.up * (radius - _controller.height / 2f + 0.06f);
            Vector3 top = bottom + Vector3.up * (_controller.height - 2f * radius);
            outward.y = 0f;
            outward = outward.sqrMagnitude > 0.001f ? outward.normalized : Vector3.forward;
            // Search straight walking exits, checking the full Ranger capsule against walls and cover.
            for (float distance = 0.2f; distance <= 5f; distance += 0.1f)
            {
                for (int angle = 0; angle < 360; angle += 15)
                {
                    Vector3 direction = Quaternion.Euler(0f, angle, 0f) * outward;
                    Vector3 end = player + direction * distance;
                    if (clear(end)
                        && !Physics.CapsuleCast(bottom, top, radius, direction, distance, _solid, QueryTriggerInteraction.Ignore))
                    {
                        return end;
                    }
                }
            }
            return null;
        }

        public static float GroundDistanceSquared(Vector3 a, Vector3 b)
        {
            Vector3 offset = a - b;
            offset.y = 0f;
            return offset.sqrMagnitude;
        }

        private static bool IsClear(ColossusArmed attack, Vector3 player, float padding)
        {
            Vector3 offset = player - attack.Origin.ToUnity();
            offset.y = 0f;
            var facing = new Vector3(attack.Facing.X, 0f, attack.Facing.Y);
            if (attack.Move == ColossusMove.SundialRay)
            {
                float along = Vector3.Dot(offset, facing);
                float across = Mathf.Abs(Vector3.Cross(offset, facing).y);
                return along < attack.LaneStart - padding || along > attack.Radius + padding
                    || across > attack.LaneHalfWidth + padding;
            }
            if (offset.sqrMagnitude > (attack.Radius + padding) * (attack.Radius + padding))
            {
                return true;
            }
            if (attack.Move == ColossusMove.Stomp || offset.sqrMagnitude <= padding * padding)
            {
                return false;
            }
            float span = attack.SectorDegrees * 1.5f;
            float clearance = Mathf.Asin(padding / offset.magnitude) * Mathf.Rad2Deg;
            return Vector3.Angle(facing, offset) > span + clearance;
        }

        private Position? TraceCover(Position from, Position to)
        {
            Vector3 lift = Vector3.up * LineOfSightSense.EyeHeight;
            Vector3 start = from.ToUnity() + lift;
            Vector3 delta = to.ToUnity() + lift - start;
            return Physics.Raycast(start, delta.normalized, out var hit, delta.magnitude, _cover, QueryTriggerInteraction.Ignore)
                ? (hit.point - lift).ToNum() : null;
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
