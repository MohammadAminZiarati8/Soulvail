using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

// Block namespace, deliberately: a MonoBehaviour in a file-scoped namespace loads as null on any
// asset that references it (Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// Draws the jackal priest's mend: a lime beam from the staff's sun disc to the ally core is
    /// healing, and the glow on the disc while it lasts. It decides nothing — core says when the mend
    /// starts and why it ends; this follows the ally and fades or flickers the beam accordingly.
    /// </summary>
    /// <remarks>
    /// Pool deactivation and death turn everything off. The shared beam material is never written:
    /// every colour goes through a <see cref="MaterialPropertyBlock"/> per renderer, and the
    /// scrolling highlight is a reused <see cref="Gradient"/>, so a frame allocates nothing.
    /// </remarks>
    public sealed class PriestMendView : MonoBehaviour
    {
        private const float FadeSeconds = 0.2f;
        private const float FlickerSeconds = 0.15f;
        private const int FlickerSegments = 4;
        private const float PulseSpeed = 1.5f;
        private const float PulseHalfWidth = 0.12f;
        private const float BaseAlpha = 0.55f;
        private const float DiscGlow = 0.8f;

        [SerializeField] private LineRenderer _beam;
        [SerializeField] private Transform _glow;
        [SerializeField] private Renderer _disc;
        [SerializeField] private float _chestHeight = 0.9f;

        private readonly GradientAlphaKey[] _alpha = new GradientAlphaKey[5];
        private readonly GradientColorKey[] _colour = new GradientColorKey[2];
        private readonly Gradient _gradient = new Gradient();
        private MaterialPropertyBlock _beamBlock;
        private MaterialPropertyBlock _discBlock;
        private DomainEventHub _hub;
        private EnemyViews _enemies;
        private EnemyView _body;
        private IDisposable _started;
        private IDisposable _ended;
        private IDisposable _died;
        private int _boundId = EnemyView.Unbound;
        private int _diedId = EnemyView.Unbound;
        private int _allyId;
        private Vector3 _end;
        private float _pulse;
        private float _phaseElapsed;
        private BeamPhase _phase;

        private enum BeamPhase { Off, On, Fading, Flickering }

        /// <summary>True while the beam is drawn, flickers included.</summary>
        public bool BeamVisible => _beam != null && _beam.enabled;

        /// <summary>1 while the mend holds, falling to 0 as the beam fades out.</summary>
        public float BeamIntensity { get; private set; }

        /// <summary>True while the staff's disc is lit.</summary>
        public bool DiscLit { get; private set; }

        private int Id => _boundId != EnemyView.Unbound ? _boundId : _body != null ? _body.Id : EnemyView.Unbound;

        [Inject]
        public void Construct(DomainEventHub hub, EnemyViews enemies)
        {
            _beamBlock ??= new MaterialPropertyBlock();
            _discBlock ??= new MaterialPropertyBlock();
            _body = GetComponent<EnemyView>();
            Unsubscribe();
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            Subscribe();
            _colour[0] = new GradientColorKey(ShamanView.Lime, 0f);
            _colour[1] = new GradientColorKey(ShamanView.Lime, 1f);
            _gradient.SetKeys(_colour, _alpha);
            TurnOff();
        }

        public void Bind(int enemyId, DomainEventHub hub, EnemyViews enemies)
        {
            Construct(hub, enemies);
            _boundId = enemyId;
        }

        public void OnSpawn()
        {
            _diedId = EnemyView.Unbound;
            Subscribe();
        }

        public void OnDespawn()
        {
            TurnOff();
            _boundId = EnemyView.Unbound;
        }

        public void Step(float dt)
        {
            if (_phase == BeamPhase.Off || !(dt > 0f) || !float.IsFinite(dt))
            { return; }

            if (_phase == BeamPhase.On && !FollowAlly())
            {
                BeginEnd(MendEndReason.Lost);
            }
            else if (_phase != BeamPhase.On)
            { FollowAlly(); }

            _phaseElapsed += dt;
            _pulse = (_pulse + dt * PulseSpeed) % 1f;

            switch (_phase)
            {
                case BeamPhase.On:
                    BeamIntensity = 1f;
                    break;
                case BeamPhase.Fading:
                    BeamIntensity = Mathf.Clamp01(1f - _phaseElapsed / FadeSeconds);
                    if (_phaseElapsed >= FadeSeconds)
                    { TurnOff(); return; }
                    break;
                case BeamPhase.Flickering:
                    if (_phaseElapsed >= FlickerSeconds)
                    { TurnOff(); return; }
                    int segment = (int)(_phaseElapsed / (FlickerSeconds / FlickerSegments));
                    _beam.enabled = segment % 2 == 0;
                    BeamIntensity = 1f;
                    break;
            }

            DrawBeam();
        }

        private void Update() => Step(Time.deltaTime);
        private void OnEnable() { Subscribe(); }
        private void OnDisable() { TurnOff(); _boundId = EnemyView.Unbound; }
        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            if (_hub == null || _started != null)
            { return; }
            _started = _hub.Subscribe<PriestMendStarted>(OnStarted);
            _ended = _hub.Subscribe<PriestMendEnded>(OnEnded);
            _died = _hub.Subscribe<EnemyDied>(OnDied);
        }

        private void Unsubscribe()
        {
            _started?.Dispose();
            _ended?.Dispose();
            _died?.Dispose();
            _started = _ended = _died = null;
        }

        private bool IsMine(int id) => isActiveAndEnabled && id != EnemyView.Unbound && id == Id && id != _diedId;

        private void OnStarted(PriestMendStarted evt)
        {
            if (!IsMine(evt.Id))
            { return; }
            _allyId = evt.AllyId;
            _phase = BeamPhase.On;
            _phaseElapsed = 0f;
            _pulse = 0f;
            BeamIntensity = 1f;
            if (!FollowAlly())
            { _end = StartPoint(); }
            _beam.enabled = true;
            DiscLit = true;
            PaintDisc(DiscGlow);
            DrawBeam();
        }

        private void OnEnded(PriestMendEnded evt)
        {
            if (!IsMine(evt.Id) || _phase == BeamPhase.Off)
            { return; }
            BeginEnd(evt.Reason);
        }

        private void OnDied(EnemyDied evt)
        {
            if (evt.Id != Id || Id == EnemyView.Unbound)
            { return; }
            TurnOff();
            _diedId = evt.Id;
        }

        private void BeginEnd(MendEndReason reason)
        {
            _phaseElapsed = 0f;
            bool broken = reason == MendEndReason.Broken;
            _phase = broken ? BeamPhase.Flickering : BeamPhase.Fading;
            DiscLit = false;
            PaintDisc(0f);
            if (broken)
            { _beam.enabled = true; }
        }

        private Vector3 StartPoint() => _glow != null ? _glow.position : transform.position;

        // False when the ally has no body any more; the last known end point stays for the fade.
        private bool FollowAlly()
        {
            if (_enemies == null || !_enemies.TryGet(_allyId, out EnemyView ally) || ally == null)
            { return false; }
            _end = ally.Position + Vector3.up * _chestHeight;
            return true;
        }

        private void DrawBeam()
        {
            if (_beam == null)
            { return; }
            _beam.SetPosition(0, StartPoint());
            _beam.SetPosition(1, _end);
            float a = BeamIntensity;
            _alpha[0] = new GradientAlphaKey(BaseAlpha * a, 0f);
            _alpha[1] = new GradientAlphaKey(BaseAlpha * a, Mathf.Clamp01(_pulse - PulseHalfWidth));
            _alpha[2] = new GradientAlphaKey(a, _pulse);
            _alpha[3] = new GradientAlphaKey(BaseAlpha * a, Mathf.Clamp01(_pulse + PulseHalfWidth));
            _alpha[4] = new GradientAlphaKey(BaseAlpha * a, 1f);
            _gradient.SetKeys(_colour, _alpha);
            _beam.colorGradient = _gradient;
            Color tint = ShamanView.Lime;
            tint.a = a;
            _beam.GetPropertyBlock(_beamBlock);
            _beamBlock.SetColor("_BaseColor", tint);
            _beam.SetPropertyBlock(_beamBlock);
        }

        private void PaintDisc(float strength)
        {
            if (_disc == null)
            { return; }
            _disc.GetPropertyBlock(_discBlock);
            _discBlock.SetColor("_EmissionColor", ShamanView.Lime * strength);
            _disc.SetPropertyBlock(_discBlock);
        }

        private void TurnOff()
        {
            _phase = BeamPhase.Off;
            _phaseElapsed = 0f;
            BeamIntensity = 0f;
            DiscLit = false;
            _allyId = 0;
            if (_beam != null)
            { _beam.enabled = false; }
            if (_discBlock != null)
            { PaintDisc(0f); }
        }
    }
}
