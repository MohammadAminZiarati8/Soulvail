using System;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Plays back the locked attack geometry and timing authored by Core.</summary>
    [RequireComponent(typeof(EnemyView))]
    public sealed class ShardbackView : MonoBehaviour, IPoolable
    {
        private const int CircleSegments = 64;
        [SerializeField] private Animator _animator;
        [SerializeField] private MeshFilter _tell;
        [SerializeField] private MeshRenderer _tellRenderer;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private AudioClip _fanCue;
        [SerializeField] private AudioClip _chargeCue;
        [SerializeField] private AudioClip _burstCue;

        private readonly Vector3[] _vertices = new Vector3[CircleSegments + 1];
        private readonly Color[] _colors = new Color[CircleSegments + 1];
        private readonly int[] _circle = new int[CircleSegments * 3];
        private readonly int[] _strips = new int[18];
        private readonly int[] _strip = new int[6];
        private IDisposable[] _subscriptions;
        private EnemyView _body;
        private AnimationClip[] _clips;
        private Mesh _mesh;
        private Guid _lifetime;
        private ulong _lastCast;
        private ShardbackArmed _cast;
        private Quaternion _heading;
        private string _state;
        private float _elapsed;
        private float _release;
        private float _recovery;
        private bool _travelling;
        private bool _dead;

        [Inject]
        public void Construct(DomainEventHub events)
        {
            DisposeSubscriptions();
            _body = GetComponent<EnemyView>();
            _clips = _animator.runtimeAnimatorController.animationClips;
            CreateMesh();
            _subscriptions = new[]
            {
                events.Subscribe<ShardbackFightStarted>(OnFightStarted),
                events.Subscribe<ShardbackArmed>(OnArmed),
                events.Subscribe<ShardbackClipped>(OnClipped),
                events.Subscribe<ShardbackChargeStarted>(OnChargeStarted),
                events.Subscribe<ShardbackImpact>(OnImpact),
                events.Subscribe<BossPhaseChanged>(OnPhase),
                events.Subscribe<EnemyDied>(OnDeath),
            };
            OnSpawn();
        }

        public void OnSpawn()
        {
            ResetPresentation();
            Play("Idle");
        }

        public void OnDespawn() => ResetPresentation();
        private void OnEnable() => Play("Idle");
        private void OnDisable() => ResetPresentation();

        private void CreateMesh()
        {
            if (_mesh != null)
                return;

            for (int i = 0; i < CircleSegments; i++)
            {
                _circle[i * 3 + 1] = i + 1;
                _circle[i * 3 + 2] = (i + 1) % CircleSegments + 1;
            }
            for (int i = 0; i < 3; i++)
            {
                int vertex = i * 4;
                int index = i * 6;
                _strips[index] = vertex;
                _strips[index + 1] = vertex + 1;
                _strips[index + 2] = vertex + 2;
                _strips[index + 3] = vertex + 2;
                _strips[index + 4] = vertex + 1;
                _strips[index + 5] = vertex + 3;
            }
            Array.Copy(_strips, _strip, _strip.Length);
            _mesh = new Mesh { name = "Shardback attack tell" };
            _mesh.MarkDynamic();
            _tell.sharedMesh = _mesh;
        }

        private bool IsMine(int id) => !_dead && isActiveAndEnabled && _body != null && _body.Id == id && _body.IsBound;

        private bool Matches(int id, in ShardbackProbeKey key) =>
            IsMine(id) && key.IsValid && _cast.Key.Cast == key.Cast && _cast.Key.Lifetime == key.Lifetime;

        private void OnFightStarted(ShardbackFightStarted evt)
        {
            if (!IsMine(evt.BossId) || evt.Lifetime == Guid.Empty || evt.Lifetime == _lifetime)
                return;

            CancelCast();
            _lifetime = evt.Lifetime;
            _lastCast = 0;
            Play("Idle");
        }

        private void OnArmed(ShardbackArmed evt)
        {
            if (!IsMine(evt.BossId) || !evt.Key.IsValid || evt.Key.Lifetime != _lifetime || evt.Key.Cast <= _lastCast)
                return;

            CancelCast();
            _cast = evt;
            _lastCast = evt.Key.Cast;
            _heading = Quaternion.LookRotation(new Vector3(evt.Facing.X, 0f, evt.Facing.Y));
            _animator.transform.rotation = _heading;
            Play(evt.Move switch
            {
                ShardbackMove.Fan => "FanWindup",
                ShardbackMove.Charge => "ChargeWindup",
                _ => "BurstWindup",
            }, evt.Seconds);
            _audio.clip = evt.Move switch
            {
                ShardbackMove.Fan => _fanCue,
                ShardbackMove.Charge => _chargeCue,
                _ => _burstCue,
            };
            if (Application.isPlaying)
                _audio.Play();
            if (evt.Move == ShardbackMove.Burst)
                ShowDisc();
        }

        private void OnClipped(ShardbackClipped evt)
        {
            if (!Matches(evt.BossId, evt.Key) || _travelling || _cast.Move == ShardbackMove.Burst
                || _tellRenderer.enabled)
                return;

            Array.Clear(_vertices, 0, _vertices.Length);
            FillStrip(0, 0f, evt.Centre);
            if (_cast.Move == ShardbackMove.Fan)
            {
                FillStrip(4, _cast.AngleDegrees, evt.Plus);
                FillStrip(8, -_cast.AngleDegrees, evt.Minus);
            }
            ShowMesh(_cast.Move == ShardbackMove.Fan ? _strips : _strip);
        }

        private void FillStrip(int start, float angle, float length)
        {
            Quaternion bearing = Quaternion.Euler(0f, angle, 0f);
            Vector3 side = bearing * (Vector3.right * _cast.HalfWidth);
            Vector3 end = bearing * (Vector3.forward * length);
            _vertices[start] = -side;
            _vertices[start + 1] = side;
            _vertices[start + 2] = end - side;
            _vertices[start + 3] = end + side;
        }

        private void ShowDisc()
        {
            _vertices[0] = Vector3.zero;
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = 2f * Mathf.PI * i / CircleSegments;
                _vertices[i + 1] = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * _cast.Reach;
            }
            ShowMesh(_circle);
        }

        private void ShowMesh(int[] indices)
        {
            _mesh.vertices = _vertices;
            _mesh.SetTriangles(indices, 0);
            PoseTell();
            PaintTell();
            _tellRenderer.enabled = true;
        }

        private void PoseTell()
        {
            Vector3 origin = _cast.Origin.ToUnity();
            _tell.transform.SetPositionAndRotation(origin + Vector3.up * 0.08f, _heading);
            Vector3 scale = transform.lossyScale;
            _tell.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
        }

        private void PaintTell()
        {
            Color tint = Palette.Danger;
            tint.a = Mathf.Lerp(0.38f, 0.84f, _cast.Seconds > 0f ? Mathf.Clamp01(_elapsed / _cast.Seconds) : 1f);
            Array.Fill(_colors, Palette.Vertex(tint));
            _mesh.colors = _colors;
        }

        private void OnChargeStarted(ShardbackChargeStarted evt)
        {
            if (!Matches(evt.BossId, evt.Key) || _cast.Move != ShardbackMove.Charge || _travelling)
                return;

            _travelling = true;
            _tellRenderer.enabled = false;
            Play("ChargeTravel");
        }

        private void OnImpact(ShardbackImpact evt)
        {
            if (!Matches(evt.BossId, evt.Key) || _cast.Move != evt.Move)
                return;

            CancelCast();
            _recovery = evt.RecoverySeconds;
            if (evt.Move == ShardbackMove.Charge)
            {
                Play(_recovery > 0f ? "Recovery" : "Idle", _recovery);
                return;
            }

            string release = evt.Move == ShardbackMove.Fan ? "FanRelease" : "BurstRelease";
            float length = ClipLength(release);
            _release = _recovery > 0f ? Mathf.Min(length, _recovery * 0.25f) : length;
            Play(release, _release);
        }

        private void OnPhase(BossPhaseChanged evt)
        {
            if (!IsMine(evt.EnemyId))
                return;
            CancelCast();
            Play("Idle");
        }

        private void OnDeath(EnemyDied evt)
        {
            if (!IsMine(evt.Id))
                return;
            CancelCast();
            _dead = true;
            Play("Death");
        }

        public void Step(float dt)
        {
            if (_dead || _body == null || !_body.IsBound || !(dt >= 0f) || !float.IsFinite(dt))
                return;

            if (_cast.Key.IsValid)
            {
                _animator.transform.rotation = _heading;
                if (_tellRenderer.enabled)
                {
                    _elapsed += dt;
                    PoseTell();
                    PaintTell();
                }
                return;
            }

            _animator.transform.localRotation = Quaternion.identity;
            _recovery = Mathf.Max(0f, _recovery - dt);
            if (_release > 0f)
            {
                _release -= dt;
                if (_release > 0f)
                    return;
                if (_recovery > 0f)
                    Play("Recovery", _recovery);
            }
            if (_recovery > 0f)
                return;

            string locomotion = _body.Velocity.sqrMagnitude > 0.01f ? "Walk" : "Idle";
            if (_state != locomotion)
                Play(locomotion);
        }

        private void LateUpdate() => Step(Time.deltaTime);

        private float ClipLength(string state)
        {
            foreach (AnimationClip clip in _clips)
            {
                if (clip.name == state)
                    return clip.length;
            }
            return 0f;
        }

        private void Play(string state, float seconds = 0f)
        {
            _state = state;
            if (_animator == null || !isActiveAndEnabled)
                return;
            _animator.speed = seconds > 0f ? ClipLength(state) / seconds : 1f;
            _animator.Play(state, 0, 0f);
        }

        private void CancelCast()
        {
            _cast = default;
            _elapsed = _release = _recovery = 0f;
            _travelling = false;
            if (_tellRenderer != null)
                _tellRenderer.enabled = false;
            if (_audio != null)
            {
                _audio.Stop();
                _audio.clip = null;
            }
        }

        private void ResetPresentation()
        {
            CancelCast();
            _dead = false;
            _lifetime = Guid.Empty;
            _lastCast = 0;
            if (_animator != null)
                _animator.transform.localRotation = Quaternion.identity;
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
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
        }
    }
}
