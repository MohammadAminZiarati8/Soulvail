using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — Mossguard_Warlord.prefab references this component, and a
// file-scoped namespace would leave that reference deserialising as null (Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// The Mossguard Warlord's own tells (JG-15f): the guarded front as a ground wedge, the stone daze,
    /// and the shield's shatter. It decides nothing — every state here is an event core published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>On the variant's root, beside <see cref="EnemyView"/></b>, so the wedge (a child) turns with
    /// the facing core sets on the root. Injected by the pool like every component on a body.
    /// </para>
    /// <para>
    /// <b>The guard layer's weight is written every <see cref="Step"/></b> from what is known — zero
    /// while dazed or shattered, one otherwise — rather than once per event, so the animator's own
    /// <c>Rebind</c> on despawn and a fresh rental can never leave it stale.
    /// </para>
    /// <para>
    /// <b>The pool calls only <see cref="EnemyView"/>'s <see cref="IPoolable"/></b>, so the restore runs
    /// from <c>OnDisable</c>, which the pool's deactivation raises on the same line.
    /// </para>
    /// </remarks>
    public sealed class WarlordView : MonoBehaviour, IPoolable
    {
        /// <summary>How long the wedge takes to reach a new half-angle.</summary>
        public const float WedgeEaseSeconds = 0.15f;

        public const float WedgeAlpha = 0.35f;

        /// <summary>The wedge's radius, in the root's metres — the shield's reach, not the sweep's.</summary>
        public const float WedgeRadius = 1.8f;

        public const int MoteCount = 3;

        private const int WedgeRays = 25;
        private const float WedgeHeight = 0.03f;
        private const float MoteSpinDegPerSecond = 300f;

        [Tooltip("The model's Animator, on AC_MossguardWarlord.")]
        [SerializeField] private Animator _animator;

        [Tooltip("The big shield on handslot.l; hidden on the shatter.")]
        [SerializeField] private GameObject _shield;

        [Tooltip("The ground wedge's MeshFilter, a child of the root at the feet.")]
        [SerializeField] private MeshFilter _wedge;

        [Tooltip("VFX_ShieldShatter's instance under the root: shards with the dust as a child.")]
        [SerializeField] private ParticleSystem _shatter;

        [Tooltip("The dust puff — the shatter's Dust child — played alone at a daze.")]
        [SerializeField] private ParticleSystem _dust;

        [Tooltip("The three bone-white motes over the head, shown through a daze.")]
        [SerializeField] private Transform _motes;

        private readonly int _dazeId = Animator.StringToHash("Daze");
        private readonly int _dazedId = Animator.StringToHash("Dazed");

        private readonly Vector3[] _vertices = new Vector3[WedgeRays + 1];
        private readonly Color[] _colours = new Color[WedgeRays + 1];

        private EnemyView _body;
        private IDisposable _arcSubscription;
        private IDisposable _dazedSubscription;
        private IDisposable _shatteredSubscription;
        private IDisposable _diedSubscription;
        private Mesh _mesh;
        private MeshRenderer _wedgeRenderer;
        private float _targetArc;
        private float _shownArc;
        private float _drawnArc = -1f;
        private float _easeRate;
        private float _dazeLeft;
        private int _guardLayer = -2;

        public float WedgeHalfArcDeg => _shownArc;

        public bool IsDazed => _dazeLeft > 0f;

        public bool IsShattered { get; private set; }

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hub"/> is null.</exception>
        [Inject]
        public void Construct(DomainEventHub hub)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            Unsubscribe();
            _arcSubscription = hub.Subscribe<WarlordGuardArc>(OnGuardArc);
            _dazedSubscription = hub.Subscribe<WarlordDazed>(OnDazed);
            _shatteredSubscription = hub.Subscribe<WarlordShattered>(OnShattered);
            _diedSubscription = hub.Subscribe<EnemyDied>(OnDied);
        }

        /// <summary>Eases the wedge, counts the daze down, spins the motes and weighs the guard layer.</summary>
        /// <remarks>Public for EditMode fixtures; a zero, negative or non-finite step does nothing.</remarks>
        public void Step(float dt)
        {
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            _shownArc = Mathf.MoveTowards(_shownArc, _targetArc, _easeRate * dt);
            DrawWedge();

            if (_dazeLeft > 0f)
            {
                _dazeLeft -= dt;

                if (_dazeLeft > 0f)
                {
                    if (_motes != null)
                    {
                        _motes.Rotate(0f, MoteSpinDegPerSecond * dt, 0f, Space.Self);
                    }
                }
                else
                {
                    EndDaze();
                }
            }

            WeighGuard();
        }

        public void OnSpawn()
        {
        }

        /// <summary>Puts back everything a life changed: shield, wedge, daze, motes and guard layer.</summary>
        public void OnDespawn()
        {
            IsShattered = false;
            _targetArc = 0f;
            _shownArc = 0f;
            _easeRate = 0f;
            DrawWedge();
            EndDaze();

            if (_shield != null)
            {
                _shield.SetActive(true);
            }

            if (_shatter != null)
            {
                _shatter.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (_dust != null)
            {
                _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            WeighGuard();
        }

        private void Awake()
        {
            _body = GetComponent<EnemyView>();

            if (_motes != null)
            {
                _motes.gameObject.SetActive(false);
            }

            DrawWedge();
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        private void OnDisable()
        {
            OnDespawn();
        }

        private void OnDestroy()
        {
            Unsubscribe();

            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        private void Unsubscribe()
        {
            _arcSubscription?.Dispose();
            _dazedSubscription?.Dispose();
            _shatteredSubscription?.Dispose();
            _diedSubscription?.Dispose();
            _arcSubscription = null;
            _dazedSubscription = null;
            _shatteredSubscription = null;
            _diedSubscription = null;
        }

        private bool IsMine(int id)
        {
            if (_body == null)
            {
                _body = GetComponent<EnemyView>();
            }

            return _body != null && _body.IsBound && id == _body.Id;
        }

        private void OnGuardArc(WarlordGuardArc evt)
        {
            if (!IsMine(evt.BossId))
            {
                return;
            }

            _targetArc = Mathf.Max(0f, evt.HalfArcDeg);
            _easeRate = Mathf.Abs(_targetArc - _shownArc) / WedgeEaseSeconds;
        }

        private void OnDazed(WarlordDazed evt)
        {
            if (!IsMine(evt.BossId) || !(evt.Seconds > 0f))
            {
                return;
            }

            _dazeLeft = evt.Seconds;

            if (_animator != null && _animator.isActiveAndEnabled)
            {
                _animator.SetBool(_dazedId, true);
                _animator.SetTrigger(_dazeId);
            }

            if (_dust != null)
            {
                Vector3 at = evt.At.ToUnity();
                _dust.transform.position = new Vector3(at.x, transform.position.y, at.z);
                _dust.Play(false);
            }

            if (_motes != null)
            {
                _motes.gameObject.SetActive(true);
            }

            WeighGuard();
        }

        private void OnShattered(WarlordShattered evt)
        {
            if (IsShattered || !IsMine(evt.BossId))
            {
                return;
            }

            IsShattered = true;

            if (_shatter != null)
            {
                _shatter.transform.position = _shield != null ? _shield.transform.position : transform.position;

                if (_dust != null)
                {
                    _dust.transform.localPosition = Vector3.zero;
                }

                _shatter.Play(true);
            }

            if (_shield != null)
            {
                _shield.SetActive(false);
            }

            WeighGuard();
        }

        private void OnDied(EnemyDied evt)
        {
            if (!IsMine(evt.Id))
            {
                return;
            }

            _targetArc = 0f;
            _shownArc = 0f;
            DrawWedge();
            EndDaze();
        }

        private void EndDaze()
        {
            _dazeLeft = 0f;

            if (_animator != null && _animator.isActiveAndEnabled)
            {
                _animator.SetBool(_dazedId, false);
                _animator.ResetTrigger(_dazeId);
            }

            if (_motes != null)
            {
                _motes.gameObject.SetActive(false);
            }
        }

        private void WeighGuard()
        {
            if (_animator == null || !_animator.isInitialized)
            {
                return;
            }

            if (_guardLayer == -2)
            {
                _guardLayer = _animator.GetLayerIndex("Guard");
            }

            if (_guardLayer >= 0)
            {
                _animator.SetLayerWeight(_guardLayer, IsShattered || IsDazed ? 0f : 1f);
            }
        }

        private void DrawWedge()
        {
            if (_wedge == null)
            {
                return;
            }

            if (_wedgeRenderer == null)
            {
                _wedgeRenderer = _wedge.GetComponent<MeshRenderer>();
            }

            bool shown = _shownArc > 0f;

            if (_wedgeRenderer != null)
            {
                _wedgeRenderer.enabled = shown;
            }

            if (!shown || Mathf.Approximately(_drawnArc, _shownArc))
            {
                return;
            }

            EnsureMesh();
            _drawnArc = _shownArc;

            for (int i = 0; i < WedgeRays; i++)
            {
                float deg = Mathf.Lerp(-_shownArc, _shownArc, i / (float)(WedgeRays - 1));
                float rad = deg * Mathf.Deg2Rad;
                _vertices[i + 1] = new Vector3(Mathf.Sin(rad) * WedgeRadius, WedgeHeight, Mathf.Cos(rad) * WedgeRadius);
            }

            _mesh.vertices = _vertices;
            _mesh.RecalculateBounds();
        }

        private void EnsureMesh()
        {
            if (_mesh != null)
            {
                return;
            }

            _mesh = new Mesh { name = "Warlord guard wedge (runtime)" };
            _mesh.MarkDynamic();

            Color colour = Palette.Vertex(Palette.Neutral);
            colour.a = WedgeAlpha;

            for (int i = 0; i < _colours.Length; i++)
            {
                _colours[i] = colour;
            }

            _vertices[0] = new Vector3(0f, WedgeHeight, 0f);
            _mesh.vertices = _vertices;
            _mesh.colors = _colours;

            var triangles = new int[(WedgeRays - 1) * 3];

            for (int i = 0; i < WedgeRays - 1; i++)
            {
                triangles[i * 3] = 0;
                triangles[(i * 3) + 1] = i + 1;
                triangles[(i * 3) + 2] = i + 2;
            }

            _mesh.triangles = triangles;
            _wedge.sharedMesh = _mesh;
        }
    }
}
