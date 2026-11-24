using System;
using System.Collections.Generic;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Soulvail.Game.Views
{
    [RequireComponent(typeof(EnemyView))]
    public sealed class ColossusView : MonoBehaviour, IPoolable
    {
        private const int SweepSectors = 3;
        private const float FloorLift = 0.08f;
        private const float FlashSeconds = 0.15f;
        private const float SettleSeconds = 0.24f;
        private const float TurnDegreesPerSecond = 360f;
        private const float RayFlashSeconds = 0.24f;
        private const float StompWaveSeconds = 0.30f;
        private const float DialHeight = 3.11f;

        [SerializeField] private Animator _animator;
        [SerializeField] private Transform _sweepPivot;
        [SerializeField] private Transform _handTip;
        [SerializeField] private Material _sandstone;
        [SerializeField] private Material _warningMaterial;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private AudioClip _sweepCue;
        [SerializeField] private AudioClip _stompCue;

        private readonly List<Vector3> _vertices = new();
        private readonly List<int> _triangles = new();
        private readonly List<Color> _colors = new();
        private readonly List<float> _emphasis = new();
        private readonly List<Vector3> _effectVertices = new();
        private readonly List<int> _effectTriangles = new();
        private readonly List<Color> _effectColors = new();
        private readonly int[] _sectorEnds = new int[SweepSectors];
        private readonly bool[] _spent = new bool[SweepSectors];
        private readonly IDisposable[] _subscriptions = new IDisposable[8];
        private readonly int _baseColor = Shader.PropertyToID("_BaseColor");
        private EnemyView _body;
        private Mesh _mesh;
        private MeshRenderer _warning;
        private Mesh _effectMesh;
        private MeshRenderer _effect;
        private ColossusArmed _attack;
        private ColossusMove _move;
        private float _sectorDegrees;
        private float _tellFraction;
        private float _flashRemaining;
        private float _recoveryRemaining;
        private float _settleRemaining;
        private float _settleYaw;
        private float _effectRemaining;
        private float _repairSeconds;
        private float _repairElapsed;
        private int _flashing;
        private int _sectorCount;
        private int _lifeId;
        private bool _committed;
        private bool _telling;
        private bool _finalImpact;
        private bool _dead;
        private Quaternion _heading;
        private Quaternion _visualHeading;
        private string _state;
        private ColossusPlateVisual _leftPlate;
        private ColossusPlateVisual _rightPlate;

        public System.Numerics.Vector2 ShoulderAxis => _leftPlate != null && _rightPlate != null
            ? (_rightPlate.Position - _leftPlate.Position).ToNumXZ()
            : default;

        [Inject]
        public void Construct(DomainEventHub events)
        {
            DisposeSubscriptions();
            _body = GetComponent<EnemyView>();
            TintStone();
            EnsurePlates();
            _subscriptions[0] = events.Subscribe<ColossusArmed>(OnArmed);
            _subscriptions[1] = events.Subscribe<ColossusTellProgress>(OnProgress);
            _subscriptions[2] = events.Subscribe<ColossusImpact>(OnImpact);
            _subscriptions[3] = events.Subscribe<BossPhaseChanged>(OnPhase);
            _subscriptions[4] = events.Subscribe<EnemyDied>(OnDeath);
            _subscriptions[5] = events.Subscribe<ColossusPlateHit>(OnPlateHit);
            _subscriptions[6] = events.Subscribe<BossBeatStarted>(OnRepairStarted);
            _subscriptions[7] = events.Subscribe<BossBeatEnded>(OnRepairFinished);
            OnSpawn();
        }

        public void OnSpawn()
        {
            _lifeId = 0;
            _dead = false;
            _repairSeconds = 0f;
            _visualHeading = transform.rotation;
            ClearPresentation();
            _leftPlate?.Reset();
            _rightPlate?.Reset();
        }

        public void OnDespawn() => OnSpawn();
        private void OnEnable() => OnSpawn();
        private void OnDisable() => OnSpawn();

        private bool TracksBody()
        {
            if (_body == null || !isActiveAndEnabled)
            {
                return false;
            }
            if (!_body.IsBound)
            {
                if (_lifeId != 0)
                {
                    OnSpawn();
                }
                return false;
            }
            if (_lifeId != _body.Id)
            {
                OnSpawn();
                _lifeId = _body.Id;
            }
            return !_dead;
        }

        private bool IsMine(int id) => _body != null && _body.Id == id && TracksBody();

        private void OnArmed(ColossusArmed evt)
        {
            if (!IsMine(evt.BossId))
            {
                return;
            }

            _attack = evt;
            _move = evt.Move;
            _sectorDegrees = evt.SectorDegrees;
            _sectorCount = _move is ColossusMove.Stomp or ColossusMove.SundialRay ? 1 : SweepSectors;
            _committed = _telling = true;
            _finalImpact = false;
            _settleRemaining = _recoveryRemaining = _flashRemaining = _tellFraction = 0f;
            _flashing = -1;
            Array.Clear(_spent, 0, _spent.Length);
            Vector3 facing = new(evt.Facing.X, 0f, evt.Facing.Y);
            _heading = facing.sqrMagnitude > 0f ? Quaternion.LookRotation(facing) : transform.rotation;

            EnsureWarning();
            Transform footprint = _warning.transform;
            footprint.SetPositionAndRotation(evt.Origin.ToUnity() + Vector3.up * FloorLift, _heading);
            HideEffect();
            BuildFootprint();
            PaintWarning();
            _warning.enabled = true;
            PoseTell();
            _audio.Stop();
            _audio.clip = _move switch
            {
                ColossusMove.Stomp => _stompCue,
                ColossusMove.SundialRay => null,
                _ => _sweepCue
            };
            if (Application.isPlaying && _audio.clip != null)
            {
                _audio.Play();
            }
        }

        private void OnProgress(ColossusTellProgress evt)
        {
            if (IsMine(evt.BossId) && _telling)
            {
                _tellFraction = Mathf.Clamp01(evt.Fraction);
                PoseTell();
                PaintWarning();
            }
        }

        private void PoseTell()
        {
            if (_move == ColossusMove.SundialRay)
            {
                _sweepPivot.localRotation = Quaternion.identity;
                Pose("Idle", 0f);
                DrawEffect();
                return;
            }

            string state = _move switch
            {
                ColossusMove.Sweep => "SweepTell",
                ColossusMove.ReverseSweep => "ReturnTell",
                _ => "StompTell"
            };
            Pose(state, _tellFraction * 0.999f);
            int start = _move == ColossusMove.ReverseSweep ? 2 : 0;
            AimArm(_move == ColossusMove.Stomp ? 0f : SectorAngle(start), _tellFraction);
        }

        private void OnImpact(ColossusImpact evt)
        {
            bool stomp = evt.Move == ColossusMove.Stomp;
            bool ray = evt.Move == ColossusMove.SundialRay;
            bool single = stomp || ray;
            if (!IsMine(evt.BossId) || !_committed || _move != evt.Move || _finalImpact
                || (single ? evt.Sector != -1 : (uint)evt.Sector >= SweepSectors))
            {
                return;
            }

            _telling = false;
            _finalImpact = single || evt.Sector == (_move == ColossusMove.ReverseSweep ? 0 : 2);
            _recoveryRemaining = evt.RecoverySeconds;
            StopCue();
            if (ray)
            {
                HideWarning();
                _effectRemaining = RayFlashSeconds;
                DrawEffect();
                return;
            }

            _flashing = stomp ? 0 : evt.Sector;
            _spent[_flashing] = true;
            _flashRemaining = FlashSeconds;
            string state = _move switch
            {
                ColossusMove.Sweep => "SweepStrike",
                ColossusMove.ReverseSweep => "ReturnStrike",
                _ => "StompStrike"
            };
            Pose(state, stomp ? 0.999f : (evt.Sector + 0.5f) / SweepSectors);
            AimArm(stomp ? 0f : SectorAngle(evt.Sector), stomp ? 0f : 1f);
            PaintWarning();
            if (stomp)
            {
                _effectRemaining = StompWaveSeconds;
                DrawEffect();
            }
        }

        private float SectorAngle(int sector) => (sector - 1) * _sectorDegrees;

        private void AimArm(float bearing, float weight)
        {
            // The shoulder is offset. Align the hand's radial position with the warning sector.
            Vector3 tip = _sweepPivot.InverseTransformPoint(_handTip.position);
            float handBearing = Mathf.Atan2(tip.x, tip.z) * Mathf.Rad2Deg;
            _sweepPivot.localRotation = Quaternion.Euler(0f, (bearing - handBearing) * weight, 0f);
        }

        private void Pose(string state, float fraction)
        {
            _animator.transform.rotation = _visualHeading;
            Play(state, fraction, 0f);
        }

        private void Play(string state, float fraction, float speed)
        {
            _state = state;
            if (_animator != null && _animator.isActiveAndEnabled)
            {
                _animator.speed = speed;
                _animator.Play(state, 0, fraction);
                _animator.Update(0f);
            }
        }

        public void Step(float dt)
        {
            if (!TracksBody() || !(dt >= 0f) || !float.IsFinite(dt))
            {
                return;
            }

            // Carry the visible world heading across immediate root turns and attack handoffs.
            _visualHeading = Quaternion.RotateTowards(_visualHeading,
                _committed ? _heading : transform.rotation, TurnDegreesPerSecond * dt);
            _animator.transform.rotation = _visualHeading;
            if (_telling && _move == ColossusMove.SundialRay)
            {
                PlaceDial();
            }
            if (_repairSeconds > 0f)
            {
                _repairElapsed += dt;
                float progress = Mathf.Clamp01(_repairElapsed / _repairSeconds);
                _leftPlate?.AnimateRepair(progress);
                _rightPlate?.AnimateRepair(progress);
            }

            if (_flashRemaining > 0f)
            {
                _flashRemaining = Mathf.Max(0f, _flashRemaining - dt);
                PaintWarning();
                if (_finalImpact && _flashRemaining == 0f)
                {
                    HideWarning();
                }
            }

            if (_effectRemaining > 0f)
            {
                _effectRemaining = Mathf.Max(0f, _effectRemaining - dt);
                if (_effectRemaining == 0f)
                {
                    HideEffect();
                }
                else
                {
                    DrawEffect();
                }
            }

            if (_committed)
            {
                if (!(_recoveryRemaining > 0f))
                {
                    return;
                }
                _recoveryRemaining = Mathf.Max(0f, _recoveryRemaining - dt);
                if (_recoveryRemaining > 0f)
                {
                    return;
                }
                _committed = false;
                _settleYaw = Mathf.DeltaAngle(0f, _sweepPivot.localEulerAngles.y);
                _settleRemaining = SettleSeconds;
                // Blend out of the pinned pose, then let locomotion take over.
                _state = "Recover";
                _animator.speed = 1f;
                _animator.CrossFadeInFixedTime("Recover", SettleSeconds, 0, 0f);
                return;
            }

            if (_settleRemaining > 0f)
            {
                _settleRemaining = Mathf.Max(0f, _settleRemaining - dt);
                float weight = _settleRemaining / SettleSeconds;
                _sweepPivot.localRotation = Quaternion.Euler(0f, _settleYaw * weight, 0f);
                if (_settleRemaining > 0f)
                {
                    return;
                }
            }

            string locomotion = _body.Velocity.sqrMagnitude > 0.01f ? "Walk" : "Idle";
            if (_state != locomotion)
            {
                Play(locomotion, 0f, 1f);
            }
        }

        private void EnsureWarning()
        {
            if (_warning != null)
            {
                return;
            }
            // Keep this in the body's scene while leaving its transform in world space.
            var footprint = new GameObject("Colossus warning") { hideFlags = HideFlags.DontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(footprint, gameObject.scene);
            _mesh = new Mesh { name = "Colossus footprint" };
            _mesh.MarkDynamic();
            footprint.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _warning = footprint.AddComponent<MeshRenderer>();
            _warning.sharedMaterial = _warningMaterial;
            _warning.shadowCastingMode = ShadowCastingMode.Off;
            _warning.receiveShadows = false;
        }

        private void BuildFootprint()
        {
            _vertices.Clear();
            _triangles.Clear();
            _colors.Clear();
            _emphasis.Clear();
            float radius = _attack.Radius;
            if (_move == ColossusMove.SundialRay)
            {
                float start = _attack.LaneStart;
                float halfWidth = _attack.LaneHalfWidth;
                float inner = halfWidth * 0.78f;
                LaneStrip(-halfWidth, -inner, start, radius, 0.85f);
                LaneStrip(-inner, inner, start, radius, 0f);
                LaneStrip(inner, halfWidth, start, radius, 0.85f);
                LaneStrip(-inner, inner, radius - 0.14f, radius, 0.85f, 0.004f);
                _sectorEnds[0] = _vertices.Count;
                UploadFootprint();
                return;
            }

            bool stomp = _move == ColossusMove.Stomp;
            float degrees = stomp ? 360f : _sectorDegrees;
            int steps = stomp ? 72 : 18;
            int direction = _move == ColossusMove.ReverseSweep ? -1 : 1;
            int startSector = direction == 1 ? 0 : 2;
            float rim = radius - Mathf.Min(0.14f, radius * 0.08f);

            for (int sector = 0; sector < _sectorCount; sector++)
            {
                float center = stomp ? 0f : SectorAngle(sector);
                float left = center - degrees * 0.5f;
                for (int step = 0; step < steps; step++)
                {
                    float a = left + degrees * step / steps;
                    float b = left + degrees * (step + 1) / steps;
                    Triangle(Vector3.zero, Polar(rim, a), Polar(rim, b), 0f);
                    Quad(Polar(rim, a), Polar(radius, a), Polar(radius, b), Polar(rim, b), 0.65f);
                }
                if (!stomp)
                {
                    bool startsPass = sector == startSector;
                    float edge = center - direction * degrees * 0.5f;
                    float inside = edge + direction * degrees * (startsPass ? 0.12f : 0.045f);
                    Triangle(Vector3.zero, Polar(radius, edge), Polar(radius, inside), startsPass ? 1f : 0.4f, 0.003f);
                    // The centered chevron flips in place on the return pass.
                    Vector3 tip = Polar(radius * 0.76f, center + direction * degrees * 0.23f);
                    Vector3 notch = Polar(radius * 0.76f, center - direction * degrees * 0.05f);
                    Triangle(Polar(radius * 0.65f, center - direction * degrees * 0.26f),
                        tip, notch, 0.8f, 0.006f);
                    Triangle(Polar(radius * 0.87f, center - direction * degrees * 0.26f),
                        tip, notch, 0.8f, 0.006f);
                }
                _sectorEnds[sector] = _vertices.Count;
            }
            UploadFootprint();
        }

        private void LaneStrip(float left, float right, float start, float end, float emphasis, float lift = 0f) =>
            Quad(new Vector3(left, lift, start), new Vector3(left, lift, end),
                new Vector3(right, lift, end), new Vector3(right, lift, start), emphasis);

        private void UploadFootprint()
        {
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetTriangles(_triangles, 0);
        }

        private void DrawEffect()
        {
            if (_effect == null)
            {
                var effect = new GameObject("Colossus attack effect") { hideFlags = HideFlags.DontSave };
                effect.transform.SetParent(_warning.transform, false);
                _effectMesh = new Mesh { name = "Colossus attack effect" };
                _effectMesh.MarkDynamic();
                effect.AddComponent<MeshFilter>().sharedMesh = _effectMesh;
                _effect = effect.AddComponent<MeshRenderer>();
                _effect.sharedMaterial = _warningMaterial;
                _effect.shadowCastingMode = ShadowCastingMode.Off;
                _effect.receiveShadows = false;
            }

            _effectVertices.Clear();
            _effectTriangles.Clear();
            _effectColors.Clear();
            if (_move == ColossusMove.Stomp)
            {
                _effect.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                DrawStompWave();
            }
            else if (_telling)
            {
                PlaceDial();
                DrawDial();
            }
            else
            {
                _effect.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                DrawRay();
            }
            _effectMesh.Clear();
            _effectMesh.SetVertices(_effectVertices);
            _effectMesh.SetTriangles(_effectTriangles, 0);
            _effectMesh.SetColors(_effectColors);
            _effect.enabled = true;
        }

        private void PlaceDial() => _effect.transform.SetPositionAndRotation(
            _animator.transform.position + Vector3.up * FloorLift, _visualHeading);

        private void DrawDial()
        {
            Vector3 center = new(0f, DialHeight - FloorLift, _attack.LaneStart);
            float radius = Mathf.Lerp(0.16f, 0.48f, _tellFraction);
            Color amber = new(1f, 0.39f, 0.08f, Mathf.Lerp(0.45f, 0.95f, _tellFraction));
            Color white = new(1f, 0.95f, 0.71f, Mathf.Lerp(0.38f, 0.98f, _tellFraction));
            for (int i = 0; i < 24; i++)
            {
                float a = i * (Mathf.PI * 2f / 24f);
                float b = (i + 1) * (Mathf.PI * 2f / 24f);
                Vector3 u = new(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 v = new(Mathf.Cos(b), Mathf.Sin(b), 0f);
                EffectQuad(center + u * radius, center + v * radius,
                    center + v * (radius * 0.76f), center + u * (radius * 0.76f), amber);
                EffectTriangle(center, center + u * (radius * 0.53f), center + v * (radius * 0.53f), white);
            }
        }

        private void DrawRay()
        {
            float age = 1f - _effectRemaining / RayFlashSeconds;
            float opacity = 1f - age * age;
            float width = _attack.LaneHalfWidth;
            Vector3 start = new(0f, DialHeight - FloorLift, _attack.LaneStart);
            Vector3 end = new(0f, 0.5f, _attack.Radius);
            Vector3 edge = Vector3.right * width;
            Color amber = new(1f, 0.35f, 0.06f, opacity * 0.9f);
            Color white = new(1f, 0.97f, 0.8f, opacity);
            EffectQuad(start - edge, start + edge, end + edge, end - edge, amber);
            Vector3 core = edge * 0.35f;
            Vector3 lift = Vector3.up * 0.035f;
            EffectQuad(start - core + lift, start + core + lift, end + core + lift, end - core + lift, white);
            // Two planes make the flash readable from above and from the side.
            Vector3 flare = end - Vector3.forward * 0.14f;
            Vector3 peak = flare + Vector3.up * (1.25f + age * 0.4f);
            Vector3 foot = flare - Vector3.up * 0.48f;
            EffectQuad(flare - edge, peak, flare + edge, foot, white);
            EffectQuad(flare - Vector3.forward * 0.8f, peak, end, foot, amber);
        }

        private void DrawStompWave()
        {
            float age = 1f - _effectRemaining / StompWaveSeconds;
            float rim = Mathf.Lerp(_attack.Radius * 0.22f, _attack.Radius, age);
            float inner = rim * 0.72f;
            Color earth = new(0.89f, 0.42f, 0.15f, 1f - age * age);
            Color crest = new(1f, 0.86f, 0.55f, 1f - age * age);
            for (int i = 0; i < 40; i++)
            {
                float a = i * 9f;
                float b = a + 9f;
                Vector3 topA = Polar(rim * 0.9f, a) + Vector3.up * 0.26f;
                Vector3 topB = Polar(rim * 0.9f, b) + Vector3.up * 0.26f;
                EffectQuad(Polar(inner, a), topA, topB, Polar(inner, b), crest);
                EffectQuad(topA, Polar(rim, a), Polar(rim, b), topB, earth);
            }
        }

        private void EffectQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color tint)
        {
            EffectTriangle(a, b, c, tint);
            EffectTriangle(a, c, d, tint);
        }

        private void EffectTriangle(Vector3 a, Vector3 b, Vector3 c, Color tint)
        {
            int start = _effectVertices.Count;
            _effectVertices.Add(a);
            _effectVertices.Add(b);
            _effectVertices.Add(c);
            Color color = Palette.Vertex(tint);
            for (int i = 0; i < 3; i++)
            {
                _effectTriangles.Add(start + i);
                _effectColors.Add(color);
            }
        }

        private Vector3 Polar(float radius, float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float emphasis)
        {
            Triangle(a, b, c, emphasis);
            Triangle(a, c, d, emphasis);
        }

        private void Triangle(Vector3 a, Vector3 b, Vector3 c, float emphasis, float lift = 0f)
        {
            int start = _vertices.Count;
            Vector3 offset = Vector3.up * lift;
            _vertices.Add(a + offset);
            _vertices.Add(b + offset);
            _vertices.Add(c + offset);
            bool up = Vector3.Cross(b - a, c - a).y > 0f;
            _triangles.Add(start);
            _triangles.Add(start + (up ? 1 : 2));
            _triangles.Add(start + (up ? 2 : 1));
            for (int i = 0; i < 3; i++)
            {
                _emphasis.Add(emphasis);
                _colors.Add(default);
            }
        }

        private void PaintWarning()
        {
            int start = 0;
            for (int sector = 0; sector < _sectorCount; sector++)
            {
                float flash = sector == _flashing ? _flashRemaining / FlashSeconds : 0f;
                bool spent = _spent[sector];
                float fill = spent ? 0.075f : Mathf.Lerp(0.25f, 0.55f, _tellFraction);
                for (int i = start; i < _sectorEnds[sector]; i++)
                {
                    float emphasis = _emphasis[i];
                    Color tint = Color.Lerp(Palette.Danger, Palette.HitFlash, emphasis * 0.55f);
                    tint = Color.Lerp(tint, Palette.HitFlash, flash * 0.72f);
                    tint.a = Mathf.Lerp(fill + emphasis * (spent ? 0.07f : 0.38f), 0.96f, flash);
                    _colors[i] = Palette.Vertex(tint);
                }
                start = _sectorEnds[sector];
            }
            _mesh.SetColors(_colors);
        }

        private void HideWarning()
        {
            if (_warning != null)
            {
                _warning.enabled = false;
            }
        }

        private void HideEffect()
        {
            _effectRemaining = 0f;
            if (_effect != null)
            {
                _effect.enabled = false;
            }
        }

        private void TintStone()
        {
            // Light, desaturated stone keeps the silhouette distinct against the Dunes sand.
            var tint = new MaterialPropertyBlock();
            tint.SetColor(_baseColor, new Color(0.88f, 0.77f, 0.65f));
            foreach (MeshRenderer stone in _animator.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (stone.sharedMaterial == _sandstone)
                {
                    stone.SetPropertyBlock(tint);
                }
            }
        }

        private void StopCue()
        {
            if (_audio != null)
            {
                _audio.Stop();
                _audio.clip = null;
            }
        }

        private void OnPhase(BossPhaseChanged evt)
        {
            if (IsMine(evt.EnemyId))
            {
                ClearPresentation();
            }
        }

        private void EnsurePlates()
        {
            if (_leftPlate != null)
            {
                return;
            }

            const string shoulders = "Rig_Medium/root/hips/spine/chest/";
            Transform left = _sweepPivot.Find(shoulders + "upperarm.l");
            Transform right = _sweepPivot.Find(shoulders + "upperarm.r");
            if (left != null && right != null)
            {
                _leftPlate = new ColossusPlateVisual(left, _sweepPivot, -1f, _sandstone);
                _rightPlate = new ColossusPlateVisual(right, _sweepPivot, 1f, _sandstone);
            }
        }

        private void OnPlateHit(ColossusPlateHit evt)
        {
            if (IsMine(evt.BossId))
            {
                ColossusPlateVisual plate = evt.Shoulder == ColossusShoulder.Left ? _leftPlate : _rightPlate;
                plate?.Hit(evt.RemainingFraction);
            }
        }

        private void OnRepairStarted(BossBeatStarted evt)
        {
            if (IsMine(evt.EnemyId))
            {
                _repairSeconds = evt.Seconds;
                _repairElapsed = 0f;
                _leftPlate?.BeginRepair();
                _rightPlate?.BeginRepair();
            }
        }

        private void OnRepairFinished(BossBeatEnded evt)
        {
            if (IsMine(evt.EnemyId))
            {
                _repairSeconds = 0f;
                _leftPlate?.Reset();
                _rightPlate?.Reset();
            }
        }

        private void OnDeath(EnemyDied evt)
        {
            if (IsMine(evt.Id))
            {
                ClearPresentation();
                _repairSeconds = 0f;
                _leftPlate?.CancelRepair();
                _rightPlate?.CancelRepair();
                _dead = true;
            }
        }

        private void ClearPresentation()
        {
            HideWarning();
            HideEffect();
            StopCue();
            _committed = _telling = _finalImpact = false;
            _flashRemaining = _recoveryRemaining = _settleRemaining = 0f;
            if (_animator != null)
            {
                _animator.transform.rotation = _visualHeading;
                _sweepPivot.localRotation = Quaternion.identity;
                Play("Idle", 0f, 1f);
            }
        }

        private void DisposeSubscriptions()
        {
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription?.Dispose();
            }
        }

        private void OnDestroy()
        {
            DisposeSubscriptions();
            _leftPlate?.Dispose();
            _rightPlate?.Dispose();
            if (_warning != null)
            {
                Release(_warning.gameObject);
            }
            if (_mesh != null)
            {
                Release(_mesh);
            }
            if (_effectMesh != null)
            {
                Release(_effectMesh);
            }
        }

        private void Release(UnityEngine.Object resource)
        {
            if (Application.isPlaying)
            {
                Destroy(resource);
            }
            else
            {
                DestroyImmediate(resource);
            }
        }
    }
}
