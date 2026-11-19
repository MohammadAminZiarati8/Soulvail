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
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;
using Num = System.Numerics;

namespace Soulvail.Tests.Game.Views;

public sealed class ShardbackViewTests
{
    private const string Project = "Assets/_Project/";
    private const string Dunes = Project + "Prefabs/Arenas/Sunscar/";
    private const int Boss = 7;
    private readonly Vector3 _origin = new(2f, 0f, -3f);
    private readonly ContentId _spec = new("enemy.shardback");
    private GameObject _root;
    private DomainEventHub _events;
    private ShardbackView _view;
    private EnemyView _body;
    private MeshFilter _mesh;
    private MeshRenderer _tell;
    private MeshFilter _fill;
    private MeshFilter _effect;
    private AudioSource _audio;
    private Animator _animator;
    private Guid _lifetime;

    [SetUp]
    public void SetUp()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Project + "Prefabs/Enemies/Shardback.prefab");
        Assert.That(prefab, Is.Not.Null);
        _root = Object.Instantiate(prefab);
        _events = new DomainEventHub();
        _body = _root.GetComponent<EnemyView>();
        _body.Bind(Boss, _origin);
        _view = _root.GetComponent<ShardbackView>();
        _view.Construct(_events);
        _animator = _root.GetComponentInChildren<Animator>();
        _audio = _root.GetComponent<AudioSource>();
        _mesh = _root.transform.Find("AttackTell").GetComponent<MeshFilter>();
        _tell = _mesh.GetComponent<MeshRenderer>();
        _fill = _root.transform.Find("AttackFill").GetComponent<MeshFilter>();
        _effect = _root.transform.Find("AttackEffect").GetComponent<MeshFilter>();
        _lifetime = Guid.NewGuid();
        _events.Publish(new ShardbackFightStarted(Boss, _lifetime));
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_root);
        _events?.Dispose();
    }

    [Test]
    public void Prefab_BindsRigControllerAndCues()
    {
        Assert.That(_animator.avatar.isValid, Is.True);
        Assert.That(_animator.applyRootMotion, Is.False);
        Assert.That(_audio.playOnAwake, Is.False);
        Assert.That(_audio.spatialBlend, Is.EqualTo(1f));
        Assert.That(_animator.GetComponentsInChildren<SkinnedMeshRenderer>().Select(s => s.name),
            Is.EquivalentTo(new[] { "Shardback_Body", "Shell_L", "Shell_R" }));
        var controller = (AnimatorController)_animator.runtimeAnimatorController;
        Assert.That(controller.layers[0].stateMachine.states.Select(s => s.state.motion.name),
            Is.EquivalentTo(new[] { "Idle", "Walk", "FanWindup", "FanRelease", "ChargeWindup",
                "ChargeTravel", "BurstWindup", "BurstRelease", "Recovery", "Death" }));
        var cues = new HashSet<AudioClip>();
        foreach (ShardbackMove move in Enum.GetValues(typeof(ShardbackMove)))
        {
            Arm(move, (ulong)cues.Count + 1);
            Assert.That(_audio.clip.name, Is.EqualTo("SFX_Shardback" + move));
            Assert.That(_audio.clip.length, Is.InRange(0.5f, move == ShardbackMove.Fan ? 1f : 1.2f));
            Assert.That(cues.Add(_audio.clip), Is.True);
        }
    }

    [TestCase(ShardbackMove.Fan)]
    [TestCase(ShardbackMove.Charge)]
    [TestCase(ShardbackMove.Burst)]
    public void Tells_MatchLockedCoreGeometry(ShardbackMove move)
    {
        _root.transform.localScale = Vector3.one * 1.7f;
        ShardbackArmed armed = Arm(move, 1);
        _body.Apply(new EnemyMoveIntent(Boss, Num.Vector3.Zero, armed.Facing), 0f);
        _view.Step(0f);
        AssertPoint(_animator.transform.forward, Vector3.right);
        Assert.That(EffectVisible, Is.False);
        Assert.That(_tell.enabled, Is.EqualTo(move == ShardbackMove.Burst));
        _events.Publish(new ShardbackClipped(Boss, armed.Key, 5f, 7f, 3f));
        Assert.That(_tell.enabled, Is.True);
        Assert.That(FillVisible, Is.False);
        Vector3[] vertices = _mesh.sharedMesh.vertices;
        Vector3 origin = _origin + Vector3.up * 0.08f;
        if (move == ShardbackMove.Burst)
        {
            Assert.That(_mesh.sharedMesh.triangles.Length, Is.EqualTo(64 * 3));
            AssertPoint(_mesh.transform.TransformPoint(vertices[0]), origin);
            foreach (Vector3 point in vertices.Skip(1))
            {
                Vector3 world = _mesh.transform.TransformPoint(point);
                Assert.That(Vector3.Distance(world, origin), Is.EqualTo(5.5f).Within(0.001f));
            }
        }
        else
        {
            int laneCount = move == ShardbackMove.Fan ? 3 : 1;
            Assert.That(_mesh.sharedMesh.triangles.Length, Is.EqualTo(laneCount * 6));
            float[] angles = { 0f, 25f, -25f };
            float[] lengths = { 5f, 7f, 3f };
            for (int lane = 0; lane < laneCount; lane++)
            {
                // With a +X cast, Core's positive XZ rotation points toward +Z.
                float radians = angles[lane] * Mathf.Deg2Rad;
                Vector3 bearing = new(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
                Vector3 start = WorldMidpoint(vertices, lane * 4);
                Vector3 end = WorldMidpoint(vertices, lane * 4 + 2);
                AssertPoint(start, origin);
                AssertPoint(end, origin + bearing * lengths[lane]);
                Assert.That(Vector3.Distance(_mesh.transform.TransformPoint(vertices[lane * 4]),
                    _mesh.transform.TransformPoint(vertices[lane * 4 + 1])), Is.EqualTo(2f * armed.HalfWidth).Within(0.001f));
            }
        }

        _events.Publish(new ShardbackTellProgress(Boss, new ShardbackProbeKey(_lifetime, 2), 0.75f));
        Assert.That(FillVisible, Is.False);
        _events.Publish(new ShardbackTellProgress(Boss, armed.Key, 0.5f));
        Assert.That(FillVisible, Is.True);
        Vector3[] filled = _fill.sharedMesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (move == ShardbackMove.Burst)
                AssertPoint(filled[i], vertices[i] * 0.5f);
            else if (i % 4 < 2)
                AssertPoint(filled[i], vertices[i]);
            else
                AssertPoint(filled[i], (vertices[i] + vertices[i - 2]) * 0.5f);
        }
        Assert.That(_fill.sharedMesh.colors[0].a, Is.GreaterThan(_mesh.sharedMesh.colors[0].a));
        Assert.That(_mesh.sharedMesh.vertices, Is.EqualTo(vertices));

        Vector3 before = _mesh.transform.TransformPoint(vertices[1]);
        _root.transform.SetPositionAndRotation(new Vector3(9f, 0f, 5f), Quaternion.Euler(0f, 65f, 0f));
        _view.Step(5f);
        Assert.That(_tell.enabled, Is.True, "Only Core ends a warning.");
        AssertPoint(_mesh.transform.TransformPoint(vertices[1]), before);
        Assert.That(_fill.sharedMesh.vertices, Is.EqualTo(filled), "Only the Core clock advances the fill.");
        Assert.That(EffectVisible, Is.False);
        _events.Publish(new ShardbackTellProgress(Boss, armed.Key, 1f));
        Assert.That(_fill.sharedMesh.vertices, Is.EqualTo(vertices));

        if (move == ShardbackMove.Charge)
        {
            _events.Publish(new ShardbackChargeStarted(Boss, armed.Key));
            Assert.That(_tell.enabled, Is.False);
            Assert.That(FillVisible, Is.False);
            Assert.That(EffectVisible, Is.True);
            Assert.That(_effect.sharedMesh.vertices[2].z, Is.Zero);
            _events.Publish(new ShardbackChargeAdvanced(Boss, armed.Key, 2f));
            Assert.That(_effect.sharedMesh.vertices[2].z, Is.EqualTo(2f));
            _events.Publish(new ShardbackChargeAdvanced(Boss, armed.Key, 6f));
            Assert.That(_effect.sharedMesh.vertices[2].z, Is.EqualTo(2f));
        }
        float travel = move == ShardbackMove.Charge ? 2f : 0f;
        _events.Publish(new ShardbackImpact(Boss, armed.Move, armed.Origin, 2f, armed.Key, travel));
        Assert.That(_tell.enabled, Is.False);
        Assert.That(FillVisible, Is.False);
        Assert.That(EffectVisible, Is.True);
        Vector3[] release = _effect.sharedMesh.vertices;
        if (move == ShardbackMove.Charge)
            Assert.That(release[2].z, Is.EqualTo(2f));
        else if (move == ShardbackMove.Fan)
        {
            Assert.That(release, Has.Length.EqualTo(72));
            Assert.That(release.All(v => v.y > 0f), Is.True);
            _view.Step(0.25f);
            Vector3[] fired = _effect.sharedMesh.vertices;
            float[] angles = { 0f, 25f, -25f };
            float[] lengths = { 5f, 7f, 3f };
            for (int lane = 0; lane < 3; lane++)
            {
                float radians = angles[lane] * Mathf.Deg2Rad;
                Vector3 direction = new(-Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                var laneVertices = fired.Skip(lane * 24).Take(24).ToArray();
                Assert.That(laneVertices.All(v => Vector3.Dot(v, direction) <= lengths[lane] + 0.001f), Is.True);
                Assert.That(Vector3.Dot(laneVertices[1], direction), Is.EqualTo(lengths[lane]).Within(0.001f));
                Assert.That(Vector3.Dot(fired[lane * 24 + 1] - release[lane * 24 + 1], direction), Is.GreaterThan(1f));
            }
        }
        else
        {
            Assert.That(release, Has.Length.EqualTo(128));
            Assert.That(release.All(v => new Vector2(v.x, v.z).magnitude > 0f), Is.True);
            _view.Step(0.35f);
            float outer = _effect.sharedMesh.vertices.Max(v => new Vector2(v.x, v.z).magnitude);
            Assert.That(outer, Is.EqualTo(armed.Reach).Within(0.001f));
            Assert.That(outer, Is.GreaterThan(release.Max(v => new Vector2(v.x, v.z).magnitude)));
        }
        _body.Apply(new EnemyMoveIntent(Boss, Num.Vector3.Zero, Num.Vector2.UnitY), 0f);
        _view.Step(0.01f);
        AssertPoint(_animator.transform.forward, Vector3.forward);
        AssertPoint(_effect.transform.forward, Vector3.right);
        _view.Step(0.5f);
        Assert.That(EffectVisible, Is.False);
    }

    [Test]
    public void Playback_FollowsCastAndRecovery()
    {
        ShardbackArmed charge = Arm(ShardbackMove.Charge, 1);
        AssertPose("ChargeWindup");
        _events.Publish(new ShardbackClipped(Boss, charge.Key, 6f, 0f, 0f));
        _events.Publish(new ShardbackChargeStarted(Boss, charge.Key));
        Assert.That(_tell.enabled, Is.False);
        AssertPose("ChargeTravel");
        _events.Publish(new ShardbackImpact(Boss, charge.Move, charge.Origin, 3f, charge.Key));
        AssertPose("Recovery");
        Assert.That(_animator.speed, Is.EqualTo(2f / 3f).Within(0.001f));
        _view.Step(2.9f);
        AssertPose("Recovery");
        _view.Step(0.2f);
        AssertPose("Idle");

        ShardbackArmed burst = Arm(ShardbackMove.Burst, 2);
        AssertPose("BurstWindup");
        _events.Publish(new ShardbackImpact(Boss, burst.Move, burst.Origin, 2f, burst.Key));
        AssertPose("BurstRelease");
        _view.Step(0.4f);
        AssertPose("Recovery");
        Assert.That(_animator.speed, Is.EqualTo(2f / 1.6f).Within(0.001f));
    }

    [Test]
    public void Cancellation_ClearsCastAndRejectsStaleEvents()
    {
        ShardbackArmed burst = Arm(ShardbackMove.Burst, 1);
        _events.Publish(new ShardbackTellProgress(Boss, burst.Key, 0.5f));
        Assert.That(FillVisible, Is.True);
        _events.Publish(new BossPhaseChanged(Boss + 1, 1, 3));
        Assert.That(_tell.enabled, Is.True);
        _events.Publish(new BossPhaseChanged(Boss, 1, 3));
        Assert.That(_tell.enabled, Is.False);
        Assert.That(FillVisible, Is.False);
        _events.Publish(new ShardbackTellProgress(Boss, burst.Key, 1f));
        Assert.That(FillVisible, Is.False);
        Assert.That(_audio.clip, Is.Null);
        _events.Publish(burst);
        _events.Publish(new ShardbackImpact(Boss, burst.Move, burst.Origin, 2f, burst.Key));
        AssertPose("Idle");

        burst = Arm(ShardbackMove.Burst, 2);
        _events.Publish(new ShardbackImpact(Boss, burst.Move, burst.Origin, 2f, burst.Key));
        Assert.That(EffectVisible, Is.True);
        _events.Publish(new EnemyDied(Boss, _spec, _origin.ToNum()));
        Assert.That(_tell.enabled, Is.False);
        Assert.That(EffectVisible, Is.False);
        Assert.That(_audio.clip, Is.Null);
        AssertPose("Death");
        _view.OnDespawn();
        _root.SetActive(false);
        _root.SetActive(true);
        _events.Publish(burst);
        Assert.That(_tell.enabled, Is.False);
        Assert.That(_audio.clip, Is.Null);
        AssertPose("Idle");
    }

    [Test]
    public void AuthoredPillars_BreakAndRestoreInFreshFight()
    {
        var cover = AssetDatabase.LoadAssetAtPath<GameObject>(Dunes + "ShardbackPillars.prefab");
        var arena = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Dunes + "Arena_Dunes.prefab"));
        arena.transform.position = new Vector3(3000f, 0f, 3000f);
        try
        {
            ExerciseFight(cover, arena.GetComponent<ArenaView>());
        }
        finally
        {
            Object.DestroyImmediate(arena);
            Physics.SyncTransforms();
        }
    }

    private void ExerciseFight(GameObject cover, ArenaView arena)
    {
        Assert.That(cover, Is.Not.Null);
        var looks = new EnemyLookBook(new Dictionary<ContentId, EnemyLook>
        {
            [_spec] = new EnemyLook(Color.white, 1f, cover: cover),
        });
        var character = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(Project + "Data/Characters/Ranger.asset").ToSpec();
        var sunscar = AssetDatabase.LoadAssetAtPath<ModeDefinition>(Project + "Data/Modes/Sunscar.asset").ToSpec();
        var enemy = new EnemySpec(_spec, new LocKey("enemy.shardback.name"),
            500f, 1.8f, 8, 40, 300f, false, 12f, 3.2f, 1f, 0.8f, 40f, EnemyBehaviourKind.Static);
        using var covers = new CoverViews(looks, _events, arena.transform);
        using IObjectResolver container = new ContainerBuilder().Build();
        using var arenas = new ArenaPool(container, Array.Empty<ArenaView>(), null, _events, _root.AddComponent<PlayerView>());
        int coverMask = LayerMask.GetMask("Cover");
        var probes = new ShardbackProbeAdapter(coverMask, arenas);
        Vector3 entry = arena.transform.position + new Vector3(0f, 0f, 2f);
        var intents = new RecordingIntents();
        var player = new PlayerCombat(character, _events, intents, 8);
        var enemies = new EnemySystem(new ContentCatalog(new[] { character }, new[] { enemy }),
            _events, new FixedRandom(0.5f), new DepthScaling(sunscar.Scaling), 8);
        EnemyAgent agent = enemies.Registry.Spawn(enemy, entry.ToNum());
        var projectiles = new ProjectileSystem(_events, 8);
        var behaviour = new ShardbackBehaviour(agent, new ShardbackStats());
        _body.Unbind();
        _body.Bind(agent.Id, entry);
        _events.Publish(new EnemySpawned(agent.Id, _spec, entry.ToNum()));
        ShardbackPillar[] slots = arena.GetComponentsInChildren<ShardbackPillar>().OrderBy(p => p.Slot).ToArray();
        Assert.That(slots.Select(p => p.Slot), Is.EqualTo(new[] { 0, 1, 2 }));
        var blockers = slots.Select(p => p.GetComponent<CapsuleCollider>()).ToArray();
        var rubble = slots.Select(p => p.transform.Find("Broken rubble").gameObject).ToArray();
        var ray = new Ray(entry + Vector3.up * 1.1f, slots[0].transform.position - entry);
        foreach (GameObject fragments in rubble)
            Assert.That(fragments.GetComponentsInChildren<Collider>(true), Is.Empty);
        Vector3 direction = ray.direction.normalized;
        agent.Blackboard.PlayerPosition = (entry + direction * 8f).ToNum();
        Physics.SyncTransforms();
        Assert.That(Physics.Raycast(ray, out RaycastHit before, 6f, coverMask), Is.True);
        Assert.That(before.collider, Is.EqualTo(blockers[0]));

        const float step = 0.05f;
        int frame;
        for (frame = 0; frame < 160 && !slots[0].IsBroken; frame++)
        {
            agent.Blackboard.SelfPosition = _body.Position.ToNum();
            intents.Clear();
            behaviour.Tick(new EnemyTickContext(step, frame * step, player, intents, _events, projectiles, enemies));
            _body.Apply(intents.LastEnemyMove, step);
            Physics.SyncTransforms();
            foreach (LaneProbeIntent lane in intents.LaneProbes)
                behaviour.OnShardbackStop(probes.Probe(lane, arena.PlayBounds));
        }
        Assert.That(slots.Select(p => p.IsBroken), Is.EqualTo(new[] { true, false, false }));
        Assert.That(rubble.Select(r => r.activeSelf), Is.EqualTo(new[] { true, false, false }));
        Assert.That(Physics.Raycast(ray, 6f, coverMask), Is.False);
        Guid firstLifetime = slots[0].Lifetime;
        _events.Publish(new BossPhaseChanged(agent.Id, 1, 3));
        Assert.That(slots[0].IsBroken, Is.True);
        Assert.That(rubble[0].activeSelf, Is.True);

        _events.Publish(new EnemyDespawned(agent.Id));
        _events.Publish(new EnemySpawned(agent.Id, _spec, entry.ToNum()));
        var fresh = new ShardbackBehaviour(agent, new ShardbackStats());
        fresh.Tick(new EnemyTickContext(0f, frame * step, player, intents, _events, projectiles, enemies));
        Physics.SyncTransforms();
        Assert.That(slots[0].Lifetime, Is.Not.EqualTo(firstLifetime));
        Assert.That(slots.Select(p => p.IsBroken), Is.All.False);
        Assert.That(rubble.Select(r => r.activeSelf), Is.All.False);
        Assert.That(Physics.Raycast(ray, out RaycastHit restored, 6f, coverMask), Is.True);
        Assert.That(restored.collider, Is.EqualTo(blockers[0]));
    }

    private ShardbackArmed Arm(ShardbackMove move, ulong cast)
    {
        var evt = new ShardbackArmed(Boss, move, _origin.ToNum(), Num.Vector2.UnitX,
            move == ShardbackMove.Burst ? 5.5f : 12f, move == ShardbackMove.Fan ? 0.6f : 1f,
            25f, move == ShardbackMove.Fan ? 1f : 1.2f, new ShardbackProbeKey(_lifetime, cast));
        _events.Publish(evt);
        return evt;
    }

    private Vector3 WorldMidpoint(Vector3[] vertices, int index) =>
        _mesh.transform.TransformPoint((vertices[index] + vertices[index + 1]) * 0.5f);

    private bool EffectVisible => _effect.GetComponent<MeshRenderer>().enabled;
    private bool FillVisible => _fill.GetComponent<MeshRenderer>().enabled;

    private static void AssertPoint(Vector3 actual, Vector3 expected) =>
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.001f));

    private void AssertPose(string state)
    {
        _animator.Update(0f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName(state), Is.True, state);
    }
}
