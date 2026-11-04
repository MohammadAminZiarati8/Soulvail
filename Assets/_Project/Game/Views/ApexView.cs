using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>The Apex's event-driven body. Ground warnings remain BossViews/TelegraphRings' own.</summary>
    [RequireComponent(typeof(EnemyView))]
    public sealed class ApexView : MonoBehaviour, IPoolable
    {
        public const float EyeIntensity = 1.8f;
        public const float WindowBrightness = 1.15f;

        [SerializeField] private Animator _animator;
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private Renderer[] _eyes;
        [SerializeField] private ParticleSystem[] _eyeHalos;
        [SerializeField] private Texture2D _eyeGlow;
        [SerializeField] private ParticleSystem _streak;
        [SerializeField] private Transform _tail;

        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private readonly int _emissionId = Shader.PropertyToID("_EmissionColor");
        private readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
        private readonly int _emissionMapId = Shader.PropertyToID("_EmissionMap");
        private readonly int _idleId = Animator.StringToHash("Idle");
        private readonly int _runId = Animator.StringToHash("Run");
        private readonly int _crouchId = Animator.StringToHash("Crouch");
        private readonly int _dashId = Animator.StringToHash("Dash");
        private readonly int _pounceId = Animator.StringToHash("Pounce");
        private readonly int _landId = Animator.StringToHash("Land");
        private readonly int _rakeId = Animator.StringToHash("Rake");
        private readonly int _roarId = Animator.StringToHash("Roar");
        private readonly int _hitId = Animator.StringToHash("Hit");
        private readonly int _deathId = Animator.StringToHash("Death");
        private readonly int _emptyId = Animator.StringToHash("Empty");
        private readonly int _speedId = Animator.StringToHash("Speed");
        private readonly int _windowId = Animator.StringToHash("Window");
        private readonly int _rakeRateId = Animator.StringToHash("RakeRate");

        private EnemyView _body;
        private IDisposable[] _subscriptions;
        private MaterialPropertyBlock _eyeBlock;
        private MaterialPropertyBlock _windowBlock;
        private Quaternion _tailRest;
        private float _modelBaseY;
        private bool _ready;
        private bool _dead;
        private bool _awaitingLane;
        private bool _leapPending;
        private bool _dashing;
        private bool _windowApplied;
        private int _laneId;
        private int _flinchLayer = -1;
        private Vector3 _dashOrigin;
        private Vector3 _dashDirection;
        private float _dashLength;
        private float _takeoffLeft;
        private float _airLeft;
        private float _windowLeft;
        private float _feintLeft;
        private float _feintElapsed;

        public bool EyesLit { get; private set; }
        public bool IsAirborne { get; private set; }
        public bool IsStreakPlaying => _streak != null && _streak.isEmitting;
        public bool InWindow => _windowLeft > 0f;
        public bool IsTailFlicking => _feintLeft > 0f;

        [Inject]
        public void Construct(DomainEventHub hub)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            Prepare();
            Unsubscribe();
            _subscriptions = new[]
            {
                hub.Subscribe<EnemyTelegraph>(OnTelegraph),
                hub.Subscribe<LaneArmed>(OnLaneArmed),
                hub.Subscribe<LaneFired>(OnLaneFired),
                hub.Subscribe<LaneClosed>(OnLaneClosed),
                hub.Subscribe<ApexCrouched>(OnCrouched),
                hub.Subscribe<EnemyLeapAimed>(OnLeapAimed),
                hub.Subscribe<ApexLanded>(OnLanded),
                hub.Subscribe<BossPhaseChanged>(OnPhase),
                hub.Subscribe<EnemyDamaged>(OnDamaged),
                hub.Subscribe<EnemyDied>(OnDied),
            };
        }

        /// <summary>Cosmetic seconds only; public so fixtures can advance paused EditMode bodies.</summary>
        public void Step(float dt)
        {
            if (!(dt > 0f) || !float.IsFinite(dt))
            {
                return;
            }

            Prepare();
            if (_dead || _body == null || !_body.IsBound)
            {
                return;
            }

            Vector3 velocity = _body.Velocity;
            velocity.y = 0f;
            float speed = velocity.magnitude;
            _animator.SetFloat(_speedId, speed);
            FlinchLayer.Drive(_animator, _flinchLayer, _hitId);

            if (_leapPending)
            {
                _takeoffLeft -= dt;
                if (_takeoffLeft <= 0f)
                {
                    _leapPending = false;
                    IsAirborne = true;
                    Play(_pounceId);
                    _airLeft += _takeoffLeft;
                }
            }
            else if (IsAirborne)
            {
                _airLeft -= dt;
            }

            if (IsAirborne && _airLeft <= 0f)
            {
                IsAirborne = false;
            }

            // The lane's lifetime is not the dash's. Core's body velocity and the stone-cut end
            // stop the streak even while an old lane remains visible during a Hunt chain.
            if (_dashing && (speed <= 0.5f ||
                Vector3.Dot(transform.position - _dashOrigin, _dashDirection) >= _dashLength - 0.1f))
            {
                _dashing = false;
                Play(speed > 0.5f ? _runId : _idleId);
            }

            if (_feintLeft > 0f)
            {
                _feintLeft = Mathf.Max(0f, _feintLeft - dt);
                _feintElapsed += dt;
                if (_tail != null)
                {
                    _tail.localRotation = _tailRest * Quaternion.Euler(0f, 0f,
                        _feintLeft > 0f ? Mathf.Sin(_feintElapsed * 22f) * 32f : 0f);
                }
            }

            if (_windowLeft > 0f)
            {
                _windowLeft = Mathf.Max(0f, _windowLeft - dt);
                _animator.SetBool(_windowId, InWindow);
            }

            DrawWindow();
            SetStreak(_dashing || IsAirborne);
        }

        public void OnSpawn() { }

        public void OnDespawn()
        {
            Prepare();
            ClearMove();
            _dead = false;
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                if (_animator.isActiveAndEnabled)
                {
                    _animator.Rebind();
                }

                _animator.SetFloat(_speedId, 0f);
                _animator.SetFloat(_rakeRateId, 1f);
                _animator.SetBool(_windowId, false);
                if (_flinchLayer >= 0)
                {
                    _animator.SetLayerWeight(_flinchLayer, 0f);
                }
            }
        }

        private void Awake() => Prepare();
        private void LateUpdate() => Step(Time.deltaTime);
        private void OnDisable() => OnDespawn();
        private void OnDestroy() => Unsubscribe();

        private void Prepare()
        {
            if (_ready)
            {
                return;
            }

            _body = GetComponent<EnemyView>();
            _eyeBlock = new MaterialPropertyBlock();
            _windowBlock = new MaterialPropertyBlock();
            _tailRest = _tail != null ? _tail.localRotation : Quaternion.identity;
            _modelBaseY = _animator != null ? _animator.transform.localPosition.y : 0f;
            _flinchLayer = _animator != null ? _animator.GetLayerIndex(FlinchLayer.Name) : -1;
            _ready = true;
            SetEyes(false);
            SetStreak(false, true);
        }

        private bool IsMine(int id) => isActiveAndEnabled && _body != null && _body.IsBound && _body.Id == id;

        private void OnTelegraph(EnemyTelegraph evt)
        {
            if (_dead || !IsMine(evt.Id))
            {
                return;
            }

            if (evt.Move == 1)
            {
                ClearMove();
                _awaitingLane = true;
                _dashOrigin = transform.position;
                Play(_crouchId);
            }
            else if (evt.Move == 2)
            {
                ClearMove();
                _animator.SetFloat(_rakeRateId, evt.Duration > 0f ? 1f / evt.Duration : 1f);
                Play(_rakeId);
            }
        }

        private void OnLaneArmed(LaneArmed evt)
        {
            // Lane ids are not enemy ids. An announced dash claims only the non-tongue lane at
            // its captured origin, then follows that lane's id, including overlapping old lanes.
            Vector3 origin = new Vector3(evt.Origin.X, evt.Origin.Y, evt.Origin.Z);
            if (!_awaitingLane || _dead || !_body.IsBound || evt.Tongue ||
                (origin - _dashOrigin).sqrMagnitude > 0.09f)
            {
                return;
            }

            _awaitingLane = false;
            _laneId = evt.Id;
            _dashOrigin = origin;
            _dashDirection = new Vector3(evt.Direction.X, 0f, evt.Direction.Y);
            _dashLength = evt.Length;
            Play(_crouchId);
        }

        private void OnLaneFired(LaneFired evt)
        {
            if (_dead || _laneId == 0 || evt.Id != _laneId || !_body.IsBound)
            {
                return;
            }

            _dashing = true;
            Play(_dashId);
            SetStreak(true);
        }

        private void OnLaneClosed(LaneClosed evt)
        {
            if (evt.Id == _laneId)
            {
                _laneId = 0;
            }
        }

        private void OnCrouched(ApexCrouched evt)
        {
            if (_dead || !IsMine(evt.Id))
            {
                return;
            }

            ClearMove();
            Play(_crouchId);
            SetEyes(!evt.IsFeint);
            _feintLeft = evt.IsFeint && float.IsFinite(evt.Seconds) ? Mathf.Max(0f, evt.Seconds) : 0f;
        }

        private void OnLeapAimed(EnemyLeapAimed evt)
        {
            if (_dead || !IsMine(evt.Id) || !(evt.AirSeconds > 0f) || !float.IsFinite(evt.AirSeconds))
            {
                return;
            }

            _takeoffLeft = Mathf.Max(0f, evt.CrouchSeconds);
            _airLeft = evt.AirSeconds;
            _leapPending = true;
            _body.Hop(_animator.transform, _takeoffLeft, _airLeft);
        }

        private void OnLanded(ApexLanded evt)
        {
            if (_dead || !IsMine(evt.Id))
            {
                return;
            }

            ClearMove();
            _windowLeft = float.IsFinite(evt.WindowSeconds) ? Mathf.Max(0f, evt.WindowSeconds) : 0f;
            _animator.SetBool(_windowId, InWindow);
            Play(_landId);
            DrawWindow();
        }

        private void OnPhase(BossPhaseChanged evt)
        {
            if (_dead || !IsMine(evt.EnemyId))
            {
                return;
            }

            ClearMove();
            Play(_roarId);
        }

        private void OnDamaged(EnemyDamaged evt)
        {
            if (!_dead && IsMine(evt.Id) && !evt.Killed && !evt.Blocked && _flinchLayer >= 0)
            {
                _animator.Play(_hitId, _flinchLayer, 0f);
                _animator.SetLayerWeight(_flinchLayer, 1f);
            }
        }

        private void OnDied(EnemyDied evt)
        {
            if (!IsMine(evt.Id))
            {
                return;
            }

            ClearMove();
            _dead = true;
            Play(_deathId);
        }

        private void ClearMove()
        {
            _awaitingLane = false;
            _laneId = 0;
            _leapPending = false;
            IsAirborne = false;
            _dashing = false;
            _takeoffLeft = 0f;
            _airLeft = 0f;
            _windowLeft = 0f;
            _feintLeft = 0f;
            _feintElapsed = 0f;
            if (_tail != null)
            {
                _tail.localRotation = _tailRest;
            }

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.SetBool(_windowId, false);
                if (_flinchLayer >= 0)
                {
                    if (_animator.isActiveAndEnabled)
                    {
                        _animator.Play(_emptyId, _flinchLayer, 0f);
                    }
                    _animator.SetLayerWeight(_flinchLayer, 0f);
                }

                // Cancel EnemyView's cosmetic hop as well on death, phase interruption and pooling.
                _body.Hop(_animator.transform, 0f, float.Epsilon);
                _body.StepHop(float.Epsilon);
                Vector3 position = _animator.transform.localPosition;
                position.y = _modelBaseY;
                _animator.transform.localPosition = position;
            }

            SetEyes(false);
            SetStreak(false, true);
            DrawWindow();
        }

        private void Play(int state)
        {
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.Play(state, 0, 0f);
            }
        }

        private void SetEyes(bool lit)
        {
            EyesLit = lit;
            if (_eyes == null)
            {
                return;
            }

            _eyeBlock.Clear();
            _eyeBlock.SetTexture(_baseMapId, Texture2D.whiteTexture);
            _eyeBlock.SetTexture(_emissionMapId, Texture2D.whiteTexture);
            _eyeBlock.SetColor(_baseColorId, Color.black);
            _eyeBlock.SetColor(_emissionId, Palette.Vertex(Palette.Danger) * EyeIntensity);
            for (int i = 0; i < _eyes.Length; i++)
            {
                if (_eyes[i] != null)
                {
                    _eyes[i].SetPropertyBlock(_eyeBlock);
                    _eyes[i].enabled = lit;
                }
            }
            // Run's camera has bloom disabled. Two local soft billboards make the tell bloom
            // without changing the camera, a scene, a shared material or the player's effects.
            if (_eyeHalos != null)
            {
                _eyeBlock.Clear();
                _eyeBlock.SetTexture(_baseMapId, _eyeGlow);
                Color halo = Palette.Vertex(Palette.Danger);
                halo.a = 0.6f;
                _eyeBlock.SetColor(_baseColorId, halo);
                for (int i = 0; i < _eyeHalos.Length; i++)
                {
                    ParticleSystem glow = _eyeHalos[i];
                    if (glow == null)
                    { continue; }
                    glow.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(_eyeBlock);
                    glow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    if (lit)
                    {
                        glow.Play(true);
                        glow.Emit(1);
                    }
                }
            }
        }

        private void SetStreak(bool playing, bool clear = false)
        {
            if (_streak == null)
            {
                return;
            }

            if (playing)
            {
                if (!_streak.isEmitting)
                {
                    _streak.Play(true);
                }
            }
            else if (_streak.isEmitting || clear)
            {
                _streak.Stop(true, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void DrawWindow()
        {
            if (_bodyRenderer == null)
            {
                return;
            }

            if (InWindow)
            {
                // EnemyHitFeedback owns the renderer-wide flash/tint/dissolve. Copy its current
                // block into the material slot, so a flash during the window stays visible too.
                _bodyRenderer.GetPropertyBlock(_windowBlock);
                Color colour = _windowBlock.HasColor(_baseColorId)
                    ? _windowBlock.GetColor(_baseColorId) : _bodyRenderer.sharedMaterial.GetColor(_baseColorId);
                _windowBlock.SetColor(_baseColorId,
                    new Color(colour.r * WindowBrightness, colour.g * WindowBrightness, colour.b * WindowBrightness, colour.a));
                _bodyRenderer.SetPropertyBlock(_windowBlock, 0);
                _windowApplied = true;
            }
            else if (_windowApplied)
            {
                _bodyRenderer.SetPropertyBlock(null, 0);
                _windowApplied = false;
            }
        }

        private void Unsubscribe()
        {
            if (_subscriptions == null)
            {
                return;
            }

            for (int i = 0; i < _subscriptions.Length; i++)
            {
                _subscriptions[i].Dispose();
            }

            _subscriptions = null;
        }
    }
}
