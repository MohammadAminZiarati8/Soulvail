using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Draws core's locked attacks; animation and cosmetic time never decide a hit.</summary>
    [RequireComponent(typeof(EnemyView))]
    public sealed class DuneclawView : MonoBehaviour, IPoolable
    {
        private const int Segments = 64;
        private const float TurnDegreesPerSecond = 360f;
        [SerializeField] private Animator _animator;
        [SerializeField] private Transform _model;
        [SerializeField] private MeshFilter _tell;
        [SerializeField] private Renderer _tellRenderer;
        [SerializeField] private Renderer _claw;
        [SerializeField] private Renderer _stinger;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private AudioClip _sweepCue;
        [SerializeField] private AudioClip _slamCue;

        [SerializeField] private Transform[] _slabs = new Transform[2];
        [SerializeField] private GameObject[] _slabTiles = new GameObject[2];
        [SerializeField] private ParticleSystem[] _slabDust = new ParticleSystem[2];
        private IDisposable _slabPlaced;
        private IDisposable _slabShattered;

        private readonly Vector3[] _vertices = new Vector3[Segments + 2];
        private readonly Color[] _colors = new Color[Segments + 2];
        private readonly int _colorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _block;
        private Mesh _mesh;
        private EnemyView _body;
        private AnimationClip[] _clips;
        private IDisposable _armed;
        private IDisposable _impact;
        private IDisposable _phase;
        private IDisposable _hit;
        private IDisposable _death;
        private float _duration;
        private float _elapsed;
        private float _remaining;
        private float _strikeRemaining;
        private bool _dead;
        private bool _committed;
        private bool _slam;
        private Quaternion _heading;
        private Quaternion _visualHeading;
        private Vector3 _tellCentre;

        public bool IsShowing => _tellRenderer != null && _tellRenderer.enabled;
        public string State { get; private set; } = "Idle";
        public float RecoveryRemaining => _remaining;

        [Inject]
        public void Construct(DomainEventHub events)
        {
            DisposeSubscriptions();
            _body = GetComponent<EnemyView>();
            _clips = _animator.runtimeAnimatorController.animationClips;
            EnsureMesh();
            _slabPlaced = events.Subscribe<DuneclawSlabPlaced>(OnSlabPlaced);
            _slabShattered = events.Subscribe<DuneclawSlabShattered>(OnSlabShattered);
            _armed = events.Subscribe<DuneclawArmed>(OnArmed);
            _impact = events.Subscribe<DuneclawImpact>(OnImpact);
            _phase = events.Subscribe<BossPhaseChanged>(OnPhase);
            _hit = events.Subscribe<EnemyDamaged>(OnHit);
            _death = events.Subscribe<EnemyDied>(OnDeath);
            OnSpawn();
        }

        public void OnSpawn() => ResetPresentation();
        public void OnDespawn() => ResetPresentation();
        private void OnDisable() => ResetPresentation();
        private bool IsMine(int id) => !_dead && isActiveAndEnabled && _body != null && _body.IsBound && _body.Id == id;

        private void EnsureMesh()
        {
            if (_mesh != null)
            {
                return;
            }
            _block = new MaterialPropertyBlock();
            _mesh = new Mesh { name = "Duneclaw tell (pooled)" };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            var triangles = new int[Segments * 3];
            for (int i = 0; i < Segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
            _mesh.triangles = triangles;
            _tell.sharedMesh = _mesh;
        }

        private void OnSlabPlaced(DuneclawSlabPlaced evt)
        {
            if (!IsMine(evt.BossId) || (uint)evt.Slot >= (uint)_slabs.Length || _slabs[evt.Slot] == null)
            {
                return;
            }
            Transform slab = _slabs[evt.Slot];
            slab.SetParent(transform.parent, false);
            slab.SetPositionAndRotation(evt.Position.ToUnity(), Quaternion.identity);
            slab.localScale = Vector3.one;
            _slabDust[evt.Slot].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _slabTiles[evt.Slot].SetActive(true);
            slab.gameObject.SetActive(true);
        }

        private void OnSlabShattered(DuneclawSlabShattered evt)
        {
            if (!IsMine(evt.BossId) || (uint)evt.Slot >= (uint)_slabs.Length
                || _slabs[evt.Slot] == null || !_slabTiles[evt.Slot].activeSelf)
            {
                return;
            }
            _slabTiles[evt.Slot].SetActive(false);
            _slabDust[evt.Slot].Play(true);
        }

        private void ClearSlabs()
        {
            for (int i = 0; i < _slabs.Length; i++)
            {
                if (_slabs[i] == null)
                {
                    continue;
                }
                _slabDust[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _slabs[i].gameObject.SetActive(false);
                _slabs[i].SetParent(transform, false);
                _slabTiles[i].SetActive(false);
            }
        }

        private void OnArmed(DuneclawArmed evt)
        {
            if (!IsMine(evt.BossId))
            {
                return;
            }
            ClearTell();
            _slam = evt.Move == DuneclawMove.Slam;
            _committed = true;
            _remaining = _strikeRemaining = 0f;
            _duration = evt.Seconds;
            _elapsed = 0f;
            Vector3 facing = new Vector3(evt.Facing.X, 0f, evt.Facing.Y);
            Vector3 aim = _slam ? evt.Target.ToUnity() - transform.position : facing;
            aim.y = 0f;
            _heading = aim.sqrMagnitude > 0f ? Quaternion.LookRotation(aim) : transform.rotation;
            _tellCentre = _slam ? evt.Target.ToUnity() : evt.Origin.ToUnity();
            Pose();
            float arc = _slam ? 360f : evt.ArcDegrees;
            float bearing = _slam ? 0f : Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            _vertices[0] = Vector3.zero;
            for (int i = 0; i <= Segments; i++)
            {
                float angle = (bearing - arc * 0.5f + arc * i / Segments) * Mathf.Deg2Rad;
                _vertices[i + 1] = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * evt.Radius;
            }
            _mesh.vertices = _vertices;
            _mesh.RecalculateBounds();
            _tellRenderer.enabled = true;
            (_slam ? _stinger : _claw).enabled = true;
            PaintTell();
            Play(_slam ? "SlamWindup" : "SweepWindup", evt.Seconds);
            _audio.Stop();
            _audio.clip = _slam ? _slamCue : _sweepCue;
            _audio.Play();
        }

        private void OnImpact(DuneclawImpact evt)
        {
            if (!IsMine(evt.BossId))
            {
                return;
            }
            ClearTell();
            _slam = evt.Move == DuneclawMove.Slam;
            if (_slam)
            {
                Vector3 aim = evt.Target.ToUnity() - transform.position;
                aim.y = 0f;
                if (aim.sqrMagnitude > 0f)
                {
                    _heading = Quaternion.LookRotation(aim);
                }
            }
            _committed = true;
            _remaining = evt.RecoverySeconds;
            // Finish the strike inside the announced recovery, then keep the tail planted.
            _strikeRemaining = _slam ? Mathf.Min(ClipLength("Slam"), _remaining) : 0f;
            Play(_slam ? "Slam" : "Sweep", _slam ? _strikeRemaining : _remaining);
            Pose();
        }

        private void OnPhase(BossPhaseChanged evt)
        {
            if (!IsMine(evt.EnemyId))
            {
                return;
            }
            ClearTell();
            _audio.Stop();
            _committed = true;
            _slam = false;
            _heading = _model.rotation;
            _remaining = ClipLength("Phase");
            Play("Phase");
        }

        private void OnHit(EnemyDamaged evt)
        {
            if (IsMine(evt.Id) && !_committed && !evt.Blocked && !evt.Killed)
            {
                _remaining = ClipLength("Hit");
                Play("Hit");
            }
        }

        private void OnDeath(EnemyDied evt)
        {
            if (!IsMine(evt.Id))
            {
                return;
            }
            ClearTell();
            _audio.Stop();
            ClearSlabs();
            _dead = true;
            _committed = false;
            Play("Death");
        }

        public void Step(float dt)
        {
            if (_dead || _body == null || !_body.IsBound || !(dt >= 0f) || !float.IsFinite(dt))
            {
                return;
            }
            // The root follows core's intent immediately; carry the visible world heading
            // across root turns, including the release from a committed attack.
            _visualHeading = Quaternion.RotateTowards(_visualHeading,
                _committed ? _heading : transform.rotation, TurnDegreesPerSecond * dt);
            _model.rotation = _visualHeading;
            if (IsShowing)
            {
                _elapsed += dt;
                Pose();
                PaintTell();
                // Keep the locked pose until impact even if the cosmetic clock finishes first.
                if (_elapsed >= _duration)
                {
                    ClearTell();
                }
                return;
            }
            if (_remaining > 0f)
            {
                _remaining = Mathf.Max(0f, _remaining - dt);
                if (_slam && _strikeRemaining > 0f)
                {
                    _strikeRemaining -= dt;
                    if (_strikeRemaining <= 0f && _remaining > 0f)
                    {
                        Play("Pinned");
                    }
                }
                if (_committed)
                {
                    Pose();
                }
                if (_remaining > 0f)
                {
                    return;
                }
                _committed = false;
            }
            if (!_committed)
            {
                string state = _body.Velocity.sqrMagnitude > 0.01f ? "Skitter" : "Idle";
                if (State != state)
                {
                    Play(state);
                }
            }
            else
            {
                Pose();
            }
        }

        private void LateUpdate() => Step(Time.deltaTime);
        private void Pose()
        {
            _tell.transform.SetPositionAndRotation(new Vector3(_tellCentre.x, 0.08f, _tellCentre.z), Quaternion.identity);
            Vector3 scale = transform.lossyScale;
            _tell.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
        }

        private void PaintTell()
        {
            Color tint = Palette.Danger;
            tint.a = Mathf.Lerp(0.3f, 0.9f, _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 1f);
            for (int i = 0; i < _colors.Length; i++)
            {
                _colors[i] = Palette.Vertex(tint);
            }
            _mesh.colors = _colors;
        }

        private float ClipLength(string state)
        {
            foreach (AnimationClip clip in _clips)
            {
                if (clip.name == state)
                {
                    return clip.length;
                }
            }
            return 0f;
        }

        private void Play(string state, float seconds = 0f)
        {
            State = state;
            _animator.speed = seconds > 0f ? ClipLength(state) / seconds : 1f;
            _animator.Play(state, 0, 0f);
        }

        private void ClearTell()
        {
            if (_tellRenderer != null)
            {
                _tellRenderer.enabled = false;
            }
            if (_claw != null)
            {
                _claw.enabled = false;
            }
            if (_stinger != null)
            {
                _stinger.enabled = false;
            }
        }

        private void ResetPresentation()
        {
            ClearSlabs();
            ClearTell();
            _dead = _committed = _slam = false;
            _duration = _elapsed = _remaining = _strikeRemaining = 0f;
            if (_audio != null)
            {
                _audio.Stop();
                _audio.clip = null;
            }
            if (_model != null)
            {
                _model.localRotation = Quaternion.identity;
                _visualHeading = _model.rotation;
            }
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.Rebind();
                Play("Idle");
            }
            if (_block != null)
            {
                _block.SetColor(_colorId, Palette.Danger);
                _claw.SetPropertyBlock(_block);
                _stinger.SetPropertyBlock(_block);
            }
        }

        private void DisposeSubscriptions()
        {
            _slabPlaced?.Dispose();
            _slabShattered?.Dispose();
            _armed?.Dispose();
            _impact?.Dispose();
            _phase?.Dispose();
            _hit?.Dispose();
            _death?.Dispose();
        }

        private void OnDestroy()
        {
            ClearSlabs();
            DisposeSubscriptions();
            if (_mesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_mesh);
                }
                else
                {
                    DestroyImmediate(_mesh);
                }
            }
        }
    }
}
