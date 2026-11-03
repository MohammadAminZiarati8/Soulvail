using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — Thornheart.prefab references this component, and a file-scoped
// namespace would leave that reference deserialising as null (Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// Thornheart's own tells (JG-20g): the heart that glows only while it takes double damage, a
    /// ripening ring under every pod, and the thorn band closing in. It decides nothing — every state
    /// here is an event core published. Its moves, flinch and fall are <see cref="EnemyAnimatorView"/>'s,
    /// on the model, through <c>AC_Thornheart</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rings live under the body, not the scene</b>, so a pooled body owns them for its life and
    /// nothing outlives it. Idle ones wait under a deactivated <c>Pool</c> child (AR §18.2); shown ones
    /// sit under an active <c>Live</c> child and are set back flat, at their world point, every
    /// <see cref="Step"/>, because core turns the root to face the player.
    /// </para>
    /// <para>
    /// <b>The band's inner edge is the core radius exactly</b> — standing outside it bites, so the
    /// bramble is drawn on the side that hurts and its edge is where the hurting starts.
    /// </para>
    /// <para>
    /// <b>Everything outside the band is tinted Danger</b> (JG-20j), faintly, out to
    /// <see cref="ZoneReach"/>: the whole area that hurts reads as hurting, not just its edge. Each
    /// <c>StrangleBite</c> flashes it brighter, so a bite from nothing in particular is drawn.
    /// </para>
    /// <para>
    /// <b>Nothing is drawn past the standing arena's floor</b>: every spoke stops at the NavMesh's
    /// bounds, so the tint ends at the walls and a band behind a wall is not drawn at all. With no
    /// arena standing (an EditMode body) the tint runs out to <see cref="ZoneReach"/>.
    /// </para>
    /// <para>
    /// <b>The pool calls only <see cref="EnemyView"/>'s <see cref="IPoolable"/></b>, so the restore runs
    /// from <c>OnDisable</c>, which the pool's deactivation raises on the same line.
    /// </para>
    /// </remarks>
    public sealed class ThornheartView : MonoBehaviour, IPoolable
    {
        /// <summary>How far out the bramble band reaches past the core radius, in metres.</summary>
        public const float BandWidth = 0.8f;

        /// <summary>How far the danger tint may reach from the boss, in metres; the arena's walls stop it sooner.</summary>
        public const float ZoneReach = 40f;

        /// <summary>The tint's opacity outside the band at rest.</summary>
        public const float ZoneAlpha = 0.14f;

        /// <summary>The tint's opacity the instant a bite lands; it fades back over <see cref="BiteFlashSeconds"/>.</summary>
        public const float BiteAlpha = 0.45f;

        /// <summary>How long a bite's flash takes to fade back to <see cref="ZoneAlpha"/>.</summary>
        public const float BiteFlashSeconds = 0.35f;

        /// <summary>A pod's ripening ring, in metres — the pod's footprint and a little more.</summary>
        public const float RipenRadius = 1.2f;

        public const float RimAlpha = 0.3f;

        public const float FillAlpha = 0.75f;

        /// <summary>The heart's emission strength over the warm colour.</summary>
        public const float HeartIntensity = 2.5f;

        /// <summary>At most this many pods are alive at once (core's own bound).</summary>
        public const int PodCapacity = ThornheartBehaviour.PhaseThreeMaxAdds / ThornheartBehaviour.RootlingsPerPod;

        private const int BandSegments = 64;
        private const float BandHeight = 0.04f;
        private const float RipenHeight = 0.06f;

        [Tooltip("The model's Animator, on AC_Thornheart; Exposed is set here.")]
        [SerializeField] private Animator _animator;

        [Tooltip("The model's Heart part; its emission is lit through a property block.")]
        [SerializeField] private Renderer _heart;

        [Tooltip("FX_StrangleRing: the band's MeshFilter and MeshRenderer on its root.")]
        [SerializeField] private GameObject _ringPrefab;

        [Tooltip("FX_PodRipen: a Rim and a Fill quad that grows over the ripen seconds.")]
        [SerializeField] private GameObject _ripenPrefab;

        private static readonly Quaternion Flat = Quaternion.identity;

        private readonly int _exposedId = Animator.StringToHash("Exposed");
        private readonly int _emissionId = Shader.PropertyToID("_EmissionColor");
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private readonly Color _heartAmber = new Color(1f, 0.6f, 0.16f, 1f);

        private readonly Pod[] _pods = new Pod[PodCapacity];
        // Per spoke of the band: its inner (Danger) edge, the bramble's outer edge, then the tint's
        // start on that same radius and its far edge at ZoneReach.
        private readonly Vector3[] _bandVertices = new Vector3[(BandSegments + 1) * 4];
        private readonly Color[] _bandColours = new Color[(BandSegments + 1) * 4];

        private EnemyView _body;
        private ArenaPool _arenas;
        private IDisposable _heartSubscription;
        private IDisposable _podSubscription;
        private IDisposable _ringSubscription;
        private IDisposable _biteSubscription;
        private IDisposable _diedSubscription;
        private IDisposable _despawnedSubscription;
        private Transform _pool;
        private Transform _live;
        private GameObject _ring;
        private Mesh _bandMesh;
        private MaterialPropertyBlock _properties;
        private float _heartLeft;
        private bool _ringShown;
        private float _ringFrom;
        private float _ringTo;
        private float _ringSeconds;
        private float _ringElapsed;
        private float _drawnRadius = -1f;
        private bool _drawnClipped;
        private Rect _drawnFloor;
        private Vector2 _drawnAt;
        private float _biteLeft;
        private float _zoneAlpha = -1f;

        public bool IsHeartLit => _heartLeft > 0f;

        /// <summary>The band's inner edge in metres from the boss; zero while no ring is shown.</summary>
        public float RingRadiusShown => _ringShown ? CurrentRadius() : 0f;

        /// <summary>
        /// The colour the heart is lit in, before <see cref="HeartIntensity"/>: the amber of its gold ramp
        /// (JG-20f). Its own value, as the Shaman's lime is — <see cref="Palette.Essence"/> is reward gold.
        /// </summary>
        public Color HeartColour => _heartAmber;

        /// <summary>The live ripening rings, for tests.</summary>
        public int RipeningCount
        {
            get
            {
                int count = 0;

                for (int i = 0; i < _pods.Length; i++)
                {
                    if (_pods[i].Ring != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>The band's instance once made, for tests.</summary>
        public GameObject Ring => _ring;

        /// <summary>The danger tint's opacity now: <see cref="ZoneAlpha"/> at rest, higher just after a bite; 0 with no ring.</summary>
        public float ZoneAlphaShown => _ringShown ? ZoneAlphaNow() : 0f;

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="arenas">The run's arenas; the standing one's floor clips the ring. Null draws it unclipped.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hub"/> is null.</exception>
        [Inject]
        public void Construct(DomainEventHub hub, ArenaPool arenas = null)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _arenas = arenas;
            Unsubscribe();
            _heartSubscription = hub.Subscribe<HeartExposed>(OnHeartExposed);
            _podSubscription = hub.Subscribe<ThornPodPlanted>(OnPodPlanted);
            _ringSubscription = hub.Subscribe<StrangleRing>(OnStrangleRing);
            _biteSubscription = hub.Subscribe<StrangleBite>(OnStrangleBite);
            _diedSubscription = hub.Subscribe<EnemyDied>(OnDied);
            _despawnedSubscription = hub.Subscribe<EnemyDespawned>(OnDespawned);
        }

        /// <summary>The ripen fill of the pod with <paramref name="podId"/>, 0–1; −1 if it has no ring.</summary>
        public float RipenFill(int podId)
        {
            for (int i = 0; i < _pods.Length; i++)
            {
                if (_pods[i].Ring != null && _pods[i].Id == podId)
                {
                    return Fill(_pods[i]);
                }
            }

            return -1f;
        }

        /// <summary>The ring under the pod with <paramref name="podId"/>, or null.</summary>
        public GameObject RipenRing(int podId)
        {
            for (int i = 0; i < _pods.Length; i++)
            {
                if (_pods[i].Ring != null && _pods[i].Id == podId)
                {
                    return _pods[i].Ring;
                }
            }

            return null;
        }

        /// <summary>Counts the heart and the pods down, closes the band, and lays the rings flat.</summary>
        /// <remarks>Public for EditMode fixtures; a zero, negative or non-finite step does nothing.</remarks>
        public void Step(float dt)
        {
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            if (_heartLeft > 0f)
            {
                _heartLeft -= dt;

                if (!(_heartLeft > 0f))
                {
                    PutHeartOut();
                }
            }

            for (int i = 0; i < _pods.Length; i++)
            {
                if (_pods[i].Ring == null)
                {
                    continue;
                }

                _pods[i].Elapsed += dt;
                PlacePod(ref _pods[i]);
            }

            if (_ringShown)
            {
                _ringElapsed += dt;
                _biteLeft = Mathf.Max(_biteLeft - dt, 0f);
                PlaceRing();
            }
        }

        public void OnSpawn()
        {
        }

        /// <summary>Puts back everything a life changed: heart, pods, band and the Exposed pose.</summary>
        public void OnDespawn()
        {
            PutHeartOut();
            HideRing();

            for (int i = 0; i < _pods.Length; i++)
            {
                ReturnPod(i);
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

            if (_bandMesh != null)
            {
                Destroy(_bandMesh);
            }
        }

        private void Unsubscribe()
        {
            _heartSubscription?.Dispose();
            _podSubscription?.Dispose();
            _ringSubscription?.Dispose();
            _biteSubscription?.Dispose();
            _diedSubscription?.Dispose();
            _despawnedSubscription?.Dispose();
            _heartSubscription = null;
            _podSubscription = null;
            _ringSubscription = null;
            _biteSubscription = null;
            _diedSubscription = null;
            _despawnedSubscription = null;
        }

        private bool IsMine(int id)
        {
            if (_body == null)
            {
                _body = GetComponent<EnemyView>();
            }

            return _body != null && _body.IsBound && id == _body.Id;
        }

        private void OnHeartExposed(HeartExposed evt)
        {
            if (!IsMine(evt.BossId))
            {
                return;
            }

            if (!evt.Open || !(evt.Seconds > 0f))
            {
                PutHeartOut();
                return;
            }

            _heartLeft = evt.Seconds;
            PaintHeart(HeartColour * HeartIntensity);
            SetExposed(true);
        }

        private void OnPodPlanted(ThornPodPlanted evt)
        {
            if (!IsMine(evt.BossId) || _ripenPrefab == null)
            {
                return;
            }

            int slot = -1;

            for (int i = 0; i < _pods.Length; i++)
            {
                if (_pods[i].Ring != null && _pods[i].Id == evt.PodId)
                {
                    slot = i;
                    break;
                }

                if (slot < 0 && _pods[i].Ring == null)
                {
                    slot = i;
                }
            }

            if (slot < 0)
            {
                Debug.LogWarning($"{nameof(ThornheartView)}: a pod past {PodCapacity} has no ripening ring.", this);
                return;
            }

            ref Pod pod = ref _pods[slot];

            if (pod.Ring == null)
            {
                pod.Ring = Rent(_ripenPrefab, ref pod.Idle);
                pod.Rim = Quad(pod.Ring, "Rim");
                pod.Fill = Quad(pod.Ring, "Fill");
                Paint(pod.Rim, RimAlpha);
                Paint(pod.Fill, FillAlpha);
            }

            pod.Id = evt.PodId;
            pod.At = evt.At.ToUnity();
            pod.Seconds = evt.RipenSeconds;
            pod.Elapsed = 0f;
            PlacePod(ref pod);
        }

        private void OnStrangleRing(StrangleRing evt)
        {
            if (!IsMine(evt.BossId) || _ringPrefab == null)
            {
                return;
            }

            if (_ring == null)
            {
                _ring = Instantiate(_ringPrefab, Pool(), false);
                _ring.name = _ringPrefab.name;
            }

            _ring.transform.SetParent(Live(), false);
            _ringShown = true;
            _ringFrom = Mathf.Max(0f, evt.FromRadius);
            _ringTo = Mathf.Max(0f, evt.ToRadius);
            _ringSeconds = evt.Seconds;
            _ringElapsed = 0f;
            _drawnRadius = -1f;
            _biteLeft = 0f;
            _zoneAlpha = -1f;
            PlaceRing();
        }

        private void OnStrangleBite(StrangleBite evt)
        {
            if (!IsMine(evt.BossId) || !_ringShown)
            {
                return;
            }

            _biteLeft = BiteFlashSeconds;
            PlaceRing();
        }

        private float ZoneAlphaNow() =>
            Mathf.Lerp(ZoneAlpha, BiteAlpha, BiteFlashSeconds > 0f ? _biteLeft / BiteFlashSeconds : 0f);

        private void OnDied(EnemyDied evt)
        {
            if (IsMine(evt.Id))
            {
                PutHeartOut();
                HideRing();
                return;
            }

            DropPod(evt.Id);
        }

        private void OnDespawned(EnemyDespawned evt)
        {
            DropPod(evt.Id);
        }

        private void DropPod(int id)
        {
            for (int i = 0; i < _pods.Length; i++)
            {
                if (_pods[i].Ring != null && _pods[i].Id == id)
                {
                    ReturnPod(i);
                }
            }
        }

        private void ReturnPod(int slot)
        {
            ref Pod pod = ref _pods[slot];

            if (pod.Ring == null)
            {
                return;
            }

            pod.Ring.transform.SetParent(Pool(), false);
            pod.Idle = pod.Ring;
            pod.Ring = null;
            pod.Id = 0;
            pod.Elapsed = 0f;
        }

        private GameObject Rent(GameObject prefab, ref GameObject idle)
        {
            GameObject instance = idle;

            if (instance == null)
            {
                instance = Instantiate(prefab, Pool(), false);
                instance.name = prefab.name;
            }

            idle = null;
            instance.transform.SetParent(Live(), false);
            return instance;
        }

        private static float Fill(in Pod pod) =>
            pod.Seconds > 0f ? Mathf.Clamp01(pod.Elapsed / pod.Seconds) : 1f;

        private void PlacePod(ref Pod pod)
        {
            pod.Ring.transform.SetPositionAndRotation(new Vector3(pod.At.x, transform.position.y + RipenHeight, pod.At.z), Flat);

            if (pod.Rim != null)
            {
                pod.Rim.localScale = new Vector3(RipenRadius * 2f, RipenRadius * 2f, 1f);
            }

            if (pod.Fill != null)
            {
                float diameter = RipenRadius * 2f * Fill(pod);
                pod.Fill.localScale = new Vector3(diameter, diameter, 1f);
            }
        }

        private float CurrentRadius()
        {
            float t = _ringSeconds > 0f ? Mathf.Clamp01(_ringElapsed / _ringSeconds) : 1f;
            return Mathf.Lerp(_ringFrom, _ringTo, t);
        }

        private void PlaceRing()
        {
            Vector3 at = transform.position;
            _ring.transform.SetPositionAndRotation(new Vector3(at.x, at.y + BandHeight, at.z), Flat);

            float radius = CurrentRadius();
            bool clipped = TryFloor(out Rect floor);
            var from = new Vector2(at.x, at.z);

            // The clip depends on where the boss stands, so a clipped band redraws as it walks.
            if (!Mathf.Approximately(radius, _drawnRadius) || clipped != _drawnClipped
                || (clipped && (floor != _drawnFloor || (from - _drawnAt).sqrMagnitude > 1e-4f)))
            {
                _drawnRadius = radius;
                _drawnClipped = clipped;
                _drawnFloor = floor;
                _drawnAt = from;
                DrawBand(radius);
            }

            float alpha = ZoneAlphaNow();

            if (!Mathf.Approximately(alpha, _zoneAlpha))
            {
                _zoneAlpha = alpha;
                PaintZone(alpha);
            }
        }

        private void HideRing()
        {
            _ringShown = false;
            _ringElapsed = 0f;
            _biteLeft = 0f;

            if (_ring != null)
            {
                _ring.transform.SetParent(Pool(), false);
            }
        }

        private void DrawBand(float inner)
        {
            var filter = _ring.GetComponent<MeshFilter>();

            if (filter == null)
            {
                return;
            }

            EnsureBand(filter);
            float outer = inner + BandWidth;

            float reach = Mathf.Max(ZoneReach, outer);

            for (int i = 0; i <= BandSegments; i++)
            {
                float rad = i * Mathf.PI * 2f / BandSegments;
                float x = Mathf.Sin(rad);
                float z = Mathf.Cos(rad);
                int v = i * 4;

                // A spoke whose band lies behind a wall collapses onto the wall: nothing is drawn there.
                float wall = _drawnClipped ? ToWall(x, z) : float.PositiveInfinity;
                float a = Mathf.Min(inner, wall);
                float b = Mathf.Min(outer, wall);
                float c = Mathf.Min(reach, wall);
                _bandVertices[v] = new Vector3(x * a, 0f, z * a);
                _bandVertices[v + 1] = new Vector3(x * b, 0f, z * b);
                _bandVertices[v + 2] = _bandVertices[v + 1];
                _bandVertices[v + 3] = new Vector3(x * c, 0f, z * c);
            }

            _bandMesh.vertices = _bandVertices;
            _bandMesh.RecalculateBounds();
        }

        // The standing arena's floor in world XZ (Rect.y is world z), from its NavMesh's bounds the
        // way OpenGroundSense reads them. Two opposite corners suffice: arenas turn in right angles.
        private bool TryFloor(out Rect floor)
        {
            ArenaView arena = _arenas?.Active;

            if (arena == null || arena.Surface == null || arena.Surface.navMeshData == null)
            {
                floor = default;
                return false;
            }

            Bounds bounds = arena.Surface.navMeshData.sourceBounds;
            Transform surface = arena.Surface.transform;
            Vector3 min = surface.position + (surface.rotation * bounds.min);
            Vector3 max = surface.position + (surface.rotation * bounds.max);
            floor = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.z, max.z), Mathf.Max(min.x, max.x), Mathf.Max(min.z, max.z));
            return true;
        }

        // Metres from the boss to the floor's edge along (x, z); 0 when the boss stands past it.
        private float ToWall(float x, float z)
        {
            float t = float.PositiveInfinity;

            if (x > 1e-5f)
            {
                t = Mathf.Min(t, (_drawnFloor.xMax - _drawnAt.x) / x);
            }
            else if (x < -1e-5f)
            {
                t = Mathf.Min(t, (_drawnFloor.xMin - _drawnAt.x) / x);
            }

            if (z > 1e-5f)
            {
                t = Mathf.Min(t, (_drawnFloor.yMax - _drawnAt.y) / z);
            }
            else if (z < -1e-5f)
            {
                t = Mathf.Min(t, (_drawnFloor.yMin - _drawnAt.y) / z);
            }

            return Mathf.Max(0f, t);
        }

        private void PaintZone(float alpha)
        {
            if (_bandMesh == null)
            {
                return;
            }

            Color zone = Palette.Vertex(new Color(Palette.Danger.r, Palette.Danger.g, Palette.Danger.b, alpha));

            for (int i = 0; i <= BandSegments; i++)
            {
                _bandColours[(i * 4) + 2] = zone;
                _bandColours[(i * 4) + 3] = zone;
            }

            _bandMesh.colors = _bandColours;
        }

        private void EnsureBand(MeshFilter filter)
        {
            if (_bandMesh != null)
            {
                return;
            }

            _bandMesh = new Mesh { name = "Thornheart strangle band (runtime)" };
            _bandMesh.MarkDynamic();

            // The inner edge is the warning, in Danger; the bramble darkens outward; past it the
            // tint, painted by PaintZone.
            Color edge = Palette.Vertex(Palette.Danger);
            Color bramble = Palette.Vertex(Palette.MotherGoo);
            var triangles = new int[BandSegments * 12];

            for (int i = 0; i <= BandSegments; i++)
            {
                _bandColours[i * 4] = edge;
                _bandColours[(i * 4) + 1] = bramble;
            }

            for (int i = 0; i < BandSegments; i++)
            {
                for (int strip = 0; strip < 2; strip++)
                {
                    int a = (i * 4) + (strip * 2);
                    int b = a + 4;
                    int k = (i * 12) + (strip * 6);
                    triangles[k] = a;
                    triangles[k + 1] = a + 1;
                    triangles[k + 2] = b;
                    triangles[k + 3] = b;
                    triangles[k + 4] = a + 1;
                    triangles[k + 5] = b + 1;
                }
            }

            _bandMesh.vertices = _bandVertices;
            _bandMesh.colors = _bandColours;
            _bandMesh.triangles = triangles;
            filter.sharedMesh = _bandMesh;
        }

        private void PutHeartOut()
        {
            _heartLeft = 0f;
            PaintHeart(Color.black);
            SetExposed(false);
        }

        private void PaintHeart(Color emission)
        {
            if (_heart == null)
            {
                return;
            }

            _properties ??= new MaterialPropertyBlock();
            _heart.GetPropertyBlock(_properties);
            _properties.SetColor(_emissionId, emission);
            _heart.SetPropertyBlock(_properties);
        }

        private void Paint(Transform quad, float alpha)
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

        private void SetExposed(bool exposed)
        {
            if (_animator == null || !_animator.isActiveAndEnabled || _animator.runtimeAnimatorController == null)
            {
                return;
            }

            _animator.SetBool(_exposedId, exposed);
        }

        private static Transform Quad(GameObject ring, string name) => ring.transform.Find(name);

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

        private struct Pod
        {
            public GameObject Ring;
            public GameObject Idle;
            public Transform Rim;
            public Transform Fill;
            public int Id;
            public Vector3 At;
            public float Seconds;
            public float Elapsed;
        }
    }
}
