using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Soulvail.Game.Views;

/// <summary>Poses the King, presents his locked warnings, and moves the sword through its flight.</summary>
internal sealed class BuriedKingView : IDisposable
{
    private const int ArcSegments = 48;
    private const float FloorOffset = 0.06f;
    // The KayKit sword's origin is in the grip. Its tip is 1.96 m above the origin.
    private const float SwordScale = 0.88f;
    private const float GripToTip = 1.96f * SwordScale;
    private const float GripToPommel = 0.40f * SwordScale;
    private const float FlightHeight = 0.8f;
    private const float HandBlendDistance = 1.35f;
    private const float LandingBlendDistance = 0.85f;
    private const float WalkCycleDistance = 1.15f;
    private const float RunCycleDistance = 1.8f;
    private const float ImpactRingSeconds = 0.34f;
    private const float SupportGripSpacing = 0.24f;

    private static readonly LungeStance LungeGuard = new(
        new(0.10f, 0.81f, 0.28f), new(0.20f, 0.84f, 0.48f),
        new(0f, -0.06f, 0f), 0.09f, -0.09f, 0f, 5f, 4f);
    private static readonly LungeStance LungeLoaded = new(
        new(0.23f, 1.10f, -0.13f), new(0.31f, 0.57f, -0.76f),
        new(0.035f, -0.12f, -0.095f), 0.17f, -0.18f, 0f, 22f, -6f);
    private static readonly LungeStance LungeGlide = new(
        new(0.26f, 1.04f, 0.05f), new(0.24f, 0.78f, -0.58f),
        new(0f, -0.15f, 0.025f), 0.26f, -0.32f, 0.07f, 14f, 15f);
    private static readonly LungeStance LungePlant = new(
        new(-0.13f, 0.72f, 0.37f), new(-0.64f, -0.44f, 0.63f),
        new(-0.012f, -0.17f, 0.13f), 0.29f, -0.29f, 0f, -25f, 22f);
    private static readonly LungeStance LungeFollowThrough = new(
        new(-0.22f, 0.71f, 0.25f), new(-0.81f, -0.42f, 0.39f),
        new(-0.022f, -0.17f, 0.105f), 0.29f, -0.29f, 0f, -31f, 21f);

    private static readonly Stance Rest = new(
        new(0.29f, -0.94f, -0.18f), new(0.02f, -0.29f, 0.96f), new(0.25f, 0.90f, 0.35f), 3f, 2f);
    private static readonly Stance SweepReady = new(
        new(0.76f, -0.46f, -0.45f), new(0.33f, 0.36f, 0.86f), new(0.96f, 0.23f, 0.16f), 32f, -3f);
    private static readonly Stance SweepStrike = new(
        new(0.03f, -0.31f, 0.95f), new(-0.24f, 0.04f, 0.97f), new(-0.97f, 0.10f, 0.22f), -37f, 8f);
    private static readonly Stance CastReady = new(
        new(0.66f, 0.36f, -0.65f), new(-0.07f, 0.97f, 0.24f), new(0.30f, 0.30f, -0.90f), 34f, -8f);
    private static readonly Stance Release = new(
        new(0.22f, -0.03f, 0.98f), new(-0.12f, 0.14f, 0.98f), new(0.16f, 0.74f, 0.65f), -21f, 12f);
    private static readonly Stance Brace = new(
        new(0.34f, -0.89f, -0.30f), new(0.03f, -0.30f, 0.95f), new(0.16f, 0.95f, 0.28f), 8f, 9f);
    private static readonly Stance Reach = new(
        new(0.28f, -0.31f, 0.91f), new(0.06f, 0.12f, 0.99f), new(0.04f, 0.98f, 0.18f), -12f, 7f);
    private static readonly Stance Catch = new(
        new(0.42f, -0.87f, -0.25f), new(-0.25f, 0.42f, 0.87f), new(0.06f, 0.98f, 0.18f), 11f, -5f);
    private static readonly Stance SlamReady = new(
        new(0.53f, 0.79f, 0.28f), new(0.25f, 0.91f, -0.32f), new(0.10f, 0.20f, -0.97f), 11f, -12f);
    private static readonly Stance SlamStrike = new(
        new(0.43f, -0.68f, 0.60f), new(0.25f, -0.42f, 0.88f), new(0.10f, -0.96f, 0.25f), -8f, 32f);

    private readonly int _id;
    private readonly EnemyView _body;
    private readonly Transform _model;
    private readonly Quaternion _modelRest;
    private readonly AnimationClip _walk;
    private readonly AnimationClip _run;
    private readonly Joint[] _joints;
    private readonly GameObject _floor;
    private readonly Material _danger;
    private readonly WarningShape _footprint;
    private readonly WarningShape _landing;
    private readonly SlamDustView _dust;
    private readonly LineRenderer _impactRing;
    private readonly LineRenderer _lungeArrow;
    private readonly MaterialPropertyBlock _impactColor = new();
    private readonly IDisposable _stateSubscription;
    private readonly Transform _sword;
    private readonly Transform _hand;
    private readonly Transform _grip;
    private readonly Quaternion _gripFromHand;
    private readonly Joint _chest;
    private readonly Joint _rightUpperArm;
    private readonly Joint _rightForearm;
    private readonly Joint _leftUpperArm;
    private readonly Joint _leftForearm;
    private readonly Joint _head;
    private readonly ArmGrip _swordArm;
    private readonly ArmGrip _supportArm;
    private readonly Transform _hips;
    private readonly Leg _leftLeg;
    private readonly Leg _rightLeg;
    private Quaternion _visualFacing;
    private Stance _stance = Rest;
    private Stance _from = Rest;
    private Stance _target = Rest;
    private float _poseTime;
    private float _poseDuration = 1f;
    private float _warningSeconds;
    private bool _newWarning;
    private float _stride;
    private float _gait;
    private BuriedKingPose _pose = BuriedKingPose.Held;
    private Vector3 _flightStart;
    private Vector3 _flightEnd;
    private Vector3 _flightPosition;
    private Vector3 _releaseGrip;
    private Quaternion _releaseRotation;
    private float _impactRadius;
    private float _impactAge;
    private float _catchAge = 1f;
    private float _cutSeconds;
    private LungeStance _lungePose = LungeGuard;
    private LungeStance _recoveryStart;
    private Vector3 _windupGrip;
    private Vector3 _windupBlade;
    private float _guardRelease;

    public BuriedKingView(EnemyView body, int id, DomainEventHub hub)
    {
        _body = body;
        _id = id;
        var rig = body.GetComponent<BuriedKingRig>();
        _model = rig.Model;
        _modelRest = _model.localRotation;
        _visualFacing = _model.rotation;
        _walk = rig.Walk;
        _run = rig.Run;
        Transform[] bones = _model.Find("Rig_Medium").GetComponentsInChildren<Transform>(true);
        _joints = new Joint[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            Transform bone = bones[i];
            var joint = new Joint(bone, _model);
            _joints[i] = joint;
            switch (bone.name)
            {
                case "chest":
                    _chest = joint;
                    break;
                case "head":
                    _head = joint;
                    break;
                case "hips":
                    _hips = bone;
                    break;
                case "upperarm.r":
                    _rightUpperArm = joint;
                    break;
                case "lowerarm.r":
                    _rightForearm = joint;
                    break;
                case "hand.r":
                    _hand = bone;
                    break;
                case "handslot.r":
                    _grip = bone;
                    break;
                case "upperarm.l":
                    _leftUpperArm = joint;
                    break;
                case "lowerarm.l":
                    _leftForearm = joint;
                    break;
            }
        }

        Transform bonesRoot = _model.Find("Rig_Medium");
        _leftLeg = new Leg(bonesRoot, _model, "l", 0.09f);
        _rightLeg = new Leg(bonesRoot, _model, "r", -0.09f);
        _swordArm = new ArmGrip(_rightUpperArm.Bone, _rightForearm.Bone, _hand, _grip, 1f);
        Transform supportHand = Array.Find(bones, bone => bone.name == "hand.l");
        _supportArm = new ArmGrip(_leftUpperArm.Bone, _leftForearm.Bone, supportHand, supportHand.Find("handslot.l"), -1f);
        _gripFromHand = Quaternion.Inverse(_hand.rotation) * _grip.rotation;
        _sword = Object.Instantiate(rig.Sword, _grip, false).transform;
        _sword.name = "Buried King's sword";

        // A world-space root keeps the locked floor shapes clear of model turns and scaling.
        _floor = new GameObject("Buried King warnings");
        SceneManager.MoveGameObjectToScene(_floor, body.gameObject.scene);
        _danger = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
        {
            renderQueue = (int)RenderQueue.Transparent,
        };
        _danger.SetFloat("_Surface", 1f);
        _danger.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        _danger.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        _danger.SetFloat("_ZWrite", 0f);
        _danger.SetFloat("_Cull", (float)CullMode.Off);
        _danger.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _footprint = new WarningShape(_floor.transform, _danger, "Attack warning");
        _landing = new WarningShape(_floor.transform, _danger, "Landing warning");
        _dust = Object.Instantiate(rig.LandingDust, _floor.transform);
        _dust.transform.localScale = Vector3.one * 0.38f;
        _impactRing = CreateImpactRing();
        _lungeArrow = CreateFloorLine("Lunge direction");
        _lungeArrow.useWorldSpace = true;
        _lungeArrow.alignment = LineAlignment.TransformZ;
        _lungeArrow.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        _lungeArrow.positionCount = 5;
        _lungeArrow.widthMultiplier = 0.12f;
        _impactColor.SetColor("_BaseColor", new Color(0.89f, 0.83f, 0.70f, 0.7f));
        _lungeArrow.SetPropertyBlock(_impactColor);
        Step(0f);
        _stateSubscription = hub.Subscribe<BuriedKingStateChanged>(OnState);
        _body.MoveApplied += Step;
    }

    public void Dispose()
    {
        _stateSubscription.Dispose();
        _body.MoveApplied -= Step;
        foreach (Joint joint in _joints)
        {
            joint.BlendWalk(0f);
        }

        _model.localRotation = _modelRest;
        _sword.gameObject.SetActive(false);
        _floor.SetActive(false);
        _footprint.Dispose();
        _landing.Dispose();
        Destroy(_sword.gameObject);
        Destroy(_floor);
        Destroy(_danger);
    }

    public void Step(float dt)
    {
        if (_pose == BuriedKingPose.Cleared)
        {
            return;
        }

        if (!_newWarning)
        {
            _poseTime += dt;
        }

        _newWarning = false;
        _catchAge += dt;
        StepImpact(dt);
        float progress = _warningSeconds > 0f ? Mathf.Clamp01(_poseTime / _warningSeconds) : 0f;
        UpdateStance(dt, progress);
        _footprint.Fill(_pose == BuriedKingPose.LungeTravel ? 1f : progress,
            _pose == BuriedKingPose.FractureTell);
        _landing.Fill(progress);
        switch (_pose)
        {
            case BuriedKingPose.Outbound:
            case BuriedKingPose.Returning:
                DrawFlight();
                break;
            case BuriedKingPose.Embedded:
            case BuriedKingPose.RecallTell:
                break;
            default:
                HoldSword();
                break;
        }
    }

    private void OnState(BuriedKingStateChanged state)
    {
        if (state.EnemyId != _id)
        {
            return;
        }

        BuriedKingPose previous = _pose;
        _pose = state.Pose;
        if (_pose == BuriedKingPose.Outbound || _pose == BuriedKingPose.Returning)
        {
            _flightStart = state.Start.ToUnity();
            _flightEnd = state.End.ToUnity();
            _flightPosition = state.WeaponPosition.ToUnity();
        }

        if (previous == _pose)
        {
            return;
        }

        if (_pose != BuriedKingPose.LungeTravel)
        {
            _footprint.Clear();
            _landing.Clear();
            _lungeArrow.enabled = false;
        }

        _warningSeconds = state.WarningSeconds;
        _newWarning = _warningSeconds > 0f;
        if (previous == BuriedKingPose.LungeRecovery)
        {
            _guardRelease = 0.18f;
        }

        if (previous == BuriedKingPose.Returning && _pose is BuriedKingPose.Held or BuriedKingPose.FractureTell or BuriedKingPose.SlamTell)
        {
            _catchAge = 0f;
        }

        Vector3 start = state.Start.ToUnity();
        Vector3 end = state.End.ToUnity();
        switch (_pose)
        {
            case BuriedKingPose.LungeTell:
                _windupGrip = _model.InverseTransformPoint(_grip.position);
                _windupBlade = _model.InverseTransformDirection(_grip.up);
                _guardRelease = 0f;
                _poseTime = 0f;
                _footprint.Lane(state.SlashStart.ToUnity(), state.SlashEnd.ToUnity(), state.Width);
                ShowLungeArrow(start, end);
                break;
            case BuriedKingPose.LungeTravel:
                _poseTime = 0f;
                _cutSeconds = state.CutSeconds;
                break;
            case BuriedKingPose.LungeRecovery:
                _poseTime = 0f;
                _cutSeconds = state.CutSeconds;
                _recoveryStart = _lungePose;
                break;
            case BuriedKingPose.SweepTell:
                PoseToward(SweepReady, _warningSeconds * 0.65f);
                _footprint.Arc(start, Vector3.ProjectOnPlane(end - start, Vector3.up), state.SweepDegrees);
                break;
            case BuriedKingPose.CastTell:
                PoseToward(CastReady, _warningSeconds * 0.77f);
                _footprint.Lane(start, end, state.Width);
                _landing.Arc(end, Vector3.forward * state.LandingRadius, 360f);
                break;
            case BuriedKingPose.Outbound:
                _releaseGrip = _grip.position;
                _releaseRotation = _grip.rotation;
                PoseToward(Release, 0.07f);
                break;
            case BuriedKingPose.Embedded:
                PoseToward(Brace, 0.24f);
                DrawEmbedded(end);
                PlayImpact(end, state.LandingRadius);
                break;
            case BuriedKingPose.RecallTell:
                PoseToward(Reach, _warningSeconds * 0.76f);
                DrawEmbedded(start);
                _footprint.Lane(start, end, state.Width);
                break;
            case BuriedKingPose.FractureTell:
                PoseToward(Rest, 0.28f);
                _footprint.Lane(start, end, state.Width);
                break;
            case BuriedKingPose.SlamTell:
                PoseToward(SlamReady, _warningSeconds * 0.65f);
                _footprint.Arc(start, Vector3.forward * state.SlamRadius, 360f);
                break;
            case BuriedKingPose.Held:
            case BuriedKingPose.LungeApproach:
                PoseToward(Rest, 0.38f);
                break;
            case BuriedKingPose.Cleared:
                _sword.gameObject.SetActive(false);
                _impactRing.enabled = false;
                _dust.OnDespawn();
                break;
        }
    }

    private void PoseToward(Stance target, float duration)
    {
        _from = _stance;
        _target = target;
        _poseDuration = duration;
        _poseTime = 0f;
    }

    private void UpdateStance(float dt, float progress)
    {
        if (_pose is BuriedKingPose.LungeTell or BuriedKingPose.LungeTravel or BuriedKingPose.LungeRecovery)
        {
            PoseLunge(dt, progress);
            return;
        }

        _stance = Stance.Blend(_from, _target, Mathf.SmoothStep(0f, 1f, _poseTime / _poseDuration));
        // Ease through the weight transfer and finish the swing on the core's impact frame.
        _stance = _pose switch
        {
            BuriedKingPose.SweepTell when progress > 0.70f =>
                Stance.Blend(SweepReady, SweepStrike, StrikeProgress(progress, 0.70f)),
            BuriedKingPose.CastTell when progress > 0.80f =>
                Stance.Blend(CastReady, Release, StrikeProgress(progress, 0.80f)),
            BuriedKingPose.SlamTell when progress > 0.70f =>
                Stance.Blend(SlamReady, SlamStrike, StrikeProgress(progress, 0.70f)),
            _ => _stance,
        };
        if (_catchAge < 0.25f)
        {
            // Draw the hand back under the returning blade, then let the next tell take over.
            float absorb = _catchAge < 0.09f
                ? Mathf.SmoothStep(0f, 1f, _catchAge / 0.09f)
                : Mathf.SmoothStep(1f, 0f, (_catchAge - 0.09f) / 0.16f);
            _stance = Stance.Blend(_stance, Catch, absorb);
        }

        Vector3 velocity = _body.Velocity;
        float speed = velocity.magnitude;
        Quaternion facing = _body.transform.rotation * _modelRest;
        bool swordAway = _pose is BuriedKingPose.Outbound or BuriedKingPose.Embedded or BuriedKingPose.RecallTell or BuriedKingPose.Returning;
        if (!swordAway)
        {
            _visualFacing = Quaternion.RotateTowards(_visualFacing, facing, dt * (_warningSeconds > 0f ? 360f : 230f));
        }

        _model.rotation = _visualFacing;

        // Distance drives the in-place cycle. Playing it backwards keeps the King's eyes on
        // the player when he backs off, and collisions slow his feet with his actual movement.
        float direction = Vector3.Dot(velocity, _body.transform.forward) < 0f ? -1f : 1f;
        bool running = _pose == BuriedKingPose.LungeApproach;
        float cycleDistance = running ? RunCycleDistance : WalkCycleDistance;
        _stride = Mathf.Repeat(_stride + direction * dt * speed / (_model.lossyScale.x * cycleDistance), 1f);
        _gait = swordAway ? 0f : Mathf.MoveTowards(_gait, Mathf.Clamp01(speed / 1.8f), 8f * dt);
        if (_gait > 0f)
        {
            AnimationClip clip = running ? _run : _walk;
            clip.SampleAnimation(_model.gameObject, _stride * clip.length);
        }

        foreach (Joint joint in _joints)
        {
            joint.BlendWalk(_gait);
        }

        // Drop the pelvis into the armor's weight while the boots keep the same floor contacts.
        float planted = 1f - _gait;
        Vector3 leftFoot = _leftLeg.Target(_model, planted);
        Vector3 rightFoot = _rightLeg.Target(_model, planted);
        _hips.position += _model.TransformVector(new Vector3(
            _stance.Twist * 0.0012f, -0.058f - Mathf.Abs(_stance.Lean) * 0.002f, _stance.Lean * 0.005f) * planted);
        _leftLeg.Plant(_model, leftFoot, planted);
        _rightLeg.Plant(_model, rightFoot, planted);

        float counterSwing = Mathf.Sin(_stride * Mathf.PI * 2f) * _gait * direction;
        _chest.Turn(_model, _stance.Lean + _gait * 4f * direction, _stance.Twist);
        _rightUpperArm.Aim(_model, 1f, _stance.Upper);
        _rightForearm.Aim(_model, 1f, _stance.Forearm);
        _leftUpperArm.Aim(_model, -1f, new Vector3(-0.29f, -0.91f, -0.14f + counterSwing * 0.31f));
        _leftForearm.Aim(_model, -1f, new Vector3(-0.15f, -0.83f, 0.53f + counterSwing * 0.31f));

        Vector3 forearm = _stance.Forearm.normalized;
        Vector3 crossGrip = Vector3.ProjectOnPlane(_stance.Blade, forearm).normalized;
        float wristBend = Mathf.Clamp(Vector3.Dot(_stance.Blade, forearm), -0.55f, 0.65f);
        Vector3 blade = _model.TransformDirection((crossGrip * Mathf.Sqrt(1f - wristBend * wristBend)) + forearm * wristBend);
        // The grip crosses the palm. Orient the hand and sword together so the wrist
        // carries the blade through the entire wind-up, impact and follow-through.
        Vector3 knuckles = _model.TransformDirection(_stance.Forearm);
        Vector3 bladeFace = Vector3.Cross(blade, knuckles).normalized;
        _hand.rotation = Quaternion.LookRotation(bladeFace, blade) * Quaternion.Inverse(_gripFromHand);
        if (_guardRelease > 0f)
        {
            float weight = Mathf.SmoothStep(0f, 1f, _guardRelease / 0.18f);
            PoseLungeGrip(LungeGuard, weight, weight);
            _guardRelease -= dt;
        }
    }

    private void PoseLunge(float dt, float progress)
    {
        Quaternion facing = _body.transform.rotation * _modelRest;
        _visualFacing = _pose == BuriedKingPose.LungeTell
            ? Quaternion.RotateTowards(_visualFacing, facing, dt * 540f)
            : facing;
        _model.rotation = _visualFacing;
        _gait = 0f;
        foreach (Joint joint in _joints)
        {
            joint.BlendWalk(0f);
        }

        float support = 1f;
        if (_pose == BuriedKingPose.LungeTell)
        {
            float load = Mathf.SmoothStep(0f, 1f, progress / 0.62f);
            var from = new LungeStance(_windupGrip, _windupBlade, LungeGuard.Pelvis,
                LungeGuard.Front, LungeGuard.Rear, 0f, LungeGuard.Twist, LungeGuard.Lean);
            _lungePose = LungeStance.Blend(from, LungeLoaded, load);
            support = Mathf.SmoothStep(0f, 1f, progress / 0.28f);
        }
        else if (_pose == BuriedKingPose.LungeTravel)
        {
            float cutStart = 1f - _cutSeconds / _warningSeconds;
            _lungePose = progress < cutStart
                ? LungeStance.Blend(LungeLoaded, LungeGlide, Mathf.SmoothStep(0f, 1f, progress / 0.36f))
                : LungeStance.Blend(LungeGlide, LungePlant, StrikeProgress(progress, cutStart));
        }
        else
        {
            // Keep both boots at their impact positions throughout the settle.
            var guard = new LungeStance(LungeGuard.Grip, LungeGuard.Blade, LungeGuard.Pelvis,
                _recoveryStart.Front, _recoveryStart.Rear, 0f, LungeGuard.Twist, LungeGuard.Lean);
            _lungePose = SettleLunge(guard, progress);
        }

        LungeStance pose = _lungePose;
        Vector3 leftFoot = _leftLeg.Target(_model, 1f, pose.Front - 0.09f, pose.Lift);
        Vector3 rightFoot = _rightLeg.Target(_model, 1f, pose.Rear + 0.09f);
        _hips.position += _model.TransformVector(pose.Pelvis);
        _leftLeg.Plant(_model, leftFoot, 1f);
        _rightLeg.Plant(_model, rightFoot, 1f);
        _chest.Turn(_model, pose.Lean, pose.Twist);
        _head.Bone.rotation = Quaternion.AngleAxis(-pose.Lean, _model.right)
            * Quaternion.AngleAxis(-pose.Twist, _model.up) * _head.Bone.rotation;

        PoseLungeGrip(pose, support, rightWeight: 1f);
    }

    private LungeStance SettleLunge(LungeStance guard, float progress)
    {
        const float followThrough = 0.18f;
        if (!(_cutSeconds > 0f))
        {
            return LungeStance.Blend(_recoveryStart, guard, StrikeProgress(progress, 0f));
        }

        return progress < followThrough
            ? LungeStance.Blend(_recoveryStart, LungeFollowThrough, Mathf.SmoothStep(0f, 1f, progress / followThrough))
            : LungeStance.Blend(LungeFollowThrough, guard, StrikeProgress(progress, followThrough));
    }

    private void PoseLungeGrip(LungeStance pose, float leftWeight, float rightWeight)
    {
        Vector3 blade = _model.TransformDirection(pose.Blade.normalized);
        Vector3 face = Vector3.Cross(blade, _model.right).normalized;
        Quaternion gripRotation = Quaternion.LookRotation(face, blade);
        _swordArm.Place(_model, _model.TransformPoint(pose.Grip), gripRotation, rightWeight);
        _supportArm.Place(_model, _grip.position - _grip.up * SupportGripSpacing, _grip.rotation, leftWeight);
    }

    private void ShowLungeArrow(Vector3 start, Vector3 end)
    {
        Vector3 forward = Vector3.ProjectOnPlane(end - start, Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        Vector3 tip = end + Vector3.up * (FloorOffset + 0.024f);
        Vector3 tail = start + forward * 0.65f + Vector3.up * (FloorOffset + 0.024f);
        Vector3 shoulder = tip - forward * 0.53f;
        _lungeArrow.SetPosition(0, tail);
        _lungeArrow.SetPosition(1, tip);
        _lungeArrow.SetPosition(2, shoulder + side * 0.30f);
        _lungeArrow.SetPosition(3, tip);
        _lungeArrow.SetPosition(4, shoulder - side * 0.30f);
        _lungeArrow.enabled = true;
    }

    private static float StrikeProgress(float progress, float start) =>
        Mathf.SmoothStep(0f, 1f, (progress - start) / (1f - start));

    private LineRenderer CreateImpactRing()
    {
        LineRenderer line = CreateFloorLine("Sword impact ring");
        line.loop = true;
        line.positionCount = ArcSegments;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        for (int i = 0; i < ArcSegments; i++)
        {
            float angle = 2f * Mathf.PI * i / ArcSegments;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
        }

        line.enabled = false;
        return line;
    }

    private LineRenderer CreateFloorLine(string name)
    {
        var root = new GameObject(name);
        root.transform.SetParent(_floor.transform, false);
        var line = root.AddComponent<LineRenderer>();
        line.sharedMaterial = _danger;
        line.useWorldSpace = false;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    private void PlayImpact(Vector3 point, float radius)
    {
        _dust.Play(point, _id);
        _impactRing.transform.position = point + Vector3.up * FloorOffset;
        _impactRing.enabled = true;
        _impactRadius = radius;
        _impactAge = 0f;
        StepImpact(0f);
    }

    private void StepImpact(float dt)
    {
        _dust.Step(dt);
        if (!_impactRing.enabled)
        {
            return;
        }

        _impactAge += dt;
        float t = Mathf.Clamp01(_impactAge / ImpactRingSeconds);
        float radius = _impactRadius * Mathf.Lerp(0.18f, 1f, 1f - (1f - t) * (1f - t));
        _impactRing.transform.localScale = new Vector3(radius, 1f, radius);
        _impactRing.widthMultiplier = Mathf.Lerp(0.21f, 0.08f, t);
        _impactColor.SetColor("_BaseColor", new Color(1f, 0.72f, 0.39f, 1f - t * t));
        _impactRing.SetPropertyBlock(_impactColor);
        _impactRing.enabled = t < 1f;
    }

    private void HoldSword()
    {
        if (_sword.parent != _grip)
        {
            _sword.SetParent(_grip, false);
        }

        _sword.localScale = Vector3.one * (SwordScale / _grip.lossyScale.x);
        _sword.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    private void DrawEmbedded(Vector3 point)
    {
        EmbeddedPose(point, out Vector3 grip, out Quaternion rotation);
        PlaceSword(grip, rotation);
    }

    private void DrawFlight()
    {
        bool returning = _pose == BuriedKingPose.Returning;
        Vector3 direction = Vector3.ProjectOnPlane(_flightEnd - _flightStart, Vector3.up).normalized;
        Vector3 king = returning ? _flightEnd : _flightStart;
        Vector3 landing = returning ? _flightStart : _flightEnd;
        float fromKing = Vector3.ProjectOnPlane(_flightPosition - king, Vector3.up).magnitude;
        float fromLanding = Vector3.ProjectOnPlane(_flightPosition - landing, Vector3.up).magnitude;
        float flight = Mathf.SmoothStep(0f, 1f, fromKing / HandBlendDistance);
        float airborne = Mathf.SmoothStep(0f, 1f, fromLanding / LandingBlendDistance);

        // The blade leads on the way out, and the pommel leads back into the King's palm.
        Vector3 flightGrip = _flightPosition + Vector3.up * FlightHeight -
            direction * (returning ? GripToPommel : GripToTip);
        Quaternion flightRotation = Quaternion.LookRotation(Vector3.up, returning ? -direction : direction);
        Vector3 grip = Vector3.Lerp(returning ? _grip.position : _releaseGrip, flightGrip, flight);
        Quaternion rotation = Quaternion.Slerp(returning ? _grip.rotation : _releaseRotation, flightRotation, flight);
        EmbeddedPose(landing, out Vector3 embeddedGrip, out Quaternion embeddedRotation);
        PlaceSword(Vector3.Lerp(embeddedGrip, grip, airborne), Quaternion.Slerp(embeddedRotation, rotation, airborne));
    }

    private static void EmbeddedPose(Vector3 point, out Vector3 grip, out Quaternion rotation)
    {
        Vector3 axis = new Vector3(0.11f, -1f, 0.19f).normalized;
        grip = point - axis * (GripToTip - 0.17f);
        rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, axis), axis);
    }

    private void PlaceSword(Vector3 grip, Quaternion rotation)
    {
        if (_sword.parent != _floor.transform)
        {
            _sword.SetParent(_floor.transform, false);
            _sword.localScale = Vector3.one * SwordScale;
        }

        _sword.SetPositionAndRotation(grip, rotation);
    }

    private readonly struct Stance
    {
        public Vector3 Upper { get; }
        public Vector3 Forearm { get; }
        public Vector3 Blade { get; }
        public float Twist { get; }
        public float Lean { get; }

        public Stance(Vector3 upper, Vector3 forearm, Vector3 blade, float twist, float lean)
        {
            Upper = upper;
            Forearm = forearm;
            Blade = blade.normalized;
            Twist = twist;
            Lean = lean;
        }

        public static Stance Blend(Stance from, Stance to, float t) => new(
            Vector3.Slerp(from.Upper, to.Upper, t),
            Vector3.Slerp(from.Forearm, to.Forearm, t),
            Vector3.Slerp(from.Blade, to.Blade, t),
            Mathf.Lerp(from.Twist, to.Twist, t),
            Mathf.Lerp(from.Lean, to.Lean, t));
    }

    private readonly struct LungeStance
    {
        public readonly Vector3 Grip;
        public readonly Vector3 Blade;
        public readonly Vector3 Pelvis;
        public readonly float Front;
        public readonly float Rear;
        public readonly float Lift;
        public readonly float Twist;
        public readonly float Lean;

        public LungeStance(Vector3 grip, Vector3 blade, Vector3 pelvis, float front, float rear, float lift,
            float twist, float lean)
        {
            Grip = grip;
            Blade = blade;
            Pelvis = pelvis;
            Front = front;
            Rear = rear;
            Lift = lift;
            Twist = twist;
            Lean = lean;
        }

        public static LungeStance Blend(LungeStance from, LungeStance to, float t) => new(
            Vector3.Lerp(from.Grip, to.Grip, t), Vector3.Slerp(from.Blade, to.Blade, t),
            Vector3.Lerp(from.Pelvis, to.Pelvis, t), Mathf.Lerp(from.Front, to.Front, t),
            Mathf.Lerp(from.Rear, to.Rear, t), Mathf.Lerp(from.Lift, to.Lift, t),
            Mathf.Lerp(from.Twist, to.Twist, t), Mathf.Lerp(from.Lean, to.Lean, t));
    }

    private readonly struct ArmGrip
    {
        private readonly Transform _shoulder;
        private readonly Transform _elbow;
        private readonly Transform _hand;
        private readonly Transform _slot;
        private readonly float _side;

        public ArmGrip(Transform shoulder, Transform elbow, Transform hand, Transform slot, float side)
        {
            _shoulder = shoulder;
            _elbow = elbow;
            _hand = hand;
            _slot = slot;
            _side = side;
        }

        public void Place(Transform model, Vector3 grip, Quaternion rotation, float weight)
        {
            if (!(weight > 0f))
            {
                return;
            }

            Quaternion handRotation = rotation * Quaternion.Inverse(_slot.localRotation);
            Vector3 wrist = grip - handRotation * Vector3.Scale(_slot.localPosition, _hand.lossyScale);
            Quaternion facing = Quaternion.Slerp(_hand.rotation, handRotation, weight);
            Vector3 bend = model.TransformDirection(new Vector3(_side, -0.6f, -0.2f));
            AimLimb(_shoulder, _elbow, _hand, Vector3.Lerp(_hand.position, wrist, weight), bend);
            _hand.rotation = facing;
        }
    }

    private static void AimLimb(Transform root, Transform middle, Transform end, Vector3 target, Vector3 pole)
    {
        Vector3 start = root.position;
        float upper = Vector3.Distance(start, middle.position);
        float lower = Vector3.Distance(middle.position, end.position);
        Vector3 axis = (target - start).normalized;
        float length = Mathf.Clamp(Vector3.Distance(start, target), Mathf.Abs(upper - lower) + 0.001f, upper + lower - 0.001f);
        float along = (upper * upper - lower * lower + length * length) / (2f * length);
        Vector3 bend = Vector3.ProjectOnPlane(pole, axis).normalized;
        Vector3 joint = start + axis * along + bend * Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
        root.rotation = Quaternion.FromToRotation(middle.position - start, joint - start) * root.rotation;
        middle.rotation = Quaternion.FromToRotation(end.position - middle.position, start + axis * length - joint) * middle.rotation;
    }

    private readonly struct Joint
    {
        private readonly Transform _bone;
        private readonly Vector3 _restPosition;
        private readonly Quaternion _restRotation;
        private readonly Vector3 _restScale;
        private readonly Vector3 _bindRight;

        public Transform Bone => _bone;

        public Joint(Transform bone, Transform model)
        {
            _bone = bone;
            _restPosition = bone.localPosition;
            _restRotation = bone.localRotation;
            _restScale = bone.localScale;
            _bindRight = bone.InverseTransformDirection(model.right);
        }

        public void BlendWalk(float weight)
        {
            _bone.SetLocalPositionAndRotation(Vector3.Lerp(_restPosition, _bone.localPosition, weight),
                Quaternion.Slerp(_restRotation, _bone.localRotation, weight));
            _bone.localScale = _restScale;
        }

        public void Aim(Transform model, float side, Vector3 aim)
        {
            _bone.localRotation = _restRotation;
            Vector3 current = _bone.TransformDirection(_bindRight * side);
            _bone.rotation = Quaternion.FromToRotation(current, model.TransformDirection(aim)) * _bone.rotation;
        }

        public void Turn(Transform model, float pitch, float yaw) =>
            _bone.rotation = Quaternion.AngleAxis(yaw, model.up) * Quaternion.AngleAxis(pitch, model.right) * _bone.rotation;
    }

    private readonly struct Leg
    {
        private readonly Transform _thigh;
        private readonly Transform _shin;
        private readonly Transform _foot;
        private readonly Vector3 _stanceFoot;
        private readonly Quaternion _footFacing;

        public Leg(Transform root, Transform model, string side, float stagger)
        {
            _thigh = Array.Find(root.GetComponentsInChildren<Transform>(true), bone => bone.name == "upperleg." + side);
            _shin = _thigh.Find("lowerleg." + side);
            _foot = _shin.Find("foot." + side);
            _stanceFoot = model.InverseTransformPoint(_foot.position) + Vector3.forward * stagger;
            _footFacing = Quaternion.Inverse(model.rotation) * _foot.rotation;
        }

        public Vector3 Target(Transform model, float weight, float step = 0f, float lift = 0f) =>
            Vector3.Lerp(_foot.position, model.TransformPoint(_stanceFoot + new Vector3(0f, lift, step)), weight);

        public void Plant(Transform model, Vector3 target, float weight)
        {
            if (weight <= 0f)
            {
                return;
            }

            Quaternion footRotation = Quaternion.Slerp(_foot.rotation, model.rotation * _footFacing, weight);
            AimLimb(_thigh, _shin, _foot, target, model.forward);
            _foot.rotation = footRotation;
        }
    }

    private sealed class WarningShape : IDisposable
    {
        private static readonly Color Red = new(1f, 0.10f, 0.07f);
        private readonly MeshRenderer _base;
        private readonly MeshRenderer _fill;
        private readonly Mesh _baseMesh;
        private readonly Mesh _fillMesh;
        private readonly Vector3[] _outline = new Vector3[ArcSegments + 2];
        private readonly Vector3[] _filled = new Vector3[ArcSegments + 2];
        private readonly int[] _triangles = new int[ArcSegments * 3];
        private readonly MaterialPropertyBlock _color = new();
        private int _count;
        private bool _isLane;

        public WarningShape(Transform parent, Material material, string name)
        {
            _base = Layer(parent, material, name, out _baseMesh);
            _fill = Layer(parent, material, name + " fill", out _fillMesh);
            _fill.sortingOrder = 1;
            for (int i = 0; i < ArcSegments; i++)
            {
                _triangles[i * 3] = 0;
                _triangles[i * 3 + 1] = i + 1;
                _triangles[i * 3 + 2] = i + 2;
            }

            Tint(_base, 0.27f);
        }

        public void Arc(Vector3 center, Vector3 reach, float degrees)
        {
            _isLane = false;
            _count = _outline.Length;
            center += Vector3.up * FloorOffset;
            _outline[0] = center;
            for (int i = 0; i <= ArcSegments; i++)
            {
                float angle = Mathf.Lerp(-degrees * 0.5f, degrees * 0.5f, (float)i / ArcSegments);
                _outline[i + 1] = center + Quaternion.AngleAxis(angle, Vector3.up) * reach;
            }

            Show();
        }

        public void Lane(Vector3 start, Vector3 end, float width)
        {
            _isLane = true;
            _count = 4;
            Vector3 halfWidth = Vector3.Cross(Vector3.up, end - start).normalized * (width * 0.5f);
            start += Vector3.up * FloorOffset;
            end += Vector3.up * FloorOffset;
            _outline[0] = start - halfWidth;
            _outline[1] = start + halfWidth;
            _outline[2] = end + halfWidth;
            _outline[3] = end - halfWidth;
            Show();
        }

        public void Fill(float progress, bool pulse = false)
        {
            if (!_base.enabled || !(progress > 0f))
            {
                return;
            }

            Vector3 offset = Vector3.up * 0.012f;
            for (int i = 0; i < _count; i++)
            {
                Vector3 origin = _isLane ? _outline[i < 2 ? i : 3 - i] : _outline[0];
                _filled[i] = Vector3.Lerp(origin, _outline[i], progress) + offset;
            }

            _fillMesh.SetVertices(_filled, 0, _count);
            _fillMesh.RecalculateBounds();
            Tint(_fill, pulse ? 0.75f + 0.13f * Mathf.Sin(progress * 4f * Mathf.PI) : 0.85f);
            _fill.enabled = true;
        }

        public void Clear()
        {
            _base.enabled = false;
            _fill.enabled = false;
        }

        public void Dispose()
        {
            Destroy(_baseMesh);
            Destroy(_fillMesh);
        }

        private void Show()
        {
            SetGeometry(_baseMesh);
            SetGeometry(_fillMesh);
            _base.enabled = true;
        }

        private void SetGeometry(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(_outline, 0, _count);
            mesh.SetIndices(_triangles, 0, (_count - 2) * 3, MeshTopology.Triangles, 0);
        }

        private void Tint(MeshRenderer renderer, float alpha)
        {
            Color color = Red;
            color.a = alpha;
            _color.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(_color);
        }

        private static MeshRenderer Layer(Transform parent, Material material, string name, out Mesh mesh)
        {
            var layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            layer.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            return renderer;
        }
    }

    private static void Destroy(Object resource)
    {
        if (Application.isPlaying)
        {
            Object.Destroy(resource);
        }
        else
        {
            Object.DestroyImmediate(resource);
        }
    }
}
