using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// RS-02a rules V1–V7: the Ranger's legs, its shot speed, its bowstring, and the three facts it
/// draws a bow from.
/// </summary>
/// <remarks>
/// <para>
/// <b>EditMode, on a controller built here</b>, as <c>PlayerAnimatorViewTests</c> does it: with a
/// controller assigned, an Animator's parameters round-trip outside play mode. The controller has
/// no upper-body layer, so the draw guard of rule V4 always answers "not drawing" and every
/// <see cref="PlayerAttacked"/> sets the trigger. The guard itself runs against the real
/// <c>AC_Ranger</c> in <c>RangerSandboxTests</c>.
/// </para>
/// <para>
/// <b>No row reads <c>Time.time</c>.</b> The view keeps its own clock, advanced by
/// <see cref="RangerAnimatorView.Step"/>, so the shot-speed rows are exact on any Editor.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RangerAnimatorViewTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>The player's body scale, Player.prefab's <c>Body</c>.</summary>
    private const float BodyScale = 0.6615215f;

    /// <summary>A shot's authored length, duplicated so the rows do not read the constant they pin.</summary>
    private const float AuthoredShotSeconds = 1.2f;

    private static readonly System.Numerics.Vector2 Facing = new System.Numerics.Vector2(0f, 1f);

    private static readonly int MoveXId = Animator.StringToHash("MoveX");
    private static readonly int MoveZId = Animator.StringToHash("MoveZ");
    private static readonly int MoveSpeedId = Animator.StringToHash("MoveSpeed");
    private static readonly int AimingId = Animator.StringToHash("Aiming");
    private static readonly int ShootId = Animator.StringToHash("Shoot");
    private static readonly int ReleaseId = Animator.StringToHash("Release");
    private static readonly int ShotSpeedId = Animator.StringToHash("ShotSpeed");

    private GameObject _body;
    private RangerAnimatorView _view;
    private Animator _animator;
    private AnimatorController _controller;
    private DomainEventHub _hub;

    [SetUp]
    public void CreateView()
    {
        _controller = BuildController();

        _body = new GameObject("Ranger");

        // RequireComponent brings PlayerView and its CharacterController. Its Velocity is zero,
        // since nothing applies an intent outside play mode.
        _view = _body.AddComponent<RangerAnimatorView>();

        _animator = _body.AddComponent<Animator>();
        _animator.runtimeAnimatorController = _controller;

        typeof(RangerAnimatorView).GetField("_animator", Private).SetValue(_view, _animator);

        _hub = new DomainEventHub();
    }

    [TearDown]
    public void DestroyView()
    {
        _hub?.Dispose();
        _hub = null;

        if (_body != null)
        {
            Object.DestroyImmediate(_body);
        }

        if (_controller != null)
        {
            Object.DestroyImmediate(_controller);
        }
    }

    // ---------------------------------------------------------------- V1: the legs

    [Test]
    public void Blend_StandingStillIsTheIdleAtAuthoredSpeed()
    {
        RangerAnimatorView.Blend(Vector3.zero, BodyScale, out float x, out float z, out float playback);

        Assert.That(x, Is.Zero);
        Assert.That(z, Is.Zero);
        Assert.That(playback, Is.EqualTo(1f));
    }

    [Test]
    public void Blend_AtTheStrideSpeedIsTheForwardRunAtAuthoredSpeed()
    {
        var velocity = new Vector3(0f, 0f, RangerAnimatorView.ForwardStrideSpeed * BodyScale);

        RangerAnimatorView.Blend(velocity, BodyScale, out float x, out float z, out float playback);

        Assert.That(x, Is.Zero.Within(1e-5f));
        Assert.That(z, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(playback, Is.EqualTo(1f).Within(1e-5f));
    }

    /// <summary>
    /// CC §2.5's 3 m/s on a body the clip's feet carry at 2.61 m/s: the direction stays on the
    /// circle and the clip plays faster, so the feet stay planted.
    /// </summary>
    [Test]
    public void Blend_FasterThanTheStrideSpeedsUpThePlayback()
    {
        RangerAnimatorView.Blend(new Vector3(0f, 0f, 3f), BodyScale, out float x, out float z, out float playback);

        Assert.That(x, Is.Zero.Within(1e-5f));
        Assert.That(z, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(playback, Is.EqualTo(3f / (RangerAnimatorView.ForwardStrideSpeed * BodyScale)).Within(1e-4f));
    }

    [Test]
    public void Blend_SidewaysIsTheStrafe()
    {
        RangerAnimatorView.Blend(new Vector3(3f, 0f, 0f), BodyScale, out float right, out float zRight, out float playback);
        RangerAnimatorView.Blend(new Vector3(-3f, 0f, 0f), BodyScale, out float left, out float zLeft, out _);

        Assert.That(right, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(left, Is.EqualTo(-1f).Within(1e-5f));
        Assert.That(zRight, Is.Zero.Within(1e-5f));
        Assert.That(zLeft, Is.Zero.Within(1e-5f));
        Assert.That(playback, Is.EqualTo(3f / (RangerAnimatorView.SideStrideSpeed * BodyScale)).Within(1e-4f));
    }

    [Test]
    public void Blend_BackwardsIsTheReversedRun()
    {
        RangerAnimatorView.Blend(new Vector3(0f, 0f, -3f), BodyScale, out float x, out float z, out _);

        Assert.That(x, Is.Zero.Within(1e-5f));
        Assert.That(z, Is.EqualTo(-1f).Within(1e-5f));
    }

    [Test]
    public void Blend_InsideTheCircleBlendsTowardTheIdle()
    {
        var velocity = new Vector3(0f, 0f, 0.5f * RangerAnimatorView.ForwardStrideSpeed * BodyScale);

        RangerAnimatorView.Blend(velocity, BodyScale, out _, out float z, out float playback);

        Assert.That(z, Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(playback, Is.EqualTo(1f));
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Blend_ADegenerateScaleReadsAsStandingStill(float scale)
    {
        RangerAnimatorView.Blend(new Vector3(3f, 0f, 3f), scale, out float x, out float z, out float playback);

        Assert.That(x, Is.Zero);
        Assert.That(z, Is.Zero);
        Assert.That(playback, Is.EqualTo(1f));
    }

    [Test]
    public void Blend_ANonFiniteVelocityReadsAsStandingStill()
    {
        RangerAnimatorView.Blend(new Vector3(float.NaN, 0f, 1f), BodyScale, out float x, out float z, out float playback);

        Assert.That(x, Is.Zero);
        Assert.That(z, Is.Zero);
        Assert.That(playback, Is.EqualTo(1f));
    }

    // ---------------------------------------------------------------- V2: Step

    [Test]
    public void Step_WritesTheLegsFromTheBody()
    {
        _animator.SetFloat(MoveXId, 0.7f);
        _animator.SetFloat(MoveZId, 0.7f);
        _animator.SetFloat(MoveSpeedId, 3f);

        _view.Step(0.016f);

        // The body has not been told to move, so the legs read standing still.
        Assert.That(_animator.GetFloat(MoveXId), Is.Zero);
        Assert.That(_animator.GetFloat(MoveZId), Is.Zero);
        Assert.That(_animator.GetFloat(MoveSpeedId), Is.EqualTo(1f));
    }

    [TestCase(0f)]
    [TestCase(-0.5f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Step_IgnoresADegenerateStep(float dt)
    {
        _animator.SetFloat(MoveSpeedId, 3f);

        _view.Step(dt);

        Assert.That(_animator.GetFloat(MoveSpeedId), Is.EqualTo(3f));
    }

    // ---------------------------------------------------------------- V3: the target

    [Test]
    public void Target_RaisesTheBowAndLowersIt()
    {
        _view.Construct(_hub);

        _hub.Publish(new TargetChanged(3, false, false, -1));

        Assert.That(_animator.GetBool(AimingId), Is.True);

        _hub.Publish(new TargetChanged(-1, false, false, -1));

        Assert.That(_animator.GetBool(AimingId), Is.False);
    }

    /// <summary>CC §3.6: a blocked target is still faced, and the bow is still raised at it.</summary>
    [Test]
    public void Target_ABlockedTargetStillRaisesTheBow()
    {
        _view.Construct(_hub);

        _hub.Publish(new TargetChanged(3, false, true, -1));

        Assert.That(_animator.GetBool(AimingId), Is.True);
    }

    // ---------------------------------------------------------------- V4: the draw

    [Test]
    public void Attack_DrawsTheBow()
    {
        _view.Construct(_hub);

        _hub.Publish(new PlayerAttacked(Facing));

        Assert.That(_animator.GetBool(ShootId), Is.True);
    }

    [Test]
    public void Attack_TheFirstShotLeavesTheShotSpeed()
    {
        _view.Construct(_hub);
        _animator.SetFloat(ShotSpeedId, 1.7f);

        _hub.Publish(new PlayerAttacked(Facing));

        Assert.That(_animator.GetFloat(ShotSpeedId), Is.EqualTo(1.7f));
    }

    /// <summary>Half a second between shots: the 1.2 s shot plays at 2.4×.</summary>
    [Test]
    public void Attack_TheShotSpeedFollowsTheGapOnTheViewsOwnClock()
    {
        _view.Construct(_hub);

        _hub.Publish(new PlayerAttacked(Facing));
        _view.Step(0.25f);
        _view.Step(0.25f);
        _hub.Publish(new PlayerAttacked(Facing));

        Assert.That(_animator.GetFloat(ShotSpeedId), Is.EqualTo(AuthoredShotSeconds / 0.5f).Within(1e-4f));
    }

    /// <summary>
    /// V3: three seconds with the bow down is a run, not a fire rate. The first shot after it keeps
    /// the last volley's speed, and the next one measures again.
    /// </summary>
    [Test]
    public void Target_LoweringTheBowEndsTheVolley()
    {
        _view.Construct(_hub);

        _hub.Publish(new TargetChanged(1, false, false, -1));
        _hub.Publish(new PlayerAttacked(Facing));
        _view.Step(0.5f);
        _hub.Publish(new PlayerAttacked(Facing));

        _hub.Publish(new TargetChanged(-1, false, false, -1));
        _view.Step(3f);
        _hub.Publish(new TargetChanged(1, false, false, -1));
        _hub.Publish(new PlayerAttacked(Facing));

        Assert.That(_animator.GetFloat(ShotSpeedId), Is.EqualTo(AuthoredShotSeconds / 0.5f).Within(1e-4f));

        _view.Step(1f);
        _hub.Publish(new PlayerAttacked(Facing));

        Assert.That(_animator.GetFloat(ShotSpeedId), Is.EqualTo(AuthoredShotSeconds / 1f).Within(1e-4f));
    }

    [Test]
    public void ShotSpeed_FitsTheShotToTheInterval()
    {
        Assert.That(RangerAnimatorView.ShotSpeed(AuthoredShotSeconds), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(RangerAnimatorView.ShotSpeed(0.6f), Is.EqualTo(2f).Within(1e-5f));
        Assert.That(RangerAnimatorView.ShotSpeed(1f), Is.EqualTo(1.2f).Within(1e-5f));
    }

    /// <summary>A slow bow holds at full draw; it does not draw in slow motion.</summary>
    [Test]
    public void ShotSpeed_IsNeverSlowerThanAuthored()
    {
        Assert.That(RangerAnimatorView.ShotSpeed(3f), Is.EqualTo(1f));
    }

    [TestCase(0.1f)]
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void ShotSpeed_IsCappedAndADegenerateIntervalIsTheCap(float interval)
    {
        Assert.That(RangerAnimatorView.ShotSpeed(interval), Is.EqualTo(RangerAnimatorView.MaxShotSpeed));
    }

    // ---------------------------------------------------------------- V5: the release

    [Test]
    public void Fired_ByThePlayerReleasesAndDropsAPendingDraw()
    {
        _view.Construct(_hub);
        _hub.Publish(new PlayerAttacked(Facing));

        _hub.Publish(Shot(sourceId: 0));

        Assert.That(_animator.GetBool(ReleaseId), Is.True);
        Assert.That(_animator.GetBool(ShootId), Is.False, "The draw still pending is dropped with the release.");
    }

    [Test]
    public void Fired_ByAnEnemyIsNotTheBowsRelease()
    {
        _view.Construct(_hub);

        _hub.Publish(Shot(sourceId: 7));

        Assert.That(_animator.GetBool(ReleaseId), Is.False);
    }

    // ---------------------------------------------------------------- V6: the string

    [Test]
    public void StringPull_RestsUntilTheArrowIsNocked()
    {
        Assert.That(RangerAnimatorView.StringPull(true, false, 0f), Is.Zero);
        Assert.That(RangerAnimatorView.StringPull(true, false, RangerAnimatorView.NockedNormalized), Is.Zero);
    }

    [Test]
    public void StringPull_FollowsTheHandToFullDraw()
    {
        float middle = (RangerAnimatorView.NockedNormalized + RangerAnimatorView.FullDrawNormalized) * 0.5f;

        Assert.That(RangerAnimatorView.StringPull(true, false, middle), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(RangerAnimatorView.StringPull(true, false, RangerAnimatorView.FullDrawNormalized), Is.EqualTo(1f));
        Assert.That(RangerAnimatorView.StringPull(true, false, 0.95f), Is.EqualTo(1f));
    }

    [Test]
    public void StringPull_IsHeldWhileAimingAndGoneOtherwise()
    {
        Assert.That(RangerAnimatorView.StringPull(false, true, 0.3f), Is.EqualTo(1f));
        Assert.That(RangerAnimatorView.StringPull(false, false, 0.3f), Is.Zero);
    }

    // ---------------------------------------------------------------- V7: guards

    [Test]
    public void Construct_ANullHubThrows()
    {
        Assert.That(() => _view.Construct(null), Throws.ArgumentNullException);
    }

    [Test]
    public void Construct_SubscribesToTheThreeFacts()
    {
        _view.Construct(_hub);

        Assert.That(_hub.SubscriberCount<PlayerAttacked>(), Is.EqualTo(1));
        Assert.That(_hub.SubscriberCount<TargetChanged>(), Is.EqualTo(1));
        Assert.That(_hub.SubscriberCount<ProjectileFired>(), Is.EqualTo(1));
    }

    private static ProjectileFired Shot(int sourceId) =>
        new ProjectileFired(
            1,
            new ContentId("sandbox.ranger"),
            sourceId,
            System.Numerics.Vector3.Zero,
            new System.Numerics.Vector3(0f, 0f, 8f),
            0.3f);

    private static AnimatorController BuildController()
    {
        var controller = new AnimatorController
        {
            name = "AC_RangerTestDouble",

            // Never written to disk, and gone with the fixture.
            hideFlags = HideFlags.HideAndDontSave,
        };

        controller.AddLayer("Base Layer");

        controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
        controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
        controller.AddParameter("MoveSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Release", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("ShotSpeed", AnimatorControllerParameterType.Float);

        return controller;
    }
}
