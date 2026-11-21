using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Renders the Matriarch's Core-authored tells without changing her root pose.</summary>
    [RequireComponent(typeof(EnemyView))]
    public sealed class MatriarchView : MonoBehaviour
    {
        private const int Segments = 64;
        private const float PounceHeight = 1.8f;
        private const float GroundOffset = 0.06f;
        private const float SlabTravelSeconds = 0.25f;
        private const float RainFallSeconds = 0.25f;
        private const int RainFeatherCount = 11;

        [SerializeField] private Transform _model;
        [SerializeField] private Animator _animator;
        [SerializeField] private Renderer _shadow;
        [SerializeField] private Canvas _healthBar;
        [SerializeField] private MeshFilter _warning;
        [SerializeField] private MeshFilter _fill;
        [SerializeField] private Transform _slab;
        [SerializeField] private Transform _feather;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private AudioClip _shoveCue;
        [SerializeField] private AudioClip _pounceCue;
        [SerializeField] private AudioClip _rainCue;
        [SerializeField] private AudioClip _diveCue;

        private readonly int _phaseId = Animator.StringToHash("VulturePhase");
        private readonly int _deadId = Animator.StringToHash("Dead");
        private readonly int _groundPose = Animator.StringToHash("Idle_Ground");
        private readonly int _flightPose = Animator.StringToHash("Flap");
        private readonly int _divePose = Animator.StringToHash("Dive");
        private readonly int _castPose = Animator.StringToHash("Strike");
        private readonly int _landPose = Animator.StringToHash("Land");
        private readonly IDisposable[] _subscriptions = new IDisposable[6];
        private DomainEventHub _events;
        private CameraShake _shake;
        private Renderer[] _bodyRenderers;
        private Mesh _laneMesh;
        private Mesh _ringMesh;
        private Mesh _discMesh;
        private Transform _rain;
        private int _id;
        private bool _airborne;
        private bool _warningActive;
        private MatriarchMove _move;
        private Vector3 _origin;
        private Vector3 _target;
        private float _width;
        private float _radius;
        private float _progress;
        private float _tellSeconds;
        private Vector3 _slabOrigin;
        private Vector3 _slabTarget;
        private float _slabAge;
        private int _pose;
        private int _playedPose;
        private VulturePhase _phase;

        [Inject]
        public void Construct(DomainEventHub events) => _events = events;

        public void Bind(int id, CameraShake shake)
        {
            _bodyRenderers ??= _model.GetComponentsInChildren<Renderer>(true);
            Unbind();
            EnsureMeshes();
            EnsureRain();
            _id = id;
            _shake = shake;
            _subscriptions[0] = _events.Subscribe<MatriarchArmed>(OnArmed);
            _subscriptions[1] = _events.Subscribe<MatriarchTellProgress>(OnProgress);
            _subscriptions[2] = _events.Subscribe<MatriarchTellCleared>(OnCleared);
            _subscriptions[3] = _events.Subscribe<MatriarchFlightChanged>(OnFlight);
            _subscriptions[4] = _events.Subscribe<MatriarchImpact>(OnImpact);
            _subscriptions[5] = _events.Subscribe<EnemyDied>(OnDied);
        }

        public void Unbind()
        {
            _id = EnemyView.Unbound;
            _shake = null;
            for (int i = 0; i < _subscriptions.Length; i++)
            {
                _subscriptions[i]?.Dispose();
                _subscriptions[i] = null;
            }

            ClearTell();
            _airborne = false;
            SetBodyVisible(true);
            _model.localPosition = Vector3.zero;
            _shadow.enabled = false;
            _shadow.transform.localPosition = Vector3.up * (GroundOffset * 0.5f);
            _slab.gameObject.SetActive(false);
            SetPose(_groundPose, VulturePhase.Grounded);
            _playedPose = 0;
        }

        private void OnArmed(MatriarchArmed evt)
        {
            if (evt.BossId != _id)
            {
                return;
            }

            _warningActive = true;
            _move = evt.Move;
            _origin = evt.Origin.ToUnity();
            _target = evt.Target.ToUnity();
            _width = evt.Width;
            _radius = evt.Radius;
            _tellSeconds = evt.Seconds;
            _progress = 0f;
            _warning.sharedMesh = evt.Move == MatriarchMove.RuinShove ? _laneMesh : _ringMesh;
            _warning.gameObject.SetActive(true);
            _fill.gameObject.SetActive(false);
            DrawWarning();

            _audio.Stop();
            _audio.clip = evt.Move switch
            {
                MatriarchMove.RuinShove => _shoveCue,
                MatriarchMove.CarrionPounce => _pounceCue,
                MatriarchMove.FeatherRain => _rainCue,
                _ => _diveCue,
            };
            if (Application.isPlaying && _audio.clip != null)
            {
                _audio.Play();
            }

            switch (evt.Move)
            {
                case MatriarchMove.RuinShove:
                    SetPose(_castPose, VulturePhase.Grounded);
                    break;
                case MatriarchMove.CarrionPounce:
                    SetPose(_flightPose, VulturePhase.Circle);
                    break;
                case MatriarchMove.CarrionDive:
                    SetPose(_divePose, VulturePhase.Dive);
                    break;
            }

            DrawBody();
        }

        private void OnProgress(MatriarchTellProgress evt)
        {
            if (evt.BossId == _id && _warningActive && float.IsFinite(evt.Fraction))
            {
                _progress = Mathf.Clamp01(evt.Fraction);
                DrawWarning();
                DrawBody();
            }
        }

        private void OnCleared(MatriarchTellCleared evt)
        {
            if (evt.BossId != _id || !_warningActive)
            {
                return;
            }

            ClearTell();
            RestorePose();
            DrawBody();
        }

        private void ClearTell()
        {
            _warningActive = false;
            _warning.gameObject.SetActive(false);
            _fill.gameObject.SetActive(false);
            if (_rain != null)
            {
                _rain.gameObject.SetActive(false);
            }

            _audio.Stop();
            _audio.clip = null;
        }

        private void OnFlight(MatriarchFlightChanged evt)
        {
            if (evt.BossId != _id)
            {
                return;
            }

            _airborne = evt.Airborne;
            SetBodyVisible(!_airborne);
            if (!_airborne)
            {
                ClearTell();
            }

            RestorePose();
            DrawBody();
        }

        private void RestorePose()
        {
            SetPose(_airborne ? _flightPose : _groundPose, _airborne ? VulturePhase.Circle : VulturePhase.Grounded);
        }

        private void OnImpact(MatriarchImpact evt)
        {
            if (evt.BossId != _id)
            {
                return;
            }

            if (evt.Move == MatriarchMove.RuinShove && _warningActive && _move == evt.Move)
            {
                _slabOrigin = _origin;
                _slabTarget = evt.Position.ToUnity();
                _slabAge = 0f;
                DrawSlab();
                _slab.gameObject.SetActive(true);
                RestorePose();
            }

            if (evt.Move is MatriarchMove.CarrionPounce or MatriarchMove.CarrionDive)
            {
                _shake?.Kick(CameraShake.SlamStrength);
                SetPose(_landPose, VulturePhase.Grounded);
            }

            ClearTell();
            DrawBody();
            if (evt.Move == MatriarchMove.CarrionDive)
            {
                _model.position = evt.Position.ToUnity();
            }
        }

        private void OnDied(EnemyDied evt)
        {
            if (evt.Id == _id)
            {
                Unbind();
                if (_animator.isInitialized)
                {
                    _animator.SetBool(_deadId, true);
                }
            }
        }

        private void SetPose(int pose, VulturePhase phase)
        {
            _pose = pose;
            _phase = phase;
        }

        private void LateUpdate() => Step(Time.deltaTime);

        public void Step(float dt)
        {
            if (_id == EnemyView.Unbound)
            {
                return;
            }

            if (_playedPose != _pose && _animator.isInitialized)
            {
                _animator.SetBool(_deadId, false);
                _animator.SetInteger(_phaseId, (int)_phase);
                _animator.CrossFadeInFixedTime(_pose, 0.08f);
                _playedPose = _pose;
            }

            DrawBody();
            if (_warningActive)
            {
                DrawWarning();
            }

            if (_slab.gameObject.activeSelf && dt > 0f && float.IsFinite(dt))
            {
                _slabAge += dt;
                DrawSlab();
                _slab.gameObject.SetActive(_slabAge < SlabTravelSeconds);
            }
        }

        private void DrawBody()
        {
            Vector3 ground = transform.position;
            bool pounce = _warningActive && _move == MatriarchMove.CarrionPounce;
            bool dive = _warningActive && _move == MatriarchMove.CarrionDive;
            if (pounce || dive)
            {
                ground = Vector3.Lerp(_origin, _target, _progress);
            }

            // Both visuals inherit the root's Core-authored heading.
            _model.position = pounce
                ? ground + Vector3.up * (PounceHeight * 4f * _progress * (1f - _progress))
                : transform.position;
            _shadow.enabled = _airborne || pounce;
            _shadow.transform.position = ground + Vector3.up * (GroundOffset * 0.5f);
        }

        private void SetBodyVisible(bool visible)
        {
            if (_bodyRenderers == null)
            {
                return;
            }

            foreach (Renderer renderer in _bodyRenderers)
            {
                renderer.forceRenderingOff = !visible;
            }

            _healthBar.enabled = visible;
        }

        private void DrawWarning()
        {
            Vector3 lane = _target - _origin;
            lane.y = 0f;
            if (_move == MatriarchMove.RuinShove)
            {
                Place(_warning.transform, _origin, Heading(lane), new Vector3(_width, 1f, lane.magnitude));
            }
            else
            {
                Place(_warning.transform, _target, Quaternion.identity, new Vector3(_radius, 1f, _radius));
            }

            bool showFill = (_move is MatriarchMove.CarrionDive or MatriarchMove.FeatherRain) && _progress > 0f;
            _fill.gameObject.SetActive(showFill);
            if (showFill)
            {
                float radius = _radius * _progress;
                Place(_fill.transform, _target - Vector3.up * 0.005f, Quaternion.identity, new Vector3(radius, 1f, radius));
            }

            DrawRain();
        }

        private void DrawRain()
        {
            float remaining = (1f - _progress) * _tellSeconds;
            bool falling = _move == MatriarchMove.FeatherRain && remaining <= RainFallSeconds;
            if (falling)
            {
                float heightFraction = Mathf.Clamp01(remaining / RainFallSeconds);
                for (int i = 0; i < RainFeatherCount; i++)
                {
                    float yaw = i * 137.508f;
                    float distance = 0.88f * _radius * Mathf.Sqrt((float)i / (RainFeatherCount - 1));
                    Vector3 floor = _target + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * distance;
                    // The feather prop points down its local Z. Keep its tip at ground height on impact.
                    float height = 0.27f + (3.6f + 0.1f * i) * heightFraction;
                    Place(_rain.GetChild(i), floor + Vector3.up * height,
                        Quaternion.Euler(80f, yaw, 0f), Vector3.one * 0.55f);
                }
            }

            _rain.gameObject.SetActive(falling);
        }

        private void EnsureRain()
        {
            if (_rain != null)
            {
                return;
            }

            _rain = new GameObject("Feather Rain").transform;
            _rain.gameObject.SetActive(false);
            _rain.SetParent(transform, false);
            for (int i = 0; i < RainFeatherCount; i++)
            {
                Transform feather = Instantiate(_feather, _rain);
                feather.GetComponent<ProjectileView>().enabled = false;
            }
        }

        private void DrawSlab()
        {
            Place(_slab, Vector3.Lerp(_slabOrigin, _slabTarget, Mathf.Clamp01(_slabAge / SlabTravelSeconds)),
                Heading(_slabTarget - _slabOrigin), Vector3.one);
        }

        private static Quaternion Heading(Vector3 delta)
        {
            delta.y = 0f;
            return delta.sqrMagnitude > 0f ? Quaternion.LookRotation(delta) : Quaternion.identity;
        }

        private void Place(Transform mark, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            mark.SetPositionAndRotation(position + Vector3.up * GroundOffset, rotation);
            Vector3 parentScale = transform.lossyScale;
            mark.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
        }

        private void EnsureMeshes()
        {
            if (_laneMesh != null)
            {
                return;
            }

            _laneMesh = CreateMesh("Matriarch lane", new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(-0.5f, 0f, 1f),
                new Vector3(0.5f, 0f, 1f), new Vector3(0.5f, 0f, 0f),
            }, new[] { 0, 1, 2, 0, 2, 3 }, 0.8f);
            _ringMesh = CreateCircle(true);
            _discMesh = CreateCircle(false);
            _fill.sharedMesh = _discMesh;
        }

        private static Mesh CreateCircle(bool rim)
        {
            var vertices = new Vector3[Segments * 2];
            var triangles = new int[Segments * 6];
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * (2f * Mathf.PI / Segments);
                Vector3 outer = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                int a = i * 2;
                int b = (a + 2) % vertices.Length;
                int index = i * 6;
                vertices[a] = outer;
                vertices[a + 1] = rim ? outer * 0.92f : Vector3.zero;
                triangles[index] = a;
                triangles[index + 1] = b;
                triangles[index + 2] = a + 1;
                triangles[index + 3] = b;
                triangles[index + 4] = b + 1;
                triangles[index + 5] = a + 1;
            }

            return CreateMesh(rim ? "Matriarch rim" : "Matriarch fill", vertices, triangles, rim ? 1f : 0.5f);
        }

        private static Mesh CreateMesh(string meshName, Vector3[] vertices, int[] triangles, float opacity)
        {
            Color tint = Palette.Danger;
            tint.a = opacity;
            var colors = new Color[vertices.Length];
            Array.Fill(colors, Palette.Vertex(tint));
            return new Mesh { name = meshName, vertices = vertices, triangles = triangles, colors = colors };
        }

        private void OnDisable()
        {
            if (_id != EnemyView.Unbound)
            {
                Unbind();
            }
        }

        private void OnDestroy()
        {
            OnDisable();
            ReleaseMesh(_laneMesh);
            ReleaseMesh(_ringMesh);
            ReleaseMesh(_discMesh);
        }

        private static void ReleaseMesh(Mesh mesh)
        {
            if (Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh);
            }
        }
    }
}
