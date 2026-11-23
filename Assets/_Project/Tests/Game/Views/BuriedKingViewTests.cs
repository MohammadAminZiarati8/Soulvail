using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class BuriedKingViewTests
{
    private const int KingId = 30;
    private readonly List<GameObject> _objects = new();
    private DomainEventHub _hub;
    private IObjectResolver _container;
    private EnemyViews _views;
    private EnemyView _body;
    private Vector3 _origin;

    [SetUp]
    public void Rent()
    {
        _origin = new Vector3(2000f, 0f, 2000f);
        var prefab = AssetDatabase.LoadAssetAtPath<EnemyView>("Assets/_Project/Prefabs/Enemies/BuriedKing.prefab");
        _hub = new DomainEventHub();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(_hub);
        var random = new FixedRandom(7);
        var recorder = new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), _hub);
        builder.RegisterInstance<IRunSession>(new RunSession(new ContentCatalog(Array.Empty<CharacterSpec>()),
            random, _hub, new RecordingIntents(), recorder, 8, 8, 8));
        _container = builder.Build();
        _views = new EnemyViews(_container, prefab, null, _hub, new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()));
        _hub.Publish(new EnemySpawned(KingId, BuriedKingBehaviour.PrototypeBodyId, _origin.ToNum()));
        Assert.That(_views.TryGet(KingId, out _body), Is.True);
    }

    [TearDown]
    public void Return()
    {
        _views?.Dispose();
        _hub?.Dispose();
        _container?.Dispose();
        foreach (GameObject resource in _objects)
        {
            Object.DestroyImmediate(resource);
        }

        _objects.Clear();
    }

    [Test]
    public void Lunge_ClearanceIncludesLaneAndEscape()
    {
        Rect bounds = new(_origin.x - 12f, _origin.z - 12f, 24f, 24f);
        Assert.That(Measure(Vector3.forward, bounds), Is.EqualTo(8f));
        Assert.That(Measure(Vector3.forward, default), Is.Zero);

        Rect nearWall = new(bounds.xMin, bounds.yMin, 18f, bounds.height);
        Assert.That(Measure(Vector3.forward + Vector3.right, nearWall), Is.InRange(5f, 7f));

        // Cover can cut into the warning while leaving the King's centerline clear.
        GameObject side = CoverAt(1.8f, 7.25f);
        Assert.That(Measure(Vector3.forward, bounds), Is.InRange(5f, 7f));
        side.SetActive(false);

        // One open side is enough. Two walls just outside the warning leave no exit.
        CoverAt(-2.6f, 4f, 8f);
        Assert.That(Measure(Vector3.forward, bounds), Is.EqualTo(8f));
        CoverAt(2.6f, 4f, 8f);
        Assert.That(Measure(Vector3.forward, bounds), Is.LessThan(5f));
    }

    [TestCase("center")]
    [TestCase("diagonal wall")]
    [TestCase("cover")]
    [TestCase("blocked after warning")]
    public void Stage30_TestStartChecksTheLeapInDunes(string scenario)
    {
        var (session, intents, arena, world) = StartStage30();
        var poses = new List<BuriedKingStateChanged>();
        using var watch = _hub.Subscribe<BuriedKingStateChanged>(poses.Add);
        int damageEvents = 0;
        using var damage = _hub.Subscribe<EnemyDamaged>(_ => damageEvents++);
        int id = _body.Id;
        float dt = world.Dt;
        Vector3 kingStart = _origin;
        Vector3 hero = _origin + 12f * Vector3.forward;
        if (scenario == "diagonal wall")
        {
            kingStart += 11f * Vector3.back;
            hero = kingStart + 5f * (Vector3.right + Vector3.back).normalized;
        }
        else if (scenario == "cover")
        {
            hero = _origin + 6f * Vector3.forward;
            CoverAt(1.8f, 7.25f);
        }

        _body.Bind(id, kingStart);
        world.PlayerPosition = hero.ToNum();
        Physics.SyncTransforms();
        _views.CopyInto(world, arena.PlayBounds);
        world.Enemies[0].HasLineOfSight = true;
        world.Enemies[0].KingLungeClearance = 0f;
        session.Tick(world);
        float walkSpeed = intents.EnemyMoves.Single(move => move.Id == id).Velocity.Length();
        Assert.That(walkSpeed, Is.EqualTo(2.8f).Within(0.001f));

        float fastestApproach = 0f;
        for (int frame = 0; frame < 300; frame++)
        {
            world.EnemyCount = 0;
            Physics.SyncTransforms();
            _views.CopyInto(world, arena.PlayBounds);
            world.Enemies[0].HasLineOfSight = true;
            intents.Clear();
            int count = poses.Count;
            session.Tick(world);
            if (poses.Count > count && poses[^1].Pose == BuriedKingPose.LungeTell)
            {
                Vector3 side = Vector3.Cross(Vector3.up, (poses[^1].End - poses[^1].Start).ToUnity()).normalized;
                world.PlayerPosition = (hero + 3f * side).ToNum();
                if (scenario == "blocked after warning")
                {
                    CoverAt(0f, 7f);
                }
            }

            foreach (EnemyMoveIntent move in intents.EnemyMoves)
            {
                if (poses.Count > 0 && poses[^1].Pose == BuriedKingPose.LungeApproach)
                {
                    fastestApproach = Mathf.Max(fastestApproach, move.Velocity.Length());
                }

                if (move.Id == id)
                {
                    _body.Apply(move, dt);
                }
            }

            if (poses.Count > 0 && poses[^1].Pose == BuriedKingPose.LungeRecovery)
            {
                break;
            }
        }

        var tell = poses.Single(state => state.Pose == BuriedKingPose.LungeTell);
        var recovery = poses.Single(state => state.Pose == BuriedKingPose.LungeRecovery);
        float length = (tell.End - tell.Start).Length();
        Assert.That(tell.Width, Is.EqualTo(4f));
        Assert.That(recovery.End, Is.EqualTo(tell.End));
        if (scenario == "center" || scenario == "blocked after warning")
        {
            Assert.That(fastestApproach, Is.EqualTo(1.6f * walkSpeed).Within(0.001f));
            Assert.That(length, Is.EqualTo(8f).Within(0.001f));
        }
        else
        {
            Assert.That(length, Is.InRange(5f, 7.99f));
        }

        Assert.That(recovery.CutSeconds, Is.EqualTo(scenario == "blocked after warning" ? 0f : 0.15f));
        Assert.That(damageEvents, Is.Zero);
        session.End();
    }

    private (RunSession, RecordingIntents, ArenaView, WorldSnapshot) StartStage30()
    {
        _hub.Publish(new EnemyDespawned(KingId));
        const string root = "Assets/_Project/";
        var room = AssetDatabase.LoadAssetAtPath<GameObject>(root + "Prefabs/Arenas/Sunscar/Arena_Dunes.prefab");
        var arena = Object.Instantiate(room, _origin, Quaternion.identity).GetComponent<ArenaView>();
        _objects.Add(arena.gameObject);
        var mode = AssetDatabase.LoadAssetAtPath<ModeDefinition>(root + "Data/Modes/Sunscar.asset").ToSpec();
        var rangerAsset = Object.Instantiate(
            AssetDatabase.LoadAssetAtPath<CharacterDefinition>(root + "Data/Characters/Ranger.asset"));
        var rangerSettings = new SerializedObject(rangerAsset);
        // The fixture baits the leap at full King health. The Ranger cannot shoot from bait range.
        rangerSettings.FindProperty("_weaponRange").floatValue = 0.01f;
        rangerSettings.ApplyModifiedPropertiesWithoutUndo();
        var ranger = rangerAsset.ToSpec();
        Object.DestroyImmediate(rangerAsset);
        var boot = AssetDatabase.LoadAssetAtPath<GameObject>(root + "Prefabs/Composition/BootScope.prefab");
        var source = new SerializedObject(boot.GetComponent<BootScope>());
        var catalog = new ContentCatalog(new[] { ranger },
            Definitions<EnemyDefinition>(source, "_enemies").Select(asset => asset.ToSpec()).ToArray(),
            new[] { mode }, bosses: Definitions<BossDefinition>(source, "_bosses").Select(asset => asset.ToSpec()).ToArray());
        var random = new SeededRandom(12);
        var intents = new RecordingIntents();
        var recorder = new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), _hub);
        var session = new RunSession(catalog, random, _hub, intents, recorder, 64, 28, 64);
        var restore = new TestStart(catalog).At(mode.Id, ranger.Id, random.Seed, random.Capture(), 30);
        int id = 0;
        using var spawn = _hub.Subscribe<EnemySpawned>(evt => id = evt.Id);
        session.Start(new RunConfig(mode.Id, ranger.Id, random.Seed, 30, SpawnPlan.Empty, restore));

        const float dt = 1f / 60f;
        var world = new WorldSnapshot(64)
        {
            Dt = dt,
            PlayerPosition = arena.PlayerStart.ToNum(),
            SpawnPoints = arena.SpawnPoints.Select(point => point.ToNum()).ToArray(),
        };
        for (int frame = 0; frame < 240 && id == 0; frame++)
        {
            session.Tick(world);
            intents.Clear();
        }

        Assert.That(_views.TryGet(id, out _body), Is.True);
        return (session, intents, arena, world);
    }

    private static IEnumerable<T> Definitions<T>(SerializedObject source, string field) where T : ScriptableObject
    {
        SerializedProperty rows = source.FindProperty(field);
        for (int i = 0; i < rows.arraySize; i++)
        {
            yield return (T)rows.GetArrayElementAtIndex(i).objectReferenceValue;
        }
    }

    [TestCase(1f, BuriedKingPose.SweepTell, BuriedKingPose.SweepImpact)]
    [TestCase(0.65f, BuriedKingPose.SlamTell, BuriedKingPose.SlamImpact)]
    [TestCase(0.32f, BuriedKingPose.SlamTell, BuriedKingPose.SlamImpact)]
    public void Stage30_TestStartChecksTheSwordCutsInDunes(float health, BuriedKingPose tell, BuriedKingPose impact)
    {
        var (session, intents, arena, world) = StartStage30();
        int id = _body.Id;
        _body.Bind(id, _origin);
        const BindingFlags statePorts = BindingFlags.Instance | BindingFlags.NonPublic;
        var enemies = (EnemySystem)typeof(RunState).GetProperty("Enemies", statePorts).GetValue(session.State);
        Assert.That(enemies.Registry.TryGet(id, out EnemyAgent king), Is.True);
        if (health < 1f)
        {
            king.Health.ApplyDamage(king.Health.MaxHp.Value * (1f - health), 0f);
        }

        Vector3 bait = _origin + Vector3.forward * (tell == BuriedKingPose.SweepTell ? 3f : 8f);
        world.PlayerPosition = bait.ToNum();
        world.OpenGround = new[] { bait.ToNum() };
        var poses = new List<BuriedKingStateChanged>();
        using var watch = _hub.Subscribe<BuriedKingStateChanged>(poses.Add);
        int hits = 0;
        using var damage = _hub.Subscribe<PlayerDamaged>(_ => hits++);
        Transform warnings = GameObject.Find("Buried King warnings").transform;
        Transform warning = warnings.Find("Attack warning");
        Transform fill = warnings.Find("Attack warning fill");
        Transform ribbon = warnings.Find("Sword cut ribbon");
        Transform model = _body.GetComponentsInChildren<Transform>().Single(bone => bone.name == "Rig_Medium").parent;
        Transform grip = model.GetComponentsInChildren<Transform>().Single(bone => bone.name == "handslot.r");
        var rootFacings = new List<Quaternion>();
        var swordDirections = new List<Vector3>();
        int impactFrame = -1;
        for (int frame = 0; frame < 1000; frame++)
        {
            world.EnemyCount = 0;
            Physics.SyncTransforms();
            _views.CopyInto(world, arena.PlayBounds);
            world.Enemies[0].HasLineOfSight = true;
            world.Enemies[0].KingLungeClearance = 0f;
            intents.Clear();
            session.Tick(world);
            EnemyMoveIntent move = intents.EnemyMoves.Single();
            BuriedKingPose pose = poses[^1].Pose;
            if (pose is BuriedKingPose.SpinTell or BuriedKingPose.SpinActive or BuriedKingPose.SpinPulse)
            {
                // This fixture isolates the planted cut at the original cast position.
                continue;
            }

            _body.Apply(move, world.Dt);
            if (pose is BuriedKingPose.SweepTell or BuriedKingPose.CastTell)
            {
                // The hero sidesteps the locked shape so all three impact checks exercise a miss.
                world.PlayerPosition = (_origin + Vector3.right * 5f).ToNum();
            }

            if (pose == tell || pose == impact)
            {
                Assert.That(move.Velocity, Is.EqualTo(System.Numerics.Vector3.Zero));
                Assert.That(Vector2.Distance(new Vector2(_body.transform.position.x, _body.transform.position.z),
                    new Vector2(_origin.x, _origin.z)), Is.LessThan(0.001f));
                Assert.That(grip.childCount, Is.GreaterThan(0));
                Transform sword = grip.Find("Buried King's sword");
                Assert.That(sword.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(sword.localRotation, Is.EqualTo(Quaternion.identity));
                if (pose == tell)
                {
                    Assert.That(IsVisible(warning), Is.True);
                    if (IsVisible(ribbon))
                    {
                        swordDirections.Add(sword.up);
                        rootFacings.Add(model.rotation);
                    }
                }
                else if (impactFrame < 0)
                {
                    impactFrame = frame;
                    Assert.That(IsVisible(fill) && IsVisible(ribbon), Is.True);
                    swordDirections.Add(sword.up);
                    rootFacings.Add(model.rotation);
                }
                else if (frame - impactFrame == 11)
                {
                    Assert.That(IsVisible(fill) || IsVisible(ribbon), Is.False);
                    break;
                }
            }
        }

        Assert.That(impactFrame, Is.GreaterThanOrEqualTo(0));
        Assert.That(hits, Is.Zero);
        Assert.That(poses.Count(change => change.Pose == impact), Is.EqualTo(1));
        Assert.That(poses.Any(change => change.Pose == BuriedKingPose.FractureTell), Is.EqualTo(health < 0.33f));
        Assert.That(swordDirections.Count, Is.GreaterThan(5));
        float turned = 0f;
        for (int i = 1; i < rootFacings.Count; i++)
        {
            turned += Quaternion.Angle(rootFacings[i - 1], rootFacings[i]);
        }

        if (impact == BuriedKingPose.SlamImpact)
        {
            Assert.That(turned, Is.InRange(340f, 360.1f));
        }
        else
        {
            Assert.That(turned, Is.LessThan(0.1f));
            Assert.That(Vector3.SignedAngle(Vector3.ProjectOnPlane(swordDirections[0], Vector3.up),
                Vector3.ProjectOnPlane(swordDirections[^1], Vector3.up), Vector3.up), Is.InRange(108f, 120.1f));
        }

        session.End();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Stage30_TestStartChecksSpinUnlockDuringLeapAndRecall(bool leap)
    {
        var (session, intents, arena, world) = StartStage30();
        int id = _body.Id;
        _body.Bind(id, _origin);
        Vector3 bait = _origin + 7f * Vector3.forward;
        world.PlayerPosition = bait.ToNum();
        world.OpenGround = new[] { bait.ToNum() };
        EnemyAgent king = StageKing(session);
        var poses = new List<BuriedKingStateChanged>();
        using var watch = _hub.Subscribe<BuriedKingStateChanged>(poses.Add);
        BuriedKingPose committed = leap ? BuriedKingPose.LungeTravel : BuriedKingPose.RecallTell;
        BuriedKingPose recovering = leap ? BuriedKingPose.LungeRecovery : BuriedKingPose.Held;
        int crossed = -1;
        int recovery = -1;
        int debut = -1;
        for (int frame = 0; frame < 800; frame++)
        {
            int count = poses.Count;
            StepStage30(session, intents, arena, world, allowLeap: leap);
            if (poses.Count == count)
            {
                continue;
            }

            if (crossed < 0 && poses[^1].Pose == committed)
            {
                crossed = frame;
                king.Health.ApplyDamage(king.Health.MaxHp.Value * 0.35f, 0f);
                world.PlayerPosition = (_origin + 7f * Vector3.right).ToNum();
            }

            if (recovery < 0 && crossed >= 0 && poses[^1].Pose == recovering)
            {
                recovery = frame;
            }

            if (poses[^1].Pose == BuriedKingPose.SpinTell)
            {
                debut = frame;
                break;
            }
        }

        Assert.That(crossed, Is.GreaterThanOrEqualTo(0));
        Assert.That(recovery, Is.GreaterThan(crossed));
        Assert.That(debut, Is.GreaterThan(recovery));
        Assert.That((debut - recovery) * world.Dt,
            Is.EqualTo(leap ? 0.9f : 2.1f).Within(2f * world.Dt));
        Assert.That(Vector3.Distance(poses[^1].Start.ToUnity(), _body.transform.position), Is.LessThan(0.001f));
        Assert.That(king.IsVulnerable, Is.True);
        session.End();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Stage30_TestStartChecksSpinPursuitAroundCoverAndAlongWalls(bool cover)
    {
        var (session, intents, arena, world) = StartStage30();
        Vector3 start = _origin + (cover ? new Vector3(4f, 0f, -2.7f) : new Vector3(17.5f, 0f, -11f));
        Vector3 bait = _origin + (cover ? new Vector3(12.5f, 0f, -2.7f) : new Vector3(17.9f, 0f, 5f));
        _body.Bind(_body.Id, start);
        world.PlayerPosition = bait.ToNum();
        EnemyAgent king = StageKing(session);
        king.Health.ApplyDamage(king.Health.MaxHp.Value * 0.35f, 0f);
        var nav = new NavPathSense(8);
        int coverMask = LayerMask.GetMask(ArenaView.CoverLayerName);
        Physics.SyncTransforms();
        Assert.That(Physics.Linecast(start + Vector3.up, bait + Vector3.up, coverMask), Is.EqualTo(cover));
        var poses = new List<BuriedKingStateChanged>();
        using var watch = _hub.Subscribe<BuriedKingStateChanged>(poses.Add);
        int hits = 0;
        using var damage = _hub.Subscribe<PlayerDamaged>(_ => hits++);
        Transform warnings = GameObject.Find("Buried King warnings").transform;
        Transform floor = warnings.Find("Pursuit spin floor");
        Transform disc = floor.Find("Pursuit spin disc");
        Transform ribbon = warnings.Find("Sword cut ribbon");
        var rim = floor.Find("Pursuit spin rim").GetComponent<LineRenderer>();
        Transform model = _body.GetComponentsInChildren<Transform>().Single(bone => bone.name == "Rig_Medium").parent;
        bool routed = false;
        float sideTravel = 0f;
        float forwardTravel = 0f;
        int flashes = 0;
        int misses = 0;
        int activeFrames = 0;
        bool checkedYaw = false;
        var color = new MaterialPropertyBlock();
        for (int frame = 0; frame < 450; frame++)
        {
            Vector3 before = _body.transform.position;
            if (cover && (bait - before).sqrMagnitude < 4f)
            {
                // Keep the hero out of point-blank auto-fire range once the King rounds the spur.
                bait += 3f * Vector3.right;
                world.PlayerPosition = bait.ToNum();
            }

            Quaternion facing = model.rotation;
            int poseCount = poses.Count;
            int hitsBefore = hits;
            Vector2 path = nav.DirectionFor(_body.Id, before, bait, frame * world.Dt);
            EnemyMoveIntent move = StepStage30(session, intents, arena, world, path: path);
            Vector3 observed = _body.transform.position;
            if (poses[^1].Pose == BuriedKingPose.SpinTell)
            {
                Assert.That(move.Velocity.Length(), Is.Zero);
                continue;
            }

            if (poses[^1].Pose == BuriedKingPose.SpinRecovery)
            {
                Assert.That(move.Velocity.Length(), Is.Zero);
                Assert.That(IsVisible(disc) || IsVisible(ribbon) || rim.enabled, Is.False);
                break;
            }

            activeFrames++;
            routed |= Mathf.Abs(path.y) > 0.2f;
            sideTravel = Mathf.Max(sideTravel, Mathf.Abs(observed.z - start.z));
            forwardTravel = Mathf.Max(forwardTravel, observed.x - start.x);
            Assert.That(move.Velocity.Length(), Is.LessThanOrEqualTo(2.001f));
            Assert.That(arena.PlayBounds.Contains(new Vector2(observed.x, observed.z)), Is.True);
            Assert.That(floor.position, Is.EqualTo(observed));
            Assert.That(IsVisible(disc) && IsVisible(ribbon) && rim.enabled, Is.True);
            Assert.That(Vertices(ribbon).All(vertex => (vertex - observed).sqrMagnitude < 12.25f), Is.True);
            if (activeFrames > 1 && move.Velocity.Length() > 0f)
            {
                Assert.That(Quaternion.Angle(facing, model.rotation), Is.EqualTo(15f).Within(0.1f));
                checkedYaw = true;
            }

            if (poses.Count > poseCount && poses[^1].Pose == BuriedKingPose.SpinPulse)
            {
                flashes++;
                misses += hits == hitsBefore ? 1 : 0;
                Assert.That(Vector3.Distance(poses[^1].Start.ToUnity(), before), Is.LessThan(0.001f));
                Assert.That(rim.widthMultiplier, Is.GreaterThan(0.2f));
                rim.GetPropertyBlock(color);
                Assert.That(color.GetColor("_BaseColor").g, Is.GreaterThan(0.9f));
            }
            else
            {
                Assert.That(hits, Is.EqualTo(hitsBefore));
            }
        }

        Assert.That(poses[^1].Pose, Is.EqualTo(BuriedKingPose.SpinRecovery));
        Assert.That(activeFrames * world.Dt, Is.EqualTo(5f).Within(world.Dt));
        Assert.That(flashes, Is.EqualTo(5));
        Assert.That(misses, Is.GreaterThan(0));
        Assert.That(checkedYaw, Is.True);
        Assert.That(sideTravel, Is.GreaterThan(cover ? 1f : 7f));
        if (cover)
        {
            Assert.That(routed, Is.True);
            Assert.That(forwardTravel, Is.GreaterThan(4.5f));
        }
        else
        {
            Assert.That(forwardTravel, Is.LessThan(0.6f));
        }

        session.End();
    }

    [TestCase(BuriedKingPose.Cleared)]
    [TestCase(BuriedKingPose.SpinRecovery)]
    public void Spin_EndClearsEffectsAndRecovers(BuriedKingPose ending)
    {
        Transform warnings = GameObject.Find("Buried King warnings").transform;
        Transform floor = warnings.Find("Pursuit spin floor");
        Transform ribbon = warnings.Find("Sword cut ribbon");
        var rim = floor.Find("Pursuit spin rim").GetComponent<LineRenderer>();
        Publish(BuriedKingPose.SpinTell, _origin, 1.25f, 0f, radius: 3.5f);
        StepBody(0f);
        StepBody(0.63f);
        Assert.That(IsVisible(floor.Find("Pursuit spin disc fill")), Is.True);
        Publish(BuriedKingPose.SpinActive, _origin, 5f, 0f, radius: 3.5f);
        StepBody(0f);
        Publish(BuriedKingPose.SpinPulse, _origin, 0f, 0f, radius: 3.5f);
        StepBody(0.05f);
        Assert.That(rim.widthMultiplier, Is.GreaterThan(0.2f));
        Assert.That(IsVisible(ribbon), Is.True);
        Publish(ending, _origin, 1.6f, 0f, radius: 3.5f);
        StepBody(0.05f);
        Assert.That(rim.enabled || IsVisible(ribbon) || IsVisible(floor.Find("Pursuit spin disc")), Is.False);
        _hub.Publish(new EnemyDespawned(KingId));
        Assert.That(warnings == null, Is.True);
    }

    private EnemyAgent StageKing(RunSession session)
    {
        const BindingFlags statePorts = BindingFlags.Instance | BindingFlags.NonPublic;
        var enemies = (EnemySystem)typeof(RunState).GetProperty("Enemies", statePorts).GetValue(session.State);
        Assert.That(enemies.Registry.TryGet(_body.Id, out EnemyAgent king), Is.True);
        return king;
    }

    private EnemyMoveIntent StepStage30(RunSession session, RecordingIntents intents, ArenaView arena,
        WorldSnapshot world, bool allowLeap = false, Vector2 path = default)
    {
        Physics.SyncTransforms();
        world.EnemyCount = 0;
        _views.CopyInto(world, arena.PlayBounds);
        ref EnemySense sense = ref world.Enemies[0];
        sense.HasLineOfSight = !Physics.Linecast(sense.Position.ToUnity() + Vector3.up,
            world.PlayerPosition.ToUnity() + Vector3.up, LayerMask.GetMask(ArenaView.CoverLayerName));
        sense.PathDirectionToPlayer = path.ToNum();
        if (!allowLeap)
        {
            sense.KingLungeClearance = 0f;
        }

        intents.Clear();
        session.Tick(world);
        EnemyMoveIntent move = intents.EnemyMoves.Single();
        _body.Apply(move, world.Dt);
        return move;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SwordCut_TrailMatchesWarningAtLowFrameRate(bool circle)
    {
        Vector3 end = _origin + (circle ? Vector3.zero : Vector3.forward * 4f);
        float duration = circle ? 0.9f : 0.85f;
        float cut = circle ? 0.22f : 0.16f;
        float radius = circle ? 3.5f : 0f;
        float arc = circle ? 0f : 120f;
        Publish(circle ? BuriedKingPose.SlamTell : BuriedKingPose.SweepTell, end, duration, cut, arc, radius);
        Transform warnings = GameObject.Find("Buried King warnings").transform;
        Transform warning = warnings.Find("Attack warning");
        Transform fill = warnings.Find("Attack warning fill");
        Transform streak = warnings.Find("Sword cut ground streak");
        Transform ribbon = warnings.Find("Sword cut ribbon");
        Vector3[] shape = Vertices(warning);
        StepBody(0f);
        StepBody(duration - cut);
        Assert.That(IsVisible(warning), Is.True);
        Assert.That(IsVisible(ribbon), Is.False);

        Transform sword = _body.GetComponentsInChildren<Transform>().Single(child => child.name == "Buried King's sword");
        Vector3 firstEdge = Vector3.ProjectOnPlane(shape[1] - shape[0], Vector3.up);
        Assert.That(Vector3.Angle(Vector3.ProjectOnPlane(sword.up, Vector3.up), firstEdge), Is.LessThan(0.1f));
        StepBody(cut * 0.5f);
        Assert.That(IsVisible(warning), Is.True);
        Assert.That(IsVisible(ribbon), Is.True);
        Assert.That(Vertices(streak).Length, Is.InRange(46, 52));
        Vector3 midCut = Quaternion.AngleAxis(circle ? 180f : 60f, Vector3.up) * firstEdge;
        Assert.That(Vector3.Angle(Vector3.ProjectOnPlane(sword.up, Vector3.up), midCut), Is.LessThan(0.1f));

        // Impact completes the skipped arcs even when the player dodged the cut.
        Publish(circle ? BuriedKingPose.SlamImpact : BuriedKingPose.SweepImpact, end, 0f, cut, arc, radius);
        StepBody(0.2f);
        Assert.That(IsVisible(fill), Is.True);
        Assert.That(IsVisible(streak), Is.True);
        Assert.That(IsVisible(ribbon), Is.True);
        Vector3[] flash = Vertices(fill);
        Vector3[] track = Vertices(streak);
        Assert.That(track.Length, Is.EqualTo((shape.Length - 1) * 2));
        for (int i = 1; i < shape.Length; i++)
        {
            Assert.That(track[i * 2 - 1], Is.EqualTo(shape[i]));
            Assert.That(Vector3.Distance(flash[i], shape[i] + 0.012f * Vector3.up), Is.LessThan(0.0001f));
        }

        Vector3 finalEdge = Vector3.ProjectOnPlane(shape[^1] - shape[0], Vector3.up);
        Assert.That(Vector3.Angle(Vector3.ProjectOnPlane(sword.up, Vector3.up), finalEdge), Is.LessThan(0.1f));
        Vector3[] blade = Vertices(ribbon);
        Assert.That(Vector3.Distance(blade[^1], sword.position + sword.up * (1.96f * 0.88f)), Is.LessThan(0.001f));

        StepBody(0.119f);
        Assert.That(IsVisible(fill), Is.True);
        StepBody(0.002f);
        Assert.That(IsVisible(fill), Is.False);
        Assert.That(IsVisible(streak), Is.False);
        Assert.That(IsVisible(ribbon), Is.False);

        Publish(circle ? BuriedKingPose.SlamTell : BuriedKingPose.SweepTell, end, duration, cut, arc, radius);
        StepBody(0f);
        StepBody(duration - cut * 0.5f);
        Assert.That(IsVisible(ribbon), Is.True);
        Publish(BuriedKingPose.Cleared, end, 0f, 0f);
        Assert.That(IsVisible(warning), Is.False);
        Assert.That(IsVisible(fill), Is.False);
        Assert.That(IsVisible(streak), Is.False);
        Assert.That(IsVisible(ribbon), Is.False);
        _hub.Publish(new EnemyDespawned(KingId));
        Assert.That(warnings == null, Is.True);
    }

    private static bool IsVisible(Transform effect) => effect.GetComponent<MeshRenderer>().enabled;

    private static Vector3[] Vertices(Transform effect) => effect.GetComponent<MeshFilter>().sharedMesh.vertices;

    private void StepBody(float dt) =>
        _body.Apply(new EnemyMoveIntent(_body.Id, System.Numerics.Vector3.Zero, System.Numerics.Vector2.UnitY), dt);

    [TestCase(true)]
    [TestCase(false)]
    public void Lunge_LockedWarningMatchesSingleHit_AndOnlyConfirmedLandingFlashes(bool landed)
    {
        Vector3 end = _origin + new Vector3(3f, 0f, 4f);
        Publish(BuriedKingPose.LungeTell, end, 1f, 0.15f);
        GameObject warnings = GameObject.Find("Buried King warnings");
        Transform warning = warnings.transform.Find("Attack warning");
        Transform fill = warnings.transform.Find("Attack warning fill");
        SlamDustView dust = warnings.GetComponentInChildren<SlamDustView>();
        MeshRenderer warningRenderer = warning.GetComponent<MeshRenderer>();
        MeshRenderer fillRenderer = fill.GetComponent<MeshRenderer>();
        Vector3 side = 2f * Vector3.Cross(Vector3.up, end - _origin).normalized;
        Vector3 lift = Vector3.up * warning.GetComponent<MeshFilter>().sharedMesh.vertices[0].y;
        var corners = new[] { _origin - side + lift, _origin + side + lift, end + side + lift, end - side + lift };
        Assert.That(warning.GetComponent<MeshFilter>().sharedMesh.vertices, Is.EqualTo(corners));
        Assert.That(warningRenderer.enabled, Is.True);

        _body.Bind(KingId, _origin + Vector3.right);
        Publish(BuriedKingPose.LungeTravel, end, 0.55f, 0.15f);
        Assert.That(warning.GetComponent<MeshFilter>().sharedMesh.vertices, Is.EqualTo(corners));
        Assert.That(warningRenderer.enabled, Is.True);
        Assert.That(dust.IsLive, Is.False);

        Publish(BuriedKingPose.LungeRecovery, end, 0.9f, landed ? 0.15f : 0f);
        Assert.That(fillRenderer.enabled, Is.EqualTo(landed));
        Assert.That(dust.IsLive, Is.EqualTo(landed));
        if (landed)
        {
            Assert.That(dust.transform.position, Is.EqualTo(end));
            Assert.That(warning.GetComponent<MeshFilter>().sharedMesh.vertices, Is.EqualTo(corners));
        }

        Publish(BuriedKingPose.Cleared, end, 0f, 0f);
        Assert.That(warningRenderer.enabled, Is.False);
        Assert.That(fillRenderer.enabled, Is.False);
        Assert.That(dust.IsLive, Is.False);
        _hub.Publish(new EnemyDespawned(KingId));
        Assert.That(warnings == null, Is.True);
    }

    private float Measure(Vector3 direction, Rect bounds)
    {
        Physics.SyncTransforms();
        var snapshot = new WorldSnapshot(1) { PlayerPosition = (_origin + direction * 10f).ToNum() };
        _views.CopyInto(snapshot, bounds);
        return snapshot.Enemies[0].KingLungeClearance;
    }

    private GameObject CoverAt(float x, float z, float length = 0.4f)
    {
        var cover = new GameObject("Leap test cover");
        _objects.Add(cover);
        cover.layer = LayerMask.NameToLayer(ArenaView.CoverLayerName);
        cover.transform.position = _origin + new Vector3(x, 1f, z);
        cover.AddComponent<BoxCollider>().size = new Vector3(0.4f, 2f, length);
        return cover;
    }

    private void Publish(BuriedKingPose pose, Vector3 end, float duration, float cut, float arc = 0f, float radius = 0f)
    {
        // Feed the actual view contract without duplicating the core state machine here.
        var change = (BuriedKingStateChanged)System.Activator.CreateInstance(typeof(BuriedKingStateChanged),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { KingId, pose, _origin.ToNum(), end.ToNum(), 4f, arc, default(System.Numerics.Vector3),
                radius, 0f, duration, 0f, cut }, null);
        _hub.Publish(change);
    }
}
