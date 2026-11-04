using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — Gravemaw.prefab references this component, and a file-scoped
// namespace would leave that reference deserialising as null (Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// Gravemaw's own tells (JG-25f): the dirt trail while it is under, the ring that fills where it
    /// will come up, the rise and the dive, the maw lit only while it can be hurt, and the cracks a
    /// Frog crawls out of. It decides nothing — every state here is an event core published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Tremor, Spit, the flinch and the fall are <see cref="EnemyAnimatorView"/>'s</b>, through
    /// <c>AC_Gravemaw</c>'s <c>Attack</c> + <c>Move</c> 4 / 5, Thornheart's way; this view sets only the
    /// <c>Rise</c> and <c>Dive</c> triggers. No clip carries an event, so no clip gates a damage frame.
    /// </para>
    /// <para>
    /// <b>Hidden is renderers off, not the model off</b>: the Animator keeps running under the ground,
    /// so its state and parameters survive a burrow. A burrow that arrives while the body is still up
    /// (phase 2's first eruption goes straight back under) plays <c>Dive</c> first and hides after it.
    /// </para>
    /// <para>
    /// <b>The rings live under the body</b> (<see cref="ThornheartView"/>'s way): idle ones under a
    /// deactivated <c>Pool</c> child, shown ones under an active <c>Live</c> child held at world scale
    /// and laid flat at their world point every <see cref="Step"/>, because the body moves and turns.
    /// </para>
    /// <para>
    /// <b>The pool calls only <see cref="EnemyView"/>'s <see cref="IPoolable"/></b>, so the restore runs
    /// from <c>OnDisable</c>, which the pool's deactivation raises on the same line.
    /// </para>
    /// </remarks>
    public sealed class GravemawView : MonoBehaviour, IPoolable
    {
        /// <summary>At most this many eruption rings at once (phase 2 arms its second after the first).</summary>
        public const int RingCapacity = 2;

        /// <summary>At most this many cracks at once: core's own bound on its adds.</summary>
        public const int CrackCapacity = GravemawBehaviour.AddCapacity;

        /// <summary>A crack's warning disc, in metres — a Frog's footprint and a little more.</summary>
        public const float CrackRadius = 0.9f;

        /// <summary>The <c>Dive</c> clip's length: a body still up is hidden this long after it dives.</summary>
        public const float DiveClipSeconds = 0.5f;

        public const float RimAlpha = 0.3f;

        public const float FillAlpha = 0.75f;

        /// <summary>The maw's emission strength over the warm colour while the window is open.</summary>
        public const float MawIntensity = 2.5f;

        /// <summary>The body's faint warm rim while the window is open.</summary>
        public const float BodyIntensity = 0.35f;

        private const float RingHeight = 0.05f;
        private const float CrackHeight = 0.06f;

        [Tooltip("The Model child: its renderers are hidden while Gravemaw is underground.")]
        [SerializeField] private GameObject _model;

        [Tooltip("FX_Gravemaw_Trail under the root: Mound, Spray and Burst children.")]
        [SerializeField] private GameObject _trail;

        [Tooltip("FX_Gravemaw_Ring: a Rim and a Fill quad that grows over the warning seconds.")]
        [SerializeField] private GameObject _ringPrefab;

        [Tooltip("FX_Gravemaw_Crack: a Rim and a Fill quad over the crack's seconds, and Dust.")]
        [SerializeField] private GameObject _crackPrefab;

        [Tooltip("The model's Animator, on AC_Gravemaw; Rise and Dive are set here.")]
        [SerializeField] private Animator _animator;

        [Tooltip("The model's Maw renderer; lit through a property block while the window is open.")]
        [SerializeField] private Renderer _maw;

        [Tooltip("The model's Body renderer; a faint warm rim while the window is open.")]
        [SerializeField] private Renderer _body;

        private static readonly Quaternion Flat = Quaternion.identity;

        private readonly int _riseId = Animator.StringToHash("Rise");
        private readonly int _diveId = Animator.StringToHash("Dive");
        private readonly int _emissionId = Shader.PropertyToID("_EmissionColor");
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private readonly Color _mawAmber = new Color(1f, 0.6f, 0.16f, 1f);

        private readonly Mark[] _rings = new Mark[RingCapacity];
        private readonly Mark[] _cracks = new Mark[CrackCapacity];

        private EnemyView _enemy;
        private CharacterController _controller;
        private CameraShake _shake;
        private bool _shakeLooked;
        private Renderer[] _modelRenderers;
        private GameObject _mound;
        private ParticleSystem _spray;
        private ParticleSystem _burst;
        private IDisposable _burrowedSubscription;
        private IDisposable _ringSubscription;
        private IDisposable _eruptedSubscription;
        private IDisposable _windowSubscription;
        private IDisposable _crackSubscription;
        private IDisposable _diedSubscription;
        private Transform _pool;
        private Transform _live;
        private MaterialPropertyBlock _properties;
        private float _windowLeft;
        private float _diveLeft;
        private bool _modelHidden;
        private bool _dived;

        /// <summary>Underground: from <c>GravemawBurrowed</c> to <c>GravemawErupted</c>.</summary>
        public bool IsBuried { get; private set; }

        /// <summary>JG-25h: whether the body's controller ignores the cover layer — only while buried.</summary>
        public bool PassesCover => _controller != null && (_controller.excludeLayers.value & CoverMask()) != 0;

        /// <summary>Whether the model's renderers are drawn now.</summary>
        public bool IsModelShown => !_modelHidden;

        /// <summary>Whether the dirt trail is following the buried body.</summary>
        public bool IsTrailPlaying { get; private set; }

        /// <summary>Whether the maw is lit: only while the window is open.</summary>
        public bool IsMawLit => _windowLeft > 0f;

        /// <summary>The colour the maw is lit in, before <see cref="MawIntensity"/>; never cyan.</summary>
        public Color MawColour => _mawAmber;

        /// <summary>The rings shown now.</summary>
        public int RingCount => Count(_rings);

        /// <summary>The cracks shown now.</summary>
        public int CrackCount => Count(_cracks);

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
            _burrowedSubscription = hub.Subscribe<GravemawBurrowed>(OnBurrowed);
            _ringSubscription = hub.Subscribe<GravemawRingArmed>(OnRingArmed);
            _eruptedSubscription = hub.Subscribe<GravemawErupted>(OnErupted);
            _windowSubscription = hub.Subscribe<GravemawWindow>(OnWindow);
            _crackSubscription = hub.Subscribe<GravemawCrack>(OnCrack);
            _diedSubscription = hub.Subscribe<EnemyDied>(OnDied);
        }

        /// <summary>
        /// <see cref="Construct(DomainEventHub)"/> with the camera's shake named, for fixtures; the run
        /// finds it on the main camera at the first eruption.
        /// </summary>
        public void Construct(DomainEventHub hub, CameraShake shake)
        {
            Construct(hub);
            _shake = shake;
            _shakeLooked = true;
        }

        /// <summary>The fill, 0–1, of the ring centred nearest <paramref name="at"/>; −1 with no ring.</summary>
        public float RingFill(Vector3 at) => Fill(_rings, Nearest(_rings, at));

        /// <summary>The ring centred nearest <paramref name="at"/>, or null.</summary>
        public GameObject Ring(Vector3 at)
        {
            int slot = Nearest(_rings, at);
            return slot < 0 ? null : _rings[slot].Go;
        }

        /// <summary>The crack opened nearest <paramref name="at"/>, or null.</summary>
        public GameObject Crack(Vector3 at)
        {
            int slot = Nearest(_cracks, at);
            return slot < 0 ? null : _cracks[slot].Go;
        }

        /// <summary>Fills the rings and cracks, counts the maw and the dive down, and lays the marks flat.</summary>
        /// <remarks>Public for EditMode fixtures; a zero, negative or non-finite step does nothing.</remarks>
        public void Step(float dt)
        {
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            if (_windowLeft > 0f)
            {
                _windowLeft -= dt;

                if (!(_windowLeft > 0f))
                {
                    PutMawOut();
                }
            }

            if (_diveLeft > 0f)
            {
                _diveLeft = Mathf.Max(0f, _diveLeft - dt);
            }

            if (IsBuried && !_modelHidden && !(_diveLeft > 0f))
            {
                ShowModel(false);
            }

            for (int i = 0; i < _rings.Length; i++)
            {
                if (_rings[i].Go != null)
                {
                    _rings[i].Elapsed += dt;
                    Place(ref _rings[i], RingHeight);
                }
            }

            for (int i = 0; i < _cracks.Length; i++)
            {
                if (_cracks[i].Go == null)
                {
                    continue;
                }

                _cracks[i].Elapsed += dt;

                if (_cracks[i].Elapsed >= _cracks[i].Seconds)
                {
                    Return(_cracks, i);
                    continue;
                }

                Place(ref _cracks[i], CrackHeight);
            }
        }

        public void OnSpawn()
        {
        }

        /// <summary>Puts back everything a life changed: model, trail, maw, rings, cracks and triggers.</summary>
        public void OnDespawn()
        {
            Bury(false);
            _diveLeft = 0f;
            _dived = false;
            ShowModel(true);
            PlayTrail(false);
            PutMawOut();
            ReturnAll(_rings);
            ReturnAll(_cracks);

            if (_burst != null)
            {
                _burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.ResetTrigger(_riseId);
                _animator.ResetTrigger(_diveId);
            }
        }

        private void LateUpdate()
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
        }

        private void Unsubscribe()
        {
            _burrowedSubscription?.Dispose();
            _ringSubscription?.Dispose();
            _eruptedSubscription?.Dispose();
            _windowSubscription?.Dispose();
            _crackSubscription?.Dispose();
            _diedSubscription?.Dispose();
            _burrowedSubscription = null;
            _ringSubscription = null;
            _eruptedSubscription = null;
            _windowSubscription = null;
            _crackSubscription = null;
            _diedSubscription = null;
        }

        // JG-25h rule 1: under the ground rocks and pillars do not stop the body; other bodies,
        // the player and the arena's walls still do. Up again, cover blocks it as before.
        private void Bury(bool buried)
        {
            IsBuried = buried;

            if (_controller == null)
            {
                _controller = GetComponent<CharacterController>();
            }

            if (_controller == null)
            {
                return;
            }

            int mask = CoverMask();
            int excluded = _controller.excludeLayers.value;
            _controller.excludeLayers = buried ? excluded | mask : excluded & ~mask;
        }

        private static int CoverMask()
        {
            int layer = LayerMask.NameToLayer(ArenaView.CoverLayerName);

            return layer < 0 ? 0 : 1 << layer;
        }

        private bool IsMine(int id)
        {
            if (_enemy == null)
            {
                _enemy = GetComponent<EnemyView>();
            }

            return _enemy != null && _enemy.IsBound && id == _enemy.Id;
        }

        // Rule 1: under the ground the body is the trail; a body still up dives before it hides.
        private void OnBurrowed(GravemawBurrowed evt)
        {
            if (!IsMine(evt.BossId))
            {
                return;
            }

            Bury(true);
            PutMawOut();
            PlayTrail(true);

            if (!_modelHidden && !_dived)
            {
                Dive();
            }

            if (!(_diveLeft > 0f))
            {
                ShowModel(false);
            }
        }

        // Rule 2: a telegraph in Danger at its fixed centre, filling over its warning seconds.
        private void OnRingArmed(GravemawRingArmed evt)
        {
            if (!IsMine(evt.BossId) || _ringPrefab == null)
            {
                return;
            }

            int slot = Free(_rings);

            if (slot < 0)
            {
                Debug.LogWarning($"{nameof(GravemawView)}: a ring past {RingCapacity} is not drawn.", this);
                return;
            }

            Rent(ref _rings[slot], _ringPrefab, evt.At.ToUnity(), Mathf.Max(0f, evt.Radius), evt.Seconds);
        }

        // Rule 3: up at the ring, whether or not the bite landed.
        private void OnErupted(GravemawErupted evt)
        {
            if (!IsMine(evt.BossId))
            {
                return;
            }

            int slot = Nearest(_rings, evt.At.ToUnity());

            if (slot >= 0)
            {
                Return(_rings, slot);
            }

            Bury(false);
            _diveLeft = 0f;
            _dived = false;
            PlayTrail(false);
            ShowModel(true);
            SetTrigger(_riseId);

            if (Burst() != null)
            {
                _burst.Play(true);
            }

            if (!_shakeLooked)
            {
                _shakeLooked = true;
                Camera camera = Camera.main;
                _shake = camera != null ? camera.GetComponent<CameraShake>() : null;
            }

            if (_shake != null)
            {
                _shake.Kick(CameraShake.SlamStrength);
            }
        }

        // Rule 4: lit for the window's seconds; the close dives.
        private void OnWindow(GravemawWindow evt)
        {
            if (!IsMine(evt.BossId))
            {
                return;
            }

            if (!evt.Open || !(evt.Seconds > 0f))
            {
                PutMawOut();

                if (!evt.Open && !IsBuried)
                {
                    Dive();
                }

                return;
            }

            _windowLeft = evt.Seconds;
            Paint(_maw, _mawAmber * MawIntensity);
            Paint(_body, _mawAmber * BodyIntensity);
        }

        // Rule 6: a crack's disc for its seconds, then the Frog is core's.
        private void OnCrack(GravemawCrack evt)
        {
            if (!IsMine(evt.BossId) || _crackPrefab == null || !(evt.Seconds > 0f))
            {
                return;
            }

            int slot = Free(_cracks);

            if (slot < 0)
            {
                Debug.LogWarning($"{nameof(GravemawView)}: a crack past {CrackCapacity} is not drawn.", this);
                return;
            }

            Rent(ref _cracks[slot], _crackPrefab, evt.At.ToUnity(), CrackRadius, evt.Seconds);

            var dust = _cracks[slot].Go.GetComponentInChildren<ParticleSystem>(true);

            if (dust != null)
            {
                dust.Play(true);
            }
        }

        private void OnDied(EnemyDied evt)
        {
            if (!IsMine(evt.Id))
            {
                return;
            }

            Bury(false);
            _diveLeft = 0f;
            _dived = false;
            ShowModel(true);
            PlayTrail(false);
            PutMawOut();
            ReturnAll(_rings);
            ReturnAll(_cracks);
        }

        private void Dive()
        {
            _dived = true;
            _diveLeft = DiveClipSeconds;
            SetTrigger(_diveId);
        }

        private void SetTrigger(int id)
        {
            if (_animator == null || !_animator.isActiveAndEnabled || _animator.runtimeAnimatorController == null)
            {
                return;
            }

            _animator.SetTrigger(id);
        }

        private void ShowModel(bool shown)
        {
            _modelHidden = !shown;

            if (_model == null)
            {
                return;
            }

            _modelRenderers ??= _model.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < _modelRenderers.Length; i++)
            {
                if (_modelRenderers[i] != null)
                {
                    _modelRenderers[i].enabled = shown;
                }
            }
        }

        private void PlayTrail(bool on)
        {
            IsTrailPlaying = on;

            if (_trail == null)
            {
                return;
            }

            if (_mound == null)
            {
                Transform mound = _trail.transform.Find("Mound");
                Transform spray = _trail.transform.Find("Spray");
                _mound = mound != null ? mound.gameObject : null;
                _spray = spray != null ? spray.GetComponent<ParticleSystem>() : null;
            }

            if (_mound != null)
            {
                _mound.SetActive(on);
            }

            if (_spray == null)
            {
                return;
            }

            if (on)
            {
                _spray.Play(true);
            }
            else
            {
                _spray.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private ParticleSystem Burst()
        {
            if (_burst == null && _trail != null)
            {
                Transform burst = _trail.transform.Find("Burst");
                _burst = burst != null ? burst.GetComponent<ParticleSystem>() : null;
            }

            return _burst;
        }

        private void PutMawOut()
        {
            _windowLeft = 0f;
            Paint(_maw, Color.black);
            Paint(_body, Color.black);
        }

        private void Paint(Renderer renderer, Color emission)
        {
            if (renderer == null)
            {
                return;
            }

            _properties ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_properties);
            _properties.SetColor(_emissionId, emission);
            renderer.SetPropertyBlock(_properties);
            _properties.Clear();
        }

        private void Rent(ref Mark mark, GameObject prefab, Vector3 at, float radius, float seconds)
        {
            GameObject instance = mark.Idle;

            if (instance == null)
            {
                instance = Instantiate(prefab, Pool(), false);
                instance.name = prefab.name;
                mark.Rim = instance.transform.Find("Rim");
                mark.Fill = instance.transform.Find("Fill");
                Tint(mark.Rim, RimAlpha);
                Tint(mark.Fill, FillAlpha);
            }

            mark.Idle = null;
            instance.transform.SetParent(Live(), false);
            mark.Go = instance;
            mark.At = at;
            mark.Radius = radius;
            mark.Seconds = seconds;
            mark.Elapsed = 0f;
            Place(ref mark, ReferenceEquals(prefab, _crackPrefab) ? CrackHeight : RingHeight);
        }

        private void Place(ref Mark mark, float height)
        {
            HoldLiveAtWorldScale();
            mark.Go.transform.SetPositionAndRotation(new Vector3(mark.At.x, transform.position.y + height, mark.At.z), Flat);
            float diameter = mark.Radius * 2f;

            if (mark.Rim != null)
            {
                mark.Rim.localScale = new Vector3(diameter, diameter, 1f);
            }

            if (mark.Fill != null)
            {
                float filled = diameter * Fill(mark);
                mark.Fill.localScale = new Vector3(filled, filled, 1f);
            }
        }

        private void Return(Mark[] marks, int slot)
        {
            ref Mark mark = ref marks[slot];

            if (mark.Go == null)
            {
                return;
            }

            mark.Go.transform.SetParent(Pool(), false);
            mark.Idle = mark.Go;
            mark.Go = null;
            mark.Elapsed = 0f;
        }

        private void ReturnAll(Mark[] marks)
        {
            for (int i = 0; i < marks.Length; i++)
            {
                Return(marks, i);
            }
        }

        private void Tint(Transform quad, float alpha)
        {
            Renderer renderer = quad != null ? quad.GetComponent<Renderer>() : null;

            if (renderer == null)
            {
                return;
            }

            _properties ??= new MaterialPropertyBlock();
            _properties.Clear();
            Color danger = Palette.Danger;
            _properties.SetColor(_baseColorId, new Color(danger.r, danger.g, danger.b, alpha));
            renderer.SetPropertyBlock(_properties);
            _properties.Clear();
        }

        private static float Fill(in Mark mark) =>
            mark.Seconds > 0f ? Mathf.Clamp01(mark.Elapsed / mark.Seconds) : 1f;

        private static float Fill(Mark[] marks, int slot) => slot < 0 ? -1f : Fill(marks[slot]);

        private static int Count(Mark[] marks)
        {
            int count = 0;

            for (int i = 0; i < marks.Length; i++)
            {
                if (marks[i].Go != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static int Free(Mark[] marks)
        {
            for (int i = 0; i < marks.Length; i++)
            {
                if (marks[i].Go == null)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int Nearest(Mark[] marks, Vector3 at)
        {
            int best = -1;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < marks.Length; i++)
            {
                if (marks[i].Go == null)
                {
                    continue;
                }

                float x = marks[i].At.x - at.x;
                float z = marks[i].At.z - at.z;
                float distance = (x * x) + (z * z);

                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private Transform Pool()
        {
            if (_pool == null)
            {
                _pool = MakeChild("Pool", false);
            }

            return _pool;
        }

        private Transform Live()
        {
            if (_live == null)
            {
                _live = MakeChild("Live", true);
            }

            return _live;
        }

        // The look scales the body: undo that on Live, so a ring is drawn in the metres core bites in.
        private void HoldLiveAtWorldScale()
        {
            Vector3 body = transform.lossyScale;
            Live().localScale = new Vector3(Inverse(body.x), Inverse(body.y), Inverse(body.z));
        }

        private static float Inverse(float scale) => Mathf.Abs(scale) > 1e-5f ? 1f / scale : 1f;

        private Transform MakeChild(string childName, bool active)
        {
            Transform found = transform.Find(childName);

            if (found != null)
            {
                return found;
            }

            var child = new GameObject(childName);
            child.SetActive(active);
            child.transform.SetParent(transform, false);
            return child.transform;
        }

        private struct Mark
        {
            public GameObject Go;
            public GameObject Idle;
            public Transform Rim;
            public Transform Fill;
            public Vector3 At;
            public float Radius;
            public float Seconds;
            public float Elapsed;
        }
    }
}
