using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VContainer;

namespace Soulvail.Game.Views
{
    [RequireComponent(typeof(EnemyView))]
    public sealed class HierophantView : MonoBehaviour, IPoolable
    {
        private const int ArcSegments = 64;
        private const float ReleaseSeconds = 0.24f;
        private const float SweepStrike = 0.35f;

        [SerializeField] private Animator _animator;
        [SerializeField] private AnimationClip _sweepMotion;
        [SerializeField] private AnimationClip _raiseMotion;
        [SerializeField] private AnimationClip _sealReleaseMotion;
        [SerializeField] private Material _footprintMaterial;
        [SerializeField] private GameObject _sealPrefab;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private AudioClip _sweepCue;
        [SerializeField] private AudioClip _sealCue;

        private readonly MeshRenderer[] _sealLights = new MeshRenderer[3];
        private readonly Vector3[] _vertices = new Vector3[ArcSegments + 2];
        private readonly Color[] _colors = new Color[ArcSegments + 2];
        private readonly int[] _sector = new int[ArcSegments * 3];
        private readonly int[] _rectangle = { 0, 1, 2, 2, 1, 3 };

        private IDisposable[] _subscriptions;
        private EnemyView _body;
        private Transform _floor;
        private Transform _footprint;
        private MeshRenderer _warning;
        private MeshRenderer _fill;
        private MeshRenderer _release;
        private Mesh _warningMesh;
        private Mesh _fillMesh;
        private Mesh _releaseMesh;
        private HierophantArmed _cast;
        private string _pose;
        private float _progress;
        private float _flash;
        private float _recovery;
        private bool _dead;

        [Inject]
        public void Construct(DomainEventHub events)
        {
            DisposeSubscriptions();
            _body = GetComponent<EnemyView>();
            _subscriptions = new[]
            {
                events.Subscribe<HierophantSealsPlaced>(OnSealsPlaced),
                events.Subscribe<HierophantArmed>(OnArmed),
                events.Subscribe<HierophantTellProgress>(OnProgress),
                events.Subscribe<HierophantImpact>(OnImpact),
                events.Subscribe<BossPhaseChanged>(OnPhase),
                events.Subscribe<EnemyDied>(OnDeath),
            };
            OnSpawn();
        }

        public void OnSpawn() => ResetPresentation();
        public void OnDespawn() => ResetPresentation();
        private void OnEnable() => Play("Idle");
        private void OnDisable() => ResetPresentation();

        private bool IsMine(int id) =>
            !_dead && isActiveAndEnabled && _body != null && _body.IsBound && _body.Id == id;

        private void CreateFloor()
        {
            _floor = new GameObject("Hierophant floor visuals").transform;
            _floor.gameObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(_floor.gameObject, gameObject.scene);
            _footprint = new GameObject("Footprint").transform;
            _footprint.SetParent(_floor, false);
            _warning = CreateLayer("Warning", 0, out _warningMesh);
            _fill = CreateLayer("Progress", 1, out _fillMesh);
            _release = CreateLayer("Release", 2, out _releaseMesh);

            for (int i = 0; i < ArcSegments; i++)
            {
                _sector[i * 3 + 1] = i + 1;
                _sector[i * 3 + 2] = i + 2;
            }

            var lightColor = new MaterialPropertyBlock();
            lightColor.SetColor("_BaseColor", Color.Lerp(Palette.Danger, Color.white, 0.55f));
            var plaqueColor = new MaterialPropertyBlock();
            for (int i = 0; i < _sealLights.Length; i++)
            {
                GameObject seal = Instantiate(_sealPrefab, _floor);
                var plaque = seal.transform.Find("Sandstone plaque").GetComponent<MeshRenderer>();
                plaqueColor.SetColor("_BaseColor", Color.Lerp(plaque.sharedMaterial.color, Color.black, 0.3f));
                plaque.SetPropertyBlock(plaqueColor);
                _sealLights[i] = seal.transform.Find("Light").GetComponent<MeshRenderer>();
                _sealLights[i].SetPropertyBlock(lightColor);
            }
        }

        private MeshRenderer CreateLayer(string label, int order, out Mesh mesh)
        {
            var layer = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            layer.transform.SetParent(_footprint, false);
            layer.transform.localPosition = Vector3.up * (0.075f + order * 0.01f);
            var renderer = layer.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _footprintMaterial;
            renderer.sortingOrder = order;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            mesh = new Mesh { name = "Hierophant " + label };
            mesh.MarkDynamic();
            layer.GetComponent<MeshFilter>().sharedMesh = mesh;
            return renderer;
        }

        private void OnSealsPlaced(HierophantSealsPlaced evt)
        {
            if (!IsMine(evt.BossId))
                return;

            if (_floor == null)
                CreateFloor();

            PlaceSeal(0, evt.West);
            PlaceSeal(1, evt.Centre);
            PlaceSeal(2, evt.East);
            _floor.gameObject.SetActive(true);
        }

        private void PlaceSeal(int slot, System.Numerics.Vector2 centre) =>
            _sealLights[slot].transform.parent.position = new Vector3(centre.X, 0f, centre.Y);

        private void OnArmed(HierophantArmed evt)
        {
            if (!IsMine(evt.BossId) || _floor == null || !_floor.gameObject.activeSelf)
                return;

            ClearAttack();
            _cast = evt;
            bool sweep = evt.Move == HierophantMove.StaffSweep;
            var facing = new Vector3(evt.Facing.X, 0f, evt.Facing.Y);
            Quaternion heading = facing != Vector3.zero
                ? Quaternion.LookRotation(facing)
                : Quaternion.identity;
            _footprint.SetPositionAndRotation(evt.Origin.ToUnity(), heading);
            Draw(_warningMesh, 1f, 0.48f);
            _warning.enabled = true;
            SetSealLight(true);

            if (sweep)
                Play("SweepTell", _sweepMotion.length * SweepStrike / evt.Seconds);
            else
                Play("SealTell", _raiseMotion.length / evt.Seconds);

            _audio.clip = sweep ? _sweepCue : _sealCue;
            if (Application.isPlaying)
                _audio.Play();
        }

        private void OnProgress(HierophantTellProgress evt)
        {
            if (!IsMine(evt.BossId) || _cast.Seconds == 0f || _flash > 0f || evt.Fraction <= _progress)
                return;

            _progress = evt.Fraction;
            Draw(_fillMesh, _progress, 0.8f);
            _fill.enabled = true;
        }

        private void Draw(Mesh mesh, float fraction, float opacity)
        {
            int vertexCount;
            int[] triangles;
            float reach = _cast.Reach * fraction;
            if (_cast.Move == HierophantMove.StaffSweep)
            {
                _vertices[0] = Vector3.zero;
                for (int i = 0; i <= ArcSegments; i++)
                {
                    float radians = Mathf.Lerp(-_cast.HalfAngleDegrees, _cast.HalfAngleDegrees,
                        (float)i / ArcSegments) * Mathf.Deg2Rad;
                    _vertices[i + 1] = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * reach;
                }
                vertexCount = ArcSegments + 2;
                triangles = _sector;
            }
            else
            {
                float width = _cast.HalfWidth;
                _vertices[0] = new Vector3(-width, 0f, 0f);
                _vertices[1] = new Vector3(-width, 0f, reach);
                _vertices[2] = new Vector3(width, 0f, 0f);
                _vertices[3] = new Vector3(width, 0f, reach);
                vertexCount = 4;
                triangles = _rectangle;
            }

            if (mesh.vertexCount != vertexCount)
                mesh.Clear();
            mesh.SetVertices(_vertices, 0, vertexCount);
            mesh.SetTriangles(triangles, 0);
            Tint(mesh, Palette.Danger, opacity);
        }

        private void Tint(Mesh mesh, Color color, float opacity)
        {
            color.a = opacity;
            Array.Fill(_colors, Palette.Vertex(color), 0, mesh.vertexCount);
            mesh.SetColors(_colors, 0, mesh.vertexCount);
        }

        private void OnImpact(HierophantImpact evt)
        {
            if (!IsMine(evt.BossId) || _cast.Seconds == 0f || _flash > 0f
                || _cast.Move != evt.Move || _cast.SealSlot != evt.SealSlot)
                return;

            HideWarning();
            Draw(_releaseMesh, 1f, 1f);
            Tint(_releaseMesh, Color.Lerp(Palette.Danger, Color.white, 0.65f), 0.94f);
            _release.enabled = true;
            _flash = ReleaseSeconds;
            _recovery = evt.RecoverySeconds;
            if (evt.Move == HierophantMove.StaffSweep)
                Play("SweepRelease", _sweepMotion.length * (1f - SweepStrike) / ReleaseSeconds, SweepStrike);
            else
                Play("SealRelease", _sealReleaseMotion.length / ReleaseSeconds);
        }

        private void OnPhase(BossPhaseChanged evt)
        {
            if (IsMine(evt.EnemyId))
            {
                ClearAttack();
                Play("Idle");
            }
        }

        private void OnDeath(EnemyDied evt)
        {
            if (IsMine(evt.Id))
            {
                ResetPresentation();
                _dead = true;
                Play("Death");
            }
        }

        public void Step(float dt)
        {
            if (_body == null || !IsMine(_body.Id) || !(dt >= 0f) || !float.IsFinite(dt))
                return;

            _recovery = Mathf.Max(0f, _recovery - dt);
            if (_flash > 0f)
            {
                _flash = Mathf.Max(0f, _flash - dt);
                if (_flash > 0f)
                {
                    Tint(_releaseMesh, Color.Lerp(Palette.Danger, Color.white, 0.65f),
                        0.94f * _flash / ReleaseSeconds);
                    return;
                }

                _release.enabled = false;
                SetSealLight(false);
                _cast = default;
            }

            if (_cast.Seconds != 0f)
                return;

            string pose = _recovery > 0f ? "Recovery" : _body.Velocity.sqrMagnitude > 0.01f ? "Walk" : "Idle";
            if (_pose != pose)
                Play(pose);
        }

        private void LateUpdate() => Step(Time.deltaTime);

        private void Play(string state, float speed = 1f, float start = 0f)
        {
            _pose = state;
            if (_animator == null)
                return;

            _animator.speed = speed;
            if (_animator.isActiveAndEnabled)
                _animator.Play(state, 0, start);
        }

        private void SetSealLight(bool lit)
        {
            if (_cast.Move == HierophantMove.SunSeal && (uint)_cast.SealSlot < (uint)_sealLights.Length)
                _sealLights[_cast.SealSlot].enabled = lit;
        }

        private void HideWarning()
        {
            _warning.enabled = false;
            _fill.enabled = false;
            _audio.Stop();
            _audio.clip = null;
        }

        private void ClearAttack()
        {
            if (_floor != null)
            {
                HideWarning();
                _release.enabled = false;
                SetSealLight(false);
            }
            _cast = default;
            _progress = _flash = _recovery = 0f;
        }

        private void ResetPresentation()
        {
            ClearAttack();
            if (_floor != null)
                _floor.gameObject.SetActive(false);
            _dead = false;
            Play("Idle");
        }

        private void DisposeSubscriptions()
        {
            if (_subscriptions == null)
                return;

            foreach (IDisposable subscription in _subscriptions)
                subscription.Dispose();
        }

        private void OnDestroy()
        {
            DisposeSubscriptions();
            if (Application.isPlaying)
            {
                if (_floor != null)
                    Destroy(_floor.gameObject);
                Destroy(_warningMesh);
                Destroy(_fillMesh);
                Destroy(_releaseMesh);
            }
            else
            {
                if (_floor != null)
                    DestroyImmediate(_floor.gameObject);
                DestroyImmediate(_warningMesh);
                DestroyImmediate(_fillMesh);
                DestroyImmediate(_releaseMesh);
            }
        }
    }
}
