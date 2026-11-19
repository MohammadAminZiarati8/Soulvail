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
        private const float FlashSeconds = 0.16f;
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
        private MeshFilter _effect;
        private MeshRenderer _effectRenderer;
        private Mesh _effectMesh;
        private Guid _lifetime;
        private ulong _lastCast;
        private ShardbackArmed _cast;
        private Quaternion _heading;
        private ShardbackArmed _effectCast;
        private Quaternion _effectHeading;
        private ShardbackClipped _clip;
        private string _state;
        private float _elapsed;
        private float _release;
        private float _recovery;
        private float _effectTime;
        private float _effectDuration;
        private float _shownTravel;
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
                events.Subscribe<ShardbackChargeAdvanced>(OnChargeAdvanced),
                events.Subscribe<ShardbackImpact>(OnImpact),
                events.Subscribe<BossPhaseChanged>(OnPhase),
                events.Subscribe<EnemyDied>(OnDeath),
            };
            OnSpawn();
        }

        public void OnSpawn()
        {
            ResetPresentation();
            transform.rotation = Quaternion.identity;
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

            var effect = new GameObject("AttackEffect", typeof(MeshFilter), typeof(MeshRenderer));
            effect.transform.SetParent(transform, false);
            _effect = effect.GetComponent<MeshFilter>();
            _effectRenderer = effect.GetComponent<MeshRenderer>();
            _effectRenderer.sharedMaterial = _tellRenderer.sharedMaterial;
            _effectRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _effectRenderer.receiveShadows = false;
            _effectMesh = new Mesh { name = "Shardback attack effect" };
            _effectMesh.MarkDynamic();
            _effect.sharedMesh = _effectMesh;
        }

        private bool IsMine(int id) => !_dead && isActiveAndEnabled && _body != null && _body.Id == id && _body.IsBound;

        private bool Matches(int id, in ShardbackProbeKey key) =>
            IsMine(id) && key.IsValid && _cast.Key.Cast == key.Cast && _cast.Key.Lifetime == key.Lifetime;

        private void OnFightStarted(ShardbackFightStarted evt)
        {
            if (!IsMine(evt.BossId) || evt.Lifetime == Guid.Empty || evt.Lifetime == _lifetime)
                return;

            CancelCast();
            ClearEffect();
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
            {
                FillDisc();
                ShowMesh(_circle);
            }
        }

        private void OnClipped(ShardbackClipped evt)
        {
            if (!Matches(evt.BossId, evt.Key) || _travelling || _cast.Move == ShardbackMove.Burst
                || _tellRenderer.enabled)
                return;

            _clip = evt;
            if (_cast.Move == ShardbackMove.Fan)
                FillFan();
            else
                FillStrip(0, 0f, evt.Centre);
            ShowMesh(_cast.Move == ShardbackMove.Fan ? _strips : _strip);
        }

        private void FillFan()
        {
            FillStrip(0, 0f, _clip.Centre);
            FillStrip(4, _cast.AngleDegrees, _clip.Plus);
            FillStrip(8, -_cast.AngleDegrees, _clip.Minus);
        }

        private void FillStrip(int start, float angle, float length)
        {
            // Unity yaw turns +Z toward +X. Core's positive XZ angle turns +Z toward -X.
            Quaternion bearing = Quaternion.Euler(0f, -angle, 0f);
            Vector3 side = bearing * (Vector3.right * _cast.HalfWidth);
            Vector3 end = bearing * (Vector3.forward * length);
            _vertices[start] = -side;
            _vertices[start + 1] = side;
            _vertices[start + 2] = end - side;
            _vertices[start + 3] = end + side;
        }

        private void FillDisc()
        {
            _vertices[0] = Vector3.zero;
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = 2f * Mathf.PI * i / CircleSegments;
                _vertices[i + 1] = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * _cast.Reach;
            }
        }

        private void ShowMesh(int[] indices)
        {
            _mesh.vertices = _vertices;
            _mesh.SetTriangles(indices, 0);
            Pose(_tell.transform, _cast, _heading, 0.08f);
            PaintTell();
            _tellRenderer.enabled = true;
        }

        private void Pose(Transform meshTransform, in ShardbackArmed cast, Quaternion heading, float height)
        {
            Vector3 origin = cast.Origin.ToUnity();
            meshTransform.SetPositionAndRotation(origin + Vector3.up * height, heading);
            Vector3 scale = transform.lossyScale;
            meshTransform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
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
            _shownTravel = 0f;
            _tellRenderer.enabled = false;
            ShowChargeEffect(0f, FlashSeconds);
            Play("ChargeTravel");
        }

        private void OnChargeAdvanced(ShardbackChargeAdvanced evt)
        {
            if (!Matches(evt.BossId, evt.Key) || !_travelling || evt.Distance <= _shownTravel
                || evt.Distance > _clip.Centre)
                return;

            _shownTravel = evt.Distance;
            ShowChargeEffect(evt.Distance, 0.22f);
        }

        private void OnImpact(ShardbackImpact evt)
        {
            if (!Matches(evt.BossId, evt.Key) || _cast.Move != evt.Move)
                return;

            if (evt.Move == ShardbackMove.Charge)
            {
                ShowChargeEffect(evt.TravelDistance, 0.38f);
                CancelCast();
                _recovery = evt.RecoverySeconds;
                Play(_recovery > 0f ? "Recovery" : "Idle", _recovery);
                return;
            }

            if (evt.Move == ShardbackMove.Fan)
            {
                FillFan();
                ShowEffect(_strips, 0.28f);
            }
            else
            {
                FillDisc();
                ShowEffect(_circle, 0.42f);
            }
            CancelCast();
            _recovery = evt.RecoverySeconds;
            string release = evt.Move == ShardbackMove.Fan ? "FanRelease" : "BurstRelease";
            float length = ClipLength(release);
            _release = _recovery > 0f ? Mathf.Min(length, _recovery * 0.25f) : length;
            Play(release, _release);
        }

        private void ShowChargeEffect(float distance, float duration)
        {
            FillStrip(0, 0f, distance);
            if (distance == 0f)
            {
                // A charge can meet cover at its origin. Keep its flash inside that plane.
                _vertices[2] += Vector3.up * 0.85f;
                _vertices[3] += Vector3.up * 0.85f;
            }
            ShowEffect(_strip, duration);
        }

        private void ShowEffect(int[] indices, float duration)
        {
            _effectCast = _cast;
            _effectHeading = _heading;
            _effectTime = _effectDuration = duration;
            _effectMesh.vertices = _vertices;
            _effectMesh.SetTriangles(indices, 0);
            StepEffect(0f);
        }

        private void StepEffect(float dt)
        {
            if (!(_effectTime > 0f))
                return;

            _effectTime -= dt;
            if (!(_effectTime > 0f))
            {
                ClearEffect();
                return;
            }

            Pose(_effect.transform, _effectCast, _effectHeading, 0.12f);
            float fade = _effectTime / _effectDuration;
            Color bright = new(1f, 0.95f, 0.83f, fade);
            Color dim = new(0.96f, 0.73f, 0.48f, 0.2f * fade);
            Array.Fill(_colors, Palette.Vertex(bright));
            if (_effectCast.Move == ShardbackMove.Burst)
                _colors[0] = Palette.Vertex(dim);
            else
            {
                int lanes = _effectCast.Move == ShardbackMove.Fan ? 3 : 1;
                for (int i = 0; i < lanes; i++)
                    _colors[i * 4] = _colors[i * 4 + 1] = Palette.Vertex(dim);
            }
            _effectMesh.colors = _colors;
            _effectRenderer.enabled = true;
        }

        private void ClearEffect()
        {
            _effectTime = 0f;
            if (_effectRenderer != null)
                _effectRenderer.enabled = false;
        }

        private void OnPhase(BossPhaseChanged evt)
        {
            if (!IsMine(evt.EnemyId))
                return;
            CancelCast();
            ClearEffect();
            Play("Idle");
        }

        private void OnDeath(EnemyDied evt)
        {
            if (!IsMine(evt.Id))
                return;
            CancelCast();
            ClearEffect();
            _dead = true;
            Play("Death");
        }

        public void Step(float dt)
        {
            if (_dead || _body == null || !_body.IsBound || !(dt >= 0f) || !float.IsFinite(dt))
                return;

            StepEffect(dt);
            if (_cast.Key.IsValid)
            {
                if (_tellRenderer.enabled)
                {
                    _elapsed += dt;
                    Pose(_tell.transform, _cast, _heading, 0.08f);
                    PaintTell();
                }
                return;
            }

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
            _clip = default;
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
            ClearEffect();
            _dead = false;
            _lifetime = Guid.Empty;
            _lastCast = 0;
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
                Destroy(_mesh);
                Destroy(_effectMesh);
            }
            else
            {
                DestroyImmediate(_mesh);
                DestroyImmediate(_effectMesh);
            }
        }
    }
}
