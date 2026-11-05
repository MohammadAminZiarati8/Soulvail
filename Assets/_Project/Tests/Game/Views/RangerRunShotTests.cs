using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using V2 = System.Numerics.Vector2;
using V3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class RangerRunShotTests
{
    private const string BodyPath = "Assets/_Project/Prefabs/Player/Bodies/Ranger.prefab";
    private GameObject _root;
    private PlayerView _player;
    private RangerAnimatorView _view;
    private Animator _animator;
    private DomainEventHub _hub;
    private int _upper;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("Ranger run shot");
        _player = _root.AddComponent<PlayerView>();
        _player.Teleport(Vector3.zero);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);
        GameObject body = Object.Instantiate(prefab, _root.transform);
        _view = body.GetComponentInChildren<RangerAnimatorView>();
        _animator = body.GetComponentInChildren<Animator>();
        _hub = new DomainEventHub();
        using (var serialized = new SerializedObject(_view))
        {
            // These pools have separate fixtures. Only the real rig and controller are needed here.
            serialized.FindProperty("_rainPrefab").objectReferenceValue = null;
            serialized.FindProperty("_snarePrefab").objectReferenceValue = null;
            serialized.FindProperty("_killshotPrefab").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        _view.Construct(_hub);
        _animator.Rebind();
        _animator.Update(0f);
        _upper = _animator.GetLayerIndex(RangerAnimatorView.UpperBodyLayer);
        Assert.That(_upper, Is.GreaterThan(0));
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_root);
        _hub.Dispose();
    }

    [Test]
    public void RunningShot_BowStaysUpWhileMoving()
    {
        PointAtTarget();
        Advance(0.2f);
        StartCoreMovement(runningShot: true);

        Assert.That(_animator.GetBool("Aiming"), Is.True);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(_upper).IsName("Empty"), Is.False);
    }

    [Test]
    public void NoRunningShot_BowLowersAsToday()
    {
        PointAtTarget();
        Advance(0.2f);
        StartCoreMovement(runningShot: false);
        Advance(0.3f);
        Advance(0.3f);

        Assert.That(_animator.GetBool("Aiming"), Is.False);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(_upper).IsName("Empty"), Is.True);
        Assert.That(_animator.GetLayerWeight(_upper), Is.EqualTo(1f));
    }

    [TestCase(0f, "Running_HoldingBow", 1f)]
    [TestCase(90f, "Running_Strafe_Right", 1f)]
    [TestCase(-90f, "Running_Strafe_Left", 1f)]
    [TestCase(180f, "Running_HoldingBow", -1f)]
    public void Legs_PickClipByAngle(float angle, string clip, float speed)
    {
        // Core supplies world velocity and facing independently. Face +X to exercise the
        // conversion to local space as well as the four authored blend-tree positions.
        Vector3 velocity = Quaternion.Euler(0f, 90f + angle, 0f) * Vector3.forward * 5f;
        Move(new V3(velocity.x, 0f, velocity.z), V3.UnitX);
        Advance(0.25f);

        Assert.That(Vector3.Dot(_root.transform.forward, Vector3.right), Is.GreaterThan(0.999f));
        var controller = (AnimatorController)_animator.runtimeAnimatorController;
        var locomotion = (BlendTree)controller.layers[0].stateMachine.defaultState.motion;
        var direction = new Vector2(_animator.GetFloat("MoveX"), _animator.GetFloat("MoveZ"));
        Assert.That(locomotion.blendType, Is.EqualTo(BlendTreeType.FreeformDirectional2D));
        Assert.That(System.Array.Exists(locomotion.children, child =>
            Vector2.Distance(child.position, direction) < 0.001f &&
            child.motion.name == clip && child.timeScale == speed), Is.True);
    }

    [Test]
    public void Release_WhileMoving_UpperLayerOnly()
    {
        Move(V3.UnitX * 5f, V3.UnitZ);
        Draw();
        float legPhase = _animator.GetCurrentAnimatorStateInfo(0).normalizedTime;

        Fire();
        Advance(0.06f);

        Assert.That(_animator.GetCurrentAnimatorStateInfo(_upper).IsName("Release"), Is.True);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), Is.True);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(legPhase));
        Assert.That(_animator.GetLayerWeight(_upper), Is.GreaterThan(0f));
    }

    [Test]
    public void Release_Standing_AsToday()
    {
        Draw();
        Fire();
        Advance(0.06f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(_upper).IsName("Release"), Is.True);

        Advance(0.15f);
        Assert.That(_animator.GetLayerWeight(_upper), Is.EqualTo(1f));
    }

    [Test]
    public void UpperLayer_FadesAfterLastRelease()
    {
        Move(-V3.UnitZ * 5f, V3.UnitZ);
        Draw();
        Fire();
        Advance(0.07f);
        Assert.That(_animator.GetLayerWeight(_upper), Is.InRange(0.01f, 0.99f));

        Advance(0.08f);
        Assert.That(_animator.GetLayerWeight(_upper), Is.Zero.Within(0.0001f));
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), Is.True);

        Draw();
        Assert.That(_animator.GetLayerWeight(_upper), Is.EqualTo(1f));
    }

    [Test]
    public void Roll_OverridesUpperLayer()
    {
        Move(V3.UnitX * 5f, V3.UnitZ);
        Draw();
        Fire();
        Advance(0.06f);

        _hub.Publish(new ChargeStarted(V2.UnitX));
        Assert.That(_animator.GetLayerWeight(_upper), Is.Zero);
        Advance(0.09f);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Dodge"), Is.True);

        _hub.Publish(new ChargeEnded());
        Advance(0.02f);
        Assert.That(_animator.GetLayerWeight(_upper), Is.Zero);
    }

    private void StartCoreMovement(bool runningShot)
    {
        var ranger = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(
            "Assets/_Project/Data/Characters/Ranger.asset").ToSpec();
        var husk = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(
            "Assets/_Project/Data/Enemies/Husk.asset").ToSpec();
        var core = new PlayerCombat(ranger, _hub, new RecordingIntents(), 8);
        if (runningShot)
        {
            core.FireWhileMoving.Add(new Modifier(ModifierKind.Flat, 1f, this));
        }

        var enemies = new EnemyRegistry(8);
        EnemyAgent target = enemies.Spawn(husk, V3.UnitZ * 8f);
        var snapshot = new WorldSnapshot(8)
        {
            Dt = 0.02f,
            MoveInput = V2.UnitX,
            PlayerVelocity = V3.UnitX * 5f,
        };
        snapshot.AddEnemy() = new EnemySense { Id = target.Id, Position = target.Position, HasLineOfSight = true };
        Move(snapshot.PlayerVelocity, V3.UnitZ);
        core.Tick(snapshot.Dt, snapshot.Dt, snapshot, enemies.Alive, V3.UnitZ);
        Assert.That(core.IsHoldingFire, Is.EqualTo(!runningShot));
        Advance(0.06f);
    }

    private void PointAtTarget() => _hub.Publish(new TargetChanged(1, false, false, -1));

    private void Draw()
    {
        Advance(0.02f);
        PointAtTarget();
        _hub.Publish(new PlayerAttacked(V2.UnitY, WeaponKind.Projectile, 0.4f));
        Advance(0.18f);
        var state = _animator.GetCurrentAnimatorStateInfo(_upper);
        Assert.That(state.IsName("Draw") || state.IsName("Aim"), Is.True);
    }

    private void Fire() => _hub.Publish(new ProjectileFired(
        1, new ContentId("character.ranger"), 0, V3.Zero, V3.UnitZ * 8f, 0.3f));

    private void Move(V3 velocity, V3 facing) => _player.Apply(new PlayerMoveIntent(velocity, facing), 0.02f);

    private void Advance(float seconds)
    {
        _view.Step(seconds);
        _animator.Update(0f);
        _animator.Update(seconds);
    }
}
