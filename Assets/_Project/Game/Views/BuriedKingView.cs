using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Soulvail.Game.Views;

/// <summary>Poses the Buried King and draws his locked floor geometry and collider-free weapon.</summary>
internal sealed class BuriedKingView : IDisposable
{
    private const int ArcSegments = 24;
    private const float FloorOffset = 0.06f;
    private const float ShaftLength = 1.15f;
    private const float GripToBase = 0.45f;
    private const float FlightHeight = 0.8f;
    private const float HandBlendDistance = 1.1f;
    private const float LandingBlendDistance = 0.55f;

    private static readonly Stance Rest = new(
        new(0.69f, -0.72f, -0.06f), new(0.48f, -0.12f, 0.87f), new(0.44f, 0.74f, 0.51f), 0f, 0f);
    private static readonly Stance SweepReady = new(
        new(0.85f, -0.30f, -0.35f), new(0.68f, 0.15f, 0.72f), new(0.80f, 0.13f, 0.55f), 24f, 0f);
    private static readonly Stance SweepStrike = new(
        new(0.18f, -0.32f, 0.93f), new(-0.58f, 0.06f, 0.81f), new(-0.89f, 0.13f, 0.46f), -29f, 6f);
    private static readonly Stance CastReady = new(
        new(0.89f, 0.18f, -0.40f), new(0.08f, 0.71f, -0.70f), new(0.22f, 0.72f, -0.65f), 22f, -8f);
    private static readonly Stance Reach = new(
        new(0.47f, -0.27f, 0.84f), new(0.05f, 0.06f, 1f), new(0.10f, 0.42f, 0.90f), -8f, 7f);
    private static readonly Stance SlamReady = new(
        new(0.86f, 0.50f, 0.12f), new(0.45f, 0.80f, 0.40f), new(-0.72f, 0.58f, 0.37f), 9f, -7f);
    private static readonly Stance SlamStrike = new(
        new(0.51f, -0.77f, 0.42f), new(0.10f, -0.68f, 0.73f), new(0.09f, -0.90f, 0.44f), -4f, 25f);

    private readonly int _id;
    private readonly EnemyView _body;
    private readonly GameObject _root;
    private readonly Material _danger;
    private readonly Material _sandstone;
    private readonly LineRenderer _weapon;
    private readonly LineRenderer _tell;
    private readonly LineRenderer _landing;
    private readonly Material _fractureMaterial;
    private readonly Mesh _fractureMesh;
    private readonly MeshRenderer _fracture;
    private readonly Vector3[] _fractureCorners = new Vector3[4];
    private readonly IDisposable _stateSubscription;
    private readonly Transform _handSocket;
    private readonly Joint _chest;
    private readonly Joint _rightUpperArm;
    private readonly Joint _rightForearm;
    private readonly Joint _rightHand;
    private readonly Joint _leftUpperArm;
    private readonly Joint _leftForearm;
    private readonly Joint _rightThigh;
    private readonly Joint _leftThigh;
    private Stance _stance = Rest;
    private Stance _from = Rest;
    private Stance _target = Rest;
    private float _poseTime;
    private float _poseDuration = 1f;
    private float _stride;
    private float _gait;
    private BuriedKingPose _pose = BuriedKingPose.Held;
    private Vector3 _flightStart;
    private Vector3 _flightEnd;
    private Vector3 _flightPosition;
    private Vector3 _releaseGrip;
    private Vector3 _releaseAxis;

    public BuriedKingView(EnemyView body, int id, DomainEventHub hub)
    {
        _body = body;
        _id = id;
        _root = new GameObject("Buried King presentation");
        _root.transform.SetParent(body.transform, false);
        foreach (Transform bone in body.GetComponentsInChildren<Transform>(true))
        {
            switch (bone.name)
            {
                case "chest":
                    _chest = new Joint(bone, body.transform);
                    break;
                case "upperarm.r":
                    _rightUpperArm = new Joint(bone, body.transform);
                    break;
                case "lowerarm.r":
                    _rightForearm = new Joint(bone, body.transform);
                    break;
                case "hand.r":
                    _rightHand = new Joint(bone, body.transform);
                    break;
                case "handslot.r":
                    _handSocket = bone;
                    break;
                case "upperarm.l":
                    _leftUpperArm = new Joint(bone, body.transform);
                    break;
                case "lowerarm.l":
                    _leftForearm = new Joint(bone, body.transform);
                    break;
                case "upperleg.r":
                    _rightThigh = new Joint(bone, body.transform);
                    break;
                case "upperleg.l":
                    _leftThigh = new Joint(bone, body.transform);
                    break;
            }
        }
        _danger = MakeMaterial(new Color(1f, 0.24f, 0.07f));
        _sandstone = MakeMaterial(new Color(1f, 0.85f, 0.59f));
        _weapon = CreateLine("Scepter", _sandstone, 0.13f);
        _tell = CreateLine("Locked warning", _danger, 0.09f);
        _landing = CreateLine("Landing marker", _danger, 0.09f);
        _fractureMaterial = MakeMaterial(_danger.color);
        _fractureMaterial.SetFloat("_Surface", 1f);
        _fractureMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        _fractureMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        _fractureMaterial.SetFloat("_ZWrite", 0f);
        _fractureMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _fractureMaterial.renderQueue = (int)RenderQueue.Transparent;
        // World-space floor geometry is independent of the body and its scale.
        var floor = new GameObject("Buried King fracture");
        _fractureMesh = new Mesh { name = "Buried King fracture", vertices = _fractureCorners };
        _fractureMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        floor.AddComponent<MeshFilter>().sharedMesh = _fractureMesh;
        _fracture = floor.AddComponent<MeshRenderer>();
        _fracture.sharedMaterial = _fractureMaterial;
        _fracture.shadowCastingMode = ShadowCastingMode.Off;
        _fracture.enabled = false;
        UpdateStance(0f);
        HoldWeapon();
        _stateSubscription = hub.Subscribe<BuriedKingStateChanged>(OnState);
        _body.MoveApplied += Step;
    }

    public void Dispose()
    {
        _stateSubscription.Dispose();
        _body.MoveApplied -= Step;
        _chest.Reset();
        _rightUpperArm.Reset();
        _rightForearm.Reset();
        _rightHand.Reset();
        _leftUpperArm.Reset();
        _leftForearm.Reset();
        _rightThigh.Reset();
        _leftThigh.Reset();
        // The held line lives under a rig bone rather than the presentation root.
        if (_weapon != null)
        {
            _weapon.enabled = false;
            Destroy(_weapon.gameObject);
        }

        if (_root != null)
        {
            _root.SetActive(false);
        }

        if (_fracture != null)
        {
            _fracture.enabled = false;
            Destroy(_fracture.gameObject);
        }

        Destroy(_root);
        Destroy(_danger);
        Destroy(_sandstone);
        Destroy(_fractureMesh);
        Destroy(_fractureMaterial);
    }

    public void Step(float dt)
    {
        if (_pose == BuriedKingPose.Cleared)
        {
            return;
        }

        UpdateStance(dt);
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
                HoldWeapon();
                break;
        }
    }

    private void OnState(BuriedKingStateChanged state)
    {
        if (state.EnemyId != _id || _root == null)
        {
            return;
        }

        if (state.Pose == BuriedKingPose.FractureTell && _fracture.enabled)
        {
            PulseFracture(state.FractureProgress);
            return;
        }

        _tell.enabled = false;
        _landing.enabled = false;
        _fracture.enabled = false;
        BuriedKingPose previous = _pose;
        _pose = state.Pose;
        Vector3 start = state.Start.ToUnity();
        Vector3 end = state.End.ToUnity();
        Vector3 planar = Vector3.ProjectOnPlane(end - start, Vector3.up);
        switch (state.Pose)
        {
            case BuriedKingPose.SweepTell:
                PoseToward(SweepReady, 0.55f);
                DrawSweep(start, planar, state.SweepDegrees);
                break;
            case BuriedKingPose.CastTell:
                PoseToward(CastReady, 0.72f);
                DrawLane(start, end, state.Width);
                DrawCircle(end, state.Width * 0.5f);
                break;
            case BuriedKingPose.Outbound:
            case BuriedKingPose.Returning:
                if (_pose == BuriedKingPose.Outbound && previous != _pose)
                {
                    _releaseGrip = HandPosition;
                    _releaseAxis = HandAxis;
                    PoseToward(Reach, 0.13f);
                }

                _flightStart = start;
                _flightEnd = end;
                _flightPosition = state.WeaponPosition.ToUnity();
                DrawFlight();
                break;
            case BuriedKingPose.Embedded:
                PoseToward(Rest, 0.24f);
                DrawEmbedded(end);
                break;
            case BuriedKingPose.RecallTell:
                PoseToward(Reach, 0.58f);
                DrawEmbedded(start);
                DrawLane(start, end, state.Width);
                break;
            case BuriedKingPose.FractureTell:
                PoseToward(Rest, 0.20f);
                HoldWeapon();
                DrawLane(start, end, state.Width);
                DrawFracture(start, end, state.Width, state.FractureProgress);
                break;
            case BuriedKingPose.SlamTell:
                PoseToward(SlamReady, 0.66f);
                HoldWeapon();
                DrawCircle(start, state.SlamRadius);
                break;
            case BuriedKingPose.Held:
                if (previous == BuriedKingPose.SweepTell || previous == BuriedKingPose.SlamTell)
                {
                    PoseToward(previous == BuriedKingPose.SweepTell ? SweepStrike : SlamStrike, 0f);
                    UpdateStance(0f);
                }

                PoseToward(Rest, 0.26f);
                HoldWeapon();
                break;
            case BuriedKingPose.Cleared:
                _weapon.enabled = false;
                _root.SetActive(false);
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

    private void UpdateStance(float dt)
    {
        _poseTime += dt;
        float t = _poseDuration > 0f ? Mathf.SmoothStep(0f, 1f, _poseTime / _poseDuration) : 1f;
        _stance = Stance.Blend(_from, _target, t);
        // The visual wind-ups reserve their last frames for the swing. The actual
        // strike and release are still driven by the incoming core state change.
        _stance = _pose switch
        {
            BuriedKingPose.SweepTell when _poseTime > 0.68f =>
                Stance.Blend(SweepReady, SweepStrike, Mathf.SmoothStep(0f, 1f, (_poseTime - 0.68f) / 0.17f)),
            BuriedKingPose.CastTell when _poseTime > 0.83f =>
                Stance.Blend(CastReady, Reach, Mathf.SmoothStep(0f, 1f, (_poseTime - 0.83f) / 0.17f)),
            BuriedKingPose.SlamTell when _poseTime > 0.73f =>
                Stance.Blend(SlamReady, SlamStrike, Mathf.SmoothStep(0f, 1f, (_poseTime - 0.73f) / 0.17f)),
            _ => _stance,
        };

        float speed = _body.Velocity.magnitude;
        _stride += dt * speed * 5f;
        _gait = Mathf.MoveTowards(_gait, Mathf.Clamp01(speed), 8f * dt);
        float step = Mathf.Sin(_stride) * _gait;
        Transform body = _body.transform;
        _chest.Turn(body, _stance.Lean, _stance.Twist);
        _rightUpperArm.Aim(body, Vector3.right, _stance.Upper);
        _rightForearm.Aim(body, Vector3.right, _stance.Forearm);
        // The palm closes across the shaft. Keep the palm-to-socket offset on the grip
        // even when the shaft goes from upright to horizontal or points down for the slam.
        Vector3 knuckles = Vector3.ProjectOnPlane(_stance.Forearm, _stance.Shaft).normalized;
        _rightHand.Aim(body, Vector3.right, knuckles);
        _leftUpperArm.Aim(body, Vector3.left, new Vector3(-0.57f, -0.80f, -0.10f));
        _leftForearm.Aim(body, Vector3.left, new Vector3(-0.15f, -0.63f, 0.80f - 0.24f * step));
        _rightThigh.Turn(body, step * 18f, 0f);
        _leftThigh.Turn(body, -step * 18f, 0f);
    }

    private void DrawSweep(Vector3 start, Vector3 reach, float degrees)
    {
        start += Vector3.up * FloorOffset;
        _tell.enabled = true;
        _tell.positionCount = ArcSegments + 3;
        _tell.SetPosition(0, start);
        for (int i = 0; i <= ArcSegments; i++)
        {
            float angle = Mathf.Lerp(-degrees * 0.5f, degrees * 0.5f, (float)i / ArcSegments);
            _tell.SetPosition(i + 1, start + Quaternion.AngleAxis(angle, Vector3.up) * reach);
        }

        _tell.SetPosition(ArcSegments + 2, start);
    }

    private void DrawLane(Vector3 start, Vector3 end, float width)
    {
        Vector3 halfWidth = Vector3.Cross(Vector3.up, end - start).normalized * (width * 0.5f);
        start += Vector3.up * FloorOffset;
        end += Vector3.up * FloorOffset;
        _tell.enabled = true;
        _tell.positionCount = 5;
        _tell.SetPosition(0, start - halfWidth);
        _tell.SetPosition(1, start + halfWidth);
        _tell.SetPosition(2, end + halfWidth);
        _tell.SetPosition(3, end - halfWidth);
        _tell.SetPosition(4, start - halfWidth);
    }

    private void DrawCircle(Vector3 center, float radius)
    {
        center += Vector3.up * FloorOffset;
        _landing.enabled = true;
        _landing.positionCount = ArcSegments + 1;
        for (int i = 0; i <= ArcSegments; i++)
        {
            float angle = 360f * i / ArcSegments;
            _landing.SetPosition(i, center + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * radius);
        }
    }

    private void DrawFracture(Vector3 start, Vector3 end, float width, float progress)
    {
        Vector3 halfWidth = Vector3.Cross(Vector3.up, end - start).normalized * (width * 0.5f);
        start += Vector3.up * FloorOffset;
        end += Vector3.up * FloorOffset;
        _fractureCorners[0] = start - halfWidth;
        _fractureCorners[1] = end - halfWidth;
        _fractureCorners[2] = end + halfWidth;
        _fractureCorners[3] = start + halfWidth;
        _fractureMesh.vertices = _fractureCorners;
        _fractureMesh.RecalculateBounds();
        PulseFracture(progress);
        _fracture.enabled = true;
    }

    private void PulseFracture(float progress)
    {
        Color color = _danger.color;
        float pulse = Mathf.Cos(progress * (4f * Mathf.PI));
        color.a = Mathf.Lerp(0.25f, 0.75f, (1f - pulse) * 0.5f);
        _fractureMaterial.color = color;
    }

    private Vector3 HandPosition => _handSocket != null
        ? _handSocket.position
        : _body.transform.TransformPoint(new Vector3(0.58f, 0.80f, 0.22f));

    private Vector3 HandAxis => _body.transform.TransformDirection(_stance.Shaft).normalized;

    private void HoldWeapon()
    {
        MountWeapon(_handSocket != null ? _handSocket : _root.transform);
        Vector3 axis = HandAxis;
        Vector3 side = Vector3.Cross(axis, _body.transform.forward).normalized;
        DrawWeapon(HandPosition - axis * GripToBase, axis, side);
    }

    private void DrawEmbedded(Vector3 position)
    {
        MountWeapon(_root.transform);
        DrawWeapon(position, Vector3.up, Vector3.right);
    }

    private void DrawFlight()
    {
        MountWeapon(_root.transform);
        bool returning = _pose == BuriedKingPose.Returning;
        Vector3 direction = Vector3.ProjectOnPlane(_flightEnd - _flightStart, Vector3.up).normalized;
        Vector3 end = returning ? _flightEnd : _flightStart;
        float distance = Vector3.ProjectOnPlane(_flightPosition - end, Vector3.up).magnitude;
        float flight = Mathf.SmoothStep(0f, 1f, distance / HandBlendDistance);
        float forwardExtent = returning ? GripToBase : ShaftLength - GripToBase;
        Vector3 flightGrip = _flightPosition + Vector3.up * FlightHeight - direction * forwardExtent;
        Vector3 grip = Vector3.Lerp(returning ? HandPosition : _releaseGrip, flightGrip, flight);
        // Recall carries the grip toward the King first, with the head trailing.
        Vector3 flightAxis = returning ? -direction : direction;
        Vector3 axis = Vector3.Slerp(returning ? HandAxis : _releaseAxis, flightAxis, flight);
        Vector3 landing = returning ? _flightStart : _flightEnd;
        float landingDistance = Vector3.ProjectOnPlane(_flightPosition - landing, Vector3.up).magnitude;
        float airborne = Mathf.SmoothStep(0f, 1f, landingDistance / LandingBlendDistance);
        grip = Vector3.Lerp(landing + Vector3.up * GripToBase, grip, airborne);
        axis = Vector3.Slerp(Vector3.up, axis, airborne);
        Vector3 side = Vector3.Lerp(Vector3.right, Vector3.ProjectOnPlane(Vector3.up, axis).normalized, airborne);
        DrawWeapon(grip - axis * GripToBase, axis, side.normalized);
    }

    private void MountWeapon(Transform parent)
    {
        if (_weapon.transform.parent != parent)
        {
            _weapon.transform.SetParent(parent, false);
        }
    }

    private void DrawWeapon(Vector3 basePoint, Vector3 axis, Vector3 side)
    {
        Vector3 head = basePoint + axis * ShaftLength;
        _weapon.enabled = true;
        _weapon.positionCount = 6;
        _weapon.SetPosition(0, basePoint);
        _weapon.SetPosition(1, head - axis * 0.18f);
        _weapon.SetPosition(2, head - side * 0.23f);
        _weapon.SetPosition(3, head + axis * 0.18f);
        _weapon.SetPosition(4, head + side * 0.23f);
        _weapon.SetPosition(5, head - axis * 0.18f);
    }

    private LineRenderer CreateLine(string name, Material material, float width)
    {
        var child = new GameObject(name);
        child.transform.SetParent(_root.transform, false);
        var line = child.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.widthMultiplier = width;
        line.useWorldSpace = true;
        line.positionCount = 0;
        line.enabled = false;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return line;
    }

    private static Material MakeMaterial(Color color) =>
        new(Shader.Find("Universal Render Pipeline/Unlit")) { color = color };

    private readonly struct Stance
    {
        public Vector3 Upper { get; }
        public Vector3 Forearm { get; }
        public Vector3 Shaft { get; }
        public float Twist { get; }
        public float Lean { get; }

        public Stance(Vector3 upper, Vector3 forearm, Vector3 shaft, float twist, float lean)
        {
            Upper = upper;
            Forearm = forearm;
            Shaft = shaft;
            Twist = twist;
            Lean = lean;
        }

        public static Stance Blend(Stance from, Stance to, float t) => new(
            Vector3.Slerp(from.Upper, to.Upper, t),
            Vector3.Slerp(from.Forearm, to.Forearm, t),
            Vector3.Slerp(from.Shaft, to.Shaft, t).normalized,
            Mathf.Lerp(from.Twist, to.Twist, t),
            Mathf.Lerp(from.Lean, to.Lean, t));
    }

    private readonly struct Joint
    {
        private readonly Transform _bone;
        private readonly Quaternion _rest;
        private readonly Vector3 _bindRight;

        public Joint(Transform bone, Transform body)
        {
            _bone = bone;
            _rest = bone.localRotation;
            _bindRight = bone.InverseTransformDirection(body.right);
        }

        public void Reset()
        {
            if (_bone != null)
            {
                _bone.localRotation = _rest;
            }
        }

        public void Aim(Transform body, Vector3 bindDirection, Vector3 aimDirection)
        {
            if (_bone != null)
            {
                Reset();
                Vector3 current = _bone.TransformDirection(_bindRight * bindDirection.x);
                _bone.rotation = Quaternion.FromToRotation(current, body.TransformDirection(aimDirection))
                    * _bone.rotation;
            }
        }

        public void Turn(Transform body, float pitch, float yaw)
        {
            if (_bone != null)
            {
                Reset();
                _bone.rotation = Quaternion.AngleAxis(yaw, body.up) * Quaternion.AngleAxis(pitch, body.right)
                    * _bone.rotation;
            }
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
