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
        private const float RockTravelSeconds = 0.3f;
        private const float RainFallSeconds = 0.25f;
        private const float FeatherHoldSeconds = 2.5f;
        private const float FeatherFadeSeconds = 0.75f;
        private const int RainFeatherCount = 11;

        private struct RainVisual
        {
            public Transform Root;
            public Renderer[] Renderers;
            public Material[] Materials;
            public Color[] Colours;
            public Vector3 Centre;
            public float Age;
        }

        [SerializeField] private Transform _model;
        [SerializeField] private Animator _animator;
        [SerializeField] private Renderer _shadow;
        [SerializeField] private Canvas _healthBar;
        [SerializeField] private MeshFilter _warning;
        [SerializeField] private MeshFilter _fill;
        [SerializeField] private Transform _rockPrefab;
        [SerializeField] private SlamDustView _dustPrefab;
        [SerializeField] private Transform _feather;
        [SerializeField] private Material _featherFade;
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
        private readonly int _baseColourId = Shader.PropertyToID("_BaseColor");
        private readonly IDisposable[] _subscriptions = new IDisposable[6];
        private readonly RainVisual[] _rain = new RainVisual[2];
        private readonly SlamDustView[] _dust = new SlamDustView[2];
        private readonly Vector3[] _dustCentres = new Vector3[2];
        private DomainEventHub _events;
        private CameraShake _shake;
        private Renderer[] _bodyRenderers;
        private Mesh _laneMesh;
        private Mesh _ringMesh;
        private Mesh _discMesh;
        private Transform _rock;
        private MaterialPropertyBlock _featherBlock;
        private int _fallingRain = -1;
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
        private int _pose;
        private int _playedPose;
        private VulturePhase _phase;

        [Inject]
        public void Construct(DomainEventHub events) => _events = events;

        public void Bind(int id, CameraShake shake)
        {
            _bodyRenderers ??= _model.GetComponentsInChildren<Renderer>(true);
            EnsureEffects();
            Unbind();
            EnsureMeshes();
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
            if (_rock != null)
            {
                for (int i = 0; i < _rain.Length; i++)
                {
                    _rain[i].Root.gameObject.SetActive(false);
                }

                foreach (SlamDustView dust in _dust)
                {
                    dust.OnDespawn();
                    dust.gameObject.SetActive(false);
                }
            }

            SetPose(_groundPose, VulturePhase.Grounded);
            _playedPose = 0;
        }

        private void OnArmed(MatriarchArmed evt)
        {
            if (evt.BossId != _id)
            {
                return;
            }

            ClearTell();
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
            if (_rock != null)
            {
                _rock.gameObject.SetActive(false);
            }

            if (_fallingRain >= 0)
            {
                _rain[_fallingRain].Root.gameObject.SetActive(false);
                _fallingRain = -1;
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
                PlayDust(0, evt.Position.ToUnity());
                RestorePose();
            }

            if (evt.Move is MatriarchMove.CarrionPounce or MatriarchMove.CarrionDive)
            {
                PlayDust(1, evt.Position.ToUnity());
                _shake?.Kick(CameraShake.SlamStrength);
                SetPose(_landPose, VulturePhase.Grounded);
            }

            if (evt.Move == MatriarchMove.FeatherRain && _warningActive && _move == evt.Move)
            {
                DrawRain(0f);
                _fallingRain = -1;
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

            if (dt > 0f && float.IsFinite(dt))
            {
                StepEffects(dt);
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

            bool showFill = _move != MatriarchMove.RuinShove && _progress > 0f;
            _fill.gameObject.SetActive(showFill);
            if (showFill)
            {
                float radius = _radius * _progress;
                Place(_fill.transform, _target - Vector3.up * 0.005f, Quaternion.identity, new Vector3(radius, 1f, radius));
            }

            float remaining = (1f - _progress) * _tellSeconds;
            if (_move == MatriarchMove.FeatherRain && remaining <= RainFallSeconds)
            {
                DrawRain(remaining);
            }
            else if (_move == MatriarchMove.RuinShove && remaining <= RockTravelSeconds)
            {
                DrawRock(1f - remaining / RockTravelSeconds, lane);
            }
        }

        private void DrawRain(float remaining)
        {
            if (_fallingRain < 0)
            {
                // Keep an earlier impact on the ground while the next cast falls.
                _fallingRain = !_rain[0].Root.gameObject.activeSelf ||
                    (_rain[1].Root.gameObject.activeSelf && _rain[0].Age >= _rain[1].Age) ? 0 : 1;

                ref RainVisual next = ref _rain[_fallingRain];
                next.Centre = _target;
                next.Age = 0f;
                for (int i = 0; i < next.Renderers.Length; i++)
                {
                    next.Renderers[i].sharedMaterial = next.Materials[i];
                    next.Renderers[i].SetPropertyBlock(null);
                }

                next.Root.gameObject.SetActive(true);
            }

            Transform root = _rain[_fallingRain].Root;
            Place(root, _target, Quaternion.identity, Vector3.one);
            float heightFraction = Mathf.Clamp01(remaining / RainFallSeconds);
            for (int i = 0; i < RainFeatherCount; i++)
            {
                float yaw = i * 137.508f;
                float distance = 0.88f * _radius * Mathf.Sqrt((float)i / (RainFeatherCount - 1));
                Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
                float height = 0.27f + (3.6f + 0.1f * i) * heightFraction;
                root.GetChild(i).SetLocalPositionAndRotation(heading * Vector3.forward * distance + Vector3.up * height,
                    heading * Quaternion.Euler(80f, 0f, 0f));
            }
        }

        private void StepEffects(float dt)
        {
            for (int i = 0; i < _rain.Length; i++)
            {
                ref RainVisual rain = ref _rain[i];
                if (i == _fallingRain || !rain.Root.gameObject.activeSelf)
                {
                    continue;
                }

                Place(rain.Root, rain.Centre, Quaternion.identity, Vector3.one);
                rain.Age += dt;
                float fade = (rain.Age - FeatherHoldSeconds) / FeatherFadeSeconds;
                if (fade >= 1f)
                {
                    rain.Root.gameObject.SetActive(false);
                }
                else if (fade > 0f)
                {
                    for (int j = 0; j < rain.Renderers.Length; j++)
                    {
                        Renderer renderer = rain.Renderers[j];
                        renderer.sharedMaterial = _featherFade;
                        Color colour = rain.Colours[j];
                        colour.a *= 1f - fade;
                        _featherBlock.SetColor(_baseColourId, colour);
                        renderer.SetPropertyBlock(_featherBlock);
                    }
                }
            }

            for (int i = 0; i < _dust.Length; i++)
            {
                SlamDustView dust = _dust[i];
                if (dust.IsLive)
                {
                    dust.transform.SetPositionAndRotation(_dustCentres[i], Quaternion.identity);
                    dust.gameObject.SetActive(dust.Step(dt));
                }
            }
        }

        private void EnsureEffects()
        {
            if (_rock != null)
            {
                return;
            }

            _featherBlock = new MaterialPropertyBlock();
            for (int i = 0; i < _rain.Length; i++)
            {
                Transform root = new GameObject(i == 0 ? "Feather Rain" : "Feather Rain (2)").transform;
                root.gameObject.SetActive(false);
                root.SetParent(transform, false);
                for (int j = 0; j < RainFeatherCount; j++)
                {
                    Transform feather = Instantiate(_feather, root);
                    feather.localScale = Vector3.one * 0.55f;
                    feather.GetComponent<ProjectileView>().enabled = false;
                }

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                var materials = new Material[renderers.Length];
                var colours = new Color[renderers.Length];
                for (int j = 0; j < renderers.Length; j++)
                {
                    materials[j] = renderers[j].sharedMaterial;
                    colours[j] = materials[j].GetColor(_baseColourId);
                }

                _rain[i] = new RainVisual { Root = root, Renderers = renderers, Materials = materials, Colours = colours };
            }

            Transform impacts = new GameObject("Impact Effects").transform;
            impacts.gameObject.SetActive(false);
            impacts.SetParent(transform, false);
            _rock = Instantiate(_rockPrefab, impacts);
            _rock.name = "Thrown Rock";
            _rock.GetComponent<ProjectileView>().enabled = false;
            _rock.gameObject.SetActive(false);
            for (int i = 0; i < _dust.Length; i++)
            {
                _dust[i] = Instantiate(_dustPrefab, impacts);
                _dust[i].gameObject.SetActive(false);
            }

            impacts.gameObject.SetActive(true);
        }

        private void PlayDust(int index, Vector3 position)
        {
            _dustCentres[index] = position;
            _dust[index].Play(position, _id);
            _dust[index].gameObject.SetActive(true);
        }

        private void DrawRock(float progress, Vector3 lane)
        {
            Vector3 position = Vector3.Lerp(_origin, _target, progress);
            position.y += Mathf.Lerp(1.3f, 0.4f, progress) + 8f * progress * (1f - progress);
            Place(_rock, position, Heading(lane) * Quaternion.Euler(160f * progress, 0f, 0f),
                Vector3.one * 2.4f);
            _rock.gameObject.SetActive(true);
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
