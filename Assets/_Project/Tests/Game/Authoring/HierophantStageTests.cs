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
        Assert.That(boss.UsesAuthoredMaxHp, Is.True);
        Assert.That(body.ToSpec().Behaviour, Is.EqualTo(EnemyBehaviourKind.Boss));
        Assert.That(body.ToSpec().MaxHp, Is.EqualTo(2100f));
        Assert.That(body.ToLook().BodyScale, Is.EqualTo(2.2f));
        Assert.That(body.ToLook().Body.GetComponent<HierophantView>(), Is.Not.Null);
        var name = body.ToSpec().NameKey;
        Assert.That(name.Key, Is.EqualTo("enemy.hierophant.name"));
        Assert.That(Load<LocalizationTable>("Data/Localisation/English.asset").ToDictionary()[name],
            Is.EqualTo("Sunscar Hierophant"));
        Assert.That(Load<LocalizationTable>("Data/Localisation/Pseudo.asset").ToDictionary()[name], Is.Not.Empty);

        AssertSchedule(catalog.Mode(Sunscar), (5, "boss.duneclaw"), (10, "boss.shardback"), (15, bossId.Value),
            (20, "boss.sundial_colossus"));
        AssertSchedule(catalog.Mode(new ContentId("mode.jungle")), (5, "boss.croaker"), (10, "boss.mother_pitcher"),
            (15, "boss.mossguard_warlord"), (20, "boss.thornheart"), (25, "boss.gravemaw"), (30, "boss.apex"));
        Assert.That(catalog.Mode(Sunscar).TryGetScriptedStage(14, out _), Is.True);
        Assert.That(catalog.Mode(Sunscar).TryGetScriptedStage(16, out _), Is.True);
    }

    [Test]
    public void Ward_HalvesDamage_PrioritizesPriest_AndBreaksOnHit()
    {
        using var fight = new Fight { Target = Hierophant };
        fight.Until(() => fight.Events.Count<HierophantWard>() > 0, 20f);
        EnemySpawned boss = fight.Events.Of<EnemySpawned>().Single(e => e.SpecId == Hierophant);
        EnemySpawned priest = fight.Events.Of<EnemySpawned>().Single(e => e.SpecId == Priest);
        EnemySpawned slinger = fight.Events.Of<EnemySpawned>().Single(e => e.SpecId == Slinger);
        float full = fight.LastHit(boss.Id).Amount;
        EnemyDamaged first = fight.Events.Of<EnemyDamaged>().First(e => e.Id == boss.Id && e.Amount > 0f);
        Assert.That(first.Amount / (1f - first.HpFraction), Is.EqualTo(2100f).Within(0.1f));
        int before = fight.Events.Count<EnemyDamaged>();
        fight.Until(() => fight.HitAfter(boss.Id, before), 5f);
        Assert.That(fight.LastHit(boss.Id).Amount, Is.EqualTo(full * 0.5f).Within(0.01f));
        fight.Until(() => fight.LastHit(boss.Id).HpFraction <= 0.94f, 30f);

        fight.Target = null;
        fight.Until(() => fight.Events.Of<TargetChanged>().Last().Id == priest.Id, 4f);
        fight.Until(() => fight.Events.Of<HierophantWard>().Any(e => !e.Active), 5f);
        float interruptedAt = fight.Session.State.Time;
        Assert.That(fight.Session.State.IsEnemyAlive(priest.Id), Is.True);
        Assert.That(fight.Events.Of<EnemyDamaged>().Last().Id, Is.EqualTo(priest.Id));
        fight.Target = Hierophant;
        before = fight.Events.Count<EnemyDamaged>();
        fight.Until(() => fight.HitAfter(boss.Id, before), 4f);
        Assert.That(fight.LastHit(boss.Id).Amount, Is.EqualTo(full).Within(0.01f));
        fight.Target = NoTarget;
        fight.Until(() => fight.Session.State.Time >= interruptedAt + 4.9f, 5.1f);
        Assert.That(fight.Events.Of<HierophantWard>().Last().Active, Is.False);
        fight.Until(() => fight.Events.Count<HierophantHealChannel>() > 0, 1f);
        Assert.That(fight.Events.Of<HierophantWard>().Last().Active, Is.False);
        fight.Until(() => fight.Events.Of<HierophantWard>().Last().Active, 2f);
        Assert.That(fight.Events.Of<EnemyHealed>().Single(e => e.EnemyId == boss.Id).Amount,
            Is.EqualTo(105f).Within(0.01f));

        fight.Target = Hierophant;
        fight.Until(() => fight.Events.Count<BossBeatStarted>() == 1, 230f);
        AssertThreshold(fight, boss.Id, 0.66f);
        Assert.That(fight.Events.Of<HierophantWard>().Last().Active, Is.False);
        Assert.That(fight.Session.State.IsEnemyAlive(priest.Id), Is.True);
        Assert.That(fight.Session.State.IsEnemyAlive(slinger.Id), Is.True);
        fight.Until(() => fight.Events.Count<BossBeatEnded>() == 1, 2f);
        Assert.That(fight.Events.Of<HierophantWard>().Last().Active, Is.False);
        Assert.That(fight.Events.Count<EnemySpawned>(), Is.EqualTo(3));
    }

    [Test]
    public void BossDeath_ClearsBothJackalsWithoutTheirXp()
    {
        using var fight = new Fight(useFinisher: true) { Target = NoTarget };
        fight.Until(() => fight.Events.Count<EnemySpawned>() == 3, 20f);
        int[] adds = fight.Events.Of<EnemySpawned>().Where(e => e.SpecId != Hierophant).Select(e => e.Id).ToArray();
        Assert.That(adds.Length, Is.EqualTo(2));
        Assert.That(adds.All(fight.Session.State.IsEnemyAlive), Is.True);

        fight.Target = Hierophant;
        fight.Until(() => fight.Events.Count<StageCleared>() == 1, 20f);
        Assert.That(fight.Events.Of<HierophantWard>().Last().Active, Is.False);
        Assert.That(fight.Events.Of<EnemyDied>().Skip(1).Select(e => e.Id), Is.EquivalentTo(adds));
        Assert.That(adds.All(id => !fight.Session.State.IsEnemyAlive(id)), Is.True);
        Assert.That(fight.Events.Single<XpChanged>().Gained, Is.EqualTo(fight.BossXp));
    }

    [Test]
    public void RangerProbe_ClearsRecurringWardAndOpensStage16()
    {
        using var fight = new Fight { FollowWard = true };
        fight.Until(() => fight.Events.Count<EnemySpawned>() == 1, 10f);
        EnemySpawned boss = fight.Events.Single<EnemySpawned>();
        Assert.That((boss.SpecId, boss.Position), Is.EqualTo((Hierophant, new Position(0f, 0f, 2f))));
        Assert.That(fight.Events.Single<StageArrived>().ArenaId, Is.EqualTo(Dunes));
        Assert.That(fight.Events.Count<SpawnTelegraphed>(), Is.Zero);
        Assert.That(fight.ArrivalPoints, Is.EqualTo(fight.ArrivalLevel - 1));

        fight.Until(() => fight.Events.Of<EnemyDied>().Any(e => e.Id == boss.Id), 520f);
        float elapsed = fight.Session.State.Time - fight.SpawnedAt;
        float firstHit = fight.Events.Of<EnemyDamaged>().First(e => e.Id == boss.Id && e.Amount > 0f).Amount;
        TestContext.WriteLine($"DT-01 Stage 15 Ranger level {fight.ArrivalLevel}, first bow hit {firstHit:F2}, " +
            $"{fight.BaseHp} authored HP, {elapsed:F2} s from spawn through both beats. Points banked, no actives.");
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
    public void RangerWithQuickDraw_SeesBothStrikesBeforeKillingBoss()
    {
        using var fight = new Fight { FollowWard = true };
        fight.Session.TakeNode(new ContentId("skill.ranger.quick-draw"));
        fight.Session.TakeNode(new ContentId("skill.ranger.quick-draw-2"));
        fight.Session.TakeNode(new ContentId("skill.ranger.quick-draw-3"));
        fight.Until(() => fight.Events.Count<BossBeatEnded>() == 2, 330f);
        int impacts = fight.Events.Count<HierophantImpact>();

        fight.Until(() => fight.Events.Count<StageCleared>() == 1, 145f);
        HierophantImpact[] final = fight.Events.Of<HierophantImpact>().Skip(impacts).Take(2).ToArray();
        Assert.That(final.Select(hit => (hit.Move, hit.RecoverySeconds)),
            Is.EqualTo(new[] { (HierophantMove.SunSeal, 0f), (HierophantMove.SunSeal, 1.5f) }));
        TestContext.WriteLine($"Quick Draw rank 3 clear at {fight.Session.State.Time - fight.SpawnedAt:F2} s after spawn.");
    }

    [TestCase(-16f, -14f)]
    [TestCase(16f, 14f)]
    [TestCase(0f, -14f)]
    public void Seal_CoversPlayerPositionAndAllowsWalkingEscape(float x, float z)
    {
        using var fight = new Fight { Target = NoTarget, ReturnToLane = false };
        Assert.That(NavMesh.SamplePosition(new Vector3(x, 0f, z), out NavMeshHit sample, 1f, NavMesh.AllAreas), Is.True);
        fight.Player.Teleport(sample.position);
        fight.Until(() => fight.Events.Count<HierophantArmed>() > 0, 10f);
        HierophantArmed warning = fight.Events.Single<HierophantArmed>();
        Assert.That(warning.Move, Is.EqualTo(HierophantMove.SunSeal));
        AssertAim(warning, sample.position);
        int hits = fight.Events.Count<PlayerDamaged>();
        fight.Until(() => fight.Events.Count<HierophantImpact>() == 1, 2f);
        Assert.That(fight.Events.Count<PlayerDamaged>(), Is.EqualTo(hits));
    }

    [Test]
    public void FinalPair_AllowsTwoWalkingEscapes()
    {
        using var fight = new Fight { FollowWard = true };
        fight.Until(() => fight.Events.Count<BossBeatStarted>() == 2, 345f);
        fight.FollowWard = false;
        fight.Target = NoTarget;
        fight.ReturnToLane = false;
        fight.Player.Teleport(new Vector3(2.5f, 0f, -5f));
        int warnings = fight.Events.Count<HierophantArmed>();
        int impacts = fight.Events.Count<HierophantImpact>();
        int hits = fight.Events.Count<PlayerDamaged>();
        for (int i = 1; i <= 2; i++)
        {
            fight.Until(() => fight.Events.Count<HierophantArmed>() == warnings + i, 4f);
            HierophantArmed warning = fight.Events.Of<HierophantArmed>()[^1];
            Assert.That((warning.Move, warning.Seconds), Is.EqualTo((HierophantMove.SunSeal, 1.3f)));
            AssertAim(warning, fight.Player.Position);
            fight.Until(() => fight.Events.Count<HierophantImpact>() == impacts + i, 2f);
        }
        Assert.That(fight.Events.Count<PlayerDamaged>(), Is.EqualTo(hits), "The Ranger walks out of both warnings.");
    }

    private static void AssertAim(HierophantArmed warning, Vector3 player)
    {
        var offset = new Direction(player.x - warning.Origin.X, player.z - warning.Origin.Z);
        var right = new Direction(warning.Facing.Y, -warning.Facing.X);
        Assert.That(Mathf.Abs(Direction.Dot(offset, right)), Is.LessThan(0.15f));
        Assert.That(Direction.Dot(offset, warning.Facing), Is.InRange(-0.15f, warning.Reach + 0.15f));
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
        for (int stage = 1; stage <= expected[^1].Stage; stage++)
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
        private bool _wardActive;
        private Vector3? _waypoint;
        private int _read;

        public Fight(bool useFinisher = false)
        {
            ContentCatalog catalog = Catalog(out _, useFinisher);
            var random = new SeededRandom(12);
            RunSnapshot restore = new TestStart(catalog).At(Sunscar, Ranger, random.Seed, random.Capture(), 15);
            BaseHp = catalog.Enemy(Hierophant).MaxHp;
            ModeSpec mode = catalog.Mode(Sunscar);
            BossXp = mode.CampaignXp.BossFraction * mode.Xp.ToReach(restore.Level + 1);
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
        public RecordingEvents Events => _events.Recorded;
        public int ArrivalLevel { get; }
        public int ArrivalPoints { get; }
        public float BaseHp { get; }
        public float BossXp { get; }
        public float SpawnedAt { get; private set; }
        public ContentId? Target { get; set; }
        public bool FollowWard { get; set; }

        public EnemyDamaged LastHit(int id) => Events.Of<EnemyDamaged>().Last(e => e.Id == id && e.Amount > 0f);

        public bool HitAfter(int id, int before) => Events.Of<EnemyDamaged>().Skip(before).Any(e => e.Id == id && e.Amount > 0f);
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
            ContentId? target = FollowWard ? (_wardActive ? Priest : Hierophant) : Target;
            foreach (var (id, enemy) in _alive)
            {
                // This is the open firing lane probe. Isolate a target without changing weapon damage or cadence.
                bool covered = target.HasValue && target.Value != enemy.Spec;
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
                    case HierophantWard ward:
                        _wardActive = ward.Active;
                        break;
                    case HierophantArmed armed:
                        _warning = armed;
                        break;
                    case HierophantImpact { Move: HierophantMove.SunSeal }:
                    case HierophantFireWave { Active: false }:
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
                // Return between attacks so successive retreats cannot pin the probe against the arena boundary.
                var towardLane = new Direction(2.5f - player.X, -5f - player.Z);
                return ReturnToLane && SpawnedAt > 0f && Session.State.StageIndex == 15
                    && Events.Count<StageCleared>() == 0 && towardLane.LengthSquared() > 0.09f
                        ? Direction.Normalize(towardLane) : Direction.Zero;
            }

            var offset = new Direction(player.X - armed.Origin.X, player.Z - armed.Origin.Z);
            var right = new Direction(armed.Facing.Y, -armed.Facing.X);
            float across = Direction.Dot(offset, right);
            float edge = armed.HalfWidth + PlayerRadius;
            if (MathF.Abs(across) >= edge)
                return Direction.Zero;

            if (armed.Move == HierophantMove.FireThrow)
            {
                float along = Direction.Dot(offset, armed.Facing);
                float retreat = armed.Reach + PlayerRadius - along;
                var exit = new Vector3(armed.Facing.X, 0f, armed.Facing.Y) * retreat;
                if (retreat <= 0f || along <= -PlayerRadius)
                    return Direction.Zero;
                if (retreat < edge - MathF.Abs(across) &&
                    !NavMesh.Raycast(player.ToUnity(), player.ToUnity() + exit, out _, NavMesh.AllAreas))
                    return armed.Facing;
            }

            float sign = across < 0f ? -1f : 1f;
            Direction step = right * (sign * edge - across);
            var end = new Vector3(player.X + step.X, player.Y, player.Z + step.Y);
            return NavMesh.Raycast(player.ToUnity(), end, out _, NavMesh.AllAreas) ? -sign * right : sign * right;
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
