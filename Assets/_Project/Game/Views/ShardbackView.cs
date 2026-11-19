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
        private const int ShardsPerLane = 4;
        private const int ShardVertices = 6;
        private const float FlashSeconds = 0.16f;
        [SerializeField] private Animator _animator;
        [SerializeField] private MeshFilter _tell;
        [SerializeField] private MeshRenderer _tellRenderer;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private AudioClip _fanCue;
        [SerializeField] private AudioClip _chargeCue;
        [SerializeField] private AudioClip _burstCue;

        private readonly Vector3[] _vertices = new Vector3[CircleSegments * 2];
        private readonly Color[] _colors = new Color[CircleSegments * 2];
        private readonly int[] _circle = new int[CircleSegments * 3];
        private readonly int[] _ring = new int[CircleSegments * 6];
        private readonly int[] _shards = new int[3 * ShardsPerLane * 24];
        private readonly int[] _strips = new int[18];
        private readonly int[] _strip = new int[6];
        private IDisposable[] _subscriptions;
        private EnemyView _body;
        private AnimationClip[] _clips;
        private Mesh _mesh;
        private MeshFilter _fill;
        private MeshRenderer _fillRenderer;
        private Mesh _fillMesh;
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
        private ShardbackClipped _effectClip;
        private string _state;
        private float _fillFraction;
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
                events.Subscribe<ShardbackTellProgress>(OnTellProgress),
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
                int next = (i + 1) % CircleSegments;
                SetQuad(_ring, i * 6, i * 2, i * 2 + 1, next * 2, next * 2 + 1);
            }
            for (int i = 0; i < 3; i++)
                SetQuad(_strips, i * 6, i * 4, i * 4 + 1, i * 4 + 2, i * 4 + 3);
            IndexShards();
            Array.Copy(_strips, _strip, _strip.Length);
            _mesh = new Mesh { name = "Shardback attack tell" };
            _mesh.MarkDynamic();
            _tell.sharedMesh = _mesh;

            _fill = CreateLayer("AttackFill", _tellRenderer.sortingOrder + 1, out _fillMesh, out _fillRenderer);
            _effect = CreateLayer("AttackEffect", _tellRenderer.sortingOrder + 2, out _effectMesh, out _effectRenderer);
        }

        private MeshFilter CreateLayer(string label, int order, out Mesh mesh, out MeshRenderer renderer)
        {
            var layer = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            layer.transform.SetParent(transform, false);
            renderer = layer.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _tellRenderer.sharedMaterial;
            renderer.sortingOrder = order;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            mesh = new Mesh { name = label };
            mesh.MarkDynamic();
            MeshFilter filter = layer.GetComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            return filter;
        }

        private static void SetQuad(int[] indices, int start, int a, int b, int c, int d)
        {
            indices[start] = a;
            indices[start + 1] = b;
            indices[start + 2] = c;
            indices[start + 3] = c;
            indices[start + 4] = b;
            indices[start + 5] = d;
        }

        private void IndexShards()
        {
            for (int shard = 0; shard < 3 * ShardsPerLane; shard++)
            {
                int vertex = shard * ShardVertices;
                for (int side = 0; side < 4; side++)
                {
                    int left = vertex + 2 + side;
                    int right = vertex + 2 + (side + 1) % 4;
                    int index = shard * 24 + side * 6;
                    _shards[index] = vertex;
                    _shards[index + 1] = left;
                    _shards[index + 2] = right;
                    _shards[index + 3] = vertex + 1;
                    _shards[index + 4] = right;
                    _shards[index + 5] = left;
                }
            }
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
                ShowTell();
        }

        private void OnClipped(ShardbackClipped evt)
        {
            if (!Matches(evt.BossId, evt.Key) || _travelling || _cast.Move == ShardbackMove.Burst
                || _tellRenderer.enabled)
                return;

            _clip = evt;
            ShowTell();
        }

        private void OnTellProgress(ShardbackTellProgress evt)
        {
            if (!Matches(evt.BossId, evt.Key) || !_tellRenderer.enabled || evt.Fraction <= _fillFraction)
                return;

            _fillFraction = evt.Fraction;
            Color tint = Palette.Danger;
            tint.a = 0.76f;
            DrawTell(_fillMesh, _fillFraction, tint);
            Pose(_fill.transform, _cast, _heading, 0.09f);
            _fillRenderer.enabled = true;
        }

        private void ShowTell()
        {
            Color light = Color.Lerp(Palette.Danger, Color.white, 0.38f);
            light.a = 0.36f;
            DrawTell(_mesh, 1f, light);
            Pose(_tell.transform, _cast, _heading, 0.08f);
            _tellRenderer.enabled = true;
        }

        private void DrawTell(Mesh mesh, float fraction, Color tint)
        {
            int[] indices;
            int count;
            if (_cast.Move == ShardbackMove.Burst)
            {
                FillDisc(_cast.Reach * fraction);
                indices = _circle;
                count = CircleSegments + 1;
            }
            else
            {
                FillStrip(0, 0f, _clip.Centre * fraction);
                bool fan = _cast.Move == ShardbackMove.Fan;
                if (fan)
                {
                    FillStrip(4, _cast.AngleDegrees, _clip.Plus * fraction);
                    FillStrip(8, -_cast.AngleDegrees, _clip.Minus * fraction);
                }
                indices = fan ? _strips : _strip;
                count = fan ? 12 : 4;
            }
            Array.Fill(_colors, Palette.Vertex(tint), 0, count);
            Write(mesh, indices, count);
        }

        private void Write(Mesh mesh, int[] indices, int count)
        {
            if (mesh.vertexCount != count)
                mesh.Clear();
            mesh.SetVertices(_vertices, 0, count);
            mesh.SetColors(_colors, 0, count);
            mesh.SetTriangles(indices, 0);
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

        private void FillDisc(float radius)
        {
            _vertices[0] = Vector3.zero;
            for (int i = 0; i < CircleSegments; i++)
            {
                _vertices[i + 1] = CircleDirection(i) * radius;
            }
        }

        private static Vector3 CircleDirection(int index)
        {
            float angle = 2f * Mathf.PI * index / CircleSegments;
            return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        }

        private void Pose(Transform meshTransform, in ShardbackArmed cast, Quaternion heading, float height)
        {
            Vector3 origin = cast.Origin.ToUnity();
            meshTransform.SetPositionAndRotation(origin + Vector3.up * height, heading);
            Vector3 scale = transform.lossyScale;
            meshTransform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
        }

        private void OnChargeStarted(ShardbackChargeStarted evt)
        {
            if (!Matches(evt.BossId, evt.Key) || _cast.Move != ShardbackMove.Charge || _travelling)
                return;

            _travelling = true;
            _shownTravel = 0f;
            _tellRenderer.enabled = false;
            _fillRenderer.enabled = false;
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

            BeginEffect(evt.Move == ShardbackMove.Fan ? 0.34f : 0.46f);
            StepEffect(0f);
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
            BeginEffect(duration);
            if (_effectMesh.vertexCount != 4)
                _effectMesh.Clear();
            _effectMesh.SetVertices(_vertices, 0, 4);
            _effectMesh.SetTriangles(_strip, 0);
            StepEffect(0f);
        }

        private void BeginEffect(float duration)
        {
            _effectCast = _cast;
            _effectHeading = _heading;
            _effectClip = _clip;
            _effectTime = _effectDuration = duration;
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
            float travel = Mathf.Clamp01((1f - fade) / 0.72f);
            Color bright = Palette.Vertex(new Color(1f, 0.96f, 0.84f, fade));
            Color dim = Palette.Vertex(new Color(0.96f, 0.73f, 0.48f, 0.34f * fade));
            if (_effectCast.Move == ShardbackMove.Fan)
                DrawSplinters(travel, bright, dim);
            else if (_effectCast.Move == ShardbackMove.Burst)
                DrawShockwave(travel, bright, dim);
            else
            {
                _colors[0] = _colors[1] = dim;
                _colors[2] = _colors[3] = bright;
                _effectMesh.SetColors(_colors, 0, 4);
            }
            _effectRenderer.enabled = true;
        }

        private void DrawShockwave(float travel, Color bright, Color dim)
        {
            float outer = _effectCast.Reach * Mathf.Lerp(0.16f, 1f, travel);
            float inner = outer * 0.8f;
            for (int i = 0; i < CircleSegments; i++)
            {
                Vector3 radial = CircleDirection(i);
                int vertex = i * 2;
                _vertices[vertex] = radial * inner;
                _vertices[vertex + 1] = radial * outer + Vector3.up * 0.17f;
                _colors[vertex] = dim;
                _colors[vertex + 1] = bright;
            }
            Write(_effectMesh, _ring, CircleSegments * 2);
        }

        private void DrawSplinters(float travel, Color bright, Color dim)
        {
            DrawLaneSplinters(0, 0f, _effectClip.Centre, travel, bright, dim);
            DrawLaneSplinters(1, _effectCast.AngleDegrees, _effectClip.Plus, travel, bright, dim);
            DrawLaneSplinters(2, -_effectCast.AngleDegrees, _effectClip.Minus, travel, bright, dim);
            Write(_effectMesh, _shards, 3 * ShardsPerLane * ShardVertices);
        }

        private void DrawLaneSplinters(int lane, float degrees, float length, float travel, Color bright, Color dim)
        {
            Quaternion heading = Quaternion.Euler(0f, -degrees, 0f);
            for (int i = 0; i < ShardsPerLane; i++)
            {
                int vertex = (lane * ShardsPerLane + i) * ShardVertices;
                if (!(length > 0f))
                {
                    Array.Clear(_vertices, vertex, ShardVertices);
                    continue;
                }

                float fraction = 0.19f - i * 0.04f + travel * (0.81f - i * 0.08f);
                float tip = length * fraction;
                float tail = Mathf.Max(0f, tip - (0.82f - i * 0.11f));
                float middle = Mathf.Lerp(tail, tip, 0.56f);
                float width = _effectCast.HalfWidth * (0.34f - i * 0.048f);
                float side = _effectCast.HalfWidth * ((i - 1.5f) * 0.34f);
                float height = 0.28f + (0.13f * i) + (0.23f * Mathf.Sin(Mathf.PI * travel));
                _vertices[vertex] = heading * new Vector3(side, height, tail);
                _vertices[vertex + 1] = heading * new Vector3(side, height + 0.15f, tip);
                _vertices[vertex + 2] = heading * new Vector3(side - width, height, middle);
                _vertices[vertex + 3] = heading * new Vector3(side, height + width, middle);
                _vertices[vertex + 4] = heading * new Vector3(side + width, height, middle);
                _vertices[vertex + 5] = heading * new Vector3(side, height - width, middle);
                Array.Fill(_colors, dim, vertex, ShardVertices);
                _colors[vertex + 1] = _colors[vertex + 3] = bright;
            }
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
                    Pose(_tell.transform, _cast, _heading, 0.08f);
                    if (_fillRenderer.enabled)
                        Pose(_fill.transform, _cast, _heading, 0.09f);
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
            _fillFraction = _release = _recovery = 0f;
            _travelling = false;
            if (_tellRenderer != null)
                _tellRenderer.enabled = false;
            if (_fillRenderer != null)
                _fillRenderer.enabled = false;
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
                Destroy(_fillMesh);
                Destroy(_effectMesh);
            }
            else
            {
                DestroyImmediate(_mesh);
                DestroyImmediate(_fillMesh);
                DestroyImmediate(_effectMesh);
            }
        }
    }
}
