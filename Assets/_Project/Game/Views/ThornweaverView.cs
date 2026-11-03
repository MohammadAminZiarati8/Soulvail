using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>
    /// Draws whose wall it is (JG-24c): the Thornweaver casts on its own telegraph, its crown glows
    /// over the cast, and a moss root crawls from its feet to the wall it armed. Pool deactivation
    /// clears every transient and subscription.
    /// </summary>
    /// <remarks>
    /// The root is attribution, not a warning: the warned strip stays <see cref="ThornWallView"/>'s,
    /// and the root is never drawn in <see cref="Presentation.Palette.Danger"/> (rule 3). The crown is
    /// one swatch of the body's atlas, so it glows as a second skin over the body: the same mesh and
    /// bones, drawn additively through a mask white on that swatch alone. The body's own emission is
    /// <see cref="EnemyHitFeedback"/>'s, and two writers on one block would overwrite each other.
    /// </remarks>
    public sealed class ThornweaverView : MonoBehaviour
    {
        // Over the warned strip (ThornWallView lays it at 0.02) so the root reads on top of it.
        private const float Lift = 0.03f;
        private const int RootPoints = 8;

        [SerializeField] private Animator _animator;
        [SerializeField] private Renderer _crown;
        [SerializeField] private LineRenderer _root;
        [SerializeField] private float _castClipSeconds = 0.6666667f;
        [Min(0.01f)]
        [SerializeField] private float _rootGrowSeconds = 0.25f;
        [Min(0.01f)]
        [SerializeField] private float _crownDropSeconds = 0.25f;
        [Min(0f)]
        [SerializeField] private float _rootWobble = 0.12f;
        [SerializeField] private Color _crownColour = new Color(0.75f, 0.9f, 0.38f, 1f);
        [Min(0f)]
        [SerializeField] private float _crownIntensity = 1.5f;
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private readonly int _castId = Animator.StringToHash("Cast");
        private readonly int _castRateId = Animator.StringToHash("CastRate");
        private readonly Vector3[] _points = new Vector3[RootPoints];
        private MaterialPropertyBlock _block;
        private DomainEventHub _hub;
        private EnemyView _body;
        private IDisposable _telegraphSub;
        private IDisposable _armedSub;
        private IDisposable _roseSub;
        private IDisposable _witheredSub;
        private IDisposable _diedSub;
        private int _boundId = EnemyView.Unbound;
        private int _wallId;
        private Vector3 _rootStart;
        private Vector3 _rootEnd;
        private float _castSeconds;
        private float _castElapsed;
        private float _rootElapsed;
        private bool _casting;
        private bool _dropping;
        private bool _dead;

        /// <summary>0 dark … 1 full.</summary>
        public float CrownGlow { get; private set; }

        /// <summary>Casts played this life.</summary>
        public int CastCount { get; private set; }

        /// <summary>The root is drawn.</summary>
        public bool RootVisible => _root != null && _root.enabled && _wallId != 0;

        /// <summary>Where the grown root ends: the wall's midpoint, lifted.</summary>
        public Vector3 RootEnd => _rootEnd;

        /// <summary>0 … 1 of the root's crawl.</summary>
        public float RootGrowth => Mathf.Clamp01(_rootElapsed / _rootGrowSeconds);

        private int Id => _boundId != EnemyView.Unbound ? _boundId : _body != null ? _body.Id : EnemyView.Unbound;

        [Inject]
        public void Construct(DomainEventHub hub)
        {
            _block ??= new MaterialPropertyBlock();
            _body = GetComponent<EnemyView>();
            Unsubscribe();
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            Subscribe();
            ResetVisuals();
        }

        public void Bind(int enemyId, DomainEventHub hub)
        {
            Construct(hub);
            _boundId = enemyId;
        }

        public void Step(float dt)
        {
            if (!(dt > 0f) || !float.IsFinite(dt))
            { return; }
            if (_casting)
            {
                _castElapsed += dt;
                Glow(Mathf.Clamp01(_castElapsed / _castSeconds));
            }
            else if (_dropping)
            {
                Glow(Mathf.MoveTowards(CrownGlow, 0f, dt / _crownDropSeconds));
                _dropping = CrownGlow > 0f;
            }
            if (_wallId != 0)
            {
                _rootElapsed = Mathf.Min(_rootGrowSeconds, _rootElapsed + dt);
                if (!_dead)
                { _rootStart = Feet(); }
                DrawRoot();
            }
        }

        private void Update() => Step(Time.deltaTime);
        public void OnSpawn() => Subscribe();
        public void OnDespawn() { ResetVisuals(); Unsubscribe(); _boundId = EnemyView.Unbound; }
        private void OnEnable() => Subscribe();
        private void OnDisable() { ResetVisuals(); Unsubscribe(); _boundId = EnemyView.Unbound; }
        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            if (_hub == null || _telegraphSub != null)
            { return; }
            _telegraphSub = _hub.Subscribe<EnemyTelegraph>(OnTelegraph);
            _armedSub = _hub.Subscribe<ThornArmed>(OnArmed);
            _roseSub = _hub.Subscribe<ThornRose>(OnRose);
            _witheredSub = _hub.Subscribe<ThornWithered>(OnWithered);
            _diedSub = _hub.Subscribe<EnemyDied>(OnDied);
        }

        private void Unsubscribe()
        {
            _telegraphSub?.Dispose();
            _armedSub?.Dispose();
            _roseSub?.Dispose();
            _witheredSub?.Dispose();
            _diedSub?.Dispose();
            _telegraphSub = _armedSub = _roseSub = _witheredSub = _diedSub = null;
        }

        private bool IsMine(int id) => !_dead && Id != EnemyView.Unbound && id == Id;

        // Rule 1.
        private void OnTelegraph(EnemyTelegraph evt)
        {
            if (!IsMine(evt.Id))
            { return; }
            _casting = true;
            _dropping = false;
            _castSeconds = Mathf.Max(0.01f, evt.Duration);
            _castElapsed = 0f;
            CastCount++;
            Glow(0f);
            if (_animator != null && _animator.isActiveAndEnabled)
            {
                _animator.SetFloat(_castRateId, _castClipSeconds / _castSeconds);
                _animator.ResetTrigger(_castId);
                _animator.SetTrigger(_castId);
            }
        }

        // Rule 2: the newest wall takes the root; core keeps two at most, and the newest is the news.
        private void OnArmed(ThornArmed evt)
        {
            if (!IsMine(evt.OwnerId))
            { return; }
            _casting = false;
            _dropping = CrownGlow > 0f;
            _wallId = evt.Id;
            _rootElapsed = 0f;
            float half = evt.Length * 0.5f;
            _rootEnd = new Vector3(
                evt.Origin.X + (evt.Direction.X * half),
                evt.Origin.Y + Lift,
                evt.Origin.Z + (evt.Direction.Y * half));
            _rootStart = Feet();
            if (_root != null)
            { _root.enabled = true; }
            DrawRoot();
        }

        // Rule 2's "gone when the wall stands", and rule 4's "fades with its wall": a dead weaver's
        // root waits on the same wall.
        private void OnRose(ThornRose evt) => ClearRootFor(evt.Id);
        private void OnWithered(ThornWithered evt) => ClearRootFor(evt.Id);

        // Rule 4.
        private void OnDied(EnemyDied evt)
        {
            if (!IsMine(evt.Id))
            { return; }
            _dead = true;
            StopGlow();
            if (_animator != null && _animator.isActiveAndEnabled)
            { _animator.ResetTrigger(_castId); }
        }

        private void ClearRootFor(int wallId)
        {
            if (_wallId == 0 || wallId != _wallId)
            { return; }
            ClearRoot();
        }

        private void ClearRoot()
        {
            _wallId = 0;
            _rootElapsed = 0f;
            if (_root != null)
            {
                _root.positionCount = 0;
                _root.enabled = false;
            }
        }

        private Vector3 Feet()
        {
            Vector3 feet = transform.position;
            feet.y += Lift;
            return feet;
        }

        private void DrawRoot()
        {
            if (_root == null)
            { return; }
            Vector3 end = Vector3.Lerp(_rootStart, _rootEnd, RootGrowth);
            Vector3 along = end - _rootStart;
            Vector3 side = new Vector3(-along.z, 0f, along.x);
            if (side.sqrMagnitude > 1e-6f)
            { side.Normalize(); }
            for (int i = 0; i < RootPoints; i++)
            {
                float t = i / (float)(RootPoints - 1);
                // Ends pinned, a slow S between: a root, not a laser.
                float wobble = Mathf.Sin(t * Mathf.PI * 2f) * Mathf.Sin(t * Mathf.PI) * _rootWobble;
                _points[i] = _rootStart + (along * t) + (side * wobble);
            }
            _root.positionCount = RootPoints;
            _root.SetPositions(_points);
        }

        private void StopGlow()
        {
            _casting = false;
            _dropping = false;
            Glow(0f);
        }

        private void Glow(float fraction)
        {
            CrownGlow = fraction;
            if (_crown == null)
            { return; }
            _block ??= new MaterialPropertyBlock();
            _crown.enabled = fraction > 0f;
            _crown.GetPropertyBlock(_block);
            Color glow = _crownColour * (fraction * _crownIntensity);
            glow.a = 1f;
            _block.SetColor(_baseColorId, glow);
            _crown.SetPropertyBlock(_block);
        }

        private void ResetVisuals()
        {
            StopGlow();
            ClearRoot();
            _dead = false;
            CastCount = 0;
            if (_animator != null && _animator.isActiveAndEnabled && _animator.isInitialized)
            {
                _animator.ResetTrigger(_castId);
                _animator.SetFloat(_castRateId, 1f);
            }
        }
    }
}
