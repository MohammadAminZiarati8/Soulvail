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

    private static readonly Stance Rest = new(
        new(0.20f, -0.97f, -0.10f), new(0.27f, -0.33f, 0.90f), new(0.10f, 0.65f, 0.75f), 0f, 0f);
    private static readonly Stance SweepReady = new(
        new(0.76f, -0.46f, -0.45f), new(0.33f, 0.36f, 0.86f), new(0.96f, 0.23f, 0.16f), 32f, -3f);
    private static readonly Stance SweepStrike = new(
        new(0.03f, -0.31f, 0.95f), new(-0.24f, 0.04f, 0.97f), new(-0.97f, 0.10f, 0.22f), -37f, 8f);
    private static readonly Stance CastReady = new(
        new(0.67f, 0.36f, -0.65f), new(0.10f, 0.91f, -0.40f), new(0.23f, -0.12f, -0.97f), 28f, -9f);
    private static readonly Stance Reach = new(
        new(0.34f, -0.33f, 0.88f), new(0.12f, 0.08f, 0.99f), new(0.14f, 0.90f, 0.40f), -13f, 8f);
    private static readonly Stance SlamReady = new(
        new(0.53f, 0.79f, 0.28f), new(0.25f, 0.91f, -0.32f), new(0.10f, 0.20f, -0.97f), 11f, -12f);
    private static readonly Stance SlamStrike = new(
        new(0.43f, -0.68f, 0.60f), new(0.25f, -0.42f, 0.88f), new(0.10f, -0.96f, 0.25f), -8f, 32f);

    private readonly int _id;
    private readonly EnemyView _body;
    private readonly Transform _model;
    private readonly Quaternion _modelRest;
    private readonly AnimationClip _walk;
    private readonly Joint[] _joints;
    private readonly GameObject _floor;
    private readonly Material _danger;
    private readonly WarningShape _footprint;
    private readonly WarningShape _landing;
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

    public BuriedKingView(EnemyView body, int id, DomainEventHub hub)
    {
        _body = body;
        _id = id;
        var rig = body.GetComponent<BuriedKingRig>();
        _model = rig.Model;
        _modelRest = _model.localRotation;
        _visualFacing = _model.rotation;
        _walk = rig.Walk;
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
        float progress = _warningSeconds > 0f ? Mathf.Clamp01(_poseTime / _warningSeconds) : 0f;
        UpdateStance(dt, progress);
        _footprint.Fill(progress, _pose == BuriedKingPose.FractureTell);
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

        _footprint.Clear();
        _landing.Clear();
        _warningSeconds = state.WarningSeconds;
        _newWarning = _warningSeconds > 0f;
        Vector3 start = state.Start.ToUnity();
        Vector3 end = state.End.ToUnity();
        switch (_pose)
        {
            case BuriedKingPose.SweepTell:
                PoseToward(SweepReady, _warningSeconds * 0.65f);
                _footprint.Arc(start, Vector3.ProjectOnPlane(end - start, Vector3.up), state.SweepDegrees);
                break;
            case BuriedKingPose.CastTell:
                PoseToward(CastReady, _warningSeconds * 0.69f);
                _footprint.Lane(start, end, state.Width);
                _landing.Arc(end, Vector3.forward * (state.Width * 0.5f), 360f);
                break;
            case BuriedKingPose.Outbound:
                _releaseGrip = _grip.position;
                _releaseRotation = _grip.rotation;
                PoseToward(Reach, 0.12f);
                break;
            case BuriedKingPose.Embedded:
                PoseToward(Rest, 0.30f);
                DrawEmbedded(end);
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
                PoseToward(Rest, 0.38f);
                break;
            case BuriedKingPose.Cleared:
                _sword.gameObject.SetActive(false);
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
        _stance = Stance.Blend(_from, _target, Mathf.SmoothStep(0f, 1f, _poseTime / _poseDuration));
        // Ease through the weight transfer and finish the swing on the core's impact frame.
        _stance = _pose switch
        {
            BuriedKingPose.SweepTell when progress > 0.70f =>
                Stance.Blend(SweepReady, SweepStrike, StrikeProgress(progress, 0.70f)),
            BuriedKingPose.CastTell when progress > 0.74f =>
                Stance.Blend(CastReady, Reach, StrikeProgress(progress, 0.74f)),
            BuriedKingPose.SlamTell when progress > 0.70f =>
                Stance.Blend(SlamReady, SlamStrike, StrikeProgress(progress, 0.70f)),
            _ => _stance,
        };

        Vector3 velocity = _body.Velocity;
        float speed = velocity.magnitude;
        Quaternion facing = _body.transform.rotation * _modelRest;
        _visualFacing = Quaternion.RotateTowards(_visualFacing, facing, dt * (_warningSeconds > 0f ? 560f : 320f));
        _model.rotation = _visualFacing;

        // Distance drives the in-place cycle. Playing it backwards keeps the King's eyes on
        // the player when he backs off, and collisions slow his feet with his actual movement.
        float direction = Vector3.Dot(velocity, _body.transform.forward) < 0f ? -1f : 1f;
        _stride = Mathf.Repeat(_stride + direction * dt * speed / (_model.lossyScale.x * WalkCycleDistance), 1f);
        _gait = Mathf.MoveTowards(_gait, Mathf.Clamp01(speed / 1.4f), 6f * dt);
        if (_gait > 0f)
        {
            _walk.SampleAnimation(_model.gameObject, _stride * _walk.length);
        }

        foreach (Joint joint in _joints)
        {
            joint.BlendWalk(_gait);
        }

        float counterSwing = Mathf.Sin(_stride * Mathf.PI * 2f) * _gait * direction;
        _chest.Turn(_model, _stance.Lean + _gait * 4f * direction, _stance.Twist);
        _rightUpperArm.Aim(_model, 1f, _stance.Upper);
        _rightForearm.Aim(_model, 1f, _stance.Forearm);
        _leftUpperArm.Aim(_model, -1f, new Vector3(-0.29f, -0.91f, -0.14f + counterSwing * 0.31f));
        _leftForearm.Aim(_model, -1f, new Vector3(-0.15f, -0.83f, 0.53f + counterSwing * 0.31f));

        Vector3 blade = _model.TransformDirection(_stance.Blade);
        // The grip crosses the palm. Orient the hand and sword together so the wrist
        // carries the blade through the entire wind-up, impact and follow-through.
        Vector3 knuckles = _model.TransformDirection(_stance.Forearm);
        Vector3 bladeFace = Vector3.Cross(blade, knuckles).normalized;
        _hand.rotation = Quaternion.LookRotation(bladeFace, blade) * Quaternion.Inverse(_gripFromHand);
    }

    private static float StrikeProgress(float progress, float start) =>
        Mathf.SmoothStep(0f, 1f, (progress - start) / (1f - start));

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

    private readonly struct Joint
    {
        private readonly Transform _bone;
        private readonly Vector3 _restPosition;
        private readonly Quaternion _restRotation;
        private readonly Vector3 _restScale;
        private readonly Vector3 _bindRight;

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
